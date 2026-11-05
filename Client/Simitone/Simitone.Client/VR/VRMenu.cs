using FSO.Client.UI.Model;
using FSO.HIT;
using Microsoft.Xna.Framework;
using Simitone.VR;

namespace Simitone.Client.VR
{
    /// <summary>
    /// The VR menu: the VR options (<see cref="UIVROptionsList"/>, the same as the Options panel's VR button) in a
    /// Simitone dialog floating in front of you. Point at an option and pull the trigger to change it; pull the trigger
    /// beside the menu, or press OK, to close it. Changes apply at once and are saved to vr.ini.
    ///
    /// Open it with the left controller's menu button (Touch-style controllers), a left trackpad squeeze (Index), the
    /// VR options button on your left wrist, or F9 on the desktop keyboard.
    /// </summary>
    public class VRMenu : VRUIPanel
    {
        private const int PanelWidth = 1120;
        private static readonly int PanelHeight = 100 + UIVROptionsList.TotalHeight(UIVROptionsList.FullListHeight) + 30;
        private readonly UIVROptionsList List;

        public VRMenu() : base(PanelWidth, PanelHeight, 0.8f)
        {
            VRUI.DialogFrame(this, PanelWidth, PanelHeight, "VR Options");
            List = new UIVROptionsList(includeSetup: true,
                "Point at an option and pull the trigger to change it. Pull the trigger beside the menu to close it.");
            List.Position = new Vector2((PanelWidth - 32 - UIVROptionsList.TotalWidth) / 2, 100);
            List.OnDone += Close;
            Add(List);
        }

        public bool IsOpen => Shown;

        /// <summary>Opens the menu in front of you (tracking space).</summary>
        public void OpenAt(VRPose head)
        {
            if (!head.Valid) return;
            PlaceInFront(head, 0.9f, 0.15f, 10);
            List.RefreshLists();
            Show();
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
        }

        public void Close()
        {
            if (!Shown) return;
            Hide();
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
        }
    }
}
