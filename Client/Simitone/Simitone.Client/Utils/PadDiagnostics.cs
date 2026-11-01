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
        private static bool PadUsedInBackground; //logged once per time in the background
        private static double FocusBackAt = -1; //when focus last came back, until the pad is next used
        private static bool QuietLogged;
        private static bool LastFallback;

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
            //Steam tells whether the game is in front through its overlay, and switches the controller to the game's
            //layout only then; without it the Desktop Layout (stick as mouse, triggers as clicks) stays on
            log.AppendLine("Steam overlay and display (without the overlay, Steam keeps the controller on its Desktop Layout):");
            var preload = Environment.GetEnvironmentVariable("LD_PRELOAD") ?? "";
            log.AppendLine($"  Steam asked to load the overlay (LD_PRELOAD): {(preload.Contains("gameoverlayrenderer") ? "yes" : "no")}" +
                ((preload.Length > 0) ? $" [{Shorten(preload, 300)}]" : ""));
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var loaded = false;
                try
                {
                    foreach (var line in File.ReadLines("/proc/self/maps"))
                    {
                        if (line.Contains("gameoverlayrenderer")) { loaded = true; break; }
                    }
                }
                catch { }
                log.AppendLine($"  Overlay library loaded in the game: {(loaded ? "yes" : "no")}");
                log.AppendLine($"  Session: {Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "?"}, " +
                    $"DISPLAY={Environment.GetEnvironmentVariable("DISPLAY") ?? "(unset)"}, " +
                    $"WAYLAND_DISPLAY={Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") ?? "(unset)"}");
            }
            log.AppendLine($"  SDL video driver: {SDL.VideoDriver()}");
            log.AppendLine($"  X11 focus check: {X11Focus.Status}");
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
                var x11 = X11Focus.ActiveWindowIsOurs();
                var x11Text = (x11 == null) ? "" : (x11.Value ? "; X11 says Simitone is the active window" : "; X11 says another window is active");
                Write((LastFocused ? "Window focused" : "Window lost focus (pad input is ignored until it's back)") + x11Text);
                PadUsedInBackground = false;
                FocusBackAt = LastFocused ? now : -1;
                QuietLogged = false;
            }
            //SDL missed the focus coming back (see X11Focus): the game is going by the X server instead
            var fallback = state.WindowFocused && !(FSO.Client.GameFacade.Game?.IsActive ?? true);
            if (fallback != LastFallback)
            {
                LastFallback = fallback;
                Write(fallback ? "SDL still thinks the window is in the background, but X11 says it's active: using the pad anyway"
                    : "SDL agrees about focus again");
            }
            if (!state.WindowFocused && pad.UsedWhileUnfocused && !PadUsedInBackground)
            {
                PadUsedInBackground = true;
                Write("The pad was used while the game thought it was in the background" +
                    ((X11Focus.ActiveWindowIsOurs() == true) ? " (X11 says Simitone IS the active window)" : ""));
            }
            if (FocusBackAt >= 0 && pad.Connected)
            {
                if (pad.Active || pad.AnyButtonDown)
                {
                    if (now - FocusBackAt > 0.5) Write($"Pad input again, {now - FocusBackAt:0.0} s after focus came back");
                    FocusBackAt = -1;
                }
                else if (!QuietLogged && now - FocusBackAt > 15)
                {
                    QuietLogged = true;
                    Write("No pad input in the 15 s since focus came back (if you were pressing buttons, Steam may have kept the controller on its Desktop Layout)");
                }
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
            public static string VideoDriver()
            {
                try { return Sdl.CurrentVideoDriver() ?? "(none)"; }
                catch (Exception e) { return "unknown (" + e.GetType().Name + ")"; }
            }

            public static int JoystickCount()
            {
                try { return Sdl.NumJoysticks(); }
                catch { return -2; }
            }

            public static string Describe()
            {
                try
                {
                    var count = Sdl.NumJoysticks();
                    var text = new StringBuilder($"SDL sees {count} joystick(s)");
                    for (int i = 0; i < count; i++)
                    {
                        var name = Marshal.PtrToStringUTF8(Sdl.JoystickNameForIndex(i)) ?? "?";
                        text.Append($"{Environment.NewLine}  {i}: \"{name}\" vendor {Sdl.JoystickGetDeviceVendor(i):x4} product {Sdl.JoystickGetDeviceProduct(i):x4}, " +
                            (Sdl.IsGameController(i) ? "usable as a gamepad" : "NOT a recognised gamepad (no mapping), so MonoGame ignores it"));
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
    }
}
