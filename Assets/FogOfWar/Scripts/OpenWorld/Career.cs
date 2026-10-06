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

    public enum JobKind { Cargo, Passengers, Survey, Rescue, Intercept }

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
        // intercept: the other ship's run from port RunFrom (leaving at T0) to port RunTo (arriving at T1)
        public string RunFrom, RunTo;
        public double T0, T1;

        public double Load => Kind == JobKind.Cargo || Kind == JobKind.Intercept ? Tonnes : Kind == JobKind.Passengers ? People * Career.PassengerTonnes : 0;
    }

    [Serializable]
    public class CareerSave
    {
        public double credits, fuel, earned;
        public int drive, tank, nextId, done, failed;
        public bool sandbox;
        public int tutorial;             // first-flight lesson + 1; 0 = done or skipped (and for saves made before it existed)
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
        public int TutorialStep;         // the scene's first-flight lesson + 1, kept so Continue resumes it (or not) — QA-051
        public readonly List<Contract> Active = new List<Contract>();
        public readonly List<Contract> Board = new List<Contract>();
        public Port Docked { get; private set; }
        int nextId = 1;
        string boardPort;
        long boardDay = long.MinValue;
        readonly HashSet<string> taken = new HashSet<string>();

        // What is the event the note reports (left at JobAccepted for notes made outside Say), so the HUD and the sound
        // can make a payday feel different from a fuel purchase.
        public struct Note { public string Text; public bool Good; public float Time; public Happening What; }
        public static bool IsPay(Happening h) => h == Happening.JobPaid || h == Happening.RescueDone || h == Happening.SurveyDone || h == Happening.InterceptDone;
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
        public enum Happening { JobAccepted, JobPaid, JobFailed, Visited, Scanned, Achievement, FuelBought, EmergencyTender, Upgrade, Docked, Undocked, TankDry, RescueDone, SurveyDone, InterceptDone }
        public event Action<Happening, string> OnEvent;

        void Say(Happening what, string text, bool good = true)
        {
            Notes.Add(new Note { Text = text, Good = good, Time = UnityEngine.Time.realtimeSinceStartup, What = what });
            OnEvent?.Invoke(what, text);
        }

        // A fresh career: docked at Earth with one easy first job to the Moon.
        public void StartNew(double jd)
        {
            TutorialStep = 1;
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
            this.ship = ship; this.jd = jd;
            StartSpan(ship, jd, simDt);
            Apply(ship);
            Notes.RemoveAll(n => UnityEngine.Time.realtimeSinceStartup - n.Time > 8);
            Discover(sys, ship, jd, simDt);
            if (Sandbox) { Docked = null; return; }
            // a job past its final cutoff fails before anything this update could pay it (#24)
            Expire(jd);
            var was = Docked;
            Docked = FindDock(sys, ship, jd, was);
            if (Docked != was) OnEvent?.Invoke(Docked != null ? Happening.Docked : Happening.Undocked, (Docked ?? was).Name);
            bool dry = ship.Fuel <= 0;
            if (dry && !wasDry)
            {
                DropWarp = true;
                // out there with no reaction mass, a rendezvous can't be flown: end those jobs now (QA-041)
                for (int i = Active.Count - 1; i >= 0; i--)
                {
                    var c = Active[i];
                    if (c.Kind != JobKind.Intercept && c.Kind != JobKind.Rescue) continue;
                    Active.RemoveAt(i); Failed++; RemoveDerelict(c);
                    Say(Happening.JobFailed, "Tank dry: " + c.Title + " failed", false);
                }
                Say(Happening.TankDry, $"Out of fuel. Call the emergency tender ({EmergencyTonnes:N0} t, {EmergencyCost:N0} cr) in the Jobs panel.", false);
            }
            wasDry = dry;
            if (Docked != null) { Codex.DockedAt.Add(Docked.Name); Deliver(jd); RefreshBoard(sys, ship, jd); }
            Scan(sys, ship, jd, simDt);
            TransferInProgress = false;
            Rescues(sys, ship, jd, simDt);
            Intercepts(sys, ship, jd, simDt);
            foreach (var t in Codex.CheckFeats(this, ship)) Say(Happening.Achievement, "Achievement: " + t);
        }

        public readonly Codex Codex = new Codex();
        bool wasDry;

        // Visits to every body passed close; a scan of the current target with the drive off. A first scan sells its data.
        void Discover(SolarSystem sys, Ship ship, double jd, double simDt)
        {
            foreach (var b in sys.Bodies)
            {
                if (b.Parent == null || IsCraft(b) || Codex.Visited.Contains(b.Name)) continue;
                if ((ship.Pos - sys.Pos(b, jd)).Mag < Codex.VisitRange(b)) { Codex.Visited.Add(b.Name); Say(Happening.Visited, "Codex: " + b.Name + " visited"); }
            }
            var t = ship.Target;
            if (t == null || t.Parent == null || IsCraft(t) || Codex.Scanned.Contains(t.Name)) { Codex.ScanTarget = null; return; }
            if (Codex.ScanTarget != t.Name) { Codex.ScanTarget = t.Name; Codex.ScanProgress = 0; }
            double q = ScanSeconds(sys, t, Codex.ScanRange(t), jd, simDt);
            if (q <= 0) return;
            Codex.ScanProgress += q;
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

        // Docked: in a bound orbit close to the port body with the drive off (or sitting on its surface). The orbit's
        // periapsis must clear the surface by DockClearance, so a fall toward the ground never counts (#23).
        public static double DockRange(Body b) => Ship.Standoff(b) * 2;
        public static double DockClearance(Body b) => b.Radius * 0.05 + 10;

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
                var vr = ship.Vel - sys.Vel(b, jd);
                double v = vr.Mag;
                if (v * v >= 2 * b.GM / d) continue;
                if (Periapsis(r, vr, b.GM) > b.Radius + DockClearance(b)) return p;
            }
            return null;
        }

        // Closest approach of the conic through (r, v) about a body of the given GM: p / (1 + e). A radial fall has none.
        public static double Periapsis(D3 r, D3 v, double gm)
        {
            if (gm <= 0) return r.Mag;
            var h = D3.Cross(r, v);
            var e = D3.Cross(v, h) / gm - r.Norm;
            return h.Sqr / gm / (1 + e.Mag);
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
            if (FinallyExpired(c, jd)) return; // Expire fails it; nothing pays after the final cutoff (#24)
            bool late = jd > c.DeadlineJd;
            double pay = late ? c.Pay * 0.5 : c.Pay;
            Credits += pay; Earned += pay; Done++;
            Active.Remove(c);
            RemoveDerelict(c);
            if (c.Kind == JobKind.Rescue || c.Kind == JobKind.Intercept) DropWarp = true;
            Say(c.Kind == JobKind.Rescue ? Happening.RescueDone : c.Kind == JobKind.Intercept ? Happening.InterceptDone : c.Kind == JobKind.Survey ? Happening.SurveyDone : Happening.JobPaid, $"{what}: +{pay:N0} cr" + (late ? " (late, half pay)" : ""));
        }

        public static double ScanRange(Body b) => Math.Max(Ship.Standoff(b) * 2, b.Radius + 1000);

        // ------------------------------------------------------------ scan time (#19)

        // The ship's state at the start of this update (the end of the last one), so a long update credits only the time
        // actually spent in scan conditions, whatever the time compression.
        D3 lastPos, lastVel, spanPos, spanVel;
        double lastJd = double.NaN, lastBurn, spanJd, spanBurn;
        bool spanOk;

        void StartSpan(Ship ship, double jd, double simDt)
        {
            // a load or a jump in the date leaves nothing to interpolate: that update credits no scan time
            spanOk = !double.IsNaN(lastJd) && simDt > 0 && Math.Abs((jd - lastJd) * 86400 - simDt) < 0.01 + 1e-6 * simDt;
            spanPos = lastPos; spanVel = lastVel; spanJd = lastJd; spanBurn = lastBurn;
            lastPos = ship.Pos; lastVel = ship.Vel; lastJd = jd; lastBurn = ship.BurnSeconds;
        }

        // Seconds of this update spent within range of b with the drive off. In between the update's end states the
        // position relative to b is taken as a cubic (Hermite) curve, sampled for range crossings; if both ends are in
        // range the whole update counts. Drive time is taken off as if it all fell in range, so this never overcounts.
        double ScanSeconds(SolarSystem sys, Body b, double range, double jd, double simDt)
        {
            if (!spanOk) return 0;
            var r1 = ship.Pos - sys.Pos(b, jd);
            var r0 = spanPos - sys.PosAt(b, spanJd);
            double d0 = r0.Mag - range, d1 = r1.Mag - range, inside;
            if (d0 <= 0 && d1 <= 0) inside = simDt;
            else
            {
                var m0 = (spanVel - sys.Vel(b, spanJd)) * simDt;
                var m1 = (ship.Vel - sys.Vel(b, jd)) * simDt;
                int n = (int)Math.Min(64, Math.Ceiling(simDt / 10));
                inside = 0;
                double prev = d0;
                for (int i = 1; i <= n; i++)
                {
                    double s = (double)i / n, s2 = s * s, s3 = s2 * s;
                    var p = r0 * (2 * s3 - 3 * s2 + 1) + m0 * (s3 - 2 * s2 + s) + r1 * (-2 * s3 + 3 * s2) + m1 * (s3 - s2);
                    double d = i == n ? d1 : p.Mag - range;
                    // the part of this piece inside the range, with the distance taken as linear across it
                    if (prev <= 0 && d <= 0) inside += simDt / n;
                    else if (prev <= 0 || d <= 0) inside += simDt / n * -Math.Min(prev, d) / Math.Abs(prev - d);
                    prev = d;
                }
            }
            return Math.Max(0, inside - (ship.BurnSeconds - spanBurn));
        }

        void Scan(SolarSystem sys, Ship ship, double jd, double simDt)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var c = Active[i];
                if (c.Kind != JobKind.Survey) continue;
                var b = sys[c.To];
                if (b == null) continue;
                double q = ScanSeconds(sys, b, Codex.ScanRange(b), jd, simDt);
                if (q <= 0) continue;
                c.Dwell += q * (Codex.SensorLevel > 0 ? 2 : 1);
                if (c.Dwell < c.DwellNeeded) continue;
                Codex.Scanned.Add(c.To); Codex.Visited.Add(c.To);
                Complete(c, jd, "Survey of " + c.To + " sent by tight beam");
            }
        }

        // Late delivery pays half until twice the time allowed has passed; after that the job has failed.
        static bool FinallyExpired(Contract c, double jd) => jd - c.IssuedJd > 2 * (c.DeadlineJd - c.IssuedJd);

        void Expire(double jd)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var c = Active[i];
                if (!FinallyExpired(c, jd)) continue;
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
            // an offer left on the board while the runner flew on (QA-064)
            else if (c.Kind == JobKind.Intercept && Sys != null && TargetOf(c) is Body rb && !Catchable(c, rb, Sys, jd)) why = $"can't catch it before it reaches {c.RunTo}";
            else if (ship != null && (c.Kind == JobKind.Intercept || c.Kind == JobKind.Rescue))
            {
                // no port out there to refuel at: even a 50 % coast must fit in what is in the tank
                double need = NeedTonnes(c, 0.5);
                if (need > ship.Fuel * 0.95) why = $"needs ≈ {need:N0} t of fuel even with a coast, {ship.Fuel:N0} t aboard";
            }
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

            for (int k = 0; k < 8; k++)
            {
                var c = k < 3 ? Haul(rng, Pick(), JobKind.Cargo, sys, jd)
                      : k < 5 ? Haul(rng, Pick(), JobKind.Passengers, sys, jd)
                      : k == 6 ? (rng.NextDouble() < 0.6 ? Rescue(rng, sys, jd) : null)
                      : k == 7 ? (rng.NextDouble() < 0.6 ? Intercept(rng, sys, jd) : null)
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

        // A port never strands a ship: what the credits don't cover, up to a tender's load in the tank, it sells on credit
        // at its own price (debt is allowed, as for the tender). Docked, dry and broke used to be a dead end.
        public void BuyFuel(Ship ship, double tonnes)
        {
            if (Docked == null) return;
            tonnes = Math.Min(tonnes, TankCapacity - ship.Fuel);
            double afford = Math.Max(Credits, 0) / Docked.FuelPrice;
            double onCredit = Math.Max(0, Math.Min(tonnes, EmergencyTonnes - ship.Fuel) - afford);
            tonnes = Math.Min(tonnes, afford + onCredit);
            if (tonnes <= 0.01)
            {
                Notes.Add(new Note { Text = "Not enough credits for fuel", Good = false, Time = UnityEngine.Time.realtimeSinceStartup });
                return;
            }
            ship.Fuel += tonnes;
            Credits -= tonnes * Docked.FuelPrice;
            Say(Happening.FuelBought, $"Bought {tonnes:N1} t of fuel: −{tonnes * Docked.FuelPrice:N0} cr" + (onCredit > 0.01 ? $" ({onCredit:N1} t on credit)" : ""));
        }

        // Adrift with a dry tank: a tender brings reaction mass. Debt is allowed.
        public void EmergencyDrop(Ship ship)
        {
            ship.Fuel = Math.Min(TankCapacity, ship.Fuel + EmergencyTonnes);
            Credits -= EmergencyCost;
            Say(Happening.EmergencyTender, $"Emergency tender: +{EmergencyTonnes:N0} t of fuel, −{EmergencyCost:N0} cr", false);
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
        // From rest, Δv = 2·f·√(a·d) and time = √(d/a)·(f + 1/f). QA-061: u and w are the ship's closing speed and
        // sideways drift relative to the target (Ship.TripFrom); left at 0 they give the from-rest figures that pay is
        // set from. Reaction mass by the rocket equation at the current mass.
        public static void Trip(double d, double a, double f, double mass, out double seconds, out double dv, out double tonnes, double u = 0, double w = 0)
        {
            f = Math.Max(0.05, Math.Min(f, 1));
            var (v, t) = Ship.TripFrom(d, a, u, w, f < 1 ? f * Math.Sqrt(a * d) : 0);
            dv = v;
            // the formula is from rest to rest; leaving orbit and parking at the far end add a few hours (measured by
            // CareerQA: +0.7 h to the Moon, +5 h Luna → Earth, +7 h to Pallas)
            seconds = t * 1.1 + ParkSeconds;
            tonnes = mass * (1 - Math.Exp(-dv / Ship.Ve));
        }

        // A brachistochrone's cruise speed for a coast at fraction f (0 = no cap, full burn) over distance d still to go.
        // Chosen during the flight, a coast never sets it below the closing speed the ship already has: it stops
        // accelerating rather than burning to slow down.
        public static double CruiseCap(double f, double a, double d, double closing = 0) =>
            f < 1 ? Math.Max(f * Math.Sqrt(a * d), closing) : 0;

        // Δv left in the tank at the current mass.
        public static double DvLeft(Ship ship) => Ship.Ve * Math.Log(ship.Mass / (ship.Mass - ship.Fuel));

        // ------------------------------------------------------------ save

        public CareerSave ToSave(Ship ship) => new CareerSave
        {
            credits = Credits, earned = Earned, fuel = ship.Fuel, drive = DriveLevel, tank = TankLevel, nextId = nextId,
            done = Done, failed = Failed, sandbox = Sandbox, active = new List<Contract>(Active), tutorial = TutorialStep,
            sensors = Codex.SensorLevel, visited = new List<string>(Codex.Visited), scanned = new List<string>(Codex.Scanned),
            achieved = new List<string>(Codex.Achieved), dockedAt = new List<string>(Codex.DockedAt),
        };

        public void FromSave(CareerSave s, Ship ship)
        {
            Credits = s.credits; Earned = s.earned; DriveLevel = Mathf.Clamp(s.drive, 0, DriveG.Length - 1); TankLevel = Mathf.Clamp(s.tank, 0, MaxTankLevel);
            nextId = s.nextId; Done = s.done; Failed = s.failed; Sandbox = s.sandbox; TutorialStep = s.tutorial;
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
