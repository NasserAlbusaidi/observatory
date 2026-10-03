using System;

namespace FogOfWar
{
    // A free particle (or photon) on an equatorial Kerr geodesic, M = 1. Integrated in Mino time λ (dλ = dτ/r² in the
    // equatorial plane), where the radial motion is r'' = R'(r)/2 with no turning-point sign trouble:
    //   R(r)  = [E(r²+a²) − aL]² − Δ[μ²r² + (L − aE)²]       (μ² = 1 for matter, 0 for light)
    //   φ'    = a/Δ [E(r²+a²) − aL] − (aE − L)
    //   t'    = (r²+a²)/Δ [E(r²+a²) − aL] − a(aE − L)
    //   τ'    = r²
    // E and L are conserved for a geodesic; the sandbox's gas model changes them on purpose (Circularise, Drain).
    public struct Geo
    {
        public double r, w, phi, t, tau;   // w = dr/dλ
        public double E, L;
        public bool photon, alive, inside; // inside: crossed the horizon (proper time keeps running for the probe)

        public double Mu2 => photon ? 0 : 1;

        public static double Delta(double r, double a) => r * r - 2 * r + a * a;

        public double R(double rr, double a)
        {
            double P = E * (rr * rr + a * a) - a * L, K = L - a * E;
            return P * P - Delta(rr, a) * (Mu2 * rr * rr + K * K);
        }

        // (dr/dλ, d²r/dλ², dφ/dλ, dt/dλ, dτ/dλ)
        void D(double rr, double ww, double a, out double dr, out double dw, out double dph, out double dt, out double dtau)
        {
            double P = E * (rr * rr + a * a) - a * L, K = L - a * E, Dl = Delta(rr, a);
            dr = ww;
            dw = 2 * rr * E * P - (rr - 1) * (Mu2 * rr * rr + K * K) - Dl * Mu2 * rr;
            double inv = 1 / (Math.Abs(Dl) < 1e-12 ? 1e-12 : Dl);
            dph = a * P * inv + K;
            dt = (rr * rr + a * a) * P * inv - a * (a * E - L);
            dtau = rr * rr;
        }

        public double StepSize(double a, double k)
        {
            D(r, w, a, out _, out double dw, out double dph, out _, out _);
            return k / (Math.Abs(w) / r + Math.Sqrt(Math.Abs(dw) / r) + Math.Abs(dph) + 1e-9);
        }

        public void Step(double a, double h)
        {
            D(r, w, a, out double r1, out double w1, out double p1, out double t1, out double s1);
            D(r + 0.5 * h * r1, w + 0.5 * h * w1, a, out double r2, out double w2, out double p2, out double t2, out double s2);
            D(r + 0.5 * h * r2, w + 0.5 * h * w2, a, out double r3, out double w3, out double p3, out double t3, out double s3);
            D(r + h * r3, w + h * w3, a, out double r4, out double w4, out double p4, out double t4, out double s4);
            r += h / 6 * (r1 + 2 * r2 + 2 * r3 + r4);
            w += h / 6 * (w1 + 2 * w2 + 2 * w3 + w4);
            phi += h / 6 * (p1 + 2 * p2 + 2 * p3 + p4);
            t += h / 6 * (t1 + 2 * t2 + 2 * t3 + t4);
            tau += h / 6 * (s1 + 2 * s2 + 2 * s3 + s4);
            // hold r'² = R away from turning points
            double Rn = R(r, a);
            if (Rn > 1e-6 * r * r * r * r) w = (w < 0 ? -1 : 1) * Math.Sqrt(Rn);
        }

        public double Tdot(double a)
        {
            D(r, w, a, out _, out _, out _, out double dt, out _);
            return dt;
        }

        // Advance by dt of coordinate (far-away) time. Outside the horizon only: far away nothing ever crosses it, so the
        // particle stops just outside and is flagged inside (it is too redshifted to see).
        public int AdvanceT(double a, double dt, double k = 0.08, int maxSteps = 400)
        {
            double rh = Kerr.Horizon(a), target = t + dt;
            int n = 0;
            while (alive && !inside && t < target && n < maxSteps)
            {
                double h = StepSize(a, k);
                double td = Tdot(a);
                if (td > 0 && h * td > target - t) h = (target - t) / td;
                Step(a, h);
                n++;
                if (r < rh * 1.01) inside = true;
            }
            return n;
        }

        // Advance by dtau of the particle's own proper time (used for the probe once it is past the horizon).
        public void AdvanceTau(double a, double dtau, double k = 0.08, int maxSteps = 400)
        {
            double target = tau + dtau;
            int n = 0;
            while (alive && tau < target && n < maxSteps && r > 0.02)
            {
                double h = StepSize(a, k);
                double sd = r * r;
                if (h * sd > target - tau) h = (target - tau) / sd;
                Step(a, h);
                n++;
            }
            if (r <= 0.02) alive = false;
        }

        // ---- 4-velocity (contravariant) in Boyer–Lindquist, equatorial
        public void FourVelocity(double a, out double ut, out double ur, out double uph)
        {
            double inv = 1 / (r * r);
            D(r, w, a, out _, out _, out double dph, out double dt, out _);
            ut = dt * inv; ur = w * inv; uph = dph * inv;
        }

        // Velocity relative to the local zero-angular-momentum observer, in its (r̂, φ̂) frame (fraction of c).
        public void ZamoVelocity(double a, out double vr, out double vph)
        {
            FourVelocity(a, out double ut, out double ur, out double uph);
            double D2 = Delta(r, a), S2 = (r * r + a * a) * (r * r + a * a) - a * a * D2;
            double lapse = Math.Sqrt(Math.Max(r * r * D2 / S2, 1e-12)), omega = 2 * a * r / S2, varpi = Math.Sqrt(S2) / r;
            double gamma = lapse * ut;
            vr = Math.Sqrt(r * r / Math.Max(D2, 1e-12)) * ur / gamma;
            vph = varpi * (uph - omega * ut) / gamma;
        }

        // ---- the gas model (not geodesic): change E or L at fixed position and radial speed
        // Solve R(r; E, L) = w² for E (the future-directed root).
        public void SolveE(double a)
        {
            double r2a = r * r + a * a, Dl = Delta(r, a);
            double A = r2a * r2a - Dl * a * a;
            double B = 4 * a * r * L;
            double C = (a * a - Dl) * L * L - Dl * Mu2 * r * r - w * w;
            double disc = B * B - 4 * A * C;
            if (disc < 0) disc = 0;
            E = (B + Math.Sqrt(disc)) / (2 * A);
        }

        // ---- launches
        public static Geo Circular(double r, double a)
        {
            double s = Math.Sqrt(r), den = Math.Pow(r, 0.75) * Math.Sqrt(Math.Max(r * s - 3 * s + 2 * a, 1e-12));
            return new Geo { r = r, w = 0, E = (r * s - 2 * s + a) / den, L = (r * r - 2 * a * s + a * a) / den, alive = true };
        }

        // Bound orbit with pericentre rp and apocentre ra (Newton on E, L from the Newtonian guess).
        public static Geo Bound(double rp, double ra, double a)
        {
            double p = 2 * rp * ra / (rp + ra), e = (ra - rp) / (ra + rp);
            var g = new Geo { E = Math.Sqrt(1 - (1 - e * e) / p), L = Math.Sqrt(p), alive = true };
            for (int it = 0; it < 40; it++)
            {
                double f1 = g.R(rp, a), f2 = g.R(ra, a);
                const double eps = 1e-7;
                var gE = g; gE.E += eps; var gL = g; gL.L += eps;
                double a11 = (gE.R(rp, a) - f1) / eps, a12 = (gL.R(rp, a) - f1) / eps;
                double a21 = (gE.R(ra, a) - f2) / eps, a22 = (gL.R(ra, a) - f2) / eps;
                double det = a11 * a22 - a12 * a21;
                if (Math.Abs(det) < 1e-30) break;
                double dE = (f1 * a22 - f2 * a12) / det, dL = (a11 * f2 - a21 * f1) / det;
                g.E -= dE; g.L -= dL;
                if (Math.Abs(dE) + Math.Abs(dL) < 1e-13) break;
            }
            g.r = ra; g.w = 0;
            return g;
        }

        // Parabolic (E = 1) prograde orbit with pericentre rp, placed at r0 on the way in. Below the marginally bound
        // radius there is no such orbit: it plunges, with the angular momentum it would have had at r_mb scaled down.
        public static Geo Parabolic(double rp, double r0, double a, out bool plunges)
        {
            var g = new Geo { E = 1, alive = true };
            double rmb = Kerr.MarginallyBound(a);
            plunges = rp <= rmb * 1.001;
            if (plunges)
            {
                g.L = Lparabolic(rmb * 1.001, a) * Math.Sqrt(rp / rmb);
            }
            else g.L = Lparabolic(rp, a);
            g.r = r0;
            double R0 = g.R(r0, a);
            g.w = -Math.Sqrt(Math.Max(R0, 0));
            return g;
        }

        // L for which R(rp) = 0 with E = 1 (smallest positive root: the orbit coming in from infinity turns at rp).
        static double Lparabolic(double rp, double a)
        {
            var g = new Geo { E = 1 };
            double lo = 0, hi = 2 * Math.Sqrt(2 * rp) + 4;
            g.L = hi;
            for (int it = 0; it < 200 && g.R(rp, a) > 0; it++) { hi *= 1.5; g.L = hi; }
            for (int it = 0; it < 100; it++)
            {
                g.L = 0.5 * (lo + hi);
                if (g.R(rp, a) > 0) lo = g.L; else hi = g.L;
            }
            return 0.5 * (lo + hi);
        }

        // E, L of matter at radius r moving with the given 4-velocity components u^r, u^φ (equatorial).
        public static Geo FromVelocity(double r, double phi, double ur, double uph, double a, bool inward)
        {
            double gtt = -(1 - 2 / r), gtp = -2 * a / r, gpp = r * r + a * a + 2 * a * a / r, grr = r * r / Delta(r, a);
            // gtt ut² + 2 gtp ut uph + gpp uph² + grr ur² = −1
            double A = gtt, B = 2 * gtp * uph, C = gpp * uph * uph + grr * ur * ur + 1;
            double disc = Math.Max(B * B - 4 * A * C, 0);
            double ut = (-B - Math.Sqrt(disc)) / (2 * A);
            if (ut < 0) ut = (-B + Math.Sqrt(disc)) / (2 * A);
            var g = new Geo { r = r, phi = phi, alive = true };
            g.E = -(gtt * ut + gtp * uph);
            g.L = gtp * ut + gpp * uph;
            g.w = r * r * ur;
            return g;
        }
    }
}
