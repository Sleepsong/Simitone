using System;
using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.Content;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;

namespace Simitone.Client.VR
{
    /// <summary>The game's own strings (they come with The Sims), with an English fallback.</summary>
    public static class VRText
    {
        public static string Get(string table, string id, string fallback)
        {
            try
            {
                var text = GameFacade.Strings?.GetString(table, id);
                return string.IsNullOrEmpty(text) ? fallback : text;
            }
            catch
            {
                return fallback;
            }
        }
    }

    /// <summary>Building blocks for the VR panels, in Simitone's own look.</summary>
    public static class VRUI
    {
        public static Texture2D CustomUI(string name) => Content.Get().CustomUI.Get(name).Get(GameFacade.GraphicsDevice);

        public static UILabel Label(string text, int size, Color colour, TextAlignment alignment = TextAlignment.Left | TextAlignment.Top)
        {
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = size;
            label.CaptionStyle.Color = colour;
            label.Alignment = alignment;
            label.Caption = text;
            return label;
        }

        /// <summary>A wrapped paragraph <paramref name="width"/> wide.</summary>
        public static UILabel Paragraph(string text, int size, Color colour, int width, int height)
        {
            var label = Label(text, size, colour);
            label.Wrapped = true;
            label.Size = new Vector2(width, height);
            return label;
        }

        /// <summary>
        /// Simitone's dialog look on a panel: the dark diagonal stripe as the body, and the white title bar with the
        /// title in black (as <see cref="UIMobileDialog"/>). Returns the title label.
        /// </summary>
        public static UILabel DialogFrame(UIContainer panel, int width, int height, string title)
        {
            panel.Add(new UIDiagonalStripe(new Point(width - 32, height), UIDiagonalStripeSide.RIGHT, UIStyle.Current.DialogBg));
            var titleBg = new UIImage(CustomUI("dialog_title_grad.png"));
            titleBg.SetSize(width - 32, 70);
            titleBg.Y = 10;
            panel.Add(titleBg);
            var titleLabel = Label(title, 37, UIStyle.Current.DialogTitle);
            titleLabel.Position = new Vector2(50, 15);
            panel.Add(titleLabel);
            return titleLabel;
        }

        /// <summary>The rounded outline used around Simitone's lists and category buttons, at any size.</summary>
        public static UIImage Outline(int width, int height)
        {
            var outline = new UIImage(CustomUI("cat_btn_base.png")).With9Slice(25, 25, 25, 25);
            outline.SetSize(width, height);
            return outline;
        }

        private static Texture2D HeadsetIcon;

        /// <summary>
        /// A VR headset as a white 65x65 stencil, matching the options panel's icons (save, neighbourhood, quit, free
        /// will): drawn here, as the game has no headset picture.
        /// </summary>
        public static Texture2D Headset(GraphicsDevice gd)
        {
            if (HeadsetIcon != null && !HeadsetIcon.IsDisposed) return HeadsetIcon;
            const int size = 65, samples = 4;
            var pixels = new Color[size * size];
            bool Inside(float x, float y)
            {
                // the visor: a rounded rectangle, with two lenses and a notch for the nose cut out; a strap each side
                bool RoundRect(float l, float t, float r, float b, float radius)
                {
                    var cx = Math.Max(l + radius, Math.Min(x, r - radius));
                    var cy = Math.Max(t + radius, Math.Min(y, b - radius));
                    return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius && x >= l && x <= r && y >= t && y <= b;
                }
                bool Circle(float cx, float cy, float r) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;
                var visor = RoundRect(9, 19, 56, 47, 9);
                var lenses = Circle(22.5f, 32, 7.5f) || Circle(42.5f, 32, 7.5f);
                var nose = y > 39 && Math.Abs(x - 32.5f) < (y - 39) * 0.9f + 1.5f;
                var strap = (x < 10 || x > 55) && y > 27 && y < 33 && x > 2 && x < 63;
                return (visor && !lenses && !nose) || strap;
            }
            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < samples; sy++)
                        for (int sx = 0; sx < samples; sx++)
                            if (Inside(px + (sx + 0.5f) / samples, py + (sy + 0.5f) / samples)) hits++;
                    var a = (byte)(255 * hits / (samples * samples));
                    pixels[py * size + px] = new Color(a, a, a, a); // premultiplied white, as the game's PNGs load
                }
            }
            HeadsetIcon = new Texture2D(gd, size, size);
            HeadsetIcon.SetData(pixels);
            return HeadsetIcon;
        }
    }

    /// <summary>A part of a texture, e.g. one state of a two-state button picture.</summary>
    public class UIVRSprite : UIElement
    {
        public Texture2D Texture;
        public Rectangle? Source;
        public Vector2 DrawSize;
        public Color Tint = Color.White;

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible || Texture == null) return;
            var source = Source ?? new Rectangle(0, 0, Texture.Width, Texture.Height);
            DrawLocalTexture(batch, Texture, source, Vector2.Zero, new Vector2(DrawSize.X / source.Width, DrawSize.Y / source.Height), Tint);
        }
    }

    /// <summary>
    /// A choice in the comfort setup: an outlined box (as Simitone's lists and buttons) with a name and a line or two
    /// of explanation. The current choice is filled in the list-selection green, like a selected list row.
    /// </summary>
    public class UIVRChoice : UIContainer
    {
        private readonly Vector2 BoxSize;
        private readonly UILabel NameLabel, HelpLabel;
        private bool Hover;
        public bool Selected;
        public event Action OnChosen;

        public UIVRChoice(string name, string help, int width, int height)
        {
            BoxSize = new Vector2(width, height);
            Add(VRUI.Outline(width, height));
            NameLabel = VRUI.Label(name, 19, UIStyle.Current.Text);
            NameLabel.Position = new Vector2(22, 12);
            Add(NameLabel);
            HelpLabel = VRUI.Paragraph(help, 15, UIStyle.Current.Text * 0.8f, width - 44, height - 46);
            HelpLabel.Position = new Vector2(22, 44);
            Add(HelpLabel);
            ListenForMouse(new Rectangle(0, 0, width, height), OnMouse);
        }

        private void OnMouse(UIMouseEventType type, UpdateState state)
        {
            switch (type)
            {
                case UIMouseEventType.MouseOver: Hover = true; break;
                case UIMouseEventType.MouseOut: Hover = false; break;
                case UIMouseEventType.MouseUp: if (Hover) OnChosen?.Invoke(); break;
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            var inset = new Vector2(6, 6);
            if (Selected) DrawLocalTexture(batch, px, null, inset, BoxSize - inset * 2, UIStyle.Current.SecondaryText);
            else if (Hover) DrawLocalTexture(batch, px, null, inset, BoxSize - inset * 2, Color.White * 0.12f);
            NameLabel.CaptionStyle.Color = Selected ? Color.Black : UIStyle.Current.Text;
            HelpLabel.CaptionStyle.Color = Selected ? Color.Black * 0.8f : UIStyle.Current.Text * 0.8f;
            base.Draw(batch);
        }
    }

    /// <summary>A key of the VR keyboard: an outlined box with its label, lit while pointed at and pressed.</summary>
    public class UIVRKey : UIContainer
    {
        private readonly Vector2 BoxSize;
        private readonly UILabel KeyLabel;
        private bool Hover;
        public bool Held { get; private set; }
        public bool Lit;
        public event Action<UIVRKey> OnPress;

        public UIVRKey(string label, int width, int height, int textSize)
        {
            BoxSize = new Vector2(width, height);
            Add(VRUI.Outline(width, height));
            KeyLabel = VRUI.Label(label, textSize, UIStyle.Current.Text, TextAlignment.Center | TextAlignment.Middle);
            KeyLabel.Size = BoxSize;
            Add(KeyLabel);
            ListenForMouse(new Rectangle(0, 0, width, height), OnMouse);
        }

        public string Caption
        {
            get => KeyLabel.Caption;
            set { if (KeyLabel.Caption != value) KeyLabel.Caption = value; }
        }

        private void OnMouse(UIMouseEventType type, UpdateState state)
        {
            switch (type)
            {
                case UIMouseEventType.MouseOver: Hover = true; break;
                case UIMouseEventType.MouseOut: Hover = Held = false; break;
                case UIMouseEventType.MouseDown:
                    Held = true;
                    OnPress?.Invoke(this);
                    break;
                case UIMouseEventType.MouseUp: Held = false; break;
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            var inset = new Vector2(5, 5);
            var on = Held || Lit;
            if (on) DrawLocalTexture(batch, px, null, inset, BoxSize - inset * 2, UIStyle.Current.SecondaryText);
            else if (Hover) DrawLocalTexture(batch, px, null, inset, BoxSize - inset * 2, Color.White * 0.15f);
            KeyLabel.CaptionStyle.Color = on ? Color.Black : UIStyle.Current.Text;
            base.Draw(batch);
        }
    }
}
