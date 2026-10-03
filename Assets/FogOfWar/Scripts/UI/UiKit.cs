using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace FogOfWar.UI
{
    // Builders for the runtime UI: fonts, labels, buttons, switches, sliders, segmented choices and tabs. Every
    // control is a plain VisualElement styled by Resources/UI/Fow.uss, so the look doesn't depend on the theme.
    public static class Ui
    {
        static FontAsset body, semi, cond, condM, mono;
        static readonly Dictionary<string, FontAsset> byClass = new Dictionary<string, FontAsset>();

        public static void LoadFonts()
        {
            if (body != null) return;
            body = Font("Barlow-Medium");
            semi = Font("Barlow-SemiBold");
            cond = Font("BarlowCondensed-SemiBold");
            condM = Font("BarlowCondensed-Medium");
            mono = Font("JetBrainsMono-Medium");
            byClass["f-semi"] = semi; byClass["f-cond"] = cond; byClass["f-condm"] = condM; byClass["f-mono"] = mono;
        }

        static FontAsset Font(string name)
        {
            var f = Resources.Load<Font>("UI/Fonts/" + name);
            if (f == null) { Debug.LogWarning("Fog of War UI: missing font " + name); return null; }
            var fa = FontAsset.CreateFontAsset(f);
            if (fa != null) fa.hideFlags = HideFlags.DontSave;
            return fa;
        }

        public static StyleFontDefinition BodyFont => body != null ? new StyleFontDefinition(FontDefinition.FromSDFFont(body)) : default;

        // Apply the font classes (f-cond, f-mono, ...) under an element. The root carries the body font.
        public static void Fontify(VisualElement root)
        {
            foreach (var kv in byClass)
            {
                if (kv.Value == null) continue;
                var def = new StyleFontDefinition(FontDefinition.FromSDFFont(kv.Value));
                if (root.ClassListContains(kv.Key)) root.style.unityFontDefinition = def;
                root.Query(className: kv.Key).ForEach(e => e.style.unityFontDefinition = def);
            }
        }

        public static VisualElement E(params string[] cls)
        {
            var e = new VisualElement();
            foreach (var c in cls) e.AddToClassList(c);
            return e;
        }

        public static VisualElement E(VisualElement parent, params string[] cls)
        {
            var e = E(cls);
            parent?.Add(e);
            return e;
        }

        public static Label L(VisualElement parent, string text, params string[] cls)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (var c in cls) l.AddToClassList(c);
            parent?.Add(l);
            return l;
        }

        // A clickable element (not a uitk Button, so the theme doesn't style it). Not focusable: keys stay with flight.
        public static Label B(VisualElement parent, string text, Action click, params string[] cls)
        {
            var l = new Label(text) { focusable = false };
            l.AddToClassList("btn");
            foreach (var c in cls) l.AddToClassList(c);
            l.RegisterCallback<ClickEvent>(_ => click?.Invoke());
            parent?.Add(l);
            return l;
        }

        // Text-only menu entry.
        public static Label MenuItem(VisualElement parent, string text, Action click, params string[] cls)
        {
            var l = new Label(text.ToUpperInvariant()) { focusable = false };
            l.AddToClassList("menu-item");
            l.AddToClassList("f-cond");
            foreach (var c in cls) l.AddToClassList(c);
            l.RegisterCallback<ClickEvent>(_ => click?.Invoke());
            parent?.Add(l);
            return l;
        }

        public static Label Section(VisualElement parent, string text)
        {
            var l = L(parent, text.ToUpperInvariant(), "section", "f-condm");
            return l;
        }

        public static void Corners(VisualElement panel)
        {
            foreach (var c in new[] { "tl", "br" })
            {
                var e = E(panel, "corner", c);
                e.pickingMode = PickingMode.Ignore;
            }
        }

        public static VisualElement Panel(VisualElement parent, string title = null, string eyebrow = null, bool corners = true, params string[] cls)
        {
            var p = E(parent, "panel");
            foreach (var c in cls) p.AddToClassList(c);
            if (corners) Corners(p);
            if (eyebrow != null) L(p, eyebrow.ToUpperInvariant(), "t-micro", "f-condm");
            if (title != null)
            {
                var head = E(p, "panel-head");
                L(head, title.ToUpperInvariant(), "t-h3", "f-cond", "glow-ink");
            }
            return p;
        }

        public static (VisualElement row, Label value) KV(VisualElement parent, string key, string value = "", params string[] valueCls)
        {
            var r = E(parent, "kv");
            L(r, key.ToUpperInvariant(), "t-label", "f-condm");
            var v = L(r, value, "t-val", "f-mono");
            foreach (var c in valueCls) v.AddToClassList(c);
            return (r, v);
        }

        public static Label Keycap(VisualElement parent, string key, params string[] cls)
        {
            var k = L(parent, key, "keycap", "f-mono");
            foreach (var c in cls) k.AddToClassList(c);
            return k;
        }

        public static VisualElement Hint(VisualElement parent, string key, string what)
        {
            var h = E(parent, "hint");
            h.pickingMode = PickingMode.Ignore;
            if (!string.IsNullOrEmpty(key)) Keycap(h, key);
            L(h, what.ToUpperInvariant(), "t-label", "f-condm");
            return h;
        }

        public static void SetClass(VisualElement e, string cls, bool on)
        {
            if (on) e.AddToClassList(cls); else e.RemoveFromClassList(cls);
        }

        public static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        public static void Text(Label l, string s) { if (l.text != s) l.text = s; }

        // Vertical scroll view with a thin scroller and no horizontal bar.
        public static ScrollView NewScroll()
        {
            var sv = new ScrollView(ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Auto,
                focusable = false,
            };
            sv.AddToClassList("fow-scroll");
            sv.style.flexShrink = 1;
            return sv;
        }

        public static ScrollView Scroll(Action<VisualElement> fill)
        {
            var sv = NewScroll();
            fill(sv.contentContainer);
            return sv;
        }

        // Form row: label left, control right.
        public static VisualElement FormRow(VisualElement parent, string label, out VisualElement ctl)
        {
            var r = E(parent, "frow");
            L(r, label, "flabel");
            ctl = E(r, "fctl");
            return r;
        }
    }

    // On/off switch.
    public class Switch : VisualElement
    {
        bool on;
        public Action<bool> Changed;
        public Switch(bool value, Action<bool> changed)
        {
            AddToClassList("switch");
            var knob = new VisualElement { pickingMode = PickingMode.Ignore };
            knob.AddToClassList("knob");
            Add(knob);
            Changed = changed;
            Value = value;
            RegisterCallback<ClickEvent>(_ => { Value = !on; Changed?.Invoke(on); });
        }
        public bool Value { get => on; set { on = value; Ui.SetClass(this, "on", on); } }
    }

    // Horizontal slider with a filled track. Drag or click anywhere on it.
    public class FowSlider : VisualElement
    {
        readonly VisualElement fill, knob, track;
        float min, max, val;
        public Action<float> Changed;
        public Action<float> Display; // value readout; also follows SetValueWithoutNotify
        bool dragging;

        public FowSlider(float min, float max, float value, Action<float> changed)
        {
            this.min = min; this.max = max;
            AddToClassList("slider");
            track = Ui.E(this, "track"); track.pickingMode = PickingMode.Ignore;
            fill = Ui.E(track, "fill"); fill.pickingMode = PickingMode.Ignore;
            knob = Ui.E(this, "knob"); knob.pickingMode = PickingMode.Ignore;
            Changed = changed;
            SetValueWithoutNotify(value);
            RegisterCallback<PointerDownEvent>(e => { dragging = true; this.CapturePointer(e.pointerId); Seek(e.localPosition.x); e.StopPropagation(); });
            RegisterCallback<PointerMoveEvent>(e => { if (dragging) Seek(e.localPosition.x); });
            RegisterCallback<PointerUpEvent>(e => { dragging = false; this.ReleasePointer(e.pointerId); });
            RegisterCallback<GeometryChangedEvent>(_ => Place());
        }

        void Seek(float x)
        {
            float w = Mathf.Max(resolvedStyle.width, 1);
            float v = Mathf.Lerp(min, max, Mathf.Clamp01(x / w));
            if (Mathf.Abs(v - val) < 1e-6f) return;
            val = v; Place(); Display?.Invoke(val); Changed?.Invoke(val);
        }

        public float Value { get => val; set { SetValueWithoutNotify(value); Changed?.Invoke(val); } }

        public void SetValueWithoutNotify(float v)
        {
            val = Mathf.Clamp(v, min, max);
            Place();
            Display?.Invoke(val);
        }

        public bool Dragging => dragging;

        void Place()
        {
            float t = max > min ? (val - min) / (max - min) : 0;
            fill.style.width = Length.Percent(t * 100);
            knob.style.left = Length.Percent(t * 100);
        }
    }

    // A row of mutually exclusive options.
    public class Segmented : VisualElement
    {
        readonly List<Label> items = new List<Label>();
        int index;
        public Action<int> Changed;

        public Segmented(string[] options, int value, Action<int> changed, bool wrap = false)
        {
            AddToClassList("seg");
            if (wrap) AddToClassList("wrap");
            Changed = changed;
            for (int i = 0; i < options.Length; i++)
            {
                int k = i;
                var b = Ui.B(this, options[i], () => { if (items[k].ClassListContains("disabled")) return; Value = k; Changed?.Invoke(k); });
                if (i == options.Length - 1) b.AddToClassList("last");
                items.Add(b);
            }
            Value = value;
        }

        public int Value
        {
            get => index;
            set { index = value; for (int i = 0; i < items.Count; i++) Ui.SetClass(items[i], "on", i == index); }
        }

        public void SetLabel(int i, string text) => Ui.Text(items[i], text);
        public void SetEnabled(int i, bool on) => Ui.SetClass(items[i], "disabled", !on);
        public int Count => items.Count;
    }

    // Tab strip; shows one body at a time.
    public class Tabs : VisualElement
    {
        readonly List<Label> tabs = new List<Label>();
        readonly List<VisualElement> bodies = new List<VisualElement>();
        public int Index { get; private set; } = -1;
        public Action<int> Changed;
        public readonly VisualElement Strip;

        public Tabs(bool small = false)
        {
            Strip = Ui.E(this, "tabs");
            if (small) Strip.AddToClassList("small");
        }

        public VisualElement Add(string name, VisualElement body)
        {
            int k = tabs.Count;
            var t = Ui.L(Strip, name.ToUpperInvariant(), "tab", "f-cond");
            t.pickingMode = PickingMode.Position;
            t.RegisterCallback<ClickEvent>(_ => Select(k));
            tabs.Add(t);
            bodies.Add(body);
            hierarchy.Add(body);
            Ui.Show(body, false);
            if (Index < 0) Select(0);
            return body;
        }

        public void Select(int i)
        {
            if (i < 0 || i >= tabs.Count) return;
            Index = i;
            for (int k = 0; k < tabs.Count; k++) { Ui.SetClass(tabs[k], "on", k == i); Ui.Show(bodies[k], k == i); }
            Changed?.Invoke(i);
        }
    }
}
