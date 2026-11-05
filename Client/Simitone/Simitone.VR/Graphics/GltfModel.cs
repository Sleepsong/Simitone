using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.VR.Graphics
{
    /// <summary>
    /// A glTF 2.0 binary (GLB) model as the runtime gives controllers (XR_EXT_interaction_render_model): meshes of
    /// triangles with base colour (factor and texture), embedded buffers and images, a node tree, and no required
    /// glTF extensions. Parsing (<see cref="GltfAsset.Parse"/>) is plain CPU work and can run on any thread; making
    /// the GPU resources (<see cref="GltfModel"/>) must happen on the game thread.
    /// </summary>
    public sealed class GltfAsset
    {
        public class Node
        {
            public string Name;
            public Matrix Local = Matrix.Identity;
            public int[] Children = new int[0];
            public int Mesh = -1;
        }

        public class Part
        {
            public VertexPositionNormalTexture[] Vertices;
            public int[] Indices;
            public int Material = -1;
        }

        public class Material
        {
            public Vector4 BaseColor = Vector4.One;
            public int Image = -1;          // index into Images, or -1
            public Vector3 Emissive;
            public bool Blend;              // alphaMode BLEND or MASK: drawn after the opaque parts, alpha blended
        }

        public Node[] Nodes;
        public List<Part>[] Meshes;
        public Material[] Materials;
        /// <summary>Encoded images (PNG or JPEG), decoded when the GPU model is made.</summary>
        public byte[][] Images;
        public int[] Roots;

        private const uint Magic = 0x46546C67, JsonChunk = 0x4E4F534A, BinChunk = 0x004E4942;

        public static GltfAsset Parse(byte[] glb)
        {
            if (glb == null || glb.Length < 20) throw new InvalidDataException("not a glTF binary");
            if (BitConverter.ToUInt32(glb, 0) != Magic) throw new InvalidDataException("not a glTF binary (bad magic)");
            if (BitConverter.ToUInt32(glb, 4) != 2) throw new InvalidDataException("glTF version " + BitConverter.ToUInt32(glb, 4));
            var length = Math.Min((int)BitConverter.ToUInt32(glb, 8), glb.Length);
            string json = null;
            byte[] bin = null;
            for (int at = 12; at + 8 <= length;)
            {
                var chunkLength = (int)BitConverter.ToUInt32(glb, at);
                var chunkType = BitConverter.ToUInt32(glb, at + 4);
                if (at + 8 + chunkLength > length) throw new InvalidDataException("truncated glTF chunk");
                if (chunkType == JsonChunk && json == null) json = Encoding.UTF8.GetString(glb, at + 8, chunkLength);
                else if (chunkType == BinChunk && bin == null)
                {
                    bin = new byte[chunkLength];
                    Buffer.BlockCopy(glb, at + 8, bin, 0, chunkLength);
                }
                at += 8 + ((chunkLength + 3) & ~3);
            }
            if (json == null) throw new InvalidDataException("glTF binary without JSON");
            using (var doc = JsonDocument.Parse(json)) return new Reader(doc.RootElement, bin).Read();
        }

        private class Reader
        {
            private readonly JsonElement Root;
            private readonly byte[] Bin;
            private readonly List<byte[]> Buffers = new List<byte[]>();

            public Reader(JsonElement root, byte[] bin)
            {
                Root = root;
                Bin = bin;
            }

            private JsonElement Array(string name) => Root.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a : default;
            private static int Count(JsonElement array) => array.ValueKind == JsonValueKind.Array ? array.GetArrayLength() : 0;
            private static int Int(JsonElement e, string name, int fallback) => e.TryGetProperty(name, out var v) ? v.GetInt32() : fallback;
            private static float[] Floats(JsonElement e, string name)
            {
                if (!e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array) return null;
                var result = new float[v.GetArrayLength()];
                int i = 0;
                foreach (var x in v.EnumerateArray()) result[i++] = x.GetSingle();
                return result;
            }

            public GltfAsset Read()
            {
                if (Root.TryGetProperty("extensionsRequired", out var required) && Count(required) > 0)
                    throw new InvalidDataException("the model needs glTF extensions: " + required.GetRawText());
                foreach (var buffer in EnumerateOrEmpty(Array("buffers"))) Buffers.Add(LoadBuffer(buffer));

                var asset = new GltfAsset();
                // nodes
                var nodes = Array("nodes");
                asset.Nodes = new Node[Count(nodes)];
                int n = 0;
                foreach (var e in EnumerateOrEmpty(nodes))
                {
                    var node = new Node
                    {
                        Name = e.TryGetProperty("name", out var name) ? name.GetString() : null,
                        Mesh = Int(e, "mesh", -1)
                    };
                    if (e.TryGetProperty("children", out var children))
                    {
                        node.Children = new int[children.GetArrayLength()];
                        int c = 0;
                        foreach (var child in children.EnumerateArray()) node.Children[c++] = child.GetInt32();
                    }
                    var m = Floats(e, "matrix");
                    if (m != null && m.Length == 16)
                        // glTF matrices are column-major for column vectors: the same numbers, in order, are XNA's
                        // row-major matrix for row vectors
                        node.Local = new Matrix(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
                    else
                    {
                        var s = Floats(e, "scale");
                        var r = Floats(e, "rotation");
                        var t = Floats(e, "translation");
                        node.Local = (s != null ? Matrix.CreateScale(s[0], s[1], s[2]) : Matrix.Identity)
                            * (r != null ? Matrix.CreateFromQuaternion(new Quaternion(r[0], r[1], r[2], r[3])) : Matrix.Identity)
                            * (t != null ? Matrix.CreateTranslation(t[0], t[1], t[2]) : Matrix.Identity);
                    }
                    asset.Nodes[n++] = node;
                }

                // the scene to draw: the default one, else the first, else every node that isn't a child
                var scenes = Array("scenes");
                var sceneIndex = Int(Root, "scene", 0);
                if (Count(scenes) > sceneIndex && scenes[sceneIndex].TryGetProperty("nodes", out var sceneNodes))
                {
                    asset.Roots = new int[sceneNodes.GetArrayLength()];
                    int i = 0;
                    foreach (var r in sceneNodes.EnumerateArray()) asset.Roots[i++] = r.GetInt32();
                }
                else
                {
                    var isChild = new bool[asset.Nodes.Length];
                    foreach (var node in asset.Nodes) foreach (var c in node.Children) isChild[c] = true;
                    var roots = new List<int>();
                    for (int i = 0; i < isChild.Length; i++) if (!isChild[i]) roots.Add(i);
                    asset.Roots = roots.ToArray();
                }

                // materials
                var materials = Array("materials");
                var textures = Array("textures");
                asset.Materials = new Material[Count(materials)];
                int mi = 0;
                foreach (var e in EnumerateOrEmpty(materials))
                {
                    var material = new Material();
                    if (e.TryGetProperty("pbrMetallicRoughness", out var pbr))
                    {
                        var factor = Floats(pbr, "baseColorFactor");
                        if (factor != null && factor.Length == 4) material.BaseColor = new Vector4(factor[0], factor[1], factor[2], factor[3]);
                        if (pbr.TryGetProperty("baseColorTexture", out var texRef))
                        {
                            var texture = Int(texRef, "index", -1);
                            if (texture >= 0 && texture < Count(textures)) material.Image = Int(textures[texture], "source", -1);
                        }
                    }
                    var emissive = Floats(e, "emissiveFactor");
                    if (emissive != null && emissive.Length == 3) material.Emissive = new Vector3(emissive[0], emissive[1], emissive[2]);
                    if (e.TryGetProperty("alphaMode", out var alphaMode)) material.Blend = alphaMode.GetString() != "OPAQUE";
                    asset.Materials[mi++] = material;
                }

                // images (embedded: in a buffer view, or a data: URI)
                var images = Array("images");
                asset.Images = new byte[Count(images)][];
                int ii = 0;
                foreach (var e in EnumerateOrEmpty(images))
                {
                    byte[] bytes = null;
                    var view = Int(e, "bufferView", -1);
                    if (view >= 0)
                    {
                        var (data, offset, len, _) = View(view);
                        bytes = new byte[len];
                        Buffer.BlockCopy(data, offset, bytes, 0, len);
                    }
                    else if (e.TryGetProperty("uri", out var uri)) bytes = DataUri(uri.GetString());
                    asset.Images[ii++] = bytes;
                }

                // meshes: triangle primitives
                var meshes = Array("meshes");
                asset.Meshes = new List<Part>[Count(meshes)];
                int me = 0;
                foreach (var e in EnumerateOrEmpty(meshes))
                {
                    var parts = new List<Part>();
                    if (e.TryGetProperty("primitives", out var primitives))
                    {
                        foreach (var p in primitives.EnumerateArray())
                        {
                            var part = ReadPrimitive(p);
                            if (part != null) parts.Add(part);
                        }
                    }
                    asset.Meshes[me++] = parts;
                }
                return asset;
            }

            private static IEnumerable<JsonElement> EnumerateOrEmpty(JsonElement array)
            {
                if (array.ValueKind != JsonValueKind.Array) yield break;
                foreach (var e in array.EnumerateArray()) yield return e;
            }

            private Part ReadPrimitive(JsonElement p)
            {
                var mode = Int(p, "mode", 4);
                if (mode != 4 || !p.TryGetProperty("attributes", out var attributes)) return null; // triangles only
                if (!attributes.TryGetProperty("POSITION", out var positionAccessor)) return null;
                var positions = ReadAccessor(positionAccessor.GetInt32(), out var pc);
                if (pc != 3) return null;
                var count = positions.Length / 3;
                float[] normals = null, uvs = null;
                if (attributes.TryGetProperty("NORMAL", out var na)) { normals = ReadAccessor(na.GetInt32(), out var c); if (c != 3) normals = null; }
                if (attributes.TryGetProperty("TEXCOORD_0", out var ta)) { uvs = ReadAccessor(ta.GetInt32(), out var c); if (c != 2) uvs = null; }

                int[] indices;
                if (p.TryGetProperty("indices", out var ia))
                {
                    var raw = ReadAccessor(ia.GetInt32(), out _, integers: true);
                    indices = new int[raw.Length];
                    for (int i = 0; i < raw.Length; i++) indices[i] = (int)raw[i];
                }
                else
                {
                    indices = new int[count];
                    for (int i = 0; i < count; i++) indices[i] = i;
                }
                if (indices.Length % 3 != 0) System.Array.Resize(ref indices, indices.Length - indices.Length % 3);
                foreach (var index in indices) if (index < 0 || index >= count) throw new InvalidDataException("glTF index out of range");

                var vertices = new VertexPositionNormalTexture[count];
                for (int i = 0; i < count; i++)
                {
                    vertices[i].Position = new Vector3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
                    if (normals != null) vertices[i].Normal = new Vector3(normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2]);
                    if (uvs != null) vertices[i].TextureCoordinate = new Vector2(uvs[i * 2], uvs[i * 2 + 1]);
                }
                if (normals == null) ComputeNormals(vertices, indices);
                return new Part { Vertices = vertices, Indices = indices, Material = Int(p, "material", -1) };
            }

            private static void ComputeNormals(VertexPositionNormalTexture[] v, int[] indices)
            {
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    if (a >= v.Length || b >= v.Length || c >= v.Length) continue;
                    var n = Vector3.Cross(v[b].Position - v[a].Position, v[c].Position - v[a].Position);
                    v[a].Normal += n; v[b].Normal += n; v[c].Normal += n;
                }
                for (int i = 0; i < v.Length; i++)
                    v[i].Normal = v[i].Normal.LengthSquared() > 1e-20f ? Vector3.Normalize(v[i].Normal) : Vector3.Up;
            }

            private (byte[] data, int offset, int length, int stride) View(int index)
            {
                var view = Array("bufferViews")[index];
                var buffer = Buffers[Int(view, "buffer", 0)];
                var offset = Int(view, "byteOffset", 0);
                var length = Int(view, "byteLength", 0);
                if (buffer == null || offset < 0 || offset + length > buffer.Length) throw new InvalidDataException("glTF buffer view out of range");
                return (buffer, offset, length, Int(view, "byteStride", 0));
            }

            /// <summary>Reads an accessor as floats (normalised integers scaled to 0..1 / -1..1; plain integers as is).</summary>
            private float[] ReadAccessor(int index, out int components, bool integers = false)
            {
                var accessor = Array("accessors")[index];
                var type = accessor.GetProperty("type").GetString();
                components = type switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 0 };
                var componentType = accessor.GetProperty("componentType").GetInt32();
                var count = accessor.GetProperty("count").GetInt32();
                var normalized = accessor.TryGetProperty("normalized", out var nz) && nz.GetBoolean();
                var result = new float[count * components];
                var viewIndex = Int(accessor, "bufferView", -1);
                if (viewIndex < 0) return result; // (sparse-only accessors aren't used by controller models)
                var (data, viewOffset, viewLength, stride) = View(viewIndex);
                var size = componentType switch { 5120 => 1, 5121 => 1, 5122 => 2, 5123 => 2, 5125 => 4, 5126 => 4, _ => 0 };
                if (size == 0 || components == 0) throw new InvalidDataException("unsupported glTF accessor");
                var elementSize = size * components;
                if (stride == 0) stride = elementSize;
                var start = viewOffset + Int(accessor, "byteOffset", 0);
                if (count > 0 && start + (count - 1) * stride + elementSize > viewOffset + viewLength)
                    throw new InvalidDataException("glTF accessor out of range");
                for (int i = 0; i < count; i++)
                {
                    var at = start + i * stride;
                    for (int c = 0; c < components; c++)
                    {
                        var o = at + c * size;
                        float value = componentType switch
                        {
                            5126 => BitConverter.ToSingle(data, o),
                            5121 => normalized && !integers ? data[o] / 255f : data[o],
                            5120 => normalized && !integers ? Math.Max((sbyte)data[o] / 127f, -1f) : (sbyte)data[o],
                            5123 => normalized && !integers ? BitConverter.ToUInt16(data, o) / 65535f : BitConverter.ToUInt16(data, o),
                            5122 => normalized && !integers ? Math.Max(BitConverter.ToInt16(data, o) / 32767f, -1f) : BitConverter.ToInt16(data, o),
                            _ => BitConverter.ToUInt32(data, o),
                        };
                        result[i * components + c] = value;
                    }
                }
                return result;
            }

            private byte[] LoadBuffer(JsonElement buffer)
            {
                if (buffer.TryGetProperty("uri", out var uri)) return DataUri(uri.GetString());
                return Bin; // the GLB's own binary chunk
            }

            private static byte[] DataUri(string uri)
            {
                if (uri == null || !uri.StartsWith("data:")) throw new InvalidDataException("the model refers to an outside file");
                var comma = uri.IndexOf(',');
                if (comma < 0 || !uri.Substring(0, comma).EndsWith(";base64")) throw new InvalidDataException("unsupported data URI");
                return Convert.FromBase64String(uri.Substring(comma + 1));
            }
        }
    }

    /// <summary>
    /// A <see cref="GltfAsset"/> on the GPU, drawn with simple lighting. Nodes named in <see cref="SetAnimatedNodes"/>
    /// can be posed and hidden each draw, as XR_EXT_render_model animates them: the pose replaces the node's own
    /// transform, relative to its parent, and a hidden node hides everything under it.
    /// </summary>
    public sealed class GltfModel : IDisposable
    {
        private class GpuPart
        {
            public VertexBuffer Vertices;
            public IndexBuffer Indices;
            public int Primitives;
            public GltfAsset.Material Material;
            public Texture2D Texture;
        }

        private readonly GltfAsset Asset;
        private readonly List<GpuPart>[] Meshes;
        private readonly Texture2D[] Textures;
        private readonly BasicEffect Effect;
        private int[] AnimatedIndex = new int[0]; // node -> index into the pose arrays, or -1
        private readonly Matrix[] NodeWorld;
        private readonly List<(GpuPart part, Matrix world)> Transparent = new List<(GpuPart, Matrix)>();

        /// <summary>A sphere around the model at rest (its own units: metres), e.g. to frame it.</summary>
        public BoundingSphere Bounds { get; }

        public GltfModel(GraphicsDevice device, GltfAsset asset)
        {
            Asset = asset;
            Effect = new BasicEffect(device);
            Effect.EnableDefaultLighting();
            Effect.PreferPerPixelLighting = false;
            Effect.SpecularPower = 24;

            Textures = new Texture2D[asset.Images.Length];
            for (int i = 0; i < Textures.Length; i++)
            {
                if (asset.Images[i] == null) continue;
                try
                {
                    using (var stream = new MemoryStream(asset.Images[i])) Textures[i] = Texture2D.FromStream(device, stream);
                }
                catch (Exception)
                {
                    Textures[i] = null; // (e.g. an image format MonoGame can't read: the base colour alone is used)
                }
            }

            Meshes = new List<GpuPart>[asset.Meshes.Length];
            for (int m = 0; m < Meshes.Length; m++)
            {
                Meshes[m] = new List<GpuPart>();
                foreach (var part in asset.Meshes[m])
                {
                    if (part.Indices.Length < 3 || part.Vertices.Length == 0) continue;
                    var vb = new VertexBuffer(device, VertexPositionNormalTexture.VertexDeclaration, part.Vertices.Length, BufferUsage.WriteOnly);
                    vb.SetData(part.Vertices);
                    IndexBuffer ib;
                    if (part.Vertices.Length <= ushort.MaxValue)
                    {
                        var shorts = new ushort[part.Indices.Length];
                        for (int i = 0; i < shorts.Length; i++) shorts[i] = (ushort)part.Indices[i];
                        ib = new IndexBuffer(device, IndexElementSize.SixteenBits, shorts.Length, BufferUsage.WriteOnly);
                        ib.SetData(shorts);
                    }
                    else
                    {
                        ib = new IndexBuffer(device, IndexElementSize.ThirtyTwoBits, part.Indices.Length, BufferUsage.WriteOnly);
                        ib.SetData(part.Indices);
                    }
                    var material = part.Material >= 0 && part.Material < asset.Materials.Length ? asset.Materials[part.Material] : new GltfAsset.Material();
                    Meshes[m].Add(new GpuPart
                    {
                        Vertices = vb,
                        Indices = ib,
                        Primitives = part.Indices.Length / 3,
                        Material = material,
                        Texture = material.Image >= 0 && material.Image < Textures.Length ? Textures[material.Image] : null
                    });
                }
            }

            NodeWorld = new Matrix[asset.Nodes.Length];
            AnimatedIndex = new int[asset.Nodes.Length];
            for (int i = 0; i < AnimatedIndex.Length; i++) AnimatedIndex[i] = -1;
            Bounds = ComputeBounds();
        }

        /// <summary>Which nodes the pose arrays passed to <see cref="Draw"/> refer to, by name, in order.</summary>
        public void SetAnimatedNodes(string[] names)
        {
            for (int i = 0; i < AnimatedIndex.Length; i++) AnimatedIndex[i] = -1;
            if (names == null) return;
            for (int a = 0; a < names.Length; a++)
                for (int n = 0; n < Asset.Nodes.Length; n++)
                    if (Asset.Nodes[n].Name == names[a]) { AnimatedIndex[n] = a; break; }
        }

        /// <summary>
        /// Draws the model. <paramref name="world"/> maps its metres into the world; <paramref name="poses"/> and
        /// <paramref name="visible"/> (optional) pose and show the animated nodes.
        /// </summary>
        public void Draw(GraphicsDevice device, Matrix world, Matrix view, Matrix projection, Matrix[] poses = null, bool[] visible = null)
        {
            // (the states are put back afterwards, for whatever the game draws next)
            var oldBlend = device.BlendState;
            var oldDepth = device.DepthStencilState;
            var oldRaster = device.RasterizerState;
            var oldSampler = device.SamplerStates[0];
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.Default;
            device.RasterizerState = RasterizerState.CullNone;
            device.SamplerStates[0] = SamplerState.LinearWrap;
            Effect.View = view;
            Effect.Projection = projection;
            Transparent.Clear();
            foreach (var root in Asset.Roots) DrawNode(device, root, world, poses, visible);
            if (Transparent.Count > 0)
            {
                device.BlendState = BlendState.NonPremultiplied;
                device.DepthStencilState = DepthStencilState.DepthRead;
                foreach (var (part, w) in Transparent) DrawPart(device, part, w);
            }
            device.BlendState = oldBlend;
            device.DepthStencilState = oldDepth;
            device.RasterizerState = oldRaster;
            device.SamplerStates[0] = oldSampler;
        }

        private void DrawNode(GraphicsDevice device, int index, Matrix parent, Matrix[] poses, bool[] visible)
        {
            if (index < 0 || index >= Asset.Nodes.Length) return;
            var node = Asset.Nodes[index];
            var local = node.Local;
            var a = AnimatedIndex[index];
            if (a >= 0)
            {
                if (visible != null && a < visible.Length && !visible[a]) return; // hidden, with everything under it
                if (poses != null && a < poses.Length) local = poses[a];          // replaces, not composes with
            }
            var w = local * parent;
            NodeWorld[index] = w;
            if (node.Mesh >= 0 && node.Mesh < Meshes.Length)
                foreach (var part in Meshes[node.Mesh])
                {
                    if (part.Material.Blend) Transparent.Add((part, w));
                    else DrawPart(device, part, w);
                }
            foreach (var child in node.Children) DrawNode(device, child, w, poses, visible);
        }

        private void DrawPart(GraphicsDevice device, GpuPart part, Matrix world)
        {
            var m = part.Material;
            Effect.World = world;
            Effect.DiffuseColor = new Vector3(m.BaseColor.X, m.BaseColor.Y, m.BaseColor.Z);
            Effect.Alpha = m.BaseColor.W;
            Effect.EmissiveColor = m.Emissive;
            Effect.SpecularColor = new Vector3(0.15f);
            Effect.TextureEnabled = part.Texture != null;
            Effect.Texture = part.Texture;
            device.SetVertexBuffer(part.Vertices);
            device.Indices = part.Indices;
            foreach (var pass in Effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, part.Primitives);
            }
        }

        private BoundingSphere ComputeBounds()
        {
            var points = new List<Vector3>();
            void Visit(int index, Matrix parent)
            {
                if (index < 0 || index >= Asset.Nodes.Length) return;
                var node = Asset.Nodes[index];
                var w = node.Local * parent;
                if (node.Mesh >= 0 && node.Mesh < Asset.Meshes.Length)
                    foreach (var part in Asset.Meshes[node.Mesh])
                        foreach (var v in part.Vertices) points.Add(Vector3.Transform(v.Position, w));
                foreach (var c in node.Children) Visit(c, w);
            }
            foreach (var root in Asset.Roots) Visit(root, Matrix.Identity);
            return points.Count > 0 ? BoundingSphere.CreateFromPoints(points) : new BoundingSphere(Vector3.Zero, 0.1f);
        }

        public void Dispose()
        {
            foreach (var mesh in Meshes)
                foreach (var part in mesh)
                {
                    part.Vertices.Dispose();
                    part.Indices.Dispose();
                }
            foreach (var t in Textures) t?.Dispose();
            Effect.Dispose();
        }
    }
}
