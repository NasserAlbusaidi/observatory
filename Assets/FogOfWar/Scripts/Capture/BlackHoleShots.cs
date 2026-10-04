using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using FogOfWar.UI;
using UnityEngine;

namespace FogOfWar
{
    // Offscreen black hole review: `FogOfWar.exe -batchmode -fowbhshots <dir> [-fowbhres 1920x1080] [-fowbhss 4]
    // [-fowbhonly name] [-fowbhui]` renders a fixed list of black hole views (camera only, or with the UI Toolkit HUD
    // layer composited when -fowbhui is given), averaging sub-pixel-jittered frames, then times the ray tracer and quits.
    // The first eight views match the external Shots driver, so before/after pairs line up.
    public class BlackHoleShots : MonoBehaviour
    {
        string dir, only;
        int W = 1920, H = 1080, SS = 4;
        bool withUi;
        StreamWriter log;
        int errors;

        public static void StartIfRequested(FogOfWarApp app)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fowbhshots");
            if (i < 0 || i + 1 >= args.Length) return;
            var s = app.gameObject.AddComponent<BlackHoleShots>();
            s.dir = args[i + 1];
            int j;
            if ((j = Array.IndexOf(args, "-fowbhres")) >= 0) { var wh = args[j + 1].Split('x'); s.W = int.Parse(wh[0]); s.H = int.Parse(wh[1]); }
            if ((j = Array.IndexOf(args, "-fowbhss")) >= 0) s.SS = int.Parse(args[j + 1]);
            if ((j = Array.IndexOf(args, "-fowbhonly")) >= 0) s.only = args[j + 1];
            s.withUi = Array.IndexOf(args, "-fowbhui") >= 0;
        }

        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        public static void Set(object o, string field, object v)
        {
            var f = o.GetType().GetField(field, F) ?? throw new MissingFieldException(o.GetType().Name, field);
            f.SetValue(o, Convert.ChangeType(v, f.FieldType, CultureInfo.InvariantCulture));
        }
        public static void Call(object o, string method, params object[] args)
        {
            foreach (var m in o.GetType().GetMethods(F))
                if (m.Name == method && m.GetParameters().Length == args.Length) { m.Invoke(o, args); return; }
            throw new MissingMethodException(o.GetType().Name, method);
        }

        public struct Shot
        {
            public string name; public Vector3 pos, look; public float fov, roll, run; public Action<SimScene> setup;
            public bool ui, free;   // composite the UI Toolkit layer; leave the camera to the scene (ride-along)
        }

        static Shot S(string name, Vector3 pos, float fov, Action<SimScene> setup = null, float roll = 0, Vector3 look = default, float run = 0, bool ui = false, bool free = false)
            => new Shot { name = name, pos = pos, fov = fov, setup = setup, roll = roll, look = look, run = run, ui = ui, free = free };

        static readonly Action<SimScene> Default = s =>
        {
            Set(s, "massIdx", 1); Set(s, "shownMassIdx", -2); Call(s, "SyncMass"); Set(s, "disk", true); Set(s, "doppler", true); Set(s, "lens", true); Set(s, "outer", 13f); Set(s, "temp", 4800f);
            Set(s, "spin", 0f); Set(s, "clumpy", true); Set(s, "debugFate", 0f);
            var b = (BlackHoleScene)s;
            Call(b, "ClearSandbox"); Set(b, "logWarp", 1.5f); Set(b, "guides", false); Set(b, "showMap", true);
        };
        static BlackHoleScene B(SimScene s) => (BlackHoleScene)s;
        // throw kind at pericentre periRs; untilR > 0: run until it is that close (M) or broken, then `run` more M
        static Action<SimScene> Tde(int kind, float periRs, int massIdx, double run, float logWarp = 1.5f, double untilR = 0, float logMass = 0) => s =>
        {
            Default(s); Set(s, "massIdx", massIdx); Set(s, "throwKind", kind); Set(s, "periRs", periRs); Set(s, "guides", true);
            Call(s, "SyncMass");
            if (logMass > 0) Set(s, "logMass", logMass);
            B(s).Throw(kind);
            if (untilR > 0) B(s).AdvanceUntilBody(untilR);
            Set(s, "logWarp", logWarp);
            if (run > 0) B(s).Advance(run);
            Set(s, "logWarp", 0f);
        };
        static Action<SimScene> Orbit(int probe, double run) => s =>
        {
            Default(s); Set(s, "disk", false); Set(s, "guides", true);
            B(s).DropProbe(probe); Set(s, "logWarp", 2.3f); B(s).Advance(run); Set(s, "logWarp", 0f);
        };
        static Action<SimScene> Ride(double r) => s =>
        {
            Default(s); Set(s, "ride", true); B(s).DropProbe(4); B(s).AdvanceUntilRide(r); Set(s, "logWarp", 0f);
        };
        static Action<SimScene> With(Action<SimScene> extra) => s => { Default(s); extra(s); };

        // Shots added by later work (the sandbox) append to this list.
        public static readonly List<Shot> Extra = new List<Shot>();

        static List<Shot> Shots()
        {
            var l = new List<Shot>
            {
                S("bh-edge-on",          new Vector3(0, 0.9f, 24f), 42, Default),
                S("bh-edge-on-galactic", new Vector3(0, 0.9f, -24f), 42, Default),
                S("bh-tilt",             new Vector3(-8f, 2.2f, 19f), 50, Default, roll: -14),
                S("bh-inside-disc",      new Vector3(0, 0.8f, 9f), 75, Default),
                S("bh-photon-ring-thin", new Vector3(0, 0.5f, 7f), 80, With(s => Set(s, "outer", 6f))),
                S("bh-hot-wide-disc",    new Vector3(0, 2.4f, 30f), 50, With(s => { Set(s, "outer", 20f); Set(s, "temp", 14000f); })),
                S("bh-from-above",       new Vector3(0, 18f, 14f), 55, Default),
                S("bh-wide",             new Vector3(-14f, 4.5f, 50f), 55, Default, look: new Vector3(-4f, 0, 0)),
                S("bh-smooth-luminet",   new Vector3(0, 4.2f, 24f), 42, With(s => Set(s, "clumpy", false))),
                S("bh-kerr-0.9",         new Vector3(0, 0.9f, 24f), 42, With(s => Set(s, "spin", 0.9f))),
                S("bh-kerr-0.998",       new Vector3(0, 0.9f, 24f), 42, With(s => Set(s, "spin", 0.998f))),
                S("bh-kerr-0.998-nodisk", new Vector3(0, 0.0f, 24f), 30, With(s => { Set(s, "spin", 0.998f); Set(s, "disk", false); })),
                S("bh-schw-nodisk",      new Vector3(0, 0.0f, 24f), 30, With(s => Set(s, "disk", false))),
                // QA-034 round 7: zoomed in from the default side, guides on, as in QA-Shots\r7\play\11-zoom.png
                S("bh-kerr-0.998-default-zoom14", new Vector3(0, 1.3f, -14f), 55, With(s => { Set(s, "spin", 0.998f); Set(s, "guides", true); })),
                S("bh-kerr-0.998-default-zoom10", new Vector3(0, 0.9f, -10f), 55, With(s => { Set(s, "spin", 0.998f); Set(s, "guides", true); })),
                S("bh-kerr-0.998-default-zoom24", new Vector3(0, 2.2f, -24f), 25, With(s => { Set(s, "spin", 0.998f); Set(s, "guides", true); })),
                S("dbg-fate-zoom24", new Vector3(0, 2.2f, -24f), 25, With(s => { Set(s, "spin", 0.998f); Set(s, "guides", true); Set(s, "debugFate", 1f); })),
                S("dbg-hit-zoom24", new Vector3(0, 2.2f, -24f), 25, With(s => { Set(s, "spin", 0.998f); Set(s, "guides", true); Set(s, "debugFate", 2f); })),
                // QA-034 recheck: the scene's own default camera (Orbit.Set in Build), spin 0.998, clumpy gas on and off
                S("bh-kerr-0.998-default-cam", new Vector3(0, 2.2f, -24f), 55, With(s => Set(s, "spin", 0.998f))),
                S("bh-kerr-0.998-default-smooth", new Vector3(0, 2.2f, -24f), 55, With(s => { Set(s, "spin", 0.998f); Set(s, "clumpy", false); })),
                S("bh-kerr-0.998-guides", new Vector3(0, 0.9f, 24f), 42, With(s => { Set(s, "spin", 0.998f); Set(s, "guides", true); })),
                S("bh-no-doppler",       new Vector3(0, 0.9f, 24f), 42, With(s => Set(s, "doppler", false))),
                // lensing off = straight rays from a flat-space camera (#32): compare with a straight-line reference
                S("bh-nolens",           new Vector3(0, 0.9f, 24f), 42, With(s => Set(s, "lens", false))),
                S("bh-nolens-4.4M",      new Vector3(0, 0.0f, 2.2f), 70, With(s => { Set(s, "lens", false); Set(s, "disk", false); })),
                S("bh-nolens-kerr-0.9",  new Vector3(0, 0.9f, 24f), 42, With(s => { Set(s, "lens", false); Set(s, "spin", 0.9f); })),
                // sandbox
                S("sb-sun-sgra-approach", new Vector3(0, 5f, 30f), 50, Tde(0, 5f, 1, 0, untilR: 30), ui: true),
                S("sb-sun-sgra-shred",    new Vector3(0, 5f, 30f), 50, Tde(0, 5f, 1, 12, untilR: 1), ui: true),
                S("sb-sun-sgra-stream",   new Vector3(0, 9f, 34f), 55, Tde(0, 5f, 1, 120, untilR: 1), ui: true),
                S("sb-sun-sgra-return",   new Vector3(0, 14f, 40f), 55, Tde(0, 5f, 1, 4.2e5, 3.6f), ui: true),
                S("sb-sun-sgra-disc",     new Vector3(0, 3f, 26f), 50, Tde(0, 5f, 1, 6e5, 3.7f), ui: true),
                S("sb-sun-m87-whole",     new Vector3(0, 5f, 30f), 50, Tde(0, 5f, 2, 0, untilR: 12), ui: true),
                S("sb-wd-imbh",           new Vector3(0, 6f, 30f), 50, Tde(1, 6f, -1, 60, untilR: 1, logMass: 3f), ui: true),
                // the main tour's path, and the same throw played in real time (40 s of 60 fps frames)
                S("sb-tour-repro",        new Vector3(0, 9f, 34f), 55, With(s => { Set(s, "periRs", 5f); B(s).Throw(0); B(s).AdvanceUntilBody(0, 500); Set(s, "logWarp", 0f); Set(s, "warpTarget", -1f); }), ui: true),
                S("sb-follow-8s",         Vector3.zero, 60, With(s => { Set(s, "periRs", 5f); B(s).Throw(0); B(s).AdvanceUntilBody(1); for (int i = 0; i < 480; i++) B(s).Tick(1f / 60); }), ui: true, free: true),
                S("sb-follow-16s",        Vector3.zero, 60, With(s => { Set(s, "periRs", 5f); B(s).Throw(0); B(s).AdvanceUntilBody(1); for (int i = 0; i < 960; i++) B(s).Tick(1f / 60); }), ui: true, free: true),
                S("sb-follow-30s",        Vector3.zero, 60, With(s => { Set(s, "periRs", 5f); B(s).Throw(0); B(s).AdvanceUntilBody(1); for (int i = 0; i < 1800; i++) B(s).Tick(1f / 60); }), ui: true, free: true),
                S("sb-stream-3s",         new Vector3(0, 12f, 30f), 60, With(s => { Set(s, "periRs", 5f); B(s).Throw(0); B(s).AdvanceUntilBody(1); for (int i = 0; i < 180; i++) B(s).Tick(1f / 60); Set(s, "logWarp", 0f); Set(s, "warpTarget", -1f); }), ui: true),
                S("sb-stream-6s",         new Vector3(0, 12f, 30f), 60, With(s => { Set(s, "periRs", 5f); B(s).Throw(0); B(s).AdvanceUntilBody(1); for (int i = 0; i < 360; i++) B(s).Tick(1f / 60); Set(s, "logWarp", 0f); Set(s, "warpTarget", -1f); }), ui: true),
                S("sb-tour-flare",        new Vector3(0, 9f, 34f), 55, With(s => { Set(s, "periRs", 5f); B(s).Throw(0); B(s).AdvanceUntilBody(0, 4.2e5); Set(s, "logWarp", 0f); Set(s, "warpTarget", -1f); }), ui: true),
                S("sb-play-40s",          new Vector3(0, 9f, 34f), 55, With(s => { Set(s, "periRs", 5f); B(s).Throw(0); for (int i = 0; i < 40 * 60; i++) B(s).Tick(1f / 60); Set(s, "logWarp", 0f); Set(s, "warpTarget", -1f); }), ui: true),
                S("sb-cloud",             new Vector3(0, 7f, 32f), 50, Tde(5, 6f, 1, 260), ui: true),
                S("sb-orbit-precess",     new Vector3(0, 30f, 12f), 55, Orbit(1, 4000), ui: true),
                S("sb-zoom-whirl",        new Vector3(0, 30f, 12f), 55, Orbit(2, 2500), ui: true),
                S("sb-photon-orbit",      new Vector3(0, 30f, 12f), 55, Orbit(5, 60), ui: true),
                S("sb-ride-fall-20rs",    Vector3.zero, 70, Ride(40), free: true),
                S("sb-ride-fall-3rs",     Vector3.zero, 70, Ride(6), free: true),
                S("sb-ride-fall-1.1rs",   Vector3.zero, 70, Ride(2.2), free: true),
            };
            l.AddRange(Extra);
            return l;
        }

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;
        void OnLog(string msg, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            if (type != LogType.Warning) errors++;
            log?.WriteLine($"[{type}] {msg}\n{stack}");
        }

        IEnumerator Start()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fowbhpanel") >= 0) { yield return PanelShots(); yield break; }
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, "bhshots.txt")) { AutoFlush = true };
            var app = GetComponent<FogOfWarApp>();
            Controls.Locked = true;
            app.InputLocked = true;
            app.Quality = 2;   // in memory only
            { int qa = Array.IndexOf(Environment.GetCommandLineArgs(), "-fowbhquality"); if (qa >= 0) app.Quality = int.Parse(Environment.GetCommandLineArgs()[qa + 1]); }
            yield return null;
            app.Activate(1);
            var shell = UiShell.Instance;
            var cam = app.Cam;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var uiRT = shell != null ? new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) : null;
            rt.Create();
            cam.targetTexture = rt;
            if (uiRT != null) shell.Surface = uiRT;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false, false);
            var acc = new float[W * H * 3];
            log.WriteLine($"{W}x{H}, {SS} samples, colorspace {QualitySettings.activeColorSpace}, gpu {SystemInfo.graphicsDeviceName}");

            foreach (var s in Shots())
            {
                if (only != null && !s.name.Contains(only)) continue;
                Time.timeScale = 1;
                try { s.setup?.Invoke(app.Current); }
                catch (Exception e) { log.WriteLine($"{s.name}: setup threw {e.InnerException?.Message ?? e.Message}"); errors++; }
                if (!s.free) app.Orbit.enabled = false;
                else if (app.Orbit.enabled) app.Orbit.Snap();   // follow-camera shots: jump to where the camera is heading
                cam.fieldOfView = s.fov;
                var baseRot = s.free ? Quaternion.identity : Quaternion.LookRotation(s.look - s.pos, Vector3.up) * Quaternion.Euler(0, 0, s.roll);
                if (!s.free) cam.transform.SetPositionAndRotation(s.pos, baseRot);
                float until = Time.realtimeSinceStartup + 0.6f + s.run;
                while (Time.realtimeSinceStartup < until) { if (!s.free) cam.transform.SetPositionAndRotation(s.pos, baseRot); yield return null; }
                Time.timeScale = 0;   // freeze the disc while sub-frames accumulate

                Array.Clear(acc, 0, acc.Length);
                float pix = 2 * Mathf.Tan(s.fov * Mathf.Deg2Rad / 2) / H * Mathf.Rad2Deg;
                Color32[] uiPx = null;
                for (int k = 0; k < SS; k++)
                {
                    float jx = SS == 1 ? 0 : Halton(k + 1, 2) - 0.5f, jy = SS == 1 ? 0 : Halton(k + 1, 3) - 0.5f;
                    if (!s.free) cam.transform.SetPositionAndRotation(s.pos, baseRot * Quaternion.Euler(-jy * pix, jx * pix, 0));
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    cam.Render();
                    ReadInto(rt, tex);
                    var px = tex.GetPixels32();
                    for (int p = 0, q = 0; p < px.Length; p++, q += 3) { acc[q] += px[p].r; acc[q + 1] += px[p].g; acc[q + 2] += px[p].b; }
                    if (k == 0 && uiRT != null && (s.ui || withUi)) { ReadInto(uiRT, tex); uiPx = tex.GetPixels32(); }
                }
                var outPx = new Color32[W * H];
                for (int p = 0, q = 0; p < outPx.Length; p++, q += 3)
                {
                    float r = acc[q] / SS, g = acc[q + 1] / SS, b = acc[q + 2] / SS;
                    if (uiPx != null)
                    {
                        // UI Toolkit writes premultiplied colour
                        float k = (255 - uiPx[p].a) / 255f;
                        r = uiPx[p].r + r * k; g = uiPx[p].g + g * k; b = uiPx[p].b + b * k;
                    }
                    outPx[p] = new Color32((byte)Mathf.Min(255, r + 0.5f), (byte)Mathf.Min(255, g + 0.5f), (byte)Mathf.Min(255, b + 0.5f), 255);
                }
                tex.SetPixels32(outPx);
                File.WriteAllBytes(Path.Combine(dir, s.name + ".png"), tex.EncodeToPNG());
                log.WriteLine($"{s.name}  {((BlackHoleScene)app.Current).DebugSummary()}");
            }

            // CPU cost of the sandbox: a full star's debris near pericentre, at the default warp
            {
                Default(app.Current);
                var bs = (BlackHoleScene)app.Current;
                Set(bs, "periRs", 5f); bs.Throw(0); bs.AdvanceUntilBody(1, 120);
                float c0 = Time.realtimeSinceStartup;
                for (int k = 0; k < 60; k++) bs.Tick(1f / 60);
                log.WriteLine($"sandbox tick, 4000 debris particles: {(Time.realtimeSinceStartup - c0) * 1000 / 60:0.00} ms per frame");
                Default(app.Current);
            }

            // perihelion shift of the 14 M × 60 M probe at every time warp, 60 fps frames (exact: 60.15° at spin 0)
            foreach (float lw in new[] { 0f, 1f, 2f, 3f, 4f, 5f, 6f })
            {
                Default(app.Current);
                var bs = (BlackHoleScene)app.Current;
                bs.DropProbe(1);
                Set(bs, "logWarp", lw);
                int frames = 0;
                for (; frames < 60 * 600 && (double.IsNaN(bs.LastProbeShift()) || frames < 120); frames++) bs.Tick(1f / 60);
                for (int k = 0; k < 120; k++) bs.Tick(1f / 60);
                log.WriteLine($"precession at warp 10^{lw}: {bs.LastProbeShift():0.00}° per orbit after {frames + 120} frames");
            }

            // sandbox play-through at 60 fps, as a player would: three Suns at Sgr A* thrown one after another, each after
            // the previous flare (so later throws start at the raised warp), then a circular probe and photon launches
            {
                Default(app.Current);
                var bs = (BlackHoleScene)app.Current;
                Set(bs, "periRs", 5f);
                var lumF = typeof(BlackHoleScene).GetField("lum", F);
                for (int throwN = 1; throwN <= 3; throwN++)
                {
                    bs.Throw(0);
                    double peak = 0; int f = 0;
                    for (; f < 60 * 60; f++) { bs.Tick(1f / 60); peak = Math.Max(peak, (double)lumF.GetValue(bs)); }
                    log.WriteLine($"throw {throwN}: after {f / 60} s  peak {peak:0.0} L_Edd  {bs.DebugSummary()}");
                }
                Default(app.Current);
                bs.DropProbe(0); for (int k = 0; k < 60 * 30; k++) bs.Tick(1f / 60);
                log.WriteLine($"circle at the ISCO: shift {bs.LastProbeShift()} (NaN = no false pericentres)");
                foreach (float sp in new[] { 0f, 0.9f, 0.998f })
                    foreach (int kind in new[] { 5, 6 })
                    {
                        Default(app.Current); Set(bs, "spin", sp); Set(bs, "logWarp", 1f);
                        bs.DropProbe(kind); for (int k = 0; k < 60 * 30; k++) bs.Tick(1f / 60);
                        var probes = (System.Collections.IList)typeof(BlackHoleScene).GetField("probes", F).GetValue(bs);
                        var pr = probes[probes.Count - 1];
                        var st = pr.GetType().GetField("status").GetValue(pr); var te = pr.GetType().GetField("turnsEnd").GetValue(pr);
                        log.WriteLine($"light spin {sp} kind {kind}: {st}, turns {te}");
                    }
            }

            // GPU cost of the ray tracer at the default view: N renders, one readback to wait for the GPU.
            // One throw-away render per configuration first, so the first row does not carry the warm-up (audit #56).
            cam.targetTexture = rt; cam.Render();
            Time.timeScale = 1;
            // second pass (audit #62): a busy sandbox: one shredded star's debris, two intact white dwarfs and a probe
            // (three bodies tested on every ray step), from the same camera
            foreach (bool busy in new[] { false, true })
            {
            Default(app.Current);
            if (busy)
            {
                var bs = (BlackHoleScene)app.Current;
                Set(bs, "periRs", 5f); bs.Throw(0); bs.AdvanceUntilBody(1, 40);
                Set(bs, "warpTarget", -1f); Set(bs, "logWarp", 0f);
                bs.Throw(1); bs.Throw(1); bs.DropProbe(1);
            }
            app.Orbit.enabled = false;
            foreach (var res in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160) })
                foreach (int q in new[] { 0, 1, 2 })
                {
                    if (busy && !((res.x == 1920 && q == 1) || (res.x == 3840 && q == 2))) continue;
                    app.Quality = q;
                    var trt = new RenderTexture(res.x, res.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    trt.Create();
                    cam.targetTexture = trt;
                    cam.fieldOfView = 42;
                    cam.transform.SetPositionAndRotation(new Vector3(0, 0.9f, 24f), Quaternion.LookRotation(-new Vector3(0, 0.9f, 24f)));
                    yield return null;
                    var small = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    cam.Render(); ReadOne(trt, small);
                    float t0 = Time.realtimeSinceStartup;
                    const int N = 12;
                    for (int k = 0; k < N; k++) cam.Render();
                    ReadOne(trt, small);
                    float ms = (Time.realtimeSinceStartup - t0) * 1000 / N;
                    log.WriteLine($"timing{(busy ? " (busy sandbox)" : "")} {res.x}x{res.y} quality {q}: {ms:0.0} ms per frame");
                    cam.targetTexture = null;
                    trt.Release(); Destroy(trt); Destroy(small);
                }
            }

            cam.targetTexture = null;
            if (shell != null) shell.Surface = null;
            log.WriteLine($"done, {errors} errors");
            log.Close(); log = null;
            Application.Quit();
        }

        // Windowed run (not batchmode): the IMGUI instrument panel only draws on screen, so capture the real window in
        // a few sandbox states. Each step runs the panel's OnGUI code for a second before the capture.
        IEnumerator PanelShots()
        {
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, "bhpanel.txt")) { AutoFlush = true };
            var app = GetComponent<FogOfWarApp>();
            app.Activate(1);
            yield return new WaitForSecondsRealtime(1.5f);
            var b = (BlackHoleScene)app.Current;
            Default(b);
            var steps = new (string, Action)[]
            {
                ("panel-default", () => { }),
                ("panel-tde", () => { Set(b, "periRs", 5f); Set(b, "guides", true); b.Throw(0); b.AdvanceUntilBody(1, 120); Set(b, "logWarp", 1f); }),
                ("panel-tde-return", () => { Set(b, "logWarp", 3.6f); b.Advance(4.2e5); Set(b, "logWarp", 2.5f); }),
                ("panel-probe", () => { Default(b); Set(b, "disk", false); Set(b, "guides", true); b.DropProbe(1); Set(b, "logWarp", 2.3f); b.Advance(3000); }),
                ("panel-ride", () => { Default(b); Set(b, "ride", true); b.DropProbe(4); b.AdvanceUntilRide(10); Set(b, "logWarp", 0.5f); }),
                ("panel-ride-off", () => { Call(b, "StopRide"); Set(b, "ride", false); }),
            };
            foreach (var (name, act) in steps)
            {
                try { act(); } catch (Exception e) { log.WriteLine($"{name}: threw {e.InnerException?.Message ?? e.Message}"); errors++; }
                yield return new WaitForSecondsRealtime(1.2f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
                yield return new WaitForSecondsRealtime(0.5f);
                log.WriteLine($"{name}  {b.DebugSummary()}");
            }
            log.WriteLine($"done, {errors} errors");
            log.Close(); log = null;
            Application.Quit();
        }

        static void ReadInto(RenderTexture rt, Texture2D tex)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            RenderTexture.active = prev;
        }

        static void ReadOne(RenderTexture rt, Texture2D tex)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
            tex.Apply();
            RenderTexture.active = prev;
        }

        static float Halton(int i, int b)
        {
            float f = 1, r = 0;
            while (i > 0) { f /= b; r += f * (i % b); i /= b; }
            return r;
        }
    }
}
