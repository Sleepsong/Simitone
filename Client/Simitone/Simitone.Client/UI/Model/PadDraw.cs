using System;
using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Simitone.Client.UI.Model
{
    /// <summary>
    /// Drawing for the gamepad UI: nine-sliced panels and button glyphs. Glyphs are drawn shapes and text (coloured
    /// letter discs for Xbox naming, symbol discs for PlayStation naming, pills for the rest), not console artwork.
    /// </summary>
    public static class PadDraw
    {
        /// <summary>The highlight green (focus frame, focused keyboard key, the note).</summary>
        public static readonly Color Accent = new Color(112, 226, 80);
        public static readonly Color Panel = new Color(16, 34, 62) * 0.9f;
        public static readonly Color PanelEdge = new Color(120, 190, 255) * 0.35f;
        public static readonly Color Light = new Color(232, 236, 242);
        public static readonly Color Dark = new Color(24, 28, 38);

        private static TextStyle GlyphText;

        /// <summary>A panel: rounded, Sims blue, with a faint light edge. In <paramref name="host"/>'s local units.</summary>
        public static void DrawPanel(UIElement host, UISpriteBatch batch, Rectangle dest, float opacity = 1f)
        {
            var gd = GameFacade.GraphicsDevice;
            var edge = dest;
            edge.Inflate(1, 1);
            NineSlice(host, batch, PadGraphics.GetRounded(gd), PadGraphics.RoundedCorner, edge, PanelEdge * opacity);
            NineSlice(host, batch, PadGraphics.GetRounded(gd), PadGraphics.RoundedCorner, dest, Panel * opacity);
        }

        /// <summary>Nine-slice in <paramref name="host"/>'s local units. Corners keep their size unless the rectangle is too small.</summary>
        public static void NineSlice(UIElement host, UISpriteBatch batch, Texture2D tex, int corner, Rectangle dest, Color color)
        {
            ForEachSlice(tex, corner, dest, 1f, (src, dst) =>
            {
                if (dst.Width <= 0 || dst.Height <= 0) return;
                host.DrawLocalTexture(batch, tex, src, new Vector2(dst.X, dst.Y),
                    new Vector2(dst.Width / (float)src.Width, dst.Height / (float)src.Height), color);
            });
        }

        /// <summary>Nine-slice straight into screen space (for drawing that has no element, like the focus highlight).</summary>
        public static void NineSliceScreen(SpriteBatch batch, Texture2D tex, int corner, Rectangle dest, Color color, float cornerScale)
        {
            ForEachSlice(tex, corner, dest, cornerScale, (src, dst) =>
            {
                if (dst.Width > 0 && dst.Height > 0) batch.Draw(tex, dst, src, color);
            });
        }

        private static void ForEachSlice(Texture2D tex, int corner, Rectangle dest, float cornerScale, Action<Rectangle, Rectangle> draw)
        {
            var dc = (int)Math.Min(corner * cornerScale, Math.Min(dest.Width, dest.Height) / 2f);
            int[] sx = { 0, corner, tex.Width - corner, tex.Width };
            int[] sy = { 0, corner, tex.Height - corner, tex.Height };
            int[] dx = { dest.Left, dest.Left + dc, dest.Right - dc, dest.Right };
            int[] dy = { dest.Top, dest.Top + dc, dest.Bottom - dc, dest.Bottom };
            for (int j = 0; j < 3; j++)
            {
                for (int i = 0; i < 3; i++)
                {
                    draw(new Rectangle(sx[i], sy[j], sx[i + 1] - sx[i], sy[j + 1] - sy[j]),
                        new Rectangle(dx[i], dy[j], dx[i + 1] - dx[i], dy[j + 1] - dy[j]));
                }
            }
        }

        /// <summary>
        /// Draws a prompt's button(s) with its left edge at <paramref name="left"/>, vertically centred on
        /// <paramref name="centreY"/>, and returns the width used. Nothing is drawn if <paramref name="batch"/> is null
        /// (for measuring).
        /// </summary>
        public static float DrawPromptButtons(UIElement host, UISpriteBatch batch, PadPrompt prompt, GamePadInput pad, float left, float centreY, float size)
        {
            var set = PadGlyphs.Resolve(PadGlyphs.Setting, pad?.DisplayName);
            if (prompt.Stick != PadStick.None) return DrawStick(host, batch, prompt.Stick, left, centreY, size);
            var x = left;
            if (prompt.Action != null) x += DrawGlyph(host, batch, BoundButton(prompt.Action.Value), set, x, centreY, size);
            if (prompt.Action2 != null)
            {
                x += size * 0.08f;
                x += DrawGlyph(host, batch, BoundButton(prompt.Action2.Value), set, x, centreY, size);
            }
            return x - left;
        }

        private static Buttons? BoundButton(PadAction action)
        {
            var buttons = GamePadSettings.Bindings?.Get(action);
            return (buttons == null || buttons.Length == 0) ? (Buttons?)null : buttons[0];
        }

        /// <summary>One button glyph; returns its width.</summary>
        public static float DrawGlyph(UIElement host, UISpriteBatch batch, Buttons? button, PadGlyphSet set, float left, float centreY, float size)
        {
            var gd = GameFacade.GraphicsDevice;
            EnsureStyle();
            if (button == null)
            {
                DrawPill(host, batch, "?", left, centreY, size);
                return MeasurePill("?", size);
            }

            var b = button.Value;
            var face = b == Buttons.A || b == Buttons.B || b == Buttons.X || b == Buttons.Y;
            if (face)
            {
                if (batch != null)
                {
                    var disc = PadGraphics.GetDisc(gd);
                    var scale = size / disc.Width;
                    var pos = new Vector2(left, centreY - size / 2);
                    if (set == PadGlyphSet.PlayStation)
                    {
                        host.DrawLocalTexture(batch, disc, null, pos, new Vector2(scale), Light);
                        var shape = PadGraphics.GetShape(gd, ShapeFor(b));
                        host.DrawLocalTexture(batch, shape, null, pos, new Vector2(size / shape.Width), ShapeColour(b));
                    }
                    else
                    {
                        host.DrawLocalTexture(batch, disc, null, pos, new Vector2(scale), LetterColour(b));
                        GlyphText.Color = Color.White;
                        GlyphText.Size = Math.Max(8, (int)(size * 0.5f));
                        host.DrawLocalString(batch, b.ToString(), Vector2.Zero, GlyphText,
                            new Rectangle((int)left, (int)(centreY - size / 2), (int)size, (int)size), TextAlignment.Center | TextAlignment.Middle);
                    }
                }
                return size;
            }

            var label = PadGlyphs.ButtonName(b, set);
            if (b == Buttons.DPadUp || b == Buttons.DPadDown || b == Buttons.DPadLeft || b == Buttons.DPadRight)
            {
                if (batch != null)
                {
                    var plus = PadGraphics.GetDPad(gd);
                    host.DrawLocalTexture(batch, plus, null, new Vector2(left, centreY - size / 2), new Vector2(size / plus.Width), Light);
                }
                return size;
            }
            DrawPill(host, batch, label, left, centreY, size);
            return MeasurePill(label, size);
        }

        private static float DrawStick(UIElement host, UISpriteBatch batch, PadStick stick, float left, float centreY, float size)
        {
            var gd = GameFacade.GraphicsDevice;
            EnsureStyle();
            if (batch != null)
            {
                if (stick == PadStick.DPad)
                {
                    var plus = PadGraphics.GetDPad(gd);
                    host.DrawLocalTexture(batch, plus, null, new Vector2(left, centreY - size / 2), new Vector2(size / plus.Width), Light);
                }
                else
                {
                    var ring = PadGraphics.GetStickRing(gd);
                    host.DrawLocalTexture(batch, ring, null, new Vector2(left, centreY - size / 2), new Vector2(size / ring.Width), Light);
                    GlyphText.Color = Light;
                    GlyphText.Size = Math.Max(8, (int)(size * 0.42f));
                    host.DrawLocalString(batch, (stick == PadStick.Left) ? "L" : "R", Vector2.Zero, GlyphText,
                        new Rectangle((int)left, (int)(centreY - size / 2), (int)size, (int)size), TextAlignment.Center | TextAlignment.Middle);
                }
            }
            return size;
        }

        private static void DrawPill(UIElement host, UISpriteBatch batch, string label, float left, float centreY, float size)
        {
            if (batch == null) return;
            var width = MeasurePill(label, size);
            var height = size * 0.78f;
            var rect = new Rectangle((int)left, (int)(centreY - height / 2), (int)width, (int)height);
            NineSlice(host, batch, PadGraphics.GetRounded(GameFacade.GraphicsDevice), PadGraphics.RoundedCorner, rect, Light);
            GlyphText.Color = Dark;
            GlyphText.Size = Math.Max(8, (int)(size * 0.4f));
            host.DrawLocalString(batch, label, Vector2.Zero, GlyphText, rect, TextAlignment.Center | TextAlignment.Middle);
        }

        private static float MeasurePill(string label, float size)
        {
            EnsureStyle();
            GlyphText.Size = Math.Max(8, (int)(size * 0.4f));
            return Math.Max(size, GlyphText.MeasureString(label).X + size * 0.6f);
        }

        private static PadShape ShapeFor(Buttons b)
        {
            switch (b)
            {
                case Buttons.A: return PadShape.Cross;
                case Buttons.B: return PadShape.Circle;
                case Buttons.X: return PadShape.Square;
                default: return PadShape.Triangle;
            }
        }

        private static Color ShapeColour(Buttons b)
        {
            switch (b)
            {
                case Buttons.A: return new Color(78, 132, 214);
                case Buttons.B: return new Color(214, 70, 76);
                case Buttons.X: return new Color(204, 104, 176);
                default: return new Color(46, 168, 140);
            }
        }

        private static Color LetterColour(Buttons b)
        {
            switch (b)
            {
                case Buttons.A: return new Color(84, 168, 58);
                case Buttons.B: return new Color(206, 56, 56);
                case Buttons.X: return new Color(46, 110, 206);
                default: return new Color(226, 176, 26);
            }
        }

        private static void EnsureStyle()
        {
            if (GlyphText != null) return;
            GlyphText = TextStyle.DefaultLabel.Clone();
        }
    }
}
