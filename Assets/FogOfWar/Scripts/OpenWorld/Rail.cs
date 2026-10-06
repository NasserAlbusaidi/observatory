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
        public Clearance Check()
        {
            var c = new Clearance { SunAu = double.MaxValue, Ratio = double.MaxValue };
            var path = new D3[Samples + 1];
            for (int i = 0; i <= Samples; i++) path[i] = Pos(T0 + (T1 - T0) * i / Samples);
            for (int i = 0; i < Samples; i++) c.SunAu = Math.Min(c.SunAu, Closest(path[i], path[i + 1]) / Frame.AU);
            foreach (var b in sys.Bodies)
            {
                if (b.Parent == null || Career.IsCraft(b) || (b.Parent != sys.Sun && b.Radius < MinMoonKm)) continue;
                double min = double.MaxValue;
                D3 prev = D3.Zero;
                for (int i = 0; i <= Samples; i++)
                {
                    var r = path[i] - sys.PosAt(b, T0 + (T1 - T0) * i / Samples);
                    if (i > 0) min = Math.Min(min, Closest(prev, r));
                    prev = r;
                }
                double ratio = min / Ship.Standoff(b);
                if (ratio < c.Ratio) { c.Ratio = ratio; c.Nearest = b; }
            }
            return c;
        }

        // distance from the origin to the segment a–b
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
