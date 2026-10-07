# Fog of War Observatory: developer guide

The full reference behind the [store page](../README.md): game rules and numbers, controls, how to build and test, the repository layout and every data credit.

## Contents

- [Game rules](#game-rules)
- [The solar system](#the-solar-system)
- [The Observatory](#the-observatory)
- [Controls](#controls)
- [Open and build](#open-and-build)
- [Saves, settings and capture](#saves-settings-and-capture)
- [Unattended test runs](#unattended-test-runs)
- [Repository layout](#repository-layout)
- [Credits and licence](#credits-and-licence)

## Game rules

### Time against reaction mass

The nav computer shows these numbers before every trip:

- **Brachistochrone** (burn, flip at the midpoint, brake): t = 2√(d/a), Δv = 2√(a·d).
- **Burn, coast, burn**: cap the cruise speed at a fraction f of the brachistochrone peak. Then Δv = f × (brachistochrone Δv) and t = √(d/a)·(f + 1/f). At f = 0.5 you need half the Δv, and the trip takes 25 % longer.
- **Rocket equation**: reaction mass used = m·(1 − e^(−Δv/vₑ)). Cargo mass costs reaction mass.
- **The planets move.** The same contract can cost several times more reaction mass on a different date.

Deadlines are in game days, so time compression saves your patience but not the deadline. A tight deadline forces a hard burn. A loose one lets you coast.

### Your ship

| | Start | Upgrades (at any port) |
|---|---|---|
| Drive | 1 g | 3 g for 50,000 cr, then 5 g for 150,000 cr |
| Reaction mass (water) | 200 t tank | +100 t for 35,000 cr, twice (each adds 8 t dry mass) |
| Dry mass | 300 t | |
| Cargo hold | 100 t | |
| Sensors | stock | 30,000 cr: scan range ×2, scan time ÷2 |
| Credits | 25,000 cr | |

If the tank runs dry, you are adrift. An emergency tender brings 30 t for 20,000 cr. Debt is allowed.

On screen, the drive's efficiency and endurance above 3 g are labelled as fictional. The trajectories and Δv are real.

### Ports

You are docked when you are in a bound orbit close to the port body with the drive off. The price of water follows how easy it is to get there.

| Port | Reaction mass, cr/t | Why |
|---|---|---|
| Earth orbit | 60 | Deep gravity well: water is lifted from the ground |
| Luna | 35 | Polar craters |
| Mars orbit | 30 | Ice under the surface |
| Vesta | 25 | Dry rock: water is shipped in |
| Titan | 15 | Water ice under the haze |
| Ganymede | 10 | Ice shell |
| Callisto | 10 | Ice and rock, outside Jupiter's worst radiation |
| Enceladus | 9 | Plume water from the south pole |
| Ceres | 8 | Ice-rich: the belt's water port |

### Jobs

Each port has a contract board that refreshes every game day. You can hold three jobs at a time.

| Kind | What you do |
|---|---|
| Cargo | Carry freight from port A to port B before the deadline |
| Passengers | Carry people from A to B. Sustained acceleration is capped at 1 g for their comfort |
| Survey | Go to a named moon or asteroid. Stay in scan range with the drive off for 2 hours |
| Rescue | A ship with a dead drive coasts on its own orbit. Match velocity within 500 km and 50 m/s, then hold for 30 minutes |
| Intercept | A ship under thrust between two ports won't stop for you. Catch it, match it within 500 km and 100 m/s while it burns, and hold for 10 minutes |

A late job pays half. After twice the time allowed, it fails and pays nothing.

### Discovery

- **Codex.** Pass close to a body to log its data from the game's own ephemeris. Scan it to unlock real facts (43 bodies have them).
- **Survey data.** The first scan of a body sells for 400 cr + 600 cr per AU from the Sun, ×1.5 for bodies under 100 km. The far moons pay for the trip out.
- **16 achievements**, from "Paid in full" to "Edge of the map" (30 AU from the Sun).

A new career starts docked at Earth with one job: medical supplies to Luna. A short first-flight lesson teaches turning, burning, attitude holds, targeting, the autopilot and time compression. You can skip it.

## The solar system

- **Positions.** The planets, the Moon and 21 named asteroids use Chebyshev fits to JPL Horizons vectors from 2000 to 2100 (TDB). Outside that span, bodies fall back to mean orbital elements.
- **453 moons** from the JPL SSD tables: Mars 2, Jupiter 115, Saturn 291, Uranus 29, Neptune 16. Fast moons are re-fitted to Horizons so resonances such as Io–Europa–Ganymede hold.
- **Asteroid belt.** A procedural sample of about 1 % of the real belt, with the Kirkwood gaps, five collision families, Hungarias, Hildas in the 3:2 resonance and Jupiter's Trojans.
- **Flight.** Coasting follows two-body conics around the dominant body (patched conics). Time compression runs from 1× to 1M×. The autopilot plans at 0.3, 1, 3, 5 or 10 g, flips at the midpoint and parks in orbit. You can also fly by hand with RCS and reaction wheels.
- **Views.** Chase, orbit and cockpit cameras, a system map, and hull floodlights for the night side.
- **Sound.** All ship sound (drive, RCS, wheels, creaks, docking, alarms) is synthesised live by the game. No audio files ship.
- **Rendering.** Physics runs in double-precision heliocentric km. The camera is the floating origin, and anything past 100,000 km is compressed logarithmically, so angular sizes stay exact.

## The Observatory

Open it from the main menu (**Esc → Observatory**). Keys **1–8** switch scenes there.

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

If the black hole or wormhole stutters, set **Ray-trace quality** (Esc → Settings → Graphics) to Fast or Balanced.

## Controls

Press **F1** in flight for the full list. Rebind any action in **Esc → Settings → Controls**. Each action has two slots.

| Group | Action | Default |
|---|---|---|
| Attitude | Pitch nose down / up | W / S |
| | Yaw left / right | A / D |
| | Roll left / right | Q / E |
| Drive | Throttle up / down | L-Shift / L-Ctrl (or Num + / Num −) |
| | Throttle full / cut | Z / X |
| Thrusters (RCS) | Forward / back | H / N |
| | Up / down | I / K |
| | Left / right | J / L |
| Flight computer | Flight assist on/off | V |
| | Flip (turn 180°) | F |
| | Engage / abort nav plan | P |
| | Next target | Tab |
| | Time compression up / down | . / , (or ] / [) |
| View and game | System map | M |
| | Camera: chase / orbit / cockpit | C |
| | Show controls | F1 |
| | Quick save | F5 |
| Capture | Photo mode | F10 |
| | Record a clip | F9 |
| | Screenshot | F12 |
| | Mark the flight log | F8 |
| Other | Hull floodlights on/off | G |
| | Velocity reference: orbit / target | B |

**Fixed keys** (you can't rebind them):

- **1–9**: attitude hold off, prograde, retrograde, normal, anti-normal, radial out, radial in, target, anti-target.
- **Esc**: menu. **F2**: hide the HUD. **F11**: fullscreen.

**Mouse**: drag to look, scroll to zoom, click a label to target that body.

With flight assist on, the attitude keys set the turn rate, and the ship stops turning when you release them. With it off, they apply torque and the spin stays. Turning by hand ends an attitude hold. Flip ends by itself when the turn is complete.

Turning uses reaction wheels and costs no reaction mass. Turning alone doesn't slow you down: burn to brake.

## Open and build

1. Install **Unity 6.6** (editor `6000.6.4f1`, see `ProjectSettings/ProjectVersion.txt`) with **Windows Build Support (Mono)**.
2. Clone this repo.
3. In Unity Hub, choose **Add → Add project from disk** and select the repo folder. The first import takes a while.
4. Use the **Fog of War** menu: **1. Create and open the sim scene**, then press Play. This also re-imports the art with the right settings.
5. To build: **Fog of War → 2. Build Windows game**. The game lands in `Builds/Windows/FogOfWar.exe`.

To build from a terminal:

```bash
Unity.exe -batchmode -quit -projectPath "<repo path>" -executeMethod FogOfWar.EditorTools.FogOfWarBuild.BuildWindows -logFile Logs/build.log
```

The **Build … review copy** menu items build the same game under another name, in `Builds/Audio`, `Builds/BlackHole`, `Builds/Cockpit`, `Builds/Fidelity`, `Builds/Ship` or `Builds/UI`. A review build never replaces the main build in `Builds/Windows`.

## Saves, settings and capture

- The game, career, settings and key bindings are stored in Unity `PlayerPrefs` (on Windows, the registry under `HKCU\Software\Nasser\Fog of War Observatory`). The save keys are `fow-save-v1` and `fow-career-v1`.
- **Settings** (Esc → Settings) has five tabs: Graphics (display mode, v-sync, frame cap, texture detail, ray-trace quality), Interface, Controls, Capture and Audio.
- **Screenshots and clips** go to `Pictures\FogOfWar`. Screenshots can be 1×, 2× or 4× the screen size.
- **Clips** are MP4 through ffmpeg, found on `PATH` or at `ffmpeg\ffmpeg.exe` next to the game. Without ffmpeg, the game writes PNG frames and an `encode.bat`.
- **Flight log.** Each run of the game writes `flight-<date>.log` to the capture folder; the newest 20 are kept. It holds a state line each second (`S`), a line for each change of hold, plan, phase, warp, target, camera, throttle or stick with the keys pressed in that frame (`EVT`), sanity rules such as `passed-target` (`WARN`) and Unity errors (`ERR`). **F8** writes a bookmark (`MARK`), the last 10 s at 10 Hz, and a screenshot. Press it when the ship does something you did not intend.
- **Photo mode** stops or slows time, hides the HUD and gives you a free camera with lens and colour controls. Its Cinema tab flies an orbit, a flyby or a keyframed path and can record it.

## Unattended test runs

The built game takes these flags. Each run writes its report and screenshots to `<dir>`, then quits.

| Flag | What it does |
|---|---|
| `-fowqa <dir>` | Drives every scene's controls to their extremes, flags NaN/∞ readouts and logged errors, and stress-tests scene, quality and window changes. Writes `qa.txt`. `-fowqascene N` limits it to scene N |
| `-fowtour <dir>` | Visits every scene and screenshots each. Writes `tour.txt` |
| `-fowcareer <dir>` | Plays the first jobs of a new career on the autopilot and compares trip estimates with what the flight used. Writes `career.txt` |
| `-fowaudioqa <dir>` | Drives the ship through its sounds, records the synth to `audioqa.wav` with the speakers muted, and writes `audioqa.txt` |
| `-fowshipshots <dir>` | Parks the ship in low orbit at Earth, Mars and Saturn and shoots day, terminator and night views. Writes `ship.txt` |
| `-fowfidelity <dir>` | Frames each mapped body up close and logs frame rate and every texture in memory. Writes `fidelity.txt`. `-fowtexdetail N` sets texture detail |
| `-batchmode -fowuishots <dir>` | Renders the game and its UI offscreen and saves a PNG per state. Set sizes with `-fowuires`, for example `1920x1080,1440x2560`. No window opens |
| `-batchmode -fowbhshots <dir>` | Renders a fixed set of black hole views offscreen and times the ray tracer. Options: `-fowbhres`, `-fowbhss`, `-fowbhonly`, `-fowbhui` |

Other flags:

- `-fowsavekey <key>` uses another save slot. Without it, unattended suites generate a unique `fow-save-qa-<nonce>` per process, isolating concurrent suites and worktrees from one another and from the player's save. Use an explicit unique key only when intentionally continuing a save across processes.
- `-fowqa` runs the career in sandbox: unlimited reaction mass and no contracts, so the physics tests keep working.
- `-fowhidehud` hides the HUD. `-fowcapturedir <dir>` sends captures somewhere other than Pictures. `-fowprof` logs frame time every 2 s. `-fownobloom` turns bloom off. `-fowlegacyui` uses the old IMGUI shell.
- `-fowspawnrunner <class>` (courier, packet, liner, freighter, tanker or hauler) starts a new game, accepts an intercept for that runner and holds the ship 2 km beside it, camera on it, for filming. It accepts a real job, so use it with `-fowsavekey`.
- `-fowterrain <dir>` loads the terrain pack from that folder only. Without the flag the game looks for a `Terrain` folder next to the exe, then `D:\SpaceSimArtSrc\terrain`. `-fowterrain none` runs on the 8K global maps, as a player without the pack does. `-fowqaterrain` runs only the terrain low passes of `-fowqa`.

## Repository layout

```
Assets/FogOfWar/
  Scripts/              FogOfWarApp (camera, scenes, menus), Controls, QA and tour harnesses
  Scripts/OpenWorld/    Ephemeris, belt, ship, career (ports, jobs, rescue, intercept), codex
  Scripts/Scenes/       one file per Observatory scene; OpenSystemScene.*.cs is the game
  Scripts/Cockpit/      procedural cockpit interior and its MFD pages
  Scripts/Audio/        the live ship synth (Dsp/) and its test
  Scripts/Capture/      photo mode, screenshots, clip recorder, offscreen shot tools
  Scripts/UI/           UI Toolkit shell, menus and settings
  Resources/            shaders, ephemerides, planet and ring maps, models, sky maps, star catalogue, UI theme and fonts
  Editor/               the "Fog of War" menu: scene setup, builds, art import settings, checks
Tools/
  horizons/             fit.mjs: Chebyshev fits to JPL Horizons (node Tools/horizons/fit.mjs)
  satellites/           build.mjs: the 453-moon table from JPL SSD
  fidelity/             Python pipeline that rebuilds planet, relief and sky maps from mission data (see its README)
  blackhole/            Python reference tracer and image checks for the black hole shader
  audio/                offline harness for the ship synth
ART-CONTRACT.md         what the code expects from Blender output
```

To add an Observatory scene, write a `SimScene` subclass in `Scripts/Scenes/` and add it to the list in `FogOfWarApp.Awake`. Put it before `OpenSystemScene`, which must stay last: the app treats the last scene as the game.

## Credits and licence

- **Ephemerides**: NASA JPL Horizons and the JPL Solar System Dynamics satellite tables.
- **Earth**: NASA Visible Earth (Blue Marble Next Generation, Black Marble, MODIS clouds) and GEBCO elevation.
- **Moon**: LRO WAC colour from the CGI Moon Kit and LRO LOLA relief. Credit: NASA's Scientific Visualization Studio.
- **Planets and moons**: USGS Astrogeology and PDS mosaics from Viking, MGS MOLA, MESSENGER, Magellan, Voyager, Galileo, Cassini and Dawn. Jupiter: Cassini PIA07782 (NASA/JPL/Space Science Institute). Saturn, Uranus, Neptune and the Uranian moons that Voyager 2 saw only in part are modelled from published imagery. Each map's source is in its `Planets/<Body>/Body.json`.
- **Shape models**: irregular moons and asteroids from NASA PDS Small Bodies Node and NAIF, decimated to three LODs with baked normal maps (sources in `Resources/Models/Bodies/LODS.json`). Shape model of Ryugu: ISAS/JAXA (modified); the game shows this credit on Ryugu's codex card. Bodies without a shape model, and the belt rocks, are procedural.
- **Rings**: radii and optical depths from the Voyager, Galileo, Cassini and New Horizons ring literature, rounded.
- **Sky**: NASA SVS Deep Star Maps 2020. Gaia DR2: ESA/Gaia/DPAC. Stars from the Yale Bright Star Catalogue (`bsc5.csv`).
- **Fonts**: Barlow and JetBrains Mono, under the SIL Open Font License (licence files in `Resources/UI/Fonts`).
- **Ships, sound and cockpit**: made for this project, in code or by its own Blender scripts. A new outside asset must be CC0 or public domain, and must be credited.

The source maps are public domain except where a credit is named above.

All rights reserved. See [`LICENSE`](../LICENSE). Third-party assets keep their own licences.
