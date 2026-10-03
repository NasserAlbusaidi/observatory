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
        int fails;
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
            State("new career");
            Check(career.Docked?.Name == "Earth orbit", "new career starts docked at Earth orbit");
            Check(!ship.UnlimitedFuel && Math.Abs(ship.Fuel - 200) < 1e-6, "tank full, reaction mass is limited");
            Check(career.Active.Count == 1 && career.Active[0].Tutorial, "first job is the Luna run");
            yield return Shot("c01-start");

            // 1. the first job, full burn to the Moon
            double c0 = career.Credits;
            log.WriteLine($"  Moon vs Earth seen from the ship: {Vector3.Angle(Frame.U(sys.Pos(sys.Moon, jd) - ship.Pos), Frame.U(sys.Pos(sys.Earth, jd) - ship.Pos)):F0} deg");
            yield return Trip(sys.Moon, 0, "Earth → Moon full burn", () => career.Docked?.Name == "Luna", moonWarp, 60);
            State("at Luna");
            Check(career.Docked?.Name == "Luna", "docked at Luna after the autopilot parks");
            Check(career.Credits - c0 >= 3000, $"first job paid ({career.Credits - c0:N0} cr)");
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

            State("end");
            log.WriteLine($"done: {fails} failed checks");
            log.Close();
            Application.Quit();
        }
    }
}
