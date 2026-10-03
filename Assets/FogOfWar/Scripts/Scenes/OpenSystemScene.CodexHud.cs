using System;
using FogOfWar.OpenWorld;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FogOfWar
{
    // The Codex tab of the ship console: the target's entry (data once visited, survey notes once scanned, scan
    // progress) and the achievement list. Same rules as CodexPanel in OpenSystemScene.Career.cs.
    public partial class OpenSystemScene
    {
        VisualElement codexBox;
        string codexSig;
        float codexT;
        int codexView; // 0 target entry, 1 achievements

        Segmented codexSeg;
        public void ShowCodex(int view) { codexView = view; if (codexSeg != null) codexSeg.Value = view; codexSig = null; codexT = 0; } // review shots

        void CodexTab(VisualElement c)
        {
            codexSeg = new Segmented(new[] { "Target entry", "Achievements" }, codexView, i => { codexView = i; codexSig = null; codexT = 0; });
            c.Add(codexSeg);
            codexBox = Ui.E(c, "col");
            codexSig = null;
        }

        void UpdateCodex()
        {
            if (codexBox == null || career == null) return;
            if ((codexT -= Time.unscaledDeltaTime) > 0) return;
            codexT = 0.4f;
            var cx = career.Codex;
            string sig = codexView + "|" + ship.Target?.Name + "|" + cx.Visited.Count + "|" + cx.Scanned.Count + "|" + cx.Achieved.Count + "|" +
                         cx.ScanTarget + Math.Round(cx.ScanProgress / Math.Max(cx.ScanSeconds, 1) * 100) + "|" + cx.SensorLevel + "|" + career.Sandbox;
            if (sig == codexSig) return;
            codexSig = sig;
            codexBox.Clear();
            if (codexView == 0) BuildCodexEntry(codexBox); else BuildFeats(codexBox);
            Ui.Fontify(codexBox);
        }

        void BuildCodexEntry(VisualElement c)
        {
            var cx = career.Codex;
            var stats = Ui.E(c, "row");
            stats.style.marginTop = 4;
            Stat(stats, cx.Visited.Count, "visited");
            Stat(stats, cx.Scanned.Count, "scanned");
            Stat(stats, sys.Bodies.Count - 1, "bodies");
            var b = ship.Target;
            if (b == null || b.Parent == null || Career.IsCraft(b))
            {
                Ui.L(c, "Target a planet, moon or asteroid to read its entry.", "t-note").style.marginTop = 8;
                return;
            }
            bool seen = cx.Visited.Contains(b.Name), scanned = cx.Scanned.Contains(b.Name);
            var head = Ui.E(c, "row");
            head.style.marginTop = 10;
            Ui.L(head, b.Name.ToUpperInvariant(), "t-h2", "f-cond", "accent", "glow").style.flexGrow = 1;
            Ui.L(head, scanned ? "SCANNED" : seen ? "VISITED" : "NOT VISITED", "chip", "f-condm", scanned ? "ok" : seen ? "cold" : "bad");
            Ui.L(c, Kind(b).ToUpperInvariant(), "t-label", "f-condm");
            if (seen || scanned)
            {
                Ui.Section(c, "Data");
                foreach (var line in Codex.Data(sys, b, jd)) Ui.L(c, line, "t-body").style.marginBottom = 3;
            }
            else Ui.L(c, $"Pass within {Km(Codex.VisitRange(b))} to log its data.", "t-note").style.marginTop = 8;

            if (scanned)
            {
                Ui.Section(c, "Survey notes");
                foreach (var f in Codex.Facts(b.Name))
                {
                    var r = Ui.E(c, "row");
                    r.style.alignItems = Align.FlexStart; r.style.marginBottom = 5;
                    Ui.L(r, "◆", "t-label", "accent").style.marginRight = 8;
                    Ui.L(r, f, "t-body").style.flexShrink = 1;
                }
                return;
            }
            Ui.Section(c, "Scan");
            string sale = career.Sandbox ? "" : $" First data sells for {Codex.DataValue(sys, b, jd):N0} cr.";
            if (cx.ScanTarget == b.Name && cx.ScanProgress > 0)
            {
                float f = (float)(cx.ScanProgress / cx.ScanSeconds);
                var bar = Ui.E(c, "thr");
                var fill = Ui.E(bar, "fill");
                fill.style.width = Length.Percent(Mathf.Clamp01(f) * 100);
                fill.style.backgroundColor = new Color(0.41f, 0.82f, 1f, 0.85f);
                Ui.L(c, $"Scanning: {Fmt.Num(100 * f, 0)} %. Keep the drive off within {Km(cx.ScanRange(b))}.{sale}", "t-body", "cold");
            }
            else Ui.L(c, $"Stay within {Km(cx.ScanRange(b))} with the drive off for {Dur(cx.ScanSeconds)}.{sale}", "t-note");
            if (cx.SensorLevel == 0) Ui.L(c, "Upgraded sensors at a port double the range and halve the time.", "t-note").style.marginTop = 4;
        }

        static void Stat(VisualElement row, int n, string what)
        {
            var col = Ui.E(row, "col");
            col.style.marginRight = 22;
            Ui.L(col, n.ToString("N0"), "t-h2", "f-mono");
            Ui.L(col, what.ToUpperInvariant(), "t-micro", "f-condm");
        }

        void BuildFeats(VisualElement c)
        {
            var cx = career.Codex;
            Ui.L(c, $"{cx.Achieved.Count} OF {Codex.Feats.Length} EARNED", "t-h3", "f-cond", "accent").style.marginTop = 6;
            var bar = Ui.E(c, "thr");
            Ui.E(bar, "fill").style.width = Length.Percent(100f * cx.Achieved.Count / Codex.Feats.Length);
            foreach (var f in Codex.Feats)
            {
                bool has = cx.Has(f.Id);
                var card = Ui.E(c, "row", "feat");
                Ui.SetClass(card, "got", has);
                Ui.L(card, has ? "✓" : "·", "feat-mark", "f-mono");
                var col = Ui.E(card, "col");
                col.style.flexShrink = 1;
                Ui.L(col, f.Title.ToUpperInvariant(), "t-h3", "f-cond", has ? "accent" : "dim");
                Ui.L(col, f.How, "t-note");
            }
        }
    }
}
