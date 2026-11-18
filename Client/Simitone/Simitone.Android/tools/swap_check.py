"""Checks swapped screens' drawing on Mesa (surfaceless EGL, GLES 3), the way SecondScreenHost does it:
context A (the game) has drawn its frame into its own surface's default framebuffer (top row red, bottom row blue).
Still in A, the frame is copied into a texture (glCopyTexImage2D the first time, glCopyTexSubImage2D after), after
binding framebuffer 0 and texture unit 0, and A's active unit, texture and framebuffer bindings are put back as they
were. Context B, sharing A, is made current on another surface (twice the size, as the other display is scaled) and
draws the copy with SecondScreenHost's shaders and CopyQuad (a window's framebuffer is stored bottom row first).
Expect: no GL errors, the frame's top at the top of B's surface (both copies), A's bindings untouched, A current after."""
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
W, H = 8, 6          # the game's frame
BW, BH = 16, 12      # the other display's surface
surfA = C.c_void_p(egl.eglCreatePbufferSurface(dpy, cfg, (C.c_int * 5)(0x3057, W, 0x3056, H, 0x3038)))
surfB = C.c_void_p(egl.eglCreatePbufferSurface(dpy, cfg, (C.c_int * 5)(0x3057, BW, 0x3056, BH, 0x3038)))
ctxA = C.c_void_p(egl.eglCreateContext(dpy, cfg, None, (C.c_int * 3)(0x3098, 3, 0x3038)))
assert egl.eglMakeCurrent(dpy, surfA, surfA, ctxA)
ctxB = C.c_void_p(egl.eglCreateContext(dpy, cfg, ctxA, (C.c_int * 3)(0x3098, 3, 0x3038)))
assert ctxB.value

def geti(e):
    v = C.c_int(); gl.glGetIntegerv(e, C.byref(v)); return v.value

# --- A: the game's state at the end of its frame, as something to put back (unit 3 active, a texture on unit 0,
# an FBO bound for drawing and reading)
gameTex = C.c_uint(); gl.glGenTextures(1, C.byref(gameTex))
gl.glActiveTexture(0x84C0); gl.glBindTexture(0x0DE1, gameTex)
gl.glTexImage2D(0x0DE1, 0, 0x1908, 4, 4, 0, 0x1908, 0x1401, None)
fbo = C.c_uint(); gl.glGenFramebuffers(1, C.byref(fbo))
gl.glActiveTexture(0x84C3)
before = (0x84C3, gameTex.value, fbo.value)

def draw_frame(top, bottom):
    """The game's frame in A's default framebuffer: the top row one colour, the bottom another, green between."""
    gl.glBindFramebuffer(0x8D40, 0)
    gl.glEnable(0x0C11)
    for y, h, c in ((0, 1, bottom), (1, H - 2, (0, 1, 0)), (H - 1, 1, top)):   # GL row 0 is the bottom
        gl.glScissor(0, y, W, h)
        gl.glClearColor(*(C.c_float(x) for x in c), C.c_float(1)); gl.glClear(0x4000)
    gl.glDisable(0x0C11)
    gl.glBindFramebuffer(0x8D40, fbo)   # (and the game had an FBO bound after, say)

copyTex = C.c_uint(0); copySize = [0, 0]
def copy_game_frame():
    """As SecondScreenHost.CopyGameFrame."""
    while gl.glGetError() != 0: pass
    unit = geti(0x84E0)
    gl.glActiveTexture(0x84C0)
    bound = geti(0x8069); draw = geti(0x8CA6); read = geti(0x8CAA)
    gl.glBindFramebuffer(0x8D40, 0)
    if copyTex.value == 0:
        gl.glGenTextures(1, C.byref(copyTex))
    gl.glBindTexture(0x0DE1, copyTex)
    if copySize != [W, H]:
        gl.glCopyTexImage2D(0x0DE1, 0, 0x1907, 0, 0, W, H, 0)   # GL_RGB
        for p, val in ((0x2801, 0x2601), (0x2800, 0x2601), (0x2802, 0x812F), (0x2803, 0x812F)): gl.glTexParameteri(0x0DE1, p, val)
        copySize[:] = [W, H]
        how = 'glCopyTexImage2D'
    else:
        gl.glCopyTexSubImage2D(0x0DE1, 0, 0, 0, 0, 0, W, H)
        how = 'glCopyTexSubImage2D'
    gl.glBindTexture(0x0DE1, bound); gl.glActiveTexture(unit)
    gl.glBindFramebuffer(0x8CA9, draw); gl.glBindFramebuffer(0x8CA8, read)
    return gl.glGetError(), how

VS = b"attribute vec2 aPos;\nattribute vec2 aUV;\nvarying vec2 vUV;\nvoid main() { vUV = aUV; gl_Position = vec4(aPos, 0.0, 1.0); }\n"
FS = b"precision mediump float;\nvarying vec2 vUV;\nuniform sampler2D uTex;\nvoid main() { gl_FragColor = vec4(texture2D(uTex, vUV).rgb, 1.0); }\n"
prog = [0]
def blit_on_b():
    """As SecondScreenHost.DrawOn + Blit with CopyQuad; returns (error, top row colour, bottom row colour)."""
    assert egl.eglMakeCurrent(dpy, surfB, surfB, ctxB)
    if not prog[0]:
        def sh(kind, src):
            s = gl.glCreateShader(kind); p = C.c_char_p(src); gl.glShaderSource(s, 1, C.byref(p), None); gl.glCompileShader(s)
            ok = C.c_int(); gl.glGetShaderiv(s, 0x8B81, C.byref(ok)); assert ok.value, 'compile'; return s
        p = gl.glCreateProgram(); gl.glAttachShader(p, sh(0x8B31, VS)); gl.glAttachShader(p, sh(0x8B30, FS))
        gl.glBindAttribLocation(p, 0, b"aPos"); gl.glBindAttribLocation(p, 1, b"aUV"); gl.glLinkProgram(p)
        ok = C.c_int(); gl.glGetProgramiv(p, 0x8B82, C.byref(ok)); assert ok.value, 'link'
        prog[0] = p
    quad = (C.c_float * 16)(-1, -1, 0, 0,  1, -1, 1, 0,  -1, 1, 0, 1,  1, 1, 1, 1)   # CopyQuad
    gl.glViewport(0, 0, BW, BH)
    for cap in (0x0BE2, 0x0B71, 0x0C11, 0x0B44): gl.glDisable(cap)
    gl.glUseProgram(prog[0]); gl.glActiveTexture(0x84C0); gl.glBindTexture(0x0DE1, copyTex)
    for p, val in ((0x2801, 0x2601), (0x2800, 0x2601), (0x2802, 0x812F), (0x2803, 0x812F)): gl.glTexParameteri(0x0DE1, p, val)
    gl.glUniform1i(gl.glGetUniformLocation(prog[0], b"uTex"), 0)
    gl.glVertexAttribPointer(0, 2, 0x1406, 0, 16, C.byref(quad, 0)); gl.glEnableVertexAttribArray(0)
    gl.glVertexAttribPointer(1, 2, 0x1406, 0, 16, C.byref(quad, 8)); gl.glEnableVertexAttribArray(1)
    gl.glDrawArrays(0x0005, 0, 4)
    err = gl.glGetError()
    px = (C.c_ubyte * (BW * BH * 4))(); gl.glReadPixels(0, 0, BW, BH, 0x1908, 0x1401, px)
    top = tuple(px[((BH - 1) * BW) * 4: ((BH - 1) * BW) * 4 + 3]); bottom = tuple(px[0:3])
    egl.eglSwapBuffers(dpy, surfB)
    assert egl.eglMakeCurrent(dpy, surfA, surfA, ctxA)
    return err, top, bottom

good = True
for frame, (top, bottom) in enumerate((((1, 0, 0), (0, 0, 1)), ((0, 0, 1), (1, 0, 0)))):
    draw_frame(top, bottom)
    gl.glActiveTexture(0x84C3)
    err, how = copy_game_frame()
    after = (geti(0x84E0), (gl.glActiveTexture(0x84C0), geti(0x8069))[1], geti(0x8CA6))
    gl.glActiveTexture(0x84C3)
    readFbo = geti(0x8CAA)
    gl.glFlush()
    berr, btop, bbottom = blit_on_b()
    want_top = tuple(int(c * 255) for c in top); want_bottom = tuple(int(c * 255) for c in bottom)
    ok = err == 0 and berr == 0 and btop == want_top and bbottom == want_bottom and after == before and readFbo == fbo.value
    print(f'frame {frame + 1} ({how}): copy error {hex(err)}, draw error {hex(berr)} | top {btop} (want {want_top}) | '
          f'bottom {bbottom} (want {want_bottom}) | game bindings kept: {after == before and readFbo == fbo.value}')
    good = good and ok
backA = egl.eglGetCurrentContext() == ctxA.value
print('game context current again:', backA)
good = good and backA
print('PASS' if good else 'FAIL'); sys.exit(0 if good else 1)
