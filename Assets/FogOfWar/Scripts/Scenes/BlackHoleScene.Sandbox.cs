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

        static readonly string[] ProbeNames = { "Circle at the ISCO", "Precessing orbit", "Zoom–whirl", "Drop from rest", "Fall from far away", "Light just outside the photon orbit", "Light just inside the photon orbit" };

        class Body
        {
            public Spec spec; public Geo g;
            public double rt, Rm;          // tidal radius and body radius, M units
            public int state;              // 0 intact, 1 shredded, 2 swallowed whole, 3 flew past
            public double stretch = 1, tDisrupt = -1, dE, rp;
        }

        class Probe
        {
            public string name; public Geo g; public bool photon;
            public readonly List<Vector2> trail = new List<Vector2>();
            public readonly List<double> periPhi = new List<double>();
            public readonly List<int> periN = new List<int>();   // pericentre count at each sample: at high warp several
                                                                 // pericentres can pass in one frame
            // perihelion shift per orbit from the last two samples, averaged over the orbits between them
            public bool Shift(out double perOrbit)
            {
                perOrbit = 0;
                int k = periPhi.Count;
                if (k < 2) return false;
                int dn = periN[k - 1] - periN[k - 2];
                if (dn <= 0) return false;
                perOrbit = (periPhi[k - 1] - periPhi[k - 2] - 2 * Math.PI * dn) / dn;
                return true;
            }
            public double t0, tau0, tauHorizon = -1;
            public int peris;
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
        bool guides = true, ride, diskAutoOff;
        double autoWarpFrom = -1;
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
            if (diskAutoOff) disk = true;
            diskAutoOff = false; autoWarpFrom = -1;
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
            var b = new Body { spec = s, g = g, rt = rt, Rm = Rm, rp = rp };
            if (bodies.Count >= 3) bodies.RemoveAt(0);
            bodies.Add(b);
            if (s.cloud) Shred(b, r0);
            else if (rt > r0) Shred(b, rt);       // already torn apart on the way in, long before it gets here
            if ((s.star || s.cloud) && disk) { disk = false; diskAutoOff = true; }  // TDEs happen around quiet holes; the disk would hide them
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
            // The bound debris first comes back after t_min = 2π (GM/2ΔE)^{3/2}: 85 days for the Sun at Sgr A*, hours of real
            // time at the default warp. Raise the warp so the return and the flare arrive about 20 s after the break-up.
            if (dE > 0)
            {
                double tmin = 2 * Math.PI * Math.Pow(1 / (2 * dE), 1.5);
                float want = (float)Math.Min(6, Math.Log10(tmin / 20));
                if (want > logWarp) { autoWarpFrom = logWarp; logWarp = want; }
            }
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
                double v2 = Math.Min(vr * vr + vph * vph, 0.98);   // camera stops at 1.03 r₊, speed capped at 0.99 c (γ ≤ 7)
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
            if (!g.inside) g.AdvanceT(a, simT - g.t, 0.08, 20000);   // few probes: let them keep up with any warp
            else if (!p.photon && a < 0.01)
            {
                // past the horizon only the probe's own clock means anything: keep falling on proper time (radial
                // Schwarzschild interior), at the same pace the clocks were running just before
                g.AdvanceTau(a, h * 0.25);
                if (!g.alive) p.status = "reached the singularity";
            }
            if (g.inside && p.tauHorizon < 0) { p.tauHorizon = g.tau; p.status = p.photon ? "fell in" : "crossed the horizon"; if (a >= 0.01) g.alive = false; }
            if (g.peris > p.peris && !g.inside) { p.peris = g.peris; p.periPhi.Add(g.periPhi); p.periN.Add(g.peris); }
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

        // The disk the flare feeds: thin, out to the circularisation radius 2 r_p of the latest shredded star, brightness and
        // temperature following the flare luminosity L/L_Edd (a display model: real TDE disks peak in the UV and X-rays).
        bool FlareDisk(out float outerRs, out float tempK, out float gain)
        {
            outerRs = tempK = gain = 0;
            if (lum < 1e-3 || nParts == 0) return false;
            Body src = null;
            for (int i = bodies.Count - 1; i >= 0; i--) if (bodies[i].state == 1 && !bodies[i].spec.cloud) { src = bodies[i]; break; }
            if (src == null) return false;
            outerRs = (float)Math.Max(src.rp, Kerr.Isco(A) * 0.5 + 1);   // 2 r_p in M is r_p in rs
            tempK = (float)Math.Max(2500, Math.Min(40000, 9000 * Math.Pow(lum, 0.25)));
            gain = (float)Math.Min(1, Math.Sqrt(lum));
            return true;
        }

        // Run the sandbox forward by t of sim time (M), in ordinary frames at the current warp (shots, tours and QA).
        // The warp can change on the way (a break-up raises it), so this runs to a target time, not a frame count.
        public void Advance(double t, float frame = 1f / 30)
        {
            double end = simT + t;
            for (int i = 0; i < 200000 && simT < end; i++)
                TickSandbox((float)Math.Min(frame, (end - simT) / Math.Pow(10, logWarp) + 1e-6));
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

        // Perihelion shift per orbit (degrees) of the newest probe, NaN until two pericentres were seen (QA).
        public double LastProbeShift()
        {
            if (probes.Count == 0 || !probes[probes.Count - 1].Shift(out double d)) return double.NaN;
            return d * 180 / Math.PI;
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

        // The sandbox readouts keep a fixed set of rows with short values, so the controls below them never move while
        // the numbers change (QA-040). "—" fills a row that has nothing to show yet.
        void SandboxReadouts(List<Readout> list)
        {
            var s = Specs[throwKind];
            TidalScales(s, out double rt, out double Rm);
            double rh = Kerr.Horizon(A), rmb = Kerr.MarginallyBound(A);
            const string none = "—";
            if (!s.cloud)
            {
                string fate = rt < rh ? "swallowed" : rt < rmb ? "torn, plunges" : rt > periRs * 2 ? "shredded" : "flies past";
                list.Add(new Readout("Tidal radius: " + s.name.ToLowerInvariant(), (rt / 2 < 1e4 ? (rt / 2).ToString("0.00") : Fmt.Num(rt / 2, 3)) + " rs",
                    rt < rh ? Tone.Cold : rt > periRs * 2 ? Tone.Bad : Tone.Normal));
                list.Add(new Readout("Its fate at this pass", fate, rt < rh ? Tone.Cold : rt > periRs * 2 ? Tone.Bad : Tone.Normal));
                // Hills masses: above the first all the debris is swallowed (r_t < r_mb), above the second the star itself
                double hills = Math.Pow(s.radius * Phys.c * Phys.c / (rmb * Phys.G), 1.5) / Math.Sqrt(s.mass) / Phys.Msun;
                double intact = Math.Pow(s.radius * Phys.c * Phys.c / (rh * Phys.G), 1.5) / Math.Sqrt(s.mass) / Phys.Msun;
                list.Add(new Readout("No flare above", Fmt.Num(hills, 3) + " suns"));
                list.Add(new Readout("Swallowed intact above", Fmt.Num(intact, 3) + " suns"));
                list.Add(new Readout("Its size", Rm / 2 < 0.01 ? Fmt.Num(Rm / 2 * Lunit * 2 / 1000, 3) + " km" : (Rm / 2).ToString("0.000") + " rs"));
            }
            else
            {
                list.Add(new Readout("Tidal radius: gas cloud", "none (no self-gravity)"));
                list.Add(new Readout("Its fate at this pass", "stretched"));
                list.Add(new Readout("No flare above", none));
                list.Add(new Readout("Swallowed intact above", none));
                list.Add(new Readout("Its size", "3 rs (model)"));
            }
            bool debris = nParts > 0 || lightCurve.Count > 0;
            list.Add(new Readout("Debris coming back", debris ? Fmt.Num(fallbackRate / Tunit * Phys.yr / Phys.Msun, 3) + " suns/yr" : none));
            list.Add(new Readout("Flare luminosity", debris ? Fmt.Num(lum, 3) + " × Eddington" : none, lum > 1 ? Tone.Hot : Tone.Normal));
            list.Add(new Readout("First debris returns after", lastDE > 0 ? Fmt.Duration(2 * Math.PI * Math.Pow(1 / (2 * lastDE), 1.5) * Tunit) : none));
            list.Add(new Readout("Swallowed so far", debris ? Fmt.Num(accreted / Phys.Msun, 3) + " suns" : none));
            string note = none; var tone = Tone.Normal;
            if (!disk && FlareDisk(out float fo, out _, out _)) { note = "flare disk ≤" + fo.ToString("0") + " rs"; tone = Tone.Hot; }
            else if (autoWarpFrom >= 0) note = "warp raised";
            else if (diskAutoOff && !disk) note = "disk off: quiet hole";
            list.Add(new Readout("Sandbox", note, tone));
            list.Add(new Readout("Sim clock", Fmt.Duration(simT * Tunit)));
            list.Add(new Readout("1 s of play is", Fmt.Duration(Math.Pow(10, logWarp) * Tunit)));
            ProbeSummary(list, probes.Count > 0 ? probes[probes.Count - 1] : null);
        }

        void ProbeSummary(List<Readout> list, Probe p)
        {
            const string none = "—";
            list.Add(new Readout("Probe", p != null ? p.name : none));
            list.Add(new Readout("Probe status", p != null ? p.status : none));
            if (p != null && p.photon) list.Add(new Readout("Light: turns so far", (p.g.phi / (2 * Math.PI)).ToString("0.0")));
            else list.Add(new Readout("Probe distance", p != null ? (p.g.r / 2).ToString("0.00") + " rs" : none));
            double dphi = 0;
            bool shift = p != null && !p.photon && p.Shift(out dphi);
            list.Add(new Readout("Perihelion shift per orbit", shift ? (dphi * 180 / Math.PI).ToString("0.0") + "°" : none, Tone.Hot));
            string weak = none;
            if (p != null && p.name == ProbeNames[1])
            {
                // weak field for the same ellipse (semi-latus rectum p = 2 rp ra / (rp + ra)): Einstein's 6πM/p, minus the
                // prograde Lense–Thirring term 8πa M^{3/2}/p^{3/2}
                double pl = 2 * 14.0 * 60 / 74;
                weak = ((6 * Math.PI / pl - 8 * Math.PI * A / Math.Pow(pl, 1.5)) * 180 / Math.PI).ToString("0.0") + "°";
            }
            list.Add(new Readout(A > 0.001 ? "Weak field (+ Lense–Thirring)" : "Weak-field formula", weak));
        }

        void ProbeReadouts(List<Readout> list)
        {
            var p = riding; var g = p.g;
            double a = A;
            const string none = "—";
            list.Add(new Readout("Camera", "rides the probe"));
            list.Add(new Readout("Probe", p.name));
            list.Add(new Readout("Probe status", p.status, g.inside ? Tone.Bad : Tone.Normal));
            list.Add(new Readout("Distance", (g.r / 2).ToString("0.000") + " rs"));
            double tau = (g.tau - p.tau0) * Tunit, t = (Math.Min(simT, g.t) - p.t0) * Tunit;
            list.Add(new Readout(g.inside ? "Probe's clock (display speed)" : "Probe's clock", Fmt.Duration(tau)));
            list.Add(new Readout("Far-away clock", g.inside ? "never sees it cross" : Fmt.Duration(t)));
            string rate = none, speed = none, beacon = none, tide = none, left = none;
            Tone rateT = Tone.Normal, beaconT = Tone.Normal, tideT = Tone.Normal;
            if (!g.inside)
            {
                g.FourVelocity(a, out double ut, out double ur, out double uph);
                rate = (1 / ut).ToString("0.0000") + "×"; if (1 / ut < 0.5) rateT = Tone.Hot;
                g.ZamoVelocity(a, out double vr, out double vph);
                speed = Math.Sqrt(vr * vr + vph * vph).ToString("0.000") + " c";
                // its beacon, sent straight out, as received far away (b = 0, outgoing)
                double Pp = g.r * g.r + a * a, Dl = Geo.Delta(g.r, a);
                double prOut = Math.Sqrt(Math.Max(Pp * Pp - Dl * a * a, 0)) / Dl;
                double gb = 1 / (ut - prOut * ur);
                if (gb > 0) { beacon = "×" + gb.ToString("0.000") + " frequency"; if (gb < 0.5) beaconT = Tone.Hot; }
                // radial tidal stretch in the probe's own frame (Marck 1983, spin 0): (2M/r³)(1 + 3L²/2r²) across 2 m
                double tidal = 4 / (g.r * g.r * g.r) * (1 + 1.5 * g.L * g.L / (g.r * g.r)) * Phys.c * Phys.c / (Lunit * Lunit) / Phys.g;
                tide = Fmt.Num(tidal) + " g"; if (tidal > 10) tideT = Tone.Bad;
            }
            else if (a < 0.01) left = g.alive ? Fmt.Duration(TauToSingularity(g.r, g.E, g.L) * Tunit) : "0 (r = 0)";
            else left = "inner horizon not modelled";
            list.Add(new Readout("Probe clock rate", rate, rateT));
            list.Add(new Readout("Speed past local ZAMOs", speed));
            list.Add(new Readout("Its beacon seen far away", beacon, beaconT));
            list.Add(new Readout(a > 0.001 ? "Tide across 2 m (spin-0 formula)" : "Tide across a 2 m body", tide, tideT));
            list.Add(new Readout("Time left to r = 0", left));
            list.Add(new Readout("Perihelion shift per orbit", p.Shift(out double dphi) ? (dphi * 180 / Math.PI).ToString("0.0") + "°" : none, Tone.Hot));
            list.Add(new Readout("Camera limits", "≤ 0.99 c, 1.03 r₊"));
            list.Add(new Readout("Camera exposure", "÷ (γ(1+v))³"));
        }

        // Proper time from r to r = 0 inside a Schwarzschild hole for a geodesic with energy E and angular momentum L:
        // τ = ∫₀^r dr′ / √(E² − (1 − 2/r′)(1 + L²/r′²)), 32-point Gauss–Legendre.
        static double[] glx, glw;
        static double TauToSingularity(double r, double E, double L)
        {
            if (glx == null) GaussLegendre(32, out glx, out glw);
            double sum = 0;
            for (int i = 0; i < glx.Length; i++)
            {
                double x = 0.5 * r * (glx[i] + 1);
                double v = E * E - (1 - 2 / x) * (1 + L * L / (x * x));
                if (v > 0) sum += glw[i] / Math.Sqrt(v);
            }
            return 0.5 * r * sum;
        }

        static void GaussLegendre(int n, out double[] x, out double[] w)
        {
            x = new double[n]; w = new double[n];
            for (int i = 0; i < n; i++)
            {
                double z = Math.Cos(Math.PI * (i + 0.75) / (n + 0.5)), dp = 1;
                for (int it = 0; it < 100; it++)
                {
                    double p0 = 1, p1 = 0;
                    for (int j = 1; j <= n; j++) { double p2 = p1; p1 = p0; p0 = ((2 * j - 1) * z * p1 - (j - 1) * p2) / j; }
                    dp = n * (z * p0 - p1) / (z * z - 1);
                    double dz = p0 / dp;
                    z -= dz;
                    if (Math.Abs(dz) < 1e-15) break;
                }
                x[i] = z; w[i] = 2 / ((1 - z * z) * dp * dp);
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
            GUILayout.Label("Model, not real: gas spreads in 12 orbits (real viscous time is far longer); debris drawn ≤ 1 M wide, energy spread uses the real star; bodies drawn at least 0.06 M, probes 0.45 M; gas cloud 3 rs wide (real G2-like clouds are thousands of rs); everything in the equatorial plane; the probe's clock inside the horizon runs at display speed.", ui.note);
        }
    }
}
