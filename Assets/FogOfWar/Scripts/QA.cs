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
        // 0 clean, 1 errors or bad readouts, 2 aborted before the end: a runner can gate on the process, not the report
        int ExitCode => errors + bad == 0 ? 0 : 1;

        static void Set(object o, string field, object value)
        {
            FieldInfo f = null;
            for (var type = o.GetType(); type != null && f == null; type = type.BaseType)
                f = type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new MissingFieldException(o.GetType().Name, field);
            f.SetValue(o, f.FieldType.IsEnum ? Enum.ToObject(f.FieldType, value) : Convert.ChangeType(value, f.FieldType));
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
            [typeof(MeteorScene)] = new (string, Action<SimScene>, float)[]
            {
                ("geminids-night", s => { Set(s, "shower", 2); Set(s, "timeWarp", 600f); }, 20),
                ("storm-600x", s => { Set(s, "shower", 3); Set(s, "timeWarp", 600f); }, 8),
                ("far-tx", s => Set(s, "txKm", 2600f), 5),
                ("near-tx-nopaths", s => { Set(s, "txKm", 200f); Set(s, "paths", false); }, 5),
            },
            [typeof(ThreeBodyScene)] = new (string, Action<SimScene>, float)[]
            {
                ("figure-eight", s => Call(s, "Reset"), 8),
                ("triangle-nudged-fast", s => { Set(s, "preset", 1); Set(s, "kick", 0.2f); Call(s, "Reset"); Set(s, "speed", 4f); }, 8),
                ("close-encounter", s => { Set(s, "preset", 2); Set(s, "kick", -0.2f); Call(s, "Reset"); }, 8),
                ("paused-no-trails", s => { Set(s, "paused", true); Set(s, "showTrails", false); }, 2),
                ("replay", s => { Set(s, "showTrails", true); Call(s, "Reset"); }, 2),
            },
            [typeof(DoublePendulumScene)] = new (string, Action<SimScene>, float)[]
            {
                ("chaotic-twins", s => Call(s, "Reset"), 15),
                ("gentle", s => { Set(s, "preset", 1); Call(s, "ApplyPreset"); }, 4),
                ("upright-fast", s => { Set(s, "preset", 2); Set(s, "gravity", 20f); Set(s, "differencePower", 0f); Set(s, "speed", 4f); Call(s, "ApplyPreset"); }, 8),
                ("tiny-difference-low-g", s => { Set(s, "gravity", 1f); Set(s, "differencePower", -4f); Call(s, "Reset"); }, 4),
                ("paused-no-trails", s => { Set(s, "paused", true); Set(s, "showTrails", false); }, 2),
                ("replay", s => { Set(s, "showTrails", true); Call(s, "Reset"); }, 2),
            },
            [typeof(LorenzScene)] = new (string, Action<SimScene>, float)[]
            {
                ("butterfly-twins", s => Call(s, "Reset"), 15),
                ("low-convection", s => { Set(s, "rho", 1f); Set(s, "offsetPower", -6f); Call(s, "Reset"); }, 4),
                ("wide-wings", s => { Set(s, "rho", 45f); Set(s, "offsetPower", -1f); Call(s, "Reset"); }, 6),
            },
            [typeof(ReactionDiffusionScene)] = new (string, Action<SimScene>, float)[]
            {
                ("growing-spots", s => Call(s, "Reset"), 20),
                ("low-feed", s => { Set(s, "feed", 0.01f); Set(s, "kill", 0.04f); Call(s, "Reset"); }, 6),
                ("high-feed", s => { Set(s, "feed", 0.08f); Set(s, "kill", 0.075f); Call(s, "Reset"); }, 6),
            },
            [typeof(WaveTankScene)] = new (string, Action<SimScene>, float)[]
            {
                ("in-phase", s => Call(s, "Reset"), 5),
                ("opposite-phase", s => { Set(s, "phase", 180f); Set(s, "frequency", 1.5f); Set(s, "separation", 4f); Set(s, "waveSpeed", 12f); Call(s, "Reset"); }, 5),
                ("low-frequency", s => { Set(s, "phase", 0f); Set(s, "frequency", 0.15f); Set(s, "separation", 32f); Set(s, "waveSpeed", 4f); Call(s, "Reset"); }, 5),
            },
            [typeof(GravityAssistScene)] = new (string, Action<SimScene>, float)[]
            {
                ("speed-gain", s => { Set(s, "speed", 4f); Call(s, "Reset"); }, 8),
                ("speed-loss", s => { Set(s, "side", 1); Set(s, "impact", 0.8f); Set(s, "incoming", 0.8f); Set(s, "planetSpeed", 1.5f); Call(s, "Reset"); }, 8),
                ("distant-fast-pass", s => { Set(s, "impact", 4f); Set(s, "incoming", 2.5f); Set(s, "planetSpeed", 0f); Call(s, "Reset"); }, 5),
            },
            [typeof(LagrangeScene)] = new (string, Action<SimScene>, float)[]
            {
                ("stable-l4", s => Call(s, "Reset"), 8),
                ("unstable-l5", s => { Set(s, "mu", 0.15f); Set(s, "offset", 0.15f); Set(s, "point", 1); Call(s, "Reset"); }, 8),
                ("small-secondary", s => { Set(s, "mu", 0.001f); Set(s, "offset", -0.15f); Call(s, "Reset"); }, 5),
            },
            [typeof(ChargedParticleScene)] = new (string, Action<SimScene>, float)[]
            {
                ("magnetic-only", s => { Set(s, "electric", 0f); Set(s, "magnetic", 3f); Set(s, "pitch", 75f); Call(s, "Reset"); }, 5),
                ("negative-drift", s => { Set(s, "sign", 1); Set(s, "electric", 1f); Set(s, "magnetic", 0.3f); Set(s, "pitch", 0f); Call(s, "Reset"); }, 6),
            },
            [typeof(RutherfordScene)] = new (string, Action<SimScene>, float)[]
            {
                ("scattered-beam", s => { Set(s, "speed", 4f); Call(s, "Reset"); }, 6),
                ("low-energy-high-charge", s => { Set(s, "energy", 0.5f); Set(s, "charge", 3f); Set(s, "spread", 5f); Call(s, "Reset"); }, 8),
                ("high-energy-narrow", s => { Set(s, "energy", 5f); Set(s, "charge", 0.5f); Set(s, "spread", 0.5f); Call(s, "Reset"); }, 6),
            },
            [typeof(ResonanceScene)] = new (string, Action<SimScene>, float)[]
            {
                ("resonance", s => Call(s, "Reset"), 8),
                ("low-drive-low-damping", s => { Set(s, "drive", 0.7f); Set(s, "damping", 0.05f); Call(s, "Reset"); }, 6),
                ("high-drive-high-damping", s => { Set(s, "drive", 3.3f); Set(s, "damping", 1f); Call(s, "Reset"); }, 6),
            },
            [typeof(FlockingScene)] = new (string, Action<SimScene>, float)[]
            {
                ("emergent-flock", s => Call(s, "Reset"), 10),
                ("ballistic", s => { Set(s, "separation", 0f); Set(s, "alignment", 0f); Set(s, "cohesion", 0f); Call(s, "Reset"); }, 5),
                ("strong-steering", s => { Set(s, "separation", 3f); Set(s, "alignment", 3f); Set(s, "cohesion", 3f); Call(s, "Reset"); }, 5),
            },
            [typeof(GalaxyEncounterScene)] = new (string, Action<SimScene>, float)[]
            {
                ("tidal-tails", s => { Set(s, "speed", 4f); Call(s, "Reset"); }, 8),
                ("near-head-on", s => { Set(s, "impact", 0.2f); Set(s, "approach", 0.2f); Call(s, "Reset"); }, 5),
                ("retrograde-flyby", s => { Set(s, "impact", 4f); Set(s, "approach", 1.4f); Set(s, "retrograde", true); Call(s, "Reset"); }, 5),
            },
        };

        IEnumerator UiPass()
        {
            // the boot menu, over the first scene
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Shot("ui-boot-menu");
            log.WriteLine($"screen {UnityEngine.Screen.width}x{UnityEngine.Screen.height}, ui scale {app.UiScale:F2}");
            foreach (var (name, m) in new[] { ("menu-main", 1), ("menu-observatory", 2), ("menu-controls", 3) })
            { app.ShowMenu(m); yield return new WaitForSecondsRealtime(0.5f); yield return Shot("ui-" + name); }
            app.ShowMenu(0);
            yield return new WaitForSecondsRealtime(0.5f); yield return Shot("ui-scene");
            app.HelpOpen = true; yield return new WaitForSecondsRealtime(0.5f); yield return Shot("ui-help"); app.HelpOpen = false;
            app.Activate(1); yield return new WaitForSecondsRealtime(1f); yield return Shot("ui-observatory-scene");
            log.WriteLine($"done: {errors} errors, {bad} bad readouts");
            log.Close(); log = null;
            Application.Quit(ExitCode);
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
                    Application.Quit(2); // incomplete: the functional result is not known
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
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, "qa.txt")) { AutoFlush = true };
            app = GetComponent<FogOfWarApp>();
            app.InputLocked = true;
            Controls.Locked = true;
            int startQuality = app.Quality;

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
                if (scene is LabScene lab)
                {
                    Set(lab, "paused", true);
                    double frozen = lab.Elapsed;
                    yield return new WaitForSecondsRealtime(1f);
                    if (lab.Elapsed != frozen) { bad++; log.WriteLine("    paused model advanced"); }
                    yield return Shot($"{i + 1}-paused");
                    lab.Reset();
                    if (lab.Elapsed != 0 || lab.Paused) { bad++; log.WriteLine("    replay did not reset playback"); }
                    yield return Shot($"{i + 1}-replay");
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

            log.WriteLine("window sizes");
            foreach (var (w, h) in new[] { (1280, 720), (1024, 768), (2560, 1440) })
            {
                Screen.SetResolution(w, h, FullScreenMode.Windowed);
                yield return new WaitForSecondsRealtime(1.5f);
                foreach (int s in new[] { 3, 7, 8, 9, 11, 12, 18, 19 }) { app.Activate(s); yield return new WaitForSecondsRealtime(1f); yield return Shot($"res{w}x{h}-{s + 1}"); }
            }

            log.WriteLine($"done: {errors} errors, {bad} bad readouts");
            log.Close();
            log = null;
            Application.Quit(ExitCode);
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
