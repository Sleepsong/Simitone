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
