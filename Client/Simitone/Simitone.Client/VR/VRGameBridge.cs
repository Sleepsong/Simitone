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

        private readonly TS1GameScreen Screen;
        private VMAvatar DirectAvatar;
        private int DirectInputID;
        private bool DirectRunning;
        private float StandUpHeldFor;
        private double StandUpSentAt = -100;
        private double Now;

        private bool LeftForFlatScreen;
        private bool WasBuyBuild;
        private UIObjectSelection LastHolding;
        private bool RotateLatched;
        private bool WasImmersive;
        private int WallsModeBefore = 1;
        private bool FrontendVisibleBefore = true;
        private int SpeedBeforePause = 1;
        private readonly VRHudData Hud = new VRHudData();

        public VRGameBridge(TS1GameScreen screen)
        {
            Screen = screen;
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
            WasImmersive = false;
            LeftForFlatScreen = false;
            Hud.Valid = false;
        }

        public void Update(UpdateState state)
        {
            Now = state.Time?.TotalGameTime.TotalSeconds ?? Now + 1 / 60.0;
            var world = Screen.VRWorld;
            var lc = Screen.LotControl;
            var vm = Screen.vm;
            var input = SimitoneVR.Runtime?.Input;
            if (world == null || lc == null || vm == null || world.VRCamera == null)
            {
                DirectAvatar = null;
                Hud.Valid = false;
                SimitoneVR.UIPanelWanted = false;
                return;
            }
            var camera = world.VRCamera;
            var immersive = world.Immersive;

            UpdateHud(vm, lc);
            HandleButtons(vm, lc, camera, immersive);

            // ---- buy/build happen in first person (VRBuildMode); options and the travel map are flat: step out of
            // first person for those, and come back after.
            var mainMode = Screen.Frontend?.MainPanel?.Mode ?? UIMainPanelMode.LIVE;
            var buyBuild = !lc.LiveMode && (mainMode == UIMainPanelMode.BUY || mainMode == UIMainPanelMode.BUILD);
            var vrBuild = buyBuild && SimitoneVR.Settings.VRBuildMode;
            var wantFlat = !lc.Visible || (!lc.LiveMode && !vrBuild);
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
            UpdateBuildMode(camera, lc, immersive && vrBuild);

            // ---- play as your Sim
            VMAvatar wanted = null;
            if (immersive && SimitoneVR.PlayMode == VRPlayMode.Sim && lc.LiveMode && lc.ActiveEntity is VMAvatar sim
                && !sim.Dead && sim.Position != LotTilePos.OUT_OF_WORLD && sim.PersistID == vm.MyUID)
                wanted = sim;
            SyncDirectControl(wanted);
            camera.SimAvatar = DirectAvatar?.WorldUI as AvatarComponent;
            if (DirectAvatar != null) DriveSim(camera, input, vm, lc, state);

            // ---- pointing at things
            UpdatePointer(world, camera, vm, lc, state, immersive);
            SimitoneVR.UIPanelWanted = immersive && (lc.PieMenuOpen || SimitoneVR.DialogOpen() || SimitoneVR.BuildMode);
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
                StandUpForStick(vm, lc, move, camera);
                return;
            }
            StandUpHeldFor = 0;

            if (move == Vector2.Zero)
            {
                DirectRunning = false;
                SendDirectInput(new VMDirectControlInput { ID = DirectInputID++, LookDirectionInt = look });
                return;
            }

            var tileMove = StickToTiles(move, camera.MoveYaw);
            var sprintHeld = input != null && input.Active && input.Sprint.Down;
            DirectRunning = sprintHeld || (DirectRunning ? move.Length() > RunOff : move.Length() > RunOn);
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
            var goTo = lc.GotoObject;
            if (goTo == null) return;

            var tile = FreeTileToward(vm, avatar, StickToTiles(move, camera.MoveYaw));
            if (tile == null) return;
            goTo.SetPosition(tile.Value, Direction.NORTH, vm.Context);
            if (goTo.Position == LotTilePos.OUT_OF_WORLD) return;
            var entry = goTo.GetPieMenu(vm, avatar, false, true).FirstOrDefault();
            if (entry == null) return;
            vm.SendCommand(new VMNetGotoCmd
            {
                Interaction = entry.ID,
                Param0 = entry.Param0,
                ActorUID = avatar.PersistID,
                x = tile.Value.x,
                y = tile.Value.y,
                level = tile.Value.Level
            });
            StandUpSentAt = Now;
            StandUpHeldFor = 0;
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
                    camera.PointerLength = SimitoneVR.PanelPointerDistance * SimitoneVR.Settings.WorldScale;
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
                camera.PointerLength = SimitoneVR.PanelPointerDistance * SimitoneVR.Settings.WorldScale;
                if (select && !SimitoneVR.PanelPointerOnPanel && lc.PieMenuOpen) lc.CancelPie(); // clicked beside the menu
                return;
            }

            var scale = SimitoneVR.Settings.WorldScale;
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
                var targetWorld = new Vector3(p.X * VRCameraController.TileUnits, p.Z * VRCameraController.TileUnits + 0.5f * scale, p.Y * VRCameraController.TileUnits);
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
            // In first person the bottom panel is only shown in buy/build mode, where it's the catalogue.
            if (immersive && frontend != null && frontend.Visible != vrBuild) frontend.Visible = vrBuild;
        }

        /// <summary>Buy/build mode in first person: what the camera's cursor does, rotating, and grabbing.</summary>
        private void UpdateBuildMode(VRCameraController camera, Simitone.Client.UI.Panels.UILotControl lc, bool active)
        {
            SimitoneVR.BuildMode = active;
            camera.BuildMode = active;
            var holder = lc.ObjectHolder;
            var holding = active ? holder?.Holding : null;
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
            if (holding != null && holding != LastHolding && holding.IsBought)
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
                    SimitoneVR.PlayMode = SimitoneVR.PlayMode == VRPlayMode.Sim ? VRPlayMode.Observer : VRPlayMode.Sim;
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
            if (vm.SpeedMultiplier < 0) return; // -1 = build/buy/options mode
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
                var motives = new[] { VMMotive.Hunger, VMMotive.Comfort, VMMotive.Hygiene, VMMotive.Bladder,
                    VMMotive.Energy, VMMotive.Fun, VMMotive.Social, VMMotive.Room };
                for (int i = 0; i < 8; i++) Hud.Motives[i] = sim.GetMotiveData(motives[i]);
            }
        }
    }
}
