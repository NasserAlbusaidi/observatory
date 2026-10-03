using System;
using System.Collections;
using System.IO;
using System.Reflection;
using FogOfWar.UI;
using UnityEngine;

namespace FogOfWar
{
    // Offscreen UI review: `FogOfWarUI.exe -batchmode -fowuishots <dir> [-fowuires 1920x1080,1440x2560]` renders the
    // camera and the UI Toolkit layer into textures at each resolution, composites them and saves a PNG per state.
    // No window opens, so it can run while someone else is playing. (IMGUI isn't drawn in batch mode: body labels
    // and the Observatory scenes' IMGUI panels are missing from these shots.)
    public class UiShots : MonoBehaviour
    {
        string dir;
        Vector2Int[] sizes = { new Vector2Int(1920, 1080), new Vector2Int(1440, 2560), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160), new Vector2Int(5120, 1440) };
        string only;
        StreamWriter log;
        int errors;

        public static void StartIfRequested(FogOfWarApp app)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-fowuishots");
            if (i < 0 || i + 1 >= args.Length) return;
            var s = app.gameObject.AddComponent<UiShots>();
            s.dir = args[i + 1];
            int j = Array.IndexOf(args, "-fowuires");
            if (j >= 0 && j + 1 < args.Length)
            {
                var parts = args[j + 1].Split(',');
                s.sizes = new Vector2Int[parts.Length];
                for (int k = 0; k < parts.Length; k++) { var wh = parts[k].Split('x'); s.sizes[k] = new Vector2Int(int.Parse(wh[0]), int.Parse(wh[1])); }
            }
            int o = Array.IndexOf(args, "-fowuionly");
            if (o >= 0 && o + 1 < args.Length) s.only = args[o + 1];
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
            log = new StreamWriter(Path.Combine(dir, "uishots.txt")) { AutoFlush = true };
            var app = GetComponent<FogOfWarApp>();
            Controls.Locked = true;
            app.InputLocked = true;
            yield return null;
            var shell = UiShell.Instance;
            if (shell == null) { log.WriteLine("no UI shell"); Quit(); yield break; }

            foreach (var size in sizes)
            {
                var camRT = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var uiRT = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                app.Cam.targetTexture = camRT;
                shell.Surface = uiRT;
                string tag = $"{size.x}x{size.y}";
                log.WriteLine("size " + tag);

                // fresh boot: main menu over the paused game
                app.Restart(app.GameIndex); app.ShowMenu(UiShell.MenuMain); SetStarted(app, false);
                yield return Shot(app, shell, camRT, uiRT, tag, "01-main-menu");
                shell.OpenSettings(0);
                yield return Shot(app, shell, camRT, uiRT, tag, "02-settings");
                shell.OpenSettings(4);
                yield return Shot(app, shell, camRT, uiRT, tag, "02b-audio");
                app.ShowMenu(UiShell.MenuControls);
                yield return Shot(app, shell, camRT, uiRT, tag, "03-controls");
                app.ShowMenu(UiShell.MenuObservatory);
                yield return Shot(app, shell, camRT, uiRT, tag, "04-observatory-menu");

                // flight
                app.NewGame();
                yield return Wait(1.5f);
                yield return Shot(app, shell, camRT, uiRT, tag, "05-flight");
                Controls.Tap(Controls.Action.ThrottleFull);
                yield return Wait(1.2f);
                yield return Shot(app, shell, camRT, uiRT, tag, "06-flight-burn");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fowuidump") >= 0) Dump(shell.HudLayer, 0);
                if (size == sizes[0] && Array.IndexOf(Environment.GetCommandLineArgs(), "-fowcapturetest") >= 0) yield return CaptureTest();
                Controls.Tap(Controls.Action.ThrottleCut);
                var os = app.Current as OpenSystemScene;
                if (os != null)
                {
                    // cockpit view: straight ahead, then looking down at the console (HUD must stay off the MFDs)
                    var setCam = typeof(OpenSystemScene).GetMethod("SetCam", BindingFlags.Instance | BindingFlags.NonPublic);
                    var lookPitch = typeof(OpenSystemScene).GetField("lookPitch", BindingFlags.Instance | BindingFlags.NonPublic);
                    setCam.Invoke(os, new object[] { 2 });
                    yield return Wait(0.5f);
                    yield return Shot(app, shell, camRT, uiRT, tag, "06f-cockpit");
                    lookPitch.SetValue(os, 22f);
                    yield return Wait(0.3f);
                    yield return Shot(app, shell, camRT, uiRT, tag, "06g-cockpit-look-down");
                    setCam.Invoke(os, new object[] { 0 });
                    yield return null;
                }
                os?.ShowConsoleTab(5);
                yield return Shot(app, shell, camRT, uiRT, tag, "06b-codex");
                os?.ShowCodex(1);
                yield return Shot(app, shell, camRT, uiRT, tag, "06c-achievements");
                os?.ShowCodex(0);
                os?.ShowConsoleTab(0);
                if (os != null)
                    foreach (var k in new[] { OpenWorld.JobKind.Intercept, OpenWorld.JobKind.Rescue })
                    {
                        bool ok = os.TestTargetJob(k);
                        log.WriteLine($"    target a {k} job: {(ok ? "yes" : "none posted")}");
                        if (ok) yield return Shot(app, shell, camRT, uiRT, tag, "06d-target-" + k.ToString().ToLowerInvariant());
                        if (ok)
                            foreach (float frac in new[] { 0.62f, 0.72f, 0.95f })
                            {
                                // QA-046: target near the right-hand panels
                                os.TestAimTargetAt(frac);
                                yield return Shot(app, shell, camRT, uiRT, tag, $"06e-{k.ToString().ToLowerInvariant()}-at-{frac * 100:0}");
                                log.WriteLine("    " + os.TestLabelReport());
                            }
                    }
                Controls.Tap(Controls.Action.Help);
                yield return Wait(0.3f);
                yield return Shot(app, shell, camRT, uiRT, tag, "07-help");
                Controls.Tap(Controls.Action.Help);
                Controls.Tap(Controls.Action.Map);
                yield return Wait(1.0f);
                yield return Shot(app, shell, camRT, uiRT, tag, "08-system-map");
                Controls.Tap(Controls.Action.Map);
                yield return Wait(0.5f);
                app.ShowMenu(UiShell.MenuMain);
                yield return Shot(app, shell, camRT, uiRT, tag, "09-pause-menu");
                app.ShowMenu(UiShell.MenuNone);
                Capture.TogglePhoto();
                yield return Wait(0.5f);
                yield return Shot(app, shell, camRT, uiRT, tag, "10-photo-mode");
                // QA-028: the free camera must actually move the view in the open world
                Capture.TestFly(new Vector3(0, 0.6f, -6f), 1f, 30f);
                yield return Shot(app, shell, camRT, uiRT, tag, "10b-photo-moved");
                log.WriteLine("    after move: " + Capture.RigState);
                Capture.TestApproach();
                yield return Shot(app, shell, camRT, uiRT, tag, "10c-photo-hull");
                log.WriteLine("    after flying at the hull: " + Capture.RigState);
                Capture.TogglePhoto();
                app.Activate(1);
                yield return Wait(1.0f);
                yield return Shot(app, shell, camRT, uiRT, tag, "11-observatory-scene");

                app.Cam.targetTexture = null;
                shell.Surface = null;
                camRT.Release(); uiRT.Release();
                Destroy(camRT); Destroy(uiRT);
            }
            log.WriteLine($"done, {errors} errors");
            Quit();
        }

        IEnumerator CaptureTest()
        {
            foreach (int scale in new[] { 1, 2 })
            {
                GameSettings.ShotScale = scale;
                Capture.TakeScreenshot();
                float t0 = Time.realtimeSinceStartup;
                string before = Capture.LastShot;
                while (Capture.LastShot == null && Time.realtimeSinceStartup - t0 < 30) yield return null;
                log.WriteLine($"  screenshot {scale}x: {Capture.LastShot} {(Capture.LastShot != null ? new FileInfo(Capture.LastShot).Length : 0)} bytes");
                yield return Wait(1.1f); // distinct file names
            }
            GameSettings.ShotScale = 1;
            Capture.StartClip(3);
            float t1 = Time.realtimeSinceStartup;
            while (Capture.Recording && Time.realtimeSinceStartup - t1 < 120) yield return null;
            while (Capture.LastClip == null && Time.realtimeSinceStartup - t1 < 180) yield return null;
            log.WriteLine($"  clip: {Capture.LastClip} {(Capture.LastClip != null && File.Exists(Capture.LastClip) ? new FileInfo(Capture.LastClip).Length : 0)} bytes, {Time.realtimeSinceStartup - t1:0.0} s");
        }

        static void SetStarted(FogOfWarApp app, bool v)
        {
            var f = typeof(FogOfWarApp).GetField("gameStarted", BindingFlags.Instance | BindingFlags.NonPublic);
            f?.SetValue(app, v);
        }

        static IEnumerator Wait(float s)
        {
            float t = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < t) yield return null;
        }

        IEnumerator Shot(FogOfWarApp app, UiShell shell, RenderTexture camRT, RenderTexture uiRT, string tag, string name)
        {
            if (only != null && !name.Contains(only)) yield break;
            // a few frames for layout, transitions and the HUD to settle
            yield return Wait(0.6f);
            yield return new WaitForEndOfFrame();
            app.Cam.Render(); // batch mode runs no render loop; the UI panel draws itself into its texture
            // overlay cameras on top of the main one (the cockpit interior) render into the same target
            foreach (var oc in Camera.allCameras)
                if (oc != app.Cam && oc.depth > app.Cam.depth && oc.clearFlags == CameraClearFlags.Depth && oc.targetTexture == camRT) oc.Render();
            var cam = Read(camRT);
            var ui = Read(uiRT);
            var a = cam.GetPixels32(); var b = ui.GetPixels32();
            for (int i = 0; i < a.Length; i++)
            {
                // UI Toolkit writes premultiplied colour into its target
                int k = 255 - b[i].a;
                a[i].r = (byte)Mathf.Min(255, b[i].r + a[i].r * k / 255);
                a[i].g = (byte)Mathf.Min(255, b[i].g + a[i].g * k / 255);
                a[i].b = (byte)Mathf.Min(255, b[i].b + a[i].b * k / 255);
                a[i].a = 255;
            }
            cam.SetPixels32(a);
            File.WriteAllBytes(Path.Combine(dir, $"{tag}-{name}.png"), cam.EncodeToPNG());
            Destroy(cam); Destroy(ui);
            log.WriteLine($"  {name}");
        }

        void Dump(UnityEngine.UIElements.VisualElement e, int depth)
        {
            if (depth > 9) return;
            var l = e.layout;
            string txt = e is UnityEngine.UIElements.Label lab ? " \"" + lab.text.Replace('\n', ' ') + "\"" : "";
            log.WriteLine($"{new string(' ', depth * 2)}{e.GetType().Name} [{string.Join(",", e.GetClasses())}] {l.x:0},{l.y:0} {l.width:0}x{l.height:0}{txt}");
            foreach (var c in e.hierarchy.Children()) Dump(c, depth + 1);
        }

        static Texture2D Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            return t;
        }

        void Quit()
        {
            log?.Close(); log = null;
            Application.Quit();
        }
    }
}
