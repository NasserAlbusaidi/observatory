using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FogOfWar.OpenWorld;
using UnityEngine;
using UnityEngine.UIElements;

namespace FogOfWar
{
    // 14b. The raider in both shells (OpenSystemScene.RaiderHud.cs): a raider call made in the scene 1,000,000 km out from
    // Vesta. The card's three answers and its live line, read again a frame after the raider moves; the pill; the hostile
    // target kind; the planet lists, the codex count, the flight log and the red dot; Give up, Run and Divert clicked on the
    // card; the Jobs page with the card up and after Run; T and the target card's arrows reaching the raider; the classic
    // panel's Divert, Give up and Run, taken through the hook that stands in for its button (classicClick), not by a mouse;
    // a card shown in the UI Toolkit HUD that is shown again after the classic panel goes on and off; and a two-answer call
    // that hides the third button. -fowlegacyui draws the same classic panel, but no run here starts in it. A throw is a
    // failed check.
    public partial class CareerQA
    {
        T R<T>(string method, params object[] a)
        {
            foreach (var m in s.GetType().GetMethods(F)) if (m.Name == method && m.GetParameters().Length == a.Length) return (T)m.Invoke(s, a);
            throw new MissingMethodException(method);
        }

        static object SceneStatic(string field) => typeof(OpenSystemScene).GetField(field, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        int ClassicIndex(Career.RouteAnswer what) => Array.FindIndex(R<(Career.RouteAnswer What, string Label)[]>("ChaseAnswers"), a => a.What == what);

        IEnumerator PirateShellChecks()
        {
            bool classicWas = GameSettings.ClassicPanel, labelsWas = GameSettings.ShowLabels;
            Contract job = null;
            Encounter e = null;
            Career.RouteCall call = null;
            int codex0 = 0;
            double pay = 0;
            bool clicked = false;
            bool go = Guard("shells setup", () =>
            {
                // the UI Toolkit HUD is built only with the classic panel off, and the dot is drawn only with labels on
                if (classicWas) GameSettings.ClassicPanel = false;
                if (!labelsWas) GameSettings.ShowLabels = true;
                career.DropContact();
                Put("warpIdx", 0);
                var vesta = sys["Vesta"];
                D3 pV = sys.Pos(vesta, jd), vV = sys.Vel(vesta, jd);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0; ship.Landed = null; ship.Hold = Hold.Off;
                ship.Pos = pV + pV.Norm * 1e6; ship.Vel = vV;
                Call("SetTarget", vesta);
                job = new Contract { Id = 900301, Kind = JobKind.Cargo, Title = "QA ice", From = "Ceres", To = "Vesta", Tonnes = 20, Pay = 20000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, Key = "qa/pirate-shells" };
                career.Active.Add(job);
                codex0 = R<int>("CodexBodies");
            });
            for (int i = 0; go && i < 3; i++) yield return null;

            // the call, and the card a frame later
            if (go) go = Guard("shells call", () =>
            {
                bool made = career.Docked == null && career.MakeCall(Career.RouteKind.Pirate, job, sys, ship, jd);
                e = career.Encounter; call = career.Call;
                Check(made && e != null && call?.Answers?.Length == 3, $"shells: a raider call in the scene ({call?.Title ?? "none"}, docked {career.Docked?.Name ?? "-"})");
            }) && e != null && call?.Answers?.Length == 3;
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("shells card", () =>
            {
                string rep = s.TestCallCardReport(), want = string.Join(" / ", Array.ConvertAll(call.Answers, a => a.Label));
                Check(rep.StartsWith($"card Flex | {want} | live {e.Proxy.Name} ") && rep.Contains(" km · closing ") && rep.EndsWith("boarding 0 %"),
                    $"shells: the call card has its three answers and the live line: {rep}");
                // the raider 2 km off and matched, half the boarding time alongside: the next frame's card reads it
                var off = (ship.Pos - sys.Pos(sys["Vesta"], jd)).Norm;
                e.Pirate.Pos = ship.Pos - off * 2; e.Pirate.Vel = ship.Vel;
                e.Dwell = Threat.BoardingSeconds / 2;
                Call("SetTarget", e.Proxy);
                s.TestAimTargetAt(0.35f); // the raider in the open, left of the right-hand column
            });
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) yield return Shot("c14b-raider-card");
            if (go) go = Guard("shells live", () =>
            {
                string rep = s.TestCallCardReport(), pill = s.TestRaiderPill();
                Check(rep.Contains($"| live {e.Proxy.Name} 2 km · ") && rep.EndsWith("boarding 50 %") && pill.StartsWith(e.Proxy.Name.ToUpperInvariant() + " 2 KM · ") && pill.EndsWith("BOARDING 50 %"),
                    $"shells: a frame after the raider closes, the card reads \"{rep.Substring(rep.IndexOf("| live", StringComparison.Ordinal) + 7)}\" and the pill \"{pill}\"");

                var kind = typeof(OpenSystemScene).GetMethod("KindOf", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { e.Proxy }).ToString();
                string sub = Get<Label>("tcSub").text;
                var arrow = Get<VisualElement>("tcIcon").Q(className: "ship");
                var red = ((Color[])SceneStatic("FactionColours"))[(int)Faction.Raiders];
                Check(kind == "Hostile" && sub.StartsWith("HOSTILE") && sub.Contains(Raiders.ToUpperInvariant()) && arrow.style.display.value == DisplayStyle.Flex && arrow.style.borderTopColor.value == red,
                    $"shells: the raider as the target is {kind}: \"{sub}\", a red arrowhead");

                var planets = R<List<Body>>("PlanetButtons");
                int codex = R<int>("CodexBodies");
                Check(sys.Bodies.Contains(e.Proxy) && !planets.Contains(e.Proxy) && codex == codex0,
                    $"shells: the proxy is in the system, in neither planet list ({planets.Count} planets), and the codex counts {codex} bodies as before the chase");

                var fields = (string[])SceneStatic("RecFields");
                var now = Get<string[]>("recNow");
                string rec = now.Length == fields.Length ? now[Array.IndexOf(fields, "raider")] : "length " + now.Length;
                string dot = Get<string>("raiderShown");
                Check(rec == e.Proxy.Name + " alongside" && dot.StartsWith($"on: {e.Proxy.Name} · 2 km"),
                    $"shells: the flight log's raider field reads \"{rec}\"; the red dot: {dot}");

                pay = job.Pay;
                clicked = s.TestClickCallButton(Array.FindIndex(call.Answers, a => a.What == Career.RouteAnswer.GiveUp));
            });
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("shells give up", () =>
            {
                string rep = s.TestCallCardReport(), pill = s.TestRaiderPill();
                Check(clicked && career.Encounter == null && career.Call == null && job.Pay == pay * (1 - Career.GiveUpShare) && rep.StartsWith("card None") && pill == "-"
                      && ship.Target == null && !sys.Bodies.Contains(e.Proxy),
                    $"shells: Give up clicked on the card: pay {pay:N0} → {job.Pay:N0} cr, the card and the pill gone, the raider target released ({rep})");

                // a second call: the Jobs page, which covers the card, shows the chase and the card's own answers
                e = null; call = null;
                bool made = career.MakeCall(Career.RouteKind.Pirate, job, sys, ship, jd);
                e = career.Encounter; call = career.Call;
                if (!made || e == null || call == null) { Check(false, "shells: a second raider call in the scene"); return; }
                var box = new VisualElement();
                Call("BuildJobs", box);
                var labels = box.Query<Label>(className: "btn").ToList().ConvertAll(l => l.text);
                bool warned = box.Query<Label>(className: "warn").ToList().Exists(l => l.text.StartsWith($"The {e.Hull} of the {Raiders} gives chase"));
                string sig0 = R<string>("JobsSignature");
                job.Pay -= 1;
                string sig1 = R<string>("JobsSignature");
                job.Pay += 1;
                Check(warned && Array.TrueForAll(call.Answers, a => labels.Contains(a.Label)) && sig0 != sig1,
                    $"shells: with the call card up, the Jobs page's card for the job warns of the chase and offers {string.Join(" / ", Array.ConvertAll(call.Answers, a => a.Label))}; a change of pay rebuilds the page");
            }) && e != null && call != null;
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("shells run", () => clicked = s.TestClickCallButton(Array.FindIndex(call.Answers, a => a.What == Career.RouteAnswer.Run)));
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("shells after run", () =>
            {
                string rep = s.TestCallCardReport(), pill = s.TestRaiderPill();
                Check(clicked && career.Call == null && career.Encounter == e && rep.StartsWith("card None") && pill.StartsWith(e.Proxy.Name.ToUpperInvariant()),
                    $"shells: Run clicked on the card closes it; the pill stays: {pill}");
                // the Jobs page's card for the job, built into a box of its own as the page builds it
                var box = new VisualElement();
                Call("BuildJobs", box);
                var labels = box.Query<Label>(className: "btn").ToList().ConvertAll(l => l.text);
                var chase = R<(Career.RouteAnswer What, string Label)[]>("ChaseAnswers");
                Check(chase.Length == 2 && labels.Contains(chase[0].Label) && labels.Contains(Career.GiveUpLabel) && !labels.Contains("Run") && chase[0].Label.StartsWith("Divert to "),
                    $"shells: after Run the job's card offers {string.Join(" and ", Array.ConvertAll(chase, a => a.Label))}");

                // T and the target card's arrows reach the raider: after the last body of the list, before the first
                var targets = Get<List<Body>>("targets");
                Call("SetTarget", targets[targets.Count - 1]);
                Call("NextTarget");
                bool toRaider = ship.Target == e.Proxy;
                Call("NextTarget");
                bool wrapped = ship.Target == targets[0];
                bool back = R<Body>("CycleTarget", -1) == e.Proxy;
                Check(toRaider && wrapped && back, $"shells: T goes from {targets[targets.Count - 1].Name} to the raider ({toRaider}) and on to {targets[0].Name} ({wrapped}); ◀ from there is the raider ({back})");
                Call("SetTarget", sys["Vesta"]);
                GameSettings.ClassicPanel = true;
            });
            for (int i = 0; go && i < 3; i++) yield return null;

            // the classic panel: the chase's answers with no card; Divert, Give up and Run through its hook
            if (go) go = Guard("shells classic chase", () =>
            {
                var chase = R<(Career.RouteAnswer What, string Label)[]>("ChaseAnswers");
                string drawn = Get<string>("classicDrawn");
                Check(drawn.StartsWith(string.Join(" / ", Array.ConvertAll(chase, a => a.Label)) + $" | {e.Proxy.Name} "),
                    $"shells: the classic panel draws the chase with no card: {drawn}");
                Put("classicClick", ClassicIndex(Career.RouteAnswer.Divert));
            });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) go = Guard("shells classic divert", () =>
            {
                // Earth orbit and Luna are about level by trip time from here: any patrolled port will do
                var port = Array.Find(Career.Ports, p => p.PatrolG > 0 && ship.Target != null && sys[p.Body] == ship.Target);
                Check(career.Encounter == e && port != null && ship.PlanActive && ship.Plan == Plan.Brachistochrone && Get<int>("classicClick") == -1,
                    $"shells: Divert from the classic panel flies to {port?.Name ?? "-"}, a patrolled port, on the Fast plan ({ship.Phase}); the chase goes on");
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0;
                pay = job.Pay;
                Put("classicClick", ClassicIndex(Career.RouteAnswer.GiveUp));
            });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) go = Guard("shells classic give up", () =>
            {
                string drawn = Get<string>("classicDrawn");
                Check(career.Encounter == null && job.Pay == pay * (1 - Career.GiveUpShare) && drawn == "" && Get<int>("classicClick") == -1,
                    $"shells: Give up from the classic panel: pay {pay:N0} → {job.Pay:N0} cr, the block gone (\"{drawn}\")");
                e = null; call = null;
                career.MakeCall(Career.RouteKind.Pirate, job, sys, ship, jd);
                e = career.Encounter; call = career.Call;
            }) && e != null && call != null;
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) yield return Shot("c14b-classic-call");
            if (go) go = Guard("shells classic call", () =>
            {
                string drawn = Get<string>("classicDrawn"), want = string.Join(" / ", Array.ConvertAll(call.Answers, a => a.Label));
                Check(drawn.StartsWith($"{want} | {e.Proxy.Name} ") && call.Answers.Length == 3,
                    $"shells: the classic panel draws the raider call's three answers and the live line: {drawn}");
                Put("classicClick", Array.FindIndex(call.Answers, a => a.What == Career.RouteAnswer.Run));
            });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) go = Guard("shells classic run", () =>
            {
                var chase = R<(Career.RouteAnswer What, string Label)[]>("ChaseAnswers");
                string drawn = Get<string>("classicDrawn");
                Check(career.Call == null && career.Encounter == e && drawn.StartsWith(string.Join(" / ", Array.ConvertAll(chase, a => a.Label)) + " | "),
                    $"shells: Run from the classic panel closes the call and the chase goes on: {drawn}");
                career.DropContact();
                GameSettings.ClassicPanel = false;
            });
            for (int i = 0; go && i < 3; i++) yield return null;

            // a call shown on the UI Toolkit card, the classic panel on and off with it still open: the HUD built anew shows it
            if (go) go = Guard("shells toolkit call", () =>
            {
                e = null; call = null;
                career.MakeCall(Career.RouteKind.Pirate, job, sys, ship, jd);
                e = career.Encounter; call = career.Call;
            }) && e != null && call != null;
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("shells toolkit shown", () =>
            {
                string rep = s.TestCallCardReport();
                Check(Get<Career.RouteCall>("callShown") == call && rep.StartsWith("card Flex"), $"shells: the call is on the UI Toolkit card ({rep.Substring(0, Math.Min(rep.Length, 40))}…)");
                GameSettings.ClassicPanel = true;
            });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) go = Guard("shells classic off", () => GameSettings.ClassicPanel = false);
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) go = Guard("shells rebuilt", () =>
            {
                string rep = s.TestCallCardReport();
                bool listed = false; int planetBtns = 0;
                foreach (var l in Get<VisualElement>("consoleBody").Query<Label>(className: "btn").ToList())
                    if (l.userData is Body b) { if (b == e.Proxy) listed = true; else if (b.Parent == sys.Sun && !b.Asteroid) planetBtns++; }
                Check(career.Call == call && rep.StartsWith("card Flex | Run / Divert to ") && !listed && planetBtns > 0,
                    $"shells: the HUD built again with the call it showed before still open shows the card ({rep.Substring(0, Math.Min(rep.Length, 60))}…), and its Targets tab has {planetBtns} planet buttons and none for the raider");
                clicked = s.TestClickCallButton(Array.FindIndex(call.Answers, a => a.What == Career.RouteAnswer.Divert));
            });
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("shells divert", () =>
            {
                var port = call.DivertTo;
                Check(clicked && career.Call == null && career.Encounter == e && port != null && ship.Target == sys[port.Body] && ship.PlanActive && ship.Plan == Plan.Brachistochrone,
                    $"shells: Divert clicked on the card flies to {port?.Name ?? "-"} on the Fast plan ({ship.Phase}); the chase goes on");
                career.DropContact();
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0;
                // a call with two answers: the third button hides
                career.MakeCall(Career.RouteKind.CargoTrouble, job, sys, ship, jd);
                call = career.Call;
            }) && call?.Kind == Career.RouteKind.CargoTrouble;
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("shells two answers", () =>
            {
                string rep = s.TestCallCardReport();
                Check(rep.StartsWith($"card Flex | {call.Answers[0].Label} / {call.Answers[1].Label} / - | live -"), $"shells: a two-answer call hides the third button and the live line: {rep}");
                clicked = s.TestClickCallButton(1);
            });
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) Guard("shells two answers clicked", () =>
                Check(clicked && career.Call == null && job.SpoilJd > 0 && s.TestCallCardReport().StartsWith("card None"),
                    $"shells: its second button answers it as before (push on: spoil clock {(job.SpoilJd - jd) * 24:F0} h) and the card closes"));

            Guard("shells cleanup", () =>
            {
                career.DropContact();
                if (career.Call != null) career.Answer(false, jd);
                career.Active.RemoveAll(c => c.Key == "qa/pirate-shells");
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0;
                Put("warpIdx", 0);
                Put("classicClick", -1);
                if (GameSettings.ClassicPanel != classicWas) GameSettings.ClassicPanel = classicWas;
                if (GameSettings.ShowLabels != labelsWas) GameSettings.ShowLabels = labelsWas;
            });
        }

        // The W4 notes against the note strip of the classic panel and -fowlegacyui, measured in an OnGUI with the strip's
        // own style: every break-off at every port, patrol, landable body and haven (and with none), the losses at 99.9 t
        // and 9,999,999 cr, the answers' notes, and the note a real raider call makes. Each is one line with no wrap, so
        // none may be wider than the strip.
        IEnumerator PirateNoteChecks(SolarSystem qs)
        {
            var texts = new List<string>();
            bool go = Guard("notes setup", () =>
            {
                var havens = new List<string>(Threat.Havens) { null };
                foreach (var h in havens)
                {
                    foreach (var p in Career.Ports)
                    {
                        texts.Add(Career.OffNote(Career.BreakOff.Docked, p.Name, h));
                        if (p.PatrolG > 0) texts.Add(Career.OffNote(Career.BreakOff.Patrol, p.Name, h));
                    }
                    foreach (var x in sys.Bodies) if (x.Parent != null && !Career.IsCraft(x)) texts.Add(Career.OffNote(Career.BreakOff.Landed, x.Name, h));
                    texts.Add(Career.OffNote(Career.BreakOff.JobOver, null, h));
                    texts.Add(Career.LostNote(h));
                    foreach (double t in new[] { 0, 99.9 }) { texts.Add(Career.BoardedNote(t, 9999999, h)); texts.Add(Career.GaveUpNote(t, 9999999, h)); }
                }
                foreach (var p in Career.Ports) if (p.PatrolG > 0) texts.Add(Career.DivertNote(p));
                texts.Add(Career.RunNote); texts.Add(Career.DryNote); texts.Add(Career.NavNote);
                var (vesta, at, vel) = Belt(qs);
                var b = MakeBench(qs, at, vel);
                b.C.MakeCall(Career.RouteKind.Pirate, b.Job, qs, b.Ship, b.Jd);
                b.C.DropContact();
                var incoming = b.Said.Find(n => n.What == Career.Happening.RouteCall && n.Text.StartsWith("Incoming: ")).Text;
                if (incoming != null) texts.Add(incoming);
                else Check(false, "pirates: notes: a raider call on the bench made no incoming note");
                Put("noteWidths", null);
                Put("noteProbe", texts.ToArray());
            });
            float[] widths = null;
            for (int i = 0; go && i < 10 && widths == null; i++) { yield return null; widths = Get<float[]>("noteWidths"); }
            if (go) Guard("notes width", () =>
            {
                if (widths == null || widths.Length != texts.Count) { Check(false, $"pirates: notes: the scene measured {widths?.Length.ToString() ?? "nothing"} of {texts.Count} texts in 10 frames"); return; }
                float strip = (float)typeof(OpenSystemScene).GetField("NoteW", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                int widest = 0;
                var over = new List<string>();
                for (int i = 0; i < widths.Length; i++)
                {
                    if (widths[i] > widths[widest]) widest = i;
                    if (widths[i] > strip) over.Add($"{widths[i]:F0} px \"{texts[i]}\"");
                }
                Check(over.Count == 0,
                    $"pirates: the W4 notes fit the note strip of the classic panel and -fowlegacyui: {texts.Count} texts (every port, patrol, landable body and haven; 99.9 t, 9,999,999 cr) " +
                    $"measured with its style at UI scale {app.UiScale:F2}, the widest {widths[widest]:F0} of {strip:F0} px: \"{texts[widest]}\"" +
                    (over.Count > 0 ? $"; {over.Count} too wide: " + string.Join("; ", over.GetRange(0, Math.Min(5, over.Count))) : ""));
            });
        }
    }
}
