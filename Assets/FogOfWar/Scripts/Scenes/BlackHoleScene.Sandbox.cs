using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // The black hole sandbox. Everything moves in the equatorial plane on real Kerr geodesics (KerrGeo.cs), timed by the
    // far-away clock, so things slow and redden as they near the horizon and never quite cross it from out here.
    //
    // Tidal disruption uses the standard frozen-in picture: a star keeps its shape (stretched by the tide) until it reaches
    // the tidal radius r_t = R (M/m)^(1/3); there its self-gravity loses and each piece follows its own orbit. The spread
    // in orbital energy across the star, ΔE = G M R / r_t², sets the debris stream: half of it is bound and comes back.
    //
    // Modelled, not solved: the returning gas circularises (its radial motion is damped once it is back at pericentre)
    // and then spreads inwards (its angular momentum drains away, sped up); the heat this releases is what makes the
    // flare. Debris is a single layer of gas in the disc plane.
    public partial class BlackHoleScene
    {
        struct Spec
        {
            public string name; public double mass, radius, temp; public float glow; public bool star, cloud;
        }

        static readonly Spec[] Specs =
        {
            new Spec { name = "Sun-like star", mass = Phys.Msun, radius = 6.957e8, temp = 5800, glow = 1.2f, star = true },
            new Spec { name = "White dwarf", mass = 0.6 * Phys.Msun, radius = 8.7e6, temp = 20000, glow = 1.5f, star = true },
            new Spec { name = "Red giant", mass = 1.2 * Phys.Msun, radius = 50 * 6.957e8, temp = 3800, glow = 1.0f, star = true },
            new Spec { name = "Earth-like planet", mass = 5.97e24, radius = 6.371e6, temp = 6500, glow = 0.15f },
            new Spec { name = "Jupiter", mass = 1.898e27, radius = 6.99e7, temp = 6500, glow = 0.18f },
            new Spec { name = "Gas cloud (3 rs wide)", mass = 3 * 5.97e24, radius = 0, temp = 7000, glow = 1.0f, cloud = true },
        };
        static readonly string[] SpecNames = Array.ConvertAll(Specs, s => s.name);

        static readonly string[] ProbeNames = { "Circle at the ISCO", "Precessing orbit", "Zoom–whirl", "Drop from rest", "Fall from far away", "Light at the photon orbit" };

        class Body
        {
            public Spec spec; public Geo g;
            public double rt, Rm;          // tidal radius and body radius, M units
            public int state;              // 0 intact, 1 shredded, 2 swallowed whole, 3 flew past
            public double stretch = 1, tDisrupt = -1, dE;
        }

        class Probe
        {
            public string name; public Geo g; public bool photon;
            public readonly List<Vector2> trail = new List<Vector2>();
            public readonly List<double> periPhi = new List<double>();
            public double t0, tau0, tauHorizon = -1;
            public string status = "orbiting";
        }

        const int MaxParts = 6000, NR = 384, NP = 512;
        const double TexRMax = 400;
        Geo[] parts;
        float[] pTemp, pHeat, pBase;
        byte[] pPhase;
        double[] pMass;
        int nParts;
        float[] texData;
        Texture2D debrisTex;
        readonly List<Body> bodies = new List<Body>();
        readonly List<Probe> probes = new List<Probe>();
        Probe riding;
        Vector4 camVel = new Vector4(0, 0, 0, 1);

        int throwKind;
        float periRs = 5f, logWarp = 1.5f;
        bool guides = true, ride;
        double simT, accreted, heatScale = 1e-9;
        // light curve: (sim time in M, luminosity / Eddington) and the fallback rate in kg per M of time
        readonly List<Vector2> lightCurve = new List<Vector2>();
        double lum, fallbackRate, fallbackAcc, curveT;
        double lastDE;
        const double LumTau = 2000;   // M: smoothing of the luminosity and fallback readouts

        double A => spin;

        void BuildSandbox()
        {
            parts = new Geo[MaxParts]; pTemp = new float[MaxParts]; pHeat = new float[MaxParts]; pBase = new float[MaxParts];
            pPhase = new byte[MaxParts]; pMass = new double[MaxParts];
            texData = new float[NR * NP * 4];
            debrisTex = Own(new Texture2D(NR, NP, TextureFormat.RGBAFloat, false, true) { wrapModeU = TextureWrapMode.Clamp, wrapModeV = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear });
            ClearSandbox();
        }

        void TeardownSandbox()
        {
            riding = null;
            camVel = new Vector4(0, 0, 0, 1);
            Orbit.enabled = true;
        }

        void ClearSandbox()
        {
            bodies.Clear(); probes.Clear(); nParts = 0; riding = null; ride = false;
            camVel = new Vector4(0, 0, 0, 1);
            lightCurve.Clear(); lum = fallbackRate = fallbackAcc = accreted = 0; heatScale = 1e-9; curveT = 0; lastDE = 0; simT = 0;
            Orbit.enabled = true;
        }

        // ------------------------------------------------------------------------------------------ throwing things

        // Tidal radius and body radius in M units for a spec at the current hole mass.
        void TidalScales(Spec s, out double rt, out double Rm)
        {
            Rm = s.cloud ? 3.0 : s.radius / Lunit;
            rt = s.cloud ? double.PositiveInfinity : s.radius * Math.Pow(MassKg / s.mass, 1.0 / 3) / Lunit;
        }

        public void Throw(int kind)
        {
            var s = Specs[kind];
            TidalScales(s, out double rt, out double Rm);
            double rp = periRs * 2, a = A;
            double r0 = Math.Max(70, 2.5 * rp);
            var g = Geo.Parabolic(rp, r0, a, out _);
            // put the pericentre behind the hole as seen from the default camera, so both lensed images show
            double nu0 = Math.Acos(Math.Max(-1, Math.Min(1, 2 * rp / r0 - 1)));
            g.phi = -Math.PI / 2 - nu0;
            g.t = simT;
            var b = new Body { spec = s, g = g, rt = rt, Rm = Rm };
            if (bodies.Count >= 3) bodies.RemoveAt(0);
            bodies.Add(b);
            if (s.cloud) Shred(b, r0);
            else if (rt > r0) Shred(b, rt);       // already torn apart on the way in, long before it gets here
            if (s.star || s.cloud) disk = false;  // tidal disruptions happen around quiet holes; the disk would hide them
        }

        // Break a body into debris at its current position. rDis is where the tide won: it sets the energy spread.
        void Shred(Body b, double rDis)
        {
            b.state = 1;
            b.tDisrupt = simT;
            double a = A;
            int n = Math.Min(b.spec.cloud ? 2500 : 4000, MaxParts - nParts);
            if (n < 100) { CompactParticles(); n = Math.Min(b.spec.cloud ? 2500 : 4000, MaxParts - nParts); }
            var c = b.g;
            c.FourVelocity(a, out _, out double ur, out double uph);
            // the debris starts as a disc of the body's size (capped so it stays on screen); the energy spread is the
            // real one, ΔE = R / r_dis² (M = 1), scaled onto the drawn size
            double Rvis = Math.Min(b.Rm, 1.0);
            double dE = b.spec.cloud ? 0 : b.Rm / (rDis * rDis);
            b.dE = dE; lastDE = Math.Max(lastDE, dE);
            var rng = new System.Random(1234 + bodies.Count);
            for (int i = 0; i < n; i++)
            {
                double rr = Math.Sqrt(rng.NextDouble()), th = rng.NextDouble() * 2 * Math.PI;
                double x = rr * Math.Cos(th), y = rr * Math.Sin(th);
                double r = c.r + x * Rvis, ph = c.phi + y * Rvis / c.r;
                Geo p;
                if (b.spec.cloud)
                    p = Geo.FromVelocity(r, ph, ur, uph, a, c.w < 0);     // no self-gravity: same velocity everywhere
                else
                {
                    p = c; p.r = r; p.phi = ph;
                    p.E = c.E + x * dE;
                    p.L = c.L * (1 + x * b.Rm / rDis);
                    double R = p.R(r, a);
                    p.w = (c.w < 0 ? -1 : 1) * Math.Sqrt(Math.Max(R, 0));
                }
                p.t = c.t; p.alive = true; p.inside = false;
                int k = nParts++;
                parts[k] = p;
                pTemp[k] = b.spec.star || b.spec.cloud ? (float)Math.Min(b.spec.temp, 9000) : 1800f;
                pBase[k] = b.spec.glow * 0.08f * 3000f / n;
                pHeat[k] = 0;
                pPhase[k] = 0;
                pMass[k] = b.spec.mass / n;
            }
        }

        void CompactParticles()
        {
            int j = 0;
            for (int i = 0; i < nParts; i++)
            {
                if (!parts[i].alive) continue;
                parts[j] = parts[i]; pTemp[j] = pTemp[i]; pHeat[j] = pHeat[i]; pBase[j] = pBase[i]; pPhase[j] = pPhase[i]; pMass[j] = pMass[i];
                j++;
            }
            nParts = j;
        }

        // ------------------------------------------------------------------------------------------ probes

        public void DropProbe(int kind)
        {
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
                        double rph = Kerr.PhotonOrbit(a) * 1.0004, D = Math.Sqrt(Geo.Delta(rph, a));
                        g = new Geo { r = rph, w = 0, E = 1, L = (rph * rph + a * a + a * D) / (a + D), photon = true, alive = true };
                        photon = true;
                        break;
                    }
            }
            g.phi = kind == 1 || kind == 2 ? Math.PI / 2 : 0;
            g.t = simT;
            var p = new Probe { name = ProbeNames[kind], g = g, photon = photon, t0 = simT, tau0 = g.tau };
            if (probes.Count >= 3) { if (riding == probes[0]) riding = null; probes.RemoveAt(0); }
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
            var pos = Cam.transform.position;
            if (pos.magnitude < 3) pos = pos.normalized * 3;
            Orbit.enabled = true;
            Orbit.target = Vector3.zero;
            Orbit.FromPosition(pos);
            Orbit.Snap();
        }

        // The ride-along camera: at the probe, just above the plane, looking at the hole, moving with the probe.
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
                double v2 = Math.Min(vr * vr + vph * vph, 0.99);   // the camera stops just outside the horizon; cap γ at 7
                camVel = new Vector4((float)vr, 0, (float)vph, (float)(1 / Math.Sqrt(1 - v2)));
            }
        }

        // ------------------------------------------------------------------------------------------ simulation

        void TickSandbox(float dt)
        {
            double a = A;
            double h = dt * Math.Pow(10, logWarp);
            simT += h;
            double rh = Kerr.Horizon(a);

            foreach (var b in bodies)
            {
                if (b.state != 0) continue;
                b.g.AdvanceT(a, simT - b.g.t);
                double eps = Math.Min(Math.Pow(b.rt / b.g.r, 3), 1);
                b.stretch = 1 + 2 * eps;
                if (b.g.r <= b.rt) Shred(b, b.rt);
                else if (b.g.inside) b.state = 2;
                else if (b.g.w > 0 && b.g.r > 150) b.state = 3;
            }

            double heatMax = 0, dissipated = 0;
            for (int i = 0; i < nParts; i++)
            {
                ref var p = ref parts[i];
                if (!p.alive) continue;
                double w0 = p.w;
                p.AdvanceT(a, simT - p.t, 0.12, 40);   // to the shared clock, so a particle that ran out of steps catches up
                if (p.inside) { p.alive = false; accreted += pMass[i]; continue; }
                if (p.E >= 1 && p.r > TexRMax * 2) { p.alive = false; continue; }
                if (pPhase[i] == 0 && w0 > 0 && p.w <= 0 && p.E < 1) pPhase[i] = 1;               // apocentre: on its way back
                else if (pPhase[i] == 1 && w0 < 0 && p.w >= 0) { pPhase[i] = 2; fallbackAcc += pMass[i]; }  // back at pericentre
                double heat = 0;
                if (pPhase[i] == 2)
                {
                    double P = 2 * Math.PI * Math.Pow(p.r, 1.5);
                    double E0 = p.E;
                    p.w *= Math.Exp(-h / P);              // circularise: damp the radial motion over about an orbit
                    p.SolveE(a);
                    p.L *= Math.Exp(-h / (12 * P));      // spread inwards (sped up: a real viscous time is far longer)
                    p.SolveE(a);
                    heat = Math.Max(E0 - p.E, 0) / Math.Max(h, 1e-9);
                    dissipated += heat * pMass[i];
                }
                pHeat[i] = Mathf.Lerp(pHeat[i], (float)heat, 0.3f);
                if (pHeat[i] > heatMax) heatMax = pHeat[i];
            }
            heatScale = Math.Max(heatScale * Math.Exp(-h / 3000), heatMax);
            if (nParts > 0 && (nParts > MaxParts / 2) && UnityEngine.Random.value < 0.02f) CompactParticles();

            foreach (var p in probes) StepProbe(p, h, dt);
            if (riding != null && (!riding.g.alive && riding.g.inside == false)) StopRide();

            // luminosity: the heat released by the gas model (E is energy per unit rest mass); Eddington for hydrogen
            double Lw = dissipated * Phys.c * Phys.c / Tunit;
            double Ledd = 1.26e31 * MassKg / Phys.Msun;
            // smoothed over sim time (not frames), on about the gas model's own time scale
            double ema = 1 - Math.Exp(-h / LumTau);
            lum += (Lw / Ledd - lum) * ema;
            curveT += h;
            if (curveT > 0 && (lightCurve.Count == 0 || simT - lightCurve[lightCurve.Count - 1].x > Math.Max(h, 5)))
            {
                fallbackRate += (fallbackAcc / Math.Max(curveT, 1e-9) - fallbackRate) * (1 - Math.Exp(-curveT / LumTau));
                fallbackAcc = 0; curveT = 0;
                if (nParts > 0 || lum > 1e-9) lightCurve.Add(new Vector2((float)simT, (float)lum));
                if (lightCurve.Count > 600) lightCurve.RemoveAt(0);
            }
            Splat();
        }

        void StepProbe(Probe p, double h, float dt)
        {
            double a = A;
            ref var g = ref p.g;
            if (!g.alive) return;
            double w0 = g.w;
            if (!g.inside) g.AdvanceT(a, simT - g.t);
            else if (!p.photon && a < 0.01)
            {
                // past the horizon only the probe's own clock means anything: keep falling on proper time (radial
                // Schwarzschild interior), at the same pace the clocks were running just before
                g.AdvanceTau(a, h * 0.25);
                if (!g.alive) p.status = "reached the singularity";
            }
            if (g.inside && p.tauHorizon < 0) { p.tauHorizon = g.tau; p.status = p.photon ? "fell in" : "crossed the horizon"; if (a >= 0.01) g.alive = false; }
            if (w0 < 0 && g.w >= 0 && !g.inside) p.periPhi.Add(g.phi);
            if (g.w > 0 && g.r > 600) { p.status = "escaped"; g.alive = false; }
            var pos = new Vector2((float)(g.r * Math.Cos(g.phi)), (float)(g.r * Math.Sin(g.phi)));
            if (p.trail.Count == 0 || (p.trail[p.trail.Count - 1] - pos).sqrMagnitude > 0.0025f * Mathf.Max(1, pos.sqrMagnitude * 0.01f)) p.trail.Add(pos);
            if (p.trail.Count > 4000) p.trail.RemoveRange(0, 1000);
        }

        // Deposit the debris into the polar texture: brightness, temperature and velocity, weighted, per unit area.
        void Splat()
        {
            if (nParts == 0) return;
            Array.Clear(texData, 0, texData.Length);
            double a = A;
            double lnMin = Math.Log(Kerr.Horizon(a) * 0.95), lnMax = Math.Log(TexRMax);
            double inv = 1 / (lnMax - lnMin);
            double hs = Math.Max(heatScale, 1e-12);
            for (int i = 0; i < nParts; i++)
            {
                ref var p = ref parts[i];
                if (!p.alive || p.r > TexRMax) continue;
                double fh = pHeat[i] / hs;
                float w = (float)((pBase[i] + 1.5 * fh * pBase[i] * 4) * 100 / Math.Max(p.r * p.r, 1));
                float T = pTemp[i] + 15000f * (float)Math.Pow(Math.Max(fh, 0), 0.25);
                p.FourVelocity(a, out _, out double ur, out double uph);
                double u = (Math.Log(p.r) - lnMin) * inv * NR - 0.5;
                double v = (p.phi / (2 * Math.PI)) % 1.0; if (v < 0) v += 1; v = v * NP - 0.5;
                int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(v);
                float fx = (float)(u - x0), fy = (float)(v - y0);
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int x = x0 + dx, y = ((y0 + dy) % NP + NP) % NP;
                        if (x < 0 || x >= NR) continue;
                        float k = w * (dx == 0 ? 1 - fx : fx) * (dy == 0 ? 1 - fy : fy);
                        int o = (y * NR + x) * 4;
                        texData[o] += k; texData[o + 1] += k * T; texData[o + 2] += k * (float)ur; texData[o + 3] += k * (float)uph;
                    }
            }
        }

        void UploadSandbox(Material m)
        {
            double a = A;
            bool any = nParts > 0;
            if (any) { debrisTex.SetPixelData(texData, 0); debrisTex.Apply(false); }
            m.SetTexture("_DebrisTex", debrisTex);
            m.SetVector("_DebrisRange", new Vector4((float)Math.Log(Kerr.Horizon(a) * 0.95), (float)Math.Log(TexRMax), any ? 1 : 0, 1.0f));
            double rt = 0;
            for (int i = bodies.Count - 1; i >= 0; i--) if (!bodies[i].spec.cloud && bodies[i].rt < 300) { rt = bodies[i].rt; break; }
            m.SetVector("_Guides", new Vector4((float)Kerr.Isco(a), (float)Kerr.PhotonOrbit(a), (float)rt, (float)Kerr.MarginallyBound(a)));
            m.SetFloat("_GuideOn", guides ? 1 : 0);

            int n = 0;
            var pos = new Vector4[4]; var axis = new Vector4[4]; var col = new Vector4[4]; var vel = new Vector4[4];
            foreach (var b in bodies)
            {
                if (b.state != 0 || n >= 4 || b.Rm > 40) continue;
                var g = b.g;
                g.FourVelocity(a, out double ut, out double ur, out double uph);
                pos[n] = (Vector4)Kerr.FromBL(g.r, 0, g.phi, a); pos[n].w = (float)Math.Max(b.Rm, 0.06);
                axis[n] = new Vector4((float)Math.Cos(g.phi), 0, (float)Math.Sin(g.phi), (float)b.stretch);
                col[n] = new Vector4((float)b.spec.temp, b.spec.glow, b.spec.star ? 1 : 0, 0);
                vel[n] = new Vector4((float)ut, (float)ur, (float)uph, 0);
                n++;
            }
            foreach (var p in probes)
            {
                if (n >= 4 || p.photon || !p.g.alive || p.g.inside || p == riding) continue;
                var g = p.g;
                g.FourVelocity(a, out double ut, out double ur, out double uph);
                pos[n] = (Vector4)Kerr.FromBL(g.r, 0, g.phi, a); pos[n].w = 0.45f;
                axis[n] = new Vector4(1, 0, 0, 1);
                col[n] = new Vector4(9000, 3f, 0, 0);
                vel[n] = new Vector4((float)ut, (float)ur, (float)uph, 0);
                n++;
            }
            m.SetInt("_NB", n);
            m.SetVectorArray("_BPos", pos); m.SetVectorArray("_BAxis", axis); m.SetVectorArray("_BCol", col); m.SetVectorArray("_BVel", vel);
        }

        // Run the sandbox forward (shots and QA): t in M.
        public void Advance(double t, float frame = 1f / 30)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(t / (frame * Math.Pow(10, logWarp))));
            for (int i = 0; i < steps; i++) TickSandbox(frame);
        }

        // Run until the newest body is within r (M units) or has broken up, then on for `after` (shots and QA).
        public void AdvanceUntilBody(double r, double after = 0)
        {
            for (int i = 0; i < 20000 && bodies.Count > 0; i++)
            {
                var b = bodies[bodies.Count - 1];
                if (b.state != 0 || b.g.r <= r) break;
                TickSandbox(1f / 30);
            }
            if (after > 0) Advance(after);
        }

        public string DebugSummary()
        {
            int alive = 0, nan = 0, ph2 = 0; double rmin = 1e9, rmax = 0;
            for (int i = 0; i < nParts; i++)
            {
                if (!parts[i].alive) continue;
                alive++;
                if (double.IsNaN(parts[i].r) || double.IsNaN(parts[i].w)) { nan++; continue; }
                if (pPhase[i] == 2) ph2++;
                rmin = Math.Min(rmin, parts[i].r); rmax = Math.Max(rmax, parts[i].r);
            }
            string b = bodies.Count > 0 ? $" body r={bodies[bodies.Count - 1].g.r:0.0} rt={bodies[bodies.Count - 1].rt:0.0} state={bodies[bodies.Count - 1].state}" : "";
            return $"t={simT:0} parts {nParts} alive {alive} nan {nan} circ {ph2} r {rmin:0.0}..{rmax:0.0} heat {heatScale:0.0e0} lum {lum:0.00e0}{b}";
        }

        // Run until the ridden probe is within r (M units).
        public void AdvanceUntilRide(double r)
        {
            for (int i = 0; i < 20000 && riding != null && riding.g.r > r && !riding.g.inside; i++) TickSandbox(1f / 30);
        }

        // ------------------------------------------------------------------------------------------ readouts and controls

        void SandboxReadouts(List<Readout> list)
        {
            var s = Specs[throwKind];
            TidalScales(s, out double rt, out double Rm);
            double rh = Kerr.Horizon(A), rmb = Kerr.MarginallyBound(A);
            if (!s.cloud)
            {
                string fate = rt < rh ? "swallowed whole" : rt < rmb ? "torn up as it plunges" : rt > periRs * 2 ? "shredded" : "flies past (stretched)";
                list.Add(new Readout("Tidal radius: " + s.name.ToLowerInvariant(), (rt / 2 < 1e4 ? (rt / 2).ToString("0.00") : Fmt.Num(rt / 2, 3)) + " rs → " + fate,
                    rt < rh ? Tone.Cold : rt > periRs * 2 ? Tone.Bad : Tone.Normal));
                // Hills mass: the heaviest hole that can still shred it outside the marginally bound orbit
                double hills = Math.Pow(s.radius * Phys.c * Phys.c / (rmb * Phys.G), 1.5) / Math.Sqrt(s.mass) / Phys.Msun;
                list.Add(new Readout("Swallowed whole above", Fmt.Num(hills, 3) + " suns"));
                list.Add(new Readout("Its size", Rm / 2 < 0.01 ? Fmt.Num(Rm / 2 * Lunit * 2 / 1000, 3) + " km" : (Rm / 2).ToString("0.000") + " rs"));
            }
            if (nParts > 0 || lightCurve.Count > 0)
            {
                list.Add(new Readout("Debris coming back", Fmt.Num(fallbackRate / Tunit * Phys.yr / Phys.Msun, 3) + " suns/yr"));
                list.Add(new Readout("Flare luminosity", Fmt.Num(lum, 3) + " × Eddington", lum > 1 ? Tone.Hot : Tone.Normal));
                if (lastDE > 0)
                {
                    double tmin = 2 * Math.PI * Math.Pow(1 / (2 * lastDE), 1.5);   // return time of the most bound debris
                    list.Add(new Readout("First debris returns after", Fmt.Duration(tmin * Tunit)));
                }
                list.Add(new Readout("Swallowed so far", Fmt.Num(accreted / Phys.Msun, 3) + " suns"));
            }
            list.Add(new Readout("Sim clock", Fmt.Duration(simT * Tunit) + " (1 s = " + Fmt.Duration(Math.Pow(10, logWarp) * Tunit) + ")"));
            if (probes.Count > 0) ProbeSummary(list, probes[probes.Count - 1]);
        }

        void ProbeSummary(List<Readout> list, Probe p)
        {
            var g = p.g;
            list.Add(new Readout("Probe", p.name + ": " + p.status));
            if (p.photon) { list.Add(new Readout("Light: turns so far", ((g.phi) / (2 * Math.PI)).ToString("0.0"))); return; }
            list.Add(new Readout("Probe distance", (g.r / 2).ToString("0.00") + " rs"));
            if (p.periPhi.Count >= 2)
            {
                double dphi = p.periPhi[p.periPhi.Count - 1] - p.periPhi[p.periPhi.Count - 2] - 2 * Math.PI;
                list.Add(new Readout("Perihelion shift per orbit", (dphi * 180 / Math.PI).ToString("0.0") + "°", Tone.Hot));
                if (p.name == ProbeNames[1])
                {
                    // the weak-field (Einstein 1915) value for the same ellipse: 6πM / (a(1 − e²)), a = 37, e = 23/37
                    double weak = 6 * Math.PI / (37 * (1 - (23.0 / 37) * (23.0 / 37)));
                    list.Add(new Readout("Weak-field formula says", (weak * 180 / Math.PI).ToString("0.0") + "°"));
                }
            }
        }

        void ProbeReadouts(List<Readout> list)
        {
            var p = riding; var g = p.g;
            double a = A;
            list.Add(new Readout("Camera", "rides " + p.name.ToLowerInvariant()));
            list.Add(new Readout("Probe status", p.status, g.inside ? Tone.Bad : Tone.Normal));
            list.Add(new Readout("Distance", (g.r / 2).ToString("0.000") + " rs"));
            double tau = (g.tau - p.tau0) * Tunit, t = (Math.Min(simT, g.t) - p.t0) * Tunit;
            list.Add(new Readout("Probe's clock", Fmt.Duration(tau)));
            list.Add(new Readout("Far-away clock", g.inside ? "∞ (never sees it cross)" : Fmt.Duration(t)));
            if (!g.inside)
            {
                g.FourVelocity(a, out double ut, out double ur, out double uph);
                list.Add(new Readout("Probe clock rate", (1 / ut).ToString("0.0000") + "× far away", 1 / ut < 0.5 ? Tone.Hot : Tone.Normal));
                g.ZamoVelocity(a, out double vr, out double vph);
                list.Add(new Readout("Speed past hovering observers", Math.Sqrt(vr * vr + vph * vph).ToString("0.000") + " c"));
                // its beacon, sent straight out, as received far away (b = 0, outgoing)
                double Pp = g.r * g.r + a * a, Dl = Geo.Delta(g.r, a);
                double prOut = Math.Sqrt(Math.Max(Pp * Pp - Dl * a * a, 0)) / Dl;
                double gb = 1 / (ut - prOut * ur);
                list.Add(new Readout("Its beacon seen far away", gb > 0 ? "×" + gb.ToString("0.000") + " frequency (z = " + Fmt.Num(1 / gb - 1, 3) + ")" : "—", gb < 0.5 ? Tone.Hot : Tone.Normal));
                double tidal = 4 / (g.r * g.r * g.r) * Phys.c * Phys.c / (Lunit * Lunit) / Phys.g;   // 2GM·(2 m)/r³ with GM = c²·Lunit
                list.Add(new Readout("Tide across a 2 m body", Fmt.Num(tidal) + " g", tidal > 10 ? Tone.Bad : Tone.Normal));
            }
            else if (a < 0.01)
            {
                // radial fall from rest at infinity: proper time left to r = 0 is (4/3) M (r/2M)^{3/2}
                double left = 4.0 / 3 * Math.Pow(g.r / 2, 1.5) * Tunit;
                list.Add(new Readout("Time left on its clock", g.alive ? Fmt.Duration(left) : "0"));
                list.Add(new Readout("Inside, r is time", "every path leads to r = 0"));
            }
            else list.Add(new Readout("Inside a spinning hole", "the inner horizon is not modelled"));
            if (p.periPhi.Count >= 2)
            {
                double dphi = p.periPhi[p.periPhi.Count - 1] - p.periPhi[p.periPhi.Count - 2] - 2 * Math.PI;
                list.Add(new Readout("Perihelion shift per orbit", (dphi * 180 / Math.PI).ToString("0.0") + "°", Tone.Hot));
            }
        }

        void SandboxControls(PanelUI ui)
        {
            ui.Section("Sandbox: throw something in");
            throwKind = ui.Choice(null, throwKind, SpecNames);
            periRs = ui.Slider("Closest approach", periRs, 1, 60, v => v.ToString("0.0") + " rs", 0.1f);
            if (ui.Buttons("Throw it in", "Clear sandbox") is int t && t >= 0) { if (t == 0) Throw(throwKind); else ClearSandbox(); }
            ui.Section("Sandbox: probes and light");
            int k = ui.Buttons(ProbeNames);
            if (k >= 0) DropProbe(k);
            bool r = ui.Toggle("Ride along with the probe", ride);
            if (r != ride)
            {
                ride = r;
                Probe last = null;
                for (int i = probes.Count - 1; i >= 0; i--) if (!probes[i].photon && probes[i].g.alive) { last = probes[i]; break; }
                if (ride && last != null) StartRide(last); else if (!ride) StopRide();
            }
            logWarp = ui.Slider("Time warp", logWarp, 0, 6, v => "1 s = " + Fmt.Duration(Math.Pow(10, v) * Tunit), 0.05f);
            guides = ui.Toggle("Guide rings (ISCO, photon orbit, tidal radius)", guides);
            showMap = ui.Toggle("Map and light curve", showMap);
        }
    }
}
