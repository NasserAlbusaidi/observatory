using System;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Hot matter in vacuum (docs/design-combat.md "Impact effects"): colour from BlackHoleScene.PlanckColor (unit luminance),
    // and how much of a blackbody's power the eye sees, so a cooling glow dims faster than T⁴ as its peak moves into the infrared.
    public static class Blackbody
    {
        public const float SunK = 5772;
        static readonly double sunShare = Luminous(SunK);

        public static Color Colour(float kelvin) => BlackHoleScene.PlanckColor(kelvin);

        // Luminance per watt at T over the Sun's: 1 at 5,772 K, 0.22 at 3,000 K, 0.001 at 1,500 K
        public static float VisibleShare(float kelvin) => kelvin < 500 ? 0 : (float)(Luminous(kelvin) / sunShare);

        // ∫ ȳ(λ)·B(λ, T) dλ / T⁴ with Planck's B up to its constants (they cancel in the ratio); ȳ is the CIE 1931 fit of
        // Wyman, Sloan and Shirley (2013) that PlanckColor uses
        static double Luminous(double T)
        {
            double Y = 0;
            for (double l = 360; l <= 830; l += 1)
            {
                double lm = l * 1e-9;
                double B = 1 / (lm * lm * lm * lm * lm * (Math.Exp(1.438776877e-2 / (lm * T)) - 1));
                Y += B * (0.821 * G(l, 568.8, 46.9, 40.5) + 0.286 * G(l, 530.9, 16.3, 31.1));
            }
            return Y / (T * T * T * T);
        }

        static double G(double x, double mu, double s1, double s2)
        {
            double t = (x - mu) / (x < mu ? s1 : s2);
            return Math.Exp(-0.5 * t * t);
        }
    }
}
