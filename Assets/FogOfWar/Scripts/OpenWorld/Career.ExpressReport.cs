using System;
using System.Collections.Generic;
using System.IO;

namespace FogOfWar.OpenWorld
{
    // Report only (decision 2, 2026-10-08: ExpressMargin stays 15 %): how many cargo and passenger jobs the stock ship can
    // fly inside the express window. Every port pair on five dates a fifth of a year apart, each load a board can post
    // (cargo 10–90 t, passengers 2–40), from the start mass with a full tank. A full burn the tank can't hold flies the
    // largest coast it can. The window, not the deadline, is the limit: the window is 2.53·√(d/g) + 8 h and the deadline at
    // least 2.58·√(d/g) + 12 h, so the board's slack does not change the count. Loaded, the stock ship starts under 1 g,
    // so the pilot's drive limit (the scene's 1 g, or TopG) does not change it either.
    public partial class Career
    {
        public static void ExpressReport(TextWriter w, double jd0)
        {
            var sys = SolarSystem.Build();
            var b = ShipBuild.Stock();
            double dry = b.DryMass, tank = b.TankCapacity, thrust = b.Thrust;
            w.WriteLine($"EXPRESS RATE, stock build: dry {dry:N1} t, tank {tank:N0} t, hold {b.HoldTonnes:N0} t, {b.Seats} seats, {thrust:0.0} MN; full {b.GFull:0.00} g, empty {b.GEmpty:0.00} g");
            w.WriteLine($"ExpressMargin {ExpressMargin}, ExpressG {ExpressG} g; dates jd {jd0} + 0, 73, 146, 219, 292 days");
            w.WriteLine();

            var misses = new List<(double ratio, string what)>();
            var bands = new[] { (0.1, "under 0.1 AU"), (1.0, "0.1-1 AU"), (5.0, "1-5 AU"), (double.MaxValue, "over 5 AU") };
            foreach (var (kind, label) in new[] { (JobKind.Cargo, "cargo"), (JobKind.Passengers, "passengers") })
                for (int band = -1; band < bands.Length; band++)
                {
                    int n = 0, express = 0, coast = 0, coastExpress = 0;
                    double worst = 0, lo = band <= 0 ? 0 : bands[band - 1].Item1, hi = band < 0 ? double.MaxValue : bands[band].Item1;
                    for (int k = 0; k < 5; k++)
                    {
                        double jd = jd0 + 73 * k;
                        foreach (var from in Ports) foreach (var to in Ports)
                        {
                            if (from == to || sys[from.Body] == null || sys[to.Body] == null) continue;
                            double d = (sys.Pos(sys[to.Body], jd) - sys.Pos(sys[from.Body], jd)).Mag;
                            if (d / Frame.AU < lo || d / Frame.AU >= hi) continue;
                            double window = ExpressDaysFor(d, 0, double.MaxValue) * 86400;
                            int loads = kind == JobKind.Cargo ? 9 : 39;
                            for (int i = 0; i < loads; i++)
                            {
                                double load = kind == JobKind.Cargo ? 10 + 10 * i : (2 + i) * PassengerTonnes;
                                double m0 = dry + tank + load;
                                double a = Math.Min(thrust / m0, kind == JobKind.Passengers ? PassengerG * Ship.G0 : double.MaxValue);
                                Trip(d, a, 1, m0, out double s, out _, out double tonnes);
                                bool coasts = tonnes > tank;
                                if (coasts)
                                {
                                    double dvMax = Ship.Ve * Math.Log(m0 / (m0 - tank));
                                    Trip(d, a, dvMax / (2 * Math.Sqrt(a * d)), m0, out s, out _, out _);
                                }
                                n++;
                                bool ok = s <= window;
                                if (ok) express++;
                                if (coasts) { coast++; if (ok) coastExpress++; }
                                worst = Math.Max(worst, s / window);
                                if (!ok && band < 0) misses.Add((s / window, $"{label} {from.Name} -> {to.Name}, {load:0.#} t, day {73 * k}: {s / 3600:F1} h against {window / 3600:F1} h{(coasts ? " (tank-limited coast)" : "")}"));
                            }
                        }
                    }
                    if (n > 0) w.WriteLine($"{label,-10} {(band < 0 ? "all" : bands[band].Item2),-12}: {express}/{n} express ({100.0 * express / n:F1} %); tank-limited {coast} ({coastExpress} of them express); worst trip/window {worst:F3}");
                }
            misses.Sort((x, y) => y.ratio.CompareTo(x.ratio));
            w.WriteLine();
            w.WriteLine($"misses: {misses.Count}; the worst 12:");
            for (int i = 0; i < Math.Min(12, misses.Count); i++) w.WriteLine($"  {misses[i].ratio:F3}  {misses[i].what}");
        }
    }
}
