using System;
using System.Collections.Generic;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.HIT;
using FSO.Client.UI.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.VR;

namespace Simitone.Client.VR
{
    public enum VRCatalogMode { Live, Buy, Build }

    /// <summary>One thing in the catalogue: an object to buy, or a build tool (a wall, floor, wallpaper...).</summary>
    public class VRCatalogEntry
    {
        public string Name;
        public int Price;
        /// <summary>The catalogue's own picture.</summary>
        public Texture2D Icon;
        /// <summary>The object's GUID, for a 3D thumbnail; 0 for build tools (their picture is the icon).</summary>
        public uint GUID;
        /// <summary>The catalogue's description (may be empty).</summary>
        public string Description;
    }

    /// <summary>An object picked up from the lot, as the game's pickup panel describes it.</summary>
    public class VRHeldObject
    {
        public string Name;
        /// <summary>The game's line about it: what it sells for, or that it can't be sold.</summary>
        public string Text;
        public bool CanSell;
        public int Value;
    }

    public class VRCatalogSubcategory
    {
        public string Label;
        public Texture2D Icon;
        public bool Disabled;
    }

    /// <summary>What the catalogue tablet shows and does: the game's buy/build catalogue (see <see cref="VRGameCatalog"/>).</summary>
    public interface IVRCatalogSource
    {
        VRCatalogMode Mode { get; }
        int Budget { get; }
        IReadOnlyList<Texture2D> Categories { get; }
        int ActiveCategory { get; }
        IReadOnlyList<VRCatalogSubcategory> Subcategories { get; }
        int ActiveSubcategory { get; }
        /// <summary>Empty until a subcategory is chosen.</summary>
        IReadOnlyList<VRCatalogEntry> Items { get; }
        int SelectedItem { get; }
        /// <summary>Changes whenever any of the lists above does.</summary>
        int Version { get; }
        /// <summary>The floor you build on (1 is the ground), and how many the lot can have.</summary>
        int Floor { get; }
        int Floors { get; }
        /// <summary>The walls in buy/build mode.</summary>
        VRWallView Walls { get; }
        /// <summary>An object you picked up from the lot (null otherwise): the tablet shows it instead of the items.</summary>
        VRHeldObject Held { get; }
        void SellHeld();
        void PutBackHeld();
        void SetMode(VRCatalogMode mode);
        /// <summary>Up (1) or down (-1) a floor, even while holding something.</summary>
        void ChangeFloor(int direction);
        void SetWalls(VRWallView walls);
        void ChooseCategory(int index);
        void ChooseSubcategory(int index);
        /// <summary>Picks an item up to place it (choosing the one already held puts it back).</summary>
        void ChooseItem(int index);
    }

    /// <summary>
    /// The buy and build catalogue as a tablet in your walking hand (the left, or the right when left-handed), in
    /// Simitone's look: the Live / Buy / Build tabs and your money at the top, then the categories and subcategories as
    /// the game's own category buttons, then the items as tiles with their price (a 3D view of each object, turning when
    /// you point at it; the catalogue's picture for build tools and until the 3D view is ready). Point with the other
    /// hand and pull the trigger: choosing an item picks it up, and you place it with the arc as before.
    /// </summary>
    public class VRCatalogTablet : VRUIPanel
    {
        public const int PanelWidth = 860, PanelHeight = 940;
        private const int Left = 30, Columns = 4, Rows = 2, PerPage = Columns * Rows;
        private const int TileW = 172, TileH = 186, GridTop = 336;

        public IVRCatalogSource Source;
        /// <summary>A 3D thumbnail for a GUID (true: the turning one for the item pointed at), or null if not ready.</summary>
        public Func<uint, bool, Texture2D> Thumbnail;

        private readonly UIVRIconButton[] Tabs = new UIVRIconButton[3];
        private readonly UIVRIconButton WallsButton, FloorDown, FloorUp;
        private readonly UILabel FloorLabel;
        private VRWallView ShownWalls = (VRWallView)(-1);
        private static readonly string[] FloorNames = { "1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th" };
        private readonly UILabel MoneyLabel, NameLabel, PriceLabel, HintLabel, PageLabel, EmptyLabel;
        private readonly List<UICatButton> CategoryButtons = new List<UICatButton>();
        private readonly List<(UICatButton button, UILabel label)> SubButtons = new List<(UICatButton, UILabel)>();
        private readonly UIVRCatalogTile[] Tiles = new UIVRCatalogTile[PerPage];
        private readonly UIVRKey PrevPage, NextPage, SellKey, PutBackKey;
        private readonly UILabel DescLabel, HeldTitle, HeldText;
        private bool HeldShown;
        private int ShownVersion = -1, Page;
        private VRCatalogMode ShownMode = (VRCatalogMode)(-1);
        private IVRCatalogSource ShownSource;
        private int Hovered = -1;

        public VRCatalogTablet() : base(PanelWidth, PanelHeight, 0.26f)
        {
            Add(new UIDiagonalStripe(new Point(PanelWidth - 32, PanelHeight), UIDiagonalStripeSide.RIGHT, UIStyle.Current.DialogBg));

            // tabs, and the money
            var tabIcons = new[] { "mode_live.png", "mode_buy.png", "mode_build.png" };
            for (int i = 0; i < 3; i++)
            {
                var mode = (VRCatalogMode)i;
                var tab = new UIVRIconButton(VRUI.CustomUI(tabIcons[i]), 76) { Position = new Vector2(Left + i * 96, 14) };
                tab.OnClick += () => { Source?.SetMode(mode); Click(); };
                Add(tab);
                Tabs[i] = tab;
            }
            // the walls (as the game's wall button: cut away, down, up) and the floor you build on
            WallsButton = new UIVRIconButton(VRUI.CustomUI("cut_btn_away.png"), 76) { Position = new Vector2(Left + 300, 14), Frames = 2 };
            WallsButton.OnClick += () =>
            {
                var walls = Source?.Walls ?? VRWallView.Cutaway;
                Source?.SetWalls(walls == VRWallView.Cutaway ? VRWallView.Up : walls == VRWallView.Up ? VRWallView.Down : VRWallView.Cutaway);
                Click();
            };
            Add(WallsButton);
            FloorDown = new UIVRIconButton(VRUI.CustomUI("level_down.png"), 60) { Position = new Vector2(Left + 396, 22) };
            FloorDown.OnClick += () => { if (Source != null && Source.Floor > 1) { Source.ChangeFloor(-1); Click(); } };
            Add(FloorDown);
            FloorLabel = VRUI.Label("", 20, UIStyle.Current.Text, TextAlignment.Center | TextAlignment.Middle);
            FloorLabel.Position = new Vector2(Left + 456, 14);
            FloorLabel.Size = new Vector2(70, 76);
            Add(FloorLabel);
            FloorUp = new UIVRIconButton(VRUI.CustomUI("level_up.png"), 60) { Position = new Vector2(Left + 526, 22) };
            FloorUp.OnClick += () => { if (Source != null && Source.Floor < Source.Floors) { Source.ChangeFloor(1); Click(); } };
            Add(FloorUp);

            var money = new UIVRMoneyPill { Position = new Vector2(PanelWidth - 32 - Left - 140, 38), ScaleX = 1.1f, ScaleY = 1.1f };
            Add(money);
            MoneyLabel = VRUI.Label("", 17, UIStyle.Current.Text, TextAlignment.Center | TextAlignment.Middle);
            MoneyLabel.Position = money.Position;
            MoneyLabel.Size = new Vector2(140, 26);
            Add(MoneyLabel);

            // the items
            for (int i = 0; i < PerPage; i++)
            {
                var index = i;
                var tile = new UIVRCatalogTile(TileW, TileH)
                {
                    Position = new Vector2(Left + 14 + (i % Columns) * (TileW + 12), GridTop + (i / Columns) * (TileH + 8))
                };
                tile.OnChosen += () => { Source?.ChooseItem(Page * PerPage + index); Click(); };
                tile.OnHover += hover => { if (hover) { Hovered = Page * PerPage + index; Tick(); } else if (Hovered == Page * PerPage + index) Hovered = -1; };
                Add(tile);
                Tiles[i] = tile;
            }
            EmptyLabel = VRUI.Label("", 18, UIStyle.Current.Text * 0.8f, TextAlignment.Center | TextAlignment.Middle);
            EmptyLabel.Position = new Vector2(Left, GridTop);
            EmptyLabel.Size = new Vector2(PanelWidth - 32 - Left * 2, TileH * 2);
            Add(EmptyLabel);

            // what you're pointing at or holding, and the pages
            NameLabel = VRUI.Label("", 20, UIStyle.Current.Text);
            NameLabel.Position = new Vector2(Left + 10, GridTop + 2 * (TileH + 8) + 6);
            Add(NameLabel);
            PriceLabel = VRUI.Label("", 20, UIStyle.Current.SecondaryText, TextAlignment.Right | TextAlignment.Top);
            PriceLabel.Position = new Vector2(PanelWidth - 32 - Left - 200, GridTop + 2 * (TileH + 8) + 6);
            PriceLabel.Size = new Vector2(190, 30);
            Add(PriceLabel);
            DescLabel = VRUI.Label("", 14, UIStyle.Current.Text * 0.9f);
            DescLabel.Position = new Vector2(Left + 10, GridTop + 2 * (TileH + 8) + 36);
            DescLabel.Size = new Vector2(PanelWidth - 32 - Left * 2 - 20, 40);
            DescLabel.Wrapped = true;
            DescLabel.MaxLines = 2;
            Add(DescLabel);
            HintLabel = VRUI.Label("", 13, UIStyle.Current.Text * 0.7f);
            HintLabel.Position = new Vector2(Left + 10, GridTop + 2 * (TileH + 8) + 92);
            Add(HintLabel);

            // an object picked up from the lot (the game's pickup panel)
            HeldTitle = VRUI.Label("", 24, UIStyle.Current.SecondaryText, TextAlignment.Center | TextAlignment.Top);
            HeldTitle.Position = new Vector2(Left, GridTop + 50);
            HeldTitle.Size = new Vector2(PanelWidth - 32 - Left * 2, 36);
            Add(HeldTitle);
            HeldText = VRUI.Label("", 18, UIStyle.Current.Text, TextAlignment.Center | TextAlignment.Top);
            HeldText.Position = new Vector2(Left + 40, GridTop + 100);
            HeldText.Size = new Vector2(PanelWidth - 32 - Left * 2 - 80, 80);
            HeldText.Wrapped = true;
            HeldText.MaxLines = 3;
            Add(HeldText);
            SellKey = new UIVRKey("Sell", 230, 66, 24) { Position = new Vector2((PanelWidth - 32) / 2 - 245, GridTop + 220) };
            SellKey.OnPress += _ => { Source?.SellHeld(); Click(); };
            Add(SellKey);
            PutBackKey = new UIVRKey("Put back", 230, 66, 24) { Position = new Vector2((PanelWidth - 32) / 2 + 15, GridTop + 220) };
            PutBackKey.OnPress += _ => { Source?.PutBackHeld(); Click(); };
            Add(PutBackKey);
            SetHeldShown(false);

            var pageY = PanelHeight - 68;
            PrevPage = new UIVRKey("<", 90, 54, 25) { Position = new Vector2(Left + 170, pageY) };
            PrevPage.OnPress += _ => { if (Page > 0) { Page--; ShowPage(); Click(); } };
            Add(PrevPage);
            NextPage = new UIVRKey(">", 90, 54, 25) { Position = new Vector2(PanelWidth - 32 - Left - 170 - 90, pageY) };
            NextPage.OnPress += _ => { if ((Page + 1) * PerPage < (Source?.Items.Count ?? 0)) { Page++; ShowPage(); Click(); } };
            Add(NextPage);
            PageLabel = VRUI.Label("", 19, UIStyle.Current.Text, TextAlignment.Center | TextAlignment.Middle);
            PageLabel.Position = new Vector2(Left + 270, pageY);
            PageLabel.Size = new Vector2(PanelWidth - 32 - Left * 2 - 540, 54);
            Add(PageLabel);
        }

        private static void Click()
        {
            HITVM.Get()?.PlaySoundEvent(UISounds.Click);
            SimitoneVR.Runtime?.Haptic(true, 0.25f, 0.012f);
        }

        private static void Tick() => SimitoneVR.Runtime?.Haptic(true, 0.1f, 0.006f);

        protected override void Refresh(UpdateState state)
        {
            var source = Source;
            if (source == null) return;
            if (source != ShownSource || source.Version != ShownVersion || source.Mode != ShownMode)
            {
                if (source != ShownSource || source.Mode != ShownMode) Page = 0;
                ShownSource = source;
                ShownVersion = source.Version;
                ShownMode = source.Mode;
                Rebuild(source);
            }
            for (int i = 0; i < 3; i++) Tabs[i].Selected = (int)source.Mode == i;
            if (source.Walls != ShownWalls)
            {
                ShownWalls = source.Walls;
                WallsButton.Picture = VRUI.CustomUI(ShownWalls switch
                {
                    VRWallView.Down => "cut_btn_down.png",
                    VRWallView.Up => "cut_btn_up.png",
                    _ => "cut_btn_away.png"
                });
            }
            var floor = source.Floor;
            SetCaption(FloorLabel, FloorNames[Math.Max(0, Math.Min(FloorNames.Length - 1, floor - 1))]);
            FloorDown.Disabled = floor <= 1;
            FloorUp.Disabled = floor >= source.Floors;
            var money = "§" + source.Budget.ToString("##,#0");
            if (MoneyLabel.Caption != money) MoneyLabel.Caption = money;

            // something picked up from the lot: what it is and selling it, instead of the catalogue (as the game does)
            var held = source.Held;
            if ((held != null) != HeldShown)
            {
                SetHeldShown(held != null);
                if (held == null) ShowPage();
            }
            if (held != null)
            {
                SetCaption(HeldTitle, held.Name ?? "");
                SetCaption(HeldText, held.Text ?? "");
                SellKey.Visible = held.CanSell;
                SellKey.Caption = held.Value > 0 ? "Sell" : "Delete";
                SetCaption(NameLabel, "");
                SetCaption(PriceLabel, "");
                SetCaption(DescLabel, "");
                SetCaption(HintLabel, "Aim the arc and pull the trigger to put it down somewhere else.");
                return;
            }

            // the tiles' pictures (3D when ready) and selection, and what's described below them
            var items = source.Items;
            for (int i = 0; i < PerPage; i++)
            {
                var index = Page * PerPage + i;
                var tile = Tiles[i];
                if (index >= items.Count) { tile.Visible = false; continue; }
                var entry = items[index];
                tile.Visible = true;
                tile.SetPrice(entry.Price);
                tile.Picture = (entry.GUID != 0 ? Thumbnail?.Invoke(entry.GUID, index == Hovered) : null) ?? entry.Icon;
                tile.Is3D = tile.Picture != entry.Icon;
                tile.Selected = index == source.SelectedItem;
                tile.Affordable = source.Mode != VRCatalogMode.Buy || entry.Price <= source.Budget;
            }
            var shown = Hovered >= 0 && Hovered < items.Count ? Hovered : source.SelectedItem;
            if (shown >= 0 && shown < items.Count)
            {
                var entry = items[shown];
                SetCaption(NameLabel, entry.Name);
                SetCaption(PriceLabel, "§" + entry.Price.ToString("##,#0"));
                PriceLabel.CaptionStyle.Color = entry.Price <= source.Budget ? UIStyle.Current.SecondaryText : UIStyle.Current.NegMoney;
                SetCaption(DescLabel, entry.Description ?? "");
                SetCaption(HintLabel, shown == source.SelectedItem
                    ? "Aim the arc and pull the trigger to place it. Grip puts it back."
                    : "Pull the trigger to pick it up.");
            }
            else
            {
                SetCaption(NameLabel, "");
                SetCaption(PriceLabel, "");
                SetCaption(DescLabel, "");
                SetCaption(HintLabel, source.Mode == VRCatalogMode.Live ? ""
                    : source.ActiveCategory < 0 ? "Choose a category." : source.ActiveSubcategory < 0 ? "Choose a subcategory." : "");
            }
        }

        private void SetHeldShown(bool shown)
        {
            HeldShown = shown;
            HeldTitle.Visible = HeldText.Visible = SellKey.Visible = PutBackKey.Visible = shown;
            if (!shown) return;
            foreach (var tile in Tiles) tile.Visible = false;
            EmptyLabel.Visible = false;
            PrevPage.Visible = NextPage.Visible = false;
            PageLabel.Caption = "";
        }

        private static void SetCaption(UILabel label, string text)
        {
            if (label.Caption != text) label.Caption = text;
        }

        private void Rebuild(IVRCatalogSource source)
        {
            foreach (var b in CategoryButtons) Remove(b);
            CategoryButtons.Clear();
            foreach (var (b, l) in SubButtons) { Remove(b); Remove(l); }
            SubButtons.Clear();

            if (source.Mode != VRCatalogMode.Live)
            {
                var cats = source.Categories;
                var spacing = Math.Min(92, (PanelWidth - 32 - Left * 2) / Math.Max(1, cats.Count));
                for (int i = 0; i < cats.Count; i++)
                {
                    var index = i;
                    var button = new UICatButton(cats[i]) { Position = new Vector2(Left + 10 + i * spacing, 118) };
                    button.Selected = i == source.ActiveCategory;
                    button.OnButtonClick += _ => { source.ChooseCategory(index); Click(); };
                    Add(button);
                    CategoryButtons.Add(button);
                }
                var subs = source.Subcategories;
                var subSpacing = Math.Min(130, (PanelWidth - 32 - Left * 2) / Math.Max(1, subs.Count));
                for (int i = 0; i < subs.Count; i++)
                {
                    var index = i;
                    var x = Left + 10 + i * subSpacing;
                    var button = new UICatButton(subs[i].Icon) { Position = new Vector2(x + (subSpacing - 75) / 2, 214) };
                    button.Selected = i == source.ActiveSubcategory;
                    button.Disabled = subs[i].Disabled;
                    button.OnButtonClick += _ => { if (!subs[index].Disabled) { source.ChooseSubcategory(index); Click(); } };
                    var label = VRUI.Label(subs[i].Label, 12, UIStyle.Current.Text, TextAlignment.Center | TextAlignment.Top);
                    label.Position = new Vector2(x, 292);
                    label.Size = new Vector2(subSpacing, 30);
                    label.Wrapped = true;
                    Add(button);
                    Add(label);
                    SubButtons.Add((button, label));
                }
            }
            ShowPage();
        }

        private void ShowPage()
        {
            var source = Source;
            var count = source?.Items.Count ?? 0;
            var pages = Math.Max(1, (count + PerPage - 1) / PerPage);
            Page = Math.Max(0, Math.Min(Page, pages - 1));
            // keep the selected item's page in view when the list changes under it
            if (source != null && source.SelectedItem >= 0 && source.SelectedItem / PerPage != Page && Hovered < 0)
                Page = Math.Min(pages - 1, source.SelectedItem / PerPage);
            PageLabel.Caption = count > PerPage ? $"{Page + 1} / {pages}" : "";
            PrevPage.Visible = NextPage.Visible = count > PerPage;
            Hovered = -1;
            EmptyLabel.Visible = true;
            EmptyLabel.Caption = source == null || source.Mode == VRCatalogMode.Live
                ? "Live mode. Choose Buy or Build above (or squeeze the walking hand's grip)."
                : source.ActiveSubcategory >= 0 && count == 0 ? "Nothing here." : "";
            for (int i = 0; i < PerPage; i++) Tiles[i].Visible = Page * PerPage + i < count;
            if (HeldShown) SetHeldShown(true); // (still showing what you picked up)
        }

        /// <summary>
        /// Puts the tablet in your hand (grip pose, tracking space): above the hand, a little toward your other hand, and
        /// tilted toward you like a clipboard. <paramref name="rightHand"/> mirrors the offset.
        /// </summary>
        public void PlaceInHand(VRPose grip, bool rightHand)
        {
            var side = rightHand ? -1 : 1; // toward the middle of your body
            PanelOrientation = grip.Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.ToRadians(-55));
            PanelPosition = grip.Position + Vector3.Transform(new Vector3(side * 0.06f, 0.11f, -0.02f), grip.Orientation);
        }
    }

    /// <summary>A catalogue tile, as Simitone's catalogue draws one: the round picture background, the picture, the price.</summary>
    public class UIVRCatalogTile : UIContainer
    {
        private readonly Vector2 BoxSize;
        private readonly UILabel PriceLabel;
        private bool Hover;
        public Texture2D Picture;
        public bool Is3D, Selected, Affordable = true;
        public event Action OnChosen;
        public event Action<bool> OnHover;
        private int ShownPrice = int.MinValue;

        public UIVRCatalogTile(int width, int height)
        {
            BoxSize = new Vector2(width, height);
            PriceLabel = VRUI.Label("", 16, UIStyle.Current.Text, TextAlignment.Center | TextAlignment.Middle);
            PriceLabel.Position = new Vector2(0, height - 34);
            PriceLabel.Size = new Vector2(width, 30);
            Add(PriceLabel);
            ListenForMouse(new Rectangle(0, 0, width, height), OnMouse);
        }

        public void SetPrice(int price)
        {
            if (price == ShownPrice) return;
            ShownPrice = price;
            PriceLabel.Caption = "§" + price.ToString("##,#0");
        }

        private void OnMouse(UIMouseEventType type, UpdateState state)
        {
            switch (type)
            {
                case UIMouseEventType.MouseOver: Hover = true; OnHover?.Invoke(true); break;
                case UIMouseEventType.MouseOut: Hover = false; OnHover?.Invoke(false); break;
                case UIMouseEventType.MouseUp: if (Hover) OnChosen?.Invoke(); break;
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var bg = VRUI.CustomUI("pswitch_icon_bg.png");
            var ring = VRUI.CustomUI("pswitch_icon_sel.png");
            var circle = 140f;
            var centre = new Vector2(BoxSize.X / 2, 4 + circle / 2);
            DrawLocalTexture(batch, bg, null, centre - new Vector2(circle / 2), new Vector2(circle / bg.Width), Hover ? new Color(140, 200, 220) : new Color(104, 164, 184));
            var pic = Picture;
            if (pic != null && !pic.IsDisposed)
            {
                Rectangle source;
                float size;
                if (Is3D) { source = new Rectangle(0, 0, pic.Width, pic.Height); size = 128; }
                else if (pic.Width / (float)pic.Height < 1.1f || pic.Width == 127 || pic.Width == 128)
                {
                    // (as the catalogue: square pictures fill the circle's middle)
                    source = new Rectangle(0, 0, pic.Width, pic.Height);
                    size = 100;
                }
                else { source = new Rectangle(0, 0, pic.Width / 2, pic.Height); size = 82; } // (the left half of a two-frame strip)
                var scale = size / Math.Max(source.Width, source.Height);
                var drawn = new Vector2(source.Width, source.Height) * scale;
                DrawLocalTexture(batch, pic, source, centre - drawn / 2, new Vector2(scale));
            }
            if (Selected)
                DrawLocalTexture(batch, ring, null, centre - new Vector2(circle * 1.07f / 2), new Vector2(circle * 1.07f / ring.Width), UIStyle.Current.ActiveSelection);
            PriceLabel.CaptionStyle.Color = Affordable ? UIStyle.Current.Text : UIStyle.Current.NegMoney;
            base.Draw(batch);
        }
    }

    /// <summary>A picture button (the mode tabs): highlighted when pointed at, with a green bar under it when chosen.</summary>
    public class UIVRIconButton : UIContainer
    {
        public Texture2D Picture;
        private readonly float Size;
        private bool Hover;
        public bool Selected, Disabled;
        /// <summary>The picture is a strip of this many frames (the game's button states): only the first is drawn.</summary>
        public int Frames = 1;
        public event Action OnClick;

        public UIVRIconButton(Texture2D picture, float size)
        {
            Picture = picture;
            Size = size;
            ListenForMouse(new Rectangle(0, 0, (int)size, (int)size), (type, state) =>
            {
                if (type == UIMouseEventType.MouseOver) Hover = true;
                else if (type == UIMouseEventType.MouseOut) Hover = false;
                else if (type == UIMouseEventType.MouseUp && Hover && !Disabled) OnClick?.Invoke();
            });
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible) return;
            var px = TextureGenerator.GetPxWhite(batch.GraphicsDevice);
            if (Hover && !Disabled) DrawLocalTexture(batch, px, null, Vector2.Zero, new Vector2(Size, Size), Color.White * 0.12f);
            if (Picture != null)
            {
                var frame = new Rectangle(0, 0, Picture.Width / Math.Max(1, Frames), Picture.Height);
                DrawLocalTexture(batch, Picture, frame, Vector2.Zero, new Vector2(Size / frame.Width, Size / frame.Height),
                    Disabled ? Color.White * 0.25f : Selected || Hover ? Color.White : Color.White * 0.6f);
            }
            if (Selected) DrawLocalTexture(batch, px, null, new Vector2(6, Size + 4), new Vector2(Size - 12, 5), UIStyle.Current.SecondaryText);
            base.Draw(batch);
        }
    }
}
