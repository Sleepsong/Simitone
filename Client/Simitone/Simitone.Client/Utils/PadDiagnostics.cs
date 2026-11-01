using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// Writes gamepad.log in the user directory (next to crash.log), rewritten each run, so a pad that isn't picked up
    /// can be diagnosed without a console (e.g. started from Steam): how the game was launched (Steam's controller
    /// environment variables), every joystick SDL can see, and when the game's pad connects, disconnects, first sends
    /// input, or the window gains or loses focus (the pad is ignored while unfocused).
    /// </summary>
    public static class PadDiagnostics
    {
        public static string Path => System.IO.Path.Combine(FSOEnvironment.UserDir ?? "", "gamepad.log");

        private static bool Started;
        private static int LastJoysticks = -1;
        private static double NextJoystickCheck;
        private static bool LastConnected;
        private static int LastSlot = -1;
        private static bool LastFocused = true;
        private static bool SawInput;
        private static bool NoSDL; //SDL couldn't be called (e.g. a build without it): stop asking

        /// <summary>Starts the log: launch environment and SDL's joysticks.</summary>
        public static void Start()
        {
            if (Started) return;
            Started = true;
            var log = new StringBuilder();
            log.AppendLine($"Simitone gamepad log, {DateTime.Now:yyyy-MM-dd HH:mm:ss}, {RuntimeInformation.OSDescription}");
            log.AppendLine("Launch environment (Steam sets these when it starts the game):");
            var any = false;
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                var name = entry.Key as string ?? "";
                if (!(name.StartsWith("SDL_") || name.StartsWith("Steam") || name.StartsWith("STEAM_"))) continue;
                var value = entry.Value as string ?? "";
                if (name == "SDL_GAMECONTROLLERCONFIG")
                {
                    var lines = value.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    value = $"({lines.Length} mapping(s)) " + Shorten(value.Replace('\n', ' '), 300);
                }
                else value = Shorten(value, 300);
                log.AppendLine($"  {name}={value}");
                any = true;
            }
            if (!any) log.AppendLine("  (none: not started by Steam)");
            log.AppendLine($"Pad support enabled: {FSO.Common.Rendering.Framework.IO.GamePadSettings.Enabled}, " +
                $"ignore keyboard/mouse while held: {FSO.Common.Rendering.Framework.IO.GamePadSettings.IgnoreEchoes}");
            try
            {
                File.WriteAllText(Path, log.ToString());
            }
            catch
            {
                //diagnostics only
            }
        }

        /// <summary>Called each frame: logs changes.</summary>
        public static void Update(UpdateState state)
        {
            if (!Started) return;
            var pad = state.Pad;
            var now = state.Time?.TotalGameTime.TotalSeconds ?? 0;

            if (!NoSDL && now >= NextJoystickCheck)
            {
                NextJoystickCheck = now + 2;
                var count = SDL.JoystickCount();
                if (count == -2) NoSDL = true;
                if (count != LastJoysticks)
                {
                    LastJoysticks = count;
                    Write(SDL.Describe());
                }
            }

            if (state.WindowFocused != LastFocused)
            {
                LastFocused = state.WindowFocused;
                Write(LastFocused ? "Window focused" : "Window lost focus (pad input is ignored until it's back)");
            }

            if (pad.Connected != LastConnected)
            {
                LastConnected = pad.Connected;
                LastSlot = pad.PlayerIndex;
                SawInput = false;
                Write(pad.Connected ? $"Pad connected: slot {pad.PlayerIndex}, \"{pad.DisplayName}\"" : "Pad disconnected");
            }
            else if (pad.Connected && pad.PlayerIndex != LastSlot)
            {
                LastSlot = pad.PlayerIndex;
                SawInput = false;
                Write($"Switched to the pad in slot {pad.PlayerIndex}, \"{pad.DisplayName}\" (it's the one being used)");
            }
            if (pad.Connected && !SawInput && (pad.Active || pad.AnyButtonDown))
            {
                SawInput = true;
                Write("First input from the pad");
            }
        }

        private static void Write(string line)
        {
            try
            {
                File.AppendAllText(Path, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
            }
            catch
            {
                //diagnostics only
            }
        }

        private static string Shorten(string value, int max) => (value.Length > max) ? value.Substring(0, max) + "..." : value;

        /// <summary>What SDL (under MonoGame) sees, read straight from SDL so devices MonoGame skips show up too.</summary>
        private static class SDL
        {
            public static int JoystickCount()
            {
                try { return Native.NumJoysticks(); }
                catch { return -2; }
            }

            public static string Describe()
            {
                try
                {
                    var count = Native.NumJoysticks();
                    var text = new StringBuilder($"SDL sees {count} joystick(s)");
                    for (int i = 0; i < count; i++)
                    {
                        var name = Marshal.PtrToStringUTF8(Native.JoystickNameForIndex(i)) ?? "?";
                        text.Append($"{Environment.NewLine}  {i}: \"{name}\" vendor {Native.JoystickGetDeviceVendor(i):x4} product {Native.JoystickGetDeviceProduct(i):x4}, " +
                            (Native.IsGameController(i) ? "usable as a gamepad" : "NOT a recognised gamepad (no mapping), so MonoGame ignores it"));
                    }
                    if (count == 0) text.Append(": no controller is reaching the game at all");
                    return text.ToString();
                }
                catch (Exception e)
                {
                    return "Couldn't ask SDL about joysticks: " + e.GetType().Name;
                }
            }
        }

        private static class Native
        {
            private static readonly bool Windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            private static readonly bool Mac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

            public static int NumJoysticks() => Windows ? W.SDL_NumJoysticks() : Mac ? M.SDL_NumJoysticks() : L.SDL_NumJoysticks();
            public static IntPtr JoystickNameForIndex(int i) => Windows ? W.SDL_JoystickNameForIndex(i) : Mac ? M.SDL_JoystickNameForIndex(i) : L.SDL_JoystickNameForIndex(i);
            public static ushort JoystickGetDeviceVendor(int i) => Windows ? W.SDL_JoystickGetDeviceVendor(i) : Mac ? M.SDL_JoystickGetDeviceVendor(i) : L.SDL_JoystickGetDeviceVendor(i);
            public static ushort JoystickGetDeviceProduct(int i) => Windows ? W.SDL_JoystickGetDeviceProduct(i) : Mac ? M.SDL_JoystickGetDeviceProduct(i) : L.SDL_JoystickGetDeviceProduct(i);
            public static bool IsGameController(int i) => (Windows ? W.SDL_IsGameController(i) : Mac ? M.SDL_IsGameController(i) : L.SDL_IsGameController(i)) != 0;

            private static class L
            {
                private const string Lib = "libSDL2-2.0.so.0";
                [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_NumJoysticks();
                [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_JoystickNameForIndex(int index);
                [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ushort SDL_JoystickGetDeviceVendor(int index);
                [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ushort SDL_JoystickGetDeviceProduct(int index);
                [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_IsGameController(int index);
            }

            private static class M
            {
                private const string Lib = "libSDL2-2.0.0.dylib"; //as MonoGame ships it
                [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_NumJoysticks();
                [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_JoystickNameForIndex(int index);
                [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ushort SDL_JoystickGetDeviceVendor(int index);
                [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ushort SDL_JoystickGetDeviceProduct(int index);
                [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_IsGameController(int index);
            }

            private static class W
            {
                private const string Lib = "SDL2.dll";
                [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_NumJoysticks();
                [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr SDL_JoystickNameForIndex(int index);
                [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ushort SDL_JoystickGetDeviceVendor(int index);
                [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern ushort SDL_JoystickGetDeviceProduct(int index);
                [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern int SDL_IsGameController(int index);
            }
        }
    }
}
