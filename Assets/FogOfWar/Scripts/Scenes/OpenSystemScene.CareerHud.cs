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
    // The career layer in the UI Toolkit HUD: the Jobs page (business, docked services, contract
    // board, trip planner), credits and reaction mass in the flight cluster, and career notes as toasts.
    // Same rules and calls as the IMGUI CareerPanel in OpenSystemScene.Career.cs.
    public partial class OpenSystemScene
    {
        VisualElement jobsBox, rmFill;
        Label rmText, creditsLabel;
        string jobsSig;
        float jobsT, lastNoteTime;
        // a payday counts the credits up instead of swapping the number; anything else (a purchase, a load) snaps
        double shownCredits = double.NaN, countFrom;
        float countT0 = -1;

        void BuildCareerStrip(VisualElement drive, VisualElement status)
        {
            var head = Ui.E(drive, "row");
            head.style.marginTop = 6;
            Ui.L(head, "FUEL", "t-micro", "f-condm").style.flexGrow = 1;
            rmText = Ui.L(head, "", "t-label", "f-mono");
            var bar = Ui.E(drive, "thr", "rm");
            bar.style.height = 6;
            rmFill = Ui.E(bar, "fill");
            rmFill.style.backgroundColor = new Color(0.41f, 0.82f, 1f, 0.85f);
            creditsLabel = Ui.L(status, "", "t-h3", "f-mono", "accent", "credits");
            creditsLabel.style.marginTop = 6;
        }

        void UpdateCareerStrip()
        {
            if (career == null || rmText == null) return;
            UpdateCallCard();
            // career notes become toasts; a payday gets its own look, stays longer and says where the business stands
            float newest = lastNoteTime;
            bool paid = false;
            foreach (var n in career.Notes)
            {
                if (n.Time <= lastNoteTime) continue;
                if (Career.IsPay(n.What))
                {
                    paid = true;
                    hudShell.Toast(n.Text, $"{career.Credits:N0} cr in the bank  ·  {career.Done} {(career.Done == 1 ? "job" : "jobs")} done", 7f, "pay");
                }
                else hudShell.Toast(n.Text, null, 5f, n.Good ? null : "bad");
                newest = Mathf.Max(newest, n.Time);
            }
            lastNoteTime = newest;

            // by the clock, not by frame steps: one long frame (a save, a shot) must not skip the count
            double cr = career.Credits;
            if (paid && !double.IsNaN(shownCredits) && cr > shownCredits) { countFrom = shownCredits; countT0 = Time.realtimeSinceStartup; }
            float k = countT0 < 0 ? 1 : (Time.realtimeSinceStartup - countT0) / 1.2f;
            if (k >= 1 || cr < countFrom) countT0 = -1;
            shownCredits = countT0 < 0 ? cr : countFrom + (cr - countFrom) * Math.Min(1, k);
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
                Ui.Text(creditsLabel, $"{shownCredits:N0} CR" + (career.Docked != null ? "  ·  DOCKED " + career.Docked.Name.ToUpperInvariant() : ""));
                Ui.SetClass(creditsLabel, "warn", career.Credits < 0);
                Ui.SetClass(creditsLabel, "payday", countT0 >= 0);
            }
        }

        // a route call (Career.Route.cs): a card with the two answers, in the lesson card's place
        VisualElement callCard;
        Label callTitle, callText;
        Label callYes, callNo;
        Career.RouteCall callShown;

        void BuildCallCard(VisualElement layer)
        {
            callCard = Ui.E(layer, "panel", "lesson", "call");
            Ui.Corners(callCard);
            callTitle = Ui.L(callCard, "", "t-micro", "f-condm", "warn");
            callText = Ui.L(callCard, "", "t-body", "lesson-text");
            var row = Ui.E(callCard, "row");
            row.style.marginTop = 8;
            callYes = Ui.B(row, "", () => career?.Answer(true, jd), "small", "primary");
            callNo = Ui.B(row, "", () => career?.Answer(false, jd), "small");
            Ui.Show(callCard, false);
        }

        void UpdateCallCard()
        {
            var call = career != null && !career.Sandbox ? career.Call : null;
            // a lesson on screen keeps its place: the call sits above it
            bool lessonUp = lessonCard != null && lessonCard.parent != null && lessonCard.resolvedStyle.display == DisplayStyle.Flex && !float.IsNaN(lessonCard.layout.y);
            callCard.style.bottom = lessonUp ? new StyleLength(lessonCard.parent.layout.height - lessonCard.layout.y + 10) : new StyleLength(StyleKeyword.Null);
            if (call == callShown) return;
            callShown = call;
            Ui.Show(callCard, call != null);
            Ui.SetClass(callCard, "in", call != null);
            if (call == null) return;
            Ui.Text(callTitle, call.Title.ToUpperInvariant());
            Ui.Text(callText, call.Text);
            Ui.Text(callYes, call.Yes); Ui.Text(callNo, call.No);
        }

        // review shots: a route call of the given kind about the first active job, made the way a trip makes one
        public bool TestRouteCall(Career.RouteKind kind) => career.Active.Count > 0 && career.MakeCall(kind, career.Active[0], sys, ship, jd);
        public string TestRouteCallReport() => $"call {career.Call?.Title ?? "none"} about {(career.Active.Count > 0 ? career.Active[0].Title + " to " + career.Active[0].To : "no job")}, docked {career.Docked?.Name ?? "-"}, " +
            $"card {callCard.resolvedStyle.display} opacity {callCard.resolvedStyle.opacity:F2} at {callCard.layout}, on screen {callCard.layout.yMax <= callCard.parent.layout.height && callCard.layout.y >= 0}" +
            (lessonCard.resolvedStyle.display == DisplayStyle.Flex ? $", above the lesson {callCard.layout.yMax <= lessonCard.layout.y}" : "");
        // review shots: a cargo run to Mars on the Jobs page, so the flown-in-by-hand line shows; false takes it off
        public void TestHandJob(bool on)
        {
            career.Active.RemoveAll(c => c.Key == "qa/hand-shot");
            if (on) career.Active.Add(new Contract { Id = 900003, Kind = JobKind.Cargo, Title = "Reactor parts", From = "Earth orbit", To = "Mars orbit", Tonnes = 40, Pay = 52000, IssuedJd = jd, DeadlineJd = jd + 30, Days = 30, Key = "qa/hand-shot" });
        }
        public void TestRouteCallClear() { if (career.Call != null) career.Answer(false, jd); }

        // review shots: a payday of `cr` credits the way Complete makes one (credits and a JobPaid note), in career mode;
        // TestPaydayUndo takes it back and restores the sandbox flag this returns
        public bool TestPayday(double cr)
        {
            bool wasSandbox = career.Sandbox;
            career.Sandbox = false;
            career.Credits += cr; career.Earned += cr; career.Done++;
            career.Notes.Add(new Career.Note { Text = $"Test delivery: +{cr:N0} cr", Good = true, Time = Time.realtimeSinceStartup, What = Career.Happening.JobPaid });
            return wasSandbox;
        }
        public void TestPaydayUndo(double cr, bool wasSandbox)
        {
            career.Credits -= cr; career.Earned -= cr; career.Done--;
            career.Sandbox = wasSandbox;
        }
        public string TestPaydayReport() => creditsLabel == null ? "none" : $"{creditsLabel.text} | counting {creditsLabel.ClassListContains("payday")} | bank {career.Credits:N0}";

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
              .Append(career.Codex.SensorLevel).Append(cruiseIdx).Append('|').Append(Math.Round(ship.MaxG, 2)).Append('|').Append(ship.PlanActive).Append(Math.Round(ship.CruiseCap)).Append('|').Append(Math.Round(jd, 2));
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
                Ui.L(c, "Sandbox: unlimited fuel, no contracts.", "t-body");
                var r = Ui.E(c, "check-row");
                r.Add(new Switch(true, v => career.Sandbox = v));
                Ui.L(r, "Sandbox", "t-body");
                return;
            }
            Ui.Section(c, "Ship's business");
            Ui.KV(c, "Credits", $"{career.Credits:N0} cr", career.Credits < 0 ? "warn" : "accent");
            Ui.KV(c, "Fuel", $"{ship.Fuel:N1} / {career.TankCapacity:N0} t");
            Ui.KV(c, "Hold", $"{career.CargoTonnes:N1} / {Career.HoldTonnes:N0} t");
            Ui.KV(c, "Drive", $"{career.DriveRating:0} g" + (career.CarryingPassengers ? " (1 g, passengers)" : ""));
            Ui.KV(c, "Speed change left", $"{Career.DvLeft(ship):N0} km/s");
            if (ship.Fuel < 1)
            {
                Ui.L(c, career.Docked == null ? "The tank is dry. You are adrift." : $"The tank is dry. The port sells up to {Career.EmergencyTonnes:N0} t on credit.", "t-body", "warn").style.marginTop = 6;
                if (career.Docked == null)
                    Ui.B(c, $"Call an emergency tender ({Career.EmergencyTonnes:N0} t, {Career.EmergencyCost:N0} cr)", () => career.EmergencyDrop(ship), "danger");
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
                    double expressLeft = j.ExpressJd - jd;
                    if (j.ExpressJd > 0 && expressLeft >= 0)
                        Ui.L(card, $"Express bonus +{j.Pay * Career.ExpressBonus:N0} cr if done in {Days(expressLeft)}", "t-label", "f-mono", "ok");
                    var body = career.TargetOf(j);
                    if (body != null && left >= 0 && career.CanFlyInByHand(j))
                    {
                        // green while the pilot has the helm and it counts
                        Ui.L(card, $"Flown in by hand +{j.Pay * Career.HandBonus:N0} cr: nav computer off from {Km(Career.DockRange(body))} out to the dock",
                            "t-label", "f-mono", career.HandArrival(j) ? "ok" : "dim");
                    }
                    Ui.L(card, left >= 0 ? "Due in " + Days(left) : "LATE by " + Days(-left), "t-label", "f-mono", left >= 0 ? "dim" : "warn");
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
                Ui.L(c, $"Fuel {port.FuelPrice:N0} cr/t. {port.Note}", "t-note");
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
                    if (j.Tier != null) what += " · " + j.Tier;
                    Ui.L(card, what, "t-body");
                    // a far run can need more than the tank for a full burn: say so here, not after Engage
                    bool overTank = fuel > career.TankCapacity;
                    var cost = Ui.L(card, $"{Days(j.Days)} allowed · {Km(d)} · full burn at {career.DriveRating:0} g: {Dur(t)}, {fuel:N1} t" + (overTank ? " (more than the tank: coast)" : ""), "t-note", "f-mono");
                    Ui.SetClass(cost, "warn", overTank);
                    if (j.ExpressDays > 0)
                        Ui.L(card, $"Express bonus +{j.Pay * Career.ExpressBonus:N0} cr if done within {Days(j.ExpressDays)}", "t-note", "f-mono", "ok");
                    bool ok = career.CanAccept(j, out string why);
                    var b = Ui.B(card, ok ? "Accept" : "Can't accept: " + why, () => { if (career.CanAccept(j, out _)) career.Accept(j, jd); }, "small", ok ? "primary" : "disabled");
                    b.style.alignSelf = Align.FlexStart; b.style.marginTop = 6;
                }
            }
            else Ui.L(c, "Not docked. Park in orbit at a port to dock: " + string.Join(", ", Array.ConvertAll(Career.Ports, p => p.Name)) + ".", "t-note").style.marginTop = 8;
        }

        string FirstJobHint()
        {
            var c = career.Active.Find(x => x.Tutorial);
            if (c == null) return null;
            if (ship.Target != sys.Moon) return $"Take the medical supplies to Luna. Set the Moon as the target: press {UiShell.Keys(A.NextTarget)} until it shows, click its label, or open Jobs ({UiShell.Keys(A.Jobs)}) and use Set target.";
            if (!ship.PlanActive) return $"Pick Full burn or a coast in the trip planner (console {UiShell.Keys(A.Console)}, Nav tab), then press {UiShell.Keys(A.Engage)} (Engage). Raise time compression with {UiShell.Keys(A.WarpUp)}.";
            return "The autopilot burns, flips at the midpoint and brakes, then parks in orbit. In orbit at a port you are docked, and the job pays.";
        }
    }
}
