using System;
using System.Collections.Generic;
using System.Reflection;
using FogOfWar.OpenWorld;
using UnityEngine;
using UIE = UnityEngine.UIElements;

namespace FogOfWar
{
    // 16m: contraband cargo (Career.Smuggle.cs, docs/design-combat.md Phase 3d). A Cargo job posted in board slot 9 at the
    // Tharsis and Compact ports whose pay on delivery is graded by raider sight: Exposure is the expected number of raider
    // calls so far, the raid roll's own exponent. Headless parts run on a system of their own at the pursuit check's date,
    // on the same belt bench 15 and 16 use (1,000,000 km out from Vesta, at rest with it). The scene part counts the
    // contraband line on the Jobs page's two cards (UI Toolkit). Every [calc] number was worked by hand and never run:
    // every check logs what it measured, and no tolerance was loosened to make one pass.
    public partial class CareerQA
    {
        void StealthSmuggleChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            Guard("stealth smuggle pay", StealthSmugglePayChecks);
            Guard("stealth smuggle exposure", () => StealthSmuggleExposureChecks(qs, at, vel));
            Guard("stealth smuggle load", () => StealthSmuggleLoadChecks(qs, at, vel));
            Guard("stealth smuggle board", () => StealthSmuggleBoardChecks(qs, at, vel));
            Guard("stealth smuggle delivery", () => StealthSmuggleDeliveryChecks(qs, at, vel));
            Guard("stealth smuggle raider notes", () => StealthSmuggleRaiderChecks(qs, at, vel));
            Guard("stealth smuggle save", () => StealthSmuggleSaveChecks(qs, at, vel));
            Guard("stealth smuggle line", StealthSmuggleLineChecks);
        }

        static string[] ContrabandGoodsList() =>
            (string[])typeof(Career).GetField("ContrabandGoods", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);

        // 16m, the grade and PayAt: a 10,000 cr cargo job, 10 days allowed, asked at the first day. The pay is in
        // posted-pay terms: the plain cargo pay is posted / 1.5, so the ratio to it is 1.5 x the grade.
        void StealthSmugglePayChecks()
        {
            double jd0 = PursuitCheck.Jd0;
            Contract Job(bool contraband, double e, double expressAt = 0, double spoilAt = 0) => new Contract
            {
                Id = 900800, Kind = JobKind.Cargo, Title = "QA contraband", From = "Vesta", To = "Ceres", Tonnes = 20, Pay = 10000, Days = 10,
                IssuedJd = jd0, DeadlineJd = jd0 + 10, ExpressJd = expressAt, SpoilJd = spoilAt, Contraband = contraband, Exposure = e, Key = "qa/smuggle-pay",
            };

            double[] es = { 0, 0.1, 0.314, 0.7 }, ratios = { 1.875, 1.535, 1.00, 0.462 };
            bool ok = true;
            var got = new List<string>();
            for (int i = 0; i < es.Length; i++)
            {
                double pay = Career.PayAt(Job(true, es[i]), jd0 + 1, out bool late, out bool express);
                double want = Math.Round(10000 * 1.25 * Math.Exp(-es[i] / 0.5)), ratio = pay / (10000 / 1.5);
                ok &= Near(pay, want, 1) && Near(ratio, ratios[i], 0.001) && !late && !express;
                got.Add($"E {es[i]:0.###}: {pay:N0} cr (want {want:N0} ± 1), x{ratio:0.00000} of the plain pay ({ratios[i]:0.000} ± 0.001)");
            }
            Check(ok, $"stealth smuggle: a 10,000 cr contraband job, not express and not late, pays the grade 1.25·e^(-E/0.5) of its posted pay: {string.Join("; ", got)}");

            double breakEven = Career.ExposureScale * Math.Log(Career.CleanBonus / (1 / 1.5)), atEven = 1.5 * Career.Grade(Job(true, breakEven));
            Check(Career.CleanBonus == 1.25 && Career.ExposureScale == 0.5 && Near(breakEven, 0.314, 5e-4) && Near(atEven, 1, 1e-9),
                $"stealth smuggle: the constants are {Career.CleanBonus} (1.25) and {Career.ExposureScale} (0.5); the posted 1.5x of plain pay breaks even with plain cargo at E {breakEven:0.000000} calls (0.314 ± 5e-4), where 1.5 x grade is {atEven:0.000000000} (1 ± 1e-9)");

            // stacking: the grade multiplies what express, late and spoiled leave; express is 1.25 of the pay first (rounded)
            const double e1 = 0.1;
            double g = 1.25 * Math.Exp(-e1 / 0.5);
            double pe = Career.PayAt(Job(true, e1, jd0 + 5), jd0 + 1, out bool lateE, out bool expE);
            double pl = Career.PayAt(Job(true, e1), jd0 + 11, out bool lateL, out bool expL);
            double ps = Career.PayAt(Job(true, e1, 0, jd0 + 0.5), jd0 + 1, out bool lateS, out bool expS);
            double pls = Career.PayAt(Job(true, e1, 0, jd0 + 0.5), jd0 + 11, out bool lateLS, out _);
            double wantE = Math.Round(Math.Round(10000 * 1.25) * g), wantL = Math.Round(5000 * g), wantLS = Math.Round(2500 * g);
            Check(Near(pe, wantE, 1) && lateE == false && expE && Near(pl, wantL, 1) && lateL && !expL && Near(ps, wantL, 1) && !lateS && !expS && Near(pls, wantLS, 1) && lateLS,
                $"stealth smuggle: the grade {g:0.000000} stacks at E {e1}: express {pe:N0} cr (round(round(10,000 x 1.25) x g) = {wantE:N0} ± 1), late {pl:N0} cr (round(5,000 x g) = {wantL:N0} ± 1), spoiled {ps:N0} cr ({wantL:N0} ± 1), late and spoiled {pls:N0} cr (round(2,500 x g) = {wantLS:N0} ± 1)");

            // the same jobs without Contraband pay as before, whatever the Exposure field holds
            double c0 = Career.PayAt(Job(false, 0.7), jd0 + 1, out _, out _), cE = Career.PayAt(Job(false, 0.7, jd0 + 5), jd0 + 1, out _, out bool cExp);
            double cL = Career.PayAt(Job(false, 0.7), jd0 + 11, out _, out _), cG = Career.Grade(Job(false, 0.7));
            Check(c0 == 10000 && cE == 12500 && cExp && cL == 5000 && cG == 1,
                $"stealth smuggle: the plain control with Exposure 0.7 and Contraband off pays {c0:N0} cr (10,000 exactly), express {cE:N0} (12,500), late {cL:N0} (5,000), grade {cG} (1)");
        }

        // 16m, exposure is the roll's exponent. The belt bench is put 1,000,000 km out from Vesta at rest with it before each
        // update, as 15d and 16h do; the bench is not tried at the place it starts, so the log has no fall-back path to report.
        void StealthSmuggleExposureChecks(SolarSystem qs, D3 at, D3 vel)
        {
            var vesta = qs["Vesta"];
            var live = new List<Bench>();
            Bench Make(bool dark, bool contraband, string to = "Vesta", string from = "Ceres")
            {
                var b = dark ? Dark(qs, at, vel) : SigBench(qs, at, vel, JobKind.Cargo, to, from);
                b.Job.Contraband = contraband;
                live.Add(b);
                return b;
            }
            try
            {
                var b = Make(false, true);
                bool raid = b.C.RaidJob(qs, b.Ship, b.Jd) == b.Job;
                double x0 = b.Job.Exposure;
                SigUpdate(b, 0, -0.5, vesta, 1e6);
                double dx = b.Job.Exposure - x0, seen = b.C.SeenSeconds(b.Ship, b.Jd, 3600);
                double rate = b.C.PirateRate(qs, b.Ship, b.Job, b.Jd), presence = Threat.Presence(qs, b.Ship.Pos, b.Jd);
                bool patrol = Threat.PatrolAt(qs, b.Ship.Pos, b.Jd) != null;
                double chance = b.C.RaidChance(qs, b.Ship, b.Job, b.Jd, seen), want = -Math.Log(1 - chance), viaRate = rate * seen / 3600;
                Check(raid && x0 == 0 && dx > 0 && Near(dx, want, 1e-9 + 1e-9 * want) && Near(dx, viaRate, 1e-9 + 1e-9 * viaRate),
                    $"stealth smuggle: the job is the raid job {raid} (True); one tracked update of 3,600 s (the last loud moment 30 min before it), the bench put 1,000,000 km out from Vesta: presence {presence:0.000}, patrol in reach {patrol}, rate {rate:0.000000} an hour, seen {seen:0.000000} s; Exposure 0 -> {dx:0.000000000000} (above 0), the roll's exponent -ln(1 - RaidChance {chance:0.000000000000}) = {want:0.000000000000} (± {1e-9 + 1e-9 * want:E1}), rate x seen / 3600 = {viaRate:0.000000000000}");

                // not tracked: the last loud moment 3 h before the update puts the 2 h hold behind its start
                var u = Make(false, true);
                SigUpdate(u, 0, -3, vesta, 1e6);
                double useen = u.C.SeenSeconds(u.Ship, u.Jd, 3600), urate = u.C.PirateRate(qs, u.Ship, u.Job, u.Jd);
                Check(u.Job.Exposure == 0 && Near(useen, 0, 1e-3) && urate > 0,
                    $"stealth smuggle: the same place, the last loud moment 3 h before the update: Exposure {u.Job.Exposure:0.000000000000} (0), seen {useen:0.000000} s (0 ± 1e-3), the rate there {urate:0.000000} an hour (above 0, so it is the sight that gates it)");

                // a plain cargo control is not graded, however long it is seen
                var p = Make(false, false);
                SigUpdate(p, 0, -0.5, vesta, 1e6);
                double pseen = p.C.SeenSeconds(p.Ship, p.Jd, 3600);
                Check(p.Job.Exposure == 0 && pseen > 0,
                    $"stealth smuggle: a plain cargo job on the same tracked update: Exposure {p.Job.Exposure:0.000000000000} (0), seen {pseen:0.000000} s (above 0)");

                // stowable radiators: the hold is 36 min, so a burn 40 min back is not seen; the stock hull's 2 h hold still is
                var d = Make(true, true);
                SigUpdate(d, 0, -40.0 / 60, vesta, 1e6);
                double dseen = d.C.SeenSeconds(d.Ship, d.Jd, 3600), dhold = d.C.HoldS(d.Ship);
                var stock = Make(false, true);
                SigUpdate(stock, 0, -40.0 / 60, vesta, 1e6);
                double sseen = stock.C.SeenSeconds(stock.Ship, stock.Jd, 3600), srate = stock.C.PirateRate(qs, stock.Ship, stock.Job, stock.Jd), sviaRate = srate * sseen / 3600;
                Check(d.Job.Exposure == 0 && Near(dseen, 0, 1e-3) && Near(dhold, 2160, 1e-9) && stock.Job.Exposure > 0 && Near(stock.Job.Exposure, sviaRate, 1e-9 + 1e-9 * sviaRate),
                    $"stealth smuggle: 40 min after a burn the Dark bench (hold {dhold:0.000} s, 2,160) has Exposure {d.Job.Exposure:0.000000000000} (0), seen {dseen:0.000000} s (0 ± 1e-3); the stock bench (hold 7,200 s) {stock.Job.Exposure:0.000000000000} (above 0, = rate x seen / 3600 = {sviaRate:0.000000000000}), seen {sseen:0.000000} s");

                // landed (so docked) at Vesta, with the job to Ceres: no sight reaches a ship in port, though the burn is tracked
                var l = Make(false, true, "Ceres", "Vesta");
                l.Ship.Landed = vesta;
                SigUpdate(l, 0, -0.5, vesta, 1e6);
                double lseen = l.C.SeenSeconds(l.Ship, l.Jd, 3600), lrate = l.C.PirateRate(qs, l.Ship, l.Job, l.Jd);
                Check(l.C.Docked?.Name == "Vesta" && lseen > 0 && lrate > 0 && l.Job.Exposure == 0 && l.C.Active.Contains(l.Job),
                    $"stealth smuggle: landed on Vesta with the job to Ceres, the same tracked update (seen would be {lseen:0.000000} s, above 0; the rate there {lrate:0.000000} an hour, above 0, so only the dock gate holds it): docked {l.C.Docked?.Name ?? "-"} (Vesta), Exposure {l.Job.Exposure:0.000000000000} (0), job still aboard {l.C.Active.Contains(l.Job)} (True)");

                // the accrual does not read RouteEvents: it was off on every bench, and no call or chase came of any update
                bool off = true, quiet = true;
                foreach (var x in live) { off &= !x.C.RouteEvents; quiet &= x.C.Call == null && x.C.Encounter == null; }
                Check(off && quiet,
                    $"stealth smuggle: RouteEvents stayed off on all {live.Count} benches ({off}), none had a call or a chase ({quiet}); the tracked bench's Exposure grew all the same ({dx:0.000000})");
            }
            finally { foreach (var b in live) b.C.DropContact(); }
        }

        // 16m, a load in flight: it starts the ship tracked (D8), which is the roll's risk and not the grade's. After a date jump
        // (the update whose length is not the date's step, as a load gives) the quiet update that follows has the whole hold as
        // seen time for the roll, and none for Exposure. A real loud moment after it counts again.
        void StealthSmuggleLoadChecks(SolarSystem qs, D3 at, D3 vel)
        {
            var vesta = qs["Vesta"];
            var b = SigBench(qs, at, vel, JobKind.Cargo, "Vesta", "Ceres");
            try
            {
                b.Job.Contraband = true;
                void Place(double end) { var p = qs.Pos(vesta, end); b.Ship.Pos = p + p.Norm * 1e6; b.Ship.Vel = qs.Vel(vesta, end); }
                double jump = b.Jd + 0.25;
                Place(jump);
                b.Jd = jump;
                b.C.Tick(qs, b.Ship, b.Jd, 1800);   // the date moved 6 h and the update says 30 min: spanOk is false, as after a load
                double xLoad = b.Job.Exposure, startJd = b.Ship.LastLoudJd;
                Place(b.Jd + 3600 / 86400.0);
                b.Tick(3600);                        // quiet: the hull coasts after the load's tracked start
                double seen = b.C.SeenSeconds(b.Ship, b.Jd, 3600), rate = b.C.PirateRate(qs, b.Ship, b.Job, b.Jd);
                double xQuiet = b.Job.Exposure;
                Check(xLoad == 0 && Near(startJd, b.Jd - 3600 / 86400.0, 1e-9) && seen > 0 && rate > 0 && xQuiet == 0,
                    $"stealth smuggle: a load (a 6 h date jump in a 1,800 s update) puts the last loud moment at its date ({startJd:0.000000}); Exposure after it {xLoad:0.000000000000} (0); the quiet 3,600 s that follows has {seen:0.000000} s seen for the roll (above 0) at {rate:0.000000} an hour (above 0), and Exposure {xQuiet:0.000000000000} (0: the load's tail is not charged)");

                SigUpdate(b, 0, -0.5, vesta, 1e6);   // a real loud moment 30 min before the update's start
                double seen2 = b.C.SeenSeconds(b.Ship, b.Jd, 3600), rate2 = b.C.PirateRate(qs, b.Ship, b.Job, b.Jd), via = rate2 * seen2 / 3600;
                Check(b.Job.Exposure > 0 && Near(b.Job.Exposure, via, 1e-9 + 1e-9 * via),
                    $"stealth smuggle: after the load, a loud moment 30 min before the next update counts as before: Exposure {b.Job.Exposure:0.000000000000} (above 0, = rate {rate2:0.000000} x seen {seen2:0.000000} s / 3600 = {via:0.000000000000})");
            }
            finally { b.C.DropContact(); }
        }

        // 16m, the board: slot 9 of the Tharsis and Compact ports over 40 days, and no such slot at the Guard ports
        void StealthSmuggleBoardChecks(SolarSystem qs, D3 at, D3 vel)
        {
            const int days = 40;
            var goods = ContrabandGoodsList();
            Check(goods != null && goods.Length > 0, $"stealth smuggle: the contraband goods list is found by reflection ({(goods == null ? "-" : goods.Length.ToString())} titles)");
            var live = new List<Bench>();
            var sw = new System.Diagnostics.Stopwatch(); // the board days' Tick calls only, not the bench set-up or the checks' own Threat.Of
            try
            {
                foreach (string name in new[] { "Vesta", "Ceres", "Mars orbit", "Ganymede", "Titan", "Earth orbit", "Luna" })
                {
                    var port = Array.Find(Career.Ports, p => p.Name == name);
                    var b = BoardBench(qs, at, vel, name, live);
                    bool guard = port.Owner == Faction.Guard;
                    int posts = 0, bad = 0, contra = 0;
                    double lo = double.MaxValue, hi = 0, minH = double.MaxValue;
                    string first = "-", firstBad = "-", others = "-";
                    for (int i = 0; i < days; i++)
                    {
                        b.Jd = PursuitCheck.Jd0 + i;
                        sw.Start();
                        b.C.Tick(qs, b.Ship, b.Jd, 0);
                        sw.Stop();
                        if (name == "Vesta" && i == 0)
                        {
                            var rest = new List<string>();
                            foreach (var j in b.C.Board) if (!j.Key.EndsWith("/9")) rest.Add($"{j.Key} {j.Title}");
                            others = string.Join("; ", rest);
                        }
                        foreach (var j in b.C.Board)
                        {
                            if (j.Contraband) contra++;
                            if (!j.Key.EndsWith("/9")) continue;
                            posts++;
                            lo = Math.Min(lo, j.Pay); hi = Math.Max(hi, j.Pay);
                            var toPort = Array.Find(Career.Ports, p => p.Name == j.To);
                            var fromB = qs[port.Body];
                            var toB = toPort == null ? null : qs[toPort.Body];
                            double hours = double.NaN;
                            bool serious = false;
                            if (fromB != null && toB != null)
                            {
                                double dist = (qs.Pos(toB, b.Jd) - qs.Pos(fromB, b.Jd)).Mag;
                                var risk = Threat.Of(qs, fromB, D3.Zero, toB, b.Jd, dist, Ship.G0); // ExpressG is 1: the poster's yardstick is Ship.G0
                                hours = risk.Hours; serious = risk.Serious;
                                minH = Math.Min(minH, hours);
                            }
                            string why = j.Kind != JobKind.Cargo ? "kind" : !j.Contraband ? "contraband" : j.Exposure != 0 ? "exposure" : j.From != name ? "from"
                                : toPort == null || toPort == port ? "to" : goods == null || Array.IndexOf(goods, j.Title) < 0 ? "title" : j.Title.Contains("oud") ? "oud" : j.Tier != null ? "tier"
                                : j.Key != $"{name}/{(long)Math.Floor(b.Jd)}/9" ? "key" : !(j.Pay > 0) || j.Pay % 100 != 0 ? "pay" : !(j.ExpressDays > 0 && j.ExpressDays <= j.Days) ? "express" : !serious ? "risk" : null;
                            if (why != null) { bad++; if (firstBad == "-") firstBad = $"{j.Key} \"{j.Title}\" to {j.To}: {why}"; }
                            if (first == "-") first = $"{j.Key} \"{j.Title}\" to {j.To} {j.Pay:N0} cr, {j.Days:0.000} d, express {j.ExpressDays:0.000} d, planner risk {hours:0.00} h";
                        }
                    }
                    string pays = posts > 0 ? $"{lo:N0} to {hi:N0}" : "-", hrs = posts > 0 ? $"{minH:0.00}" : "-";
                    if (guard)
                        Check(posts == 0 && contra == 0 && bad == 0,
                            $"stealth smuggle: {name} (Guard) over {days} days posts {posts} in slot 9 (0) and {contra} contraband jobs (0)");
                    else if (name == "Vesta")
                        Check(posts >= 10 && posts <= 30 && bad == 0 && contra == posts,
                            $"stealth smuggle: {name} posts contraband on {posts} of {days} days (10 to 30), {bad} of them wrong (0; first wrong: {firstBad}), {contra} contraband on the board in all (= {posts}); pay {pays} cr; lowest planner risk {hrs} h (at least 1); first: {first}");
                    else
                        Check(bad == 0 && contra == posts,
                            $"stealth smuggle: {name} posts contraband on {posts} of {days} days (no range), {bad} of them wrong (0; first wrong: {firstBad}), {contra} contraband on the board in all (= {posts}); pay {pays} cr; lowest planner risk {hrs} h (at least 1); first: {first}");
                    if (name == "Vesta") log.WriteLine($"    stealth smuggle: Vesta on its first day, the other slots: {others}");
                }
                log.WriteLine($"    stealth smuggle: 7 ports x {days} board days: the Tick calls took {sw.ElapsedMilliseconds} ms");
            }
            finally { foreach (var b in live) b.C.DropContact(); }
        }

        // 16m, a delivery: the job pays the grade of its posted pay, and the note says so. A bench has no helm target, so the
        // by-hand bonus is 0 (it is read from the note, and taken off if it is there).
        void StealthSmuggleDeliveryChecks(SolarSystem qs, D3 at, D3 vel)
        {
            var live = new List<Bench>();
            try
            {
                const double e = 0.1;
                double g = 1.25 * Math.Exp(-e / 0.5);
                (bool gone, double paid, double hand, string note, bool docked) Land(double expressIn)
                {
                    var b = MakeBench(qs, at, vel);
                    live.Add(b);
                    var j = b.Job;
                    j.Pay = 10000; j.Contraband = true; j.Exposure = e;
                    j.ExpressJd = expressIn > 0 ? b.Jd + expressIn : 0;
                    double c0 = b.C.Credits;
                    b.Ship.Landed = qs["Vesta"];
                    b.Tick(60);
                    string text = b.Said.FindLast(x => x.What == Career.Happening.JobPaid).Text ?? "-";
                    double hand = text.Contains("flown in by hand") ? Math.Round(10000 * Career.HandBonus) : 0;
                    return (!b.C.Active.Contains(j), b.C.Credits - c0 - hand, hand, text, b.C.Docked?.Name == "Vesta");
                }

                var plain = Land(0);
                double want = Math.Round(10000 * 1.25 * Math.Exp(-e / 0.5));
                Check(plain.gone && plain.docked && Near(plain.paid, want, 1) && plain.note.Contains("contraband") && plain.note.Contains("×1.02") && plain.note.Contains("seen 0.10 calls"),
                    $"stealth smuggle: Exposure {e} delivered at Vesta, not express and not late: gone {plain.gone} (True), credits +{plain.paid:N0} cr (round(10,000 x 1.25 x e^-0.2) = {want:N0} ± 1; by-hand bonus {plain.hand:N0} taken off, 0 expected), note \"{plain.note}\" (holds \"contraband\", \"seen 0.10 calls\" and \"×1.02\": the note prints the grade {g:0.0000}, not the 1.5 x of plain pay)");

                var fast = Land(5);
                double wantF = Math.Round(Math.Round(10000 * 1.25) * g), extra = (Math.Round(10000 * 1.25) - 10000) * g;
                string extraText = $"express bonus +{extra:N0} cr";
                Check(fast.gone && Near(fast.paid, wantF, 1) && fast.note.Contains(extraText) && fast.note.Contains("contraband"),
                    $"stealth smuggle: the same inside the express window: credits +{fast.paid:N0} cr (round(12,500 x g) = {wantF:N0} ± 1), note \"{fast.note}\" (holds \"{extraText}\": the extra is 2,500 x g = {extra:0.000}, the grade is not folded into it)");
            }
            finally { foreach (var b in live) b.C.DropContact(); }
        }

        // 16m, the raider notes: a give-up's and a boarding's note name the cut at the job's grade, the pay it loses. A SigBench
        // ship has never burned, so no update in the chase adds Exposure and the grade stays the one set here. The boarding
        // clock is set by hand one update short (the full 7,200 s alongside is the pirates' Q4). The job is MakeBench's: 20,000 cr, 40 t.
        void StealthSmuggleRaiderChecks(SolarSystem qs, D3 at, D3 vel)
        {
            const double e = 0.3;
            double g = 1.25 * Math.Exp(-e / 0.5);   // [calc] 1.25 x e^-0.6 = 0.686015
            double cutG = 20000 * Career.GiveUpShare, cutB = 20000 * Career.BoardShare; // 5,000 and 10,000 cr
            var live = new List<Bench>();
            (string give, string board, Contract jg, Contract jb) Run(bool contraband)
            {
                var a = SigBench(qs, at, vel);
                var b = SigBench(qs, at, vel);
                live.Add(a); live.Add(b);
                foreach (var x in new[] { a, b }) { x.Job.Contraband = contraband; x.Job.Exposure = e; }
                var off = new D3(1, 0, 0);
                a.C.StartContact(a.Job, qs, a.Ship, a.Jd, a.Ship.Pos + off, a.Ship.Vel);
                a.C.Answer(Career.RouteAnswer.GiveUp, a.Jd);
                var enc = b.C.StartContact(b.Job, qs, b.Ship, b.Jd, b.Ship.Pos + off, b.Ship.Vel);
                enc.Dwell = Threat.BoardingSeconds - 30;
                Alongside(b, enc, off);
                return (a.Said.FindLast(s => s.What == Career.Happening.RaiderGone).Text ?? "-", b.Said.FindLast(s => s.What == Career.Happening.Boarded).Text ?? "-", a.Job, b.Job);
            }
            try
            {
                var smug = Run(true);
                double grade = Career.Grade(smug.jg), lostG = cutG * g, lostB = cutB * g;   // [calc] 3,430.07 and 6,860.15 cr
                string wantG = $"Gave up {10.0:N1} t and {lostG:N0} cr: the raider ", wantB = $"Boarded: the raider took {20.0:N1} t and {lostB:N0} cr back to ";
                Check(Near(grade, 0.686015, 1e-6) && Near(g, grade, 1e-12) && smug.give.StartsWith(wantG) && HavenNamed(smug.give) && smug.board.StartsWith(wantB) && HavenNamed(smug.board)
                      && smug.jg.Pay == 15000 && smug.jg.Tonnes == 30 && smug.jb.Pay == 10000 && smug.jb.Tonnes == 20 && smug.jg.Exposure == e && smug.jb.Exposure == e,
                    $"stealth smuggle: a contraband job at Exposure {e} has the grade {grade:0.000000000} (1.25 x e^-0.6 = 0.686015 ± 1e-6); give up: \"{smug.give}\" (starts \"{wantG}\": 5,000 cr x the grade = {lostG:0.00}), pay {smug.jg.Pay:N0} cr (15,000), {smug.jg.Tonnes:0.###} t (30); " +
                    $"boarded: \"{smug.board}\" (starts \"{wantB}\": 10,000 cr x the grade = {lostB:0.00}), pay {smug.jb.Pay:N0} cr (10,000), {smug.jb.Tonnes:0.###} t (20); Exposure after {smug.jg.Exposure:0.000000000000} and {smug.jb.Exposure:0.000000000000} ({e}: the chase added none)");

                var plain = Run(false);
                string plainG = $"Gave up {10.0:N1} t and {cutG:N0} cr: the raider ", plainB = $"Boarded: the raider took {20.0:N1} t and {cutB:N0} cr back to ";
                Check(Career.Grade(plain.jg) == 1 && plain.give.StartsWith(plainG) && plain.board.StartsWith(plainB) && plain.jg.Pay == 15000 && plain.jb.Pay == 10000,
                    $"stealth smuggle: the plain control, Exposure {e} and Contraband off: grade {Career.Grade(plain.jg)} (1); give up \"{plain.give}\" (starts \"{plainG}\"), boarded \"{plain.board}\" (starts \"{plainB}\"), pay {plain.jg.Pay:N0} and {plain.jb.Pay:N0} cr (15,000 and 10,000)");
            }
            finally { foreach (var b in live) b.C.DropContact(); }
        }

        // 16m, the save: an old contract has no Contraband or Exposure, a contraband one keeps both exactly
        void StealthSmuggleSaveChecks(SolarSystem qs, D3 at, D3 vel)
        {
            var old = JsonUtility.FromJson<Contract>("{\"Id\":1,\"Kind\":0,\"Title\":\"x\"}");
            Check(old != null && !old.Contraband && old.Exposure == 0,
                $"stealth smuggle: an old contract loads with Contraband {old?.Contraband} (False) and Exposure {old?.Exposure} (0)");

            const double e = 0.123456789;
            var smug = new Contract { Id = 900810, Kind = JobKind.Cargo, Title = "Unmanifested ice", From = "Vesta", To = "Ceres", Tonnes = 20, Pay = 15000, Days = 10, Contraband = true, Exposure = e, Key = "qa/smuggle-save" };
            string cj = JsonUtility.ToJson(smug);
            var back = JsonUtility.FromJson<Contract>(cj);
            Check(back != null && back.Contraband && back.Exposure == e && cj.Contains("\"Contraband\":true"),
                $"stealth smuggle: a contraband contract goes through JsonUtility ({cj.Length} chars) and comes back with Contraband {back?.Contraband} (True) and Exposure {back?.Exposure:R} ({e:R} exactly); the JSON holds \"Contraband\":true {cj.Contains("\"Contraband\":true")} (True)");

            var live = new List<Bench>();
            try
            {
                var b = SigBench(qs, at, vel);
                live.Add(b);
                b.C.Active.Add(smug);
                string json = JsonUtility.ToJson(b.C.ToSave(b.Ship));
                var c = new Career { Sys = qs };
                c.FromSave(JsonUtility.FromJson<CareerSave>(json), new Ship());
                var loaded = c.Active.Find(x => x.Id == smug.Id);
                var plain = c.Active.Find(x => x.Id == b.Job.Id);
                string lText = loaded == null ? "missing" : $"{loaded.Contraband}, {loaded.Exposure:R}", pText = plain == null ? "missing" : $"{plain.Contraband}, {plain.Exposure:R}";
                bool noOud = !json.Contains("oud");
                Check(loaded != null && loaded.Contraband && loaded.Exposure == e && plain != null && !plain.Contraband && plain.Exposure == 0 && noOud,
                    $"stealth smuggle: a career save with the contraband job active ({json.Length} chars) loads it with Contraband, Exposure {lText} (True, {e:R}) beside a plain cargo job with {pText} (False, 0); the save holds no \"oud\" ({noOud})");
            }
            finally { foreach (var b in live) b.C.DropContact(); }
        }

        // 16m, the line: one short line with no "oud" (the save scan of 15 reads for it). The grade is of the posted pay, so a
        // fresh job reads 1.25 (the brief's 1.88 is the multiple of the plain pay, which the line does not print).
        void StealthSmuggleLineChecks()
        {
            var fresh = new Contract { Id = 900820, Kind = JobKind.Cargo, Title = "QA contraband", Contraband = true, Exposure = 0 };
            var seen = new Contract { Id = 900821, Kind = JobKind.Cargo, Title = "QA contraband", Contraband = true, Exposure = 0.7 };
            string l0 = Career.ContrabandLine(fresh), l7 = Career.ContrabandLine(seen);
            bool OneLine(string t) => t.Length > 0 && t.Length <= 90 && !t.Contains("\n") && !t.Contains("\r") && !t.Contains("oud");
            Check(OneLine(l0) && OneLine(l7) && l0.Contains("1.25") && l7.Contains("0.31") && l0 != l7,
                $"stealth smuggle: the line of a fresh job is {l0.Length} chars (1 to 90), one line, no \"oud\", holds \"1.25\": \"{l0}\"; at Exposure 0.7 it is {l7.Length} chars and holds \"0.31\": \"{l7}\"");
        }

        // 16m, the scene: the Jobs page's active card and board card print the contraband line (UI Toolkit). The page is built
        // into a box of its own, as 16j does; the board card needs a dock, which is set by hand and put back, with the yard's draft.
        // The two jobs carry different Exposure, so each card must print its own job's line.
        void StealthSmuggleShellChecks()
        {
            if (UI.UiShell.Disabled)
            {
                log.WriteLine("  SKIP stealth smuggle: no UI Toolkit shell (-fowlegacyui): the contraband line on the Jobs page's cards; the classic panel draws it with GUILayout.Label and no QA reads IMGUI text");
                return;
            }
            if (!app.ShellActive) { Check(false, "stealth smuggle: no UI Toolkit shell, and -fowlegacyui is not on"); return; }
            log.WriteLine("  SKIP stealth smuggle: the classic panel's two lines (ActiveJobs, ContractBoard) are GUILayout.Label calls that no QA reads; the UI Toolkit cards are checked");
            var setDocked = typeof(Career).GetProperty("Docked").GetSetMethod(true);
            var dock0 = career.Docked;
            var draft0 = Get<ShipBuild>("yardDraft");
            var yardPort0 = Get<Port>("yardPort");
            var head0 = Get<UIE.Label>("yardHead");
            var act = new Contract { Id = 900830, Kind = JobKind.Cargo, Title = "Unmanifested ice", From = "Ceres", To = "Vesta", Tonnes = 20, Pay = 9000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, Contraband = false, Exposure = 0.2, Key = "qa/smuggle-active" };
            var post = new Contract { Id = 900831, Kind = JobKind.Cargo, Title = "Unlisted med packs", From = "Vesta", To = "Ceres", Tonnes = 20, Pay = 9000, Days = 100, Contraband = false, Exposure = 0, Key = "qa/smuggle-board" };
            act.Contraband = true; post.Contraband = true;
            string textA = Career.ContrabandLine(act), textP = Career.ContrabandLine(post);
            act.Contraband = false; post.Contraband = false;
            (int a, int p) Lines()
            {
                var box = new UIE.VisualElement();
                Call("BuildJobs", box);
                return (CountText(box, textA), CountText(box, textP));
            }
            try
            {
                career.Active.Add(act);
                var base0 = Lines();
                act.Contraband = true;
                var active = Lines();

                setDocked.Invoke(career, new object[] { Array.Find(Career.Ports, p => p.Name == "Vesta") });
                act.Contraband = false;
                career.Board.Add(post);
                var baseD = Lines();
                act.Contraband = true;
                var activeD = Lines();
                post.Contraband = true;
                var both = Lines();
                act.Contraband = false; post.Contraband = false;
                var none = Lines();
                Check(textA != textP && active.a == base0.a + 1 && active.p == base0.p && activeD.a == baseD.a + 1 && activeD.p == baseD.p
                      && both.a == baseD.a + 1 && both.p == baseD.p + 1 && none.a == baseD.a && none.p == baseD.p,
                    $"stealth smuggle: the Jobs page prints the contraband line under a contraband job's text (active card \"{textA}\", board card \"{textP}\"): undocked, the active card's line {base0.a} -> {active.a} copies (+1), the board card's {base0.p} -> {active.p} (+0); docked at Vesta with a plain board job {baseD.a}/{baseD.p} -> {activeD.a}/{activeD.p} with the active card contraband (+1/+0), {both.a}/{both.p} with the board card too (+1/+1), {none.a}/{none.p} with both set plain (back to {baseD.a}/{baseD.p})");
            }
            finally
            {
                career.Board.Remove(post);
                career.Active.Remove(act);
                setDocked.Invoke(career, new object[] { dock0 });
                Put("yardDraft", draft0); Put("yardPort", yardPort0); Put("yardHead", head0);
                Put("jobsSig", null); // the page rebuilds from the real jobs
            }
        }
    }
}
