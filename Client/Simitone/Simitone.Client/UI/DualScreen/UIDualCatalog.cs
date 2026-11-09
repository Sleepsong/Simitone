using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Content;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Panels.LiveSubpanels;
using Simitone.Client.UI.Screens;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.DualScreen
{
    /// <summary>
    /// Buy or build mode's catalog on the second screen, for touch:
    /// - the mode's categories along the top (buy: seating, surfaces, ...; build: walls, floors and roofs; outdoors;
    ///   doors, windows and more), the open one lit;
    /// - under them the game's own catalog panel (<see cref="UIBuyBrowsePanel"/>): first the category's subcategories,
    ///   then its items, here in two rows (a little smaller). Picking an item works as from the main screen's catalog:
    ///   the object (or the build tool) is in hand, to place on the lot on the main screen;
    /// - the picked item's name and price, and a button back to the subcategories; under them, while it's in hand,
    ///   its description and what it does for the Sims' needs. (The main screen's info panel, which shows these on
    ///   one screen, sits over the middle of the lot, where the item is placed: with two screens it stays hidden.)
    /// The controller uses it too: LT moves the pad between the lot and this catalog (its focus is outlined here), RT
    /// opens the next category, and the highlighted item's name and price show under the items.
    /// </summary>
    public class UIDualCatalog : UIContainer
    {
        public readonly UIMainPanelMode Mode;
        private readonly TS1GameScreen Game;
        private readonly float W;
        private readonly float Top;
        private readonly List<(int id, UICatButton button)> Categories = new List<(int, UICatButton)>();
        private int Category = -1;
        public UIBuyBrowsePanel Browse { get; private set; }
        /// <summary>The catalog on the second screen, while there is one.</summary>
        public static UIDualCatalog Current { get; private set; }
        /// <summary>The controller's focus in this catalog (run by the lot's pad helper, drawn here).</summary>
        public static readonly UIFocusNavigator Navigator = new UIFocusNavigator();
        /// <summary>Whether the pad is the device in use (set with the navigator), so its focus shows.</summary>
        public static bool PadShown;
        private float Time;
        private UILabel ItemLabel;
        private UILabel DescLabel;
        private UILabel AdLabel;
        private string DescFor; //the description DescLabel was laid out for
        private UIBigButton BackButton;
        private const int Rows = 2;
        private const float BrowseScale = 0.8f; //the items' size, to leave room for the description
        private const int DescLines = 4;

        /// <param name="top">Where the catalog starts (under the money and clock).</param>
        public UIDualCatalog(TS1GameScreen game, UIMainPanelMode mode, float width, float top)
        {
            Game = game;
            Mode = mode;
            W = width;
            Top = top;
            var panel = game.Frontend.MainPanel;
            var cats = (mode == UIMainPanelMode.BUILD) ? panel.BuildCategories : panel.BuyCategories;

            // categories: one row, spread over the width
            var spacing = W / cats.Count;
            for (int i = 0; i < cats.Count; i++)
            {
                var cat = cats[i];
                var button = new UICatButton(Content.Get().CustomUI.Get(cat.IconName).Get(GameFacade.GraphicsDevice));
                button.Position = new Vector2(spacing * (i + 0.5f) - 65 / 2f, Top + 5);
                button.Tooltip = cat.Name;
                var id = cat.ID;
                button.OnButtonClick += (b) => SelectCategory(id);
                Add(button);
                Categories.Add((id, button));
            }

            ItemLabel = new UILabel();
            ItemLabel.CaptionStyle = ItemLabel.CaptionStyle.Clone();
            ItemLabel.CaptionStyle.Size = 15;
            ItemLabel.CaptionStyle.Color = UIStyle.Current.Text;
            ItemLabel.Alignment = TextAlignment.Left | TextAlignment.Middle;
            ItemLabel.Position = new Vector2(15, RowsBottom + 2);
            ItemLabel.Size = new Vector2(W - 160, 24);
            Add(ItemLabel);

            DescLabel = new UILabel();
            DescLabel.CaptionStyle = DescLabel.CaptionStyle.Clone();
            DescLabel.CaptionStyle.Size = 12;
            DescLabel.CaptionStyle.Color = UIStyle.Current.Text * 0.85f;
            DescLabel.Alignment = TextAlignment.Left | TextAlignment.Top;
            DescLabel.Wrapped = true;
            DescLabel.MaxLines = DescLines;
            DescLabel.Position = new Vector2(15, RowsBottom + 28);
            DescLabel.Size = new Vector2(W - 160, DescLines * 16);
            Add(DescLabel);

            AdLabel = new UILabel();
            AdLabel.CaptionStyle = AdLabel.CaptionStyle.Clone();
            AdLabel.CaptionStyle.Size = 12;
            AdLabel.CaptionStyle.Color = UIStyle.Current.SecondaryText;
            AdLabel.Alignment = TextAlignment.Left | TextAlignment.Top;
            AdLabel.Position = new Vector2(15, RowsBottom + 28);
            AdLabel.Size = new Vector2(W - 30, 16);
            Add(AdLabel);

            BackButton = new UIBigButton(false);
            BackButton.Caption = "Back";
            BackButton.Width = 220;
            BackButton.ScaleX = BackButton.ScaleY = 0.5f; //(110 x 50)
            BackButton.Position = new Vector2(W - 125, RowsBottom + 2);
            BackButton.OnButtonClick += (b) => Browse?.Reset(); //items -> the category's subcategories
            Add(BackButton);

            if (cats.Count > 0) SelectCategory(cats[0].ID);
            Current = this;
        }

        /// <summary>Opens the category after the open one (the pad's RT), wrapping round.</summary>
        public void NextCategory()
        {
            if (Categories.Count == 0) return;
            var index = Categories.FindIndex(x => x.id == Category);
            SelectCategory(Categories[(index + 1) % Categories.Count].id);
        }

        /// <summary>Whether the pad is moving over this catalog, and its focus is to be shown.</summary>
        private bool PadFocusShown => PadShown && Browse != null && Navigator.Scope == Browse && Navigator.Focused != null;

        private float BrowseTop => Top + 78;
        private float RowsBottom => BrowseTop + 128 * Rows * BrowseScale;

        public void SelectCategory(int id)
        {
            if (id == Category && Browse != null) return;
            Category = id;
            foreach (var (catId, button) in Categories) button.Selected = catId == id;
            //(as the main panel does: the old catalog panel lets go of whatever it had picked)
            Browse?.Kill();
            var lotType = (Mode == UIMainPanelMode.BUILD) ? UICatalogMode.Build : Game.Frontend.MainPanel.GetLotType(false);
            Browse = new UIBuyBrowsePanel(Game, (sbyte)id, lotType);
            Browse.Position = new Vector2(0, BrowseTop);
            Browse.ScaleX = Browse.ScaleY = BrowseScale;
            Add(Browse);
            Browse.SetHost(W / BrowseScale, Rows);
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            // the picked item: name and price
            Time += (float)state.Time.ElapsedGameTime.TotalSeconds;
            string text = "";
            var browse = Browse;
            //the pad's highlight (it browses without picking), else the picked item
            var index = (browse != null && PadFocusShown && Navigator.Focused == browse.CatContainer)
                ? browse.CatContainer.PadHighlightIndex : -1;
            if (index < 0 && browse != null) index = browse.ItemID;
            if (browse != null && !browse.ChoosingSub && index >= 0 && browse.FilterCategory != null)
            {
                var item = browse.FilterCategory.ElementAtOrDefault(index);
                if (item != null) text = item.ToString() + ((item.CalcPrice > 0) ? "   §" + item.CalcPrice.ToString("##,#0") : "");
            }
            else if (browse != null && browse.ChoosingSub) text = "Choose a kind of item";
            if (ItemLabel.Caption != text) ItemLabel.Caption = text;
            BackButton.Visible = browse != null && !browse.ChoosingSub;
            UpdateDescription(browse != null && !browse.ChoosingSub && index >= 0 && index == browse.ItemID);
        }

        /// <summary>
        /// The description and needs of the item in hand, from the main screen's info panel (which fills them in when
        /// an item is picked), shown while the label names that item.
        /// </summary>
        private void UpdateDescription(bool showingPicked)
        {
            var query = Game.LotControl?.QueryPanel;
            var show = showingPicked && query != null && query.Active && query.Mode == 1;
            var desc = show ? (query.Body.Caption ?? "") : "";
            var ads = show ? (query.Ad.Caption ?? "") : "";
            if (desc != DescFor)
            {
                DescFor = desc;
                DescLabel.Caption = desc;
            }
            if (AdLabel.Caption != ads) AdLabel.Caption = ads;
            //the needs go straight under however many lines the description took
            var lines = (desc.Length > 0) ? System.Math.Min(DescLines, System.Math.Max(1, DescLabel.NumLines)) : 0;
            AdLabel.Y = RowsBottom + 28 + lines * DescLabel.CaptionStyle.LineHeight + 2;
        }

        public override void Draw(UISpriteBatch batch)
        {
            base.Draw(batch);
            if (!Visible || !PadFocusShown || !Navigator.Focused.WillDraw()) return;
            //the pad's focus: Simitone's mint frame, as on the main screen (its scale is this screen's)
            var rect = UIFocusNavigator.GlobalRect(Navigator.Focused);
            var scale = (SecondScreen.Panel?.UIScale ?? 1f);
            var gd = GameFacade.GraphicsDevice;
            var pad = (int)((PadGraphics.GlowInset + 3) * scale);
            var frame = rect;
            frame.Inflate(pad, pad);
            var pulse = 0.82f + 0.18f * (float)System.Math.Sin(Time * 4f);
            var shadow = frame;
            shadow.Inflate((int)(2 * scale), (int)(2 * scale));
            PadDraw.NineSliceScreen(batch, PadGraphics.GetGlow(gd), PadGraphics.GlowCorner, shadow, PadDraw.Ink * 0.45f, scale);
            PadDraw.NineSliceScreen(batch, PadGraphics.GetGlow(gd), PadGraphics.GlowCorner, frame, PadDraw.Accent * pulse, scale);
        }

        /// <summary>Lets go of the catalog (and whatever it had picked), as leaving buy or build mode does.</summary>
        public void Destroy()
        {
            Browse?.Kill();
            Browse = null;
            if (Current == this) Current = null;
            Navigator.Clear();
            Parent?.Remove(this);
        }
    }
}
