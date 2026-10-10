using System;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // A raider's missile (combat Phase 4a, docs/design-combat.md): a point in the ship's frame, km from the ship in the sim
    // (ecliptic) frame. It lives in the Encounter, so the end of a chase drops it, and it is not saved. It flies straight at
    // the ship in that frame: from S0, the raider's closing speed when it fired, it burns at MissileG until it has added
    // MissileDvKms, then coasts. Age is its seconds in flight. The defaults are a missile fired from rest just now.
    public struct Missile
    {
        public D3 P;
        public double S0, Age;

        public bool Burning => Age < Career.BurnS;
        // the closing speed t seconds on, km/s
        public double KmsIn(double t) => S0 + Career.MissileA * Math.Min(Math.Max(0, Age + t), Career.BurnS);
        public double Kms => KmsIn(0);

        // km closed in the next t seconds: what is left of the burn, then the coast
        public double ClosesIn(double t)
        {
            double burn = Math.Min(t, Math.Max(0, Career.BurnS - Age));
            return Kms * t + Career.MissileA * burn * (t - 0.5 * burn);
        }

        // the seconds to close km; on the burn 2·km / (s + √(s² + 2·a·km)), which keeps its digits when s ≫ a·t
        public double SecondsTo(double km)
        {
            if (!(km > 0)) return 0;
            double a = Career.MissileA, s = Kms, burn = Math.Max(0, Career.BurnS - Age), onBurn = burn * (s + 0.5 * a * burn);
            if (km <= onBurn) return 2 * km / (s + Math.Sqrt(s * s + 2 * a * km));
            return burn + (km - onBurn) / (s + a * burn);
        }

        public double ImpactInS => SecondsTo(P.Mag);
        // t seconds on, short of the ship
        public Missile After(double t) => new Missile { P = P - P.Norm * ClosesIn(t), S0 = S0, Age = Age + t };
    }

    // The hits not yet repaired (combat Phase 4a). Saved; a save made before it has none. Each count is capped at
    // Career.MaxHits on write, so the floor, the repair bill and the save agree.
    [Serializable]
    public class ShipDamage
    {
        public int drive, sensors;   // hits that count, 0 to Career.MaxHits
        public bool radiators;       // stowable radiators jammed out
        public int mounts;           // 4b: bit i, PDC_i fitted and dead
        public bool Any => drive > 0 || sensors > 0 || radiators || mounts != 0;
        public ShipDamage Clone() => (ShipDamage)MemberwiseClone();
    }

    // Combat Phase 4a: raiders shoot to cripple, not to kill (D1). A raider closer than FireKm fires a salvo when none is
    // in flight; each missile flies as Missile has it and hits on the update it would reach the ship. The plating rolls
    // first, then the side it comes in on picks the system (the hit table). No hit points: a hit halves the drive or the
    // sensors to a floor, vents water, takes cargo, fails a passenger job or jams the radiators out, and any dock repairs the
    // parts for credits and days. The draws come from combatRng, so the route and board draws do not move (D10). 4b: before the
    // plating, the working PDC mounts that bear on the approach get one kill draw (PointDefense), and a PDC hit kills a mount.
    public partial class Career
    {
        public const double FireKm = 2000;                   // [design] D2: fire range
        public const double MissileG = 30, MissileDvKms = 10; // [design] D2 (2026-10-09): the burn, and the closing speed it adds
        public const double MissileA = MissileG * Ship.G0;  // km/s²
        public const double BurnS = MissileDvKms / MissileA;  // [calc] 34.0 s, 170 km from rest; from rest at FireKm 217 s
        public const double MissileKg = 100;                 // [design] Impact effects: its momentum goes into the ship
        public const int Salvo = 2, MagazineSize = 6;        // [design] D2
        public const int MaxHits = 2;                        // [design] D6: hits that count on the drive and the sensors
        public const double HitFactor = 0.5;                 // [design] D6: thrust or sensor range per hit; floor 0.25
        public const double VentShare = 0.2, HoldShare = 0.2; // [design] D6: of the water aboard; of the cargo and its pay
        public const double RepairShare = 0.25;              // [design] D8: of the fitted part's price, per hit
        public const double DriveRepairDays = 1, SensorRepairDays = 0.5, RadiatorRepairDays = 1, MountRepairDays = 1; // [design] D8, per hit

        public enum Aspect { Fore, Broadside, Aft }
        // Pdc: a working mount; with none, the hull takes it
        public enum HitSystem { Drive, Tank, Hold, Cabin, Sensors, Pdc, Radiators }

        // [design] the hit table: per cent by Aspect, in HitSystem order. Each row sums to 100
        internal static readonly int[][] HitWeights =
        {
            new[] { 0, 20, 20, 20, 30, 10, 0 },
            new[] { 10, 30, 20, 10, 10, 15, 5 },
            new[] { 60, 20, 0, 0, 0, 10, 10 },
        };

        public ShipDamage Damage = new ShipDamage();
        public double DriveFactor => Math.Pow(HitFactor, Damage.drive);
        public double SensorFactor => Math.Pow(HitFactor, Damage.sensors);
        public int WorkingMounts => Build.FittedMounts & ~Damage.mounts;

        // the working mounts that bear on sim-frame direction d from the ship, at the ship's attitude now
        public int MountsBearing(Ship ship, D3 d) => PointDefense.Bearing(Build.Hull, WorkingMounts, ToBody(ship, d));

        System.Random combatRng = new System.Random();
        internal void SeedCombat(int seed) => combatRng = new System.Random(seed); // QA

        // The ship's body frame, as Ship has it: Heading = Frame.FromU(Att * Vector3.forward), Att in the render frame. So a
        // sim-frame direction d is Quaternion.Inverse(Att) * Frame.U(d) in body axes: +Z the nose, +Y dorsal, +X starboard
        // (Unity's axes, as Ship.AngVel and Ship.RcsInput). 4b's PDC mount zeniths are in these axes.
        public static Vector3 ToBody(Ship ship, D3 d) => Quaternion.Inverse(ship.Att) * Frame.U(d.Norm);

        // [design] fore within 45° of the nose, aft within 45° of the tail, broadside the rest
        static readonly double ConeCos = Math.Cos(Math.PI / 4);
        public static Aspect AspectOf(Vector3 body) => body.z >= ConeCos ? Aspect.Fore : body.z <= -ConeCos ? Aspect.Aft : Aspect.Broadside;

        // the system a draw u in [0, 1) picks on that aspect
        internal static HitSystem SystemFor(Aspect a, double u)
        {
            var w = HitWeights[(int)a];
            double x = u * 100;
            for (int i = 0; i < w.Length; i++) if (w[i] > 0 && (x -= w[i]) < 0) return (HitSystem)i;
            return (HitSystem)Array.FindLastIndex(w, v => v > 0);
        }

        // Each update of a chase (Contacts): the missiles close and hit, then the raider fires if it can. The approach is p̂
        // at the impact update; the flight is straight in the ship's frame, so it is the side the raider fired from. No
        // salvo once a cabin hit has failed the chase's job: the chase ends in this update.
        void Volley(Encounter e, Ship ship, double simDt)
        {
            if (!(simDt > 0)) return;
            for (int i = 0; i < e.Missiles.Count;)
            {
                var m = e.Missiles[i];
                double t = m.ImpactInS;
                if (t > simDt) { e.Missiles[i] = m.After(simDt); i++; continue; }
                e.Missiles.RemoveAt(i);
                if (Strike(ship, m.P, m.KmsIn(t))) e.Killed++;
            }
            if (e.Missiles.Count > 0 || e.Magazine <= 0 || !(e.Range < FireKm) || !Active.Contains(e.Job)) return;
            Fire(e, ship);
        }

        // a salvo from where the raider is now; the count fired
        int Fire(Encounter e, Ship ship)
        {
            int n = Math.Min(Salvo, e.Magazine);
            if (n <= 0) return 0;
            e.Magazine -= n;
            var from = e.Pirate.Pos - ship.Pos;
            double r = from.Mag;
            // [design] the raider's closing rate on the ship, −d|r|/dt; a raider opening the range gives 0
            var m = new Missile { P = from, S0 = r > 0 ? Math.Max(0, -D3.Dot(from, e.Pirate.Vel - ship.Vel) / r) : 0 };
            for (int i = 0; i < n; i++) e.Missiles.Add(m);
            Say(Happening.MissileFired, FiredNote(n, m.ImpactInS), false);
            return n;
        }

        // One missile at the ship from direction `from`, closing at kms: the PDCs, the plating, then the aspect's system. True
        // when the PDCs killed it. No kill draw with no mount bearing, so a ship without PDCs draws as it did in 4a.
        internal bool Strike(Ship ship, D3 from, double kms)
        {
            Kick(ship, from, kms);
            var body = ToBody(ship, from);
            int m = PointDefense.Bearing(Build.Hull, WorkingMounts, body);
            if (m > 0 && PointDefense.Kills(combatRng, m, kms)) { Say(Happening.MissileKilled, KillNote); return true; }
            if (combatRng.NextDouble() < Build.ArmorStop) { Say(Happening.MissileHit, PlatingNote); return false; }
            Hit(ship, SystemFor(AspectOf(body), combatRng.NextDouble()), body);
            return false;
        }

        // [design] Every missile's mass reaches the hull, killed or not: a kill in the last 200 m (most of them) spreads its
        // debris a few metres at most. So its momentum goes into the ship, m·V / M along its flight: 3.3 m/s to a 300 t ship
        // at 10 km/s. The spray thrown back off the hull is left out (momentum factor 1). No draw: the seeded draws do not move.
        internal static void Kick(Ship ship, D3 from, double kms)
        {
            if (!(from.Sqr > 0) || !(ship.Mass > 0)) return;
            ship.Vel -= from.Norm * (MissileKg * kms / (ship.Mass * 1000));
        }

        // The hit's effect and its note; body is the approach in the body frame. A system with nothing to lose (no water, no
        // cargo or passengers aboard, fixed radiators, no working PDC mount) is the hull's: a note, no change.
        internal void Hit(Ship ship, HitSystem s, Vector3 body = default)
        {
            switch (s)
            {
                case HitSystem.Drive:
                    Damage.drive = Math.Min(MaxHits, Damage.drive + 1);
                    Apply(ship);
                    Say(Happening.MissileHit, $"Hit: drive, thrust {DriveFactor * 100:0} %", false);
                    return;
                case HitSystem.Sensors:
                    Damage.sensors = Math.Min(MaxHits, Damage.sensors + 1);
                    Apply(ship);
                    Say(Happening.MissileHit, $"Hit: sensors, range {SensorFactor * 100:0} %", false);
                    return;
                case HitSystem.Tank when ship.Fuel > 0:
                    // the rest of the tank keeps its temperature: SinkK is intensive
                    double vent = ship.Fuel * VentShare;
                    ship.Fuel -= vent;
                    Say(Happening.MissileHit, $"Hit: tank, {vent:N1} t of water vented", false);
                    return;
                case HitSystem.Hold when HoldJob() is Contract cargo:
                    var (t, pay) = Take(cargo, HoldShare);
                    Say(Happening.MissileHit, "Hit: hold, lost " + Loot(t, pay), false);
                    return;
                case HitSystem.Cabin when CabinJob() is Contract pax:
                    // D7: the job fails as an expired one does: no pay, a failure, the standing
                    Active.Remove(pax); Failed++;
                    RemoveDerelict(pax);
                    Say(Happening.JobFailed, $"Hit: cabin, passengers hurt: {pax.Title} failed", false);
                    ChangeStanding(pax, -StandingLoss);
                    return;
                case HitSystem.Radiators when Build.Stowable:
                    Damage.radiators = true;
                    Say(Happening.MissileHit, "Hit: radiators jammed out, not dark-ready", false);
                    return;
                case HitSystem.Pdc when WorkingMounts != 0:
                    Damage.mounts |= 1 << PointDefense.Nearest(Build.Hull, WorkingMounts, body);
                    Say(Happening.MissileHit, MountNote(PointDefense.Count(WorkingMounts), Build.MountCount), false);
                    return;
            }
            Say(Happening.MissileHit, HullNote, false);
        }

        // the chase's job if it is cargo with a load, else the largest load aboard
        Contract HoldJob()
        {
            var e = Encounter;
            if (e != null && e.Job.Kind == JobKind.Cargo && e.Job.Tonnes > 0 && Active.Contains(e.Job)) return e.Job;
            Contract best = null;
            foreach (var c in Active) if (c.Kind == JobKind.Cargo && c.Tonnes > 0 && (best == null || c.Tonnes > best.Tonnes)) best = c;
            return best;
        }

        // the chase's job if it carries passengers, else the first passenger job aboard
        Contract CabinJob()
        {
            var e = Encounter;
            if (e != null && e.Job.Kind == JobKind.Passengers && e.Job.People > 0 && Active.Contains(e.Job)) return e.Job;
            return Active.Find(c => c.Kind == JobKind.Passengers && c.People > 0);
        }

        // short, one line each: the classic note strip has no wrap (OpenSystemScene.DrawCareerNotes)
        internal const string PlatingNote = "Hit: the plating took it", HullNote = "Hit: the hull took it", KillNote = "PDCs killed a missile";
        internal static string MountNote(int working, int fitted) => $"Hit: a PDC mount, {working} of {fitted} working";
        public static string BearText(int n) => n == 1 ? "1 mount bears" : $"{n} mounts bear";
        internal static string FiredNote(int n, double seconds) => $"Missiles: {n} fired, impact in " + (seconds >= 60 ? $"{Math.Round(seconds / 60):0} min" : $"{Math.Ceiling(seconds):0} s");

        // each damaged system and what it has left, for the Damage row and the yard; null with none
        public string DamageText()
        {
            if (!Damage.Any) return null;
            var parts = new System.Collections.Generic.List<string>();
            if (Damage.drive > 0) parts.Add($"drive {DriveFactor * 100:0} %");
            if (Damage.sensors > 0) parts.Add($"sensors {SensorFactor * 100:0} %");
            if (Damage.radiators) parts.Add("radiators jammed out");
            int dead = PointDefense.Count(Damage.mounts);
            if (dead > 0) parts.Add($"{dead} of {Build.MountCount} PDC mounts out");
            return string.Join(", ", parts);
        }

        // ------------------------------------------------------------ repairs (D8)

        // Any port, every hit at once: RepairShare of the fitted part's price (a mount's for a dead mount) and the part's
        // days per hit, in the yard as a refit is (RefitJd). A refit that replaces a damaged part clears its hits (RefitTo).
        public bool CanRepair(out string why, out double cost, out double days)
        {
            int dead = PointDefense.Count(Damage.mounts);
            cost = Math.Round(RepairShare * (Damage.drive * Build[Slot.Drive].Price + Damage.sensors * Build[Slot.Sensors].Price + (Damage.radiators ? Build[Slot.Radiators].Price : 0)
                                             + dead * ShipBuild.MountPrice));
            days = Damage.drive * DriveRepairDays + Damage.sensors * SensorRepairDays + (Damage.radiators ? RadiatorRepairDays : 0) + dead * MountRepairDays;
            why = Sandbox ? "sandbox" : !Damage.Any ? "no damage" : Docked == null ? "dock first" : Credits < cost ? "not enough credits" : null;
            return why == null;
        }

        public bool Repair(Ship ship)
        {
            if (!CanRepair(out _, out double cost, out double days)) return false;
            Credits -= cost;
            RefitJd = Math.Max(RefitJd, jd) + days;
            Damage = new ShipDamage();
            Apply(ship);
            Say(Happening.Upgrade, $"Repaired: −{cost:N0} cr. In the yard {RefitDays:0.#} days");
            return true;
        }

        // the hits a refit to `to` clears: those on the parts it replaces. A new hull's mounts are new: its fit mask differs
        void ClearReplaced(ShipBuild to)
        {
            if (to.pick[(int)Slot.Drive] != Build.pick[(int)Slot.Drive]) Damage.drive = 0;
            if (to.pick[(int)Slot.Sensors] != Build.pick[(int)Slot.Sensors]) Damage.sensors = 0;
            if (to.pick[(int)Slot.Radiators] != Build.pick[(int)Slot.Radiators]) Damage.radiators = false;
            if (to.pick[(int)Slot.PointDefense] != Build.pick[(int)Slot.PointDefense] || to.hull != Build.hull) Damage.mounts = 0;
        }

        // a save's damage, clamped: a missing field is none, fixed radiators cannot jam, and only a fitted mount can be dead
        static ShipDamage Cleaned(ShipDamage d, ShipBuild b) => d == null ? new ShipDamage() : new ShipDamage
        {
            drive = Math.Max(0, Math.Min(MaxHits, d.drive)), sensors = Math.Max(0, Math.Min(MaxHits, d.sensors)), radiators = d.radiators && b.Stowable,
            mounts = d.mounts & b.FittedMounts,
        };
    }
}
