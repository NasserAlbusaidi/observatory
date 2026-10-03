using System;
using System.Collections.Generic;
using System.Text;
using FogOfWar.OpenWorld;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;
using A = FogOfWar.Controls.Action;

namespace FogOfWar
{
    // The career layer in the UI Toolkit HUD: the Jobs tab of the ship console (business, docked services, contract
    // board, trip planner), credits and reaction mass in the flight cluster, and career notes as toasts.
    // Same rules and calls as the IMGUI CareerPanel in OpenSystemScene.Career.cs.
    public partial class OpenSystemScene
    {
        VisualElement jobsBox, rmFill;
        Label rmText, creditsLabel;
        string jobsSig;
        float jobsT, lastNoteTime;

        void BuildCareerStrip(VisualElement drive, VisualElement status)
        {
            var head = Ui.E(drive, "row");
            head.style.marginTop = 6;
            Ui.L(head, "REACTION MASS", "t-micro", "f-condm").style.flexGrow = 1;
            rmText = Ui.L(head, "", "t-label", "f-mono");
            var bar = Ui.E(drive, "thr", "rm");
            bar.style.height = 6;
            rmFill = Ui.E(bar, "fill");
            rmFill.style.backgroundColor = new Color(0.41f, 0.82f, 1f, 0.85f);
            creditsLabel = Ui.L(status, "", "t-h3", "f-mono", "accent");
            creditsLabel.style.marginTop = 6;
        }

        void UpdateCareerStrip()
        {
            if (career == null || rmText == null) return;
            if (career.Sandbox)
            {
                Ui.Text(rmText, "SANDBOX: UNLIMITED");
                rmFill.style.width = Length.Percent(100);
                Ui.Text(creditsLabel, "");
            }
            else
            {
                double frac = career.TankCapacity > 0 ? ship.Fuel / career.TankCapacity : 0;
                Ui.Text(rmText, $"{ship.Fuel:N0} / {career.TankCapacity:N0} t");
                rmFill.style.width = Length.Percent((float)Math.Max(0, Math.Min(1, frac)) * 100);
                bool low = frac < 0.15;
                Ui.SetClass(rmText, "warn", low);
                rmFill.style.backgroundColor = low ? new Color(1f, 0.42f, 0.37f, 0.9f) : new Color(0.41f, 0.82f, 1f, 0.85f);
                Ui.Text(creditsLabel, $"{career.Credits:N0} CR" + (career.Docked != null ? "  ·  DOCKED " + career.Docked.Name.ToUpperInvariant() : ""));
                Ui.SetClass(creditsLabel, "warn", career.Credits < 0);
            }
            // career notes become toasts
            float newest = lastNoteTime;
            foreach (var n in career.Notes)
            {
                if (n.Time <= lastNoteTime) continue;
                hudShell.Toast(n.Text, null, 5f, n.Good ? null : "bad");
                newest = Mathf.Max(newest, n.Time);
            }
            lastNoteTime = newest;
        }

        // drive-limit buttons above what the drive (or the passengers) allow are disabled, not silently clamped
        void UpdateDriveLimit()
        {
            double cap = career != null && !career.Sandbox ? career.MaxG : double.MaxValue;
            for (int i = 0; i < Gs.Length; i++) segG.SetEnabled(i, Gs[i] <= cap + 1e-6);
            Ui.Text(segGNote, career == null || career.Sandbox ? "* 10 g needs the juice (fictional)." :
                $"Drive rated {career.DriveRating:0} g" + (career.CarryingPassengers ? ", held to 1 g with passengers aboard" : "") + ". Upgrade at a port.");
        }

        Label segGNote;

        void JobsTab(VisualElement c)
        {
            jobsBox = Ui.E(c, "col");
            jobsSig = null;
        }

        string JobsSignature()
        {
            var sb = new StringBuilder();
            sb.Append(career.Sandbox).Append('|').Append(Math.Round(career.Credits)).Append('|').Append(Math.Round(ship.Fuel, 1)).Append('|')
              .Append(career.Docked?.Name).Append('|').Append(career.DriveLevel).Append(career.TankLevel).Append('|').Append(ship.Target?.Name).Append('|')
              .Append(career.Codex.SensorLevel).Append(cruiseIdx).Append('|').Append(Math.Round(ship.MaxG, 2)).Append('|').Append(ship.PlanActive).Append('|').Append(Math.Round(jd, 2));
            foreach (var a in career.Active) sb.Append('a').Append(a.Id).Append(':').Append(Math.Round(a.Dwell));
            foreach (var b in career.Board) sb.Append('b').Append(b.Id);
            return sb.ToString();
        }

        void UpdateJobs()
        {
            if (jobsBox == null || career == null) return;
            if ((jobsT -= Time.unscaledDeltaTime) > 0) return;
            jobsT = 0.4f;
            var sig = JobsSignature();
            if (sig == jobsSig) return;
            jobsSig = sig;
            jobsBox.Clear();
            BuildJobs(jobsBox);
            Ui.Fontify(jobsBox);
        }

        void BuildJobs(VisualElement c)
        {
            if (career.Sandbox)
            {
                Ui.L(c, "Sandbox: unlimited reaction mass, no contracts.", "t-body");
                var r = Ui.E(c, "check-row");
                r.Add(new Switch(true, v => career.Sandbox = v));
                Ui.L(r, "Sandbox", "t-body");
                return;
            }
            Ui.Section(c, "Ship's business");
            Ui.KV(c, "Credits", $"{career.Credits:N0} cr", career.Credits < 0 ? "warn" : "accent");
            Ui.KV(c, "Reaction mass", $"{ship.Fuel:N1} / {career.TankCapacity:N0} t");
            Ui.KV(c, "Hold", $"{career.CargoTonnes:N1} / {Career.HoldTonnes:N0} t");
            Ui.KV(c, "Drive", $"{career.DriveRating:0} g" + (career.CarryingPassengers ? " (1 g, passengers)" : ""));
            Ui.KV(c, "Δv left", $"{Career.DvLeft(ship):N0} km/s");
            if (ship.Fuel < 1)
            {
                Ui.L(c, "The tank is dry. You are adrift.", "t-body", "warn").style.marginTop = 6;
                if (career.Docked == null)
                    Ui.B(c, $"Call an emergency tender ({Career.EmergencyTonnes:N0} t, {Career.EmergencyCost:N0} cr)", () => career.EmergencyDrop(ship), "danger");
            }

            var tut = TutorialText ?? FirstJobHint(); // the running lesson, else the first-job hint
            if (tut != null)
            {
                var p = Ui.E(c, "panel");
                p.style.marginTop = 10; p.style.paddingTop = 8; p.style.paddingBottom = 8;
                p.style.borderLeftColor = new Color(1f, 0.71f, 0.28f); p.style.borderLeftWidth = 2;
                Ui.L(p, "FIRST JOB", "t-micro", "f-condm", "accent");
                Ui.L(p, tut, "t-body");
            }

            if (career.Active.Count > 0)
            {
                Ui.Section(c, "Jobs");
                foreach (var job in career.Active.ToArray())
                {
                    var j = job;
                    double left = j.DeadlineJd - jd;
                    var card = Ui.E(c, "panel", "soft");
                    card.style.paddingTop = 8; card.style.paddingBottom = 4; card.style.marginBottom = 6;
                    var head = Ui.E(card, "row");
                    Ui.L(head, j.Title.ToUpperInvariant(), "t-h3", "f-cond", "job-title");
                    Ui.L(head, $"{j.Pay:N0} cr", "t-val", "f-mono", "accent");
                    string what = j.Kind == JobKind.Rescue
                        ? (j.Dwell > 0 ? $"Crew crossing over: {Fmt.Num(100 * j.Dwell / Career.TransferSeconds, 0)} %" : $"Match velocity within {Career.RescueKm:N0} km and {Career.RescueKms * 1000:N0} m/s, drive off, then hold.")
                        : j.Kind == JobKind.Survey
                        ? $"Survey {j.To}: {Fmt.Num(100 * j.Dwell / j.DwellNeeded, 0)} % scanned. Stay within {Km(career.Codex.ScanRange(sys[j.To]))} with the drive off."
                        : j.Kind == JobKind.Passengers ? $"{j.People} passengers to {j.To}" : $"{j.Tonnes:N0} t to {j.To}";
                    Ui.L(card, what, "t-note");
                    Ui.L(card, left >= 0 ? "Due in " + Days(left) : "LATE by " + Days(-left), "t-label", "f-mono", left >= 0 ? "dim" : "warn");
                    var body = career.TargetOf(j);
                    var row = Ui.E(card, "row");
                    row.style.marginTop = 6;
                    var st = Ui.B(row, "Set target", () => { if (body != null) SetTarget(body); }, "small");
                    Ui.SetClass(st, "on", body != null && ship.Target == body);
                    Ui.B(row, "Abandon", () => career.Abandon(j), "small", "danger");
                }
            }

            var port = career.Docked;
            if (port != null)
            {
                Ui.Section(c, "Docked at " + port.Name);
                Ui.L(c, $"Reaction mass {port.FuelPrice:N0} cr/t. {port.Note}", "t-note");
                double fill = career.TankCapacity - ship.Fuel;
                var row = Ui.E(c, "row", "wrap");
                row.style.marginTop = 6;
                if (fill > 0.05)
                {
                    Ui.B(row, $"Fill tank ({fill:N1} t, {career.FillCost(ship):N0} cr)", () => career.BuyFuel(ship, career.TankCapacity - ship.Fuel), "primary");
                    Ui.B(row, "+20 t", () => career.BuyFuel(ship, 20));
                }
                if (career.DriveLevel < Career.DriveG.Length - 1)
                {
                    double g = Career.DriveG[career.DriveLevel + 1];
                    Ui.B(row, $"Drive {g:0} g ({Career.DriveCost[career.DriveLevel + 1]:N0} cr)" + (g > 3 ? " *" : ""), () => career.BuyDrive());
                }
                if (career.TankLevel < Career.MaxTankLevel)
                    Ui.B(row, $"Tank +{Career.TankStep:N0} t ({Career.TankCost:N0} cr)", () => career.BuyTank(ship));
                if (career.Codex.SensorLevel == 0)
                    Ui.B(row, $"Sensors: ×2 range, ½ scan time ({Codex.SensorCost:N0} cr)", () => career.BuySensors());

                Ui.Section(c, "Contract board (refreshes each day)");
                if (career.Board.Count == 0) Ui.L(c, "No work posted.", "t-note");
                foreach (var job in career.Board.ToArray())
                {
                    var j = job;
                    var dest = career.TargetOf(j);
                    if (dest == null) continue;
                    double d = Math.Max((sys.Pos(dest, jd) - ship.Pos).Mag - Ship.Standoff(dest), 0);
                    Career.Trip(d, career.DriveRating * Ship.G0, 1, ship.Mass + j.Load, out double t, out _, out double fuel);
                    var card = Ui.E(c, "panel", "soft");
                    card.style.paddingTop = 8; card.style.paddingBottom = 4; card.style.marginBottom = 6;
                    var head = Ui.E(card, "row");
                    Ui.L(head, j.Title.ToUpperInvariant(), "t-h3", "f-cond", "job-title");
                    Ui.L(head, $"{j.Pay:N0} cr", "t-val", "f-mono", "accent");
                    string what = j.Kind == JobKind.Rescue ? "Find the derelict and take the crew off" : j.Kind == JobKind.Survey ? $"Survey {j.To}" : j.Kind == JobKind.Passengers ? $"{j.People} passengers to {j.To} (max 1 g)" : $"{j.Tonnes:N0} t to {j.To}";
                    Ui.L(card, what, "t-body");
                    Ui.L(card, $"{Days(j.Days)} allowed · {Km(d)} · full burn at {career.DriveRating:0} g: {Dur(t)}, {fuel:N1} t", "t-note", "f-mono");
                    bool ok = career.CanAccept(j, out string why);
                    var b = Ui.B(card, ok ? "Accept" : "Can't accept: " + why, () => { if (career.CanAccept(j, out _)) career.Accept(j, jd); }, "small", ok ? "primary" : "disabled");
                    b.style.alignSelf = Align.FlexStart; b.style.marginTop = 6;
                }
            }
            else Ui.L(c, "Not docked. Park in orbit at a port to dock: " + string.Join(", ", Array.ConvertAll(Career.Ports, p => p.Name)) + ".", "t-note").style.marginTop = 8;

            if (ship.Target != null)
            {
                Ui.Section(c, "Trip planner: " + ship.Target.Name);
                c.Add(new Segmented(CruiseNames, cruiseIdx, i => { cruiseIdx = i; jobsSig = null; }));
                double d = Math.Max((sys.Pos(ship.Target, jd) - ship.Pos).Mag - Ship.Standoff(ship.Target), 0);
                Career.Trip(d, ship.AMax, CruiseFracs[cruiseIdx], ship.Mass, out double t, out double dv, out double fuel);
                bool short_ = fuel > ship.Fuel;
                Ui.L(c, $"At {Fmt.Num(ship.MaxG, 1)} g: {Dur(t)} with parking, Δv {dv:N0} km/s, reaction mass {fuel:N1} t of {ship.Fuel:N1} t", "t-note", "f-mono", short_ ? "warn" : "dim");
                if (short_) Ui.L(c, "Not enough reaction mass. Coast, lower the g, or refuel.", "t-body", "warn");
                foreach (var j in career.Active)
                {
                    var body = career.TargetOf(j);
                    if (body != ship.Target) continue;
                    double arrive = t / 86400 + (j.Kind == JobKind.Survey ? j.DwellNeeded / 86400 : 0);
                    bool onTime = jd + arrive <= j.DeadlineJd;
                    Ui.L(c, $"{j.Title}: arrives with {Days(Math.Abs(j.DeadlineJd - jd - arrive))} {(onTime ? "to spare" : "too late")}", "t-body", onTime ? "ok" : "warn");
                }
                if (ship.PlanActive && ship.CruiseCap > 0) Ui.L(c, $"Cruise speed {ship.CruiseCap:N0} km/s", "t-note");
                var eng = Ui.B(c, (ship.PlanActive ? "ABORT PLAN  " : "ENGAGE  ") + UiShell.Keys(A.Engage), TogglePlan, "primary", "f-cond");
                eng.style.marginTop = 6; eng.style.alignSelf = Align.FlexStart;
                Ui.L(c, "Δv grows with √g: a gentler burn or a coast saves reaction mass and costs days.", "t-note");
            }
        }

        string FirstJobHint()
        {
            var c = career.Active.Find(x => x.Tutorial);
            if (c == null) return null;
            if (ship.Target != sys.Moon) return $"Take the medical supplies to Luna. Set the Moon as the target: press {UiShell.Keys(A.NextTarget)} until it shows, click its label, or use Set target below.";
            if (!ship.PlanActive) return $"Pick Full burn or a coast in the trip planner, then press {UiShell.Keys(A.Engage)} (Engage). Raise time compression with {UiShell.Keys(A.WarpUp)}.";
            return "The autopilot burns, flips at the midpoint and brakes, then parks in orbit. In orbit at a port you are docked, and the job pays.";
        }
    }
}
