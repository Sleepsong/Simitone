"""Make 2DWorldBatchFetch.xnb from the GLES 2D world effect (2DWorldBatchiOS.xnb): every shader becomes GLSL ES 3.00;
pixel shaders write colour to draw buffer 0 and keep the packed depth image in draw buffer 1, read through
GL_EXT_shader_framebuffer_fetch. Depth-tested sprites compare against that value and write their own depth in the same
pass, replacing the colour pass / stencil / depth pass ping-pong (two render target switches per sprite group).
Also writes a plain-text copy of the shaders so the app can test-compile them at start-up and fall back if they fail.
usage: make_fetch.py in.xnb out.xnb out_shaders.txt"""
import sys, re
sys.path.insert(0, __import__('os').path.dirname(__file__))
import mgfx

src, dst, txt = sys.argv[1:4]
data = open(src, 'rb').read()
prefix, fx, suffix = mgfx.parse_xnb(data)
header, pre, shaders, post = mgfx.parse_mgfx(fx)
params, techs, _ = mgfx.parse_post(post)
pnames = [p[0] for p in params]

HEAD = "#version 300 es\n"
FETCH = "#extension GL_EXT_shader_framebuffer_fetch : require\n"
PACK = """highp vec4 fsPack(highp float d) {
	highp vec4 e = fract(vec4(1.0, 255.0, 65025.0, 0.0) * d);
	e.xyz -= e.yzw * (1.0 / 255.0);
	e.w = 1.0;
	return e;
}
"""

def convert_vs(code):
    code = code.replace("\r\n", "\n")
    assert code.startswith('#ifdef GL_ES')
    code = HEAD + code
    code = re.sub(r'\battribute vec4 ', 'in vec4 ', code)
    code = re.sub(r'\bvarying vec4 ', 'out vec4 ', code)
    assert 'texture2D' not in code
    return code

def last_main_brace(code):
    i = code.rindex('}')
    return i

def convert_ps(k, sh):
    code = sh.code.replace("\r\n", "\n")
    assert code.startswith('#ifdef GL_ES'), k
    code = HEAD + FETCH + code
    code = re.sub(r'\bvarying vec4 ', 'in vec4 ', code)
    assert code.count('#define ps_oC0 gl_FragColor') == 1, k
    code = code.replace('#define ps_oC0 gl_FragColor',
        'layout(location = 0) out vec4 fsColor;\nlayout(location = 1) inout highp vec4 fsDepth;\n#define ps_oC0 fsColor')
    # the depth image is no longer a texture: read the value already in draw buffer 1 at this pixel
    dm = [n for (n, t, s, p) in sh.samplers if pnames[p] == 'depthMap']
    for n in dm:
        code, c = re.subn(r'texture2D\(%s, [^)]*\)' % n, 'fsDepth', code)
        assert c == 1, (k, c)
    code = code.replace('texture2D(', 'texture(')
    assert 'gl_Frag' not in code, k
    # write this sprite's depth where it drew (depth-channel shaders)
    m = re.search(r'\n(\s*)ps_r\d = ps_r(\d)\.([xyzw])\3\3\3 \* ps_c\d+;\n\s*ps_r\d = fract\(', code)
    writes = False
    if k == 11:
        anchor = '\t\tps_oC0 = ((ps_r1.y >= 0.0) ? ps_r2 : ps_r0);'
        assert code.count(anchor) == 1
        code = code.replace(anchor, '\t\tfsDepth = fsPack(ps_r1.x);\n' + anchor)
        writes = True
    elif m and dm:
        reg, comp = m.group(2), m.group(3)
        # the depthOutMode branch starts at the last "if (" before the pack: capture the depth just before it
        ifpos = code.rindex('\n\tif (', 0, m.start())
        code = code[:ifpos] + '\n\thighp float fsD = ps_r%s.%s;' % (reg, comp) + code[ifpos:]
        end = last_main_brace(code)
        code = code[:end] + '\tfsDepth = fsPack(fsD);\n' + code[end:]
        writes = True
    if writes:
        mi = code.index('void main()')
        code = code[:mi] + PACK + '\n' + code[mi:]
    return code, writes, bool(dm)

report = []
for k, sh in enumerate(shaders):
    if sh.vertex:
        sh.code = convert_vs(sh.code); report.append('%d VS' % k)
    else:
        sh.code, w, t = convert_ps(k, sh)
        report.append('%d PS%s%s' % (k, ' depth-test' if t else '', ' depth-write' if w else ''))

# a different cache key, so this never shares MonoGame's effect cache entry with the original
key = int.from_bytes(header[6:10], 'little', signed=True) ^ 0x46455443
fx2 = mgfx.build_mgfx(header, pre, shaders, post, key=key)
open(dst, 'wb').write(mgfx.build_xnb(prefix, fx2, suffix))

with open(txt, 'w') as f:
    for k, sh in enumerate(shaders):
        f.write('@@shader %d %s\n' % (k, 'vertex' if sh.vertex else 'pixel'))
        f.write(sh.code.rstrip('\n') + '\n')
    for t, ps in techs:
        for pname, vs, psi in ps:
            f.write('@@program %s.%s %d %d\n' % (t, pname, vs, psi))
print('\n'.join(report))
