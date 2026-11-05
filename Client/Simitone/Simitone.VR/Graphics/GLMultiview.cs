using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.VR.Graphics
{
    /// <summary>
    /// Rewrites a MonoGame (MojoShader) vertex shader so that one draw renders both eyes (GL_OVR_multiview2): the
    /// shader computes its position as usual, for a view that encloses both eyes, and each eye's image is then that
    /// position times the eye's clip-space delta (<see cref="VRMath.ClipDelta"/>), from a uniform block. Everything
    /// else the shader computes is unchanged. Attribute locations are pinned to the original program's, since
    /// MonoGame caches them per shader. Returns null if the shader doesn't look like MonoGame's.
    /// </summary>
    public static class MultiviewShaderPatch
    {
        public const int StereoBinding = 3;
        private const string FixupLine = "gl_Position.y = gl_Position.y * posFixup.y;";
        private static readonly Regex Attribute = new Regex(@"^\s*attribute\s+(\w+)\s+(\w+)\s*;\s*$");

        public static string Patch(string glsl, IReadOnlyDictionary<string, int> locations)
        {
            if (string.IsNullOrEmpty(glsl) || glsl.Contains("#version")) return null;
            var lines = glsl.Replace("\r", "").Split('\n');
            var extensions = new List<string>();
            var body = new StringBuilder();
            var inserted = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("#extension")) { extensions.Add(trimmed); continue; } // (moved to the top)
                var m = Attribute.Match(line);
                if (m.Success && locations != null && locations.TryGetValue(m.Groups[2].Value, out var location) && location >= 0)
                {
                    body.Append($"layout(location = {location}) in {m.Groups[1].Value} {m.Groups[2].Value};\n");
                    continue;
                }
                if (!inserted && trimmed == FixupLine)
                {
                    // (before MonoGame's own clip-space fix-ups, which are linear and the same for both eyes)
                    body.Append("\tgl_Position += simitone_stereo_delta[int(gl_ViewID_OVR)] * gl_Position;\n");
                    inserted = true;
                }
                body.Append(line).Append('\n');
            }
            if (!inserted) return null;

            var header = new StringBuilder();
            header.Append("#version 130\n");
            header.Append("#extension GL_OVR_multiview2 : require\n");
            header.Append("#extension GL_ARB_uniform_buffer_object : require\n");
            header.Append("#extension GL_ARB_shading_language_420pack : require\n");
            header.Append("#extension GL_ARB_explicit_attrib_location : require\n");
            foreach (var e in extensions) header.Append(e).Append('\n');
            header.Append("layout(num_views = 2) in;\n");
            // (each eye's delta minus identity: an unset block reads as zeros, i.e. no change)
            header.Append($"layout(std140, binding = {StereoBinding}) uniform SimitoneStereo {{ mat4 simitone_stereo_delta[2]; }};\n");
            return header.ToString() + body;
        }
    }

    /// <summary>
    /// Single-pass stereo for FreeSO's renderer: the lot is drawn once into a two-layer framebuffer
    /// (GL_OVR_multiview2) instead of once per eye, which roughly halves the CPU cost of drawing it (and the vertex
    /// work). Neither FreeSO's compiled effects nor MonoGame know about multiview, so this works underneath them:
    ///  - every vertex shader MonoGame has (FreeSO's effects and its own) gets a second, multiview variant
    ///    (<see cref="MultiviewShaderPatch"/>), compiled here;
    ///  - while a stereo pass runs (<see cref="Begin"/> to <see cref="End"/>), MonoGame's shaders use those variants,
    ///    its program cache is swapped for a second one (so multiview programs are linked and kept separately), and
    ///    the layered framebuffer is bound under the render target MonoGame thinks it is drawing to;
    ///  - afterwards each layer is copied (colour and depth) into its eye's render target.
    /// This reaches into MonoGame 3.8 DesktopGL's internals by reflection; anything missing (fields, GL extensions,
    /// a shader that won't compile) means <see cref="TryCreate"/> or <see cref="PrepareShaders"/> says no, and the
    /// caller draws each eye separately as before.
    /// </summary>
    public sealed unsafe class GLMultiview : IDisposable
    {
        public sealed class Target
        {
            public int Width, Height;
            internal uint Color, Depth, Fbo;
            internal readonly uint[] LayerFbo = new uint[2];
        }

        private class Variant
        {
            public int Handle;
        }

        private readonly GraphicsDevice Device;
        private readonly Action<string> Log;
        private readonly object MultiviewCache;
        private object NormalCache;
        private readonly ConditionalWeakTable<object, Variant> Variants = new ConditionalWeakTable<object, Variant>();
        private readonly List<WeakReference> Patched = new List<WeakReference>();
        private readonly HashSet<object> Failed = new HashSet<object>(ReferenceEqualityComparer.Instance);
        private readonly List<(object shader, object handle)> Swapped = new List<(object, object)>();
        private uint Ubo;
        private int SavedDraw, SavedRead;
        private bool InPass;
        private int TrivialFragment;

        // MonoGame internals
        private static Type ShaderType;
        private static FieldInfo ShaderHandle, ShaderGlsl, ResourcesField, ResourcesLock, ProgramCacheField, ShaderProgramField,
            VertexDirty, PixelDirty, AttributeName;
        private static PropertyInfo ShaderStage, ShaderAttributes;

        // GL
        private const uint GL_TEXTURE_2D_ARRAY = 0x8C1A, GL_TEXTURE_BINDING_2D_ARRAY = 0x8C1D, GL_RGBA8 = 0x8058, GL_RGBA = 0x1908,
            GL_UNSIGNED_BYTE = 0x1401, GL_DEPTH24_STENCIL8 = 0x88F0, GL_DEPTH_STENCIL = 0x84F9, GL_UNSIGNED_INT_24_8 = 0x84FA,
            GL_TEXTURE_MIN_FILTER = 0x2801, GL_TEXTURE_MAG_FILTER = 0x2800, GL_NEAREST = 0x2600, GL_FRAMEBUFFER = 0x8D40,
            GL_DRAW_FRAMEBUFFER = 0x8CA9, GL_READ_FRAMEBUFFER = 0x8CA8, GL_DRAW_FRAMEBUFFER_BINDING = 0x8CA6,
            GL_READ_FRAMEBUFFER_BINDING = 0x8CAA, GL_COLOR_ATTACHMENT0 = 0x8CE0, GL_DEPTH_STENCIL_ATTACHMENT = 0x821A,
            GL_FRAMEBUFFER_COMPLETE = 0x8CD5, GL_UNIFORM_BUFFER = 0x8A11, GL_UNIFORM_BUFFER_BINDING = 0x8A28, GL_DYNAMIC_DRAW = 0x88E8,
            GL_VERTEX_SHADER = 0x8B31, GL_FRAGMENT_SHADER = 0x8B30, GL_COMPILE_STATUS = 0x8B81, GL_LINK_STATUS = 0x8B82,
            GL_INFO_LOG_LENGTH = 0x8B84, GL_EXTENSIONS = 0x1F03, GL_NUM_EXTENSIONS = 0x821D, GL_COLOR_BUFFER_BIT = 0x4000,
            GL_DEPTH_BUFFER_BIT = 0x100, GL_STENCIL_BUFFER_BIT = 0x400, GL_SCISSOR_TEST = 0x0C11, GL_FRAMEBUFFER_SRGB = 0x8DB9;

        private static delegate* unmanaged<uint, int*, void> glGetIntegerv;
        private static delegate* unmanaged<uint, uint, byte*> glGetStringi;
        private static delegate* unmanaged<int, uint*, void> glGenTextures, glDeleteTextures, glGenFramebuffers, glDeleteFramebuffers, glGenBuffers, glDeleteBuffers;
        private static delegate* unmanaged<uint, uint, void> glBindTexture, glBindFramebuffer, glBindBuffer;
        private static delegate* unmanaged<uint, int, int, int, int, int, int, uint, uint, void*, void> glTexImage3D;
        private static delegate* unmanaged<uint, uint, int, void> glTexParameteri;
        private static delegate* unmanaged<uint, uint, uint, int, int, int, void> glFramebufferTextureMultiviewOVR;
        private static delegate* unmanaged<uint, uint, uint, int, int, void> glFramebufferTextureLayer;
        private static delegate* unmanaged<uint, uint> glCheckFramebufferStatus;
        private static delegate* unmanaged<uint, nint, void*, uint, void> glBufferData;
        private static delegate* unmanaged<uint, nint, nint, void*, void> glBufferSubData;
        private static delegate* unmanaged<uint, uint, uint, void> glBindBufferBase;
        private static delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void> glBlitFramebuffer;
        private static delegate* unmanaged<uint, uint> glCreateShader;
        private static delegate* unmanaged<uint, int, byte**, int*, void> glShaderSource;
        private static delegate* unmanaged<uint, void> glCompileShader, glDeleteShader, glLinkProgram, glDeleteProgram;
        private static delegate* unmanaged<uint, uint, int*, void> glGetShaderiv, glGetProgramiv;
        private static delegate* unmanaged<uint, int, int*, byte*, void> glGetShaderInfoLog, glGetProgramInfoLog;
        private static delegate* unmanaged<uint> glCreateProgram;
        private static delegate* unmanaged<uint, uint, void> glAttachShader, glDetachShader;
        private static delegate* unmanaged<uint, byte*, int> glGetAttribLocation;
        private static delegate* unmanaged<uint, byte> glIsEnabled;
        private static delegate* unmanaged<uint, void> glEnable, glDisable;
        private static delegate* unmanaged<uint> glGetError;

        public static string Unavailable { get; private set; }

        private GLMultiview(GraphicsDevice device, Action<string> log)
        {
            Device = device;
            Log = log ?? (s => { });
            var cacheType = ShaderType.Assembly.GetType("Microsoft.Xna.Framework.Graphics.ShaderProgramCache");
            MultiviewCache = Activator.CreateInstance(cacheType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { device }, null);
            uint id;
            glGenBuffers(1, &id);
            Ubo = id;
            glBindBuffer(GL_UNIFORM_BUFFER, Ubo);
            glBufferData(GL_UNIFORM_BUFFER, 128, null, GL_DYNAMIC_DRAW);
            glBindBuffer(GL_UNIFORM_BUFFER, 0);
        }

        /// <summary>Single-pass stereo if the GL driver and this MonoGame can do it; null (see <see cref="Unavailable"/>) otherwise.</summary>
        public static GLMultiview TryCreate(GraphicsDevice device, Action<string> log)
        {
            try
            {
                GLInterop.Load();
                if (!LoadGL(out var missing)) { Unavailable = "the OpenGL driver lacks " + missing; return null; }
                var extensions = Extensions();
                foreach (var needed in new[] { "GL_OVR_multiview2", "GL_ARB_uniform_buffer_object", "GL_ARB_shading_language_420pack", "GL_ARB_explicit_attrib_location" })
                    if (!extensions.Contains(needed)) { Unavailable = "the OpenGL driver lacks " + needed; return null; }
                if (!FindMonoGameInternals(out var what)) { Unavailable = "this MonoGame build is different (" + what + ")"; return null; }
                return new GLMultiview(device, log);
            }
            catch (Exception e)
            {
                Unavailable = e.Message;
                return null;
            }
        }

        private static bool LoadGL(out string missing)
        {
            missing = null;
            IntPtr F(string name, ref string miss)
            {
                var p = GLInterop.TryGetProc(name);
                if (p == IntPtr.Zero && miss == null) miss = name;
                return p;
            }
            glGetIntegerv = (delegate* unmanaged<uint, int*, void>)F("glGetIntegerv", ref missing);
            glGetStringi = (delegate* unmanaged<uint, uint, byte*>)F("glGetStringi", ref missing);
            glGenTextures = (delegate* unmanaged<int, uint*, void>)F("glGenTextures", ref missing);
            glDeleteTextures = (delegate* unmanaged<int, uint*, void>)F("glDeleteTextures", ref missing);
            glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)F("glGenFramebuffers", ref missing);
            glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)F("glDeleteFramebuffers", ref missing);
            glGenBuffers = (delegate* unmanaged<int, uint*, void>)F("glGenBuffers", ref missing);
            glDeleteBuffers = (delegate* unmanaged<int, uint*, void>)F("glDeleteBuffers", ref missing);
            glBindTexture = (delegate* unmanaged<uint, uint, void>)F("glBindTexture", ref missing);
            glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)F("glBindFramebuffer", ref missing);
            glBindBuffer = (delegate* unmanaged<uint, uint, void>)F("glBindBuffer", ref missing);
            glTexImage3D = (delegate* unmanaged<uint, int, int, int, int, int, int, uint, uint, void*, void>)F("glTexImage3D", ref missing);
            glTexParameteri = (delegate* unmanaged<uint, uint, int, void>)F("glTexParameteri", ref missing);
            glFramebufferTextureMultiviewOVR = (delegate* unmanaged<uint, uint, uint, int, int, int, void>)F("glFramebufferTextureMultiviewOVR", ref missing);
            glFramebufferTextureLayer = (delegate* unmanaged<uint, uint, uint, int, int, void>)F("glFramebufferTextureLayer", ref missing);
            glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)F("glCheckFramebufferStatus", ref missing);
            glBufferData = (delegate* unmanaged<uint, nint, void*, uint, void>)F("glBufferData", ref missing);
            glBufferSubData = (delegate* unmanaged<uint, nint, nint, void*, void>)F("glBufferSubData", ref missing);
            glBindBufferBase = (delegate* unmanaged<uint, uint, uint, void>)F("glBindBufferBase", ref missing);
            glBlitFramebuffer = (delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void>)F("glBlitFramebuffer", ref missing);
            glCreateShader = (delegate* unmanaged<uint, uint>)F("glCreateShader", ref missing);
            glShaderSource = (delegate* unmanaged<uint, int, byte**, int*, void>)F("glShaderSource", ref missing);
            glCompileShader = (delegate* unmanaged<uint, void>)F("glCompileShader", ref missing);
            glDeleteShader = (delegate* unmanaged<uint, void>)F("glDeleteShader", ref missing);
            glLinkProgram = (delegate* unmanaged<uint, void>)F("glLinkProgram", ref missing);
            glDeleteProgram = (delegate* unmanaged<uint, void>)F("glDeleteProgram", ref missing);
            glGetShaderiv = (delegate* unmanaged<uint, uint, int*, void>)F("glGetShaderiv", ref missing);
            glGetProgramiv = (delegate* unmanaged<uint, uint, int*, void>)F("glGetProgramiv", ref missing);
            glGetShaderInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)F("glGetShaderInfoLog", ref missing);
            glGetProgramInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)F("glGetProgramInfoLog", ref missing);
            glCreateProgram = (delegate* unmanaged<uint>)F("glCreateProgram", ref missing);
            glAttachShader = (delegate* unmanaged<uint, uint, void>)F("glAttachShader", ref missing);
            glDetachShader = (delegate* unmanaged<uint, uint, void>)F("glDetachShader", ref missing);
            glGetAttribLocation = (delegate* unmanaged<uint, byte*, int>)F("glGetAttribLocation", ref missing);
            glIsEnabled = (delegate* unmanaged<uint, byte>)F("glIsEnabled", ref missing);
            glEnable = (delegate* unmanaged<uint, void>)F("glEnable", ref missing);
            glDisable = (delegate* unmanaged<uint, void>)F("glDisable", ref missing);
            glGetError = (delegate* unmanaged<uint>)F("glGetError", ref missing);
            return missing == null;
        }

        private static HashSet<string> Extensions()
        {
            var set = new HashSet<string>();
            int n;
            glGetIntegerv(GL_NUM_EXTENSIONS, &n);
            for (uint i = 0; i < n; i++)
            {
                var s = glGetStringi(GL_EXTENSIONS, i);
                if (s != null) set.Add(Str(s));
            }
            return set;
        }

        private static string Str(byte* s)
        {
            int len = 0;
            while (s[len] != 0) len++;
            return Encoding.ASCII.GetString(s, len);
        }

        private static bool FindMonoGameInternals(out string what)
        {
            const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var asm = typeof(GraphicsDevice).Assembly;
            ShaderType = asm.GetType("Microsoft.Xna.Framework.Graphics.Shader");
            what = "Shader";
            if (ShaderType == null) return false;
            ShaderHandle = ShaderType.GetField("_shaderHandle", I); what = "Shader._shaderHandle"; if (ShaderHandle == null) return false;
            ShaderGlsl = ShaderType.GetField("_glslCode", I); what = "Shader._glslCode"; if (ShaderGlsl == null) return false;
            ShaderStage = ShaderType.GetProperty("Stage", I); what = "Shader.Stage"; if (ShaderStage == null) return false;
            ShaderAttributes = ShaderType.GetProperty("Attributes", I); what = "Shader.Attributes"; if (ShaderAttributes == null) return false;
            AttributeName = ShaderAttributes.PropertyType.GetElementType()?.GetField("name", I); what = "VertexAttribute.name"; if (AttributeName == null) return false;
            var gd = typeof(GraphicsDevice);
            ResourcesField = gd.GetField("_resources", I); what = "GraphicsDevice._resources"; if (ResourcesField == null) return false;
            ResourcesLock = gd.GetField("_resourcesLock", I); what = "GraphicsDevice._resourcesLock"; if (ResourcesLock == null) return false;
            ProgramCacheField = gd.GetField("_programCache", I); what = "GraphicsDevice._programCache"; if (ProgramCacheField == null) return false;
            ShaderProgramField = gd.GetField("_shaderProgram", I); what = "GraphicsDevice._shaderProgram"; if (ShaderProgramField == null) return false;
            VertexDirty = gd.GetField("_vertexShaderDirty", I); what = "GraphicsDevice._vertexShaderDirty"; if (VertexDirty == null) return false;
            PixelDirty = gd.GetField("_pixelShaderDirty", I); what = "GraphicsDevice._pixelShaderDirty"; if (PixelDirty == null) return false;
            what = null;
            return true;
        }

        // ------------------------------------------------------------------ shader variants

        /// <summary>
        /// Makes multiview variants of every vertex shader MonoGame has that doesn't have one yet. Returns false if
        /// any can't be made (then single-pass stereo mustn't be used: a draw with that shader would fail).
        /// </summary>
        public bool PrepareShaders(out int made, out string problem)
        {
            made = 0;
            problem = null;
            var shaders = new List<object>();
            var resources = (List<WeakReference>)ResourcesField.GetValue(Device);
            lock (ResourcesLock.GetValue(Device))
            {
                foreach (var r in resources)
                {
                    var target = r.Target;
                    if (target != null && ShaderType.IsInstanceOfType(target) && ShaderStage.GetValue(target).ToString() == "Vertex")
                        shaders.Add(target);
                }
            }
            foreach (var shader in shaders)
            {
                if (Variants.TryGetValue(shader, out _)) continue;
                if (Failed.Contains(shader)) { problem ??= "a vertex shader couldn't be made multiview"; continue; }
                if (!MakeVariant(shader, out var why))
                {
                    Failed.Add(shader);
                    problem ??= why;
                    continue;
                }
                made++;
            }
            Patched.RemoveAll(w => !w.IsAlive);
            return problem == null;
        }

        private bool MakeVariant(object shader, out string why)
        {
            why = null;
            var glsl = (string)ShaderGlsl.GetValue(shader);
            var attributes = (Array)ShaderAttributes.GetValue(shader);
            if (TrivialFragment == 0)
            {
                TrivialFragment = Compile(GL_FRAGMENT_SHADER, "void main() { gl_FragColor = vec4(1.0); }", out why);
                if (TrivialFragment == 0) return false;
            }

            // the original's attribute locations, from a test link (they depend only on the vertex shader)
            var locations = new Dictionary<string, int>();
            var original = Compile(GL_VERTEX_SHADER, glsl, out why);
            if (original == 0) return false;
            var program = Link(original, out why);
            glDeleteShader((uint)original);
            if (program == 0) return false;
            foreach (var a in attributes)
            {
                var name = (string)AttributeName.GetValue(a);
                locations[name] = AttribLocation(program, name);
            }
            glDeleteProgram((uint)program);

            var patched = MultiviewShaderPatch.Patch(glsl, locations);
            if (patched == null) { why = "a vertex shader isn't in MonoGame's usual form"; return false; }
            var handle = Compile(GL_VERTEX_SHADER, patched, out why);
            if (handle == 0) return false;
            var check = Link(handle, out why);
            if (check == 0) { glDeleteShader((uint)handle); return false; }
            foreach (var pair in locations)
            {
                var got = AttribLocation(check, pair.Key);
                if (got != pair.Value)
                {
                    why = $"attribute {pair.Key} moved ({pair.Value} -> {got})";
                    glDeleteProgram((uint)check);
                    glDeleteShader((uint)handle);
                    return false;
                }
            }
            glDeleteProgram((uint)check);
            Variants.Add(shader, new Variant { Handle = handle });
            Patched.Add(new WeakReference(shader));
            return true;
        }

        private static int Compile(uint stage, string source, out string why)
        {
            why = null;
            var shader = glCreateShader(stage);
            var bytes = Encoding.ASCII.GetBytes(source + "\0");
            fixed (byte* p = bytes)
            {
                var ptr = p;
                glShaderSource(shader, 1, &ptr, null);
            }
            glCompileShader(shader);
            int ok;
            glGetShaderiv(shader, GL_COMPILE_STATUS, &ok);
            if (ok == 0)
            {
                why = "shader compile: " + InfoLog(shader, true);
                glDeleteShader(shader);
                return 0;
            }
            return (int)shader;
        }

        private int Link(int vertex, out string why)
        {
            why = null;
            var program = glCreateProgram();
            glAttachShader(program, (uint)vertex);
            glAttachShader(program, (uint)TrivialFragment);
            glLinkProgram(program);
            glDetachShader(program, (uint)vertex);
            glDetachShader(program, (uint)TrivialFragment);
            int ok;
            glGetProgramiv(program, GL_LINK_STATUS, &ok);
            if (ok == 0)
            {
                why = "shader link: " + InfoLog(program, false);
                glDeleteProgram(program);
                return 0;
            }
            return (int)program;
        }

        private static int AttribLocation(int program, string name)
        {
            var bytes = Encoding.ASCII.GetBytes(name + "\0");
            fixed (byte* p = bytes) return glGetAttribLocation((uint)program, p);
        }

        private static string InfoLog(uint obj, bool shader)
        {
            int len;
            if (shader) glGetShaderiv(obj, GL_INFO_LOG_LENGTH, &len); else glGetProgramiv(obj, GL_INFO_LOG_LENGTH, &len);
            if (len <= 1) return "(no log)";
            var buffer = new byte[len];
            fixed (byte* p = buffer)
            {
                if (shader) glGetShaderInfoLog(obj, len, &len, p); else glGetProgramInfoLog(obj, len, &len, p);
            }
            return Encoding.ASCII.GetString(buffer, 0, Math.Max(0, len)).Trim();
        }

        // ------------------------------------------------------------------ targets

        /// <summary>A two-layer colour and depth target for stereo passes.</summary>
        public Target CreateTarget(int width, int height)
        {
            var t = new Target { Width = width, Height = height };
            int oldTexture;
            glGetIntegerv(GL_TEXTURE_BINDING_2D_ARRAY, &oldTexture);
            uint* ids = stackalloc uint[2];
            glGenTextures(2, ids);
            t.Color = ids[0];
            t.Depth = ids[1];
            glBindTexture(GL_TEXTURE_2D_ARRAY, t.Color);
            glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_RGBA8, width, height, 2, 0, GL_RGBA, GL_UNSIGNED_BYTE, null);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
            glBindTexture(GL_TEXTURE_2D_ARRAY, t.Depth);
            glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, (int)GL_DEPTH24_STENCIL8, width, height, 2, 0, GL_DEPTH_STENCIL, GL_UNSIGNED_INT_24_8, null);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, (int)GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, (int)GL_NEAREST);
            glBindTexture(GL_TEXTURE_2D_ARRAY, (uint)oldTexture);

            int oldDraw, oldRead;
            glGetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &oldDraw);
            glGetIntegerv(GL_READ_FRAMEBUFFER_BINDING, &oldRead);
            uint* fbos = stackalloc uint[3];
            glGenFramebuffers(3, fbos);
            t.Fbo = fbos[0];
            t.LayerFbo[0] = fbos[1];
            t.LayerFbo[1] = fbos[2];
            glBindFramebuffer(GL_DRAW_FRAMEBUFFER, t.Fbo);
            glFramebufferTextureMultiviewOVR(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, t.Color, 0, 0, 2);
            glFramebufferTextureMultiviewOVR(GL_DRAW_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, t.Depth, 0, 0, 2);
            var status = glCheckFramebufferStatus(GL_DRAW_FRAMEBUFFER);
            for (int layer = 0; layer < 2; layer++)
            {
                glBindFramebuffer(GL_DRAW_FRAMEBUFFER, t.LayerFbo[layer]);
                glFramebufferTextureLayer(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, t.Color, 0, layer);
                glFramebufferTextureLayer(GL_DRAW_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, t.Depth, 0, layer);
            }
            glBindFramebuffer(GL_DRAW_FRAMEBUFFER, (uint)oldDraw);
            glBindFramebuffer(GL_READ_FRAMEBUFFER, (uint)oldRead);
            if (status != GL_FRAMEBUFFER_COMPLETE)
            {
                DestroyTarget(t);
                throw new InvalidOperationException($"multiview framebuffer incomplete (0x{status:x})");
            }
            return t;
        }

        public void DestroyTarget(Target t)
        {
            if (t == null) return;
            uint* fbos = stackalloc uint[3] { t.Fbo, t.LayerFbo[0], t.LayerFbo[1] };
            glDeleteFramebuffers(3, fbos);
            uint* textures = stackalloc uint[2] { t.Color, t.Depth };
            glDeleteTextures(2, textures);
            t.Fbo = t.Color = t.Depth = 0;
        }

        // ------------------------------------------------------------------ passes

        /// <summary>
        /// Starts a stereo pass into <paramref name="target"/>: call right after MonoGame's render target (of the same
        /// size) is set. <paramref name="left"/> and <paramref name="right"/> are each eye's clip delta from the view
        /// the pass is drawn for.
        /// </summary>
        public void Begin(Target target, Matrix left, Matrix right)
        {
            if (InPass) throw new InvalidOperationException("stereo pass already running");
            float* data = stackalloc float[32];
            Write(data, left - Matrix.Identity);
            Write(data + 16, right - Matrix.Identity);
            glBindBuffer(GL_UNIFORM_BUFFER, Ubo);
            glBufferSubData(GL_UNIFORM_BUFFER, 0, 128, data);
            glBindBuffer(GL_UNIFORM_BUFFER, 0);
            glBindBufferBase(GL_UNIFORM_BUFFER, (uint)MultiviewShaderPatch.StereoBinding, Ubo);

            int draw, read;
            glGetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &draw);
            glGetIntegerv(GL_READ_FRAMEBUFFER_BINDING, &read);
            SavedDraw = draw;
            SavedRead = read;
            glBindFramebuffer(GL_FRAMEBUFFER, target.Fbo);

            Swapped.Clear();
            foreach (var w in Patched)
            {
                var shader = w.Target;
                if (shader == null || !Variants.TryGetValue(shader, out var variant)) continue;
                Swapped.Add((shader, ShaderHandle.GetValue(shader)));
                ShaderHandle.SetValue(shader, variant.Handle);
            }
            NormalCache = ProgramCacheField.GetValue(Device);
            ProgramCacheField.SetValue(Device, MultiviewCache);
            ForceProgramChange();
            InPass = true;
        }

        /// <summary>Ends the stereo pass: MonoGame's own shaders and framebuffer are back.</summary>
        public void End()
        {
            if (!InPass) return;
            InPass = false;
            foreach (var (shader, handle) in Swapped) ShaderHandle.SetValue(shader, handle);
            Swapped.Clear();
            ProgramCacheField.SetValue(Device, NormalCache);
            ForceProgramChange();
            glBindFramebuffer(GL_DRAW_FRAMEBUFFER, (uint)SavedDraw);
            glBindFramebuffer(GL_READ_FRAMEBUFFER, (uint)SavedRead);
        }

        private void ForceProgramChange()
        {
            ShaderProgramField.SetValue(Device, null);
            VertexDirty.SetValue(Device, true);
            PixelDirty.SetValue(Device, true);
        }

        private static void Write(float* dst, Matrix m)
        {
            // XNA's row-major order, read by GLSL (std140, column-major) as the transpose: delta * v in the shader is
            // then v * delta for XNA's row vectors
            dst[0] = m.M11; dst[1] = m.M12; dst[2] = m.M13; dst[3] = m.M14;
            dst[4] = m.M21; dst[5] = m.M22; dst[6] = m.M23; dst[7] = m.M24;
            dst[8] = m.M31; dst[9] = m.M32; dst[10] = m.M33; dst[11] = m.M34;
            dst[12] = m.M41; dst[13] = m.M42; dst[14] = m.M43; dst[15] = m.M44;
        }

        /// <summary>
        /// Copies one eye's layer (colour, depth and stencil) into a MonoGame render target of the same size. Leaves
        /// that render target set on the device.
        /// </summary>
        public void CopyLayer(Target target, int layer, RenderTarget2D destination)
        {
            Device.SetRenderTarget(destination);
            int draw, read;
            glGetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &draw);
            glGetIntegerv(GL_READ_FRAMEBUFFER_BINDING, &read);
            var scissor = glIsEnabled(GL_SCISSOR_TEST) != 0;
            var srgb = glIsEnabled(GL_FRAMEBUFFER_SRGB) != 0;
            if (scissor) glDisable(GL_SCISSOR_TEST);
            if (srgb) glDisable(GL_FRAMEBUFFER_SRGB);
            glBindFramebuffer(GL_READ_FRAMEBUFFER, target.LayerFbo[layer]);
            glBlitFramebuffer(0, 0, target.Width, target.Height, 0, 0, destination.Width, destination.Height,
                GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT | GL_STENCIL_BUFFER_BIT, GL_NEAREST);
            glBindFramebuffer(GL_READ_FRAMEBUFFER, (uint)read);
            glBindFramebuffer(GL_DRAW_FRAMEBUFFER, (uint)draw);
            if (scissor) glEnable(GL_SCISSOR_TEST);
            if (srgb) glEnable(GL_FRAMEBUFFER_SRGB);
        }

        public uint LastError() => glGetError();

        public void Dispose()
        {
            End();
            foreach (var w in Patched)
            {
                var shader = w.Target;
                if (shader != null && Variants.TryGetValue(shader, out var v) && v.Handle != 0) glDeleteShader((uint)v.Handle);
            }
            Patched.Clear();
            (MultiviewCache as IDisposable)?.Dispose();
            if (TrivialFragment != 0) glDeleteShader((uint)TrivialFragment);
            TrivialFragment = 0;
            if (Ubo != 0)
            {
                uint id = Ubo;
                glDeleteBuffers(1, &id);
            }
            Ubo = 0;
        }
    }
}
