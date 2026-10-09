using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // Open System: flight at time compression. The two-body coast used at 1k× and above, the surface-hit search for it,
    // and the substep size of the integrator below that.
    public partial class OpenSystemScene
    {
        // Through 100×, drive-off flight uses the same multi-body/J2 integrator as powered flight. At 1k× and above,
        // eligible coasts use a labelled dominant-body approximation; switching preserves world position/velocity.
        bool Coasting() => warpIdx >= 3 && ship.Landed == null && ship.RcsInput.sqrMagnitude == 0 &&
            (ship.PlanActive ? ship.Throttle == 0 && (ship.Phase == "in orbit" || ship.Phase == "velocity matched" || ship.Phase == "cruising") : ship.ThrottleCmd == 0);

        string FlightModel => ship.Landed != null ? (ship.AssistedArrival ? "assisted arrival" : "body-fixed contact") :
            Coasting() ? "two-body coast (approximation, 1k×+)" : "integrated multi-body gravity + J2";

        string ArrivalNotice => InArrivalBoundary(reference) ?
            Ship.ArrivalPolicy(reference) : null;

        double Coast(double maxDt)
        {
            var dom = Ship.Dominant(sys, ship.Pos, jd);
            var dp = sys.Pos(dom, jd);
            var r0 = ship.Pos - dp; var v0 = ship.Vel - sys.Vel(dom, jd);
            double T = Rails.Period(r0, v0, dom.GM);
            // chunks short enough to notice a sphere-of-influence change or a surface in the way
            double chunk = Math.Min(maxDt, double.IsInfinity(T) ? Math.Max(60, 0.25 * r0.Mag / Math.Max(v0.Mag, 1e-6)) : T / 16);
            // a cruising autopilot must wake up in time for its braking point
            if (ship.PlanActive && ship.Phase == "cruising")
                chunk = Math.Min(chunk, 0.01 * (sys.Pos(ship.Target, jd) - ship.Pos).Mag / Math.Max((ship.Vel - sys.Vel(ship.Target, jd)).Mag, 1e-3));
            chunk = Math.Max(chunk, Math.Min(maxDt, 1));
            // #5: never adopt an unconverged conic; shorter arcs are easier, and failing that the integrator takes over
            D3 r1, v1;
            for (int tries = 0; ; tries++)
            {
                chunk = (jd + chunk / 86400 - jd) * 86400;
                if (chunk <= 0) return 0;
                if (Rails.Propagate(r0, v0, dom.GM, chunk, out r1, out v1)) break;
                if (tries == 20) return 0;
                chunk *= 0.5;
            }
            // #6: stop at the first surface the arc passes through, even if it is outside again by the arc's end
            double hit = FirstSurfaceHit(dom, r0, v0, chunk);
            if (hit >= 0)
            {
                chunk = Math.Max(hit, 1e-3);
                chunk = (jd + chunk / 86400 - jd) * 86400;
                if (!Rails.Propagate(r0, v0, dom.GM, chunk, out r1, out v1)) return 0;
            }
            double jd1 = jd + chunk / 86400;
            ship.Pos = sys.Pos(dom, jd1) + r1;
            ship.Vel = sys.Vel(dom, jd1) + v1;
            ship.Throttle = 0; // nothing fires on a coast arc
            if (!ship.PlanActive) ship.Phase = "coasting";
            ship.Turn(sys, jd1, chunk, dom);
            ship.CheckTouchdown(sys, jd1);
            if (ship.PlanActive && ship.Phase == "cruising") ship.CruiseSeconds += chunk;
            // the autopilot re-checks its plan after each arc and burns again if it has drifted
            if (ship.PlanActive) ship.RecheckPlan(sys, jd1, dom);
            return chunk;
        }

        // The first time within `span` at which the coast arc (r0, v0 about `dom`) meets a surface, or −1. The arc is a
        // conic about `dom`, so that body is found exactly (Rails.FirstCrossing); other nearby bodies move on their
        // own orbits, so the arc is sampled and any chord that enters one is refined by bisection on the distance.
        double FirstSurfaceHit(Body dom, D3 r0, D3 v0, double span)
        {
            // only bodies CheckTouchdown will test (ship.Near), or a found crossing would stop the arc without landing
            double best = dom == sys.Sun || !ship.Near.Contains(dom) ? -1 : Rails.FirstCrossing(r0, v0, dom.GM, InnerRadius(dom), span);
            double limit = best >= 0 ? best : span;
            var vShip = ship.Vel;
            double gDom = dom.GM / r0.Sqr;
            foreach (var b in ship.Near)
            {
                if (b == dom || b == sys.Sun) continue;
                double Rb = InnerRadius(b);
                var rel0 = ship.Pos - sys.Pos(b, jd);
                // can't get there within the arc even with the dominant body's pull added to today's closing speed
                double reach = ((vShip - sys.Vel(b, jd)).Mag + gDom * limit) * limit * 1.5;
                if (rel0.Mag - Rb > reach) continue;
                const int K = 24;
                D3 Rel(double t)
                {
                    Rails.Propagate(r0, v0, dom.GM, t, out var p, out _);
                    double j = jd + t / 86400;
                    return sys.Pos(dom, j) + p - sys.Pos(b, j);
                }
                var a = Rel(0); double ta = 0;
                for (int i = 1; i <= K; i++)
                {
                    double tb = limit * i / K; var c = Rel(tb);
                    // closest point of the chord a→c to the body
                    var d = c - a; double s = d.Sqr > 0 ? Math.Max(0, Math.Min(1, -D3.Dot(a, d) / d.Sqr)) : 0;
                    double tc = ta + s * (tb - ta);
                    if ((a + d * s).Mag < Rb && Rel(tc).Mag < Rb)
                    {
                        double lo = ta, hi = tc;
                        for (int k = 0; k < 60 && hi - lo > 1e-3; k++) { double m = 0.5 * (lo + hi); if (Rel(m).Mag < Rb) hi = m; else lo = m; }
                        limit = hi; best = hi;
                        break;
                    }
                    a = c; ta = tb;
                }
            }
            return best;
        }

        // Strictly inside every point of the touchdown surface, so CheckTouchdown always fires at a found crossing.
        static double InnerRadius(Body b) => b.Radius * Math.Max(b.Shape.x, Math.Max(b.Shape.y, b.Shape.z)) * (1 - b.Flattening) * 0.998;

        double SubstepSize(double remaining)
        {
            // a small fraction of the local orbital period around whatever is closest relative to its pull
            double best = double.MaxValue;
            foreach (var b in sys.Bodies)
            {
                if (b.GM < 1e-3) continue;
                double r = Math.Max((sys.Pos(b, jd) - ship.Pos).Mag, b.Radius);
                best = Math.Min(best, 0.01 * Math.Sqrt(r * r * r / b.GM));
            }
            double h = best;
            if (ship.ThrottleCmd > 0 || ship.PlanActive || ship.RcsInput.sqrMagnitude > 0) h = Math.Min(h, 30);
            // rotating under pilot control at low warp: small steps so the rigid-body dynamics stay smooth
            if (ship.RotInput.sqrMagnitude > 0 || ship.AngVel.sqrMagnitude > 0) h = Math.Min(h, 0.05);
            foreach (var b in ship.Near)
            {
                if (b == sys.Sun) continue;
                var rel = ship.Pos - sys.Pos(b, jd);
                double alt = rel.Mag - b.Radius, closing = -D3.Dot(ship.Vel - sys.Vel(b, jd), rel.Norm);
                if (closing > 0) h = Math.Min(h, Math.Max(0.2 * alt / closing, 0.05));
            }
            if (ship.PlanActive)
            {
                double d = (sys.Pos(ship.Target, jd) - ship.Pos).Mag;
                double v = (ship.Vel - sys.Vel(ship.Target, jd)).Mag + 1e-6;
                h = Math.Max(Math.Min(h, 0.05 * d / v), 0.5);
            }
            return Math.Max(Math.Min(h, remaining), Math.Min(0.02, remaining));
        }
    }
}
