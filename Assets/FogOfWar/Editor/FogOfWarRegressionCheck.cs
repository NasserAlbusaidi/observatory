using System;
using System.IO;
using System.Reflection;
using FogOfWar.OpenWorld;
using UnityEditor;
using UnityEngine;

namespace FogOfWar.EditorTools
{
    // Focused production-path regressions. Run in batch mode with -executeMethod
    // FogOfWar.EditorTools.FogOfWarRegressionCheck.Run. A failed assertion exits 1.
    public static class FogOfWarRegressionCheck
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static int failed;
        static StreamWriter report;
        static void Check(bool pass, string text)
        {
            if (!pass) failed++;
            report.WriteLine((pass ? "PASS " : "FAIL ") + text);
            Debug.Log("REGRESSION " + (pass ? "PASS " : "FAIL ") + text);
        }

        [MenuItem("Fog of War/Issue regression check")]
        public static void Run()
        {
            int count = RunChecks();
            if (Application.isBatchMode) EditorApplication.Exit(count == 0 ? 0 : 1);
        }

        public static int RunChecks()
        {
            failed = 0;
            Directory.CreateDirectory("Logs");
            using (report = new StreamWriter("Logs/issue-regressions.txt"))
            {
                report.WriteLine($"Unity {Application.unityVersion}; UTC {DateTime.UtcNow:O}; double precision km, s, TDB JD");
                try { CoastClock(); SurfaceContact(); Provenance(); GravityPolicy(); PlanetTargets(); }
                catch (Exception e) { Check(false, e.ToString()); }
                report.WriteLine($"done: {failed} failed checks");
            }
            return failed;
        }

        static void PlanetTargets()
        {
            var sys = SolarSystem.Build();
            var scene = new OpenSystemScene();
            var ship = new Ship { Target = sys.Earth };
            var career = new Career { Sys = sys };
            var job = new Contract { Id = 910101, Kind = JobKind.Intercept, To = "Runner QA planet buttons",
                RunFrom = "Earth orbit", RunTo = "Mars orbit", T0 = CareerQA.Epoch, T1 = CareerQA.Epoch + 10 };
            career.Active.Add(job);
            typeof(Career).GetMethod("SpawnDerelict", Private).Invoke(career, new object[] { job });
            typeof(OpenSystemScene).GetField("sys", Private).SetValue(scene, sys);
            typeof(OpenSystemScene).GetField("ship", Private).SetValue(scene, ship);
            var planets = (System.Collections.Generic.List<Body>)typeof(OpenSystemScene).GetMethod("PlanetButtons", Private).Invoke(scene, null);
            Check(career.Active.Contains(job) && sys[job.To]?.Path != null && planets.Count == 8 && !planets.Contains(sys[job.To]),
                "#101 active intercept runner is absent from the shared classic/HUD planet list; all eight planets remain");
            var root = new UnityEngine.UIElements.VisualElement();
            typeof(OpenSystemScene).GetMethod("TargetsTab", Private).Invoke(scene, new object[] { root });
            var labels = UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.Label>(root).ToList();
            Check(labels.Exists(l => l.text == "Earth") && !labels.Exists(l => l.text == job.To),
                "#101 rebuilt Targets tab renders Earth and no intercept-runner planet button");
        }

        static void CoastClock()
        {
            var sun = new Body { Name = "Sun", GM = 1.32712440018e11, Radius = 695700, Look = Look.Sun };
            var sys = new SolarSystem { Sun = sun }; sys.Bodies.Add(sun);
            var scene = new OpenSystemScene();
            void Set(string name, object value) => typeof(OpenSystemScene).GetField(name, Private).SetValue(scene, value);
            var coast = typeof(OpenSystemScene).GetMethod("Coast", Private);
            foreach (double step in new[] { 1.0 / 60, 0.05, 1.0, 30.0 })
            {
                double jd0 = CareerQA.Epoch, jd = jd0, radius = 2 * Frame.AU;
                var p0 = new D3(radius, 0, 0);
                var v0 = new D3(0, Math.Sqrt(sun.GM / radius), 0);
                var ship = new Ship { Pos = p0, Vel = v0 };
                Set("sys", sys); Set("ship", ship);
                const int frames = 6000;
                for (int i = 0; i < frames; i++)
                {
                    Set("jd", jd);
                    double used = (double)coast.Invoke(scene, new object[] { step });
                    if (!(used > 0)) throw new Exception("Coast did not advance");
                    jd += used / 86400;
                }
                // An independent circular-orbit analytic solution, evaluated at the clock's actual elapsed time.
                double t = (jd - jd0) * 86400, n = Math.Sqrt(sun.GM / (radius * radius * radius));
                var wantP = new D3(radius * Math.Cos(n * t), radius * Math.Sin(n * t), 0);
                var wantV = new D3(-radius * n * Math.Sin(n * t), radius * n * Math.Cos(n * t), 0);
                double dp = (ship.Pos - wantP).Mag, dv = (ship.Vel - wantV).Mag;
                Check(dp < 0.002 && dv < 1e-8, $"#88 coast h={step:R}, N={frames}, elapsed={t:R}: analytic circle position {dp:E3} km < 0.002, velocity {dv:E3} km/s < 1e-8");
            }

            // A plan re-check may change its command/phase, never position, velocity, fuel, attitude or burn time.
            var target = new Body { Name = "Derelict probe", Parent = sun, Radius = 0.05,
                Path = j => new D3(2 * Frame.AU, 0, 0), PathAccel = j => D3.Zero };
            var parked = new Ship { Pos = target.Path(CareerQA.Epoch) + new D3(1.05, 0, 0),
                Vel = new D3(0, 18.6, 0), Target = target, Plan = Plan.Brachistochrone, UnlimitedFuel = false };
            var p = parked.Pos; var v = parked.Vel; var a = parked.Att; double fuel = parked.Fuel, dvUsed = parked.DvUsed;
            for (int i = 0; i < 6000; i++) parked.RecheckPlan(sys, CareerQA.Epoch, sun);
            Check((parked.Pos - p).Sqr == 0 && (parked.Vel - v).Sqr == 0 && parked.Att == a &&
                parked.Fuel == fuel && parked.DvUsed == dvUsed && parked.BurnSeconds == 0,
                "#88 6000 plan re-checks advance no physics or fuel");
        }

        static void SurfaceContact()
        {
            var sun = new Body { Name = "Sun", GM = 1, Radius = 1 };
            foreach (double spin in new[] { 360.0, -360.0 })
            foreach (double latitude in new[] { 0.0, 45.0, 90.0 })
            foreach (double step in new[] { 1.0, 60.0, 21600.0 })
            {
                var body = new Body { Name = "Rotating fixture", Parent = sun, GM = 1000, Radius = 1000,
                    A = 1e6, dL = 0.05, Wd = spin, PoleRa = 30, PoleDec = 45 };
                var sys = new SolarSystem { Sun = sun }; sys.Bodies.Add(sun); sys.Bodies.Add(body);
                double jd0 = CareerQA.Epoch, jd = jd0, end = jd0 + 0.25;
                double lat = latitude * Math.PI / 180;
                var initialUp = body.FromFixed(new D3(Math.Cos(lat), 0, Math.Sin(lat)), jd0);
                var ship = new Ship { Pos = sys.Pos(body, jd) + initialUp * (body.Radius - 1e-6),
                    Vel = Ship.SurfaceVelocity(sys, body, initialUp * body.Radius, jd) - initialUp * 0.005 };
                ship.Near.Add(body); ship.CheckTouchdown(sys, jd);
                bool gentle = ship.Landed == body && ship.Crashed == null && ship.ImpactSpeed < Ship.SafeTouchdown;
                var fixedStart = ship.Pos - sys.Pos(body, jd);
                while (jd < end)
                {
                    double next = Math.Min(end, jd + step / 86400), h = (next - jd) * 86400;
                    ship.Step(sys, jd, h, body); jd = next;
                }
                // Rodrigues rotation at a known constant spin, independently of the body-fixed implementation.
                double angle = spin * 0.25 * Math.PI / 180, omega = spin * Math.PI / 180 / 86400;
                var pole = body.Pole;
                var offset = fixedStart * Math.Cos(angle) + D3.Cross(pole, fixedStart) * Math.Sin(angle) +
                    pole * (D3.Dot(pole, fixedStart) * (1 - Math.Cos(angle)));
                var expectedP = sys.Pos(body, end) + offset;
                var expectedV = sys.Vel(body, end) + D3.Cross(pole, offset) * omega;
                double dp = (ship.Pos - expectedP).Mag, dv = (ship.Vel - expectedV).Mag;
                Check(gentle && dp < 1e-6 && dv < 1e-6,
                    $"#54 spin={spin}, latitude={latitude}, h={step}: quarter-turn position {dp:E3} km < 1e-6; surface velocity {dv:E3} km/s < 1e-6; safe local touchdown {gentle}");

                var save = ship.ToSave(jd, 6); var loaded = new Ship(); loaded.FromSave(save, sys);
                Check((loaded.Pos - ship.Pos).Mag < 1e-7 && (loaded.Vel - ship.Vel).Mag < 1e-7,
                    $"#54 body-fixed save/load spin={spin}, latitude={latitude}, h={step}");
                save.fixedContact = false; save.vx = save.vy = save.vz = 0; loaded.FromSave(save, sys);
                Check((loaded.Pos - ship.Pos).Mag < 1e-7 && (loaded.Vel - ship.Vel).Mag < 1e-7,
                    $"#54 legacy contact migration spin={spin}, latitude={latitude}, h={step}");
                ship.Att = Quaternion.LookRotation(Frame.U(offset.Norm)); ship.ThrottleCmd = 1;
                var v0 = ship.Vel; var g = sys.Gravity(ship.Pos, jd); double hLift = 0.01;
                ship.Step(sys, jd, hLift, body);
                double jump = (ship.Vel - v0 - (g + ship.Heading * ship.AMax) * hLift).Mag;
                Check(ship.Landed == null && jump < 1e-6,
                    $"#54 liftoff keeps surface velocity spin={spin}, latitude={latitude}, h={step}: unexplained delta {jump:E3} km/s < 1e-6");
            }
            // The same orientation machinery handles a synchronous moon whose pole is its orbit normal.
            var moon = new Body { Name = "Sync fixture", Parent = sun, A = 1e6, dL = 360, Radius = 100, GM = 100,
                Synchronous = true, I = 30 };
            var syncSys = new SolarSystem { Sun = sun }; syncSys.Bodies.Add(sun); syncSys.Bodies.Add(moon);
            var point = new D3(100.01, 0, 0);
            var sync = new Ship { Landed = moon, Pos = syncSys.Pos(moon, CareerQA.Epoch) + moon.FromFixed(point, CareerQA.Epoch) };
            sync.Step(syncSys, CareerQA.Epoch, 21600, moon);
            double syncError = (sync.Pos - syncSys.Pos(moon, CareerQA.Epoch + 0.25) - moon.FromFixed(point, CareerQA.Epoch + 0.25)).Mag;
            Check(syncError < 1e-6, $"#54 synchronous quarter orbit keeps contact: {syncError:E3} km < 1e-6");

            foreach (var look in new[] { Look.Rocky, Look.Gas, Look.Ice })
            foreach (double speed in new[] { 0.005, 2.0 })
            {
                moon.Look = look; var up = moon.FromFixed(new D3(1, 0, 0), CareerQA.Epoch);
                var ship = new Ship { Pos = syncSys.Pos(moon, CareerQA.Epoch) + up * (moon.Radius - 1e-7),
                    Vel = Ship.SurfaceVelocity(syncSys, moon, up * moon.Radius, CareerQA.Epoch) - up * speed };
                ship.Near.Add(moon); ship.CheckTouchdown(syncSys, CareerQA.Epoch);
                Check((ship.Crashed != null) == (speed > Ship.SafeTouchdown) &&
                    (speed > Ship.SafeTouchdown || ship.Phase.StartsWith(look == Look.Rocky ? "landed" : "assisted")),
                    $"#53 {look} arrival at {speed} km/s: {ship.Phase}; no ordinary giant landing");
            }
        }

        static void Provenance()
        {
            var sys = SolarSystem.Build(); var earth = sys.Earth;
            Check(earth.Cheb != null && earth.PositionModel(CareerQA.Epoch).StartsWith("fitted"), "#51 Earth uses a fitted ephemeris at the fixture epoch");
            if (earth.Cheb != null)
            {
                Check(earth.PositionModel(earth.Cheb.JdStart + 1).Contains("blend") &&
                    earth.PositionModel(earth.Cheb.JdEnd).Contains("fallback"), "#51 per-body boundary blend and fallback labelled");
                earth.Cheb = null;
                Check(earth.PositionModel(CareerQA.Epoch).Contains("unavailable"), "#51 missing fit asset explicitly labelled");
            }
            var mean = sys.Bodies.Find(b => b.Synchronous && b.Cheb == null);
            var estimated = sys.Bodies.Find(b => b.SizeEstimated);
            Check(mean != null && mean.PositionModel(CareerQA.Epoch).Contains("elements") && mean.RotationSource.Contains("assumed"),
                "#51 mean-element moon identifies its assumed rotation");
            Check(estimated != null && estimated.RadiusSource.Contains("unmeasured"), "#51 nominal-size body identifies unmeasured radius");
        }

        static void GravityPolicy()
        {
            var scene = new OpenSystemScene(); var ship = new Ship();
            typeof(OpenSystemScene).GetField("ship", Private).SetValue(scene, ship);
            var coast = typeof(OpenSystemScene).GetMethod("Coasting", Private);
            foreach (int warp in new[] { 0, 1, 2, 3, 6 })
            {
                typeof(OpenSystemScene).GetField("warpIdx", Private).SetValue(scene, warp);
                bool rails = (bool)coast.Invoke(scene, null);
                Check(rails == (warp >= 3), $"#50 warp index {warp}: {(rails ? "labelled two-body approximation" : "same integrated gravity at zero thrust")}");
            }
        }
    }
}
