"""Relief from real DEMs: tangent-space normal maps (8192x4096) for the Moon, Mercury, Mars and Earth, and the
16-bit Mars height map that scene 7 displaces its globe with.

    python Tools/fidelity/relief.py [Moon Mercury Mars Earth]

Normals use true relief (no exaggeration): slopes are metres of height over metres of ground per pixel at the
map's own resolution. Sources (NASA / USGS / PDS, public domain):
    Moon     LRO LOLA DEM 128 ppd v04 (ISIS cube, 0.5 m units, left edge 0 E)
    Mercury  MESSENGER USGS DEM 665 m v2 (left edge 180 W per its GeoTIFF tiepoint)
    Mars     MGS MOLA DEM mosaic 463 m (left edge 180 W)
    Earth    NASA Visible Earth GEBCO_08 / SRTM land elevation, 8-bit, 0 = sea level .. 255 = 6400 m (land only)
"""
import os
import re
import sys

import numpy as np
from PIL import Image

import fidelity_common as C

W = 8192


def read_isis_tiled(path):
    """Full ISIS3 cube band 1 as a (lines, samples) array, untiling 'Format = Tile' cubes. Applies Base/Multiplier."""
    head = open(path, "rb").read(65536).decode("latin1")
    g = lambda k: re.search(r"\b" + k + r"\s*=\s*([^\r\n]+)", head).group(1).strip()
    ns, nl = int(g("Samples")), int(g("Lines"))
    start = int(g("StartByte")) - 1
    assert g("Type") == "SignedWord" and g("ByteOrder") == "Lsb"
    mult, base = float(g("Multiplier")), float(g("Base"))
    if g("Format") == "Tile":
        ts, tl = int(g("TileSamples")), int(g("TileLines"))
        mm = np.memmap(path, np.int16, "r", start, (nl // tl, ns // ts, tl, ts))
        raw = np.ascontiguousarray(mm.transpose(0, 2, 1, 3)).reshape(nl, ns)
    else:
        raw = np.array(np.memmap(path, np.int16, "r", start, (nl, ns)))
    return raw, mult, base


def downsample_dem(raw, w, mult=1.0, base=0.0, nodata=-32000):
    """Integer box average to >= w wide, holes filled, Lanczos to w x w/2. Returns metres, float32."""
    H, Wd = raw.shape
    k = max(1, Wd // w)
    h2, w2 = H // k, Wd // k
    out = np.zeros((h2, w2), np.float32)
    cov = np.zeros((h2, w2), np.float32)
    step = max(1, 2048 // k)
    for r0 in range(0, h2, step):
        r1 = min(h2, r0 + step)
        blk = raw[r0 * k: r1 * k, : w2 * k]
        v = blk > nodata
        f = np.where(v, blk.astype(np.float32) * mult + base, 0)
        num = f.reshape(r1 - r0, k, w2, k).sum((1, 3))
        den = v.reshape(r1 - r0, k, w2, k).sum((1, 3))
        out[r0:r1] = num / np.maximum(den, 1)
        cov[r0:r1] = den / (k * k)
    out = C.fill_holes(out[..., None], cov < 0.5)
    return C.resize(out, w, w // 2)[..., 0]


def write_normal(body, metres, radius_m, note):
    n = C.normal_from_height(metres, radius_m)
    path = os.path.join(C.OUT, "Planets", body, "Normal.png")
    C.save_png(n, path)
    C.update_body_json(body, normal="Normal.png", normalSource=note + "; true relief, no exaggeration")


def moon():
    raw, mult, base = read_isis_tiled(os.path.join(C.SRC_NEW, "Lunar_LRO_LOLA_DEM_Global_128ppd_v04.cub"))
    m = downsample_dem(raw, W, mult, base)
    del raw
    m = C.roll_to_centre(m[..., None], 0)[..., 0]
    print("Moon relief m: %.0f .. %.0f" % (m.min(), m.max()))
    write_normal("Moon", m, 1737400.0, "LRO LOLA DEM 128 ppd v04 (USGS / PDS)")


def mercury():
    raw = C.open_array(os.path.join(C.SRC_MAPS, "Mercury_Messenger_USGS_DEM_Global_665m_v2.tif"))[..., 0]
    m = downsample_dem(raw, W)
    m = C.roll_to_centre(m[..., None], -180)[..., 0]
    print("Mercury relief m: %.0f .. %.0f" % (m.min(), m.max()))
    write_normal("Mercury", m, 2439400.0, "MESSENGER USGS global DEM 665 m v2 (USGS / PDS)")


def mars():
    raw = C.open_array(os.path.join(C.SRC_NEW, "Mars_MGS_MOLA_DEM_mosaic_global_463m.tif"))[..., 0]
    m = downsample_dem(raw, W)
    m = C.roll_to_centre(m[..., None], -180)[..., 0]
    print("Mars relief m: %.0f .. %.0f" % (m.min(), m.max()))
    write_normal("Mars", m, 3396190.0, "MGS MOLA DEM mosaic 463 m (USGS / PDS)")
    # scene 7 height: same encoding as before (km = -8.2 + grey * 29.4), 4096 wide (vertex displacement on an
    # ico-7 globe gains nothing from more), 16-bit
    hk = C.resize(m[..., None], 4096, 2048)[..., 0] / 1000.0
    g = np.clip((hk + 8.2) / 29.4, 0, 1)
    C.save_png(g, os.path.join(C.OUT, "Planets", "Mars", "Height.png"), bits=16)
    C.update_body_json("Mars", colourSource="Viking MDIM 2.1 colour mosaic, 925 m (USGS Astrogeology); "
                       "Height and Normal = MGS MOLA DEM 463 m", height="Height.png")


def earth():
    a = np.asarray(Image.open(os.path.join(C.SRC_NEW, "gebco_08_rev_elev_21600x10800.png")).convert("L"), np.float32)
    m = C.resize((a * (6400.0 / 255.0))[..., None], W, W // 2)[..., 0]
    write_normal("Earth", m, 6371000.0, "NASA Visible Earth GEBCO_08 / SRTM land elevation (8-bit, 25 m steps; oceans flat)")


if __name__ == "__main__":
    jobs = {"Moon": moon, "Mercury": mercury, "Mars": mars, "Earth": earth}
    for b in (sys.argv[1:] or list(jobs)):
        jobs[b]()
