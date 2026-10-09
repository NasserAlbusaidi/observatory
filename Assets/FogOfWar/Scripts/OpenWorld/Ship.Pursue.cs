using System;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Ship: Plan.Pursue guidance, a pirate on the player (Pursuit.cs). The drive is shared between matching the target's
    // acceleration and steering onto it.
    public partial class Ship
    {
        // Plan.Pursue, a pirate on the player (Pursuit.cs). The target thrusts, so comp (what matches its acceleration)
        // can take most of the drive. Budget leaves AMax − |comp| for steering in every direction, but the drive has more
        // when the steering opposes comp: a 2 g pirate behind a 1 g ship burning away could brake at only 1 g against a
        // curve that assumed 1.7 g, and flew past it at 16 km/s (pirate spike, S2b). Here steering gets all the drive has
        // left along it, and the braking curve all it has left along the braking line. With nothing left (|comp| ≥ AMax)
        // the pirate only matches: a hand burn at exactly its g leaves it no spare, and one of the same g behind the
        // autopilot's full burn closes only on Budget's reserve (about 4e-4 g on a Vesta–Mars leg), so
        // burning harder escapes. The curve counts on the target's present thrust: a target that cuts its drive or flips
        // under it, by hand or as the autopilot does at turnover and at a coast trip's brake start, takes that spare away.
        public const double PursueMargin = 0.85; // [design] the braking curve's share of that spare, as Brachistochrone's

        D3? Pursue(SolarSystem sys, double jd, D3 r, double dist, D3 vrel, D3 comp, double tau)
        {
            // A distinct capture zone leaves a one-kilometre clearance reserve outside the old craft standoff.
            // The target-thrust braking law is deliberately unchanged; its gameplay tradeoff remains issue #89.
            double standoff = Standoff(Target) + 1;
            var los = dist > 0 ? r / dist : Heading;
            bool hold = dist < 20 && vrel.Mag < 0.2;
            if (!hold && PursueClearance(sys, jd) is D3 guard) { Phase = "climbing clear of " + guardBody.Name; return guard; }
            if (comp.Sqr >= AMax * AMax) { Phase = "matching its burn"; return comp.Norm * AMax; }
            D3 want, ff = D3.Zero;
            if (hold)
            {
                // Critically damped relative position control, with target acceleration fed forward. It seeks the
                // 2.05 km standoff; transient approaches can come closer (only S1ahead gates a 1.05 km exclusion).
                // Guide supplies tau >= 20 s, so omega <= 0.0375 /s without additional clamps.
                double omega = 3 / (4 * tau);
                Phase = "holding station";
                return Share(los * ((dist - standoff) * omega * omega) - vrel * (2 * omega), comp);
            }
            // Short relative-motion lead: capped so an acceleration change cannot put the aim far behind the target.
            double leadSeconds = Math.Min(600, Math.Sqrt(Math.Max(dist - standoff, 0) / AMax));
            var transverse = vrel - los * D3.Dot(vrel, los);
            var lead = r - transverse * leadSeconds;
            var aim = lead.Sqr > 0 && D3.Dot(lead, r) > 0 ? lead.Norm : los;
            var dir = Avoid(sys, jd, aim, dist);
            double brake = PursueMargin * Spare(comp, -dir, AMax), vb = Math.Sqrt(2 * brake * Math.Max(dist - standoff, 0));
            want = dir * vb;
            // on or above the braking curve, feed its own deceleration forward (see Brachistochrone)
            if (vb > 0 && D3.Dot(vrel, dir) >= vb) ff = -dir * brake;
            var cmd = Share((want - vrel) / tau + ff, comp);
            // what the drive does, not where the steering points: braking against a target that pulls away can need no thrust
            Phase = cmd.Mag <= RcsAccel ? "trimming (RCS)" : D3.Dot(cmd, los) >= 0 ? "closing" : "braking";
            return cmd;
        }

        // The most the drive has left along unit vector u once it matches the target: the largest k ≥ 0 with
        // |comp + k·u| ≤ aMax, k = −(comp·u) + sqrt((comp·u)² − |comp|² + aMax²). 0 when it cannot match at all.
        public static double Spare(D3 comp, D3 u, double aMax)
        {
            double cu = D3.Dot(comp, u), disc = cu * cu - comp.Sqr + aMax * aMax;
            return comp.Sqr >= aMax * aMax || disc <= 0 ? 0 : -cu + Math.Sqrt(disc);
        }

        // Pursue's Budget: comp first, then the steering, cut to what the drive has left along it.
        D3 Share(D3 steer, D3 comp)
        {
            double s = steer.Mag;
            return s > 0 ? comp + steer * (Math.Min(s, Spare(comp, steer / s, AMax)) / s) : comp;
        }
    }
}
