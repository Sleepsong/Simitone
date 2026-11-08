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

        // ---- cutaway in the basement: with no walls built, its earth's sides cut away as walls would
        {
            var carch = ctx.Architecture;
            carch.Walls = new LevelArray<WallTile[]>(5);
            carch.Walls[-1] = walls;
            carch.Walls[0] = groundWalls;
            var edges = FSO.SimAntics.Utils.VMArchitectureTools.EarthEdges(carch, 0);
            Check("the earth's edges", edges != null && edges[4 * W + 3] && edges[4 * W + 2] && edges[6 * W + 6] && edges[7 * W + 7]
                && !edges[4 * W + 4] && !edges[5 * W + 5] && !edges[4 * W + 1], "dug next to earth and earth next to dug; not inside, or far out");
            Check("only in the basement", FSO.SimAntics.Utils.VMArchitectureTools.EarthEdges(carch, 1) == null, "");
            var rot = FSO.LotView.WorldRotation.TopLeft;
            var cut = FSO.SimAntics.Utils.VMArchitectureTools.GenerateRoomCut(carch, 0, rot, new HashSet<uint> { dugRoom });
            Check("cutting the basement's room cuts at its edges", cut.Count(x => x) > 0 && cut[4 * W + 3] && cut[4 * W + 6], $"{cut.Count(x => x)} tiles cut");
            //the sides: whole with no cut; with it, those toward the camera come down and those away from it stay (as walls)
            var dir = FSO.LotView.RC.WallComponentRC.CutCheckDir[(int)rot]; //from a cut wall, toward the room it's cut for
            var nearX = dir.X < 0 ? 7 : 3; //the side the camera looks over
            var farX = dir.X < 0 ? 3 : 7;
            Func<int, float> sideHeight = (x) => Modelled3DBasement.SideHeight(W, cut, rot, new Point(x, 5));
            Check("no cut: sides whole", Modelled3DBasement.SideHeight(W, null, rot, new Point(nearX, 5)) > 2.9f, "");
            Check("the near side comes down", sideHeight(nearX) < 0.5f, $"x {nearX}: {sideHeight(nearX)}");
            Check("the far side stays", sideHeight(farX) > 2.9f, $"x {farX}: {sideHeight(farX)}");
            var rect = new Rectangle(8, 8, 2, 2); //(away from the basement, and off the lot's first row and column, which always count)
            var cut2 = new bool[W * H];
            Check("cutting a rectangle out in the earth: nothing to cut", !FSO.SimAntics.Utils.VMArchitectureTools.ApplyCutRectangle(carch, 0, cut2, rect), "");
            Check("and at the basement's edge, its sides", FSO.SimAntics.Utils.VMArchitectureTools.ApplyCutRectangle(carch, 0, cut2, new Rectangle(2, 2, 2, 2)), "");
        }

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
        //pools: in the basement too (not over it); taking one up brings the basement's floor back
        Check("a pool in the basement", arch.SetFloor(4, 4, 0, new FloorTile { Pattern = 65535 }, false) && arch.Floors[-1][4 * W + 4].Pattern == 65535, $"{arch.Floors[-1][4 * W + 4].Pattern}");
        Check("not over it", !arch.SetFloor(4, 3, 1, new FloorTile { Pattern = 65535 }, false), "");
        Check("not in the earth", !arch.SetFloor(8, 8, 0, new FloorTile { Pattern = 65535 }, false), "");
        Check("taken up: the floor round it comes back", arch.SetFloor(4, 4, 0, new FloorTile { Pattern = 0 }, false) && arch.Floors[-1][4 * W + 4].Pattern == 5, $"{arch.Floors[-1][4 * W + 4].Pattern}");
        Check("a basement floor can't be taken up", !arch.SetFloor(4, 4, 0, new FloorTile { Pattern = 0 }, false) && arch.Floors[-1][4 * W + 4].Pattern == 5, "");
        Check("digging it again: nothing to do", FSO.SimAntics.Utils.VMBasementTools.Dig(arch, new Rectangle(3, 3, 2, 1), true, 5, 7) == 0, "");
        Check("not off the buildable area", FSO.SimAntics.Utils.VMBasementTools.Dig(arch, new Rectangle(0, 3, 1, 0), true, 5, 7) == 0
            && arch.LastFailReason == FSO.SimAntics.Utils.VMBasementTools.FailBlocked && !arch.Basement.IsDug(1, 3), "the lot's edge");
        //under a foundation: its floor's a storey under the foundation's top, so a basement is all under one, or not at all
        {
            var fmap = arch.Terrain.Foundations;
            for (int y = 8; y <= 9; y++) for (int x = 8; x <= 9; x++) { fmap.Raise[y * W + x] = FoundationMap.DefaultHeight; fmap.Kind[y * W + x] = (byte)FoundationKind.Foundation; }
            fmap.Refresh();
            Check("under a foundation", FSO.SimAntics.Utils.VMBasementTools.Dig(arch, new Rectangle(8, 8, 1, 0), true, 5, 7) == 2 && arch.Basement.IsDug(9, 8), "(8,8) and (9,8)");
            Check("not partly under it", FSO.SimAntics.Utils.VMBasementTools.Dig(arch, new Rectangle(8, 9, 0, 1), true, 5, 7) == 0
                && arch.LastFailReason == FSO.SimAntics.Utils.VMBasementTools.FailUneven && !arch.Basement.IsDug(8, 9), "(8,10) isn't under it");
            Check("nor out from under it", FSO.SimAntics.Utils.VMBasementTools.Dig(arch, new Rectangle(10, 8, 0, 0), true, 5, 7) == 0
                && arch.LastFailReason == FSO.SimAntics.Utils.VMBasementTools.FailUneven, "(10,8), beside (9,8)");
            fmap.Kind[9 * W + 9] = (byte)FoundationKind.ColumnDeck;
            Check("not under a deck", FSO.SimAntics.Utils.VMBasementTools.Dig(arch, new Rectangle(9, 9, 0, 0), true, 5, 7) == 0
                && arch.LastFailReason == FSO.SimAntics.Utils.VMBasementTools.FailBlocked, "it's open underneath");
            fmap.Kind[9 * W + 9] = (byte)FoundationKind.Foundation;
            Check("filled in from under it", FSO.SimAntics.Utils.VMBasementTools.Dig(arch, new Rectangle(8, 8, 1, 0), false, 5, 7) == -2, "");
            for (int y = 8; y <= 9; y++) for (int x = 8; x <= 9; x++) { fmap.Raise[y * W + x] = 0; fmap.Kind[y * W + x] = 0; }
            fmap.Refresh();
            //and a foundation over a basement (3..5, 3..4): over all of it, not part
            Check("a foundation over part of a basement: no", FSO.SimAntics.Utils.VMFoundationTools.PlaceFoundation(arch, new Rectangle(3, 3, 0, 0), FoundationMap.DefaultHeight, 0, FoundationKind.Foundation, out _) == 0
                && arch.LastFailReason == FSO.SimAntics.Utils.VMBasementTools.FailUneven, "");
            Check("a deck over it: no", FSO.SimAntics.Utils.VMFoundationTools.PlaceFoundation(arch, new Rectangle(3, 3, 2, 1), FoundationMap.DefaultHeight, 0, FoundationKind.LatticedDeck, out _) == 0, "");
            Check("over all of it: yes", FSO.SimAntics.Utils.VMFoundationTools.PlaceFoundation(arch, new Rectangle(2, 2, 4, 3), FoundationMap.DefaultHeight, 0, FoundationKind.Foundation, out _) > 0
                && fmap.RaiseOf(4, 4) == FoundationMap.DefaultHeight, "and round it");
            Check("taken off part of it: no", FSO.SimAntics.Utils.VMFoundationTools.PlaceFoundation(arch, new Rectangle(5, 3, 0, 1), 0, 0, FoundationKind.Foundation, out _) == 0
                && arch.LastFailReason == FSO.SimAntics.Utils.VMBasementTools.FailUneven, "");
            Check("turned into a deck: no", FSO.SimAntics.Utils.VMFoundationTools.PlaceFoundation(arch, new Rectangle(3, 3, 0, 0), FoundationMap.DefaultHeight, 0, FoundationKind.ColumnDeck, out _) == 0, "");
            Check("taken off all of it", FSO.SimAntics.Utils.VMFoundationTools.PlaceFoundation(arch, new Rectangle(2, 2, 4, 3), 0, 0, FoundationKind.Foundation, out _) < 0 //(negative: taken off)
                && fmap.RaiseOf(4, 4) == 0, "");
            //basement windows: on its edge, where a foundation lifts it above the ground outside, and high in the wall
            {
                var W2 = FSO.SimAntics.Utils.VMBasementWindows.StoreyTiles;
                Check("no foundation: none of the wall's above ground", FSO.SimAntics.Utils.VMBasementWindows.AboveGround(arch, 3, 3, 3) == 0, "");
                FSO.SimAntics.Utils.VMFoundationTools.PlaceFoundation(arch, new Rectangle(3, 3, 2, 1), FoundationMap.DefaultHeight, 0, FoundationKind.Foundation, out _);
                var band = FoundationMap.DefaultHeight * FSO.SimAntics.Utils.VMBasementWindows.HeightToTiles / W2;
                var edge = FSO.SimAntics.Utils.VMBasementWindows.AboveGround(arch, 3, 3, 3); //(3,3)'s -x side, against (2,3) outside
                Check("under a foundation: its top's above ground", Math.Abs(edge - band) < 1e-4f && band > 0.2f && band < 0.3f, $"{edge:0.000} of the wall");
                Check("not between two of its tiles", FSO.SimAntics.Utils.VMBasementWindows.AboveGround(arch, 3, 3, 1) == 0, "(3,3)'s +x side");
                Check("from the outside too", Math.Abs(FSO.SimAntics.Utils.VMBasementWindows.AboveGround(arch, 2, 3, 1) - band) < 1e-4f, "(2,3)'s +x side");
                FSO.SimAntics.Utils.VMFoundationTools.PlaceFoundation(arch, new Rectangle(3, 3, 2, 1), 0, 0, FoundationKind.Foundation, out _);
                //how far down a window's opening goes, from the hole its wall style cuts (a wall 40 wide and 100 high here)
                Func<int, int, FSO.Files.Formats.IFF.Chunks.SPRFrame> mask = (holeTop, holeBottom) =>
                {
                    var f = (FSO.Files.Formats.IFF.Chunks.SPRFrame)RuntimeHelpers.GetUninitializedObject(typeof(FSO.Files.Formats.IFF.Chunks.SPRFrame)); //(no sprite file behind it)
                    var t = typeof(FSO.Files.Formats.IFF.Chunks.SPRFrame);
                    t.GetProperty("Width").GetSetMethod(true).Invoke(f, new object[] { 40 });
                    t.GetProperty("Height").GetSetMethod(true).Invoke(f, new object[] { 100 });
                    t.GetMethod("Init", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(f, null);
                    for (int y = 0; y < 100; y++)
                        for (int x = 0; x < 40; x++)
                            f.SetPixel(x, y, (y >= holeTop && y <= holeBottom && x > 8 && x < 32) ? Color.Transparent : Color.White);
                    return f;
                };
                var high = FSO.SimAntics.Utils.VMBasementWindows.MeasureOpening(mask(8, 28));
                var middle = FSO.SimAntics.Utils.VMBasementWindows.MeasureOpening(mask(30, 70));
                Check("a high window's opening", Math.Abs(high - 28 / 99f) < 0.01f && high <= band + FSO.SimAntics.Utils.VMBasementWindows.Slack, $"down to {high:0.00}");
                Check("an ordinary one's", Math.Abs(middle - 70 / 99f) < 0.01f && middle > band + FSO.SimAntics.Utils.VMBasementWindows.Slack, $"down to {middle:0.00}");
                Check("no opening: can't tell (not a basement window)", FSO.SimAntics.Utils.VMBasementWindows.MeasureOpening(mask(-1, -1)) == 1, "");
            }
        }
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
            //under a foundation, its sides go from the floor (raised with it) to the foundation's top
            var fnd = new FoundationMap(W, H);
            for (int y = 3; y <= 6; y++) for (int x = 3; x <= 6; x++) { fnd.Raise[y * W + x] = FoundationMap.DefaultHeight; fnd.Kind[y * W + x] = (byte)FoundationKind.Foundation; }
            fnd.Refresh();
            bp.Foundations = fnd;
            var raisedY = new TestBasement(bp, 7, shown: true).Build().Select(v => v.Position.Y).ToList();
            var r = FoundationMap.DefaultHeight * bp.TerrainFactor;
            Check("under a foundation: raised with it", Math.Abs(raisedY.Min() - r * 3) < 1e-3f && Math.Abs(raisedY.Max() - (r + 2.95f) * 3) < 1e-3f, $"{raisedY.Min()}..{raisedY.Max()}");
            //seen from above, they stop at the ground outside it (the foundation's own side is above: they flickered)
            var aboveY = new TestBasement(bp, 7, shown: false).Build().Select(v => v.Position.Y).ToList();
            Check("from above: up to the ground outside it", Math.Abs(aboveY.Max() - 2.95f * 3) < 1e-3f, $"{aboveY.Min()}..{aboveY.Max()}");
            //and black under its floor, round its edge (what's below the floor showed in the void)
            var skirt = new TestSkirt(bp).Build();
            var skirtY = skirt.Select(v => v.Position.Y).ToList();
            Check("a skirt under it", skirt.Length == 16 * 4 && Math.Abs(skirtY.Max() - r * 3) < 1e-3f && Math.Abs(skirtY.Min() - (r - Modelled3DBasementSkirt.Depth) * 3) < 1e-3f,
                $"{skirt.Length / 4} quads, {skirtY.Min()}..{skirtY.Max()}");
            bp.Foundations = null;
            //walls on its edges: the earth behind them isn't drawn (it showed through them in 2D)
            bp.Walls[-1][4 * W + 3].Segments = WallSegments.TopLeft;  //(3,4)'s -x side
            bp.Walls[-1][3 * W + 5].Segments = WallSegments.TopRight; //(5,3)'s -y side
            bp.Walls[-1][5 * W + 7].Segments = WallSegments.TopLeft;  //(6,5)'s +x side
            bp.Walls[-1][5 * W + 5].Segments = WallSegments.TopLeft;  //inside, between (4,5) and (5,5): no side there anyway
            var walled = new TestBasement(bp, 7).Build();
            Check("no earth behind walls on its edges", walled.Length == 13 * 4, $"{walled.Length / 4} quads");
            for (int i = 0; i < W * H; i++) bp.Walls[-1][i] = new WallTile();
            Check("level 0 is there to build on", bp.Walls[-1] != null && bp.Floors[-1] != null && bp.Walls[-1].Length == W * H, $"{bp.Walls[-1]?.Length} tiles");
        }

        // ---- lighting: the basement's room map and its cell of the light map (no GPU: the slots, and the shaders' maths)
        {
            Check("the basement's room map has its own slot", FSO.LotView.LMap.GPURoomMaps.Slot(-1) == FSO.LotView.LMap.GPURoomMaps.BasementSlot
                && Enumerable.Range(0, 5).All(f => FSO.LotView.LMap.GPURoomMaps.Slot(f) == f), $"slot {FSO.LotView.LMap.GPURoomMaps.Slot(-1)}, floors 0..4 as before");
            //LightingCommon.fx: a floor's cell is floor(fmod(Level, w), Level / w) of the 3x2 map, plus the light offset (fmod
            //keeps the dividend's sign, as C#'s % does); Level is level - 1 + 0.001 for floors, -0.999 for the basement
            Func<float, Vector2, Point> cell = (level, offset) =>
            {
                var uv = new Vector2((float)Math.Floor(level % 3f) / 3f, (float)Math.Floor(level / 3f) / 2f) + offset;
                return new Point((int)Math.Floor(uv.X * 3 + 1e-4f), (int)Math.Floor(uv.Y * 2 + 1e-4f));
            };
            var shift = FSO.LotView.Effects.LightMappedEffect.BasementCellShift;
            var floorCells = Enumerable.Range(0, 5).Select(f => cell(f + 0.001f, Vector2.Zero)).ToList();
            Check("floors keep their cells", floorCells.SequenceEqual(new[] { new Point(0, 0), new Point(1, 0), new Point(2, 0), new Point(0, 1), new Point(1, 1) }),
                string.Join(" ", floorCells));
            var basementCell = cell(-0.999f, shift);
            Check("the basement's draws read the last cell", basementCell == new Point(2, 1), $"{basementCell}, unshifted {cell(-0.999f, Vector2.Zero)}");
            //walls work their level out from their height: min(Level, floor(height) + 0.0001), the basement's -1..0
            var wallCell = cell(Math.Min(0.001f, (float)Math.Floor(-0.5f) + 0.0001f), shift);
            Check("and its walls, seen from the ground floor", wallCell == basementCell, $"{wallCell}");
        }

        Console.WriteLine(Failures == 0 ? "all passed" : $"{Failures} failed");
        return Failures == 0 ? 0 : 1;
    }
}

class TestBasement : Modelled3DBasement
{
    public TestBasement(Blueprint bp, ushort pattern, bool shown = false) : base(bp, pattern) { Shown = shown; }
    public TerrainParallaxVertex[] Build()
    {
        GenerateGeometry();
        return Vertices;
    }
}

class TestSkirt : Modelled3DBasementSkirt
{
    public TestSkirt(Blueprint bp) : base(bp) { }
    public TerrainParallaxVertex[] Build()
    {
        GenerateGeometry();
        return Vertices;
    }
}
