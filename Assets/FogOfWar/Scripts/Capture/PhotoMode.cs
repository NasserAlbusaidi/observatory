using System;
using System.Collections.Generic;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FogOfWar
{
    // Photo mode: time stops (or slows), the HUD hides and a free camera flies anywhere. Lens and colour controls
    // feed PhotoFX. The Cinema tab drives the same camera on an orbit, a flyby or a keyframed path, and can record it.
    public partial class CaptureRunner
    {
        public bool Photo { get; private set; }
        CamRig rig;
        float savedTimeScale = 1, savedFov = 55;
        bool savedShowUI;
        int timeMode; // 0 paused, 1 0.1×, 2 0.25×, 3 1×
        static readonly float[] TimeScales = { 0, 0.1f, 0.25f, 1 };
        static readonly string[] TimeNames = { "Paused", "0.1×", "0.25×", "Normal" };
        static readonly float[] Letterboxes = { 0, 16f / 9f, 2.39f, 1f, 4f / 5f };
        static readonly string[] LetterboxNames = { "Off", "16:9", "2.39:1", "1:1", "4:5" };
        int letterbox;
        bool panelHidden;

        public void TogglePhoto()
        {
            if (Photo) ExitPhoto();
            else if (!app.MenuOpen && app.Current != null) EnterPhoto();
        }

        void EnterPhoto()
        {
            Photo = true;
            savedTimeScale = Time.timeScale;
            savedFov = app.Cam.fieldOfView;
            savedShowUI = app.ShowUI;
            rig = new CamRig { Fov = app.Cam.fieldOfView };
            app.Current.RigBegin(rig);
            rig.FromRotation();
            rig.ToRotation();
            rig.Speed = 0.6f;
            app.Rig = rig;
            app.HelpOpen = false;
            app.HideGui = true;
            timeMode = 0;
            Time.timeScale = TimeScales[timeMode];
            Fx.enabled = true;
            cineMode = CineMode.Free;
            keys.Clear();
            panelHidden = false;
            if (Shell != null) { Shell.HideHud = true; BuildPhotoPanel(); }
        }

        public void ExitPhoto()
        {
            if (!Photo) return;
            if (Recorder.Active) Recorder.Stop();
            Photo = false;
            app.Rig = null;
            rig = null;
            Time.timeScale = savedTimeScale;
            app.Cam.fieldOfView = savedFov;
            app.HideGui = false;
            app.ShowUI = savedShowUI;
            Fx.enabled = false;
            Fx.Letterbox = 0;
            if (Shell != null) { Shell.HideHud = false; photoRoot?.RemoveFromHierarchy(); photoRoot = null; }
        }

        void PhotoUpdate()
        {
            if (Controls.Locked) return;
            float dt = Time.unscaledDeltaTime;
            if (Input.GetKeyDown(KeyCode.H)) { panelHidden = !panelHidden; if (photoRoot != null) Ui.Show(photoPanel, !panelHidden); }
            if (Input.GetKeyDown(KeyCode.K)) AddKey();
            if (rig.Driven) return;
            var mv = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) mv.z += 1;
            if (Input.GetKey(KeyCode.S)) mv.z -= 1;
            if (Input.GetKey(KeyCode.D)) mv.x += 1;
            if (Input.GetKey(KeyCode.A)) mv.x -= 1;
            if (Input.GetKey(KeyCode.R) || Input.GetKey(KeyCode.Space)) mv.y += 1;
            if (Input.GetKey(KeyCode.F) || Input.GetKey(KeyCode.C)) mv.y -= 1;
            float boost = Input.GetKey(KeyCode.LeftShift) ? 4f : Input.GetKey(KeyCode.LeftControl) ? 0.2f : 1f;
            if (mv != Vector3.zero) rig.Move(mv * boost, dt);
            float roll = (Input.GetKey(KeyCode.Q) ? 1 : 0) - (Input.GetKey(KeyCode.E) ? 1 : 0);
            if (roll != 0) { rig.Roll = Mathf.Clamp(rig.Roll + roll * 30 * dt, -90, 90); rig.ToRotation(); rollSlider?.SetValueWithoutNotify(rig.Roll); }
        }

        // ------------------------------------------------------------------------------------------ cinematics

        enum CineMode { Free, Orbit, Flyby, Path }
        CineMode cineMode;
        struct Key { public Vector3 pos; public Quaternion rot; public float fov; }
        readonly List<Key> keys = new List<Key>();
        bool playing; float playT, playDur; bool recordOnPlay;
        Vector3 flyA, flyB;
        float orbitSpeed = 12; // degrees per second

        void AddKey()
        {
            keys.Add(new Key { pos = rig.Offset, rot = rig.Rotation, fov = rig.Fov });
            Shell?.Toast($"Keyframe {keys.Count} added");
        }

        void Play(bool record)
        {
            if (cineMode == CineMode.Path && keys.Count < 2) { Shell?.Toast("Add at least two keyframes (K)"); return; }
            playing = true; playT = 0; playDur = GameSettings.ClipSeconds;
            recordOnPlay = record;
            if (cineMode == CineMode.Flyby)
            {
                // pass the subject from front-left to back-left at a few times its size
                var fwd = Quaternion.Euler(0, rig.Yaw, 0) * Vector3.forward;
                var side = Vector3.Cross(Vector3.up, fwd).normalized;
                float d = rig.Offset.magnitude > 0 ? rig.Offset.magnitude : rig.Scale;
                flyA = -fwd * d * 2.2f - side * d * 0.6f + Vector3.up * d * 0.25f;
                flyB = fwd * d * 2.2f - side * d * 0.6f + Vector3.up * d * 0.1f;
            }
            rig.Driven = cineMode != CineMode.Free;
            if (record && !Recorder.Active) Recorder.Start(app.Cam, Mathf.CeilToInt(playDur), GameSettings.ClipFps);
        }

        void StopPlay()
        {
            playing = false;
            rig.Driven = false;
            rig.FromRotation();
            if (recordOnPlay && Recorder.Active) Recorder.Stop();
            recordOnPlay = false;
        }

        void CineLateUpdate()
        {
            if (!playing) { if (cineMode == CineMode.Orbit && orbitLive) OrbitStep(Time.unscaledDeltaTime); return; }
            // the recorder fixes the frame time, so a clip plays at its true speed whatever the frame rate
            float dt = Recorder.Active ? 1f / Recorder.Fps : Time.unscaledDeltaTime;
            playT += dt;
            float u = Mathf.Clamp01(playT / playDur);
            switch (cineMode)
            {
                case CineMode.Orbit: OrbitStep(dt); break;
                case CineMode.Flyby:
                {
                    float e = u * u * (3 - 2 * u);
                    rig.Offset = Vector3.Lerp(flyA, flyB, e);
                    rig.Rotation = Quaternion.LookRotation(-rig.Offset, Vector3.up) * Quaternion.Euler(0, 0, rig.Roll);
                    break;
                }
                case CineMode.Path: PathAt(u); break;
            }
            if (u >= 1) StopPlay();
        }

        bool orbitLive;

        void OrbitStep(float dt)
        {
            var q = Quaternion.AngleAxis(orbitSpeed * dt, Vector3.up);
            rig.Offset = q * rig.Offset;
            if (rig.Offset.sqrMagnitude < 1e-12f) rig.Offset = new Vector3(0, 0, -rig.Scale);
            rig.Rotation = Quaternion.LookRotation(-rig.Offset, Vector3.up) * Quaternion.Euler(0, 0, rig.Roll);
        }

        void PathAt(float u)
        {
            float e = u * u * (3 - 2 * u); // ease in and out
            float f = e * (keys.Count - 1);
            int i = Mathf.Min(Mathf.FloorToInt(f), keys.Count - 2);
            float t = f - i;
            Key k0 = keys[Mathf.Max(i - 1, 0)], k1 = keys[i], k2 = keys[i + 1], k3 = keys[Mathf.Min(i + 2, keys.Count - 1)];
            rig.Offset = CatmullRom(k0.pos, k1.pos, k2.pos, k3.pos, t);
            rig.Rotation = Quaternion.Slerp(k1.rot, k2.rot, t * t * (3 - 2 * t));
            rig.Fov = Mathf.Lerp(k1.fov, k2.fov, t);
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - p0 - 3 * p2 + p3) * t3);
        }

        // ------------------------------------------------------------------------------------------ panel

        VisualElement photoRoot, photoPanel;
        FowSlider rollSlider, fovSlider;
        Label focusLabel, recLabel, keysLabel, playLabel;
        VisualElement recBox;

        void BuildPhotoPanel()
        {
            photoRoot = Ui.E(Shell.OverlayLayer, "layer");
            photoRoot.pickingMode = PickingMode.Ignore;
            photoPanel = Ui.Panel(photoRoot, "Photo mode", null, true, "photo-panel");
            photoPanel.style.maxHeight = Shell.H - 40;
            var tabs = new Tabs(true);
            tabs.style.flexShrink = 1;
            photoPanel.Add(tabs);
            tabs.Add("Camera", Scroll(CameraTab));
            tabs.Add("Look", Scroll(LookTab));
            tabs.Add("Cinema", Scroll(CinemaTab));
            var hints = Ui.E(photoPanel, "row", "wrap");
            hints.style.marginTop = 8;
            foreach (var (k, w) in new[] { ("WASD", "move"), ("R/F", "up/down"), ("Q/E", "roll"), ("Shift", "fast"), ("Drag", "look"), ("Wheel", "zoom"), ("H", "hide panel"), (Capture.ShotKey, "photo"), ("Esc", "exit") })
                Ui.Hint(hints, k, w).style.marginLeft = 0;

            recBox = Ui.E(photoRoot, "rec-box");
            Ui.E(recBox, "rec-dot");
            recLabel = Ui.L(recBox, "REC", "t-body", "f-mono");
            Ui.Show(recBox, false);
            Ui.Fontify(photoRoot);
        }

        static VisualElement Scroll(Action<VisualElement> fill) => Ui.Scroll(fill);

        FowSlider Slider(VisualElement c, string label, float min, float max, float v, Func<float, string> fmt, Action<float> set)
        {
            var wrap = Ui.E(c, "slider-row");
            var head = Ui.E(wrap, "row");
            Ui.L(head, label.ToUpperInvariant(), "t-label", "f-condm");
            var val = Ui.L(head, fmt(v), "t-val", "f-mono");
            var s = new FowSlider(min, max, v, x => { val.text = fmt(x); set(x); });
            wrap.Add(s);
            return s;
        }

        void CameraTab(VisualElement c)
        {
            Ui.Section(c, "Time");
            c.Add(new Segmented(TimeNames, timeMode, i => { timeMode = i; Time.timeScale = TimeScales[i]; }));
            Ui.Section(c, "Lens");
            fovSlider = Slider(c, "Field of view", 5, 120, rig.Fov, v => v.ToString("0") + "°", v => rig.Fov = v);
            rollSlider = Slider(c, "Roll", -90, 90, rig.Roll, v => v.ToString("0") + "°", v => { rig.Roll = v; rig.ToRotation(); });
            Slider(c, "Move speed", -2, 2, Mathf.Log10(rig.Speed), v => Mathf.Pow(10, v).ToString("0.00") + "×", v => rig.Speed = Mathf.Pow(10, v));
            Ui.Section(c, "Depth of field");
            var r = Ui.E(c, "check-row");
            r.Add(new Switch(Fx.Dof, v => Fx.Dof = v));
            Ui.L(r, "Depth of field", "t-body");
            r = Ui.E(c, "check-row");
            r.Add(new Switch(Fx.AutoFocus, v => Fx.AutoFocus = v));
            Ui.L(r, "Autofocus on the centre", "t-body");
            Slider(c, "Focus (manual)", 0, 1, Fx.FocusT, v => v < 0.33f ? "near" : v < 0.66f ? "mid" : "far", v => Fx.FocusT = v);
            Slider(c, "Aperture", 0, 1, Fx.Aperture, v => "f/" + Mathf.Lerp(16, 1.2f, v).ToString("0.0"), v => Fx.Aperture = v);
            focusLabel = Ui.L(c, "", "t-note", "f-mono");
            Ui.Section(c, "Frame");
            c.Add(new Segmented(LetterboxNames, letterbox, i => { letterbox = i; Fx.Letterbox = Letterboxes[i]; }));
            var row = Ui.E(c, "row", "wrap");
            row.style.marginTop = 8;
            Ui.B(row, "Take photo  " + Capture.ShotKey, Shoot, "primary");
            Ui.B(row, "Reset camera", ResetCamera);
        }

        void ResetCamera()
        {
            StopPlayIfAny();
            app.Rig = null;
            var r = new CamRig { Fov = savedFov };
            app.Current.RigBegin(r);
            rig.Offset = r.Offset; rig.Rotation = r.Rotation; rig.Scale = r.Scale; rig.Roll = 0; rig.Fov = savedFov;
            rig.FromRotation(); rig.ToRotation();
            app.Rig = rig;
            fovSlider?.SetValueWithoutNotify(rig.Fov);
            rollSlider?.SetValueWithoutNotify(0);
        }

        void StopPlayIfAny() { if (playing) StopPlay(); }

        void LookTab(VisualElement c)
        {
            Ui.Section(c, "Filter");
            c.Add(new Segmented(PhotoFX.FilterNames, Fx.Filter, i => Fx.Filter = i, wrap: true));
            Ui.Section(c, "Grade");
            var sliders = new List<(FowSlider s, Func<float> reset)>();
            sliders.Add((Slider(c, "Exposure", -3, 3, Fx.Exposure, v => (v >= 0 ? "+" : "") + v.ToString("0.0") + " EV", v => Fx.Exposure = v), () => 0));
            sliders.Add((Slider(c, "Contrast", 0.5f, 1.8f, Fx.Contrast, v => v.ToString("0.00"), v => Fx.Contrast = v), () => 1));
            sliders.Add((Slider(c, "Saturation", 0, 2, Fx.Saturation, v => v.ToString("0.00"), v => Fx.Saturation = v), () => 1));
            sliders.Add((Slider(c, "Temperature", -1, 1, Fx.Temperature, v => v < -0.05f ? "cool" : v > 0.05f ? "warm" : "neutral", v => Fx.Temperature = v), () => 0));
            Ui.Section(c, "Lens character");
            sliders.Add((Slider(c, "Vignette", 0, 1, Fx.Vignette, v => Mathf.RoundToInt(v * 100) + " %", v => Fx.Vignette = v), () => 0.25f));
            sliders.Add((Slider(c, "Film grain", 0, 1, Fx.Grain, v => Mathf.RoundToInt(v * 100) + " %", v => Fx.Grain = v), () => 0));
            sliders.Add((Slider(c, "Chromatic aberration", 0, 1, Fx.Chroma, v => Mathf.RoundToInt(v * 100) + " %", v => Fx.Chroma = v), () => 0));
            var row = Ui.E(c, "row");
            row.style.marginTop = 8;
            Ui.B(row, "Reset look", () => { Fx.ResetLook(); foreach (var (s, r) in sliders) s.Value = r(); });
        }

        void CinemaTab(VisualElement c)
        {
            Ui.L(c, "Moves the photo camera by itself. Clips record without the HUD or this panel.", "t-note");
            Ui.Section(c, "Camera move");
            c.Add(new Segmented(new[] { "Free", "Orbit", "Flyby", "Path" }, (int)cineMode, i => { StopPlayIfAny(); cineMode = (CineMode)i; orbitLive = false; }));
            Slider(c, "Orbit speed", -45, 45, orbitSpeed, v => v.ToString("0") + "°/s", v => orbitSpeed = v);
            var orow = Ui.E(c, "check-row");
            orow.Add(new Switch(false, v => orbitLive = v));
            Ui.L(orow, "Orbit continuously in Orbit mode (for photos)", "t-body");
            Ui.Section(c, "Keyframes (Path)");
            keysLabel = Ui.L(c, "", "t-body", "f-mono");
            var krow = Ui.E(c, "row", "wrap");
            Ui.B(krow, "Add keyframe  K", AddKey);
            Ui.B(krow, "Clear", () => keys.Clear(), "danger");
            Ui.Section(c, "Length");
            int[] len = { 10, 15, 20, 30 };
            c.Add(new Segmented(new[] { "10 s", "15 s", "20 s", "30 s" }, Math.Max(0, Array.IndexOf(len, GameSettings.ClipSeconds)), i => GameSettings.ClipSeconds = len[i]));
            var prow = Ui.E(c, "row", "wrap");
            prow.style.marginTop = 6;
            playLabel = Ui.B(prow, "Play", () => { if (playing) StopPlay(); else Play(false); });
            Ui.B(prow, "Play and record", () => { if (!playing) Play(true); }, "primary");
            Ui.Section(c, "Recorder");
            Ui.L(c, ClipRecorder.FfmpegPath != null ? "MP4 through ffmpeg: " + ClipRecorder.FfmpegPath : "ffmpeg not found: clips are saved as PNG frames with an encode script.", "t-note");
            var rrow = Ui.E(c, "row", "wrap");
            Ui.B(rrow, "Record / stop  " + Capture.RecordKey, ToggleRecord);
            Ui.B(rrow, "Open folder", Capture.OpenFolder);
        }

        // live values in the panel; called from the shell's frame via PhotoUpdate → here
        void LatePanel()
        {
            if (photoRoot == null) return;
            if (focusLabel != null) Ui.Text(focusLabel, Fx.Dof ? "Focus at " + Fx.FocusDistance.ToString("0.###") + " (scene units)" : "");
            if (keysLabel != null) Ui.Text(keysLabel, keys.Count == 0 ? "No keyframes yet" : keys.Count + " keyframe" + (keys.Count == 1 ? "" : "s"));
            if (playLabel != null) Ui.Text(playLabel, playing ? "Stop" : "Play");
            Ui.Show(recBox, Recorder.Active);
            if (Recorder.Active) Ui.Text(recLabel, $"REC  {Recorder.Elapsed:0.0} / {Recorder.Length:0} s");
            if (fovSlider != null && !fovSlider.Dragging && Mathf.Abs(fovSlider.Value - rig.Fov) > 0.01f) fovSlider.SetValueWithoutNotify(rig.Fov);
        }
    }
}
