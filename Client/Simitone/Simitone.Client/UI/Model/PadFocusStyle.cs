using System;
using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common;
using Microsoft.Xna.Framework;

namespace Simitone.Client.UI.Model
{
    /// <summary>
    /// Simitone's gamepad focus highlight, in place of the engine's plain outline: a soft green frame with rounded
    /// corners that breathes gently and pops out a little when focus arrives, and the Simitone note (from the logo)
    /// bobbing over the focused element. It also records what it drew, for the focus tooltip.
    /// </summary>
    public static class PadFocusStyle
    {
        public static UIElement Focus { get; private set; }
        public static Rectangle FocusRect { get; private set; }
        public static int FocusFrame { get; private set; } = -1;

        public static void Install()
        {
            UIFocusNavigator.FocusRenderer = Render;
        }

        public static void Render(UISpriteBatch batch, UIElement element, Rectangle rect, float focusTime)
        {
            //while typing, the keyboard has the pad; the box it types into is underneath it anyway
            if (Simitone.Client.UI.Controls.UIPadOverlay.Instance?.Keyboard.Active ?? false) return;
            var gd = GameFacade.GraphicsDevice;
            var dpi = FSOEnvironment.DPIScaleFactor;
            var now = (float)(Environment.TickCount64 % 1000000) / 1000f;

            //pop out when focus arrives, then settle
            var pop = (focusTime < 0.2f) ? 1f - focusTime / 0.2f : 0f;
            var gap = (3f + 6f * pop) * dpi;
            var pad = (int)(PadGraphics.GlowInset * dpi + gap);
            var frame = rect;
            frame.Inflate(pad, pad);
            var pulse = 0.82f + 0.18f * (float)Math.Sin(now * 4f);
            PadDraw.NineSliceScreen(batch, PadGraphics.GetGlow(gd), PadGraphics.GlowCorner, frame, PadDraw.Accent * pulse, dpi);

            //the Simitone note, where there's room above and the element isn't a large area (a carousel, a whole list)
            var note = PadGraphics.GetNote(gd);
            var mh = 26f * dpi;
            var mw = mh * note.Width / note.Height;
            var bob = (float)Math.Sin(now * 3f) * 2.5f * dpi;
            var top = rect.Top - gap - mh - 3f * dpi + bob - 4f * dpi * pop;
            if (top > 2 * dpi && rect.Width < 420 * dpi)
            {
                batch.Draw(note, new Rectangle((int)(rect.Center.X - mw / 2), (int)top, (int)mw, (int)mh), PadDraw.Accent);
            }

            Focus = element;
            FocusRect = rect;
            FocusFrame = PadPrompts.Frame;
        }
    }
}
