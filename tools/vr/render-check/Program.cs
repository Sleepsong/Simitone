using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.VR;
using Simitone.VR;
using Simitone.VR.OpenXR;

// Renders the VR comfort overlay, the VR options menu, the wrist displays and the VR keyboard to PNG files.
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
            VRPixelFont.Draw(batch, white, cases[i].name, new Vector2(i * size + 8, 8), 2, Color.White);
        }
        batch.End();
        gd.SetRenderTarget(null);
        Save(strip, "vignette.png");

        // ---- the VR options menu, with a row hovered
        var menu = new VRMenu();
        menu.Show(new VRPose { Valid = true, Orientation = Quaternion.Identity, Position = new Vector3(0, 1.6f, 0) }, null);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(VRMenu).GetField("Hover", flags).SetValue(menu, 0);
        menu.Render(gd);
        Save((RenderTarget2D)typeof(VRMenu).GetField("Target", flags).GetValue(menu), "menu.png");

        // ---- the wrist displays: a queue (second action pointed at, third being cancelled) and two messages waiting
        var hud = new VRWristHud();
        typeof(VRWristHud).GetProperty("NeedsVisible").GetSetMethod(true).Invoke(hud, new object[] { true });
        typeof(VRWristHud).GetProperty("WatchVisible").GetSetMethod(true).Invoke(hud, new object[] { true });
        hud.NeedsHover = 1;
        var data = new VRHudData { Valid = true, Hour = 15, Minute = 42, Speed = 2, Money = 20450, SimName = "Bella Goth" };
        for (int i = 0; i < 8; i++) data.Motives[i] = -90 + i * 26;
        var iconColours = new[] { Color.Orange, Color.MediumPurple, Color.SeaGreen };
        var names = new[] { "Have Snack", "Watch TV", "Take Shower" };
        for (int i = 0; i < 3; i++)
        {
            var icon = new Texture2D(gd, 32, 32);
            var pixels = new Color[32 * 32];
            for (int p = 0; p < pixels.Length; p++)
            {
                int x = p % 32, y = p / 32;
                var inside = (x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f) < 13 * 13;
                pixels[p] = inside ? iconColours[i] : Color.Transparent;
            }
            icon.SetData(pixels);
            var item = data.Queue[i];
            item.Icon = icon;
            item.Name = names[i];
            item.Active = i == 0;
            item.Cancelled = i == 2;
            item.UID = (ushort)(i + 1);
        }
        data.QueueCount = 3;
        data.Messages = 2;
        data.MessageTitle = "Job Promotion";
        hud.Render(gd, data);
        Save((RenderTarget2D)typeof(VRWristHud).GetField("Needs", flags).GetValue(hud), "needs.png");
        Save((RenderTarget2D)typeof(VRWristHud).GetField("Watch", flags).GetValue(hud), "watch.png");

        // ---- the keyboard, with some text typed and a key pointed at
        var keyboard = new VRKeyboard();
        keyboard.Show(new VRPose { Valid = true, Orientation = Quaternion.Identity, Position = new Vector3(0, 1.6f, 0) }, null);
        typeof(VRKeyboard).GetField("LastText", flags).SetValue(keyboard, "Bella Go");
        typeof(VRKeyboard).GetField("Hover", flags).SetValue(keyboard, 23);
        keyboard.Render(gd);
        Save((RenderTarget2D)typeof(VRKeyboard).GetField("Target", flags).GetValue(keyboard), "keyboard.png");
    }
}
