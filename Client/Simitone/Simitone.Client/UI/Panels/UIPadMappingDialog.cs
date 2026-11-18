using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// Options > Controller > Button Mapping: which button does what. Choose an action (A, or click it), then press the
    /// button for it. It's bound to that action, and the action that had that button takes this one's old one, so no
    /// action is ever left without a button (Confirm and Cancel can't be lost). Nothing pressed within a few seconds
    /// leaves it as it was. Reset to Defaults puts them all back. Saved to gamepad.ini as they change.
    /// </summary>
    public class UIPadMappingDialog : UIMobileDialog, IPadScope
    {
        private class Row
        {
            public PadAction Action;
            public UILabel Label;
            public UIBigButton Button;
        }

        private List<Row> Rows = new List<Row>();
        private UILabel NoteLabel;
        private UIBigButton ResetButton;
        private UIBigButton DoneButton;

        private const float RowScale = 0.55f;
        private const float FooterScale = 0.7f;
        private const int RowsPerColumn = 8;
        private const float CaptureTimeout = 5f; //seconds to press a button before it's left as it was

        private PadAction? Capturing; //waiting for the button for this action
        private bool WaitRelease; //done: waiting for the buttons to be let go, before the pad works as usual again
        private float CaptureTime;
        private GamePadInput Pad;
        private string PadName;
        private string Note = "Choose an action, then press the button for it.";
        private bool NoteWarn;

        public UIPadMappingDialog()
        {
            Caption = "Button Mapping";
            SetHeight(700);

            foreach (var entry in PadOptions.ActionNames)
            {
                var row = new Row() { Action = entry.Action };
                row.Label = new UILabel();
                row.Label.Caption = entry.Name;
                row.Label.Alignment = TextAlignment.Left | TextAlignment.Middle;
                row.Label.CaptionStyle = row.Label.CaptionStyle.Clone();
                row.Label.CaptionStyle.Size = 18;
                row.Label.CaptionStyle.Color = UIStyle.Current.DialogText;
                Add(row.Label);

                row.Button = new UIBigButton(false);
                row.Button.ScaleX = row.Button.ScaleY = RowScale;
                row.Button.OnButtonClick += (btn) => Begin(row.Action);
                Add(row.Button);
                Rows.Add(row);
            }

            NoteLabel = new UILabel();
            NoteLabel.Alignment = TextAlignment.Center | TextAlignment.Middle;
            NoteLabel.CaptionStyle = NoteLabel.CaptionStyle.Clone();
            NoteLabel.CaptionStyle.Size = 15;
            Add(NoteLabel);

            ResetButton = new UIBigButton(false);
            ResetButton.Caption = "Reset to Defaults";
            ResetButton.ScaleX = ResetButton.ScaleY = FooterScale;
            ResetButton.OnButtonClick += (btn) => Reset();
            Add(ResetButton);

            DoneButton = new UIBigButton(true);
            DoneButton.Caption = "Done";
            DoneButton.ScaleX = DoneButton.ScaleY = FooterScale;
            DoneButton.OnButtonClick += (btn) => { EndCapture(); Close(); };
            Add(DoneButton);

            Layout();
            Refresh();
        }

        /// <summary>Starts waiting for the button for an action.</summary>
        private void Begin(PadAction action)
        {
            if (Capturing != null) return;
            if (Pad == null || !Pad.Connected)
            {
                SetNote("Connect a controller to set its buttons.", true);
                return;
            }
            Capturing = action;
            CaptureTime = 0;
            WaitRelease = false;
            Pad.Capture = true; //(no action happens while the buttons are read as they are)
            SetNote("Press the button for " + PadOptions.ActionName(action) + ". Wait to leave it as it is.", false);
            Refresh();
        }

        private void EndCapture()
        {
            Capturing = null;
            WaitRelease = false;
            if (Pad != null) Pad.Capture = false;
            Refresh();
        }

        private void Bind(PadAction action, Buttons button)
        {
            var bindings = GamePadSettings.Bindings.Clone();
            var set = Glyphs();
            var other = bindings.ActionOf(button);
            var old = bindings.Get(action);
            bindings.Rebind(action, button);
            var error = GamePadSettings.SaveBindings(bindings);
            if (error != null) SetNote("Couldn't save it (" + error + "), but it's set until you quit.", true);
            else if (other != null && other != action && old.Length > 0 && old[0] != button)
                SetNote(PadOptions.ActionName(action) + ": " + PadGlyphs.ButtonName(button, set) + ". "
                    + PadOptions.ActionName(other.Value) + " takes " + PadGlyphs.ButtonName(old[0], set) + ".", false);
            else SetNote(PadOptions.ActionName(action) + ": " + PadGlyphs.ButtonName(button, set) + ".", false);
        }

        private void Reset()
        {
            if (Capturing != null) return;
            var error = GamePadSettings.SaveBindings(GamePadSettings.Defaults());
            SetNote(error == null ? "Back to the defaults." : "Couldn't save it (" + error + "), but it's set until you quit.", error != null);
            Refresh();
        }

        private void SetNote(string text, bool warn)
        {
            Note = text;
            NoteWarn = warn;
            Refresh();
        }

        private PadGlyphSet Glyphs() => PadGlyphs.Resolve(PadGlyphs.Setting, Pad?.DisplayName);

        public override void Update(UpdateState state)
        {
            base.Update(state);
            Pad = state.Pad;
            if (Closing)
            {
                if (Capturing != null) EndCapture();
                return;
            }
            if (Capturing != null)
            {
                Pad.Capture = true;
                if (!WaitRelease)
                {
                    CaptureTime += (float)state.Time.ElapsedGameTime.TotalSeconds;
                    var pressed = Pad.JustPressed;
                    if (pressed != null)
                    {
                        Bind(Capturing.Value, pressed.Value);
                        WaitRelease = true;
                    }
                    else if (CaptureTime > CaptureTimeout || !Pad.Connected)
                    {
                        SetNote("Nothing pressed: " + PadOptions.ActionName(Capturing.Value) + " is as it was.", false);
                        WaitRelease = true;
                    }
                }
                if (WaitRelease && !Pad.AnyBindableDown) EndCapture();
            }
            //the names follow the pad (its symbols)
            if (Pad.DisplayName != PadName)
            {
                PadName = Pad.DisplayName;
                Refresh();
            }
        }

        private void Refresh()
        {
            var bindings = GamePadSettings.Bindings;
            var defaults = GamePadSettings.Defaults();
            var set = Glyphs();
            foreach (var row in Rows)
            {
                var waiting = Capturing == row.Action;
                row.Button.Caption = waiting ? "Press a button" : PadOptions.ButtonsText(bindings.Get(row.Action), set);
                //(an action set away from its default shows in the highlight colour)
                row.Label.CaptionStyle.Color = bindings.SameAs(row.Action, defaults) ? UIStyle.Current.DialogText : UIStyle.Current.ActiveSelection;
                row.Button.Disabled = Capturing != null && !waiting;
            }
            ResetButton.Disabled = Capturing != null;
            DoneButton.Disabled = Capturing != null;
            NoteLabel.Caption = Note;
            NoteLabel.CaptionStyle.Color = NoteWarn ? UIStyle.Current.NegMoney : UIStyle.Current.DialogText * 0.7f;
        }

        private void Layout()
        {
            //two columns of eight: the action's name, then its button(s)
            var colWidth = Math.Min(520, Width / 2 - 40);
            var labelWidth = colWidth * 0.55f;
            var buttonWidth = colWidth - labelWidth - 10;
            for (int i = 0; i < Rows.Count; i++)
            {
                var row = Rows[i];
                var col = i / RowsPerColumn;
                var centerX = (col == 0) ? Width / 4 : (Width * 3) / 4;
                var left = centerX - colWidth / 2;
                var y = 100 + (i % RowsPerColumn) * 60;
                row.Label.Position = new Vector2(left, y);
                row.Label.Size = new Vector2(labelWidth, 55);
                row.Button.Position = new Vector2(left + labelWidth + 10, y);
                row.Button.Width = buttonWidth / RowScale;
            }
            NoteLabel.Position = new Vector2(0, 585);
            NoteLabel.Size = new Vector2(Width, 25);
            var footerWidth = 300f;
            ResetButton.Width = footerWidth / FooterScale;
            DoneButton.Width = footerWidth / FooterScale;
            ResetButton.Position = new Vector2(Width / 2 - footerWidth - 10, 615);
            DoneButton.Position = new Vector2(Width / 2 + 10, 615);
        }

        /// <summary>Gamepad: start on the first action.</summary>
        public UIElement PadDefaultFocus => Rows.Count > 0 ? Rows[0].Button : DoneButton;

        /// <summary>Gamepad Cancel closes, like Done (each change is already saved). While waiting for a button, no action happens.</summary>
        public bool PadCancel()
        {
            if (Capturing != null) return true;
            DoneButton.PerformClick();
            return true;
        }

        public override void GameResized()
        {
            base.GameResized();
            Layout();
        }
    }
}
