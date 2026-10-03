using System;

namespace FogOfWar.OpenWorld
{
    public enum Attitude { Hold, Prograde, Retrograde, Target, AntiTarget }

    // The player's torch ship: a point mass under every body's gravity plus a fusion drive. Positions are heliocentric
    // ecliptic km, velocities km/s.
    public class Ship
    {
        public const double G0 = 9.80665e-3; // km/s^2
        public D3 Pos, Vel, Heading = new D3(1, 0, 0);
        public double MaxG = 1, Throttle;
        public bool DriveLit;
        public Attitude Attitude = Attitude.Hold;
        public bool Autopilot;
        public Body Target;
        public double DvUsed;
        public string Phase = "coasting";
        public Body Landed;

        public double AMax => MaxG * G0;

        // Standoff from a target's centre where the autopilot parks the ship in orbit.
        public static double Standoff(Body b) => Math.Max(b.Radius * 3, b.Radius + 300);

        // Smallest sphere of influence the ship is inside; the Sun otherwise.
        public static Body Dominant(SolarSystem sys, D3 p, double jd)
        {
            Body best = sys.Sun; double bestSoi = double.MaxValue;
            foreach (var b in sys.Bodies)
            {
                if (b.Parent == null || b == sys.Emb) continue;
                double parentGM = b == sys.Moon ? sys.Earth.GM : b.Parent.GM;
                double soi = b.A * Math.Pow(b.GM / parentGM, 0.4);
                if (b == sys.Earth) soi = sys.Emb.A * Math.Pow(b.GM / sys.Sun.GM, 0.4);
                soi = Math.Max(soi, b.Radius * 1.5);
                if (soi < bestSoi && (sys.Pos(b, jd) - p).Mag < soi) { best = b; bestSoi = soi; }
            }
            return best;
        }

        // Advance by h seconds at Julian date jd (the start of the step).
        public void Step(SolarSystem sys, double jd, double h, Body reference)
        {
            var g = sys.Gravity(Pos, jd);
            if (Autopilot && Target != null) Guide(sys, jd, h, g);
            else
            {
                Phase = DriveLit ? "burning" : "coasting";
                var refVel = sys.Vel(reference, jd);
                switch (Attitude)
                {
                    case Attitude.Prograde: SetHeading(Vel - refVel); break;
                    case Attitude.Retrograde: SetHeading(refVel - Vel); break;
                    case Attitude.Target when Target != null: SetHeading(sys.Pos(Target, jd) - Pos); break;
                    case Attitude.AntiTarget when Target != null: SetHeading(Pos - sys.Pos(Target, jd)); break;
                }
                Throttle = DriveLit ? 1 : 0;
            }

            if (Landed != null)
            {
                if (Throttle * AMax <= 1e-9 || D3.Dot(Heading, (Pos - sys.Pos(Landed, jd)).Norm) <= 0.1)
                {
                    // Sitting on the surface: ride along with the body.
                    var bp = sys.Pos(Landed, jd + h / 86400);
                    var rel = (Pos - sys.Pos(Landed, jd)).Norm;
                    Pos = bp + rel * Landed.Radius * 1.0001;
                    Vel = sys.Vel(Landed, jd);
                    Phase = "landed on " + Landed.Name;
                    return;
                }
                Landed = null;
            }

            var a = g + Heading * (Throttle * AMax);
            Vel += a * h;
            Pos += Vel * h;
            DvUsed += Throttle * AMax * h;

            // Touchdown: anything that hits a surface stops on it (no crash model yet).
            foreach (var b in sys.Bodies)
            {
                if (b == sys.Sun || b == sys.Emb) continue;
                var bp = sys.Pos(b, jd + h / 86400);
                var r = Pos - bp;
                double R = b.Radius * Math.Max(b.Shape.x, Math.Max(b.Shape.y, b.Shape.z)) * 0.98;
                if (r.Mag < R)
                {
                    Pos = bp + r.Norm * b.Radius * 1.0001;
                    Vel = sys.Vel(b, jd);
                    Landed = b; Autopilot = false; DriveLit = false; Throttle = 0;
                    Phase = "landed on " + b.Name;
                    break;
                }
            }
        }

        void SetHeading(D3 d) { if (d.Sqr > 1e-18) Heading = d.Norm; }

        // Brachistochrone guidance: steer relative velocity onto v = sqrt(2·a·distance) toward the target, so the
        // ship accelerates, flips at the right moment and brakes. Near the target it holds a circular orbit.
        void Guide(SolarSystem sys, double jd, double h, D3 gShip)
        {
            var T = Target;
            var tp = sys.Pos(T, jd);
            var tv = sys.Vel(T, jd);
            var r = tp - Pos;
            double dist = r.Mag;
            double standoff = Standoff(T);
            var vrel = Vel - tv;
            double aMax = AMax;
            var gRel = gShip - sys.Gravity(tp, jd, T);

            D3 vDes;
            bool orbiting = dist < standoff * 1.25 && vrel.Mag < Math.Sqrt(T.GM / Math.Max(dist, 1)) * 2 + 0.5;
            if (!orbiting)
            {
                double s = Math.Max(dist - standoff, 0);
                vDes = Avoid(sys, jd, r.Norm, dist) * Math.Sqrt(2 * aMax * 0.85 * s);
            }
            else
            {
                var radial = (-r).Norm;
                var n = T.Pole;
                var tangent = D3.Cross(n, radial);
                if (tangent.Sqr < 1e-9) tangent = D3.Cross(new D3(1, 0, 0), radial);
                tangent = tangent.Norm;
                double vc = Math.Sqrt(T.GM / dist);
                vDes = tangent * vc + radial * (-(dist - standoff) * 0.002);
            }

            // Bodies too small to orbit against their parent's tides (Phobos, Deimos): station-keep beside them instead.
            bool hover = orbiting && T.GM < 1;
            if (hover) vDes = (-r).Norm * (-(dist - standoff) * 0.002);
            var err = vDes - vrel;
            double tau = Math.Max(20, 4 * h);
            var cmd = err / tau - gRel;
            if (orbiting && !hover) cmd = err / tau; // in orbit, gravity is what keeps us there
            if (hover && err.Mag < 0.003) { Throttle = Math.Min(cmd.Mag / aMax, 1); if (cmd.Mag > 1e-12) Heading = cmd.Norm; Phase = "holding station"; return; }
            double c = cmd.Mag;
            if (orbiting && err.Mag < 0.003) { Throttle = 0; Phase = "in orbit"; return; }
            if (c > 1e-12) Heading = cmd / c;
            Throttle = Math.Min(c / aMax, 1);
            Phase = orbiting ? "circularising" : D3.Dot(Heading, r.Norm) >= 0 ? "accelerating" : "flipped, braking";
        }

        // Bend the straight-line course around any body (other than the target) it would pass through.
        D3 Avoid(SolarSystem sys, double jd, D3 dir, double dist)
        {
            foreach (var b in sys.Bodies)
            {
                if (b == Target || b == sys.Emb || b.Parent == null) continue;
                var c = sys.Pos(b, jd);
                var rel = c - Pos;
                double d = rel.Mag;
                if (d > 40 * b.Radius || d > dist) continue;
                double safe = b.Radius * 1.5 + 200;
                if (d < safe) { dir = (dir + (-rel).Norm * 1.5).Norm; continue; } // too close: climb out first
                double t = D3.Dot(rel, dir);
                if (t <= 0) continue;
                var miss = Pos + dir * t - c;
                double m = miss.Mag;
                if (m >= safe) continue;
                var perp = m > 1e-3 ? miss / m : D3.Cross(dir, b.Pole).Norm;
                // aim at the tangent point just outside the safe radius
                dir = ((c + perp * safe) - Pos).Norm;
            }
            return dir;
        }

        // Rough flip-and-burn trip time to the target from rest: t = 2·sqrt(d / a).
        public double EtaSeconds(SolarSystem sys, double jd)
        {
            if (Target == null) return 0;
            double d = Math.Max((sys.Pos(Target, jd) - Pos).Mag - Standoff(Target), 0);
            return 2 * Math.Sqrt(d / AMax);
        }
    }
}
