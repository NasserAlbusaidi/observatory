# 0001. The game is the open world; the Observatory scenes move to their own repo

- Status: Accepted
- Date: 2026-10-10
- Source: Nasser's decision, 2026-10-10. Plan: [plan-observatory-split.md](../plan-observatory-split.md)

## Decision

- The game is an open-world, solar-system-wide career game in the style of *The Expanse*. The open world is the product.
- The eight Observatory scenes leave the game and move to a separate repo and Unity project: wormhole, black hole, warp bubble, 1 g journey, negative mass, tides, Mars terraforming and radio meteors.
- The true-scale physics stays in the game. The ephemeris, the Newtonian flight, the trip planner and the rocket equation are the game, not the part that leaves.
- The name is decided later, in its own change.

## Why

- The career has been the product since the world track (W1–W4). Every phase since builds on it.
- The Observatory is a different thing: physics demonstrations with no player goal. In one exe, it adds menu items, settings, shaders and QA suites that the game does not use.

## Consequences

- The game boots into the open world with no "Observatory scenes" menu. `FogOfWarApp` keeps only `OpenSystemScene`.
- Code used by both (the app shell, the camera rig, photo mode and capture, settings) is copied to the new repo. After the split, the two copies can change apart.
- QA splits with the code. `-fowqa` and `-fowtour` visit every scene; `-fowbhshots` and the `gpu` suite are Observatory only. Each repo keeps the suites for its own code.
- The "Ray-trace quality" setting goes with the Observatory.
- A rename is its own change. The save and the settings live under the product name (`HKCU\Software\Nasser\Fog of War Observatory`, see `docs/DEVELOPING.md`). A rename needs a save migration, or players lose their saves.
- The Observatory program takes a different product name, so the two programs do not share one settings store.
- The game uses one Observatory function today (`BlackHoleScene.PlanckColor`, for the hit flash). It moves into the game before the split.
- Black-hole and Observatory work stops in this repo when the split starts. No such branch was open on 2026-10-10.
- The planned Sandbox and Battle modes (`docs/design-modes.md`) stay in the game. They are open-world modes, not Observatory scenes.

## Where

- `Assets/FogOfWar/Scripts/FogOfWarApp.cs`: the scene list and the "Observatory scenes" menu.
- `Assets/FogOfWar/Scripts/Scenes/`: `WormholeScene`, `BlackHoleScene` (6 files), `Kerr`, `KerrGeo`, `BHPost`, `WarpScene`, `JourneyScene`, `NegativeMassScene`, `TidesScene`, `MarsScene`, `MeteorScene`.

Read at ef016bf (main).
