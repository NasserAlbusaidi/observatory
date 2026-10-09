using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FogOfWar.OpenWorld;
using UnityEngine;
using UIE = UnityEngine.UIElements;

namespace FogOfWar
{
    // 16. Stowable radiators and the heat sink (Career.Heat.cs, docs/design-combat.md Phase 3a): the constants, the yard rows
    // and the build, the soak of one update, the heat paths (burn, dry, tender, dock), what the raider sees, the planner's
    // hold, the save and the call's cost, the raider that loses a dark hull (3b), the quiet watch on Pallas (3c: a survey whose
    // dwell counts only unseen time, its board slot, its save and its line), contraband (3d), the corvette (3e), then in the scene the Signature, Heat sink,
    // Raiders and Raider track rows and the SEEN and DARK pills (the rows alone under -fowlegacyui), and the Jobs page's
    // quiet line. Headless parts run on a system of their own at the pursuit check's date, the planner at the scene's date
    // as 15 does. Every [calc] number was worked by hand and never run: every check logs what it measured.
    public partial class CareerQA
    {
        IEnumerator StealthChecks()
        {
            SolarSystem qs = null;
            if (Guard("stealth system", () => qs = SolarSystem.Build()))
            {
                Guard("stealth constants", StealthConstantChecks);
                Guard("stealth build", StealthBuildChecks);
                Guard("stealth soak", () => StealthSoakChecks(qs));
                Guard("stealth heat paths", () => StealthHeatChecks(qs));
                Guard("stealth seen", () => StealthSeenChecks(qs));
                Guard("stealth planner", () => StealthPlannerChecks(qs));
                Guard("stealth save", () => StealthSaveChecks(qs));
                Guard("stealth lost", () => StealthLostChecks(qs));
                Guard("stealth scout", () => StealthScoutChecks(qs));
                Guard("stealth smuggle", () => StealthSmuggleChecks(qs));
                Guard("stealth hulls", () => StealthHullChecks(qs));
            }
            yield return StealthSceneChecks();
            Guard("stealth scout lines", StealthScoutShellChecks);
            Guard("stealth smuggle lines", StealthSmuggleShellChecks);
            Guard("stealth hull yard", StealthHullShellChecks);
            yield return StealthLineChecks(); // 16k and 16l (CareerQA.StealthLines.cs)
        }

        static bool Near(double v, double want, double tol) => Math.Abs(v - want) <= tol;

        // a 15 bench (warmed, never burned) with the stowable radiators fitted: 312 t dry, 200 t of fuel
        static Bench Dark(SolarSystem qs, D3 at, D3 vel)
        {
            var b = SigBench(qs, at, vel);
            b.C.Build.pick[(int)Slot.Radiators] = 1;
            b.C.Apply(b.Ship);
            return b;
        }

        // one update ending at jdEnd, its length the date's own difference: no rounding of the date enters the soak
        static void StealthTickTo(Bench b, double jdEnd)
        {
            double dt = (jdEnd - b.Jd) * 86400;
            b.Jd = jdEnd;
            b.C.Tick(b.Sys, b.Ship, jdEnd, dt);
        }

        // 16a
        void StealthConstantChecks()
        {
            double w = Signature.ColdW(0.3), stowed = Signature.RangeKm(0, Signature.StockKm, w), cold = Signature.RangeKm(0);
            Check(Near(w, 180000, 1) && Near(stowed, 3000, 1e-6) && Near(cold, 10000, 1e-6),
                $"stealth: rho 0.3 radiates {w:N3} W (180,000 ± 1) and is seen at {stowed:0.000000} km (3,000 ± 1e-6); radiators out, {cold:0.000000} km (10,000 ± 1e-6)");
            string h2 = Signature.HoldText(7200), h36 = Signature.HoldText(2160);
            Check(h2 == "2 h" && h36 == "36 min", $"stealth: HoldText(7200) \"{h2}\" (2 h), HoldText(2160) \"{h36}\" (36 min)");

            double perT = Career.SinkSecondsPerT;
            double[] tonnes = { 50, 100, 200, 300, 400 }, want = { 10465, 20930, 41860, 62790, 83720 };
            bool full = Near(perT, 209.3, 1e-9);
            var got = new List<string>();
            for (int i = 0; i < tonnes.Length; i++)
            {
                double s = tonnes[i] * perT;
                full &= Near(s, want[i], 1e-6);
                got.Add($"{tonnes[i]:0} t {s:0.000000} s");
            }
            Check(full, $"stealth: a tonne of water takes {perT:0.000000000} s of 2 MW to warm 100 K (209.3 ± 1e-9); time to full: {string.Join(", ", got)} (10,465 / 20,930 / 41,860 / 62,790 / 83,720, each ± 1e-6)");

            // a burn cannot hide: 180 kW against 24.5 TW is 7e-8 of the range
            double p500 = Signature.JetW(500, Ship.G0), dflt = Signature.RangeKm(p500), withCold = Signature.RangeKm(p500, Signature.StockKm, w);
            double rel = Math.Abs(withCold - dflt) / dflt;
            Check(rel <= 1e-6, $"stealth: a stock burn at 500 t is seen at {dflt / Frame.AU:0.0000} AU by default and {withCold / Frame.AU:0.0000} AU with the radiators stowed (0.2340), {rel:E2} apart (within 1e-6)");

            var stock = ShipBuild.Stock();
            var draft = ShipBuild.Stock();
            draft.pick[(int)Slot.Radiators] = 1;
            var yard = typeof(OpenSystemScene).GetMethod("YardReadouts", BindingFlags.Static | BindingFlags.NonPublic);
            var rows = (List<(string key, string now, string after)>)yard.Invoke(null, new object[] { stock, draft });
            var seen = rows.Find(r => r.key == "Seen at, full burn · cold");
            var dark = rows.Find(r => r.key == "Dark, full tank");
            var sees = rows.Find(r => r.key == "Sees a cold hull at");
            Check(seen.key != null && seen.now == "0.234 AU · 10,000 km" && seen.after == "0.234 AU · 3,000 km stowed",
                $"stealth: the yard row \"{seen.key ?? "-"}\" reads stock \"{seen.now}\" (0.234 AU · 10,000 km), stowable draft \"{seen.after}\" (0.234 AU · 3,000 km stowed)");
            Check(dark.key != null && dark.now == "none: fixed radiators" && dark.after == "11.6 h; tracked 36 min after a burn",
                $"stealth: the yard row \"{dark.key ?? "-"}\" reads stock \"{dark.now}\" (none: fixed radiators), stowable draft \"{dark.after}\" (11.6 h; tracked 36 min after a burn)");
            Check(sees.key != null && sees.now == "10,000 km" && sees.after == "10,000 km",
                $"stealth: the yard row \"{sees.key ?? "-"}\" reads stock \"{sees.now}\", stowable draft \"{sees.after}\" (10,000 km both)");
        }

        // 16b
        void StealthBuildChecks()
        {
            var stock = ShipBuild.Stock();
            var st = ShipBuild.Stock();
            st.pick[(int)Slot.Radiators] = 1;
            Check(ShipBuild.StockPick.Length == 6 && ShipBuild.StockPick[5] == 0 && !stock.Stowable && Near(stock.DryMass, 300, 1e-9),
                $"stealth: the stock pick has {ShipBuild.StockPick.Length} slots (6), radiators pick {ShipBuild.StockPick[ShipBuild.StockPick.Length - 1]} (0), stock stowable {stock.Stowable} (False), dry {stock.DryMass:0.000000000} t (300 ± 1e-9)");
            Check(Near(st.DryMass, 312, 1e-9) && Near(st.GFull, 0.9765625, 1e-9) && Near(st.GEmpty, 1.6026, 1e-4) && Near(st.DvFull, 4953.2, 0.1) && Near(st.StowRho, 0.3, 1e-12) && st.Stowable,
                $"stealth: the stowable build is {st.DryMass:0.000000000} t dry (312 ± 1e-9), {st.GFull:0.0000000000} g full (0.9765625 ± 1e-9), {st.GEmpty:0.000000} g empty (1.6026 ± 1e-4), {st.DvFull:0.000} km/s (4,953.2 ± 0.1), rho {st.StowRho} (0.3), stowable {st.Stowable}");

            var old = JsonUtility.FromJson<CareerSave>("{\"build\":{\"hull\":\"FWO-052 light frigate\",\"pick\":[1,1,1,2,0]}}");
            var c = new Career { Sys = sys };
            c.FromSave(old, new Ship());
            Check(c.Build.pick.Length == 6 && c.Build.pick[5] == 0 && !c.Build.Stowable && c.Build.Hull == ShipBuild.Hulls[0],
                $"stealth: an old 5-pick build loads as {c.Build.pick.Length} picks ({string.Join(",", c.Build.pick)}), radiators pick {c.Build.pick[c.Build.pick.Length - 1]} (0), stowable {c.Build.Stowable} (False)");
        }

        // 16c: updates on a Dark bench, the last loud moment set by hand
        void StealthSoakChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            string Kind(Bench b) => b.C.Call == null ? "no call" : b.C.Call.Kind.ToString();

            var one = Dark(qs, at, vel);
            one.Ship.LastLoudJd = one.Jd; // a burn just ended
            one.Tick(3600);
            var c1 = one.C;
            Check(Near(c1.SinkK, 8.6001, 1e-3) && c1.DarkReady(one.Ship) && Near(c1.HoldS(one.Ship), 2160, 1e-9) && c1.Stowed(one.Ship, one.Jd),
                $"stealth: a burn just ended, one update of 3,600 s: {c1.SinkK:0.000000} K (8.6001 ± 1e-3), dark-ready {c1.DarkReady(one.Ship)}, hold {c1.HoldS(one.Ship):0.000000000} s (2,160 ± 1e-9), stowed {c1.Stowed(one.Ship, one.Jd)}");

            // ten updates against one of the same length; the step is a whole number of the date's grid (2^-31 d at JD 2.46e6), so no date rounding enters
            double q = 1.0 / 2147483648.0;
            double step = Math.Round(360 / 86400.0 / q) * q;
            var a = Dark(qs, at, vel);
            var t = Dark(qs, at, vel);
            double j0 = a.Jd;
            a.Ship.LastLoudJd = a.Jd; t.Ship.LastLoudJd = t.Jd;
            StealthTickTo(a, j0 + 10 * step);
            for (int k = 1; k <= 10; k++) StealthTickTo(t, j0 + k * step);
            Check(Near(t.C.SinkK, a.C.SinkK, 1e-9) && a.C.SinkK > 8,
                $"stealth: ten updates of {step * 86400:0.0000} s soak {t.C.SinkK:0.000000000000} K, one of {10 * step * 86400:0.0000} s {a.C.SinkK:0.000000000000} K, {Math.Abs(t.C.SinkK - a.C.SinkK):E2} apart (within 1e-9)");

            var big = Dark(qs, at, vel);
            big.Ship.LastLoudJd = big.Jd;
            big.Tick(16667);
            Check(Near(big.C.SinkK, 17.2002, 1e-3) && !big.C.Stowed(big.Ship, big.Jd),
                $"stealth: one update of 16,667 s from a fresh window soaks only its 7,200 s: {big.C.SinkK:0.000000} K (17.2002 ± 1e-3), stowed at the end {big.C.Stowed(big.Ship, big.Jd)} (False), dark-ready {big.C.DarkReady(big.Ship)}");

            var half = Dark(qs, at, vel);
            half.Ship.LastLoudJd = half.Jd - 1.5 / 24;
            half.Tick(3600);
            Check(Near(half.C.SinkK, 4.3000, 1e-3), $"stealth: the last loud moment 1.5 h before the update soaks its last 1,800 s: {half.C.SinkK:0.000000} K (4.3000 ± 1e-3)");

            var never = Dark(qs, at, vel);
            never.Tick(3600);
            Check(Near(never.C.SinkK, 0, 1e-12), $"stealth: never burned, 3,600 s soak {never.C.SinkK:0.000000000000} K (0)");

            // 3b: a chase is quiet time too: it soaks (3a: 0 K), and 100,000 km off the raider is lost in the same update
            var ch = Dark(qs, at, vel);
            var e = ch.C.StartContact(ch.Job, qs, ch.Ship, ch.Jd, at - at.Norm * 1e5, vel);
            ch.Tick(3600);
            Check(Near(ch.C.SinkK, 8.6001, 1e-3) && ch.C.Encounter == null,
                $"stealth: a chase on with no loud moment, one update of 3,600 s: {ch.C.SinkK:0.000000} K (8.6001 ± 1e-3), chase {(ch.C.Encounter == e ? "same" : ch.C.Encounter == null ? "gone" : "other")} (gone: held 3,600 s ≥ 2,160), {Kind(ch)}");
            ch.C.DropContact();

            var full = Dark(qs, at, vel);
            full.Ship.Fuel = 50;
            full.C.SinkK = 95;
            full.Ship.LastLoudJd = full.Jd;
            full.Tick(3600);
            var cf = full.C;
            Check(Near(cf.SinkK, 100, 1e-12) && !cf.DarkReady(full.Ship) && Near(cf.HoldS(full.Ship), 7200, 1e-9) && Near(cf.ColdW(full.Ship, full.Jd), 2e6, 1e-6),
                $"stealth: at 50 t from 95 K a window of 3,600 s ends at {cf.SinkK:0.000000000000} K (100), dark-ready {cf.DarkReady(full.Ship)} (False), hold {cf.HoldS(full.Ship):0.0} s (7,200), cold power {cf.ColdW(full.Ship, full.Jd):N0} W (2,000,000)");

            // the sink fills inside one 16,667 s update: the seen time is what 1x frames would count, 2,160 s of the stowed hold, then 7,200 s of the track from the fill at 4,186 s
            var fill = Dark(qs, at, vel);
            fill.Ship.Fuel = 50;
            fill.C.SinkK = 60;
            fill.Ship.LastLoudJd = fill.Jd;
            fill.Tick(16667);
            double fillSeen = fill.C.SeenSeconds(fill.Ship, fill.Jd, 16667);
            Check(Near(fill.C.SinkK, 100, 1e-12) && Near(fillSeen, 5174, 0.01),
                $"stealth: 50 t from 60 K, one update of 16,667 s fills the sink at {fill.C.SinkK:0.000000000000} K (100); a raider sees {fillSeen:0.000} s of it (5,174 ± 0.01: 2,160 + 3,014, as 1x frames count)");

            var fixedBench = SigBench(qs, at, vel);
            fixedBench.Ship.LastLoudJd = fixedBench.Jd;
            fixedBench.Tick(3600);
            Check(fixedBench.C.SinkK == 0 && !fixedBench.C.DarkReady(fixedBench.Ship) && Near(fixedBench.C.HoldS(fixedBench.Ship), Signature.TrackHoldS, 1e-9),
                $"stealth: fixed radiators in the same window soak {fixedBench.C.SinkK} K (0), dark-ready {fixedBench.C.DarkReady(fixedBench.Ship)} (False), hold {fixedBench.C.HoldS(fixedBench.Ship):0.0} s (7,200)");
        }

        // 16d: the heat leaves with the water, a dry tank is a full sink, a port empties it
        void StealthHeatChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);

            var burn = Dark(qs, at, vel);
            burn.C.SinkK = 50;
            double left0 = burn.C.SinkLeftS(burn.Ship);
            burn.Ship.Fuel = 100; // 100 t burned, outside the window
            burn.Tick(3600);
            double left1 = burn.C.SinkLeftS(burn.Ship);
            Check(Near(burn.C.SinkK, 50, 1e-12) && Near(left0, 20930, 1e-6) && Near(left1, 10465, 1e-6),
                $"stealth: 100 t burned at 50 K leaves the water at {burn.C.SinkK:0.000000000000} K (50 ± 1e-12), the time left goes {left0:0.000000} s to {left1:0.000000} s (20,930 to 10,465, each ± 1e-6)");

            var dry = Dark(qs, at, vel);
            dry.Ship.Fuel = 0;
            dry.Tick(3600);
            Check(Near(dry.C.SinkK, 100, 1e-12) && !dry.C.DarkReady(dry.Ship), $"stealth: a dry tank, one update: {dry.C.SinkK:0.000000000000} K (100), dark-ready {dry.C.DarkReady(dry.Ship)} (False)");

            var t1 = Dark(qs, at, vel);
            t1.Ship.Fuel = 0; t1.C.SinkK = 100;
            t1.C.EmergencyDrop(t1.Ship);
            var t2 = Dark(qs, at, vel);
            t2.Ship.Fuel = 20; t2.C.SinkK = 80;
            t2.C.EmergencyDrop(t2.Ship);
            Check(Near(t1.Ship.Fuel, 30, 1e-9) && Near(t1.C.SinkK, 0, 1e-12) && Near(t2.Ship.Fuel, 50, 1e-9) && Near(t2.C.SinkK, 32, 1e-9),
                $"stealth: the tender into a dry tank at 100 K gives {t1.Ship.Fuel:0.000} t at {t1.C.SinkK:0.000000000000} K (30 t, 0); into 20 t at 80 K, {t2.Ship.Fuel:0.000} t at {t2.C.SinkK:0.000000000} K (50 t, 32 ± 1e-9)");

            // docked in the window: the port takes the heat; undocked, the window is still open
            var d = Dark(qs, at, vel);
            d.C.SinkK = 50;
            d.Ship.LastLoudJd = d.Jd;
            d.Ship.Landed = qs["Ceres"];
            d.Tick(3600);
            string dockedAt = d.C.Docked?.Name ?? "-";
            double docked = d.C.SinkK;
            d.Ship.Landed = null;
            d.Tick(3600);
            Check(dockedAt == "Ceres" && Near(docked, 0, 1e-12) && d.C.Docked == null && Near(d.C.SinkK, 8.6001, 1e-3),
                $"stealth: landed on Ceres in the window at 50 K: docked at {dockedAt} (Ceres), {docked:0.000000000000} K (0); landed off, one update later docked {d.C.Docked?.Name ?? "-"} (-), {d.C.SinkK:0.000000} K (window still open: 8.6001)");
        }

        // 16e: what the raider sees
        void StealthSeenChecks(SolarSystem qs)
        {
            var (vesta, at, vel) = Belt(qs);
            string Kind(Bench b) => b.C.Call == null ? "no call" : b.C.Call.Kind.ToString();

            var s0 = SigBench(qs, at, vel);
            SigUpdate(s0, 0, -0.5);
            double stockSeen = s0.C.SeenSeconds(s0.Ship, s0.Jd, 3600);
            var d0 = Dark(qs, at, vel);
            SigUpdate(d0, 0, -0.5);
            double darkSeen = d0.C.SeenSeconds(d0.Ship, d0.Jd, 3600);
            var d1 = Dark(qs, at, vel);
            d1.C.SinkK = 100;
            SigUpdate(d1, 0, -0.5);
            double fullSeen = d1.C.SeenSeconds(d1.Ship, d1.Jd, 3600);
            Check(Near(stockSeen, 3600, 0.01) && Near(darkSeen, 360, 0.01) && Near(fullSeen, 3600, 0.01),
                $"stealth: the last loud moment 30 min before the update is seen {stockSeen:0.000} s of 3,600 on the stock bench (3,600 ± 0.01), {darkSeen:0.000} s on the Dark bench (360 ± 0.01), {fullSeen:0.000} s with the sink full (3,600 ± 0.01)");

            // 40 min after the last loud moment: a stowable hull is cold, a fixed one still tracked
            var wd = Dark(qs, at, vel);
            wd.Ship.LastLoudJd = wd.Jd - 40.0 / 1440;
            var wdW = wd.C.RaiderWatch(qs, wd.Ship, wd.Jd, out double wdLeft);
            var ws = SigBench(qs, at, vel);
            ws.Ship.LastLoudJd = ws.Jd - 40.0 / 1440;
            var wsW = ws.C.RaiderWatch(qs, ws.Ship, ws.Jd, out double wsLeft);
            Check(wdW == Career.Watch.Cold && Near(wdLeft, -240, 1) && wsW == Career.Watch.Tracked && Near(wsLeft, 4800, 1),
                $"stealth: 40 min after the last loud moment the Dark bench is {wdW} with {wdLeft:0.00} s left (Cold, -240 ± 1), the stock bench {wsW} with {wsLeft:0.00} s left (Tracked, 4,800 ± 1)");

            // the roll
            var rd = Dark(qs, at, vel);
            var rdRoll = SigRoll(rd);
            SigUpdate(rd, 0, -40.0 / 60, vesta, 1e6);
            string darkKind = Kind(rd);
            bool darkOk = rd.C.Call == null && rd.C.Encounter == null && rdRoll.Draws == 1;
            rd.C.DropContact();
            var rs = SigBench(qs, at, vel);
            var rsRoll = SigRoll(rs);
            SigUpdate(rs, 0, -40.0 / 60, vesta, 1e6);
            string stockKind = Kind(rs);
            bool stockOk = rs.C.Call?.Kind == Career.RouteKind.Pirate && rs.C.Encounter != null && rsRoll.Draws == 1;
            rs.C.DropContact();
            Check(darkOk && stockOk,
                $"stealth: cold 40 min, no loud time in the update, the roll that always hits: the Dark bench {darkKind}, {rdRoll.Draws} draws (no call, 1), the stock bench {stockKind}, {rsRoll.Draws} draws (Pirate, 1)");
        }

        // 16f: the planner's hold at the scene's date
        void StealthPlannerChecks(SolarSystem qs)
        {
            double j0 = jd;
            Body from = qs["Mars"], to = qs["Vesta"];
            double d = (qs.PosAt(to, j0) - qs.PosAt(from, j0)).Mag - Ship.Standoff(to);
            var full = Threat.Of(qs, from, D3.Zero, to, j0, d, Ship.G0);
            var dark = Threat.Of(qs, from, D3.Zero, to, j0, d, Ship.G0, 0, 2160);
            // #162: a port docks the ship at once, so the hold does not reach Vesta; Pallas has no port and holds it in the open
            Body pallas = qs["Pallas"];
            double dp = (qs.PosAt(pallas, j0) - qs.PosAt(from, j0)).Mag - Ship.Standoff(pallas);
            var fullP = Threat.Of(qs, from, D3.Zero, pallas, j0, dp, Ship.G0);
            var darkP = Threat.Of(qs, from, D3.Zero, pallas, j0, dp, Ship.G0, 0, 2160);
            Check(Near(full.Hours, 2.00, 0.05) && Near(dark.Hours, full.Hours, 1e-9) && darkP.Hours < fullP.Hours && darkP.Hours > 0,
                $"stealth: Mars orbit → Vesta at 1 g: {full.Hours:0.00} h with the default hold, {dark.Hours:0.00} h with a hold of 2,160 s (2.00 ± 0.05, the same: no hold at a port); Mars orbit → Pallas: {fullP.Hours:0.00} h against {darkP.Hours:0.00} h ({fullP.Arriving:0.00} and {darkP.Arriving:0.00} arriving; fewer, above 0)");
            string stock = Threat.Line(full), short_ = Threat.Line(full, 2160);
            Check(stock.Contains("2 h after one") && short_.Contains("36 min after one"),
                $"stealth: the planner line reads \"{stock}\" and, for a hold of 2,160 s, \"{short_}\"");
        }

        // 16g: the save, and what a call costs
        void StealthSaveChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var b = Dark(qs, at, vel);
            b.C.SinkK = 42.5;
            string json = JsonUtility.ToJson(b.C.ToSave(b.Ship));
            var c = new Career { Sys = qs };
            c.FromSave(JsonUtility.FromJson<CareerSave>(json), new Ship());
            Check(Near(c.SinkK, 42.5, 1e-12) && c.Build.Stowable && !json.Contains("oud"),
                $"stealth: 42.5 K and the stowable build go through a save ({json.Length} chars) and come back as {c.SinkK:0.000000000000} K (42.5 ± 1e-12), stowable {c.Build.Stowable}; the save holds no loud time ({!json.Contains("oud")})");

            c.SinkK = 30;
            c.FromSave(JsonUtility.FromJson<CareerSave>("{\"credits\":1000,\"fuel\":250}"), new Ship());
            double none = c.SinkK;
            c.FromSave(JsonUtility.FromJson<CareerSave>("{\"sinkK\":250}"), new Ship());
            double clamped = c.SinkK;
            c.SinkK = 30;
            c.FromSave(new CareerSave { sinkK = double.NaN }, new Ship());
            double nan = c.SinkK;
            c.SinkK = 30;
            c.FromSave(new CareerSave { sinkK = -5 }, new Ship());
            double neg = c.SinkK;
            Check(none == 0 && clamped == 100 && nan == 0 && neg == 0,
                $"stealth: an old save with no sink loads at {none} K (0), 250 K clamps to {clamped} (100), NaN loads at {nan} (0), -5 at {neg} (0)");

            // the cost of Soak and HoldS, which the Tick and the HUD call every frame
            b.Tick(3600);
            b.Ship.LastLoudJd = b.Jd - 0.5 / 24;
            var soak = (Action<Ship, double>)Delegate.CreateDelegate(typeof(Action<Ship, double>), b.C, typeof(Career).GetMethod("Soak", F));
            const int N = 100000;
            double acc = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < N; i++) soak(b.Ship, 3600);
            for (int i = 0; i < N; i++) acc += b.C.HoldS(b.Ship);
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds;
            Perf(ms < 50, $"stealth: {N:N0} calls of Soak and {N:N0} of HoldS take {ms:0.0} ms (under 50 ms; {acc / N:0.0} s hold)");
        }

        // 16i: a chasing raider loses a dark hull (Career.Pirates.cs LoseInS, docs/design-combat.md Phase 3b). The headless raider
        // never moves (Tick does not advance it), and that is what holds the range cases. Ticks are 100 s, as the Fly loop steps.
        void StealthLostChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var live = new List<Bench>();
            Bench Keep(Bench b) { live.Add(b); return b; }
            Encounter Chase(Bench b, double km) => b.C.StartContact(b.Job, qs, b.Ship, b.Jd, at - at.Norm * km, vel);
            void Ticks(Bench b, int n, double dt = 100) { for (int i = 0; i < n; i++) b.Tick(dt); }
            string State(Bench b, Encounter e) => b.C.Encounter == e ? "on" : b.C.Encounter == null ? "gone" : "other";
            string Lose(Bench b) { double l = b.C.LoseInS(b.Ship, b.Jd); return double.IsNaN(l) ? "NaN" : $"{l:0.0} s"; }
            string Why(Bench b) => $"; MakeCall found no raider at the Belt point (patrol {Threat.PatrolAt(qs, b.Ship.Pos, b.Jd)?.Name ?? "-"}, call {(b.C.Call == null ? "none" : b.C.Call.Kind.ToString())})";
            try
            {
                string note = Career.OffNote(Career.BreakOff.Lost, null, "Pallas");
                Check(note == "Track lost: the raider breaks off, back to Pallas", $"stealth: the lost-track note reads \"{note}\" (Track lost: the raider breaks off, back to Pallas)");

                // 2,160 s of hold, a raider 100,000 km off (outside the 3,000 km stowed reach), never burned
                var lb = Keep(Dark(qs, at, vel));
                var le = Chase(lb, 1e5);
                Ticks(lb, 21);
                string on21 = State(lb, le), lose21 = Lose(lb);
                Ticks(lb, 1);
                string said = lb.Said.Count > 0 ? lb.Said[lb.Said.Count - 1].Text : "-";
                Check(on21 == "on" && State(lb, le) == "gone" && lb.Heard(Career.Happening.RaiderGone, "Track lost: the raider breaks off") && Near(lb.C.SinkK, 5.2556, 1e-3),
                    $"stealth: a Dark hull, raider 100,000 km off, never burned: after 2,100 s the chase is {on21} (on; {lose21} left, 60), after 2,200 s {State(lb, le)} (gone), heard \"{said}\"; sink {lb.C.SinkK:0.000000} K (5.2556 ± 1e-3, 2,200 quiet chase seconds)");
                var roll = SigRoll(lb);
                lb.Tick(3600);
                Check(roll.Draws == 0 && lb.C.Call == null && lb.C.Encounter == null,
                    $"stealth: one update of 3,600 s after the loss, the roll that always hits: {roll.Draws} draws (0; the gap holds for 0.5 d), call {(lb.C.Call == null ? "none" : lb.C.Call.Kind.ToString())} (none), chase {State(lb, le)} (gone)");

                // a stock hull has fixed radiators: nothing to hide, the raider keeps the track
                var sb = Keep(SigBench(qs, at, vel));
                var se = Chase(sb, 1e5);
                Ticks(sb, 8, 3600);
                Check(State(sb, se) == "on" && Near(sb.C.SinkK, 0, 1e-12) && double.IsNaN(sb.C.LoseInS(sb.Ship, sb.Jd)),
                    $"stealth: a stock hull, raider 100,000 km off, 8 updates of 3,600 s: chase {State(sb, se)} (on), sink {sb.C.SinkK:0.000000000000} K (0), loses in {Lose(sb)} (NaN)");

                // inside the stowed reach (3,000 km) the raider sees the hull whatever it does
                var cb = Keep(Dark(qs, at, vel));
                var ce = Chase(cb, 2000);
                Ticks(cb, 30);
                Check(State(cb, ce) == "on" && double.IsNaN(cb.C.LoseInS(cb.Ship, cb.Jd)),
                    $"stealth: a Dark hull, raider 2,000 km off, 3,000 s: chase {State(cb, ce)} (on), range {ce.Range:N0} km (2,000), loses in {Lose(cb)} (NaN), sink {cb.C.SinkK:0.000000} K");

                // a burn is a loud moment: 60 loud seconds at 1,000 s restart the clock, and that update soaks nothing
                var bb = Keep(Dark(qs, at, vel));
                var be = Chase(bb, 1e5);
                Ticks(bb, 10);
                bb.Ship.LoudSeconds += 60; bb.Ship.LastLoudJd = bb.Jd + 60 / 86400.0;
                bb.Tick(60);
                double sink1060 = bb.C.SinkK;
                Ticks(bb, 21);
                string on3160 = State(bb, be), lose3160 = Lose(bb);
                Ticks(bb, 1);
                Check(Near(sink1060, 2.3889, 1e-3) && on3160 == "on" && State(bb, be) == "gone",
                    $"stealth: a burn ending at 1,060 s: the loud update soaks 0 (sink {sink1060:0.000000} K after 1,000 quiet seconds, 2.3889 ± 1e-3); at 3,160 s (2,100 s after the burn) the chase is {on3160} (on; {lose3160} left, 60), at 3,260 s {State(bb, be)} (gone); sink {bb.C.SinkK:0.000000} K");

                // a full sink puts the radiators out: the raider keeps the fix
                var fb = Keep(Dark(qs, at, vel));
                fb.C.SinkK = 100;
                var fe = Chase(fb, 1e5);
                Ticks(fb, 22);
                Check(State(fb, fe) == "on" && double.IsNaN(fb.C.LoseInS(fb.Ship, fb.Jd)) && Near(fb.C.SinkK, 100, 1e-12),
                    $"stealth: a Dark hull with the sink full, raider 100,000 km off, 2,200 s: chase {State(fb, fe)} (on), loses in {Lose(fb)} (NaN), sink {fb.C.SinkK:0.000000000000} K (100)");

                // the sink fills inside the chase (50 t, 99 K: full after 104.65 s): from then the raider has the fix
                var xb = Keep(Dark(qs, at, vel));
                xb.Ship.Fuel = 50;
                xb.C.SinkK = 99;
                var xe = Chase(xb, 1e5);
                Ticks(xb, 22);
                Check(State(xb, xe) == "on" && double.IsNaN(xb.C.LoseInS(xb.Ship, xb.Jd)) && Near(xb.C.SinkK, 100, 1e-12) && Near((xb.Jd - xe.FixJd) * 86400, 0, 1e-3),
                    $"stealth: a Dark hull at 50 t from 99 K, raider 100,000 km off, 2,200 s: chase {State(xb, xe)} (on), loses in {Lose(xb)} (NaN), sink {xb.C.SinkK:0.000000000000} K (100), fix {(xb.Jd - xe.FixJd) * 86400:0.000} s old (0: refreshed on every update since the fill)");

                // the call card offers the dark option to a hull that can take it
                const string dark = "Or go dark: cut the drive and coast 36 min out of its sight.";
                var db = Keep(Dark(qs, at, vel));
                bool dMade = db.C.MakeCall(Career.RouteKind.Pirate, db.Job, qs, db.Ship, db.Jd);
                string dText = db.C.Call?.Text;
                string dWhy = dMade ? "" : Why(db);
                db.C.DropContact();
                var kb = Keep(MakeBench(qs, at, vel));
                bool kMade = kb.C.MakeCall(Career.RouteKind.Pirate, kb.Job, qs, kb.Ship, kb.Jd);
                string kText = kb.C.Call?.Text;
                string kWhy = kMade ? "" : Why(kb);
                kb.C.DropContact();
                bool hasDark = dText != null && dText.Contains(dark), noThey = dText != null && !System.Text.RegularExpressions.Regex.IsMatch(dText, @"\b[Tt]hey\b");
                Check(dMade && hasDark && noThey,
                    $"stealth: the Dark hull's call card has the dark sentence {hasDark} (True) and no \"they\" {noThey} (True): \"{SigPart(dText, "Or go dark")}\"" + dWhy);
                bool noDark = kText != null && !kText.Contains("go dark");
                Check(kMade && noDark, $"stealth: the stock hull's call card has no \"go dark\" {noDark} (True): \"{SigPart(kText, "Run:")}\"" + kWhy);
            }
            finally { foreach (var b in live) b.C.DropContact(); }
        }

        // 16j: the quiet watch (Career.Scout.cs, docs/design-combat.md Phase 3c): a survey with Quiet counts only the seconds
        // of an update that no raider could see. Updates of 3,600 s on a Pallas survey bench, the ship in scan range and at rest
        // with it; every plain control is an identical bench with Quiet off. A quiet or seen time is worked from three dates,
        // and a date is a double near 2.46e6 with a grid of 2^-31 d = 40 µs, so those carry tol = 1e-3 s, not the brief's 1e-6 s.
        // The plain controls and the full-credit dwell are exact (the whole update in range, no burn, seen 0) and keep 1e-6 s.
        void StealthScoutChecks(SolarSystem qs)
        {
            const double tol = 1e-3, exact = 1e-6;
            var (_, at, vel) = Belt(qs);
            var pallas = qs[Career.ReconBody];
            Check(pallas != null, $"stealth scout: the recon body {Career.ReconBody} is in the system");
            if (pallas == null) return;
            var live = new List<Bench>();
            double km = 0;
            Bench Make(bool stowable, bool quiet)
            {
                var b = SigBench(qs, at, vel, JobKind.Survey, Career.ReconBody);
                if (stowable) { b.C.Build.pick[(int)Slot.Radiators] = 1; b.C.Apply(b.Ship); }
                b.Job.Quiet = quiet;
                live.Add(b);
                return b;
            }
            // a warm-up update puts the ship in range, so the measured one starts there; the dwell and the seen seconds are its own
            double Run(Bench b, double loudS, double lastH, out double seen, Action before = null)
            {
                SigUpdate(b, 0, double.NaN, pallas, km);
                b.Job.Dwell = 0;
                before?.Invoke();
                SigUpdate(b, loudS, lastH, pallas, km);
                seen = b.C.SeenSeconds(b.Ship, b.Jd, 3600);
                return b.Job.Dwell;
            }
            try
            {
                var probe = Make(false, true);
                double radius = pallas.Radius, range = probe.C.Codex.ScanRange(pallas);
                int level = probe.C.Codex.SensorLevel, mult = level > 0 ? 2 : 1;
                km = radius + (range - radius) / 2;
                Check(km > radius && km < range && level == 0,
                    $"stealth scout: Pallas has radius {radius:N1} km and scan range {range:N1} km; the ship sits {km:N1} km out at rest with it (inside the range, above the radius); stock sensors level {level} (0), dwell counts x{mult} (1)");

                // the 2 h hold ends 1,800 s into the update: half of it is unseen
                var q1 = Make(false, true);
                double d1 = Run(q1, 0, -1.5, out double s1);
                var p1 = Make(false, false);
                double e1 = Run(p1, 0, -1.5, out double t1);
                Check(Near(d1, 1800 * mult, tol * mult) && Near(e1, 3600 * mult, exact * mult) && Near(s1, 1800, tol),
                    $"stealth scout: stock hull, the last loud moment 1.5 h before the update: quiet dwell {d1:0.000000} s (1,800 ± {tol * mult}), seen {s1:0.000000} s (1,800 ± {tol}), plain control {e1:0.000000} s (3,600 ± {exact * mult}, seen {t1:0.000000})");

                // a burn just ended at the update's start: all of it is seen
                var q2 = Make(false, true);
                double d2 = Run(q2, 0, 0, out double s2);
                var p2 = Make(false, false);
                double e2 = Run(p2, 0, 0, out _);
                Check(Near(d2, 0, tol * mult) && Near(e2, 3600 * mult, exact * mult) && Near(s2, 3600, tol),
                    $"stealth scout: stock hull, a burn just ended: quiet dwell {d2:0.000000} s (0 ± {tol * mult}), seen {s2:0.000000} s (3,600 ± {tol}), plain control {e2:0.000000} s (3,600 ± {exact * mult})");

                // 1,800 loud seconds, then the hold's 1,800 s: the two cover the update
                var q3 = Make(false, true);
                double d3 = Run(q3, 1800, -1.5, out double s3);
                var p3 = Make(false, false);
                double e3 = Run(p3, 1800, -1.5, out _);
                Check(Near(d3, 0, tol * mult) && Near(e3, 3600 * mult, exact * mult) && Near(s3, 3600, tol),
                    $"stealth scout: stock hull, 1,800 loud seconds in the update and the hold's 1,800 s: quiet dwell {d3:0.000000} s (0 ± {tol * mult}), seen {s3:0.000000} s (3,600 ± {tol}), plain control {e3:0.000000} s (3,600 ± {exact * mult}: the scan takes off burn seconds only, and this update has none)");

                // 40 min after a burn: the stowed hold (2,160 s) has run out, the stock one (7,200 s) has not
                var q4 = Make(true, true);
                double d4 = Run(q4, 0, -40.0 / 60, out double s4);
                bool stowed = q4.C.Stowed(q4.Ship, q4.Jd);
                double hold = q4.C.HoldS(q4.Ship);
                var p4 = Make(true, false);
                double e4 = Run(p4, 0, -40.0 / 60, out _);
                Check(Near(d4, 3600 * mult, exact * mult) && Near(e4, 3600 * mult, exact * mult) && Near(s4, 0, tol) && stowed && Near(hold, 2160, 1e-9),
                    $"stealth scout: stowable hull, 40 min after a burn: quiet dwell {d4:0.000000} s (3,600 ± {exact * mult}), seen {s4:0.000000} s (0 ± {tol}), plain control {e4:0.000000} s (3,600 ± {exact * mult}), stowed {stowed} (True), hold {hold:0.000000000} s (2,160 ± 1e-9)");
                var k4 = Make(false, true);
                double d4k = Run(k4, 0, -40.0 / 60, out double s4k);
                double hold4k = k4.C.HoldS(k4.Ship);
                Check(Near(d4k, 0, tol * mult) && Near(s4k, 3600, tol) && Near(hold4k, Signature.TrackHoldS, 1e-9),
                    $"stealth scout: stock hull, 40 min after a burn: quiet dwell {d4k:0.000000} s (0 ± {tol * mult}), seen {s4k:0.000000} s (3,600 ± {tol}), hold {hold4k:0.0} s (7,200)");

                // a chase on: a raider watches the ship whatever its hold says. Scan runs before Contacts in Tick, so the chase is
                // still on when the dwell is credited, even if the lose rule ends it later in the update (st5 logs which)
                Encounter ch = null;
                var q5 = Make(true, true);
                double d5 = Run(q5, 0, double.NaN, out _, () => ch = q5.C.StartContact(q5.Job, qs, q5.Ship, q5.Jd, q5.Ship.Pos - q5.Ship.Pos.Norm * 1e5, q5.Ship.Vel));
                string st5 = q5.C.Encounter == null ? "gone" : q5.C.Encounter == ch ? "on" : "other";
                q5.C.DropContact();
                var p5 = Make(true, false);
                double e5 = Run(p5, 0, double.NaN, out _, () => p5.C.StartContact(p5.Job, qs, p5.Ship, p5.Jd, p5.Ship.Pos - p5.Ship.Pos.Norm * 1e5, p5.Ship.Vel));
                p5.C.DropContact();
                var n5 = Make(true, true);
                double dn5 = Run(n5, 0, double.NaN, out _);
                Check(Near(d5, 0, tol * mult) && e5 > 0 && Near(dn5, 3600 * mult, exact * mult),
                    $"stealth scout: stowable hull, never burned, a chase on for the update (afterwards {st5}): quiet dwell {d5:0.000000} s (0 ± {tol * mult}), plain control {e5:0.000000} s (above 0), the same hull with no chase {dn5:0.000000} s (3,600 ± {exact * mult})");

                // the ship leaves the range about 3/4 into the update (the Hermite curve is a smoothstep, S(0.75) = 0.84375 of the way
                // out), and the first 1,800 s are seen: the in-range seconds lie inside the seen ones up to 1,800, so only q - seen count.
                // The whole-update rule would credit min(q, 3,600 - seen) = 1,800 here
                double kmOut = km + (range - km) / 0.84375;
                double Leave(Bench b, out double seen)
                {
                    SigUpdate(b, 0, double.NaN, pallas, km);
                    b.Job.Dwell = 0;
                    SigUpdate(b, 0, -1.5, pallas, kmOut);
                    seen = b.C.SeenSeconds(b.Ship, b.Jd, 3600);
                    return b.Job.Dwell;
                }
                var q6 = Make(false, true);
                double d6 = Leave(q6, out double s6);
                var p6 = Make(false, false);
                double e6 = Leave(p6, out _);
                double inRange = e6 / mult, want6 = Math.Max(0, inRange - s6) * mult, old6 = Math.Min(inRange, 3600 - s6) * mult;
                Check(inRange > 1900 && inRange < 3500 && Near(s6, 1800, tol) && Near(d6, want6, tol * mult) && d6 < old6 - 100,
                    $"stealth scout: stock hull leaving the range ({kmOut:N1} km out at the update's end), the last loud moment 1.5 h before it: in range {inRange:0.000} s (1,900 to 3,500), seen {s6:0.000000} s (1,800 ± {tol}), quiet dwell {d6:0.000000} s (in range - seen = {want6:0.000000} ± {tol * mult}, under the whole-update rule's {old6:0.000000} by over 100)");

                // landed on the target: no raider reaches a landed ship (RouteTick, Contacts), so the hold after the landing burn does not count
                var q7 = Make(false, true);
                double d7 = Run(q7, 0, 0, out double s7, () => q7.Ship.Landed = pallas);
                Check(Near(d7, 3600 * mult, exact * mult) && Near(s7, 3600, tol),
                    $"stealth scout: stock hull landed on {Career.ReconBody}, a burn just ended: quiet dwell {d7:0.000000} s (3,600 ± {exact * mult}; with the ship aloft check 2 has 0), seen {s7:0.000000} s (3,600 ± {tol})");
            }
            finally { foreach (var b in live) b.C.DropContact(); }

            StealthScoutBoardChecks(qs, at, vel);
            StealthScoutSaveChecks(qs, at, vel);
            string line = Career.QuietLine;
            Check(!line.Contains("oud") && !line.Contains("\n") && line.Length > 0,
                $"stealth scout: the quiet line is {line.Length} chars, one line, with no \"oud\" (the save scan of 15 reads for it): \"{line}\"");
        }

        // 16j, the board: slot 8 of Ceres and Vesta over 40 days, and no such slot elsewhere
        void StealthScoutBoardChecks(SolarSystem qs, D3 at, D3 vel)
        {
            const int days = 40;
            var live = new List<Bench>();
            try
            {
                foreach (string name in new[] { "Ceres", "Vesta" })
                {
                    var b = BoardBench(qs, at, vel, name, live);
                    int posts = 0, bad = 0;
                    double lo = double.MaxValue, hi = 0;
                    string first = "-", others = "-";
                    for (int i = 0; i < days; i++)
                    {
                        b.Jd = PursuitCheck.Jd0 + i;
                        b.C.Tick(qs, b.Ship, b.Jd, 0);
                        if (name == "Ceres" && i == 0)
                        {
                            var rest = new List<string>();
                            foreach (var j in b.C.Board) if (!j.Key.EndsWith("/8")) rest.Add($"{j.Key} {j.Title}");
                            others = string.Join("; ", rest);
                        }
                        foreach (var j in b.C.Board)
                        {
                            if (!j.Key.EndsWith("/8")) continue;
                            posts++;
                            lo = Math.Min(lo, j.Pay); hi = Math.Max(hi, j.Pay);
                            double wait = (Career.DwellSeconds + Signature.TrackHoldS) / 86400;
                            bool ok = j.Kind == JobKind.Survey && j.Quiet && j.To == Career.ReconBody && j.From == name && j.DwellNeeded == Career.DwellSeconds
                                      && j.Title == "Quiet watch on " + Career.ReconBody && !j.Title.Contains("oud") && j.Key == $"{name}/{(long)Math.Floor(b.Jd)}/8"
                                      && j.Pay % 100 == 0 && j.Pay >= 1500 && j.Days > wait && j.ExpressDays > 0 && j.ExpressDays <= j.Days;
                            if (!ok) bad++;
                            if (first == "-") first = $"{j.Key} \"{j.Title}\" {j.Pay:N0} cr, {j.Days:0.000} d, express {j.ExpressDays:0.000} d";
                        }
                    }
                    string pays = posts > 0 ? $"{lo:N0} to {hi:N0}" : "-";
                    Check(posts >= 10 && posts <= 30 && bad == 0,
                        $"stealth scout: {name} posts the quiet watch on {posts} of {days} days (10 to 30), {bad} of them wrong (0: a Survey, Quiet, to {Career.ReconBody}, dwell {Career.DwellSeconds:N0} s, key ending /8, pay a multiple of 100 and at least 1,500, days over the wait); pay {pays} cr; first: {first}");
                    if (name == "Ceres") log.WriteLine($"    stealth scout: Ceres on its first day, the other slots: {others}");
                }
                foreach (string name in new[] { "Mars orbit", "Earth orbit", "Ganymede" })
                {
                    var b = BoardBench(qs, at, vel, name, live);
                    int eights = 0, quiets = 0, jobs = 0;
                    for (int i = 0; i < days; i++)
                    {
                        b.Jd = PursuitCheck.Jd0 + i;
                        b.C.Tick(qs, b.Ship, b.Jd, 0);
                        foreach (var j in b.C.Board) { jobs++; if (j.Key.EndsWith("/8")) eights++; if (j.Quiet) quiets++; }
                    }
                    Check(eights == 0 && quiets == 0 && jobs > 0,
                        $"stealth scout: {name} over {days} days posts {jobs} jobs, {eights} in slot 8 (0), {quiets} quiet (0)");
                }
            }
            finally { foreach (var b in live) b.C.DropContact(); }
        }

        // a career docked at a port by the real path (FindDock lands a ship on a port body), with no job aboard
        static Bench BoardBench(SolarSystem qs, D3 at, D3 vel, string port, List<Bench> live)
        {
            var b = MakeBench(qs, at, vel);
            b.C.Active.Clear();
            b.Ship.Landed = qs[Array.Find(Career.Ports, p => p.Name == port).Body];
            live.Add(b);
            return b;
        }

        // 16j, the save: an old contract has no Quiet, a quiet one keeps it
        void StealthScoutSaveChecks(SolarSystem qs, D3 at, D3 vel)
        {
            var old = JsonUtility.FromJson<Contract>("{\"Id\":1,\"Kind\":2,\"Title\":\"x\"}");
            var quiet = new Contract { Id = 2, Kind = JobKind.Survey, Title = "Quiet watch on Pallas", To = Career.ReconBody, DwellNeeded = Career.DwellSeconds, Quiet = true, Key = "qa/scout-save" };
            string cj = JsonUtility.ToJson(quiet);
            var back = JsonUtility.FromJson<Contract>(cj);
            bool inJson = cj.Contains("\"Quiet\":true");
            Check(old != null && !old.Quiet && back != null && back.Quiet && inJson,
                $"stealth scout: an old contract loads with Quiet {old?.Quiet} (False); a quiet one goes through JsonUtility ({cj.Length} chars) and comes back with Quiet {back?.Quiet} (True), the JSON holds \"Quiet\":true {inJson} (True)");

            var b = SigBench(qs, at, vel, JobKind.Survey, Career.ReconBody);
            b.C.Active.Add(quiet);
            string json = JsonUtility.ToJson(b.C.ToSave(b.Ship));
            var c = new Career { Sys = qs };
            c.FromSave(JsonUtility.FromJson<CareerSave>(json), new Ship());
            var loaded = c.Active.Find(x => x.Id == quiet.Id);
            var plain = c.Active.Find(x => x.Id == b.Job.Id);
            string qText = loaded == null ? "missing" : loaded.Quiet.ToString(), pText = plain == null ? "missing" : plain.Quiet.ToString();
            bool noOud = !json.Contains("oud");
            Check(loaded != null && loaded.Quiet && plain != null && !plain.Quiet && noOud,
                $"stealth scout: a career save with an active quiet watch ({json.Length} chars) loads it with Quiet {qText} (True) beside a plain survey with Quiet {pText} (False); the save holds no \"oud\" ({noOud})");
        }

        static int CountText(UIE.VisualElement e, string text)
        {
            int n = e is UIE.Label l && l.text == text ? 1 : 0;
            foreach (var c in e.Children()) n += CountText(c, text);
            return n;
        }

        // 16j, the scene: the Jobs page's active card and board card print the quiet line (UI Toolkit). The page is built into
        // a box of its own, as 14b does; the board card needs a dock, which is set by hand and put back, with the yard's draft.
        void StealthScoutShellChecks()
        {
            if (UI.UiShell.Disabled)
            {
                log.WriteLine("  SKIP stealth scout: no UI Toolkit shell (-fowlegacyui): the quiet line on the Jobs page's cards; the classic panel draws it with GUILayout.Label and no QA reads IMGUI text");
                return;
            }
            if (!app.ShellActive) { Check(false, "stealth scout: no UI Toolkit shell, and -fowlegacyui is not on"); return; }
            log.WriteLine("  SKIP stealth scout: the classic panel's two lines (ActiveJobs, ContractBoard) are GUILayout.Label calls that no QA reads; the UI Toolkit cards are checked");
            var setDocked = typeof(Career).GetProperty("Docked").GetSetMethod(true);
            var dock0 = career.Docked;
            var draft0 = Get<ShipBuild>("yardDraft");
            var yardPort0 = Get<Port>("yardPort");
            var head0 = Get<UIE.Label>("yardHead");
            var act = new Contract { Id = 900600, Kind = JobKind.Survey, Title = "Quiet watch on Pallas", From = "Ceres", To = Career.ReconBody, Pay = 9000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, DwellNeeded = Career.DwellSeconds, Quiet = false, Key = "qa/scout-active" };
            var post = new Contract { Id = 900601, Kind = JobKind.Survey, Title = "Quiet watch on Pallas", From = "Ceres", To = Career.ReconBody, Pay = 9000, Days = 100, DwellNeeded = Career.DwellSeconds, Quiet = false, Key = "qa/scout-board" };
            int Lines()
            {
                var box = new UIE.VisualElement();
                Call("BuildJobs", box);
                return CountText(box, Career.QuietLine);
            }
            try
            {
                career.Active.Add(act);
                int base0 = Lines();
                act.Quiet = true;
                int active = Lines();

                setDocked.Invoke(career, new object[] { Array.Find(Career.Ports, p => p.Name == "Ceres") });
                act.Quiet = false;
                int baseD = Lines();
                act.Quiet = true;
                int activeD = Lines();
                career.Board.Add(post);
                post.Quiet = true;
                int both = Lines();
                act.Quiet = false; post.Quiet = false;
                int none = Lines();
                Check(active == base0 + 1 && activeD == baseD + 1 && both == baseD + 2 && none == baseD,
                    $"stealth scout: the Jobs page prints the quiet line under a quiet job's text: undocked, the active card {base0} -> {active} copies (+1); docked at Ceres {baseD} -> {activeD} with the active card (+1), {both} with the board card too (+2), {none} with both set plain (back to {baseD})");
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

        // 16h: the ship 1,000,000 km out from Vesta at rest with it, a cargo job aboard, the stowable radiators fitted
        IEnumerator StealthSceneChecks()
        {
            bool pills = !UI.UiShell.Disabled;
            if (pills && !app.ShellActive) { Check(false, "stealth: no UI Toolkit shell, and -fowlegacyui is not on"); yield break; }
            if (!pills) log.WriteLine("  SKIP stealth: no UI Toolkit shell: the DARK and SEEN pills; the rows run alone");
            bool classicWas = GameSettings.ClassicPanel;
            double fuel0 = 0, sink0 = 0;
            ShipBuild build0 = null;
            Contract job = null;
            bool go = Guard("stealth scene setup", () =>
            {
                build0 = career.Build.Clone(); sink0 = career.SinkK; fuel0 = ship.Fuel;
                if (pills && classicWas) GameSettings.ClassicPanel = false; // the pills are on the UI Toolkit HUD
                career.DropContact();
                Put("warpIdx", 0);
                var vesta = sys["Vesta"];
                D3 pV = sys.Pos(vesta, jd);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0; ship.Landed = null; ship.Hold = Hold.Off;
                ship.Pos = pV + pV.Norm * 1e6; ship.Vel = sys.Vel(vesta, jd);
                job = new Contract { Id = 900500, Kind = JobKind.Cargo, Title = "QA stealth", From = "Ceres", To = "Vesta", Tonnes = 10, Pay = 20000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, Key = "qa/stealth" };
                career.Active.Add(job);
                var fitted = build0.Clone();
                fitted.pick[(int)Slot.Radiators] = 1;
                career.Build = fitted;
                career.Apply(ship);
                ship.Fuel = career.TankCapacity;
                career.SinkK = 0;
                ship.LastLoudJd = jd;
                s.TestWatchNow();
            });
            for (int i = 0; go && i < 3; i++) yield return null;

            // just burned, stowed: 36 min of track, the sink warming
            if (go) go = Guard("stealth stowed", () =>
            {
                var rows = SigRows();
                string sig = SigVal(rows, "Signature"), sink = SigVal(rows, "Heat sink"), raid = SigVal(rows, "Raiders");
                string seenPill = pills ? s.TestSeenPill() : "-", darkPill = pills ? s.TestDarkPill() : "-";
                bool pillsOk = !pills || ((seenPill == "TRACKED 0:35" || seenPill == "TRACKED 0:36") && darkPill.StartsWith("DARK ", StringComparison.Ordinal));
                Check(sig.Contains("radiators stowed (0.18 MW)") && sig.Contains("3,000 km") && sink.Contains("radiators stowed") && SigTone(rows, "Heat sink") == Tone.Cold
                      && raid.StartsWith("tracking you", StringComparison.Ordinal) && SigTone(rows, "Raiders") == Tone.Bad && pillsOk,
                    $"stealth: stowed after a burn: Signature \"{sig}\", Heat sink \"{sink}\" ({SigTone(rows, "Heat sink")}), Raiders \"{raid}\" ({SigTone(rows, "Raiders")}), SEEN pill \"{seenPill}\" (TRACKED 0:35 or 0:36), DARK pill \"{darkPill}\"; {career.SinkK:0.0000} K");
            });
            if (go && pills) yield return Shot("c16-stealth-dark");

            // the drive on: the radiators are out while it is loud
            if (go) go = Guard("stealth burn start", () => ship.ThrottleCmd = 1);
            if (go) yield return Frames(0.3f);
            if (go) go = Guard("stealth loud", () =>
            {
                var rows = SigRows();
                string sig = SigVal(rows, "Signature"), sink = SigVal(rows, "Heat sink"), raid = SigVal(rows, "Raiders");
                string darkPill = pills ? s.TestDarkPill() : "-";
                Check(ship.Throttle > 0 && sig.Contains("exhaust") && sink.Contains("radiators out") && !sink.Contains("stowed") && SigTone(rows, "Heat sink") != Tone.Cold
                      && raid == "can see your burn" && darkPill == "-",
                    $"stealth: burning: Signature \"{sig}\" (exhaust), Heat sink \"{sink}\" ({SigTone(rows, "Heat sink")}; radiators out, not Cold), Raiders \"{raid}\" (can see your burn), DARK pill \"{darkPill}\" (-)");
                ship.ThrottleCmd = 0; ship.Throttle = 0;
            });
            for (int i = 0; go && i < 2; i++) yield return null;

            // 40 min on: the track is gone, the hull still stowed
            if (go) go = Guard("stealth cold", () =>
            {
                ship.LastLoudJd = jd - 40.0 / 1440;
                var rows = SigRows();
                string sig = SigVal(rows, "Signature"), raid = SigVal(rows, "Raiders");
                Check(raid == "no track: cold for over 36 min" && SigTone(rows, "Raiders") == Tone.Cold && sig.Contains("stowed"),
                    $"stealth: 40 min after the burn: Raiders \"{raid}\" ({SigTone(rows, "Raiders")}; no track: cold for over 36 min, Cold), Signature \"{sig}\" (still stowed)");

                // 3 h on: the window is shut and the radiators are out
                ship.LastLoudJd = jd - 3.0 / 24;
                rows = SigRows();
                sig = SigVal(rows, "Signature");
                string sink = SigVal(rows, "Heat sink"), darkPill = pills ? s.TestDarkPill() : "-";
                Check(sig == "cold (2 MW idle), seen at 10,000 km" && sink.Contains("radiators out") && darkPill == "-",
                    $"stealth: 3 h after the burn: Signature \"{sig}\" (cold (2 MW idle), seen at 10,000 km), Heat sink \"{sink}\" ({SigTone(rows, "Heat sink")}), DARK pill \"{darkPill}\" (-)");

                // the sink full: the radiators are out and the raider holds the track 2 h
                career.SinkK = 100;
                ship.LastLoudJd = jd - 40.0 / 1440;
                rows = SigRows();
                sig = SigVal(rows, "Signature"); sink = SigVal(rows, "Heat sink");
                raid = SigVal(rows, "Raiders");
                Check(raid.StartsWith("tracking you", StringComparison.Ordinal) && sink.StartsWith("full", StringComparison.Ordinal) && sig.Contains("2 MW idle"),
                    $"stealth: the sink full, 40 min after the burn: Raiders \"{raid}\" ({SigTone(rows, "Raiders")}; tracking you), Heat sink \"{sink}\" ({SigTone(rows, "Heat sink")}; full), Signature \"{sig}\" (2 MW idle)");
            });

            // 3b: a chase on the stowed hull, a raider 100,000 km off: the raider's clock, then the sink full and the clock gone
            if (go) go = Guard("stealth lost start", () =>
            {
                ship.ThrottleCmd = 0; ship.Throttle = 0;
                career.SinkK = 0;
                ship.LastLoudJd = double.NegativeInfinity;
                career.StartContact(job, sys, ship, jd, ship.Pos - ship.Pos.Norm * 1e5, ship.Vel);
                s.TestWatchNow();
            });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) go = Guard("stealth lost clock", () =>
            {
                var rows = SigRows();
                string track = SigVal(rows, "Raider track"), raid = SigVal(rows, "Raiders"), darkPill = pills ? s.TestDarkPill() : "-";
                bool pillOk = !pills || darkPill.StartsWith("DARK ", StringComparison.Ordinal);
                Check(track.StartsWith("it loses you in", StringComparison.Ordinal) && SigTone(rows, "Raider track") == Tone.Cold && SigAt(rows, "Raiders") < 0 && pillOk,
                    $"stealth: a chase on the dark hull: Raider track \"{track}\" ({SigTone(rows, "Raider track")}; it loses you in m:ss if you stay dark, Cold), Raiders row \"{raid}\" (absent: -), DARK pill \"{darkPill}\" (DARK h:mm); range {(career.Encounter?.Range ?? double.NaN):N0} km, sink {career.SinkK:0.0000} K");
                career.SinkK = 100;
            });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) go = Guard("stealth lost full", () =>
            {
                var rows = SigRows();
                string track = SigVal(rows, "Raider track");
                Check(track == "it has your track" && SigTone(rows, "Raider track") == Tone.Bad,
                    $"stealth: the same chase with the sink full: Raider track \"{track}\" ({SigTone(rows, "Raider track")}; it has your track, Bad); sink {career.SinkK:0.0000} K, chase {(career.Encounter != null ? "on" : "gone")} (on)");
            });

            Guard("stealth cleanup", () =>
            {
                career.DropContact();
                ship.ThrottleCmd = 0; ship.Throttle = 0;
                if (build0 != null) { career.Build = build0; career.SinkK = sink0; }
                if (job != null) career.Active.Remove(job);
                career.Apply(ship);
                var vesta = sys["Vesta"];
                D3 pV = sys.Pos(vesta, jd);
                ship.Pos = pV + pV.Norm * 1e6; ship.Vel = sys.Vel(vesta, jd);
                if (build0 != null) ship.Fuel = fuel0;
                ship.LastLoudJd = double.NegativeInfinity;
                if (GameSettings.ClassicPanel != classicWas) GameSettings.ClassicPanel = classicWas;
                s.TestWatchNow();
            });
        }
    }
}
