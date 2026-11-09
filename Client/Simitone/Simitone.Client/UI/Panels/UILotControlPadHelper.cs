using System;
using System.Collections.Generic;
using System.Linq;
using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Client.UI.Model;
using FSO.Client.UI.Panels.LotControls;
using FSO.Common;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.HIT;
using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.NetPlay.Model.Commands;
using FSO.LotView.Utils.Camera;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Controls;
using Simitone.Client.UI.DualScreen;
using Simitone.Client.UI.Panels.LiveSubpanels;
using Simitone.Client.UI.Screens;

namespace Simitone.Client.UI.Panels
{
    /// <summary>
    /// Native gamepad control of the lot (controller support, "cursor mode").
    ///
    /// While the lot owns the pad (no modal dialog or text field has focus):
    /// <list type="bullet">
    /// <item>right stick: up/down zooms, left/right rotates (continuous in 3D, a flick per step in 2D)</item>
    /// <item>Select: next mode (Live, Buy, Build, Options); Start: pause/resume</item>
    /// <item>D-pad up/down: floor; D-pad left/right: game speed</item>
    /// <item>L3: cycle walls (down, cutaway, up, roofs); R3: re-centre on and follow the active Sim</item>
    /// </list>
    /// In live mode it also captures the pad, which makes the virtual cursor stand down:
    /// <list type="bullet">
    /// <item>left stick: moves a selector over the lot that snaps to nearby objects and people; pushing it to the
    /// edge of the screen scrolls the camera</item>
    /// <item>A: interaction menu for the selected object, or "go here" on empty floor</item>
    /// <item>LB/RB: previous/next household Sim</item>
    /// <item>LT: needs panel closed, then open, then focused (D-pad/stick move between its buttons and lists,
    /// A activates, B leaves the panel), then closed; RT: next panel tab</item>
    /// <item>Y: focus the action queue (left/right to choose, A cancels the action, B or Y to leave)</item>
    /// <item>X: switch between cursor mode and direct control (3D camera only). In direct control the left stick walks
    /// the Sim relative to the camera (running at full tilt), the camera follows them, and A opens the menu of the
    /// object or person just in front of them.</item>
    /// </list>
    /// In Options mode the panel's buttons are navigated directly (B returns to live mode).
    ///
    /// In buy and build mode the pad works on the lot or on the catalog:
    /// <list type="bullet">
    /// <item>lot: a cursor fixed at the screen centre, with the left stick scrolling the lot under it. A is the mouse
    /// button: pick up or place, or hold to drag a wall or floor tool. LB/RB rotate what you hold, Y sells it, B puts it
    /// back (or puts the tool away) and returns to the catalog. With nothing held, X is the eyedropper. While a build
    /// tool is out, hold X for its Shift form (whole room / fill) and Y for its Ctrl form (erase / delete).</item>
    /// <item>catalog: LT moves the pad into and out of it, RT changes category. The D-pad or stick move between
    /// subcategories and along the items without buying anything; A picks one (the pad goes to the lot to place it);
    /// B goes back from items to subcategories, then to the lot. After a new item is placed, the pad returns to the
    /// catalog on the same item.</item>
    /// </list>
    /// </summary>
    public class UILotControlPadHelper : UIElement
    {
        //(speeds and distances on screen are in screen pixels, as tuned on desktops: see PxToUI)
        private const float SelectorSpeed = 520f; //px per second at full deflection (quadratic response)
        private const float SnapPixels = 36f; //on-screen distance from a footprint at which the selector snaps to it
        private const float MinSnapTiles = 0.2f;
        private const float MaxSnapTiles = 0.6f;
        private const double CutawayFocusSeconds = 3; //after the selector stops, the cutaway goes back to the active sim
        private const float ZoomRate = 1600f; //wheel units per second at full deflection
        private const float RotateRate3D = 2.2f; //radians per second at full deflection
        private const int MaxFloor = 5; //matches the mobile layout's floor buttons
        //direct control target: within DirectReach tiles of the Sim and DirectMaxAngle of its facing, in its room
        private const float DirectReach = 1.25f;
        private const float DirectMaxAngle = 0.61f; //35 degrees
        private const float DirectAnglePenalty = 1f; //score per radian off centre (score is in tiles)
        private const float RunOn = 0.9f; //stick deflection at which a directly controlled Sim starts running...
        private const float RunOff = 0.75f; //...and below which it walks again
        private const float DirectCameraRate = 15f; //camera follow, fraction of the gap closed per second (exponential)
        private const float DirectCameraSnapTiles = 3f; //further than this (stairs, a new Sim) the camera jumps
        private const float BuildScrollSpeed = 640f; //buy/build: px per second the lot scrolls at full deflection
        private const float BuildCursorSize = 22f;

        private readonly UILotControl Master;

        /// <summary>The lot owns the pad this frame: live, buy or build mode, and no modal dialog or text field has focus.</summary>
        public bool Capturing { get; private set; }

        /// <summary>Capturing, and the pad is the device in use: show the selector and hide mouse affordances.</summary>
        public bool PadActive { get; private set; }

        /// <summary>Tile the cutaway should centre on while the pad is in use, or null to follow the mouse.</summary>
        public Vector2? CutawayFocus { get; private set; }

        private Vector2 Selector; //tile coordinates on the current level
        private bool SelectorPlaced;
        private double LastSelectorMove = double.NegativeInfinity;

        private VMEntity TargetPart; //entity the selector snapped to; may be one tile of a multitile object
        private VMEntity Target; //its interaction group leader, which owns the interaction menu
        private bool TargetHasInteractions;
        private VMEntity TargetCheckedFor; //the active sim TargetHasInteractions was computed for

        private bool WasPadActive;
        private bool FollowSim;
        private VMEntity LastActive;
        private bool RotateArmed = true;

        private const double ModeMenuHold = 0.6; //seconds Select is held to open the mode menu; a tap cycles modes
        private double SelectDownAt = -1; //when Select went down, while it may still be a tap or a hold; else -1
        private double Now;
        private readonly UIFocusNavigator ModeMenuNav = new UIFocusNavigator();
        private readonly ModeMenuScope MenuScope = new ModeMenuScope();
        private bool ModeMenuActive;

        //LB, RB, LT, RT: alone they act when let go; RT held opens the panel's tab fan; all four held open the cheat box
        private readonly PadChords Chords = new PadChords();
        private readonly UIFocusNavigator CategoryMenuNav = new UIFocusNavigator();
        private readonly CategoryMenuScope CategoryScope = new CategoryMenuScope();
        private bool CategoryMenuActive;

        /// <summary>The FISH EYE cheat (typed in the cheat box): in live mode, B looks through the active Sim's eyes. For the session.</summary>
        public static bool FishEyeCheat;
        private bool FishEye; //looking through the Sim's eyes now (FreeSO's Direct camera, at the Sim's head)
        private AvatarComponent FishEyeHead; //whose head is hidden
        private int FishEyeWallsBefore;
        private const float LookRate = 2.2f; //radians per second at full deflection
        private const float PitchLimit = 1.2f; //radians up or down from level
        private bool QueueFocus;
        private bool PanelFocus; //live mode: the needs panel has the pad; buy/build: the catalog has it

        /// <summary>The pad is in one of the lot's menus: the mode menu, the tab fan, the action queue, or a panel.</summary>
        public bool InMenu => ModeMenuActive || CategoryMenuActive || QueueFocus || PanelFocus;

        //buy/build
        private bool BuildConfirmDown; //A went down on the lot (the holder or tool got a mouse down) and hasn't come up
        private bool ConfirmNeedsRelease; //the A press that picked a catalog item mustn't also place it
        private bool EyedropDown;
        private UIObjectSelection LastHolding;
        private bool HeldFromCatalog; //what's held is a new item from the catalog (not an object being moved)
        private Vector2? BuildCursor; //UI coordinates of the centre cursor, set in Update for Draw
        private float CatalogEmptyTime; //how long the catalog has had nothing to focus while it has the pad
        private const float CatalogEmptyGrace = 1f; //it fades between subcategories and items; don't drop out mid-fade
        /// <summary>
        /// The second screen's catalog while it shows one (buy/build mode with two screens): LT and RT, and the
        /// catalog's focus, work there instead of on the main screen's catalog panel. Set each update.
        /// </summary>
        private UIDualCatalog DualCatalog;

        /// <summary>
        /// X: the player chose direct control (it applies only in live mode with the 3D camera). Kept for the session,
        /// not per lot, so it survives travelling (each lot gets new lot controls); until X is pressed, the setting.
        /// </summary>
        private static bool? SessionDirectMode;
        private bool DirectMode
        {
            get => SessionDirectMode ?? GlobalSettings.Default.GamePadDirectControl;
            set => SessionDirectMode = value;
        }
        private VMAvatar DirectAvatar; //the avatar direct control is currently enabled on
        private int DirectInputID;
        private short DirectFacing; //last direction the Sim was walked in, in VMDirectControlInput units
        private bool DirectRunning;
        private Vector2? DirectCamera; //smoothed camera centre while in direct control
        private sbyte DirectSimLevel; //the directly controlled Sim's floor when the camera last went to it (0: not yet)

        private int SpeedBeforePause = 1;

        private Vector2? MarkerBase; //UI coordinates, set in Update for Draw
        private Vector2? MarkerTop; //top of the target: where the beam ends and the note bobs (null for empty floor)
        private const int RingSegments = 48;
        private readonly Vector2[] RingPoints = new Vector2[RingSegments]; //UI points round the floor ring; NaN where unseen
        private float RingBand = 4.5f; //UI px thickness of the ring's bright band
        private float AnimTime;

        public UILotControlPadHelper(UILotControl master)
        {
            Master = master;
        }

        private FSO.LotView.World World => Master.World;

        public override void Update(UpdateState state)
        {
            base.Update(state);
            Capturing = false;
            PadActive = false;
            ModeMenuActive = false;
            CategoryMenuActive = false;
            Chords.Holdable[PadChords.RT] = true;
            CutawayFocus = null;
            MarkerBase = MarkerTop = null;
            BuildCursor = null;
            Master.PadCursor = null;
            Master.PadModifiers = 0;

            var screen = Master.Parent as TS1GameScreen;
            var vm = Master.vm;
            if (screen?.Frontend == null || !vm.Ready || vm.Context.Architecture == null || !Master.Visible || !screen.Visible)
            {
                EndQueueFocus();
                return;
            }

            var frontend = screen.Frontend;
            Now = state.Time.TotalGameTime.TotalSeconds;

            //direct control follows the active Sim, and is off outside live mode and in 2D. Every frame starts by
            //telling the Sim to stand still; it only walks if the stick drives it further down.
            var is3D = World.State.Cameras.ActiveType == CameraControllerType._3D;
            //FISH EYE ends if live mode does, the Sim goes, or something else took the camera
            if (FishEye && !(Master.LiveMode && Master.ActiveEntity is VMAvatar
                && World.State.Cameras.ActiveType == CameraControllerType.Direct)) ExitFishEye(frontend);
            SyncDirectControl(screen, (((DirectMode && is3D) || FishEye) && Master.LiveMode) ? Master.ActiveEntity as VMAvatar : null);
            SendDirectInput(new VMDirectControlInput { ID = DirectInputID++, LookDirectionInt = LookFacing() });

            //the mode menu (the Simitone button's fan of modes, with the household's Sims): holding Select opens it
            if (frontend.MainPanel.ShowingSelect && frontend.ModeSwitcher != null && !GameFacade.Screens.HasModalDialog
                && state.InputManager?.GetFocus() == null)
            {
                SelectDownAt = -1;
                Chords.Reset();
                EndQueueFocus();
                ClearNavigators(frontend);
                UpdateModeMenu(state, frontend);
                return;
            }
            ModeMenuNav.Clear();

            ReleaseDetachedFocus(state);
            var lotHasPad = !GameFacade.Screens.HasModalDialog
                && state.InputManager?.GetFocus() == null
                && !frontend.MainPanel.ShowingSelect
                && !frontend.CheatTextbox.Visible;
            if (!lotHasPad)
            {
                SelectDownAt = -1;
                Chords.Reset();
                EndQueueFocus();
                ClearNavigators(frontend);
                return;
            }

            var panel = frontend.MainPanel;
            var buildMode = !Master.LiveMode && (panel.Mode == UIMainPanelMode.BUY || panel.Mode == UIMainPanelMode.BUILD);
            var dual = UIDualCatalog.Current;
            DualCatalog = (buildMode && SecondScreen.Active && dual != null && dual.Parent != null && dual.Browse != null) ? dual : null;
            if (!(Master.LiveMode || buildMode) || (DualCatalog == null && !panel.PanelActive)) PanelFocus = false;
            var optionsNav = !Master.LiveMode && panel.Mode == UIMainPanelMode.OPTIONS && panel.PanelActive;
            var panelNav = optionsNav || PanelFocus;
            if (!panelNav) ClearNavigators(frontend);

            var pad = state.Pad;
            var dt = (float)Math.Min(state.Time.ElapsedGameTime.TotalSeconds, 0.1);
            var now = state.Time.TotalGameTime.TotalSeconds;
            //analog input only counts while the pad is the device in use, so a drifting stick can't move
            //the camera under a mouse player. Buttons are safe: pressing one makes the pad the active device.
            var padInUse = state.LastInputDevice == InputDevice.GamePad;
            var wasPadActive = WasPadActive;
            WasPadActive = false;

            //the panel's tab fan (RT held, or the mouse opened it): the d-pad picks a tab
            if (padInUse && panel.PanelActive && panel.Switcher.Expanded)
            {
                Chords.Reset();
                EndQueueFocus();
                UpdateCategoryMenu(state, panel);
                return;
            }
            CategoryMenuNav.Clear();

            if (!Master.PieMenuOpen)
            {
                Chords.Update(new[] { pad.IsDown(PadAction.ShoulderLeft), pad.IsDown(PadAction.ShoulderRight),
                    pad.IsDown(PadAction.TriggerLeft), pad.IsDown(PadAction.TriggerRight) }, now);
                if (padInUse) UpdateCamera(pad, dt, screen);
                HandleSharedButtons(pad, screen, frontend, dpadFree: !panelNav);
                if (Chords.Combo) //LB+RB+LT+RT held: the cheat box, as the console games had it
                {
                    frontend.CheatTextbox.OpenForPad(state);
                    return;
                }
            }
            else
            {
                SelectDownAt = -1;
                Chords.Reset();
            }
            var catalogFocusBefore = PanelFocus;
            if (!PanelFocus) CatalogEmptyTime = 0;
            var selectionBefore = BuildSelection();
            if (panelNav)
            {
                if (DualCatalog != null) UpdateDualCatalogNavigation(state, frontend);
                else UpdatePanelNavigation(state, frontend, optionsNav);
            }
            if (optionsNav) PadPrompts.Set(PadPrompt.For(PadStick.DPad, "Move"), PadPrompt.Of(PadAction.Confirm, "Select"),
                PadPrompt.Of(PadAction.Cancel, "Back to live"));

            if (buildMode)
            {
                UpdateBuild(state, frontend, padInUse, dt, catalogFocusBefore, selectionBefore);
                return;
            }

            var active = Master.ActiveEntity;
            if (!Master.LiveMode || active == null)
            {
                EndQueueFocus();
                return;
            }

            Capturing = true;
            state.PadCaptured = true;
            PadActive = padInUse;
            WasPadActive = PadActive;

            TrackActiveSim(active);
            if (!PadActive)
            {
                EndQueueFocus(); //the mouse took over
                return;
            }
            if (!SelectorPlaced) PlaceSelectorOn(active, true);

            var direct = DirectAvatar != null && DirectAvatar == active;
            if (FishEye) KeepFishEyeOn(active);
            else if (direct) FollowDirect(active, dt);

            var recentlyMoved = !direct && now - LastSelectorMove < CutawayFocusSeconds;
            CutawayFocus = recentlyMoved ? SelectorOrTargetTile() : new Vector2(active.VisualPosition.X, active.VisualPosition.Y);

            if (Master.PieMenuOpen) //the pie menu reads the pad itself
            {
                PadPrompts.Set(PadPrompt.For(PadStick.Left, "Point"), PadPrompt.Of(PadAction.Confirm, "Choose"), PadPrompt.Of(PadAction.Cancel, "Back"));
                return;
            }

            var wasQueueFocus = QueueFocus;
            var wasPanelFocus = PanelFocus;
            if (QueueFocus) UpdateQueueFocus(pad);
            else
            {
                HandleLiveButtons(pad, frontend, is3D);
                if (!PanelFocus)
                {
                    if (direct)
                    {
                        DriveDirect(pad.LeftStick);
                        if (!DirectSteering) StandUpForStick(active, pad.LeftStick, dt, now);
                        else
                        {
                            DirectSeated = false;
                            StandUpHeldFor = 0;
                        }
                    }
                    else MoveSelector(pad.LeftStick, dt, now);
                }
            }
            if (QueueFocus || PanelFocus || wasPanelFocus) //the stick and A belong to the panel
            {
                if (QueueFocus) PadPrompts.Set(PadPrompt.For(PadStick.DPad, "Choose"), PadPrompt.Of(PadAction.Confirm, "Cancel action"),
                    PadPrompt.Of(PadAction.Cancel, "Back"));
                else PadPrompts.Set(PadPrompt.For(PadStick.DPad, "Move"), PadPrompt.Of(PadAction.Confirm, "Select"),
                    PadPrompt.Of(PadAction.Cancel, "Back"), PadPrompt.Of(PadAction.TriggerRight, "Next tab"));
                return;
            }

            if (direct && DirectSteering) UpdateTargetInFront(active);
            else if (direct) SetTarget(active, BusyTarget(active)); //carrying out a queued action
            else UpdateTargetAtSelector(active);
            UpdateMarker(state, dt, floorRing: !direct);
            //the press that hands control to the pad only reveals the selector; it doesn't act on whatever is under it.
            //In direct control, A with nothing in front of the Sim does nothing (there's no "go here").
            if (wasPadActive && !wasQueueFocus && pad.Pressed(PadAction.Confirm) && (!direct || Target != null)) OpenMenu(state);
            SetLivePrompts(direct, is3D, panel);
        }

        /// <summary>The direction the Sim's body turns to: where the camera looks in FISH EYE, else where it last walked.</summary>
        private short LookFacing()
        {
            return FishEye ? PadMath.DirectionToShort(World.Transform(new Vector2(0, -1))) : DirectFacing;
        }

        /// <summary>
        /// FISH EYE: FreeSO's Direct camera, which sits at an avatar's head (FirstPersonAvatar) with the head hidden. The
        /// Sim is under direct control: the left stick walks relative to the view, the right stick looks around, and
        /// the body turns to face where the camera looks. Walls go up (with roofs) and every floor is drawn, as FreeSO
        /// does for first person.
        /// </summary>
        private void EnterFishEye(UISimitoneFrontend frontend, VMAvatar sim)
        {
            if (!FSOEnvironment.Enable3D)
            {
                ShowHint("FISH EYE needs the 3D renderer.");
                HITVM.Get().PlaySoundEvent(UISounds.Error);
                return;
            }
            World.ToggleFirstPerson(CameraControllerType.Direct);
            if (World.State.Cameras.ActiveType != CameraControllerType.Direct) return;
            var camera = World.State.Cameras.CameraDirect;
            camera.CaptureMouse = false; //the pad looks, not the mouse
            camera.RotationX = PadMath.FirstPersonYaw(sim.RadianDirection); //start looking where the Sim faces
            camera.RotationY = (float)Math.PI / 2; //level
            FishEyeWallsBefore = Master.WallsMode;
            frontend.SetWallsMode(3);
            World.State.Level = 5;
            FishEye = true;
            KeepFishEyeOn(sim);
            ShowHint("FISH EYE");
            HITVM.Get().PlaySoundEvent(UISounds.Click);
        }

        /// <summary>Keeps the camera in the active Sim's head (it can change with LB/RB).</summary>
        private void KeepFishEyeOn(VMEntity sim)
        {
            var head = sim.WorldUI as AvatarComponent;
            var camera = World.State.Cameras.CameraDirect;
            if (FishEyeHead != head)
            {
                if (FishEyeHead != null) FishEyeHead.Avatar.HideHead = false;
                FishEyeHead = head;
            }
            if (head != null) head.Avatar.HideHead = true;
            camera.FirstPersonAvatar = head;
            Selector = new Vector2(sim.VisualPosition.X, sim.VisualPosition.Y);
        }

        private void ExitFishEye(UISimitoneFrontend frontend)
        {
            FishEye = false;
            if (FishEyeHead != null) FishEyeHead.Avatar.HideHead = false;
            FishEyeHead = null;
            World.State.Cameras.CameraDirect.FirstPersonAvatar = null;
            if (World.State.Cameras.ActiveType == CameraControllerType.Direct) World.ToggleFirstPerson(CameraControllerType.Direct);
            frontend.SetWallsMode(FishEyeWallsBefore);
            var active = Master.ActiveEntity;
            if (active != null)
            {
                World.State.Level = active.Position.Level;
                PlaceSelectorOn(active, true);
            }
        }

        /// <summary>
        /// Prompts for live mode, cursor or direct control, naming what A does on the current target and what RT shows
        /// next. Select (mode), Start (pause) and the d-pad (floors, speed) are left out to keep the bar short.
        /// </summary>
        private void SetLivePrompts(bool direct, bool is3D, UIMainPanel panel)
        {
            var items = new System.Collections.Generic.List<PadPrompt>();
            items.Add(PadPrompt.For(PadStick.Left, !direct ? "Move" : DirectSeated ? "Stand up" : "Walk"));
            if (FishEye) items.Add(PadPrompt.For(PadStick.Right, "Look"));
            if (Target != null) items.Add(PadPrompt.Of(PadAction.Confirm, "Interact"));
            else if (!direct) items.Add(PadPrompt.Of(PadAction.Confirm, "Go here"));
            if (FishEye) items.Add(PadPrompt.Of(PadAction.Cancel, "Normal view"));
            else if (direct) items.Add(PadPrompt.Of(PadAction.FaceLeft, "Cursor"));
            else if (is3D) items.Add(PadPrompt.Of(PadAction.FaceLeft, "Direct control"));
            items.Add(PadPrompt.Of(PadAction.FaceTop, "Actions"));
            var deck = DualDeck;
            if (deck != null)
            {
                //the second screen's pages (needs, job, personality, relationships, inventory)
                var names = UIDualDeck.PageNames;
                items.Add(PadPrompt.Of(PadAction.TriggerLeft, "Prev: " + names[(deck.Page + names.Length - 1) % names.Length]));
                items.Add(PadPrompt.Of(PadAction.TriggerRight, "Next: " + names[(deck.Page + 1) % names.Length]));
            }
            else
            {
                items.Add(PadPrompt.Of(PadAction.TriggerLeft, "Needs"));
                items.Add(PadPrompt.Of(PadAction.TriggerRight, NextPanelLabel(panel)));
            }
            items.Add(PadPrompt.Pair(PadAction.ShoulderLeft, PadAction.ShoulderRight, "Switch Sim"));
            items.Add(PadPrompt.Of(PadAction.LeftStickClick, "Walls"));
            if (!direct) items.Add(PadPrompt.Of(PadAction.RightStickClick, "Find Sim")); //the cursor back on the Sim
            if (FishEyeCheat && !FishEye) items.Add(PadPrompt.Of(PadAction.Cancel, "FISH EYE"));
            PadPrompts.Set(items.ToArray());
        }

        /// <summary>What RT does in live mode: opens the Sim info panel, or shows its next tab ("Next: Job").</summary>
        private static string NextPanelLabel(UIMainPanel panel)
        {
            if (!panel.PanelActive) return "Sim info";
            var categories = panel.Switcher.Categories;
            if (categories == null || categories.Count == 0) return "Next tab";
            var index = categories.FindIndex(x => x.ID == panel.Switcher.ActiveCategory);
            return "Next: " + categories[(index + 1) % categories.Count].Name;
        }

        #region Buy and build

        /// <summary>What the player has in hand: the held object's group, the build tool, or null.</summary>
        private object BuildSelection()
        {
            return (object)Master.ObjectHolder.Holding?.Group ?? Master.CustomControl;
        }

        private void UpdateBuild(UpdateState state, UISimitoneFrontend frontend, bool padInUse, float dt,
            bool catalogFocusBefore, object selectionBefore)
        {
            var pad = state.Pad;
            var holder = Master.ObjectHolder;
            Capturing = true;
            state.PadCaptured = true;
            PadActive = padInUse;

            //picking something in the catalog hands the pad to the lot to place it. The A press that picked it is
            //still down, and mustn't also place it.
            var selection = BuildSelection();
            if (PanelFocus && selection != null && selection != selectionBefore)
            {
                PanelFocus = false;
                ClearNavigators(frontend);
                ConfirmNeedsRelease = true;
            }
            if (holder.Holding != LastHolding)
            {
                LastHolding = holder.Holding;
                HeldFromCatalog = holder.Holding != null && !holder.Holding.IsBought;
            }

            if (DualCatalog != null) HandleDualCatalogTriggers();
            else HandlePanelTriggers(pad, frontend, canClose: false);

            if (!PadActive || PanelFocus || catalogFocusBefore)
            {
                EndBuildPointer(state); //nothing is dragged while the catalog or the mouse has control
                if (PanelFocus) SetCatalogPrompts(frontend);
                return;
            }

            var centre = new Vector2(ScreenWidth, ScreenHeight) / 2;
            var level = World.State.Level;
            var centreTile = World.EstTileAtPosWithScroll(FromUI(centre));
            Master.PadCursor = centre.ToPoint();
            if (Master.CustomControl != null)
            {
                Master.PadModifiers = (pad.IsDown(PadAction.FaceLeft) ? UILotControlModifiers.SHIFT : 0)
                    | (pad.IsDown(PadAction.FaceTop) ? UILotControlModifiers.CTRL : 0);
            }
            CutawayFocus = centreTile;
            BuildCursor = centre;
            AnimTime += dt;

            ScrollLot(pad.LeftStick, dt, centreTile, level);

            //A: the mouse button
            if (ConfirmNeedsRelease)
            {
                if (!pad.IsDown(PadAction.Confirm)) ConfirmNeedsRelease = false;
            }
            else if (pad.Pressed(PadAction.Confirm) && !BuildConfirmDown && !EyedropDown)
            {
                Master.SimulateMD(state);
                BuildConfirmDown = true;
            }
            if (BuildConfirmDown && !pad.IsDown(PadAction.Confirm))
            {
                var wasHolding = holder.Holding;
                var fromCatalog = HeldFromCatalog;
                BuildConfirmDown = false;
                Master.SimulateMU(state);
                //placed a new item: back to the catalog, on the same item, to pick another
                if (wasHolding != null && holder.Holding == null && fromCatalog) FocusCatalog(frontend);
            }

            if (BuildConfirmDown) return; //mid-click or mid-drag: only the stick and A apply

            if (holder.Holding != null)
            {
                if (pad.Pressed(PadAction.ShoulderLeft)) holder.RotateObject(-2);
                if (pad.Pressed(PadAction.ShoulderRight)) holder.RotateObject(2);
                if (pad.Pressed(PadAction.FaceTop))
                {
                    var fromCatalog = HeldFromCatalog;
                    holder.SellBack(null);
                    if (fromCatalog && holder.Holding == null) FocusCatalog(frontend);
                }
                else if (pad.Pressed(PadAction.Cancel))
                {
                    var fromCatalog = HeldFromCatalog;
                    holder.Cancel();
                    if (fromCatalog) FocusCatalog(frontend);
                    HITVM.Get().PlaySoundEvent(UISounds.Click);
                }
            }
            else if (Master.CustomControl != null)
            {
                //X and Y are the tool's modifiers (above)
                if (pad.Pressed(PadAction.Cancel))
                {
                    Master.ReleaseCustomControl();
                    FocusCatalog(frontend);
                    HITVM.Get().PlaySoundEvent(UISounds.Click);
                }
            }
            else
            {
                //X: eyedropper on whatever is under the cursor; it picks the same item from the catalog
                if (pad.Pressed(PadAction.FaceLeft))
                {
                    holder.EyedropperMode = true;
                    Master.SimulateMD(state);
                    EyedropDown = true;
                }
                if (pad.Pressed(PadAction.FaceTop))
                {
                    ShowHint($"Pick it up with {PadGlyphs.ActionName(PadAction.Confirm, pad)}, then {PadGlyphs.ActionName(PadAction.FaceTop, pad)} sells it.");
                    HITVM.Get().PlaySoundEvent(UISounds.Error);
                }
                if (pad.Pressed(PadAction.Cancel))
                {
                    FocusCatalog(frontend);
                    HITVM.Get().PlaySoundEvent(UISounds.Click);
                }
            }
            if (EyedropDown && !pad.IsDown(PadAction.FaceLeft))
            {
                EyedropDown = false;
                Master.SimulateMU(state);
                holder.EyedropperMode = false;
            }
            SetBuildPrompts(holder);
        }

        /// <summary>Prompts on the lot in buy/build mode, for what's in hand.</summary>
        private void SetBuildPrompts(UIObjectHolder holder)
        {
            var move = PadPrompt.For(PadStick.Left, "Move");
            if (holder.Holding != null)
            {
                PadPrompts.Set(move, PadPrompt.Of(PadAction.Confirm, "Place"),
                    PadPrompt.Pair(PadAction.ShoulderLeft, PadAction.ShoulderRight, "Rotate"),
                    PadPrompt.Of(PadAction.FaceTop, "Sell"), PadPrompt.Of(PadAction.Cancel, "Put back"));
            }
            else if (Master.CustomControl != null)
            {
                PadPrompts.Set(move, PadPrompt.Of(PadAction.Confirm, "Use (hold to drag)"),
                    PadPrompt.Of(PadAction.FaceLeft, "Whole room (hold)"), PadPrompt.Of(PadAction.FaceTop, "Erase (hold)"),
                    PadPrompt.Of(PadAction.Cancel, "Put away"));
            }
            else
            {
                PadPrompts.Set(move, PadPrompt.Of(PadAction.Confirm, "Pick up"), PadPrompt.Of(PadAction.FaceLeft, "Eyedropper"),
                    PadPrompt.Of(PadAction.TriggerLeft, "Catalog"), PadPrompt.Of(PadAction.LeftStickClick, "Walls"),
                    PadPrompt.Of(PadAction.Select, "Mode (hold: menu)"));
            }
        }

        /// <summary>Prompts while the catalog has the pad.</summary>
        private void SetCatalogPrompts(UISimitoneFrontend frontend)
        {
            var browse = (DualCatalog != null) ? DualCatalog.Browse : frontend.MainPanel.SubPanel as UIBuyBrowsePanel;
            var items = new System.Collections.Generic.List<PadPrompt> { PadPrompt.For(PadStick.DPad, "Browse") };
            if (browse != null && !browse.ChoosingSub)
                items.Add(PadPrompt.Of(PadAction.Confirm, (browse.Mode == UICatalogMode.Build) ? "Choose" : "Buy"));
            else items.Add(PadPrompt.Of(PadAction.Confirm, "Open"));
            items.Add(PadPrompt.Of(PadAction.Cancel, "Back"));
            if (DualCatalog == null && frontend.MainPanel.VisibleSearchField != null) items.Add(PadPrompt.Of(PadAction.FaceTop, "Search"));
            items.Add(PadPrompt.Of(PadAction.TriggerRight, "Category"));
            items.Add(PadPrompt.Of(PadAction.TriggerLeft, "Lot"));
            PadPrompts.Set(items.ToArray());
        }

        /// <summary>Lets go of anything the pad is pressing on the lot (A or the eyedropper).</summary>
        private void EndBuildPointer(UpdateState state)
        {
            if (BuildConfirmDown)
            {
                BuildConfirmDown = false;
                Master.SimulateMU(state);
            }
            if (EyedropDown)
            {
                EyedropDown = false;
                Master.SimulateMU(state);
                Master.ObjectHolder.EyedropperMode = false;
            }
        }

        /// <summary>Gives the catalog the pad, if the panel is open and has anything to select.</summary>
        private void FocusCatalog(UISimitoneFrontend frontend)
        {
            if (DualCatalog != null)
            {
                if (UIFocusNavigator.HasFocusable(DualCatalog.Browse)) PanelFocus = true;
                return;
            }
            var panel = frontend.MainPanel;
            if (panel.PanelActive && UIFocusNavigator.HasFocusable(panel.SubPanel)) PanelFocus = true;
        }

        private void ClearNavigators(UISimitoneFrontend frontend)
        {
            frontend.PanelNavigator.Clear();
            UIDualCatalog.Navigator.Clear();
        }

        /// <summary>
        /// The second screen's catalog has the pad: the d-pad moves over its subcategories or items, A opens or picks,
        /// B goes back to the subcategories, then to the lot. Its focus is drawn on that screen.
        /// </summary>
        private void UpdateDualCatalogNavigation(UpdateState state, UISimitoneFrontend frontend)
        {
            frontend.PanelNavigator.Clear();
            var browse = DualCatalog.Browse;
            if (!UIDualCatalog.Navigator.Update(state, browse))
            {
                UIDualCatalog.Navigator.Clear();
                CatalogEmptyTime += (float)state.Time.ElapsedGameTime.TotalSeconds;
                if (CatalogEmptyTime >= CatalogEmptyGrace || state.Pad.Pressed(PadAction.Cancel)) PanelFocus = false;
                else state.PadCaptured = true;
                return;
            }
            CatalogEmptyTime = 0;
            state.PadCaptured = true;
            UIDualCatalog.PadShown = state.LastInputDevice == InputDevice.GamePad;
            if (state.Pad.Pressed(PadAction.Cancel))
            {
                if (!browse.ChoosingSub) browse.Reset(); //items -> subcategories
                else PanelFocus = false;
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }
        }

        /// <summary>
        /// LT and RT with the catalog on the second screen: LT moves the pad between the lot and the catalog (the
        /// main screen's catalog panel stays closed), RT opens the next category.
        /// </summary>
        private void HandleDualCatalogTriggers()
        {
            if (Chords.Tap[PadChords.LT])
            {
                if (!PanelFocus && UIFocusNavigator.HasFocusable(DualCatalog.Browse)) PanelFocus = true;
                else PanelFocus = false;
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }
            if (Chords.Tap[PadChords.RT])
            {
                DualCatalog.NextCategory();
                UIDualCatalog.Navigator.Clear(); //a new catalog panel: focus starts on its first subcategory
                CatalogEmptyTime = 0;
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }
        }

        /// <summary>Scrolls the lot under the centre cursor, screen-relative, keeping the cursor over the lot.</summary>
        private void ScrollLot(Vector2 stick, float dt, Vector2 centreTile, sbyte level)
        {
            if (stick == Vector2.Zero) return;
            var screenDelta = new Vector2(stick.X, -stick.Y) * (BuildScrollSpeed * PxToUI * stick.Length() * dt);
            var tileDelta = ScreenToTileDelta(centreTile, screenDelta, level);
            tileDelta = ClampToLot(centreTile + tileDelta) - centreTile;
            World.State.CenterTile += tileDelta;
            World.State.ScrollAnchor = null;
            FollowSim = false;
        }

        #endregion

        #region Direct control

        /// <summary>Turns direct control on for <paramref name="wanted"/> (or nobody), and off for whoever had it.</summary>
        private void SyncDirectControl(TS1GameScreen screen, VMAvatar wanted)
        {
            if (wanted == DirectAvatar) return;
            //a sim we no longer control was already let go by the control change (VMNetChangeControlCmd), and can't be
            //addressed by a command anyway: its PersistID is 0 now.
            if (DirectAvatar != null && !DirectAvatar.Dead && DirectAvatar.PersistID == Master.vm.MyUID)
                SendToggle(screen, DirectAvatar, false);
            //leaving direct control in live mode (X, or the camera went 2D): the cursor starts at the Sim, not where it was left
            if (DirectAvatar != null && wanted == null && Master.LiveMode && Master.ActiveEntity != null)
                PlaceSelectorOn(Master.ActiveEntity, true);
            DirectAvatar = wanted;
            DirectCamera = null;
            DirectSimLevel = 0;
            DirectRunning = false;
            if (wanted != null)
            {
                SendToggle(screen, wanted, true);
                DirectFacing = PadMath.RadiansToDirection(wanted.RadianDirection);
            }
        }

        private static void SendToggle(TS1GameScreen screen, VMAvatar avatar, bool enable)
        {
            //addressed to this sim explicitly rather than through vm.SendCommand (the selected sim): the two differ
            //while a control change is queued but hasn't run yet.
            screen.Driver.SendCommand(new VMNetDirectControlToggleCommand { ActorUID = avatar.PersistID, Enable = enable });
        }

        /// <summary>Walks the Sim in the stick's direction relative to the camera, running at nearly full tilt.</summary>
        private void DriveDirect(Vector2 stick)
        {
            if (stick == Vector2.Zero) //the stand-still input sent at the start of the frame applies
            {
                DirectRunning = false;
                return;
            }
            //(x, -y) like the keyboard axes FreeSO's own direct control uses: screen up is -y.
            var move = World.Transform(new Vector2(stick.X, -stick.Y));
            if (move == Vector2.Zero) return;
            DirectFacing = PadMath.DirectionToShort(move);
            DirectRunning = PadMath.Run(DirectRunning, stick.Length(), RunOn, RunOff);
            SendDirectInput(new VMDirectControlInput
            {
                ID = DirectInputID++,
                Direction = DirectFacing,
                InputIntensity = Math.Max(1, (int)(stick.Length() * 100)),
                //the frame turns the body towards LookDirectionInt; pointing it where the Sim walks gives a
                //third-person feel. In FISH EYE it's where the camera looks, as TSO's first person did.
                LookDirectionInt = LookFacing(),
                Sprint = DirectRunning
            });
        }

        /// <summary>
        /// Keeps the camera on a directly controlled Sim. Rather than anchoring it (World.CenterTo), which locks onto the
        /// pelvis bone every frame and so shakes with the running animation's sway, this follows the Sim's position with
        /// a little smoothing.
        /// </summary>
        private void FollowDirect(VMEntity sim, float dt)
        {
            World.State.ScrollAnchor = null;
            if (sim.Position == LotTilePos.OUT_OF_WORLD) return;
            var target = new Vector2(sim.VisualPosition.X, sim.VisualPosition.Y);
            var current = DirectCamera ?? World.State.CenterTile;
            DirectCamera = (Vector2.Distance(current, target) > DirectCameraSnapTiles)
                ? target
                : PadMath.Follow(current, target, DirectCameraRate, dt);
            World.State.CenterTile = DirectCamera.Value;
            //up and down stairs: the view goes to the Sim's floor when they change floors, and otherwise stays
            //on whichever floor the player chose
            if (sim.Position.Level != DirectSimLevel)
            {
                DirectSimLevel = sim.Position.Level;
                World.State.Level = sim.Position.Level;
            }
            Selector = target;
        }

        private void SendDirectInput(VMDirectControlInput input)
        {
            if (DirectAvatar?.Thread?.Stack.LastOrDefault() is VMDirectControlFrame frame) frame.SendUserControls(input);
        }

        /// <summary>A short message on screen (the pad overlay's toast).</summary>
        private static void ShowHint(string text)
        {
            UIPadOverlay.ShowToast(text);
        }

        #endregion

        /// <summary>
        /// Clears keyboard focus held by an element that is no longer on screen (e.g. the text box of a dialog that
        /// was removed). Nothing else clears it, and while it's set the lot treats a text field as focused.
        /// </summary>
        private static void ReleaseDetachedFocus(UpdateState state)
        {
            if (!(state.InputManager?.GetFocus() is UIElement focused)) return;
            var screen = GameFacade.Screens.CurrentUIScreen;
            for (UIElement element = focused; element != null; element = element.Parent)
            {
                if (element == screen) return; //still attached
            }
            state.InputManager.SetFocus(null);
        }

        #region Buttons

        /// <param name="dpadFree">False while a panel is navigated with the d-pad.</param>
        private void HandleSharedButtons(GamePadInput pad, TS1GameScreen screen, UISimitoneFrontend frontend, bool dpadFree)
        {
            //Select: a tap moves to the next mode, a hold opens the mode menu to pick one
            if (pad.Pressed(PadAction.Select)) SelectDownAt = Now;
            if (SelectDownAt >= 0)
            {
                if (!pad.IsDown(PadAction.Select))
                {
                    SelectDownAt = -1;
                    frontend.CycleMode();
                }
                else if (Now - SelectDownAt >= ModeMenuHold && frontend.ModeSwitcher != null)
                {
                    SelectDownAt = -1;
                    frontend.ModeSwitcher.SwitchMode(frontend.MainPanel.Mode); //as clicking the Simitone button does
                    HITVM.Get().PlaySoundEvent(UISounds.Click);
                }
            }
            if (pad.Pressed(PadAction.Start)) TogglePause(screen);

            if (dpadFree) HandleDpad(pad, screen);

            if (pad.Pressed(PadAction.LeftStickClick))
            {
                var walls = (Master.WallsMode + 1) % 4;
                frontend.SetWallsMode(walls);
                ShowHint(new[] { "Walls down", "Walls cutaway", "Walls up", "Roofs" }[walls]);
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }

            if (pad.Pressed(PadAction.RightStickClick) && Master.ActiveEntity != null)
            {
                PlaceSelectorOn(Master.ActiveEntity, true);
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }
        }

        /// <summary>
        /// The mode menu: the d-pad moves between the modes and the household's Sims, A picks one, B closes it in the
        /// current mode. Its buttons show focus with their own hover (they grow).
        /// </summary>
        private void UpdateModeMenu(UpdateState state, UISimitoneFrontend frontend)
        {
            MenuScope.Frontend = frontend;
            if (!ModeMenuNav.Update(state, MenuScope)) return;
            ModeMenuActive = true;
            Capturing = true;
            state.PadCaptured = true;
            PadPrompts.Set(PadPrompt.For(PadStick.DPad, "Move"), PadPrompt.Of(PadAction.Confirm, "Select"),
                PadPrompt.Of(PadAction.Cancel, "Back"));
        }

        /// <summary>The mode menu as a pad scope: the mode buttons and the Sim portraits under them.</summary>
        private class ModeMenuScope : UIElement, IPadScope, IPadScopeRoots
        {
            public UISimitoneFrontend Frontend;

            public IEnumerable<UIElement> PadRoots
            {
                get
                {
                    yield return Frontend.ModeSwitcher;
                    if (Frontend.MainPanel.SwitchAvatar != null) yield return Frontend.MainPanel.SwitchAvatar;
                }
            }

            public UIElement PadDefaultFocus
            {
                get
                {
                    var modes = Frontend.ModeSwitcher;
                    switch (Frontend.MainPanel.Mode)
                    {
                        case UIMainPanelMode.BUY: return modes.BuyButton;
                        case UIMainPanelMode.BUILD: return modes.BuildButton;
                        case UIMainPanelMode.OPTIONS: return modes.OptionButton;
                        default: return modes.LiveButton;
                    }
                }
            }

            /// <summary>Picking the current mode closes the menu, as with the mouse.</summary>
            public bool PadCancel()
            {
                Frontend.ModeSwitcher.SwitchMode(Frontend.MainPanel.Mode);
                return true;
            }

            public override void Draw(UISpriteBatch batch) { }
        }

        private void HandleDpad(GamePadInput pad, TS1GameScreen screen)
        {
            if (pad.PressedRepeat(PadAction.Up) && screen.Level < MaxFloor)
            {
                screen.Level++;
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }
            if (pad.PressedRepeat(PadAction.Down) && screen.Level > 1)
            {
                screen.Level--;
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }

            if (!QueueFocus) //left/right move the queue focus instead
            {
                var speed = CurrentSpeed(Master.vm);
                if (pad.Pressed(PadAction.Right) && speed >= 0 && speed < 3) screen.ChangeSpeedTo(speed + 1);
                if (pad.Pressed(PadAction.Left) && speed > 0) screen.ChangeSpeedTo(speed - 1);
            }
        }

        /// <summary>
        /// Runs the panel navigator on the open subpanel (needs panel when focused, or Options). B leaves: the needs
        /// panel stays open but unfocused, and Options returns to live mode.
        /// </summary>
        private void UpdatePanelNavigation(UpdateState state, UISimitoneFrontend frontend, bool optionsMode)
        {
            var subpanel = frontend.MainPanel.SubPanel;
            if (subpanel == null || !frontend.PanelNavigator.Update(state, subpanel))
            {
                ClearNavigators(frontend);
                //the catalog cross-fades between subcategories and items, and may briefly have nothing to focus
                CatalogEmptyTime += (float)state.Time.ElapsedGameTime.TotalSeconds;
                var fading = subpanel is UIBuyBrowsePanel && CatalogEmptyTime < CatalogEmptyGrace;
                if (!fading || state.Pad.Pressed(PadAction.Cancel)) PanelFocus = false; //nothing to navigate on this tab
                else state.PadCaptured = true;
                return;
            }
            CatalogEmptyTime = 0;
            state.PadCaptured = true;

            //Y in the catalog: type a search (the on-screen keyboard comes up on the search box)
            if (subpanel is UIBuyBrowsePanel && state.Pad.Pressed(PadAction.FaceTop) && frontend.MainPanel.VisibleSearchField != null)
            {
                state.InputManager.SetFocus(frontend.MainPanel.VisibleSearchField);
                HITVM.Get().PlaySoundEvent(UISounds.Click);
                return;
            }

            if (state.Pad.Pressed(PadAction.Cancel))
            {
                if (optionsMode) frontend.SetModeDirect(UIMainPanelMode.LIVE);
                else if (subpanel is UIBuyBrowsePanel browse && !browse.ChoosingSub) browse.Reset(); //items -> subcategories
                else PanelFocus = false;
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }
        }

        private void HandleLiveButtons(GamePadInput pad, UISimitoneFrontend frontend, bool is3D)
        {
            if (!FishEye && pad.Pressed(PadAction.FaceLeft))
            {
                if (!is3D)
                {
                    ShowHint("Direct control needs the 3D camera.");
                    HITVM.Get().PlaySoundEvent(UISounds.Error);
                }
                else
                {
                    DirectMode = !DirectMode; //takes effect in SyncDirectControl next frame
                    ShowHint(DirectMode ? "Direct control" : "Cursor mode");
                    HITVM.Get().PlaySoundEvent(UISounds.Click);
                }
            }

            if (Chords.Tap[PadChords.LB] || Chords.Tap[PadChords.RB])
            {
                FollowSim = true; //TrackActiveSim moves the selector and camera once the switch lands
                frontend.CycleSim(Chords.Tap[PadChords.LB] ? -1 : 1);
            }

            //FISH EYE (the cheat): B, Circle on PlayStation as on PS2, in and out of the Sim's eyes
            if (FishEyeCheat && !PanelFocus && pad.Pressed(PadAction.Cancel))
            {
                if (FishEye) ExitFishEye(frontend);
                else if (Master.ActiveEntity is VMAvatar sim) EnterFishEye(frontend, sim);
            }

            var deck = DualDeck;
            if (deck != null) HandleDualPageTriggers(deck);
            else HandlePanelTriggers(pad, frontend, canClose: true);

            if (!PanelFocus && pad.Pressed(PadAction.FaceTop) && Master.Queue.FocusLastForPad()) QueueFocus = true;
        }

        /// <summary>The second screen's live controls, while it shows them: the Sim's pages are there.</summary>
        private static UIDualDeck DualDeck
        {
            get
            {
                var deck = UIDualDeck.Current;
                return (SecondScreen.Active && deck != null && deck.Parent != null) ? deck : null;
            }
        }

        /// <summary>LT and RT in live mode with two screens: the previous and next of the Sim's pages there.</summary>
        private void HandleDualPageTriggers(UIDualDeck deck)
        {
            PanelFocus = false;
            if (Chords.Tap[PadChords.LT]) deck.TurnPage(-1);
            if (Chords.Tap[PadChords.RT]) deck.TurnPage(1);
        }

        /// <summary>
        /// LT: panel closed, then open, then focused (if this tab has anything to select), then closed again, or
        /// unfocused when <paramref name="canClose"/> is false (buy/build, where closing the panel drops the catalog).
        /// RT: next tab.
        /// </summary>
        private void HandlePanelTriggers(GamePadInput pad, UISimitoneFrontend frontend, bool canClose)
        {
            var panel = frontend.MainPanel;
            if (Chords.Tap[PadChords.LT])
            {
                if (!panel.PanelActive)
                {
                    OpenPanel(panel);
                    PanelFocus = false;
                }
                else if (!PanelFocus && UIFocusNavigator.HasFocusable(panel.SubPanel)) PanelFocus = true;
                else if (canClose)
                {
                    panel.Close();
                    PanelFocus = false;
                }
                else PanelFocus = false;
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }
            if (Chords.Tap[PadChords.RT])
            {
                if (!panel.PanelActive) OpenPanel(panel);
                else
                {
                    var categories = panel.Switcher.Categories;
                    var index = categories.FindIndex(x => x.ID == panel.Switcher.ActiveCategory);
                    panel.Switcher.Select(categories[(index + 1) % categories.Count].ID);
                }
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }
            if (Chords.Hold[PadChords.RT]) //held: the fan of tabs, to pick one
            {
                if (!panel.PanelActive) OpenPanel(panel);
                if (!panel.Switcher.Expanded) panel.Switcher.Open();
                HITVM.Get().PlaySoundEvent(UISounds.Click);
            }
        }

        /// <summary>The panel's tab fan: the d-pad moves between the tabs, A picks one, B closes the fan.</summary>
        private void UpdateCategoryMenu(UpdateState state, UIMainPanel panel)
        {
            CategoryScope.Panel = panel;
            if (!CategoryMenuNav.Update(state, CategoryScope)) return;
            CategoryMenuActive = true;
            Capturing = true;
            state.PadCaptured = true;
            PadPrompts.Set(PadPrompt.For(PadStick.DPad, "Move"), PadPrompt.Of(PadAction.Confirm, "Select"),
                PadPrompt.Of(PadAction.Cancel, "Back"));
        }

        /// <summary>The tab fan as a pad scope: the tab buttons, and the main button (which closes it).</summary>
        private class CategoryMenuScope : UIElement, IPadScope, IPadScopeRoots
        {
            public UIMainPanel Panel;

            public IEnumerable<UIElement> PadRoots
            {
                get { yield return Panel.Switcher; }
            }

            public UIElement PadDefaultFocus
            {
                get
                {
                    var switcher = Panel.Switcher;
                    var active = switcher.Categories?.FirstOrDefault(x => x.ID == switcher.ActiveCategory)?.Name;
                    return (UIElement)switcher.CatSwitchButtons.FirstOrDefault(b => b.Tooltip == active)
                        ?? switcher.CatSwitchButtons.FirstOrDefault();
                }
            }

            public bool PadCancel()
            {
                Panel.Switcher.Close();
                return true;
            }

            public override void Draw(UISpriteBatch batch) { }
        }

        private static void OpenPanel(UIMainPanel panel)
        {
            panel.Open();
            panel.Switcher_OnCategorySelect(panel.Switcher.ActiveCategory);
        }

        private void TogglePause(TS1GameScreen screen)
        {
            var speed = CurrentSpeed(Master.vm);
            if (speed == 0) screen.ChangeSpeedTo(SpeedBeforePause);
            else if (speed > 0)
            {
                SpeedBeforePause = speed;
                screen.ChangeSpeedTo(0);
            }
        }

        /// <summary>Speed as ChangeSpeedTo numbers it: 0 paused, 1-3, or -1 when the clock is stopped (build/buy).</summary>
        private static int CurrentSpeed(VM vm)
        {
            switch (vm.SpeedMultiplier)
            {
                case 0: return 0;
                case 1: return 1;
                case 3: return 2;
                case 10: return 3;
                default: return -1;
            }
        }

        private void UpdateQueueFocus(GamePadInput pad)
        {
            var queue = Master.Queue;
            if (!queue.HasPadFocus)
            {
                QueueFocus = false; //the queue emptied
                return;
            }
            if (pad.NavPressedRepeat(PadDirection.Left)) queue.MovePadFocus(-1);
            if (pad.NavPressedRepeat(PadDirection.Right)) queue.MovePadFocus(1);
            if (pad.Pressed(PadAction.Confirm)) queue.CancelPadFocused();
            if (pad.Pressed(PadAction.Cancel) || pad.Pressed(PadAction.FaceTop)) EndQueueFocus();
        }

        private void EndQueueFocus()
        {
            if (!QueueFocus) return;
            Master.Queue.ClearPadFocus();
            QueueFocus = false;
        }

        #endregion

        #region Camera

        private void UpdateCamera(GamePadInput pad, float dt, TS1GameScreen screen)
        {
            var stick = pad.RightStick;
            if (FishEye)
            {
                //look: right turns right (yaw falls), up looks up (RotationY below level)
                var camera = World.State.Cameras.CameraDirect;
                camera.RotationX -= stick.X * LookRate * dt;
                var level = (float)Math.PI / 2;
                camera.RotationY = Math.Max(level - PitchLimit, Math.Min(level + PitchLimit, camera.RotationY - stick.Y * LookRate * dt));
                return;
            }
            if (Math.Abs(stick.X) < 0.3f) RotateArmed = true;
            if (stick == Vector2.Zero) return;

            if (Math.Abs(stick.Y) >= Math.Abs(stick.X))
            {
                Master.Touch.NudgeZoom((GlobalSettings.Default.GamePadInvertZoom ? -stick.Y : stick.Y) * ZoomRate * dt);
            }
            else if (Master.Touch._3D)
            {
                Master.Rotate.RotationX += stick.X * RotateRate3D * dt;
            }
            else if (RotateArmed && Math.Abs(stick.X) > 0.7f)
            {
                screen.Rotation = (screen.Rotation + ((stick.X > 0) ? 1 : 3)) % 4;
                HITVM.Get().PlaySoundEvent(UISounds.ObjectRotate);
                RotateArmed = false;
            }
        }

        /// <summary>Notices when the active sim changes, and when something else (mouse, edge scroll) takes the camera.</summary>
        private void TrackActiveSim(VMEntity active)
        {
            if (active != LastActive)
            {
                LastActive = active;
                if (FollowSim) PlaceSelectorOn(active, true);
            }
            if (FollowSim && World.State.ScrollAnchor != (active.WorldUI as AvatarComponent)) FollowSim = false;
        }

        private void PlaceSelectorOn(VMEntity entity, bool follow)
        {
            Selector = new Vector2(entity.VisualPosition.X, entity.VisualPosition.Y);
            SelectorPlaced = true;
            if (follow && entity.WorldUI is AvatarComponent avatar)
            {
                FollowSim = true;
                World.State.ScrollAnchor = avatar;
            }
        }

        #endregion

        #region Selector

        private void MoveSelector(Vector2 stick, float dt, double now)
        {
            if (stick == Vector2.Zero) return;
            var level = World.State.Level;
            var width = ScreenWidth;
            var height = ScreenHeight;

            //if the selector was left off screen (e.g. the camera followed a sim away), start from the middle.
            var pos = ToUI(World.ProjectTileToScreen(Selector, level));
            if (PadMath.Overflow(pos, 0, 0, width, height) != Vector2.Zero)
            {
                Selector = ClampToLot(World.EstTileAtPosWithScroll(FromUI(new Vector2(width, height) / 2)));
            }

            var screenDelta = new Vector2(stick.X, -stick.Y) * (SelectorSpeed * PxToUI * stick.Length() * dt);
            Selector = ClampToLot(Selector + ScreenToTileDelta(Selector, screenDelta, level));
            LastSelectorMove = now;

            //keep the selector inside a safe area by scrolling the camera instead. The bottom margin clears the HUD.
            pos = ToUI(World.ProjectTileToScreen(Selector, level));
            var overflow = PadMath.Overflow(pos, width * 0.12f, height * 0.15f, width * 0.88f, height * 0.72f);
            if (overflow != Vector2.Zero)
            {
                World.State.CenterTile += ScreenToTileDelta(Selector, overflow, level);
                World.State.ScrollAnchor = null;
                FollowSim = false;
            }
        }

        /// <summary>Average on-screen length, in UI pixels, of one tile step along X and Y at a tile.</summary>
        private float PixelsPerTile(Vector2 tile, sbyte level)
        {
            var origin = ToUI(World.ProjectTileToScreen(tile, level));
            var stepX = ToUI(World.ProjectTileToScreen(tile + Vector2.UnitX, level)) - origin;
            var stepY = ToUI(World.ProjectTileToScreen(tile + Vector2.UnitY, level)) - origin;
            return (stepX.Length() + stepY.Length()) / 2;
        }

        /// <summary>Converts a movement in UI pixels at a tile into the tile-space movement that produces it.</summary>
        private Vector2 ScreenToTileDelta(Vector2 tile, Vector2 screenDelta, sbyte level)
        {
            var origin = ToUI(World.ProjectTileToScreen(tile, level));
            var stepX = ToUI(World.ProjectTileToScreen(tile + Vector2.UnitX, level)) - origin;
            var stepY = ToUI(World.ProjectTileToScreen(tile + Vector2.UnitY, level)) - origin;
            return PadMath.SolveBasis(stepX, stepY, screenDelta);
        }

        private Vector2 ClampToLot(Vector2 tile)
        {
            var arch = Master.vm.Context.Architecture;
            return new Vector2(
                Math.Max(0.5f, Math.Min(arch.Width - 0.5f, tile.X)),
                Math.Max(0.5f, Math.Min(arch.Height - 0.5f, tile.Y)));
        }

        /// <summary>
        /// Cursor mode: the interactable object or person nearest the selector, within a snap radius of its footprint.
        /// The radius comes from a fixed on-screen distance (a fixed tile radius felt sticky up close, e.g. with the 3D
        /// camera); the tie-break biases scale with it.
        /// </summary>
        private void UpdateTargetAtSelector(VMEntity active)
        {
            var point = Selector;
            var snapRadius = PadMath.SnapRadius(PixelsPerTile(Selector, World.State.Level), SnapPixels * PxToUI, MinSnapTiles, MaxSnapTiles);
            PickTarget(active, World.State.Level, Math.Min(1f, snapRadius / MaxSnapTiles), (entity, x1, y1, x2, y2) =>
            {
                var distance = PadMath.DistanceToFootprint(point, x1, y1, x2, y2);
                return (distance > snapRadius) ? float.PositiveInfinity : distance;
            });
        }

        /// <summary>
        /// Direct control: what the Sim is facing. Only things in the Sim's own room count, so nothing is picked through
        /// a wall (walls and fences bound rooms, and each tile of a multi-tile object is checked on its own).
        /// </summary>
        private void UpdateTargetInFront(VMEntity active)
        {
            var context = Master.vm.Context;
            var sim = new Vector2(active.VisualPosition.X, active.VisualPosition.Y);
            var forward = PadMath.Forward(active.RadianDirection);
            var room = context.GetObjectRoom(active);
            PickTarget(active, active.Position.Level, 1f, (entity, x1, y1, x2, y2) =>
            {
                if (entity == active || context.GetObjectRoom(entity) != room) return float.PositiveInfinity;
                return PadMath.FrontScore(sim, forward, x1, y1, x2, y2, DirectReach, DirectMaxAngle, DirectAnglePenalty);
            });
        }

        /// <summary>
        /// Picks the interactable object or person with the lowest score on <paramref name="level"/>. The score
        /// function gets the entity and its footprint (1/16 tiles) and returns PositiveInfinity to skip it; people,
        /// things on surfaces and the current target get small bonuses, scaled by <paramref name="bias"/>.
        /// </summary>
        private void PickTarget(VMEntity active, sbyte level, float bias, Func<VMEntity, int, int, int, int, float> scoreOf)
        {
            var vm = Master.vm;
            VMEntity best = null;
            var bestScore = float.MaxValue;

            foreach (var entity in vm.Entities)
            {
                if (entity == Master.GotoObject || entity.Dead) continue;
                var pos = entity.Position;
                if (pos == LotTilePos.OUT_OF_WORLD || pos.Level != level) continue;

                var footprint = entity.Footprint;
                var score = (footprint != null)
                    ? scoreOf(entity, footprint.x1, footprint.y1, footprint.x2, footprint.y2)
                    : scoreOf(entity, pos.x, pos.y, pos.x, pos.y);
                if (float.IsPositiveInfinity(score)) continue;

                var leader = entity.MultitileGroup.GetInteractionGroupLeader(entity);
                if ((leader.TreeTable?.Interactions.Length ?? 0) == 0 && (entity.TreeTable?.Interactions.Length ?? 0) == 0) continue;

                if (entity is VMAvatar) score += ((entity == active) ? 0.3f : -0.15f) * bias; //people over furniture, but not yourself
                if (entity.Container != null) score -= 0.1f * bias; //things sitting on surfaces over the surface
                if (entity == TargetPart) score -= 0.15f * bias; //hysteresis, so the target doesn't flicker between neighbours
                if (score < bestScore)
                {
                    bestScore = score;
                    best = entity;
                }
            }

            SetTarget(active, best);
        }

        /// <summary>Targets <paramref name="best"/> (an entity or one of its parts), checking its menu once per target.</summary>
        private void SetTarget(VMEntity active, VMEntity best)
        {
            if (best != TargetPart || active != TargetCheckedFor)
            {
                TargetPart = best;
                Target = best?.MultitileGroup.GetInteractionGroupLeader(best);
                //checked once per target, like mouse hover. Building a menu runs every interaction's check tree,
                //so re-checking on a timer is costly, and any tree that throws would raise a popup each time.
                TargetHasInteractions = Target != null && Target.GetPieMenu(Master.vm, active, false, true).Count > 0;
                TargetCheckedFor = active;
            }
        }

        private const float StandUpStick = 0.5f; //how far the stick is pushed to stand a seated Sim up
        private const float StandUpHold = 0.25f; //seconds it's held there first, so a bump doesn't end a meal
        private const double StandUpRetry = 1.5; //seconds before asking again, if they didn't get up
        private const uint DestinationGUID = 0x000007C4; //"Go Here"'s object (VMNetGotoCmd)
        private float StandUpHeldFor;
        private double StandUpSentAt = double.MinValue;
        private bool DirectSeated;

        /// <summary>
        /// Direct control comes back only when the Sim stands (VMThread.EnsureDirectControlAction checks the posture), so
        /// a Sim left sitting (woken from a nap on a loveseat, then just sitting) couldn't be walked until free will got
        /// them up. Holding the stick then sends them a step that way with the floor's "Go Here", as clicking the floor
        /// does: routing first runs the seat's own stand-up (VMRoutingFrame, STAND_FUNC), and direct control returns once
        /// they're on their feet. Like a click, it interrupts what they're doing in the seat.
        /// </summary>
        private void StandUpForStick(VMEntity active, Vector2 stick, float dt, double now)
        {
            var avatar = active as VMAvatar;
            DirectSeated = avatar != null && avatar.GetPersonData(FSO.SimAntics.Model.VMPersonDataVariable.Posture) == 1;
            if (!DirectSeated || stick.Length() < StandUpStick)
            {
                StandUpHeldFor = 0;
                return;
            }
            StandUpHeldFor += dt;
            if (StandUpHeldFor < StandUpHold || now - StandUpSentAt < StandUpRetry) return;
            var queue = avatar.Thread?.Queue;
            if (queue == null || queue.Any(x => x.Callee?.Object?.OBJ?.GUID == DestinationGUID)) return; //already on their way
            var goTo = Master.GotoObject;
            if (goTo == null) return;

            var tile = FreeTileToward(avatar, World.Transform(new Vector2(stick.X, -stick.Y)));
            if (tile == null) return;
            goTo.SetPosition(tile.Value, Direction.NORTH, Master.vm.Context);
            if (goTo.Position == LotTilePos.OUT_OF_WORLD) return;
            var entry = goTo.GetPieMenu(Master.vm, avatar, false, true).FirstOrDefault(); //"Go Here", as the floor's menu
            if (entry == null) return;
            Master.vm.SendCommand(new VMNetGotoCmd
            {
                Interaction = entry.ID,
                Param0 = entry.Param0,
                ActorUID = avatar.PersistID,
                x = tile.Value.x,
                y = tile.Value.y,
                level = tile.Value.Level
            });
            StandUpSentAt = now;
            StandUpHeldFor = 0;
        }

        /// <summary>The free tile next to the Sim closest to <paramref name="direction"/> (tiles), or null.</summary>
        private LotTilePos? FreeTileToward(VMAvatar avatar, Vector2 direction)
        {
            if (direction == Vector2.Zero) return null;
            direction.Normalize();
            var pos = avatar.Position;
            var context = Master.vm.Context;
            LotTilePos? best = null;
            var bestDot = float.MinValue;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var tile = LotTilePos.FromBigTile((short)(pos.TileX + dx), (short)(pos.TileY + dy), pos.Level);
                    if (context.IsOutOfBounds(tile) || context.SolidToAvatars(tile).Solid) continue;
                    var offset = new Vector2(dx, dy);
                    offset.Normalize();
                    var dot = Vector2.Dot(offset, direction);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        best = tile;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Direct control is an interaction (FreeSO's routine 9001): queuing an action ends it, the action runs, and then
        /// direct control is queued again. The stick only steers while its frame is running.
        /// </summary>
        private bool DirectSteering => DirectAvatar?.Thread?.Stack.LastOrDefault() is VMDirectControlFrame;

        /// <summary>
        /// Direct control, while the Sim carries out a queued action: what the action is with (the fridge they're walking
        /// to, the Sim they're talking to), or nothing. Picking what's in front instead made the marker flash from
        /// object to object as the Sim turned along its route.
        /// </summary>
        private VMEntity BusyTarget(VMEntity active)
        {
            var callee = active.Thread?.ActiveAction?.Callee;
            if (callee == null || callee == active || callee == Master.GotoObject || callee.Dead) return null;
            if (callee.Position == LotTilePos.OUT_OF_WORLD || callee.Position.Level != active.Position.Level) return null;
            return callee;
        }

        private Vector2 SelectorOrTargetTile()
        {
            return (TargetPart != null) ? new Vector2(TargetPart.VisualPosition.X, TargetPart.VisualPosition.Y) : Selector;
        }

        private void OpenMenu(UpdateState state)
        {
            if (Target != null && !TargetHasInteractions)
            {
                //the ring is already red for this; say why A did nothing
                HITVM.Get().PlaySoundEvent(UISounds.Error);
                ShowHint("No actions available.");
                return;
            }
            var anchor = MarkerTop ?? MarkerBase ?? new Vector2(ScreenWidth, ScreenHeight) / 2;
            //keep the whole pie (about 300px wide and 250px tall with captions) on screen.
            var menuPos = PadMath.ClampAnchor(anchor, ScreenWidth, ScreenHeight, 170, 130);
            Master.OpenPieMenu(Target, Selector, menuPos, state, fromPad: true);
        }

        #endregion

        #region Drawing

        /// <param name="floorRing">Show a ring at the selector when nothing is targeted (cursor mode).</param>
        private void UpdateMarker(UpdateState state, float dt, bool floorRing)
        {
            AnimTime += dt;
            var level = World.State.Level;
            if (TargetPart == null)
            {
                if (floorRing)
                {
                    SetFloorRing(Selector, level, EmptyRingRadius, EmptyRingRadius);
                    RingBand = 3f;
                }
                return;
            }

            //an oval on the floor round the target
            var area = TargetArea(Target ?? TargetPart, TargetPart);
            var entityLevel = TargetPart.Position.Level;
            SetFloorRing(area.centre, entityLevel, area.rx, area.ry);
            RingBand = 4.5f;

            //the beam rises straight up from the ring's centre to the top of the target (raised for things on
            //counters and shelves; terrain is already in the projection, so it isn't counted again)
            var height = (TargetPart is VMAvatar) ? 2.1f : RaisedBy(TargetPart) + 1.2f;
            MarkerTop = TryProject(area.centre, entityLevel, height, out var top) ? top : (Vector2?)null;
            if (MarkerBase == null) MarkerTop = null;

            if (Target is VMAvatar avatar && MarkerTop != null && !UIPadOverlay.ToastShowing)
            {
                var tip = state.UIState.TooltipProperties;
                tip.Show = true;
                tip.Color = Color.Black;
                tip.Opacity = 1;
                tip.Position = MarkerTop.Value * FSOEnvironment.DPIScaleFactor;
                tip.UpdateDead = false;
                state.UIState.Tooltip = avatar.ToString();
            }
        }

        private const float EmptyRingRadius = 0.3f; //tiles, on empty floor
        private const float PersonRingRadius = 0.38f;

        /// <summary>
        /// The floor oval round a target. A person gets a small circle. An object gets an oval hugging its footprint:
        /// the whole object if it's at most 2x2 tiles (a sofa, a bed), otherwise just the tile being pointed at
        /// (stairs, railings and long counters would get rings across the room).
        /// </summary>
        private (Vector2 centre, float rx, float ry) TargetArea(VMEntity target, VMEntity part)
        {
            if (target is VMAvatar || part is VMAvatar)
            {
                var v = part.VisualPosition;
                return (new Vector2(v.X, v.Y), PersonRingRadius, PersonRingRadius);
            }
            var group = FootprintBounds(target.MultitileGroup.Objects);
            var bounds = (group != null && group.Value.Width <= 32 && group.Value.Height <= 32) ? group : FootprintBounds(new[] { part });
            if (bounds == null)
            {
                var v = part.VisualPosition;
                return (new Vector2(v.X, v.Y), PersonRingRadius, PersonRingRadius);
            }
            var b = bounds.Value;
            return PadMath.FootprintEllipse(b.Left, b.Top, b.Right, b.Bottom, 1.05f, 0.05f, PersonRingRadius, 1.2f);
        }

        /// <summary>Combined footprints (1/16 tiles) of objects on the lot; a tile for one without a footprint (e.g. on a wall).</summary>
        private static Rectangle? FootprintBounds(System.Collections.Generic.IEnumerable<VMEntity> parts)
        {
            int x1 = int.MaxValue, y1 = int.MaxValue, x2 = int.MinValue, y2 = int.MinValue;
            foreach (var part in parts)
            {
                var pos = part.Position;
                if (pos == LotTilePos.OUT_OF_WORLD) continue;
                var fp = part.Footprint;
                if (fp != null && fp.x2 > fp.x1 && fp.y2 > fp.y1)
                {
                    x1 = Math.Min(x1, fp.x1); y1 = Math.Min(y1, fp.y1);
                    x2 = Math.Max(x2, fp.x2); y2 = Math.Max(y2, fp.y2);
                }
                else
                {
                    x1 = Math.Min(x1, pos.TileX * 16); y1 = Math.Min(y1, pos.TileY * 16);
                    x2 = Math.Max(x2, pos.TileX * 16 + 16); y2 = Math.Max(y2, pos.TileY * 16 + 16);
                }
            }
            if (x1 > x2) return null;
            return new Rectangle(x1, y1, x2 - x1, y2 - y1);
        }

        /// <summary>How far an object sits above the floor because it's on something (a counter, a shelf).</summary>
        private static float RaisedBy(VMEntity entity)
        {
            var root = entity;
            while (root.Container != null) root = root.Container;
            return (root == entity) ? 0 : Math.Max(0, entity.VisualPosition.Z - root.VisualPosition.Z);
        }

        /// <summary>
        /// Projects a point at a height above a level's floor to UI space. False if it can't be seen properly: in 3D,
        /// behind the camera.
        /// </summary>
        private bool TryProject(Vector2 tile, sbyte level, float height, out Vector2 ui)
        {
            if (World.State.Cameras.ActiveType != CameraControllerType._2D)
            {
                var p = World.ProjectTileToScreenWithDepth(tile, level, height);
                ui = ToUI(new Vector2(p.X, p.Y));
                return p.Z >= 0 && p.Z <= 1 && !float.IsNaN(p.X) && !float.IsNaN(p.Y);
            }
            ui = ToUI(World.ProjectTileToScreen(tile, level, height));
            return true;
        }

        /// <summary>Projects the floor oval: its centre (<see cref="MarkerBase"/>) and points round it.</summary>
        private void SetFloorRing(Vector2 tile, sbyte level, float rx, float ry)
        {
            MarkerBase = TryProject(tile, level, 0, out var centre) ? centre : (Vector2?)null;
            for (int i = 0; i < RingSegments; i++)
            {
                var t = i * Math.PI * 2 / RingSegments;
                var point = tile + new Vector2(rx * (float)Math.Cos(t), ry * (float)Math.Sin(t));
                RingPoints[i] = TryProject(point, level, 0, out var ui) ? ui : new Vector2(float.NaN);
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (ModeMenuActive) ModeMenuNav.Draw(batch);
            if (CategoryMenuActive) CategoryMenuNav.Draw(batch);
            if (BuildCursor != null)
            {
                var cursorRing = PadGraphics.GetRing(GameFacade.GraphicsDevice);
                var cursorPulse = 0.75f + 0.25f * (float)Math.Sin(AnimTime * 5);
                DrawLocalTexture(batch, cursorRing, null, DrawPoint(BuildCursor.Value) - new Vector2(BuildCursorSize / 2),
                    new Vector2(BuildCursorSize / cursorRing.Width), Color.White * cursorPulse);
            }
            if (MarkerBase == null) return;
            var gd = GameFacade.GraphicsDevice;
            var pulse = 0.85f + 0.15f * (float)Math.Sin(AnimTime * 5);
            Color color;
            if (Target == null) color = Color.White;
            else color = TargetHasInteractions ? PadDraw.Accent : new Color(255, 96, 86);

            //the ring on the floor: a line through the projected points, the same thickness at any zoom or angle
            var line = PadGraphics.GetLine(gd);
            var band = RingBand * (1f + 0.12f * (float)Math.Sin(AnimTime * 4));
            DrawRing(batch, line, band * 3.2f, color * (0.22f * pulse), false); //glow
            DrawRing(batch, line, band + 3f, Color.Black * 0.45f, false); //dark rim, for light floors
            DrawRing(batch, line, band, color * pulse, true);

            if (MarkerTop != null)
            {
                //a soft beam from the ring up to the target, like a spotlight on it
                var beam = PadGraphics.GetBeam(gd);
                var up = MarkerTop.Value - MarkerBase.Value;
                var length = up.Length();
                var reach = float.IsNaN(RingPoints[0].X) ? 20f : Vector2.Distance(MarkerBase.Value, RingPoints[0]);
                if (length > 4)
                {
                    var width = Math.Max(10f, Math.Min(36f, reach * 0.5f));
                    var beamAngle = (float)Math.Atan2(up.X, -up.Y);
                    DrawLocalTexture(batch, beam, null, DrawPoint(MarkerBase.Value), new Vector2(width / beam.Width, length / beam.Height),
                        color * 0.65f, beamAngle, new Vector2(beam.Width / 2f, beam.Height));
                }

                //the Simitone note, bobbing over the target
                var note = PadGraphics.GetNote(gd);
                var noteScale = 42f / note.Height;
                var bob = (float)Math.Sin(AnimTime * 3) * 4;
                DrawLocalTexture(batch, note, null, DrawPoint(MarkerTop.Value) - new Vector2(note.Width * noteScale / 2f, note.Height * noteScale - bob),
                    new Vector2(noteScale), color);
            }
        }

        /// <summary>
        /// Draws the floor ring as segments between its projected points, skipping any it can't see (behind the
        /// camera, or so far off screen the projection is meaningless).
        /// </summary>
        /// <param name="overlap">Lengthen segments a little so an opaque line has no gaps at the joins.</param>
        private void DrawRing(UISpriteBatch batch, Microsoft.Xna.Framework.Graphics.Texture2D line, float thickness, Color color, bool overlap)
        {
            var limit = new Rectangle((int)(-ScreenWidth), (int)(-ScreenHeight), (int)(ScreenWidth * 3), (int)(ScreenHeight * 3));
            for (int i = 0; i < RingSegments; i++)
            {
                var a = RingPoints[i];
                var b = RingPoints[(i + 1) % RingSegments];
                if (float.IsNaN(a.X) || float.IsNaN(b.X)) continue;
                if (!limit.Contains(a.ToPoint()) || !limit.Contains(b.ToPoint())) continue;
                var d = b - a;
                var length = d.Length();
                if (length < 0.01f) continue;
                var dir = d / length;
                var extend = overlap ? thickness * 0.3f : 0f;
                var start = a - dir * extend;
                DrawLocalTexture(batch, line, null, DrawPoint(start), new Vector2((length + extend * 2) / line.Width, thickness / line.Height),
                    color, (float)Math.Atan2(d.Y, d.X), new Vector2(0, line.Height / 2f));
            }
        }

        #endregion

        #region Coordinates

        private float ScreenWidth => GameFacade.Screens.CurrentUIScreen.ScreenWidth;
        private float ScreenHeight => GameFacade.Screens.CurrentUIScreen.ScreenHeight;

        /// <summary>
        /// Screen pixels to UI units. The UI is scaled up by DPIScaleFactor (1 on desktops, about 2 on phones and the
        /// Thor); the pad's speeds and on-screen distances were tuned on desktops, in pixels, and keep that feel.
        /// </summary>
        private static float PxToUI => 1f / FSOEnvironment.DPIScaleFactor;

        /// <summary>
        /// A point in UI units to this element's drawing space. (GlobalPoint takes screen pixels: the UI screen is
        /// scaled by DPIScaleFactor.)
        /// </summary>
        private Vector2 DrawPoint(Vector2 ui)
        {
            return GlobalPoint(ui * FSOEnvironment.DPIScaleFactor);
        }

        /// <summary>
        /// World screen space (what EstTileAtPosWithScroll takes: screen pixels, in the world's image) to UI space,
        /// undoing the 2D smooth-zoom scale and the UI scale (DPIScaleFactor: 1 on desktops, about 2 on phones).
        /// </summary>
        private Vector2 ToUI(Vector2 worldScreen)
        {
            var dpi = FSOEnvironment.DPIScaleFactor;
            var middle = new Vector2(ScreenWidth, ScreenHeight) * (dpi / 2);
            return ((worldScreen - middle) * World.BackbufferScale + middle) / dpi;
        }

        private Vector2 FromUI(Vector2 ui)
        {
            var dpi = FSOEnvironment.DPIScaleFactor;
            var middle = new Vector2(ScreenWidth, ScreenHeight) * (dpi / 2);
            return (ui * dpi - middle) / World.BackbufferScale + middle;
        }

        #endregion
    }
}
