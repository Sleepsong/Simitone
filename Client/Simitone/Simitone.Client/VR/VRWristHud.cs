using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.VR;
using Simitone.VR.Graphics;

namespace Simitone.Client.VR
{
    /// <summary>Game state the wrist displays show. Filled in by <see cref="VRGameBridge"/>.</summary>
    public class VRHudData
    {
        public bool Valid;
        public int Hour, Minute;
        /// <summary>0 paused, 1-3 speed, -1 build/buy/options.</summary>
        public int Speed;
        public int Money;
        public string SimName = "";
        /// <summary>Hunger, Comfort, Hygiene, Bladder, Energy, Fun, Social, Room: -100..100.</summary>
        public readonly float[] Motives = new float[8];
    }

    /// <summary>
    /// The HUD on your wrists while in first person: a watch on the right wrist (time, game speed, money) and the
    /// Sim's needs on the left. Each is drawn into a small render target and shown as a transparent quad attached to
    /// the controller's grip pose, on the back of the wrist; by default only while you turn that wrist to look at it.
    /// Pointing the right controller at the needs display and pulling the trigger opens the VR options menu.
    /// </summary>
    public class VRWristHud : IDisposable
    {
        private const int WatchWidth = 256, WatchHeight = 128;
        private const int NeedsWidth = 256, NeedsHeight = 276;
        private const float WatchMetres = 0.075f;  // physical width of the watch face
        private const float NeedsMetres = 0.085f;

        private static readonly string[] MotiveNames = { "HUNGER", "COMFORT", "HYGIENE", "BLADDER", "ENERGY", "FUN", "SOCIAL", "ROOM" };

        private RenderTarget2D Watch, Needs;
        private VRSwapchain WatchChain, NeedsChain;
        private SpriteBatch Batch;
        private Texture2D White;
        private int RenderCountdown;
        public bool WatchVisible { get; private set; }
        public bool NeedsVisible { get; private set; }
        private XrPoseCache WatchPose, NeedsPose;
        private bool _NeedsHighlight;

        /// <summary>The right controller points at the needs display (it lights up: the trigger opens the VR menu).</summary>
        public bool NeedsHighlight
        {
            get => _NeedsHighlight;
            set
            {
                if (value == _NeedsHighlight) return;
                _NeedsHighlight = value;
                RenderCountdown = 0;
            }
        }

        private struct XrPoseCache
        {
            public Vector3 Position;
            public Quaternion Orientation;
        }

        /// <summary>Decides visibility from the hand and head poses (tracking space). Call once per frame.</summary>
        public void UpdatePlacement(VRInput input, VRPose head, VRWristMode mode)
        {
            WatchVisible = NeedsVisible = false;
            if (mode == VRWristMode.Off || input == null || !input.Active || !head.Valid) return;
            // grip space: -Z towards the fingers, +X away from the left palm / into the right palm.
            // The display sits on the back of the wrist, a little behind the grip, reading along the forearm.
            WatchVisible = Place(input.RightGrip, head, new Vector3(-0.035f, 0.005f, 0.085f), -MathHelper.PiOver2, mode, out WatchPose);
            NeedsVisible = Place(input.LeftGrip, head, new Vector3(0.035f, 0.005f, 0.085f), MathHelper.PiOver2, mode, out NeedsPose);
        }

        /// <summary>Whether a controller ray hits the (visible) needs display, with a little margin.</summary>
        public bool PointsAtNeeds(VRPose aim)
        {
            if (!NeedsVisible || !aim.Valid) return false;
            var normal = Vector3.Transform(Vector3.UnitZ, NeedsPose.Orientation);
            var denom = Vector3.Dot(aim.Forward, normal);
            if (Math.Abs(denom) < 1e-4f) return false;
            var t = Vector3.Dot(NeedsPose.Position - aim.Position, normal) / denom;
            if (t <= 0 || t > 1.5f) return false;
            var local = Vector3.Transform(aim.Position + aim.Forward * t - NeedsPose.Position, Quaternion.Inverse(NeedsPose.Orientation));
            const float margin = 0.02f;
            return Math.Abs(local.X) <= NeedsMetres / 2 + margin
                && Math.Abs(local.Y) <= NeedsMetres * NeedsHeight / NeedsWidth / 2 + margin;
        }

        private static bool Place(VRPose grip, VRPose head, Vector3 offset, float yaw, VRWristMode mode, out XrPoseCache pose)
        {
            pose = default;
            if (!grip.Valid) return false;
            pose.Orientation = grip.Orientation * Quaternion.CreateFromAxisAngle(Vector3.Up, yaw);
            pose.Position = grip.Position + Vector3.Transform(offset, grip.Orientation);
            if (mode == VRWristMode.Always) return true;
            var normal = Vector3.Transform(Vector3.UnitZ, pose.Orientation);
            var toHead = head.Position - pose.Position;
            var distance = toHead.Length();
            if (distance < 1e-3f || distance > 0.9f) return false;
            return Vector3.Dot(normal, toHead / distance) > 0.55f; // turned towards your eyes
        }

        /// <summary>Redraws the displays (every few frames). Call outside the main draw (render target switches).</summary>
        public void Render(GraphicsDevice gd, VRHudData data)
        {
            if (!(WatchVisible || NeedsVisible) || data == null || !data.Valid) return;
            if (RenderCountdown-- > 0 && Watch != null) return;
            RenderCountdown = 5;

            Batch ??= new SpriteBatch(gd);
            if (White == null)
            {
                White = new Texture2D(gd, 1, 1);
                White.SetData(new[] { Color.White });
            }
            Watch ??= new RenderTarget2D(gd, WatchWidth, WatchHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            Needs ??= new RenderTarget2D(gd, NeedsWidth, NeedsHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);

            var bg = new Color(16, 20, 28) * 0.88f;
            var frame = new Color(90, 200, 255);
            var lcd = new Color(140, 255, 190);

            // ---- watch
            gd.SetRenderTarget(Watch);
            gd.Clear(Color.Transparent);
            Batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            Panel(new Rectangle(0, 0, WatchWidth, WatchHeight), bg, frame);
            var hour12 = data.Hour % 12 == 0 ? 12 : data.Hour % 12;
            var time = $"{hour12}:{data.Minute:00}";
            var big = 7;
            VRPixelFont.Draw(Batch, White, time, new Vector2((WatchWidth - VRPixelFont.Measure(time, big)) / 2 - 18, 14), big, lcd);
            VRPixelFont.Draw(Batch, White, data.Hour < 12 ? "AM" : "PM", new Vector2(WatchWidth - 40, 22), 3, lcd);
            var speed = data.Speed switch { 0 => "||", 1 => ">", 2 => ">>", 3 => ">>>", _ => "--" };
            VRPixelFont.Draw(Batch, White, speed, new Vector2(14, 90), 3, data.Speed == 0 ? new Color(255, 200, 90) : lcd);
            var money = "$" + data.Money.ToString("N0");
            VRPixelFont.Draw(Batch, White, money, new Vector2(WatchWidth - 14 - VRPixelFont.Measure(money, 3), 90), 3, lcd);
            Batch.End();

            // ---- needs
            gd.SetRenderTarget(Needs);
            gd.Clear(Color.Transparent);
            Batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            Panel(new Rectangle(0, 0, NeedsWidth, NeedsHeight), bg, NeedsHighlight ? Color.White : frame);
            var name = (data.SimName ?? "").ToUpperInvariant();
            if (name.Length > 18) name = name.Substring(0, 18);
            VRPixelFont.Draw(Batch, White, name, new Vector2(12, 10), 2, frame);
            for (int i = 0; i < 8; i++)
            {
                var y = 34 + i * 27;
                VRPixelFont.Draw(Batch, White, MotiveNames[i], new Vector2(12, y + 4), 2, Color.White * 0.9f);
                var bar = new Rectangle(104, y, NeedsWidth - 116, 20);
                Batch.Draw(White, bar, new Color(0, 0, 0) * 0.6f);
                var fraction = MathHelper.Clamp((data.Motives[i] + 100f) / 200f, 0, 1);
                var fill = new Rectangle(bar.X + 2, bar.Y + 2, (int)((bar.Width - 4) * fraction), bar.Height - 4);
                Batch.Draw(White, fill, MotiveColour(fraction));
            }
            VRPixelFont.Draw(Batch, White, "TRIGGER: VR MENU", new Vector2(12, NeedsHeight - 22), 2,
                NeedsHighlight ? Color.White : frame * 0.6f);
            Batch.End();
            gd.SetRenderTarget(null);
        }

        private void Panel(Rectangle r, Color fill, Color border)
        {
            Batch.Draw(White, r, fill);
            Batch.Draw(White, new Rectangle(r.X, r.Y, r.Width, 3), border);
            Batch.Draw(White, new Rectangle(r.X, r.Bottom - 3, r.Width, 3), border);
            Batch.Draw(White, new Rectangle(r.X, r.Y, 3, r.Height), border);
            Batch.Draw(White, new Rectangle(r.Right - 3, r.Y, 3, r.Height), border);
        }

        private static Color MotiveColour(float fraction)
        {
            // red (empty) -> yellow -> green (full), like the game's own need bars
            if (fraction < 0.5f) return Color.Lerp(new Color(230, 50, 40), new Color(240, 210, 60), fraction * 2);
            return Color.Lerp(new Color(240, 210, 60), new Color(90, 220, 80), (fraction - 0.5f) * 2);
        }

        /// <summary>Copies the displays into their swapchains and adds them to the frame.</summary>
        public void Submit(VRRuntime runtime, ref VRFrameSubmission submission)
        {
            if (Watch == null) return;
            if (WatchVisible) AddQuad(runtime, ref submission, Watch, ref WatchChain, WatchPose, WatchMetres);
            if (NeedsVisible) AddQuad(runtime, ref submission, Needs, ref NeedsChain, NeedsPose, NeedsMetres);
        }

        private static void AddQuad(VRRuntime runtime, ref VRFrameSubmission submission, RenderTarget2D target,
            ref VRSwapchain chain, XrPoseCache pose, float widthMetres)
        {
            chain ??= runtime.CreateSwapchain(target.Width, target.Height);
            var image = chain.Acquire();
            GLInterop.BlitTexture(GLInterop.GetTextureHandle(target), target.Width, target.Height, image, 0, 0, chain.Width, chain.Height, flipY: true);
            chain.Release();
            submission.AddQuad(new VRQuad
            {
                Swapchain = chain,
                Pose = VRMath.Pose(pose.Position, pose.Orientation),
                Width = widthMetres,
                Height = widthMetres * target.Height / target.Width,
                Transparent = true
            });
        }

        public void Dispose()
        {
            WatchChain?.Dispose();
            NeedsChain?.Dispose();
            Watch?.Dispose();
            Needs?.Dispose();
            White?.Dispose();
            Batch?.Dispose();
            WatchChain = NeedsChain = null;
        }
    }
}
