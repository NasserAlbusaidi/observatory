using System;
using System.Collections.Generic;

namespace FogOfWar.OpenWorld
{
    // Where raiders can take a ship (docs/design-world.md, W3). A raider must match the ship's velocity and stay alongside
    // for the boarding time, and a patrol that can get there within that time stops it. So the danger is where a ship is
    // slow, outside every patrol's reach, in the space raiders work: the main belt and around their havens. The trip
    // planner shows the risk, the map shows the reach, and W4's raiders (Career.Pirates.cs) strike at a rate set from both.
    public static class Threat
    {
        // decision 4 in the doc, still open: how long a boarding takes [design]. Every reach and speed here follows from it.
        public const double BoardingSeconds = 2 * 3600;

        // From relative rest, a patrol covers a·t²/4 in the boarding time: it speeds up for half of it and brakes for the
        // other half [calc]. 381,000 km at 3 g.
        public static double Reach(double g) => g * Ship.G0 * BoardingSeconds * BoardingSeconds / 4;

        // A raider that burns for one boarding time at 1 g matches 70.6 km/s [calc]. The faster a ship moves against the
        // ports it is between, the harder it is to catch: its weight falls by e for each MatchKms.
        public const double MatchKms = Ship.G0 * BoardingSeconds;

        // working values [design]: the havens, and how far raiders work from one (a 1 g raider's reach in two days, a·t²/4)
        public static readonly string[] Havens = { "Pallas", "Hektor" };
        public const double HavenAu = 0.5;

        // working values [design]: the tiers, in exposed hours
        static readonly (double Below, string Name)[] Tiers = { (0.1, "none"), (1, "low"), (3, "moderate"), (double.MaxValue, "high") };

        public struct Risk
        {
            public double Hours;             // exposed: slow, seen (a burn or its track), outside every patrol's reach, where raiders work
            public double Leaving, Arriving; // of those, the hours in the first and the second half of the trip
            public string Tier { get { foreach (var t in Tiers) if (Hours < t.Below) return t.Name; return Tiers[Tiers.Length - 1].Name; } }
            public bool Serious => Hours >= Tiers[1].Below; // moderate or high
        }

        // 0 to 1: how much raiders work at x: 1 in the main belt, and from 1 at a haven to 0 at HavenAu from it
        public static double Presence(SolarSystem sys, D3 x, double jd) => Presence(sys, HavenBodies(sys), x, jd);

        static double Presence(SolarSystem sys, Body[] havens, D3 x, double jd)
        {
            double rAu = x.Mag / Frame.AU;
            double p = rAu > Belt.MainInAu && rAu < Belt.MainOutAu ? 1 : 0;
            foreach (var h in havens)
            {
                double q = (x - sys.PosAt(h, jd)).Mag / (HavenAu * Frame.AU);
                if (q < 1) p = Math.Max(p, (1 - q * q) * (1 - q * q));
            }
            return p;
        }

        static Body[] HavenBodies(SolarSystem sys) => Array.FindAll(Array.ConvertAll(Havens, h => sys[h]), b => b != null);

        // the haven nearest x, where a raider goes when it gives up a chase (W4); null when the system has none
        public static Body NearestHaven(SolarSystem sys, D3 x, double jd)
        {
            Body best = null; double bd = double.MaxValue;
            foreach (var h in HavenBodies(sys)) { double d = (x - sys.PosAt(h, jd)).Mag; if (d < bd) { bd = d; best = h; } }
            return best;
        }

        // the port whose patrol can reach x within the boarding time, or null
        public static Port PatrolAt(SolarSystem sys, D3 x, double jd)
        {
            foreach (var p in Career.Ports)
            {
                var b = sys[p.Body];
                if (p.PatrolG > 0 && b != null && (x - sys.PosAt(b, jd)).Mag < Reach(p.PatrolG)) return p;
            }
            return null;
        }

        // A trip of d km from the start to body `to` at a km/s², from rest to rest, cruising at `cap` km/s (0: a full burn),
        // held still for half the parking time (Career.ParkSeconds) at the target; a raider sees a burn and keeps its track
        // Signature.TrackHoldS (combat Phase 2). The ship's place goes from the start's track to the target's as it covers
        // the distance, so it moves with each end while it is slow there. The start is body `from` plus a fixed offset; with
        // the Sun as `from` it is a fixed point, near enough for a ship that starts far from everything.
        public static Risk Of(SolarSystem sys, Body from, D3 offset, Body to, double jd, double d, double a, double cap = 0)
        {
            var r = new Risk();
            if (from == null || to == null || !(d > 1) || !(a > 0)) return r;
            var havens = HavenBodies(sys);
            var patrols = new List<(Body b, double reach)>();
            foreach (var p in Career.Ports)
                if (p.PatrolG > 0 && sys[p.Body] is Body pb) patrols.Add((pb, Reach(p.PatrolG)));

            double vp = Math.Sqrt(a * d);
            if (cap > 0 && cap < vp) vp = cap;
            double ta = vp / a, da = vp * vp / (2 * a), tc = Math.Max(d - 2 * da, 0) / vp, T = 2 * ta + tc;
            double park = Career.ParkSeconds / 2;
            // past 7 MatchKms the weight is under 0.1 %: only the slow parts of the trip are sampled, six samples to each
            // e-fold of the weight
            double fast = Math.Min(ta, 7 * MatchKms / a), step = MatchKms / a / 6;

            // t: seconds from departure; the hold at the target is t > T
            void Add(double t0, double t1, double most)
            {
                if (!(t1 > t0)) return;
                int n = (int)Math.Ceiling((t1 - t0) / most);
                double dt = (t1 - t0) / n;
                for (int i = 0; i < n; i++)
                {
                    double t = t0 + (i + 0.5) * dt, u = Math.Max(0, Math.Min(t, T));
                    double v = u < ta ? a * u : u < ta + tc ? vp : a * (T - u);
                    double s = u < ta ? a * u * u / 2 : u < ta + tc ? da + vp * (u - ta) : d - a * (T - u) * (T - u) / 2;
                    double q = Math.Max(0, Math.Min(s / d, 1)), j = jd + (t + park) / 86400;
                    var x = (sys.PosAt(from, j) + offset) * (1 - q) + sys.PosAt(to, j) * q;
                    double h = Presence(sys, havens, x, j) * Math.Exp(-v / MatchKms);
                    if (h <= 0) continue;
                    foreach (var (pb, reach) in patrols)
                        if ((x - sys.PosAt(pb, j)).Mag < reach) { h = 0; break; }
                    h *= dt / 3600;
                    r.Hours += h;
                    if (t < T / 2) r.Leaving += h; else r.Arriving += h;
                }
            }
            // no hold at the start: the ship has not burned, so no raider sees it (W3 counted 1.5 h)
            Add(0, fast, step);                                                                  // speeding up: loud
            if (vp < 7 * MatchKms) Add(ta, ta + Math.Min(tc, Signature.TrackHoldS), step);       // a slow coast, while the burn is tracked
            Add(T - fast, T, step);                                                              // braking: loud
            Add(T, T + Math.Min(park, Signature.TrackHoldS), park / 6);                          // held at the target, tracked
            return r;
        }

        // the trip planner's line
        public static string Line(Risk r)
        {
            if (r.Hours < Tiers[0].Below) return "Raider risk: none on this course.";
            // #90: a raider called on arrival can't board before the dock (PursuitCheck S3b), so docking first is the answer
            string end = r.Arriving > 2 * r.Leaving ? ", most of it on arrival: dock first and a raider can't board" :r.Leaving > 2 * r.Arriving ? ", most of it on departure" : "";
            return $"Raider risk: {r.Tier}. {r.Hours:0.0} h slow and seen (a burn, or {Signature.TrackHoldS / 3600:0} h after one), out of patrol reach in raider space{end}.";
        }
    }
}
