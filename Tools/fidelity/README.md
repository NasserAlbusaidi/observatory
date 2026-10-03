# Fidelity map pipeline

Rebuilds the planet, Earth, relief and sky maps in `Assets/FogOfWar/Resources` from real mission data.
Python 3.12 with numpy, Pillow, tifffile, imagecodecs and OpenEXR. No Blender.

```
python Tools/fidelity/fetch.py      # downloads sources.txt into D:\SpaceSimArtSrc\fidelity, writes SHA256SUMS
python Tools/fidelity/planets.py    # Planets/<Body>/Color.jpg, up to 8192x4096 (needs D:\SpaceSimArtSrc\maps too)
python Tools/fidelity/earth.py      # Planets/Earth/{Color,Night,Clouds,Ocean}
python Tools/fidelity/relief.py     # Planets/{Moon,Mercury,Mars,Earth}/Normal.png, Planets/Mars/Height.png
python Tools/fidelity/sky.py        # Sky/SkyOurs.png, Sky/MilkyWay.png
```

Set `FIDELITY_OUT` to write somewhere other than the project (for example while a build reads Resources).

Rules: every map comes from a named public mission product; nothing is upscaled past its source or invented.
Data gaps are filled by smooth interpolation, not texture. Each body's `Body.json` records its source.
The per-asset audit and the targets are in `space sim/FIDELITY-PLAN.md`.

Credits the maps need where credits are shown: NASA/Goddard Space Flight Center Scientific Visualization Studio
(Deep Star Maps 2020, CGI Moon Kit); Gaia DR2: ESA/Gaia/DPAC; NASA Visible Earth (Blue Marble NG, Black Marble);
USGS Astrogeology / PDS mission mosaics.
