using System;
using System.Collections.Generic;
using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.VR;

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
    /// The displays on your wrists while in first person, made of Simitone's own UI (see <see cref="VRWatchPanel"/> and
    /// <see cref="VRNeedsPanel"/>), each attached to the controller's grip pose on the back of the wrist; by default
    /// shown only while you turn that wrist to look at it.
    /// </summary>
    public class VRWristHud
    {
        public readonly VRWatchPanel Watch = new VRWatchPanel();
        public readonly VRNeedsPanel Needs = new VRNeedsPanel();

        /// <summary>The watch is turned towards your eyes (you're looking at it), whatever <see cref="VRWristMode"/> is.</summary>
        public bool WatchFacing { get; private set; }

        /// <summary>Places and shows (or hides) the displays from the hand and head poses (tracking space). Once per frame.</summary>
        /// <param name="needsAllowed">False hides the needs display (the catalogue tablet is in that hand).</param>
        public void UpdatePlacement(VRInput input, VRPose head, VRWristMode mode, bool needsAllowed = true)
        {
            WatchFacing = false;
            bool watch = false, needs = false;
            if (mode != VRWristMode.Off && input != null && input.Active && head.Valid)
            {
                // grip space: -Z towards the fingers, +X away from the left palm / into the right palm.
                // The display sits on the back of the wrist, a little behind the grip, reading along the forearm.
                // The watch is on the pointing hand's wrist and the needs (which you point at) on the other: right and
                // left, swapped when left-handed.
                var rightWrist = (offset: new Vector3(-0.035f, 0.005f, 0.085f), yaw: -MathHelper.PiOver2);
                var leftWrist = (offset: new Vector3(0.035f, 0.005f, 0.085f), yaw: MathHelper.PiOver2);
                var watchSide = input.LeftHanded ? leftWrist : rightWrist;
                var needsSide = input.LeftHanded ? rightWrist : leftWrist;
                watch = Place(Watch, input.PointerGrip, head, watchSide.offset, watchSide.yaw, mode, out var watchFacing);
                needs = needsAllowed && Place(Needs, input.WalkGrip, head, needsSide.offset, needsSide.yaw, mode, out _);
                WatchFacing = watchFacing;
            }
            SetShown(Watch, watch);
            SetShown(Needs, needs);
        }

        public void HideAll()
        {
            WatchFacing = false;
            SetShown(Watch, false);
            SetShown(Needs, false);
        }

        private static void SetShown(VRUIPanel panel, bool shown)
        {
            if (shown && !panel.Shown) panel.Show();
            else if (!shown && panel.Shown) panel.Hide();
        }

        private static bool Place(VRUIPanel panel, VRPose grip, VRPose head, Vector3 offset, float yaw, VRWristMode mode, out bool facing)
        {
            facing = false;
            if (!grip.Valid) return false;
            panel.PanelOrientation = grip.Orientation * Quaternion.CreateFromAxisAngle(Vector3.Up, yaw);
            panel.PanelPosition = grip.Position + Vector3.Transform(offset, grip.Orientation);
            var normal = Vector3.Transform(Vector3.UnitZ, panel.PanelOrientation);
            var toHead = head.Position - panel.PanelPosition;
            var distance = toHead.Length();
            facing = distance > 1e-3f && distance < 0.9f && Vector3.Dot(normal, toHead / distance) > 0.55f; // turned towards your eyes
            return mode == VRWristMode.Always || facing;
        }

        public void Submit(VRRuntime runtime, ref VRFrameSubmission submission)
        {
            Watch.Submit(runtime, ref submission);
            Needs.Submit(runtime, ref submission);
        }

        public void Destroy()
        {
            Watch.Destroy();
            Needs.Destroy();
        }
    }

    /// <summary>
    /// The watch on your right wrist: Simitone's clock (its pill, time and current speed icon) and money display, and a
    /// line for messages waiting to be read (look at the watch and pull that hand's trigger).
    /// </summary>
    public class VRWatchPanel : VRUIPanel
    {
        private const float FaceScale = 1.4f;
        private readonly UIImage InnerBg;
        private readonly UILabel TimeLabel, TimeShadow, MoneyLabel, MessageLabel, MessageHint;
        private readonly UIVRSprite SpeedIcon;
        private readonly UIDiagonalStripe MessageStripe;
        private int LastSpeed = -2;

        public VRWatchPanel() : base((int)(228 * FaceScale), (int)(136 * FaceScale), 0.075f)
        {
            var face = new UIContainer { ScaleX = FaceScale, ScaleY = FaceScale };
            Add(face);

            // the clock, as UIClockPanel draws it when folded up
            var outer = new UIImage(VRUI.CustomUI("clockbg.png")).With9Slice(25, 25, 0, 0);
            outer.SetSize(196, 50);
            face.Add(outer);
            InnerBg = new UIImage(VRUI.CustomUI("clockinbg.png")).With9Slice(19, 19, 0, 0);
            InnerBg.SetSize(133, 38);
            InnerBg.Position = new Vector2(10, 6);
            face.Add(InnerBg);
            TimeShadow = VRUI.Label("", 15, Color.Black * 0.25f, TextAlignment.Middle | TextAlignment.Center);
            TimeShadow.Size = new Vector2(133, 38);
            TimeShadow.Position = InnerBg.Position + new Vector2(2);
            face.Add(TimeShadow);
            TimeLabel = VRUI.Label("", 15, Color.White, TextAlignment.Middle | TextAlignment.Center);
            TimeLabel.Size = new Vector2(133, 38);
            TimeLabel.Position = InnerBg.Position;
            face.Add(TimeLabel);
            SpeedIcon = new UIVRSprite { Position = new Vector2(151, 6), DrawSize = new Vector2(37, 40) };
            face.Add(SpeedIcon);

            // the money, as UIMoneyPanel draws it
            var money = new UIVRMoneyPill { Position = new Vector2(34, 60) };
            face.Add(money);
            MoneyLabel = VRUI.Label("", 15, UIStyle.Current.Text, TextAlignment.Center | TextAlignment.Middle);
            MoneyLabel.Size = new Vector2(128, 24);
            MoneyLabel.Position = money.Position;
            face.Add(MoneyLabel);

            // messages waiting
            MessageStripe = new UIDiagonalStripe(new Point(196, 44), UIDiagonalStripeSide.RIGHT, UIStyle.Current.Bg) { Position = new Vector2(0, 92) };
            face.Add(MessageStripe);
            MessageLabel = VRUI.Label("", 15, UIStyle.Current.NegMoney);
            MessageLabel.Position = new Vector2(10, 94);
            face.Add(MessageLabel);
            MessageHint = VRUI.Label("Pull the trigger to read", 12, UIStyle.Current.SecondaryText);
            MessageHint.Position = new Vector2(10, 116);
            face.Add(MessageHint);
        }

        protected override void Refresh(UpdateState state)
        {
            var data = SimitoneVR.HudProvider?.Invoke();
            if (data == null || !data.Valid) return;
            var hour = data.Hour % 12 == 0 ? 12 : data.Hour % 12;
            var time = $"{hour}:{data.Minute:00} {(data.Hour > 11 ? "PM" : "AM")}";
            if (TimeLabel.Caption != time) TimeLabel.Caption = TimeShadow.Caption = time;

            // speed: the clock panel's own speed pictures (1, 2, 3, and 4 = paused)
            var speed = data.Speed;
            if (speed != LastSpeed)
            {
                LastSpeed = speed;
                var picture = speed switch { 0 => 4, 1 => 1, 2 => 2, 3 => 3, _ => 0 };
                SpeedIcon.Visible = picture > 0;
                if (picture > 0)
                {
                    SpeedIcon.Texture = VRUI.CustomUI($"speedbtn_{picture}.png");
                    SpeedIcon.Source = new Rectangle(0, 0, SpeedIcon.Texture.Width / 2, SpeedIcon.Texture.Height);
                }
                InnerBg.Texture = VRUI.CustomUI(speed == 0 ? "clockinbg_pause.png" : "clockinbg.png");
            }
            var money = "§" + data.Money.ToString("##,#0");
            if (MoneyLabel.Caption != money) MoneyLabel.Caption = money;

            var messages = data.Messages > 0;
            MessageStripe.Visible = MessageLabel.Visible = MessageHint.Visible = messages;
            if (messages)
            {
                var text = MessageLabel.CaptionStyle.TruncateToWidth(data.MessageTitle ?? "", 176);
                if (MessageLabel.Caption != text) MessageLabel.Caption = text;
                var hint = data.Messages > 1 ? $"{data.Messages} waiting: pull the trigger" : "Pull the trigger to read";
                if (MessageHint.Caption != hint) MessageHint.Caption = hint;
            }
        }
    }

    /// <summary>The money display's pill (money_bg.png in three slices, tinted like the game's).</summary>
    public class UIVRMoneyPill : UIElement
    {
        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var bg = VRUI.CustomUI("money_bg.png");
            var tint = UIStyle.Current.Bg;
            DrawLocalTexture(batch, bg, new Rectangle(0, 0, 12, 24), Vector2.Zero, Vector2.One, tint);
            DrawLocalTexture(batch, bg, new Rectangle(12, 0, 12, 24), new Vector2(12, 0), new Vector2(8.666667f, 1), tint);
            DrawLocalTexture(batch, bg, new Rectangle(24, 0, 12, 24), new Vector2(116, 0), Vector2.One, tint);
        }
    }

    /// <summary>
    /// The display on your left wrist: the selected Sim's name, their action queue (the game's own queue icons: point at
    /// one and pull the trigger to cancel it), their needs (the game's need bars, in two columns as in The Sims) and a
    /// button for the VR options.
    /// </summary>
    public class VRNeedsPanel : VRUIPanel
    {
        private const int QueueTop = 34, MotivesTop = 250, RowHeight = 58;
        private const int PanelWidth = 420, DialTop = MotivesTop + 4 * RowHeight + 76, PanelHeight = DialTop + 142;
        private static readonly string[] MotiveNames = { "Hunger", "Comfort", "Hygiene", "Bladder", "Energy", "Fun", "Social", "Room" };

        private readonly UILabel NameLabel, ActionLabel;
        private readonly UIContainer QueueArea;
        private readonly List<(ushort uid, UIInteraction ui, bool cancelled)> QueueUI = new List<(ushort, UIInteraction, bool)>();
        private readonly UIMotiveBar[] Bars = new UIMotiveBar[8];
        private readonly UIVRSpeedDial Dial;

        public VRNeedsPanel() : base(PanelWidth, PanelHeight, 0.09f)
        {
            Add(new UIDiagonalStripe(new Point(PanelWidth - 32, PanelHeight), UIDiagonalStripeSide.RIGHT, UIStyle.Current.Bg));
            NameLabel = VRUI.Label("", 19, UIStyle.Current.Text);
            NameLabel.Position = new Vector2(16, 6);
            Add(NameLabel);

            QueueArea = new UIContainer { Position = new Vector2(0, QueueTop) };
            Add(QueueArea);
            ActionLabel = VRUI.Label("", 15, UIStyle.Current.SecondaryText);
            ActionLabel.Position = new Vector2(16, QueueTop + 140);
            Add(ActionLabel);
            var hint = VRUI.Label("Point and pull the trigger to cancel", 12, UIStyle.Current.Text * 0.6f);
            hint.Position = new Vector2(16, QueueTop + 164);
            Add(hint);

            for (int i = 0; i < 8; i++)
            {
                // the game's layout: Hunger, Comfort, Hygiene, Bladder in the first column; Energy, Fun, Social, Room in the second
                var x = 18 + (i / 4) * 190;
                var y = MotivesTop + (i % 4) * RowHeight;
                var label = VRUI.Label(VRText.Get("f102", (i + 1).ToString(), MotiveNames[i]), 15, UIStyle.Current.Text, TextAlignment.Bottom);
                label.Size = new Vector2(1);
                label.Position = new Vector2(x, y - 6);
                Add(label);
                Bars[i] = new UIMotiveBar { Position = new Vector2(x, y) };
                Add(Bars[i]);
            }

            var menuButton = new UICatButton(VRUI.Headset(GameFacade.GraphicsDevice))
            {
                Position = new Vector2(16, MotivesTop + 4 * RowHeight - 10)
            };
            menuButton.OnButtonClick += _ => SimitoneVR.RequestMenu();
            Add(menuButton);
            var menuLabel = VRUI.Label("VR Options", 19, UIStyle.Current.Text, TextAlignment.Left | TextAlignment.Middle);
            menuLabel.Size = new Vector2(200, 75);
            menuLabel.Position = new Vector2(100, MotivesTop + 4 * RowHeight - 10);
            Add(menuLabel);

            // the game's speed, as a dial: pause, normal, fast, fastest
            var speedLabel = VRUI.Label("Speed", 19, UIStyle.Current.Text, TextAlignment.Left | TextAlignment.Middle);
            speedLabel.Size = new Vector2(100, 60);
            speedLabel.Position = new Vector2(16, DialTop + 40);
            Add(speedLabel);
            Dial = new UIVRSpeedDial { Position = DialPosition };
            Dial.OnSet += speed => SimitoneVR.RequestSpeed(speed);
            Add(Dial);
        }

        private static readonly Vector2 DialPosition = new Vector2(150, DialTop);

        /// <summary>Where a speed's stop on the dial is (0 pause to 3 fastest), in the panel's pixels.</summary>
        public static Vector2 DialStop(int speed) => DialPosition + UIVRSpeedDial.Stop(speed);

        protected override void Refresh(UpdateState state)
        {
            var data = SimitoneVR.HudProvider?.Invoke();
            if (data == null || !data.Valid) return;
            if (NameLabel.Caption != data.SimName) NameLabel.Caption = data.SimName ?? "";
            Dial.Speed = data.Speed;
            for (int i = 0; i < 8; i++) Bars[i].MotiveValue = (int)data.Motives[i];
            var action = data.QueueCount > 0 ? data.Queue[0].Name : "";
            if (ActionLabel.Caption != action) ActionLabel.Caption = action;
            RefreshQueue(data);
        }

        /// <summary>The queue icons, placed as the game's queue places them (the running action big, the rest in a row).</summary>
        private void RefreshQueue(VRHudData data)
        {
            var same = QueueUI.Count == data.QueueCount;
            for (int i = 0; same && i < data.QueueCount; i++) same = QueueUI[i].uid == data.Queue[i].UID;
            if (!same)
            {
                foreach (var entry in QueueUI) QueueArea.Remove(entry.ui);
                QueueUI.Clear();
                for (int i = 0; i < data.QueueCount; i++)
                {
                    var item = data.Queue[i];
                    var ui = new UIInteraction(i == 0) { Icon = item.Icon };
                    ui.Position = i == 0 ? new Vector2(73, 73) : new Vector2(37 + 68 + i * 60, 41.5f);
                    var uid = item.UID;
                    ui.OnMouseEvent += _ => SimitoneVR.RequestQueueCancel(uid);
                    QueueArea.Add(ui);
                    QueueUI.Add((uid, ui, false));
                }
            }
            for (int i = 0; i < QueueUI.Count; i++)
            {
                var (uid, ui, cancelled) = QueueUI[i];
                if (ui.Icon != data.Queue[i].Icon) ui.Icon = data.Queue[i].Icon;
                if (data.Queue[i].Cancelled && !cancelled)
                {
                    ui.SetCancelled();
                    QueueUI[i] = (uid, ui, true);
                }
            }
        }
    }

    /// <summary>
    /// The game's speed as a dial: its four speed buttons (pause, normal, fast, fastest) around an arc, the current one
    /// lit and the needle pointing at it. Point at one and pull the trigger. Dimmed in buy and build, where the game is
    /// paused anyway.
    /// </summary>
    public class UIVRSpeedDial : UIContainer
    {
        private const float Radius = 78;
        private static readonly Vector2 Centre = new Vector2(120, 104);
        private static readonly string[] IconNames = { "speedbtn_4.png", "speedbtn_1.png", "speedbtn_2.png", "speedbtn_3.png" };
        private readonly Texture2D[] Icons = new Texture2D[4];
        private readonly bool[] Hover = new bool[4];
        /// <summary>0 paused, 1-3 speed, -1 buy/build/options.</summary>
        public int Speed = 1;
        public event Action<int> OnSet;

        public UIVRSpeedDial()
        {
            for (int i = 0; i < 4; i++)
            {
                Icons[i] = VRUI.CustomUI(IconNames[i]);
                var stop = i;
                var at = Stop(i);
                ListenForMouse(new Rectangle((int)at.X - 24, (int)at.Y - 26, 48, 52), (type, state) =>
                {
                    if (type == UIMouseEventType.MouseOver) Hover[stop] = true;
                    else if (type == UIMouseEventType.MouseOut) Hover[stop] = false;
                    else if (type == UIMouseEventType.MouseUp && Hover[stop] && Speed >= 0 && Speed != stop)
                    {
                        OnSet?.Invoke(stop);
                        FSO.HIT.HITVM.Get()?.PlaySoundEvent(FSO.Client.UI.Model.UISounds.Click);
                    }
                });
            }
        }

        /// <summary>Where a stop sits: on an arc over the knob, pause on the left to fastest on the right.</summary>
        public static Vector2 Stop(int i)
        {
            var angle = MathHelper.Pi - i * MathHelper.Pi / 3;
            return Centre + new Vector2((float)Math.Cos(angle), -(float)Math.Sin(angle)) * Radius;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var enabled = Speed >= 0;
            var dim = enabled ? 1f : 0.35f;
            var knob = VRUI.CustomUI("pswitch_icon_bg.png");
            var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            DrawLocalTexture(batch, knob, null, Centre - new Vector2(30), new Vector2(60f / knob.Width), new Color(104, 164, 184) * dim);
            if (enabled)
            {
                // the needle, from the knob to the current speed
                var to = Stop(Speed) - Centre;
                var angle = (float)Math.Atan2(to.Y, to.X);
                DrawLocalTexture(batch, px, null, Centre, new Vector2(Radius - 30, 6), UIStyle.Current.SecondaryText, angle, new Vector2(0, 0.5f));
            }
            for (int i = 0; i < 4; i++)
            {
                var icon = Icons[i];
                if (icon == null) continue;
                var lit = enabled && i == Speed;
                var frame = new Rectangle(lit ? icon.Width / 2 : 0, 0, icon.Width / 2, icon.Height);
                var scale = (Hover[i] && enabled ? 1.35f : 1.2f);
                var size = new Vector2(frame.Width, frame.Height) * scale;
                DrawLocalTexture(batch, icon, frame, Stop(i) - size / 2, new Vector2(scale), Color.White * dim);
            }
            base.Draw(batch);
        }
    }
}
