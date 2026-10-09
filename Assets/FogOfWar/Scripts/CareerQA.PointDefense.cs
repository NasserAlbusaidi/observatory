using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // 17p-17t: the PDC pass and the mount table (docs/design-combat.md Phase 4, "PDCs", PR 4b; OpenWorld/PointDefense.cs,
    // Hull.Mounts). Headless: the closed form against a sum of λ·dt, P(kill) for 1-3 mounts, seeded passes, the bearing
    // counts; then the six zeniths of each hull's model against the table. 4a owns 17a-17o (CareerQA.Defense.cs).
    public partial class CareerQA
    {
        static string V(Vector3 v) => $"({v.x:0.0000}, {v.y:0.0000}, {v.z:0.0000})";

        IEnumerator PointDefenseChecks()
        {
            Guard("pdc integral", PdcIntegralChecks);
            Guard("pdc kill chance", PdcKillChecks);
            Guard("pdc passes", PdcPassChecks);
            Guard("pdc bearing", PdcBearingChecks);
            Guard("pdc mounts", PdcMountChecks);
            yield return null; // the spawned models' Destroy lands
        }

        // 17p: the closed form against a midpoint sum of λ·dt over 1e5 steps of 5 cm (StraightM falls on a step edge), the
        // jink alone and the whole pass; and the c → 0 limit
        void PdcIntegralChecks()
        {
            const int steps = 100000;
            const double v = 2;
            double c = PointDefense.SpreadC, r0 = PointDefense.FloorM, env = PointDefense.EnvelopeKm * 1e3, dx = env / steps;
            double jink = 0, straight = 0;
            for (int k = 0; k < steps; k++)
            {
                double x = (k + 0.5) * dx;
                bool jinking = x >= PointDefense.StraightM;
                double r = jinking ? r0 + c * x * x : r0;
                double lambda = PointDefense.Rate * PointDefense.TargetM2 / (Math.PI * r * r), dt = dx / (v * 1e3);
                if (jinking) jink += lambda * dt; else straight += lambda * dt;
            }
            double per = PointDefense.PerIntegral(v), cfJ = per * PointDefense.JinkIntegral, cfS = per * PointDefense.StraightIntegral;
            double eJ = Math.Abs(jink / cfJ - 1), eAll = Math.Abs((jink + straight) / (cfJ + cfS) - 1);
            Check(eJ <= 1e-3 && eAll <= 1e-3,
                $"pdc: at {v} km/s, c {c:0.000000e0} /m, the jink's Σλ·dt {jink:0.000000} against the closed form {cfJ:0.000000} (off {eJ:0.0e0}, ≤ 1e-3); the whole pass {jink + straight:0.000000} against {cfJ + cfS:0.000000} (off {eAll:0.0e0}, ≤ 1e-3)");

            double lim = PointDefense.Antiderivative(env, r0, 0), tiny = PointDefense.Antiderivative(env, r0, 1e-15);
            Check(Math.Abs(tiny / lim - 1) <= 1e-6 && Near(lim, env / (r0 * r0), 1e-9),
                $"pdc: the c → 0 limit: ∫0..5 km at c 1e-15 is {tiny:0.000000}, at c 0 {lim:0.000000} (x / r₀² = {env / (r0 * r0):0.000000}; agree within 1e-6)");
        }

        // 17q: the spec's numbers at 2 km/s and 20 g
        void PdcKillChecks()
        {
            double per = PointDefense.PerIntegral(2), j = per * PointDefense.JinkIntegral, st = per * PointDefense.StraightIntegral, e = PointDefense.Exponent(2);
            Check(Near(j, 0.139, 5e-4) && Near(st, 0.398, 5e-4) && Near(e, 0.537, 5e-4) && Near(j + st, e, 1e-12),
                $"pdc: one mount's exponent at 2 km/s: jink {j:0.0000} (0.139 ± 5e-4), straight {st:0.0000} (0.398 ± 5e-4), all {e:0.0000} (0.537 ± 5e-4)");

            double[] want = { 0, 0.42, 0.66, 0.80 };
            var got = new List<string>();
            bool ok = true;
            for (int m = 0; m <= 3; m++)
            {
                double p = PointDefense.KillChance(m, 2);
                ok &= Near(p, want[m], m == 0 ? 0 : 0.01);
                got.Add($"{m}: {p:0.0000} ({want[m]:0.00}), {6 * (1 - p):0.00} of 6 through");
            }
            Check(ok, $"pdc: P(kill) a missile at 2 km/s by mounts bearing (± 0.01): {string.Join("; ", got)}");
        }

        // 17r: 10,000 seeded passes for 1-3 mounts, each a walk in 5 m steps with a kill chance 1 − exp(−m·λ·dt) a step (the
        // process the closed form integrates), and 10,000 of the game's one draw (Kills); both within 2 percentage points
        void PdcPassChecks()
        {
            const int passes = 10000;
            const double v = 2, dx = 5;
            double c = PointDefense.SpreadC, r0 = PointDefense.FloorM;
            int n = (int)(PointDefense.EnvelopeKm * 1e3 / dx);
            var hazard = new double[n];
            for (int k = 0; k < n; k++)
            {
                double x = (k + 0.5) * dx, r = x >= PointDefense.StraightM ? r0 + c * x * x : r0;
                hazard[k] = PointDefense.Rate * PointDefense.TargetM2 / (Math.PI * r * r) * dx / (v * 1e3);
            }
            var got = new List<string>();
            bool ok = true;
            for (int m = 1; m <= 3; m++)
            {
                var step = new double[n];
                for (int k = 0; k < n; k++) step[k] = 1 - Math.Exp(-m * hazard[k]);
                var walk = new System.Random(1700 + m);
                int walked = 0;
                for (int p = 0; p < passes; p++)
                    for (int k = n - 1; k >= 0; k--)
                        if (walk.NextDouble() < step[k]) { walked++; break; }
                var draw = new System.Random(1710 + m);
                int drawn = 0;
                for (int p = 0; p < passes; p++) if (PointDefense.Kills(draw, m, v)) drawn++;
                double want = PointDefense.KillChance(m, v), fw = (double)walked / passes, fd = (double)drawn / passes;
                ok &= Near(fw, want, 0.02) && Near(fd, want, 0.02);
                got.Add($"{m} mounts: walked {fw:0.0000}, drawn {fd:0.0000}, closed form {want:0.0000}");
            }
            Check(ok, $"pdc: {passes:N0} seeded passes a fit agree with the closed form within 0.02: {string.Join("; ", got)}");
        }

        // 17s: the mounts that bear on set approach directions (body frame, from the ship toward the missile), all six fitted,
        // and with the 2 and 4 mount fits (PDC_1, PDC_5; + PDC_2, PDC_3). The counts are worked by hand from the zeniths.
        // Exactly on the horizontal plane (nose, tail, a broadside) the frigate's dorsal and ventral mounts are on their
        // horizon and do not bear (zenith · d > 0 is strict); 10° up or down they do.
        void PdcBearingChecks()
        {
            float s10 = Mathf.Sin(10 * Mathf.Deg2Rad), c10 = Mathf.Cos(10 * Mathf.Deg2Rad);
            var dirs = new (string Name, Vector3 D)[]
            {
                ("nose", Vector3.forward), ("tail", Vector3.back), ("starboard", Vector3.right), ("port", Vector3.left),
                ("top", Vector3.up), ("bottom", Vector3.down),
                ("nose 10° high", new Vector3(0, s10, c10)), ("nose 10° low", new Vector3(0, -s10, c10)),
                ("tail 10° high", new Vector3(0, s10, -c10)), ("tail 10° low", new Vector3(0, -s10, -c10)),
                ("port low", new Vector3(-0.53f, -0.848f, 0).normalized),
            };
            const int two = 0x22, four = 0x2E;
            var want = new Dictionary<string, int[][]>
            {
                // by fit (6, 2, 4), in the order of dirs
                [FrigateName] = new[]
                {
                    new[] { 0, 0, 1, 1, 4, 2, 4, 2, 4, 2, 3 },
                    new[] { 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1 },
                    new[] { 0, 0, 1, 1, 3, 1, 3, 1, 3, 1, 2 },
                },
                [CorvetteName] = new[]
                {
                    new[] { 0, 0, 2, 2, 4, 2, 4, 2, 4, 2, 2 },
                    new[] { 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 0 },
                    new[] { 0, 0, 2, 1, 3, 1, 3, 1, 3, 1, 1 },
                },
            };
            foreach (var h in ShipBuild.Hulls)
            {
                if (!want.TryGetValue(h.Name, out var w)) { Check(false, $"pdc bearing: no hand counts for \"{h.Name}\""); continue; }
                var fits = new[] { (PointDefense.AllMounts, "6 mounts"), (two, "2 mounts (PDC_1, PDC_5)"), (four, "4 mounts (+ PDC_2, PDC_3)") };
                for (int f = 0; f < fits.Length; f++)
                {
                    var got = new List<string>();
                    bool ok = h.Mounts != null && h.Mounts.Length == 6;
                    for (int i = 0; ok && i < dirs.Length; i++)
                    {
                        int b = PointDefense.Bearing(h, fits[f].Item1, dirs[i].D);
                        ok &= b == w[f][i];
                        got.Add($"{dirs[i].Name} {b} ({w[f][i]})");
                    }
                    Check(ok, $"pdc bearing: {h.Name}, {fits[f].Item2}: {string.Join(", ", got)}");
                }
            }
        }

        // 17t: each hull's model spawned as RefreshHullModel spawns it (the scene's SpawnHullModel, then Nose aligned to +Z) in
        // a holder at the origin, unrotated: each PDC_n's local +Y, read in the holder's axes, is Hull.Mounts[n] within 1°
        void PdcMountChecks()
        {
            var spawnM = typeof(OpenSystemScene).GetMethod("SpawnHullModel", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (spawnM == null) { Check(false, "pdc mounts: the scene has no SpawnHullModel"); return; }
            foreach (var h in ShipBuild.Hulls)
            {
                var holder = new GameObject("QA pdc mounts");
                try
                {
                    var model = ((GameObject)spawnM.Invoke(null, new object[] { h.Model, holder.transform })).transform;
                    Models.AlignMarker(model, "Nose", Vector3.forward);
                    var got = new List<string>();
                    bool ok = model.name == h.Model && h.Mounts != null && h.Mounts.Length == 6;
                    float worst = 0;
                    for (int i = 0; i < 6; i++)
                    {
                        var m = Models.FindDeep(model, "PDC_" + i);
                        if (m == null) { ok = false; got.Add($"PDC_{i} missing"); continue; }
                        var up = holder.transform.InverseTransformDirection(m.up);
                        var at = holder.transform.InverseTransformPoint(m.position);
                        float a = h.Mounts != null && i < h.Mounts.Length ? Vector3.Angle(up, h.Mounts[i]) : 180;
                        worst = Mathf.Max(worst, a);
                        got.Add($"PDC_{i} +Y {V(up)} at {V(at)}, {a:0.000}° off");
                    }
                    ok &= worst <= 1;
                    Check(ok, $"pdc mounts: {h.Name}, model \"{model.name}\" ({h.Model}), body frame (x starboard, y up, z nose), positions in model units: {string.Join("; ", got)}; worst {worst:0.000}° (≤ 1)");
                }
                finally { DestroyModel(holder); }
            }
        }
    }
}
