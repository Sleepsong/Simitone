using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.HIT;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Model;
using System;

namespace Simitone.Client.UI.DualScreen
{
    /// <summary>
    /// A keyboard on the second screen, for typing there (the second screen's window can't take the keyboard focus, so
    /// the main screen's keyboards can't reach it). The same keys, look and controls as the main screen's pad keyboard
    /// (<see cref="Simitone.Client.UI.Controls.UIPadKeyboard"/>), without the number row so the keys are tall enough
    /// to tap:
    /// - touch: tap a key (it's pressed when the finger lifts, so a slip off it types nothing);
    /// - pad (<see cref="PadUpdate"/>): D-pad moves, A presses, B deletes (on nothing, closes), Y space, X shift,
    ///   Start closes.
    /// It only reports keys; whoever owns it keeps the text (<see cref="OnKey"/>).
    /// </summary>
    public class UIDualKeyboard : UIElement
    {
        private const int Margin = 6, Gap = 5;

        public readonly PadKeyboardLayout Layout = new PadKeyboardLayout(numberRow: false);
        /// <summary>The next letter is a capital (shift applies to one key, as on the main screen).</summary>
        public bool Shift;
        /// <summary>Show which key the pad is on (only while the pad is in use).</summary>
        public bool ShowFocus;
        /// <summary>A key was pressed: a character, Space, Delete or Done (Shift is handled here).</summary>
        public Action<PadKey> OnKey;
        /// <summary>Whether there's text to delete: the pad's B deletes, and on nothing it closes (sends Done).</summary>
        public Func<bool> HasText;

        private readonly float W, H;
        private PadKey Pressed; //under a finger
        private float Time;

        public UIDualKeyboard(float width, float height)
        {
            W = width;
            H = height;
            ListenForMouse(new Rectangle(0, 0, (int)width, (int)height), OnMouse);
        }

        private float KeyW => (W - Margin * 2 - Gap * (PadKeyboardLayout.Columns - 1)) / PadKeyboardLayout.Columns;
        private float KeyH => (H - Margin * 2 - Gap * (Layout.Rows.Count - 1)) / Layout.Rows.Count;

        private Rectangle KeyRect(int row, PadKey key)
        {
            var x = Margin + key.Column * (KeyW + Gap);
            var y = Margin + row * (KeyH + Gap);
            return new Rectangle((int)x, (int)y, (int)(key.Width * KeyW + (key.Width - 1) * Gap), (int)KeyH);
        }

        private PadKey KeyAt(Vector2 local)
        {
            for (int r = 0; r < Layout.Rows.Count; r++)
            {
                foreach (var key in Layout.Rows[r])
                {
                    var rect = KeyRect(r, key);
                    rect.Inflate(Gap / 2 + 1, Gap / 2 + 1); //the gaps count as the nearest key
                    if (rect.Contains((int)local.X, (int)local.Y)) return key;
                }
            }
            return null;
        }

        private void OnMouse(UIMouseEventType type, UpdateState state)
        {
            switch (type)
            {
                case UIMouseEventType.MouseDown:
                    Pressed = KeyAt(GetMousePosition(state.MouseState));
                    break;
                case UIMouseEventType.MouseOut:
                    Pressed = null;
                    break;
                case UIMouseEventType.MouseUp:
                    var key = Pressed;
                    Pressed = null;
                    if (key != null) Press(key);
                    break;
            }
        }

        /// <summary>The pad's controls, while this keyboard has the pad.</summary>
        public void PadUpdate(UpdateState state)
        {
            var pad = state.Pad;
            for (int i = 0; i < 4; i++)
            {
                var direction = (PadDirection)i;
                if (pad.NavPressedRepeat(direction))
                {
                    Layout.Move(direction);
                    HITVM.Get()?.PlaySoundEvent(UISounds.Click);
                }
            }
            if (pad.Pressed(PadAction.Confirm)) Press(Layout.Current);
            if (pad.PressedRepeat(PadAction.Cancel))
            {
                if (HasText?.Invoke() ?? false) Send(new PadKey { Action = PadKeyAction.Backspace });
                else if (pad.Pressed(PadAction.Cancel)) Send(new PadKey { Action = PadKeyAction.Done }); //a fresh press, not a held delete running out
            }
            if (pad.PressedRepeat(PadAction.FaceTop)) Send(new PadKey { Action = PadKeyAction.Space });
            if (pad.Pressed(PadAction.FaceLeft)) Press(new PadKey { Action = PadKeyAction.Shift });
            if (pad.Pressed(PadAction.Start)) Send(new PadKey { Action = PadKeyAction.Done });
        }

        private void Press(PadKey key)
        {
            if (key.Action == PadKeyAction.Shift)
            {
                Shift = !Shift;
                HITVM.Get()?.PlaySoundEvent(UISounds.Click);
                return;
            }
            Send(key);
            if (key.Action == PadKeyAction.Char) Shift = false; //shift applies to one key
        }

        private void Send(PadKey key)
        {
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
            OnKey?.Invoke(key);
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            Time += (float)state.Time.ElapsedGameTime.TotalSeconds;
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var gd = GameFacade.GraphicsDevice;
            var rounded = PadGraphics.GetRounded(gd);
            PadDraw.DrawPanel(this, batch, new Rectangle(0, 0, (int)W, (int)H));

            var style = TextStyle.DefaultLabel.Clone();
            style.Size = 16;
            style.Color = PadDraw.Ink;
            Rectangle? focusedRect = null;
            for (int r = 0; r < Layout.Rows.Count; r++)
            {
                var row = Layout.Rows[r];
                for (int i = 0; i < row.Count; i++)
                {
                    var key = row[i];
                    var rect = KeyRect(r, key);
                    var focused = ShowFocus && r == Layout.Row && i == Layout.Index;
                    if (focused) focusedRect = rect;
                    var on = key.Action == PadKeyAction.Shift && Shift;
                    var fill = (focused || key == Pressed) ? PadDraw.Accent : (on ? new Color(196, 246, 234) : new Color(228, 235, 245));
                    PadDraw.NineSlice(this, batch, rounded, PadGraphics.RoundedCorner, rect, fill);
                    var label = (key.Action == PadKeyAction.Char) ? key.Type(Shift) : key.Label;
                    DrawLocalString(batch, label, Vector2.Zero, style, rect, TextAlignment.Center | TextAlignment.Middle);
                }
            }
            if (focusedRect != null)
            {
                var glow = focusedRect.Value;
                glow.Inflate(PadGraphics.GlowInset + 2, PadGraphics.GlowInset + 2);
                var pulse = 0.8f + 0.2f * (float)Math.Sin(Time * 5);
                PadDraw.NineSlice(this, batch, PadGraphics.GetGlow(gd), PadGraphics.GlowCorner, glow, PadDraw.Accent * pulse);
            }
        }
    }
}
