using System;
using FSO.Common.Rendering.Framework.Model;
using FSO.LotView;
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
    /// A camera whose view/projection come straight from the headset: one pair per eye, plus a "mono" pair
    /// for anything that queries the camera outside the per-eye passes (audio listener, picking, transitions).
    /// </summary>
    public class VREyeCamera : WorldCamera3D
    {
        public readonly Matrix[] EyeView = { Matrix.Identity, Matrix.Identity };
        public readonly Matrix[] EyeProjection = { Matrix.Identity, Matrix.Identity };
        public Matrix MonoView = Matrix.Identity;
        public Matrix MonoProjection = Matrix.Identity;
        private int _Eye = -1;

        public VREyeCamera(GraphicsDevice device) : base(device, Vector3.Zero, Vector3.Forward, Vector3.Up)
        {
            MonoProjection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(90), 1, 0.15f, 1000);
        }

        /// <summary>-1 = mono, 0 = left eye, 1 = right eye.</summary>
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
            m_View = (_Eye >= 0) ? EyeView[_Eye] : MonoView;
        }

        protected override void CalculateProjection()
        {
            m_Projection = (_Eye >= 0) ? EyeProjection[_Eye] : MonoProjection;
        }
    }

    /// <summary>
    /// Immersive first-person VR camera with full (smooth) locomotion.
    ///
    /// It takes over FreeSO's first-person camera slot (CameraControllers.CameraFirstPerson), so the existing
    /// first-person toggle (Tab, or A on the right controller) switches between it and the normal camera.
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
        private bool Active;

        // where the player's head is, in world units / tiles (updated every frame)
        public Vector3 HeadWorld;
        public float HeadWorldYaw;

        // desktop keyboard fallback (no controllers, e.g. testing with a simulated headset)
        private Vector2 KeyboardMove;
        private float KeyboardTurn;
        private int KeyboardLevel;

        // render state we change while immersive, restored on exit
        private sbyte SavedLevel;
        private bool SavedDrawRoofs;
        private bool[] SavedCutaway;
        private bool SavedDynamicCutaway;

        private World World;

        // where the player was when immersion last ended, so we can put them back (e.g. after a dialog)
        public bool ResumeOnNextActivate;
        private bool HasSavedRig;
        private Vector3 SavedRigPosition;
        private float SavedRigYaw;
        private sbyte SavedPlayerLevel;

        public VRCameraController(GraphicsDevice gd, WorldState state) : base(gd, state)
        {
            Camera = new VREyeCamera(gd);
        }

        private static VRSettings Settings => SimitoneVR.Settings ?? VRSettings.Default;
        private float Scale => Settings.WorldScale;

        // ------------------------------------------------------------------ activation

        public override ICameraController BeforeActive(ICameraController previous, World world)
        {
            // FreeSO's first-person camera converts a 2D camera into a 3D one first; we read the 2D state directly.
            return previous;
        }

        public override void OnActive(ICameraController previous, World world)
        {
            World = world;
            Active = true;
            LastFP = false;
            var state = world.State;
            state.Cameras.TransitionWeights.Clear();

            // Face the way the previous camera was looking and stand where it was looking.
            float rotationX;
            if (previous is CameraController3D c3d) rotationX = c3d.RotationX;
            else if (previous is CameraController2D c2d) rotationX = (float)Math.PI * (((int)c2d.Camera.Rotation) / 2f - 0.25f);
            else rotationX = _RotationX;

            if (ResumeOnNextActivate && HasSavedRig)
            {
                RigPosition = SavedRigPosition;
                RigYaw = SavedRigYaw;
                PlayerLevel = SavedPlayerLevel;
                SetRenderState(world, true);
            }
            else
            {
                var spawnTile = state.CenterTile;
                PlayerLevel = Math.Max((sbyte)1, state.Level);
                SetRenderState(world, true);
                FloorInitialised = false;
                PlaceHead(spawnTile, rotationX + MathHelper.PiOver2);
            }
            ResumeOnNextActivate = false;

            SimitoneVR.OnImmersiveChanged(this, true);
        }

        /// <summary>Called by <see cref="VRLotWorld"/> when another camera takes over.</summary>
        public void OnDeactivated(World world)
        {
            if (!Active) return;
            Active = false;
            HasSavedRig = true;
            SavedRigPosition = RigPosition;
            SavedRigYaw = RigYaw;
            SavedPlayerLevel = PlayerLevel;
            SetRenderState(world, false);
            EyeCamera.Eye = -1;
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
                var top = bp != null ? (sbyte)Math.Max(bp.GetFloorsUsed(), PlayerLevel) : state.Level;
                if (state.Level != top) state.Level = top;
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
        }

        /// <summary>Jump next to a Sim (or any tile), e.g. the currently selected one.</summary>
        public void JumpTo(Vector2 tile, sbyte level)
        {
            // Stand a little behind the target so it is in front of you, not inside you.
            var forward = new Vector2(-(float)Math.Sin(HeadWorldYaw), -(float)Math.Cos(HeadWorldYaw));
            PlayerLevel = Math.Max((sbyte)1, level);
            PlaceHead(tile - forward * 1.25f, HeadWorldYaw);
            if (Lot(World) != null)
            {
                var top = (sbyte)Math.Max(Lot(World).GetFloorsUsed(), PlayerLevel);
                if (World.State.Level != top) World.State.Level = top;
            }
        }

        private static Vector3 RotateYaw(Vector3 v, float yaw)
        {
            var c = (float)Math.Cos(yaw);
            var s = (float)Math.Sin(yaw);
            return new Vector3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
        }

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

        public override void PreDraw(World world)
        {
            World = world;
            var vr = SimitoneVR.Runtime;
            var dt = SimitoneVR.FrameDelta;
            var input = vr?.Input;

            var head = SimitoneVR.HeadPoseTracking();
            var settings = Settings;

            // ---- turning (pivot around the head so the world rotates around you, not around the play space centre)
            float turnX = (input != null && input.Active ? input.Turn.X : 0) + KeyboardTurn;
            float yawDelta = 0;
            if (settings.TurnMode == VRTurnMode.Snap && KeyboardTurn == 0)
            {
                if (!SnapLatched && Math.Abs(turnX) > 0.7f)
                {
                    yawDelta = -Math.Sign(turnX) * MathHelper.ToRadians(settings.SnapTurnDegrees);
                    SnapLatched = true;
                    vr?.Haptic(true, 0.3f, 0.02f);
                }
                else if (Math.Abs(turnX) < 0.3f) SnapLatched = false;
            }
            else
            {
                yawDelta = -turnX * MathHelper.ToRadians(settings.SmoothTurnDegreesPerSecond) * dt;
            }
            if (yawDelta != 0) RotateRigAroundHead(head, yawDelta);

            // ---- floor changes (right stick up/down, PageUp/PageDown)
            float turnY = input != null && input.Active ? input.Turn.Y : 0;
            int levelChange = KeyboardLevel;
            KeyboardLevel = 0;
            if (!LevelLatched && Math.Abs(turnY) > 0.8f && Math.Abs(turnX) < 0.5f)
            {
                levelChange = Math.Sign(turnY);
                LevelLatched = true;
            }
            else if (Math.Abs(turnY) < 0.3f) LevelLatched = false;

            // ---- head in world space
            var headTrack = head.Valid ? head.Position : new Vector3(0, settings.FallbackEyeHeight, 0);
            HeadWorld = RigPosition + RotateYaw(headTrack * Scale, RigYaw);
            var headTile = new Vector2(HeadWorld.X, HeadWorld.Z) / TileUnits;

            if (levelChange != 0) ChangeLevel(world, headTile, levelChange, vr);

            // ---- smooth locomotion
            Vector2 move = (input != null && input.Active ? input.Move : Vector2.Zero) + KeyboardMove;
            if (move.LengthSquared() > 1) move.Normalize();
            if (move != Vector2.Zero)
            {
                float dirYaw;
                if (settings.MoveDirection == VRMoveDirection.LeftHand && input != null && input.LeftAim.Valid)
                    dirYaw = VRMath.Yaw(input.LeftAim.Orientation);
                else
                    dirYaw = head.Valid ? VRMath.Yaw(head.Orientation) : 0;
                var worldYaw = RigYaw + dirYaw;
                var forward = new Vector2(-(float)Math.Sin(worldYaw), -(float)Math.Cos(worldYaw));
                var right = new Vector2((float)Math.Cos(worldYaw), -(float)Math.Sin(worldYaw));
                var speed = settings.MoveSpeed * ((input != null && input.Sprint.Down) || IsShiftDown() ? settings.SprintMultiplier : 1f);
                var deltaTiles = (forward * move.Y + right * move.X) * speed * dt * (Scale / TileUnits);

                var newTile = settings.WallCollision ? Collide(world, headTile, deltaTiles) : headTile + deltaTiles;
                newTile = ClampToLot(world, newTile);
                var moved = (newTile - headTile) * TileUnits;
                RigPosition.X += moved.X;
                RigPosition.Z += moved.Y;
                HeadWorld.X += moved.X;
                HeadWorld.Z += moved.Y;
                headTile = newTile;
            }

            // ---- gravity: drop to the floor below if there is nothing under you on this level
            while (PlayerLevel > 1 && !HasFloor(world, headTile, PlayerLevel)) PlayerLevel--;
            var targetFloor = FloorHeight(world, headTile, PlayerLevel);
            if (!FloorInitialised) { FloorY = targetFloor; FloorInitialised = true; }
            else FloorY += (targetFloor - FloorY) * (1f - (float)Math.Exp(-dt * 14f));

            var eyeBase = vr != null && vr.FloorRelative ? 0f : settings.FallbackEyeHeight;
            RigPosition.Y = FloorY + (eyeBase + settings.HeightOffset) * Scale;
            HeadWorld = RigPosition + RotateYaw(headTrack * Scale, RigYaw);

            UpdateMatrices(world, head);
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
            var top = (sbyte)Math.Max(bp.GetFloorsUsed(), PlayerLevel);
            if (world.State.Level != top) world.State.Level = top;
        }

        // ------------------------------------------------------------------ lot geometry queries (tile space)

        /// <summary>The lot blueprint (World keeps it protected; VRLotWorld exposes it).</summary>
        public static Blueprint Lot(World world) => (world as VRLotWorld)?.LotBlueprint;

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

        /// <summary>Moves a circle of <see cref="BodyRadiusTiles"/> by <paramref name="delta"/>, sliding along walls.</summary>
        private Vector2 Collide(World world, Vector2 pos, Vector2 delta)
        {
            var bp = Lot(world);
            if (bp == null || PlayerLevel > bp.Stories) return pos + delta;
            const float r = BodyRadiusTiles;
            const float eps = 0.001f;

            // X axis
            if (delta.X != 0)
            {
                float nx = pos.X + delta.X;
                int rowMin = (int)Math.Floor(pos.Y - r), rowMax = (int)Math.Floor(pos.Y + r);
                if (delta.X > 0)
                {
                    int line = (int)Math.Floor(pos.X + r) + 1;
                    if (nx + r >= line)
                        for (int row = rowMin; row <= rowMax; row++)
                            if (WallOnVerticalLine(bp, line, row)) { nx = line - r - eps; break; }
                }
                else
                {
                    int line = (int)Math.Floor(pos.X - r);
                    if (nx - r <= line)
                        for (int row = rowMin; row <= rowMax; row++)
                            if (WallOnVerticalLine(bp, line, row)) { nx = line + r + eps; break; }
                }
                pos.X = nx;
            }

            // Y axis
            if (delta.Y != 0)
            {
                float ny = pos.Y + delta.Y;
                int colMin = (int)Math.Floor(pos.X - r), colMax = (int)Math.Floor(pos.X + r);
                if (delta.Y > 0)
                {
                    int line = (int)Math.Floor(pos.Y + r) + 1;
                    if (ny + r >= line)
                        for (int col = colMin; col <= colMax; col++)
                            if (WallOnHorizontalLine(bp, line, col)) { ny = line - r - eps; break; }
                }
                else
                {
                    int line = (int)Math.Floor(pos.Y - r);
                    if (ny - r <= line)
                        for (int col = colMin; col <= colMax; col++)
                            if (WallOnHorizontalLine(bp, line, col)) { ny = line + r + eps; break; }
                }
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

        // ------------------------------------------------------------------ matrices

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
                    var eyePos = RigPosition + RotateYaw(VRMath.ToXna(view.Pose.Position) * Scale, RigYaw);
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
    }
}
