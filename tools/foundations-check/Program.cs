using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FSO.LotView.Components.Geometry;
using FSO.LotView.Model;
using FSO.LotView.Utils;
using FSO.SimAntics.Model;
using FSO.SimAntics.Model.Routing;
using Microsoft.Xna.Framework;

// Foundations (FreeSO's FSO.LotView.Model.FoundationMap and what uses it): heights under Sims and things, which tile
// edges Sims can't cross, saving, and the model drawn for the sides and steps.
static class Program
{
    static int Failures;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: {detail}");
        if (!ok) Failures++;
    }

    static bool Near(float a, float b) => Math.Abs(a - b) < 1e-4f;

    // a 12x12 lot with a foundation on tiles x 4..6, y 4..6, and steps at (5, 7) rising toward -y onto it
    static FoundationMap Sample()
    {
        var map = new FoundationMap(12, 12);
        for (int y = 4; y <= 6; y++)
            for (int x = 4; x <= 6; x++)
            {
                map.Raise[y * 12 + x] = FoundationMap.DefaultHeight;
                map.Pattern[y * 12 + x] = 7;
            }
        map.Steps[7 * 12 + 5] = 1; // direction 0: toward -y
        map.Pattern[7 * 12 + 5] = 7;
        map.Refresh();
        return map;
    }

    static int Main()
    {
        var map = Sample();
        const short H = FoundationMap.DefaultHeight;

        // ---- heights
        Check("on the foundation", Near(map.RaiseAt(5.5f, 5.5f), H), $"{map.RaiseAt(5.5f, 5.5f)}");
        Check("off it", Near(map.RaiseAt(2.5f, 2.5f), 0) && Near(map.RaiseAt(7.01f, 5.5f), 0), $"{map.RaiseAt(2.5f, 2.5f)}, {map.RaiseAt(7.01f, 5.5f)}");
        Check("foot of the steps", Near(map.RaiseAt(5.5f, 7.999f), 0.001f * H), $"{map.RaiseAt(5.5f, 7.999f)}");
        Check("halfway up", Near(map.RaiseAt(5.5f, 7.5f), H / 2f), $"{map.RaiseAt(5.5f, 7.5f)}");
        Check("head of the steps", Near(map.RaiseAt(5.5f, 7.0f), H), $"{map.RaiseAt(5.5f, 7.0f)}");
        Check("steps lead to their foundation", map.StepTop(5, 7) == H && map.StepDirection(5, 7) == 0, $"top {map.StepTop(5, 7)}");
        Check("walls on the edge stand on it", map.WallRaise(7, 5, WallSegments.TopLeft) == H && map.WallRaise(7, 5, WallSegments.TopRight) == 0,
            $"TL {map.WallRaise(7, 5, WallSegments.TopLeft)}, TR {map.WallRaise(7, 5, WallSegments.TopRight)}");
        Check("corner near a foundation", map.RaiseNear(7, 7, 0.25f) == H && map.RaiseNear(8, 8, 0.25f) == 0, $"{map.RaiseNear(7, 7, 0.25f)}, {map.RaiseNear(8, 8, 0.25f)}");

        // ---- which edges Sims can't cross
        Check("off the side", map.EdgeBlocked(6, 5, 1) && map.EdgeBlocked(7, 5, 3), "blocked both ways");
        Check("inside the foundation", !map.EdgeBlocked(5, 5, 1) && !map.EdgeBlocked(5, 5, 2), "open");
        Check("up the steps", !map.EdgeBlocked(5, 7, 0) && !map.EdgeBlocked(5, 6, 2), "open both ways");
        Check("onto the steps from the ground", !map.EdgeBlocked(5, 8, 0) && !map.EdgeBlocked(4, 7, 1), "open");
        Check("beside the steps", map.EdgeBlocked(4, 7, 0) && map.EdgeBlocked(6, 6, 2), "blocked");
        Check("open ground", !map.EdgeBlocked(1, 1, 1), "open");

        var obstacles = new List<VMObstacle>();
        FSO.SimAntics.Model.VMRoomMap.AddFoundationEdges(map, 6, 5, obstacles);
        Check("edge obstacle", obstacles.Count == 1 && obstacles[0].x1 == 6 * 16 + 13 && obstacles[0].x2 == 6 * 16 + 19, $"{obstacles.Count} obstacle(s)");
        obstacles.Clear();
        FSO.SimAntics.Model.VMRoomMap.AddFoundationEdges(map, 5, 6, obstacles);
        Check("no obstacle at the steps", obstacles.Count == 0, $"{obstacles.Count} obstacle(s)");
        obstacles.Clear();
        FSO.SimAntics.Model.VMRoomMap.AddFoundationEdges(map, 4, 4, obstacles);
        Check("corner tile: two edges", obstacles.Count == 2, $"{obstacles.Count} obstacle(s)");

        // ---- saving: foundations go at the very end of a save (version 39), so the terrain, walls, floors and
        // everything else are laid out as before and builds without foundations still read them
        var terrain = new VMArchitectureTerrain(12, 12);
        terrain.Foundations.CopyFrom(map);
        byte[] saved;
        using (var mem = new MemoryStream())
        {
            using (var w = new BinaryWriter(mem)) VMArchitectureTerrain.WriteFoundations(w, terrain.Foundations);
            saved = mem.ToArray();
        }
        FoundationMap back;
        using (var r = new BinaryReader(new MemoryStream(saved))) back = VMArchitectureTerrain.ReadFoundations(r);
        var loaded = new VMArchitectureTerrain(12, 12);
        loaded.SetFoundations(back);
        Check("saved and loaded", loaded.Foundations.Any && loaded.Foundations.Raise.SequenceEqual(map.Raise)
            && loaded.Foundations.Steps.SequenceEqual(map.Steps) && loaded.Foundations.Pattern.SequenceEqual(map.Pattern), $"{saved.Length} bytes");
        using (var mem = new MemoryStream())
        {
            using (var w = new BinaryWriter(mem)) VMArchitectureTerrain.WriteFoundations(w, new FoundationMap(12, 12));
            Check("none: one byte", mem.ToArray().Length == 1, $"{mem.ToArray().Length} bytes");
        }
        using (var mem = new MemoryStream())
        {
            using (var w = new BinaryWriter(mem)) terrain.SerializeInto(w);
            Check("terrain laid out as before", mem.ToArray().Length == 2 + 4 + 12 * 12 * 2 + 4 + 12 * 12, $"{mem.ToArray().Length} bytes");
        }
        var wrongSize = new VMArchitectureTerrain(10, 10);
        wrongSize.SetFoundations(back);
        Check("foundations for another size of lot are ignored", !wrongSize.Foundations.Any, "10x10 lot, 12x12 foundations");

        // ---- the drawn model: sides, bare tops, steps
        Blueprint bp = null;
        try
        {
            bp = new Blueprint(12, 12);
        }
        catch (Exception e)
        {
            Console.WriteLine("skip geometry: no blueprint without the game (" + e.GetType().Name + ")");
        }
        if (bp != null)
        {
            bp.Altitude = new short[12 * 12];
            bp.AltitudeCenters = new short[12 * 12];
            bp.Foundations = map;
            var model = new TestFoundation(bp, 7);
            var verts = model.Build();
            var top = H * bp.TerrainFactor * 3;
            var ys = verts.Select(v => v.Position.Y).ToList();
            Check("model made", verts.Length > 0, $"{verts.Length} vertices");
            Check("heights between the ground and the top", ys.All(y => y >= -1e-4f && y <= top + 1e-4f), $"0..{top}");
            // 9 bare tops + 11 sides (12 round the outside, less the one behind the steps) = 20 quads, and the steps:
            // 3 risers, 3 treads, 6 sides, 1 last riser = 13 quads; 4 vertices a quad
            Check("quads", verts.Length == (20 + 13) * 4, $"{verts.Length / 4} quads");
            var sides = verts.Where(v => Math.Abs(v.Normal.Y) < 0.5f).ToList();
            Check("sides on tile edges", sides.Where(v => Math.Abs(v.Normal.X) > 0.5f).All(v => Near(v.Position.X % 3, 0)), "x edges");
            var stepTop = verts.Where(v => v.Position.Z >= 7 * 3 - 1e-3f && v.Position.Z <= 8 * 3 + 1e-3f).Max(v => v.Position.Y);
            Check("steps reach the foundation", Near(stepTop, top), $"{stepTop} of {top}");
            Check("nothing on the open ground", verts.All(v => v.Position.X >= 4 * 3 - 1e-3f && v.Position.X <= 7 * 3 + 1e-3f && v.Position.Z >= 4 * 3 - 1e-3f && v.Position.Z <= 8 * 3 + 1e-3f), "inside x 4..7, y 4..8");

            bp.Floors[0][5 * 12 + 5].Pattern = 12; // floor laid on the middle: its top is the floor's
            verts = new TestFoundation(bp, 7).Build();
            Check("floored tile has no bare top", verts.Length == (19 + 13) * 4, $"{verts.Length / 4} quads");

            var heightAt = bp.InterpAltitude(new Vector3(5.5f, 7.5f, 0));
            Check("Sims walk up the steps", Near(heightAt, H / 2f * bp.TerrainFactor), $"{heightAt}");
            Check("walls take the foundation's side", Near(bp.GetWallAltitude(7, 5, WallSegments.TopLeft), H * bp.TerrainFactor), $"{bp.GetWallAltitude(7, 5, WallSegments.TopLeft)}");
            Check("a window outside a foundation's wall stands with it",
                Near(bp.WallObjectAltitude(new Vector3(7.5f, 5.5f, 0), WallSegments.TopLeft), H * bp.TerrainFactor)
                && Near(bp.WallObjectAltitude(new Vector3(7.5f, 5.5f, 0), WallSegments.BottomRight), 0), "TL raised, BR not");
        }

        Console.WriteLine(Failures == 0 ? "all passed" : $"{Failures} failed");
        return Failures == 0 ? 0 : 1;
    }
}

class TestFoundation : Modelled3DFoundation
{
    public TestFoundation(Blueprint bp, ushort pattern) : base(bp, pattern) { }
    public TerrainParallaxVertex[] Build()
    {
        GenerateGeometry();
        return Vertices;
    }
}
