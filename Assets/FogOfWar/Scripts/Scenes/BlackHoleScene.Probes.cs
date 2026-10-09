using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // The black hole sandbox's probes: dropping one on an orbit, and the ride-along camera that moves with it.
    public partial class BlackHoleScene
    {
        // ------------------------------------------------------------------------------------------ probes

        public void DropProbe(int kind)
        {
            CheckExperiment();
            double a = A;
            Geo g;
            bool photon = false;
            switch (kind)
            {
                case 0: g = Geo.Circular(Kerr.Isco(a) * 1.03, a); break;
                case 1: g = Geo.Bound(14, 60, a); break;
                case 2: g = ZoomWhirl(a); break;
                case 3:
                    g = new Geo { r = 40, w = 0, L = 0, alive = true }; g.SolveE(a); break;
                case 4:
                    g = new Geo { r = 70, E = 1, L = 0, alive = true }; g.w = -Math.Sqrt(Math.Max(g.R(70, a), 0)); break;
                default:
                    {
                        // the photon orbit's own b, started 1e-8 outside or inside it: the orbit is unstable by e^{2π} per
                        // turn, so the light circles about 3 times, then escapes (outside) or falls in (inside)
                        double rph = Kerr.PhotonOrbit(a), D = Math.Sqrt(Geo.Delta(rph, a));
                        g = new Geo { r = rph * (kind == 5 ? 1 + 1e-8 : 1 - 1e-8), w = 0, E = 1, L = (rph * rph + a * a + a * D) / (a + D), photon = true, alive = true };
                        photon = true;
                        break;
                    }
            }
            g.phi = kind == 1 || kind == 2 ? Math.PI / 2 : 0;
            g.t = simT;
            var p = new Probe { name = ProbeNames[kind], g = g, photon = photon, t0 = simT, tau0 = g.tau };
            if (probes.Count >= 3) { if (riding == probes[0]) StopRide(); probes.RemoveAt(0); }   // same detach as the toggle (BH audit #31)
            probes.Add(p);
            if (ride && !photon) StartRide(p);
        }

        // An orbit just below the energy of an unstable circular orbit: it whirls round near there several times
        // before zooming out again.
        static Geo ZoomWhirl(double a)
        {
            double rmb = Kerr.MarginallyBound(a), risco = Kerr.Isco(a);
            var u = Geo.Circular(rmb + 0.35 * (risco - rmb), a);
            var g = new Geo { E = u.E * (1 - 3e-5), L = u.L, alive = true };
            // apocentre: the outer root of R(r) = 0
            double lo = u.r * 1.001, hi = 2000;
            while (g.R(lo, a) < 0 && lo < hi) lo *= 1.01;
            for (int i = 0; i < 200; i++) { double m = 0.5 * (lo + hi); if (g.R(m, a) > 0) lo = m; else hi = m; }
            g.r = lo; g.w = 0;
            return g;
        }

        void StartRide(Probe p)
        {
            riding = p;
            Orbit.enabled = false;
        }

        void StopRide()
        {
            if (riding == null) return;
            riding = null;
            camVel = new Vector4(0, 0, 0, 1);
            Orbit.enabled = true;
            Orbit.target = Vector3.zero;
            var pos = Cam.transform.position;
            if (pos.magnitude < 3) pos = pos.normalized * 3;
            Orbit.FromPosition(pos);
            Orbit.Snap();
        }

        // The ride-along camera: at the probe, just above the plane, looking at the hole, moving with the probe.
        // It is not quite the probe: it sits 0.12 rs above the plane at the probe's r and φ (held at 1.03 r₊ or more,
        // also once the probe has crossed), and its observer is the ZAMO there, boosted by the probe's own velocity
        // past its local ZAMO (r̂ and φ̂), capped at CamVMax. The cap scales the whole vector, and γ is computed from
        // the capped vector, so the shader's boost always has γ²(1 − v²) = 1 (#28).
        const double CamVMax = 0.99;
        static Vector4 CamBoost(double vr, double vph)
        {
            double v2 = vr * vr + vph * vph;
            if (v2 > CamVMax * CamVMax) { double k = CamVMax / Math.Sqrt(v2); vr *= k; vph *= k; v2 = CamVMax * CamVMax; }
            return new Vector4((float)vr, 0, (float)vph, (float)(1 / Math.Sqrt(1 - v2)));
        }

        void RideCamera()
        {
            if (riding == null) { camVel = new Vector4(0, 0, 0, 1); return; }
            var g = riding.g;
            double a = A;
            double r = Math.Max(g.r, Kerr.Horizon(a) * 1.03);
            var p = Kerr.FromBL(r, 0, g.phi, a) * 0.5f + Vector3.up * 0.12f;
            Cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(-p, Vector3.up));
            if (!g.inside)
            {
                g.ZamoVelocity(a, out double vr, out double vph);
                camVel = CamBoost(vr, vph);   // camera stops at 1.03 r₊, speed capped at 0.99 c (γ ≤ 7.09)
            }
        }
    }
}
