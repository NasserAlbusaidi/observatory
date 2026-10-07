using System;

namespace FogOfWar.OpenWorld
{
    // A ship on rails between two bodies (docs/design-world.md, "NPC traffic"): the cubic (Hermite) course from the
    // parking-orbit distance of one body at T0 to that of the other at T1, matching each body's velocity at its end. That
    // is the trajectory of a drive whose thrust changes linearly in time. It leaves out gravity, so it is exact and costs
    // one call at any warp. Runners fly it; traffic is its second use.
    public sealed class Rail
    {
        // working values [design]: a course keeps this far from the Sun, and outside the parking orbit of every body but
        // the two it joins. Moons smaller than MinMoonKm are left out: their parking orbit is a few hundred km.
        public const double MinSunAu = 0.3, MinMoonKm = 100;
        const int Samples = 256;
        // CheckEnds reads a planet's position every Stride samples and joins them with cubics, at an eighth of the cost.
        // A knot is at most 0.7 day apart (1/32 of the longest trip), where Earth turns 0.7Â°: the cubic is within a km.
        const int Stride = 8;

        public readonly Body From, To;
        public readonly double T0, T1;
        readonly SolarSystem sys;
        readonly D3 p0, m0, p1, m1, off0, off1;
        readonly double T; // s

        public Rail(SolarSystem sys, Body from, Body to, double t0, double t1)
        {
            this.sys = sys; From = from; To = to; T0 = t0; T1 = t1;
            T = (t1 - t0) * 86400;
            p0 = sys.PosAt(from, t0); m0 = Vel(sys, from, t0) * T;
            p1 = sys.PosAt(to, t1); m1 = Vel(sys, to, t1) * T;
            // The centre-to-centre course says which way the thrust points, relative to each body, as the ship leaves and
            // arrives. Each end moves out that way, so the ship climbs away from one parking orbit and comes down to the
            // other from outside it.
            off0 = Away(AccelAt(0) - BodyAccel(sys, from, t0), p1 - p0) * Ship.Standoff(from);
            off1 = Away(AccelAt(1) - BodyAccel(sys, to, t1), p0 - p1) * Ship.Standoff(to);
            p0 += off0; p1 += off1;
        }

        public D3 Pos(double jd)
        {
            if (jd <= T0) return sys.PosAt(From, jd) + off0;
            if (jd >= T1) return sys.PosAt(To, jd) + off1; // arrived: it holds its place off the port
            double s = (jd - T0) / (T1 - T0), s2 = s * s, s3 = s2 * s;
            return p0 * (2 * s3 - 3 * s2 + 1) + m0 * (s3 - 2 * s2 + s) + p1 * (-2 * s3 + 3 * s2) + m1 * (s3 - s2);
        }

        // the thrust it is under (zero before it leaves and after it arrives)
        public D3 Accel(double jd) => jd <= T0 || jd >= T1 ? D3.Zero : AccelAt((jd - T0) / (T1 - T0));
        D3 AccelAt(double s) => (p0 * (12 * s - 6) + m0 * (6 * s - 4) + p1 * (6 - 12 * s) + m1 * (6 * s - 2)) / (T * T);

        public Body Craft(string name) => new Body { Name = name, Parent = sys.Sun, GM = 0, Radius = 0.05, Look = Look.Rocky, Path = Pos, PathAccel = Accel };

        public struct Clearance
        {
            public double SunAu;  // closest to the Sun
            public double Ratio;  // closest to a body, as a fraction of its parking-orbit distance
            public Body Nearest;
            public bool Clear => SunAu >= MinSunAu && Ratio >= 0.99;
        }

        // How close the course comes to the Sun and to the bodies, measured on straight pieces between samples. The
        // samples are even in time, so they are close together near the ports, where the course bends most.
        public Clearance Check() => Check(sys.Bodies, false);

        // The traffic shortcut checks endpoint parents, plus siblings only for moon endpoints: Titan-to-Enceladus
        // can cross Rhea while clearing Saturn. Planet endpoints retain the old set (no heliocentric sibling sweep).
        // QA checks 400 accepted scheduled trips against the full body list and a Titan/Enceladus Rhea regression.
        // Endpoint children (such as the Moon on Earth departures) remain outside this cheap test's scope.
        public Clearance CheckEnds() => Check(sys.Bodies.FindAll(b => !b.Asteroid && !Career.IsCraft(b) && !Traffic.Is(b) &&
            (b == From.Parent || b == To.Parent ||
             (From.Parent != null && From.Parent != sys.Sun && b.Parent == From.Parent) ||
             (To.Parent != null && To.Parent != sys.Sun && b.Parent == To.Parent))), true);

        Clearance Check(System.Collections.Generic.IEnumerable<Body> bodies, bool smooth)
        {
            var c = new Clearance { SunAu = double.MaxValue, Ratio = double.MaxValue };
            var path = new D3[Samples + 1];
            for (int i = 0; i <= Samples; i++) path[i] = Pos(At(i));
            for (int i = 0; i < Samples; i++) c.SunAu = Math.Min(c.SunAu, Closest(path[i], path[i + 1]) / Frame.AU);
            // Reuse each slow parent track for its moons. The same cubic approximation was already used for
            // the parent clearance; moons retain their fast, fully sampled local orbits. This avoids evaluating
            // a giant planet's fit and massive-moon barycentre correction once per moon per sample.
            var parentTracks = smooth ? new System.Collections.Generic.Dictionary<Body, D3[]>() : null;
            D3[] ParentTrack(Body parent)
            {
                if (parentTracks.TryGetValue(parent, out var track)) return track;
                var knots = new D3[Samples / Stride + 3];
                for (int k = 0; k < knots.Length; k++) knots[k] = sys.PosAt(parent, At((k - 1) * Stride));
                track = new D3[Samples + 1];
                for (int i = 0; i <= Samples; i++) track[i] = path[i] - Cubic(knots, i);
                parentTracks.Add(parent, track);
                return track;
            }
            foreach (var b in bodies)
            {
                if (b == null || b.Parent == null || Career.IsCraft(b) || (b.Parent != sys.Sun && b.Radius < MinMoonKm)) continue;
                bool fit = smooth && b.Parent == sys.Sun;
                bool localMoon = smooth && b.Parent.Parent == sys.Sun && b.Cheb == null && b.Path == null && !b.Barycentric;
                double min = double.MaxValue;
                if (localMoon)
                {
                    var relative = ParentTrack(b.Parent);
                    // An interpolated moon segment stays inside the largest endpoint orbital radius. A ship
                    // segment outside that sphere plus the standoff cannot hit it. The bound also covers rates
                    // in a/e over the trip; fitted/custom moon paths take the full sampling path below.
                    double d0 = T0 - b.EpochJd, d1 = T1 - b.EpochJd;
                    double a = Math.Max(Math.Abs(b.A + b.dA * d0), Math.Abs(b.A + b.dA * d1));
                    double e = Math.Min(0.99, Math.Max(0, Math.Max(b.E + b.dE * d0, b.E + b.dE * d1)));
                    double envelope = a * (1 + e) + Ship.Standoff(b);
                    D3 prev = D3.Zero; int last = -1;
                    for (int i = 1; i <= Samples; i++)
                    {
                        if (Closest(relative[i - 1], relative[i]) > envelope) continue;
                        if (last != i - 1) prev = relative[i - 1] - b.Kepler(At(i - 1));
                        var r = relative[i] - b.Kepler(At(i));
                        min = Math.Min(min, Closest(prev, r));
                        prev = r; last = i;
                    }
                }
                else
                {
                    var track = fit ? ParentTrack(b) : null;
                    D3 prev = D3.Zero;
                    for (int i = 0; i <= Samples; i++)
                    {
                        var r = fit ? track[i] : path[i] - sys.PosAt(b, At(i));
                        if (i > 0) min = Math.Min(min, Closest(prev, r));
                        prev = r;
                    }
                }
                double ratio = min / Ship.Standoff(b);
                if (ratio < c.Ratio) { c.Ratio = ratio; c.Nearest = b; }
            }
            return c;
        }

        double At(int i) => T0 + (T1 - T0) * i / Samples;

        // the cubic through the four knots around sample i; knot k is at sample (k - 1) * Stride
        static D3 Cubic(D3[] k, int i)
        {
            int j = Math.Min(i / Stride, Samples / Stride - 1) + 1; // knots j and j + 1 bracket the sample
            double u = (i - (j - 1) * Stride) / (double)Stride;
            return k[j - 1] * (-u * (u - 1) * (u - 2) / 6) + k[j] * ((u + 1) * (u - 1) * (u - 2) / 2)
                 + k[j + 1] * (-(u + 1) * u * (u - 2) / 2) + k[j + 2] * ((u + 1) * u * (u - 1) / 6);
        }

        // distance from the origin to the segment aâ€“b
        static double Closest(D3 a, D3 b)
        {
            var d = b - a;
            double t = d.Sqr > 0 ? Math.Max(0, Math.Min(1, -D3.Dot(a, d) / d.Sqr)) : 0;
            return (a + d * t).Mag;
        }

        static D3 Away(D3 v, D3 fallback) => v.Sqr > 0 ? v.Norm : fallback.Norm;

        public static D3 Vel(SolarSystem sys, Body b, double jd) => (sys.PosAt(b, jd + 30.0 / 86400) - sys.PosAt(b, jd - 30.0 / 86400)) / 60.0;
        static D3 BodyAccel(SolarSystem sys, Body b, double jd) => (Vel(sys, b, jd + 600.0 / 86400) - Vel(sys, b, jd - 600.0 / 86400)) / 1200.0;
    }
}
