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

        // Returns false, with r and v still set to the best estimate, when the solve did not converge or the state is
        // not finite; callers must not adopt such a state (issue #5). F(χ) below is strictly increasing (dF/dχ is
        // the radius), so Newton runs inside a bracket and falls back to bisection when a step would leave it. Plain
        // Newton overshot on fast hyperbolic passes and returned finite garbage (1e8 to 1e99 km off).
        public static bool Propagate(D3 r0, D3 v0, double mu, double dt, out D3 r, out D3 v)
        {
            double r0m = r0.Mag, vr0 = D3.Dot(r0, v0) / r0m, sqmu = Math.Sqrt(mu);
            double alpha = 2 / r0m - v0.Sqr / mu; // 1/a
            if (alpha > 1e-12)
            {
                // bound orbit: whole revolutions change nothing
                double T = 2 * Math.PI / Math.Sqrt(mu * alpha * alpha * alpha);
                dt = Math.IEEERemainder(dt, T);
            }
            if (dt == 0 || !(r0m > 0)) { r = r0; v = v0; return r0m > 0; }
            double cA = r0m * vr0 / sqmu, cB = 1 - alpha * r0m, target = sqmu * dt, sgn = Math.Sign(dt);
            double c, s;
            double Eval(double x, out double dF)
            {
                double z = alpha * x * x;
                Stumpff(z, out var cc, out var ss);
                dF = cA * x * (1 - z * ss) + cB * x * x * cc + r0m;
                return cA * x * x * cc + cB * x * x * x * ss + r0m * x - target;
            }
            // bracket: lo on the near side of the root (sgn·F ≤ 0), hi beyond it (sgn·F > 0, or overflowed, which
            // only happens far past the root since F grows without bound)
            double guess = alpha > 1e-12 ? sqmu * alpha * dt : sgn * sqmu * Math.Abs(dt) / r0m;
            double lo = 0, hi = guess;
            for (int k = 0; k < 200; k++)
            {
                double Fh = Eval(hi, out _);
                if (double.IsNaN(Fh) || double.IsInfinity(Fh) || sgn * Fh > 0) break;
                lo = hi; hi *= 2;
            }
            double chi = 0.5 * (lo + hi), tol = 1e-13 * (Math.Abs(target) + r0m * Math.Abs(hi));
            bool ok = false;
            double width2 = double.PositiveInfinity, width1 = double.PositiveInfinity;
            for (int k = 0; k < 400; k++)
            {
                double F = Eval(chi, out var dF);
                if (double.IsNaN(F) || double.IsInfinity(F)) { hi = chi; chi = 0.5 * (lo + hi); continue; }
                if (Math.Abs(F) <= tol) { ok = true; break; }
                if (sgn * F > 0) hi = chi; else lo = chi;
                double next = chi - F / dF;
                // bisect when a step would leave the bracket, or when Newton crawls (from far beyond the root of the
                // exponential hyperbolic F it moves only about √|a| per step)
                double width = Math.Abs(hi - lo);
                if (!(sgn > 0 ? next > lo && next < hi : next < lo && next > hi) || width > 0.5 * width2) next = 0.5 * (lo + hi);
                width2 = width1; width1 = width;
                if (next == chi) { ok = true; break; } // as close as doubles get
                chi = next;
            }
            Stumpff(alpha * chi * chi, out c, out s);
            double f = 1 - chi * chi / r0m * c, g = dt - chi * chi * chi / sqmu * s;
            r = r0 * f + v0 * g;
            double rm = r.Mag;
            double fdot = sqmu / (rm * r0m) * (alpha * chi * chi * chi * s - chi), gdot = 1 - chi * chi / rm * c;
            v = r0 * fdot + v0 * gdot;
            if (!(rm > 0) || double.IsInfinity(rm) || double.IsNaN(v.Sqr) || double.IsInfinity(v.Sqr)) return false;
            // energy must survive the arc: the unconverged states of issue #5 were off by 41 %
            double e0 = v0.Sqr / 2 - mu / r0m, e1 = v.Sqr / 2 - mu / rm;
            return ok && Math.Abs(e1 - e0) <= 1e-9 * (v0.Sqr / 2 + mu / r0m);
        }

        // Issue #6: the first time in [0, dt] (dt > 0) at which the arc comes within radius R of the focus, or −1 if it
        // stays outside. Checking only the arc's end misses a pass that dips inside and climbs out again. Radius falls
        // monotonically from apoapsis (or the start, if already inbound) to periapsis, so each leg is bisected.
        public static double FirstCrossing(D3 r0, D3 v0, double mu, double R, double dt)
        {
            double r0m = r0.Mag;
            if (r0m <= R) return 0;
            if (!(dt > 0)) return -1;
            var h = D3.Cross(r0, v0);
            double e = (D3.Cross(v0, h) * (1 / mu) - r0 * (1 / r0m)).Mag;
            if (h.Sqr / mu / (1 + e) >= R) return -1; // periapsis clears it
            double alpha = 2 / r0m - v0.Sqr / mu;
            bool bound = alpha > 1e-12;
            double T = bound ? 2 * Math.PI / Math.Sqrt(mu * alpha * alpha * alpha) : double.PositiveInfinity;
            double Vr(double t) { if (t <= 0) return D3.Dot(r0, v0); Propagate(r0, v0, mu, t, out var p, out var w); return D3.Dot(p, w); }
            double Rad(double t) { if (t <= 0) return r0m; Propagate(r0, v0, mu, t, out var p, out _); return p.Mag; }
            // the inbound leg starts now, or at the next apoapsis (within half a period; never, on an open orbit)
            double t0 = 0;
            if (D3.Dot(r0, v0) > 0)
            {
                if (!bound) return -1;
                double lo = 0, hi = Math.Min(T / 2, dt);
                if (Vr(hi) > 0) return -1;
                for (int k = 0; k < 80 && hi - lo > 1e-9 * T; k++) { double m = 0.5 * (lo + hi); if (Vr(m) > 0) lo = m; else hi = m; }
                t0 = hi;
            }
            // ...and ends at periapsis or at dt, whichever is first
            double t1 = bound ? Math.Min(dt, t0 + T / 2) : dt;
            if (Vr(t1) > 0)
            {
                double lo = t0, hi = t1;
                for (int k = 0; k < 80 && hi - lo > 1e-9 * Math.Max(1, hi); k++) { double m = 0.5 * (lo + hi); if (Vr(m) > 0) hi = m; else lo = m; }
                t1 = hi;
            }
            if (Rad(t1) > R) return -1; // still above it at dt
            double a = t0, b = t1;
            for (int k = 0; k < 80 && b - a > 1e-6; k++) { double m = 0.5 * (a + b); if (Rad(m) > R) a = m; else b = m; }
            return b; // just inside
        }

        // Orbital period around mu, or +∞ when unbound.
        public static double Period(D3 r, D3 v, double mu)
        {
            double alpha = 2 / r.Mag - v.Sqr / mu;
            return alpha > 1e-12 ? 2 * Math.PI / Math.Sqrt(mu * alpha * alpha * alpha) : double.PositiveInfinity;
        }
    }
}
