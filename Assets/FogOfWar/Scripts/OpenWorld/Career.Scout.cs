using System;

namespace FogOfWar.OpenWorld
{
    // Combat Phase 3c (docs/design-combat.md): a quiet watch on a raider haven. It is a survey whose dwell counts only the
    // seconds of an update that no raider could see (SeenSeconds), so the ship waits out the raider's track after each
    // burn, then dwells. Any hull can do it: stock radiators wait 2 h, stowed ones StowRho of that. Gameplay values.
    public partial class Career
    {
        public const string ReconBody = "Pallas";   // [design] D8: Hektor is too far for a board job
        const double ReconPayFactor = 1.5;          // [design] over the survey pay: the watch costs the hold wait, and a raider's eyes are on it
        public const string QuietLine = "Counts only unseen time: wait out the raider's track after each burn.";

        // The part q of this update's scan seconds that no raider could see. SeenSeconds counts loud seconds and the hold after
        // them, not a chase. [design] While a chase is on, a raider watches the ship, so nothing counts: Scan runs before
        // Contacts, so a chase that ends in this update still blocks it. A landed ship is out of a raider's reach (RouteTick,
        // Contacts), so all of q counts. A chase that has ended leaves no track: the hold runs from the last burn, as it does for
        // the raid roll. whole: the ship was in range the whole update, so the seen seconds lie in q's window and the unseen
        // ones are simDt - seen. Else the seen seconds may overlap q anywhere, and q - seen is the least that is unseen.
        internal double QuietSeconds(Ship ship, double jd, double simDt, double q, bool whole)
        {
            if (Encounter != null) return 0;
            if (ship.Landed != null) return q;
            double seen = SeenSeconds(ship, jd, simDt);
            return whole ? Math.Min(q, Math.Max(0, simDt - seen)) : Math.Max(0, q - seen);
        }

        // Board slot 8, Ceres and Vesta only: half of the days, a quiet watch on Pallas. It draws no rng before the roll, and
        // the slot comes last, so the jobs of slots 0-7 do not move.
        Contract Recon(System.Random rng, SolarSystem sys, double jd)
        {
            if (Docked.Name != "Ceres" && Docked.Name != "Vesta") return null;
            if (rng.NextDouble() >= 0.5) return null;
            var t = sys[ReconBody];
            if (t == null) return null;
            double d = (sys.Pos(t, jd) - sys.Pos(sys[Docked.Body], jd)).Mag;
            double slack = 1.2 + rng.NextDouble();
            var c = new Contract
            {
                Id = nextId++, Kind = JobKind.Survey, From = Docked.Name, To = t.Name, Title = "Quiet watch on " + t.Name,
                DwellNeeded = DwellSeconds, Quiet = true,
            };
            double survey = Math.Round(RefPay(d, 0, out c.Days, slack) * 1.2 / 100) * 100 + 1000;
            c.Pay = Math.Round(survey * ReconPayFactor / 100) * 100;
            // [design] the dwell starts only after the arrival burn's hold (stock hull: 2 h), so the days and the express window carry it
            double wait = DwellSeconds + Signature.TrackHoldS;
            c.Days += wait / 86400;
            c.ExpressDays = ExpressDaysFor(d, wait, c.Days);
            return c;
        }
    }
}
