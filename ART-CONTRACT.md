# Art hand-off contract (code side)

What the Unity code expects from Blender output, so art can drop in without code changes.

## Models (already wired)
- Drop FBX into `Assets/FogOfWar/Resources/Models/`. The game loads by file name via `Models.Spawn(name)`.
- Names the game asks for today: `WarpShip`, `RadioTower`, `Antenna`. A missing file falls back to primitives, so nothing breaks.
- Export settings: `axis_forward="-Z"`, `axis_up="Y"`, `bake_space_transform=True`, `apply_scale_options="FBX_SCALE_ALL"`, empties + meshes only.
- Scale: about 1 Blender unit long. Each scene rescales the model itself.
- `WarpShip`: an empty named `Nose` at the front (the game rotates the model so Nose points along travel), an object named `Engine` (pulses).
- Any material or object with `Glow` in its name renders emissive. Every other material is swapped for the game's own lit shader, keeping only the base colour, so don't bother with PBR maps or node setups. Unity won't see them.
- Keep it under roughly 50k triangles per model.

## Wired as of the first art pack
- `Sky/SkyOurs.png`, `Sky/SkyOther.png`: skyA/skyB in FOWCommon.cginc (global `_SkyA`, `_SkyB`, `_SkyRot`). SkyOurs is
  the SVS Milky Way plus the Yale Bright Star Catalogue at 8192x4096, drawn like the mesh sky, and `Sky/MilkyWay.png`
  (SVS diffuse Milky Way) draws behind `Gfx.Starfield`; both come from `Tools/fidelity/sky.py`.
- `Stars/bsc5.csv`: `Art.Catalogue()` feeds `Gfx.Starfield` and the 1g trip.
- `Planets/Earth/{Color,Night,Clouds,Ocean,Normal}`: Earth.shader (open world, Tides) and Ground.shader (Meteors).
  `Planets/Moon/Color`: Moon.shader. `Planets/Mars/Color` and `Planets/Mars/Height` (16-bit): Mars.shader and
  MarsScene's relief. The old `Textures/` copies are gone: a missing path silently falls back to the procedural look.
- `Models/Rubble.fbx` (Tides Roche ring), `Models/TorchShip.fbx` (1g trip chase camera).
- Each file keeps its name and lookup (`SkyUV`: u = 0.5 + atan2(z,x)/2π, v = 0.5 + asin(y)/π). Swapping in a better file with the same name needs no code change.

## Anything new
Send the code thread the path in `/mnt/project-files/space-sim-art/` and which scene it belongs to.

## Open System (scene 9): open-world contract

Adopts the art side's `OPEN-WORLD-ART.md` naming. The code is the source of truth for physical values
(radii, flattening, poles, rotation, orbits); art files carry looks only.

### Maps: `Planets/<Body>/`
- Loaded by name: `Planets/<Body>/Color` (any image type). Already wired: Mercury, Venus, Jupiter, Saturn, Uranus, Neptune.
  Every body, Earth, Moon and Mars included, loads from `Planets/<Body>/`. `Planets/<Body>/Normal` (tangent space,
  east = +x, north = +y, BC5) adds DEM relief on the Moon, Mercury, Mars and Earth. Maps go up to 8192x4096 and are
  rebuilt by `Tools/fidelity/` (see its README); copying the art pack over them would put the 4K maps back.
  Major moons (increment 2) will load `Planets/<Moon>/Color` with the same convention.
- Layout: equirectangular, longitude 0 at the centre, east to the right, north up; object +Y = IAU north pole,
  +X = prime meridian (`SkyUV`). The globe is rendered as an oblate spheroid from the code's equatorial radius and
  flattening, so draw maps on the sphere, not pre-squashed.
- `Height.png` + `Height.json` (km = minKm + grey·(maxKm − minKm)) and `Clouds.png`: imported linear, single channel;
  heights uncompressed and CPU-readable. Not yet displaced in the open world (Mars scene only).
- Without a map, the body renders procedurally from its two colours, so any map is a drop-in.

### Rings: `Rings/<Planet>_Rings.png` + `.json`
- Imported as uncompressed RGBA half, linear, clamped, no mips (FogOfWarArtImport).
- Alpha is optical depth on a log scale: `a = (log10 τ + 9) / 10` over τ = 10⁻⁹…10, a = 0 means no ring;
  decode τ = 10^(10a − 9). (Linear 16-bit alpha lost Jupiter's rings, τ ≈ 10⁻⁶, and left
  Neptune's Galle/Lassell on a handful of grey levels.) The shader applies the slant path τ/|μ| itself.
- Rows 0–1 lit face, rows 2–3 unlit/forward scatter (as drafted). Rendered in increment 2.

### Irregular bodies: `Models/Bodies/<Name>.fbx`
- Mean radius 1, +Y north, +X long axis (toward the parent when locked). The code scales by the body's mean radius
  and locks +X to the parent. Phobos and Deimos are wired; the rest arrive with their moon systems.
- `Bodies.json`: `diametersKm` (full axes), `meanRadiusKm`, `geometricAlbedo`. Mean radii follow IAU
  or the shape-model volume.

### Asteroid set: `Models/Asteroids/Asteroid00..31.fbx`
- Used for the belt, Trojans and tiny moons in increment 3 (GPU instanced). Mean radius 1, under ~2k tris.

### Player ship: `Models/PlayerShip.fbx` + `PlayerShip.json`
- Wired. The code aligns `Nose` to the direction of thrust, scales to `lengthM` (52 m), pulses `Engine` from its
  origin at the nozzle, and draws the drive plume behind it. `Cockpit`, `RCS_*` and `PDC_*` are reserved for the
  cockpit camera, RCS puffs and point-defence turrets.
- `PDC_0..5` are empties. Each one's local +Y (as Unity imports it) is the turret's zenith, and the mount covers 100°
  about it, a hemisphere and 10° more (zenith · d > −sin 10°; the hull blocks nothing). `Hull.Mounts` in `ShipBuild.cs` holds the six zeniths
  in the ship's body frame (x starboard, y up, z nose); QA 17t checks them against the model within 1°, so a moved or
  turned `PDC_n` needs that table changed with it.
- Textured (ship-visuals branch): `Models/PlayerShip/PlayerShip_{Albedo,Normal,Mask,Emission}.png`, 4096 (emission
  2048), from `Blender/ship_pbr.py`. One UV atlas on the hull mesh; `Engine` keeps its glow material. Albedo and
  emission sRGB; **Normal is object space in Blender axes** (all three channels, BC7, not BC5; ShipPBR converts with
  `ShipLighting.BlenderToMesh`, checked by Fog of War/Check ship normal map); Mask is linear R metallic, G occlusion,
  A smoothness. Rendered by `FogOfWar/ShipPBR` (GGX) under `ShipLighting`. Without the texture set the old flat look
  stays. Every surface is procedural; see `space-sim-art/SHIP-CREDITS.md`.

### Stealth corvette: `Models/StealthShip.fbx` + `StealthShip.json`
- Wired (combat PR 3f). The FWO-060 stealth corvette's own model; `Hull.Model` in `ShipBuild.cs` names it, and the
  open world spawns the fitted hull's model (`OpenSystemScene.Hull.cs` `RefreshHullModel`, again after a refit, a load
  or a new career). Same frame as the player ship (x from -0.60 nozzle exit to +0.50 nose, 1.1 model units), scaled to
  the hull's `lengthM` (60 m). Markers: `Nose`, `Engine` (origin at the bell throat), `Cockpit`, the eight
  `RCS_<Fore|Aft>_<Up|Down|Left|Right>`, `PDC_0..5`, and `Radiator_0..3`. `Cockpit`, `RCS_*` and `PDC_*` are reserved,
  as on the player ship, and the `PDC_n` zenith rule is the same.
- `Radiator_0..3` are separate objects with their origin on the hinge and local X from hinge to tip (never apply their
  transforms). While the radiators are stowed (`Career.Stowed`) each one's local X scale eases to 0.05 of its import
  value over 1.5 s of real time, and back when they come out. The FBX axis conversion swaps the panel's local Y and Z;
  local X, the fold axis, is unchanged. A model without `Radiator_n` folds nothing.
- Textured: `Models/StealthShip/StealthShip_{Albedo,Normal,Mask,Emission}.png`, from `Blender/stealth_pbr.py`, the
  same sizes and encodings as the player ship. `Body` and the four radiators share the one atlas and the one hull
  material; `Engine` keeps its glow material; the nav lights are baked into the emission map. Checked by Fog of
  War/Check ship normal map with the player ship's.
- Missing files: no `StealthShip.fbx` spawns `PlayerShip` (then `TorchShip`) at the corvette's length; no texture set
  keeps the flat `Models.Spawn` look.
- Floodlights: the corvette has its own four mounts in the same frame (`ShipLighting.CorvetteFloods`): its flanks are
  far narrower than the player ship's, and its keel railgun runs where the player ship's chin lamp sits.

### Physical lighting (open world)
`OpenWorld/ShipLighting.cs` sets these shader globals every frame; `FogOfWarApp` resets them to neutral for the
other scenes:
- `_FOWSunIrr`: sunlight at the ship, 1 at 1 AU, inverse square. `_FOWEclipse`: fraction of the solar disc hidden
  by a body, seen from the ship (umbra and penumbra from the overlap of the two discs).
- `_FOWShineDir` (xyz direction, w angular radius) and `_FOWShineCol` (linear RGB irradiance): planetshine from the
  brightest sunlit body, a Lambert sphere with Body.json `geometricAlbedo` at its phase angle.
- `_FOWExposure`: camera exposure, 1 = the tuned look in full sunlight at 1 AU. It adapts toward 1 / (light on the
  ship), clamped to 1/8..64 (-3..+6 stops), and opens at most 2 stops further in an eclipse.
- `_FOWPlumePos`/`_FOWPlumeCol`: the drive plume as a point light, zero unless the drive fires.
- `_FOWFloodPos[4]`/`_FOWFloodDir[4]`/`_FOWFloodCol`: four hull floodlights (G toggles, `Controls.Action.Floodlights`),
  spot cones aimed back at the hull, about 5,000 lux at 10 m; zero while off. They count toward the exposure meter.
- `_ShipShadowTex`/`_ShipShadowMatrix`/`_ShipShadowParams`: the hull's self-shadow map along the sunlight.
- Body shaders scale their sunlit output with `fowSunlit(_SunIrr)` (per-material `_SunIrr`, default 1) and their
  self-luminous output by `_FOWExposure` (FOWCommon.cginc). The sky and stars are not exposure-scaled.
- A subtle bloom (`OpenWorld/Bloom.cs`) runs on the main camera in the open world only; `-fownobloom` disables it.
