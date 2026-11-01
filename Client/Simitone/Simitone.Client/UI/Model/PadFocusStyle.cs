using System;
using System.Collections.Generic;
using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common;
using Simitone.Client.UI.Controls;
using Microsoft.Xna.Framework;

namespace Simitone.Client.UI.Model
{
    /// <summary>
    /// Simitone's gamepad focus highlight, in place of the engine's plain outline:
    /// <list type="bullet">
    /// <item>a button with a hover look of its own (catalog and panel buttons, CAS buttons, the choices in a popup, the house
    /// panel's buttons, round buttons that grow) is put in its real hover state, exactly as the mouse would show it,
    /// caption colour included;</item>
    /// <item>a control that shows the focus itself (<see cref="IPadOwnFocus"/>, e.g. the family list's highlighted
    /// family) gets nothing more;</item>
    /// <item>anything else (other lists, carousels, text boxes, houses, portraits) gets a soft mint frame (Simitone's
    /// highlight colour) that breathes gently and pops out when focus arrives, with the Simitone note (from the logo)
    /// bobbing over it.</item>
    /// </list>
    /// It also records what it drew and where the other targets are, for the focus tooltip.
    /// </summary>
    /// <summary>A pad target that can show it has the focus by itself, so no frame is needed while it does.</summary>
    public interface IPadOwnFocus
    {
        bool PadShowsFocus { get; }
    }

    public static class PadFocusStyle
    {
        public static UIElement Focus { get; private set; }
        public static int FocusFrame { get; private set; } = -1;
        /// <summary>Screen area the focus look takes up at rest: the control, plus the frame and note if drawn.</summary>
        public static Rectangle FocusOuter { get; private set; }
        /// <summary>Screen rectangles of the other things the pad can move to, for labels to keep clear of.</summary>
        public static readonly List<Rectangle> Neighbours = new List<Rectangle>();

        private static UIButton Hovered; //the button forced into its hover state for the pad
        private static int HoveredFrame = -1;

        /// <summary>Called first thing each update: lets go of a button forced into hover if focus has gone.</summary>
        public static void BeginFrame()
        {
            if (Hovered != null && HoveredFrame < PadPrompts.Frame - 1) ReleaseHover();
        }

        /// <summary>
        /// Whether a button shows hover differently from normal: round buttons grow, stencil buttons recolour, others
        /// need a hover image.
        /// </summary>
        public static bool HasHoverLook(UIButton button)
        {
            if (button is UIElasticButton || button is UIStencilButton || button is UITwoStateButton) return true;
            return button.ImageStates >= 3;
        }

        private static void SetHover(UIButton button)
        {
            if (Hovered != button)
            {
                ReleaseHover();
                if (button.ForceState != -1) return; //something else is driving its look
                button.ForceState = 2; //the hover image, even if disabled or selected
                //and the hover state proper, as the mouse sets it: the caption takes its hover colour from this
                if (!button.IsDown) button.CurrentFrame = 2;
                Hovered = button;
            }
            HoveredFrame = PadPrompts.Frame;
        }

        private static void ReleaseHover()
        {
            if (Hovered != null)
            {
                if (Hovered.ForceState == 2) Hovered.ForceState = -1;
                //back to how the mouse left it
                if (Hovered.CurrentFrame == 2 && !Hovered.Hovered && !Hovered.IsDown) Hovered.CurrentFrame = 0;
            }
            Hovered = null;
        }

        /// <summary>
        /// Names an element in the focus tooltip bubble this frame without drawing any focus look, for something
        /// pointed at another way (the neighbourhood's pad cursor). <paramref name="others"/> are screen rectangles for
        /// the bubble to keep clear of. Call during update.
        /// </summary>
        public static void Label(UIElement element, Rectangle rect, IEnumerable<Rectangle> others)
        {
            Neighbours.Clear();
            if (others != null) Neighbours.AddRange(others);
            Focus = element;
            FocusOuter = rect;
            FocusFrame = PadPrompts.Frame;
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
            var outer = rect;
            if (button != null && HasHoverLook(button))
            {
                SetHover(button); //its own hover look shows the focus; nothing else is drawn
            }
            else if ((element as IPadOwnFocus)?.PadShowsFocus ?? false)
            {
                ReleaseHover(); //it shows the focus itself
            }
            else
            {
                ReleaseHover();
                //a faint navy shadow first, so the mint frame still shows against light buttons and backgrounds
                var shadow = frame;
                shadow.Inflate((int)(2 * dpi), (int)(2 * dpi));
                PadDraw.NineSliceScreen(batch, PadGraphics.GetGlow(gd), PadGraphics.GlowCorner, shadow, PadDraw.Ink * 0.45f, dpi);
                PadDraw.NineSliceScreen(batch, PadGraphics.GetGlow(gd), PadGraphics.GlowCorner, frame, PadDraw.Accent * pulse, dpi);
                var rest = (int)((PadGraphics.GlowInset + 3f) * dpi);
                outer.Inflate(rest, rest);

                //the Simitone note, where there's room above and the element isn't a large area (a carousel, a whole list)
                var note = PadGraphics.GetNote(gd);
                var mh = 26f * dpi;
                var mw = mh * note.Width / note.Height;
                var bob = (float)Math.Sin(now * 3f) * 2.5f * dpi;
                var top = rect.Top - gap - mh - 3f * dpi + bob - 4f * dpi * pop;
                if (top > 2 * dpi && rect.Width < 420 * dpi)
                {
                    batch.Draw(note, new Rectangle((int)(rect.Center.X - mw / 2), (int)top, (int)mw, (int)mh), PadDraw.Accent);
                    //at rest, at the top of its bob
                    var restTop = rect.Top - 3f * dpi - mh - 3f * dpi - 2.5f * dpi;
                    outer = Rectangle.Union(outer, new Rectangle((int)(rect.Center.X - mw / 2), (int)restTop, (int)mw, (int)(mh + 5f * dpi)));
                }
            }

            Neighbours.Clear();
            var navigator = UIFocusNavigator.Drawing;
            if (navigator != null)
            {
                foreach (var target in navigator.Targets)
                {
                    if (target != element && target.WillDraw()) Neighbours.Add(UIFocusNavigator.GlobalRect(target));
                }
            }

            Focus = element;
            FocusOuter = outer;
            FocusFrame = PadPrompts.Frame;
        }
    }
}
