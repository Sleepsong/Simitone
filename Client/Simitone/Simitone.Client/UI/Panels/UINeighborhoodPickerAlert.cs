using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// Lets the player switch neighbourhood, create a new one, or install a neighbourhood package.
    /// </summary>
    public class UINeighborhoodPickerAlert : UIMobileDialog
    {
        /// <summary>Fired after the game has switched to another neighbourhood.</summary>
        public event Action OnSwitched;

        private UITouchStringList List;
        private UIBigButton NewButton;
        private UIBigButton CancelButton;
        private UIBigButton AcceptButton;
        private UIContainer Panel;

        private List<NeighborhoodSlot> Slots;
        private List<NeighborhoodPackage> Packages;
        private int Selection = -1;

        public UINeighborhoodPickerAlert()
        {
            Caption = "Neighborhoods";
            SetHeight(490);

            Panel = new UIContainer();
            Add(Panel);

            List = new UITouchStringList();
            List.Size = new Vector2(690, 350);
            List.OnSelectionChange += List_OnSelectionChange;
            Panel.Add(List);

            NewButton = new UIBigButton(false);
            NewButton.Caption = "New";
            NewButton.Position = new Vector2(370 + 385, 15);
            NewButton.Width = 275;
            NewButton.OnButtonClick += (btn) => NewNeighborhood();
            Panel.Add(NewButton);

            CancelButton = new UIBigButton(false);
            CancelButton.Caption = "Cancel";
            CancelButton.Position = new Vector2(370 + 385, 135);
            CancelButton.Width = 275;
            CancelButton.OnButtonClick += (btn) => Close();
            Panel.Add(CancelButton);

            AcceptButton = new UIBigButton(true);
            AcceptButton.Caption = "Play";
            AcceptButton.Position = new Vector2(370 + 385, 255);
            AcceptButton.Width = 275;
            AcceptButton.OnButtonClick += (btn) => Accept();
            Panel.Add(AcceptButton);

            Populate();
            GameResized();
        }

        public override void GameResized()
        {
            base.GameResized();
            if (Panel != null) Panel.Position = new Vector2((Width - 1030) / 2, 110);
        }

        private void Populate()
        {
            Slots = NeighborhoodManager.ListSlots();
            Packages = NeighborhoodManager.ListPackages();

            var items = new List<string>();
            foreach (var slot in Slots)
            {
                var text = slot.Name;
                if (!string.IsNullOrWhiteSpace(slot.Author)) text += " by " + slot.Author;
                if (slot.ID == NeighborhoodManager.ActiveSlot) text += " (current)";
                items.Add(text);
            }
            foreach (var package in Packages)
            {
                var text = "Install: " + package.Name;
                if (!string.IsNullOrWhiteSpace(package.Manifest.Author)) text += " by " + package.Manifest.Author;
                items.Add(text);
            }
            List.BackingList = items;
            List.Refresh();
            List_OnSelectionChange(-1);
        }

        private void List_OnSelectionChange(int index)
        {
            Selection = index;
            var isPackage = index >= Slots.Count;
            AcceptButton.Caption = isPackage ? "Install" : "Play";
            AcceptButton.Disabled = index < 0 || (!isPackage && Slots[index].ID == NeighborhoodManager.ActiveSlot);
        }

        private void Accept()
        {
            if (Selection < 0) return;
            if (Selection < Slots.Count)
            {
                var slot = Slots[Selection];
                SwitchTo(() => slot.ID, "Could not load \"" + slot.Name + "\".");
            }
            else
            {
                var package = Packages[Selection - Slots.Count];
                SwitchTo(() => NeighborhoodManager.InstallPackage(package), "Could not install \"" + package.Name + "\".");
            }
        }

        private void NewNeighborhood()
        {
            UIMobileAlert nameDialog = null;
            nameDialog = new UIMobileAlert(new UIAlertOptions()
            {
                Title = "New Neighborhood",
                Message = "Enter a name for the new neighborhood.",
                TextEntry = true,
                MaxChars = 32,
                Buttons = UIAlertButton.OkCancel(
                    (b) =>
                    {
                        var name = nameDialog.ResponseText;
                        nameDialog.Close();
                        SwitchTo(() => NeighborhoodManager.CreateNew(name), "Could not create a new neighborhood.");
                    },
                    (b) => nameDialog.Close())
            });
            UIScreen.GlobalShowDialog(nameDialog, true);
        }

        /// <summary>
        /// Gets a slot to play (which may create it first), then switches to it.
        /// </summary>
        private void SwitchTo(Func<int> getSlot, string failMessage)
        {
            if (Closing) return;
            try
            {
                NeighborhoodManager.Switch(getSlot());
            }
            catch (Exception e)
            {
                //if a new neighbourhood was made but could not be loaded, it should still show up in the list.
                Populate();
                UIScreen.GlobalShowDialog(new UIMobileAlert(new UIAlertOptions()
                {
                    Title = "Neighborhoods",
                    Message = failMessage + "\n\n" + e.Message,
                    Buttons = UIAlertButton.Ok()
                }), true);
                return;
            }
            OnSwitched?.Invoke();
            Close();
        }
    }
}
