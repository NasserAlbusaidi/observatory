using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // 14. Pirates (Career.Pirates.cs, docs/design-world.md W4): the raider's call and its start (D11: far from the lair, a
    // patrolled lair, the departure half, never on the ship), the rate (D3), the jobs it works (D4) and their titles in the
    // card, the three answers, patrol break-off (Q1), escape by a full burn against a coasting control (Q2), boarding,
    // its edges and a broken contact (Q4, Q5, D10), give up (Q6), dock first (Q7), a landing, a save and load (Q9), the
    // texts with a singular faction name (D13), then in the scene the lockstep on both branches of the substep loop, the
    // proxy body, the warp cap (Q8), Divert, a load mid-chase, a landing, the two shells (14b), the W4 notes against the
    // note strip, and a load whose career save will not read. Headless parts run on a system of their own at the pursuit
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
        static double PirateA => Career.PirateG * Ship.G0;
        static string CareerKey => (string)typeof(OpenSystemScene).GetProperty("CareerKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);

        IEnumerator PirateChecks()
        {
            SolarSystem qs = null;
            if (Guard("system", () => qs = SolarSystem.Build()))
            {
                Guard("call and start", () => PirateCallChecks(qs));
                Guard("spawn", () => PirateSpawnChecks(qs));
                Guard("answers", () => PirateAnswerChecks(qs));
                Guard("patrol", () => PiratePatrolChecks(qs));
                Guard("boarding", () => PirateBoardingChecks(qs));
                Guard("ends", () => PirateEndChecks(qs));
                Guard("escape", () => PirateEscapeChecks(qs));
                Guard("names", () => PirateNameChecks(qs));
            }

            // the scene: a chase started by hand 1,000,000 km out from Vesta, the pirate 50,000 km off at rest with the ship
            Contract job = null;
            Encounter e = null;
            double dv0 = 0, shipDv0 = 0, span0 = 0;
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
                // sys.Vel differences the path 30 s either side, on jd's 40 µs grid: the stand-in's gate (pursuit check G4)
                Check(!R<bool>("Coasting") && lag == 0 && dp < 1e-6 && dv < PursuitCheck.GateVel && e.Pirate.DvUsed > 0,
                    $"pirates: in the scene at 1×, the ship in free flight on integrated gravity, the pirate flies the player's spans: its clock {lag:G3} s from the scene's, the proxy {dp:G3} km and {dv:G3} km/s (< {PursuitCheck.GateVel:E0}) off it, Δv {e.Pirate.DvUsed:F3} km/s ({e.Pirate.Phase})");
                Put("warpIdx", 6);
            });
            if (ok) yield return null;
            if (ok) ok = Guard("warp cap far", () =>
            {
                int w = Get<int>("warpIdx");
                double cap = career.WarpCap, ran = Get<double>("frameWarp");
                Check(cap == Career.ChaseWarp && WarpOf(w) <= cap && WarpOf(w + 1) > cap && ran <= cap * (1 + 1e-9),
                    $"pirates: 1M× asked during a chase {e.Range:N0} km off runs at {WarpOf(w):N0}× (cap {cap:N0}×; the frame ran at {ran:N0}×)");
                var p = e.Proxy;
                double lag = (e.Chase.SpanEnd - jd) * 86400, dp = (sys.Pos(p, jd) - e.Pirate.Pos).Mag, dv = (sys.Vel(p, jd) - e.Pirate.Vel).Mag;
                Check(R<bool>("Coasting") && lag == 0 && dp < 1e-6 && dv < PursuitCheck.GateVel,
                    $"pirates: at the {WarpOf(w):N0}× cap the ship coasts on its conic and the pirate flies those spans: its clock {lag:G3} s from the scene's, the proxy {dp:G3} km and {dv:G3} km/s (< {PursuitCheck.GateVel:E0}) off it");
                e.Pirate.Pos =ship.Pos + (ship.Pos - sys.Pos(sys["Vesta"], jd)).Norm * -5000; e.Pirate.Vel = ship.Vel;
                Put("warpIdx", 6);
            });
            if (ok) yield return null;
            if (ok) ok = Guard("warp cap close", () =>
            {
                int w = Get<int>("warpIdx");
                double ran = Get<double>("frameWarp");
                // the cap set before the frame's sim time reads the range then, so the first frame within CloseKm is held too
                Check(career.WarpCap == Career.CloseWarp && WarpOf(w) <= Career.CloseWarp && ran <= Career.CloseWarp * (1 + 1e-9),
                    $"pirates: the first frame within {Career.CloseKm:N0} km ({e.Range:N0} km) ran at {ran:N0}×; the cap is {career.WarpCap:N0}× and the warp {WarpOf(w):N0}×");
                Put("warpIdx", 0);
                var port = Career.NearestPatrol(sys, ship, jd, out _);
                career.Answer(Career.RouteAnswer.Divert, jd);
                Check(port != null && ship.Target == sys[port.Body] && ship.PlanActive && ship.Plan == Plan.Brachistochrone && Get<int>("cruiseIdx") == 0 && career.Encounter == e,
                    $"pirates: Divert targets {port?.Name ?? "-"}, the nearest patrolled port by trip time, and engages the Fast plan ({ship.Phase}); the chase goes on");
                // the plan's first frames only turn the ship (throttle 0): take the helm at full throttle, so the drive fires
                ship.Plan = Plan.None; ship.ThrottleCmd = 1;
                dv0 = e.Pirate.DvUsed; shipDv0 = ship.DvUsed;
            });
            // the drive firing: the substep loop integrates, and the pirate must fly those spans too
            for (int i = 0; ok && i < 3; i++) yield return null;
            if (ok) ok = Guard("scene lockstep under thrust", () =>
            {
                var p = e.Proxy;
                double lag = (e.Chase.SpanEnd - jd) * 86400, dp = (sys.Pos(p, jd) - e.Pirate.Pos).Mag, dv = (sys.Vel(p, jd) - e.Pirate.Vel).Mag;
                // the pirate's own Δv is not asked for: its guidance may burn nothing in three frames, and that is not the lockstep
                Check(!R<bool>("Coasting") && ship.Throttle > 0 && ship.DvUsed > shipDv0 && lag == 0 && dp < 1e-6 && dv < PursuitCheck.GateVel,
                    $"pirates: under thrust by hand ({ship.Phase}, throttle {ship.Throttle:F2}, the ship's Δv +{(ship.DvUsed - shipDv0) * 1000:G4} m/s) the pirate flies the integrated spans: " +
                    $"its clock {lag:G3} s from the scene's, the proxy {dp:G3} km and {dv:G3} km/s off it (pirate '{e.Pirate.Phase}', its Δv +{(e.Pirate.DvUsed - dv0) * 1000:G4} m/s)");
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0;
                // #99: no save mid-chase; the save from before it stays, and loading it still drops the chase (D8)
                string kept = PlayerPrefs.GetString(CareerKey);
                bool saved = s.SaveGame();
                Check(!saved && PlayerPrefs.GetString(CareerKey) == kept && career.Notes.Exists(n => n.Text == OpenSystemScene.ChaseNoSave),
                    $"pirates: Save mid-chase is refused and the save is left as it was ({(saved ? "saved" : OpenSystemScene.ChaseNoSave)})");
                s.LoadGame();
            });
            if (ok) { yield return null; yield return null; }
            if (ok) ok = Guard("scene load", () =>
            {
                Check(career.Encounter == null && !sys.Bodies.Exists(Career.IsRaider) && career.Notes.Exists(n => n.What == Career.Happening.RaiderGone && n.Text.Contains("not saved") && HavenNamed(n.Text)),
                    $"pirates: a load mid-chase drops it, the proxy goes, and a note says so ({career.Notes.Find(n => n.What == Career.Happening.RaiderGone).Text})");
            });

            // #99: Give up autosaves, so a reload can't undo the loss
            Contract given = null;
            string before = null;
            if (ok) ok = Guard("give-up autosave setup", () =>
            {
                Put("autosaveArmed", true); Put("warpIdx", 0);
                var vesta = sys["Vesta"];
                // the load put the ship back where it was saved: out in free flight again, so no dock ends the chase
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0; ship.Landed = null; ship.Hold = Hold.Off;
                ship.Pos = sys.Pos(vesta, jd) + sys.Pos(vesta, jd).Norm * 1e6; ship.Vel = sys.Vel(vesta, jd);
                given = new Contract { Id = 900304, Kind = JobKind.Cargo, Title = "QA water", From = "Ceres", To = "Vesta", Tonnes = 20, Pay = 20000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, Key = "qa/pirate-giveup" };
                career.Active.Add(given);
                career.StartContact(given, sys, ship, jd, ship.Pos - sys.Pos(vesta, jd).Norm * 5e4, sys.Vel(vesta, jd));
                before = PlayerPrefs.GetString(CareerKey);
                Put("autosaveDue", true);
            });
            if (ok) { yield return null; yield return null; }
            // an autosave due mid-chase waits: a save then would not hold the chase
            if (ok) ok = Guard("give-up autosave waits", () =>
            {
                Check(career.Encounter != null && Get<bool>("autosaveDue") && PlayerPrefs.GetString(CareerKey) == before,
                    $"pirates: an autosave due mid-chase waits for the chase to end (chase {(career.Encounter != null ? "on" : "over: " + career.Notes.FindLast(n => n.What == Career.Happening.RaiderGone).Text)}, due {Get<bool>("autosaveDue")})");
                career.Answer(Career.RouteAnswer.GiveUp, jd);
            });
            if (ok) { yield return null; yield return null; }
            if (ok) Guard("give-up autosave", () =>
            {
                var saved = JsonUtility.FromJson<CareerSave>(PlayerPrefs.GetString(CareerKey));
                var job = saved?.active?.Find(c => c.Key == "qa/pirate-giveup");
                Check(career.Encounter == null && PlayerPrefs.GetString(CareerKey) != before && job != null && Math.Abs(job.Pay - 20000 * (1 - Career.GiveUpShare)) < 1,
                    $"pirates: Give up autosaves: the save holds the job at {job?.Pay ?? double.NaN:N0} cr");
                career.Active.Remove(given);
            });

            // a landing in the scene: the substep loop rides the surface and flies no span; the same frame's career tick ends it
            if (ok) ok = Guard("scene landed setup", () =>
            {
                var vesta = sys["Vesta"];
                D3 pV = sys.Pos(vesta, jd), vV = sys.Vel(vesta, jd);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0; ship.Hold = Hold.Off;
                ship.Pos = pV + pV.Norm * 1e6; ship.Vel = vV;
                job = new Contract { Id = 900302, Kind = JobKind.Cargo, Title = "QA water", From = "Ceres", To = "Vesta", Tonnes = 20, Pay = 20000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, Key = "qa/pirate-scene" };
                career.Active.Add(job);
                e = career.StartContact(job, sys, ship, jd, ship.Pos - pV.Norm * 5e4, vV);
                span0 = e.Chase.SpanEnd;
                ship.Landed = sys["Pallas"]; // not a port: no dock ends it first
            });
            if (ok) yield return null;
            if (ok) Guard("scene landed", () =>
            {
                var note = career.Notes.Find(n => n.What == Career.Happening.RaiderGone && n.Text.StartsWith("Landed on Pallas"));
                Check(ship.Landed?.Name == "Pallas" && career.Encounter == null && !sys.Bodies.Contains(e.Proxy) && e.Chase.SpanEnd == span0 && note.Text != null,
                    $"pirates: landed in the scene ({ship.Phase}): that frame flies the pirate no span (its clock still at the start) and its career tick ends the chase and takes the proxy out ({note.Text})");
            });
            Guard("scene cleanup", () =>
            {
                ship.Landed = null;
                var vesta = sys["Vesta"];
                D3 pV = sys.Pos(vesta, jd);
                ship.Pos = pV + pV.Norm * 1e6; ship.Vel = sys.Vel(vesta, jd);
                career.DropContact();
                career.Active.RemoveAll(c => c.Key == "qa/pirate-scene");
                Put("warpIdx", 0);
            });
            yield return PirateShellChecks(); // 14b: the call card, the pill, the dot and the classic panel
            yield return RaiderModelChecks(); // 17x-17z: the raider's model (CareerQA.RaiderModel.cs)
            yield return MissileFxChecks();   // 18a-18f: the missiles on screen and the 1× hold (CareerQA.MissileFx.cs)
            yield return PirateNoteChecks(qs);
            yield return PirateNoSaveChecks();
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
            double d0 = (e.Pirate.Pos - at).Mag, restToRest = 2 * Math.Sqrt(d0 / PirateA);
            Check(e.Lair == vesta && (e.Pirate.Vel - vel).Mag < 1e-9 && e.CatchSeconds >= Career.CatchMinS && e.CatchSeconds <= Career.CatchMaxS
                  && Math.Abs(e.CatchSeconds - restToRest) < 1,
                $"pirates: it waits at rest with Vesta, {(e.Pirate.Pos - qs.Pos(vesta, b.Jd)).Mag:N0} km off it and {d0:N0} km from the ship: catch {e.CatchSeconds / 3600:F2} h " +
                $"(D11 window {Career.CatchMinS / 3600:0}–{Career.CatchMaxS / 3600:0} h; from rest 2√(d/a) = {restToRest / 3600:F2} h)");
            // D13: the faction's name comes from the table; the proxy is known by its type, not by its name
            Check(qs.Bodies.Contains(e.Proxy) && !qs.Bodies.Contains(e.Chase.StandIn) && Career.IsRaider(e.Proxy) && e.Proxy.Name == $"{e.Hull} ({Raiders})"
                  && call.Text.Contains($"the {e.Hull},") && e.Pirate.Plan == Plan.Pursue && e.Pirate.MaxG == Career.PirateG && b.C.WarpCap == Career.ChaseWarp,
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

            // D4, on this bench where a cargo call was just made: a survey job gets one, a rescue or an intercept job none
            b.C.DropContact();
            var survey = new Contract { Id = 900203, Kind = JobKind.Survey, Title = "Gravity survey of Vesta", From = "Ceres", To = "Vesta", Key = "qa/pirate-survey" };
            var rescue = new Contract { Id = 900201, Kind = JobKind.Rescue, Title = "QA wreck", From = "Ceres", To = "Vesta", Key = "qa/pirate-rescue" };
            var intercept = new Contract { Id = 900202, Kind = JobKind.Intercept, Title = "QA runner", From = "Ceres", To = "Vesta", Key = "qa/pirate-intercept" };
            bool onRescue = b.C.MakeCall(Career.RouteKind.Pirate, rescue, qs, b.Ship, b.Jd), onIntercept = b.C.MakeCall(Career.RouteKind.Pirate, intercept, qs, b.Ship, b.Jd);
            bool onSurvey = b.C.MakeCall(Career.RouteKind.Pirate, survey, qs, b.Ship, b.Jd);
            var se = b.C.Encounter;
            string surveyText = b.C.Call?.Text ?? "";
            Check(!onRescue && !onIntercept && onSurvey && se?.Job == survey,
                $"pirates: raiders work cargo, passengers and surveys: on the same bench a survey job gets a call ({onSurvey}), a rescue ({onRescue}) or an intercept ({onIntercept}) none");
            b.C.DropContact();

            // a job's title keeps its case in the card's sentence, but for a first letter that starts a lower-case word
            var pellets = new Contract { Id = 900204, Kind = JobKind.Cargo, Title = "D–He³ fuel pellets", From = "Ceres", To = "Vesta", Tonnes = 40, Pay = 20000, Key = "qa/pirate-pellets" };
            b.C.MakeCall(Career.RouteKind.Pirate, pellets, qs, b.Ship, b.Jd);
            string pelletText = b.C.Call?.Text ?? "";
            b.C.DropContact();
            string Part(string text, string start) { int i = text.IndexOf(start, StringComparison.Ordinal); return i < 0 ? "-" : text.Substring(i, Math.Min(text.Length - i, 60)); }
            Check(surveyText.Contains("of the pay for the gravity survey of Vesta.") && pelletText.Contains("of the D–He³ fuel pellets and of its pay"),
                $"pirates: job titles in the card: \"Gravity survey of Vesta\" reads \"{Part(surveyText, "of the pay for")}\", \"D–He³ fuel pellets\" reads \"{Part(pelletText, "of the D")}\"");

            // no start fits: closing on Vesta at 1,000 km/s from 10,000,000 km, a 1 g raider needs over a day to match
            var c = MakeBench(qs, qs.Pos(vesta, PursuitCheck.Jd0) + at.Norm * 1e7, vel - at.Norm * 1000);
            double fastest = double.PositiveInfinity, dist = (c.Ship.Pos - qs.Pos(vesta, c.Jd)).Mag, far = PirateA * Career.CatchMaxS * Career.CatchMaxS;
            for (int i = 0; i <= 400; i++)
            {
                double sAlong = Career.DockRange(vesta) + (dist + far - Career.DockRange(vesta)) * i / 400;
                fastest = Math.Min(fastest, Career.CatchSeconds(qs.Pos(vesta, c.Jd) + at.Norm * sAlong - c.Ship.Pos, vel - c.Ship.Vel, PirateA));
            }
            Check(!c.C.MakeCall(Career.RouteKind.Pirate, c.Job, qs, c.Ship, c.Jd) && c.C.Encounter == null && c.C.Call == null && fastest > Career.CatchMaxS && fastest >= 1000 / PirateA,
                $"pirates: with no start inside the D11 window, no raider call: at 1,000 km/s the quickest catch from any start on the line is {fastest / 3600:F1} h (no less than u/a = {1000 / PirateA / 3600:F1} h)");

            // the old kinds keep their paths: an unknown kind makes no call, and Run does not answer cargo trouble
            bool unknown = c.C.MakeCall((Career.RouteKind)99, c.Job, qs, c.Ship, c.Jd);
            bool trouble = c.C.MakeCall(Career.RouteKind.CargoTrouble, c.Job, qs, c.Ship, c.Jd);
            var tc = c.C.Call;
            c.C.Answer(Career.RouteAnswer.Run, c.Jd);
            bool kept = c.C.Call == tc;
            c.C.Answer(Career.RouteAnswer.No, c.Jd);
            Check(!unknown && trouble && kept && c.C.Call == null && c.Job.SpoilJd > 0 && tc.Answers.Length == 2 && tc.Answers[0].What == Career.RouteAnswer.Yes,
                $"pirates: an unknown kind makes no call ({unknown}); cargo trouble ignores Run and takes No as before (spoil clock {(c.Job.SpoilJd - c.Jd) * 24:F0} h)");
        }

        // D11's start where the old 5 % grid stepped over it, and where it must keep out of a patrol's reach
        void PirateSpawnChecks(SolarSystem qs)
        {
            var (vesta, at, vel) = Belt(qs);
            // 100,000,000 km out from Vesta, at rest with it (a slow belt coast): the 2–6 h ring is about 1e6 km wide
            var pV = qs.Pos(vesta, PursuitCheck.Jd0);
            var f = MakeBench(qs, pV + pV.Norm * 1e8, vel, from: "Vesta", to: "Vesta");
            bool made = f.C.MakeCall(Career.RouteKind.Pirate, f.Job, qs, f.Ship, f.Jd);
            var e = f.C.Encounter;
            double d0 = e != null ? (e.Pirate.Pos - f.Ship.Pos).Mag : double.NaN, fromRest = 2 * Math.Sqrt(d0 / PirateA);
            Check(made && e?.Lair == vesta && e.CatchSeconds >= Career.CatchMinS && e.CatchSeconds <= Career.CatchMaxS && Math.Abs(e.CatchSeconds - fromRest) < 1
                  && Threat.PatrolAt(qs, f.Ship.Pos, f.Jd) == null,
                $"pirates: 1e8 km from Vesta at rest with it a raider still finds a start: {d0:N0} km from the ship, catch {(e?.CatchSeconds ?? double.NaN) / 3600:F2} h (2√(d/a) = {fromRest / 3600:F2} h)");

            // 10,000,000 km out from Vesta, at rest with it along the line but 190 km/s across it: the drift alone (w / a,
            // 5.4 h) nearly fills the window, so the D11 starts lie within ~12,000 km of the ship and the ship's own point
            // fits. The grid lands on that point (72 steps from dist − far); a start there would be a raider on the hull
            var side = D3.Cross(pV.Norm, vesta.Pole).Norm;
            var k = MakeBench(qs, pV + pV.Norm * 1e7, vel + side * 190, from: "Vesta", to: "Vesta");
            double onHull = Career.CatchSeconds(pV.Norm * -1e-3, vel - k.Ship.Vel, PirateA);
            made = k.C.MakeCall(Career.RouteKind.Pirate, k.Job, qs, k.Ship, k.Jd);
            e = k.C.Encounter;
            double dk = e != null ? (e.Pirate.Pos - k.Ship.Pos).Mag : double.NaN;
            Check(onHull >= Career.CatchMinS && onHull <= Career.CatchMaxS && (made ? e != null && dk >= Career.InterceptKm : e == null),
                $"pirates: 190 km/s across the line, a start on the ship itself would fit (catch {onHull / 3600:F2} h), but none is closer than {Career.InterceptKm:N0} km: " +
                (made ? $"a start {dk:N0} km off, catch {e.CatchSeconds / 3600:F2} h" : "no call"));

            // the departure half, a patrolled lair: 300,000 km out from Ceres, just outside its patrol's reach, on a job
            // from Ceres. The raider waits at rest with Ceres where no patrol reaches it, not off the port
            var ceres = qs["Ceres"];
            var pC = qs.Pos(ceres, PursuitCheck.Jd0);
            var g = MakeBench(qs, pC + pC.Norm * 3e5, qs.Vel(ceres, PursuitCheck.Jd0));
            made = g.C.MakeCall(Career.RouteKind.Pirate, g.Job, qs, g.Ship, g.Jd);
            e = g.C.Encounter;
            double off = e != null ? (e.Pirate.Pos - qs.Pos(ceres, g.Jd)).Mag : double.NaN;
            Check(made && Threat.PatrolAt(qs, g.Ship.Pos, g.Jd) == null && e?.Lair == ceres && Threat.PatrolAt(qs, e.Pirate.Pos, g.Jd) == null
                  && e.CatchSeconds >= Career.CatchMinS && e.CatchSeconds <= Career.CatchMaxS,
                $"pirates: leaving Ceres, the raider waits {off:N0} km off it, out of the {Career.Factions[(int)Faction.Compact].Name} patrol's reach: catch {(e?.CatchSeconds ?? double.NaN) / 3600:F2} h");

            // the departure half, a port with no patrol: on a job from Vesta to Mars, 1,000,000 km out from Vesta
            var h = MakeBench(qs, at, vel, from: "Vesta", to: "Mars orbit");
            made = h.C.MakeCall(Career.RouteKind.Pirate, h.Job, qs, h.Ship, h.Jd);
            Check(made && h.C.Encounter?.Lair == vesta && h.C.TargetOf(h.Job) == qs["Mars"],
                $"pirates: leaving Vesta for Mars the raider waits at the posting port ({h.C.Encounter?.Lair?.Name ?? "-"}), not at the job's target");
        }

        void PirateAnswerChecks(SolarSystem qs)
        {
            var (vesta, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            b.C.MakeCall(Career.RouteKind.Pirate, b.Job, qs, b.Ship, b.Jd);
            var e = b.C.Encounter;
            var card = b.C.Call;

            // a dry tank: Divert says so and the card stays up (the plan would engage and drop at the next update)
            Body asked = null;
            b.C.Divert = body => { asked = body; return true; };
            b.Ship.Fuel = 0;
            b.C.Answer(Career.RouteAnswer.Divert, b.Jd);
            b.Ship.Fuel = 200;
            Check(card != null && b.C.Call == card && b.C.Encounter == e && asked == null && b.Heard(Career.Happening.RouteCall, "tank is dry"),
                "pirates: with a dry tank Divert says so, asks the scene for nothing and leaves the card up");

            b.C.Answer(Career.RouteAnswer.Run, b.Jd);
            Check(e != null && b.C.Call == null && b.C.Encounter == e && qs.Bodies.Contains(e.Proxy) && b.Heard(Career.Happening.RouteCall, $"{Career.ChaseWarp:N0}×"),
                $"pirates: Run closes the card and the chase goes on; its note says time is held to {Career.ChaseWarp:N0}×");

            // Divert, after Run closed the card: the nearest patrolled port by full-burn time from the ship's state
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

            // and one worked by hand: at rest 100,000 km from Ceres, Ceres is first, a rest-to-rest burn of 2√(d/a)
            var ceres = qs["Ceres"];
            var pC = qs.Pos(ceres, PursuitCheck.Jd0);
            var near = new Ship { Pos = pC + pC.Norm * 1e5, Vel = qs.Vel(ceres, PursuitCheck.Jd0), MaxG = 1 };
            var port = Career.NearestPatrol(qs, near, PursuitCheck.Jd0, out double secs);
            double hand = 2 * Math.Sqrt((1e5 - Ship.Standoff(ceres)) / near.AMax);
            Check(port?.Body == "Ceres" && Math.Abs(secs - hand) < 1, $"pirates: 100,000 km from Ceres at rest, Divert's port is {port?.Name ?? "-"} in {secs:F0} s (2√(d/a) = {hand:F0} s)");

            // Q6: give up takes GiveUpShare of the pay and the tonnes; no Abandon, no standing change; the raider leaves
            double pay0 = b.Job.Pay, t0 = b.Job.Tonnes;
            int failed = b.C.Failed, standing = b.C.Standing(Faction.Compact);
            b.C.Answer(Career.RouteAnswer.GiveUp, b.Jd);
            b.Tick(1);
            Check(b.Job.Pay == pay0 * (1 - Career.GiveUpShare) && b.Job.Tonnes == t0 * (1 - Career.GiveUpShare) && b.C.Active.Contains(b.Job) && b.C.Failed == failed
                  && b.C.Standing(Faction.Compact) == standing && b.C.Encounter == null && !qs.Bodies.Contains(e.Proxy) && b.C.WarpCap == 0 && b.Ship.Cargo == b.Job.Tonnes
                  && b.Said.Exists(s => s.What == Career.Happening.RaiderGone && s.Text.StartsWith($"Gave up {t0 * Career.GiveUpShare:N1} t and {pay0 * Career.GiveUpShare:N0} cr: the raider goes back to") && HavenNamed(s.Text)),
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
            Check(on && c.C.Encounter == null && !qs.Bodies.Contains(e.Proxy) && c.Said.Exists(s => s.What == Career.Happening.RaiderGone && s.Text.StartsWith("Ceres patrol in reach: the raider breaks off") && HavenNamed(s.Text)),
                $"pirates: a chase that comes in reach of the Ceres patrol breaks off ({c.Said.FindLast(s => s.What == Career.Happening.RaiderGone).Text})");
        }

        // the pirate kept alongside by hand: off the ship, matched (or dvel along off), after the ship's own 30 s step. Its
        // magazine is empty: these are W4's boarding checks, and 4a's missiles are section 17's (CareerQA.Defense.cs)
        static void Alongside(Bench b, Encounter e, D3 off, double dvel = 0)
        {
            e.Magazine = 0;
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
                  && !qs.Bodies.Contains(e.Proxy) && b.Said.Exists(s => s.What == Career.Happening.Boarded && s.Text.StartsWith($"Boarded: the raider took {t0 * Career.BoardShare:N1} t and {pay0 * Career.BoardShare:N0} cr back to") && HavenNamed(s.Text)),
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

            // D10's edges: alongside is inside InterceptKm and InterceptKms, at any throttle, and a NaN is never alongside
            var d = MakeBench(qs, at, vel);
            var e3 = d.C.StartContact(d.Job, qs, d.Ship, d.Jd, at + off, vel);
            var dwells = new List<double>();
            foreach (var (o, dv) in new[] { (Career.InterceptKm - 1, 0.0), (Career.InterceptKm + 1, 0.0), (1.0, Career.InterceptKms * 0.99), (1.0, Career.InterceptKms * 1.01) })
            {
                Alongside(d, e3, off * o, dv);
                dwells.Add(e3.Dwell);
            }
            d.Ship.ThrottleCmd = 1;
            for (int i = 0; i < 3; i++) Alongside(d, e3, off);
            double burning = d.Ship.Throttle, underBurn = e3.Dwell;
            d.Ship.ThrottleCmd = 0;
            Alongside(d, e3, new D3(double.NaN, 0, 0));
            double nan = e3.Dwell;
            Check(dwells[0] == 30 && dwells[1] == 0 && dwells[2] == 30 && dwells[3] == 0 && burning > 0 && underBurn == 90 && nan == 0 && d.C.Encounter == e3,
                $"pirates: alongside at {Career.InterceptKm - 1:N0} km ({dwells[0]:F0} s) but not at {Career.InterceptKm + 1:N0} km ({dwells[1]:F0} s), at {Career.InterceptKms * 0.99 * 1000:F0} m/s ({dwells[2]:F0} s) " +
                $"but not at {Career.InterceptKms * 1.01 * 1000:F0} m/s ({dwells[3]:F0} s); the clock runs under a burn (throttle {burning:F2}: {underBurn:F0} s) and a NaN range resets it ({nan:F0} s)");
            d.C.DropContact();
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
            // and into a career that never had the chase (quit, start again, Continue): only the saved flag can say so
            var f = MakeBench(qs, at, vel);
            f.C.FromSave(JsonUtility.FromJson<CareerSave>(json), f.Ship);
            f.Tick(1);
            Check(f.C.Encounter == null && f.Heard(Career.Happening.RaiderGone, "not saved"),
                $"pirates: the same save loaded into a fresh career says the chase was not saved ({f.Said.FindLast(s => s.What == Career.Happening.RaiderGone).Text})");
        }

        // Q2: a 1 g pirate 100,000 km behind on the line to Mars, at rest with the ship. A 1 g full burn (the autopilot, as the
        // Fast plan flies it) is never boarded, and the raider breaks off once it can't catch it (#98); the same start with the
        // ship coasting is boarded. The burn's proxy, read 4 h
        // ahead, goes on straight: it holds the last span's acceleration for 60 s only. The drive's g falls with mass, so the
        // 1 g burn is the fast (3 g) drive held to 1 g; the stock drive with the load aboard makes less than 1 g and is caught.
        void PirateEscapeChecks(SolarSystem qs)
        {
            var (vesta, at, vel) = Belt(qs);
            var mars = qs["Mars"];
            string Fly(bool burn, out bool boarded, out bool outrun, out double range, out double ahead, out double ended, bool stock = false)
            {
                var b = MakeBench(qs, at, vel, from: "Vesta", to: "Mars orbit");
                if (!stock) { b.C.Build.pick[(int)Slot.Drive] = 2; b.C.Apply(b.Ship); }
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
                outrun = b.Heard(Career.Happening.RaiderGone, "Outrun");
                ended = t;
                range = (e.Pirate.Pos - b.Ship.Pos).Mag;
                // 4 h on: off the straight line from now by at most a·60 s·t, where the span's a·t²/2 would be ~1e6 km
                ahead = (qs.PosAt(e.Proxy, e.Chase.SpanEnd + 4.0 / 24) - (e.Pirate.Pos + e.Pirate.Vel * (4 * 3600))).Mag;
                string what = $"{b.Ship.AccelG:F2} g at the end, {t / 3600:F1} h, range 100,000 → {range:N0} km, first alongside {(first < 0 ? "never" : $"{first:F0} s")}, ship '{b.Ship.Phase}', pirate '{e.Pirate.Phase}'";
                b.C.DropContact();
                return what;
            }
            string burnRun = Fly(true, out bool burnBoarded, out bool burnOutrun, out _, out double burnAhead, out double burnEnd);
            Check(!burnBoarded && burnOutrun && burnEnd >= Career.GiveUpS && burnEnd < Career.GiveUpS + 1800,
                $"pirates: a 1 g full burn is not boarded by a 1 g raider, which breaks off {Career.GiveUpS / 3600:0} h after its catch goes out of reach ({burnRun})");
            double bound = 1.01 * PirateA * 60 * 4 * 3600;
            Check(burnAhead <= bound, $"pirates: the proxy read 4 h ahead is {burnAhead:N0} km off the straight line from the pirate's state (≤ {bound:N0} km; the span's quadratic would be {0.5 * PirateA * Math.Pow(4 * 3600, 2):N0} km)");
            string coastRun = Fly(false, out bool coastBoarded, out _, out _, out _, out _);
            Check(coastBoarded, $"pirates: the control, the same start coasting, is boarded ({coastRun})");
            string stockRun = Fly(true, out bool stockBoarded, out _, out _, out _, out _, stock: true);
            Check(stockBoarded, $"pirates: the stock drive's full burn with the load aboard is under 1 g, and a 1 g raider boards it ({stockRun})");
        }

        // D13: every text built around the faction's name, made with a singular name in the style of the other factions'.
        // None may put a possessive, an article or a plural verb on it, or lower its case. The table's own name goes back
        // in a finally, so no later check sees the test name.
        void PirateNameChecks(SolarSystem qs)
        {
            var info = Career.Factions[(int)Faction.Raiders];
            string was = info.Name, name = "Black Banner";
            var texts = new List<string>();
            string title = null, kind = null, warn = null, incoming = null;
            try
            {
                info.Name = name;
                var (vesta, at, vel) = Belt(qs);
                var b = MakeBench(qs, at, vel);
                b.C.MakeCall(Career.RouteKind.Pirate, b.Job, qs, b.Ship, b.Jd);
                var e = b.C.Encounter;
                title = b.C.Call?.Title;
                texts.Add(title); texts.Add(b.C.Call?.Text); texts.Add(e?.Proxy.Name);
                if (e != null)
                {
                    kind = R<string>("CraftKind", e.Proxy);
                    warn = (string)typeof(OpenSystemScene).GetMethod("ChaseWarn", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { e.Hull, true });
                    texts.Add(kind); texts.Add(warn);
                }
                b.C.Divert = _ => true;
                b.C.Answer(Career.RouteAnswer.Run, b.Jd);
                b.C.Answer(Career.RouteAnswer.Divert, b.Jd);
                b.C.Answer(Career.RouteAnswer.GiveUp, b.Jd);
                foreach (var (_, text) in b.Said) { texts.Add(text); if (text.StartsWith("Incoming: ")) incoming = text; }
                texts.Add(Career.OffNote(Career.BreakOff.Patrol, "Ceres", "Pallas")); texts.Add(Career.OffNote(Career.BreakOff.Outrun, null, "Pallas")); texts.Add(Career.OffNote(Career.BreakOff.Lost, null, "Pallas")); texts.Add(Career.BoardedNote(20, 10000, "Pallas")); texts.Add(Career.LostNote("Pallas"));
            }
            finally { info.Name = was; }
            var bad = texts.FindAll(t => t == null || t.Contains(name + "'") || t.Contains(name.ToLowerInvariant()) || Regex.IsMatch(t, $@"\b[Aa]n? {name}")
                                         || Regex.IsMatch(t, $@"{name} (break|take|have|are|were|give)\b") || Regex.IsMatch(t, @"\b[Tt]hey\b") || t.Contains(" break off"));
            int named = texts.FindAll(t => t != null && t.Contains(name)).Count;
            Check(bad.Count == 0 && named >= 6 && title?.Contains(name) == true && kind?.Contains(name) == true && warn?.Contains(name) == true && incoming?.Contains(name) == true,
                $"pirates: D13 with a singular name, \"{name}\": {texts.Count} texts, {named} name it, none with a possessive, an indefinite article, a plural verb or in lower case: " +
                $"\"{title}\" / \"{incoming}\" / \"{kind}\" / \"{warn}\"" + (bad.Count > 0 ? "; wrong: " + string.Join(" | ", bad.ConvertAll(t => t ?? "(none)")) : ""));
        }

        // D8 on the path with no save that reads: a load whose career save will not read starts a new career, and that one
        // still says the chase was not saved. The career save is put back and loaded again after.
        IEnumerator PirateNoSaveChecks()
        {
            string key = null, kept = null;
            bool ok = Guard("no-save setup", () =>
            {
                key = (string)typeof(OpenSystemScene).GetProperty("CareerKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
                career.DropContact();
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0; ship.Landed = null;
                s.SaveGame();
                kept = PlayerPrefs.GetString(key);
                var vesta = sys["Vesta"];
                var job = new Contract { Id = 900303, Kind = JobKind.Cargo, Title = "QA water", From = "Ceres", To = "Vesta", Tonnes = 20, Pay = 20000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, Key = "qa/pirate-nosave" };
                career.StartContact(job, sys, ship, jd, ship.Pos - sys.Pos(vesta, jd).Norm * 5e4, sys.Vel(vesta, jd));
                PlayerPrefs.SetString(key, "not a career save");
                s.LoadGame();
            });
            if (ok) { yield return null; yield return null; }
            if (ok) Guard("no-save load", () =>
            {
                var note = career.Notes.Find(n => n.What == Career.Happening.RaiderGone && n.Text.Contains("not saved"));
                Check(career.Encounter == null && !sys.Bodies.Exists(Career.IsRaider) && note.Text != null && HavenNamed(note.Text),
                    $"pirates: a load mid-chase whose career save will not read starts a new career, and it says the chase was not saved ({note.Text})");
            });
            Guard("no-save restore", () =>
            {
                if (key == null || kept == null) return;
                PlayerPrefs.SetString(key, kept);
                s.LoadGame();
            });
        }
    }
}
