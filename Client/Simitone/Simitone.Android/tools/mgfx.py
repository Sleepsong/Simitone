"""Read and write MonoGame XNB-wrapped MGFX effects (MonoGame 3.8, MGFX version 10), keeping everything but the
shaders' GLSL byte-for-byte. Used to make the one-pass phone (GLES) 2D world shader for the Android build (make_fetch.py)."""
import struct, io

def read7(b, i):
    r = 0; s = 0
    while True:
        c = b[i]; i += 1
        r |= (c & 0x7f) << s; s += 7
        if not c & 0x80: return r, i

def write7(n):
    out = bytearray()
    while True:
        c = n & 0x7f; n >>= 7
        if n: out.append(c | 0x80)
        else: out.append(c); return bytes(out)

def rstr(b, i):
    n, i = read7(b, i); return b[i:i+n].decode('utf-8'), i + n

class Shader:
    pass

def parse_xnb(data):
    assert data[:3] == b'XNB', 'not an xnb'
    flags = data[5]
    assert not (flags & 0xC0), 'compressed xnb not supported'
    i = 10
    nreaders, i = read7(data, i)
    readers = []
    for _ in range(nreaders):
        name, i = rstr(data, i); ver = struct.unpack_from('<i', data, i)[0]; i += 4
        readers.append((name, ver))
    shared, i = read7(data, i)
    ridx, i = read7(data, i)
    fxlen = struct.unpack_from('<i', data, i)[0]; i += 4
    prefix = data[:i - 4]
    fx = data[i:i + fxlen]
    suffix = data[i + fxlen:]
    return prefix, fx, suffix

def parse_mgfx(fx):
    """returns (header(10 bytes), pre_shaders bytes, shaders list, post bytes)"""
    assert fx[:4] == b'MGFX'
    header = fx[:10]
    b = fx; i = 10
    start = i
    ncb = struct.unpack_from('<i', b, i)[0]; i += 4
    for _ in range(ncb):
        _, i = rstr(b, i); i += 2
        np_ = struct.unpack_from('<i', b, i)[0]; i += 4
        i += np_ * 6
    pre = b[start:i]
    nsh = struct.unpack_from('<i', b, i)[0]; i += 4
    shaders = []
    for _ in range(nsh):
        sh = Shader(); s0 = i
        sh.vertex = b[i] != 0; i += 1
        ln = struct.unpack_from('<i', b, i)[0]; i += 4
        sh.code = b[i:i+ln].decode('ascii'); i += ln
        t0 = i
        ns = b[i]; i += 1
        sh.samplers = []
        for _ in range(ns):
            typ, tslot, sslot = b[i], b[i+1], b[i+2]; i += 3
            has = b[i]; i += 1
            if has: i += 3 + 4 + 1 + 4 + 4 + 4
            name, i = rstr(b, i); param = b[i]; i += 1
            sh.samplers.append((name, tslot, sslot, param))
        ncbuf = b[i]; i += 1; i += ncbuf
        na = b[i]; i += 1
        sh.attributes = []
        for _ in range(na):
            name, i = rstr(b, i); usage, idx = b[i], b[i+1]; i += 2; loc = struct.unpack_from('<h', b, i)[0]; i += 2
            sh.attributes.append((name, usage, idx))
        sh.tail = b[t0:i]
        shaders.append(sh)
    post = b[i:]
    return header, pre, shaders, post

def build_mgfx(header, pre, shaders, post, key=None):
    out = bytearray(header)
    if key is not None: out[6:10] = struct.pack('<i', key)
    out += pre
    out += struct.pack('<i', len(shaders))
    for sh in shaders:
        code = sh.code.encode('ascii')
        out += bytes([1 if sh.vertex else 0]) + struct.pack('<i', len(code)) + code + sh.tail
    out += post
    return bytes(out)

def build_xnb(prefix, fx, suffix):
    body = bytearray(prefix) + struct.pack('<i', len(fx)) + fx + suffix
    struct.pack_into('<i', body, 6, len(body))
    return bytes(body)

# EffectParameterType values (MonoGame): Void 0, Bool 1, Int32 2, Single 3, String 4, Texture... 5+
def _read_params(b, i, out, depth=0):
    n = struct.unpack_from('<i', b, i)[0]; i += 4
    for _ in range(n):
        cls, typ = b[i], b[i+1]; i += 2
        name, i = rstr(b, i); sem, i = rstr(b, i)
        na = struct.unpack_from('<i', b, i)[0]; i += 4
        assert na == 0, 'annotations not supported'
        rows, cols = b[i], b[i+1]; i += 2
        before = len(out)
        i, ne = _read_params(b, i, [], depth + 1)
        i, nm = _read_params(b, i, [], depth + 1)
        if ne == 0 and nm == 0 and typ in (1, 2, 3):
            i += rows * cols * 4
        if depth == 0: out.append((name, typ, rows, cols))
    return i, n

def parse_post(post):
    """parameters and techniques: [(technique, [(pass, vs index, ps index)])]"""
    b = post; params = []
    i, _ = _read_params(b, 0, params)
    nt = struct.unpack_from('<i', b, i)[0]; i += 4
    techs = []
    for _ in range(nt):
        tname, i = rstr(b, i)
        na = struct.unpack_from('<i', b, i)[0]; i += 4; assert na == 0
        np_ = struct.unpack_from('<i', b, i)[0]; i += 4
        passes = []
        for _ in range(np_):
            pname, i = rstr(b, i)
            na = struct.unpack_from('<i', b, i)[0]; i += 4; assert na == 0
            vs, ps = struct.unpack_from('<ii', b, i); i += 8
            if b[i]: i += 1 + 18
            else: i += 1
            if b[i]: i += 1 + 4 + 3 + 4 + 4 + 4 + 4 + 4 + 1
            else: i += 1
            if b[i]: i += 1 + 1 + 4 + 1 + 1 + 1 + 4
            else: i += 1
            passes.append((pname, vs, ps))
        techs.append((tname, passes))
    return params, techs, i
