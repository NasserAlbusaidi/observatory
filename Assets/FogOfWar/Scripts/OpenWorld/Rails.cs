using System;

namespace FogOfWar.OpenWorld
{
    // Two-body propagation with the universal variable (Vallado, Fundamentals of Astrodynamics, alg. 8). Exact for
    // elliptic, parabolic and hyperbolic arcs at any time step, so a coasting ship rides "on rails" under time
    // compression instead of being integrated in thousands of substeps.
    public static class Rails
    {
        static void Stumpff(double z, out double c, out double s)
        {
            if (z > 1e-6) { double q = Math.Sqrt(z); c = (1 - Math.Cos(q)) / z; s = (q - Math.Sin(q)) / (q * q * q); }
            else if (z < -1e-6) { double q = Math.Sqrt(-z); c = (Math.Cosh(q) - 1) / -z; s = (Math.Sinh(q) - q) / (q * q * q); }
            else { c = 0.5 - z / 24; s = 1.0 / 6 - z / 120; }
        }

        public static void Propagate(D3 r0, D3 v0, double mu, double dt, out D3 r, out D3 v)
        {
            double r0m = r0.Mag, vr0 = D3.Dot(r0, v0) / r0m, sqmu = Math.Sqrt(mu);
            double alpha = 2 / r0m - v0.Sqr / mu; // 1/a
            if (alpha > 1e-12)
            {
                // bound orbit: whole revolutions change nothing
                double T = 2 * Math.PI / Math.Sqrt(mu * alpha * alpha * alpha);
                dt = Math.IEEERemainder(dt, T);
            }
            double chi = alpha > 1e-12 ? sqmu * alpha * dt : Math.Sign(dt) * sqmu * Math.Abs(dt) / r0m;
            double c = 0.5, s = 1.0 / 6;
            for (int k = 0; k < 60; k++)
            {
                double z = alpha * chi * chi;
                Stumpff(z, out c, out s);
                double F = r0m * vr0 / sqmu * chi * chi * c + (1 - alpha * r0m) * chi * chi * chi * s + r0m * chi - sqmu * dt;
                double dF = r0m * vr0 / sqmu * chi * (1 - z * s) + (1 - alpha * r0m) * chi * chi * c + r0m;
                double step = F / dF;
                chi -= step;
                if (Math.Abs(step) < 1e-9 * Math.Max(1, Math.Abs(chi))) break;
            }
            Stumpff(alpha * chi * chi, out c, out s);
            double f = 1 - chi * chi / r0m * c, g = dt - chi * chi * chi / sqmu * s;
            r = r0 * f + v0 * g;
            double rm = r.Mag;
            double fdot = sqmu / (rm * r0m) * (alpha * chi * chi * chi * s - chi), gdot = 1 - chi * chi / rm * c;
            v = r0 * fdot + v0 * gdot;
        }

        // Orbital period around mu, or +∞ when unbound.
        public static double Period(D3 r, D3 v, double mu)
        {
            double alpha = 2 / r.Mag - v.Sqr / mu;
            return alpha > 1e-12 ? 2 * Math.PI / Math.Sqrt(mu * alpha * alpha * alpha) : double.PositiveInfinity;
        }
    }
}
