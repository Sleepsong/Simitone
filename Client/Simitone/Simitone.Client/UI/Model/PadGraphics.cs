using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Client.UI.Model
{
    /// <summary>
    /// Procedurally drawn shapes for gamepad affordances: the lot selector and focus rings.
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
