using System;
using System.Collections.Generic;

namespace FogOfWar.OpenWorld
{
    // Phase 5 point 2 (docs/design-combat.md, "A step"): a warp drop is a decision. Each update of a chase, after Volley,
    // the raider's events (ChaseEvents: the impact, FireKm, alongside, the boarding) are set against the last update's,
    // aged by the update's time. A launch (an impact where none was expected) or an expected event a step sooner than its
    // own aged time puts up a Threat card: what, when, and the plan against the catch. It drops the warp to 1× and takes
    // the place of the card that was up (the raider's call, or the last Threat card). An event that arrives, a new event of
    // another kind and the player's own flip and arrival show none; nor does the chase's first update, whose call is its card.
    public partial class Career
    {
        public const double StepFactor = 10; // a step: the gap between two warp settings

        readonly List<(ChaseEvent what, double s)> threatWas = new List<(ChaseEvent, double)>(), threatNow = new List<(ChaseEvent, double)>();
        bool threatKept; // threatWas holds the last update's events; false until the chase's first update

        static bool RaiderEvent(ChaseEvent k) => k == ChaseEvent.Impact || k == ChaseEvent.Fire || k == ChaseEvent.Alongside || k == ChaseEvent.Boarded;

        // point 1's cap for an event s seconds out, before D11's hold and the scene's rounding to a setting
        internal static double CapOf(double s) => Math.Max(1, Math.Min(ChaseWarp, s / EventS));

        void ThreatCheck(Encounter e, Ship ship, double simDt)
        {
            if (!(simDt > 0)) return;
            ChaseEvents(threatNow);
            threatNow.RemoveAll(x => !RaiderEvent(x.what));
            int why = -1;
            for (int k = 0; threatKept && why < 0 && k < threatNow.Count; k++)
            {
                var x = threatNow[k];
                int i = threatWas.FindIndex(w => w.what == x.what && w.s - simDt > 0);
                if (i < 0 ? x.what == ChaseEvent.Impact : CapOf(x.s) <= CapOf(threatWas[i].s - simDt) / StepFactor) why = k;
            }
            threatWas.Clear();
            threatWas.AddRange(threatNow);
            threatKept = true;
            if (why >= 0) ThreatCard(e, ship, threatNow[why].what, threatNow[why].s);
        }

        void ThreatCard(Encounter e, Ship ship, ChaseEvent what, double s)
        {
            string title, line, when;
            if (what == ChaseEvent.Impact)
            {
                var m = e.Missiles[0];
                foreach (var x in e.Missiles) if (x.ImpactInS < m.ImpactInS) m = x;
                int n = e.Missiles.Count;
                title = n == 1 ? "Missile inbound" : "Missiles inbound";
                line = $"{n} {(n == 1 ? "missile" : "missiles")} inbound {SideOf(AspectOf(ToBody(ship, m.P)))}, {m.P.Mag:N0} km";
                when = $"Impact in {Clock(s)}";
            }
            else
            {
                title = $"The {e.Hull} closes";
                line = $"The {e.Hull} changed its burn, {e.Range:N0} km off";
                when = what == ChaseEvent.Fire ? $"In missile range ({FireKm:N0} km) in {Clock(s)} at the soonest"
                    : what == ChaseEvent.Alongside ? $"Alongside in {Clock(s)} at the soonest" : $"Boarded in {Clock(s)}";
            }
            Call = new RouteCall
            {
                Kind = RouteKind.Threat, Job = e.Job, Title = title, Text = $"{line}. {when}. {PlanLine(e, ship)}.",
                Answers = new[] { (RouteAnswer.Continue, "Continue"), (RouteAnswer.Revise, "Revise the plan") },
                Yes = "Continue", No = "Revise the plan",
            };
            DropWarp = true;
        }

        // The card's plan: the raider's catch from now (CatchNow) against the Fast plan's arrival
        string PlanLine(Encounter e, Ship ship)
        {
            if (e.Range < InterceptKm && e.Vrel < InterceptKms) return "It is alongside";
            double c = CatchNow(e, ship);
            if (!FastPlanIn(e.Sys, ship, out double arrive, out _)) return $"It can be alongside in {Clock(c)}. No plan flying";
            return $"It can be alongside in {Clock(c)}; you reach {ship.Target.Name} in {Clock(arrive)}: " + (c < arrive ? "it catches you first" : "you get there first");
        }

        static string SideOf(Aspect a) => a == Aspect.Fore ? "from ahead" : a == Aspect.Aft ? "from astern" : "from abeam";

        // seconds as a clock reads them: "45 s", "3 min 37 s", "1 h 10 min", then Span's days
        internal static string Clock(double s)
        {
            if (!(s < 2 * 86400)) return double.IsPositiveInfinity(s) ? "never" : Span(s / 86400);
            long n = (long)Math.Round(Math.Max(0, s));
            if (n < 60) return $"{n} s";
            if (n < 3600) return n % 60 == 0 ? $"{n / 60} min" : $"{n / 60} min {n % 60} s";
            return n % 3600 / 60 == 0 ? $"{n / 3600} h" : $"{n / 3600} h {n % 3600 / 60} min";
        }
    }
}
