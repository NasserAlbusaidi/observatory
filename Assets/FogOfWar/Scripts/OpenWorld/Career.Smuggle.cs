using System;

namespace FogOfWar.OpenWorld
{
    // Combat Phase 3d (docs/design-combat.md): contraband cargo. A Cargo job posted at the Tharsis and Compact ports, with a
    // risky route, that pays 1.5x plain cargo. On delivery the pay is graded by how much raiders saw the ship: Exposure is the
    // expected number of raider calls so far (the raid roll's own exponent), and the grade is 1.25 of the posted pay at none and
    // 0.667 (the plain pay) at 0.314 calls. Stealth helps by cutting each burn's tail; it cannot hide the loud seconds. Gameplay values.
    public partial class Career
    {
        const double ContrabandPayFactor = 1.5;   // [design] D9: over the plain cargo pay
        // [design] D9: the grade is CleanBonus * e^(-Exposure / ExposureScale) of the posted pay; it breaks even with plain
        // cargo (1.5 * grade = 1) at Exposure = ExposureScale * ln(1.875) = 0.314 calls [calc]
        public const double CleanBonus = 1.25, ExposureScale = 0.5;

        // [design] No title contains "oud" (the save scan of CareerQA 15 reads for it), and each reads through InSentence on the
        // raider card ("50 % of the unmanifested ice and of its pay")
        static readonly string[] ContrabandGoods =
        {
            "Unmanifested ice", "Untaxed D–He³ pellets", "Off-book reactor coils", "Unregistered drill heads", "Unlisted med packs",
        };

        // What a contract pays on delivery, as a part of the posted pay (before express, late and spoiled, which it multiplies)
        public static double Grade(Contract c) => c.Contraband ? CleanBonus * Math.Exp(-c.Exposure / ExposureScale) : 1;

        // The board and active cards' line. The grade is of the posted pay: the 1.5 is already in it.
        public static string ContrabandLine(Contract c) => $"Contraband: pays ×{Grade(c):0.00} of the posted pay now. Each hour a raider sees you cuts it.";

        double loadJd = double.NaN;   // the date a load or a date jump last started the ship tracked (StartSpan)

        // [design] Exposure accrues apart from the raid roll's gates (RouteEvents, a pending call, RouteGapDays, a chase): the
        // grade is not an event, and a chase does not stop it. The seen seconds are the ship's own (SeenSeconds): a chase's
        // fix on the ship adds none. Same gate as the raider's sight otherwise: not docked, not landed, and the roll's own
        // exponent PirateRate * seen hours, so a call's odds and the grade agree. The seen seconds are worked out once and
        // only when a contraband job is aboard, so a plain career pays nothing for this.
        // [design] The tracked start of a load (D8) is the roll's risk and not the grade's: a save and a resume must not take
        // half the pay. So while the last loud moment is that start, only the loud seconds count. Not for the roll.
        void Expose(SolarSystem sys, Ship ship, double jd, double simDt)
        {
            if (Docked != null || ship.Landed != null || simDt <= 0) return;
            double seenS = -1;
            foreach (var c in Active)
            {
                if (!c.Contraband) continue;
                if (seenS < 0) seenS = !spanOk ? 0 : ship.LastLoudJd == loadJd ? Math.Max(0, ship.LoudSeconds - spanLoud) : SeenSeconds(ship, jd, simDt);
                if (seenS <= 0) return;
                c.Exposure += PirateRate(sys, ship, c, jd) * seenS / 3600;
            }
        }

        // Board slot 9, Tharsis and Compact ports only: half of the days, a contraband run to one other port, if the trip is
        // risky. It draws no rng at a Guard port, and the slot comes last, so the jobs of slots 0-8 do not move.
        // [design] Pick, then test: one destination is drawn, then ONE Threat.Of runs for that route (about 1 ms, once per
        // port-day), and a route under the Serious line posts nothing that day. [design] The yardstick is a 1 g full burn
        // (cap 0) with the default hold: the poster does not know the buyer's hull. A departure from Vesta (belt, no patrol)
        // is about 2 h exposed at 1 g, so every Vesta route passes [calc, unrun].
        Contract Smuggle(System.Random rng, Port[] others, SolarSystem sys, double jd)
        {
            if (Docked.Owner != Faction.Tharsis && Docked.Owner != Faction.Compact) return null;
            if (others.Length == 0 || rng.NextDouble() >= 0.5) return null;
            var to = others[rng.Next(others.Length)];
            var here = sys[Docked.Body];
            var tb = sys[to.Body];
            if (here == null || tb == null) return null;
            double d = (sys.Pos(tb, jd) - sys.Pos(here, jd)).Mag;
            if (!Threat.Of(sys, here, D3.Zero, tb, jd, d, ExpressG * Ship.G0).Serious) return null;
            var c = Haul(rng, to, JobKind.Cargo, sys, jd);
            c.Contraband = true;   // [design] Tier stays null: the Toolkit board card prints a tier only when it is set
            c.Title = ContrabandGoods[rng.Next(ContrabandGoods.Length)]; // overwrites Haul's own title draw; the slot is last
            c.Pay = Math.Round(c.Pay * ContrabandPayFactor / 100) * 100;
            return c;
        }
    }
}
