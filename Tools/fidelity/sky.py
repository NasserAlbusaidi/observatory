"""Real sky maps from NASA SVS "Deep Star Maps 2020" (svs.gsfc.nasa.gov/4851; Hipparcos-2, Tycho-2 and Gaia DR2).

    python Tools/fidelity/sky.py

Writes Resources/Sky/SkyOurs.png (8192x4096, stars + Milky Way, for the ray tracers) and Resources/Sky/MilkyWay.png
(4096x2048, the diffuse Milky Way without the catalogue stars, drawn behind the Bright Star Catalogue points in the
mesh scenes). Replaces the art pack's modelled Milky Way.

Source frame: equatorial (ICRS) plate carree, RA 0h at the centre, RA increasing to the LEFT, north up (checked
against Sirius, Canopus, Vega, Arcturus and Rigel). Game frame (sky.py / SkyUV): RA 0h at u = 0.5, RA increasing
to the right, north up. So the map is mirrored left-right.

Pixel encoding: the ray tracers read a pixel c (linear) as radiance R = -ln(1 - c) / 1.15 and tonemap it back to c,
so c = 1 - exp(-1.15 * EXPOSURE * L) with L the SVS radiance. One exposure for both maps keeps them consistent.

The SVS files were fetched from the Internet Archive's copy (byte sizes match the originals; SVS was unreachable
on 2026-10-03). Credit: NASA/Goddard Space Flight Center Scientific Visualization Studio; Gaia DR2: ESA/Gaia/DPAC.
"""
import os

import numpy as np
import OpenEXR
from PIL import Image

import fidelity_common as C

EXPOSURE = 3.0


def read_exr(name):
    with OpenEXR.File(os.path.join(C.SRC_NEW, name)) as f:
        return np.asarray(f.channels()["RGB"].pixels, np.float32)


def encode(lin_radiance):
    c = 1.0 - np.exp(-1.15 * EXPOSURE * np.maximum(lin_radiance, 0))
    srgb = np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)
    return (np.clip(srgb, 0, 1) * 255 + 0.5).astype(np.uint8)


def main():
    out = os.path.join(C.OUT, "Sky")
    os.makedirs(out, exist_ok=True)
    a = read_exr("starmap_2020_8k.exr")[:, ::-1]          # mirror: RA increases to the right
    Image.fromarray(encode(a)).save(os.path.join(out, "SkyOurs.png"), optimize=True)
    print("wrote SkyOurs.png", a.shape)
    m = read_exr("milkyway_2020_8k.exr")[:, ::-1]
    m = m.reshape(m.shape[0] // 2, 2, m.shape[1] // 2, 2, 3).mean((1, 3))   # 4096x2048: it is diffuse light
    Image.fromarray(encode(m)).save(os.path.join(out, "MilkyWay.png"), optimize=True)
    print("wrote MilkyWay.png", m.shape)


if __name__ == "__main__":
    main()
