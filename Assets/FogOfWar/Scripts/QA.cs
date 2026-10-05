using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace FogOfWar
{
    // Unattended QA: `FogOfWar.exe -fowqa <dir>` drives every scene's controls to their extremes (by setting the same
    // fields the panel sets), screenshots each step, flags NaN/Infinity readouts and logged errors, then stress-tests
    // scene switching, quality modes and window sizes. Writes qa.txt and quits.
    public class QA : MonoBehaviour
    {
        string dir;
        int errors, bad, only = -1;
        StreamWriter log;
        FogOfWarApp app;

        public static void StartIfRequested(FogOfWarApp app)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fowqa");
            if (i < 0 || i + 1 >= args.Length) return;
            var qa = app.gameObject.AddComponent<QA>();
            qa.dir = args[i + 1];
            // -fowqascene N limits the per-scene steps to scene N (1-based)
            int j = Array.IndexOf(args, "-fowqascene");
            if (j >= 0 && j + 1 < args.Length && int.TryParse(args[j + 1], out int only)) qa.only = only - 1;
        }

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            if (type != LogType.Warning) errors++;
            if (seen.Add(msg + stack)) log?.WriteLine($"    [{type}] {msg}\n{stack}");
        }

        readonly HashSet<string> seen = new HashSet<string>();

        static void Set(object o, string field, object value)
        {
            var f = o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new MissingFieldException(o.GetType().Name, field);
            f.SetValue(o, f.FieldType.IsEnum ? Enum.ToObject(f.FieldType, value) : Convert.ChangeType(value, f.FieldType));
        }

        static void Call(object o, string method) =>
            o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(o, null);

        // Open System: aim the target camera at a named body.
        static void View(SimScene s, string body, double radii)
        {
            var sys = (OpenWorld.SolarSystem)s.GetType().GetField("sys", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(s);
            var b = sys[body];
            s.GetType().GetMethod("SetTarget", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(s, new object[] { b });
            var pivot = Enum.ToObject(s.GetType().GetNestedType("Pivot", BindingFlags.NonPublic), 1);
            s.GetType().GetMethod("SetPivot", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(s, new object[] { pivot, b.Radius * radii });
        }

        // Open System: park the ship (stationary relative to it) beside a belt rock and frame it.
        static void BeltRock(SimScene s, int k)
        {
            var t = s.GetType();
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
            var belt = (OpenWorld.Belt)t.GetField("belt", F).GetValue(s);
            var ship = (OpenWorld.Ship)t.GetField("ship", F).GetValue(s);
            double jd = (double)t.GetField("jd", F).GetValue(s);
            belt.Update(jd + 1.0 / 86400); var p1 = belt.Pos[k];
            belt.Update(jd); var p0 = belt.Pos[k];
            ship.Plan = OpenWorld.Plan.None; ship.ThrottleCmd = 0; ship.Landed = null;
            ship.Pos = p0 + new OpenWorld.D3(belt.RadiusKm[k] * 6 + 1, 0, belt.RadiusKm[k] * 2);
            ship.Vel = p1 - p0;
            var pivot = Enum.ToObject(t.GetNestedType("Pivot", BindingFlags.NonPublic), 0);
            t.GetMethod("SetPivot", F).Invoke(s, new object[] { pivot, belt.RadiusKm[k] * 9.0 });
        }

        static OpenWorld.D3 flipFrom;
        static int fpsFrame; static float fpsTime;
        static void FpsMark() { fpsFrame = Time.frameCount; fpsTime = Time.realtimeSinceStartup; }
        static void FpsReport(string what) =>
            Debug.LogWarning($"QA fps {what}: {(Time.frameCount - fpsFrame) / Mathf.Max(Time.realtimeSinceStartup - fpsTime, 1e-3f):F1} fps average at {Screen.width}x{Screen.height}, ended {DateTime.UtcNow:HH:mm:ss}Z");

        // QA-019 regression: park in a circular standoff orbit around a planet, then plan a brachistochrone to a far
        // target at maximum time compression.
        static void Depart(SimScene s, string from, string to)
        {
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
            var t = s.GetType();
            var sys = (OpenWorld.SolarSystem)t.GetField("sys", F).GetValue(s);
            var ship = (OpenWorld.Ship)Ship(s);
            double jd = (double)t.GetField("jd", F).GetValue(s);
            var p = sys[from];
            double r = OpenWorld.Ship.Standoff(p);
            var pp = sys.Pos(p, jd); var pv = sys.Vel(p, jd);
            var radial = OpenWorld.D3.Cross(p.Pole, new OpenWorld.D3(1, 0, 0)).Norm;
            var tangent = OpenWorld.D3.Cross(p.Pole, radial).Norm;
            ship.Plan = OpenWorld.Plan.None; ship.Landed = null; ship.ThrottleCmd = 0; ship.Hold = OpenWorld.Hold.Off;
            ship.Pos = pp + radial * r; ship.Vel = pv + tangent * Math.Sqrt(p.GM / r);
            View(s, to, 4);
            Invoke(s, "SetPivot", PivotShip(s), 0.16);
            Set(s, "selectedPlan", OpenWorld.Plan.Brachistochrone);
            Invoke(s, "TogglePlan");
            Set(s, "warpIdx", 6);
            departFrom = p;
        }

        static OpenWorld.Body departFrom;

        static void DepartCheck(SimScene s)
        {
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
            var sys = (OpenWorld.SolarSystem)s.GetType().GetField("sys", F).GetValue(s);
            double jd = (double)s.GetType().GetField("jd", F).GetValue(s);
            var ship = (OpenWorld.Ship)Ship(s);
            double alt = (ship.Pos - sys.Pos(departFrom, jd)).Mag - departFrom.Radius;
            string msg = $"QA depart {departFrom.Name}: {ship.Phase}, {alt:F0} km from its centre minus radius, plan {ship.Plan}";
            if (ship.Landed != null || alt < 0) Debug.LogError(msg); else Debug.LogWarning(msg);
            Set(s, "warpIdx", 0);
        }
        static double helpJd;
        static double Jd(SimScene s) => (double)s.GetType().GetField("jd", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(s);
        static FogOfWarApp App(SimScene s) => (FogOfWarApp)typeof(SimScene).GetField("App", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(s);
        static void Check(bool ok, string msg) { if (ok) Debug.LogWarning(msg + ": ok"); else Debug.LogError(msg); }

        static void CutCheck(SimScene s)
        {
            var sh = (OpenWorld.Ship)Ship(s);
            int warp = (int)s.GetType().GetField("warpIdx", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(s);
            Check(!sh.PlanActive && sh.Throttle == 0 && sh.ThrottleCmd == 0 && warp == 0, $"QA cut during autopilot: plan {sh.Plan}, throttle {sh.Throttle}, warp index {warp}");
        }

        static object Ship(SimScene s) => s.GetType().GetField("ship", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(s);
        static object PivotShip(SimScene s) => Enum.ToObject(s.GetType().GetNestedType("Pivot", BindingFlags.NonPublic), 0);
        static void Invoke(object o, string method, params object[] args)
        {
            foreach (var m in o.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                if (m.Name == method && m.GetParameters().Length == args.Length) { m.Invoke(o, args); return; }
            throw new MissingMethodException(o.GetType().Name, method);
        }

        // name, action, seconds to let it run before the screenshot
        static readonly Dictionary<Type, (string, Action<SimScene>, float)[]> Steps = new Dictionary<Type, (string, Action<SimScene>, float)[]>
        {
            [typeof(WormholeScene)] = new (string, Action<SimScene>, float)[]
            {
                ("fly-through", s => Call(s, "StartFly"), 9),
                ("fly-back", s => Call(s, "StartFly"), 9),
                ("pinch", s => Set(s, "pinchTarget", 1), 4),
                ("reopen-min", s => { Set(s, "pinchTarget", 0); Set(s, "throat", 0.4f); Set(s, "unitPow", 0f); }, 3),
                ("max", s => { Set(s, "throat", 3f); Set(s, "unitPow", 6f); }, 3),
            },
            [typeof(BlackHoleScene)] = new (string, Action<SimScene>, float)[]
            {
                ("stellar", s => Set(s, "massIdx", 0), 2),
                ("ton618-hot", s => { Set(s, "massIdx", 3); Set(s, "outer", 20f); Set(s, "temp", 20000f); }, 2),
                ("cold-small", s => { Set(s, "outer", 6f); Set(s, "temp", 2500f); }, 2),
                ("all-off", s => { Set(s, "disk", false); Set(s, "doppler", false); Set(s, "lens", false); }, 2),
            },
            [typeof(WarpScene)] = new (string, Action<SimScene>, float)[]
            {
                ("max", s => { Set(s, "v", 10f); Set(s, "R", 5f); Set(s, "sigma", 8f); Set(s, "amp", 3f); }, 3),
                ("min", s => { Set(s, "v", 0.1f); Set(s, "R", 1.5f); Set(s, "sigma", 0.6f); Set(s, "amp", 0.2f); }, 3),
                // energy-ratio references (#9): integrated shape gives 0.140 at v 2, R 1.5, σ 0.6 and 0.797 at σ 8
                ("thick-wall-v2", s => { Set(s, "v", 2f); Set(s, "R", 1.5f); Set(s, "sigma", 0.6f); Set(s, "amp", 1.2f); }, 2),
                ("thin-wall-v2", s => Set(s, "sigma", 8f), 2),
            },
            [typeof(JourneyScene)] = new (string, Action<SimScene>, float)[]
            {
                ("proxima", s => { Set(s, "dest", 0); Set(s, "tau", 0.0); Call(s, "BuildSky"); }, 3),
                ("andromeda-3g-fast", s => { Set(s, "dest", 3); Set(s, "tau", 0.0); Call(s, "BuildSky"); Set(s, "acc", 3f); Set(s, "speed", 4f); }, 5),
                ("arrived", s => Set(s, "tau", 1e9), 2),
                ("chase-braking", s => { Set(s, "dest", 2); Set(s, "acc", 1f); Call(s, "BuildSky"); Set(s, "tau", 14.0); ((JourneyScene)s).ShowChase(); }, 5),
                ("low-g-no-cmb", s => { Set(s, "acc", 0.1f); Set(s, "tau", 0.0); Set(s, "cmb", false); Set(s, "paused", true); }, 2),
            },
            [typeof(NegativeMassScene)] = new (string, Action<SimScene>, float)[]
            {
                ("swarm", s => { Set(s, "preset", 1); Call(s, "Reset"); Set(s, "speed", 4f); }, 8),
                ("only-negative", s => { Set(s, "preset", 2); Call(s, "Reset"); }, 8),
                ("orbit-nofollow", s => { Set(s, "preset", 3); Call(s, "Reset"); Set(s, "follow", false); }, 8),
            },
            [typeof(TidesScene)] = new (string, Action<SimScene>, float)[]
            {
                ("half", s => Set(s, "d", 384400f / 2), 3),
                ("roche", s => Set(s, "d", 16000f), 6),
                ("min-maxwarp", s => { Set(s, "d", 12000f); Set(s, "warp", 200000f); }, 4),
                ("far-bigmoon", s => { Set(s, "d", 500000f); Set(s, "bigMoon", true); Set(s, "exag", 2e6f); }, 4),
            },
            [typeof(MarsScene)] = new (string, Action<SimScene>, float)[]
            {
                ("today", s => { Set(s, "tau", 0.05f); Set(s, "albedo", 0.25f); Set(s, "lapse", 2.5f); Set(s, "pressure", 6.1f); Set(s, "years", 0.0); Set(s, "cwarp", 10f); }, 6),
                ("runaway-terrain", s => { Set(s, "tau", 12f); Set(s, "albedo", 0.2f); Set(s, "lapse", 6.8f); Set(s, "years", 0.0); Set(s, "mode", 1); }, 8),
                ("relief-max", s => Set(s, "relief", 20f), 2),
                ("flat-icy", s => { Set(s, "relief", 0f); Set(s, "tau", 0f); Set(s, "albedo", 0.6f); Set(s, "lapse", 0f); }, 6),
            },
            [typeof(OpenSystemScene)] = new (string, Action<SimScene>, float)[]
            {
                ("pitch-held", s => { Controls.Hold(Controls.Action.PitchUp, true); }, 1.5f),
                ("assist-stops-rotation", s => { Controls.Hold(Controls.Action.PitchUp, false); }, 3f),
                ("assist-off-yaw", s => { Set(Ship(s), "FlightAssist", false); Controls.Hold(Controls.Action.YawRight, true); }, 1f),
                ("assist-off-keeps-spinning", s => { Controls.Hold(Controls.Action.YawRight, false); }, 2f),
                ("hold-prograde", s => { Set(Ship(s), "FlightAssist", true); Invoke(s, "SetHold", OpenWorld.Hold.Prograde); }, 6f),
                ("throttle-full", s => Controls.Tap(Controls.Action.ThrottleFull), 2f),
                ("throttle-cut", s => Controls.Tap(Controls.Action.ThrottleCut), 1f),
                ("rcs-forward", s => Controls.Hold(Controls.Action.RcsForward, true), 2f),
                ("rcs-release", s => { Controls.Hold(Controls.Action.RcsForward, false); Invoke(s, "SetHold", OpenWorld.Hold.Off); }, 1f),
                ("flip", s => { flipFrom = ((OpenWorld.Ship)Ship(s)).Heading; Controls.Tap(Controls.Action.Flip); }, 8f),
                ("flip-check", s => { var h = ((OpenWorld.Ship)Ship(s)).Heading; Debug.LogWarning($"QA flip: nose turned {Math.Acos(Math.Max(-1, Math.Min(1, OpenWorld.D3.Dot(h, flipFrom)))) * 180 / Math.PI:F1} deg (hold {((OpenWorld.Ship)Ship(s)).Hold}, plan {((OpenWorld.Ship)Ship(s)).Plan}, warp {s.GetType().GetField("warpIdx", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(s)}, angvel {((OpenWorld.Ship)Ship(s)).AngVel.magnitude:F2})"); }, 0.5f),
                ("cam-orbit", s => Controls.Tap(Controls.Action.CameraMode), 1.5f),
                ("cam-cockpit", s => Controls.Tap(Controls.Action.CameraMode), 1.5f),
                // the cabin: red high-g lighting under a 3 g burn, then a look round the interior
                ("cockpit-burn-3g", s => { Set(Ship(s), "MaxG", 3.0); Controls.Tap(Controls.Action.ThrottleFull); }, 2.5f),
                ("cockpit-look-down", s => { Controls.Tap(Controls.Action.ThrottleCut); Set(Ship(s), "MaxG", 1.0); Set(s, "lookPitch", 40f); }, 1.5f),
                ("cockpit-look-left", s => { Set(s, "lookPitch", 5f); Set(s, "lookYaw", -75f); }, 1f),
                ("cockpit-look-back", s => { Set(s, "lookPitch", 15f); Set(s, "lookYaw", 150f); }, 1f),
                ("cockpit-wide", s => { Set(s, "lookPitch", 0f); Set(s, "lookYaw", 0f); Camera.main.fieldOfView = 90; }, 1f),
                // frame rate, cockpit vs chase, from the same spot: frames over 8 s each
                ("fps-cockpit", s => { Camera.main.fieldOfView = 55; FpsMark(); }, 8f),
                ("fps-cockpit-done", s => FpsReport("cockpit"), 0.1f),
                ("cam-chase", s => Controls.Tap(Controls.Action.CameraMode), 1.5f),
                ("fps-chase", s => FpsMark(), 8f),
                ("fps-chase-done", s => FpsReport("chase"), 0.1f),
                ("match-velocity-moon", s => { View(s, "Moon", 4); Set(s, "selectedPlan", OpenWorld.Plan.MatchVelocity); Invoke(s, "TogglePlan"); Set(s, "warpIdx", 2); Invoke(s, "SetPivot", PivotShip(s), 0.16); }, 10f),
                // #15: X during an autopilot burn stops the drive and the plan and drops to 1×; then re-engage for the next step
                ("cut-autopilot", s => { Set(Ship(s), "Throttle", 1.0); Controls.Tap(Controls.Action.ThrottleCut); }, 0.5f),
                ("cut-autopilot-check", s => { CutCheck(s); Invoke(s, "TogglePlan"); Set(s, "warpIdx", 2); }, 2f),
                // #17: the help sheet pauses flight
                ("help-pauses", s => { helpJd = Jd(s); App(s).HelpOpen = true; }, 1f),
                ("help-pauses-check", s => { Check(Jd(s) == helpJd, $"QA help: game clock {(Jd(s) - helpJd) * 86400:F1} s on while help was open"); App(s).HelpOpen = false; }, 0.5f),
                ("hold-station", s => { Invoke(s, "TogglePlan"); Set(s, "selectedPlan", OpenWorld.Plan.HoldStation); Invoke(s, "TogglePlan"); }, 6f),
                ("plot-moon-100k", s => { Invoke(s, "TogglePlan"); View(s, "Moon", 4); Set(s, "selectedPlan", OpenWorld.Plan.Brachistochrone); Invoke(s, "TogglePlan"); Set(s, "warpIdx", 5); Invoke(s, "SetPivot", PivotShip(s), 0.16); }, 12f),
                ("moon-parked", s => Set(s, "warpIdx", 0), 2f),
                ("save", s => { Invoke(s, "TogglePlan"); Set(s, "warpIdx", 0); ((OpenSystemScene)s).SaveGame(); }, 1f),
                ("load", s => { Set(s, "jd", 2451545.0); ((OpenSystemScene)s).LoadGame(); }, 1f),
                ("map", s => Call(s, "ToggleMap"), 3),
                ("ship", s => Call(s, "ToggleMap"), 2),
                ("plot-mars-100k", s => { Call(s, "TogglePlot"); Set(s, "warpIdx", 5); }, 5),
                ("mars-arrival", s => { }, 15),
                ("mars-target-cam", s => { Set(s, "warpIdx", 0); s.GetType().GetMethod("SetPivot", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(s, new object[] { Enum.ToObject(s.GetType().GetNestedType("Pivot", BindingFlags.NonPublic), 1), 15000.0 }); }, 3),
                ("phobos-hold", s => { Call(s, "TogglePlot"); View(s, "Phobos", 4); Call(s, "TogglePlot"); Set(s, "warpIdx", 3); }, 12),
                ("jupiter-1M", s => { Call(s, "TogglePlot"); View(s, "Jupiter", 4); Call(s, "TogglePlot"); Set(s, "warpIdx", 6); }, 12),
                ("cut-drive", s => { Call(s, "TogglePlot"); Set(s, "warpIdx", 0); }, 1),
                ("view-venus", s => { Set(s, "warpIdx", 0); View(s, "Venus", 3.5); }, 2),
                ("view-saturn", s => View(s, "Saturn", 4), 2),
                ("view-earth", s => View(s, "Earth", 3), 2),
                ("view-phobos", s => View(s, "Phobos", 4), 2),
                ("view-saturn-rings", s => { View(s, "Saturn", 7); Set(s, "pitch", 18f); }, 2),
                ("view-jupiter-system", s => View(s, "Jupiter", 40), 2),
                ("view-io", s => View(s, "Io", 3), 2),
                ("view-europa", s => View(s, "Europa", 3), 2),
                ("view-titan", s => View(s, "Titan", 3), 2),
                ("view-enceladus", s => View(s, "Enceladus", 3), 2),
                ("view-uranus-faint-rings", s => { View(s, "Uranus", 6); Set(s, "faintRings", true); }, 2),
                ("view-triton", s => { Set(s, "faintRings", false); View(s, "Triton", 3); }, 2),
                ("autopilot-off", s => { var sh = (OpenWorld.Ship)s.GetType().GetField("ship", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(s); sh.Plan = OpenWorld.Plan.None; }, 1),
                ("belt-map", s => { Set(s, "warpIdx", 0); Call(s, "ToggleMap"); Set(s, "showHist", true); }, 2),
                ("view-ceres", s => { Call(s, "ToggleMap"); View(s, "Ceres", 3); }, 2),
                ("view-vesta", s => View(s, "Vesta", 3), 2),
                ("plot-ceres-1M", s => { View(s, "Ceres", 4); Call(s, "TogglePlot"); Set(s, "warpIdx", 6); }, 12),
                ("belt-rock", s => { Call(s, "TogglePlot"); Set(s, "warpIdx", 0); BeltRock(s, 1234); }, 2),
                ("depart-jupiter", s => Depart(s, "Jupiter", "Ceres"), 4),
                ("depart-jupiter-check", s => DepartCheck(s), 0.5f),
                ("depart-saturn", s => Depart(s, "Saturn", "Ceres"), 4),
                ("depart-saturn-check", s => DepartCheck(s), 0.5f),
                ("depart-uranus", s => Depart(s, "Uranus", "Mars"), 4),
                ("depart-uranus-check", s => DepartCheck(s), 0.5f),
                ("depart-neptune", s => Depart(s, "Neptune", "Mars"), 4),
                ("depart-neptune-check", s => DepartCheck(s), 0.5f),
                ("plot-titan-1M", s => { View(s, "Titan", 6); Call(s, "TogglePlot"); Set(s, "warpIdx", 6); }, 15),
            },
            [typeof(MeteorScene)] = new (string, Action<SimScene>, float)[]
            {
                ("geminids-night", s => { Set(s, "shower", 2); Set(s, "timeWarp", 600f); }, 20),
                ("storm-600x", s => { Set(s, "shower", 3); Set(s, "timeWarp", 600f); }, 8),
                ("far-tx", s => Set(s, "txKm", 2600f), 5),
                ("near-tx-nopaths", s => { Set(s, "txKm", 200f); Set(s, "paths", false); }, 5),
            },
        };

        IEnumerator UiPass()
        {
            // the boot menu, before any game is started (QA-011: no Resume / Save here)
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("ui-boot-menu");
            app.Activate(app.GameIndex);
            yield return new WaitForSecondsRealtime(2f);
            log.WriteLine($"screen {UnityEngine.Screen.width}x{UnityEngine.Screen.height}, ui scale {app.UiScale:F2}");
            foreach (var (name, m) in new[] { ("menu-main", 1), ("menu-observatory", 2), ("menu-controls", 3) })
            { app.ShowMenu(m); yield return new WaitForSecondsRealtime(0.5f); yield return Shot("ui-" + name); }
            app.ShowMenu(0);
            yield return new WaitForSecondsRealtime(0.5f); yield return Shot("ui-game");
            app.HelpOpen = true; yield return new WaitForSecondsRealtime(0.5f); yield return Shot("ui-help"); app.HelpOpen = false;
            app.Activate(1); yield return new WaitForSecondsRealtime(1f); yield return Shot("ui-observatory-scene");
            log.WriteLine($"done: {errors} errors, {bad} bad readouts");
            log.Close(); log = null;
            Application.Quit();
        }

        IEnumerator Start() => Guard(Run());

        // Runs a coroutine tree by hand so an exception anywhere in it (nested Shot() included) ends the run: log it,
        // write the summary and quit. Unity would otherwise drop the coroutine and leave the process running forever.
        IEnumerator Guard(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                bool more;
                try { more = top.MoveNext(); }
                catch (Exception e)
                {
                    errors++;
                    if (log == null) { Directory.CreateDirectory(dir); log = new StreamWriter(Path.Combine(dir, "qa.txt"), true); }
                    log.WriteLine($"QA aborted: {e}");
                    log.WriteLine($"done: {errors} errors, {bad} bad readouts (aborted)");
                    log.Close(); log = null;
                    Application.Quit();
                    yield break;
                }
                if (!more) { stack.Pop(); continue; }
                if (top.Current is IEnumerator inner) stack.Push(inner);
                else yield return top.Current;
            }
        }

        IEnumerator Run()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fowui") >= 0)
            {
                Directory.CreateDirectory(dir);
                log = new StreamWriter(Path.Combine(dir, "qa.txt")) { AutoFlush = true };
                app = GetComponent<FogOfWarApp>();
                app.InputLocked = true; Controls.Locked = true;
                yield return UiPass();
                yield break;
            }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fowqaterrain") >= 0)
            {
                // just the terrain low passes (the full run includes them too)
                Directory.CreateDirectory(dir);
                log = new StreamWriter(Path.Combine(dir, "qa.txt")) { AutoFlush = true };
                app = GetComponent<FogOfWarApp>();
                app.InputLocked = true; Controls.Locked = true;
                log.WriteLine($"screen {UnityEngine.Screen.width}x{UnityEngine.Screen.height}");
                yield return TerrainPasses();
                log.WriteLine($"done: {errors} errors, {bad} bad readouts");
                log.Close(); log = null;
                Application.Quit();
                yield break;
            }
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, "qa.txt")) { AutoFlush = true };
            app = GetComponent<FogOfWarApp>();
            app.InputLocked = true;
            Controls.Locked = true;
            int startQuality = app.Quality;
            PhysicsChecks();

            for (int i = 0; i < app.SceneCount; i++)
            {
                if (only >= 0 && i != only) continue;
                app.Activate(i);
                var scene = app.Current;
                log.WriteLine($"scene {i + 1}: {scene.Title}");
                yield return Shot($"{i + 1}-default");
                if (!Steps.TryGetValue(scene.GetType(), out var steps)) continue;
                foreach (var (name, act, wait) in steps)
                {
                    try { act(scene); }
                    catch (Exception e) { errors++; log.WriteLine($"    step {name} threw: {e.InnerException?.Message ?? e.Message}"); }
                    yield return new WaitForSecondsRealtime(wait);
                    yield return Shot($"{i + 1}-{name}");
                }
            }

            log.WriteLine("quality modes on the ray tracers");
            foreach (int s in new[] { 0, 1 })
                foreach (int q in new[] { 0, 2 })
                {
                    app.Activate(s);
                    app.Quality = q;
                    yield return new WaitForSecondsRealtime(1.5f);
                    yield return Shot($"{s + 1}-quality{q}");
                }
            app.Quality = startQuality;

            log.WriteLine("rapid scene switching, 5 rounds");
            long mem0 = GC.GetTotalMemory(true);
            // QA-026: log the heap after every round, so a leak (steady growth) is told apart from caches that level off
            for (int round = 0; round < 20; round++)
            {
                for (int i = 0; i < app.SceneCount; i++) { app.Activate(i); yield return null; yield return null; }
                if (round % 4 == 3) { yield return Resources.UnloadUnusedAssets(); log.WriteLine($"    after round {round + 1}: managed heap {GC.GetTotalMemory(true) / 1048576} MB"); }
            }
            yield return Resources.UnloadUnusedAssets();
            log.WriteLine($"    managed heap {mem0 / 1048576} MB -> {GC.GetTotalMemory(true) / 1048576} MB");

            // New game while a rescue is active and its wreck (with lamps) is drawn nearby: the old wreck's objects must
            // not outlive the career (they spammed NullReferenceExceptions from LateWreck)
            log.WriteLine("new game during a rescue");
            int errBefore = errors;
            app.Restart(app.GameIndex);
            yield return new WaitForSecondsRealtime(1.5f);
            // under -fowqa the career starts in sandbox mode, which never docks; Career.Rescue() posts from the port the
            // ship is docked at, so switch sandbox off and let the first ticks dock it
            if ((app.Current as OpenSystemScene)?.Career is OpenWorld.Career cr) cr.Sandbox = false;
            for (int k = 0; k < 50 && (app.Current as OpenSystemScene)?.Career?.Docked == null; k++) yield return new WaitForSecondsRealtime(0.1f);
            if (app.Current is OpenSystemScene os && os.Career?.Docked != null)
            {
                const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
                var sys = (OpenWorld.SolarSystem)typeof(OpenSystemScene).GetField("sys", F).GetValue(os);
                double jd = (double)typeof(OpenSystemScene).GetField("jd", F).GetValue(os);
                var job = os.Career.Board.Find(c => c.Kind == OpenWorld.JobKind.Rescue) ?? os.Career.Rescue(new System.Random(7), sys, jd);
                os.Career.Accept(job, jd);
                var wreck = sys[job.To];
                if (wreck != null)
                {
                    var ship = (OpenWorld.Ship)Ship(os);
                    ship.Plan = OpenWorld.Plan.None; ship.Landed = null; ship.ThrottleCmd = 0;
                    ship.Pos = sys.Pos(wreck, jd) + new OpenWorld.D3(2, 0, 0); ship.Vel = sys.Vel(wreck, jd); // undocks itself
                }
                yield return new WaitForSecondsRealtime(2f);
                yield return Shot("9-rescue-wreck");
                app.NewGame();
                yield return new WaitForSecondsRealtime(3f);
                yield return Shot("9-newgame-after-rescue");
                log.WriteLine($"    rescue {job.To}, wreck {(wreck != null ? "found" : "MISSING")}, errors during new game: {errors - errBefore}");
            }
            else { errors++; log.WriteLine($"    new career is not docked ({app.Current?.Title}), so no rescue can be posted"); }

            yield return FleetShots();
            yield return TerrainPasses();

            log.WriteLine("window sizes");
            foreach (var (w, h) in new[] { (1280, 720), (1024, 768), (2560, 1440) })
            {
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(1.5f);
                foreach (int s in new[] { 3, 7 }) { app.Activate(s); yield return new WaitForSecondsRealtime(1f); yield return Shot($"res{w}x{h}-{s + 1}"); }
            }

            log.WriteLine($"done: {errors} errors, {bad} bad readouts");
            log.Close();
            log = null;
            Application.Quit();
        }

        // Issues #8 and #13: bodies' positions and velocities stay continuous across the ends of the JPL fits, and a
        // powered step is exact under constant thrust, reaction mass running out mid-step included, at any step size.
        void PhysicsChecks()
        {
            log.WriteLine("physics regressions");
            var sys = OpenWorld.SolarSystem.Build();
            foreach (var b in sys.Bodies)
            {
                if (b.Cheb == null) continue;
                foreach (double e in new[] { b.Cheb.JdStart, b.Cheb.JdEnd })
                {
                    double typ = Math.Max(sys.Vel(b, e - 1).Mag, sys.Vel(b, e + 1).Mag), worst = 0;
                    for (double t = e - 0.01; t <= e + 0.01; t += 0.0005) worst = Math.Max(worst, sys.Vel(b, t).Mag);
                    double step = (sys.PosAt(b, e + 1e-7) - sys.PosAt(b, e - 1e-7)).Mag;
                    if (worst > 1.1 * typ || step > 5) { errors++; log.WriteLine($"    BAD {b.Name} at JD {e}: speed up to {worst:F1} km/s (typical {typ:F1}), position step {step:F1} km"); }
                }
            }
            var empty = new OpenWorld.SolarSystem();
            const double g0 = OpenWorld.Ship.G0;
            foreach (double h in new[] { 1.0, 30, 60 })
            {
                // 300 s at 1 g: unlimited, then with reaction mass for exactly 100 s (coasting the rest)
                foreach (bool limited in new[] { false, true })
                {
                    var ship = new OpenWorld.Ship { ThrottleCmd = 1, UnlimitedFuel = !limited };
                    if (limited) ship.Fuel = (ship.DryMass + ship.Cargo) * (Math.Exp(g0 * 100 / OpenWorld.Ship.Ve) - 1);
                    double tb = limited ? 100 : 300, x = 0.5 * g0 * tb * tb + g0 * tb * (300 - tb), jd = 2461317.5;
                    for (int k = 0; k < Math.Round(300 / h); k++) { ship.Step(empty, jd, h, null); jd += h / 86400; }
                    double ex = Math.Abs(ship.Pos.Mag / x - 1), ev = Math.Abs(ship.Vel.Mag / (g0 * tb) - 1);
                    string msg = $"    1 g burn{(limited ? ", fuel for 100 s" : "")}, h {h} s: position error {ex:E1}, velocity error {ev:E1}";
                    if (ex > 1e-9 || ev > 1e-9) { errors++; msg += "  BAD"; }
                    log.WriteLine(msg);
                }
            }
        }

        // The intercept runner for each class in Fleet.json: post an intercept for that hull, hold beside it and look at it.
        // Logs which model the scene spawned (a class without its own model falls back to the torch ship).
        IEnumerator FleetShots()
        {
            log.WriteLine("fleet runners");
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (var (cls, name) in new[] { ("Courier", "Halcyon"), ("Packet", "Wren"), ("Liner", "Meridian"), ("Freighter", "Okavango"), ("Tanker", "Ross 128"), ("Hauler", "Tisiphone") })
            {
                app.Restart(app.GameIndex);
                yield return new WaitForSecondsRealtime(1f);
                var os = app.Current as OpenSystemScene;
                if (os?.Career == null) { errors++; log.WriteLine("    the game scene isn't the open world"); yield break; }
                os.Career.Sandbox = false;
                for (int k = 0; k < 50 && os.Career.Docked == null; k++) yield return new WaitForSecondsRealtime(0.1f);
                var sys = (OpenWorld.SolarSystem)typeof(OpenSystemScene).GetField("sys", F).GetValue(os);
                double jd = (double)typeof(OpenSystemScene).GetField("jd", F).GetValue(os);
                OpenWorld.Contract job = null;
                for (int seed = 1; seed < 400 && job == null && os.Career.Docked != null; seed++)
                {
                    var c = os.Career.Intercept(new System.Random(seed), sys, jd);
                    if (c != null && c.To.Contains(" " + name + " ")) job = c;
                }
                if (job == null) { log.WriteLine($"    {cls}: no intercept posted (docked {os.Career.Docked?.Name ?? "-"})"); continue; }
                os.Career.Accept(job, jd);
                var runner = sys[job.To];
                if (runner == null) { errors++; log.WriteLine($"    {cls}: accepted but no runner body"); continue; }
                var ship = (OpenWorld.Ship)Ship(os);
                ship.Plan = OpenWorld.Plan.None; ship.ThrottleCmd = 0; ship.Landed = null;
                ship.Pos = sys.Pos(runner, jd) + new OpenWorld.D3(0, 0, 2); ship.Vel = sys.Vel(runner, jd);
                typeof(OpenSystemScene).GetMethod("SetTarget", F).Invoke(os, new object[] { runner });
                yield return new WaitForSecondsRealtime(0.5f);
                float km = (float)typeof(OpenSystemScene).GetField("runnerKm", F).GetValue(os);
                Invoke(os, "SetPivot", Enum.ToObject(typeof(OpenSystemScene).GetNestedType("Pivot", BindingFlags.NonPublic), 1), (double)km * 2.5);
                yield return new WaitForSecondsRealtime(1.5f);
                var rt = (Transform)typeof(OpenSystemScene).GetField("runnerT", F).GetValue(os);
                string model = rt != null && rt.childCount > 0 ? rt.GetChild(0).name : "none";
                log.WriteLine($"    {cls}: {job.To}, model {model}, {km * 1000:0} m");
                // QA-065: the target line is the runner's course, with no chords across its destination's orbit
                string course = os.TestTargetLineReport(out double longest);
                bool lineOk = longest > 0 && longest < 0.05;
                if (!lineOk) errors++;
                log.WriteLine($"    {cls} course line: {course}{(lineOk ? "" : "  BAD")}");
                yield return Shot($"9-fleet-{cls.ToLowerInvariant()}");
            }
        }

        // Streaming terrain low passes: Tycho, Valles Marineris and Caloris from 100, 10 and 1 km, in local morning light.
        IEnumerator TerrainPasses()
        {
            log.WriteLine("terrain low passes");
            app.Restart(app.GameIndex);
            yield return new WaitForSecondsRealtime(1f);
            var os = app.Current as OpenSystemScene;
            if (os == null) { errors++; log.WriteLine("    the game scene isn't the open world"); yield break; }
            foreach (var (body, site, lat, lon) in new[] { ("Moon", "tycho", -43.31, -11.36), ("Mars", "valles", -13.9, -59.2), ("Mercury", "caloris", 30.5, -170.2) })
            {
                bool first = true;
                foreach (var (alt, pitch) in new[] { (100.0, 35.0), (10.0, 20.0), (1.0, 10.0) })
                {
                    string line = os.TestLowPass(body, lat, lon, alt, pitch, first);
                    first = false;
                    log.WriteLine("    " + line);
                    if (line.Contains("no terrain")) { errors++; break; }
                    // let the quadtree refine: until nothing is queued for a few frames, at most 12 s
                    float t0 = Time.realtimeSinceStartup;
                    int idle = 0;
                    while (Time.realtimeSinceStartup - t0 < 12f && idle < 20)
                    {
                        yield return null;
                        idle = OpenWorld.TerrainBody.Pending == 0 ? idle + 1 : 0;
                    }
                    float settled = Time.realtimeSinceStartup - t0;
                    // steady frame rate once the meshes are built (the shot line's fps includes the build frames)
                    int f0 = Time.frameCount; float s0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - s0 < 3f) yield return null;
                    float fps = (Time.frameCount - f0) / (Time.realtimeSinceStartup - s0);
                    log.WriteLine($"    {os.TestTerrainReport(body)}, settled in {settled:0.0} s, steady {fps:0} fps at {Screen.width}x{Screen.height}, " +
                                  $"heap {GC.GetTotalMemory(false) >> 20} MB, gfx {UnityEngine.Profiling.Profiler.GetAllocatedMemoryForGraphicsDriver() >> 20} MB");
                    yield return Shot($"10-terrain-{site}-{(alt >= 1 ? alt.ToString("0") : alt.ToString("0.0"))}km");
                }
            }
            os.TestLowPassOff();
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            var list = new List<Readout>();
            app.Current.Readouts(list);
            var flagged = new List<string>();
            foreach (var r in list)
                if (r.value.Contains("NaN") || r.value.Contains("Infinity") || r.value.Contains("∞")) flagged.Add($"{r.label}={r.value}");
            bad += flagged.Count;
            if (app.Current is OpenSystemScene) foreach (var r in list) log.WriteLine($"      {r.label}: {r.value}");
            log.WriteLine($"  {name}: {1f / Time.smoothDeltaTime:F0} fps{(flagged.Count > 0 ? "  BAD: " + string.Join(", ", flagged) : "")}");
        }
    }
}
