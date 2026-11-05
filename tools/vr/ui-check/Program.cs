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
        GltfCheck.ParseOnly();
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
    private VRCatalogThumbnails CatalogThumbs;

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
        Shots.Add(("setup-3", setup, () => setup.ShowPage(2)));
        Shots.Add(("setup-7", setup, () => setup.ShowPage(6)));
        Shots.Add(("tip", tip, () => tip.ShowAt("There isn't enough room to put that here: something is in the way.", true,
            new Vector3(0.4f, 1.2f, -2.5f), head)));
        Shots.Add(("keyboard", keyboard, () =>
        {
            keyboard.Show();
            keyboard.SetMouse(new Vector2(200, 230), false, false); // pointing at a key
        }));

        // the catalogue tablet: buy mode with a subcategory open (the first two items have made-up 3D meshes; the
        // chair is held, the sofa pointed at, the throne too dear), and build mode choosing a subcategory
        CatalogCheck.LoadEffect(Content);
        CatalogCheck.MakeMeshes(GraphicsDevice);
        CatalogThumbs = new VRCatalogThumbnails(GraphicsDevice) { Meshes = CatalogCheck.Meshes };
        var buy = new VRCatalogTablet { Source = new SampleCatalog(false, Dot), Thumbnail = CatalogThumbs.Get };
        var build = new VRCatalogTablet { Source = new SampleCatalog(true, Dot) };
        Shots.Add(("catalogue-buy", buy, () =>
        {
            buy.Show();
            buy.SetMouse(new Vector2(30 + 14 + 184 + 86, 336 + 70), false, false); // over the second tile
        }));
        Shots.Add(("catalogue-build", build, () => build.Show()));
        // the pie menu: the top level (a stand-in for the Sim's head), and a category of eleven
        var pieTop = new VRPieMenuPanel { Source = new SamplePie(false, CatalogCheck.HeadStandIn(GraphicsDevice)) };
        var pieSub = new VRPieMenuPanel { Source = new SamplePie(true, null) };
        Shots.Add(("pie-top", pieTop, () =>
        {
            pieTop.Show();
            pieTop.SetMouse(new Vector2(500 + 2 * 70, 550 - 2 * 42), false, false); // pointing at a choice
        }));
        Shots.Add(("pie-category", pieSub, () => pieSub.Show()));
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
            CatalogThumbs?.Render(); // (the lot world does this in the game)
            if (Shot >= Shots.Count)
            {
                RenderController();
                if (GltfCheck.Run(GraphicsDevice, OutDir) > 0) throw new Exception("glTF checks failed");
                if (MultiviewCheck.Run(GraphicsDevice, Content, OutDir) > 0) throw new Exception("multiview checks failed");
                if (StereoCheck.Run(GraphicsDevice, OutDir) > 0) throw new Exception("stereo rendering checks failed");
                if (CatalogCheck.Run(GraphicsDevice, OutDir) > 0) throw new Exception("catalogue picture checks failed");
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

// The glTF loader used for the runtime's controller models (Simitone.VR.Graphics.GltfModel): a small model made here
// (a textured body with a red "stick" node the runtime would animate) is parsed, drawn, posed and hidden; then, if
// UICHECK_GLB_DIR names a folder of real controller models (e.g. the webxr-input-profiles assets' "profiles" folder),
// a few are drawn, with the trigger pulled through the same node-pose mechanism the runtime uses.
static class GltfCheck
{
    static int Failures;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} glTF: {name}: {detail}");
        if (!ok) Failures++;
    }

    // ---- a GLB built by hand: node 0 "body" (a white textured cube), child node 1 "stick" (a small red cube above it)
    static byte[] MakeGlb(byte[] png)
    {
        var positions = new List<float>(); var normals = new List<float>(); var uvs = new List<float>(); var indices = new List<ushort>();
        var faces = new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ };
        foreach (var n in faces)
        {
            var u = Math.Abs(n.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
            var v = Vector3.Cross(n, u);
            var b = (ushort)(positions.Count / 3);
            foreach (var (du, dv) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                var p = (n + u * du + v * dv) * 0.05f; // a 10 cm cube
                positions.AddRange(new[] { p.X, p.Y, p.Z }); normals.AddRange(new[] { n.X, n.Y, n.Z });
                uvs.AddRange(new[] { (du + 1) / 2f, (dv + 1) / 2f });
            }
            indices.AddRange(new[] { b, (ushort)(b + 1), (ushort)(b + 2), b, (ushort)(b + 2), (ushort)(b + 3) });
        }
        var bin = new MemoryStream();
        int Add(byte[] bytes) { var at = (int)bin.Length; bin.Write(bytes, 0, bytes.Length); while (bin.Length % 4 != 0) bin.WriteByte(0); return at; }
        byte[] F(List<float> f) { var b = new byte[f.Count * 4]; Buffer.BlockCopy(f.ToArray(), 0, b, 0, b.Length); return b; }
        byte[] U(List<ushort> f) { var b = new byte[f.Count * 2]; Buffer.BlockCopy(f.ToArray(), 0, b, 0, b.Length); return b; }
        int pAt = Add(F(positions)), nAt = Add(F(normals)), tAt = Add(F(uvs)), iAt = Add(U(indices)), imgAt = Add(png);
        var count = positions.Count / 3;
        var json = $@"{{""asset"":{{""version"":""2.0""}},""scene"":0,""scenes"":[{{""nodes"":[0]}}],
""nodes"":[{{""name"":""body"",""mesh"":0,""children"":[1]}},{{""name"":""stick"",""mesh"":1,""translation"":[0,0.08,0],""scale"":[0.3,0.3,0.3]}}],
""meshes"":[{{""primitives"":[{{""attributes"":{{""POSITION"":0,""NORMAL"":1,""TEXCOORD_0"":2}},""indices"":3,""material"":0}}]}},
            {{""primitives"":[{{""attributes"":{{""POSITION"":0,""NORMAL"":1}},""indices"":3,""material"":1}}]}}],
""materials"":[{{""pbrMetallicRoughness"":{{""baseColorTexture"":{{""index"":0}}}}}},{{""pbrMetallicRoughness"":{{""baseColorFactor"":[1,0,0,1]}},""emissiveFactor"":[0.6,0,0]}}],
""textures"":[{{""source"":0}}],""images"":[{{""bufferView"":4,""mimeType"":""image/png""}}],
""accessors"":[{{""bufferView"":0,""componentType"":5126,""count"":{count},""type"":""VEC3"",""min"":[-0.05,-0.05,-0.05],""max"":[0.05,0.05,0.05]}},
               {{""bufferView"":1,""componentType"":5126,""count"":{count},""type"":""VEC3""}},
               {{""bufferView"":2,""componentType"":5126,""count"":{count},""type"":""VEC2""}},
               {{""bufferView"":3,""componentType"":5123,""count"":{indices.Count},""type"":""SCALAR""}}],
""bufferViews"":[{{""buffer"":0,""byteOffset"":{pAt},""byteLength"":{positions.Count * 4}}},{{""buffer"":0,""byteOffset"":{nAt},""byteLength"":{normals.Count * 4}}},
                 {{""buffer"":0,""byteOffset"":{tAt},""byteLength"":{uvs.Count * 4}}},{{""buffer"":0,""byteOffset"":{iAt},""byteLength"":{indices.Count * 2}}},
                 {{""buffer"":0,""byteOffset"":{imgAt},""byteLength"":{png.Length}}}],
""buffers"":[{{""byteLength"":{bin.Length}}}]}}";
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        var jsonPadded = new byte[(jsonBytes.Length + 3) & ~3];
        for (int i = 0; i < jsonPadded.Length; i++) jsonPadded[i] = i < jsonBytes.Length ? jsonBytes[i] : (byte)' ';
        var binBytes = bin.ToArray();
        var glb = new MemoryStream();
        var w = new BinaryWriter(glb);
        w.Write(0x46546C67u); w.Write(2u); w.Write((uint)(12 + 8 + jsonPadded.Length + 8 + binBytes.Length));
        w.Write((uint)jsonPadded.Length); w.Write(0x4E4F534Au); w.Write(jsonPadded);
        w.Write((uint)binBytes.Length); w.Write(0x004E4942u); w.Write(binBytes);
        return glb.ToArray();
    }

    static byte[] TinyPng()
    {
        // a 2x2 blue and white checker, as PNG
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAE0lEQVR4nGPQCLjz//9/BiAGsgA7FAijjt680AAAAABJRU5ErkJggg==");
    }

    /// <summary>The parsing part, with no graphics device (runs with --arms-only too).</summary>
    public static void ParseOnly()
    {
        var asset = Simitone.VR.Graphics.GltfAsset.Parse(MakeGlb(TinyPng()));
        Check("parse: nodes and names", asset.Nodes.Length == 2 && asset.Nodes[0].Name == "body" && asset.Nodes[1].Name == "stick",
            $"{asset.Nodes.Length} nodes");
        Check("parse: scene root and child", asset.Roots.Length == 1 && asset.Roots[0] == 0 && asset.Nodes[0].Children.Length == 1, "body > stick");
        Check("parse: meshes (24 vertices, 12 triangles each)", asset.Meshes.Length == 2 && asset.Meshes[0][0].Vertices.Length == 24
            && asset.Meshes[0][0].Indices.Length == 36, "two cubes");
        Check("parse: materials", asset.Materials.Length == 2 && asset.Materials[0].Image == 0 && asset.Materials[1].BaseColor == new Vector4(1, 0, 0, 1),
            "textured, and red");
        Check("parse: embedded image", asset.Images.Length == 1 && asset.Images[0] != null && asset.Images[0][1] == (byte)'P', "PNG bytes");
        var stick = asset.Nodes[1].Local;
        Check("parse: node transform (scale then translate)", Math.Abs(stick.M11 - 0.3f) < 1e-6f && Math.Abs(stick.M42 - 0.08f) < 1e-6f,
            $"scale {stick.M11}, y {stick.M42}");
        try
        {
            Simitone.VR.Graphics.GltfAsset.Parse(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 });
            Check("parse: rejects a non-glTF file", false, "no error");
        }
        catch (InvalidDataException) { Check("parse: rejects a non-glTF file", true, "InvalidDataException"); }
    }

    static (int count, Vector2 centre) RedPixels(Color[] pixels, int width)
    {
        int count = 0; var sum = Vector2.Zero;
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            if (c.R > 150 && c.G < 80 && c.B < 80) { count++; sum += new Vector2(i % width, i / width); }
        }
        return (count, count > 0 ? sum / count : Vector2.Zero);
    }

    public static int Run(GraphicsDevice gd, string outDir)
    {
        // ---- the made-up model: drawn, then with the stick moved, then with it hidden (as the runtime would)
        var asset = Simitone.VR.Graphics.GltfAsset.Parse(MakeGlb(TinyPng()));
        using (var model = new Simitone.VR.Graphics.GltfModel(gd, asset))
        using (var target = new RenderTarget2D(gd, 256, 256, false, SurfaceFormat.Color, DepthFormat.Depth24))
        {
            model.SetAnimatedNodes(new[] { "stick" });
            var view = Matrix.CreateLookAt(new Vector3(0, 0.03f, 0.4f), new Vector3(0, 0.03f, 0), Vector3.Up);
            var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(40), 1, 0.01f, 10);
            Color[] Render(Matrix[] poses, bool[] visible)
            {
                gd.SetRenderTarget(target);
                gd.Clear(Color.Black);
                model.Draw(gd, Matrix.Identity, view, projection, poses, visible);
                gd.SetRenderTarget(null);
                var data = new Color[256 * 256];
                target.GetData(data);
                return data;
            }
            var rest = RedPixels(Render(null, null), 256);
            var moved = RedPixels(Render(new[] { Matrix.CreateScale(0.3f) * Matrix.CreateTranslation(0.08f, 0.08f, 0) }, new[] { true }), 256);
            var hidden = RedPixels(Render(new[] { Matrix.Identity }, new[] { false }), 256);
            Check("draw: the model draws (red stick visible)", rest.count > 50, $"{rest.count} red pixels");
            Check("draw: a node pose replaces the node's own transform", moved.count > 50 && moved.centre.X - rest.centre.X > 20,
                $"stick moved right by {moved.centre.X - rest.centre.X:0} px");
            Check("draw: a hidden node isn't drawn", hidden.count == 0, $"{hidden.count} red pixels");
            using (var file = File.Create(Path.Combine(outDir, "gltf-test.png"))) { Render(null, null); target.SaveAsPng(file, 256, 256); }
        }

        // ---- real controller models, if a folder of them is given
        var dir = Environment.GetEnvironmentVariable("UICHECK_GLB_DIR");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            Console.WriteLine("     glTF: real controller models skipped (set UICHECK_GLB_DIR to a folder of them)");
            return Failures;
        }
        var files = new[] { "valve-index/right.glb", "oculus-touch-v3/left.glb", "meta-quest-touch-plus/right.glb", "htc-vive/none.glb" };
        const int cell = 300;
        using (var target = new RenderTarget2D(gd, cell * files.Length, cell * 2, false, SurfaceFormat.Color, DepthFormat.Depth24, 0,
            RenderTargetUsage.PreserveContents)) // (read back between cells)
        {
            gd.SetRenderTarget(target);
            gd.Clear(new Color(150, 170, 190));
            for (int i = 0; i < files.Length; i++)
            {
                var path = Path.Combine(dir, files[i]);
                if (!File.Exists(path)) { Check("real model " + files[i], false, "missing"); continue; }
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var real = Simitone.VR.Graphics.GltfAsset.Parse(File.ReadAllBytes(path));
                var parseMs = watch.ElapsedMilliseconds;
                using (var model = new Simitone.VR.Graphics.GltfModel(gd, real))
                {
                    var b = model.Bounds;
                    Check("real model " + files[i], b.Radius > 0.03f && b.Radius < 0.3f,
                        $"{real.Nodes.Length} nodes, {b.Radius * 200:0} cm across, parsed in {parseMs} ms");
                    // the trigger pulled, the way the runtime animates a part: its node's pose replaced
                    int Find(string name) => Array.FindIndex(real.Nodes, n => n.Name == name);
                    var valueNode = Find("xr_standard_trigger_pressed_value");
                    var maxNode = Find("xr_standard_trigger_pressed_max");
                    var rows = new Color[2][];
                    for (int row = 0; row < 2; row++)
                    {
                        gd.Viewport = new Viewport(i * cell, row * cell, cell, cell);
                        // from the side and a little below, where the trigger is
                        var eye = b.Center + new Vector3(b.Radius * 2.9f, -b.Radius * 0.5f, b.Radius * 0.6f);
                        var view = Matrix.CreateLookAt(eye, b.Center, Vector3.Up);
                        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(40), 1, 0.005f, 10);
                        Matrix[] poses = null;
                        if (row == 1 && valueNode >= 0 && maxNode >= 0)
                        {
                            model.SetAnimatedNodes(new[] { real.Nodes[valueNode].Name });
                            poses = new[] { real.Nodes[maxNode].Local };
                        }
                        else model.SetAnimatedNodes(null);
                        model.Draw(gd, Matrix.Identity, view, projection, poses, null);
                        gd.SetRenderTarget(null);
                        rows[row] = new Color[cell * cell];
                        target.GetData(0, new Rectangle(i * cell, row * cell, cell, cell), rows[row], 0, cell * cell);
                        gd.SetRenderTarget(target);
                    }
                    int changed = 0;
                    for (int k = 0; k < cell * cell; k++) if (rows[0][k] != rows[1][k]) changed++;
                    if (valueNode >= 0 && maxNode >= 0)
                        Check("real model " + files[i] + ": the trigger moves when its node is posed", changed > 30, $"{changed} pixels changed");
                }
            }
            gd.SetRenderTarget(null);
            using (var file = File.Create(Path.Combine(outDir, "controllers-real.png"))) target.SaveAsPng(file, target.Width, target.Height);
        }
        return Failures;
    }
}

// Single-pass stereo (Simitone.VR.Graphics.GLMultiview): every vertex shader MonoGame has (FreeSO's effects, MonoGame's
// own) gets a multiview variant, and a scene drawn once through them lands in both layers of a stereo target, each
// with its own view; normal drawing works as before afterwards.
static class MultiviewCheck
{
    static int Failures;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} multiview: {name}: {detail}");
        if (!ok) Failures++;
    }

    public static int Run(GraphicsDevice gd, Microsoft.Xna.Framework.Content.ContentManager content, string outDir)
    {
        var mv = Simitone.VR.Graphics.GLMultiview.TryCreate(gd, s => Console.WriteLine("     " + s));
        if (mv == null)
        {
            Check("available", false, Simitone.VR.Graphics.GLMultiview.Unavailable);
            return Failures;
        }
        // all of FreeSO's world effects (as WorldContent loads them), so their shaders are on the device
        int effects = 0;
        foreach (var file in Directory.GetFiles(Path.Combine(content.RootDirectory, "Effects"), "*.xnb"))
        {
            try { content.Load<Effect>("Effects/" + Path.GetFileNameWithoutExtension(file)); effects++; } catch { }
        }
        using var basic = new BasicEffect(gd) { VertexColorEnabled = true };
        using var batch = new SpriteBatch(gd);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var ready = mv.PrepareShaders(out var made, out var problem);
        Check("every vertex shader has a multiview variant", ready && made >= 45, $"{made} made from {effects} effects files and MonoGame's own, in {watch.ElapsedMilliseconds} ms{(problem != null ? "; " + problem : "")}");
        if (!ready) return Failures;

        const int size = 256;
        var target = mv.CreateTarget(size, size);
        var left = new RenderTarget2D(gd, size, size, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
        var right = new RenderTarget2D(gd, size, size, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
        var normal = new RenderTarget2D(gd, size, size, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
        var quad = new[]
        {
            new VertexPositionColor(new Vector3(-0.25f, -0.25f, 0.5f), Color.Red), new VertexPositionColor(new Vector3(0.25f, -0.25f, 0.5f), Color.Red),
            new VertexPositionColor(new Vector3(-0.25f, 0.25f, 0.5f), Color.Red), new VertexPositionColor(new Vector3(0.25f, 0.25f, 0.5f), Color.Red)
        };
        void Scene()
        {
            gd.Clear(Color.Black);
            basic.World = basic.View = basic.Projection = Matrix.Identity;
            gd.DepthStencilState = DepthStencilState.Default;
            gd.RasterizerState = RasterizerState.CullNone;
            foreach (var pass in basic.CurrentTechnique.Passes) { pass.Apply(); gd.DrawUserPrimitives(PrimitiveType.TriangleStrip, quad, 0, 2); }
            // and a SpriteBatch quad (MonoGame's sprite shader), green, top left
            batch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, null, DepthStencilState.None, RasterizerState.CullNone);
            batch.Draw(Pixel(gd), new Rectangle(10, 10, 30, 30), Color.Lime);
            batch.End();
        }
        (int count, Vector2 centre) Find(RenderTarget2D rt, Func<Color, bool> match)
        {
            var data = new Color[size * size];
            rt.GetData(data);
            int n = 0; var sum = Vector2.Zero;
            for (int i = 0; i < data.Length; i++) if (match(data[i])) { n++; sum += new Vector2(i % size, i / size); }
            return (n, n > 0 ? sum / n : Vector2.Zero);
        }
        bool Red(Color c) => c.R > 200 && c.G < 50;
        bool Green(Color c) => c.G > 200 && c.R < 50;

        // the right eye sees everything 0.5 to the right in clip space (a quarter of the image)
        var shift = Matrix.Identity;
        shift.M41 = 0.5f;
        gd.SetRenderTarget(normal);
        mv.Begin(target, Matrix.Identity, shift);
        Scene();
        mv.End();
        mv.CopyLayer(target, 0, left);
        mv.CopyLayer(target, 1, right);
        var errors = mv.LastError();
        var l = Find(left, Red);
        var r = Find(right, Red);
        Check("one draw fills the left layer", l.count > 3000 && Math.Abs(l.centre.X - 128) < 2, $"{l.count} px at {l.centre}");
        Check("and the right layer, with its own view", r.count > 3000 && Math.Abs(r.centre.X - 128 - 64) < 2 && Math.Abs(r.centre.Y - l.centre.Y) < 1,
            $"{r.count} px at {r.centre} (64 px to the right)");
        var sl = Find(left, Green);
        var sr = Find(right, Green);
        Check("MonoGame's own shaders (SpriteBatch) too", sl.count > 500 && sr.count > 500 && sr.centre.X - sl.centre.X > 60,
            $"left {sl.count} px at {sl.centre}, right {sr.count} px at {sr.centre}");
        Check("no GL errors", errors == 0, $"0x{errors:x}");

        // depth came along: draw a nearer blue quad over the left eye image with depth testing against the copy
        gd.SetRenderTarget(left);
        // (identity projection: depth is z, smaller is nearer; the red quad is at 0.5)
        var nearQuad = Array.ConvertAll(quad, v => new VertexPositionColor(v.Position + new Vector3(0.1f, 0, -0.3f), Color.Blue));
        var farQuad = Array.ConvertAll(quad, v => new VertexPositionColor(v.Position + new Vector3(-0.1f, 0, 0.3f), Color.Yellow));
        gd.DepthStencilState = DepthStencilState.Default;
        foreach (var pass in basic.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawUserPrimitives(PrimitiveType.TriangleStrip, nearQuad, 0, 2);
            gd.DrawUserPrimitives(PrimitiveType.TriangleStrip, farQuad, 0, 2);
        }
        gd.SetRenderTarget(null);
        var blue = Find(left, c => c.B > 200 && c.R < 50 && c.G < 50);
        var yellowOverRed = Find(left, c => c.R > 200 && c.G > 200 && c.B < 50);
        Check("the depth buffer is copied too (nearer drawn over, farther hidden behind the scene)",
            blue.count == 4096 && yellowOverRed.count > 500 && yellowOverRed.count < 1200,
            $"{blue.count} blue px in front (all of it); {yellowOverRed.count} yellow px, only where the red quad isn't");

        // and normal drawing still works after the pass
        gd.SetRenderTarget(normal);
        Scene();
        gd.SetRenderTarget(null);
        var n = Find(normal, Red);
        Check("normal drawing afterwards", n.count > 3000 && Math.Abs(n.centre.X - 128) < 2, $"{n.count} px at {n.centre}");

        using (var f = File.Create(Path.Combine(outDir, "multiview-left.png"))) left.SaveAsPng(f, size, size);
        using (var f = File.Create(Path.Combine(outDir, "multiview-right.png"))) right.SaveAsPng(f, size, size);
        mv.DestroyTarget(target);
        mv.Dispose();
        left.Dispose(); right.Dispose(); normal.Dispose();
        return Failures;
    }

    static Texture2D PixelTexture;
    static Texture2D Pixel(GraphicsDevice gd)
    {
        if (PixelTexture == null) { PixelTexture = new Texture2D(gd, 1, 1); PixelTexture.SetData(new[] { Color.White }); }
        return PixelTexture;
    }
}

// The VR renderer (VRStereoRenderer) on a made-up scene of boxes from 0.4 m to 8 m away, with a headset-like
// asymmetric field of view and a 64 mm IPD: single-pass stereo (one draw for both eyes) must match drawing each
// eye separately, and foveated rendering must match full resolution where you look and stay close elsewhere.
static class StereoCheck
{
    static int Failures;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} stereo: {name}: {detail}");
        if (!ok) Failures++;
    }

    public static int Run(GraphicsDevice gd, string outDir)
    {
        FSO.Common.Utils.PPXDepthEngine.InitGD(gd);
        const float w = 3f; // world units per metre
        var cam = new VREyeCamera(gd) { NearPlane = 0.05f * w, FarPlane = 1000 };
        var head = new Vector3(20, 4.8f, 20);
        var headRot = Quaternion.CreateFromYawPitchRoll(0.3f, -0.15f, 0);
        for (int i = 0; i < 2; i++)
        {
            var side = i == 0 ? -1 : 1;
            var f = new Simitone.VR.VRMath.ViewFrustum
            {
                Position = head + Vector3.Transform(new Vector3(side * 0.032f * w, 0, 0), headRot),
                Orientation = headRot,
                Left = (float)Math.Tan(MathHelper.ToRadians(side < 0 ? -52 : -44)), Right = (float)Math.Tan(MathHelper.ToRadians(side < 0 ? 44 : 52)),
                Down = (float)Math.Tan(MathHelper.ToRadians(-50)), Up = (float)Math.Tan(MathHelper.ToRadians(46))
            };
            cam.EyeFrustum[i] = f;
            cam.EyeView[i] = f.View;
            cam.EyeProjection[i] = f.Projection(cam.NearPlane, cam.FarPlane);
        }
        cam.EyeFrustaValid = true;

        // the scene: boxes in front, at several distances, some near the edges of view
        var verts = new List<VertexPositionColor>();
        var rnd = new Random(3);
        var forward = Vector3.Transform(-Vector3.UnitZ, headRot);
        var rightDir = Vector3.Transform(Vector3.UnitX, headRot);
        var up = Vector3.Transform(Vector3.UnitY, headRot);
        Color[] palette = { Color.Red, Color.Lime, Color.Blue, Color.Yellow, Color.Magenta, Color.Cyan, Color.Orange, Color.White };
        for (int b = 0; b < 40; b++)
        {
            var dist = (0.4f + (float)Math.Pow(rnd.NextDouble(), 1.5) * 7.6f) * w;
            var centre = head + forward * dist + rightDir * (float)(rnd.NextDouble() * 2 - 1) * dist * 0.9f + up * (float)(rnd.NextDouble() * 2 - 1) * dist * 0.8f;
            var size = (0.05f + (float)rnd.NextDouble() * 0.3f) * w;
            AddBox(verts, centre, size, palette[b % palette.Length], (float)rnd.NextDouble() * 6);
        }
        var scene = verts.ToArray();
        using var effect = new BasicEffect(gd) { VertexColorEnabled = true };
        int draws = 0;
        void DrawWorld()
        {
            draws++;
            gd.Clear(new Color(40, 50, 70));
            effect.World = Matrix.Identity;
            effect.View = cam.View;
            effect.Projection = cam.Projection;
            gd.DepthStencilState = DepthStencilState.Default;
            gd.RasterizerState = RasterizerState.CullNone;
            gd.BlendState = BlendState.Opaque;
            foreach (var pass in effect.CurrentTechnique.Passes) { pass.Apply(); gd.DrawUserPrimitives(PrimitiveType.TriangleList, scene, 0, scene.Length / 3); }
        }

        const int W = 480, H = 480;
        RenderTarget2D[] Eyes() => new[]
        {
            new RenderTarget2D(gd, W, H, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents),
            new RenderTarget2D(gd, W, H, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents)
        };
        var settings = SimitoneVR.Settings;
        using var renderer = new VRStereoRenderer(gd);
        Color[][] Render(bool singlePass, VRFoveation foveation, Vector3? gaze, out string mode, out int passes, string file)
        {
            settings.SinglePassStereo = singlePass;
            settings.Foveation = foveation;
            var eyes = Eyes();
            draws = 0;
            for (int frame = 0; frame < 3; frame++) renderer.Render(cam, eyes, new Color(40, 50, 70), gaze, DrawWorld); // (gaze smoothing settles)
            passes = draws / 3;
            mode = renderer.Mode;
            gd.SetRenderTarget(null);
            var result = new Color[2][];
            for (int i = 0; i < 2; i++) { result[i] = new Color[W * H]; eyes[i].GetData(result[i]); }
            using (var f = File.Create(Path.Combine(outDir, file))) { SideBySide(gd, eyes).SaveAsPng(f, W * 2, H); }
            foreach (var e in eyes) e.Dispose();
            return result;
        }
        (float differing, float meanError) Compare(Color[] a, Color[] b, Rectangle? only = null)
        {
            int diff = 0, n = 0; double sum = 0;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (only != null && !only.Value.Contains(x, y)) continue;
                    var p = a[y * W + x]; var q = b[y * W + x];
                    var d = Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B);
                    if (d > 30) diff++;
                    sum += d / 3.0; n++;
                }
            return (diff / (float)n, (float)(sum / n));
        }

        var reference = Render(false, VRFoveation.Off, null, out var refMode, out var refPasses, "stereo-two-pass.png");
        Check("two passes draw the lot twice", refPasses == 2 && refMode == "two passes", $"{refPasses} draws a frame ({refMode})");
        var single = Render(true, VRFoveation.Off, null, out var singleMode, out var singlePasses, "stereo-single-pass.png");
        Check("single pass draws the lot once", singlePasses == 1 && singleMode == "single pass", $"{singlePasses} draw a frame ({singleMode})");
        for (int i = 0; i < 2; i++)
        {
            var (differing, mean) = Compare(reference[i], single[i]);
            Check($"single pass looks the same as two passes ({(i == 0 ? "left" : "right")} eye)", differing < 0.003f && mean < 1f,
                $"{differing * 100:0.000}% of pixels differ, mean error {mean:0.00}/255");
        }
        var leftRightDiffer = Compare(single[0], single[1]).differing;
        Check("the eyes differ (it is stereo)", leftRightDiffer > 0.02f, $"{leftRightDiffer * 100:0.0}% of pixels differ between eyes");

        // foveated: looking a little left of centre and down, at a point 2 m away
        var gazePoint = head + Vector3.Transform(Vector3.Normalize(new Vector3(-0.25f, -0.15f, -1)), headRot) * 2 * w;
        var foveated = Render(true, VRFoveation.Strong, gazePoint, out var fovMode, out var fovPasses, "stereo-foveated.png");
        Check("foveated single pass draws the lot twice (soft view and sharp centre)", fovPasses == 2 && fovMode.Contains("following your eyes"),
            $"{fovPasses} draws a frame ({fovMode})");
        for (int i = 0; i < 2; i++)
        {
            var eye = cam.EyeFrustum[i];
            var t = Simitone.VR.VRMath.Tangents(eye, gazePoint - eye.Position).Value;
            var rect = Simitone.VR.VRMath.InsetPixels(eye, t, new Point(1, 1), W, H).rect; // (its centre)
            rect = new Rectangle(rect.X - 70, rect.Y - 70, 140, 140);
            var core = new Rectangle(rect.X + rect.Width / 4, rect.Y + rect.Height / 4, rect.Width / 2, rect.Height / 2);
            var inside = Compare(reference[i], foveated[i], core);
            var whole = Compare(reference[i], foveated[i]);
            Check($"foveated: full resolution where you look ({(i == 0 ? "left" : "right")} eye)", inside.differing < 0.003f && inside.meanError < 1f,
                $"{inside.differing * 100:0.000}% differ in the middle of the sharp square {rect}");
            Check($"foveated: close to full resolution elsewhere ({(i == 0 ? "left" : "right")} eye)", whole.meanError < 6f,
                $"mean error {whole.meanError:0.00}/255 over the whole eye, {whole.differing * 100:0.0}% of pixels differ visibly (soft edges)");
        }
        // two passes + foveation: four draws
        Render(false, VRFoveation.Light, null, out var fov2Mode, out var fov2Passes, "stereo-foveated-two-pass.png");
        Check("foveated without single pass draws four times (centre, no eye tracking)", fov2Passes == 4 && fov2Mode.Contains("centre"),
            $"{fov2Passes} draws a frame ({fov2Mode})");
        return Failures;
    }

    static void AddBox(List<VertexPositionColor> v, Vector3 c, float s, Color colour, float spin)
    {
        var rot = Matrix.CreateRotationY(spin) * Matrix.CreateRotationX(spin * 0.7f);
        Vector3 P(int x, int y, int z) => c + Vector3.Transform(new Vector3(x, y, z) * s / 2, rot);
        var faces = new[] { (0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3) };
        Vector3 Corner(int i) => P((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1);
        for (int f = 0; f < faces.Length; f++)
        {
            var (a, b, cc, d) = faces[f];
            var shade = new Color(colour.ToVector3() * (0.55f + 0.08f * f));
            v.Add(new VertexPositionColor(Corner(a), shade)); v.Add(new VertexPositionColor(Corner(b), shade)); v.Add(new VertexPositionColor(Corner(cc), shade));
            v.Add(new VertexPositionColor(Corner(a), shade)); v.Add(new VertexPositionColor(Corner(cc), shade)); v.Add(new VertexPositionColor(Corner(d), shade));
        }
    }

    static RenderTarget2D SideBySide(GraphicsDevice gd, RenderTarget2D[] eyes)
    {
        var both = new RenderTarget2D(gd, eyes[0].Width * 2, eyes[0].Height);
        using var batch = new SpriteBatch(gd);
        gd.SetRenderTarget(both);
        batch.Begin(SpriteSortMode.Immediate, BlendState.Opaque);
        batch.Draw(eyes[0], Vector2.Zero, Color.White);
        batch.Draw(eyes[1], new Vector2(eyes[0].Width, 0), Color.White);
        batch.End();
        gd.SetRenderTarget(null);
        return both;
    }
}
