using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using FSO.LotView.Components.Geometry;
using FSO.LotView.Model;
using FSO.LotView.Utils;
using FSO.SimAntics;
using FSO.SimAntics.Marshals;
using FSO.SimAntics.Model;
using FSO.SimAntics.Model.Routing;
using Microsoft.Xna.Framework;

// The basement (level 0, see FreeSO's FSO.LotView.Model.BasementMap and LevelArray): the per-level arrays, which tiles are
// dug out, the rooms on level 0, saving, and the model drawn for the earth's sides.
static class Program
{
    static int Failures;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: {detail}");
        if (!ok) Failures++;
    }

    const int W = 12, H = 12;

    // dug out: tiles x 3..6, y 3..6 (a 4x4 basement)
    static BasementMap Sample()
    {
        var map = new BasementMap(W, H);
        for (int y = 3; y <= 6; y++)
            for (int x = 3; x <= 6; x++)
            {
                map.Dug[y * W + x] = 1;
                map.Pattern[y * W + x] = 7;
            }
        map.Refresh();
        return map;
    }

    static VMContext Context(BasementMap map)
    {
        //just enough of a context for the room maps and bounds: the lot's size and terrain (with the basement), no
        //objects (so no doors)
        var ctx = (VMContext)RuntimeHelpers.GetUninitializedObject(typeof(VMContext));
        var arch = (VMArchitecture)RuntimeHelpers.GetUninitializedObject(typeof(VMArchitecture));
        arch.Width = W;
        arch.Height = H;
        arch.Stories = 5;
        arch.BuildableFloors = 2;
        arch.BuildableArea = new Rectangle(1, 1, W - 2, H - 2);
        arch.Terrain = new VMArchitectureTerrain(W, H);
        arch.Terrain.SetBasement(map);
        ctx.Architecture = arch;
        arch.Context = ctx;
        ctx.ObjectQueries = new VMObjectQueries(ctx);
        return ctx;
    }

    static int Main()
    {
        // ---- per-level arrays: by level - 1 as before, with the basement at -1
        var levels = new LevelArray<int[]>(5);
        for (int i = -1; i < 5; i++) levels[i] = new[] { i + 1 };
        Check("the ground floor is [0]", levels[0][0] == 1 && levels[4][0] == 5, $"[0] level {levels[0][0]}, [4] level {levels[4][0]}");
        Check("the basement is [-1]", levels[-1][0] == 0 && levels.Basement[0] == 0, $"level {levels[-1][0]}");
        Check("Length as before", levels.Length == 5, $"{levels.Length}");
        Check("enumerates the ground floor and up", levels.Select(x => x[0]).SequenceEqual(new[] { 1, 2, 3, 4, 5 }), string.Join(",", levels.Select(x => x[0])));
        var plain = levels.ToArray();
        var back = LevelArray<int[]>.From(plain, new[] { 0 });
        Check("to a plain array and back", plain.Length == 5 && back[2][0] == 3 && back[-1][0] == 0, $"{plain.Length} levels");

        // ---- which tiles are dug
        var map = Sample();
        Check("dug out", map.IsDug(3, 3) && map.IsDug(6, 6) && !map.IsDug(2, 3) && !map.IsDug(7, 6), "3..6 dug, 2 and 7 not");
        Check("off the lot isn't dug", !map.IsDug(-1, 0) && !map.IsDug(W, 0), "");
        Check("the earth's sides", map.EarthSide(3, 4, 3) && map.EarthSide(6, 4, 1) && !map.EarthSide(4, 4, 1) && !map.EarthSide(2, 4, 1), "-x of 3, +x of 6; none inside or on earth");
        Check("walls along the basement's edge", map.SegmentDug(7, 4, WallSegments.TopLeft) && map.SegmentDug(4, 7, WallSegments.TopRight)
            && !map.SegmentDug(8, 4, WallSegments.TopLeft) && !map.SegmentDug(7, 7, WallSegments.TopLeft), "beside a dug tile: yes; between earth: no");

        // ---- rooms: the ground floor (all grass) is made first, the basement last, as VMArchitecture.RegenRoomMap does
        var ctx = Context(map);

        // ---- where things can be on level 0: only dug out tiles
        Check("in bounds on a dug tile", !ctx.IsOutOfBounds(LotTilePos.FromBigTile(4, 4, 0)), "level 0, (4, 4)");
        Check("out of bounds in the earth", ctx.IsOutOfBounds(LotTilePos.FromBigTile(2, 4, 0)) && ctx.IsOutOfBounds(LotTilePos.FromBigTile(8, 8, 0)), "level 0, (2, 4) and (8, 8)");
        Check("the ground floor as before", !ctx.IsOutOfBounds(LotTilePos.FromBigTile(2, 4, 1)) && ctx.IsOutOfBounds(LotTilePos.FromBigTile(2, 4, -1)), "level 1 in, level -1 out");
        Check("user placement too", !ctx.IsUserOutOfBounds(LotTilePos.FromBigTile(4, 4, 0)) && ctx.IsUserOutOfBounds(LotTilePos.FromBigTile(2, 4, 0)), "dug yes, earth no");
        Check("build tools: the basement and its edges", !ctx.Architecture.OutsideClip(4, 4, 0) && !ctx.Architecture.OutsideClip(7, 4, 0)
            && ctx.Architecture.OutsideClip(8, 4, 0) && ctx.Architecture.OutsideClip(4, 4, -1), "dug, next to dug: yes; earth, level -1: no");
        var rooms = new List<VMRoom> { new VMRoom() };
        var groundWalls = new WallTile[W * H];
        var groundFloors = new FloorTile[W * H];
        for (int i = 0; i < W * H; i++) if (map.Dug[i] != 0) groundFloors[i].Pattern = 5; //the basement's ceiling, laid when it's dug
        var ground = new VMRoomMap();
        ground.GenerateMap(groundWalls, groundFloors, W, H, rooms, 0, ctx);
        var groundRooms = rooms.Count;
        Check("the ground floor: one outside room (floored over the basement)", groundRooms == 2 && rooms[1].IsOutside, $"{groundRooms - 1} room(s)");

        var walls = new WallTile[W * H];
        var floors = new FloorTile[W * H];
        for (int i = 0; i < W * H; i++) if (map.Dug[i] != 0) floors[i].Pattern = 5; //dug tiles always have a floor
        var basement = new VMRoomMap();
        basement.GenerateMap(walls, floors, W, H, rooms, -1, ctx, basement: true);
        var earthRoom = (ushort)basement.Map[0];
        var dugRoom = (ushort)basement.Map[4 * W + 4];
        Check("the earth is a room of its own", earthRoom != dugRoom && earthRoom >= groundRooms, $"earth {earthRoom}, basement {dugRoom}");
        Check("one basement room", Enumerable.Range(3, 4).All(y => Enumerable.Range(3, 4).All(x => (ushort)basement.Map[y * W + x] == dugRoom)), "all 16 tiles");
        Check("the basement isn't outside", !rooms[dugRoom].IsOutside, $"outside: {rooms[dugRoom].IsOutside}");
        Check("the earth isn't next to it", !rooms[dugRoom].AdjRooms.Contains(earthRoom) && !rooms[earthRoom].AdjRooms.Contains(dugRoom), "no adjacency either way");
        Check("lit on its own", rooms[dugRoom].LightBaseRoom == dugRoom, $"light base {rooms[dugRoom].LightBaseRoom}");
        Check("its floor is -1", rooms[dugRoom].Floor == -1, $"{rooms[dugRoom].Floor}");
        ctx.Architecture.Rooms = new LevelArray<VMRoomMap>(5);
        ctx.Architecture.Rooms[-1] = basement;
        ctx.Architecture.Rooms[0] = ground;
        Check("things in the basement are in its room", ctx.GetRoomAt(LotTilePos.FromBigTile(4, 4, 0)) == dugRoom,
            $"room {ctx.GetRoomAt(LotTilePos.FromBigTile(4, 4, 0))} (0 would hide them and let Sims walk anywhere)");
        var obs = rooms[dugRoom].RoomObs;
        Func<int, int, bool> blocked = (x, y) => obs.Any(o => o.x1 < x * 16 + 8 && o.x2 > x * 16 + 8 && o.y1 < y * 16 + 8 && o.y2 > y * 16 + 8);
        Check("Sims can't walk into the earth", blocked(2, 4) && blocked(7, 4) && blocked(4, 2) && blocked(4, 7), "the tiles round it are obstacles");
        Check("but can walk the basement", !blocked(3, 3) && !blocked(6, 6) && !blocked(4, 5), "its tiles are open");

        // the ground floor over the basement: all floored (its ceiling) but one tile taken up for a stairwell
        var rooms3 = new List<VMRoom> { new VMRoom() };
        var overFloors = new FloorTile[W * H];
        for (int i = 0; i < W * H; i++) if (map.Dug[i] != 0) overFloors[i].Pattern = 5;
        overFloors[4 * W + 4].Pattern = 0;
        var over = new VMRoomMap();
        over.GenerateMap(groundWalls, overFloors, W, H, rooms3, 0, ctx);
        var yard = (ushort)over.Map[0];
        var ceiling = (ushort)over.Map[3 * W + 3];
        var well = (ushort)over.Map[4 * W + 4];
        Check("a stairwell over the basement is air of its own", well != yard && well != ceiling, $"yard {yard}, ceiling {ceiling}, stairwell {well}");
        Check("Sims on the ground floor can't walk into it", rooms3[ceiling].RoomObs.Any(o => o.x1 < 4 * 16 + 8 && o.x2 > 4 * 16 + 8 && o.y1 < 4 * 16 + 8 && o.y2 > 4 * 16 + 8), "an obstacle over it");
        Check("the yard is still ground", (ushort)over.Map[1 * W + 9] == yard && rooms3[yard].IsOutside, "");

        // a wall across the middle (x = 5, the top left edge of the tiles there) makes two rooms
        var rooms2 = new List<VMRoom> { new VMRoom() };
        new VMRoomMap().GenerateMap(groundWalls, groundFloors, W, H, rooms2, 0, ctx);
        var walls2 = new WallTile[W * H];
        for (int y = 3; y <= 6; y++) walls2[y * W + 5] = new WallTile { Segments = WallSegments.TopLeft, TopLeftStyle = 1 };
        var split = new VMRoomMap();
        split.GenerateMap(walls2, floors, W, H, rooms2, -1, ctx, basement: true);
        var left = (ushort)split.Map[4 * W + 3];
        var right = (ushort)split.Map[4 * W + 6];
        Check("a wall splits it in two", left != right, $"rooms {left} and {right}");
        Check("both indoors", !rooms2[left].IsOutside && !rooms2[right].IsOutside, $"{rooms2[left].IsOutside}, {rooms2[right].IsOutside}");

        // ---- digging out and filling in (the build mode command), on a lot with an empty basement
        var digCtx = Context(new BasementMap(W, H));
        var arch = digCtx.Architecture;
        arch.Walls = new LevelArray<WallTile[]>(5);
        arch.Floors = new LevelArray<FloorTile[]>(5);
        arch.WallsAt = new LevelArray<List<int>>(5);
        for (int i = -1; i < 5; i++) { arch.Walls[i] = new WallTile[W * H]; arch.Floors[i] = new FloorTile[W * H]; arch.WallsAt[i] = new List<int>(); }
        arch.RealMode = true;
        arch.Floors[0][3 * W + 3].Pattern = 9; //a floor already laid above one corner
        var dugCount = FSO.SimAntics.Utils.VMBasementTools.Dig(arch, new Rectangle(3, 3, 2, 1), true, 5, 7);
        Check("dig: a 3x2 basement", dugCount == 6 && arch.Basement.IsDug(3, 3) && arch.Basement.IsDug(5, 4) && !arch.Basement.IsDug(6, 4), $"{dugCount} tiles");
        Check("dug tiles get a floor", arch.Floors[-1][4 * W + 4].Pattern == 5 && arch.Floors[-1][2 * W + 2].Pattern == 0, "5 in it, none outside");
        Check("and the tiles above, unless they had one", arch.Floors[0][4 * W + 4].Pattern == 5 && arch.Floors[0][3 * W + 3].Pattern == 9, "5 above, the floor that was there kept");
        Check("the earth's sides get the covering", arch.Basement.Pattern[4 * W + 4] == 7, $"{arch.Basement.Pattern[4 * W + 4]}");
        Check("digging it again: nothing to do", FSO.SimAntics.Utils.VMBasementTools.Dig(arch, new Rectangle(3, 3, 2, 1), true, 5, 7) == 0, "");
        Check("not off the buildable area", FSO.SimAntics.Utils.VMBasementTools.Dig(arch, new Rectangle(0, 3, 1, 0), true, 5, 7) == 0
            && arch.LastFailReason == FSO.SimAntics.Utils.VMBasementTools.FailBlocked && !arch.Basement.IsDug(1, 3), "the lot's edge");
        arch.Terrain.Foundations.Raise[8 * W + 8] = FoundationMap.DefaultHeight;
        arch.Terrain.Foundations.Refresh();
        Check("not under a foundation (yet)", FSO.SimAntics.Utils.VMBasementTools.Dig(arch, new Rectangle(8, 8, 0, 0), true, 5, 7) == 0, "");
        //walls: one inside, one on the basement's edge with the earth, one inside the part that's filled in
        arch.SetWall(5, 4, 0, new WallTile { Segments = WallSegments.TopLeft, TopLeftStyle = 1 }); //between (4,4) and (5,4): stays
        arch.SetWall(6, 3, 0, new WallTile { Segments = WallSegments.TopLeft, TopLeftStyle = 1 }); //the +x edge, against earth at (6,3)
        var filled = FSO.SimAntics.Utils.VMBasementTools.Dig(arch, new Rectangle(5, 3, 0, 1), false, 5, 7);
        Check("fill in: a column of 2", filled == -2 && !arch.Basement.IsDug(5, 3) && arch.Basement.IsDug(4, 3), $"{filled}");
        Check("its floor goes, the floor above stays", arch.Floors[-1][3 * W + 5].Pattern == 0 && arch.Floors[0][3 * W + 5].Pattern == 5, "");
        Check("a wall between the basement and the filled earth stays", (arch.GetWall(5, 4, 0).Segments & WallSegments.TopLeft) != 0, "the new edge");
        Check("a wall left with earth both sides goes", arch.GetWall(6, 3, 0).Segments == 0, "");

        // ---- saving: at the very end of a save (version 40), after the foundations
        var savedWalls = new WallTile[W * H];
        savedWalls[4 * W + 4] = new WallTile { Segments = WallSegments.TopLeft, TopLeftStyle = 1, TopLeftPattern = 3 };
        byte[] saved;
        using (var mem = new MemoryStream())
        {
            using (var w = new BinaryWriter(mem)) VMArchitectureMarshal.WriteBasement(w, map, savedWalls, floors);
            saved = mem.ToArray();
        }
        var marshal = new VMArchitectureMarshal();
        using (var r = new BinaryReader(new MemoryStream(saved))) marshal.ReadBasement(r);
        Check("saved and loaded", marshal.Basement != null && marshal.Basement.Any && marshal.Basement.Dug.SequenceEqual(map.Dug)
            && marshal.Basement.Pattern.SequenceEqual(map.Pattern), $"{saved.Length} bytes");
        Check("its walls and floors too", marshal.BasementWalls[4 * W + 4].Segments == WallSegments.TopLeft && marshal.BasementWalls[4 * W + 4].TopLeftPattern == 3
            && marshal.BasementFloors[4 * W + 4].Pattern == 5 && marshal.BasementFloors[0].Pattern == 0, "a wall, a floor");
        using (var mem = new MemoryStream())
        {
            using (var w = new BinaryWriter(mem)) VMArchitectureMarshal.WriteBasement(w, new BasementMap(W, H), new WallTile[W * H], new FloorTile[W * H]);
            Check("none: one byte", mem.ToArray().Length == 1, $"{mem.ToArray().Length} bytes");
        }
        var terrain = new VMArchitectureTerrain(W, H);
        terrain.SetBasement(marshal.Basement);
        Check("the terrain takes it", terrain.Basement.Any && terrain.Basement.IsDug(4, 4), "");
        var other = new VMArchitectureTerrain(10, 10);
        other.SetBasement(marshal.Basement);
        Check("a basement for another size of lot is ignored", !other.Basement.Any, "10x10 lot, 12x12 basement");
        Check("save version 40", VMMarshal.LATEST_VERSION == 40, $"{VMMarshal.LATEST_VERSION}");

        // ---- the drawn model: the earth's sides
        Blueprint bp = null;
        try
        {
            bp = new Blueprint(W, H);
        }
        catch (Exception e)
        {
            Console.WriteLine("skip geometry: no blueprint without the game (" + e.GetType().Name + ")");
        }
        if (bp != null)
        {
            bp.Altitude = new short[W * H];
            bp.AltitudeCenters = new short[W * H];
            bp.Basement = map;
            var verts = new TestBasement(bp, 7).Build();
            // 4x4 dug: 16 edges against the earth, a quad each
            Check("the earth's sides", verts.Length == 16 * 4, $"{verts.Length / 4} quads");
            var ys = verts.Select(v => v.Position.Y).ToList();
            Check("from the basement floor up a storey", Math.Abs(ys.Min()) < 1e-4f && Math.Abs(ys.Max() - 2.95f * 3) < 1e-3f, $"{ys.Min()}..{ys.Max()}");
            Check("on the basement's edge", verts.All(v => v.Position.X >= 3 * 3 - 1e-3f && v.Position.X <= 7 * 3 + 1e-3f && v.Position.Z >= 3 * 3 - 1e-3f && v.Position.Z <= 7 * 3 + 1e-3f), "x, y 3..7");
            var inward = verts.Where(v => Math.Abs(v.Position.X - 3 * 3) < 1e-3f && Math.Abs(v.Normal.X) > 0.5f).All(v => v.Normal.X > 0);
            Check("facing into the basement", inward, "the -x side faces +x");
            Check("other covering: none", new TestBasement(bp, 8).Build().Length == 0, "");
            Check("level 0 is there to build on", bp.Walls[-1] != null && bp.Floors[-1] != null && bp.Walls[-1].Length == W * H, $"{bp.Walls[-1]?.Length} tiles");
        }

        Console.WriteLine(Failures == 0 ? "all passed" : $"{Failures} failed");
        return Failures == 0 ? 0 : 1;
    }
}

class TestBasement : Modelled3DBasement
{
    public TestBasement(Blueprint bp, ushort pattern) : base(bp, pattern) { }
    public TerrainParallaxVertex[] Build()
    {
        GenerateGeometry();
        return Vertices;
    }
}
