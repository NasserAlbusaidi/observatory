using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FogOfWar.OpenWorld
{
    // The pursuit check (W4): a pirate on Plan.Pursue (Pursuit.cs) against the player, in the lockstep the scene will run,
    // judged by a rule fixed before the first run. Runtime assembly, so CareerQA runs a fast subset (Fly, Probe); the
    // editor entry is FogOfWarPursuitCheck.
    public static class PursuitCheck
    {
        public const double Jd0 = 2461316.5; // 2026-10-03 TDB, as FogOfWarEphemCheck
        const double SampleS = 60, DomS = 60, SteadyS = 600, PreHoldS = 600;
        // the pre-registered gates (header): G2 range change, G3 catch time over the closed form, G4 stand-in velocity error
        public const double GateRange = 0.02, GateCatch = 1.2, GateVel = 1e-4;
        static readonly double[] SmallHs = { 1.0 / 60, 0.05 }, Hs = { 1, 5 }, Spans = { 30, 50 }, ProbeHs = { 1.0 / 60, 0.05, 1, 5, 30 };

        struct Init { public Ship Player; public D3 PiratePos, PirateVel; public double PirateG, Jd; }

        sealed class Scenario
        {
            public string Id, What;
            public double Duration;
            public Body Centre;   // the body the player is at (pirate altitude, dock time), or null
            public bool TrackDock;
            public double BurnG;  // S5: the player's hand burns once the pirate is alongside (0: none)
            public Func<Init> Make;
        }

        public sealed class Result
        {
            public string Id, What;
            public double H, PirateG, Rs = Pursuit.Radius;
            public Plan Plan;
            public double FirstAlong = -1, Longest, EntryVrel, SteadyVrelMax, SteadyVrelMed = double.NaN, MedRangeAlong = double.NaN;
            public double MinRange = double.MaxValue, MinRangeT, Range0, EndRange, Dv, ErrVel, Gap, UsPirate, ClosedForm = double.NaN;
            public double MinAlt = double.MaxValue, DockT = -1, AlongAtDock, AlongTotalAtDock, CatchT = -1, BurnStart = -1;
            public int Stale, Breaks, Rebounds, Spans, SubSteps, ClimbIn, TurnIn;
            public bool EarlyStop;
            public readonly Dictionary<string, double> Phases = new Dictionary<string, double>();
            public readonly List<double> Minima = new List<double>();
            public string Note = "";
            public bool Holds => Longest >= Threat.BoardingSeconds;
            public double RangeChange => Math.Abs(EndRange - Range0) / Range0;
            public bool Caught => FirstAlong >= 0 && FirstAlong <= GateCatch * ClosedForm;
        }

        static void Echo(TextWriter w, string line)
        {
            if (w == null) return;
            w.WriteLine(line);
            w.Flush();
            Debug.Log("PURSUIT " + line);
        }

        static readonly string[] HeaderText =
        {
            "PURSUIT CHECK (W4, PR 5a). Ship Plan.Pursue flown by Pursuit.cs against a stand-in Body for the player. SolarSystem.Build() at jd 2461316.5 TDB.",
            "Lockstep as the scene will run it. Each frame of h seconds: the player steps first, by the scene's rule (OpenSystemScene.Coasting() true: a copy of",
            "OpenSystemScene.Coast(); else Ship.Step over h rounded as in TickInner), possibly in several steps. Each player step is one span: the harness reads",
            "the stand-in at the span's start through sys.Pos (a read of the old path, as a HUD might make), Pursuit.Span sets the quadratic from the step,",
            "and the pirate steps over the span by Pursuit.Step: h_p = min(span left, 30 s, max(0.05 d/v, 0.5 s)), its Dominant body every 60 s.",
            "Coast() is a private instance method of OpenSystemScene and needs the scene, so it cannot be called here. The copy keeps its chunk rule,",
            "Rails.Propagate with halving, Pos/Vel from the dominant body at the chunk's end, Turn, and a planned ship's 1 ms re-check Step. It leaves out",
            "FirstSurfaceHit and CheckTouchdown (Near is empty) and looks the dominant body up every 60 s, not every chunk.",
            "Stand-in radius Rs = 0.05 km (Pursuit.Radius): the pirate holds 1.05 km off. Pirate from Pursuit's constructor: UnlimitedFuel, CruiseCap 0,",
            "Att facing the player, Obstacles by the scene's rule (Parent != null, Radius >= 100), Near empty.",
            "Measures. alongside = |pirate.Pos - player.Pos| < 500 km AND |pirate.Vel - player.Vel| < 0.1 km/s, from the two Ships at each span end.",
            " hold = the span lengths summed while alongside, reset to 0 on any break; breaks = on-to-off transitions (the run's counter).",
            " minRng = the least distance inside every pirate step: the pirate on the quadratic through its step's ends, the player on the stand-in path.",
            " entryVrel = max |vrel| in the first 600 s of each hold; steady = max/median |vrel| in holds after their first 600 s.",
            " errVel = |sys.Vel(stand-in, j) - (v0 + a t)| at every pirate step start j. stale = sys.Pos(stand-in, j) != its path at j.",
            " Runs with h < 1 s stop once a hold reaches 7800 s (7200 s plus a 600 s steady window).",
            "",
            "PRE-REGISTERED VERDICT RULE (committed before the first run of the scenarios):",
            " G1  S1 and S4 (Pallas) with Pursue hold >= 7200 s continuously in every run: h = 1/60, 0.05, 1, 5 s and spans of 30 and 50 s (12 runs).",
            " G2  S2a with Pursue at h = 1 and 5 s: |range(6 h) - range(0)| / range(0) < 2 %.",
            " G3  S2b with Pursue at h = 0.05, 1 and 5 s: first alongside <= 1.2 x the closed form, and minRng >= Rs over the whole run.",
            "     Closed form: rest-to-rest relative motion along the line of sight, closing at a1 = Ship.Spare(comp0, +los, AMax) and braking at",
            "     a2 = 0.85 Ship.Spare(comp0, -los, AMax); comp0 = the stand-in's acceleration less the pirate's gravity in the first span, los the",
            "     line of sight at t = 0, d = range(0) - standoff: v^2 = 2 d a1 a2 / (a1 + a2), t = v / a1 + v / a2. For a 1 g target and a 2 g",
            "     pirate 50,000 km back on its thrust line: a1 = 1 g, a2 = 2.55 g, t = 3768 s, gate 4521 s.",
            " G4  errVel < 1e-4 km/s in every run and in the probe.",
            " VERDICT pass if G1, G2, G3 and G4 all hold, else fail. Exit code 0 on pass, 1 on fail.",
            " Report only: S4v (Vesta) and S4r (Pallas, in-plane start), S3, S5, and S1 on Plan.Brachistochrone (first alongside against the",
            " spike's 6650 s at h = 1 and 5).",
        };

        static D3 TangentOf(D3 p, D3 v) { var r = p.Norm; return (v - r * D3.Dot(v, r)).Norm; }

        static Quaternion Look(D3 dir)
        {
            var up = Math.Abs(D3.Dot(dir.Norm, Frame.EclNorth)) > 0.99 ? new D3(1, 0, 0) : Frame.EclNorth;
            return Quaternion.LookRotation(Frame.U(dir.Norm), Frame.U(up));
        }

        // The player: the scene's ship defaults for a check (no fuel limit, no cruise cap), the scene's Obstacles rule.
        static Ship Player(SolarSystem sys, D3 pos, D3 vel, D3 facing)
        {
            var s = new Ship { Pos = pos, Vel = vel, MaxG = 1, UnlimitedFuel = true, CruiseCap = 0, Att = Look(facing) };
            foreach (var b in sys.Bodies) if (b.Parent != null && b.Radius >= 100) s.Obstacles.Add(b);
            return s;
        }

        static List<Scenario> Scenarios(SolarSystem sys)
        {
            var vesta = sys["Vesta"]; var mars = sys["Mars"]; var pallas = sys["Pallas"];
            D3 pV = sys.Pos(vesta, Jd0), vV = sys.Vel(vesta, Jd0);
            D3 ur = pV.Norm, ut = TangentOf(pV, vV);
            D3 belt = pV + ur * 1e6;
            var list = new List<Scenario>();

            Init Coasting(double g)
            {
                var pl = Player(sys, belt, vV, ut);
                return new Init { Player = pl, PiratePos = belt - ut * 1e5, PirateVel = vV, PirateG = g, Jd = Jd0 };
            }

            // drive off in a circular orbit at the body's standoff, in its equator; the pirate at rest relative to the body
            Init Parked(Body c, bool overPole)
            {
                D3 pc = sys.Pos(c, Jd0), vc = sys.Vel(c, Jd0), pole = c.Pole;
                var rad = (pc.Norm - pole * D3.Dot(pc.Norm, pole)).Norm;
                var tan = D3.Cross(pole, rad).Norm;
                double r0 = Ship.Standoff(c);
                var pl = Player(sys, pc + rad * r0, vc + tan * Math.Sqrt(c.GM / r0), tan);
                return new Init { Player = pl, PiratePos = pc + (overPole ? pole : rad) * 5e4, PirateVel = vc, PirateG = 1, Jd = Jd0 };
            }

            // under way toward Mars at 1 g: 600 s at h = 5 s first, so the run starts in the accelerate leg
            Init Accelerating(double g)
            {
                double jd = Jd0 - 600.0 / 86400;
                var pl = Player(sys, belt, sys.Vel(vesta, jd), sys.Pos(mars, jd) - belt);
                pl.Target = mars; pl.Plan = Plan.Brachistochrone;
                var dom = Ship.Dominant(sys, pl.Pos, jd);
                for (int i = 0; i < 120; i++)
                {
                    double h = (jd + 5.0 / 86400 - jd) * 86400;
                    pl.Step(sys, jd, h, dom);
                    jd += h / 86400;
                }
                return new Init { Player = pl, PiratePos = pl.Pos - pl.Heading * 5e4, PirateVel = pl.Vel, PirateG = g, Jd = jd };
            }

            // braking into Vesta on its 0.85 g curve from 600,000 km at 100 km/s; the pirate at rest relative to Vesta,
            // `out` km out on the approach line and 20,000 km along-track
            Init Braking(double g, double out_)
            {
                var pl = Player(sys, pV + ur * 6e5, vV - ur * 100, ur);
                pl.Target = vesta; pl.Plan = Plan.Brachistochrone;
                return new Init { Player = pl, PiratePos = pV + ur * out_ + ut * 2e4, PirateVel = vV, PirateG = g, Jd = Jd0 };
            }

            list.Add(new Scenario { Id = "S1", What = "player coasting at Vesta+1e6 km; 1 g pirate 100,000 km behind on the orbit, at relative rest", Duration = 8 * 3600, Make = () => Coasting(1) });
            list.Add(new Scenario { Id = "S4", What = "player parked at Pallas (not a port; 769.5 km, drive off); 1 g pirate 50,000 km out along Pallas' pole", Duration = 10 * 3600, Centre = pallas, Make = () => Parked(pallas, true) });
            list.Add(new Scenario { Id = "S4v", What = "as S4 at Vesta (a port; 788.1 km)", Duration = 10 * 3600, Centre = vesta, Make = () => Parked(vesta, true) });
            list.Add(new Scenario { Id = "S4r", What = "as S4 with the pirate on the Pallas->player line, in the orbit plane", Duration = 10 * 3600, Centre = pallas, Make = () => Parked(pallas, false) });
            list.Add(new Scenario { Id = "S2a", What = "player 1 g brachistochrone to Mars; 1 g pirate 50,000 km behind on its thrust line, at relative rest", Duration = 6 * 3600, Make = () => Accelerating(1) });
            list.Add(new Scenario { Id = "S2b", What = "as S2a with a 2 g pirate", Duration = 10 * 3600, Make = () => Accelerating(2) });
            foreach (var (tag, out_) in new[] { ("n", 1.5e5), ("l", 3e5) })
                foreach (var g in new[] { 1.0, 2.0 })
                    list.Add(new Scenario
                    {
                        Id = $"S3{tag}-{g:F0}g", Duration = 6 * 3600, Centre = vesta, TrackDock = true, Make = () => Braking(g, out_),
                        What = $"player braking into Vesta from 600,000 km at 100 km/s; {g:F0} g pirate at rest relative to Vesta, {out_ / 1000:F0},000 km out on the approach line, 20,000 km along-track",
                    });
            foreach (var g in new[] { 1.0, 2.0, 3.0 })
                list.Add(new Scenario { Id = $"S5-{g:F0}g", What = $"S1 with a 3 g pirate; once alongside {PreHoldS:F0} s, the player burns {g:F0} g by hand and flips every 1800 s for 6 h", Duration = 4 * 3600, BurnG = g, Make = () => Coasting(3) });
            return list;
        }

        static bool Coasting(Ship s) => s.Landed == null && s.RcsInput.sqrMagnitude == 0 &&
            (s.PlanActive ? s.Throttle == 0 && (s.Phase == "in orbit" || s.Phase == "velocity matched" || s.Phase == "cruising") : s.ThrottleCmd == 0);

        // OpenSystemScene.Coast() without the scene (see the header): returns the chunk flown, or 0 when the conic does not
        // converge and the step should be integrated instead.
        static double Coast(SolarSystem sys, Ship pl, double jd, double maxDt, Body dom)
        {
            var dp = sys.Pos(dom, jd);
            var r0 = pl.Pos - dp; var v0 = pl.Vel - sys.Vel(dom, jd);
            double T = Rails.Period(r0, v0, dom.GM);
            double chunk = Math.Min(maxDt, double.IsInfinity(T) ? Math.Max(60, 0.25 * r0.Mag / Math.Max(v0.Mag, 1e-6)) : T / 16);
            if (pl.PlanActive && pl.Phase == "cruising")
                chunk = Math.Min(chunk, 0.01 * (sys.Pos(pl.Target, jd) - pl.Pos).Mag / Math.Max((pl.Vel - sys.Vel(pl.Target, jd)).Mag, 1e-3));
            chunk = Math.Max(chunk, Math.Min(maxDt, 1));
            D3 r1, v1;
            for (int tries = 0; !Rails.Propagate(r0, v0, dom.GM, chunk, out r1, out v1); tries++)
                if (tries == 20) return 0; else chunk *= 0.5;
            double jd1 = jd + chunk / 86400;
            pl.Pos = sys.Pos(dom, jd1) + r1;
            pl.Vel = sys.Vel(dom, jd1) + v1;
            pl.Throttle = 0;
            if (!pl.PlanActive) pl.Phase = "coasting";
            pl.Turn(sys, jd1, chunk, dom);
            if (pl.PlanActive) pl.Step(sys, jd1, 0.001, dom);
            return chunk;
        }

        // Least distance between the pirate (on the quadratic through its step's ends) and the player (on the stand-in's
        // path) over one pirate step of h seconds that starts t seconds into the span; skipped when it cannot beat `best`.
        static double Closest(Pursuit pu, double t, double h, D3 pp, D3 vp, D3 pp1, double best)
        {
            D3 r0 = pp - pu.PlayerPos(t), w0 = vp - pu.PlayerVel(t);
            D3 ar = (pp1 - pp - vp * h) * (2 / (h * h)) - pu.StandIn.PathAccel(0);
            if (r0.Mag - w0.Mag * h - 0.5 * ar.Mag * h * h > best) return best;
            double R2(double s) => (r0 + w0 * s + ar * (0.5 * s * s)).Sqr;
            const int K = 16;
            int bi = 0; double bv = R2(0);
            for (int i = 1; i <= K; i++) { double v = R2(h * i / K); if (v < bv) { bv = v; bi = i; } }
            double lo = h * Math.Max(bi - 1, 0) / K, hi = h * Math.Min(bi + 1, K) / K;
            for (int k = 0; k < 40; k++)
            {
                double m1 = lo + (hi - lo) * 0.382, m2 = lo + (hi - lo) * 0.618;
                if (R2(m1) < R2(m2)) hi = m2; else lo = m1;
            }
            return Math.Min(best, Math.Sqrt(Math.Min(bv, R2(0.5 * (lo + hi)))));
        }

        // One scenario at frame length h with the pirate on the given plan. raw (optional) gets the trace.
        public static Result Fly(SolarSystem sys, string id, double h, Plan plan = Plan.Pursue, TextWriter raw = null)
        {
            var sc = Scenarios(sys).Find(s => s.Id == id) ?? throw new ArgumentException("no pursuit scenario " + id);
            return RunOne(sys, sc, h, plan, raw);
        }

        static Result RunOne(SolarSystem sys, Scenario sc, double h0, Plan plan, TextWriter raw)
        {
            var init = sc.Make();
            var pl = init.Player;
            double jd = init.Jd;
            var pu = new Pursuit(sys, jd, init.PiratePos, init.PirateVel, init.PirateG, pl.Pos, pl.Vel);
            var pi = pu.Pirate;
            pi.Plan = plan;
            var res = new Result { Id = sc.Id, What = sc.What, H = h0, Plan = plan, PirateG = init.PirateG };
            double standoff = Ship.Standoff(pu.StandIn);
            var los0 = (pl.Pos - pi.Pos).Norm;
            res.Range0 = (pi.Pos - pl.Pos).Mag;
            double t = 0, end = sc.Duration, nextDom = 0, nextSample = 0, nextSteady = 0;
            Body domPl = null;
            long ticks = 0;
            var sw = new Stopwatch();
            var alongRanges = new List<double>();
            var steady = new List<double>();
            // S5: metrics start with the first burn; everything else from t = 0
            double t0 = sc.BurnG > 0 ? -1 : 0, nextFlip = 0, pre = 0, maxRange = 0;
            double along = 0, alongTotal = 0, ext = res.Range0;
            bool on = false, falling = true, stop = false;
            int events = 0;
            raw?.WriteLine($"RUN {sc.Id} {plan} h={h0:G4} | {sc.What} | range0 {res.Range0:F0} km");
            raw?.WriteLine("        t_s     range_km   vrel_kms  thr    along_s  comp/AMax  pirate phase | player phase");
            while (t < end - 1e-9 && !stop)
            {
                if (sc.BurnG > 0 && t0 >= 0 && t >= nextFlip - 1e-9)
                {
                    // the scene's Flip(): a one-shot slew to the reverse heading (FlipDone ends the hold on arrival)
                    if (pl.ThrottleCmd == 0) { pl.MaxG = sc.BurnG; pl.ThrottleCmd = 1; }
                    else { pl.Plan = Plan.None; pl.CustomDir = -pl.Heading; pl.Hold = Hold.Custom; }
                    nextFlip += 1800;
                }
                double simDt = h0;
                while (simDt > 1e-9)
                {
                    if (t >= nextDom - 1e-9) { domPl = Ship.Dominant(sys, pl.Pos, jd); nextDom = t + DomS; }
                    D3 p0 = pl.Pos, v0 = pl.Vel;
                    double used = Coasting(pl) ? Coast(sys, pl, jd, simDt, domPl) : 0, cut = used;
                    if (used <= 0)
                    {
                        used = (jd + simDt / 86400 - jd) * 86400;
                        if (used <= 0) break;
                        pl.Step(sys, jd, used, domPl);
                        cut = used;
                    }
                    double jd1 = jd + used / 86400, h = (jd1 - jd) * 86400;
                    // a read of the old path at the new span's start, as a HUD might make: Span must not let it through
                    sys.Pos(pu.StandIn, jd);
                    pu.Span(sys, jd, h, p0, v0, pl.Vel);
                    res.Gap = Math.Max(res.Gap, (pu.PlayerPos(h) - pl.Pos).Mag);
                    if (res.Spans == 0 && (pi.Vel - pl.Vel).Mag < 1) // rest-to-rest starts only
                    {
                        var comp0 = pu.StandIn.PathAccel(jd) - sys.Gravity(pi.Pos, jd);
                        double a1 = Ship.Spare(comp0, los0, pi.AMax), a2 = Ship.PursueMargin * Ship.Spare(comp0, -los0, pi.AMax);
                        double v = Math.Sqrt(2 * Math.Max(res.Range0 - standoff, 0) * a1 * a2 / (a1 + a2));
                        res.ClosedForm = v / a1 + v / a2;
                    }
                    for (double j = jd; j < jd1;)
                    {
                        double tj = (j - jd) * 86400;
                        if ((sys.Pos(pu.StandIn, j) - pu.PlayerPos(tj)).Sqr > 0) res.Stale++;
                        res.ErrVel = Math.Max(res.ErrVel, (sys.Vel(pu.StandIn, j) - pu.PlayerVel(tj)).Mag);
                        D3 pp = pi.Pos, vp = pi.Vel;
                        string ph0 = pi.Phase;
                        sw.Restart();
                        double j2 = pu.Step(sys, j);
                        sw.Stop();
                        ticks += sw.ElapsedTicks;
                        res.SubSteps++;
                        double hp = (j2 - j) * 86400;
                        if (t0 >= 0)
                        {
                            double m = Closest(pu, tj, hp, pp, vp, pi.Pos, res.MinRange);
                            if (m < res.MinRange) { res.MinRange = m; res.MinRangeT = t - t0 + tj; }
                            res.Phases.TryGetValue(pi.Phase, out var ph);
                            res.Phases[pi.Phase] = ph + hp;
                            if (pi.Phase.StartsWith("climbing") && !ph0.StartsWith("climbing")) res.ClimbIn++;
                            if (pi.Phase == "turning" && ph0 != "turning") res.TurnIn++;
                        }
                        j = j2;
                    }
                    jd = jd1; t += h; res.Spans++;
                    simDt -= cut;
                    if (simDt < 1e-4) simDt = 0;

                    double range = (pi.Pos - pl.Pos).Mag, vrel = (pi.Vel - pl.Vel).Mag;
                    bool now = range < Career.InterceptKm && vrel < Career.InterceptKms;
                    if (t0 < 0)
                    {
                        pre = now ? pre + h : 0;
                        if (pre >= PreHoldS) { t0 = t; res.CatchT = t - pre; res.BurnStart = t; nextFlip = t; end = t + 6 * 3600; ext = range; }
                        continue;
                    }
                    double tm = t - t0;
                    if (now)
                    {
                        if (!on && events++ < 40) raw?.WriteLine($"  EVENT t={tm:F0} alongside starts (range {range:F2} km, vrel {vrel:F4} km/s)");
                        if (res.FirstAlong < 0) res.FirstAlong = tm;
                        along += h; alongTotal += h;
                        res.Longest = Math.Max(res.Longest, along);
                        if (along <= SteadyS) res.EntryVrel = Math.Max(res.EntryVrel, vrel);
                        else
                        {
                            res.SteadyVrelMax = Math.Max(res.SteadyVrelMax, vrel);
                            if (t >= nextSteady) { steady.Add(vrel); nextSteady = t + 1; } // a 1 s sample, for the median
                        }
                        alongRanges.Add(range);
                        if (h0 < 1 && along >= Threat.BoardingSeconds + SteadyS) { stop = true; res.EarlyStop = true; }
                    }
                    else if (on)
                    {
                        res.Breaks++;
                        if (events++ < 40) raw?.WriteLine($"  EVENT t={tm:F0} alongside breaks after {along:F0} s (range {range:F2} km, vrel {vrel:F4} km/s, pirate '{pi.Phase}', player '{pl.Phase}')");
                        along = 0;
                    }
                    on = now;
                    maxRange = Math.Max(maxRange, range);
                    // a rebound: the range climbs 10 % + 5 km off a minimum before it falls again (overshoot, orbiting)
                    if (falling) { if (range < ext) ext = range; else if (range > ext * 1.1 + 5) { res.Rebounds++; if (res.Minima.Count < 6) res.Minima.Add(ext); falling = false; ext = range; } }
                    else { if (range > ext) ext = range; else if (range < ext * 0.9 - 5) { falling = true; ext = range; } }
                    if (sc.Centre != null)
                    {
                        var cp = sys.Pos(sc.Centre, jd);
                        res.MinAlt = Math.Min(res.MinAlt, (pi.Pos - cp).Mag - sc.Centre.Radius);
                        if (sc.TrackDock && res.DockT < 0 && pl.Throttle == 0)
                        {
                            // Career.FindDock's rule, applied to the scenario's body
                            D3 r = pl.Pos - cp, vr = pl.Vel - sys.Vel(sc.Centre, jd);
                            double d = r.Mag;
                            if (d <= Career.DockRange(sc.Centre) && vr.Sqr < 2 * sc.Centre.GM / d && Career.Periapsis(r, vr, sc.Centre.GM) > sc.Centre.Radius + Career.DockClearance(sc.Centre))
                            { res.DockT = tm; res.AlongAtDock = along; res.AlongTotalAtDock = alongTotal; raw?.WriteLine($"  EVENT t={tm:F0} player docks at {sc.Centre.Name}"); }
                        }
                    }
                    if (raw != null && t >= nextSample - 1e-9)
                    {
                        double comp = (pu.StandIn.PathAccel(jd) - sys.Gravity(pi.Pos, jd)).Mag / pi.AMax;
                        raw.WriteLine($"  {tm,9:F0}  {range,11:F3}  {vrel,9:F5}  {pi.Throttle,4:F2}  {along,8:F0}  {comp,9:F4}  {pi.Phase} | {pl.Phase}");
                        nextSample += SampleS;
                    }
                    if (stop) break;
                }
                if (sc.BurnG > 0 && t0 < 0 && t >= end - 1e-9) res.Note = "pirate never alongside 600 s within 4 h; ";
            }
            res.EndRange = (pi.Pos - pl.Pos).Mag;
            res.Dv = pi.DvUsed;
            res.UsPirate = ticks * 1e6 / Stopwatch.Frequency / Math.Max(res.SubSteps, 1);
            if (alongRanges.Count > 0) { alongRanges.Sort(); res.MedRangeAlong = alongRanges[alongRanges.Count / 2]; }
            if (steady.Count > 0) { steady.Sort(); res.SteadyVrelMed = steady[steady.Count / 2]; }
            if (res.MinRange == double.MaxValue) res.MinRange = double.NaN;
            if (sc.Id.StartsWith("S2")) res.Note += $"range {res.Range0:F0}->{res.EndRange:F0} km ({(res.EndRange - res.Range0) / res.Range0 * 100:+0.00;-0.00}%); ";
            if (sc.TrackDock) res.Note += res.DockT < 0 ? "player never docks; " : $"player docks at {res.DockT:F0} s, alongside then {res.AlongAtDock:F0} s continuous, {res.AlongTotalAtDock:F0} s total; ";
            if (sc.BurnG > 0 && res.CatchT >= 0) res.Note += $"caught at {res.CatchT:F0} s, burns from {res.BurnStart:F0} s; max range in burns {maxRange:F2} km; ";
            if (sc.Centre != null) res.Note += $"pirate min alt over {sc.Centre.Name} {res.MinAlt:F0} km; ";
            if (res.Rebounds > 0) res.Note += $"minima {string.Join("/", res.Minima.Select(m => m.ToString("F1")))} km; ";
            if (res.EarlyStop) res.Note += "stopped at hold 7800 s; ";
            res.Note += $"end: pirate '{pi.Phase}', player '{pl.Phase}'";
            raw?.WriteLine($"END {sc.Id} {plan} h={h0:G4} | first {res.FirstAlong:F0} s, longest {res.Longest:F0} s, breaks {res.Breaks}, min range {res.MinRange:F3} km at {res.MinRangeT:F0} s, " +
                $"spans {res.Spans}, pirate steps {res.SubSteps}, stale {res.Stale}, errVel {res.ErrVel:E2} | {res.Note}");
            raw?.WriteLine();
            raw?.Flush();
            return res;
        }

        // The stand-in against one 1 g burn step of the player at Vesta + 1e6 km: sys.Vel (the +-30 s central difference)
        // at the step's start, middle and end, PathAccel against the step's thrust plus mean gravity, the gap between the
        // quadratic's end and the player's, and a read of the old path that Span must not let through. Returns the worst
        // velocity error (km/s), or +inf on a stale read.
        public static double Probe(SolarSystem sys, TextWriter t = null)
        {
            var vesta = sys["Vesta"];
            D3 pV = sys.Pos(vesta, Jd0), vV = sys.Vel(vesta, Jd0), ut = TangentOf(pV, vV);
            Echo(t, $"NUMERICS PROBE: one 1 g burn step of the player at Vesta + 1e6 km (heliocentric speed {vV.Mag:F2} km/s)");
            Echo(t, "        h_s   errVel0_kms  errVelMid_kms  errVel1_kms   errAcc_g    gap1_km   cache");
            double worst = 0;
            foreach (var h0 in ProbeHs)
            {
                var pl = Player(sys, pV + pV.Norm * 1e6, vV, ut);
                pl.ThrottleCmd = 1;
                double jd = Jd0, h = (jd + h0 / 86400 - jd) * 86400, jd1 = jd + h / 86400;
                D3 p0 = pl.Pos, v0 = pl.Vel, g0 = sys.Gravity(p0, jd);
                pl.Step(sys, jd, h, sys.Sun);
                D3 p1 = pl.Pos, v1 = pl.Vel, g1 = sys.Gravity(p1, jd1), thrust = pl.Heading * (pl.Throttle * pl.AMax);
                // constructed 1000 km off, so the old path's read at jd differs from the new one
                var pu = new Pursuit(sys, jd, p0 - ut * 1e5, v0, 1, p0 + ut * 1000, v0);
                sys.Pos(pu.StandIn, jd);
                pu.Span(sys, jd, h, p0, v0, v1);
                bool stale = (sys.Pos(pu.StandIn, jd) - p0).Sqr > 0;
                double jm = jd + 0.5 * h / 86400, tm = (jm - jd) * 86400;
                double e0 = (sys.Vel(pu.StandIn, jd) - v0).Mag;
                double em = (sys.Vel(pu.StandIn, jm) - pu.PlayerVel(tm)).Mag;
                double e1 = (sys.Vel(pu.StandIn, jd1) - v1).Mag;
                double ea = (pu.StandIn.PathAccel(jd) - (thrust + (g0 + g1) * 0.5)).Mag / Ship.G0;
                double gap = (pu.StandIn.Path(jd1) - p1).Mag;
                worst = Math.Max(worst, stale ? double.PositiveInfinity : Math.Max(e0, Math.Max(em, e1)));
                Echo(t, $"  {h,9:G4}  {e0,12:E2}  {em,13:E2}  {e1,11:E2}  {ea,10:E2}  {gap,9:E2}   {(stale ? "STALE" : "ok")}");
            }
            return worst;
        }

        public static double Probe(TextWriter t) => Probe(SolarSystem.Build(), t);

        static string Row(params string[] c) =>
            $"{c[0],-3} {c[1],-6} {c[2],-5} {c[3],6} {c[4],7} {c[5],6} {c[6],9} {c[7],-4} {c[8],4} {c[9],8} {c[10],17} {c[11],7} {c[12],9} {c[13],9} {c[14],7} {c[15],6} {c[16],4} {c[17],5} {c[18],8} {c[19],5} {c[20],6} {c[21],-9} {c[22],-40} {c[23]}";

        static string Format(int n, Result r)
        {
            string P(string phase) => phase.StartsWith("climbing clear") ? "climb" : phase switch
            {
                "closing" => "clo", "braking" => "brk", "holding station" => "hold", "turning" => "turn", "trimming (RCS)" => "trim",
                "matching its burn" => "match", "accelerating" => "acc", "flipped, braking" => "fbrk", _ => phase,
            };
            double tot = r.Phases.Values.Sum();
            var ph = tot > 0 ? string.Join(" ", r.Phases.OrderByDescending(kv => kv.Value).Select(kv => $"{P(kv.Key)}{kv.Value * 100 / tot:F0}")) : "-";
            bool any = r.FirstAlong >= 0;
            return Row(n.ToString(), r.Id, r.Plan == Plan.Pursue ? "Purs" : "Brach", r.H.ToString("G3"), any ? r.FirstAlong.ToString("F0") : "never",
                double.IsNaN(r.ClosedForm) || double.IsInfinity(r.ClosedForm) ? "-" : r.ClosedForm.ToString("F0"), r.Longest.ToString("F0"), r.Holds ? "YES" : "no", r.Breaks.ToString(),
                any ? r.EntryVrel.ToString("F4") : "-", r.SteadyVrelMax > 0 ? $"{r.SteadyVrelMax:E1}/{r.SteadyVrelMed:E1}" : "-",
                double.IsNaN(r.MedRangeAlong) ? "-" : r.MedRangeAlong.ToString("F2"), r.MinRange.ToString("F3"), r.EndRange.ToString("F1"),
                (100 * (r.EndRange - r.Range0) / r.Range0).ToString("+0.00;-0.00"), r.Dv.ToString("F1"), r.Rebounds.ToString(), r.Stale.ToString(),
                r.ErrVel.ToString("E1"), r.UsPirate.ToString("F0"), (r.SubSteps / (double)Math.Max(r.Spans, 1)).ToString("F2"), $"c{r.ClimbIn}/t{r.TurnIn}", ph, r.Note);
        }

        public static int Run(TextWriter table, TextWriter raw)
        {
            var sys = SolarSystem.Build();
            foreach (var l in HeaderText) Echo(table, l);
            Echo(table, "");
            double probe = Probe(sys, table);
            Echo(table, "");
            var runs = new List<(string id, double h, Plan plan)>();
            foreach (var id in new[] { "S1", "S4" }) foreach (var h in SmallHs.Concat(Hs).Concat(Spans)) runs.Add((id, h, Plan.Pursue));
            foreach (var h in Hs) runs.Add(("S2a", h, Plan.Pursue));
            foreach (var h in new[] { 0.05 }.Concat(Hs)) runs.Add(("S2b", h, Plan.Pursue));
            foreach (var id in new[] { "S4v", "S4r", "S3n-1g", "S3n-2g", "S3l-1g", "S3l-2g", "S5-1g", "S5-2g", "S5-3g" }) foreach (var h in Hs) runs.Add((id, h, Plan.Pursue));
            foreach (var h in Hs) runs.Add(("S1", h, Plan.Brachistochrone));
            var scenarios = Scenarios(sys);
            foreach (var sc in scenarios.Where(s => runs.Any(r => r.id == s.Id))) Echo(table, $"  {sc.Id,-6} {sc.What}");
            Echo(table, "");
            Echo(table, Row("run", "scen", "plan", "h_s", "first_s", "cf_s", "longest_s", "hold", "brk", "entryVrel", "steadyVrel max/med", "medRng", "minRng_km",
                "endRng_km", "rng%", "dv", "reb", "stale", "errVel", "us/st", "st/sp", "climb/trn", "phases (% of time)", "notes"));
            var results = new List<Result>();
            var total = Stopwatch.StartNew();
            foreach (var (id, h, plan) in runs)
            {
                var r = RunOne(sys, scenarios.Find(s => s.Id == id), h, plan, raw);
                results.Add(r);
                Echo(table, Format(results.Count, r));
            }
            Echo(table, "");
            Echo(table, $"{results.Count} runs in {total.Elapsed.TotalSeconds:F0} s wall clock; {results.Sum(r => r.SubSteps):N0} pirate steps at {results.Sum(r => r.UsPirate * r.SubSteps) / Math.Max(1, results.Sum(r => r.SubSteps)):F1} us each");
            Echo(table, "");
            return Verdict(results, probe, table);
        }

        static int Verdict(List<Result> rs, double probe, TextWriter t)
        {
            IEnumerable<Result> Of(string id, Plan plan = Plan.Pursue) => rs.Where(r => r.Id == id && r.Plan == plan);
            string Each(IEnumerable<Result> list, Func<Result, string> f) => string.Join(", ", list.Select(r => $"h {r.H:G3}: {f(r)}"));
            Echo(t, "VERDICT (pre-registered rule above)");
            var g1Runs = Of("S1").Concat(Of("S4")).ToList();
            bool g1 = g1Runs.Count == 12 && g1Runs.All(r => r.Holds);
            foreach (var id in new[] { "S1", "S4" })
                Echo(t, $"  G1 {id}: {Each(Of(id), r => $"{r.Longest:F0} s{(r.Holds ? "" : " FAIL")}")}");
            Echo(t, $"  G1 {(g1 ? "PASS" : "FAIL")}");
            var s2a = Of("S2a").ToList();
            bool g2 = s2a.Count == 2 && s2a.All(r => r.RangeChange < GateRange);
            Echo(t, $"  G2 S2a: {Each(s2a, r => $"{r.RangeChange * 100:F2} %")} (gate < {GateRange * 100:F0} %): {(g2 ? "PASS" : "FAIL")}");
            var s2b = Of("S2b").ToList();
            bool g3 = s2b.Count == 3 && s2b.All(r => r.Caught && r.MinRange >= r.Rs);
            Echo(t, $"  G3 S2b: {Each(s2b, r => $"first {(r.FirstAlong < 0 ? "never" : r.FirstAlong.ToString("F0"))} s against {GateCatch} x {r.ClosedForm:F0} = {GateCatch * r.ClosedForm:F0} s, minRng {r.MinRange:F3} km")}: {(g3 ? "PASS" : "FAIL")}");
            double worst = Math.Max(probe, rs.Max(r => r.ErrVel));
            bool g4 = worst < GateVel;
            Echo(t, $"  G4 worst stand-in velocity error {worst:E2} km/s (probe {probe:E2}; gate < {GateVel:E0}): {(g4 ? "PASS" : "FAIL")}");
            Echo(t, $"  stale stand-in reads: {rs.Sum(r => r.Stale)} in {rs.Sum(r => r.SubSteps):N0} pirate steps");
            var brach = Of("S1", Plan.Brachistochrone).ToList();
            Echo(t, $"  S1 on Brachistochrone (report): {Each(brach, r => $"first {r.FirstAlong:F0} s (spike 6650 s), longest {r.Longest:F0} s")}");
            foreach (var id in new[] { "S3n-1g", "S3n-2g", "S3l-1g", "S3l-2g" })
                Echo(t, $"  {id} (report): {Each(Of(id), r => r.DockT < 0 ? "player never docks" : $"dock {r.DockT:F0} s, first alongside {(r.FirstAlong < 0 ? "never" : r.FirstAlong.ToString("F0") + " s")}, alongside at dock {r.AlongAtDock:F0} s continuous, {r.AlongTotalAtDock:F0} s total")}");
            bool pass = g1 && g2 && g3 && g4;
            Echo(t, $"  VERDICT: {(pass ? "pass" : "fail")}");
            return pass ? 0 : 1;
        }
    }
}
