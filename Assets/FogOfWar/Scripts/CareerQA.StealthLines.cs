using System;
using System.Collections;
using FogOfWar.OpenWorld;
using UnityEngine;
using UIE = UnityEngine.UIElements;

namespace FogOfWar
{
    // 16k and 16l (#167, #169): the planner adds the raider's hold before a quiet watch's dwell (the helper in both shells;
    // the trip card and the target card on the UI Toolkit HUD), and the help sheet has the dark-coast line. The classic
    // panel's planner and help are GUILayout calls that no QA reads.
    public partial class CareerQA
    {
        IEnumerator StealthLineChecks()
        {
            bool shell = !UI.UiShell.Disabled;
            if (shell && !app.ShellActive) { Check(false, "stealth lines: no UI Toolkit shell, and -fowlegacyui is not on"); yield break; }
            if (!shell) log.WriteLine("  SKIP stealth lines: no UI Toolkit shell (-fowlegacyui): the trip card, the target card and the help sheet; the classic panel draws them with GUILayout and no QA reads IMGUI text. The arrival helper runs alone");
            bool classicWas = GameSettings.ClassicPanel;
            ShipBuild build0 = null, stock = null;
            Body target0 = null;
            int cruise0 = 0;
            double fuel0 = 0, sink0 = 0;
            Contract quiet = null, plain = null;
            bool go = Guard("stealth lines setup", () =>
            {
                build0 = career.Build.Clone(); sink0 = career.SinkK; fuel0 = ship.Fuel;
                target0 = ship.Target; cruise0 = Get<int>("cruiseIdx");
                if (shell && classicWas) GameSettings.ClassicPanel = false; // the trip and target cards are on the UI Toolkit HUD
                career.DropContact();
                Put("warpIdx", 0);
                // 1,000,000 km out from Pallas at rest with it: a short trip (about 5.6 h at 1 g) that no tank is short for
                var pallas = sys[Career.ReconBody];
                D3 pP = sys.Pos(pallas, jd);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0; ship.Landed = null; ship.Hold = Hold.Off;
                ship.Pos = pP + pP.Norm * 1e6; ship.Vel = sys.Vel(pallas, jd);
                stock = build0.Clone();
                stock.pick[(int)Slot.Radiators] = 0;
                career.Build = stock;
                career.Apply(ship);
                ship.Fuel = career.TankCapacity;
                career.SinkK = 0;
                ship.LastLoudJd = double.NegativeInfinity;
                ship.Target = pallas;
                Put("cruiseIdx", 0);
                quiet = new Contract { Id = 900700, Kind = JobKind.Survey, Title = "QA quiet watch", From = "Ceres", To = Career.ReconBody, Pay = 9000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, DwellNeeded = Career.DwellSeconds, Quiet = true, Key = "qa/lines-quiet" };
                plain = new Contract { Id = 900701, Kind = JobKind.Survey, Title = "QA plain survey", From = "Ceres", To = Career.ReconBody, Pay = 9000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, DwellNeeded = Career.DwellSeconds, Quiet = false, Key = "qa/lines-plain" };
            });

            if (go) go = Guard("stealth lines helper", () =>
            {
                var cargo = new Contract { Kind = JobKind.Cargo };
                double h = career.HoldS(ship), q = s.AfterArrivalS(quiet), p = s.AfterArrivalS(plain), c = s.AfterArrivalS(cargo);
                Check(Near(h, Signature.TrackHoldS, 1e-9) && Near(q, Career.DwellSeconds + Signature.TrackHoldS, 1e-9) && Near(p, Career.DwellSeconds, 1e-9) && c == 0,
                    $"stealth lines: stock radiators, after the arrival: quiet watch {q:0.000} s (14,400: dwell and the 2 h hold), plain survey {p:0.000} s (7,200), cargo {c} s (0); hold {h:0.000} s (7,200)");
                var dark = stock.Clone();
                dark.pick[(int)Slot.Radiators] = 1;
                career.Build = dark; career.Apply(ship); ship.Fuel = career.TankCapacity; career.SinkK = 0;
                double hd = career.HoldS(ship), qd = s.AfterArrivalS(quiet), pd = s.AfterArrivalS(plain);
                Check(Near(hd, Signature.TrackHoldS * dark.StowRho, 1e-9) && Near(qd, Career.DwellSeconds + hd, 1e-9) && Near(pd, Career.DwellSeconds, 1e-9),
                    $"stealth lines: stowable radiators, after the arrival: quiet watch {qd:0.000} s (9,360: dwell and the 36 min hold), plain survey {pd:0.000} s (7,200); hold {hd:0.000} s (2,160)");
                career.Build = stock; career.Apply(ship); ship.Fuel = career.TankCapacity; career.SinkK = 0;
            });

            for (int i = 0; go && shell && i < 3; i++) yield return null;
            if (go && shell) Guard("stealth lines planner", () =>
            {
                var pt = new object[] { 0.0, 0.0, 0.0, -1.0 };
                Call("PlanTrip", pt); // Invoke writes the out values back into pt
                double t = (double)pt[0], h = career.HoldS(ship);
                double deadline = jd + (t + Career.DwellSeconds + h / 2) / 86400; // half a hold past arrival + dwell, so only the hold decides the line
                var tripLines = Get<UIE.VisualElement>("tripLines");
                var tcJob = Get<UIE.Label>("tcJob");
                if (tripLines == null || tcJob == null) { Check(false, $"stealth lines: the UI Toolkit HUD has no trip lines ({tripLines != null}) or target card job line ({tcJob != null})"); return; }
                (string trip, string card) Read(Contract j)
                {
                    j.DeadlineJd = deadline;
                    career.Active.Insert(0, j); // the first job bound for the target is the cards' "due"
                    try
                    {
                        Put("tripSig", null);
                        Call("UpdateTrip");
                        var first = UIE.UQueryExtensions.Q(tripLines, null, "trip-card"); // static form: this file has only the UIE alias
                        var last = first == null || first.childCount == 0 ? null : first[first.childCount - 1] as UIE.Label;
                        Call("UpdateTargetCard");
                        return (last?.text ?? "-", tcJob.text);
                    }
                    finally { career.Active.Remove(j); }
                }
                var (qTrip, qCard) = Read(quiet);
                var (pTrip, pCard) = Read(plain);
                bool qLate = qTrip.EndsWith("too late", StringComparison.Ordinal) && qCard.EndsWith("too late", StringComparison.Ordinal);
                bool pOk = pTrip.EndsWith("to spare", StringComparison.Ordinal) && pCard.EndsWith("to spare", StringComparison.Ordinal);
                Check(qLate && pOk,
                    $"stealth lines: due {(t + Career.DwellSeconds + h / 2) / 3600:0.00} h out (trip {t / 3600:0.00} h, dwell 2 h, half the {h / 3600:0.#} h hold): quiet watch trip card \"{qTrip}\", target card \"{qCard}\" (too late); plain survey \"{pTrip}\", \"{pCard}\" (to spare)");
                Put("tripSig", null);
            });

            if (go && shell) { app.HelpOpen = true; for (int i = 0; i < 2; i++) yield return null; }
            if (go && shell) Guard("stealth lines help", () =>
            {
                var root = (UIE.VisualElement)typeof(UI.UiShell).GetField("helpRoot", F).GetValue(UI.UiShell.Instance);
                int n = root == null ? -1 : CountText(root, Career.DarkCoastHelp);
                Check(app.InGame && n == 1, $"stealth lines: the help sheet in the game has the dark-coast line {n} time(s) (1): \"{Career.DarkCoastHelp}\"");
            });
            app.HelpOpen = false;

            Guard("stealth lines cleanup", () =>
            {
                if (quiet != null) career.Active.Remove(quiet);
                if (plain != null) career.Active.Remove(plain);
                if (build0 != null) { career.Build = build0; career.SinkK = sink0; career.Apply(ship); ship.Fuel = fuel0; }
                var vesta = sys["Vesta"];
                D3 pV = sys.Pos(vesta, jd);
                ship.Pos = pV + pV.Norm * 1e6; ship.Vel = sys.Vel(vesta, jd); // where 16h's cleanup leaves the ship
                ship.Target = target0; // not SetTarget: SetTarget(null) throws when the camera pivots on the target
                Put("cruiseIdx", cruise0);
                Put("tripSig", null);
                ship.LastLoudJd = double.NegativeInfinity;
                if (GameSettings.ClassicPanel != classicWas) GameSettings.ClassicPanel = classicWas;
            });
            yield return null;
        }
    }
}
