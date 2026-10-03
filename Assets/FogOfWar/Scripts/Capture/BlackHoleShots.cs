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
        }

        static Shot S(string name, Vector3 pos, float fov, Action<SimScene> setup = null, float roll = 0, Vector3 look = default, float run = 0)
            => new Shot { name = name, pos = pos, fov = fov, setup = setup, roll = roll, look = look, run = run };

        static readonly Action<SimScene> Default = s =>
        {
            Set(s, "massIdx", 1); Set(s, "disk", true); Set(s, "doppler", true); Set(s, "lens", true); Set(s, "outer", 13f); Set(s, "temp", 4800f);
            Set(s, "spin", 0f); Set(s, "clumpy", true);
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
                S("bh-no-doppler",       new Vector3(0, 0.9f, 24f), 42, With(s => Set(s, "doppler", false))),
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
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, "bhshots.txt")) { AutoFlush = true };
            var app = GetComponent<FogOfWarApp>();
            Controls.Locked = true;
            app.InputLocked = true;
            app.Quality = 2;   // in memory only
            yield return null;
            app.Activate(1);
            var shell = UiShell.Instance;
            var cam = app.Cam;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var uiRT = withUi && shell != null ? new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) : null;
            rt.Create();
            cam.targetTexture = rt;
            if (uiRT != null) shell.Surface = uiRT;
            else app.ShowUI = false;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false, false);
            var acc = new float[W * H * 3];
            log.WriteLine($"{W}x{H}, {SS} samples, colorspace {QualitySettings.activeColorSpace}, gpu {SystemInfo.graphicsDeviceName}");

            foreach (var s in Shots())
            {
                if (only != null && !s.name.Contains(only)) continue;
                Time.timeScale = 1;
                try { s.setup?.Invoke(app.Current); }
                catch (Exception e) { log.WriteLine($"{s.name}: setup threw {e.InnerException?.Message ?? e.Message}"); errors++; }
                app.Orbit.enabled = false;
                cam.fieldOfView = s.fov;
                var baseRot = Quaternion.LookRotation(s.look - s.pos, Vector3.up) * Quaternion.Euler(0, 0, s.roll);
                cam.transform.SetPositionAndRotation(s.pos, baseRot);
                float until = Time.realtimeSinceStartup + 0.6f + s.run;
                while (Time.realtimeSinceStartup < until) { cam.transform.SetPositionAndRotation(s.pos, baseRot); yield return null; }
                Time.timeScale = 0;   // freeze the disc while sub-frames accumulate

                Array.Clear(acc, 0, acc.Length);
                float pix = 2 * Mathf.Tan(s.fov * Mathf.Deg2Rad / 2) / H * Mathf.Rad2Deg;
                Color32[] uiPx = null;
                for (int k = 0; k < SS; k++)
                {
                    float jx = SS == 1 ? 0 : Halton(k + 1, 2) - 0.5f, jy = SS == 1 ? 0 : Halton(k + 1, 3) - 0.5f;
                    cam.transform.SetPositionAndRotation(s.pos, baseRot * Quaternion.Euler(-jy * pix, jx * pix, 0));
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    cam.Render();
                    ReadInto(rt, tex);
                    var px = tex.GetPixels32();
                    for (int p = 0, q = 0; p < px.Length; p++, q += 3) { acc[q] += px[p].r; acc[q + 1] += px[p].g; acc[q + 2] += px[p].b; }
                    if (k == 0 && uiRT != null) { ReadInto(uiRT, tex); uiPx = tex.GetPixels32(); }
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
                log.WriteLine($"{s.name}");
            }

            // GPU cost of the ray tracer at the default view: N renders, one readback to wait for the GPU.
            Time.timeScale = 1;
            Default(app.Current);
            foreach (var res in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160) })
                foreach (int q in new[] { 0, 1, 2 })
                {
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
                    log.WriteLine($"timing {res.x}x{res.y} quality {q}: {ms:0.0} ms per frame");
                    cam.targetTexture = null;
                    trt.Release(); Destroy(trt); Destroy(small);
                }

            cam.targetTexture = null;
            if (shell != null) shell.Surface = null;
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
