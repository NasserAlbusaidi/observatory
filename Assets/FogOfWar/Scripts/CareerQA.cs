using System;
using System.Collections;
using System.IO;
using System.Reflection;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // Unattended career run: `FogOfWar.exe -fowcareer <dir>` plays the first jobs of a new career through the autopilot
    // and logs credits, reaction mass and trip estimates against what the flight really used. Screenshots unless the
    // player runs in -batchmode. Writes career.txt and quits.
    public class CareerQA : MonoBehaviour
    {
        string dir;
        StreamWriter log;
        FogOfWarApp app;
        OpenSystemScene s;
        int fails, exceptions;
        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;
        void OnLog(string msg, string stack, LogType type) { if (type == LogType.Exception || type == LogType.Error) { exceptions++; log?.WriteLine("    [" + type + "] " + msg); } }
        float traceEvery = 2; int moonWarp = 3;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public static void StartIfRequested(FogOfWarApp app)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fowcareer");
            if (i < 0 || i + 1 >= args.Length) return;
            app.gameObject.AddComponent<CareerQA>().dir = args[i + 1];
        }

        T Get<T>(string field) => (T)s.GetType().GetField(field, F).GetValue(s);
        void Put(string field, object v) => s.GetType().GetField(field, F).SetValue(s, v);
        void Call(string method, params object[] a)
        {
            foreach (var m in s.GetType().GetMethods(F)) if (m.Name == method && m.GetParameters().Length == a.Length) { m.Invoke(s, a); return; }
            throw new MissingMethodException(method);
        }
        Ship ship => Get<Ship>("ship");
        SolarSystem sys => Get<SolarSystem>("sys");
        double jd => Get<double>("jd");
        Career career => s.Career;
        static string SaveKey => (string)typeof(OpenSystemScene).GetField("SaveKey", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        void Check(bool ok, string what) { if (!ok) fails++; log.WriteLine((ok ? "  PASS " : "  FAIL ") + what); }

        void State(string tag) => log.WriteLine($"[{tag}] {Frame.UtcFromTdb(jd):yyyy-MM-dd HH:mm}  credits {career.Credits:N0}  fuel {ship.Fuel:N2} t  mass {ship.Mass:N1} t  " +
            $"docked {career.Docked?.Name ?? "-"}  phase {ship.Phase}  target {ship.Target?.Name}  jobs {career.Active.Count}  board {career.Board.Count}");

        IEnumerator Shot(string name)
        {
            if (Application.isBatchMode) yield break;
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
        }

        // Fly an engaged plan at the given warp until the predicate holds or the real-time budget runs out.
        IEnumerator Fly(Func<bool> done, int warp, float budget)
        {
            Put("warpIdx", warp);
            float t0 = Time.realtimeSinceStartup;
            float next = 0;
            while (!done() && Time.realtimeSinceStartup - t0 < budget)
            {
                if (Time.realtimeSinceStartup - t0 > next)
                {
                    next += traceEvery;
                    var dom = Ship.Dominant(sys, ship.Pos, jd);
                    log.WriteLine($"    t+{(Time.realtimeSinceStartup - t0):F0}s  {ship.Phase}  thr {ship.Throttle:F2}  near {dom.Name} {(ship.Pos - sys.Pos(dom, jd)).Mag:N0} km  " +
                                  $"to target {(ship.Pos - sys.Pos(ship.Target, jd)).Mag:N0} km  vrel {(ship.Vel - sys.Vel(ship.Target, jd)).Mag:F2} km/s  fuel {ship.Fuel:F2}");
                }
                yield return null;
            }
            Put("warpIdx", 0);
        }

        IEnumerator Trip(Body to, int cruise, string tag, Func<bool> done, int warp = 5, float budget = 90)
        {
            Call("SetTarget", to);
            Put("cruiseIdx", cruise);
            double d = Math.Max((sys.Pos(to, jd) - ship.Pos).Mag - Ship.Standoff(to), 0);
            var (u, w) = ship.RelToTarget(sys, jd);
            Career.Trip(d, ship.AMax, new[] { 1, 0.5, 0.25 }[cruise], ship.Mass, out double estT, out double estDv, out double estFuel, u, w);
            double jd0 = jd, fuel0 = ship.Fuel, dv0 = ship.DvUsed;
            Put("selectedPlan", Plan.Brachistochrone);
            Call("TogglePlan");
            // QA-061: these trips are meant to fly; if the Δv guard asks first, say so and go anyway
            if (!ship.PlanActive && ship.Phase.StartsWith("fuel short"))
            {
                log.WriteLine($"  {tag}: Δv guard asked first (est {estDv:F0} km/s × 1.1, {Career.DvLeft(ship):F0} left)");
                Call("TogglePlan");
            }
            yield return null;
            yield return Fly(done, warp, budget);
            log.WriteLine($"  {tag}: est {estT / 3600:F1} h, Δv {estDv:F0} km/s, {estFuel:F2} t  |  flew {(jd - jd0) * 24:F1} h, Δv {ship.DvUsed - dv0:F0} km/s, {fuel0 - ship.Fuel:F2} t  cap {ship.CruiseCap:F0} km/s");
        }

        // QA-041: the long Ganymede-to-Vesta intercept taken from Earth with a 200 t tank, flown at 100k×.
        IEnumerator Repro041()
        {
            Contract ic = null;
            for (int seed = 1; seed < 3000 && ic == null; seed++)
            {
                var cand = career.Intercept(new System.Random(seed), sys, jd);
                if (cand != null && cand.RunFrom == "Ganymede" && cand.RunTo == "Vesta") ic = cand;
            }
            log.WriteLine($"job: {ic?.Title} pay {ic?.Pay}");
            if (ic == null) { log.Close(); Application.Quit(); yield break; }
            career.Accept(ic, jd);
            log.WriteLine($"accepted: {career.Active.Contains(ic)}");
            if (!career.Active.Contains(ic)) { log.Close(); Application.Quit(); yield break; }
            Call("SetTarget", sys[ic.To]);
            Put("selectedPlan", Plan.Brachistochrone);
            Call("TogglePlan");
            Put("warpIdx", 5);
            float t0 = Time.realtimeSinceStartup, next = 0;
            double cr = career.Credits;
            while (Time.realtimeSinceStartup - t0 < 90)
            {
                var b = sys[ic.To];
                if (Time.realtimeSinceStartup - t0 > next || career.Credits != cr)
                {
                    next += 1; cr = career.Credits;
                    log.WriteLine($"  {Frame.UtcFromTdb(jd):MM-dd HH:mm} warp {Get<int>("warpIdx")} cr {career.Credits:N0} fuel {ship.Fuel:F1} phase {ship.Phase} active {career.Active.Contains(ic)} dwell {ic.Dwell:F0}" +
                        (b != null ? $" dist {(ship.Pos - sys.Pos(b, jd)).Mag:N0} vrel {(ship.Vel - sys.Vel(b, jd)).Mag:F2}" : " (runner gone)") + $" sun {ship.Pos.Mag / Frame.AU:F2} AU");
                }
                yield return null;
            }
            log.Close(); Application.Quit();
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, "career.txt")) { AutoFlush = true };
            app = GetComponent<FogOfWarApp>();
            int ti = Array.IndexOf(Environment.GetCommandLineArgs(), "-fowtrace"); if (ti >= 0) { traceEvery = 0.25f; moonWarp = int.Parse(Environment.GetCommandLineArgs()[ti + 1]); }
            app.InputLocked = true; Controls.Locked = true;
            yield return new WaitForSecondsRealtime(1f);
            app.Restart(app.GameIndex);
            yield return new WaitForSecondsRealtime(1.5f);
            s = (OpenSystemScene)app.Current;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fowqa041") >= 0) { yield return Repro041(); yield break; }
            State("new career");
            Check(career.Docked?.Name == "Earth orbit", "new career starts docked at Earth orbit");
            Check(!ship.UnlimitedFuel && Math.Abs(ship.Fuel - 200) < 1e-6, "tank full, reaction mass is limited");
            Check(career.Active.Count == 1 && career.Active[0].Tutorial, "first job is the Luna run");
            yield return Shot("c01-start");

            // 0. the first flight is the first job: target, Engage, time compression, payday (audit #7). A throttle blip
            // at the home port undocks and redocks: a new career must not autosave over the old save for that
            string oldSave = PlayerPrefs.GetString(SaveKey);
            Check(s.TutorialText != null && s.TutorialText.StartsWith("First flight (1/3)"), $"tutorial starts on a new career at the target step: {s.TutorialText?.Substring(0, Math.Min(40, s.TutorialText.Length))}");
            yield return Shot("c00-tutorial");
            Controls.Tap(Controls.Action.ThrottleFull); yield return new WaitForSecondsRealtime(0.4f);
            Controls.Tap(Controls.Action.ThrottleCut); yield return new WaitForSecondsRealtime(0.4f);
            Check(PlayerPrefs.GetString(SaveKey) == oldSave, "a throttle blip at Earth does not autosave over the old save");

            // 1. the first job, full burn to the Moon
            double c0 = career.Credits;
            log.WriteLine($"  Moon vs Earth seen from the ship: {Vector3.Angle(Frame.U(sys.Pos(sys.Moon, jd) - ship.Pos), Frame.U(sys.Pos(sys.Earth, jd) - ship.Pos)):F0} deg");
            yield return Trip(sys.Moon, 0, "Earth → Moon full burn", () => career.Docked?.Name == "Luna" && ship.Phase == "in orbit", moonWarp, 60);
            State("at Luna");
            Check(career.Docked?.Name == "Luna", "docked at Luna after the autopilot parks");
            Check(PlayerPrefs.GetString(SaveKey) != oldSave && PlayerPrefs.GetString(SaveKey).Length > 0, "reaching Luna autosaves");
            Check(career.Credits - c0 >= 3000, $"first job paid ({career.Credits - c0:N0} cr)");
            yield return null;
            Check(s.TutorialText != null && s.TutorialText.StartsWith("First job paid"), "the first flight ends on the payday card, which offers the hand-flying practice");
            yield return Shot("c02-luna-docked");

            // 0b. the hand-flying practice from the payday card, at Luna, done the way a player does it
            Put("warpIdx", 0);
            Call("StartPractice");
            yield return null; yield return null;
            Check(s.TutorialText != null && s.TutorialText.StartsWith("Hand-flying practice (1/8)") && !ship.PlanActive, "the practice starts from the payday card and turns the parked plan off");
            Controls.Hold(Controls.Action.PitchUp, true); yield return new WaitForSecondsRealtime(0.6f);
            Controls.Hold(Controls.Action.PitchUp, false); yield return new WaitForSecondsRealtime(2.5f);
            bool waits = s.TutorialText != null && s.TutorialText.StartsWith("(2/8)");
            Controls.Hold(Controls.Action.PitchUp, true); yield return new WaitForSecondsRealtime(0.6f);
            Controls.Hold(Controls.Action.PitchUp, false); yield return new WaitForSecondsRealtime(2.5f);
            Check(waits && s.TutorialText != null && s.TutorialText.StartsWith("(3/8)"), "the flight assist step stays up until the pilot turns and lets go again");
            Controls.Tap(Controls.Action.ThrottleFull); yield return new WaitForSecondsRealtime(0.4f);
            Controls.Tap(Controls.Action.ThrottleCut); yield return new WaitForSecondsRealtime(0.4f);
            Call("SetHold", Hold.Prograde); yield return new WaitForSecondsRealtime(0.4f);
            Check(s.TutorialText != null && s.TutorialText.StartsWith("(5/8)") && ship.Target?.Name == "Practice beacon",
                "burn and prograde hold steps pass; the practice beacon is the target");
            Call("SetHold", Hold.Off);
            Call("SetTarget", sys.Moon);
            yield return null; yield return null;
            Check(sys["Practice beacon"] == null && s.TutorialText == null && career.TutorialStep == 0, "retargeting ends the practice and removes the beacon");
            yield return new WaitForSecondsRealtime(1f);
            Check(career.Docked?.Name == "Luna", $"docked at Luna after the practice ({ship.Phase})");

            // 2. refuel and take the best-paying cargo job, flown with a 50 % coast
            career.BuyFuel(ship, 1000);
            Check(Math.Abs(ship.Fuel - career.TankCapacity) < 0.01 || career.Credits < 50, "refuel fills the tank");
            var cargo = career.Board.FindAll(c => c.Kind == JobKind.Cargo);
            cargo.Sort((x, y) => x.Days.CompareTo(y.Days)); // the nearest run
            var job = cargo.Count > 0 ? cargo[0] : null;
            Check(job != null, "the Luna board has cargo work");
            // the tiered board: one cargo run per tier, and the far run is doable at 1 g with the first tank and a coast
            {
                string tiers = string.Join(", ", cargo.ConvertAll(c => $"{c.Tier} {c.To}"));
                Check(cargo.Exists(c => c.Tier == "local") && cargo.Exists(c => c.Tier == "interplanetary") && cargo.Exists(c => c.Tier == "far"),
                    $"the Luna board has local, interplanetary and far cargo ({tiers})");
                var farJob = cargo.Find(c => c.Tier == "far");
                if (farJob != null)
                {
                    var fb = sys[Career.PortAt(farJob.To).Body];
                    double d = Math.Max((sys.Pos(fb, jd) - ship.Pos).Mag - Ship.Standoff(fb), 0), best = -1, bestT = 0, bestFuel = 0;
                    for (double f = 1; f > 0.05; f -= 0.05)
                    {
                        Career.Trip(d, Ship.G0, f, career.DryMass + career.TankCapacity + farJob.Load, out double s, out _, out double tonnes);
                        if (s <= farJob.Days * 86400 && tonnes <= career.TankCapacity * 0.95) { best = f; bestT = s; bestFuel = tonnes; break; }
                    }
                    Check(best > 0, $"far run to {farJob.To} ({farJob.Days:F1} days allowed) is doable at 1 g: coast {best * 100:F0} %, {bestT / 86400:F1} days, {bestFuel:F0} t");
                }
            }
            if (job != null)
            {
                career.Accept(job, jd);
                Check(career.Active.Contains(job), $"accepted {job.Tonnes} t to {job.To}");
                yield return null;
                Check(Math.Abs(ship.Cargo - career.CargoTonnes) < 1e-6, "cargo mass is on the ship");
                yield return Shot("c03-luna-board");
                var dest = sys[Career.PortAt(job.To).Body];
                double c1 = career.Credits, expressBy = job.ExpressJd;
                Check(job.ExpressDays > 0 && job.ExpressDays <= job.Days && Math.Abs(job.ExpressJd - (job.IssuedJd + job.ExpressDays)) < 1e-9,
                    $"cargo has an express window inside the deadline ({job.ExpressDays * 24:F1} h of {job.Days * 24:F1} h)");
                yield return Trip(dest, 1, $"Luna → {job.To} coast 50 %", () => !career.Active.Contains(job), 6, 150);
                State("after cargo run");
                Check(!career.Active.Contains(job) && career.Credits > c1, $"cargo delivered and paid ({career.Credits - c1:N0} cr)");
                // a 1 g ship with a 50 % coast makes the window on a short hop, and the note names the bonus
                Check(jd <= expressBy && career.Credits - c1 >= job.Pay * (1 + Career.ExpressBonus) - 1 && career.Notes.Exists(n => n.Text.Contains("express bonus")),
                    $"1 g with a 50 % coast makes the express window: pay ×{1 + Career.ExpressBonus} ({(expressBy - jd) * 24:F1} h to spare)");
                Check(!career.Notes.Exists(n => n.Text.Contains("by hand")), "the autopilot's arrival earns no flown-in-by-hand bonus");
                yield return Shot("c04-delivered");
            }

            // 3. passengers hold the drive to 1 g
            var pax = career.Board.Find(c => c.Kind == JobKind.Passengers);
            if (pax != null && career.Docked != null)
            {
                career.DriveLevel = 1; ship.MaxG = 3;
                career.Accept(pax, jd);
                yield return null;
                Check(ship.MaxG <= 1 + 1e-9, $"passengers aboard: drive held to {ship.MaxG} g");
                career.Abandon(pax);
                career.DriveLevel = 0;
            }

            // 4. a survey, if this port posts one
            var survey = career.Board.Find(c => c.Kind == JobKind.Survey);
            if (survey != null && career.Docked != null)
            {
                career.BuyFuel(ship, 1000);
                career.Accept(survey, jd);
                double c2 = career.Credits;
                yield return Trip(sys[survey.To], 0, "survey of " + survey.To, () => !career.Active.Contains(survey), 6, 150);
                State("after survey");
                Check(!career.Active.Contains(survey) && career.Credits > c2, $"survey scanned and paid ({career.Credits - c2:N0} cr)");
                Check(career.Notes.Exists(n => n.Text.Contains("express bonus")), $"a 1 g full burn makes a survey's express window ({survey.ExpressDays * 24:F0} h)");
                yield return Shot("c05-survey");
            }
            else log.WriteLine("  (no survey on this board)");

            // 5. a dry tank cuts the drive; the tender refills it
            ship.Plan = Plan.None; ship.Fuel = 0; ship.ThrottleCmd = 1;
            var v0 = ship.Vel;
            yield return new WaitForSecondsRealtime(0.5f);
            ship.ThrottleCmd = 0;
            Check(ship.Throttle == 0, "dry tank: drive does not fire");
            yield return Shot("c06-dry");
            double cr = career.Credits;
            career.EmergencyDrop(ship);
            Check(Math.Abs(ship.Fuel - Career.EmergencyTonnes) < 1e-6 && career.Credits < cr, "emergency tender adds reaction mass for a fee");

            // 5b. the express window: bonus inside it, plain pay after it, half after the deadline; a job from an older
            // save (no window) pays plain
            {
                var x = new Contract { Pay = 10000, IssuedJd = 100, DeadlineJd = 104, ExpressDays = 2, ExpressJd = 102 };
                double inside = Career.PayAt(x, 101.9, out bool l1, out bool e1), after = Career.PayAt(x, 103, out bool l2, out bool e2);
                double late = Career.PayAt(x, 105, out bool l3, out bool e3);
                x.ExpressDays = 0; x.ExpressJd = 0;
                double old = Career.PayAt(x, 100.5, out _, out bool e4);
                Check(inside == 12500 && e1 && !l1 && after == 10000 && !e2 && !l2 && late == 5000 && l3 && !e3 && old == 10000 && !e4,
                    $"express pay: {inside:N0} inside the window, {after:N0} after it, {late:N0} late, {old:N0} with no window");
                int n = 0, bad = 0;
                foreach (var b in career.Board) if (b.Kind != JobKind.Intercept) { n++; if (!(b.ExpressDays > 0 && b.ExpressDays <= b.Days)) bad++; }
                Check(bad == 0, $"every board job but an intercept has an express window inside its deadline ({n - bad} of {n})");
            }

            // 5c. route events: calls in flight about a job (a test cargo run to Mars, removed afterwards)
            {
                Check(!career.RouteEvents, "route events are off in the unattended suite");
                double fuelWas = ship.Fuel;
                ship.Fuel = career.TankCapacity;
                var mars = sys["Mars"];
                var run = new Contract { Id = 900001, Kind = JobKind.Cargo, Title = "QA coolant", From = "Luna", To = "Mars orbit", Tonnes = 10, Pay = 10000, Days = 100, Key = "qa/route" };
                run.IssuedJd = jd; run.DeadlineJd = jd + run.Days;
                career.Active.Add(run);
                Call("SetTarget", mars);
                yield return null;

                // a distress call: a rescue near Mars, with the rest of this trip in its time; yes takes it on
                Check(career.MakeCall(Career.RouteKind.Distress, run, sys, ship, jd) && career.Call?.Offer != null, "a distress call comes in");
                var offer = career.Call?.Offer;
                var (ru, rw) = ship.RelToTarget(sys, jd);
                Career.Trip(Math.Max((sys.Pos(mars, jd) - ship.Pos).Mag - Ship.Standoff(mars), 0), ship.MaxG * Ship.G0, 1, ship.Mass, out double burn, out _, out _, ru, rw);
                career.Answer(true, jd);
                yield return null;
                Check(offer != null && career.Active.Contains(offer) && sys[offer.To] != null && sys[offer.To].Parent == mars && offer.Days > burn / 86400 && offer.ExpressDays == 0 && career.Call == null,
                    $"answering takes on the rescue: its wreck orbits Mars, {offer?.Days:F1} days allowed for a {burn / 86400:F1}-day trip");
                if (offer != null) career.Abandon(offer);
                career.MakeCall(Career.RouteKind.Distress, run, sys, ship, jd);
                var ignored = career.Call?.Offer;
                career.Answer(false, jd);
                Check(ignored != null && !career.Active.Contains(ignored) && sys[ignored.To] == null && career.Call == null, "ignoring a distress call takes nothing on");

                // cargo trouble: venting costs the fuel shown; pushing on halves the pay after the spoil time
                career.MakeCall(Career.RouteKind.CargoTrouble, run, sys, ship, jd);
                double vent = career.Call?.VentTonnes ?? -1, f0 = ship.Fuel;
                career.Answer(true, jd);
                Check(vent > 0 && Math.Abs(f0 - ship.Fuel - vent) < 1e-6 && run.SpoilJd == 0, $"venting costs the {vent:N1} t shown and leaves the cargo whole");
                career.MakeCall(Career.RouteKind.CargoTrouble, run, sys, ship, jd);
                double spoilDays = career.Call?.SpoilDays ?? -1;
                career.Answer(false, jd);
                Check(spoilDays > burn / 86400 && Math.Abs(run.SpoilJd - (jd + spoilDays)) < 1e-9 && Career.PayAt(run, run.SpoilJd - 0.01, out _, out _) == 10000 && Career.PayAt(run, run.SpoilJd + 0.01, out _, out _) == 5000,
                    $"pushing on: full pay within {spoilDays * 24:F0} h, half after (a full burn takes {burn / 3600:F0} h)");
                run.SpoilJd = 0;

                // a real roll: flying the job with events on, a call comes in and drops the warp
                yield return null; // the calls above left DropWarp set: let the scene take it before the warp goes up
                career.RouteEvents = true;
                Put("cruiseIdx", 1);
                Put("selectedPlan", Plan.Brachistochrone);
                Call("TogglePlan");
                if (!ship.PlanActive && ship.Phase.StartsWith("fuel short")) Call("TogglePlan");
                Put("warpIdx", 6);
                float t0 = Time.realtimeSinceStartup;
                double jd0 = jd;
                while (career.Call == null && Time.realtimeSinceStartup - t0 < 30) yield return null;
                yield return null;
                Check(career.Call != null && Get<int>("warpIdx") == 0, $"in flight a call comes in and drops the warp ({career.Call?.Title ?? "none"} after {Time.realtimeSinceStartup - t0:F1} s; plan {ship.PlanActive} {ship.Phase}, docked {career.Docked?.Name ?? "-"}, landed {ship.Landed?.Name ?? "-"}, target {ship.Target?.Name ?? "-"}, warp {Get<int>("warpIdx")}, jd +{(jd - jd0) * 24:F1} h, active {career.Active.Count})");
                yield return Shot("c05c-route-call");
                career.RouteEvents = false;
                career.Answer(false, jd);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0;
                run.SpoilJd = 0;
                career.Active.Remove(run);
                yield return null;
                Check(career.Call == null, "with the job gone, no call is left");
                ship.Fuel = fuelWas;
            }

            // 5d. flown in by hand: the helm taken outside the dock range, then a bound orbit with the drive off, pays the
            // bonus; the helm taken inside the range does not. The ship is put in place, not flown (test cargo runs to Mars)
            {
                var mars = sys["Mars"];
                double range = Career.DockRange(mars);
                var rdir = (sys.Pos(mars, jd) - sys.Pos(sys.Sun, jd)).Norm;
                var vdir = D3.Cross(D3.Cross(rdir, sys.Vel(mars, jd).Norm), rdir).Norm;
                double rOrbit = Ship.Standoff(mars);
                foreach (var (from, paid) in new[] { (1.5, true), (0.8, false) })
                {
                    var run = new Contract { Id = 900002, Kind = JobKind.Cargo, Title = "QA ore", From = "Luna", To = "Mars orbit", Tonnes = 10, Pay = 10000, Days = 100, Key = "qa/hand" };
                    run.IssuedJd = jd; run.DeadlineJd = jd + run.Days;
                    // the autopilot flies a frame, then the pilot takes the helm at rest relative to Mars, `from` × the dock range out
                    // (a fall with no orbit: not docked, so the job, added after, is not paid there)
                    ship.Landed = null; ship.ThrottleCmd = 0; ship.Throttle = 0;
                    Call("SetTarget", mars);
                    ship.Pos = sys.Pos(mars, jd) + rdir * (range * from); ship.Vel = sys.Vel(mars, jd);
                    ship.Plan = Plan.Brachistochrone;
                    yield return null;
                    ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0;
                    ship.Pos = sys.Pos(mars, jd) + rdir * (range * from); ship.Vel = sys.Vel(mars, jd);
                    career.Active.Add(run);
                    yield return null;
                    bool shown = career.HandArrival(run) == paid;
                    // then a circular orbit at the autopilot's parking height, drive off: the port docks it and pays
                    double handC0 = career.Credits;
                    ship.Pos = sys.Pos(mars, jd) + rdir * rOrbit; ship.Vel = sys.Vel(mars, jd) + vdir * Math.Sqrt(mars.GM / rOrbit);
                    yield return null;
                    yield return null;
                    double got = career.Credits - handC0, want = paid ? Math.Round(run.Pay * (1 + Career.HandBonus)) : run.Pay;
                    Check(!career.Active.Contains(run) && career.Docked?.Name == "Mars orbit" && shown && Math.Abs(got - want) < 1e-6
                          && paid == career.Notes.Exists(n => n.Text.Contains("by hand")),
                        $"helm taken {from} × the dock range out ({range * from:N0} km), docked in orbit: {got:N0} cr, {(paid ? "flown-in-by-hand bonus" : "no bonus")}{(shown ? "" : ", job card wrong")}");
                    career.Active.Remove(run);
                    career.Notes.Clear();
                }
            }

            // the job pay the buyout price is set against (a load clears the board, so before section 6)
            {
                double boardPay = 0; foreach (var b in career.Board) boardPay += b.Pay;
                log.WriteLine($"  buyout price {Career.ShipPrice:N0} cr; the board at {career.Docked?.Name ?? "no port"} posts {career.Board.Count} jobs, mean pay {boardPay / Math.Max(1, career.Board.Count):N0} cr");
            }

            // 6. the career save round-trips (through JSON only: the player's own save slot is left alone)
            double credits = career.Credits, fuel = ship.Fuel; int done = career.Done;
            var json = JsonUtility.ToJson(career.ToSave(ship));
            career.Credits = -1; ship.Fuel = 0;
            career.FromSave(JsonUtility.FromJson<CareerSave>(json), ship);
            Check(Math.Abs(career.Credits - credits) < 1e-6 && Math.Abs(ship.Fuel - fuel) < 1e-6 && career.Done == done, "save and load keep credits, reaction mass and record");

            // 6b. the long goal: the ship bought out from the yard in steps at a port; a save keeps it, an old save has none paid
            {
                double bank = career.Credits, paid0 = career.ShipPaid;
                career.ShipPaid = 0; career.Notes.Clear();
                career.Credits = Career.ShipStep - 1;
                bool poor = !career.PayTowardShip();
                career.Credits = Career.ShipPrice + 1000;
                int steps = 0; while (steps < 100 && career.PayTowardShip()) steps++;
                int ownedNotes = career.Notes.FindAll(n => n.What == Career.Happening.ShipOwned).Count;
                bool feat = career.Codex.CheckFeats(career, ship).Contains("Owner-operator");
                Check(career.Docked != null && poor && steps == (int)(Career.ShipPrice / Career.ShipStep) && career.OwnsShip && Math.Abs(career.Credits - 1000) < 1e-6
                      && ownedNotes == 1 && feat && !career.PayTowardShip(),
                    $"the ship bought out in {steps} steps of {Career.ShipStep:N0} cr at {career.Docked?.Name}: owned {career.OwnsShip}, {career.Credits:N0} cr left, {ownedNotes} owned note, feat {feat}{(poor ? "" : ", paid without the credits")}");
                var sj = JsonUtility.ToJson(career.ToSave(ship));
                career.ShipPaid = 0;
                career.FromSave(JsonUtility.FromJson<CareerSave>(sj), ship);
                bool kept = career.OwnsShip;
                career.FromSave(JsonUtility.FromJson<CareerSave>(System.Text.RegularExpressions.Regex.Replace(sj, "\"shipPaid\":[^,]*,", "")), ship);
                Check(kept && career.ShipPaid == 0 && sj.Contains("\"shipPaid\""), $"a save keeps the buyout; a save from before it loads with {career.ShipPaid:N0} cr paid");
                career.Credits = bank; career.ShipPaid = paid0; career.Codex.Achieved.Remove("owner"); career.Notes.Clear();
            }

            // 6c. standing (Career.Factions.cs): every port has an owner; the run's own jobs moved it; on time it goes up, late
            // it stays, an abandon or an expiry takes more off; a new tier says so and reprices the day's board; a save keeps
            // it and an old save loads neutral. Test jobs to the port the ship is docked at; the career is put back after
            {
                var keep = JsonUtility.ToJson(career.ToSave(ship));
                var here = career.Docked;
                Check(Career.PortAt("Earth orbit").Owner == Faction.Guard && Career.PortAt("Luna").Owner == Faction.Guard && Career.PortAt("Mars orbit").Owner == Faction.Tharsis
                      && Career.PortAt("Vesta").Owner == Faction.Tharsis && Array.TrueForAll(new[] { "Ceres", "Ganymede", "Callisto", "Titan", "Enceladus" }, p => Career.PortAt(p).Owner == Faction.Compact),
                    "every port has its owner: the Guard at Earth and Luna, Tharsis at Mars and Vesta, the Compact at the ice ports");
                // jobs posted at Earth and Luna were paid (Guard); the distress rescue near Mars was taken on and dropped (Tharsis)
                Check(career.Standing(Faction.Guard) > 0 && career.Standing(Faction.Tharsis) < 0,
                    $"the run's jobs moved standing: Guard {career.Standing(Faction.Guard):+0;-0;0}, Tharsis {career.Standing(Faction.Tharsis):+0;-0;0}, Compact {career.Standing(Faction.Compact):+0;-0;0}");

                int n = 0;
                Contract Job(string from, double issuedAgo, double days) => new Contract
                {
                    Id = 900100 + n, Kind = JobKind.Cargo, Title = "QA standing", From = from, To = here?.Name, Tonnes = 1, Pay = 1000,
                    IssuedJd = jd - issuedAgo, DeadlineJd = jd - issuedAgo + days, Days = days, Key = "qa/standing/" + n++,
                };
                int Cx() => career.Standing(Faction.Compact);
                int s0 = Cx();
                career.Active.Add(Job("Ceres", 0, 10));
                yield return null; yield return null;
                int s1 = Cx();
                career.Active.Add(Job("Titan", 3, 2));            // 1 day late, not yet failed: half pay
                yield return null; yield return null;
                int s2 = Cx();
                var drop = Job("Ganymede", 0, 10);
                career.Active.Add(drop); career.Abandon(drop);     // before a tick can deliver it
                int s3 = Cx();
                career.Active.Add(Job("Callisto", 10, 2));        // far past twice its time: fails before it can pay
                yield return null; yield return null;
                int s4 = Cx();
                Check(here != null && s1 - s0 == Career.StandingGain && s2 == s1 && s3 - s2 == -Career.StandingLoss && s4 - s3 == -Career.StandingLoss && !career.Active.Exists(x => x.Title == "QA standing"),
                    $"Compact standing: {s0:+0;-0;0}, on time {s1:+0;-0;0}, late {s2:+0;-0;0}, abandoned {s3:+0;-0;0}, expired {s4:+0;-0;0} (docked at {here?.Name ?? "-"})");

                // one job short of Known with this port's owner: the next delivery here makes the tier and reprices the board
                if (here != null)
                {
                    var owner = here.Owner;
                    var cs = career.ToSave(ship);
                    cs.standing[(int)owner] = 4;
                    career.FromSave(cs, ship);
                    yield return null;
                    var before = new System.Collections.Generic.Dictionary<string, double>();
                    foreach (var b in career.Board) if (b.Kind == JobKind.Cargo || b.Kind == JobKind.Passengers) before[b.Key] = b.Pay;
                    career.Notes.Clear();
                    career.Active.Add(Job(here.Name, 0, 10));
                    yield return null; yield return null;
                    double was = 0, now = 0; int same = 0;
                    foreach (var b in career.Board) if (before.TryGetValue(b.Key, out double p)) { was += p; now += b.Pay; same++; }
                    bool note = career.Notes.Exists(x => x.What == Career.Happening.Standing && x.Good && x.Text.Contains("Known"));
                    Check(career.TierName(owner) == "Known" && Math.Abs(career.PayFactor(owner) - 1.05) < 1e-9 && note && same > 0 && same == before.Count && now / was > 1.035 && now / was < 1.065,
                        $"{Career.Factions[(int)owner].Name} at {career.Standing(owner):+0;-0;0}: {career.TierName(owner)}, note {note}; {same} of {before.Count} board jobs repriced ×{now / Math.Max(1, was):F3}");
                    career.Notes.Clear();
                    var lose = Job(here.Name, 0, 10);
                    career.Active.Add(lose); career.Abandon(lose);
                    note = career.Notes.Exists(x => x.What == Career.Happening.Standing && !x.Good && x.Text.Contains("Neutral"));
                    Check(career.TierName(owner) == "Neutral" && career.PayFactor(owner) == 1 && note, $"an abandon drops back to {career.TierName(owner)} ({career.Standing(owner):+0;-0;0}), note {note}");
                }

                // the tier table at its edges, the clamp, a save round trip and a save from before factions
                {
                    var cs = career.ToSave(ship);
                    var tiers = new System.Text.StringBuilder();
                    bool edges = true;
                    foreach (var (v, name, pay) in new[] { (-5, "Distrusted", 0.90), (-4, "Neutral", 1.0), (4, "Neutral", 1.0), (5, "Known", 1.05), (14, "Known", 1.05),
                                                          (15, "Trusted", 1.10), (29, "Trusted", 1.10), (30, "Valued", 1.15), (99, "Valued", 1.15) })
                    {
                        cs.standing[(int)Faction.Guard] = v;
                        career.FromSave(cs, ship);
                        edges &= career.TierName(Faction.Guard) == name && Math.Abs(career.PayFactor(Faction.Guard) - pay) < 1e-9;
                        tiers.Append($"{v}:{career.TierName(Faction.Guard)} ×{career.PayFactor(Faction.Guard):F2}  ");
                    }
                    Check(edges && career.Standing(Faction.Guard) == Career.StandingMax, $"tiers {tiers}(99 kept as {career.Standing(Faction.Guard)})");
                    cs.standing[(int)Faction.Guard] = 7; cs.standing[(int)Faction.Tharsis] = -3; cs.standing[(int)Faction.Compact] = 12;
                    career.FromSave(cs, ship);
                    var sj = JsonUtility.ToJson(career.ToSave(ship));
                    career.FromSave(JsonUtility.FromJson<CareerSave>(sj), ship);
                    bool kept = career.Standing(Faction.Guard) == 7 && career.Standing(Faction.Tharsis) == -3 && career.Standing(Faction.Compact) == 12;
                    var old = System.Text.RegularExpressions.Regex.Replace(sj, "\"standing\":\\[[^\\]]*\\],", "");
                    career.FromSave(JsonUtility.FromJson<CareerSave>(old), ship);
                    bool neutral = career.Standing(Faction.Guard) == 0 && career.Standing(Faction.Tharsis) == 0 && career.Standing(Faction.Compact) == 0 && career.PayFactor(Faction.Tharsis) == 1;
                    Check(kept && neutral && sj.Contains("\"standing\"") && !old.Contains("\"standing\""), $"a save keeps standing ({kept}); a save from before factions loads neutral ({neutral})");
                }
                career.FromSave(JsonUtility.FromJson<CareerSave>(keep), ship);
                career.Notes.Clear();
                yield return null;
            }

            // 6d. a board job taken and dropped stays off the day's board through a save and a load (it came back when the
            // load kept only the active jobs' slots); a save from before the slot list loads as then, and posts it again
            {
                var keep = JsonUtility.ToJson(career.ToSave(ship));
                var posted = career.Board.ConvertAll(b => b.Key);
                var pick = career.Board.Find(b => career.CanAccept(b, out _));
                if (pick == null)
                    Check(false, $"a board job to take at {career.Docked?.Name ?? "no port"}: none of {career.Board.Count} can be accepted ({career.Active.Count} active, {career.CargoTonnes:N0} t aboard)");
                else
                {
                    string key = pick.Key;
                    career.Accept(pick, jd);
                    bool took = career.Active.Contains(pick);
                    career.Abandon(pick);
                    var sj = JsonUtility.ToJson(career.ToSave(ship));
                    career.FromSave(JsonUtility.FromJson<CareerSave>(sj), ship);
                    yield return null;
                    var after = career.Board.ConvertAll(b => b.Key);
                    bool off = !after.Contains(key) && after.Count == posted.Count - 1 && after.TrueForAll(posted.Contains);
                    Check(career.Docked != null && took && off && sj.Contains("\"taken\""),
                        $"{pick.Title} ({key}) taken and abandoned at {career.Docked?.Name ?? "-"}, saved and loaded: {(off ? "off the board" : "on the board again")}, {after.Count} of {posted.Count} jobs posted");
                    // a slot from a past day, and one that is not a board slot, go at the next board: the list does not grow
                    var cs = career.ToSave(ship);
                    string stale = $"{career.Docked?.Name}/{(long)Math.Floor(jd) - 3}/0";
                    cs.taken.Add(stale); cs.taken.Add("route/1");
                    career.FromSave(cs, ship);
                    yield return null;
                    var slots = career.ToSave(ship).taken;
                    Check(slots.Contains(key) && !slots.Contains(stale) && !slots.Contains("route/1"), $"slots kept after the next board: {string.Join(", ", slots)}");
                    var old = System.Text.RegularExpressions.Regex.Replace(sj, "\"taken\":\\[[^\\]]*\\],", "");
                    career.FromSave(JsonUtility.FromJson<CareerSave>(old), ship);
                    yield return null;
                    // jobs taken here earlier today may come back too: the old save has no slots but the active jobs'
                    bool back = posted.TrueForAll(k => career.Board.Exists(b => b.Key == k));
                    Check(back && !old.Contains("\"taken\""), $"a save from before the slot list loads: {career.Board.Count} of {posted.Count} jobs posted, the dropped job {(back ? "posted again" : "missing")}");
                }
                career.FromSave(JsonUtility.FromJson<CareerSave>(keep), ship);
                career.Notes.Clear();
                yield return null;
            }

            // 7. autopilot arrivals with the target behind its planet (the climb-away rule must not stop it parking)
            career.Sandbox = true;
            foreach (var (name, from, g) in new[] { ("Phobos", 2e6, 1.0), ("Io", 5e6, 1.0), ("Moon", 1e6, 1.0), ("Titan", 1e7, 3.0), ("Ganymede", 2e5, 1.0), ("Moon", 60000.0, 0.3) })
            {
                var t = sys[name];
                var planet = t.Parent;
                var pp = sys.Pos(planet, jd);
                var away = (pp - sys.Pos(t, jd)).Norm; // from the moon through the planet and out the far side
                ship.Plan = Plan.None; ship.Landed = null; ship.ThrottleCmd = 0; ship.MaxG = g;
                ship.Pos = pp + away * from; ship.Vel = sys.Vel(planet, jd);
                Call("SetTarget", t);
                Put("cruiseIdx", 0);
                Put("selectedPlan", Plan.Brachistochrone);
                Call("TogglePlan");
                yield return null;
                yield return Fly(() => ship.Phase == "in orbit" || ship.Phase == "holding station" || ship.Landed != null, 5, 60);
                Check((ship.Phase == "in orbit" || ship.Phase == "holding station") && ship.Landed == null,
                    $"arrive at {name} from {from:N0} km behind {planet.Name} at {g} g: {ship.Phase}");
                yield return Fly(() => career.Codex.Scanned.Contains(name), 4, 15); // park long enough to scan it
            }

            // 8. discovery: parking at a target scans it; the codex survives a save
            var cx = career.Codex;
            Check(Codex.Facts("Titan").Length > 0 && Codex.Facts("Phobos").Length > 0, "codex facts load");
            Check(cx.Visited.Contains("Jupiter") && cx.Visited.Contains("Saturn") && cx.Visited.Contains("Moon"), $"bodies passed close are logged ({cx.Visited.Count} visited)");
            Check(cx.Scanned.Contains("Phobos") && cx.Scanned.Contains("Titan"), $"parked targets were scanned ({string.Join(", ", cx.Scanned)})");
            int nv = cx.Visited.Count, ns = cx.Scanned.Count, na = cx.Achieved.Count;
            var cj = JsonUtility.ToJson(career.ToSave(ship));
            cx.Visited.Clear(); cx.Scanned.Clear(); cx.Achieved.Clear();
            career.FromSave(JsonUtility.FromJson<CareerSave>(cj), ship);
            Check(cx.Visited.Count == nv && cx.Scanned.Count == ns && cx.Achieved.Count == na, "codex and achievements survive a save");

            // 8b. the data market: each first scan in a system sells for less than the one before, other systems unaffected
            {
                var saturn = sys["Saturn"];
                var moons = sys.Bodies.FindAll(o => o.Parent == saturn && !Career.IsCraft(o) && !cx.Scanned.Contains(o.Name));
                var jovian = sys.Bodies.Find(o => o.Parent == sys["Jupiter"] && !Career.IsCraft(o) && !cx.Scanned.Contains(o.Name));
                var rock = sys.Bodies.Find(o => o.Asteroid && !cx.Scanned.Contains(o.Name));
                var keep = new System.Collections.Generic.List<string>(cx.Scanned);
                cx.Scanned.Clear();
                double first = cx.SaleValue(sys, moons[0], jd), jFirst = cx.SaleValue(sys, jovian, jd);
                for (int i = 1; i <= 3; i++) cx.Scanned.Add(moons[i].Name);
                double fourth = cx.SaleValue(sys, moons[0], jd), jAfter = cx.SaleValue(sys, jovian, jd);
                Check(Math.Abs(fourth - Math.Round(Codex.DataValue(sys, moons[0], jd) * 0.512 / 10) * 10) < 1e-6 && fourth < first && jAfter == jFirst
                      && Codex.Market(sys, rock) == sys.Sun && Codex.Market(sys, moons[0]) == saturn && Codex.Share(40) == Codex.MarketFloor,
                    $"{moons[0].Name} sells for {first:N0} cr, {fourth:N0} cr after 3 other Saturn scans; {jovian.Name} {jFirst:N0} → {jAfter:N0} cr; {rock.Name} sells to the asteroid market");
                // what every first scan in the game is worth, sold in the order the bodies are listed
                cx.Scanned.Clear();
                double all = 0, uncapped = 0, inSaturn = 0;
                var sold = new System.Collections.Generic.Dictionary<Body, int>();
                foreach (var o in sys.Bodies)
                {
                    if (o.Parent == null || Career.IsCraft(o)) continue;
                    var m = Codex.Market(sys, o);
                    sold.TryGetValue(m, out int k); sold[m] = k + 1;
                    double v = Codex.DataValue(sys, o, jd), sale = Math.Round(v * Codex.Share(k) / 10) * 10;
                    all += sale; uncapped += v;
                    if (m == saturn) inSaturn += sale;
                }
                log.WriteLine($"  codex data market: every first scan sells for {all:N0} cr in all ({uncapped:N0} uncapped), the Saturn system {inSaturn:N0} cr; all upgrades cost {Career.DriveCost[1] + Career.DriveCost[2] + Career.MaxTankLevel * Career.TankCost + Codex.SensorCost:N0} cr");
                cx.Scanned.UnionWith(keep);
            }
            career.Sandbox = false;
            Put("cruiseIdx", 0);
            Call("SetTarget", sys["Titan"]);
            Put("warpIdx", 0);
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("c07-codex-titan");
            Check(cx.Has("first_job") && cx.Has("jovian") && cx.Has("coast"), $"achievements (career only): {string.Join(", ", cx.Achieved)}");

            // 9. a rescue: fly to a drifting ship on its own orbit, match it, hold while the crew crosses
            State("before rescue");
            Check(career.Docked?.Name == "Luna", "docked at Luna again");
            if (career.Docked != null)
            {
                career.BuyFuel(ship, 1000);
                var res = career.Board.Find(c => c.Kind == JobKind.Rescue) ?? career.Rescue(new System.Random(7), sys, jd);
                career.Accept(res, jd);
                var wreck = sys[res.To];
                Check(wreck != null && Career.IsDerelict(wreck), $"derelict is in the system: {res.To} around {res.Parent}");
                double c3 = career.Credits;
                // closest approach, and which side it came in on: a frame at warp can hide a pass through the wreck
                double wreckMin = double.MaxValue; D3 wreckSide = D3.Zero;
                yield return Trip(wreck, 0, "rescue " + res.To, () =>
                {
                    var rel = ship.Pos - sys.Pos(wreck, jd);
                    wreckMin = Math.Min(wreckMin, rel.Mag);
                    if (wreckSide.Sqr == 0 && rel.Mag < 50) wreckSide = rel.Norm;
                    return res.Dwell > 0 || !career.Active.Contains(res);
                }, 4, 120);
                var wreckRel = ship.Pos - sys.Pos(wreck, jd);
                Check(wreckMin > 0.3 && wreckRel.Mag < 3 && D3.Dot(wreckSide, wreckRel.Norm) > 0,
                    $"the ship stops beside the wreck, not through it: closest {wreckMin:F2} km, {wreckRel.Mag:F2} km as the crew crosses, same side {D3.Dot(wreckSide, wreckRel.Norm):F2}");
                // a close look at the wreck while the crew crosses over
                Call("SetPivot", Enum.ToObject(s.GetType().GetNestedType("Pivot", F), 1), 0.12);
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Shot("c08-derelict");
                Call("SetPivot", Enum.ToObject(s.GetType().GetNestedType("Pivot", F), 0), 0.16);
                yield return Fly(() => !career.Active.Contains(res), 3, 60);
                State("after rescue");
                Check(!career.Active.Contains(res) && career.Credits > c3 && sys[res.To] == null, $"crew rescued and paid ({career.Credits - c3:N0} cr), wreck removed");
                yield return Shot("c09-rescued");
            }

            // 10. an intercept: back to Luna, then catch a ship under thrust between two inner ports
            ship.MaxG = 1; // the arrival tests left it at 0.3 g
            yield return Trip(sys.Moon, 0, "back to Luna", () => career.Docked?.Name == "Luna" && ship.Phase == "in orbit", 3, 60);
            Check(career.Docked?.Name == "Luna", "docked at Luna for the intercept");
            if (career.Docked != null)
            {
                career.BuyFuel(ship, 1000);
                var inner = new[] { "Earth orbit", "Luna", "Mars orbit" };
                Contract ic = null;
                for (int seed = 1; seed < 500 && ic == null; seed++)
                {
                    var cand = career.Intercept(new System.Random(seed), sys, jd);
                    if (cand != null && Array.IndexOf(inner, cand.RunFrom) >= 0 && Array.IndexOf(inner, cand.RunTo) >= 0 && cand.RunTo != "Luna") ic = cand;
                }
                Check(ic != null, "an inner-system intercept can be posted");

                // QA-064: a runner nearly at its port can't be caught in time, so it is neither posted nor accepted
                if (ic != null)
                {
                    int posted = 0;
                    for (int seed = 1; seed < 400; seed++) if (career.Intercept(new System.Random(seed), sys, jd) != null) posted++;
                    var lateJob = JsonUtility.FromJson<Contract>(JsonUtility.ToJson(ic));
                    lateJob.Id += 200000; lateJob.To += "L";
                    double shift = (ic.T1 - jd) * 0.98;
                    lateJob.T0 -= shift; lateJob.T1 -= shift; lateJob.Days = lateJob.T1 - jd;
                    var lateRunner = career.TargetOf(lateJob);
                    bool lateOk = career.CanAccept(lateJob, out string lateWhy);
                    bool lateCatch = lateRunner != null && career.Catchable(lateJob, lateRunner, sys, jd);
                    Check(career.CanAccept(ic, out string icWhy), $"QA-064: the posted intercept can be accepted ({icWhy ?? "ok"}), {posted} posted from 399 seeds");
                    Check(lateRunner != null && !lateCatch && !lateOk && lateWhy != null && lateWhy.Contains("catch"),
                        $"QA-064: a runner {(lateJob.T1 - jd) * 24:F1} h from {lateJob.RunTo} is refused: {lateWhy}");
                }

                // QA-041 (1): a job the tank can't fly is refused on accept
                if (ic != null)
                {
                    double keep = ship.Fuel;
                    ship.Fuel = 3;
                    yield return null;
                    bool ok = career.CanAccept(ic, out string why);
                    Check(!ok && why != null && why.Contains("needs"), $"infeasible intercept refused: {why}");
                    ship.Fuel = keep;
                    yield return null;
                }

                // QA-041 (2): a 204 km/s flyby within 500 km never pays, even at 100k×
                if (ic != null)
                {
                    var fly = ic;
                    var copy = JsonUtility.FromJson<Contract>(JsonUtility.ToJson(fly)); copy.Id += 100000; copy.To += "x";
                    career.Accept(copy, jd);
                    var rb = sys[copy.To];
                    double cf = career.Credits;
                    var keepPos = ship.Pos; var keepVel = ship.Vel;
                    ship.Plan = Plan.None; ship.Landed = null;
                    Put("warpIdx", 5);
                    for (int f = 0; f < 30 && rb != null; f++)
                    {
                        var rv = Career.MakeRunner(copy, sys);
                        var tv = (rv.Path(jd + 30.0 / 86400) - rv.Path(jd - 30.0 / 86400)) / 60.0;
                        var along = tv.Mag > 0 ? tv.Norm : new D3(1, 0, 0);
                        ship.Pos = rb.Path(jd) + along * 100; ship.Vel = tv + along * 204; // passing at 204 km/s, 100 km off
                        yield return null;
                    }
                    Put("warpIdx", 0);
                    Check(career.Credits == cf && career.Active.Contains(copy) && copy.Dwell == 0, $"204 km/s flyby does not pay (dwell {copy.Dwell:F0} s)");
                    career.Abandon(copy);
                    ship.Pos = keepPos; ship.Vel = keepVel;
                    yield return null;
                }

                // QA-041 (3): a dry tank fails a rendezvous job, drops time compression and stops the autopilot
                if (ic != null)
                {
                    var dry = ic;
                    var copy = JsonUtility.FromJson<Contract>(JsonUtility.ToJson(dry)); copy.Id += 200000; copy.To += "y";
                    career.Accept(copy, jd);
                    Call("SetTarget", sys[copy.To]);
                    Put("selectedPlan", Plan.Brachistochrone);
                    Call("TogglePlan");
                    Put("warpIdx", 5);
                    double keep = ship.Fuel;
                    yield return null;
                    ship.Fuel = 0;
                    yield return null; yield return null;
                    Check(!career.Active.Contains(copy) && Get<int>("warpIdx") == 0 && !ship.PlanActive && sys[copy.To] == null,
                        $"dry tank: job failed, warp {Get<int>("warpIdx")}, plan {(ship.PlanActive ? "on" : "off")}");
                    Check(career.Notes.Exists(n => n.Text.Contains("emergency tender")), "dry tank offers the emergency tender");
                    ship.Fuel = keep;
                    ship.Plan = Plan.None;
                    Call("SetTarget", sys.Moon);
                    yield return Trip(sys.Moon, 0, "back to Luna again", () => career.Docked?.Name == "Luna" && ship.Phase == "in orbit", 3, 60);
                }
                if (ic != null)
                {
                    var probe = Career.MakeRunner(ic, sys);
                    double e0 = (probe.Path(ic.T0 + 1e-7) - sys.PosAt(sys[Career.PortAt(ic.RunFrom).Body], ic.T0)).Mag;
                    double e1 = (probe.Path(ic.T1 - 1e-7) - sys.PosAt(sys[Career.PortAt(ic.RunTo).Body], ic.T1)).Mag;
                    double peak = Career.RunnerPeakG(probe, ic);
                    Check(e0 < 50 && e1 < 50 && peak > 0.1 && peak < 0.4, $"runner course: leaves {ic.RunFrom} ({e0:F1} km off), reaches {ic.RunTo} ({e1:F1} km off), peak {peak:F3} g");
                    career.Accept(ic, jd);
                    var runner = sys[ic.To];
                    // QA-065: the line drawn for it is one smooth course from port to port, not chords across an orbit
                    Call("SetTarget", runner);
                    yield return null; yield return null;
                    string course = s.TestTargetLineReport(out double longest);
                    Check(longest > 0 && longest < 0.05, $"QA-065: runner course line: {course}");
                    double c4 = career.Credits;
                    double runnerMin = double.MaxValue; D3 runnerSide = D3.Zero;
                    yield return Trip(runner, 0, "intercept " + ic.To, () =>
                    {
                        var rel = ship.Pos - sys.Pos(runner, jd);
                        runnerMin = Math.Min(runnerMin, rel.Mag);
                        if (runnerSide.Sqr == 0 && rel.Mag < 50) runnerSide = rel.Norm;
                        return ic.Dwell > 0 || !career.Active.Contains(ic);
                    }, 5, 150);
                    var runnerRel = ship.Pos - sys.Pos(runner, jd);
                    Check(runnerMin > 0.3 && runnerRel.Mag < 3 && D3.Dot(runnerSide, runnerRel.Norm) > 0,
                        $"the ship stops beside the runner, not through it: closest {runnerMin:F2} km, {runnerRel.Mag:F2} km as the cargo crosses, same side {D3.Dot(runnerSide, runnerRel.Norm):F2}");
                    Call("SetPivot", Enum.ToObject(s.GetType().GetNestedType("Pivot", F), 1), 0.2);
                    Put("warpIdx", 0);
                    yield return new WaitForSecondsRealtime(1.5f);
                    yield return Shot("c10-intercept");
                    Call("SetPivot", Enum.ToObject(s.GetType().GetNestedType("Pivot", F), 0), 0.16);
                    yield return Fly(() => !career.Active.Contains(ic), 2, 60);
                    State("after intercept");
                    Check(!career.Active.Contains(ic) && career.Credits > c4 && sys[ic.To] == null, $"cargo handed over in flight and paid ({career.Credits - c4:N0} cr)");
                    Check(ship.Target == null && !ship.PlanActive, "after the handover the autopilot lets the runner go");
                }
            }

            // QA-061: Vesta 0.9 AU off while coasting away from it at 446 km/s, 300 km/s sideways. The card read the trip
            // from rest (2·√(a·d)) and the tank ran dry short; the plan has to stop first. The estimate must cover what
            // the autopilot flies, and a tank that can't finish it must not engage on the first press.
            {
                app.Restart(app.GameIndex);
                yield return new WaitForSecondsRealtime(1f);
                s = (OpenSystemScene)app.Current;
                Put("warpIdx", 0);
                var vesta = sys["Vesta"];
                var dir = sys.Pos(vesta, jd).Norm;
                var side = D3.Cross(dir, new D3(0, 0, 1)).Norm;
                var pos0 = sys.Pos(vesta, jd) - dir * (0.9 * Frame.AU);
                var vel0 = sys.Vel(vesta, jd) - dir * 446 + side * 300;
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Landed = null;
                ship.Pos = pos0; ship.Vel = vel0; ship.Fuel = career.TankCapacity;
                Call("SetTarget", vesta);
                Put("cruiseIdx", 0);
                Put("selectedPlan", Plan.Brachistochrone);
                double d = Math.Max((sys.Pos(vesta, jd) - ship.Pos).Mag - Ship.Standoff(vesta), 0);
                var (u, w) = ship.RelToTarget(sys, jd);
                Career.Trip(d, ship.AMax, 1, ship.Mass, out double estT, out double estDv, out _, u, w);
                double fromRest = 2 * Math.Sqrt(ship.AMax * d);
                log.WriteLine($"  QA-061 setup: {d / Frame.AU:F3} AU, closing {u:F0} km/s, sideways {w:F0} km/s; from rest {fromRest:F0} km/s, from state {estDv:F0} km/s, Δv left {Career.DvLeft(ship):F0}");

                // tank holding 90 % of the estimate: the first press warns, the second goes anyway
                ship.Fuel = (ship.DryMass + ship.Cargo) * (Math.Exp(0.9 * estDv / Ship.Ve) - 1);
                Call("TogglePlan");
                Check(!ship.PlanActive && ship.Phase.StartsWith("fuel short"), $"short tank ({Career.DvLeft(ship):F0} km/s left): first engage warns ({ship.Phase})");
                Call("TogglePlan");
                Check(ship.PlanActive, "second engage goes anyway");
                ship.Plan = Plan.None;

                // full tank: fly it and compare
                ship.Pos = pos0; ship.Vel = vel0; ship.Fuel = career.TankCapacity;
                double dv0 = ship.DvUsed, jd0 = jd;
                Call("TogglePlan");
                Check(ship.PlanActive, $"full tank ({Career.DvLeft(ship):F0} km/s left) engages at once");
                yield return Fly(() => ship.Phase == "in orbit" || ship.Dry, 5, 120);
                Put("warpIdx", 0);
                double flown = ship.DvUsed - dv0;
                log.WriteLine($"  QA-061 flew {(jd - jd0) * 24:F1} h (est {estT / 3600:F1} h), Δv {flown:F0} km/s = {flown / estDv:F2} × estimate, phase {ship.Phase}");
                Check(ship.Phase == "in orbit" && flown <= estDv * 1.1, $"arrives at Vesta within the 10 % margin ({flown:F0} of {estDv:F0} km/s)");
                ship.Plan = Plan.None;
            }

            // #23: a radial fall toward Luna is not docked, a parking orbit is; #18: Continue on a save made mid-plan at
            // 100k× with a rescue targeted loads at 1×, says the plan was not resumed and targets the rebuilt derelict
            {
                app.Restart(app.GameIndex);
                yield return new WaitForSecondsRealtime(1f);
                s = (OpenSystemScene)app.Current;
                Put("warpIdx", 0);
                var moon = sys.Moon;
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Landed = null;
                ship.Pos = sys.Pos(moon, jd) + new D3(2000, 0, 0); ship.Vel = sys.Vel(moon, jd) + new D3(-1, 0, 0);
                yield return null; yield return null;
                Check(career.Docked == null, $"radial fall 263 km above Luna is not docked ({career.Docked?.Name ?? "none"})");
                ship.Pos = sys.Pos(moon, jd) + new D3(5000, 0, 0); ship.Vel = sys.Vel(moon, jd) + new D3(0, Math.Sqrt(moon.GM / 5000), 0);
                yield return null; yield return null;
                Check(career.Docked?.Name == "Luna", $"circular orbit at Luna docks ({career.Docked?.Name ?? "none"})");
                // docked, dry and in debt was a dead end: the tender comes only out there, and fuel needed credits
                if (career.Docked != null)
                {
                    double fuelWas = ship.Fuel, crWas = career.Credits;
                    ship.Fuel = 0; career.Credits = -5000;
                    career.BuyFuel(ship, career.TankCapacity);
                    double price = career.Docked.FuelPrice;
                    Check(Math.Abs(ship.Fuel - Career.EmergencyTonnes) < 0.01 && Math.Abs(career.Credits - (-5000 - Career.EmergencyTonnes * price)) < 1,
                        $"docked, dry and broke: the port sells a tender's load on credit ({ship.Fuel:N1} t, {career.Credits:N0} cr)");
                    career.BuyFuel(ship, 20);
                    Check(Math.Abs(ship.Fuel - Career.EmergencyTonnes) < 0.01, $"no more credit past a tender's load ({ship.Fuel:N1} t)");
                    ship.Fuel = fuelWas; career.Credits = crWas;
                }
                var rj = career.Docked != null ? career.Rescue(new System.Random(5), sys, jd) : null;
                if (rj != null) career.Accept(rj, jd);
                if (rj != null && career.Active.Contains(rj))
                {
                    Call("SetTarget", sys[rj.To]);
                    Put("selectedPlan", Plan.Brachistochrone);
                    Call("TogglePlan");
                    Put("warpIdx", 5);
                    yield return null;
                    s.SaveGame();
                    app.Restart(app.GameIndex);
                    yield return new WaitForSecondsRealtime(1f);
                    s = (OpenSystemScene)app.Current;
                    s.LoadGame();
                    yield return null;
                    var wreck = sys[rj.To];
                    Check(wreck != null && ship.Target == wreck, $"Continue targets the rebuilt derelict ({ship.Target?.Name ?? "none"})");
                    Check(Get<int>("warpIdx") == 0 && !ship.PlanActive && career.Notes.Exists(n => n.Text.Contains("Not resumed")),
                        $"Continue starts at 1× with the plan dropped and says so (warp {Get<int>("warpIdx")})");
                }
                else log.WriteLine("  (rescue not accepted: Continue check skipped)");
            }

            // #25: a slow touchdown lands; a fast one destroys the ship, and the last save comes back. Landing at Luna (a
            // port) autosaves, so that save is the landing, not the parking orbit saved by hand before it.
            {
                app.Restart(app.GameIndex);
                yield return new WaitForSecondsRealtime(1f);
                s = (OpenSystemScene)app.Current;
                Put("warpIdx", 0);
                string saveKey = SaveKey;
                string kept = PlayerPrefs.GetString(saveKey);
                var first = career.Board.Count > 0 ? career.Board[0] : null;
                if (first != null) career.Accept(first, jd);
                yield return null; yield return null;
                Check(first != null && career.Active.Contains(first) && PlayerPrefs.GetString(saveKey) == kept,
                    "a new career still docked at Earth does not autosave over the old save (a job accepted)");
                var moon = sys.Moon;
                var up = new D3(1, 0, 0);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Landed = null;
                ship.Pos = sys.Pos(moon, jd) + up * 5000; ship.Vel = sys.Vel(moon, jd) + new D3(0, Math.Sqrt(moon.GM / 5000), 0);
                s.SaveGame();
                ship.Pos = sys.Pos(moon, jd) + up * (Ship.SurfaceRadius(moon, up) + 0.02); ship.Vel = sys.Vel(moon, jd) - up * 0.005;
                yield return Fly(() => ship.Landed != null, 0, 20);
                Check(ship.Landed == moon && ship.Phase.StartsWith("landed") && ship.ImpactSpeed < Ship.SafeTouchdown,
                    $"a {ship.ImpactSpeed * 1000:F0} m/s touchdown lands ({ship.Phase})");
                yield return null; yield return null;
                Check(PlayerPrefs.GetString(saveKey).Contains("\"landed\":\"Moon\""), "landing at Luna, a port, autosaves");
                ship.Landed = null;
                ship.Pos = sys.Pos(moon, jd) + up * (Ship.SurfaceRadius(moon, up) + 50); ship.Vel = sys.Vel(moon, jd) - up * 2;
                yield return Fly(() => career.Notes.Exists(n => n.Text.StartsWith("Destroyed")), 1, 30);
                Put("warpIdx", 0);
                double alt = (ship.Pos - sys.Pos(moon, jd)).Mag;
                Check(career.Notes.Exists(n => n.Text.StartsWith("Destroyed: hit Moon")) && ship.Landed == moon && Math.Abs(alt - Ship.SurfaceRadius(moon, up)) < 1,
                    $"a 2 km/s impact destroys the ship and reloads the autosaved landing ({alt:F0} km from Luna's centre, {ship.Phase})");
            }

            // New game while a rescue is active: the rebuilt scene must not touch the old wreck's materials
            {
                int before = exceptions;
                for (int round = 0; round < 2; round++)
                {
                    app.Restart(app.GameIndex); // a new career starts docked at Earth orbit
                    yield return new WaitForSecondsRealtime(1f);
                    s = (OpenSystemScene)app.Current;
                    Put("warpIdx", 0);
                    var wreckJob = career.Rescue(new System.Random(11 + round), sys, jd);
                    career.Accept(wreckJob, jd);
                    yield return new WaitForSecondsRealtime(1f); // the wreck is built and drawn
                }
                Check(career.Active.Exists(c => c.Kind == JobKind.Rescue) && exceptions == before, $"new game with a rescue active: {exceptions - before} exceptions");
            }

            // QA-051: Continue keeps the tutorial where the save left it, and a skipped tutorial stays skipped
            {
                app.Restart(app.GameIndex);
                yield return new WaitForSecondsRealtime(1f);
                s = (OpenSystemScene)app.Current;
                yield return null;
                string running = JsonUtility.ToJson(career.ToSave(ship));        // step 1 of 3 in progress
                Call("SkipTutorial");
                yield return null;
                string skipped = JsonUtility.ToJson(career.ToSave(ship));
                Check(s.TutorialText == null && career.TutorialStep == 0, "skip ends the tutorial");
                foreach (var (saved, wantOn, what) in new[] { (skipped, false, "skipped"), (running, true, "in progress") })
                {
                    app.Restart(app.GameIndex);
                    yield return new WaitForSecondsRealtime(1f);
                    s = (OpenSystemScene)app.Current;
                    career.FromSave(JsonUtility.FromJson<CareerSave>(saved), ship);
                    Call("ResumeTutorial");
                    yield return null; yield return null;
                    Check((s.TutorialText != null) == wantOn, $"Continue on a save with the tutorial {what}: card {(s.TutorialText != null ? "shown" : "hidden")}");
                }
                // a save from before the tutorial was saved (no field) counts as done
                app.Restart(app.GameIndex);
                yield return new WaitForSecondsRealtime(1f);
                s = (OpenSystemScene)app.Current;
                var old = JsonUtility.FromJson<CareerSave>(skipped.Replace("\"tutorial\":0,", ""));
                career.FromSave(old, ship);
                Call("ResumeTutorial");
                yield return null;
                Check(s.TutorialText == null, "an older save without tutorial state stays quiet");
            }

            State("end");
            log.WriteLine($"done: {fails} failed checks");
            log.Close();
            Application.Quit();
        }
    }
}
