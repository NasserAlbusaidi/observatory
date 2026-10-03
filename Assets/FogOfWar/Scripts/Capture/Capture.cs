using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using A = FogOfWar.Controls.Action;

namespace FogOfWar
{
    // Screenshots (F12), photo mode (F10) and clip recording (F9). Files go to Pictures\FogOfWar.
    public static class Capture
    {
        static CaptureRunner runner;

        public static void Install(FogOfWarApp app)
        {
            runner = app.gameObject.AddComponent<CaptureRunner>();
            runner.Init(app);
        }

        public static string ShotKey => UiShell.Keys(A.Screenshot);
        public static string PhotoKey => UiShell.Keys(A.PhotoMode);
        public static string RecordKey => UiShell.Keys(A.RecordClip);
        public static bool PhotoActive => runner != null && runner.Photo;
        public static bool Recording => runner != null && runner.Recorder.Active;
        public static float RecordElapsed => runner != null ? runner.Recorder.Elapsed : 0;
        public static bool HandleEscape() => runner != null && runner.HandleEscape();
        public static void KeyEvent(KeyCode k) => runner?.KeyEvent(k);
        public static void KeyUp(KeyCode k) => runner?.KeyUp(k);
        // tests: fly the photo camera as WASD/QE would
        public static string RigState => runner == null ? "" : $"distance {runner.RigDistance:0.000}, roll slider {runner.RollShown:0}";
        public static void TestApproach() => runner?.TestApproach();
        public static void TestFly(Vector3 local, float seconds, float roll) => runner?.TestFly(local, seconds, roll);

        public static void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(GameSettings.CaptureDir);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + GameSettings.CaptureDir + "\"");
            }
            catch (Exception e) { Debug.LogWarning("Fog of War: could not open the capture folder: " + e.Message); }
        }

        public static string Stamp() => DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        // QA hooks
        public static void TakeScreenshot() => runner?.Shoot();
        public static string LastShot => runner?.LastShot;
        public static void StartClip(int seconds) => runner?.Recorder.Start(runner.Cam, seconds, GameSettings.ClipFps);
        public static string LastClip => runner?.Recorder.LastOutput;
        public static void TogglePhoto() => runner?.TogglePhoto();
    }

    [DefaultExecutionOrder(200)] // after the scene has placed the camera
    public partial class CaptureRunner : MonoBehaviour
    {
        FogOfWarApp app;
        public PhotoFX Fx { get; private set; }
        public Camera Cam => app.Cam;
        public ClipRecorder Recorder { get; private set; }
        bool shooting;
        readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();

        public void Init(FogOfWarApp app)
        {
            this.app = app;
            Fx = app.Cam.gameObject.GetComponent<PhotoFX>() ?? app.Cam.gameObject.AddComponent<PhotoFX>();
            Fx.ResetLook();
            Fx.enabled = false;
            Recorder = new ClipRecorder(this);
        }

        UiShell Shell => UiShell.Instance;
        void Toast(string text, string note = null) { if (Shell != null) Shell.Toast(text, note); else Debug.Log("Fog of War: " + text + " " + note); }
        public void Post(Action a) => mainThread.Enqueue(a);

        void Update()
        {
            while (mainThread.TryDequeue(out var a)) a();
            // read every frame (Pressed clears virtual taps); act only when keys aren't held back by a rebind
            bool shot = Controls.Pressed(A.Screenshot), photo = Controls.Pressed(A.PhotoMode), rec = Controls.Pressed(A.RecordClip);
            if (!app.KeysQuiet)
            {
                if (shot) Fire(A.Screenshot);
                if (photo) Fire(A.PhotoMode);
                if (rec) Fire(A.RecordClip);
            }
            if (Photo) PhotoUpdate();
        }

        void LateUpdate()
        {
            if (Photo) { CineLateUpdate(); app.Cam.fieldOfView = rig.Fov; LatePanel(); }
            if (Fx.enabled) { Fx.FocusScale = rig != null ? rig.Scale : 1; Fx.RenderDepth(); }
            Recorder.LateUpdate();
        }

        // one trigger per press, whether it arrives through Input polling or an IMGUI key event
        readonly System.Collections.Generic.Dictionary<A, int> firedAt = new System.Collections.Generic.Dictionary<A, int>();

        void Fire(A a)
        {
            if (firedAt.TryGetValue(a, out int f) && Time.frameCount - f < 6) return;
            firedAt[a] = Time.frameCount;
            if (a == A.Screenshot) Shoot();
            else if (a == A.PhotoMode) TogglePhoto();
            else if (a == A.RecordClip) ToggleRecord();
        }

        readonly System.Collections.Generic.HashSet<KeyCode> down = new System.Collections.Generic.HashSet<KeyCode>();
        public void KeyUp(KeyCode k) => down.Remove(k);

        public void KeyEvent(KeyCode k)
        {
            if (Controls.Locked || !down.Add(k)) return; // held keys auto-repeat KeyDown events
            foreach (var a in new[] { A.Screenshot, A.PhotoMode, A.RecordClip })
                if (Controls.Get(a, 0) == k || Controls.Get(a, 1) == k) Fire(a);
        }

        public bool HandleEscape()
        {
            if (Recorder.Active) { Recorder.Stop(); return true; }
            if (Photo) { ExitPhoto(); return true; }
            return false;
        }

        // ------------------------------------------------------------------------------------------ screenshots

        public void Shoot()
        {
            if (shooting) return;
            StartCoroutine(ShotRoutine());
        }

        // The camera renders on its own into a texture at 1×, 2× or 4× the screen size, so the HUD, labels and menus
        // are never in the shot and nothing on screen flickers.
        IEnumerator ShotRoutine()
        {
            shooting = true;
            yield return new WaitForEndOfFrame();
            var cam = app.Cam;
            int sw = cam.pixelWidth, sh = cam.pixelHeight;
            int scale = Mathf.Clamp(GameSettings.ShotScale, 1, 4);
            // stay inside the GPU's texture limit and about 140 megapixels
            while (scale > 1 && (Mathf.Max(sw, sh) * scale > SystemInfo.maxTextureSize || (long)sw * sh * scale * scale > 140_000_000L)) scale--;
            int w = sw * scale, h = sh * scale;
            byte[] raw = null;
            RenderTexture rt = null;
            var prev = cam.targetTexture;
            try
            {
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = scale == 1 ? 8 : 4 };
                cam.targetTexture = rt;
                cam.Render();
                // overlay cameras drawn on top of the main one (the open world's cockpit interior)
                foreach (var oc in Camera.allCameras)
                {
                    if (oc == cam || oc.depth <= cam.depth || oc.clearFlags != CameraClearFlags.Depth) continue;
                    var ot = oc.targetTexture;
                    oc.targetTexture = rt; oc.Render(); oc.targetTexture = ot;
                }
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var act = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                RenderTexture.active = act;
                raw = tex.GetRawTextureData<byte>().ToArray();
                Destroy(tex);
            }
            catch (Exception e) { Debug.LogError("Fog of War: screenshot failed: " + e.Message); }
            finally
            {
                cam.targetTexture = prev;
                if (rt != null) { rt.Release(); Destroy(rt); }
                shooting = false;
            }
            if (raw == null) { Toast("Screenshot failed"); yield break; }

            string dir = GameSettings.CaptureDir;
            string name = "FogOfWar_" + Capture.Stamp() + (scale > 1 ? $"_{scale}x" : "") + ".png";
            string path = Path.Combine(dir, name);
            Toast(scale > 1 ? $"Saving {w}×{h} screenshot…" : "Screenshot taken", null);
            LastShot = null;
            // PNG encoding of a large shot takes seconds: do it off the main thread
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    var png = ImageConversion.EncodeArrayToPNG(raw, GraphicsFormat.R8G8B8A8_SRGB, (uint)w, (uint)h);
                    File.WriteAllBytes(path, png);
                    Post(() => { LastShot = path; Toast("Screenshot saved", name); });
                }
                catch (Exception e) { Post(() => { Debug.LogError("Fog of War: could not save the screenshot: " + e.Message); Toast("Screenshot failed", e.Message); }); }
            });
        }

        public string LastShot { get; private set; }

        // ------------------------------------------------------------------------------------------ clips

        public void ToggleRecord()
        {
            if (Recorder.Active) { Recorder.Stop(); return; }
            if (app.MenuOpen) return;
            Recorder.Start(app.Cam, GameSettings.ClipSeconds, GameSettings.ClipFps);
        }

        public void RecorderMessage(string text, string note) => Toast(text, note);
    }
}
