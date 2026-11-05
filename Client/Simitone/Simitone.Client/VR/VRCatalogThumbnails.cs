using System;
using System.Collections.Generic;
using FSO.Content;
using FSO.Files.Formats.IFF.Chunks;
using FSO.Files.RC;
using FSO.LotView;
using FSO.LotView.Effects;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.Client.VR
{
    /// <summary>
    /// 3D pictures of catalogue objects for the VR catalogue tablet: each object's 3D mesh (the same one the lot draws,
    /// from FreeSO's mesh reconstruction) lit evenly and seen from where the game's thumbnails look from, plus a
    /// turning view of the one you point at.
    ///
    /// Pictures are asked for while the UI updates (<see cref="Get"/>) and drawn on the next frame
    /// (<see cref="Render"/>), a couple per frame so a page of new items doesn't cause a hitch. A mesh that FreeSO is
    /// still reconstructing is tried again until it's ready; objects without one keep the catalogue's 2D picture.
    /// </summary>
    public sealed class VRCatalogThumbnails : IDisposable
    {
        public const int Size = 192;
        private const int PerFrame = 2, MaxCached = 64, GiveUpFrames = 900, ForgetFrames = 3;
        /// <summary>The direction (from the object) the pictures are taken from: FreeSO's own object thumbnails.</summary>
        private static readonly Vector3 ViewFrom = Vector3.Normalize(new Vector3(-9, 6, -9));
        private const float FieldOfView = MathHelper.Pi / 6, LevelHeight = 2.95f * 3;
        private static readonly BlendState PremultiplyOver = new BlendState
        {
            ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.InverseSourceAlpha,
            AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.InverseSourceAlpha
        };

        private sealed class Entry
        {
            public RenderTarget2D Picture;
            public bool Failed;
            public int LastWanted, FirstWanted;
        }

        /// <summary>A mesh of an object and where it sits (the lot's 3D units, the object's tile at the origin, facing north).</summary>
        public struct Part
        {
            public DGRP3DMesh Mesh;
            public Matrix World;
        }

        /// <summary>The meshes that make up an object (by GUID), or null if it has none: <see cref="GameMeshes"/>.</summary>
        public Func<uint, Part[]> Meshes = GameMeshes;

        private readonly GraphicsDevice Device;
        private readonly Dictionary<uint, Entry> Cache = new Dictionary<uint, Entry>();
        private readonly List<uint> Pending = new List<uint>();
        private readonly Dictionary<uint, Part[]> Parts = new Dictionary<uint, Part[]>();
        private readonly List<uint> Evict = new List<uint>();
        private RenderTarget2D Work, Spin;
        private SpriteBatch Batch;
        private Texture2D White;
        private int Frame;
        private uint SpinWanted, SpinShown;
        private int SpinWantedFrame;
        private float SpinAngle;
        private bool Broken;

        public VRCatalogThumbnails(GraphicsDevice device)
        {
            Device = device;
        }

        /// <summary>
        /// The 3D picture of an object (or, with <paramref name="turning"/>, a view of it turning), or null if it isn't
        /// ready (yet, or ever). Asking is what keeps a picture: ones not asked for in a while are let go.
        /// </summary>
        public Texture2D Get(uint guid, bool turning)
        {
            if (Broken || guid == 0) return null;
            if (turning)
            {
                if (SpinWanted != guid) SpinAngle = 0;
                SpinWanted = guid;
                SpinWantedFrame = Frame;
                if (SpinShown == guid && Spin != null) return Spin;
            }
            if (Cache.TryGetValue(guid, out var entry))
            {
                entry.LastWanted = Frame;
                return entry.Failed ? null : entry.Picture;
            }
            Cache[guid] = new Entry { LastWanted = Frame, FirstWanted = Frame };
            Pending.Add(guid);
            return null;
        }

        /// <summary>Draws pictures that were asked for. Call once per frame on the game thread, between the world's passes and the UI.</summary>
        public void Render()
        {
            Frame++;
            if (Broken) return;
            var spin = SpinWanted != 0 && Frame - SpinWantedFrame <= ForgetFrames;
            if (!spin) { SpinWanted = SpinShown = 0; }
            if (Pending.Count == 0 && !spin)
            {
                Trim();
                return;
            }

            var gd = Device;
            var targets = gd.GetRenderTargets();
            var viewport = gd.Viewport;
            var blend = gd.BlendState;
            var depth = gd.DepthStencilState;
            var raster = gd.RasterizerState;
            var effect = WorldContent.RCObject;
            var saved = SaveEffect(effect);
            try
            {
                Work ??= new RenderTarget2D(gd, Size * 2, Size * 2, false, SurfaceFormat.Color, DepthFormat.Depth24);
                Batch ??= new SpriteBatch(gd);
                if (White == null)
                {
                    White = new Texture2D(gd, 1, 1);
                    White.SetData(new[] { Color.White });
                }
                // even, white light (no light map), as the game's own thumbnails
                effect.AdvancedLight = White;
                effect.LightingAdjust = Vector3.One;
                FSO.LotView.Model.Blueprint.SetLightColor(effect, Color.White, Color.White);
                effect.Level = 0;
                effect.SetTechnique(RCObjectTechniques.Draw);

                int drawn = 0;
                for (int i = 0; i < Pending.Count && drawn < PerFrame;)
                {
                    var guid = Pending[i];
                    var entry = Cache[guid];
                    if (Frame - entry.LastWanted > ForgetFrames)
                    {
                        // no longer on the tablet: ask again when it is
                        Pending.RemoveAt(i);
                        Cache.Remove(guid);
                        continue;
                    }
                    var parts = GetParts(guid);
                    if (parts == null) { entry.Failed = true; Pending.RemoveAt(i); continue; }
                    if (!Ready(parts))
                    {
                        if (Frame - entry.FirstWanted > GiveUpFrames) { entry.Failed = true; Pending.RemoveAt(i); }
                        else i++;
                        continue;
                    }
                    entry.Picture ??= new RenderTarget2D(gd, Size, Size, false, SurfaceFormat.Color, DepthFormat.None);
                    Draw(gd, effect, parts, 0, entry.Picture);
                    Pending.RemoveAt(i);
                    drawn++;
                }

                if (spin)
                {
                    var parts = GetParts(SpinWanted);
                    if (parts != null && Ready(parts))
                    {
                        SpinAngle += SimitoneVR.FrameDelta * MathHelper.ToRadians(50);
                        Spin ??= new RenderTarget2D(gd, Size, Size, false, SurfaceFormat.Color, DepthFormat.None);
                        Draw(gd, effect, parts, SpinAngle, Spin);
                        SpinShown = SpinWanted;
                    }
                }
            }
            catch (Exception e)
            {
                // (pictures are a nicety: the catalogue keeps its 2D ones)
                Broken = true;
                SimitoneVR.Log("catalogue 3D pictures stopped: " + e);
            }
            finally
            {
                RestoreEffect(effect, saved);
                if (targets.Length > 0) gd.SetRenderTargets(targets);
                else gd.SetRenderTarget(null);
                gd.Viewport = viewport;
                gd.BlendState = blend;
                gd.DepthStencilState = depth;
                gd.RasterizerState = raster;
            }
            Trim();
        }

        /// <summary>Lets go of pictures that haven't been asked for in a while, when there are many.</summary>
        private void Trim()
        {
            if (Cache.Count <= MaxCached) return;
            Evict.Clear();
            foreach (var pair in Cache)
                if (Frame - pair.Value.LastWanted > 30 && !Pending.Contains(pair.Key)) Evict.Add(pair.Key);
            Evict.Sort((a, b) => Cache[a].LastWanted.CompareTo(Cache[b].LastWanted));
            for (int i = 0; i < Evict.Count && Cache.Count > MaxCached; i++)
            {
                Cache[Evict[i]].Picture?.Dispose();
                Cache.Remove(Evict[i]);
                Parts.Remove(Evict[i]);
            }
        }

        private Part[] GetParts(uint guid)
        {
            if (Parts.TryGetValue(guid, out var parts)) return parts;
            parts = Meshes(guid);
            Parts[guid] = parts;
            return parts;
        }

        /// <summary>
        /// The meshes that make up a catalogue object, where they sit facing north: one, or one per tile of a multi-tile
        /// object (its parts are the object definitions sharing its master ID). Null if it has none.
        /// </summary>
        public static Part[] GameMeshes(uint guid)
        {
            var obj = Content.Get().WorldObjects.Get(guid);
            var list = new List<Part>();
            if (obj != null)
            {
                var master = obj.OBJ.MasterID;
                if (master != 0 && obj.OBJ.SubIndex == -1)
                {
                    foreach (var objd in obj.Resource.List<OBJD>())
                    {
                        if (objd.MasterID != master || objd.SubIndex == -1) continue;
                        var sub = Content.Get().WorldObjects.Get(objd.GUID);
                        AddPart(list, sub?.Resource ?? obj.Resource, objd,
                            (sbyte)((ushort)objd.SubIndex >> 8), (sbyte)((ushort)objd.SubIndex & 0xFF), (sbyte)objd.LevelOffset);
                    }
                }
                else AddPart(list, obj.Resource, obj.OBJ, 0, 0, 0);
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        private static void AddPart(List<Part> list, GameObjectResource resource, OBJD objd, int x, int y, int level)
        {
            if (objd.BaseGraphicID <= 0) return;
            var dgrp = resource.Get<DGRP>(objd.BaseGraphicID);
            if (dgrp == null) return;
            var mesh = Content.Get().RCMeshes.Get(dgrp, objd); // (reconstructed in the background if it isn't cached)
            if (mesh == null) return;
            // as the lot places a north-facing object on a tile (ObjectComponent.World3D)
            list.Add(new Part
            {
                Mesh = mesh,
                World = Matrix.CreateScale(3f) * Matrix.CreateTranslation(1.5f + x * 3f, 0.1f + level * LevelHeight, 1.5f + y * 3f)
            });
        }

        /// <summary>Whether every part's mesh is finished (FreeSO reconstructs them on worker threads).</summary>
        private static bool Ready(Part[] parts)
        {
            foreach (var part in parts)
            {
                var mesh = part.Mesh;
                if (mesh.Bounds == null || mesh.Geoms.Count == 0) return false;
                foreach (var geom in mesh.Geoms[0].Values)
                    if (geom.PrimCount > 0 && !geom.Rendered) return false;
            }
            return true;
        }

        /// <summary>Draws the object, turned <paramref name="angle"/> about its middle, filling <paramref name="into"/>.</summary>
        private void Draw(GraphicsDevice gd, RCObjectEffect effect, Part[] parts, float angle, RenderTarget2D into)
        {
            // what to frame: all the parts' bounds
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var part in parts)
            {
                foreach (var corner in part.Mesh.Bounds.Value.GetCorners())
                {
                    var p = Vector3.Transform(corner, part.World);
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }
            var centre = (min + max) / 2;
            var radius = Math.Max(0.3f, (max - min).Length() / 2);
            var distance = radius / (float)Math.Sin(FieldOfView / 2) * 1.02f;
            var from = Vector3.Transform(ViewFrom, Matrix.CreateRotationY(angle));
            var view = Matrix.CreateLookAt(centre + from * distance, centre, Vector3.Up);
            var projection = Matrix.CreatePerspectiveFieldOfView(FieldOfView, 1, Math.Max(0.01f, distance - radius * 1.5f), distance + radius * 1.5f);

            // drawn at twice the size, then halved (smooth edges)
            gd.SetRenderTarget(Work);
            gd.Clear(Color.Transparent);
            gd.BlendState = PremultiplyOver;
            gd.DepthStencilState = DepthStencilState.Default;
            gd.RasterizerState = RasterizerState.CullNone;
            effect.ViewProjection = view * projection;
            foreach (var part in parts)
            {
                effect.World = part.World;
                foreach (var geom in part.Mesh.Geoms[0].Values) // (the base sprites: not the dynamic ones, e.g. a TV's picture)
                {
                    if (geom.PrimCount == 0 || !geom.Rendered || geom.Verts == null) continue;
                    effect.MeshTex = geom.Pixel;
                    effect.UVScale = (geom.Pixel?.Tag as FSO.Common.Rendering.TextureInfo)?.UVScale ?? Vector2.One;
                    effect.CurrentTechnique.Passes[0].Apply();
                    gd.Indices = geom.Indices;
                    gd.SetVertexBuffer(geom.Verts);
                    gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, geom.PrimCount);
                }
            }

            gd.SetRenderTarget(into);
            gd.Clear(Color.Transparent);
            Batch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
            Batch.Draw(Work, new Rectangle(0, 0, Size, Size), Color.White);
            Batch.End();
        }

        // The effect is shared with the lot, which sets most of these per draw, but not its lighting: put it all back.
        private struct SavedEffect
        {
            public Texture2D AdvancedLight;
            public Vector3 LightingAdjust;
            public Vector4 OutsideDark;
            public Vector2 MinAvg;
            public Matrix World, ViewProjection;
            public float Level;
            public EffectTechnique Technique;
        }

        private static SavedEffect SaveEffect(RCObjectEffect effect)
        {
            var p = effect.Parameters;
            return new SavedEffect
            {
                AdvancedLight = p["advancedLight"]?.GetValueTexture2D(),
                LightingAdjust = p["LightingAdjust"]?.GetValueVector3() ?? Vector3.One,
                OutsideDark = p["OutsideDark"]?.GetValueVector4() ?? Vector4.One,
                MinAvg = p["MinAvg"]?.GetValueVector2() ?? Vector2.One,
                World = p["World"]?.GetValueMatrix() ?? Matrix.Identity,
                ViewProjection = p["ViewProjection"]?.GetValueMatrix() ?? Matrix.Identity,
                Level = p["Level"]?.GetValueSingle() ?? 0,
                Technique = effect.CurrentTechnique
            };
        }

        private static void RestoreEffect(RCObjectEffect effect, SavedEffect saved)
        {
            var p = effect.Parameters;
            p["advancedLight"]?.SetValue(saved.AdvancedLight);
            p["LightingAdjust"]?.SetValue(saved.LightingAdjust);
            p["OutsideDark"]?.SetValue(saved.OutsideDark);
            p["MinAvg"]?.SetValue(saved.MinAvg);
            p["World"]?.SetValue(saved.World);
            p["ViewProjection"]?.SetValue(saved.ViewProjection);
            p["Level"]?.SetValue(saved.Level);
            if (saved.Technique != null) effect.CurrentTechnique = saved.Technique;
        }

        public void Dispose()
        {
            foreach (var entry in Cache.Values) entry.Picture?.Dispose();
            Cache.Clear();
            Pending.Clear();
            Parts.Clear();
            Work?.Dispose();
            Spin?.Dispose();
            Batch?.Dispose();
            White?.Dispose();
            Work = Spin = null;
            Batch = null;
            White = null;
        }
    }
}
