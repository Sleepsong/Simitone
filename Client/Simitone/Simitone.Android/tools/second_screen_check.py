"""Checks the second screen's drawing on Mesa (surfaceless EGL, GLES 3), the way SecondScreenHost does it:
context A (the game) draws an image into a texture, stored top row first as MonoGame's render targets are; context B,
made with A's config (looked up by its config id) and sharing A's objects, is made current on another surface, draws
the texture over it with SecondScreenHost's shaders and quad (client-side vertex arrays), and A is made current again.
Expect: B sees A's texture after a glFlush, the top of the image lands at the top of B's surface, A is current after."""
import ctypes as C, sys
egl = C.CDLL('libEGL.so.1'); gl = C.CDLL('libGLESv2.so.2')
for f in ('eglGetPlatformDisplay','eglCreateContext','eglCreatePbufferSurface','eglGetCurrentContext','eglGetCurrentSurface'):
    getattr(egl, f).restype = C.c_void_p
dpy = C.c_void_p(egl.eglGetPlatformDisplay(0x31DD, None, None))
assert egl.eglInitialize(dpy, None, None)
egl.eglBindAPI(0x30A0)
attrs = (C.c_int * 9)(0x3040, 0x40, 0x3033, 0x0001, 0x3024, 8, 0x3021, 8, 0x3038)  # ES3, PBUFFER, R8, A8
cfg = C.c_void_p(); n = C.c_int()
assert egl.eglChooseConfig(dpy, attrs, C.byref(cfg), 1, C.byref(n)) and n.value
W, H = 8, 6
pb = (C.c_int * 5)(0x3057, W, 0x3056, H, 0x3038)
surfA = C.c_void_p(egl.eglCreatePbufferSurface(dpy, cfg, pb)); surfB = C.c_void_p(egl.eglCreatePbufferSurface(dpy, cfg, pb))
ctxA = C.c_void_p(egl.eglCreateContext(dpy, cfg, None, (C.c_int * 3)(0x3098, 3, 0x3038)))
assert egl.eglMakeCurrent(dpy, surfA, surfA, ctxA)

# --- as SetupContext: A's config id and client version -> B sharing A
v = C.c_int()
egl.eglQueryContext(dpy, ctxA, 0x3028, C.byref(v)); cid = v.value                     # EGL_CONFIG_ID
cfg2 = C.c_void_p(); n2 = C.c_int()
assert egl.eglChooseConfig(dpy, (C.c_int * 3)(0x3028, cid, 0x3038), C.byref(cfg2), 1, C.byref(n2)) and n2.value == 1
egl.eglQueryContext(dpy, ctxA, 0x3098, C.byref(v)); ver = max(2, v.value)              # EGL_CONTEXT_CLIENT_VERSION
ctxB = C.c_void_p(egl.eglCreateContext(dpy, cfg2, ctxA, (C.c_int * 3)(0x3098, ver, 0x3038)))
assert ctxB.value, 'shared context'
print('config id', cid, 'GLES', ver, '-> shared context made')

# --- A: an image, rows stored top row first (row 0 red = the top, last row blue = the bottom)
tex = C.c_uint(); gl.glGenTextures(1, C.byref(tex)); gl.glBindTexture(0x0DE1, tex)
rows = [(255, 0, 0)] + [(0, 255, 0)] * (H - 2) + [(0, 0, 255)]
data = bytes(b for r in rows for _ in range(W) for b in (*r, 255))
gl.glTexImage2D(0x0DE1, 0, 0x1908, W, H, 0, 0x1908, 0x1401, data)
# (MonoGame leaves a fresh texture's default filter; SecondScreenHost sets its own in B)
gl.glClearColor(C.c_float(0.5), C.c_float(0.5), C.c_float(0.5), C.c_float(1)); gl.glClear(0x4000)
gl.glFlush()

# --- B on the other surface: SecondScreenHost's program and quad
assert egl.eglMakeCurrent(dpy, surfB, surfB, ctxB)
egl.eglSwapInterval(dpy, 0)
VS = b"attribute vec2 aPos;\nattribute vec2 aUV;\nvarying vec2 vUV;\nvoid main() { vUV = aUV; gl_Position = vec4(aPos, 0.0, 1.0); }\n"
FS = b"precision mediump float;\nvarying vec2 vUV;\nuniform sampler2D uTex;\nvoid main() { gl_FragColor = vec4(texture2D(uTex, vUV).rgb, 1.0); }\n"
def sh(kind, src):
    s = gl.glCreateShader(kind); p = C.c_char_p(src); gl.glShaderSource(s, 1, C.byref(p), None); gl.glCompileShader(s)
    ok = C.c_int(); gl.glGetShaderiv(s, 0x8B81, C.byref(ok)); assert ok.value, 'compile'; return s
prog = gl.glCreateProgram(); gl.glAttachShader(prog, sh(0x8B31, VS)); gl.glAttachShader(prog, sh(0x8B30, FS))
gl.glBindAttribLocation(prog, 0, b"aPos"); gl.glBindAttribLocation(prog, 1, b"aUV"); gl.glLinkProgram(prog)
ok = C.c_int(); gl.glGetProgramiv(prog, 0x8B82, C.byref(ok)); assert ok.value, 'link'
quad = (C.c_float * 16)(-1, -1, 0, 1,  1, -1, 1, 1,  -1, 1, 0, 0,  1, 1, 1, 0)
gl.glViewport(0, 0, W, H)
for cap in (0x0BE2, 0x0B71, 0x0C11, 0x0B44): gl.glDisable(cap)
gl.glUseProgram(prog); gl.glActiveTexture(0x84C0); gl.glBindTexture(0x0DE1, tex)
for p, val in ((0x2801, 0x2601), (0x2800, 0x2601), (0x2802, 0x812F), (0x2803, 0x812F)): gl.glTexParameteri(0x0DE1, p, val)
gl.glUniform1i(gl.glGetUniformLocation(prog, b"uTex"), 0)
gl.glVertexAttribPointer(0, 2, 0x1406, 0, 16, C.byref(quad, 0)); gl.glEnableVertexAttribArray(0)
gl.glVertexAttribPointer(1, 2, 0x1406, 0, 16, C.byref(quad, 8)); gl.glEnableVertexAttribArray(1)
gl.glDrawArrays(0x0005, 0, 4)
err = gl.glGetError()
px = (C.c_ubyte * (W * H * 4))(); gl.glReadPixels(0, 0, W, H, 0x1908, 0x1401, px)
top = tuple(px[((H - 1) * W) * 4: ((H - 1) * W) * 4 + 3]); bottom = tuple(px[0:3])   # GL row 0 is the bottom
egl.eglSwapBuffers(dpy, surfB)

# --- back to A
assert egl.eglMakeCurrent(dpy, surfA, surfA, ctxA)
backA = egl.eglGetCurrentContext() == ctxA.value
print('gl error', hex(err), '| top of surface', top, '| bottom', bottom, '| game context current again:', backA)
good = err == 0 and top == (255, 0, 0) and bottom == (0, 0, 255) and backA
print('PASS' if good else 'FAIL'); sys.exit(0 if good else 1)
