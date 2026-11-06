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

        private float PlacedDistance = 1, PlacedDrop, PlacedTilt;

        /// <summary>Puts the panel in front of the head, a little below eye level, tilted back by <paramref name="tiltDegrees"/>.</summary>
        public void PlaceInFront(VRPose head, float distance, float drop, float tiltDegrees)
        {
            PlacedDistance = distance;
            PlacedDrop = drop;
            PlacedTilt = tiltDegrees;
            var yaw = Quaternion.CreateFromAxisAngle(Vector3.Up, VRMath.Yaw(head.Orientation));
            var forward = Vector3.Transform(-Vector3.UnitZ, yaw);
            PanelOrientation = yaw * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.ToRadians(-tiltDegrees));
            PanelPosition = head.Position + forward * distance - new Vector3(0, drop, 0);
        }

        /// <summary>Puts it in front of the head again, as it was last placed (distance, height and tilt).</summary>
        public void BringInFront(VRPose head)
        {
            if (head.Valid) PlaceInFront(head, PlacedDistance, PlacedDrop, PlacedTilt);
        }

        /// <summary>How far round from where the head faces the panel is (degrees, about the vertical; 0 straight ahead).</summary>
        public float DegreesFromFacing(VRPose head)
        {
            if (!head.Valid) return 0;
            var to = PanelPosition - head.Position;
            to.Y = 0;
            if (to.LengthSquared() < 1e-6f) return 0;
            return Math.Abs(MathHelper.ToDegrees(MathHelper.WrapAngle(VRMath.Yaw(to) - VRMath.Yaw(head.Orientation))));
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

        /// <summary>The pixel a point (tracking space) is over, and how far in front of the panel it is (metres). False if it's off the panel.</summary>
        public bool PointAt(Vector3 point, out Vector2 pixel, out float distance)
        {
            var uv = VRMath.PointOnPlane(point, PanelPosition, PanelOrientation, WidthMetres, WidthMetres * Height / Width, out distance);
            pixel = new Vector2(uv.X * Width, uv.Y * Height);
            return Shown && VRMath.Inside(uv);
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
        /// <summary>
        /// A dot where the pointer is on the panel (a black-edged white square, green while the trigger's held): where
        /// there's no laser (over the flat screen it's in the hands layer, but this is exact), you see where you point.
        /// </summary>
        private void DrawCursor(uint swapImage)
        {
            if (mouse.X < 0 || mouse.Y < 0 || mouse.X >= Width || mouse.Y >= Height) return;
            int cx = mouse.X * Chain.Width / Math.Max(1, Width);
            int cy = Chain.Height - 1 - mouse.Y * Chain.Height / Math.Max(1, Height); // (GL's origin is the bottom left)
            int s = Math.Max(4, Math.Max(Chain.Width, Chain.Height) / 90);
            var down = mouse.LeftButton == ButtonState.Pressed;
            GLInterop.FillRects(swapImage, new (int, int, int, int, float, float, float)[]
            {
                (cx - s - 1, cy - s - 1, 2 * s + 2, 2 * s + 2, 0f, 0f, 0f),
                (cx - s + 1, cy - s + 1, 2 * s - 2, 2 * s - 2, down ? 0.4f : 1f, 1f, down ? 0.4f : 1f),
            });
        }

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
            DrawCursor(swapImage);
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
