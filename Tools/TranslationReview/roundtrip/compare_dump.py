"""Independent check (issue #4): decode each master data table as the game stores it and as
the plugin hands it back, with Python's msgpack + lz4 instead of the mod's own code, and
require that the only differences are Japanese text replaced by other text."""
import msgpack, lz4.block, os, re, sys, collections
JP = re.compile(r'[぀-ヿ㐀-鿿]')

def ext_hook(code, data):
    if code == 99:  # MessagePack-CSharp Lz4Block: msgpack int (raw length) + LZ4 block
        u = msgpack.Unpacker(); u.feed(data); n = u.unpack(); off = u.tell()
        return unpack(lz4.block.decompress(data[off:], uncompressed_size=n))
    return msgpack.ExtType(code, data)

def fix_arrays(o):
    # Lz4BlockArray: [ext98(header of raw lengths), bin, bin, ...]
    if isinstance(o, list):
        if o and isinstance(o[0], msgpack.ExtType) and o[0].code == 98:
            lengths = msgpack.unpackb(o[0].data)
            lengths = lengths if isinstance(lengths, list) else [lengths]
            raw = b''.join(lz4.block.decompress(b, uncompressed_size=n) for b, n in zip(o[1:], lengths))
            return unpack(raw)
        return [fix_arrays(x) for x in o]
    if isinstance(o, dict): return {k: fix_arrays(v) for k, v in o.items()}
    return o

def unpack(b): return fix_arrays(msgpack.unpackb(b, ext_hook=ext_hook, raw=False, strict_map_key=False))

stats = collections.Counter(); bad = []
def walk(a, b, path):
    if type(a) != type(b):
        bad.append((path, repr(a)[:60], repr(b)[:60])); return
    if isinstance(a, list):
        if len(a) != len(b): bad.append((path, f'len {len(a)}', f'len {len(b)}')); return
        for i, (x, y) in enumerate(zip(a, b)): walk(x, y, f'{path}[{i}]')
    elif isinstance(a, dict):
        if a.keys() != b.keys(): bad.append((path, 'keys', 'keys')); return
        for k in a: walk(a[k], b[k], f'{path}.{k}')
    elif a != b:
        if isinstance(a, str) and JP.search(a): stats['japanese text replaced'] += 1
        else: bad.append((path, repr(a)[:60], repr(b)[:60]))
    else:
        stats['unchanged'] += 1

d = sys.argv[1]; tables = sorted(f[:-5] for f in os.listdir(d) if f.endswith('.orig'))
for t in tables:
    walk(unpack(open(os.path.join(d, t + '.orig'), 'rb').read()), unpack(open(os.path.join(d, t + '.game'), 'rb').read()), t)
print(f'{len(tables)} tables: {stats["unchanged"]} values unchanged, {stats["japanese text replaced"]} Japanese strings replaced, {len(bad)} OTHER differences')
for x in bad[:20]: print('  ', x)
