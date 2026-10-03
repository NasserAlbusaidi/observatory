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
- `Sky/SkyOurs.png`, `Sky/SkyOther.png`: skyA/skyB in FOWCommon.cginc (global `_SkyA`, `_SkyB`, `_SkyRot`).
- `Stars/bsc5.csv`: `Art.Catalogue()` feeds `Gfx.Starfield` and the 1g trip.
- `Textures/Earth_*`: Earth.shader (Tides) and Ground.shader (Meteors). `Moon_Color`: Moon.shader. `Mars_Color` and `Mars_Height`: Mars.shader and MarsScene's relief.
- `Models/Rubble.fbx` (Tides Roche ring), `Models/TorchShip.fbx` (1g trip chase camera).
- Each file keeps its name and lookup (`SkyUV`: u = 0.5 + atan2(z,x)/2π, v = 0.5 + asin(y)/π). Swapping in a better file with the same name needs no code change.

## Anything new
Send the code thread the path in `/mnt/project-files/space-sim-art/` and which scene it belongs to.
