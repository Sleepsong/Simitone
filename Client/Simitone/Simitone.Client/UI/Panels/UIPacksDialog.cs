/*
This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
If a copy of the MPL was not distributed with this file, You can obtain one at
http://mozilla.org/MPL/2.0/.
*/

using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework;
using Simitone.Client.Packs;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using System;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// Lists the installed add-on packs and lets the user turn each one on or off.
    /// </summary>
    public class UIPacksDialog : UIMobileDialog
    {
        private const int Margin = 80;
        private const int RowTop = 105;
        private const int RowHeight = 115;
        private const int ButtonWidth = 275;

        private UIContainer Body;
        private int Page;
        private string Message; //why the last toggle was refused

        public UIPacksDialog() : base()
        {
            Caption = "Add-on Packs";
            Refresh();
        }

        public override void GameResized()
        {
            base.GameResized();
            Refresh();
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            //ride in and out with the dialog's stripes
            Body.X = ((Closing) ? 1 : -1) * (1 - InterpolatedAnimation) * Width;
        }

        private UILabel AddLabel(string text, int size, Color color, Vector2 pos, int width, int maxLines)
        {
            var label = new UILabel();
            label.CaptionStyle = label.CaptionStyle.Clone();
            label.CaptionStyle.Size = size;
            label.CaptionStyle.Color = color;
            label.Alignment = TextAlignment.Top | TextAlignment.Left;
            label.Wrapped = true;
            label.MaxLines = maxLines;
            label.Position = pos;
            label.Size = new Vector2(width, 1);
            label.Caption = text;
            Body.Add(label);
            return label;
        }

        private UIBigButton AddButton(string text, bool green, Vector2 pos, int width, Action onClick)
        {
            var btn = new UIBigButton(green);
            btn.Caption = text;
            btn.Width = width;
            btn.Position = pos;
            btn.OnButtonClick += (b) => onClick();
            Body.Add(btn);
            return btn;
        }

        /// <summary>
        /// Rebuild the page from the pack manager's current state.
        /// </summary>
        private void Refresh()
        {
            if (Body != null) Remove(Body);
            Body = new UIContainer();
            Add(Body);

            var packs = PackManager.All;
            var style = UIStyle.Current;
            var textWidth = Width - Margin * 3 - ButtonWidth;

            //as many rows as the screen has room for, under the title and over the status line and buttons
            var perPage = Math.Max(1, (ScrHeight - RowTop - 60 - 150) / RowHeight);
            var pages = Math.Max(1, (packs.Count + perPage - 1) / perPage);
            Page = Math.Min(Page, pages - 1);
            var rows = Math.Min(perPage, packs.Count - Page * perPage);

            for (int i = 0; i < rows; i++)
            {
                var pack = packs[Page * perPage + i];
                var y = RowTop + i * RowHeight;
                var enabled = PackManager.IsEnabled(pack);
                var manifest = pack.Manifest;

                var title = pack.DisplayName;
                if (!string.IsNullOrWhiteSpace(manifest.Version)) title += "  " + manifest.Version;
                if (!string.IsNullOrWhiteSpace(manifest.Author)) title += "  by " + manifest.Author;
                AddLabel(title, 19, style.DialogText, new Vector2(Margin, y), textWidth, 1);

                //an enabled pack that isn't running this session is the one case the user can't see for themselves
                var problem = PackManager.GetProblem(pack) ?? (enabled ? pack.CodeError : null);
                if (problem == null && enabled && !PackManager.IsActive(pack.Id) && !PackManager.RestartRequired)
                    problem = "Not loaded";
                var detail = problem ?? ("[" + pack.LayerSummary + "]  " + manifest.Description);
                AddLabel(detail, 12, problem == null ? style.DialogText * 0.75f : style.NegMoney, new Vector2(Margin, y + 40), textWidth, 3);

                var toggle = AddButton(enabled ? "On" : "Off", enabled, new Vector2(Width - Margin - ButtonWidth, y), ButtonWidth, () => Toggle(pack));
                toggle.Disabled = pack.LoadError != null;
            }

            var statusY = RowTop + Math.Max(rows, 1) * RowHeight;
            string status;
            var statusColor = style.DialogText;
            if (Message != null) { status = Message; statusColor = style.NegMoney; }
            else if (packs.Count == 0) status = "No packs are installed. To add one, put its folder in:\n" + PackManager.PacksDir;
            else if (PackManager.RestartRequired) { status = "Restart Simitone to apply your changes."; statusColor = style.SecondaryText; }
            else status = "Changes apply the next time Simitone starts.";
            if (packs.Count == 0) statusY = RowTop + 20;
            AddLabel(status, 15, statusColor, new Vector2(Margin, statusY), Width - Margin * 2, 2);

            var buttonY = statusY + 70;
            AddButton("Close", true, new Vector2((Width - ButtonWidth) / 2, buttonY), ButtonWidth, Close);
            if (pages > 1)
            {
                var prev = AddButton("<", false, new Vector2(Margin, buttonY), 150, () => { Page--; Message = null; Refresh(); });
                prev.Disabled = Page == 0;
                var next = AddButton(">", false, new Vector2(Width - Margin - 150, buttonY), 150, () => { Page++; Message = null; Refresh(); });
                next.Disabled = Page == pages - 1;
                AddLabel((Page + 1) + " / " + pages, 15, style.DialogText, new Vector2(Margin + 170, buttonY + 35), 150, 1);
            }

            SetHeight(buttonY + 125);
        }

        private void Toggle(Pack pack)
        {
            var enable = !PackManager.IsEnabled(pack);
            if (enable && pack.HasCode)
            {
                //code runs with the same access to this computer as the game itself, so this one needs a deliberate yes
                UIMobileAlert alert = null;
                alert = new UIMobileAlert(new UIAlertOptions()
                {
                    Title = "Enable " + pack.DisplayName + "?",
                    Message = "This pack contains program code, which can do anything on this computer that Simitone can. "
                        + "Only enable it if you trust where it came from.",
                    Buttons = UIAlertButton.YesNo(
                        (b) => { alert.Close(); Apply(pack, true); },
                        (b) => { alert.Close(); })
                });
                UIScreen.GlobalShowDialog(alert, true);
                return;
            }
            Apply(pack, enable);
        }

        private void Apply(Pack pack, bool enable)
        {
            Message = PackManager.SetEnabled(pack, enable, true); //Toggle has already asked about code
            if (Message != null) Message = pack.DisplayName + ": " + Message + ".";
            Refresh();
        }
    }
}
