using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.VR;
using Simitone.VR.Graphics;

namespace Simitone.Client.VR
{
    /// <summary>One entry of the Sim's action queue, as the wrist display and the action bubble show it.</summary>
    public class VRHudQueueItem
    {
        public Texture2D Icon;
        public string Name = "";
        /// <summary>The action running now (the first in the queue).</summary>
        public bool Active;
        /// <summary>Already being cancelled (the Sim is winding it down).</summary>
        public bool Cancelled;
        /// <summary>The queued action's UID, for VMNetInteractionCancelCmd.</summary>
        public ushort UID;
    }

    /// <summary>Game state the wrist displays show. Filled in by <see cref="VRGameBridge"/>.</summary>
    public class VRHudData
    {
        public const int MaxQueue = 5;

        public bool Valid;
        public int Hour, Minute;
        /// <summary>0 paused, 1-3 speed, -1 build/buy/options.</summary>
        public int Speed;
        public int Money;
        public string SimName = "";
        /// <summary>Hunger, Comfort, Hygiene, Bladder, Energy, Fun, Social, Room: -100..100.</summary>
        public readonly float[] Motives = new float[8];
        /// <summary>The selected Sim's action queue, as the game's own queue display shows it (at most <see cref="MaxQueue"/>).</summary>
        public readonly VRHudQueueItem[] Queue = new VRHudQueueItem[MaxQueue];
        public int QueueCount;
        /// <summary>Unread messages waiting on the watch, and the oldest one's title.</summary>
        public int Messages;
        public string MessageTitle = "";

        public VRHudData()
        {
            for (int i = 0; i < MaxQueue; i++) Queue[i] = new VRHudQueueItem();
        }
    }

    /// <summary>
    /// The HUD on your wrists while in first person. Each display is drawn into a small render target and shown as a
    /// transparent quad attached to the controller's grip pose, on the back of the wrist; by default only while you
    /// turn that wrist to look at it.
    ///  - <b>Right wrist, the watch:</b> time, game speed and money, and a "!" line when messages are waiting (look at it
    ///    and pull that hand's trigger to read the oldest).
    ///  - <b>Left wrist:</b> the Sim's name, their action queue (icons, the running one first, and its name) and their
    ///    needs. Point the right controller at a queue icon and pull the trigger to cancel that action; anywhere else
    ///    on the display, the trigger opens the VR options menu.
    /// </summary>
    public class VRWristHud : IDisposable
    {
        private const int WatchWidth = 256, WatchHeight = 160;
        private const int NeedsWidth = 256, NeedsHeight = 344;
        private const float WatchMetres = 0.075f;  // physical width of the watch face
        private const float NeedsMetres = 0.085f;
        private const int QueueTop = 30, QueueSlot = 40, QueueStride = 48, QueueLeft = 12;
        private const int MotivesTop = 100;

        private static readonly string[] MotiveNames = { "HUNGER", "COMFORT", "HYGIENE", "BLADDER", "ENERGY", "FUN", "SOCIAL", "ROOM" };

        private RenderTarget2D Watch, Needs;
        private VRSwapchain WatchChain, NeedsChain;
        private SpriteBatch Batch;
        private Texture2D White;
        private int RenderCountdown;
        public bool WatchVisible { get; private set; }
        public bool NeedsVisible { get; private set; }
        /// <summary>The watch is turned towards your eyes (you're looking at it), whatever <see cref="VRWristMode"/> is.</summary>
        public bool WatchFacing { get; private set; }
        private XrPoseCache WatchPose, NeedsPose;
        private int _NeedsHover = NotPointed;

        public const int NotPointed = -2;
        public const int PointedAtDisplay = -1;

        /// <summary>
        /// What the right controller points at on the needs display: <see cref="NotPointed"/>, the display itself
        /// (<see cref="PointedAtDisplay"/>: the trigger opens the VR menu) or a queue slot (0..4: the trigger cancels it).
        /// </summary>
        public int NeedsHover
        {
            get => _NeedsHover;
            set
            {
                if (value == _NeedsHover) return;
                _NeedsHover = value;
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
            WatchVisible = NeedsVisible = WatchFacing = false;
            if (mode == VRWristMode.Off || input == null || !input.Active || !head.Valid) return;
            // grip space: -Z towards the fingers, +X away from the left palm / into the right palm.
            // The display sits on the back of the wrist, a little behind the grip, reading along the forearm.
            WatchVisible = Place(input.RightGrip, head, new Vector3(-0.035f, 0.005f, 0.085f), -MathHelper.PiOver2, mode, out WatchPose, out var watchFacing);
            NeedsVisible = Place(input.LeftGrip, head, new Vector3(0.035f, 0.005f, 0.085f), MathHelper.PiOver2, mode, out NeedsPose, out _);
            WatchFacing = watchFacing;
        }

        /// <summary>
        /// Where a controller ray hits the (visible) needs display, in its pixels (with a little margin around it, so
        /// the result can be just outside the image). False if it misses.
        /// </summary>
        public bool PointNeeds(VRPose aim, out Point pixel)
        {
            pixel = Point.Zero;
            if (!NeedsVisible || !aim.Valid) return false;
            var normal = Vector3.Transform(Vector3.UnitZ, NeedsPose.Orientation);
            var denom = Vector3.Dot(aim.Forward, normal);
            if (Math.Abs(denom) < 1e-4f) return false;
            var t = Vector3.Dot(NeedsPose.Position - aim.Position, normal) / denom;
            if (t <= 0 || t > 1.5f) return false;
            var local = Vector3.Transform(aim.Position + aim.Forward * t - NeedsPose.Position, Quaternion.Inverse(NeedsPose.Orientation));
            const float margin = 0.02f;
            var height = NeedsMetres * NeedsHeight / NeedsWidth;
            if (Math.Abs(local.X) > NeedsMetres / 2 + margin || Math.Abs(local.Y) > height / 2 + margin) return false;
            pixel = new Point((int)((local.X / NeedsMetres + 0.5f) * NeedsWidth), (int)((0.5f - local.Y / height) * NeedsHeight));
            return true;
        }

        /// <summary>The queue slot (0..4) at a pixel of the needs display, or -1.</summary>
        public static int QueueSlotAt(Point pixel)
        {
            if (pixel.Y < QueueTop - 4 || pixel.Y > QueueTop + QueueSlot + 4) return -1;
            var slot = (pixel.X - QueueLeft + (QueueStride - QueueSlot) / 2) / QueueStride;
            return pixel.X >= QueueLeft - 4 && slot >= 0 && slot < VRHudData.MaxQueue ? slot : -1;
        }

        private static bool Place(VRPose grip, VRPose head, Vector3 offset, float yaw, VRWristMode mode, out XrPoseCache pose, out bool facing)
        {
            pose = default;
            facing = false;
            if (!grip.Valid) return false;
            pose.Orientation = grip.Orientation * Quaternion.CreateFromAxisAngle(Vector3.Up, yaw);
            pose.Position = grip.Position + Vector3.Transform(offset, grip.Orientation);
            var normal = Vector3.Transform(Vector3.UnitZ, pose.Orientation);
            var toHead = head.Position - pose.Position;
            var distance = toHead.Length();
            facing = distance > 1e-3f && distance < 0.9f && Vector3.Dot(normal, toHead / distance) > 0.55f; // turned towards your eyes
            return mode == VRWristMode.Always || facing;
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
            var amber = new Color(255, 200, 90);

            // ---- watch
            gd.SetRenderTarget(Watch);
            gd.Clear(Color.Transparent);
            Batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            Panel(new Rectangle(0, 0, WatchWidth, WatchHeight), bg, data.Messages > 0 ? amber : frame);
            var hour12 = data.Hour % 12 == 0 ? 12 : data.Hour % 12;
            var time = $"{hour12}:{data.Minute:00}";
            var big = 7;
            VRPixelFont.Draw(Batch, White, time, new Vector2((WatchWidth - VRPixelFont.Measure(time, big)) / 2 - 18, 14), big, lcd);
            VRPixelFont.Draw(Batch, White, data.Hour < 12 ? "AM" : "PM", new Vector2(WatchWidth - 40, 22), 3, lcd);
            var speed = data.Speed switch { 0 => "||", 1 => ">", 2 => ">>", 3 => ">>>", _ => "--" };
            VRPixelFont.Draw(Batch, White, speed, new Vector2(14, 82), 3, data.Speed == 0 ? amber : lcd);
            var money = "$" + data.Money.ToString("N0");
            VRPixelFont.Draw(Batch, White, money, new Vector2(WatchWidth - 14 - VRPixelFont.Measure(money, 3), 82), 3, lcd);
            if (data.Messages > 0)
            {
                var title = (data.Messages > 1 ? "! " + data.Messages + " " : "! ") + VRPixelFont.Fit(data.MessageTitle, 2, WatchWidth - 40);
                VRPixelFont.Draw(Batch, White, VRPixelFont.Fit(title, 2, WatchWidth - 28), new Vector2(14, 114), 2, amber);
                VRPixelFont.Draw(Batch, White, "TRIGGER: READ", new Vector2(14, 136), 2, amber * 0.6f);
            }
            Batch.End();

            // ---- needs, with the action queue
            gd.SetRenderTarget(Needs);
            gd.Clear(Color.Transparent);
            Batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
            Panel(new Rectangle(0, 0, NeedsWidth, NeedsHeight), bg, NeedsHover >= PointedAtDisplay ? Color.White : frame);
            VRPixelFont.Draw(Batch, White, VRPixelFont.Fit((data.SimName ?? "").ToUpperInvariant(), 2, NeedsWidth - 24), new Vector2(12, 8), 2, frame);

            for (int i = 0; i < VRHudData.MaxQueue; i++)
            {
                var slot = new Rectangle(QueueLeft + i * QueueStride, QueueTop, QueueSlot, QueueSlot);
                Batch.Draw(White, slot, Color.Black * 0.45f);
                if (i >= data.QueueCount) continue;
                var item = data.Queue[i];
                if (item.Icon != null)
                {
                    // like the game's queue: wide icons hold two pictures side by side; use the first
                    var source = item.Icon.Width / (float)item.Icon.Height < 1.1f
                        ? new Rectangle(0, 0, item.Icon.Width, item.Icon.Height)
                        : new Rectangle(0, 0, item.Icon.Width / 2, item.Icon.Height);
                    Batch.Draw(item.Icon, slot, source, item.Cancelled ? Color.White * 0.35f : Color.White);
                }
                if (item.Cancelled)
                {
                    // a red bar across: being cancelled
                    Batch.Draw(White, new Rectangle(slot.X, slot.Center.Y - 2, slot.Width, 4), new Color(230, 60, 50));
                }
                var border = i == NeedsHover ? Color.White : item.Active ? frame : Color.White * 0.25f;
                Border(slot, border, i == NeedsHover || item.Active ? 2 : 1);
            }
            var current = data.QueueCount > 0 ? data.Queue[0].Name : "";
            var hovered = NeedsHover >= 0 && NeedsHover < data.QueueCount ? data.Queue[NeedsHover] : null;
            var label = hovered != null ? "CANCEL " + hovered.Name : current.Length > 0 ? current : "NOTHING QUEUED";
            VRPixelFont.Draw(Batch, White, VRPixelFont.Fit(label.ToUpperInvariant(), 2, NeedsWidth - 24), new Vector2(12, 78), 2,
                hovered != null ? new Color(255, 120, 110) : current.Length > 0 ? lcd : Color.White * 0.4f);

            for (int i = 0; i < 8; i++)
            {
                var y = MotivesTop + i * 27;
                VRPixelFont.Draw(Batch, White, MotiveNames[i], new Vector2(12, y + 4), 2, Color.White * 0.9f);
                var bar = new Rectangle(104, y, NeedsWidth - 116, 20);
                Batch.Draw(White, bar, new Color(0, 0, 0) * 0.6f);
                var fraction = MathHelper.Clamp((data.Motives[i] + 100f) / 200f, 0, 1);
                var fill = new Rectangle(bar.X + 2, bar.Y + 2, (int)((bar.Width - 4) * fraction), bar.Height - 4);
                Batch.Draw(White, fill, MotiveColour(fraction));
            }
            var hint = NeedsHover >= 0 ? "TRIGGER: CANCEL IT" : "TRIGGER: VR MENU";
            VRPixelFont.Draw(Batch, White, hint, new Vector2(12, NeedsHeight - 22), 2, NeedsHover >= PointedAtDisplay ? Color.White : frame * 0.6f);
            Batch.End();
            gd.SetRenderTarget(null);
        }

        private void Panel(Rectangle r, Color fill, Color border)
        {
            Batch.Draw(White, r, fill);
            Border(r, border, 3);
        }

        private void Border(Rectangle r, Color colour, int thickness)
        {
            Batch.Draw(White, new Rectangle(r.X, r.Y, r.Width, thickness), colour);
            Batch.Draw(White, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), colour);
            Batch.Draw(White, new Rectangle(r.X, r.Y, thickness, r.Height), colour);
            Batch.Draw(White, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), colour);
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
