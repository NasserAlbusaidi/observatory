# Before/after checks for the black hole visuals: python compare.py <before_dir> <after_dir>
# - debug views (dbg-*.png) must be pixel-identical
# - mean brightness and left/right (approaching/receding) contrast of the main shots
# - isolated dark pixels (specks) in the after renders
import sys, os, glob
import numpy as np
from PIL import Image

before, after = sys.argv[1], sys.argv[2]

def load(p):
    return np.asarray(Image.open(p).convert('RGB')).astype(np.int32)

def specks(a, thr=80):
    L = a.sum(2)
    c = L[1:-1, 1:-1]
    nb = np.stack([L[1 + dy:L.shape[0] - 1 + dy, 1 + dx:L.shape[1] - 1 + dx] for dy in (-1, 0, 1) for dx in (-1, 0, 1) if dy or dx])
    return int(((c < 30) & (nb.min(0) > thr)).sum())

def luma(a):
    return (0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2])

for pa in sorted(glob.glob(os.path.join(after, '*.png'))):
    name = os.path.basename(pa)
    pb = os.path.join(before, name)
    a = load(pa)
    line = f'{name:34s}'
    if os.path.exists(pb):
        b = load(pb)
        if name.startswith('dbg-'):
            diff = int((a != b).any(2).sum())
            line += f' debug view: {"IDENTICAL" if diff == 0 else str(diff) + " pixels differ"}'
            print(line); continue
        line += f' mean {luma(b).mean():6.1f} -> {luma(a).mean():6.1f}'
    if not name.startswith('dbg-'):
        h, w = a.shape[:2]
        # left and right thirds of the middle band (edge-on views: approaching side left)
        band = luma(a)[int(h * 0.42):int(h * 0.58)]
        l, r = band[:, int(w * 0.2):int(w * 0.4)].mean(), band[:, int(w * 0.6):int(w * 0.8)].mean()
        line += f'  left/right {l:5.1f}/{r:5.1f}  specks {specks(a)}'
    print(line)
