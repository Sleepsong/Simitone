using System;
using System.Collections.Generic;
using System.Linq;
using FSO.Client.UI.Framework;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;

namespace Simitone.Client.VR
{
    /// <summary>
    /// The VR options as Simitone UI: a hint, the options (<see cref="VROptions"/>) in two of Simitone's touch lists, and
    /// an OK button. Clicking an option steps it to its next value; changes apply at once and are saved to vr.ini.
    /// Shown by the VR menu in the headset (<see cref="VRMenu"/>) and by the Options panel's VR button
    /// (<see cref="UIVROptionsDialog"/>).
    /// </summary>
    public class UIVROptionsList : UIContainer
    {
        public const int ListWidth = 480, ListGap = 40, ListTop = 40;
        /// <summary>Lists tall enough for every option (38 px rows), as the VR menu shows them; shorter ones scroll.</summary>
        public const int FullListHeight = 11 * 38 + 12, ShortListHeight = 360;
        public const int TotalWidth = ListWidth * 2 + ListGap;
        public static int TotalHeight(int listHeight) => ListTop + listHeight + 20 + 100;

        private readonly List<VROption> Options;
        private readonly UITouchStringList[] Lists = new UITouchStringList[2];
        private readonly int Split;
        /// <summary>OK was pressed, or an option that does something (the comfort setup) was chosen.</summary>
        public event Action OnDone;

        public UIVROptionsList(bool includeSetup, string hint, int listHeight = FullListHeight)
        {
            Options = VROptions.Create(includeSetup);
            Split = (Options.Count + 1) / 2;

            var hintLabel = VRUI.Label(hint, 15, UIStyle.Current.Text);
            Add(hintLabel);

            for (int i = 0; i < 2; i++)
            {
                var list = new UITouchStringList();
                list.Size = new Vector2(ListWidth, listHeight);
                list.Position = new Vector2(i * (ListWidth + ListGap), ListTop);
                var offset = i * Split;
                list.OnSelectionChange += index => Changed(offset + index);
                Add(list);
                Lists[i] = list;
            }
            RefreshLists();

            var done = new UIBigButton(true);
            done.Caption = VRText.Get("142", "0", "OK");
            done.Width = 275;
            done.Position = new Vector2((TotalWidth - 275) / 2, ListTop + listHeight + 20);
            done.OnButtonClick += _ => OnDone?.Invoke();
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
                if (option.IsAction) OnDone?.Invoke();
                else RefreshLists();
            });
        }

        /// <summary>Shows the options' current values (they can also change from the other place they're shown).</summary>
        public void RefreshLists()
        {
            for (int i = 0; i < 2; i++)
            {
                Lists[i].BackingList = Options.Skip(i * Split).Take(Split).Select(o => o.Label + ":  " + o.Value()).ToList();
                Lists[i].Refresh();
            }
        }
    }

    /// <summary>
    /// The VR options in the game's own UI: opened by the VR button in options mode, a Simitone dialog with the same
    /// options as the VR menu in the headset.
    /// </summary>
    public class UIVROptionsDialog : UIMobileDialog
    {
        public UIVROptionsDialog()
        {
            Caption = "VR Options";
            SetHeight(100 + UIVROptionsList.TotalHeight(UIVROptionsList.ShortListHeight) + 30);
            var list = new UIVROptionsList(SimitoneVR.SessionRunning,
                "Click an option to change it (drag the lists for more). Changes apply at once and are saved to vr.ini.",
                UIVROptionsList.ShortListHeight);
            list.Position = new Vector2((Width - UIVROptionsList.TotalWidth) / 2, 100);
            list.OnDone += Close;
            Add(list);
        }
    }
}
