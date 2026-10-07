# Issue validation, 7 October 2026

These results cover the combined source at `57954edbb5bbfd962308ca17f30c87e0d07cbd29`, before it was separated into one review branch per issue. The branch partition must preserve that source. [manifest.json](manifest.json) records native binary and evidence hashes. The build used Unity 6000.6.4f1 on Windows 11, Direct3D 11, an RTX 4080 and a Ryzen 9 7900X. The native Windows build succeeded.

Reproduction commands, independent references, units, fixture domains and tolerances are in [Tools/benchmarks/README.md](../../../Tools/benchmarks/README.md). Every result applies to its recorded fixture and revision, not to every possible orbit, image or machine.

| Check | Result | Retained evidence |
|---|---|---|
| Core physics | 1,041 checks, zero failures | [physics-benchmarks.json](physics-benchmarks.json) |
| Focused issue regressions | 96 passes, zero failures | [issue-regressions.txt](issue-regressions.txt) |
| Native GPU geometry | 833 checks, zero failures; 3,060 classification rays and 1,524 independent escape directions | [gpu-benchmarks.json](gpu-benchmarks.json) |
| Pursuit | 54 runs; gates G1–G7 pass. A final focused run passes 12 checks. | [full table](pursuit-check-table.txt), [focused results](pursuit-focused.json) |
| Parallel career QA | Both runs pass all 151 checks, with zero Unity errors, while a separate Unity build runs. Distinct worktree executable paths and process save keys; identical 135-ship traffic lists. | [run A](career-final-a.txt), [run B](career-final-b.txt), [comparison](career-comparison.json) |
| Traffic negative control | Changing only the lookback from 30 to 25 days fails three assertions and exits 1: the 26.7-day trip is outside the window, the scene has 133/135 ships, and the required liner is missing. Accepted source/build restored to 30 days. | [mutation output](traffic-mutation25-final.txt) |
| Rendered image comparison | Eight paired comparisons and four reason masks pass. Maximum normalized RGB RMSE is about 0.001913, below 0.02. No final captured table cells report numerical failure. | [image-comparison.json](image-comparison.json), [capture and timings](bhshots.txt), [images](images) |
| UI | Five provenance cases and the snapshot-time HUD captured with zero errors; final images inspected for readability. | [provenance log](provenance-ui.txt), [snapshot log](snapshot-ui.txt), [images](images) |
| CareerQA extraction | 252 moved lines are identical after newline normalization; original yielded values are forwarded. Main file 776 lines, partial 267. All 151 checks pass before and after. | [source comparison](career-split-source.json), [pre-split run](career-pre-split.txt) |

The player's existing save was hashed before and after the runs and remained unchanged. No save contents are retained here.

## Issue coverage

- #88: 6,000-step coast/clock fixtures at four step sizes and 6,000 guidance rechecks; equal represented TDB elapsed time, no uncounted motion or fuel.
- #50: normal-warp gravity selection plus independent step convergence, an inclined Earth/J2 orbit, Moon encounter and SOI exit. The high-warp conic differences are model differences, not integration error.
- #51: original fit-source vectors, separate fresh Horizons comparison, per-body fitted/blended/missing/mean/estimated metadata and five readable native UI captures.
- #52: explicit retained drive/RCS/wheel/heat assumptions, corrected exhaust-power units/meaning, and independent acceleration/fuel/depletion checks.
- #53–54: rotating contact, local surface speed, migration and liftoff across latitudes, rotation directions and step sizes; safe/unsafe contact and assisted giant arrival; native career docking/save checks.
- #55: snapshot-time rendering remains the explicit decision; the HUD and model guide state that photon travel delays are absent. The stationary rendering path is retained.
- #56: termination classes, unresolved/fallback accounting, native rays, table/full/fine images, reason masks and timing; precision fixes for near-polar tracing and zero-spin horizon evaluation.
- #57: README, scene introductions and model guide distinguish equations, approximations, omitted effects and measured validation.
- #58: offline independent reference fixtures, native C#/HLSL gates, declared tolerances, provenance and failure records. Includes the low-rapidity Journey cancellation and parent/sibling rail-clearance bugs exposed by validation.
- #93–94: process-specific QA save slots and simulation-time functional limits, verified with simultaneous native runs under a concurrent build; performance samples and hang guards remain separate.
- #95: explicit ordinal, case-sensitive derelict-prefix comparison; career rescue coverage also passes.
- #96: fixed epoch and six-hour scene interval, exact 135-ship reference list, and a failing 25-day lookback control.
- #97: S4r catches near the closed-form time with no climb entries; S1ahead stays beyond the 1.05 km safety threshold across all six timesteps. Player navigation remains separate from pursuit clearance.
- #101: an active intercept runner is absent from both the shared planet list and actual rebuilt UI Toolkit Targets tab; all eight planets remain.
- #102: move-only source comparison and functional before/after career results.

## Limits and diagnostic failures

The fresh Horizons comparison is not interchangeable with the original fit inputs: Ryugu differs by about 4.5 km near 2100, although the original inputs match closely. Both responses identify JPL#270; the cause is not established. See [current-horizons-comparison.json](current-horizons-comparison.json).

The initial tightly toleranced IVP ray reference drifted near high-spin critical orbits. The accepted reference separates radial quadrature and analytic Jacobi polar motion; [reference-development-comparison.json](reference-development-comparison.json) retains the disagreement. [development-failures.json](development-failures.json) preserves failures found during development. [render-before-horizon-fix.json](render-before-horizon-fix.json) retains the earlier numerical-cell failure; the final zero-spin cancellation fix removes it.

The polar capture automatically uses full tracing because its table is unavailable. Its image result is labelled automatic full fallback/full, rather than claiming a table comparison. The high-spin table has 104 budget-exhausted cells out of 1,048,576, including 33 display fallbacks; all are explicit uncertainty and excluded from interpolation. Unknown escape direction has no small angular-error bound.

Final quality-2 table/full timings are 2.1/4.1 ms at 1080p, 2.9/8.4 ms at 1440p and 5.7/15.6 ms at 4K. The busy sandbox's 4K sample is 18.8/17.7 ms: a table is not always faster. These are dispatch-to-GPU-completion wall timings on the stated hardware, not vendor GPU-counter measurements or a universal FPS claim.

Random route events and contract diagnostics make some literal PASS messages differ across career runs. All 151 functional checks pass, finish at the same game date, and produce the same traffic list. The extraction's source identity is verified separately; exact equality of every PASS string is not claimed.

The full pursuit matrix predates the final documentation/rendering commits but uses the same production pursuit code; the focused pursuit report records the final source revision. Target-thrust braking (#89) and gameplay decisions #90–92 and #98–100 remain open. Native automated evidence does not substitute for the user's gameplay acceptance of those decisions.
