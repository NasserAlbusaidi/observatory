using System;
using System.Collections;
using FogOfWar.OpenWorld;

namespace FogOfWar
{
    // 12. Threat (Threat.cs, docs/design-world.md W3): a patrol's reach follows the doc's table, a belt route shows more
    // risk than a cislunar one, a patrol at the far end or a harder burn lowers it, the Raiders own no port, and the scene
    // shows the planner's risk line and the map's rings.
    public partial class CareerQA
    {
        IEnumerator ThreatChecks()
        {
            var qs = SolarSystem.Build(); // a system of its own: the checks fly nothing
            double j0 = jd;
            double r3 = Threat.Reach(3), r5 = Threat.Reach(5);
            Check(Math.Abs(r3 / 381000 - 1) < 0.01 && Math.Abs(r5 / 635000 - 1) < 0.01,
                $"threat: a patrol's reach in a {Threat.BoardingSeconds / 3600:0} h boarding is {r3:N0} km at 3 g and {r5:N0} km at 5 g (the doc's table: 381,000 and 635,000)");

            Threat.Risk Trip(string from, string to, double g)
            {
                Body a = qs[from], b = qs[to];
                double d = (qs.PosAt(b, j0) - qs.PosAt(a, j0)).Mag - Ship.Standoff(b);
                return Threat.Of(qs, a, D3.Zero, b, j0, d, g * Ship.G0);
            }
            string R(Threat.Risk r) => $"{r.Hours:0.00} h, {r.Tier} ({r.Leaving:0.00} leaving, {r.Arriving:0.00} arriving)";
            var luna = Trip("Earth", "Moon", 1);
            var vesta = Trip("Mars", "Vesta", 1);
            var ceres = Trip("Mars", "Ceres", 1);
            var vesta3 = Trip("Mars", "Vesta", 3);
            Check(luna.Hours == 0 && vesta.Hours > 1 && vesta.Arriving > vesta.Leaving,
                $"threat: a belt route shows more risk than a cislunar one: Mars orbit → Vesta {R(vesta)}, Earth → Luna {R(luna)}");
            Check(ceres.Hours < vesta.Hours / 2, $"threat: a patrol at the far end lowers it: Mars orbit → Ceres (a 2 g patrol) {R(ceres)}");
            Check(vesta3.Hours < vesta.Hours - 1, $"threat: a harder burn lowers it: Mars orbit → Vesta at 3 g {R(vesta3)}");

            var earth = qs.PosAt(qs["Earth"], j0);
            double atHaven = Threat.Presence(qs, qs.PosAt(qs["Pallas"], j0), j0), atEarth = Threat.Presence(qs, earth, j0);
            var guard = Threat.PatrolAt(qs, earth, j0);
            Check(atHaven == 1 && atEarth == 0 && guard?.Name == "Earth orbit",
                $"threat: raiders work at Pallas ({atHaven:0.00}), not at Earth ({atEarth:0.00}), and Earth is in the {guard?.Name ?? "-"} patrol's reach");
            Check(Career.Factions[(int)Faction.Raiders].Name == "Raiders" && !Career.OwnsPort(Faction.Raiders) && Career.OwnsPort(Faction.Compact),
                "threat: the Raiders join the factions and own no port, so no board and no standing line shows them");

            // the scene: the planner's line for a belt target, and the rings on the map
            Call("SetTarget", sys["Vesta"]);
            yield return null;
            string line = s.TestRiskLine();
            Check(line != null && line.StartsWith("Raider risk: ", StringComparison.Ordinal), $"threat: the trip planner, aimed at Vesta, says \"{line}\"");
            Call("ToggleMap");
            yield return null;
            yield return null;
            yield return Shot("c11-threat-map");
            string rep = s.TestMapReport();
            Call("ToggleMap");
            yield return null;
            Check(rep.Contains("rings 4 patrol, 2 haven, 2 belt"), $"threat: the map draws the patrols' reach, the havens' zones and the belt ({rep})");
        }
    }
}
