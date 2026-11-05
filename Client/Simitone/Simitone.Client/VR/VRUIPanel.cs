using System;
using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Simitone.VR;
using Simitone.VR.Graphics;

namespace Simitone.Client.VR
{
    /// <summary>
    /// A panel of Simitone's own UI floating in the room: the VR menu, the comfort setup, the keyboard and the wrist
    /// displays. It is one of FreeSO's <see cref="UIExternalContainer"/>s, so the UI layer updates it and renders it
    /// into its own texture (not onto the window) with its own input, using the game's fonts, art and controls. The
    /// controller's ray is its mouse, and the texture goes to the headset as an OpenXR quad layer.
    /// </summary>
    public class VRUIPanel : UIExternalContainer
    {
        /// <summary>Where the panel floats, in tracking space (metres).</summary>
        public Vector3 PanelPosition;
        public Quaternion PanelOrientation = Quaternion.Identity;
        /// <summary>Physical width; the height follows the panel's aspect.</summary>
        public float WidthMetres;
        public bool Shown { get; private set; }

        private bool Registered;
        private VRSwapchain Chain;
        private static readonly MouseState Away = new MouseState(-10000, -10000, 0, ButtonState.Released, ButtonState.Released,
            ButtonState.Released, ButtonState.Released, ButtonState.Released);

        public VRUIPanel(int width, int height, float widthMetres) : base(width, height)
        {
            WidthMetres = widthMetres;
            NeedFrames = 0; // no CPU copy of each frame (that's for other kinds of external UI)
            ClearColor = Color.Transparent;
            Visible = false;
            mouse = Away;
        }

        public void Show()
        {
            if (!Registered && GameFacade.Screens != null)
            {
                GameFacade.Screens.AddExternal(this);
                Registered = true;
            }
            Shown = Visible = true;
            Invalidated = true;
        }

        public void Hide()
        {
            Shown = Visible = false;
            mouse = Away;
        }

        /// <summary>Called every update while shown, before the controls update: refresh what the panel shows.</summary>
        protected virtual void Refresh(UpdateState state) { }

        public override void Update(UpdateState state)
        {
            if (!Shown) return;
            Refresh(state);
            base.Update(state);
            Invalidated = true; // redraw every frame while shown: small panels, and it keeps animations moving
        }

        /// <summary>Puts the panel in front of the head, a little below eye level, tilted back by <paramref name="tiltDegrees"/>.</summary>
        public void PlaceInFront(VRPose head, float distance, float drop, float tiltDegrees)
        {
            var yaw = Quaternion.CreateFromAxisAngle(Vector3.Up, VRMath.Yaw(head.Orientation));
            var forward = Vector3.Transform(-Vector3.UnitZ, yaw);
            PanelOrientation = yaw * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.ToRadians(-tiltDegrees));
            PanelPosition = head.Position + forward * distance - new Vector3(0, drop, 0);
        }

        /// <summary>Where a controller ray meets the panel, in its pixels, and how far away (metres). False if it misses.</summary>
        public bool Hit(VRPose aim, out Vector2 pixel, out float distance)
        {
            pixel = Vector2.Zero;
            distance = 0;
            if (!Shown || !aim.Valid) return false;
            if (!VRMath.RayPlane(aim.Position, aim.Forward, PanelPosition, PanelOrientation, WidthMetres, WidthMetres * Height / Width,
                out var uv, out var t) || !VRMath.Inside(uv)) return false;
            pixel = new Vector2(uv.X * Width, uv.Y * Height);
            distance = t;
            return true;
        }

        /// <summary>The panel's mouse: at a pixel (or away from the panel), with the trigger and grip as its buttons.</summary>
        public void SetMouse(Vector2? pixel, bool left, bool right)
        {
            if (pixel == null)
            {
                mouse = Away;
                return;
            }
            mouse = new MouseState((int)pixel.Value.X, (int)pixel.Value.Y, 0,
                left ? ButtonState.Pressed : ButtonState.Released, ButtonState.Released,
                right ? ButtonState.Pressed : ButtonState.Released, ButtonState.Released, ButtonState.Released);
        }

        /// <summary>The panel's current image (rendered by the UI layer), or null before its first frame.</summary>
        public RenderTarget2D Image => Target;

        /// <summary>Copies the panel into its swapchain and adds it to the frame, while it's shown.</summary>
        public void Submit(VRRuntime runtime, ref VRFrameSubmission submission)
        {
            var image = Target;
            if (!Shown || image == null || image.IsDisposed) return;
            if (Chain == null || Chain.Width != image.Width || Chain.Height != image.Height)
            {
                Chain?.Dispose();
                Chain = runtime.CreateSwapchain(image.Width, image.Height);
            }
            var swapImage = Chain.Acquire();
            GLInterop.BlitTexture(GLInterop.GetTextureHandle(image), image.Width, image.Height, swapImage, 0, 0, Chain.Width, Chain.Height, flipY: true);
            Chain.Release();
            submission.AddQuad(new VRQuad
            {
                Swapchain = Chain,
                Pose = VRMath.Pose(PanelPosition, PanelOrientation),
                Width = WidthMetres,
                Height = WidthMetres * image.Height / image.Width,
                Transparent = true
            });
        }

        /// <summary>Takes the panel out of the UI layer and frees its swapchain.</summary>
        public void Destroy()
        {
            Hide();
            if (Registered) GameFacade.Screens?.RemoveExternal(this);
            Registered = false;
            Chain?.Dispose();
            Chain = null;
        }
    }
}
