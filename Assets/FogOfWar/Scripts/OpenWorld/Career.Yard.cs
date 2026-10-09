using System;
using System.Collections.Generic;

namespace FogOfWar.OpenWorld
{
    // The yard (combat design, Phase 1): refit the build at any port. Each changed part costs its price less 70 % of the
    // part it replaces (a smaller part can pay) and its days in the yard; a refit of several parts sums both. A part the
    // load or the passengers aboard need can't come out; reaction mass a smaller tank can't hold is sold to the port.
    // A new hull (3e) is the same sum: its price less 70 % of the old hull's, HullRefitDays on top. Only a ship you own can
    // change hull, and only at a big yard (Port.HullSale).
    public partial class Career
    {
        public const double HullRefitDays = 7;   // [design] D10

        public bool CanRefitTo(ShipBuild to, out string why, out double cost, out double days)
        {
            cost = 0; days = 0;
            bool newHull = to.hull != Build.hull;
            if (newHull) { cost += to.Hull.Price - Build.Hull.Price * ShipBuild.SellBack; days += HullRefitDays; }
            foreach (Slot s in Enum.GetValues(typeof(Slot)))
                if (to.pick[(int)s] != Build.pick[(int)s]) { cost += to[s].Price - Build[s].Price * ShipBuild.SellBack; days += to[s].RefitDays; }
            cost = Math.Round(cost);
            why = Sandbox ? "sandbox" : Docked == null ? "dock first" : days == 0 ? "fitted"
                : newHull && !OwnsShip ? "buy out the ship first"
                : newHull && !Docked.HullSale ? "only Earth, Mars and Ceres sell hulls"
                : to.Thrust > to.Hull.MaxThrust ? "too big for the hull"
                : CargoTonnes > to.HoldTonnes ? "the load aboard needs the hold"
                : PeopleAboard > to.Seats ? "the passengers aboard need the seats"
                : cost > 0 && Credits < cost ? "not enough credits" : null;
            return why == null;
        }

        public bool RefitTo(Ship ship, ShipBuild to)
        {
            if (!CanRefitTo(to, out _, out double cost, out double days)) return false;
            var fitted = new List<string>();
            if (to.hull != Build.hull) fitted.Add(to.hull);
            foreach (Slot s in Enum.GetValues(typeof(Slot)))
                if (to.pick[(int)s] != Build.pick[(int)s]) fitted.Add(to[s].Name);
            Credits -= cost;
            Build = to.Clone();
            RefitJd = Math.Max(RefitJd, jd) + days;
            double spill = ship.Fuel - TankCapacity, refund = spill > 0 ? spill * Docked.FuelPrice : 0;
            if (spill > 0) { ship.Fuel = TankCapacity; Credits += refund; }
            Apply(ship);
            Say(Happening.Upgrade, $"Refit: {string.Join(", ", fitted)} fitted, " + (cost >= 0 ? $"−{cost:N0} cr" : $"+{-cost:N0} cr") +
                (spill > 0 ? $", {spill:N1} t of fuel sold for {refund:N0} cr" : "") + $". In the yard {RefitDays:0.#} days");
            return true;
        }

        ShipBuild With(Slot s, int i) { var b = Build.Clone(); b.pick[(int)s] = i; return b; }
        public bool CanRefit(Slot s, int i, out string why, out double cost) => CanRefitTo(With(s, i), out why, out cost, out _);
        public bool Refit(Ship ship, Slot s, int i) => RefitTo(ship, With(s, i));

        public double RefitDays => Math.Max(0, RefitJd - jd);

        // The yard's trip check: a full burn from this port to Mars (to Earth from Mars), leaving now with a full tank and
        // an empty hold, at the start mass's g (conservative: g rises as the tank empties).
        public bool YardTrip(ShipBuild b, out string to, out double seconds, out double tonnes)
        {
            to = null; seconds = tonnes = 0;
            if (Docked == null || Sys == null) return false;
            var dest = PortAt(Docked.Body == "Mars" ? "Earth orbit" : "Mars orbit");
            Body from = Sys[Docked.Body], there = Sys[dest.Body];
            if (from == null || there == null) return false;
            to = dest.Name;
            double d = Math.Max((Sys.PosAt(there, jd) - Sys.PosAt(from, jd)).Mag - Ship.Standoff(there), 0);
            double m = b.DryMass + b.TankCapacity;
            Trip(d, b.Thrust / m, 1, m, out seconds, out _, out tonnes);
            return true;
        }
    }
}
