using System;
using System.Collections.Generic;
using System.IO;

namespace FogOfWar.OpenWorld
{
    // Report only (docs/design-combat.md, Phase 5 point 4, PR 5-0): does the Phase 4 gear pay for itself? Every port's real
    // board (Tick on a ship docked there) on four days at each of five dates a fifth of a year apart. For each raidable job
    // (cargo, passengers, survey; RaidJob) the planner's own risk (Threat.Of) at a full burn of the stock build with the job
    // aboard: P(call) = 1 − e^(−RaidsPerHour · hours), as RaidChance has it. The loss is the raider's cut when the player stays
    // and is boarded (BoardShare of the pay; Take, grade 1 as posted) or gives up (GiveUpShare). A PDC fit stops a boarding
    // while one working mount bears (D9), so a fit's break-even is its price over the boarding loss it saves a trip. The free
    // answers (dark, dock first, run) and the repair bill of the hits that still land are left out: the hit table below gives
    // the hits a full magazine lands, for the reader to price.
    public partial class Career
    {
        public static void GearReport(TextWriter w, double jd0)
        {
            var sys = SolarSystem.Build();
            var b = ShipBuild.Stock();
            double dry = b.DryMass, tank = b.TankCapacity, thrust = b.Thrust;
            w.WriteLine($"GEAR REPORT, stock build: dry {dry:N1} t, tank {tank:N0} t, {thrust:0.0} MN; full {b.GFull:0.00} g. Full burn with the job aboard (passengers at {PassengerG} g).");
            w.WriteLine($"RaidsPerHour {RaidsPerHour}, BoardShare {BoardShare}, GiveUpShare {GiveUpShare}; dates jd {jd0} + 0, 73, 146, 219, 292 days, 4 boards each");
            w.WriteLine();

            var rows = new List<(string from, JobKind kind, double hours, double p, double pay)>();
            var seen = new HashSet<string>();
            foreach (var port in Ports)
            {
                var home = sys[port.Body];
                if (home == null) continue;
                for (int k = 0; k < 5; k++)
                    for (int day = 0; day < 4; day++)
                    {
                        double jd = jd0 + 73 * k + day;
                        var c = new Career { Sys = sys, RouteEvents = false };
                        var ship = new Ship { Pos = sys.Pos(home, jd), Vel = sys.Vel(home, jd), MaxG = 1, Fuel = 200, UnlimitedFuel = false, Landed = home };
                        c.Tick(sys, ship, jd, 0);
                        foreach (var j in c.Board)
                        {
                            if (j.Kind != JobKind.Cargo && j.Kind != JobKind.Passengers && j.Kind != JobKind.Survey) continue;
                            if (!seen.Add(j.Key)) continue;
                            var to = c.TargetOf(j);
                            if (to == null || to == home) continue;
                            double d = Math.Max((sys.Pos(to, jd) - sys.Pos(home, jd)).Mag - Ship.Standoff(to), 0);
                            double load = j.Kind == JobKind.Cargo ? j.Tonnes : j.People * PassengerTonnes;
                            double a = Math.Min(thrust / (dry + tank + load), j.Kind == JobKind.Passengers ? PassengerG * Ship.G0 : double.MaxValue);
                            double hours = Threat.Of(sys, home, D3.Zero, to, jd, d, a).Hours;
                            rows.Add((port.Name, j.Kind, hours, 1 - Math.Exp(-RaidsPerHour * hours), j.Pay));
                        }
                    }
            }

            double Q(List<double> xs, double q) { if (xs.Count == 0) return double.NaN; xs.Sort(); return xs[(int)Math.Min(xs.Count - 1, Math.Floor(q * xs.Count))]; }
            double Mean(List<double> xs) { double s = 0; foreach (var x in xs) s += x; return xs.Count > 0 ? s / xs.Count : double.NaN; }
            var fits = new[] { (2, 2 * ShipBuild.MountPrice), (4, 4 * ShipBuild.MountPrice), (6, 6 * ShipBuild.MountPrice) };

            void Block(string label, Predicate<(string from, JobKind kind, double hours, double p, double pay)> keep)
            {
                var sel = rows.FindAll(keep);
                var p = sel.ConvertAll(r => r.p);
                var board = sel.ConvertAll(r => r.p * BoardShare * r.pay);
                var give = sel.ConvertAll(r => r.p * GiveUpShare * r.pay);
                var pay = sel.ConvertAll(r => r.pay);
                int risky = sel.FindAll(r => r.hours >= 0.1).Count;
                double eb = Mean(board);
                w.WriteLine($"{label}: {sel.Count} jobs, {risky} with risk (0.1 h or more)");
                if (sel.Count == 0) { w.WriteLine(); return; }
                w.WriteLine($"  P(call)          mean {Mean(p):P1}, median {Q(p, 0.5):P1}, p90 {Q(p, 0.9):P1}, max {Q(p, 1):P1}");
                w.WriteLine($"  pay              mean {Mean(pay):N0} cr, median {Q(pay, 0.5):N0} cr");
                w.WriteLine($"  E[loss] a trip   boarded: mean {eb:N0} cr, median {Q(board, 0.5):N0}, p90 {Q(board, 0.9):N0}, max {Q(board, 1):N0}; gave up: mean {Mean(give):N0} cr");
                var parts = new List<string>();
                foreach (var (n, price) in fits) parts.Add($"{n} mounts ({price:N0} cr) {(eb > 0 ? (price / eb).ToString("N0") : "never")}");
                w.WriteLine($"  trips to pay a PDC fit from the boarding loss it saves (mean): {string.Join("; ", parts)}");
                w.WriteLine();
            }

            Block("all raidable jobs", r => true);
            Block("risky jobs only (0.1 h or more)", r => r.hours >= 0.1);
            foreach (var kind in new[] { JobKind.Cargo, JobKind.Passengers, JobKind.Survey }) Block(kind.ToString().ToLowerInvariant(), r => r.kind == kind);

            w.WriteLine("by port of posting (all raidable jobs): jobs, mean P(call), mean E[loss] boarded a trip, trips to pay 2 mounts");
            foreach (var port in Ports)
            {
                var sel = rows.FindAll(r => r.from == port.Name);
                if (sel.Count == 0) continue;
                double eb = Mean(sel.ConvertAll(r => r.p * BoardShare * r.pay));
                w.WriteLine($"  {port.Name,-14} {sel.Count,4}  {Mean(sel.ConvertAll(r => r.p)),7:P1}  {eb,9:N0} cr  {(eb > 0 ? (2 * ShipBuild.MountPrice / eb).ToString("N0") : "never"),8}");
            }
            w.WriteLine();

            // a full magazine's hits: MagazineSize × (1 − KillChance(m, V)) × (1 − armor stop), V the impact speed
            w.WriteLine($"hits from a full magazine ({MagazineSize} missiles), by mounts bearing and plating; a raider at rest hits at {MissileDvKms:0} km/s, one closing at 5 km/s at 15:");
            var armors = new[] { (0.0, "none"), (ShipBuild.Catalog[(int)Slot.Armor][1].Size, "light"), (ShipBuild.Catalog[(int)Slot.Armor][2].Size, "heavy") };
            foreach (double v in new[] { MissileDvKms, MissileDvKms + 5 })
            {
                w.WriteLine($"  at {v:0} km/s       none   light  heavy   P(kill)");
                for (int m = 0; m <= 3; m++)
                {
                    double pk = PointDefense.KillChance(m, v), through = MagazineSize * (1 - pk);
                    var cells = new List<string>();
                    foreach (var (stop, _) in armors) cells.Add($"{through * (1 - stop),5:0.00}");
                    w.WriteLine($"  {m} bearing     {string.Join("  ", cells)}   {pk:P0}");
                }
            }
        }
    }
}
