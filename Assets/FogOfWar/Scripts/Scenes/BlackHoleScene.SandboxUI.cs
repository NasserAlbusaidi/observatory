using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // The black hole sandbox's panel: the narration row, the fixed set of readouts for the throw and the probes, and the
    // controls that throw things in, drop probes and set the clock.
    public partial class BlackHoleScene
    {
        // One line on what the newest throw is doing now, in real seconds of play where that helps (audit #63).
        string Narration(out Tone tone)
        {
            tone = Tone.Normal;
            if (noticeShort != null) { tone = Tone.Hot; return noticeShort; }
            Body b = bodies.Count > 0 ? bodies[bodies.Count - 1] : null;
            double perSec = Math.Pow(10, logWarp);
            if (b == null) return probes.Count > 0 ? "probe on its orbit" : "throw something in";
            if (b.state == 0) return b.g.r > b.rt * 1.5 ? "falling in" : "stretched by the tide";
            if (b.state == 2) { tone = Tone.Cold; return "swallowed whole"; }
            if (b.state == 3) return "flew past intact";
            if (!disk && FlareDisk(out _, out _, out _)) { tone = Tone.Hot; return "flare: " + Fmt.Num(lum, 2) + " × Eddington"; }
            if (b.rp <= Kerr.MarginallyBound(A)) { tone = Tone.Cold; return "all debris swallowed"; }
            if (b.dE > 0)
            {
                double back = b.tDisrupt + 2 * Math.PI * Math.Pow(1 / (2 * b.dE), 1.5) - simT;
                if (back > 0)
                {
                    // real seconds left, allowing for the warp ramp still to come
                    double rate = warpTarget > 0 && autoWarp ? Math.Pow(10, warpTarget) : perSec;
                    return (warpHold > 0 ? "torn apart; " : "debris out; ") + "back in ~" + Fmt.Num(Math.Max(back / rate, 1), 0) + " s";
                }
            }
            return lum > 1e-3 ? "debris returning" : "debris on its way";
        }

        // ------------------------------------------------------------------------------------------ readouts and controls

        // The sandbox readouts keep a fixed set of rows with short values, so the controls below them never move while
        // the numbers change (QA-040). "—" fills a row that has nothing to show yet.
        void SandboxReadouts(List<Readout> list)
        {
            var s = Specs[throwKind];
            TidalScales(s, out double rt, out double Rm);
            double rh = Kerr.Horizon(A), rmb = Kerr.MarginallyBound(A);
            const string none = "—";
            if (!s.cloud)
            {
                // the fate depends on the closest approach too: r_p inside r_mb plunges, r_p inside r_t tears (audit #52a)
                double rpM = periRs * 2;
                string fate = rpM < rmb ? (rt > rpM && rt > rh ? "torn, all swallowed" : "swallowed whole")
                            : rt > rpM ? "shredded" : "flies past intact";
                list.Add(new Readout("Tidal radius: " + s.name.ToLowerInvariant(), (rt / 2 < 1e4 ? (rt / 2).ToString("0.00") : Fmt.Num(rt / 2, 3)) + " rs",
                    rt < rh ? Tone.Cold : rt > periRs * 2 ? Tone.Bad : Tone.Normal));
                list.Add(new Readout("Its fate at this pass", fate, rt < rh ? Tone.Cold : rt > periRs * 2 ? Tone.Bad : Tone.Normal));
                // Hills masses: above the first all the debris is swallowed (r_t < r_mb), above the second the star itself
                double hills = Math.Pow(s.radius * Phys.c * Phys.c / (rmb * Phys.G), 1.5) / Math.Sqrt(s.mass) / Phys.Msun;
                double intact = Math.Pow(s.radius * Phys.c * Phys.c / (rh * Phys.G), 1.5) / Math.Sqrt(s.mass) / Phys.Msun;
                list.Add(new Readout("No flare above", Fmt.Num(hills, 3) + " suns"));
                list.Add(new Readout("Swallowed intact above", Fmt.Num(intact, 3) + " suns"));
                list.Add(new Readout("Its size", Rm / 2 < 0.01 ? Fmt.Num(Rm / 2 * Lunit * 2 / 1000, 3) + " km" : (Rm / 2).ToString("0.000") + " rs"));
            }
            else
            {
                list.Add(new Readout("Tidal radius: gas cloud", "none (no self-gravity)"));
                list.Add(new Readout("Its fate at this pass", "stretched"));
                list.Add(new Readout("No flare above", none));
                list.Add(new Readout("Swallowed intact above", none));
                list.Add(new Readout("Its size", "3 rs (model)"));
            }
            bool debris = nParts > 0 || lightCurve.Count > 0;
            list.Add(new Readout("Debris coming back", debris ? Fmt.Num(fallbackRate / Tunit * Phys.yr / Phys.Msun, 3) + " suns/yr" : none));
            list.Add(new Readout("Flare luminosity", debris ? Fmt.Num(lum, 3) + " × Eddington" : none, lum > 1 ? Tone.Hot : Tone.Normal));
            list.Add(new Readout("First debris returns after", lastDE > 0 ? Fmt.Duration(2 * Math.PI * Math.Pow(1 / (2 * lastDE), 1.5) * Tunit) : none));
            list.Add(new Readout("Swallowed so far", debris ? Fmt.Num(accreted / Phys.Msun, 3) + " suns" : none));
            list.Add(new Readout("What is happening", Narration(out var tone), tone));
            list.Add(new Readout("Sim clock", Fmt.Duration(simT * Tunit) + (paused ? " (paused)" : "")));
            list.Add(new Readout("1 s of play is", Fmt.Duration(Math.Pow(10, logWarp) * Tunit)));
            ProbeSummary(list, probes.Count > 0 ? probes[probes.Count - 1] : null);
        }

        void ProbeSummary(List<Readout> list, Probe p)
        {
            const string none = "—";
            list.Add(new Readout("Probe", p != null ? p.name : none));
            // light still circling until its escape or fall is recorded; then that event shows at once (BH audit #36)
            string status = p == null ? none : p.photon && p.turnsEnd < 0 && p.g.alive && !p.g.inside ? "circling" : p.status;
            list.Add(new Readout("Probe status", status));
            if (p != null && p.photon) list.Add(new Readout("Light: turns", (p.turnsEnd >= 0 ? p.turnsEnd : p.g.phi / (2 * Math.PI)).ToString("0.0")));
            else list.Add(new Readout("Probe distance", p != null ? (p.g.r / 2).ToString("0.00") + " rs" : none));
            double dphi = 0;
            bool shift = p != null && !p.photon && p.Shift(out dphi);
            list.Add(new Readout("Perihelion shift per orbit", shift ? (dphi * 180 / Math.PI).ToString("0.0") + "°" : none, Tone.Hot));
            string weak = none;
            if (p != null && p.name == ProbeNames[1])
            {
                // weak field for the same ellipse (semi-latus rectum p = 2 rp ra / (rp + ra)): Einstein's 6πM/p, minus the
                // prograde Lense–Thirring term 8πa M^{3/2}/p^{3/2}
                double pl = 2 * 14.0 * 60 / 74;
                weak = ((6 * Math.PI / pl - 8 * Math.PI * A / Math.Pow(pl, 1.5)) * 180 / Math.PI).ToString("0.0") + "°";
            }
            list.Add(new Readout(A > 0.001 ? "Weak field (+ Lense–Thirring)" : "Weak-field formula", weak));
        }

        void ProbeReadouts(List<Readout> list)
        {
            var p = riding; var g = p.g;
            double a = A;
            const string none = "—";
            list.Add(new Readout("Camera", "rides the probe"));
            list.Add(new Readout("Probe", p.name));
            list.Add(new Readout("Probe status", p.status, g.inside ? Tone.Bad : Tone.Normal));
            list.Add(new Readout("Distance", (g.r / 2).ToString("0.000") + " rs"));
            double tau = (g.tau - p.tau0) * Tunit, t = (Math.Min(simT, g.t) - p.t0) * Tunit;
            list.Add(new Readout(g.inside ? "Probe's clock (display speed)" : "Probe's clock", Fmt.Duration(tau)));
            list.Add(new Readout("Far-away clock", g.inside ? "never sees it cross" : Fmt.Duration(t)));
            string rate = none, speed = none, beacon = none, tide = none, left = none;
            Tone rateT = Tone.Normal, beaconT = Tone.Normal, tideT = Tone.Normal;
            if (!g.inside)
            {
                g.FourVelocity(a, out double ut, out double ur, out double uph);
                rate = (1 / ut).ToString("0.0000") + "×"; if (1 / ut < 0.5) rateT = Tone.Hot;
                g.ZamoVelocity(a, out double vr, out double vph);
                speed = Math.Sqrt(vr * vr + vph * vph).ToString("0.000") + " c";
                // its beacon, sent straight out, as received far away (b = 0, outgoing)
                double Pp = g.r * g.r + a * a, Dl = Geo.Delta(g.r, a);
                double prOut = Math.Sqrt(Math.Max(Pp * Pp - Dl * a * a, 0)) / Dl;
                double gb = 1 / (ut - prOut * ur);
                if (gb > 0) { beacon = "×" + gb.ToString("0.000") + " frequency"; if (gb < 0.5) beaconT = Tone.Hot; }
                // radial tidal stretch in the probe's own frame (Marck 1983, spin 0): (2M/r³)(1 + 3L²/2r²) across 2 m
                double tidal = 4 / (g.r * g.r * g.r) * (1 + 1.5 * g.L * g.L / (g.r * g.r)) * Phys.c * Phys.c / (Lunit * Lunit) / Phys.g;
                tide = Fmt.Num(tidal) + " g"; if (tidal > 10) tideT = Tone.Bad;
            }
            else if (a < 0.01) left = g.alive ? Fmt.Duration(TauToSingularity(g.r, g.E, g.L) * Tunit) : "0 (r = 0)";
            else left = "inner horizon not modelled";
            list.Add(new Readout("Probe clock rate", rate, rateT));
            list.Add(new Readout("Speed past local ZAMOs", speed));
            list.Add(new Readout("Its beacon seen far away", beacon, beaconT));
            list.Add(new Readout(a > 0.001 ? "Tide across 2 m (spin-0 formula)" : "Tide across a 2 m body", tide, tideT));
            list.Add(new Readout("Time left to r = 0", left));
            list.Add(new Readout("Perihelion shift per orbit", p.Shift(out double dphi) ? (dphi * 180 / Math.PI).ToString("0.0") + "°" : none, Tone.Hot));
            list.Add(new Readout("Camera limits", "≤ 0.99 c, 1.03 r₊"));
            list.Add(new Readout("Camera exposure", "÷ (γ(1+v))³"));
        }

        // Proper time from r to r = 0 inside a Schwarzschild hole for a geodesic with energy E and angular momentum L:
        // τ = ∫₀^r dr′ / √(E² − (1 − 2/r′)(1 + L²/r′²)), 32-point Gauss–Legendre.
        static double[] glx, glw;
        static double TauToSingularity(double r, double E, double L)
        {
            if (glx == null) GaussLegendre(32, out glx, out glw);
            double sum = 0;
            for (int i = 0; i < glx.Length; i++)
            {
                double x = 0.5 * r * (glx[i] + 1);
                double v = E * E - (1 - 2 / x) * (1 + L * L / (x * x));
                if (v > 0) sum += glw[i] / Math.Sqrt(v);
            }
            return 0.5 * r * sum;
        }

        static void GaussLegendre(int n, out double[] x, out double[] w)
        {
            x = new double[n]; w = new double[n];
            for (int i = 0; i < n; i++)
            {
                double z = Math.Cos(Math.PI * (i + 0.75) / (n + 0.5)), dp = 1;
                for (int it = 0; it < 100; it++)
                {
                    double p0 = 1, p1 = 0;
                    for (int j = 1; j <= n; j++) { double p2 = p1; p1 = p0; p0 = ((2 * j - 1) * z * p1 - (j - 1) * p2) / j; }
                    dp = n * (z * p0 - p1) / (z * z - 1);
                    double dz = p0 / dp;
                    z -= dz;
                    if (Math.Abs(dz) < 1e-15) break;
                }
                x[i] = z; w[i] = 2 / ((1 - z * z) * dp * dp);
            }
        }

        void SandboxControls(PanelUI ui)
        {
            ui.Section("Sandbox: throw something in");
            throwKind = ui.Choice(null, throwKind, SpecNames);
            periRs = ui.Slider("Closest approach", periRs, 1, 60, v => v.ToString("0.0") + " rs", 0.1f);
            if (ui.Buttons("Throw it in", "Clear sandbox") is int t && t >= 0) { if (t == 0) Throw(throwKind); else ClearSandbox(); }
            ui.Section("Sandbox: probes and light");
            int k = ui.Buttons(ProbeNames);
            if (k >= 0) DropProbe(k);
            follow = ui.Toggle("Camera follows the debris", follow);
            bool r = ui.Toggle("Ride along with the probe", ride);
            if (r != ride)
            {
                ride = r;
                Probe last = null;
                for (int i = probes.Count - 1; i >= 0; i--) if (!probes[i].photon && probes[i].g.alive) { last = probes[i]; break; }
                if (ride && last != null) StartRide(last); else if (!ride) StopRide();
            }
            // the slider rounds to its step every frame: compare with the rounded value, so only a real move takes the
            // clock over from the automatic warp (BH audit #33)
            autoWarp = ui.Toggle("Automatic time warp after a break-up", autoWarp);
            float shownWarp = Mathf.Round(logWarp / 0.05f) * 0.05f;
            float lw = ui.Slider(autoWarp ? "Time warp" : "Time warp (manual)", shownWarp, 0, 6, v => "1 s = " + Fmt.Duration(Math.Pow(10, v) * Tunit), 0.05f);
            if (Mathf.Abs(lw - shownWarp) > 1e-4f) { logWarp = lw; autoWarp = false; }
            int tc = ui.Buttons(paused ? "Resume" : "Pause", "Step 1/30 s");
            if (tc == 0) paused = !paused;
            else if (tc == 1) { paused = true; stepPending = true; }
            guides = ui.Toggle("Guide rings (ISCO, photon orbit, tidal radius)", guides);
            showMap = ui.Toggle("Map and light curve", showMap);
            GUILayout.Label("Model, not real: gas spreads in 12 orbits (real viscous time is far longer); debris drawn ≤ 1 M wide, energy spread uses the real star; bodies drawn at least 0.06 M, probes 0.45 M; gas cloud 3 rs wide (real G2-like clouds are thousands of rs); everything in the equatorial plane; the probe's clock inside the horizon runs at display speed.", ui.note);
            if (notice != null) GUILayout.Label(notice, ui.note);   // last, so nothing above it moves
        }
    }
}
