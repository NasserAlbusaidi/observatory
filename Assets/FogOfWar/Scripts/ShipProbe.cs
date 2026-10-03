using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // Unattended ship look check: `FogOfWar.exe -fowshipshots <dir>` parks the player ship in low orbit around Earth,
    // Mars and Saturn (day side, terminator, night side, plus one Earth drive burn), frames it with the planet behind,
    // screenshots each view and logs frame rate. Writes ship.txt and quits. Used for before/after shots of ship work.
    public class ShipProbe : MonoBehaviour
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly string[] Bodies = { "Earth", "Mars", "Saturn" };

        string dir;
        StreamWriter log;
        FogOfWarApp app;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fowshipshots");
            if (i < 0 || i + 1 >= args.Length) return;
            var app = FindAnyObjectByType<FogOfWarApp>();
            if (app == null) { Debug.LogError("ShipProbe: no FogOfWarApp"); return; }
            app.gameObject.AddComponent<ShipProbe>().dir = args[i + 1];
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, "ship.txt")) { AutoFlush = true };
            app = GetComponent<FogOfWarApp>();
            app.InputLocked = true;
            Controls.Locked = true;
            yield return new WaitForSecondsRealtime(2f);
            app.Activate(app.GameIndex);
            yield return new WaitForSecondsRealtime(2f);
            log.WriteLine($"{SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}");
            var s = app.Current;
            var fps = new List<float>();
            var views = new List<(string body, string tag, float sunAngle, float thr)>();
            foreach (var b in Bodies)
            {
                views.Add((b, "day", 35f, 0f));
                views.Add((b, "terminator", 95f, 0f));
                views.Add((b, "night", 170f, 0f));
            }
            views.Add(("Earth", "burn", 95f, 0.3f));
            foreach (var v in views)
            {
                bool ok = true;
                try { Park(s, v.body, v.sunAngle, v.thr); }
                catch (Exception e) { ok = false; log.WriteLine($"  {v.body} {v.tag}: {e.InnerException?.Message ?? e.Message}"); }
                if (!ok) continue;
                yield return new WaitForSecondsRealtime(3f); // exposure adaptation and mip streaming settle
                float f = 0;
                yield return Measure(x => f = x);
                fps.Add(f);
                log.WriteLine($"  {v.body} {v.tag}: {f:F0} fps{Lighting()}");
                yield return Shot($"{v.body}-{v.tag}");
            }
            if (fps.Count > 0) log.WriteLine($"fps over all views: min {fps.Min():F0}, mean {fps.Average():F0}");
            // photo mode stacks PhotoFX on the same camera as the open world's bloom: check both still draw
            Park(s, "Earth", 35f, 0f);
            yield return new WaitForSecondsRealtime(2f);
            Capture.TogglePhoto();
            yield return new WaitForSecondsRealtime(1f);
            log.WriteLine($"  photo mode active {Capture.PhotoActive}, bloom {(app.Cam.GetComponent<OpenWorld.Bloom>()?.enabled ?? false)}");
            yield return Shot("photo");
            Capture.TestApproach();
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("photo-hull");
            Capture.TogglePhoto();
            log.Close();
            Application.Quit();
        }

        static string Lighting()
        {
            return $"  exposure {Shader.GetGlobalFloat("_FOWExposure"):F2} sunIrr {Shader.GetGlobalFloat("_FOWSunIrr"):F4} eclipse {Shader.GetGlobalFloat("_FOWEclipse"):F2} shine {(Vector4)Shader.GetGlobalVector("_FOWShineCol")}";
        }

        // Puts the ship in a low orbit around `body`, `sunAngleDeg` from the subsolar point (0 = noon, 180 = midnight),
        // a little above the equator so Saturn's rings don't hide it, and frames it from outside with the planet behind.
        static void Park(SimScene s, string body, float sunAngleDeg, float throttle)
        {
            var t = s.GetType();
            var sys = (SolarSystem)t.GetField("sys", F).GetValue(s);
            var b = sys[body];
            if (b == null) throw new Exception("no body " + body);
            double jd = (double)t.GetField("jd", F).GetValue(s);
            var ship = (Ship)t.GetField("ship", F).GetValue(s);
            var bp = sys.Pos(b, jd);
            var toSun = (sys.Pos(sys.Sun, jd) - bp).Norm;
            var pole = b.Pole;
            var east = D3.Cross(pole, toSun).Norm;
            var north = D3.Cross(toSun, east).Norm;
            double a = sunAngleDeg * Math.PI / 180, lat = 12 * Math.PI / 180;
            var dir = ((toSun * Math.Cos(a) + east * Math.Sin(a)) * Math.Cos(lat) + north * Math.Sin(lat)).Norm;
            double r = b.Radius * (body == "Saturn" ? 1.5 : 1.3);
            ship.Pos = bp + dir * r;
            ship.Vel = sys.Vel(b, jd);
            ship.Plan = Plan.None;
            ship.Hold = Hold.Off;
            ship.AngVel = Vector3.zero;
            ship.ThrottleCmd = throttle;
            var tangent = D3.Cross(pole, dir).Norm;
            ship.Att = Quaternion.LookRotation(Frame.U(tangent), Frame.U(dir));
            t.GetField("warpIdx", F).SetValue(s, 0);
            var pivot = Enum.ToObject(t.GetNestedType("Pivot", BindingFlags.NonPublic), 0);
            t.GetMethod("SetPivot", F).Invoke(s, new object[] { pivot, 0.11 });
            t.GetField("camMode", F).SetValue(s, Enum.ToObject(t.GetNestedType("CamMode", BindingFlags.NonPublic), 1));
            // camera outside the orbit, off to one side and above, looking back past the ship at the planet
            var off = (Frame.U(dir) * 0.75f + Frame.U(tangent) * 0.55f + Frame.U(pole) * 0.35f).normalized;
            t.GetField("yaw", F).SetValue(s, Mathf.Atan2(off.x, -off.z) * Mathf.Rad2Deg);
            t.GetField("pitch", F).SetValue(s, Mathf.Asin(Mathf.Clamp(off.y, -1, 1)) * Mathf.Rad2Deg);
        }

        static IEnumerator Measure(Action<float> result)
        {
            int frames = 0;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 3f) { frames++; yield return null; }
            result(frames / (Time.realtimeSinceStartup - t0));
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
        }
    }
}
