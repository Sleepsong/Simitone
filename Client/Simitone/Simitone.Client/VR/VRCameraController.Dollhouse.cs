using System;
using FSO.LotView;
using Microsoft.Xna.Framework;
using Simitone.VR;

namespace Simitone.Client.VR
{
    /// <summary>
    /// Dollhouse mode: the lot is a model on a table in front of you and you're a giant over it. It's the same camera
    /// with a much bigger scale (world units per metre: a tile is a few centimetres, not a metre) and a rig that stays
    /// put instead of following a Sim or your stick:
    ///  - on entering, the lot is fitted to about 1.3 m across, its ground a little below your hands, in front of you;
    ///  - squeezing a grip in empty space holds the model: it moves with that hand (up and down too); with both hands it
    ///    also turns with them and grows or shrinks as they move apart or together (a tile from 0.75 to 25 cm);
    ///  - the left stick slides it, the right stick turns it (as turning does elsewhere) and shows a floor up or down;
    ///  - the floor shown is <see cref="PlayerLevel"/> (floors above it and the roof are hidden, as in the game);
    ///  - the walls follow the buy/build wall setting, cut away around where you point.
    /// What the hands hold (the model, furniture, Sims) is decided by <see cref="VRDollhouse"/>, through
    /// <see cref="DragHands"/> and <see cref="HandCursor"/>.
    /// </summary>
    public partial class VRCameraController
    {
        /// <summary>Set every update by the bridge: show the lot as a dollhouse.</summary>
        public bool Dollhouse;
        /// <summary>The rig is a dollhouse rig now (switched at the middle of a blink).</summary>
        public bool InDollhouse { get; private set; }
        private bool DollhouseSwitching;
        /// <summary>World units per metre in the dollhouse.</summary>
        private float DollhouseScale = 120f;
        public const float DollhouseMinScale = TileUnits / 0.25f, DollhouseMaxScale = TileUnits / 0.0075f;
        private const float DollhouseWidth = 1.3f, DollhouseBelowEyes = 0.65f, DollhouseAhead = 0.75f;

        /// <summary>Set every update by the dollhouse: the hands (0 left, 1 right) holding the model itself.</summary>
        public readonly bool[] DragHands = new bool[2];
        private int DragMask;
        private Vector3 DragAnchor;    // the world point held: under the hand, or between the hands
        private Vector3 DragStartVector;
        private float DragStartScale, DragStartYaw;

        /// <summary>The point on the floor shown that you're pointing at (or looking at): cutaway and sound centre.</summary>
        public Vector3 DollhouseFocus;
        public bool DollhouseFocusValid;

        /// <summary>
        /// Set by the dollhouse while a hand picks up or carries furniture: the buy/build cursor goes through that hand
        /// (straight down) instead of following the pointer.
        /// </summary>
        public Vector3? HandCursor;
        /// <summary>How far above the hand the picking camera starts (world units): what's just above the hand isn't picked.</summary>
        private const float HandPickAbove = 0.3f;

        /// <summary>World units per metre of your room: <see cref="VRSettings.WorldScale"/>, or the dollhouse's.</summary>
        public float UnitsPerMetre => Scale;

        /// <summary>A hand's point, in the world: a little ahead of the middle of your fist.</summary>
        public Vector3 HandPoint(int hand) => hand == 0
            ? LeftGripWorld.Translation + LeftPoint * 0.04f * Scale
            : RightGripWorld.Translation + RightPoint * 0.04f * Scale;

        public bool HandValid(int hand) => hand == 0 ? LeftHandValid : RightHandValid;

        /// <summary>Which way a hand points, as a yaw about the vertical (for turning what it holds).</summary>
        public float HandYaw(int hand) => VRMath.Yaw(hand == 0 ? LeftPoint : RightPoint);

        /// <summary>Starts or ends the dollhouse when <see cref="Dollhouse"/> changes, behind a blink.</summary>
        private void UpdateDollhouseSwitch(World world, Vector3 headTrack)
        {
            if (Dollhouse == InDollhouse || DollhouseSwitching) return;
            var enter = Dollhouse;
            DollhouseSwitching = true;
            Blink(() =>
            {
                DollhouseSwitching = false;
                if (enter) EnterDollhouse(world);
                else LeaveDollhouse(world);
            });
        }

        /// <summary>Fits the lot to a table-sized model in front of you, its ground a little below your hands.</summary>
        private void EnterDollhouse(World world)
        {
            var bp = Lot(world);
            if (bp == null) return;
            InDollhouse = true;
            DragMask = 0;
            DollhouseScale = FitScale(Math.Max(bp.Width, bp.Height));
            var head = SimitoneVR.HeadPoseTracking();
            var headTrack = head.Valid ? head.Position : new Vector3(0, Settings.FallbackEyeHeight, 0);
            var yaw = head.Valid ? VRMath.Yaw(head.Orientation) : 0;
            var forward = new Vector3(-(float)Math.Sin(yaw), 0, -(float)Math.Cos(yaw));
            var table = new Vector3(headTrack.X, Math.Max(0.5f, headTrack.Y - DollhouseBelowEyes), headTrack.Z) + forward * DollhouseAhead;
            var centreTile = new Vector2(bp.Width, bp.Height) / 2;
            var centre = new Vector3(centreTile.X * TileUnits, FloorHeight(world, centreTile, 1), centreTile.Y * TileUnits);
            PlaceTrackingAt(table, centre);
            FloorsUsedRefresh = 0;
            SimitoneVR.Log($"dollhouse: a tile is {TileUnits / DollhouseScale * 100:0.0} cm");
        }

        /// <summary>World units per metre that make a lot this many tiles across about <see cref="DollhouseWidth"/> metres wide.</summary>
        public static float FitScale(int tiles) => MathHelper.Clamp(TileUnits * Math.Max(1, tiles) / DollhouseWidth, DollhouseMinScale, DollhouseMaxScale);

        /// <summary>Back to your own size, standing where you were looking down at, facing the same way.</summary>
        private void LeaveDollhouse(World world)
        {
            var target = DollhouseFocusValid ? DollhouseFocus : HeadWorld;
            InDollhouse = false;
            DragMask = 0;
            HandCursor = null;
            var bp = Lot(world);
            var tile = new Vector2(target.X, target.Z) / TileUnits;
            if (bp != null) tile = Vector2.Clamp(tile, new Vector2(1), new Vector2(bp.Width - 1, bp.Height - 1));
            FloorInitialised = false;
            PlaceHead(tile, HeadWorldYaw);
        }

        /// <summary>Moves the rig so that a point of your room (tracking space) is at a point of the world.</summary>
        private void PlaceTrackingAt(Vector3 tracking, Vector3 world)
        {
            RigPosition = VRMath.RigPlacing(tracking, world, RigYaw, Scale);
            Teleported = true;
        }

        /// <summary>
        /// The dollhouse's frame: floors, the stick sliding it, the hands holding it, and where you point. Returns true
        /// while the stick moves you (for the vignette).
        /// </summary>
        private bool UpdateDollhouseRig(World world, VRInput input, float turnX, float dt)
        {
            var active = input != null && input.Active;
            // ---- floors: right stick up/down, PageUp/PageDown (any floor: there's nothing to stand on anyway)
            var turnY = active ? input.Turn.Y : 0;
            int levelChange = KeyboardLevel;
            KeyboardLevel = 0;
            if (!LevelLatched && Math.Abs(turnY) > 0.8f && Math.Abs(turnX) < 0.5f)
            {
                levelChange = Math.Sign(turnY);
                LevelLatched = true;
            }
            else if (Math.Abs(turnY) < 0.3f) LevelLatched = false;
            if (levelChange != 0) ChangeLevel(world, new Vector2(HeadWorld.X, HeadWorld.Z) / TileUnits, levelChange, SimitoneVR.Runtime);

            // ---- the left stick slides the model (you move over it)
            var move = MoveIntent;
            var driven = false;
            if (move != Vector2.Zero && DragMask == 0)
            {
                var forward = new Vector3(-(float)Math.Sin(HeadWorldYaw), 0, -(float)Math.Cos(HeadWorldYaw));
                var right = new Vector3((float)Math.Cos(HeadWorldYaw), 0, -(float)Math.Sin(HeadWorldYaw));
                RigPosition += (forward * move.Y + right * move.X) * 0.5f * Scale * dt;
                driven = true;
            }

            // ---- the hands holding the model
            if (active) UpdateDollhouseDrag(input);
            else DragMask = 0;

            // ---- where you're pointing, on the floor shown (or looking, without a pointer)
            var floorY = (PlayerLevel - 1) * LevelHeightTiles * TileUnits;
            var bp = Lot(world);
            DollhouseFocusValid = false;
            var from = PointerAimValid ? PointerStart : HeadWorld;
            var dir = PointerAimValid ? PointerDirection : TrackingDirectionToWorld(Vector3.Transform(-Vector3.UnitZ,
                SimitoneVR.HeadPoseTracking().Orientation));
            if (dir.Y < -0.01f)
            {
                var t = (floorY - from.Y) / dir.Y;
                if (t > 0)
                {
                    var hit = from + dir * t;
                    var tile = new Vector2(hit.X, hit.Z) / TileUnits;
                    if (bp == null || (tile.X >= 0 && tile.Y >= 0 && tile.X < bp.Width && tile.Y < bp.Height))
                    {
                        DollhouseFocus = new Vector3(hit.X, FloorHeight(world, tile, PlayerLevel), hit.Z);
                        DollhouseFocusValid = true;
                    }
                }
            }
            return driven;
        }

        /// <summary>
        /// One hand holds a point of the model, which stays in that hand. Two hands hold the point between them; turning
        /// them turns the model and moving them apart or together makes it bigger or smaller.
        /// </summary>
        private void UpdateDollhouseDrag(VRInput input)
        {
            var mask = (DragHands[0] && input.LeftGrip.Valid ? 1 : 0) | (DragHands[1] && input.RightGrip.Valid ? 2 : 0);
            if (mask == 0) { DragMask = 0; return; }
            var left = input.LeftGrip.Position;
            var right = input.RightGrip.Position;
            if (mask != DragMask)
            {
                // a hand took hold or let go: start again from here
                DragMask = mask;
                DragStartScale = DollhouseScale;
                DragStartYaw = RigYaw;
                DragStartVector = right - left;
                DragAnchor = TrackingToWorld(mask == 3 ? (left + right) / 2 : mask == 1 ? left : right);
                SimitoneVR.Runtime?.Haptic(mask == 3 || (mask == 2) != SimitoneVR.Settings.LeftHanded, 0.25f, 0.02f);
            }
            if (mask == 3)
            {
                (DollhouseScale, RigYaw) = VRMath.TwoHandHold(DragStartVector, DragStartScale, DragStartYaw, right - left,
                    DollhouseMinScale, DollhouseMaxScale);
                PlaceTrackingAt((left + right) / 2, DragAnchor);
            }
            else PlaceTrackingAt(mask == 1 ? left : right, DragAnchor);
        }
    }
}
