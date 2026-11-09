
using FSO.Client;
using FSO.Client.Debug;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Common;
using FSO.Common.Rendering.Framework;
using FSO.Common.Utils;
using FSO.Content;
using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;
using FSO.Files.RC;
using FSO.HIT;
using FSO.LotView;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Engine.TSOTransaction;
using FSO.SimAntics.Marshals;
using FSO.SimAntics.Model;
using FSO.SimAntics.NetPlay;
using FSO.SimAntics.NetPlay.Drivers;
using FSO.SimAntics.NetPlay.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using FSO.SimAntics.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Panels.WorldUI;
using Simitone.Client.Utils;
using Simitone.Client.Services;
using System;
using FSO.Common.Rendering.Framework.IO;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Simitone.Client.UI.Screens
{
    public class TS1GameScreen : FSO.Client.UI.Framework.GameScreen, IPadScope
    {
        public UIContainer WindowContainer;
        public bool Downtown;
        /// <summary>
        /// True for the desktop lot layout (UIDesktopUCP), false for the mobile one. Chosen when each lot's frontend
        /// is created (see CreateFrontend); with UILayout "auto" it then follows the device in use, and the frontend is
        /// rebuilt when it changes (see UpdateAutoLayout).
        /// </summary>
        public bool Desktop = !FSOEnvironment.SoftwareKeyboard;
        private InputDevice LastInputDevice;

        public UILotControl LotControl { get; set; }
        public UISimitoneFrontend Frontend { get; set; }
        private FSO.LotView.World World;
        public FSO.SimAntics.VM vm { get; set; }
        public VMNetDriver Driver;
        public UISimitoneBg Bg;
        public uint VisualBudget { get; set; }

        //for TS1 hybrid mode
        public UINeighborhoodSelectionPanel TS1NeighPanel;
        private UIScreenPadNavigator NeighPadNav; //gamepad on the neighbourhood map and house panel

        /// <summary>Showing the neighbourhood map (rather than a lot).</summary>
        public bool InNeighbourhood => TS1NeighPanel != null && GetChildren().Contains(TS1NeighPanel);

        /// <summary>
        /// The map on screen: the neighbourhood, or a town a Sim is travelling to from a lot (vacation, Studio Town,
        /// Magic Town...), which the lot shows over itself. Null if neither.
        /// </summary>
        public UINeighborhoodSelectionPanel ActiveNeighPanel
        {
            get
            {
                if (InNeighbourhood) return TS1NeighPanel;
                var travel = LotControl?.TravelSelector;
                return (travel?.Parent == this) ? travel : null;
            }
        }
        public FAMI ActiveFamily;
        private ushort CurrentNeighborhoodMode = 4; // default to Normal/Old Town

        // Weather effects state tracking
        private float LastSoundIntensity = -1;
        private WeatherType LastWeatherType = WeatherType.Rain;
        private bool LastThunder = false;
        private bool TerrainSnowApplied = false;
        private float ThunderTimer = 0f;
        private Random ThunderRandom = new Random();
        private short? PendingWeatherData = null;

        public bool InLot
        {
            get
            {
                return (vm != null);
            }
        }

        private int m_ZoomLevel;
        public int ZoomLevel
        {
            get
            {
                return m_ZoomLevel;
            }
            set
            {
                value = Math.Max(1, Math.Min(3, value));

                if (value < 4)
                {
                    if (vm == null)
                    {

                    }
                    else
                    {
                        var targ = (WorldZoom)(4 - value); //near is 3 for some reason... will probably revise
                        //HITVM.Get().PlaySoundEvent(UIMusic.None);
                        LotControl.Visible = true;
                        Bg.Visible = false;
                        World.Visible = true;
                        //ucp.SetMode(UIUCP.UCPMode.LotMode);
                        LotControl.SetTargetZoom(targ);
                        if (m_ZoomLevel != value) vm.Context.World.InitiateSmoothZoom(targ);
                        vm.Context.World.State.Zoom = targ;
                        m_ZoomLevel = value;
                    }
                }
                else //cityrenderer! we'll need to recreate this if it doesn't exist...
                {
                    if (m_ZoomLevel < 4)
                    { //coming from lot view... snap zoom % to 0 or 1
                        if (World != null)
                        {
                            LotControl.Visible = false;
                        }
                    }
                    m_ZoomLevel = value;
                }
            }
        }

        private int _Rotation = 0;
        public int Rotation
        {
            get
            {
                return _Rotation;
            }
            set
            {
                _Rotation = value;
                World.State.CenterTile = World.EstTileAtPosWithScroll(new Vector2(ScreenWidth / 2, ScreenHeight / 2));
                if (World != null)
                {
                    switch (_Rotation)
                    {
                        case 0:
                            World.State.Rotation = WorldRotation.TopLeft; break;
                        case 1:
                            World.State.Rotation = WorldRotation.TopRight; break;
                        case 2:
                            World.State.Rotation = WorldRotation.BottomRight; break;
                        case 3:
                            World.State.Rotation = WorldRotation.BottomLeft; break;
                    }
                }
                World.RestoreTerrainToCenterTile();
            }
        }

        public sbyte Level
        {
            get
            {
                if (World == null) return 1;
                else return World.State.Level;
            }
            set
            {
                if (World != null)
                {
                    World.State.Level = value;
                }
            }
        }

        public sbyte Stories
        {
            get
            {
                if (World == null) return 2;
                return World.Stories;
            }
        }

        public VMAvatar SelectedAvatar
        {
            get
            {
                return vm.GetAvatarByPersist(vm.MyUID);
            }
        }

        private ushort GetNeighborhoodModeFromHouse(short house)
        {
            // House number ranges match the lot types
            if (house >= 21 && house <= 31) return 2;  // Downtown
            else if (house >= 40 && house <= 49) return 3;  // Vacation
            else if (house >= 81 && house <= 90) return 5;  // Studiotown
            else if (house >= 90 && house <= 99) return 7;  // Magictown
            else return 4;  // Normal/Old Town (default)
        }

        public TS1GameScreen(NeighSelectionMode mode) : base()
        {
            Bg = new UISimitoneBg();
            Bg.Position = (new Vector2(ScreenWidth, ScreenHeight)) / 2;
            Add(Bg);

            WindowContainer = new UIContainer();
            Add(WindowContainer);

            if (Content.Get().TS1)
            {
                NeighSelection(mode);

                // Check for updates from GitHub releases
                UpdateChecker.CheckForUpdatesAsync();
            }
        }
        public int? MoveInFamily;

        public void StartMoveIn(int familyID)
        {
            MoveInFamily = familyID;
        }

        public void NeighSelection(NeighSelectionMode mode)
        {
            var nbd = (ushort)((mode == NeighSelectionMode.MoveInMagic) ? 7 : 4);
            NeighSelection(nbd, mode != NeighSelectionMode.Normal);
        }

        public void NeighSelection(ushort neighborhoodMode, bool moveInMode = false)
        {
            Content.Get().Neighborhood.PreparePersonDataFromObject = PersonGeneratorHelper.PreparePersonDataFromObject;
            Content.Get().Neighborhood.AddMissingNeighbors();
            TS1NeighPanel = new UINeighborhoodSelectionPanel(neighborhoodMode);
            var switcher = new UINeighbourhoodSwitcher(TS1NeighPanel, neighborhoodMode, moveInMode);
            TS1NeighPanel.OnHouseSelect += (house) =>
            {
                if (MoveInFamily != null)
                {
                    //move them in first
                    //confirm it
                    UIMobileAlert confirmDialog = null;
                    confirmDialog = new UIMobileAlert(new UIAlertOptions()
                    {
                        Title = GameFacade.Strings.GetString("132", "0"),
                        Message = GameFacade.Strings.GetString("132", "1"),
                        Buttons = UIAlertButton.YesNo((b) =>
                        {
                            confirmDialog.Close();
                            MoveInAndPlay((short)house, MoveInFamily.Value, switcher);
                        },
                        (b) => confirmDialog.Close())
                    });
                    UIScreen.GlobalShowDialog(confirmDialog, true);
                }
                else
                {
                    PlayHouse((short)house, switcher);
                }
            };
            Add(TS1NeighPanel);
            Add(switcher);
            CreateNeighPadNav();
            if (TestMode.TakeLot(out var testLot)) PlayHouse(testLot, switcher);
        }

        /// <summary>The pad's navigator for the map on screen (<see cref="ActiveNeighPanel"/>), replacing any old one.</summary>
        private void CreateNeighPadNav()
        {
            if (NeighPadNav != null) Remove(NeighPadNav);
            var nav = new UIScreenPadNavigator(NeighbourhoodPadScope);
            NeighPadNav = nav;
            //uses its own navigator, not the field: entering a lot clears the field while this navigator is updating
            nav.Prompts = () =>
            {
                var housePanel = ActiveNeighPanel?.LastHS;
                if (housePanel?.Parent != null && nav.Navigator.Scope == housePanel)
                    return new[] { PadPrompt.For(PadStick.DPad, "Move"), PadPrompt.Of(PadAction.Confirm, "Select"), PadPrompt.Of(PadAction.Cancel, "Back") };
                var label = (nav.Navigator.Focused is UINeighborhoodHouseButton house) ? house.PadLabel(MoveInFamily != null) : "Select";
                var items = new List<PadPrompt> { PadPrompt.For(PadStick.DPad, "Move"), PadPrompt.Of(PadAction.Confirm, label) };
                if (GamePadSettings.VirtualCursor) items.Add(PadPrompt.Of(PadAction.FaceLeft, "Cursor"));
                return items.ToArray();
            };
            NeighPadNav.FocusChanged += (previous, current) =>
            {
                (previous as UINeighborhoodHouseButton)?.SetPadHover(false);
                (current as UINeighborhoodHouseButton)?.SetPadHover(true);
            };
            Add(NeighPadNav);
        }

        /// <summary>
        /// What the pad navigates on the neighbourhood screen: the open house panel, or else the whole screen (the
        /// houses on the map and the neighbourhood buttons). Null outside the neighbourhood.
        /// </summary>
        private UIElement NeighbourhoodPadScope()
        {
            var map = ActiveNeighPanel;
            if (map == null) return null;
            var house = map.LastHS;
            if (house?.Parent != null) return house;
            if (NeighCursorActive) return null; //the virtual cursor has the pad
            return this;
        }

        /// <summary>
        /// Neighbourhood pad mode, toggled with X. Browse (false) jumps between the lots and buttons with the focus
        /// frame; cursor (true) hands the map to the virtual cursor: the left stick moves the pointer and A clicks, which
        /// is quicker for reaching a lot across the map. The house panel is navigated as usual either way. Remembered
        /// for the session; needs the virtual cursor setting.
        /// </summary>
        private static bool NeighCursorMode;
        private bool NeighCursorActive => NeighCursorMode && GamePadSettings.VirtualCursor;

        private void UpdateNeighbourhoodPad(FSO.Common.Rendering.Framework.Model.UpdateState state)
        {
            //a travel map (from a lot) gets the neighbourhood's navigator while it's up
            var panel = ActiveNeighPanel;
            if (panel != null && NeighPadNav == null) CreateNeighPadNav();
            else if (panel == null && NeighPadNav != null && TS1NeighPanel?.Parent == null)
            {
                Remove(NeighPadNav);
                NeighPadNav = null;
            }
            if (panel?.Parent == null || NeighPadNav == null) return;
            var pad = state.Pad;
            var free = pad.Connected && GlobalSettings.Default.GamePadEnabled && !state.PadTextEntry
                && state.InputManager?.GetFocus() == null && !GameFacade.Screens.HasModalDialog
                && state.LastInputDevice == InputDevice.GamePad;
            if (!free) return;

            if (panel.LastHS?.Parent != null)
            {
                //the house panel has the pad; when it closes, the cursor comes back on its lot
                if (NeighCursorActive && panel.HouseButtons.TryGetValue(panel.LastHouse, out var open))
                    state.PadCursorStart = UIFocusNavigator.GlobalRect(open).Center;
                return;
            }

            if (GamePadSettings.VirtualCursor && pad.Pressed(PadAction.FaceLeft))
            {
                NeighCursorMode = !NeighCursorMode;
                if (NeighCursorMode)
                {
                    //start the cursor on whatever had the focus
                    var focused = NeighPadNav.Navigator.Focused;
                    if (focused != null) state.PadCursorStart = UIFocusNavigator.GlobalRect(focused).Center;
                }
                else
                {
                    //and the focus on the lot under the cursor
                    var under = panel.HouseButtons.Values.FirstOrDefault(b => b.MouseHovered);
                    if (under != null) panel.LastHouse = under.House;
                }
                HITVM.Get().PlaySoundEvent(UISounds.Click);
                return;
            }

            if (!NeighCursorActive) return;
            var hovered = panel.HouseButtons.Values.FirstOrDefault(b => b.MouseHovered);
            if (hovered != null)
            {
                //name it, as the focus does in browse mode, clear of the other lots
                PadFocusStyle.Label(hovered, UIFocusNavigator.GlobalRect(hovered), null); //lot pictures may be covered
            }
            PadPrompts.Set(PadPrompt.For(PadStick.Left, "Cursor"),
                PadPrompt.Of(PadAction.Confirm, hovered?.PadLabel(MoveInFamily != null) ?? "Select"),
                PadPrompt.Of(PadAction.FaceLeft, "Browse"));
        }

        /// <summary>Neighbourhood map: start on the house last selected, or the one nearest the middle.</summary>
        public UIElement PadDefaultFocus
        {
            get
            {
                var panel = ActiveNeighPanel;
                if (panel == null || panel.HouseButtons.Count == 0) return null;
                if (panel.HouseButtons.TryGetValue(panel.LastHouse, out var last)) return last;
                var middle = new Vector2(ScreenWidth, ScreenHeight) / 2;
                return panel.HouseButtons.Values
                    .OrderBy(b => Vector2.DistanceSquared(UIFocusNavigator.GlobalRect(b).Center.ToVector2(), middle))
                    .First();
            }
        }

        public bool PadCancel() => false;

        public void PlayHouse(short house, UIElement switcher)
        {
            ActiveFamily = Content.Get().Neighborhood.GetFamilyForHouse((short)house);
            CurrentNeighborhoodMode = GetNeighborhoodModeFromHouse(house);
            InitializeLot(Content.Get().Neighborhood.GetHousePath(house), false);// "UserData/Houses/House21.iff"
            Remove(TS1NeighPanel);
            if (NeighPadNav != null)
            {
                Remove(NeighPadNav);
                NeighPadNav = null;
            }
            if (switcher != null) Remove(switcher);
        }

        public void MoveInAndPlay(short house, int family, UIElement switcher)
        {
            MoveInFamily = null;
            var neigh = Content.Get().Neighborhood;
            var fami = neigh.GetFamily((ushort)family);
            neigh.SetFamilyForHouse(house, fami, true);
            PlayHouse(house, switcher);
        }

        public void EvictLot(FAMI family, short houseID)
        {
            family.Budget += family.ValueInArch;
            family.ValueInArch = 0;
            Content.Get().Neighborhood.MoveOut(houseID);
            TS1NeighPanel.SelectHouse(houseID);
        }

        public override void GameResized()
        {
            base.GameResized();
            Bg.Position = (new Vector2(ScreenWidth, ScreenHeight)) / 2;
            World?.GameResized();
        }

        public void Initialize(string propertyName, bool external)
        {
            GameFacade.CurrentCityName = propertyName;
            ZoomLevel = 1; //screen always starts at near zoom
            InitializeLot(propertyName, external);
        }

        private int SwitchLot = -1;

        // As in TS1: while none of the household's Sims on the lot is awake (all at work or school, or all asleep),
        // run at ultra speed, then go back to the previous speed when one of them returns or wakes up.
        private bool AutoFastForward;
        private int SpeedBeforeAutoFastForward;
        private bool AutoFastForwardDeclined; // the user changed speed while everyone was away or asleep

        /// <summary>Running at ultra speed by itself (auto fast-forward): the speed 3 button flashes, as in TS1.</summary>
        public bool UltraSpeed => AutoFastForward && vm?.SpeedMultiplier == 10;

        /// <summary>Whether a flashing button is lit now (twice a second, half the time).</summary>
        public static bool FlashOn => (Environment.TickCount / 500) % 2 == 0;

        /// <summary>
        /// Flashes the speed 3 button between its selected and plain look while ultra is on, and puts it back once when
        /// it goes off. It's left alone otherwise, so the pad's highlight (ForceState 2) can use it. Returns whether
        /// it's flashing.
        /// </summary>
        public static bool FlashSpeed3(UIButton button, bool ultra, bool wasFlashing)
        {
            if (ultra)
            {
                if (button.ForceState != 2) button.ForceState = FlashOn ? 1 : 0;
                return true;
            }
            if (wasFlashing && (button.ForceState == 0 || button.ForceState == 1)) button.ForceState = -1;
            return false;
        }

        private void UpdateAutoFastForward()
        {
            var family = vm?.TS1State?.CurrentFamily;
            if (vm == null || !vm.Ready || family == null) return;
            //household Sims who aren't on the lot (at work or school) are kept as objects out of the world (as
            //VMTS1LotState creates absent members), so only Sims in the world count as home; and a Sim asleep (their
            //sleep state motive is set, as the motive decay reads it) doesn't count as awake
            var present = vm.Context.ObjectQueries.Avatars.Any(x => !x.Dead && family.RuntimeSubset.Contains(x.Object.OBJ.GUID)
                && x.Position != FSO.LotView.Model.LotTilePos.OUT_OF_WORLD
                && (x as VMAvatar)?.GetMotiveData(VMMotive.SleepState) == 0);

            if (present) AutoFastForwardDeclined = false;

            if (!AutoFastForward)
            {
                // only while running: never override pause (0), menus (-1) or speed changes the user made
                if (!present && !AutoFastForwardDeclined && vm.SpeedMultiplier > 0 && vm.SpeedMultiplier != 10)
                {
                    SpeedBeforeAutoFastForward = vm.SpeedMultiplier;
                    ChangeSpeedTo(3);
                    AutoFastForward = true;
                }
            }
            else if (vm.SpeedMultiplier > 0 && vm.SpeedMultiplier != 10)
            {
                AutoFastForward = false; // the user picked a speed themselves; leave it until someone is home and awake
                AutoFastForwardDeclined = true;
            }
            else if (present && vm.SpeedMultiplier == 10)
            {
                ChangeSpeedTo((SpeedBeforeAutoFastForward == 3) ? 2 : 1);
                AutoFastForward = false;
            }
        }

        public void ChangeSpeedTo(int speed)
        {
            //0 speed is 0x
            //1 speed is 1x
            //2 speed is 3x
            //3 speed is 10x

            if (vm == null) return;
            if (vm.SpeedMultiplier == -1) return;

            switch (vm.SpeedMultiplier)
            {
                case 0:
                    switch (speed)
                    {
                        case 1:
                            HITVM.Get().PlaySoundEvent(UISounds.SpeedPTo1); break;
                        case 2:
                            HITVM.Get().PlaySoundEvent(UISounds.SpeedPTo2); break;
                        case 3:
                            HITVM.Get().PlaySoundEvent(UISounds.SpeedPTo3); break;
                    }
                    break;
                case 1:
                    switch (speed)
                    {
                        case 0:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed1ToP); break;
                        case 2:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed1To2); break;
                        case 3:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed1To3); break;
                    }
                    break;
                case 3:
                    switch (speed)
                    {
                        case 0:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed2ToP); break;
                        case 1:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed2To1); break;
                        case 3:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed2To3); break;
                    }
                    break;
                case 10:
                    switch (speed)
                    {
                        case 0:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed3ToP); break;
                        case 1:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed3To1); break;
                        case 2:
                            HITVM.Get().PlaySoundEvent(UISounds.Speed3To2); break;
                    }
                    break;
            }

            switch (speed)
            {
                case 0: vm.SpeedMultiplier = 0; break;
                case 1: vm.SpeedMultiplier = 1; break;
                case 2: vm.SpeedMultiplier = 3; break;
                case 3: vm.SpeedMultiplier = 10; break;
            }
            vm.ResetTickAlign();
        }

        public override void Update(FSO.Common.Rendering.Framework.Model.UpdateState state)
        {
            GameFacade.Game.IsFixedTimeStep = (vm == null || vm.Ready);
            
            Visible = World?.Visible != false && World?.State.Cameras.HideUI != true;
            LastInputDevice = state.LastInputDevice;
            //the lot hides the pointer while it drives the pad natively; it comes back for the mouse or the virtual cursor.
            GameFacade.Game.IsMouseVisible = Visible && !(state.PadWasCaptured && state.LastInputDevice == InputDevice.GamePad);
            TestMode.Update(this);

            var nofocus = state.InputManager.GetFocus() == null;
            if (nofocus && state.NewKeys.Contains(Keys.D1)) ChangeSpeedTo(1);
            if (nofocus && state.NewKeys.Contains(Keys.D2)) ChangeSpeedTo(2);
            if (nofocus && state.NewKeys.Contains(Keys.D3)) ChangeSpeedTo(3);
            if (nofocus && state.NewKeys.Contains(Keys.P)) ChangeSpeedTo(0);
            if (nofocus && state.NewKeys.Contains(Keys.D0))
            {
                //frame advance
                ChangeSpeedTo(1);
                GameThread.NextUpdate((FSO.Common.Rendering.Framework.Model.UpdateState ustate) => ChangeSpeedTo(0));
            }
            base.Update(state);
            UpdateNeighbourhoodPad(state);
            UpdateAutoLayout(state);
            UpdateAutoFastForward();

            // Update weather effects (sounds and terrain)
            UpdateWeatherEffects();

            if (state.NewKeys.Contains(Microsoft.Xna.Framework.Input.Keys.F12) && GraphicsModeControl.Mode != GlobalGraphicsMode.Full2D)
            {
                GraphicsModeControl.ChangeMode((GraphicsModeControl.Mode == GlobalGraphicsMode.Full3D) ? GlobalGraphicsMode.Hybrid2D : GlobalGraphicsMode.Full3D);
            }

            /*
            if (state.NewKeys.Contains(Keys.F12))
            {
                ChangeSpeedTo(1);
                //running 10000 ticks
                var timer = new System.Diagnostics.Stopwatch();
                timer.Start();

                for (int i=0; i<10000; i++)
                {
                    vm.Tick();
                }

                timer.Stop();
                UIScreen.GlobalShowDialog(new UIMobileAlert(new UIAlertOptions() {
                    Title = "Benchmark",
                    Message = "10000 ticks took " + timer.ElapsedMilliseconds + "ms."
                }), true);
            }
            */

            if (World != null)
            {
                //stub smooth zoom?
            }

            if (SwitchLot > 0)
            {
                if (!Downtown) SavedLot = vm.Save();
                if (SwitchLot == ActiveFamily.HouseNumber && SavedLot != null)
                {
                    Downtown = false;
                    InitializeLot(SavedLot);
                    SavedLot = null;
                }
                else
                {
                    Downtown = true;
                    InitializeLot(Content.Get().Neighborhood.GetHousePath(SwitchLot), false);
                }
                SwitchLot = -1;
            }
            //vm.Context.Clock.Hours = 12;
            if (vm != null) vm.Update();

            //SaveHouseButton_OnButtonClick(null);
        }

        private void UpdateWeatherEffects()
        {
            // Apply deferred weather state once Blueprint is available
            if (PendingWeatherData.HasValue && vm?.Context?.Blueprint?.Weather != null)
            {
                vm.Context.Blueprint.Weather.SetWeather(PendingWeatherData.Value);
                PendingWeatherData = null;
            }

            if (vm?.Context?.Blueprint?.Weather == null) return;

            var weather = vm.Context.Blueprint.Weather;
            var currentType = weather.WeatherType;
            var currentThunder = weather.IsThunder;
            var currentIntensity = weather.WeatherIntensity;

            // Thunder timer runs every frame regardless of state changes
            if (LastThunder && LastWeatherType == WeatherType.Rain && LastSoundIntensity > 0.1f && vm?.SpeedMultiplier > 0)
            {
                ThunderTimer -= 1f / 60f;
                if (ThunderTimer <= 0)
                {
                    WeatherSounds.PlayThunder(LastSoundIntensity * 0.8f);
                    ThunderTimer = 5f + (float)ThunderRandom.NextDouble() * 10f;
                }
            }

            // Pause/resume rain sound to match game speed
            // SpeedMultiplier is 0 when explicitly paused, -1 in build/buy/options mode
            if (vm.SpeedMultiplier <= 0)
                WeatherSounds.PauseRain();
            else
                WeatherSounds.ResumeRain();

            bool stateChanged = currentType != LastWeatherType ||
                              currentThunder != LastThunder ||
                              Math.Abs(currentIntensity - LastSoundIntensity) > 0.05f;

            if (!stateChanged) return;

            if (currentType == WeatherType.Rain && currentIntensity > 0.1f)
                WeatherSounds.PlayRain(currentIntensity);
            else
            {
                WeatherSounds.StopRain();
                ThunderTimer = 0f;
            }

            if (vm?.Context?.Blueprint?.Terrain != null)
            {
                if (currentType == WeatherType.Snow && currentIntensity > 0.1f)
                {
                    // if it's snowing, make the grass snowy
                    if (!TerrainSnowApplied)
                    {
                        var terrain = vm.Context.Blueprint.Terrain;
                        terrain.ForceSnow(0);
                        terrain.UpdateLotType();
                        terrain.TerrainDirty = true;
                        TerrainSnowApplied = true;
                    }
                }
                else
                {   // remove it when it stops #TODO, don't do this on snow vacation lots
                    if (TerrainSnowApplied)
                    {
                        var terrain = vm.Context.Blueprint.Terrain;
                        terrain.ForceSnow(1);
                        terrain.UpdateLotType();
                        terrain.TerrainDirty = true;
                        TerrainSnowApplied = false;
                    }
                }
            }

            LastWeatherType = currentType;
            LastThunder = currentThunder;
            LastSoundIntensity = currentIntensity;
        }

        public override void PreDraw(UISpriteBatch batch)
        {
            base.PreDraw(batch);
            vm?.PreDraw();
        }

        public void CleanupLastWorld()
        {
            if (vm == null) return;

            //clear our cache too, if the setting lets us do that
            TimedReferenceController.Clear();
            TimedReferenceController.Clear();

            if (ZoomLevel < 4) ZoomLevel = 5;
            WeatherSounds.StopRain();
            LastSoundIntensity = -1;
            LastWeatherType = WeatherType.Rain;
            LastThunder = false;
            vm.Context.Ambience.Kill();
            foreach (var ent in vm.Entities)
            { //stop object sounds
                var threads = ent.SoundThreads;
                for (int i = 0; i < threads.Count; i++)
                {
                    threads[i].Sound.RemoveOwner(ent.ObjectID);
                }
                threads.Clear();
            }
            vm.CloseNet(VMCloseNetReason.LeaveLot);
            GameFacade.Scenes.Remove(World);
            World.Dispose();
            //LotControl.Dispose();
            this.Remove(LotControl);
            this.Remove(Frontend);
            vm.SuppressBHAVChanges();
            vm = null;
            World = null;
            Driver = null;
            LotControl = null;
        }

        private VMMarshal SavedLot;

        public void InitializeLot()
        {
            CleanupLastWorld();
            World = new FSO.LotView.World(GameFacade.GraphicsDevice);

            World.Opacity = 1;
            GameFacade.Scenes.Add(World);

            var globalLink = new VMTS1GlobalLinkStub();
            Driver = new VMServerDriver(globalLink);

            vm = new VM(new VMContext(World), Driver, new UIHeadlineRendererProvider());
            vm.AllowDirectControl = true; //walking Sims with the gamepad (UILotControlPadHelper)
            vm.ListenBHAVChanges();
            vm.Init();

            LastSoundIntensity = -1;
            LastWeatherType = WeatherType.Rain;
            LastThunder = false;
            TerrainSnowApplied = false;
            ThunderTimer = 0f;
            PendingWeatherData = null;

            LotControl = new UILotControl(vm, World);
            this.AddAt(0, LotControl);

            if (m_ZoomLevel > 3)
            {
                World.Visible = false;
                LotControl.Visible = false;
            }

            ZoomLevel = Math.Max(ZoomLevel, 4);

            if (IDEHook.IDE != null) IDEHook.IDE.StartIDE(vm);

            vm.OnFullRefresh += VMRefreshed;
            //vm.OnEODMessage += LotControl.EODs.OnEODMessage;
            vm.OnRequestLotSwitch += VMLotSwitch;
            vm.OnGenericVMEvent += Vm_OnGenericVMEvent;
        }

        public void InitializeLot(VMMarshal marshal)
        {
            InitializeLot();
            vm.MyUID = 65537;
            vm.Load(marshal);

            vm.TS1State.ActivateFamily(vm, ActiveFamily);

            var settings = GlobalSettings.Default;
            var myClient = new VMNetClient
            {
                PersistID = 1,
                RemoteIP = "local",
                AvatarState = new VMNetAvatarPersistState()
                {
                    Name = settings.LastUser ?? "",
                    DefaultSuits = new VMAvatarDefaultSuits(settings.DebugGender),
                    BodyOutfit = settings.DebugBody,
                    HeadOutfit = settings.DebugHead,
                    PersistID = 1,
                    SkinTone = (byte)settings.DebugSkin,
                    Gender = (short)(settings.DebugGender ? 1 : 0),
                    Permissions = FSO.SimAntics.Model.TSOPlatform.VMTSOAvatarPermissions.Admin,
                    Budget = 1000000
                }

            };

            var server = (VMServerDriver)Driver;
            server.ConnectClient(myClient);

            GameFacade.Cursor.SetCursor(CursorType.Normal);
            ZoomLevel = 1;

            CreateFrontend();
        }

        private void CreateFrontend()
        {
            Desktop = UseDesktopLayout(GlobalSettings.Default.UILayout, FSOEnvironment.SoftwareKeyboard, LastInputDevice);
            LayoutDevice = LastInputDevice;
            LayoutPendingFor = 0;
            Frontend = new UISimitoneFrontend(this);
            this.Add(Frontend);
        }

        /// <summary>
        /// Resolves the UILayout setting. "desktop" and "mobile" force a layout; anything else is "auto", which picks
        /// the mobile layout for touch screens, or when the gamepad is the device in use (see <see cref="UpdateAutoLayout"/>).
        /// </summary>
        public static bool UseDesktopLayout(string setting, bool touch, InputDevice lastDevice)
        {
            switch ((setting ?? "").Trim().ToLowerInvariant())
            {
                case "desktop": return true;
                case "mobile": return false;
                default: return !touch && lastDevice != InputDevice.GamePad;
            }
        }

        /// <summary>Whether the layout follows the device in use: UILayout "auto", off touch screens.</summary>
        public static bool AutoLayout(string setting, bool touch)
        {
            var value = (setting ?? "").Trim().ToLowerInvariant();
            return !touch && value != "desktop" && value != "mobile";
        }

        /// <summary>Seconds the other device has to be the one in use before the lot's layout switches to it.</summary>
        public const float LayoutSwitchDelay = 0.75f;
        /// <summary>The device the layout follows: the pad, or the mouse (the keyboard counts as the mouse here).</summary>
        private InputDevice LayoutDevice = InputDevice.Mouse;
        private InputDevice LastPointerDevice = InputDevice.Mouse;
        private float LayoutPendingFor;

        /// <summary>
        /// The device the layout should follow after this frame's input. A pad press means the pad; a key press, or
        /// the pointer passing to the mouse (it moved, clicked or scrolled), means the mouse. Keys the pad itself sends
        /// (Escape for B under the virtual cursor) arrive on frames the pad is active, and keyboard and mouse echoes
        /// from Steam's desktop layout are already dropped while the pad is held (PadEchoFilter).
        /// </summary>
        public static InputDevice LayoutDeviceAfter(InputDevice current, InputDevice lastPointer, InputDevice pointer,
            bool padActive, bool keyPressed)
        {
            if (padActive) return InputDevice.GamePad;
            if (keyPressed) return InputDevice.Mouse;
            if (pointer != lastPointer) return pointer;
            return current;
        }

        /// <summary>
        /// With UILayout "auto", switches the lot between the desktop and mobile layouts as the player changes device:
        /// the pad gets the mobile layout (big buttons, the mode wheel), the mouse or keyboard the desktop one. The other
        /// device has to stay the one in use for <see cref="LayoutSwitchDelay"/> seconds, and the lot's UI has to be at
        /// rest (<see cref="LayoutAtRest"/>); until then the switch waits. The frontend is rebuilt in the new layout in
        /// the same mode (live, buy, build or options).
        /// </summary>
        private void UpdateAutoLayout(FSO.Common.Rendering.Framework.Model.UpdateState state)
        {
            var pointer = state.LastInputDevice;
            LayoutDevice = LayoutDeviceAfter(LayoutDevice, LastPointerDevice, pointer, state.Pad.Active, state.NewKeys.Count > 0);
            LastPointerDevice = pointer;
            if (Frontend == null || vm == null || LotControl == null) return;
            if (!AutoLayout(GlobalSettings.Default.UILayout, FSOEnvironment.SoftwareKeyboard)) return;

            var desktop = UseDesktopLayout(GlobalSettings.Default.UILayout, FSOEnvironment.SoftwareKeyboard, LayoutDevice);
            if (desktop == Desktop)
            {
                LayoutPendingFor = 0;
                return;
            }
            LayoutPendingFor += (float)(state.Time?.ElapsedGameTime.TotalSeconds ?? 1 / 60.0);
            if (LayoutPendingFor < LayoutSwitchDelay || !LayoutAtRest(state)) return;
            LayoutPendingFor = 0;
            SwitchLayout(desktop);
        }

        /// <summary>
        /// Nothing in progress that rebuilding the frontend would lose: no pie menu, no object in hand, no build tool,
        /// no text box being typed in, no dialog, no mode selection, and the pad isn't in one of the lot's menus. (The
        /// pad helper captures the pad all through live mode, so being captured on its own doesn't count.)
        /// </summary>
        private bool LayoutAtRest(FSO.Common.Rendering.Framework.Model.UpdateState state)
        {
            return Frontend.Visible && !LotControl.PieMenuOpen && LotControl.ObjectHolder?.Holding == null
                && LotControl.CustomControl == null && state.InputManager?.GetFocus() == null
                && !GameFacade.Screens.HasModalDialog && !Frontend.MainPanel.ShowingSelect
                && !(LotControl.PadHelper?.InMenu ?? false) && !(Frontend.CheatTextbox?.Visible ?? false);
        }

        /// <summary>Rebuilds the lot's frontend in the other layout, where the old one was, in the same mode.</summary>
        private void SwitchLayout(bool desktop)
        {
            var old = Frontend;
            var mode = old.MainPanel.Mode;
            var wasOpen = old.MainPanel.PanelActive;
            var category = old.MainPanel.Switcher.ActiveCategory;
            var children = GetChildren();
            var index = children.IndexOf(old);
            var next = (index >= 0 && index + 1 < children.Count) ? children[index + 1] : null;

            Remove(old);
            Desktop = desktop;
            Frontend = new UISimitoneFrontend(this);
            if (next != null) AddBefore(Frontend, next);
            else Add(Frontend);

            //the new frontend starts in live mode (or buy, for a lot that asks for it): put it back as it was
            if (Frontend.MainPanel.Mode != mode) Frontend.SetModeDirect(mode);
            if (wasOpen)
            {
                //opening the panel doesn't fill it (the expand button selects the tab too): without this the needs
                //tab came back empty, its bars gone until the panel was closed and opened again
                if (!Frontend.MainPanel.PanelActive) Frontend.MainPanel.Open();
                Frontend.MainPanel.Switcher.Select(category);
            }
        }

        public void ShowLoadErrors(List<VMLoadError> errors, bool verbose)
        {
            var errorMsg = GameFacade.Strings.GetString("153", "16");

            if (verbose)
            {
                errorMsg += "\n";
                foreach (var error in errors)
                {
                    errorMsg += "\n" + error.ToString();
                }
            }

            //signal thru the VM so we can stop time appropriately
            vm.LastSpeedMultiplier = vm.SpeedMultiplier;
            vm.SpeedMultiplier = 0;
            vm.SignalDialog(new VMDialogInfo
            {
                Block = true,
                Caller = null,
                Yes = "OK",
                DialogID = 0,
                Title = GameFacade.Strings.GetString("153", "17"),
                Message = errorMsg,
            });

            /*
            CloseAlert = new UIMobileAlert(new FSO.Client.UI.Controls.UIAlertOptions
            {
                Title = GameFacade.Strings.GetString("153", "17"), //missing objects!
                Message = errorMsg,
                Buttons = UIAlertButton.Ok(
                        (b) => { CloseAlert.Close(); CloseAlert = null; }
                        )
            });
            */
        }

        public void InitializeLot(string lotName, bool external)
        {
            if (lotName == "" || lotName[0] == '!') return;
            InitializeLot();
            
            if (!external)
            {
                if (!Downtown && ActiveFamily != null)
                {
                    ActiveFamily.SelectWholeFamily();
                    vm.TS1State.ActivateFamily(vm, ActiveFamily);
                }
                BlueprintReset(lotName, null);
                // Read saved weather state now; apply it deferred in UpdateWeatherEffects
                // once Blueprint is available (BlueprintReset queues a command, so Blueprint
                // may still be null when this line runs).
                short pendingWeather = (short)(1 << 8);
                var weatherPath = Path.Combine(FSOEnvironment.UserDir, "LocalHouse/cas.weather");
                if (File.Exists(weatherPath))
                {
                    var bytes = File.ReadAllBytes(weatherPath);
                    if (bytes.Length >= 2) pendingWeather = BitConverter.ToInt16(bytes, 0);
                }
                PendingWeatherData = pendingWeather;

                if (vm.LoadErrors.Count > 0) GameThread.NextUpdate((state) => ShowLoadErrors(vm.LoadErrors, true));

                vm.MyUID = 65537;
                var settings = GlobalSettings.Default;
                var myClient = new VMNetClient
                {
                    PersistID = 1,
                    RemoteIP = "local",
                    AvatarState = new VMNetAvatarPersistState()
                    {
                        Name = settings.LastUser ?? "",
                        DefaultSuits = new VMAvatarDefaultSuits(settings.DebugGender),
                        BodyOutfit = settings.DebugBody,
                        HeadOutfit = settings.DebugHead,
                        PersistID = 1,
                        SkinTone = (byte)settings.DebugSkin,
                        Gender = (short)(settings.DebugGender ? 1 : 0),
                        Permissions = FSO.SimAntics.Model.TSOPlatform.VMTSOAvatarPermissions.Admin,
                        Budget = 1000000
                    }
                };

                if (Downtown)
                {
                    var ngbh = Content.Get().Neighborhood;
                    var crossData = ngbh.GameState;
                    var neigh = ngbh.GetNeighborIDForGUID(crossData.DowntownSimGUID);
                    if (neigh != null) {
                        var inv = ngbh.GetInventoryByNID(neigh.Value);
                        if (inv != null) {
                            var hr = inv.FirstOrDefault(x => x.Type == 2 && x.GUID == 7)?.Count ?? 0;
                            var min = inv.FirstOrDefault(x => x.Type == 2 && x.GUID == 8)?.Count ?? 0;
                            Driver.SendCommand(new VMNetSetTimeCmd()
                            {
                                Hours = hr,
                                Minutes = min,
                            });
                        }
                    }
                }

                var server = (VMServerDriver)Driver;
                server.ConnectClient(myClient);
                LoadSurrounding(short.Parse(lotName.Substring(lotName.Length - 6, 2)));

                GameFacade.Cursor.SetCursor(CursorType.Normal);
                ZoomLevel = 1;
            }

            CreateFrontend();
        }

        public void LoadSurrounding(short houseID)
        {
            return;
            var surrounding = new NBHm(new OBJ(File.OpenRead(@"C:\Users\Rhys\Desktop\fso 2018\nb4.obj")));
            NBHmHouse myH = null;
            var myHeight = vm.Context.Blueprint.InterpAltitude(new Vector3(0, 0, 0));
            if (!surrounding.Houses.TryGetValue(houseID, out myH)) return;
            foreach (var house in surrounding.Houses)
            {
                if (house.Key == houseID) continue;
                var h = house.Value;
                //let's make their lot as a surrounding lot
                var gd = World.State.Device;
                var subworld = World.MakeSubWorld(gd);
                subworld.Initialize(gd);
                var tempVM = new VM(new VMContext(subworld), new VMServerDriver(new VMTSOGlobalLinkStub()), new VMNullHeadlineProvider());
                tempVM.Init();
                BlueprintReset(Content.Get().Neighborhood.GetHousePath(house.Key), tempVM);
                subworld.State.Level = 5;
                var subHeight = tempVM.Context.Blueprint.InterpAltitude(new Vector3(0, 0, 0));
                tempVM.Context.Blueprint.BaseAlt = (int)Math.Round(((subHeight - myHeight) + myH.Position.Y - h.Position.Y) / tempVM.Context.Blueprint.TerrainFactor);
                subworld.UseFade = false;
                subworld.GlobalPosition = new Vector2((myH.Position.X - h.Position.X), (myH.Position.Z - h.Position.Z));

                foreach (var obj in tempVM.Entities)
                {
                    obj.Position = obj.Position;
                }

                vm.Context.Blueprint.SubWorlds.Add(subworld);
            }
            vm.Context.World.InitSubWorlds();
        }

        public void BlueprintReset(string path, VM vm)
        {
            string filename = Path.GetFileName(path);
            bool isSurrounding = true;
            if (vm == null)
            {
                isSurrounding = false;
                vm = this.vm;
            }
            try
            {
                using (var file = new BinaryReader(File.OpenRead(Path.Combine(FSOEnvironment.UserDir, "LocalHouse/") + filename.Substring(0, filename.Length - 4) + ".fsov")))
                {
                    var marshal = new FSO.SimAntics.Marshals.VMMarshal();
                    marshal.Deserialize(file);
                    //vm.SendCommand(new VMStateSyncCmd()
                    //{
                    //    State = marshal
                    //});

                    vm.Load(marshal);
                    vm.Reset();
                }
            }
            catch (Exception)
            {
                var floorClip = Rectangle.Empty;
                var offset = new Point();
                var targetSize = 0;

                var isIff = path.EndsWith(".iff");
                short jobLevel = -1;
                if (isIff) jobLevel = short.Parse(path.Substring(path.Length - 6, 2));
                vm.SendCommand(new VMBlueprintRestoreCmd
                {
                    JobLevel = jobLevel,
                    XMLData = File.ReadAllBytes(path),
                    IffData = isIff,

                    FloorClipX = floorClip.X,
                    FloorClipY = floorClip.Y,
                    FloorClipWidth = floorClip.Width,
                    FloorClipHeight = floorClip.Height,
                    OffsetX = offset.X,
                    OffsetY = offset.Y,
                    TargetSize = targetSize
                });
            }

            var isSimless = (ActiveFamily == null && !isSurrounding);
            vm.SpeedMultiplier = -1;
            vm.Tick();
            vm.SpeedMultiplier = 1;

            if (isSimless)
            {
                vm.SpeedMultiplier = -1;
            }
            vm.SetGlobalValue(32, (short)(isSimless ? 1 : 0));
        }


        private void Vm_OnGenericVMEvent(VMEventType type, object data)
        {
            switch (type)
            {
                case VMEventType.TS1BuildBuyChange:
                    Frontend?.ModeSwitcher?.UpdateBuildBuy();
                    Frontend?.DesktopUCP?.UpdateBuildBuy();
                    break;
            }
        }

        private void VMLotSwitch(uint lotId)
        {
            vm.SpeedMultiplier = 0;
            if ((short)lotId == -1)
            {
                lotId = (uint)ActiveFamily.HouseNumber;
            }
            SwitchLot = (int)lotId;
        }

        private void VMRefreshed()
        {
            if (vm == null) return;
            LotControl.ActiveEntity = null;
            LotControl.RefreshCut();
        }

        private void SaveHouseButton_OnButtonClick(UIElement button)
        {
            if (vm == null) return;

            var exporter = new VMWorldExporter();
            Directory.CreateDirectory(Path.Combine(FSOEnvironment.UserDir, "Blueprints/cas.xml"));
            exporter.SaveHouse(vm, Path.Combine(FSOEnvironment.UserDir, "Blueprints/cas.xml"));
            var marshal = vm.Save();
            Directory.CreateDirectory(Path.Combine(FSOEnvironment.UserDir, "LocalHouse/"));
            using (var output = new FileStream(Path.Combine(FSOEnvironment.UserDir, "LocalHouse/cas.fsov"), FileMode.Create))
            {
                marshal.SerializeInto(new BinaryWriter(output));
            }
            var weatherData = vm.Context.Blueprint?.Weather?.WeatherData ?? (short)(1 << 8);
            File.WriteAllBytes(Path.Combine(FSOEnvironment.UserDir, "LocalHouse/cas.weather"), BitConverter.GetBytes(weatherData));
        }

        private UIMobileAlert CloseAlert;
        public override bool CloseAttempt()
        {
            if (CloseAlert != null) return true;

            GameThread.NextUpdate(x =>
            {
                if (CloseAlert == null)
                {
                    var canSave = vm != null;
                    CloseAlert = new UIMobileAlert(new FSO.Client.UI.Controls.UIAlertOptions
                    {
                        Title = GameFacade.Strings.GetString("153", "1"), //quit?
                        Message = GameFacade.Strings.GetString("153", canSave?"6":"2"), //are you sure (2), save before quitting (3)
                        Buttons = 
                        canSave?
                        UIAlertButton.YesNoCancel(
                            (b) => { Save(); GameFacade.Game.Exit(); },
                            (b) => { GameFacade.Game.Exit(); },
                            (b) => { CloseAlert.Close(); CloseAlert = null; }
                            )
                        :
                        UIAlertButton.YesNo(
                            (b) => { GameFacade.Game.Exit(); },
                            (b) => { CloseAlert.Close(); CloseAlert = null; }
                            )
                    });
                    GlobalShowDialog(CloseAlert, true);
                }
            });
            return false;
        }

        public void ReturnToNeighbourhood()
        {
            if (CloseAlert == null)
            {
                CloseAlert = new UIMobileAlert(new FSO.Client.UI.Controls.UIAlertOptions
                {
                    Title = GameFacade.Strings.GetString("153", "3"), //save
                    Message = GameFacade.Strings.GetString("153", "4"), //Do you want to save the game?
                    Buttons =
                    UIAlertButton.YesNoCancel(
                        (b) => { Save(); ExitLot(); CloseAlert.Close(); CloseAlert = null; },
                        (b) => { ExitLot(); CloseAlert.Close(); CloseAlert = null; },
                        (b) => { CloseAlert.Close(); CloseAlert = null; }
                        )
                });
                GlobalShowDialog(CloseAlert, true);
            }
        }

        public void Save()
        {
            //save the house first
            var iff = new IffFile();
            vm.TS1State.UpdateSIMI(vm);
            var marshal = vm.Save();
            var fsov = new FSOV();
            fsov.ChunkLabel = "Simitone Lot Data";
            fsov.ChunkID = 1;
            fsov.ChunkProcessed = true;
            fsov.ChunkType = "FSOV";
            fsov.AddedByPatch = true;

            using (var stream = new MemoryStream())
            {
                marshal.SerializeInto(new BinaryWriter(stream));
                fsov.Data = stream.ToArray();
            }

            iff.AddChunk(fsov);

            var simi = vm.TS1State.SimulationInfo;
            simi.ChunkProcessed = true;
            simi.AddedByPatch = true;
            iff.AddChunk(simi);

            Texture2D roofless = null;
            var thumb = World.GetLotThumb(GameFacade.GraphicsDevice, (tex) => roofless = FSO.Common.Utils.TextureUtils.Decimate(tex, GameFacade.GraphicsDevice, 2, false));
            thumb = FSO.Common.Utils.TextureUtils.Decimate(thumb, GameFacade.GraphicsDevice, 2, false);

            var tPNG = GeneratePNG(thumb);
            tPNG.ChunkID = 513;
            iff.AddChunk(tPNG);

            var rPNG = GeneratePNG(roofless);
            rPNG.ChunkID = 512;
            iff.AddChunk(rPNG);

            Content.Get().Neighborhood.SaveHouse(vm.GetGlobalValue(10), iff);
            Content.Get().Neighborhood.SaveNeighbourhood(true);

            // Write weather sidecar alongside the save
            var weatherData = vm.Context.Blueprint?.Weather?.WeatherData ?? (short)(1 << 8);
            Directory.CreateDirectory(Path.Combine(FSOEnvironment.UserDir, "LocalHouse/"));
            File.WriteAllBytes(Path.Combine(FSOEnvironment.UserDir, "LocalHouse/cas.weather"), BitConverter.GetBytes(weatherData));
        }

        public PNG GeneratePNG(Texture2D data)
        {
            var png = new PNG();
            using (var stream = new MemoryStream())
            {
                data.SaveAsPng(stream, data.Width, data.Height);
                png.data = stream.ToArray();
            }

            png.ChunkLabel = "Lot Thumbnail";
            png.ChunkProcessed = true;
            png.ChunkType = "PNG_";
            png.AddedByPatch = true;

            return png;
        }

        public void ExitLot()
        {
            CleanupLastWorld();
            Bg.Visible = true; //hidden while on the lot: show the patterned background around the neighbourhood again
            NeighSelection(CurrentNeighborhoodMode);
            Downtown = false;
            SavedLot = null;
        }
    }

    public enum NeighSelectionMode
    {
        Normal,
        MoveIn,
        MoveInMagic
    }
}
