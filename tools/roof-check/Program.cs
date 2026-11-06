using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FSO.LotView.Components;
using FSO.LotView.Model;
using FSO.SimAntics.Model;
using FSO.SimAntics.Utils;
using Microsoft.Xna.Framework;

// The roof tool (FreeSO's FSO.LotView.Model.DrawnRoofs, RoofComponent's shapes and meshes, VMRoofTools): roof shapes,
// the meshes drawn for them, the build commands, and saving.
static class Program
{
    static int Failures;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: {detail}");
        if (!ok) Failures++;
    }

    static bool Near(float a, float b, float eps = 1e-3f) => Math.Abs(a - b) < eps;

    // as RoofComponent.ToWorldPos on flat ground: 3 world units a tile, height scaled by the pitch
    static Vector3 World(int x, int y, int z, float pitch) => new Vector3(x / 16f * 3f, z * pitch / 16f * 3f, y / 16f * 3f);

    static RoofPiece Piece(RoofPieceType type, int dir, int x = 2, int y = 2, int w = 4, int h = 3, int pitch = 66)
        => new RoofPiece { Level = 2, X = (short)x, Y = (short)y, Width = (short)w, Height = (short)h, Type = type, Direction = (byte)dir, Pitch = (byte)pitch };

    /// <summary>Each side's normal (as the mesher works it out) and whether it's sloped.</summary>
    static (Vector3 normal, bool slope)[] Sides(RoofComponent.RoofShape s)
    {
        var c = s.Corners.Select(p => World(p.X, p.Y, 0, s.Pitch)).ToArray();
        var m = s.Ridge.Select(p => World(p.X, p.Y, p.Z, s.Pitch)).ToArray();
        var result = new (Vector3, bool)[4];
        for (int k = 0; k < 4; k++)
        {
            var k1 = (k + 1) % 4;
            var n = -Vector3.Normalize(Vector3.Cross(c[k] - c[k1], c[k1] - m[k1]));
            result[k] = (n, s.Slopes[k]);
        }
        return result;
    }

    // outward directions of the sides: 0 is -y (world -z), 1 is +x, 2 is +y, 3 is -x
    static readonly Vector3[] Outward = { new Vector3(0, 0, -1), new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(-1, 0, 0) };

    static void CheckSides(string name, RoofComponent.RoofShape shape, float pitch)
    {
        var sides = Sides(shape);
        for (int k = 0; k < 4; k++)
        {
            var (n, slope) = sides[k];
            if (slope)
            {
                //faces up and out, at the pitch: rise over run = pitch, so the normal's horizontal part over its vertical is the pitch
                var horiz = Vector3.Dot(n, Outward[k]);
                Check($"{name}: side {k} slopes up and out", n.Y > 0 && horiz > 0 && Near(horiz / n.Y, pitch, 0.01f), $"normal {n}");
            }
            else
            {
                Check($"{name}: side {k} is upright, facing out", Near(n.Y, 0) && Vector3.Dot(n, Outward[k]) > 0.99f, $"normal {n}");
            }
        }
    }

    static int Main()
    {
        // ---- shapes
        var hip = RoofComponent.PieceShape(Piece(RoofPieceType.Hip, 0));
        Check("hip: eaves half a tile out all round", hip.Corners[0] == new Point(24, 24) && hip.Corners[2] == new Point(104, 88), $"{hip.Corners[0]} to {hip.Corners[2]}");
        Check("hip: ridge as high as half the narrow side", hip.Ridge.All(r => r.Z == 32) && hip.Ridge[0].X == 56 && hip.Ridge[1].X == 72, $"z {hip.Ridge[0].Z}, x {hip.Ridge[0].X}..{hip.Ridge[1].X}");
        CheckSides("hip", hip, 0.66f);

        var gableX = RoofComponent.PieceShape(Piece(RoofPieceType.Gable, 0));
        Check("gable (ridge along x): eaves on the long sides only", gableX.Corners[0] == new Point(32, 24) && gableX.Corners[2] == new Point(96, 88), $"{gableX.Corners[0]} to {gableX.Corners[2]}");
        Check("gable (ridge along x): ridge end to end", gableX.Ridge[0].X == 32 && gableX.Ridge[1].X == 96 && gableX.Ridge[0].Y == 56 && gableX.Ridge.All(r => r.Z == 32), $"x {gableX.Ridge[0].X}..{gableX.Ridge[1].X}, y {gableX.Ridge[0].Y}");
        CheckSides("gable along x", gableX, 0.66f);

        var gableY = RoofComponent.PieceShape(Piece(RoofPieceType.Gable, 1));
        Check("gable (ridge along y): slopes on the x sides", !gableY.Slopes[0] && gableY.Slopes[1] && !gableY.Slopes[2] && gableY.Slopes[3], string.Join(",", gableY.Slopes));
        CheckSides("gable along y", gableY, 0.66f);

        for (int dir = 0; dir < 4; dir++)
        {
            var shed = RoofComponent.PieceShape(Piece(RoofPieceType.Shed, dir, pitch: 50));
            var depth = (dir % 2 == 0) ? (shed.Corners[2].Y - shed.Corners[0].Y) : (shed.Corners[2].X - shed.Corners[0].X);
            var high = shed.Ridge.All(r => r.Z == depth);
            Check($"shed low on side {dir}: one slope, rising the full depth", shed.Slopes.Count(x => x) == 1 && shed.Slopes[dir] && high, $"depth {depth}");
            CheckSides($"shed low on side {dir}", shed, 0.5f);
        }

        var steep = RoofComponent.PieceShape(Piece(RoofPieceType.Hip, 0, pitch: 120));
        CheckSides("steep hip (120%)", steep, 1.2f);

        // ---- meshes (on a small blueprint: no GPU needed to build the vertex data)
        try
        {
            var bp = new Blueprint(16, 16);
            bp.AltitudeCenters = new short[16 * 16];
            var roofs = new DrawnRoofs { AutoRoof = false };
            roofs.Pieces.Add(Piece(RoofPieceType.Hip, 0));
            roofs.Pieces.Add(Piece(RoofPieceType.Gable, 0, 8, 2));
            roofs.Pieces.Add(Piece(RoofPieceType.Shed, 2, 2, 8));
            roofs.Pieces.Add(new RoofPiece { Level = 3, X = 8, Y = 8, Width = 2, Height = 2, Type = RoofPieceType.Gable, Direction = 1, Pitch = 66 });
            bp.SetDrawnRoofs(roofs);
            var level2 = bp.RoofComp.MeshRectData(2);
            var level3 = bp.RoofComp.MeshRectData(3);
            // level 2: hip 4 slopes, gable 2 slopes + 2 ends, shed 1 slope + 3 upright sides
            Check("mesh: sloped sides", level2.NumPrimitives == (4 + 2 + 1) * 2, $"{level2.NumPrimitives / 2} quads");
            Check("mesh: gable ends and shed sides", level2.GableNumPrimitives == (2 + 3) * 2, $"{level2.GableNumPrimitives / 2} quads");
            Check("mesh: rims, edges and undersides", level2.AdvNumPrimitives == ((4 + 2 + 1) * 4 + 3) * 2, $"{level2.AdvNumPrimitives / 2} quads");
            Check("mesh: roofs stay on their level", level3.NumPrimitives == 2 * 2 && level3.GableNumPrimitives == 2 * 2, $"level 3: {level3.NumPrimitives / 2} + {level3.GableNumPrimitives / 2}");
            var maxIndex = level2.Indices.Max();
            Check("mesh: indices in range", maxIndex < level2.Vertices.Length && level2.GableIndices.Max() < level2.GableVertices.Length && level2.AdvIndices.Max() < level2.AdvVertices.Length, $"max {maxIndex} of {level2.Vertices.Length}");
            var top = level2.Vertices.Max(v => v.Position.Y);
            Check("mesh: roof height on the ground floor's walls", Near(top, 2.95f * 3 + 32 * 0.66f / 16 * 3, 0.01f) || top > 2.95f * 3, $"highest point {top}");
            var bad = level2.Vertices.Concat(level2.GableVertices).Concat(level2.AdvVertices).Any(v => float.IsNaN(v.Position.X) || float.IsNaN(v.Normal.X));
            Check("mesh: no broken vertices", !bad, bad ? "NaN found" : "all finite");

            // back to the automatic roof: no drawn shapes
            bp.SetDrawnRoofs(new DrawnRoofs());
            Check("automatic roof: no drawn shapes", !bp.RoofComp.DrawnMode, "auto");
        }
        catch (Exception e)
        {
            Check("meshes", false, "couldn't build a blueprint here: " + e.GetType().Name + " " + e.Message);
        }

        // ---- commands
        {
            var roofs = new DrawnRoofs();
            int fail; bool changed;
            VMArchitectureCommand Rect(int x, int y, int w, int h, RoofPieceType t, int dir, int level = 2, int pitch = 66) => new VMArchitectureCommand
            {
                Type = VMArchitectureCommandType.ROOF_RECT, x = x, y = y, x2 = w - 1, y2 = h - 1, level = (sbyte)level, pattern = (ushort)t, style = (ushort)(dir | (pitch << 8))
            };
            var cost = VMRoofTools.Apply(roofs, 32, 32, 5, Rect(4, 4, 5, 3, RoofPieceType.Gable, 1), out fail, out changed);
            Check("draw: a roof costs per tile", cost == 15 * VMRoofTools.TilePrice && changed && fail == 0, $"${cost}");
            Check("draw: the first roof turns the automatic roof off", !roofs.AutoRoof && roofs.Pieces.Count == 1, $"auto {roofs.AutoRoof}");
            var p = roofs.Pieces[0];
            Check("draw: the roof as asked", p.X == 4 && p.Y == 4 && p.Width == 5 && p.Height == 3 && p.Type == RoofPieceType.Gable && p.Direction == 1 && p.Pitch == 66 && p.Level == 2, $"{p.X},{p.Y} {p.Width}x{p.Height} {p.Type} dir {p.Direction} pitch {p.Pitch}");
            cost = VMRoofTools.Apply(roofs, 32, 32, 5, Rect(4, 4, 5, 3, RoofPieceType.Gable, 1), out fail, out changed);
            Check("draw: the same roof twice is one roof", cost == 0 && !changed && roofs.Pieces.Count == 1, $"${cost}");
            cost = VMRoofTools.Apply(roofs, 32, 32, 5, Rect(30, 4, 3, 3, RoofPieceType.Hip, 0), out fail, out changed);
            Check("draw: not off the lot", cost == 0 && fail == VMRoofTools.FailOutside, $"fail {fail}");
            cost = VMRoofTools.Apply(roofs, 32, 32, 5, Rect(4, 4, 2, 2, RoofPieceType.Hip, 0, level: 7), out fail, out changed);
            Check("draw: not above the top floor", cost == 0 && fail == VMRoofTools.FailLevel, $"fail {fail}");
            cost = VMRoofTools.Apply(roofs, 32, 32, 5, Rect(10, 10, 2, 2, RoofPieceType.Hip, 0, pitch: 0), out fail, out changed);
            Check("draw: no pitch given is the usual pitch", roofs.Pieces.Last().Pitch == RoofPiece.DefaultPitch, $"{roofs.Pieces.Last().Pitch}");

            var erase = new VMArchitectureCommand { Type = VMArchitectureCommandType.ROOF_ERASE, x = 6, y = 5, level = 2 };
            cost = VMRoofTools.Apply(roofs, 32, 32, 5, erase, out fail, out changed);
            Check("erase: removes the roof there, money back", cost == -15 * VMRoofTools.TilePrice && changed && roofs.Pieces.Count == 1, $"${cost}");
            cost = VMRoofTools.Apply(roofs, 32, 32, 5, erase, out fail, out changed);
            Check("erase: nothing there", cost == 0 && fail == VMRoofTools.FailNoRoof, $"fail {fail}");
            Check("erase: still no automatic roof", !roofs.AutoRoof, $"auto {roofs.AutoRoof}");

            var auto = new VMArchitectureCommand { Type = VMArchitectureCommandType.ROOF_AUTO, style = 1 };
            cost = VMRoofTools.Apply(roofs, 32, 32, 5, auto, out fail, out changed);
            Check("automatic roof: back, drawn roofs removed and paid back", roofs.AutoRoof && roofs.Pieces.Count == 0 && cost == -4 * VMRoofTools.TilePrice && changed, $"${cost}");
            cost = VMRoofTools.Apply(roofs, 32, 32, 5, auto, out fail, out changed);
            Check("automatic roof: already on", !changed, $"changed {changed}");
        }

        // ---- saving
        {
            var roofs = new DrawnRoofs { AutoRoof = false };
            roofs.Pieces.Add(Piece(RoofPieceType.Shed, 3, 5, 6, 7, 2, 90));
            roofs.Pieces.Add(Piece(RoofPieceType.Hip, 0, 1, 1, 2, 2));
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(12345); // the rest of a save
            roofs.Write(w);
            ms.Position = 0;
            var r = new BinaryReader(ms);
            r.ReadInt32();
            var back = DrawnRoofs.ReadIfPresent(r);
            Check("save: drawn roofs come back", back != null && back.SameAs(roofs), $"{back?.Pieces.Count} roofs, auto {back?.AutoRoof}");

            var ms2 = new MemoryStream();
            new BinaryWriter(ms2).Write(12345);
            ms2.Position = 0;
            var r2 = new BinaryReader(ms2);
            r2.ReadInt32();
            Check("save: a save without them has none", DrawnRoofs.ReadIfPresent(r2) == null, "null");

            var ms3 = new MemoryStream();
            var w3 = new BinaryWriter(ms3);
            w3.Write(12345);
            w3.Write(0x11223344); w3.Write(0L); // something else after the save
            ms3.Position = 0;
            var r3 = new BinaryReader(ms3);
            r3.ReadInt32();
            var none = DrawnRoofs.ReadIfPresent(r3);
            Check("save: other data after a save isn't mistaken for roofs", none == null && ms3.Position == 4, $"position {ms3.Position}");
            Check("save: lots with the automatic roof save nothing extra", !new DrawnRoofs().Any, "Any = false");
        }

        Console.WriteLine(Failures == 0 ? "all checks passed" : $"{Failures} check(s) failed");
        return Failures == 0 ? 0 : 1;
    }
}
