"""Make the phone (GLES) grass effect sharper for distant ground in 3D: GrassShaderiOS.xnb with one change to its 3D
base pass (DrawBase/MainPass3D). From a distance the 3D ground draws no blades, only a grain made from a mipmapped noise
texture mixed into the ground colour; mipmapping blurs that grain to almost nothing (the ground looks like flat paint).
Sampling the noise one mip level sharper (a bias of -1) keeps about twice the grain. Everything else is byte-for-byte.
usage: make_grass.py in.xnb out.xnb"""
import sys
sys.path.insert(0, __import__('os').path.dirname(__file__))
import mgfx

src, dst = sys.argv[1:3]
data = open(src, 'rb').read()
prefix, fx, suffix = mgfx.parse_xnb(data)
header, pre, shaders, post = mgfx.parse_mgfx(fx)
params, techs, _ = mgfx.parse_post(post)

ps = dict(techs)['DrawBase'][2]
assert ps[0] == 'MainPass3D', ps
sh = shaders[ps[2]]
assert not sh.vertex
# the noise texture is the base pass's third sampler; it's read once, with the grain's coordinates
names = [s[0] for s in sh.samplers]
assert names == ['ps_s0', 'ps_s1', 'ps_s2'], names
old = 'texture2D(ps_s2, ps_r0.xw);'
assert sh.code.count(old) == 1 and sh.code.count('texture2D(ps_s2') == 1, 'unexpected 3D base pass'
sh.code = sh.code.replace(old, 'texture2D(ps_s2, ps_r0.xw, -1.0);')

open(dst, 'wb').write(mgfx.build_xnb(prefix, mgfx.build_mgfx(header, pre, shaders, post), suffix))
print('wrote', dst)
