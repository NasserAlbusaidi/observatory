using System;
using System.Collections;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // The shipyard (combat design, Phase 1): the stock build is the ship the career always had, an old save's counters
    // load as their modules, and a refit charges credits and days in the yard and survives a save.
    public partial class CareerQA
    {
        // what the biggest part in every slot costs over the stock parts' sell-back
        static double TopRefits()
        {
            double sum = 0;
            foreach (Slot s in Enum.GetValues(typeof(Slot)))
            {
                var parts = ShipBuild.Catalog[(int)s];
                double top = 0;
                foreach (var m in parts) top = Math.Max(top, m.Price);
                sum += top - parts[ShipBuild.StockPick[(int)s]].Price * ShipBuild.SellBack;
            }
            return sum;
        }

        void BuildChecks()
        {
            var b = ShipBuild.Stock();
            double g = b.Thrust / ShipBuild.RatedMass / Ship.G0;
            Check(Math.Abs(b.DryMass - 300) < 1e-9 && Math.Abs(g - 1) < 1e-9 && b.TankCapacity == 200 && b.HoldTonnes == 100 && b.Seats == 40 && b.SensorLevel == 0,
                $"yard: the stock build is today's ship: {b.DryMass} t dry, {b.Thrust:0.000} MN ({g:0.000} g at {ShipBuild.RatedMass} t), {b.TankCapacity} t tank, {b.HoldTonnes} t hold, {b.Seats} seats, sensors {b.SensorLevel}");
            double loaded = b.Thrust / (b.DryMass + b.TankCapacity + b.HoldTonnes) / Ship.G0, empty = b.Thrust / b.DryMass / Ship.G0;
            Check(Math.Abs(loaded - 0.833) < 0.001 && Math.Abs(empty - 1.667) < 0.001, $"yard: the stock drive gives {loaded:0.000} g full and loaded, {empty:0.000} g empty");
            Check(Math.Abs(b.GFull - 1) < 1e-9 && Math.Abs(b.GEmpty - 5.0 / 3) < 1e-9 && Math.Abs(b.JetPowerTW - 24.5) < 0.05 && Math.Abs(b.DvFull - Ship.Ve * Math.Log(500.0 / 300)) < 1e-6,
                $"yard: stock readouts: {b.GFull:0.00} g full, {b.GEmpty:0.00} g empty, {b.DvFull:N0} km/s on a full tank, {b.JetPowerTW:0.0} TW jet power");
            foreach (Slot s in Enum.GetValues(typeof(Slot)))
                Check(Array.TrueForAll(ShipBuild.Catalog[(int)s], m => m.Slot == s && m.Mass >= 0 && m.Price >= 0 && m.RefitDays > 0), $"yard: every {s} part is in its own slot with a mass, a price and refit days");

            // an old save, written before the build: its counters stand for the parts
            var old = JsonUtility.FromJson<CareerSave>("{\"credits\":1000,\"fuel\":250,\"drive\":2,\"tank\":1,\"sensors\":1}");
            var c = new Career { Sys = sys };
            var probe = new Ship();
            c.FromSave(old, probe);
            Check(c.Build[Slot.Drive].Name == "Torch drive" && c.TankCapacity == 300 && c.Codex.SensorLevel == 1 && c.HoldTonnes == 100 && c.Build.Seats == 40 && probe.Fuel == 250,
                $"yard: an old save (drive 2, tank 1, sensors 1) loads as {c.Build[Slot.Drive].Name}, {c.TankCapacity} t tank, sensors {c.Codex.SensorLevel}, {c.HoldTonnes} t hold, {c.Build.Seats} seats");
            var none = JsonUtility.FromJson<CareerSave>("{\"credits\":1000}");
            c.FromSave(none, probe);
            Check(string.Join(",", c.Build.pick) == string.Join(",", ShipBuild.StockPick), "yard: an old save with no upgrades loads as the stock build");
            var bad = new CareerSave { build = new ShipBuild { hull = "no such hull", pick = new[] { 9, -1, 2 } } };
            c.FromSave(bad, probe);
            Check(c.Build.Hull == ShipBuild.Hulls[0] && c.Build.pick.Length == ShipBuild.StockPick.Length && c.Build.pick[2] == 2 && c.Build.pick[0] == ShipBuild.StockPick[0],
                $"yard: a broken build in a save falls back to stock parts ({string.Join(",", c.Build.pick)})");
        }

        // Docked: refit the drive, check the price, the days and the cold drive, save and load it, then put it all back.
        IEnumerator RefitChecks()
        {
            if (career.Docked == null) { Check(false, "yard: docked for the refit checks"); yield break; }
            var was = career.Build.Clone();
            double credits0 = career.Credits, fuel0 = ship.Fuel, g0 = ship.MaxG;
            bool feat0 = career.Codex.Achieved.Contains("drive3");
            int warp0 = Get<int>("warpIdx");
            Put("warpIdx", 0); // ten frames at 1×, well inside the refit's days
            career.Credits = 500000;
            Check(!career.CanRefit(Slot.Drive, 1, out string why, out _) && why == "fitted", $"yard: the fitted part can't be bought again ({why})");
            career.CanRefit(Slot.Drive, 2, out _, out double cost);
            double dry0 = career.DryMass, jd0 = jd;
            Check(career.Refit(ship, Slot.Drive, 2), "yard: refit to the fast drive");
            var fast = ShipBuild.Catalog[(int)Slot.Drive][2];
            Check(Math.Abs(500000 - career.Credits - (fast.Price - ShipBuild.Catalog[(int)Slot.Drive][1].Price * ShipBuild.SellBack)) < 1 && Math.Abs(cost - (500000 - career.Credits)) < 1,
                $"yard: it costs its price less 70 % of the old drive: {500000 - career.Credits:N0} cr");
            Check(Math.Abs(career.DryMass - dry0 - 30) < 1e-9 && Math.Abs(career.RefitJd - jd0 - fast.RefitDays) < 0.01 && career.InYard,
                $"yard: {career.DryMass - dry0:+0;-0} t dry, in the yard {(career.RefitJd - jd0):0.#} days");
            ship.MaxG = 1; ship.ThrottleCmd = 1;
            for (int i = 0; i < 10; i++) yield return null;
            Check(ship.Throttle == 0 && ship.InYard && career.Docked != null, $"yard: in the yard the drive stays cold at full throttle (throttle {ship.Throttle}, docked {career.Docked?.Name})");
            ship.ThrottleCmd = 0;
            var save = JsonUtility.ToJson(career.ToSave(ship));
            var c = new Career { Sys = sys };
            c.FromSave(JsonUtility.FromJson<CareerSave>(save), new Ship());
            Check(string.Join(",", c.Build.pick) == string.Join(",", career.Build.pick) && Math.Abs(c.RefitJd - career.RefitJd) < 1e-9,
                $"yard: the save keeps the build ({string.Join(",", c.Build.pick)}) and the refit date");

            // a smaller tank sells the fuel it can't hold; a hold can't shrink under the load aboard
            ship.Fuel = career.TankCapacity;
            double before = career.Credits, port = career.Docked.FuelPrice;
            career.CanRefit(Slot.Tank, 0, out _, out double tankCost);
            Check(career.Refit(ship, Slot.Tank, 0) && Math.Abs(ship.Fuel - 100) < 1e-9 && Math.Abs(career.Credits - (before - tankCost + 100 * port)) < 1,
                $"yard: a 100 t tank sells the other 100 t at {port:N0} cr/t ({career.Credits - before:+#,0;-#,0} cr in all)");
            var load = new Contract { Id = 900300, Kind = JobKind.Cargo, Title = "QA crates", From = career.Docked.Name, To = career.Docked.Name, Tonnes = 80, Days = 10, Key = "qa/yard" };
            career.Active.Add(load);
            Check(!career.CanRefit(Slot.Hold, 0, out why, out _) && why == "the load aboard needs the hold", $"yard: a 50 t hold can't go in with 80 t aboard ({why})");
            career.Active.Remove(load);

            // several parts at once: the costs and the days add up; a refused refit changes nothing
            career.Build = was.Clone(); career.RefitJd = 0;
            var two = was.Clone();
            two.pick[(int)Slot.Drive] = was.pick[(int)Slot.Drive] == 2 ? 1 : 2;
            two.pick[(int)Slot.Sensors] = 1 - was.pick[(int)Slot.Sensors];
            double want = Math.Round(two[Slot.Drive].Price - was[Slot.Drive].Price * ShipBuild.SellBack + two[Slot.Sensors].Price - was[Slot.Sensors].Price * ShipBuild.SellBack);
            double wantDays = two[Slot.Drive].RefitDays + two[Slot.Sensors].RefitDays;
            career.CanRefitTo(two, out _, out double twoCost, out double twoDays);
            career.Credits = twoCost - 1;
            career.CanRefitTo(two, out why, out _, out _);
            Check(why == "not enough credits" && !career.RefitTo(ship, two) && career.Build.Same(was) && career.Credits == twoCost - 1 && career.RefitJd == 0 && !career.InYard,
                $"yard: a refit short of credits changes nothing ({why})");
            career.Credits = 500000;
            Check(career.RefitTo(ship, two) && career.Build.Same(two) && Math.Abs(twoCost - want) < 1 && Math.Abs(500000 - career.Credits - want) < 1
                  && Math.Abs(twoDays - wantDays) < 1e-9 && Math.Abs(career.RefitJd - jd - wantDays) < 0.01,
                $"yard: drive and sensors in one refit: {twoCost:N0} cr, {twoDays:0.#} days (the sums)");

            // the trip check: a bigger drive gets there sooner for more reaction mass
            var stock = ShipBuild.Stock(); var torch = ShipBuild.Stock(); torch.pick[(int)Slot.Drive] = 3;
            bool t1 = career.YardTrip(stock, out string to, out double ts, out double ms), t2 = career.YardTrip(torch, out _, out double tt, out double mt);
            Check(t1 && t2 && ts > 0 && tt < ts && mt > ms && !double.IsNaN(ms + mt),
                $"yard: trip check {career.Docked.Name} → {to}, full burn: stock {ts / 86400:0.0} days, {ms:N0} t; torch {tt / 86400:0.0} days, {mt:N0} t");

            career.Build = was; career.RefitJd = 0; career.Credits = credits0; ship.Fuel = fuel0; ship.MaxG = g0;
            if (!feat0) career.Codex.Achieved.Remove("drive3");
            Put("warpIdx", warp0);
            career.Apply(ship);
            yield return null;
            Check(!ship.InYard && Math.Abs(career.DryMass - dry0) < 1e-9, "yard: put back as it was");
        }
    }
}
