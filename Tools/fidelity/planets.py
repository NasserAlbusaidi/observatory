"""Global colour maps for the rocky planets and moons at up to 8K, from USGS Astrogeology / PDS mosaics.

    python Tools/fidelity/planets.py [Body ...]

Same processing as the art pack's Blender/maps.py (roll to the shared convention, luminance stretch + published
tint for greyscale sources), but streamed with numpy/tifffile so 20K+ sources fit in memory, with an 8192 cap
instead of 4096, and with no procedural grain in data gaps. Writes Resources/Planets/<Body>/Color.jpg and updates
Body.json. All sources are NASA / USGS / PDS public domain.
"""
import math
import os
import sys

import numpy as np

import fidelity_common as C

CAP = 8192

# body: (file under D:\SpaceSimArtSrc\maps or absolute, east longitude of the left edge, tint (None: colour source
# used as is; "keep": calibrated colour albedo, scaled in linear light without a stretch), note)
MAPS = {
    "Venus": ("Venus_Magellan_C3-MDIR_Global_Mosaic_2025m.tif", -180, (1.0, 0.72, 0.42),
              "Magellan C3-MDIR radar mosaic (USGS). Radar brightness, tinted; Venus's visible face is Clouds.jpg"),
    # CGI Moon Kit colour (svs.gsfc.nasa.gov/4720): LRO WAC Hapke-normalised colour, no gaps and no baked shadows,
    # so the LOLA normals do the shading. Already in the shared convention. Fetched from the Internet Archive copy
    # (SVS unreachable on 2026-10-03; byte size matches the original). Replaces Kaguya TC, which has far-side gaps.
    "Moon": (os.path.join(C.SRC_NEW, "lroc_color_poles_16k.tif"), -180, "keep",
             "LRO WAC Hapke-normalised colour, NASA SVS CGI Moon Kit (lroc_color_poles_16k)"),
    "Io": ("Io_GalileoSSI-Voyager_Global_Mosaic_ClrMerge_1km.tif", -180, None,
           "Galileo SSI / Voyager colour merge, 1 km (USGS)"),
    "Europa": ("Europa_Voyager_GalileoSSI_global_mosaic_500m.tif", 0, (0.92, 0.86, 0.76),
               "Voyager / Galileo SSI mosaic, 500 m (USGS)"),
    "Ganymede": ("Ganymede_Voyager_GalileoSSI_Global_ClrMosaic_1435m.tif", 0, None,
                 "Voyager / Galileo SSI colour mosaic, 1.4 km (USGS)"),
    "Callisto": ("Callisto_Voyager_GalileoSSI_global_mosaic_1km.tif", 0, (0.66, 0.60, 0.53),
                 "Voyager / Galileo SSI mosaic, 1 km (USGS)"),
    "Mimas": ("Cassini_DLR/MI_170630_DLR_basemap_degrees.tif", -180, (0.93, 0.92, 0.90),
              "Cassini ISS DLR basemap (USGS archive)"),
    "Enceladus": ("Enceladus_Cassini_mosaic_global_110m.tif", -180, (0.97, 0.98, 1.0),
                  "Cassini ISS mosaic, 110 m (USGS)"),
    "Tethys": ("Tethys_Cassini_mosaic_global_293m.tif", -180, (0.94, 0.94, 0.92),
               "Cassini ISS mosaic, 293 m (USGS)"),
    "Dione": ("Dione_Cassini_Voyager_mosaic_global_154m.tif", -180, (0.92, 0.91, 0.89),
              "Cassini / Voyager mosaic, 154 m (USGS)"),
    "Rhea": ("Rhea_Cassini_Voyager_mosaic_global_417m.tif", -180, (0.90, 0.88, 0.85),
             "Cassini / Voyager mosaic, 417 m (USGS)"),
    "Iapetus": ("Iapetus_Cassini_Voyager_mosaic_global_783m.tif", -180, (1.0, 0.86, 0.70),
                "Cassini / Voyager mosaic, 783 m (USGS). Tint warms the dark Cassini Regio toward its reddish-brown"),
    "Titan": ("Titan_ISS_P19658_Mosaic_Global_4km.tif", 0, (0.95, 0.80, 0.55),
              "Cassini ISS 938 nm surface mosaic, 4 km (USGS). Seen in visible light Titan is an orange haze ball: draw the haze over this"),
    "Triton": ("Triton_Voyager2_ClrMosaic_GlobalFill_600m.tif", -180, (1.0, 0.88, 0.82),
               "Voyager 2 colour mosaic with gap fill, 600 m (USGS). The unimaged north is filled, not mapped"),
    "Ceres": ("Ceres_Dawn_FC_DLR_global_20ppd_Oct2015.tif", 0, (0.95, 0.95, 0.97),
              "Dawn FC DLR mosaic, 20 ppd (USGS)"),
    "Mercury": ("Mercury_MESSENGER_ClrMosaic_global_665m_v3.tif", -180, (1.0, 0.93, 0.85),
                "MESSENGER MDIS colour mosaic, 665 m (USGS). Enhanced-colour source reduced to luminance and given Mercury's grey-brown tint"),
    "Vesta": ("Vesta_Dawn_FC_HAMO_Mosaic_Global_74ppd.tif", -180, (0.97, 0.93, 0.88),
              "Dawn FC HAMO mosaic, 74 ppd (USGS)"),
    "Mars": (os.path.join(C.SRC_NEW, "Mars_Viking_ClrMosaic_global_925m.tif"), -180, None,
             "Viking MDIM 2.1 colour mosaic, 925 m (USGS Astrogeology); Height and Normal = MGS MOLA DEM 463 m"),
}


LICENCE = {"Moon": "NASA SVS (CGI Moon Kit, LRO WAC; free use with credit to NASA's Scientific Visualization Studio)"}

# Low-frequency colour reference (file in SRC_NEW, description). Mars: the NASA 3D Resources map
# the game used before (1440x720 source, Hubble-like butterscotch), already in the shared convention.
CHROMA = {"Mars": ("mars_chroma_ref_2048.jpg", "NASA 3D Resources Mars colour map")}


def build(body):
    fname, left, tint, note = MAPS[body]
    path = fname if os.path.isabs(fname) else os.path.join(C.SRC_MAPS, fname)
    if not os.path.exists(path):
        print("missing", path)
        return
    a = C.open_array(path)
    H, W, ch = a.shape
    w = C.pow2_width(W, CAP)
    is16 = a.dtype == np.int16
    m = C.load_global(path, w, channels=[0, 1, 2] if ch >= 3 else None)
    m = m / (32767.0 if is16 else 255.0)
    m = C.roll_to_centre(m, left)
    if m.shape[-1] == 1:
        m = np.repeat(m, 3, -1)
    if body == "Venus":
        dark = m.mean(-1) < 4 / 255           # Magellan's gores: dark strips inside valid data
        for _ in range(2):
            dark = dark | np.roll(dark, 1, 0) | np.roll(dark, -1, 0) | np.roll(dark, 1, 1) | np.roll(dark, -1, 1)
        m = C.fill_holes(m, dark)
    if tint is not None:
        lw = np.array([0.2126, 0.7152, 0.0722], np.float32)
        if tint == "keep":
            # calibrated albedo: scale in linear light so the median sits at 0.45 display, keeping the real contrast
            # between maria and highlands (a percentile stretch would push the maria to black)
            lin = np.clip(m, 0, 1) ** 2.2
            lin *= 0.45 ** 2.2 / float(np.median(lin @ lw))
            C.save_jpg(np.clip(lin, 0, 1) ** (1 / 2.2), os.path.join(C.OUT, "Planets", body, "Color.jpg"))
            C.update_body_json(body, colourSource=note, sourceFile=os.path.basename(path), sourceSize=f"{W}x{H}",
                               mapSize=f"{w}x{w // 2}", licence=LICENCE.get(body),
                               convention="equirect, lon 0 at u=0.5, east right, north up",
                               colourNote="calibrated albedo scaled so the median is 0.45 display; use geometricAlbedo for absolute brightness")
            return
        g = m @ lw
        lo, hi = np.percentile(g, 0.5), np.percentile(g, 99.8)
        g = np.clip((g - lo) / max(hi - lo, 1e-4), 0, 1)
        med = float(np.median(g))
        if 0.02 < med < 0.98:
            g = g ** float(np.clip(math.log(0.45) / math.log(med), 0.45, 1.5))
        if body == "Iapetus":
            dark = np.clip((0.55 - g) / 0.35, 0, 1)[..., None]
            m = g[..., None] * (dark * np.array(tint, np.float32) + (1 - dark) * np.array((0.96, 0.95, 0.93), np.float32))
        else:
            m = g[..., None] * np.array(tint, np.float32)
    if body in CHROMA:
        # Viking colour runs too red and its dark areas too blue. Keep Viking's brightness detail and take the
        # low-frequency colour (RGB / luminance) from a reference map: standard pan-sharpening, both real data.
        from PIL import Image, ImageFilter
        ref = Image.open(os.path.join(C.SRC_NEW, CHROMA[body][0])).convert("RGB").filter(ImageFilter.GaussianBlur(2))
        ref = np.asarray(ref.resize((m.shape[1], m.shape[0]), Image.BICUBIC), np.float32) / 255
        lw = np.array([0.2126, 0.7152, 0.0722], np.float32)
        lin_ref = ref ** 2.2
        chroma = lin_ref / np.maximum(lin_ref @ lw, 1e-3)[..., None]
        lum = (np.clip(m, 0, 1) ** 2.2) @ lw
        lum *= float((lin_ref @ lw).mean() / lum.mean())   # the map is absolute: keep the reference's mean level
        m = np.clip(lum[..., None] * chroma, 0, 1) ** (1 / 2.2)
        note += "; colour (not brightness) from " + CHROMA[body][1]
    C.save_jpg(m, os.path.join(C.OUT, "Planets", body, "Color.jpg"))
    C.update_body_json(body, colourSource=note, tint=tint if tint != "keep" else None, sourceFile=os.path.basename(path),
                       sourceSize=f"{W}x{H}", mapSize=f"{w}x{w // 2}",
                       licence=LICENCE.get(body, "public domain (NASA / USGS Astrogeology / PDS)"),
                       convention="equirect, lon 0 at u=0.5, east right, north up",
                       colourNote="map brightness is relative (contrast-stretched); use geometricAlbedo for absolute brightness")


if __name__ == "__main__":
    for body in (sys.argv[1:] or list(MAPS)):
        build(body)
