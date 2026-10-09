using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;
using HS = FogOfWar.OpenWorld.Career.HitSystem;

namespace FogOfWar
{
    // The dev menu (OpenSystemScene.Dev.cs) in both shells: a panel on the UI Toolkit HUD, and a section at the top of the
    // classic panel (-fowlegacyui, or the Classic panel setting). One list of entries feeds both. Its buttons take clicks
    // only, as the other panels' do: the flight keys stay with flight.
    public partial class OpenSystemScene
    {
        sealed class DevEntry
        {
            public string Group, Name;
            public Func<string> Why;  // why it can't run now; null when it can
            public Func<bool> On;     // a toggle's state
            public Action Run;
        }

        List<DevEntry> devEntries;
        List<DevEntry> DevEntries() => devEntries ??= new List<DevEntry>
        {
            new DevEntry { Group = "Career", Name = "Test money", On = () => devMoney, Why = () => SandboxWhy, Run = () => DevSetMoney(!devMoney) },
            new DevEntry { Group = "Career", Name = "Reveal traffic", On = () => Signature.RevealAll, Run = () => DevReveal(!Signature.RevealAll) },
            new DevEntry { Group = "Career", Name = "Seed combat", On = () => devSeeded, Run = () => DevSeedCombat(!devSeeded) },
            new DevEntry { Group = "Raider (at rest with the ship)", Name = "300 km", Why = () => ChaseWhy, Run = () => DevChase(300) },
            new DevEntry { Group = "Raider (at rest with the ship)", Name = "2,000 km", Why = () => ChaseWhy, Run = () => DevChase(2000) },
            new DevEntry { Group = "Raider (at rest with the ship)", Name = "10,000 km", Why = () => ChaseWhy, Run = () => DevChase(10000) },
            new DevEntry { Group = "Raider (at rest with the ship)", Name = "Fire salvo", Why = () => SalvoWhy, Run = () => DevSalvo() },
            new DevEntry { Group = "Raider (at rest with the ship)", Name = "Refill magazine", Why = () => RefillWhy, Run = () => DevRefill() },
            new DevEntry { Group = "Damage", Name = "Hit drive", Why = () => HitWhy(HS.Drive), Run = () => DevHit(HS.Drive) },
            new DevEntry { Group = "Damage", Name = "Hit sensors", Why = () => HitWhy(HS.Sensors), Run = () => DevHit(HS.Sensors) },
            new DevEntry { Group = "Damage", Name = "Hit tank", Why = () => HitWhy(HS.Tank), Run = () => DevHit(HS.Tank) },
            new DevEntry { Group = "Damage", Name = "Jam radiators", Why = () => HitWhy(HS.Radiators), Run = () => DevHit(HS.Radiators) },
            new DevEntry { Group = "Damage", Name = "Kill a PDC", Why = () => HitWhy(HS.Pdc), Run = () => DevHit(HS.Pdc) },
            new DevEntry { Group = "Damage", Name = "Free repair", Why = () => RepairWhy, Run = () => DevRepair() },
            new DevEntry { Group = "Water and heat", Name = "Fill tank", Why = () => WaterWhy, Run = () => DevWater() },
            new DevEntry { Group = "Water and heat", Name = "Empty sink", Why = () => SinkWhy, Run = () => DevSink(false) },
            new DevEntry { Group = "Water and heat", Name = "Fill sink", Why = () => SinkWhy, Run = () => DevSink(true) },
            new DevEntry { Group = "Time (warp to a stop)", Name = "+1 h", Why = () => SkipWhy, Run = () => DevSkip(1) },
            new DevEntry { Group = "Time (warp to a stop)", Name = "+1 day", Why = () => SkipWhy, Run = () => DevSkip(24) },
        };

        static string DevLabel(DevEntry e, string why) => (e.On?.Invoke() == true ? "● " : "") + e.Name + (why != null ? " (" + why + ")" : "");

        void DevRun(DevEntry e) { if (e.Why?.Invoke() == null) e.Run(); }

        // ------------------------------------------------------------ UI Toolkit

        VisualElement devBox;
        Label devStatusText;
        readonly List<(DevEntry e, Label b)> devBtns = new List<(DevEntry, Label)>();

        void BuildDevMenu(VisualElement layer, bool portrait)
        {
            devBtns.Clear();
            devBox = Ui.Panel(layer);
            devBox.style.position = Position.Absolute;
            devBox.style.left = 20; devBox.style.top = portrait ? 300 : 190; devBox.style.width = 380;
            devBox.pickingMode = PickingMode.Position; // clicks stop here, not at the flight camera
            var head = Ui.E(devBox, "row");
            head.style.alignItems = Align.Center;
            Ui.L(head, "DEV MENU", "t-h3", "f-cond", "glow-ink");
            Ui.E(head).style.flexGrow = 1;
            Ui.B(head, "Close  F7", ToggleDevMenu, "small", "ghost");
            devStatusText = Ui.L(devBox, "", "t-note", "f-mono");
            devStatusText.style.whiteSpace = WhiteSpace.Normal;
            string group = null;
            VisualElement row = null;
            foreach (var e in DevEntries())
            {
                if (e.Group != group) { group = e.Group; Ui.Section(devBox, group); row = Ui.E(devBox, "row", "wrap"); }
                var entry = e;
                devBtns.Add((e, Ui.B(row, e.Name, () => DevRun(entry), "small")));
            }
            Ui.Show(devBox, devMenu);
        }

        void UpdateDevMenu()
        {
            if (devBox == null) return;
            Ui.Show(devBox, devMenu);
            if (!devMenu) return;
            Ui.Text(devStatusText, DevStatus());
            foreach (var (e, b) in devBtns)
            {
                string why = e.Why?.Invoke();
                Ui.Text(b, DevLabel(e, why));
                Ui.SetClass(b, "disabled", why != null);
                Ui.SetClass(b, "on", e.On?.Invoke() == true);
            }
        }

        // QA: the Toolkit panel as laid out (display, size) and its buttons' labels
        internal string TestDevMenuReport() => devBox == null ? "no panel" :
            $"{devBox.resolvedStyle.display} {devBox.layout.width:0}×{devBox.layout.height:0} at {devBox.layout.x:0},{devBox.layout.y:0}: " + string.Join(" / ", devBtns.ConvertAll(x => x.b.text));

        // ------------------------------------------------------------ classic panel

        int devClassicFrame = -1; // QA: the frame the classic panel last painted the menu
        int devClassicClick = -1; // QA: entry i clicked as its button would be, at the next repaint

        void DevPanel(PanelUI ui)
        {
            if (!devMenu) return;
            ui.Section("Dev menu (F7)");
            GUILayout.Label(DevStatus(), ui.note);
            var all = DevEntries();
            for (int i = 0; i < all.Count;)
            {
                string group = all[i].Group;
                int j = i;
                while (j < all.Count && all[j].Group == group) j++;
                GUILayout.Label(group, ui.label);
                var labels = new string[j - i];
                for (int k = i; k < j; k++) labels[k - i] = DevLabel(all[k], all[k].Why?.Invoke());
                int hit = ui.Buttons(labels);
                // a repaint comes after the layout, so a change here does not unbalance this event's controls
                if (devClassicClick >= i && devClassicClick < j && Event.current.type == EventType.Repaint) { hit = devClassicClick - i; devClassicClick = -1; }
                if (hit >= 0) DevRun(all[i + hit]);
                i = j;
            }
            if (ui.Button("Close (F7)")) ToggleDevMenu();
            if (Event.current.type == EventType.Repaint) devClassicFrame = Time.frameCount;
        }

        internal int TestDevClassicFrame => devClassicFrame;

        // QA: the entry's button clicked in the shell that shows the menu: a ClickEvent sent to the HUD's button, or the
        // classic panel's at its next repaint. False with the menu closed
        internal bool TestClickDev(string name)
        {
            int i = DevEntries().FindIndex(e => e.Name == name);
            if (!devMenu || i < 0) return false;
            if (!(App.ShellActive && HasHud)) { devClassicClick = i; return true; }
            if (devBox == null) return false;
            using (var ev = ClickEvent.GetPooled())
            {
                ev.target = devBtns[i].b;
                devBtns[i].b.SendEvent(ev);
            }
            return true;
        }

        internal bool TestDevMenuFits => devBox != null && devBox.layout.yMax <= devBox.parent.layout.height && devBox.layout.xMax <= devBox.parent.layout.width;
    }
}
