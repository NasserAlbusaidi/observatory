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
        int errors, bad;
        StreamWriter log;
        FogOfWarApp app;

        public static void StartIfRequested(FogOfWarApp app)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fowqa");
            if (i < 0 || i + 1 >= args.Length) return;
            app.gameObject.AddComponent<QA>().dir = args[i + 1];
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
            f.SetValue(o, Convert.ChangeType(value, f.FieldType));
        }

        static void Call(object o, string method) =>
            o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(o, null);

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
                ("today", s => { Set(s, "tau", 0.05f); Set(s, "albedo", 0.25f); Set(s, "lapse", 2.5f); Set(s, "years", 0.0); Set(s, "cwarp", 10f); }, 6),
                ("runaway-terrain", s => { Set(s, "tau", 12f); Set(s, "albedo", 0.2f); Set(s, "lapse", 6.8f); Set(s, "years", 0.0); Set(s, "mode", 1); }, 8),
                ("relief-max", s => Set(s, "relief", 20f), 2),
                ("flat-icy", s => { Set(s, "relief", 0f); Set(s, "tau", 0f); Set(s, "albedo", 0.6f); Set(s, "lapse", 0f); }, 6),
            },
            [typeof(MeteorScene)] = new (string, Action<SimScene>, float)[]
            {
                ("storm-600x", s => { Set(s, "shower", 3); Set(s, "timeWarp", 600f); }, 8),
                ("far-tx", s => Set(s, "txKm", 2600f), 5),
                ("near-tx-nopaths", s => { Set(s, "txKm", 200f); Set(s, "paths", false); }, 5),
            },
        };

        IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, "qa.txt")) { AutoFlush = true };
            app = GetComponent<FogOfWarApp>();
            app.InputLocked = true;
            int startQuality = app.Quality;

            for (int i = 0; i < app.SceneCount; i++)
            {
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
            for (int round = 0; round < 5; round++)
                for (int i = 0; i < app.SceneCount; i++) { app.Activate(i); yield return null; yield return null; }
            yield return Resources.UnloadUnusedAssets();
            log.WriteLine($"    managed heap {mem0 / 1048576} MB -> {GC.GetTotalMemory(true) / 1048576} MB");

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
            log.WriteLine($"  {name}: {1f / Time.smoothDeltaTime:F0} fps{(flagged.Count > 0 ? "  BAD: " + string.Join(", ", flagged) : "")}");
        }
    }
}
