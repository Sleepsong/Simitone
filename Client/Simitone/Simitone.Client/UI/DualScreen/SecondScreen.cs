using FSO.Client;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Simitone.Client.UI.DualScreen
{
    /// <summary>
    /// The platform's side of a second screen: on Android, a second display such as the AYN Thor's bottom screen
    /// (or a monitor). It finds the screen, shows a window there, passes its touches on, and puts the images the
    /// game draws for it on that screen.
    /// </summary>
    public interface ISecondScreenHost
    {
        /// <summary>Whether the game wants the second screen (Options, Second Screen). Set every update.</summary>
        bool Wanted { set; }
        /// <summary>A window on the second screen is up and has a surface to draw on.</summary>
        bool Ready { get; }
        /// <summary>The second screen's drawing surface, in pixels.</summary>
        int Width { get; }
        int Height { get; }
        /// <summary>The next touch event on the second screen, in its pixels; false when there are no more.</summary>
        bool NextTouch(out int x, out int y, out bool down);
        /// <summary>Shows a newly drawn image of the second screen there. Game thread, after the frame is drawn.</summary>
        void Present(Texture2D image);
    }

    /// <summary>
    /// Simitone on two screens: the lot fills the main screen, and the second screen (<see cref="Host"/>) shows the
    /// controls (<see cref="UISecondScreen"/>). Without a host, or with Options > Second Screen off, nothing changes.
    /// </summary>
    public static class SecondScreen
    {
        /// <summary>Set by the platform when it can show a second screen (Android).</summary>
        public static ISecondScreenHost Host;
        public static UISecondScreen Panel { get; private set; }
        /// <summary>The second screen is showing the controls: the main screen leaves them out.</summary>
        public static bool Active => Panel != null && Panel.Shown;
        /// <summary>For the log: called with a line when the second screen starts or stops being used.</summary>
        public static Action<string> Note;

        /// <summary>Game thread, every update, before the UI updates.</summary>
        public static void Update()
        {
            if (Host == null || GameFacade.Screens == null) return;
            var wanted = GlobalSettings.Default.DualScreen;
            Host.Wanted = wanted;
            var ready = wanted && Host.Ready && Host.Width > 0 && Host.Height > 0;
            if (ready)
            {
                if (Panel == null || Panel.PixelWidth != Host.Width || Panel.PixelHeight != Host.Height)
                {
                    Panel?.Destroy();
                    Panel = new UISecondScreen(Host.Width, Host.Height);
                    Note?.Invoke($"second screen: {Host.Width}x{Host.Height} px, UI scale {Panel.UIScale:0.00}");
                }
                if (!Panel.Shown)
                {
                    Panel.Show();
                    Note?.Invoke("second screen: showing the controls");
                }
            }
            else if (Panel != null && Panel.Shown)
            {
                Panel.Hide();
                Note?.Invoke("second screen: not in use (" + (wanted ? "no screen" : "turned off in Options") + ")");
            }
        }

        /// <summary>Game thread, after the frame is drawn: a newly drawn second screen goes to the host.</summary>
        public static void Present()
        {
            if (Host == null || Panel == null || !Panel.Shown) return;
            if (!Panel.TakeNewFrame()) return;
            var image = Panel.Image;
            if (image != null && !image.IsDisposed) Host.Present(image);
        }
    }
}
