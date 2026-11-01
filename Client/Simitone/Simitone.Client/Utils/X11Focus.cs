using System;
using System.Runtime.InteropServices;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// A second opinion on window focus for X11 (including XWayland). SDL 2 ignores focus events that arrive during a
    /// keyboard grab (as a window switcher's Alt+Tab can), so after tabbing back it can keep believing the game is in
    /// the background, and while it does, the game ignores the pad and SDL stops reading it. This asks the X server
    /// which window is active (the root window's _NET_ACTIVE_WINDOW, kept by the window manager) and compares it with
    /// the game's. Only the root window is queried, which can't fail, so no X error can end the process.
    /// </summary>
    public static class X11Focus
    {
        private static IntPtr Display;
        private static ulong OurWindow;
        private static ulong Root;
        private static ulong ActiveAtom;
        private static bool Ready;
        private static long NextQuery;
        private static bool? LastAnswer;

        /// <summary>For gamepad.log: whether the check is running, or why not.</summary>
        public static string Status { get; private set; } = "not started";

        /// <param name="sdlWindow">MonoGame's window handle (an SDL_Window*).</param>
        public static void Start(IntPtr sdlWindow)
        {
            if (Ready) return;
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) { Status = "not needed (not Linux)"; return; }
                if (Sdl.CurrentVideoDriver() != "x11") { Status = "not used (SDL isn't on X11)"; return; }
                OurWindow = Sdl.X11Window(sdlWindow);
                if (OurWindow == 0) { Status = "unavailable (no X11 window from SDL)"; return; }
                Display = XOpenDisplay(IntPtr.Zero);
                if (Display == IntPtr.Zero) { Status = "unavailable (couldn't open the X display)"; return; }
                Root = XDefaultRootWindow(Display);
                ActiveAtom = XInternAtom(Display, "_NET_ACTIVE_WINDOW", 0);
                Ready = ActiveAtom != 0;
                Status = Ready ? $"on (game window 0x{OurWindow:x})" : "unavailable (no _NET_ACTIVE_WINDOW)";
            }
            catch (Exception e)
            {
                Status = "unavailable (" + e.GetType().Name + ")";
            }
        }

        /// <summary>
        /// Whether the X server says the game's window is the active one: null if unknown. Asked at most four times a
        /// second; in between the last answer stands.
        /// </summary>
        public static bool? ActiveWindowIsOurs()
        {
            if (!Ready) return null;
            var now = Environment.TickCount64;
            if (now < NextQuery) return LastAnswer;
            NextQuery = now + 250;
            try
            {
                LastAnswer = null;
                if (XGetWindowProperty(Display, Root, ActiveAtom, 0, 1, 0, 0, out _, out var format, out var items, out _, out var data) == 0
                    && data != IntPtr.Zero)
                {
                    if (format == 32 && items >= 1) LastAnswer = (ulong)Marshal.ReadInt64(data) == OurWindow;
                    XFree(data);
                }
            }
            catch
            {
                Ready = false;
                Status = "stopped after an error";
                LastAnswer = null;
            }
            return LastAnswer;
        }

        /// <summary>For FSO.Common.Rendering.Framework.Game.FocusFallback: focused if X says so.</summary>
        public static bool Fallback() => ActiveWindowIsOurs() == true;

        private const string X11 = "libX11.so.6";
        [DllImport(X11)] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport(X11)] private static extern ulong XDefaultRootWindow(IntPtr display);
        [DllImport(X11)] private static extern ulong XInternAtom(IntPtr display, string name, int onlyIfExists);
        [DllImport(X11)] private static extern int XGetWindowProperty(IntPtr display, ulong window, ulong property, long offset, long length,
            int delete, ulong requestedType, out ulong actualType, out int actualFormat, out ulong items, out ulong bytesAfter, out IntPtr data);
        [DllImport(X11)] private static extern int XFree(IntPtr data);
    }
}
