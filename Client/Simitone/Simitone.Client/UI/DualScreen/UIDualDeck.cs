using FSO.Client;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common.Rendering.Framework.Model;
using FSO.Content;
using FSO.HIT;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.UI.DualScreen
{
    /// <summary>
    /// A lot's controls on the second screen, so the main screen only shows the lot:
    /// - the money, the floor (up and down), the cutaway button and the clock (tap the clock for the speed buttons),
    ///   along the top;
    /// In live (and options) mode:
    /// - the family: tap a Sim to control them (the one you control is outlined);
    /// - the controlled Sim's needs, in the game's two columns;
    /// - the controlled Sim's actions (tap one to cancel it, as on the main screen, which hides its own queue unless the
    ///   controller is using it);
    /// In buy and build mode, the catalog instead (<see cref="UIDualCatalog"/>).
    /// - Live, Buy, Build and Options, along the bottom. Buy and Build show their catalog here; Options opens its panel
    ///   on the main screen (as its mode button does). Tapping the lit one again goes back to Live. From those, Live
    ///   goes back to live mode; in live mode it opens or closes the Sim's panels (needs, relationships, job,
    ///   personality, inventory) on the main screen.
    /// The main screen hides its own copies of these while the second screen shows them (UISimitoneFrontend).
    /// </summary>
    public class UIDualDeck : UIContainer
    {
        private readonly TS1GameScreen Game;
        private readonly float W, H;

        private UIClockPanel Clock;
        private UIStencilButton FloorUp, FloorDown;
        private UILabel FloorLabel;
        private int LastFloor = -1;
        /// <summary>Live mode's part (family, needs, actions); hidden while the catalog shows.</summary>
        private UIContainer LiveGroup = new UIContainer();
        private UIDualCatalog Catalog;
        private UIMoneyPanel Money;
        private UITwoStateButton CutBtn;
        private int LastCut = -1;
        private float LastClockTween = -1;

        private readonly List<Tuple<short, UIAvatarSelectButton>> Family = new List<Tuple<short, UIAvatarSelectButton>>();
        private string FamilyKey;

        private readonly UIMotiveBar[] Motives = new UIMotiveBar[8];
        private UIInteractionQueue Queue;
        private const float QueueScale = 0.75f;
        private static readonly VMMotive[] MotiveOrder =
        {
            VMMotive.Hunger, VMMotive.Comfort, VMMotive.Hygiene, VMMotive.Bladder,
            VMMotive.Energy, VMMotive.Fun, VMMotive.Social, VMMotive.Room
        };

        private static readonly UIMainPanelMode[] TabModes = { UIMainPanelMode.LIVE, UIMainPanelMode.BUY, UIMainPanelMode.BUILD, UIMainPanelMode.OPTIONS };
        private static readonly string[] TabIcons = { "mode_live.png", "mode_buy.png", "mode_build.png", "mode_options.png" };
        private readonly UIContainer[] TabHolders = new UIContainer[4];
        private readonly TabButton[] Tabs = new TabButton[4];
        private const float TabScale = 0.6f;

        public UIDualDeck(TS1GameScreen game, float width, float height)
        {
            Game = game;
            W = width;
            H = height;
            var ui = Content.Get().CustomUI;
            var gd = GameFacade.GraphicsDevice;

            // --- top row: money (left), cutaway and clock (right), as on the main screen
            Money = new UIMoneyPanel(game);
            Money.Position = new Vector2(15, 28);
            Add(Money);

            CutBtn = new UITwoStateButton(ui.Get("cut_btn_down.png").Get(gd));
            CutBtn.Y = 15;
            CutBtn.OnButtonClick += (b) => NextCutaway();
            Add(CutBtn);

            // the floor (as the main screen's panel has it): down, the floor's name, up
            FloorDown = new UIStencilButton(ui.Get("level_down.png").Get(gd));
            FloorDown.Position = new Vector2(152, 15);
            FloorDown.OnButtonClick += (b) => { if (Game.Level > 1) Game.Level--; };
            Add(FloorDown);
            FloorLabel = new UILabel();
            FloorLabel.CaptionStyle = FloorLabel.CaptionStyle.Clone();
            FloorLabel.CaptionStyle.Size = 15;
            FloorLabel.CaptionStyle.Color = UIStyle.Current.Text;
            FloorLabel.Alignment = TextAlignment.Middle | TextAlignment.Center;
            FloorLabel.Position = new Vector2(203, 30);
            FloorLabel.Size = new Vector2(44, 20);
            Add(FloorLabel);
            FloorUp = new UIStencilButton(ui.Get("level_up.png").Get(gd));
            FloorUp.Position = new Vector2(247, 15);
            FloorUp.OnButtonClick += (b) => { if (Game.Level < 5) Game.Level++; };
            Add(FloorUp);

            Clock = new UIClockPanel(game.vm);
            Clock.X = W - (334 + 15);
            Clock.Y = 15;
            Add(Clock);

            Add(LiveGroup);

            // --- needs: two columns of four (hunger, comfort, hygiene, bladder | energy, fun, social, room)
            var colWidth = (W - 45) / 2;
            for (int i = 0; i < 8; i++)
            {
                var x = 15 + (i / 4) * (colWidth + 15);
                var y = 196 + (i % 4) * 44;

                var label = new UILabel();
                label.CaptionStyle = label.CaptionStyle.Clone();
                label.CaptionStyle.Size = 15;
                label.CaptionStyle.Color = UIStyle.Current.Text;
                label.Alignment = TextAlignment.Bottom;
                label.Size = new Vector2(1);
                label.Position = new Vector2(x + 2, y);
                label.Caption = GameFacade.Strings.GetString("f102", (i + 1).ToString());
                LiveGroup.Add(label);

                var bar = new UIMotiveBar();
                bar.Width = (int)colWidth;
                bar.Position = new Vector2(x, y + 6);
                LiveGroup.Add(bar);
                Motives[i] = bar;
            }

            // --- the controlled Sim's actions, under the needs (the running one large, on the left), at 3/4 size to fit
            // between the needs and the modes: the running one's ring is 106 across, centred 73 in
            Queue = new UIInteractionQueue(game.LotControl?.ActiveEntity, game.vm);
            Queue.ScaleX = Queue.ScaleY = QueueScale;
            Queue.Position = new Vector2(15, 404 - 73 * QueueScale);
            LiveGroup.Add(Queue);

            // --- modes, along the bottom
            for (int i = 0; i < 4; i++)
            {
                var mode = TabModes[i];
                var holder = new UIContainer();
                holder.ScaleX = holder.ScaleY = TabScale; //(the button animates its own scale when pressed)
                holder.Position = new Vector2(W * (i + 0.5f) / 4, H - 128 * TabScale / 2 - 12);
                var btn = new TabButton(ui.Get(TabIcons[i]).Get(gd));
                btn.OnButtonClick += (b) => ModeClicked(mode);
                holder.Add(btn);
                Add(holder);
                TabHolders[i] = holder;
                Tabs[i] = btn;
            }
        }

        public override void Update(UpdateState state)
        {
            UpdateFamily();
            UpdateMotives();
            var owner = Game.LotControl?.ActiveEntity;
            if (Queue.QueueOwner != owner) Queue.QueueOwner = owner;
            UpdateCutaway();
            UpdateFloor();
            UpdateCatalog();
            UpdateTabs();
            base.Update(state);
        }

        // ---- floor

        private void UpdateFloor()
        {
            var level = Game.Level;
            if (level != LastFloor)
            {
                LastFloor = level;
                var names = Game.Frontend?.MainPanel?.FloorNames;
                FloorLabel.Caption = (names != null && level >= 1 && level <= names.Length) ? names[level - 1] : level.ToString();
                FloorDown.Disabled = level <= 1;
                FloorUp.Disabled = level >= 5;
            }
            // (the clock's speed buttons, while it shows them, reach over here)
            var show = Clock.TweenHook == 0;
            FloorDown.Visible = FloorUp.Visible = FloorLabel.Visible = show;
        }

        // ---- buy and build: the catalog in place of live mode's part

        private void UpdateCatalog()
        {
            var mode = Game.Frontend?.MainPanel?.Mode ?? UIMainPanelMode.LIVE;
            var catalogMode = mode == UIMainPanelMode.BUY || mode == UIMainPanelMode.BUILD;
            if (Catalog != null && (!catalogMode || Catalog.Mode != mode))
            {
                Catalog.Destroy();
                Catalog = null;
            }
            if (catalogMode && Catalog == null)
            {
                Catalog = new UIDualCatalog(Game, mode, W, 70);
                Add(Catalog);
            }
            LiveGroup.Visible = !catalogMode;
        }

        /// <summary>The second screen leaves the lot: its catalog lets go of what it picked.</summary>
        public override void Removed()
        {
            Catalog?.Destroy();
            Catalog = null;
            base.Removed();
        }

        // ---- family

        private List<VMAvatar> FamilyMembers()
        {
            var family = Game.vm?.TS1State?.CurrentFamily;
            if (family == null) return new List<VMAvatar>();
            return Game.vm.Context.ObjectQueries.Avatars
                .Where(x => family.RuntimeSubset.Contains(x.Object.OBJ.GUID))
                .Cast<VMAvatar>()
                .ToList();
        }

        private void UpdateFamily()
        {
            var members = FamilyMembers();
            var key = string.Join(",", members.Select(x => x.ObjectID));
            if (key != FamilyKey)
            {
                FamilyKey = key;
                foreach (var old in Family) LiveGroup.Remove(old.Item2);
                Family.Clear();
                var spacing = Math.Min(100f, (W - 30) / Math.Max(1, members.Count));
                var start = W / 2 - spacing * (members.Count - 1) / 2;
                int i = 0;
                foreach (var sim in members)
                {
                    var id = sim.ObjectID;
                    var btn = new UIAvatarSelectButton(UIIconCache.GetObject(sim));
                    btn.Name = sim.Name;
                    btn.Position = new Vector2(start + spacing * (i++), 112);
                    btn.OnButtonClick += (b) => SelectSim(id);
                    LiveGroup.Add(btn);
                    Family.Add(Tuple.Create(id, btn));
                }
            }
            var selected = Game.SelectedAvatar?.ObjectID ?? -1;
            foreach (var entry in Family) entry.Item2.Outlined = entry.Item1 == selected;
        }

        private void SelectSim(short id)
        {
            if (Game.SelectedAvatar?.ObjectID == id) return;
            HITVM.Get().PlaySoundEvent(UISounds.QueueAdd);
            Game.vm.SendCommand(new VMNetChangeControlCmd() { TargetID = id });
        }

        // ---- needs

        private void UpdateMotives()
        {
            var sim = Game.SelectedAvatar;
            for (int i = 0; i < 8; i++)
            {
                Motives[i].Visible = sim != null;
                if (sim != null) Motives[i].MotiveValue = sim.GetMotiveData(MotiveOrder[i]);
            }
        }

        // ---- cutaway: each tap goes to the next wall view (cut away, walls up, roof, walls down)

        private static readonly string[] CutImages = { "cut_btn_down.png", "cut_btn_away.png", "cut_btn_up.png", "cut_btn_roof.png" };

        private void NextCutaway()
        {
            var lot = Game.LotControl;
            if (lot == null) return;
            var next = (lot.WallsMode + 1) % 4;
            lot.World.State.DrawRoofs = next == 3;
            lot.WallsMode = next;
        }

        private void UpdateCutaway()
        {
            var mode = Game.LotControl?.WallsMode ?? 0;
            if (mode != LastCut && mode >= 0 && mode < 4)
            {
                LastCut = mode;
                CutBtn.Texture = Content.Get().CustomUI.Get(CutImages[mode]).Get(GameFacade.GraphicsDevice);
            }
            // (as on the main screen: the button moves aside while the clock shows its speed buttons)
            if (Clock.TweenHook != LastClockTween)
            {
                LastClockTween = Clock.TweenHook;
                CutBtn.X = W - (256 + 138f * LastClockTween + 15);
            }
        }

        // ---- modes

        private UIMainPanelMode ShownMode()
        {
            var panel = Game.Frontend?.MainPanel;
            if (panel == null) return UIMainPanelMode.LIVE;
            return panel.Mode;
        }

        private void UpdateTabs()
        {
            var panel = Game.Frontend?.MainPanel;
            var mode = ShownMode();
            var buildBuy = Game.vm?.Context?.Architecture?.BuildBuyEnabled ?? false;
            for (int i = 0; i < 4; i++)
            {
                var tabMode = TabModes[i];
                if (tabMode == UIMainPanelMode.BUY || tabMode == UIMainPanelMode.BUILD) Tabs[i].Disabled = !buildBuy;
                // the current mode is lit; Live is lit fully only while its panel is open on the main screen
                var lit = tabMode == mode && (tabMode != UIMainPanelMode.LIVE || (panel?.PanelActive ?? false));
                Tabs[i].Brightness = lit ? 1f : (tabMode == mode ? 0.8f : 0.5f);
            }
        }

        private void ModeClicked(UIMainPanelMode mode)
        {
            var frontend = Game.Frontend;
            var panel = frontend?.MainPanel;
            if (panel == null || panel.ShowingSelect) return;
            if ((mode == UIMainPanelMode.BUY || mode == UIMainPanelMode.BUILD) && !Game.vm.Context.Architecture.BuildBuyEnabled) return;

            if (panel.PanelActive && panel.Mode == mode)
            {
                // the lit one again: close it, back to Live
                panel.Close();
                if (mode != UIMainPanelMode.LIVE) panel.SetMode(UIMainPanelMode.LIVE);
            }
            else if (mode == UIMainPanelMode.LIVE && panel.Mode != UIMainPanelMode.LIVE)
            {
                // Live from buy, build or options: back to live mode, the main screen showing just the lot
                panel.SetMode(UIMainPanelMode.LIVE);
                if (panel.PanelActive) panel.Close();
            }
            else if (mode == UIMainPanelMode.BUY || mode == UIMainPanelMode.BUILD)
            {
                // their catalog is on this screen: the lit one again goes back to live mode
                var back = panel.Mode == mode;
                if (panel.PanelActive) panel.Close();
                panel.SetMode(back ? UIMainPanelMode.LIVE : mode);
            }
            else
            {
                if (!panel.PanelActive) panel.Open();
                panel.SetMode(mode);
                if (mode == UIMainPanelMode.LIVE) panel.Switcher_OnCategorySelect(panel.Switcher.ActiveCategory);
            }
            frontend.ModeSwitcher?.EndSwitch(panel.Mode);
        }

        /// <summary>A mode button that's dimmed unless it's the current mode (the UI's opacity doesn't reach these).</summary>
        private class TabButton : UIElasticButton
        {
            public float Brightness = 1f;

            public TabButton(Microsoft.Xna.Framework.Graphics.Texture2D tex) : base(tex) { }

            public override void Draw(UISpriteBatch batch)
            {
                if (!Visible) return;
                DrawLocalTexture(batch, Texture, null, new Vector2(Texture.Width, Texture.Height) / -2, Vector2.One,
                    Color.White * (Disabled ? 0.3f : Brightness));
            }
        }
    }
}
