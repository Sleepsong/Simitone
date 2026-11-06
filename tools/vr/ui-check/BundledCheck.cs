using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.VR;
using Simitone.VR.Graphics;

// The controller models shipped with Simitone (VRControllers/, for runtimes with none of their own): which model each
// headset gets, that every one loads and draws, and that their parts move with the controls as profile.json says.
// Image: controllers-bundled.png, a row per model: left hand at rest and in use (trigger, grip, stick and buttons all
// pressed, stick pushed forward and right), then the right hand.
static class BundledCheck
{
    static int Failures;
    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + "bundled controllers: " + name + (detail != null ? " (" + detail + ")" : ""));
        if (!ok) Failures++;
    }

    public static int Run(GraphicsDevice gd, string outDir)
    {
        const string touch = "/interaction_profiles/oculus/touch_controller";
        var picks = new (string profile, string system, string want)[]
        {
            (touch, "Meta Quest 3", "meta-quest-touch-plus"), (touch, "Meta Quest 3S", "meta-quest-touch-plus"),
            (touch, "Meta Quest Pro", "meta-quest-touch-pro"), (touch, "Oculus Quest2", "oculus-touch-v3"),
            (touch, "Oculus Rift S", "oculus-touch-v2"), (touch, "Oculus Quest", "oculus-touch-v2"), (touch, "Some Headset", "oculus-touch-v3"),
            ("/interaction_profiles/htc/vive_controller", "Monado: Vive", "htc-vive"),
            ("/interaction_profiles/valve/index_controller", "Valve Index", ""), ("/interaction_profiles/khr/simple_controller", "", "")
        };
        var wrong = 0;
        foreach (var (profile, system, want) in picks) if (VRBundledControllers.ProfileFolder(profile, system) != want) wrong++;
        Check("each headset gets its controllers' model (none for Index, which SteamVR provides)", wrong == 0, $"{picks.Length - wrong} of {picks.Length}");

        var bundled = new VRBundledControllers(Path.Combine(AppContext.BaseDirectory, "VRControllers"), s => Console.WriteLine("     " + s));
        var models = new[] { ("meta-quest-touch-plus", "Meta Quest 3"), ("meta-quest-touch-pro", "Meta Quest Pro"),
            ("oculus-touch-v3", "Oculus Quest2"), ("oculus-touch-v2", "Oculus Rift S"), ("htc-vive", "Vive") };
        const int cell = 220;
        using var target = new RenderTarget2D(gd, cell * 4, cell * models.Length, false, SurfaceFormat.Color, DepthFormat.Depth24);
        gd.SetRenderTarget(target);
        gd.Viewport = new Viewport(0, 0, target.Width, target.Height);
        gd.Clear(new Color(150, 170, 190));
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(30), 1, 0.01f, 10);
        // from above and behind, a little to the outside of each hand: as you see a controller you hold
        Matrix View(int hand) => Matrix.CreateLookAt(new Vector3(hand == 0 ? -0.08f : 0.08f, 0.2f, 0.26f), new Vector3(0, -0.01f, -0.03f), Vector3.Up);
        var used = new VRBundledControllers.HandState
            { Trigger = 1, Squeeze = 1, Stick = new Vector2(1, 1), StickClick = true, Lower = true, Upper = true, Menu = true };
        var drawn = new List<(string key, int parts, int row, int hand)>();
        for (int row = 0; row < models.Length; row++)
        {
            var (name, system) = models[row];
            var profile = name == "htc-vive" ? "/interaction_profiles/htc/vive_controller" : touch;
            for (int hand = 0; hand < 2; hand++)
            {
                var layout = bundled.For(hand, profile, system);
                if (layout == null) continue;
                var asset = GltfAsset.Parse(layout.Glb);
                VRBundledControllers.Prepare(layout, asset);
                using var model = new GltfModel(gd, asset);
                for (int inUse = 0; inUse < 2; inUse++)
                {
                    VRBundledControllers.Animate(layout, inUse == 1 ? used : default);
                    gd.Viewport = new Viewport((hand * 2 + inUse) * cell, row * cell, cell, cell);
                    gd.Clear(ClearOptions.DepthBuffer, Color.Black, 1, 0);
                    model.SetAnimatedNodes(layout.Device.AnimatedNodes);
                    model.Draw(gd, Matrix.Identity, View(hand), projection, layout.Device.NodePoses, layout.Device.NodeVisible);
                }
                drawn.Add((layout.Key, layout.Device.AnimatedNodes.Length, row, hand));
            }
        }
        gd.SetRenderTarget(null);
        var pixels = new Color[target.Width * target.Height];
        target.GetData(pixels);
        using (var file = File.Create(Path.Combine(outDir, "controllers-bundled.png"))) target.SaveAsPng(file, target.Width, target.Height);

        int moving = 0, visible = 0;
        foreach (var (key, parts, row, hand) in drawn)
        {
            int changed = 0, model = 0;
            for (int y = 0; y < cell; y++)
            {
                for (int x = 0; x < cell; x++)
                {
                    var rest = pixels[(row * cell + y) * target.Width + hand * 2 * cell + x];
                    var inUse = pixels[(row * cell + y) * target.Width + (hand * 2 + 1) * cell + x];
                    if (rest != new Color(150, 170, 190)) model++;
                    if (rest != inUse) changed++;
                }
            }
            if (model > cell * cell / 20) visible++;
            if (changed > 30 && parts >= 3) moving++;
            Console.WriteLine($"     {key}: {parts} moving parts, {model} pixels of model, {changed} change when used");
        }
        // (the Vive wand is one model, for either hand)
        Check("every model loads and draws, the size of a controller", drawn.Count == 10 && visible == drawn.Count, $"{visible} of {drawn.Count} (10 expected)");
        Check("their triggers, grips, sticks and buttons move", moving == drawn.Count, $"{moving} of {drawn.Count}");
        return Failures;
    }
}
