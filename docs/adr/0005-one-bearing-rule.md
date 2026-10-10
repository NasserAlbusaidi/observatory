# 0005. One bearing rule for the rows, the cards and the tracers

- Status: Accepted
- Date: 2026-10-10 (the rule holds since Phase 4b)
- Source: [design-combat.md, Phase 4 "PDCs"](../design-combat.md#pdcs), D3; Phase 5 point 9

## Decision

- A PDC mount bears on a missile when `PointDefense.Bears` says so, and only working mounts count. The hull blocks nothing.
- Every surface uses this one rule: the kill draw, the Missiles row, the after-action card ("N mounts bore") and the tracers in the scene.
- The aspect of a missile (nose, side, tail) also has one rule: `Career.AspectOf`. The Threat card, the hit table and the after-action card use it.
- The hit flash shows at the hull in the direction the missile was last drawn (`missileLast`), so it is on the same side as the card's aspect.

## Why

- If the screen shows tracers from four mounts and the card says five, the player cannot trust either.
- One rule means one change point. A future rule (for example, hull blocking) changes everything at once.

## Consequences

- An effect must not add its own arc, range test or hull blocking. If a new rule is needed, change `PointDefense` and the draw, in one PR, with QA.
- The tracers show only inside the 5 km envelope, where the draw counts.

## Where

- `OpenWorld/PointDefense.cs`: `Bears`, `Bearing`, `ArcDeg`, `EnvelopeKm`.
- `OpenWorld/Career.Damage.cs`: `Strike` (`Bearing`, `AspectOf`).
- `OpenWorld/Career.Report.cs`: `SalvoLine`.
- `Scenes/OpenSystemScene.Missiles.cs`: `LateTracers`.

Read at ef016bf (main).
