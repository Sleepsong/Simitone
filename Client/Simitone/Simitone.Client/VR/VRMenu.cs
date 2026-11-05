using System;
using System.Collections.Generic;
using FSO.Client.UI.Model;
using FSO.HIT;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.VR;
using Simitone.VR.Graphics;

namespace Simitone.Client.VR
{
    /// <summary>
    /// The VR options menu: a small floating panel for the comfort and control options, so they can be changed in the
    /// headset while playing. Point at it with the right controller: the trigger steps an option forward, the grip
    /// steps it back, and pressing either beside the menu closes it. Changes apply at once and are saved to vr.ini.
    ///
    /// Open it with the left controller's menu button (Touch-style controllers), by pointing at your left wrist (the
    /// needs display) and pulling the trigger, or with F9 on the desktop keyboard.
    /// </summary>
    public class VRMenu : IDisposable
    {
        private const int Width = 640, HeaderHeight = 78, RowHeight = 42, FooterHeight = 14;
        private const int Dot = 3;
        private const float WidthMetres = 0.6f;

        private class Row
        {
            public string Label;
            public Func<string> Value;
            /// <summary>+1 next value, -1 previous; null for a button (Close).</summary>
            public Action<int> Change;
        }

        private readonly List<Row> Rows = new List<Row>();
        public bool Open { get; private set; }
        /// <summary>Distance (metres) from the controller to where it points on the menu, or 0 if it's not on it.</summary>
        public float PointerDistance { get; private set; }
        private int Hover = -1;
        private bool Armed;
        private bool Dirty = true;
        private Vector3 Position;
        private Quaternion Orientation = Quaternion.Identity;

        private RenderTarget2D Target;
        private VRSwapchain Chain;
        private SpriteBatch Batch;
        private Texture2D White;

        private int Height => HeaderHeight + Rows.Count * RowHeight + FooterHeight;
        private static VRSettings Settings => SimitoneVR.Settings;

        private static readonly (VRTurnMode mode, float degrees, string name)[] Turns =
        {
            (VRTurnMode.Snap, 30, "SNAP 30"), (VRTurnMode.Snap, 45, "SNAP 45"), (VRTurnMode.Snap, 90, "SNAP 90"),
            (VRTurnMode.Smooth, 90, "SMOOTH SLOW"), (VRTurnMode.Smooth, 120, "SMOOTH"), (VRTurnMode.Smooth, 180, "SMOOTH FAST"),
        };
        private static readonly float[] Heights = { 0, 0.15f, 0.3f, 0.45f, 0.6f };
        private static readonly float[] RenderScales = { 0.5f, 0.6f, 0.7f, 0.75f, 0.8f, 0.9f, 1f, 1.1f, 1.25f, 1.5f };

        public VRMenu()
        {
            Add("SIM ACTIONS", () => Name(Settings.SimActionView), d => Settings.SimActionView = Cycle(Settings.SimActionView, d));
            Add("VIGNETTE", () => Name(Settings.Vignette), d => Settings.Vignette = Cycle(Settings.Vignette, d));
            Add("VIGNETTE MOVING", () => Name(Settings.VignetteWhenMoving), d => Settings.VignetteWhenMoving = Cycle(Settings.VignetteWhenMoving, d));
            Add("MOVEMENT", () => Settings.Movement switch
            {
                VRMovement.Stick => "STICK",
                VRMovement.Arc => "ARC",
                _ => "STICK + ARC"
            }, d => Settings.Movement = Cycle(Settings.Movement, d));
            Add("TURNING", () => Turns[TurnIndex()].name, d =>
            {
                var turn = Turns[Wrap(TurnIndex() + d, Turns.Length)];
                Settings.TurnMode = turn.mode;
                if (turn.mode == VRTurnMode.Snap) Settings.SnapTurnDegrees = turn.degrees;
                else Settings.SmoothTurnDegreesPerSecond = turn.degrees;
            });
            Add("WALK TOWARDS", () => Settings.MoveDirection == VRMoveDirection.Head ? "HEAD" : "LEFT HAND",
                d => Settings.MoveDirection = Cycle(Settings.MoveDirection, d));
            Add("PLAY AS", () => SimitoneVR.PlayMode == VRPlayMode.Sim ? "YOUR SIM" : "OBSERVER", d =>
            {
                SimitoneVR.PlayMode = SimitoneVR.PlayMode == VRPlayMode.Sim ? VRPlayMode.Observer : VRPlayMode.Sim;
                Settings.PlayMode = SimitoneVR.PlayMode;
            });
            Add("EXTRA HEIGHT", () => Settings.HeightOffset < 0.01f ? "OFF" : "+" + Settings.HeightOffset.ToString("0.00") + " M",
                d => Settings.HeightOffset = Heights[Wrap(Nearest(Heights, Settings.HeightOffset) + d, Heights.Length)]);
            Add("WRIST DISPLAYS", () => Name(Settings.WristDisplays), d => Settings.WristDisplays = Cycle(Settings.WristDisplays, d));
            Add("MESSAGES", () => Settings.WatchMessages ? "ON WATCH" : "POP UP", d => Settings.WatchMessages = !Settings.WatchMessages);
            Add("STICK WHEN BUSY", () => Settings.StickCancelsActions ? "TAKES OVER" : "WAITS", d => Settings.StickCancelsActions = !Settings.StickCancelsActions);
            Add("ACTION BUBBLE", () => Settings.ActionBubble ? "ON" : "OFF", d => Settings.ActionBubble = !Settings.ActionBubble);
            Add("BUY AND BUILD", () => Settings.VRBuildMode ? "IN VR" : "FLAT SCREEN", d => Settings.VRBuildMode = !Settings.VRBuildMode);
            Add("RENDER SCALE", () => (int)Math.Round(Settings.RenderScale * 100) + "%",
                d => SimitoneVR.SetRenderScale(RenderScales[Wrap(Nearest(RenderScales, Settings.RenderScale) + d, RenderScales.Length)]));
            Rows.Add(new Row { Label = "CLOSE", Value = () => "", Change = null });
        }

        private void Add(string label, Func<string> value, Action<int> change)
            => Rows.Add(new Row { Label = label, Value = value, Change = change });

        private static string Name<T>(T value) where T : struct, Enum => value.ToString().ToUpperInvariant();

        private static T Cycle<T>(T value, int direction) where T : struct, Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            return values[Wrap(Array.IndexOf(values, value) + direction, values.Length)];
        }

        private static int Wrap(int i, int n) => ((i % n) + n) % n;

        private static int Nearest(float[] values, float value)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++) if (Math.Abs(values[i] - value) < Math.Abs(values[best] - value)) best = i;
            return best;
        }

        private static int TurnIndex()
        {
            var s = Settings;
            int best = 0;
            float bestError = float.MaxValue;
            for (int i = 0; i < Turns.Length; i++)
            {
                if (Turns[i].mode != s.TurnMode) continue;
                var error = Math.Abs(Turns[i].degrees - (s.TurnMode == VRTurnMode.Snap ? s.SnapTurnDegrees : s.SmoothTurnDegreesPerSecond));
                if (error < bestError) { bestError = error; best = i; }
            }
            return best;
        }

        // ------------------------------------------------------------------ open, close, pointing

        /// <summary>Opens the menu in front of you (tracking space).</summary>
        public void Show(VRPose head, VRInput input)
        {
            if (!head.Valid) return;
            var yaw = Quaternion.CreateFromAxisAngle(Vector3.Up, VRMath.Yaw(head.Orientation));
            var forward = Vector3.Transform(-Vector3.UnitZ, yaw);
            // a little below eye level, tilted back to face you
            Orientation = yaw * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.ToRadians(-12));
            Position = head.Position + forward * 0.8f - new Vector3(0, 0.12f, 0);
            Open = true;
            Armed = input == null || (!input.Select.Down && !input.Secondary.Down);
            Hover = -1;
            Dirty = true;
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
        }

        public void Close()
        {
            if (!Open) return;
            Open = false;
            PointerDistance = 0;
            Hover = -1;
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
        }

        /// <summary>Points the right controller at the menu and handles its buttons. Call once per frame while open.</summary>
        public void Update(VRInput input, VRRuntime runtime)
        {
            PointerDistance = 0;
            if (!Open || input == null || !input.Active) return;
            if (!input.Select.Down && !input.Secondary.Down) Armed = true;

            var hover = -1;
            var onMenu = false;
            var aim = input.RightAim.Valid ? input.RightAim : input.LeftAim;
            if (aim.Valid)
            {
                var normal = Vector3.Transform(Vector3.UnitZ, Orientation);
                var denom = Vector3.Dot(aim.Forward, normal);
                if (Math.Abs(denom) > 1e-4f)
                {
                    var t = Vector3.Dot(Position - aim.Position, normal) / denom;
                    if (t > 0)
                    {
                        var local = Vector3.Transform(aim.Position + aim.Forward * t - Position, Quaternion.Inverse(Orientation));
                        var heightMetres = WidthMetres * Height / Width;
                        var u = local.X / WidthMetres + 0.5f;
                        var v = 0.5f - local.Y / heightMetres;
                        if (u >= 0 && u <= 1 && v >= 0 && v <= 1)
                        {
                            onMenu = true;
                            PointerDistance = t;
                            var row = (int)Math.Floor((v * Height - HeaderHeight) / RowHeight);
                            if (row >= 0 && row < Rows.Count) hover = row;
                        }
                    }
                }
            }
            if (hover != Hover)
            {
                Hover = hover;
                Dirty = true;
                if (hover >= 0) runtime?.Haptic(true, 0.1f, 0.01f);
            }
            if (!Armed) return;

            int direction = input.Select.Pressed ? 1 : input.Secondary.Pressed ? -1 : 0;
            if (direction == 0) return;
            if (!onMenu)
            {
                Close(); // pressed beside the menu
                return;
            }
            if (hover < 0) return;
            var r = Rows[hover];
            if (r.Change == null)
            {
                Close();
                return;
            }
            r.Change(direction);
            Settings.Save();
            SimitoneVR.Log("VR menu: " + r.Label + " = " + r.Value());
            Dirty = true;
            runtime?.Haptic(true, 0.3f, 0.02f);
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
        }

        // ------------------------------------------------------------------ drawing

        /// <summary>Redraws the menu when something changed. Call outside the main draw (it switches render targets).</summary>
        public void Render(GraphicsDevice gd)
        {
            if (!Open || (!Dirty && Target != null)) return;
            Dirty = false;
            Batch ??= new SpriteBatch(gd);
            if (White == null)
            {
                White = new Texture2D(gd, 1, 1);
                White.SetData(new[] { Color.White });
            }
            if (Target == null || Target.Height != Height)
            {
                Target?.Dispose();
                Target = new RenderTarget2D(gd, Width, Height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            }

            var bg = new Color(16, 20, 28) * 0.94f;
            var frame = new Color(90, 200, 255);
            var text = Color.White * 0.92f;
            var value = new Color(140, 255, 190);

            gd.SetRenderTarget(Target);
            gd.Clear(Color.Transparent);
            Batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            Batch.Draw(White, new Rectangle(0, 0, Width, Height), bg);
            Border(new Rectangle(0, 0, Width, Height), frame, 3);
            VRPixelFont.Draw(Batch, White, "VR OPTIONS", new Vector2(20, 16), 4, frame);
            VRPixelFont.Draw(Batch, White, "TRIGGER: NEXT   GRIP: BACK", new Vector2(20, 54), 2, text * 0.6f);
            for (int i = 0; i < Rows.Count; i++)
            {
                var row = Rows[i];
                var y = HeaderHeight + i * RowHeight;
                var rect = new Rectangle(8, y + 2, Width - 16, RowHeight - 4);
                if (i == Hover) Batch.Draw(White, rect, frame * 0.25f);
                var textY = y + (RowHeight - VRPixelFont.GlyphHeight * Dot) / 2;
                if (row.Change == null)
                {
                    VRPixelFont.Draw(Batch, White, row.Label, new Vector2((Width - VRPixelFont.Measure(row.Label, Dot)) / 2, textY), Dot,
                        i == Hover ? frame : text);
                    continue;
                }
                VRPixelFont.Draw(Batch, White, row.Label, new Vector2(20, textY), Dot, text);
                var shown = i == Hover ? "< " + row.Value() + " >" : row.Value();
                VRPixelFont.Draw(Batch, White, shown, new Vector2(Width - 20 - VRPixelFont.Measure(shown, Dot), textY), Dot, value);
            }
            Batch.End();
            gd.SetRenderTarget(null);
        }

        private void Border(Rectangle r, Color colour, int thickness)
        {
            Batch.Draw(White, new Rectangle(r.X, r.Y, r.Width, thickness), colour);
            Batch.Draw(White, new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), colour);
            Batch.Draw(White, new Rectangle(r.X, r.Y, thickness, r.Height), colour);
            Batch.Draw(White, new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), colour);
        }

        /// <summary>Copies the menu into its swapchain and adds it to the frame, on top of everything else.</summary>
        public void Submit(VRRuntime runtime, ref VRFrameSubmission submission)
        {
            if (!Open || Target == null) return;
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

        public void Dispose()
        {
            Chain?.Dispose();
            Chain = null;
            Target?.Dispose();
            White?.Dispose();
            Batch?.Dispose();
        }
    }
}
