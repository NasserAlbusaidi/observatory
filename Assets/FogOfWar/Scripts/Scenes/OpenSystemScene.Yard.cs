using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FogOfWar
{
    // The yard page, docked (combat design, Phase 1): pick a part in each slot to make a draft build, read what it does
    // next to the fitted build, then refit to it in one go. Drawn in both the classic panel and the UI Toolkit shell.
    public partial class OpenSystemScene
    {
        ShipBuild yardDraft; // the parts picked, not yet fitted; null when not docked
        Port yardPort;

        ShipBuild Draft()
        {
            if (career.Docked != yardPort) { yardDraft = null; yardPort = career.Docked; }
            if (career.Docked == null || career.Sandbox) return yardDraft = null;
            return yardDraft ??= career.Build.Clone();
        }

        string PartLabel(Slot s, int i)
        {
            var m = ShipBuild.Catalog[(int)s][i];
            string what = s == Slot.Drive ? $"{m.Name} {m.Size:0.0} MN, {m.Mass:0.#} t" + (m.Size > ShipBuild.Rated(3) ? " (juice: fictional)" : "")
                : s == Slot.Sensors ? m.Name + (m.Size > 0 ? ": ×2 scan and contact range, ½ scan time" : "")
                : s == Slot.Radiators ? m.Name + (m.Size < 1 ? $", {m.Mass:0.#} t: stowed after a burn, cold at {Km(Signature.RangeKm(0, Signature.StockKm, Signature.ColdW(m.Size)))}" : ": always out")
                : m.Name + (m.Mass > 0 ? $", {m.Mass:0.#} t" : "");
            return what + (career.Build.pick[(int)s] == i ? " (fitted)" : "");
        }

        // (what, now, with the draft)
        static List<(string key, string now, string after)> YardReadouts(ShipBuild now, ShipBuild after)
        {
            var list = new List<(string, string, string)>();
            void Add(string key, Func<ShipBuild, string> f) => list.Add((key, f(now), f(after)));
            Add("Dry mass", b => $"{b.DryMass:N1} t");
            Add("Full tank, empty hold", b => $"{b.GFull:0.00} g");
            Add("Empty tank", b => $"{b.GEmpty:0.00} g");
            Add("Speed change, full tank", b => $"{b.DvFull:N0} km/s");
            Add("Jet power, full burn", b => $"{b.JetPowerTW:0.0} TW");
            Add("Seen at, full burn · cold", b => $"{Km(Signature.RangeKm(b.JetPowerTW * 1e12))} · {Km(Signature.RangeKm(0, Signature.StockKm, Signature.ColdW(b.StowRho)))}" + (b.Stowable ? " stowed" : ""));
            if (now.Stowable || after.Stowable)
                Add("Dark, full tank", b => b.Stowable ? $"{b.TankCapacity * Career.SinkSecondsPerT / 3600:0.0} h; tracked {Signature.HoldText(Signature.TrackHoldS * b.StowRho)} after a burn" : "none: fixed radiators");
            Add("Sees a stock burn at", b => Km(Signature.RangeKm(Signature.StockBurnW, Signature.SensorKm(b.SensorLevel))));
            Add("Sees a cold hull at", b => Km(Signature.RangeKm(0, Signature.SensorKm(b.SensorLevel))));
            Add("Tank · hold · seats", b => $"{b.TankCapacity:N0} t · {b.HoldTonnes:N0} t · {b.Seats}");
            return list;
        }

        string YardTripLine(ShipBuild b)
        {
            if (!career.YardTrip(b, out string to, out double s, out double t)) return null;
            return $"{career.Docked.Name} → {to}, full burn: {Days(s / 86400)}, {t:N0} t of reaction mass" +
                   (t > b.TankCapacity ? $": more than the {b.TankCapacity:N0} t tank holds" : "");
        }

        string YardPriceLine(ShipBuild d, out bool can)
        {
            can = career.CanRefitTo(d, out string why, out double cost, out double days);
            if (why == "fitted") return "Pick parts above to see a refit.";
            string price = cost >= 0 ? $"{cost:N0} cr" : $"{-cost:N0} cr back";
            return $"Refit: {price}, {Days(days)} in the yard" + (can ? "" : $": {why}");
        }

        void Fit(ShipBuild d)
        {
            if (career.RefitTo(ship, d)) yardDraft = null;
        }

        // ---------------------------------------------------------------- classic panel

        void YardPanel(PanelUI ui)
        {
            var d = Draft();
            if (d == null) return;
            ui.Section("Yard");
            if (Event.current.type == EventType.Repaint) { classicYardY = GUILayoutUtility.GetLastRect().y; classicYardFrame = Time.frameCount; }
            if (YardLine() is string yard) GUILayout.Label(yard, ui.label);
            foreach (Slot s in Enum.GetValues(typeof(Slot)))
            {
                var parts = ShipBuild.Catalog[(int)s];
                var labels = new string[parts.Length];
                for (int i = 0; i < parts.Length; i++) labels[i] = (d.pick[(int)s] == i ? "● " : "") + PartLabel(s, i);
                GUILayout.Label(s.ToString(), ui.note);
                int k = ui.Buttons(labels);
                if (k >= 0) d.pick[(int)s] = k;
            }
            bool same = d.Same(career.Build);
            foreach (var (key, now, after) in YardReadouts(career.Build, d))
                GUILayout.Label($"{key}: " + (same || now == after ? now : $"{now} → {after}"), ui.note);
            if (YardTripLine(d) is string trip) GUILayout.Label("Trip check: " + trip, ui.note);
            string price = YardPriceLine(d, out bool can);
            if (same) GUILayout.Label(price, ui.note);
            else
            {
                int b = ui.Buttons(can ? price : price + " (can't)", "Reset");
                if (b == 0 && can) Fit(d);
                if (b == 1) yardDraft = null;
            }
        }

        // ---------------------------------------------------------------- shell

        Label yardHead;
        // review shots: where the classic panel drew the yard's head (scroll content space) and in which frame
        float classicYardY = -1;
        int classicYardFrame = -1;

        void BuildYard(VisualElement c)
        {
            var d = Draft();
            if (d == null) return;
            yardHead = Ui.Section(c, "Yard");
            if (YardLine() is string yard) Ui.L(c, yard, "t-body", "warn");
            foreach (Slot s in Enum.GetValues(typeof(Slot)))
            {
                var parts = ShipBuild.Catalog[(int)s];
                var labels = new string[parts.Length];
                for (int i = 0; i < parts.Length; i++) labels[i] = PartLabel(s, i);
                Ui.L(c, s.ToString(), "t-label").style.marginTop = 4;
                var slot = s;
                c.Add(new Segmented(labels, d.pick[(int)s], k => { d.pick[(int)slot] = k; jobsT = 0; }, wrap: true));
            }
            bool same = d.Same(career.Build);
            foreach (var (key, now, after) in YardReadouts(career.Build, d))
            {
                var (_, v) = Ui.KV(c, key, same || now == after ? now : $"{now} → {after}");
                if (!same && now != after) v.AddToClassList("accent");
            }
            if (YardTripLine(d) is string trip) Ui.L(c, "Trip check: " + trip, "t-note").style.marginTop = 4;
            string price = YardPriceLine(d, out bool can);
            if (same) { Ui.L(c, price, "t-note"); return; }
            var row = Ui.E(c, "row", "wrap");
            row.style.marginTop = 6;
            Ui.B(row, price, () => { if (can) Fit(d); jobsT = 0; }, can ? "primary" : "disabled");
            Ui.B(row, "Reset", () => { yardDraft = null; jobsT = 0; });
        }

        // review shots: a courier draft (torch drive, 100 t tank) on the Jobs page; false drops it
        public void TestYardDraft(bool on)
        {
            yardDraft = null; jobsT = 0;
            if (!on || Draft() is not ShipBuild d) return;
            d.pick[(int)Slot.Drive] = 3; d.pick[(int)Slot.Tank] = 0;
        }
        public void TestYardScroll() { if (yardHead != null && pageJobs is ScrollView sv) sv.scrollOffset = new Vector2(0, yardHead.worldBound.y - sv.contentContainer.worldBound.y); }
        public string TestYardReport() => yardDraft == null ? "no draft (not docked?)" : YardReportText() + $" | shown {yardHead?.panel != null}";
        // the classic panel: drawn in this frame or the one before, and the head's y in the panel's scroll content
        public string TestClassicYardReport(out float headY)
        {
            headY = classicYardY;
            return yardDraft == null ? "no draft (not docked?)" : YardReportText() + $" | drawn {Time.frameCount - classicYardFrame <= 1}";
        }
        string YardReportText()
        {
            var parts = new List<string>();
            foreach (var (key, now, after) in YardReadouts(career.Build, yardDraft)) parts.Add($"{key} {now} -> {after}");
            return string.Join("; ", parts) + " | " + YardTripLine(yardDraft) + " | " + YardPriceLine(yardDraft, out _);
        }
    }
}
