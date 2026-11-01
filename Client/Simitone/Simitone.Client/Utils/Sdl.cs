using System;
using System.Runtime.InteropServices;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// The few SDL2 calls Simitone makes itself, beside MonoGame (which loads the same library): what SDL sees
    /// (gamepad.log), its window handle for the X11 focus check, and a hint. Throws if SDL can't be loaded.
    /// </summary>
    internal static class Sdl
    {
        private static readonly bool Windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static readonly bool Mac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static int NumJoysticks() => Windows ? W.SDL_NumJoysticks() : Mac ? M.SDL_NumJoysticks() : L.SDL_NumJoysticks();
        public static IntPtr JoystickNameForIndex(int i) => Windows ? W.SDL_JoystickNameForIndex(i) : Mac ? M.SDL_JoystickNameForIndex(i) : L.SDL_JoystickNameForIndex(i);
        public static ushort JoystickGetDeviceVendor(int i) => Windows ? W.SDL_JoystickGetDeviceVendor(i) : Mac ? M.SDL_JoystickGetDeviceVendor(i) : L.SDL_JoystickGetDeviceVendor(i);
        public static ushort JoystickGetDeviceProduct(int i) => Windows ? W.SDL_JoystickGetDeviceProduct(i) : Mac ? M.SDL_JoystickGetDeviceProduct(i) : L.SDL_JoystickGetDeviceProduct(i);
        public static bool IsGameController(int i) => (Windows ? W.SDL_IsGameController(i) : Mac ? M.SDL_IsGameController(i) : L.SDL_IsGameController(i)) != 0;
        public static string CurrentVideoDriver() => Marshal.PtrToStringUTF8(Windows ? W.SDL_GetCurrentVideoDriver() : Mac ? M.SDL_GetCurrentVideoDriver() : L.SDL_GetCurrentVideoDriver());
        public static bool SetHint(string name, string value) => (Windows ? W.SDL_SetHint(name, value) : Mac ? M.SDL_SetHint(name, value) : L.SDL_SetHint(name, value)) != 0;

        /// <summary>
        /// The X11 window behind an SDL window, or 0 if it isn't one. Reads SDL_SysWMinfo: version (3 bytes), then
        /// the subsystem (int, at 4; X11 is 2), then for X11 the Display* (at 8) and the Window (at 16).
        /// </summary>
        public static ulong X11Window(IntPtr sdlWindow)
        {
            if (Windows || Mac || sdlWindow == IntPtr.Zero) return 0;
            var version = new byte[3];
            L.SDL_GetVersion(version);
            var info = new byte[256]; //SDL_SysWMinfo is 72 bytes on 64-bit; room to spare
            Array.Copy(version, info, 3);
            if (L.SDL_GetWindowWMInfo(sdlWindow, info) == 0) return 0;
            if (BitConverter.ToInt32(info, 4) != 2) return 0;
            return BitConverter.ToUInt64(info, 16);
        }

        private static class L
        {
            private const string Lib = "libSDL2-2.0.so.0";
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_NumJoysticks();
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_JoystickNameForIndex(int index);
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ushort SDL_JoystickGetDeviceVendor(int index);
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ushort SDL_JoystickGetDeviceProduct(int index);
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_IsGameController(int index);
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GetCurrentVideoDriver();
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void SDL_GetVersion(byte[] version);
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_GetWindowWMInfo(IntPtr window, byte[] info);
        }

        private static class M
        {
            private const string Lib = "libSDL2-2.0.0.dylib"; //as MonoGame ships it
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_NumJoysticks();
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_JoystickNameForIndex(int index);
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ushort SDL_JoystickGetDeviceVendor(int index);
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ushort SDL_JoystickGetDeviceProduct(int index);
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_IsGameController(int index);
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GetCurrentVideoDriver();
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        }

        private static class W
        {
            private const string Lib = "SDL2.dll";
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_NumJoysticks();
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_JoystickNameForIndex(int index);
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ushort SDL_JoystickGetDeviceVendor(int index);
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ushort SDL_JoystickGetDeviceProduct(int index);
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_IsGameController(int index);
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_GetCurrentVideoDriver();
            [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        }
    }
}
