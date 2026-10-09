using System;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // A raider's missile (combat Phase 4a, docs/design-combat.md): a point in the ship's frame, km from the ship in the sim
    // (ecliptic) frame. It lives in the Encounter, so the end of a chase drops it, and it is not saved.
    public struct Missile { public D3 P; }

    // The hits not yet repaired (combat Phase 4a). Saved; a save made before it has none. Each count is capped at
    // Career.MaxHits on write, so the floor, the repair bill and the save agree.
    [Serializable]
    public class ShipDamage
    {
        public int drive, sensors;   // hits that count, 0 to Career.MaxHits
        public bool radiators;       // stowable radiators jammed out
        // 4b: the dead-mount mask goes here
        public bool Any => drive > 0 || sensors > 0 || radiators;
        public ShipDamage Clone() => (ShipDamage)MemberwiseClone();
    }

    // Combat Phase 4a: raiders shoot to cripple, not to kill (D1). A raider closer than FireKm fires a salvo when none is
    // in flight; each missile closes at MissileKms and hits on the update it would reach the ship. The plating rolls first,
    // then the side it comes in on picks the system (the hit table). No hit points: a hit halves the drive or the sensors to a
    // floor, vents water, takes cargo, fails a passenger job or jams the radiators out, and any dock repairs the parts for
    // credits and days. The draws come from combatRng, so the route and board draws do not move (D10).
    public partial class Career
    {
        public const double FireKm = 2000, MissileKms = 2;   // [design] D2: fire range, closing speed: 1,000 s from FireKm
        public const int Salvo = 2, MagazineSize = 6;        // [design] D2
        public const int MaxHits = 2;                        // [design] D6: hits that count on the drive and the sensors
        public const double HitFactor = 0.5;                 // [design] D6: thrust or sensor range per hit; floor 0.25
        public const double VentShare = 0.2, HoldShare = 0.2; // [design] D6: of the water aboard; of the cargo and its pay
        public const double RepairShare = 0.25;              // [design] D8: of the fitted part's price, per hit
        public const double DriveRepairDays = 1, SensorRepairDays = 0.5, RadiatorRepairDays = 1; // [design] D8, per hit

        public enum Aspect { Fore, Broadside, Aft }
        // Pdc: 4b's mounts. With none fitted in 4a, the hull takes it
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
        // at the impact update; the flight is straight in the ship's frame, so it is the side the raider fired from.
        void Volley(Encounter e, Ship ship, double jd, double simDt)
        {
            if (!(simDt > 0)) return;
            double step = MissileKms * simDt;
            for (int i = 0; i < e.Missiles.Count;)
            {
                var p = e.Missiles[i].P;
                if (p.Mag > step) { e.Missiles[i] = new Missile { P = p - p.Norm * step }; i++; continue; }
                e.Missiles.RemoveAt(i);
                Strike(ship, p);
            }
            if (e.Missiles.Count > 0 || e.Magazine <= 0 || !(e.Range < FireKm)) return;
            int n = Math.Min(Salvo, e.Magazine);
            e.Magazine -= n;
            var from = e.Pirate.Pos - ship.Pos;
            for (int i = 0; i < n; i++) e.Missiles.Add(new Missile { P = from });
            Say(Happening.MissileFired, FiredNote(n, from.Mag / MissileKms), false);
        }

        // one missile at the ship from direction `from`: the plating, then the aspect's system
        internal void Strike(Ship ship, D3 from)
        {
            if (combatRng.NextDouble() < Build.ArmorStop) { Say(Happening.MissileHit, PlatingNote); return; }
            Hit(ship, SystemFor(AspectOf(ToBody(ship, from)), combatRng.NextDouble()));
        }

        // The hit's effect and its note. A system with nothing to lose (no water, no cargo or passengers aboard, fixed
        // radiators, any PDC in 4a) is the hull's: a note, no change.
        internal void Hit(Ship ship, HitSystem s)
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
        internal const string PlatingNote = "Hit: the plating took it", HullNote = "Hit: the hull took it";
        internal static string FiredNote(int n, double seconds) => $"Missiles: {n} fired, impact in " + (seconds >= 60 ? $"{Math.Round(seconds / 60):0} min" : $"{Math.Ceiling(seconds):0} s");

        // each damaged system and what it has left, for the Damage row and the yard; null with none
        public string DamageText()
        {
            if (!Damage.Any) return null;
            var parts = new System.Collections.Generic.List<string>();
            if (Damage.drive > 0) parts.Add($"drive {DriveFactor * 100:0} %");
            if (Damage.sensors > 0) parts.Add($"sensors {SensorFactor * 100:0} %");
            if (Damage.radiators) parts.Add("radiators jammed out");
            return string.Join(", ", parts);
        }

        // ------------------------------------------------------------ repairs (D8)

        // Any port, every hit at once: RepairShare of the fitted part's price and the part's days per hit, in the yard as a
        // refit is (RefitJd). A refit that replaces a damaged part clears its hits (RefitTo).
        public bool CanRepair(out string why, out double cost, out double days)
        {
            cost = Math.Round(RepairShare * (Damage.drive * Build[Slot.Drive].Price + Damage.sensors * Build[Slot.Sensors].Price + (Damage.radiators ? Build[Slot.Radiators].Price : 0)));
            days = Damage.drive * DriveRepairDays + Damage.sensors * SensorRepairDays + (Damage.radiators ? RadiatorRepairDays : 0);
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

        // the hits a refit to `to` clears: those on the parts it replaces
        void ClearReplaced(ShipBuild to)
        {
            if (to.pick[(int)Slot.Drive] != Build.pick[(int)Slot.Drive]) Damage.drive = 0;
            if (to.pick[(int)Slot.Sensors] != Build.pick[(int)Slot.Sensors]) Damage.sensors = 0;
            if (to.pick[(int)Slot.Radiators] != Build.pick[(int)Slot.Radiators]) Damage.radiators = false;
        }

        // a save's damage, clamped: a missing field is none, and fixed radiators cannot jam
        static ShipDamage Cleaned(ShipDamage d, ShipBuild b) => d == null ? new ShipDamage() : new ShipDamage
        {
            drive = Math.Max(0, Math.Min(MaxHits, d.drive)), sensors = Math.Max(0, Math.Min(MaxHits, d.sensors)), radiators = d.radiators && b.Stowable,
        };
    }
}
