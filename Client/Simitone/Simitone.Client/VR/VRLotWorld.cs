using FSO.Common;
using FSO.Common.Rendering.Framework;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using FSO.LotView;
using FSO.LotView.Model;
using FSO.LotView.Utils.Camera;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Simitone.Client.VR
{
    /// <summary>
    /// The lot world used when Simitone runs with -vr. Identical to FreeSO's World except that:
    ///  - the first-person camera slot holds a <see cref="VRCameraController"/>;
    ///  - while that camera is active and the headset wants a frame, the scene is rendered once per eye into
    ///    the VR eye targets (with the controller's pointer beam), and one eye is mirrored to the desktop window;
    ///  - it can pick what the controller points at, using FreeSO's own 3D picking.
    /// Everything else (lighting, walls, avatars, culling) is FreeSO's normal 3D path, run twice.
    /// </summary>
    public class VRLotWorld : World
    {
        public VRCameraController VRCamera { get; private set; }
        public Blueprint LotBlueprint => Blueprint;

        private SpriteBatch MirrorBatch;
        private BasicEffect BeamEffect;
        private readonly VertexPositionColor[] BeamVerts = new VertexPositionColor[12];
        private ICameraController LastActiveCamera;
        private int FramesSinceInit;
        private bool AutoEntered;

        public VRLotWorld(GraphicsDevice device) : base(device)
        {
        }

        public override void Initialize(_3DLayer layer)
        {
            base.Initialize(layer);
            SimitoneVR.CurrentWorld = this;
            // Without a working OpenXR session keep FreeSO's own (mouse-look) first-person camera.
            if (!SimitoneVR.Available) return;
            var cameras = State.Cameras;
            VRCamera = new VRCameraController(layer.Device, State);
            var index = cameras.Cameras.IndexOf(cameras.CameraFirstPerson);
            if (index >= 0) cameras.Cameras[index] = VRCamera;
            cameras.CameraFirstPerson = VRCamera;
            MirrorBatch = new SpriteBatch(layer.Device);
            BeamEffect = new BasicEffect(layer.Device) { VertexColorEnabled = true, LightingEnabled = false, TextureEnabled = false };
        }

        public bool Immersive => VRCamera != null && State?.Cameras?.ActiveCamera == VRCamera;

        /// <summary>Switch between the immersive VR view and the normal camera (shown on the virtual screen).</summary>
        /// <param name="resume">When entering, put the player back where they were when they last left.</param>
        public void ToggleImmersive(bool resume = false)
        {
            if (VRCamera == null || !FSOEnvironment.Enable3D || !CanSwitchCameras || !HasInit) return;
            if (Immersive)
            {
                ExitImmersive();
                return;
            }
            if (!Visible) return;
            VRCamera.ResumeOnNextActivate = resume;
            ToggleFirstPerson(CameraControllerType.FirstPerson);
            CheckCameraChange();
        }

        /// <summary>Hands the view back to the normal camera (works even if the lot is hidden).</summary>
        public void ExitImmersive()
        {
            if (!Immersive) return;
            ToggleFirstPerson(CameraControllerType.FirstPerson);
            CheckCameraChange();
        }

        /// <summary>
        /// Notices when another camera took over (Tab, F12, a graphics mode change, our own toggles) and lets the VR
        /// camera restore the normal view. Called from several places so that two switches between checks can't
        /// slip past.
        /// </summary>
        private void CheckCameraChange()
        {
            if (VRCamera == null || State?.Cameras == null) return;
            var active = State.Cameras.ActiveCamera;
            if (active != VRCamera && VRCamera.Active) VRCamera.OnDeactivated(this);
            LastActiveCamera = active;
        }

        public override void Update(UpdateState state)
        {
            CheckCameraChange(); // camera switches queued with GameThread.NextUpdate ran just before this
            base.Update(state);
            if (!HasInit || VRCamera == null) { CheckCameraChange(); return; }
            FramesSinceInit++;

            // Drop straight into first person once the lot is ready, if the headset is in use.
            if (!AutoEntered && FramesSinceInit > 30 && Visible && SimitoneVR.SessionRunning && SimitoneVR.Settings.AutoEnterImmersive)
            {
                var focus = SimitoneVR.FocusProvider?.Invoke();
                if (focus != null || FramesSinceInit > 600)
                {
                    AutoEntered = true;
                    if (!Immersive && SimitoneVR.WantsImmersiveOnLot) ToggleImmersive();
                    if (Immersive && focus != null) VRCamera.JumpTo(focus.Value.tile, focus.Value.level);
                }
            }
            // e.g. zoomed out to the neighbourhood: hand back to the normal camera.
            if (!Visible && Immersive) ExitImmersive();
            CheckCameraChange();
        }

        public override void PreDraw(GraphicsDevice device)
        {
            CheckCameraChange();
            base.PreDraw(device);
        }

        // ------------------------------------------------------------------ pointer picking

        /// <summary>
        /// The object or Sim the controller points at (0 for none), using FreeSO's own picking: the pointer camera
        /// looks down the ray, and we ask for the object at the centre of the screen.
        /// </summary>
        public short PickPointed()
        {
            if (!Immersive || !HasInit) return 0;
            var cam = VRCamera.EyeCamera;
            var cameras = State.Cameras;
            var device = m_Device;
            var viewport = device.Viewport;
            VRCamera.AimPointerCamera(viewport);
            var oldTransitions = cameras.DisableTransitions;
            cameras.DisableTransitions = true;
            cam.Eye = VREyeCamera.PointerEye;
            short id;
            try
            {
                id = GetObjectIDAtScreenPos(viewport.Width / 2, viewport.Height / 2, device);
            }
            finally
            {
                cam.Eye = VREyeCamera.MonoEye;
                cameras.DisableTransitions = oldTransitions;
            }
            return id;
        }

        // ------------------------------------------------------------------ drawing

        protected override void InternalDraw(GraphicsDevice device)
        {
            if (!Immersive || !SimitoneVR.WantsEyes)
            {
                base.InternalDraw(device);
                return;
            }

            var targets = SimitoneVR.GetEyeTargets(device);
            var cam = VRCamera.EyeCamera;
            var cameras = State.Cameras;
            var oldTransitions = cameras.DisableTransitions;
            cameras.DisableTransitions = true; // never blend another camera into an eye matrix

            for (int eye = 0; eye < 2; eye++)
            {
                cam.Eye = eye;
                PPXDepthEngine.SetPPXTarget(targets[eye], null, true, State.OutsideColor);
                base.InternalDraw(device);
                DrawPointerBeam(device, cam);
            }
            cam.Eye = VREyeCamera.MonoEye;
            cameras.DisableTransitions = oldTransitions;
            SimitoneVR.EyesRendered = true;

            // Desktop mirror: crop one eye to the window (World.Draw then presents the PPX backbuffer).
            PPXDepthEngine.SetPPXTarget(null, null, true);
            var mirror = SimitoneVR.MirrorEye;
            if (mirror >= 0)
            {
                var vp = device.Viewport;
                var src = targets[mirror];
                var scale = Math.Max(vp.Width / (float)src.Width, vp.Height / (float)src.Height);
                var size = new Vector2(src.Width, src.Height) * scale;
                var pos = (new Vector2(vp.Width, vp.Height) - size) / 2;
                MirrorBatch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
                MirrorBatch.Draw(src, new Rectangle(pos.ToPoint(), size.ToPoint()), Color.White);
                MirrorBatch.End();
            }
        }

        /// <summary>The laser from the controller: a thin strip facing the eye, plus a dot where it ends.</summary>
        private void DrawPointerBeam(GraphicsDevice device, VREyeCamera cam)
        {
            var vr = VRCamera;
            if (!vr.PointerVisible || BeamEffect == null) return;
            var start = vr.PointerStart;
            var end = start + vr.PointerDirection * vr.PointerLength;
            var eyePos = Matrix.Invert(cam.View).Translation;
            var scale = SimitoneVR.Settings.WorldScale;
            var colour = vr.PointerHasTarget ? new Color(120, 255, 140) : new Color(230, 230, 255);

            var toEye = eyePos - (start + end) / 2;
            var side = Vector3.Cross(end - start, toEye);
            if (side.LengthSquared() < 1e-8f) return;
            side.Normalize();
            var w = 0.002f * scale;
            var faded = colour * 0.1f;
            BeamVerts[0] = new VertexPositionColor(start - side * w, faded);
            BeamVerts[1] = new VertexPositionColor(start + side * w, faded);
            BeamVerts[2] = new VertexPositionColor(end - side * w, colour);
            BeamVerts[3] = new VertexPositionColor(end + side * w, colour);
            BeamVerts[4] = BeamVerts[2];
            BeamVerts[5] = BeamVerts[1];

            // the dot: a small square facing the eye
            var forward = Vector3.Normalize(eyePos - end);
            var up = Math.Abs(forward.Y) > 0.95f ? Vector3.Forward : Vector3.Up;
            var right = Vector3.Normalize(Vector3.Cross(up, forward)) * 0.012f * scale;
            up = Vector3.Normalize(Vector3.Cross(forward, right)) * 0.012f * scale;
            BeamVerts[6] = new VertexPositionColor(end - right - up, colour);
            BeamVerts[7] = new VertexPositionColor(end + right - up, colour);
            BeamVerts[8] = new VertexPositionColor(end - right + up, colour);
            BeamVerts[9] = BeamVerts[8];
            BeamVerts[10] = BeamVerts[7];
            BeamVerts[11] = new VertexPositionColor(end + right + up, colour);

            BeamEffect.World = Matrix.Identity;
            BeamEffect.View = cam.View;
            BeamEffect.Projection = cam.Projection;
            device.BlendState = BlendState.AlphaBlend;
            device.DepthStencilState = DepthStencilState.DepthRead;
            device.RasterizerState = RasterizerState.CullNone;
            foreach (var pass in BeamEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleList, BeamVerts, 0, 4);
            }
            device.DepthStencilState = DepthStencilState.Default;
        }

        public override void Dispose()
        {
            if (Immersive) VRCamera.OnDeactivated(this);
            if (SimitoneVR.CurrentWorld == this) SimitoneVR.CurrentWorld = null;
            MirrorBatch?.Dispose();
            BeamEffect?.Dispose();
            base.Dispose();
        }
    }
}
