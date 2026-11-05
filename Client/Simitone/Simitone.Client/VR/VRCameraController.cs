using System;
using FSO.Common.Rendering.Framework.Model;
using FSO.LotView;
using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.LotView.Utils;
using FSO.LotView.Utils.Camera;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Simitone.VR;

namespace Simitone.Client.VR
{
    /// <summary>
    /// A camera whose view/projection come straight from the headset: one pair per eye, a "mono" pair for anything
    /// that queries the camera outside the per-eye passes (audio listener, transitions), and a "pointer" pair that
    /// looks down the controller's ray so FreeSO's own picking can find what you point at.
    /// </summary>
    public class VREyeCamera : WorldCamera3D
    {
        public const int MonoEye = -1;
        public const int PointerEye = 2;

        public readonly Matrix[] EyeView = { Matrix.Identity, Matrix.Identity };
        public readonly Matrix[] EyeProjection = { Matrix.Identity, Matrix.Identity };
        public Matrix MonoView = Matrix.Identity;
        public Matrix MonoProjection = Matrix.Identity;
        public Matrix PointerView = Matrix.Identity;
        public Matrix PointerProjection = Matrix.Identity;
        private int _Eye = MonoEye;

        public VREyeCamera(GraphicsDevice device) : base(device, Vector3.Zero, Vector3.Forward, Vector3.Up)
        {
            MonoProjection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(90), 1, 0.15f, 1000);
        }

        /// <summary>-1 = mono, 0 = left eye, 1 = right eye, 2 = pointer.</summary>
        public int Eye
        {
            get => _Eye;
            set { _Eye = value; Invalidate(); }
        }

        public void Invalidate()
        {
            m_ViewDirty = true;
            m_ProjectionDirty = true;
        }

        protected override void CalculateView()
        {
            m_View = _Eye switch { 0 => EyeView[0], 1 => EyeView[1], PointerEye => PointerView, _ => MonoView };
        }

        protected override void CalculateProjection()
        {
            m_Projection = _Eye switch { 0 => EyeProjection[0], 1 => EyeProjection[1], PointerEye => PointerProjection, _ => MonoProjection };
        }
    }

    /// <summary>Where your view is relative to your Sim in Sim mode (see <see cref="VRSimActionView"/>).</summary>
    public enum VRSimView
    {
        /// <summary>In the Sim's head.</summary>
        Anchored,
        /// <summary>Stepped out, standing still and following by blinks (Comfortable, and lying down in any tier).</summary>
        Detached,
        /// <summary>Stepped out, gliding after the Sim at walking pace (Moderate).</summary>
        Gliding
    }

    /// <summary>
    /// The immersive VR camera. It takes over FreeSO's first-person camera slot (CameraControllers.CameraFirstPerson),
    /// so the existing first-person toggle (Tab, or A on the right controller) switches between it and the normal camera.
    ///
    /// Two ways to play:
    ///  - <b>Sim</b> (<see cref="SimAvatar"/> set): you see through your Sim's eyes. The view is anchored to their head
    ///    (smoothed, with a little room to lean) and the left stick walks them through FreeSO's direct control
    ///    (see <see cref="VRGameBridge"/>), so walls, doors and stairs are the game's own.
    ///  - <b>Observer</b>: you roam the lot yourself with smooth locomotion, wall collision and floor changes.
    ///
    /// Comfort (see VR_COMFORT.md): whatever the Sim does on their own - walking off to an interaction, sitting down,
    /// going upstairs, lying in bed - never slides or bobs your view about unless you chose the Intense view. You step
    /// out and follow by blinks (fade to black, move, fade in), glide after them at walking pace, or ride along with a
    /// vignette. Posture and floor changes are blinks in every tier, and the view never turns by itself.
    ///
    /// Coordinates: FreeSO's 3D world uses 3 units per tile with +Y up; a TS1 tile is roughly a metre, so
    /// <see cref="VRSettings.WorldScale"/> world units = 1 m. The "rig" is the tracking space origin placed
    /// in the world: world = RigPosition + yaw(RigYaw) * (tracked metres * scale).
    /// </summary>
    public class VRCameraController : CameraControllerFP
    {
        public const float TileUnits = WorldSpace.WorldUnitsPerTile; // world units per tile
        public const float LevelHeightTiles = 2.95f;                 // floor-to-floor height used by FreeSO's 3D view
        private const float BodyRadiusTiles = 0.2f;

        public VREyeCamera EyeCamera => (VREyeCamera)Camera;

        public Vector3 RigPosition;
        public float RigYaw;
        public sbyte PlayerLevel = 1;
        private float FloorY;
        private bool FloorInitialised;
        private bool SnapLatched, LevelLatched;
        public bool Active { get; private set; }

        // where the player's head is, in world units (updated every frame)
        public Vector3 HeadWorld;
        public float HeadWorldYaw;

        // ---- Sim mode
        /// <summary>The Sim whose eyes we look through, or null for observer mode. Set by <see cref="VRGameBridge"/>.</summary>
        public AvatarComponent SimAvatar;
        private AvatarComponent HiddenHead;
        private Vector3 SmoothedSimHead;
        private bool SimHeadInitialised;
        private Vector3 LeanNeutral;   // tracking-space head position that sits exactly in the Sim's head
        private bool LeanInitialised;
        public bool SimMode => SimAvatar != null;

        // ---- pointer (right controller ray), in world units; the bridge picks along it and reports the hit distance
        public bool PointerVisible;
        public Vector3 PointerStart, PointerDirection;
        public float PointerLength = 6f;
        public bool PointerHasTarget;
        private bool PointerAimValid;

        // ---- buy/build mode (set by VRGameBridge). The game's own object holder and build tools do the work: they
        // read a virtual mouse that SimitoneVR parks on CursorPixel, and in this mode the "mono" camera is replaced by
        // a cursor camera that maps exactly that pixel onto where you aim (the arc's landing point while placing,
        // along the controller's ray while picking things up).
        public bool BuildMode;
        /// <summary>Holding an object or using a build tool: aim with the arc. Otherwise the straight pointer picks.</summary>
        public bool PlacementMode;
        public bool PlacementValid = true;
        /// <summary>The right stick's X axis rotates the held object instead of turning you.</summary>
        public bool SuppressTurn;
        /// <summary>Launch speed of the placement arc (m/s).</summary>
        public float Reach = 7f;
        /// <summary>Where the virtual mouse sits, in the coordinates the holder passes to EstTileAtPosWithScroll.</summary>
        public Vector2 CursorPixel;
        public Vector3 CursorTarget;
        public bool CursorValid;
        public readonly Vector3[] ArcPoints = new Vector3[64];
        public int ArcCount;

        // ---- comfort: blinks (fade to black, move, fade in) and the vignette; VRLotWorld draws both in each eye
        /// <summary>0 = clear, 1 = black.</summary>
        public float FadeAlpha { get; private set; }
        /// <summary>How much of the edge of the view the vignette covers: 0 none, 1 the most.</summary>
        public float VignetteAmount { get; private set; }
        private float BlinkTime = -1, BlinkLength;
        private Action BlinkAction;
        /// <summary>A blink is under way (fading out or back in).</summary>
        public bool Blinking => BlinkTime >= 0;
        /// <summary>A blink is fading out: the view holds still until it is black and the move is made.</summary>
        private bool BlinkFadingOut => BlinkAction != null;
        /// <summary>The view jumped this frame (blink, snap turn, placement): not motion for the vignette.</summary>
        private bool Teleported;

        // ---- Sim mode comfort (Settings.SimActionView)
        public VRSimView SimView { get; private set; }
        /// <summary>Set by the bridge every update: the player can steer the Sim right now (direct control is on top).</summary>
        public bool SimSteering;
        /// <summary>Set by the bridge: the room at a tile on a floor, to keep follow spots in the Sim's room.</summary>
        public Func<Vector2, sbyte, int> RoomAt;
        /// <summary>Set by the bridge: the Sim is taking the step that pushing the stick sends a seated Sim on.</summary>
        public bool SimStandingUp;
        /// <summary>
        /// Set by the bridge: you were steering and walked into a door or stairs, whose routine now runs on top of
        /// direct control. It's your walk, so it doesn't count as the Sim going off on their own.
        /// </summary>
        public bool SimInPortal;
        /// <summary>Set by the bridge: the Sim is routing (walking somewhere on their own) or has a walk queued.</summary>
        public bool SimRouting;
        private AvatarComponent LastSimAvatar;
        private float StandingHeadHeight = 1.6f; // metres; learnt while steering (direct control only runs standing)
        private readonly System.Collections.Generic.Dictionary<AvatarComponent, float> StandingHeights
            = new System.Collections.Generic.Dictionary<AvatarComponent, float>();
        private bool SimLying;
        private Vector3? CarryMotion;   // anchored: how far the Sim carried your head this frame (for the vignette)

        // ---- action bubble: while you're stepped out, what the Sim is doing floats over their head (set by the bridge)
        public Texture2D BubbleIcon;
        public bool BubbleCancelled;
        public ushort BubbleUID;
        /// <summary>The Sim's head, in world units (Sim mode).</summary>
        public Vector3 SimHeadWorld { get; private set; }
        public Vector3 BubblePosition => SimHeadWorld + Vector3.Up * 0.45f * Scale;
        public float BubbleRadius => 0.17f * Scale;
        public bool BubbleVisible => SimAvatar != null && SimView != VRSimView.Anchored && Settings.ActionBubble && BubbleIcon != null;
        private float DrivenHold;       // the vignette keeps the "you're driving" strength briefly after you let go
        private sbyte LastSimLevel;
        private int FramesActive;
        private float SteeringFor, NotSteeringFor, DetachedFor, SinceFollow, SettledFor, HeightSettledFor, GlideStuckFor;
        private Vector2 SettleRefBody, AnchorRefBody, LastFollowBody;
        private float SettleRefHeadY, HeightRefY, HeldHeadY, LastSimHeadY;
        private bool HoldingPosture, Gliding;

        // ---- movement arc (left trigger): your Sim walks to where it lands, or you teleport there as an observer
        public readonly Vector3[] MoveArcPoints = new Vector3[64];
        public int MoveArcCount;
        public bool MoveArcValid;
        private bool MoveArcHeld, MoveArcPending;
        private Vector2 MoveArcTile;
        private sbyte MoveArcLevel;
        private const float MoveArcSpeed = 8f; // launch speed in m/s: about 7.5 m of reach at 45 degrees (tools/vr/math-check)

        // desktop keyboard fallback (no controllers, e.g. testing with a simulated headset)
        private Vector2 KeyboardMove;
        private float KeyboardTurn;
        private int KeyboardLevel;

        // render state we change while immersive, restored on exit
        private sbyte SavedLevel;
        private bool SavedDrawRoofs;
        private bool[] SavedCutaway;
        private bool SavedDynamicCutaway;
        private int FloorsUsedRefresh;
        private sbyte FloorsUsed = 1;

        // where the player was when immersion last ended, so we can put them back (e.g. after build mode)
        public bool ResumeOnNextActivate;
        private bool HasSavedRig;
        private Vector3 SavedRigPosition;
        private float SavedRigYaw;
        private sbyte SavedPlayerLevel;

        private World World;

        public VRCameraController(GraphicsDevice gd, WorldState state) : base(gd, state)
        {
            Camera = new VREyeCamera(gd);
        }

        private static VRSettings Settings => SimitoneVR.Settings ?? VRSettings.Default;
        private float Scale => Settings.WorldScale;

        /// <summary>The lot blueprint (World keeps it protected; VRLotWorld exposes it).</summary>
        public static Blueprint Lot(World world) => (world as VRLotWorld)?.LotBlueprint;

        // ------------------------------------------------------------------ activation

        public override ICameraController BeforeActive(ICameraController previous, World world)
        {
            // FreeSO's first-person camera converts a 2D camera into a 3D one first; we read the 2D state directly.
            return previous;
        }

        public override void OnActive(ICameraController previous, World world)
        {
            World = world;
            LastFP = false;
            var state = world.State;
            state.Cameras.TransitionWeights.Clear();

            // Face the way the previous camera was looking and stand where it was looking.
            float rotationX;
            if (previous is CameraController3D c3d && previous != this) rotationX = c3d.RotationX;
            else if (previous is CameraController2D c2d) rotationX = (float)Math.PI * (((int)c2d.Camera.Rotation) / 2f - 0.25f);
            else rotationX = _RotationX;

            if (ResumeOnNextActivate && HasSavedRig)
            {
                RigPosition = SavedRigPosition;
                RigYaw = SavedRigYaw;
                PlayerLevel = SavedPlayerLevel;
            }
            else
            {
                PlayerLevel = Math.Max((sbyte)1, state.Level);
                FloorInitialised = false;
                PlaceHead(state.CenterTile, rotationX + MathHelper.PiOver2);
            }
            ResumeOnNextActivate = false;
            SimHeadInitialised = false;
            LeanInitialised = false;
            SimView = VRSimView.Anchored;
            LastSimAvatar = null;
            FramesActive = 0;
            CancelBlink();
            VignetteAmount = 0;
            MoveArcHeld = MoveArcPending = false;

            // Only remember the normal view's state once: a second activation without a deactivation in between
            // (two toggles in one update) must not save our own immersive state as "normal".
            if (!Active)
            {
                Active = true;
                SetRenderState(world, true);
                SimitoneVR.OnImmersiveChanged(this, true);
            }
        }

        /// <summary>Called by <see cref="VRLotWorld"/> when another camera has taken over.</summary>
        public void OnDeactivated(World world)
        {
            if (!Active) return;
            Active = false;
            HasSavedRig = true;
            SavedRigPosition = RigPosition;
            SavedRigYaw = RigYaw;
            SavedPlayerLevel = PlayerLevel;
            SetHiddenHead(null);
            SetRenderState(world, false);
            EyeCamera.Eye = VREyeCamera.MonoEye;
            PointerVisible = false;
            CancelBlink();
            VignetteAmount = 0;
            MoveArcCount = 0;
            MoveArcHeld = MoveArcPending = false;
            LastSimAvatar = null;
            SimView = VRSimView.Anchored;
            SimitoneVR.OnImmersiveChanged(this, false);
        }

        private void SetRenderState(World world, bool immersive)
        {
            var state = world.State;
            var bp = Lot(world);
            if (immersive)
            {
                SavedLevel = state.Level;
                SavedDrawRoofs = state.DrawRoofs;
                SavedDynamicCutaway = state.DynamicCutaway;
                // Everything above you is visible from inside: upper floors become ceilings, roofs are drawn.
                FloorsUsedRefresh = 0;
                KeepAllFloorsVisible(world);
                state.DrawRoofs = true;
                state.DynamicCutaway = false;
                if (bp != null)
                {
                    SavedCutaway = bp.Cutaway;
                    bp.Cutaway = new bool[bp.Width * bp.Height]; // walls up
                    bp.Changes.SetFlag(BlueprintGlobalChanges.WALL_CUT_CHANGED);
                }
            }
            else
            {
                if (state.Level != SavedLevel && SavedLevel > 0) state.Level = SavedLevel;
                state.DrawRoofs = SavedDrawRoofs;
                state.DynamicCutaway = SavedDynamicCutaway;
                if (bp != null && SavedCutaway != null && SavedCutaway.Length == bp.Width * bp.Height)
                {
                    bp.Cutaway = SavedCutaway;
                    bp.Changes.SetFlag(BlueprintGlobalChanges.WALL_CUT_CHANGED);
                }
                SavedCutaway = null;
            }
        }

        /// <summary>
        /// Draw every floor in use (upper floors are ceilings from inside). Re-applied every frame because the game
        /// moves the visible floor to the selected Sim's when it centres the camera on them (World.CenterTo).
        /// </summary>
        private void KeepAllFloorsVisible(World world)
        {
            var bp = Lot(world);
            if (bp == null) return;
            if (FloorsUsedRefresh-- <= 0)
            {
                FloorsUsed = bp.GetFloorsUsed();
                FloorsUsedRefresh = 120;
            }
            var top = (sbyte)Math.Max(FloorsUsed, PlayerLevel);
            if (world.State.Level != top) world.State.Level = top;
        }

        private void SetHiddenHead(AvatarComponent avatar)
        {
            if (HiddenHead == avatar) return;
            if (HiddenHead?.Avatar != null) HiddenHead.Avatar.HideHead = false;
            HiddenHead = avatar;
            if (HiddenHead?.Avatar != null) HiddenHead.Avatar.HideHead = true;
        }

        /// <summary>
        /// Moves the rig so the player's head is over <paramref name="tile"/> looking along <paramref name="worldYaw"/>,
        /// keeping their physical position/orientation in the room.
        /// </summary>
        public void PlaceHead(Vector2 tile, float worldYaw)
        {
            var head = SimitoneVR.HeadPoseTracking();
            var headYaw = head.Valid ? VRMath.Yaw(head.Orientation) : 0f;
            RigYaw = MathHelper.WrapAngle(worldYaw - headYaw);
            var headOffset = head.Valid ? RotateYaw(new Vector3(head.Position.X, 0, head.Position.Z) * Scale, RigYaw) : Vector3.Zero;
            RigPosition = new Vector3(tile.X * TileUnits - headOffset.X, RigPosition.Y, tile.Y * TileUnits - headOffset.Z);
            FloorInitialised = false;
            Teleported = true;
        }

        /// <summary>Observer mode: jump next to a Sim (or any tile), e.g. the selected one.</summary>
        public void JumpTo(Vector2 tile, sbyte level)
        {
            PlayerLevel = Math.Max((sbyte)1, level);
            // Stand a little behind the target so it is in front of you, without crossing a wall to get there.
            var forward = new Vector2(-(float)Math.Sin(HeadWorldYaw), -(float)Math.Cos(HeadWorldYaw));
            var stand = (World != null) ? CollideStepped(World, tile, -forward * 1.25f) : tile - forward * 1.25f;
            PlaceHead(stand, HeadWorldYaw);
            if (World != null) { FloorsUsedRefresh = 0; KeepAllFloorsVisible(World); }
        }

        public static Vector3 RotateYaw(Vector3 v, float yaw)
        {
            var c = (float)Math.Cos(yaw);
            var s = (float)Math.Sin(yaw);
            return new Vector3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
        }

        /// <summary>Tracking space (metres) to world units.</summary>
        public Vector3 TrackingToWorld(Vector3 tracking) => RigPosition + RotateYaw(tracking * Scale, RigYaw);

        /// <summary>World yaw of something tracked (a controller, the head).</summary>
        public float WorldYaw(Quaternion trackingOrientation) => MathHelper.WrapAngle(RigYaw + VRMath.Yaw(trackingOrientation));

        // ------------------------------------------------------------------ per-frame

        public override void InvalidateCamera(WorldState state)
        {
            // View/projection come from the headset in PreDraw; nothing to derive from CenterTile here.
        }

        public override void Update(UpdateState state, World world)
        {
            World = world;
            KeyboardMove = Vector2.Zero;
            KeyboardTurn = 0;
            if (!state.WindowFocused || state.InputManager?.GetFocus() != null) return;
            var kb = state.KeyboardState;
            if (kb.IsKeyDown(Keys.W) || kb.IsKeyDown(Keys.Up)) KeyboardMove.Y += 1;
            if (kb.IsKeyDown(Keys.S) || kb.IsKeyDown(Keys.Down)) KeyboardMove.Y -= 1;
            if (kb.IsKeyDown(Keys.A) || kb.IsKeyDown(Keys.Left)) KeyboardMove.X -= 1;
            if (kb.IsKeyDown(Keys.D) || kb.IsKeyDown(Keys.Right)) KeyboardMove.X += 1;
            if (kb.IsKeyDown(Keys.Q)) KeyboardTurn -= 1;
            if (kb.IsKeyDown(Keys.E)) KeyboardTurn += 1;
            if (state.NewKeys.Contains(Keys.PageUp)) KeyboardLevel = 1;
            if (state.NewKeys.Contains(Keys.PageDown)) KeyboardLevel = -1;
        }

        /// <summary>Movement intent this frame (stick plus keyboard fallback), for the bridge to walk the Sim with.</summary>
        public Vector2 MoveIntent
        {
            get
            {
                var input = SimitoneVR.Runtime?.Input;
                var stick = input != null && input.Active && Settings.Movement != VRMovement.Arc ? input.Move : Vector2.Zero;
                var move = stick + KeyboardMove;
                if (move.LengthSquared() > 1) move.Normalize();
                return move;
            }
        }

        /// <summary>World yaw that "forward" on the movement stick refers to (head, or left controller).</summary>
        public float MoveYaw
        {
            get
            {
                var input = SimitoneVR.Runtime?.Input;
                if (Settings.MoveDirection == VRMoveDirection.LeftHand && input != null && input.LeftAim.Valid)
                    return WorldYaw(input.LeftAim.Orientation);
                return HeadWorldYaw;
            }
        }

        public override void PreDraw(World world)
        {
            World = world;
            var vr = SimitoneVR.Runtime;
            var dt = SimitoneVR.FrameDelta;
            var input = vr?.Input;
            var head = SimitoneVR.HeadPoseTracking();
            var settings = Settings;
            var headTrack = head.Valid ? head.Position : new Vector3(0, settings.FallbackEyeHeight, 0);
            FramesActive++;
            var lastRigPosition = RigPosition;
            var lastRigYaw = RigYaw;
            UpdateBlink(dt); // at full black this makes the blink's move

            // ---- turning (pivot around the head so the world rotates around you, not around the play space centre)
            float turnX = (input != null && input.Active && !SuppressTurn ? input.Turn.X : 0) + KeyboardTurn;
            float yawDelta = 0;
            bool smoothTurn = false;
            if (settings.TurnMode == VRTurnMode.Snap && KeyboardTurn == 0)
            {
                if (!SnapLatched && Math.Abs(turnX) > 0.7f)
                {
                    yawDelta = -Math.Sign(turnX) * MathHelper.ToRadians(settings.SnapTurnDegrees);
                    SnapLatched = true;
                    Teleported = true;
                    vr?.Haptic(true, 0.3f, 0.02f);
                }
                else if (Math.Abs(turnX) < 0.3f) SnapLatched = false;
            }
            else
            {
                yawDelta = -turnX * MathHelper.ToRadians(settings.SmoothTurnDegreesPerSecond) * dt;
                smoothTurn = yawDelta != 0;
            }
            if (yawDelta != 0) RotateRigAroundHead(head, yawDelta);

            bool driven = smoothTurn; // the player is moving the view (vignette: VignetteWhenMoving rather than Vignette)
            if (SimAvatar != null && SimAvatar.Avatar != null) driven |= UpdateSim(world, headTrack, dt);
            else
            {
                if (LastSimAvatar != null)
                {
                    // left Sim mode (B, or build mode): blink out to your own height rather than drop to it
                    LastSimAvatar = null;
                    SimView = VRSimView.Anchored;
                    if (FramesActive > 2) Blink(() => FloorInitialised = false);
                }
                if (!BlinkFadingOut) SetHiddenHead(null); // (not while we're still in their head, fading out)
                SimHeadInitialised = false;
                if (!BlinkFadingOut) driven |= UpdateObserverRig(world, head, headTrack, input, turnX, dt);
            }
            UpdateMoveArc(world, input);

            if (BuildMode)
            {
                // Build like the normal game: on your floor, with the floors above and the roof out of the way.
                if (world.State.Level != PlayerLevel) world.State.Level = PlayerLevel;
                world.State.DrawRoofs = false;
            }
            else
            {
                KeepAllFloorsVisible(world);
                world.State.DrawRoofs = true;
            }
            HeadWorld = TrackingToWorld(headTrack);
            UpdateVignette(lastRigPosition, lastRigYaw, headTrack, driven, dt);
            UpdateMatrices(world, head);
            UpdatePointer(input);
            UpdateBuildCursor(world, input, dt);
        }

        private float Metres(float tiles) => tiles * TileUnits / Scale;
        private float Tiles(float metres) => metres * Scale / TileUnits;
        private Vector2 HeadTile(Vector3 headTrack)
        {
            var w = TrackingToWorld(headTrack);
            return new Vector2(w.X, w.Z) / TileUnits;
        }

        // ------------------------------------------------------------------ Sim mode

        /// <summary>
        /// Sim mode: decides whether you're in your Sim's head or stepped out (see <see cref="VRSimView"/>) and places
        /// the rig. Returns true while the player is driving the view by steering the Sim.
        /// </summary>
        private bool UpdateSim(World world, Vector3 headTrack, float dt)
        {
            var settings = Settings;
            var avatar = SimAvatar;
            var tier = settings.SimActionView;
            var simLevel = Math.Max((sbyte)1, avatar.Level);
            var body = new Vector2(avatar.Position.X, avatar.Position.Y);
            // The HEAD bone's height (z = height in tiles including the floor), as FreeSO's own Sim-eye camera uses it.
            var headTiles = avatar.GetHeadlinePos() * avatar.Scale + avatar.Position;
            var headY = headTiles.Z * TileUnits + 0.25f * avatar.Scale;
            LastSimHeadY = headY;
            SimHeadWorld = new Vector3(body.X * TileUnits, headY, body.Y * TileUnits);
            var headAboveFloor = (headY - FloorHeight(world, body, simLevel)) / Scale;

            if (avatar != LastSimAvatar)
            {
                // Sim mode just started, or another Sim: into their head (with a blink unless we've only just arrived)
                var first = LastSimAvatar == null && FramesActive <= 2;
                LastSimAvatar = avatar;
                LastSimLevel = simLevel;
                SettleRefBody = AnchorRefBody = LastFollowBody = body;
                SettleRefHeadY = HeightRefY = headY;
                SettledFor = 0;
                HeightSettledFor = 1f; // judge their posture straight away (don't show a lying Sim's view first)
                if (!StandingHeights.TryGetValue(avatar, out StandingHeadHeight))
                    StandingHeadHeight = SimSteering ? Math.Max(0.5f, headAboveFloor) : 1.6f;
                SimLying = headAboveFloor < StandingHeadHeight * 0.5f;
                if (first) Anchor(); else Blink(Anchor);
            }

            // ---- what the Sim is doing
            if (SimSteering) { SteeringFor += dt; NotSteeringFor = 0; }
            else { NotSteeringFor += dt; SteeringFor = 0; }
            if ((SimSteering || SimInPortal) && SimView == VRSimView.Anchored) AnchorRefBody = body;
            if (SimSteering && headAboveFloor > 0.4f)
            {
                StandingHeadHeight += (headAboveFloor - StandingHeadHeight) * (1f - (float)Math.Exp(-dt * 2f));
                StandingHeights[avatar] = StandingHeadHeight;
            }
            if (Metres(Vector2.Distance(body, SettleRefBody)) > 0.2f || Math.Abs(headY - SettleRefHeadY) / Scale > 0.15f)
            {
                SettleRefBody = body;
                SettleRefHeadY = headY;
                SettledFor = 0;
            }
            else SettledFor += dt;
            if (Math.Abs(headY - HeightRefY) / Scale > 0.12f)
            {
                HeightRefY = headY;
                HeightSettledFor = 0;
            }
            else HeightSettledFor += dt;

            // lying: head under half its standing height (sitting is about two thirds; children are shorter). Two
            // thresholds, so a head hovering around one line doesn't flip between in and out.
            if (headAboveFloor > StandingHeadHeight * 0.62f) SimLying = false;
            else if (headAboveFloor < StandingHeadHeight * 0.5f && HeightSettledFor > 0.25f) SimLying = true;
            var lying = SimLying;
            var upright = !SimLying;
            // walking off on their own: not steerable, and moved away from where you last had them (the step a seated
            // Sim takes when you push the stick to stand them up, and your own walk through a door, don't count)
            var walkedOff = !SimSteering && !SimInPortal && !SimStandingUp && NotSteeringFor > 0.25f
                && Metres(Vector2.Distance(body, AnchorRefBody)) > 0.6f;
            var levelChanged = simLevel != LastSimLevel;
            LastSimLevel = simLevel;

            if (BlinkFadingOut) return false; // hold still until it's black

            if (SimView == VRSimView.Anchored)
            {
                if (lying) StepOut(world, VRSimView.Detached); // never lie down with them
                else if (walkedOff && tier == VRSimActionView.Comfortable) StepOut(world, VRSimView.Detached);
                else if (walkedOff && tier == VRSimActionView.Moderate) StepOut(world, VRSimView.Gliding);
                else if (levelChanged || (SimHeadInitialised
                    && Vector2.Distance(new Vector2(SmoothedSimHead.X, SmoothedSimHead.Z) / TileUnits, body) > 1.5f))
                    Blink(Anchor); // stairs, or a jump: never slide there
                // (with blinks off, StepOut has already moved you out: don't put you back in the head)
                if (BlinkFadingOut || SimView != VRSimView.Anchored) return false;
                UpdateAnchoredRig(world, avatar, body, headY, headTrack, dt);
                return (SimSteering || SimInPortal) && MoveIntent != Vector2.Zero;
            }

            // ---- stepped out
            SetHiddenHead(null);
            DetachedFor += dt;
            SinceFollow += dt;
            var back = (SimSteering && SteeringFor > 0.2f)
                || (upright && (tier == VRSimActionView.Intense
                    ? HeightSettledFor > 0.4f                       // Intense only steps out while they lie down
                    // settled: sat down (1 s), or standing busy in one spot for a while with no walk to come (3 s, so
                    // the short stops of cooking or cleaning don't blink you in and out)
                    : DetachedFor > 1.5f && !SimRouting
                        && SettledFor > (headAboveFloor < StandingHeadHeight * 0.85f ? 1.0f : 3.0f)));
            if (back)
            {
                Blink(Anchor);
                return false;
            }

            var headTile = HeadTile(headTrack);
            var distance = Metres(Vector2.Distance(headTile, body));
            var otherLevel = simLevel != PlayerLevel;
            var otherRoom = RoomAt != null && Vector2.Distance(body, LastFollowBody) > 1f
                && RoomAt(headTile, PlayerLevel) != RoomAt(body, simLevel);
            var leash = Math.Max(1.5f, settings.LeashDistance);

            if (SimView == VRSimView.Gliding && tier == VRSimActionView.Moderate && !otherLevel)
            {
                // Moderate: glide after them at a constant walking pace (no easing, no turning) when they get away
                if (distance > 2.6f) Gliding = true;
                else if (distance < 1.9f) Gliding = false;
                if (Gliding && distance > 1e-3f)
                {
                    var toward = (body - headTile) / Vector2.Distance(body, headTile);
                    var step = toward * Tiles(Math.Max(0.2f, settings.GlideSpeed) * dt);
                    var next = ClampToLot(world, Collide(world, headTile, step));
                    var moved = next - headTile;
                    RigPosition.X += moved.X * TileUnits;
                    RigPosition.Z += moved.Y * TileUnits;
                    GlideStuckFor = moved.Length() < step.Length() * 0.3f ? GlideStuckFor + dt : 0;
                }
                else GlideStuckFor = 0;
                // round a corner, through a doorway or too far behind: blink after them instead
                if (SinceFollow > 1f && (GlideStuckFor > 0.6f || distance > leash * 1.5f || (otherRoom && GlideStuckFor > 0.2f)))
                    FollowSim(world);
            }
            else if (SinceFollow > 1f && (distance > leash || otherLevel || otherRoom))
            {
                FollowSim(world);
            }

            if (!BlinkFadingOut) ApplyStandingHeight(world, HeadTile(headTrack), dt);
            return false;
        }

        /// <summary>In the Sim's head: the neutral head position sits inside it.</summary>
        private void UpdateAnchoredRig(World world, AvatarComponent avatar, Vector2 body, float headY, Vector3 headTrack, float dt)
        {
            var settings = Settings;
            SetHiddenHead(avatar);
            PlayerLevel = Math.Max((sbyte)1, avatar.Level);

            var target = new Vector3(body.X * TileUnits, headY, body.Y * TileUnits);
            if (!SimHeadInitialised)
            {
                SmoothedSimHead = target; // first frame, after a blink
                HeldHeadY = headY;
                HoldingPosture = false;
                SimHeadInitialised = true;
                Teleported = true;
            }
            // Horizontally the Sim's position rather than the head bone, so walk cycles and idle animations don't sway you.
            var follow = 1f - (float)Math.Exp(-dt * Math.Max(0.5f, settings.SimHeadSmoothing));
            var carriedFrom = SmoothedSimHead;
            SmoothedSimHead.X += (target.X - SmoothedSimHead.X) * follow;
            SmoothedSimHead.Z += (target.Z - SmoothedSimHead.Z) * follow;

            // Vertically the head's height, low-passed. A posture change (sitting down, standing up, kneeling) holds
            // the old height until the new one settles, then blinks there instead of riding the animation.
            var heightChange = Math.Abs(headY - HeldHeadY) / Scale;
            if (HoldingPosture || heightChange > 0.3f)
            {
                HoldingPosture = true;
                if (HeightSettledFor > 0.3f) // (after the lying check at 0.25 s: lying down steps out instead)
                {
                    HoldingPosture = false;
                    if (heightChange > 0.08f) Blink(() => { HeldHeadY = LastSimHeadY; SmoothedSimHead.Y = HeldHeadY; });
                }
            }
            else HeldHeadY += (headY - HeldHeadY) * (1f - (float)Math.Exp(-dt * 3f));
            SmoothedSimHead.Y = HeldHeadY;
            // what the vignette counts: the Sim carrying you, not the lean limit holding your head back
            CarryMotion = SmoothedSimHead - carriedFrom;

            // Lean: you can move your head a little away from the Sim's; walking further drags the anchor along.
            if (!LeanInitialised) { LeanNeutral = headTrack; LeanInitialised = true; }
            var limit = Math.Max(0.01f, settings.LeanLimit);
            var horizontal = new Vector2(headTrack.X - LeanNeutral.X, headTrack.Z - LeanNeutral.Z);
            if (horizontal.Length() > limit)
            {
                var excess = horizontal * (1f - limit / horizontal.Length());
                LeanNeutral.X += excess.X;
                LeanNeutral.Z += excess.Y;
            }
            // Vertically, follow the player's posture slowly (sitting down, standing up) and allow some ducking.
            LeanNeutral.Y += (headTrack.Y - LeanNeutral.Y) * (1f - (float)Math.Exp(-dt * 0.15f));
            var dy = headTrack.Y - LeanNeutral.Y;
            var clampedDy = MathHelper.Clamp(dy, -0.5f, 0.15f);
            var anchor = LeanNeutral + new Vector3(0, dy - clampedDy, 0);

            RigPosition = SmoothedSimHead - RotateYaw(anchor * Scale, RigYaw);
            FloorY = RigPosition.Y;
        }

        /// <summary>Back into the Sim's head (runs at full black).</summary>
        private void Anchor()
        {
            SimView = VRSimView.Anchored;
            SimHeadInitialised = false;
            LeanInitialised = false;
            HoldingPosture = Gliding = false;
            var avatar = LastSimAvatar;
            if (avatar != null) AnchorRefBody = new Vector2(avatar.Position.X, avatar.Position.Y);
            Teleported = true;
        }

        /// <summary>Blinks out of the Sim's head to a spot just behind them.</summary>
        private void StepOut(World world, VRSimView view)
        {
            Blink(() =>
            {
                var avatar = LastSimAvatar;
                if (avatar == null) return;
                SimView = view;
                DetachedFor = SinceFollow = GlideStuckFor = 0;
                Gliding = false;
                SetHiddenHead(null);
                var body = new Vector2(avatar.Position.X, avatar.Position.Y);
                LastFollowBody = body;
                PlaceNear(world, body, Math.Max((sbyte)1, avatar.Level), view == VRSimView.Gliding ? 1.8f : 1.0f);
            });
        }

        /// <summary>Blinks to a spot near the Sim, facing them.</summary>
        private void FollowSim(World world)
        {
            SinceFollow = 0;
            Blink(() =>
            {
                var avatar = LastSimAvatar;
                if (avatar == null) return;
                var body = new Vector2(avatar.Position.X, avatar.Position.Y);
                LastFollowBody = body;
                SinceFollow = GlideStuckFor = 0;
                Gliding = false;
                PlaceNear(world, body, Math.Max((sbyte)1, avatar.Level), 1.8f);
            });
        }

        /// <summary>
        /// Puts your head <paramref name="metres"/> from a tile (the Sim), with it in front of you: straight back along
        /// the way you're looking if that's clear, otherwise the nearest direction that is (turning you to face it).
        /// Spots in the same room, with a floor and clear of walls are preferred.
        /// </summary>
        private void PlaceNear(World world, Vector2 tile, sbyte level, float metres)
        {
            PlayerLevel = level;
            var distance = Tiles(metres);
            var room = RoomAt?.Invoke(tile, level) ?? 0;
            var bestSpot = tile;
            var bestYaw = HeadWorldYaw;
            var bestScore = float.MinValue;
            foreach (var degrees in new[] { 0f, 40f, -40f, 80f, -80f, 120f, -120f, 180f })
            {
                var yaw = HeadWorldYaw + MathHelper.ToRadians(degrees);
                var forward = new Vector2(-(float)Math.Sin(yaw), -(float)Math.Cos(yaw));
                var spot = ClampToLot(world, CollideStepped(world, tile, -forward * distance));
                var reached = Math.Min(1f, Vector2.Distance(spot, tile) / distance);
                var sameRoom = RoomAt == null || RoomAt(spot, level) == room;
                var floor = HasFloor(world, spot, level);
                var score = reached + (sameRoom ? 1f : 0f) + (floor ? 2f : 0f) - Math.Abs(degrees) / 720f;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestSpot = spot;
                    bestYaw = yaw;
                }
                if (sameRoom && floor && reached > 0.9f) break;
            }
            PlaceHead(bestSpot, MathHelper.WrapAngle(bestYaw));
        }

        /// <summary>Your own standing eye height over the floor where you are (observer, or stepped out of the Sim).</summary>
        private void ApplyStandingHeight(World world, Vector2 headTile, float dt)
        {
            var settings = Settings;
            // gravity: drop to the floor below if there is nothing under you on this level
            while (PlayerLevel > 1 && !HasFloor(world, headTile, PlayerLevel)) PlayerLevel--;
            var targetFloor = FloorHeight(world, headTile, PlayerLevel);
            if (!FloorInitialised) { FloorY = targetFloor; FloorInitialised = true; Teleported = true; }
            else FloorY += (targetFloor - FloorY) * (1f - (float)Math.Exp(-dt * 14f));
            var eyeBase = SimitoneVR.Runtime != null && SimitoneVR.Runtime.FloorRelative ? 0f : settings.FallbackEyeHeight;
            RigPosition.Y = FloorY + (eyeBase + settings.HeightOffset) * Scale;
        }

        // ------------------------------------------------------------------ comfort: blinks and the vignette

        /// <summary>
        /// Fades to black, runs <paramref name="atBlack"/> (which moves you) and fades back in. The view holds still
        /// while it fades out; a blink asked for during another one joins it.
        /// </summary>
        public void Blink(Action atBlack)
        {
            atBlack ??= () => { };
            var length = Settings.BlinkDuration;
            if (length < 0.03f || !Active)
            {
                atBlack();
                Teleported = true;
                return;
            }
            if (BlinkAction != null)
            {
                var first = BlinkAction;
                BlinkAction = () => { first(); atBlack(); };
                return;
            }
            BlinkAction = atBlack;
            BlinkLength = length;
            // fading back in from another blink: go dark again from where it is
            BlinkTime = BlinkTime >= 0 ? FadeAlpha * length * 0.4f : 0;
        }

        private void CancelBlink()
        {
            BlinkAction = null;
            BlinkTime = -1;
            FadeAlpha = 0;
        }

        private void UpdateBlink(float dt)
        {
            if (BlinkTime < 0) { FadeAlpha = 0; return; }
            BlinkTime += dt;
            var fadeOut = BlinkLength * 0.4f; // ~100 ms out, ~150 ms in
            if (BlinkAction != null)
            {
                if (BlinkTime < fadeOut)
                {
                    FadeAlpha = BlinkTime / fadeOut;
                    return;
                }
                var action = BlinkAction;
                BlinkAction = null;
                BlinkTime = fadeOut;
                FadeAlpha = 1; // this frame is black
                action();
                Teleported = true;
                return;
            }
            var t = (BlinkTime - fadeOut) / Math.Max(1e-3f, BlinkLength - fadeOut);
            FadeAlpha = 1 - MathHelper.Clamp(t, 0, 1);
            if (t >= 1)
            {
                BlinkTime = -1;
                FadeAlpha = 0;
            }
        }

        public static float VignetteStrength(VRVignetteStrength strength) => strength switch
        {
            VRVignetteStrength.Low => 0.5f,
            VRVignetteStrength.Medium => 0.7f,
            VRVignetteStrength.High => 1f,
            _ => 0f
        };

        /// <summary>
        /// The vignette follows how fast the rig carries you (moving or turning, not counting your own head motion),
        /// at <see cref="VRSettings.Vignette"/> strength, or <see cref="VRSettings.VignetteWhenMoving"/> while you drive.
        /// </summary>
        private void UpdateVignette(Vector3 lastRigPosition, float lastRigYaw, Vector3 headTrack, bool driven, float dt)
        {
            if (Teleported || dt <= 0)
            {
                Teleported = false;
                CarryMotion = null;
                return;
            }
            var settings = Settings;
            var h = headTrack * Scale;
            var before = lastRigPosition + RotateYaw(h, lastRigYaw);
            var after = RigPosition + RotateYaw(h, RigYaw);
            var moved = CarryMotion ?? (after - before);
            CarryMotion = null;
            var speed = moved.Length() / Scale / dt;                          // m/s
            // letting go of the stick: keep the lighter "driving" strength while the Sim slows to a stop
            if (driven) DrivenHold = 0.6f;
            else if (DrivenHold > 0) { DrivenHold -= dt; driven = true; }
            var turn = Math.Abs(MathHelper.WrapAngle(RigYaw - lastRigYaw)) / dt; // rad/s
            float target = 0;
            if (speed < 20f)
            {
                var motion = Math.Max(MathHelper.Clamp((speed - 0.25f) / 1.25f, 0, 1), MathHelper.Clamp((turn - 0.3f) / 1.2f, 0, 1));
                target = motion * VignetteStrength(driven ? settings.VignetteWhenMoving : settings.Vignette);
            }
            var rate = target > VignetteAmount ? 8f : 3f;
            VignetteAmount += (target - VignetteAmount) * (1f - (float)Math.Exp(-dt * rate));
            if (VignetteAmount < 0.003f) VignetteAmount = 0;
        }

        // ------------------------------------------------------------------ movement arc

        /// <summary>
        /// Left trigger: an arc from the left controller lands on your floor; letting go walks your Sim there (the
        /// bridge sends a "Go Here") or, as an observer, blinks you there.
        /// </summary>
        private void UpdateMoveArc(World world, VRInput input)
        {
            MoveArcCount = 0;
            var enabled = Settings.Movement != VRMovement.Stick && input != null && input.Active && input.LeftAim.Valid
                && !SimitoneVR.MenuOpen;
            if (!enabled)
            {
                MoveArcHeld = false;
                return;
            }
            if (input.MoveArc.Down)
            {
                MoveArcHeld = true;
                var start = TrackingToWorld(input.LeftAim.Position);
                var dir = Vector3.Normalize(RotateYaw(input.LeftAim.Forward, RigYaw));
                var end = TraceArc(world, start, dir * MoveArcSpeed * Scale, PlayerLevel, MoveArcPoints, out MoveArcCount, out var landed);
                var tile = new Vector2(end.X, end.Z) / TileUnits;
                var bp = Lot(world);
                MoveArcValid = landed && bp != null && tile.X >= 0 && tile.Y >= 0 && tile.X < bp.Width && tile.Y < bp.Height
                    && HasFloor(world, tile, PlayerLevel);
                MoveArcTile = tile;
                MoveArcLevel = PlayerLevel;
                return;
            }
            if (!MoveArcHeld) return;
            MoveArcHeld = false;
            if (!MoveArcValid) return;
            SimitoneVR.Runtime?.Haptic(false, 0.35f, 0.03f);
            if (SimAvatar != null)
            {
                MoveArcPending = true;
                return;
            }
            var target = MoveArcTile;
            var level = MoveArcLevel;
            Blink(() =>
            {
                PlayerLevel = level;
                PlaceHead(target, HeadWorldYaw);
                FloorsUsedRefresh = 0;
            });
        }

        /// <summary>Sim mode: where the movement arc was let go (once), for the bridge to walk the Sim to.</summary>
        public bool ConsumeMoveArc(out Vector2 tile, out sbyte level)
        {
            tile = MoveArcTile;
            level = MoveArcLevel;
            var pending = MoveArcPending;
            MoveArcPending = false;
            return pending;
        }

        /// <summary>A ballistic arc (as a teleport arc) until it reaches the floor of <paramref name="level"/>.</summary>
        private Vector3 TraceArc(World world, Vector3 pos, Vector3 velocity, sbyte level, Vector3[] points, out int count, out bool landed)
        {
            var gravity = 9.8f * Scale;
            const float step = 0.015f;
            count = 0;
            points[count++] = pos;
            landed = false;
            for (int i = 0; i < 300 && !landed; i++)
            {
                var next = pos + velocity * step;
                velocity.Y -= gravity * step;
                var floor = FloorHeight(world, new Vector2(next.X, next.Z) / TileUnits, level);
                if (next.Y <= floor)
                {
                    var drop = pos.Y - next.Y;
                    var f = drop > 1e-5f ? MathHelper.Clamp((pos.Y - floor) / drop, 0, 1) : 1;
                    next = pos + (next - pos) * f;
                    next.Y = floor;
                    landed = true;
                }
                pos = next;
                if ((i % 5 == 4 || landed) && count < points.Length) points[count++] = pos;
            }
            return pos;
        }

        /// <summary>Observer mode: smooth locomotion, wall collision, floors. Returns true while the stick moves you.</summary>
        private bool UpdateObserverRig(World world, VRPose head, Vector3 headTrack, VRInput input, float turnX, float dt)
        {
            var settings = Settings;

            // ---- floor changes (right stick up/down, PageUp/PageDown); while placing, the stick changes the reach
            float turnY = input != null && input.Active && !(BuildMode && PlacementMode) ? input.Turn.Y : 0;
            int levelChange = KeyboardLevel;
            KeyboardLevel = 0;
            if (!LevelLatched && Math.Abs(turnY) > 0.8f && Math.Abs(turnX) < 0.5f)
            {
                levelChange = Math.Sign(turnY);
                LevelLatched = true;
            }
            else if (Math.Abs(turnY) < 0.3f) LevelLatched = false;

            HeadWorld = TrackingToWorld(headTrack);
            var headTile = new Vector2(HeadWorld.X, HeadWorld.Z) / TileUnits;
            if (levelChange != 0) ChangeLevel(world, headTile, levelChange, SimitoneVR.Runtime);

            // ---- smooth locomotion
            var move = MoveIntent;
            if (move != Vector2.Zero)
            {
                var worldYaw = MoveYaw;
                var forward = new Vector2(-(float)Math.Sin(worldYaw), -(float)Math.Cos(worldYaw));
                var right = new Vector2((float)Math.Cos(worldYaw), -(float)Math.Sin(worldYaw));
                var speed = settings.MoveSpeed * ((input != null && input.Sprint.Down) || IsShiftDown() ? settings.SprintMultiplier : 1f);
                var deltaTiles = (forward * move.Y + right * move.X) * speed * dt * (Scale / TileUnits);

                var newTile = settings.WallCollision ? Collide(world, headTile, deltaTiles) : headTile + deltaTiles;
                newTile = ClampToLot(world, newTile);
                var moved = (newTile - headTile) * TileUnits;
                RigPosition.X += moved.X;
                RigPosition.Z += moved.Y;
                headTile = newTile;
            }

            ApplyStandingHeight(world, headTile, dt);
            return move != Vector2.Zero;
        }

        private static bool IsShiftDown()
        {
            var kb = Keyboard.GetState();
            return kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift);
        }

        private void RotateRigAroundHead(VRPose head, float yawDelta)
        {
            var headTrack = head.Valid ? new Vector3(head.Position.X, 0, head.Position.Z) * Scale : Vector3.Zero;
            var headWorld = RigPosition + RotateYaw(headTrack, RigYaw);
            RigYaw = MathHelper.WrapAngle(RigYaw + yawDelta);
            var newOffset = RotateYaw(headTrack, RigYaw);
            RigPosition.X = headWorld.X - newOffset.X;
            RigPosition.Z = headWorld.Z - newOffset.Z;
        }

        private void ChangeLevel(World world, Vector2 tile, int direction, VRRuntime vr)
        {
            var bp = Lot(world);
            if (bp == null) return;
            if (direction > 0)
            {
                for (sbyte level = (sbyte)(PlayerLevel + 1); level <= bp.Stories; level++)
                {
                    if (HasFloor(world, tile, level))
                    {
                        PlayerLevel = level;
                        vr?.Haptic(true, 0.5f, 0.05f);
                        break;
                    }
                }
            }
            else if (PlayerLevel > 1)
            {
                PlayerLevel--;
                vr?.Haptic(true, 0.5f, 0.05f);
            }
            FloorsUsedRefresh = 0;
        }

        // ------------------------------------------------------------------ lot geometry queries (tile space)

        private static bool InBounds(Blueprint bp, int x, int y) => x >= 0 && y >= 0 && x < bp.Width && y < bp.Height;

        public static bool HasFloor(World world, Vector2 tile, sbyte level)
        {
            if (level <= 1) return true;
            var bp = Lot(world);
            if (bp == null || level > bp.Stories) return false;
            int x = (int)Math.Floor(tile.X), y = (int)Math.Floor(tile.Y);
            if (!InBounds(bp, x, y)) return false;
            return bp.GetFloor((short)x, (short)y, level).Pattern != 0;
        }

        /// <summary>Height of the floor surface in world units.</summary>
        public static float FloorHeight(World world, Vector2 tile, sbyte level)
        {
            var bp = Lot(world);
            float altitude = bp != null ? bp.InterpAltitude(new Vector3(tile, 0)) : 0;
            return (altitude + (level - 1) * LevelHeightTiles) * TileUnits;
        }

        private bool WallOnVerticalLine(Blueprint bp, int lineX, int row)
        {
            // TopLeft wall of tile (x, y) runs along x = tile.X from y to y+1 (see VMArchitecture.RaycastWall).
            return InBounds(bp, lineX, row) && bp.GetWall((short)lineX, (short)row, PlayerLevel).TopLeftSolid;
        }

        private bool WallOnHorizontalLine(Blueprint bp, int lineY, int column)
        {
            // TopRight wall of tile (x, y) runs along y = tile.Y from x to x+1.
            return InBounds(bp, column, lineY) && bp.GetWall((short)column, (short)lineY, PlayerLevel).TopRightSolid;
        }

        /// <summary><see cref="Collide"/> in short steps, for moves longer than a tile.</summary>
        private Vector2 CollideStepped(World world, Vector2 pos, Vector2 delta)
        {
            var steps = Math.Max(1, (int)Math.Ceiling(delta.Length() / 0.4f));
            var step = delta / steps;
            for (int i = 0; i < steps; i++) pos = Collide(world, pos, step);
            return pos;
        }

        /// <summary>
        /// Moves a circle of <see cref="BodyRadiusTiles"/> by <paramref name="delta"/>, sliding along walls. The wall
        /// line tested is the next one from the head's centre, so a head that already overlaps a wall (after leaning
        /// into it physically) is pushed back out rather than let through.
        /// </summary>
        private Vector2 Collide(World world, Vector2 pos, Vector2 delta)
        {
            var bp = Lot(world);
            if (bp == null || PlayerLevel > bp.Stories) return pos + delta;
            const float r = BodyRadiusTiles;
            const float eps = 0.001f;

            if (delta.X != 0)
            {
                float nx = pos.X + delta.X;
                int rowMin = (int)Math.Floor(pos.Y - r), rowMax = (int)Math.Floor(pos.Y + r);
                int line = delta.X > 0 ? (int)Math.Floor(pos.X) + 1 : (int)Math.Floor(pos.X);
                bool crossing = delta.X > 0 ? nx + r >= line : nx - r <= line;
                if (crossing)
                    for (int row = rowMin; row <= rowMax; row++)
                        if (WallOnVerticalLine(bp, line, row)) { nx = delta.X > 0 ? line - r - eps : line + r + eps; break; }
                pos.X = nx;
            }

            if (delta.Y != 0)
            {
                float ny = pos.Y + delta.Y;
                int colMin = (int)Math.Floor(pos.X - r), colMax = (int)Math.Floor(pos.X + r);
                int line = delta.Y > 0 ? (int)Math.Floor(pos.Y) + 1 : (int)Math.Floor(pos.Y);
                bool crossing = delta.Y > 0 ? ny + r >= line : ny - r <= line;
                if (crossing)
                    for (int col = colMin; col <= colMax; col++)
                        if (WallOnHorizontalLine(bp, line, col)) { ny = delta.Y > 0 ? line - r - eps : line + r + eps; break; }
                pos.Y = ny;
            }
            return pos;
        }

        private static Vector2 ClampToLot(World world, Vector2 tile)
        {
            var bp = Lot(world);
            float size = bp?.Width ?? 64;
            const float margin = 8f; // allow stepping a little off the lot, onto the road / neighbouring area
            return new Vector2(MathHelper.Clamp(tile.X, -margin, size + margin), MathHelper.Clamp(tile.Y, -margin, size + margin));
        }

        // ------------------------------------------------------------------ matrices and pointer

        private void UpdateMatrices(World world, VRPose head)
        {
            var cam = EyeCamera;
            var vr = SimitoneVR.Runtime;
            var rigRotation = Quaternion.CreateFromAxisAngle(Vector3.Up, RigYaw);
            var settings = Settings;
            float near = Math.Max(0.01f, settings.NearClip) * Scale;
            float far = 1000f;

            if (vr != null && vr.ViewsValid)
            {
                for (int i = 0; i < 2; i++)
                {
                    var view = vr.Views[i];
                    var eyePos = TrackingToWorld(VRMath.ToXna(view.Pose.Position));
                    var eyeRot = rigRotation * VRMath.ToXna(view.Pose.Orientation);
                    var eyeWorld = Matrix.CreateFromQuaternion(eyeRot) * Matrix.CreateTranslation(eyePos);
                    cam.EyeView[i] = Matrix.Invert(eyeWorld);
                    cam.EyeProjection[i] = VRMath.Projection(view.Fov, near, far);
                }
            }

            // Mono camera: centre eye, symmetric projection with the desktop window's aspect ratio.
            var headRot = rigRotation * (head.Valid ? head.Orientation : Quaternion.Identity);
            var headWorldMtx = Matrix.CreateFromQuaternion(headRot) * Matrix.CreateTranslation(HeadWorld);
            cam.MonoView = Matrix.Invert(headWorldMtx);
            var vp = world.State.Device.Viewport;
            cam.MonoProjection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(80), Math.Max(0.1f, vp.AspectRatio), near, far);
            cam.Invalidate();

            var forward = Vector3.Transform(-Vector3.UnitZ, headRot);
            HeadWorldYaw = VRMath.Yaw(forward);
            cam.Position = HeadWorld;
            cam.Target = HeadWorld + forward * 10;
            cam.NearPlane = near;
            cam.FarPlane = far;

            // Keep FreeSO's own camera bookkeeping in sync: CenterTile drives sound, sky and culling helpers;
            // the fields below are what CameraController3D reads when we hand control back to it.
            var state = world.State;
            state.CenterTile = new Vector2(HeadWorld.X, HeadWorld.Z) / TileUnits;
            _RotationX = HeadWorldYaw - MathHelper.PiOver2; // also drives CutRotation
            SavedYRot = 0.6f;
            _Zoom3D = 4f;
            FPCamHeight = 0;
            CamHeight = HeadWorld.Y - 3;
        }

        private void UpdatePointer(VRInput input)
        {
            PointerAimValid = false;
            if (input == null || !input.Active)
            {
                PointerVisible = false;
                return;
            }
            var aim = input.RightAim.Valid ? input.RightAim : input.LeftAim;
            if (!aim.Valid)
            {
                PointerVisible = false;
                return;
            }
            PointerStart = TrackingToWorld(aim.Position);
            PointerDirection = Vector3.Normalize(RotateYaw(aim.Forward, RigYaw));
            PointerAimValid = true;
        }

        // ------------------------------------------------------------------ buy/build cursor

        /// <summary>
        /// Placing: a ballistic arc from the controller (like a teleport arc) lands on your floor, and the cursor
        /// camera looks straight down at the landing point. Picking: the cursor camera looks down the controller's
        /// ray, so the game's own picking finds what you point at. Either way the camera's projection is shifted so
        /// that <see cref="CursorPixel"/> maps exactly to that aim, and given a tiny field of view so that any other
        /// mouse position (e.g. while the pointer is on the catalogue) still maps to practically the same spot.
        /// </summary>
        private void UpdateBuildCursor(World world, VRInput input, float dt)
        {
            if (BuildMode && CursorValid && (SimitoneVR.PanelPointerOnPanel || SimitoneVR.MenuOpen))
            {
                // Choosing from the catalogue (or using the VR menu): keep the cursor where it was in the world and hide
                // the arc, so what you hold doesn't fly about while the pointer is elsewhere.
                ArcCount = 0;
                return;
            }
            ArcCount = 0;
            CursorValid = false;
            if (!BuildMode || !PointerAimValid) return;
            var cam = EyeCamera;
            var scale = Scale;
            Matrix view;

            if (PlacementMode)
            {
                // (the stick scrolls the catalogue instead while you point at it)
                var stickY = input != null && input.Active && !SimitoneVR.PanelPointerOnPanel ? input.Turn.Y : 0;
                Reach = MathHelper.Clamp(Reach * (float)Math.Exp(stickY * 1.2f * dt), 1.5f, 18f);

                var pos = TraceArc(world, PointerStart, PointerDirection * Reach * scale, PlayerLevel, ArcPoints, out ArcCount, out var landed);
                if (!landed)
                {
                    // out of range: drop straight down from where the arc gave up
                    pos.Y = FloorHeight(world, new Vector2(pos.X, pos.Z) / TileUnits, PlayerLevel);
                    if (ArcCount < ArcPoints.Length) ArcPoints[ArcCount++] = pos;
                }
                CursorTarget = pos;
                view = Matrix.CreateLookAt(pos + Vector3.Up * 20f, pos, Vector3.Forward);
            }
            else
            {
                var dir = PointerDirection;
                var floor = FloorHeight(world, new Vector2(PointerStart.X, PointerStart.Z) / TileUnits, PlayerLevel);
                var t = dir.Y < -0.01f ? (floor - PointerStart.Y) / dir.Y : 30f * scale;
                CursorTarget = PointerStart + dir * Math.Max(0, t);
                var up = Math.Abs(dir.Y) > 0.99f ? Vector3.Forward : Vector3.Up;
                view = Matrix.CreateLookAt(PointerStart, PointerStart + dir, up);
            }

            var pp = world.State.Device.PresentationParameters;
            cam.MonoView = view;
            cam.MonoProjection = VRMath.ShiftedProjection(0.004f, pp.BackBufferWidth, pp.BackBufferHeight, CursorPixel, 0.05f, 4000f);
            cam.Invalidate();
            CursorValid = true;
        }

        /// <summary>
        /// Picks the arc's reach so that it lands on <paramref name="target"/> with the controller where it is now:
        /// used when you grab an object, so it doesn't jump away.
        /// </summary>
        public void AimArcAt(Vector3 target)
        {
            if (!PointerAimValid) return;
            var scale = Scale;
            var speed = VRMath.ArcSpeedToHit(PointerStart, PointerDirection, target, 9.8f * scale);
            if (speed == null) return; // above the straight line: no arc gets there
            Reach = MathHelper.Clamp(speed.Value / scale, 1.5f, 18f);
        }

        /// <summary>Sets up the pointer camera along the controller ray (for FreeSO's picking).</summary>
        public void AimPointerCamera(Viewport viewport)
        {
            var cam = EyeCamera;
            var up = Math.Abs(PointerDirection.Y) > 0.99f ? Vector3.Forward : Vector3.Up;
            cam.PointerView = Matrix.CreateLookAt(PointerStart, PointerStart + PointerDirection, up);
            cam.PointerProjection = Matrix.CreatePerspectiveFieldOfView(0.05f, Math.Max(0.1f, viewport.AspectRatio),
                Math.Max(0.01f, Settings.NearClip) * Scale, 1000f);
        }
    }
}
