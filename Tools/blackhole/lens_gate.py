# Step 2 gate: the lens table against the full trace. python lens_gate.py <shots dir> [out dir for diff images]
# For each gate view the harness renders <view>.png (table), <view>-full.png (full trace everywhere), <view>-fallback.png
# (red: pixel took the full trace, green: table, 2 samples, yellow: table, 8 samples) and <view>-fate.png (ray fates: red captured, white opaque disc).
# Reports the mean absolute difference (% of full scale, gate < 1 %), the fallback fraction, and where the visible
# differences are: their distance to the critical curve (the edge of the captured region).
import sys, os
import numpy as np
from PIL import Image
from scipy import ndimage

VIEWS = ['bh-edge-on', 'bh-inside-disc', 'bh-photon-ring-thin', 'bh-kerr-0.998-guides', 'bh-thick-edge', 'bh-hotspot']
VISIBLE = 4   # a channel difference of more than 4/255 counts as visible

def load(p):
    return np.asarray(Image.open(p).convert('RGB')).astype(np.int32)

def main(d, out, sfx=''):
    print(f'{"view":24s} {"mean |diff|":>11s} {"gate":>5s} {"fallback":>8s} {"8 spp":>6s} {"8x8 tiles":>9s} {"visible px":>10s} {"of which >1 px":>14s} {">1 px":>7s}  worst')
    for v in VIEWS:
        pa, pb = os.path.join(d, v + sfx + '.png'), os.path.join(d, v + '-full.png')
        if not (os.path.exists(pa) and os.path.exists(pb)): continue
        A, B = load(pa), load(pb)
        diff = np.abs(A - B)
        mean = diff.mean() / 255 * 100
        fb = load(os.path.join(d, v + sfx + '-fallback.png'))
        red, green = (fb[..., 0] > 128) & (fb[..., 1] < 128), (fb[..., 1] > 128) & (fb[..., 0] < 128)
        yellow = (fb[..., 0] > 128) & (fb[..., 1] > 128) & (fb[..., 2] < 128)   # adaptive: 8 samples
        frac = red.sum() / max(red.sum() + green.sum() + yellow.sum(), 1)
        refined = yellow.sum() / max(green.sum() + yellow.sum(), 1)
        # GPU cost follows thread groups, not pixels: the share of 8 x 8 tiles with at least one 8-sample pixel
        h8, w8 = yellow.shape[0] // 8 * 8, yellow.shape[1] // 8 * 8
        ty = yellow[:h8, :w8].reshape(h8 // 8, 8, w8 // 8, 8).any((1, 3))
        tt = (yellow | green)[:h8, :w8].reshape(h8 // 8, 8, w8 // 8, 8).any((1, 3))
        tiles = ty.sum() / max(tt.sum(), 1)
        fate = load(os.path.join(d, v + '-fate.png'))
        captured = (fate[..., 0] > 128) & (fate[..., 1] < 128) & (fate[..., 2] < 128)
        edge = captured & ~ndimage.binary_erosion(captured) | ~captured & ndimage.binary_dilation(captured)
        dist = ndimage.distance_transform_edt(~edge) if edge.any() else np.full(captured.shape, 1e9)
        vis = diff.max(2) > VISIBLE
        far = vis & (dist > 1.5)
        worst = diff.max()
        print(f'{v:24s} {mean:10.3f}% {"ok" if mean < 1 else "FAIL":>5s} {frac * 100:7.1f}% {refined * 100:5.1f}% {tiles * 100:8.1f}% {vis.mean() * 100:9.3f}% {far.sum():14d} {far.mean() * 100:6.3f}%  {worst}')
        if out:
            os.makedirs(out, exist_ok=True)
            # diff × 16 in grey, the critical curve in cyan, visible differences farther than 1 px from it in red
            img = np.repeat(np.clip(diff.max(2) * 16, 0, 255)[..., None], 3, 2).astype(np.uint8)
            img[edge] = (0, 160, 160)
            img[far] = (255, 40, 40)
            Image.fromarray(img).save(os.path.join(out, v + sfx + '-diff.png'))

if __name__ == '__main__':
    # optional third argument: a variant suffix (e.g. -q1) whose <view><suffix>.png and -fallback map are gated instead
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 and sys.argv[2] else None, sys.argv[3] if len(sys.argv) > 3 else '')
