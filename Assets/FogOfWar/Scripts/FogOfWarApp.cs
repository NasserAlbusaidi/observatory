using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // Owns the camera, the scene list and the IMGUI shell (scene rail on the left, instrument panel on the right).
    public class FogOfWarApp : MonoBehaviour
    {
        public Camera Cam { get; private set; }
        public OrbitCamera Orbit { get; private set; }
        public int Quality = 1; // 0 fast, 1 balanced, 2 sharp
        public float StepsScale => Quality == 0 ? 0.55f : Quality == 1 ? 0.8f : 1f;

        SimScene[] scenes;
        SimScene current;
        int currentIndex = -1;
        public int SceneCount => scenes.Length;
        public SimScene Current => current;
        public bool InputLocked;
        readonly PanelUI ui = new PanelUI();
        readonly List<Readout> readouts = new List<Readout>();
        float readoutTimer;
        bool showUI = true;
        Vector2 panelScroll, railScroll;
        Rect railRect, panelRect;
        bool dragging; int dragButton;
        float uiScale = 1;
        float simTime;

        void Awake()
        {
            Application.targetFrameRate = 120;
            Cam = Camera.main;
            if (Cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                Cam = go.AddComponent<Camera>();
            }
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color32(0x05, 0x06, 0x0B, 0xFF);
            Cam.nearClipPlane = 0.05f;
            Cam.farClipPlane = 6000f;
            // Leftover lights from a default scene don't affect our shaders, but switch them off to avoid confusion.
            foreach (var l in FindObjectsByType<Light>()) l.enabled = false;
            Orbit = new OrbitCamera(Cam);
            Art.BindSky();

            scenes = new SimScene[]
            {
                new WormholeScene(), new BlackHoleScene(), new WarpScene(), new JourneyScene(),
                new NegativeMassScene(), new TidesScene(), new MarsScene(), new MeteorScene(), new OpenSystemScene(),
            };
            Quality = PlayerPrefs.GetInt("fow-quality", 1);
            Activate(Mathf.Clamp(PlayerPrefs.GetInt("fow-scene", 0), 0, scenes.Length - 1));
            Tour.StartIfRequested(this);
            QA.StartIfRequested(this);
        }

        public void Activate(int i)
        {
            if (i == currentIndex) return;
            current?.Exit();
            currentIndex = i;
            current = scenes[i];
            Cam.fieldOfView = 55;
            Cam.farClipPlane = 6000f;
            current.Enter(this);
            readoutTimer = 0;
            panelScroll = Vector2.zero;
            PlayerPrefs.SetInt("fow-scene", i);
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            simTime += dt;
            Shader.SetGlobalFloat("_SimTime", simTime);
            current?.Tick(dt);
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            Orbit.Apply(dt);
            current?.LateTick(dt);
            readoutTimer -= Time.unscaledDeltaTime;
            if (readoutTimer <= 0 && current != null)
            {
                readoutTimer = 0.15f;
                readouts.Clear();
                current.Readouts(readouts);
            }
        }

        void OnDestroy() { current?.Exit(); }

        void OnGUI()
        {
            ui.Build();
            uiScale = Mathf.Clamp(Screen.height / 1000f, 0.85f, 2.2f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1));
            float W = Screen.width / uiScale, H = Screen.height / uiScale;
            var e = Event.current;
            // An unattended tour must not be steered by stray clicks or keys.
            if (InputLocked && (e.isMouse || e.isKey || e.type == EventType.ScrollWheel)) e.Use();

            if (e.type == EventType.KeyDown) HandleKey(e);

            if (showUI)
            {
                DrawRail(W, H);
                DrawPanel(W, H);
                GUI.Label(new Rect(W / 2 - 300, H - 30, 600, 20), "drag to orbit · scroll to zoom · 1–9 switch scene · H hide panels · F11 fullscreen · Esc quit", ui.hint);
            }
            else { railRect = panelRect = Rect.zero; }

            current?.OnGUIOverlay();

            // Camera input from whatever the panels didn't consume.
            bool overUI = railRect.Contains(e.mousePosition) || panelRect.Contains(e.mousePosition);
            switch (e.type)
            {
                case EventType.MouseDown when !overUI:
                    dragging = true; dragButton = e.button; break;
                case EventType.MouseUp:
                    dragging = false; break;
                case EventType.MouseDrag when dragging:
                    if (current == null || !current.HandleDrag(e.delta, dragButton)) Orbit.Drag(e.delta, dragButton != 0); e.Use(); break;
                case EventType.ScrollWheel when !overUI:
                    if (current == null || !current.HandleZoom(e.delta.y)) Orbit.Zoom(e.delta.y); e.Use(); break;
            }
        }

        void HandleKey(Event e)
        {
            if (e.keyCode == KeyCode.H) { showUI = !showUI; e.Use(); }
            else if (e.keyCode == KeyCode.F11) { Screen.fullScreen = !Screen.fullScreen; e.Use(); }
            else if (e.keyCode == KeyCode.Escape) { Application.Quit(); e.Use(); }
            else if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9 && e.keyCode - KeyCode.Alpha1 < scenes.Length) { Activate(e.keyCode - KeyCode.Alpha1); e.Use(); }
        }

        void DrawRail(float W, float H)
        {
            float w = 290;
            GUILayout.BeginArea(new Rect(16, 16, w, H - 32));
            GUILayout.Label("FOG OF WAR", ui.brand);
            GUILayout.Label("OBSERVATORY", ui.brandAccent);
            GUILayout.Space(4);
            GUILayout.Label("Everything we've argued about in this project, run on the real equations.", ui.sceneSub);
            GUILayout.Space(10);
            GUILayout.BeginVertical(ui.panel);
            railScroll = GUILayout.BeginScrollView(railScroll, GUIStyle.none, GUIStyle.none);
            for (int i = 0; i < scenes.Length; i++)
            {
                bool on = i == currentIndex;
                GUILayout.BeginVertical();
                if (GUILayout.Button($"{scenes[i].Title.ToUpperInvariant()}", on ? ui.sceneBtnOn : ui.sceneBtn)) Activate(i);
                GUILayout.Label(scenes[i].Short, ui.sceneSub);
                var r2 = GUILayoutUtility.GetLastRect();
                // the subtitle is part of the click target too
                if (Event.current.type == EventType.MouseDown && r2.Contains(Event.current.mousePosition)) { Activate(i); Event.current.Use(); }
                GUILayout.EndVertical();
                GUILayout.Space(2);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            var listRect = GUILayoutUtility.GetLastRect();
            GUILayout.EndArea();
            railRect = new Rect(16, 16, w, listRect.yMax);
        }

        void DrawPanel(float W, float H)
        {
            float w = Mathf.Min(390, W * 0.32f);
            panelRect = new Rect(W - w - 16, 16, w, H - 32);
            GUILayout.BeginArea(panelRect, ui.panel);
            ui.AvailWidth = w - 40;
            panelScroll = GUILayout.BeginScrollView(panelScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.Label(current.Eyebrow.ToUpperInvariant(), ui.eyebrow);
            GUILayout.Label(current.Title.ToUpperInvariant(), ui.title);
            GUILayout.Label(current.Blurb, ui.body);

            ui.Section("Readouts");
            foreach (var r in readouts)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(r.label, ui.label, GUILayout.Width(ui.AvailWidth * 0.48f));
                var prev = ui.value.normal.textColor;
                ui.value.normal.textColor = ui.ToneColor(r.tone);
                GUILayout.Label(r.value, ui.value);
                ui.value.normal.textColor = prev;
                GUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            ui.Section("Controls");
            current.Controls(ui);

            ui.Section("Ray-trace quality");
            int q = GUILayout.Toolbar(Quality, new[] { "Fast", "Balanced", "Sharp" }, ui.button);
            if (q != Quality) { Quality = q; PlayerPrefs.SetInt("fow-quality", q); }

            if (!string.IsNullOrEmpty(current.Note)) GUILayout.Label(current.Note, ui.note);
            GUILayout.Space(8);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
