using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FogOfWar.OpenWorld;
using UnityEngine;
using UIE = UnityEngine.UIElements;

namespace FogOfWar
{
    // 17p-17t: the PDC pass and the mount table (docs/design-combat.md Phase 4, "PDCs", PR 4b; OpenWorld/PointDefense.cs,
    // Hull.Mounts). Headless: the closed form against a sum of λ·dt, P(kill) for 1-3 mounts, seeded passes, the bearing
    // counts; then the six zeniths of each hull's model against the table. 4a owns 17a-17o (CareerQA.Defense.cs). Then 17u-17w,
    // the part in play, which DefenseChecks runs: the slot, the save, the repair (17u); the kill roll on the strike path, the
    // PDC hit, the draws and the scene's rows (17v); the boarding clock and the break-off (17w).
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

            // 2026-10-09: a missile closes at 10 km/s from a raider at rest, 15 km/s from one closing at 5 [calc]
            var fast = new List<string>();
            bool fastOk = true;
            foreach (var (v, w) in new[] { (10.0, new[] { 0.10, 0.19, 0.28 }), (15.0, new[] { 0.07, 0.13, 0.19 }) })
                for (int m = 1; m <= 3; m++)
                {
                    double p = PointDefense.KillChance(m, v);
                    fastOk &= Near(p, w[m - 1], 0.01);
                    fast.Add($"{v:0} km/s, {m}: {p:0.0000} ({w[m - 1]:0.00})");
                }
            Check(fastOk, $"pdc: P(kill) at the missile's coast speeds (± 0.01): {string.Join("; ", fast)}");
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

        // ------------------------------------------------------------ 17u-17w: the PDC part in play (CareerQA.Defense.cs runs them)

        // a belt bench with the PDC pick on the frigate, its attitude the identity: a body direction is Frame.FromU of it
        static Bench PdcBench(SolarSystem qs, int pick)
        {
            var (_, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            b.C.Build.pick[(int)Slot.PointDefense] = pick;
            b.C.Apply(b.Ship);
            b.Ship.Att = Quaternion.identity;
            return b;
        }

        // a raider 1 km off along body direction dir, matched and with its magazine spent: alongside on the next update
        static Encounter Beside(Bench b, Vector3 dir)
        {
            var e = b.C.StartContact(b.Job, b.Sys, b.Ship, b.Jd, b.Ship.Pos + Frame.FromU(b.Ship.Att * dir), b.Ship.Vel);
            e.Magazine = 0;
            return e;
        }

        static string Mask(int m) => $"0x{m:X2}";

        // 17u: the slot and its catalog, the fit masks and the mass, the save, the repair and a refit
        void PdcFitChecks(SolarSystem qs)
        {
            var rows = ShipBuild.Catalog[(int)Slot.PointDefense];
            string list = string.Join("; ", Array.ConvertAll(rows, m => $"{m.Name} {m.Size} {m.Mass} t {m.Price:N0} cr {m.RefitDays} d"));
            bool table = rows.Length == 4 && rows[0].Name == "No PDCs" && rows[0].Size == 0 && rows[0].Mass == 0 && rows[0].Price == 0 && rows[0].RefitDays == 0.5;
            for (int i = 1; table && i < 4; i++) table = rows[i].Size == 2 * i && rows[i].Mass == 6 * i && rows[i].Price == 60000 * i && rows[i].RefitDays == 2 && rows[i].Name == $"{2 * i} PDC mounts";
            Check((int)Slot.PointDefense == 7 && table && Array.TrueForAll(ShipBuild.Hulls, h => h.StockPick.Length == 8 && h.StockPick[(int)Slot.PointDefense] == 0),
                $"defense 17u: Slot.PointDefense is index {(int)Slot.PointDefense} (7); {list} (none 0.5 d; 3 t and 30,000 cr a mount, 2 d); every hull's stock pick has 8 slots and no PDCs");

            var fits = new List<string>();
            bool masks = true;
            var wantMask = new Dictionary<string, int[]> { [FrigateName] = new[] { 0, 0x22, 0x2E, 0x3F }, [CorvetteName] = new[] { 0, 0x24, 0x3C, 0x3F } };
            foreach (var h in ShipBuild.Hulls)
            {
                var stock = ShipBuild.Stock(h);
                for (int k = 0; k < 4; k++)
                {
                    var b = ShipBuild.Stock(h); b.pick[(int)Slot.PointDefense] = k;
                    masks &= b.FittedMounts == wantMask[h.Name][k] && b.MountCount == 2 * k && Near(b.DryMass - stock.DryMass, 6 * k, 1e-9);
                    fits.Add($"{(h == ShipBuild.Hulls[0] ? "frigate" : "corvette")} {b.MountCount}: {Mask(b.FittedMounts)} ({Mask(wantMask[h.Name][k])}), +{b.DryMass - stock.DryMass} t");
                }
            }
            var four = ShipBuild.Stock(); four.pick[(int)Slot.PointDefense] = 2;
            var heavy6 = ShipBuild.Stock(); heavy6.pick[(int)Slot.PointDefense] = 3; heavy6.pick[(int)Slot.Armor] = 2;
            Check(masks && Near(four.DryMass, 312, 1e-9) && Near(four.GFull, 0.977, 0.0005) && Near(four.GEmpty, 1.603, 0.0005) && Near(four.DvFull, 4953, 1)
                  && Near(heavy6.DryMass, 358, 1e-9) && Near(heavy6.GFull, 0.896, 0.0005) && Near(heavy6.GEmpty, 1.397, 0.0005) && Near(heavy6.DvFull, 4438, 1),
                $"defense 17u: fitted masks and mass: {string.Join("; ", fits)}; 4 mounts {four.DryMass} t dry, {four.GFull:0.000} g full, {four.GEmpty:0.000} empty, {four.DvFull:N0} km/s (312, 0.977, 1.603, 4,953); " +
                $"6 mounts and heavy plating {heavy6.DryMass} t, {heavy6.GFull:0.000}, {heavy6.GEmpty:0.000}, {heavy6.DvFull:N0} (358, 0.896, 1.397, 4,438)");

            // the save: the mask round trips; an old save has none; a mask past the fitted mounts is clamped to them
            var s = PdcBench(qs, 1);
            s.C.Damage = new ShipDamage { drive = 1, mounts = 0x02 };
            string json = JsonUtility.ToJson(s.C.ToSave(s.Ship));
            var c = new Career { Sys = qs };
            c.FromSave(JsonUtility.FromJson<CareerSave>(json), new Ship());
            var old = new Career { Sys = qs };
            old.FromSave(JsonUtility.FromJson<CareerSave>("{\"credits\":1000,\"fuel\":100,\"build\":{\"hull\":\"FWO-052 light frigate\",\"pick\":[1,1,1,2,0,0,1]},\"damage\":{\"drive\":1}}"), new Ship());
            var bad = new Career { Sys = qs };
            bad.FromSave(JsonUtility.FromJson<CareerSave>("{\"credits\":1000,\"build\":{\"hull\":\"FWO-052 light frigate\",\"pick\":[1,1,1,2,0,0,0,1]},\"damage\":{\"mounts\":63}}"), new Ship());
            var none = new Career { Sys = qs };
            none.FromSave(JsonUtility.FromJson<CareerSave>("{\"credits\":1000,\"damage\":{\"mounts\":63}}"), new Ship());
            Check(c.Damage.mounts == 0x02 && c.Damage.drive == 1 && c.Build.MountCount == 2 && c.WorkingMounts == 0x20 && json.Contains("\"mounts\":2")
                  && old.Build.pick.Length == 8 && old.Build.MountCount == 0 && old.Damage.mounts == 0 && old.Damage.drive == 1 && old.Build.pick[(int)Slot.Armor] == 1
                  && bad.Damage.mounts == 0x22 && none.Damage.mounts == 0 && !none.Damage.Any,
                $"defense 17u: a save round trip keeps the dead mask {Mask(c.Damage.mounts)} (0x02: PDC_1) on {c.Build.MountCount} mounts, working {Mask(c.WorkingMounts)} (0x20); a 7-pick save with no mounts field loads {Picks(old.Build)}, " +
                $"dead {Mask(old.Damage.mounts)} (0x00), its drive hit kept ({old.Damage.drive}); a mask of 0x3F on 2 mounts loads as {Mask(bad.Damage.mounts)} (0x22), on no PDCs as {Mask(none.Damage.mounts)} (0x00, no damage {!none.Damage.Any})");

            // the repair and the refits, docked at Ceres
            var (_, at, vel) = Belt(qs);
            var live = new List<Bench>();
            try
            {
                var d = DockedAt(qs, at, vel, "Ceres", live, false);
                d.C.Build.pick[(int)Slot.PointDefense] = 2;
                d.C.Damage = new ShipDamage { drive = 1, mounts = 0x06 };
                d.C.Apply(d.Ship);
                string text = d.C.DamageText();
                bool ok = d.C.CanRepair(out string why, out double cost, out double days);
                double cr0 = d.C.Credits;
                bool done = d.C.Repair(d.Ship);
                Check(text == "drive 50 %, 2 of 4 PDC mounts out" && ok && cost == 22500 && days == 3 && done && d.C.Credits == cr0 - 22500 && d.C.Damage.mounts == 0 && d.C.WorkingMounts == 0x2E,
                    $"defense 17u: one drive hit and PDC_1, PDC_2 out of 4 mounts: \"{text}\"; repair {cost:N0} cr (25 % of 30,000 + 2 × 30,000 = 22,500), {days} days (1 + 2 × 1 = 3), allowed {ok} ({why ?? "-"}); " +
                    $"repaired {done}: −{cr0 - d.C.Credits:N0} cr, working {Mask(d.C.WorkingMounts)} (0x2E)");

                d.C.RefitJd = 0;
                d.C.Damage = new ShipDamage { sensors = 1, mounts = 0x02 };
                var tank = d.C.Build.Clone(); tank.pick[(int)Slot.Tank] = 2;
                bool r1 = d.C.RefitTo(d.Ship, tank);
                int kept = d.C.Damage.mounts;
                var six = d.C.Build.Clone(); six.pick[(int)Slot.PointDefense] = 3;
                bool r2 = d.C.RefitTo(d.Ship, six);
                Check(r1 && kept == 0x02 && r2 && d.C.Damage.mounts == 0 && d.C.Damage.sensors == 1 && d.C.WorkingMounts == 0x3F,
                    $"defense 17u: a tank refit ({r1}) keeps the dead mask {Mask(kept)} (0x02); a refit to 6 mounts ({r2}) clears it ({Mask(d.C.Damage.mounts)}) and keeps the sensor hit ({d.C.Damage.sensors}); working {Mask(d.C.WorkingMounts)} (0x3F)");
            }
            finally { foreach (var x in live) x.C.DropContact(); }
        }

        // 17v: the kill roll on the real strike path, the PDC hit, and the draws of a whole chase
        void PdcStrikeChecks(SolarSystem qs)
        {
            // 10,000 strikes at 10 km/s for 1, 2 and 3 mounts bearing (from 17s's table: 2 mounts from the top and from the nose, 4 from
            // starboard). The mounts are made whole after each strike, so a PDC hit does not change m mid-run
            var got = new List<string>();
            bool ok = true;
            foreach (var (pick, dir, name, m) in new[] { (1, Vector3.up, "top", 1), (1, Vector3.forward, "nose", 2), (2, Vector3.right, "starboard", 3) })
            {
                var b = PdcBench(qs, pick);
                var rng = new CountingRng(1720 + m);
                SetCombatRng(b.C, rng);
                var from = Frame.FromU(dir) * 1.5;
                int bearing = b.C.MountsBearing(b.Ship, from), kills = 0;
                const int n = 10000;
                for (int i = 0; i < n; i++) { if (b.C.Strike(b.Ship, from, Career.MissileDvKms)) kills++; b.C.Damage = new ShipDamage(); }
                double want = PointDefense.KillChance(m, Career.MissileDvKms), f = (double)kills / n;
                int notes = SaidCount(b, Career.KillNote);
                bool one = bearing == m && Near(f, want, 0.02) && notes == kills && rng.Draws == kills + 3 * (n - kills);
                ok &= one;
                got.Add($"{b.C.Build.MountCount} mounts from the {name}: {bearing} bear ({m}), killed {f:0.0000} ({want:0.0000} ± 0.02), {notes} kill notes, {rng.Draws:N0} draws (kills + 3 × the rest = {kills + 3 * (n - kills):N0})");
            }
            Check(ok, $"defense 17v: {10000:N0} seeded strikes a case: {string.Join("; ", got)}");

            // no mount bearing, no kill roll: PDC_5 dead and the approach from below, and no PDCs at all
            var nb = new List<string>();
            bool quiet = true;
            foreach (var (pick, dead) in new[] { (1, 0x20), (0, 0) })
            {
                var b = PdcBench(qs, pick);
                var rng = new CountingRng(1730);
                SetCombatRng(b.C, rng);
                var from = Frame.FromU(Vector3.down);
                b.C.Damage = new ShipDamage { mounts = dead };
                int bearing = b.C.MountsBearing(b.Ship, from), kills = 0;
                for (int i = 0; i < 1000; i++) { if (b.C.Strike(b.Ship, from, Career.MissileDvKms)) kills++; b.C.Damage = new ShipDamage { mounts = dead }; }
                quiet &= bearing == 0 && kills == 0 && rng.Draws == 2000 && SaidCount(b, Career.KillNote) == 0;
                nb.Add($"{b.C.Build.MountCount} mounts, dead {Mask(dead)}: {bearing} bear, {kills} kills, {rng.Draws:N0} draws");
            }
            Check(quiet, $"defense 17v: 1,000 strikes from below with no mount bearing draw no kill roll (2 draws each: 2,000) and kill none: {string.Join("; ", nb)}");

            // the PDC hit: the working mount nearest the approach goes, bearing or not; with none left, the hull
            var h = PdcBench(qs, 1);
            var masks = new List<string>();
            var costs = new List<double>();
            for (int i = 0; i < 3; i++) { h.C.Hit(h.Ship, Career.HitSystem.Pdc, Vector3.up); masks.Add(Mask(h.C.Damage.mounts)); h.C.CanRepair(out _, out double cost, out _); costs.Add(cost); }
            var notesH = h.Said.FindAll(x => x.Text.StartsWith("Hit:")).ConvertAll(x => x.Text);
            var w = PdcBench(qs, 3);
            w.C.Hit(w.Ship, Career.HitSystem.Pdc, Vector3.right);
            Check(masks[0] == "0x02" && masks[1] == "0x22" && masks[2] == "0x22" && notesH.Count == 3 && notesH[0] == "Hit: a PDC mount, 1 of 2 working" && notesH[1] == "Hit: a PDC mount, 0 of 2 working"
                  && notesH[2] == Career.HullNote && h.C.DamageText() == "2 of 2 PDC mounts out" && w.C.Damage.mounts == 0x08
                  && h.C.Damage.freeMounts == 0x02 && costs[0] == 0 && costs[1] == 7500 && costs[2] == 7500 && w.C.Damage.freeMounts == 0x08,
                $"defense 17v: three PDC hits from the top on 2 mounts leave the dead mask {string.Join(", ", masks)} (0x02 PDC_1 the dorsal, 0x22 then PDC_5, 0x22: the hull); notes {string.Join(" / ", notesH)}; " +
                $"Damage \"{h.C.DamageText()}\"; repair {string.Join(", ", costs.ConvertAll(x => x.ToString("N0")))} cr (0: PDC_5 still worked, then 7,500 for the last; free mask {Mask(h.C.Damage.freeMounts)}, 0x02); " +
                $"on 6 mounts a hit from starboard takes {Mask(w.C.Damage.mounts)} (0x08, PDC_3), free ({Mask(w.C.Damage.freeMounts)})");

            // a whole chase with 6 mounts: routeRng does not move; the combat rng draws the kill roll, then plating and system
            // for each missile that gets through
            var c = PdcBench(qs, 3);
            var roll = SigRoll(c);
            var combat = new CountingRng(5);
            SetCombatRng(c.C, combat);
            var e = RaiderAt(c, 1999);
            int bear0 = c.C.MountsBearing(c.Ship, e.Pirate.Pos - c.Ship.Pos);
            for (int i = 0; i < 32 && c.C.Encounter == e; i++) c.Tick(100);
            c.C.DropContact();
            int kill = SaidCount(c, Career.KillNote);
            int hits = c.Said.FindAll(x => x.What == Career.Happening.MissileHit || (x.What == Career.Happening.JobFailed && x.Text.StartsWith("Hit:"))).Count;
            Check(roll.Draws == 0 && kill + hits == 6 && e.Killed == kill && combat.Draws == kill + 3 * hits && e.Magazine == 0,
                $"defense 17v: a chase of 3 salvos at 6 mounts ({bear0} bear on the raider): routeRng drew {roll.Draws} (0); {kill} killed (e.Killed {e.Killed}), {hits} hit; the combat rng drew {combat.Draws} (kills + 3 × hits = {kill + 3 * hits})");
        }

        // 17w: the boarding clock against the mounts, and the raider that breaks off
        void PdcBoardingChecks(SolarSystem qs)
        {
            string Run(Bench b, Encounter e, int ticks)
            {
                for (int i = 0; i < ticks && b.C.Encounter == e; i++) b.Tick(30);
                return b.C.Encounter == e ? $"dwell {e.Dwell:F0} s, repel {e.Repel:F0} s" : "ended";
            }

            // a working mount bears: the clock stops; with a spent magazine the repel clock runs instead
            var a = PdcBench(qs, 1);
            var ea = Beside(a, Vector3.up);
            string held = Run(a, ea, 60);
            int bearA = a.C.MountsBearing(a.Ship, ea.Pirate.Pos - a.Ship.Pos);
            a.C.DropContact();

            // the bearing mount dead: the other one (PDC_5, ventral) does not bear on the top, so the clock runs
            var d = PdcBench(qs, 1);
            d.C.Damage = new ShipDamage { mounts = 0x02 };
            var ed = Beside(d, Vector3.up);
            string dead = Run(d, ed, 10);
            d.C.DropContact();

            // the working mount turned away: PDC_5 dead, PDC_1 bears on the top; rolled 180° about the nose, the raider is below
            var t = PdcBench(qs, 1);
            t.C.Damage = new ShipDamage { mounts = 0x20 };
            var et = Beside(t, Vector3.up);
            string before = Run(t, et, 10);
            t.Ship.Att = Quaternion.AngleAxis(180, Vector3.forward);
            int bearT = t.C.MountsBearing(t.Ship, et.Pirate.Pos - t.Ship.Pos);
            string turned = Run(t, et, 10);
            t.C.DropContact();

            // no PDCs: boarded as in W4, the clock runs
            var n = PdcBench(qs, 0);
            var en = Beside(n, Vector3.up);
            string stock = Run(n, en, 10);
            n.C.DropContact();
            Check(bearA == 1 && held == "dwell 0 s, repel 1800 s" && dead == "dwell 300 s, repel 0 s" && before == "dwell 0 s, repel 300 s" && bearT == 0 && turned == "dwell 300 s, repel 0 s" && stock == "dwell 300 s, repel 0 s",
                $"defense 17w: a raider 1 km above, alongside, magazine spent: 2 mounts ({bearA} bears, 1), 1,800 s: {held} (dwell 0, repel 1,800); PDC_1 dead, 300 s: {dead} (dwell 300); " +
                $"PDC_5 dead, 300 s: {before} (dwell 0), then rolled 180° ({bearT} bear, 0), 300 s: {turned} (dwell 300, repel 0); no PDCs, 300 s: {stock} (dwell 300)");

            // the break-off: held off for RepelS with the magazine spent; not while a missile is in flight or the magazine holds one
            var r = PdcBench(qs, 1);
            var er = Beside(r, Vector3.up);
            double pay0 = r.Job.Pay, t0 = r.Job.Tonnes;
            int k = 0;
            while (r.C.Encounter == er && k < 200) { r.Tick(30); k++; }
            var off = r.Said.FindLast(x => x.What == Career.Happening.RaiderGone);
            ReportedCheck(r.C, off.Text, "held off by a PDC");
            var flying = new List<string>();
            foreach (int mag in new[] { 0, 2 })
            {
                var f = PdcBench(qs, 1);
                var ef = Beside(f, Vector3.up);
                ef.Magazine = mag;
                ef.Missiles.Add(new Missile { P = f.Ship.Pos.Norm * 1e6 });
                for (int i = 0; i < 130 && f.C.Encounter == ef; i++) f.Tick(30);
                flying.Add(f.C.Encounter == ef ? $"magazine {mag}: on, repel {ef.Repel:F0} s, {ef.Missiles.Count} in flight" : $"magazine {mag}: ended");
                f.C.DropContact();
            }
            Check(k * 30 == Career.RepelS && r.C.Encounter == null && off.Text != null && off.Text.StartsWith("PDCs: the raider breaks off, back to ") && HavenNamed(off.Text)
                  && r.Job.Pay == pay0 && r.Job.Tonnes == t0 && !r.Said.Exists(x => x.What == Career.Happening.Boarded)
                  && flying[0] == "magazine 0: on, repel 0 s, 1 in flight" && flying[1] == "magazine 2: on, repel 0 s, 1 in flight",
                $"defense 17w: held off with a spent magazine the raider breaks off after {k * 30:N0} s ({Career.RepelS:N0}): \"{off.Text}\", the job untouched ({r.Job.Pay:N0} cr, {r.Job.Tonnes} t), no boarding; " +
                $"3,900 s with a missile in flight 1,000,000 km out: {string.Join("; ", flying)} (on, repel 0)");
        }

        // 17v, in the scene: the Missiles row's mounts that bear (both shells), the Damage row's dead mounts, and the yard's PDC
        // row and Repair price on the Jobs page (UI Toolkit; UiShots 06y-yard-pdc shoots the row)
        IEnumerator PdcSceneChecks()
        {
            Encounter e = null;
            var dock0 = career.Docked;
            var build0 = career.Build.Clone();
            var draft0 = Get<ShipBuild>("yardDraft");
            var yardPort0 = Get<Port>("yardPort");
            var head0 = Get<UIE.Label>("yardHead");
            var att0 = ship.Att;
            var setDocked = typeof(Career).GetProperty("Docked").GetSetMethod(true);
            bool go = Guard("pdc scene setup", () =>
            {
                career.DropContact();
                Put("warpIdx", 0);
                var vesta = sys["Vesta"];
                D3 pV = sys.Pos(vesta, jd), vV = sys.Vel(vesta, jd);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0; ship.Landed = null; ship.Hold = Hold.Off; ship.AngVel = Vector3.zero;
                ship.Pos = pV + pV.Norm * 1e6; ship.Vel = vV;
                career.Build.pick[(int)Slot.PointDefense] = 2;
                career.Apply(ship);
                var job = new Contract { Id = 900710, Kind = JobKind.Cargo, Title = "QA pdc", From = "Ceres", To = "Vesta", Tonnes = 10, Pay = 20000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, Key = "qa/pdc-scene" };
                career.Active.Add(job);
                e = career.StartContact(job, sys, ship, jd, ship.Pos + pV.Norm * 1999, vV);
            });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) Guard("pdc scene rows", () =>
            {
                var rows = SigRows();
                string m = SigVal(rows, "Missiles");
                var near = e.Missiles.Count > 0 ? e.Missiles[0].P : D3.Zero;
                int bear = career.MountsBearing(ship, near);
                Check(e.Missiles.Count == 2 && bear > 0 && m.EndsWith("; 4 left aboard the raider; " + Career.BearText(bear)) && m.StartsWith("2 inbound, impact in "),
                    $"defense 17v: in the scene with 4 mounts and a salvo of 2 in flight: Missiles row \"{m}\" (ends \"; {Career.BearText(bear)}\": the working mounts that bear on the nearest now)");
                career.Hit(ship, Career.HitSystem.Pdc, Career.ToBody(ship, near));
                rows = SigRows();
                string d = SigVal(rows, "Damage");
                string m2 = SigVal(rows, "Missiles");
                int bear2 = career.MountsBearing(ship, near);
                Check(d == "1 of 4 PDC mounts out: any port repairs it" && m2.EndsWith("; " + Career.BearText(bear2)) && bear2 == bear - 1,
                    $"defense 17v: a PDC hit from the salvo's side: Damage row \"{d}\"; the Missiles row now ends \"{Career.BearText(bear2)}\" ({bear} − 1)");
                career.DropContact();
                if (UI.UiShell.Disabled)
                {
                    log.WriteLine("  SKIP defense 17v: no UI Toolkit shell (-fowlegacyui): the Jobs page's PDC row; the rows above run in both shells, and the classic panel's yard draws the same PartLabel");
                    return;
                }
                setDocked.Invoke(career, new object[] { Array.Find(Career.Ports, p => p.Name == "Ceres") });
                var box = new UIE.VisualElement();
                Call("BuildJobs", box);
                string two = $"2 PDC mounts, 6 t: one bearing kills {PointDefense.KillChance(1, Career.MissileDvKms) * 100:0} % of missiles at 10 km/s";
                int head = CountText(box, "PDCs"), row = CountText(box, two), fitted = CountText(box, "4 PDC mounts, 12 t: one bearing kills 10 % of missiles at 10 km/s (fitted)");
                string want = R<string>("RepairLine", false);
                Check(head == 1 && row == 1 && fitted == 1 && want.StartsWith("Repair all: 0 cr, ", StringComparison.Ordinal) && CountText(box, want) == 1,
                    $"defense 17v: the Jobs page docked at Ceres draws the \"PDCs\" head {head} time (1), \"{two}\" {row} (1), the 4 fitted {fitted} (1), and \"{want}\" (0 cr: 3 mounts still worked, so the mount is free; 1 day)");
            });
            Guard("pdc scene cleanup", () =>
            {
                setDocked.Invoke(career, new object[] { dock0 });
                Put("yardDraft", draft0); Put("yardPort", yardPort0); Put("yardHead", head0); Put("repairBtn", null); Put("yardPdcHead", null);
                career.DropContact();
                career.Active.RemoveAll(c => c.Key == "qa/pdc-scene");
                career.Build = build0;
                career.Damage = new ShipDamage();
                ship.Att = att0;
                career.Apply(ship);
                Put("warpIdx", 0);
                Put("jobsSig", null);
            });
            yield return null;
        }
    }
}
