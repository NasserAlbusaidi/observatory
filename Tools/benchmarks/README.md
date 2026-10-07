# Independent physics checks

These gates exercise production C# and native HLSL. They separate integration error, changes of physical model, and source-data comparisons. A Python transcription agreeing with C# is not sufficient evidence; the sources below provide the independent controls. Snapshot-time emission is retained, so retarded-emission tests are not applicable.

Run from the repository root in PowerShell, supplying the installed Unity editor:

```powershell
$editor = 'D:\Unity\Editors\6000.6.4f1\Editor\Unity.exe'
& $editor -batchmode -nographics -projectPath (Get-Location).Path -executeMethod FogOfWar.EditorTools.FogOfWarPhysicsCheck.Run -logFile Logs/physics-editor.log
& $editor -batchmode -force-d3d11 -projectPath (Get-Location).Path -executeMethod FogOfWar.EditorTools.FogOfWarGpuCheck.Run -logFile Logs/gpu-editor.log
```

Run sequentially; Unity locks its project directory. Both entry points exit nonzero for a failed assertion, exception, or unavailable required suite. **Do not use `-nographics` for the GPU suite or game QA.** The latter requires instanced graphics even in batch mode. Results are `Logs/physics-benchmarks.json`, `Logs/gpu-benchmarks.json`, `Logs/issue-regressions.txt`, and `Logs/current-horizons-comparison.json`. JSON records commit, source hashes (including uncommitted sources), runtime, hardware, precision, reference, measured value, tolerance and result for every gate. A source-hash mismatch means a baseline is not evidence for the new source.

| Domain | Independent reference and scope | Gate |
|---|---|---|
| Drive and fuel | Piecewise constant-acceleration solution; Tsiolkovsky mass relation. Cargo 0/120 t, main/RCS together, depletion at 2.3 s within a 120 s history, h=.05/1/30 s. | 1 mm position, 1e-8 km/s velocity, 1e-8 t fuel |
| Conics/contact crossing | Elliptic and hyperbolic anomaly parameterisations, e=0 to 8 including 1.0001; analytic inbound crossing whose arc ends outside again. | 1 m position, 1e-6 km/s velocity, 1e-4 s crossing |
| Gravity modes | Zero-thrust production integrator refined 2/1/.5/.25 s, equal TDB endpoint. Sun/Earth/Moon, actual ephemerides and Earth J2; inclined Earth orbit, Moon encounter, SOI exit, isolated sphere. | Integrated convergence: 250 m and .3 m/s; monotonic refinement with 10 mm roundoff floor. High-warp model differences reported separately with fixture-specific 250 km bound, **not a universal accuracy claim**. |
| Rotating contact | Independent Rodrigues rotation; prograde/retrograde equator/45°/pole; synchronous orientation. h=1/60/21600 s, quarter day, local-speed contact, save migration and liftoff. | 1 mm pose/velocity-scale gates; details in regression report |
| Ephemerides | Retained original JPL input vectors near fit interior/segment/data boundaries for all 30 fitted bodies; fresh Horizons positions/velocities separately retained. Frame: ICRF ecliptic J2000, parent centred, geometric, km, TDB. | Original-input fit ≤2 km; derivative versus source velocity ≤0.1 m/s; boundary continuity ≤1 m. No accuracy claim for mean-element moons or edge blends. |
| Kerr orbits | Exact circular frequency; radial first integral; Schwarzschild precession from independent Simpson quadrature in Darwin anomaly. Spin 0/.5/.9/.998; two eccentric orbit domains; step factors .08/.04/.02. | Circular frequency 1e-10/M, radius 1e-7 M; precession .0005 rad/orbit; quadrature refinement 1e-10 rad |
| Journey | Hyperbolic-worldline reference time, symmetric rest arrival, Newtonian low-rapidity limit. All four destinations, proper times 1e-8 and 1e-5 yr. | 1e-7 yr reference time; 1e-8 relative low-rapidity displacement |
| Native rays | Quartic radial roots and analytic spherical photon orbits; 3,060 rays at spins 0/.5/.9/.998, inclinations 1/30/60/89°, observer radii 12/48 M, ±.1% and ±1% of critical constants. Native presets plus fine trace; independent float64 radial quadrature and Jacobi polar motion. | Correct resolved classifications; finite output; explicit unresolved rates; 0.02 unit-vector distance and 2% first-crossing radius convergence. Directions are compared at the finite outer boundary, 400 M for these fixtures. Reference trace is not automatically ground truth. |

The fresh Horizons comparison deliberately retains discrepancies rather than moving the fit tolerance. In the October 2026 baseline, Ryugu differs by about 4.5 km near 2100 despite matching the original fit inputs closely. Both responses identify JPL#270; the cause of their long-range difference is not established. This is a limitation of using a fresh service query as an exact substitute for the original source vectors.

The initial GPU reference used a tightly toleranced DOP853 initial-value solve. Near the high-spin prograde critical orbit it still accumulated radial-invariant error. The accepted oracle separates the radial quadrature from analytic Jacobi polar motion, preserving the constants directly. `diagnostic_ivp` is retained for comparison and is not used to decide correctness.

Ray termination codes are capture=0, confirmed outward escape=1, budget exhausted=2, opaque hit=3, numerical failure=4. Full debug view 1 and table geometry view 4 use red/green/blue/white/magenta; unresolved rays offered a display sky sample are cyan. Table T4 also stores step count and fallback flag. An unresolved display sample has unknown sky direction; its uncertainty can span the sky, so its rate is reported separately and its cells are rejected by the image interpolator. It never counts as confirmed escape. The table uses 80 MiB at 1024².

## Rebuild references

Python 3, numpy and scipy are required only to regenerate fixtures. Normal Unity checks are offline.

```powershell
python Tools/benchmarks/references.py geometry
python Tools/benchmarks/ray_reference.py
python Tools/benchmarks/references.py horizons
python Tools/benchmarks/references.py fit-source --cache Tools/horizons/cache
```

The last command requires the original `fit.mjs` response cache. It preserves response headers and SHA256 hashes, plus nearest original samples. Missing inputs fail instead of inventing replacements. Horizons requests are serial, cached and retried on temporary failure. Raw fresh responses and exact query URLs are retained.

References: [JPL Horizons API](https://ssd-api.jpl.nasa.gov/doc/horizons.html), [Teo, spherical photon orbits, equations 5–6 and 11](https://www.physics.nus.edu.sg/wp-content/uploads/sites/5/2019/10/paper.pdf), [Darwin-anomaly Schwarzschild formulation](https://link.aps.org/accepted/10.1103/PhysRevD.92.044048).

## Native rendered images and timing

Build with `FogOfWar.EditorTools.FogOfWarBuild.BuildWindows`. Use `Builds/Windows/FogOfWar.exe -batchmode -force-d3d11 -fowbhshots Logs/bh-images -fowbhonly bh-edge-on,bh-hotspot,bh-kerr-0.998-guides,bh-polar -fowbhres 1920x1080 -fowbhss 4 -fowbhnotests` for paired table/full/fine traces, diagnostic masks and native timing. The separate core suite covers the sandbox numerical checks omitted by `-fowbhnotests`. Add `-fowbhnotiming` only when timings have already been retained for the same build/hardware. The harness logs resolution, quality, table/full mode, postprocessing and hardware. Its timings include dispatch and GPU completion; they are not vendor GPU-counter measurements.

Run `python Tools/benchmarks/compare_images.py Logs/bh-images` to assert the image pairs and reason masks. In normalized tonemapped RGB, RMSE must be at most .02 and the fraction of pixels differing by more than .1 in any channel at most .01. Missing required captures fail. These image-domain budgets do not validate accretion dynamics or retarded emission.

Use `-fowuishots Logs/ui -fowuionly 06b-provenance -fowuires 1920x1080` for fitted, calibrated mean-moon, estimated-radius and outside-fit codex cases. Inspect the PNGs: successful capture alone does not establish readability.

`-fowcareer` uses fixed epoch/frame steps, process-unique save keys, simulation-time flight limits and separate performance reports. Compare two concurrent `career.txt` files, including the complete traffic fixture list. For the lookback negative control, temporarily change `Traffic.LookbackDays` from 30 to 25, build, run `-fowcareer <dir> -fowcareertraffic`, retain the failing output, restore 30 and rebuild. Never ship the mutation.
