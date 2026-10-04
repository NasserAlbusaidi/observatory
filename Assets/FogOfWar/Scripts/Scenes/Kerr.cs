using System;
using UnityEngine;

namespace FogOfWar
{
    // Kerr black hole helpers in geometric units (G = c = M = 1, so rs = 2). Spin a = J/M in [0, 1).
    // World space puts the hole at the origin with its spin along +y; Boyer–Lindquist (r, θ, φ) map to
    // x = √(r²+a²) sin θ cos φ, y = r cos θ, z = √(r²+a²) sin θ sin φ, so φ grows from +x towards +z.
    public static class Kerr
    {
        public static double Horizon(double a) => 1 + Math.Sqrt(1 - a * a);

        // Bardeen–Press–Teukolsky ISCO; prograde = true for orbits co-rotating with the hole.
        public static double Isco(double a, bool prograde = true)
        {
            double z1 = 1 + Math.Pow(1 - a * a, 1.0 / 3) * (Math.Pow(1 + a, 1.0 / 3) + Math.Pow(1 - a, 1.0 / 3));
            double z2 = Math.Sqrt(3 * a * a + z1 * z1);
            double s = Math.Sqrt((3 - z1) * (3 + z1 + 2 * z2));
            return prograde ? 3 + z2 - s : 3 + z2 + s;
        }

        // Circular photon orbit in the equatorial plane.
        public static double PhotonOrbit(double a, bool prograde = true) =>
            2 * (1 + Math.Cos(2.0 / 3 * Math.Acos(prograde ? -a : a)));

        // Marginally bound circular orbit: a parabolic (E = 1) orbit with pericentre inside it is swallowed.
        public static double MarginallyBound(double a, bool prograde = true) => prograde ? 2 - a + 2 * Math.Sqrt(1 - a) : 2 + a + 2 * Math.Sqrt(1 + a);

        // Page–Thorne flux (shape only, as in BlackHole.shader).
        // x1, x2, x3 are the roots of x³ − 3x + 2a, so a = xᵢ(3 − xᵢ²)/2 and the coefficient (xᵢ − a)²/xᵢ equals
        // xᵢ(xᵢ² − 1)²/4: no division by x2, which is 0 at a = 0. Spin 0 then gives the exact Schwarzschild flux
        // [x − x0 − (√3/2) ln((x − √3)(x0 + √3) / ((x + √3)(x0 − √3)))] / (x⁴(x³ − 3x)) with no spin substitute (#35).
        public static double PageThorne(double r, double a)
        {
            double x = Math.Sqrt(r), x0 = Math.Sqrt(Isco(a));
            if (x <= x0) return 0;
            double ac = Math.Acos(a);
            double x1 = 2 * Math.Cos((ac - Math.PI) / 3), x2 = 2 * Math.Cos((ac + Math.PI) / 3), x3 = -2 * Math.Cos(ac / 3);
            double C(double xi) => xi * (xi * xi - 1) * (xi * xi - 1) / 4;
            double br = x - x0 - 1.5 * a * Math.Log(x / x0)
                - 3 * C(x1) / ((x1 - x2) * (x1 - x3)) * Math.Log((x - x1) / (x0 - x1))
                - 3 * C(x2) / ((x2 - x1) * (x2 - x3)) * Math.Log((x - x2) / (x0 - x2))
                - 3 * C(x3) / ((x3 - x1) * (x3 - x2)) * Math.Log((x - x3) / (x0 - x3));
            return Math.Max(br / (x * x * x * x * (x * x * x - 3 * x + 2 * a)), 0);
        }

        // Radius (M units) and value of the flux peak.
        public static (double r, double f) FluxPeak(double a)
        {
            double r0 = Isco(a), best = 0, rb = r0;
            for (int i = 1; i <= 4000; i++)
            {
                double r = r0 * Math.Pow(30, i / 4000.0), f = PageThorne(r, a);
                if (f > best) { best = f; rb = r; }
            }
            return (rb, best);
        }

        // Prograde circular equatorial orbit: angular velocity dφ/dt and u^t (= dt/dτ).
        public static double Omega(double r, double a) => 1 / (Math.Pow(r, 1.5) + a);
        public static double OrbitUt(double r, double a)
        {
            double s = Math.Sqrt(r);
            return (r * s + a) / (Math.Pow(r, 0.75) * Math.Sqrt(Math.Max(r * s - 3 * s + 2 * a, 1e-12)));
        }

        public static void ToBL(Vector3 world, double a, out double r, out double mu, out double phi)
        {
            double x = world.x, y = world.y, z = world.z;
            double t = x * x + y * y + z * z - a * a;
            r = Math.Sqrt(0.5 * (t + Math.Sqrt(t * t + 4 * a * a * y * y)));
            mu = r > 1e-9 ? Math.Max(-1, Math.Min(1, y / r)) : 0;
            phi = Math.Atan2(z, x);
        }

        public static Vector3 FromBL(double r, double mu, double phi, double a)
        {
            double s = Math.Sqrt(r * r + a * a), st = Math.Sqrt(Math.Max(1 - mu * mu, 0));
            return new Vector3((float)(s * st * Math.Cos(phi)), (float)(r * mu), (float)(s * st * Math.Sin(phi)));
        }

        // Upload the camera's Boyer–Lindquist position, local orthonormal triad and ZAMO metric functions.
        // pos is in rs units (scene units); the shader works in M units.
        public static void SetCamera(Material m, Vector3 posRs, double a)
        {
            var p = posRs * 2f;
            ToBL(p, a, out double r, out double mu, out double phi);
            double rh = Horizon(a);
            if (r < rh * 1.02) { r = rh * 1.02; p = FromBL(r, mu, phi, a); }
            double st = Math.Sqrt(Math.Max(1 - mu * mu, 1e-12)), s = Math.Sqrt(r * r + a * a);
            double cp = Math.Cos(phi), sp = Math.Sin(phi);
            var er = new Vector3((float)(r / s * st * cp), (float)mu, (float)(r / s * st * sp)).normalized;
            var eth = new Vector3((float)(s * mu * cp), (float)(-r * st), (float)(s * mu * sp));
            eth = (eth - Vector3.Dot(eth, er) * er).normalized;
            var eph = new Vector3((float)-sp, 0, (float)cp);
            double rho2 = r * r + a * a * mu * mu, D = r * r - 2 * r + a * a;
            double S2 = (r * r + a * a) * (r * r + a * a) - a * a * D * st * st;
            double lapse = Math.Sqrt(rho2 * D / S2), omega = 2 * a * r / S2, varpi = Math.Sqrt(S2 / rho2) * st;
            m.SetVector("_CamBL", new Vector4((float)r, (float)mu, (float)phi, (float)st));
            m.SetVector("_CamEr", er);
            m.SetVector("_CamEth", eth);
            m.SetVector("_CamEph", eph);
            m.SetVector("_CamMet", new Vector4((float)lapse, (float)omega, (float)varpi, (float)Math.Sqrt(rho2)));
            m.SetFloat("_CamDelta", (float)D);
            m.SetFloat("_CamTheta", (float)Math.Acos(mu));
            m.SetFloat("_RMax", (float)Math.Max(400, 8 * r));
        }
    }
}
