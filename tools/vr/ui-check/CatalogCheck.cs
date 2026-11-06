using System;
using System.Collections.Generic;
using System.IO;
using FSO.Files.RC;
using FSO.LotView;
using FSO.LotView.Effects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Simitone.Client.VR;

// The catalogue tablet and the VR pie menu: sample sources for their screenshots, and a check of the tablet's 3D pictures
// (VRCatalogThumbnails) drawing made-up object meshes through FreeSO's real object shader (RCObject), as it draws the
// lot's objects in 3D mode.

/// <summary>A buy catalogue (or build, with <see cref="Build"/>) with sample categories and items.</summary>
class SampleCatalog : IVRCatalogSource
{
    public bool Build;
    public int Chosen = 1, Selected = 0;
    private readonly List<Texture2D> CategoryList = new List<Texture2D>();
    private readonly List<VRCatalogSubcategory> SubList = new List<VRCatalogSubcategory>();
    private readonly List<VRCatalogEntry> ItemList = new List<VRCatalogEntry>();

    public SampleCatalog(bool build, Func<Color, Texture2D> dot)
    {
        Build = build;
        if (!build)
        {
            foreach (var c in new[] { "seat", "surf", "appl", "elec", "plum", "deco", "misc", "ligt" })
                CategoryList.Add(VRUI.CustomUI("cat_" + c + ".png"));
            var subs = new[] { ("Dining Chairs", "seat_dine", false), ("Lounge Chairs", "seat_loun", false), ("Sofas", "seat_sofa", false),
                ("Beds", "seat_beds", false), ("Other", "other", true), ("All", "all", false) };
            foreach (var (label, icon, disabled) in subs)
                SubList.Add(new VRCatalogSubcategory { Label = label, Icon = VRUI.CustomUI("cat_" + icon + ".png"), Disabled = disabled });
            var names = new[] { "Wooden Dining Chair", "Two-Seater Sofa", "Reading Armchair", "Bar Stool", "Garden Bench",
                "Office Chair", "Rocking Chair", "Leather Recliner", "Bean Bag", "Throne", "Folding Chair" };
            var prices = new[] { 80, 450, 260, 95, 150, 120, 310, 1250, 70, 25000, 35 };
            var colours = new[] { Color.SaddleBrown, Color.IndianRed, Color.OliveDrab, Color.SlateBlue, Color.Goldenrod };
            for (int i = 0; i < names.Length; i++)
                ItemList.Add(new VRCatalogEntry { Name = names[i], Price = prices[i], Icon = dot(colours[i % colours.Length]), GUID = i < 2 ? (uint)(i + 1) : 0,
                    Description = i == 1 ? "Room for two, and just about room for a cat as well. The cushions are stain resistant, mostly." : "" });
        }
        else
        {
            foreach (var c in new[] { "build_arch", "build_outs", "build_objs" })
                CategoryList.Add(VRUI.CustomUI("cat_" + c + ".png"));
            var subs = new[] { ("Walls", "build_wall"), ("Wallpaper", "build_walp"), ("Floors", "build_flor"), ("Doors", "build_door"),
                ("Windows", "build_wind"), ("Stairs", "build_stai"), ("Roofs", "build_roof") };
            foreach (var (label, icon) in subs)
                SubList.Add(new VRCatalogSubcategory { Label = label, Icon = VRUI.CustomUI("cat_" + icon + ".png") });
            Chosen = -1;
            Selected = -1;
        }
    }

    public VRCatalogMode Mode => Build ? VRCatalogMode.Build : VRCatalogMode.Buy;
    public int Budget => 20450;
    public IReadOnlyList<Texture2D> Categories => CategoryList;
    public int ActiveCategory => 0;
    public IReadOnlyList<VRCatalogSubcategory> Subcategories => SubList;
    public int ActiveSubcategory => Chosen;
    public IReadOnlyList<VRCatalogEntry> Items => Chosen >= 0 ? ItemList : (IReadOnlyList<VRCatalogEntry>)Array.Empty<VRCatalogEntry>();
    public int SelectedItem => Selected;
    public int Version => 1;
    public int Floor => 2;
    public int Floors => 5;
    public Simitone.VR.VRWallView Walls => Build ? Simitone.VR.VRWallView.Down : Simitone.VR.VRWallView.Cutaway;
    public void SetMode(VRCatalogMode mode) { }
    public VRHeldObject HeldObject;
    public VRHeldObject Held => HeldObject;
    public void SellHeld() { }
    public void PutBackHeld() { }
    public void ChangeFloor(int direction) { }
    public void SetWalls(Simitone.VR.VRWallView walls) { }
    public void ChooseCategory(int index) { }
    public void ChooseSubcategory(int index) { }
    public void ChooseItem(int index) { }
}

/// <summary>A pie menu: the top level, or a category of eleven choices (eight around, three stacked).</summary>
class SamplePie : IVRPieSource
{
    private readonly List<VRPieItem> ItemList = new List<VRPieItem>();
    private readonly string Level;
    private readonly Texture2D HeadPicture;

    public SamplePie(bool category, Texture2D head)
    {
        HeadPicture = head;
        if (!category)
        {
            Level = null;
            foreach (var (name, cat, hi) in new[] { ("Talk", false, false), ("Joke", false, false), ("Entertain...", true, false),
                ("Hug", false, true), ("Give Back Rub", false, false), ("Ask...", true, false) })
                ItemList.Add(new VRPieItem { Name = name, Category = cat, Highlight = hi });
        }
        else
        {
            Level = "Entertain...";
            foreach (var name in new[] { "Juggle", "Tell Story", "Dance", "Sing", "Play Charades", "Do Magic Trick", "Puppet Show",
                "Recite Poem", "Impersonate", "Whistle Tune", "Make Faces" })
                ItemList.Add(new VRPieItem { Name = name });
        }
    }

    public IReadOnlyList<VRPieItem> Items => ItemList;
    public string LevelName => Level;
    public Texture2D Head => HeadPicture;
    public int Version => 1;
    public void Choose(int index) { }
    public void Back() { }
    public void Cancel() { }
    public void Look(Vector2 at) { }
}

static class CatalogCheck
{
    static int Failures;
    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + "thumbnails: " + name + (detail != null ? " (" + detail + ")" : ""));
        if (!ok) Failures++;
    }

    /// <summary>A stand-in for the Sim's head the pie menu shows (the game renders the real one): a face in a 200 unit square.</summary>
    public static Texture2D HeadStandIn(GraphicsDevice gd)
    {
        const int n = 128;
        var px = new Color[n * n];
        for (int i = 0; i < px.Length; i++)
        {
            float x = i % n - 63.5f, y = i / n - 63.5f;
            var face = x * x / (24f * 24f) + y * y / (30f * 30f) < 1;
            var eye = ((x + 9) * (x + 9) + (y + 6) * (y + 6) < 9) || ((x - 9) * (x - 9) + (y + 6) * (y + 6) < 9);
            var hair = y < -14 && x * x / (26f * 26f) + (y + 2) * (y + 2) / (34f * 34f) < 1;
            px[i] = eye ? new Color(40, 30, 20) : hair ? new Color(70, 45, 25) : face ? new Color(230, 190, 150) : Color.Transparent;
        }
        var t = new Texture2D(gd, n, n);
        t.SetData(px);
        return t;
    }

    /// <summary>FreeSO's object shader, as the lot loads it (WorldContent), for the 3D pictures.</summary>
    public static void LoadEffect(Microsoft.Xna.Framework.Content.ContentManager content)
    {
        WorldContent.RCObject ??= new RCObjectEffect(content.Load<Effect>("Effects/RCObject"));
    }

    // made-up objects: 1 a chair, 2 a two-tile sofa, 3 a mesh still being made, anything else none
    public static DGRP3DMesh Chair, Sofa, Unfinished;
    private static Texture2D Palette;

    public static VRCatalogThumbnails.Part[] Meshes(uint guid) => guid switch
    {
        1 => new[] { Part(Chair, 0, 0) },
        2 => new[] { Part(Sofa, 0, 0), Part(Sofa, 1, 0) },
        3 => new[] { Part(Unfinished, 0, 0) },
        _ => null
    };

    private static VRCatalogThumbnails.Part Part(DGRP3DMesh mesh, int x, int y) => new VRCatalogThumbnails.Part
    {
        Mesh = mesh,
        World = Matrix.CreateScale(3f) * Matrix.CreateTranslation(1.5f + x * 3f, 0.1f, 1.5f + y * 3f)
    };

    /// <summary>
    /// The texture the made-up meshes use: four colours in its left 6 of 8 columns, and magenta in the 2 columns of
    /// padding (as the game's textures are padded to a multiple of 4: TextureInfo.UVScale maps UVs to the used part).
    /// Magenta in a picture means the padding was sampled.
    /// </summary>
    public static void MakeMeshes(GraphicsDevice gd)
    {
        var px = new Color[8 * 8];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                px[y * 8 + x] = x >= 6 ? Color.Magenta
                    : (x < 3 ? (y < 4 ? new Color(150, 90, 50) : new Color(200, 140, 80)) : (y < 4 ? new Color(60, 110, 160) : new Color(230, 220, 200)));
        Palette = new Texture2D(gd, 8, 8);
        Palette.SetData(px);
        Palette.Tag = new FSO.Common.Rendering.TextureInfo(Palette, 6, 8);

        // a chair: seat, back and four legs (mesh units: a tile is 1 wide, centred on the origin)
        var chair = new List<(Vector3 min, Vector3 max, int colour)>
        {
            (new Vector3(-0.3f, 0.35f, -0.3f), new Vector3(0.3f, 0.42f, 0.3f), 0),
            (new Vector3(-0.3f, 0.42f, 0.24f), new Vector3(0.3f, 1.0f, 0.3f), 1),
        };
        foreach (var (lx, lz) in new[] { (-0.28f, -0.28f), (0.24f, -0.28f), (-0.28f, 0.24f), (0.24f, 0.24f) })
            chair.Add((new Vector3(lx, 0, lz), new Vector3(lx + 0.04f, 0.35f, lz + 0.04f), 2));
        Chair = MakeMesh(gd, chair, true);
        // a sofa half (one per tile): base, back and arm
        Sofa = MakeMesh(gd, new List<(Vector3, Vector3, int)>
        {
            (new Vector3(-0.5f, 0, -0.35f), new Vector3(0.5f, 0.4f, 0.35f), 2),
            (new Vector3(-0.5f, 0.4f, 0.2f), new Vector3(0.5f, 0.85f, 0.35f), 3),
        }, true);
        Unfinished = MakeMesh(gd, chair, false);
    }

    private static DGRP3DMesh MakeMesh(GraphicsDevice gd, List<(Vector3 min, Vector3 max, int colour)> boxes, bool rendered)
    {
        var verts = new List<DGRP3DVert>();
        var indices = new List<int>();
        foreach (var (min, max, colour) in boxes)
        {
            // each colour is a 3x4 texel block of the used 6x8: UVs in 0..1 of the used part
            var uv = new Vector2((colour % 2) * 0.5f + 0.25f, (colour / 2) * 0.5f + 0.25f);
            var c = new[]
            {
                new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, max.Y, min.Z), new Vector3(min.X, max.Y, min.Z),
                new Vector3(min.X, min.Y, max.Z), new Vector3(max.X, min.Y, max.Z), new Vector3(max.X, max.Y, max.Z), new Vector3(min.X, max.Y, max.Z)
            };
            var faces = new[] { (0, 1, 2, 3), (5, 4, 7, 6), (4, 0, 3, 7), (1, 5, 6, 2), (3, 2, 6, 7), (4, 5, 1, 0) };
            foreach (var (a, b, cc, d) in faces)
            {
                var start = verts.Count;
                var normal = Vector3.Normalize(Vector3.Cross(c[b] - c[a], c[d] - c[a]));
                foreach (var i in new[] { a, b, cc, d }) verts.Add(new DGRP3DVert(c[i], normal, uv));
                indices.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
        }
        var vb = new VertexBuffer(gd, typeof(DGRP3DVert), verts.Count, BufferUsage.None);
        vb.SetData(verts.ToArray());
        var ib = new IndexBuffer(gd, IndexElementSize.ThirtyTwoBits, indices.Count, BufferUsage.None);
        ib.SetData(indices.ToArray());
        var mesh = new DGRP3DMesh(null, null, gd, null); // (empty: a DGRP is what it would be made from)
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var v in verts) { lo = Vector3.Min(lo, v.Position); hi = Vector3.Max(hi, v.Position); }
        mesh.Bounds = rendered ? new BoundingBox(lo, hi) : (BoundingBox?)null;
        mesh.Geoms.Add(new Dictionary<Texture2D, DGRP3DGeometry>
        {
            [Palette] = new DGRP3DGeometry { Pixel = Palette, Verts = vb, Indices = ib, PrimCount = indices.Count / 3, Rendered = rendered }
        });
        return mesh;
    }

    private static Color[] Pixels(Texture2D t)
    {
        var data = new Color[t.Width * t.Height];
        t.GetData(data);
        return data;
    }

    /// <summary>The opaque part of a picture: its bounds and how many pixels, and how many are magenta-ish.</summary>
    private static (Rectangle box, int opaque, int magenta) Measure(Color[] px, int size)
    {
        int x0 = size, y0 = size, x1 = -1, y1 = -1, opaque = 0, magenta = 0;
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i].A < 128) continue;
            opaque++;
            int x = i % size, y = i / size;
            x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
            if (px[i].R > 200 && px[i].B > 200 && px[i].G < 80) magenta++;
        }
        return (x1 < 0 ? Rectangle.Empty : new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1), opaque, magenta);
    }

    public static int Run(GraphicsDevice gd, string outDir)
    {
        var size = VRCatalogThumbnails.Size;
        using var thumbs = new VRCatalogThumbnails(gd) { Meshes = Meshes };

        // the lot's lighting and matrices on the shared effect, and a render target set: all must be as they were after
        var effect = WorldContent.RCObject;
        var light = new Texture2D(gd, 2, 2);
        effect.AdvancedLight = light;
        effect.LightingAdjust = new Vector3(0.5f, 0.6f, 0.7f);
        var vp = Matrix.CreateTranslation(1, 2, 3);
        effect.ViewProjection = vp;
        var technique = effect.CurrentTechnique;
        using var current = new RenderTarget2D(gd, 64, 32);
        gd.SetRenderTarget(current);
        var viewport = gd.Viewport;

        Check("nothing at first (asked for, drawn on the next frame)", thumbs.Get(1, false) == null && thumbs.Get(2, false) == null
            && thumbs.Get(3, false) == null && thumbs.Get(4, false) == null, null);
        thumbs.Render();
        var chair = thumbs.Get(1, false);
        var sofa = thumbs.Get(2, false);
        Check("two drawn on the next frame", chair != null && sofa != null, null);
        var targets = gd.GetRenderTargets();
        Check("render target and viewport put back", targets.Length == 1 && targets[0].RenderTarget == current
            && gd.Viewport.Width == viewport.Width && gd.Viewport.Height == viewport.Height, null);
        Check("the lot's lighting and matrices put back", effect.Parameters["advancedLight"].GetValueTexture2D() == light
            && effect.Parameters["LightingAdjust"].GetValueVector3() == new Vector3(0.5f, 0.6f, 0.7f)
            && effect.Parameters["ViewProjection"].GetValueMatrix() == vp && effect.CurrentTechnique == technique, null);
        if (chair == null || sofa == null) return Failures;

        var chairPx = Pixels(chair);
        var (chairBox, chairOpaque, chairMagenta) = Measure(chairPx, size);
        Check("the chair is drawn, with transparent corners", chairOpaque > size * size / 20 && chairPx[0].A == 0 && chairPx[chairPx.Length - 1].A == 0,
            $"{chairOpaque} opaque pixels, box {chairBox}");
        Check("textures use only their unpadded part (UVScale)", chairMagenta == 0, $"{chairMagenta} magenta pixels");
        var distinct = new HashSet<Color>();
        foreach (var p in chairPx) if (p.A == 255) distinct.Add(new Color(p.R / 32, p.G / 32, p.B / 32));
        Check("evenly lit in its own colours", distinct.Count >= 3, $"{distinct.Count} colours");
        var (sofaBox, _, _) = Measure(Pixels(sofa), size);
        Check("framed: the whole object, filling the picture", chairBox.Width > size / 3 && sofaBox.Width > size / 2
            && chairBox.Left > 0 && chairBox.Right < size && sofaBox.Left > 0 && sofaBox.Right < size && sofaBox.Top > 0 && sofaBox.Bottom < size,
            $"chair {chairBox}, two-tile sofa {sofaBox}");

        // a mesh FreeSO is still making waits, and is drawn once it's done; an object without one never is
        for (int i = 0; i < 3; i++) { thumbs.Get(3, false); thumbs.Get(4, false); thumbs.Render(); }
        Check("a mesh still being made waits", thumbs.Get(3, false) == null, null);
        Unfinished.Bounds = Chair.Bounds;
        foreach (var g in Unfinished.Geoms[0].Values) g.Rendered = true;
        thumbs.Render();
        Check("...and is drawn once made", thumbs.Get(3, false) != null, null);
        Check("no mesh: no picture (the catalogue's stays)", thumbs.Get(4, false) == null, null);

        // the one you point at turns
        thumbs.Get(1, true);
        thumbs.Render();
        var spin1 = thumbs.Get(1, true);
        var first = spin1 != null ? Pixels(spin1) : null;
        for (int i = 0; i < 45; i++) { thumbs.Get(1, true); thumbs.Render(); }
        var spin2 = thumbs.Get(1, true);
        int changed = 0;
        if (first != null && spin2 != null)
        {
            var later = Pixels(spin2);
            for (int i = 0; i < later.Length; i++) if (later[i] != first[i]) changed++;
        }
        Check("the pointed-at one turns", spin1 != null && spin1 != chair && changed > size * size / 50, $"{changed} pixels changed in half a second");
        Save(chair, Path.Combine(outDir, "thumb-chair.png"));
        Save(sofa, Path.Combine(outDir, "thumb-sofa.png"));
        if (spin2 != null) Save(spin2, Path.Combine(outDir, "thumb-turned.png"));
        gd.SetRenderTarget(null);
        light.Dispose();
        return Failures;
    }

    private static void Save(Texture2D t, string path)
    {
        using var file = File.Create(path);
        t.SaveAsPng(file, t.Width, t.Height);
    }
}
