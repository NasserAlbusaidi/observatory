using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace FogOfWar.UI
{
    // The runtime UI (UI Toolkit): main and pause menus, settings, the help sheet, the Observatory scene rail, key
    // hints, toasts and the host for a scene's own HUD. FogOfWarApp keeps the menu state; the shell draws it.
    // `-fowlegacyui` turns the shell off and brings back the original IMGUI menus.
    [DefaultExecutionOrder(100)] // after the app and the scene have placed the camera, so HUD markers line up
    public partial class UiShell : MonoBehaviour
    {
        public static UiShell Instance { get; private set; }
        public static bool Disabled => Array.IndexOf(Environment.GetCommandLineArgs(), "-fowlegacyui") >= 0;

        FogOfWarApp app;
        UIDocument doc;
        PanelSettings settings;
        public VisualElement Root { get; private set; }
        VisualElement hudLayer, chromeLayer, menuLayer, overlayLayer, toastLayer;
        public VisualElement HudLayer => hudLayer;
        public VisualElement OverlayLayer => overlayLayer;

        // logical (panel) size and scale
        public float Scale { get; private set; } = 1;
        public float W { get; private set; } = 1600;
        public float H { get; private set; } = 900;
        public bool Portrait { get; private set; }
        Vector2Int lastSize; float lastUserScale = -1;
        public RenderTexture Surface; // set by the offscreen screenshot harness

        // hidden for a screenshot or photo mode
        public bool HideAll;
        public bool HideHud;

        public static void Install(FogOfWarApp app)
        {
            if (Disabled) return;
            var s = app.gameObject.AddComponent<UiShell>();
            s.app = app;
        }

        void Awake() { Instance = this; }

        void Start()
        {
            if (app == null) app = GetComponent<FogOfWarApp>();
            Ui.LoadFonts();
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "Fog of War UI";
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/FowTheme");
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.sortingOrder = 10;
            settings.clearColor = false;
            doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            Root = doc.rootVisualElement;
            var uss = Resources.Load<StyleSheet>("UI/Fow");
            if (uss != null) Root.styleSheets.Add(uss); else Debug.LogWarning("Fog of War UI: missing Resources/UI/Fow.uss");
            Root.AddToClassList("fow-root");
            Root.style.unityFontDefinition = Ui.BodyFont;
            Root.pickingMode = PickingMode.Ignore;

            hudLayer = Layer("hud-layer");
            chromeLayer = Layer("chrome-layer");
            menuLayer = Layer("menu-layer");
            overlayLayer = Layer("overlay-layer");
            toastLayer = Layer("toast-layer");
            toastBox = Ui.E(toastLayer, "toasts");
            toastBox.pickingMode = PickingMode.Ignore;

            BuildChrome();
            GameSettings.Changed += OnSettingsChanged;
            GameSettings.ApplyDisplay();
            Relayout(true);
        }

        void OnDestroy()
        {
            GameSettings.Changed -= OnSettingsChanged;
            if (Instance == this) Instance = null;
        }

        void OnSettingsChanged() { settingsDirty = true; }
        bool settingsDirty;

        VisualElement Layer(string name)
        {
            var l = Ui.E(Root, "layer");
            l.name = name;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        // ------------------------------------------------------------------------------------------ frame

        int builtMenu = -1, builtScene = -1; bool builtHud;
        float fpsT; int fpsN;

        void LateUpdate()
        {
            if (Root == null) return;
            Relayout(false);
            if (settingsDirty) { settingsDirty = false; GameSettings.ApplyDisplay(); }

            Root.style.display = HideAll ? DisplayStyle.None : DisplayStyle.Flex;

            // menus
            int menu = app.MenuIndex;
            if (menu != builtMenu)
            {
                builtMenu = menu;
                BuildMenu(menu);
            }
            UpdateMenu();

            // the scene's own HUD and the scene rail
            if (app.SceneEpoch != builtScene || (app.Current != null && app.Current.HasHud != builtHud))
            {
                builtScene = app.SceneEpoch;
                hudLayer.Clear();
                builtHud = app.Current != null && app.Current.HasHud;
                if (builtHud) { app.Current.BuildHud(this, hudLayer); Ui.Fontify(hudLayer); }
                RebuildRail();
            }
            bool hudVisible = !app.MenuOpen && app.ShowUI && !HideHud && !app.HelpOpen;
            Ui.Show(hudLayer, hudVisible);
            hudLayer.style.opacity = GameSettings.HudOpacity;
            if (hudVisible && builtHud) app.Current.UpdateHud(this);
            Ui.Show(rail, hudVisible);
            if (rail.style.display == DisplayStyle.Flex) UpdateRail();

            UpdateChrome();
            UpdateHelp();
            UpdateToasts();

            fpsN++; fpsT += Time.unscaledDeltaTime;
            if (fpsT > 0.5f) { Ui.Text(fpsLabel, $"{fpsN / fpsT:0} FPS"); fpsN = 0; fpsT = 0; }
        }

        void Relayout(bool force)
        {
            int sw = Surface != null ? Surface.width : UnityEngine.Screen.width;
            int sh = Surface != null ? Surface.height : UnityEngine.Screen.height;
            var size = new Vector2Int(sw, sh);
            if (!force && size == lastSize && Mathf.Approximately(lastUserScale, GameSettings.UiScale) && settings.targetTexture == Surface) return;
            lastSize = size; lastUserScale = GameSettings.UiScale;
            Portrait = sh > sw * 1.05f;
            // landscape fits a 1600×900 layout; portrait a 1080×1700 one. Whichever axis is tighter sets the scale.
            float s = Portrait ? Mathf.Min(sw / 1080f, sh / 1700f) : Mathf.Min(sw / 1600f, sh / 900f);
            Scale = Mathf.Clamp(s * GameSettings.UiScale, 0.5f, 4f);
            settings.scale = Scale;
            W = sw / Scale; H = sh / Scale;
            Ui.SetClass(Root, "portrait", Portrait);
            Ui.SetClass(Root, "compact", !Portrait && W < 1400);
            // on very wide screens keep the HUD in a central band so it stays in view
            float band = Portrait ? W : Mathf.Min(W, 2100);
            float inset = (W - band) / 2;
            hudLayer.style.left = inset; hudLayer.style.right = inset;
            // offscreen review shots render the UI into a transparent texture instead of the screen
            settings.targetTexture = Surface;
            settings.clearColor = Surface != null;
            settings.colorClearValue = Color.clear;
        }

        // ------------------------------------------------------------------------------------------ input helpers

        // True when the mouse (screen pixels, origin bottom-left) is over a UI element that takes clicks.
        public bool PointerOver(Vector2 screen)
        {
            if (Root == null || Root.panel == null || HideAll) return false;
            var p = RuntimePanelUtils.ScreenToPanel(Root.panel, new Vector2(screen.x, UnityEngine.Screen.height - screen.y));
            var e = Root.panel.Pick(p);
            return e != null && e != Root;
        }

        public bool PointerOverMouse => PointerOver(Input.mousePosition);

        // Same, for a point in IMGUI coordinates (top-left origin, divided by the IMGUI scale).
        public bool UnderGui(Vector2 gui, float guiScale) => PointerOver(new Vector2(gui.x * guiScale, UnityEngine.Screen.height - gui.y * guiScale));

        // Screen pixel (origin bottom-left, from Camera.WorldToScreenPoint) → panel coordinates of the HUD layer.
        public Vector2 ScreenToHud(Vector3 sp)
        {
            int sh = Surface != null ? Surface.height : UnityEngine.Screen.height;
            float x = sp.x / Scale, y = (sh - sp.y) / Scale;
            return new Vector2(x - hudLayer.resolvedStyle.left, y);
        }

        public float HudWidth => W - 2 * (float.IsNaN(hudLayer.resolvedStyle.left) ? 0 : hudLayer.resolvedStyle.left);
    }
}
