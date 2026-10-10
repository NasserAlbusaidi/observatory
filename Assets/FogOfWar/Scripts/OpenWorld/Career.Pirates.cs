using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // A raider's chase (docs/design-world.md W4): the pirate Ship of a Pursuit, flying Plan.Pursue at the player, and a
    // proxy Body that follows it in sys.Bodies, so the target card, SetTarget and Divert see it like any craft. The scene
    // steps the player first and calls Advance with that step; Career decides the rest (Career.Pirates.cs).
    public sealed class Encounter
    {
        public readonly Contract Job;
        public readonly Pursuit Chase;
        public readonly Body Proxy, Lair;   // Lair: the body it waited at, at rest with it (null for a start placed by hand)
        public readonly string Hull;         // the raider's own name; the proxy's adds the faction's from the table
        public readonly double CatchSeconds; // the closed-form catch time at the start (Career.CatchSeconds)
        public double Dwell;                 // the boarding clock: seconds alongside, reset on any break
        public double Hopeless;              // seconds the catch has stayed out of reach (#98), reset when it comes back
        public double FixJd;                 // 3b: the raider's last fix on the ship: the call, then every update it can see the hull (not saved)
        public double Range, Vrel;           // from the player at the last career update
        public readonly List<Missile> Missiles = new List<Missile>(); // 4a: in flight (Career.Damage.cs); the chase's end drops them
        public int Magazine = Career.MagazineSize;                     // 4a: missiles it has left
        public int Killed;                                             // 4b: its missiles the PDCs killed
        public double Repel;                 // 4b: seconds alongside, held off by a mount, its magazine spent (Career.RepelS)
        internal readonly SolarSystem Sys;   // the system the proxy is in
        D3 pos, vel, acc;
        double at;

        public Ship Pirate => Chase.Pirate;

        // the proxy's type is what marks it (Career.IsRaider), so its name is free to follow the faction table (D13)
        internal sealed class RaiderBody : Body { }

        internal Encounter(SolarSystem sys, Contract job, Body lair, string hull, string name, double jd, D3 p, D3 v, double g, Ship player, double catchSeconds)
        {
            Sys = sys; Job = job; Lair = lair; Hull = hull; CatchSeconds = catchSeconds;
            Chase = new Pursuit(sys, jd, p, v, g, player.Pos, player.Vel);
            pos = p; vel = v; acc = D3.Zero; at = jd;
            // a craft (Path set) so it is never a dock, a Near body, a frame or a codex entry; GM 0 and a 50 m radius keep it
            // out of the gravity sum, the substep rule, the obstacles and the eclipse test
            Proxy = new RaiderBody
            {
                Name = name, Parent = sys.Sun, GM = 0, Radius = 0.05, Look = Look.Rocky,
                ColA = new Color(0.75f, 0.3f, 0.26f), ColB = new Color(0.45f, 0.2f, 0.18f), Path = PathAt, PathAccel = _ => acc,
            };
            Range = (p - player.Pos).Mag; Vrel = (v - player.Vel).Mag;
        }

        // The player's step from (p0, v0) at jd to v1, h seconds later: the pirate flies the same span (Pursuit.Advance), then
        // the proxy takes its state at the span's end and its mean acceleration over the span, gravity included, as Guide
        // reads PathAccel in place of a target's gravity. A quadratic, so sys.Vel's central difference is its velocity.
        public void Advance(SolarSystem sys, double jd, double h, D3 p0, D3 v0, D3 v1)
        {
            if (!(h > 0)) return;
            var before = Pirate.Vel;
            Chase.Advance(sys, jd, h, p0, v0, v1);
            pos = Pirate.Pos; vel = Pirate.Vel; acc = (vel - before) / h; at = Chase.SpanEnd;
            sys.Pos(sys.Sun, double.NaN); // the proxy keeps its Body: no read of the old path may survive in the date cache
        }

        // The span's acceleration holds for PathQuadS either side of its end, past sys.Vel's 30 s difference, and the
        // path goes on straight from there: the raider's thrust after this frame is not known, and a quadratic read hours
        // ahead (the map's meet point) would be off by a·t²/2, about 1e6 km at 1 g and 4 h.
        const double PathQuadS = 60;
        D3 PathAt(double jd)
        {
            double t = (jd - at) * 86400, tq = Math.Max(-PathQuadS, Math.Min(PathQuadS, t));
            return pos + vel * t + acc * (tq * t - 0.5 * tq * tq);
        }
    }

    // W4: raiders. A route call (RouteKind.Pirate) starts an encounter, kept here and not in the call, so Run can close
    // the card while the chase goes on. The pilot wins by planning: burn harder (a pirate of the same g closes only on the
    // autopilot's reserve), dock first (a docked ship is safe), divert into a patrol's reach, or give up part of the job.
    // Not saved: a load drops it and says so.
    public partial class Career
    {
        // working values [design]. D1 (#90): a 1 g raider at rest with its lair; it can't board an arriving ship before the dock
        public const double PirateG = 1;                          // D1: the raider's drive, in g
        public const double RaidsPerHour = 0.2;                   // D3: k, calls per exposed hour (PirateRate)
        public const double BoardShare = 0.5, GiveUpShare = 0.25; // D5: the part of the job's pay, and of its tonnes for cargo
        public const double ChaseWarp = 1000;                     // D7: the warp cap's top in a chase
        public const double EventS = 10;                          // [design] Phase 5 point 1: the next event this many real seconds away, or more
        public const double CatchMinS = 2 * 3600, CatchMaxS = 6 * 3600;        // D11: the catch time a raider's start must give
        public const double GiveUpS = 3600;                       // #98: how long the catch stays past CatchMaxS before it breaks off
        public const double RepelS = 3600;                        // combat 4b (D9): held off with a spent magazine this long, it breaks off
        static readonly string[] RaiderHulls = { "Magpie", "Jackal", "Lamprey", "Skua", "Cutlass", "Remora" };

        public static bool IsRaider(Body b) => b is Encounter.RaiderBody;
        static string Raiders => Factions[(int)Faction.Raiders].Name; // decision 2: the faction table names them (D13)
        // the answers' labels, on the card and, once Run has closed it, in the scene's job card and classic panel
        public static string GiveUpLabel => $"Give up {GiveUpShare * 100:0} %";
        public static string DivertLabel(Port p) => "Divert to " + p.Name;

        public Encounter Encounter { get; private set; }
        // the scene's: set the body as the target and engage the Fast plan; false when the plan does not engage (D6)
        public Func<Body, bool> Divert;
        bool contactLost; // a load dropped an encounter: the note waits for a tick, which knows the date and the ship

        public const double ImpactHoldS = 20; // [design] D11 (combat 4c): 1× for the last 20 s before an impact, 200 km out at 10 km/s
        // A frame cut to end at the hold lands within jd's 40 µs grid of it, so the hold takes that frame's end as in: the
        // scene would otherwise cut a frame of nothing, and time would stop
        public const double ImpactSlackS = 0.01;

        // The seconds to the next missile impact: each missile's closed form (Missile.ImpactInS), which Volley steps exactly.
        // Infinite with none in flight, and in the sandbox, whose tick flies no missiles.
        public double ImpactInS
        {
            get
            {
                var e = Encounter;
                double t = double.PositiveInfinity;
                if (e == null || Sandbox) return t;
                foreach (var m in e.Missiles) t = Math.Min(t, m.ImpactInS);
                return t;
            }
        }

        // True for D11's 1× hold: the next impact ImpactHoldS away or less
        public bool ImpactHeld => ImpactInS <= ImpactHoldS + ImpactSlackS;

        // The time compression the scene must hold to while a raider gives chase (0: none). Phase 5 point 1: the cap follows
        // time, not range: the next event (ChaseEvents) EventS of real time away or more, at most ChaseWarp, at least 1×.
        // The positions are read now, not at the last update, so the scene's cap before a frame's sim time is set is that
        // frame's. 4c: 1× from ImpactHoldS before an impact; the scene ends the frame before it there.
        public double WarpCap
        {
            get
            {
                if (Encounter == null) return 0;
                if (ImpactHeld) return 1;
                return Math.Max(1, Math.Min(ChaseWarp, EventInS / EventS));
            }
        }

        // Phase 5 point 1: what the cap waits for
        internal enum ChaseEvent { Impact, Fire, Alongside, Boarded, Flip, Arrival }

        readonly List<(ChaseEvent what, double s)> chaseEvents = new List<(ChaseEvent, double)>();

        // the seconds to the soonest event of the chase (ChaseEvents); infinite with none
        public double EventInS
        {
            get
            {
                ChaseEvents(chaseEvents);
                double t = double.PositiveInfinity;
                foreach (var x in chaseEvents) t = Math.Min(t, x.s);
                return t;
            }
        }

        // Phase 5 point 1: each event ahead in the chase and its seconds from now, each a closed form that is never late. The
        // next impact (Missile.ImpactInS). The raider at FireKm while it has missiles and none in flight, and alongside
        // (InterceptKm, and InterceptKms: the speed to lose takes |v| − InterceptKms over a at least), at ReachInS on its
        // drive and the ship's thrust toward it. A boarding, alongside with no mount bearing. Under the player's Fast plan,
        // the flip (or the end of the cruise) and the arrival.
        internal void ChaseEvents(List<(ChaseEvent what, double s)> into)
        {
            into.Clear();
            var e = Encounter;
            if (e == null) return;
            double impact = ImpactInS;
            if (impact < double.PositiveInfinity) into.Add((ChaseEvent.Impact, impact));
            if (ship == null) return;
            var r = e.Pirate.Pos - ship.Pos;
            var v = e.Pirate.Vel - ship.Vel;
            double d = r.Mag, u = d > 1e-9 ? -D3.Dot(r, v) / d : 0;
            double toward = d > 1e-9 ? D3.Dot(ship.Heading, r / d) : 0;
            double a = PirateG * Ship.G0 + Math.Max(0, ship.Throttle * ship.AMax * toward);
            if (e.Magazine > 0 && e.Missiles.Count == 0) into.Add((ChaseEvent.Fire, ReachInS(d - FireKm, u, a)));
            if (!(d < InterceptKm && v.Mag < InterceptKms)) into.Add((ChaseEvent.Alongside, Math.Max(ReachInS(d - InterceptKm, u, a), (v.Mag - InterceptKms) / a)));
            else if (MountsBearing(ship, r) == 0) into.Add((ChaseEvent.Boarded, Math.Max(0, Threat.BoardingSeconds - e.Dwell)));
            if (!FastPlanIn(e.Sys, ship, out double arrive, out double brake)) return;
            into.Add((ChaseEvent.Arrival, arrive));
            if (brake > 0) into.Add((ChaseEvent.Flip, brake));
        }

        // The player's Fast plan from now: the seconds to the arrival (Ship.TripFrom) and to the brake (BrakeInS). False
        // with none flying.
        bool FastPlanIn(SolarSystem sys, Ship ship, out double arrive, out double brake)
        {
            arrive = brake = double.NaN;
            if (ship == null || !ship.PlanActive || ship.Plan != Plan.Brachistochrone || ship.Target == null || !(ship.AMax > 0)) return false;
            var T = ship.Target;
            double dist = Math.Max((sys.Pos(T, jd) - ship.Pos).Mag - Ship.Standoff(T), 0), am = ship.AMax;
            var (uT, wT) = ship.RelToTarget(sys, jd);
            arrive = Ship.TripFrom(dist, am, uT, wT, ship.CruiseCap).t;
            brake = BrakeInS(dist, uT, am, ship.CruiseCap);
            return true;
        }

        // The seconds to close s km from a closing speed u (negative: opening) at a straight acceleration a: s = u·t + a·t²/2,
        // t = 2s / (u + √(u² + 2as)), which keeps its digits when u ≫ a·t. 0 when s ≤ 0. Nothing closes faster from there,
        // so it is a lower bound on the raider's real time.
        internal static double ReachInS(double s, double u, double a)
        {
            if (!(s > 0)) return 0;
            if (!(a > 0)) return u > 0 ? s / u : double.PositiveInfinity;
            return 2 * s / (u + Math.Sqrt(u * u + 2 * a * s));
        }

        // The seconds until Ship.Guide's Fast plan starts to brake: it steers the closing speed onto √(2 · 0.85a · d), so
        // from u at d it flips where (u + a·t)² = 1.7a·(d − u·t − a·t²/2), at v = √((1.7a·d + 0.85u²) / 1.85). With a
        // cruise cap below that it brakes at d = cap² / 1.7a, after the climb to the cap and the coast. 0 or less: braking.
        internal static double BrakeInS(double d, double u, double a, double cap)
        {
            double vf = Math.Sqrt((1.7 * a * d + 0.85 * u * u) / 1.85);
            if (!(cap > 0) || vf <= cap) return (vf - u) / a;
            double climb = Math.Max(0, (cap - u) / a), climbKm = Math.Max(0, (cap * cap - u * u) / (2 * a));
            return climb + Math.Max(0, d - climbKm - cap * cap / (1.7 * a)) / cap;
        }

        // The body a raider waits at for job c: the job's target on the second half of the trip, the posting port on the first.
        Body Lair(Contract c, SolarSystem sys, Ship ship, double jd)
        {
            var to = TargetOf(c);
            var from = sys[PortAt(c.From)?.Body ?? ""];
            if (to == null || from == null) return to;
            return (ship.Pos - sys.Pos(to, jd)).Mag <= (ship.Pos - sys.Pos(from, jd)).Mag ? to : from;
        }

        // D3: raider calls per hour on job c: RaidsPerHour × the raiders' presence here × e^(−v / Threat.MatchKms), v the
        // ship's speed against the job's lair. That is W3's exposure, so the planner's risk line predicts it. None inside a
        // patrol's reach.
        internal double PirateRate(SolarSystem sys, Ship ship, Contract c, double jd)
        {
            double p = Threat.Presence(sys, ship.Pos, jd);
            if (p <= 0) return 0;
            var lair = Lair(c, sys, ship, jd);
            if (lair == null || Threat.PatrolAt(sys, ship.Pos, jd) != null) return 0;
            return RaidsPerHour * p * Math.Exp(-(ship.Vel - sys.Vel(lair, jd)).Mag / Threat.MatchKms);
        }

        // D11's closed form: the time a raider with drive a, at r and moving at v relative to the ship, needs to be alongside
        // a ship that coasts on. In a frame moving with the ship the meeting point is fixed, so it is a stop at a point, as
        // Catchable has it: d = |r|, u the raider's closing speed, w its sideways drift; Ship.TripFrom(d, a, u, w).t + w / a.
        // TripFrom: from rest-ish, vp = √(a·d + u²/2) and t = (2·vp − u) / a; too fast to stop short (u² / 2a > d),
        // t = (u + 2·√(a·(u² / 2a − d))) / a. Its present thrust is left out: a ship that burns away takes longer.
        public static double CatchSeconds(D3 r, D3 v, double a)
        {
            if (!(a > 1e-12)) return double.PositiveInfinity;
            double d = r.Mag;
            var los = d > 1e-9 ? r / -d : new D3(1, 0, 0);
            double u = D3.Dot(v, los), w = (v - los * u).Mag;
            return Ship.TripFrom(d, a, u, w).t + w / a;
        }

        // D11: where a raider waits for the ship: at rest with the lair, on the line from the lair's centre through the ship,
        // the nearest the lair whose catch time is CatchMinS to CatchMaxS, out of every patrol's reach (W3: raiders work
        // where no patrol gets there in time). No start more than a·CatchMaxS² from the ship fits, so the candidates run
        // from there (or the dock range) to as far past the ship, in steps of 5 % but never over a·CatchMinS²/8: from
        // relative rest the window is a ring a·CatchMinS²/4 to a·CatchMaxS²/4 from the ship, about 1e6 km wide at 1 g,
        // which 5 % steps jump over past 2e7 km from the lair. False when none fits. A start closer than InterceptKm is
        // passed over: past ~160 km/s across the line the sideways drift alone (w / a) nearly fills the window, so the
        // ship's own point fits, and far is exactly 72 steps, so the grid lands on it.
        static bool Spawn(Body lair, SolarSystem sys, Ship ship, double jd, out D3 pos, out D3 vel, out double catchS)
        {
            var lp = sys.Pos(lair, jd);
            vel = sys.Vel(lair, jd);
            var off = ship.Pos - lp;
            double dist = off.Mag, a = PirateG * Ship.G0, far = a * CatchMaxS * CatchMaxS, step = a * CatchMinS * CatchMinS / 8;
            var n = dist > 1e-9 ? off / dist : new D3(1, 0, 0);
            for (double s = Math.Max(DockRange(lair), dist - far); s < dist + far; s += Math.Min(0.05 * s, step))
            {
                pos = lp + n * s;
                if ((pos - ship.Pos).Mag < InterceptKm) continue;
                catchS = CatchSeconds(pos - ship.Pos, vel - ship.Vel, a);
                if (catchS >= CatchMinS && catchS <= CatchMaxS && Threat.PatrolAt(sys, pos, jd) == null) return true;
            }
            pos = D3.Zero; catchS = double.NaN;
            return false;
        }

        // MakeCall's raider: an encounter and its card. False for a job it does not work, while one is on, for a docked or
        // landed ship, inside a patrol's reach, or with no start out of reach that gives D11's catch time.
        bool PirateCall(Contract job, SolarSystem sys, Ship ship, double jd)
        {
            if (Encounter != null || Docked != null || ship.Landed != null) return false;
            if (job.Kind != JobKind.Cargo && job.Kind != JobKind.Passengers && job.Kind != JobKind.Survey) return false;
            if (Threat.PatrolAt(sys, ship.Pos, jd) != null) return false;
            var lair = Lair(job, sys, ship, jd);
            if (lair == null || !Spawn(lair, sys, ship, jd, out var pos, out var vel, out double catchS)) return false;
            var e = StartContact(job, sys, ship, jd, pos, vel, lair, catchS);
            var port = NearestPatrol(sys, ship, jd, out double divertS);
            string hull = e.Hull, title = InSentence(job.Title);
            string takes = job.Kind == JobKind.Cargo ? $"{BoardShare * 100:0} % of the {title} and of its pay" : $"{BoardShare * 100:0} % of the pay for the {title}";
            string giveUp = GiveUpLabel;
            var answers = new List<(RouteAnswer, string)> { (RouteAnswer.Run, "Run") };
            if (port != null) answers.Add((RouteAnswer.Divert, DivertLabel(port)));
            answers.Add((RouteAnswer.GiveUp, giveUp));
            // D13: a faction's name takes no possessive, no indefinite article and no verb that agrees with it, so a singular
            // name reads
            Call = new RouteCall
            {
                Kind = RouteKind.Pirate, Job = job, DivertTo = port, Answers = answers.ToArray(),
                Title = $"{Raiders} in pursuit",
                Text = $"The {Raiders} had a ship, the {hull}, waiting {(pos - sys.Pos(lair, jd)).Mag:N0} km off {lair.Name}." + SeenLine(ship, pos, jd) +
                       $" At {PirateG:0} g it can be alongside in " +
                       $"about {Span(catchS / 86400)}, and {Span(Threat.BoardingSeconds / 86400)} alongside is a boarding: it takes {takes}. " +
                       $"It carries {MagazineSize} missiles to cripple the drive. " +
                       "Run: burn hard, or dock first. " +
                       (DarkReady(ship) && SinkLeftS(ship) >= HoldS(ship) ? $"Or go dark: cut the drive and coast {Signature.HoldText(HoldS(ship))} out of its sight. " : "") +
                       (port != null ? $"Divert: {port.Name} is {Span(divertS / 86400)} away at full burn, in the {Factions[(int)port.Owner].Name} patrol's reach. " : "") +
                       $"Or give up {GiveUpShare * 100:0} % now and it leaves.",
                Yes = giveUp, No = "Run",
            };
            return true;
        }

        // An encounter with the pirate at (pos, vel) on date jd, about job (also the QA hook: a start placed by hand). Its
        // proxy body joins sys.Bodies; the pirate's stand-in for the player stays out.
        internal Encounter StartContact(Contract job, SolarSystem sys, Ship ship, double jd, D3 pos, D3 vel, Body lair = null, double catchS = double.NaN)
        {
            DropContact();
            string hull = RaiderHulls[Math.Abs(job.Id) % RaiderHulls.Length];
            Encounter = new Encounter(sys, job, lair, hull, $"{hull} ({Raiders})", jd, pos, vel, PirateG, ship, catchS);
            Encounter.FixJd = jd; // the call is a fix
            sys.Bodies.Add(Encounter.Proxy);
            return Encounter;
        }

        // Each update: a docked or landed ship is safe, a patrol in reach drives the raider off, its missiles fly (4a,
        // Volley), and one alongside boards at Threat.BoardingSeconds. Alongside is Career.Intercept's rule (InterceptKm and
        // InterceptKms, any throttle, at most 30 s a frame), and any break resets the clock. 4b (D9): a working PDC mount
        // that bears on the raider stops the clock, and held off for RepelS with its magazine spent and none in flight, it
        // breaks off.
        void Contacts(SolarSystem sys, Ship ship, double jd, double simDt)
        {
            // D8: a load dropped a chase (FromSave, LostContact)
            if (contactLost) { contactLost = false; Say(Happening.RaiderGone, LostNote(HavenNear(sys, ship.Pos, jd)), false); }
            var e = Encounter;
            if (e == null) return;
            // looked up only for a note that ends the chase: this runs every update while one is on
            string Haven() => HavenNear(sys, e.Pirate.Pos, jd);
            if (Docked != null) { EndContact(Happening.RaiderGone, OffNote(BreakOff.Docked, Docked.Name, Haven()), true, jd); return; }
            if (ship.Landed != null) { EndContact(Happening.RaiderGone, OffNote(BreakOff.Landed, ship.Landed.Name, Haven()), true, jd); return; }
            if (!Active.Contains(e.Job)) { EndContact(Happening.RaiderGone, OffNote(BreakOff.JobOver, null, Haven()), true, jd); return; }
            var patrol = Threat.PatrolAt(sys, ship.Pos, jd);
            if (patrol != null) { EndContact(Happening.RaiderGone, OffNote(BreakOff.Patrol, patrol.Name, Haven()), true, jd); return; }
            e.Range = (e.Pirate.Pos - ship.Pos).Mag;
            e.Vrel = (e.Pirate.Vel - ship.Vel).Mag;
            bool alongside = e.Range < InterceptKm && e.Vrel < InterceptKms; // as Intercepts has it: a NaN is never alongside
            // #98: the catch from now, on the drive the ship's thrust away from the raider leaves it; past CatchMaxS for
            // GiveUpS, it breaks off
            if (!alongside && CatchNow(e, ship) > CatchMaxS)
            {
                e.Hopeless += simDt;
                if (e.Hopeless >= GiveUpS) { EndContact(Happening.RaiderGone, OffNote(BreakOff.Outrun, null, Haven()), true, jd); return; }
            }
            else e.Hopeless = 0;
            // 3b (D7): a dark-ready hull out of the raider's stowed reach for HoldS since its last fix or burn is lost
            double lose = LoseInS(ship, jd);
            if (double.IsNaN(lose)) e.FixJd = jd;
            else if (lose <= 0) { EndContact(Happening.RaiderGone, OffNote(BreakOff.Lost, null, Haven()), true, jd); return; }
            Volley(e, ship, simDt);
            // a cabin hit failed the chase's passenger job
            if (!Active.Contains(e.Job)) { EndContact(Happening.RaiderGone, OffNote(BreakOff.JobOver, null, Haven()), true, jd); return; }
            ThreatCheck(e, ship, simDt);
            if (!alongside) { e.Dwell = 0; e.Repel = 0; return; }
            if (MountsBearing(ship, e.Pirate.Pos - ship.Pos) > 0)
            {
                if (e.Magazine > 0 || e.Missiles.Count > 0) return;
                e.Repel += simDt;
                if (e.Repel >= RepelS) EndContact(Happening.RaiderGone, OffNote(BreakOff.Repelled, null, Haven()), true, jd);
                return;
            }
            e.Repel = 0;
            e.Dwell += Math.Min(simDt, 30);
            if (e.Dwell < Threat.BoardingSeconds) return;
            var (t, pay) = Take(e.Job, BoardShare);
            DropWarp = true;
            EndContact(Happening.Boarded, BoardedNote(t, pay, Haven()), false, jd);
        }

        // 3b: the seconds until a chasing raider loses a dark ship: HoldS after its last fix or the last loud moment. NaN while
        // it holds the track: no chase, not dark-ready, or inside the stowed range (Range from the last update; a NaN range
        // counts as inside).
        public double LoseInS(Ship ship, double jd)
        {
            var e = Encounter;
            if (e == null || !DarkReady(ship) || !(e.Range >= Signature.RangeKm(0, Signature.StockKm, Signature.ColdW(Build.DarkRho)))) return double.NaN;
            return HoldS(ship) - (jd - Math.Max(e.FixJd, ship.LastLoudJd)) * 86400;
        }

        // Run closes the card; Divert flies to the patrolled port on the card (or the nearest now, once the card is gone);
        // GiveUp hands over GiveUpShare and the raider leaves.
        void AnswerRaider(RouteAnswer a, double jd)
        {
            var e = Encounter;
            bool card = Call?.Kind == RouteKind.Pirate;
            if (e == null) { if (card) Drop(); return; }
            if (a == RouteAnswer.GiveUp)
            {
                var (t, pay) = Take(e.Job, GiveUpShare);
                EndContact(Happening.RaiderGone, GaveUpNote(t, pay, HavenNear(e.Sys, e.Pirate.Pos, jd)), false, jd);
                return;
            }
            if (a == RouteAnswer.Divert)
            {
                // a dry tank: the plan would engage and drop at the next update, so the card stays up
                if (ship != null && ship.Dry) { Say(Happening.RouteCall, DryNote, false); return; }
                var port = (card ? Call.DivertTo : null) ?? (ship != null ? NearestPatrol(e.Sys, ship, jd, out _) : null);
                var b = port != null ? e.Sys[port.Body] : null;
                if (b == null || Divert == null || !Divert(b)) { Say(Happening.RouteCall, NavNote, false); return; }
                if (card || Call?.Kind == RouteKind.Threat) Drop(); // a Threat card's plan line is the old plan's
                Say(Happening.RouteCall, DivertNote(port));
                return;
            }
            if (card) Drop();
            Say(Happening.RouteCall, RunNote, false);
        }

        // W4's notes, built here so CareerQA can measure every one: the classic and -fowlegacyui note strip draws one line
        // of a fixed width with no wrap (OpenSystemScene.DrawCareerNotes). So they leave out what the card and the pill say
        // (the range, the job's title), and a faction's name takes no possessive, no indefinite article and no verb that
        // agrees with it (D13).
        // #98: the catch from now with the ship's thrust f away from the raider: it closes at a − f and brakes at a + f, so
        // the peak closing speed vp has (vp² − u²) / 2(a − f) + vp² / 2(a + f) = d. With a ≤ f it can't speed up and only
        // its closing speed u brings it in. Past a stop-short overshoot it falls back to the coasting ship's closed form.
        internal static double CatchNow(Encounter e, Ship ship)
        {
            var r = e.Pirate.Pos - ship.Pos;
            double d = r.Mag, a = PirateG * Ship.G0;
            var los = d > 1e-9 ? r / -d : new D3(1, 0, 0);
            var v = e.Pirate.Vel - ship.Vel;
            double u = D3.Dot(v, los), w = (v - los * u).Mag;
            double f = Math.Max(0, ship.Throttle * ship.AMax * D3.Dot(ship.Heading, los));
            double a1 = a - f, a2 = a + f;
            if (f <= 0 || (u > 0 && u * u / (2 * a2) > d)) return CatchSeconds(r, v, Math.Max(a1, 1e-12));
            if (a1 <= 1e-12) return u > 0 ? d / u + u / (2 * a2) + w / a2 : double.PositiveInfinity;
            double vp = Math.Sqrt((d + u * u / (2 * a1)) / (1 / (2 * a1) + 1 / (2 * a2)));
            return (vp - u) / a1 + vp / a2 + w / a2;
        }

        internal enum BreakOff { Docked, Landed, JobOver, Patrol, Outrun, Lost, Repelled }
        internal const string DryNote = "Can't divert: the tank is dry", NavNote = "Can't divert: the nav computer would not engage";
        internal static string RunNote => "Running: burn hard, or dock first. Time slows as the raider closes";
        internal static string DivertNote(Port p) => $"Diverting to {p.Name}, into the {Factions[(int)p.Owner].Name} patrol's reach";
        internal static string LostNote(string haven) => "The chase was not saved: the raider is gone" + Back(haven);
        internal static string BoardedNote(double t, double pay, string haven) => "Boarded: the raider took " + Loot(t, pay) + (haven != null ? " back to " + haven : "");
        internal static string GaveUpNote(double t, double pay, string haven) => $"Gave up {Loot(t, pay)}: the raider " + (haven != null ? "goes back to " + haven : "leaves");
        internal static string OffNote(BreakOff why, string where, string haven) =>
            (why == BreakOff.Docked ? $"Docked at {where}" : why == BreakOff.Landed ? $"Landed on {where}" : why == BreakOff.Patrol ? $"{where} patrol in reach"
                : why == BreakOff.Outrun ? "Outrun" : why == BreakOff.Lost ? "Track lost" : why == BreakOff.Repelled ? "PDCs" : "The job is over") +
            ": the raider breaks off" + Back(haven);
        static string Back(string haven) => haven != null ? ", back to " + haven : "";
        static string Loot(double t, double pay) => (t > 0 ? $"{t:N1} t and " : "") + $"{pay:N0} cr";

        // A job's title inside a sentence: the first letter lowered only when the next is lower case, so "Gravity survey
        // of Vesta" reads "gravity survey of Vesta" and "D–He³ fuel pellets" or "QA ice" keep their capitals.
        internal static string InSentence(string title) =>
            title != null && title.Length > 1 && char.IsUpper(title[0]) && char.IsLower(title[1]) ? char.ToLowerInvariant(title[0]) + title.Substring(1) : title;

        // D6: the patrolled port the ship reaches soonest at full burn from its state now (Ship.TripFrom), and that time.
        internal static Port NearestPatrol(SolarSystem sys, Ship ship, double jd, out double seconds)
        {
            Port best = null;
            seconds = double.PositiveInfinity;
            foreach (var p in Ports)
            {
                var b = sys[p.Body];
                if (p.PatrolG <= 0 || b == null) continue;
                var to = sys.Pos(b, jd) - ship.Pos;
                var r = to.Norm;
                var v = sys.Vel(b, jd) - ship.Vel;
                double u = -D3.Dot(v, r), w = (v + r * u).Mag;
                double t = Ship.TripFrom(Math.Max(to.Mag - Ship.Standoff(b), 0), ship.AMax, u, w).t;
                if (t < seconds) { seconds = t; best = p; }
            }
            return best;
        }

        // The raider's share of job c: that part of its pay, and of its load for cargo. The job stays on (no Abandon, no
        // standing change), and Apply takes the ship's cargo mass from Tonnes on the next update. The pay it returns, for the
        // notes, is the pay lost: the cut at the job's grade (3d), as delivery would have paid it. The cut leaves Exposure as
        // it is, so the grade is the same before and after; a plain job's grade is 1.
        static (double tonnes, double pay) Take(Contract c, double share)
        {
            double grade = Grade(c), pay = c.Pay * share, t = c.Kind == JobKind.Cargo ? c.Tonnes * share : 0;
            c.Pay -= pay; c.Tonnes -= t;
            return (t, pay * grade);
        }

        static string HavenNear(SolarSystem sys, D3 x, double jd) => Threat.NearestHaven(sys, x, jd)?.Name; // null: no haven in the system

        // The raider goes at once (no course home in W4), with a note; the next call waits RouteGapDays from now.
        void EndContact(Happening what, string text, bool good, double jd)
        {
            DropContact();
            lastCallJd = jd;
            Say(what, text, good);
        }

        // D8, for the scene's LoadCareer: the career it replaced had a chase. The next update says it was not saved, whether
        // a save is read after this or not (no save, or one that will not read: a new career).
        public void LostContact() => contactLost = true;

        // The raider and its proxy body leave the system now, with no note: a load, a new career. True if there was one.
        public bool DropContact()
        {
            threatKept = false;
            var e = Encounter;
            if (e == null) return false;
            e.Sys.Bodies.Remove(e.Proxy);
            Encounter = null;
            if (Call?.Kind == RouteKind.Pirate || Call?.Kind == RouteKind.Threat) Drop();
            return true;
        }
    }
}
