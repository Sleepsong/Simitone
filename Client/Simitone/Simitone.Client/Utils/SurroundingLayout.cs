using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Simitone.Client.Utils
{
    /// <summary>
    /// Where the neighbouring houses go around the lot you're on. The Sims 1 never placed its lots next to each other:
    /// a neighbourhood is a painting with each house pinned at a spot on it. So the spot is turned back into a position
    /// on the ground (the painting is a 2:1 isometric view, as <see cref="SimitoneNeighOBJExporter"/> works it out), and
    /// the houses around yours are put in the cells of a grid of lots around it, as FreeSO puts the lots around one in
    /// The Sims Online: each in the free cell nearest the way it really lies, the nearest houses first. Lots side by
    /// side then meet edge to edge instead of overlapping, in the directions the painting has them.
    /// </summary>
    public static class SurroundingLayout
    {
        /// <summary>
        /// The ground position (tiles) a house's spot on the neighbourhood painting stands for. The painting's scale
        /// depends on the neighbourhood: those with many houses are painted smaller.
        /// </summary>
        public static Vector2 MapToTiles(Vector2 screen, int houseCount)
        {
            var scale = (houseCount > 30 ? 1f : 2f) * 1.41421356f;
            return new Vector2(screen.X + screen.Y * 2, screen.Y * 2 - screen.X) / scale / 3f;
        }

        /// <summary>
        /// Which house goes in each cell of the ring of lots around <paramref name="me"/> (cells are offsets in lots:
        /// (1, 0) is the next lot along +x). <paramref name="spacing"/> is a lot's width (tiles) on the grid. A house
        /// goes in the free cell nearest where it really is, if that's within a cell and a half of it; houses further
        /// than one cell beyond the ring aren't neighbours.
        /// </summary>
        public static Dictionary<Point, short> Arrange(IList<(short house, Vector2 tiles)> houses, short me, float spacing, int ring = 1)
        {
            var cells = new Dictionary<Point, short>();
            var mine = houses.FirstOrDefault(h => h.house == me);
            if (mine.house != me || spacing <= 0) return cells;
            var others = houses.Where(h => h.house != me)
                .Select(h => (h.house, offset: (h.tiles - mine.tiles) / spacing))
                .OrderBy(h => h.offset.LengthSquared());
            foreach (var (house, offset) in others)
            {
                if (Math.Max(Math.Abs(offset.X), Math.Abs(offset.Y)) > ring + 1) continue;
                Point? best = null;
                var bestDistance = float.MaxValue;
                for (int y = -ring; y <= ring; y++)
                {
                    for (int x = -ring; x <= ring; x++)
                    {
                        var cell = new Point(x, y);
                        if ((x == 0 && y == 0) || cells.ContainsKey(cell)) continue;
                        var distance = Vector2.DistanceSquared(new Vector2(x, y), offset);
                        if (distance < bestDistance) { bestDistance = distance; best = cell; }
                    }
                }
                if (best != null && bestDistance <= 1.5f * 1.5f) cells[best.Value] = house;
            }
            return cells;
        }

        /// <summary>The houses of a neighbourhood and their spots on its painting, from its LotLocations strings ("house, x, y").</summary>
        public static List<(short house, Vector2 screen)> Parse(IEnumerable<string> lotLocations)
        {
            var result = new List<(short, Vector2)>();
            foreach (var line in lotLocations)
            {
                var parts = (line ?? "").Split(',');
                if (parts.Length < 3) continue;
                if (short.TryParse(parts[0].Trim(), out var house) && int.TryParse(parts[1].Trim(), out var x) && int.TryParse(parts[2].Trim(), out var y))
                    result.Add((house, new Vector2(x, y)));
            }
            return result;
        }
    }
}
