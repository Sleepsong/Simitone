using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common;
using Microsoft.Xna.Framework;
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
        /// <summary>
        /// Whether the game wants the screens swapped (Options, Swap Screens): the game on the other screen, and the
        /// controls on the device's own. Set every update; the host swaps while the game runs.
        /// </summary>
        bool SwapWanted { set; }
        /// <summary>The screens are swapped now: <see cref="Width"/> and <see cref="Height"/> are the device's screen's.</summary>
        bool Swapped { get; }
        /// <summary>A window on the second screen is up and has a surface to draw on.</summary>
        bool Ready { get; }
        /// <summary>The controls' drawing surface (the second screen's, or the device's own while swapped), in pixels.</summary>
        int Width { get; }
        int Height { get; }
        /// <summary>The next touch event on the second screen, in its pixels; false when there are no more.</summary>
        bool NextTouch(out int x, out int y, out bool down);
        /// <summary>
        /// Game thread, after each frame is drawn: shows a newly drawn image of the controls (null if there's none), and,
        /// while swapped, the game's frame on the other screen.
        /// </summary>
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
        /// <summary>
        /// The screens are swapped (Options, Swap Screens): the game is on the other screen (the Thor's bottom screen, or
        /// a TV or monitor), and the controls on the device's own. As of the last update.
        /// </summary>
        public static bool Swapped { get; private set; }

        /// <summary>
        /// The pad's focus on the second screen, from a main-screen navigator whose focus moved there (see
        /// <see cref="InstallPadHooks"/>): the second screen draws its outline. With the update it was set in.
        /// </summary>
        public static UIFocusNavigator ElsewhereFocus;
        public static int ElsewhereFocusFrame = -1;
        public static int Frame;

        /// <summary>Whether an element is on the second screen (its topmost parent is <see cref="Panel"/>).</summary>
        public static bool IsOnSecondScreen(UIElement element)
        {
            var panel = Panel;
            if (panel == null) return false;
            for (var e = element; e != null; e = e.Parent)
            {
                if (e == panel) return true;
            }
            return false;
        }

        /// <summary>
        /// Lets the pad move between the screens: for moving between elements, the second screen sits just below the
        /// main one, centred and at the same scale, so down from the main screen's lowest element reaches the second
        /// screen's top. Outlines are drawn on whichever screen the focus is on.
        /// </summary>
        private static void InstallPadHooks()
        {
            UIFocusNavigator.ToNavSpace = (element, rect) =>
            {
                if (!Active || !IsOnSecondScreen(element)) return rect;
                var main = GameFacade.Screens?.CurrentUIScreen;
                if (main == null) return rect;
                //second screen pixels -> its UI units -> main screen UI units, below it -> main screen pixels
                var bottomScale = Panel.UIScale;
                var dpi = FSOEnvironment.DPIScaleFactor;
                var left = (main.ScreenWidth - Panel.LogicalWidth) / 2f;
                var top = main.ScreenHeight + 8;
                var x = (left + rect.X / bottomScale) * dpi;
                var y = (top + rect.Y / bottomScale) * dpi;
                return new Rectangle((int)x, (int)y, (int)(rect.Width / bottomScale * dpi), (int)(rect.Height / bottomScale * dpi));
            };
            UIFocusNavigator.DrawsHere = (element) => !Active || !IsOnSecondScreen(element);
            UIFocusNavigator.DrawElsewhere = (navigator) =>
            {
                ElsewhereFocus = navigator;
                ElsewhereFocusFrame = Frame;
            };
        }
        private static bool HooksInstalled;

        /// <summary>Game thread, every update, before the UI updates.</summary>
        public static void Update()
        {
            Frame++;
            if (Host == null || GameFacade.Screens == null) return;
            if (!HooksInstalled)
            {
                InstallPadHooks();
                HooksInstalled = true;
            }
            var wanted = GlobalSettings.Default.DualScreen;
            Host.Wanted = wanted;
            Host.SwapWanted = wanted && GlobalSettings.Default.DualScreenSwap;
            Swapped = Host.Swapped;
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

        /// <summary>
        /// Game thread, after the frame is drawn: a newly drawn image of the controls goes to the host, which also takes
        /// the game's frame while the screens are swapped.
        /// </summary>
        public static void Present()
        {
            if (Host == null) return;
            Texture2D image = null;
            if (Panel != null && Panel.Shown && Panel.TakeNewFrame())
            {
                image = Panel.Image;
                if (image != null && image.IsDisposed) image = null;
            }
            Host.Present(image);
        }
    }
}
