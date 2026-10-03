using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;
using A = FogOfWar.Controls.Action;
using Ctl = FogOfWar.Controls;

namespace FogOfWar
{
    // Open System: the trucker career (contracts, reaction mass, ports, upgrades). The rules live in Career; this file
    // wires them to the ship and draws a plain panel section that the HUD rework can restyle.
    public partial class OpenSystemScene
    {
        static readonly double[] CruiseFracs = { 1, 0.5, 0.25 };
        static readonly string[] CruiseNames = { "Full burn", "Coast at 50 %", "Coast at 25 %" };

        public Career Career => career; // read by the HUD
        Career career;
        int cruiseIdx;
        bool planWasActive;
        Body planTarget;

        void InitCareer()
        {
            career = new Career { Sandbox = Array.IndexOf(Environment.GetCommandLineArgs(), "-fowqa") >= 0, Sys = sys };
            career.StartNew(jd);
            ship.Fuel = career.TankCapacity;
            career.Apply(ship);
        }

        void TickCareer(double simDt)
        {
            // a plan starts (or restarts on a new target in the same frame): fix its cruise speed from the distance to go
            if (ship.PlanActive && (!planWasActive || ship.Target != planTarget) && ship.Plan == Plan.Brachistochrone)
            {
                double f = CruiseFracs[cruiseIdx];
                double d = Math.Max((sys.Pos(ship.Target, jd) - ship.Pos).Mag - Ship.Standoff(ship.Target), 0);
                ship.CruiseCap = f < 1 ? f * Math.Sqrt(ship.AMax * d) : 0;
            }
            if (!ship.PlanActive) ship.CruiseCap = 0;
            planWasActive = ship.PlanActive;
            planTarget = ship.Target;
            career.Tick(sys, ship, jd, simDt);
        }

        void CareerReadouts(List<Readout> list)
        {
            if (career.Sandbox) { list.Add(new Readout("Mode", "Sandbox: unlimited reaction mass")); return; }
            list.Add(new Readout("Credits", career.Credits.ToString("N0") + " cr", career.Credits < 0 ? Tone.Bad : Tone.Normal));
            list.Add(new Readout("Reaction mass", $"{ship.Fuel:N1} of {career.TankCapacity:N0} t (Δv left {Career.DvLeft(ship):N0} km/s)",
                ship.Fuel < career.TankCapacity * 0.15 ? Tone.Bad : Tone.Normal));
            list.Add(new Readout("Ship mass", $"{ship.Mass:N0} t ({career.CargoTonnes:N1} t in the hold)"));
            list.Add(new Readout("Docked", career.Docked != null ? career.Docked.Name : "no", career.Docked != null ? Tone.Cold : Tone.Normal));
        }

        // ---------------------------------------------------------------- panel

        void CareerPanel(PanelUI ui)
        {
            ui.Section("Ship's business");
            if (career.Sandbox)
            {
                GUILayout.Label("Sandbox: unlimited reaction mass, no contracts.", ui.label);
                career.Sandbox = ui.Toggle("Sandbox", career.Sandbox);
                CodexPanel(ui);
                return;
            }
            GUILayout.Label($"{career.Credits:N0} cr   ·   reaction mass {ship.Fuel:N1} / {career.TankCapacity:N0} t   ·   hold {career.CargoTonnes:N1} / {Career.HoldTonnes:N0} t", ui.label);
            GUILayout.Label($"Drive rated {career.DriveRating:0} g" + (career.CarryingPassengers ? ": passengers aboard, held to 1 g" : "") +
                            $"   ·   Δv left {Career.DvLeft(ship):N0} km/s", ui.note);
            if (ship.Fuel < 1)
            {
                GUILayout.Label("The tank is dry. You are adrift.", ui.label);
                if (career.Docked == null && ui.Button($"Call an emergency tender ({Career.EmergencyTonnes:N0} t, {Career.EmergencyCost:N0} cr)")) career.EmergencyDrop(ship);
            }

            TutorialHint(ui);
            ActiveJobs(ui);

            var port = career.Docked;
            if (port != null)
            {
                ui.Section("Docked at " + port.Name);
                GUILayout.Label($"Reaction mass {port.FuelPrice:N0} cr/t. {port.Note}", ui.note);
                double fill = career.TankCapacity - ship.Fuel;
                if (fill > 0.05)
                {
                    int fb = ui.Buttons($"Fill tank ({fill:N1} t, {career.FillCost(ship):N0} cr)", "+20 t");
                    if (fb == 0) career.BuyFuel(ship, fill);
                    if (fb == 1) career.BuyFuel(ship, 20);
                }
                var up = new List<(string label, Action buy)>();
                if (career.DriveLevel < Career.DriveG.Length - 1)
                    up.Add(($"Drive {Career.DriveG[career.DriveLevel + 1]:0} g ({Career.DriveCost[career.DriveLevel + 1]:N0} cr)" + (Career.DriveG[career.DriveLevel + 1] > 3 ? " juice: fictional" : ""), () => career.BuyDrive()));
                if (career.TankLevel < Career.MaxTankLevel) up.Add(($"Tank +{Career.TankStep:N0} t ({Career.TankCost:N0} cr)", () => career.BuyTank(ship)));
                if (career.Codex.SensorLevel == 0) up.Add(($"Sensors: range ×2, scan ÷2 ({Codex.SensorCost:N0} cr)", () => career.BuySensors()));
                if (up.Count > 0)
                {
                    int ub = ui.Buttons(up.ConvertAll(u => u.label).ToArray());
                    if (ub >= 0) up[ub].buy();
                }
                ContractBoard(ui);
            }
            else GUILayout.Label("Not docked. Park in orbit at a port to dock: " + string.Join(", ", Array.ConvertAll(Career.Ports, p => p.Name)) + ".", ui.note);

            TripPlanner(ui);
            CodexPanel(ui);
        }

        bool showFeats;

        void CodexPanel(PanelUI ui)
        {
            var cx = career.Codex;
            int bodies = sys.Bodies.Count - 1;
            ui.Section($"Codex: {cx.Visited.Count} visited, {cx.Scanned.Count} scanned of {bodies}");
            var b = ship.Target;
            if (b != null && b.Parent != null)
            {
                bool seen = cx.Visited.Contains(b.Name), scanned = cx.Scanned.Contains(b.Name);
                GUILayout.Label(b.Name + (scanned ? " (scanned)" : seen ? " (visited)" : " (not visited)"), ui.label);
                if (seen || scanned) foreach (var line in Codex.Data(sys, b, jd)) GUILayout.Label(line, ui.note);
                else GUILayout.Label($"Pass within {Km(Codex.VisitRange(b))} to log its data.", ui.note);
                if (scanned) foreach (var f in Codex.Facts(b.Name)) GUILayout.Label("• " + f, ui.note);
                else
                {
                    string sale = career.Sandbox ? "" : $" First data sells for {Codex.DataValue(sys, b, jd):N0} cr.";
                    if (cx.ScanTarget == b.Name && cx.ScanProgress > 0)
                        GUILayout.Label($"Scanning: {Fmt.Num(100 * cx.ScanProgress / cx.ScanSeconds, 0)} %. Keep the drive off within {Km(cx.ScanRange(b))}.{sale}", ui.label);
                    else GUILayout.Label($"Scan: stay within {Km(cx.ScanRange(b))} with the drive off for {Dur(cx.ScanSeconds)}.{sale}", ui.note);
                }
            }
            showFeats = ui.Toggle($"Achievements ({cx.Achieved.Count} of {Codex.Feats.Length})", showFeats);
            if (showFeats)
                foreach (var f in Codex.Feats)
                    GUILayout.Label((cx.Has(f.Id) ? "✓ " : "· ") + f.Title + ": " + f.How, cx.Has(f.Id) ? ui.label : ui.note);
        }

        void ActiveJobs(PanelUI ui)
        {
            if (career.Active.Count == 0) return;
            GUILayout.Label("Jobs", ui.label);
            foreach (var c in career.Active.ToArray())
            {
                double left = c.DeadlineJd - jd;
                string due = left >= 0 ? "due in " + Days(left) : "LATE by " + Days(-left);
                string what = c.Kind == JobKind.Rescue
                    ? $"{c.Title}: " + (c.Dwell > 0 ? $"crew crossing over, {Fmt.Num(100 * c.Dwell / Career.TransferSeconds, 0)} %" : $"match velocity within {Career.RescueKm:N0} km and {Career.RescueKms * 1000:N0} m/s, then hold")
                    : c.Kind == JobKind.Survey
                    ? $"{c.Title}: {Fmt.Num(100 * c.Dwell / c.DwellNeeded, 0)} % scanned (stay within {Km(career.Codex.ScanRange(sys[c.To]))}, drive off)"
                    : c.Kind == JobKind.Passengers ? $"{c.People} × {c.Title} → {c.To}" : $"{c.Tonnes:N0} t {c.Title} → {c.To}";
                GUILayout.Label($"{what}   ·   {due}   ·   {c.Pay:N0} cr", left >= 0 ? ui.label : ui.note);
                var body = career.TargetOf(c);
                int b = ui.Buttons((ship.Target == body ? "● " : "") + "Set target", "Abandon");
                if (b == 0 && body != null) SetTarget(body);
                if (b == 1) career.Abandon(c);
            }
        }

        void ContractBoard(PanelUI ui)
        {
            GUILayout.Label("Contract board (refreshes each day)", ui.label);
            if (career.Board.Count == 0) { GUILayout.Label("No work posted.", ui.note); return; }
            foreach (var c in career.Board.ToArray())
            {
                // reaction mass a 1 g full burn would take, for comparison
                var dest = career.TargetOf(c);
                double d = Math.Max((sys.Pos(dest, jd) - ship.Pos).Mag - Ship.Standoff(dest), 0);
                Career.Trip(d, career.DriveRating * Ship.G0, 1, ship.Mass + c.Load, out double t, out _, out double fuel);
                string what = c.Kind == JobKind.Survey || c.Kind == JobKind.Rescue ? c.Title
                    : c.Kind == JobKind.Passengers ? $"{c.People} × {c.Title} → {c.To} (max 1 g)" : $"{c.Tonnes:N0} t {c.Title} → {c.To}";
                GUILayout.Label($"{what}\n{c.Pay:N0} cr   ·   {Days(c.Days)} allowed   ·   {Km(d)}   ·   full burn at {career.DriveRating:0} g: {Dur(t)} with parking, {fuel:N1} t", ui.note);
                bool ok = career.CanAccept(c, out string why);
                if (ui.Buttons(ok ? "Accept" : "Accept (" + why + ")") == 0 && ok) career.Accept(c, jd);
            }
        }

        void TripPlanner(PanelUI ui)
        {
            if (ship.Target == null) return;
            ui.Section("Trip planner: " + ship.Target.Name);
            int ci = ui.Buttons(Array.ConvertAll(new[] { 0, 1, 2 }, i => (cruiseIdx == i ? "● " : "") + CruiseNames[i]));
            if (ci >= 0) cruiseIdx = ci;
            double d = Math.Max((sys.Pos(ship.Target, jd) - ship.Pos).Mag - Ship.Standoff(ship.Target), 0);
            Career.Trip(d, ship.AMax, CruiseFracs[cruiseIdx], ship.Mass, out double t, out double dv, out double fuel);
            GUILayout.Label($"At {Fmt.Num(ship.MaxG, 1)} g: {Dur(t)} with parking, Δv {dv:N0} km/s, reaction mass {fuel:N1} t of {ship.Fuel:N1} t", fuel > ship.Fuel ? ui.label : ui.note);
            if (fuel > ship.Fuel) GUILayout.Label("Not enough reaction mass. Coast, lower the g, or refuel.", ui.label);
            foreach (var c in career.Active)
            {
                var body = career.TargetOf(c);
                if (body != ship.Target) continue;
                double arrive = t / 86400 + (c.Kind == JobKind.Survey ? c.DwellNeeded / 86400 : 0);
                bool onTime = jd + arrive <= c.DeadlineJd;
                GUILayout.Label($"{c.Title}: arrives with {Days(Math.Abs(c.DeadlineJd - jd - arrive))} {(onTime ? "to spare" : "too late")}", onTime ? ui.note : ui.label);
            }
            if (ship.PlanActive && ship.CruiseCap > 0) GUILayout.Label($"Cruise speed {ship.CruiseCap:N0} km/s", ui.note);
            GUILayout.Label("Δv grows with √g: a gentler burn or a coast saves reaction mass and costs days.", ui.note);
        }

        void TutorialHint(PanelUI ui)
        {
            var c = career.Active.Find(x => x.Tutorial);
            if (c == null) return;
            var moon = sys.Moon;
            string hint;
            if (ship.Target != moon) hint = $"First job: take the medical supplies to Luna. Set the Moon as the target: press {Ctl.Keys(A.NextTarget)} until it shows, click its label, or use Set target below.";
            else if (!ship.PlanActive) hint = $"Pick Full burn or a coast in the trip planner, then press {Ctl.Keys(A.Engage)} (Engage plan). Raise time compression with {Ctl.Keys(A.WarpUp)}.";
            else hint = "The autopilot burns, flips at the midpoint and brakes, then parks in orbit. In orbit at a port you are docked, and the job pays.";
            GUILayout.Label(hint, ui.body);
        }

        // ---------------------------------------------------------------- notes (toasts)

        GUIStyle noteStyle;

        void DrawCareerNotes()
        {
            if (career == null || career.Notes.Count == 0) return;
            if (noteStyle == null) noteStyle = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            float ui = App.UiScale;
            float W = UnityEngine.Screen.width / ui;
            float free = W - App.PanelWidth - 32;
            float y = 64;
            for (int i = Math.Max(0, career.Notes.Count - 4); i < career.Notes.Count; i++)
            {
                var n = career.Notes[i];
                var r = new Rect((free - 560) / 2 + 8, y, 560, 26);
                GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = n.Good ? PanelUI.Accent : PanelUI.Warn;
                GUI.Label(r, n.Text, noteStyle);
                y += 30;
            }
            GUI.color = Color.white;
        }

        static string Days(double d) => d < 2 ? Fmt.Num(d * 24, 1) + " h" : Fmt.Num(d, 1) + " days";

        // ---------------------------------------------------------------- save

        const string CareerKey = "fow-career-v1";

        void SaveCareer()
        {
            PlayerPrefs.SetString(CareerKey, JsonUtility.ToJson(career.ToSave(ship)));
            PlayerPrefs.Save();
        }

        void LoadCareer()
        {
            foreach (var old in career.Active.ToArray()) career.Abandon(old); // drop this session's derelicts from the system
            career = new Career { Sys = sys };
            if (!PlayerPrefs.HasKey(CareerKey)) { career.StartNew(jd); ship.Fuel = career.TankCapacity; career.Apply(ship); return; }
            try { career.FromSave(JsonUtility.FromJson<CareerSave>(PlayerPrefs.GetString(CareerKey)), ship); }
            catch (Exception e) { Debug.LogWarning("Fog of War: could not read the career save: " + e.Message); career.StartNew(jd); ship.Fuel = career.TankCapacity; }
        }
    }
}
