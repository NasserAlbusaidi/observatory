using System;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Ship: trip-time and delta-v estimates for the nav computer, from rest or from the ship's own state.
    public partial class Ship
    {
        // Flip-and-burn trip time to the target from rest: t = 2·sqrt(d / a).
        public double EtaSeconds(SolarSystem sys, double jd)
        {
            if (Target == null) return 0;
            double d = Math.Max((sys.Pos(Target, jd) - Pos).Mag - Standoff(Target), 0);
            return 2 * Math.Sqrt(d / AMax);
        }

        // QA-061: a brachistochrone from the ship's state, not from rest. u is the closing speed along the line to the
        // target (negative: moving apart, which costs a stop and the ground lost while stopping), w the sideways drift
        // the plan also cancels, cap the coast speed (0 = none). Braking is taken at the full a; the guidance brakes on
        // a 0.85·a curve, which costs a little less. Returns Δv (km/s) and burn-plus-coast time (s), without parking.
        public static (double dv, double t) TripFrom(double d, double a, double u, double w, double cap = 0)
        {
            double along, t;
            if (u > 0 && u * u / (2 * a) > d)
            {
                // too fast to stop short: brake, overshoot, and come back from rest
                double vb = Math.Sqrt(a * (u * u / (2 * a) - d));
                along = u + 2 * vb; t = along / a;
            }
            else
            {
                double vp = Math.Sqrt(a * d + u * u / 2);
                if (cap > 0 && cap < vp)
                {
                    // speed up (or slow down) to the cap, coast, brake from the cap
                    double d1 = (cap >= u ? cap * cap - u * u : u * u - cap * cap) / (2 * a);
                    along = Math.Abs(cap - u) + cap;
                    t = along / a + Math.Max(d - d1 - cap * cap / (2 * a), 0) / cap;
                }
                else { along = 2 * vp - u; t = along / a; }
            }
            return (Math.Sqrt(along * along + w * w), t);
        }

        // The braking curve's share of the drive in Brachistochrone (vb = √(2·a·BrakeShare·d)).
        public const double BrakeShare = 0.85;

        // Time left in each leg of a brachistochrone as the guidance flies it: speed up at a (to the cruise cap, if
        // any), coast at the cap, then brake on the BrakeShare·a curve to the standoff. d is the distance to the
        // standoff, u the closing speed (negative: moving apart, which the first leg stops). Like Career.CruiseCap, a
        // ship already over the cap coasts at its own speed rather than braking down to it. Drift and parking are left
        // out (the trip planner's ETA counts them); this is for the route strip, which shows where the flip falls.
        public static (double accel, double coast, double brake) Legs(double d, double a, double u, double cap = 0)
        {
            double ab = a * BrakeShare, extra = 0;
            if (u < 0) { extra = -u / a; d += u * u / (2 * a); u = 0; }
            if (u * u / (2 * ab) >= d) return (extra, 0, u / ab);
            double vp = Math.Sqrt((d + u * u / (2 * a)) / (1 / (2 * a) + 1 / (2 * ab)));
            if (cap > 0 && cap < vp)
            {
                double v = Math.Max(cap, u);
                double coast = Math.Max(d - (v * v - u * u) / (2 * a) - v * v / (2 * ab), 0) / v;
                return (extra + (v - u) / a, coast, v / ab);
            }
            return (extra + (vp - u) / a, 0, vp / ab);
        }

        // Closing speed toward the target (negative: moving apart) and the sideways drift, both relative to it.
        public (double u, double w) RelToTarget(SolarSystem sys, double jd)
        {
            if (Target == null) return (0, 0);
            var r = (sys.Pos(Target, jd) - Pos).Norm;
            var v = sys.Vel(Target, jd) - Vel;
            double u = -D3.Dot(v, r);
            return (u, (v + r * u).Mag);
        }

        // Hohmann transfer between the circular orbits of the ship's and the target's heliocentric distance (an
        // economy reference next to the brachistochrone): total Δv (km/s) and transfer time (s).
        public static (double dv, double t) Hohmann(double r1, double r2, double mu)
        {
            double at = (r1 + r2) / 2;
            double dv1 = Math.Abs(Math.Sqrt(mu / r1) * (Math.Sqrt(r2 / at) - 1));
            double dv2 = Math.Abs(Math.Sqrt(mu / r2) * (1 - Math.Sqrt(r1 / at)));
            return (dv1 + dv2, Math.PI * Math.Sqrt(at * at * at / mu));
        }
    }
}
