"""Sky maps for the ray-traced scenes, and the diffuse Milky Way for the mesh scenes.

    python Tools/fidelity/sky.py              # Sky/SkyOurs.png: Sky/MilkyWay.png + Stars/bsc5.csv, 8192x4096
    python Tools/fidelity/sky.py milkyway     # Sky/MilkyWay.png from the NASA SVS EXR (the input of the line above)
    python Tools/fidelity/sky.py svs-ours     # the old SkyOurs.png: the SVS deep star map (about 64,000 dots a frame)

SkyOurs.png is read only by Wormhole.shader and BlackHoleCore.cginc (skyTex / skyAGrad in FOWCommon.cginc). It is made
to show the sky the open-world mesh scenes show: the Milky Way layer at the SkyMap gain, plus the Bright Star Catalogue
drawn with the Stars.shader model. An unlensed patch of it comes out of the wormhole as the same display pixel as the
mesh sky in that direction, at exposure 1.

Display chain (Unity linear colour space; the 8-bit sRGB texture is decoded by the GPU with the real sRGB curve and the
framebuffer is encoded with the real sRGB curve):
  mesh Milky Way  texToDisplay(tex) * 0.3, then displayToOutput  ->  tex^(1/2.2) * 0.3, then ^2.2  ->  0.3^2.2 * tex
  mesh stars      Blend One One, linear: unitLuma(blackbody(T)) * peak(V) * exp(-4 r^2)
  wormhole        skyTex undoes the tonemap (clamp 0.985, -ln(1 - c) / 1.15) and tonemap() applies it again, so the
                  decoded texel c reaches the framebuffer unchanged
So the target is linear c = 0.3^2.2 * decode(MilkyWay.png) + sum of the stars, stored as plain sRGB8(c). (The black hole
does the same round trip through its luminance ACES curve at the default exposure; only a changed exposure, the colour
tint and the blueshift near the hole make it differ.)

Frame: equirect, top row = north, RA 0h at u = 0.5, RA to the right (Art.SphereUV / SkyUV): u = 0.5 + atan2(z, x) / 2pi,
v = 0.5 + asin(y) / pi, as in Stars/bsc5.csv (+Y celestial north, RA 6h at +Z).

Stars are points sized in screen pixels in the mesh scenes, so the map fixes their size at the reference view (55 degree
vertical FOV, 1080 px high: one pixel at the centre is 2 tan(27.5 deg) / 1080 rad). Each star keeps its integrated light
and its Gaussian profile exp(-4 r^2), r in units of the half-width of its quad; the sigma is 0.49 of a texel at the
equator, so every texel stores the profile averaged over its own area (supersampled, great-circle distance, which is
the 1/cos(lat) stretch in longitude) instead of one point sample. Stars at V >= 6.5 draw nothing, as in the mesh scenes.

The photometry numbers below are copies of FOWCommon.cginc (starPeak, starSpot, unitLuma, blackbody) and Art.cs
(StarPeak, StarSpot); the Milky Way gain is Gfx.Starfield in Util.cs. Keep them in step.

The Milky Way input is the file the mesh scenes sample, so both skies show the same pixels (the EXR route would give a
sharper layer than the mesh shows). MilkyWay.png itself comes from the SVS EXR in the "milkyway" mode.

Source frame of the SVS EXR files: equatorial (ICRS) plate carree, RA 0h at the centre, RA increasing to the LEFT, north
up (checked against Sirius, Canopus, Vega, Arcturus and Rigel). The game frame has RA increasing to the right, so the
map is mirrored left-right. Their pixels are radiance L; the Milky Way file is c = 1 - exp(-1.15 * EXPOSURE * L), the
curve the ray tracers used to invert.

The SVS files were fetched from the Internet Archive's copy (byte sizes match the originals; SVS was unreachable on
2026-10-03). Credit: NASA/Goddard Space Flight Center Scientific Visualization Studio; Gaia DR2: ESA/Gaia/DPAC (CC BY-NC
3.0 IGO, so the results are for non-commercial use); stars from the Yale Bright Star Catalogue, 5th revised edition.
"""
import csv
import math
import os
import sys

import numpy as np
from PIL import Image

import fidelity_common as C

EXPOSURE = 3.0                       # SVS radiance -> MilkyWay.png (and the old SkyOurs.png)

GAMMA = 2.2                          # texToDisplay / displayToOutput / unitLuma
MW_GAIN = 0.3                        # _Gain of the SkyMap material (Gfx.Starfield)
STAR_SLOPE, STAR_V0, STAR_P0, STAR_VLIM, STAR_WIDTH_PX = 0.7, 6.0, 0.006, 6.5, 2.2
REF_VFOV_DEG, REF_HEIGHT_PX = 55.0, 1080
PX_REF = 2 * math.tan(math.radians(REF_VFOV_DEG) / 2) / REF_HEIGHT_PX     # rad per pixel at the centre of the view
SUPERSAMPLE = 8                      # points per texel side
W, H = 8192, 4096

SKY_DIR = os.path.join(C.OUT, "Sky")
MILKYWAY = os.path.join(C.RES, "Sky", "MilkyWay.png")
CATALOGUE = os.path.join(C.RES, "Stars", "bsc5.csv")


def srgb_to_linear(s):
    s = np.asarray(s, np.float32)
    return np.where(s <= 0.04045, s / 12.92, ((s + 0.055) / 1.055) ** 2.4).astype(np.float32)


def linear_to_srgb8(c):
    c = np.clip(c, 0, 1)
    s = np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)
    return (np.clip(s, 0, 1) * 255 + 0.5).astype(np.uint8)


def blackbody(t):
    """Tanner Helland fit, display colour, as blackbody() in FOWCommon.cginc. t in kelvin, array."""
    t = np.clip(np.asarray(t, np.float64), 800, 40000) / 100
    sat = lambda x: np.clip(x, 0, 1)
    k = np.maximum(np.abs(t - 60), 1e-9)                  # np.where runs both branches: no 0 ** negative at t = 60
    r = np.where(t <= 66, 1.0, sat(1.29293618606 * k ** -0.1332047592))
    g = np.where(t <= 66, sat(0.39008157876 * np.log(t) - 0.63184144378), sat(1.12989086089 * k ** -0.0755148492))
    b = np.where(t >= 66, 1.0, np.where(t <= 19, 0.0, sat(0.54320678911 * np.log(np.maximum(t - 10, 1e-9)) - 1.19625408914)))
    return np.stack([r, g, b], -1)


def unit_luma(display):
    """Linear colour with luminance 1 from a display colour (unitLuma in FOWCommon.cginc)."""
    c = np.maximum(display, 0) ** GAMMA
    return c / np.maximum(c @ np.array([0.2126, 0.7152, 0.0722]), 1e-4)[..., None]


def star_peak(v):
    """Linear peak of a point's Gaussian at exposure 1 (starPeak)."""
    t = np.clip(STAR_VLIM - v, 0, 1)
    return STAR_P0 * 10 ** (-0.4 * STAR_SLOPE * (v - STAR_V0)) * t * t * (3 - 2 * t)


def read_catalogue(path):
    """x, y, z (Unity frame), tempK, vmag; rows that do not parse are skipped, as Art.Catalogue() does."""
    rows = []
    with open(path, newline="") as f:
        for r in csv.reader(f):
            try:
                rows.append([float(v) for v in r[:5]])
            except (ValueError, IndexError):
                continue
    a = np.array(rows, np.float64)
    d = a[:, :3] / np.linalg.norm(a[:, :3], axis=1, keepdims=True)
    return d, a[:, 3], a[:, 4]


def upsample2(a):
    """What bilinear sampling of a map gives on a grid twice as fine: wrap in longitude, clamp at the poles."""
    for axis, wrap in ((1, True), (0, False)):
        if wrap:
            prev, nxt = np.roll(a, 1, axis), np.roll(a, -1, axis)
        else:
            prev = np.concatenate([a.take([0], axis), a.take(range(a.shape[axis] - 1), axis)], axis)
            nxt = np.concatenate([a.take(range(1, a.shape[axis]), axis), a.take([-1], axis)], axis)
        out = np.empty(a.shape[:axis] + (2 * a.shape[axis],) + a.shape[axis + 1:], np.float32)
        lo, hi = [slice(None)] * a.ndim, [slice(None)] * a.ndim
        lo[axis], hi[axis] = slice(0, None, 2), slice(1, None, 2)
        out[tuple(lo)] = 0.75 * a + 0.25 * prev
        out[tuple(hi)] = 0.75 * a + 0.25 * nxt
        a = out
    return a


def milky_way_linear():
    """The Milky Way as the mesh scenes display it, in linear light, on the 8192x4096 grid."""
    mw = srgb_to_linear(np.asarray(Image.open(MILKYWAY).convert("RGB"), np.float32) / 255)
    if mw.shape[:2] != (H // 2, W // 2):
        raise SystemExit(f"{MILKYWAY} is {mw.shape[1]}x{mw.shape[0]}; expected {W // 2}x{H // 2}")
    return upsample2(mw) * np.float32(MW_GAIN ** GAMMA)


def add_star(img, n0, colour, peak):
    """Adds one star: the profile exp(-4 r^2), r = angle / (half-width of the quad), averaged over each texel."""
    shown = min(peak, 1.0)
    radius = 0.5 * STAR_WIDTH_PX * math.sqrt(max(peak, 1.0)) * PX_REF           # rad; past peak 1 the quad widens
    lat0, lon0 = math.asin(n0[1]), math.atan2(n0[2], n0[0])
    r0 = max(int(math.floor((0.5 - (lat0 + radius) / math.pi) * H)), 0)
    r1 = min(int(math.floor((0.5 - (lat0 - radius) / math.pi) * H)), H - 1)
    s = math.sin(radius) / max(math.cos(lat0), 1e-12)
    if s >= 1:                                                                   # the disc holds a pole: every longitude
        c0, c1 = 0, W - 1
    else:
        dl = math.asin(s)
        c0 = int(math.floor((lon0 - dl) / (2 * math.pi) * W + W / 2))
        c1 = int(math.floor((lon0 + dl) / (2 * math.pi) * W + W / 2))
    if c1 - c0 + 1 >= W:
        c0, c1 = 0, W - 1
    rows, cols = np.arange(r0, r1 + 1), np.arange(c0, c1 + 1)
    sub = (np.arange(SUPERSAMPLE) + 0.5) / SUPERSAMPLE
    lat = (0.5 - ((rows[:, None] + sub) / H)) * math.pi                          # (rows, S)
    lon = (((cols[:, None] + sub) / W) - 0.5) * 2 * math.pi                      # (cols, S), unwrapped: only differences matter
    lat, lon = lat.ravel(), lon.ravel()
    # haversine: stable for tiny angles, and exact on the sphere (the 1/cos(lat) stretch of the profile in longitude)
    hav = np.sin((lat - lat0) / 2)[:, None] ** 2 + (np.cos(lat) * math.cos(lat0))[:, None] * np.sin((lon - lon0) / 2)[None, :] ** 2
    r = 2 * np.arcsin(np.sqrt(np.minimum(hav, 1.0))) / radius
    g = np.where(r <= 1, np.exp(-4 * r * r), 0.0)
    g = g.reshape(len(rows), SUPERSAMPLE, len(cols), SUPERSAMPLE).mean((1, 3))
    img[np.ix_(rows, cols % W)] += (g[..., None] * (shown * colour)).astype(np.float32)


def build_ours():
    img = milky_way_linear()
    dirs, temp, vmag = read_catalogue(CATALOGUE)
    peak = star_peak(vmag)
    colour = unit_luma(blackbody(temp))
    drawn = 0
    for i in np.flatnonzero(peak > 0):
        add_star(img, dirs[i], colour[i], float(peak[i]))
        drawn += 1
    print(f"{drawn} of {len(vmag)} catalogue stars drawn (V < {STAR_VLIM})")
    return img


def save(u8, name):
    os.makedirs(SKY_DIR, exist_ok=True)
    Image.fromarray(u8).save(os.path.join(SKY_DIR, name), optimize=True)
    print("wrote", name, u8.shape)


def read_exr(name):
    import OpenEXR
    with OpenEXR.File(os.path.join(C.SRC_NEW, name)) as f:
        return np.asarray(f.channels()["RGB"].pixels, np.float32)


def encode_svs(lin_radiance):
    return linear_to_srgb8(1.0 - np.exp(-1.15 * EXPOSURE * np.maximum(lin_radiance, 0)))


def main(mode="ours"):
    if mode == "ours":
        img = build_ours()
        save(np.concatenate([linear_to_srgb8(img[r:r + 512]) for r in range(0, H, 512)]), "SkyOurs.png")
    elif mode == "milkyway":
        m = read_exr("milkyway_2020_8k.exr")[:, ::-1]                    # mirror: RA increases to the right
        m = m.reshape(m.shape[0] // 2, 2, m.shape[1] // 2, 2, 3).mean((1, 3))   # 4096x2048: it is diffuse light
        save(encode_svs(m), "MilkyWay.png")
    elif mode == "svs-ours":
        save(encode_svs(read_exr("starmap_2020_8k.exr")[:, ::-1]), "SkyOurs.png")
    else:
        raise SystemExit(__doc__)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "ours")
