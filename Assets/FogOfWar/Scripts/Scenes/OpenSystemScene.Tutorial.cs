using System;
using FogOfWar.OpenWorld;
using UnityEngine;
using A = FogOfWar.Controls.Action;
using Ctl = FogOfWar.Controls;

namespace FogOfWar
{
    // The first flight of a new career: target, Engage, time compression, payday, each step finished by doing it, with
    // the first job (medical supplies to Luna) flown by the autopilot. Skippable; it ends on a payday card. Hand flying
    // (turn, burn, holds, a beacon approach) is an optional practice, offered on that card and on the Jobs page.
    public partial class OpenSystemScene
    {
        // Lessons are saved by number (career.TutorialStep = lesson + 1): new ones go at the end. Flow and PracticeFlow
        // give the order they are taught in.
        enum Lesson { Turn, Assist, Burn, Hold, Target, Engage, Warp, Arrive, Done, Beacon, RefSwitch, Brake, Creep, Payday }
        static readonly Lesson[] Flow = { Lesson.Target, Lesson.Engage, Lesson.Warp };
        static readonly Lesson[] PracticeFlow = { Lesson.Turn, Lesson.Assist, Lesson.Burn, Lesson.Hold, Lesson.Beacon, Lesson.RefSwitch, Lesson.Brake, Lesson.Creep };
        static bool Practice(Lesson l) => Array.IndexOf(PracticeFlow, l) >= 0;
        static bool OnBeacon(Lesson l) => l >= Lesson.Beacon && l <= Lesson.Creep;
        static string Step(Lesson l) { var f = Practice(l) ? PracticeFlow : Flow; return $"({Array.IndexOf(f, l) + 1}/{f.Length})"; }
        Lesson lesson = Lesson.Done;
        bool turned, turnedAgain, burned;
        int doneBefore;                                  // jobs done while the first job was still active: more means it paid

        public string TutorialText { get; private set; } // read by the HUD; null when no lesson is running
        public bool InPractice => Practice(lesson);
        bool TutorialPaid => lesson == Lesson.Payday;

        void StartTutorial() { lesson = Lesson.Target; doneBefore = career.Done; career.TutorialStep = (int)lesson + 1; }

        // The hand-flying practice: anywhere in orbit, with no trip flying. The autopilot keeps its plan on once parked,
        // so a parked plan is turned off here. A landed ship has no orbit for the beacon.
        bool Parked => ship.Phase == "in orbit" || ship.Phase == "holding station";
        bool CanPractise => !career.Sandbox && (!ship.PlanActive || Parked) && ship.Landed == null && (lesson == Lesson.Done || lesson == Lesson.Payday);
        void StartPractice()
        {
            if (!CanPractise) return;
            if (ship.PlanActive) { ship.Plan = Plan.None; ship.Throttle = 0; }
            lesson = Lesson.Turn; turned = turnedAgain = burned = false;
            career.TutorialStep = (int)lesson + 1;
        }

        // After Continue: pick up the saved lesson, or stay quiet when the save had finished or skipped it (QA-051). The
        // beacon and the payday card aren't saved, and older saves hold steps of the longer first flight: anything but
        // a first-flight lesson goes back to the target step while the first job is aboard, and ends otherwise.
        void ResumeTutorial()
        {
            int step = career.TutorialStep;
            lesson = step > 0 && Enum.IsDefined(typeof(Lesson), step - 1) && !career.Sandbox ? (Lesson)(step - 1) : Lesson.Done;
            turned = turnedAgain = burned = false;
            doneBefore = career.Done;
            bool job = career.Active.Find(x => x.Tutorial) != null;
            if (Practice(lesson) || lesson == Lesson.Payday) lesson = job ? Lesson.Target : Lesson.Done;
            if (lesson == Lesson.Done) TutorialText = null;
        }

        void EndTutorial() { EndPractice(); lesson = Lesson.Done; TutorialText = null; career.TutorialStep = 0; }

        void TickTutorial()
        {
            if (lesson == Lesson.Done || career.Sandbox) { EndTutorial(); return; }
            if (!Practice(lesson) && lesson != Lesson.Payday)
            {
                // the first job gone: paid, it ends on the payday card; abandoned or failed, the tutorial just ends
                if (career.Active.Find(x => x.Tutorial) != null) doneBefore = career.Done;
                else if (career.Done > doneBefore) lesson = Lesson.Payday;
                else { EndTutorial(); return; }
            }
            // the beacon part is optional: picking another target or engaging a plan ends the practice
            if (OnBeacon(lesson) && (beacon == null || ship.Target != beacon || ship.PlanActive)) { EndTutorial(); return; }
            switch (lesson)
            {
                case Lesson.Turn:
                    if (ship.RotInput.sqrMagnitude > 0) turned = true;
                    if (turned && ship.RotInput.sqrMagnitude == 0) lesson = Lesson.Assist;
                    break;
                // done by doing it once more, so the text stays up long enough to read (audit #7)
                case Lesson.Assist:
                    if (ship.RotInput.sqrMagnitude > 0) turnedAgain = true;
                    if (turnedAgain && ship.RotInput.sqrMagnitude == 0 && ship.AngVel.sqrMagnitude < 1e-4f) lesson = Lesson.Burn;
                    break;
                case Lesson.Burn:
                    if (ship.Throttle > 0) burned = true;
                    if (burned && ship.ThrottleCmd == 0) lesson = Lesson.Hold;
                    break;
                case Lesson.Hold:
                    if (ship.Hold == Hold.Prograde) { lesson = Lesson.Beacon; SpawnBeacon(); }
                    break;
                // #27: a short manual approach to a beacon on the ship's own orbit
                case Lesson.Beacon:
                    if (BeaconClosing() >= 0.02 && ship.ThrottleCmd == 0 && ship.Throttle == 0) lesson = Lesson.RefSwitch;
                    break;
                case Lesson.RefSwitch:
                    if (ship.VelRef == beacon) lesson = Lesson.Brake;
                    break;
                case Lesson.Brake:
                    if (BeaconRel().Mag < 0.002 && ship.Throttle == 0) lesson = Lesson.Creep;
                    break;
                case Lesson.Creep:
                    if ((sys.Pos(beacon, jd) - ship.Pos).Mag < 0.5 && BeaconRel().Mag < 0.002 && ship.RcsInput.sqrMagnitude == 0)
                    {
                        EndTutorial();
                        career.Notes.Add(new Career.Note { Text = "Practice done: you flew an approach by hand", Good = true, Time = Time.realtimeSinceStartup });
                        return;
                    }
                    break;
                case Lesson.Target:
                    if (ship.Target == sys.Moon) lesson = Lesson.Engage;
                    break;
                case Lesson.Engage:
                    if (ship.PlanActive) lesson = Lesson.Warp;
                    break;
                case Lesson.Warp:
                    if (warpIdx >= 3) lesson = Lesson.Arrive;
                    break;
                // the card waits for its buttons; leaving the port dismisses it
                case Lesson.Payday:
                    if (career.Docked == null) { EndTutorial(); return; }
                    break;
            }
            TutorialText = Text(lesson);
            career.TutorialStep = (int)lesson + 1;
        }

        string Text(Lesson l)
        {
            switch (l)
            {
                case Lesson.Target:
                    return $"First flight {Step(Lesson.Target)}. Your first job: medical supplies to Luna. Set the Moon as the target: press {Ctl.Keys(A.NextTarget)} until it shows, click its label, or use Set target on the Jobs page ({Ctl.Keys(A.Jobs)}). Drag the mouse to look around.";
                case Lesson.Engage:
                    return $"{Step(Lesson.Engage)} Check the trip planner for time and fuel, then press {Ctl.Keys(A.Engage)}. The autopilot burns, flips at the midpoint and brakes.";
                case Lesson.Warp:
                    return $"{Step(Lesson.Warp)} Speed up time with {Ctl.Keys(A.WarpUp)} (try 1k×). Time compression saves your patience, not game days: deadlines run on the game clock.";
                case Lesson.Arrive:
                    return "The autopilot parks you in orbit. In orbit at a port you are docked, and the job pays.";
                case Lesson.Payday:
                    return $"First job paid. The board on the Jobs page ({Ctl.Keys(A.Jobs)}) has the next one. Cargo and passengers pay {Career.HandBonus * 100:0} % more when you fly the arrival by hand: practise hand flying now, or later from the Jobs page.";
                case Lesson.Turn:
                    return $"Hand-flying practice {Step(Lesson.Turn)}. Turn the ship: {Ctl.Keys(A.PitchUp)} / {Ctl.Keys(A.PitchDown)} pitch, {Ctl.Keys(A.YawLeft)} / {Ctl.Keys(A.YawRight)} yaw, {Ctl.Keys(A.RollLeft)} / {Ctl.Keys(A.RollRight)} roll.";
                case Lesson.Assist:
                    return $"{Step(Lesson.Assist)} Turn again, then let go. With flight assist on, the keys set how fast you turn, and the reaction wheels stop the ship when you release them; they run on power, so turning costs no fuel. With assist off, the ship keeps turning, as Newton says.";
                case Lesson.Burn:
                    return $"{Step(Lesson.Burn)} A short burn: {Ctl.Keys(A.ThrottleFull)} for full throttle, then {Ctl.Keys(A.ThrottleCut)} to cut it. At 1 g you gain 9.8 m/s every second, and every second costs fuel.";
                case Lesson.Hold:
                    return $"{Step(Lesson.Hold)} Press 2 to hold prograde: the nose follows your velocity. Keys 1–9 pick the other holds; 1 turns the hold off, and so does turning by hand.";
                case Lesson.Beacon:
                    return $"{Step(l)} A beacon sits 5 km ahead of you on your own orbit, and it is your target now (pick another target to end the practice). Press 8 to hold Target, and once the nose is on it give a short burn, until the target card's Closing reads 20 m/s or more. Then cut ({Ctl.Keys(A.ThrottleCut)}).";
                case Lesson.RefSwitch:
                    return $"{Step(l)} You are moving relative to two things now: the body you orbit (kilometres a second) and the beacon (a few metres a second). Press {Ctl.Keys(A.VelocityRef)} to measure against the beacon: the speed readout, prograde and retrograde then use it.";
                case Lesson.Brake:
                    return $"{Step(l)} Brake: press 3 to hold retrograde, against your motion relative to the beacon. When the nose has come round, burn. The card's Stop row says how long and how far a full burn takes; cut when you are under 2 m/s.";
                case Lesson.Creep:
                    return $"{Step(l)} Finish on RCS, which pushes without turning: {Ctl.Keys(A.RcsForward)}/{Ctl.Keys(A.RcsBack)} fore-aft, {Ctl.Keys(A.RcsLeft)}/{Ctl.Keys(A.RcsRight)} sideways, {Ctl.Keys(A.RcsUp)}/{Ctl.Keys(A.RcsDown)} up-down. Close to within 500 m and stop (under 2 m/s). Time compression ({Ctl.Keys(A.WarpUp)}) helps while you coast.";
            }
            return null;
        }

        // ---- the practice beacon: a massless marker coasting on the ship's own orbit, 5 km ahead (not saved)
        Body beacon, beforeBeacon; // the target the player had before the practice, given back after it
        const string BeaconName = "Practice beacon";

        void SpawnBeacon()
        {
            EndPractice();
            beforeBeacon = ship.Target;
            var dom = Ship.Dominant(sys, ship.Pos, jd);
            double jd0 = jd, mu = dom.GM;
            var r = ship.Pos - sys.Pos(dom, jd); var v = ship.Vel - sys.Vel(dom, jd);
            Rails.Propagate(r, v, mu, 5.0 / Math.Max(v.Mag, 1e-3), out var r0, out var v0);
            beacon = new Body
            {
                Name = BeaconName, Parent = dom, GM = 0, Radius = 0.01, Look = Look.Rocky,
                ColA = new Color(1f, 0.71f, 0.28f), ColB = new Color(1f, 0.85f, 0.5f),
                Path = j => { Rails.Propagate(r0, v0, mu, (j - jd0) * 86400, out var p, out _); return sys.Pos(dom, j) + p; },
            };
            sys.Bodies.Add(beacon);
            ship.Target = beacon;
        }

        void EndPractice()
        {
            if (beacon == null) return;
            sys.Bodies.Remove(beacon);
            if (ship.Target == beacon) ship.Target = beforeBeacon != null && sys.Bodies.Contains(beforeBeacon) ? beforeBeacon : null;
            if (ship.VelRef == beacon) ship.VelRef = null;
            beacon = null;
        }

        D3 BeaconRel() => beacon == null ? D3.Zero : ship.Vel - sys.Vel(beacon, jd);
        double BeaconClosing() => beacon == null ? 0 : D3.Dot(BeaconRel(), (sys.Pos(beacon, jd) - ship.Pos).Norm);

        GUIStyle lessonStyle;

        void DrawTutorial()
        {
            if (string.IsNullOrEmpty(TutorialText)) return;
            if (lessonStyle == null) lessonStyle = new GUIStyle { fontSize = 15, wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(14, 14, 10, 10), normal = { textColor = Color.white } };
            float ui = App.UiScale;
            float W = UnityEngine.Screen.width / ui, H = UnityEngine.Screen.height / ui;
            float free = W - App.PanelWidth - 32, w = Mathf.Min(620, free - 24);
            float h = lessonStyle.CalcHeight(new GUIContent(TutorialText), w);
            var r = new Rect((free - w) / 2 + 8, H - 110 - h - 30, w, h);
            GUI.color = new Color(0.04f, 0.05f, 0.09f, 0.85f); GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = PanelUI.Accent; GUI.DrawTexture(new Rect(r.x, r.y, 3, r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(r, TutorialText, lessonStyle);
            if (TutorialPaid)
            {
                if (GUI.Button(new Rect(r.xMax - 250, r.yMax + 4, 160, 22), "Practise hand flying")) StartPractice();
                if (GUI.Button(new Rect(r.xMax - 80, r.yMax + 4, 80, 22), "Done")) SkipTutorial();
            }
            else if (GUI.Button(new Rect(r.xMax - 90, r.yMax + 4, 90, 22), InPractice ? "End practice" : "Skip tutorial")) SkipTutorial();
        }
    }
}
