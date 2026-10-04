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
            Career.Trip(d, ship.AMax, new[] { 1, 0.5, 0.25 }[cruise], ship.Mass, out double estT, out double estDv, out double estFuel);
            double jd0 = jd, fuel0 = ship.Fuel, dv0 = ship.DvUsed;
            Put("selectedPlan", Plan.Brachistochrone);
            Call("TogglePlan");
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

            // 0. the first-flight lessons, done the way a player does them
            Check(s.TutorialText != null && s.TutorialText.StartsWith("First flight"), "tutorial starts on a new career");
            Controls.Hold(Controls.Action.PitchUp, true); yield return new WaitForSecondsRealtime(0.6f);
            Controls.Hold(Controls.Action.PitchUp, false); yield return new WaitForSecondsRealtime(2.5f);
            Check(s.TutorialText != null && s.TutorialText.StartsWith("(3/11)"), "turn and let go: flight assist lesson passes");
            yield return Shot("c00-tutorial");
            Controls.Tap(Controls.Action.ThrottleFull); yield return new WaitForSecondsRealtime(0.4f);
            Controls.Tap(Controls.Action.ThrottleCut); yield return new WaitForSecondsRealtime(0.4f);
            Call("SetHold", Hold.Prograde); yield return new WaitForSecondsRealtime(0.4f);
            Check(s.TutorialText != null && s.TutorialText.StartsWith("(5/11)") && ship.Target?.Name == "Practice beacon",
                "burn and prograde hold lessons pass; the practice beacon is the target");
            Call("SetHold", Hold.Off);
            // #27: the beacon practice is optional, so the Trip below (which targets the Moon) skips it

            // 1. the first job, full burn to the Moon
            double c0 = career.Credits;
            log.WriteLine($"  Moon vs Earth seen from the ship: {Vector3.Angle(Frame.U(sys.Pos(sys.Moon, jd) - ship.Pos), Frame.U(sys.Pos(sys.Earth, jd) - ship.Pos)):F0} deg");
            yield return Trip(sys.Moon, 0, "Earth → Moon full burn", () => career.Docked?.Name == "Luna" && ship.Phase == "in orbit", moonWarp, 60);
            State("at Luna");
            Check(career.Docked?.Name == "Luna", "docked at Luna after the autopilot parks");
            Check(sys["Practice beacon"] == null, "retargeting ended the practice and removed the beacon");
            Check(career.Credits - c0 >= 3000, $"first job paid ({career.Credits - c0:N0} cr)");
            Check(s.TutorialText == null, "tutorial ends with the first job");
            yield return Shot("c02-luna-docked");

            // 2. refuel and take the best-paying cargo job, flown with a 50 % coast
            career.BuyFuel(ship, 1000);
            Check(Math.Abs(ship.Fuel - career.TankCapacity) < 0.01 || career.Credits < 50, "refuel fills the tank");
            var cargo = career.Board.FindAll(c => c.Kind == JobKind.Cargo);
            cargo.Sort((x, y) => x.Days.CompareTo(y.Days)); // the nearest run
            var job = cargo.Count > 0 ? cargo[0] : null;
            Check(job != null, "the Luna board has cargo work");
            if (job != null)
            {
                career.Accept(job, jd);
                Check(career.Active.Contains(job), $"accepted {job.Tonnes} t to {job.To}");
                yield return null;
                Check(Math.Abs(ship.Cargo - career.CargoTonnes) < 1e-6, "cargo mass is on the ship");
                yield return Shot("c03-luna-board");
                var dest = sys[Career.PortAt(job.To).Body];
                double c1 = career.Credits;
                yield return Trip(dest, 1, $"Luna → {job.To} coast 50 %", () => !career.Active.Contains(job), 6, 150);
                State("after cargo run");
                Check(!career.Active.Contains(job) && career.Credits > c1, $"cargo delivered and paid ({career.Credits - c1:N0} cr)");
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

            // 6. the career save round-trips (through JSON only: the player's own save slot is left alone)
            double credits = career.Credits, fuel = ship.Fuel; int done = career.Done;
            var json = JsonUtility.ToJson(career.ToSave(ship));
            career.Credits = -1; ship.Fuel = 0;
            career.FromSave(JsonUtility.FromJson<CareerSave>(json), ship);
            Check(Math.Abs(career.Credits - credits) < 1e-6 && Math.Abs(ship.Fuel - fuel) < 1e-6 && career.Done == done, "save and load keep credits, reaction mass and record");

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
                yield return Trip(wreck, 0, "rescue " + res.To, () => res.Dwell > 0 || !career.Active.Contains(res), 4, 120);
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
                    double c4 = career.Credits;
                    yield return Trip(runner, 0, "intercept " + ic.To, () => ic.Dwell > 0 || !career.Active.Contains(ic), 5, 150);
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
                string running = JsonUtility.ToJson(career.ToSave(ship));        // lesson 1 of 11 in progress
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
