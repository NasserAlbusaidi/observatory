# Ship building and combat: design

Date: 2026-10-06. Updated 2026-10-08. The four decisions are agreed (2026-10-06).
**Status:** W1–W4 are built (see [design-world.md](design-world.md)). Phase 1 is built (PRs 130, 131, 132). Phase 2 is next. Phases 2–6 are not built.
Code first read on branch `controls-rework`. "Built" notes read `main` at 77f72eb. Scope: career mode.

**The world came first.** Phases W1–W4 in [design-world.md](design-world.md) (factions, traffic, threat, pirate contacts) shipped before Phase 1 here.

**Start small.** Phase 1 is a civil shipyard with no weapons. Each later phase ships alone and is playable alone.

**Number tags**
- `[code]`: I read it in the code.
- `[design]`: I picked it. Tune it.
- `[calc]`: arithmetic from the other two.

## Pitch

- Combat is the trip planner with a threat in it.
- Every module, weapon and armor plate is mass. Mass costs fuel and g. The game's one trade, time vs mass, now covers the fight too.
- The torch drive stays the one piece of fiction. Torpedoes get it too.

## Phases

| # | Phase | The player gets | Weapons | Status |
|---|---|---|---|---|
| W1–W4 | The world ([design-world.md](design-world.md)) | Factions, NPC traffic, threat map, pirates that board you | None | Built |
| 1 | Shipyard | Modules on their own hull | None | Built (PRs 130–132) |
| 2 | Signatures | Detection ranges, quiet coasts | None | Next |
| 3 | Stealth | A stealth corvette hull, smuggling jobs | None | — |
| 4 | Point defense | PDCs, armor, damage | Defense only | — |
| 5 | Offense | Missiles, torpedoes, bounty and escort jobs | Yes | — |
| 6 | Hull ladder | A destroyer for the player, NPC capital ships | Railguns on capitals | — |

## Phase 1: the shipyard

### Built (PRs 130, 131, 132; merged 2026-10-08)

- **Thrust, not g** (PR 130). `Ship.AMax` is `min(MaxG·G0, Thrust/Mass)` when `Thrust > 0` (`Ship.cs:71`) [code]. `Thrust = 0` keeps the old rule, so pirates and QA ships do not change.
- **The build** (PR 131, `ShipBuild.cs`). One hull: "FWO-052 light frigate", 52 m, 228 t of structure, at most a 5 g-rated drive [code]. Five slots, one part each [code]:

| Slot | Parts (mass, price) | Refit |
|---|---|---|
| Drive | Economy 0.5 g (25 t, 10,000 cr) · **Standard 1 g (40 t, 30,000 cr)** · Fast 3 g (70 t, 70,000 cr) · Torch 5 g (100 t, 220,000 cr) | 3 days |
| Tank | 100 t · **200 t** · 300 t · 400 t, at 8 t per 100 t (10,000 to 90,000 cr) | 1 day |
| Hold | 50 t · **100 t** · 200 t (5 to 20 t; 10,000 to 60,000 cr) | 1 day |
| Cabin | None · 12 seats (1.5 t) · **40 seats** (4 t) | 0.5 day |
| Sensors | **Stock** (2 t, 10,000 cr) · Long-range (4 t, 37,000 cr): ×2 scan range, ½ scan time | 0.5 day |

  - A drive's g rating is its thrust at 500 t (`ShipBuild.Rated`). Bold is the stock build.
- **The stock build** is 300 t dry, 4.9 MN. It gives 1.00 g with a full tank, 1.67 g with an empty tank and 0.83 g full with 100 t aboard. Δv on a full tank is 5,108 km/s. Jet power at full burn is 24.5 TW [code, `CareerQA.Yard.cs:30-35`].
- **Refits** (`Career.Yard.cs`). The price is each new part less 70 % of the old part. The days add up. During the refit the drive is cold. A smaller tank sells the fuel it cannot hold. Every port refits. The yard is closed in the QA sandbox.
- **The yard page** (PR 132). It shows the parts and the readouts fitted → draft: dry mass, g full, g empty, Δv, jet power, and tank, hold and seats. It also shows a trip check (a full burn to Mars, or to Earth from Mars), the net price and days.
- **Save.** `CareerSave` keeps the build and the refit end. An old save loads as the stock build with its old upgrades (`ShipBuild.FromCounters`).
- **Different from the plan below:**
  - The hull has no internal and external slots yet. One hull only.
  - The Economy drive is new.
  - Trip estimates use the g at the start mass. No variable-mass trip yet.
  - The express window is not re-tuned (`ExpressG = 1`, 15 % margin). PR 140 measured the stock ship: 78.1 % of cargo jobs and 96.2 % of passenger jobs can make the window. Every miss is beyond 5 AU, on a tank-limited coast.
  - A 1 g raider now boards a loaded stock ship (0.83–0.95 g) on a full burn. A bigger drive is the answer (issue #90, accepted).
  - The classic-panel yard has no screenshot check yet (PR 132).
- **No Radiator module.** It waits for Phase 2 or 3.

### Before Phase 1 (code at the time)

- **Three upgrade counters:** `DriveLevel` (1, 3, 5 g), `TankLevel` (+100 t per step) and `Codex.SensorLevel` (0 or 1) (`Career.cs:69-102`, `Codex.cs:30-31`).
- **One seam:** each tick, `Career.Apply(ship)` pushes dry mass, cargo, the tank cap and MaxG onto the ship (`Career.cs:130-138`).
- **The hull is already a corvette-class light frigate:** 52 m (`PlayerShip.json`), 300 t dry, 200 t tank, 100 t hold [code] (`Career.cs:75`, `:98-99`). Its model has `PDC_0..5` markers that nothing uses yet. Phase 1 adds no hull.
- **All three drives weigh the same.** `DryMass` changes only with tank level (`Career.cs:99`). A 5 g drive costs credits and nothing else.
- **Audit #4:** upgrades only cut waits, nothing sinks money, and there is no long goal. Phase 1 fixes the first part.

### The model

- A `ShipBuild` is one hull and a list of modules. It replaces the three counters and feeds `Career.Apply`.
- The **hull** sets length, structure mass, internal and external slots, and the largest drive it takes.
- **Modules:**

| Module | Sets | Mass rule | Matters from |
|---|---|---|---|
| Drive | Thrust (MN) | Grows with thrust [design] | Phase 1 |
| Tank | Reaction mass capacity | 8 t per 100 t of tank [code] | Phase 1 |
| Hold | Cargo tonnes | Grows with capacity [design] | Phase 1 |
| Cabin | Passenger seats | 0.25 t per passenger loaded [code]; cabin structure [design] | Phase 1 |
| Sensors | Scan range and time; later, detection range | Small [design] | Phase 1 |
| Radiator | Heat rejection | Grows with area [design] | Phase 2 |

- Each module has a mass, a price and a refit time in days.
- **Refit time is a real cost.** Time has a price now (the express bonus, commit 3f19d97).
- **Sell-back** at 70 % of price [design].
- **Live readouts in the yard:**
  - Dry mass, g with a full tank, g with an empty tank, Δv on a full tank, jet power, price.
  - A trip check, for example "Earth → Mars, Fast: N days, M t". Reuse `Ship.TripFrom` (`Ship.cs:503`) and `Career.NeedTonnes` (`Career.Intercept.cs:22`).
- The **stock build** equals today's ship: 4.9 MN drive (1 g at 500 t) [calc], 200 t tank, 100 t hold, stock sensors.

### Builds that the board can ask for

With thrust fixed, the stock 4.9 MN drive gives [calc]:
- 1.00 g at 500 t: full tank, empty hold.
- 1.67 g at 300 t: empty tank, empty hold.
- 0.83 g at 600 t: full tank, 100 t of cargo.

So cargo now costs g, not only fuel. Builds then match job types:
- **Hauler:** big hold, big tank, small drive. Slow, cheap per tonne.
- **Courier:** big drive, small tank, small hold. Wins express bonuses.
- **Surveyor:** sensors and a big tank. Goes far and stays long.

This is fix (b) from audit #4: each purchase opens a kind of work, not only a shorter wait.

### Decision 1: thrust, not g

- **Today:** `Ship.AMax => MaxG * G0` (`Ship.cs:57`). Mass does not change g. Cargo and armor cost fuel only.
- **Problem:** with that rule, a builder is only a fuel calculator.
- **Recommendation:** g = thrust / mass.
- **What it touches:**
  - `NeedTonnes` and `Catchable` assume a constant acceleration (`Career.Intercept.cs:22`, `:104`). Use the start mass, which is conservative, or integrate.
  - Deadlines and pay use `RefG = 0.6` (`Career.cs:80`). Keep it as the reference. Jobs do not change.
  - The QA-041 rule "post only what a full tank can fly" (`Career.Intercept.cs:67`) must read the build.
  - The express window is a 1 g full burn plus 15 % (`ExpressG = 1`, commit 3f19d97 on `route-events`). A stock ship with 100 t aboard starts at 0.83 g, so its full burn takes about 9 % longer [calc]. That uses most of the margin. Re-tune it.
  - `PassengerG` already caps MaxG (`Career.cs:102`). It becomes a throttle cap. No change in behavior.
  - **g rises as fuel burns**, up to 1.67× on the stock tank [calc]. The flip point of a Fast trip moves later. The autopilot re-aims each step, so it copes. The planner estimates do not. They need the variable-mass trip.
  - **Save migration:** old `DriveLevel`, `TankLevel` and sensor level load as the stock build with the matching modules.

### Not in Phase 1

- No weapons, no new hulls, no hostiles.
- Modules do not change the hull model.

### QA for Phase 1

- The stock build matches today: 1 g at 500 t, 200 t tank, 100 t hold.
- Every job on the board can be flown by the current build (QA-041, now from the build).
- A refit charges credits and days. The save keeps the build.
- An old save loads as the stock build plus its upgrades.

## Phase 2: signatures

### No stealth under thrust

- Jet power P = ½·m·a·vₑ is already on the HUD (`OpenSystemScene.UI.cs:376-377`, the Data tab and the classic panel). The stock ship at 1 g makes 24.5 TW [calc].
- Detection range grows with √P (inverse square).
- **Baseline [design]:** stock sensors see a 2 MW cold hull at 10,000 km. Then [calc]:
  - The stock ship burning at 1 g is visible at 0.23 AU.
  - At 5 g it is visible at 0.52 AU.
- So the Fast/Economy choice gets a third axis: **the burn is loud and the coast is quiet.**
- The sensor module sets the baseline. Sensors get a job beyond scan time.

### Fights need matched velocity

- A 1 g Fast trip peaks at 856 km/s over 0.5 AU and 1,483 km/s over 1.5 AU [calc].
- Crossed courses close at more than 1,000 km/s. At that speed, a ship crosses a 5 km envelope in 5 ms. Nothing can aim.
- Only an attacker that matches velocity can fight. That is the intercept job.
- So every fight takes days to set up and shows on the map.

Pirates that board you moved to [design-world.md](design-world.md), phase W4.

## Phase 3: stealth

This needs signatures (Phase 2) only. It needs no weapons, so it fits the hauler before combat does.

- A stealth ship cannot hide a burn. It hides by coasting cold.
- **Heat sink: its own reaction mass.** Reaction mass is water (port notes, `Career.cs:56-67`). The hull dumps waste heat into the tank, then burns the warm water.
  - 200 t warmed by 100 K stores 84 GJ [calc]. At 2 MW idle [design], that is 11.6 h cold [calc].
  - With 50 t left, it is 2.9 h [calc].
  - So a full tank gives a long hide and a low tank gives a short one.
  - When the sink is full, the ship deploys its radiators and becomes visible.
- Stowed radiators and a faceted shape cut active-sensor range.
- **Jobs:** smuggling (deliver unseen), scouting, and later ambush.
- **Hull:** a stealth corvette, about 60 m [design]. It is a second yard hull, so the `ShipBuild` model must allow more than one hull.

## Phase 4: point defense and damage

### PDCs

- Mounts go on the `PDC_0..5` markers of `PlayerShip.json`. Each mount has an arc. Ship attitude sets which mounts bear.
- **Position spread:** r = ½·a·t², where t = range / round speed.
- With 2 km/s rounds and a torpedo that jinks at 20 g [design], the spread is [calc]:
  - 25 m at 1 km.
  - 98 m at 2 km.
  - 613 m at 5 km.
- So a PDC is a weapon for the last 2 km.
- **Do not simulate rounds.** Use a kill rate: λ = rounds/s × target area / (π·r²), and P_survive = exp(−∫λ dt).
- **Integrate each pass in closed form.** At 10 km/s closing, the last 5 km take 0.5 s. Per-frame samples miss passes. This is the same problem as issue #6 (`Rails.FirstCrossing`, `Rails.cs:84`).
- **The torpedo has a choice:** jink to survive the PDCs, or fly straight to hit.

### Damage: no hit points

A hit takes fuel, g or a system.

| Hit | Effect |
|---|---|
| Tank | Reaction mass leaks. The rocket equation does the rest. |
| Drive | Thrust drops. The planner says you are late. |
| PDC mount | That arc is open. |
| Sensors | Detection range drops. |
| Cabin | Passengers are hurt. The job fails. |

- **Armor is mass.** With Decision 1, it costs g and fuel on every trip.
- **Passengers cap the ship at 1 g** (`Career.cs:77`). A 5 g dodge with passengers aboard fails the job.

### Time warp

- The approach runs at warp: contacts, reach bubbles, plan changes.
- Drop to 1× only for the last minutes. `DropWarp` and `TransferInProgress` already do this for hand-overs (`Career.Intercept.cs:36-37`).
- The F8 flight recorder replays the fight.

## Phase 5: missiles and torpedoes

| Weapon | Role | Limit |
|---|---|---|
| PDC | Defense in the last 2 km | Position spread |
| Missiles | Cheap, short range, salvos, counter-missiles | Short burn |
| Torpedoes | Long range, guided, expensive | Burn endurance and price |

- **Fuel does not limit a torpedo.** At vₑ = 10,000 km/s (`Ship.cs:44`), 50 g for 10 minutes is 294 km/s, which uses 2.9 % of its mass [calc]. The limit is drive endurance (heat) [design].
- **Endurance sets a reach.** 50 g for 10 minutes reaches about 86,000 km against a 1 g ship and 79,000 km against a 5 g ship [calc]. Outside that bubble, running works. Inside it, only PDCs and counter-missiles work. Show the bubble on the map.
- **Evasion does not save you.** Proportional navigation needs about 3× the target's g. A 5 g ship cannot out-turn a 50 g torpedo. Evasion only uses up the torpedo's endurance.
- **Magazines are mass.** Six 2 t torpedoes [design] ride along on every trip and cost fuel each time.
- **Munitions are a money sink** (audit #4).
- **The Kzinti lesson:** your drive is your biggest weapon. A pursuer straight behind flies into a 24 TW jet. So attackers come in from the side.
- **Jobs:**
  - Bounty: catch a runner, kill its drive, dock, take the hold.
  - Escort: take one of the six `Fleet.json` hulls through a threat bubble.

## Phase 6: the hull ladder

Ordered by size. Your list put the destroyer first.

**Scaling rules [design]**
- Mass grows with length³, from the player hull (300 t at 52 m).
- Max g falls with 1/length: thrust grows with the drive's cross-section (length²) and mass with length³. The anchor is 5 g for the corvette at 500 t. That is today's top rating (`DriveG`, `Career.cs:69`) [code]. Under Decision 1, the anchor becomes 24.5 MN of thrust [calc]. A heavier drive then gives the loaded corvette a little less than 5 g, so treat this row as an upper bound.
- Wet mass is dry mass × 5/3, the same fuel fraction as today's ship.

**Results [calc]:** jet power grows with length², so big ships are loud. Railgun reach grows with length, so big ships are easy targets.

| Class | Length | Dry mass | Max g | Jet power at max g | Seen burning at | Railgun reach on it | Owner |
|---|---|---|---|---|---|---|---|
| Corvette (light frigate) | 52 m [code] | 300 t [code] | ≤ 5 | 123 TW | 0.52 AU | 51 km | Player (today) |
| Stealth corvette | ~60 m | — | — | — | Cold: Phase 3 | — | Player |
| Destroyer | 120 m | ~3,700 t | 2.2 | 650 TW | 1.2 AU | 120 km | Player, late |
| Cruiser | 250 m | ~33,000 t | 1.0 | 2.8 PW | 2.5 AU | 250 km | NPC |
| Battleship | 450 m | ~190,000 t | 0.58 | 9.2 PW | 4.5 AU | 450 km | NPC |
| Dreadnought | 800 m | ~1.1 Mt | 0.33 | 29 PW | 8 AU | 790 km | NPC |

- **"Seen burning at"** uses the Phase 2 baseline (2 MW at 10,000 km). You can see a dreadnought burn from Jupiter.
- **"Railgun reach on it"** is the range at which the spread of a 50 km/s round [design] equals half the target's length, with the target jinking at its max g. Rule of thumb: about 1 km of reach per metre of hull. Small ships are hard to hit at range. Capital ships are not.
- **Railguns** are a capital-ship weapon, spinal mount. The ship aims by turning.
- **Flip time:** today's ship turns 180° in about 5.6 s (`AngAccel` 1.5 rad/s², `MaxRate` 0.6 rad/s, `Ship.cs:20-21`) [calc]. Bigger hulls must turn more slowly [design].
- **Art:**
  - `Fleet.json` has six civil hulls, 32–175 m. Warship hulls are new art.
  - Use the same markers (`Nose`, `Engine`, `Dock`, `DockOut`). Add `PDC_n` (as on `PlayerShip`), and propose `Tube_n` and `Rail_n`. See `ART-CONTRACT.md`.
  - At 500 km a 52 m ship is smaller than a pixel (audit #6). The player reads a fight through drive plumes, PDC tracers and markers.

## Code impact

| Area | Before | Change | Phase | Status |
|---|---|---|---|---|
| `Career.cs:69-102` (then) | Three upgrade counters | `ShipBuild` (hull and modules) | 1 | Built: `ShipBuild.cs` |
| `Codex.cs:30-31` | `SensorLevel` 0 or 1 | Sensor module | 1 | Built: set from the build (`Career.cs:172`) |
| `Ship.cs:57` (then) | `AMax = MaxG · G0` | Thrust / mass | 1 | Built: `Ship.cs:71` |
| Save | Counters | Build record and migration | 1 | Built |
| `Ship.cs:34` | `Target` is a `Body` | A stand-in `Body` for the player | W4 | Built: `Pursuit.cs` (no `Ship.Target` change) |
| `Career.Intercept.cs:77` | Scripted runner paths | Guided hostile `Ship` | W4 | Built: `Plan.Pursue`, `Career.Pirates.cs` |
| `PlayerShip.json` | `PDC_0..5` unused | PDC mounts | 4 | — |
| `Ship.cs:19-21`, `:44` | `RcsAccel`, `AngAccel`, `MaxRate`, `Ve` are `const` | Per instance (hulls, torpedoes) | 5–6 | — |

## Decisions (agreed 2026-10-06)

1. **Thrust, not g.** Without it, the builder is a fuel calculator.
2. **Who owns the big hulls.** There is no crew system. The player ladder stops at the destroyer. Cruisers and larger are NPC navies that you see and route around.
3. **Where stealth goes.** Right after Phase 2, as in the table. It needs signatures only.
4. **Where the yards are.** Every port refits. Only big yards sell hulls: Earth, Mars, Ceres [design]. Phase 1 has one hull, so the hull rule waits for the second hull (Phase 3).

Phase 1 shipped after W4 of [design-world.md](design-world.md). Phase 2 is next.
