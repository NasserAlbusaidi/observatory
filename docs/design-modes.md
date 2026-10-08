# Ship builder, sandbox and battle: design

Date: 2026-10-06. Updated 2026-10-08. Status: draft. The four decisions below are still open.
**What happened since** (read on `main` at 77f72eb):
- The world track finished first (W1–W4), so the reorder below did not happen.
- M2 and M6 shipped together as the **career** yard (PRs 130–132). There are no build files and no sandbox yard. The yard refuses in the QA sandbox (`Career.Yard.cs:17`). `AngAccel` and `MaxRate` are still `const`.
- M4 shipped as W4's guided pirate (`Pursuit.cs`, `Plan.Pursue`). It serves the career chase only.
- M1 (game modes), M3 (sections) and M5 (battle) are not built. `Career.Sandbox` is still only the QA switch.
Code read on `main` at bfa735e. Line numbers refer to that commit.

This doc builds on [design-combat.md](design-combat.md) and [design-world.md](design-world.md). It does not change their agreed decisions. It changes one thing: **the order** (see "What this changes").

**Number tags** (same as the other two docs)
- `[code]`: I read it in the code.
- `[design]`: I picked it. Tune it.
- `[calc]`: arithmetic from the other two.

**Assumption.** "Modular" means you **see** the modules on the hull, not only in a stats panel. If you mean stats only, combat Phase 1 already covers it, and section "Visible sections" drops out.

## Pitch

- **Builder:** a ship is a stack of sections. Each section is mass, and you can see it.
- **Sandbox:** the open world with no economy. Build anything, start anywhere, on any date from 2000 to 2100.
- **Battle:** an arena in real space. Pick two builds, a place and a start geometry. Fight.
- Battle mode is the **test bench** for the combat doc. Each combat phase plays there first, then goes into the career.

## What this changes

- The combat doc puts its Phase 1 (shipyard) after W4: "you cannot fight nothing".
- That order is a priority call, not a code need:
  - Phase 1 (`ShipBuild`, thrust not g) reads no faction, traffic or pirate code.
  - In sandbox, balance does not matter. So the builder can ship before the career yard is tuned.
  - In battle mode, the opponent is set up by hand. So combat Phases 4–5 get a harness without factions or traffic.
- **So two tracks run side by side:**
  - World track (other session): W1 → W4.
  - Modes track (this doc): modes → builder → arena → weapons in the arena.
  - Career gets the yard and the weapons when both tracks meet.
- **One shared piece:** the guided hostile `Ship` (W4 spike, design-world.md "PR 5"). Battle mode needs it too. Build it once, on its own PR. Whichever track gets there first does it.

## Three modes

| | Career | Sandbox | Battle |
|---|---|---|---|
| Start | Earth, today, 25,000 cr | Any port or body, any date 2000–2100 | Setup screen |
| Jobs, pay, standing | Yes | No | No |
| Yard | Docked, costs credits and days | Anywhere, free, instant | Setup screen only |
| Reaction mass | Real | Toggle (default real) | Real |
| Docking | Yes | Yes | No |
| Codex | Logs and pays | Logs, no pay | No |
| Route events | Yes | No | No |
| Save | Career slot | Sandbox slot | No save; replay (F8 recorder) |

### Today's `Career.Sandbox` is not this

`Career.Sandbox` is a QA switch. `-fowqa` sets it (`OpenSystemScene.Career.cs:26`) and `CareerQA` flips it (`CareerQA.cs:524`, `:585`). What it gates [code]:

| Gate | Where | QA switch today | Player sandbox |
|---|---|---|---|
| Unlimited fuel, cargo 0 | `Career.cs:158-159` | On | Fuel: toggle. Cargo: 0 |
| No docking | `Career.cs:174` | On | **Off.** Docking stays, so the free yard has a "docked" state to show |
| Scan pays nothing | `Career.cs:225` | On | Same |
| No ship buyout | `Career.cs:643` | On | Same (buyout hidden) |
| No standing | `Career.Factions.cs:56` | On | Same |
| No route events | `Career.Route.cs:36` | On | Same |

- So the player sandbox is **not** the QA switch with a menu entry. The docking gate differs, and the start and yard rules are new.
- **Proposal:** add `GameMode { Career, Sandbox, Battle }` to the scene and the save. Keep `Career.Sandbox` as the QA switch and do not rename it. Sandbox mode sets its own gates.
- **Saves:**
  - `CareerSave` gets `mode`. `JsonUtility` gives a missing field 0, so 0 must be `Career`. Old saves load as career.
  - An old QA save with `sandbox = true` loads as career with the QA switch on, as today. No old save turns into a player sandbox.
  - Sandbox gets its own key next to `fow-career-v1` (`OpenSystemScene.Career.cs:580`). A sandbox never overwrites a career.

### Menu

- The live menu is the UI Toolkit shell (`UiShell.Menus.cs:61-72`). The IMGUI menu (`FogOfWarApp.cs:289-296`) is the `-fowlegacyui` fallback (`UiShell.cs:10`). Both get the entries, as with W1.
- "Start" becomes "New career". Add "Sandbox" and "Battle" under it.
- "Continue" names the slot it opens.

## Modular ships

### Two layers

1. **Stats:** `ShipBuild` from combat Phase 1, as written: hull, modules, thrust not g (Decision 1). Nothing here changes it.
2. **Visible sections:** the build also sets the model. This doc adds this layer.

### Visible sections: a spine, not free assembly

- A ship is a **stack of sections along its thrust axis**, nose to tail:

| Section | Carries | Stats module (combat Phase 1) |
|---|---|---|
| Command | Cockpit, sensors | Sensors |
| Cabin | Passenger seats | Cabin |
| Hold | Cargo | Hold |
| Tank | Reaction mass | Tank |
| Drive | Thrust | Drive |
| Radiator collar | Heat rejection, folds flat | Radiator (Phase 2) |

- Each section comes in 2–3 sizes. Size sets the stats and the length.
- **Hardpoints** sit on sections: `PDC_n`, later `Tube_n`. A section brings its own markers. Weapons can only go where a section has a mount.
- **The hull** is the frame. It sets the section count, the largest size and the largest drive. Decision 2 still holds: the player's top frame is the destroyer.

**Ruled out: free KSP-style assembly.**
- `Ship` is a point mass with one thrust axis (`Ship.cs:16`). There is no structure to simulate. Parts off the axis would only look different.
- The art contract is one FBX per model with named empties (`ART-CONTRACT.md`). A stack of sections fits that contract: each section is one FBX with two more empties.
- A 52 m ship is smaller than a pixel at 500 km (audit #6). The look matters in the yard, near ports and in the last minutes of a fight. A stack reads clearly there.

### What the stack changes in the physics

- **Mass:** sum of sections. Already in combat Phase 1.
- **Length:** sum of sections. Today it is fixed at 52 m (`PlayerShip.json`).
- **Turn rate.** Today `AngAccel` (1.5 rad/s²) and `MaxRate` are `const` (`Ship.cs:20-21`). The combat doc makes them per ship in Phases 5–6. The stack gives the rule:
  - Moment of inertia: about I = m·L²/12 (a uniform rod) [design].
  - The stock ship at 500 t and 52 m: I = 1.13 × 10⁸ kg·m² [calc]. For 1.5 rad/s² the wheels give 1.7 × 10⁸ N·m [calc]. That is the stock wheel torque.
  - α = torque / I. A ship twice as long at the same mass turns 4× slower [calc]. A heavier, longer ship flips later, and the planner must know.
  - The wheels can be a module later. Not in the first cut.
- **Knock-on to combat Phase 6:** the hull ladder scales mass with length³ and railgun reach at about 1 km per metre. Player ships now vary in length. Recheck that table when sections land. I did not recompute it here.

### Art

- New FBX per section and size, for example `Models/Sections/Tank_M.fbx`.
- Each has two empties, `Fore` and `Aft`. The game joins `Aft` of one section to `Fore` of the next.
- The Command section has `Nose` and `Cockpit`. The Drive section has `Engine`. The scene finds them as today (`Models.AlignMarker`, `OpenSystemScene.Career.cs:523`).
- **Fallback:** a missing section draws as a primitive cylinder of the right length (as `Models.Spawn` does today). The stock build keeps `PlayerShip.fbx` until its sections exist. So code lands before art.
- Hull texture: one atlas per section, the same `ShipPBR` rules.
- Add this to `ART-CONTRACT.md` when the art pass starts.

### The yard screen

- Camera: orbit the ship at rest. Reuse the photo-mode free camera.
- Left: sections and sizes. Right: the readouts from combat Phase 1 (dry mass, g full and empty, Δv, jet power, price, a trip check).
- Add one readout: **flip time**, from the turn rule above.
- Career: credits and refit days. Sandbox: free, instant. Battle: on the setup screen.
- **Builds are files.** Save a build by name (JSON). Sandbox and battle read the same files. Career keeps its own build in the save.

## Sandbox

- **Start screen:** a port or any body, a date, a build. Defaults: Earth orbit, today, the stock build.
- **In flight:** the yard opens anywhere, at any time. The world does not stop for it.
- **Toggles:** unlimited reaction mass, time warp cap. No more in the first cut [design].
- **Codex:** logs bodies and scans, pays nothing. Each slot has its own codex [design]. A shared codex would make career discoveries free.
- **Achievements:** off in sandbox [design].

## Battle

### Setup

- **You:** a saved build.
- **Opponent:** a saved build, or a preset (the six `Fleet.json` hulls, later warships). One opponent in the first cut.
- **Place:** deep space near a body (default: 50,000 km off Ceres) or low orbit (Earth, Mars). The ephemeris and gravity are real.
- **Start geometry:** range, closing speed, approach angle. Presets: "tail chase", "head-on pass", "matched velocity, 1,000 km".
- **Win:** the other ship's drive is dead or its tank is dry, or it leaves a set range. **Draw:** time limit.

### The opponent needs a guided `Ship`

- Today there is one `Ship`, made in `OpenSystemScene.cs:370`. `Ship.Target` is a `Body` (`Ship.cs:34`).
- Other craft (derelicts, runners) are `Body` objects. A runner has a scripted `Path` (`Ephemeris.cs:93`, `Career.IsCraft`, `Career.Rescue.cs:17`). It does not react to you.
- **Option A, scripted:** the opponent is a `Body` with a `Path`, like a runner. Cheap. It cannot react, so it is a target drone, not a fight.
- **Option B, guided:** a second `Ship`, stepped by the scene. A stand-in `Body` follows the player so the opponent's autopilot can target it. This is the W4 spike.
- **Recommendation:** B. Do the spike once, as its own PR, and both tracks use it. Use A only as a target drone for the first arena build if the spike is late.
- **Scene work for B:** the HUD, camera and sound assume one ship. The opponent needs a model (as `LateRunner` does, `OpenSystemScene.Career.cs:506`), a contact marker, and its own step in the substep loop.
- **Torpedoes are not `Ship`s.** There will be many. They need a light guided struct with closed-form steps (combat Phase 4: per-frame samples miss passes).

### Battle versions

| Version | Adds | Combat doc phase |
|---|---|---|
| B0 | Chase and evade. No weapons. Win by matching velocity and holding (as intercept). | W4 spike |
| B1 | Signatures: the opponent sees your burn, you see its burn | 2 |
| B2 | PDCs, armor, damage as mass loss | 4 |
| B3 | Missiles, torpedoes, reach bubbles | 5 |

- Each version is playable alone.
- Each version gets a scripted QA run, `-fowbattle`, in the style of `-fowcareer`. It flies set fights and checks the combat doc's `[calc]` numbers: PDC spread, torpedo reach, flip time.

### Time warp

- Most of a fight is the approach. It runs at warp, as in combat Phase 4.
- Warp drops to 1× at a set range [design: 2,000 km], with the same rule as `DropWarp` (`Career.Intercept.cs:36`).

## Build plan

One PR, one feature. Each passes `-fowcareer` with 0 FAIL. Career play does not change until M6.

| PR | Branch | Adds | Touches |
|---|---|---|---|
| M1 | `game-modes` | `GameMode`, menu entries (both shells), sandbox slot, sandbox start screen | `Career.cs` save, menus, scene start |
| M2 | `ship-build` | `ShipBuild`, thrust not g (Decision 1), build files, yard screen, sandbox only | `Career.Apply`, `Ship.AMax`, `Ship.cs` turn constants |
| M3 | `ship-sections` | Section stack model with fallback primitives, per-ship turn rate | Ship model load (`OpenSystemScene.cs:340`) |
| M4 | `hostile-ship` | The W4 spike: a second guided `Ship` | `Ship.Target`, scene step loop |
| M5 | `battle-arena` | Battle setup, B0, `-fowbattle` | New scene file |
| M6 | `career-yard` | The yard in career (combat Phase 1 rules, refit days, save migration) | After W4, or when you call it |

- **Overlap with the world track:** W2 (`rail-paths`) touches `Career.Rescue.cs`, `Career.Intercept.cs` and `Ephemeris.cs`. M1–M3 do not. M1 touches `CareerSave`, as W1 did. Rebase M1 on main after W2 merges if both are open.
- M4 touches what W4 needs. Agree with the other session who builds it.

## Decisions for you

1. **Visible sections or stats only?** Recommendation: sections. The rest of the doc assumes it.
2. **Battle before the world finishes?** Recommendation: yes. It is the reorder above. Without it, all combat waits for W4.
3. **Battle location: real space or empty space?** Recommendation: real space. The planner, gravity and orbits are the game. An empty arena throws that away.
4. **Separate codex per sandbox slot?** Recommendation: yes. Otherwise sandbox gives free career discoveries.
