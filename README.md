# Fog of War Observatory (Unity, Windows)

A Unity space simulator for the physics topics argued about in the Learning project. It has 8 scenes, all running on the real equations:

| Key | Scene | What's simulated |
|---|---|---|
| 1 | Wormhole | Morris–Thorne/Ellis metric, ray-traced per pixel. Fly-through to another universe, Einstein–Rosen pinch-off, exotic-matter bill compared with the Casimir effect |
| 2 | Black Hole | Schwarzschild geodesics, photon ring, Doppler-beamed accretion disk, time dilation and spaghettification readouts |
| 3 | Warp Bubble | Alcubierre York-time surface (space contracts ahead and expands behind), works past 1 c |
| 4 | 1g to Anywhere | Constant-1g trip: 220,000 ly in ~24 ship-years. Relativistic aberration, Doppler colour, CMB blue-shifted into view |
| 5 | Negative Mass | Bondi runaway pair, N-body with signed mass |
| 6 | Moon 1,000 km Closer | True-scale Earth–Moon, 1/d³ tides, Kepler month, Roche-limit breakup into a ring |
| 7 | Terraform Mars | Energy in vs out, greenhouse optical depth, lapse rate × Olympus Mons |
| 8 | Radio Meteors | Forward scatter from Muscat to a transmitter beyond the horizon, with an SDR waterfall |

## Setup (about 5 minutes)

1. **Unity Hub → New project.** Pick **Unity 6** and the **3D (Built-In Render Pipeline)** template. **Universal 3D** works too; every shader here is custom and pipeline-independent.
2. **Copy** the `Assets/FogOfWar` folder from this zip into your new project's `Assets` folder. Do it in Explorer while Unity is open and it imports automatically.
3. In Unity's top menu: **Fog of War → 1. Create and open the sim scene**, then press **Play**.
4. To make the `.exe`: **Fog of War → 2. Build Windows game**. It lands in `<project>/Builds/Windows/FogOfWar.exe` and Explorer opens on it.

You don't need to set up any scene by hand. A bootstrap script also starts the sim in any scene you press Play in.

## Controls

- Left-drag orbits (or looks around, in the 1g trip). Right-drag pans where it's allowed. The wheel zooms.
- Keys 1–8 switch scenes, H hides the panels, F11 toggles fullscreen, Esc quits.
- If the black hole or wormhole stutters, set **Ray-trace quality** at the bottom of the panel to Fast or Balanced.

## Art

The models, sky maps, star catalogue and planet textures come from the art pack in `space-sim-art/` (its Blender scripts live there too) and are already copied into `Assets/FogOfWar/Resources/`. What they do in the game:

- **Real sky.** The wormhole and black hole ray-trace against a 4096×2048 map of the actual sky (9,096 Bright Star Catalogue stars plus the Milky Way, Magellanic Clouds and M31). The black hole has the galactic centre behind it. Every other scene's starfield, and the 1g trip, use the catalogue stars at their true positions.
- **Planets.** Earth uses NASA day, night-lights, cloud, ocean-glint and normal maps. The Moon is tidally locked with its near side to Earth, and Mars has its real colour and MOLA relief. The meteor scene's globe is the real night-side Earth, with Muscat under the antenna and the transmitter path heading toward Karachi.
- **Models.** Warp ship, FM mast, RTL-SDR dipole kit, Roche-limit rubble, and the 1g torch ship (pick the "Chase camera" view in the 1g trip).

`Editor/FogOfWarArtImport.cs` sets the texture import options: 4096 max size, no mips on the sky maps, linear data maps, and the Mars height map left readable. **Fog of War → 1** re-imports the art with those settings, so run it once after copying the folder in. If an art file is missing, that part falls back to the procedural look.

## Layout

```
Assets/FogOfWar/
  Scripts/            FogOfWarApp (camera, IMGUI panels), OrbitCamera, Util, Models, Art (textures, star catalogue)
  Scripts/Scenes/     one file per simulation
  Resources/Shaders/  HLSL: ray tracers, relativistic stars, CMB, planets, warp surface
  Resources/Models/   FBX from the art pack
  Resources/Sky, Stars, Textures/   sky maps, bsc5.csv, planet maps
  Editor/             the "Fog of War" menu (scene setup + Windows build) and the art import settings
```

To add a topic, write a new `SimScene` subclass in `Scripts/Scenes/` and add it to the list in `FogOfWarApp.Awake`.
