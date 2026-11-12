using System;
using System.Collections.Generic;

namespace Simitone.Client.UI.Panels.Neigh3D
{
    /// <summary>
    /// The projection TS1 used to paint its neighbourhood screens: a 2:1 dimetric view (camera 30 degrees above the
    /// horizon, looking along -X-Z) where heights are drawn three times taller than they physically are.
    ///
    /// World units are FreeSO's (3 per tile). "Y" throughout this file is the *drawn* height, ie. already multiplied
    /// by VerticalExaggeration, so that an orthographic camera at the original angle reproduces the painting exactly.
    /// </summary>
    public class NeighProjection
    {
        /// <summary> sqrt(0.5) * cos(30deg): painting pixels moved up per (unit of Y * PixelsPerUnit). </summary>
        public const float HeightFactor = 0.61237f;
        /// <summary> TS1 draws a 2.95 unit wall 230px tall at near zoom, about 3x what the ground scale implies. </summary>
        public const float VerticalExaggeration = 3f;
        /// <summary> World units per step of a TS1 height byte (x10 altitude, x 3/160 terrain factor). </summary>
        public const float UnitsPerHeightStep = 10f * 3f / 160f;

        /// <summary> Horizontal painting pixels per unit of (X - Z) / 2. 2*sqrt(2) on the full size neighbourhoods. </summary>
        public readonly float PixelsPerUnit;

        public NeighProjection(float pixelsPerUnit)
        {
            PixelsPerUnit = pixelsPerUnit;
        }

        /// <summary> Painting pixels a point moves up the screen per unit of Y. </summary>
        public float PixelsPerY => HeightFactor * PixelsPerUnit;

        public void PixelToWorld(float px, float py, float y, out float x, out float z)
        {
            //find where this pixel would be if it were on the ground plane, then unproject that.
            var groundPy = py + PixelsPerY * y;
            x = (px + 2 * groundPy) / PixelsPerUnit;
            z = (2 * groundPy - px) / PixelsPerUnit;
        }

        public void WorldToPixel(float x, float y, float z, out float px, out float py)
        {
            px = PixelsPerUnit * (x - z) / 2;
            py = PixelsPerUnit * (x + z) / 4 - PixelsPerY * y;
        }

        /// <summary> Converts a difference in TS1 height bytes into drawn height. </summary>
        public static float HeightStepsToY(float steps)
        {
            return steps * UnitsPerHeightStep * VerticalExaggeration;
        }
    }

    /// <summary> A point on the painting where we know how high the ground is. </summary>
    public struct NeighHeightSample
    {
        public float Px;
        public float Py;
        public float Y;

        public NeighHeightSample(float px, float py, float y)
        {
            Px = px; Py = py; Y = y;
        }
    }

    /// <summary>
    /// A heightmap defined over the pixels of a neighbourhood painting, rather than over the ground.
    ///
    /// Each grid vertex owns a painting pixel and a height, and is pushed back along the original camera's view ray
    /// until it reaches that height. That means the mesh always lines up with the painting when seen from the original
    /// angle, whatever the heights are - the heights only decide what you see when the camera moves away from it.
    /// </summary>
    public class NeighHeightField
    {
        public readonly int Cols;
        public readonly int Rows;
        public readonly float Cell;
        public readonly float[] Heights;
        public readonly NeighProjection Projection;

        /// <summary> Distance (painting pixels) at which the ground has mostly relaxed back to the base level. </summary>
        public float FalloffRadius = 220f;
        /// <summary> Higher values keep the ground closer to its nearest sample. </summary>
        public float Power = 3f;
        /// <summary> How far water is sunk below the base level (drawn height). </summary>
        public float WaterDepth = 1.5f;
        public int SmoothPasses = 2;
        /// <summary>
        /// How many samples the pull towards the base level counts as. Set this to the number of samples a lot
        /// produces, so that open ground is as convincing as one lot rather than one tile of one lot.
        /// </summary>
        public float BaseSamples = 1f;

        public NeighHeightField(int width, int height, float cell, NeighProjection projection)
        {
            Cell = cell;
            Cols = (int)Math.Ceiling(width / cell) + 1;
            Rows = (int)Math.Ceiling(height / cell) + 1;
            Heights = new float[Cols * Rows];
            Projection = projection;
        }

        /// <summary>
        /// Fills the heightmap from scattered samples, with base level 0 everywhere we know nothing.
        /// </summary>
        /// <param name="water">Optional, one entry per grid vertex. Water is flattened below the base level.</param>
        public void Build(IList<NeighHeightSample> samples, bool[] water)
        {
            var eps = Cell * Cell * 0.25f;
            var halfPow = Power / 2;
            var baseWeight = BaseSamples / (float)Math.Pow(FalloffRadius * FalloffRadius, halfPow);

            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Cols; col++)
                {
                    var px = col * Cell;
                    var py = row * Cell;
                    //inverse distance weighting, plus a constant pull towards the base level
                    //so that the influence of a lot fades out instead of spreading across the whole map.
                    float sum = 0, weights = baseWeight;
                    for (int i = 0; i < samples.Count; i++)
                    {
                        var s = samples[i];
                        var dx = s.Px - px;
                        var dy = s.Py - py;
                        var w = 1f / (float)Math.Pow(dx * dx + dy * dy + eps, halfPow);
                        sum += w * s.Y;
                        weights += w;
                    }
                    Heights[row * Cols + col] = sum / weights;
                }
            }

            if (water != null)
            {
                for (int i = 0; i < Heights.Length; i++)
                {
                    if (water[i]) Heights[i] = Math.Min(Heights[i], 0) - WaterDepth;
                }
            }

            for (int i = 0; i < SmoothPasses; i++) Smooth(water);
            LimitSlope();
        }

        private void Smooth(bool[] pinned)
        {
            var src = (float[])Heights.Clone();
            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Cols; col++)
                {
                    var i = row * Cols + col;
                    if (pinned != null && pinned[i]) continue;
                    float sum = 0;
                    int n = 0;
                    for (int oy = -1; oy <= 1; oy++)
                    {
                        var r = row + oy;
                        if (r < 0 || r >= Rows) continue;
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            var c = col + ox;
                            if (c < 0 || c >= Cols) continue;
                            sum += src[r * Cols + c];
                            n++;
                        }
                    }
                    Heights[i] = sum / n;
                }
            }
        }

        /// <summary>
        /// Ground that rises faster than the original camera's view ray, as you go up the painting, would have been
        /// an overhang from the original angle. The painting can't contain one, and it would fold the mesh over itself.
        /// </summary>
        private void LimitSlope()
        {
            var maxRise = (Cell / Projection.PixelsPerY) * 0.85f;
            for (int row = Rows - 2; row >= 0; row--)
            {
                for (int col = 0; col < Cols; col++)
                {
                    var below = Heights[(row + 1) * Cols + col];
                    var i = row * Cols + col;
                    if (Heights[i] > below + maxRise) Heights[i] = below + maxRise;
                }
            }
        }

        /// <summary> Bilinear height at a painting pixel. </summary>
        public float SampleHeight(float px, float py)
        {
            var fx = Math.Max(0, Math.Min(Cols - 1.001f, px / Cell));
            var fy = Math.Max(0, Math.Min(Rows - 1.001f, py / Cell));
            var x0 = (int)fx;
            var y0 = (int)fy;
            var tx = fx - x0;
            var ty = fy - y0;
            var i = y0 * Cols + x0;
            var top = Heights[i] * (1 - tx) + Heights[i + 1] * tx;
            var bottom = Heights[i + Cols] * (1 - tx) + Heights[i + Cols + 1] * tx;
            return top * (1 - ty) + bottom * ty;
        }

        /// <summary>
        /// Builds the terrain mesh. Positions are xyz triples in world space, uvs are painting pixels divided by the
        /// given size (so the painting can be used as the texture as is).
        /// </summary>
        public void BuildMesh(float texWidth, float texHeight, out float[] positions, out float[] uvs, out int[] indices)
        {
            positions = new float[Cols * Rows * 3];
            uvs = new float[Cols * Rows * 2];
            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Cols; col++)
                {
                    var i = row * Cols + col;
                    var px = col * Cell;
                    var py = row * Cell;
                    var y = Heights[i];
                    float x, z;
                    Projection.PixelToWorld(px, py, y, out x, out z);
                    positions[i * 3] = x;
                    positions[i * 3 + 1] = y;
                    positions[i * 3 + 2] = z;
                    uvs[i * 2] = px / texWidth;
                    uvs[i * 2 + 1] = py / texHeight;
                }
            }

            indices = new int[(Cols - 1) * (Rows - 1) * 6];
            int n = 0;
            for (int row = 0; row < Rows - 1; row++)
            {
                for (int col = 0; col < Cols - 1; col++)
                {
                    var i = row * Cols + col;
                    indices[n++] = i;
                    indices[n++] = i + 1;
                    indices[n++] = i + Cols;
                    indices[n++] = i + 1;
                    indices[n++] = i + Cols + 1;
                    indices[n++] = i + Cols;
                }
            }
        }
    }

    /// <summary>
    /// Turns the terrain stored in each TS1 house file into height samples on the neighbourhood painting.
    /// </summary>
    public static class NeighLotSampler
    {
        public const int LotSize = 64;

        public class LotTerrain
        {
            public int House;
            /// <summary> Where the neighbourhood places this lot on the painting. </summary>
            public float Px, Py;
            /// <summary> One byte per tile, row major (y * 64 + x), as stored by TS1. </summary>
            public byte[] Heights;
            /// <summary> True where the player can build. Optional. </summary>
            public bool[] Buildable;

            //filled in by the sampler
            public int MinX, MinY, MaxX, MaxY;
            public byte CentreHeight;
            public byte MinHeight, MaxHeight;
        }

        /// <summary> Finds the buildable bounds and the height under their centre. </summary>
        public static void Measure(LotTerrain lot)
        {
            int minX = LotSize, minY = LotSize, maxX = -1, maxY = -1;
            if (lot.Buildable != null)
            {
                for (int y = 0; y < LotSize; y++)
                {
                    for (int x = 0; x < LotSize; x++)
                    {
                        if (!lot.Buildable[y * LotSize + x]) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (maxX < minX || maxY < minY)
            {
                //no buildable area information... assume everything but the border.
                minX = minY = 8;
                maxX = maxY = LotSize - 9;
            }
            lot.MinX = minX; lot.MinY = minY; lot.MaxX = maxX; lot.MaxY = maxY;
            lot.CentreHeight = lot.Heights[((minY + maxY) / 2) * LotSize + (minX + maxX) / 2];

            byte lo = 255, hi = 0;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var h = lot.Heights[y * LotSize + x];
                    if (h < lo) lo = h;
                    if (h > hi) hi = h;
                }
            }
            lot.MinHeight = lo; lot.MaxHeight = hi;
        }

        /// <summary> The most common lot height. Lots at this height sit on the base plane. </summary>
        public static byte FindBaseHeight(IList<LotTerrain> lots)
        {
            var counts = new int[256];
            foreach (var lot in lots) counts[lot.CentreHeight]++;
            int best = 0;
            for (int i = 1; i < 256; i++) if (counts[i] > counts[best]) best = i;
            return (byte)best;
        }

        /// <summary>
        /// Samples the buildable part of each lot onto the painting. The lot's painting position is taken to be the
        /// centre of its buildable area; every other tile is placed relative to that using the painting's projection.
        /// </summary>
        public static List<NeighHeightSample> Sample(IList<LotTerrain> lots, NeighProjection proj, byte baseHeight, int tileStep = 3)
        {
            var result = new List<NeighHeightSample>();
            foreach (var lot in lots)
            {
                var cx = (lot.MinX + lot.MaxX) / 2f;
                var cy = (lot.MinY + lot.MaxY) / 2f;
                var centreY = NeighProjection.HeightStepsToY(lot.CentreHeight - baseHeight);
                float worldX, worldZ;
                proj.PixelToWorld(lot.Px, lot.Py, centreY, out worldX, out worldZ);

                for (int y = lot.MinY; y <= lot.MaxY; y += tileStep)
                {
                    for (int x = lot.MinX; x <= lot.MaxX; x += tileStep)
                    {
                        var h = NeighProjection.HeightStepsToY(lot.Heights[y * LotSize + x] - baseHeight);
                        float px, py;
                        proj.WorldToPixel(worldX + (x - cx) * 3f, h, worldZ + (y - cy) * 3f, out px, out py);
                        result.Add(new NeighHeightSample(px, py, h));
                    }
                }
            }
            return result;
        }
    }
}
