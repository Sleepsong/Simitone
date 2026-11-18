using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Simitone.VR
{
    /// <summary>
    /// The way up or down a staircase, for the observer's stair ride: through the stairs' own tiles, from the one you're
    /// on to the other end, so stairs that turn are followed (an L, or round a 2x2 block as a spiral staircase goes),
    /// with the corners rounded off. Positions are in tiles (a tile's centre is x + 0.5, y + 0.5).
    /// </summary>
    public static class VRStairPath
    {
        /// <summary>
        /// The centres of the tiles from <paramref name="start"/> to <paramref name="end"/> through <paramref name="tiles"/>
        /// (the stairs' footprint, every floor's tiles together), moving between tiles that share an edge: the shortest
        /// way; or, for a 2x2 block whose ends are side by side, the long way round all four (a spiral). Just the two
        /// ends if there's no way through the footprint.
        /// </summary>
        public static List<Vector2> Tiles(IEnumerable<Point> tiles, Point start, Point end)
        {
            var set = new HashSet<Point>(tiles) { start, end };
            var result = new List<Vector2>();
            if (start == end)
            {
                result.Add(Centre(start));
                return result;
            }
            var round = RoundBlock(set, start, end);
            var path = round ?? Shortest(set, start, end);
            if (path == null)
            {
                result.Add(Centre(start));
                result.Add(Centre(end));
                return result;
            }
            foreach (var tile in path) result.Add(Centre(tile));
            return result;
        }

        private static Vector2 Centre(Point tile) => new Vector2(tile.X + 0.5f, tile.Y + 0.5f);

        private static readonly Point[] Steps = { new Point(1, 0), new Point(0, 1), new Point(-1, 0), new Point(0, -1) };

        /// <summary>Breadth-first through the footprint, tiles sharing an edge; null if the end can't be reached.</summary>
        private static List<Point> Shortest(HashSet<Point> set, Point start, Point end)
        {
            var from = new Dictionary<Point, Point> { [start] = start };
            var queue = new Queue<Point>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var tile = queue.Dequeue();
                if (tile == end) break;
                foreach (var step in Steps)
                {
                    var next = new Point(tile.X + step.X, tile.Y + step.Y);
                    if (!set.Contains(next) || from.ContainsKey(next)) continue;
                    from[next] = tile;
                    queue.Enqueue(next);
                }
            }
            if (!from.ContainsKey(end)) return null;
            var path = new List<Point>();
            for (var tile = end; ; tile = from[tile])
            {
                path.Add(tile);
                if (tile == start) break;
            }
            path.Reverse();
            return path;
        }

        /// <summary>A 2x2 footprint with its ends side by side: round the other two tiles, as a spiral goes. Else null.</summary>
        private static List<Point> RoundBlock(HashSet<Point> set, Point start, Point end)
        {
            if (set.Count != 4) return null;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (var t in set)
            {
                minX = Math.Min(minX, t.X); maxX = Math.Max(maxX, t.X);
                minY = Math.Min(minY, t.Y); maxY = Math.Max(maxY, t.Y);
            }
            if (maxX - minX != 1 || maxY - minY != 1) return null;
            // the block's tiles in order round it
            var ring = new[] { new Point(minX, minY), new Point(maxX, minY), new Point(maxX, maxY), new Point(minX, maxY) };
            int s = Array.IndexOf(ring, start), e = Array.IndexOf(ring, end);
            if (s < 0 || e < 0 || (s + 2) % 4 == e) return null; //(opposite corners: the shortest way is as good as any)
            var direction = (s + 1) % 4 == e ? 3 : 1; // away from the end, so as to come back to it the other side
            var path = new List<Point>();
            for (int i = 0; i < 4; i++) path.Add(ring[(s + direction * i) % 4]);
            return path;
        }

        /// <summary>
        /// The path with its corners rounded: each turn becomes a curve between points <paramref name="radius"/> tiles
        /// before and after it (less where a straight is shorter than twice that), drawn with <paramref name="steps"/>
        /// points. Its ends stay where they are.
        /// </summary>
        public static List<Vector2> Round(IReadOnlyList<Vector2> path, float radius = 0.45f, int steps = 6)
        {
            var result = new List<Vector2>();
            if (path.Count == 0) return result;
            result.Add(path[0]);
            for (int i = 1; i < path.Count - 1; i++)
            {
                var p = path[i - 1];
                var v = path[i];
                var n = path[i + 1];
                var inLength = Vector2.Distance(p, v);
                var outLength = Vector2.Distance(v, n);
                if (inLength < 1e-4f || outLength < 1e-4f)
                {
                    result.Add(v);
                    continue;
                }
                var inDir = (v - p) / inLength;
                var outDir = (n - v) / outLength;
                if (Vector2.Dot(inDir, outDir) > 0.999f)
                {
                    result.Add(v); //(straight on)
                    continue;
                }
                var r = Math.Min(radius, Math.Min(inLength, outLength) / 2);
                var a = v - inDir * r;
                var b = v + outDir * r;
                for (int k = 0; k <= steps; k++)
                {
                    var t = k / (float)steps;
                    // a quadratic curve from a to b, pulled towards the corner
                    result.Add((1 - t) * (1 - t) * a + 2 * (1 - t) * t * v + t * t * b);
                }
            }
            if (path.Count > 1) result.Add(path[path.Count - 1]);
            return result;
        }

        /// <summary>The path's length (tiles).</summary>
        public static float Length(IReadOnlyList<Vector2> path)
        {
            float length = 0;
            for (int i = 1; i < path.Count; i++) length += Vector2.Distance(path[i - 1], path[i]);
            return length;
        }

        /// <summary>The point a distance along the path (clamped to its ends).</summary>
        public static Vector2 At(IReadOnlyList<Vector2> path, float distance)
        {
            if (path.Count == 0) return Vector2.Zero;
            if (distance <= 0) return path[0];
            for (int i = 1; i < path.Count; i++)
            {
                var segment = Vector2.Distance(path[i - 1], path[i]);
                if (distance <= segment && segment > 1e-6f) return Vector2.Lerp(path[i - 1], path[i], distance / segment);
                distance -= segment;
            }
            return path[path.Count - 1];
        }
    }
}
