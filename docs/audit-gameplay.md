# Gameplay and enjoyment audit

Date: 2026-10-06. Code: `SpaceSim-ui` worktree, branch `hud-map` (contains `controls-rework`).
Scope: career mode only. Fun first, realism second.

**Sources**
- Code reading by three audit passes: core loop, economy, onboarding and feedback.
- I checked the key claims in the code myself.
- A `-fowcareer` simulated career run: 0 failed checks. The output is in `SpaceSim-ui/Screenshots/career-audit/career.txt`.

**Limit:** I did not play the game by hand. The window tool is blocked for the exe, and the shots run offscreen. Times-to-upgrade are estimates from the constants.

## Measured (career simulation)

| Job | Pay | Game time | Reaction mass |
|---|---|---|---|
| Tutorial: Earth → Luna | 6,000 | 4.3 h | 6.7 t |
| Cargo Luna → Earth, 50 t | 5,900 | 6.0 h | 3.6 t (≈ 120 cr) |
| Survey Bennu | 15,120 | 108 h | 144 t of 200 |
| Rescue near Earth | 10,000 | 7.6 h | 3.6 t |
| Runner intercept | 24,500 | 70 h | 96 t |

At 10k× a Mars run (≈ 3.7 game-days) takes about 30 s of real time. Waiting is not the problem. The problem is that the waiting is empty.

## Findings, ranked by effect on fun

### 1. The player has nothing to do between ENGAGE and payday — HIGH
- **Experience:** the loop is accept, set target, ENGAGE, warp up, watch.
- **Automatic steps:** the autopilot turns, burns, flips, brakes and parks. Docking and payment happen on their own.
- **No events in flight:** `Career.Tick` only docks, delivers, scans, expires and checks for a dry tank (`Career.cs:140-174`). The only random element is the job board.
- **No collisions:** the autopilot steers around every body with a radius of 100 km or more (`Ship.cs:429-481`).
- **Hand flying gets no reward:**
  - Hand input cancels the plan and forces 1× (`Ship.cs:87`, `OpenSystemScene.UI.cs:54`).
  - No job needs a precise approach. Dock range is two times the standoff, which is 38,000 km at Earth (`Ship.cs:63`, `Career.cs:211-232`).
- **Fix options:**
  - (a) Route events: a distress call to divert to, a runner you can catch on the way, cargo trouble. **(recommended)**
  - (b) An active arrival: a bonus for a close, slow dock or a quick handover.

### 2. Every plan decision has one correct answer — HIGH
- **Experience:** "Pick the slowest plan that is still on time." The planner even prints which plans are on time (`OpenSystemScene.Career.cs:352-358`).
- **Why:**
  - Time costs nothing: no wages, no life support, no early bonus.
  - Deadlines are slack 1–2 × the 0.6 g trip time + 12 h (`Career.cs:426`).
  - At 1 g, a 50 % coast is on time for about 94 % of jobs.
  - A late delivery pays 50 %, and that never beats the fuel you save.
- **Short hops:** fuel is 2–7 % of the pay. Exhaust velocity is 10,000 km/s (`Ship.cs:40`), and pay has a flat 2,000 + 40/t (`Career.cs:428`).
- **Fix:** give time a price. Use a daily crew cost, or an early-arrival bonus that pays more than the fuel saved. Then the README's "time vs mass" pitch becomes a real choice.

### 3. The first hours are an Earth ↔ Luna shuttle — HIGH
- **Experience:** "Every job from Earth goes to the Moon."
- **Why:** destinations are weighted by distance^-0.75 (`Career.cs:393-396`).
  - About 96 % of Earth cargo and passenger slots go to Luna.
  - Ganymede ↔ Callisto and Titan ↔ Enceladus pair up the same way.
  - The asteroid survey is the only way out, and it is accidental (`Career.cs:461`).
- **Fix:** fill the board by tier: always at least one local, one interplanetary and one far job.

### 4. Progression is short, upgrades only cut waits, and codex money breaks the economy — HIGH
- **Upgrade cost:** all upgrades together cost 300k. Estimate: maxed in about 50 jobs.
  - 3 g and 5 g only make trips shorter, and time is free (finding 2).
  - Sensors halve a 2 h scan that warp already makes take seconds.
  - Tanks are the only upgrade that opens new work.
- **No sink, no goal:** nothing else costs money. There is no end state.
  - `Earned` and `Failed` are saved but never shown.
- **Codex money:** first-scan sales are (400 + 600·AU) × 1.5 for small bodies (`Codex.cs:43-47`).
  - Saturn's 282 small moons are worth about 2.6M, which is 13× all upgrades together.
  - This is good, because it pulls the player outward. But it makes job pay meaningless.
- **Fix options:**
  - (a) A long goal: buy out the ship, own a port stake, or reach reputation tiers that unlock better jobs.
  - (b) Tie each upgrade to a new job type (fragile cargo, a passenger cabin above 1 g, deep-space licence).
  - (c) Cap or decay codex sale value per planet system.

### 5. Good moments feel like nothing — HIGH
- **Payday:** a 5 s toast and a 0.15 s two-beep chime (`Career.cs:262`, `ShipSynth.cs:367-370`).
  - Buying fuel plays the same chime (`Career.cs:491`). So do a scan, an achievement and an upgrade.
  - The credits number changes with no animation.
- **Docking:** gets no toast.
- **Achievements:** the first one stacks with the pay toast in the same frame.
- **Unused hooks:**
  - `Career.OnEvent`, documented "for sound and HUD hooks", has no subscribers (`Career.cs:110`).
  - The `ScanDone` sound exists and never plays.
- **Silence:**
  - The default chase view is almost silent. Ship sound plays only in the cockpit view (`OpenSystemScene.Career.cs:73`).
  - Cinematic sound defaults to off (`GameSettings.cs:43`).
  - There is no music.
- **Fix:**
  - A distinct pay sting, a credit count-up and a "job done" card.
  - Cinematic sound on by default.
  - A music bed.
  - Route all of it through `OnEvent`.

### 6. Rescues and intercepts stop 300 km from a 38 m ship — MED-HIGH
- **Experience:** the most exciting jobs end with the target smaller than one pixel.
- **Why:** `Standoff = max(3R, R + 300)` with R = 0.05 km (`Ship.cs:63`, `Career.Rescue.cs:80`). The models are true scale.
- **Fix:** a standoff of about 0.5–2 km for craft. This is a one-line change with a large visual payoff.

### 7. The tutorial teaches about 16 ideas, then the autopilot flies the job — MED-HIGH
- **Length:** 11 steps before the first pay (`OpenSystemScene.Tutorial.cs:15-16`).
- **Docking practice:** steps 5–8 are a precise manual practice: closing speed ≥ 20 m/s, under 2 m/s, under 500 m. No job ever asks for this.
- **Assist step:** it ends as soon as the rotation stops, which takes a fraction of a second. The player cannot read its 45 words.
- **Ending:** the tutorial gets no celebration at the end.
- **Fix:** teach target, ENGAGE, warp, payday. Move the manual practice to an optional lesson.

### 8. Failure is backwards: small failures cost nothing, one crash can cost everything — MED
- **Free failures:** abandoning, expiring or missing a job costs nothing. It only increases a counter that no screen shows.
- **Dry tank:** the tender is instant. You pay 20k and get 30 t.
- **Crash:** a crash reloads the last manual save. With no save, it starts a **new game** (`OpenSystemScene.cs:482-491`). There is no autosave.
- **Fix options:**
  - Autosave on dock and on job accept.
  - A deposit on accept, and a reputation score that sets the quality of the board.
  - A tender that takes time to arrive, so there is drift and tension.

### 9. The HUD reads like an instrument panel — MED
- **Labels:**
  - The main plan button says "Brachistochrone" (`OpenSystemScene.Hud.cs:459`).
  - The Economy line says "Hohmann … Δv ≈ … km/s" (`:657`).
- **Always on screen:**
  - A big "Δv LEFT … KM/S" gauge.
  - The UTC date to the second.
  - "x.xxx AU from the Sun".
  - A "JET … TW (FICTIONAL)" pill on every burn.
  - An "EPHEMERIS APPROXIMATE" pill.
- **Counts:** "Reaction mass" appears 30 times and "Δv" 21 times. The hold grid shows Normal, Anti-normal and Radial.
- **Fix:**
  - Rename the plans "Fast" and "Economy".
  - Say "fuel" and "range".
  - Show "Day N".
  - Remove the debug pills, and move the physics readouts to an optional detail view.

## Bugs found on the way (not fun issues; fix separately)

- **Soft-lock:** docked, dry tank and credits ≤ 0 leaves no way to continue.
  - `BuyFuel` buys `max(Credits, 0)/price` = 0 t (`Career.cs:487`).
  - The tender button shows only when undocked (`OpenSystemScene.CareerHud.cs:127`).
- **Wrong text:** `OpenSystemScene.Hud.cs:467` says hand flying drops to 10×. The code drops to 1×.
- **Pip count:** the tutorial ARRIVAL card shows 7 pips after 11 steps (`OpenSystemScene.TutorialHud.cs:226`).
- **Dead code (fixed, #153):** the passenger deadline floor never applied. It is removed; `CareerQA.EventsAndSaves.cs` 5b checks the 1 g margin on the board's passenger jobs.

## Suggested order

1. **Quick wins (small PRs):**
   - Payday moment and sound (#5).
   - Craft standoff of about 1 km (#6).
   - Autosave (#8).
   - Plain words on the HUD (#9).
   - The soft-lock fix.
2. **Make the choice real:** give time a price, and use a tiered board (#2, #3).
3. **Give the trip content:** route events and an active-arrival bonus (#1).
4. **A long goal:** reputation tiers or a ship buyout, and a codex exploration track with a cap (#4).
5. **Tutorial trim (#7):** after step 1, because the lessons will change.
