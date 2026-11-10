using FSO.Files.Formats.IFF;
using FSO.Files.Formats.IFF.Chunks;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Writes TestCube.iff: a minimal Sims 1 object, a plain grey cube, with no behaviour.
// Usage: MakeTestCube <output folder>   (then it reads the file back and checks it)
//
// The object has no art files: the sprites are drawn here by tracing rays against a cube, using the same
// camera maths the engine uses to turn a sprite and its depth buffer back into a 3D model (DGRP3DMesh).
// So the sprite, its depth and the 3D model the game builds from them all describe the same cube.

const uint GUID = 0x7E57C0BE;
const float HalfSize = 0.35f;   //in tiles. the cube sits in the middle of its tile...
const float Height = 0.7f;      //...standing on the floor

// --- the engine's sprite space -> object space transform, for the near zoom (see DGRP3DMesh) ---
const float ZOff = -55f;
const float DepthRange = 110.851251f / 0.39f; //sprite space distance covered by depth 1 -> 0
Matrix ToObject = Matrix.CreateTranslation(new Vector3(-72, -344, ZOff))
    * Matrix.CreateScale((1f / 128) * 1.43f)
    * Matrix.CreateScale(1, -1, 1)
    * Matrix.CreateRotationX((float)Math.PI / -6)
    * Matrix.CreateRotationY((float)Math.PI / 4);
// a near zoom canvas position (x from the middle, y from the top, see DGRP3DMesh "pos") at depth d
Vector3 Reconstruct(float x, float y, float d) => Vector3.Transform(new Vector3(x + 72, y, (1f - d) * DepthRange), ToObject);

Color Top = new Color(205, 205, 205), Left = new Color(160, 160, 160), Right = new Color(120, 120, 120);
Color[] Palette = Enumerable.Repeat(new Color(255, 0, 255), 256).ToArray();
Palette[0] = Top; Palette[1] = Left; Palette[2] = Right;

// What a ray through this canvas position hits: the colour and depth of the cube's surface nearest the camera.
bool Trace(float x, float y, out byte colour, out float depth)
{
    colour = 0; depth = 1;
    var a = Reconstruct(x, y, 1f);          //the ray at depth 1 (furthest)
    var b = Reconstruct(x, y, 0f) - a;      //and how far it travels by depth 0 (nearest)
    float enter = 0, exit = 1; int exitAxis = -1;
    var lo = new[] { -HalfSize, 0f, -HalfSize }; var hi = new[] { HalfSize, Height, HalfSize };
    var origin = new[] { a.X, a.Y, a.Z }; var dir = new[] { b.X, b.Y, b.Z };
    for (int axis = 0; axis < 3; axis++)
    {
        if (Math.Abs(dir[axis]) < 1e-9f) { if (origin[axis] < lo[axis] || origin[axis] > hi[axis]) return false; continue; }
        var t1 = (lo[axis] - origin[axis]) / dir[axis]; var t2 = (hi[axis] - origin[axis]) / dir[axis];
        if (t1 > t2) (t1, t2) = (t2, t1);
        if (t1 > enter) enter = t1;
        if (t2 < exit) { exit = t2; exitAxis = axis; }
    }
    if (enter > exit || exitAxis == -1) return false;
    //travelling toward the camera, the ray leaves the cube through the face the camera sees
    colour = (byte)(exitAxis == 1 ? 0 : (exitAxis == 0 ? 1 : 2));
    depth = 1f - exit;
    return true;
}

// the quantizer normally comes from the Volcanic editor. ours is trivial: the pixels are already palette colours
SPR2FrameEncoder.QuantizeFrame = (SPR2Frame frame, out byte[] indices) =>
{
    indices = frame.PixelData.Select(p => p.A == 0 ? (byte)255 : (byte)Array.IndexOf(Palette, new Color(p.R, p.G, p.B))).ToArray();
    return Palette;
};

IffFile.TargetTS1 = true;
var iff = new IffFile();

var objd = new OBJD()
{
    ChunkID = 16807, ChunkLabel = "Test Cube", ChunkProcessed = true,
    GUID = GUID,
    ObjectType = OBJDType.Normal,
    BaseGraphicID = 100, NumGraphics = 1,
    CatalogStringsID = 2000,
    Price = 10,
    FunctionFlags = 32,      //buy mode: decorative
    FunctionSubsort = 128,   //...under "other"
    RoomFlags = 0xFF,        //for every kind of room. without a room the catalog hides it
};
iff.AddChunk(objd);

var ctss = new CTSS() { ChunkID = 2000, ChunkLabel = "Catalog", ChunkProcessed = true };
ctss.InsertString(0, new STRItem("Test Cube"));
ctss.InsertString(1, new STRItem("A plain grey cube from the Test Cube sample pack. It does nothing, which is exactly what it is for."));
iff.AddChunk(ctss);

// one sprite per zoom. the cube looks the same from all four sides, so every direction shares it
var images = new List<DGRPImage>();
var dgrp = new DGRP() { ChunkID = 100, ChunkLabel = "Cube", ChunkProcessed = true };
for (uint zoom = 3; zoom >= 1; zoom--)
{
    var scale = 1f / (1 << (int)(3 - zoom)); //near 1, medium 1/2, far 1/4
    // where the cube's corners land on the near canvas gives the sprite's rectangle
    var inv = Matrix.Invert(ToObject);
    var corners = from cx in new[] { -HalfSize, HalfSize } from cy in new[] { 0f, Height } from cz in new[] { -HalfSize, HalfSize }
                  select Vector3.Transform(new Vector3(cx, cy, cz), inv);
    int minX = (int)Math.Floor((corners.Min(c => c.X) - 72) * scale) - 1, maxX = (int)Math.Ceiling((corners.Max(c => c.X) - 72) * scale) + 1;
    int minY = (int)Math.Floor(corners.Min(c => c.Y) * scale) - 1, maxY = (int)Math.Ceiling(corners.Max(c => c.Y) * scale) + 1;
    int w = maxX - minX + 1, h = maxY - minY + 1;

    var px = new Color[w * h]; var z = new byte[w * h];
    for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            //the middle of this pixel, in near zoom canvas units
            var hit = Trace((minX + x + 0.5f) / scale - 0.5f, (minY + y + 0.5f) / scale - 0.5f, out var colour, out var depth);
            px[y * w + x] = hit ? Palette[colour] : Color.Transparent;
            z[y * w + x] = hit ? (byte)Math.Clamp((int)Math.Round(depth * 255), 0, 254) : (byte)255;
        }

    var id = (ushort)(100 + (3 - zoom));
    var palt = new PALT() { ChunkID = id, ChunkLabel = "", ChunkProcessed = true, Colors = Palette };
    iff.AddChunk(palt);
    var spr = new SPR2() { ChunkID = id, ChunkLabel = "Cube " + new[] { "", "far", "medium", "near" }[zoom], ChunkProcessed = true };
    iff.AddChunk(spr);
    var frame = new SPR2Frame(spr);
    frame.SetData(px, z, new Rectangle(0, 0, w, h));
    frame.SetPalt(palt);
    spr.Frames = new[] { frame };

    // the canvas for a zoom is 136*scale wide with its baseline at 348*scale: a sprite is placed by its
    // left edge from the middle, and its bottom edge from the baseline
    var offset = new Vector2(minX, (maxY + 1) - 348 * scale);
    foreach (var direction in new uint[] { 0x01, 0x04, 0x10, 0x40 })
    {
        images.Add(new DGRPImage(dgrp)
        {
            Direction = direction, Zoom = zoom,
            Sprites = new[] { new DGRPSprite(dgrp) { SpriteID = id, SpriteFrameIndex = 0, SpriteOffset = offset } }
        });
    }
}
dgrp.Images = images.ToArray();
iff.AddChunk(dgrp);

var outDir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(outDir);
var path = Path.Combine(outDir, "TestCube.iff");
using (var stream = File.Create(path)) iff.Write(stream);
Console.WriteLine("Wrote " + path + " (" + new FileInfo(path).Length + " bytes)");

// --- read it back the way the game does, and check the cube survived ---
var check = new IffFile(path);
var o = check.List<OBJD>().Single();
var strings = check.Get<CTSS>(o.CatalogStringsID);
Console.WriteLine($"Object: \"{o.ChunkLabel}\" GUID {o.GUID:X8}, type {o.ObjectType}, price {o.Price}, catalog \"{strings.GetString(0)}\"");
if (o.GUID != GUID || o.FunctionFlags != 32 || o.FunctionSubsort != 128 || o.RoomFlags == 0 || o.NumGraphics != 1) throw new Exception("Object definition did not survive");
var g = check.Get<DGRP>(o.BaseGraphicID);
if (g.Images.Length != 12) throw new Exception("Expected 12 images");
for (uint rotation = 0; rotation < 4; rotation++)
    for (uint zoom = 1; zoom <= 3; zoom++)
        if (g.GetImage(1, zoom, rotation) == null) throw new Exception("Missing image for rotation " + rotation + " zoom " + zoom);

// rebuild 3D points from the near sprite exactly as DGRP3DMesh does, and measure how far they are from the cube
var near = g.GetImage(1, 3, 0).Sprites[0];
var f = check.Get<SPR2>((ushort)near.SpriteID).Frames[0];
f.DecodeIfRequired(true);
var start = near.SpriteOffset + new Vector2(72, 348 - f.Height);
float worst = 0; int solid = 0; var counts = new int[3];
for (int y = 0; y < f.Height; y++)
    for (int x = 0; x < f.Width; x++)
    {
        var d = f.ZBufferData[y * f.Width + x] / 255f;
        var p = f.PixelData[y * f.Width + x];
        if (d >= 0.999f) { if (p.A != 0) throw new Exception("Solid pixel without depth"); continue; }
        if (p.A != 255) throw new Exception("Depth without a solid pixel");
        solid++; counts[Array.IndexOf(Palette, new Color(p.R, p.G, p.B))]++;
        var pt = Vector3.Transform(new Vector3(start.X + x, start.Y + y, (1f - d) * DepthRange), ToObject);
        //distance outside the cube, or (if inside) to its nearest face
        var outX = Math.Max(Math.Abs(pt.X) - HalfSize, 0); var outY = Math.Max(Math.Max(-pt.Y, pt.Y - Height), 0); var outZ = Math.Max(Math.Abs(pt.Z) - HalfSize, 0);
        var outside = (float)Math.Sqrt(outX * outX + outY * outY + outZ * outZ);
        var inside = Math.Min(Math.Min(HalfSize - Math.Abs(pt.X), HalfSize - Math.Abs(pt.Z)), Math.Min(pt.Y, Height - pt.Y));
        worst = Math.Max(worst, outside > 0 ? outside : Math.Max(inside, 0));
    }
Console.WriteLine($"Near sprite {f.Width}x{f.Height} at offset {near.SpriteOffset.X},{near.SpriteOffset.Y}: {solid} solid pixels (top {counts[0]}, left {counts[1]}, right {counts[2]})");
Console.WriteLine($"Rebuilt in 3D, every pixel is within {worst:0.000} tiles of the cube's surface");
if (solid < 1000 || counts.Any(c => c < 200) || worst > 0.03f) throw new Exception("The sprite does not describe the cube");
Console.WriteLine("OK");

// optional: MakeTestCube <output folder> <preview.ppm> saves the near sprite and its depth side by side, to look at
if (args.Length > 1)
{
    using var ppm = new StreamWriter(args[1]);
    ppm.WriteLine($"P3 {f.Width * 2} {f.Height} 255");
    for (int y = 0; y < f.Height; y++)
    {
        for (int x = 0; x < f.Width; x++) { var p = f.PixelData[y * f.Width + x]; ppm.Write(p.A == 0 ? "40 90 150 " : $"{p.R} {p.G} {p.B} "); }
        for (int x = 0; x < f.Width; x++) { var d = f.ZBufferData[y * f.Width + x]; ppm.Write($"{d} {d} {d} "); }
        ppm.WriteLine();
    }
}
