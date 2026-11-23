using FSO.Client.UI.Framework;
using FSO.Common.Rendering.Framework.IO;
using Microsoft.Xna.Framework;

namespace Simitone.Client.UI.Controls
{
    /// <summary>
    /// Covers the screen and takes every click, so nothing below it can be pressed. Draws nothing.
    /// Co-op uses it while another player has control of a screen; anything added after it still works.
    /// Only while Visible.
    /// </summary>
    public class UICoopBlocker : UIElement
    {
        private UIMouseEventRef Area;

        public UICoopBlocker()
        {
            Resize();
        }

        public void Resize()
        {
            if (Area != null) RemoveMouseListener(Area);
            var screen = UIScreen.Current;
            Area = ListenForMouse(new Rectangle(0, 0, (screen?.ScreenWidth ?? 4096) + 1, (screen?.ScreenHeight ?? 4096) + 1), (evt, state) => { });
        }

        public override void Draw(UISpriteBatch batch)
        {
        }
    }
}
