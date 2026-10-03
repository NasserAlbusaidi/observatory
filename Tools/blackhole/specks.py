# Count isolated dark pixels (dark, all 8 neighbours bright) in renders: QA-034 check.
import sys, numpy as np
from PIL import Image
for n in sys.argv[1:]:
    L = np.asarray(Image.open(n).convert('RGB')).astype(int).sum(2)
    c = L[1:-1, 1:-1]
    nb = np.stack([L[1+dy:L.shape[0]-1+dy, 1+dx:L.shape[1]-1+dx] for dy in (-1,0,1) for dx in (-1,0,1) if dy or dx])
    for thr in (150, 80):
        iso = (c < 30) & (nb.min(0) > thr)
        ys, xs = np.nonzero(iso)
        print(n, f'neighbours>{thr}:', len(xs), list(zip((xs+1)[:6], (ys+1)[:6])))
