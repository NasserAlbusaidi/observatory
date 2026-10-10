using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using FogOfWar.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace FogOfWar
{
    // A raider's chase on screen (Career.Pirates.cs, docs/design-world.md W4): the live figures for the call card, the HUD
    // pill and the classic panel; a red dot and tag in the view, as traffic has; and the answers in the classic and
    // -fowlegacyui panel, and on the job's card once Run has closed the call card.
    public partial class OpenSystemScene
    {
        static string Raiders => Career.Factions[(int)Faction.Raiders].Name; // decision 2: never a literal name (D13)

        // The raider seen from the ship now (it flies each span with the player, so its state is the scene's date):
        // range in km, closing speed in km/s (positive: coming nearer, as the target card reads it), and the boarding
        // clock as a part of Threat.BoardingSeconds. False with no chase.
        bool RaiderStatus(out double range, out double closing, out double boarding)
        {
            var e = career?.Encounter;
            range = closing = boarding = 0;
            if (e == null) return false;
            var r = e.Pirate.Pos - ship.Pos;
            range = r.Mag;
            closing = range > 1e-9 ? D3.Dot(ship.Vel - e.Pirate.Vel, r / range) : 0;
            boarding = Math.Min(1, e.Dwell / Threat.BoardingSeconds);
            return true;
        }

        // the live line: the card, the pill (in capitals) and the classic panel; null with no chase
        string RaiderLine()
        {
            if (!RaiderStatus(out double range, out double closing, out double boarding)) return null;
            return $"{career.Encounter.Proxy.Name} {Km(range)} · " + (closing >= 0 ? "closing " + SpeedText(closing) : "opening " + SpeedText(-closing)) +
                   $" · boarding {Math.Floor(boarding * 100):0} %";
        }

        // the Jobs page's line on the chased job: the call's answers while its card is up, then the ones a chase keeps
        static string ChaseWarn(string hull, bool card) =>
            $"The {hull} of the {Raiders} gives chase: " + (card ? "run, divert into a patrol's reach, or give up part of the job." : "divert into a patrol's reach, or give up part of the job.");

        // An answer from a card on screen, the call read when it is clicked: a call that changed under the pointer is not
        // answered with another call's choice
        void AnswerCall(Career.RouteCall call, int i)
        {
            if (career == null || call == null || call != career.Call || call.Answers == null || i < 0 || i >= call.Answers.Length) return;
            var what = call.Answers[i].What;
            career.Answer(what, jd);
            // Phase 5 point 2: Revise opens the trip planner, the console's Nav tab; the classic panel has it on the panel
            if (what == Career.RouteAnswer.Revise && !GameSettings.ClassicPanel && consoleTabs != null) ShowConsoleTab(1);
        }

        // Once Run has closed the card the chase goes on, and Divert and Give up stay on offer: Divert to the patrolled port
        // the ship reaches soonest now, as Career.Answer picks it with no card
        (Career.RouteAnswer What, string Label)[] ChaseAnswers()
        {
            var port = Career.NearestPatrol(sys, ship, jd, out _);
            return port != null
                ? new[] { (Career.RouteAnswer.Divert, Career.DivertLabel(port)), (Career.RouteAnswer.GiveUp, Career.GiveUpLabel) }
                : new[] { (Career.RouteAnswer.GiveUp, Career.GiveUpLabel) };
        }

        // What a chase can be answered with now: the raider call's answers while its card is up (call set), else ChaseAnswers
        (Career.RouteAnswer What, string Label)[] RaiderAnswers(out Career.RouteCall call)
        {
            call = career.Call?.Kind == Career.RouteKind.Pirate ? career.Call : null;
            return call?.Answers ?? ChaseAnswers();
        }

        // ---------------------------------------------------------------- classic and -fowlegacyui panel

        int classicClick = -1;    // review checks: answer i as its button would, at the next repaint
        string classicDrawn = ""; // review checks: the answers and the live line the panel drew last

        // At the top of the career panel: a call with its answers, every kind; with no card, a chase's line and the answers
        // it still has
        void CallPanel(PanelUI ui)
        {
            var call = career.Call;
            string live = RaiderLine();
            if (call == null && live == null) { classicDrawn = ""; return; }
            ui.Section(call?.Title ?? Raiders + " in pursuit");
            if (call != null) GUILayout.Label(call.Text, ui.note);
            if (live != null) GUILayout.Label(live, ui.label);
            (Career.RouteAnswer What, string Label)[] answers = call != null ? call.Answers ?? Array.Empty<(Career.RouteAnswer, string)>() : ChaseAnswers();
            var labels = Array.ConvertAll(answers, a => a.Label);
            int hit = ui.Buttons(labels);
            // a repaint comes after the layout, so a change here does not unbalance this event's controls
            if (classicClick >= 0 && Event.current.type == EventType.Repaint) { hit = classicClick; classicClick = -1; }
            classicDrawn = string.Join(" / ", labels) + (live != null ? " | " + live : "");
            if (hit < 0 || hit >= answers.Length) return;
            if (call != null) AnswerCall(call, hit);
            else career.Answer(answers[hit].What, jd);
        }

        // ---------------------------------------------------------------- the view and the map

        string raiderShown = "none"; // review checks: what DrawRaider did last (the tag; the dot is raiderDot)
        bool raiderDot;              // review checks: whether DrawRaider drew the dot last
        GUIStyle raiderStyle;

        // 4c (D12): the raider flies as the courier's hull, with no art of its own, at its Fleet.json length
        const string RaiderModel = "CourierShip";
        // As a body's label goes once its disc is 40 px (DrawLabels): a hull that long reads as a ship. Shorter, the dot stays
        // on it: nose on (12 m across) or on its unlit side, a 46 m hull at 2 km is a few dim pixels.
        const float RaiderDotPx = 40;
        Transform raiderT, raiderPlume, raiderGlow;
        Renderer[] raiderHull;        // the model's renderers, for its box on screen
        Rect raiderTag;               // review checks: where DrawRaider put the tag last
        Encounter raiderFor;          // the chase the model belongs to: a new one spawns its own
        float raiderLen = 1, raiderKm = TorchKm;
        readonly List<UnityEngine.Object> raiderMade = new List<UnityEngine.Object>();

        // The pirate Ship's model, from LateCareer: at its position and attitude, its plume while it thrusts. Spawned when a
        // chase starts and destroyed, with every material and mesh made for it, when it ends by any path (EndContact,
        // DropContact, a load, a new career) or another starts: each frame compares the career's encounter to raiderFor.
        void LateRaider()
        {
            var e = career?.Encounter;
            if (raiderT != null && e != raiderFor) DropRaiderModel();
            if (e == null) return;
            if (raiderT == null)
            {
                DropRaiderModel(); // a scene rebuild destroyed the holder: free what it left
                var f = FleetModel(RaiderModel);
                bool own = f != null && Resources.Load<GameObject>("Models/" + f.model) != null;
                raiderKm = own ? f.lengthM / 1000f : TorchKm;
                raiderT = SpawnCraft("Raider", own ? f.model : "TorchShip", own, raiderMade, out raiderLen, out raiderGlow, out raiderPlume);
                raiderHull = raiderT.GetChild(0).GetComponentsInChildren<Renderer>(true);
                raiderFor = e;
            }
            raiderT.localPosition = Map(e.Pirate.Pos, out double k);
            raiderT.localRotation = e.Pirate.Att;
            raiderT.localScale = Vector3.one * (float)(raiderKm * k / raiderLen);
            CraftDrive(raiderPlume, raiderGlow, raiderLen, (float)(e.Pirate.Throttle * e.Pirate.AccelG));
        }

        void DropRaiderModel()
        {
            if (raiderT != null) { raiderT.gameObject.SetActive(false); Gfx.Destroy(raiderT.gameObject); }
            foreach (var o in raiderMade) Gfx.Destroy(o);
            raiderMade.Clear();
            raiderT = raiderPlume = raiderGlow = null;
            raiderHull = null;
            raiderFor = null;
        }

        // After DrawTraffic, the same way, in the Raiders' colour and a little larger. The tag is a button that targets it,
        // as a body's label is (T also cycles to it, CycleTarget); a button only as wide as its text and only where it is
        // clear of the panels, so a click or a drag-to-look there is not taken from a control under it. With the raider as
        // the target in the UI Toolkit HUD only the dot: its bracket names it, and the tag would lie over that name. The
        // classic panel and -fowlegacyui mark the target with a bracket that has no name, so there the tag stays. Near enough
        // that its model is longer than RaiderDotPx on screen, the model takes the dot's place and the tag clears its hull.
        void DrawRaider()
        {
            var e = career?.Encounter;
            raiderDot = false;
            if (e == null) { raiderShown = "none"; return; }
            float ui = App.UiScale;
            var w = sys.Pos(e.Proxy, jd);
            var p = Map(w, out double k);
            float dist = p.magnitude;
            var sp = Cam.WorldToScreenPoint(p);
            if (dist < 1e-6f || sp.z <= 0 || !CockpitShows(sp)) { raiderShown = "out of view"; return; }
            var g = new Vector2(sp.x / ui, (UnityEngine.Screen.height - sp.y) / ui);
            if (App.UnderPanel(g)) { raiderShown = "under a panel"; return; }
            foreach (var (c, cr, cd) in occluders) if (cd < dist * 0.999f && (c - g).magnitude < cr) { raiderShown = "behind a body"; return; }
            var col = FactionColours[(int)Faction.Raiders];
            if (raiderStyle == null) raiderStyle = new GUIStyle(labelStyle) { normal = { textColor = col } };
            string tag = $"{e.Proxy.Name} · {Km((w - ship.Pos).Mag)}";
            float pxPerRad = UnityEngine.Screen.height / 2f / Mathf.Tan(Cam.fieldOfView * Mathf.Deg2Rad / 2);
            float modelPx = raiderT != null && raiderT.gameObject.activeInHierarchy ? (float)(raiderKm * k) / dist * pxPerRad / ui : 0;
            raiderDot = modelPx <= RaiderDotPx;
            if (raiderDot)
            {
                GUI.color = col;
                GUI.DrawTexture(new Rect(g.x - 2.5f, g.y - 2.5f, 5, 5), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            if (ship.Target == e.Proxy && HasHud && App.ShellActive) { raiderShown = "dot only, the target: " + tag; return; }
            var r = new Rect((raiderDot ? g.x : HullRight(g.x, ui)) + 6, g.y - 8, raiderStyle.CalcSize(new GUIContent(tag)).x, 16);
            raiderTag = r;
            bool clear = !App.UnderPanel(new Vector2(r.xMax, r.yMin)) && !App.UnderPanel(new Vector2(r.xMax, r.yMax))
                         && !(App.ShellActive && UiShell.Instance.PointerOverMouse);
            if (!clear) GUI.Label(r, tag, raiderStyle);
            else if (GUI.Button(r, tag, raiderStyle)) SetTarget(e.Proxy);
            raiderShown = "on: " + tag;
        }

        // The right edge, in GUI px, of the model's box on screen (its world box's corners in front of the camera), and
        // never left of x: the tag goes past it, not over the hull
        float HullRight(float x, float ui)
        {
            // only the renderers shown: the Engine glow is off while the raider coasts, and an inactive one has no box
            bool any = false;
            var b = new Bounds();
            foreach (var h in raiderHull)
                if (h.gameObject.activeInHierarchy) { if (any) b.Encapsulate(h.bounds); else { b = h.bounds; any = true; } }
            if (!any) return x;
            for (int i = 0; i < 8; i++)
            {
                var c = Cam.WorldToScreenPoint(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (c.z > 0) x = Mathf.Max(x, c.x / ui);
            }
            return x;
        }

        // ---------------------------------------------------------------- the flight log

        // the recorder's discrete field: which raider, and how near
        string RecRaider()
        {
            var e = career?.Encounter;
            if (e == null) return "-";
            return e.Proxy.Name + (e.Dwell > 0 ? " alongside" : e.Range < Career.FireKm ? " in range" : " chasing");
        }

        // combat 4a: the missiles in flight and left, 4b's kills, and 4c's 1× hold before an impact; changes only on a salvo,
        // a hit, a kill or the hold
        string RecMissiles()
        {
            var e = career?.Encounter;
            return e == null ? "-" : $"{e.Missiles.Count} in flight, {e.Magazine} left, {e.Killed} killed" + (career.ImpactHeld ? ", held 1×" : "");
        }

        // ---------------------------------------------------------------- review checks

        // a ClickEvent sent straight to the call card's button i, so its click handler runs; no pointer picking, so a layer
        // over the card would not stop it. False if that button is hidden
        public bool TestClickCallButton(int i)
        {
            var b = callBtns[i];
            if (b == null || b.style.display.value != DisplayStyle.Flex) return false;
            using (var ev = ClickEvent.GetPooled())
            {
                ev.target = b;
                b.SendEvent(ev);
            }
            return true;
        }

        // the call card in one line: shown or not, each button's label (- when hidden), the live line (- when hidden)
        public string TestCallCardReport()
        {
            if (callCard == null) return "no card";
            string Btn(Label b) => b.style.display.value == DisplayStyle.Flex ? b.text : "-";
            return $"card {callCard.style.display.value} | {Btn(callBtns[0])} / {Btn(callBtns[1])} / {Btn(callBtns[2])} | live " +
                   (callLive.style.display.value == DisplayStyle.Flex ? callLive.text : "-");
        }

        // the raider's pill: its text, or - when hidden
        public string TestRaiderPill() => pRaider == null ? "no pill" : pRaider.style.display.value == DisplayStyle.Flex ? pRaider.text : "-";

        // review shots: on puts the ship 1,000,000 km out from Vesta at rest with it, out of every patrol's reach, with a
        // cargo job first in the list, so TestRouteCall(Pirate) makes a raider call as a trip would; off ends the chase,
        // takes the job out and puts the ship back
        (D3 Pos, D3 Vel, Body Target) raiderShotWas;
        public void TestRaiderPlace(bool on)
        {
            if (!on)
            {
                career.DropContact();
                career.Active.RemoveAll(c => c.Key == "qa/raider-shot");
                (ship.Pos, ship.Vel, ship.Target) = raiderShotWas;
                return;
            }
            raiderShotWas = (ship.Pos, ship.Vel, ship.Target);
            var vesta = sys["Vesta"];
            var pV = sys.Pos(vesta, jd);
            ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0;
            ship.Pos = pV + pV.Norm * 1e6; ship.Vel = sys.Vel(vesta, jd);
            career.Active.Insert(0, new Contract { Id = 900005, Kind = JobKind.Cargo, Title = "Water reclamation parts", From = "Ceres", To = "Vesta", Tonnes = 40, Pay = 24000, IssuedJd = jd, DeadlineJd = jd + 30, Days = 30, Key = "qa/raider-shot" });
        }
    }
}
