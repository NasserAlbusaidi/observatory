"""Earth's colour, night, cloud and ocean maps at 8192x4096 (relief normals are in relief.py).

    python Tools/fidelity/earth.py

Sources (NASA Visible Earth, public domain), all equirect with 180 W at the left edge and north up, which is
already the shared convention (longitude 0 at u = 0.5):
    Color   Blue Marble Next Generation, August 2004, 21600x10800 (world.200408, plain ocean)
    Night   Black Marble 2016 colour, 3 km, 13500x6750
    Clouds  cloud_combined_8192 (MODIS composite), 8192x4096
    Ocean   water mask: sea level in Visible Earth GEBCO_08/SRTM elevation, minus bright pixels (sea ice), plus dark
            blue lakes from the BMNG colour
"""
import os

import numpy as np
from PIL import Image

import fidelity_common as C

W, H = 8192, 4096
OUT = os.path.join(C.OUT, "Planets", "Earth")


def src(name):
    return os.path.join(C.SRC_NEW, name)


def main():
    os.makedirs(OUT, exist_ok=True)
    day = Image.open(src("world.200408.3x21600x10800.png")).convert("RGB")
    day.resize((W, H), Image.LANCZOS).save(os.path.join(OUT, "Color.jpg"), quality=95, subsampling=0, optimize=True)
    print("wrote Color.jpg")

    Image.open(src("BlackMarble_2016_3km.jpg")).convert("RGB").resize((W, H), Image.LANCZOS) \
        .save(os.path.join(OUT, "Night.jpg"), quality=95, subsampling=0, optimize=True)
    print("wrote Night.jpg")

    clouds = C.open_array(src("cloud_combined_8192.tif"))
    c = np.asarray(clouds[..., 0], np.uint8)
    if c.shape != (H, W):
        c = np.asarray(Image.fromarray(c).resize((W, H), Image.LANCZOS))
    Image.fromarray(c).save(os.path.join(OUT, "Clouds.png"), optimize=True)
    print("wrote Clouds.png", c.shape, c.mean())

    # water: sea level in the GEBCO/SRTM elevation (oceans are 0 there) and not bright (sea ice, salt flats and
    # low land stay dry), plus very dark blue pixels at any height (lakes sit at their surface elevation)
    d = np.asarray(day, np.int16)
    e = np.asarray(Image.open(src("gebco_08_rev_elev_21600x10800.png")).convert("L"))
    r, g, b = d[..., 0], d[..., 1], d[..., 2]
    lum = (r + g + b) / 3
    water = ((e == 0) & (lum < 80) & (b > r)) | ((lum < 14) & (b > r) & (b >= g))
    del d, e, r, g, b, lum
    m = Image.fromarray(water.astype(np.uint8) * 255).resize((W, H), Image.BOX)
    m.save(os.path.join(OUT, "Ocean.png"), optimize=True)
    a = np.asarray(m, np.float32) / 255
    wgt = np.cos((0.5 - (np.arange(H) + 0.5) / H) * np.pi)
    print("wrote Ocean.png, area-weighted water fraction %.3f" % ((a.mean(1) * wgt).sum() / wgt.sum()))

    C.update_body_json("Earth",
                       colourSource="NASA Blue Marble NG Aug 2004 21600x10800 (Color); Black Marble 2016 3 km (Night); "
                                    "Visible Earth cloud_combined_8192 MODIS composite (Clouds); water mask from "
                                    "GEBCO sea level + BMNG colour (Ocean); GEBCO_08/SRTM elevation (Normal)",
                       licence="public domain (NASA Visible Earth)", mapSize=f"{W}x{H}")


if __name__ == "__main__":
    main()
