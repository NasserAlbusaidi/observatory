using System;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Ship: keeping clear of bodies. The climb-away guard for the autopilot and for a pursuing pirate, and the course
    // bend around anything in the way.
    public partial class Ship
    {
        // Pursuit has an active braking curve, so a ballistic periapsis alone does not predict an impact. Its guard
        // checks the segment to the stopping point along the velocity, plus a bounded turn margin. Radial stopping
        // distance alone falsely treats an off-axis moon-orbit approach as a plunge. The player's Climb is unchanged.
        // #141: a run-out too fast to stop before the body is pushed sideways off its line instead, and is looked for as
        // far out as a sideways burn needs. Inside 40 R alone, a 1 g pirate overshooting the player at 30-130 km/s
        // flew through Vesta: its braking was what Pursue already gave, and 0.2 g sideways came too late.
        const double BodySpeedBound = 100; // km/s; no body moves faster round the Sun (Mercury peaks at 59)

        D3? PursueClearance(SolarSystem sys, double jd)
        {
            foreach (var b in Obstacles)
            {
                if (b == null || b == sys.Sun || b == Target || b.GM < 1) continue;
                var r = Pos - sys.Pos(b, jd); double rm = r.Mag;
                // Avoid still bends the path outside 1.5 R + 200 km. This last-resort braking guard protects the
                // physical surface, rather than repeatedly ejecting a safe approach from that wider routing margin.
                double safe = b.Radius * 1.1 + 20;
                // a dodge under way pushes on to 1.2 safe, so Pursue's pull back toward the line does not toggle it
                double clear = safe * (guardBody == b && Phase.StartsWith("climbing") ? 1.2 : 1.1);
                double lead = 2 * Math.Sqrt(2 * clear / Math.Max(AMax, 1e-9));
                bool near = rm <= 40 * b.Radius;
                if (!near && rm > clear + (Vel.Mag + BodySpeedBound) * lead) continue; // cheap cut before the velocity lookup
                var outward = r / Math.Max(rm, 1e-9); var v = Vel - sys.Vel(b, jd);
                double speed = v.Mag;
                if (!near && rm > clear + speed * lead) continue;
                double inward = -D3.Dot(v, outward);
                double net = Math.Max(AMax - b.GM / (rm * rm), 1e-9);
                var direction = speed > 0 ? v / speed : D3.Zero;
                double ahead = -D3.Dot(r, direction);
                if (rm > safe && ahead > 0)
                {
                    var miss = r + direction * ahead; double m = miss.Mag;
                    double entry = ahead - Math.Sqrt(Math.Max(0, clear * clear - m * m)), stopFull = speed * speed / (2 * net);
                    if (m < clear && entry > 0 && stopFull > entry)
                    {
                        double te = entry / speed, side = 0.5 * AMax * te * te;
                        if (side / (clear - m) > entry / stopFull)
                        {
                            guardBody = b;
                            var away = m > 1e-6 * clear ? miss / m : D3.Cross(b.Pole, direction);
                            if (away.Sqr < 1e-12) away = D3.Cross(new D3(1, 0, 0), direction);
                            return away.Norm * AMax;
                        }
                    }
                }
                if (!near) continue;
                double stop = speed * speed / (2 * PursueMargin * net);
                double turnMargin = Math.Min(0.05 * safe, speed * 5);
                // The minimum radius on the whole braking segment, not just the radius of its endpoint: a stop
                // beyond the far side of the body must still trigger the guard. A safe pole approach misses it.
                double along = Math.Max(0, Math.Min(stop + turnMargin, ahead));
                double closest = (r + direction * along).Mag;
                if (rm > safe && (inward <= 0 || closest > safe)) continue;
                guardBody = b;
                var lateral = v - outward * D3.Dot(v, outward);
                var tangent = lateral.Sqr > 1e-12 ? lateral.Norm : D3.Cross(b.Pole, outward).Norm;
                return (outward + tangent * 0.2).Norm * AMax;
            }
            return null;
        }

        Body guardBody;

        // If the two-body arc around the body we're inside would come within 10 % of its surface, burn radially out and
        // along the motion (raising periapsis) until it won't.

        D3? Clearance(SolarSystem sys, double jd)
        {
            var dom = dominant ?? Dominant(sys, Pos, jd);
            foreach (var b in Obstacles)
                if (Climb(sys, jd, b) is D3 c) return c;
            return Obstacles.Contains(dom) ? null : Climb(sys, jd, dom);
        }

        // Climb away from b when the ship is falling toward it and either (a) its two-body arc would hit the body
        // (periapsis under 1.1 R), or (b) it is falling too fast to stop above the safe radius with the drive left over
        // after gravity (the case of a 1 g ship that kills its orbital speed to head for the Moon behind Earth).
        D3? Climb(SolarSystem sys, double jd, Body b)
        {
            if (b == null || b == sys.Sun || b == Target || b.GM < 1) return null;
            var r = Pos - sys.Pos(b, jd);
            double rm = r.Mag;
            if (rm > 40 * b.Radius) return null; // cheap distance cut before the (uncached) velocity lookup
            var v = Vel - sys.Vel(b, jd);
            var outward = r / rm;
            double vr = D3.Dot(v, outward), vin = -vr;
            if (vin <= 0 && rm > b.Radius * 1.2) return null; // climbing and clear
            double rp = Periapsis(r, v, b.GM), safe = b.Radius * 1.5 + 200;
            double net = AMax - b.GM / (rm * rm);
            bool impact = rp < b.Radius * 1.1;
            bool cannotStop = vin > 0 && rp < safe && rm - safe < vin * vin / Math.Max(net, 1e-9) + 0.3 * safe;
            if (!impact && !cannotStop) return null;
            guardBody = b;
            var along = v.Sqr > 1e-12 && (v - outward * vr).Sqr > 1e-12 ? (v - outward * vr).Norm : D3.Cross(b.Pole, outward).Norm;
            return (outward + along).Norm * AMax;
        }

        // Bend the straight-line course around any body (other than the target) it would pass through.
        D3 Avoid(SolarSystem sys, double jd, D3 dir, double dist)
        {
            foreach (var b in Obstacles)
            {
                if (b == Target || b.Parent == null) continue;
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
                dir = ((c + perp * safe) - Pos).Norm; // aim at the tangent point just outside the safe radius
            }
            return dir;
        }

        // Two-body periapsis radius of relative state (r, v) about GM; for an escape orbit, the hyperbola's.
        static double Periapsis(D3 r, D3 v, double mu)
        {
            double h = D3.Cross(r, v).Mag, eps = v.Sqr / 2 - mu / r.Mag;
            double e = Math.Sqrt(Math.Max(0, 1 + 2 * eps * h * h / (mu * mu)));
            return h * h / mu / (1 + e);
        }
    }
}
