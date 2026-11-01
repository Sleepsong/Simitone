using System;
using System.Collections.Generic;
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
        private Rectangle BarRect; //this frame's prompt bar, in UI units (empty if none)
        private readonly List<Rectangle> Avoid = new List<Rectangle>();
        private readonly List<Rectangle> KeepClear = new List<Rectangle>();

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
            PadFocusStyle.BeginFrame();
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
                    BarRect = Rectangle.Empty;
                    if (!Keyboard.Active) DrawPromptBar(batch, screen); //first, so the tooltip can keep clear of it
                    if (!Keyboard.Active) DrawFocusTooltip(batch, screen);
                    if (ToastTime > 0 && !string.IsNullOrEmpty(Toast)) DrawToast(batch, screen.ScreenWidth, screen.ScreenHeight);
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
            BarRect = new Rectangle((int)x, (int)y, (int)total, (int)BarHeight);
            PadDraw.DrawPanel(this, batch, BarRect);

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

        /// <summary>
        /// Names the focused control (its tooltip) in a bubble beside it, as mouse hover would: above it if that's
        /// clear, otherwise on whichever side covers least of the other controls (and never the prompt bar).
        /// </summary>
        private void DrawFocusTooltip(UISpriteBatch batch, UIScreen screen)
        {
            if (PadFocusStyle.FocusFrame != PadPrompts.Frame) return;
            var text = PadFocusStyle.Focus?.Tooltip;
            if (string.IsNullOrEmpty(text)) return;

            var dpi = FSOEnvironment.DPIScaleFactor;
            Rectangle ToUI(Rectangle r) => new Rectangle((int)(r.X / dpi), (int)(r.Y / dpi),
                (int)Math.Ceiling(r.Width / dpi), (int)Math.Ceiling(r.Height / dpi));
            Avoid.Clear();
            foreach (var other in PadFocusStyle.Neighbours) Avoid.Add(ToUI(other));
            KeepClear.Clear();
            if (BarRect.Width > 0) KeepClear.Add(BarRect);

            var size = LabelStyle.MeasureString(text);
            var bubble = PadMath.PlaceLabel(ToUI(PadFocusStyle.FocusOuter), new Point((int)(size.X + 22f), 30), 6,
                screen.ScreenWidth, screen.ScreenHeight, Avoid, KeepClear);
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
