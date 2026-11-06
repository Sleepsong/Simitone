using System;
using System.Linq;
using FSO.HIT;
using FSO.Client.UI.Model;
using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.SimAntics;
using FSO.SimAntics.Model;
using FSO.SimAntics.NetPlay.Model.Commands;
using Microsoft.Xna.Framework;
using Simitone.Client.UI.Panels;
using Simitone.Client.UI.Screens;
using Simitone.VR;

namespace Simitone.Client.VR
{
    /// <summary>
    /// Your hands in the dollhouse. Squeezing a grip takes hold of what that hand is on:
    ///  - <b>furniture</b>: you pick it up and carry it, it's drawn in your hand, a line down to the floor shows where it
    ///    will go (green where it fits, red where not), turning your hand turns it a quarter at a time, and letting go
    ///    puts it there (or back where it was, if it can't go there). This is the game's own object holder, as buy mode
    ///    uses it with the mouse: its camera looks straight down through your hand, and the grip presses and lets go
    ///    of its mouse button. During live play the game goes into buy mode for it (and pauses, as buy mode does) and
    ///    comes back afterwards.
    ///  - <b>a Sim</b> (live mode): you lift them, they dangle from your hand, and letting go puts them down on the floor
    ///    below. They stop what they're doing (as when you cancel their actions) and, once they have, the game moves
    ///    them there (<see cref="VMNetMoveObjectCmd"/>, which moves Sims in The Sims 1, as its "moveobjects" cheat
    ///    does); until then they're drawn where you put them. If they can't go there, they come back.
    ///  - <b>nothing</b>: you hold the model itself (see <see cref="VRCameraController"/>): it moves with your hand, and
    ///    with both hands it turns and resizes.
    /// </summary>
    public sealed class VRDollhouse
    {
        private enum Hold { None, Model, Furniture, Sim }
        private enum Step { None, WaitForBuyMode, Aim, Press, Carry, PlaceDown, PlaceUp, Check }

        private readonly Hold[] Holds = new Hold[2];
        private readonly bool[] GripWas = new bool[2];

        // furniture
        private Step FurnitureStep;
        private int StepFrames;
        private int FurnitureHand = -1;
        private bool AutoBuy;
        private float TurnReference;

        // Sims
        private VMAvatar Lifted;
        private int SimHand = -1;
        private VMAvatar Dropping;
        private LotTilePos DropTile;
        private float DropWait, CancelWait, MoveWait;
        private bool MoveSent;

        /// <summary>How far your hand reaches into what it grabs (metres, in your room).</summary>
        private const float GrabReach = 0.025f;
        /// <summary>How far below the hand a Sim's feet hang (world units: you hold them by the scruff).</summary>
        private const float SimHang = 3.9f;

        /// <summary>The game went into buy mode just for a hand to carry something (the catalogue tablet stays away).</summary>
        public bool AutoBuyMode => AutoBuy;
        /// <summary>A hand is picking up or carrying furniture (the bridge leaves the holder's grab offset alone).</summary>
        public bool CarryingByHand => FurnitureStep != Step.None;

        /// <summary>Once per update, in the dollhouse (<paramref name="active"/>); otherwise lets go of everything.</summary>
        public void Update(TS1GameScreen screen, VRLotWorld world, VRCameraController camera, VRInput input, bool active, float dt)
        {
            var lc = screen.LotControl;
            var vm = screen.vm;
            if (!active || lc == null || vm == null || input == null || !input.Active)
            {
                LetGoOfEverything(screen, world, camera);
                return;
            }
            var pointerHand = input.LeftHanded ? 0 : 1;
            for (int hand = 0; hand < 2; hand++)
            {
                var down = Grip(input, hand, pointerHand);
                var pressed = down && !GripWas[hand];
                var released = !down && GripWas[hand];
                GripWas[hand] = down;
                if (pressed && Holds[hand] == Hold.None && camera.HandValid(hand))
                    Holds[hand] = Take(screen, world, camera, hand, pointerHand);
                if (released && Holds[hand] != Hold.None)
                {
                    LetGo(screen, world, camera, hand);
                    Holds[hand] = Hold.None;
                }
                camera.DragHands[hand] = Holds[hand] == Hold.Model;
            }
            UpdateFurniture(screen, world, camera, lc);
            UpdateSims(vm, world, camera, dt);
        }

        /// <summary>A hand's grip, with a little hysteresis on the analogue one.</summary>
        private bool Grip(VRInput input, int hand, int pointerHand)
        {
            if (hand == pointerHand) return input.SecondaryValue > (GripWas[hand] ? 0.35f : 0.6f);
            return input.BuildMenu.Down;
        }

        private static void Buzz(int hand, VRInput input, float strength, float seconds)
            => SimitoneVR.Runtime?.Haptic((hand == 1) != (input?.LeftHanded ?? false), strength, seconds);

        // ------------------------------------------------------------------ taking hold and letting go

        private Hold Take(TS1GameScreen screen, VRLotWorld world, VRCameraController camera, int hand, int pointerHand)
        {
            var lc = screen.LotControl;
            var input = SimitoneVR.Runtime?.Input;
            // (on a VR panel or with a pie menu up, the pointing hand's grip is a right click or "back" there)
            if (hand == pointerHand && (SimitoneVR.OverlayHasPointer || SimitoneVR.PieMenuOpen)) return Hold.None;
            var point = camera.HandPoint(hand);
            var reach = GrabReach * camera.UnitsPerMetre;

            // a Sim (live mode: in buy and build the game is paused, and they couldn't be moved)
            if (lc.LiveMode && Lifted == null && Dropping == null)
            {
                var sim = SimAt(screen.vm, camera, point, reach);
                if (sim != null)
                {
                    Lifted = sim;
                    SimHand = hand;
                    CancelWait = 0;
                    StopActions(screen.vm, sim);
                    world.LiftedSim = sim.WorldUI as AvatarComponent;
                    world.LiftedFeet = point - Vector3.Up * SimHang;
                    HITVM.Get()?.PlaySoundEvent(UISounds.ObjectRotate);
                    Buzz(hand, input, 0.4f, 0.04f);
                    SimitoneVR.Log("dollhouse: picked up " + sim.Name);
                    return Hold.Sim;
                }
            }
            // furniture: one piece at a time, and not while something from the catalogue (or a build tool) is in hand
            if (FurnitureStep == Step.None && lc.ObjectHolder?.Holding == null && lc.CustomControl == null
                && FurnitureAt(world, camera, point, reach) != 0)
            {
                FurnitureHand = hand;
                TurnReference = camera.HandYaw(hand);
                StepFrames = 0;
                if (lc.LiveMode)
                {
                    // the game only lets you move furniture in buy mode (paused): go there for it, and back after
                    AutoBuy = true;
                    SetGameMode(screen, UIMainPanelMode.BUY);
                    FurnitureStep = Step.WaitForBuyMode;
                }
                else FurnitureStep = Step.Aim;
                camera.HandCursor = point;
                Buzz(hand, input, 0.3f, 0.03f);
                return Hold.Furniture;
            }
            return Hold.Model;
        }

        private void LetGo(TS1GameScreen screen, VRLotWorld world, VRCameraController camera, int hand)
        {
            switch (Holds[hand])
            {
                case Hold.Furniture:
                    if (hand != FurnitureHand) break;
                    if (FurnitureStep == Step.Carry) { FurnitureStep = Step.PlaceDown; StepFrames = 0; }
                    else if (FurnitureStep != Step.PlaceDown && FurnitureStep != Step.PlaceUp && FurnitureStep != Step.Check)
                        FinishFurniture(screen, world, camera); // let go before it was picked up
                    break;
                case Hold.Sim:
                    if (hand == SimHand) PutDownSim(screen.vm, world, camera, hand);
                    break;
            }
        }

        private void LetGoOfEverything(TS1GameScreen screen, VRLotWorld world, VRCameraController camera)
        {
            for (int hand = 0; hand < 2; hand++)
            {
                Holds[hand] = Hold.None;
                GripWas[hand] = false;
                if (camera != null) camera.DragHands[hand] = false;
            }
            if (FurnitureStep != Step.None)
            {
                // carrying something when you left the dollhouse: it goes back where it was
                var holder = screen?.LotControl?.ObjectHolder;
                if (holder?.Holding != null && FurnitureStep >= Step.Press) holder.Cancel();
                FinishFurniture(screen, world, camera);
            }
            if (Lifted != null || Dropping != null)
            {
                Lifted = Dropping = null;
                SimHand = -1;
                if (world != null) world.LiftedSim = null;
            }
        }

        // ------------------------------------------------------------------ what a hand is on

        /// <summary>
        /// The Sim a hand is on (as the game's 3D picking sees Sims: a box around the middle of their body), on the
        /// floor shown or below it.
        /// </summary>
        private static VMAvatar SimAt(VM vm, VRCameraController camera, Vector3 point, float reach)
        {
            VMAvatar best = null;
            var bestDistance = float.MaxValue;
            foreach (var entity in vm.Context.ObjectQueries.Avatars)
            {
                if (!(entity is VMAvatar avatar) || avatar.Dead || avatar.Position == LotTilePos.OUT_OF_WORLD) continue;
                if (avatar.Position.Level > camera.PlayerLevel || !(avatar.WorldUI is AvatarComponent component) || !component.Visible) continue;
                var pelvis = component.GetPelvisPosition() * VRCameraController.TileUnits;
                var centre = new Vector3(pelvis.X, pelvis.Z, pelvis.Y);
                var half = new Vector3(0.5f + reach, 2f + reach, 0.5f + reach);
                if (new BoundingBox(centre - half, centre + half).Contains(point) == ContainmentType.Disjoint) continue;
                var distance = Vector3.Distance(centre, point);
                if (distance < bestDistance) { best = avatar; bestDistance = distance; }
            }
            return best;
        }

        /// <summary>
        /// The object a hand is on: the first one straight down from just above the hand, within reach of it. That's
        /// what the game's picking finds with its camera looking down through the hand, so it's what it picks up.
        /// </summary>
        private static short FurnitureAt(VRLotWorld world, VRCameraController camera, Vector3 point, float reach)
        {
            var bp = world.LotBlueprint;
            if (bp == null) return 0;
            const float above = 0.3f; // (as VRCameraController.HandPickAbove)
            var ray = new Ray(point + Vector3.Up * above, -Vector3.Up);
            short best = 0;
            var bestDistance = above + reach;
            foreach (var obj in bp.Objects)
            {
                if (obj.Level > camera.PlayerLevel || !obj.Visible || obj.CutawayHidden) continue;
                var distance = obj.IntersectsBounds(ray);
                if (distance != null && distance.Value <= bestDistance)
                {
                    best = obj.ObjectID;
                    bestDistance = distance.Value;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ furniture

        /// <summary>
        /// The game's object holder driven by a hand: its camera looks down through the hand and the grip is its mouse
        /// button. Picking up is a press; putting down is a press and a release (as clicking it down with the mouse).
        /// Each step lasts at least one update, since the mouse is read at the start of the next one.
        /// </summary>
        private void UpdateFurniture(TS1GameScreen screen, VRLotWorld world, VRCameraController camera, UILotControl lc)
        {
            if (FurnitureStep == Step.None) return;
            var holder = lc.ObjectHolder;
            var input = SimitoneVR.Runtime?.Input;
            var hand = FurnitureHand;
            var point = camera.HandPoint(hand);
            camera.HandCursor = point;
            StepFrames++;
            switch (FurnitureStep)
            {
                case Step.WaitForBuyMode:
                    SimitoneVR.ForcedWorldButton = false;
                    if (!lc.LiveMode && camera.BuildMode) Next(Step.Aim);
                    else if (StepFrames > 90) FinishFurniture(screen, world, camera);
                    break;
                case Step.Aim:
                    // (the cursor camera has to have looked through the hand once before the press)
                    SimitoneVR.ForcedWorldButton = false;
                    if (StepFrames >= 2) Next(Step.Press);
                    break;
                case Step.Press:
                    SimitoneVR.ForcedWorldButton = true;
                    if (holder.Holding != null)
                    {
                        Next(Step.Carry);
                        SimitoneVR.Log("dollhouse: picked up " + holder.Holding.Group.BaseObject);
                        Buzz(hand, input, 0.4f, 0.04f);
                    }
                    else if (StepFrames > 6)
                    {
                        // the game wouldn't let it be picked up (it says why, as a tip over it)
                        Buzz(hand, input, 0.6f, 0.1f);
                        FinishFurniture(screen, world, camera);
                    }
                    break;
                case Step.Carry:
                    SimitoneVR.ForcedWorldButton = false;
                    if (holder.Holding == null) { FinishFurniture(screen, world, camera); break; } // (sold with B)
                    // drawn in your hand: its floor below, lifted to the hand
                    var floor = VRCameraController.FloorHeight(world, new Vector2(point.X, point.Z) / VRCameraController.TileUnits, camera.PlayerLevel);
                    world.HeldLift = Math.Max(0, point.Y - floor - 0.5f);
                    // turning your hand a quarter turns it a quarter
                    var turn = MathHelper.WrapAngle(camera.HandYaw(hand) - TurnReference);
                    if (Math.Abs(turn) > MathHelper.ToRadians(55))
                    {
                        holder.RotateObject(turn < 0 ? 2 : -2);
                        TurnReference += Math.Sign(turn) * MathHelper.PiOver2;
                        Buzz(hand, input, 0.2f, 0.015f);
                    }
                    break;
                case Step.PlaceDown:
                    world.HeldLift = 0;
                    SimitoneVR.ForcedWorldButton = true;
                    if (StepFrames >= 1) Next(Step.PlaceUp);
                    break;
                case Step.PlaceUp:
                    SimitoneVR.ForcedWorldButton = false;
                    if (StepFrames >= 1) Next(Step.Check);
                    break;
                case Step.Check:
                    if (StepFrames < 2) break;
                    if (holder.Holding != null)
                    {
                        // it can't go there: back where it was (the game said why, as a tip)
                        holder.Cancel();
                        Buzz(hand, input, 0.6f, 0.1f);
                        SimitoneVR.Log("dollhouse: couldn't put it there; it went back");
                    }
                    else Buzz(hand, input, 0.3f, 0.03f);
                    FinishFurniture(screen, world, camera);
                    break;
            }
        }

        private void Next(Step step)
        {
            FurnitureStep = step;
            StepFrames = 0;
        }

        private void FinishFurniture(TS1GameScreen screen, VRLotWorld world, VRCameraController camera)
        {
            FurnitureStep = Step.None;
            FurnitureHand = -1;
            SimitoneVR.ForcedWorldButton = null;
            if (camera != null) camera.HandCursor = null;
            if (world != null) world.HeldLift = 0;
            if (AutoBuy)
            {
                AutoBuy = false;
                if (screen?.LotControl?.ObjectHolder?.Holding == null) SetGameMode(screen, UIMainPanelMode.LIVE);
            }
        }

        /// <summary>Live, buy or build, as the game's mode buttons switch them.</summary>
        public static void SetGameMode(TS1GameScreen screen, UIMainPanelMode mode)
        {
            var frontend = screen?.Frontend;
            if (frontend?.MainPanel == null || frontend.MainPanel.Mode == mode) return;
            frontend.SwitchMode(mode);
            if (frontend.MainPanel.Mode != mode)
            {
                // (the touch UI opens its mode picker instead: set it directly)
                frontend.MainPanel.Open();
                frontend.MainPanel.SetMode(mode);
            }
            SimitoneVR.Log("mode: " + mode);
        }

        // ------------------------------------------------------------------ Sims

        private void UpdateSims(VM vm, VRLotWorld world, VRCameraController camera, float dt)
        {
            if (Lifted != null)
            {
                if (Lifted.Dead || SimHand < 0) { Lifted = null; world.LiftedSim = null; }
                else
                {
                    world.LiftedSim = Lifted.WorldUI as AvatarComponent;
                    world.LiftedFeet = camera.HandPoint(SimHand) - Vector3.Up * SimHang;
                    KeepStopping(vm, Lifted, dt);
                }
            }
            if (Dropping == null) return;
            if (Dropping.Dead) { Dropping = null; world.LiftedSim = null; return; }
            var running = vm.SpeedMultiplier > 0; // (paused, the game can't move them yet)
            if (!MoveSent)
            {
                KeepStopping(vm, Dropping, dt);
                if (running) DropWait += dt;
                // once they've stopped (standing, nothing left to do), or after a while anyway
                var stopped = !VRGameBridge.ShownEntries(Dropping.Thread.Queue).Any()
                    && Dropping.GetPersonData(VMPersonDataVariable.Posture) != 1;
                if ((stopped && running) || DropWait > 6f)
                {
                    vm.SendCommand(new VMNetMoveObjectCmd
                    {
                        ObjectID = Dropping.ObjectID,
                        x = DropTile.x,
                        y = DropTile.y,
                        level = DropTile.Level,
                        dir = Dropping.Direction
                    });
                    MoveSent = true;
                    MoveWait = 0;
                }
                return;
            }
            if (running) MoveWait += dt;
            var pos = Dropping.Position;
            if (pos.TileX == DropTile.TileX && pos.TileY == DropTile.TileY && pos.Level == DropTile.Level)
            {
                HITVM.Get()?.PlaySoundEvent(UISounds.ObjectMovePlace);
                SimitoneVR.Log("dollhouse: put " + Dropping.Name + " down");
                Dropping = null;
                world.LiftedSim = null;
            }
            else if (MoveWait > 1.5f)
            {
                // the game wouldn't put them there (in the way of something): they stay where they were
                HITVM.Get()?.PlaySoundEvent(UISounds.Error);
                SimitoneVR.Runtime?.Haptic(true, 0.6f, 0.1f);
                SimitoneVR.Log("dollhouse: couldn't put " + Dropping.Name + " there");
                Dropping = null;
                world.LiftedSim = null;
            }
        }

        /// <summary>Lets go of a Sim over the floor below the hand: they're drawn standing there until the game moves them.</summary>
        private void PutDownSim(VM vm, VRLotWorld world, VRCameraController camera, int hand)
        {
            var sim = Lifted;
            Lifted = null;
            SimHand = -1;
            if (sim == null) return;
            var point = camera.HandPoint(hand);
            var tile = new Vector2(point.X, point.Z) / VRCameraController.TileUnits;
            var bp = world.LotBlueprint;
            if (bp == null || tile.X < 1 || tile.Y < 1 || tile.X >= bp.Width - 1 || tile.Y >= bp.Height - 1)
            {
                // off the lot: they stay where they were
                world.LiftedSim = null;
                SimitoneVR.Runtime?.Haptic(true, 0.6f, 0.1f);
                return;
            }
            // the floor below the hand: the highest one shown that has floor there
            var level = camera.PlayerLevel;
            while (level > 1 && !VRCameraController.HasFloor(world, tile, level)) level--;
            int tx = (int)Math.Floor(tile.X), ty = (int)Math.Floor(tile.Y);
            DropTile = new LotTilePos((short)(tx * 16 + 8), (short)(ty * 16 + 8), level);
            var centre = new Vector2(tx + 0.5f, ty + 0.5f);
            world.LiftedFeet = new Vector3(centre.X * VRCameraController.TileUnits, VRCameraController.FloorHeight(world, centre, level),
                centre.Y * VRCameraController.TileUnits);
            Dropping = sim;
            DropWait = 0;
            MoveSent = false;
            HITVM.Get()?.PlaySoundEvent(UISounds.ObjectPlace);
        }

        /// <summary>Cancels what a Sim is doing or has queued, again every so often (free will finds them things to do).</summary>
        private void KeepStopping(VM vm, VMAvatar sim, float dt)
        {
            CancelWait -= dt;
            if (CancelWait > 0) return;
            CancelWait = 1.5f;
            StopActions(vm, sim);
        }

        private static void StopActions(VM vm, VMAvatar sim)
        {
            var queue = sim.Thread?.Queue;
            if (queue == null) return;
            foreach (var i in VRGameBridge.ShownEntries(queue).ToList())
            {
                if (!VRGameBridge.IsCancelled(sim, queue[i]))
                    vm.SendCommand(new VMNetInteractionCancelCmd { ActionUID = queue[i].UID, ActorUID = sim.PersistID });
            }
        }
    }
}
