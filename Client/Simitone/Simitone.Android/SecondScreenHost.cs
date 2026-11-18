using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Display;
using Android.Opengl;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.UI.DualScreen;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using AndroidDisplay = Android.Views.Display;
using XnaGraphicsDeviceManager = Microsoft.Xna.Framework.GraphicsDeviceManager;

namespace Simitone.Android
{
    /// <summary>
    /// A second screen for Simitone on Android: the AYN Thor's bottom screen, a TV or monitor plugged into a phone, or
    /// another display (such as one simulated in Developer options). Android sees it as a second display. This finds
    /// it, puts a full-screen window there, and shows the game's images on it.
    ///
    /// Normally the window shows the controls (<see cref="UISecondScreen"/>), and its touches go to them. With the
    /// screens swapped (Options, Swap Screens: the Thor's screens the other way round, or TV mode), it shows the game
    /// instead, and the controls cover the device's own screen. That happens while the game runs, without moving it:
    /// Simitone's activity stays on the device's screen, with the controller's buttons, but its view is resized to the
    /// other display's shape (at the device's screen height, so the UI keeps its size; a 4K TV gets a 1080p game,
    /// scaled up), and covered by a layer for the controls. After each frame, the game's image is copied out and
    /// drawn on the other display's window, and that window's touches go to the game's view, rescaled.
    ///
    /// The window is a Presentation (a dialog of this activity shown on the other display, as the dual-screen
    /// emulators use); if Android won't put one there, an activity launched onto that display instead. Either way
    /// the window can't take the focus, so the built-in controls and keys stay with the game.
    ///
    /// Drawing: the game draws the controls into a texture as part of its frame. Then, on the game thread, a small
    /// second OpenGL context that shares the game's textures is made current on a window's surface, draws a texture
    /// over it and swaps, and the game's context is made current again. With the screens swapped, the game's finished
    /// frame is first copied into a texture of its own (the game's GL state is put back as it was). Neither window
    /// waits for its display's refresh.
    ///
    /// Without the swap, only used when the other display is no bigger than the one Simitone is on (the Thor: 1240x1080
    /// under 1920x1080). Every display, and what happens, goes to simitone.log ("second screen").
    /// </summary>
    public class SecondScreenHost : Java.Lang.Object, DisplayManager.IDisplayListener, ISecondScreenHost
    {
        /// <summary>An empty file named this in Download/Simitone: use an activity on the second display, never a Presentation.</summary>
        public const string ActivityMarker = "secondscreen-activity";
        public static SecondScreenHost Current;

        private readonly Activity Activity;
        private readonly Handler Main;
        private readonly string UserDir;
        private DisplayManager Displays;
        private bool Paused, WantedValue, WantedSet, SwapValue;

        // the window on the second display (UI thread)
        private int ShownOn = -1;
        private SecondScreenPresentation Presentation;
        private bool UsingActivity;
        private string LastProbe;

        // the game's view, in a layout of its own (UI thread): resized and covered while the screens are swapped
        private FrameLayout GameRoot;
        private View GameView;
        private Func<XnaGraphicsDeviceManager> Graphics;
        private SurfaceView ControlsLayer;
        private bool SwappedLayout;
        private int GameW, GameH;

        // the surfaces (set on the UI thread; used on the game thread, under Lock)
        private readonly object Lock = new object();
        /// <summary>The window on the other display: the controls, or the game when swapped.</summary>
        private readonly Output Other;
        /// <summary>The layer over the game's view on the device's screen, for the controls while swapped.</summary>
        private readonly Output Layer;
        /// <summary>The game's frames go to the other display (under Lock).</summary>
        private bool Swapped;

        // the controls' touches (UI thread in, game thread out)
        private readonly ConcurrentQueue<(int x, int y, bool down)> Touches = new ConcurrentQueue<(int, int, bool)>();
        private bool TouchIsDown;
        private int TouchesLogged;

        // OpenGL (game thread, under Lock)
        private EGLDisplay EglDisplay;
        private EGLContext SharedWith, BlitContext;
        private EGLConfig Config;
        private int GameGLVersion;
        private int Program, AttrPos, AttrUV, UniTex;
        private Java.Nio.FloatBuffer Quad, CopyQuad;
        private int CopyTexture, CopyW, CopyH;
        private bool Broken;
        private int GameFramesShown;
        private static readonly FieldInfo GLTextureField = typeof(Texture).GetField("glTexture", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public SecondScreenHost(Activity activity, string userDir)
        {
            Activity = activity;
            UserDir = userDir;
            Main = new Handler(Looper.MainLooper);
            Other = new Output(this, "the other display");
            Layer = new Output(this, "the controls layer");
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

        /// <summary>
        /// UI thread: the game's view, in the layout that's the activity's content, and its graphics settings (for its
        /// size). Needed to swap the screens.
        /// </summary>
        public void UseGameView(FrameLayout root, View gameView, Func<XnaGraphicsDeviceManager> graphics)
        {
            GameRoot = root;
            GameView = gameView;
            Graphics = graphics;
            Main.Post(Pick);
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

        public bool SwapWanted
        {
            set
            {
                if (value == SwapValue) return;
                SwapValue = value;
                Main.Post(Pick);
            }
        }

        bool ISecondScreenHost.Swapped { get { lock (Lock) return Swapped; } }

        /// <summary>Where the controls go: the other display, or the layer on the device's screen while swapped.</summary>
        private Output ControlsOutput => Swapped ? Layer : Other;

        public bool Ready
        {
            get { lock (Lock) return !Broken && ControlsOutput.HasSurface; }
        }

        public int Width { get { lock (Lock) return ControlsOutput.W; } }
        public int Height { get { lock (Lock) return ControlsOutput.H; } }

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
        /// opening the second screen's activity can pause Simitone for a moment on some devices). The game's view keeps
        /// its swapped shape, so coming back doesn't resize it.
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

        /// <summary>
        /// The display to use: another one that's on, a presentation display first (the Thor's bottom screen, a TV or
        /// monitor). For the controls, the first that's no bigger than Simitone's; for the game (swapped), the largest.
        /// </summary>
        private AndroidDisplay Choose(bool forGame, out string why)
        {
            why = null;
            var own = OwnDisplay();
            if (own == null) { why = "no display of its own yet"; return null; }
            var (ow, oh) = RealSize(own);
            var presentation = Displays.GetDisplays(DisplayManager.DisplayCategoryPresentation).Select(x => x.DisplayId).ToHashSet();
            var others = Displays.GetDisplays()
                .Where(d => d.DisplayId != own.DisplayId && d.IsValid && d.State != DisplayState.Off)
                .OrderByDescending(d => presentation.Contains(d.DisplayId))
                .ThenByDescending(d => { var (w, h) = RealSize(d); return (long)w * h; })
                .ToList();
            if (others.Count == 0) { why = "no other display on"; return null; }
            if (forGame) return others[0];
            foreach (var d in others)
            {
                var (w, h) = RealSize(d);
                if ((long)w * h <= (long)ow * oh) return d;
            }
            why = "the other display is bigger than Simitone's (turn on Swap Screens to put the game there)";
            return null;
        }

        /// <summary>UI thread: shows the window on the right display, or closes it; and swaps the screens, or not.</summary>
        private void Pick()
        {
            try
            {
                if (Displays == null) return;
                string why;
                AndroidDisplay target = null;
                if (!WantedSet) return; //(the game says once it's running)
                var swap = WantedValue && SwapValue && GameView != null;
                if (!WantedValue) why = "turned off in Options";
                else if (Paused) why = "Simitone stopped";
                else target = Choose(swap, out why);

                if (target == null)
                {
                    if (!Paused) Unswap(); //(while stopped, the game's view keeps its shape for coming back)
                    CloseWindow(why);
                    return;
                }
                if (target.DisplayId != ShownOn)
                {
                    CloseWindow(null);
                    OpenWindow(target);
                }
                if (swap) Swap();
                else Unswap();
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

        // ---- swapping the screens (UI thread)

        /// <summary>
        /// The game on the other display, the controls on this one: the game's view takes the other display's shape (at
        /// this screen's height, so the UI keeps its size), and a layer for the controls covers it. Once the window
        /// there has a surface (this is called again when it does).
        /// </summary>
        private void Swap()
        {
            if (GameRoot == null || GameView == null) return;
            int ow, oh;
            lock (Lock) { ow = Other.W; oh = Other.H; }
            if (ow <= 0 || oh <= 0) return; //(when the window's surface comes)

            var screenShort = Math.Min(GameRoot.Width, GameRoot.Height);
            if (screenShort <= 0) screenShort = 1080;
            var scale = screenShort / (float)Math.Min(ow, oh);
            var gw = Math.Max(2, (int)Math.Round(ow * scale) & ~1);
            var gh = Math.Max(2, (int)Math.Round(oh * scale) & ~1);
            if (!SwappedLayout || gw != GameW || gh != GameH)
            {
                GameW = gw;
                GameH = gh;
                //(the game's buffer takes this shape when its surface changes: see MonoGame's ResetClientBounds)
                SetBackBuffer(gw, gh);
                GameView.LayoutParameters = new FrameLayout.LayoutParams(gw, gh);
                GameView.RequestLayout();
                Log($"screens swapped: the game at {gw}x{gh} on display {ShownOn} ({ow}x{oh}), the controls on this screen");
            }
            if (ControlsLayer == null)
            {
                ControlsLayer = new SurfaceView(Activity);
                ControlsLayer.SetZOrderMediaOverlay(true); //(over the game's view, itself a surface)
                ControlsLayer.Focusable = false;
                ControlsLayer.FocusableInTouchMode = false;
                Layer.Attach(ControlsLayer);
                GameRoot.AddView(ControlsLayer, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            }
            if (!SwappedLayout) ReleaseControlsTouch();
            SwappedLayout = true;
            lock (Lock) Swapped = true;
        }

        /// <summary>Back the usual way round: the game's view fills this screen again, and the controls go to the other display.</summary>
        private void Unswap()
        {
            if (!SwappedLayout) return;
            lock (Lock) Swapped = false;
            ReleaseGameTouch();
            if (ControlsLayer != null)
            {
                GameRoot.RemoveView(ControlsLayer);
                ControlsLayer = null;
            }
            ReleaseControlsTouch();
            SetBackBuffer(GameRoot.Width, GameRoot.Height);
            GameView.LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
            GameView.RequestLayout();
            SwappedLayout = false;
            GameW = GameH = 0;
            Log("screens back the usual way round");
        }

        /// <summary>
        /// The size the game's buffer should be. MonoGame keeps the buffer at this shape inside its view (letterboxed if
        /// they differ), so this is set before the view is resized; the game then takes the view's size when its surface
        /// changes (SimitoneGame's resize, as when a desktop window is resized).
        /// </summary>
        private void SetBackBuffer(int width, int height)
        {
            if (width <= 0 || height <= 0) return;
            var graphics = Graphics?.Invoke();
            if (graphics == null) return;
            graphics.PreferredBackBufferWidth = width;
            graphics.PreferredBackBufferHeight = height;
        }

        // ---- the windows' surfaces and touches (UI thread)

        /// <summary>Called by the presentation or activity with the view it draws on.</summary>
        public void Attach(SurfaceView view)
        {
            Other.Attach(view);
        }

        /// <summary>A surface the host draws on (UI thread sets it; the game thread draws, under Lock).</summary>
        private class Output : Java.Lang.Object, ISurfaceHolderCallback
        {
            private readonly SecondScreenHost Host;
            private readonly string Name;
            public Surface Pending;
            public bool Dirty;
            public int W, H;
            public EGLSurface Egl;
            public int Shown;

            public Output(SecondScreenHost host, string name)
            {
                Host = host;
                Name = name;
            }

            public bool HasSurface => (Pending != null || Egl != null) && W > 0 && H > 0;

            public void Attach(SurfaceView view)
            {
                view.Holder.AddCallback(this);
                view.Touch += (s, e) => Host.OnTouch(this, e);
            }

            public void SurfaceCreated(ISurfaceHolder holder) { }

            public void SurfaceChanged(ISurfaceHolder holder, global::Android.Graphics.Format format, int width, int height)
            {
                lock (Host.Lock)
                {
                    Pending = holder.Surface;
                    W = width;
                    H = height;
                    Dirty = true;
                }
                Log($"surface {width}x{height} ({Name})");
                if (this == Host.Other) Host.Main.Post(Host.Pick); //(swapped: the game takes its shape)
            }

            public void SurfaceDestroyed(ISurfaceHolder holder)
            {
                lock (Host.Lock)
                {
                    DestroyEgl();
                    Pending = null;
                    W = H = 0;
                    Dirty = false;
                }
                if (this == Host.Other && Host.SwappedLayout) Host.ReleaseGameTouch();
                else Host.ReleaseControlsTouch();
            }

            /// <summary>Under Lock.</summary>
            public void DestroyEgl()
            {
                if (Egl != null && Host.EglDisplay != null)
                {
                    try { EGL14.EglDestroySurface(Host.EglDisplay, Egl); } catch (Exception) { }
                }
                Egl = null;
            }
        }

        private void OnTouch(Output from, View.TouchEventArgs e)
        {
            var ev = e.Event;
            e.Handled = true;
            if (ev == null) return;
            if (from == Other && SwappedLayout)
            {
                ForwardToGame(ev, from);
                return;
            }
            if (from == Layer && !SwappedLayout) return;
            int x = (int)ev.GetX(), y = (int)ev.GetY();
            switch (ev.ActionMasked)
            {
                case MotionEventActions.Down:
                    TouchIsDown = true;
                    Touches.Enqueue((x, y, true));
                    if (TouchesLogged < 3) Log($"touch {++TouchesLogged} at {x},{y}" + (TouchesLogged == 3 ? " (no more touches logged)" : ""));
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

        /// <summary>A touch held on the controls is let go (their surface went, or moved to the other screen).</summary>
        private void ReleaseControlsTouch()
        {
            while (Touches.TryDequeue(out _)) { }
            if (TouchIsDown) Touches.Enqueue((0, 0, false));
            TouchIsDown = false;
        }

        /// <summary>Swapped: a touch on the other display goes to the game's view, rescaled to its size (every finger).</summary>
        private void ForwardToGame(MotionEvent ev, Output from)
        {
            var view = GameView;
            int w, h;
            lock (Lock) { w = from.W; h = from.H; }
            if (view == null || w <= 0 || h <= 0 || view.Width <= 0 || view.Height <= 0) return;
            switch (ev.ActionMasked)
            {
                case MotionEventActions.Down: GameTouchDown = true; break;
                case MotionEventActions.Up:
                case MotionEventActions.Cancel: GameTouchDown = false; break;
            }
            var copy = MotionEvent.Obtain(ev);
            try
            {
                var matrix = new global::Android.Graphics.Matrix();
                matrix.SetScale(view.Width / (float)w, view.Height / (float)h);
                copy.Transform(matrix);
                view.DispatchTouchEvent(copy);
            }
            finally
            {
                copy.Recycle();
            }
        }
        private bool GameTouchDown;

        /// <summary>A touch held on the game from the other display is let go (its window went, or the screens swapped back).</summary>
        private void ReleaseGameTouch()
        {
            if (!GameTouchDown || GameView == null) return;
            GameTouchDown = false;
            var now = SystemClock.UptimeMillis();
            var cancel = MotionEvent.Obtain(now, now, MotionEventActions.Cancel, 0, 0, 0);
            try { GameView.DispatchTouchEvent(cancel); }
            finally { cancel.Recycle(); }
        }

        /// <summary>
        /// The second screen's window over the whole display: its system bars hidden where Android lets this window
        /// hide them (a swipe from the edge shows them for a moment), and the drawing kept clear of any bars that
        /// stay (the Thor's bottom screen has its own navigation bar). Returns the view to show.
        /// </summary>
        public static View EdgeToEdge(Window window, SurfaceView surface)
        {
            var frame = new FrameLayout(surface.Context);
            frame.AddView(surface, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            if (window == null) return frame;
            try
            {
                var decor = window.DecorView; //(makes the window's decor first: its bar controller comes from there)
                if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
                {
                    var attributes = window.Attributes;
                    attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
                    window.Attributes = attributes;
                }
                if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                {
                    window.SetDecorFitsSystemWindows(false);
                    var bars = window.InsetsController;
                    if (bars != null)
                    {
                        bars.Hide(WindowInsets.Type.SystemBars());
                        bars.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
                    }
                    // bars that stay: keep the drawing out from under them
                    string last = null;
                    frame.SetOnApplyWindowInsetsListener(new InsetsPadding((v, insets) =>
                    {
                        var bar = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout());
                        v.SetPadding(bar.Left, bar.Top, bar.Right, bar.Bottom);
                        var text = $"{bar.Left},{bar.Top},{bar.Right},{bar.Bottom}";
                        if (text != last)
                        {
                            last = text;
                            Log("system bars left/top/right/bottom: " + text + " px");
                        }
                        return WindowInsets.Consumed;
                    }));
                }
                else
                {
#pragma warning disable CA1422
                    decor.SystemUiFlags = SystemUiFlags.ImmersiveSticky | SystemUiFlags.HideNavigation | SystemUiFlags.Fullscreen
                        | SystemUiFlags.LayoutStable | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutFullscreen;
#pragma warning restore CA1422
                }
            }
            catch (Exception e)
            {
                Log("couldn't hide the system bars: " + e.Message);
            }
            return frame;
        }

        private class InsetsPadding : Java.Lang.Object, View.IOnApplyWindowInsetsListener
        {
            private readonly Func<View, WindowInsets, WindowInsets> Apply;
            public InsetsPadding(Func<View, WindowInsets, WindowInsets> apply) { Apply = apply; }
            public WindowInsets OnApplyWindowInsets(View v, WindowInsets insets) => Apply(v, insets);
        }

        // ---- drawing (game thread)

        /// <summary>
        /// After each frame: swapped, the game's frame goes to the other display; and a new image of the controls (if
        /// there is one) goes to wherever they are.
        /// </summary>
        public void Present(Texture2D image)
        {
            if (Broken) return;
            lock (Lock)
            {
                if (image == null && !Swapped) return;
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
                    var otherReady = MakeEglSurface(Other);
                    var layerReady = MakeEglSurface(Layer);

                    // the game's frame, copied while its context is current (then to the other display)
                    var gameFrame = Swapped && otherReady && CopyGameFrame(display, gameRead);
                    var controls = Swapped ? Layer : Other;
                    var controlsReady = Swapped ? layerReady : otherReady;
                    var texture = (image != null && controlsReady) ? ((GLTextureField?.GetValue(image) as int?) ?? 0) : 0;
                    if (!gameFrame && texture == 0) return;

                    GLES20.GlFlush(); //(the game's drawing and the copy, before the other context reads them)
                    if (gameFrame)
                    {
                        switched = true;
                        if (DrawOn(Other, CopyTexture, CopyQuad) && ++GameFramesShown == 1)
                            Log($"first game frame on the other display ({CopyW}x{CopyH} to {Other.W}x{Other.H})");
                    }
                    if (texture != 0)
                    {
                        switched = true;
                        if (DrawOn(controls, texture, Quad) && controls.Shown == 1)
                            Log($"first image shown on {(controls == Layer ? "this screen" : "the other display")} ({image.Width}x{image.Height})");
                    }
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

        /// <summary>Under Lock: the output's EGL surface, made again if its window's surface changed. False if it has none.</summary>
        private bool MakeEglSurface(Output output)
        {
            if (output.Dirty)
            {
                output.Dirty = false;
                output.DestroyEgl();
                if (output.Pending != null && output.Pending.IsValid)
                {
                    output.Egl = EGL14.EglCreateWindowSurface(EglDisplay, Config, output.Pending, new[] { EGL14.EglNone }, 0);
                    if (output.Egl == null || output.Egl.Equals(EGL14.EglNoSurface))
                    {
                        Log("couldn't make a drawing surface (EGL error 0x" + EGL14.EglGetError().ToString("x") + ")");
                        output.Egl = null;
                    }
                    output.Shown = 0;
                }
            }
            return output.Egl != null;
        }

        /// <summary>Under Lock, with the blit context made current on the output: draws a texture over it and swaps.</summary>
        private bool DrawOn(Output output, int texture, Java.Nio.FloatBuffer quad)
        {
            if (!EGL14.EglMakeCurrent(EglDisplay, output.Egl, output.Egl, BlitContext))
            {
                Log("couldn't draw on the surface (EGL error 0x" + EGL14.EglGetError().ToString("x") + ")");
                output.DestroyEgl();
                return false;
            }
            if (output.Shown == 0) EGL14.EglSwapInterval(EglDisplay, 0); //(don't wait for that display's refresh)
            if (Program == 0) MakeProgram();
            Blit(texture, quad, output.W, output.H);
            EGL14.EglSwapBuffers(EglDisplay, output.Egl);
            output.Shown++;
            return true;
        }

        /// <summary>
        /// Under Lock, in the game's context: its finished frame (the default framebuffer) copied into a texture both
        /// contexts can use. The texture and framebuffer bindings the game had are put back.
        /// </summary>
        private bool CopyGameFrame(EGLDisplay display, EGLSurface gameSurface)
        {
            var size = new int[1];
            EGL14.EglQuerySurface(display, gameSurface, EGL14.EglWidth, size, 0);
            var w = size[0];
            EGL14.EglQuerySurface(display, gameSurface, EGL14.EglHeight, size, 0);
            var h = size[0];
            if (w <= 0 || h <= 0) return false;

            // (GL enums by number: some share their names with functions in Android's bindings)
            const int ActiveTextureEnum = 0x84E0, TextureBinding2D = 0x8069, FramebufferBinding = 0x8CA6,
                ReadFramebufferBinding = 0x8CAA, DrawFramebuffer = 0x8CA9, ReadFramebuffer = 0x8CA8, Framebuffer = 0x8D40;
            var es3 = GameGLVersion >= 3;
            var activeUnit = new int[1];
            var bound = new int[1];
            var drawFbo = new int[1];
            var readFbo = new int[1];
            for (int i = 0; i < 8 && GLES20.GlGetError() != 0; i++) { } //(errors left from the game's drawing aren't the copy's)
            GLES20.GlGetIntegerv(ActiveTextureEnum, activeUnit, 0);
            GLES20.GlActiveTexture(GLES20.GlTexture0);
            GLES20.GlGetIntegerv(TextureBinding2D, bound, 0);
            GLES20.GlGetIntegerv(FramebufferBinding, drawFbo, 0);
            if (es3) GLES20.GlGetIntegerv(ReadFramebufferBinding, readFbo, 0);
            try
            {
                GLES20.GlBindFramebuffer(Framebuffer, 0);
                if (CopyTexture == 0)
                {
                    var names = new int[1];
                    GLES20.GlGenTextures(1, names, 0);
                    CopyTexture = names[0];
                    CopyW = CopyH = 0;
                }
                GLES20.GlBindTexture(GLES20.GlTexture2d, CopyTexture);
                if (w != CopyW || h != CopyH)
                {
                    //(its format from the framebuffer's, so the copy always fits)
                    GLES20.GlCopyTexImage2D(GLES20.GlTexture2d, 0, GLES20.GlRgb, 0, 0, w, h, 0);
                    GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureMinFilter, GLES20.GlLinear);
                    GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureMagFilter, GLES20.GlLinear);
                    GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureWrapS, GLES20.GlClampToEdge);
                    GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureWrapT, GLES20.GlClampToEdge);
                    CopyW = w;
                    CopyH = h;
                    Log($"copying the game's frames ({w}x{h})");
                }
                else GLES20.GlCopyTexSubImage2D(GLES20.GlTexture2d, 0, 0, 0, 0, 0, w, h);
            }
            finally
            {
                GLES20.GlBindTexture(GLES20.GlTexture2d, bound[0]);
                GLES20.GlActiveTexture(activeUnit[0]);
                if (es3)
                {
                    GLES20.GlBindFramebuffer(DrawFramebuffer, drawFbo[0]);
                    GLES20.GlBindFramebuffer(ReadFramebuffer, readFbo[0]);
                }
                else GLES20.GlBindFramebuffer(Framebuffer, drawFbo[0]);
            }
            var error = GLES20.GlGetError();
            if (error != 0)
            {
                Log("copying the game's frame failed (GL error 0x" + error.ToString("x") + "); the other display shows the controls");
                CopyW = CopyH = -1; //(not again)
                return false;
            }
            return CopyW > 0;
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
            GameGLVersion = version;
            Program = 0;
            CopyTexture = 0; //(the game's context made it: a new one in a new context)
            CopyW = CopyH = 0;
            Other.Dirty = Other.Pending != null;
            Layer.Dirty = Layer.Pending != null;
            Log($"drawing with a context sharing the game's (GLES {version}, EGL config {configId})");
            return true;
        }

        private void DropGL()
        {
            Other.DestroyEgl();
            Layer.DestroyEgl();
            if (BlitContext != null && EglDisplay != null)
            {
                try { EGL14.EglDestroyContext(EglDisplay, BlitContext); } catch (Exception) { }
            }
            BlitContext = null;
            SharedWith = null;
            Program = 0;
            CopyTexture = 0;
            Other.Dirty = Other.Pending != null;
            Layer.Dirty = Layer.Pending != null;
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

        private static Java.Nio.FloatBuffer MakeQuad(float[] quad)
        {
            var buffer = Java.Nio.ByteBuffer.AllocateDirect(quad.Length * 4).Order(Java.Nio.ByteOrder.NativeOrder()).AsFloatBuffer();
            buffer.Put(quad);
            buffer.Position(0);
            return buffer;
        }

        private void MakeProgram()
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
            Quad = MakeQuad(new float[]
            {
                -1, -1, 0, 1,
                 1, -1, 1, 1,
                -1,  1, 0, 0,
                 1,  1, 1, 0,
            });
            // the same for a copy of a window's framebuffer, which is stored bottom row first
            CopyQuad = MakeQuad(new float[]
            {
                -1, -1, 0, 0,
                 1, -1, 1, 0,
                -1,  1, 0, 1,
                 1,  1, 1, 1,
            });
        }

        private void Blit(int texture, Java.Nio.FloatBuffer quad, int w, int h)
        {
            GLES20.GlViewport(0, 0, w, h);
            GLES20.GlDisable(GLES20.GlBlend);
            GLES20.GlDisable(GLES20.GlDepthTest);
            GLES20.GlDisable(GLES20.GlScissorTest);
            GLES20.GlDisable(0x0B44); //GL_CULL_FACE (GLES20.GlCullFace is the function here)
            GLES20.GlUseProgram(Program);
            GLES20.GlActiveTexture(GLES20.GlTexture0);
            GLES20.GlBindTexture(GLES20.GlTexture2d, texture);
            // (render targets have no mipmaps: sample the top level only. The texture is the game's, but its sampling
            // is set the same way there.)
            GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureMinFilter, GLES20.GlLinear);
            GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureMagFilter, GLES20.GlLinear);
            GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureWrapS, GLES20.GlClampToEdge);
            GLES20.GlTexParameteri(GLES20.GlTexture2d, GLES20.GlTextureWrapT, GLES20.GlClampToEdge);
            GLES20.GlUniform1i(UniTex, 0);
            quad.Position(0);
            GLES20.GlVertexAttribPointer(AttrPos, 2, GLES20.GlFloat, false, 16, quad);
            GLES20.GlEnableVertexAttribArray(AttrPos);
            quad.Position(2);
            GLES20.GlVertexAttribPointer(AttrUV, 2, GLES20.GlFloat, false, 16, quad);
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
            SetContentView(SecondScreenHost.EdgeToEdge(Window, view));
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
            SetContentView(SecondScreenHost.EdgeToEdge(Window, view));
        }

        protected override void OnDestroy()
        {
            if (Current == this) Current = null;
            base.OnDestroy();
        }
    }
}
