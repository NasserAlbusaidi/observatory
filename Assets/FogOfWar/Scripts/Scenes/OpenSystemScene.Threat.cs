using System;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // Threat in the scene (Threat.cs, docs/design-world.md W3): the trip planner's risk line, and rings for the patrols'
    // reach, the raider havens and the main belt.
    public partial class OpenSystemScene
    {
        string riskLine, riskKey;
        float riskAt;
        Threat.Risk risk;
        int patrolRings, havenRings, beltRings;

        // The line for the target at the planner's cruise, worked out at most twice a second: it costs about a millisecond,
        // and the planners draw every frame.
        string RiskLine()
        {
            var t = ship.Target;
            if (t == null) return null;
            string key = t.Name + "|" + cruiseIdx + "|" + ship.AMax.ToString("R") + "|" + ship.PlanActive;
            if (key == riskKey && Time.unscaledTime - riskAt < 0.5f) return riskLine;
            riskKey = key; riskAt = Time.unscaledTime;
            var from = reference ?? sys.Sun;
            double d = Math.Max((sys.Pos(t, jd) - ship.Pos).Mag - Ship.Standoff(t), 0);
            double f = PlannerFrac(d);
            risk = Threat.Of(sys, from, ship.Pos - sys.Pos(from, jd), t, jd, d, ship.AMax, f < 1 ? f * Math.Sqrt(ship.AMax * d) : 0);
            return riskLine = Threat.Line(risk);
        }

        public string TestRiskLine() { riskKey = null; return RiskLine(); } // checks

        // After the orbits: each patrol's reach in its owner's colour, kept in close views, where it has a size (from the
        // map's opening zoom it is under a pixel); the havens' zones and the main belt's edges in red, fading out in close
        // views like a planet's orbit.
        void AddThreatRings(float fade)
        {
            patrolRings = havenRings = beltRings = 0;
            foreach (var p in Career.Ports)
            {
                var b = sys[p.Body];
                if (p.PatrolG <= 0 || b == null) continue;
                var c = FactionColours[(int)p.Owner]; c.a = 0.55f;
                MapRing(sys.Pos(b, jd), Threat.Reach(p.PatrolG), c);
                patrolRings++;
            }
            if (fade <= 0) return;
            var red = FactionColours[(int)Faction.Raiders];
            red.a = 0.45f * fade;
            foreach (var h in Threat.Havens)
            {
                var b = sys[h];
                if (b == null) continue;
                MapRing(sys.Pos(b, jd), Threat.HavenAu * Frame.AU, red);
                havenRings++;
            }
            red.a = 0.2f * fade;
            MapRing(D3.Zero, Belt.MainInAu * Frame.AU, red);
            MapRing(D3.Zero, Belt.MainOutAu * Frame.AU, red);
            beltRings = 2;
        }

        // a circle parallel to the ecliptic
        void MapRing(D3 c, double r, Color col)
        {
            const int N = 72;
            var prev = Map(c + new D3(r, 0, 0), out _);
            for (int i = 1; i <= N; i++)
            {
                double th = 2 * Math.PI * i / N;
                var q = Map(c + new D3(r * Math.Cos(th), r * Math.Sin(th), 0), out _);
                pts.Add(prev); pts.Add(q); cols.Add(col); cols.Add(col);
                prev = q;
            }
        }
    }
}
