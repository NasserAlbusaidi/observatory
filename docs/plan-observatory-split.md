# The Observatory split: plan

Date: 2026-10-10. Status: proposed. Nothing is moved yet.
Decision: [ADR 0001](adr/0001-open-world-game.md). The game is the open world. The eight Observatory scenes move to a separate repo. The name is decided later.
Code read on main at ef016bf. Cut points re-checked on fed0783: no change under `Assets/`. Paths are under `Assets/FogOfWar/` unless shown.

## Size

| | `.cs` files | Lines |
|---|---|---|
| Observatory runtime | 16 | 3,689 |
| Observatory tooling | 4 | 1,146 |
| Game runtime | 84 | 17,162 |
| Game tooling | 36 | 9,609 |
| Shared runtime | 17 | 3,519 |
| Shared tooling | 10 | 2,277 |

The Observatory is about 13 % of the code. The game does not need most of the shared code to change.

## What moves

- **Scenes (16 files):** `Scripts/Scenes/` `WormholeScene`, `BlackHoleScene` and its partials (`.Hud`, `.Lens`, `.Probes`, `.Sandbox`, `.SandboxUI`), `Kerr`, `KerrGeo`, `BHPost`, `WarpScene`, `JourneyScene`, `NegativeMassScene`, `TidesScene`, `MarsScene`, `MeteorScene`.
- **Shaders:** `Resources/Shaders/` `BlackHole`, `BlackHoleCore.cginc`, `BlackHoleLens`, `BHPost`, `Wormhole`, `WarpSurface`, `CMB`, `Mars`, `Moon`, `Ground`; `Editor/BlackHoleBenchmark.shader`.
- **Assets:** `Resources/Sky/SkyOurs.png`, `SkyOther.png`; `Planets/Mars/Height.png`, `Height.json`; `Resources/Models/` `WarpShip`, `Rubble`, `Antenna`, `RadioTower`.
- **Tooling:** `Capture/BlackHoleShots.cs` (`-fowbhshots`), `Capture/BlackHoleClip.cs` (`-fowbhclip`), `Editor/FogOfWarBhBuild`, `Editor/FogOfWarGpuCheck`, `Tools/bh-build-shots.sh`, `Tools/blackhole/`, and in `Tools/benchmarks/` `ray_reference.py`, `rays.json`, `compare_images.py`.
- **UI:** the "Observatory scenes" menu, the scene rail, the 1–8 scene keys and the "Ray-trace quality" setting (`FogOfWarApp.cs`, `UI/UiShell.Menus.cs`, `Fow.uss` `.rail*`).

## What both keep (copied, then free to change apart)

`FogOfWarApp`, `SimScene`, `Bootstrap`, `Util` (`Gfx`, `Fmt`, `Phys`), `Art`, `Models`, `OrbitCamera`, `PanelUI`, `UI/` (shell, kit, settings), `Capture/` (photo mode, clips). Shaders: `FOWCommon.cginc`, `Unlit`, `SimpleLit`, `Stars`, `SkyMap`, `Earth`, `PhotoFX`, `PhotoDepth`. Assets: `Sky/MilkyWay.png`, `Stars/bsc5.csv`, `Planets/Earth/*`, Moon and Mars colour maps, `TorchShip.fbx`, fonts and styles.

## Cut points: fix these first

1. **The game uses `BlackHoleScene.PlanckColor`.** `OpenWorld/Blackbody.cs:13` calls it for the hit flash and the sparks (`Sparks.cs`, `OpenSystemScene.Missiles.cs`). Move `PlanckColor` and its helper (`BlackHoleScene.cs:206-230`) into `OpenWorld/Blackbody.cs`. `BlackHoleScene` then calls the new place.
2. **The Observatory reads game code.** `BHPost.cs:13` reads `OpenWorld.Bloom.Disabled`. `FogOfWarApp.cs:87` calls `OpenWorld.ShipLighting.ResetGlobals()`, which sets `_FOWExposure` and `_FOWStarGlare`; `SimpleLit`, `Earth`, `Stars` and `SkyMap` read them. The new repo needs a small replacement that sets both globals, or its emission and night lights go to 0.
3. **The scene list.** `FogOfWarApp.cs:90-94` builds one array, and `GameIndex` assumes the open world is last.
4. **The sky textures.** `FogOfWarApp.cs:88` `Art.BindSky()` loads `SkyOurs` and `SkyOther` at every launch. No game shader reads them.
5. **The product name.** `Editor/FogOfWarBuild.cs:44` sets `productName = "Fog of War Observatory"`. The saves and settings live under it (`HKCU\Software\Nasser\Fog of War Observatory`). The Observatory repo must take a different product name, or the two programs share one settings store.

## Order

Each step is its own PR, gated as `CLAUDE.md` says.

**S0. Game repo: move `PlanckColor`** (cut point 1). Small. No behaviour change. Suites: `career` and `qa`.

**S1. New repo: the Observatory alone.**
- Nasser creates the repo: `NasserAlbusaidi/observatory`, public (see "Decided").
- Start from a clone of this repo with full history, so the Observatory files keep their history and the GPL-3.0 licence. Then delete the game-only code in one commit.
- Fix cut points 2 and 4 there. Give it its own product name (cut point 5).
- Its QA: the Observatory part of `-fowqa` and `-fowtour`, `-fowbhshots`, the `gpu` suite, and the relativity checks of `FogOfWarPhysicsCheck`.
- Done when it builds, boots into its scene list, and its suites pass.

**S2. Game repo: remove the Observatory.** One PR, because the runtime and the QA that visits it must go together.
- Delete the files in "What moves". Remove the menu, the rail, the 1–8 keys, the quality setting and `Art.BindSky()`.
- Trim the shared code: `Models.cs:76-88` fallbacks, `Util.ValueNoise`, the Observatory-only parts of `FOWCommon.cginc` (`skyA`, `skyB`, `skyAGrad`, `planckRGB`, `primaryRay`).
- Trim the tooling: the Observatory tables in `QA.cs` (lines 160-210, 291), the scene loop in `FidelityProbe.cs`, the Observatory shots in `Capture/UiShots.cs`, `Relativity()` and `Journey()` in `FogOfWarPhysicsCheck`, and `Tour.cs` (game part only, or remove).
- Update `README.md`, `docs/DEVELOPING.md` and `docs/MODEL-FIDELITY.md`.
- Suites: all four, plus `physics` and `regression`.
- Starts only after S1 builds, so nothing is lost.

**S3. Later: the name.** A rename changes the product name, so it needs a save migration: on first run, copy the old registry keys into the new product's keys. Unity's `PlayerPrefs` cannot read another product's keys, so this reads the registry directly. The GitHub repo rename can come in the same step (GitHub keeps a redirect).

## Risks

- **Lost saves.** Any change to the product name before S3's migration moves the saves out of reach. S1 changes only the new repo's name.
- **Missed asset references.** Assets are loaded by string (`Gfx.Mat(name)`, `Art.Tex`, `Resources.Load`), so the compiler does not catch a missing one. S2's suites and a manual boot catch it.
- **Two copies of the shared code.** Fixes in one copy do not reach the other. Accept this; the Observatory changes little.

## Decided (Nasser, 2026-10-10)

1. The new repo: `NasserAlbusaidi/observatory`, public. This repo is public, under GPL-3.0.
2. The Observatory program's product name: `Observatory` (cut point 5). Its settings go under `HKCU\Software\Nasser\Observatory`.
3. S0: the "observatory-split" session.

## Open (Nasser)

1. When to create the repo (S1). It needs his approval at that time.
