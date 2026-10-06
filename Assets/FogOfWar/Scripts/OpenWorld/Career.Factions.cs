using System;

namespace FogOfWar.OpenWorld
{
    // Who owns the ports (docs/design-world.md, W1). Saved by index: add a faction at the end. None is for a job no port
    // posted, such as a distress rescue near a survey body.
    public enum Faction { None, Guard, Tharsis, Compact, Raiders }

    public class FactionInfo
    {
        public string Name, Blurb;
    }

    // Standing: what each faction thinks of the pilot. A job done on time raises it with the faction whose port posted
    // the job; a failure lowers it by more, so dropping work costs something (audit #8). The tier sets the pay on that
    // faction's boards.
    public partial class Career
    {
        // working names: the final ones are an open decision, so every name and blurb is here
        public static readonly FactionInfo[] Factions =
        {
            new FactionInfo { Name = "Independent", Blurb = "" },
            new FactionInfo { Name = "Cislunar Guard", Blurb = "Earth's coast guard. It patrols from Earth orbit to Luna, runs customs and answers distress calls. The only fleet with capital ships." },
            new FactionInfo { Name = "Tharsis Company", Blurb = "Mars is a company town. The Company owns Mars orbit and mines Vesta, which buys its water from the Compact. It asks its contractors few questions." },
            new FactionInfo { Name = "Ice Compact", Blurb = "The water ports of the belt and the outer moons. Water flows toward the Sun, and the Compact wants Earth to pay more for it." },
            // no port, so no board and no standing shown before weapons (W3; havens in Threat.cs)
            new FactionInfo { Name = "Raiders", Blurb = "They work the belt from havens on Pallas, whose orbit tilts far out of the ports' plane, and Hektor, a Jupiter Trojan. They take cargo from ships that are slow and far from a patrol." },
        };

        public static bool OwnsPort(Faction f) => Array.Exists(Ports, p => p.Owner == f);

        public const int StandingGain = 1, StandingLoss = 2, StandingMin = -20, StandingMax = 40;

        // from the bottom: the standing each tier starts at, its name and the pay on that faction's boards. Neutral is
        // exactly 1, so a new career's boards pay what they always did.
        static readonly (int From, string Name, double Pay)[] Tiers =
        {
            (int.MinValue, "Distrusted", 0.90), (-4, "Neutral", 1), (5, "Known", 1.05), (15, "Trusted", 1.10), (30, "Valued", 1.15),
        };

        readonly int[] standing = new int[Factions.Length];

        public int Standing(Faction f) => standing[(int)f];
        static int TierOf(int s) { int i = Tiers.Length - 1; while (s < Tiers[i].From) i--; return i; }
        public string TierName(Faction f) => Tiers[TierOf(Standing(f))].Name;
        public double PayFactor(Faction f) => Tiers[TierOf(Standing(f))].Pay;
        public string PayText(Faction f)
        {
            double p = PayFactor(f) - 1;
            return Math.Abs(p) < 1e-9 ? "standard pay" : $"{Math.Abs(p) * 100:0} % {(p > 0 ? "more" : "less")} pay";
        }

        public static Faction OwnerOf(Contract c) => PortAt(c.From)?.Owner ?? Faction.None;

        // A change of standing with the faction behind job c. A new tier says so and rebuilds the day's board, so its pay
        // follows at once (the same seed posts the same jobs).
        void ChangeStanding(Contract c, int delta)
        {
            var f = OwnerOf(c);
            if (f == Faction.None || delta == 0 || Sandbox) return;
            int was = TierOf(standing[(int)f]);
            standing[(int)f] = Math.Max(StandingMin, Math.Min(StandingMax, standing[(int)f] + delta));
            int now = TierOf(standing[(int)f]);
            if (now == was) return;
            boardDay = long.MinValue;
            Say(Happening.Standing, $"{Factions[(int)f].Name}: you are {Tiers[now].Name}. Their boards: {PayText(f)}", now > was);
        }
    }
}
