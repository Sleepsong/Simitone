using FSO.Common.Rendering.Framework;
using FSO.Common.Rendering.Framework.Model;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Client.VR
{
    /// <summary>
    /// Wraps Simitone's UI layer while VR is running: the UI is drawn into its own transparent render target
    /// (<see cref="SimitoneVR.UITarget"/>), which VR shows as a floating panel (pie menus, dialogs) over the world,
    /// and is then composited onto the window as usual so the desktop view is unchanged.
    /// FreeSO's UI draw pass doesn't switch render targets itself (only its PreDraw does), so redirecting it is safe.
    /// </summary>
    public class VRUILayer : IGraphicsLayer
    {
        private readonly IGraphicsLayer Inner;
        private SpriteBatch Batch;

        public VRUILayer(IGraphicsLayer inner)
        {
            Inner = inner;
        }

        public void Initialize(GraphicsDevice device)
        {
            Inner.Initialize(device);
        }

        public void Update(UpdateState state)
        {
            Inner.Update(state);
        }

        public void PreDraw(GraphicsDevice device)
        {
            Inner.PreDraw(device);
        }

        public void Draw(GraphicsDevice device)
        {
            if (!SimitoneVR.Available)
            {
                Inner.Draw(device);
                return;
            }

            var pp = device.PresentationParameters;
            // MonoGame clears the window when you switch back to it if its usage is DiscardContents (the default),
            // which would wipe the world drawn before the UI. Set every frame: ApplyChanges replaces these parameters.
            pp.RenderTargetUsage = RenderTargetUsage.PreserveContents;
            var target = SimitoneVR.GetUITarget(device, pp.BackBufferWidth, pp.BackBufferHeight);
            device.SetRenderTarget(target);
            device.Clear(Color.Transparent);
            Inner.Draw(device);
            device.SetRenderTarget(null);

            Batch ??= new SpriteBatch(device);
            Batch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            Batch.Draw(target, Vector2.Zero, Color.White);
            Batch.End();
        }
    }
}
