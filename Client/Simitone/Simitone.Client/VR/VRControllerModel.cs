using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Client.VR
{
    /// <summary>
    /// A simple controller drawn in your hands when you're not in your Sim's body (as an observer, or stepped out of
    /// them): a handle through your fist, a round head with a thumbstick and a band of Simitone's green, and a trigger. It's
    /// made here from boxes and cylinders, in the grip pose's space (metres; -Z along the handle, +Y toward the thumb),
    /// rather than any one headset maker's model.
    /// </summary>
    public class VRControllerModel : IDisposable
    {
        private readonly BasicEffect Effect;
        private readonly VertexPositionColor[] Vertices;
        private static readonly Vector3 Light = Vector3.Normalize(new Vector3(0.3f, 1f, 0.45f));

        private static readonly Color Body = new Color(72, 77, 88), Head = new Color(58, 62, 72), Nub = new Color(120, 126, 138);
        private static readonly Color Accent = new Color(0, 255, 128); // Simitone's green (UIStyle SecondaryText)

        public VRControllerModel(GraphicsDevice device)
        {
            Effect = new BasicEffect(device) { VertexColorEnabled = true, LightingEnabled = false, TextureEnabled = false };
            var v = new List<VertexPositionColor>();
            Box(v, new Vector3(-0.017f, -0.022f, -0.058f), new Vector3(0.017f, 0.020f, 0.062f), Body);             // handle
            var alongHandle = Matrix.CreateRotationX(-MathHelper.PiOver2);                                           // cylinder Y -> -Z
            Cylinder(v, alongHandle * Matrix.CreateTranslation(0, 0.006f, -0.074f), 0.033f, 0.015f, 0, Head, Head); // head
            Cylinder(v, alongHandle * Matrix.CreateTranslation(0, 0.006f, -0.0895f), 0.031f, 0.0008f, 0.022f, Accent, Accent); // ring
            Cylinder(v, alongHandle * Matrix.CreateTranslation(0, 0.006f, -0.062f), 0.0345f, 0.0035f, 0, Accent, Head);   // band
            Box(v, new Vector3(-0.006f, 0.036f, -0.081f), new Vector3(0.006f, 0.047f, -0.069f), Nub);                // thumbstick
            Box(v, new Vector3(-0.008f, -0.034f, -0.064f), new Vector3(0.008f, -0.020f, -0.044f), Nub);              // trigger
            Vertices = v.ToArray();
        }

        /// <summary>Draws one controller. <paramref name="world"/> maps the grip pose's metres into the world.</summary>
        public void Draw(GraphicsDevice device, Matrix world, Matrix view, Matrix projection)
        {
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.Default;
            device.RasterizerState = RasterizerState.CullNone;
            Effect.World = world;
            Effect.View = view;
            Effect.Projection = projection;
            foreach (var pass in Effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleList, Vertices, 0, Vertices.Length / 3);
            }
        }

        public void Dispose() => Effect.Dispose();

        // ------------------------------------------------------------------ building the mesh

        private static Color Shade(Color colour, Vector3 normal)
        {
            var light = 0.55f + 0.45f * Math.Max(0, Vector3.Dot(Vector3.Normalize(normal), Light));
            return new Color(colour.ToVector3() * light);
        }

        private static void Quad(List<VertexPositionColor> v, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour)
        {
            var shaded = Shade(colour, Vector3.Cross(b - a, c - a));
            v.Add(new VertexPositionColor(a, shaded));
            v.Add(new VertexPositionColor(b, shaded));
            v.Add(new VertexPositionColor(c, shaded));
            v.Add(new VertexPositionColor(a, shaded));
            v.Add(new VertexPositionColor(c, shaded));
            v.Add(new VertexPositionColor(d, shaded));
        }

        private static void Box(List<VertexPositionColor> v, Vector3 min, Vector3 max, Color colour)
        {
            Vector3 P(float x, float y, float z) => new Vector3(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z);
            Quad(v, P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0), colour); // top (+Y)
            Quad(v, P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1), colour); // bottom
            Quad(v, P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1), colour); // +X
            Quad(v, P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0), colour); // -X
            Quad(v, P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1), colour); // +Z
            Quad(v, P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0), colour); // -Z
        }

        /// <summary>
        /// A cylinder along local Y (moved into place by <paramref name="transform"/>), with a hole of radius
        /// <paramref name="inner"/> if that's above zero (a ring).
        /// </summary>
        private static void Cylinder(List<VertexPositionColor> v, Matrix transform, float radius, float halfHeight, float inner,
            Color side, Color ends)
        {
            const int segments = 24;
            Vector3 T(Vector3 p) => Vector3.Transform(p, transform);
            for (int i = 0; i < segments; i++)
            {
                float a0 = MathHelper.TwoPi * i / segments, a1 = MathHelper.TwoPi * (i + 1) / segments;
                Vector3 Rim(float a, float r, float y) => new Vector3((float)Math.Cos(a) * r, y, (float)Math.Sin(a) * r);
                // outside
                Quad(v, T(Rim(a0, radius, -halfHeight)), T(Rim(a0, radius, halfHeight)), T(Rim(a1, radius, halfHeight)),
                    T(Rim(a1, radius, -halfHeight)), side);
                // the two ends (an annulus for a ring)
                foreach (var y in new[] { halfHeight, -halfHeight })
                {
                    if (inner > 0 && y > 0)
                        Quad(v, T(Rim(a0, inner, y)), T(Rim(a1, inner, y)), T(Rim(a1, radius, y)), T(Rim(a0, radius, y)), ends);
                    else if (inner > 0) // (wound the other way, so it's shaded as facing the other way)
                        Quad(v, T(Rim(a0, inner, y)), T(Rim(a0, radius, y)), T(Rim(a1, radius, y)), T(Rim(a1, inner, y)), ends);
                    else
                    {
                        var centre = T(new Vector3(0, y, 0));
                        var shaded = Shade(ends, Vector3.TransformNormal(new Vector3(0, Math.Sign(y), 0), transform));
                        v.Add(new VertexPositionColor(centre, shaded));
                        v.Add(new VertexPositionColor(T(Rim(a0, radius, y)), shaded));
                        v.Add(new VertexPositionColor(T(Rim(a1, radius, y)), shaded));
                    }
                }
            }
        }
    }
}
