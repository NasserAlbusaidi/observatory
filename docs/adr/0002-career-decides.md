# 0002. The career decides every outcome; the scene only draws it

- Status: Accepted
- Date: 2026-10-10 (the rule holds since combat Phase 4)
- Source: [design-combat.md, Phase 4 "Time warp"](../design-combat.md#time-warp), D11

## Decision

- `Career` decides every result: a kill, a hit, a boarding, a break-off, a job's end. The scene never decides one.
- The scene draws what the career said. It listens to `Career.OnEvent` and reads career state. It does not roll, and it does not change a result.
- A visual that needs a fact (a side, a mount, a position) reads it from the career or from the same shared rule the career used (see 0005).

## Why

- The career runs headless in QA (`-fowcareer`) and at any warp. A result that came from the scene would change with the frame rate, the warp and the shell.
- Two sessions build in parallel: one on combat rules, one on effects. This rule lets them work apart.

## Consequences

- A new effect needs no change in `Career`. A new outcome needs a `Happening` and a note first; the effect then draws it.
- An effect can be dropped, slow or off, and the game result is the same.
- Effects may add their own visual-only rules (for example, kill debris that meets the hull draws no hit). They never feed back into the career.

## Where

- `OpenWorld/Career.cs`: `OnEvent`, `Say`.
- `Scenes/OpenSystemScene.Career.cs`: `career.OnEvent += MissileEvent`.
- `Scenes/OpenSystemScene.Missiles.cs`: the flares, tracers, hit and kill flashes.

Read at ef016bf (main).
