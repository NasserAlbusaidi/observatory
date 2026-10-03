using System;
using UnityEngine;

namespace FogOfWar
{
    // IMGUI styling and controls for the instrument panel. Built in code so the project needs no UI assets.
    public class PanelUI
    {
        public static readonly Color Ink = new Color32(0xE6, 0xE9, 0xF5, 0xFF);
        public static readonly Color InkDim = new Color32(0x8D, 0x94, 0xB3, 0xFF);
        public static readonly Color Accent = new Color32(0xFF, 0xB5, 0x47, 0xFF);   // accretion-disk amber
        public static readonly Color Accent2 = new Color32(0x69, 0xD2, 0xFF, 0xFF);  // negative-energy cyan
        public static readonly Color Warn = new Color32(0xFF, 0x6B, 0x5E, 0xFF);
        static readonly Color PanelBg = new Color(0.04f, 0.047f, 0.086f, 0.86f);
        static readonly Color Line = new Color(0.63f, 0.69f, 0.9f, 0.16f);

        public GUIStyle panel, brand, brandAccent, eyebrow, title, body, section, label, value, button, sceneBtn, sceneBtnOn, sceneSub, sceneSubOn, toggle, hint, note, choice, choiceOn;
        public GUIStyle sliderStyle, thumbStyle;
        Texture2D checkOff, checkOn;
        bool built;

        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c); t.Apply();
            return t;
        }

        // Rounded-ish 9-slice box with a 1px border.
        static Texture2D Box(Color fill, Color border, int size = 12)
        {
            var t = new Texture2D(size, size) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    bool corner = (x < 2 || x > size - 3) && (y < 2 || y > size - 3);
                    var c = edge ? border : fill;
                    if (corner) c = new Color(fill.r, fill.g, fill.b, 0);
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        public void Build()
        {
            if (built) return;
            built = true;
            var font = GUI.skin.font;
            panel = new GUIStyle { normal = { background = Box(PanelBg, Line) }, border = new RectOffset(4, 4, 4, 4), padding = new RectOffset(16, 16, 14, 14) };
            brand = new GUIStyle { font = font, fontSize = 30, fontStyle = FontStyle.Bold, normal = { textColor = Ink }, wordWrap = true };
            brandAccent = new GUIStyle(brand) { normal = { textColor = Accent } };
            eyebrow = new GUIStyle { font = font, fontSize = 11, normal = { textColor = InkDim }, margin = new RectOffset(0, 0, 0, 2) };
            title = new GUIStyle { font = font, fontSize = 26, fontStyle = FontStyle.Bold, normal = { textColor = Ink }, wordWrap = true, margin = new RectOffset(0, 0, 0, 8) };
            body = new GUIStyle { font = font, fontSize = 14, normal = { textColor = new Color(0.77f, 0.79f, 0.87f) }, wordWrap = true, richText = true, margin = new RectOffset(0, 0, 0, 8) };
            section = new GUIStyle { font = font, fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = InkDim }, margin = new RectOffset(0, 0, 10, 4) };
            label = new GUIStyle { font = font, fontSize = 13, normal = { textColor = InkDim }, wordWrap = true };
            value = new GUIStyle { font = font, fontSize = 13, normal = { textColor = Ink }, alignment = TextAnchor.UpperRight, wordWrap = true };
            button = new GUIStyle
            {
                font = font, fontSize = 13, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(10, 10, 6, 6), margin = new RectOffset(0, 6, 0, 6),
                normal = { background = Box(new Color(1, 1, 1, 0.05f), Line), textColor = Ink },
                hover = { background = Box(new Color(1, 0.71f, 0.28f, 0.12f), Accent), textColor = Accent },
                active = { background = Box(new Color(1, 0.71f, 0.28f, 0.25f), Accent), textColor = Accent },
                border = new RectOffset(4, 4, 4, 4),
            };
            sceneBtn = new GUIStyle
            {
                font = font, fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 7, 0),
                normal = { textColor = Ink }, hover = { background = Tex(new Color(1, 1, 1, 0.05f)), textColor = Ink }, margin = new RectOffset(0, 0, 0, 0),
            };
            sceneBtnOn = new GUIStyle(sceneBtn) { normal = { background = Tex(new Color(1, 0.71f, 0.28f, 0.12f)), textColor = Accent }, hover = { background = Tex(new Color(1, 0.71f, 0.28f, 0.16f)), textColor = Accent } };
            sceneSub = new GUIStyle { font = font, fontSize = 12, normal = { textColor = InkDim }, padding = new RectOffset(10, 10, 0, 7), wordWrap = true };
            sceneSubOn = new GUIStyle(sceneSub);
            // The built-in skin's toggle draws "on" and "off" the same at runtime, so the check box is drawn by Toggle().
            toggle = new GUIStyle { font = font, fontSize = 13, alignment = TextAnchor.MiddleLeft, wordWrap = true, padding = new RectOffset(24, 0, 3, 3), normal = { textColor = Ink }, hover = { textColor = Accent } };
            checkOff = Box(new Color(1, 1, 1, 0.06f), new Color(0.63f, 0.69f, 0.9f, 0.45f));
            checkOn = Box(Accent, Accent);
            hint = new GUIStyle { font = font, fontSize = 12, normal = { textColor = InkDim }, alignment = TextAnchor.MiddleCenter };
            note = new GUIStyle(body) { fontSize = 13, normal = { textColor = InkDim }, padding = new RectOffset(10, 0, 0, 0), margin = new RectOffset(0, 0, 10, 0) };
            choice = new GUIStyle(button) { alignment = TextAnchor.MiddleLeft, margin = new RectOffset(0, 0, 0, 4), stretchWidth = true };
            choiceOn = new GUIStyle(choice) { normal = { background = Box(new Color(1, 0.71f, 0.28f, 0.16f), Accent), textColor = Accent } };
            sliderStyle = new GUIStyle(GUI.skin.horizontalSlider);
            thumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb);
        }

        public void Section(string text) => GUILayout.Label(text.ToUpperInvariant(), section);

        public bool Button(string text) => GUILayout.Button(text, button, GUILayout.ExpandWidth(false));

        // A wrapping row of buttons; returns the index clicked or -1.
        public int Buttons(params string[] labels)
        {
            int hit = -1;
            float avail = AvailWidth;
            float x = 0;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < labels.Length; i++)
            {
                float w = button.CalcSize(new GUIContent(labels[i])).x + 6;
                if (x > 0 && x + w > avail) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); x = 0; }
                if (GUILayout.Button(labels[i], button, GUILayout.ExpandWidth(false))) hit = i;
                x += w;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            return hit;
        }

        public float AvailWidth = 320;

        public float Slider(string text, float v, float min, float max, Func<float, string> fmt, float step = 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(text, label, GUILayout.Width(AvailWidth * 0.62f));
            GUILayout.FlexibleSpace();
            GUILayout.Label(fmt(v), value, GUILayout.Width(AvailWidth * 0.36f));
            GUILayout.EndHorizontal();
            float nv = GUILayout.HorizontalSlider(v, min, max, sliderStyle, thumbStyle, GUILayout.Width(AvailWidth));
            if (step > 0) nv = Mathf.Round(nv / step) * step;
            GUILayout.Space(8);
            return nv;
        }

        public bool Toggle(string text, bool v)
        {
            var content = new GUIContent(text);
            var r = GUILayoutUtility.GetRect(content, toggle, GUILayout.Width(AvailWidth));
            if (GUI.Button(r, content, toggle)) v = !v;
            GUI.DrawTexture(new Rect(r.x + 2, r.y + (r.height - 14) / 2, 14, 14), v ? checkOn : checkOff);
            GUILayout.Space(4);
            return v;
        }

        public int Choice(string text, int idx, string[] options)
        {
            if (!string.IsNullOrEmpty(text)) GUILayout.Label(text, label);
            for (int i = 0; i < options.Length; i++)
                if (GUILayout.Button(options[i], i == idx ? choiceOn : choice)) idx = i;
            GUILayout.Space(6);
            return idx;
        }

        public void Image(Texture t, float height)
        {
            var r = GUILayoutUtility.GetRect(AvailWidth, height, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(r, t, ScaleMode.StretchToFill);
        }

        public Color ToneColor(Tone t) => t == Tone.Hot ? Accent : t == Tone.Cold ? Accent2 : t == Tone.Bad ? Warn : Ink;
    }
}
