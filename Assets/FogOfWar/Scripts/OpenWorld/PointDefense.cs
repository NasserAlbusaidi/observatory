using System;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Combat Phase 4 (docs/design-combat.md, "PDCs" and "The pass"): a PDC mount's chance to kill a missile in its last
    // EnvelopeKm. Rounds are not simulated: a kill rate λ = n·A/(π·r²) on the spread r = r₀ + ½·a·t², t = range / u, and
    // P_survive = exp(−∫λ dt), integrated once at impact in closed form. Ranges in the integral are in m.
    public static class PointDefense
    {
        public const double RoundKms = 2;     // [design] u, the round's speed
        public const double Rate = 100;       // [design] n, rounds/s a mount
        public const double TargetM2 = 0.5;   // [design] A, what a round must hit to kill a missile
        public const double FloorM = 2;       // [design] r₀, the aim floor
        public const double JinkG = 20;       // [design] the missile's random jink, until StraightM out
        public const double StraightM = 200;  // [design] the last run is straight: r = r₀
        public const double EnvelopeKm = 5;   // [design] the mounts fire inside this range
        public const double ArcDeg = 100;     // [design] a mount's half-angle about its zenith: a hemisphere and 10° more
        public const int AllMounts = 0x3F;    // a fitted mask with PDC_0..5 all fitted and working
        static readonly float ArcCos = (float)Math.Cos(ArcDeg * Math.PI / 180); // −sin 10° = −0.1736

        // c in r = r₀ + c·x²: ½·a·(x/u)², in 1/m. 2.45e-5: 6.1 m at 500 m, 613 m at 5 km [calc]
        public static double SpreadC => JinkG * Ship.G0 * 1e3 / (2 * RoundKms * 1e3 * RoundKms * 1e3);

        // ∫ dx / (r₀ + c·x²)², the closed form; x / r₀² when c = 0
        public static double Antiderivative(double x, double r0, double c) =>
            c <= 0 ? x / (r0 * r0) : x / (2 * r0 * (r0 + c * x * x)) + Math.Atan(x * Math.Sqrt(c / r0)) / (2 * r0 * Math.Sqrt(r0 * c));

        // ∫ dx / r² over the jink (EnvelopeKm in to StraightM) and over the straight run (StraightM in to 0), in 1/m
        public static double JinkIntegral => Antiderivative(EnvelopeKm * 1e3, FloorM, SpreadC) - Antiderivative(StraightM, FloorM, SpreadC);
        public static double StraightIntegral => Antiderivative(StraightM, FloorM, 0);

        // n·A/(π·V) in m: one mount's exponent per unit of ∫ dx / r² at closing speed V (dt = dx / V)
        public static double PerIntegral(double closingKms) => Rate * TargetM2 / (Math.PI * closingKms * 1e3);

        // one mount's exponent for a whole pass: 0.139 jink + 0.398 straight = 0.537 at 2 km/s [calc]
        public static double Exponent(double closingKms) => PerIntegral(closingKms) * (JinkIntegral + StraightIntegral);

        // 0.42, 0.66 and 0.80 for 1, 2 and 3 mounts at 2 km/s [calc]
        public static double KillChance(int mountsBearing, double closingKms) =>
            mountsBearing <= 0 ? 0 : 1 - Math.Exp(-mountsBearing * Exponent(closingKms));

        // the one draw a missile gets, at impact: the pass takes 2.5 s, so the attitude then stands for all of it [design]
        public static bool Kills(System.Random rng, int mountsBearing, double closingKms) => rng.NextDouble() < KillChance(mountsBearing, closingKms);

        // A mount covers ArcDeg about its zenith; the hull blocks nothing [design]. 10° past the hemisphere, so a missile dead
        // on the nose or the tail, on every mount's horizon, is in every mount's arc. Both vectors are unit and in the body frame
        // (Hull.Mounts), and d points from the ship toward the missile (its position relative to the ship), not along its flight.
        public static bool Bears(Vector3 zenithBody, Vector3 dirBody) => Vector3.Dot(zenithBody, dirBody) > ArcCos;

        // the fitted mask of a 2, 4 or 6 mount part: the first `count` markers of the hull's fit order
        public static int FitMask(Hull h, int count)
        {
            int mask = 0;
            for (int i = 0; i < Math.Min(count, h.MountFit.Length); i++) mask |= 1 << h.MountFit[i];
            return mask;
        }

        // the mounts that bear on dirBody: bit i of fitted is PDC_i, fitted and working
        public static int Bearing(Hull h, int fitted, Vector3 dirBody)
        {
            int n = 0;
            for (int i = 0; i < h.Mounts.Length; i++)
                if ((fitted & (1 << i)) != 0 && Bears(h.Mounts[i], dirBody)) n++;
            return n;
        }
    }
}
