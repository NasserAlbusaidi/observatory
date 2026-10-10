# 0008. A chase is not saved

- Status: Accepted
- Date: combat Phase 2 (D8)
- Source: [design-combat.md, Phase 4 "Save"](../design-combat.md#save-1); Phase 2 "A load starts tracked"

## Decision

- The `Encounter` is not saved: the raider, its track memory, the missiles in flight, the salvo record and the cards of the chase.
- A load during a chase ends it. The note says the chase was not saved. No after-action card shows.
- Lasting results are saved: damage (`CareerSave.damage`), water, cargo, credits and jobs.
- A load starts the ship as tracked (`LastLoudJd = jd`), so a reload is not a free escape.

## Why

- A chase is short and full of state that changes each update. Saving it would need a save format for every new weapon and card.
- The lasting cost of a fight is the damage and the lost cargo or job, and those are saved.

## Consequences

- New chase state (torpedoes, an escort, a tow race) goes on the `Encounter`, and is not saved either, unless an ADR says otherwise.
- A new lasting result needs a save field. A missing field reads as no damage (the `sinkK` pattern).

## Where

- `OpenWorld/Career.Pirates.cs`: `Encounter`.
- `OpenWorld/Career.cs`: the save and load.
- `CareerQA.Pirates.cs`: the "chase was not saved" checks.

Read at ef016bf (main).
