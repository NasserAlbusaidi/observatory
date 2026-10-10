using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Report only (docs/design-combat.md, Phase 5 point 4): what a raider's missiles cost, against the boarding cut, for each
    // Phase 4 fit. The same board sample as GearReport. For each job, fit and approach (nose, starboard, tail), the game's own
    // Strike runs a full magazine (MagazineSize, in salvos of Salvo; none after a cabin hit fails the job, as Volley has it)
    // at the coast speed from rest, on seeded combat draws. The bill: the D8 repair (CanRepair's cost), the water vented at
    // the posting port's price, the pay lost (a hold hit's cut, or a failed passenger job's whole pay), and the boarding cut
    // (BoardShare of what pay is left) when no PDC mount still works (D9: a working mount holds a boarder off). Left out: the
    // standing a failed job costs, the trip time a drive hit adds, the yard days, the fit's own mass (fuel and g every trip),
    // and the free answers (dark, dock first, run). Water aboard at the call is half the stock tank.
    public partial class Career
    {
        public static void HitReport(TextWriter w, double jd0)
        {
            const double fuelAboard = 100;
            const int trials = 32;
            double kms = MissileDvKms;
            var sys = SolarSystem.Build();
            var rows = SampleRaidable(sys, jd0);
            var stock = ShipBuild.Stock();

            ShipBuild With(Slot s, int pick) { var b = stock.Clone(); b.pick[(int)s] = pick; return b; }
            var fits = new List<(string name, ShipBuild build)> { ("no gear", stock) };
            for (int i = 1; i < ShipBuild.Catalog[(int)Slot.PointDefense].Length; i++) fits.Add((ShipBuild.Catalog[(int)Slot.PointDefense][i].Name, With(Slot.PointDefense, i)));
            for (int i = 1; i < ShipBuild.Catalog[(int)Slot.Armor].Length; i++) fits.Add((ShipBuild.Catalog[(int)Slot.Armor][i].Name, With(Slot.Armor, i)));
            var aspects = new[] { ("nose", Vector3.forward), ("starboard", Vector3.right), ("tail", Vector3.back) };

            double meanFuel = 0; foreach (var p in Ports) meanFuel += p.FuelPrice; meanFuel /= Ports.Length;
            w.WriteLine($"HIT REPORT, stock build ({stock.Hull.Name}): {stock[Slot.Drive].Name}, {stock[Slot.Sensors].Name}, {stock[Slot.Radiators].Name}. {MagazineSize} missiles in salvos of {Salvo} at {kms:0} km/s; {fuelAboard:0} t of water aboard; {trials} seeded runs per job, fit and approach; {rows.Count} jobs.");
            w.WriteLine("one hit, by system (D6, D8):");
            w.WriteLine($"  drive     {RepairShare * stock[Slot.Drive].Price,7:N0} cr repair, thrust ×{HitFactor}; the first {MaxHits} hits only");
            w.WriteLine($"  sensors   {RepairShare * stock[Slot.Sensors].Price,7:N0} cr repair, range ×{HitFactor}; the first {MaxHits} hits only");
            w.WriteLine($"  tank      {VentShare * fuelAboard,7:0} t of water ({VentShare * fuelAboard * meanFuel:N0} cr at the ports' mean {meanFuel:N0} cr/t)");
            w.WriteLine($"  hold      {HoldShare:P0} of the cargo job's pay and load");
            w.WriteLine($"  cabin     the passenger job's whole pay, a failure and {StandingLoss} standing; the chase ends");
            w.WriteLine($"  PDC mount       0 cr while another mount works, else {RepairShare * ShipBuild.MountPrice:N0} cr repair; with no mount, the hull");
            w.WriteLine($"  radiators {(stock.Stowable ? (RepairShare * stock[Slot.Radiators].Price).ToString("N0") + " cr repair" : "fixed on the stock build: the hull")}");
            w.WriteLine("weights by approach (drive, tank, hold, cabin, sensors, PDC, radiators): " + string.Join("; ", Array.ConvertAll(new[] { 0, 1, 2 }, i => $"{(Aspect)i} {string.Join("/", HitWeights[i])}")));
            w.WriteLine();

            // sums[fit, aspect, part]: part 0 repair, 1 water, 2 pay, 3 boarding; over all jobs (unweighted, and × P) and the risky ones
            int F = fits.Count, A = aspects.Length;
            var called = new double[F, A, 4]; var perTrip = new double[F, A, 4]; var risky = new double[F, A, 4]; var vesta = new double[F, A, 4];
            var riskyTrip = new double[F, A, 4]; var vestaTrip = new double[F, A, 4];
            int nRisky = 0, nVesta = 0, seed = 1;
            foreach (var r in rows)
            {
                bool isRisky = r.hours >= 0.1, isVesta = r.from == "Vesta";
                if (isRisky) nRisky++;
                if (isVesta) nVesta++;
                string template = JsonUtility.ToJson(r.job);
                for (int f = 0; f < F; f++)
                {
                    var c = new Career { Sys = sys, RouteEvents = false, Build = fits[f].build.Clone() };
                    var ship = new Ship { Fuel = fuelAboard, UnlimitedFuel = false, MaxG = 1, Att = Quaternion.identity };
                    for (int a = 0; a < A; a++)
                    {
                        var from = Frame.FromU(aspects[a].Item2) * FireKm;
                        for (int t = 0; t < trials; t++)
                        {
                            c.Damage = new ShipDamage(); c.Active.Clear(); c.Notes.Clear();
                            var job = JsonUtility.FromJson<Contract>(template);
                            c.Active.Add(job);
                            ship.Fuel = fuelAboard;
                            c.SeedCombat(seed++);
                            for (int fired = 0; fired < MagazineSize && c.Active.Contains(job); fired += Salvo)
                                for (int m = 0; m < Salvo; m++) c.Strike(ship, from, kms);
                            c.CanRepair(out _, out double repair, out _);
                            bool alive = c.Active.Contains(job);
                            double[] part =
                            {
                                repair,
                                (fuelAboard - ship.Fuel) * r.fuelPrice,
                                alive ? r.job.Pay - job.Pay : r.job.Pay,
                                alive && c.WorkingMounts == 0 ? BoardShare * job.Pay : 0,
                            };
                            for (int k = 0; k < 4; k++)
                            {
                                double x = part[k] / trials;
                                called[f, a, k] += x; perTrip[f, a, k] += r.p * x;
                                if (isRisky) { risky[f, a, k] += x; riskyTrip[f, a, k] += r.p * x; }
                                if (isVesta) { vesta[f, a, k] += x; vestaTrip[f, a, k] += r.p * x; }
                            }
                        }
                    }
                }
            }

            double Sum(double[,,] s, int f, int a, int k, int n) => n > 0 ? s[f, a, k] / n : double.NaN;
            double Total(double[,,] s, int f, int a, int n) { double x = 0; for (int k = 0; k < 4; k++) x += Sum(s, f, a, k, n); return x; }
            double Mean3(double[,,] s, int f, int n) { double x = 0; for (int a = 0; a < A; a++) x += Total(s, f, a, n); return x / A; }
            double Part3(double[,,] s, int f, int k, int n) { double x = 0; for (int a = 0; a < A; a++) x += Sum(s, f, a, k, n); return x / A; }
            double Price(int f) { var b = fits[f].build; return b[Slot.PointDefense].Price + b[Slot.Armor].Price; }

            void Table(string label, double[,,] s, int n, bool trip)
            {
                w.WriteLine($"{label} ({n} jobs), the mean of the three approaches; cr:");
                w.WriteLine($"  {"fit",-16} {"repair",7} {"water",7} {"pay",7} {"boarded",8} {"total",8}   {"nose",7} {"stbd",7} {"tail",7}" + (trip ? $"   {"saves",6} {"price",8} {"trips",6}" : ""));
                double none = Mean3(s, 0, n);
                for (int f = 0; f < F; f++)
                {
                    double tot = Mean3(s, f, n), save = none - tot;
                    string line = $"  {fits[f].name,-16} {Part3(s, f, 0, n),7:N0} {Part3(s, f, 1, n),7:N0} {Part3(s, f, 2, n),7:N0} {Part3(s, f, 3, n),8:N0} {tot,8:N0}   {Total(s, f, 0, n),7:N0} {Total(s, f, 1, n),7:N0} {Total(s, f, 2, n),7:N0}";
                    if (trip && f > 0) line += $"   {save,6:N0} {Price(f),8:N0} {(save > 0 ? (Price(f) / save).ToString("N0") : "never"),6}";
                    w.WriteLine(line);
                }
                w.WriteLine();
            }

            Table("the loss when a raider calls, all raidable jobs", called, rows.Count, false);
            Table("the loss when a raider calls, risky jobs (0.1 h or more)", risky, nRisky, false);
            Table("the loss when a raider calls, jobs posted at Vesta", vesta, nVesta, false);
            Table("the loss a trip (× P(call)), all raidable jobs; trips = price / what the fit saves a trip", perTrip, rows.Count, true);
            Table("the loss a trip (× P(call)), risky jobs", riskyTrip, nRisky, true);
            Table("the loss a trip (× P(call)), jobs posted at Vesta", vestaTrip, nVesta, true);
        }
    }
}
