using System;
using System.Collections.Generic;
using System.Reflection;
using FogOfWar.OpenWorld;
using UnityEngine;
using UIE = UnityEngine.UIElements;

namespace FogOfWar
{
    // 16n: the second hull (docs/design-combat.md Phase 3e, Career.Yard.cs, ShipBuild.cs). The hull table and the corvette's
    // numbers, the facet in the career's readers (hold, cold power, seen time, the raider that loses a dark hull), the saves,
    // the swap rules at the yard, then in the scene the hull's length and the free camera's floor, the hull row on the Jobs
    // page and the Jobs page's redraw signature (the page alone under -fowlegacyui is not drawn, so the shell part is skipped).
    // Every [calc] number was worked by hand and never run: every check logs what it measured. Headless parts run on benches
    // of their own; nothing here changes the scene's career except the shell part, which puts all of it back.
    public partial class CareerQA
    {
        const string FrigateName = "FWO-052 light frigate", CorvetteName = "FWO-060 stealth corvette";

        static Hull HullNamed(string name) => Array.Find(ShipBuild.Hulls, h => h.Name == name);
        static string Picks(ShipBuild b) => b.pick == null ? "-" : string.Join(",", b.pick);

        void StealthHullChecks(SolarSystem qs)
        {
            Guard("stealth hull table", StealthHullTableChecks);
            Guard("stealth hull numbers", StealthHullNumberChecks);
            Guard("stealth hull readers", () => StealthHullReaderChecks(qs));
            Guard("stealth hull lost", () => StealthHullLostChecks(qs));
            Guard("stealth hull save", () => StealthHullSaveChecks(qs));
            Guard("stealth hull swap", () => StealthHullSwapChecks(qs));
        }

        // 16n-1: the table. Hull 0 is the save key and keeps every number it had.
        void StealthHullTableChecks()
        {
            var f = ShipBuild.Hulls[0];
            var stock = ShipBuild.Stock();
            Check(f.Name == FrigateName && f.LengthM == 52 && f.Structure == 228 && f.MaxThrust == ShipBuild.Rated(5) && f.Facet == 1 && f.Price == 0,
                $"hulls: hull 0 is \"{f.Name}\" ({FrigateName}), {f.LengthM} m (52), structure {f.Structure} t (228), max thrust {f.MaxThrust:0.000} MN ({ShipBuild.Rated(5):0.000}), facet {f.Facet:R} (1), price {f.Price:N0} cr (0)");
            const string want = "1,1,1,2,0,0,0";
            string sp = string.Join(",", ShipBuild.StockPick), hp = string.Join(",", f.StockPick), st = Picks(stock);
            Check(sp == want && hp == want && st == want && stock.hull == FrigateName && Near(stock.DryMass, 300, 1e-9) && Near(stock.GFull, 1, 1e-9),
                $"hulls: ShipBuild.StockPick {sp}, hull 0's stock pick {hp}, Stock() {st} ({want}), Stock() hull \"{stock.hull}\", dry {stock.DryMass:0.000000000} t (300 ± 1e-9), {stock.GFull:0.0000000000} g full (1 ± 1e-9)");

            var c = HullNamed(CorvetteName);
            if (c == null) { Check(false, $"hulls: no hull named \"{CorvetteName}\" in the table ({ShipBuild.Hulls.Length} hulls)"); return; }
            var cs = ShipBuild.Stock(c);
            cs.pick[0] = 3; // a copy: the hull's own stock pick must not move
            string cp = string.Join(",", c.StockPick);
            Check(c.LengthM == 60 && Near(c.Structure, 350.25, 0.005) && c.MaxThrust == ShipBuild.Rated(3) && Near(c.Facet, 1.0 / 3, 1e-15) && c.Price == 400000 && cp == "1,2,0,0,0,1,0" && cs.hull == CorvetteName && !c.Name.Contains("oud"),
                $"hulls: \"{c.Name}\": {c.LengthM} m (60), structure {c.Structure:0.0000} t (350.25 ± 0.005), max thrust {c.MaxThrust:0.000} MN ({ShipBuild.Rated(3):0.000}, no Torch drive), facet {c.Facet:R} (1/3), price {c.Price:N0} cr (400,000), stock pick {cp} (1,2,0,0,0,1,0) after a copy of it was edited; no \"oud\" in the name");
        }

        // 16n-2: the corvette's numbers, the stock pick of its hull
        void StealthHullNumberChecks()
        {
            var h = HullNamed(CorvetteName);
            if (h == null) { Check(false, $"hulls: no hull named \"{CorvetteName}\""); return; }
            var b = ShipBuild.Stock(h);
            double sinkH = b.TankCapacity * Career.SinkSecondsPerT / 3600;
            Check(Near(b.DryMass, 433.25, 0.005) && Near(b.GFull, 0.682, 0.0005) && Near(b.GEmpty, 1.154, 0.0005) && Near(b.DvFull, 5262, 1) && Near(sinkH, 17.44, 0.01),
                $"hulls: the stock corvette is {b.DryMass:0.0000} t dry (433.25 ± 0.005), {b.GFull:0.00000} g full (0.682 ± 0.0005), {b.GEmpty:0.00000} g empty (1.154 ± 0.0005), {b.DvFull:0.000} km/s (5,262 ± 1); a full {b.TankCapacity:0} t tank sinks heat for {sinkH:0.0000} h (17.44 ± 0.01)");

            double stowedKm = Signature.RangeKm(0, Signature.StockKm, Signature.ColdW(b.DarkRho)), outKm = Signature.RangeKm(0, Signature.StockKm, Signature.ColdW(b.OutRho));
            double stowedHold = Signature.TrackHoldS * b.DarkRho, outHold = Signature.TrackHoldS * b.OutRho;
            Check(Near(stowedKm, 1000, 0.5) && Near(stowedHold, 720, 1e-6) && Near(outKm, 3333.3, 0.5) && Near(outHold, 2400, 1e-6),
                $"hulls: the corvette stowed is cold at {stowedKm:0.0000} km (1,000 ± 0.5, {Signature.ColdW(b.DarkRho):N3} W) and tracked {stowedHold:0.000000} s (720 ± 1e-6); with the radiators out {outKm:0.0000} km (3,333.3 ± 0.5, {Signature.ColdW(b.OutRho):N3} W) and {outHold:0.000000} s (2,400 ± 1e-6); rho out {b.OutRho:R}, stowed {b.DarkRho:R}");

            // the drive alone sets the jet power: a corvette with the Fast drive is seen at a full burn as far as a frigate with it
            var cf = ShipBuild.Stock(h); cf.pick[(int)Slot.Drive] = 2;
            var ff = ShipBuild.Stock(); ff.pick[(int)Slot.Drive] = 2;
            double rc = Signature.RangeKm(cf.JetPowerTW * 1e12), rf = Signature.RangeKm(ff.JetPowerTW * 1e12);
            double rel = Math.Abs(rc - rf) / rf;
            Check(rel <= 1e-9 && Near(rc / Frame.AU, 0.4054, 0.001),
                $"hulls: the Fast drive's full burn is seen at {rc / Frame.AU:0.000000} AU on the corvette and {rf / Frame.AU:0.000000} AU on the frigate ({rel:E2} apart, within 1e-9; the pinned 0.4054 ± 0.001)");
        }

        // a never-burned bench (15) with the corvette fitted, as Dark() fits the stowable radiators: dark-ready, 200 t of fuel
        static Bench CorvetteBench(SolarSystem qs, D3 at, D3 vel)
        {
            var b = SigBench(qs, at, vel);
            b.C.Build = ShipBuild.Stock(HullNamed(CorvetteName));
            b.C.Apply(b.Ship);
            return b;
        }

        // 16n-3: the facet in the career's readers
        void StealthHullReaderChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            if (HullNamed(CorvetteName) == null) { Check(false, $"hulls: no hull named \"{CorvetteName}\""); return; }
            const double minute = 60.0 / 86400;

            var dark = CorvetteBench(qs, at, vel);
            var full = CorvetteBench(qs, at, vel);
            full.C.SinkK = Career.SinkMaxK;
            double hDark = dark.C.HoldS(dark.Ship), hFull = full.C.HoldS(full.Ship), outHold = dark.C.OutHoldS;
            Check(dark.C.DarkReady(dark.Ship) && Near(hDark, 720, 1e-6) && !full.C.DarkReady(full.Ship) && Near(hFull, 2400, 1e-6) && Near(outHold, 2400, 1e-6),
                $"hulls: the corvette's hold is {hDark:0.000000} s dark-ready (720 ± 1e-6), {hFull:0.000000} s with the sink full (2,400 ± 1e-6); OutHoldS {outHold:0.000000} s (2,400 ± 1e-6)");

            var fd = Dark(qs, at, vel);
            var ffull = Dark(qs, at, vel);
            ffull.C.SinkK = Career.SinkMaxK;
            double fdHold = fd.C.HoldS(fd.Ship), ffHold = ffull.C.HoldS(ffull.Ship), fOut = fd.C.OutHoldS;
            Check(Near(fdHold, 2160, 1e-9) && Near(ffHold, 7200, 1e-9) && Near(fOut, 7200, 1e-9),
                $"hulls: the frigate on the same bench holds {fdHold:0.000000000} s dark-ready (2,160 ± 1e-9) and {ffHold:0.000000000} s with the sink full (7,200 ± 1e-9); OutHoldS {fOut:0.000000000} s (7,200 ± 1e-9)");

            // cold power: stowed inside the OutHoldS window, out after it, out with the sink full
            var hot = CorvetteBench(qs, at, vel);
            hot.Ship.LastLoudJd = hot.Jd - 10 * minute;
            bool stowed = hot.C.Stowed(hot.Ship, hot.Jd);
            double wStowed = hot.C.ColdW(hot.Ship, hot.Jd);
            var edge = CorvetteBench(qs, at, vel);
            edge.Ship.LastLoudJd = edge.Jd - 2300.0 / 86400;
            bool edgeIn = edge.C.Stowed(edge.Ship, edge.Jd);
            double wEdge = edge.C.ColdW(edge.Ship, edge.Jd);
            var late = CorvetteBench(qs, at, vel);
            late.Ship.LastLoudJd = late.Jd - 2500.0 / 86400;
            bool lateStowed = late.C.Stowed(late.Ship, late.Jd);
            double wLate = late.C.ColdW(late.Ship, late.Jd);
            var fullHot = CorvetteBench(qs, at, vel);
            fullHot.C.SinkK = Career.SinkMaxK;
            fullHot.Ship.LastLoudJd = fullHot.Jd - 10 * minute;
            bool fullStowed = fullHot.C.Stowed(fullHot.Ship, fullHot.Jd);
            double wFull = fullHot.C.ColdW(fullHot.Ship, fullHot.Jd);
            double wOut = 2e6 / 9; // rho 1/3 radiating: (1/3)^2 of 2 MW; the brief's 2.222e5 is this rounded to four digits
            Check(stowed && Near(wStowed, 2e4, 1) && edgeIn && Near(wEdge, 2e4, 1) && !lateStowed && Near(wLate, wOut, 1) && !fullStowed && Near(wFull, wOut, 1),
                $"hulls: the corvette's cold power is {wStowed:N3} W stowed 10 min after a burn (stowed {stowed}; 20,000 ± 1), {wEdge:N3} W at 2,300 s (stowed {edgeIn}: the window is OutHoldS, 2,400 s; 20,000 ± 1), {wLate:N3} W at 2,500 s (stowed {lateStowed}, out: {wOut:N3} ± 1), {wFull:N3} W with the sink full (stowed {fullStowed}, out: {wOut:N3} ± 1)");

            var fHot = Dark(qs, at, vel);
            fHot.Ship.LastLoudJd = fHot.Jd - 10 * minute;
            var fLate = Dark(qs, at, vel);
            fLate.Ship.LastLoudJd = fLate.Jd - 7500.0 / 86400;
            var fOutB = SigBench(qs, at, vel);
            double fwStowed = fHot.C.ColdW(fHot.Ship, fHot.Jd), fwLate = fLate.C.ColdW(fLate.Ship, fLate.Jd), fwFixed = fOutB.C.ColdW(fOutB.Ship, fOutB.Jd);
            Check(Near(fwStowed, 180000, 1e-6) && Near(fwLate, Signature.IdleW, 1e-6) && Near(fwFixed, Signature.IdleW, 1e-6),
                $"hulls: the frigate's cold power is {fwStowed:N3} W stowed (180,000 ± 1e-6), {fwLate:N3} W 7,500 s after a burn (2,000,000 ± 1e-6), {fwFixed:N3} W with fixed radiators (2,000,000 ± 1e-6)");

            // what a raider sees of an update: the last loud moment 30 min before it, then 6 min before it
            var c30 = CorvetteBench(qs, at, vel);
            SigUpdate(c30, 0, -0.5);
            double seen30 = c30.C.SeenSeconds(c30.Ship, c30.Jd, 3600);
            var f30 = SigBench(qs, at, vel);
            SigUpdate(f30, 0, -0.5);
            double fSeen30 = f30.C.SeenSeconds(f30.Ship, f30.Jd, 3600);
            var c6 = CorvetteBench(qs, at, vel);
            SigUpdate(c6, 0, -0.1);
            double seen6 = c6.C.SeenSeconds(c6.Ship, c6.Jd, 3600);
            Check(Near(seen30, 0, 1e-6) && Near(fSeen30, 3600, 0.01) && Near(seen6, 360, 1e-3),
                $"hulls: the last loud moment 30 min before the update is seen {seen30:0.000000} s of 3,600 on the corvette (0: its 720 s hold was over) and {fSeen30:0.000} s on a stock frigate bench (3,600 ± 0.01); 6 min before it, {seen6:0.000000} s on the corvette (360 ± 1e-3: the hold ends 360 s in); sink {c6.C.SinkK:0.0000} K");
        }

        // 16n-3: the raider that loses a dark hull (Career.Pirates.cs LoseInS) reads the corvette's own stowed reach, 1,000 km
        void StealthHullLostChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var live = new List<Bench>();
            try
            {
                double Lose(Bench b, double km, out double range)
                {
                    live.Add(b);
                    var e = b.C.StartContact(b.Job, qs, b.Ship, b.Jd, at - at.Norm * km, vel);
                    range = e.Range;
                    return b.C.LoseInS(b.Ship, b.Jd);
                }
                string Show(double v) => double.IsNaN(v) ? "NaN" : $"{v:0.000000} s";
                double lOut = Lose(CorvetteBench(qs, at, vel), 2000, out double rOut);
                double lIn = Lose(CorvetteBench(qs, at, vel), 500, out double rIn);
                double fOut = Lose(Dark(qs, at, vel), 2000, out double frOut);
                Check(Near(lOut, 720, 1e-3) && double.IsNaN(lIn) && double.IsNaN(fOut),
                    $"hulls: a raider {rOut:N0} km off a dark corvette loses it in {Show(lOut)} (720 ± 1e-3: outside the 1,000 km stowed reach); {rIn:N0} km off, {Show(lIn)} (NaN: inside it); {frOut:N0} km off a dark frigate, {Show(fOut)} (NaN: inside its 3,000 km)");
            }
            finally { foreach (var b in live) b.C.DropContact(); }
        }

        // 16n-4: the saves. A build is its hull name and its picks; a name that is no hull is hull 0.
        void StealthHullSaveChecks(SolarSystem qs)
        {
            var corv = HullNamed(CorvetteName);
            if (corv == null) { Check(false, $"hulls: no hull named \"{CorvetteName}\""); return; }
            var old = JsonUtility.FromJson<ShipBuild>("{\"hull\":\"" + FrigateName + "\",\"pick\":[1,1,1,2,0]}").Checked();
            Check(old.Hull == ShipBuild.Hulls[0] && old.pick.Length == 7 && old.pick[5] == 0 && old.pick[6] == 0 && !old.Stowable,
                $"hulls: a 5-pick frigate build checks to {old.hull} with picks {Picks(old)} (1,1,1,2,0,0,0), stowable {old.Stowable} (False)");

            string cj = JsonUtility.ToJson(ShipBuild.Stock(corv));
            var back = JsonUtility.FromJson<ShipBuild>(cj).Checked();
            Check(back.hull == CorvetteName && Picks(back) == "1,2,0,0,0,1,0" && back.Hull == corv && !cj.Contains("oud"),
                $"hulls: a corvette build goes through JsonUtility ({cj.Length} chars: {cj}) and checks to {back.hull} with picks {Picks(back)} (1,2,0,0,0,1,0); no \"oud\" in it");

            var nameless = JsonUtility.FromJson<ShipBuild>("{\"hull\":\"no such hull\"}").Checked();
            var strange = JsonUtility.FromJson<ShipBuild>("{\"hull\":\"no such hull\",\"pick\":[9,-1]}").Checked();
            string want = string.Join(",", ShipBuild.Hulls[0].StockPick);
            Check(nameless.Hull == ShipBuild.Hulls[0] && Picks(nameless) == want && strange.Hull == ShipBuild.Hulls[0] && Picks(strange) == want,
                $"hulls: an unknown hull name checks to {nameless.hull} with picks {Picks(nameless)} ({want}); with picks out of range, {strange.hull} with {Picks(strange)} (the same)");

            var (_, at, vel) = Belt(qs);
            var b = CorvetteBench(qs, at, vel);
            string json = JsonUtility.ToJson(b.C.ToSave(b.Ship));
            var c = new Career { Sys = qs };
            c.FromSave(JsonUtility.FromJson<CareerSave>(json), new Ship());
            Check(c.Build.hull == CorvetteName && Picks(c.Build) == "1,2,0,0,0,1,0" && Near(c.DryMass, 433.25, 0.005) && !json.Contains("oud"),
                $"hulls: a career save with the corvette fitted ({json.Length} chars) loads as {c.Build.hull} (corvette) with picks {Picks(c.Build)} (1,2,0,0,0,1,0), {c.DryMass:0.0000} t dry (433.25 ± 0.005); the save holds no \"oud\" ({!json.Contains("oud")})");
        }

        // a career docked at a port by the real path, with no job aboard, credits in hand and the ship owned or not
        static Bench DockedAt(SolarSystem qs, D3 at, D3 vel, string port, List<Bench> live, bool owned)
        {
            var b = BoardBench(qs, at, vel, port, live);
            b.C.Credits = 5e6;
            b.C.ShipPaid = owned ? Career.ShipPrice : 0;
            b.C.Tick(qs, b.Ship, b.Jd, 0); // FindDock docks a landed ship
            return b;
        }

        // the swap's price and days as the catalog gives them: the hull's price less 70 % of the old one's, 7 days, then each changed part
        static double HullSwapWant(ShipBuild from, ShipBuild to, out double days)
        {
            double cost = 0;
            days = 0;
            if (to.hull != from.hull) { cost += to.Hull.Price - from.Hull.Price * ShipBuild.SellBack; days += Career.HullRefitDays; }
            for (int s = 0; s < to.pick.Length; s++)
            {
                if (to.pick[s] == from.pick[s]) continue;
                var n = ShipBuild.Catalog[s][to.pick[s]];
                var o = ShipBuild.Catalog[s][from.pick[s]];
                cost += n.Price - o.Price * ShipBuild.SellBack;
                days += n.RefitDays;
            }
            return Math.Round(cost);
        }

        // 16n-5: the swap rules
        void StealthHullSwapChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var corv = HullNamed(CorvetteName);
            if (corv == null) { Check(false, $"hulls: no hull named \"{CorvetteName}\""); return; }
            var live = new List<Bench>();
            try
            {
                ShipBuild Vette() => ShipBuild.Stock(corv);

                // not owned: the yard keeps the ship
                var rented = DockedAt(qs, at, vel, "Earth orbit", live, false);
                bool okR = rented.C.CanRefitTo(Vette(), out string whyR, out double costR, out double daysR);
                double credR = rented.C.Credits;
                bool didR = rented.C.RefitTo(rented.Ship, Vette());
                Check(!okR && whyR == "buy out the ship first" && rented.C.Docked?.Name == "Earth orbit" && !didR && rented.C.Build.hull == FrigateName && rented.C.Credits == credR,
                    $"hulls: at {rented.C.Docked?.Name ?? "-"} with {rented.C.ShipPaid:N0} of {Career.ShipPrice:N0} cr paid, the corvette is refused \"{whyR}\" (buy out the ship first; cost {costR:N0}, {daysR:0.#} days); refit done {didR} (False), hull {rented.C.Build.hull}, credits kept {rented.C.Credits == credR}");

                // owned, but a small yard
                var luna = DockedAt(qs, at, vel, "Luna", live, true);
                bool okL = luna.C.CanRefitTo(Vette(), out string whyL, out _, out _);
                bool didL = luna.C.RefitTo(luna.Ship, Vette());
                Check(!okL && whyL == "only Earth, Mars and Ceres sell hulls" && luna.C.Docked?.Name == "Luna" && !didL && luna.C.Build.hull == FrigateName,
                    $"hulls: at {luna.C.Docked?.Name ?? "-"}, ship owned ({luna.C.OwnsShip}), the corvette is refused \"{whyL}\" (only Earth, Mars and Ceres sell hulls); refit done {didL} (False), hull {luna.C.Build.hull}");

                // owned, at each big yard: stock frigate -> stock corvette
                Bench ceres = null;
                foreach (string port in new[] { "Earth orbit", "Mars orbit", "Ceres" })
                {
                    var b = DockedAt(qs, at, vel, port, live, true);
                    if (port == "Ceres") ceres = b;
                    var draft = Vette();
                    bool ok = b.C.CanRefitTo(draft, out string why, out double cost, out double days);
                    double want = HullSwapWant(b.C.Build, draft, out double wantDays);
                    double c0 = b.C.Credits, paid0 = b.C.ShipPaid;
                    bool did = ok && b.C.RefitTo(b.Ship, draft);
                    Check(ok && Near(cost, 479500, 0.5) && Near(days, 10.5, 1e-9) && Near(want, 479500, 0.5) && Near(wantDays, 10.5, 1e-9)
                          && did && b.C.Build.hull == CorvetteName && b.C.Build.Same(draft) && b.C.ShipPaid == paid0 && Near(c0 - b.C.Credits, 479500, 1e-6) && Near(b.C.RefitDays, 10.5, 1e-9)
                          && Near(b.Ship.DryMass, 433.25, 0.005) && b.Heard(Career.Happening.Upgrade, "Refit: " + CorvetteName + ", 300 t tank"),
                        $"hulls: at {b.C.Docked?.Name ?? "-"}, ship owned, stock frigate -> stock corvette: allowed {ok} (True, {why ?? "-"}), {cost:N0} cr (479,500), {days:0.###} days (10.5); the catalog's sum {want:N0} cr, {wantDays:0.###} days; refit done {did}, hull {b.C.Build.hull}, ShipPaid {b.C.ShipPaid:N0} (kept {paid0:N0}), credits fell {c0 - b.C.Credits:N0} (479,500), in the yard {b.C.RefitDays:0.000000000} days (10.5 ± 1e-9), ship dry {b.Ship.DryMass:0.0000} t (433.25 ± 0.005)");
                }

                // the corvette takes no Torch drive; with the Fast drive it is bought
                var big = DockedAt(qs, at, vel, "Earth orbit", live, true);
                var torch = Vette(); torch.pick[(int)Slot.Drive] = 3;
                bool okT = big.C.CanRefitTo(torch, out string whyT, out double costT, out _);
                var fast = Vette(); fast.pick[(int)Slot.Drive] = 2;
                bool okF = big.C.CanRefitTo(fast, out string whyF, out double costF, out double daysF);
                double wantF = HullSwapWant(big.C.Build, fast, out double wantDaysF);
                Check(!okT && whyT == "too big for the hull" && okF && Near(costF, wantF, 0.5) && Near(daysF, wantDaysF, 1e-9) && Near(costF, 528500, 0.5) && Near(daysF, 13.5, 1e-9),
                    $"hulls: a corvette with the Torch drive is refused \"{whyT}\" (too big for the hull; cost {costT:N0}); with the Fast drive allowed {okF} (True, {whyF ?? "-"}): {costF:N0} cr (the catalog's {wantF:N0}; 479,500 + 70,000 - 21,000 = 528,500), {daysF:0.###} days (13.5)");

                // and back to the frigate at Ceres: the corvette's price comes back at 70 %
                if (ceres == null) { Check(false, "hulls: no Ceres bench"); return; }
                var home = ShipBuild.Stock();
                bool okB = ceres.C.CanRefitTo(home, out string whyB, out double costB, out double daysB);
                double wantB = HullSwapWant(ceres.C.Build, home, out double wantDaysB);
                double cB0 = ceres.C.Credits, paidB0 = ceres.C.ShipPaid;
                bool didB = okB && ceres.C.RefitTo(ceres.Ship, home);
                Check(okB && Near(costB, wantB, 0.5) && Near(daysB, wantDaysB, 1e-9) && Near(costB, -302500, 0.5) && Near(daysB, 10.5, 1e-9)
                      && didB && ceres.C.Build.Same(home) && ceres.C.ShipPaid == paidB0 && Near(ceres.C.Credits - cB0, -costB, 1e-6) && Near(ceres.C.RefitDays, 21, 1e-9),
                    $"hulls: at {ceres.C.Docked?.Name ?? "-"}, back to the stock frigate: allowed {okB} (True, {whyB ?? "-"}), {costB:N0} cr (the catalog's {wantB:N0}; -400,000 x 0.7 and the parts, -302,500), {daysB:0.###} days (10.5); refit done {didB}, hull {ceres.C.Build.hull}, credits changed {ceres.C.Credits - cB0:N0}, ShipPaid kept {ceres.C.ShipPaid == paidB0}, in the yard {ceres.C.RefitDays:0.000000000} days (21 ± 1e-9: the two refits)");

                // a slot-only refit on the corvette is no hull change: any port, owned or not
                var small = DockedAt(qs, at, vel, "Luna", live, false);
                small.C.Build = Vette();
                small.C.Apply(small.Ship);
                var longRange = Vette(); longRange.pick[(int)Slot.Sensors] = 1;
                bool okS = small.C.CanRefitTo(longRange, out string whyS, out double costS, out double daysS);
                bool didS = okS && small.C.RefitTo(small.Ship, longRange);
                Check(okS && Near(costS, 30000, 0.5) && Near(daysS, 0.5, 1e-9) && didS && small.C.Build.hull == CorvetteName && small.C.Build.SensorLevel == 1,
                    $"hulls: on the corvette at {small.C.Docked?.Name ?? "-"}, ship not owned ({small.C.OwnsShip}), long-range sensors are allowed {okS} (True, {whyS ?? "-"}): {costS:N0} cr (37,000 - 7,000 = 30,000), {daysS:0.###} days (0.5); refit done {didS}, hull {small.C.Build.hull}, sensors {small.C.Build.SensorLevel} (1)");
            }
            finally { foreach (var b in live) b.C.DropContact(); }
        }

        static int CountContains(UIE.VisualElement e, string part)
        {
            int n = e is UIE.TextElement t && t.text != null && t.text.Contains(part) ? 1 : 0;
            foreach (var c in e.Children()) n += CountContains(c, part);
            return n;
        }

        // 16n, the scene. The hull's length and the free camera's floor (both shells); the hull row on the Jobs page and the Jobs
        // page's signature (UI Toolkit; the classic panel's row is GUILayout and no QA reads it). The scene's career is changed
        // by hand (build, dock) and put back, with the ship's thrust limits, the yard's draft and the page's signature.
        void StealthHullShellChecks()
        {
            var corv = HullNamed(CorvetteName);
            if (corv == null) { Check(false, $"hulls scene: no hull named \"{CorvetteName}\""); return; }
            var hullKm = s.GetType().GetMethod("HullKm", F);
            if (hullKm == null) { Check(false, "hulls scene: the scene has no HullKm"); return; }
            float Km() => (float)hullKm.Invoke(s, null);
            var setDocked = typeof(Career).GetProperty("Docked").GetSetMethod(true);
            var build0 = career.Build;
            double fuel0 = ship.Fuel, maxG0 = ship.MaxG;
            var dock0 = career.Docked;
            var draft0 = Get<ShipBuild>("yardDraft");
            var yardPort0 = Get<Port>("yardPort");
            var head0 = Get<UIE.Label>("yardHead");
            void Restore()
            {
                career.Build = build0;
                career.Apply(ship);
                ship.Fuel = fuel0; ship.MaxG = maxG0;
                setDocked.Invoke(career, new object[] { dock0 });
                Put("yardDraft", draft0); Put("yardPort", yardPort0); Put("yardHead", head0);
                Put("jobsSig", null); // the page rebuilds from the real jobs
            }
            try
            {
                bool onShip = Convert.ToInt32(Get<object>("pivot")) == 0;
                career.Build = ShipBuild.Stock(); career.Apply(ship);
                float kmF = Km(), floorF = s.RigMinDistance;
                career.Build = ShipBuild.Stock(corv); career.Apply(ship);
                float kmC = Km(), floorC = s.RigMinDistance;
                Restore();
                Check(Near(kmF, 0.052, 1e-6) && Near(kmC, 0.060, 1e-6),
                    $"hulls scene: the fitted hull's length is {kmF:0.000000} km on the frigate (0.052 ± 1e-6) and {kmC:0.000000} km on the corvette (0.060 ± 1e-6)");
                if (onShip)
                    Check(Near(floorF, 0.045, 1e-6) && Near(floorC, 0.045 * 60 / 52, 1e-6),
                        $"hulls scene: the free camera's floor around the ship is {floorF:0.000000} km on the frigate (0.045 ± 1e-6) and {floorC:0.000000} km on the corvette ({0.045 * 60 / 52:0.000000} ± 1e-6)");
                else
                    log.WriteLine($"  SKIP hulls scene: the camera's pivot is not the ship, so the free camera's floor is not the hull's (frigate {floorF:0.000000} km, corvette {floorC:0.000000} km)");

                Guard("stealth hull yard pick", () => StealthHullPickChecks(corv)); // a throw here must not skip the row checks below

                if (UI.UiShell.Disabled)
                {
                    log.WriteLine("  SKIP hulls scene: no UI Toolkit shell (-fowlegacyui): the hull row and the Jobs page's signature; the classic panel draws the row with GUILayout and no QA reads IMGUI text");
                    return;
                }
                if (!app.ShellActive) { Check(false, "hulls scene: no UI Toolkit shell, and -fowlegacyui is not on"); return; }
                log.WriteLine("  SKIP hulls scene: the classic panel's hull row is a GUILayout button row that no QA reads; the UI Toolkit row is checked");

                var labelM = typeof(OpenSystemScene).GetMethod("HullLabel", BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var sigM = s.GetType().GetMethod("JobsSignature", F);
                if (labelM == null || sigM == null) { Check(false, $"hulls scene: the scene has no HullLabel ({labelM != null}) or JobsSignature ({sigM != null})"); return; }
                int fi = 0, ci = Array.IndexOf(ShipBuild.Hulls, corv);
                string HullText(int i) => (string)labelM.Invoke(s, new object[] { ShipBuild.Hulls[i], career.Build });
                // the page docked at a port: the exact labels of both hulls and any text holding either name
                (int f, int c, int fAny, int cAny, string fl, string cl) Page(string port)
                {
                    setDocked.Invoke(career, new object[] { Array.Find(Career.Ports, p => p.Name == port) });
                    var box = new UIE.VisualElement();
                    Call("BuildJobs", box);
                    string a = HullText(fi), b = HullText(ci);
                    return (CountText(box, a), CountText(box, b), CountContains(box, ShipBuild.Hulls[fi].Name), CountContains(box, corv.Name), a, b);
                }
                var luna = Page("Luna");
                var earth = Page("Earth orbit");
                var mars = Page("Mars orbit");
                var ceres = Page("Ceres");
                Check(luna.f == 0 && luna.c == 0
                      && earth.f == 1 && earth.c == 1 && mars.f == 1 && mars.c == 1 && ceres.f == 1 && ceres.c == 1,
                    $"hulls scene: the Jobs page's hull row: docked at Luna the hull labels are drawn {luna.f}/{luna.c} times and any text with a hull name {luna.fAny}/{luna.cAny} (logged, no page text should hold one); at Earth orbit {earth.f}/{earth.c} (1/1, any {earth.fAny}/{earth.cAny}), Mars orbit {mars.f}/{mars.c} (1/1), Ceres {ceres.f}/{ceres.c} (1/1); the labels read \"{earth.fl}\" and \"{earth.cl}\"");

                // the signature must see a hull change alone: the picks of the draft, and of the fitted build, stay equal
                var draft = Get<ShipBuild>("yardDraft");
                if (draft == null) { Check(false, "hulls scene: no yard draft after the page was built docked at Ceres"); return; }
                string sig0 = (string)sigM.Invoke(s, null);
                string hull0 = draft.hull;
                draft.hull = corv.Name;
                string sigDraft = (string)sigM.Invoke(s, null);
                draft.hull = hull0;
                string sigBack = (string)sigM.Invoke(s, null);
                var alt = build0.Clone();
                alt.hull = corv.Name;
                career.Build = alt;
                string sigBuild = (string)sigM.Invoke(s, null);
                career.Build = build0;
                Check(sigDraft != sig0 && sigBuild != sig0 && sigBack == sig0 && Picks(draft) == Picks(build0),
                    $"hulls scene: the Jobs page's signature ({sig0.Length} chars) changes when only the draft's hull does ({sigDraft != sig0}, True) or only the fitted build's ({sigBuild != sig0}, True), picks equal ({Picks(draft)} against {Picks(build0)}); put back it reads as before ({sigBack == sig0}, True)");
            }
            finally { Restore(); }
        }

        // 16n, the yard's hull pick: both shells call OpenSystemScene.PickHull(draft, hull, fitted build) from their hull row.
        // This checks that helper alone, not either shell's wiring to it (the GUILayout row, the UI Toolkit Segmented).
        // The method is static, so it runs on builds made here and reaches no scene state; nothing is changed, nothing to put back.
        // The picks are the slot order of Slot: drive, tank, hold, cabin, sensors, radiators, armor. [calc] by hand from the table:
        // frigate stock 1,1,1,2,0,0,0; corvette stock 1,2,0,0,0,1,0; the fitted frigate has the 400 t tank (tank 3): 1,3,1,2,0,0,0.
        void StealthHullPickChecks(Hull corv)
        {
            var frig = ShipBuild.Hulls[0];
            var pickM = typeof(OpenSystemScene).GetMethod("PickHull", BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (pickM == null) { Check(false, "hulls yard pick: the scene has no PickHull"); return; }
            void Pick(ShipBuild d, Hull h, ShipBuild fitted) => pickM.Invoke(null, new object[] { d, h, fitted });
            ShipBuild Fitted(Hull h, int slot, int part) { var b = ShipBuild.Stock(h); b.pick[slot] = part; return b; }

            // 1: the other hull gets its own stock pick, and the draft's picks are a copy of the table's
            var fitted = Fitted(frig, (int)Slot.Tank, 3);
            var d = fitted.Clone();
            Pick(d, corv, fitted);
            string toCorv = $"{d.hull} {Picks(d)}";
            d.pick[(int)Slot.Drive] = 0;
            string tableAfter = string.Join(",", corv.StockPick), fittedAfter = Picks(fitted);
            Check(toCorv == CorvetteName + " 1,2,0,0,0,1,0" && tableAfter == "1,2,0,0,0,1,0" && fittedAfter == "1,3,1,2,0,0,0",
                $"hulls yard pick: a draft of the fitted frigate (picks {Picks(fitted)}) that picks the corvette reads \"{toCorv}\" (\"{CorvetteName} 1,2,0,0,0,1,0\", its stock pick, not the frigate's 1,3,1,2,0,0,0); editing the draft's drive left the corvette's table at {tableAfter} (1,2,0,0,0,1,0) and the fitted build at {fittedAfter} (1,3,1,2,0,0,0)");

            // 2: the fitted hull again gives back the fitted picks, not its stock pick; a pick made on the other hull is gone
            d = fitted.Clone();
            Pick(d, corv, fitted);
            d.pick[(int)Slot.Sensors] = 1;
            string onCorv = Picks(d);
            Pick(d, frig, fitted);
            string back = $"{d.hull} {Picks(d)}";
            d.pick[(int)Slot.Hold] = 2;
            string fittedKept = Picks(fitted);
            Check(onCorv == "1,2,0,0,1,1,0" && back == FrigateName + " 1,3,1,2,0,0,0" && fittedKept == "1,3,1,2,0,0,0",
                $"hulls yard pick: on the corvette with long-range sensors the picks read {onCorv} (1,2,0,0,1,1,0); the fitted frigate picked again reads \"{back}\" (\"{FrigateName} 1,3,1,2,0,0,0\": the fitted picks, not the stock 1,1,1,2,0,0,0; the sensor pick is gone); editing the hold of that draft left the fitted build at {fittedKept} (1,3,1,2,0,0,0)");

            // 3: the same hull twice changes nothing: a slot pick made in between survives, and the picks array is the same object
            d = fitted.Clone();
            d.pick[(int)Slot.Hold] = 2;
            var arr = d.pick;
            Pick(d, frig, fitted);
            string sameF = $"{d.hull} {Picks(d)}";
            bool sameFArr = ReferenceEquals(arr, d.pick);
            Pick(d, corv, fitted);
            d.pick[(int)Slot.Sensors] = 1;
            arr = d.pick;
            Pick(d, corv, fitted);
            string sameC = $"{d.hull} {Picks(d)}";
            bool sameCArr = ReferenceEquals(arr, d.pick);
            Check(sameF == FrigateName + " 1,3,2,2,0,0,0" && sameFArr && sameC == CorvetteName + " 1,2,0,0,1,1,0" && sameCArr,
                $"hulls yard pick: the frigate picked again with the hold at 200 t reads \"{sameF}\" (\"{FrigateName} 1,3,2,2,0,0,0\"), picks object kept {sameFArr} (True); the corvette picked twice with long-range sensors between reads \"{sameC}\" (\"{CorvetteName} 1,2,0,0,1,1,0\"), picks object kept {sameCArr} (True)");

            // 4: the fitted hull is the corvette: the frigate is then the stock pick, and the corvette comes back as fitted (Fast drive, drive 2)
            var fittedC = Fitted(corv, (int)Slot.Drive, 2);
            d = fittedC.Clone();
            Pick(d, frig, fittedC);
            string toFrig = $"{d.hull} {Picks(d)}";
            Pick(d, corv, fittedC);
            string backC = $"{d.hull} {Picks(d)}";
            Check(Picks(fittedC) == "2,2,0,0,0,1,0" && toFrig == FrigateName + " 1,1,1,2,0,0,0" && backC == CorvetteName + " 2,2,0,0,0,1,0",
                $"hulls yard pick: with the corvette fitted (picks {Picks(fittedC)}, 2,2,0,0,0,1,0) the frigate picked reads \"{toFrig}\" (\"{FrigateName} 1,1,1,2,0,0,0\", its stock pick) and the corvette picked again reads \"{backC}\" (\"{CorvetteName} 2,2,0,0,0,1,0\", the fitted picks); the fitted build still reads {Picks(fittedC)}");
        }
    }
}
