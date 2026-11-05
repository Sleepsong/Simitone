using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.VR;
using Simitone.VR.Graphics;

namespace Simitone.Client.VR
{
    /// <summary>
    /// A flat panel floating in the room, drawn into its own render target and shown as an OpenXR quad layer (the VR
    /// menu, the comfort setup, the keyboard). Handles placement, pointing (controller ray to panel pixels), the
    /// render target and swapchain, and a few drawing helpers.
    /// </summary>
    public abstract class VRPanel : IDisposable
    {
        protected Vector3 Position;
        protected Quaternion Orientation = Quaternion.Identity;
        /// <summary>Physical width in metres; the height follows the image's aspect.</summary>
        protected float WidthMetres = 0.6f;

        protected RenderTarget2D Target;
        protected SpriteBatch Batch;
        protected Texture2D White;
        private VRSwapchain Chain;

        /// <summary>Distance (metres) from the controller to where it points on the panel, or 0 if it's not on it.</summary>
        public float PointerDistance { get; protected set; }

        /// <summary>Puts the panel in front of the head (tracking space), tilted back by <paramref name="tiltDegrees"/>.</summary>
        protected void PlaceInFront(VRPose head, float distance, float drop, float tiltDegrees)
        {
            var yaw = Quaternion.CreateFromAxisAngle(Vector3.Up, VRMath.Yaw(head.Orientation));
            var forward = Vector3.Transform(-Vector3.UnitZ, yaw);
            Orientation = yaw * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.ToRadians(-tiltDegrees));
            Position = head.Position + forward * distance - new Vector3(0, drop, 0);
        }

        /// <summary>
        /// Where the right controller (or the left, if the right isn't tracked) points on the panel, in pixels of a
        /// <paramref name="width"/> x <paramref name="height"/> image. Sets <see cref="PointerDistance"/>.
        /// </summary>
        protected bool PointAt(VRInput input, int width, int height, out Point pixel)
        {
            pixel = Point.Zero;
            PointerDistance = 0;
            if (input == null || !input.Active) return false;
            var aim = input.RightAim.Valid ? input.RightAim : input.LeftAim;
            if (!aim.Valid) return false;
            var normal = Vector3.Transform(Vector3.UnitZ, Orientation);
            var denom = Vector3.Dot(aim.Forward, normal);
            if (Math.Abs(denom) < 1e-4f) return false;
            var t = Vector3.Dot(Position - aim.Position, normal) / denom;
            if (t <= 0) return false;
            var local = Vector3.Transform(aim.Position + aim.Forward * t - Position, Quaternion.Inverse(Orientation));
            var heightMetres = WidthMetres * height / width;
            var u = local.X / WidthMetres + 0.5f;
            var v = 0.5f - local.Y / heightMetres;
            if (u < 0 || u > 1 || v < 0 || v > 1) return false;
            PointerDistance = t;
            pixel = new Point((int)(u * width), (int)(v * height));
            return true;
        }

        /// <summary>Gets the render target (and the batch and a white pixel) ready, and starts drawing into it.</summary>
        protected void BeginRender(GraphicsDevice gd, int width, int height)
        {
            Batch ??= new SpriteBatch(gd);
            if (White == null)
            {
                White = new Texture2D(gd, 1, 1);
                White.SetData(new[] { Color.White });
            }
            if (Target == null || Target.Width != width || Target.Height != height)
            {
                Target?.Dispose();
                Target = new RenderTarget2D(gd, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            }
            gd.SetRenderTarget(Target);
            gd.Clear(Color.Transparent);
            Batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        }

        protected void EndRender(GraphicsDevice gd)
        {
            Batch.End();
            gd.SetRenderTarget(null);
        }

        protected void Fill(Rectangle r, Color colour) => Batch.Draw(White, r, colour);

        protected void Border(Rectangle r, Color colour, int thickness)
        {
            Batch.Draw(White, new Rectangle(r.X, r.Y, r.Width, thickness), colour);
            Batch.Draw(White, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), colour);
            Batch.Draw(White, new Rectangle(r.X, r.Y, thickness, r.Height), colour);
            Batch.Draw(White, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), colour);
        }

        protected void Text(string text, int x, int y, int dot, Color colour)
            => VRPixelFont.Draw(Batch, White, text, new Vector2(x, y), dot, colour);

        protected void TextCentred(string text, Rectangle r, int dot, Color colour)
            => Text(text, r.X + (r.Width - VRPixelFont.Measure(text, dot)) / 2, r.Y + (r.Height - VRPixelFont.GlyphHeight * dot) / 2, dot, colour);

        /// <summary>Copies the panel into its swapchain and adds it to the frame (call only while it's shown).</summary>
        protected void SubmitPanel(VRRuntime runtime, ref VRFrameSubmission submission)
        {
            if (Target == null) return;
            if (Chain == null || Chain.Width != Target.Width || Chain.Height != Target.Height)
            {
                Chain?.Dispose();
                Chain = runtime.CreateSwapchain(Target.Width, Target.Height);
            }
            var image = Chain.Acquire();
            GLInterop.BlitTexture(GLInterop.GetTextureHandle(Target), Target.Width, Target.Height, image, 0, 0, Chain.Width, Chain.Height, flipY: true);
            Chain.Release();
            submission.AddQuad(new VRQuad
            {
                Swapchain = Chain,
                Pose = VRMath.Pose(Position, Orientation),
                Width = WidthMetres,
                Height = WidthMetres * Target.Height / Target.Width,
                Transparent = true
            });
        }

        public virtual void Dispose()
        {
            Chain?.Dispose();
            Chain = null;
            Target?.Dispose();
            White?.Dispose();
            Batch?.Dispose();
        }
    }
}
