using System;

namespace FogOfWar.OpenWorld
{
    // Combat Phase 2 (docs/design-combat.md): what a raider sees of the ship. Signature.cs holds the rule; the roll is in
    // RouteTick (Career.Route.cs).
    public partial class Career
    {
        // Seconds of this update a raider could see the ship: its loud seconds, plus the time after its last loud moment, up
        // to TrackHoldS. Gaps between two burns inside one update are left out (at most one frame).
        internal double SeenSeconds(Ship ship, double jd, double simDt)
        {
            if (!spanOk || simDt <= 0) return 0;
            double loud = Math.Max(0, ship.LoudSeconds - spanLoud);
            double tail = (Math.Min(jd, ship.LastLoudJd + Signature.TrackHoldS / 86400) - Math.Max(spanJd, ship.LastLoudJd)) * 86400;
            return Math.Min(simDt, loud + Math.Max(0, tail));
        }

        internal double RaidChance(SolarSystem sys, Ship ship, Contract job, double jd, double seenS) =>
            seenS > 0 ? 1 - Math.Exp(-PirateRate(sys, ship, job, jd) * seenS / 3600) : 0;

        // The job a raider works: the one flown to, else goods or people aboard, else a survey whose dwell is under way
        internal Contract RaidJob(SolarSystem sys, Ship ship, double jd)
        {
            Contract carry = null, survey = null;
            foreach (var c in Active)
            {
                if (c.Tutorial || (c.Kind != JobKind.Cargo && c.Kind != JobKind.Passengers && c.Kind != JobKind.Survey)) continue;
                var to = TargetOf(c);
                if (to != null && to == ship.Target) return c;
                if (c.Kind != JobKind.Survey) carry ??= c;
                else if (survey == null && to != null && (ship.Pos - sys.Pos(to, jd)).Mag < Codex.ScanRange(to)) survey = c;
            }
            return carry ?? survey;
        }

        public enum Watch { None, Loud, Tracked, Cold }

        // For the HUD: None outside raider space (no raid job, presence 0, a patrol in reach, docked, landed, a chase on)
        public Watch RaiderWatch(SolarSystem sys, Ship ship, double jd, out double leftS)
        {
            leftS = 0;
            if (Sandbox || Docked != null || ship.Landed != null || Encounter != null || RaidJob(sys, ship, jd) == null) return Watch.None;
            if (Threat.Presence(sys, ship.Pos, jd) <= 0 || Threat.PatrolAt(sys, ship.Pos, jd) != null) return Watch.None;
            if (Signature.Loud(ship.Mass, ship.Throttle * ship.AMax + ship.RcsLevel * Ship.RcsAccel)) return Watch.Loud;
            leftS = Signature.TrackHoldS - (jd - ship.LastLoudJd) * 86400;
            return leftS > 0 ? Watch.Tracked : Watch.Cold;
        }

        // The call card's sentence: how the raider knew (empty for a call made by hand, as QA does)
        string SeenLine(Ship ship, D3 start, double jd)
        {
            double ago = (jd - ship.LastLoudJd) * 86400;
            if (ago < 60) return $" It saw your burn from {(start - ship.Pos).Mag:N0} km.";
            return ago < Signature.TrackHoldS ? $" It has tracked you since your last burn, {(ago < 3600 ? $"{Math.Floor(ago / 60):0} min" : $"{ago / 3600:0.#} h")} ago." : "";
        }
    }
}
