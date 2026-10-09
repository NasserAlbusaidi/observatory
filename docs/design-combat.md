# Ship building and combat: design

Date: 2026-10-06. Updated 2026-10-08. The four decisions are agreed (2026-10-06).
**Status:** W1–W4 are built (see [design-world.md](design-world.md)). Phase 1 is built (PRs 130, 131, 132). Phase 2 is built: 2a (#145) and 2b (#147) are merged. 2c is optional and not built. Phase 3 is designed below. PR 3a (#148, `stealth-sink`) and PR 3b (#149, `stealth-lost`) are built. PR 3c (`stealth-scout`) is built in this PR. PRs 3d–3f are not built. Phases 4–6 are not built.
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
| 2 | Signatures | Detection ranges, quiet coasts | None | Built (2a #145, 2b #147); 2c optional, not built |
| 3 | Stealth | Stowable radiators and a water heat sink, losing a raider, quiet scouting and contraband jobs, then a stealth corvette | None | Designed; 3a (#148) and 3b (#149) built, 3c built in this PR, 3d–3f not built |
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
- **Radiators:** Phase 3 (PR 3a) (Phase 2, decision 14).

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
| Radiator | Heat rejection | Grows with area [design] | Phase 3 (moved from 2) |

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

Code read on `main` at 77f72eb. Paths are under `Assets/FogOfWar/Scripts/`. Status: PR 2a is merged (#145). PR 2b is merged (#147). PR 2c is optional and not built.

### The rule

- **The burn is loud and the coast is quiet.** A raider sees a ship while its jet is loud. After that, it keeps the track for 2 h. A ship that has been cold for 2 h is not seen.
- **Raiders see the ship, not the autopilot.** A hand-flown burn is raided like a planned one. This replaces the "autopilot off = never raided" gate (`Career.Route.cs:43`, #90 comment 1, `design-world.md:347`).
- The √P range law is real. It decides what the player sees (PR 2b), and the HUD and the yard show it. **It does not grade raider risk.** Every burn that matters is seen across the whole raider window (below).

### The model

| Item | Rule | Value | Tag |
|---|---|---|---|
| Jet power | P = ½·m·a·vₑ, with a = applied main drive + applied RCS | stock full burn: ½·F·vₑ = **24.52 TW** at any mass | formula [code] `UI.cs:377`, `ShipBuild.cs:119`; RCS term [design] |
| Idle | 2 MW, every hull | | [design] (doc baseline) |
| Range | R = R₀·√((P + 2 MW) / 2 MW) | R₀ = 10,000 km stock, 20,000 km long-range | [design] |
| Raider sensors | stock | R₀ = 10,000 km | [design] |
| Raider window | a·CatchMinS²/4 to a·CatchMaxS²/4 from relative rest (`Spawn`, `Career.Pirates.cs:150-166`; `:78`) | 127,094 to **1,143,848 km** | [calc] |
| Loud | R(P) ≥ window edge, so P ≥ 2 MW·((1,143,848/10,000)² − 1) | **26.17 GW** = 0.00107 g at 500 t | [calc] |
| Track hold | `TrackHoldS = Threat.BoardingSeconds` | **2 h** | [design] |

- **Why binary.** Spawn never puts a raider inside 127,094 km of a ship at relative rest. A cold hull is seen only to 10,000 km, which is 12.7 times too short [calc]. Any real burn is seen across the whole window: the weakest drive, Economy, makes 12.26 TW, which is 468 × the loud line [calc]. So "loud or not" is the whole raider question.
- **Moving ships.** A fast ship can draw a start up to a·CatchMaxS² = 4.58e6 km away. That needs 419 GW [calc]. The choice only moves the line for trims between 0.001 g and 0.017 g. Decision 2.
- **RCS counts.** RCS uses the same vₑ (`MODEL-FIDELITY.md:30`), and `dv` already includes `rcs.Mag` (`Ship.cs:160`) [code].
  - Full pilot RCS (0.1 g, 500 t) is 2.45 TW: loud.
  - An autopilot `HoldStation` trim at Vesta's scan range is GM/r² = 17.29/1,576² = 7.1e-4 g, which is 17.4 GW: quiet. At Ceres it is 8.0e-4 g, 19.7 GW: quiet [calc].
  - A ship in free orbit gets no command ("in orbit", `Ship.cs:459`), so it is cold [code].
- **Why a track hold.** A cold ship is ballistic, so a raider that saw the burn can predict where it goes. One constant keeps the danger at arrivals and survey dwells (`design-world.md:81-82`) for a sensor reason. Without the hold, a survey dwell in free orbit would draw no raiders at all.
- **Light delay: omitted.** 0.234 AU is 117 s of light time. The raider window edge is 3.8 s. A freighter at 0.72 AU is 359 s. At 300 km/s and 1 AU the position error is 1.5e5 km, about 1 px in the flight view [calc]. Record this as a labelled line in `MODEL-FIDELITY.md`.

**Ranges, stock sensors** [calc] (long-range doubles each row):

| Source | Jet power | Seen at | Long-range |
|---|---|---|---|
| Cold hull | 2 MW idle | 10,000 km | 20,000 km |
| HoldStation trim, Vesta | 17.4 GW | 932,752 km (quiet) | — |
| Full RCS, 500 t | 2.45 TW | 0.074 AU | 0.148 AU |
| Economy 0.5 g | 12.26 TW | 0.165 AU | 0.331 AU |
| **Standard 1 g (stock)** | **24.52 TW** | **0.234 AU** | 0.468 AU |
| Fast 3 g | 73.55 TW | 0.405 AU | 0.811 AU |
| Torch 5 g | 122.6 TW | **0.523 AU** | 1.047 AU |

The stock and Torch rows match the doc's 0.23 and 0.52 AU.

### Raiders: the live roll (PR 2a)

- **Seen seconds per update.** The ship counts its loud seconds (`LoudSeconds`) and the date of its last loud moment (`LastLoudJd`). This happens in `Ship.Step`, beside `BurnSeconds` (`Ship.cs:174`).
  - A frame at 1M× is 4.63 h [calc]. A burn can start and end inside one frame, so a sample of `Throttle` at the end of the frame is not enough.
  - Rails coasts at 1k× and above take no `Step` (`OpenSystemScene.cs:469`). They add no loud time. That is correct, because a rails coast is cold.
- `seen = min(simDt, loud + tail)`.
  - `loud` is the loud seconds in this update.
  - `tail` is the part of this update after the last loud moment, up to `LastLoudJd + 2 h`.
- **Chance per update:** `1 − exp(−PirateRate · seen / 3600)`. `PirateRate` (`Career.Pirates.cs:119-126`) does not change. It is now "calls per hour while seen". `PirateRate` is skipped when `seen = 0`. That removes the per-frame `HavenBodies` allocation in cold flight (`Threat.cs:39,53`).
- **The gate.**
  - `!ship.PlanActive` leaves `Career.Route.cs:43` for the raider roll only.
  - Distress and cargo trouble keep it, because they ride the plan (`Career.Route.cs:5-9`).
  - The raider draw is made even when `seen = 0`, so the draw count stays as before (`CareerQA.EventsAndSaves.cs:115`, `Draws >= 2`).
- **Raidable job (`RaidJob`).**
  1. The non-tutorial cargo, passenger or survey job whose target is `ship.Target`.
  2. If none, the first cargo or passenger job: goods or people are aboard.
  3. If none, a survey job whose body is within the scan range, where the dwell accrues (`Career.cs:408`).
  - A survey job far from its body, with nothing aboard and no target match, draws nothing.
  - This closes both dodges in #90 comment 1: cutting the autopilot, and clearing the target.
- **A load starts tracked.** When `spanOk` is false (a load or a date jump, `Career.cs:364`), the ship is set to `LastLoudJd = jd`. A chase is not saved (D8), and a reload must not be a free escape.
- **The chase does not change.** `Spawn`, `Contacts`, `CatchNow` and `Hopeless` get no detection term. A raider that saw the burn keeps the vector. The W4 results stay: a coasting ship is boarded at 3.8 h, and a 1 g full burn escapes [code, PR 137 QA].
- **The call card says why.** A sentence goes after the first one (`Career.Pirates.cs:191`):
  - "It saw your burn from 412,000 km."
  - Or, when the call comes from the track: "It has tracked you since your last burn, 1 h ago."

### Raiders: the planner predicts it (PR 2a)

`Threat.Of` (`Threat.cs:80-127`) keeps its signature. Three lines change:

| Phase | W3 today | Phase 2 |
|---|---|---|
| Held at the start (`:119`) | counted, 1.5 h | **dropped**: no burn, no track |
| Speeding up (`:120`) | counted | counted: loud |
| Slow coast (`:121`) | whole coast | **first 2 h only**: the departure burn's track |
| Braking (`:122`) | counted | counted: loud |
| Held at the target (`:125`) | 1.5 h | 1.5 h at a body with no port; **dropped** at a port (#162, decision 9) |

Exposed hours (presence, patrol reach and e^(−v/70.6 km/s) unchanged). Chance at 0.2 calls/h [calc]:

| Case | W3 today | Phase 2 |
|---|---|---|
| Mars orbit → Vesta, 1 g | 3.50 h, high (50 %) | 3.50 h, high (50 %); **2.00 h, moderate** since #162 |
| Mars orbit → Vesta, 3 g | 2.17 h | 2.17 h; **0.67 h** since #162 |
| Mars orbit → Ceres (2 g patrol) | 0.75 h | 0.75 h |
| Earth → Luna | 0 | 0 |
| Vesta → Mars orbit, 1 g | 3.50 h (1.5 h hold + 2.0 h burn) | **2.00 h** |
| Belt hop, 1.5e7 km, 1 g, full burn | 6.98 h | **5.48 h** |
| Same, cruise ½ | 7.81 h | **5.37 h** |
| Same, cruise ¼ | 16.45 h | **4.99 h** |
| Survey dwell after the braking burn, 2 h | 33.0 % | 33.0 % |
| Hand-flown trip, any | 0 % (gate) | as the planned trip |

- Every W3 check at `CareerQA.Threat.cs:31-34` stays as it is. The "dock first" clause (`Threat.cs:134`) stays. #162 later dropped the port hold (decision 9), which moves the two Mars orbit → Vesta numbers.
- **The third axis is real for cruise.**
  - Today a ¼ cruise counts 2.4× the risk of a full burn.
  - In Phase 2 it is the quietest of the three. The long coast goes cold 2 h after the departure burn.
  - At full thrust the drive choice moves only the time spent burning: Δv / a.
- **Port hold dropped (#162).** `FindDock` (`Career.cs:253-270`) docks a ship as soon as it is inside `DockRange`, bound, with the drive off, so the 1.5 h hold at a port was never live exposure. `Threat.Of` now counts the hold only at a body with no port, such as a survey target. See decision 9.
- **The line** (`Threat.cs:135`) reads: "Raider risk: moderate. 2.0 h slow and seen (a burn, or 2 h after one), out of patrol reach in raider space, most of it on arrival: dock first and a raider can't board."

### What the player sees and does not see

| Contact | PR 2a | PR 2b |
|---|---|---|
| Planets, moons, ports | always | always |
| Traffic ship | always (as today) | only while R(its P) × sensor factor ≥ distance from the ship |
| Runner (Intercept target) | always | always: the board gave its course |
| Derelict (Rescue target) | always | always: distress beacon |
| Raider in a chase | always | always: the call is the contact; its 24.5 TW burn is seen to 0.23 AU |
| Your own ship | Signature row, LOUD/TRACKED pill, yard row | + a map ring at R(own P) |

- **Hidden means absent.** A hidden contact has no dot, no tag, no map mark and no ghost.
  - A ghost needs track memory, which is new state. That is Phase 3.
  - A hidden body stays in `sys.Bodies`. So a target on it is kept (`OpenSystemScene.Career.cs:251` releases only a body that left the list).
- **Target cycle.** Tab (`Controls.cs:49`) walks `targets`. That list is built once, before traffic exists (`OpenSystemScene.cs:69`). So traffic is never in it [code], and nothing changes in 2a or 2b. Optional PR 2c adds seen traffic.

### UI in both shells

| Surface | UI Toolkit | Classic panel and `-fowlegacyui` | PR |
|---|---|---|---|
| Signature row (replaces the exhaust row, `UI.cs:376-377`) | Data tab via `Readouts()` (`Hud.cs:556`) | side panel via `Readouts()` (`FogOfWarApp.cs:163-164`) | 2a |
| Raiders row: "can see your burn" / "tracking you, 1 h 24 min left" / "no track" | same `Readouts()` | same | 2a |
| Pill `LOUD · SEEN AT 0.234 AU` / `TRACKED 1:24` | new `pSeen` beside `pRaider` (`Hud.cs:124,624`) | no pill system; the Raiders row carries the state (`Tone.Bad`) | 2a |
| Yard row "Seen at, full burn · cold" | `YardReadouts` (`Yard.cs:34-45`) | same function | 2a |
| Planner line | `Pages.cs:150` | `OpenSystemScene.Career.cs:411` | 2a (text only) |
| Call card sentence | call card | `CallPanel` (both draw `Call.Text`) | 2a |
| Traffic dots filtered | `DrawTraffic` in `OnGUIOverlay` (`UI.cs:230`), view and map | same overlay | 2b |
| Contacts row "Sensed: 9 of 112 in flight" | Data tab | side panel | 2b |
| Own-burn map ring | `MapRing` after `AddThreatRings` (`Orbits.cs:61`) | same map | 2b |
| Yard rows "Sees a stock burn at" (0.234 / 0.468 AU), "Sees a cold hull at" (10,000 / 20,000 km); sensor label (`Yard.cs:28`) | `YardReadouts` | same | 2b |

- The pill and the Raiders row show only when all of these are true: a raidable job is aboard, `Threat.Presence > 0`, no patrol is in reach, the ship is not docked or landed, and no chase is on.
- The state is cached at 4 Hz, because `Presence` allocates. This is the same pattern as `RiskLine` (scene `Threat.cs:18-30`).
- A quiet ship shows no pill. A pill is for a state that is not the normal one (`Hud.cs:623`). A quiet ship really draws no calls.

### Traffic (PR 2b)

- **Mass [design].** Wet mass = 500 t × (L/52 m)³: the Phase 6 rule (300 t dry × 5/3).
  - Put `HullLengthM[]` beside `Traffic.Hulls` (`Traffic.cs:34`). OpenWorld cannot `Resources.Load`.
  - QA compares the table with `Resources/Models/Fleet.json`.
- **Power.** P = ½·m·|`PathAccel(jd)`|·vₑ.
  - The peak is 0.25 g at the ends: `T = √(6d/a)`, `Traffic.cs:144`, `RunnerG`.
  - At mid-trip the thrust is not zero. `AccelAt(0.5) = (m1 − m0)/T²` (`Rail.cs:49`) is the port velocity mismatch. On Mars–Vesta it is about 1.4e-3 g [calc].
  - So rail ships **fade** near the flip. They do not go dark: a freighter at mid-trip makes about 1.3 TW and is seen to 0.054 AU [calc].

| Hull | L m | Wet t | Peak jet | Seen at, stock | Long-range |
|---|---|---|---|---|---|
| packet | 32.38 | 121 | 1.48 TW | 0.058 AU | 0.115 AU |
| courier | 45.63 | 338 | 4.14 TW | 0.096 AU | 0.192 AU |
| hauler | 123.22 | 6,653 | 81.6 TW | 0.427 AU | 0.854 AU |
| liner | 142.26 | 10,238 | 125.5 TW | 0.530 AU | 1.059 AU |
| tanker | 162.96 | 15,389 | 188.6 TW | 0.649 AU | 1.298 AU |
| freighter | 174.56 | 18,914 | 231.9 TW | 0.720 AU | 1.439 AU |

- A freighter seen from 0.5 AU shows for the first and last 26 % of its trip [calc].
- A Monte Carlo estimate puts the sky at about 5 to 9 ships from a port with stock sensors, and 7 to 13 with long-range sensors, out of about 110 [estimate]. QA measures the real count.
- **Sensors.** Use the two tiers that exist (`ShipBuild.cs:74-75`), with range ×2^level, the same factor as `Codex.SensorRange` (`Codex.cs:34`). No new field, no price change, no save change. A third tier waits for Phase 3 stealth hulls.
- **Radiators and the heat store:** Phase 3 (PR 3a). Phase 2 has no heat store and no heat limit (`MODEL-FIDELITY.md:32`).

### Per-frame cost (1M×, about 110 traffic bodies)

- **PR 2a.**
  - `Ship.Step` gets one multiply and one compare per substep.
  - The career tick gets a few flops for `seen`. `RaidJob` does the same `TargetOf` lookups that `:45` does today.
  - `PirateRate` (which allocates) runs only while seen, so cold flight gets cheaper.
  - The planner has fewer samples: the start hold is gone and the coast is capped.
  - The HUD watch runs at 4 Hz.
  - Target: no change in the `-fowprof` tick column.
- **PR 2b.**
  - Per flying ship, once per tick inside the `swTraffic` stopwatch: one `PathAccel` (about 4 D3 ops), one multiply, one compare against the squared distance.
  - That is about 110 × 0.3 µs ≈ 0.03 ms [estimate]. `sys.Pos` is cached per jd, so `DrawTraffic` reuses it.
  - Hidden ships are skipped before `Map()` and `WorldToScreenPoint`, so the draw gets cheaper. The check is never in the Repaint path.
  - Gate: traffic ≤ 0.9 ms at 1M× (today 0.74 to 0.79 ms, `design-world.md:262`).

### Save impact

- None in 2a or 2b.
- `LoudSeconds` and `LastLoudJd` are live. They are not in `Ship.SaveData`. A load resets the span and starts tracked.
- `Trip.Seen` is derived. The sensor tier is already in `build.pick`. Old saves load unchanged.
- The first persistent signature state is `CareerSave.sinkK` (PR 3a).

### Build plan

Each PR passes `-fowcareer` with 0 FAIL in both shells (default and `-fowlegacyui`).

**PR 0: the compile fix and the docs** (PRs 143 and 144; precondition)
- Main at 77f72eb does not compile (CS0136 in `TargetCard.cs`, from PR 135). PR 143 fixes it. PR 144 adds these docs.
- The player notices: nothing. It unblocks every gate.

**PR 2a: `signature-raiders`** (stacked on PRs 143 and 144)
- Adds:
  - `OpenWorld/Signature.cs` (about 40 lines)
  - `OpenWorld/Career.Signature.cs` (about 75 lines)
  - `Scenes/OpenSystemScene.Signature.cs` (about 75 lines)
  - `CareerQA.Signature.cs` (section 15, about 220 lines)
- Touches:
  - `Ship.cs` (+2 lines, 783 → 785)
  - `Career.cs` StartSpan (+1, 709 → 710)
  - `Career.Route.cs` (header and `:43-58`)
  - `Career.Pirates.cs:191` (+1)
  - `Threat.cs:74-78, :118-122, :132`
  - `UI.cs:376-377` (−1)
  - `Hud.cs:124, :624` (+2)
  - `Yard.cs:42` (+1)
  - `CareerQA.Threat.cs:56` (+1 `yield return`)
  - Docs: `MODEL-FIDELITY.md:32`, `design-combat.md` (this section, `:4`, `:28`, `:61`, `:84`, the code-impact table), `design-world.md:281, :321, :347`
- QA: section 15 (see the PR 2a brief): constants, ranges, `Step` loud accounting, `seen` arithmetic, the roll and its gate, the planner, both shells, save.
- The player notices:
  - Cutting the drive is the way to be quiet. Turning off the autopilot no longer is.
  - The pill shows LOUD on a burn in raider space and TRACKED 2:00 counting down after it.
  - A slow cruise through the belt is now the quiet choice (16.45 h → 4.99 h on a belt hop).
  - Holding still before a departure is free.
  - A pilot can brake early, drift in cold, and capture with RCS under 0.001 g, so the survey dwell is quiet.
  - The yard shows how far each drive's burn is seen.

**PR 2b: `signature-sky`**
- Adds: `CareerQA.Contacts.cs` (chained from `TrafficChecks()`).
- Touches:
  - `Traffic.cs`: `HullLengthM`, `Trip.Seen`, `Signature.HullMassT`
  - `Scenes/OpenSystemScene.Traffic.cs`: the pass inside the `TickTraffic` stopwatch `:22-28`; `DrawTraffic :31-53` skips unseen ships first; fix the stale comment at `:10`
  - `OpenSystemScene.Signature.cs`: Contacts row, map ring
  - `Orbits.cs:61`: ring call
  - `Yard.cs:28`: label "×2 scan and contact range, ½ scan time"
  - `Yard.cs` YardReadouts: 2 rows
  - `Capture/UiShots.cs`: `Signature.RevealAll = true`
  - No store class. If PR 2c needs one, call it `SensorPicture`, because `Career.Contacts()` already exists (`Career.Pirates.cs:215`).
- QA:
  - The mass table matches `Fleet.json` within 1 %.
  - Each hull's peak range matches the table within 10 % (the velocity terms move the end thrust).
  - A rail ship at s = 0.5 makes under 5 % of its peak and is not seen from 1 AU.
  - Long-range sensors double the seen distance.
  - The runner, derelict and raider are always drawn.
  - A hidden ship stays in `sys.Bodies`, and a target on it is kept.
  - The drawn-dot count equals the seen count, in both shells.
  - `RevealAll` shows every ship.
  - Report the seen counts at three fixture epochs (stock and long-range).
  - `-fowprof` traffic ≤ 0.9 ms at 1M×.
- The player notices:
  - The sky is no longer complete. Liners and freighters blaze near ports and fade toward the flip. Packets vanish beyond about 0.06 AU.
  - Long-range sensors show twice as far.
  - A ring shows how far your own burn is seen.

**PR 2c: `signature-targets`** (optional; only if play of 2b asks for it)
- Seen traffic joins Tab after the bodies and the raider (`UI.cs:198-209`).
- `KindOf` gets a `Traffic.Is` branch before Beacon (`CraftHud.cs:15-19`; today a traffic body reads as a Beacon).
- The card shows hull, owner, jet power, seen-at and light time.
- A traffic target that docks is released with "contact docked" (scene `Career.cs:251-254`).
- Update `CareerQA.PirateShells.cs:181-188`.
- QA: Tab never lands on a hidden ship, a target that goes dark stays targeted, and both shells show the kind line.

### Decisions

PR 2a depends on decisions 1 to 7.

1. **[2a] Raider knowledge: seen while loud, then a 2 h track (`TrackHoldS = BoardingSeconds`).** Recommend.
   - Alternative "sensors only" (0 h): Mars→Vesta falls to 2.00 h (moderate), and a free-orbit survey dwell falls from 33 % to 0 %. `design-world.md:81-82` would lose two strike points.
   - Alternative "port reports": every hold counts by decree, not by sensors.
2. **[2a] Binary loud line at 26.17 GW (seen across the whole 1.14e6 km window).** Recommend.
   - Alternative: graded min(1, P/P_win). The Vesta hold trim then costs 0.66 of the rate.
   - Alternative: the moving-ship edge, 419 GW.
3. **[2a] RCS counts toward the signature.** Recommend. Pilot RCS (2.45 TW) is loud. Autopilot station trims (17 to 20 GW) are quiet.
4. **[2a] Raiders roll for any raidable job, hand-flown or not.**
   - This replaces `Career.Route.cs:43` for raiders. Distress and cargo trouble keep the plan gate.
   - `RaidJob`: the target-matched job; else a cargo or passenger job; else a survey job in scan range.
   - Recommend. It closes #90 comment 1.
5. **[2a] A load or a date jump starts tracked (2 h).** Recommend. A reload must not shed a raider, and no track is saved.
6. **[2a] Baselines:** 2 MW idle for every hull, 10,000 km for stock sensors, raiders on stock sensors. Add the labelled `MODEL-FIDELITY.md:32` line: signature = jet power (RCS included) + idle, not waste heat. Recommend confirming the doc values.
7. **[2a] The Signature row replaces the exhaust-power row**, with RCS now included. One formula (`Signature.JetW`) serves the HUD, the yard and the roll. Recommend.
8. **Keep `RaidsPerHour` 0.2 and the tiers (0.1 / 1 / 3 h).** All W3 numbers stay. Recommend. Measure the calls per trip once hand fliers are raided, then tune.
9. **Planner port hold. Done (#162).** The 1.5 h hold at a port is never live exposure: `FindDock` docks the ship at once. The planner now counts the hold only at a body with no port. Mars orbit → Vesta moves from 3.50 h to 2.00 h at 1 g, and from 2.17 h to 0.67 h at 3 g.
10. **Frame-end speed.** `PirateRate` reads `ship.Vel` at the end of the update. At 1M× a whole brake can end near v = 0 inside one frame, so the roll can overcount. Recommend: a manual planned-vs-flown check at 100k× and 1M× in 2a. If it drifts, weight by the span mean (e^(−v0/V) + e^(−v1/V))/2 using `spanVel`.
11. **[2b] Traffic mass** = 500 t × (L/52 m)³ wet, 2 MW idle. Recommend.
12. **[2b] Sky** shows seen traffic only. `Signature.RevealAll` is for shots and QA only, with no player setting. Recommend, and judge it on the measured counts. The fallback is a const flag that keeps the other ships as dim dots.
13. **[2b] Sensor tiers:** the two that exist, ×2, prices unchanged. A third tier waits for Phase 3. Recommend.
14. **Radiator moves to Phase 3.** Recommend.
15. **Light delay** is omitted. A card readout comes only if PR 2c is built. Recommend.
16. **No lose-the-raider rule.** A raider that saw the burn keeps the vector, and the chase does not change. Recommend. Phase 3 can revisit this. Revisited and built in 3b (D7): a dark-ready hull can lose the raider.
17. **PR 2c is optional.** Recommend deciding after playing 2b.
18. **Housekeeping, outside this scope.** `MODEL-FIDELITY.md:29` has been stale since #130 (`Ship.cs:71` now limits by thrust). Fix it in a separate PR.

### Fights need matched velocity

- A 1 g Fast trip peaks at 856 km/s over 0.5 AU and 1,483 km/s over 1.5 AU [calc].
- Crossed courses close at more than 1,000 km/s. At that speed, a ship crosses a 5 km envelope in 5 ms. Nothing can aim.
- Only an attacker that matches velocity can fight. That is the intercept job.
- So every fight takes days to set up and shows on the map.

Pirates that board you moved to [design-world.md](design-world.md), phase W4.

## Phase 3: stealth

This needs signatures (Phase 2) only. It has no weapons. Code read on `main` at c6ebc46. Paths are under `Assets/FogOfWar/Scripts/`. Status: PR 3a (#148, `stealth-sink`) and PR 3b (#149, `stealth-lost`) are built. PR 3c (`stealth-scout`) is built in this PR. PRs 3d–3f are not built.

### The rule

- **A burn cannot hide. A coast can.** Every drive's burn is loud (`Signature.cs:13`, 26.17 GW). A stealth fit cannot change that.
- **A raider's track decays.** After a loud moment, a raider holds the track for `HoldS`. Phase 2 sets 2 h for every hull (`Signature.cs:9`).
- **Stowed radiators shorten the hold.** A hull with stowable radiators puts its waste heat into its own reaction mass (water) and stows its radiators. It is cold to a fraction ρ of the normal range. A raider then loses it in ρ × 2 h.
- **The sink is the cost.** The water warms. A full sink puts the radiators out, and the hold goes back to 2 h. Only a port or a tender of cold water empties it.
- **A raider that has lost you breaks off** (PR 3b, built). This is the first lose-the-raider rule (it revisits decision 16). It works only for a hull with stowable radiators.

**Why the hold scales with ρ.** In Phase 2 a raider's fix goes stale at u = 10,000 km / 7,200 s = 1.389 km/s [calc]. When the stale area is wider than the raider's reach on a cold hull, it has lost the ship. Stock reach is 10,000 km, so it is lost in 7,200 s, the Phase 2 value. A stowed hull is seen to ρ × 10,000 km, so it is lost in ρ × 7,200 s. u is a game value. It stands for small unseen trims and tracking error, which the game does not model. `MODEL-FIDELITY.md` says so.

**Why not a range test against raiders.** Raiders are a rate field, not ships with positions (`Career.Pirates.cs:119-126`). A raider never starts closer than 127,094 km (`design-combat.md:152`). A 2 MW hull is seen to 10,000 km (`Signature.cs:17`). So "radiators out, now visible" can reach raiders only through the hold. This design uses the hold.

### The model

| Item | Value | Tag |
|---|---|---|
| Water heat capacity c | 4,186 J/kg·K | [calc] physics |
| Sink limit | 100 K above port water | [design] |
| Sink load `SinkLoadW` | 2 MW. Same number as `Signature.IdleW`, a separate role | [design] |
| Stowable radiators ρ | 0.3: cold power ρ² × 2 MW = 0.18 MW, seen at 3,000 km, hold 2,160 s = 36 min | [design] |
| Stowable radiators part | 12 t, 60,000 cr, 1 day | [design] |
| Fixed radiators part (stock) | ρ = 1, 0 t, 0 cr, 1 day | [design] |
| Corvette facet (3e) | ρ × 1/3 always; 0.1 stowed: 1,000 km, 12 min | [design] |
| Stale-fix speed u | 1.389 km/s | [calc] |

- **The 12 t check.** A 2 MW radiator at 300 K, ε 0.9, two sides, needs 2e6 / (0.9 × 5.67e-8 × 300⁴ × 2) = 2,419 m² [calc]. At 5 kg/m² that is 12.1 t [calc]. So the stowable set is about one more radiator, plus the tank loop.
- **The cost in g.** The stock frigate with stowable radiators is 312 t dry. It gives 4.903 MN / 512 t = 0.977 g full, 1.603 g empty and Δv 10,000 × ln(512/312) = 4,953 km/s [calc]. Stock is 1.000 g, 1.667 g and 5,108 km/s.

**Sink arithmetic** [calc]. 1 t warmed 100 K stores 1,000 × 4,186 × 100 = 418.6 MJ. At 2 MW that is 209.3 s per tonne.

| Water | Stored heat | Time to full at 2 MW | Burn windows (2 h each) |
|---|---|---|---|
| 400 t | 167.4 GJ | 83,720 s = 23.26 h | 11.6 |
| 300 t | 125.6 GJ | 62,790 s = 17.44 h | 8.7 |
| 200 t | 83.7 GJ | 41,860 s = 11.63 h | 5.8 |
| 100 t | 41.9 GJ | 20,930 s = 5.81 h | 2.9 |
| 50 t | 20.9 GJ | 10,465 s = 2.91 h | 1.5 |

- Fill rate: 2e6 / (4,186 × 2e5 kg) = 2.389e-3 K/s = 8.6001 K/h at 200 t. At 50 t it is 34.40 K/h [calc].
- One frame at 1M× and 60 fps is 16,667 s. That is 39.82 K at 200 t [calc]. So the soak must clamp at 100 K.

**When the radiators stow** [design]. A fitted hull stows them by itself:
- in the 2 h after its last loud moment (the window where a raider could hold the track), and
- during a raider's chase (3b).
- While the drive is loud, they are out. A burn cannot hide, so the sink takes nothing then.
- Outside those times they are out, and the sink holds. Deployed radiators reject exactly the 2 MW load. They do not cool the sink.
- So one burn costs at most 2 h of sink: 17.2 K at 200 t, 68.8 K at 50 t [calc]. A long coast costs nothing.
- Nothing is stowed near Earth by choice. The window opens after any burn. A dock empties the sink, and most burns end at a dock.

**The stored variable is `Career.SinkK`:** the water's rise in K above port water. It is intensive, so the tank's remaining water keeps its temperature when some of it leaves.

| Event | What happens to the stored heat | Code |
|---|---|---|
| Burning warm water | It leaves with the exhaust. `SinkK` does not change, and the tank holds less water, so the time left falls. At 100 K a stock burn (4.903 MN / 1e7 m/s = 0.490 kg/s) carries 0.490 × 4,186 × 100 = 205 kW, 10 % of the load [calc]. Flushing 200 t by burning takes 113 h [calc]. A burn does not cool the sink. | none (`Ship.cs:178` unchanged) |
| Drive waste heat | It leaves with the exhaust [design]. A 24.5 TW drive at 0.01 % waste would fill the sink in 34 s [calc], so a burn cannot load it. | none |
| Cargo-trouble vent | No change (intensive) | `Career.Route.cs:160` unchanged |
| Refit to a smaller tank | No change (intensive) | `Career.Yard.cs:34-35` unchanged |
| Refuel at a port | It needs `Docked` (`Career.cs:614`), and docked sets `SinkK = 0`. No mix needed | none |
| Emergency tender (30 t) | Cold port water mixes in: K' = K × before / after. Example: 80 K at 20 t plus 30 t gives 32.0 K. A dry tank (full sink) plus 30 t gives 0 K | `Career.cs:630-635` |
| Docked (port or yard) | `SinkK = 0` at once [design]. The port's coolant loop takes the heat. A ship always leaves cold | `Career.Tick` |
| Landed off a port (Pallas) | Not docked: it soaks like a coast | none |
| Dry tank | No water, no sink: `SinkK = 100`, radiators out | `Career.Heat.cs` |
| Sandbox | No soak (`Tick` returns at `Career.cs:190`) | none |
| New career, load | `SinkK` lives in `Career`. The scene builds a new `Career` for a new game (`OpenSystemScene.Career.cs:28`) and for a load (`:607`). No fuel-reset site needs a heat reset | none |
| 1M× warp | One soak per `Career.Tick`, which runs once per frame with the achieved time (`OpenSystemScene.cs:491,499`). The load is constant, so the step is exact, with a clamp at 100 K. The soak seconds are the overlap of the frame with the 2 h window, which `SeenSeconds` already computes. If the sink fills inside the frame, `Soak` keeps the fill date and `SeenSeconds` counts the stowed hold up to it and `TrackHoldS` from it on, so the raid roll sees what one tick per frame would see. The 1k×+ rails coast (`OpenSystemScene.cs:466-475`) takes no `Ship.Step`, and that does not matter here. | `Career.Heat.cs` |

- **The water mass in the soak** is the fuel at the end of the update. The window is after the last loud moment, so no main-drive fuel is spent inside it. The only error is quiet RCS trims, under 0.001 g. That is negligible [calc].

### What "cold" means in the signature formula

- `Signature.IdleW` (2 MW) stays the sensor reference: stock sensors see 2 MW at 10,000 km. It is no longer "what every hull radiates".
- New signature: `RangeKm(jetW, baseKm = StockKm, coldW = IdleW) = baseKm · √((jetW + coldW) / IdleW)`. Every existing call uses the default, so no existing number moves.
- Cold means `jetW = 0`. Then R = baseKm · √(coldW / IdleW) = baseKm · ρ.

| Hull state | coldW | Stock sensors | Long-range | Hold |
|---|---|---|---|---|
| Fixed radiators (stock, today) | 2 MW | 10,000 km | 20,000 km | 7,200 s |
| Stowable, radiators out (outside the window, or sink full) | 2 MW | 10,000 km | 20,000 km | 7,200 s (sink full) |
| Stowable, stowed (frigate, ρ 0.3) | 0.18 MW | 3,000 km | 6,000 km | 2,160 s |
| Corvette, out (facet 1/3) (3e) | 0.222 MW | 3,333 km | 6,667 km | 2,400 s |
| Corvette, stowed (ρ 0.1) (3e) | 0.02 MW | 1,000 km | 2,000 km | 720 s |

- **A burn is unchanged.** At 24.52 TW the cold term moves the range by under 1e-6 of itself [calc]. A corvette on the Fast drive is seen at 0.4054 AU, the same as the frigate's pinned Fast value (`CareerQA.Signature.cs:42`). A stealth hull cannot hide a burn.
- **`LoudW` and `Loud` do not change.** They read jet power only (`Signature.cs:13,18`), so 26.17 GW stays.
- **Every pin on the 2 MW / 10,000 km baseline holds,** because the stock build has fixed radiators:

| Pin | Where | In Phase 3 |
|---|---|---|
| WindowKm, LoudW | `CareerQA.Signature.cs:36` | unchanged |
| Cold 10,000 km; stock, Torch, Economy, Fast, RCS in AU | `:42` | unchanged (default `coldW`) |
| Loud line at 500 t | `:47-48` | unchanged |
| Yard row "Seen at, full burn · cold" = "0.234 AU · 10,000 km" / "0.523 AU · 10,000 km" | `:58` | unchanged (stock and Torch drafts are fixed) |
| Chase with a cold ship stays after 600 s | `:219` | unchanged (fixed radiators never lose a raider) |
| Planner hours 3.50 / 2.17 / 0.75 / 0 / 2.00; hop 5.48 / 5.37 / 4.99 | `:239-255` | unchanged (`holdS` defaults to `TrackHoldS`) |
| Planner line has "seen" and "dock first" | `:269` | unchanged |
| Row "cold" and "10,000 km", Raiders "no track", pill "-" | `:316` | unchanged |
| Signature row text and range 0.20-0.24 AU, pill | `:331` | unchanged |
| "TRACKED 1:59" or "2:00" | `:343` | unchanged |
| No "oud" in either save | `:365` | new names `sinkK`, `Quiet`, `Contraband`, `Exposure`, `FixJd` (not saved), hull names all pass; job titles must avoid "loud", "cloud" |
| Mass table, ranges, seen counts, rings, "Sees a stock burn", "Sees a cold hull at" 10,000 / 20,000 km, `StockBurnW` | `CareerQA.Contacts.cs:24-28,114,146,195,333-355,368-372` (survey) | unchanged: traffic and the player's own sensors use the 2 MW reference |
| Stock build 300 t, 1 g; parts have days > 0; old save loads stock | `CareerQA.Yard.cs:30,37,44-52` | unchanged (fixed radiators: 0 t, 0 cr, 1 day) |

- **UI rows that print the baseline:**
  - The Signature cold text (`Scenes/OpenSystemScene.Signature.cs:38`) keeps "cold (2 MW idle)" for fixed or out radiators. Stowed reads "cold, radiators stowed (0.18 MW), seen at 3,000 km".
  - The Raiders cold text (`:42`) reads "no track: cold for over {hold}". That is "2 h" for stock.
  - The yard row `Yard.cs:43` uses the build's stowed cold power. `:45` ("Sees a cold hull at") stays the 2 MW reference.
  - The planner line (`Threat.cs:132`) prints the hold.
  - The own-burn ring (`Signature.cs:49-57`) is unchanged: no ring when cold.

### Raiders

**The hold (3a).** `Career.HoldS(ship)` is ρ × `TrackHoldS` while the hull is dark-ready (stowable, water aboard, sink under 100 K, not sandbox). Otherwise it is `TrackHoldS`. It replaces the constant at:
- `Career.Signature.cs:15` (the `SeenSeconds` tail);
- `:46` (`RaiderWatch`);
- `:55` (`SeenLine`);
- `Threat.cs:120,122` (a new `holdS` parameter on `Threat.Of`, default `TrackHoldS`);
- `Threat.cs:132` (`Line`);
- the scene's Raiders cold text (`Scenes/OpenSystemScene.Signature.cs:42`).

The roll, the HUD, the call card and the planner then agree.

Example [calc]. The last loud moment is 30 min before a 3,600 s update. Stock: the tail is min(end, start + 1.5 h) − start = 3,600 s. Stowed: min(end, start − 1,800 + 2,160 s) − start = 360 s.

**The live rule** [design]. A ship is tracked while the time since its last loud moment is under `HoldS` now. If the sink fills 50 min after a burn, the ship is tracked again with 70 min left. The stale-fix rule says the same thing: the radiators came out, and the raider's 4,170 km stale area (u × 50 min) [calc] is inside the 10,000 km deployed reach. This also stops an exploit. Under a hold fixed at the loud moment, a pilot could stow for 36 min and then deploy for free. Under the live rule, the radiators stay stowed for the full 2 h window. The alternative is in Decision D4.

**The planner** uses `HoldS` now. It does not predict a sink that fills on the way. With the radiators out on long coasts, the sink at arrival is the sink at departure minus 2 h per burn window. The line says "(or 36 min after one)".

**Lose the raider (3b, revisits decision 16).**
- New state: `Encounter.FixJd`, the raider's track memory. It is not saved, because `Encounter` is not saved (`Career.Pirates.cs:70`). `StartContact` (`:204-211`) sets it to `jd`. The call is a fix.
- In `Contacts`, between the `Hopeless` block (`:234-239`) and `if (!alongside) { e.Dwell = 0; return; }` (`:240`):
  ```
  if (!DarkReady(ship) || e.Range < Signature.RangeKm(0, Signature.StockKm, Signature.ColdW(Build.StowRho))) e.FixJd = jd;
  if ((jd - Math.Max(e.FixJd, ship.LastLoudJd)) * 86400 >= HoldS(ship))
  { EndContact(Happening.RaiderGone, OffNote(BreakOff.Lost, null, Haven()), true, jd); return; }
  ```
- As built, the test is the public helper `Career.LoseInS(ship, jd)`. It returns the seconds until the raider loses the ship, or NaN while the raider holds the track (no chase, not dark-ready, or inside the stowed reach). `Contacts` refreshes `FixJd` on NaN and ends the chase at 0 or less. The scene's Raider track row reads the same helper.
- A fixed-radiator ship refreshes the fix every update, so decision 16 holds as it is for it. `CareerQA.Signature.cs:219` and the coast control (`CareerQA.Pirates.cs:621`) stay valid.
- Alongside is 500 km (`Career.Intercept.cs:13`), inside 3,000 km. So a boarding is never lost.
- A full sink or a dry tank ends the escape: the ship is not dark-ready, and the fix refreshes.
- A loud moment restarts the clock, through `LastLoudJd`.
- `BreakOff.Lost` is appended (`:298`). `OffNote` (`:305-308`) gets "Track lost" + ": the raider breaks off" + `Back(haven)`.
- There is one exit, `EndContact` (`:348`). The next call waits `RouteGapDays`, 0.5 d (`Career.Route.cs:45`). After the lose, `SeenSeconds` is 0 too, because the tail uses the same hold.
- `Pursuit` is not changed. The raider steers at the true ship until the end. The lose is a hold rule, not a steering rule.
- **The cost** [calc]. 36 min dark is 21.2 km/s of Δv not gained at 1 g. At `ChaseWarp` 1,000× (`Career.Pirates.cs:77`) it is 2.2 s of real time. From rest, a 1 g raider closes 0.5 × 9.807e-3 × 2,160² = 22,877 km in that time. Its start is 127,094 km or more. This is cheap. ρ is the knob (Decision D2).

### Track memory and ghosts

- The raider's track memory is `Encounter.FixJd` (3b) plus the existing `Ship.LastLoudJd`. That is the only new track state in Phase 3.
- **Traffic ghost: not built in Phase 3.** Nothing in Phase 3 reads it.
  - The later hook is one unsaved `double Trip.LastSeenJd`, set in `Traffic.Sense` (`Traffic.cs:110-120`): 110 double stores per call.
  - It waits for Phase 4 targeting and a `SensorPicture` store (`design-combat.md:364`).
  - A ghost must fade in real time, not sim time. At 1M× one frame is 16,667 s, so a 300 s sim ghost never draws.

### Jobs

**Scouting (3c, built in this PR): a quiet watch on a raider haven.**
- A Survey with `Contract.Quiet = true`. The target is Pallas [design]. Hektor is too far for a board job.
- Posted at Ceres and Vesta in board slot k = 8, with a 0.5 roll. The loop (`Career.cs:519`) becomes `k < 9`. Earlier slots draw first, so their jobs and keys do not move. `Key` keeps three parts (`:488`).
- In `Scan` (`Career.cs:410`): `if (c.Quiet) q = QuietSeconds(ship, jd, simDt, q, whole)`. The dwell counts only the seconds no raider could see. When the ship was in range for the whole update, that is `Math.Min(q, Math.Max(0, simDt - seen))`. When it entered or left the range in the update, the seen seconds may lie anywhere in `q`'s window. Then only `Math.Max(0, q - seen)` is sure to be unseen, and the dwell counts that.
- Pay is the survey pay × 1.5 [design].
- Possible on any hull: wait out the 2 h hold, then dwell. Stowed radiators cut the wait to 36 min.
- Four more [design] choices, made in 3c:
  - **The chase rule.** While a raider chase is on (`Encounter != null`), the quiet dwell gets no credit. `SeenSeconds` sees only loud seconds and the hold after them. It does not see a chase. A raider that has the ship's track is watching it.
  - **A chase that has ended leaves no track.** `Scan` runs before `Contacts`, so a chase that ends in an update still blocks that update. After it, the hold runs from the last burn, as it does for the raid roll. A boarding or an outrun does not start a new hold.
  - **A landed ship is unseen.** No raider reaches a landed ship (`RouteTick`, `Contacts`). So on Pallas all of `q` counts, and the hold after the landing burn does not delay the dwell.
  - **Days and express include the hold.** The posted days and the express window add the 2 h hold (`Signature.TrackHoldS`) to the dwell. The dwell starts only after the arrival burn's hold. So a stock hull can still earn the express bonus. The planner arrival lines still leave out the hold wait. That is a later fix.
- Built: `Career.Scout.cs` holds `ReconBody`, `QuietLine`, `QuietSeconds` and `Recon`. `Recon` draws no rng at other ports, and only the roll and the slack at Ceres and Vesta. It takes `nextId++` only when it posts a job, so contracts made after this board can get a different `Id`. Keys do not move.

**Smuggling (3d): contraband cargo.**
- A Cargo job with `Contract.Contraband = true`. It keeps every Cargo gate (`RaidJob`, `PirateCall`, route events, the hand bonus), so it needs no new `JobKind`.
- Posted at Tharsis and Compact ports (Mars, Vesta, Ceres, Ganymede, Callisto, Titan, Enceladus) in slot k = 9, with a 0.5 roll.
- **It is posted only if the route's planner risk is at least 1 h:** `Threat.Of(...).Hours ≥ 1` at the posting date. The call costs about 1 ms and runs once per port-day (`RefreshBoard`, `Career.cs:485`).
- **Exposure** is the expected number of raider calls. Each update, while the job is aboard and not docked, with `seenS = SeenSeconds(...) > 0`: `c.Exposure += PirateRate(sys, ship, c, jd) × seenS / 3600` (`Career.Pirates.cs:119`). This is the roll's own exponent, so the roll and the grade agree.
- **Pay on delivery** (in `Complete`, `Career.cs:294`): posted pay × 1.25 × e^(−Exposure / 0.5). Posted pay is plain cargo pay × 1.5 [design].

| Exposure (calls) | Pay vs plain cargo [calc] |
|---|---|
| 0 | 1.5 × 1.25 = 1.875 |
| 0.1 | 1.535 |
| 0.314 | 1.00 (break-even: 0.5 × ln 1.875) |
| 0.7 | 0.462 |

- **Stealth helps** by cutting each burn's tail from 2 h to 36 min (12 min on the corvette). It cannot hide the loud seconds.
- **"Caught by the Guard"** (`design-world.md:136`) is not built in Phase 3. Phase 3 has no patrol sensor model. A follow-up can add one: a port with `PatrolG > 0` sees the ship within `RangeKm(P, StockKm, coldW)` of the port body. Then the stowed range matters to a second observer. That PR also adds `ChangeStanding(Faction, int)` (`Career.Factions.cs:57`).

### The hull (3e, last gameplay PR) and the art (3f)

**The corvette** [design]:
- Name: "FWO-060 stealth corvette". Length 60 m.
- Structure: 228 × (60/52)³ = 350.25 t [calc] (Phase 6 rule, `design-combat.md:501`).
- `MaxThrust = Rated(3)` = 14.71 MN, so no Torch drive.
- `Facet = 1/3`. Price 400,000 cr.
- Stock pick {1, 2, 0, 0, 0, 1}: Standard drive, 300 t tank, 50 t hold, no cabin, stock sensors, stowable radiators.

Corvette numbers [calc]:
- Dry: 350.25 + 40 + 24 + 5 + 0 + 2 + 12 = 433.25 t.
- 0.682 g full, 1.154 g empty, Δv 5,262 km/s.
- Sink on 300 t: 17.44 h.
- Stowed: 1,000 km and a 12 min hold. Out: 3,333 km and 40 min.

The frigate keeps `Facet = 1` and price 0, so every pin holds.

**The multi-hull seam:**
- `Hull` gains `Price`, `Facet`, `Model`, `int[] StockPick` (`ShipBuild.cs:17-21`). Append the corvette to `Hulls[]` (`:36-39`). Never rename or reorder hull 0. Its name is the save key.
- `Stock(Hull h)`. Keep a no-argument `Stock()` that returns hull 0, for `Signature.StockBurnW` (`Signature.cs:26`), `Career.ExpressReport.cs:18` and QA.
- `Checked()` (`:95-102`) sets `hull` first, then starts from that hull's stock pick.
- `Career.Yard.cs:11-23`. A hull change counts as a change.
  - It costs `to.Hull.Price − Build.Hull.Price × 0.7`, plus the per-slot module differences. It takes 7 days [design].
  - It is refused with "buy out the ship first" while `!OwnsShip` (`Career.cs:105`).
  - It is refused with "only Earth, Mars and Ceres sell hulls" where `!Docked.HullSale`.
  - `Port.HullSale` is true for Earth orbit, Mars orbit and Ceres (`Career.cs:71,73,74`). This is agreed Decision 4. The check runs only on a hull change. Every port still refits parts.
- In the yard draft, picking a hull resets the picks to that hull's stock.
- `ShipPaid` does not change with a hull swap. The new hull is paid in cash.
- Both yard shells get a hull row before the slot loop: `YardPanel` (`Scenes/OpenSystemScene.Yard.cs:78`) and `BuildYard` (`:111`). It shows only at a hull-sale port. `JobsSignature` (`Scenes/OpenSystemScene.CareerHud.cs:234`) adds `Build.hull` and `yardDraft?.hull`. Without them, a swap with equal picks does not redraw.
- `HoldS` and the cold power multiply by `Hull.Facet`. 3a already routes every reader through `HoldS`, so 3e adds no new call sites.

**What ships without new art:**
- 3a, 3b, 3c and 3d need no model, marker or texture. The radiators are a yard part, HUD text and a pill.
- 3e uses a **placeholder: the existing `PlayerShip` model scaled to 60 m.** It looks like the frigate, 15.4 % longer (60/52) [calc]. The yard hull row says "(placeholder model)". The code change:
  - `float realLen = 0.052f;` at `OpenSystemScene.cs:778` becomes `float realLen = HullKm();`. The file stays at 803 lines.
  - `HullKm()` goes in a new partial, `Scenes/OpenSystemScene.Hull.cs`.
  - The camera floor `0.045f` (`OpenSystemScene.Hud.cs:24`) scales by `HullKm() / 0.052f`.
  - `shipLen` comes from the model's bounds (`OpenSystemScene.cs:356`), so the plume scales too.
- 3f is the drop-in for the user's Blender model. It needs new art:
  - `Resources/Models/StealthShip.fbx` and its atlas, `Models/StealthShip/StealthShip_{Albedo,Normal,Mask,Emission}.png`.
  - Markers: `Nose`, `Engine`, `Cockpit`, `RCS_<Fore|Aft>_<Up|Down|Left|Right>`, optional `PDC_n`, and new `Radiator_0..3`, modelled deployed with the pivot at the hinge.
  - `Hull.Model = "StealthShip"`.
  - `RefreshHullModel()` in `OpenSystemScene.Hull.cs` reruns the body of `BuildShip` (`OpenSystemScene.cs:345-370`) after `RefitTo` and `FromSave`.
  - Each `Radiator_n` folds (local X scale 1 to 0.05 over 1.5 s) while stowed.
  - Add an `ART-CONTRACT.md` entry.
  - A missing file falls back to `PlayerShip` (`OpenSystemScene.cs:349-350`).

### UI in both shells

All text comes from shared functions, so the classic panel, `-fowlegacyui` and the UI Toolkit shell show the same words.
- **Rows:** `SignatureReadouts` (`Scenes/OpenSystemScene.Signature.cs:33-44`) feeds the UI Toolkit Data tab (`OpenSystemScene.Hud.cs:559-562`) and the classic side panel (`FogOfWarApp.cs:160-164`).
  - 3a: the Signature cold text, a "Heat sink" row (stowable builds only), and the Raiders cold text.
  - 3b: a "Raider track" row while a chase is on. `RaiderWatch` returns `None` then (`Career.Signature.cs:44`). It reads "it loses you in m:ss if you stay dark" or "it has your track".
- **Pill (UI Toolkit only):** "DARK h:mm" (dark time left in the sink) while stowed, in the cold tone. The classic shell has no pills. The Heat sink row carries the state, as the Raiders row does for SEEN (`CareerQA.Signature.cs:13`).
- **Yard:** the Radiators row appears in both shells with no loop change, because both loop over `Slot` (`Yard.cs:78,111`). `PartLabel` (`:24-31`) and `YardReadouts` (`:34-48`) get a radiators branch and a "Dark, full tank" row.
- **Notes:** 3b's Lost note uses the existing one-line strip. It is measured for width (`CareerQA.PirateShells.cs:409-446`).
- **Not touched:** `RaiderLine` and `RunNote`. QA pins the raider pill text and the note widths.
- **Tutorial or help text (not built in 3a; later PR):** "Coast with the drive off and hands off the RCS." Pilot RCS above about 1 % of full is loud at 500 t (`Signature.cs:12`; `CareerQA.Signature.cs:47`).

### Per-frame cost (1M×, about 110 traffic bodies)

- 3a: one `Soak` per `Career.Tick`: a few flops, no allocation. `HoldS` and `DarkReady` are field reads. `Threat.Of` has the same samples (the hold changes only the bounds). `Traffic.Sense` and `DrawTraffic` are not touched, so the 110 bodies cost the same. `Ship.Step` is not touched. QA times 100,000 soaks.
- 3b: one `sqrt` per update, only while a chase is on.
- 3c: one `SeenSeconds` per quiet survey update (O(1)).
- 3d: `PirateRate` (it allocates through `Threat.Presence`) only when `seenS > 0`, the same as the roll. `Threat.Of` once per port-day per slot.
- 3e: none per frame.

### Save

- 3a: `CareerSave.sinkK` (double), next to `fuel` (`Career.cs:48`). An old save reads 0, a cold tank. `JsonUtility` ignores unknown keys, so an old build reads a new save.
- 3a: `StockPick` grows to 6 entries. `Checked()` copies `min(pick.Length, 6)` entries (`ShipBuild.cs:99`), so a 5-pick save loads with fixed radiators. `FromCounters` starts from `Stock()` (`:87`).
- 3b: `Encounter.FixJd` is not saved. A load drops the chase (D8).
- 3c and 3d: `Contract.Quiet`, `Contraband`, `Exposure` default to false, false and 0, the same as `ExpressDays` and `SpoilJd` (`Career.cs:29-30`).
- 3e: an unknown hull falls back to hull 0 (`CareerQA.Yard.cs:51`). A corvette save opened by an older build loads as the frigate. That is not supported, as with any new content.
- No saved name or title may contain "oud" (`CareerQA.Signature.cs:365`).

### QA

- Section 16 is in a new partial, `CareerQA.Stealth.cs`, chained at the end of `SignatureChecks` (`CareerQA.Signature.cs:29`). `CareerQA.cs` (827 lines) is not touched.
- 3e adds `CareerQA.Hulls.cs`, chained from the end of `StealthChecks`.
- Every check uses `Check(ok, msg)` and logs what it measured.
- Run `-fowcareer` in the UI Toolkit shell and with `-fowlegacyui`. Toolkit-only parts log a SKIP line, as `CareerQA.Signature.cs:290` does.

### Build plan

Each PR passes `-fowcareer` with 0 FAIL in both shells, and is playable alone.

**PR 3a: `stealth-sink`** (needs new art: no. Built, #148)
- Adds:
  - `Slot.Radiators` with "Fixed radiators" (stock: ρ 1, 0 t, 0 cr, 1 day) and "Stowable radiators" (ρ 0.3, 12 t, 60,000 cr, 1 day);
  - `Career.SinkK`, soaked once per tick in the 2 h window after a loud moment;
  - `Career.HoldS`, `DarkReady` and `ColdW`; `RangeKm(…, coldW)`; `Signature.HoldText`;
  - the docked, tender and dry rules; `CareerSave.sinkK`.
- UI: rows, the DARK pill and yard rows in both shells.
- Docs: `MODEL-FIDELITY.md:32-33`, this section, the status lines, `design-modes.md:97`.
- Touches:
  - `OpenWorld/ShipBuild.cs`, `OpenWorld/Signature.cs`;
  - new `OpenWorld/Career.Heat.cs`;
  - `OpenWorld/Career.cs` (one call, one save field, the tender mix);
  - `OpenWorld/Career.Signature.cs`, `OpenWorld/Threat.cs`;
  - `Scenes/OpenSystemScene.Signature.cs`, `.Threat.cs`, `.Yard.cs`, `.Hud.cs` (2 lines);
  - new `CareerQA.Stealth.cs`, plus 1 line in `CareerQA.Signature.cs`.
- QA: section 16 of the career QA, in `CareerQA.Stealth.cs` (16a-16h).
- The player notices:
  - A new yard part. With it, a burn's track lasts 36 min, not 2 h.
  - The roll, the HUD clock, the call card and the planner all show the shorter hold.
  - A Heat sink row counts the dark hours left: 11.6 h on a full 200 t tank, about 5.8 burns.
  - A full sink brings the 2 h back. Any dock empties it.
  - The stock ship plays as in Phase 2.

**PR 3b: `stealth-lost`** (needs new art: no. Built, #149)
- Adds:
  - `Encounter.FixJd`, the lose rule, `BreakOff.Lost` and its note;
  - the chase stow: `Career.Stowed` and `Soak` gain `Encounter != null` (stowed for the quiet seconds of a chase);
  - the "Raider track" row;
  - one call-card sentence when dark-ready and the sink outlasts the hold (`SinkLeftS >= HoldS`; a chase soaks every quiet second, so a nearly full sink would fill first): "Or go dark: cut the drive and coast {hold} out of its sight."
- In 3a nothing reads a stowed state during a chase (`RaiderWatch` returns `None`, and `RouteTick` returns at `Career.Route.cs:43`). So the chase stow waits for 3b, and a 3a chase does not spend the sink. 3b adds the chase stow. `Career.Stowed` also holds while `Encounter != null`. `Soak` then counts the update's quiet seconds: `simDt` less its loud seconds. A chase keeps no fill date.
- Touches: `OpenWorld/Career.Pirates.cs` (`:17-19`, `:194`, `:204-211`, between `:239` and `:240`, `:298`, `:305-308`), `OpenWorld/Career.Heat.cs` (`Stowed`, `Soak`), `Scenes/OpenSystemScene.Signature.cs`, `CareerQA.Stealth.cs` (16c fix, 16i, 16i scene), `CareerQA.PirateShells.cs:409-416`, `CareerQA.Pirates.cs:656`.
- QA:
  - A dark-ready bench with a raider 100,000 km off is still chased at 2,100 s and is lost at 2,200 s with "Track lost: the raider breaks off".
  - The draw count is 0 in the next update (`RouteGapDays`).
  - A fixed-radiator ship is still chased at 8 h.
  - A raider at 2,000 km keeps the chase at 3,000 s.
  - A 60 s loud step at 1,000 s restarts the clock.
  - A full sink keeps the chase.
  - `CareerQA.Signature.cs:219` and `CareerQA.Pirates.cs:621-622` pass unchanged.
- The player notices: a fitted ship that is chased can cut the drive and coast 36 min to make the raider leave. A full sink or a burn ends the trick.

**PR 3c: `stealth-scout`** (needs new art: no. Built in this PR)
- Adds: `Contract.Quiet`, a `Recon` generator beside `Survey`, board slot k = 8 at Ceres and Vesta, the quiet dwell in `Scan`, and a job-card line "counts only unseen time".
- [design] choices, as in the Scouting section above: no quiet credit during a chase, none from a chase that has ended, full credit while landed, and Days and express that include the 2 h hold.
- Touches:
  - `OpenWorld/Career.cs` (`:31`, `:410`, `:519`);
  - new `OpenWorld/Career.Scout.cs`;
  - the kind chains in both shells. Survey lines, not re-checked: classic `OpenSystemScene.Career.cs:365-371,393-395`; Toolkit `CareerHud.cs:328-332,400`.
- QA:
  - A half-loud update credits half the dwell. A tail update credits 0. A stowed update 40 min after a burn credits all of it.
  - An update that leaves the range credits only `q - seen`. A landed ship credits all of `q`.
  - Check 16j logs one Ceres day's keys and titles for k < 8, so a later diff shows if they move. It makes no comparison itself.
  - An old `Contract` JSON loads `Quiet = false`.
  - Both shells show the line.
- The player notices: a high-pay watch on Pallas that counts only unseen time.

**PR 3d: `stealth-contraband`** (needs new art: no)
- Adds: `Contract.Contraband` and `Exposure`, slot k = 9 at Tharsis and Compact ports where the route risk is ≥ 1 h, the exposure sum, and the pay grade in `Complete`.
- Touches:
  - `OpenWorld/Career.cs` (`:31`, `:294`, `:519`, the `Tick` call near `:217`);
  - new `OpenWorld/Career.Smuggle.cs`;
  - the card lines in both shells;
  - `JobsSignature` (`CareerHud.cs:234`) adds `Exposure`.
- QA:
  - Exposure equals the sum of the roll's exponents over the same updates.
  - Pay at exposure 0, 0.1, 0.314 and 0.7 is 1.875, 1.535, 1.00 and 0.462 of plain pay (± 1 cr).
  - No contraband job is posted on a route under 1 h.
  - Board keys for k < 9 are the same.
  - A save round trip keeps `Exposure`.
- The player notices: a high-pay job that pays most for a quiet run.

**PR 3e: `stealth-corvette`** (needs new art: no. It ships on the placeholder model and says so)
- Adds: the multi-hull seam, the corvette, `Port.HullSale`, the hull swap rules, the yard hull row in both shells, `Hull.Facet` in `HoldS` and `ColdW`, `HullKm()`.
- Touches:
  - `OpenWorld/ShipBuild.cs`, `OpenWorld/Career.Yard.cs`, `OpenWorld/Career.cs` (`:8-14`, `:71-74`);
  - `Scenes/OpenSystemScene.Yard.cs`, `.CareerHud.cs:234`;
  - `OpenSystemScene.cs:778` (one line);
  - `.Hud.cs:24`;
  - new `Scenes/OpenSystemScene.Hull.cs`;
  - new `CareerQA.Hulls.cs`.
- QA:
  - `Hulls[0]` keeps its name. A 5-pick save loads the frigate. A corvette save round trips.
  - A swap is refused when unowned and at Luna, and allowed at Earth, Mars and Ceres.
  - The swap cost equals the formula.
  - A Torch drive on the corvette is "too big for the hull".
  - The corvette is 433.25 t dry and 0.682 g, with a 17.44 h sink, 1,000 km and 12 min stowed, 3,333 km out.
  - Its Fast burn range is 0.4054 AU.
  - `realLen` is 0.060.
  - A swap with equal picks redraws the Jobs page.
- The player notices: after the buyout, a 400,000 cr hull at Earth, Mars or Ceres that loses a raider in 12 min. It looks like the frigate, stretched 15 %, until the art lands.

**PR 3f: `stealth-corvette-art`** (needs new art: yes, the user's Blender model)
- Adds: the model drop-in, `RefreshHullModel()`, the radiator fold, and the `ART-CONTRACT.md` entry.
- QA:
  - The model loads by name, and every marker is found.
  - It is 60 m long.
  - The radiators fold when stowed.
  - A missing file falls back to `PlayerShip`.
  - Screenshots in both shells.

### Decisions

PR 3a depends on D1-D6 and D11. Each has a safe default, the recommendation, so none blocks 3a.

1. **[3a] D1. Stowable radiators are sold for the existing frigate.** Recommend. Without this, all stealth play waits for the corvette art. Alternative: corvette only.
2. **[3a] D2. ρ = 0.3** (0.18 MW, 3,000 km, 36 min hold). Recommend, and tune after play. ρ 0.5 gives 60 min. The corvette facet 1/3 is a 3e value.
3. **[3a] D3. The radiators stow by themselves** in the 2 h window after a loud moment and during a chase, and are out while loud. Recommend. Alternative: a pilot toggle (a key and a switch in both shells), which costs micromanagement at 1M×. Stowing for the whole trip would fill the sink before every interplanetary arrival.
4. **[3a] D4. The live hold rule:** tracked while the time since the last loud moment is under `HoldS` now. Recommend: it adds no state, it matches the stale-fix rule, and it stops the "stow 36 min, then deploy" exploit. Alternative: freeze the hold at the last loud moment (one more field).
5. **[3a] D5. Heat paths.** Drive waste heat leaves with the exhaust. Docked sets the sink to 0 at once. The tender mixes cold water. Deployed radiators hold the sink but do not cool it. Recommend. Alternatives: a dump rate in port, or in-flight cooling. Both add state for no gain.
6. **[3a] D6. Part numbers:** fixed radiators 0 t, 0 cr, 1 day; stowable 12 t, 60,000 cr, 1 day; sink load 2 MW; limit 100 K. Recommend.
7. **[3a] D11. Docs.** `MODEL-FIDELITY.md:32-33` gets rewritten. The heat sink, the 2 MW load, ρ and u are gameplay values, not an engineering claim. Recommend.
8. **[3b] D7. A raider can be lost only by a dark-ready hull.** Recommend. Alternative: every cold hull after 2 h. Every catch takes at least `CatchMinS` = 2 h (`Career.Pirates.cs:78,162`), and boarding takes 2 h more. So that alternative beats every chase and makes the W4 answers redundant.
9. **[3c] D8. Scouting is at Pallas only,** slot k = 8 at Ceres and Vesta, with survey pay × 1.5. Recommend.
10. **[3d] D9. The smuggling grade** is 1.25 × e^(−Exposure/0.5), posted pay × 1.5, on routes with ≥ 1 h of risk. Recommend. "Caught by the Guard" waits for a patrol-sensor PR.
11. **Carry-forward, decision 13. The third sensor tier is not built in Phase 3.** Recommend. No stealthy NPC exists for it to find, and raiders use stock sensors by rule (decision 6). It would only push traffic contacts out. Revisit it with the patrol-sensor PR or Phase 6 raider hulls.
12. **Carry-forward, decision 14. Radiators are built in 3a.** Recommend.
13. **Carry-forward, decision 16. Revisited in 3b** (D7). Built in 3b.
14. **Carry-forward, ghost track memory.** The raider's memory is `Encounter.FixJd` (3b). The traffic ghost waits for Phase 4 `SensorPicture`, and it fades in real time. Recommend.
15. **[3e] D10. Hull money and rules.** The corvette costs 400,000 cr and the frigate hull counts as 0. The swap takes 7 days, needs `OwnsShip`, and is sold only at Earth, Mars and Ceres (`Port.HullSale`, agreed Decision 4). Picks reset to the hull's stock, and `ShipPaid` does not change. Recommend. Alternative: let a swap happen before the buyout.
16. **[3e] D12. Corvette stats:** 350.25 t structure, `Rated(3)`, facet 1/3, stock {1,2,0,0,0,1}. Recommend, or give your own.
17. **[3f] D13. The art contract** adds `Radiator_0..3` with hinge pivots. Recommend.

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
| `Career.Route.cs:43` | Raiders roll only on an active plan | Raiders roll whenever the ship is seen | 2 | Built (2a, #145) |
| `Traffic.cs`, `OpenSystemScene.Traffic.cs`, `.Signature.cs`, `.Orbits.cs:61`, `.Yard.cs`, `Signature.cs`, `UiShots.cs` | Every ship in flight is drawn | Only ships the sensors see: `Trip.Seen` from `Traffic.Sense`, a Contacts row, an own-burn map ring, two yard rows | 2 | Built (2b): QA in `CareerQA.Contacts.cs` |
| `ShipBuild.cs:5,44-78`, `Career.Heat.cs` (new), `Career.cs:46-61,182-220,630-635` | No heat state. No radiator part | `Slot.Radiators` (fixed or stowable); `Career.SinkK`, soaked once per tick and saved | 3 (3a) | Built (3a) |
| `Signature.cs:17`, `Career.Signature.cs:15,46,55`, `Threat.cs:79,120,122,132` | One cold range (10,000 km) and one 2 h hold | `RangeKm(jetW, baseKm, coldW)`; `Career.HoldS` = ρ × 2 h for a dark-ready hull | 3 (3a) | Built (3a) |
| `Career.Pirates.cs:10-19,204-246,298-308` | No lose-the-raider rule (D16) | `Encounter.FixJd`, `BreakOff.Lost`, for stowable hulls only | 3 (3b) | Built (3b) |
| `Career.cs:16-43,402-417,482-531`, `Career.Scout.cs`, `Career.Smuggle.cs` (new) | No quiet or contraband jobs | `Contract.Quiet`, `Contraband`, `Exposure`; board slots k = 8 and 9 | 3 (3c, 3d) | 3c built; 3d not built |
| `ShipBuild.cs:17-39,81,95-102`, `Career.Yard.cs:11-40`, `Career.cs:8-14,71-74`, `OpenSystemScene.cs:778`, `.Hud.cs:24`, `.CareerHud.cs:234`, `OpenSystemScene.Hull.cs` (new) | One hull | Per-hull stock, price and facet; `Port.HullSale`; a hull row in the yard; scale from hull length | 3 (3e) | — |
| `Resources/Models/StealthShip.fbx`, `ART-CONTRACT.md` | No stealth model | The user's Blender model, `Radiator_n` markers, a model rebuild on hull change | 3 (3f) | Needs art |
| `PlayerShip.json` | `PDC_0..5` unused | PDC mounts | 4 | — |
| `Ship.cs:19-21`, `:44` | `RcsAccel`, `AngAccel`, `MaxRate`, `Ve` are `const` | Per instance (hulls, torpedoes) | 5–6 | — |

## Decisions (agreed 2026-10-06)

1. **Thrust, not g.** Without it, the builder is a fuel calculator.
2. **Who owns the big hulls.** There is no crew system. The player ladder stops at the destroyer. Cruisers and larger are NPC navies that you see and route around.
3. **Where stealth goes.** Right after Phase 2, as in the table. It needs signatures only.
4. **Where the yards are.** Every port refits. Only big yards sell hulls: Earth, Mars, Ceres [design]. Phase 1 has one hull, so the hull rule waits for the second hull (Phase 3).

Phase 1 shipped after W4 of [design-world.md](design-world.md). Phase 2 is built (2a, 2b). Phase 3 is designed. PR 3a (#148) and PR 3b (#149) are built. PR 3c is built in this PR. PRs 3d–3f are not built.
