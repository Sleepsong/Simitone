using System;
using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Model;

namespace Simitone.Client.UI.Controls
{
    /// <summary>
    /// Gamepad navigation for a screen outside the lot (neighbourhood, CAS): runs a <see cref="UIFocusNavigator"/>
    /// over whatever <see cref="ScopeProvider"/> returns this frame, and draws its focus outline. Stands down while a
    /// modal dialog (the UI layer navigates those), a focused text box or the on-screen keyboard has the pad.
    /// Add it to the screen last, so the outline draws on top.
    /// </summary>
    public class UIScreenPadNavigator : UIElement
    {
        public readonly UIFocusNavigator Navigator = new UIFocusNavigator();
        public Func<UIElement> ScopeProvider;

        /// <summary>
        /// Button prompts shown along the bottom of the screen while the pad is in use, or null for none. Use
        /// <see cref="PadGlyphs.ActionName(PadAction, GamePadInput)"/> to name buttons.
        /// </summary>
        public Func<GamePadInput, string> Prompts;
        private string PromptText;
        private Texture2D Px;
        private TextStyle PromptStyle;

        /// <summary>Focus moved: (previous, current); either may be null.</summary>
        public event Action<UIElement, UIElement> FocusChanged;
        private UIElement LastFocused;

        public UIScreenPadNavigator(Func<UIElement> scopeProvider)
        {
            ScopeProvider = scopeProvider;
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            var free = state.Pad.Connected && GlobalSettings.Default.GamePadEnabled
                && !state.PadTextEntry
                && state.InputManager?.GetFocus() == null
                && !GameFacade.Screens.HasModalDialog;
            var scope = free ? ScopeProvider?.Invoke() : null;
            PromptText = null;
            if (scope == null || !Navigator.Update(state, scope))
            {
                Navigator.Clear();
                SetFocused(null);
                return;
            }
            state.PadCaptured = true; //the virtual cursor isn't needed here
            SetFocused(Navigator.Focused);
            if (state.LastInputDevice == InputDevice.GamePad) PromptText = Prompts?.Invoke(state.Pad);
        }

        private void SetFocused(UIElement focused)
        {
            if (focused == LastFocused) return;
            var previous = LastFocused;
            LastFocused = focused;
            FocusChanged?.Invoke(previous, focused);
        }

        public override void Draw(UISpriteBatch batch)
        {
            Navigator.Draw(batch);
            if (string.IsNullOrEmpty(PromptText)) return;

            if (Px == null)
            {
                Px = TextureGenerator.GetPxWhite(GameFacade.GraphicsDevice);
                PromptStyle = TextStyle.DefaultLabel.Clone();
                PromptStyle.Size = 12;
                PromptStyle.Color = UIStyle.Current.Text;
            }
            var screen = GameFacade.Screens.CurrentUIScreen;
            var size = PromptStyle.MeasureString(PromptText);
            var bar = new Rectangle((int)((screen.ScreenWidth - size.X) / 2 - 12), screen.ScreenHeight - 30, (int)size.X + 24, 24);
            DrawLocalTexture(batch, Px, null, new Vector2(bar.X, bar.Y), new Vector2(bar.Width, bar.Height), UIStyle.Current.Bg);
            DrawLocalString(batch, PromptText, Vector2.Zero, PromptStyle, bar, TextAlignment.Center | TextAlignment.Middle);
        }
    }
}
