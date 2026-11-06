using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FSO.LotView.Components.Geometry;
using FSO.LotView.Model;
using FSO.LotView.Utils;
using FSO.SimAntics.Model;
using FSO.SimAntics.Model.Routing;
using FSO.SimAntics.NetPlay.Model;
using FSO.SimAntics;
using FSO.SimAntics.Utils;
using System.Runtime.CompilerServices;
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
                map.SetAllSides(y * 12 + x, 7);
            }
        map.Steps[7 * 12 + 5] = 1; // direction 0: toward -y
        map.Refresh();
        return map;
    }

    /// <summary>The area (tiles) of the up-facing quads at a height (world units), each counted once.</summary>
    static float UpArea(TerrainParallaxVertex[] verts, float height)
    {
        float area = 0;
        for (int q = 0; q < verts.Length; q += 4)
        {
            if (verts[q].Normal.Y < 0.5f || !Enumerable.Range(q, 4).All(i => Near(verts[i].Position.Y, height))) continue;
            float quad = 0;
            for (int i = 0; i < 4; i++)
            {
                var a = verts[q + i].Position / 3;
                var b = verts[q + (i + 1) % 4].Position / 3;
                quad += a.X * b.Z - b.X * a.Z;
            }
            area += Math.Abs(quad) / 2;
        }
        return area;
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
            && loaded.Foundations.Steps.SequenceEqual(map.Steps) && loaded.Foundations.Sides.SequenceEqual(map.Sides), $"{saved.Length} bytes");
        using (var r = new BinaryReader(new MemoryStream(saved)))
            Check("older builds still see foundations", saved[0] == VMArchitectureTerrain.FoundationsRevision && r.ReadBoolean(), $"first byte {saved[0]}");
        // older saves had one covering per tile. Revision 1 (before the concrete look): the tool put a brick covering on
        // every foundation, so it goes; revision 2: it's on all four sides
        Func<byte, byte[]> perTileSave = (revision) =>
        {
            using (var mem = new MemoryStream())
            {
                using (var w = new BinaryWriter(mem))
                {
                    w.Write(revision);
                    w.Write(12); w.Write(12);
                    VMSerializableUtils.WriteArray(w, map.Raise);
                    w.Write(map.Steps);
                    VMSerializableUtils.WriteArray(w, Enumerable.Range(0, 144).Select(i => map.Raise[i] > 0 ? (ushort)7 : (ushort)0).ToArray());
                    w.Write(1234); // what comes after in the save
                }
                return mem.ToArray();
            }
        };
        foreach (var rev in new byte[] { 1, 2 })
        {
            using (var r = new BinaryReader(new MemoryStream(perTileSave(rev))))
            {
                var oldBack = VMArchitectureTerrain.ReadFoundations(r);
                var after = r.ReadInt32();
                var want = (rev == 1) ? 0 : 7;
                Check($"revision {rev} save", oldBack.Raise.SequenceEqual(map.Raise) && oldBack.Steps.SequenceEqual(map.Steps) && after == 1234
                    && Enumerable.Range(0, 144).All(i => Enumerable.Range(0, 4).All(d => oldBack.Sides[i * 4 + d] == (map.Raise[i] > 0 ? want : 0))),
                    rev == 1 ? "the tool's brick goes" : "its covering on all four sides");
            }
        }
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
        // ---- painting the sides, as walls are painted: one side, or every side of the foundation
        var arch = (VMArchitecture)RuntimeHelpers.GetUninitializedObject(typeof(VMArchitecture));
        arch.Terrain = new VMArchitectureTerrain(12, 12);
        arch.Terrain.Foundations.CopyFrom(map);
        var painted = arch.Terrain.Foundations;
        Check("paint one side", VMFoundationTools.PaintSides(arch, new Point(4, 5), 3, 9) == 1 && painted.SideOf(4, 5, 3) == 9
            && painted.SideOf(4, 5, 1) == 7 && painted.SideOf(4, 4, 3) == 7, "(4, 5)'s -x side only");
        Check("again: nothing changes", VMFoundationTools.PaintSides(arch, new Point(4, 5), 3, 9) == 0, "same covering");
        Check("a side inside the foundation can't be painted", VMFoundationTools.PaintSides(arch, new Point(5, 5), 0, 9) == 0, "(5, 5)");
        Check("nor the one the steps lead up", VMFoundationTools.PaintSides(arch, new Point(5, 6), 2, 9) == 0, "(5, 6)'s +y side");
        Check("nor the ground", VMFoundationTools.PaintSides(arch, new Point(1, 1), 0, 9) == 0, "(1, 1)");
        Check("groups: one per covering painted", Modelled3DFoundation.Groups(painted).OrderBy(g => g).SequenceEqual(new ushort[] { 0, 7, 9 }), "0, 7, 9");
        var savedPainted = new MemoryStream();
        using (var w = new BinaryWriter(savedPainted, System.Text.Encoding.UTF8, true)) VMArchitectureTerrain.WriteFoundations(w, painted);
        savedPainted.Position = 0;
        using (var r = new BinaryReader(savedPainted)) Check("each side saved", VMArchitectureTerrain.ReadFoundations(r).Sides.SequenceEqual(painted.Sides), "revision " + VMArchitectureTerrain.FoundationsRevision);
        Check("paint the whole foundation (shift)", VMFoundationTools.PaintSides(arch, new Point(5, 5), VMFoundationTools.PaintWholeFoundation, 0) == 11
            && Enumerable.Range(0, 144 * 4).All(i => painted.Sides[i] == 0 || !painted.SideShows(i / 4 % 12, i / 4 / 12, i % 4)), "11 showing sides back to plain");

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
            Check("groups: the concrete, and the covered sides", Modelled3DFoundation.Groups(map).OrderBy(p => p).SequenceEqual(new ushort[] { 0, 7 }), "0, 7");
            var top = H * bp.TerrainFactor * 3;
            var cap = Modelled3DFoundation.CapHeight * 3;
            const float O = Modelled3DFoundation.CapOverhang;

            // the covered sides: 12 round the outside, less the one behind the steps, from the ground up to the cap
            var sides = new TestFoundation(bp, 7).Build();
            Check("covered sides", sides.Length == 11 * 4, $"{sides.Length / 4} quads");
            Check("covered sides stop under the cap", sides.All(v => v.Position.Y >= -1e-4f && v.Position.Y <= top - cap + 1e-4f), $"0..{top - cap}");
            Check("covered sides on tile edges", sides.Where(v => Math.Abs(v.Normal.X) > 0.5f).All(v => Near(v.Position.X % 3, 0))
                && sides.Where(v => Math.Abs(v.Normal.Z) > 0.5f).All(v => Near(v.Position.Z % 3, 0)), "x and y edges");

            // the concrete: 9 bare tops; a cap on each of the 11 sides (face, top, underside); an end on the two caps
            // that stop at the steps; the steps (3 risers, 3 treads, 6 sides, 1 last riser)
            var verts = new TestFoundation(bp, 0).Build();
            var ys = verts.Select(v => v.Position.Y).ToList();
            Check("heights between the ground and the top", ys.All(y => y >= -1e-4f && y <= top + 1e-4f), $"0..{top}");
            Check("concrete quads", verts.Length == (9 + 11 * 3 + 2 + 13) * 4, $"{verts.Length / 4} quads");
            // the last riser (facing down the steps, +y) goes up to the foundation's edge, across the steps' tile
            Check("steps reach the foundation", new[] { 5, 6 }.All(x => verts.Any(v => Near(v.Position.X, x * 3) && Near(v.Position.Z, 7 * 3)
                && Near(v.Position.Y, top) && v.Normal.Z > 0.5f)), $"riser to {top}");
            Check("nothing on the open ground but the cap", verts.All(v => v.Position.X >= (4 - O) * 3 - 1e-3f && v.Position.X <= (7 + O) * 3 + 1e-3f
                && v.Position.Z >= (4 - O) * 3 - 1e-3f && v.Position.Z <= 8 * 3 + 1e-3f), $"inside x 4..7 and y 4..8, and {O} round");
            Check("caps meet round the corners", new[] { new Vector2(4 - O, 4 - O), new Vector2(7 + O, 4 - O), new Vector2(7 + O, 7 + O), new Vector2(4 - O, 7 + O) }
                .All(c => verts.Count(v => Near(v.Position.X, c.X * 3) && Near(v.Position.Z, c.Y * 3) && Near(v.Position.Y, top)) >= 2), "all four");
            // the caps' tops (all up-facing concrete above the bare tops' area) cover the band round the outside once:
            // 11 sides of the outline, and the four corner squares
            var capTops = UpArea(verts, top) - 9;
            Check("caps' tops cover the outline once", Near(capTops, 11 * O + 4 * O * O), $"{capTops} (want {11 * O + 4 * O * O})");

            bp.Floors[0][5 * 12 + 5].Pattern = 12; // floor laid on the middle: its top is the floor's
            verts = new TestFoundation(bp, 0).Build();
            Check("floored tile has no bare top", verts.Length == (8 + 11 * 3 + 2 + 13) * 4, $"{verts.Length / 4} quads");

            // one side painted something else: the covered group loses it, and its own group has it
            map.Sides[map.SideIndex(4, 5, 3)] = 9;
            Check("a painted side is in its covering's group", new TestFoundation(bp, 9).Build().Length == 4 && new TestFoundation(bp, 7).Build().Length == 10 * 4, "1 and 10 quads");
            map.Sides[map.SideIndex(4, 5, 3)] = 7;

            // picking the side to paint: a ray (world units, 3 a tile) at a third of a tile up, toward -y
            Point pt;
            int pd;
            var hitSide = bp.FoundationSideAt(new Ray(new Vector3(4.5f * 3, 0.3f * 3, 10 * 3), new Vector3(0, 0, -1)), out pt, out pd);
            Check("pick the side facing the camera", hitSide && pt == new Point(4, 6) && pd == 2, $"{pt}, side {pd}");
            hitSide = bp.FoundationSideAt(new Ray(new Vector3(1 * 3, 0.3f * 3, 5.5f * 3), new Vector3(1, 0, 0)), out pt, out pd);
            Check("from the other way", hitSide && pt == new Point(4, 5) && pd == 3, $"{pt}, side {pd}");
            Check("not behind the steps", !bp.FoundationSideAt(new Ray(new Vector3(5.5f * 3, 0.3f * 3, 10 * 3), new Vector3(0, 0, -1)), out pt, out pd), "nothing");
            Check("not from above (the top hides them)", !bp.FoundationSideAt(new Ray(new Vector3(5.5f * 3, 5 * 3, 5.5f * 3), new Vector3(0, -1, 0)), out pt, out pd), "nothing");
            Check("not over the top", !bp.FoundationSideAt(new Ray(new Vector3(4.5f * 3, 1 * 3, 10 * 3), new Vector3(0, 0, -1)), out pt, out pd), "nothing");
            var slant = Vector3.Normalize(new Vector3(0, -1, -1)); // down at 45 degrees onto the +y side, then the top behind it
            Check("a slanting ray meets the side before the top", bp.FoundationSideAt(new Ray(new Vector3(6.5f * 3, 1.25f * 3, 7.6f * 3), slant), out pt, out pd)
                && pt == new Point(6, 6) && pd == 2, $"{pt}, side {pd}");

            // an L: x 4..6 on y 4, and x 6 on y 5..6: one inside corner, at (6, 5)
            var l = new FoundationMap(12, 12);
            foreach (var t in new[] { new Point(4, 4), new Point(5, 4), new Point(6, 4), new Point(6, 5), new Point(6, 6) })
                l.Raise[t.Y * 12 + t.X] = FoundationMap.DefaultHeight;
            l.Refresh();
            bp.Foundations = l;
            bp.Floors[0][5 * 12 + 5].Pattern = 0;
            verts = new TestFoundation(bp, 0).Build();
            Check("inside corner", Modelled3DFoundation.EndOf(l, 5, 4, 2, false) == Modelled3DFoundation.CapEnd.Concave
                && Modelled3DFoundation.EndOf(l, 6, 5, 3, true) == Modelled3DFoundation.CapEnd.Concave, "both caps see it");
            // the outline is 12 sides long, with 5 outside corners and 1 inside one
            var lTops = UpArea(verts, top) - 5;
            Check("caps' tops cover an L's outline once", Near(lTops, 12 * O + 5 * O * O - O * O), $"{lTops} (want {12 * O + 4 * O * O})");
            bp.Foundations = map;

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
