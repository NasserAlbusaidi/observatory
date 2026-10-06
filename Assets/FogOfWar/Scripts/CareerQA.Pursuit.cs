using System;
using System.Collections;
using System.Linq;
using FogOfWar.OpenWorld;

namespace FogOfWar
{
    // 13. Pursuit (Pursuit.cs, Ship Plan.Pursue, docs/design-world.md W4): the fast subset of the pursuit check
    // (OpenWorld/PursuitCheck.cs) at h = 5 s on a system of its own: a pirate catches and holds a coasting ship and one
    // parked at Pallas, a 2 g pirate catches a steady 1 g burn without flying through it, one of the same g closes only on the
    // autopilot's Budget reserve and not at all on a hand burn at its g, and the stand-in's velocity is exact. No scene
    // and no waits; a throw is a failed check, since Start() has no guard.
    public partial class CareerQA
    {
        IEnumerator PursuitChecks()
        {
            try
            {
                var qs = SolarSystem.Build();
                var s1 = PursuitCheck.Fly(qs, "S1", 5);
                Check(s1.Holds && s1.Stale == 0,
                    $"pursuit: a 1 g pirate 100,000 km off a coasting ship is alongside at {s1.FirstAlong:F0} s and holds {s1.Longest:F0} s (>= {Threat.BoardingSeconds:F0}), {s1.Breaks} breaks, {s1.Stale} stale reads");
                var s4 = PursuitCheck.Fly(qs, "S4", 5);
                Check(s4.Holds && s4.Stale == 0,
                    $"pursuit: a 1 g pirate 50,000 km over Pallas' pole is alongside a ship coasting in orbit there at {s4.FirstAlong:F0} s and holds {s4.Longest:F0} s (>= {Threat.BoardingSeconds:F0}), {s4.Breaks} breaks, {s4.Stale} stale reads");
                var s2b = PursuitCheck.Fly(qs, "S2b", 5);
                Check(s2b.Caught && s2b.MinRange >= s2b.Rs && s2b.Holds,
                    $"pursuit: a 2 g pirate 50,000 km behind a 1 g burn holds alongside from {s2b.Capture:F0} s (closed form {s2b.ClosedForm:F0} s, gate {PursuitCheck.GateCatch} x) for {s2b.Longest:F0} s and is never nearer than {s2b.MinRange:F2} km (>= {s2b.Rs} km)");
                var s2a = PursuitCheck.Fly(qs, "S2a", 5);
                Check(s2a.RangeChange < PursuitCheck.GateRange,
                    $"pursuit: a 1 g pirate behind a 1 g autopilot burn closes only on Budget's reserve (about 4e-4 g on this leg): {s2a.Range0:N0} -> {s2a.EndRange:N0} km in 6 h ({s2a.RangeChange * 100:F2} %, < {PursuitCheck.GateRange * 100:F0} %)");
                var s2h = PursuitCheck.Fly(qs, "S2h", 5);
                Check(s2h.RangeChange < PursuitCheck.GateHand,
                    $"pursuit: a 1 g pirate behind a 1 g hand burn has no spare and cannot close: {s2h.Range0:N1} -> {s2h.EndRange:N1} km in 6 h ({s2h.RangeChange * 100:F4} %, < {PursuitCheck.GateHand * 100:F1} %)");
                double err = Math.Max(PursuitCheck.Probe(qs), new[] { s1, s4, s2b, s2a, s2h }.Max(r => r.ErrVel));
                Check(err < PursuitCheck.GateVel, $"pursuit: the player's stand-in carries its velocity to {err:E1} km/s (< {PursuitCheck.GateVel:E0})");
            }
            catch (Exception e) { Check(false, "pursuit: threw " + e.GetType().Name + ": " + e.Message); }
            yield break;
        }
    }
}
