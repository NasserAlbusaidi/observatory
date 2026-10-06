using System;
using FogOfWar.OpenWorld;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;
using A = FogOfWar.Controls.Action;
using Ctl = FogOfWar.Controls;

namespace FogOfWar
{
    // Flying keeps the screen clear: the ship console is a drawer (closed at the start, ` opens it), and the trading
    // and reading tasks have a full-screen page of their own (Jobs F3, Codex F4, Esc closes). The trip planner, which
    // is flight work, lives in the console's Nav tab; the first-job hint sits under the target card.
    public partial class OpenSystemScene
    {
        VisualElement page, pageJobs, pageCodex, firstJob, tripBox, tripLines;
        Segmented pageSeg, cruiseSeg;
        Label firstJobText;
        int pageView = -1; // -1 closed, 0 jobs, 1 codex
        string tripSig, lessonSeen;

        void BuildPage(VisualElement layer)
        {
            page = Ui.E(layer);
            page.style.position = Position.Absolute;
            page.style.left = 0; page.style.right = 0; page.style.top = 0; page.style.bottom = 0;
            page.style.backgroundColor = new Color(0.02f, 0.03f, 0.06f, 0.9f);
            page.style.alignItems = Align.Center;
            page.pickingMode = PickingMode.Position; // drags and clicks stop here, not at the flight camera
            var p = Ui.Panel(page, null, null, true);
            p.style.width = Length.Percent(100); p.style.maxWidth = 780;
            p.style.marginTop = 18; p.style.marginBottom = 56;
            p.style.flexGrow = 1; p.style.flexShrink = 1;
            p.style.backgroundColor = new Color(0.03f, 0.043f, 0.078f, 0.97f); // a page to read: the HUD must not show through
            var head = Ui.E(p, "row");
            head.style.alignItems = Align.Center;
            pageSeg = new Segmented(new[] { "Jobs  " + UiShell.Keys(A.Jobs), "Codex  " + UiShell.Keys(A.Codex) }, 0, OpenPage);
            pageSeg.style.marginBottom = 0;
            head.Add(pageSeg);
            Ui.E(head).style.flexGrow = 1;
            Ui.B(head, "Close  Esc", ClosePage, "small", "ghost");
            Ui.E(p, "rule");
            pageJobs = Ui.Scroll(JobsTab);
            pageCodex = Ui.Scroll(CodexTab);
            foreach (var b in new[] { pageJobs, pageCodex }) { b.style.flexGrow = 1; b.style.flexShrink = 1; p.Add(b); }
            Ui.Show(page, false);
        }

        public void OpenPage(int v) // review shots too
        {
            pageView = v;
            Ui.Show(page, true);
            Ui.Show(pageJobs, v == 0); Ui.Show(pageCodex, v == 1);
            pageSeg.Value = v;
            jobsSig = null; jobsT = 0; codexSig = null; codexT = 0;
        }

        public void ClosePage() { pageView = -1; Ui.Show(page, false); }

        public override bool CloseOverlay()
        {
            if (pageView < 0) return false;
            ClosePage();
            return true;
        }

        void SetConsole(bool open)
        {
            consoleOpen = open;
            Ui.Show(consoleBody, open);
            Ui.Text(consoleToggle, open ? "–" : "+");
        }

        public void ShowConsoleTab(int i) { SetConsole(true); consoleTabs?.Select(i); } // review shots
        public bool TestConsoleOpen => consoleOpen;

        // the drawer bar: its name and key, the two page buttons, and open / close
        void ConsoleHead(VisualElement p)
        {
            var head = Ui.E(p, "row");
            head.style.alignItems = Align.Center;
            Ui.L(head, "SHIP CONSOLE", "t-h3", "f-cond", "glow-ink");
            Ui.L(head, UiShell.Keys(A.Console), "t-micro", "f-condm").style.marginLeft = 8;
            Ui.E(head).style.flexGrow = 1;
            Ui.B(head, "JOBS  " + UiShell.Keys(A.Jobs), () => TogglePage(0), "small");
            Ui.B(head, "CODEX  " + UiShell.Keys(A.Codex), () => TogglePage(1), "small");
            consoleToggle = Ui.B(head, "+", () => SetConsole(!consoleOpen), "small", "ghost");
        }

        void TogglePage(int v) { if (pageView == v) ClosePage(); else OpenPage(v); }

        void BuildFirstJob(VisualElement parent)
        {
            firstJob = Ui.Panel(parent, null, null, false);
            firstJob.style.flexShrink = 0; firstJob.style.marginBottom = 10;
            firstJob.style.paddingTop = 8; firstJob.style.paddingBottom = 8;
            firstJob.style.borderLeftColor = new Color(1f, 0.71f, 0.28f); firstJob.style.borderLeftWidth = 2;
            Ui.L(firstJob, "FIRST JOB", "t-micro", "f-condm", "accent");
            firstJobText = Ui.L(firstJob, "", "t-body");
            Ui.Show(firstJob, false);
        }

        // the trip planner in the Nav tab: cruise choice, then time, Δv and reaction mass for the target with it
        void BuildTrip(VisualElement c)
        {
            tripBox = Ui.E(c, "col");
            Ui.Section(tripBox, "Trip planner");
            cruiseSeg = new Segmented(CruiseNames, cruiseIdx, i => { cruiseIdx = i; tripSig = null; });
            tripBox.Add(cruiseSeg);
            tripLines = Ui.E(tripBox, "col");
            tripSig = null;
        }

        void UpdateTrip()
        {
            bool on = career != null && !career.Sandbox && ship.Target != null;
            Ui.Show(tripBox, on);
            Ui.Show(navEst, !on);
            if (!on) return;
            cruiseSeg.Value = cruiseIdx;
            var sig = JobsSignature();
            if (sig == tripSig) return;
            tripSig = sig;
            tripLines.Clear();
            PlanTrip(out double t, out double dv, out double fuel);
            bool short_ = DvShort(dv);
            Ui.L(tripLines, $"{PlannerLead()} {Fmt.Num(ship.MaxG, 1)} g: {Dur(t)} with parking, {fuel:N1} t of fuel of {ship.Fuel:N1} t aboard", "t-note", "f-mono", short_ ? "warn" : "dim");
            if (short_) Ui.L(tripLines, "Not enough fuel. Coast, lower the g, or refuel.", "t-body", "warn");
            foreach (var j in career.Active)
            {
                if (career.TargetOf(j) != ship.Target) continue;
                double arrive = t / 86400 + (j.Kind == JobKind.Survey ? j.DwellNeeded / 86400 : 0);
                bool onTime = jd + arrive <= j.DeadlineJd;
                Ui.L(tripLines, $"{j.Title}: arrives with {Days(Math.Abs(j.DeadlineJd - jd - arrive))} {(onTime ? "to spare" : "too late")}", "t-body", onTime ? "ok" : "warn");
                // the time price: say whether this plan makes the express window, and by how much it misses
                if (j.ExpressJd > jd)
                {
                    double margin = j.ExpressJd - jd - arrive;
                    Ui.L(tripLines, margin >= 0 ? $"Express bonus +{j.Pay * Career.ExpressBonus:N0} cr: made with {Days(margin)} to spare"
                        : $"Misses the express bonus (+{j.Pay * Career.ExpressBonus:N0} cr) by {Days(-margin)}", "t-body", margin >= 0 ? "ok" : "dim");
                }
            }
            if (ship.PlanActive && ship.CruiseCap > 0) Ui.L(tripLines, $"Cruise speed {ship.CruiseCap:N0} km/s", "t-note");
            Ui.L(tripLines, "A gentler burn or a coast saves fuel, but takes longer and can miss the express bonus.", "t-note");
            Ui.Fontify(tripLines);
        }

        // keys, the open page, the first-job hint and a lesson that sends the pilot to the trip planner
        void UpdatePages()
        {
            if (!App.KeysQuiet)
            {
                if (Ctl.Pressed(A.Console)) SetConsole(!consoleOpen);
                if (Ctl.Pressed(A.Jobs)) TogglePage(0);
                if (Ctl.Pressed(A.Codex)) TogglePage(1);
            }
            if (pageView == 0) UpdateJobs();
            if (pageView == 1) UpdateCodex();

            string hint = career != null && !career.Sandbox && string.IsNullOrEmpty(TutorialText) ? FirstJobHint() : null;
            Ui.Show(firstJob, hint != null);
            if (hint != null) Ui.Text(firstJobText, hint);

            // once per lesson, so the pilot can close the drawer again
            if (TutorialText != lessonSeen)
            {
                lessonSeen = TutorialText;
                if (LessonNames("trip planner")) ShowConsoleTab(1);
            }
        }
    }
}
