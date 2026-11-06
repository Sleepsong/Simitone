"""Compile/link the patched GLES shaders on Mesa (surfaceless EGL, GLES 3.x) and test MRT + framebuffer fetch."""
import ctypes as C, sys, struct
egl = C.CDLL('libEGL.so.1'); gl = C.CDLL('libGLESv2.so.2')
egl.eglGetProcAddress.restype = C.c_void_p
for f in ('eglGetDisplay','eglGetPlatformDisplay','eglCreateContext'): getattr(egl, f).restype = C.c_void_p
gl.glGetString.restype = C.c_char_p
EGL_PLATFORM_SURFACELESS_MESA = 0x31DD
dpy = egl.eglGetPlatformDisplay(EGL_PLATFORM_SURFACELESS_MESA, None, None)
maj, mnr = C.c_int(), C.c_int()
assert egl.eglInitialize(C.c_void_p(dpy), C.byref(maj), C.byref(mnr)), 'eglInitialize'
egl.eglBindAPI(0x30A0)
attrs = (C.c_int * 7)(0x3040, 0x40, 0x3033, 0, 0x3038, 0, 0x3038)  # RENDERABLE_TYPE=ES3_BIT, SURFACE_TYPE=0
cfg = C.c_void_p(); n = C.c_int()
assert egl.eglChooseConfig(C.c_void_p(dpy), attrs, C.byref(cfg), 1, C.byref(n)) and n.value, 'no config'
ctxattr = (C.c_int * 5)(0x3098, 3, 0x30FB, 2, 0x3038)  # MAJOR 3, MINOR 2
ctx = egl.eglCreateContext(C.c_void_p(dpy), cfg, None, ctxattr)
if not ctx:
    ctxattr = (C.c_int * 3)(0x3098, 3, 0x3038); ctx = egl.eglCreateContext(C.c_void_p(dpy), cfg, None, ctxattr)
assert ctx, 'context'
assert egl.eglMakeCurrent(C.c_void_p(dpy), None, None, C.c_void_p(ctx)), 'makecurrent'
ver = gl.glGetString(0x1F02).decode(); rend = gl.glGetString(0x1F01).decode(); ext = gl.glGetString(0x1F03).decode()
print('GL:', ver, '|', rend); fetch = 'GL_EXT_shader_framebuffer_fetch' in ext.split()
print('framebuffer fetch:', fetch, '| non-coherent:', 'GL_EXT_shader_framebuffer_fetch_non_coherent' in ext)

def compile(kind, src):
    s = gl.glCreateShader(0x8B31 if kind == 'vertex' else 0x8B30)
    b = src.encode(); p = C.c_char_p(b); ln = C.c_int(len(b))
    gl.glShaderSource(s, 1, C.byref(p), C.byref(ln)); gl.glCompileShader(s)
    ok = C.c_int(); gl.glGetShaderiv(s, 0x8B81, C.byref(ok))
    buf = C.create_string_buffer(8192); gl.glGetShaderInfoLog(s, 8192, None, buf)
    return s, ok.value, buf.value.decode(errors='replace')

def link(vs, ps):
    p = gl.glCreateProgram(); gl.glAttachShader(p, vs); gl.glAttachShader(p, ps); gl.glLinkProgram(p)
    ok = C.c_int(); gl.glGetProgramiv(p, 0x8B82, C.byref(ok))
    buf = C.create_string_buffer(8192); gl.glGetProgramInfoLog(p, 8192, None, buf)
    return p, ok.value, buf.value.decode(errors='replace')

if len(sys.argv) > 1:
    text = open(sys.argv[1]).read()
    shaders = {}; programs = []; cur = None
    for line in text.split('\n'):
        if line.startswith('@@shader '):
            _, k, kind = line.split(); cur = [int(k), kind, []]; shaders[int(k)] = cur
        elif line.startswith('@@program '):
            _, name, vs, ps = line.split(); programs.append((name, int(vs), int(ps))); cur = None
        elif cur is not None: cur[2].append(line)
    handles = {}; bad = 0
    for k, (k_, kind, lines) in sorted(shaders.items()):
        h, ok, log = compile(kind, '\n'.join(lines)); handles[k] = h
        print('shader %2d %-6s %s %s' % (k, kind, 'ok' if ok else 'FAILED', log.strip()[:600]))
        bad += not ok
    for name, vs, ps in programs:
        p, ok, log = link(handles[vs], handles[ps])
        print('program %-34s %s %s' % (name, 'ok' if ok else 'FAILED', log.strip()[:400])); bad += not ok
    print('failures:', bad)
