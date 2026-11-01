using System;
using Microsoft.Xna.Framework;

namespace Simitone.Client.UI.Model
{
    /// <summary>
    /// Pure geometry used by the gamepad lot controls. Kept free of game state so it can be tested on its own.
    /// </summary>
    public static class PadMath
    {
        /// <summary>
        /// Solves <c>a * x + b * y = target</c> for (x, y). Used to turn a screen-space movement into a tile-space one,
        /// given the screen-space images <paramref name="a"/> and <paramref name="b"/> of one tile step along X and Y.
        /// Returns zero when a and b are (nearly) parallel, e.g. a camera looking along the floor.
        /// </summary>
        public static Vector2 SolveBasis(Vector2 a, Vector2 b, Vector2 target)
        {
            var det = a.X * b.Y - a.Y * b.X;
            if (Math.Abs(det) < 1e-4f) return Vector2.Zero;
            return new Vector2(
                (target.X * b.Y - target.Y * b.X) / det,
                (a.X * target.Y - a.Y * target.X) / det);
        }

        /// <summary>
        /// How far a point lies outside a rectangle on each axis: negative past the left/top edge, positive past the
        /// right/bottom edge, zero on an axis where it's inside.
        /// </summary>
        public static Vector2 Overflow(Vector2 point, float left, float top, float right, float bottom)
        {
            return new Vector2(
                point.X < left ? point.X - left : (point.X > right ? point.X - right : 0),
                point.Y < top ? point.Y - top : (point.Y > bottom ? point.Y - bottom : 0));
        }

        /// <summary>
        /// Distance in tiles from a tile-space point to a footprint rectangle given in 1/16 tile units
        /// (the units of LotTilePos and VMObstacle). Zero when the point is inside the footprint.
        /// </summary>
        public static float DistanceToFootprint(Vector2 tile, int x1, int y1, int x2, int y2)
        {
            var px = tile.X * 16f;
            var py = tile.Y * 16f;
            var dx = Math.Max(Math.Max(x1 - px, 0), px - x2);
            var dy = Math.Max(Math.Max(y1 - py, 0), py - y2);
            return (float)Math.Sqrt(dx * dx + dy * dy) / 16f;
        }

        /// <summary>
        /// Converts a snap distance measured on screen into tiles at the current camera scale, so snapping feels the
        /// same at every zoom level and in 2D and 3D. Clamped to [min, max] tiles.
        /// </summary>
        public static float SnapRadius(float pixelsPerTile, float snapPixels, float min, float max)
        {
            if (!(pixelsPerTile > 1e-3f)) return max;
            return Math.Max(min, Math.Min(max, snapPixels / pixelsPerTile));
        }

        /// <summary>
        /// Converts a tile-space direction to VMDirectControlInput's direction units: -32767..32767 covering -pi..pi,
        /// measured the way VMDirectControlFrame moves (x = sin, y = -cos).
        /// </summary>
        public static short DirectionToShort(Vector2 tileDirection)
        {
            return RadiansToDirection(Math.Atan2(tileDirection.X, -tileDirection.Y));
        }

        /// <summary>An angle in radians (any range, same convention as avatar RadianDirection) in direction units.</summary>
        public static short RadiansToDirection(double radians)
        {
            var normalized = radians % (Math.PI * 2);
            if (normalized > Math.PI) normalized -= Math.PI * 2;
            if (normalized <= -Math.PI) normalized += Math.PI * 2;
            return (short)Math.Round(normalized / Math.PI * 32767);
        }

        /// <summary>Unit tile-space vector an avatar with this RadianDirection faces.</summary>
        public static Vector2 Forward(float radianDirection)
        {
            return new Vector2((float)Math.Sin(radianDirection), -(float)Math.Cos(radianDirection));
        }

        /// <summary>The point of a footprint (1/16 tile units) closest to <paramref name="tile"/>, in tiles.</summary>
        public static Vector2 ClosestOnFootprint(Vector2 tile, int x1, int y1, int x2, int y2)
        {
            return new Vector2(Math.Max(x1, Math.Min(x2, tile.X * 16f)), Math.Max(y1, Math.Min(y2, tile.Y * 16f))) / 16f;
        }

        /// <summary>Angle in radians (0..pi) between a unit vector and any non-zero vector.</summary>
        public static float AngleBetween(Vector2 unit, Vector2 other)
        {
            var length = other.Length();
            if (length < 1e-6f) return 0;
            return (float)Math.Acos(Math.Max(-1f, Math.Min(1f, Vector2.Dot(unit, other / length))));
        }

        /// <summary>
        /// How squarely a footprint (1/16 tile units) sits in front of a Sim at <paramref name="sim"/> facing
        /// <paramref name="forward"/> (a unit vector): the distance to its best point that is within
        /// <paramref name="reach"/> tiles and <paramref name="maxAngle"/> radians of the facing, plus
        /// <paramref name="anglePenalty"/> per radian off centre. Lower is better; PositiveInfinity if no part qualifies.
        /// Besides the point nearest the Sim, the points nearest two spots straight ahead are tried, so a long counter
        /// counts by its part ahead of the Sim rather than its part beside them.
        /// </summary>
        public static float FrontScore(Vector2 sim, Vector2 forward, int x1, int y1, int x2, int y2,
            float reach, float maxAngle, float anglePenalty)
        {
            var best = float.PositiveInfinity;
            for (int i = 0; i < 3; i++)
            {
                var probe = sim + forward * (reach * i / 2f);
                var to = ClosestOnFootprint(probe, x1, y1, x2, y2) - sim;
                var distance = to.Length();
                if (distance > reach) continue;
                //touching or standing in it: the direction to its nearest point means nothing, so use its centre
                var direction = (distance < 0.05f) ? new Vector2(x1 + x2, y1 + y2) / 32f - sim : to;
                var angle = AngleBetween(forward, direction);
                if (angle > maxAngle) continue;
                best = Math.Min(best, distance + angle * anglePenalty);
            }
            return best;
        }

        /// <summary>
        /// Moves <paramref name="current"/> towards <paramref name="target"/>, covering the same fraction of the gap per
        /// second at any frame rate (<paramref name="rate"/> per second, exponential).
        /// </summary>
        public static Vector2 Follow(Vector2 current, Vector2 target, float rate, float dt)
        {
            if (dt <= 0) return current;
            return current + (target - current) * (1f - (float)Math.Exp(-rate * dt));
        }

        /// <summary>Clamps a list index into 0..length-1 (0 for an empty list).</summary>
        public static int ClampIndex(int index, int length)
        {
            return Math.Max(0, Math.Min(length - 1, index));
        }

        /// <summary>The index a highlight moves to from <paramref name="from"/> by <paramref name="step"/>, or -1 past either end.</summary>
        public static int HighlightStep(int from, int step, int length)
        {
            var to = from + step;
            return (to < 0 || to >= length) ? -1 : to;
        }

        /// <summary>
        /// On-screen rectangle of item <paramref name="index"/> in a scrolling list of
        /// <paramref name="itemSize"/>-wide (or tall) items, clipped to the list's <paramref name="width"/> x
        /// <paramref name="height"/>. The whole list if the item is out of view.
        /// </summary>
        public static Rectangle ListItemBounds(int index, int itemSize, float scroll, int width, int height, bool vertical)
        {
            var whole = new Rectangle(0, 0, width, height);
            var start = (int)(index * itemSize - scroll);
            var item = vertical ? new Rectangle(0, start, width, itemSize) : new Rectangle(start, 0, itemSize, height);
            var clipped = Rectangle.Intersect(item, whole);
            return (clipped.Width > 0 && clipped.Height > 0) ? clipped : whole;
        }

        /// <summary>
        /// Run/walk with hysteresis: start running past <paramref name="on"/> deflection, keep running until it drops
        /// below <paramref name="off"/>. A single threshold flips every frame on a stick resting near it.
        /// </summary>
        public static bool Run(bool running, float deflection, float on, float off)
        {
            return running ? deflection >= off : deflection > on;
        }

        /// <summary>
        /// Clamps a menu anchor so a menu reaching <paramref name="marginX"/>/<paramref name="marginY"/> out from it
        /// stays on a screen of the given size. If the screen is too small, the anchor is centred on that axis.
        /// </summary>
        public static Vector2 ClampAnchor(Vector2 anchor, float width, float height, float marginX, float marginY)
        {
            return new Vector2(ClampAxis(anchor.X, width, marginX), ClampAxis(anchor.Y, height, marginY));
        }

        private static float ClampAxis(float value, float size, float margin)
        {
            if (size <= margin * 2) return size / 2;
            return Math.Max(margin, Math.Min(size - margin, value));
        }
    }
}
