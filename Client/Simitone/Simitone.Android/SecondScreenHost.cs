using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Display;
using Android.Opengl;
using Android.OS;
using Android.Util;
using Android.Views;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.DualScreen;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using AndroidDisplay = Android.Views.Display;

namespace Simitone.Android
{
    /// <summary>
    /// A second screen for Simitone on Android: the AYN Thor's bottom screen (or another display, such as one
    /// simulated in Developer options). Android sees it as a second display. This finds it, puts a full-screen
    /// window there, sends that window's touches to the game, and shows the images the game draws for it.
    ///
    /// The window is a Presentation (a dialog of this activity shown on the other display, as the dual-screen
    /// emulators use); if Android won't put one there, an activity launched onto that display instead. Either way
    /// the window can't take the focus, so the built-in controls and keys stay with the game on the main screen.
    ///
    /// Drawing: the game draws the second screen's UI into a texture as part of its frame. Then, on the game
    /// thread, a small second OpenGL context that shares the game's textures is made current on the second
    /// screen's surface, draws that texture over it and swaps, and the game's context is made current again. The
    /// game's own GL state is never touched. The second screen doesn't wait for its display's refresh.
    ///
    /// Only used when the other display is no bigger than the one Simitone is on (the Thor: 1240x1080 under
    /// 1920x1080). Every display, and what happens, goes to simitone.log ("second screen").
    /// </summary>
    public class SecondScreenHost : Java.Lang.Object, DisplayManager.IDisplayListener, ISurfaceHolderCallback, ISecondScreenHost
    {
        /// <summary>An empty file named this in Download/Simitone: use an activity on the second display, never a Presentation.</summary>
        public const string ActivityMarker = "secondscreen-activity";
        public static SecondScreenHost Current;

        private readonly Activity Activity;
        private readonly Handler Main;
        private readonly string UserDir;
        private DisplayManager Displays;
        private bool Paused, WantedValue, WantedSet;

        // the window on the second display (UI thread)
        private int ShownOn = -1;
        private SecondScreenPresentation Presentation;
        private bool UsingActivity;
        private string LastProbe;

        // the surface (set on the UI thread; used on the game thread, under Lock)
        private readonly object Lock = new object();
        private Surface PendingSurface;
        private bool SurfaceDirty;
        private int SurfaceWidth, SurfaceHeight;

        // touches (UI thread in, game thread out)
        private readonly ConcurrentQueue<(int x, int y, bool down)> Touches = new ConcurrentQueue<(int, int, bool)>();
        private bool TouchIsDown;

        // OpenGL (game thread, under Lock)
        private EGLDisplay EglDisplay;
        private EGLContext SharedWith, BlitContext;
        private EGLConfig Config;
        private EGLSurface BottomSurface;
        private int Program, AttrPos, AttrUV, UniTex, ParamsSetFor;
        private Java.Nio.FloatBuffer Quad;
        private bool Broken;
        private int Presented;
        private static readonly FieldInfo GLTextureField = typeof(Texture).GetField("glTexture", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public SecondScreenHost(Activity activity, string userDir)
        {
            Activity = activity;
            UserDir = userDir;
            Main = new Handler(Looper.MainLooper);
            Current = this;
        }

        private static void Log(string line) => SimitoneActivity.Log("second screen: " + line);

        /// <summary>UI thread: starts watching the displays and logs them.</summary>
        public void Start()
        {
            try
            {
                Displays = (DisplayManager)Activity.GetSystemService(Context.DisplayService);
                Displays.RegisterDisplayListener(this, Main);
                Probe();
            }
            catch (Exception e)
            {
                Log("couldn't watch the displays: " + e.Message);
            }
        }

        // ---- ISecondScreenHost (game thread)

        public bool Wanted
        {
            set
            {
                if (WantedSet && value == WantedValue) return;
                WantedSet = true;
                WantedValue = value;
                Main.Post(Pick);
            }
        }

        public bool Ready
        {
            get { lock (Lock) return !Broken && (PendingSurface != null || BottomSurface != null) && SurfaceWidth > 0 && SurfaceHeight > 0; }
        }

        public int Width { get { lock (Lock) return SurfaceWidth; } }
        public int Height { get { lock (Lock) return SurfaceHeight; } }

        public bool NextTouch(out int x, out int y, out bool down)
        {
            if (Touches.TryDequeue(out var t))
            {
                x = t.x; y = t.y; down = t.down;
                return true;
            }
            x = y = 0;
            down = false;
            return false;
        }

        // ---- activity lifecycle (UI thread)

        /// <summary>
        /// Simitone left the screen (not just paused: a pause leaves the second screen showing its last image, and
        /// opening the second screen's activity can pause Simitone for a moment on some devices).
        /// </summary>
        public void OnActivityStopped()
        {
            Paused = true;
            CloseWindow("Simitone stopped");
        }

        public void OnActivityResumed()
        {
            Paused = false;
            Pick();
        }

        // ---- displays (UI thread)

        public void OnDisplayAdded(int displayId) { Probe(); Pick(); }
        public void OnDisplayRemoved(int displayId) { Probe(); Pick(); }
        public void OnDisplayChanged(int displayId) { Probe(); Pick(); }

        private AndroidDisplay OwnDisplay()
        {
#pragma warning disable CA1422
            return (Build.VERSION.SdkInt >= BuildVersionCodes.R) ? Activity.Display : Activity.WindowManager.DefaultDisplay;
#pragma warning restore CA1422
        }

        private static (int w, int h) RealSize(AndroidDisplay d)
        {
            var metrics = new DisplayMetrics();
#pragma warning disable CA1422
            d.GetRealMetrics(metrics);
#pragma warning restore CA1422
            return (metrics.WidthPixels, metrics.HeightPixels);
        }

        /// <summary>Logs every display (when the list or any of them changes).</summary>
        private void Probe()
        {
            try
            {
                var own = OwnDisplay()?.DisplayId ?? -1;
                var presentation = Displays.GetDisplays(DisplayManager.DisplayCategoryPresentation).Select(x => x.DisplayId).ToHashSet();
                var lines = Displays.GetDisplays().Select(d =>
                {
                    var (w, h) = RealSize(d);
                    var metrics = new DisplayMetrics();
#pragma warning disable CA1422
                    d.GetRealMetrics(metrics);
#pragma warning restore CA1422
                    return $"display {d.DisplayId} \"{d.Name}\": {w}x{h} @ {metrics.DensityDpi} dpi, {d.RefreshRate:0} Hz, state {d.State}, " +
                        $"flags {d.Flags}, rotation {d.Rotation}{(presentation.Contains(d.DisplayId) ? ", presentation" : "")}" +
                        $"{(d.DisplayId == own ? ", Simitone's" : "")}";
                }).ToList();
                var text = string.Join("\n", lines);
                if (text == LastProbe) return;
                LastProbe = text;
                foreach (var line in lines) Log(line);
            }
            catch (Exception e)
            {
                Log("couldn't list the displays: " + e.Message);
            }
        }

        /// <summary>The display to use: another one that's on and no bigger than Simitone's.</summary>
        private AndroidDisplay Choose(out string why)
        {
            why = null;
            var own = OwnDisplay();
            if (own == null) { why = "no display of its own yet"; return null; }
            var (ow, oh) = RealSize(own);
            var presentation = Displays.GetDisplays(DisplayManager.DisplayCategoryPresentation).Select(x => x.DisplayId).ToHashSet();
            var others = Displays.GetDisplays()
                .Where(d => d.DisplayId != own.DisplayId && d.IsValid && d.State != DisplayState.Off)
                .OrderByDescending(d => presentation.Contains(d.DisplayId))
                .ToList();
            if (others.Count == 0) { why = "no other display on"; return null; }
            foreach (var d in others)
            {
                var (w, h) = RealSize(d);
                if ((long)w * h <= (long)ow * oh) return d;
            }
            why = "the other display is bigger than Simitone's (to use two screens, open Simitone on the bigger one)";
            return null;
        }

        /// <summary>UI thread: shows the window on the right display, or closes it.</summary>
        private void Pick()
        {
            try
            {
                if (Displays == null) return;
                string why;
                AndroidDisplay target = null;
                if (!WantedSet) return; //(the game says once it's running)
                if (!WantedValue) why = "turned off in Options";
                else if (Paused) why = "Simitone stopped";
                else target = Choose(out why);

                if (target == null)
                {
                    CloseWindow(why);
                    return;
                }
                if (target.DisplayId == ShownOn) return;
                CloseWindow(null);
                OpenWindow(target);
            }
            catch (Exception e)
            {
                Log("couldn't set up: " + e);
            }
        }

        private void OpenWindow(AndroidDisplay display)
        {
            var (w, h) = RealSize(display);
            var forceActivity = File.Exists(Path.Combine(UserDir, ActivityMarker));
            if (!forceActivity)
            {
                try
                {
                    Presentation = new SecondScreenPresentation(Activity, display, this);
                    Presentation.Show();
                    ShownOn = display.DisplayId;
                    UsingActivity = false;
                    Log($"window on display {display.DisplayId} ({w}x{h}), as a presentation");
                    return;
                }
                catch (Exception e)
                {
                    Log($"Android wouldn't show a presentation on display {display.DisplayId} ({e.GetType().Name}: {e.Message}); trying an activity there");
                    try { Presentation?.Dismiss(); } catch (Exception) { }
                    Presentation = null;
                }
            }
            try
            {
                var options = ActivityOptions.MakeBasic();
                options.SetLaunchDisplayId(display.DisplayId);
                var intent = new Intent(Activity, typeof(SecondScreenActivity));
                intent.PutExtra(SecondScreenActivity.DisplayExtra, display.DisplayId);
                intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.MultipleTask | ActivityFlags.NoAnimation);
                Activity.StartActivity(intent, options.ToBundle());
                ShownOn = display.DisplayId;
                UsingActivity = true;
                Log($"window on display {display.DisplayId} ({w}x{h}), as an activity" + (forceActivity ? " (" + ActivityMarker + " file)" : ""));
            }
            catch (Exception e)
            {
                Log($"couldn't open an activity on display {display.DisplayId} either: {e.Message}");
                ShownOn = -1;
            }
        }

        private void CloseWindow(string why)
        {
            if (ShownOn == -1 && Presentation == null) return;
            try { Presentation?.Dismiss(); } catch (Exception) { }
            Presentation = null;
            if (UsingActivity) SecondScreenActivity.Current?.Finish();
            UsingActivity = false;
            ShownOn = -1;
            if (why != null) Log("closed (" + why + ")");
        }

        // ---- the window's surface (UI thread)

        /// <summary>Called by the presentation or activity with the view it draws on.</summary>
        public void Attach(SurfaceView view)
        {
            view.Holder.AddCallback(this);
            view.Touch += OnTouch;
        }

        public void SurfaceCreated(ISurfaceHolder holder) { }

        public void SurfaceChanged(ISurfaceHolder holder, global::Android.Graphics.Format format, int width, int height)
        {
            lock (Lock)
            {
                PendingSurface = holder.Surface;
                SurfaceWidth = width;
                SurfaceHeight = height;
                SurfaceDirty = true;
            }
            Log($"surface {width}x{height}");
        }

        public void SurfaceDestroyed(ISurfaceHolder holder)
        {
            lock (Lock)
            {
                DestroyBottomSurface();
                PendingSurface = null;
                SurfaceWidth = SurfaceHeight = 0;
                SurfaceDirty = false;
            }
            while (Touches.TryDequeue(out _)) { }
            if (TouchIsDown) Touches.Enqueue((0, 0, false));
            TouchIsDown = false;
        }

        // ---- touches (UI thread)

        private void OnTouch(object sender, View.TouchEventArgs e)
        {
            var ev = e.Event;
            e.Handled = true;
            if (ev == null) return;
            int x = (int)ev.GetX(), y = (int)ev.GetY();
            switch (ev.ActionMasked)
            {
                case MotionEventActions.Down:
                    TouchIsDown = true;
                    Touches.Enqueue((x, y, true));
                    break;
                case MotionEventActions.Move:
                    if (TouchIsDown) Touches.Enqueue((x, y, true));
                    break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    TouchIsDown = false;
                    Touches.Enqueue((x, y, false));
                    break;
            }
        }

        // ---- drawing (game thread)

        public void Present(Texture2D image)
        {
            if (Broken) return;
            lock (Lock)
            {
                EGLDisplay display = null;
                EGLContext gameContext = null;
                EGLSurface gameDraw = null, gameRead = null;
                var switched = false;
                try
                {
                    display = EGL14.EglGetCurrentDisplay();
                    gameContext = EGL14.EglGetCurrentContext();
                    gameDraw = EGL14.EglGetCurrentSurface(EGL14.EglDraw);
                    gameRead = EGL14.EglGetCurrentSurface(EGL14.EglRead);
                    if (gameContext == null || gameContext.Equals(EGL14.EglNoContext)) return;

                    if (BlitContext != null && !gameContext.Equals(SharedWith))
                    {
                        Log("the game's graphics were set up again: setting up the second screen's again");
                        DropGL();
                    }
                    if (BlitContext == null && !SetupContext(display, gameContext)) return;

                    if (SurfaceDirty)
                    {
                        SurfaceDirty = false;
                        DestroyBottomSurface();
                        if (PendingSurface != null && PendingSurface.IsValid)
                        {
                            BottomSurface = EGL14.EglCreateWindowSurface(EglDisplay, Config, PendingSurface, new[] { EGL14.EglNone }, 0);
                            if (BottomSurface == null || BottomSurface.Equals(EGL14.EglNoSurface))
                            {
                                Log("couldn't make a drawing surface (EGL error 0x" + EGL14.EglGetError().ToString("x") + ")");
                                BottomSurface = null;
                                return;
                            }
                            Presented = 0;
                        }
                    }
                    if (BottomSurface == null) return;

                    var texture = (GLTextureField?.GetValue(image) as int?) ?? 0;
                    if (texture == 0) return;

                    GLES20.GlFlush(); //(the game's drawing of the texture, before the other context reads it)
                    if (!EGL14.EglMakeCurrent(EglDisplay, BottomSurface, BottomSurface, BlitContext))
                    {
                        Log("couldn't draw on the surface (EGL error 0x" + EGL14.EglGetError().ToString("x") + ")");
                        DestroyBottomSurface();
                        return;
                    }
                    switched = true;
                    if (Presented == 0) EGL14.EglSwapInterval(EglDisplay, 0); //(don't wait for the second screen's refresh)
                    if (Program == 0 && !MakeProgram()) return;
                    Blit(texture);
                    EGL14.EglSwapBuffers(EglDisplay, BottomSurface);
                    if (++Presented == 1) Log($"first image shown ({image.Width}x{image.Height})");
                }
                catch (Exception e)
                {
                    Broken = true;
                    Log("drawing failed, second screen off until Simitone restarts: " + e);
                }
                finally
                {
                    if (switched) EGL14.EglMakeCurrent(display, gameDraw, gameRead, gameContext);
                }
            }
        }

        /// <summary>A context sharing the game's (its config and GLES version), to draw on the second screen with.</summary>
        private bool SetupContext(EGLDisplay display, EGLContext gameContext)
        {
            var value = new int[1];
            EGL14.EglQueryContext(display, gameContext, EGL14.EglConfigId, value, 0);
            var configId = value[0];
            var configs = new EGLConfig[1];
            var count = new int[1];
            if (!EGL14.EglChooseConfig(display, new[] { EGL14.EglConfigId, configId, EGL14.EglNone }, 0, configs, 0, 1, count, 0) || count[0] < 1)
            {
                Broken = true;
                Log("couldn't find the game's EGL config " + configId);
                return false;
            }
            EGL14.EglQueryContext(display, gameContext, EGL14.EglContextClientVersion, value, 0);
            var version = Math.Max(2, value[0]);
            var context = EGL14.EglCreateContext(display, configs[0], gameContext, new[] { EGL14.EglContextClientVersion, version, EGL14.EglNone }, 0);
            if (context == null || context.Equals(EGL14.EglNoContext))
            {
                Broken = true;
                Log("couldn't make a context sharing the game's (EGL error 0x" + EGL14.EglGetError().ToString("x") + ")");
                return false;
            }
            EglDisplay = display;
            Config = configs[0];
            BlitContext = context;
            SharedWith = gameContext;
            Program = 0;
            ParamsSetFor = 0;
            SurfaceDirty = PendingSurface != null;
            Log($"drawing with a context sharing the game's (GLES {version}, EGL config {configId})");
            return true;
        }

        private void DestroyBottomSurface()
        {
            if (BottomSurface != null && EglDisplay != null)
            {
                try { EGL14.EglDestroySurface(EglDisplay, BottomSurface); } catch (Exception) { }
            }
            BottomSurface = null;
        }

        private void DropGL()
        {
            DestroyBottomSurface();
            if (BlitContext != null && EglDisplay != null)
            {
                try { EGL14.EglDestroyContext(EglDisplay, BlitContext); } catch (Exception) { }
            }
            BlitContext = null;
            SharedWith = null;
            Program = 0;
            SurfaceDirty = PendingSurface != null;
        }

        private const string VertexSource =
            "attribute vec2 aPos;\nattribute vec2 aUV;\nvarying vec2 vUV;\n" +
            "void main() { vUV = aUV; gl_Position = vec4(aPos, 0.0, 1.0); }\n";
        private const string FragmentSource =
            "precision mediump float;\nvarying vec2 vUV;\nuniform sampler2D uTex;\n" +
            "void main() { gl_FragColor = vec4(texture2D(uTex, vUV).rgb, 1.0); }\n";

        private static int Compile(int type, string source)
        {
            var shader = GLES20.GlCreateShader(type);
            GLES20.GlShaderSource(shader, source);
            GLES20.GlCompileShader(shader);
            var ok = new int[1];
            GLES20.GlGetShaderiv(shader, GLES20.GlCompileStatus, ok, 0);
            if (ok[0] == 0) throw new Exception("shader didn't compile: " + GLES20.GlGetShaderInfoLog(shader));
            return shader;
        }

        private bool MakeProgram()
        {
            var vs = Compile(GLES20.GlVertexShader, VertexSource);
            var fs = Compile(GLES20.GlFragmentShader, FragmentSource);
            var program = GLES20.GlCreateProgram();
            GLES20.GlAttachShader(program, vs);
            GLES20.GlAttachShader(program, fs);
            GLES20.GlBindAttribLocation(program, 0, "aPos");
            GLES20.GlBindAttribLocation(program, 1, "aUV");
            GLES20.GlLinkProgram(program);
            var ok = new int[1];
            GLES20.GlGetProgramiv(program, GLES20.GlLinkStatus, ok, 0);
            if (ok[0] == 0) throw new Exception("program didn't link: " + GLES20.GlGetProgramInfoLog(program));
            Program = program;
            AttrPos = 0;
            AttrUV = 1;
            UniTex = GLES20.GlGetUniformLocation(program, "uTex");

            // a quad over the whole surface: its top shows the image's top (the game's render targets are stored
            // top row first, as it draws them flipped)
            var quad = new float[]
            {
                -1, -1, 0, 1,
                 1, -1, 1, 1,
                -1,  1, 0, 0,
                 1,  1, 1, 0,
            };
            Quad = Java.Nio.ByteBuffer.AllocateDirect(quad.Length * 4).Order(Java.Nio.ByteOrder.NativeOrder()).AsFloatBuffer();
            Quad.Put(quad);
            Quad.Position(0);
            return true;
        }

        private void Blit(int texture)
        {
            int w, h;
            w = SurfaceWidth;
            h = SurfaceHeight;
            GLES20.GlViewport(0, 0, w, h);
            GLES20.GlDisable(GLES20.GlBlend);
            GLES20.GlDisable(GLES20.GlDepthTest);
            GLES20.GlDisable(GLES20.GlScissorTest);
            GLES20.GlDisable(0x0B44); //GL_CULL_FACE (GLES20.GlCullFace is the function here)
            GLES20.GlUseProgram(Program);
            GLES20.GlActiveTexture(GLES20.GlTexture0);
            GLES20.GlBindTexture(GLES20.GlTexture2d, texture);
            if (ParamsSetFor != texture)
            {
                // (the game's render target has no mipmaps: sample its top level only)
                GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureMinFilter, GLES20.GlLinear);
                GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureMagFilter, GLES20.GlLinear);
                GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureWrapS, GLES20.GlClampToEdge);
                GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureWrapT, GLES20.GlClampToEdge);
                ParamsSetFor = texture;
            }
            GLES20.GlUniform1i(UniTex, 0);
            Quad.Position(0);
            GLES20.GlVertexAttribPointer(AttrPos, 2, GLES20.GlFloat, false, 16, Quad);
            GLES20.GlEnableVertexAttribArray(AttrPos);
            Quad.Position(2);
            GLES20.GlVertexAttribPointer(AttrUV, 2, GLES20.GlFloat, false, 16, Quad);
            GLES20.GlEnableVertexAttribArray(AttrUV);
            GLES20.GlDrawArrays(GLES20.GlTriangleStrip, 0, 4);
        }
    }

    /// <summary>The second screen's window, as a Presentation: a dialog of Simitone's activity shown on the other display.</summary>
    public class SecondScreenPresentation : Presentation
    {
        private readonly SecondScreenHost Host;

        public SecondScreenPresentation(Context outer, AndroidDisplay display, SecondScreenHost host) : base(outer, display)
        {
            Host = host;
        }

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            // not focusable: touches still come here, but the keys and built-in controls stay with the game
            Window?.AddFlags(WindowManagerFlags.NotFocusable | WindowManagerFlags.KeepScreenOn | WindowManagerFlags.Fullscreen);
            var view = new SurfaceView(Context);
            Host.Attach(view);
            SetContentView(view);
        }
    }

    /// <summary>
    /// The second screen's window as an activity launched onto the other display, for when Android won't put a
    /// presentation there (or with the "secondscreen-activity" file). It never takes the focus either.
    /// </summary>
    [Activity(Label = "Simitone", Exported = false, ExcludeFromRecents = true, ResizeableActivity = true,
        TaskAffinity = "com.sleepsong.simitone.secondscreen", LaunchMode = LaunchMode.SingleInstance,
        Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden
            | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.UiMode | ConfigChanges.SmallestScreenSize)]
    public class SecondScreenActivity : Activity
    {
        public const string DisplayExtra = "display";
        public static SecondScreenActivity Current;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            // if Android put it somewhere else (Simitone's own screen, say), it would cover the game: close at once
            var wanted = Intent?.GetIntExtra(DisplayExtra, -1) ?? -1;
#pragma warning disable CA1422
            var actual = (Build.VERSION.SdkInt >= BuildVersionCodes.R) ? (Display?.DisplayId ?? -1) : WindowManager.DefaultDisplay.DisplayId;
#pragma warning restore CA1422
            if (wanted != actual)
            {
                SimitoneActivity.Log($"second screen: Android opened its activity on display {actual}, not {wanted}; closing it");
                Finish();
                return;
            }
            Current = this;
            Window?.AddFlags(WindowManagerFlags.NotFocusable | WindowManagerFlags.KeepScreenOn | WindowManagerFlags.Fullscreen);
            var view = new SurfaceView(this);
            if (SecondScreenHost.Current != null) SecondScreenHost.Current.Attach(view);
            else Finish();
            SetContentView(view);
        }

        protected override void OnDestroy()
        {
            if (Current == this) Current = null;
            base.OnDestroy();
        }
    }
}
