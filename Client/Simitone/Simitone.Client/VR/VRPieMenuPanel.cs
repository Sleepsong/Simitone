using System;
using System.Collections.Generic;
using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.HIT;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Panels;
using Simitone.VR;

namespace Simitone.Client.VR
{
    public class VRPieItem
    {
        public string Name;
        /// <summary>Opens a further level of choices.</summary>
        public bool Category;
        /// <summary>Shown in yellow, as the game shows some choices.</summary>
        public bool Highlight;
    }

    /// <summary>What the VR pie menu shows and does: the game's open pie menu (see <see cref="VRGamePie"/>).</summary>
    public interface IVRPieSource
    {
        /// <summary>The choices at the level shown.</summary>
        IReadOnlyList<VRPieItem> Items { get; }
        /// <summary>The level's name (its button in the middle goes back), or null at the top.</summary>
        string LevelName { get; }
        /// <summary>The Sim's head, shown in the middle at the top level (null for none).</summary>
        Texture2D Head { get; }
        /// <summary>Changes whenever the level does.</summary>
        int Version { get; }
        void Choose(int index);
        /// <summary>Up a level, or closes the menu at the top.</summary>
        void Back();
        void Cancel();
        /// <summary>Turns the Sim's head towards a point of the menu (menu units from its middle).</summary>
        void Look(Vector2 at);
    }

    /// <summary>
    /// The pie menu, made for VR: the game's own pie (its buttons, colours and layout, with your Sim's head in the
    /// middle looking at the choice you point at) at a size you can read in a headset, floating over what you pointed
    /// at. The trigger chooses; the grip, or the middle button, goes back a level; the trigger anywhere else closes it.
    /// </summary>
    public class VRPieMenuPanel : VRUIPanel
    {
        public const int PanelWidth = 1000, PanelHeight = 1100;
        /// <summary>Panel pixels per unit of the game's pie layout.</summary>
        public const float Scale = 2f;
        /// <summary>The panel's width at 1 m away (it's sized with its distance, so it always looks the same size).</summary>
        public const float WidthPerMetre = 0.9f;

        public IVRPieSource Source;

        private readonly UIContainer Pie;
        private readonly List<UIButton> Buttons = new List<UIButton>();
        private readonly TextStyle ButtonStyle, HighlightStyle, BackStyle;
        private IVRPieSource ShownSource;
        private int ShownVersion = -1;
        private int HoverIndex = -1;
        private float Grow;

        public VRPieMenuPanel() : base(PanelWidth, PanelHeight, WidthPerMetre)
        {
            Add(new Backdrop(this));
            Pie = new UIContainer { Position = new Vector2(PanelWidth / 2f, PanelHeight / 2f), ScaleX = Scale, ScaleY = Scale };
            Add(Pie);
            // (as UIPieMenu)
            ButtonStyle = new TextStyle
            {
                Font = GameFacade.MainFont,
                VFont = GameFacade.VectorFont,
                Size = 12,
                Color = new Color(0xA5, 0xC3, 0xD6),
                SelectedColor = new Color(0x00, 0xFF, 0xFF),
                CursorColor = new Color(255, 255, 255)
            };
            HighlightStyle = ButtonStyle.Clone();
            HighlightStyle.Color = Color.Yellow;
            BackStyle = ButtonStyle.Clone();
            BackStyle.Color = BackStyle.SelectedColor;
        }

        /// <summary>Over what you pointed at (<paramref name="anchor"/>, tracking space), facing you, at a readable distance.</summary>
        public void PlaceAt(Vector3 anchor, VRPose head)
        {
            var yaw = Quaternion.CreateFromAxisAngle(Vector3.Up, VRMath.Yaw(head.Orientation));
            var forward = Vector3.Transform(-Vector3.UnitZ, yaw);
            var toAnchor = anchor - head.Position;
            var distance = toAnchor.Length();
            var direction = distance > 0.05f ? toAnchor / distance : forward;
            var d = MathHelper.Clamp(distance - 0.25f, 0.7f, 1.5f);
            PanelPosition = head.Position + direction * d;
            PanelOrientation = Quaternion.CreateFromRotationMatrix(Microsoft.Xna.Framework.Matrix.CreateWorld(Vector3.Zero, direction,
                Math.Abs(direction.Y) > 0.95f ? forward : Vector3.Up));
            WidthMetres = d * WidthPerMetre;
            Grow = 0;
        }

        protected override void Refresh(UpdateState state)
        {
            var source = Source;
            if (source == null) return;
            if (source != ShownSource || source.Version != ShownVersion)
            {
                ShownSource = source;
                ShownVersion = source.Version;
                Rebuild(source);
            }
            // (opens with a quick grow, as the game's does)
            Grow = Math.Min(1, Grow + 6f * SimitoneVR.FrameDelta);
            var s = Scale * (0.85f + 0.15f * (1 - (1 - Grow) * (1 - Grow)));
            Pie.ScaleX = Pie.ScaleY = s;
        }

        private void Rebuild(IVRPieSource source)
        {
            foreach (var b in Buttons) Pie.Remove(b);
            Buttons.Clear();
            HoverIndex = -1;
            var items = source.Items;
            var tex = TextureGenerator.GetPieButtonImg(GameFacade.GraphicsDevice);

            // the game's layout (UIPieMenu.RenderMenu): up to 8 around a circle of radius 60, the rest stacked above
            // and below, and the level's name in the middle to go back
            int dirConfig = items.Count > 4 ? 8 : items.Count > 2 ? 4 : 2;
            bool top = true;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var index = i;
                var but = new UIButton
                {
                    Caption = item.Name + (i >= 8 && item.Category ? "..." : ""),
                    CaptionStyle = item.Highlight ? HighlightStyle : ButtonStyle,
                    ImageStates = 1,
                    Texture = tex
                };
                but.AutoMargins = 4;
                if (i < dirConfig)
                {
                    double dir = (double)i / dirConfig * Math.PI * 2;
                    float sx = (float)(Math.Sin(dir) * 60), sy = (float)(Math.Cos(dir) * -60);
                    if (i == 0) but.Position = new Vector2(sx - but.Width / 2, sy - but.Size.Y);
                    else if (i == dirConfig / 2) but.Position = new Vector2(sx - but.Width / 2, sy);
                    else if (i < dirConfig / 2) but.Position = new Vector2(sx, sy - but.Size.Y / 2);
                    else but.Position = new Vector2(sx - but.Width, sy - but.Size.Y / 2);
                }
                else
                {
                    var row = (i - 8) / 2;
                    but.Position = new Vector2(-but.Width / 2, top ? -60 - but.Size.Y * (row + 2) : 60 + but.Size.Y * (row + 1));
                    top = !top;
                }
                but.OnButtonClick += _ => { Click(); source.Choose(index); };
                but.OnButtonHover += _ =>
                {
                    HoverIndex = index;
                    source.Look(but.Position + new Vector2(but.Width / 2f, but.Size.Y / 2f));
                    HITVM.Get()?.PlaySoundEvent(UISounds.PieMenuHighlight);
                    SimitoneVR.Runtime?.Haptic(true, 0.1f, 0.006f);
                };
                but.OnButtonExit += _ => { if (HoverIndex == index) { HoverIndex = -1; source.Look(Vector2.Zero); } };
                Pie.Add(but);
                Buttons.Add(but);
            }
            if (source.LevelName != null)
            {
                var back = new UIButton { Caption = source.LevelName, CaptionStyle = BackStyle, ImageStates = 1, Texture = tex };
                back.AutoMargins = 4;
                back.Position = new Vector2(-back.Width / 2, -back.Size.Y / 2);
                back.OnButtonClick += _ => { Click(); source.Back(); };
                Pie.Add(back);
                Buttons.Add(back);
            }
        }

        private static void Click() => SimitoneVR.Runtime?.Haptic(true, 0.25f, 0.012f);

        /// <summary>Behind the buttons: the game's dark round backdrop, and the Sim's head at the top level.</summary>
        /// <remarks>(A child: the panel renders its children into its texture, not through its own Draw.)</remarks>
        private class Backdrop : UIContainer
        {
            private readonly VRPieMenuPanel Owner;
            public Backdrop(VRPieMenuPanel owner) { Owner = owner; }

            public override void Draw(UISpriteBatch batch)
            {
                var s = Owner.Pie.ScaleX;
                var centre = Owner.Pie.Position;
                var bg = TextureGenerator.GetPieBG(batch.GraphicsDevice);
                var bgSize = 200 * s * (0.6f + 0.4f * Owner.Grow);
                DrawLocalTexture(batch, bg, null, centre - new Vector2(bgSize / 2), new Vector2(bgSize / bg.Width));
                var source = Owner.ShownSource;
                var head = source != null && source.LevelName == null ? source.Head : null;
                if (head != null && !head.IsDisposed)
                    DrawLocalTexture(batch, head, null, centre - new Vector2(100 * s), new Vector2(200 * s / head.Width, 200 * s / head.Height));
            }
        }
    }

    /// <summary>The game's open pie menu (<see cref="UIPieMenu"/>) as a source for the VR pie menu.</summary>
    public class VRGamePie : IVRPieSource
    {
        public readonly UIPieMenu Menu;
        private readonly UILotControl Control;
        private readonly List<VRPieItem> ItemList = new List<VRPieItem>();
        private UIPieMenuItem ShownLevel;
        private int VersionNumber;

        public VRGamePie(UIPieMenu menu, UILotControl control)
        {
            Menu = menu;
            Control = control;
        }

        private void Poll()
        {
            var level = Menu.CurrentItem;
            if (level == ShownLevel) return;
            ShownLevel = level;
            VersionNumber++;
            ItemList.Clear();
            foreach (var child in level.Children)
                ItemList.Add(new VRPieItem { Name = child.Name, Category = child.Category, Highlight = child.ColorMod > 0 });
        }

        public IReadOnlyList<VRPieItem> Items { get { Poll(); return ItemList; } }
        public string LevelName => Menu.AtTop ? null : Menu.CurrentItem.Name;
        public Texture2D Head => Menu.AtTop ? Menu.HeadTexture : null;
        public int Version { get { Poll(); return VersionNumber; } }

        public void Choose(int index)
        {
            var children = Menu.CurrentItem.Children;
            if (index >= 0 && index < children.Count) Menu.Choose(children[index]);
        }

        public void Back()
        {
            if (Menu.AtTop) Control.CancelPie();
            else Menu.Back();
        }

        public void Cancel() => Control.CancelPie();
        public void Look(Vector2 at) => Menu.HeadLook = at;
    }
}
