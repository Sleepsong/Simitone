using System;
using System.Diagnostics;
using System.IO;
using FSO.Common;
using FSO.Common.Rendering.Framework.Model;
using FSO.Common.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Simitone.VR;
using Simitone.VR.Graphics;
using Simitone.Client.UI.Controls;

namespace Simitone.Client.VR
{
    /// <summary>
    /// Glue between Simitone's game loop and the OpenXR runtime.
    ///
    /// Per frame (all on the game thread, inside MonoGame's Draw):
    ///   BeginDraw  - xrWaitFrame / xrBeginFrame, locate eyes, read controllers, draw the wrist displays.
    ///   (draw)     - in first person, VRLotWorld renders both eyes into EyeTargets; otherwise Simitone draws
    ///                normally to the window. The UI is captured separately (VRUILayer).
    ///   EndDraw    - first person: eyes (projection layer) + floating UI panel when a pie menu / dialog is up + wrist
    ///                displays. Otherwise: the whole window on a flat virtual screen (loading, CAS, neighbourhood,
    ///                build/buy). Then xrEndFrame.
    ///
    /// Wherever a flat surface is shown, the right controller is a laser pointer driving the mouse. The VR options menu
    /// (<see cref="VRMenu"/>) floats on top of either view when open.
    /// </summary>
    public static class SimitoneVR
    {
        /// <summary>Set by the -vr command line flag.</summary>
        public static bool Requested;

        public static VRRuntime Runtime { get; private set; }
        public static VRSettings Settings { get; private set; } = VRSettings.Default;
        public static bool Available => Runtime != null && Runtime.Session != 0 && !Runtime.Lost;
        public static bool SessionRunning => Available && Runtime.SessionRunning;
        public static string Status { get; private set; } = "VR not started";

        public static VRLotWorld CurrentWorld;
        /// <summary>Sim (first person as your Sim) or Observer (roam freely). B switches while in first person.</summary>
        public static VRPlayMode PlayMode = VRPlayMode.Sim;
        /// <summary>Enter first person automatically when a lot loads.</summary>
        public static bool WantsImmersiveOnLot = true;

        /// <summary>Supplies the tile and floor of the selected Sim (entering a lot places you next to them).</summary>
        public static Func<(Vector2 tile, sbyte level)?> FocusProvider;
        public static Func<VRHudData> HudProvider;

        // frame state
        public static bool FrameActive { get; private set; }
        public static float FrameDelta { get; private set; } = 1 / 90f;
        public static bool EyesRendered;
        public static bool WantsEyes => FrameActive && Runtime.ShouldRender && Runtime.ViewsValid;
        public static int MirrorEye { get; private set; }

        private static RenderTarget2D[] EyeTargets;
        private static VRSwapchain[] EyeSwapchains;
        private static VRSwapchain ScreenSwapchain;
        private static VRSwapchain PanelSwapchain;
        private static RenderTarget2D UITarget;
        private static VRWristHud Wrists;
        private static VRMenu Menu;
        private static VRKeyboard Keyboard;
        private static VRSetup Setup;
        private static bool SetupRequested, SetupShownThisSession, MenuRequested;
        private static bool PanelPressArmed;
        private static bool PendingEyeResize;
        private static bool KeyboardMenuToggle;
        private static VRPose LastHead;
        private static Stopwatch FrameTimer;

        // flat virtual screen (not in first person)
        private static bool ScreenPoseDirty = true;
        private static Vector3 ScreenPosition;
        private static Quaternion ScreenOrientation = Quaternion.Identity;

        // floating UI panel (first person, while a pie menu or dialog is open, and in buy/build mode)
        /// <summary>Set every update by <see cref="VRGameBridge"/>.</summary>
        public static bool UIPanelWanted;
        /// <summary>Buy/build mode in first person (set by <see cref="VRGameBridge"/>): the catalogue is on a desk-like
        /// panel and, off the panel, the pointer is the game's mouse at the placement cursor.</summary>
        public static bool BuildMode;
        public static bool PanelShown { get; private set; }
        public static bool PanelPointerOnPanel { get; private set; }
        public static float PanelPointerDistance { get; private set; }
        private static Vector3 PanelPosition;
        private static Quaternion PanelOrientation = Quaternion.Identity;
        private static float PanelWidth = 1.7f;
        private enum PanelKind { Full, Desk, Dialog }
        private static PanelKind PanelKindShown;
        /// <summary>The part of the UI the panel shows, in window pixels (all of it, or just the open dialog).</summary>
        private static Rectangle PanelRegion;
        /// <summary>Set every update by <see cref="VRGameBridge"/>: a pie menu is open.</summary>
        public static bool PieMenuOpen;
        private static FSO.Client.UI.Framework.UIElement LastTopDialog;
        private static bool NeedsRelease;
        /// <summary>The VR menu or keyboard has the pointer: the bridge draws the beam to it and picks nothing.</summary>
        public static bool OverlayHasPointer { get; private set; }
        /// <summary>Distance (metres) from the controller to the menu or keyboard it points at.</summary>
        public static float OverlayPointerDistance { get; private set; }
        private static bool PanelArmed; // the trigger that opened a menu must be let go before it clicks on it
        private static bool WorldArmed; // ...and a trigger held while coming off the panel doesn't click in the world
        private static bool LastOnPanel;

        // laser pointer -> mouse
        private static bool PointerActive;
        private static bool CursorOnSurface; // the pointer is on the screen/panel (draw a cursor there)
        private static Point PointerPixel;
        private static int ScrollOffset;
        private static float ScrollAccumulator;
        private static bool PointerLeft, PointerRight;

        // requests consumed by the game (VRGameBridge), set from controller presses in BeginDraw
        private static bool PauseRequested, SpeedRequested, ModeSwitchRequested, NextSimRequested, BuildMenuRequested;
        private static bool WorldSelectRequested, CancelRequested;
        private static int QueueCancelRequested = -1;

        private static Game Game;
        private static StreamWriter LogFile;
        private static System.Reflection.FieldInfo DialogsField;

        // ------------------------------------------------------------------ lifecycle

        public static void Log(string message)
        {
            var line = "[VR] " + message;
            Console.WriteLine(line);
            try { LogFile?.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff ") + message); LogFile?.Flush(); } catch { }
        }

        /// <summary>Starts OpenXR on MonoGame's GL context. Call from Game.Initialize, after the GraphicsDevice exists.</summary>
        public static void Initialize(Game game, GraphicsDeviceManager graphics)
        {
            if (!Requested) return;
            Game = game;
            FrameTimer = Stopwatch.StartNew();
            try
            {
                var userDir = FSOEnvironment.UserDir ?? AppContext.BaseDirectory;
                Directory.CreateDirectory(userDir);
                LogFile = new StreamWriter(Path.Combine(userDir, "vr.log"), false);
            }
            catch { }

            Settings = VRSettings.Load(FSOEnvironment.UserDir, Log);
            PlayMode = Settings.PlayMode;
            MirrorEye = Settings.MirrorEye?.ToLowerInvariant() switch { "right" => 1, "none" => -1, _ => 0 };

            Runtime = new VRRuntime(Log);
            if (!Runtime.Initialize("Simitone", out var error))
            {
                Status = "VR unavailable: " + error;
                Log(Status);
                Log("Continuing in desktop mode.");
                Runtime = null;
                return;
            }

            try
            {
                CreateEyeBuffers(game.GraphicsDevice);
                // (the panels are Simitone UI: made once the game's content is loaded, see EnsurePanels)
            }
            catch (Exception e)
            {
                Fail("could not create eye buffers", e);
                return;
            }

            // Only now that VR works: the headset paces rendering (xrWaitFrame), so desktop vsync must not also block
            // us, and updates run at a VR-friendly rate.
            graphics.SynchronizeWithVerticalRetrace = false;
            graphics.ApplyChanges();
            if (!(FSOEnvironment.Args ?? "").Contains("-hz") && Settings.UpdateRate > 0)
            {
                FSOEnvironment.RefreshRate = Settings.UpdateRate;
                game.TargetElapsedTime = TimeSpan.FromTicks(10000000 / Settings.UpdateRate);
            }
            // In a headset the desktop window usually isn't focused. MonoGame would then sleep 20 ms every frame and
            // FreeSO would ignore all input (including the laser pointer), so a running session counts as focus.
            game.InactiveSleepTime = TimeSpan.Zero;
            var previousFallback = FSO.Common.Rendering.Framework.Game.FocusFallback;
            FSO.Common.Rendering.Framework.Game.FocusFallback = () => SessionRunning || (previousFallback?.Invoke() ?? false);

            Runtime.ReferenceSpaceChanged += () => { ScreenPoseDirty = true; PanelShown = false; };
            GameThread.EveryUpdate(InjectPointer);
            Status = $"VR running on {Runtime.SystemName} ({Runtime.RuntimeName}), update rate {FSOEnvironment.RefreshRate} Hz";
            Log(Status);
        }

        /// <summary>(Re)creates the per-eye render targets and swapchains at the current render scale.</summary>
        private static void CreateEyeBuffers(GraphicsDevice gd)
        {
            GLInterop.Finish(); // the last frame's copies into the old swapchain images must be done before they go
            if (EyeSwapchains != null) foreach (var s in EyeSwapchains) s.Dispose();
            if (EyeTargets != null) foreach (var t in EyeTargets) t.Dispose();
            EyeSwapchains = null;
            EyeTargets = null;
            float scale = MathHelper.Clamp(Settings.RenderScale, 0.25f, 2f);
            int w = Math.Min(Runtime.MaxWidth, Math.Max(64, (int)(Runtime.RecommendedWidth * scale)));
            int h = Math.Min(Runtime.MaxHeight, Math.Max(64, (int)(Runtime.RecommendedHeight * scale)));
            var first = Runtime.CreateSwapchain(w, h);
            try { EyeSwapchains = new[] { first, Runtime.CreateSwapchain(w, h) }; }
            catch { first.Dispose(); throw; }
            EyeTargets = new[]
            {
                new RenderTarget2D(gd, w, h, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents),
                new RenderTarget2D(gd, w, h, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents)
            };
            Log($"eye targets {w}x{h} (render scale {scale:0.##})");
        }

        /// <summary>Changes the render scale while running (from the VR menu); the eye buffers are rebuilt next frame.</summary>
        public static void SetRenderScale(float scale)
        {
            Settings.RenderScale = scale;
            PendingEyeResize = true;
        }

        public static bool MenuOpen => Menu?.IsOpen == true;
        public static bool SetupOpen => Setup?.Shown == true;
        /// <summary>The VR menu or the comfort setup is open (it has the pointer and the trigger).</summary>
        public static bool OverlayOpen => MenuOpen || SetupOpen;

        /// <summary>Opens the comfort setup (from the VR menu or the game's Options panel) at the next frame.</summary>
        public static void StartSetup() => SetupRequested = true;

        /// <summary>Opens the VR menu at the next frame (the VR options button on the left wrist).</summary>
        public static void RequestMenu() => MenuRequested = true;

        /// <summary>A queue icon on the left wrist was clicked: cancel that action (VRGameBridge does it).</summary>
        public static void RequestQueueCancel(ushort uid) => QueueCancelRequested = uid;

        /// <summary>
        /// The VR panels are Simitone UI (fonts, art, controls), so they're made once the game's UI and content are
        /// ready, rather than when VR starts.
        /// </summary>
        private static void EnsurePanels()
        {
            if (Menu != null || FSO.Client.GameFacade.Screens == null || FSO.Content.Content.Get() == null
                || FSO.Client.GameFacade.GraphicsDevice == null) return;
            try
            {
                Wrists = new VRWristHud();
                Menu = new VRMenu();
                Keyboard = new VRKeyboard();
                Setup = new VRSetup();
            }
            catch (Exception e)
            {
                Log("could not make the VR panels: " + e);
                Menu = null;
            }
        }

        /// <summary>The panels the controller can point at now: a modal one (setup, menu) alone, else keyboard and wrist.</summary>
        private static System.Collections.Generic.IEnumerable<VRUIPanel> PointablePanels(bool immersive)
        {
            if (SetupOpen) { yield return Setup; yield break; }
            if (MenuOpen) { yield return Menu; yield break; }
            if (Keyboard?.Shown == true) yield return Keyboard;
            if (immersive && Wrists?.Needs.Shown == true) yield return Wrists.Needs;
        }

        public static void Shutdown()
        {
            if (Runtime == null) return;
            try
            {
                if (EyeSwapchains != null) foreach (var s in EyeSwapchains) s.Dispose();
                ScreenSwapchain?.Dispose();
                PanelSwapchain?.Dispose();
                Wrists?.Destroy();
                Menu?.Destroy();
                Keyboard?.Destroy();
                Setup?.Destroy();
                Runtime.Dispose();
            }
            catch { }
            EyeSwapchains = null;
            ScreenSwapchain = PanelSwapchain = null;
            Runtime = null;
            Log("shut down");
            try { LogFile?.Dispose(); } catch { }
            LogFile = null;
        }

        private static void Fail(string what, Exception e)
        {
            Status = "VR stopped: " + what + ": " + e.Message;
            Log(Status + "\n" + e);
            FrameActive = false;
            try { Runtime?.Dispose(); } catch { }
            Runtime = null;
            PointerActive = false;
            PanelShown = false;
            Wrists?.HideAll();
            Menu?.Hide();
            Keyboard?.Hide();
            Setup?.Hide();
            // Without a headset, first person would leave you with no UI and keyboard-only movement.
            var world = CurrentWorld;
            if (world?.Immersive == true) GameThread.NextUpdate(_ => world.ExitImmersive());
        }

        // ------------------------------------------------------------------ frame loop

        public static void BeginDraw(GraphicsDevice gd)
        {
            if (FrameTimer != null)
            {
                FrameDelta = MathHelper.Clamp((float)FrameTimer.Elapsed.TotalSeconds, 0.001f, 0.1f);
                FrameTimer.Restart();
            }
            EyesRendered = false;
            if (!Available) { FrameActive = false; return; }
            if (PendingEyeResize)
            {
                PendingEyeResize = false;
                try { CreateEyeBuffers(gd); }
                catch (Exception e)
                {
                    Fail("could not resize the eye buffers", e);
                    return;
                }
            }

            try
            {
                FrameActive = Runtime.BeginFrame(Settings.DeadZone);
            }
            catch (Exception e)
            {
                Fail("xrBeginFrame failed", e);
                return;
            }
            if (Runtime.ExitRequested)
            {
                Log("the runtime ended the VR session; continuing on the desktop");
                if (FrameActive) try { Runtime.EndFrame(default); } catch { }
                Fail("session exited", new OperationCanceledException());
                return;
            }
            if (!FrameActive) return;

            if (Runtime.ViewsValid)
            {
                var v0 = Runtime.Views[0].Pose;
                var v1 = Runtime.Views[1].Pose;
                LastHead = new VRPose
                {
                    Valid = true,
                    Position = (VRMath.ToXna(v0.Position) + VRMath.ToXna(v1.Position)) / 2,
                    Orientation = Quaternion.Slerp(VRMath.ToXna(v0.Orientation), VRMath.ToXna(v1.Orientation), 0.5f)
                };
            }

            var world = CurrentWorld;
            bool immersive = world?.Immersive == true;
            var input = Runtime.Input;
            var inputActive = input != null && input.Active;
            var vrCamera = world?.VRCamera;

            // After the VR menu, the keyboard or a wrist display has used a press, the trigger and grip must be let go
            // before they click anything else (the press that closes the menu mustn't click what's behind it).
            if (!inputActive || (!input.Select.Down && !input.Secondary.Down)) NeedsRelease = false;

            // ---- popups: a buzz when one arrives, wherever you are
            var dialog = TopModalDialog();
            if (dialog != LastTopDialog)
            {
                if (dialog != null && inputActive)
                {
                    Runtime.Haptic(true, 0.45f, 0.08f);
                    Runtime.Haptic(false, 0.45f, 0.08f);
                    FSO.HIT.HITVM.Get()?.PlaySoundEvent(FSO.Client.UI.Model.UISounds.Whoosh);
                }
                LastTopDialog = dialog;
            }

            // ---- floating UI panel (first person only): a dialog on its own at eye level, pie menus with the rest of
            // the UI straight ahead, the buy/build catalogue lower down and tilted up like a desk
            var kind = BuildMode ? PanelKind.Desk : (dialog is UIMobileDialog && !PieMenuOpen) ? PanelKind.Dialog : PanelKind.Full;
            var showPanel = immersive && (UIPanelWanted || BuildMode);
            if (showPanel && !PanelShown && vrCamera?.Blinking == true) showPanel = false; // wait for a blink to end
            if (showPanel && (!PanelShown || PanelKindShown != kind)) { PlacePanel(kind); PanelArmed = false; }
            PanelShown = showPanel && LastHead.Valid;
            PanelRegion = kind == PanelKind.Dialog ? DialogRegion(gd, dialog) : FullRegion(gd);

            if (inputActive)
            {
                if (input.ToggleView.Pressed && world != null)
                    GameThread.NextUpdate(_ => world.ToggleImmersive());
                if (input.SwitchMode.Pressed)
                {
                    if (immersive) ModeSwitchRequested = true;
                    else ScreenPoseDirty = true; // B on the flat screen: bring it back in front of you
                }
                if (input.Pause.Pressed) PauseRequested = true;
                if (input.Speed.Pressed) SpeedRequested = true;
                if (input.NextSim.Pressed) NextSimRequested = true;
                if (input.BuildMenu.Pressed) BuildMenuRequested = true;
            }

            // ---- the VR panels (Simitone UI): wrist displays, comfort setup, VR menu, keyboard
            EnsurePanels();
            if (immersive) Wrists?.UpdatePlacement(input, LastHead, Settings.WristDisplays);
            else Wrists?.HideAll();
            var overlayWasOpen = OverlayOpen;

            // the comfort setup: the first time VR runs, or when asked for
            if (Setup != null && LastHead.Valid && (SetupRequested || (!Settings.ComfortSetupDone && !SetupShownThisSession)))
            {
                Menu?.Close();
                Setup.Begin(LastHead);
                SetupRequested = false;
                SetupShownThisSession = true;
                NeedsRelease = true;
            }
            // the VR menu: the left menu button, F9, or the VR options button on the left wrist
            if (Menu != null && !SetupOpen && ((inputActive && input.Menu.Pressed) || KeyboardMenuToggle || MenuRequested))
            {
                if (Menu.IsOpen) Menu.Close();
                else Menu.OpenAt(LastHead);
                NeedsRelease = true;
            }
            KeyboardMenuToggle = MenuRequested = false;
            // the keyboard: whenever a text box is waiting for typing (until Hide)
            var target = KeyboardTarget;
            if (Keyboard != null)
            {
                if (Keyboard.HideRequested)
                {
                    Keyboard.HideRequested = false;
                    KeyboardDismissed = target;
                }
                var wantKeyboard = target != null && target != KeyboardDismissed && LastHead.Valid;
                if (wantKeyboard && !Keyboard.Shown) Keyboard.ShowAt(LastHead);
                else if (!wantKeyboard && Keyboard.Shown) Keyboard.Hide();
            }

            // the controller's ray on the panels: the nearest one it hits has it as its mouse. A press that started
            // off the panels doesn't press on one (e.g. dragging a wall onto the wrist).
            VRUIPanel pointed = null;
            var pointedPixel = Vector2.Zero;
            var pointedDistance = float.MaxValue;
            var aim = inputActive ? (input.RightAim.Valid ? input.RightAim : input.LeftAim) : default;
            var panels = new System.Collections.Generic.List<VRUIPanel>(PointablePanels(immersive));
            foreach (var panel in panels)
            {
                if (panel.Hit(aim, out var pixel, out var distance) && distance < pointedDistance)
                {
                    pointed = panel;
                    pointedPixel = pixel;
                    pointedDistance = distance;
                }
            }
            var buttonsDown = inputActive && (input.Select.Down || input.Secondary.Down);
            if (!buttonsDown) PanelPressArmed = true;
            else if (pointed == null) PanelPressArmed = false;
            foreach (var panel in new VRUIPanel[] { Setup, Menu, Keyboard, Wrists?.Needs, Wrists?.Watch })
            {
                if (panel == null) continue;
                if (panel == pointed) panel.SetMouse(pointedPixel, PanelPressArmed && input.Select.Down, PanelPressArmed && input.Secondary.Down);
                else panel.SetMouse(null, false, false);
            }
            var overlayUsedPress = pointed != null && buttonsDown;
            if (overlayUsedPress) NeedsRelease = true;

            var triggerFree = inputActive && input.Select.Pressed && !NeedsRelease;
            if (triggerFree && pointed == null)
            {
                if (MenuOpen)
                {
                    Menu.Close(); // pulled beside the menu
                    overlayUsedPress = NeedsRelease = true;
                }
                else if (immersive && Wrists?.WatchFacing == true && Messages.Count > 0 && !OverlayOpen)
                {
                    // looking at the watch with messages waiting: read the oldest
                    GameThread.NextUpdate(_ => ShowOldestMessage());
                    Runtime.Haptic(true, 0.25f, 0.02f);
                    overlayUsedPress = NeedsRelease = true;
                }
            }
            if (SetupOpen && inputActive && input.Secondary.Pressed) Setup.GoBack(); // the grip goes back a page

            OverlayHasPointer = OverlayOpen || pointed != null;
            OverlayPointerDistance = pointed != null ? pointedDistance : 0;
            if (overlayWasOpen && !OverlayOpen) NeedsRelease = true;

            // ---- laser pointer (not while an overlay has it, nor on the frame one used a press)
            PointerActive = false;
            CursorOnSurface = false;
            PanelPointerOnPanel = false;
            PanelPointerDistance = 0;
            if (inputActive && !overlayWasOpen && !OverlayHasPointer && !overlayUsedPress)
            {
                if (!immersive)
                {
                    if (ScreenSwapchain != null && !ScreenPoseDirty)
                        UpdatePointer(gd, input, ScreenPosition, ScreenOrientation, Settings.ScreenWidth,
                            (float)ScreenSwapchain.Height / ScreenSwapchain.Width, !NeedsRelease, FullRegion(gd));
                }
                else if (PanelShown)
                {
                    if (!input.Select.Down) PanelArmed = WorldArmed = true;
                    if (NeedsRelease) PanelArmed = WorldArmed = false;
                    var aspect = (float)PanelRegion.Height / Math.Max(1, PanelRegion.Width);
                    // a trigger held while sliding onto the panel (e.g. dragging a wall) doesn't click there
                    if (!LastOnPanel && input.Select.Down && HitsSurface(input, PanelPosition, PanelOrientation, PanelWidth, aspect))
                        PanelArmed = false;
                    UpdatePointer(gd, input, PanelPosition, PanelOrientation, PanelWidth, aspect, PanelArmed, PanelRegion);
                    PanelPointerOnPanel = PointerActive;
                    if (BuildMode && !PointerActive)
                    {
                        // Off the catalogue the pointer is the game's mouse, parked where the cursor camera maps it
                        // onto your aim (VRCameraController.UpdateBuildCursor).
                        if (LastOnPanel && input.Select.Down) WorldArmed = false;
                        PointerPixel = WorldCursorPixel(gd);
                        PointerLeft = WorldArmed && input.Select.Down;
                        PointerRight = false;
                        PointerActive = true;
                        if (WorldArmed && input.Select.Pressed) Runtime.Haptic(true, 0.2f, 0.015f);
                    }
                    else if (!PointerActive)
                    {
                        if (input.Select.Pressed && !NeedsRelease) WorldSelectRequested = true; // beside the panel
                    }
                    if (input.Secondary.Pressed && !PanelPointerOnPanel && !NeedsRelease) CancelRequested = true;
                    LastOnPanel = PanelPointerOnPanel;
                }
                else if (!NeedsRelease)
                {
                    if (input.Select.Pressed) WorldSelectRequested = true;
                    if (input.Secondary.Pressed) CancelRequested = true;
                }
            }

            if (vrCamera != null) vrCamera.CursorPixel = WorldCursorPixel(gd).ToVector2() / FSOEnvironment.DPIScaleFactor;

            // (the panels themselves are drawn by the UI layer, into their own textures, during the frame)
        }

        public static void EndDraw(GraphicsDevice gd)
        {
            if (!FrameActive) return;
            FrameActive = false;
            try
            {
                var submission = new VRFrameSubmission();
                if (Runtime.ShouldRender)
                {
                    if (EyesRendered)
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            var image = EyeSwapchains[i].Acquire();
                            GLInterop.BlitTexture(GLInterop.GetTextureHandle(EyeTargets[i]), EyeTargets[i].Width, EyeTargets[i].Height,
                                image, 0, 0, EyeSwapchains[i].Width, EyeSwapchains[i].Height, flipY: true);
                            EyeSwapchains[i].Release();
                        }
                        submission.ProjectionSwapchains = EyeSwapchains;
                        Wrists?.Submit(Runtime, ref submission);
                        if (PanelShown) SubmitPanel(ref submission);
                    }
                    else
                    {
                        SubmitScreen(gd, ref submission);
                    }
                    Keyboard?.Submit(Runtime, ref submission);
                    Menu?.Submit(Runtime, ref submission);
                    Setup?.Submit(Runtime, ref submission);
                }
                Runtime.EndFrame(submission);
            }
            catch (Exception e)
            {
                Fail("xrEndFrame failed", e);
            }
        }

        public static RenderTarget2D[] GetEyeTargets(GraphicsDevice gd) => EyeTargets;

        /// <summary>The render target the UI is captured into (window sized), for <see cref="VRUILayer"/>.</summary>
        public static RenderTarget2D GetUITarget(GraphicsDevice gd, int width, int height)
        {
            if (UITarget == null || UITarget.Width != width || UITarget.Height != height)
            {
                UITarget?.Dispose();
                UITarget = new RenderTarget2D(gd, Math.Max(1, width), Math.Max(1, height), false, SurfaceFormat.Color,
                    DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
            }
            return UITarget;
        }

        public static VRPose HeadPoseTracking() => LastHead;

        public static void OnImmersiveChanged(VRCameraController controller, bool immersive)
        {
            Log(immersive ? "entered first person (" + PlayMode + ")" : "left first person");
            if (!immersive) { ScreenPoseDirty = true; PanelShown = false; }
        }

        // ------------------------------------------------------------------ flat screen and panel

        private static void SubmitScreen(GraphicsDevice gd, ref VRFrameSubmission submission)
        {
            var pp = gd.PresentationParameters;
            int w = pp.BackBufferWidth, h = pp.BackBufferHeight;
            if (w <= 0 || h <= 0) return;
            if (ScreenSwapchain == null || ScreenSwapchain.Width != w || ScreenSwapchain.Height != h)
            {
                ScreenSwapchain?.Dispose();
                ScreenSwapchain = Runtime.CreateSwapchain(w, h);
            }
            if (ScreenPoseDirty) PlaceScreen();

            var image = ScreenSwapchain.Acquire();
            GLInterop.BlitBackbuffer(w, h, image, w, h);
            DrawCursor(image, new Rectangle(0, 0, w, h));
            ScreenSwapchain.Release();

            submission.AddQuad(new VRQuad
            {
                Swapchain = ScreenSwapchain,
                Pose = VRMath.Pose(ScreenPosition, ScreenOrientation),
                Width = Settings.ScreenWidth,
                Height = Settings.ScreenWidth * h / w
            });
        }

        private static void SubmitPanel(ref VRFrameSubmission submission)
        {
            if (UITarget == null) return;
            var region = Rectangle.Intersect(PanelRegion, new Rectangle(0, 0, UITarget.Width, UITarget.Height));
            if (region.Width <= 0 || region.Height <= 0) region = new Rectangle(0, 0, UITarget.Width, UITarget.Height);
            int w = region.Width, h = region.Height;
            if (PanelSwapchain == null || PanelSwapchain.Width != w || PanelSwapchain.Height != h)
            {
                PanelSwapchain?.Dispose();
                PanelSwapchain = Runtime.CreateSwapchain(w, h);
            }
            var image = PanelSwapchain.Acquire();
            GLInterop.BlitTextureRegion(GLInterop.GetTextureHandle(UITarget), region.X, region.Y, w, h, image, 0, 0, w, h, flipY: true);
            DrawCursor(image, region);
            PanelSwapchain.Release();

            submission.AddQuad(new VRQuad
            {
                Swapchain = PanelSwapchain,
                Pose = VRMath.Pose(PanelPosition, PanelOrientation),
                Width = PanelWidth,
                Height = PanelWidth * h / w,
                Transparent = true
            });
        }

        /// <param name="region">The part of the window the image shows.</param>
        private static void DrawCursor(uint image, Rectangle region)
        {
            if (!PointerActive || !CursorOnSurface) return;
            // GL origin is bottom-left.
            int cx = PointerPixel.X - region.X, cy = region.Height - 1 - (PointerPixel.Y - region.Y);
            int s = Math.Max(4, Math.Max(region.Height, region.Width * 9 / 16) / 120);
            GLInterop.FillRects(image, new (int, int, int, int, float, float, float)[]
            {
                (cx - s - 1, cy - s - 1, 2 * s + 2, 2 * s + 2, 0f, 0f, 0f),
                (cx - s + 1, cy - s + 1, 2 * s - 2, 2 * s - 2, PointerLeft ? 0.4f : 1f, 1f, PointerLeft ? 0.4f : 1f),
            });
        }

        private static void PlaceScreen()
        {
            var head = LastHead;
            if (!head.Valid) return;
            var yaw = VRMath.Yaw(head.Orientation);
            ScreenOrientation = Quaternion.CreateFromAxisAngle(Vector3.Up, yaw);
            var forward = Vector3.Transform(-Vector3.UnitZ, ScreenOrientation);
            ScreenPosition = head.Position + forward * Settings.ScreenDistance - new Vector3(0, 0.1f, 0);
            ScreenPoseDirty = false;
        }

        /// <param name="kind">Desk (buy/build catalogue): lower and tilted up towards you, so the world above stays
        /// clear. Dialog: just the dialog, at eye level. Full: the whole UI (pie menus).</param>
        private static void PlacePanel(PanelKind kind)
        {
            var head = LastHead;
            if (!head.Valid) return;
            var yaw = Quaternion.CreateFromAxisAngle(Vector3.Up, VRMath.Yaw(head.Orientation));
            var forward = Vector3.Transform(-Vector3.UnitZ, yaw);
            PanelKindShown = kind;
            if (kind == PanelKind.Dialog)
            {
                PanelWidth = Settings.PanelWidth;
                PanelOrientation = yaw;
                PanelPosition = head.Position + forward * Settings.PanelDistance - new Vector3(0, 0.08f, 0);
            }
            else if (kind == PanelKind.Desk)
            {
                PanelWidth = Settings.PanelWidth * 0.9f;
                PanelOrientation = yaw * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathHelper.ToRadians(-40));
                PanelPosition = head.Position + forward * (Settings.PanelDistance * 0.7f) - new Vector3(0, 0.55f, 0);
            }
            else
            {
                PanelWidth = Settings.PanelWidth;
                PanelOrientation = yaw;
                PanelPosition = head.Position + forward * Settings.PanelDistance - new Vector3(0, 0.15f, 0);
            }
        }

        /// <summary>The virtual mouse position used for the world in buy/build mode: the middle of the window,
        /// clear of the catalogue (bottom) and the item info panel (top).</summary>
        private static Point WorldCursorPixel(GraphicsDevice gd)
        {
            var pp = gd.PresentationParameters;
            return new Point(pp.BackBufferWidth / 2, pp.BackBufferHeight / 2);
        }

        private static bool HitsSurface(VRInput input, Vector3 position, Quaternion orientation, float width, float aspect)
        {
            var aim = input.RightAim.Valid ? input.RightAim : input.LeftAim;
            if (!aim.Valid) return false;
            var normal = Vector3.Transform(Vector3.UnitZ, orientation);
            var denom = Vector3.Dot(aim.Forward, normal);
            if (Math.Abs(denom) < 1e-4f) return false;
            var t = Vector3.Dot(position - aim.Position, normal) / denom;
            if (t <= 0) return false;
            var local = Vector3.Transform(aim.Position + aim.Forward * t - position, Quaternion.Inverse(orientation));
            return Math.Abs(local.X) <= width / 2 && Math.Abs(local.Y) <= width * aspect / 2;
        }

        /// <summary>Points the controller's ray at a flat surface; when it hits, the hit drives the mouse.</summary>
        private static void UpdatePointer(GraphicsDevice gd, VRInput input, Vector3 position, Quaternion orientation,
            float width, float aspect, bool armed, Rectangle region)
        {
            var aim = input.RightAim.Valid ? input.RightAim : input.LeftAim;
            if (!aim.Valid) return;

            var normal = Vector3.Transform(Vector3.UnitZ, orientation);
            var dir = aim.Forward;
            var denom = Vector3.Dot(dir, normal);
            if (Math.Abs(denom) < 1e-4f) return;
            var t = Vector3.Dot(position - aim.Position, normal) / denom;
            if (t <= 0) return;
            PanelPointerDistance = t;
            var hit = aim.Position + dir * t;
            var local = Vector3.Transform(hit - position, Quaternion.Inverse(orientation));
            float height = width * aspect;
            float u = local.X / width + 0.5f;
            float v = 0.5f - local.Y / height;
            if (u < 0 || u > 1 || v < 0 || v > 1) return;

            PointerPixel = new Point(region.X + (int)(u * region.Width), region.Y + (int)(v * region.Height));
            PointerLeft = armed && input.Select.Down;
            PointerRight = armed && input.Secondary.Down;
            ScrollAccumulator += input.Turn.Y * 900f * FrameDelta;
            ScrollOffset += (int)ScrollAccumulator;
            ScrollAccumulator -= (int)ScrollAccumulator;
            PointerActive = true;
            CursorOnSurface = true;
            if (armed && (input.Select.Pressed || input.Secondary.Pressed)) Runtime.Haptic(true, 0.25f, 0.015f);
        }

        /// <summary>
        /// Feeds the laser pointer into FreeSO's UI as the mouse (runs before the UI updates). The pointer's scrolling
        /// is an offset on the real wheel, applied whether or not the pointer is on a surface, so the zoom doesn't
        /// jump when the pointer moves on or off.
        /// </summary>
        private static void InjectPointer(UpdateState state)
        {
            if (state.NewKeys.Contains(Keys.F9) && SessionRunning) KeyboardMenuToggle = true;
            // the VR keyboard types into the text box that's waiting (between frames, like real key presses)
            KeyboardTarget = FindTextTarget(state);
            if (KeyboardTarget == null) KeyboardDismissed = null;
            if (Keyboard != null) Keyboard.TextTarget = KeyboardTarget; // the keyboard types into it as its keys are pressed
            var real = state.MouseState;
            if (!PointerActive)
            {
                if (ScrollOffset != 0)
                {
                    var withOffset = new MouseState(real.X, real.Y, real.ScrollWheelValue + ScrollOffset,
                        real.LeftButton, real.MiddleButton, real.RightButton, real.XButton1, real.XButton2);
                    state.MouseState = withOffset;
                    if (state.MouseStates.Count > 0) state.MouseStates[0].MouseState = withOffset;
                }
                return;
            }
            var ms = new MouseState(PointerPixel.X, PointerPixel.Y, real.ScrollWheelValue + ScrollOffset,
                PointerLeft ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released,
                PointerRight ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released, ButtonState.Released);
            state.MouseState = ms;
            if (state.MouseStates.Count > 0) state.MouseStates[0].MouseState = ms;
        }

        private static Rectangle FullRegion(GraphicsDevice gd)
        {
            var pp = gd.PresentationParameters;
            return new Rectangle(0, 0, Math.Max(1, pp.BackBufferWidth), Math.Max(1, pp.BackBufferHeight));
        }

        /// <summary>
        /// The band of the window a mobile-style dialog covers (Simitone's dialogs span the window's width and are
        /// centred vertically), so the panel can show just the dialog.
        /// </summary>
        private static Rectangle DialogRegion(GraphicsDevice gd, FSO.Client.UI.Framework.UIElement dialog)
        {
            var full = FullRegion(gd);
            if (!(dialog is UIMobileDialog md) || md.Width <= 0 || md.Height <= 0) return full;
            var topLeft = md.LocalPoint(Vector2.Zero);
            var bottomRight = md.LocalPoint(new Vector2(md.Width, md.Height));
            var unitsWide = bottomRight.X - topLeft.X;
            if (unitsWide <= 1) return full;
            var scale = full.Width / unitsWide; // UI units to window pixels
            int y0 = Math.Max(0, (int)Math.Floor(topLeft.Y * scale) - 8);
            int y1 = Math.Min(full.Height, (int)Math.Ceiling(bottomRight.Y * scale) + 8);
            return y1 - y0 < 32 ? full : new Rectangle(0, y0, full.Width, y1 - y0);
        }

        /// <summary>The top-most modal dialog in the game UI, or null.</summary>
        public static FSO.Client.UI.Framework.UIElement TopModalDialog()
        {
            var dialogs = Dialogs();
            if (dialogs == null) return null;
            for (int i = dialogs.Count - 1; i >= 0; i--) if (dialogs[i].Modal) return dialogs[i].Dialog;
            return null;
        }

        private static System.Collections.Generic.List<FSO.Client.UI.DialogReference> Dialogs()
        {
            var layer = FSO.Client.GameFacade.Screens;
            if (layer == null) return null;
            DialogsField ??= typeof(FSO.Client.UI.UILayer).GetField("Dialogs",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return DialogsField?.GetValue(layer) as System.Collections.Generic.List<FSO.Client.UI.DialogReference>;
        }

        // ------------------------------------------------------------------ messages on the watch

        private class VRMessage
        {
            public FSO.Client.UI.Framework.UIElement Alert;
            public string Title;
            public long Posted;
        }

        private static readonly System.Collections.Generic.List<VRMessage> Messages = new System.Collections.Generic.List<VRMessage>();
        private const int MaxMessages = 5;
        private const long MessageLifetimeMs = 120000;

        /// <summary>
        /// Messages that need no answer go to the watch instead of popping up, while you're in first person with the
        /// wrist displays on (<see cref="VRSettings.WatchMessages"/>).
        /// </summary>
        public static bool WatchTakesMessages => SessionRunning && Settings.WatchMessages && CurrentWorld?.Immersive == true
            && Settings.WristDisplays != VRWristMode.Off;

        /// <summary>Puts a message (an alert not yet shown) on the watch, with a buzz on the right wrist.</summary>
        public static void PostMessage(FSO.Client.UI.Framework.UIElement alert, string title)
        {
            Messages.Add(new VRMessage { Alert = alert, Title = title ?? "", Posted = Environment.TickCount64 });
            while (Messages.Count > MaxMessages) Messages.RemoveAt(0);
            Runtime?.Haptic(true, 0.5f, 0.12f);
            FSO.HIT.HITVM.Get()?.PlaySoundEvent(FSO.Client.UI.Model.UISounds.Whoosh);
            Log("message on the watch: " + title);
        }

        public static int MessageCount => Messages.Count;
        public static string MessageTitle => Messages.Count > 0 ? Messages[0].Title : "";

        /// <summary>Shows the oldest waiting message as a normal dialog. Call from the game update.</summary>
        public static void ShowOldestMessage()
        {
            if (Messages.Count == 0) return;
            var message = Messages[0];
            Messages.RemoveAt(0);
            FSO.Client.UI.Framework.UIScreen.GlobalShowDialog(message.Alert, true);
        }

        /// <summary>Called every update by the bridge: old messages expire; outside first person they're all shown.</summary>
        public static void UpdateMessages(bool immersive)
        {
            var now = Environment.TickCount64;
            Messages.RemoveAll(m => now - m.Posted > MessageLifetimeMs);
            if (!immersive) while (Messages.Count > 0) ShowOldestMessage();
        }

        /// <summary>The lot is gone: so are its messages.</summary>
        public static void ClearMessages() => Messages.Clear();

        // ------------------------------------------------------------------ keyboard target

        /// <summary>The text box the VR keyboard types into (set every update), or null.</summary>
        private static FSO.Client.UI.Controls.UITextEdit KeyboardTarget;
        private static FSO.Client.UI.Controls.UITextEdit KeyboardDismissed;

        /// <summary>
        /// The text box waiting for typing: the focused one if it's editable and on screen, else one in the top-most
        /// modal dialog (which is then focused, as clicking it would).
        /// </summary>
        private static FSO.Client.UI.Controls.UITextEdit FindTextTarget(UpdateState state)
        {
            if (!SessionRunning) return null;
            var dialog = TopModalDialog();
            if (state.InputManager?.GetFocus() is FSO.Client.UI.Controls.UITextEdit focused && !focused.IsReadOnly
                && IsShown(focused, dialog)) return focused;
            if (dialog is FSO.Client.UI.Framework.UIContainer container)
            {
                var edit = FindEdit(container);
                if (edit != null)
                {
                    state.InputManager?.SetFocus(edit);
                    return edit;
                }
            }
            return null;
        }

        /// <summary>Visible, and part of the top dialog (if one is open) or of the current screen.</summary>
        private static bool IsShown(FSO.Client.UI.Framework.UIElement element, FSO.Client.UI.Framework.UIElement dialog)
        {
            var screen = FSO.Client.GameFacade.Screens?.CurrentUIScreen;
            for (var e = element; e != null; e = e.Parent)
            {
                if (!e.Visible) return false;
                if (dialog != null ? e == dialog : e == screen) return true;
            }
            return false;
        }

        private static FSO.Client.UI.Controls.UITextEdit FindEdit(FSO.Client.UI.Framework.UIContainer container)
        {
            foreach (var child in container.GetChildren())
            {
                if (!child.Visible) continue;
                if (child is FSO.Client.UI.Controls.UITextEdit edit && !edit.IsReadOnly) return edit;
                if (child is FSO.Client.UI.Framework.UIContainer inner)
                {
                    var found = FindEdit(inner);
                    if (found != null) return found;
                }
            }
            return null;
        }

        /// <summary>True while a modal dialog is open in the game UI.</summary>
        public static bool DialogOpen() => TopModalDialog() != null;

        // ------------------------------------------------------------------ requests for the game

        private static bool Consume(ref bool flag)
        {
            var r = flag;
            flag = false;
            return r;
        }

        public static bool ConsumePauseRequest() => Consume(ref PauseRequested);
        public static bool ConsumeSpeedRequest() => Consume(ref SpeedRequested);
        public static bool ConsumeModeSwitch() => Consume(ref ModeSwitchRequested);
        public static bool ConsumeNextSim() => Consume(ref NextSimRequested);
        public static bool ConsumeBuildMenu() => Consume(ref BuildMenuRequested);
        public static bool ConsumeWorldSelect() => Consume(ref WorldSelectRequested);
        public static bool ConsumeCancel() => Consume(ref CancelRequested);

        /// <summary>A queue icon on the left wrist was chosen: the UID of the action to cancel.</summary>
        public static bool ConsumeQueueCancel(out ushort uid)
        {
            uid = (ushort)Math.Max(0, QueueCancelRequested);
            var requested = QueueCancelRequested >= 0;
            QueueCancelRequested = -1;
            return requested;
        }
    }
}
