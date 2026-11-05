using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace Simitone.VR
{
    public enum VRTurnMode { Snap, Smooth }
    public enum VRMoveDirection { Head, LeftHand }

    /// <summary>
    /// User tunable VR options, stored as key=value lines in &lt;UserDir&gt;/vr.ini.
    /// The file is (re)written with every key and its default on first run, so it doubles as documentation.
    /// </summary>
    public class VRSettings
    {
        /// <summary>Multiplier on the runtime's recommended per-eye resolution. Lower this on the Steam Frame if frame rate suffers.</summary>
        public float RenderScale = 1.0f;
        /// <summary>FreeSO world units per real-world metre. 3 = one TS1 tile per metre, i.e. life size.</summary>
        public float WorldScale = 3.0f;
        /// <summary>Walking speed in metres per second (thumbstick fully forward).</summary>
        public float MoveSpeed = 1.6f;
        /// <summary>Speed multiplier while the sprint button (left stick click) is held.</summary>
        public float SprintMultiplier = 2.5f;
        /// <summary>Which way "forward" is on the movement stick: where you look, or where the left controller points.</summary>
        public VRMoveDirection MoveDirection = VRMoveDirection.Head;
        public VRTurnMode TurnMode = VRTurnMode.Snap;
        public float SnapTurnDegrees = 30f;
        public float SmoothTurnDegreesPerSecond = 120f;
        /// <summary>Stop artificial locomotion at walls (doors stay passable). Physical room-scale movement is never blocked.</summary>
        public bool WallCollision = true;
        /// <summary>Automatically switch to the immersive first-person view when a lot finishes loading.</summary>
        public bool AutoEnterImmersive = true;
        /// <summary>Extra height in metres added to the tracked head height (e.g. 0.5 to play seated at standing height).</summary>
        public float HeightOffset = 0f;
        /// <summary>Eye height used if the runtime only offers a LOCAL (non floor-relative) reference space.</summary>
        public float FallbackEyeHeight = 1.65f;
        /// <summary>Virtual screen (menus, build/buy, CAS, neighbourhood) distance and width in metres.</summary>
        public float ScreenDistance = 2.0f;
        public float ScreenWidth = 2.4f;
        /// <summary>Which eye is mirrored to the desktop window: Left, Right or None.</summary>
        public string MirrorEye = "Left";
        /// <summary>Near clip plane in metres.</summary>
        public float NearClip = 0.05f;
        /// <summary>Thumbstick dead zone (0-1).</summary>
        public float DeadZone = 0.2f;
        /// <summary>
        /// Game update rate while in VR (Hz). Rendering is paced by the headset; updates catch up as needed, so
        /// simulation speed is correct whatever the headset refresh rate. Overridden by the -hzNN command line flag.
        /// </summary>
        public int UpdateRate = 90;

        public static VRSettings Default { get; private set; } = new VRSettings();

        private static string FilePath;

        /// <summary>
        /// Loads settings: built-in defaults, then vr.defaults.ini next to the executable (platform packages such
        /// as the Steam Frame build ship one), then the user's vr.ini.
        /// </summary>
        public static VRSettings Load(string userDir, Action<string> log = null)
        {
            var settings = new VRSettings();
            FilePath = Path.Combine(userDir ?? AppContext.BaseDirectory, "vr.ini");
            try
            {
                Apply(settings, Path.Combine(AppContext.BaseDirectory, "vr.defaults.ini"), log);
                Apply(settings, FilePath, log);
                settings.Save();
            }
            catch (Exception e)
            {
                log?.Invoke("vr.ini: could not read settings: " + e.Message);
            }
            Default = settings;
            return settings;
        }

        private static void Apply(VRSettings settings, string path, Action<string> log)
        {
            if (!File.Exists(path)) return;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            foreach (var field in typeof(VRSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!values.TryGetValue(field.Name, out var str)) continue;
                try
                {
                    object value;
                    if (field.FieldType.IsEnum) value = Enum.Parse(field.FieldType, str, true);
                    else value = Convert.ChangeType(str, field.FieldType, CultureInfo.InvariantCulture);
                    field.SetValue(settings, value);
                }
                catch (Exception e)
                {
                    log?.Invoke($"{Path.GetFileName(path)}: ignoring {field.Name}={str} ({e.Message})");
                }
            }
        }

        public void Save()
        {
            if (FilePath == null) return;
            try
            {
                var lines = new List<string>
                {
                    "# Simitone VR settings. Delete a line to restore its default.",
                    "# Options: TurnMode = Snap|Smooth, MoveDirection = Head|LeftHand, MirrorEye = Left|Right|None",
                };
                foreach (var field in typeof(VRSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    lines.Add(field.Name + "=" + Convert.ToString(field.GetValue(this), CultureInfo.InvariantCulture));
                }
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, lines);
            }
            catch { }
        }
    }
}
