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
        enum Lesson { Turn, Assist, Burn, Hold, Target, Engage, Warp, Arrive, Done }
        Lesson lesson = Lesson.Done;
        bool turned, burned;

        public string TutorialText { get; private set; } // read by the HUD; null when no lesson is running

        void StartTutorial() { lesson = Lesson.Turn; turned = burned = false; }

        void TickTutorial()
        {
            if (lesson == Lesson.Done || career.Active.Find(x => x.Tutorial) == null) { lesson = Lesson.Done; TutorialText = null; return; }
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
                    if (ship.Hold == Hold.Prograde) lesson = Lesson.Target;
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
        }

        string Text(Lesson l)
        {
            switch (l)
            {
                case Lesson.Turn:
                    return $"First flight (1/7). Turn the ship: {Ctl.Keys(A.PitchUp)} / {Ctl.Keys(A.PitchDown)} pitch, {Ctl.Keys(A.YawLeft)} / {Ctl.Keys(A.YawRight)} yaw, {Ctl.Keys(A.RollLeft)} / {Ctl.Keys(A.RollRight)} roll. Drag the mouse to look around.";
                case Lesson.Assist:
                    return "(2/7) Let go. Flight assist fires the RCS to stop the rotation. With it off, the ship keeps turning, as Newton says.";
                case Lesson.Burn:
                    return $"(3/7) A short burn: {Ctl.Keys(A.ThrottleFull)} for full throttle, then {Ctl.Keys(A.ThrottleCut)} to cut it. At 1 g you gain 9.8 m/s every second, and every second costs reaction mass.";
                case Lesson.Hold:
                    return "(4/7) Press 2 to hold prograde: the nose follows your velocity. Keys 1–9 pick the other holds; 1 turns the hold off.";
                case Lesson.Target:
                    return $"(5/7) Your first job: medical supplies to Luna. Set the Moon as the target: press {Ctl.Keys(A.NextTarget)} until it shows, click its label, or use Set target in the panel.";
                case Lesson.Engage:
                    return $"(6/7) Check the trip planner for time and reaction mass, then press {Ctl.Keys(A.Engage)}. The autopilot burns, flips at the midpoint and brakes.";
                case Lesson.Warp:
                    return $"(7/7) Speed up time with {Ctl.Keys(A.WarpUp)} (try 1k×). Time compression saves your patience, not game days: deadlines run on the game clock.";
                case Lesson.Arrive:
                    return "The autopilot parks you in orbit. In orbit at a port you are docked, and the job pays.";
            }
            return null;
        }

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
