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
        private VRComfortOverlay ComfortOverlay;
        private VRControllerRenderer Controllers;
        private readonly VRSimArms Arms = new VRSimArms();
        private BasicEffect IconEffect;
        private Texture2D BubbleBg, BubbleRim, BubbleCancel;
        private readonly VertexPositionColorTexture[] IconVerts = new VertexPositionColorTexture[6];
        private readonly VertexPositionColor[] BeamVerts = new VertexPositionColor[1024];
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
            ComfortOverlay = new VRComfortOverlay(layer.Device);
            Controllers = new VRControllerRenderer(layer.Device);
            IconEffect = new BasicEffect(layer.Device) { VertexColorEnabled = true, LightingEnabled = false, TextureEnabled = true };
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
            // your Sim's arms reach for the controllers while you steer them (after the game animated them this frame)
            if (VRCamera.SimAvatar != null) Arms.Apply(VRCamera.SimAvatar, VRCamera);
            Controllers?.Prepare(device, VRCamera, SimitoneVR.Runtime?.RenderModels);

            for (int eye = 0; eye < 2; eye++)
            {
                cam.Eye = eye;
                PPXDepthEngine.SetPPXTarget(targets[eye], null, true, State.OutsideColor);
                base.InternalDraw(device);
                DrawControllers(device, cam);
                DrawPointerBeam(device, cam);
                DrawActionBubble(device, cam);
                ComfortOverlay?.Draw(device, cam.Projection, cam.NearPlane, VRCamera.VignetteAmount, VRCamera.FadeAlpha);
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

        /// <summary>The controllers in your hands, when you're not in your Sim's body (where their hands are yours).</summary>
        private void DrawControllers(GraphicsDevice device, VREyeCamera cam) => Controllers?.Draw(device, VRCamera, cam.View, cam.Projection);

        /// <summary>
        /// The laser from the controller: a thin strip facing the eye with a dot where it ends. While placing
        /// something in buy/build mode it is the placement arc instead, green where the object fits and red where not.
        /// The movement arc (left trigger) is cyan, or red where you can't go, with a ring where it lands.
        /// </summary>
        private void DrawPointerBeam(GraphicsDevice device, VREyeCamera cam)
        {
            var vr = VRCamera;
            if (BeamEffect == null) return;
            var eyePos = Matrix.Invert(cam.View).Translation;
            var scale = SimitoneVR.Settings.WorldScale;
            var w = 0.002f * scale;
            int count = 0;
            Color colour = Color.White;
            Vector3 end = Vector3.Zero;
            bool dot = false;

            if (vr.BuildMode && vr.PlacementMode && vr.ArcCount > 1)
            {
                colour = vr.PlacementValid ? new Color(120, 255, 140) : new Color(255, 90, 80);
                count = AddArc(count, vr.ArcPoints, vr.ArcCount, eyePos, w, colour);
                end = vr.ArcPoints[vr.ArcCount - 1];
                dot = true;
            }
            else if (vr.PointerVisible)
            {
                var start = vr.PointerStart;
                end = start + vr.PointerDirection * vr.PointerLength;
                colour = vr.PointerHasTarget ? new Color(120, 255, 140) : new Color(230, 230, 255);
                count = AddSegment(count, start, end, eyePos, w, colour * 0.1f, colour);
                dot = count > 0;
            }

            if (dot)
            {
                // the dot: a small square facing the eye
                var forward = Vector3.Normalize(eyePos - end);
                var up = Math.Abs(forward.Y) > 0.95f ? Vector3.Forward : Vector3.Up;
                var right = Vector3.Normalize(Vector3.Cross(up, forward)) * 0.012f * scale;
                up = Vector3.Normalize(Vector3.Cross(forward, right)) * 0.012f * scale;
                count = AddQuad(count, end - right - up, end + right - up, end - right + up, end + right + up, colour);
            }

            if (vr.MoveArcCount > 1)
            {
                var arcColour = vr.MoveArcValid ? new Color(90, 210, 255) : new Color(255, 90, 80);
                count = AddArc(count, vr.MoveArcPoints, vr.MoveArcCount, eyePos, w * 1.4f, arcColour);
                count = AddFloorRing(count, vr.MoveArcPoints[vr.MoveArcCount - 1], 0.22f * scale, 0.025f * scale, arcColour);
            }
            if (count == 0) return;

            BeamEffect.World = Matrix.Identity;
            BeamEffect.View = cam.View;
            BeamEffect.Projection = cam.Projection;
            device.BlendState = BlendState.AlphaBlend;
            device.DepthStencilState = DepthStencilState.DepthRead;
            device.RasterizerState = RasterizerState.CullNone;
            foreach (var pass in BeamEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleList, BeamVerts, 0, count / 3);
            }
            device.DepthStencilState = DepthStencilState.Default;
        }

        /// <summary>
        /// While you're stepped out of your Sim: what they're doing, as the game's own queue icon (the blue disc, the
        /// action's picture and the selection rim, with the cancel mark while it's being cancelled) floating over their
        /// head. Point at it and pull the trigger to cancel it.
        /// </summary>
        private void DrawActionBubble(GraphicsDevice device, VREyeCamera cam)
        {
            var vr = VRCamera;
            var icon = vr.BubbleIcon;
            if (!vr.BubbleVisible || IconEffect == null || icon == null || icon.IsDisposed) return;
            BubbleBg ??= VRUI.CustomUI("int_big_bg.png");
            BubbleRim ??= VRUI.CustomUI("int_big_sel.png");
            BubbleCancel ??= VRUI.CustomUI("int_cancel.png");

            var eyePos = Matrix.Invert(cam.View).Translation;
            var centre = vr.BubblePosition;
            var forward = Vector3.Normalize(eyePos - centre);
            var right = Vector3.Normalize(Vector3.Cross(Math.Abs(forward.Y) > 0.95f ? Vector3.Forward : Vector3.Up, forward));
            var up = Vector3.Normalize(Vector3.Cross(forward, right));
            var px = vr.BubbleRadius / 61f; // the rim is 122 pixels across: world units per pixel of the queue art

            device.BlendState = BlendState.AlphaBlend;
            device.DepthStencilState = DepthStencilState.DepthRead;
            device.RasterizerState = RasterizerState.CullNone;
            device.SamplerStates[0] = SamplerState.LinearClamp;
            IconEffect.World = Matrix.Identity;
            IconEffect.View = cam.View;
            IconEffect.Projection = cam.Projection;

            // as UIInteraction draws the running action: disc (106 px), picture (74 px), rim (122 px), cancel mark (86 px)
            Billboard(device, BubbleBg, centre, right, up, 53 * px, new Color(104, 164, 184, 255), 1);
            var u1 = icon.Width / (float)icon.Height < 1.1f ? 1f : 0.5f; // wide icons hold two pictures: the first
            Billboard(device, icon, centre, right, up, 37 * px, Color.White, u1);
            Billboard(device, BubbleRim, centre, right, up, 61 * px, Color.White, 1);
            if (vr.BubbleCancelled) Billboard(device, BubbleCancel, centre, right, up, 43 * px, Color.White, 1);
            device.DepthStencilState = DepthStencilState.Default;
        }

        /// <summary>A square picture facing the eye, <paramref name="half"/> world units from its centre to each side.</summary>
        private void Billboard(GraphicsDevice device, Texture2D texture, Vector3 centre, Vector3 right, Vector3 up, float half, Color tint, float u1)
        {
            var tl = centre - right * half + up * half;
            var tr = centre + right * half + up * half;
            var bl = centre - right * half - up * half;
            var br = centre + right * half - up * half;
            IconVerts[0] = new VertexPositionColorTexture(tl, tint, new Vector2(0, 0));
            IconVerts[1] = new VertexPositionColorTexture(tr, tint, new Vector2(u1, 0));
            IconVerts[2] = new VertexPositionColorTexture(bl, tint, new Vector2(0, 1));
            IconVerts[3] = new VertexPositionColorTexture(bl, tint, new Vector2(0, 1));
            IconVerts[4] = new VertexPositionColorTexture(tr, tint, new Vector2(u1, 0));
            IconVerts[5] = new VertexPositionColorTexture(br, tint, new Vector2(u1, 1));
            IconEffect.Texture = texture;
            foreach (var pass in IconEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleList, IconVerts, 0, 2);
            }
        }

        /// <summary>An arc as strips, fading in from the controller.</summary>
        private int AddArc(int index, Vector3[] points, int pointCount, Vector3 eyePos, float halfWidth, Color colour)
        {
            for (int i = 0; i + 1 < pointCount; i++)
            {
                var alpha = 0.25f + 0.75f * (i + 1) / (pointCount - 1);
                index = AddSegment(index, points[i], points[i + 1], eyePos, halfWidth, colour * alpha, colour * alpha);
            }
            return index;
        }

        /// <summary>A flat ring lying on the floor (raised a hair so it isn't lost in it).</summary>
        private int AddFloorRing(int index, Vector3 centre, float radius, float width, Color colour)
        {
            const int segments = 20;
            centre.Y += 0.01f * SimitoneVR.Settings.WorldScale;
            for (int i = 0; i < segments; i++)
            {
                var a0 = MathHelper.TwoPi * i / segments;
                var a1 = MathHelper.TwoPi * (i + 1) / segments;
                var d0 = new Vector3((float)Math.Cos(a0), 0, (float)Math.Sin(a0));
                var d1 = new Vector3((float)Math.Cos(a1), 0, (float)Math.Sin(a1));
                index = AddQuad(index, centre + d0 * (radius - width), centre + d0 * radius,
                    centre + d1 * (radius - width), centre + d1 * radius, colour);
            }
            return index;
        }

        /// <summary>A quad (a, b, c, d: two corners along each edge) as two triangles.</summary>
        private int AddQuad(int index, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour)
        {
            if (index + 6 > BeamVerts.Length) return index;
            BeamVerts[index++] = new VertexPositionColor(a, colour);
            BeamVerts[index++] = new VertexPositionColor(b, colour);
            BeamVerts[index++] = new VertexPositionColor(c, colour);
            BeamVerts[index++] = new VertexPositionColor(c, colour);
            BeamVerts[index++] = new VertexPositionColor(b, colour);
            BeamVerts[index++] = new VertexPositionColor(d, colour);
            return index;
        }

        /// <summary>Adds a strip from a to b, turned to face the eye, as two triangles.</summary>
        private int AddSegment(int index, Vector3 a, Vector3 b, Vector3 eyePos, float halfWidth, Color colourA, Color colourB)
        {
            if (index + 6 > BeamVerts.Length - 6 * 24) return index; // leave room for the dot and the landing ring
            var side = Vector3.Cross(b - a, eyePos - (a + b) / 2);
            if (side.LengthSquared() < 1e-10f) return index;
            side = Vector3.Normalize(side) * halfWidth;
            BeamVerts[index++] = new VertexPositionColor(a - side, colourA);
            BeamVerts[index++] = new VertexPositionColor(a + side, colourA);
            BeamVerts[index++] = new VertexPositionColor(b - side, colourB);
            BeamVerts[index++] = new VertexPositionColor(b - side, colourB);
            BeamVerts[index++] = new VertexPositionColor(a + side, colourA);
            BeamVerts[index++] = new VertexPositionColor(b + side, colourB);
            return index;
        }

        public override void Dispose()
        {
            if (Immersive) VRCamera.OnDeactivated(this);
            if (SimitoneVR.CurrentWorld == this) SimitoneVR.CurrentWorld = null;
            MirrorBatch?.Dispose();
            BeamEffect?.Dispose();
            ComfortOverlay?.Dispose();
            Controllers?.Dispose();
            IconEffect?.Dispose();
            base.Dispose();
        }
    }
}
