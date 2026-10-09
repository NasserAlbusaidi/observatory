# Ship building and combat: design

Date: 2026-10-06. Updated 2026-10-08. The four decisions are agreed (2026-10-06).
**Status:** W1–W4 are built (see [design-world.md](design-world.md)). Phase 1 is built (PRs 130, 131, 132). Phase 2 is in progress (PR 2a is in PR #145, PR 2b is built). Phases 3–6 are not built.
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
| 2 | Signatures | Detection ranges, quiet coasts | None | In progress (2a, 2b) |
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
- **No Radiator module.** It moves to Phase 3 (Phase 2, decision 14).

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

Code read on `main` at 77f72eb. Paths are under `Assets/FogOfWar/Scripts/`. Status: PR 2a is in PR #145. PR 2b is built in this PR (`signature-sky`). PR 2c is not built.

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
- **Radiator: Phase 3.** Phase 2 has no heat store and no heat limit (`MODEL-FIDELITY.md:32`).

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
- The first persistent signature state is Phase 3's heat store.

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
16. **No lose-the-raider rule.** A raider that saw the burn keeps the vector, and the chase does not change. Recommend. Phase 3 can revisit this.
17. **PR 2c is optional.** Recommend deciding after playing 2b.
18. **Housekeeping, outside this scope.** `MODEL-FIDELITY.md:29` has been stale since #130 (`Ship.cs:71` now limits by thrust). Fix it in a separate PR.

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
| `Career.Route.cs:43` | Raiders roll only on an active plan | Raiders roll whenever the ship is seen | 2 | In progress (2a) |
| `Traffic.cs`, `OpenSystemScene.Traffic.cs`, `.Signature.cs`, `.Orbits.cs:61`, `.Yard.cs`, `Signature.cs`, `UiShots.cs` | Every ship in flight is drawn | Only ships the sensors see: `Trip.Seen` from `Traffic.Sense`, a Contacts row, an own-burn map ring, two yard rows | 2 | Built (2b): QA in `CareerQA.Contacts.cs` |
| `PlayerShip.json` | `PDC_0..5` unused | PDC mounts | 4 | — |
| `Ship.cs:19-21`, `:44` | `RcsAccel`, `AngAccel`, `MaxRate`, `Ve` are `const` | Per instance (hulls, torpedoes) | 5–6 | — |

## Decisions (agreed 2026-10-06)

1. **Thrust, not g.** Without it, the builder is a fuel calculator.
2. **Who owns the big hulls.** There is no crew system. The player ladder stops at the destroyer. Cruisers and larger are NPC navies that you see and route around.
3. **Where stealth goes.** Right after Phase 2, as in the table. It needs signatures only.
4. **Where the yards are.** Every port refits. Only big yards sell hulls: Earth, Mars, Ceres [design]. Phase 1 has one hull, so the hull rule waits for the second hull (Phase 3).

Phase 1 shipped after W4 of [design-world.md](design-world.md). Phase 2 is in progress.
