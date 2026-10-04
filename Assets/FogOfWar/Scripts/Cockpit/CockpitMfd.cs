using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FogOfWar.Cockpit
{
    // The three MFDs, drawn by one UI Toolkit panel into one texture (three screens side by side). The panel takes no
    // input: its screen-to-panel mapping returns NaN, so it never steals clicks from the HUD.
    public sealed class CockpitMfd : IDisposable
    {
        public const int W = 512, H = 384, Screens = 3;
        static readonly Color Bg = new Color(0.010f, 0.016f, 0.018f), Amber = new Color(1f, 0.71f, 0.28f), Cyan = new Color(0.42f, 0.86f, 0.92f),
            Dim = new Color(0.45f, 0.55f, 0.56f), Red = new Color(1f, 0.32f, 0.25f), Grid = new Color(0.25f, 0.4f, 0.42f, 0.35f);

        public readonly RenderTexture Texture;
        readonly PanelSettings settings;
        readonly GameObject host;
        readonly VisualElement root;
        readonly Label[] titles = new Label[Screens];
        readonly VisualElement[] rowBoxes = new VisualElement[Screens];
        readonly List<(Label k, Label v)>[] rows = { new List<(Label, Label)>(), new List<(Label, Label)>(), new List<(Label, Label)>() };
        readonly VisualElement plot;

        // orbit plot, in plot units (−1…1, y up)
        readonly List<Vector2> orbit = new List<Vector2>();
        Vector2 shipAt, shipVel;
        float bodyR;
        bool bound;

        public CockpitMfd(Transform parent)
        {
            Texture = new RenderTexture(W * Screens, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "Cockpit MFDs", useMipMap = true, autoGenerateMips = true };
            Texture.Create();
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "Cockpit MFDs";
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/FowTheme");
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1;
            settings.targetTexture = Texture;
            settings.clearColor = true;
            settings.colorClearValue = Bg;
            settings.sortingOrder = -10;
            settings.SetScreenToPanelSpaceFunction(_ => new Vector2(float.NaN, float.NaN));

            host = new GameObject("Cockpit MFDs");
            host.transform.SetParent(parent, false);
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            root = doc.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.width = W * Screens; root.style.height = H;
            root.style.flexDirection = FlexDirection.Row;
            root.style.unityFontDefinition = Ui.BodyFont;

            for (int i = 0; i < Screens; i++)
            {
                var s = Box(root);
                s.style.width = W; s.style.height = H;
                s.style.paddingLeft = s.style.paddingRight = 22; s.style.paddingTop = 16; s.style.paddingBottom = 14;
                var head = Box(s);
                head.style.flexDirection = FlexDirection.Row; head.style.justifyContent = Justify.SpaceBetween;
                head.style.borderBottomWidth = 2; head.style.borderBottomColor = Amber; head.style.paddingBottom = 4; head.style.marginBottom = 8;
                titles[i] = Text(head, "", 26, Amber);
                titles[i].style.unityFontStyleAndWeight = FontStyle.Bold;
                Text(head, "MFD " + (i + 1), 18, Dim);
                if (i == 1)
                {
                    plot = Box(s);
                    plot.style.flexGrow = 1;
                    plot.generateVisualContent += DrawPlot;
                }
                rowBoxes[i] = Box(s);
                if (i != 1) rowBoxes[i].style.flexGrow = 1;
            }
            root.style.display = DisplayStyle.None;
        }

        static VisualElement Box(VisualElement parent)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            parent.Add(e);
            return e;
        }

        static Label Text(VisualElement parent, string s, int size, Color col)
        {
            var l = new Label(s) { pickingMode = PickingMode.Ignore };
            l.style.fontSize = size; l.style.color = col;
            l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0;
            l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0;
            parent.Add(l);
            return l;
        }

        public void Show(bool on) => root.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        // One screen's page: a title and key/value rows.
        public void Page(int screen, string title, IList<Readout> lines)
        {
            Ui.Text(titles[screen], title);
            var list = rows[screen]; var box = rowBoxes[screen];
            while (list.Count < lines.Count)
            {
                var r = Box(box);
                r.style.flexDirection = FlexDirection.Row; r.style.justifyContent = Justify.SpaceBetween; r.style.marginBottom = 5;
                list.Add((Text(r, "", 20, Dim), Text(r, "", 24, Cyan)));
            }
            for (int i = 0; i < list.Count; i++)
            {
                bool used = i < lines.Count;
                list[i].k.parent.style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                if (!used) continue;
                Ui.Text(list[i].k, lines[i].label);
                Ui.Text(list[i].v, lines[i].value);
                list[i].v.style.color = lines[i].tone == Tone.Bad ? Red : lines[i].tone == Tone.Hot ? Amber : Cyan;
            }
        }

        // The orbit about the dominant body, in its own plane: relative position r and velocity v (km, km/s), GM, radius.
        public void Orbit(D3 r, D3 v, double mu, double radius)
        {
            orbit.Clear();
            var h = D3.Cross(r, v);
            if (h.Mag < 1e-9 || r.Mag < 1e-9) { bound = false; plot.MarkDirtyRepaint(); return; }
            var e1 = r.Norm; var e2 = D3.Cross(h.Norm, e1);
            double T = OpenWorld.Rails.Period(r, v, mu);
            bound = !double.IsInfinity(T);
            // a closed orbit once round; an open one ±(a few crossing times), clipped to a sensible frame
            double span = bound ? T : 6 * r.Mag / Math.Max(v.Mag, 1e-6);
            const int N = 120;
            var pts = new List<D3>(N + 1);
            double extent = Math.Max(r.Mag, radius) * 1.05;
            for (int i = 0; i <= N; i++)
            {
                double t = bound ? T * i / N : span * (2.0 * i / N - 1);
                if (!OpenWorld.Rails.Propagate(r, v, mu, t, out var p, out _)) continue;
                if (!bound && p.Mag > 6 * r.Mag) continue;
                pts.Add(p);
                extent = Math.Max(extent, p.Mag * 1.05);
            }
            Vector2 To(D3 p) => new Vector2((float)(D3.Dot(p, e1) / extent), (float)(D3.Dot(p, e2) / extent));
            foreach (var p in pts) orbit.Add(To(p));
            shipAt = To(r);
            shipVel = To(v.Norm * extent * 0.18);
            bodyR = (float)(radius / extent);
            plot.MarkDirtyRepaint();
        }

        void DrawPlot(MeshGenerationContext ctx)
        {
            var rect = plot.contentRect;
            if (rect.width < 4 || rect.height < 4) return;
            float s = Mathf.Min(rect.width, rect.height) * 0.48f;
            var c = rect.center;
            Vector2 P(Vector2 q) => new Vector2(c.x + q.x * s, c.y - q.y * s);
            var pt = ctx.painter2D;
            // range rings
            pt.strokeColor = Grid; pt.lineWidth = 1;
            for (int k = 1; k <= 2; k++) { pt.BeginPath(); pt.Arc(c, s * k / 2, 0, 360); pt.Stroke(); }
            // the body
            pt.fillColor = new Color(0.12f, 0.16f, 0.17f); pt.strokeColor = Dim; pt.lineWidth = 2;
            pt.BeginPath(); pt.Arc(c, Mathf.Max(bodyR * s, 3), 0, 360); pt.Fill(); pt.Stroke();
            // the path
            if (orbit.Count > 1)
            {
                pt.strokeColor = Cyan; pt.lineWidth = 2.5f;
                pt.BeginPath(); pt.MoveTo(P(orbit[0]));
                for (int i = 1; i < orbit.Count; i++) pt.LineTo(P(orbit[i]));
                if (bound) pt.ClosePath();
                pt.Stroke();
            }
            // the ship and its velocity
            pt.strokeColor = Amber; pt.lineWidth = 2;
            pt.BeginPath(); pt.MoveTo(P(shipAt)); pt.LineTo(P(shipAt + shipVel)); pt.Stroke();
            pt.fillColor = Amber;
            pt.BeginPath(); pt.Arc(P(shipAt), 6, 0, 360); pt.Fill();
        }

        public void Dispose()
        {
            if (host != null) Gfx.Destroy(host);
            if (settings != null) Gfx.Destroy(settings);
            if (Texture != null) { Texture.Release(); Gfx.Destroy(Texture); }
        }
    }
}
