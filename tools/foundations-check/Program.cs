using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FSO.LotView.Components;
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

// Foundations and decks (FreeSO's FSO.LotView.Model.FoundationMap and what uses it): heights under Sims and things,
// which tile edges Sims can't cross, building and pricing each kind, painting, steps, saving, and the models drawn.
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

    /// <summary>
    /// Sets the per-level walls or floors: plain arrays, or (with basements) a LevelArray with the basement too, so
    /// this check builds either way.
    /// </summary>
    static void SetLevels<T>(VMArchitecture arch, string name, T[] levels, T basement)
    {
        var field = typeof(VMArchitecture).GetField(name);
        if (field.FieldType.IsArray) field.SetValue(arch, levels);
        else field.SetValue(arch, field.FieldType.GetMethod("From").Invoke(null, new object[] { levels, basement }));
    }

    /// <summary>Just enough of a lot for the build tools: its size, walls and floors (none), terrain, no objects.</summary>
    static VMContext Fixture(FoundationMap map)
    {
        var ctx = (VMContext)RuntimeHelpers.GetUninitializedObject(typeof(VMContext));
        var arch = (VMArchitecture)RuntimeHelpers.GetUninitializedObject(typeof(VMArchitecture));
        arch.Width = 12;
        arch.Height = 12;
        arch.Stories = 5;
        arch.BuildableFloors = 2;
        arch.BuildableArea = new Rectangle(1, 1, 10, 10);
        SetLevels(arch, "Walls", Enumerable.Range(0, 5).Select(i => new WallTile[144]).ToArray(), new WallTile[144]);
        SetLevels(arch, "Floors", Enumerable.Range(0, 5).Select(i => new FloorTile[144]).ToArray(), new FloorTile[144]);
        arch.Terrain = new VMArchitectureTerrain(12, 12);
        arch.Terrain.Foundations.CopyFrom(map);
        ctx.Architecture = arch;
        arch.Context = ctx;
        ctx.ObjectQueries = new VMObjectQueries(ctx);
        return ctx;
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
        var ctx = Fixture(map);
        var arch = ctx.Architecture;
        var painted = arch.Terrain.Foundations;
        Check("paint one side", VMFoundationTools.PaintSides(arch, new Point(4, 5), 3, 9) == 1 && painted.SideOf(4, 5, 3) == 9
            && painted.SideOf(4, 5, 1) == 7 && painted.SideOf(4, 4, 3) == 7, "(4, 5)'s -x side only");
        Check("again: nothing changes", VMFoundationTools.PaintSides(arch, new Point(4, 5), 3, 9) == 0, "same covering");
        Check("a side inside the foundation can't be painted", VMFoundationTools.PaintSides(arch, new Point(5, 5), 0, 9) == 0, "(5, 5)");
        Check("nor the one the steps lead up", VMFoundationTools.PaintSides(arch, new Point(5, 6), 2, 9) == 0, "(5, 6)'s +y side");
        Check("nor the ground", VMFoundationTools.PaintSides(arch, new Point(1, 1), 0, 9) == 0, "(1, 1)");
        Check("groups: one per covering painted", Modelled3DFoundation.Groups(painted).Where(g => g.Item1 == FoundationMaterial.Covering)
            .Select(g => g.Item2).OrderBy(g => g).SequenceEqual(new ushort[] { 7, 9 }), "7, 9");
        var savedPainted = new MemoryStream();
        using (var w = new BinaryWriter(savedPainted, System.Text.Encoding.UTF8, true)) VMArchitectureTerrain.WriteFoundations(w, painted);
        savedPainted.Position = 0;
        using (var r = new BinaryReader(savedPainted)) Check("each side saved", VMArchitectureTerrain.ReadFoundations(r).Sides.SequenceEqual(painted.Sides), "revision " + VMArchitectureTerrain.FoundationsRevision);
        Check("paint the whole foundation (shift)", VMFoundationTools.PaintSides(arch, new Point(5, 5), VMFoundationTools.PaintWholeFoundation, 0) == 11
            && Enumerable.Range(0, 144 * 4).All(i => painted.Sides[i] == 0 || !painted.SideShows(i / 4 % 12, i / 4 / 12, i % 4)), "11 showing sides back to its brick");

        // ---- kinds: a foundation, a latticed deck, a column deck; each priced (and sold back) for its kind
        var kinds = Fixture(new FoundationMap(12, 12));
        var kmap = kinds.Architecture.Terrain.Foundations;
        Func<int, int, int, int, short, FoundationKind, int> place = (x, y, w, h, height, kind) =>
        {
            VMFoundationTools.PlaceFoundation(kinds.Architecture, new Rectangle(x, y, w, h), height, 0, kind, out var cost);
            return cost;
        };
        Check("a foundation: 3x3 at its price", place(2, 2, 2, 2, H, FoundationKind.Foundation) == 9 * VMFoundationTools.TilePrice(FoundationKind.Foundation)
            && kmap.KindOf(3, 3) == FoundationKind.Foundation, $"{9 * VMFoundationTools.TilePrice(FoundationKind.Foundation)}");
        var conv = place(2, 2, 2, 2, H, FoundationKind.LatticedDeck);
        Check("made a latticed deck: the new kind less the old's sellback", conv == 9 * (VMFoundationTools.TilePrice(FoundationKind.LatticedDeck) - VMFoundationTools.TileSellback(FoundationKind.Foundation))
            && kmap.KindOf(3, 3) == FoundationKind.LatticedDeck && kmap.RaiseOf(3, 3) == H, $"{conv}");
        Check("a deck's sides can't be painted", VMFoundationTools.PaintSides(kinds.Architecture, new Point(2, 3), 3, 9) == 0 && !kmap.SidePaintable(2, 3, 3) && kmap.SideShows(2, 3, 3), "(2, 3)'s -x side");
        Check("column decks are cheaper", VMFoundationTools.TilePrice(FoundationKind.ColumnDeck) < VMFoundationTools.TilePrice(FoundationKind.LatticedDeck)
            && VMFoundationTools.TilePrice(FoundationKind.LatticedDeck) < VMFoundationTools.TilePrice(FoundationKind.Foundation), "foundation > latticed > column");
        var sold = place(2, 2, 2, 2, 0, FoundationKind.Foundation);
        Check("removing it sells back the deck", sold == -9 * VMFoundationTools.TileSellback(FoundationKind.LatticedDeck) && !kmap.Any, $"{sold}");
        place(6, 2, 1, 0, H, FoundationKind.Foundation);
        place(8, 2, 0, 0, H, FoundationKind.ColumnDeck);
        var paintedSides = VMFoundationTools.PaintSides(kinds.Architecture, new Point(6, 2), VMFoundationTools.PaintWholeFoundation, 9);
        Check("painting stops where the foundation meets a deck", paintedSides == 5 && kmap.SideOf(8, 2, 0) == 0,
            $"{paintedSides} sides: the foundation's 2 tiles' 5 (the deck beside it is as high)");

        // ---- steps: a staircase's bottom piece (by its GUID), a quarter of its price; plain steps without one
        VMFoundationTools.PlaceFoundationSteps(kinds.Architecture, new Point(6, 3), 255, true, 0, out var plainCost);
        Check("plain steps", plainCost == VMArchitecture.FoundationStepsPrice && kmap.StepDirection(6, 3) == 0 && kmap.StepStyle[3 * 12 + 6] == 0, $"{plainCost}");
        VMFoundationTools.PlaceFoundationSteps(kinds.Architecture, new Point(6, 3), 255, true, 0x12345678, out var restyle);
        Check("steps can change to another staircase's", kmap.StepStyle[3 * 12 + 6] == 0x12345678
            && restyle == VMFoundationTools.StepsPrice(0x12345678) - VMFoundationTools.StepsSellback(0), $"{restyle}");
        VMFoundationTools.PlaceFoundationSteps(kinds.Architecture, new Point(6, 3), 255, false, 0, out var stepsBack);
        Check("removing steps sells them back", stepsBack == -VMFoundationTools.StepsSellback(0x12345678) && kmap.Steps[3 * 12 + 6] == 0 && kmap.StepStyle[3 * 12 + 6] == 0, $"{stepsBack}");
        Check("a staircase's bottom piece by name", FoundationLook.IsLowPiece("Stair - Low") && FoundationLook.IsLowPiece("Stairs - Low - White")
            && !FoundationLook.IsLowPiece("Stair - Low Pad") && !FoundationLook.IsLowPiece("Stair - Middle Low") && !FoundationLook.IsLowPiece("Stair - Hi"), "\"Low\", not its pad or the middle");
        Check("a staircase's top piece by name", FoundationLook.IsHighPiece("Stair - Hi") && !FoundationLook.IsHighPiece("Stair - Hi Pad")
            && !FoundationLook.IsHighPiece("Stair - Middle Hi") && !FoundationLook.IsHighPiece("Stair - Hi stub 1") && !FoundationLook.IsHighPiece("Stair - Low"), "\"Hi\", not its pad, stubs or the middle");
        var northStair = new FoundationLook.Stair { Rise = 0 };
        var eastStair = new FoundationLook.Stair { Rise = 1 };
        Check("a staircase rising north faces the way its steps rise", northStair.FacingFor(0) == Direction.NORTH && northStair.FacingFor(1) == Direction.EAST
            && northStair.FacingFor(2) == Direction.SOUTH && northStair.FacingFor(3) == Direction.WEST, "north, east, south, west");
        Check("one rising east is turned a quarter less", eastStair.FacingFor(1) == Direction.NORTH && eastStair.FacingFor(0) == Direction.WEST
            && eastStair.FacingFor(2) == Direction.EAST, "east steps: north; north steps: west");
        place(8, 2, 0, 0, H, FoundationKind.ColumnDeck);
        VMFoundationTools.PlaceFoundationSteps(kinds.Architecture, new Point(8, 3), 255, true, 0x12345678, out _);
        var kindsSaved = new MemoryStream();
        using (var w = new BinaryWriter(kindsSaved, System.Text.Encoding.UTF8, true)) VMArchitectureTerrain.WriteFoundations(w, kmap);
        kindsSaved.Position = 0;
        using (var r = new BinaryReader(kindsSaved))
        {
            var kback = VMArchitectureTerrain.ReadFoundations(r);
            Check("kinds and steps' staircases saved", kback.Kind.SequenceEqual(kmap.Kind) && kback.StepStyle.SequenceEqual(kmap.StepStyle)
                && kback.KindOf(8, 2) == FoundationKind.ColumnDeck && kback.StepStyle[3 * 12 + 8] == 0x12345678, "revision " + VMArchitectureTerrain.FoundationsRevision);
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
            var top = H * bp.TerrainFactor * 3;
            Check("groups: brick sides, plywood, plain steps", Modelled3DFoundation.Groups(map).OrderBy(g => g).SequenceEqual(new[] {
                (FoundationMaterial.Covering, (ushort)7), (FoundationMaterial.Plywood, (ushort)0), (FoundationMaterial.Steps, (ushort)0) }),
                string.Join(", ", Modelled3DFoundation.Groups(map).Select(g => g.Item1 + "/" + g.Item2)));

            // the covered sides: 12 round the outside, less the one behind the steps, from the ground right up to the top
            var sides = new TestFoundation(bp, FoundationMaterial.Covering, 7).Build();
            Check("covered sides", sides.Length == 11 * 4, $"{sides.Length / 4} quads");
            Check("covered sides go from the ground to the top", Near(sides.Min(v => v.Position.Y), 0) && Near(sides.Max(v => v.Position.Y), top), $"0..{top}");
            Check("covered sides on tile edges", sides.Where(v => Math.Abs(v.Normal.X) > 0.5f).All(v => Near(v.Position.X % 3, 0))
                && sides.Where(v => Math.Abs(v.Normal.Z) > 0.5f).All(v => Near(v.Position.Z % 3, 0)), "x and y edges");

            // plywood where no floor's laid: 9 tops
            var plywood = new TestFoundation(bp, FoundationMaterial.Plywood, 0).Build();
            Check("plywood tops", plywood.Length == 9 * 4, "9");
            Check("nothing past the foundation's own tiles", sides.Concat(plywood).All(v => v.Position.X >= 4 * 3 - 1e-3f && v.Position.X <= 7 * 3 + 1e-3f
                && v.Position.Z >= 4 * 3 - 1e-3f && v.Position.Z <= 7 * 3 + 1e-3f), "x 4..7, y 4..7");

            // plain steps (no staircase here): 3 risers, 3 treads, 6 sides, the last riser, up to the foundation
            var steps = new TestFoundation(bp, FoundationMaterial.Steps, 0).Build();
            Check("plain steps", steps.Length == 13 * 4, $"{steps.Length / 4} quads");
            Check("steps reach the foundation", new[] { 5, 6 }.All(x => steps.Any(v => Near(v.Position.X, x * 3) && Near(v.Position.Z, 7 * 3)
                && Near(v.Position.Y, top) && v.Normal.Z > 0.5f)), $"riser to {top}");

            bp.Floors[0][5 * 12 + 5].Pattern = 12; // floor laid on the middle: its top is the floor's
            Check("floored tile has no plywood", new TestFoundation(bp, FoundationMaterial.Plywood, 0).Build().Length == 8 * 4, "8");
            bp.Floors[0][5 * 12 + 5].Pattern = 0;

            // one side painted something else: the covered group loses it, and its own group has it
            map.Sides[map.SideIndex(4, 5, 3)] = 9;
            Check("a painted side is in its covering's group", new TestFoundation(bp, FoundationMaterial.Covering, 9).Build().Length == 4
                && new TestFoundation(bp, FoundationMaterial.Covering, 7).Build().Length == 10 * 4, "1 and 10 quads");
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

            // decks: a latticed deck (3x1) has planks, a board and lattice; a column deck (3x3) has planks, a board, an
            // underside and columns, one every other tile; all on their own tiles
            var decks = new FoundationMap(12, 12);
            for (int x = 2; x <= 4; x++) { decks.Raise[2 * 12 + x] = H; decks.Kind[2 * 12 + x] = (byte)FoundationKind.LatticedDeck; }
            for (int y = 5; y <= 7; y++) for (int x = 2; x <= 4; x++) { decks.Raise[y * 12 + x] = H; decks.Kind[y * 12 + x] = (byte)FoundationKind.ColumnDeck; }
            decks.Refresh();
            bp.Foundations = decks;
            Check("deck groups", Modelled3DFoundation.Groups(decks).OrderBy(g => g).SequenceEqual(new[] {
                (FoundationMaterial.Planks, (ushort)0), (FoundationMaterial.Lattice, (ushort)0), (FoundationMaterial.Column, (ushort)0) }),
                string.Join(", ", Modelled3DFoundation.Groups(decks).Select(g => g.Item1 + "/" + g.Item2)));
            Check("lattice on the latticed deck's 8 sides", new TestFoundation(bp, FoundationMaterial.Lattice, 0).Build().Length == 8 * 4, "8 quads");
            var columns = Modelled3DFoundation.ColumnTiles(decks);
            Check("columns every other tile", columns.OrderBy(i => i).SequenceEqual(new[] { 5 * 12 + 2, 5 * 12 + 4, 7 * 12 + 2, 7 * 12 + 4 }), string.Join(", ", columns.Select(i => $"({i % 12}, {i / 12})")));
            var colVerts = new TestFoundation(bp, FoundationMaterial.Column, 0).Build();
            var under = (H * bp.TerrainFactor - Modelled3DFoundation.FasciaHeight) * 3;
            Check("columns from the ground to the deck's underside", colVerts.Length > 0 && Near(colVerts.Min(v => v.Position.Y), 0) && Near(colVerts.Max(v => v.Position.Y), under), $"0..{under}");
            var planks = new TestFoundation(bp, FoundationMaterial.Planks, 0).Build();
            Check("planks: 12 tops, the column deck's 9 undersides, and boards", planks.Count(v => v.Normal.Y > 0.5f && Near(v.Position.Y, top)) >= 12 * 4
                && planks.Count(v => v.Normal.Y < -0.5f && Near(v.Position.Y, under)) >= 9 * 4, $"{planks.Length / 4} quads");
            Check("no lattice or columns where they don't belong", new TestFoundation(bp, FoundationMaterial.Covering, 0).Build().Length == 0
                && new TestFoundation(bp, FoundationMaterial.Plywood, 0).Build().Length == 0, "decks have no brick or plywood");
            var lattice = new TestFoundation(bp, FoundationMaterial.Lattice, 0).Build();
            Check("decks stay on their own tiles", planks.Concat(lattice).Concat(colVerts).All(v => v.Position.X >= 2 * 3 - 1e-3f && v.Position.X <= 5 * 3 + 1e-3f
                && v.Position.Z >= 2 * 3 - 1e-3f && v.Position.Z <= 8 * 3 + 1e-3f), "x 2..5, y 2..8");
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
    public TestFoundation(Blueprint bp, FoundationMaterial material, ushort pattern) : base(bp, material, pattern) { }
    public TerrainParallaxVertex[] Build()
    {
        GenerateGeometry();
        return Vertices;
    }
}
