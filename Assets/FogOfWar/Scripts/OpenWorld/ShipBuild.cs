using System;

namespace FogOfWar.OpenWorld
{
    public enum Slot { Drive, Tank, Hold, Cabin, Sensors }

    // A part the yard fits in one slot. Size is the slot's own unit: MN of thrust, tonnes of tank or hold, seats, or the
    // sensor level (0 stock, 1 long range: scan range ×2, dwell ÷2).
    public class Module
    {
        public Slot Slot;
        public string Name;
        public double Size, Mass, Price, RefitDays; // t, cr, days in the yard to fit it
    }

    // The frame the modules go on: its length, its own structure mass, and the most thrust it can take.
    public class Hull
    {
        public string Name;
        public double LengthM, Structure, MaxThrust;
    }

    // The ship as the yard sees it: one hull and one module in each slot (combat design, Phase 1). Every module is mass,
    // and with a fixed thrust mass is g, so each refit trades speed for range or load. The stock build is the ship the
    // career always had: 300 t dry, 4.9 MN, a 200 t tank, a 100 t hold and stock sensors.
    [Serializable]
    public class ShipBuild
    {
        public string hull;
        public int[] pick;     // by Slot: an index into Catalog[slot]

        // A drive is named for the g it gives at RatedMass, the stock ship with a full tank and an empty hold.
        public const double RatedMass = 500;
        public static double Rated(double g) => g * Ship.G0 * RatedMass; // MN

        public static readonly Hull[] Hulls =
        {
            new Hull { Name = "FWO-052 light frigate", LengthM = 52, Structure = 228, MaxThrust = Rated(5) },
        };

        // Prices [design]: an upgrade from stock costs what it did as a counter (drive 50k and 150k more, tank 35k a step,
        // sensors 30k) plus the 70 % the stock part sells back for. Drive mass grows with thrust; a tank weighs 8 t per
        // 100 t, as the counters' did.
        public static readonly Module[][] Catalog =
        {
            new[]
            {
                new Module { Slot = Slot.Drive, Name = "Economy drive", Size = Rated(0.5), Mass = 25, Price = 10000, RefitDays = 3 },
                new Module { Slot = Slot.Drive, Name = "Standard drive", Size = Rated(1), Mass = 40, Price = 30000, RefitDays = 3 },
                new Module { Slot = Slot.Drive, Name = "Fast drive", Size = Rated(3), Mass = 70, Price = 70000, RefitDays = 3 },
                new Module { Slot = Slot.Drive, Name = "Torch drive", Size = Rated(5), Mass = 100, Price = 220000, RefitDays = 3 },
            },
            new[]
            {
                new Module { Slot = Slot.Tank, Name = "100 t tank", Size = 100, Mass = 8, Price = 10000, RefitDays = 1 },
                new Module { Slot = Slot.Tank, Name = "200 t tank", Size = 200, Mass = 16, Price = 30000, RefitDays = 1 },
                new Module { Slot = Slot.Tank, Name = "300 t tank", Size = 300, Mass = 24, Price = 55000, RefitDays = 1 },
                new Module { Slot = Slot.Tank, Name = "400 t tank", Size = 400, Mass = 32, Price = 90000, RefitDays = 1 },
            },
            new[]
            {
                new Module { Slot = Slot.Hold, Name = "50 t hold", Size = 50, Mass = 5, Price = 10000, RefitDays = 1 },
                new Module { Slot = Slot.Hold, Name = "100 t hold", Size = 100, Mass = 10, Price = 20000, RefitDays = 1 },
                new Module { Slot = Slot.Hold, Name = "200 t hold", Size = 200, Mass = 20, Price = 60000, RefitDays = 1 },
            },
            new[]
            {
                new Module { Slot = Slot.Cabin, Name = "No cabin", Size = 0, Mass = 0, Price = 0, RefitDays = 0.5 },
                new Module { Slot = Slot.Cabin, Name = "12-seat cabin", Size = 12, Mass = 1.5, Price = 6000, RefitDays = 0.5 },
                new Module { Slot = Slot.Cabin, Name = "40-seat cabin", Size = 40, Mass = 4, Price = 15000, RefitDays = 0.5 },
            },
            new[]
            {
                new Module { Slot = Slot.Sensors, Name = "Stock sensors", Size = 0, Mass = 2, Price = 10000, RefitDays = 0.5 },
                new Module { Slot = Slot.Sensors, Name = "Long-range sensors", Size = 1, Mass = 4, Price = 37000, RefitDays = 0.5 },
            },
        };
        public static readonly int[] StockPick = { 1, 1, 1, 2, 0 };
        public const double SellBack = 0.7;

        public static ShipBuild Stock() => new ShipBuild { hull = Hulls[0].Name, pick = (int[])StockPick.Clone() };

        // The build an old save's counters stand for: drive level 0–2 is the standard, fast or torch drive; tank level
        // 0–2 the 200, 300 or 400 t tank; sensor level 0 or 1 the stock or long-range sensors.
        public static ShipBuild FromCounters(int drive, int tank, int sensors)
        {
            var b = Stock();
            b.pick[(int)Slot.Drive] = 1 + Math.Max(0, Math.Min(drive, 2));
            b.pick[(int)Slot.Tank] = 1 + Math.Max(0, Math.Min(tank, 2));
            b.pick[(int)Slot.Sensors] = sensors > 0 ? 1 : 0;
            return b;
        }

        // A save's build, checked: an unknown hull or a pick out of range falls back to the stock part.
        public ShipBuild Checked()
        {
            var b = Stock();
            if (Array.Exists(Hulls, h => h.Name == hull)) b.hull = hull;
            for (int s = 0; pick != null && s < Math.Min(pick.Length, b.pick.Length); s++)
                if (pick[s] >= 0 && pick[s] < Catalog[s].Length) b.pick[s] = pick[s];
            return b;
        }

        public ShipBuild Clone() => new ShipBuild { hull = hull, pick = (int[])pick.Clone() };
        public Hull Hull => Array.Find(Hulls, h => h.Name == hull) ?? Hulls[0];
        public Module this[Slot s] => Catalog[(int)s][pick[(int)s]];

        public double DryMass { get { double m = Hull.Structure; foreach (Slot s in Enum.GetValues(typeof(Slot))) m += this[s].Mass; return m; } }
        public double Thrust => this[Slot.Drive].Size;
        public double TankCapacity => this[Slot.Tank].Size;
        public double HoldTonnes => this[Slot.Hold].Size;
        public int Seats => (int)this[Slot.Cabin].Size;
        public int SensorLevel => (int)this[Slot.Sensors].Size;

        // the yard's readouts: full burn with an empty hold
        public double GFull => Thrust / (DryMass + TankCapacity) / Ship.G0;
        public double GEmpty => Thrust / DryMass / Ship.G0;
        public double DvFull => Ship.Ve * Math.Log((DryMass + TankCapacity) / DryMass);
        public double JetPowerTW => 0.5 * Thrust * Ship.Ve / 1000; // ½·F·vₑ; MN × km/s is GW
        public bool Same(ShipBuild o)
        {
            if (hull != o.hull || pick.Length != o.pick.Length) return false;
            for (int s = 0; s < pick.Length; s++) if (pick[s] != o.pick[s]) return false;
            return true;
        }
    }
}
