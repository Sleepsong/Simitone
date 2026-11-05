using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Xna.Framework.Graphics;

namespace Simitone.VR.Graphics
{
    public enum GLContextKind
    {
        None,
        GLX,
        EGL,
        WGL
    }

    /// <summary>Native handles describing the OpenGL context MonoGame (SDL2) is rendering with.</summary>
    public struct GLContextInfo
    {
        public GLContextKind Kind;
        public IntPtr Display;      // X11 Display* (GLX) or EGLDisplay
        public IntPtr Context;      // GLXContext, EGLContext or HGLRC
        public ulong Drawable;      // GLXDrawable
        public IntPtr FBConfig;     // GLXFBConfig or EGLConfig
        public uint VisualId;       // GLX only
        public IntPtr HDC;          // WGL only
        public IntPtr EglGetProcAddress;
        public IntPtr SdlWindow;
        public IntPtr SdlContext;
        public string VideoDriver;

        public override string ToString()
            => $"{Kind} (SDL video driver '{VideoDriver}', display 0x{Display.ToInt64():x}, context 0x{Context.ToInt64():x})";
    }

    /// <summary>
    /// Thin OpenGL layer used to move pixels between MonoGame render targets and OpenXR swapchain images.
    /// MonoGame keeps its own GL state cache, so every function here restores the state it touches.
    /// All calls must happen on the game thread with MonoGame's context current.
    /// </summary>
    public static unsafe class GLInterop
    {
        public const uint GL_TEXTURE_2D = 0x0DE1;
        public const uint GL_READ_FRAMEBUFFER = 0x8CA8;
        public const uint GL_DRAW_FRAMEBUFFER = 0x8CA9;
        public const uint GL_READ_FRAMEBUFFER_BINDING = 0x8CAA;
        public const uint GL_DRAW_FRAMEBUFFER_BINDING = 0x8CA6;
        public const uint GL_COLOR_ATTACHMENT0 = 0x8CE0;
        public const uint GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
        public const uint GL_COLOR_BUFFER_BIT = 0x4000;
        public const uint GL_NEAREST = 0x2600;
        public const uint GL_LINEAR = 0x2601;
        public const uint GL_SCISSOR_TEST = 0x0C11;
        public const uint GL_SCISSOR_BOX = 0x0C10;
        public const uint GL_COLOR_WRITEMASK = 0x0C23;
        public const uint GL_COLOR_CLEAR_VALUE = 0x0C22;
        public const uint GL_FRAMEBUFFER_SRGB = 0x8DB9;
        public const uint GL_VENDOR = 0x1F00;
        public const uint GL_RENDERER = 0x1F01;
        public const uint GL_VERSION = 0x1F02;
        public const uint GL_TEXTURE_BINDING_2D = 0x8069;
        public const uint GL_PACK_ALIGNMENT = 0x0D05;
        public const uint GL_RGBA = 0x1908;
        public const uint GL_UNSIGNED_BYTE = 0x1401;

        public const long GL_RGBA8 = 0x8058;
        public const long GL_SRGB8_ALPHA8 = 0x8C43;
        public const long GL_RGB10_A2 = 0x8059;
        public const long GL_RGBA16F = 0x881A;

        private static bool Loaded;
        private static IntPtr SdlLib;

        // SDL2
        private static delegate* unmanaged<byte*, IntPtr> SDL_GL_GetProcAddress;
        private static delegate* unmanaged<IntPtr> SDL_GL_GetCurrentContext;
        private static delegate* unmanaged<IntPtr> SDL_GL_GetCurrentWindow;
        private static delegate* unmanaged<IntPtr, IntPtr, int> SDL_GL_MakeCurrent;
        private static delegate* unmanaged<byte*> SDL_GetCurrentVideoDriver;

        // GL
        private static delegate* unmanaged<uint, int*, void> glGetIntegerv;
        private static delegate* unmanaged<uint, byte*, void> glGetBooleanv;
        private static delegate* unmanaged<uint, float*, void> glGetFloatv;
        private static delegate* unmanaged<uint, byte> glIsEnabled;
        private static delegate* unmanaged<uint, void> glEnable;
        private static delegate* unmanaged<uint, void> glDisable;
        private static delegate* unmanaged<int, int, int, int, void> glScissor;
        private static delegate* unmanaged<byte, byte, byte, byte, void> glColorMask;
        private static delegate* unmanaged<float, float, float, float, void> glClearColor;
        private static delegate* unmanaged<uint, void> glClear;
        private static delegate* unmanaged<int, uint*, void> glGenFramebuffers;
        private static delegate* unmanaged<int, uint*, void> glDeleteFramebuffers;
        private static delegate* unmanaged<uint, uint, void> glBindFramebuffer;
        private static delegate* unmanaged<uint, uint, uint, uint, int, void> glFramebufferTexture2D;
        private static delegate* unmanaged<uint, uint> glCheckFramebufferStatus;
        private static delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void> glBlitFramebuffer;
        private static delegate* unmanaged<uint> glGetError;
        private static delegate* unmanaged<uint, byte*> glGetString;
        private static delegate* unmanaged<void> glFlush;
        private static delegate* unmanaged<void> glFinish;
        private static delegate* unmanaged<int, int, int, int, uint, uint, void*, void> glReadPixels;

        private static uint ReadFbo, DrawFbo;

        public static string GLVersion { get; private set; }
        public static string GLRenderer { get; private set; }

        public static void Load()
        {
            if (Loaded) return;
            SdlLib = LoadSdl();
            SDL_GL_GetProcAddress = (delegate* unmanaged<byte*, IntPtr>)NativeLibrary.GetExport(SdlLib, "SDL_GL_GetProcAddress");
            SDL_GL_GetCurrentContext = (delegate* unmanaged<IntPtr>)NativeLibrary.GetExport(SdlLib, "SDL_GL_GetCurrentContext");
            SDL_GL_GetCurrentWindow = (delegate* unmanaged<IntPtr>)NativeLibrary.GetExport(SdlLib, "SDL_GL_GetCurrentWindow");
            SDL_GL_MakeCurrent = (delegate* unmanaged<IntPtr, IntPtr, int>)NativeLibrary.GetExport(SdlLib, "SDL_GL_MakeCurrent");
            SDL_GetCurrentVideoDriver = (delegate* unmanaged<byte*>)NativeLibrary.GetExport(SdlLib, "SDL_GetCurrentVideoDriver");

            glGetIntegerv = (delegate* unmanaged<uint, int*, void>)GL("glGetIntegerv");
            glGetBooleanv = (delegate* unmanaged<uint, byte*, void>)GL("glGetBooleanv");
            glGetFloatv = (delegate* unmanaged<uint, float*, void>)GL("glGetFloatv");
            glIsEnabled = (delegate* unmanaged<uint, byte>)GL("glIsEnabled");
            glEnable = (delegate* unmanaged<uint, void>)GL("glEnable");
            glDisable = (delegate* unmanaged<uint, void>)GL("glDisable");
            glScissor = (delegate* unmanaged<int, int, int, int, void>)GL("glScissor");
            glColorMask = (delegate* unmanaged<byte, byte, byte, byte, void>)GL("glColorMask");
            glClearColor = (delegate* unmanaged<float, float, float, float, void>)GL("glClearColor");
            glClear = (delegate* unmanaged<uint, void>)GL("glClear");
            glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)GL("glGenFramebuffers");
            glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)GL("glDeleteFramebuffers");
            glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)GL("glBindFramebuffer");
            glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)GL("glFramebufferTexture2D");
            glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)GL("glCheckFramebufferStatus");
            glBlitFramebuffer = (delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void>)GL("glBlitFramebuffer");
            glGetError = (delegate* unmanaged<uint>)GL("glGetError");
            glGetString = (delegate* unmanaged<uint, byte*>)GL("glGetString");
            glFlush = (delegate* unmanaged<void>)GL("glFlush");
            glFinish = (delegate* unmanaged<void>)GL("glFinish");
            glReadPixels = (delegate* unmanaged<int, int, int, int, uint, uint, void*, void>)GL("glReadPixels");

            GLVersion = Str(glGetString(GL_VERSION));
            GLRenderer = Str(glGetString(GL_RENDERER));
            Loaded = true;
        }

        private static IntPtr LoadSdl()
        {
            string[] names;
            if (OperatingSystem.IsWindows()) names = new[] { "SDL2.dll" };
            else if (OperatingSystem.IsMacOS()) names = new[] { "libSDL2-2.0.0.dylib", "libSDL2.dylib" };
            else names = new[] { "libSDL2-2.0.so.0", "libSDL2.so" };

            var rid = RuntimeInformation.RuntimeIdentifier;
            foreach (var name in names)
            {
                // MonoGame has already loaded SDL by the time we get here; asking for the same file returns that copy.
                foreach (var candidate in new[]
                {
                    Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", name),
                    Path.Combine(AppContext.BaseDirectory, name),
                    name
                })
                {
                    if (candidate.Contains(Path.DirectorySeparatorChar) && !File.Exists(candidate)) continue;
                    if (NativeLibrary.TryLoad(candidate, out var handle)) return handle;
                }
            }
            throw new DllNotFoundException("Could not locate the SDL2 library used by MonoGame.");
        }

        private static IntPtr GL(string name)
        {
            var bytes = Encoding.ASCII.GetBytes(name + "\0");
            IntPtr fn;
            fixed (byte* p = bytes) fn = SDL_GL_GetProcAddress(p);
            if (fn == IntPtr.Zero && !name.EndsWith("EXT"))
            {
                // Some old drivers only expose the framebuffer functions with an EXT suffix.
                fixed (byte* p = Encoding.ASCII.GetBytes(name + "EXT\0")) fn = SDL_GL_GetProcAddress(p);
            }
            if (fn == IntPtr.Zero) throw new EntryPointNotFoundException("OpenGL function not available: " + name);
            return fn;
        }

        private static string Str(byte* s)
        {
            if (s == null) return "";
            int n = 0;
            while (s[n] != 0) n++;
            return Encoding.ASCII.GetString(s, n);
        }

        public static string VideoDriver => Str(SDL_GetCurrentVideoDriver());

        // ------------------------------------------------------------------ context capture

        /// <summary>Describes MonoGame's current GL context in the form an OpenXR graphics binding needs.</summary>
        public static GLContextInfo CaptureCurrentContext()
        {
            Load();
            var info = new GLContextInfo
            {
                SdlWindow = SDL_GL_GetCurrentWindow(),
                SdlContext = SDL_GL_GetCurrentContext(),
                VideoDriver = VideoDriver
            };

            if (OperatingSystem.IsWindows())
            {
                var gl = NativeLibrary.Load("opengl32.dll");
                var getDC = (delegate* unmanaged<IntPtr>)NativeLibrary.GetExport(gl, "wglGetCurrentDC");
                var getRC = (delegate* unmanaged<IntPtr>)NativeLibrary.GetExport(gl, "wglGetCurrentContext");
                info.HDC = getDC();
                info.Context = getRC();
                if (info.Context != IntPtr.Zero) info.Kind = GLContextKind.WGL;
                return info;
            }

            // Prefer GLX: SteamVR (desktop and Steam Frame) supports XR_KHR_opengl_enable with the Xlib binding.
            if (TryCaptureGLX(ref info)) return info;
            TryCaptureEGL(ref info);
            return info;
        }

        private static IntPtr TryLoadAny(params string[] names)
        {
            foreach (var n in names)
                if (NativeLibrary.TryLoad(n, out var h)) return h;
            return IntPtr.Zero;
        }

        private static bool TryCaptureGLX(ref GLContextInfo info)
        {
            var lib = TryLoadAny("libGLX.so.0", "libGL.so.1", "libGL.so");
            if (lib == IntPtr.Zero) return false;
            if (!NativeLibrary.TryGetExport(lib, "glXGetCurrentContext", out var pCtx)) return false;
            var getCtx = (delegate* unmanaged<IntPtr>)pCtx;
            var ctx = getCtx();
            if (ctx == IntPtr.Zero) return false;

            var getDisplay = (delegate* unmanaged<IntPtr>)NativeLibrary.GetExport(lib, "glXGetCurrentDisplay");
            var getDrawable = (delegate* unmanaged<ulong>)NativeLibrary.GetExport(lib, "glXGetCurrentDrawable");
            var queryContext = (delegate* unmanaged<IntPtr, IntPtr, int, int*, int>)NativeLibrary.GetExport(lib, "glXQueryContext");
            var chooseFBConfig = (delegate* unmanaged<IntPtr, int, int*, int*, IntPtr*>)NativeLibrary.GetExport(lib, "glXChooseFBConfig");
            var visualFromFB = (delegate* unmanaged<IntPtr, IntPtr, IntPtr>)NativeLibrary.GetExport(lib, "glXGetVisualFromFBConfig");

            info.Kind = GLContextKind.GLX;
            info.Context = ctx;
            info.Display = getDisplay();
            info.Drawable = getDrawable();

            const int GLX_FBCONFIG_ID = 0x8013;
            const int GLX_SCREEN = 0x800C;
            int fbId = 0, screen = 0;
            queryContext(info.Display, ctx, GLX_FBCONFIG_ID, &fbId);
            queryContext(info.Display, ctx, GLX_SCREEN, &screen);

            int* attribs = stackalloc int[] { GLX_FBCONFIG_ID, fbId, 0 };
            int count = 0;
            var configs = chooseFBConfig(info.Display, screen, attribs, &count);
            var x11 = TryLoadAny("libX11.so.6", "libX11.so");
            delegate* unmanaged<void*, int> xFree = null;
            if (x11 != IntPtr.Zero && NativeLibrary.TryGetExport(x11, "XFree", out var pFree)) xFree = (delegate* unmanaged<void*, int>)pFree;

            if (configs != null && count > 0)
            {
                info.FBConfig = configs[0];
                var visual = visualFromFB(info.Display, info.FBConfig);
                if (visual != IntPtr.Zero)
                {
                    // XVisualInfo { Visual* visual; VisualID visualid; ... }
                    info.VisualId = (uint)(*(ulong*)((byte*)visual + IntPtr.Size));
                    if (xFree != null) xFree((void*)visual);
                }
                if (xFree != null) xFree(configs);
            }
            return true;
        }

        private static bool TryCaptureEGL(ref GLContextInfo info)
        {
            var lib = TryLoadAny("libEGL.so.1", "libEGL.so");
            if (lib == IntPtr.Zero) return false;
            var getCtx = (delegate* unmanaged<IntPtr>)NativeLibrary.GetExport(lib, "eglGetCurrentContext");
            var ctx = getCtx();
            if (ctx == IntPtr.Zero) return false;

            var getDisplay = (delegate* unmanaged<IntPtr>)NativeLibrary.GetExport(lib, "eglGetCurrentDisplay");
            var queryContext = (delegate* unmanaged<IntPtr, IntPtr, int, int*, uint>)NativeLibrary.GetExport(lib, "eglQueryContext");
            var chooseConfig = (delegate* unmanaged<IntPtr, int*, IntPtr*, int, int*, uint>)NativeLibrary.GetExport(lib, "eglChooseConfig");

            const int EGL_CONFIG_ID = 0x3028;
            const int EGL_NONE = 0x3038;
            info.Kind = GLContextKind.EGL;
            info.Context = ctx;
            info.Display = getDisplay();
            info.EglGetProcAddress = NativeLibrary.GetExport(lib, "eglGetProcAddress");

            int configId = 0;
            queryContext(info.Display, ctx, EGL_CONFIG_ID, &configId);
            int* attribs = stackalloc int[] { EGL_CONFIG_ID, configId, EGL_NONE };
            IntPtr config = IntPtr.Zero;
            int num = 0;
            if (chooseConfig(info.Display, attribs, &config, 1, &num) != 0 && num > 0) info.FBConfig = config;
            return true;
        }

        /// <summary>
        /// Some runtimes switch GL contexts internally. If that leaked out, put MonoGame's context back.
        /// </summary>
        public static void EnsureContext(in GLContextInfo info)
        {
            if (info.SdlContext == IntPtr.Zero) return;
            if (SDL_GL_GetCurrentContext() != info.SdlContext)
                SDL_GL_MakeCurrent(info.SdlWindow, info.SdlContext);
        }

        // ------------------------------------------------------------------ MonoGame texture handles

        private static FieldInfo GlTextureField;

        /// <summary>Returns the OpenGL texture name backing a MonoGame (DesktopGL) texture or render target.</summary>
        public static uint GetTextureHandle(Texture texture)
        {
            if (GlTextureField == null)
            {
                GlTextureField = typeof(Texture).GetField("glTexture", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (GlTextureField == null)
                    throw new MissingFieldException("MonoGame Texture.glTexture not found - is this the DesktopGL build of MonoGame?");
            }
            return (uint)(int)GlTextureField.GetValue(texture);
        }

        // ------------------------------------------------------------------ blits

        private struct SavedState
        {
            public int ReadFb, DrawFb;
            public bool Scissor, Srgb;
        }

        private static SavedState Save()
        {
            var s = new SavedState();
            int v;
            glGetIntegerv(GL_READ_FRAMEBUFFER_BINDING, &v); s.ReadFb = v;
            glGetIntegerv(GL_DRAW_FRAMEBUFFER_BINDING, &v); s.DrawFb = v;
            s.Scissor = glIsEnabled(GL_SCISSOR_TEST) != 0;
            s.Srgb = glIsEnabled(GL_FRAMEBUFFER_SRGB) != 0;
            if (s.Scissor) glDisable(GL_SCISSOR_TEST);
            if (s.Srgb) glDisable(GL_FRAMEBUFFER_SRGB);
            return s;
        }

        private static void Restore(in SavedState s)
        {
            glBindFramebuffer(GL_READ_FRAMEBUFFER, (uint)s.ReadFb);
            glBindFramebuffer(GL_DRAW_FRAMEBUFFER, (uint)s.DrawFb);
            if (s.Scissor) glEnable(GL_SCISSOR_TEST);
            if (s.Srgb) glEnable(GL_FRAMEBUFFER_SRGB);
        }

        private static void EnsureFbos()
        {
            if (ReadFbo != 0) return;
            uint* ids = stackalloc uint[2];
            glGenFramebuffers(2, ids);
            ReadFbo = ids[0];
            DrawFbo = ids[1];
        }

        /// <summary>
        /// Copies a texture into another (an OpenXR swapchain image). MonoGame stores render targets upside
        /// down relative to OpenGL convention, so <paramref name="flipY"/> should be true for those.
        /// </summary>
        public static void BlitTexture(uint srcTexture, int srcWidth, int srcHeight,
            uint dstTexture, int dstX, int dstY, int dstWidth, int dstHeight, bool flipY, bool linear = true)
            => BlitTextureRegion(srcTexture, 0, 0, srcWidth, srcHeight, dstTexture, dstX, dstY, dstWidth, dstHeight, flipY, linear);

        /// <summary>
        /// Copies a region of a texture into another. The source region is in the texture's memory rows (for a MonoGame
        /// render target, which is stored top-down, row 0 is the top of the image); with <paramref name="flipY"/> the
        /// region's first row lands at the top (GL: highest row) of the destination.
        /// </summary>
        public static void BlitTextureRegion(uint srcTexture, int srcX, int srcY, int srcWidth, int srcHeight,
            uint dstTexture, int dstX, int dstY, int dstWidth, int dstHeight, bool flipY, bool linear = true)
        {
            EnsureFbos();
            var saved = Save();
            glBindFramebuffer(GL_READ_FRAMEBUFFER, ReadFbo);
            glFramebufferTexture2D(GL_READ_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, srcTexture, 0);
            glBindFramebuffer(GL_DRAW_FRAMEBUFFER, DrawFbo);
            glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, dstTexture, 0);

            int sy0 = flipY ? srcY + srcHeight : srcY;
            int sy1 = flipY ? srcY : srcY + srcHeight;
            glBlitFramebuffer(srcX, sy0, srcX + srcWidth, sy1, dstX, dstY, dstX + dstWidth, dstY + dstHeight,
                GL_COLOR_BUFFER_BIT, linear ? GL_LINEAR : GL_NEAREST);

            // Detach so the swapchain image is not referenced by our FBO when the runtime takes it back.
            glFramebufferTexture2D(GL_READ_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, 0, 0);
            glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, 0, 0);
            Restore(saved);
        }

        /// <summary>
        /// Waits for all GL work to finish: before destroying swapchains whose images were just written (OpenXR requires
        /// the application to be done with an image before it is destroyed).
        /// </summary>
        public static void Finish()
        {
            if (glFinish != null) glFinish();
        }

        /// <summary>Copies the window's back buffer (what MonoGame is about to present) into a texture.</summary>
        public static void BlitBackbuffer(int srcWidth, int srcHeight, uint dstTexture, int dstWidth, int dstHeight)
        {
            EnsureFbos();
            var saved = Save();
            glBindFramebuffer(GL_READ_FRAMEBUFFER, 0);
            glBindFramebuffer(GL_DRAW_FRAMEBUFFER, DrawFbo);
            glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, dstTexture, 0);
            glBlitFramebuffer(0, 0, srcWidth, srcHeight, 0, 0, dstWidth, dstHeight, GL_COLOR_BUFFER_BIT, GL_LINEAR);
            glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, 0, 0);
            Restore(saved);
        }

        /// <summary>
        /// Fills rectangles of a texture with solid colours (scissored clears). Used to draw the VR laser-pointer
        /// cursor onto the virtual screen without needing a shader. Rectangles are in GL (bottom-left) coordinates.
        /// </summary>
        public static void FillRects(uint dstTexture, ReadOnlySpan<(int x, int y, int w, int h, float r, float g, float b)> rects)
        {
            EnsureFbos();
            var saved = Save();
            int* box = stackalloc int[4];
            glGetIntegerv(GL_SCISSOR_BOX, box);
            byte* mask = stackalloc byte[4];
            glGetBooleanv(GL_COLOR_WRITEMASK, mask);
            float* clear = stackalloc float[4];
            glGetFloatv(GL_COLOR_CLEAR_VALUE, clear);

            glBindFramebuffer(GL_DRAW_FRAMEBUFFER, DrawFbo);
            glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, dstTexture, 0);
            glColorMask(1, 1, 1, 1);
            glEnable(GL_SCISSOR_TEST);
            foreach (var rect in rects)
            {
                glScissor(rect.x, rect.y, Math.Max(0, rect.w), Math.Max(0, rect.h));
                glClearColor(rect.r, rect.g, rect.b, 1f);
                glClear(GL_COLOR_BUFFER_BIT);
            }
            glDisable(GL_SCISSOR_TEST);
            glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, 0, 0);

            glScissor(box[0], box[1], box[2], box[3]);
            glColorMask(mask[0], mask[1], mask[2], mask[3]);
            glClearColor(clear[0], clear[1], clear[2], clear[3]);
            Restore(saved);
        }

        /// <summary>Reads back one pixel of a texture (debug / self-test only).</summary>
        public static uint ReadPixel(uint texture, int x, int y)
        {
            EnsureFbos();
            var saved = Save();
            glBindFramebuffer(GL_READ_FRAMEBUFFER, ReadFbo);
            glFramebufferTexture2D(GL_READ_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, texture, 0);
            uint px = 0;
            glReadPixels(x, y, 1, 1, GL_RGBA, GL_UNSIGNED_BYTE, &px);
            glFramebufferTexture2D(GL_READ_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, 0, 0);
            Restore(saved);
            return px;
        }

        public static uint GetError() => glGetError();
        public static void Flush() => glFlush();
    }
}
