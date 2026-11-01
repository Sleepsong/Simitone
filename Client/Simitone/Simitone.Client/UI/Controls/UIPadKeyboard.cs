using System;
using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.HIT;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Model;

namespace Simitone.Client.UI.Controls
{
    /// <summary>
    /// On-screen keyboard for the gamepad. It appears while a text box has keyboard focus and the pad is the device in
    /// use, on any screen or dialog (it is the UI layer's overlay, so it's updated before everything else and drawn on
    /// top). While shown it owns the pad (<see cref="UpdateState.PadTextEntry"/>).
    ///
    /// D-pad/left stick: move between keys. A: press the key. B: delete (on an empty box, close). Y: space.
    /// X: shift. Start or the Done key: close. Closing releases the text box's focus; typing on a real keyboard
    /// still works throughout.
    /// </summary>
    public class UIPadKeyboard : UIElement
    {
        private const int KeySize = 48;
        private const int Gap = 6;
        private const int Margin = 14;
        private const int PreviewHeight = 34;
        private const int HintHeight = 22;
        private const int PreviewChars = 48;

        private readonly PadKeyboardLayout Layout = new PadKeyboardLayout();
        private UITextEdit Target;
        private bool Shift;
        private bool Shown;
        private float Time;
        private Vector2 Origin; //top-left of the panel, UI units
        private string Hints = "";

        private Texture2D Px;
        private TextStyle KeyStyle;
        private TextStyle PreviewStyle;
        private TextStyle HintStyle;

        /// <summary>Whether it's on screen this frame.</summary>
        public bool Active => Shown;

        public override void Update(UpdateState state)
        {
            base.Update(state);
            var focus = state.InputManager?.GetFocus() as UITextEdit;
            var show = focus != null && !focus.IsReadOnly && Attached(focus)
                && state.LastInputDevice == InputDevice.GamePad && GlobalSettings.Default.GamePadEnabled;
            if (!show)
            {
                Shown = false;
                Target = null;
                return;
            }

            if (focus != Target)
            {
                Target = focus;
                Layout.Reset();
                Shift = focus.CurrentText.Length == 0; //capitalise the first letter of a name
            }
            Shown = true;
            state.PadTextEntry = true;
            state.PadCaptured = true;
            Time += (float)state.Time.ElapsedGameTime.TotalSeconds;

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

            if (pad.Pressed(PadAction.Confirm)) Press(Layout.Current, state);
            if (pad.PressedRepeat(PadAction.Cancel))
            {
                if (Target.CurrentText.Length > 0) Target.PadBackspace();
                else if (pad.Pressed(PadAction.Cancel)) Close(state); //a fresh press, not a held delete running out
            }
            if (pad.PressedRepeat(PadAction.FaceTop)) TypeText(" ");
            if (pad.Pressed(PadAction.FaceLeft)) Shift = !Shift;
            if (pad.Pressed(PadAction.Start)) Close(state);

            var screen = GameFacade.Screens.CurrentUIScreen;
            var width = PanelWidth;
            var height = PanelHeight;
            Origin = new Vector2((screen.ScreenWidth - width) / 2f, screen.ScreenHeight - height - 16);
            ScaleX = ScaleY = FSOEnvironment.DPIScaleFactor;
            string P(PadAction action) => PadGlyphs.ActionName(action, pad);
            Hints = $"{P(PadAction.Confirm)} type    {P(PadAction.Cancel)} delete    {P(PadAction.FaceTop)} space    {P(PadAction.FaceLeft)} shift    {P(PadAction.Start)} done";
        }

        private void Press(PadKey key, UpdateState state)
        {
            switch (key.Action)
            {
                case PadKeyAction.Shift:
                    Shift = !Shift;
                    HITVM.Get()?.PlaySoundEvent(UISounds.Click);
                    break;
                case PadKeyAction.Backspace:
                    Target.PadBackspace();
                    break;
                case PadKeyAction.Done:
                    Close(state);
                    break;
                default:
                    TypeText(key.Type(Shift));
                    if (key.Action == PadKeyAction.Char) Shift = false; //shift applies to one key
                    break;
            }
        }

        private void TypeText(string text)
        {
            Target.PadType(text);
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
        }

        private void Close(UpdateState state)
        {
            state.InputManager.SetFocus(null);
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
        }

        /// <summary>Whether a text box is still part of the current screen (a dialog may have closed under it).</summary>
        private static bool Attached(UIElement element)
        {
            var screen = GameFacade.Screens.CurrentUIScreen;
            for (var e = element; e != null; e = e.Parent)
            {
                if (!e.Visible) return false;
                if (e == screen) return true;
            }
            return false;
        }

        private static int PanelWidth => PadKeyboardLayout.Columns * KeySize + (PadKeyboardLayout.Columns - 1) * Gap + Margin * 2;
        private int PanelHeight => Margin * 2 + PreviewHeight + Gap + Layout.Rows.Count * (KeySize + Gap) + HintHeight;

        public override void Draw(UISpriteBatch batch)
        {
            if (!Shown || Target == null) return;
            EnsureResources();

            var width = PanelWidth;
            var height = PanelHeight;
            Rect(batch, Origin, new Vector2(width, height), UIStyle.Current.Bg);

            //what's been typed (the box itself may be under the keyboard)
            var text = Target.CurrentText.Replace("\r", "").Replace('\n', ' ');
            if (text.Length > PreviewChars) text = "..." + text.Substring(text.Length - PreviewChars);
            var caret = ((int)(Time * 2) % 2 == 0) ? "_" : " ";
            var previewPos = Origin + new Vector2(Margin, Margin);
            Rect(batch, previewPos, new Vector2(width - Margin * 2, PreviewHeight), Color.Black * 0.5f);
            DrawLocalString(batch, text + caret, Vector2.Zero, PreviewStyle,
                ToRect(previewPos + new Vector2(10, 0), new Vector2(width - Margin * 2 - 20, PreviewHeight)), TextAlignment.Left | TextAlignment.Middle);

            var keysTop = previewPos.Y + PreviewHeight + Gap;
            for (int r = 0; r < Layout.Rows.Count; r++)
            {
                var row = Layout.Rows[r];
                for (int i = 0; i < row.Count; i++)
                {
                    var key = row[i];
                    var pos = new Vector2(Origin.X + Margin + key.Column * (KeySize + Gap), keysTop + r * (KeySize + Gap));
                    var size = new Vector2(key.Width * KeySize + (key.Width - 1) * Gap, KeySize);
                    var focused = r == Layout.Row && i == Layout.Index;
                    var on = key.Action == PadKeyAction.Shift && Shift;
                    var fill = focused ? UIStyle.Current.ActiveSelection : (on ? UIStyle.Current.SecondaryText * 0.6f : Color.White * 0.15f);
                    Rect(batch, pos, size, fill);

                    var label = (key.Action == PadKeyAction.Char) ? key.Type(Shift) : key.Label;
                    KeyStyle.Color = focused ? Color.Black : UIStyle.Current.Text;
                    DrawLocalString(batch, label, Vector2.Zero, KeyStyle, ToRect(pos, size), TextAlignment.Center | TextAlignment.Middle);
                }
            }

            var hintPos = new Vector2(Origin.X + Margin, keysTop + Layout.Rows.Count * (KeySize + Gap));
            DrawLocalString(batch, Hints, Vector2.Zero, HintStyle,
                ToRect(hintPos, new Vector2(width - Margin * 2, HintHeight)), TextAlignment.Center | TextAlignment.Middle);
        }

        private void Rect(UISpriteBatch batch, Vector2 pos, Vector2 size, Color color)
        {
            DrawLocalTexture(batch, Px, null, pos, size, color);
        }

        private static Rectangle ToRect(Vector2 pos, Vector2 size)
        {
            return new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y);
        }

        private void EnsureResources()
        {
            if (Px != null) return;
            Px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
            KeyStyle = TextStyle.DefaultLabel.Clone();
            KeyStyle.Size = 16;
            PreviewStyle = TextStyle.DefaultLabel.Clone();
            PreviewStyle.Size = 16;
            PreviewStyle.Color = UIStyle.Current.Text;
            HintStyle = TextStyle.DefaultLabel.Clone();
            HintStyle.Size = 11;
            HintStyle.Color = UIStyle.Current.Text * 0.8f;
        }
    }
}
