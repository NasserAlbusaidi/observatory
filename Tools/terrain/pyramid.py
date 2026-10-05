"""Tiled height pyramids for the streaming terrain, cut from real DEMs (public domain, NASA / USGS / PDS).

    py -3 Tools/terrain/pyramid.py [Moon Mercury Mars Venus]

Output (outside git, see TERRAIN_OUT): <out>/<Body>/terrain.json, L<n>.bin, L<n>.mm.
- Equirect, top row = north, longitude 0 at u = 0.5, east to the right (the game's map convention). The source is
  only column-rolled to get there: level 0 holds the product's own samples, unresampled, in its own int16 units
  (metres = raw * scale). Holes (nodata) are filled smoothly and counted in terrain.json.
- Level n is level 0 box-averaged 2^n times, down to <= 512 samples wide.
- L<n>.bin: 256 x 256 int16 tiles, row-major by tile (ty, tx); partial edge tiles repeat the last row / column.
- L<n>.mm: per tile (min, max) int16, same order, so the game can bound a chunk without loading its tile.
Heights are relative to a sphere of refRadiusKm (each product's own reference surface).
"""
import json
import os
import re
import sys

import numpy as np
import tifffile

T = 256
OUT = os.environ.get("TERRAIN_OUT", r"D:\SpaceSimArtSrc\terrain")
SRC_MAPS = r"D:\SpaceSimArtSrc\maps"
SRC_NEW = r"D:\SpaceSimArtSrc\fidelity"


def read_isis_tiled(path):
    """ISIS3 cube band 1 as (lines, samples) int16, untiling 'Format = Tile' cubes. Returns raw, multiplier, base."""
    head = open(path, "rb").read(65536).decode("latin1")
    g = lambda k: re.search(r"\b" + k + r"\s*=\s*([^\r\n]+)", head).group(1).strip()
    ns, nl = int(g("Samples")), int(g("Lines"))
    start = int(g("StartByte")) - 1
    assert g("Type") == "SignedWord" and g("ByteOrder") == "Lsb"
    mult, base = float(g("Multiplier")), float(g("Base"))
    assert base == 0.0
    if g("Format") == "Tile":
        ts, tl = int(g("TileSamples")), int(g("TileLines"))
        mm = np.memmap(path, np.int16, "r", start, (nl // tl, ns // ts, tl, ts))
        raw = np.ascontiguousarray(mm.transpose(0, 2, 1, 3)).reshape(nl, ns)
    else:
        raw = np.array(np.memmap(path, np.int16, "r", start, (nl, ns)))
    return raw, mult


def read_tif(path):
    a = tifffile.imread(path)
    if a.ndim == 3:
        a = a[..., 0] if a.shape[2] <= 4 else a[0]
    return np.ascontiguousarray(a)


def roll_to_centre(a, left_east_lon):
    """Shift columns so longitude 0 sits at u = 0.5 (left edge at -180 east). A pure column shift."""
    shift = int(round((left_east_lon + 180) / 360 * a.shape[1]))
    return np.roll(a, shift, axis=1)


def fill_holes(a, hole):
    """Pull-push fill of nodata samples (smooth, no added texture). a: int16 in place. Returns the count filled."""
    n = int(hole.sum())
    if n == 0:
        return 0
    f = a.astype(np.float32)
    w0 = (~hole).astype(np.float32)
    levels = [(f * w0, w0)]
    while levels[-1][1].min() == 0 and min(levels[-1][1].shape) > 2:
        num, den = levels[-1]
        hh, ww = den.shape
        h2, w2 = hh // 2 * 2, ww // 2 * 2
        levels.append((num[:h2, :w2].reshape(h2 // 2, 2, w2 // 2, 2).sum((1, 3)),
                       den[:h2, :w2].reshape(h2 // 2, 2, w2 // 2, 2).sum((1, 3))))
    num, den = levels[-1]
    est = num / np.maximum(den, 1e-6)
    for num, den in reversed(levels[:-1]):
        hh, ww = den.shape
        up = np.repeat(np.repeat(est, 2, 0), 2, 1)
        up = np.pad(up, ((0, hh - up.shape[0]), (0, ww - up.shape[1])), mode="edge")
        est = np.where(den > 0, num / np.maximum(den, 1e-6), up)
    a[hole] = np.round(est[hole]).astype(np.int16)
    return n


def down2(a):
    """2x2 box average (int16 in, int16 out); odd edges repeat the last row / column."""
    h, w = a.shape
    if h % 2 or w % 2:
        a = np.pad(a, ((0, h % 2), (0, w % 2)), mode="edge")
    f = a.astype(np.float32)
    out = np.empty((a.shape[0] // 2, a.shape[1] // 2), np.int16)
    step = 2048
    for r in range(0, out.shape[0], step):
        blk = f[2 * r: 2 * (r + step)]
        out[r: r + step] = np.round(blk.reshape(blk.shape[0] // 2, 2, -1, 2).mean((1, 3)))
    return out


def write_level(d, n, a):
    h, w = a.shape
    tx, ty = -(-w // T), -(-h // T)
    pad = np.pad(a, ((0, ty * T - h), (0, tx * T - w)), mode="edge")
    mm = np.empty((ty, tx, 2), np.int16)
    with open(os.path.join(d, "L%d.bin" % n), "wb") as f:
        for j in range(ty):
            band = pad[j * T:(j + 1) * T].reshape(T, tx, T).transpose(1, 0, 2)  # (tx, T, T)
            mm[j, :, 0] = band.min((1, 2))
            mm[j, :, 1] = band.max((1, 2))
            f.write(np.ascontiguousarray(band).astype("<i2").tobytes())
    mm.astype("<i2").tofile(os.path.join(d, "L%d.mm" % n))
    return {"w": w, "h": h, "tilesX": tx, "tilesY": ty}


def build(body, raw, scale, left_east_lon, nodata, ref_km, product, source_file, res_m, datum="sphere"):
    d = os.path.join(OUT, body)
    os.makedirs(d, exist_ok=True)
    raw = roll_to_centre(raw, left_east_lon)
    filled = fill_holes(raw, raw == nodata) if nodata is not None else 0
    print("%s: %dx%d, %d holes filled, %.0f .. %.0f m" % (body, raw.shape[1], raw.shape[0], filled,
                                                        raw.min() * scale, raw.max() * scale))
    levels = []
    a, n = raw, 0
    while True:
        levels.append(write_level(d, n, a))
        print("  L%d %dx%d" % (n, a.shape[1], a.shape[0]))
        if a.shape[1] <= 512:
            break
        a, n = down2(a), n + 1
    meta = {
        "body": body, "product": product, "sourceFile": source_file, "nativeResolutionM": res_m,
        "licence": "public domain (NASA / USGS / PDS)",
        "convention": "equirect, top row north, lon 0 at u=0.5, east right; level 0 = source samples, column-rolled only",
        "units": "metres = raw * scale, relative to a sphere of refRadiusKm", "scale": scale,
        "refRadiusKm": ref_km, "datum": datum, "tile": T, "holesFilled": filled, "levels": levels,
    }
    with open(os.path.join(d, "terrain.json"), "w") as f:
        json.dump(meta, f, indent=1)


def moon():
    raw, mult = read_isis_tiled(os.path.join(SRC_NEW, "Lunar_LRO_LOLA_DEM_Global_128ppd_v04.cub"))
    build("Moon", raw, mult, 0.0, -32768, 1737.4, "LRO LOLA DEM 128 ppd v04 (PDS / USGS Astrogeology)",
          "Lunar_LRO_LOLA_DEM_Global_128ppd_v04.cub", 236.9)


def mercury():
    raw = read_tif(os.path.join(SRC_MAPS, "Mercury_Messenger_USGS_DEM_Global_665m_v2.tif"))
    build("Mercury", raw, 0.5, -180.0, -32768, 2439.4, "MESSENGER MDIS USGS global DEM 665 m v2 (USGS Astrogeology)",
          "Mercury_Messenger_USGS_DEM_Global_665m_v2.tif", 665.2)


def mars():
    raw = read_tif(os.path.join(SRC_NEW, "Mars_MGS_MOLA_DEM_mosaic_global_463m.tif"))
    # heights are above the MOLA areoid, which departs from this sphere by up to ~2 km
    build("Mars", raw, 1.0, -180.0, -32768, 3396.19, "MGS MOLA DEM mosaic global 463 m (USGS Astrogeology / PDS)",
          "Mars_MGS_MOLA_DEM_mosaic_global_463m.tif", 463.1, datum="areoid")


def venus():
    raw = read_tif(os.path.join(SRC_MAPS, "Venus_Magellan_Topography_Global_4641m_v02.tif"))
    build("Venus", raw, 1.0, -180.0, -32768, 6051.8, "Magellan global topography 4641 m v02 (USGS Astrogeology / PDS)",
          "Venus_Magellan_Topography_Global_4641m_v02.tif", 4641.1)


if __name__ == "__main__":
    jobs = {"Moon": moon, "Mercury": mercury, "Mars": mars, "Venus": venus}
    for b in (sys.argv[1:] or list(jobs)):
        jobs[b]()
