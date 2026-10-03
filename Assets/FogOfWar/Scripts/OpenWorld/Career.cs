using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // A port at a real body. The price of reaction mass (water) follows how easy water is to get there.
    public class Port
    {
        public string Name, Body, Note;
        public double FuelPrice; // cr per tonne
    }

    public enum JobKind { Cargo, Passengers, Survey, Rescue }

    [Serializable]
    public class Contract
    {
        public int Id;
        public JobKind Kind;
        public string Title;
        public string From, To;          // port names; for a survey, To is the body to scan
        public double Tonnes;
        public int People;
        public double Pay, Days;         // Days: time allowed, from acceptance
        public double IssuedJd, DeadlineJd;
        public double Dwell, DwellNeeded; // survey: seconds spent in scan range with the drive off
        public bool Tutorial;
        public string Key;               // board slot, so an accepted job doesn't come back the same day
        // rescue: the derelict's two-body orbit about Parent (Body element conventions, deg and km, at Epoch)
        public string Parent;
        public double A, E, I, Node, Peri, L, DL, Epoch;

        public double Load => Kind == JobKind.Cargo ? Tonnes : Kind == JobKind.Passengers ? People * Career.PassengerTonnes : 0;
    }

    [Serializable]
    public class CareerSave
    {
        public double credits, fuel, earned;
        public int drive, tank, nextId, done, failed;
        public bool sandbox;
        public List<Contract> active = new List<Contract>();
        public int sensors;
        public List<string> visited = new List<string>(), scanned = new List<string>(), achieved = new List<string>(), dockedAt = new List<string>();
    }

    // The trucker career: credits, reaction mass, ports, contracts and upgrades. Plain data and rules, no UI; the scene
    // draws it and calls Tick once a frame with the sim time that passed.
    public partial class Career
    {
        public static readonly Port[] Ports =
        {
            new Port { Name = "Earth orbit", Body = "Earth", FuelPrice = 60, Note = "Deep gravity well: water is lifted from the ground." },
            new Port { Name = "Luna", Body = "Moon", FuelPrice = 35, Note = "Water from the polar craters." },
            new Port { Name = "Mars orbit", Body = "Mars", FuelPrice = 30, Note = "Water from the ice under the surface." },
            new Port { Name = "Ceres", Body = "Ceres", FuelPrice = 8, Note = "Ice-rich. The belt's water port." },
            new Port { Name = "Vesta", Body = "Vesta", FuelPrice = 25, Note = "Dry rock: water is shipped in." },
            new Port { Name = "Ganymede", Body = "Ganymede", FuelPrice = 10, Note = "Ice shell." },
            new Port { Name = "Callisto", Body = "Callisto", FuelPrice = 10, Note = "Ice and rock, outside Jupiter's worst radiation." },
            new Port { Name = "Titan", Body = "Titan", FuelPrice = 15, Note = "Water ice under the haze." },
            new Port { Name = "Enceladus", Body = "Enceladus", FuelPrice = 9, Note = "Plume water from the south pole." },
        };

        public static readonly double[] DriveG = { 1, 3, 5 };
        public static readonly double[] DriveCost = { 0, 50000, 150000 };
        public const double TankCost = 35000;
        public const int MaxTankLevel = 2;
        public const double TankStep = 100, TankDryMass = 8; // t of tank, t of dry mass per upgrade
        public const int MaxActive = 3;
        public const double HoldTonnes = 100;
        public const double PassengerTonnes = 0.25;          // a person, life support and consumables
        public const double PassengerG = 1;                  // sustained comfort limit
        public const double DwellSeconds = 7200;
        public const double EmergencyTonnes = 30, EmergencyCost = 20000;
        const double RefG = 0.6;                             // deadlines and pay are set against this acceleration
        const double RefFuelPrice = 30;

        public double Credits = 25000, Earned;
        public int DriveLevel, TankLevel, Done, Failed;
        public bool Sandbox;
        public readonly List<Contract> Active = new List<Contract>();
        public readonly List<Contract> Board = new List<Contract>();
        public Port Docked { get; private set; }
        int nextId = 1;
        string boardPort;
        long boardDay = long.MinValue;
        readonly HashSet<string> taken = new HashSet<string>();

        public struct Note { public string Text; public bool Good; public float Time; }
        public readonly List<Note> Notes = new List<Note>();

        public double TankCapacity => 200 + TankStep * TankLevel;
        public double DryMass => 300 + TankDryMass * TankLevel;
        public double DriveRating => DriveG[DriveLevel];
        public bool CarryingPassengers => Active.Exists(c => c.Kind == JobKind.Passengers);
        public double MaxG => CarryingPassengers ? Math.Min(DriveRating, PassengerG) : DriveRating;
        public double CargoTonnes { get { double t = 0; foreach (var c in Active) t += c.Load; return t; } }

        public static Port PortAt(string name) => Array.Find(Ports, p => p.Name == name);

        // Gameplay events for sound and HUD hooks: (what happened, the notice text). Fired on the main thread from Tick or a
        // player action. Docked/Undocked/TankDry carry the port name or an empty string.
        public enum Happening { JobAccepted, JobPaid, JobFailed, Visited, Scanned, Achievement, FuelBought, EmergencyTender, Upgrade, Docked, Undocked, TankDry, RescueDone, SurveyDone }
        public event Action<Happening, string> OnEvent;

        void Say(Happening what, string text, bool good = true)
        {
            Notes.Add(new Note { Text = text, Good = good, Time = UnityEngine.Time.realtimeSinceStartup });
            OnEvent?.Invoke(what, text);
        }

        // A fresh career: docked at Earth with one easy first job to the Moon.
        public void StartNew(double jd)
        {
            Active.Add(new Contract
            {
                Id = nextId++, Kind = JobKind.Cargo, Title = "Medical supplies for Shackleton base", From = "Earth orbit", To = "Luna",
                Tonnes = 12, Pay = 6000, Days = 2, IssuedJd = jd, DeadlineJd = jd + 2, Tutorial = true, Key = "tutorial",
            });
        }

        // Ship limits that follow from the career state.
        public void Apply(Ship ship)
        {
            ship.UnlimitedFuel = Sandbox;
            if (Sandbox) { ship.Cargo = 0; return; }
            ship.DryMass = DryMass;
            ship.Cargo = CargoTonnes;
            ship.Fuel = Math.Min(ship.Fuel, TankCapacity);
            if (ship.MaxG > MaxG) ship.MaxG = MaxG;
        }

        public void Tick(SolarSystem sys, Ship ship, double jd, double simDt)
        {
            Apply(ship);
            Notes.RemoveAll(n => UnityEngine.Time.realtimeSinceStartup - n.Time > 8);
            Discover(sys, ship, jd, simDt);
            if (Sandbox) { Docked = null; return; }
            var was = Docked;
            Docked = FindDock(sys, ship, jd, was);
            if (Docked != was) OnEvent?.Invoke(Docked != null ? Happening.Docked : Happening.Undocked, (Docked ?? was).Name);
            bool dry = ship.Fuel <= 0;
            if (dry && !wasDry) OnEvent?.Invoke(Happening.TankDry, "");
            wasDry = dry;
            if (Docked != null) { Codex.DockedAt.Add(Docked.Name); Deliver(jd); RefreshBoard(sys, ship, jd); }
            Scan(sys, ship, jd, simDt);
            Rescues(sys, ship, jd, simDt);
            Expire(jd);
            foreach (var t in Codex.CheckFeats(this, ship)) Say(Happening.Achievement, "Achievement: " + t);
        }

        public readonly Codex Codex = new Codex();
        bool wasDry;

        // Visits to every body passed close; a scan of the current target with the drive off. A first scan sells its data.
        void Discover(SolarSystem sys, Ship ship, double jd, double simDt)
        {
            foreach (var b in sys.Bodies)
            {
                if (b.Parent == null || IsDerelict(b) || Codex.Visited.Contains(b.Name)) continue;
                if ((ship.Pos - sys.Pos(b, jd)).Mag < Codex.VisitRange(b)) { Codex.Visited.Add(b.Name); Say(Happening.Visited, "Codex: " + b.Name + " visited"); }
            }
            var t = ship.Target;
            if (t == null || t.Parent == null || IsDerelict(t) || Codex.Scanned.Contains(t.Name)) { Codex.ScanTarget = null; return; }
            if (Codex.ScanTarget != t.Name) { Codex.ScanTarget = t.Name; Codex.ScanProgress = 0; }
            if (ship.Throttle > 0 || (ship.Pos - sys.Pos(t, jd)).Mag > Codex.ScanRange(t)) return;
            Codex.ScanProgress += simDt;
            if (Codex.ScanProgress < Codex.ScanSeconds) return;
            Codex.Scanned.Add(t.Name); Codex.Visited.Add(t.Name);
            if (Sandbox) { Say(Happening.Scanned, "Codex: " + t.Name + " scanned"); return; }
            double v = Codex.DataValue(sys, t, jd);
            Credits += v; Earned += v;
            Say(Happening.Scanned, $"Codex: {t.Name} scanned. First data sold: +{v:N0} cr");
        }

        public bool BuySensors()
        {
            if (Docked == null || Codex.SensorLevel > 0 || Credits < Codex.SensorCost) return false;
            Credits -= Codex.SensorCost; Codex.SensorLevel = 1;
            Say(Happening.Upgrade, "Sensors upgraded: scan range ×2, scan time halved");
            return true;
        }

        // Docked: in a bound orbit close to the port body with the drive off (or sitting on its surface).
        public static double DockRange(Body b) => Ship.Standoff(b) * 2;

        // Once docked, trim burns while the autopilot circularises don't undock you; leaving range or orbit does.
        Port FindDock(SolarSystem sys, Ship ship, double jd, Port was)
        {
            foreach (var p in Ports)
            {
                var b = sys[p.Body];
                if (b == null) continue;
                if (ship.Landed == b) return p;
                if (ship.Throttle > 0 && p != was) continue;
                var r = ship.Pos - sys.Pos(b, jd);
                double d = r.Mag;
                if (d > DockRange(b)) continue;
                double v = (ship.Vel - sys.Vel(b, jd)).Mag;
                if (v * v < 2 * b.GM / d) return p;
            }
            return null;
        }

        void Deliver(double jd)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var c = Active[i];
                if (c.Kind == JobKind.Survey || c.To != Docked.Name) continue;
                Complete(c, jd, c.Kind == JobKind.Passengers ? "Passengers delivered to " + c.To : "Delivered to " + c.To);
            }
        }

        void Complete(Contract c, double jd, string what)
        {
            bool late = jd > c.DeadlineJd;
            double pay = late ? c.Pay * 0.5 : c.Pay;
            Credits += pay; Earned += pay; Done++;
            Active.Remove(c);
            RemoveDerelict(c);
            Say(c.Kind == JobKind.Rescue ? Happening.RescueDone : c.Kind == JobKind.Survey ? Happening.SurveyDone : Happening.JobPaid, $"{what}: +{pay:N0} cr" + (late ? " (late, half pay)" : ""));
        }

        public static double ScanRange(Body b) => Math.Max(Ship.Standoff(b) * 2, b.Radius + 1000);

        void Scan(SolarSystem sys, Ship ship, double jd, double simDt)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var c = Active[i];
                if (c.Kind != JobKind.Survey) continue;
                var b = sys[c.To];
                if (b == null || ship.Throttle > 0) continue;
                if ((ship.Pos - sys.Pos(b, jd)).Mag > Codex.ScanRange(b)) continue;
                c.Dwell += simDt * (Codex.SensorLevel > 0 ? 2 : 1);
                if (c.Dwell < c.DwellNeeded) continue;
                Codex.Scanned.Add(c.To); Codex.Visited.Add(c.To);
                Complete(c, jd, "Survey of " + c.To + " sent by tight beam");
            }
        }

        void Expire(double jd)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var c = Active[i];
                if (jd - c.IssuedJd <= 2 * (c.DeadlineJd - c.IssuedJd)) continue;
                Active.RemoveAt(i); Failed++;
                RemoveDerelict(c);
                Say(Happening.JobFailed, "Contract failed, far past its deadline: " + c.Title, false);
            }
        }

        // ------------------------------------------------------------ the board

        public bool CanAccept(Contract c, out string why)
        {
            why = null;
            if (Docked == null) why = "dock first";
            else if (Active.Count >= MaxActive) why = $"{MaxActive} jobs at most";
            else if (CargoTonnes + c.Load > HoldTonnes) why = "hold full";
            return why == null;
        }

        public void Accept(Contract c, double jd)
        {
            if (!CanAccept(c, out _)) return;
            Board.Remove(c);
            taken.Add(c.Key);
            c.IssuedJd = jd; c.DeadlineJd = jd + c.Days;
            Active.Add(c);
            SpawnDerelict(c);
            Say(Happening.JobAccepted, "Accepted: " + c.Title);
        }

        public void Abandon(Contract c)
        {
            if (!Active.Remove(c)) return;
            RemoveDerelict(c);
            Failed++;
            Say(Happening.JobFailed, "Abandoned: " + c.Title, false);
        }

        void RefreshBoard(SolarSystem sys, Ship ship, double jd)
        {
            long day = (long)Math.Floor(jd);
            if (boardPort == Docked.Name && boardDay == day) return;
            boardPort = Docked.Name; boardDay = day;
            Board.Clear();
            var rng = new System.Random(Docked.Name.GetHashCode() * 7919 + (int)(day % 100000));
            var here = sys[Docked.Body];
            var others = Array.FindAll(Ports, p => p != Docked && sys[p.Body] != null);
            // nearer ports get more work: weight by distance^-0.75, so the far ports still show up now and then
            var w = new double[others.Length]; double wsum = 0;
            for (int i = 0; i < others.Length; i++) { w[i] = 1 / Math.Pow((sys.Pos(sys[others[i].Body], jd) - sys.Pos(here, jd)).Mag, 0.75); wsum += w[i]; }
            Port Pick() { double x = rng.NextDouble() * wsum; for (int i = 0; i < others.Length; i++) if ((x -= w[i]) <= 0) return others[i]; return others[others.Length - 1]; }

            for (int k = 0; k < 7; k++)
            {
                var c = k < 3 ? Haul(rng, Pick(), JobKind.Cargo, sys, jd)
                      : k < 5 ? Haul(rng, Pick(), JobKind.Passengers, sys, jd)
                      : k == 6 ? (rng.NextDouble() < 0.6 ? Rescue(rng, sys, jd) : null)
                      : Survey(rng, sys, jd);
                if (c == null) continue;
                c.Key = $"{Docked.Name}/{day}/{k}";
                if (!taken.Contains(c.Key)) Board.Add(c);
            }
        }

        static readonly string[] Goods =
        {
            "Medical supplies", "Water reclamation parts", "D–He³ fuel pellets", "Hydroponics seed stock", "Mining drill heads",
            "Data cores and mail", "Spin-bearing assemblies", "Radiator panels", "Air scrubber cartridges", "Spare reactor coils",
        };
        static readonly string[] Riders = { "Survey crew", "Miners on rotation", "Medical team", "Settler families", "Research team", "Engineers" };
        static readonly string[] Scans = { "Gravity survey", "Spectral scan", "Ice-prospecting scan", "Shape and spin survey" };

        // Pay is set from the reaction mass a 0.6 g brachistochrone would burn, at a mid-system price, with a margin.
        static double RefPay(double d, double tonnes, out double days, double slack)
        {
            double a = RefG * Ship.G0;
            double dv = 2 * Math.Sqrt(a * d);
            double mass = 300 + 100 + tonnes;
            double fuel = mass * (1 - Math.Exp(-dv / Ship.Ve));
            days = (slack * 2 * Math.Sqrt(d / a) + 43200) / 86400;
            double urgency = 1.5 - 0.35 * (slack - 1);
            return (fuel * RefFuelPrice * 2.5 + 2000 + tonnes * 40) * urgency;
        }

        Contract Haul(System.Random rng, Port to, JobKind kind, SolarSystem sys, double jd)
        {
            double d = (sys.Pos(sys[to.Body], jd) - sys.Pos(sys[Docked.Body], jd)).Mag;
            double slack = 1 + rng.NextDouble();
            var c = new Contract { Id = nextId++, Kind = kind, From = Docked.Name, To = to.Name };
            if (kind == JobKind.Cargo)
            {
                c.Tonnes = 10 + rng.Next(9) * 10;
                c.Title = Goods[rng.Next(Goods.Length)];
                c.Pay = RefPay(d, c.Tonnes, out c.Days, slack);
            }
            else
            {
                c.People = 2 + rng.Next(39);
                c.Title = Riders[rng.Next(Riders.Length)];
                c.Pay = RefPay(d, c.Load, out c.Days, slack) * 1.5;
                // passengers ride at 1 g or less: give them the time a 1 g ship needs at least
                c.Days = Math.Max(c.Days, (1.15 * 2 * Math.Sqrt(d / (PassengerG * Ship.G0)) + 43200) / 86400);
            }
            c.Pay = Math.Round(c.Pay / 100) * 100;
            return c;
        }

        Contract Survey(System.Random rng, SolarSystem sys, double jd)
        {
            var here = sys[Docked.Body];
            var system = here.Parent == sys.Sun ? here : here.Parent;
            var portBodies = new HashSet<string>(Array.ConvertAll(Ports, p => p.Body));
            var pool = sys.Bodies.FindAll(b => b.Parent == system && b.Radius >= 5 && b.Radius < 400 && !portBodies.Contains(b.Name));
            // the Earth–Moon system and the belt have few small moons: send them to named asteroids instead
            if (pool.Count < 2) pool = sys.Bodies.FindAll(b => b.Asteroid && !portBodies.Contains(b.Name));
            if (pool.Count == 0) return null;
            // one of the nearest few, so a survey is a side trip and not a voyage
            var hp = sys.Pos(here, jd);
            pool.Sort((x, y) => (sys.Pos(x, jd) - hp).Mag.CompareTo((sys.Pos(y, jd) - hp).Mag));
            var t = pool[rng.Next(Math.Min(4, pool.Count))];
            double d = (sys.Pos(t, jd) - sys.Pos(here, jd)).Mag;
            double slack = 1.2 + rng.NextDouble();
            var c = new Contract
            {
                Id = nextId++, Kind = JobKind.Survey, From = Docked.Name, To = t.Name, Title = Scans[rng.Next(Scans.Length)] + " of " + t.Name,
                DwellNeeded = DwellSeconds,
            };
            c.Pay = Math.Round(RefPay(d, 0, out c.Days, slack) * 1.2 / 100) * 100 + 1000;
            c.Days += DwellSeconds / 86400;
            return c;
        }

        // ------------------------------------------------------------ services

        public double FillCost(Ship ship) => Docked == null ? 0 : (TankCapacity - ship.Fuel) * Docked.FuelPrice;

        public void BuyFuel(Ship ship, double tonnes)
        {
            if (Docked == null) return;
            tonnes = Math.Min(tonnes, TankCapacity - ship.Fuel);
            tonnes = Math.Min(tonnes, Math.Max(Credits, 0) / Docked.FuelPrice);
            if (tonnes <= 0.01) return;
            ship.Fuel += tonnes;
            Credits -= tonnes * Docked.FuelPrice;
            Say(Happening.FuelBought, $"Bought {tonnes:N1} t of reaction mass: −{tonnes * Docked.FuelPrice:N0} cr");
        }

        // Adrift with a dry tank: a tender brings reaction mass. Debt is allowed.
        public void EmergencyDrop(Ship ship)
        {
            ship.Fuel = Math.Min(TankCapacity, ship.Fuel + EmergencyTonnes);
            Credits -= EmergencyCost;
            Say(Happening.EmergencyTender, $"Emergency tender: +{EmergencyTonnes:N0} t of reaction mass, −{EmergencyCost:N0} cr", false);
        }

        public bool BuyDrive()
        {
            if (Docked == null || DriveLevel >= DriveG.Length - 1 || Credits < DriveCost[DriveLevel + 1]) return false;
            Credits -= DriveCost[++DriveLevel];
            Say(Happening.Upgrade, $"Drive upgraded: rated {DriveRating:0} g");
            return true;
        }

        public bool BuyTank(Ship ship)
        {
            if (Docked == null || TankLevel >= MaxTankLevel || Credits < TankCost) return false;
            Credits -= TankCost; TankLevel++;
            Say(Happening.Upgrade, $"Tank enlarged to {TankCapacity:N0} t");
            return true;
        }

        // ------------------------------------------------------------ trip estimates

        const double ParkSeconds = 3 * 3600;

        // Flip-and-burn with an optional coast: cruise at a fraction f of the brachistochrone peak speed √(a·d).
        // Δv = 2·f·√(a·d); time = √(d/a)·(f + 1/f). Reaction mass by the rocket equation at the current mass.
        public static void Trip(double d, double a, double f, double mass, out double seconds, out double dv, out double tonnes)
        {
            f = Math.Max(0.05, Math.Min(f, 1));
            double peak = Math.Sqrt(a * d);
            dv = 2 * f * peak;
            // the formula is from rest to rest; leaving orbit and parking at the far end add a few hours (measured by
            // CareerQA: +0.7 h to the Moon, +5 h Luna → Earth, +7 h to Pallas)
            seconds = Math.Sqrt(d / a) * (f + 1 / f) * 1.1 + ParkSeconds;
            tonnes = mass * (1 - Math.Exp(-dv / Ship.Ve));
        }

        // Δv left in the tank at the current mass.
        public static double DvLeft(Ship ship) => Ship.Ve * Math.Log(ship.Mass / (ship.Mass - ship.Fuel));

        // ------------------------------------------------------------ save

        public CareerSave ToSave(Ship ship) => new CareerSave
        {
            credits = Credits, earned = Earned, fuel = ship.Fuel, drive = DriveLevel, tank = TankLevel, nextId = nextId,
            done = Done, failed = Failed, sandbox = Sandbox, active = new List<Contract>(Active),
            sensors = Codex.SensorLevel, visited = new List<string>(Codex.Visited), scanned = new List<string>(Codex.Scanned),
            achieved = new List<string>(Codex.Achieved), dockedAt = new List<string>(Codex.DockedAt),
        };

        public void FromSave(CareerSave s, Ship ship)
        {
            Credits = s.credits; Earned = s.earned; DriveLevel = Mathf.Clamp(s.drive, 0, DriveG.Length - 1); TankLevel = Mathf.Clamp(s.tank, 0, MaxTankLevel);
            nextId = s.nextId; Done = s.done; Failed = s.failed; Sandbox = s.sandbox;
            foreach (var c in Active) RemoveDerelict(c);
            Active.Clear(); if (s.active != null) Active.AddRange(s.active);
            foreach (var c in Active) SpawnDerelict(c);
            Board.Clear(); boardPort = null; taken.Clear();
            foreach (var c in Active) taken.Add(c.Key);
            Codex.SensorLevel = s.sensors;
            void Fill(HashSet<string> set, List<string> l) { set.Clear(); if (l != null) set.UnionWith(l); }
            Fill(Codex.Visited, s.visited); Fill(Codex.Scanned, s.scanned); Fill(Codex.Achieved, s.achieved); Fill(Codex.DockedAt, s.dockedAt);
            ship.Fuel = s.fuel;
            Apply(ship);
        }
    }
}
