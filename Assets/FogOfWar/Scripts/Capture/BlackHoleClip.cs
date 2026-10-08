using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using static FogOfWar.BlackHoleShots;

namespace FogOfWar
{
    // Offscreen montage clips: `FogOfWarBH.exe -batchmode -fowbhclip <dir> [-fowbhres 1080x1350] [-fowbhss 16]
    // [-fowbhonly a,b] [-fowbhstill] [-fowbhdry]` renders scripted camera moves and sandbox runs as PNG frame sequences
    // (<dir>/<clip>/0000.png …) at 30 fps, with a per-frame CSV of the probe's clocks for the overlay. Time is frozen while
    // each frame accumulates SS jittered sub-frames, so every frame is a converged still. -fowbhstill renders only the
    // first, middle and last frame of each clip; -fowbhdry renders nothing and writes the CSVs only (pacing).
    public class BlackHoleClip : MonoBehaviour
    {
        string dir, only;
        int W = 1080, H = 1350, SS = 16;
        bool still, dry;
        StreamWriter log;
        const float Dt = 1f / 30;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public static void StartIfRequested(FogOfWarApp app)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fowbhclip");
            if (i < 0 || i + 1 >= args.Length) return;
            var c = app.gameObject.AddComponent<BlackHoleClip>();
            c.dir = args[i + 1];
            int j;
            if ((j = Array.IndexOf(args, "-fowbhres")) >= 0) { var wh = args[j + 1].Split('x'); c.W = int.Parse(wh[0]); c.H = int.Parse(wh[1]); }
            if ((j = Array.IndexOf(args, "-fowbhss")) >= 0) c.SS = int.Parse(args[j + 1]);
            if ((j = Array.IndexOf(args, "-fowbhonly")) >= 0) c.only = args[j + 1];
            c.still = Array.IndexOf(args, "-fowbhstill") >= 0;
            c.dry = Array.IndexOf(args, "-fowbhdry") >= 0;
            if ((j = Array.IndexOf(args, "-fowbhflare")) >= 0) FlareStart = double.Parse(args[j + 1], CultureInfo.InvariantCulture);
        }

        struct Clip
        {
            public string name; public int frames;
            public Action<BlackHoleScene> setup;
            public Action<BlackHoleScene, Camera, int, float> frame;   // scene, camera, frame index, 0…1 through the clip
        }

        static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }
        static float Seg(float u, float a, float b) => Ease((u - a) / (b - a));

        // camera on a sphere round `target`: azimuth from +z towards +x, elevation above the disc plane, degrees
        static void Sph(Camera cam, float az, float el, float dist, float fov, Vector3 target = default, float roll = 0)
        {
            float a = az * Mathf.Deg2Rad, e = el * Mathf.Deg2Rad;
            var pos = target + dist * new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
            cam.fieldOfView = fov;
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos, Vector3.up) * Quaternion.Euler(0, 0, roll));
        }

        static void Base(BlackHoleScene s)
        {
            Set(s, "massIdx", 1); Set(s, "shownMassIdx", -2); Call(s, "SyncMass");
            Set(s, "disk", true); Set(s, "doppler", true); Set(s, "lens", true); Set(s, "outer", 13f); Set(s, "temp", 4800f);
            Set(s, "spin", 0f); Set(s, "clumpy", true); Set(s, "debugFate", 0f); Set(s, "rawHdr", 0f); Set(s, "thinDisk", false);
            Set(s, "hotSpot", false); Set(s, "lensTable", false); Set(s, "exposureEV", 0f);
            Call(s, "ClearSandbox"); Set(s, "autoWarp", true); Set(s, "logWarp", 1.5f); Set(s, "guides", false); Set(s, "showMap", false);
            Set(s, "diskClock", 3.0);
        }

        static double FlareStart = 3.5e5;   // M after the break-up: the returning debris is about to light up its disc (-fowbhflare)

        static object LastProbe(BlackHoleScene s)
        {
            var probes = (IList)typeof(BlackHoleScene).GetField("probes", F).GetValue(s);
            return probes.Count > 0 ? probes[probes.Count - 1] : null;
        }
        static Geo ProbeGeo(BlackHoleScene s) { var p = LastProbe(s); return p == null ? default : (Geo)p.GetType().GetField("g", F).GetValue(p); }
        static double ProbeR(BlackHoleScene s) => ProbeGeo(s).r;
        static bool Inside(BlackHoleScene s) => ProbeGeo(s).inside;

        static List<Clip> Clips()
        {
            var l = new List<Clip>();
            void Add(string name, float seconds, Action<BlackHoleScene> setup, Action<BlackHoleScene, Camera, int, float> frame)
                => l.Add(new Clip { name = name, frames = Mathf.RoundToInt(seconds * 30), setup = setup, frame = frame });

            // 1. hook: from over the pole down to the disc plane; the far side of the disc lifts over the top
            Add("hook", 5f, s => Base(s), (s, c, i, u) =>
            {
                s.Tick(Dt);
                float k = Ease(u);
                Sph(c, Mathf.Lerp(-30, 8, k), Mathf.Lerp(72, 3.5f, k), Mathf.Lerp(34, 22, k), Mathf.Lerp(58, 50, k));
            });
            // 2. the same view with straight light (lensing off) and bent light, for a wipe in the edit
            foreach (bool lens in new[] { false, true })
                Add(lens ? "lens-on" : "lens-off", 4f, s => { Base(s); Set(s, "lens", lens); }, (s, c, i, u) =>
                {
                    s.Tick(Dt);
                    Sph(c, Mathf.Lerp(-6, 6, u), Mathf.Lerp(5, 7, u), Mathf.Lerp(25, 23, u), 50);
                });
            // 3. spin it up: the shadow flattens on the side where light goes with the spin
            Add("spin", 4.5f, s => Base(s), (s, c, i, u) =>
            {
                Set(s, "spin", 0.998f * Seg(u, 0.12f, 0.8f));
                s.Tick(Dt);
                Sph(c, 0, 2.4f, Mathf.Lerp(24, 21, u), 46);
            });
            // 4. throw in a Sun-like star: torn apart by the tide, the stream flung round the hole
            Add("star", 7f, s => { Base(s); Set(s, "periRs", 5f); s.Throw(0); Set(s, "logWarp", 1.9f); s.AdvanceUntilBody(44); Set(s, "autoWarp", false); }, (s, c, i, u) =>
            {
                // through the break-up at an even pace, then faster so the stream can be seen stretching out
                Set(s, "logWarp", Mathf.Lerp(1.6f, 2.5f, Seg(u, 0.4f, 0.9f)));
                s.Tick(Dt);
                Sph(c, 180 + Mathf.Lerp(-30, -10, u), Mathf.Lerp(30, 38, u), Mathf.Lerp(30, 44, Ease(u)), 55);
            });
            // 4b. later: the debris comes back and lights up a new disc (the flare)
            Add("flare", 6f, s => { Base(s); Set(s, "periRs", 5f); s.Throw(0); s.AdvanceUntilBody(1); Set(s, "autoWarp", false); Set(s, "logWarp", 3.6f); s.Advance(FlareStart); Set(s, "logWarp", 3.9f); }, (s, c, i, u) =>
            {
                s.Tick(Dt);
                Sph(c, 180 + Mathf.Lerp(20, -10, u), Mathf.Lerp(18, 9, Ease(u)), Mathf.Lerp(30, 24, Ease(u)), 50);
            });
            // 5. a gas cloud (no self-gravity) smeared round the hole
            Add("cloud", 5f, s => { Base(s); Set(s, "periRs", 6f); s.Throw(5); Set(s, "logWarp", 1.9f); s.Advance(110); Set(s, "logWarp", 1.6f); }, (s, c, i, u) =>
            {
                s.Tick(Dt);
                Sph(c, 180 + Mathf.Lerp(10, -8, u), Mathf.Lerp(34, 24, u), Mathf.Lerp(24, 20, u), 56);
            });
            // 6. drop a probe from rest, seen from far away: its clock runs slower and it freezes at the edge. The warp
            // eases off as it nears the horizon so the slowdown can be seen, then picks up again once it has frozen.
            Add("probe", 11f, s => { Base(s); Set(s, "disk", false); s.DropProbe(3); Set(s, "logWarp", 1.75f); s.Advance(150); }, (s, c, i, u) =>
            {
                double r = ProbeR(s);
                float x = Mathf.Clamp01((float)(r - 2) / 12);
                Set(s, "logWarp", Inside(s) ? 1.3f : Mathf.Lerp(0.35f, 1.75f, Mathf.Sqrt(x)));
                s.Tick(Dt);
                // the probe sits on +x: aim the camera's up there so it falls from the top of the frame
                float k = Ease(u * 1.4f);
                var tgt = new Vector3(Mathf.Lerp(4.5f, 1.2f, k), 0, 0);
                var pos = tgt + Quaternion.Euler(-6, 0, 0) * new Vector3(0, 0, -Mathf.Lerp(24, 17, k));
                c.fieldOfView = 60;
                c.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(tgt - pos, Vector3.right));
            });
            // 7. ride along with a probe falling from far away, to the horizon
            Add("ride", 9f, s => { Base(s); Set(s, "ride", true); s.DropProbe(4); s.AdvanceUntilRide(40); Set(s, "logWarp", 1.4f); }, (s, c, i, u) =>
            {
                double r = ProbeR(s);
                float x = Mathf.Clamp01((float)(r - 2) / 20);
                Set(s, "logWarp", Mathf.Lerp(0.3f, 1.5f, Mathf.Sqrt(x)));
                Set(s, "exposureEV", Mathf.Lerp(2f, 0f, Mathf.Clamp01((float)(r - 2.5) / 6)));
                s.Tick(Dt);
                c.fieldOfView = 70;
            });
            // 8. outro: a slow push in at a tilt
            Add("outro", 5f, s => { Base(s); Set(s, "temp", 4200f); }, (s, c, i, u) =>
            {
                s.Tick(Dt);
                Sph(c, Mathf.Lerp(-18, -10, u), Mathf.Lerp(9, 6, u), Mathf.Lerp(26, 20, u), 50, roll: -12);
            });
            return l;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, "bhclip.txt")) { AutoFlush = true };
            OpenSystemScene.CheckUnattendedSaveKey(log);
            Application.logMessageReceived += (msg, stack, type) => { if (type != LogType.Log) log.WriteLine($"[{type}] {msg}\n{stack}"); };
            var app = GetComponent<FogOfWarApp>();
            Controls.Locked = true;
            app.InputLocked = true;
            app.Quality = 2;   // in memory only
            yield return null;
            app.Activate(1);
            var cam = app.Cam;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            cam.targetTexture = rt;
            if (UI.UiShell.Instance != null) UI.UiShell.Instance.Surface = new RenderTexture(W, H, 24);   // keep the HUD off our target
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false, false);
            Time.timeScale = 0;   // the clip drives the sandbox itself, one Tick(1/30) per frame
            log.WriteLine($"{W}x{H}, {SS} sub-frames, gpu {SystemInfo.graphicsDeviceName}, still {still}, dry {dry}");
            var bs = (BlackHoleScene)app.Current;
            double tunit = (double)typeof(BlackHoleScene).GetProperty("Tunit", F).GetValue(bs);

            foreach (var clip in Clips())
            {
                if (only != null && Array.TrueForAll(only.Split(','), o => clip.name != o)) continue;
                string cdir = Path.Combine(dir, clip.name);
                Directory.CreateDirectory(cdir);
                float t0 = Time.realtimeSinceStartup;
                app.Orbit.enabled = false;
                clip.setup(bs);
                if (!(bool)typeof(BlackHoleScene).GetField("ride", F).GetValue(bs)) app.Orbit.enabled = false;
                using (var csv = new StreamWriter(Path.Combine(cdir, "data.csv")))
                {
                    csv.WriteLine("frame,simT,logWarp,probeR,probeTau,farT,rate,inside,camR,lum,spin,exposureEV,px,py,narration");
                    for (int i = 0; i < clip.frames; i++)
                    {
                        float u = clip.frames > 1 ? i / (float)(clip.frames - 1) : 0;
                        clip.frame(bs, cam, i, u);
                        if (!(bool)typeof(BlackHoleScene).GetField("ride", F).GetValue(bs)) app.Orbit.enabled = false;
                        csv.WriteLine(Row(bs, cam, i, tunit));
                        bool want = !dry && (!still || i == 0 || i == clip.frames / 2 || i == clip.frames - 1);
                        if (!want) continue;
                        // two warm-up renders use up the pose change, then the accumulation restarts at this frame
                        for (int k = 0; k < 2; k++) { yield return null; yield return new WaitForEndOfFrame(); cam.Render(); }
                        bs.Post?.Reset();
                        int n = still ? Mathf.Min(SS, 8) : SS;
                        for (int k = 0; k < n; k++) { yield return null; yield return new WaitForEndOfFrame(); cam.Render(); }
                        var prev = RenderTexture.active; RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0, false); tex.Apply(false);
                        RenderTexture.active = prev;
                        File.WriteAllBytes(Path.Combine(cdir, i.ToString("0000") + ".png"), tex.EncodeToPNG());
                    }
                }
                log.WriteLine($"{clip.name}: {clip.frames} frames in {Time.realtimeSinceStartup - t0:0.0} s; {bs.DebugSummary()}");
            }
            log.WriteLine("done");
            log.Close();
            Application.Quit(0);
        }

        // the probe's straight-line (unlensed) place on screen, 0…1 from the top left, for the overlay marker
        static string Screen(BlackHoleScene bs, Camera cam)
        {
            var p = LastProbe(bs);
            if (p == null) return ",";
            var g = ProbeGeo(bs);
            var v = cam.WorldToViewportPoint(Kerr.FromBL(g.r, 0, g.phi, 0) * 0.5f);
            return v.z > 0 ? (v.x.ToString(CultureInfo.InvariantCulture) + "," + (1 - v.y).ToString(CultureInfo.InvariantCulture)) : ",";
        }

        static string Row(BlackHoleScene bs, Camera cam, int i, double tunit)
        {
            var inv = CultureInfo.InvariantCulture;
            double simT = (double)typeof(BlackHoleScene).GetField("simT", F).GetValue(bs);
            float lw = (float)typeof(BlackHoleScene).GetField("logWarp", F).GetValue(bs);
            var probes = (IList)typeof(BlackHoleScene).GetField("probes", F).GetValue(bs);
            string pr = "", ptau = "", pt = "", rate = "", inside = "";
            if (probes.Count > 0)
            {
                object p = probes[probes.Count - 1];
                var pt_ = p.GetType();
                var g = (Geo)pt_.GetField("g", F).GetValue(p);
                double t0 = (double)pt_.GetField("t0", F).GetValue(p), tau0 = (double)pt_.GetField("tau0", F).GetValue(p);
                pr = g.r.ToString("R", inv);
                ptau = ((g.tau - tau0) * tunit).ToString("R", inv);
                pt = ((Math.Min(simT, g.t) - t0) * tunit).ToString("R", inv);
                inside = g.inside ? "1" : "0";
                if (!g.inside) { g.FourVelocity((float)typeof(BlackHoleScene).GetField("spin", F).GetValue(bs), out double ut, out _, out _); rate = (1 / ut).ToString("R", inv); }
            }
            var args = new object[] { null };
            string narr = (string)typeof(BlackHoleScene).GetMethod("Narration", F).Invoke(bs, args);
            return string.Join(",", i.ToString(inv), simT.ToString("R", inv), lw.ToString(inv), pr, ptau, pt, rate, inside,
                cam.transform.position.magnitude.ToString(inv),
                ((double)typeof(BlackHoleScene).GetField("lum", F).GetValue(bs)).ToString("R", inv),
                ((float)typeof(BlackHoleScene).GetField("spin", F).GetValue(bs)).ToString(inv),
                ((float)typeof(BlackHoleScene).GetField("exposureEV", F).GetValue(bs)).ToString(inv), Screen(bs, cam), "\"" + narr.Replace("\"", "'") + "\"");
        }
    }
}
