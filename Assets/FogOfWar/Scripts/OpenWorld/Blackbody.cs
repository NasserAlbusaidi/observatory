using System;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Hot matter in vacuum (docs/design-combat.md "Impact effects"): colour from PlanckColor (unit luminance),
    // and how much of a blackbody's power the eye sees, so a cooling glow dims faster than T⁴ as its peak moves into the infrared.
    public static class Blackbody
    {
        public const float SunK = 5772;
        static readonly double sunShare = Luminous(SunK);

        public static Color Colour(float kelvin) => PlanckColor(kelvin);

        // Planck's law × CIE 1931 2° colour-matching functions (the multi-lobe Gaussian fit of Wyman, Sloan and Shirley
        // 2013), 360–830 nm, → XYZ → linear sRGB (D65), scaled to luminance 1; out-of-gamut colours are mixed with white
        // of the same luminance until no channel is negative.
        public static Color PlanckColor(double T)
        {
            double X = 0, Y = 0, Z = 0;
            for (double l = 360; l <= 830; l += 1)
            {
                double lm = l * 1e-9;
                double B = 1 / (lm * lm * lm * lm * lm * (Math.Exp(1.438776877e-2 / (lm * T)) - 1));
                X += B * (1.056 * G(l, 599.8, 37.9, 31.0) + 0.362 * G(l, 442.0, 16.0, 26.7) - 0.065 * G(l, 501.1, 20.4, 26.2));
                Y += B * (0.821 * G(l, 568.8, 46.9, 40.5) + 0.286 * G(l, 530.9, 16.3, 31.1));
                Z += B * (1.217 * G(l, 437.0, 11.8, 36.0) + 0.681 * G(l, 459.0, 26.0, 13.8));
            }
            X /= Y; Z /= Y; Y = 1;
            double r = 3.2406 * X - 1.5372 * Y - 0.4986 * Z;
            double g = -0.9689 * X + 1.8758 * Y + 0.0415 * Z;
            double b = 0.0557 * X - 0.2040 * Y + 1.0570 * Z;
            double m = Math.Min(r, Math.Min(g, b));
            if (m < 0) { double t = -m / (1 - m); r += (1 - r) * t; g += (1 - g) * t; b += (1 - b) * t; }
            return new Color((float)r, (float)g, (float)b, 1);
        }

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
