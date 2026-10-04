# Step 2 sheets from a shots run (lens_gate.py's views): python step2_sheets.py <run dir with land/ port/> <out dir>
#  lens-<o>.jpg: per gate view, full trace | lens table | |difference| x16 | fallback map (red: full trace)
#  <view>-<o>-before.jpg / -after.jpg: the step 1b renderer (full trace, thin disc) and step 2 (table, thick disc)
#  before-after-<o>.jpg: the same pairs on one sheet
import sys, os
import numpy as np
from PIL import Image, ImageDraw

VIEWS = ['bh-edge-on', 'bh-inside-disc', 'bh-photon-ring-thin', 'bh-kerr-0.998-guides', 'bh-thick-edge', 'bh-hotspot']
GATE = VIEWS[:4]
run, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)

def im(o, n):
    return Image.open(os.path.join(run, o, n + '.png')).convert('RGB')

def label(img, text):
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 8 * len(text) + 10, 18], fill=(0, 0, 0))
    d.text((5, 3), text, fill=(255, 255, 255))
    return img

for o in ('land', 'port'):
    w, h = im(o, VIEWS[0]).size
    s = 0.25 if o == 'land' else 0.2
    tw, th = int(w * s), int(h * s)
    sheet = Image.new('RGB', (tw * 4, th * len(GATE)))
    for r, v in enumerate(GATE):
        full, tab = im(o, v + '-full'), im(o, v)
        diff = np.clip(np.abs(np.asarray(full, int) - np.asarray(tab, int)).max(2) * 16, 0, 255).astype(np.uint8)
        tiles = [full, tab, Image.fromarray(np.repeat(diff[..., None], 3, 2)), im(o, v + '-fallback')]
        names = ['full trace', 'lens table', '|diff| x16', 'fallback (red)']
        for c, (t, nm) in enumerate(zip(tiles, names)):
            sheet.paste(label(t.resize((tw, th), Image.LANCZOS), f'{v} {nm}' if c == 0 else nm), (c * tw, r * th))
    sheet.save(os.path.join(out, f'lens-{o}.jpg'), quality=90)

    s = 0.5 if o == 'land' else 0.3
    tw, th = int(w * s), int(h * s)
    ba = Image.new('RGB', (tw * 2, th * len(VIEWS)) if o == 'land' else (tw * len(VIEWS), th * 2))
    for r, v in enumerate(VIEWS):
        b4, af = im(o, v + '-1b'), im(o, v)
        short = v.replace('bh-', '')
        b4.save(os.path.join(out, f'{short}-{o}-before.jpg'), quality=92)
        af.save(os.path.join(out, f'{short}-{o}-after.jpg'), quality=92)
        for c, (t, nm) in enumerate([(b4, 'before (1b)'), (af, 'after (step 2)')]):
            pos = (c * tw, r * th) if o == 'land' else (r * tw, c * th)
            ba.paste(label(t.resize((tw, th), Image.LANCZOS), f'{short} {nm}'), pos)
    ba.save(os.path.join(out, f'before-after-{o}.jpg'), quality=88)
print('ok', out)
