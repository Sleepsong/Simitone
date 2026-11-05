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
    ///   BeginDraw  - xrWaitFrame / xrBeginFrame, locate eyes, read controllers.
    ///   (draw)     - if the immersive camera is active, VRLotWorld renders both eyes into EyeTargets;
    ///                otherwise Simitone draws normally to the window.
    ///   EndDraw    - copy the eyes into the OpenXR swapchains (immersive), or copy the whole window onto a
    ///                virtual screen quad (menus, CAS, neighbourhood, build/buy...), then xrEndFrame.
    ///
    /// While the virtual screen is shown, the right controller is a laser pointer driving the mouse.
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

        /// <summary>Supplies the tile and floor of the selected Sim (B button jumps there in immersive mode).</summary>
        public static Func<(Vector2 tile, sbyte level)?> FocusProvider;

        /// <summary>
        /// True while a modal dialog is open. The game UI (and its dialogs) is hidden in the immersive view, so we
        /// step out to the virtual screen until the dialog is answered, then put the player back where they were.
        /// </summary>
        public static Func<bool> DialogOpenProvider = DefaultDialogOpen;
        private static bool LeftImmersiveForDialog;
        private static System.Reflection.FieldInfo DialogsField;

        // frame state
        public static bool FrameActive { get; private set; }
        public static float FrameDelta { get; private set; } = 1 / 90f;
        public static bool EyesRendered;
        public static bool WantsEyes => FrameActive && Runtime.ShouldRender && Runtime.ViewsValid;
        public static int MirrorEye { get; private set; }

        private static RenderTarget2D[] EyeTargets;
        private static VRSwapchain[] EyeSwapchains;
        private static VRSwapchain ScreenSwapchain;
        private static VRPose LastHead;
        private static Stopwatch FrameTimer;

        // virtual screen
        private static bool ScreenPoseDirty = true;
        private static Vector3 ScreenPosition;
        private static Quaternion ScreenOrientation = Quaternion.Identity;
        private static bool PointerActive;
        private static Point PointerPixel;
        private static int PointerScroll;
        private static float ScrollAccumulator;
        private static bool PointerLeft, PointerRight;

        // requests consumed by the game screen
        private static bool PauseRequested, SpeedRequested;

        private static Game Game;
        private static StreamWriter LogFile;

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
            MirrorEye = Settings.MirrorEye?.ToLowerInvariant() switch { "right" => 1, "none" => -1, _ => 0 };

            // The headset paces rendering (xrWaitFrame), so desktop vsync must not also block us.
            graphics.SynchronizeWithVerticalRetrace = false;
            graphics.ApplyChanges();
            if (!(FSOEnvironment.Args ?? "").Contains("-hz") && Settings.UpdateRate > 0)
            {
                FSOEnvironment.RefreshRate = Settings.UpdateRate;
                game.TargetElapsedTime = TimeSpan.FromTicks(10000000 / Settings.UpdateRate);
            }

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
                var gd = game.GraphicsDevice;
                float scale = MathHelper.Clamp(Settings.RenderScale, 0.25f, 2f);
                int w = Math.Min(Runtime.MaxWidth, Math.Max(64, (int)(Runtime.RecommendedWidth * scale)));
                int h = Math.Min(Runtime.MaxHeight, Math.Max(64, (int)(Runtime.RecommendedHeight * scale)));
                EyeSwapchains = new[] { Runtime.CreateSwapchain(w, h), Runtime.CreateSwapchain(w, h) };
                EyeTargets = new[]
                {
                    new RenderTarget2D(gd, w, h, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents),
                    new RenderTarget2D(gd, w, h, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents)
                };
                Log($"eye targets {w}x{h} (render scale {scale:0.##}), update rate {FSOEnvironment.RefreshRate} Hz");
            }
            catch (Exception e)
            {
                Fail("could not create eye buffers", e);
                return;
            }

            Runtime.ReferenceSpaceChanged += () => ScreenPoseDirty = true;
            GameThread.EveryUpdate(InjectPointer);
            Status = $"VR running on {Runtime.SystemName} ({Runtime.RuntimeName})";
            Log(Status);
        }

        public static void Shutdown()
        {
            if (Runtime == null) return;
            try
            {
                if (EyeSwapchains != null) foreach (var s in EyeSwapchains) s.Dispose();
                ScreenSwapchain?.Dispose();
                Runtime.Dispose();
            }
            catch { }
            EyeSwapchains = null;
            ScreenSwapchain = null;
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
            if (world != null)
            {
                bool dialogOpen = DialogOpenProvider?.Invoke() == true;
                if (immersive && dialogOpen && !LeftImmersiveForDialog)
                {
                    LeftImmersiveForDialog = true;
                    Runtime.Haptic(true, 0.6f, 0.08f);
                    GameThread.NextUpdate(_ => world.ToggleImmersive());
                }
                else if (immersive && !dialogOpen)
                {
                    LeftImmersiveForDialog = false; // player went back in by themselves
                }
                else if (!immersive && !dialogOpen && LeftImmersiveForDialog)
                {
                    LeftImmersiveForDialog = false;
                    GameThread.NextUpdate(_ => { if (!world.Immersive) world.ToggleImmersive(resume: true); });
                }
            }

            var input = Runtime.Input;
            if (input == null || !input.Active) { PointerActive = false; return; }

            if (input.ToggleView.Pressed && world != null)
                GameThread.NextUpdate(_ => world.ToggleImmersive());
            if (input.Recenter.Pressed)
            {
                if (immersive && FocusProvider?.Invoke() is (Vector2 tile, sbyte level))
                    GameThread.NextUpdate(_ => world.VRCamera.JumpTo(tile, level));
                else
                    ScreenPoseDirty = true;
            }
            if (input.Pause.Pressed) PauseRequested = true;
            if (input.Speed.Pressed) SpeedRequested = true;

            if (!immersive) UpdatePointer(gd, input);
            else PointerActive = false;
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
                    }
                    else
                    {
                        SubmitScreen(gd, ref submission);
                    }
                }
                Runtime.EndFrame(submission);
            }
            catch (Exception e)
            {
                Fail("xrEndFrame failed", e);
            }
        }

        public static RenderTarget2D[] GetEyeTargets(GraphicsDevice gd) => EyeTargets;

        public static VRPose HeadPoseTracking() => LastHead;

        public static void OnImmersiveChanged(VRCameraController controller, bool immersive)
        {
            Log(immersive ? "entered immersive view" : "left immersive view");
            if (!immersive) ScreenPoseDirty = true;
        }

        // ------------------------------------------------------------------ virtual screen

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
            if (PointerActive)
            {
                // GL origin is bottom-left.
                int cx = PointerPixel.X, cy = h - 1 - PointerPixel.Y;
                int s = Math.Max(4, h / 120);
                GLInterop.FillRects(image, new (int, int, int, int, float, float, float)[]
                {
                    (cx - s - 1, cy - s - 1, 2 * s + 2, 2 * s + 2, 0f, 0f, 0f),
                    (cx - s + 1, cy - s + 1, 2 * s - 2, 2 * s - 2, PointerLeft ? 0.4f : 1f, 1f, PointerLeft ? 0.4f : 1f),
                });
            }
            ScreenSwapchain.Release();

            submission.QuadSwapchain = ScreenSwapchain;
            submission.QuadPose = VRMath.Pose(ScreenPosition, ScreenOrientation);
            submission.QuadWidth = Settings.ScreenWidth;
            submission.QuadHeight = Settings.ScreenWidth * h / w;
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

        private static void UpdatePointer(GraphicsDevice gd, VRInput input)
        {
            var aim = input.RightAim.Valid ? input.RightAim : input.LeftAim;
            PointerActive = false;
            if (!aim.Valid || ScreenPoseDirty || ScreenSwapchain == null) return;

            var normal = Vector3.Transform(Vector3.UnitZ, ScreenOrientation);
            var dir = aim.Forward;
            var denom = Vector3.Dot(dir, normal);
            if (Math.Abs(denom) < 1e-4f) return;
            var t = Vector3.Dot(ScreenPosition - aim.Position, normal) / denom;
            if (t <= 0) return;
            var hit = aim.Position + dir * t;
            var local = Vector3.Transform(hit - ScreenPosition, Quaternion.Inverse(ScreenOrientation));
            float width = Settings.ScreenWidth;
            float height = width * ScreenSwapchain.Height / ScreenSwapchain.Width;
            float u = local.X / width + 0.5f;
            float v = 0.5f - local.Y / height;
            if (u < 0 || u > 1 || v < 0 || v > 1) return;

            var pp = gd.PresentationParameters;
            PointerPixel = new Point((int)(u * pp.BackBufferWidth), (int)(v * pp.BackBufferHeight));
            PointerLeft = input.Select.Down;
            PointerRight = input.Secondary.Down;
            ScrollAccumulator += input.Turn.Y * 900f * FrameDelta;
            PointerScroll += (int)ScrollAccumulator;
            ScrollAccumulator -= (int)ScrollAccumulator;
            PointerActive = true;
            if (input.Select.Pressed || input.Secondary.Pressed) Runtime.Haptic(true, 0.25f, 0.015f);
        }

        /// <summary>Feeds the laser pointer into FreeSO's UI as the mouse (runs before the UI updates).</summary>
        private static void InjectPointer(UpdateState state)
        {
            if (!PointerActive) return;
            var ms = new MouseState(PointerPixel.X, PointerPixel.Y, PointerScroll,
                PointerLeft ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released,
                PointerRight ? ButtonState.Pressed : ButtonState.Released,
                ButtonState.Released, ButtonState.Released);
            state.MouseState = ms;
            if (state.MouseStates.Count > 0) state.MouseStates[0].MouseState = ms;
        }

        private static bool DefaultDialogOpen()
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

        // ------------------------------------------------------------------ requests for the game screen

        public static bool ConsumePauseRequest()
        {
            var r = PauseRequested;
            PauseRequested = false;
            return r;
        }

        public static bool ConsumeSpeedRequest()
        {
            var r = SpeedRequested;
            SpeedRequested = false;
            return r;
        }
    }
}
