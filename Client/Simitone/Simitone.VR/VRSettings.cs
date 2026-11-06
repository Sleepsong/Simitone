using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace Simitone.VR
{
    public enum VRTurnMode { Snap, Smooth }
    public enum VRMoveDirection { Head, LeftHand }
    /// <summary>Sim: see through your Sim's eyes and walk them. Observer: roam the lot freely as yourself.</summary>
    /// <summary>
    /// First person as your Sim, first person roaming freely, or the dollhouse: the lot as a model on a table in front
    /// of you, a giant over it who moves furniture and Sims about with their hands.
    /// </summary>
    public enum VRPlayMode { Sim, Observer, Dollhouse }
    public enum VRWristMode { Auto, Always, Off }
    /// <summary>
    /// What your view does while your Sim acts on their own (walking to an interaction, sitting down, sleeping):
    /// Comfortable steps out and follows them with blinks, Moderate glides after them in third person at walking
    /// speed, Intense rides along in their head (with a vignette).
    /// </summary>
    public enum VRSimActionView { Comfortable, Moderate, Intense }
    public enum VRVignetteStrength { Off, Low, Medium, High }
    /// <summary>
    /// Stick: the left stick walks. Arc: hold the left trigger, aim the arc and let go (your Sim walks there; as an
    /// observer you teleport there). StickAndArc: both.
    /// </summary>
    public enum VRMovement { StickAndArc, Stick, Arc }
    /// <summary>
    /// Foveated rendering: the edges of your view are drawn at lower resolution and only where you look (or, without
    /// eye tracking, the middle) at full. Light: 70% resolution outside a 48 degree centre; Strong: 50% outside 40.
    /// </summary>
    public enum VRFoveation { Off, Light, Strong }

    /// <summary>
    /// Where the buy/build catalogue is in first person. Hand: a tablet in your walking hand, with 3D pictures of the
    /// objects. Desk: the game's own catalogue on a panel in front of you, below eye level.
    /// </summary>
    public enum VRCatalogStyle { Hand, Desk }

    /// <summary>
    /// The walls in buy/build mode, as the game's wall buttons: cut away around where you're placing (not right next to
    /// you), all down, or all up.
    /// </summary>
    public enum VRWallView { Cutaway, Down, Up }

    /// <summary>Observer mode on stairs: ride up or down them at walking speed, or blink to the other end.</summary>
    public enum VRStairs { Ride, Blink }

    /// <summary>
    /// User tunable VR options, stored as key=value lines in &lt;UserDir&gt;/vr.ini.
    /// The file is (re)written with every key and its default on first run, so it doubles as documentation.
    /// </summary>
    public class VRSettings
    {
        /// <summary>How you start on a lot: as your Sim (first person, walking them) or as a free observer.</summary>
        public VRPlayMode PlayMode = VRPlayMode.Sim;
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
        /// <summary>
        /// Observer: walls and furniture stop the stick (doors don't, and stairs take you up or down). Physical
        /// room-scale movement is never blocked.
        /// </summary>
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
        /// <summary>
        /// Buy and build mode in first person: pick objects up from a distance with the pointer and place them where
        /// an arc lands, like a teleport arc. False switches buy/build to the flat screen instead.
        /// </summary>
        public bool VRBuildMode = true;
        /// <summary>
        /// 3D sound in first person: each Sim's and object's sound comes from where it is around your head (above,
        /// behind...), with OpenAL Soft's HRTF for headphones. False: the game's own left/right panning.
        /// </summary>
        public bool Sound3D = true;
        /// <summary>Observer mode on stairs (see <see cref="VRStairs"/>).</summary>
        public VRStairs ObserverStairs = VRStairs.Ride;
        /// <summary>Walls in buy/build mode (see <see cref="VRWallView"/>); the catalogue tablet's wall button changes it.</summary>
        public VRWallView BuildWalls = VRWallView.Cutaway;
        /// <summary>Where the buy/build catalogue is (see <see cref="VRCatalogStyle"/>).</summary>
        public VRCatalogStyle Catalogue = VRCatalogStyle.Hand;
        /// <summary>
        /// Pie menus drawn for VR: the choices as buttons around your Sim's head, over what you pointed at, sized for a
        /// headset. False shows the game's own pie menu on a panel instead.
        /// </summary>
        public bool VRPieMenus = true;
        /// <summary>Starting launch speed of the placement arc (m/s); the right stick changes it while placing.</summary>
        public float ArcReach = 7f;
        /// <summary>Pie menus and dialogs float this far in front of you (metres), this wide.</summary>
        public float PanelDistance = 1.3f;
        public float PanelWidth = 1.7f;
        /// <summary>Clock (right wrist) and needs (left wrist): Auto shows them when you turn your wrist to look.</summary>
        public VRWristMode WristDisplays = VRWristMode.Auto;
        /// <summary>
        /// How quickly the view follows your Sim's head (per second). Lower is smoother but laggier; the head bone
        /// bobs with every animation, so following it exactly is uncomfortable.
        /// </summary>
        public float SimHeadSmoothing = 8f;
        /// <summary>How far (metres) you can lean away from your Sim's head before the view drags along with you.</summary>
        public float LeanLimit = 0.3f;
        /// <summary>See <see cref="VRSimActionView"/>.</summary>
        public VRSimActionView SimActionView = VRSimActionView.Comfortable;
        /// <summary>Darken the edge of your view while it moves without you driving it (your Sim walking you about).</summary>
        public VRVignetteStrength Vignette = VRVignetteStrength.Medium;
        /// <summary>The same while you move with the stick or turn smoothly.</summary>
        public VRVignetteStrength VignetteWhenMoving = VRVignetteStrength.Low;
        /// <summary>See <see cref="VRMovement"/>.</summary>
        public VRMovement Movement = VRMovement.StickAndArc;
        /// <summary>Comfortable view: how far (metres) your Sim can get from you before you blink after them.</summary>
        public float LeashDistance = 4f;
        /// <summary>
        /// Moderate view: the speed (m/s) you glide after your Sim at; constant, a little above the pace Sims walk
        /// somewhere on their own (about 1.5 m/s at normal game speed), so you keep up.
        /// </summary>
        public float GlideSpeed = 1.6f;
        /// <summary>Length of a blink (fade out, move, fade in) in seconds.</summary>
        public float BlinkDuration = 0.25f;
        /// <summary>
        /// Holding the left stick while your Sim is busy cancels what they're doing (they finish gracefully, as when
        /// you click their action) so you can walk them again. Off: only a seated Sim stands up for the stick.
        /// </summary>
        public bool StickCancelsActions = true;
        /// <summary>While you're stepped out of your Sim, show what they're doing in a bubble over their head.</summary>
        public bool ActionBubble = true;
        /// <summary>
        /// In first person, messages that don't need an answer go to your watch (a buzz and a "!") instead of popping
        /// up in front of you. Look at the watch and pull that hand's trigger to read one.
        /// </summary>
        public bool WatchMessages = true;
        /// <summary>
        /// Playing as your Sim, pushing the stick all the way makes them run. Off (the default): they walk (about 1.7 m/s)
        /// and run (about 5 m/s, fast for VR) only after you click the stick, until you let go of it.
        /// </summary>
        public bool RunAtFullTilt = false;
        /// <summary>
        /// When you walk your Sim with the stick while the game runs fast (speed 2 or 3, which makes them walk 3 or 10
        /// times as fast), the game drops to normal speed, and goes back to yours 2 s after you let go.
        /// </summary>
        public bool NormalSpeedWhenSteering = true;
        /// <summary>
        /// Playing as your Sim, while you steer them their arms reach for your controllers, so you see their hands where
        /// yours are. (During actions their hands do the action.) Off: their arms always animate as usual.
        /// </summary>
        public bool SimHands = true;
        /// <summary>Show simple controller models in your hands when you're not in your Sim's body (observer, stepped out).</summary>
        public bool ControllerModels = true;
        /// <summary>
        /// Draw the lot once for both eyes (OpenGL multiview) instead of once per eye: about half the CPU cost of drawing
        /// it. Used when the graphics driver supports it; otherwise each eye is drawn separately.
        /// </summary>
        public bool SinglePassStereo = true;
        /// <summary>Foveated rendering (see <see cref="VRFoveation"/>); follows your eyes when the headset tracks them.</summary>
        public VRFoveation Foveation = VRFoveation.Off;
        /// <summary>
        /// Left-handed: the left controller points and interacts, the right one walks, and the wrist displays swap
        /// over (the needs display, which you point at, goes on your right wrist).
        /// </summary>
        public bool LeftHanded = false;
        /// <summary>The first-run comfort setup has been done (set it to False to see it again, or use the VR menu).</summary>
        public bool ComfortSetupDone = false;
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
        /// <summary>What the settings are when vr.ini doesn't say (built in, then vr.defaults.ini).</summary>
        private static VRSettings Defaults;

        public static VRSettings Load(string userDir, Action<string> log = null)
        {
            var settings = new VRSettings();
            FilePath = Path.Combine(userDir ?? AppContext.BaseDirectory, "vr.ini");
            try
            {
                var defaults = new VRSettings();
                Apply(defaults, Path.Combine(AppContext.BaseDirectory, "vr.defaults.ini"), log);
                Defaults = defaults;
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
                    "# Simitone VR settings. Lines starting with # are defaults (built in, or from vr.defaults.ini next to the",
                    "# executable) and follow them if they change; remove the # to set one yourself, or add it back to undo.",
                    "# Options: PlayMode = Sim|Observer|Dollhouse, TurnMode = Snap|Smooth, MoveDirection = Head|LeftHand,",
                    "#          WristDisplays = Auto|Always|Off, MirrorEye = Left|Right|None,",
                    "#          SimActionView = Comfortable|Moderate|Intense, Vignette / VignetteWhenMoving = Off|Low|Medium|High,",
                    "#          Movement = StickAndArc|Stick|Arc, Foveation = Off|Light|Strong",
                    "# Most of these can also be changed in the headset: open the VR menu (the menu button, or point at the VR",
                    "# options button on your wrist and pull the trigger).",
                };
                foreach (var field in typeof(VRSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    var value = Convert.ToString(field.GetValue(this), CultureInfo.InvariantCulture);
                    var byDefault = Defaults != null && value == Convert.ToString(field.GetValue(Defaults), CultureInfo.InvariantCulture);
                    lines.Add((byDefault ? "# " : "") + field.Name + "=" + value);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, lines);
            }
            catch { }
        }
    }
}
