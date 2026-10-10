using System;
using System.Collections;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;
using UnityEngine.UIElements;

namespace FogOfWar
{
    // 20: Phase 5's encounter flow (docs/design-combat.md, PR 5a). 20a-20c, point 1, the cap follows time: 20a the raider's
    // FireKm and alongside times (Career.ReachInS) against a raider flown by its own Pursue: from every update the bound plus
    // the time gone is never past the real event; 20b the cap at a launch from rest at FireKm is the flight's 217 s / EventS,
    // and D11's 1× floor holds for a coasting missile 15 s out; 20c the Fast plan's brake (Career.BrakeInS) against the autopilot flown to it.
    // 20d-20f, point 2, the Threat card (Career.Threat.cs): 20d one card a salvo, and none else, on a raider flown by its own
    // Pursue from far (a salvo put in flight by hand would not show the first rule's defect: the alongside time near at
    // launch); 20e a raider burn that brings FireKm from 2 h to 5 min gives a card, from 1,500 s to 900 s none; 20f the card
    // in the scene, in both shells: it takes the raider call's place and drops the warp, Continue closes it, Revise closes it
    // and opens the console's Nav tab (the toolkit shell). 20g-20h, point 9, the after-action card (Career.Report.cs): 20g its
    // lines against the notes a seeded chase at 6 PDC mounts said and the side each salvo came in on; Give up drops no warp,
    // the card outlives its job and a new call takes its place; each of the nine ends puts it up with its note as the cause
    // (ReportedCheck, beside each end's own check); a load gives none (the "pirates: a save and load mid-chase" check).
    // 20h the card in the scene, in both shells, after Give up: drawn, the warp kept, Close closes it.
    public partial class CareerQA
    {
        IEnumerator EncounterFlowChecks()
        {
            SolarSystem qs = null;
            if (Guard("encounter flow system", () => qs = SolarSystem.Build()))
            {
                Guard("encounter flow 20a", () => EventBoundChecks(qs));
                Guard("encounter flow 20b", () => LaunchCapChecks(qs));
                Guard("encounter flow 20c", () => BrakeEventChecks(qs));
                Guard("encounter flow 20d", () => LaunchCardChecks(qs));
                Guard("encounter flow 20e", () => BurnCardChecks(qs));
                Guard("encounter flow 20g", () => ReportCardChecks(qs));
            }
            yield return ThreatShellChecks();
        }

        static double EventIn(Career c, Career.ChaseEvent what)
        {
            var list = new List<(Career.ChaseEvent what, double s)>();
            c.ChaseEvents(list);
            var i = list.FindIndex(x => x.what == what);
            return i < 0 ? double.NaN : list[i].s;
        }

        // 20a: a raider at rest with a coasting ship, 50,000 km off, flown by Pursue in 10 s updates as the scene flies it
        void EventBoundChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            var e = RaiderAt(b, 5e4);
            b.Tick(0);
            double fire0 = EventIn(b.C, Career.ChaseEvent.Fire), along0 = EventIn(b.C, Career.ChaseEvent.Alongside);
            double t = 0, fired = double.NaN, along = double.NaN;
            var fireAt = new List<double>();
            var alongAt = new List<double>();
            Body dom = Ship.Dominant(qs, b.Ship.Pos, b.Jd);
            while (t < 4 * 3600 && b.C.Encounter == e && double.IsNaN(along))
            {
                double f = EventIn(b.C, Career.ChaseEvent.Fire), a = EventIn(b.C, Career.ChaseEvent.Alongside);
                if (double.IsNaN(fired) && !double.IsNaN(f)) fireAt.Add(t + f);
                if (!double.IsNaN(a)) alongAt.Add(t + a);
                D3 p0 = b.Ship.Pos, v0 = b.Ship.Vel;
                double j0 = b.Jd, h = (b.Jd + 10.0 / 86400 - b.Jd) * 86400;
                b.Ship.Step(qs, j0, h, dom);
                e.Advance(qs, j0, h, p0, v0, b.Ship.Vel);
                b.Tick(h);
                t += h;
                if (double.IsNaN(fired) && e.Magazine < Career.MagazineSize) fired = t;
                if (double.IsNaN(along) && e.Range < Career.InterceptKm && e.Vrel < Career.InterceptKms) along = t;
            }
            double fireLate = double.NegativeInfinity, alongLate = double.NegativeInfinity;
            foreach (var x in fireAt) fireLate = Math.Max(fireLate, x - fired);
            foreach (var x in alongAt) alongLate = Math.Max(alongLate, x - along);
            Check(!double.IsNaN(fired) && !double.IsNaN(along) && fireLate <= 1e-6 && alongLate <= 1e-6 && fire0 >= 0.5 * fired && along0 >= 0.5 * along,
                $"20a cap events: a 1 g raider at rest 50,000 km off, flown by Pursue in 10 s updates: it fired at {fired:N0} s (bound at the start {fire0:N0} s, ≥ half), alongside at {along:N0} s (bound {along0:N0} s, ≥ half); " +
                $"over {fireAt.Count} and {alongAt.Count} updates the bound plus the time gone came at most {fireLate:N1} s and {alongLate:N1} s past the real time (≤ 0: never late)");
            b.C.DropContact();
        }

        // 20b: a salvo put in flight from rest at FireKm, the raider 12,000 km astern with an empty magazine
        void LaunchCapChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            var dir = b.Ship.Pos.Norm;
            var e = RaiderAt(b, 12000, dir);
            e.Magazine = 0;
            b.Tick(0);
            e.Missiles.Add(new Missile { P = dir * Career.FireKm });
            e.Missiles.Add(new Missile { P = dir * Career.FireKm });
            double flight = FlightS(0, Career.FireKm), cap = b.C.WarpCap;
            e.Missiles.Clear();
            e.Missiles.Add(new Missile { P = dir * 150, Age = Career.BurnS });
            double flight150 = 150 / Career.MissileDvKms, in150 = b.C.ImpactInS, cap150 = b.C.WarpCap;
            Check(Math.Abs(flight - 217.0) < 0.1 && Math.Abs(cap - flight / Career.EventS) < 1e-6 && Math.Abs(in150 - flight150) < 1e-6 && flight150 < Career.ImpactHoldS && cap150 == 1,
                $"20b cap at launch: a salvo from rest {Career.FireKm:N0} km off hits in {flight:N1} s (217.0), so the cap is {cap:N2}× ({flight / Career.EventS:N2}×: the flight / {Career.EventS:0} s); " +
                $"one coasting 150 km off, {in150:N1} s out ({flight150:N1}), is held at {cap150:N0}× (D11: 1× inside {Career.ImpactHoldS:0} s)");
            b.C.DropContact();
        }

        // 20c: a Fast plan to Vesta from rest with it, 1,000,000 km out, on a constant mass (unlimited fuel: the formula takes
        // the drive's a now, and a ship that burns fuel speeds up and flips sooner, so the scene's cap reads it again each frame)
        void BrakeEventChecks(SolarSystem qs)
        {
            var (vesta, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            b.Ship.UnlimitedFuel = true;
            var dir = (qs.Pos(vesta, b.Jd) - at).Norm;
            b.Ship.Att = Quaternion.LookRotation(Frame.U(dir), Frame.U(Frame.EclNorth));
            b.Ship.Target = vesta; b.Ship.Plan = Plan.Brachistochrone;
            var e = RaiderAt(b, 1e5, -dir);
            e.Magazine = 0;
            b.Tick(0);
            double flip0 = EventIn(b.C, Career.ChaseEvent.Flip), arrive0 = EventIn(b.C, Career.ChaseEvent.Arrival);
            b.C.DropContact();
            double t = 0, flipped = double.NaN;
            Body dom = Ship.Dominant(qs, b.Ship.Pos, b.Jd);
            while (t < 8 * 3600 && double.IsNaN(flipped))
            {
                double h = (b.Jd + 10.0 / 86400 - b.Jd) * 86400;
                b.Ship.Step(qs, b.Jd, h, dom);
                b.Jd += h / 86400;
                t += h;
                if (b.Ship.Phase.StartsWith("flipped", StringComparison.Ordinal)) flipped = t;
            }
            Check(!double.IsNaN(flipped) && flip0 <= flipped + 10 && flip0 >= 0.95 * flipped && arrive0 > flip0,
                $"20c cap events: a Fast plan to Vesta from 1,000,000 km at rest with it, {b.Ship.AccelG:F2} g: the brake event at the start {flip0:N0} s, the autopilot flipped at {flipped:N0} s " +
                $"(the event not past it by more than a 10 s update, and ≥ 95 % of it); the arrival event {arrive0:N0} s, after the flip");
        }

        // 20d: a 1 g raider from rest 50,000 km off with a full magazine, flown by Pursue in 10 s updates as 20a flies it,
        // against a ship with no PDCs, until the chase ends (a boarding) or 4 h; each card is answered Continue
        void LaunchCardChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            var e = RaiderAt(b, 5e4);
            b.Tick(0);
            int launches = 0, cards = 0, offLaunch = 0, noDrop = 0;
            string first = null;
            double t = 0;
            Body dom = Ship.Dominant(qs, b.Ship.Pos, b.Jd);
            while (t < 4 * 3600 && b.C.Encounter == e)
            {
                int mag0 = e.Magazine;
                var call0 = b.C.Call;
                b.C.DropWarp = false;
                D3 p0 = b.Ship.Pos, v0 = b.Ship.Vel;
                double j0 = b.Jd, h = (b.Jd + 10.0 / 86400 - b.Jd) * 86400;
                b.Ship.Step(qs, j0, h, dom);
                e.Advance(qs, j0, h, p0, v0, b.Ship.Vel);
                b.Tick(h);
                t += h;
                bool launched = e.Magazine < mag0;
                if (launched) launches++;
                var call = b.C.Call;
                if (call == null || call == call0 || call.Kind != Career.RouteKind.Threat) continue;
                cards++;
                if (!launched) offLaunch++;
                if (!b.C.DropWarp) noDrop++;
                first ??= $"{call.Title}: {call.Text}";
                b.C.Answer(Career.RouteAnswer.Continue, b.Jd);
            }
            Check(launches >= 2 && cards == launches && offLaunch == 0 && noDrop == 0 && (b.C.Encounter == e ? b.C.Call == null : b.C.Call?.Kind == Career.RouteKind.Report) && first != null
                  && first.StartsWith("Missiles inbound: 2 missiles inbound from ", StringComparison.Ordinal) && first.Contains(". Impact in ") && first.EndsWith(". No plan flying.", StringComparison.Ordinal),
                $"20d threat cards: a raider flown from rest 50,000 km off over {t:N0} s launched {launches} salvos and put up {cards} cards (one a salvo, 2 at least), {offLaunch} on another update (0), " +
                $"{noDrop} without a warp drop (0); Continue closed each ({(b.C.Call == null ? "none up" : b.C.Call.Title)}); the first: \"{first}\"");
            b.C.DropContact();
        }

        // point 9: the after-action card is up with the end's note as its last line, and Close its one answer
        static bool Reported(Career c, string note) => c.Call?.Kind == Career.RouteKind.Report && note != null
            && c.Call.Text.EndsWith("\n" + note + ".", StringComparison.Ordinal) && c.Call.Answers?.Length == 1 && c.Call.Answers[0].What == Career.RouteAnswer.Close;

        static string CardText(Career c) => c.Call == null ? "no card" : $"{c.Call.Kind} \"{c.Call.Title}: {c.Call.Text.Replace("\n", " / ")}\"";

        void ReportedCheck(Career c, string note, string end) =>
            Check(Reported(c, note), $"20g after-action card: {end} puts it up with the end's note as its cause: {CardText(c)}");

        // 20g: a 1 g raider from rest 50,000 km off, flown by Pursue in 10 s updates as 20d flies it, at a seeded ship with
        // 6 PDC mounts, until the chase ends or 4 h. The card against what the chase said: the kills, the hit notes in order,
        // and the side each salvo came in on (its flight the update before the impact; the bench ship does not turn).
        void ReportCardChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            b.C.Build.pick[(int)Slot.PointDefense] = 3;
            b.C.SeedCombat(2010);
            var e = RaiderAt(b, 5e4);
            b.Tick(0);
            var sides = new List<Career.Aspect>();
            var hits = new List<string>();
            int kills = 0, arrived = 0, seen = b.Said.Count;
            double t = 0;
            bool dropped = false;
            Body dom = Ship.Dominant(qs, b.Ship.Pos, b.Jd);
            while (t < 4 * 3600 && b.C.Encounter == e)
            {
                D3? inbound = e.Missiles.Count > 0 ? e.Missiles[0].P : (D3?)null;
                b.C.DropWarp = false;
                D3 p0 = b.Ship.Pos, v0 = b.Ship.Vel;
                double j0 = b.Jd, h = (b.Jd + 10.0 / 86400 - b.Jd) * 86400;
                b.Ship.Step(qs, j0, h, dom);
                e.Advance(qs, j0, h, p0, v0, b.Ship.Vel);
                b.Tick(h);
                t += h;
                int struck = 0;
                for (; seen < b.Said.Count; seen++)
                {
                    var s = b.Said[seen];
                    if (s.What == Career.Happening.MissileKilled) { kills++; struck++; }
                    else if (s.What == Career.Happening.MissileHit || s.What == Career.Happening.JobFailed && s.Text.StartsWith("Hit:", StringComparison.Ordinal)) { hits.Add(s.Text); struck++; }
                }
                if (struck > 0 && inbound != null) sides.Add(Career.AspectOf(Career.ToBody(b.Ship, inbound.Value)));
                arrived += struck;
                dropped = b.C.DropWarp;
                if (b.C.Call?.Kind == Career.RouteKind.Threat) b.C.Answer(Career.RouteAnswer.Continue, b.Jd);
            }
            string end = b.Said.FindLast(s => s.What == Career.Happening.RaiderGone || s.What == Career.Happening.Boarded).Text;
            bool boarded = b.Said.Exists(s => s.What == Career.Happening.Boarded);
            var lines = b.C.Call?.Text.Split('\n') ?? new string[0];
            bool linesOk = lines.Length == e.Salvos.Count + 1, sidesOk = sides.Count == e.Salvos.Count;
            var logged = new List<string>();
            int logKills = 0, logArrived = 0;
            for (int i = 0; i < e.Salvos.Count; i++)
            {
                var s = e.Salvos[i];
                logged.AddRange(s.Hits); logKills += s.Killed; logArrived += s.Arrived;
                sidesOk &= i < sides.Count && s.Aspect == sides[i];
                linesOk &= i < lines.Length && lines[i] == $"Salvo {i + 1}: " + Career.SalvoLine(s) && s.Fired == Career.Salvo && s.Km < Career.FireKm
                           && lines[i].StartsWith($"Salvo {i + 1}: {Career.Salvo} missiles from {s.Km:N0} km, in at the ", StringComparison.Ordinal);
            }
            Check(b.C.Encounter == null && Reported(b.C, end) && b.C.Call.Title == $"After action: the {e.Hull}" && e.Salvos.Count == Career.MagazineSize / Career.Salvo && linesOk && sidesOk
                  && string.Join("|", logged) == string.Join("|", hits) && logKills == kills && kills == e.Killed && logArrived == arrived && e.Salvos[0].BoreFirst > 0 && (boarded || !dropped),
                $"20g after-action card: a raider flown from 50,000 km at 6 PDC mounts (seed 2010) ended after {t:N0} s, \"{end}\": {e.Salvos.Count} salvo lines ({Career.MagazineSize / Career.Salvo}) as built from the record ({linesOk}), " +
                $"the sides {string.Join("/", sides)} as flown ({sidesOk}), {logKills} kills ({kills} said, {e.Killed} counted), {logged.Count} hit notes in order as said ({hits.Count}), {logArrived} arrivals ({arrived}), " +
                $"the end's warp drop {dropped} (none but a boarding's); {CardText(b.C)}");

            // Give up on a second chase: the card with no salvo, no warp drop; it stays when its job goes, and a raider call takes its place
            var c = MakeBench(qs, at, vel);
            var ec = RaiderAt(c, 5e4);
            c.Tick(1);
            c.C.DropWarp = false;
            c.C.Answer(Career.RouteAnswer.GiveUp, c.Jd);
            string gave = c.Said.FindLast(s => s.What == Career.Happening.RaiderGone).Text;
            bool up = Reported(c.C, gave) && c.C.Call.Text == $"The {ec.Hull} fired no missiles.\n{gave}.", noDrop = !c.C.DropWarp;
            string shown = CardText(c.C);
            c.C.Active.Remove(c.Job);
            c.Tick(10);
            bool outlives = Reported(c.C, gave);
            c.C.Active.Add(c.Job);
            bool made = c.C.MakeCall(Career.RouteKind.Pirate, c.Job, qs, c.Ship, c.Jd);
            var kind = c.C.Call?.Kind;
            c.C.DropContact();
            Check(up && noDrop && outlives && made && kind == Career.RouteKind.Pirate,
                $"20g after-action card: Give up shows {shown} ({up}), no warp drop ({noDrop}); with its job gone an update on it stays ({outlives}); a raider call takes its place ({made}, {kind?.ToString() ?? "none"})");
        }

        // 20e: a raider at rest, its FireKm `from` s out by ReachInS, given in one update the closing speed that brings it to
        // `to` s (the bench flies no raider: its burn is put in by hand); the card, and the event before and after
        void BurnCardChecks(SolarSystem qs)
        {
            (Career.RouteCall call, double was, double now) Burn(double from, double to)
            {
                var (_, at, vel) = Belt(qs);
                var b = MakeBench(qs, at, vel);
                double a = Career.PirateG * Ship.G0, s = 0.5 * a * from * from;
                var e = RaiderAt(b, Career.FireKm + s);
                b.Tick(1); // the chase's first update: its events kept
                double was = EventIn(b.C, Career.ChaseEvent.Fire);
                e.Pirate.Vel = b.Ship.Vel + (b.Ship.Pos - e.Pirate.Pos).Norm * ((s - 0.5 * a * to * to) / to); // s = u·t + a·t²/2
                b.Tick(1);
                var r = (b.C.Call, was, EventIn(b.C, Career.ChaseEvent.Fire));
                b.C.DropContact();
                return r;
            }
            var (far, far0, far1) = Burn(7200, 300);
            var (near, near0, near1) = Burn(1500, 900);
            Check(far?.Kind == Career.RouteKind.Threat && far.Title.StartsWith("The ", StringComparison.Ordinal) && far.Text.Contains($"In missile range ({Career.FireKm:N0} km) in 5 min at the soonest") && near == null,
                $"20e threat cards: a burn that brings the raider's FireKm from {far0:N0} s to {far1:N0} s ({Career.CapOf(far0):N0}× to {Career.CapOf(far1):N0}×, a step) shows \"{far?.Title}: {far?.Text}\"; " +
                $"one from {near0:N0} s to {near1:N0} s ({Career.CapOf(near0):N0}× to {Career.CapOf(near1):N0}×) shows {(near == null ? "none" : near.Title)} (none)");
        }

        // 20f: the Threat card in the scene, 1,000,000 km out from Vesta; the scene is put back after, a failed check too
        IEnumerator ThreatShellChecks()
        {
            bool legacy = UI.UiShell.Disabled, classicWas = GameSettings.ClassicPanel, consoleWas = false;
            string shell = legacy ? "legacy" : "toolkit";
            D3 pos0 = default, vel0 = default;
            Body target0 = null;
            int warp0 = 0;
            Contract job = null;
            Encounter e = null;
            int Tab() { var tabs = Get<object>("consoleTabs"); return tabs != null ? (int)tabs.GetType().GetProperty("Index").GetValue(tabs) : -1; }
            void Launch()
            {
                var m = new Missile { P = (e.Pirate.Pos - ship.Pos).Norm * Career.FireKm };
                e.Missiles.Add(m); e.Missiles.Add(m);
                Put("warpIdx", 2);
            }
            // the card a salvo put in flight shows: the warp dropped, the answers, the raider's live line on it
            void Shown(string what)
            {
                var call = career.Call;
                bool line = legacy ? Get<string>("classicDrawn").StartsWith($"Continue / Revise the plan | {e.Proxy.Name} ", StringComparison.Ordinal)
                    : Get<Label>("callLive").text.StartsWith(e.Proxy.Name, StringComparison.Ordinal) && Get<Label>("callTitle").text == "MISSILES INBOUND";
                Check(call?.Kind == Career.RouteKind.Threat && call.Title == "Missiles inbound" && call.Text.StartsWith("2 missiles inbound from ", StringComparison.Ordinal)
                      && call.Text.Contains(". Impact in 3 min 3") && call.Text.EndsWith(". No plan flying.", StringComparison.Ordinal) && call.Answers?.Length == 2 && Get<int>("warpIdx") == 0 && line,
                    $"20f threat card ({shell}): {what}: the {call?.Kind.ToString() ?? "no"} card \"{call?.Title}: {call?.Text}\", answers {(call?.Answers != null ? string.Join(" / ", Array.ConvertAll(call.Answers, a => a.Label)) : "-")}, " +
                    $"the warp {WarpOf(Get<int>("warpIdx")):N0}× (1×), the raider's live line on it {line}");
            }

            bool go = Guard("threat card setup", () =>
            {
                pos0 = ship.Pos; vel0 = ship.Vel; target0 = ship.Target; warp0 = Get<int>("warpIdx");
                if (!legacy) { consoleWas = s.TestConsoleOpen; GameSettings.ClassicPanel = false; }
                career.DropContact();
                Put("warpIdx", 0);
                var vesta = sys["Vesta"];
                D3 pV = sys.Pos(vesta, jd);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0; ship.Landed = null; ship.Hold = Hold.Off;
                ship.Pos = pV + pV.Norm * 1e6; ship.Vel = sys.Vel(vesta, jd);
                Call("SetTarget", vesta);
                job = new Contract { Id = 900500, Kind = JobKind.Cargo, Title = "QA ice", From = "Ceres", To = "Vesta", Tonnes = 20, Pay = 20000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, Key = "qa/threat-card" };
                career.Active.Add(job);
            });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) go = Guard("threat card call", () =>
            {
                bool made = career.Docked == null && career.MakeCall(Career.RouteKind.Pirate, job, sys, ship, jd);
                e = career.Encounter;
                Check(made && e != null && career.Call?.Kind == Career.RouteKind.Pirate, $"20f threat card ({shell}): a raider call in the scene ({career.Call?.Title ?? "none"})");
                if (e != null) e.Magazine = 0; // its own salvo is 20d's
            });
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("threat card launch", Launch);
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("threat card shown", () => Shown("a salvo with the raider's call up takes its place"));
            if (go) go = Guard("threat card continue", () =>
            {
                if (legacy) Put("classicClick", 0);
                else Check(s.TestClickCallButton(0), $"20f threat card ({shell}): the card's Continue button takes a click");
            });
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("threat card continued", () =>
            {
                Check(career.Call == null && career.Encounter == e && e.Missiles.Count == 2, $"20f threat card ({shell}): Continue closes the card ({career.Call?.Title ?? "none"}); the chase and the salvo go on");
                e.Missiles.Clear();
                if (!legacy) Call("SetConsole", false);
            });
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("threat card launch 2", Launch);
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("threat card shown 2", () =>
            {
                Shown("the next salvo");
                if (legacy) Put("classicClick", 1);
                else Check(s.TestClickCallButton(1), $"20f threat card ({shell}): the card's Revise button takes a click");
            });
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("threat card revised", () =>
                Check(career.Call == null && career.Encounter == e && (legacy || s.TestConsoleOpen && Tab() == 1),
                    $"20f threat card ({shell}): Revise closes the card ({career.Call?.Title ?? "none"}) and " + (legacy ? "leaves the classic panel's planner" : $"opens the console ({s.TestConsoleOpen}) on the Nav tab ({Tab()}, 1)")));
            // 20h: Give up ends the chase; the after-action card shows, the warp stays, and Close closes it
            if (go) go = Guard("report card give up", () =>
            {
                e.Missiles.Clear();
                if (!legacy) Call("SetConsole", false);
                Put("warpIdx", 2);
                career.Answer(Career.RouteAnswer.GiveUp, jd);
            });
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("report card shown", () =>
            {
                var call = career.Call;
                string note = career.Notes.FindLast(n => n.What == Career.Happening.RaiderGone).Text, how;
                bool drawn;
                if (legacy) { how = Get<string>("classicDrawn"); drawn = how == "Close"; }
                else
                {
                    var text = Get<Label>("callText");
                    float px = text.resolvedStyle.fontSize, high = text.layout.height;
                    string title = Get<Label>("callTitle").text;
                    drawn = title == $"AFTER ACTION: THE {e.Hull.ToUpperInvariant()}" && text.text == call?.Text && Get<Label>("callLive").resolvedStyle.display == DisplayStyle.None && high >= 2 * px;
                    how = $"title \"{title}\", the text {high:0} px high at {px:0} px a line (2 lines at least), the live line {Get<Label>("callLive").resolvedStyle.display}";
                }
                Check(Reported(career, note) && call.Title == $"After action: the {e.Hull}" && drawn && Get<int>("warpIdx") == 2,
                    $"20h after-action card ({shell}): Give up puts it up ({CardText(career)}); drawn: {how}; the warp {WarpOf(Get<int>("warpIdx")):N0}× (100×, kept)");
                if (legacy) Put("classicClick", 0);
                else Check(s.TestClickCallButton(0), $"20h after-action card ({shell}): the card's Close button takes a click");
            });
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("report card closed", () =>
            {
                string how = legacy ? Get<string>("classicDrawn") : s.TestCallCardReport();
                Check(career.Call == null && (legacy ? how == "" : how.StartsWith("card None", StringComparison.Ordinal)),
                    $"20h after-action card ({shell}): Close closes it ({CardText(career)}; {how})");
            });
            Guard("threat card restore", () =>
            {
                e?.Missiles.Clear();
                career.DropContact();
                if (career.Call?.Kind == Career.RouteKind.Report) career.Answer(Career.RouteAnswer.Close, jd);
                if (job != null) career.Active.Remove(job);
                ship.Pos = pos0; ship.Vel = vel0;
                Call("SetTarget", target0);
                Put("warpIdx", warp0);
                if (!legacy) { Call("SetConsole", consoleWas); GameSettings.ClassicPanel = classicWas; }
            });
            yield return null;
        }
    }
}
