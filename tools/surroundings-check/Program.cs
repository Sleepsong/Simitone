using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Simitone.Client.Utils;

// Simitone.Client.Utils.SurroundingLayout: turning a house's spot on the neighbourhood painting into a ground position,
// and giving each nearby house a cell of the ring of lots around the one you're on.
static class Program
{
    static int Failures;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}: {detail}");
        if (!ok) Failures++;
    }

    static string Show(Dictionary<Point, short> cells) =>
        cells.Count == 0 ? "(none)" : string.Join(", ", cells.OrderBy(c => c.Key.Y).ThenBy(c => c.Key.X).Select(c => $"({c.Key.X},{c.Key.Y})={c.Value}"));

    static int Main()
    {
        const float spacing = 62; // a 64 tile lot, as FreeSO spaces them (size - 2)

        // ---- the painting: a step right on screen is +x and -y on the ground; a step down is +x and +y
        var right = SurroundingLayout.MapToTiles(new Vector2(100, 0), 10) - SurroundingLayout.MapToTiles(Vector2.Zero, 10);
        var down = SurroundingLayout.MapToTiles(new Vector2(0, 100), 10) - SurroundingLayout.MapToTiles(Vector2.Zero, 10);
        Check("screen right", right.X > 0 && right.Y < 0 && Math.Abs(right.X + right.Y) < 1e-3, $"{right}");
        Check("screen down", down.X > 0 && down.Y > 0 && Math.Abs(down.X - down.Y) < 1e-3, $"{down}");
        var big = SurroundingLayout.MapToTiles(new Vector2(100, 0), 40);
        Check("big neighbourhoods are painted at half scale", Math.Abs(big.X - 2 * right.X) < 1e-3, $"{big} vs {right}");

        // ---- one house in each of the eight directions, exactly a lot away
        var houses = new List<(short, Vector2)> { (1, new Vector2(500, 500)) };
        short id = 10;
        var expected = new Dictionary<Point, short>();
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0) continue;
                houses.Add((id, new Vector2(500 + x * spacing, 500 + y * spacing)));
                expected[new Point(x, y)] = id++;
            }
        var cells = SurroundingLayout.Arrange(houses, 1, spacing);
        Check("eight directions", cells.Count == 8 && expected.All(e => cells.TryGetValue(e.Key, out var h) && h == e.Value), Show(cells));

        // ---- off the grid: a little off a cell still lands in it
        cells = SurroundingLayout.Arrange(new List<(short, Vector2)> { (1, Vector2.Zero), (2, new Vector2(70, -20)) }, 1, spacing);
        Check("near a cell", cells.Count == 1 && cells.TryGetValue(new Point(1, 0), out var h2) && h2 == 2, Show(cells));

        // ---- two houses want the same cell: the nearer gets it, the other the next nearest free cell
        cells = SurroundingLayout.Arrange(new List<(short, Vector2)> { (1, Vector2.Zero), (2, new Vector2(80, 10)), (3, new Vector2(60, 0)) }, 1, spacing);
        Check("contested cell", cells.TryGetValue(new Point(1, 0), out var h3) && h3 == 3 && cells.ContainsValue(2)
            && cells.First(c => c.Value == 2).Key.X == 1, Show(cells));

        // ---- a house practically on top of yours still gets a cell next to it
        cells = SurroundingLayout.Arrange(new List<(short, Vector2)> { (1, Vector2.Zero), (2, new Vector2(5, 0)) }, 1, spacing);
        Check("very close", cells.Count == 1 && cells.Keys.First() == new Point(1, 0), Show(cells));

        // ---- far houses aren't neighbours
        cells = SurroundingLayout.Arrange(new List<(short, Vector2)> { (1, Vector2.Zero), (2, new Vector2(spacing * 2.6f, 0)), (3, new Vector2(0, -spacing * 4)) }, 1, spacing);
        Check("far away", cells.Count == 0, Show(cells));

        // ---- just beyond the ring, with its cell free: it comes in; with nothing near enough free: left out
        cells = SurroundingLayout.Arrange(new List<(short, Vector2)> { (1, Vector2.Zero), (2, new Vector2(spacing * 1.9f, 0)) }, 1, spacing);
        Check("just beyond the ring", cells.Count == 1 && cells.Keys.First() == new Point(1, 0), Show(cells));
        var crowd = new List<(short, Vector2)> { (1, Vector2.Zero) };
        for (short i = 2; i < 14; i++) crowd.Add((i, new Vector2(spacing * (0.9f + i * 0.01f), (i - 8) * 3)));
        cells = SurroundingLayout.Arrange(crowd, 1, spacing);
        Check("crowded side", cells.Count < 12 && cells.Keys.All(c => c.X >= 0), $"{cells.Count} placed: {Show(cells)}");

        // ---- every cell is in the ring, each house once
        Check("cells in the ring", cells.Keys.All(c => Math.Abs(c.X) <= 1 && Math.Abs(c.Y) <= 1 && c != Point.Zero)
            && cells.Values.Distinct().Count() == cells.Count, Show(cells));

        // ---- nothing to go on
        Check("not on the map", SurroundingLayout.Arrange(houses, 99, spacing).Count == 0, "house 99 isn't listed");
        Check("no spacing", SurroundingLayout.Arrange(houses, 1, 0).Count == 0, "spacing 0");

        // ---- LotLocations lines
        var parsed = SurroundingLayout.Parse(new[] { "1, 120, 340", " 12 ,-5,7", "bad line", "3, x, 9", null, "4,1" });
        Check("parse", parsed.Count == 2 && parsed[0] == (1, new Vector2(120, 340)) && parsed[1] == (12, new Vector2(-5, 7)),
            string.Join("; ", parsed.Select(p => $"{p.house}@{p.screen}")));

        Console.WriteLine(Failures == 0 ? "all passed" : $"{Failures} failed");
        return Failures == 0 ? 0 : 1;
    }
}
