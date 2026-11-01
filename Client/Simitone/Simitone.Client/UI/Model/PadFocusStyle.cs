using System;
using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common;
using Simitone.Client.UI.Controls;
using Microsoft.Xna.Framework;

namespace Simitone.Client.UI.Model
{
    /// <summary>
    /// Simitone's gamepad focus highlight, in place of the engine's plain outline, with the Simitone note (from the
    /// logo) bobbing over the focused element:
    /// <list type="bullet">
    /// <item>a button with a hover look of its own (catalog and panel buttons, the choices in a popup) is shown in that
    /// hover state, as the mouse would show it;</item>
    /// <item>anything else (lists, carousels, text boxes, houses, round buttons whose hover is only a slight grow) gets
    /// a soft mint frame (Simitone's highlight colour) that breathes gently and pops out when focus arrives.</item>
    /// </list>
    /// Screens can ask for the frame on everything (<see cref="AlwaysFrame"/>): the neighbourhood and CAS do.
    /// It also records what it drew, for the focus tooltip.
    /// </summary>
    public static class PadFocusStyle
    {
        public static UIElement Focus { get; private set; }
        public static Rectangle FocusRect { get; private set; }
        public static int FocusFrame { get; private set; } = -1;

        /// <summary>Set by a navigator while it draws, for the frame even on buttons that have a hover look.</summary>
        public static bool AlwaysFrame;

        private static UIButton Hovered; //the button forced into its hover state for the pad
        private static int HoveredFrame = -1;

        /// <summary>Called first thing each update: lets go of a button forced into hover if focus has gone.</summary>
        public static void BeginFrame()
        {
            if (Hovered != null && HoveredFrame < PadPrompts.Frame - 1) ReleaseHover();
        }

        /// <summary>Whether a button shows hover differently from normal (stencil buttons recolour; others need a hover image).</summary>
        public static bool HasHoverLook(UIButton button)
        {
            if (button is UIElasticButton) return false; //only grows a little
            if (button is UIStencilButton || button is UITwoStateButton) return true;
            return button.ImageStates >= 3;
        }

        private static void SetHover(UIButton button)
        {
            if (Hovered != button)
            {
                ReleaseHover();
                if (button.ForceState != -1) return; //something else is driving its look
                button.ForceState = 2;
                Hovered = button;
            }
            HoveredFrame = PadPrompts.Frame;
        }

        private static void ReleaseHover()
        {
            if (Hovered != null && Hovered.ForceState == 2) Hovered.ForceState = -1;
            Hovered = null;
        }

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
            var button = element as UIButton;
            if (!AlwaysFrame && button != null && HasHoverLook(button))
            {
                SetHover(button); //its own hover look shows the focus
            }
            else
            {
                ReleaseHover();
                //a faint navy shadow first, so the mint frame still shows against light buttons and backgrounds
                var shadow = frame;
                shadow.Inflate((int)(2 * dpi), (int)(2 * dpi));
                PadDraw.NineSliceScreen(batch, PadGraphics.GetGlow(gd), PadGraphics.GlowCorner, shadow, PadDraw.Ink * 0.45f, dpi);
                PadDraw.NineSliceScreen(batch, PadGraphics.GetGlow(gd), PadGraphics.GlowCorner, frame, PadDraw.Accent * pulse, dpi);
            }

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
