using System.Collections;
using System.IO;
using UnityEngine;

namespace FogOfWar
{
    // Unattended play-test: `FogOfWar.exe -fowtour <dir>` visits every scene, saves a screenshot of each to <dir>,
    // writes tour.txt with the errors logged along the way, then quits.
    public class Tour : MonoBehaviour
    {
        const float Dwell = 4f;
        string dir;
        int errors;
        StreamWriter log;

        public static void StartIfRequested(FogOfWarApp app)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-fowtour");
            if (i < 0 || i + 1 >= args.Length) return;
            var t = app.gameObject.AddComponent<Tour>();
            t.dir = args[i + 1];
        }

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        void OnLog(string msg, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert && type != LogType.Warning) return;
            if (type != LogType.Warning) errors++;
            log?.WriteLine($"[{type}] {msg}\n{stack}");
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(dir);
            log = new StreamWriter(Path.Combine(dir, "tour.txt")) { AutoFlush = true };
            var app = GetComponent<FogOfWarApp>();
            app.InputLocked = true;
            Controls.Locked = true;
            for (int s = 0; s < app.SceneCount; s++)
            {
                app.Activate(s);
                log.WriteLine($"scene {s + 1}: {app.Current.Title}");
                yield return Shot($"scene-{s + 1}");
                // The torch ship only shows in the 1g trip's chase view.
                if (app.Current is JourneyScene j) { j.ShowChase(); yield return Shot($"scene-{s + 1}-chase"); }
                if (app.Current is WarpScene w) { w.ShowCloseUp(); yield return Shot($"scene-{s + 1}-ship"); }
            }
            log.WriteLine($"done, {errors} errors");
            log.Close();
            log = null;
            Application.Quit();
        }

        IEnumerator Shot(string name)
        {
            float t0 = Time.realtimeSinceStartup;
            int f0 = Time.frameCount;
            yield return new WaitForSecondsRealtime(Dwell);
            float fps = (Time.frameCount - f0) / (Time.realtimeSinceStartup - t0);
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            log.WriteLine($"  {name}: {fps:F0} fps");
        }
    }
}
