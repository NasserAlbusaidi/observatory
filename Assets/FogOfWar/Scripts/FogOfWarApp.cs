using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // Owns the camera, the scenes and the IMGUI shell. The game boots into the open world (Open System) behind a main
    // menu; the eight original physics scenes live in an "Observatory" submenu with their own scene rail.
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
        public int GameIndex => scenes.Length - 1;
        public SimScene Current => current;
        public bool InputLocked;
        public float UiScale => uiScale;
        public float PanelWidth { get; private set; }
        public bool UnderPanel(Vector2 guiPoint) => panelRect.Contains(guiPoint) || railRect.Contains(guiPoint) || menuRect.Contains(guiPoint)
            || (ShellActive && UI.UiShell.Instance.UnderGui(guiPoint, uiScale));

        enum Screen { None, Main, Observatory, Controls, Settings }
        Screen menu = Screen.Main;
        bool gameStarted;
        public bool MenuOpen => menu != Screen.None;
        public bool InGame => currentIndex == GameIndex;
        public bool HelpOpen;

        // ---- for the UI Toolkit shell (UI/UiShell) and the capture tools (Capture/)
        public bool ShellActive => UI.UiShell.Instance != null;
        public int MenuIndex => (int)menu;
        public bool GameStarted => gameStarted;
        public int CurrentIndex => currentIndex;
        public int SceneEpoch { get; private set; } // bumps whenever a scene is (re)built
        public SimScene SceneAt(int i) => scenes[i];
        public bool ShowUI { get => showUI; set => showUI = value; }
        // hides the IMGUI layer (panels, labels, scene overlays) for screenshots and photo mode
        public bool HideGui;
        // free camera for photo mode and cinematics; null when the scene drives the camera
        public CamRig Rig;
        public Controls.Action? Rebinding => rebinding;
        public int RebindSlot => rebindSlot;
        public string RebindNote => rebindNote;
        public void BeginRebind(Controls.Action a, int slot) { rebinding = a; rebindSlot = slot; rebindNote = null; }
        public void CancelRebind() => rebinding = null;
        // actions stay quiet for a few frames after a rebind, so the key just bound doesn't also fire
        public bool KeysQuiet => rebinding != null || Time.frameCount - rebindEndedFrame < 4;
        int rebindEndedFrame = -100;
        public void NewGame() => Restart(GameIndex);
        public void ContinueGame() { Activate(GameIndex); (current as OpenSystemScene)?.LoadGame(); }
        public void SaveGame() => (current as OpenSystemScene)?.SaveGame();

        readonly PanelUI ui = new PanelUI();
        readonly List<Readout> readouts = new List<Readout>();
        float readoutTimer;
        bool showUI = true;
        Vector2 panelScroll, railScroll, menuScroll, helpScroll;
        Rect railRect, panelRect, menuRect;
        bool dragging; int dragButton;
        float uiScale = 1;
        float simTime;
        Controls.Action? rebinding; int rebindSlot;
        string rebindNote;

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
            // clean captures from tours and QA runs, whose input is locked
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fowhidehud") >= 0) showUI = false;
            // the open world runs behind the main menu
            Activate(GameIndex);
            menu = Screen.Main;
            gameStarted = false;
            UI.UiShell.Install(this);
            Capture.Install(this);
            UiShots.StartIfRequested(this);
            Tour.StartIfRequested(this);
            QA.StartIfRequested(this);
            CareerQA.StartIfRequested(this);
        }

        // Switch scene (closes any menu). The open world is the game; the rest are Observatory scenes.
        public void Activate(int i)
        {
            menu = Screen.None;
            if (i == GameIndex) gameStarted = true;
            if (i == currentIndex) return;
            current?.Exit();
            currentIndex = i;
            current = scenes[i];
            Cam.fieldOfView = 55;
            Cam.farClipPlane = 6000f;
            current.Enter(this);
            SceneEpoch++;
            readoutTimer = 0;
            panelScroll = Vector2.zero;
        }

        // Rebuild a scene from scratch (New game).
        public void Restart(int i)
        {
            current?.Exit();
            currentIndex = -1;
            Activate(i);
        }

        public void OpenMenu() => menu = Screen.Main;
        // QA: 0 none, 1 main, 2 observatory, 3 controls
        public void ShowMenu(int which) => menu = (Screen)which;

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            simTime += dt;
            Shader.SetGlobalFloat("_SimTime", simTime);
            if (Controls.Pressed(Controls.Action.Help) && !KeysQuiet) HelpOpen = !HelpOpen;
            // the game pauses under its menu; Observatory scenes keep animating behind theirs
            if (!(MenuOpen && InGame)) current?.Tick(dt);
        }

        void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (Rig == null) Orbit.Apply(dt);
            else if (current != null && !current.RigInScene) current.RigApply(Rig);
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
            // scale to whichever of width or height is tighter, so portrait and ultrawide screens both fit
            uiScale = Mathf.Clamp(Mathf.Min(UnityEngine.Screen.height / 1000f, UnityEngine.Screen.width / 1100f), 0.7f, 2.2f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1));
            float W = UnityEngine.Screen.width / uiScale, H = UnityEngine.Screen.height / uiScale;
            var e = Event.current;
            // An unattended tour must not be steered by stray clicks or keys.
            if (InputLocked && (e.isMouse || e.isKey || e.type == EventType.ScrollWheel)) e.Use();

            if (rebinding != null && e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
            {
                if (e.keyCode != KeyCode.Escape) rebindNote = Controls.Set(rebinding.Value, rebindSlot, e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace ? KeyCode.None : e.keyCode);
                rebinding = null; rebindEndedFrame = Time.frameCount; e.Use();
            }
            // QA-030: catch capture keys from the event stream too, so very short taps aren't missed
            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None && !KeysQuiet) Capture.KeyEvent(e.keyCode);
            if (e.type == EventType.KeyUp) Capture.KeyUp(e.keyCode);
            if (e.type == EventType.KeyDown) HandleKey(e);

            menuRect = Rect.zero;
            if (ShellActive)
            {
                // the shell draws menus, rail, hints and help; IMGUI keeps the scene's instrument panel
                railRect = panelRect = Rect.zero;
                if (!MenuOpen && showUI && !HideGui && (!InGame || GameSettings.ClassicPanel)) DrawPanel(W, H);
            }
            else if (MenuOpen) { railRect = panelRect = Rect.zero; DrawMenu(W, H); }
            else if (showUI)
            {
                if (!InGame) DrawRail(W, H); else railRect = Rect.zero;
                DrawPanel(W, H);
                GUI.Label(new Rect(W / 2 - 360, H - 30, 720, 20), InGame
                    ? "F1 controls · drag to look · scroll to zoom · Esc menu · F2 hide panels"
                    : "drag to orbit · scroll to zoom · 1–8 switch scene · H hide panels · Esc menu", ui.hint);
            }
            else { railRect = panelRect = Rect.zero; }

            if (!MenuOpen && !HideGui && (showUI || !ShellActive)) current?.OnGUIOverlay(); // F2 hides labels too
            if (HelpOpen && !ShellActive) DrawHelp(W, H);

            // Camera input from whatever the panels didn't consume.
            bool overUI = railRect.Contains(e.mousePosition) || panelRect.Contains(e.mousePosition) || menuRect.Contains(e.mousePosition)
                || (ShellActive && (e.isMouse || e.type == EventType.ScrollWheel) && UI.UiShell.Instance.PointerOverMouse);
            if (Rig != null)
            {
                // photo mode: the mouse steers the free camera
                switch (e.type)
                {
                    case EventType.MouseDown when !overUI: dragging = true; dragButton = e.button; break;
                    case EventType.MouseUp: dragging = false; break;
                    case EventType.MouseDrag when dragging: Rig.Look(e.delta, dragButton); e.Use(); break;
                    case EventType.ScrollWheel when !overUI: Rig.Wheel(e.delta.y); e.Use(); break;
                }
                return;
            }
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
            if (rebinding != null) return;
            if (e.keyCode == KeyCode.Escape)
            {
                if (Capture.HandleEscape()) { }
                else if (HelpOpen) HelpOpen = false;
                else if (menu == Screen.Controls || menu == Screen.Observatory || menu == Screen.Settings) { menu = Screen.Main; rebindNote = null; }
                else if (MenuOpen) { if (gameStarted && InGame) menu = Screen.None; }
                else menu = Screen.Main;
                e.Use();
            }
            else if (e.keyCode == KeyCode.F11) { UnityEngine.Screen.fullScreen = !UnityEngine.Screen.fullScreen; e.Use(); }
            else if (Rig != null) { }
            else if (e.keyCode == KeyCode.F2 || (!InGame && e.keyCode == KeyCode.H)) { showUI = !showUI; e.Use(); }
            else if (!InGame && !MenuOpen && e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha8) { Activate(e.keyCode - KeyCode.Alpha1); e.Use(); }
        }

        // ------------------------------------------------------------------------------------------------ menus

        void DrawMenu(float W, float H)
        {
            float w = Mathf.Min(560, W - 32), h = Mathf.Min(H - 64, menu == Screen.Main ? 560 : menu == Screen.Observatory ? 760 : 940);
            menuRect = new Rect((W - w) / 2, (H - h) / 2, w, h);
            GUILayout.BeginArea(menuRect, ui.panel);
            ui.AvailWidth = w - 40;
            menuScroll = GUILayout.BeginScrollView(menuScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical(GUILayout.Width(ui.AvailWidth));
            GUILayout.Label("FOG OF WAR", ui.brand);
            GUILayout.Label("OBSERVATORY", ui.brandAccent);
            GUILayout.Space(6);
            switch (menu)
            {
                case Screen.Main: MainMenu(); break;
                case Screen.Observatory: ObservatoryMenu(); break;
                case Screen.Controls: ControlsMenu(); break;
            }
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void MainMenu()
        {
            GUILayout.Label("The solar system at true scale and today's date. Take the ship anywhere from Mercury to Neptune.", ui.body);
            GUILayout.Space(8);
            if (gameStarted && InGame && GUILayout.Button("Resume", ui.choice)) menu = Screen.None;
            if (GUILayout.Button("New game", ui.choice)) { Restart(GameIndex); }
            if (OpenSystemScene.HasSave && GUILayout.Button("Continue (" + OpenSystemScene.SaveSummary + ")", ui.choice))
            {
                Activate(GameIndex);
                (current as OpenSystemScene)?.LoadGame();
            }
            if (gameStarted && InGame && GUILayout.Button("Save game", ui.choice)) (current as OpenSystemScene)?.SaveGame();
            GUILayout.Space(6);
            if (GUILayout.Button("Observatory scenes", ui.choice)) menu = Screen.Observatory;
            if (GUILayout.Button("Controls", ui.choice)) menu = Screen.Controls;
            if (GUILayout.Button("Quit", ui.choice)) Application.Quit();
        }

        void ObservatoryMenu()
        {
            GUILayout.Label("Eight physics scenes, each on the real equations.", ui.body);
            for (int i = 0; i < GameIndex; i++)
            {
                if (GUILayout.Button($"{i + 1}. {scenes[i].Title}", ui.choice)) Activate(i);
                GUILayout.Label(scenes[i].Short, ui.sceneSub);
            }
            GUILayout.Space(6);
            if (GUILayout.Button("Back", ui.choice)) menu = Screen.Main;
        }

        void ControlsMenu()
        {
            GUILayout.Label("Click a key slot, then press the new key. Esc cancels; Delete clears the slot. A key already in use swaps places.", ui.body);
            GUILayout.Label(string.IsNullOrEmpty(rebindNote) ? " " : rebindNote, ui.note);
            foreach (var a in Controls.All)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Controls.Label[a], ui.label, GUILayout.Width(ui.AvailWidth * 0.5f));
                for (int s = 0; s < 2; s++)
                {
                    bool waiting = rebinding == a && rebindSlot == s;
                    var k = Controls.Get(a, s);
                    string text = waiting ? "press a key…" : k == KeyCode.None ? "—" : k.ToString();
                    if (GUILayout.Button(text, waiting ? ui.choiceOn : ui.button, GUILayout.Width(ui.AvailWidth * 0.24f))) { rebinding = a; rebindSlot = s; }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("Fixed keys: 1–9 attitude holds in flight (off, prograde, retrograde, normal, anti-normal, radial out, radial in, target, anti-target) · Esc menu · F2 hide panels · F11 fullscreen.", ui.note);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset to defaults", ui.button)) { Controls.ResetDefaults(); rebindNote = null; }
            if (GUILayout.Button("Back", ui.button)) { rebinding = null; rebindNote = null; menu = Screen.Main; }
            GUILayout.EndHorizontal();
        }

        void DrawHelp(float W, float H)
        {
            float w = Mathf.Min(520, W - 32), h = Mathf.Min(H - 64, 760);
            var r = new Rect((W - w) / 2, (H - h) / 2, w, h);
            GUILayout.BeginArea(r, ui.panel);
            ui.AvailWidth = w - 40;
            helpScroll = GUILayout.BeginScrollView(helpScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical(GUILayout.Width(ui.AvailWidth));
            GUILayout.Label("CONTROLS", ui.title);
            foreach (var a in Controls.All)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Controls.Label[a], ui.label, GUILayout.Width(ui.AvailWidth * 0.6f));
                GUILayout.Label(Controls.Keys(a), ui.value);
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("1–9: hold off / prograde / retrograde / normal / anti-normal / radial out / radial in / target / anti-target. Mouse: drag to look around, scroll to zoom. Change keys under Esc → Controls.", ui.note);
            GUILayout.Space(6);
            if (GUILayout.Button("Close (F1)", ui.button)) HelpOpen = false;
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            menuRect = r;
        }

        // ------------------------------------------------------------------------------------------------ panels

        void DrawRail(float W, float H)
        {
            float w = Mathf.Min(290, W * 0.3f);
            // QA-005: measure the list so the box ends with its last entry instead of stretching to the screen bottom
            float inner = w - ui.panel.padding.horizontal;
            float listH = 0;
            for (int i = 0; i < GameIndex; i++)
                listH += ui.sceneBtn.CalcHeight(new GUIContent(scenes[i].Title.ToUpperInvariant()), inner) + ui.sceneSub.CalcHeight(new GUIContent(scenes[i].Short), inner) + 2;
            listH += 6 + ui.sceneBtn.CalcHeight(new GUIContent("◀ MAIN MENU"), inner);
            float head = ui.brand.CalcHeight(new GUIContent("FOG OF WAR"), w) + ui.brandAccent.CalcHeight(new GUIContent("OBSERVATORY"), w) + 10;
            float boxH = Mathf.Min(listH + ui.panel.padding.vertical + 4, H - 32 - head);
            GUILayout.BeginArea(new Rect(16, 16, w, head + boxH));
            GUILayout.Label("FOG OF WAR", ui.brand);
            GUILayout.Label("OBSERVATORY", ui.brandAccent);
            GUILayout.Space(10);
            GUILayout.BeginVertical(ui.panel, GUILayout.Height(boxH));
            railScroll = GUILayout.BeginScrollView(railScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            for (int i = 0; i < GameIndex; i++)
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
            GUILayout.Space(6);
            if (GUILayout.Button("◀ MAIN MENU", ui.sceneBtn)) menu = Screen.Main;
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndArea();
            railRect = new Rect(16, 16, w, head + boxH);
        }

        void DrawPanel(float W, float H)
        {
            float w = Mathf.Min(InGame ? 430 : 390, W * (InGame ? 0.4f : 0.32f));
            panelRect = new Rect(W - w - 16, 16, w, H - 32);
            GUILayout.BeginArea(panelRect, ui.panel);
            ui.AvailWidth = w - 62;
            PanelWidth = w;
            panelScroll = GUILayout.BeginScrollView(panelScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginVertical(GUILayout.Width(ui.AvailWidth));
            GUILayout.Label(current.Eyebrow.ToUpperInvariant(), ui.eyebrow);
            GUILayout.Label(current.Title.ToUpperInvariant(), ui.title);
            if (!InGame) GUILayout.Label(current.Blurb, ui.body);

            ui.Section("Readouts");
            foreach (var r in readouts)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(r.label, ui.label, GUILayout.Width(ui.AvailWidth * 0.48f));
                var prev = ui.value.normal.textColor;
                ui.value.normal.textColor = ui.ToneColor(r.tone);
                GUILayout.Label(r.value, ui.value, GUILayout.Width(ui.AvailWidth * 0.5f));
                ui.value.normal.textColor = prev;
                GUILayout.EndHorizontal();
                GUILayout.Space(2);
            }

            ui.Section(InGame ? "Ship and navigation" : "Controls");
            current.Controls(ui);

            if (!InGame)
            {
                ui.Section("Ray-trace quality");
                int q = GUILayout.Toolbar(Quality, new[] { "Fast", "Balanced", "Sharp" }, ui.button);
                if (q != Quality) { Quality = q; PlayerPrefs.SetInt("fow-quality", q); }
            }

            if (!string.IsNullOrEmpty(current.Note)) GUILayout.Label(current.Note, ui.note);
            GUILayout.Space(8);
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
