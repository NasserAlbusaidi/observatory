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
        VisualElement jobsBox, rmFill, rmNeed;
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
            rmNeed = Ui.E(bar, "need");
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
                else if (n.What == Career.Happening.ShipOwned) hudShell.Toast(n.Text, CareerRecord(), 12f, "pay");
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
                Ui.Show(rmNeed, false);
                Ui.Text(creditsLabel, "");
            }
            else
            {
                // the bar splits what is aboard: the part the trip to the target will burn (amber, with the planner's
                // margin, as DvShort judges it) and what is spare after it (cyan). Short: the whole tank in red.
                double cap = career.TankCapacity, need = 0;
                bool short_ = false;
                if (ship.Target != null && ship.Landed == null)
                {
                    PlanTrip(out _, out double dv, out _);
                    need = ship.Mass * (1 - Math.Exp(-dv * DvMargin / Ship.Ve));
                    short_ = DvShort(dv);
                }
                if (need < 0.05) need = 0;
                double frac = cap > 0 ? ship.Fuel / cap : 0;
                double spare = short_ ? 0 : Math.Max(ship.Fuel - need, 0);
                float Pct(double t) => (float)Math.Max(0, Math.Min(1, cap > 0 ? t / cap : 0)) * 100;
                Ui.Text(rmText, $"{ship.Fuel:N0} / {cap:N0} t" + (short_ ? "  ·  SHORT" : need > 0 ? $"  ·  TRIP {Fmt.Num(need, need < 10 ? 1 : 0)} t" : ""));
                rmFill.style.width = Length.Percent(Pct(spare));
                rmNeed.style.left = Length.Percent(Pct(spare));
                rmNeed.style.width = Length.Percent(Pct(ship.Fuel) - Pct(spare));
                Ui.Show(rmNeed, short_ || need > 0);
                Ui.SetClass(rmNeed, "short", short_);
                bool low = frac < 0.15;
                Ui.SetClass(rmText, "warn", low || short_);
                rmFill.style.backgroundColor = low ? new Color(1f, 0.42f, 0.37f, 0.9f) : new Color(0.41f, 0.82f, 1f, 0.85f);
                Ui.Text(creditsLabel, $"{shownCredits:N0} CR" + (career.Docked != null ? "  ·  DOCKED " + career.Docked.Name.ToUpperInvariant() : ""));
                Ui.SetClass(creditsLabel, "warn", career.Credits < 0);
                Ui.SetClass(creditsLabel, "payday", countT0 >= 0);
            }
        }

        // a route call (Career.Route.cs): a card with its answers (RouteCall.Answers: two, or a raider's three), in the
        // lesson card's place
        VisualElement callCard;
        Label callTitle, callText, callLive;
        readonly Label[] callBtns = new Label[3];
        Career.RouteCall callShown;

        void BuildCallCard(VisualElement layer)
        {
            callCard = Ui.E(layer, "panel", "lesson", "call");
            Ui.Corners(callCard);
            callTitle = Ui.L(callCard, "", "t-micro", "f-condm", "warn");
            callText = Ui.L(callCard, "", "t-body", "lesson-text");
            callLive = Ui.L(callCard, "", "t-label", "f-mono", "warn");
            callLive.style.marginTop = 6;
            var row = Ui.E(callCard, "row", "wrap");
            row.style.marginTop = 8;
            for (int i = 0; i < callBtns.Length; i++)
            {
                int k = i;
                callBtns[i] = Ui.B(row, "", () => AnswerCall(callShown, k), i == 0 ? new[] { "small", "primary" } : new[] { "small" });
            }
            callShown = null; // a HUD built again (the classic panel turned off) shows a call that is already on
            Ui.Show(callCard, false);
        }

        void UpdateCallCard()
        {
            var call = career != null && !career.Sandbox ? career.Call : null;
            // a lesson on screen keeps its place: the call sits above it
            bool lessonUp = lessonCard != null && lessonCard.parent != null && lessonCard.resolvedStyle.display == DisplayStyle.Flex && !float.IsNaN(lessonCard.layout.y);
            callCard.style.bottom = lessonUp ? new StyleLength(lessonCard.parent.layout.height - lessonCard.layout.y + 10) : new StyleLength(StyleKeyword.Null);
            // a raider's range, closing speed and boarding clock change every frame; the rest only with the call
            string live = call?.Kind == Career.RouteKind.Pirate ? RaiderLine() : null;
            Ui.Show(callLive, live != null);
            if (live != null) Ui.Text(callLive, live);
            if (call == callShown) return;
            callShown = call;
            Ui.Show(callCard, call != null);
            Ui.SetClass(callCard, "in", call != null);
            if (call == null) return;
            Ui.Text(callTitle, call.Title.ToUpperInvariant());
            Ui.Text(callText, call.Text);
            // a button shows only with a label: a raider's Divert drops out when no patrolled port is in the system
            for (int i = 0; i < callBtns.Length; i++)
            {
                string label = call.Answers != null && i < call.Answers.Length ? call.Answers[i].Label : null;
                Ui.Show(callBtns[i], !string.IsNullOrEmpty(label));
                Ui.Text(callBtns[i], label ?? "");
            }
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

        // the career so far: the buyout card and the Jobs page say it (Earned and Failed were saved but never shown)
        string CareerRecord() => $"{career.Done} {(career.Done == 1 ? "job" : "jobs")} done, {career.Failed} failed  ·  {career.Earned:N0} cr earned" +
            (career.StartJd > 0 ? $"  ·  day {Math.Floor(jd - career.StartJd) + 1:N0}" : "");

        // review shots: the buyout part paid, so the Jobs page shows the bar; NaN puts back what was there
        double shotShipPaid = double.NaN;
        public void TestShipPaid(double paid)
        {
            if (double.IsNaN(paid)) { if (!double.IsNaN(shotShipPaid)) career.ShipPaid = shotShipPaid; shotShipPaid = double.NaN; return; }
            if (double.IsNaN(shotShipPaid)) shotShipPaid = career.ShipPaid;
            career.ShipPaid = paid;
        }
        // review shots: the ship bought out, with the note PayTowardShip makes. Not through PayTowardShip: its event
        // could autosave the shot state over the player's save. TestBuyoutUndo takes it back.
        public void TestBuyout()
        {
            TestShipPaid(Career.ShipPrice);
            career.Notes.Add(new Career.Note { Text = "The ship is yours: bought out from the yard", Good = true, Time = Time.realtimeSinceStartup, What = Career.Happening.ShipOwned });
        }
        public void TestBuyoutUndo()
        {
            TestShipPaid(double.NaN);
            career.Codex.Achieved.Remove("owner");
        }
        public string TestBuyoutReport() => $"owned {career.OwnsShip}, record \"{CareerRecord()}\"";

        // drive-limit buttons above what the drive (or the passengers) allow are disabled, not silently clamped
        void UpdateDriveLimit()
        {
            double cap = career != null && !career.Sandbox ? career.MaxG : double.MaxValue;
            for (int i = 0; i < Gs.Length; i++) segG.SetEnabled(i, Gs[i] <= cap + 1e-6);
            Ui.Text(segGNote, career == null || career.Sandbox ? "* 10 g needs the juice (fictional)." :
                $"Drive {career.Thrust:0.0} MN: {Fmt.Num(career.AccelAt(ship.Mass) / Ship.G0, 2)} g at {ship.Mass:N0} t, more as the tank empties" + (career.CarryingPassengers ? ", held to 1 g with passengers aboard" : "") + ". Upgrade at a port.");
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
              .Append(career.Docked?.Name).Append('|').Append(career.ShipPaid).Append('|').Append(lesson).Append(ship.Landed != null).Append('|').Append(string.Join(",", career.Build.pick)).Append(career.InYard).Append(yardDraft == null ? "" : string.Join(",", yardDraft.pick)).Append('|').Append(ship.Target?.Name).Append('|')
              .Append(career.Codex.SensorLevel).Append(cruiseIdx).Append('|').Append(Math.Round(ship.MaxG, 2)).Append('|').Append(ship.PlanActive).Append(Math.Round(ship.CruiseCap)).Append('|').Append(Math.Round(jd, 2));
            for (int i = 1; i < Career.Factions.Length; i++) sb.Append('s').Append(career.Standing((Faction)i));
            // pay and tonnes too: a raider's boarding or give-up changes them on a job that stays on
            foreach (var a in career.Active) sb.Append('a').Append(a.Id).Append(':').Append(Math.Round(a.Dwell)).Append(':').Append(Math.Round(a.Pay)).Append(':').Append(Math.Round(a.Tonnes, 1));
            if (career.Encounter != null) foreach (var a in RaiderAnswers(out _)) sb.Append('r').Append(a.Label);
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
            Ui.KV(c, "Hold", $"{career.CargoTonnes:N1} / {career.HoldTonnes:N0} t");
            Ui.KV(c, "Cabin", $"{career.PeopleAboard} / {career.Build.Seats} seats");
            Ui.KV(c, "Drive", $"{career.Thrust:0.0} MN, {Fmt.Num(career.AccelAt(ship.Mass) / Ship.G0, 2)} g now" + (career.CarryingPassengers ? " (1 g, passengers)" : ""));
            Ui.KV(c, "Speed change left", $"{Career.DvLeft(ship):N0} km/s");
            Ui.KV(c, "Record", CareerRecord());
            // standing with each port owner (Career.Factions.cs): the tier sets the pay on that faction's boards
            for (int i = 1; i < Career.Factions.Length; i++)
            {
                var f = (Faction)i;
                if (!Career.OwnsPort(f)) continue; // the Raiders post no jobs
                double pay = career.PayFactor(f);
                var (_, tier) = Ui.KV(c, Career.Factions[i].Name, $"{career.TierName(f)} ({career.Standing(f):+0;−0;0}) · {career.PayText(f)}");
                if (pay != 1) tier.AddToClassList(pay > 1 ? "ok" : "warn");
            }
            Ui.L(c, $"A job done on time: +{Career.StandingGain} with the faction whose port posted it. A failed or abandoned job: −{Career.StandingLoss}.", "t-note").style.marginTop = 2;
            // the long goal: buy the ship out from the yard
            if (career.OwnsShip) Ui.KV(c, "Ship", "Yours: bought out from the yard", "ok");
            else
            {
                Ui.KV(c, "Ship buyout", $"{career.ShipPaid:N0} / {Career.ShipPrice:N0} cr");
                var bar = Ui.E(c, "thr");
                bar.style.height = 6; bar.style.marginTop = 4;
                Ui.E(bar, "fill").style.width = Length.Percent((float)(100 * career.ShipPaid / Career.ShipPrice));
                double step = career.ShipStepNow;
                if (career.Docked == null) Ui.L(c, $"The yard owns the ship until you buy it out. Pay in steps of {Career.ShipStep:N0} cr at any port.", "t-note").style.marginTop = 4;
                else
                {
                    bool can = career.Credits >= step;
                    var pay = Ui.B(c, can ? $"Pay {step:N0} cr toward the ship" : $"Pay {step:N0} cr toward the ship: not enough credits", () => career.PayTowardShip(), "small", can ? "primary" : "disabled");
                    pay.style.alignSelf = Align.FlexStart; pay.style.marginTop = 6;
                }
            }
            if (ship.Fuel < 1)
            {
                Ui.L(c, career.Docked == null ? "The tank is dry. You are adrift." : $"The tank is dry. The port sells up to {Career.EmergencyTonnes:N0} t on credit.", "t-body", "warn").style.marginTop = 6;
                if (career.Docked == null)
                    Ui.B(c, $"Call an emergency tender ({Career.EmergencyTonnes:N0} t, {Career.EmergencyCost:N0} cr)", () => career.EmergencyDrop(ship), "danger");
            }

            // the optional hand-flying practice (OpenSystemScene.Tutorial.cs), once the first flight is over
            if (CanPractise && lesson == Lesson.Done && !career.Active.Exists(x => x.Tutorial))
            {
                var practice = Ui.B(c, "Hand-flying practice: turn, burn and fly an approach to a beacon", StartPractice, "small");
                practice.style.alignSelf = Align.FlexStart; practice.style.marginTop = 6;
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
                    var owner = Career.OwnerOf(j);
                    if (owner != Faction.None) what += " · for " + Career.Factions[(int)owner].Name;
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
                    if (career.Encounter?.Job == j)
                    {
                        // the call's answers while its card is up (this page covers the card), then the ones a chase keeps
                        var answers = RaiderAnswers(out var call);
                        Ui.L(card, ChaseWarn(career.Encounter.Hull, call != null), "t-label", "warn").style.marginTop = 4;
                        var chase = Ui.E(card, "row", "wrap");
                        chase.style.marginTop = 4;
                        for (int i = 0; i < answers.Length; i++)
                        {
                            int k = i; var a = answers[i].What;
                            Ui.B(chase, answers[i].Label, () => { if (call != null) AnswerCall(call, k); else career.Answer(a, jd); }, "small");
                        }
                    }
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
                var who = Career.Factions[(int)port.Owner];
                Ui.L(c, $"Owned by the {who.Name}. You are {career.TierName(port.Owner)} with them: {career.PayText(port.Owner)} on this board. {who.Blurb}", "t-note").style.marginTop = 2;
                double fill = career.TankCapacity - ship.Fuel;
                var row = Ui.E(c, "row", "wrap");
                row.style.marginTop = 6;
                if (fill > 0.05)
                {
                    Ui.B(row, $"Fill tank ({fill:N1} t, {career.FillCost(ship):N0} cr)", () => career.BuyFuel(ship, career.TankCapacity - ship.Fuel), "primary");
                    Ui.B(row, "+20 t", () => career.BuyFuel(ship, 20));
                }
                BuildYard(c);

                Ui.Section(c, "Contract board (refreshes each day)");
                if (career.Board.Count == 0) Ui.L(c, "No work posted.", "t-note");
                foreach (var job in career.Board.ToArray())
                {
                    var j = job;
                    var dest = career.TargetOf(j);
                    if (dest == null) continue;
                    double d = Math.Max((sys.Pos(dest, jd) - ship.Pos).Mag - Ship.Standoff(dest), 0);
                    double a = career.AccelAt(ship.Mass + j.Load, j.Kind == JobKind.Passengers);
                    Career.Trip(d, a, 1, ship.Mass + j.Load, out double t, out _, out double fuel);
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
                    var cost = Ui.L(card, $"{Days(j.Days)} allowed · {Km(d)} · full burn at {Fmt.Num(a / Ship.G0, 2)} g: {Dur(t)}, {fuel:N1} t" + (overTank ? " (more than the tank: coast)" : ""), "t-note", "f-mono");
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
