# Independent physics checks

These gates exercise production C# and native HLSL. They separate integration error, changes of physical model, and source-data comparisons. A Python transcription agreeing with C# is not sufficient evidence; the sources below provide the independent controls. Snapshot-time emission is retained, so retarded-emission tests are not applicable.

Run from the repository root in PowerShell, supplying the installed Unity editor:

```powershell
$editor = 'C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe'
& $editor -batchmode -nographics -projectPath (Get-Location).Path -executeMethod FogOfWar.EditorTools.FogOfWarPhysicsCheck.Run -logFile Logs/physics-editor.log
& $editor -batchmode -force-d3d11 -projectPath (Get-Location).Path -executeMethod FogOfWar.EditorTools.FogOfWarGpuCheck.Run -logFile Logs/gpu-editor.log
```

Run sequentially; Unity locks its project directory. Both entry points exit nonzero for a failed assertion, exception, or unavailable required suite. **Do not use `-nographics` for the GPU suite or game QA.** The latter requires instanced graphics even in batch mode. Results are `Logs/physics-benchmarks.json`, `Logs/gpu-benchmarks.json`, `Logs/issue-regressions.txt`, and `Logs/current-horizons-comparison.json`. JSON records commit, source hashes (including uncommitted sources), runtime, hardware, precision, reference, measured value, tolerance and result for every gate. A source-hash mismatch means a baseline is not evidence for the new source.

| Domain | Independent reference and scope | Gate |
|---|---|---|
| Kerr orbits | Exact circular frequency; radial first integral; Schwarzschild precession from independent Simpson quadrature in Darwin anomaly. Spin 0/.5/.9/.998; two eccentric orbit domains; step factors .08/.04/.02. | Circular frequency 1e-10/M, radius 1e-7 M; precession .0005 rad/orbit; quadrature refinement 1e-10 rad |
| Journey | Hyperbolic-worldline reference time, symmetric rest arrival, Newtonian low-rapidity limit. All four destinations, proper times 1e-8 and 1e-5 yr. | 1e-7 yr reference time; 1e-8 relative low-rapidity displacement |
| Native rays | Quartic radial roots and analytic spherical photon orbits; 3,060 rays at spins 0/.5/.9/.998, inclinations 1/30/60/89°, observer radii 12/48 M, ±.1% and ±1% of critical constants. Native presets plus fine trace; independent float64 radial quadrature and Jacobi polar motion. | Correct resolved classifications; finite output; explicit unresolved rates; 0.02 unit-vector distance and 2% first-crossing radius convergence. Directions are compared at the finite outer boundary, 400 M for these fixtures. Reference trace is not automatically ground truth. |

The initial GPU reference used a tightly toleranced DOP853 initial-value solve. Near the high-spin prograde critical orbit it still accumulated radial-invariant error. The accepted oracle separates the radial quadrature from analytic Jacobi polar motion, preserving the constants directly. `diagnostic_ivp` is retained for comparison and is not used to decide correctness.

Ray termination codes are capture=0, confirmed outward escape=1, unresolved=2 (budget exhausted, or a forbidden plunge at the retry floor), opaque hit=3, numerical failure=4. Full debug view 1 and table geometry view 4 use red/green/blue/white/magenta; unresolved rays offered a display sky sample are cyan. Table T4 also stores step count and fallback flag. An unresolved display sample has unknown sky direction; its uncertainty can span the sky, so its rate is reported separately and its cells are rejected by the image interpolator. It never counts as confirmed escape. The table uses 80 MiB at 1024².

## Rebuild references

Python 3 with the versions in `requirements.txt` regenerates fixtures; Pillow also supports the image comparisons. Install with `python -m pip install -r Tools/benchmarks/requirements.txt`. Normal Unity checks are offline.

```powershell
python Tools/benchmarks/references.py geometry
python Tools/benchmarks/ray_reference.py
```

References: [JPL Horizons API](https://ssd-api.jpl.nasa.gov/doc/horizons.html), [Teo, spherical photon orbits, equations 5–6 and 11](https://www.physics.nus.edu.sg/wp-content/uploads/sites/5/2019/10/paper.pdf), [Darwin-anomaly Schwarzschild formulation](https://link.aps.org/accepted/10.1103/PhysRevD.92.044048).

## Native rendered images and timing

Build with `FogOfWar.EditorTools.FogOfWarBuild.BuildWindows`. Use `Builds/Windows/FogOfWar.exe -batchmode -force-d3d11 -fowbhshots Logs/bh-images -fowbhonly bh-edge-on,bh-hotspot,bh-kerr-0.998-guides,bh-polar -fowbhres 1920x1080 -fowbhss 4 -fowbhnotests` for paired table/full/fine traces, diagnostic masks and native timing. The separate core suite covers the sandbox numerical checks omitted by `-fowbhnotests`. Add `-fowbhnotiming` only when timings have already been retained for the same build/hardware. The harness logs resolution, quality, table/full mode, postprocessing and hardware. Its timings include dispatch and GPU completion; they are not vendor GPU-counter measurements.

Run `python Tools/benchmarks/compare_images.py Logs/bh-images` to assert the image pairs and reason masks. In normalized tonemapped RGB, RMSE must be at most .02 and the fraction of pixels differing by more than .1 in any channel at most .01. Missing required captures fail. These image-domain budgets do not validate accretion dynamics or retarded emission.

## What the counts mean

The GPU report separates `independent-reference` gates (quartic classification and quadrature/Jacobi directions), `self-convergence` gates (the same production shader at a finer step/budget), `consistency` gates (full trace versus table generation), and invariant/coverage gates. The 192 direction/intersection refinement checks are self-convergence, not independent references. The 96 fallback-direction measurements are diagnostics with no pass/fail tolerance; unresolved-rate gates remain assertions. Counts and categories are written into the JSON and final log line.

Historical combined-branch evidence is retained on [`codex/resolve-open-issues`](https://github.com/NasserAlbusaidi/fog-of-war-observatory/tree/codex/resolve-open-issues/docs/validation/2026-10-07). Generated captures and logs are not part of this PR's tree; fixtures and the tools that consume them remain here. Those historical counts do not describe a later stack head.
