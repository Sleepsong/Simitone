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
    ///    the VR eye targets, and one eye is mirrored to the desktop window.
    /// Everything else (lighting, walls, avatars, culling) is FreeSO's normal 3D path, run twice.
    /// </summary>
    public class VRLotWorld : World
    {
        public VRCameraController VRCamera { get; private set; }
        public Blueprint LotBlueprint => Blueprint;

        private SpriteBatch MirrorBatch;
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
        }

        public bool Immersive => State?.Cameras?.ActiveCamera == VRCamera && VRCamera != null;

        /// <summary>Switch between the immersive VR view and the normal camera (shown on the virtual screen).</summary>
        /// <param name="resume">When entering, put the player back where they were when they last left.</param>
        public void ToggleImmersive(bool resume = false)
        {
            if (VRCamera == null || !FSOEnvironment.Enable3D || !CanSwitchCameras || !Visible || !HasInit) return;
            VRCamera.ResumeOnNextActivate = resume && !Immersive;
            ToggleFirstPerson(CameraControllerType.FirstPerson);
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!HasInit || VRCamera == null) return;
            FramesSinceInit++;

            // Notice when the normal camera took over (Tab, F12, graphics mode change...).
            var active = State.Cameras.ActiveCamera;
            if (active != LastActiveCamera)
            {
                if (LastActiveCamera == VRCamera) VRCamera.OnDeactivated(this);
                LastActiveCamera = active;
            }

            // Drop straight into first person next to the selected Sim once the lot is ready, if the headset is in use.
            if (!AutoEntered && FramesSinceInit > 30 && Visible && SimitoneVR.SessionRunning && SimitoneVR.Settings.AutoEnterImmersive)
            {
                var focus = SimitoneVR.FocusProvider?.Invoke();
                if (focus != null || FramesSinceInit > 600)
                {
                    AutoEntered = true;
                    if (!Immersive) ToggleImmersive();
                    if (Immersive && focus != null) VRCamera.JumpTo(focus.Value.tile, focus.Value.level);
                }
            }
            // e.g. zoomed out to the neighbourhood: hand back to the normal camera (ToggleImmersive needs Visible).
            if (!Visible && Immersive) ToggleFirstPerson(CameraControllerType.FirstPerson);
        }

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
            }
            cam.Eye = -1;
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

        public override void Dispose()
        {
            if (Immersive) VRCamera.OnDeactivated(this);
            if (SimitoneVR.CurrentWorld == this) SimitoneVR.CurrentWorld = null;
            MirrorBatch?.Dispose();
            base.Dispose();
        }
    }
}
