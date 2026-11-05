using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.VR;
using Simitone.VR.Graphics;

static class Program
{
    static int Main(string[] args)
    {
        using (var game = new SmokeGame(args.Length > 0 ? int.Parse(args[0]) : 240)) game.Run();
        Console.WriteLine(SmokeGame.Summary);
        return SmokeGame.ExitCode;
    }
}

class SmokeGame : Game
{
    public static string Summary = "did not run";
    public static int ExitCode = 2;

    private readonly GraphicsDeviceManager Graphics;
    private readonly int FramesToRun;
    private VRRuntime Runtime;
    private VRSwapchain[] Eyes;
    private VRSwapchain Quad;
    private RenderTarget2D[] EyeTargets;
    private SpriteBatch Batch;
    private Texture2D White;
    private int Frames, Submitted, Focused;
    private int SyncedRight, SyncedLeft; // input synced with each hand layout's action set
    private bool OrientationChecked;
    private string Failure;

    public SmokeGame(int frames)
    {
        FramesToRun = frames;
        Graphics = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 640, PreferredBackBufferHeight = 360, SynchronizeWithVerticalRetrace = false };
        IsFixedTimeStep = false;
    }

    protected override void Initialize()
    {
        base.Initialize();
        Batch = new SpriteBatch(GraphicsDevice);
        White = new Texture2D(GraphicsDevice, 1, 1);
        White.SetData(new[] { Color.White });

        Runtime = new VRRuntime(s => Console.WriteLine(s));
        if (!Runtime.Initialize("Simitone VR smoke test", out var error))
        {
            Summary = "FAIL: " + error;
            ExitCode = 1;
            Exit();
            return;
        }
        int w = Runtime.RecommendedWidth / 2, h = Runtime.RecommendedHeight / 2;
        Eyes = new[] { Runtime.CreateSwapchain(w, h), Runtime.CreateSwapchain(w, h) };
        Quad = Runtime.CreateSwapchain(640, 360);
        EyeTargets = new[]
        {
            new RenderTarget2D(GraphicsDevice, w, h, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8),
            new RenderTarget2D(GraphicsDevice, w, h, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8)
        };
    }

    protected override void Draw(GameTime gameTime)
    {
        if (Runtime == null || Runtime.Session == 0) return;
        try
        {
            Frames++;
            // the second half of the run is left-handed: the other action set is synced and read
            Runtime.Input.LeftHanded = Frames > FramesToRun / 2;
            bool began = Runtime.BeginFrame(0.2f);
            if (Runtime.Focused) Focused++;
            if (Runtime.Input.Active) { if (Runtime.Input.LeftHanded) SyncedLeft++; else SyncedRight++; }

            // Each eye: top half red, bottom half blue (MonoGame/XNA coordinates, y down).
            for (int i = 0; i < 2; i++)
            {
                GraphicsDevice.SetRenderTarget(EyeTargets[i]);
                GraphicsDevice.Clear(Color.Black);
                Batch.Begin();
                Batch.Draw(White, new Rectangle(0, 0, EyeTargets[i].Width, EyeTargets[i].Height / 2), Color.Red);
                Batch.Draw(White, new Rectangle(0, EyeTargets[i].Height / 2, EyeTargets[i].Width, EyeTargets[i].Height / 2), Color.Blue);
                Batch.End();
            }
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.Green);

            if (!OrientationChecked)
            {
                // GL row 0 is the bottom row in OpenGL convention. MonoGame stores render targets flipped,
                // so row 0 of the RT texture holds the *top* (red) half.
                var tex = GLInterop.GetTextureHandle(EyeTargets[0]);
                var px = GLInterop.ReadPixel(tex, 1, 1);
                bool rtFlipped = (px & 0xFF) > 200 && ((px >> 16) & 0xFF) < 50;
                Console.WriteLine($"orientation: RT texel(1,1)=0x{px:x8} -> MonoGame RT stored {(rtFlipped ? "top-down (flip needed)" : "bottom-up")}");
                if (!rtFlipped) Failure = "MonoGame render target orientation differs from what GLInterop.BlitTexture assumes";

                // A region copy (the floating panel shows just an open dialog this way): the middle half of the image,
                // red above blue, must come out with red at the top (GL: last row) and blue at the bottom (GL: row 0).
                int w = EyeTargets[0].Width, h = EyeTargets[0].Height;
                using (var band = new RenderTarget2D(GraphicsDevice, w, h / 2))
                {
                    var bandTex = GLInterop.GetTextureHandle(band);
                    GLInterop.BlitTextureRegion(tex, 0, h / 4, w, h / 2, bandTex, 0, 0, w, h / 2, flipY: true);
                    var bottom = GLInterop.ReadPixel(bandTex, 1, 1);
                    var top = GLInterop.ReadPixel(bandTex, 1, h / 2 - 2);
                    bool regionOk = ((bottom >> 16) & 0xFF) > 200 && (bottom & 0xFF) < 50 && (top & 0xFF) > 200 && ((top >> 16) & 0xFF) < 50;
                    Console.WriteLine($"orientation: region copy bottom=0x{bottom:x8} top=0x{top:x8} -> {(regionOk ? "correct" : "WRONG")}");
                    if (!regionOk) Failure = "region copy (BlitTextureRegion) has the wrong orientation or offset";
                }
                OrientationChecked = true;
            }

            if (began)
            {
                var submission = new VRFrameSubmission();
                if (Runtime.ShouldRender)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        var image = Eyes[i].Acquire();
                        GLInterop.BlitTexture(GLInterop.GetTextureHandle(EyeTargets[i]), EyeTargets[i].Width, EyeTargets[i].Height,
                            image, 0, 0, Eyes[i].Width, Eyes[i].Height, flipY: true);
                        if (i == 0 && Submitted == 0)
                        {
                            // After the flip, GL row 0 of the swapchain image must be the bottom (blue) half.
                            var px = GLInterop.ReadPixel(image, 1, 1);
                            bool ok = ((px >> 16) & 0xFF) > 200 && (px & 0xFF) < 50;
                            Console.WriteLine($"orientation: swapchain texel(1,1)=0x{px:x8} -> {(ok ? "correct (bottom row = bottom of image)" : "WRONG")}");
                            if (!ok) Failure = "swapchain image orientation is wrong after blit";
                        }
                        Eyes[i].Release();
                    }
                    submission.ProjectionSwapchains = Eyes;

                    // Every other frame, also submit the backbuffer as a virtual screen.
                    if (Frames % 2 == 0)
                    {
                        var image = Quad.Acquire();
                        GLInterop.BlitBackbuffer(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height, image, Quad.Width, Quad.Height);
                        GLInterop.FillRects(image, new[] { (10, 10, 20, 20, 1f, 1f, 1f) });
                        Quad.Release();
                        submission.AddQuad(new VRQuad
                        {
                            Swapchain = Quad,
                            Pose = VRMath.Pose(new Vector3(0, 1.5f, -2), Quaternion.Identity),
                            Width = 1.6f,
                            Height = 0.9f,
                            Transparent = Frames % 4 == 0
                        });
                    }
                    Submitted++;
                }
                Runtime.EndFrame(submission);
            }

            var glError = GLInterop.GetError();
            if (glError != 0) Failure = $"GL error 0x{glError:x} on frame {Frames}";
        }
        catch (Exception e)
        {
            Failure = e.ToString();
        }

        if (Failure != null || Frames >= FramesToRun)
        {
            if (Failure == null && Focused > 10 && (SyncedRight == 0 || SyncedLeft == 0))
                Failure = $"input didn't sync in both hand layouts (right-handed {SyncedRight} frames, left-handed {SyncedLeft})";
            ExitCode = (Failure == null && Submitted > 0) ? 0 : 1;
            Summary = (ExitCode == 0 ? "PASS" : "FAIL") +
                $": {Frames} frames, {Submitted} submitted, {Focused} focused, state {Runtime.SessionState}, " +
                $"period {Runtime.PredictedDisplayPeriod * 1000:0.0} ms, views valid {Runtime.ViewsValid}, " +
                $"left eye at {Runtime.Views[0].Pose.Position.X:0.00},{Runtime.Views[0].Pose.Position.Y:0.00},{Runtime.Views[0].Pose.Position.Z:0.00}, " +
                $"input synced right-handed {SyncedRight} / left-handed {SyncedLeft} frames, " +
                $"bindings [{string.Join(", ", Runtime.Input.BoundProfiles)}]" + (Failure != null ? "\n" + Failure : "");
            foreach (var e in Eyes) e.Dispose();
            Quad.Dispose();
            Runtime.Dispose();
            Runtime = null;
            Exit();
        }
    }
}
