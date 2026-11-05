using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Client.VR
{
    /// <summary>
    /// Comfort effects drawn last in each eye:
    ///  - the <b>vignette</b>: a ring that is clear in the middle and black at the edge, centred on the eye's
    ///    forward direction and sized in degrees, so it is round whatever the headset's (asymmetric) field of view;
    ///  - the <b>blink</b>: the whole view faded towards black.
    /// </summary>
    public class VRComfortOverlay : IDisposable
    {
        private const int Segments = 48;
        private const float ClearDegrees = 72f;   // no vignette: the clear area reaches past any headset's view
        private const float NarrowestDegrees = 22f; // strongest vignette: about a 45 degree clear circle
        private const float BandDegrees = 16f;    // width of the soft edge
        private const float OuterDegrees = 86f;

        private readonly BasicEffect Effect;
        private readonly VertexPositionColor[] Verts = new VertexPositionColor[Segments * 12 + 6];

        public VRComfortOverlay(GraphicsDevice device)
        {
            Effect = new BasicEffect(device) { VertexColorEnabled = true, LightingEnabled = false, TextureEnabled = false };
        }

        /// <param name="projection">The eye's projection matrix (the overlay is drawn in view space).</param>
        /// <param name="near">The eye's near plane distance, in the same units.</param>
        /// <param name="vignette">0 = none, 1 = strongest.</param>
        /// <param name="fade">0 = clear, 1 = black.</param>
        public void Draw(GraphicsDevice device, Matrix projection, float near, float vignette, float fade)
        {
            int count = 0;
            if (vignette > 0.003f && fade < 0.999f)
            {
                var inner = MathHelper.Lerp(ClearDegrees, NarrowestDegrees, MathHelper.Clamp(vignette, 0, 1));
                var mid = inner + BandDegrees;
                var depth = Math.Max(near, 1e-3f) * 2f;
                var clear = new Color(0, 0, 0, 0);
                var black = new Color(0, 0, 0, 255);
                count = AddRing(count, inner, mid, depth, clear, black);
                count = AddRing(count, mid, Math.Max(mid + 1, OuterDegrees), depth, black, black);
            }

            if (count > 0)
            {
                Effect.World = Matrix.Identity;
                Effect.View = Matrix.Identity;
                Effect.Projection = projection;
                Submit(device, count);
            }

            if (fade > 0.001f)
            {
                // straight in clip space, covering everything
                var colour = new Color(0, 0, 0, (int)(MathHelper.Clamp(fade, 0, 1) * 255));
                Verts[0] = new VertexPositionColor(new Vector3(-1, -1, 0.5f), colour);
                Verts[1] = new VertexPositionColor(new Vector3(1, -1, 0.5f), colour);
                Verts[2] = new VertexPositionColor(new Vector3(-1, 1, 0.5f), colour);
                Verts[3] = new VertexPositionColor(new Vector3(-1, 1, 0.5f), colour);
                Verts[4] = new VertexPositionColor(new Vector3(1, -1, 0.5f), colour);
                Verts[5] = new VertexPositionColor(new Vector3(1, 1, 0.5f), colour);
                Effect.World = Matrix.Identity;
                Effect.View = Matrix.Identity;
                Effect.Projection = Matrix.Identity;
                Submit(device, 6);
            }
        }

        /// <summary>A ring between two angles from the view direction, as a triangle strip of quads in view space.</summary>
        private int AddRing(int index, float innerDegrees, float outerDegrees, float depth, Color innerColour, Color outerColour)
        {
            var rInner = depth * (float)Math.Tan(MathHelper.ToRadians(innerDegrees));
            var rOuter = depth * (float)Math.Tan(MathHelper.ToRadians(outerDegrees));
            for (int i = 0; i < Segments; i++)
            {
                var a0 = MathHelper.TwoPi * i / Segments;
                var a1 = MathHelper.TwoPi * (i + 1) / Segments;
                var d0 = new Vector2((float)Math.Cos(a0), (float)Math.Sin(a0));
                var d1 = new Vector2((float)Math.Cos(a1), (float)Math.Sin(a1));
                var i0 = new Vector3(d0 * rInner, -depth);
                var i1 = new Vector3(d1 * rInner, -depth);
                var o0 = new Vector3(d0 * rOuter, -depth);
                var o1 = new Vector3(d1 * rOuter, -depth);
                Verts[index++] = new VertexPositionColor(i0, innerColour);
                Verts[index++] = new VertexPositionColor(o0, outerColour);
                Verts[index++] = new VertexPositionColor(i1, innerColour);
                Verts[index++] = new VertexPositionColor(i1, innerColour);
                Verts[index++] = new VertexPositionColor(o0, outerColour);
                Verts[index++] = new VertexPositionColor(o1, outerColour);
            }
            return index;
        }

        private void Submit(GraphicsDevice device, int count)
        {
            device.BlendState = BlendState.AlphaBlend;
            device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = RasterizerState.CullNone;
            foreach (var pass in Effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleList, Verts, 0, count / 3);
            }
            device.DepthStencilState = DepthStencilState.Default;
        }

        public void Dispose()
        {
            Effect.Dispose();
        }
    }
}
