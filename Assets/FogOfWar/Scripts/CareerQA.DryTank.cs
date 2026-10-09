using System;
using System.Collections.Generic;
using System.Reflection;
using FogOfWar.OpenWorld;

namespace FogOfWar
{
    // #151, #152: a dry tank at a port. The tender is not offered there, so the panel line and the note point to the
    // port's credit. A fresh career has no dry edge yet, as after a load, so its first update with a dry tank is the edge.
    public partial class CareerQA
    {
        void DryTankChecks()
        {
            Guard("dry line", () =>
            {
                var line = typeof(OpenSystemScene).GetMethod("DryLine", BindingFlags.Static | BindingFlags.NonPublic);
                Check(line != null, "#151: OpenSystemScene has a static DryLine(bool docked)");
                if (line == null) return;
                string atPort = (string)line.Invoke(null, new object[] { true }), outThere = (string)line.Invoke(null, new object[] { false });
                Check(atPort.Contains("on credit") && !atPort.Contains("adrift") && outThere.Contains("adrift"),
                    $"#151: the dry-tank line at a port offers credit (\"{atPort}\"), out there it says adrift (\"{outThere}\")");
            });

            SolarSystem qs = null;
            if (!Guard("dry tank system", () => qs = SolarSystem.Build())) return;
            double t0 = PursuitCheck.Jd0;

            List<(Career.Happening What, string Text)> DryTick(D3 pos, D3 vel, out Career dc)
            {
                var said = new List<(Career.Happening, string)>();
                var c = new Career { Sys = qs, RouteEvents = false };
                c.OnEvent += (w, x) => said.Add((w, x));
                var dryShip = new Ship { Pos = pos, Vel = vel, MaxG = 1, Fuel = 0, UnlimitedFuel = false };
                c.Tick(qs, dryShip, t0, 0);
                dc = c;
                return said;
            }

            Guard("dry tank at a port", () =>
            {
                var moon = qs.Moon;
                double r = Ship.Standoff(moon);
                var said = DryTick(qs.Pos(moon, t0) + new D3(r, 0, 0), qs.Vel(moon, t0) + new D3(0, Math.Sqrt(moon.GM / r), 0), out var dc);
                bool credit = said.Exists(e => e.What == Career.Happening.TankDry && e.Text.Contains("on credit"));
                bool tender = said.Exists(e => e.What == Career.Happening.TankDry && e.Text.Contains("emergency tender"));
                Check(dc.Docked?.Name == "Luna" && credit && !tender,
                    $"#152: dry at {dc.Docked?.Name ?? "no port"} on the first update, the note offers the port's credit, not the tender ({string.Join(" | ", said.ConvertAll(e => e.Text))})");
            });

            Guard("dry tank out there", () =>
            {
                var vesta = qs["Vesta"];
                var pV = qs.Pos(vesta, t0);
                var said = DryTick(pV + pV.Norm * 1e6, qs.Vel(vesta, t0), out var dc);
                Check(dc.Docked == null && said.Exists(e => e.What == Career.Happening.TankDry && e.Text.Contains("emergency tender")),
                    $"#152: dry out there, the note offers the emergency tender ({dc.Docked?.Name ?? "not docked"}: {string.Join(" | ", said.ConvertAll(e => e.Text))})");
            });
        }
    }
}
