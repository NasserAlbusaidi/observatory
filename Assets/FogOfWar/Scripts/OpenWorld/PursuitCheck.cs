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
        const double DwellCapS = 30; // Career.Intercept credits at most 30 s of dwell per frame
        // the pre-registered gates (header): G2 range change, G3 catch time over the closed form, G4 stand-in velocity
        // error, G5 range change against a hand burn at the pirate's own g
        public const double GateRange = 0.02, GateCatch = 1.2, GateVel = 1e-4, GateHand = 0.001;
        static readonly double[] SmallHs = { 1.0 / 60, 0.05 }, Hs = { 1, 5 }, Spans = { 30, 50 }, ProbeHs = { 1.0 / 60, 0.05, 1, 5, 30 };

        struct Init { public Ship Player; public D3 PiratePos, PirateVel; public double PirateG, Jd; }

        sealed class Scenario
        {
            public string Id, What;
            public double Duration;
            public Body Centre;   // the body the player is at (pirate altitude, dock time), or null
            public bool TrackDock;
            public double BurnG;  // S5: the player's hand burns once the pirate is alongside (0: none)
            public string Evade;  // S2b-cut, S2b-flip: "cut" or "flip" once the range falls under EvadeKm (null: none)
            public double EvadeKm;
            public Func<Init> Make;
        }

        public sealed class Result
        {
            public string Id, What;
            public double H, PirateG, Rs = Pursuit.Radius;
            public Plan Plan;
            public double FirstAlong = -1, Capture = -1, Longest, EntryVrel, SteadyVrelMax, SteadyVrelMed = double.NaN, MedRangeAlong = double.NaN;
            public double MinRange = double.MaxValue, MinRangeT, MinRangeVrel = double.NaN, Range0, EndRange, Dv, ErrVel, Gap, UsPirate, ClosedForm = double.NaN;
            public double MinAlt = double.MaxValue, DockT = -1, AlongAtDock, AlongTotalAtDock, CatchT = -1, BurnStart = -1, PeakVrel, SteadyPreDock;
            // S2b-cut, S2b-flip: when the player acted, at what range and closing speed; the pass that followed (closest
            // approach inside a pirate step and the relative speed there) and how far the pirate then ran past
            public double EvadeT = -1, EvadeRange, EvadeClosing, PassRange = double.NaN, PassVrel = double.NaN, Apex = double.NaN;
            public int Stale, Breaks, Rebounds, Spans, SubSteps, ClimbIn, TurnIn, TurnInPreDock;
            public bool EarlyStop;
            public readonly Dictionary<string, double> Phases = new Dictionary<string, double>();
            public readonly List<double> Minima = new List<double>();
            public string Note = "";
            public bool Holds => Longest >= Threat.BoardingSeconds;
            public double RangeChange => Math.Abs(EndRange - Range0) / Range0;
            // the start of the first hold that lasts 600 s, not the first alongside: that can be an overshoot's apex under 500 km
            public bool Caught => Capture >= 0 && Capture <= GateCatch * ClosedForm;
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
            "Rails.Propagate with halving and represented-time rounding, Pos/Vel at the chunk's end, Turn, and a plan-only RecheckPlan. It leaves out",
            "FirstSurfaceHit and CheckTouchdown (Near is empty) and looks the dominant body up every 60 s, not every chunk.",
            "The integrate branch steps the whole frame and leaves out OpenSystemScene.SubstepSize. No run here would split differently: above 0.25 s",
            "the attitude slews by MaxRate h and leaves AngVel 0, so the 0.05 s rotation cap never applies; the plan term 0.05 d/v is over 21 s on every",
            "powered leg (S2 toward Mars; S3 on the 0.85 g curve, d/v >= 435 s); the orbit term is over 53 s at Vesta; at h <= 0.05 s the step is the frame.",
            "Stand-in radius Rs = 0.05 km (Pursuit.Radius): the pirate seeks 2.05 km; the 1.05 km exclusion is gated only for S1ahead. Its 20 km/0.2 km/s capture zone",
            "uses critically damped relative position control. Transverse-motion lead is capped at 600 s. Pirate constructor: UnlimitedFuel, CruiseCap 0,",
            "Att facing the player, Obstacles by the scene's rule (Parent != null, Radius >= 100), Near empty.",
            "Measures. alongside = |pirate.Pos - player.Pos| < 500 km AND |pirate.Vel - player.Vel| < 0.1 km/s, from the two Ships at each span end.",
            " hold = the boarding clock: Career.Intercept's min(frame, 30 s) per frame, as span time x min(1, 30 s / h) while alongside, reset to 0 on",
            "   any break (span time for h <= 30 s, 0.6 of it at h = 50 s); breaks = on-to-off transitions (the run's counter).",
            " capt = the start of the first alongside spell that lasts 600 s of sim time. first = the first alongside span end, which can be the apex of",
            "   an overshoot that turns round under 500 km.",
            " minRng = the least distance inside every pirate step: the pirate on the quadratic through its step's ends, the player on the stand-in path;",
            "   vMin = the relative speed there. gap = the largest distance between the quadratic's end and the player's actual end of a span.",
            " entryVrel = max |vrel| in the first 600 s of each spell; steady = max/median |vrel| in spells after their first 600 s.",
            " errVel = |sys.Vel(stand-in, j) - (v0 + a t)| at every pirate step start j. stale = sys.Pos(stand-in, j) != its path at j.",
            " Runs with h < 1 s stop once a hold reaches 7800 s (7200 s plus a 600 s steady window).",
            " S5: the pirate is alongside when the burns start, so a loss of contact at the first burn counts as a break; peak vrel = max |vrel| in burns.",
            "After #88, Coast propagates exactly the represented clock interval; RecheckPlan advances no physics. After #50, h < 30 s runs use the",
            "normal integrated gravity model. The 30/50 s span runs explicitly exercise the high-warp conic approximation. S4 is a free Pallas orbit.",
            "",
            "PRE-REGISTERED VERDICT RULE. Committed in 25ea354 before the first run of the scenarios. AMENDED after review, in the commit that adds this",
            "paragraph, before any run of the amended check. Each amendment tightens a gate or restates one; none loosens a gate: G1's hold is the",
            "boarding clock; G3 times the lasting hold, not the first alongside, and adds the 7200 s hold; G4 adds no stale reads; G5 is new; G2's",
            "limit is unchanged and now says where it came from.",
            " G1  S1 and S4 (Pallas) with Pursue hold >= 7200 s continuously in every run: h = 1/60, 0.05, 1, 5 s and spans of 30 and 50 s (12 runs).",
            " G2  S2a with Pursue at h = 1 and 5 s: |range(6 h) - range(0)| / range(0) < 2 %. Not a blind prediction. The player's autopilot burns",
            "     through Budget, which keeps back what cancels the target's gravity, so it accelerates a little under 1 g (comp/AMax 0.9996 in the",
            "     trace), and the pirate closes on that reserve alone, about 4e-4 g (comp is along the line of sight, so Share does what Budget does;",
            "     the range falls with t^2). The 2 % limit was set from the spike's measured 1.75 % for this scenario.",
            " G3  S2b with Pursue at h = 0.05, 1 and 5 s: capt <= 1.2 x the closed form, minRng >= Rs over the whole run, and hold >= 7200 s.",
            "     Closed form: rest-to-rest relative motion along the line of sight, closing at a1 = Ship.Spare(comp0, +los, AMax) and braking at",
            "     a2 = 0.85 Ship.Spare(comp0, -los, AMax); comp0 = the stand-in's acceleration less the pirate's gravity in the first span, los the",
            "     line of sight at t = 0, d = range(0) - standoff: v^2 = 2 d a1 a2 / (a1 + a2), t = v / a1 + v / a2. For a 1 g target and a 2 g",
            "     pirate 50,000 km back on its thrust line: a1 = 1 g, a2 = 2.55 g, t = 3768 s, gate 4521 s.",
            " G4  errVel < 1e-4 km/s in every run and in the probe, and 0 stale reads over all runs.",
            " G5  S2h with Pursue at h = 1 and 5 s: range change < 0.1 % in 6 h. A 1 g pirate 50,000 km behind a 1 g hand burn (no plan, a held",
            "     heading, throttle 1) at relative rest has no spare drive: |comp| = AMax to within the gravity gradient (about 3e-8 g), so it only",
            "     matches ('matching its burn', or Share with a spare near 0). Burning at the pirate's own g escapes. Closed form: under 0.1 km in 6 h.",
            " G6 (#97) S4r at h=1 and 5 s: lasting catch <=1.2 x closed form, hold >=7200 s, at most 10 climb entries (previously 114 at h=1).",
            " G7 (#97) S1ahead at h=1/60,.05,1,5,30,50 s: minimum separation >= Rs+1 km and hold >=7200 s. Added before the new scenario runs.",
            " REVIEW AMENDMENT: this paragraph and G8 are committed before the first run of the amended check. It adds a gate; no old gate is loosened.",
            " G8 S4 (Pallas, all six h/spans) and S4v (Vesta, h=1 and 5 s): lasting catch <=1.05 x closed form, hold >=7200 s, climb entries <=10.",
            "     The historical #115 table motivates this regression: a pole approach must not repeatedly trip the last-resort surface guard.",
            " JOB 4 AMENDMENT: G9 is committed before its first run. S4guard is a 1 g inward plunge at Pallas; h=1 and 5 s must",
            "     stay at altitude >= 0.1 R + 20 km (the guard safe radius minus R) and enter climbing >=1 time. No old gate changes.",
            "     Call-level guard checks require non-null for a braking segment through the far side, and null for S4's safe pole geometry.",
            " VERDICT pass if G1-G9 all hold, else fail. Exit code 0 on pass, 1 on fail. Gameplay choices #89-92 remain open; braking margin stays .85.",
            " Report only: S2b-cut and S2b-flip (under 10,000 km the player cuts the drive, or flips and burns at the pirate: the pass and the run-out),",
            " S3 (S3m-2g, 375,000 km out, tests the 2 g boarding band), S5 (S5-1g also at 1/60 s, S5-2g",
            " also at 1/60 and 0.05 s), and S1 on Plan.Brachistochrone (first alongside and hold against the spike's).",
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

            // as Accelerating, then by hand: full throttle on the heading it had, no plan, so no Budget holds any drive back
            Init HandBurn(double g)
            {
                var i = Accelerating(g);
                var pl = i.Player;
                pl.Plan = Plan.None; pl.Hold = Hold.Custom; pl.CustomDir = pl.Heading; pl.ThrottleCmd = 1;
                return i;
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
            list.Add(new Scenario { Id = "S1ahead", What = "S1 with the pirate 100,000 km ahead; exclusion-distance regression", Duration = 8 * 3600,
                Make = () => { var i=Coasting(1); i.PiratePos=belt+ut*1e5; return i; } });
            list.Add(new Scenario { Id = "S4", What = "player parked at Pallas (not a port; 769.5 km, drive off); 1 g pirate 50,000 km out along Pallas' pole", Duration = 10 * 3600, Centre = pallas, Make = () => Parked(pallas, true) });
            list.Add(new Scenario { Id = "S4v", What = "as S4 at Vesta (a port; 788.1 km)", Duration = 10 * 3600, Centre = vesta, Make = () => Parked(vesta, true) });
            list.Add(new Scenario { Id = "S4r", What = "as S4 with the pirate on the Pallas->player line, in the orbit plane", Duration = 10 * 3600, Centre = pallas, Make = () => Parked(pallas, false) });
            list.Add(new Scenario { Id = "S4guard", What = "1 g pirate 20 Pallas radii out, aimed inward at 90% of its full-drive stopping energy; player on the far side", Duration = 3600, Centre = pallas,
                Make = () =>
                {
                    var i = Parked(pallas, false); var pc = sys.Pos(pallas, Jd0);
                    var outward = (i.Player.Pos - pc).Norm;
                    double radius = 20 * pallas.Radius, safe = 1.1 * pallas.Radius + 20;
                    i.PiratePos = pc - outward * radius;
                    i.PirateVel = sys.Vel(pallas, Jd0) + outward * Math.Sqrt(2 * 0.9 * 9.80665e-3 * (radius - safe));
                    return i;
                } });
            list.Add(new Scenario { Id = "S2a", What = "player 1 g brachistochrone to Mars; 1 g pirate 50,000 km behind on its thrust line, at relative rest", Duration = 6 * 3600, Make = () => Accelerating(1) });
            list.Add(new Scenario { Id = "S2h", What = "as S2a, but the player burns 1 g by hand on its heading (no plan); 1 g pirate", Duration = 6 * 3600, Make = () => HandBurn(1) });
            list.Add(new Scenario { Id = "S2b", What = "as S2a with a 2 g pirate", Duration = 10 * 3600, Make = () => Accelerating(2) });
            list.Add(new Scenario { Id = "S2b-cut", What = "as S2b; under 10,000 km the player cuts the drive (the scene's CutDrive)", Duration = 10 * 3600, Evade = "cut", EvadeKm = 1e4, Make = () => Accelerating(2) });
            list.Add(new Scenario { Id = "S2b-flip", What = "as S2b; under 10,000 km the player flips and burns 1 g at the pirate (Flip, full throttle)", Duration = 10 * 3600, Evade = "flip", EvadeKm = 1e4, Make = () => Accelerating(2) });
            foreach (var (tag, out_, gs) in new[] { ("n", 1.5e5, new[] { 1.0, 2.0 }), ("l", 3e5, new[] { 1.0, 2.0 }), ("m", 3.75e5, new[] { 2.0 }) })
                foreach (var g in gs)
                    list.Add(new Scenario
                    {
                        Id = $"S3{tag}-{g:F0}g", Duration = 6 * 3600, Centre = vesta, TrackDock = true, Make = () => Braking(g, out_),
                        What = $"player braking into Vesta from 600,000 km at 100 km/s; {g:F0} g pirate at rest relative to Vesta, {out_:N0} km out on the approach line, 20,000 km along-track",
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
            for (int tries = 0; ; tries++)
            {
                chunk = (jd + chunk / 86400 - jd) * 86400;
                if (chunk <= 0) return 0;
                if (Rails.Propagate(r0, v0, dom.GM, chunk, out r1, out v1)) break;
                if (tries == 20) return 0;
                chunk *= 0.5;
            }
            double jd1 = jd + chunk / 86400;
            pl.Pos = sys.Pos(dom, jd1) + r1;
            pl.Vel = sys.Vel(dom, jd1) + v1;
            pl.Throttle = 0;
            if (!pl.PlanActive) pl.Phase = "coasting";
            pl.Turn(sys, jd1, chunk, dom);
            if (pl.PlanActive) pl.RecheckPlan(sys, jd1, dom);
            return chunk;
        }

        // Least distance between the pirate (on the quadratic through its step's ends) and the player (on the stand-in's
        // path) over one pirate step of h seconds that starts t seconds into the span; skipped when it cannot beat `best`.
        // speed: the relative speed at that point, when it beats `best`.
        static double Closest(Pursuit pu, double t, double h, D3 pp, D3 vp, D3 pp1, double best, out double speed)
        {
            speed = double.NaN;
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
            double sm = 0.5 * (lo + hi), vm = R2(sm), s = bv <= vm ? h * bi / K : sm, d = Math.Sqrt(Math.Min(bv, vm));
            if (d >= best) return best;
            speed = (w0 + ar * s).Mag;
            return d;
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
            double along = 0, alongTotal = 0, ext = res.Range0, spell = 0, spellStart = 0;
            bool on = false, falling = true, stop = false;
            int events = 0;
            // S2b-cut, S2b-flip: 0 before the player acts, 1 closing on the pass, 2 running past it, 3 done
            int ev = 0;
            double evMin = double.MaxValue, evMinV = double.NaN, evMinS = double.MaxValue;
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
                if (sc.Evade != null && ev == 0 && (pi.Pos - pl.Pos).Mag < sc.EvadeKm)
                {
                    var rel = pl.Pos - pi.Pos;
                    ev = 1; res.EvadeT = t; res.EvadeRange = rel.Mag; res.EvadeClosing = D3.Dot(pi.Vel - pl.Vel, rel.Norm);
                    // the scene's CutDrive(); or SetThrottle(1) then Flip(): a full burn while it slews to the reverse heading
                    if (sc.Evade == "cut") { pl.ThrottleCmd = 0; pl.Throttle = 0; pl.Plan = Plan.None; pl.Phase = "drive cut"; }
                    else { pl.ThrottleCmd = 1; pl.Plan = Plan.None; pl.Phase = "pilot override"; pl.CustomDir = -pl.Heading; pl.Hold = Hold.Custom; }
                    raw?.WriteLine($"  EVENT t={t:F0} player {(sc.Evade == "cut" ? "cuts the drive" : "flips and burns at the pirate")} (range {res.EvadeRange:F1} km, closing {res.EvadeClosing:F2} km/s)");
                }
                double simDt = h0;
                while (simDt > 1e-9)
                {
                    if (t >= nextDom - 1e-9) { domPl = Ship.Dominant(sys, pl.Pos, jd); nextDom = t + DomS; }
                    D3 p0 = pl.Pos, v0 = pl.Vel;
                    // Short-frame runs exercise the normal integrated force model. 30/50 s spans explicitly
                    // exercise the high-compression conic approximation, including its represented-time clock.
                    double used = h0 >= 30 && Coasting(pl) ? Coast(sys, pl, jd, simDt, domPl) : 0, cut = used;
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
                            double m = Closest(pu, tj, hp, pp, vp, pi.Pos, res.MinRange, out var sp);
                            if (m < res.MinRange) { res.MinRange = m; res.MinRangeT = t - t0 + tj; res.MinRangeVrel = sp; }
                            if (ev == 1) { double me = Closest(pu, tj, hp, pp, vp, pi.Pos, evMin, out var se); if (me < evMin) { evMin = me; evMinV = se; } }
                            res.Phases.TryGetValue(pi.Phase, out var ph);
                            res.Phases[pi.Phase] = ph + hp;
                            if (pi.Phase.StartsWith("climbing") && !ph0.StartsWith("climbing")) res.ClimbIn++;
                            if (pi.Phase == "turning" && ph0 != "turning") { res.TurnIn++; if (res.DockT < 0) res.TurnInPreDock++; }
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
                        // alongside now, so a loss of contact when the burns start is a break
                        if (pre >= PreHoldS) { t0 = t; res.CatchT = t - pre; res.BurnStart = t; nextFlip = t; end = t + 6 * 3600; ext = range; on = true; }
                        continue;
                    }
                    double tm = t - t0;
                    if (sc.BurnG > 0) res.PeakVrel = Math.Max(res.PeakVrel, vrel);
                    if (now)
                    {
                        if (!on)
                        {
                            spell = 0; spellStart = tm;
                            if (events++ < 40) raw?.WriteLine($"  EVENT t={tm:F0} alongside starts (range {range:F2} km, vrel {vrel:F4} km/s)");
                        }
                        if (res.FirstAlong < 0) res.FirstAlong = tm;
                        spell += h;
                        double credit = h * Math.Min(1, DwellCapS / h0); // the boarding clock (header)
                        along += credit; alongTotal += credit;
                        res.Longest = Math.Max(res.Longest, along);
                        if (res.Capture < 0 && spell >= SteadyS) res.Capture = spellStart;
                        if (spell <= SteadyS) res.EntryVrel = Math.Max(res.EntryVrel, vrel);
                        else
                        {
                            res.SteadyVrelMax = Math.Max(res.SteadyVrelMax, vrel);
                            if (sc.TrackDock && res.DockT < 0) res.SteadyPreDock = Math.Max(res.SteadyPreDock, vrel);
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
                    if (ev == 1)
                    {
                        // the pass ends when the range climbs 10 % + 5 km off its least value (as a rebound below), or alongside
                        evMinS = Math.Min(evMinS, range);
                        if (now) ev = 3; else if (range > evMinS * 1.1 + 5) { ev = 2; res.Apex = range; }
                        if (ev > 1) { res.PassRange = evMin; res.PassVrel = evMinV; }
                    }
                    else if (ev == 2) { res.Apex = Math.Max(res.Apex, range); if (now || range < res.Apex * 0.9 - 5) ev = 3; }
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
            if (ev == 1) { res.PassRange = evMin; res.PassVrel = evMinV; }
            if (sc.Id.StartsWith("S2")) res.Note += $"range {res.Range0:F0}->{res.EndRange:F0} km ({(res.EndRange - res.Range0) / res.Range0 * 100:+0.00;-0.00}%); ";
            if (res.EvadeT >= 0)
                res.Note += $"{(sc.Evade == "cut" ? "drive cut" : "flip")} at {res.EvadeT:F0} s ({res.EvadeRange:F0} km, closing {res.EvadeClosing:F2} km/s), " +
                    $"pass {res.PassRange:F3} km at {res.PassVrel:F2} km/s{(double.IsNaN(res.Apex) ? "" : $", out to {res.Apex:F0} km")}; ";
            if (sc.TrackDock) res.Note += res.DockT < 0 ? "player never docks; " : $"player docks at {res.DockT:F0} s, alongside then {res.AlongAtDock:F0} s continuous, {res.AlongTotalAtDock:F0} s total; ";
            if (sc.TrackDock) res.Note += $"before the dock: steady vrel max {res.SteadyPreDock:E1} km/s, {res.TurnInPreDock} turn entries; ";
            if (sc.BurnG > 0 && res.CatchT >= 0) res.Note += $"caught at {res.CatchT:F0} s, burns from {res.BurnStart:F0} s; max range in burns {maxRange:F2} km, peak vrel {res.PeakVrel:F4} km/s; ";
            if (sc.Centre != null) res.Note += $"pirate min alt over {sc.Centre.Name} {res.MinAlt:F0} km; ";
            if (res.Rebounds > 0) res.Note += $"minima {string.Join("/", res.Minima.Select(m => m.ToString("F1")))} km; ";
            if (res.EarlyStop) res.Note += "stopped at hold 7800 s; ";
            res.Note += $"end: pirate '{pi.Phase}', player '{pl.Phase}'";
            raw?.WriteLine($"END {sc.Id} {plan} h={h0:G4} | first {res.FirstAlong:F0} s, capture {res.Capture:F0} s, longest {res.Longest:F0} s, breaks {res.Breaks}, " +
                $"min range {res.MinRange:F3} km at {res.MinRangeT:F0} s ({res.MinRangeVrel:F4} km/s), spans {res.Spans}, pirate steps {res.SubSteps}, stale {res.Stale}, " +
                $"errVel {res.ErrVel:E2}, gap {res.Gap:E2} km | {res.Note}");
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
            $"{c[0],-3} {c[1],-8} {c[2],-5} {c[3],6} {c[4],7} {c[5],7} {c[6],6} {c[7],9} {c[8],-4} {c[9],4} {c[10],9} {c[11],17} {c[12],7} {c[13],9} {c[14],8} {c[15],9} {c[16],7} {c[17],6} {c[18],4} {c[19],5} {c[20],8} {c[21],8} {c[22],5} {c[23],6} {c[24],-9} {c[25],-40} {c[26]}";

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
                r.Capture >= 0 ? r.Capture.ToString("F0") : "never",
                double.IsNaN(r.ClosedForm) || double.IsInfinity(r.ClosedForm) ? "-" : r.ClosedForm.ToString("F0"), r.Longest.ToString("F0"), r.Holds ? "YES" : "no", r.Breaks.ToString(),
                any ? r.EntryVrel.ToString("F4") : "-", r.SteadyVrelMax > 0 ? $"{r.SteadyVrelMax:E1}/{r.SteadyVrelMed:E1}" : "-",
                double.IsNaN(r.MedRangeAlong) ? "-" : r.MedRangeAlong.ToString("F2"), r.MinRange.ToString("F3"),
                double.IsNaN(r.MinRangeVrel) ? "-" : r.MinRangeVrel.ToString("F4"), r.EndRange.ToString("F1"),
                (100 * (r.EndRange - r.Range0) / r.Range0).ToString("+0.00;-0.00"), r.Dv.ToString("F1"), r.Rebounds.ToString(), r.Stale.ToString(),
                r.ErrVel.ToString("E1"), r.Gap.ToString("E1"), r.UsPirate.ToString("F0"), (r.SubSteps / (double)Math.Max(r.Spans, 1)).ToString("F2"),
                $"c{r.ClimbIn}/t{r.TurnIn}", ph, r.Note);
        }

        // Direct production call: distinguish a far-side crossing from S4's safe off-axis stopping segment.
        public static bool ClearanceCalls(SolarSystem sys, TextWriter log)
        {
            var b = sys["Pallas"]; var pc = sys.Pos(b, Jd0); var vc = sys.Vel(b, Jd0);
            var rad = (pc.Norm - b.Pole * D3.Dot(pc.Norm, b.Pole)).Norm;
            var probe = new Ship { MaxG = 1, Pos = pc + rad * (20 * b.Radius), Vel = vc - rad * 12 };
            probe.Obstacles.Add(b);
            var guard = typeof(Ship).GetMethod("PursueClearance", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            bool crossing = guard.Invoke(probe, new object[] { sys, Jd0 }) != null;
            double safe = 1.1 * b.Radius + 20;
            double stop = 12 * 12 / (2 * Ship.PursueMargin * (probe.AMax - b.GM / Math.Pow(20 * b.Radius, 2)));
            crossing &= stop > 20 * b.Radius + safe; // the endpoint is beyond the far side, not merely inside the body
            var target = rad * Ship.Standoff(b);
            var direction = (target - b.Pole * 50000).Norm;
            probe.Pos = pc + target * 0.9 + b.Pole * 5000; // point on S4's original pole-to-orbit line
            probe.Vel = vc + direction * 12;
            bool pole = guard.Invoke(probe, new object[] { sys, Jd0 }) == null;
            Echo(log, $"  G9 far-side crossing guard non-null: {(crossing ? "PASS" : "FAIL")}");
            Echo(log, $"  G9 S4 pole guard null: {(pole ? "PASS" : "FAIL")}");
            return crossing && pole;
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
            foreach (var h in SmallHs.Concat(Hs).Concat(Spans)) runs.Add(("S1ahead", h, Plan.Pursue));
            foreach (var id in new[] { "S2a", "S2h" }) foreach (var h in Hs) runs.Add((id, h, Plan.Pursue));
            foreach (var h in new[] { 0.05 }.Concat(Hs)) runs.Add(("S2b", h, Plan.Pursue));
            foreach (var id in new[] { "S2b-cut", "S2b-flip", "S4v", "S4r", "S4guard", "S3n-1g", "S3n-2g", "S3l-1g", "S3l-2g", "S3m-2g", "S5-1g", "S5-2g", "S5-3g" })
                foreach (var h in Hs) runs.Add((id, h, Plan.Pursue));
            runs.Add(("S5-1g", SmallHs[0], Plan.Pursue));
            foreach (var h in SmallHs) runs.Add(("S5-2g", h, Plan.Pursue));
            foreach (var h in Hs) runs.Add(("S1", h, Plan.Brachistochrone));
            var scenarios = Scenarios(sys);
            foreach (var sc in scenarios.Where(s => runs.Any(r => r.id == s.Id))) Echo(table, $"  {sc.Id,-8} {sc.What}");
            Echo(table, "");
            Echo(table, Row("run", "scen", "plan", "h_s", "first_s", "capt_s", "cf_s", "longest_s", "hold", "brk", "entryVrel", "steadyVrel max/med", "medRng", "minRng_km",
                "vMin_kms", "endRng_km", "rng%", "dv", "reb", "stale", "errVel", "gap_km", "us/st", "st/sp", "climb/trn", "phases (% of time)", "notes"));
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
            bool guardCalls = ClearanceCalls(sys, table);
            return Verdict(results, probe, table, guardCalls);
        }

        static int Verdict(List<Result> rs, double probe, TextWriter t, bool guardCalls)
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
            bool g3 = s2b.Count == 3 && s2b.All(r => r.Caught && r.MinRange >= r.Rs && r.Holds);
            Echo(t, $"  G3 S2b: {Each(s2b, r => $"capt {(r.Capture < 0 ? "never" : r.Capture.ToString("F0"))} s (first {(r.FirstAlong < 0 ? "never" : r.FirstAlong.ToString("F0"))} s) against {GateCatch} x {r.ClosedForm:F0} = {GateCatch * r.ClosedForm:F0} s, minRng {r.MinRange:F3} km, hold {r.Longest:F0} s")}: {(g3 ? "PASS" : "FAIL")}");
            double worst = Math.Max(probe, rs.Max(r => r.ErrVel));
            int stale = rs.Sum(r => r.Stale);
            bool g4 = worst < GateVel && stale == 0;
            Echo(t, $"  G4 worst stand-in velocity error {worst:E2} km/s (probe {probe:E2}; gate < {GateVel:E0}); stale reads {stale} in {rs.Sum(r => r.SubSteps):N0} pirate steps (gate 0): {(g4 ? "PASS" : "FAIL")}");
            var s2h = Of("S2h").ToList();
            bool g5 = s2h.Count == 2 && s2h.All(r => r.RangeChange < GateHand);
            Echo(t, $"  G5 S2h: {Each(s2h, r => $"{r.Range0:F1} -> {r.EndRange:F1} km ({r.RangeChange * 100:F4} %)")} (gate < {GateHand * 100:F1} %): {(g5 ? "PASS" : "FAIL")}");
            foreach (var id in new[] { "S2b-cut", "S2b-flip" })
                Echo(t, $"  {id} (report): {Each(Of(id), r => r.EvadeT < 0 ? "never under the trigger range" : $"at {r.EvadeT:F0} s, {r.EvadeRange:F0} km, closing {r.EvadeClosing:F2} km/s; pass {r.PassRange:F3} km at {r.PassVrel:F2} km/s{(double.IsNaN(r.Apex) ? "" : $", out to {r.Apex:F0} km")}; capt {(r.Capture < 0 ? "never" : r.Capture.ToString("F0") + " s")}, hold {r.Longest:F0} s")}");
            var brach = Of("S1", Plan.Brachistochrone).ToList();
            Echo(t, $"  S1 on Brachistochrone (report; spike, variant A, Rs 0.05: first 6650 s at h 1 and 5, longest 22152 s at h 1 and 22155 s at h 5): {Each(brach, r => $"first {r.FirstAlong:F0} s, longest {r.Longest:F0} s")}");
            foreach (var id in new[] { "S3n-1g", "S3n-2g", "S3l-1g", "S3l-2g", "S3m-2g" })
                Echo(t, $"  {id} (report): {Each(Of(id), r => r.DockT < 0 ? "player never docks" : $"dock {r.DockT:F0} s, first alongside {(r.FirstAlong < 0 ? "never" : r.FirstAlong.ToString("F0") + " s")}, alongside at dock {r.AlongAtDock:F0} s continuous, {r.AlongTotalAtDock:F0} s total{(r.AlongAtDock >= Threat.BoardingSeconds ? " (boards first)" : "")}")}");
            foreach (var id in new[] { "S5-1g", "S5-2g", "S5-3g" })
                Echo(t, $"  {id} (report): {Each(Of(id), r => r.CatchT < 0 ? "never caught" : $"peak vrel in the burns {r.PeakVrel:F4} km/s against {Career.InterceptKms} km/s, breaks {r.Breaks}, longest {r.Longest:F0} s")}");
            var nearBody = Of("S4r").ToList();
            bool g6 = nearBody.Count == 2 && nearBody.All(r => r.Caught && r.Holds && r.ClimbIn <= 10);
            Echo(t, $"  G6 S4r: {Each(nearBody,r => $"catch {r.Capture:F0}/{GateCatch*r.ClosedForm:F0} s; climb entries {r.ClimbIn} <= 10; hold {r.Longest:F0} s")}: {(g6 ? "PASS" : "FAIL")}");
            var ahead = Of("S1ahead").ToList();
            bool g7 = ahead.Count == 6 && ahead.All(r => r.Holds && r.MinRange >= r.Rs + 1);
            Echo(t, $"  G7 S1ahead: {Each(ahead,r => $"min range {r.MinRange:F4} >= {r.Rs+1:F2} km; hold {r.Longest:F0} s")}: {(g7 ? "PASS" : "FAIL")}");
            var poleRuns = Of("S4").Concat(Of("S4v")).ToList();
            bool g8 = poleRuns.Count == 8 && poleRuns.All(r => r.Capture >= 0 && r.Capture <= 1.05 * r.ClosedForm && r.Holds && r.ClimbIn <= 10);
            foreach (var id in new[] { "S4", "S4v" })
                Echo(t, $"  G8 {id}: {Each(Of(id), r => $"catch {r.Capture:F0}/{1.05*r.ClosedForm:F0} s; climb entries {r.ClimbIn} <= 10; hold {r.Longest:F0} s")}");
            Echo(t, $"  G8 {(g8 ? "PASS" : "FAIL")}");
            var guardRuns = Of("S4guard").ToList();
            double floor = 0.1 * SolarSystem.Build()["Pallas"].Radius + 20;
            bool g9 = guardCalls && guardRuns.Count == 2 && guardRuns.All(r => r.MinAlt >= floor && r.ClimbIn >= 1);
            Echo(t, $"  G9 S4guard: {Each(guardRuns, r => $"min altitude {r.MinAlt:F3} >= {floor:F3} km; climb entries {r.ClimbIn} >= 1")}: {(g9 ? "PASS" : "FAIL")}");
            bool pass = g1 && g2 && g3 && g4 && g5 && g6 && g7 && g8 && g9;
            Echo(t, $"  VERDICT: {(pass ? "pass" : "fail")}");
            return pass ? 0 : 1;
        }
    }
}
