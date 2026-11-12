using FSO.Client;
using FSO.Client.UI.Framework;
using FSO.Common;
using FSO.Common.Rendering.Framework.IO;
using FSO.Common.Rendering.Framework.Model;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.HIT;
using FSO.SimAntics.Utils;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Simitone.Client.UI.Panels.Neigh3D
{
    /// <summary>
    /// PROTOTYPE: draws the neighbourhood painting as 3D terrain, using a heightmap worked out from the terrain of
    /// the lots placed on it. Houses are still their 2D thumbnails, stood up as camera facing cutouts.
    ///
    /// With the camera at its starting angle this is pixel identical to the 2D screen. Hold the right mouse button
    /// and drag to orbit and tilt.
    /// </summary>
    public class UINeighborhood3DView : UIElement
    {
        private const float GridCell = 8f;
        private const float DefaultPitch = (float)(Math.PI / 6);
        private const float MinPitch = 0.14f;
        private const float MaxPitch = 1.5f;
        private const float CameraDistance = 4000f;

        private class House
        {
            public UINeighborhoodHouseButton Button;
            public Vector2 Pixel;
            public Vector3 World;
            public float Depth;
            public Vector2 ScreenTL;
            public Vector2 ScreenBR;
        }

        private UINeighborhoodSelectionPanel Panel;
        private Texture2D Painting;
        private NeighProjection Projection;
        private NeighHeightField Field;
        private VertexPositionTexture[] TerrainVerts;
        private int[] TerrainIndices;
        private List<House> Houses = new List<House>();
        private House Hovered;
        private BasicEffect Effect;
        private VertexPositionTexture[] Quad = new VertexPositionTexture[4];
        private static readonly short[] QuadIndices = new short[] { 0, 1, 2, 1, 3, 2 };

        /// <summary> Rotation around the vertical axis, relative to the angle the painting was drawn from. </summary>
        public float Yaw;
        public float Pitch = DefaultPitch;

        private bool Orbiting;
        private Point LastMouse;
        private bool MouseDownOnUs;

        public UINeighborhood3DView(UINeighborhoodSelectionPanel panel, int mode, float houseScale, Texture2D painting, STR locations, IEnumerable<UINeighborhoodHouseButton> buttons)
        {
            Panel = panel;
            Painting = painting;
            //the scale 2 neighbourhood (Unleashed) draws everything at half size.
            Projection = new NeighProjection(2.8284271f / houseScale);

            var gd = GameFacade.GraphicsDevice;
            var lots = LoadLots(locations);
            foreach (var lot in lots) NeighLotSampler.Measure(lot);
            var baseHeight = NeighLotSampler.FindBaseHeight(lots);
            var samples = NeighLotSampler.Sample(lots, Projection, baseHeight);

            Field = new NeighHeightField(painting.Width, painting.Height, GridCell, Projection);
            if (lots.Count > 0) Field.BaseSamples = samples.Count / (float)lots.Count;
            Field.Build(samples, FindWater(painting, Field));

            float[] pos, uv;
            Field.BuildMesh(painting.Width, painting.Height, out pos, out uv, out TerrainIndices);
            TerrainVerts = new VertexPositionTexture[pos.Length / 3];
            for (int i = 0; i < TerrainVerts.Length; i++)
            {
                TerrainVerts[i] = new VertexPositionTexture(
                    new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]),
                    new Vector2(uv[i * 2], uv[i * 2 + 1]));
            }

            foreach (var btn in buttons)
            {
                var y = Field.SampleHeight(btn.X, btn.Y);
                float x, z;
                Projection.PixelToWorld(btn.X, btn.Y, y, out x, out z);
                Houses.Add(new House() { Button = btn, Pixel = btn.Position, World = new Vector3(x, y, z) });
            }

            Effect = new BasicEffect(gd);
            Effect.TextureEnabled = true;
            Effect.LightingEnabled = false;
            Effect.VertexColorEnabled = false;

            //listen everywhere: anything drawn above us (house panel, switcher) still gets its clicks first.
            ListenForMouse(new Rectangle(-8000, -8000, 16000, 16000), OnMouse);

            WriteReport(mode, lots, baseHeight, samples.Count);
        }

        private List<NeighLotSampler.LotTerrain> LoadLots(STR locations)
        {
            var result = new List<NeighLotSampler.LotTerrain>();
            for (int i = 0; i < locations.Length; i++)
            {
                var loc = locations.GetString(i).Split(',');
                var num = int.Parse(loc[0].TrimStart());
                if (num == 99) continue;
                try
                {
                    var house = Content.Get().Neighborhood.GetHouse(num);
                    var alt = house?.Get<ARRY>(0);
                    if (alt == null) continue;
                    var heights = VMTS1Activator.DecodeHeights(alt.TransposeData);
                    if (heights.Length < NeighLotSampler.LotSize * NeighLotSampler.LotSize) continue;
                    var flags = house.Get<ARRY>(8)?.TransposeData;
                    result.Add(new NeighLotSampler.LotTerrain()
                    {
                        House = num,
                        Px = int.Parse(loc[1].TrimStart()),
                        Py = int.Parse(loc[2].TrimStart()),
                        Heights = heights,
                        Buildable = (flags != null && flags.Length >= heights.Length) ? flags.Select(x => (x & 32) == 0).ToArray() : null
                    });
                }
                catch (Exception)
                {
                    //a lot we can't read just doesn't contribute any height.
                }
            }
            return result;
        }

        /// <summary>
        /// Guesses which grid vertices are water from the colour of the painting. Deliberately strict: a missed river
        /// just stays level with its banks, but a false positive would dig a hole.
        /// </summary>
        private static bool[] FindWater(Texture2D painting, NeighHeightField field)
        {
            var w = painting.Width;
            var h = painting.Height;
            var data = new Color[w * h];
            try
            {
                painting.GetData(data);
            }
            catch (Exception)
            {
                return null; //can't read the painting back on this device, so no water.
            }
            var result = new bool[field.Cols * field.Rows];
            var radius = 2;
            for (int row = 0; row < field.Rows; row++)
            {
                for (int col = 0; col < field.Cols; col++)
                {
                    var cx = (int)(col * field.Cell);
                    var cy = (int)(row * field.Cell);
                    int blue = 0, total = 0;
                    for (int oy = -radius; oy <= radius; oy++)
                    {
                        var y = cy + oy;
                        if (y < 0 || y >= h) continue;
                        for (int ox = -radius; ox <= radius; ox++)
                        {
                            var x = cx + ox;
                            if (x < 0 || x >= w) continue;
                            var c = data[y * w + x];
                            total++;
                            if (c.B > 70 && c.B > c.R * 1.35f && c.B > c.G * 1.08f) blue++;
                        }
                    }
                    result[row * field.Cols + col] = total > 0 && blue >= total * 0.8f;
                }
            }
            return result;
        }

        /// <summary>
        /// Writes what was measured to the user directory, so it's possible to tell whether the lots actually
        /// describe the hills in the painting or whether they are all at the same level.
        /// </summary>
        private void WriteReport(int mode, List<NeighLotSampler.LotTerrain> lots, byte baseHeight, int sampleCount)
        {
            try
            {
                var sb = new StringBuilder();
                var inv = CultureInfo.InvariantCulture;
                sb.AppendLine("Neighbourhood " + mode + " 3D prototype height report");
                sb.AppendLine("lots: " + lots.Count + ", samples: " + sampleCount + ", base height byte: " + baseHeight);
                sb.AppendLine("terrain height range (drawn units): " + Field.Heights.Min().ToString("0.00", inv) + " to " + Field.Heights.Max().ToString("0.00", inv));
                sb.AppendLine();
                sb.AppendLine("house, px, py, centre, min, max (height bytes inside the buildable area)");
                foreach (var lot in lots.OrderBy(x => x.House))
                {
                    sb.AppendLine(lot.House + ", " + lot.Px.ToString(inv) + ", " + lot.Py.ToString(inv) + ", " + lot.CentreHeight + ", " + lot.MinHeight + ", " + lot.MaxHeight);
                }
                File.WriteAllText(Path.Combine(FSOEnvironment.UserDir, "neigh3d_heights_" + mode + ".txt"), sb.ToString());
            }
            catch (Exception)
            {
                //diagnostic only
            }
        }

        private void OnMouse(UIMouseEventType evt, UpdateState state)
        {
            if (!Visible) return;
            switch (evt)
            {
                case UIMouseEventType.MouseDown:
                    MouseDownOnUs = true;
                    break;
                case UIMouseEventType.MouseUp:
                    if (!MouseDownOnUs) break;
                    MouseDownOnUs = false;
                    var hit = Pick(state.MouseState.Position);
                    if (hit != null)
                    {
                        HITVM.Get().PlaySoundEvent(FSO.Client.UI.Model.UISounds.NeighborhoodClick);
                        Panel.SelectHouse(hit.Button.HouseNumber);
                    }
                    else Panel.ResetZoom();
                    break;
                case UIMouseEventType.MouseOut:
                    MouseDownOnUs = false;
                    break;
            }
        }

        private House Pick(Point mouse)
        {
            //nearest to the camera wins.
            House best = null;
            foreach (var house in Houses)
            {
                if (mouse.X < house.ScreenTL.X || mouse.X > house.ScreenBR.X || mouse.Y < house.ScreenTL.Y || mouse.Y > house.ScreenBR.Y) continue;
                if (best == null || house.Depth < best.Depth) best = house;
            }
            return best;
        }

        public void ResetCamera()
        {
            Yaw = 0;
            Pitch = DefaultPitch;
        }

        public override void Update(UpdateState state)
        {
            base.Update(state);
            if (!Visible) return;

            var mouse = state.MouseState;
            if (mouse.RightButton == ButtonState.Pressed)
            {
                if (Orbiting)
                {
                    Yaw -= (mouse.X - LastMouse.X) * 0.008f;
                    Pitch = MathHelper.Clamp(Pitch + (mouse.Y - LastMouse.Y) * 0.006f, MinPitch, MaxPitch);
                }
                Orbiting = true;
                LastMouse = mouse.Position;
            }
            else Orbiting = false;

            if (state.NewKeys.Contains(Keys.Home)) ResetCamera();

            var over = Orbiting ? null : Pick(mouse.Position);
            if (over != Hovered)
            {
                if (over != null) HITVM.Get().PlaySoundEvent(FSO.Client.UI.Model.UISounds.NeighborhoodRollover);
                Hovered = over;
            }
        }

        public override void Draw(UISpriteBatch batch)
        {
            if (!Visible || Effect == null) return;
            var gd = batch.GraphicsDevice;
            var vp = gd.Viewport;

            //screen pixels per painting pixel. the panel is laid out in "UI pixels", which may not be backbuffer pixels.
            var uiToScreen = vp.Height / (float)GlobalSettings.Default.GraphicsHeight;
            var paintToScreen = Panel.ScaleX * uiToScreen;
            //screen pixels per world unit, for a world axis that lies along the screen.
            var unitToScreen = paintToScreen * Projection.PixelsPerUnit * 0.70710678f;

            float tx, tz;
            Projection.PixelToWorld(Panel.CenterPositionX, Panel.CenterPositionY, 0, out tx, out tz);
            var target = new Vector3(tx, 0, tz);
            var angle = MathHelper.PiOver4 + Yaw;
            var toCamera = new Vector3((float)(Math.Cos(Pitch) * Math.Sin(angle)), (float)Math.Sin(Pitch), (float)(Math.Cos(Pitch) * Math.Cos(angle)));

            var view = Microsoft.Xna.Framework.Matrix.CreateLookAt(target + toCamera * CameraDistance, target, Vector3.Up);
            var projection = Microsoft.Xna.Framework.Matrix.CreateOrthographic(vp.Width / unitToScreen, vp.Height / unitToScreen, 1f, CameraDistance * 2);
            var right = new Vector3(view.M11, view.M21, view.M31);
            var up = new Vector3(view.M12, view.M22, view.M32);

            batch.Pause();

            gd.Clear(ClearOptions.DepthBuffer, Color.Black, 1f, 0);
            gd.BlendState = BlendState.Opaque;
            gd.DepthStencilState = DepthStencilState.Default;
            gd.RasterizerState = RasterizerState.CullNone;
            gd.SamplerStates[0] = SamplerState.LinearClamp;

            Effect.World = Microsoft.Xna.Framework.Matrix.Identity;
            Effect.View = view;
            Effect.Projection = projection;
            Effect.DiffuseColor = Vector3.One;
            Effect.Alpha = 1f;
            Effect.Texture = Painting;
            foreach (var pass in Effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, TerrainVerts, 0, TerrainVerts.Length, TerrainIndices, 0, TerrainIndices.Length / 3);
            }

            //houses: thumbnails standing on the terrain, always facing the camera. drawn furthest first, without depth
            //testing against the terrain, because a thumbnail includes the garden in front of the house.
            gd.BlendState = BlendState.AlphaBlend; //UI textures are premultiplied
            gd.DepthStencilState = DepthStencilState.None;
            var viewProj = view * projection;
            foreach (var house in Houses)
            {
                var clip = Vector4.Transform(new Vector4(house.World, 1), viewProj);
                house.Depth = clip.Z / clip.W;
                var screen = new Vector2((clip.X / clip.W * 0.5f + 0.5f) * vp.Width, (0.5f - clip.Y / clip.W * 0.5f) * vp.Height);
                var tl = house.Button.ThumbOffset(false) * paintToScreen;
                var size = house.Button.ThumbSize * paintToScreen;
                //picking happens in UI pixels
                house.ScreenTL = (screen + tl) / uiToScreen;
                house.ScreenBR = (screen + tl + size) / uiToScreen;
            }

            foreach (var house in Houses.OrderByDescending(x => x.Depth))
            {
                var open = house == Hovered;
                var tex = house.Button.GetThumb(open);
                if (tex == null) continue;
                var tl = house.Button.ThumbOffset(open) / (Projection.PixelsPerUnit * 0.70710678f);
                var size = house.Button.ThumbSize / (Projection.PixelsPerUnit * 0.70710678f);
                var origin = house.World + right * tl.X - up * tl.Y;
                Quad[0] = new VertexPositionTexture(origin, new Vector2(0, 0));
                Quad[1] = new VertexPositionTexture(origin + right * size.X, new Vector2(1, 0));
                Quad[2] = new VertexPositionTexture(origin - up * size.Y, new Vector2(0, 1));
                Quad[3] = new VertexPositionTexture(origin + right * size.X - up * size.Y, new Vector2(1, 1));
                Effect.Texture = tex;
                foreach (var pass in Effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, Quad, 0, 4, QuadIndices, 0, 2);
                }
            }

            gd.DepthStencilState = DepthStencilState.None;
            batch.Resume();
        }

        public override void Removed()
        {
            base.Removed();
            Effect?.Dispose();
            Effect = null;
        }
    }
}
