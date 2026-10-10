# Observatory: developer guide

How to build and test the Observatory, the repository layout and every data credit. The [models and limits guide](MODEL-FIDELITY.md) records each scene's assumptions, supported domain and validation limits.

## Contents

- [The scenes](#the-scenes)
- [Open and build](#open-and-build)
- [Settings and capture](#settings-and-capture)
- [Unattended test runs](#unattended-test-runs)
- [Repository layout](#repository-layout)
- [Credits and licence](#credits-and-licence)

## The scenes

The program opens on the main menu over the first scene. Keys **1–9 / 0** select scenes 1–10; **Shift + digit** selects scenes 11–20. The scrollable scene rail and **Esc → Scenes** also switch scenes. The rail scrolls to the selected exhibit.

| Key | Scene | Physics |
|---|---|---|
| 1 | Wormhole | Morris–Thorne metric, ray-traced per pixel to another sky |
| 2 | Black Hole | Schwarzschild or Kerr: lensing, photon ring, Doppler-beamed disc, spin, tidal disruption |
| 3 | Warp Bubble | Alcubierre York-time surface: space shrinks ahead and stretches behind |
| 4 | 1g to Anywhere | Special relativity: cross the galaxy in about 24 ship-years, with aberration and Doppler colour |
| 5 | Negative Mass | Bondi's runaway pair: free acceleration forever |
| 6 | Moon 1,000 km Closer | True-scale tides, month length and Roche-limit breakup |
| 7 | Terraform Mars | Energy balance, greenhouse optical depth, lapse rate |
| 8 | Radio Meteors | Forward scatter heard with an SDR from Muscat |
| 9 | Three-Body Lab | Planar softened Newtonian gravity: figure-eight, rotating triangle and 3:4:5 encounter |
| 0 | Double Pendulum | Two independent pendulums with a small initial-angle difference; equal masses and rod lengths |
| Shift+1 | Lorenz Attractor | Three-variable convection system; twin trajectories and variable rho |
| Shift+2 | Reaction–Diffusion | Gray–Scott chemistry on a periodic 64×64 lattice |
| Shift+3 | Wave Interference | Damped scalar wave equation, reflecting walls and phase-controlled point drivers |
| Shift+4 | Gravity Assist | Newtonian flyby with a second inertial-frame velocity comparison |
| Shift+5 | Lagrange-Point Lab | Planar circular restricted three-body dynamics near L4/L5 |
| Shift+6 | Charged Particle | Exact nonrelativistic motion in uniform perpendicular electric and magnetic fields |
| Shift+7 | Rutherford Scattering | Repulsive inverse-square trajectories at varying impact parameters |
| Shift+8 | Resonance Bank | Nineteen driven damped oscillators, compared with their analytic steady amplitudes |
| Shift+9 | Flocking Lab | Bounded Reynolds-style steering on a periodic plane |
| Shift+0 | Galaxy Encounter | Two gravitating cores and 192 massless stellar disk tracers |

In the two labs, **Pause** exposes a single-step button; **Replay** resets time and trails. Changing a starting condition restarts the experiment, while speed and trail visibility leave the physical state intact. The three-body camera can keep all bodies in frame; scrolling switches to manual zoom. **Top view** and **Front view** restore useful camera angles.

Scenes 11–20 share **Pause / Resume**, **Replay**, **Reset view**, **One model step** (while paused) and a **Playback speed** slider. Parameter sliders restart the model; speed and camera controls preserve it. The reaction and wave fields have **Plant seed** and **Drop pulse** actions. Galaxy auto zoom can be disabled by scrolling or its toggle. Charged-particle view follows the particle except in photo mode.

If the black hole or wormhole stutters, set **Ray-trace quality** (Esc → Settings → Graphics) to Fast or Balanced.

## Open and build

1. Install **Unity 6.6** (editor `6000.6.4f1`, see `ProjectSettings/ProjectVersion.txt`) with **Windows Build Support (Mono)**.
2. Clone this repo.
3. In Unity Hub, choose **Add → Add project from disk** and select the repo folder. The first import takes a while.
4. Use the **Fog of War** menu: **1. Create and open the sim scene**, then press Play. This also re-imports the art with the right settings.
5. To build: **Fog of War → 2. Build Windows game**. The program lands in `Builds/Windows/FogOfWar.exe`.

To build from a terminal:

```bash
Unity.exe -batchmode -quit -projectPath "<repo path>" -executeMethod FogOfWar.EditorTools.FogOfWarBuild.BuildWindows -logFile Logs/build.log
```

The **Build … review copy** menu item builds the same program under another name, in `Builds/BlackHole`. A review build never replaces the main build in `Builds/Windows`.

## Settings and capture

- Settings and key bindings are stored in Unity `PlayerPrefs` (on Windows, the registry under `HKCU\Software\Nasser\Observatory`). The program has no save game.
- **Settings** (Esc → Settings) has four tabs: Graphics (display mode, v-sync, frame cap, texture detail, ray-trace quality), Interface, Controls and Capture.
- **Screenshots and clips** go to `Pictures\FogOfWar`. Screenshots can be 1×, 2× or 4× the screen size.
- **Clips** are MP4 through ffmpeg, found on `PATH` or at `ffmpeg\ffmpeg.exe` next to the program. Without ffmpeg, it writes PNG frames and an `encode.bat`.
- **Photo mode** stops or slows time, hides the HUD and gives you a free camera with lens and colour controls. Its Cinema tab flies an orbit, a flyby or a keyframed path and can record it.

## Unattended test runs

`Tools/qa.sh <dir>` builds the Windows program and runs the suites a PR reports, in order: `uishots` and `qa`. It stops at the first nonzero exit and leaves each report under `<dir>/<suite>/`. Name suites to run a subset, or add `tour`, `bhshots`, `physics` or `gpu`. `QA_NOBUILD=1` reuses the last build. The scripts find the editor under the Unity Hub folder and keep one build or suite running at a time with a lock in the temp folder; `Tools/unity-env.sh` names the variables (`UNITY_EDITORS`, `FOW_UNITY_LOCK`) and the gitignored `Tools/unity.local` that can set them.

The built program takes these flags. Each run writes its report and screenshots to `<dir>`, then quits with 0 when the run is clean, 1 on a failed check or a logged error, and 2 when the run did not finish.

| Flag | What it does |
|---|---|
| `-fowqa <dir>` | Drives every scene's controls to their extremes, flags NaN/∞ readouts and logged errors, and stress-tests scene, quality and window changes. Writes `qa.txt`. `-fowqascene N` limits it to scene N; `-fowui` shoots only the IMGUI menus |
| `-fowtour <dir>` | Visits every scene and screenshots each. Writes `tour.txt` |
| `-batchmode -fowuishots <dir>` | Renders the menus, each scene and photo mode offscreen and saves a PNG per state. Set sizes with `-fowuires`, for example `1920x1080,1440x2560`. No window opens |
| `-batchmode -fowbhshots <dir>` | Renders a fixed set of black hole views offscreen and times the ray tracer. Options: `-fowbhres`, `-fowbhss`, `-fowbhonly`, `-fowbhui` |
| `-fowbhclip <dir>` | Renders the offscreen black hole montage |

The editor checks run in batch mode: `physics` (`FogOfWarPhysicsCheck.Run`: Kerr and Schwarzschild geodesics, the relativistic journey) and `gpu` (`FogOfWarGpuCheck.Run`: the black hole shader against the reference rays in `Tools/benchmarks/fixtures/rays.json`). See `Tools/benchmarks/README.md`.

Other flags: `-fowhidehud` hides the HUD. `-fowcapturedir <dir>` sends captures somewhere other than Pictures. `-fownobloom` turns the black hole's glare off. `-fowlegacyui` uses the old IMGUI shell.

### Chaos lab checks without Unity

```powershell
pwsh -NoProfile -File Tools/check-chaos.ps1
```

This compiles `ThreeBodyModel.cs` and `DoublePendulumModel.cs` directly with PowerShell's C# compiler. The 85 assertions cover conservation over 60 model-time units, preset/nudge extremes, an analytic rotating triangle, an approximate figure-eight return, the pendulum's small-angle normal mode, deterministic replay, twin divergence, timestep refinement and invalid parameters. A failed assertion exits nonzero.

For native checks, the normal `qa`, `tour` and `uishots` suites include both scenes. `-fowqascene 9` and `-fowqascene 10` exercise their controls, including pause, replay, hidden trails and extreme settings. Check **9 / 0** selection, scrolling to the last rail entry on small windows, pause/single-step, scene re-entry and photo mode in a current build. The headless model checks do not establish Unity compilation, visuals, native input or frame rate.

### Additional exhibit and scene checks

```powershell
pwsh -NoProfile -File Tools/check-exhibits.ps1
pwsh -NoProfile -File Tools/check-scenes.ps1
```

The first runner compiles the actual pure C# equations. Fixtures cover Lorenz equilibria and refinement, reaction positivity and periodic transport, wave damping and destructive interference, central-force invariants and gravity-assist direction, L4/L5 equilibria and Jacobi drift, exact Lorentz work-energy balance and drift, analytic forced resonance, deterministic bounded flocking, and galaxy core invariants with finite tracers. See [ModelChecks.cs](../Tools/exhibits/ModelChecks.cs) for parameter ranges, elapsed times and tolerances.

The second compiles the twelve added scenes, their models, `SimScene` and `Controls` against small adapters. It checks all twenty digit mappings, reserved bindings, stable controls, pause/single-step/replay, 30/120 fps trajectories, slider limits, recipes, field actions, bounded geometry and re-entry. The adapters check data passed to meshes/textures; they do not emulate Unity rendering, input dispatch, transforms or native object lifetime.

The native `QA.Steps` now covers all twenty scenes, including extreme controls, paused clocks and replay for the new ten. Use `-fowqascene 11` through `20` to focus it. The portrait/window pass includes both field scenes, flocking and the final rail entry. Also manually verify **Shift+1…Shift+0**, mouse orbit/zoom, rail scrolling, field colours, marker visibility and photo mode. Unity 6000.6.4f1 was unavailable when these exhibits were implemented; native builds, screenshots and frame rate remain unverified.

## Repository layout

```
Assets/FogOfWar/
  Scripts/              FogOfWarApp (camera, scenes, menus), Controls, QA and tour harnesses
  Scripts/Scenes/       one file per scene; the black hole has its partials, Kerr, KerrGeo and BHPost
  Scripts/Capture/      photo mode, screenshots, clip recorder, offscreen shot tools
  Scripts/UI/           UI Toolkit shell, menus and settings
  Resources/            shaders, Earth, Moon and Mars maps, models, sky maps, star catalogue, UI theme and fonts
  Editor/               the "Fog of War" menu: scene setup, builds, art import settings, checks
Tools/
  blackhole/            Python reference tracer and image checks for the black hole shader
  benchmarks/           reference rays and image comparison for the GPU check
  chaos/                production-model checks for the first two labs
  exhibits/             model checks and limited headless scene adapters
```

To add a scene, write a `SimScene` subclass in `Scripts/Scenes/` and add it to the list in `FogOfWarApp.Awake`.
Add its control extremes to `QA.Steps` too. `LabScene` provides shared playback, bounded batched markers/lines and optional scalar-field drawing for small exhibits; model equations remain independent of Unity. Digits and Shift+digits cover twenty scenes; the rail and menu use the full scene list.

## Credits and licence

### Licence

The repository has three parts, each under its own terms.

- **Code: GNU General Public License v3.0 only** ([`LICENSE`](../LICENSE)). This covers the C# scripts (`Assets/FogOfWar/Scripts`, `Assets/FogOfWar/Editor`), the shaders (`Assets/FogOfWar/Resources/Shaders`), the UI style sheets (`*.uss`, `*.tss`), the scripts and tools in `Tools/`, and the Unity project files (`ProjectSettings/`, `Packages/`, the scene and the `.meta` files).

  ```
  Copyright (C) 2026 Nasser Albusaidi

  This program is free software: you can redistribute it and/or modify it under the terms of
  the GNU General Public License as published by the Free Software Foundation, version 3.

  This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
  without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
  See the GNU General Public License for more details.

  Additional permission under GNU GPL version 3 section 7

  If you modify this Program, or any covered work, by linking or combining it with the Unity
  Editor and engine, including its built-in modules and packages (or a modified version of
  them), containing parts covered by the terms of Unity's licence agreements, the licensors of
  this Program grant you additional permission to convey the resulting work.
  ```

  The additional permission lets anyone share a build of the program, which always contains Unity's proprietary engine.

- **The project's own art and text: Creative Commons Attribution 4.0 International ([CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)).** © 2026 Nasser Albusaidi. This covers everything else made for the project: the ship, rubble, antenna and tower models, the docs, and the screenshots and videos in `docs/media`. You may share and change them, if you credit Nasser Albusaidi, link the licence and say what you changed. Third-party content inside them, such as the sky in the screenshots, keeps its own terms.
- **Third-party content keeps its own licence.** The sources and their credits are below. Files built from them, such as the reprojected maps, keep their source's terms.

The Unity Editor and engine are not in this repository. They are under Unity's own terms.

### Sources

- **Earth**: NASA Visible Earth (Blue Marble Next Generation, Black Marble, MODIS clouds) and GEBCO elevation (GEBCO Compilation Group, GEBCO_08 Grid).
- **Moon**: LRO WAC colour from the CGI Moon Kit and LRO LOLA relief. Credit: NASA's Scientific Visualization Studio.
- **Mars**: USGS Astrogeology and PDS. Colour (Viking MDIM, 925 m): USGS Astrogeology Science Center and PDS (MDIM by Eliason, Batson and Manley); hue from the NASA 3D Resources Mars map (NASA/JPL-Caltech). Relief (MGS MOLA DEM, 463 m): MOLA Team, NASA Goddard Space Flight Center.
- **Sky**: NASA/Goddard Space Flight Center Scientific Visualization Studio, Deep Star Maps 2020. Gaia DR2: ESA/Gaia/DPAC. ESA distributes Gaia data under [CC BY-NC 3.0 IGO](https://www.cosmos.esa.int/web/gaia-users/license). So `Sky/SkyOurs.png`, `Sky/MilkyWay.png` and the screenshots that show this sky are for non-commercial use only. For commercial use, ESA points to its Terms and Conditions for the use of data in the ESA space science archives. Stars from the Yale Bright Star Catalogue, 5th revised edition (Hoffleit and Warren) (`bsc5.csv`).
- **Fonts**: Barlow and JetBrains Mono, under the SIL Open Font License (licence files in `Resources/UI/Fonts`).
- **Models**: made for this project, in code or by its own Blender scripts. A new outside asset must be CC0 or public domain, and must be credited.
- **Earlier versions in the git history**:
  - From 2f3642c until 94b4de0 removed them, `Resources/Textures` held the first Earth, Moon and Mars textures. The Earth and Moon files came from the three.js example planet textures (`examples/textures/planets` in mrdoob/three.js).
  - From 3bcb67a until 94b4de0 replaced it, the Moon map was the SELENE (Kaguya) Terrain Camera morning mosaic, ©JAXA/SELENE, from USGS, resampled and recoloured. We thank the SELENE(KAGUYA) TC team and the SELENE Data Archive for providing the SELENE(KAGUYA) data. SELENE is a Japanese mission developed and operated by JAXA.

NASA, USGS and PDS data are public domain in the United States. The credits above are the ones their sources ask for.
