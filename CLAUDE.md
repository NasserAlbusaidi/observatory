# Fog of War (working name): project rules

An open-world career game in a true-scale solar system, in the style of *The Expanse*. You fly a torch ship, take jobs, and trade time against reaction mass. Raiders, stealth and combat sit on top of the trip planner. Unity 6 (`6000.6.4f1`), Windows, C#.

The Observatory physics scenes are leaving this repo (ADR 0001). Do not add features to them.

## Where things are

- `docs/adr/`: the project-wide rules (index below). Read them before you design or review a change.
- `docs/design-combat.md`, `docs/design-world.md`, `docs/design-modes.md`: the design, the numbers, each phase's build plan and decisions.
- `docs/DEVELOPING.md`: build, flags, suites, layout. `docs/MODEL-FIDELITY.md`: the physics assumptions.
- `ART-CONTRACT.md`: the model markers the code needs (`PDC_n`, `Radiator_n`, …).
- Gameplay code: `Assets/FogOfWar/Scripts/OpenWorld/` (the career, headless) and `Assets/FogOfWar/Scripts/Scenes/OpenSystemScene*.cs` (the scene that draws it). QA: `Assets/FogOfWar/Scripts/CareerQA*.cs`.

## Rules (ADRs)

| # | Rule |
|---|---|
| 0001 | The game is the open world; the Observatory scenes move to their own repo. Name later. |
| 0002 | The career decides every outcome; the scene only draws it (`Career.OnEvent`). |
| 0003 | Combat resolves in closed form at any warp: one draw a missile, at impact. |
| 0004 | Combat draws from `combatRng` only; route and board draws must not move. |
| 0005 | One bearing rule (`PointDefense.Bears`, working mounts) for rows, cards and tracers. |
| 0006 | In a chase, the warp follows the time to the next event; 1× before each impact. |
| 0007 | Every career surface works in both UI shells. |
| 0008 | A chase is not saved; a load ends it. |
| 0009 | Thrust, not g: every part is mass. |

A change that breaks a rule needs a new ADR first. See `docs/adr/README.md` for when and how to write one.

## Design docs

- Numbers carry a tag: `[code]` (read in the code), `[design]` (picked; tune it), `[calc]` (arithmetic from the other two). Numbers live in the design docs, not here and not in ADRs.
- Each phase numbers its decisions D1, D2 … again. Name the phase with the number ("Phase 4 D10").
- Before a phase is built, its doc gets a touch list and a QA list. After it is built, the doc says what was built and what was left out.

## Working in parallel

- Several sessions work at once. Each works in its own git worktree, for example `D:/Unity/Projects/SpaceSim-<name>`. Do not edit the main checkout `D:/Unity/Projects/SpaceSim`; it holds Nasser's own work.
- **The Unity lock.** One Unity build or suite runs at a time on this PC. The lock is the directory `C:\Users\nasse\OneDrive\Desktop\Projects\space sim\UNITY.lock.d`, taken with `mkdir` (atomic). The owner writes `owner.txt`. A lock whose `owner.txt` is older than 45 minutes is stale. Remove the directory when done. `Tools/qa.sh` does all of this itself.
- Set these (or put them in the gitignored `Tools/unity.local`): `UNITY_EDITORS=/d/Unity/Editors` and `FOW_UNITY_LOCK="/c/Users/nasse/OneDrive/Desktop/Projects/space sim/UNITY.lock.d"`. The default Hub path has no editor on this PC.
- To align with another session, message it. Merge another session's PR only on Nasser's direct approval in the merging session.

## Build and QA

- A Unity build is the only real compile check. `dotnet build` of `Assembly-CSharp.csproj` is a quick check only: the csproj is stale and lacks files added on other branches.
- `Tools/qa.sh <outdir> [suite …]` builds, then runs the suites in order: `career`, `legacy`, `uishots`, `qa`. `QA_NOBUILD=1` reuses the last build. It stops at the first suite that exits nonzero; to see every result, run the suites one at a time.
- **During a PR:** one build, then only the suite that covers the change.
  - Career rules or combat: `career`.
  - UI: `uishots`, and `legacy` only if the classic shell is touched.
  - Scene readouts or visuals: `qa`.
- **Before a merge to main, or a build for a playtest:** all four suites, once, on the combined tree that main will equal. Then merge, in order, with merge commits.
- **The monitor.** On the portrait monitor (1440×2560), 17y, 17z and 18c (twice) fail with "under a panel". This is the environment, not the code. The player flag `-monitor 2` opens the game on the ultrawide. `Tools/qa.sh` passes no extra player flags, so these four FAILs can show in its runs.
- **QA style.** Each feature adds checks to its `CareerQA.*.cs` section. Every check is `Check(ok, msg)` and logs what it measured. No unit-test project.
- **The playtest build.** After the merge suites pass: `robocopy <worktree>\Builds\Windows "C:\Users\nasse\OneDrive\Desktop\Projects\space sim\QA-Build" /MIR`. `/MIR` deletes files in `QA-Build` that the source lacks, so check first that the source is a complete build.

## Git and PRs

- One concern a PR. Each PR is playable alone and passes its suites with 0 FAIL.
- Stacked PRs: the base is the parent branch until the parent merges.
- Commit each new asset's `.meta` file with the asset.
- The PR body has a summary and a test plan. The test plan lists the suites run, the build tag and the result.
- Merge only on Nasser's explicit approval of that PR.
