using System.Collections.Generic;
using System.Linq;
using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;

namespace Simitone.Client.VR
{
    /// <summary>
    /// The VR options in the game's own UI: opened by the VR button in options mode, it shows the same options as the
    /// VR menu in the headset (<see cref="VROptions"/>) in two lists. Clicking an option steps it to its next value;
    /// changes apply at once and are saved to vr.ini.
    /// </summary>
    public class UIVROptionsDialog : UIMobileDialog
    {
        private const int ListWidth = 480, ListHeight = 360, ListGap = 40, ListTop = 140;

        private readonly List<VROption> Options;
        private readonly UITouchStringList[] Lists = new UITouchStringList[2];
        private readonly int Split;

        public UIVROptionsDialog()
        {
            Caption = "VR Options";
            Options = VROptions.Create(includeSetup: SimitoneVR.SessionRunning);
            Split = (Options.Count + 1) / 2;
            SetHeight(ListTop + ListHeight + 150);

            var left = (Width - (ListWidth * 2 + ListGap)) / 2;
            var hint = new UILabel();
            hint.CaptionStyle = hint.CaptionStyle.Clone();
            hint.CaptionStyle.Size = 15;
            hint.CaptionStyle.Color = UIStyle.Current.Text;
            hint.Caption = "Click an option to change it. Changes apply at once and are saved to vr.ini. "
                + "In the headset, the VR menu has the same options.";
            hint.Position = new Vector2(left, 100);
            Add(hint);

            for (int i = 0; i < 2; i++)
            {
                var list = new UITouchStringList();
                list.Size = new Vector2(ListWidth, ListHeight);
                list.Position = new Vector2(left + i * (ListWidth + ListGap), ListTop);
                var offset = i * Split;
                list.OnSelectionChange += index => Changed(offset + index);
                Add(list);
                Lists[i] = list;
            }
            RefreshLists();

            var done = new UIBigButton(true);
            done.Caption = GameFacade.Strings.GetString("142", "0");
            done.Width = 275;
            done.Position = new Vector2((Width - 275) / 2, ListTop + ListHeight + 20);
            done.OnButtonClick += _ => Close();
            Add(done);
        }

        private void Changed(int index)
        {
            if (index < 0 || index >= Options.Count) return;
            // after the list has finished handling the click
            GameThread.NextUpdate(_ =>
            {
                var option = Options[index];
                VROptions.Apply(option, 1);
                if (option.IsAction) Close();
                else RefreshLists();
            });
        }

        private void RefreshLists()
        {
            for (int i = 0; i < 2; i++)
            {
                Lists[i].BackingList = Options.Skip(i * Split).Take(Split).Select(o => o.Label + ":  " + o.Value()).ToList();
                Lists[i].Refresh();
            }
        }
    }
}
