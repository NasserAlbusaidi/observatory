# 0009. Thrust, not g: every part is mass

- Status: Accepted
- Date: 2026-10-06 (combat Decision 1)
- Source: [design-combat.md, "Decision 1: thrust, not g"](../design-combat.md#decision-1-thrust-not-g) and "Decisions (agreed 2026-10-06)"

## Decision

- A ship's acceleration is its thrust over its mass, capped by the g the player sets.
- Every module, weapon, armor plate, cargo and tonne of water is mass. Mass costs g and reaction mass on every trip.
- The game's one trade is time against mass. Every new feature (combat included) is priced in that trade.

## Why

- With a fixed g, a ship builder is only a fuel calculator. With thrust over mass, every part changes how the ship flies.

## Consequences

- g rises as water burns. The planner estimates must use the variable-mass trip.
- A new part states its mass. Its cost in g and fuel shows in the yard before the player buys it.
- Weapons are mass too: a torpedo or a PDC mount slows the ship that carries it.

## Where

- `OpenWorld/Ship.cs`: `AMax` (`Math.Min(MaxG * G0, Thrust / Mass)`).
- `OpenWorld/ShipBuild.cs`: the parts and their masses.

Read at ef016bf (main).

Note: `docs/MODEL-FIDELITY.md`, "Acceleration regulation", still says the drive has "sufficient thrust reserve" for any mass. That text predates this decision and conflicts with `AMax`.
