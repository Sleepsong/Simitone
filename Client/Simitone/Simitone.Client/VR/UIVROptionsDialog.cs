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
        public const int ListWidth = 480, ListHeight = 360, ListGap = 40, ListTop = 40;
        public const int TotalWidth = ListWidth * 2 + ListGap;
        public const int TotalHeight = ListTop + ListHeight + 20 + 100;

        private readonly List<VROption> Options;
        private readonly UITouchStringList[] Lists = new UITouchStringList[2];
        private readonly int Split;
        /// <summary>OK was pressed, or an option that does something (the comfort setup) was chosen.</summary>
        public event Action OnDone;

        public UIVROptionsList(bool includeSetup, string hint)
        {
            Options = VROptions.Create(includeSetup);
            Split = (Options.Count + 1) / 2;

            var hintLabel = VRUI.Label(hint, 15, UIStyle.Current.Text);
            Add(hintLabel);

            for (int i = 0; i < 2; i++)
            {
                var list = new UITouchStringList();
                list.Size = new Vector2(ListWidth, ListHeight);
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
            done.Position = new Vector2((TotalWidth - 275) / 2, ListTop + ListHeight + 20);
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
            SetHeight(100 + UIVROptionsList.TotalHeight + 30);
            var list = new UIVROptionsList(SimitoneVR.SessionRunning,
                "Click an option to change it. Changes apply at once and are saved to vr.ini. In the headset, the VR menu has the same options.");
            list.Position = new Vector2((Width - UIVROptionsList.TotalWidth) / 2, 100);
            list.OnDone += Close;
            Add(list);
        }
    }
}
