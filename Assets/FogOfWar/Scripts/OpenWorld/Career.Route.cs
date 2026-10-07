using System;

namespace FogOfWar.OpenWorld
{
    // Route events: calls that come in during a trip, so the time between Engage and payday has decisions in it. A
    // distress call offers a rescue near where you are going; cargo trouble asks you to trade fuel for the load; a raider
    // (Career.Pirates.cs) gives chase. Rolled in game time while the autopilot flies a job, one call at a time. The scene
    // turns them off for the unattended suites, whose trips would stall when a call drops the warp. A pending call is not
    // saved.
    public partial class Career
    {
        public enum RouteKind { Distress, CargoTrouble, Pirate }

        // Yes and No answer a distress call or cargo trouble; Run, Divert and GiveUp a raider
        public enum RouteAnswer { Yes, No, Run, Divert, GiveUp }

        public class RouteCall
        {
            public RouteKind Kind;
            public string Title, Text, Yes, No;
            public (RouteAnswer What, string Label)[] Answers; // every answer the card offers, in order
            public Contract Job;            // the job the call is about (cargo trouble, a raider) or comes in on (distress)
            public Contract Offer;          // distress: the rescue on offer
            public double VentTonnes, SpoilDays, ExpiresJd;
            public Port DivertTo;           // a raider: the patrolled port the Divert answer flies to
        }

        public bool RouteEvents = true;
        public RouteCall Call { get; private set; }
        public const double RouteMeanDays = 1.5, RouteGapDays = 0.5, CallStandsDays = 0.5;
        double lastCallJd = double.NegativeInfinity;
        readonly System.Random routeRng = new System.Random();

        void RouteTick(SolarSystem sys, Ship ship, double jd, double simDt)
        {
            if (Call != null)
            {
                // the job ended (delivered, failed or dropped), or nobody answered the distress call in time
                if (!Active.Contains(Call.Job) || (Call.Kind == RouteKind.Distress && jd > Call.ExpiresJd)) Drop();
                return;
            }
            if (Encounter != null) return; // one encounter at a time, and no other call while a raider gives chase
            if (!RouteEvents || Sandbox || Docked != null || ship.Landed != null || !ship.PlanActive || simDt <= 0) return;
            if (jd - lastCallJd < RouteGapDays) return;
            var job = Active.Find(c => !c.Tutorial && TargetOf(c) == ship.Target && (c.Kind == JobKind.Cargo || c.Kind == JobKind.Passengers || c.Kind == JobKind.Survey));
            if (job == null) return;
            // raiders strike on the approach too (no cut-off): their rate is where the ship is slow, out of patrol reach
            if (routeRng.NextDouble() < 1 - Math.Exp(-PirateRate(sys, ship, job, jd) * simDt / 3600) && MakeCall(RouteKind.Pirate, job, sys, ship, jd))
            {
                lastCallJd = jd;
                return;
            }
            var t = ship.Target;
            if ((ship.Pos - sys.Pos(t, jd)).Mag < 10 * DockRange(t)) return; // nothing on the approach
            if (routeRng.NextDouble() >= 1 - Math.Exp(-simDt / 86400 / RouteMeanDays)) return;
            lastCallJd = jd;
            bool trouble = job.Kind == JobKind.Cargo && (Active.Count >= MaxActive || routeRng.NextDouble() < 0.5);
            MakeCall(trouble ? RouteKind.CargoTrouble : RouteKind.Distress, job, sys, ship, jd);
        }

        // A call about `job` now (also the QA hook). False when it does not fit: trouble needs cargo, a distress call a free
        // slot, a raider a place to wait (PirateCall).
        public bool MakeCall(RouteKind kind, Contract job, SolarSystem sys, Ship ship, double jd)
        {
            var dest = TargetOf(job);
            if (dest == null) return false;
            // a full burn from here to the job's target, the yardstick for both calls
            var (u, w) = ship.Target == dest ? ship.RelToTarget(sys, jd) : (0.0, 0.0);
            Trip(Math.Max((sys.Pos(dest, jd) - ship.Pos).Mag - Ship.Standoff(dest), 0), ship.MaxG * Ship.G0, 1, ship.Mass, out double burn, out _, out _, u, w);

            if (kind == RouteKind.Pirate)
            {
                if (!PirateCall(job, sys, ship, jd)) return false;
            }
            else if (kind == RouteKind.CargoTrouble)
            {
                if (job.Kind != JobKind.Cargo || ship.Fuel <= 0) return false;
                double vent = Math.Round(Math.Min(ship.Fuel * 0.08, 12), 1);
                double spoil = burn * 1.3 / 86400; // a full burn makes it; a long coast does not
                Call = new RouteCall
                {
                    Kind = kind, Job = job, VentTonnes = vent, SpoilDays = spoil,
                    Title = "Cargo trouble: hold cooling failing",
                    Text = $"The {job.Title.ToLowerInvariant()} for {job.To} will spoil without cooling. Vent fuel through the heat exchanger to keep it cold, " +
                           $"or push on and deliver within {Span(spoil)}. After that it pays half.",
                    Yes = $"Vent {vent:N1} t of fuel", No = $"Push on ({Span(spoil)})",
                };
            }
            else if (kind == RouteKind.Distress)
            {
                if (Active.Count >= MaxActive) return false;
                var here = job.Kind == JobKind.Survey ? sys[job.To] : sys[PortAt(job.To)?.Body ?? ""];
                if (here == null) return false;
                var c = Rescue(routeRng, sys, jd, here, job.To);
                // the wreck is near where you are going: the rest of this trip comes on top of the rescue's own time
                c.Days += burn / 86400;
                c.ExpressDays = 0;
                c.Key = "route/" + c.Id;
                Call = new RouteCall
                {
                    Kind = kind, Job = job, Offer = c, ExpiresJd = jd + CallStandsDays,
                    Title = "Distress call",
                    Text = $"{c.Title}. The wreck is near {here.Name}, where you are going. {c.Pay:N0} cr, {Span(c.Days)} to reach them.",
                    Yes = "Answer the call", No = "Ignore",
                };
            }
            else return false;
            Call.Answers ??= new[] { (RouteAnswer.Yes, Call.Yes), (RouteAnswer.No, Call.No) };
            DropWarp = true;
            Say(Happening.RouteCall, "Incoming: " + (kind == RouteKind.Pirate ? Call.Title : Call.Title.ToLowerInvariant())); // a raider's title starts with its faction's name
            return true;
        }

        // An answer from the card (RouteCall.Answers). Yes and No go to a distress call or cargo trouble; Run, Divert and
        // GiveUp to a raider, also once Run has closed its card, while the chase goes on.
        public void Answer(RouteAnswer a, double jd)
        {
            var call = Call;
            if (call != null && call.Kind != RouteKind.Pirate)
            {
                if (a == RouteAnswer.Yes || a == RouteAnswer.No) Answer(a == RouteAnswer.Yes, jd);
                return;
            }
            if (a == RouteAnswer.Run || a == RouteAnswer.Divert || a == RouteAnswer.GiveUp) AnswerRaider(a, jd);
        }

        // The pilot's answer. Distress: yes takes the rescue on (if there is a slot and the fuel for it). Cargo trouble:
        // yes vents the fuel now, no starts the spoil clock on the job. A raider's two-button card: yes gives up the share
        // its Yes label names, no runs.
        public void Answer(bool yes, double jd)
        {
            var call = Call;
            if (call == null) return;
            if (call.Kind == RouteKind.Pirate) { AnswerRaider(yes ? RouteAnswer.GiveUp : RouteAnswer.Run, jd); return; }
            if (call.Kind != RouteKind.Distress && call.Kind != RouteKind.CargoTrouble) return;
            Drop();
            if (call.Kind == RouteKind.Distress)
            {
                if (!yes) return;
                string why = Active.Count >= MaxActive ? $"{MaxActive} jobs at most" : null;
                if (why == null && ship != null)
                {
                    double need = NeedTonnes(call.Offer, 0.5);
                    if (need > ship.Fuel * 0.95) why = $"needs ≈ {need:N0} t of fuel even with a coast, {ship.Fuel:N0} t aboard";
                }
                if (why != null) { Say(Happening.JobFailed, "Can't answer the call: " + why, false); return; }
                Add(call.Offer, jd);
                return;
            }
            if (yes)
            {
                if (ship != null) ship.Fuel = Math.Max(0, ship.Fuel - call.VentTonnes);
                Say(Happening.RouteCall, $"Vented {call.VentTonnes:N1} t of fuel: the cargo stays cold");
            }
            else
            {
                call.Job.SpoilJd = jd + call.SpoilDays;
                Say(Happening.RouteCall, $"Pushing on: deliver within {Span(call.SpoilDays)} or the cargo pays half", false);
            }
        }

        void Drop()
        {
            if (Call?.Offer != null && !Active.Contains(Call.Offer)) previews.Remove(Call.Offer.Id);
            Call = null;
        }

        static string Span(double days) => days < 2 ? $"{days * 24:N0} h" : $"{days:N1} days";
    }
}
