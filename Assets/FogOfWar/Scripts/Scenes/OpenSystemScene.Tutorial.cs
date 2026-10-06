using System;
using FogOfWar.OpenWorld;
using UnityEngine;
using A = FogOfWar.Controls.Action;
using Ctl = FogOfWar.Controls;

namespace FogOfWar
{
    // The first flight of a new career: a short sequence of steps, each finished by doing it, that ends with the
    // first job (medical supplies to Luna) flown by the autopilot. Skippable; it ends when the first job is done.
    public partial class OpenSystemScene
    {
        // New lessons go after Done so saved step numbers keep their meaning; Flow gives the order they are taught in.
        enum Lesson { Turn, Assist, Burn, Hold, Target, Engage, Warp, Arrive, Done, Beacon, RefSwitch, Brake, Creep }
        static readonly Lesson[] Flow = { Lesson.Turn, Lesson.Assist, Lesson.Burn, Lesson.Hold, Lesson.Beacon, Lesson.RefSwitch, Lesson.Brake, Lesson.Creep,
            Lesson.Target, Lesson.Engage, Lesson.Warp };
        static string Step(Lesson l) => $"({Array.IndexOf(Flow, l) + 1}/{Flow.Length})";
        static bool Practice(Lesson l) => l >= Lesson.Beacon;
        Lesson lesson = Lesson.Done;
        bool turned, burned;

        public string TutorialText { get; private set; } // read by the HUD; null when no lesson is running

        void StartTutorial() { lesson = Lesson.Turn; turned = burned = false; career.TutorialStep = 1; }

        // After Continue: pick up the saved lesson, or stay quiet when the save had finished or skipped it (QA-051).
        void ResumeTutorial()
        {
            int step = career.TutorialStep;
            lesson = step > 0 && Enum.IsDefined(typeof(Lesson), step - 1) && !career.Sandbox ? (Lesson)(step - 1) : Lesson.Done;
            turned = burned = false;
            // the beacon isn't saved: a practice picks up from its start with a new one ahead of the ship
            if (Practice(lesson)) { lesson = Lesson.Beacon; SpawnBeacon(); }
            if (lesson == Lesson.Done) TutorialText = null;
        }

        void TickTutorial()
        {
            if (lesson == Lesson.Done || career.Sandbox || career.Active.Find(x => x.Tutorial) == null) { EndPractice(); lesson = Lesson.Done; TutorialText = null; career.TutorialStep = 0; return; }
            // the practice is optional: picking another target (the Moon job) or engaging a plan ends it, and the
            // lessons carry on from there
            if (Practice(lesson) && (beacon == null || ship.Target != beacon || ship.PlanActive)) { EndPractice(); lesson = Lesson.Target; }
            switch (lesson)
            {
                case Lesson.Turn:
                    if (ship.RotInput.sqrMagnitude > 0) turned = true;
                    if (turned && ship.RotInput.sqrMagnitude == 0) lesson = Lesson.Assist;
                    break;
                case Lesson.Assist:
                    if (ship.AngVel.sqrMagnitude < 1e-4f) lesson = Lesson.Burn;
                    break;
                case Lesson.Burn:
                    if (ship.Throttle > 0) burned = true;
                    if (burned && ship.ThrottleCmd == 0) lesson = Lesson.Hold;
                    break;
                case Lesson.Hold:
                    if (ship.Hold == Hold.Prograde) { lesson = Lesson.Beacon; SpawnBeacon(); }
                    break;
                // #27: a short manual approach to a beacon on the ship's own orbit, before the autopilot does it for real
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
                        EndPractice();
                        lesson = Lesson.Target;
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
            }
            TutorialText = Text(lesson);
            career.TutorialStep = (int)lesson + 1;
        }

        string Text(Lesson l)
        {
            switch (l)
            {
                case Lesson.Turn:
                    return $"First flight {Step(Lesson.Turn)}. Turn the ship: {Ctl.Keys(A.PitchUp)} / {Ctl.Keys(A.PitchDown)} pitch, {Ctl.Keys(A.YawLeft)} / {Ctl.Keys(A.YawRight)} yaw, {Ctl.Keys(A.RollLeft)} / {Ctl.Keys(A.RollRight)} roll. Drag the mouse to look around.";
                case Lesson.Assist:
                    return $"{Step(Lesson.Assist)} Let go. With flight assist on, the keys set how fast you turn, and the reaction wheels stop the ship when you release them; they run on power, so turning costs no reaction mass. With assist off, the ship keeps turning, as Newton says.";
                case Lesson.Burn:
                    return $"{Step(Lesson.Burn)} A short burn: {Ctl.Keys(A.ThrottleFull)} for full throttle, then {Ctl.Keys(A.ThrottleCut)} to cut it. At 1 g you gain 9.8 m/s every second, and every second costs reaction mass.";
                case Lesson.Hold:
                    return $"{Step(Lesson.Hold)} Press 2 to hold prograde: the nose follows your velocity. Keys 1–9 pick the other holds; 1 turns the hold off, and so does turning by hand.";
                case Lesson.Target:
                    return $"{Step(Lesson.Target)} Your first job: medical supplies to Luna. Set the Moon as the target: press {Ctl.Keys(A.NextTarget)} until it shows, click its label, or use Set target in the panel.";
                case Lesson.Engage:
                    return $"{Step(Lesson.Engage)} Check the trip planner for time and reaction mass, then press {Ctl.Keys(A.Engage)}. The autopilot burns, flips at the midpoint and brakes.";
                case Lesson.Warp:
                    return $"{Step(Lesson.Warp)} Speed up time with {Ctl.Keys(A.WarpUp)} (try 1k×). Time compression saves your patience, not game days: deadlines run on the game clock.";
                case Lesson.Beacon:
                    return $"{Step(l)} Practice first: a beacon sits 5 km ahead of you on your own orbit, and it is your target now (pick another target to skip the practice). Press 8 to hold Target, and once the nose is on it give a short burn, until the target card's Closing reads 20 m/s or more. Then cut ({Ctl.Keys(A.ThrottleCut)}).";
                case Lesson.RefSwitch:
                    return $"{Step(l)} You are moving relative to two things now: Earth (your orbit, kilometres a second) and the beacon (a few metres a second). Press {Ctl.Keys(A.VelocityRef)} to measure against the beacon: the speed readout, prograde and retrograde then use it.";
                case Lesson.Brake:
                    return $"{Step(l)} Brake: press 3 to hold retrograde, against your motion relative to the beacon. When the nose has come round, burn. The card's Stop row says how long and how far a full burn takes; cut when you are under 2 m/s.";
                case Lesson.Creep:
                    return $"{Step(l)} Finish on RCS, which pushes without turning: {Ctl.Keys(A.RcsForward)}/{Ctl.Keys(A.RcsBack)} fore-aft, {Ctl.Keys(A.RcsLeft)}/{Ctl.Keys(A.RcsRight)} sideways, {Ctl.Keys(A.RcsUp)}/{Ctl.Keys(A.RcsDown)} up-down. Close to within 500 m and stop (under 2 m/s). Time compression ({Ctl.Keys(A.WarpUp)}) helps while you coast.";
                case Lesson.Arrive:
                    return "The autopilot parks you in orbit. In orbit at a port you are docked, and the job pays.";
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
            if (GUI.Button(new Rect(r.xMax - 90, r.yMax + 4, 90, 22), "Skip tutorial")) { lesson = Lesson.Done; TutorialText = null; }
        }
    }
}
