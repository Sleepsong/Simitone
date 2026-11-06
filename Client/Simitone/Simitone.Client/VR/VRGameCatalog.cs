using System.Collections.Generic;
using System.Linq;
using FSO.Client;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Panels.LiveSubpanels;
using Simitone.Client.UI.Panels.LiveSubpanels.Catalog;
using Simitone.Client.UI.Screens;
using Simitone.VR;
using FSO.SimAntics;
using FSO.SimAntics.Model;

namespace Simitone.Client.VR
{
    /// <summary>
    /// The game's buy/build catalogue for the VR tablet: it reads and drives the same panels the flat UI uses (the main
    /// panel's mode and category switcher, and the buy/build browse panel), so choosing on the tablet is the same as
    /// clicking the game's own buttons, and both stay in step.
    /// </summary>
    public class VRGameCatalog : IVRCatalogSource
    {
        private readonly TS1GameScreen Screen;
        private readonly List<Texture2D> CategoryIcons = new List<Texture2D>();
        private readonly List<VRCatalogSubcategory> SubcategoryList = new List<VRCatalogSubcategory>();
        private readonly List<VRCatalogEntry> ItemList = new List<VRCatalogEntry>();
        private int VersionNumber;

        // what the lists were made from
        private List<Simitone.Client.UI.Controls.UICategory> ShownCategories;
        private UIBuyBrowsePanel ShownPanel;
        private List<UICatalogSubcat> ShownSubcategories;
        private IEnumerable<UICatalogElement> ShownItems;

        public VRGameCatalog(TS1GameScreen screen)
        {
            Screen = screen;
        }

        private UIMainPanel MainPanel => Screen.Frontend?.MainPanel;
        private UIBuyBrowsePanel Browse => MainPanel?.SubPanel as UIBuyBrowsePanel;

        public VRCatalogMode Mode => MainPanel?.Mode switch
        {
            UIMainPanelMode.BUY => VRCatalogMode.Buy,
            UIMainPanelMode.BUILD => VRCatalogMode.Build,
            _ => VRCatalogMode.Live
        };

        public int Budget => Screen.ActiveFamily?.Budget ?? 0;

        public IReadOnlyList<Texture2D> Categories { get { Poll(); return CategoryIcons; } }

        public int ActiveCategory
        {
            get
            {
                var switcher = MainPanel?.Switcher;
                if (switcher?.Categories == null || Mode == VRCatalogMode.Live) return -1;
                return switcher.Categories.FindIndex(c => c.ID == switcher.ActiveCategory);
            }
        }

        public IReadOnlyList<VRCatalogSubcategory> Subcategories { get { Poll(); return SubcategoryList; } }

        public int ActiveSubcategory
        {
            get
            {
                var browse = Browse;
                return browse == null || browse.ChoosingSub ? -1 : browse.ActiveSubcategory;
            }
        }

        public IReadOnlyList<VRCatalogEntry> Items { get { Poll(); return ItemList; } }

        public int SelectedItem
        {
            get
            {
                var browse = Browse;
                var lc = Screen.LotControl;
                if (browse == null || browse.ChoosingSub || lc == null) return -1;
                // (the panel remembers the last choice after what it made has gone, e.g. sold back with B)
                var holding = lc.ObjectHolder?.Holding != null || lc.CustomControl != null;
                return holding ? browse.ItemID : -1;
            }
        }

        public int Version { get { Poll(); return VersionNumber; } }

        /// <summary>Remakes the lists when what they show has changed in the game.</summary>
        private void Poll()
        {
            var switcher = MainPanel?.Switcher;
            var categories = Mode == VRCatalogMode.Live ? null : switcher?.Categories;
            var browse = Browse;
            var subcategories = browse?.Subcategories;
            var items = browse == null || browse.ChoosingSub ? null : browse.FilterCategory;
            if (categories == ShownCategories && browse == ShownPanel && subcategories == ShownSubcategories && items == ShownItems)
                return;
            VersionNumber++;
            var gd = GameFacade.GraphicsDevice;

            if (categories != ShownCategories)
            {
                ShownCategories = categories;
                CategoryIcons.Clear();
                if (categories != null)
                    foreach (var c in categories) CategoryIcons.Add(Content.Get().CustomUI.Get(c.IconName).Get(gd));
            }

            if (browse != ShownPanel || subcategories != ShownSubcategories)
            {
                ShownPanel = browse;
                ShownSubcategories = subcategories;
                SubcategoryList.Clear();
                if (subcategories != null)
                {
                    for (int i = 0; i < subcategories.Count; i++)
                    {
                        var sub = subcategories[i];
                        SubcategoryList.Add(new VRCatalogSubcategory
                        {
                            Label = GameFacade.Strings.GetString(sub.StrTable.ToString(), sub.StrInd.ToString()) ?? "",
                            Icon = Content.Get().CustomUI.Get("cat_" + sub.IconName + ".png").Get(gd),
                            Disabled = i < browse.SubcategoryEmpty.Count && browse.SubcategoryEmpty[i]
                        });
                    }
                }
            }

            ShownItems = items;
            ItemList.Clear();
            if (items != null)
            {
                foreach (var elem in items)
                {
                    var special = elem.Special;
                    ItemList.Add(new VRCatalogEntry
                    {
                        Name = elem.ToString(),
                        Price = (int)elem.Item.Price,
                        Icon = special?.Res != null ? special.Res.GetIcon(special.ResID) : ObjectIcon(elem.Item.GUID),
                        GUID = special == null ? elem.Item.GUID : 0,
                        Description = special?.Res != null ? special.Res.GetDescription(special.ResID) : ObjectDescription(elem.Item.GUID)
                    });
                }
            }
        }

        /// <summary>An object's catalogue picture (shared with the game's catalogue, which caches them the same way).</summary>
        private static Texture2D ObjectIcon(uint guid)
        {
            var cache = UICatalogItem.IconCache;
            if (cache.TryGetValue(guid, out var icon)) return icon;
            var obj = Content.Get().WorldObjects.Get(guid);
            var bmp = obj?.Resource.Get<BMP>(obj.OBJ.CatalogStringsID);
            icon = bmp?.GetTexture(GameFacade.GraphicsDevice);
            cache[guid] = icon;
            return icon;
        }

        private VRCameraController Camera => Screen.VRWorld?.VRCamera;
        public int Floor => Camera?.PlayerLevel ?? 1;
        public int Floors => Camera?.Stories ?? 1;
        public void ChangeFloor(int direction) => Camera?.RequestLevelChange(direction);
        public VRWallView Walls => SimitoneVR.Settings.BuildWalls;

        public void SetWalls(VRWallView walls)
        {
            SimitoneVR.Settings.BuildWalls = walls;
            SimitoneVR.Settings.Save();
        }

        /// <summary>An object's catalogue description (as the game's query panel shows it).</summary>
        private static string ObjectDescription(uint guid)
        {
            var obj = Content.Get().WorldObjects.Get(guid);
            return obj?.Resource.Get<CTSS>(obj.OBJ.CatalogStringsID)?.GetString(1) ?? "";
        }

        private VMEntity HeldEntity;
        private VRHeldObject HeldInfo;

        public VRHeldObject Held
        {
            get
            {
                var lc = Screen.LotControl;
                var holding = lc?.ObjectHolder?.Holding;
                if (holding == null || !holding.IsBought || lc.vm == null)
                {
                    HeldEntity = null;
                    return null;
                }
                var entity = holding.RealEnt ?? holding.Group.BaseObject;
                if (entity != HeldEntity)
                {
                    HeldEntity = entity;
                    HeldInfo = Describe(lc.vm, entity);
                }
                return HeldInfo;
            }
        }

        /// <summary>What the game's pickup panel says about an object picked up from the lot (UIPickupPanel.SetInfo).</summary>
        private static VRHeldObject Describe(VM vm, VMEntity entity)
        {
            var def = entity.MasterDefinition ?? entity.Object.OBJ;
            var name = entity.Object.Resource.Get<CTSS>(def.CatalogStringsID)?.GetString(0) ?? entity.ToString();
            var value = entity.MultitileGroup.Price;
            var canSell = entity.IsUserMovable(vm.Context, true) == VMPlacementError.Success;
            string Str(string id, string fallback)
            {
                var s = GameFacade.Strings?.GetString("136", id);
                return string.IsNullOrEmpty(s) ? fallback : s;
            }
            var text = !canSell ? Str("2", "% can't be deleted.").Replace("%", name)
                : value > 0 ? Str("1", "Sell % for $?").Replace("%", name).Replace("$", "§" + value)
                : Str("0", "Delete %?").Replace("%", name);
            return new VRHeldObject { Name = name, Text = text, CanSell = canSell, Value = value };
        }

        public void SellHeld() => Screen.LotControl?.ObjectHolder?.SellBack(null);
        public void PutBackHeld() => Screen.LotControl?.ObjectHolder?.Cancel();

        public void SetMode(VRCatalogMode mode)
        {
            var frontend = Screen.Frontend;
            if (frontend?.MainPanel == null || mode == Mode) return;
            frontend.SwitchMode(mode switch
            {
                VRCatalogMode.Buy => UIMainPanelMode.BUY,
                VRCatalogMode.Build => UIMainPanelMode.BUILD,
                _ => UIMainPanelMode.LIVE
            });
            SimitoneVR.Log("mode: " + mode + " (catalogue)");
        }

        public void ChooseCategory(int index)
        {
            var switcher = MainPanel?.Switcher;
            if (switcher?.Categories == null || index < 0 || index >= switcher.Categories.Count) return;
            if (index == ActiveCategory && Browse?.ChoosingSub == true) return;
            switcher.Select(switcher.Categories[index].ID); // (a new browse panel, choosing its subcategory)
        }

        public void ChooseSubcategory(int index)
        {
            var browse = Browse;
            if (browse == null || index < 0 || index >= browse.Subcategories.Count) return;
            if (!browse.ChoosingSub)
            {
                if (index == browse.ActiveSubcategory) return;
                browse.Reset(); // back to choosing (as the game's category button does)
            }
            if (index >= browse.Subcategories.Count || (index < browse.SubcategoryEmpty.Count && browse.SubcategoryEmpty[index])) return;
            browse.InitSubcategory(browse.Subcategories[index]);
        }

        public void ChooseItem(int index)
        {
            var browse = Browse;
            if (browse == null || browse.ChoosingSub || browse.FilterCategory == null) return;
            if (index < 0 || index >= ItemList.Count) return;
            if (index == SelectedItem) browse.Deselect();
            else browse.Selected(index);
        }
    }
}
