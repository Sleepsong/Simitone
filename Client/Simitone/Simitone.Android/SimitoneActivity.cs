using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using FSO.Client;
using FSO.Common;
using FSO.LotView;
using Simitone.Client;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Simitone.Android
{
    /// <summary>
    /// Simitone on Android (built from the claude/android branch). It asks for access to the phone's files,
    /// unpacks Simitone's content from the APK to the app's storage (first run), finds The Sims' game files in
    /// Download/sims (or another folder in Download), and starts the game with FreeSO's mobile settings (the phone
    /// shaders with software depth, no multiple render targets, touch controls). Saves, config.ini and simitone.log
    /// go in Download/Simitone.
    /// </summary>
    [Activity(Label = "Simitone", MainLauncher = true,
        Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
        AlwaysRetainTaskState = true, LaunchMode = LaunchMode.SingleInstance,
        ScreenOrientation = ScreenOrientation.SensorLandscape,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden
            | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.UiMode | ConfigChanges.SmallestScreenSize)]
    public class SimitoneActivity : Microsoft.Xna.Framework.AndroidGameActivity
    {
        private static string LogPath;
        /// <summary>The always-reachable copy of the log (Android/media/com.sleepsong.simitone), set up by SimitoneApp.</summary>
        public static string MediaLogPath;
        private static readonly object LogLock = new object();
        private SimitoneGame Game;
        private SecondScreenHost SecondScreen; //(a second display, such as the AYN Thor's bottom screen)
        private TextView Status;
        private string AppFolder; //Android/data/<package>/files: always writable, but not reachable without root
        private string Downloads; //the phone's Download folder
        private bool WaitingForAccess;
        private bool Started;

        public static void Log(string line)
        {
            global::Android.Util.Log.Info("Simitone", line);
            var stamped = DateTime.Now.ToString("HH:mm:ss ") + line + "\n";
            lock (LogLock)
            {
                if (MediaLogPath != null) try { File.AppendAllText(MediaLogPath, stamped); } catch (Exception) { }
                if (LogPath != null) try { File.AppendAllText(LogPath, stamped); } catch (Exception) { }
            }
        }

        protected override void OnCreate(Bundle bundle)
        {
            Log("activity starting");
            base.OnCreate(bundle);
            Log("MonoGame activity started");
            try
            {
                CreateUI();
            }
            catch (Exception e)
            {
                Log("startup failed: " + e);
                var error = new TextView(this) { Text = "Simitone couldn't start:\n\n" + e + "\n\nLog: Android/media/com.sleepsong.simitone/simitone.log", TextSize = 13 };
                error.SetPadding(32, 32, 32, 32);
                SetContentView(error);
            }
        }

        /// <summary>
        /// Full screen: the status and navigation bars hidden (a swipe from the edge shows them for a moment), and the
        /// game drawn under the camera cut-out too. Again whenever Simitone gets the focus back.
        /// </summary>
        private void HideSystemBars()
        {
            try
            {
                var decor = Window.DecorView; //(makes the window's decor first: its bar controller comes from there)
                if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
                {
                    var attributes = Window.Attributes;
                    attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
                    Window.Attributes = attributes;
                }
                if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                {
                    Window.SetDecorFitsSystemWindows(false);
                    var bars = Window.InsetsController;
                    if (bars != null)
                    {
                        bars.Hide(WindowInsets.Type.SystemBars());
                        bars.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
                    }
                }
                else
                {
#pragma warning disable CA1422
                    Window.DecorView.SystemUiFlags = SystemUiFlags.ImmersiveSticky | SystemUiFlags.HideNavigation | SystemUiFlags.Fullscreen
                        | SystemUiFlags.LayoutStable | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutFullscreen;
#pragma warning restore CA1422
                }
            }
            catch (Exception e)
            {
                Log("couldn't hide the system bars: " + e.Message);
            }
        }

        public override void OnWindowFocusChanged(bool hasFocus)
        {
            base.OnWindowFocusChanged(hasFocus);
            if (hasFocus) HideSystemBars();
        }

        private void CreateUI()
        {
            Window.AddFlags(WindowManagerFlags.Fullscreen | WindowManagerFlags.KeepScreenOn);
            HideSystemBars();

            AppFolder = GetExternalFilesDir(null)?.AbsolutePath ?? FilesDir.AbsolutePath;
#pragma warning disable CA1422 // (the public Download folder's path: still the way to get it for file access)
            Downloads = global::Android.OS.Environment.GetExternalStoragePublicDirectory(global::Android.OS.Environment.DirectoryDownloads).AbsolutePath;
#pragma warning restore CA1422
            Console.SetOut(new LogWriter(Console.Out));
            Log($"Simitone {PackageManager.GetPackageInfo(PackageName, 0).VersionName} on Android {Build.VERSION.Release} (API {(int)Build.VERSION.SdkInt}), {Build.Manufacturer} {Build.Model}");

            Status = new TextView(this) { Text = "Preparing Simitone...", Gravity = GravityFlags.Center, TextSize = 18 };
            SetContentView(Status);

            if (HasFileAccess()) Begin();
            else AskForFileAccess();
        }

        /// <summary>Whether Simitone can read the game in Download (and write its saves there).</summary>
        private bool HasFileAccess()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R) return global::Android.OS.Environment.IsExternalStorageManager;
            return CheckSelfPermission(Manifest.Permission.ReadExternalStorage) == Permission.Granted
                && CheckSelfPermission(Manifest.Permission.WriteExternalStorage) == Permission.Granted;
        }

        private void AskForFileAccess()
        {
            WaitingForAccess = true;
            var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
            layout.SetGravity(GravityFlags.Center);
            layout.SetPadding(64, 32, 64, 32);
            var text = new TextView(this)
            {
                Text = "Simitone reads The Sims' game files from your Download folder (Download/sims) and keeps its saves "
                    + "in Download/Simitone. Android needs you to allow \"All files access\" for that.\n\n"
                    + "Tap the button, switch Simitone on, then come back.",
                TextSize = 16
            };
            var button = new Button(this) { Text = "Allow file access" };
            button.Click += (s, e) => OpenAccessSettings();
            layout.AddView(text);
            layout.AddView(button);
            SetContentView(layout);
            Log("asking for file access");
        }

        private void OpenAccessSettings()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
                try
                {
                    StartActivity(new Intent(global::Android.Provider.Settings.ActionManageAppAllFilesAccessPermission,
                        global::Android.Net.Uri.Parse("package:" + PackageName)));
                }
                catch (Exception)
                {
                    StartActivity(new Intent(global::Android.Provider.Settings.ActionManageAllFilesAccessPermission));
                }
            }
            else RequestPermissions(new[] { Manifest.Permission.ReadExternalStorage, Manifest.Permission.WriteExternalStorage }, 1);
        }

        protected override void OnResume()
        {
            base.OnResume();
            Log("resumed");
            RestartIfAccessGranted();
            SecondScreen?.OnActivityResumed();
        }

        protected override void OnStop()
        {
            SecondScreen?.OnActivityStopped();
            base.OnStop();
        }

        protected override void OnPause()
        {
            Log("paused");
            base.OnPause();
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            RestartIfAccessGranted();
        }

        /// <summary>
        /// Once file access is allowed, the screen starts again: MonoGame only runs a game made before the screen is
        /// resumed (its view waits for the "resumed" signal), so the game has to be made in OnCreate.
        /// </summary>
        private void RestartIfAccessGranted()
        {
            if (!WaitingForAccess || Started || !HasFileAccess()) return;
            WaitingForAccess = false;
            Log("file access allowed: starting the screen again to start the game");
            Recreate();
        }

        protected override void OnDestroy()
        {
            var closing = IsFinishing && Game != null;
            Log(closing ? "closing" : "screen destroyed");
            base.OnDestroy();
            // one game per run: the engine keeps a lot of global state, so opening Simitone again starts afresh
            if (closing) global::Android.OS.Process.KillProcess(global::Android.OS.Process.MyPid());
        }

        /// <summary>With file access: log to Download/Simitone, unpack, find the game, start.</summary>
        private void Begin()
        {
            if (Started) return;
            Started = true;
            WaitingForAccess = false;
            var userDir = Path.Combine(Downloads, "Simitone");
            try
            {
                Directory.CreateDirectory(userDir);
                var log = Path.Combine(userDir, "simitone.log");
                File.WriteAllText(log, (MediaLogPath != null && File.Exists(MediaLogPath)) ? File.ReadAllText(MediaLogPath) : "");
                LogPath = log;
            }
            catch (Exception e)
            {
                Log("can't write to " + userDir + ": " + e.Message);
            }
            Log("file access: yes. Download folder: " + Downloads);
            Status.Text = "Preparing Simitone...";
            SetContentView(Status);

            // (all here in OnCreate, before the screen is resumed: see RestartIfAccessGranted)
            string game;
            try
            {
                UnpackContent();
                game = FindGame(Downloads, AppFolder);
            }
            catch (Exception e)
            {
                Log("startup failed: " + e);
                Status.Text = "Simitone couldn't start:\n" + e.Message + "\n\nSee Download/Simitone/simitone.log";
                return;
            }
            if (game == null) ShowMissingGame();
            else StartGame(game, userDir);
        }

        /// <summary>Copies Content/ out of the APK into the app's storage, once per install or update.</summary>
        private void UnpackContent()
        {
            var root = FilesDir.AbsolutePath;
            var stamp = Path.Combine(root, "content.stamp");
            var installed = PackageManager.GetPackageInfo(PackageName, 0).LastUpdateTime.ToString();
            if (File.Exists(stamp) && File.ReadAllText(stamp) == installed && Directory.Exists(Path.Combine(root, "Content")))
            {
                Log("content already unpacked");
                return;
            }
            RunOnUiThread(() => Status.Text = "Unpacking Simitone's content (first run)...");
            string[] files;
            using (var list = new StreamReader(Assets.Open("Content/files.txt")))
                files = list.ReadToEnd().Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            var buffer = new byte[1 << 16];
            foreach (var file in files)
            {
                var dest = Path.Combine(root, file);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                using (var input = Assets.Open(file))
                using (var output = File.Create(dest))
                {
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0) output.Write(buffer, 0, read);
                }
            }
            File.WriteAllText(stamp, installed);
            Log($"unpacked {files.Length} content files to {root}");
        }

        /// <summary>
        /// The Sims' game folder (it has GameData/Behavior.iff): Download/sims, else any folder in Download (or one inside
        /// it, e.g. "The Sims Legacy Collection"), else the app's own folder.
        /// </summary>
        private static string FindGame(string downloads, string appFolder)
        {
            bool Has(string dir) => File.Exists(Path.Combine(dir, "GameData", "Behavior.iff"))
                || File.Exists(Path.Combine(dir, "GameData", "behavior.iff"));
            var candidates = new[] { Path.Combine(downloads, "sims"), Path.Combine(downloads, "Sims"), Path.Combine(downloads, "The Sims") }
                .Concat(SafeDirs(downloads))
                .Concat(SafeDirs(downloads).SelectMany(SafeDirs))
                .Concat(new[] { appFolder })
                .Concat(SafeDirs(appFolder));
            foreach (var dir in candidates)
            {
                if (Directory.Exists(dir) && Has(dir))
                {
                    Log("game files: " + dir);
                    return dir.TrimEnd('/') + "/";
                }
            }
            Log("game files: NOT FOUND in " + downloads);
            return null;
        }

        private static string[] SafeDirs(string dir)
        {
            try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : new string[0]; }
            catch (Exception) { return new string[0]; }
        }

        private void ShowMissingGame()
        {
            Status.Text = "Simitone needs The Sims' game files.\n\n"
                + "Put your game folder (the one with GameData in it) in your Download folder, named sims:\n\n"
                + "    Download/sims/GameData/...\n\n"
                + "Your own saves (a UserData folder) can go in Download/Simitone; otherwise Simitone starts from the "
                + "game's neighbourhood.\n\n"
                + "Then open Simitone again.";
            Status.Gravity = GravityFlags.CenterVertical | GravityFlags.Left;
            Status.TextSize = 15;
            Status.SetPadding(48, 24, 48, 24);
        }

        /// <summary>
        /// A neighbourhood copy cut short (Simitone closed while copying it, before copies were made safe) is moved
        /// aside, not deleted, so it's copied again whole: it has fewer files than the game's.
        /// </summary>
        private static void SetAsideIncompleteCopy(string gamePath, string userFolder)
        {
            try
            {
                var source = Path.Combine(gamePath, "UserData");
                var copy = Path.Combine(userFolder, "UserData");
                if (!Directory.Exists(source) || !Directory.Exists(copy)) return;
                var have = Directory.GetFiles(copy, "*", SearchOption.AllDirectories).Length;
                var want = Directory.GetFiles(source, "*", SearchOption.AllDirectories).Length;
                if (have >= want) return;
                var aside = copy + ".incomplete-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                Directory.Move(copy, aside);
                Log($"neighbourhood copy was incomplete ({have} of {want} files): moved to {aside}, copying again");
            }
            catch (Exception e)
            {
                Log("couldn't check the neighbourhood copy: " + e.Message);
            }
        }

        private void StartGame(string gamePath, string userFolder)
        {
            SetAsideIncompleteCopy(gamePath, userFolder);
            try
            {
                Directory.SetCurrentDirectory(FilesDir.AbsolutePath);
                var userDir = userFolder.TrimEnd('/') + "/";
                Directory.CreateDirectory(userDir);
                FSOEnvironment.UserDir = userDir; //(before GlobalSettings is first read: config.ini lives here)
                FSOEnvironment.Args = "";
                FSOEnvironment.ContentDir = Path.Combine(FilesDir.AbsolutePath, "Content") + "/";
                FSOEnvironment.GFXContentDir = "Content/iOS/"; //the phone shader set (as FreeSO's Android app), read from the APK's assets
                FSOEnvironment.Linux = true; //(case-sensitive file system)
                FSOEnvironment.DirectX = false;
                FSOEnvironment.SoftwareKeyboard = true; //touch controls
                FSOEnvironment.SoftwareDepth = true; //the phone shaders
                FSOEnvironment.UseMRT = false;
                FSOEnvironment.EnableNPOTMip = false;
                FSOEnvironment.GLVer = 2;
                FSOEnvironment.TexCompress = false;
                FSOEnvironment.TexCompressSupport = false;
                // 3D by default (as FreeSO's Android app): it draws with a real depth buffer. The 2D view's "software
                // depth" (for phones without one) misdraws here. The cube button switches; an empty file named "2d" in
                // Download/Simitone starts in 2D only (no switch).
                FSOEnvironment.Enable3D = !File.Exists(Path.Combine(userDir, "2d"));
                FSOEnvironment.GameThread = Thread.CurrentThread;
                FSO.Files.ImageLoaderHelpers.BitmapFunction = BitmapReader;

                var metrics = new DisplayMetrics();
                WindowManager.DefaultDisplay.GetRealMetrics(metrics);
                var shortSide = Math.Min(metrics.WidthPixels, metrics.HeightPixels);
                var scale = Math.Max(1f, Math.Min(4f, shortSide / 560f)); //UI at least ~560 px tall
                Log($"screen {metrics.WidthPixels}x{metrics.HeightPixels} @ {metrics.DensityDpi} dpi, UI scale {scale:0.00}, 3D: {FSOEnvironment.Enable3D}");

                var set = GlobalSettings.Default;
                set.DPIScaleFactor = scale;
                FSOEnvironment.DPIScaleFactor = scale;
                set.Windowed = true;
                set.TargetRefreshRate = 60;
                if (set.LanguageCode == 0) set.LanguageCode = 1;
                FSO.Files.Formats.IFF.Chunks.STR.DefaultLangCode = (FSO.Files.Formats.IFF.Chunks.STRLangCode)set.LanguageCode;
                set.StartupPath = gamePath;
                set.TS1HybridEnable = true;
                set.TS1HybridPath = gamePath;
                set.ClientVersion = "0";
                set.LightingMode = 0; //basic lighting: the lighting map's max/min blending isn't available in MonoGame on Android
                set.AntiAlias = 0;
                set.ComplexShaders = false;
                set.EnableTransitions = true;
                set.DirectionalLight3D = false;

                GameFacade.DirectX = false;
                World.DirectX = false;
                Log("starting the game");
                Game = new AndroidSimitoneGame();
                Game.Components.Add(new FrameLogger(Game));
                SecondScreen = new SecondScreenHost(this, userDir);
                Simitone.Client.UI.DualScreen.SecondScreen.Host = SecondScreen;
                Simitone.Client.UI.DualScreen.SecondScreen.Note = Log;
                SecondScreen.Start();
                SetContentView((View)Game.Services.GetService(typeof(View)));
                Game.Run();
                Log("game running");
            }
            catch (Exception e)
            {
                Log("game failed to start: " + e);
                Status.Text = "Simitone couldn't start:\n" + e.Message + "\n\nSee Download/Simitone/simitone.log";
                SetContentView(Status);
            }
        }

        /// <summary>Image decoding (as Simitone's desktop build does it): RGBA pixels with ImageSharp.</summary>
        public static Tuple<byte[], int, int> BitmapReader(Stream str)
        {
            using (var image = SixLabors.ImageSharp.Image.Load<Rgba32>(str))
            {
                var data = new byte[image.Width * image.Height * 4];
                image.CopyPixelDataTo(data);
                return new Tuple<byte[], int, int>(data, image.Width, image.Height);
            }
        }

        /// <summary>Logs when the game gets its graphics, and its first frames (so a black screen says how far it got).</summary>
        private class FrameLogger : Microsoft.Xna.Framework.DrawableGameComponent
        {
            private int Frames;
            private FSO.Content.ContentLoadingProgress LastStep = FSO.Content.ContentLoadingProgress.Invalid;
            private readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
            private long StepStart, LastBeat;
            public FrameLogger(Microsoft.Xna.Framework.Game game) : base(game) { DrawOrder = int.MaxValue; }
            public override void Initialize()
            {
                try { Log($"game initialising: graphics {GraphicsDevice?.Adapter?.Description ?? "?"}, back buffer {GraphicsDevice?.PresentationParameters.BackBufferWidth}x{GraphicsDevice?.PresentationParameters.BackBufferHeight}"); }
                catch (Exception e) { Log("game initialising (" + e.Message + ")"); }
                base.Initialize();
            }
            public override void Draw(Microsoft.Xna.Framework.GameTime gameTime)
            {
                Frames++;
                if (Frames == 1 || Frames == 60 || Frames == 600) Log($"frame {Frames} drawn");
                if (Frames == 1)
                {
                    try
                    {
                        var ext = global::Android.Opengl.GLES20.GlGetString(global::Android.Opengl.GLES20.GlExtensions) ?? "";
                        var version = global::Android.Opengl.GLES20.GlGetString(global::Android.Opengl.GLES20.GlVersion);
                        var renderer = global::Android.Opengl.GLES20.GlGetString(global::Android.Opengl.GLES20.GlRenderer);
                        Log($"graphics: {version} on {renderer}, npot: {ext.Contains("GL_OES_texture_npot")}");
                    }
                    catch (Exception e) { Log("graphics: (" + e.Message + ")"); }
                }
                // loading: each step as it starts, and a line every 15 s while one runs
                var now = Clock.ElapsedMilliseconds;
                var step = FSO.Content.Content.LoadProgress;
                if (step != LastStep)
                {
                    if (LastStep != FSO.Content.ContentLoadingProgress.Invalid) Log($"loading step {LastStep} took {(now - StepStart) / 1000f:0.0} s");
                    Log($"loading step: {step}");
                    LastStep = step;
                    StepStart = LastBeat = now;
                }
                else if (step != FSO.Content.ContentLoadingProgress.Done && now - LastBeat > 15000)
                {
                    Log($"still loading: {step} for {(now - StepStart) / 1000} s (frame {Frames})");
                    LastBeat = now;
                }
            }
        }

        /// <summary>Console output (the engine's diagnostics) to logcat and simitone.log.</summary>
        private class LogWriter : TextWriter
        {
            private readonly TextWriter Inner;
            private readonly StringBuilder Line = new StringBuilder();
            public LogWriter(TextWriter inner) { Inner = inner; }
            public override Encoding Encoding => Encoding.UTF8;
            public override void Write(char value)
            {
                if (value == '\n') { Log(Line.ToString()); Line.Clear(); }
                else if (value != '\r') Line.Append(value);
            }
            public override void WriteLine(string value) { Log(Line.ToString() + value); Line.Clear(); }
        }
    }
}
