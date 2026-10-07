using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace FogOfWar
{
    // Unattended fidelity check: `FogOfWar.exe -fowfidelity <dir>` frames the mapped bodies of the open world from
    // close range (sunlit and near the terminator), screenshots each, logs frame rate per view and every Texture2D in
    // memory with its size and format. Writes fidelity.txt and quits. Used to compare texture work against a baseline.
    // -fowtexdetail N runs it at texture detail N (0 full, 1 half, 2 quarter) without touching the saved setting.
    public class FidelityProbe : MonoBehaviour
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly string[] Bodies =
        {
            "Earth", "Moon", "Mars", "Mercury", "Venus", "Jupiter", "Io", "Europa", "Ganymede", "Callisto",
            "Saturn", "Enceladus", "Dione", "Titan", "Vesta", "Ceres", "Triton",
        };

        string dir;
        StreamWriter log;
        FogOfWarApp app;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fowfidelity");
            if (i < 0 || i + 1 >= args.Length) return;
            var app = FindAnyObjectByType<FogOfWarApp>();
            if (app == null) { Debug.LogError("FidelityProbe: no FogOfWarApp"); return; }
            app.gameObject.AddComponent<FidelityProbe>().dir = args[i + 1];
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, "fidelity.txt")) { AutoFlush = true };
            OpenSystemScene.CheckUnattendedSaveKey(log);
            app = GetComponent<FogOfWarApp>();
            app.InputLocked = true;
            Controls.Locked = true;
            yield return new WaitForSecondsRealtime(2f);
            // after the shell has applied the saved settings, so this one wins
            var args = Environment.GetCommandLineArgs();
            int ti = Array.IndexOf(args, "-fowtexdetail");
            if (ti >= 0 && ti + 1 < args.Length && int.TryParse(args[ti + 1], out int detail))
            {
                GameSettings.ApplyTextureDetail(Mathf.Clamp(detail, 0, 2));
                yield return new WaitForSecondsRealtime(1f);
            }
            log.WriteLine($"{SystemInfo.graphicsDeviceName}, {Screen.width}x{Screen.height}, mipmap limit {QualitySettings.globalTextureMipmapLimit}, streaming {QualitySettings.streamingMipmapsActive} budget {QualitySettings.streamingMipmapsMemoryBudget} MB");

            var fps = new List<float>();
            for (int i = 0; i < app.SceneCount; i++)
            {
                app.Activate(i);
                yield return new WaitForSecondsRealtime(2f);
                float f = 0;
                yield return Measure(v => f = v);
                fps.Add(f);
                log.WriteLine($"scene {i + 1} {app.Current.Title}: {f:F0} fps");
                yield return Shot($"scene{i + 1}");
            }

            app.Activate(app.GameIndex);
            yield return new WaitForSecondsRealtime(2f);
            var s = app.Current;
            foreach (var name in Bodies)
            {
                foreach (var (tag, radii, phase) in new[] { ("near", 1.6, 25f), ("terminator", 1.6, 80f) })
                {
                    bool ok = true;
                    try { Frame(s, name, radii, phase); }
                    catch (Exception e) { ok = false; log.WriteLine($"  {name} {tag}: {e.InnerException?.Message ?? e.Message}"); }
                    if (!ok) continue;
                    yield return new WaitForSecondsRealtime(2.5f); // camera smoothing and mip streaming settle
                    float f = 0;
                    yield return Measure(v => f = v);
                    fps.Add(f);
                    log.WriteLine($"  {name} {tag}: {f:F0} fps");
                    yield return Shot($"{name}-{tag}");
                }
            }
            log.WriteLine($"fps over all views: min {fps.Min():F0}, mean {fps.Average():F0}");
            Textures();
            log.Close();
            Application.Quit();
        }

        // Puts the camera on the target body, `radii` body radii from its centre, `phaseDeg` away from the Sun line.
        static void Frame(SimScene s, string body, double radii, float phaseDeg)
        {
            var t = s.GetType();
            var sys = (OpenWorld.SolarSystem)t.GetField("sys", F).GetValue(s);
            var b = sys[body];
            if (b == null) throw new Exception("no body " + body);
            t.GetMethod("SetTarget", F).Invoke(s, new object[] { b });
            var pivot = Enum.ToObject(t.GetNestedType("Pivot", BindingFlags.NonPublic), 1);
            t.GetMethod("SetPivot", F).Invoke(s, new object[] { pivot, b.Radius * radii });
            double jd = (double)t.GetField("jd", F).GetValue(s);
            var toSun = OpenWorld.Frame.U((sys.Pos(sys.Sun, jd) - sys.Pos(b, jd)).Norm);
            var off = Quaternion.AngleAxis(phaseDeg, Vector3.up) * toSun;
            t.GetField("yaw", F).SetValue(s, Mathf.Atan2(off.x, -off.z) * Mathf.Rad2Deg);
            t.GetField("pitch", F).SetValue(s, Mathf.Clamp(Mathf.Asin(Mathf.Clamp(off.y, -1, 1)) * Mathf.Rad2Deg + 10, -80, 80));
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

        void Textures()
        {
            var all = Resources.FindObjectsOfTypeAll<Texture2D>();
            long total = 0;
            var rows = new List<(long bytes, string line)>();
            foreach (var t in all)
            {
                if (t.width < 256 && t.height < 256) continue;
                long bytes = 0;
                for (int m = 0; m < t.mipmapCount; m++)
                    bytes += (long)GraphicsFormatUtility.ComputeMipmapSize(Math.Max(1, t.width >> m), Math.Max(1, t.height >> m), t.graphicsFormat);
                total += bytes;
                rows.Add((bytes, $"  {bytes / 1048576.0,8:F1} MB  {t.width}x{t.height}  {t.graphicsFormat}  mips {t.mipmapCount}  stream {t.streamingMipmaps} loaded {(t.streamingMipmaps ? t.loadedMipmapLevel : -1)} desired {(t.streamingMipmaps ? t.desiredMipmapLevel : -1)}  {t.name}"));
            }
            log.WriteLine($"textures >= 256 px: {rows.Count}, {total / 1048576.0:F0} MB if fully resident");
            log.WriteLine($"Texture.currentTextureMemory {Texture.currentTextureMemory / 1048576} MB, desired {Texture.desiredTextureMemory / 1048576} MB, total {Texture.totalTextureMemory / 1048576} MB");
            foreach (var r in rows.OrderByDescending(r => r.bytes)) log.WriteLine(r.line);
        }
    }
}
