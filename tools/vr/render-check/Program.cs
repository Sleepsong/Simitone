using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.VR;
using Simitone.VR;
using Simitone.VR.OpenXR;

// Renders the VR comfort overlay (vignette strengths, blink) to a PNG file, with the real Simitone.Client code.
// The VR panels are Simitone UI: tools/vr/ui-check renders those.
static class Program
{
    static int Main(string[] args)
    {
        var outDir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(outDir);
        using (var game = new RenderGame(outDir)) game.Run();
        Console.WriteLine(RenderGame.Summary);
        return RenderGame.ExitCode;
    }
}

class RenderGame : Game
{
    public static string Summary = "did not run";
    public static int ExitCode = 2;
    private readonly GraphicsDeviceManager Graphics;
    private readonly string OutDir;

    public RenderGame(string outDir)
    {
        OutDir = outDir;
        Graphics = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 640, PreferredBackBufferHeight = 360 };
    }

    protected override void Draw(GameTime gameTime)
    {
        try
        {
            RenderAll();
            Summary = "rendered to " + Path.GetFullPath(OutDir);
            ExitCode = 0;
        }
        catch (Exception e)
        {
            Summary = "FAIL: " + e;
            ExitCode = 1;
        }
        Exit();
    }

    private void Save(RenderTarget2D target, string name)
    {
        using var file = File.Create(Path.Combine(OutDir, name));
        target.SaveAsPng(file, target.Width, target.Height);
    }

    private void RenderAll()
    {
        var gd = GraphicsDevice;
        var batch = new SpriteBatch(gd);
        var white = new Texture2D(gd, 1, 1);
        white.SetData(new[] { Color.White });

        // ---- vignette and blink, over a grid, with an asymmetric (headset-like) left-eye field of view
        const int size = 360;
        var fov = new XrFovf { AngleLeft = -0.96f, AngleRight = 0.80f, AngleUp = 0.92f, AngleDown = -0.98f };
        var near = 0.15f;
        var projection = VRMath.Projection(fov, near, 1000f);
        var overlay = new VRComfortOverlay(gd);
        var cases = new (string name, float vignette, float fade)[]
        {
            ("none", 0, 0),
            ("low", VRCameraController.VignetteStrength(VRVignetteStrength.Low), 0),
            ("medium", VRCameraController.VignetteStrength(VRVignetteStrength.Medium), 0),
            ("high", VRCameraController.VignetteStrength(VRVignetteStrength.High), 0),
            ("blink half", 0, 0.5f),
        };
        var strip = new RenderTarget2D(gd, size * cases.Length, size + 30, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        var eye = new RenderTarget2D(gd, size, size, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
        var tiles = new RenderTarget2D[cases.Length];
        for (int i = 0; i < cases.Length; i++)
        {
            gd.SetRenderTarget(eye);
            gd.Clear(new Color(120, 170, 220));
            batch.Begin();
            for (int g = 0; g <= size; g += 30)
            {
                batch.Draw(white, new Rectangle(g, 0, 2, size), Color.White);
                batch.Draw(white, new Rectangle(0, g, size, 2), Color.White);
            }
            // where straight ahead is for this eye (the vignette should be centred here, not on the image centre)
            var ahead = Vector4.Transform(new Vector4(0, 0, -1, 1), projection);
            var px = (ahead.X / ahead.W * 0.5f + 0.5f) * size;
            var py = (0.5f - ahead.Y / ahead.W * 0.5f) * size;
            batch.Draw(white, new Rectangle((int)px - 5, (int)py - 5, 10, 10), Color.Red);
            batch.End();
            overlay.Draw(gd, projection, near, cases[i].vignette, cases[i].fade);
            gd.SetRenderTarget(null);
            tiles[i] = new RenderTarget2D(gd, size, size);
            gd.SetRenderTarget(tiles[i]);
            batch.Begin();
            batch.Draw(eye, Vector2.Zero, Color.White);
            batch.End();
        }
        gd.SetRenderTarget(strip);
        gd.Clear(Color.Black);
        batch.Begin();
        for (int i = 0; i < cases.Length; i++)
        {
            batch.Draw(tiles[i], new Vector2(i * size, 30), Color.White);
        }
        batch.End();
        gd.SetRenderTarget(null);
        Save(strip, "vignette.png");

    }
}
