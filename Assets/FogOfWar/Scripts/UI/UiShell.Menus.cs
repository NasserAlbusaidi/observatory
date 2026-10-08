using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using A = FogOfWar.Controls.Action;

namespace FogOfWar.UI
{
    // Menus, settings, help sheet, Observatory rail, key hints and toasts.
    public partial class UiShell
    {
        // menu indexes, as FogOfWarApp.ShowMenu takes them
        public const int MenuNone = 0, MenuMain = 1, MenuObservatory = 2, MenuControls = 3, MenuSettings = 4;

        VisualElement menuRoot, menuContent;
        Tabs settingsTabs;
        int settingsTab;
        readonly List<Action> menuUpdaters = new List<Action>();

        static Texture2D scrimH, scrimV;

        static Texture2D Gradient(bool vertical)
        {
            int n = 256;
            var t = new Texture2D(vertical ? 1 : n, vertical ? n : 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int i = 0; i < n; i++)
            {
                float u = i / (n - 1f);
                // dark on the menu side, clear toward the view; smooth so there's no visible edge
                float a = vertical ? Mathf.SmoothStep(0, 0.88f, u) : Mathf.SmoothStep(0.9f, 0, u);
                var c = new Color(0.012f, 0.016f, 0.035f, a);
                if (vertical) t.SetPixel(0, i, c); else t.SetPixel(i, 0, c);
            }
            t.Apply();
            return t;
        }

        void BuildMenu(int menu)
        {
            menuLayer.Clear();
            menuUpdaters.Clear();
            menuRoot = null;
            if (menu == MenuNone) return;
            if (scrimH == null) { scrimH = Gradient(false); scrimV = Gradient(true); }

            menuRoot = Ui.E(menuLayer, "menu-wrap");
            menuRoot.pickingMode = PickingMode.Position; // the menu swallows clicks meant for the view
            var bg = Ui.E(menuRoot, "layer");
            bg.pickingMode = PickingMode.Ignore;
            bg.style.backgroundImage = Portrait ? scrimV : scrimH;
            if (Portrait) bg.style.top = Length.Percent(0);
            if (app.GameStarted) bg.style.backgroundColor = new Color(0.01f, 0.012f, 0.03f, 0.35f);

            var side = Ui.E(menuRoot, "menu-side", "fade");
            Ui.L(side, "FOG OF WAR", "brand-a", "f-cond");
            Ui.L(side, "OBSERVATORY", "brand-b", "f-condm");
            Ui.E(side, "brand-rule");
            bool started = app.GameStarted && app.InGame;
            Ui.L(side, started ? "Flight paused. The solar system waits." : "The solar system at true scale, on today's date. Take a torch ship anywhere from Mercury to Neptune.", "tagline");

            if (started) Ui.MenuItem(side, "Resume", () => app.ShowMenu(MenuNone));
            Ui.MenuItem(side, started ? "New game" : "Start", () => app.NewGame());
            if (OpenSystemScene.HasSave)
            {
                Ui.MenuItem(side, "Continue", () => app.ContinueGame());
                Ui.L(side, "Saved: " + OpenSystemScene.SaveSummary, "menu-item", "sub");
            }
            if (started) Ui.MenuItem(side, "Save game", () => Toast(app.SaveGame() ? "Game saved" : OpenSystemScene.ChaseNoSave), "small");
            Ui.MenuItem(side, "Observatory", () => app.ShowMenu(MenuObservatory), "small");
            Ui.MenuItem(side, "Settings", () => { settingsTab = 0; app.ShowMenu(MenuSettings); }, "small");
            Ui.MenuItem(side, "Controls", () => app.ShowMenu(MenuControls), "small");
            Ui.MenuItem(side, "Quit", Application.Quit, "small");

            var foot = Ui.E(menuRoot, "menu-foot");
            foot.pickingMode = PickingMode.Ignore;
            Ui.Hint(foot, "Esc", started ? "back to flight" : "back");
            Ui.Hint(foot, "F11", "fullscreen");
            Ui.L(foot, DateTime.UtcNow.ToString("yyyy-MM-dd") + "  ·  JPL Horizons ephemeris", "t-label", "f-mono").style.marginLeft = 12;

            menuContent = Ui.E(menuRoot, "menu-content", "fade-up");
            switch (menu)
            {
                case MenuObservatory: BuildObservatory(menuContent); break;
                case MenuControls: settingsTab = 2; BuildSettings(menuContent); break;
                case MenuSettings: BuildSettings(menuContent); break;
            }
            Ui.Fontify(menuLayer);
            // animate in next frame (transitions need a style change after the first layout)
            side.schedule.Execute(() => { side.AddToClassList("in"); menuContent.AddToClassList("in"); }).StartingIn(16);
        }

        void UpdateMenu()
        {
            foreach (var u in menuUpdaters) u();
        }

        // ------------------------------------------------------------------------------------------ observatory

        void BuildObservatory(VisualElement host)
        {
            var card = Ui.Panel(host, "Observatory", "Eight physics models, with their limits explained", true, "menu-card");
            var sv = Ui.NewScroll();
            card.Add(sv);
            for (int i = 0; i < app.GameIndex; i++)
            {
                int k = i;
                var s = app.SceneAt(i);
                var item = Ui.E(sv.contentContainer, "scene-item");
                item.RegisterCallback<ClickEvent>(_ => app.Activate(k));
                var head = Ui.E(item, "row");
                Ui.L(head, $"{i + 1:00}", "t-label", "f-mono").style.marginRight = 10;
                Ui.L(head, s.Title.ToUpperInvariant(), "t-h3", "f-cond");
                Ui.L(item, s.Short, "t-note");
            }
            var row = Ui.E(card, "row");
            row.style.marginTop = 10;
            Ui.B(row, "Back", () => app.ShowMenu(MenuMain));
        }

        // ------------------------------------------------------------------------------------------ settings

        public void OpenSettings(int tab) { settingsTab = tab; builtMenu = -1; app.ShowMenu(MenuSettings); } // review shots

        void BuildSettings(VisualElement host)
        {
            var card = Ui.Panel(host, null, null, true, "menu-card");
            Ui.L(card, "SETTINGS", "t-h2", "f-cond", "glow-ink");
            settingsTabs = new Tabs();
            card.Add(settingsTabs);
            settingsTabs.style.flexShrink = 1;
            settingsTabs.Add("Graphics", Scroll(GraphicsTab));
            settingsTabs.Add("Interface", Scroll(InterfaceTab));
            settingsTabs.Add("Controls", Scroll(ControlsTab));
            settingsTabs.Add("Capture", Scroll(CaptureTab));
            settingsTabs.Add("Audio", Scroll(AudioTab));
            settingsTabs.Select(settingsTab);
            settingsTabs.Changed = i => settingsTab = i;
            var row = Ui.E(card, "row");
            row.style.marginTop = 12;
            Ui.B(row, "Back", () => { app.CancelRebind(); app.ShowMenu(MenuMain); });
        }

        static VisualElement Scroll(Action<VisualElement> fill) => Ui.Scroll(fill);

        void GraphicsTab(VisualElement c)
        {
            Ui.FormRow(c, "Display mode", out var ctl);
            ctl.Add(new Segmented(new[] { "Borderless", "Fullscreen", "Windowed" }, GameSettings.DisplayMode, GameSettings.SetDisplayMode));
            Ui.FormRow(c, "V-sync", out ctl);
            ctl.Add(new Switch(GameSettings.VSync, v => GameSettings.VSync = v));
            Ui.FormRow(c, "Frame-rate cap (when v-sync is off)", out ctl);
            int[] caps = { 60, 120, 144, 0 };
            ctl.Add(new Segmented(new[] { "60", "120", "144", "Off" }, Math.Max(0, Array.IndexOf(caps, GameSettings.FrameCap)), i => GameSettings.FrameCap = caps[i]));
            Ui.FormRow(c, "Texture detail (planet and sky maps: 8K, 4K or 2K)", out ctl);
            ctl.Add(new Segmented(new[] { "Full", "Half", "Quarter" }, GameSettings.TextureDetail, i => GameSettings.TextureDetail = i));
            Ui.FormRow(c, "Ray-trace quality (black hole, wormhole, warp scenes)", out ctl);
            ctl.Add(new Segmented(new[] { "Fast", "Balanced", "Sharp" }, app.Quality, i => { app.Quality = i; PlayerPrefs.SetInt("fow-quality", i); }));
            Ui.L(c, "Resolution follows the window. F11 toggles fullscreen.", "t-note").style.marginTop = 10;
        }

        void InterfaceTab(VisualElement c)
        {
            SliderRow(c, "UI scale", 0.7f, 1.6f, GameSettings.UiScale, v => v.ToString("0.00") + "×", v => GameSettings.UiScale = v, true);
            SliderRow(c, "HUD opacity", 0.3f, 1f, GameSettings.HudOpacity, v => Mathf.RoundToInt(v * 100) + " %", v => GameSettings.HudOpacity = v);
            Ui.FormRow(c, "Key hints at the bottom of the screen", out var ctl);
            ctl.Add(new Switch(GameSettings.ShowHints, v => GameSettings.ShowHints = v));
            Ui.FormRow(c, "Body labels in flight", out ctl);
            ctl.Add(new Switch(GameSettings.ShowLabels, v => GameSettings.ShowLabels = v));
            Ui.FormRow(c, "Frame-rate counter", out ctl);
            ctl.Add(new Switch(GameSettings.ShowFps, v => GameSettings.ShowFps = v));
            Ui.FormRow(c, "Classic instrument panel in flight (the original panel with every control)", out ctl);
            ctl.Add(new Switch(GameSettings.ClassicPanel, v => GameSettings.ClassicPanel = v));
        }

        // slider with its label and value above it; `onRelease` applies only when the drag ends (UI scale would
        // otherwise rescale the slider under the pointer)
        void SliderRow(VisualElement c, string label, float min, float max, float v0, Func<float, string> fmt, Action<float> set, bool onRelease = false)
        {
            var wrap = Ui.E(c, "frow");
            var lab = Ui.L(wrap, label, "flabel");
            var ctl = Ui.E(wrap, "fctl");
            var val = Ui.L(ctl, fmt(v0), "t-val", "f-mono");
            val.style.width = 70; val.style.marginRight = 12;
            var s = new FowSlider(min, max, v0, null);
            s.style.width = 220;
            ctl.Add(s);
            s.Changed = v => { val.text = fmt(v); if (!onRelease) set(v); };
            if (onRelease) s.RegisterCallback<PointerUpEvent>(_ => set(s.Value));
        }

        readonly Dictionary<(A, int), Label> bindSlots = new Dictionary<(A, int), Label>();
        Label rebindNote;

        void ControlsTab(VisualElement c)
        {
            bindSlots.Clear();
            Ui.L(c, "Click a slot, then press a key. Esc cancels, Delete clears. A key already in use swaps places.", "t-note");
            rebindNote = Ui.L(c, "", "t-note", "accent");
            foreach (var group in ActionGroups)
            {
                Ui.Section(c, group.name);
                foreach (var a in group.actions)
                {
                    var row = Ui.E(c, "bind-row");
                    Ui.L(row, Controls.Label[a], "t-body");
                    for (int s = 0; s < 2; s++)
                    {
                        int slot = s;
                        var b = Ui.L(row, "", "bind-slot", "f-mono");
                        b.pickingMode = PickingMode.Position;
                        b.RegisterCallback<ClickEvent>(_ => app.BeginRebind(a, slot));
                        bindSlots[(a, s)] = b;
                    }
                }
            }
            Ui.Section(c, "Fixed keys");
            Ui.L(c, "1–9 attitude holds (off, prograde, retrograde, normal, anti-normal, radial out, radial in, target, anti-target) · Esc menu · F2 hide the HUD · F11 fullscreen. These keys can't be assigned to other actions.", "t-note");
            var row2 = Ui.E(c, "row");
            row2.style.marginTop = 10;
            Ui.B(row2, "Reset to defaults", () => { Controls.ResetDefaults(); app.CancelRebind(); }, "danger");
            menuUpdaters.Add(UpdateBinds);
        }

        void UpdateBinds()
        {
            if (settingsTabs == null || settingsTabs.Index != 2) return;
            foreach (var kv in bindSlots)
            {
                var (a, s) = kv.Key;
                bool listen = app.Rebinding == a && app.RebindSlot == s;
                var k = Controls.Get(a, s);
                Ui.Text(kv.Value, listen ? "press a key…" : k == KeyCode.None ? "—" : KeyName(k));
                Ui.SetClass(kv.Value, "listen", listen);
                Ui.SetClass(kv.Value, "empty", !listen && k == KeyCode.None);
            }
            Ui.Text(rebindNote, app.RebindNote ?? "");
        }

        public static string KeyName(KeyCode c) => c switch
        {
            KeyCode.None => "", KeyCode.LeftShift => "L-Shift", KeyCode.RightShift => "R-Shift", KeyCode.LeftControl => "L-Ctrl", KeyCode.RightControl => "R-Ctrl",
            KeyCode.LeftAlt => "L-Alt", KeyCode.RightAlt => "R-Alt", KeyCode.Period => ".", KeyCode.Comma => ",", KeyCode.LeftBracket => "[", KeyCode.RightBracket => "]",
            KeyCode.KeypadPlus => "Num +", KeyCode.KeypadMinus => "Num -", KeyCode.Alpha0 => "0", KeyCode.Alpha1 => "1", KeyCode.Alpha2 => "2", KeyCode.Alpha3 => "3",
            KeyCode.Alpha4 => "4", KeyCode.Alpha5 => "5", KeyCode.Alpha6 => "6", KeyCode.Alpha7 => "7", KeyCode.Alpha8 => "8", KeyCode.Alpha9 => "9",
            KeyCode.BackQuote => "`", KeyCode.Slash => "/", KeyCode.Semicolon => ";", KeyCode.Quote => "'", KeyCode.Backslash => "\\", KeyCode.Minus => "-", KeyCode.Equals => "=",
            KeyCode.Return => "Enter", KeyCode.Space => "Space", KeyCode.Mouse0 => "LMB", KeyCode.Mouse1 => "RMB", KeyCode.Mouse2 => "MMB",
            _ => c.ToString(),
        };

        // Bound keys of an action as "W" or "Shift / Num +".
        public static string Keys(A a)
        {
            string k0 = KeyName(Controls.Get(a, 0)), k1 = KeyName(Controls.Get(a, 1));
            return k1 == "" ? k0 : k0 == "" ? k1 : k0 + " / " + k1;
        }

        static readonly (string name, A[] actions)[] ActionGroups = BuildGroups();

        static (string, A[])[] BuildGroups()
        {
            var groups = new List<(string, A[])>
            {
                ("Attitude", new[] { A.PitchDown, A.PitchUp, A.YawLeft, A.YawRight, A.RollLeft, A.RollRight }),
                ("Drive", new[] { A.ThrottleUp, A.ThrottleDown, A.ThrottleFull, A.ThrottleCut }),
                ("Thrusters (RCS)", new[] { A.RcsForward, A.RcsBack, A.RcsUp, A.RcsDown, A.RcsLeft, A.RcsRight }),
                ("Flight computer", new[] { A.FlightAssist, A.Flip, A.Engage, A.NextTarget, A.WarpUp, A.WarpDown }),
                ("View and game", new[] { A.Map, A.CameraMode, A.Help, A.QuickSave }),
                ("Capture", new[] { A.Screenshot, A.PhotoMode, A.RecordClip, A.MarkLog }),
            };
            // anything added to Controls later still shows up
            var listed = new HashSet<A>();
            foreach (var g in groups) foreach (var a in g.Item2) listed.Add(a);
            var rest = new List<A>();
            foreach (var a in Controls.All) if (!listed.Contains(a)) rest.Add(a);
            if (rest.Count > 0) groups.Add(("Other", rest.ToArray()));
            return groups.ToArray();
        }

        void CaptureTab(VisualElement c)
        {
            Ui.FormRow(c, "Screenshot resolution (supersampling)", out var ctl);
            int[] sc = { 1, 2, 4 };
            ctl.Add(new Segmented(new[] { "1×", "2×", "4×" }, Math.Max(0, Array.IndexOf(sc, GameSettings.ShotScale)), i => GameSettings.ShotScale = sc[i]));
            Ui.FormRow(c, "Clip length", out ctl);
            int[] len = { 10, 15, 20, 30 };
            ctl.Add(new Segmented(new[] { "10 s", "15 s", "20 s", "30 s" }, Math.Max(0, Array.IndexOf(len, GameSettings.ClipSeconds)), i => GameSettings.ClipSeconds = len[i]));
            Ui.FormRow(c, "Clip frame rate", out ctl);
            ctl.Add(new Segmented(new[] { "30 fps", "60 fps" }, GameSettings.ClipFps == 60 ? 1 : 0, i => GameSettings.ClipFps = i == 1 ? 60 : 30));
            Ui.FormRow(c, "Clip quality", out ctl);
            ctl.Add(new Segmented(new[] { "Small", "High", "Max" }, GameSettings.ClipQuality, i => GameSettings.ClipQuality = i));
            Ui.Section(c, "Folder");
            Ui.L(c, GameSettings.CaptureDir, "t-body", "f-mono");
            var row = Ui.E(c, "row");
            row.style.marginTop = 8;
            Ui.B(row, "Open folder", Capture.OpenFolder);
            Ui.L(c, "Screenshots hide the HUD. Clips need ffmpeg on the PATH for MP4; without it they are saved as PNG frames with an encode script.", "t-note").style.marginTop = 10;
        }

        void AudioTab(VisualElement c)
        {
            Ui.L(c, "Space carries no sound. In the cockpit view you hear what reaches you through the hull and the cabin air: the drive, the thrusters, the machinery, life support and the console. Outside views are silent apart from the console.", "t-note");
            Ui.FormRow(c, "Ship sound", out var ctl);
            ctl.Add(new Switch(GameSettings.ShipSound, v => GameSettings.ShipSound = v));
            string Pct(float v) => Mathf.RoundToInt(v * 100) + " %";
            SliderRow(c, "Master volume", 0, 1, GameSettings.Master, Pct, v => GameSettings.Master = v);
            SliderRow(c, "Drive, thrusters and machinery", 0, 1, GameSettings.Effects, Pct, v => GameSettings.Effects = v);
            SliderRow(c, "Life support and hull", 0, 1, GameSettings.Music, Pct, v => GameSettings.Music = v);
            SliderRow(c, "Console and alarms", 0, 1, GameSettings.Chimes, Pct, v => GameSettings.Chimes = v);
            Ui.FormRow(c, "Cinematic sound in outside views (not physical)", out ctl);
            ctl.Add(new Switch(GameSettings.CinematicSound, v => GameSettings.CinematicSound = v));
            Ui.FormRow(c, "Mute when the game is in the background", out ctl);
            ctl.Add(new Switch(GameSettings.MuteInBackground, v => GameSettings.MuteInBackground = v));
            Ui.L(c, "Cinematic sound adds the film version outside the ship: a roaring drive and hissing thrusters. Real vacuum carries none of it. The ship is silent while a menu is open. There is no music.", "t-note").style.marginTop = 10;
        }

        // ------------------------------------------------------------------------------------------ chrome

        VisualElement rail, railList, hintBar, helpRoot;
        Label fpsLabel;
        readonly List<VisualElement> railItems = new List<VisualElement>();
        string hintKey;

        void BuildChrome()
        {
            rail = Ui.E(chromeLayer, "rail");
            hintBar = Ui.E(chromeLayer, "hintbar");
            hintBar.pickingMode = PickingMode.Ignore;
            recChip = Ui.E(chromeLayer, "rec-box");
            Ui.E(recChip, "rec-dot");
            recText = Ui.L(recChip, "REC", "t-body", "f-mono");
            Ui.Show(recChip, false);
            fpsLabel = Ui.L(chromeLayer, "", "t-label", "f-mono");
            fpsLabel.style.position = Position.Absolute; fpsLabel.style.right = 12; fpsLabel.style.bottom = 8;
            helpRoot = Ui.E(overlayLayer, "layer");
            helpRoot.pickingMode = PickingMode.Ignore;
            Ui.Show(helpRoot, false);
            Ui.Fontify(chromeLayer);
        }

        void RebuildRail()
        {
            rail.Clear(); railItems.Clear();
            if (app.InGame) return;
            Ui.L(rail, "FOG OF WAR", "rail-brand", "f-cond");
            Ui.L(rail, "OBSERVATORY", "rail-brand-b", "f-condm");
            var p = Ui.Panel(rail, null, null, true, "soft");
            p.style.paddingLeft = 0; p.style.paddingRight = 0; p.style.paddingTop = 6; p.style.paddingBottom = 6;
            for (int i = 0; i < app.GameIndex; i++)
            {
                int k = i;
                var s = app.SceneAt(i);
                var item = Ui.E(p, "scene-item");
                item.RegisterCallback<ClickEvent>(_ => app.Activate(k));
                var head = Ui.E(item, "row");
                Ui.L(head, (i + 1).ToString(), "t-label", "f-mono").style.marginRight = 8;
                Ui.L(head, s.Title.ToUpperInvariant(), "t-h3", "f-cond");
                Ui.L(item, s.Short, "t-note");
                railItems.Add(item);
            }
            var back = Ui.E(p, "scene-item");
            back.RegisterCallback<ClickEvent>(_ => app.ShowMenu(MenuMain));
            Ui.L(back, "◀  MAIN MENU", "t-h3", "f-cond", "dim");
            Ui.Fontify(rail);
        }

        void UpdateRail()
        {
            for (int i = 0; i < railItems.Count; i++) Ui.SetClass(railItems[i], "on", i == app.CurrentIndex);
        }

        VisualElement recChip; Label recText;

        void UpdateChrome()
        {
            bool rec = Capture.Recording && !Capture.PhotoActive;
            Ui.Show(recChip, rec);
            if (rec) Ui.Text(recText, $"REC  {Capture.RecordElapsed:0.0} s   ·   {Capture.RecordKey} stop");
            Ui.Show(fpsLabel, GameSettings.ShowFps && !HideHud);
            bool hints = GameSettings.ShowHints && !app.MenuOpen && app.ShowUI && !HideHud && !app.HelpOpen
                && (app.Current == null || app.Current.WantsKeyHints);
            Ui.Show(hintBar, hints);
            if (!hints) return;
            string key = (app.InGame ? "g" : "o") + Keys(A.Help) + Keys(A.Map) + Keys(A.CameraMode) + Capture.PhotoKey + Capture.ShotKey;
            if (key == hintKey) return;
            hintKey = key;
            hintBar.Clear();
            Ui.Hint(hintBar, Keys(A.Help), "controls");
            if (app.InGame)
            {
                Ui.Hint(hintBar, Keys(A.Map), "system map");
                Ui.Hint(hintBar, Keys(A.CameraMode), "camera");
                Ui.Hint(hintBar, "Drag", "look");
            }
            else
            {
                Ui.Hint(hintBar, "1–8", "scenes");
                Ui.Hint(hintBar, "Drag", "orbit");
            }
            Ui.Hint(hintBar, Capture.ShotKey, "screenshot");
            Ui.Hint(hintBar, Capture.PhotoKey, "photo mode");
            Ui.Hint(hintBar, "F2", "hide HUD");
            Ui.Hint(hintBar, "Esc", "menu");
            Ui.Fontify(hintBar);
        }

        // ------------------------------------------------------------------------------------------ help sheet

        bool helpBuilt;

        void UpdateHelp()
        {
            bool open = app.HelpOpen && !app.MenuOpen && !HideHud;
            Ui.Show(helpRoot, open);
            if (!open) { helpBuilt = false; return; }
            if (helpBuilt) return;
            helpBuilt = true;
            helpRoot.Clear();
            var dim = Ui.E(helpRoot, "layer", "scrim");
            dim.pickingMode = PickingMode.Position;
            dim.RegisterCallback<ClickEvent>(_ => app.HelpOpen = false);
            var wrap = Ui.E(helpRoot, "layer");
            wrap.pickingMode = PickingMode.Ignore;
            wrap.style.alignItems = Align.Center; wrap.style.justifyContent = Justify.Center;
            var p = Ui.Panel(wrap, "Controls", null, true);
            p.style.width = Mathf.Min(W - 40, Portrait ? 900 : 1080);
            p.style.maxHeight = H - 60;
            if (app.InGame) Ui.L(p, "Flight is paused while this sheet is open.", "t-note");
            var sv = Ui.NewScroll();
            p.Add(sv);
            var cols = Ui.E(sv.contentContainer, "row");
            cols.style.alignItems = Align.FlexStart;
            cols.style.flexWrap = Wrap.Wrap;
            foreach (var g in ActionGroups)
            {
                var col = Ui.E(cols, "col");
                col.style.width = Portrait ? Length.Percent(100) : Length.Percent(50);
                col.style.paddingRight = 18;
                Ui.Section(col, g.name);
                foreach (var a in g.actions)
                {
                    var r = Ui.E(col, "kv");
                    Ui.L(r, Controls.Label[a], "t-body");
                    Ui.L(r, Keys(a), "t-val", "f-mono", "accent");
                }
            }
            var cap = Ui.E(cols, "col");
            cap.style.width = Portrait ? Length.Percent(100) : Length.Percent(50);
            cap.style.paddingRight = 18;
            Ui.Section(cap, "Display");
            foreach (var (k, what) in new[] { ("F2", "Hide the HUD"), ("F11", "Fullscreen"), ("Esc", "Menu") })
            {
                var r = Ui.E(cap, "kv");
                Ui.L(r, what, "t-body");
                Ui.L(r, k, "t-val", "f-mono", "accent");
            }
            Ui.L(sv.contentContainer, "1–9: attitude hold off / prograde / retrograde / normal / anti-normal / radial out / radial in / target / anti-target. " +
                $"Flip ({Keys(A.Flip)}) turns the nose 180° from where it points now; 3 points it against your velocity. Turning alone doesn't slow you: burn to brake. " +
                "Turning uses reaction wheels, which run on power and spend no fuel. " +
                "Mouse: drag to look, scroll to zoom, click a label to target it. Change keys in Esc → Settings → Controls.", "t-note").style.marginTop = 12;
            var row = Ui.E(p, "row");
            row.style.marginTop = 10;
            Ui.B(row, "Close  (" + Keys(A.Help) + ")", () => app.HelpOpen = false);
            Ui.Fontify(helpRoot);
        }

        // ------------------------------------------------------------------------------------------ toasts

        VisualElement toastBox;
        readonly List<(VisualElement e, float until)> toasts = new List<(VisualElement, float)>();

        public void Toast(string text, string note = null, float seconds = 3.5f, string tone = null)
        {
            if (toastBox == null) return;
            var t = Ui.E(toastBox, "toast");
            if (tone != null) t.AddToClassList(tone);
            t.pickingMode = PickingMode.Ignore;
            Ui.E(t, "dot");
            // title and note stack in one column, so a long note wraps under the title instead of squeezing it (QA-063)
            var col = Ui.E(t, "toast-text");
            col.pickingMode = PickingMode.Ignore;
            Ui.L(col, text, "t-body", "f-semi");
            if (!string.IsNullOrEmpty(note)) Ui.L(col, note, "t-note", "f-mono");
            Ui.Fontify(t);
            t.schedule.Execute(() => t.AddToClassList("in")).StartingIn(16);
            toasts.Add((t, Time.unscaledTime + seconds));
            while (toasts.Count > 4) { toasts[0].e.RemoveFromHierarchy(); toasts.RemoveAt(0); }
        }

        // review shots: each toast's title size; a title squeezed to one letter per line reads as narrow and tall (QA-063)
        public string TestToastReport()
        {
            var parts = new List<string>();
            foreach (var (e, _) in toasts)
            {
                var title = e.Q(className: "t-body");
                if (title == null) continue;
                var r = title.worldBound;
                parts.Add($"{r.width:0}x{r.height:0}{(r.width < 60 || r.height > 80 ? " SQUEEZED" : "")}{(e.ClassListContains("pay") ? " pay" : e.ClassListContains("bad") ? " bad" : "")}");
            }
            return string.Join(", ", parts);
        }

        void UpdateToasts()
        {
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var (e, until) = toasts[i];
                if (Time.unscaledTime > until) e.RemoveFromClassList("in");
                if (Time.unscaledTime > until + 0.4f) { e.RemoveFromHierarchy(); toasts.RemoveAt(i); }
            }
        }
    }
}
