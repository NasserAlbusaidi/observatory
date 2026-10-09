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

        // 17s: each hull's 2-mount pair is opposite; then the mounts that bear on set approach directions (body frame, from the
        // ship toward the missile) for the 6, 2 and 4 mount fits read from Hull.MountFit. The counts are worked by hand from
        // the zeniths and the 100° arc. Dead on the nose or the tail every mount is on its horizon and bears; 10° off the axis
        // is the arc's edge for a mount square above or below, so the cases sit at 5° (inside it) and 15° (past it).
        void PdcBearingChecks()
        {
            Vector3 Pitched(float deg, float z) => new Vector3(0, Mathf.Sin(deg * Mathf.Deg2Rad), z * Mathf.Cos(deg * Mathf.Deg2Rad));
            var dirs = new (string Name, Vector3 D)[]
            {
                ("nose", Vector3.forward), ("tail", Vector3.back), ("starboard", Vector3.right), ("port", Vector3.left),
                ("top", Vector3.up), ("bottom", Vector3.down),
                ("nose 5° high", Pitched(5, 1)), ("nose 5° low", Pitched(-5, 1)), ("nose 15° high", Pitched(15, 1)), ("nose 15° low", Pitched(-15, 1)),
                ("tail 15° high", Pitched(15, -1)), ("tail 15° low", Pitched(-15, -1)),
                ("port low", new Vector3(-0.53f, -0.848f, 0).normalized),
            };
            var want = new Dictionary<string, int[][]>
            {
                // by fit (6, 2, 4), in the order of dirs
                [FrigateName] = new[]
                {
                    new[] { 6, 6, 5, 5, 4, 2, 6, 6, 4, 4, 4, 4, 3 },
                    new[] { 2, 2, 2, 2, 1, 1, 2, 2, 1, 1, 1, 1, 1 },
                    new[] { 4, 4, 3, 3, 3, 1, 4, 4, 3, 3, 3, 3, 2 },
                },
                [CorvetteName] = new[]
                {
                    new[] { 6, 6, 4, 4, 4, 2, 6, 6, 6, 4, 6, 4, 3 },
                    new[] { 2, 2, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2 },
                    new[] { 4, 4, 2, 2, 2, 2, 4, 4, 4, 4, 4, 4, 3 },
                },
            };
            foreach (var h in ShipBuild.Hulls)
            {
                if (!want.TryGetValue(h.Name, out var w)) { Check(false, $"pdc bearing: no hand counts for \"{h.Name}\""); continue; }
                bool table = h.Mounts != null && h.Mounts.Length == 6 && h.MountFit != null && h.MountFit.Length == 6;
                var order = new List<int>();
                for (int i = 0; table && i < 6; i++) { table &= h.MountFit[i] >= 0 && h.MountFit[i] < 6 && !order.Contains(h.MountFit[i]); order.Add(h.MountFit[i]); }
                float pair = table ? Vector3.Angle(h.Mounts[h.MountFit[0]], h.Mounts[h.MountFit[1]]) : 0;
                Check(table && pair >= 179,
                    $"pdc bearing: {h.Name} fits PDC_{string.Join(", PDC_", order)} in that order (each marker once); its 2-mount pair's zeniths are {pair:0.00}° apart (≥ 179: opposite)");
                if (!table) continue;
                foreach (var (f, n) in new[] { (0, 6), (1, 2), (2, 4) })
                {
                    int mask = PointDefense.FitMask(h, n);
                    var got = new List<string>();
                    bool ok = true;
                    for (int i = 0; i < dirs.Length; i++)
                    {
                        int b = PointDefense.Bearing(h, mask, dirs[i].D);
                        ok &= b == w[f][i];
                        got.Add($"{dirs[i].Name} {b} ({w[f][i]})");
                    }
                    Check(ok, $"pdc bearing: {h.Name}, {n} mounts (mask 0x{mask:X2}), arc {PointDefense.ArcDeg}°: {string.Join(", ", got)}");
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
