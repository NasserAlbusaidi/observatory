using System;
using System.Collections;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // 14. Pirates (Career.Pirates.cs, docs/design-world.md W4): the raider's call and its start (D11), the rate (D3), the
    // three answers, patrol break-off (Q1), escape by a full burn against a coasting control (Q2), boarding and a broken
    // contact (Q4, Q5), give up (Q6), dock first (Q7), a landing, a save and load (Q9), then in the scene the lockstep, the
    // proxy body, the warp cap (Q8), Divert and a load mid-chase. Headless parts run on a system of their own at the pursuit
    // check's date with calls made directly: no waits on a roll. A throw is a failed check, since Start() has no guard.
    public partial class CareerQA
    {
        bool Guard(string what, Action a)
        {
            try { a(); return true; }
            catch (Exception e) { Check(false, $"pirates: {what} threw {e.GetType().Name}: {e.Message}"); return false; }
        }

        // a career on a system of its own: a 1 g ship with one job, ticked once so the career knows its ship and date
        sealed class Bench
        {
            public SolarSystem Sys;
            public Career C;
            public Ship Ship;
            public Contract Job;
            public double Jd = PursuitCheck.Jd0;
            public readonly List<(Career.Happening What, string Text)> Said = new List<(Career.Happening, string)>();
            public bool Heard(Career.Happening what, string part) => Said.Exists(s => s.What == what && s.Text.Contains(part));
            public void Tick(double dt) { Jd += dt / 86400; C.Tick(Sys, Ship, Jd, dt); }
        }

        static Bench MakeBench(SolarSystem qs, D3 pos, D3 vel, JobKind kind = JobKind.Cargo, string from = "Ceres", string to = "Vesta")
        {
            var b = new Bench { Sys = qs };
            b.C = new Career { Sys = qs, RouteEvents = false };
            b.C.OnEvent += (w, t) => b.Said.Add((w, t));
            b.Ship = new Ship { Pos = pos, Vel = vel, MaxG = 1, Fuel = 200, UnlimitedFuel = false };
            foreach (var x in qs.Bodies) if (x.Parent != null && x.Radius >= 100) b.Ship.Obstacles.Add(x);
            b.Job = new Contract
            {
                Id = 900200, Kind = kind, Title = "QA ore", From = from, To = to, Tonnes = kind == JobKind.Cargo ? 40 : 0, People = kind == JobKind.Passengers ? 10 : 0,
                Pay = 20000, Days = 100, IssuedJd = b.Jd, DeadlineJd = b.Jd + 100, Key = "qa/pirate",
            };
            b.C.Active.Add(b.Job);
            b.C.Tick(qs, b.Ship, b.Jd, 0);
            return b;
        }

        static string Raiders => Career.Factions[(int)Faction.Raiders].Name;
        static bool HavenNamed(string text) => Array.Exists(Threat.Havens, h => text.Contains(h));

        IEnumerator PirateChecks()
        {
            SolarSystem qs = null;
            if (Guard("system", () => qs = SolarSystem.Build()))
            {
                Guard("call and start", () => PirateCallChecks(qs));
                Guard("answers", () => PirateAnswerChecks(qs));
                Guard("patrol", () => PiratePatrolChecks(qs));
                Guard("boarding", () => PirateBoardingChecks(qs));
                Guard("ends", () => PirateEndChecks(qs));
                Guard("escape", () => PirateEscapeChecks(qs));
            }

            // the scene: a chase started by hand 1,000,000 km out from Vesta, the pirate 50,000 km off at rest with the ship
            Contract job = null;
            Encounter e = null;
            bool ok = Guard("scene setup", () =>
            {
                Put("warpIdx", 0);
                var vesta = sys["Vesta"];
                D3 pV = sys.Pos(vesta, jd), vV = sys.Vel(vesta, jd), out_ = pV.Norm;
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0; ship.Landed = null; ship.Hold = Hold.Off;
                ship.Pos = pV + out_ * 1e6; ship.Vel = vV;
                Call("SetTarget", vesta);
                job = new Contract { Id = 900300, Kind = JobKind.Cargo, Title = "QA water", From = "Ceres", To = "Vesta", Tonnes = 20, Pay = 20000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, Key = "qa/pirate-scene" };
                career.Active.Add(job);
                e = career.StartContact(job, sys, ship, jd, ship.Pos - out_ * 5e4, vV);
            });
            for (int i = 0; ok && i < 3; i++) yield return null;
            if (ok) ok = Guard("scene lockstep", () =>
            {
                var p = e.Proxy;
                Check(career.Encounter == e && sys.Bodies.Contains(p) && !sys.Bodies.Contains(e.Chase.StandIn) && !ship.Obstacles.Contains(p) && !ship.Near.Contains(p)
                      && Ship.Dominant(sys, sys.Pos(p, jd), jd) != p && Career.IsRaider(p) && Career.IsCraft(p) && p.GM == 0,
                    $"pirates: the proxy {p.Name} is in the system, the pirate's stand-in for the player is not; the proxy is no obstacle, no Near body, no frame and pulls nothing");
                double lag = (e.Chase.SpanEnd - jd) * 86400, dp = (sys.Pos(p, jd) - e.Pirate.Pos).Mag, dv = (sys.Vel(p, jd) - e.Pirate.Vel).Mag;
                Check(lag == 0 && dp < 1e-6 && dv < 1e-6 && e.Pirate.DvUsed > 0,
                    $"pirates: in the scene the pirate flies the player's spans: its clock {lag:G3} s from the scene's, the proxy {dp:G3} km and {dv:G3} km/s off it, Δv {e.Pirate.DvUsed:F3} km/s ({e.Pirate.Phase})");
                Put("warpIdx", 6);
            });
            if (ok) yield return null;
            if (ok) ok = Guard("warp cap far", () =>
            {
                int w = Get<int>("warpIdx");
                double cap = career.WarpCap;
                Check(cap == Career.ChaseWarp && WarpOf(w) <= cap && WarpOf(w + 1) > cap,
                    $"pirates: 1M× asked during a chase {e.Range:N0} km off runs at {WarpOf(w):N0}× (cap {cap:N0}×)");
                e.Pirate.Pos = ship.Pos + (ship.Pos - sys.Pos(sys["Vesta"], jd)).Norm * -5000; e.Pirate.Vel = ship.Vel;
                Put("warpIdx", 6);
            });
            if (ok) yield return null;
            if (ok) ok = Guard("warp cap close", () =>
            {
                int w = Get<int>("warpIdx");
                Check(career.WarpCap == Career.CloseWarp && WarpOf(w) <= Career.CloseWarp,
                    $"pirates: within {Career.CloseKm:N0} km ({e.Range:N0} km) the cap is {career.WarpCap:N0}× and the warp {WarpOf(w):N0}×");
                Put("warpIdx", 0);
                var port = Career.NearestPatrol(sys, ship, jd, out _);
                career.Answer(Career.RouteAnswer.Divert, jd);
                Check(port != null && ship.Target == sys[port.Body] && ship.PlanActive && ship.Plan == Plan.Brachistochrone && Get<int>("cruiseIdx") == 0 && career.Encounter == e,
                    $"pirates: Divert targets {port?.Name ?? "-"}, the nearest patrolled port by trip time, and engages the Fast plan ({ship.Phase}); the chase goes on");
                ship.Plan = Plan.None; ship.Throttle = 0;
                s.SaveGame();
                s.LoadGame();
            });
            if (ok) { yield return null; yield return null; }
            if (ok) Guard("scene load", () =>
            {
                Check(career.Encounter == null && !sys.Bodies.Exists(Career.IsRaider) && career.Notes.Exists(n => n.What == Career.Happening.RaiderGone && n.Text.Contains("not saved") && HavenNamed(n.Text)),
                    $"pirates: a load mid-chase drops it, the proxy goes, and a note says so ({career.Notes.Find(n => n.What == Career.Happening.RaiderGone).Text})");
            });
            Guard("scene cleanup", () =>
            {
                career.DropContact();
                career.Active.RemoveAll(c => c.Key == "qa/pirate-scene");
                Put("warpIdx", 0);
            });
        }

        static double WarpOf(int i)
        {
            var w = (double[])typeof(OpenSystemScene).GetField("Warps", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
            return i < w.Length ? w[i] : double.PositiveInfinity;
        }

        // the belt point the pursuit check uses: 1,000,000 km out from Vesta, at rest with it
        static (Body vesta, D3 at, D3 vel) Belt(SolarSystem qs)
        {
            var vesta = qs["Vesta"];
            var pV = qs.Pos(vesta, PursuitCheck.Jd0);
            return (vesta, pV + pV.Norm * 1e6, qs.Vel(vesta, PursuitCheck.Jd0));
        }

        void PirateCallChecks(SolarSystem qs)
        {
            var (vesta, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            double rate = b.C.PirateRate(qs, b.Ship, b.Job, b.Jd), presence = Threat.Presence(qs, at, b.Jd);
            Check(presence == 1 && Math.Abs(rate - Career.RaidsPerHour) < 1e-12 && Threat.PatrolAt(qs, at, b.Jd) == null,
                $"pirates: in the belt, at rest with Vesta and out of patrol reach, raiders call {rate:F3} an hour (k {Career.RaidsPerHour} × presence {presence} × e^0)");
            b.Ship.Vel = vel + at.Norm * Threat.MatchKms;
            double fast = b.C.PirateRate(qs, b.Ship, b.Job, b.Jd);
            b.Ship.Vel = vel;
            Check(Math.Abs(fast - Career.RaidsPerHour / Math.E) < 1e-12, $"pirates: at {Threat.MatchKms:F1} km/s against Vesta the rate falls by e ({fast:F4} an hour)");

            bool made = b.C.MakeCall(Career.RouteKind.Pirate, b.Job, qs, b.Ship, b.Jd);
            var e = b.C.Encounter;
            var call = b.C.Call;
            Check(made && e != null && call?.Kind == Career.RouteKind.Pirate && call.Job == b.Job && b.C.DropWarp && call.Answers.Length == 3
                  && call.Answers[0].What == Career.RouteAnswer.Run && call.Answers[1].What == Career.RouteAnswer.Divert && call.Answers[2].What == Career.RouteAnswer.GiveUp
                  && call.DivertTo != null && call.DivertTo.PatrolG > 0 && call.Title.Contains(Raiders) && call.Text.Contains(Raiders),
                $"pirates: a raider call: \"{call?.Title}\", answers {(call?.Answers == null ? "-" : string.Join(" / ", Array.ConvertAll(call.Answers, a => a.Label)))}. {call?.Text}");
            if (e == null) return;
            double d0 = (e.Pirate.Pos - at).Mag, restToRest = 2 * Math.Sqrt(d0 / (Career.PirateG * Ship.G0));
            Check(e.Lair == vesta && (e.Pirate.Vel - vel).Mag < 1e-9 && e.CatchSeconds >= Career.CatchMinS && e.CatchSeconds <= Career.CatchMaxS
                  && Math.Abs(e.CatchSeconds - restToRest) < 1,
                $"pirates: it waits at rest with Vesta, {(e.Pirate.Pos - qs.Pos(vesta, b.Jd)).Mag:N0} km off it and {d0:N0} km from the ship: catch {e.CatchSeconds / 3600:F2} h " +
                $"(D11 window {Career.CatchMinS / 3600:0}–{Career.CatchMaxS / 3600:0} h; from rest 2√(d/a) = {restToRest / 3600:F2} h)");
            Check(qs.Bodies.Contains(e.Proxy) && !qs.Bodies.Contains(e.Chase.StandIn) && e.Proxy.Name.StartsWith("Raider ") && e.Pirate.Plan == Plan.Pursue && e.Pirate.MaxG == Career.PirateG
                  && b.C.WarpCap == Career.ChaseWarp,
                $"pirates: the {e.Proxy.Name} is a {e.Pirate.MaxG:0} g {e.Pirate.Plan}, its proxy in the system and its stand-in out; warp cap {b.C.WarpCap:N0}×");
            Check(!b.C.MakeCall(Career.RouteKind.Pirate, b.Job, qs, b.Ship, b.Jd) && b.C.Call == call && b.C.Encounter == e, "pirates: one encounter at a time: a second raider call is refused");

            // with the chase on, a tick that would roll a call almost surely (1e6 s at RouteMeanDays) makes none
            b.C.RouteEvents = true;
            b.C.Answer(Career.RouteAnswer.Run, b.Jd);
            b.Ship.Target = vesta; b.Ship.Plan = Plan.Brachistochrone;
            b.C.Tick(qs, b.Ship, b.Jd, 1e6);
            b.Ship.Plan = Plan.None; b.Ship.Target = null;
            b.C.RouteEvents = false;
            Check(b.C.Call == null && b.C.Encounter == e, "pirates: while a raider gives chase, RouteTick makes no other call");

            // no start fits: closing on Vesta at 1,000 km/s from 10,000,000 km, a 1 g raider needs over a day to match
            var c = MakeBench(qs, qs.Pos(vesta, PursuitCheck.Jd0) + at.Norm * 1e7, vel - at.Norm * 1000);
            Check(!c.C.MakeCall(Career.RouteKind.Pirate, c.Job, qs, c.Ship, c.Jd) && c.C.Encounter == null && c.C.Call == null,
                "pirates: with no start inside the D11 window (a ship at 1,000 km/s), no raider call");

            // the old kinds keep their paths: an unknown kind or a rescue job makes no call, and Run does not answer cargo trouble
            var rescue = new Contract { Id = 900201, Kind = JobKind.Rescue, Title = "QA wreck", From = "Ceres", To = "Vesta", Key = "qa/pirate-rescue" };
            bool unknown = c.C.MakeCall((Career.RouteKind)99, c.Job, qs, c.Ship, c.Jd), onRescue = c.C.MakeCall(Career.RouteKind.Pirate, rescue, qs, c.Ship, c.Jd);
            bool trouble = c.C.MakeCall(Career.RouteKind.CargoTrouble, c.Job, qs, c.Ship, c.Jd);
            var tc = c.C.Call;
            c.C.Answer(Career.RouteAnswer.Run, c.Jd);
            bool kept = c.C.Call == tc;
            c.C.Answer(Career.RouteAnswer.No, c.Jd);
            Check(!unknown && !onRescue && trouble && kept && c.C.Call == null && c.Job.SpoilJd > 0 && tc.Answers.Length == 2 && tc.Answers[0].What == Career.RouteAnswer.Yes,
                $"pirates: an unknown kind ({unknown}) or a rescue job ({onRescue}) makes no call; cargo trouble ignores Run and takes No as before (spoil clock {(c.Job.SpoilJd - c.Jd) * 24:F0} h)");
            b.C.DropContact();
        }

        void PirateAnswerChecks(SolarSystem qs)
        {
            var (vesta, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            b.C.MakeCall(Career.RouteKind.Pirate, b.Job, qs, b.Ship, b.Jd);
            var e = b.C.Encounter;
            b.C.Answer(Career.RouteAnswer.Run, b.Jd);
            Check(e != null && b.C.Call == null && b.C.Encounter == e && qs.Bodies.Contains(e.Proxy), "pirates: Run closes the card and the chase goes on");

            // Divert, after Run closed the card: the nearest patrolled port by full-burn time from the ship's state
            Body asked = null;
            b.C.Divert = body => { asked = body; return true; };
            b.C.Answer(Career.RouteAnswer.Divert, b.Jd);
            Port best = null; double bestT = double.MaxValue; var times = new List<string>();
            foreach (var p in Career.Ports)
            {
                if (p.PatrolG <= 0) continue;
                var pb = qs[p.Body];
                var to = qs.Pos(pb, b.Jd) - b.Ship.Pos;
                var v = qs.Vel(pb, b.Jd) - b.Ship.Vel;
                double u = -D3.Dot(v, to.Norm), t = Ship.TripFrom(Math.Max(to.Mag - Ship.Standoff(pb), 0), b.Ship.AMax, u, (v + to.Norm * u).Mag).t;
                times.Add($"{p.Name} {t / 3600:F0} h");
                if (t < bestT) { bestT = t; best = p; }
            }
            Check(best != null && asked == qs[best.Body] && b.C.Encounter == e, $"pirates: Divert asks the scene for {asked?.Name ?? "nothing"} ({string.Join(", ", times)}); the chase goes on");
            b.C.Divert = null;
            b.C.Answer(Career.RouteAnswer.Divert, b.Jd);
            Check(b.C.Encounter == e && b.Heard(Career.Happening.RouteCall, "Can't divert"), "pirates: with no course set, Divert says it can't and the chase goes on");

            // Q6: give up takes GiveUpShare of the pay and the tonnes; no Abandon, no standing change; the raider leaves
            double pay0 = b.Job.Pay, t0 = b.Job.Tonnes;
            int failed = b.C.Failed, standing = b.C.Standing(Faction.Compact);
            b.C.Answer(Career.RouteAnswer.GiveUp, b.Jd);
            b.Tick(1);
            Check(b.Job.Pay == pay0 * (1 - Career.GiveUpShare) && b.Job.Tonnes == t0 * (1 - Career.GiveUpShare) && b.C.Active.Contains(b.Job) && b.C.Failed == failed
                  && b.C.Standing(Faction.Compact) == standing && b.C.Encounter == null && !qs.Bodies.Contains(e.Proxy) && b.C.WarpCap == 0 && b.Ship.Cargo == b.Job.Tonnes
                  && b.Said.Exists(s => s.What == Career.Happening.RaiderGone && s.Text.Contains(Raiders) && HavenNamed(s.Text)),
                $"pirates: give up: pay {pay0:N0} → {b.Job.Pay:N0} cr, {t0:N0} → {b.Job.Tonnes:N0} t ({b.Ship.Cargo:N0} t aboard), the job kept, the proxy gone, no warp cap ({b.Said.FindLast(s => s.What == Career.Happening.RaiderGone).Text})");

            // the two-button card (its Yes label is the give-up): No runs, Yes gives up
            b.C.MakeCall(Career.RouteKind.Pirate, b.Job, qs, b.Ship, b.Jd);
            var yes = b.C.Call?.Yes;
            b.C.Answer(false, b.Jd);
            bool ran = b.C.Call == null && b.C.Encounter != null;
            b.C.DropContact();
            b.C.MakeCall(Career.RouteKind.Pirate, b.Job, qs, b.Ship, b.Jd);
            double pay1 = b.Job.Pay;
            b.C.Answer(true, b.Jd);
            Check(ran && yes != null && yes.StartsWith("Give up") && b.C.Encounter == null && b.Job.Pay == pay1 * (1 - Career.GiveUpShare),
                $"pirates: on the two-button card No runs and Yes (\"{yes}\") gives up");
        }

        void PiratePatrolChecks(SolarSystem qs)
        {
            // Q1: in a patrol's reach there is no call: 100,000 km from Earth (the doc's case) and from Ceres, in the belt
            foreach (var (port, from) in new[] { ("Earth", "Earth orbit"), ("Ceres", "Ceres") })
            {
                var body = qs[port];
                var bp = qs.Pos(body, PursuitCheck.Jd0);
                var b = MakeBench(qs, bp + bp.Norm * 1e5, qs.Vel(body, PursuitCheck.Jd0), from: from);
                var patrol = Threat.PatrolAt(qs, b.Ship.Pos, b.Jd);
                double rate = b.C.PirateRate(qs, b.Ship, b.Job, b.Jd), presence = Threat.Presence(qs, b.Ship.Pos, b.Jd);
                Check(patrol?.Body == port && rate == 0 && !b.C.MakeCall(Career.RouteKind.Pirate, b.Job, qs, b.Ship, b.Jd) && b.C.Encounter == null,
                    $"pirates: 100,000 km from {port}, in the {patrol?.Name ?? "no"} patrol's reach (raider presence {presence:0.00}): rate {rate}, no raider call");
            }

            // and a chase that reaches one breaks off at the next tick
            var (vesta, at, vel) = Belt(qs);
            var c = MakeBench(qs, at, vel);
            var e = c.C.StartContact(c.Job, qs, c.Ship, c.Jd, at - at.Norm * 5e4, vel);
            c.Tick(1);
            bool on = c.C.Encounter == e;
            var ceres = qs["Ceres"];
            var cp = qs.Pos(ceres, c.Jd);
            c.Ship.Pos = cp + cp.Norm * 1e5; c.Ship.Vel = qs.Vel(ceres, c.Jd);
            c.Tick(1);
            Check(on && c.C.Encounter == null && !qs.Bodies.Contains(e.Proxy) && c.Said.Exists(s => s.What == Career.Happening.RaiderGone && s.Text.Contains("patrol at Ceres") && HavenNamed(s.Text)),
                $"pirates: a chase that comes in reach of the Ceres patrol breaks off ({c.Said.FindLast(s => s.What == Career.Happening.RaiderGone).Text})");
        }

        // the pirate kept alongside by hand: 1 km off, matched, the ship coasting; every update 30 s
        static void Alongside(Bench b, Encounter e, D3 off, double dvel = 0)
        {
            b.Ship.Step(b.Sys, b.Jd, 30, b.Sys.Sun);
            e.Pirate.Pos = b.Ship.Pos + off; e.Pirate.Vel = b.Ship.Vel + off.Norm * dvel;
            b.Tick(30);
        }

        void PirateBoardingChecks(SolarSystem qs)
        {
            var (vesta, at, vel) = Belt(qs);
            // Q4: 7200 s alongside boards: half the pay and half the tonnes, the job kept
            var b = MakeBench(qs, at, vel);
            var off = new D3(1, 0, 0);
            var e = b.C.StartContact(b.Job, qs, b.Ship, b.Jd, at + off, vel);
            double pay0 = b.Job.Pay, t0 = b.Job.Tonnes;
            int n = 0;
            bool capped = true;
            while (b.C.Encounter != null && n < 300) { Alongside(b, e, off); n++; if (b.C.Encounter != null) capped &= b.C.WarpCap == Career.CloseWarp && e.Dwell == 30 * n; }
            b.Tick(1);
            Check(n == (int)(Threat.BoardingSeconds / 30) && capped && b.Job.Pay == pay0 * (1 - Career.BoardShare) && b.Job.Tonnes == t0 * (1 - Career.BoardShare)
                  && b.C.Active.Contains(b.Job) && b.C.Failed == 0 && b.C.Standing(Faction.Compact) == 0 && b.Ship.Cargo == b.Job.Tonnes && b.C.DropWarp
                  && !qs.Bodies.Contains(e.Proxy) && b.Said.Exists(s => s.What == Career.Happening.Boarded && s.Text.Contains(Raiders) && HavenNamed(s.Text)),
                $"pirates: boarded after {n * 30} s alongside (warp cap {Career.CloseWarp:N0}× meanwhile): pay {pay0:N0} → {b.Job.Pay:N0} cr, {t0:N0} → {b.Job.Tonnes:N0} t, the job kept, no standing lost " +
                $"({b.Said.Find(s => s.What == Career.Happening.Boarded).Text})");

            // Q5: one update at 0.2 km/s in the middle resets the clock, and the boarding needs the full time again
            var c = MakeBench(qs, at, vel, JobKind.Passengers);
            var e2 = c.C.StartContact(c.Job, qs, c.Ship, c.Jd, at + off, vel);
            double cpay = c.Job.Pay;
            for (int i = 0; i < 120; i++) Alongside(c, e2, off);
            double mid = e2.Dwell;
            Alongside(c, e2, off, 0.2);
            double broken = e2.Dwell;
            int m = 0;
            while (c.C.Encounter != null && m < 300) { Alongside(c, e2, off); m++; }
            Check(mid == 3600 && broken == 0 && m == (int)(Threat.BoardingSeconds / 30) && c.Job.Pay == cpay * (1 - Career.BoardShare) && c.Job.People == 10,
                $"pirates: a break at 0.2 km/s after {mid:F0} s resets the clock ({broken:F0} s), then {m * 30} s more board; passengers lose pay only ({c.Job.Pay:N0} cr, {c.Job.People} aboard)");
        }

        void PirateEndChecks(SolarSystem qs)
        {
            var (vesta, at, vel) = Belt(qs);
            // Q7: dock first: the job delivers at full pay, the call drops and the raider breaks off
            var b = MakeBench(qs, at, vel);
            b.C.MakeCall(Career.RouteKind.Pirate, b.Job, qs, b.Ship, b.Jd);
            var e = b.C.Encounter;
            double cr0 = b.C.Credits, pay = b.Job.Pay;
            var vp = qs.Pos(vesta, b.Jd + 1.0 / 86400);
            var rad = (vp.Norm - vesta.Pole * D3.Dot(vp.Norm, vesta.Pole)).Norm;
            double r = Ship.Standoff(vesta);
            b.Ship.Pos = vp + rad * r; b.Ship.Vel = qs.Vel(vesta, b.Jd + 1.0 / 86400) + D3.Cross(vesta.Pole, rad).Norm * Math.Sqrt(vesta.GM / r);
            b.Tick(1);
            Check(e != null && b.C.Docked?.Name == "Vesta" && !b.C.Active.Contains(b.Job) && b.C.Credits - cr0 == pay && b.C.Encounter == null && b.C.Call == null
                  && !qs.Bodies.Contains(e.Proxy) && b.Said.Exists(s => s.What == Career.Happening.RaiderGone && s.Text.StartsWith("Docked at Vesta") && HavenNamed(s.Text)),
                $"pirates: dock first: docked at {b.C.Docked?.Name ?? "-"}, paid {b.C.Credits - cr0:N0} of {pay:N0} cr, the call and the raider gone ({b.Said.FindLast(s => s.What == Career.Happening.RaiderGone).Text})");

            // a landing (on Pallas, not a port) ends it too
            var c = MakeBench(qs, at, vel);
            var e2 = c.C.StartContact(c.Job, qs, c.Ship, c.Jd, at - at.Norm * 5e4, vel);
            c.Ship.Landed = qs["Pallas"];
            c.Tick(1);
            c.Ship.Landed = null;
            Check(c.C.Encounter == null && !qs.Bodies.Contains(e2.Proxy) && c.Said.Exists(s => s.What == Career.Happening.RaiderGone && s.Text.StartsWith("Landed on Pallas")),
                "pirates: landing ends the chase");

            // Q9: a save made mid-chase loads without it; the next update says so and names the haven
            var d = MakeBench(qs, at, vel);
            d.C.MakeCall(Career.RouteKind.Pirate, d.Job, qs, d.Ship, d.Jd);
            var e3 = d.C.Encounter;
            var json = JsonUtility.ToJson(d.C.ToSave(d.Ship));
            d.C.FromSave(JsonUtility.FromJson<CareerSave>(json), d.Ship);
            d.Tick(1);
            Check(e3 != null && json.Contains("\"contact\":true") && d.C.Encounter == null && d.C.Call == null && !qs.Bodies.Contains(e3.Proxy)
                  && d.Said.Exists(s => s.What == Career.Happening.RaiderGone && s.Text.Contains("not saved") && HavenNamed(s.Text)),
                $"pirates: a save and load mid-chase drops it ({d.Said.FindLast(s => s.What == Career.Happening.RaiderGone).Text})");
        }

        // Q2: a 1 g pirate 100,000 km behind on the line to Mars, at rest with the ship. A 1 g full burn (the autopilot, as the
        // Fast plan flies it) is never boarded in 12 h; the same start with the ship coasting is.
        void PirateEscapeChecks(SolarSystem qs)
        {
            var (vesta, at, vel) = Belt(qs);
            var mars = qs["Mars"];
            string Fly(bool burn, out bool boarded, out bool on, out double range)
            {
                var b = MakeBench(qs, at, vel, from: "Vesta", to: "Mars orbit");
                var dir = (qs.Pos(mars, b.Jd) - at).Norm;
                b.Ship.Att = Quaternion.LookRotation(Frame.U(dir), Frame.U(Frame.EclNorth));
                if (burn) { b.Ship.Target = mars; b.Ship.Plan = Plan.Brachistochrone; }
                var e = b.C.StartContact(b.Job, qs, b.Ship, b.Jd, at - dir * 1e5, vel);
                double t = 0, nextDom = 0, first = -1;
                Body dom = null;
                while (t < 12 * 3600 && b.C.Encounter != null)
                {
                    if (t >= nextDom) { dom = Ship.Dominant(qs, b.Ship.Pos, b.Jd); nextDom = t + 60; }
                    D3 p0 = b.Ship.Pos, v0 = b.Ship.Vel;
                    double j0 = b.Jd, h = (b.Jd + 10.0 / 86400 - b.Jd) * 86400;
                    b.Ship.Step(qs, j0, h, dom);
                    e.Advance(qs, j0, h, p0, v0, b.Ship.Vel);
                    b.Tick(h);
                    t += h;
                    if (first < 0 && e.Dwell > 0) first = t;
                }
                boarded = b.Said.Exists(s => s.What == Career.Happening.Boarded);
                on = b.C.Encounter == e;
                range = (e.Pirate.Pos - b.Ship.Pos).Mag;
                string what = $"{t / 3600:F1} h, range 100,000 → {range:N0} km, first alongside {(first < 0 ? "never" : $"{first:F0} s")}, ship '{b.Ship.Phase}', pirate '{e.Pirate.Phase}'";
                b.C.DropContact();
                return what;
            }
            string burnRun = Fly(true, out bool burnBoarded, out bool burnOn, out _);
            Check(!burnBoarded && burnOn, $"pirates: a 1 g full burn is not boarded by a 1 g raider in 12 h ({burnRun})");
            string coastRun = Fly(false, out bool coastBoarded, out _, out _);
            Check(coastBoarded, $"pirates: the control, the same start coasting, is boarded ({coastRun})");
        }
    }
}
