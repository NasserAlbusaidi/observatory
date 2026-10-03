"""Shared helpers for the fidelity map pipeline (numpy + tifffile + Pillow; no Blender or OIIO).

Convention for every output (same as the art pack and FOWCommon.cginc tex2DSphere):
    equirectangular, top row = north, longitude 0 at u = 0.5, east to the right.
"""
import json
import math
import os

import numpy as np
import tifffile
from PIL import Image

Image.MAX_IMAGE_PIXELS = None

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
RES = os.path.join(REPO, "Assets", "FogOfWar", "Resources")
SRC_MAPS = r"D:\SpaceSimArtSrc\maps"
SRC_NEW = r"D:\SpaceSimArtSrc\fidelity"
# outputs go here; set FIDELITY_OUT to stage them outside the project (e.g. while a build reads Resources)
OUT = os.environ.get("FIDELITY_OUT", RES)


def open_array(path):
    """Memory-mapped (or loaded) source array as (H, W, C). Handles band-first TIFFs and plain images."""
    if path.lower().endswith((".tif", ".tiff")):
        try:
            a = tifffile.memmap(path)
        except Exception:
            a = tifffile.imread(path)
    else:
        a = np.asarray(Image.open(path))
    if a.ndim == 2:
        a = a[..., None]
    elif a.ndim == 3 and a.shape[0] in (1, 3, 4) and a.shape[2] > 4:
        a = np.moveaxis(a, 0, -1)          # band-first -> band-last (a view on a memmap)
    return a


def default_valid(block):
    """Nodata rule: int16 -32768 (or any value below -32000), or 0 in every channel of 8-bit data."""
    if block.dtype == np.int16:
        return block[..., 0] > -32000
    if block.dtype == np.uint8:
        return block.max(-1) > 0
    return np.isfinite(block[..., 0])


def block_average(path, k, valid_fn=default_valid, channels=None, rows_per_strip=None):
    """Box-average the source by an integer factor k, counting only valid pixels. Streams in strips so a 2 GB TIFF
    never sits in RAM as float. Returns (mean float32 (h, w, c), coverage float32 (h, w) in 0..1)."""
    a = open_array(path)
    H, W = a.shape[:2]
    if channels is not None:
        a = a[..., channels]
    C = a.shape[2]
    h, w = H // k, W // k
    out = np.zeros((h, w, C), np.float32)
    cov = np.zeros((h, w), np.float32)
    step = max(1, (rows_per_strip or 4096) // k)
    for r0 in range(0, h, step):
        r1 = min(h, r0 + step)
        blk = np.asarray(a[r0 * k: r1 * k, : w * k])
        v = valid_fn(blk)
        f = blk.astype(np.float32) * v[..., None]
        num = f.reshape(r1 - r0, k, w, k, C).sum((1, 3))
        den = v.reshape(r1 - r0, k, w, k).sum((1, 3)).astype(np.float32)
        out[r0:r1] = num / np.maximum(den, 1)[..., None]
        cov[r0:r1] = den / (k * k)
    return out, cov


def _blur121(x):
    x = (np.roll(x, 1, 1) + 2 * x + np.roll(x, -1, 1)) / 4
    up = np.concatenate([x[:1], x[:-1]], 0)
    dn = np.concatenate([x[1:], x[-1:]], 0)
    return (up + 2 * x + dn) / 4


def fill_holes(a, hole):
    """Pull-push interpolation into missing pixels: smooth, continuous with the data edges, no added texture.
    (Unlike the art pack's version, no procedural grain is added: gaps stay honest, smooth fills.)"""
    if hole.mean() < 1e-7:
        return a
    w0 = (~hole).astype(np.float32)
    levels = [(a * w0[..., None], w0)]
    while levels[-1][1].min() == 0 and min(levels[-1][1].shape) > 2:
        num, den = levels[-1]
        hh, ww = den.shape
        h2, w2 = hh // 2 * 2, ww // 2 * 2
        num = num[:h2, :w2].reshape(h2 // 2, 2, w2 // 2, 2, -1).sum((1, 3))
        den = den[:h2, :w2].reshape(h2 // 2, 2, w2 // 2, 2).sum((1, 3))
        levels.append((num, den))
    num, den = levels[-1]
    est = num / np.maximum(den, 1e-6)[..., None]
    if den.min() == 0:
        est[den == 0] = num.sum((0, 1)) / max(den.sum(), 1e-6)
    for num, den in reversed(levels[:-1]):
        hh, ww = den.shape
        up = np.repeat(np.repeat(est, 2, 0), 2, 1)
        up = np.pad(up, ((0, hh - up.shape[0]), (0, ww - up.shape[1]), (0, 0)), mode="edge")
        for _ in range(2):
            up = _blur121(up)
        c = np.clip(den, 0, 1)[..., None]
        est = num / np.maximum(den, 1e-6)[..., None] * c + up * (1 - c)
    return np.where(hole[..., None], est, a)


def resize(a, w, h):
    """Lanczos resample of a float (h, w, c) array, channel by channel through Pillow's 32-bit float mode."""
    if a.shape[1] == w and a.shape[0] == h:
        return a
    chans = [np.asarray(Image.fromarray(np.ascontiguousarray(a[..., c], np.float32), "F").resize((w, h), Image.LANCZOS))
             for c in range(a.shape[2])]
    return np.stack(chans, -1)


def roll_to_centre(a, left_east_lon):
    """Shift columns so longitude 0 sits at u = 0.5 (left edge at -180 east)."""
    shift = int(round((left_east_lon + 180) / 360 * a.shape[1]))
    return np.roll(a, shift, axis=1)


def pow2_width(src_w, cap):
    """Largest power of two not above the source width (5% slack, so a 4040 px source maps to 4096), capped."""
    return min(cap, 1 << int(math.log2(src_w * 1.05)))


def load_global(path, w, valid_fn=default_valid, channels=None, scale=None):
    """Source -> (w/2, w) float array in 0..1 (or raw units if scale is None for int data), holes filled.
    Block-averages by the largest integer factor that keeps >= 2x the target (alias-free), then Lanczos."""
    a = open_array(path)
    H, W = a.shape[:2]
    k = max(1, -(-W // (w * 2)))  # ceil: keeps >= ~1.4x the target before Lanczos, and the float copy small
    m, cov = block_average(path, k, valid_fn, channels)
    hole = cov < 0.5
    m = fill_holes(m, hole)
    if scale is not None:
        m = m * scale
    return resize(m, w, w // 2)


def save_jpg(a, path, quality=95):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    u8 = (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)
    if u8.shape[-1] == 1:
        u8 = u8[..., 0]
    Image.fromarray(u8).save(path, quality=quality, subsampling=0, optimize=True)
    print("wrote", path, u8.shape)


def save_png(a, path, bits=8):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    if bits == 16:
        Image.fromarray((np.clip(a[..., 0] if a.ndim == 3 else a, 0, 1) * 65535 + 0.5).astype(np.uint16)).save(path)
    else:
        u8 = (np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)
        Image.fromarray(u8[..., 0] if u8.ndim == 3 and u8.shape[-1] == 1 else u8).save(path, optimize=True)
    print("wrote", path)


def normal_from_height(hm, radius_m, metres_per_unit=1.0, strength=1.0):
    """Tangent-space normal map (east = +x, north = +y) from an equirect height grid in metres. Slopes use the real
    ground distance per pixel (shrinking with cos(lat) east-west). Returns (h, w, 3) in 0..1. strength=1 is true
    relief; values above 1 exaggerate it and must be recorded as such."""
    h, w = hm.shape
    z = hm.astype(np.float32) * metres_per_unit
    lat = (0.5 - (np.arange(h) + 0.5) / h) * np.pi
    dx_m = 2 * np.pi * radius_m / w * np.maximum(np.cos(lat), 1e-3)
    dy_m = np.pi * radius_m / h
    dzdx = (np.roll(z, -1, 1) - np.roll(z, 1, 1)) / (2 * dx_m[:, None])
    up = np.concatenate([z[:1], z[:-1]], 0)
    dn = np.concatenate([z[1:], z[-1:]], 0)
    dzdy = (up - dn) / (2 * dy_m)                  # +y = north (row above)
    n = np.stack([-dzdx * strength, -dzdy * strength, np.ones_like(z)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def update_body_json(body, **fields):
    p = os.path.join(OUT, "Planets", body, "Body.json")
    src = p if os.path.exists(p) else os.path.join(RES, "Planets", body, "Body.json")
    d = json.load(open(src)) if os.path.exists(src) else {"name": body}
    os.makedirs(os.path.dirname(p), exist_ok=True)
    d.update({k: v for k, v in fields.items() if v is not None})
    with open(p, "w") as f:
        json.dump(d, f, indent=1)
