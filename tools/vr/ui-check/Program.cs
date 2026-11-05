using System;
using System.Collections.Generic;
using System.IO;
using FSO.Client;
using FSO.Client.UI;
using FSO.Client.UI.Controls;
using FSO.Client.UI.Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.VR;

// Renders the VR panels with Simitone's real UI (its fonts, art and controls, through FreeSO's UI layer) to PNG files,
// with sample game data. Needs only Simitone's own Content folder, not The Sims: see run.sh.
static class Program
{
    static int Main(string[] args)
    {
        // args: <published Simitone dir> <output dir>
        var simitoneDir = Path.GetFullPath(args[0]);
        var outDir = Path.GetFullPath(args.Length > 1 ? args[1] : "out");
        Directory.CreateDirectory(outDir);
        Directory.SetCurrentDirectory(simitoneDir); // the content system scans "Content/"
        using (var game = new UIGame(outDir)) game.Run();
        Console.WriteLine(UIGame.Summary);
        return UIGame.ExitCode;
    }
}

// Built on FreeSO's Game, as SimitoneGame is, so the UI layer runs as it does in the game.
class UIGame : FSO.Common.Rendering.Framework.Game
{
    public static string Summary = "did not run";
    public static int ExitCode = 2;
    private readonly string OutDir;
    private UILayer UI;
    private int Frame;
    private readonly List<(string name, VRUIPanel panel, Action before)> Shots = new List<(string, VRUIPanel, Action)>();
    private int Shot;
    private int ShotFrame;

    public UIGame(string outDir)
    {
        OutDir = outDir;
        Graphics.PreferredBackBufferWidth = 1280;
        Graphics.PreferredBackBufferHeight = 720;
        Content.RootDirectory = "Content/OGL";
    }

    protected override void Initialize()
    {
        base.Initialize();
        GameFacade.Game = this;
        Screen.Layers.Add(UI);
        try { MakePanels(); }
        catch (Exception e)
        {
            Summary = "FAIL making the panels: " + e;
            ExitCode = 1;
            Exit();
        }
    }

    protected override void LoadContent()
    {
        // what SimitoneGame sets up for its UI, minus everything that needs The Sims
        GameFacade.GraphicsDevice = GraphicsDevice;
        FSO.Common.FSOEnvironment.GameThread = System.Threading.Thread.CurrentThread;
        GameFacade.GameThread = System.Threading.Thread.CurrentThread;
        GameFacade.MainFont = new FSO.Client.UI.Framework.Font();
        GameFacade.EdithFont = new FSO.Client.UI.Framework.Font();
        GameFacade.VectorFont = new FSO.UI.Framework.MSDFFont(Content.Load<MSDFData.FieldFont>("../Fonts/mobile"));
        GameFacade.EdithVectorFont = new FSO.UI.Framework.MSDFFont(Content.Load<MSDFData.FieldFont>("../Fonts/trebuchet"));
        FSO.UI.Framework.MSDFFont.MSDFEffect = Content.Load<Effect>("Effects/MSDFFont");
        FSO.Content.Content.TS1Hybrid = true;
        FSO.Content.Content.InitBasic(Path.Combine(OutDir, "no-sims"), GraphicsDevice);
        UI = new UILayer(this);
        GameFacade.Screens = UI;
    }

    private Texture2D Dot(Color colour)
    {
        var pixels = new Color[64 * 64];
        for (int i = 0; i < pixels.Length; i++)
        {
            float x = i % 64 - 31.5f, y = i / 64 - 31.5f;
            var inside = x * x + y * y < 26 * 26;
            pixels[i] = inside ? colour : Color.Transparent;
        }
        var texture = new Texture2D(GraphicsDevice, 64, 64);
        texture.SetData(pixels);
        return texture;
    }

    private void MakePanels()
    {
        // sample game state: a Sim with three actions queued (the third being cancelled) and two messages waiting
        var hud = new VRHudData { Valid = true, Hour = 15, Minute = 42, Speed = 2, Money = 20450, SimName = "Bella Goth" };
        for (int i = 0; i < 8; i++) hud.Motives[i] = -90 + i * 26;
        var names = new[] { "Have Snack", "Watch TV", "Take Shower" };
        var colours = new[] { Color.Orange, Color.MediumPurple, Color.SeaGreen };
        for (int i = 0; i < 3; i++)
        {
            var item = hud.Queue[i];
            item.Icon = Dot(colours[i]);
            item.Name = names[i];
            item.Active = i == 0;
            item.Cancelled = i == 2;
            item.UID = (ushort)(i + 1);
        }
        hud.QueueCount = 3;
        hud.Messages = 2;
        hud.MessageTitle = "Job Promotion";
        SimitoneVR.HudProvider = () => hud;

        var wrists = new VRWristHud();
        var menu = new VRMenu();
        var setup = new VRSetup();
        var keyboard = new VRKeyboard();
        var textBox = new UITextBox();
        textBox.CurrentText = "Bella Go";
        keyboard.TextTarget = textBox;

        Shots.Add(("watch", wrists.Watch, () => wrists.Watch.Show()));
        Shots.Add(("needs", wrists.Needs, () => wrists.Needs.Show()));
        Shots.Add(("menu", menu, () => menu.Show()));
        Shots.Add(("setup-2", setup, () => setup.ShowPage(1)));
        Shots.Add(("setup-6", setup, () => setup.ShowPage(5)));
        Shots.Add(("keyboard", keyboard, () =>
        {
            keyboard.Show();
            keyboard.SetMouse(new Vector2(200, 230), false, false); // pointing at a key
        }));
    }

    protected override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);
        if (ExitCode != 2) return;
        try
        {
            if (Shot >= Shots.Count)
            {
                Summary = "rendered to " + OutDir;
                ExitCode = 0;
                Exit();
                return;
            }
            var (name, panel, before) = Shots[Shot];
            if (ShotFrame == 0) before();
            // let it settle: the UI layer updates and renders it, and its open animations finish
            if (++ShotFrame < 50) return;
            var image = panel.Image;
            if (image == null) throw new Exception(name + " has no image");
            using (var file = File.Create(Path.Combine(OutDir, name + ".png"))) image.SaveAsPng(file, image.Width, image.Height);
            panel.Hide();
            Shot++;
            ShotFrame = 0;
        }
        catch (Exception e)
        {
            Summary = "FAIL: " + e;
            ExitCode = 1;
            Exit();
        }
    }
}
