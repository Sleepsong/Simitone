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
        private static bool PanelDesk;
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
                Wrists = new VRWristHud();
                Menu = new VRMenu();
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
            if (EyeSwapchains != null) foreach (var s in EyeSwapchains) s.Dispose();
            if (EyeTargets != null) foreach (var t in EyeTargets) t.Dispose();
            EyeSwapchains = null;
            EyeTargets = null;
            float scale = MathHelper.Clamp(Settings.RenderScale, 0.25f, 2f);
            int w = Math.Min(Runtime.MaxWidth, Math.Max(64, (int)(Runtime.RecommendedWidth * scale)));
            int h = Math.Min(Runtime.MaxHeight, Math.Max(64, (int)(Runtime.RecommendedHeight * scale)));
            EyeSwapchains = new[] { Runtime.CreateSwapchain(w, h), Runtime.CreateSwapchain(w, h) };
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

        public static bool MenuOpen => Menu?.Open == true;
        /// <summary>How far (metres) the controller is from where it points on the open VR menu; 0 when not on it.</summary>
        public static float MenuPointerDistance => Menu?.PointerDistance ?? 0;

        public static void Shutdown()
        {
            if (Runtime == null) return;
            try
            {
                if (EyeSwapchains != null) foreach (var s in EyeSwapchains) s.Dispose();
                ScreenSwapchain?.Dispose();
                PanelSwapchain?.Dispose();
                Wrists?.Dispose();
                Menu?.Dispose();
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

            // ---- floating UI panel (first person only): pie menus and dialogs straight ahead, the buy/build
            // catalogue lower down and tilted up like a desk
            var showPanel = immersive && (UIPanelWanted || BuildMode);
            if (showPanel && (!PanelShown || PanelDesk != BuildMode)) { PlacePanel(BuildMode); PanelArmed = false; }
            PanelShown = showPanel && LastHead.Valid;

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

            // ---- VR options menu: the left menu button, the trigger on the left wrist's needs display, or F9
            if (immersive && Wrists != null) Wrists.UpdatePlacement(input, LastHead, Settings.WristDisplays);
            var menuWasOpen = MenuOpen;
            if (Menu != null)
            {
                var wristAimed = immersive && inputActive && !Menu.Open && Wrists != null && Wrists.PointsAtNeeds(input.RightAim);
                if (Wrists != null) Wrists.NeedsHighlight = wristAimed;
                if ((inputActive && input.Menu.Pressed) || KeyboardMenuToggle || (wristAimed && input.Select.Pressed))
                {
                    if (Menu.Open) Menu.Close();
                    else Menu.Show(LastHead, inputActive ? input : null);
                }
                KeyboardMenuToggle = false;
                if (Menu.Open) Menu.Update(input, Runtime);
            }

            // ---- laser pointer (the menu has it while it's open, including the frame a press closes it)
            PointerActive = false;
            CursorOnSurface = false;
            PanelPointerOnPanel = false;
            PanelPointerDistance = 0;
            if (inputActive && !menuWasOpen && !MenuOpen)
            {
                if (!immersive)
                {
                    if (ScreenSwapchain != null && !ScreenPoseDirty)
                        UpdatePointer(gd, input, ScreenPosition, ScreenOrientation, Settings.ScreenWidth,
                            (float)ScreenSwapchain.Height / ScreenSwapchain.Width, armed: true);
                }
                else if (PanelShown)
                {
                    if (!input.Select.Down) PanelArmed = WorldArmed = true;
                    var pp = gd.PresentationParameters;
                    var aspect = (float)pp.BackBufferHeight / Math.Max(1, pp.BackBufferWidth);
                    // a trigger held while sliding onto the panel (e.g. dragging a wall) doesn't click there
                    if (!LastOnPanel && input.Select.Down && HitsSurface(input, PanelPosition, PanelOrientation, PanelWidth, aspect))
                        PanelArmed = false;
                    UpdatePointer(gd, input, PanelPosition, PanelOrientation, PanelWidth, aspect, PanelArmed);
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
                        if (input.Select.Pressed) WorldSelectRequested = true; // beside the panel
                    }
                    if (input.Secondary.Pressed && !PanelPointerOnPanel) CancelRequested = true;
                    LastOnPanel = PanelPointerOnPanel;
                }
                else
                {
                    if (input.Select.Pressed) WorldSelectRequested = true;
                    if (input.Secondary.Pressed) CancelRequested = true;
                }
            }

            var vrCamera = world?.VRCamera;
            if (vrCamera != null) vrCamera.CursorPixel = WorldCursorPixel(gd).ToVector2() / FSOEnvironment.DPIScaleFactor;

            // ---- wrist displays and the menu (drawn now: switching render targets later would disturb the frame)
            if (immersive && Wrists != null)
            {
                try { Wrists.Render(gd, HudProvider?.Invoke()); }
                catch (Exception e) { Log("wrist display error: " + e.Message); }
            }
            try { Menu?.Render(gd); }
            catch (Exception e) { Log("VR menu error: " + e.Message); }
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
                    Menu?.Submit(Runtime, ref submission);
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
            DrawCursor(image, h);
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
            int w = UITarget.Width, h = UITarget.Height;
            if (PanelSwapchain == null || PanelSwapchain.Width != w || PanelSwapchain.Height != h)
            {
                PanelSwapchain?.Dispose();
                PanelSwapchain = Runtime.CreateSwapchain(w, h);
            }
            var image = PanelSwapchain.Acquire();
            GLInterop.BlitTexture(GLInterop.GetTextureHandle(UITarget), w, h, image, 0, 0, w, h, flipY: true);
            DrawCursor(image, h);
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

        private static void DrawCursor(uint image, int height)
        {
            if (!PointerActive || !CursorOnSurface) return;
            // GL origin is bottom-left.
            int cx = PointerPixel.X, cy = height - 1 - PointerPixel.Y;
            int s = Math.Max(4, height / 120);
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

        /// <param name="desk">Buy/build catalogue: lower and tilted up towards you, so the world above stays clear.</param>
        private static void PlacePanel(bool desk)
        {
            var head = LastHead;
            if (!head.Valid) return;
            var yaw = Quaternion.CreateFromAxisAngle(Vector3.Up, VRMath.Yaw(head.Orientation));
            var forward = Vector3.Transform(-Vector3.UnitZ, yaw);
            PanelDesk = desk;
            if (desk)
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
            float width, float aspect, bool armed)
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

            var pp = gd.PresentationParameters;
            PointerPixel = new Point((int)(u * pp.BackBufferWidth), (int)(v * pp.BackBufferHeight));
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

        /// <summary>True while a modal dialog is open in the game UI.</summary>
        public static bool DialogOpen()
        {
            var layer = FSO.Client.GameFacade.Screens;
            if (layer == null) return false;
            DialogsField ??= typeof(FSO.Client.UI.UILayer).GetField("Dialogs",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (DialogsField?.GetValue(layer) is System.Collections.Generic.List<FSO.Client.UI.DialogReference> dialogs)
            {
                foreach (var d in dialogs) if (d.Modal) return true;
            }
            return false;
        }

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
    }
}
