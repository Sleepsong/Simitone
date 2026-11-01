using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Client.UI.Model
{
    public enum PadShape
    {
        Cross,
        Circle,
        Square,
        Triangle
    }

    /// <summary>
    /// Procedurally drawn shapes for gamepad affordances: the lot selector, the focus highlight, button prompts and
    /// panels. All original geometry generated here; no console artwork.
    /// Shapes are white with a black outline baked in, so tinting them only colours the white part.
    /// Textures are premultiplied alpha, matching the default sprite batch blend state.
    /// </summary>
    public static class PadGraphics
    {
        private static Texture2D Ring;
        private static Texture2D Marker;

        /// <summary>64x64 ring (radius 27) with a soft black outline.</summary>
        public static Texture2D GetRing(GraphicsDevice gd)
        {
            if (Ring == null || Ring.IsDisposed)
            {
                var centre = new Vector2(32, 32);
                Ring = Generate(gd, 64, 64, (x, y) =>
                {
                    var fromBand = Math.Abs(Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre) - 27f);
                    return (Clamp01(4f - fromBand), Clamp01(2.25f - fromBand));
                });
            }
            return Ring;
        }

        /// <summary>32x40 downward-pointing diamond, like a small plumb bob, with a black outline.</summary>
        public static Texture2D GetMarker(GraphicsDevice gd)
        {
            if (Marker == null || Marker.IsDisposed)
            {
                Marker = Generate(gd, 32, 40, (x, y) =>
                {
                    //diamond "radius" metric: 1 on the outer edge, 0 at the centre.
                    var metric = Math.Abs(x + 0.5f - 16f) / 15f + Math.Abs(y + 0.5f - 20f) / 19f;
                    return (Clamp01((1f - metric) * 15f), Clamp01((0.8f - metric) * 15f));
                });
            }
            return Marker;
        }

        private static Texture2D Disc;
        private static Texture2D Rounded;
        private static Texture2D Glow;
        private static Texture2D StickRing;
        private static Texture2D DPad;
        private static readonly Texture2D[] Shapes = new Texture2D[4];

        /// <summary>Corner size, in texture pixels, for nine-slicing <see cref="GetRounded"/>.</summary>
        public const int RoundedCorner = 12;
        /// <summary>Corner size for nine-slicing <see cref="GetGlow"/>; its outline sits <see cref="GlowInset"/> in from the edge.</summary>
        public const int GlowCorner = 24;
        public const int GlowInset = 12;

        /// <summary>32x32 white disc.</summary>
        public static Texture2D GetDisc(GraphicsDevice gd)
        {
            if (Disc == null || Disc.IsDisposed)
                Disc = GenerateMask(gd, 32, 32, (x, y) => Coverage(Vector2.Distance(Centre(x, y), new Vector2(16, 16)) - 15f));
            return Disc;
        }

        /// <summary>32x32 white rounded square (radius 10), for nine-slicing panels, keys and pills.</summary>
        public static Texture2D GetRounded(GraphicsDevice gd)
        {
            if (Rounded == null || Rounded.IsDisposed)
                Rounded = GenerateMask(gd, 32, 32, (x, y) => Coverage(RoundedRectDistance(Centre(x, y), new Vector2(16, 16), new Vector2(15.5f, 15.5f), 10f)));
            return Rounded;
        }

        /// <summary>
        /// 64x64 focus frame: a rounded outline (radius 10, <see cref="GlowInset"/> in from the edge) with a soft halo
        /// outside it and a faint one inside. White, for tinting; nine-slice with <see cref="GlowCorner"/>.
        /// </summary>
        public static Texture2D GetGlow(GraphicsDevice gd)
        {
            if (Glow == null || Glow.IsDisposed)
            {
                Glow = GenerateMask(gd, 64, 64, (x, y) =>
                {
                    var d = RoundedRectDistance(Centre(x, y), new Vector2(32, 32), new Vector2(32 - GlowInset, 32 - GlowInset), 10f);
                    var stroke = Coverage(Math.Abs(d) - 1.75f);
                    var halo = (d > 0) ? 0.55f * (float)Math.Exp(-d / 4f) : 0.18f * (float)Math.Exp(d / 3f);
                    return Math.Max(stroke, halo);
                });
            }
            return Glow;
        }

        /// <summary>32x32 white ring, for analog stick prompts.</summary>
        public static Texture2D GetStickRing(GraphicsDevice gd)
        {
            if (StickRing == null || StickRing.IsDisposed)
                StickRing = GenerateMask(gd, 32, 32, (x, y) => Coverage(Math.Abs(Vector2.Distance(Centre(x, y), new Vector2(16, 16)) - 12.5f) - 2.2f));
            return StickRing;
        }

        /// <summary>32x32 white plus sign, for D-pad prompts.</summary>
        public static Texture2D GetDPad(GraphicsDevice gd)
        {
            if (DPad == null || DPad.IsDisposed)
            {
                DPad = GenerateMask(gd, 32, 32, (x, y) =>
                {
                    var p = Centre(x, y);
                    var c = new Vector2(16, 16);
                    var bar1 = RoundedRectDistance(p, c, new Vector2(13, 4.5f), 2f);
                    var bar2 = RoundedRectDistance(p, c, new Vector2(4.5f, 13), 2f);
                    return Coverage(Math.Min(bar1, bar2));
                });
            }
            return DPad;
        }

        /// <summary>32x32 white PlayStation-style face button symbol, drawn as plain geometric strokes.</summary>
        public static Texture2D GetShape(GraphicsDevice gd, PadShape shape)
        {
            var i = (int)shape;
            if (Shapes[i] == null || Shapes[i].IsDisposed)
            {
                const float half = 1.9f; //stroke half-width
                Shapes[i] = GenerateMask(gd, 32, 32, (x, y) =>
                {
                    var p = Centre(x, y);
                    float d;
                    switch (shape)
                    {
                        case PadShape.Cross:
                            d = Math.Min(SegmentDistance(p, new Vector2(9, 9), new Vector2(23, 23)), SegmentDistance(p, new Vector2(23, 9), new Vector2(9, 23)));
                            break;
                        case PadShape.Circle:
                            d = Math.Abs(Vector2.Distance(p, new Vector2(16, 16)) - 9f);
                            break;
                        case PadShape.Square:
                            d = Math.Abs(RoundedRectDistance(p, new Vector2(16, 16), new Vector2(8.5f, 8.5f), 0.5f));
                            break;
                        default: //triangle
                            var a = new Vector2(16, 6.5f);
                            var b = new Vector2(25, 22.5f);
                            var c2 = new Vector2(7, 22.5f);
                            d = Math.Min(SegmentDistance(p, a, b), Math.Min(SegmentDistance(p, b, c2), SegmentDistance(p, c2, a)));
                            break;
                    }
                    return Coverage(d - half);
                });
            }
            return Shapes[i];
        }

        private static Vector2 Centre(int x, int y) => new Vector2(x + 0.5f, y + 0.5f);

        /// <summary>Anti-aliased coverage from a signed distance (negative inside).</summary>
        private static float Coverage(float distance) => Clamp01(0.5f - distance);

        /// <summary>Signed distance to a rounded rectangle centred at <paramref name="c"/> with half-size <paramref name="half"/>.</summary>
        private static float RoundedRectDistance(Vector2 p, Vector2 c, Vector2 half, float radius)
        {
            var q = new Vector2(Math.Abs(p.X - c.X), Math.Abs(p.Y - c.Y)) - (half - new Vector2(radius));
            var outside = new Vector2(Math.Max(q.X, 0), Math.Max(q.Y, 0)).Length();
            return outside + Math.Min(Math.Max(q.X, q.Y), 0) - radius;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Clamp01(Vector2.Dot(p - a, ab) / ab.LengthSquared());
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>White shape with the given per-pixel coverage, premultiplied.</summary>
        private static Texture2D GenerateMask(GraphicsDevice gd, int width, int height, Func<int, int, float> coverage)
        {
            var data = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var a = Clamp01(coverage(x, y));
                    data[y * width + x] = new Color(a, a, a, a);
                }
            }
            var texture = new Texture2D(gd, width, height);
            texture.SetData(data);
            return texture;
        }

        /// <param name="shape">Returns (alpha, brightness) for a pixel: alpha covers the outline, brightness the white fill.</param>
        private static Texture2D Generate(GraphicsDevice gd, int width, int height, Func<int, int, (float alpha, float bright)> shape)
        {
            var data = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var (alpha, bright) = shape(x, y);
                    var value = Math.Min(alpha, bright);
                    data[y * width + x] = new Color(value, value, value, alpha);
                }
            }
            var texture = new Texture2D(gd, width, height);
            texture.SetData(data);
            return texture;
        }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
