# 0004. Combat draws from its own random stream

- Status: Accepted
- Date: Phase 4 decisions (D10)
- Source: [design-combat.md, Phase 4 "Rng"](../design-combat.md#rng), D10

## Decision

- Combat draws only from `combatRng`, its own `System.Random`.
- The route draws (`routeRng`) and the board draws (seeded by the dock and the day) must not move because of combat.
- A new combat rule that needs no chance takes no draw. A new rule that needs one draws from `combatRng`, in a fixed order.

## Why

- QA counts the route draws (check 5c). The jobs board must be the same for the same dock and day.
- A combat change that shifted those streams would change jobs and route events far from the fight, and break seeded checks for no reason.

## Consequences

- Seeded combat checks call `Career.SeedCombat(seed)`.
- A change to the draw order inside `Strike` changes every seeded combat check. Keep the order, or update the checks in the same PR and say so.
- A rule with no draw (for example the momentum kick) does not move any seed.

## Where

- `OpenWorld/Career.Damage.cs`: `combatRng`, `SeedCombat`, `Strike`.
- `OpenWorld/Career.Route.cs`: `routeRng`.

Read at ef016bf (main).
