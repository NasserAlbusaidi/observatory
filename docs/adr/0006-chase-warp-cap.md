# 0006. In a chase, the warp follows the time to the next event

- Status: Accepted
- Date: 2026-10-10
- Source: [design-combat.md, Phase 5 "Encounter flow"](../design-combat.md#encounter-flow-added-2026-10-09), point 1; Phase 4 D11

## Decision

- While a chase is on, the warp cap is the time to the soonest event divided by `EventS`, between 1× and `ChaseWarp`. Events: the next impact, the raider in missile range, the raider alongside, a boarding, and the player's flip and arrival under a plan.
- The raider's times are lower bounds. The warp can slow early; it never runs past an event.
- The last `ImpactHoldS` before each impact run at 1×. Every PDC pass and every hit is seen at 1×.
- The frame runs at the cap, not at the warp step below it. When the cap rises again, the warp the player had comes back.
- A raider event that comes a full warp step sooner, or a launch, shows a Threat card and drops the warp to 1×.

## Why

- Each event arrives about `EventS` of real time after it first limits the warp. The player can see it coming and act.
- Running at the warp step below the cap made one salvo take more than twice as long in real time.

## Consequences

- New events (torpedo impact, an escort, a tow) join the same list. They do not add their own warp rules.
- Effects can rely on 1× at every impact (0002, 0003).

## Where

- `OpenWorld/Career.Pirates.cs`: `ChaseWarp`, `EventS`, `ImpactHoldS`, `WarpCap`.
- `OpenWorld/Career.Threat.cs`: the Threat card.
- `Scenes/OpenSystemScene.Pirates.cs`: `CapWarp`, `FrameRate`.

Read at ef016bf (main).
