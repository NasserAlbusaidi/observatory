using System;
using FogOfWar.OpenWorld;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;
using A = FogOfWar.Controls.Action;

namespace FogOfWar
{
    // The target card: the selected target, how far and how fast, the trip there, and ENGAGE. Compact by default; the
    // details (kind and size, relative speed, stop, the full-burn line, the note) open with + or while the pointer is
    // on the card. They sit under ENGAGE, so opening them never moves the button under the pointer.
    public partial class OpenSystemScene
    {
        VisualElement tcBody, tcIcon, tcRelRow, tcMoreRule;
        Label tcName, tcSub, tcDist, tcRel, tcClose, tcTrip, tcBrach, tcNav, tcEngage, tcNoTarget, tcMore;
        Label tcDrift, tcStop, tcStopNote, tcJob;
        bool cardExpanded, cardHover;

        void BuildTargetCard(VisualElement parent)
        {
            var p = Ui.Panel(parent, null, "Target", true);
            p.style.flexShrink = 0;
            p.style.marginBottom = 10;
            tcPanel = p;
            p.RegisterCallback<PointerEnterEvent>(_ => cardHover = true);
            p.RegisterCallback<PointerLeaveEvent>(_ => cardHover = false);
            tcNoTarget = Ui.L(p, "No target. Press " + UiShell.Keys(A.NextTarget) + " or click a label.", "t-note");
            tcBody = Ui.E(p, "col");
            var head = Ui.E(tcBody, "row");
            tcIcon = KindIcon(head);
            tcName = Ui.L(head, "", "tc-name", "f-cond");
            tcName.style.flexGrow = 1;
            Ui.B(head, "◀", () => SetTarget(CycleTarget(-1)), "small");
            Ui.B(head, "▶", NextTarget, "small");
            tcMore = Ui.B(head, "+", () => cardExpanded = !cardExpanded, "small", "ghost");
            tcDist = Ui.KV(tcBody, "Distance").value;
            (tcCloseRow, tcClose) = Ui.KV(tcBody, "Closing");
            // #27: the approach aid. Sideways drift is what a straight-in brake leaves over
            (tcDriftRow, tcDrift) = Ui.KV(tcBody, "Sideways drift");
            BuildBrake(tcBody);
            Ui.E(tcBody, "rule");
            tcTrip = Ui.L(tcBody, "", "t-note", "f-mono");
            tcJob = Ui.L(tcBody, "", "t-label");
            tcJob.style.marginTop = 3; tcJob.style.whiteSpace = WhiteSpace.Normal;
            var nrow = Ui.E(tcBody, "row");
            nrow.style.marginTop = 6;
            tcNav = Ui.L(nrow, "", "t-label", "f-condm");
            tcNav.style.flexGrow = 1; tcNav.style.flexShrink = 1;
            tcEngage = Ui.B(nrow, "ENGAGE", TogglePlan, "primary", "f-cond");
            tcEngage.style.marginRight = 0; tcEngage.style.marginBottom = 0;

            // details
            tcMoreRule = Ui.E(tcBody, "rule");
            tcSub = Ui.L(tcBody, "", "t-label", "f-condm", "tc-sub");
            tcSub.style.marginBottom = 6;
            (tcRelRow, tcRel) = Ui.KV(tcBody, "Relative speed");
            // Stop is a flip-and-burn
            (tcStopRow, tcStop) = Ui.KV(tcBody, "Stop");
            tcBrach = Ui.L(tcBody, "", "t-note", "f-mono");
            tcStopNote = Ui.L(tcBody, "", "t-note");
        }

        // The one place that shows or hides a card row: from the flight mode, the details toggle (or the pointer) and a
        // lesson that names a row, which keeps that row on screen.
        void CardRows()
        {
            bool more = cardExpanded || cardHover;
            bool gauge = flightMode != FlightMode.Cruise;
            Ui.Text(tcMore, cardExpanded ? "–" : "+");
            // in approach and docking the gauge head carries the closing speed
            Ui.Show(tcCloseRow, !gauge || LessonNames("Closing"));
            Ui.Show(tcDriftRow, flightMode == FlightMode.Approach || more);
            Ui.Show(brakeBox, gauge);
            Ui.Show(dockSplit, flightMode == FlightMode.Docking);
            Ui.Show(tcMoreRule, more);
            Ui.Show(tcSub, more);
            Ui.Show(tcRelRow, more);
            Ui.Show(tcStopRow, more || LessonNames("Stop row"));
            Ui.Show(tcBrach, more);
            Ui.Show(tcStopNote, more);
        }

        void UpdateTargetCard()
        {
            var t = ship.Target;
            Ui.Show(tcNoTarget, t == null); Ui.Show(tcBody, t != null);
            if (t != null)
            {
                var tp = sys.Pos(t, jd);
                var r = tp - ship.Pos;
                var vr = ship.Vel - sys.Vel(t, jd);
                double closing = D3.Dot(vr, r.Norm);
                Ui.Text(tcName, t.Name.ToUpperInvariant());
                var kind = KindOf(t);
                SetKindIcon(tcIcon, kind);
                SetKindIcon(mkIcon, kind);
                Ui.Text(tcSub, Kind(t).ToUpperInvariant() + (kind != TargetKind.Body ? "" : t.SizeEstimated ? "  ·  SIZE UNKNOWN" : "  ·  R " + Km(t.Radius)));
                Ui.Text(tcDist, Km(r.Mag - t.Radius));
                Ui.Text(tcRel, SpeedText(vr.Mag));
                Ui.Text(tcClose, (closing >= 0 ? "+" : "") + SpeedText(closing));
                Ui.SetClass(tcClose, "cold", closing > 0);
                double drift = (vr - r.Norm * closing).Mag;
                Ui.Text(tcDrift, Spd(drift));
                StopEstimate(vr, out var aStop, out var tStop, out var dStop, out var by);
                double room = r.Mag - t.Radius;
                bool late = closing > 0 && dStop > room;
                Ui.Text(tcStop, vr.Mag < 1e-4 ? "at rest" : aStop > 0 ? Dur(tStop) + "  ·  " + Km(dStop) + (late ? "  TOO FAST" : "") : "can't");
                Ui.SetClass(tcStop, "warn", late);
                UpdateApproach(t, r, vr, closing, aStop, tStop, dStop, room);
                CardRows();
                Ui.Text(tcStopNote, $"Relative to {t.Name}. Stop: flip now, {by}, no gravity. Speed and holds measure against {VelRefBody.Name} ({UiShell.Keys(A.VelocityRef)} switches).");
                double d = Math.Max(r.Mag - Ship.Standoff(t), 0);
                // QA-062: inside the parking distance there is no trip to plan; short trips read in m/s
                bool dvShort = false;
                Ui.Text(tcBrach, d < 1 ? "Within parking distance: no burn needed." : BrachLine(d, out dvShort));
                Ui.SetClass(tcBrach, "warn", dvShort);
                Ui.Text(tcTrip, d < 1 ? "Within parking distance" : TripLine(dvShort));
                Ui.SetClass(tcTrip, "warn", dvShort);
                // the job bound here, with the trip planner's arrival against its deadline (as the planner's cards read it)
                var due = career == null || career.Sandbox ? null : career.Active.Find(j => career.TargetOf(j) == t);
                Ui.Show(tcJob, due != null);
                if (due != null)
                {
                    double late = jd - due.DeadlineJd;
                    if (d >= 1)
                    {
                        PlanTrip(out double secs, out _, out _);
                        late += secs / 86400 + (due.Kind == JobKind.Survey ? due.DwellNeeded / 86400 : 0);
                    }
                    Ui.Text(tcJob, $"{due.Title}  ·  due in {Days(Math.Max(due.DeadlineJd - jd, 0))}, " + (late > 0 ? $"{Days(late)} too late" : $"{Days(-late)} to spare"));
                    Ui.SetClass(tcJob, "ok", late <= 0);
                    Ui.SetClass(tcJob, "warn", late > 0);
                }
                Ui.Text(tcNav, ship.PlanActive ? "NAV  " + ship.Phase.ToUpperInvariant() : "NAV IDLE");
                Ui.SetClass(tcNav, "accent", ship.PlanActive);
                Ui.Text(tcEngage, ship.PlanActive ? "ABORT" : "ENGAGE");
                Ui.SetClass(tcEngage, "danger", ship.PlanActive);
            }
            else { flightMode = FlightMode.Cruise; stopCue = ""; brakeState = "no target"; }
            Ui.Show(tcPanel, cockpit == null || !cockpit.Visible || CardInCockpit);
        }

        // the trip the autopilot would fly (the trip planner's coast choice, parking included) in one line: its time,
        // and the fuel it burns (the speed change with an unmetered tank)
        string TripLine(bool dvShort)
        {
            PlanTrip(out double secs, out double dv, out double tonnes);
            string cost = ship.UnlimitedFuel ? BrachSpeed(dv) + " burn" : tonnes < 0.05 ? "< 0.1 t" : Fmt.Num(tonnes, 1) + " t";
            return (FlyingPlan ? "ETA " : "TRIP ") + Dur(secs) + "  ·  " + cost + (dvShort ? "  ·  FUEL SHORT" : "");
        }

        // review shots
        public void SetCardExpanded(bool on) => cardExpanded = on;

        public string TestCardRows()
        {
            if (tcBody == null || tcBody.resolvedStyle.display != DisplayStyle.Flex) return "no target";
            var rows = new System.Collections.Generic.List<string>();
            void Row(string name, VisualElement e) { if (e.resolvedStyle.display == DisplayStyle.Flex) rows.Add(name); }
            Row("Distance", tcDist.parent); Row("Closing", tcCloseRow); Row("Drift", tcDriftRow); Row("Gauge", brakeBox);
            Row("Trip", tcTrip); Row("Engage", tcEngage); Row("Kind", tcSub); Row("Rel", tcRelRow); Row("Stop", tcStopRow);
            Row("Brach", tcBrach); Row("Note", tcStopNote);
            return string.Join(" ", rows);
        }
    }
}
