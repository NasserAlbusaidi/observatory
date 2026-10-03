using System;

namespace FogOfWar.OpenWorld
{
    // Intercept jobs: a ship already under thrust between two ports won't stop for you. Catch it, match its velocity
    // while it burns, and hold alongside while the cargo crosses over, before it arrives.
    //
    // Its course is the cubic (Hermite) path from the origin port's state at T0 to the destination's at T1. That is
    // the trajectory of a drive whose thrust changes linearly in time; peak thrust is kept near 0.25 g so a 1 g
    // ship can catch it. The Sun's pull (about 0.0006 g at 1 AU) is left out of its course and called out on screen.
    public partial class Career
    {
        public const double InterceptKm = 500, InterceptKms = 0.1, CrossSeconds = 600;
        const double RunnerG = 0.25;
        const string RunnerPrefix = "Runner ";

        static readonly string[] Runners = { "courier Halcyon", "liner Meridian", "freighter Okavango", "tanker Ross 128", "packet Wren", "hauler Tisiphone" };

        internal Contract Intercept(System.Random rng, SolarSystem sys, double jd)
        {
            var from = Ports[rng.Next(Ports.Length)];
            var to = Ports[rng.Next(Ports.Length)];
            if (to == from || sys[from.Body] == null || sys[to.Body] == null) return null;
            double d = (sys.PosAt(sys[to.Body], jd) - sys.PosAt(sys[from.Body], jd)).Mag;
            double T = Math.Sqrt(6 * d / (RunnerG * Ship.G0)) / 86400; // days, peak drive ≈ RunnerG
            double done = 0.1 + rng.NextDouble() * 0.3;              // already part way
            var c = new Contract
            {
                Id = nextId++, Kind = JobKind.Intercept, From = Docked.Name, RunFrom = from.Name, RunTo = to.Name,
                T0 = jd - done * T, T1 = jd + (1 - done) * T, Tonnes = 5 + rng.Next(4) * 5,
            };
            string hull = Runners[rng.Next(Runners.Length)];
            c.To = RunnerPrefix + hull.Substring(hull.IndexOf(' ') + 1) + " " + c.Id;
            c.Title = $"Deliver {c.Tonnes:N0} t of {Goods[rng.Next(Goods.Length)].ToLowerInvariant()} to the {hull}, under thrust from {from.Name} to {to.Name}";
            var b = MakeRunner(c, sys);
            double gap = (sys.PosAt(b, jd) - sys.PosAt(sys[Docked.Body], jd)).Mag;
            c.Pay = Math.Round(RefPay(gap, c.Tonnes, out _, 1) * 1.6 / 100) * 100 + 3000;
            c.Days = c.T1 - jd;
            return c;
        }

        public static Body MakeRunner(Contract c, SolarSystem sys)
        {
            Body a = sys[PortAt(c.RunFrom).Body], z = sys[PortAt(c.RunTo).Body];
            double T = (c.T1 - c.T0) * 86400;
            var p0 = sys.PosAt(a, c.T0); var m0 = Vel(sys, a, c.T0) * T;
            var p1 = sys.PosAt(z, c.T1); var m1 = Vel(sys, z, c.T1) * T;
            D3 Pos(double jd)
            {
                if (jd <= c.T0) return sys.PosAt(a, jd);
                if (jd >= c.T1) return sys.PosAt(z, jd); // arrived: it sits at the port
                double s = (jd - c.T0) / (c.T1 - c.T0), s2 = s * s, s3 = s2 * s;
                return p0 * (2 * s3 - 3 * s2 + 1) + m0 * (s3 - 2 * s2 + s) + p1 * (-2 * s3 + 3 * s2) + m1 * (s3 - s2);
            }
            D3 Accel(double jd)
            {
                if (jd <= c.T0 || jd >= c.T1) return D3.Zero;
                double s = (jd - c.T0) / (c.T1 - c.T0);
                return (p0 * (12 * s - 6) + m0 * (6 * s - 4) + p1 * (6 - 12 * s) + m1 * (6 * s - 2)) / (T * T);
            }
            return new Body { Name = c.To, Parent = sys.Sun, GM = 0, Radius = 0.05, Look = Look.Rocky, Path = Pos, PathAccel = Accel };
        }

        // Peak drive acceleration of the runner's course, in g (the thrust is linear in time, so it peaks at an end).
        public static double RunnerPeakG(Body b, Contract c) =>
            Math.Max(b.PathAccel(c.T0 + 1e-6).Mag, b.PathAccel(c.T1 - 1e-6).Mag) / Ship.G0;

        static D3 Vel(SolarSystem sys, Body b, double jd) => (sys.PosAt(b, jd + 30.0 / 86400) - sys.PosAt(b, jd - 30.0 / 86400)) / 60.0;

        void Intercepts(SolarSystem sys, Ship ship, double jd, double simDt)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var c = Active[i];
                if (c.Kind != JobKind.Intercept) continue;
                if (jd >= c.T1)
                {
                    Active.RemoveAt(i); Failed++; RemoveDerelict(c);
                    Say(Happening.JobFailed, $"Missed: the {c.To.Substring(RunnerPrefix.Length)} reached {c.RunTo}", false);
                    continue;
                }
                var b = sys[c.To];
                if (b == null) continue;
                bool alongside = (ship.Pos - sys.Pos(b, jd)).Mag < InterceptKm && (ship.Vel - sys.Vel(b, jd)).Mag < InterceptKms;
                if (!alongside) { c.Dwell = 0; continue; }
                c.Dwell += simDt;
                if (c.Dwell >= CrossSeconds) Complete(c, jd, "Cargo handed over to the " + c.To.Substring(RunnerPrefix.Length) + " in flight");
            }
        }
    }
}
