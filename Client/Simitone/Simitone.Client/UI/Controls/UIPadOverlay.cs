using System;
using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Screens;

namespace Simitone.Client.UI.Controls
{
    /// <summary>
    /// The gamepad layer over everything (the UI layer's overlay): the on-screen keyboard, the button prompt bar for
    /// whatever has the pad, a tooltip bubble naming the focused control, and short messages ("toasts"). Shown only
    /// while the pad is the device in use.
    /// </summary>
    public class UIPadOverlay : UIElement
    {
        public static UIPadOverlay Instance { get; private set; }

        public readonly UIPadKeyboard Keyboard = new UIPadKeyboard();

        private const float GlyphSize = 26f;
        private const float BarHeight = 40f;

        private bool PadInUse;
        private GamePadInput Pad;
        private string Toast;
        private float ToastTime;
        private TextStyle LabelStyle;
        private TextStyle ToastStyle;

        public UIPadOverlay()
        {
            Instance = this;
        }

        /// <summary>Shows a short message near the top of the screen (pad only).</summary>
        public static void ShowToast(string text, float seconds = 2.5f)
        {
            if (Instance == null) return;
            Instance.Toast = text;
            Instance.ToastTime = seconds;
        }

        public static bool ToastShowing => Instance != null && Instance.ToastTime > 0 && Instance.PadInUse;

        public override void Update(UpdateState state)
        {
            base.Update(state);
            PadPrompts.BeginFrame();
            ScaleX = ScaleY = FSOEnvironment.DPIScaleFactor;
            PadInUse = state.LastInputDevice == InputDevice.GamePad && GlobalSettings.Default.GamePadEnabled && state.Pad.Connected;
            Pad = state.Pad;
            if (ToastTime > 0) ToastTime -= (float)state.Time.ElapsedGameTime.TotalSeconds;
            Keyboard.Update(state);
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (PadInUse)
            {
                EnsureStyles();
                var screen = GameFacade.Screens.CurrentUIScreen;
                if (screen != null)
                {
                    if (!Keyboard.Active) DrawFocusTooltip(batch);
                    if (ToastTime > 0 && !string.IsNullOrEmpty(Toast)) DrawToast(batch, screen.ScreenWidth, screen.ScreenHeight);
                    if (!Keyboard.Active) DrawPromptBar(batch, screen);
                }
            }
            Keyboard.Draw(batch);
        }

        private void DrawPromptBar(UISpriteBatch batch, UIScreen screen)
        {
            PadPrompt[] items;
            var dialogs = GameFacade.Screens.DialogNavigator;
            if (dialogs.Scope != null && dialogs.Focused != null)
            {
                items = new[] { PadPrompt.Of(PadAction.Confirm, "Select"), PadPrompt.Of(PadAction.Cancel, "Back") };
            }
            else items = PadPrompts.Current;
            if (items.Length == 0) return;

            //measure
            const float pad = 14f, gap = 20f, labelGap = 7f;
            var widths = new float[items.Length];
            var total = pad * 2;
            for (int i = 0; i < items.Length; i++)
            {
                var icon = PadDraw.DrawPromptButtons(this, null, items[i], Pad, 0, 0, GlyphSize);
                widths[i] = icon + labelGap + LabelStyle.MeasureString(items[i].Label ?? "").X;
                total += widths[i] + ((i > 0) ? gap : 0);
            }

            //in a lot: bottom right, above the main panel when it's open (the clock and queue have the top, the mode
            //button and money the bottom left). Elsewhere: bottom centre.
            float x, y;
            if (screen is TS1GameScreen game && !game.InNeighbourhood && game.Frontend != null)
            {
                var panel = game.Frontend.MainPanel;
                var bottom = panel.PanelActive ? panel.Y : screen.ScreenHeight;
                y = bottom - BarHeight - 10f;
                x = screen.ScreenWidth - total - 15f;
            }
            else
            {
                y = screen.ScreenHeight - BarHeight - 10f;
                x = (screen.ScreenWidth - total) / 2f;
            }
            PadDraw.DrawPanel(this, batch, new Rectangle((int)x, (int)y, (int)total, (int)BarHeight));

            x += pad;
            var centreY = y + BarHeight / 2f;
            for (int i = 0; i < items.Length; i++)
            {
                var icon = PadDraw.DrawPromptButtons(this, batch, items[i], Pad, x, centreY, GlyphSize);
                DrawLocalString(batch, items[i].Label ?? "", Vector2.Zero, LabelStyle,
                    new Rectangle((int)(x + icon + labelGap), (int)y, (int)(widths[i] - icon - labelGap + 4), (int)BarHeight),
                    TextAlignment.Left | TextAlignment.Middle);
                x += widths[i] + gap;
            }
        }

        /// <summary>Names the focused control (its tooltip) in a bubble over it, as mouse hover would.</summary>
        private void DrawFocusTooltip(UISpriteBatch batch)
        {
            if (PadFocusStyle.FocusFrame != PadPrompts.Frame) return;
            var text = PadFocusStyle.Focus?.Tooltip;
            if (string.IsNullOrEmpty(text)) return;

            var dpi = FSOEnvironment.DPIScaleFactor;
            var rect = PadFocusStyle.FocusRect;
            var size = LabelStyle.MeasureString(text);
            var width = size.X + 22f;
            const float height = 30f;
            var centreX = rect.Center.X / dpi;
            var above = rect.Top / dpi - 40f - height; //clear of the note
            var y = (above > 4) ? above : rect.Bottom / dpi + 12f;
            var screen = GameFacade.Screens.CurrentUIScreen;
            var x = Math.Max(4f, Math.Min(screen.ScreenWidth - width - 4f, centreX - width / 2f));
            var bubble = new Rectangle((int)x, (int)y, (int)width, (int)height);
            PadDraw.DrawPanel(this, batch, bubble);
            DrawLocalString(batch, text, Vector2.Zero, LabelStyle, bubble, TextAlignment.Center | TextAlignment.Middle);
        }

        private void DrawToast(UISpriteBatch batch, int screenWidth, int screenHeight)
        {
            var size = ToastStyle.MeasureString(Toast);
            var width = size.X + 40f;
            const float height = 44f;
            var fade = Math.Min(1f, ToastTime / 0.3f);
            var rect = new Rectangle((int)((screenWidth - width) / 2f), (int)(screenHeight * 0.2f), (int)width, (int)height);
            PadDraw.DrawPanel(this, batch, rect, fade);
            ToastStyle.Color = PadDraw.Ink * fade;
            DrawLocalString(batch, Toast, Vector2.Zero, ToastStyle, rect, TextAlignment.Center | TextAlignment.Middle);
        }

        private void EnsureStyles()
        {
            if (LabelStyle != null) return;
            LabelStyle = TextStyle.DefaultLabel.Clone();
            LabelStyle.Size = 13;
            LabelStyle.Color = PadDraw.Ink;
            ToastStyle = TextStyle.DefaultLabel.Clone();
            ToastStyle.Size = 16;
            ToastStyle.Color = PadDraw.Ink;
        }
    }
}
