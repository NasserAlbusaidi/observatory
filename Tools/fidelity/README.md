# Fidelity map pipeline

Rebuilds the planet, Earth, relief and sky maps in `Assets/FogOfWar/Resources` from real mission data.
Python 3.12 with numpy, Pillow, tifffile, imagecodecs and OpenEXR. No Blender.

```
python Tools/fidelity/fetch.py      # downloads sources.txt into D:\SpaceSimArtSrc\fidelity, writes SHA256SUMS
python Tools/fidelity/planets.py    # Planets/<Body>/Color.jpg, up to 8192x4096 (needs D:\SpaceSimArtSrc\maps too)
python Tools/fidelity/earth.py      # Planets/Earth/{Color,Night,Clouds,Ocean}
python Tools/fidelity/relief.py     # Planets/{Moon,Mercury,Mars,Earth}/Normal.png, Planets/Mars/Height.png
python Tools/fidelity/sky.py        # Sky/SkyOurs.png (from Sky/MilkyWay.png and Stars/bsc5.csv; needs no EXR)
python Tools/fidelity/sky.py milkyway   # Sky/MilkyWay.png from the SVS EXR (the input of the line above)
python Tools/fidelity/sky.py svs-ours   # the old SkyOurs.png: the SVS deep star map, about 64,000 dots a frame
```

Set `FIDELITY_OUT` to write somewhere other than the project (for example while a build reads Resources).

Rules: every map comes from a named public mission product; nothing is upscaled past its source or invented.
Data gaps are filled by smooth interpolation, not texture. Each body's `Body.json` records its source.
The per-asset audit and the targets are in `space sim/FIDELITY-PLAN.md`.

Credits the maps need where credits are shown: NASA/Goddard Space Flight Center Scientific Visualization Studio
(Deep Star Maps 2020, CGI Moon Kit); Gaia DR2: ESA/Gaia/DPAC; Yale Bright Star Catalogue, 5th revised edition
(Hoffleit and Warren); NASA Visible Earth (Blue Marble NG, Black Marble); USGS Astrogeology / PDS mission mosaics.

## Sky

`Sky/SkyOurs.png` (8192x4096 equirect) is read only by the wormhole and black-hole ray tracers. `sky.py` draws it so
that it shows the same sky as the open-world mesh scenes: the Milky Way layer at the mesh gain, plus the Bright Star
Catalogue (about 8,350 stars with V < 6.5) in the mesh star model. About the naked-eye sky, not the deep star map.

Inputs: `Sky/MilkyWay.png` (the file the mesh scenes sample; it is made from the SVS EXR by `sky.py milkyway`) and
`Stars/bsc5.csv` (x, y, z, tempK, vmag; Unity frame). Both are in the repository. The default mode does not read the
EXR, so it needs no download. It writes only `SkyOurs.png`, and it uses no random numbers (two runs gave the same
SHA-256 here).

Encoding. The ray tracers decode the texel c, undo their tonemap and apply it again, so c reaches the framebuffer
unchanged; the mesh shaders add the Milky Way as `0.3^2.2 * texel` and each star in linear light (Blend One One).
So the PNG stores plain sRGB8 of the linear sum `0.3^2.2 * decode(MilkyWay.png) + stars`. There is no
`1 - exp(-1.15 L)` step any more (the SVS modes keep it). An unlensed patch of the map then shows the same display
pixel as the mesh sky in that direction at exposure 1 (`_FOWStarGlare` = 0). The black hole does the same round trip
through its luminance ACES curve at the default exposure; a changed exposure, its colour tint and the blueshift near
the hole make it differ.

Stars. Each star keeps the integrated light of its mesh point: peak `0.006 * 10^(-0.28 (V - 6))` with a smoothstep
fade to 0 at V 6.5, profile `exp(-4 r^2)` out to r = 1, quad width 2.2 px, unit-luminance blackbody colour. The mesh
sizes it in screen pixels, so the map fixes the size at the reference view (55 degree vertical FOV, 1080 px: one pixel
at the centre is 2 tan(27.5 deg) / 1080 = 9.64e-4 rad; one texel is 7.67e-4 rad). The profile has sigma 0.49 texel at
the equator, so each texel stores the profile averaged over its own area (8x8 supersampling, great-circle distance:
the 1/cos(lat) stretch in longitude, exact at the poles and over the RA 12h seam). Seen through the map, a star is
wider and its peak pixel is lower than the mesh star's (about 0.55 of it at 1080p in a numpy model of skyTex's
trilinear sampling, not measured in a build); its integrated light is the same.
A star at the edge of the frame covers more pixels than at the centre (the map is in angle, the mesh star in pixels).

Numbers shared with other files (keep them in step): `FOWCommon.cginc` (`starPeak`, `starSpot`, `unitLuma`,
`blackbody`, the 2.2 gamma of `texToDisplay`/`displayToOutput`), `Art.cs` (`StarPeak`, `StarSpot`) and the Milky Way
`_Gain` 0.3 in `Gfx.Starfield` (`Util.cs`). If one of them changes, run `sky.py` again.

The Milky Way is Gaia DR2 data through the SVS Deep Star Maps 2020 (ESA CC BY-NC 3.0 IGO), so `SkyOurs.png` and
`MilkyWay.png` are for non-commercial use only, as `docs/DEVELOPING.md` says. The star positions and magnitudes are
from the Yale Bright Star Catalogue.
