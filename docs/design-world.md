# The world: design

Date: 2026-10-06. Updated 2026-10-08. Status: W1–W4 are built. W1: PR 81. W2: PRs 82, 84, 85. W3: PR 86. W4: PRs 87 and 121, with fixes 105 and 115, and the follow-ups 134, 137, 138, 139 and 140. The combat doc's Phase 1 (the shipyard, PRs 130–132) followed. Open: decision 2 (#92) and the pirate fly-through bug (#141).
Code first read on `main` at ee4c7f2, before W1. Line numbers refer to that commit; some have moved since. Scope: career mode.

**This came first.** Phases W1–W4 came before Phase 1 (the shipyard) of [design-combat.md](design-combat.md). You cannot fight nothing.

**Number tags** (same as the combat doc)
- `[code]`: I read it in the code or the game data.
- `[design]`: I picked it. Tune it.
- `[calc]`: arithmetic from the other two.

## Why the world comes first

- Today the only other ships are one runner or one derelict per job. The game adds them when you take the job (`Career.Rescue.cs:91-92`).
- Combat needs other ships, someone who wants them, and a reason to shoot.

## Setting

- The game runs at today's date, with every body where it really is (README, ephemeris). So the world is an **alternate present**.
- The one fiction stays the torch drive. Proposal: it arrived about **40 years ago** [design]. That is long enough for people born in the belt, and short enough that Earth still leads.
- The code already uses the word "belter" (an achievement, `Codex.cs:107`).

## Water is the economy

The port fuel prices already describe the politics (`Career.cs:62-70`) [code]:

| Port | Water, cr/t | Port note |
|---|---|---|
| Earth orbit | 60 | Deep gravity well |
| Luna | 35 | Polar craters |
| Mars orbit | 30 | Ice under the surface |
| Vesta | 25 | Dry rock: water is shipped in |
| Titan | 15 | Ice under the haze |
| Ganymede, Callisto | 10 | Ice |
| Enceladus | 9 | Plume water |
| Ceres | 8 | The belt's water port |

- Water flows toward the Sun. The ice ports sell, and the inner ports buy.
- One 1,000 t tanker load from Ceres to Earth is a 52,000 cr price gap [calc]. The game already has a tanker hull (`tanker Ross 128`, `Fleet.json`).
- That gap is the reason for the factions, the patrols and the pirates.

## Factions

All names are **working names**. See decision 2.

| Faction | Ports | Wants | Fleet (Phase 6 ladder) | To the player |
|---|---|---|---|---|
| Cislunar Guard | Earth orbit, Luna | Cheap water, order | Corvettes to a home-fleet dreadnought | Law: patrols, customs, bounties |
| Tharsis Company | Mars orbit, Vesta | Profit, less Earth control | Corvettes, destroyers | Hires contractors, asks few questions |
| Ice Compact | Ceres, Ganymede, Callisto, Titan, Enceladus | High water prices, safe convoys | Militia: corvettes, destroyers | Pays for escorts. In a price fight, licenses privateers. |
| Raiders | Havens on Pallas and Hektor | Cargo and ships | Corvettes, stolen haulers | A threat now. Later, a fence and a career. |
| Shipping lines | Every port | Steady trade | None: the six `Fleet.json` hulls | Post jobs. Their ships are the traffic. |

- **Cislunar Guard** is your "UN navy". It is a coast guard more than a war fleet: patrols, customs, rescue. It is the only faction with capital ships. You can see its dreadnought burn from Jupiter (combat doc, Phase 6).
- **Mars is a company town**, not a republic. The Tharsis Company owns Mars orbit and runs Vesta as its belt mine. Vesta is dry rock and buys its water (`Career.cs:66`). So Mars depends on Compact water to run its own mine. That tension is built in.
- **The Ice Compact** is the water cartel. It wants Earth to pay more. In a price fight, it licenses privateers against Earth ships. The same act is piracy to the Guard and lawful to the Compact.
- **Raider havens use real orbits:**
  - Pallas has a 34.95° tilt (`Ephem/Pallas.elements.json`) [code]. At the far points of its orbit it is 1.3–1.8 AU above or below the plane of the ports [calc]. The lanes cannot pass near it.
  - Hektor is a Jupiter Trojan at 5.2 AU, far from every patrol.

## Where it is safe: patrol reach

- To board a ship, a pirate must match its velocity and stay alongside. Boarding takes **2 h** [design]. Today's cargo hand-over takes 600 s (`CrossSeconds`, `Career.Intercept.cs:13`).
- A patrol stops a boarding only if it can get there in that time. From relative rest, its reach is d = a·t²/4 [calc].

| Patrol | 1 h | 2 h |
|---|---|---|
| 3 g | 95,000 km | 381,000 km |
| 5 g | 159,000 km | 635,000 km |

- **One 3 g patrol covers cislunar space** if a boarding takes 2 h: 381,000 km of reach against 384,400 km from Earth to the Moon.
- **In the belt, help is days away:** 53 h at 3 g over 1.8 AU, and 41 h at 5 g [calc].
- So "Earth is safe and the belt is not" comes from the physics. The game does not have to say it.
- Boarding time is the knob. It sets the size of every safe zone.
- Show patrol reach as bubbles on the map. W3 builds this look first; the torpedo reach bubbles of combat Phase 5 reuse it.

## Where pirates strike

A pirate must match velocity. In mid-course, at hundreds of km/s, that takes days of loud burning. So pirates strike where ships are slow or easy to predict:
- **Arrivals and departures** at ports that have no patrol.
- **Survey jobs:** the ship holds still for the 2 h scan (`DwellSeconds`, `Career.cs:82`).
- **Rescue jobs:** a derelict is good bait. A distress call (`RouteKind.Distress` in `Career.Route.cs`) can be a trap.
- **Long coasts,** but only for a pirate that planned days ahead. You see that pirate coming.

The danger is at the ends of a trip, not in the middle.

## NPC traffic

- **Rails for the world.** A traffic ship is a body like the ones `Rail.Craft` makes for runners (`Rail.cs`, W2a): a closed-form path (`Path`, `PathAccel`, `GM = 0`).
  - It is exact at any warp, and it costs one function call per ship per frame.
  - `IsCraft` is already true for it (`Career.Rescue.cs:17`).
- **No port structures, launches or landings.** A port is not an object today. The `Port` class has a name, a body, a note and a water price (`Career.cs:8-12`). Docking is an orbit rule: `FindDock` (`Career.cs:241`) docks a ship that coasts within dock range (2 × standoff, `Career.cs:237`) on an orbit that misses the body, or that is landed on it.
  - A traffic ship appears in the parking orbit and burns away. At the next port it stops in the parking orbit and disappears. Nobody flies a launch or a landing.
  - Station models at ports are optional art, for later. Player landing exists (`Ship.Landed`), but no port needs it.
- **Paths start and end on the parking orbit.** Before W2a, `MakeRunner` started and ended a path at the body's centre. W2a (`Rail.cs`, PR 82) puts the ends on the parking orbit.
  - Today's runners are fine: they are already in flight when the job is posted, and the job ends when they arrive.
  - Traffic would come out of the middle of the planet. Put the ends on the parking orbit (`Ship.Standoff`, `Ship.cs:64`). At Earth that is 19,100 km from the centre [calc].
- **A path is fixed between events, not forever.**
  - Between events, the path is a closed form. That is what makes it cheap and exact at any warp.
  - At an event (a pirate appears, the Guard gives an order, a port closes), the ship gets a new path from its current position and velocity. A Hermite path matches both at its ends, so the ship does not jump. Only its thrust changes.
  - In an encounter, the ship leaves its path and becomes a guided `Ship` (W4). After the encounter, it gets a new path from where it is.
- **Generated, plus a short saved list.** A schedule from (seed, date) tells each port when to launch ships on its routes.
  - A ship that never changed course comes from the schedule. The same date always gives the same ship. It is not saved.
  - A ship that changed (diverted, boarded or destroyed) is saved, with its current path. It overrides its scheduled copy.
  - Saves stay small, and QA runs repeat.
- **Rails traffic never drops the warp.** So it can stay on in the unattended suites. Route events cannot (`Career.Route.cs`, header comment).
- **Physics only for encounters.** A ship that reacts (pirate, patrol, bounty hunter) leaves its rails and becomes a guided `Ship`. The code gaps for that are in the combat doc, "Code impact": `Ship.Target` is a `Body`, and hostiles need guidance.
- **Look:**
  - Far ships show as drive-plume markers on the map.
  - Near ships use the `Fleet.json` hulls, which already carry a `registry` and a `livery` [code]. Factions add their own liveries.
  - **W2b:** each ship is a 3 px dot in its owner's colour [design], not a plume. Inside 50,000 km [design], a tag names its hull class and owner. A dot is not a button: nothing can target a traffic ship before W4. Since combat Phase 2b, only ships that our sensors see are drawn (see [design-combat.md](design-combat.md)).
  - Near models wait. A hull model shows only inside about 100 km (its true angular size), and a model pool for traffic is its own work.
- **Count:** about 100 ships in flight [design]. Measure the frame cost. `Ship.Dominant` visits every body, craft included (`Ship.cs:67`).
  - **Measured (W2b):** 108 to 117 ships in flight. The cost is in PR 3.
- **Speed:** runners peak near 0.25 g (`RunnerG`, `Career.Intercept.cs:14`). A 1 AU run then takes about 7 days [calc].
- **Limit: the path ignores gravity and obstacles.**
  - Close up, the motion is not physically correct. At Earth's parking orbit, gravity is about 0.11 g [calc], and traffic thrusts at about 0.25 g. From a distance, nobody can see it. Traffic stays on its rail even near the player: nothing hands a traffic ship to a physics `Ship`. A raider (W4) is a guided `Ship` from the start, placed at its lair, not taken from traffic (`Career.Pirates.cs`).
  - A path between ports on opposite sides of the Sun could pass close to it. Check this in QA. It may already affect runners today.
  - **Answer (W2a survey, `-fowcareer`):** 936 runner courses (9 ports, every ordered pair, 13 dates 60 days apart) [measured].
    - 68 pass within 0.3 AU of the Sun. The closest is 0.034 AU (Mars orbit → Vesta).
    - 69 cross the parking orbit of a third body: Earth 2, Jupiter 20, Saturn 47. The worst (Ganymede → Luna) passes Jupiter at 3 % of its parking-orbit distance, inside the planet.
    - So yes, today's runners were affected. From W2a, a board posts no runner whose course fails this test.

## Standing and law

- **Per-faction standing** is the reputation idea from the audit (#4a, reputation tiers; #8, a reputation score). The fields go in `CareerSave` (`Career.cs:43-54`).
- **Ports belong to factions.** Each `Port` gets an owner (`Career.cs:8`, `:60-71`).

What changes standing:

| Act | Effect | Phase |
|---|---|---|
| Job done for a faction | Up with that faction | W1 |
| Job failed or dropped | Down with that faction. Today a failure costs nothing (audit #8). | W1 |
| Smuggling, caught | Down with the Guard | Combat Phase 3 |
| Bounty paid out | Up with whoever posted it | Combat Phase 5 |
| Piracy | Far down with the victim's faction, up with the Raiders | Combat Phase 5 |
| Privateering under a Compact licence | Down with the Guard, up with the Compact | Combat Phase 5 |

What standing does:
- **High:** better jobs on the board.
- **Low:** a port refuses to dock you, or charges more for water.
- **Wanted:** patrols try to intercept you in their space. NPC bounty hunters come for you.

## Reasons to fight

| Reason | Who | Phase |
|---|---|---|
| Self-defence | Pirates attack you | W4 with no weapons (run, divert, give up cargo); combat Phase 4 on |
| Escort | The Compact or a shipping line pays you | Combat Phases 4–5 |
| Bounty | The Guard posts named raiders | Combat Phase 5 |
| Piracy | You attack traders | Combat Phase 5 |
| Privateering | A Compact licence against Earth ships | Combat Phase 5 |

The player cannot be a pirate before weapons exist. The W phases build the law, the standing and the NPC pirates. Player piracy, bounties and privateering come with weapons (combat doc, Phase 5).

## Phases

| # | Phase | Adds | New simulation |
|---|---|---|---|
| W1 | Factions | Port owners, standing, faction jobs, lore text | None |
| W2 | Traffic | NPC ships on rails between ports, on the map and in view | Rails only |
| W3 | Threat | Patrol bubbles, raider havens, a risk line in the trip planner | None: fields over the map |
| W4 | Pirate contacts | Pirates that board you. You run, divert or give up cargo. | Guided hostile `Ship` |

### W4: pirates with no weapons

- **Trigger:** a pirate contact is a route event. Add a `RouteKind` to `Career.Route.cs` (merged in PR 76), next to `Distress` and `CargoTrouble`. It uses the same tick gate, the same attended-only rule and the same call card. W4 needs no new event system.
- A pirate flies `Career.Intercept` against you.
- If it stays alongside for the boarding time, it takes part of your hold. "Alongside" is 500 km and 0.1 km/s [code] (`Career.Intercept.cs:13`).
- **The player wins by planning:**
  - Burn harder, which costs fuel.
  - Change the flip time.
  - Divert into a patrol bubble.
  - Dock before the pirate can match.
    - A docked ship is safe [design]. W3 counts the 1.5 h hold at each end of a trip as exposure, so its line counts more arrival risk than W4 gives.
    - Measured in PR 5a, for a pirate that waits at rest ahead of a ship that brakes into Vesta: a 1 g pirate cannot board before the dock, from any distance. A 2 g pirate can, from far enough out. A pirate that starts behind, already at the ship's speed, does not board before the dock either: 1 g from 2, 4 or 6 h of catch time, and 2 g from 4 or 6 h. Only a 2 g pirate from 2 h out gets alongside, for 3,578 s, which is less than a boarding (PR 134).
    - So with 1 g pirates, W3's arrival warning points at a risk that the tested starts cannot make. 5b uses 1 g as a working value and starts the pirate at rest with the port (PR 5b, "Built"). Issue #90 is decided: raiders stay 1 g and wait at rest with their lair, so boardings happen mid-route. The arrival line now says "dock first and a raider can't board" (`Threat.cs:130-131`, PR 134).
- **Or the player gives up.** The call card offers part of the hold, and the pirate leaves.
- If a patrol can reach you within the boarding time, the pirate breaks off.
- Pirates work the far tier. Far runs pay more because they are riskier. Risk joins time and mass as a price.
- **Code:**
  - `MakeRunner` gives a scripted path that cannot react (`Career.Intercept.cs:80`). A pirate needs a guided `Ship`.
  - `Ship.Target` is a `Body` (`Ship.cs:34`). The player ship is not a `Body`.
  - The autopilot reads its target only through `sys.Pos`, `sys.Vel` and `PathAccel` (`Ship.cs:326-333`). So a stand-in `Body` for the player can be enough. The spike tested this: it works, and no `Ship.Target` change is needed. Two corrections:
    - `PathAccel` takes the place of the target's gravity (`Ship.cs:333`). So it is the player's whole acceleration (d²Path/dt²), gravity included, not only its thrust. The stand-in is a quadratic from the player's last step.
    - `Plan.Brachistochrone` is not enough. It brakes on what the drive has left after it matches the target, as if that were the same in every direction. A 2 g pirate behind a 1 g ship braked at 1 g on a curve for 1.7 g, and flew past at 16 km/s. PR 5a adds `Plan.Pursue`, which brakes on what the drive has left along the braking line.

### QA

- **W1:** standing survives a save and load. An old save loads with neutral standing.
- **W2:**
  - The same seed and date give the same traffic.
  - Traffic stays inside a frame budget at 1M× warp [design budget].
  - No traffic path passes inside a planet or too near the Sun.
  - Every traffic path starts and ends on a parking orbit, never inside the body.
  - A diverted ship keeps its new path through a save and load. Its scheduled copy does not come back.
- **W3:** a belt route shows more risk than a cislunar route.
- **W4:**
  - A pirate in reach of a patrol breaks off.
  - A full burn escapes a pirate of the same drive. PR 5b tests it this way: a 1 g pirate 100,000 km behind, at rest with the ship. The autopilot's 1 g full burn is not boarded in 12 h. The same start with the ship coasting is boarded. This replaces "a pirate that starts outside a set range".
  - Route events stay off in the unattended suites.

## Build plan

Six PRs, one feature each, plus one spike. W4 is two PRs (5a and 5b). Each PR branches from `main`, or from the PR before it while that one is in review. Each PR passes `-fowcareer` with 0 FAIL before review.

### PR 1: `world-factions` (W1). Merged in PR 81.

- `Career.Factions.cs` holds the factions, with their working names and blurbs, in one table. A rename (decision 2) is one edit there.
- Each `Port` gets an `Owner`.
- A job belongs to the owner of the port that posts it (`Contract.From`). Nothing new is saved on the job. A distress rescue near a survey body has no owner.
- **Standing** per faction [design]:
  - A job done on time: +1. A late job: 0.
  - A failed job (expired, abandoned, tank dry, intercept missed): −2. A failure costs more than a job earns (audit #8).
  - Standing stays between −20 and +40.
- **Tiers** [design]:

| Standing | Tier | Board pay |
|---|---|---|
| −5 or less | Distrusted | −10 % |
| −4 to 4 | Neutral | ±0 |
| 5 to 14 | Known | +5 % |
| 15 to 29 | Trusted | +10 % |
| 30 or more | Valued | +15 % |

- The pay factor applies once, when the port makes its board (`RefreshBoard`). Neutral is exactly ×1, so a new career pays the same as today.
- A tier change makes a note and rebuilds the day's board at the new pay.
- **Save:** an array in `CareerSave`. An old save loads as neutral.
- **UI:** the owner and its blurb under "Docked at", the owner on the job cards, and a standing list under "Ship's business". Both shells get it (UI Toolkit and `-fowlegacyui`).
- **QA:** a new `-fowcareer` section. Standing goes up after a delivery and down after an abandon or an expiry. A late job does not change it. It also checks the tier note, the board pay, a save round trip and an old save.
- The Raiders join the faction list in W3, with their havens. They own no port in W1.

### PR 2: `rail-paths` (W2a). Merged (PR 82).

- Move the Hermite path out of `MakeRunner` (`Career.Intercept.cs:77`) into a shared builder. Traffic is its second use.
- Paths start and end on the parking orbit (`Ship.Standoff`), not at the body's centre.
- QA: no path passes inside a planet or too near the Sun. Run this on today's runners first. It answers the open question in "NPC traffic".
- The test (`Rail.Check`): the course stays 0.3 AU or more from the Sun, and outside the parking orbit of every body but its two ends. Moons under 100 km radius are left out. Both limits are working values [design].
- One visible change: a board posts no runner whose course fails the test.
- Cost: one check takes about 21 ms [measured]. A board tries at most one runner per refresh. Traffic would call it far more often, so W2b does not run the full check. It runs a cheap one at each departure (PR 3).

### PR 3: `traffic` (W2b). Merged (PR 84).

- `Traffic.cs`: a schedule from (seed, day) gives the trips: from, to, departure, arrival, hull and faction.
- Only ships in flight are bodies. Add them at departure and remove them at arrival, as `Career.Rescue.cs:91-92` does.
- At 1M× warp, one frame can cover many departures. Step through the schedule one day at a time.
- Ships show as dots in their owner's colour. Near models wait (see "Look" in "NPC traffic"). Since combat Phase 2b, only ships that our sensors see are drawn (see [design-combat.md](design-combat.md)).
- Measure with `-fowprof` at 1M×. QA: the frame budget, and the same seed and day give the same traffic.
- The saved list of changed ships waits for W4. Before W4, nothing changes a traffic ship.
- **Built:**
  - Departures a day: 2 from Earth orbit, 2 from Mars orbit, 1 from each other port [design]. A ship picks its destination with weight distance^-0.25 [design]. It flies the runners' drive (0.25 g).
  - A trip takes about 10 days, so 108 to 117 ships are in flight [measured].
  - A trip's course is built and tested once, at departure, not when its day is drawn. A reset (scene start, load) then builds only the ships in flight.
  - The cheap test (`Rail.CheckEnds`) covers the Sun, and the planet that each moon end orbits. It reads that planet at every 8th sample and joins the points with cubics. The error is under 1 km [calc].
  - It refuses 37 of 453 trips (8 %) [measured].
  - QA gate: of 400 trips that pass the cheap test, the full test (`Rail.Check`, every body) refuses none [measured].
  - A first try with 128 samples missed one pass near Saturn. A 20-day trip then has a sample every 3.75 h. At 0.25 g, a chord can be off by up to about 56,000 km [calc]. Saturn's parking orbit is 175,000 km [calc].
  - **Fix after the merge** (PR 85): a reset looked back 25 days for ships in flight. Jupiter to Saturn across the Sun takes up to 27.6 days (15.6 AU) [calc]. So a reset dropped those ships for their last days. The lookback is now 30 days. W3's QA run found it: a 25.3-day hauler.
- **Frame budget:** traffic costs 1 ms a frame or less on average, at 1M× and 60 fps [design]. That is a tenth of the 10 ms integration budget (QA-003). A reset costs at most 200 ms [design].
  - Profile (`-fowprof`, 1M×, about 37 fps): tick 10 to 11 ms (mostly the integration budget), late 8 ms, gui 1 ms. Traffic is 0.74 to 0.79 ms of that [measured].
  - Before the fixes, traffic was 2.1 ms, the third-largest item [measured].
  - Stepped one 1M× frame (60 fps) at a time: 0.36 ms on average, 3.5 ms at worst. A reset: 26 ms. One trip: 0.16 ms [measured].
  - The draw costs about 0.15 ms a frame at any warp [measured].
  - The cost follows game time per frame. At 1M× and 30 fps, it is double the 60 fps figure.

### PR 4: `threat-map` (W3). Merged (PR 86).

- Fixed patrol bubbles around guarded ports, sized by d = a·t²/4.
- Risk from the raider havens and the belt, weighted toward the ends of a trip.
- One risk line in the trip planner, and the bubbles on the map. No encounters yet.
- **Built:**
  - `Threat.cs`. Boarding takes 2 h. This is decision 4, still open: W3 uses it as a working value. A patrol's reach is a·t²/4: 381,000 km at 3 g, 254,000 km at 2 g [calc].
  - Patrols [design]: 3 g at Earth orbit, Luna and Mars orbit; 2 g at Ceres. The Company leaves Vesta to itself. The Compact guards only Ceres. The outer moons have no patrol.
  - Where raiders work [design]: all of the main belt (2.1 to 3.3 AU, the same band as `Belt.cs`), and near a haven. Near a haven, the weight falls from 1 at the haven to 0 at 0.5 AU from it.
  - Speed: a 1 g raider matches 70.6 km/s in one boarding time [calc]. A ship's weight falls by e for each 70.6 km/s of its speed along the trip.
  - Exposure, in hours: the time a ship is in raider space, outside every patrol's reach, times the speed weight. Inside a patrol's reach, it is 0.
  - Only the hold at the target counts, half the parking time (1.5 h) and no more than the track hold (combat Phase 2). The hold at the start is gone. Where most of the hours fall, on departure or arrival, goes into the line.
  - Tiers [design]: under 0.1 h none, under 1 h low, under 3 h moderate, else high. Moderate and high show as a warning.
  - Both trip planners show one line. Example: "Raider risk: high. 3.5 h slow and seen (a burn, or 2 h after one), out of patrol reach in raider space, most of it on arrival: dock first and a raider can't board."
  - The map: a ring for each patrol's reach in its owner's colour, a red ring for each haven's zone, and faint red rings at the belt's edges. The haven and belt rings fade in close views, as the orbits do.
  - A patrol ring is under a pixel at the map's opening zoom (381,000 km seen from 6 AU). It shows when you zoom in on a port. The rings keep their true size.
  - The Raiders join the faction list. They own no port, so no board and no standing line shows them.
  - QA [measured], all at 1 g unless stated. Mars orbit to Vesta: 3.50 h, high, all on arrival. Earth to Luna: 0. Mars orbit to Ceres (a 2 g patrol): 0.75 h, low. Mars orbit to Vesta at 3 g: 2.17 h, moderate.
  - Of the 3.5 h to Vesta, 1.5 h is the hold at Vesta. A harder burn cuts only the rest. A patrol at Vesta would cut both.

### PR 5: `pirates` (W4)

- **Spike first,** on a short throwaway branch: the stand-in `Body` for the player (W4, "Code"). If a pirate `Ship` can fly `Plan.Brachistochrone` at it and match velocity, W4 is mostly event and UI work. If not, W4 starts with the larger `Ship.Target` change.
  - **Done** (throwaway branch, not merged). The stand-in works: a 1 g pirate is alongside a coasting ship at 6650 s and holds for hours [measured]. `Plan.Brachistochrone` fails against a ship that burns away from it (W4, "Code"). So W4 is two PRs: 5a (guidance) and 5b (the encounter).

#### PR 5a: `pirate-guidance`. Merged (PR 87).

- **Built:**
  - `Plan.Pursue` (`Ship.cs`). It matches the target's acceleration first. Steering and braking then get all the drive has left along their own line (`Ship.Spare`). The braking curve takes 0.85 of that [design], as `Plan.Brachistochrone` does.
  - The player's plans do not change: one guarded return in `Guide`, and no shared code changed. The plan pickers list their plans by hand, so the player cannot pick `Pursue`.
  - `Pursuit.cs`: the stand-in and the lockstep. The scene steps the player first. The pirate then steps over the same span, in steps of min(30 s, 0.05·d/v), and 0.5 s or more.
  - `PursuitCheck.cs` (menu "Fog of War/Pursuit check"): 48 runs at steps from 1/60 s to 50 s. The pass rule was committed before the first run. The exit code is the verdict. `-fowcareer` section 13 runs a fast subset.
- **Results** [measured]. Each catch time is within 1 % of its closed form [calc], except the climb case under "Open for 5b".
  - A ship coasting near Vesta, a 1 g pirate 100,000 km behind: alongside at 6650 s, then holds 22,150 s.
  - A ship in orbit at Pallas, a 1 g pirate 50,000 km out: alongside at 4700 s, then holds 31,300 s.
  - A 1 g burn, a 2 g pirate 50,000 km behind: alongside at 3764 s. It comes no nearer than 0.55 km, so it does not fly through.
  - A 1 g burn, a 1 g pirate behind: it cannot close on a hand burn. Behind the autopilot it closes 1.75 % in 6 h, on the small reserve that the autopilot keeps back.
  - Arrival: a ship brakes into Vesta from 600,000 km at 100 km/s and docks at 12,005 s. A 1 g pirate cannot stay alongside for 2 h before the dock, from any start at rest ahead of the ship (at most 6498 s [calc]). A 2 g pirate stays 5585 to 6928 s from 150,000 to 300,000 km out, and 7676 s from 375,000 km (it boards first).
  - Burn and flip: a 3 g pirate holds a ship that burns 1 g or 2 g by hand and flips every 30 min for 6 h. A 3 g burn escapes. At the 1× step, a 2 g burn breaks contact once, at the first burn.
- **Open for 5b** (found in 5a; all closed now):
  - The braking curve counts on the target's present thrust. If the ship cuts its drive under 10,000 km, the pirate passes it at 10.4 km/s. If the ship flips and burns at it, 17.4 km/s. The pirate catches it again 1,000 to 3,500 s later. The autopilot does the same at turnover, and at the brake start of a coast trip [inference]. See decision 6 (issue #89). Decided: keep today's law (`Ship.cs:495`). PR 140 measured the autopilot's brake start: the pirate passes at 32 to 128 km/s and never gets alongside before the dock.
  - The scene's coast step (`OpenSystemScene.Coast`) moves the conic by the raw chunk, but the clock moves by the rounded chunk. A coasting ship then drifts about 11 m/s from the pirate's clock at 1×. A planned ship also moves 1 ms each chunk that the clock does not count (about 19 m). Both affect the player's own coasting. Then rerun the check at 1/60 s. Fixed in PR 105 (issue #88).
  - `Pursue` has no lead point. It uses `Brachistochrone`'s hold zone (1.3 km, 0.5 km/s). Issue #97, fixed in PR 115: `Pursue` has its own capture zone and body clearance.
  - If the pirate's line passes inside a body's safe radius, it switches between climbing and braking, and the catch takes up to 19 % longer. Issue #97, fixed in PR 115.

#### PR 5b: `pirates`. Merged (PR 121).

- The plan:
  - `RouteKind.Pirate` in `Career.Route.cs`. The call card offers three answers: run, divert, or give up cargo.
  - In an encounter, the pirate moves with `Ship.Step`. The warp is capped, as `TransferInProgress` does today.
  - Boarding takes part of the hold. A patrol in reach makes the pirate break off. After the encounter, the pirate goes back on rails, and the saved list of changed ships starts here.
- **Built:**
  - `Career.Pirates.cs`: the encounter. It holds the pirate (`Pursuit`, on `Plan.Pursue`) and a proxy body in `sys.Bodies`, so the target card, `SetTarget` and Divert see the pirate as a craft. The proxy has no gravity and is no obstacle. The scene flies the pirate over each span that the player's substep used.
  - The call: raiders strike at 0.2 calls per exposed hour [design] (combat Phase 2: per hour while the ship is seen, hand-flown or not) × the W3 presence × e^(−v / 70.6 km/s), v the ship's speed against the port the pirate waits at. The rate is 0 in a patrol's reach. The approach cut-off of the other calls does not apply. One encounter at a time, and no other call during a chase. Jobs: cargo, passengers and survey.
  - The start: at rest with a port, the job's target on the second half of the trip and its posting port on the first. It is on the line from that port through the ship, as near the port as the catch time allows. The catch time, from a closed form, is 2 to 6 h [design]. The start is out of every patrol's reach and at least 500 km (the intercept range) from the ship. If no start fits, no call comes.
  - The answers: Run (the card closes, and the chase goes on). Divert (the Fast plan to the patrolled port that the ship reaches first at full burn). Give up 25 % of the job's pay, and of its tonnes for cargo [design]. After Run, Divert and Give up stay on the job's card and in the classic panel.
  - Boarding: 2 h alongside takes 50 % [design]. Alongside is the intercept rule (500 km and 0.1 km/s, at any throttle), and any break resets the clock. No standing change, and the job stays on.
  - The ends: a dock (the job pays first), a landing, the job's end, a patrol's reach, a boarding, Give up and a load. The warp is capped at 1,000×, and at 100× within 10,000 km or alongside [design].
  - Both shells: the UI Toolkit card with three buttons and a live line (range, closing speed, boarding %), the classic and `-fowlegacyui` panel, a HUD pill, a Hostile target kind, a red dot and tag in the view, and a flight-log field. Tab (the cycle key) reaches the raider.
  - QA: `-fowcareer` sections 14 and 14b, and a check in 5c that the unattended real roll makes no raider call.
- **Changed from the plan:**
  - After an encounter, the pirate leaves at once, with a note that names the nearest haven. It does not go back on rails, so no list of changed ships is saved (D9).
  - The warp cap is its own (`Career.WarpCap`), not `TransferInProgress`.
  - A chase is not saved. A load drops it and says so.
- **Results** [measured]:
  - In the belt, at rest with Vesta and out of patrol reach: 0.2 calls an hour. At 70.6 km/s against Vesta: 0.074.
  - A start at rest with Vesta, about 1,000,000 km from the ship: catch 5.61 h, the same as 2√(d/a).
  - A 1 g full burn against a 1 g pirate 100,000 km behind: never alongside in 12 h (the range opens to 96,492 km). The same start, coasting: alongside at 6650 s, boarded at about 3.8 h.
  - Divert from the belt: Earth orbit is about 84 h away at full burn, against a catch time of about 5.6 h. Divert gives no real escape there (issue #90, accepted as is).
- **Decided and built after 5b:**
  - **Drive and start** (#90, PR 134). Raiders stay 1 g and wait at rest with their lair. A loaded stock ship (0.83–0.95 g) is boarded on a full burn, so a bigger drive is the answer. Divert from the belt stays weak (84 h to Earth orbit against a 5.6 h catch).
  - **Braking law** (#89, PR 140). Keep today's law. PR 140 is a report and changes no rule.
  - **Boarding time** (#91). 2 h stays (`Threat.BoardingSeconds`).
  - **Give up a chase** (#98, PR 137). If the catch time stays over 6 h for 1 h (`CatchMaxS`, `GiveUpS`), the raider breaks off with the note "Outrun".
  - **Save during a chase** (#99, PR 138). A save is refused ("Can't save during a chase"). A boarding and a raider's exit autosave. A load still drops the chase.
  - **Target card** (#100, PR 139). With a raider as the target, the gauge reads CONTACT, and the raider's answers replace ENGAGE and the trip lines.
- **Open:**
  - #141: a pirate that overshoots at speed can pass inside Vesta (149 to 257 km under the surface in PR 140's S6 runs). The surface guard misses a fast run-out.
  - #92: final faction names (decision 2).
  - Not decided (#90, first comment): a pilot with the autopilot off is never raided, because the roll needs an active plan (`Career.Route.cs:43`). Combat Phase 2 (signatures) settles it in PR 2a: raiders see the burn, not the autopilot.

### Risks

1. **The W4 spike.** Do it early, before W2 if possible. Its result sets the size of W4. Done: the stand-in works, with a new plan for the pirate (PR 5a).
2. **W2b frame cost** with about 100 extra bodies at 1M× warp. `Ship.Dominant` visits every body (`Ship.cs:67`).
   - W2b measured the direct cost: 0.74 to 0.79 ms of a 1M× frame (PR 3). The cost of the extra bodies in other loops, such as `Ship.Dominant`, is inside tick and late. It is not measured on its own.

## Decisions for you

1. **When the torch drive arrived.** Proposal: about 40 years ago [design].
2. **Final faction names.** The trope (an Earth navy, a lawless belt) is generic. The Expanse's names (UN Navy, MCRN, OPA, Belter creole) are not, and the UN is a real organization. Pick final names before any text goes in front of players.
3. **Mars as a company town.** Proposal: yes. It makes Mars different from Earth, from the Compact, and from the Expanse.
4. **Boarding time.** Decided: 2 h (#91). It sets the size of every safe zone (`Threat.BoardingSeconds`).
5. **Traffic count.** Proposal: about 100 ships in flight [design], after a frame-cost check. W2b runs 108 to 117, inside the frame budget [measured].

6. **How a pirate brakes.** Today it brakes on the spare drive that the target's present thrust leaves it. A ship that cuts its drive or flips near the pirate makes it fly past, and it must come back (PR 5a). The other choice: brake as if the target could cut its drive at any time. Then nothing flies past, but a 1 g pirate brakes at 1 g, not 1.85 g, against a ship that brakes into port. Decided: keep today's law (#89). "Change the flip time" is one of the player's answers in W4.

W1 started with the working names. Set decision 2 before this text goes into a release: W4's call card names the Raiders. Decisions 4 and 6 are decided (#91, #89). Decision 5 is measured. Decisions 1 and 3 are still proposals. Decision 2 is issue #92. The combat doc's Phase 1 (the shipyard) followed W4.
