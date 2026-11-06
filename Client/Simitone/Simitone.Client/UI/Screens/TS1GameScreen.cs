
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
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Panels.WorldUI;
using Simitone.Client.Utils;
using Simitone.Client.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Simitone.Client.UI.Screens
{
    public class TS1GameScreen : FSO.Client.UI.Framework.GameScreen
    {
        public UIContainer WindowContainer;
        public bool Downtown;
        public bool Desktop = !FSOEnvironment.SoftwareKeyboard;

        public UILotControl LotControl { get; set; }
        public UISimitoneFrontend Frontend { get; set; }
        private FSO.LotView.World World;
        public FSO.SimAntics.VM vm { get; set; }
        public VMNetDriver Driver;
        public UISimitoneBg Bg;
        public uint VisualBudget { get; set; }

        //for TS1 hybrid mode
        public UINeighborhoodSelectionPanel TS1NeighPanel;
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
            if (TestMode.TakeLot(out var testLot)) PlayHouse(testLot, switcher);
        }

        public void PlayHouse(short house, UIElement switcher)
        {
            ActiveFamily = Content.Get().Neighborhood.GetFamilyForHouse((short)house);
            CurrentNeighborhoodMode = GetNeighborhoodModeFromHouse(house);
            InitializeLot(Content.Get().Neighborhood.GetHousePath(house), false);// "UserData/Houses/House21.iff"
            Remove(TS1NeighPanel);
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

        // As in TS1: while none of the household's Sims are on the lot (all at work, school...),
        // run at ultra speed, then go back to the previous speed when one of them returns.
        private bool AutoFastForward;
        private int SpeedBeforeAutoFastForward;
        private bool AutoFastForwardDeclined; // the user changed speed while everyone was away

        private void UpdateAutoFastForward()
        {
            var family = vm?.TS1State?.CurrentFamily;
            if (vm == null || !vm.Ready || family == null) return;
            var present = vm.Context.ObjectQueries.Avatars.Any(x => !x.Dead && family.RuntimeSubset.Contains(x.Object.OBJ.GUID));

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
                AutoFastForward = false; // the user picked a speed themselves; leave it until someone is home
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
            GameFacade.Game.IsMouseVisible = Visible;
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

            Frontend = new UISimitoneFrontend(this);
            this.Add(Frontend);
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

            Frontend = new UISimitoneFrontend(this);
            this.Add(Frontend);
        }

        /// <summary>
        /// The lots around yours, instead of grey void: as FreeSO puts the lots around one in The Sims Online, a ring of
        /// eight, drawn with it but not simulated (<see cref="FSO.LotView.World"/>'s sub-worlds). Which depends on
        /// SurroundingLotMode (config.ini): 0 none, 1 open ground (your lot's ground and grass, at its height), 2 your
        /// neighbours' houses where the neighbourhood has them, as it has them on its map (<see cref="SurroundingLayout"/>),
        /// furnished but without their Sims, and open ground between. They're loaded one per update after you arrive,
        /// so the lot comes up as quickly as before.
        /// </summary>
        public void LoadSurrounding(short houseID)
        {
            var mode = FSO.LotView.WorldConfig.Current?.SurroundingLots ?? 0;
            if (mode <= 0 || vm == null || World == null) return;
            var myVM = vm;
            var queue = new Queue<(Point cell, short house)>();
            Dictionary<Point, short> neighbours = null;
            var tries = 0;
            void Next(FSO.Common.Rendering.Framework.Model.UpdateState state)
            {
                if (vm != myVM || World == null) return; // (left the lot)
                var arch = vm.Context?.Architecture;
                if (arch == null)
                {
                    if (++tries < 300) GameThread.NextUpdate(Next); // (the lot's still loading)
                    return;
                }
                if (neighbours == null)
                {
                    neighbours = mode >= 2 ? NeighbourCells(houseID, arch.Width - 2) : new Dictionary<Point, short>();
                    for (int y = -1; y <= 1; y++)
                        for (int x = -1; x <= 1; x++)
                            if (x != 0 || y != 0)
                                queue.Enqueue((new Point(x, y), neighbours.TryGetValue(new Point(x, y), out var h) ? h : (short)-1));
                    System.Diagnostics.Debug.WriteLine($"surroundings: {neighbours.Count} neighbour(s) around house {houseID}");
                }
                if (queue.Count == 0) return;
                var (cell, house) = queue.Dequeue();
                try { AddSurrounding(cell, house); }
                catch (Exception e) { System.Diagnostics.Debug.WriteLine($"surroundings: lot at {cell} failed: {e.Message}"); }
                if (queue.Count > 0) GameThread.NextUpdate(Next);
            }
            GameThread.NextUpdate(Next);
        }

        /// <summary>Your neighbours' houses around <paramref name="houseID"/>, by cell, from the neighbourhood map's spots.</summary>
        private static Dictionary<Point, short> NeighbourCells(short houseID, float spacing)
        {
            var locations = Content.Get().Neighborhood?.LotLocations;
            if (locations == null) return new Dictionary<Point, short>();
            foreach (var str in locations.List<STR>() ?? new List<STR>())
            {
                var lines = new List<string>();
                for (int i = 0; i < str.Length; i++) lines.Add(str.GetString(i));
                var houses = SurroundingLayout.Parse(lines);
                if (!houses.Any(h => h.house == houseID)) continue;
                var onGround = houses.Select(h => (h.house, SurroundingLayout.MapToTiles(h.screen, houses.Count))).ToList();
                return SurroundingLayout.Arrange(onGround, houseID, spacing);
            }
            return new Dictionary<Point, short>();
        }

        /// <summary>
        /// One lot of the ring: a neighbour's house (furnished, their Sims left out) or, for -1 or if it won't load, open
        /// ground; placed in its cell, its edges at your lot's height.
        /// </summary>
        private void AddSurrounding(Point cell, short house)
        {
            var gd = World.State.Device;
            var mine = vm.Context.Architecture;
            var myHeight = BorderHeight(mine.Terrain);
            var subworld = World.MakeSubWorld(gd);
            subworld.Initialize(gd);
            var tempVM = new VM(new VMContext(subworld), new VMServerDriver(new VMTS1GlobalLinkStub()), new VMNullHeadlineProvider());
            tempVM.Init();
            var path = house >= 0 ? Content.Get().Neighborhood.GetHousePath(house) : null;
            var loaded = false;
            if (path != null && File.Exists(path))
            {
                try
                {
                    BlueprintReset(path, tempVM);
                    // scenery: their Sims stay at home, out of sight
                    foreach (var avatar in tempVM.Entities.OfType<VMAvatar>().ToList()) avatar.Delete(true, tempVM.Context);
                    loaded = tempVM.Context.Blueprint != null && tempVM.Context.Architecture != null;
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine($"surroundings: house {house} won't load ({e.Message}); open ground instead");
                }
            }
            if (!loaded)
            {
                subworld.Dispose();
                subworld = World.MakeSubWorld(gd);
                subworld.Initialize(gd);
                tempVM = new VM(new VMContext(subworld), new VMServerDriver(new VMTS1GlobalLinkStub()), new VMNullHeadlineProvider());
                tempVM.Init();
                OpenGround(tempVM, subworld, mine.Width, mine.Terrain, (short)Math.Round(myHeight));
            }

            var blueprint = tempVM.Context.Blueprint;
            var size = tempVM.Context.Architecture.Width;
            // edges at your lot's edge height, and centred in its cell
            blueprint.BaseAlt = (int)Math.Round(BorderHeight(tempVM.Context.Architecture.Terrain) - myHeight) + vm.Context.Blueprint.BaseAlt;
            var spacing = mine.Width - 2;
            var origin = new Vector2(cell.X * spacing + (mine.Width - size) / 2, cell.Y * spacing + (mine.Width - size) / 2);
            subworld.GlobalPosition = -origin;
            subworld.State.Level = 5;
            subworld.UseFade = false;
            foreach (var obj in tempVM.Entities) obj.Position = obj.Position; // (draw them where they are)
            vm.Context.Blueprint.SubWorlds.Add(subworld);
            vm.Context.World.InitSubWorlds();
        }

        /// <summary>The average height of a lot's ground a tile in from its edge (the lots' heights are matched there).</summary>
        private static float BorderHeight(VMArchitectureTerrain terrain)
        {
            if (terrain?.Heights == null) return 0;
            int w = terrain.Width, h = terrain.Height;
            double sum = 0;
            int count = 0;
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    if (x != 1 && y != 1 && x != w - 2 && y != h - 2) continue;
                    sum += terrain.Heights[y * w + x];
                    count++;
                }
            return count == 0 ? 0 : (float)(sum / count);
        }

        /// <summary>An empty lot: your lot's kind of ground and grass, flat at its edge height.</summary>
        private static void OpenGround(VM tempVM, FSO.LotView.Components.SubWorldComponent subworld, int size, VMArchitectureTerrain like, short height)
        {
            var blueprint = new Blueprint(size, size);
            tempVM.Context.Blueprint = blueprint;
            subworld.InitBlueprint(blueprint);
            var arch = new VMArchitecture(size, size, blueprint, tempVM.Context);
            tempVM.Context.Architecture = arch;
            arch.EmptyRoomMap();
            tempVM.Context.RegeneratePortalInfo();
            var terrain = new FSO.LotView.Components.TerrainComponent(new Rectangle(0, 0, size, size), blueprint);
            terrain.Initialize(subworld.State.Device, subworld.State);
            blueprint.Terrain = terrain;
            arch.Terrain.LightType = like?.LightType ?? FSO.Content.Model.TerrainType.GRASS;
            arch.Terrain.DarkType = like?.DarkType ?? FSO.Content.Model.TerrainType.GRASS;
            arch.Terrain.GenerateGrassStates(RestoreLotType.Blank);
            for (int i = 0; i < arch.Terrain.Heights.Length; i++) arch.Terrain.Heights[i] = height;
            arch.Terrain.RegenerateCenters();
            arch.SignalTerrainRedraw();
            arch.ClearDirty();
            arch.RegenRoomMap();
            tempVM.Context.RegeneratePortalInfo();
            tempVM.Context.RefreshAllLighting();
            tempVM.Tick();
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
