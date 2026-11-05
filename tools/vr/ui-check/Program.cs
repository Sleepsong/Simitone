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
using Simitone.VR;

// Renders the VR panels with Simitone's real UI (its fonts, art and controls, through FreeSO's UI layer) to PNG files,
// with sample game data, and checks the Sim arm pose (VRSimArms) on a made-up skeleton. Needs only Simitone's own
// Content folder, not The Sims: see run.sh.
static class Program
{
    static int Main(string[] args)
    {
        var armFailures = ArmCheck.Run();
        if (args.Length > 0 && args[0] == "--arms-only") return armFailures == 0 ? 0 : 1;
        // args: <published Simitone dir> <output dir>
        var simitoneDir = Path.GetFullPath(args[0]);
        var outDir = Path.GetFullPath(args.Length > 1 ? args[1] : "out");
        Directory.CreateDirectory(outDir);
        Directory.SetCurrentDirectory(simitoneDir); // the content system scans "Content/"
        using (var game = new UIGame(outDir)) game.Run();
        Console.WriteLine(UIGame.Summary);
        return armFailures > 0 ? 1 : UIGame.ExitCode;
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
        var tip = new VRTipPanel();
        var head = new VRPose { Valid = true, Position = new Vector3(0, 1.6f, 0), Orientation = Quaternion.Identity };
        var textBox = new UITextBox();
        textBox.CurrentText = "Bella Go";
        keyboard.TextTarget = textBox;

        Shots.Add(("watch", wrists.Watch, () => wrists.Watch.Show()));
        Shots.Add(("needs", wrists.Needs, () => wrists.Needs.Show()));
        Shots.Add(("menu", menu, () => menu.Show()));
        Shots.Add(("setup-2", setup, () => setup.ShowPage(1)));
        Shots.Add(("setup-6", setup, () => setup.ShowPage(5)));
        Shots.Add(("tip", tip, () => tip.ShowAt("There isn't enough room to put that here: something is in the way.", true,
            new Vector3(0.4f, 1.2f, -2.5f), head)));
        Shots.Add(("keyboard", keyboard, () =>
        {
            keyboard.Show();
            keyboard.SetMouse(new Vector2(200, 230), false, false); // pointing at a key
        }));
    }

    // the controller model drawn in your hands when you're not in your Sim's body: from the side and from your eyes
    private void RenderController()
    {
        using (var model = new VRControllerModel(GraphicsDevice))
        using (var target = new RenderTarget2D(GraphicsDevice, 800, 400, false, SurfaceFormat.Color, DepthFormat.Depth24))
        {
            GraphicsDevice.SetRenderTarget(target);
            GraphicsDevice.Clear(new Color(150, 170, 190));
            var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(30), 1, 0.01f, 10);
            var views = new[]
            {
                Matrix.CreateLookAt(new Vector3(0.35f, 0.08f, 0), Vector3.Zero, Vector3.Up),           // from the right side
                Matrix.CreateLookAt(new Vector3(0.05f, 0.25f, 0.25f), new Vector3(0, 0, -0.03f), Vector3.Up) // from above and behind
            };
            for (int i = 0; i < views.Length; i++)
            {
                GraphicsDevice.Viewport = new Viewport(i * 400, 0, 400, 400);
                model.Draw(GraphicsDevice, Matrix.Identity, views[i], projection);
            }
            GraphicsDevice.SetRenderTarget(null);
            using (var file = File.Create(Path.Combine(OutDir, "controller.png"))) target.SaveAsPng(file, 800, 400);
        }
    }

    protected override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);
        if (ExitCode != 2) return;
        try
        {
            if (Shot >= Shots.Count)
            {
                RenderController();
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

// The Sim arm pose (VRSimArms.Solve) on a made-up skeleton shaped like The Sims' (ROOT, PELVIS, SPINE2, then for each
// side L_ARM / L_ARM1 / L_ARM2 / L_HAND / L_FINGER0: collarbone, upper arm, forearm, hand, fingers), arms out to the
// sides (T pose), Y up. Units are the game's world units (3 per tile).
static class ArmCheck
{
    static int Failures;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} arms: {name}: {detail}");
        if (!ok) Failures++;
    }

    static FSO.Vitaboy.Skeleton MakeSkeleton()
    {
        var list = new List<FSO.Vitaboy.Bone>();
        void B(string name, string parent, float x, float y, float z) => list.Add(new FSO.Vitaboy.Bone
        {
            Name = name, ParentName = parent, Translation = new Vector3(x, y, z), Rotation = Quaternion.Identity, Index = list.Count
        });
        B("ROOT", "NULL", 0, 0, 0);
        B("PELVIS", "ROOT", 0, 3.0f, 0);
        B("SPINE2", "PELVIS", 0, 1.3f, 0);
        B("HEAD", "SPINE2", 0, 0.6f, 0);
        foreach (var (side, sx) in new[] { ("L", 1f), ("R", -1f) })
        {
            B(side + "_ARM", "SPINE2", 0.25f * sx, 0.45f, 0);  // collarbone, from the neck
            B(side + "_ARM1", side + "_ARM", 0.3f * sx, 0, 0);  // upper arm, from the shoulder
            B(side + "_ARM2", side + "_ARM1", 0.9f * sx, 0, 0); // forearm, from the elbow
            B(side + "_HAND", side + "_ARM2", 0.8f * sx, 0, 0); // hand, from the wrist
            B(side + "_FINGER0", side + "_HAND", 0.3f * sx, 0, 0);
        }
        var skeleton = new FSO.Vitaboy.Skeleton { Name = "check", Bones = list.ToArray() };
        skeleton.BuildBoneDictionary();
        foreach (var bone in skeleton.Bones) bone.Children = Array.FindAll(skeleton.Bones, b => b.ParentName == bone.Name);
        skeleton.RootBone = skeleton.GetBone("ROOT");
        skeleton.ComputeBonePositions(skeleton.RootBone, Matrix.Identity);
        return skeleton;
    }

    static Matrix[] Matrices(FSO.Vitaboy.Skeleton skeleton) => Array.ConvertAll(skeleton.Bones, b => b.AbsoluteMatrix);

    static Vector3 P(FSO.Vitaboy.Skeleton s, Matrix[] m, string name) => m[s.GetBone(name).Index].Translation;

    public static int Run()
    {
        var arms = new VRSimArms();
        var forward = new Vector3(0, -0.3f, -1);
        forward.Normalize();

        // ---- reachable: a hand held out in front, a little below the shoulder, pointing forward and down
        {
            var s = MakeSkeleton();
            var m = Matrices(s);
            var shoulder = P(s, m, "L_ARM1");
            var target = shoulder + new Vector3(0.3f, -0.6f, -1.1f); // 1.29 from the shoulder; the arm reaches 1.7
            var ok = arms.Solve(s, m, 1, target, forward, null, Vector3.Zero);
            Check("finds both arms", ok, ok ? "L_ARM1 / L_ARM2 / L_HAND" : "not found");
            var wrist = P(s, m, "L_HAND");
            Check("the wrist reaches a target within reach", Vector3.Distance(wrist, target) < 1e-3f,
                $"{Vector3.Distance(wrist, target):0.00000} units away");
            var elbow = P(s, m, "L_ARM2");
            Check("the upper arm keeps its length", Math.Abs(Vector3.Distance(shoulder, elbow) - 0.9f) < 1e-3f,
                $"{Vector3.Distance(shoulder, elbow):0.0000} (0.9)");
            Check("the forearm keeps its length", Math.Abs(Vector3.Distance(elbow, wrist) - 0.8f) < 1e-3f,
                $"{Vector3.Distance(elbow, wrist):0.0000} (0.8)");
            var finger = P(s, m, "L_FINGER0");
            var pointing = Vector3.Normalize(finger - wrist);
            Check("the hand points the way the controller does", Vector3.Dot(pointing, forward) > 0.999f,
                $"dot {Vector3.Dot(pointing, forward):0.00000}");
            Check("the fingers stay on the hand", Math.Abs(Vector3.Distance(finger, wrist) - 0.3f) < 1e-3f,
                $"{Vector3.Distance(finger, wrist):0.0000} (0.3)");
            // the elbow bends down and out, not up
            var line = Vector3.Normalize(target - shoulder);
            var foot = shoulder + line * Vector3.Dot(elbow - shoulder, line);
            var bend = elbow - foot;
            Check("the elbow bends down", bend.Y < -0.05f, $"elbow is {bend.Y:0.000} below the shoulder-wrist line");
            Check("the elbow bends outward (left arm: +X)", bend.X > 0, $"x {bend.X:0.000}");
            Check("the bones' own matrices follow (what the hand holds stays in it)",
                s.GetBone("L_FINGER0").AbsolutePosition == finger && s.GetBone("L_HAND").AbsoluteMatrix == m[s.GetBone("L_HAND").Index],
                "absolute position and matrix updated");
            var right = MakeSkeleton();
            Check("the other arm is left alone", P(s, m, "R_HAND") == right.GetBone("R_HAND").AbsolutePosition, "unchanged");
        }

        // ---- out of reach: the arm straightens toward it
        {
            var s = MakeSkeleton();
            var m = Matrices(s);
            var shoulder = P(s, m, "R_ARM1");
            var target = shoulder + new Vector3(-0.5f, 0.2f, -4f);
            arms.Solve(s, m, 1, null, Vector3.Zero, target, forward);
            var wrist = P(s, m, "R_HAND");
            var along = Vector3.Dot(Vector3.Normalize(wrist - shoulder), Vector3.Normalize(target - shoulder));
            var reach = Vector3.Distance(wrist, shoulder);
            Check("out of reach: the arm points at the target", along > 0.9999f, $"dot {along:0.000000}");
            Check("out of reach: the arm is (almost) straight", reach > 1.69f && reach <= 1.7f, $"reach {reach:0.0000} of 1.7");
            var bend = P(s, m, "R_ARM2") - (shoulder + Vector3.Normalize(target - shoulder) * 0.9f);
            Check("out of reach: the elbow bends outward (right arm: -X) if at all", bend.X <= 1e-3f, $"x {bend.X:0.0000}");
        }

        // ---- too close: the arm folds as far as it can, without flipping through itself
        {
            var s = MakeSkeleton();
            var m = Matrices(s);
            var shoulder = P(s, m, "L_ARM1");
            arms.Solve(s, m, 1, shoulder + new Vector3(0.01f, -0.02f, -0.03f), forward, null, Vector3.Zero);
            var reach = Vector3.Distance(P(s, m, "L_HAND"), shoulder);
            Check("too close: the wrist stops where the arm is fully folded", reach > 0.1f && reach < 0.2f, $"reach {reach:0.000}");
            Check("too close: nothing is NaN", !float.IsNaN(reach) && !float.IsNaN(P(s, m, "L_FINGER0").X), "finite");
        }

        // ---- blending: halfway there at half weight, and no change at weight 0
        {
            var s = MakeSkeleton();
            var m = Matrices(s);
            var before = P(s, m, "L_HAND");
            var target = P(s, m, "L_ARM1") + new Vector3(0.2f, -0.7f, -1.0f);
            arms.Solve(s, m, 0.5f, target, forward, null, Vector3.Zero);
            var half = P(s, m, "L_HAND");
            var full = Vector3.Distance(before, target);
            var left = Vector3.Distance(half, target);
            Check("half weight: part of the way there", left > 0.15f * full && left < 0.85f * full,
                $"{left:0.000} of {full:0.000} still to go");
            var s0 = MakeSkeleton();
            var m0 = Matrices(s0);
            arms.Solve(s0, m0, 0, target, forward, null, Vector3.Zero);
            Check("weight 0: the arm is unchanged", P(s0, m0, "L_HAND") == before, "unchanged");
        }

        // ---- a skeleton without arms is left alone
        {
            var s = MakeSkeleton();
            var list = new List<FSO.Vitaboy.Bone>(s.Bones);
            var bare = new FSO.Vitaboy.Skeleton { Name = "pet", Bones = list.FindAll(b => !b.Name.Contains("_")).ToArray() };
            bare.BuildBoneDictionary();
            foreach (var bone in bare.Bones) bone.Children = Array.FindAll(bare.Bones, b => b.ParentName == bone.Name);
            Check("a skeleton with no arms (a pet) is left alone", !new VRSimArms().Solve(bare, Matrices(bare), 1,
                Vector3.Zero, forward, Vector3.Zero, forward), "Solve returns false");
        }
        return Failures;
    }
}
