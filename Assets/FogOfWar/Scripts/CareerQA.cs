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
    public partial class CareerQA : MonoBehaviour
    {
        string dir;
        StreamWriter log;
        FogOfWarApp app;
        OpenSystemScene s;
        int fails, exceptions;
        public const double Epoch = 2460000.5; // fixed TDB fixture; normal play still starts today
        static readonly double[] QaWarps = { 1, 10, 100, 1e3, 1e4, 1e5, 1e6 };
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
        void Perf(bool ok, string what) => log.WriteLine((ok ? "  PERF PASS " : "  PERF SLOW ") + what);
        static IEnumerator Frames(float seconds)
        {
            for (int i = 0, n = Mathf.CeilToInt(seconds * 60); i < n; i++) yield return null;
        }

        void Finish()
        {
            Check(exceptions == 0, $"no Unity errors or exceptions ({exceptions})");
            log.WriteLine($"done: {fails} failed checks");
            log.Close();
            Application.Quit(fails == 0 ? 0 : 1);
        }

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

        // The old budget meant real seconds at the selected warp. Preserve that simulation-time allowance;
        // CPU load can make it take longer, but cannot silently shorten the flight. Wall time is only a hang guard.
        IEnumerator Fly(Func<bool> done, int warp, float budget)
        {
            Put("warpIdx", warp);
            float t0 = Time.realtimeSinceStartup;
            double stopJd = jd + budget * QaWarps[warp] / 86400;
            s.QaStopJd = stopJd;
            float next = 0;
            while (!done() && jd < stopJd)
            {
                if (Time.realtimeSinceStartup - t0 > Math.Max(600, budget * 10))
                {
                    log.WriteLine($"  TIMEOUT hang guard: {ship.Phase}, {(stopJd - jd) * 86400:F1} simulation seconds left; functional result incomplete");
                    log.Close();
                    Application.Quit(2);
                    // Never run the caller's functional assertions against an incomplete flight while quit is pending.
                    while (true) yield return null;
                }
                if (Time.realtimeSinceStartup - t0 > next)
                {
                    next += traceEvery;
                    var dom = Ship.Dominant(sys, ship.Pos, jd);
                    log.WriteLine($"    t+{(Time.realtimeSinceStartup - t0):F0}s  {ship.Phase}  thr {ship.Throttle:F2}  near {dom.Name} {(ship.Pos - sys.Pos(dom, jd)).Mag:N0} km  " +
                                  $"to target {(ship.Pos - sys.Pos(ship.Target, jd)).Mag:N0} km  vrel {(ship.Vel - sys.Vel(ship.Target, jd)).Mag:F2} km/s  fuel {ship.Fuel:F2}");
                }
                yield return null;
            }
            Check(done(), $"flight predicate reached before simulation limit ({budget * QaWarps[warp]:F0} s)");
            s.QaStopJd = double.PositiveInfinity;
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
            OpenSystemScene.CheckUnattendedSaveKey(log);
            app = GetComponent<FogOfWarApp>();
            int ti = Array.IndexOf(Environment.GetCommandLineArgs(), "-fowtrace"); if (ti >= 0) { traceEvery = 0.25f; moonWarp = int.Parse(Environment.GetCommandLineArgs()[ti + 1]); }
            app.InputLocked = true; Controls.Locked = true;
            yield return Frames(1f);
            app.Restart(app.GameIndex);
            yield return Frames(1.5f);
            s = (OpenSystemScene)app.Current;
            log.WriteLine($"fixture epoch {Epoch:R} TDB; save key {SaveKey}; Unity {Application.unityVersion}");
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fowcareertraffic") >= 0)
            { yield return TrafficChecks(); Finish(); yield break; }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fowqa041") >= 0) { yield return Repro041(); yield break; }
            TouchdownSoundChecks();
            SurfaceReadoutChecks();
            CoastStateChecks();
            SaveKeyChecks();
            TargetSpeedRowChecks();
            WarpLimitedNoteChecks();
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
            Controls.Tap(Controls.Action.ThrottleFull); yield return Frames(0.4f);
            Controls.Tap(Controls.Action.ThrottleCut); yield return Frames(0.4f);
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
            Controls.Hold(Controls.Action.PitchUp, true); yield return Frames(0.6f);
            Controls.Hold(Controls.Action.PitchUp, false); yield return Frames(2.5f);
            bool waits = s.TutorialText != null && s.TutorialText.StartsWith("(2/8)");
            Controls.Hold(Controls.Action.PitchUp, true); yield return Frames(0.6f);
            Controls.Hold(Controls.Action.PitchUp, false); yield return Frames(2.5f);
            Check(waits && s.TutorialText != null && s.TutorialText.StartsWith("(3/8)"), "the flight assist step stays up until the pilot turns and lets go again");
            Controls.Tap(Controls.Action.ThrottleFull); yield return Frames(0.4f);
            Controls.Tap(Controls.Action.ThrottleCut); yield return Frames(0.4f);
            Call("SetHold", Hold.Prograde); yield return Frames(0.4f);
            Check(s.TutorialText != null && s.TutorialText.StartsWith("(5/8)") && ship.Target?.Name == "Practice beacon",
                "burn and prograde hold steps pass; the practice beacon is the target");
            Call("SetHold", Hold.Off);
            Call("SetTarget", sys.Moon);
            yield return null; yield return null;
            Check(sys["Practice beacon"] == null && s.TutorialText == null && career.TutorialStep == 0, "retargeting ends the practice and removes the beacon");
            yield return Frames(1f);
            Check(career.Docked?.Name == "Luna", $"docked at Luna after the practice ({ship.Phase})");

            // 2. refuel and take the best-paying cargo job, flown with a 50 % coast
            career.BuyFuel(ship, 1000);
            Check(Math.Abs(ship.Fuel - career.TankCapacity) < 0.01 || career.Credits < 50, "refuel fills the tank");
            var cargo = career.Board.FindAll(c => c.Kind == JobKind.Cargo);
            cargo.Sort((x, y) => x.Days.CompareTo(y.Days)); // the nearest run
            var job = cargo.Count > 0 ? cargo[0] : null;
            Check(job != null, "the Luna board has cargo work");
            // the tiered board: one cargo run per tier, and the far run is doable on the stock drive with the first tank and a coast
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
                        double m = career.DryMass + career.TankCapacity + farJob.Load;
                        Career.Trip(d, career.AccelAt(m), f, m, out double s, out _, out double tonnes);
                        if (s <= farJob.Days * 86400 && tonnes <= career.TankCapacity * 0.95) { best = f; bestT = s; bestFuel = tonnes; break; }
                    }
                    Check(best > 0, $"far run to {farJob.To} ({farJob.Days:F1} days allowed) is doable at full drive with the load ({career.AccelAt(career.DryMass + career.TankCapacity + farJob.Load) / Ship.G0:F2} g): coast {best * 100:F0} %, {bestT / 86400:F1} days, {bestFuel:F0} t");
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

            BuildChecks();

            // 3. passengers hold the drive to 1 g
            var pax = career.Board.Find(c => c.Kind == JobKind.Passengers);
            if (pax != null && career.Docked != null)
            {
                career.Build.pick[(int)Slot.Drive] = 2; ship.MaxG = 3;
                career.Accept(pax, jd);
                yield return null;
                Check(ship.MaxG <= 1 + 1e-9, $"passengers aboard: drive held to {ship.MaxG} g");
                career.Abandon(pax);
                career.Build.pick[(int)Slot.Drive] = 1;
            }
            yield return RefitChecks();

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

            foreach (var step in EventAndSaveChecks()) yield return step;

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
                log.WriteLine($"  codex data market: every first scan sells for {all:N0} cr in all ({uncapped:N0} uncapped), the Saturn system {inSaturn:N0} cr; the yard's biggest parts cost {TopRefits():N0} cr over the stock ones' sell-back");
                cx.Scanned.UnionWith(keep);
            }
            career.Sandbox = false;
            Put("cruiseIdx", 0);
            Call("SetTarget", sys["Titan"]);
            Put("warpIdx", 0);
            yield return Frames(1f);
            yield return Shot("c07-codex-titan");
            Check(cx.Has("first_job") && cx.Has("jovian") && cx.Has("coast"), $"achievements (career only): {string.Join(", ", cx.Achieved)}");

            yield return RescueAndInterceptChecks();

            yield return ReloadChecks();

            yield return TrafficChecks();
            yield return ThreatChecks();
            yield return PursuitChecks();
            yield return PirateChecks();
            yield return DefenseChecks(); // 17: raider missiles, damage, armor and repairs (CareerQA.Defense.cs)
            yield return EncounterFlowChecks(); // 20: Phase 5's encounter flow (CareerQA.EncounterFlow.cs)

            yield return DevMenuChecks(); // 19: the dev menu (CareerQA.DevMenu.cs)
            State("end");
            Finish();
        }
    }
}
