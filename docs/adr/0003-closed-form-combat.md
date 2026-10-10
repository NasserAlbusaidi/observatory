# 0003. Combat resolves in closed form at any warp: one draw a missile, at impact

- Status: Accepted
- Date: 2026-10-09 (D2 and D3 as changed after play)
- Source: [design-combat.md, Phase 4 "PDCs" and "Time warp"](../design-combat.md#pdcs), D2, D3, D11

## Decision

- A missile's flight is a closed form. Each update steps it exactly over the update. The impact lands on the update that holds the closed-form impact time.
- Rounds are not simulated. The PDC pass is an integral, solved once, at impact.
- Each missile gets one kill draw, at impact. It uses the mounts that bear at impact and the missile's real closing speed.
- The warp never changes a result. A 1× frame and a 1,000× frame give the same impact time and the same draw.

## Why

- A step can be longer than the whole 5 km PDC envelope (the substep floor is 0.5 s; at 10 km/s that is 5 km). Sampling per step would make the result depend on the warp.
- One draw keeps QA seeds stable and the after-action card exact.

## Consequences

- The attitude at impact counts for the whole pass. The pass takes about 0.5 s at 10 km/s, so this is close to the truth.
- Effects may show tracers before impact, but the kill is known only at the impact update (0002).
- New weapons (torpedoes, player missiles) follow the same pattern: a closed-form flight and one draw at impact. Their areas and masses become per weapon.

## Where

- `OpenWorld/PointDefense.cs`: `Exponent`, `KillChance`, `Kills`.
- `OpenWorld/Career.Damage.cs`: `Missile`, `Volley`, `Strike`.

Read at ef016bf (main).
