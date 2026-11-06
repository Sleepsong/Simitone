"""Behaviour test on Mesa GLES 3.2: two RGBA8 draw buffers (colour, packed depth), depth image cleared to white.
1) a 'far' red quad, then a 'near' green quad overlapping half of it, then a 'farther' blue quad over everything:
   blue must lose where red/green are and win elsewhere. 2) a shader that never writes draw buffer 1 (inout untouched)
   must leave the depth image as it was, under alpha blending."""
import ctypes as C, sys
sys.argv = [sys.argv[0]]
exec(open(__import__('os').path.join(__import__('os').path.dirname(__file__), 'gles_check.py')).read())
W = H = 8
def tex():
    t = C.c_uint(); gl.glGenTextures(1, C.byref(t)); gl.glBindTexture(0x0DE1, t)
    gl.glTexImage2D(0x0DE1, 0, 0x8058, W, H, 0, 0x1908, 0x1401, None)  # RGBA8
    gl.glTexParameteri(0x0DE1, 0x2801, 0x2600); gl.glTexParameteri(0x0DE1, 0x2800, 0x2600); return t
col, dep = tex(), tex()
fbo = C.c_uint(); gl.glGenFramebuffers(1, C.byref(fbo)); gl.glBindFramebuffer(0x8D40, fbo)
gl.glFramebufferTexture2D(0x8D40, 0x8CE0, 0x0DE1, col, 0); gl.glFramebufferTexture2D(0x8D40, 0x8CE1, 0x0DE1, dep, 0)
bufs = (C.c_uint * 2)(0x8CE0, 0x8CE1); gl.glDrawBuffers(2, bufs)
print('fbo complete:', gl.glCheckFramebufferStatus(0x8D40) == 0x8CD5)
gl.glViewport(0, 0, W, H)
# clear colour to transparent black, depth image to white (as the game does)
gl.glDrawBuffers(1, bufs); gl.glClearColor(C.c_float(0), C.c_float(0), C.c_float(0), C.c_float(0)); gl.glClear(0x4000)
one = (C.c_uint * 2)(0, 0x8CE1); gl.glDrawBuffers(2, one); gl.glClearColor(C.c_float(1), C.c_float(1), C.c_float(1), C.c_float(1)); gl.glClear(0x4000)
gl.glDrawBuffers(2, bufs)
VS = """#version 300 es
in vec4 pos; void main(){ gl_Position = pos; }"""
FS_DEPTH = """#version 300 es
#extension GL_EXT_shader_framebuffer_fetch : require
precision highp float;
uniform vec4 colour; uniform float depth;
layout(location = 0) out vec4 fsColor; layout(location = 1) inout highp vec4 fsDepth;
highp vec4 fsPack(highp float d){ highp vec4 e = fract(vec4(1.0,255.0,65025.0,0.0)*d); e.xyz -= e.yzw*(1.0/255.0); e.w = 1.0; return e; }
void main(){ if (dot(fsDepth.xyz, vec3(1.0, 0.003922, 0.000015)) < depth) discard; fsColor = colour; fsDepth = fsPack(depth); }"""
FS_PLAIN = """#version 300 es
#extension GL_EXT_shader_framebuffer_fetch : require
precision highp float;
layout(location = 0) out vec4 fsColor; layout(location = 1) inout highp vec4 fsDepth;
void main(){ fsColor = vec4(0.0, 0.0, 0.0, 0.5); }"""
vs, ok, log = compile('vertex', VS); assert ok, log
fd, ok, log = compile('pixel', FS_DEPTH); assert ok, log
fp, ok, log = compile('pixel', FS_PLAIN); assert ok, log
pd, ok, log = link(vs, fd); assert ok, log
pp, ok, log = link(vs, fp); assert ok, log
gl.glEnable(0x0BE2); gl.glBlendFunc(1, 0x0303)  # AlphaBlend: One, InvSrcAlpha
def quad(prog, x0, x1, colour=None, depth=None):
    gl.glUseProgram(prog)
    if colour: gl.glUniform4f(gl.glGetUniformLocation(prog, b'colour'), *[C.c_float(c) for c in colour])
    if depth is not None: gl.glUniform1f(gl.glGetUniformLocation(prog, b'depth'), C.c_float(depth))
    v = (C.c_float * 8)(x0, -1, x1, -1, x0, 1, x1, 1)
    loc = gl.glGetAttribLocation(prog, b'pos'); gl.glEnableVertexAttribArray(loc)
    gl.glVertexAttribPointer(loc, 2, 0x1406, 0, 0, v); gl.glDrawArrays(5, 0, 4)
quad(pd, -1.0, 0.0, (1, 0, 0, 1), 0.6)   # red, far, left half
quad(pd, -0.5, 0.5, (0, 1, 0, 1), 0.3)   # green, near, middle
quad(pd, -1.0, 1.0, (0, 0, 1, 1), 0.8)   # blue, farthest, everywhere
def read(att):
    gl.glReadBuffer(att); px = (C.c_ubyte * (W * H * 4))(); gl.glReadPixels(0, 0, W, H, 0x1908, 0x1401, px)
    return [tuple(px[(H // 2 * W + x) * 4:(H // 2 * W + x) * 4 + 4]) for x in range(W)]
c = read(0x8CE0); d = read(0x8CE1)
names = {(255,0,0,255): 'R', (0,255,0,255): 'G', (0,0,255,255): 'B'}
row = ''.join(names.get(p, '?') for p in c); print('colour row :', row, '(expect RRGGGGBB)')
before = d[:]
quad(pp, -1.0, 1.0)
after = read(0x8CE1)
print('depth kept by a shader that doesn\'t write it:', before == after)
unpack = lambda p: p[0]/255 + p[1]/255/255 + p[2]/255/65025
print('depth row  :', ' '.join('%.2f' % unpack(p) for p in after), '(expect 0.60 0.60 0.30 x4 0.80 x2)')
print('PASS' if row == 'RRGGGGBB' and before == after else 'FAIL')
