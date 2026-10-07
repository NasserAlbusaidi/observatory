using System;
using System.Collections.Generic;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FogOfWar
{
    // UI Toolkit HUD for the black hole sandbox: a top-down map of the equatorial plane (horizon, ergosphere, photon
    // orbits, ISCO, tidal radius, probe trails and debris) and the flare's light curve. Both are flat-space plots of
    // Boyer–Lindquist r and φ, not what the camera sees.
    public partial class BlackHoleScene
    {
        bool showMap = true;
        public override bool HasHud => showMap;

        VisualElement mapPlot, curvePlot, curveBox;
        Label mapScale, curveLabel;
        double mapR = 40;
        readonly List<double> hudRs = new List<double>();
        readonly HashSet<int> hudCells = new HashSet<int>();

        static readonly Color CHorizon = new Color(0.9f, 0.92f, 1f, 0.9f), CErgo = new Color(0.6f, 0.66f, 0.9f, 0.5f);
        static readonly Color CPhoton = new Color32(0x69, 0xD2, 0xFF, 0xE0), CIsco = new Color32(0xFF, 0xB5, 0x47, 0xE0);
        static readonly Color CTidal = new Color32(0xFF, 0x6B, 0x5E, 0xE0), CMb = new Color(0.7f, 0.7f, 0.8f, 0.5f);
        static readonly Color CTrail = new Color(1f, 0.85f, 0.5f, 0.95f), CLight = new Color(0.85f, 0.95f, 1f, 0.95f);

        public override void BuildHud(UiShell shell, VisualElement layer)
        {
            // bottom left, beside the scene rail (270 px wide at left 16) and clear of the key hints
            var col = Ui.E(layer, "row");
            col.pickingMode = PickingMode.Ignore;
            col.style.position = Position.Absolute;
            col.style.left = 300; col.style.bottom = 52; col.style.alignItems = Align.FlexEnd;

            var map = Ui.Panel(col, "Top view", "Equatorial plane");
            map.pickingMode = PickingMode.Ignore;
            map.style.width = 270;
            mapPlot = new VisualElement { pickingMode = PickingMode.Ignore };
            mapPlot.style.height = 230;
            mapPlot.generateVisualContent += DrawMap;
            // trails and debris beyond the plot's edge are clipped to it (QA-039)
            var clip = new VisualElement { pickingMode = PickingMode.Ignore };
            clip.style.overflow = Overflow.Hidden;
            clip.Add(mapPlot);
            map.Add(clip);
            var legend = Ui.E(map, "row");
            legend.style.flexWrap = Wrap.Wrap; legend.style.marginTop = 4;
            Key(legend, CIsco, "ISCO"); Key(legend, CPhoton, "photon orbit"); Key(legend, CTidal, "tidal radius"); Key(legend, CMb, "marginally bound");
            mapScale = Ui.L(map, "", "t-label", "f-mono");
            mapScale.style.whiteSpace = WhiteSpace.Normal;   // QA-053: wrap inside the panel rather than run into the next one

            curveBox = Ui.Panel(col, "Flare", "Light curve");
            curveBox.pickingMode = PickingMode.Ignore;
            curveBox.style.marginLeft = 10; curveBox.style.width = 300;
            curvePlot = new VisualElement { pickingMode = PickingMode.Ignore };
            curvePlot.style.height = 92;
            curvePlot.generateVisualContent += DrawCurve;
            var clip2 = new VisualElement { pickingMode = PickingMode.Ignore };
            clip2.style.overflow = Overflow.Hidden;
            clip2.Add(curvePlot);
            curveBox.Add(clip2);
            curveLabel = Ui.L(curveBox, "", "t-label", "f-mono");
            Ui.L(curveBox, "Prescribed fallback; no photon travel delays", "t-label").style.whiteSpace = WhiteSpace.Normal;

            spotBox = Ui.Panel(col, "Hot spot", "Light curve");
            spotBox.pickingMode = PickingMode.Ignore;
            spotBox.style.marginLeft = 10; spotBox.style.width = 300;
            spotPlot = new VisualElement { pickingMode = PickingMode.Ignore };
            spotPlot.style.height = 80;
            spotPlot.generateVisualContent += DrawSpot;
            var clip3 = new VisualElement { pickingMode = PickingMode.Ignore };
            clip3.style.overflow = Overflow.Hidden;
            clip3.Add(spotPlot);
            spotBox.Add(clip3);
            spotLabel = Ui.L(spotBox, "", "t-label", "f-mono");
            spotLabel.style.whiteSpace = WhiteSpace.Normal;
            Ui.L(spotBox, "Snapshot time; no photon travel delays", "t-label").style.whiteSpace = WhiteSpace.Normal;
        }

        VisualElement spotBox, spotPlot;
        float[] spotSorted = new float[400];
        Label spotLabel;

        // Total light from the spot per frame, normalised to its own peak: one beaming peak per orbit, weaker lensed bumps.
        void DrawSpot(MeshGenerationContext mgc)
        {
            var rect = spotPlot.contentRect;
            float w = rect.width, h = rect.height;
            if (spotCurve.Count < 2 || w < 10) return;
            float max = 1e-9f;
            foreach (var v in spotCurve) max = Mathf.Max(max, v);
            var pt = mgc.painter2D;
            pt.strokeColor = CPhoton; pt.lineWidth = 1.5f;
            pt.BeginPath();
            for (int i = 0; i < spotCurve.Count; i++)
            {
                var q = new Vector2(i / (float)(spotCurve.Count - 1) * w, h - 2 - spotCurve[i] / max * (h - 4));
                if (i == 0) pt.MoveTo(q); else pt.LineTo(q);
            }
            pt.Stroke();
        }

        static void Key(VisualElement parent, Color c, string text)
        {
            var k = Ui.E(parent, "row");
            k.style.marginRight = 10; k.style.alignItems = Align.Center;
            var sw = Ui.E(k);
            sw.style.width = 10; sw.style.height = 2; sw.style.backgroundColor = c; sw.style.marginRight = 4;
            Ui.L(k, text, "t-label");
        }

        public override void UpdateHud(UiShell shell)
        {
            if (mapPlot == null) return;
            double want = 30;
            foreach (var p in probes) if (p.g.alive || p.trail.Count > 0) want = Math.Max(want, Math.Min(p.g.r, 300) * 1.15);
            foreach (var b in bodies) if (b.state == 0) want = Math.Max(want, Math.Min(b.g.r, 300) * 1.1);
            // debris: frame the inner half of it (median r of every 8th particle, doubled)
            hudRs.Clear();
            for (int i = 0; i < nParts; i += 8) if (parts[i].alive) hudRs.Add(parts[i].r);
            if (hudRs.Count > 0) { hudRs.Sort(); want = Math.Max(want, Math.Min(hudRs[hudRs.Count / 2] * 2, 8000)); }
            mapR = want > mapR * 1.5 || want < mapR / 1.5 ? want : mapR + (want - mapR) * 0.08;
            Ui.Text(mapScale, $"edge {Fmt.Num(mapR / 2, 0)} rs · flat, not lensed");
            mapPlot.MarkDirtyRepaint();
            Ui.Show(spotBox, hotSpot && spotCurve.Count > 1);
            if (hotSpot && spotCurve.Count > 1)
            {
                // 95th over 5th percentile: single readback glitches don't set it; a dip of ~0 means the spot was hidden
                if (spotSorted.Length < spotCurve.Count) spotSorted = new float[400];
                spotCurve.CopyTo(spotSorted);
                System.Array.Sort(spotSorted, 0, spotCurve.Count);
                float hi = spotSorted[(int)(0.95f * (spotCurve.Count - 1))], lo = spotSorted[(int)(0.05f * (spotCurve.Count - 1))];
                Ui.Text(spotLabel, lo > 1e-3f * hi ? $"peak / dip ×{hi / lo:0.0}  (beaming, once per orbit)" : "peak once per orbit (beaming); hidden behind the hole or disk part of the time");
                spotPlot.MarkDirtyRepaint();
            }
            bool curve = lightCurve.Count > 1 && lum > 1e-4;   // not an empty plot while the debris is still out (audit #57)
            Ui.Show(curveBox, curve);
            if (curve)
            {
                Ui.Text(curveLabel, $"now {Fmt.Num(lum, 3)} × Eddington   dashed: t^-5/3");
                curvePlot.MarkDirtyRepaint();
            }
        }

        void DrawMap(MeshGenerationContext mgc)
        {
            var rect = mapPlot.contentRect;
            float w = rect.width, h = rect.height;
            if (w < 10 || h < 10) return;
            var c = new Vector2(w / 2, h / 2);
            float s = (float)(Mathf.Min(w, h) / 2 / mapR);
            double a = A;
            Vector2 P(double r, double ph) => c + new Vector2((float)(r * Math.Cos(ph)), -(float)(r * Math.Sin(ph))) * s;
            var pt = mgc.painter2D;

            void Circle(double r, Color col, float lw, bool fill = false)
            {
                if (r * s < 0.5f || r > mapR * 1.5) return;
                pt.BeginPath();
                pt.Arc(c, (float)r * s, 0, 360);
                pt.ClosePath();
                if (fill) { pt.fillColor = col; pt.Fill(); }
                else { pt.strokeColor = col; pt.lineWidth = lw; pt.Stroke(); }
            }

            // debris as small squares, one path (at most ~2500 of them)
            int nDots = 0;
            for (int i = 0; i < nParts; i++) if (parts[i].alive && parts[i].r < mapR * 1.4) nDots++;
            int stride = Math.Max(1, nDots / 2500);
            // one square per 2-pixel cell: overlapping squares in one path make the tessellator blow up
            var cells = hudCells; cells.Clear();
            for (int phase = 0; phase < 3 && nDots > 0; phase++)
            {
                pt.fillColor = phase == 2 ? new Color(1f, 0.55f, 0.25f, 0.95f) : phase == 1 ? new Color(0.55f, 0.65f, 1f, 0.8f) : new Color(1f, 0.88f, 0.65f, 0.9f);
                pt.BeginPath();
                int seen = 0, inPath = 0;
                for (int i = 0; i < nParts; i++)
                {
                    ref var p = ref parts[i];
                    if (!p.alive || p.r >= mapR * 1.4) continue;
                    if (seen++ % stride != 0) continue;
                    int ph = pPhase[i] == 2 ? 2 : p.E >= 1 ? 1 : 0;
                    if (ph != phase) continue;
                    var q = P(p.r, p.phi);
                    if (!cells.Add(((int)(q.x * 0.5f) << 16) ^ (int)(q.y * 0.5f) ^ (phase << 30))) continue;
                    const float d = 1f;
                    pt.MoveTo(q + new Vector2(-d, -d)); pt.LineTo(q + new Vector2(d, -d)); pt.LineTo(q + new Vector2(d, d)); pt.LineTo(q + new Vector2(-d, d)); pt.ClosePath();
                    // a Fill() is capped at 65,535 vertices, so fill in batches
                    if (++inPath >= 200) { pt.Fill(); pt.BeginPath(); inPath = 0; }
                }
                if (inPath > 0) pt.Fill();
            }

            // reference rings
            Circle(Kerr.Horizon(a), Color.black, 0, true);
            Circle(Kerr.Horizon(a), CHorizon, 1.5f);
            if (a > 0.001) Circle(2, CErgo, 1f);
            Circle(Kerr.PhotonOrbit(a), CPhoton, 1.2f);
            if (a > 0.001) Circle(Kerr.PhotonOrbit(a, false), new Color(CPhoton.r, CPhoton.g, CPhoton.b, 0.4f), 1f);
            Circle(Kerr.MarginallyBound(a), CMb, 1f);
            Circle(Kerr.Isco(a), CIsco, 1.2f);
            if (a > 0.001) Circle(Kerr.Isco(a, false), new Color(CIsco.r, CIsco.g, CIsco.b, 0.4f), 1f);
            for (int i = bodies.Count - 1; i >= 0; i--)
                if (!bodies[i].spec.cloud) { Circle(bodies[i].rt, CTidal, 1.2f); break; }

            // probe trails
            foreach (var p in probes)
            {
                if (p.trail.Count < 2) continue;
                pt.strokeColor = p.photon ? CLight : CTrail;
                pt.lineWidth = 1.4f;
                pt.BeginPath();
                bool started = false;
                foreach (var q in p.trail)
                {
                    if (q.sqrMagnitude > mapR * mapR * 2.5) { started = false; continue; }
                    var m = c + new Vector2(q.x, -q.y) * s;
                    if (!started) { pt.MoveTo(m); started = true; } else pt.LineTo(m);
                }
                pt.Stroke();
                if (p.g.alive && p.g.r < mapR * 1.4) Dot(pt, P(p.g.r, p.g.phi), 3f, p.photon ? CLight : CTrail);
            }
            foreach (var b in bodies)
                if (b.state == 0 && b.g.r < mapR * 1.4) Dot(pt, P(b.g.r, b.g.phi), Mathf.Max(3f, (float)(b.Rm * s * Math.Sqrt(b.stretch))), Color.white);
        }

        static void Dot(Painter2D pt, Vector2 at, float r, Color col)
        {
            pt.BeginPath();
            pt.Arc(at, r, 0, 360);
            pt.ClosePath();
            pt.fillColor = col;
            pt.Fill();
        }

        void DrawCurve(MeshGenerationContext mgc)
        {
            var rect = curvePlot.contentRect;
            float w = rect.width, h = rect.height;
            if (lightCurve.Count < 2 || w < 10) return;
            var pt = mgc.painter2D;
            float t0 = lightCurve[0].x, t1 = Mathf.Max(lightCurve[lightCurve.Count - 1].x, t0 + 1);
            float lmax = -30, lmin = 30;
            int peakI = 0;
            for (int i = 0; i < lightCurve.Count; i++)
            {
                float l = Mathf.Log10(Mathf.Max(lightCurve[i].y, 1e-12f));
                if (l > lmax) { lmax = l; peakI = i; }
                lmin = Mathf.Min(lmin, l);
            }
            lmin = Mathf.Max(lmin, lmax - 6);
            if (lmax - lmin < 1) lmin = lmax - 1;
            Vector2 P(float t, float l) => new Vector2((t - t0) / (t1 - t0) * w, h - (Mathf.Clamp(l, lmin, lmax) - lmin) / (lmax - lmin) * (h - 4) - 2);
            // Eddington line, if in range
            if (0 > lmin && 0 < lmax)
            {
                pt.strokeColor = new Color(1, 1, 1, 0.25f); pt.lineWidth = 1;
                pt.BeginPath(); pt.MoveTo(P(t0, 0)); pt.LineTo(P(t1, 0)); pt.Stroke();
            }
            pt.strokeColor = CIsco; pt.lineWidth = 1.6f;
            pt.BeginPath();
            for (int i = 0; i < lightCurve.Count; i++)
            {
                var q = P(lightCurve[i].x, Mathf.Log10(Mathf.Max(lightCurve[i].y, 1e-12f)));
                if (i == 0) pt.MoveTo(q); else pt.LineTo(q);
            }
            pt.Stroke();
            // the classic fallback law from the peak on: L ∝ t^(−5/3), t counted from the disruption
            double td = -1;
            foreach (var b in bodies) if (b.tDisrupt >= 0) td = Math.Max(td, b.tDisrupt);
            float tp = lightCurve[peakI].x;
            if (td >= 0 && tp > td && tp < t1)
            {
                pt.strokeColor = new Color(1, 1, 1, 0.55f); pt.lineWidth = 1;
                for (float t = tp; t < t1; t += (t1 - tp) / 24)
                {
                    float tb = Mathf.Min(t + (t1 - tp) / 48, t1);
                    float l0 = lmax - 5f / 3 * Mathf.Log10((float)((t - td) / (tp - td)));
                    float l1 = lmax - 5f / 3 * Mathf.Log10((float)((tb - td) / (tp - td)));
                    pt.BeginPath(); pt.MoveTo(P(t, l0)); pt.LineTo(P(tb, l1)); pt.Stroke();
                }
            }
        }
    }
}
