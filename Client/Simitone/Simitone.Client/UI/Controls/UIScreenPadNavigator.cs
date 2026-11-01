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

        /// <summary>Button prompts for the prompt bar while this navigator has the pad, or null for none.</summary>
        public Func<PadPrompt[]> Prompts;

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
            //panels opened after it (e.g. the house panel) would draw over its outline: stay on top
            if (Parent is UIContainer parent)
            {
                var siblings = parent.GetChildren();
                if (siblings.Count > 0 && siblings[siblings.Count - 1] != this) parent.Add(this);
            }

            var free = state.Pad.Connected && GlobalSettings.Default.GamePadEnabled
                && !state.PadTextEntry
                && state.InputManager?.GetFocus() == null
                && !GameFacade.Screens.HasModalDialog;
            var scope = free ? ScopeProvider?.Invoke() : null;
            if (scope == null || !Navigator.Update(state, scope))
            {
                Navigator.Clear();
                SetFocused(null);
                return;
            }
            state.PadCaptured = true; //the virtual cursor isn't needed here
            //what was activated may have taken this navigator off the screen (Enter Lot): then it's done
            if (!(Parent is UIContainer owner) || !owner.GetChildren().Contains(this))
            {
                Navigator.Clear();
                SetFocused(null);
                return;
            }
            SetFocused(Navigator.Focused);
            if (Prompts != null) PadPrompts.Set(Prompts());
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
        }
    }
}
