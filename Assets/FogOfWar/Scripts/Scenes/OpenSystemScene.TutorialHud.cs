using System.Text.RegularExpressions;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FogOfWar
{
    // The first-flight lesson as a UI Toolkit card above the flight cluster: step counter, progress pips, the
    // lesson text and a Skip button. Same lessons as DrawTutorial (OpenSystemScene.Tutorial.cs).
    public partial class OpenSystemScene
    {
        VisualElement lessonCard, lessonPips, lessonPaid;
        Label lessonStep, lessonText, lessonSkip;
        string lessonShown;
        static readonly Regex StepRx = new Regex(@"^(?:First flight |Hand-flying practice )?\((\d+)/(\d+)\)\.?\s*");

        void BuildLessonCard(VisualElement layer)
        {
            lessonCard = Ui.E(layer, "panel", "lesson");
            Ui.Corners(lessonCard);
            var head = Ui.E(lessonCard, "row");
            lessonStep = Ui.L(head, "", "t-micro", "f-condm", "accent");
            lessonStep.style.flexGrow = 1;
            lessonPips = Ui.E(head, "row");
            lessonPips.style.marginRight = 12;
            lessonSkip = Ui.B(head, "Skip tutorial", SkipTutorial, "small", "ghost");
            lessonSkip.style.marginBottom = 0;
            lessonText = Ui.L(lessonCard, "", "t-body", "lesson-text");
            // the payday card's answers: the hand-flying practice, or done
            lessonPaid = Ui.E(lessonCard, "row");
            lessonPaid.style.marginTop = 8;
            Ui.B(lessonPaid, "Practise hand flying", StartPractice, "small", "primary");
            Ui.B(lessonPaid, "Done", SkipTutorial, "small");
            Ui.Show(lessonCard, false);
            lessonShown = null;
        }

        void SkipTutorial() { lesson = Lesson.Done; TutorialText = null; }

        // review shots: the card's head, pip count, and which buttons show
        public string TestLessonCardReport() => lessonCard == null ? "none" :
            $"{lessonStep.text} | pips {lessonPips.childCount} | skip {lessonSkip.resolvedStyle.display == DisplayStyle.Flex} | answers {lessonPaid.resolvedStyle.display == DisplayStyle.Flex} | {lessonText.text}";

        void UpdateLessonCard()
        {
            if (lessonCard == null) return;
            var t = TutorialText;
            bool on = !string.IsNullOrEmpty(t) && pivot != Pivot.Sun;
            Ui.Show(lessonCard, on);
            // QA-059: in portrait the card sits under the right column; a tall card pushes the column's bottom up
            if (hudShell != null && hudShell.Portrait && rightCol != null)
            {
                float bottom = 350;
                var box = lessonCard.layout;
                if (on && lessonCard.parent != null && !float.IsNaN(box.y))
                    bottom = Mathf.Max(bottom, lessonCard.parent.layout.height - box.y + 12);
                rightCol.style.bottom = bottom;
            }
            if (!on || t == lessonShown) return;
            lessonShown = t;
            var m = StepRx.Match(t);
            int step = 0, of = InPractice ? PracticeFlow.Length : Flow.Length;
            if (m.Success) { step = int.Parse(m.Groups[1].Value); of = int.Parse(m.Groups[2].Value); t = t.Substring(m.Length); }
            string title = InPractice ? "HAND-FLYING PRACTICE" : "FIRST FLIGHT";
            Ui.Text(lessonStep, step > 0 ? $"{title}  ·  STEP {step} OF {of}" : TutorialPaid ? $"{title}  ·  PAID" : $"{title}  ·  ARRIVAL");
            Ui.Text(lessonSkip, InPractice ? "End practice" : "Skip tutorial");
            Ui.Show(lessonSkip, !TutorialPaid);
            Ui.Show(lessonPaid, TutorialPaid);
            lessonPips.Clear();
            for (int i = 1; i <= of; i++)
            {
                var p = Ui.E(lessonPips, "pip");
                Ui.SetClass(p, "done", step == 0 || i < step);
                Ui.SetClass(p, "now", i == step);
            }
            Ui.Text(lessonText, t);
            // a short fade each time the step changes
            lessonCard.RemoveFromClassList("in");
            lessonCard.schedule.Execute(() => lessonCard.AddToClassList("in")).StartingIn(16);
            Ui.Fontify(lessonCard);
        }
    }
}
