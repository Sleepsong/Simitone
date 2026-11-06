using System;
using System.IO;
using System.Linq;
using FSO.Content.Model;
using FSO.SimAntics.Model;
using FSO.SimAntics.Utils;
using Microsoft.Xna.Framework;

// Terrain painting (FreeSO's VMTerrainPaintTools and VMArchitectureTerrain.Paint): the brushes, taking paint off,
// previews leaving the real ground alone, and saving.
static class Program
{
    static int Failures;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: {detail}");
        if (!ok) Failures++;
    }

    static VMArchitectureCommand Brush(int x, int y, int radius, TerrainType type, bool soft = false)
        => new VMArchitectureCommand { Type = VMArchitectureCommandType.TERRAIN_PAINT, x = x, y = y, x2 = radius, pattern = (ushort)type, style = (ushort)(soft ? 1 : 0) };

    static VMArchitectureCommand Erase(int x, int y, int radius)
        => new VMArchitectureCommand { Type = VMArchitectureCommandType.TERRAIN_PAINT, x = x, y = y, x2 = radius, pattern = VMTerrainPaintTools.Erase };

    static int Count(VMArchitectureTerrain t, TerrainType type) => t.Paint?.Count(p => p == (byte)type) ?? 0;

    static int Main()
    {
        var all = new Rectangle(0, 0, 24, 24);

        // ---- brushes
        var t = new VMArchitectureTerrain(24, 24);
        Check("a new lot has no paint", t.Paint == null && !t.AnyPaint, "no layer");
        var n = VMTerrainPaintTools.Apply(t, all, Brush(10, 10, 0, TerrainType.SAND));
        Check("smallest brush: one point", n == 1 && t.Paint[10 * 24 + 10] == (byte)TerrainType.SAND, $"{n} changed");
        n = VMTerrainPaintTools.Apply(t, all, Brush(10, 10, 1, TerrainType.SAND));
        Check("brush 1: the 3x3 around it (the centre was already sand)", n == 8 && Count(t, TerrainType.SAND) == 9, $"{n} changed, {Count(t, TerrainType.SAND)} sand");
        n = VMTerrainPaintTools.Apply(t, all, Brush(10, 10, 1, TerrainType.SAND));
        Check("painting the same again changes nothing (and costs nothing)", n == 0, $"{n} changed");
        n = VMTerrainPaintTools.Apply(t, all, Brush(4, 16, 2, TerrainType.TS1AutumnGrass));
        Check("brush 2: round (5x5 without its corners)", n == 21, $"{n} changed");
        Check("unpainted points stay the lot's", t.Paint[0] == VMArchitectureTerrain.Unpainted && t.Paint[23 * 24 + 23] == VMArchitectureTerrain.Unpainted, "255");

        var hard = new VMArchitectureTerrain(24, 24);
        var soft = new VMArchitectureTerrain(24, 24);
        var nh = VMTerrainPaintTools.Apply(hard, all, Brush(12, 12, 3, TerrainType.SNOW));
        var ns = VMTerrainPaintTools.Apply(soft, all, Brush(12, 12, 3, TerrainType.SNOW, soft: true));
        var soft2 = new VMArchitectureTerrain(24, 24);
        VMTerrainPaintTools.Apply(soft2, all, Brush(12, 12, 3, TerrainType.SNOW, soft: true));
        Check("soft brush: a feathered edge (fewer points than hard)", ns < nh && ns > nh / 2, $"hard {nh}, soft {ns}");
        Check("soft brush: the same pattern every time (so every machine agrees)", soft.Paint.SequenceEqual(soft2.Paint), "same");
        Check("soft brush: the middle is solid", soft.Paint[12 * 24 + 12] == (byte)TerrainType.SNOW && soft.Paint[12 * 24 + 13] == (byte)TerrainType.SNOW, "painted");

        var edge = new VMArchitectureTerrain(24, 24);
        n = VMTerrainPaintTools.Apply(edge, new Rectangle(5, 5, 10, 10), Brush(5, 5, 2, TerrainType.ROCK));
        Check("paint stays inside the buildable area", n > 0 && edge.Paint[4 * 24 + 5] == VMArchitectureTerrain.Unpainted && edge.Paint[5 * 24 + 5] == (byte)TerrainType.ROCK, $"{n} changed");
        n = VMTerrainPaintTools.Apply(edge, all, Brush(0, 0, 3, TerrainType.ROCK));
        Check("paint off the lot's edge is clipped", n > 0, $"{n} changed");
        n = VMTerrainPaintTools.Apply(new VMArchitectureTerrain(24, 24), all, Brush(8, 8, 1, TerrainType.WATER));
        Check("only the paintable types (no water)", n == 0, $"{n} changed");
        n = VMTerrainPaintTools.Apply(new VMArchitectureTerrain(24, 24), all, Brush(8, 8, 99, TerrainType.GRASS));
        Check("brushes are capped in size", n == Enumerable.Range(-6, 13).Sum(dy => Enumerable.Range(-6, 13).Count(dx => dx * dx + dy * dy <= 6.5f * 6.5f)), $"{n} changed");

        // ---- taking paint off
        n = VMTerrainPaintTools.Apply(t, all, Erase(10, 10, 1));
        Check("remove: back to the lot's ground", n == 9 && Count(t, TerrainType.SAND) == 0, $"{n} changed");
        n = VMTerrainPaintTools.Apply(t, all, Erase(4, 16, 2));
        Check("remove: with nothing left painted, no paint layer", n == 21 && t.Paint == null && !t.AnyPaint, $"{n} changed");
        n = VMTerrainPaintTools.Apply(t, all, Erase(4, 16, 2));
        Check("remove: nothing painted to remove", n == 0, $"{n} changed");

        // ---- previews leave the real ground alone
        var real = new VMArchitectureTerrain(24, 24);
        VMTerrainPaintTools.Apply(real, all, Brush(6, 6, 1, TerrainType.TS1DarkGrass));
        var before = (byte[])real.Paint.Clone();
        real.EnterVis();
        VMTerrainPaintTools.Apply(real, all, Brush(15, 15, 2, TerrainType.SAND));
        real.ExitVis();
        Check("preview: the real paint is unchanged", real.Paint.SequenceEqual(before), "unchanged");
        Check("preview: the preview has the new paint", real.VisPaint != null && real.VisPaint[15 * 24 + 15] == (byte)TerrainType.SAND, "sand in the preview");
        var bare = new VMArchitectureTerrain(24, 24);
        bare.EnterVis();
        VMTerrainPaintTools.Apply(bare, all, Brush(3, 3, 0, TerrainType.SAND));
        bare.ExitVis();
        Check("preview: painting an unpainted lot doesn't give it a layer", bare.Paint == null && bare.VisPaint != null, "real: none");

        // ---- saving
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(777); // the rest of a save
        VMArchitectureTerrain.WritePaint(w, 24, 24, before);
        ms.Position = 0;
        var r = new BinaryReader(ms);
        r.ReadInt32();
        var back = VMArchitectureTerrain.ReadPaintIfPresent(r, 24, 24);
        Check("save: the paint comes back", back != null && back.SequenceEqual(before), $"{back?.Length} points");

        ms.Position = 4;
        var wrong = VMArchitectureTerrain.ReadPaintIfPresent(new BinaryReader(ms), 32, 32);
        Check("save: paint for another size of lot is ignored", wrong == null, "null");

        var ms2 = new MemoryStream();
        var w2 = new BinaryWriter(ms2);
        w2.Write(777);
        w2.Write(0x12345678); w2.Write(0L); w2.Write(0L); // something else after the save
        ms2.Position = 4;
        var r2 = new BinaryReader(ms2);
        Check("save: other data after a save isn't mistaken for paint", VMArchitectureTerrain.ReadPaintIfPresent(r2, 24, 24) == null && ms2.Position == 4, $"position {ms2.Position}");

        Check("the paint types are the game's (all but water)", VMTerrainPaintTools.PaintTypes.Length == 7 && !VMTerrainPaintTools.PaintTypes.Contains(TerrainType.WATER),
            string.Join(", ", VMTerrainPaintTools.PaintTypes.Select(VMTerrainPaintTools.Name)));

        Console.WriteLine(Failures == 0 ? "all checks passed" : $"{Failures} check(s) failed");
        return Failures == 0 ? 0 : 1;
    }
}
