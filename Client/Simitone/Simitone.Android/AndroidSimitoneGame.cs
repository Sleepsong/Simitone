using FSO.Client;
using FSO.Common;
using FSO.Common.Utils;
using FSO.LotView;
using Microsoft.Xna.Framework;
using Simitone.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;

namespace Simitone.Android
{
    /// <summary>
    /// Simitone's game, on Android: MonoGame runs the game loop on its own thread there (not the UI thread that made
    /// the game), so the engine is told which thread is the game's when the loop first runs. Without this, anything
    /// that hands work to "the game thread" and waits for it would wait forever. Also measures performance (see
    /// <see cref="PerfLog"/>).
    /// </summary>
    public class AndroidSimitoneGame : SimitoneGame
    {
        private readonly PerfLog Perf = new PerfLog();

        protected override void Initialize()
        {
            GameThread.Game = Thread.CurrentThread;
            FSOEnvironment.GameThread = Thread.CurrentThread;
            SimitoneActivity.Log("game loop thread: " + Thread.CurrentThread.ManagedThreadId);
            FixDepthStencilCaps();
            SetupFramebufferFetch();
            base.Initialize();
        }

        private static readonly int[] TwoDrawBuffers = { global::Android.Opengl.GLES30.GlColorAttachment0, global::Android.Opengl.GLES30.GlColorAttachment0 + 1 };

        /// <summary>
        /// 2D on phones: draw software depth in one pass when the GPU has framebuffer fetch and the patched shaders
        /// (Content/iOS/Effects/2DWorldBatchFetch) compile and link here; otherwise the two-pass way. Settings can turn it off.
        /// </summary>
        private void SetupFramebufferFetch()
        {
            FSOEnvironment.FramebufferFetch = false;
            if (FSOEnvironment.Enable3D) return; //(3D doesn't use software depth for the world)
            string why;
            try
            {
                var ext = (global::Android.Opengl.GLES20.GlGetString(global::Android.Opengl.GLES20.GlExtensions) ?? "").Split(' ');
                if (!FSO.Client.GlobalSettings.Default.FastDepth2D) why = "turned off in Settings";
                else if (!ext.Contains("GL_EXT_shader_framebuffer_fetch")) why = "the GPU has no framebuffer fetch";
                else why = TestFetchShaders();
            }
            catch (Exception e)
            {
                why = "check failed: " + e.Message;
            }
            if (why == null)
            {
                FSOEnvironment.FramebufferFetch = true;
                FSO.Common.Utils.PPXDepthEngine.DrawBuffersHook = (n) => global::Android.Opengl.GLES30.GlDrawBuffers(n, TwoDrawBuffers, 0);
                SimitoneActivity.Log("2D depth: one pass (framebuffer fetch)");
            }
            else SimitoneActivity.Log("2D depth: two passes (" + why + ")");
        }

        /// <summary>Compiles and links every shader pair of the one-pass 2D effect; null if all work, else the first error.</summary>
        private static string TestFetchShaders()
        {
            string text;
            using (var reader = new System.IO.StreamReader(global::Android.App.Application.Context.Assets.Open("Content/iOS/Effects/2DWorldBatchFetch.glsl.txt")))
                text = reader.ReadToEnd();
            var sources = new Dictionary<int, Tuple<bool, System.Text.StringBuilder>>();
            var programs = new List<Tuple<string, int, int>>();
            Tuple<bool, System.Text.StringBuilder> current = null;
            foreach (var line in text.Split('\n'))
            {
                if (line.StartsWith("@@shader "))
                {
                    var parts = line.Split(' ');
                    current = Tuple.Create(parts[2].Trim() == "vertex", new System.Text.StringBuilder());
                    sources[int.Parse(parts[1])] = current;
                }
                else if (line.StartsWith("@@program "))
                {
                    var parts = line.Split(' ');
                    programs.Add(Tuple.Create(parts[1], int.Parse(parts[2]), int.Parse(parts[3])));
                    current = null;
                }
                else current?.Item2.Append(line).Append('\n');
            }

            var handles = new Dictionary<int, int>();
            var status = new int[1];
            try
            {
                foreach (var source in sources)
                {
                    var shader = global::Android.Opengl.GLES20.GlCreateShader(source.Value.Item1 ? global::Android.Opengl.GLES20.GlVertexShader : global::Android.Opengl.GLES20.GlFragmentShader);
                    handles[source.Key] = shader;
                    global::Android.Opengl.GLES20.GlShaderSource(shader, source.Value.Item2.ToString());
                    global::Android.Opengl.GLES20.GlCompileShader(shader);
                    global::Android.Opengl.GLES20.GlGetShaderiv(shader, global::Android.Opengl.GLES20.GlCompileStatus, status, 0);
                    if (status[0] == 0)
                    {
                        var log = global::Android.Opengl.GLES20.GlGetShaderInfoLog(shader)?.Trim();
                        return $"shader {source.Key} didn't compile: {log}";
                    }
                }
                foreach (var program in programs)
                {
                    var handle = global::Android.Opengl.GLES20.GlCreateProgram();
                    global::Android.Opengl.GLES20.GlAttachShader(handle, handles[program.Item2]);
                    global::Android.Opengl.GLES20.GlAttachShader(handle, handles[program.Item3]);
                    global::Android.Opengl.GLES20.GlLinkProgram(handle);
                    global::Android.Opengl.GLES20.GlGetProgramiv(handle, global::Android.Opengl.GLES20.GlLinkStatus, status, 0);
                    var log = (status[0] == 0) ? global::Android.Opengl.GLES20.GlGetProgramInfoLog(handle)?.Trim() : null;
                    global::Android.Opengl.GLES20.GlDeleteProgram(handle);
                    if (status[0] == 0) return $"{program.Item1} didn't link: {log}";
                }
                return null;
            }
            finally
            {
                foreach (var shader in handles.Values) global::Android.Opengl.GLES20.GlDeleteShader(shader);
            }
        }

        /// <summary>
        /// MonoGame on Android only uses a packed 24-bit depth + 8-bit stencil buffer when the driver lists the old
        /// GL_OES_packed_depth_stencil / GL_OES_depth24 extensions. GLES 3 has both built in, and drivers may not list
        /// them; MonoGame then pairs a 16-bit depth buffer with a separate stencil buffer, which GLES 3 framebuffers
        /// don't accept. The 2D view's software depth needs the stencil, so turn them on for GLES 3 and up.
        /// </summary>
        private void FixDepthStencilCaps()
        {
            try
            {
                var version = global::Android.Opengl.GLES20.GlGetString(global::Android.Opengl.GLES20.GlVersion) ?? "";
                var ext = global::Android.Opengl.GLES20.GlGetString(global::Android.Opengl.GLES20.GlExtensions) ?? "";
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                var caps = typeof(Microsoft.Xna.Framework.Graphics.GraphicsDevice).GetProperty("GraphicsCapabilities", flags)?.GetValue(GraphicsDevice);
                var packed = caps?.GetType().GetProperty("SupportsPackedDepthStencil", flags);
                var depth24 = caps?.GetType().GetProperty("SupportsDepth24", flags);
                var line = $"depth buffers: driver lists packed depth-stencil {ext.Contains("GL_OES_packed_depth_stencil")}, depth24 {ext.Contains("GL_OES_depth24")}; MonoGame had {packed?.GetValue(caps)}/{depth24?.GetValue(caps)}";
                var es3 = version.StartsWith("OpenGL ES ") && version.Length > 10 && char.IsDigit(version[10]) && version[10] >= '3';
                if (es3 && packed != null && depth24 != null)
                {
                    packed.SetValue(caps, true);
                    depth24.SetValue(caps, true);
                    line += ", now on (GLES 3)";
                }
                SimitoneActivity.Log(line);
            }
            catch (Exception e)
            {
                SimitoneActivity.Log("depth buffers: couldn't check (" + e.Message + ")");
            }
        }

        protected override void Update(GameTime gameTime)
        {
            var start = Stopwatch.GetTimestamp();
            base.Update(gameTime);
            Perf.Updated(Stopwatch.GetTimestamp() - start);
        }

        protected override void Draw(GameTime gameTime)
        {
            var start = Stopwatch.GetTimestamp();
            base.Draw(gameTime);
            Perf.Drawn(Stopwatch.GetTimestamp() - start);
        }
    }

    /// <summary>
    /// Performance, every 5 seconds, into simitone.log: frames per second, how long frames take (average, 95% of
    /// frames under, worst), how long the game logic (update) and the drawing take, how many frames were slow,
    /// garbage collections (they pause the game) and memory. Frames over 150 ms are also logged one by one.
    /// </summary>
    public class PerfLog
    {
        private const double WindowSeconds = 5;
        private static readonly double TicksToMs = 1000.0 / Stopwatch.Frequency;

        private long WindowStart = Stopwatch.GetTimestamp();
        private long LastFrameEnd;
        private long UpdateTicksThisFrame;
        private int UpdatesThisFrame;

        private readonly double[] FrameMs = new double[2048];
        private int Frames, Updates, Slow, VerySlow, SpikesLogged;
        private double UpdateSum, UpdateMax, DrawSum, DrawMax;
        private int Gen0, Gen1, Gen2;
        private int LastRedraws;

        public PerfLog()
        {
            Gen0 = GC.CollectionCount(0); Gen1 = GC.CollectionCount(1); Gen2 = GC.CollectionCount(2);
        }

        public void Updated(long ticks)
        {
            UpdateTicksThisFrame += ticks;
            UpdatesThisFrame++;
            Updates++;
            var ms = ticks * TicksToMs;
            UpdateSum += ms;
            if (ms > UpdateMax) UpdateMax = ms;
        }

        public void Drawn(long drawTicks)
        {
            var now = Stopwatch.GetTimestamp();
            var drawMs = drawTicks * TicksToMs;
            DrawSum += drawMs;
            if (drawMs > DrawMax) DrawMax = drawMs;

            if (LastFrameEnd != 0)
            {
                var frameMs = (now - LastFrameEnd) * TicksToMs;
                if (Frames < FrameMs.Length) FrameMs[Frames] = frameMs;
                Frames++;
                if (frameMs > 33.4) Slow++;
                if (frameMs > 100) VerySlow++;
                if (frameMs > 150 && SpikesLogged < 5)
                {
                    SpikesLogged++;
                    SimitoneActivity.Log($"perf spike: frame {frameMs:0} ms (update {UpdateTicksThisFrame * TicksToMs:0} ms over {UpdatesThisFrame} update(s), draw {drawMs:0} ms) on {Where()}");
                }
            }
            LastFrameEnd = now;
            UpdateTicksThisFrame = 0;
            UpdatesThisFrame = 0;

            var elapsed = (now - WindowStart) / (double)Stopwatch.Frequency;
            if (elapsed >= WindowSeconds) Report(now, elapsed);
        }

        private static string Where()
        {
            string screen;
            try { screen = GameFacade.Screens?.CurrentUIScreen?.GetType().Name ?? "?"; }
            catch (Exception) { screen = "?"; }
            return $"{screen}, {(GraphicsModeControl.Mode == FSO.LotView.Model.GlobalGraphicsMode.Full3D ? "3D" : "2D")}";
        }

        private void Report(long now, double elapsed)
        {
            if (Frames > 0)
            {
                var n = Math.Min(Frames, FrameMs.Length);
                var sorted = new double[n];
                Array.Copy(FrameMs, sorted, n);
                Array.Sort(sorted);
                double sum = 0;
                foreach (var f in sorted) sum += f;
                var p95 = sorted[Math.Min(n - 1, (int)(n * 0.95))];
                var g0 = GC.CollectionCount(0); var g1 = GC.CollectionCount(1); var g2 = GC.CollectionCount(2);
                SimitoneActivity.Log($"perf [{Where()}] {Frames / elapsed:0.0} fps | frame avg {sum / n:0.0} ms, 95% under {p95:0.0}, worst {sorted[n - 1]:0} | " +
                    $"update avg {(Updates > 0 ? UpdateSum / Updates : 0):0.0} max {UpdateMax:0} ({Updates} updates) | draw avg {DrawSum / Frames:0.0} max {DrawMax:0} | " +
                    $"slow frames {Slow} (>33 ms), {VerySlow} (>100 ms) | GC {g0 - Gen0}/{g1 - Gen1}/{g2 - Gen2} | heap {GC.GetTotalMemory(false) / (1024 * 1024)} MB" +
                    $" | 2D redraws {FSO.LotView.WorldStatic.Redraws - LastRedraws}" +
                    ((FSO.LotView.WorldStatic.Redraws != LastRedraws) ? $" ({FSO.LotView.BlueprintChanges.TakeStaticReasons()})" : "") +
                    ((FSO.Files.RC.DGRP3DMesh.Building > 0) ? $" | 3D meshes building {FSO.Files.RC.DGRP3DMesh.Building}" : ""));
                LastRedraws = FSO.LotView.WorldStatic.Redraws;
                var texLoads = FSO.Content.Model.AbstractTextureRef.Loads;
                var texMs = FSO.Content.Model.AbstractTextureRef.LoadTicks * 1000 / Stopwatch.Frequency;
                if (texLoads > 0)
                {
                    if (texMs >= 100) SimitoneActivity.Log($"  textures loaded: {texLoads} ({texMs} ms)");
                    FSO.Content.Model.AbstractTextureRef.Loads = 0;
                    FSO.Content.Model.AbstractTextureRef.LoadTicks = 0;
                }
                Gen0 = g0; Gen1 = g1; Gen2 = g2;
            }
            WindowStart = now;
            Frames = Updates = Slow = VerySlow = SpikesLogged = 0;
            UpdateSum = UpdateMax = DrawSum = DrawMax = 0;
        }
    }
}
