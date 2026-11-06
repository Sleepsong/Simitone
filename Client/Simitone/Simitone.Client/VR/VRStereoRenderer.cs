using System;
using System.Collections.Generic;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.VR;
using Simitone.VR.Graphics;

namespace Simitone.Client.VR
{
    /// <summary>
    /// Draws the lot into both eyes' render targets (colour and depth; the pointer, controllers and comfort overlay are
    /// drawn on top afterwards, per eye):
    ///  - <b>Single-pass stereo</b> (<see cref="GLMultiview"/>, when the driver can): the lot is drawn once, from one
    ///    view that encloses both eyes (<see cref="VRMath.Enclose"/>), into a two-layer target, each layer through its
    ///    eye's own projection; else once per eye, as before.
    ///  - <b>Foveated rendering</b> (<see cref="VRSettings.Foveation"/>): each eye is drawn twice: the whole view at
    ///    lower resolution, and a square around where you look (<see cref="VRInput.Gaze"/>; the middle without eye
    ///    tracking) at full resolution; the sharp square is blended over the soft one with a feathered edge. With
    ///    single-pass stereo that's two passes for the frame instead of two; without it, four, so it trades CPU for GPU.
    /// </summary>
    public class VRStereoRenderer : IDisposable
    {
        private readonly GraphicsDevice GD;
        private GLMultiview Multiview;
        private bool MultiviewTried, MultiviewBroken;
        private int ScannedResources = -1;
        private readonly Dictionary<Point, GLMultiview.Target> Targets = new Dictionary<Point, GLMultiview.Target>();
        private readonly RenderTarget2D[] Periphery = new RenderTarget2D[2], Inset = new RenderTarget2D[2];
        private SpriteBatch Batch;
        private Texture2D Mask;
        private readonly Vector2[] GazeTangent = new Vector2[2];
        private float GazeLostFor = 10;
        private const float GazeSmoothing = 50, GazeReturn = 9; // per second
        private string LastMode;

        private static readonly BlendState WriteAlphaOnly = new BlendState
        {
            ColorWriteChannels = ColorWriteChannels.Alpha,
            ColorSourceBlend = Blend.Zero, ColorDestinationBlend = Blend.One,
            AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.Zero
        };

        // the sharp square over the soft view: blended by its (mask) alpha, leaving the eye image opaque
        private static readonly BlendState BlendKeepAlpha = new BlendState
        {
            ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.InverseSourceAlpha,
            AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One
        };

        public VRStereoRenderer(GraphicsDevice device)
        {
            GD = device;
        }

        /// <summary>What the last frame used, for the log and tests ("single pass", "two passes, foveated ...").</summary>
        public string Mode => LastMode;

        /// <summary>The point you're looking at (2 m along your gaze, world units), or null without eye tracking.</summary>
        public static Vector3? GazePoint(VRCameraController camera)
        {
            var gaze = SimitoneVR.Runtime?.Input?.Gaze ?? default;
            if (!gaze.Valid) return null;
            return camera.TrackingToWorld(gaze.Position)
                + camera.TrackingDirectionToWorld(gaze.Forward) * 2f * camera.UnitsPerMetre;
        }

        /// <summary>
        /// Draws the lot into <paramref name="eyes"/>. <paramref name="drawWorld"/> draws it once with whatever the eye
        /// camera is set to (into the render target that's set). <paramref name="gazePoint"/> is the point you're
        /// looking at (world units), if the headset tracks your eyes.
        /// </summary>
        public void Render(VREyeCamera cam, RenderTarget2D[] eyes, Color clear, Vector3? gazePoint, Action drawWorld)
        {
            var settings = SimitoneVR.Settings;
            if (!cam.EyeFrustaValid)
            {
                for (int eye = 0; eye < 2; eye++)
                {
                    cam.Eye = eye;
                    PPXDepthEngine.SetPPXTarget(eyes[eye], null, true, clear);
                    drawWorld();
                }
                return;
            }
            var mv = settings.SinglePassStereo ? GetMultiview() : null;
            float near = cam.NearPlane, far = cam.FarPlane;
            var left = cam.EyeFrustum[0];
            var right = cam.EyeFrustum[1];

            if (settings.Foveation == VRFoveation.Off)
            {
                Pair(mv, cam, left, right, near, far, eyes[0], eyes[1], clear, drawWorld);
                Report(mv != null ? "single pass" : "two passes");
                return;
            }

            // ---- foveated: the soft whole view, then the sharp centre around the gaze
            var strong = settings.Foveation == VRFoveation.Strong;
            var scale = strong ? 0.5f : 0.7f;
            var half = strong ? 0.36f : 0.45f; // tangent half-size of the sharp square: ~20 or ~24 degrees each way
            var tracked = UpdateGaze(gazePoint, left, right);
            int w = eyes[0].Width, h = eyes[0].Height;
            // (the sharp square's size in pixels, at the eye's density, the same for both eyes and every frame so its
            // targets stay; snapped to the eye's pixels so the sharp image lines up with it exactly)
            int insetW = Math.Min(w, (int)Math.Ceiling(Math.Max(2 * half / (left.Right - left.Left), 2 * half / (right.Right - right.Left)) * w));
            int insetH = Math.Min(h, (int)Math.Ceiling(Math.Max(2 * half / (left.Up - left.Down), 2 * half / (right.Up - right.Down)) * h));
            var insetLeft = VRMath.InsetPixels(left, GazeTangent[0], new Point(insetW, insetH), w, h);
            var insetRight = VRMath.InsetPixels(right, GazeTangent[1], new Point(insetW, insetH), w, h);
            Ensure(Periphery, Math.Max(16, (int)(w * scale)), Math.Max(16, (int)(h * scale)));
            Ensure(Inset, insetLeft.rect.Width, insetLeft.rect.Height);

            Pair(mv, cam, left, right, near, far, Periphery[0], Periphery[1], clear, drawWorld);
            Pair(mv, cam, insetLeft.view, insetRight.view, near, far, Inset[0], Inset[1], clear, drawWorld);
            Composite(eyes[0], Periphery[0], Inset[0], insetLeft.rect);
            Composite(eyes[1], Periphery[1], Inset[1], insetRight.rect);
            Report((mv != null ? "single pass" : "two passes") + $", foveated ({settings.Foveation}, {(tracked ? "following your eyes" : "centre")})");
        }

        /// <summary>Draws two views of the lot, into two render targets of the same size.</summary>
        private void Pair(GLMultiview mv, VREyeCamera cam, in VRMath.ViewFrustum a, in VRMath.ViewFrustum b, float near, float far,
            RenderTarget2D outA, RenderTarget2D outB, Color clear, Action drawWorld)
        {
            if (mv != null)
            {
                var both = VRMath.Enclose(a, b);
                var projection = both.Projection(near * 0.5f, far);
                var viewProjection = both.View * projection;
                cam.SetCustom(both.View, projection); // (FreeSO culls and sorts for this one view)
                var target = TargetFor(outA.Width, outA.Height);
                PPXDepthEngine.SetPPXTarget(outA, null, true, clear);
                mv.Begin(target, VRMath.ClipDelta(viewProjection, a.View * a.Projection(near, far)),
                    VRMath.ClipDelta(viewProjection, b.View * b.Projection(near, far)));
                try { drawWorld(); }
                finally { mv.End(); }
                mv.CopyLayer(target, 0, outA);
                mv.CopyLayer(target, 1, outB);
                return;
            }
            cam.SetCustom(a.View, a.Projection(near, far));
            PPXDepthEngine.SetPPXTarget(outA, null, true, clear);
            drawWorld();
            cam.SetCustom(b.View, b.Projection(near, far));
            PPXDepthEngine.SetPPXTarget(outB, null, true, clear);
            drawWorld();
        }

        /// <summary>The soft view stretched over the eye, the sharp square blended in where it belongs, and their depth.</summary>
        private void Composite(RenderTarget2D eye, RenderTarget2D soft, RenderTarget2D sharp, Rectangle rect)
        {
            Batch ??= new SpriteBatch(GD);
            Mask ??= MakeMask(GD);
            // the sharp square's alpha becomes the feathered mask
            GD.SetRenderTarget(sharp);
            Batch.Begin(SpriteSortMode.Immediate, WriteAlphaOnly, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
            Batch.Draw(Mask, new Rectangle(0, 0, sharp.Width, sharp.Height), Color.White);
            Batch.End();
            GD.SetRenderTarget(eye);
            Batch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
            Batch.Draw(soft, new Rectangle(0, 0, eye.Width, eye.Height), Color.White);
            Batch.End();
            Batch.Begin(SpriteSortMode.Immediate, BlendKeepAlpha, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
            Batch.Draw(sharp, rect, Color.White);
            Batch.End();
            // depth, for what's drawn over the lot afterwards (pointer, controllers)
            GLInterop.BlitTargets(GD, soft, new Rectangle(0, 0, soft.Width, soft.Height), eye, new Rectangle(0, 0, eye.Width, eye.Height), false, true, false);
            GLInterop.BlitTargets(GD, sharp, new Rectangle(0, 0, sharp.Width, sharp.Height), eye, rect, false, true, false);
        }

        /// <summary>Where each eye looks, as tangents (smoothed); the middle without eye tracking. True while tracked.</summary>
        private bool UpdateGaze(Vector3? gazePoint, in VRMath.ViewFrustum left, in VRMath.ViewFrustum right)
        {
            var dt = SimitoneVR.FrameDelta;
            if (gazePoint != null)
            {
                GazeLostFor = 0;
                var point = gazePoint.Value;
                var follow = 1 - (float)Math.Exp(-GazeSmoothing * dt); // (a little smoothing of tracker noise)
                for (int i = 0; i < 2; i++)
                {
                    var eye = i == 0 ? left : right;
                    var t = VRMath.Tangents(eye, point - eye.Position);
                    if (t != null) GazeTangent[i] = Vector2.Lerp(GazeTangent[i], t.Value, follow);
                }
                return true;
            }
            // a blink or a moment's tracking loss keeps the last spot; after that, ease back to the middle
            GazeLostFor += dt;
            if (GazeLostFor > 0.4f)
            {
                var back = 1 - (float)Math.Exp(-GazeReturn * dt);
                for (int i = 0; i < 2; i++) GazeTangent[i] = Vector2.Lerp(GazeTangent[i], Vector2.Zero, back);
            }
            return false;
        }

        private GLMultiview GetMultiview()
        {
            if (MultiviewBroken) return null;
            if (!MultiviewTried)
            {
                MultiviewTried = true;
                Multiview = GLMultiview.TryCreate(GD, SimitoneVR.Log);
                if (Multiview == null)
                {
                    MultiviewBroken = true;
                    SimitoneVR.StereoStatus = "Unsupported";
                    SimitoneVR.Log("single-pass stereo unavailable: " + GLMultiview.Unavailable + "; drawing each eye separately");
                    return null;
                }
            }
            // Every vertex shader MonoGame has needs its stereo variant before the pass (a program without one can't draw
            // into the two-layer target). New effects can appear while playing; they add GPU resources, so look again
            // whenever the count changes.
            var resources = Multiview.ResourceCount;
            if (resources != ScannedResources)
            {
                ScannedResources = resources;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                if (!Multiview.PrepareShaders(out var made, out var problem))
                {
                    MultiviewBroken = true;
                    SimitoneVR.StereoStatus = "Unsupported";
                    SimitoneVR.Log("single-pass stereo off: " + problem + "; drawing each eye separately");
                    DisposeMultiview();
                    return null;
                }
                if (made > 0) SimitoneVR.Log($"single-pass stereo: {made} shader(s) prepared in {watch.ElapsedMilliseconds} ms");
            }
            SimitoneVR.StereoStatus = "On";
            return Multiview;
        }

        private GLMultiview.Target TargetFor(int width, int height)
        {
            var key = new Point(width, height);
            if (!Targets.TryGetValue(key, out var target))
            {
                if (Targets.Count >= 4) ClearTargets(); // (render scale changed: the old sizes are done with)
                target = Multiview.CreateTarget(width, height);
                Targets[key] = target;
            }
            return target;
        }

        private void Ensure(RenderTarget2D[] pair, int width, int height)
        {
            for (int i = 0; i < 2; i++)
            {
                if (pair[i] != null && pair[i].Width == width && pair[i].Height == height) continue;
                pair[i]?.Dispose();
                pair[i] = new RenderTarget2D(GD, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0,
                    RenderTargetUsage.PreserveContents);
            }
        }

        private void Report(string mode)
        {
            if (mode == LastMode) return;
            LastMode = mode;
            SimitoneVR.Log("drawing the lot: " + mode);
        }

        /// <summary>White with a soft-edged rounded square of alpha: the sharp centre fades into the soft view.</summary>
        private static Texture2D MakeMask(GraphicsDevice gd)
        {
            const int size = 64;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2 - 1, v = (y + 0.5f) / size * 2 - 1;
                    var d = (float)Math.Pow(Math.Pow(Math.Abs(u), 4) + Math.Pow(Math.Abs(v), 4), 0.25);
                    var t = MathHelper.Clamp((d - 0.72f) / (0.97f - 0.72f), 0, 1);
                    var alpha = 1 - t * t * (3 - 2 * t);
                    pixels[y * size + x] = new Color(255, 255, 255, (int)(alpha * 255));
                }
            var texture = new Texture2D(gd, size, size);
            texture.SetData(pixels);
            return texture;
        }

        private void ClearTargets()
        {
            foreach (var t in Targets.Values) Multiview?.DestroyTarget(t);
            Targets.Clear();
        }

        private void DisposeMultiview()
        {
            ClearTargets();
            Multiview?.Dispose();
            Multiview = null;
        }

        public void Dispose()
        {
            DisposeMultiview();
            foreach (var t in Periphery) t?.Dispose();
            foreach (var t in Inset) t?.Dispose();
            Batch?.Dispose();
            Mask?.Dispose();
        }
    }
}
