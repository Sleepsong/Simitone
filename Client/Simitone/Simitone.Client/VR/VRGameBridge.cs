using System;
using System.Linq;
using FSO.Client;
using FSO.Client.UI.Model;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.HIT;
using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Engine;
using FSO.SimAntics.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Model;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using Simitone.VR;

namespace Simitone.Client.VR
{
    /// <summary>
    /// Connects VR to the running game (one per <see cref="TS1GameScreen"/>, updated every game update):
    ///
    ///  - <b>Play as your Sim</b>: direct control is switched on for the selected Sim while you're in first person,
    ///    and the left stick walks them relative to where you look, running near full tilt. This is FreeSO's direct
    ///    control (VMDirectControlFrame, TS1 support from the gamepad work on Sleepsong/FreeSO), driven the same way
    ///    the controller branch's UILotControlPadHelper drives it. Free will needs no special handling: the direct
    ///    control action is re-queued every tick, so the Sim's queue is never empty and TS1's idle-loop free will
    ///    doesn't fire for them (confirmed on hardware for the gamepad).
    ///  - <b>Comfort</b>: the camera decides when to step out of the Sim's head and follow (the SimActionView
    ///    tiers); the bridge tells it whether the player can steer the Sim right now and which room a tile is in.
    ///  - <b>Movement arc</b> (left trigger): where it lands, the Sim walks to with the floor's "Go Here".
    ///  - <b>Actions</b>: the selected Sim's queue goes to the left wrist display (and the running action to a bubble
    ///    over their head while you're stepped out); pointing at one and pulling the trigger cancels it, as clicking it
    ///    in the game's queue does. Holding the stick while your Sim is busy cancels what they're doing so you can
    ///    walk them again (<see cref="VRSettings.StickCancelsActions"/>).
    ///  - <b>Messages</b> that need no answer wait on the watch in first person (see <see cref="SimitoneVR.PostMessage"/>).
    ///  - <b>Pointing</b>: the right controller picks objects and Sims; the trigger opens their real pie menu (or "Go
    ///    Here" on the floor), which shows on the floating UI panel together with any dialog.
    ///  - <b>Buy and build mode</b> in first person: the catalogue sits on a desk-like panel, the pointer picks objects
    ///    up from a distance and an arc places them (see <see cref="VRCameraController"/>'s build cursor). The game's
    ///    own object holder and build tools do the work, driven by a virtual mouse.
    ///  - <b>Flat screen</b> for options and the travel map (and buy/build if VRBuildMode is off), returning to
    ///    first person afterwards.
    ///  - Family switching, pause and speed buttons, and the data for the wrist displays.
    /// </summary>
    public class VRGameBridge
    {
        private const uint GotoGuid = 0x000007C4;     // the floor's "Go Here" object
        private const float RunOn = 0.9f, RunOff = 0.75f;
        private const float StandUpStick = 0.5f, StandUpHold = 0.25f, StandUpRetry = 1.5f;
        private const float TakeOverStick = 0.5f, TakeOverHold = 0.6f, TakeOverRetry = 2f;

        private readonly TS1GameScreen Screen;
        private VMAvatar DirectAvatar;
        private int DirectInputID;
        private bool DirectRunning;
        private float StandUpHeldFor;
        private double StandUpSentAt = -100;
        private double Now;
        private float UpdateDelta = 1 / 60f;
        private float TakeOverHeldFor;
        private double TakeOverSentAt = -100;
        private bool RunLatched;            // clicked the stick: run until it's let go
        private int SpeedBeforeSteering;    // the game speed we dropped from to walk, or 0
        private double LastSteerInputAt = -100;

        // measured speeds, written to vr.log (see MeasureSpeed)
        private enum MoveKind { None, Walk, Run, Routed }
        private MoveKind SegmentKind;
        private float SegmentTime, SegmentDistance, SegmentStill;
        private Vector2 LastSimTile;
        private VMAvatar LastMeasured;
        private readonly FSO.SimAntics.VMEntity[] IconOwners = new FSO.SimAntics.VMEntity[VRHudData.MaxQueue];

        private bool LeftForFlatScreen;
        private bool WasBuyBuild;
        /// <summary>The game's catalogue and open pie menu, for the VR tablet and pie menu.</summary>
        private VRGameCatalog Catalog;
        private VRGamePie Pie;
        private readonly VRAudio Audio = new VRAudio();
        private readonly VRDollhouse Dollhouse = new VRDollhouse();
        private UIObjectSelection LastHolding;
        private bool RotateLatched;
        private bool WasImmersive;
        private int WallsModeBefore = 1;
        private bool FrontendVisibleBefore = true;
        private int SpeedBeforePause = 1;
        private readonly VRHudData Hud = new VRHudData();
        private readonly Func<Vector2, sbyte, int> RoomAtFunc;
        private static readonly VMMotive[] HudMotives = { VMMotive.Hunger, VMMotive.Comfort, VMMotive.Hygiene, VMMotive.Bladder,
            VMMotive.Energy, VMMotive.Fun, VMMotive.Social, VMMotive.Room };
        private readonly Func<Vector2, sbyte, (Vector2 from, Vector2 to, sbyte level)?> StairsAtFunc;
        private readonly Func<Vector2, sbyte, float, bool> ObjectBlocksFunc;

        public VRGameBridge(TS1GameScreen screen)
        {
            Screen = screen;
            RoomAtFunc = RoomAt;
            StairsAtFunc = StairsAt;
            ObjectBlocksFunc = ObjectBlocks;
            SimitoneVR.FocusProvider = FocusTile;
            SimitoneVR.HudProvider = () => Hud;
        }

        private (Vector2 tile, sbyte level)? FocusTile()
        {
            var sim = Screen.LotControl?.ActiveEntity;
            if (sim == null || sim.Position == LotTilePos.OUT_OF_WORLD) return null;
            return (new Vector2(sim.Position.x / 16f, sim.Position.y / 16f), sim.Position.Level);
        }

        /// <summary>Called when the lot is torn down: the VM and its Sims are gone.</summary>
        public void Reset()
        {
            DirectAvatar = null;
            Pie = null;
            Audio.ReleaseAll();
            LastMeasured = null;
            SegmentKind = MoveKind.None;
            SpeedBeforeSteering = 0;
            WasImmersive = false;
            LeftForFlatScreen = false;
            Hud.Valid = false;
            Hud.QueueCount = 0;
            for (int i = 0; i < IconOwners.Length; i++) IconOwners[i] = null;
            SimitoneVR.ClearMessages();
        }

        public void Update(UpdateState state)
        {
            var now = state.Time?.TotalGameTime.TotalSeconds ?? Now + 1 / 60.0;
            UpdateDelta = (float)MathHelper.Clamp((float)(now - Now), 0.001f, 0.1f);
            Now = now;
            var world = Screen.VRWorld;
            var lc = Screen.LotControl;
            var vm = Screen.vm;
            var input = SimitoneVR.Runtime?.Input;
            if (world == null || lc == null || vm == null || world.VRCamera == null)
            {
                DirectAvatar = null;
                Hud.Valid = false;
                SimitoneVR.UIPanelWanted = false;
                SimitoneVR.PieMenuOpen = false;
                SimitoneVR.OpenPie = null;
                SimitoneVR.PieSource = null;
                SimitoneVR.CatalogSource = null;
                Audio.ReleaseAll();
                return;
            }
            var camera = world.VRCamera;
            var immersive = world.Immersive;

            UpdateHud(vm, lc);
            HandleButtons(vm, lc, camera, immersive);
            SimitoneVR.UpdateMessages(immersive);
            if (SimitoneVR.ConsumeQueueCancel(out var cancelUID) && lc.ActiveEntity is VMAvatar queueOwner)
                CancelAction(vm, queueOwner, cancelUID);

            // ---- buy/build happen in first person (VRBuildMode); options and the travel map are flat: step out of
            // first person for those, and come back after.
            var mainMode = Screen.Frontend?.MainPanel?.Mode ?? UIMainPanelMode.LIVE;
            var buyBuild = !lc.LiveMode && (mainMode == UIMainPanelMode.BUY || mainMode == UIMainPanelMode.BUILD);
            // (in the dollhouse buy and build are always in VR: it's where your hands move furniture)
            var vrBuild = buyBuild && (SimitoneVR.Settings.VRBuildMode || SimitoneVR.PlayMode == VRPlayMode.Dollhouse);
            // the vacation / downtown picker hides the lot control, but it floats on the panel in first person
            var picker = lc.NeighbourhoodPickerOpen;
            var wantFlat = (!lc.Visible && !picker) || (!lc.LiveMode && !vrBuild);
            if (vrBuild && !WasBuyBuild && !immersive && world.Visible && SimitoneVR.SessionRunning)
            {
                // switched to buy/build from the flat screen: build in first person
                world.ToggleImmersive(resume: true);
                immersive = world.Immersive;
            }
            WasBuyBuild = buyBuild;
            if (immersive && wantFlat)
            {
                LeftForFlatScreen = true;
                world.ExitImmersive();
                immersive = false;
            }
            else if (!immersive && !wantFlat && LeftForFlatScreen && world.Visible)
            {
                LeftForFlatScreen = false;
                world.ToggleImmersive(resume: true);
                immersive = world.Immersive;
            }

            UpdateImmersiveUI(lc, immersive, vrBuild);
            // ---- the dollhouse: you're a giant over the lot, and your hands move furniture, Sims and the model itself
            camera.Dollhouse = immersive && SimitoneVR.PlayMode == VRPlayMode.Dollhouse;
            var inDollhouse = immersive && camera.InDollhouse;
            SimitoneVR.Dollhouse = inDollhouse;
            Dollhouse.Update(Screen, world, camera, input, inDollhouse, UpdateDelta);
            if (Dollhouse.ConsumeTap(out var tapped)) OpenPieFor(lc, camera, vm.GetObjectById(tapped), state);
            if (camera.ConsumeLeanIn() && inDollhouse)
            {
                // you put your head into a room: in you go, as the observer
                SimitoneVR.PlayMode = VRPlayMode.Observer;
                HITVM.Get()?.PlaySoundEvent(UISounds.Whoosh);
                SimitoneVR.Log("dollhouse: leaned into a room");
            }
            UpdateDollhouseCutaway(vm, world, camera, inDollhouse);
            SimitoneVR.DollhouseAutoBuy = Dollhouse.AutoBuyMode;
            UpdateBuildMode(camera, lc, immersive && vrBuild);

            // ---- play as your Sim
            VMAvatar wanted = null;
            if (immersive && SimitoneVR.PlayMode == VRPlayMode.Sim && lc.LiveMode && lc.ActiveEntity is VMAvatar sim
                && !sim.Dead && sim.Position != LotTilePos.OUT_OF_WORLD && sim.PersistID == vm.MyUID)
                wanted = sim;
            SyncDirectControl(wanted);
            camera.SimAvatar = DirectAvatar?.WorldUI as AvatarComponent;
            camera.SimSteering = DirectAvatar != null && Steering;
            camera.SimStandingUp = DirectAvatar != null && Now - StandUpSentAt < 3.0;
            camera.SimInPortal = DirectAvatar != null && !Steering && InPortal;
            camera.SimRouting = DirectAvatar != null && IsRouting(DirectAvatar);
            var running = Hud.QueueCount > 0 ? Hud.Queue[0] : null;
            camera.BubbleIcon = DirectAvatar != null ? running?.Icon : null;
            camera.BubbleCancelled = running?.Cancelled ?? false;
            camera.BubbleUID = running?.UID ?? 0;
            camera.RoomAt = RoomAtFunc;
            camera.StairsAt = StairsAtFunc;
            camera.ObjectBlocks = ObjectBlocksFunc;
            if (DirectAvatar != null) DriveSim(camera, input, vm, lc, state);
            RestoreSpeed(vm);
            MeasureSpeed(DirectAvatar);
            if (camera.ConsumeMoveArc(out var arcTile, out var arcLevel) && DirectAvatar != null)
            {
                // the movement arc: walk there, as clicking the floor and choosing "Go Here" does
                var tile = LotTilePos.FromBigTile((short)Math.Floor(arcTile.X), (short)Math.Floor(arcTile.Y), arcLevel);
                CancelWalks(vm, DirectAvatar); // a new arc replaces the walk you sent before, rather than queueing after it
                if (SendGoHere(vm, lc, DirectAvatar, tile)) HITVM.Get()?.PlaySoundEvent(UISounds.QueueAdd);
                else SimitoneVR.Runtime?.Haptic(false, 0.6f, 0.08f);
            }

            // ---- pointing at things
            UpdatePointer(world, camera, vm, lc, state, immersive);
            // ---- sounds come from where they are around your head
            Audio.Update(vm, camera, immersive && SimitoneVR.Settings.Sound3D);
            // (published together, after pointing may have opened a pie menu, so the next frame shows it as one)
            SimitoneVR.PickerOpen = immersive && picker;
            SimitoneVR.PieMenuOpen = lc.PieMenuOpen;
            SimitoneVR.OpenPie = lc.OpenPie;
            if (lc.OpenPie == null) Pie = null;
            else if (Pie?.Menu != lc.OpenPie) Pie = new VRGamePie(lc.OpenPie, lc);
            SimitoneVR.PieSource = Pie;
            Catalog ??= new VRGameCatalog(Screen);
            SimitoneVR.CatalogSource = Catalog;
            // (the panel shows the game's UI: pie menus only when they aren't drawn for VR, the catalogue only on the desk)
            SimitoneVR.UIPanelWanted = immersive && ((lc.PieMenuOpen && !SimitoneVR.Settings.VRPieMenus) || SimitoneVR.DialogOpen()
                || (SimitoneVR.BuildMode && !SimitoneVR.CatalogInHand && !SimitoneVR.DollhouseAutoBuy) || picker);
        }

        // ------------------------------------------------------------------ direct control

        private void SyncDirectControl(VMAvatar wanted)
        {
            if (wanted == DirectAvatar) return;
            // A Sim we no longer control was already let go by the control change (VMNetChangeControlCmd) and can't be
            // addressed any more (its PersistID is 0 now).
            if (DirectAvatar != null && !DirectAvatar.Dead && DirectAvatar.PersistID == Screen.vm.MyUID)
                SendToggle(DirectAvatar, false);
            DirectAvatar = wanted;
            DirectRunning = false;
            StandUpHeldFor = 0;
            if (wanted != null) SendToggle(wanted, true);
        }

        private void SendToggle(VMAvatar avatar, bool enable)
        {
            // Addressed to this Sim rather than "the selected Sim": the two differ while a control change is queued.
            Screen.Driver.SendCommand(new VMNetDirectControlToggleCommand { ActorUID = avatar.PersistID, Enable = enable });
        }

        /// <summary>Direct control only steers while its frame runs; a queued interaction replaces it until done.</summary>
        private bool Steering => DirectAvatar?.Thread?.Stack.LastOrDefault() is VMDirectControlFrame;

        /// <summary>
        /// You walked into a door or stairs while steering: its routine runs on top of the direct control frame, which
        /// takes over again when it's done.
        /// </summary>
        private bool InPortal => DirectAvatar?.Thread?.Stack.Any(x => x is VMDirectControlFrame) == true;

        /// <summary>Walking somewhere on their own, or about to (another action, or a walk, is queued after this one).</summary>
        private bool IsRouting(VMAvatar avatar)
        {
            var thread = avatar?.Thread;
            if (thread == null) return false;
            if (thread.Stack.Any(x => x is VMRoutingFrame)) return true;
            var shown = 0;
            foreach (var action in thread.Queue) if (action.Mode != VMQueueMode.Idle && ++shown > 1) return true;
            return false;
        }

        private void SendDirectInput(VMDirectControlInput input)
        {
            if (DirectAvatar?.Thread?.Stack.LastOrDefault() is VMDirectControlFrame frame) frame.SendUserControls(input);
        }

        private void DriveSim(VRCameraController camera, VRInput input, VM vm, Simitone.Client.UI.Panels.UILotControl lc, UpdateState state)
        {
            var move = camera.MoveIntent;
            // The body turns to where you look, as the gamepad's FISH EYE first person does.
            var look = RadiansToDirection(-camera.HeadWorldYaw);

            if (!Steering)
            {
                DirectRunning = false;
                if (InPortal) return; // going through a door: you have them back in a moment
                if (!SimitoneVR.Settings.StickCancelsActions || !TakeOver(vm, move)) StandUpForStick(vm, lc, move, camera);
                return;
            }
            StandUpHeldFor = 0;
            TakeOverHeldFor = 0;

            if (move == Vector2.Zero)
            {
                DirectRunning = RunLatched = false;
                SendDirectInput(new VMDirectControlInput { ID = DirectInputID++, LookDirectionInt = look });
                return;
            }

            // Walking at speed 2 or 3 would be 3 or 10 times as fast: drop to normal speed (restored in RestoreSpeed).
            LastSteerInputAt = Now;
            if (SimitoneVR.Settings.NormalSpeedWhenSteering)
            {
                var speed = CurrentSpeed(vm);
                if (speed > 1)
                {
                    SpeedBeforeSteering = speed;
                    Screen.ChangeSpeedTo(1);
                    SimitoneVR.Log($"walking: game speed {speed} -> 1");
                }
            }

            // Walk (about 1.7 m/s); run (about 5 m/s) after clicking the stick, until it's let go, or at full tilt if
            // RunAtFullTilt is on.
            var tileMove = StickToTiles(move, camera.MoveYaw);
            if (input != null && input.Active && input.Sprint.Down) RunLatched = true;
            var fullTilt = SimitoneVR.Settings.RunAtFullTilt && (DirectRunning ? move.Length() > RunOff : move.Length() > RunOn);
            DirectRunning = RunLatched || fullTilt;
            SendDirectInput(new VMDirectControlInput
            {
                ID = DirectInputID++,
                Direction = DirectionToShort(tileMove),
                InputIntensity = Math.Max(1, (int)(Math.Min(1f, move.Length()) * 100)),
                LookDirectionInt = look,
                Sprint = DirectRunning
            });
        }

        /// <summary>
        /// Two seconds after you stop walking your Sim, the game goes back to the speed it had before walking slowed it
        /// down, unless you've changed the speed yourself meanwhile.
        /// </summary>
        private void RestoreSpeed(VM vm)
        {
            if (SpeedBeforeSteering == 0 || Now - LastSteerInputAt < 2.0) return;
            var restore = SpeedBeforeSteering;
            SpeedBeforeSteering = 0;
            if (CurrentSpeed(vm) != 1) return; // paused, or changed by hand: leave it
            Screen.ChangeSpeedTo(restore);
            SimitoneVR.Log($"walking done: game speed 1 -> {restore}");
        }

        /// <summary>
        /// Measures how fast your Sim really moves in VR (metres per second, at the current WorldScale): while you walk
        /// or run them, and while they walk somewhere on their own. Each stretch of 1.5 s or more is written to vr.log.
        /// </summary>
        private void MeasureSpeed(VMAvatar avatar)
        {
            if (avatar != LastMeasured)
            {
                EndSegment();
                LastMeasured = avatar;
                if (avatar != null) LastSimTile = new Vector2(avatar.Position.x / 16f, avatar.Position.y / 16f);
                return;
            }
            if (avatar == null) return;
            var tile = new Vector2(avatar.Position.x / 16f, avatar.Position.y / 16f);
            var step = Vector2.Distance(tile, LastSimTile);
            LastSimTile = tile;
            if (step > 2f) { EndSegment(); return; } // a jump (stairs, a snap), not walking

            var kind = Steering ? (DirectRunning ? MoveKind.Run : MoveKind.Walk) : IsRouting(avatar) ? MoveKind.Routed : MoveKind.None;
            if (kind != SegmentKind)
            {
                EndSegment();
                SegmentKind = kind;
            }
            if (kind == MoveKind.None) return;
            SegmentTime += UpdateDelta;
            if (step > 0)
            {
                SegmentDistance += step * VRCameraController.TileUnits / SimitoneVR.Settings.WorldScale;
                SegmentStill = 0;
            }
            else if ((SegmentStill += UpdateDelta) > 0.5f) EndSegment(); // stopped
        }

        private void EndSegment()
        {
            var moving = SegmentTime - SegmentStill;
            if (SegmentKind != MoveKind.None && moving >= 1.5f && SegmentDistance > 0.5f)
            {
                var vm = Screen.vm;
                SimitoneVR.Log($"speed: {SegmentKind.ToString().ToLowerInvariant()} {SegmentDistance / moving:0.00} m/s over "
                    + $"{moving:0.0} s ({SegmentDistance:0.0} m) at game speed {(vm != null ? CurrentSpeed(vm) : 0)}");
            }
            SegmentKind = MoveKind.None;
            SegmentTime = SegmentDistance = SegmentStill = 0;
        }

        /// <summary>
        /// Holding the stick while your Sim is busy cancels everything they're doing or have queued (they wind down as
        /// when you click their actions), so direct control comes back. Returns false when there's nothing to cancel.
        /// </summary>
        private bool TakeOver(VM vm, Vector2 move)
        {
            var avatar = DirectAvatar;
            if (move.Length() < TakeOverStick)
            {
                TakeOverHeldFor = 0;
                return HasCancellable(avatar);
            }
            if (!HasCancellable(avatar)) return false;
            TakeOverHeldFor += UpdateDelta;
            if (TakeOverHeldFor < TakeOverHold || Now - TakeOverSentAt < TakeOverRetry) return true;
            var queue = avatar.Thread.Queue;
            foreach (var i in ShownEntries(queue).ToList())
            {
                if (!IsCancelled(avatar, queue[i]))
                    vm.SendCommand(new VMNetInteractionCancelCmd { ActionUID = queue[i].UID, ActorUID = avatar.PersistID });
            }
            TakeOverSentAt = Now;
            TakeOverHeldFor = 0;
            HITVM.Get()?.PlaySoundEvent(UISounds.QueueDelete);
            SimitoneVR.Runtime?.Haptic(false, 0.5f, 0.08f);
            SimitoneVR.Log("stick: cancelled " + avatar.Name + "'s actions");
            return true;
        }

        private static bool HasCancellable(VMAvatar avatar)
        {
            var queue = avatar?.Thread?.Queue;
            if (queue == null) return false;
            foreach (var i in ShownEntries(queue)) if (!IsCancelled(avatar, queue[i])) return true;
            return false;
        }

        /// <summary>The queue entries the game's queue display shows, in order (UIInteractionQueue's rule).</summary>
        internal static System.Collections.Generic.IEnumerable<int> ShownEntries(System.Collections.Generic.List<VMQueuedAction> queue)
        {
            var skipParentIdle = false;
            for (int i = 0; i < queue.Count; i++)
            {
                var mode = queue[i].Mode;
                if (mode != VMQueueMode.Idle && (i == 0 || mode != VMQueueMode.ParentExit) && (!skipParentIdle || mode != VMQueueMode.ParentIdle))
                    yield return i;
                if (mode == VMQueueMode.ParentIdle) skipParentIdle = true;
            }
        }

        /// <summary>Already being cancelled (as the game's queue display marks it).</summary>
        internal static bool IsCancelled(VMAvatar avatar, VMQueuedAction action)
        {
            var priority = action == avatar.Thread.ActiveAction
                ? avatar.GetPersonData(VMPersonDataVariable.Priority)
                : action.Priority;
            return action.NotifyIdle && priority <= 0;
        }

        /// <summary>Cancels one of a Sim's actions by UID, as clicking it in the game's queue does.</summary>
        private static void CancelAction(VM vm, VMAvatar avatar, ushort uid)
        {
            var action = avatar.Thread?.Queue.FirstOrDefault(x => x.UID == uid);
            if (action == null || IsCancelled(avatar, action)) return;
            HITVM.Get()?.PlaySoundEvent(UISounds.QueueDelete);
            vm.SendCommand(new VMNetInteractionCancelCmd { ActionUID = uid, ActorUID = avatar.PersistID });
        }

        /// <summary>
        /// A seated Sim can't be walked until they stand. Holding the stick for a moment sends them a step that way
        /// with the floor's "Go Here", as clicking the floor does; the seat's own stand-up runs first.
        /// (Ported from the controller branch's UILotControlPadHelper.StandUpForStick.)
        /// </summary>
        private void StandUpForStick(VM vm, Simitone.Client.UI.Panels.UILotControl lc, Vector2 move, VRCameraController camera)
        {
            var avatar = DirectAvatar;
            var seated = avatar != null && avatar.GetPersonData(VMPersonDataVariable.Posture) == 1;
            if (!seated || move.Length() < StandUpStick)
            {
                StandUpHeldFor = 0;
                return;
            }
            StandUpHeldFor += SimitoneVR.FrameDelta;
            if (StandUpHeldFor < StandUpHold || Now - StandUpSentAt < StandUpRetry) return;
            var queue = avatar.Thread?.Queue;
            if (queue == null || queue.Any(x => x.Callee?.Object?.OBJ?.GUID == GotoGuid)) return; // already on their way

            var tile = FreeTileToward(vm, avatar, StickToTiles(move, camera.MoveYaw));
            if (tile == null) return;
            if (!SendGoHere(vm, lc, avatar, tile.Value)) return;
            StandUpSentAt = Now;
            StandUpHeldFor = 0;
        }

        /// <summary>Queues the floor's "Go Here" to a tile for the Sim, as clicking the floor does.</summary>
        private static bool SendGoHere(VM vm, Simitone.Client.UI.Panels.UILotControl lc, VMAvatar avatar, LotTilePos tile)
        {
            var goTo = lc.GotoObject;
            if (goTo == null || avatar == null) return false;
            goTo.SetPosition(tile, Direction.NORTH, vm.Context);
            if (goTo.Position == LotTilePos.OUT_OF_WORLD) return false;
            var entry = goTo.GetPieMenu(vm, avatar, false, true).FirstOrDefault();
            if (entry == null) return false;
            vm.SendCommand(new VMNetGotoCmd
            {
                Interaction = entry.ID,
                Param0 = entry.Param0,
                ActorUID = avatar.PersistID,
                x = tile.x,
                y = tile.y,
                level = tile.Level
            });
            return true;
        }

        /// <summary>Cancels the Sim's walks ("Go Here", from an earlier arc or a floor click), running or queued.</summary>
        private static void CancelWalks(VM vm, VMAvatar avatar)
        {
            var queue = avatar?.Thread?.Queue;
            if (queue == null) return;
            foreach (var action in queue.ToList())
            {
                if (action.Callee?.Object?.OBJ?.GUID == GotoGuid && !IsCancelled(avatar, action))
                    vm.SendCommand(new VMNetInteractionCancelCmd { ActionUID = action.UID, ActorUID = avatar.PersistID });
            }
        }

        private int RoomAt(Vector2 tile, sbyte level)
        {
            var context = Screen.vm?.Context;
            if (context == null) return 0;
            return context.GetRoomAt(LotTilePos.FromBigTile((short)Math.Floor(tile.X), (short)Math.Floor(tile.Y), level));
        }

        /// <summary>
        /// Stairs under a tile on a floor (for the observer, who has no Sim to climb them): a multitile object with
        /// portal parts on two floors, one part of it on this tile. Returns the centres of the portal part on this floor
        /// and the one on the other floor. Doors are portals too, but both their parts are on one floor.
        /// </summary>
        private (Vector2 from, Vector2 to, sbyte level)? StairsAt(Vector2 tile, sbyte level)
        {
            var vm = Screen.vm;
            var context = vm?.Context;
            if (context?.RoomInfo == null) return null;
            int room = RoomAt(tile, level);
            if (room <= 0 || room >= context.RoomInfo.Length || context.RoomInfo[room].Portals == null) return null;
            int tx = (int)Math.Floor(tile.X), ty = (int)Math.Floor(tile.Y);
            foreach (var portal in context.RoomInfo[room].Portals)
            {
                var here = vm.GetObjectById(portal.ObjectID);
                var group = here?.MultitileGroup;
                if (group == null || here.Position.Level != level) continue;
                VMEntity there = null;
                var onTile = false;
                foreach (var part in group.Objects)
                {
                    var pos = part.Position;
                    if (pos == LotTilePos.OUT_OF_WORLD) continue;
                    if (pos.Level != level)
                    {
                        if (part.Portal && there == null) there = part;
                    }
                    else if (pos.TileX == tx && pos.TileY == ty) onTile = true;
                }
                if (there == null || !onTile) continue;
                return (TileCentre(here.Position), TileCentre(there.Position), there.Position.Level);
            }
            return null;
        }

        private static Vector2 TileCentre(LotTilePos pos) => new Vector2(pos.TileX + 0.5f, pos.TileY + 0.5f);

        /// <summary>
        /// Whether furniture blocks a body of this radius (tiles) at a tile on a floor, as the game's direct control
        /// decides it for Sims: objects that don't let people through, except doors and stairs (portals).
        /// </summary>
        private bool ObjectBlocks(Vector2 tile, sbyte level, float radius)
        {
            var context = Screen.vm?.Context;
            if (context?.RoomInfo == null) return false;
            int room = RoomAt(tile, level);
            if (room <= 0 || room >= context.RoomInfo.Length) return false;
            var entities = context.RoomInfo[room].Entities;
            if (entities == null) return false;
            int cx = (int)(tile.X * 16), cy = (int)(tile.Y * 16), r = (int)Math.Ceiling(radius * 16);
            var body = new FSO.SimAntics.Model.Routing.VMObstacle(cx - r, cy - r, cx + r, cy + r);
            foreach (var obj in entities)
            {
                if (!(obj is VMGameObject) || obj.Position.Level != level) continue;
                var ft = obj.Footprint;
                if (ft == null || !ft.Intersects(body)) continue;
                var flags = (VMEntityFlags)obj.GetValue(VMStackObjectVariable.Flags);
                if ((flags & VMEntityFlags.DisallowPersonIntersection) == 0 && (flags & VMEntityFlags.AllowPersonIntersection) != 0) continue;
                if (IsPortalGroup(obj)) continue;
                return true;
            }
            return false;
        }

        private static bool IsPortalGroup(VMEntity obj)
        {
            if (obj.Portal) return true;
            var group = obj.MultitileGroup;
            if (group == null) return false;
            foreach (var part in group.Objects) if (part.Portal) return true;
            return false;
        }

        private static LotTilePos? FreeTileToward(VM vm, VMAvatar avatar, Vector2 direction)
        {
            if (direction == Vector2.Zero) return null;
            direction.Normalize();
            var pos = avatar.Position;
            var context = vm.Context;
            LotTilePos? best = null;
            var bestDot = float.MinValue;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var tile = LotTilePos.FromBigTile((short)(pos.TileX + dx), (short)(pos.TileY + dy), pos.Level);
                    if (context.IsOutOfBounds(tile) || context.SolidToAvatars(tile).Solid) continue;
                    var offset = Vector2.Normalize(new Vector2(dx, dy));
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

        /// <summary>Stick deflection to a tile-space direction, "forward" being <paramref name="worldYaw"/>.</summary>
        private static Vector2 StickToTiles(Vector2 stick, float worldYaw)
        {
            // World X = tile X, world Z = tile Y; yaw 0 looks down -Z.
            var forward = new Vector2(-(float)Math.Sin(worldYaw), -(float)Math.Cos(worldYaw));
            var right = new Vector2((float)Math.Cos(worldYaw), -(float)Math.Sin(worldYaw));
            return forward * stick.Y + right * stick.X;
        }

        /// <summary>
        /// Tile-space direction to VMDirectControlInput units: -32767..32767 covering -pi..pi, measured the way
        /// VMDirectControlFrame moves (x = sin, y = -cos). Same as PadMath.DirectionToShort on the controller branch.
        /// </summary>
        public static short DirectionToShort(Vector2 tileDirection) => RadiansToDirection(Math.Atan2(tileDirection.X, -tileDirection.Y));

        public static short RadiansToDirection(double radians)
        {
            var normalized = radians % (Math.PI * 2);
            if (normalized > Math.PI) normalized -= Math.PI * 2;
            if (normalized <= -Math.PI) normalized += Math.PI * 2;
            return (short)Math.Round(normalized / Math.PI * 32767);
        }

        // ------------------------------------------------------------------ pointing

        private void UpdatePointer(VRLotWorld world, VRCameraController camera, VM vm, Simitone.Client.UI.Panels.UILotControl lc,
            UpdateState state, bool immersive)
        {
            var select = SimitoneVR.ConsumeWorldSelect();
            var cancel = SimitoneVR.ConsumeCancel();
            if (!immersive || SimitoneVR.Runtime?.Input?.Active != true)
            {
                camera.PointerVisible = false;
                return;
            }
            if (SimitoneVR.OverlayHasPointer)
            {
                // the VR options menu or keyboard has the pointer
                var distance = SimitoneVR.OverlayPointerDistance;
                camera.PointerVisible = true;
                camera.PointerHasTarget = distance > 0;
                camera.PointerLength = (distance > 0 ? distance : 2f) * camera.UnitsPerMetre;
                return;
            }
            if (SimitoneVR.BuildMode)
            {
                if (cancel)
                {
                    var holder = lc.ObjectHolder;
                    if (holder?.Holding != null) holder.Cancel();
                    else lc.ReleaseCustomControl();
                }
                // The object holder / build tools pick and place through the virtual mouse; here we only show the
                // straight pointer (green over something) while nothing is held.
                if (SimitoneVR.PanelPointerOnPanel)
                {
                    camera.PointerVisible = true;
                    camera.PointerHasTarget = true;
                    camera.PointerLength = SimitoneVR.PanelPointerDistance * camera.UnitsPerMetre;
                }
                else if (!camera.PlacementMode)
                {
                    camera.PointerVisible = true;
                    var picked = world.PickPointed();
                    camera.PointerHasTarget = picked > 0 && vm.GetObjectById(picked) is VMGameObject;
                    camera.PointerLength = Math.Max(0.2f, Vector3.Distance(camera.PointerStart, camera.CursorTarget));
                }
                return;
            }

            if (cancel && lc.PieMenuOpen) lc.CancelPie();

            if (SimitoneVR.PanelShown)
            {
                // The panel owns the pointer; SimitoneVR reports how far away it is.
                camera.PointerVisible = SimitoneVR.PanelPointerDistance > 0;
                camera.PointerHasTarget = SimitoneVR.PanelPointerOnPanel;
                camera.PointerLength = SimitoneVR.PanelPointerDistance * camera.UnitsPerMetre;
                if (select && !SimitoneVR.PanelPointerOnPanel && lc.PieMenuOpen) lc.CancelPie(); // clicked beside the menu
                return;
            }

            var scale = camera.UnitsPerMetre; // (metres in your room: in the dollhouse a metre is much more of the lot)
            const float above = 1.5f;         // (world units over an object's base: its middle, roughly)

            // the action bubble over your Sim (while you're stepped out): the trigger cancels what they're doing
            if (camera.BubbleVisible && RayHitsSphere(camera.PointerStart, camera.PointerDirection, camera.BubblePosition,
                camera.BubbleRadius, out var bubbleDistance))
            {
                camera.PointerVisible = true;
                camera.PointerHasTarget = !camera.BubbleCancelled;
                camera.PointerLength = bubbleDistance;
                if (select && !lc.PieMenuOpen && DirectAvatar != null && !camera.BubbleCancelled)
                {
                    CancelAction(vm, DirectAvatar, camera.BubbleUID);
                    SimitoneVR.Runtime?.Haptic(true, 0.35f, 0.03f);
                }
                return;
            }

            var id = world.PickPointed();
            var target = id > 0 ? vm.GetObjectById(id) : null;
            if (target != null && target.Dead) target = null;
            var start = camera.PointerStart;
            var dir = camera.PointerDirection;

            float length;
            Vector2? floorTile = null;
            if (target != null)
            {
                var p = target.VisualPosition;
                var targetWorld = new Vector3(p.X * VRCameraController.TileUnits, p.Z * VRCameraController.TileUnits + above, p.Y * VRCameraController.TileUnits);
                length = MathHelper.Clamp(Vector3.Dot(targetWorld - start, dir), 0.2f * scale, 30f * scale);
            }
            else
            {
                // the floor you're standing on
                var headTile = new Vector2(camera.HeadWorld.X, camera.HeadWorld.Z) / VRCameraController.TileUnits;
                var floorY = VRCameraController.FloorHeight(world, headTile, camera.PlayerLevel);
                length = 8f * scale;
                if (dir.Y < -0.02f)
                {
                    var t = (floorY - start.Y) / dir.Y;
                    if (t > 0 && t < length)
                    {
                        length = t;
                        var hit = start + dir * t;
                        floorTile = new Vector2(hit.X, hit.Z) / VRCameraController.TileUnits;
                    }
                }
            }
            camera.PointerVisible = true;
            camera.PointerHasTarget = target != null;
            camera.PointerLength = length;

            if (!select || lc.PieMenuOpen) return;
            var pp = GameFacade.GraphicsDevice.PresentationParameters;
            var centre = new Vector2(pp.BackBufferWidth, pp.BackBufferHeight) / 2 / FSOEnvironment.DPIScaleFactor;
            // the pie menu floats over what you pointed at
            var anchor = start + dir * length;
            if (target != null)
            {
                var vp = target.VisualPosition;
                anchor = new Vector3(vp.X * VRCameraController.TileUnits, vp.Z * VRCameraController.TileUnits + above, vp.Y * VRCameraController.TileUnits);
            }
            SimitoneVR.SetPieAnchor(camera.WorldToTracking(anchor));
            if (target != null)
            {
                var p = target.Position;
                lc.OpenPieMenu(target, new Vector2(p.x / 16f, p.y / 16f), centre, state);
            }
            else if (floorTile != null)
            {
                // "Go Here" on the floor you're pointing at: the menu works on the visible floor, which in first
                // person is the top one, so point it at yours for the duration of the call.
                var worldState = world.State;
                var visible = worldState.Level;
                worldState.SilentLevel = camera.PlayerLevel;
                try { lc.OpenPieMenu(null, floorTile.Value, centre, state); }
                finally { worldState.SilentLevel = visible; }
            }
            if (lc.PieMenuOpen) SimitoneVR.Runtime?.Haptic(true, 0.35f, 0.03f);
        }

        /// <summary>
        /// Opens an object's or Sim's pie menu over it, as pointing at it and pulling the trigger does (a fingertip tap
        /// in the dollhouse).
        /// </summary>
        private static void OpenPieFor(Simitone.Client.UI.Panels.UILotControl lc, VRCameraController camera, VMEntity target, UpdateState state)
        {
            if (target == null || target.Dead || lc.PieMenuOpen || !lc.LiveMode) return;
            var pp = GameFacade.GraphicsDevice.PresentationParameters;
            var centre = new Vector2(pp.BackBufferWidth, pp.BackBufferHeight) / 2 / FSOEnvironment.DPIScaleFactor;
            var vp = target.VisualPosition;
            SimitoneVR.SetPieAnchor(camera.WorldToTracking(new Vector3(vp.X * VRCameraController.TileUnits,
                vp.Z * VRCameraController.TileUnits + 1.5f, vp.Y * VRCameraController.TileUnits)));
            var p = target.Position;
            lc.OpenPieMenu(target, new Vector2(p.x / 16f, p.y / 16f), centre, state);
            if (lc.PieMenuOpen) SimitoneVR.Runtime?.Haptic(true, 0.35f, 0.03f);
        }

        private static bool RayHitsSphere(Vector3 start, Vector3 dir, Vector3 centre, float radius, out float distance)
        {
            distance = Vector3.Dot(centre - start, dir);
            if (distance <= 0) return false;
            return Vector3.DistanceSquared(start + dir * distance, centre) <= radius * radius;
        }

        // ------------------------------------------------------------------ UI, buttons, HUD

        /// <summary>
        /// In first person the bottom panel is replaced by the wrist displays; the lot control stops cutting walls
        /// away around the (invisible) mouse. The rest of the UI stays live so pie menus and dialogs can be shown on
        /// the floating panel.
        /// </summary>
        private void UpdateImmersiveUI(Simitone.Client.UI.Panels.UILotControl lc, bool immersive, bool vrBuild)
        {
            var frontend = Screen.Frontend;
            if (immersive != WasImmersive)
            {
                WasImmersive = immersive;
                if (immersive)
                {
                    WallsModeBefore = lc.WallsMode;
                    lc.WallsMode = 2;
                    if (frontend != null) FrontendVisibleBefore = frontend.Visible;
                }
                else
                {
                    lc.WallsMode = WallsModeBefore;
                    if (frontend != null) frontend.Visible = FrontendVisibleBefore;
                    lc.CancelPie();
                }
            }
            // In first person the bottom panel is only shown in buy/build mode, where it's the catalogue on the desk panel
            // (the tablet in your hand drives it unseen).
            var showFrontend = vrBuild && !SimitoneVR.CatalogInHand && !SimitoneVR.DollhouseAutoBuy;
            if (immersive && frontend != null && frontend.Visible != showFrontend) frontend.Visible = showFrontend;
        }

        private sbyte CutLevel = -1;
        private FSO.LotView.WorldRotation CutRotation;
        private float CutAge;

        /// <summary>
        /// The dollhouse's cutaway: the walls of every room on the floor shown that face you are cut away, as the game's
        /// cutaway mode does (its own room cut, VMArchitectureTools.GenerateRoomCut), from the side you're looking at
        /// the model from; worked out again when you go round to another side, change floor, or every second (rooms
        /// change as you build).
        /// </summary>
        private void UpdateDollhouseCutaway(VM vm, VRLotWorld world, VRCameraController camera, bool inDollhouse)
        {
            var bp = world.LotBlueprint;
            if (!inDollhouse || SimitoneVR.Settings.BuildWalls != VRWallView.Cutaway || bp == null || vm.Context?.RoomInfo == null)
            {
                CutLevel = -1;
                return;
            }
            var level = world.State.Level;
            var rotation = world.State.CutRotation;
            CutAge += UpdateDelta;
            if (level == CutLevel && rotation == CutRotation && CutAge < 1f) return;
            CutLevel = level;
            CutRotation = rotation;
            CutAge = 0;
            var rooms = new System.Collections.Generic.HashSet<uint>();
            foreach (var info in vm.Context.RoomInfo)
                if (!info.Room.IsOutside && info.Room.Floor == level - 1) rooms.Add(info.Room.RoomID);
            try
            {
                bp.Cutaway = FSO.SimAntics.Utils.VMArchitectureTools.GenerateRoomCut(vm.Context.Architecture, level, rotation, rooms);
                bp.Changes.SetFlag(FSO.LotView.BlueprintGlobalChanges.WALL_CUT_CHANGED);
            }
            catch (Exception e)
            {
                SimitoneVR.Log("dollhouse cutaway: " + e.Message);
            }
        }

        /// <summary>Buy/build mode in first person: what the camera's cursor does, rotating, and grabbing.</summary>
        private void UpdateBuildMode(VRCameraController camera, Simitone.Client.UI.Panels.UILotControl lc, bool active)
        {
            SimitoneVR.BuildMode = active;
            camera.BuildMode = active;
            var holder = lc.ObjectHolder;
            var holding = active ? holder?.Holding : null;
            if (Screen.VRWorld != null) Screen.VRWorld.Held = holding?.Group;
            camera.PlacementMode = active && (holding != null || lc.CustomControl != null);
            camera.PlacementValid = holding == null || holding.CanPlace == VMPlacementError.Success;
            camera.SuppressTurn = holding != null;

            if (!active)
            {
                LastHolding = null;
                RotateLatched = false;
                return;
            }

            // Grabbed an existing object: aim the arc at it so it doesn't jump away (the holder's grab offset was
            // measured for the straight pointer).
            if (holding != null && holding != LastHolding && holding.IsBought && !camera.InDollhouse)
            {
                var basePos = holding.Group.BaseObject.Position;
                var tile = new Vector2(basePos.x / 16f + 0.5f, basePos.y / 16f + 0.5f);
                holding.TilePosOffset = Vector2.Zero;
                camera.AimArcAt(new Vector3(tile.X * VRCameraController.TileUnits,
                    VRCameraController.FloorHeight(Screen.VRWorld, tile, camera.PlayerLevel),
                    tile.Y * VRCameraController.TileUnits));
            }
            if (holding != null && holding != LastHolding) SimitoneVR.Runtime?.Haptic(true, 0.4f, 0.04f);
            LastHolding = holding;

            // Right stick left/right turns what you hold by 90 degrees.
            var input = SimitoneVR.Runtime?.Input;
            var turnX = input != null && input.Active ? input.Turn.X : 0;
            if (holding != null && !RotateLatched && Math.Abs(turnX) > 0.7f)
            {
                holder.RotateObject(turnX > 0 ? 2 : -2);
                RotateLatched = true;
            }
            else if (Math.Abs(turnX) < 0.3f) RotateLatched = false;
        }

        private void HandleButtons(VM vm, Simitone.Client.UI.Panels.UILotControl lc, VRCameraController camera, bool immersive)
        {
            if (SimitoneVR.ConsumeModeSwitch() && immersive)
            {
                if (SimitoneVR.BuildMode && lc.ObjectHolder?.Holding != null)
                {
                    lc.ObjectHolder.SellBack(null); // B while holding something in buy/build: sell / delete it
                }
                else
                {
                    // your Sim -> observer -> dollhouse -> your Sim
                    SimitoneVR.PlayMode = SimitoneVR.PlayMode == VRPlayMode.Sim ? VRPlayMode.Observer
                        : SimitoneVR.PlayMode == VRPlayMode.Observer ? VRPlayMode.Dollhouse : VRPlayMode.Sim;
                    SimitoneVR.Log("play mode: " + SimitoneVR.PlayMode);
                    HITVM.Get()?.PlaySoundEvent(UISounds.Click);
                }
            }

            if (SimitoneVR.ConsumeBuildMenu())
            {
                // left grip: live -> buy -> build -> live
                var frontend = Screen.Frontend;
                if (frontend?.MainPanel != null)
                {
                    var next = frontend.MainPanel.Mode switch
                    {
                        UIMainPanelMode.LIVE => UIMainPanelMode.BUY,
                        UIMainPanelMode.BUY => UIMainPanelMode.BUILD,
                        _ => UIMainPanelMode.LIVE
                    };
                    frontend.SwitchMode(next);
                    SimitoneVR.Log("mode: " + next);
                }
            }

            if (SimitoneVR.ConsumeNextSim())
            {
                var selected = lc.ActiveEntity;
                var family = vm.TS1State?.CurrentFamily;
                if (family != null)
                {
                    var members = vm.Context.ObjectQueries.Avatars.Where(x => family.RuntimeSubset.Contains(x.Object.OBJ.GUID)).ToList();
                    if (members.Count > 0)
                    {
                        var index = (members.IndexOf(selected) + 1) % members.Count;
                        HITVM.Get()?.PlaySoundEvent(UISounds.QueueAdd);
                        vm.SendCommand(new VMNetChangeControlCmd() { TargetID = members[index].ObjectID });
                    }
                }
            }

            var pause = SimitoneVR.ConsumePauseRequest();
            var speed = SimitoneVR.ConsumeSpeedRequest();
            var dial = SimitoneVR.ConsumeSpeedSet();
            if (SimitoneVR.ConsumeDollhouseRefit()) camera.RefitDollhouse();
            if (vm.SpeedMultiplier < 0) return; // -1 = build/buy/options mode
            if (dial >= 0)
            {
                // the wrist's speed dial
                if (dial == 0 && CurrentSpeed(vm) > 0) SpeedBeforePause = CurrentSpeed(vm);
                Screen.ChangeSpeedTo(dial);
                return;
            }
            var current = CurrentSpeed(vm);
            if (pause)
            {
                if (current == 0) Screen.ChangeSpeedTo(SpeedBeforePause);
                else { SpeedBeforePause = current; Screen.ChangeSpeedTo(0); }
            }
            else if (speed)
            {
                Screen.ChangeSpeedTo(current >= 3 ? 1 : current + 1);
            }
        }

        private static int CurrentSpeed(VM vm)
        {
            switch (vm.SpeedMultiplier)
            {
                case 0: return 0;
                case 3: return 2;
                case 10: return 3;
                case -1: return -1;
                default: return 1;
            }
        }

        private void UpdateHud(VM vm, Simitone.Client.UI.Panels.UILotControl lc)
        {
            var clock = vm.Context?.Clock;
            if (clock == null) { Hud.Valid = false; return; }
            Hud.Valid = true;
            Hud.Hour = clock.Hours;
            Hud.Minute = clock.Minutes;
            Hud.Speed = CurrentSpeed(vm);
            Hud.Money = Screen.ActiveFamily?.Budget ?? 0;
            var sim = lc.ActiveEntity as VMAvatar;
            Hud.SimName = sim?.Name ?? "";
            if (sim != null)
            {
                for (int i = 0; i < 8; i++) Hud.Motives[i] = sim.GetMotiveData(HudMotives[i]);
            }

            // the action queue, as the game's queue display shows it
            Hud.QueueCount = 0;
            var queue = sim?.Thread?.Queue;
            if (queue != null)
            {
                foreach (var i in ShownEntries(queue))
                {
                    if (Hud.QueueCount >= VRHudData.MaxQueue) break;
                    var action = queue[i];
                    var slot = Hud.QueueCount++;
                    var item = Hud.Queue[slot];
                    item.UID = action.UID;
                    item.Name = action.Name ?? "";
                    item.Active = i == 0;
                    item.Cancelled = IsCancelled(sim, action);
                    if (IconOwners[slot] != action.IconOwner || item.Icon == null)
                    {
                        IconOwners[slot] = action.IconOwner;
                        try { item.Icon = action.IconOwner != null ? UIIconCache.GetObject(action.IconOwner) : null; }
                        catch { item.Icon = null; }
                    }
                }
            }
            Hud.Messages = SimitoneVR.MessageCount;
            Hud.MessageTitle = SimitoneVR.MessageTitle;
        }
    }
}
