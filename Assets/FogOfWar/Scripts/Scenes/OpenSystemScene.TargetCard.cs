using System;
using FogOfWar.OpenWorld;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;
using A = FogOfWar.Controls.Action;

namespace FogOfWar
{
    // The target card: the selected target, how far and how fast, the trip there, and ENGAGE.
    public partial class OpenSystemScene
    {
        VisualElement tcBody, tcIcon;
        Label tcName, tcSub, tcDist, tcRel, tcClose, tcBrach, tcNav, tcEngage, tcNoTarget;
        Label tcDrift, tcStop, tcStopNote;

        void BuildTargetCard(VisualElement parent)
        {
            var p = Ui.Panel(parent, null, "Target", true);
            p.style.flexShrink = 0;
            p.style.marginBottom = 10;
            tcPanel = p;
            tcNoTarget = Ui.L(p, "No target. Press " + UiShell.Keys(A.NextTarget) + " or click a label.", "t-note");
            tcBody = Ui.E(p, "col");
            var head = Ui.E(tcBody, "row");
            tcIcon = KindIcon(head);
            tcName = Ui.L(head, "", "tc-name", "f-cond");
            tcName.style.flexGrow = 1;
            Ui.B(head, "◀", () => SetTarget(targets[(targetIdx - 1 + targets.Count) % targets.Count]), "small");
            Ui.B(head, "▶", NextTarget, "small");
            tcSub = Ui.L(tcBody, "", "t-label", "f-condm", "tc-sub");
            tcSub.style.marginBottom = 6;
            tcDist = Ui.KV(tcBody, "Distance").value;
            tcRel = Ui.KV(tcBody, "Relative speed").value;
            (tcCloseRow, tcClose) = Ui.KV(tcBody, "Closing");
            // #27: the approach aid. Sideways drift is what a straight-in brake leaves over; Stop is a flip-and-burn
            (tcDriftRow, tcDrift) = Ui.KV(tcBody, "Sideways drift");
            (tcStopRow, tcStop) = Ui.KV(tcBody, "Stop");
            tcStopNote = Ui.L(tcBody, "", "t-note");
            // the note explains the rows; it is read once, so it shows only while the pointer is on the card
            Ui.Show(tcStopNote, false);
            p.RegisterCallback<PointerEnterEvent>(_ => Ui.Show(tcStopNote, true));
            p.RegisterCallback<PointerLeaveEvent>(_ => Ui.Show(tcStopNote, false));
            BuildBrake(tcBody);
            Ui.E(tcBody, "rule");
            tcBrach = Ui.L(tcBody, "", "t-note", "f-mono");
            var nrow = Ui.E(tcBody, "row");
            nrow.style.marginTop = 6;
            tcNav = Ui.L(nrow, "", "t-label", "f-condm");
            tcNav.style.flexGrow = 1; tcNav.style.flexShrink = 1;
            tcEngage = Ui.B(nrow, "ENGAGE", TogglePlan, "primary", "f-cond");
            tcEngage.style.marginRight = 0; tcEngage.style.marginBottom = 0;
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
                Ui.Text(tcStopNote, $"Relative to {t.Name}. Stop: flip now, {by}, no gravity. Speed and holds measure against {VelRefBody.Name} ({UiShell.Keys(A.VelocityRef)} switches).");
                double d = Math.Max(r.Mag - Ship.Standoff(t), 0);
                // QA-062: inside the parking distance there is no trip to plan; short trips read in m/s
                bool dvShort = false;
                Ui.Text(tcBrach, d < 1 ? "Within parking distance: no brachistochrone needed." : BrachLine(d, out dvShort));
                Ui.SetClass(tcBrach, "warn", dvShort);
                Ui.Text(tcNav, ship.PlanActive ? "NAV  " + ship.Phase.ToUpperInvariant() : "NAV IDLE");
                Ui.SetClass(tcNav, "accent", ship.PlanActive);
                Ui.Text(tcEngage, ship.PlanActive ? "ABORT" : "ENGAGE");
                Ui.SetClass(tcEngage, "danger", ship.PlanActive);
            }
            else { flightMode = FlightMode.Cruise; stopCue = ""; brakeState = "no target"; }
            Ui.Show(tcPanel, cockpit == null || !cockpit.Visible || CardInCockpit);
        }
    }
}
