using System;
using FogOfWar.OpenWorld;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FogOfWar
{
    // The HUD follows what the pilot does with the target. Cruise: the card keeps to distance and the trip. Approach
    // (the stop distance is a real part of the range): a brake gauge in the card, from the ship to the target with a
    // mark at the stop distance. Docking (under 10 km): the relative velocity split along the ship's own axes, for RCS.
    // Target-relative prograde and retrograde markers show whenever there is a target; the retrograde one is also the
    // stop marker: point the nose at it and burn to stop against the target.
    public partial class OpenSystemScene
    {
        enum FlightMode { Cruise, Approach, Docking }
        FlightMode flightMode;
        const double DockKm = 10;
        // braking at full drive from the moment the stop distance is 80 % of the range leaves a 20 % margin
        const double BurnMargin = 0.8;

        VisualElement tcPanel, tcCloseRow, tcDriftRow, tcStopRow, brakeBox, brakeFill, brakeMark;
        Label brakeHead, brakeClose, brakeNote, dockSplit;
        VisualElement mkTPro, mkTRetro, mkTRetroDue;
        Label mkTRetroLabel;
        string brakeState = "", stopCue = "";

        static readonly Color TgtCol = new Color(0.41f, 0.82f, 1f, 0.95f);

        void BuildBrake(VisualElement parent)
        {
            brakeBox = Ui.E(parent, "col");
            brakeBox.style.marginTop = 6;
            var head = Ui.E(brakeBox, "row");
            head.style.alignItems = Align.FlexEnd;
            brakeHead = Ui.L(head, "APPROACH", "t-micro", "f-condm");
            brakeHead.style.flexGrow = 1;
            brakeClose = Ui.L(head, "", "t-h2", "f-mono", "cold");
            var bar = Ui.E(brakeBox, "thr");
            bar.style.marginTop = 6;
            brakeFill = Ui.E(bar, "fill");
            brakeMark = Ui.E(bar, "cmd");
            brakeNote = Ui.L(brakeBox, "", "t-label", "f-mono");
            brakeNote.style.whiteSpace = WhiteSpace.Normal;
            dockSplit = Ui.L(brakeBox, "", "t-label", "f-mono", "cold");
            dockSplit.style.marginTop = 4;
            Ui.Show(brakeBox, false);
        }

        void BuildTargetMarkers()
        {
            mkTPro = Mk();
            Ring(mkTPro, 20, TgtCol);
            Bar(mkTPro, 0, -14, 2, 7, TgtCol); Bar(mkTPro, -14, 0, 7, 2, TgtCol); Bar(mkTPro, 14, 0, 7, 2, TgtCol);
            Bar(mkTPro, 0, 0, 4, 4, TgtCol);
            TgtTag(mkTPro, "TGT");
            mkTRetro = Mk();
            Ring(mkTRetro, 20, TgtCol);
            Bar(mkTRetro, 0, 0, 18, 2, TgtCol, 45); Bar(mkTRetro, 0, 0, 18, 2, TgtCol, -45);
            Bar(mkTRetro, 0, 14, 2, 7, TgtCol); Bar(mkTRetro, -14, 0, 7, 2, TgtCol); Bar(mkTRetro, 14, 0, 7, 2, TgtCol);
            // the burn cue: a second ring that blinks when it is time to brake
            mkTRetroDue = Ring(mkTRetro, 40, new Color(1f, 0.71f, 0.28f, 1f), 3);
            mkTRetroLabel = TgtTag(mkTRetro, "STOP");
        }

        Label TgtTag(VisualElement m, string text)
        {
            var l = Ui.L(m, text, "t-micro", "f-condm");
            l.pickingMode = PickingMode.Ignore;
            l.style.position = Position.Absolute;
            l.style.left = -80; l.style.width = 160; l.style.top = 22;
            l.style.unityTextAlign = TextAnchor.UpperCenter;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.color = TgtCol;
            return l;
        }

        void UpdateTargetMarkers(bool flying)
        {
            var t = ship.Target;
            var vr = t != null ? ship.Vel - sys.Vel(t, jd) : D3.Zero;
            // 1 cm/s: below that the direction is noise. Against the body we orbit they sit on the orbit markers.
            bool on = flying && t != null && t != reference && vr.Mag > 1e-5;
            if (!on) { Ui.Show(mkTPro, false); Ui.Show(mkTRetro, false); return; }
            PlaceDir(mkTPro, Frame.U(vr), out _);
            PlaceDir(mkTRetro, Frame.U(-vr), out _);
            bool due = stopCue == "now" || stopCue == "late";
            Ui.Show(mkTRetroDue, due && Mathf.Repeat(Time.unscaledTime, 0.8f) < 0.5f);
            if (due)
            {
                var c = stopCue == "late" ? new Color(1f, 0.42f, 0.37f, 1f) : new Color(1f, 0.71f, 0.28f, 1f);
                mkTRetroDue.style.borderTopColor = mkTRetroDue.style.borderBottomColor = mkTRetroDue.style.borderLeftColor = mkTRetroDue.style.borderRightColor = c;
            }
            Ui.Text(mkTRetroLabel, stopCue == "late" ? "STOP · TOO FAST" : stopCue == "now" ? "STOP · BURN NOW" : "STOP");
        }

        // Called from UpdateTargetCard with the card's own numbers; sets the mode, the brake gauge and the stop cue.
        // CardRows decides which rows show.
        void UpdateApproach(Body t, D3 r, D3 vr, double closing, double aStop, double tStop, double dStop, double room)
        {
            bool atRest = vr.Mag < 1e-4;
            double ratio = room > 0 && aStop > 0 ? dStop / room : double.PositiveInfinity;
            // the autopilot flies its own braking curve, so a plan in flight is never "approach"
            bool approach = !atRest && closing > 0 && !ship.PlanActive && ratio > (flightMode == FlightMode.Approach ? 0.1 : 0.2);
            flightMode = room < DockKm ? FlightMode.Docking : approach ? FlightMode.Approach : FlightMode.Cruise;

            stopCue = "";
            if (flightMode == FlightMode.Cruise) { brakeState = "cruise"; return; }

            Ui.Text(brakeHead, flightMode == FlightMode.Docking ? "DOCKING" : "APPROACH");
            Ui.Text(brakeClose, atRest ? "at rest" : (closing >= 0 ? "+" : "") + SpeedText(closing));
            // the bar is the range to the target; the white mark is where a full burn started now would stop
            float frac = double.IsInfinity(ratio) || atRest ? 0 : Mathf.Clamp01((float)ratio);
            brakeFill.style.width = Length.Percent(frac * 100);
            brakeMark.style.left = Length.Percent(frac * 100);
            bool late = closing > 0 && ratio > 1;
            bool now = closing > 0 && !late && ratio >= BurnMargin;
            var fill = late ? new Color(1f, 0.42f, 0.37f, 0.9f) : now ? new Color(1f, 0.71f, 0.28f, 0.9f) : new Color(0.49f, 1f, 0.7f, 0.75f);
            brakeFill.style.backgroundColor = fill;
            string note;
            if (atRest) note = "At rest against " + t.Name + ".";
            else if (closing <= 0) note = "Moving away: opening at " + SpeedText(-closing) + ".";
            else if (aStop <= 0) note = "No way to brake: no reaction mass.";
            else if (late) note = $"TOO FAST: a full burn now stops {Km(dStop - room)} past the target.";
            else
            {
                double tBurn = (room - dStop / BurnMargin) / closing;
                note = (now ? "BURN NOW" : "Burn in " + Dur(tBurn)) + $"  ·  stop {Km(dStop)} of {Km(room)}, {Dur(tStop)}";
            }
            Ui.Text(brakeNote, note);
            Ui.SetClass(brakeNote, "warn", late);
            Ui.SetClass(brakeNote, "accent", now);
            stopCue = late ? "late" : now ? "now" : "";
            brakeState = flightMode + " " + (late ? "late" : now ? "now" : "early") + " " + Mathf.RoundToInt(frac * 100) + "%";

            // docking: velocity against the target along the ship's axes, the way the RCS keys push
            if (flightMode == FlightMode.Docking)
            {
                double fwd = D3.Dot(vr, ship.Heading);
                double right = D3.Dot(vr, Frame.FromU(ship.Att * Vector3.right).Norm);
                double up = D3.Dot(vr, Frame.FromU(ship.Att * Vector3.up).Norm);
                Ui.Text(dockSplit, $"FWD {Signed(fwd)}  ·  RIGHT {Signed(right)}  ·  UP {Signed(up)}");
            }
        }

        // a lesson that tells the pilot to read a card row keeps that row on screen
        bool LessonNames(string row) => TutorialText != null && TutorialText.Contains(row);

        static string Signed(double kms) { string s = Spd(kms); return kms > 0 && s != "0 m/s" ? "+" + s : s; }

        // cockpit: the MFDs carry the target page, so the card shows only when it has the brake gauge
        bool CardInCockpit => flightMode != FlightMode.Cruise || LessonNames("card");

        // review shots: put the ship `km` from the target's surface, closing at `kms`, with `driftKms` sideways
        public void TestApproachTarget(double km, double kms, double driftKms = 0)
        {
            var t = ship.Target;
            if (t == null) return;
            var tp = sys.Pos(t, jd);
            var dir = (ship.Pos - tp).Mag > 1e-6 ? (ship.Pos - tp).Norm : new D3(1, 0, 0);
            var side = D3.Cross(dir, new D3(0, 0, 1));
            if (side.Sqr < 1e-9) side = D3.Cross(dir, new D3(0, 1, 0));
            ship.Pos = tp + dir * (t.Radius + km);
            ship.Vel = sys.Vel(t, jd) - dir * kms + side.Norm * driftKms;
            ship.Plan = Plan.None;
        }

        // review shots: orbit camera around the ship, looking straight away from the target (at the stop marker)
        public void TestLookAwayFromTarget()
        {
            if (ship.Target == null) return;
            SetPivot(Pivot.Ship, 0.16);
            camMode = CamMode.Orbit;
            var off = Frame.U(sys.Pos(ship.Target, jd) - ship.Pos).normalized;
            yaw = Mathf.Atan2(off.x, -off.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(off.y, -1, 1)) * Mathf.Rad2Deg;
        }

        public string TestStopMarkerReport() => mkTRetro != null && mkTRetro.resolvedStyle.display == DisplayStyle.Flex ? "stop marker on: " + mkTRetroLabel.text : "stop marker hidden";

        public bool TestCardShown => tcPanel != null && tcPanel.resolvedStyle.display == DisplayStyle.Flex;

        // review shots: the mode and the gauge in one line
        public string TestApproachReport() => brakeState + " | " + (brakeNote?.text ?? "") + " | " + (dockSplit != null && dockSplit.resolvedStyle.display == DisplayStyle.Flex ? dockSplit.text : "no split");
    }
}
