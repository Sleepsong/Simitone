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
    /// panels, plus the Simitone note cut from Simitone's own logo. All original geometry generated here; no console
    /// or Sims artwork.
    /// Shapes are white with a black outline baked in, so tinting them only colours the white part.
    /// Textures are premultiplied alpha, matching the default sprite batch blend state.
    /// </summary>
    public static class PadGraphics
    {
        private static Texture2D Ring;

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

        /// <summary>Height, in pixels, of the note texture (about twice the size it's drawn at).</summary>
        public const int NoteHeight = 64;

        private static Texture2D Note;

        /// <summary>
        /// The Simitone note: the musical note that forms the "t" of the Simitone logo, white with a dark outline (so
        /// tinting colours the note), about <see cref="NoteHeight"/> pixels tall. It's cut from the white top layer of the
        /// logo image (load_logo.png); if that can't be read, a plain drawn quaver stands in.
        /// </summary>
        public static Texture2D GetNote(GraphicsDevice gd)
        {
            if (Note == null || Note.IsDisposed) Note = CutNoteFromLogo(gd) ?? DrawnNote(gd);
            return Note;
        }

        private static Texture2D CutNoteFromLogo(GraphicsDevice gd)
        {
            try
            {
                var logo = FSO.Content.Content.Get().CustomUI.Get("load_logo.png")?.Get(gd);
                if (logo == null) return null;
                int w = logo.Width, h = logo.Height;
                var px = new Color[w * h];
                logo.GetData(px);
                bool White(int x, int y)
                {
                    var c = px[y * w + x];
                    return c.A > 200 && Math.Min(c.R, Math.Min(c.G, c.B)) > 235;
                }

                //the note's stem is a little past the middle of the wordmark, in its upper half
                int sx = -1, sy = -1;
                for (int x = (int)(w * 0.50f); x < (int)(w * 0.54f) && sx < 0; x++)
                    for (int y = (int)(h * 0.10f); y < (int)(h * 0.40f); y++)
                        if (White(x, y)) { sx = x; sy = y; break; }
                if (sx < 0) return null;

                //flood fill the white shape it belongs to
                var mask = new bool[w * h];
                var stack = new System.Collections.Generic.Stack<int>();
                stack.Push(sy * w + sx);
                mask[sy * w + sx] = true;
                int minX = sx, maxX = sx, minY = sy, maxY = sy, count = 0;
                while (stack.Count > 0)
                {
                    var i = stack.Pop();
                    int x = i % w, y = i / w;
                    count++;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    void Visit(int nx, int ny)
                    {
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) return;
                        var ni = ny * w + nx;
                        if (mask[ni] || !White(nx, ny)) return;
                        mask[ni] = true;
                        stack.Push(ni);
                    }
                    Visit(x + 1, y); Visit(x - 1, y); Visit(x, y + 1); Visit(x, y - 1);
                }
                //sanity: one letter-sized shape, taller than wide (not a run of joined letters)
                int bw = maxX - minX + 1, bh = maxY - minY + 1;
                if (count < 1000 || bw > w * 0.2f || bh < h * 0.5f) return null;

                //at the logo's size: white note, dark outline around it (dilated mask), with a margin for the outline
                const int outline = 6, margin = 8;
                int sw = bw + margin * 2, shh = bh + margin * 2;
                var inside = new bool[sw * shh];
                for (int y = 0; y < bh; y++)
                    for (int x = 0; x < bw; x++)
                        inside[(y + margin) * sw + x + margin] = mask[(y + minY) * w + x + minX];
                var near = (bool[])inside.Clone();
                for (int pass = 0; pass < outline; pass++)
                {
                    var next = (bool[])near.Clone();
                    for (int y = 1; y < shh - 1; y++)
                        for (int x = 1; x < sw - 1; x++)
                        {
                            var i = y * sw + x;
                            if (near[i]) continue;
                            if (near[i - 1] || near[i + 1] || near[i - sw] || near[i + sw] || (pass % 2 == 1 && (near[i - sw - 1] || near[i - sw + 1] || near[i + sw - 1] || near[i + sw + 1])))
                                next[i] = true;
                        }
                    near = next;
                }

                //box-filter down to NoteHeight (premultiplied: white inside, translucent black outline)
                var scale = NoteHeight / (float)shh;
                int ow = Math.Max(1, (int)Math.Ceiling(sw * scale)), oh = NoteHeight;
                var output = new Color[ow * oh];
                for (int oy = 0; oy < oh; oy++)
                {
                    for (int ox = 0; ox < ow; ox++)
                    {
                        int x0 = (int)(ox / scale), x1 = Math.Min(sw, (int)((ox + 1) / scale));
                        int y0 = (int)(oy / scale), y1 = Math.Min(shh, (int)((oy + 1) / scale));
                        float white = 0, alpha = 0, n = 0;
                        for (int y = y0; y < Math.Max(y1, y0 + 1); y++)
                            for (int x = x0; x < Math.Max(x1, x0 + 1); x++)
                            {
                                if (x >= sw || y >= shh) continue;
                                var i = y * sw + x;
                                if (inside[i]) { white += 1; alpha += 1; }
                                else if (near[i]) alpha += 0.85f;
                                n++;
                            }
                        if (n == 0) continue;
                        output[oy * ow + ox] = new Color(white / n, white / n, white / n, alpha / n);
                    }
                }
                var texture = new Texture2D(gd, ow, oh);
                texture.SetData(output);
                return texture;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>A plain quaver (round head, stem, curved flag) with a black outline, if the logo can't be read.</summary>
        private static Texture2D DrawnNote(GraphicsDevice gd)
        {
            return Generate(gd, 40, NoteHeight, (x, y) =>
            {
                var p = Centre(x, y);
                //head: an ellipse tilted up to the right
                var d = p - new Vector2(15, 50);
                var rx = d.X * 0.94f + d.Y * 0.34f;
                var ry = -d.X * 0.34f + d.Y * 0.94f;
                var head = (float)Math.Sqrt(rx * rx / (9f * 9f) + ry * ry / (6.5f * 6.5f)) * 7f - 7f;
                var stem = RoundedRectDistance(p, new Vector2(22.5f, 29), new Vector2(1.8f, 21), 0.5f);
                var flag = SegmentDistance(p, new Vector2(23, 9), new Vector2(31, 18)) - 2.2f;
                flag = Math.Min(flag, SegmentDistance(p, new Vector2(31, 18), new Vector2(30, 28)) - 2.2f);
                var shape = Math.Min(head, Math.Min(stem, flag));
                return (Coverage(shape - 2.5f), Coverage(shape));
            });
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
