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
            public double turnsEnd = -1;   // light: turns made when it escaped or fell in (frozen there)
        }

        const int MaxParts = 12000, NR = 448, NP = 512;
        const double TexRMax = 8000;   // M: the outgoing stream stays drawn to its apocentre
        Geo[] parts;
        float[] pTemp, pHeat, pBase;
        byte[] pPhase;
        double[] pMass;
        int nParts, liveParts;   // slots in use, and how many of them are still alive (BH audit #37)
        float[] texData;
        bool texDirty;
        Texture2D debrisTex;
        readonly List<Body> bodies = new List<Body>();
        readonly List<Probe> probes = new List<Probe>();
        Probe riding;
        Vector4 camVel = new Vector4(0, 0, 0, 1);

        int throwKind;
        float periRs = 5f, logWarp = 1.5f;
        bool guides = true, ride, diskAutoOff, follow = true, followOn;
        double autoWarpFrom = -1;
        // after a break-up the warp is held for a few seconds so the stream can be seen stretching, then ramped up to
        // warpTarget (audit #46). Moving the warp slider hands the clock to the player until they tick automatic warp
        // back on (BH audit #33).
        float warpTarget = -1, warpHold;
        bool autoWarp = true;
        // pause freezes the particles, probes, light curve and the disk's swirl; Step runs one 1/30 s frame of play
        bool paused, stepPending;
        double diskClock;   // the disk's swirl animation, in seconds of play (stops when paused)
        // the hole this experiment was set up around: a new mass or spin starts a new experiment (BH audit #29)
        float expLogMass = float.NaN, expSpin = float.NaN;
        string notice, noticeShort; float noticeLeft;   // a few seconds' explanation: panel note, and the narration row
        int rejectedParts;   // debris pieces with no valid orbit left out since the sandbox was cleared (BH audit #34)
        double simT, accreted, heatScale = 1e-9;
        // light curve: (sim time in M, luminosity / Eddington) and the fallback rate in kg per M of time
        readonly List<Vector2> lightCurve = new List<Vector2>();
        double lum, fallbackRate, fallbackAcc, curveT;
        double lastDE;
        const double LumTau = 2000;   // M: smoothing of the luminosity and fallback readouts
        const double HeatTau = 0.25;  // orbits: smoothing of each gas particle's drawn heat, on its own clock
        const double GasChunk = 0.125; // orbits: returned gas's motion between two applications of the gas model
        // BH audit #30: the gas model is applied at fixed points of each particle's own time (pKick: from its return
        // to pericentre, every GasChunk of a local orbit), and the heat it releases is binned by that time, not by
        // frame. A bin is final once no returned gas is still behind it, so frame rate, warp and step budget change
        // only how soon the light curve is drawn, not its shape.
        double[] pKick, pKickDt;
        const double LumBin = 50;      // M
        readonly Dictionary<long, double> lumBins = new Dictionary<long, double>();
        long lumNext;
        double curveLastT;
        void Deposit(double t, double e) { long k = Math.Max((long)Math.Floor(t / LumBin), lumNext); lumBins.TryGetValue(k, out var v); lumBins[k] = v + e; }

        double A => spin;

        void BuildSandbox()
        {
            parts = new Geo[MaxParts]; pTemp = new float[MaxParts]; pHeat = new float[MaxParts]; pBase = new float[MaxParts];
            pPhase = new byte[MaxParts]; pMass = new double[MaxParts]; pKick = new double[MaxParts]; pKickDt = new double[MaxParts];
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
            StopRide();
            bodies.Clear(); probes.Clear(); nParts = liveParts = 0; ride = false; rejectedParts = 0;
            if (diskAutoOff) disk = true;
            diskAutoOff = false; autoWarpFrom = -1; warpTarget = -1; debrisMedR = -1;
            if (followOn) { Orbit.target = Vector3.zero; Orbit.distance = 24; followOn = false; }
            camVel = new Vector4(0, 0, 0, 1);
            lightCurve.Clear(); lum = fallbackRate = fallbackAcc = accreted = 0; heatScale = 1e-9; curveT = 0; lastDE = 0; simT = 0;
            lumBins.Clear(); lumNext = 0; curveLastT = double.NegativeInfinity;
            Orbit.enabled = true;
        }

        // The orbits, tidal radii, sizes, debris energies and light curve all belong to the hole they were set up around.
        // A new mass or spin therefore starts a new experiment: the sandbox is cleared (the ride-along and follow choices
        // and the clock settings stay) and a short notice says why (BH audit #29).
        void CheckExperiment()
        {
            SyncMass();
            if (Math.Abs(logMass - expLogMass) < 0.005f && Math.Abs(spin - expSpin) < 0.0005f) return;   // half a slider step
            bool live = bodies.Count > 0 || probes.Count > 0 || nParts > 0 || lightCurve.Count > 0;
            expLogMass = logMass; expSpin = spin;
            if (!live) return;
            bool keepRide = ride;
            ClearSandbox();
            ride = keepRide;
            Notify("new hole: sandbox cleared", "A new mass or spin is a new black hole: the sandbox was cleared, because its orbits, debris and light curve belonged to the old one.");
        }

        void Notify(string shortText, string text) { noticeShort = shortText; notice = text; noticeLeft = 8; }

        // ------------------------------------------------------------------------------------------ throwing things

        // Tidal radius and body radius in M units for a spec at the current hole mass.
        void TidalScales(Spec s, out double rt, out double Rm)
        {
            Rm = s.cloud ? 3.0 : s.radius / Lunit;
            rt = s.cloud ? double.PositiveInfinity : s.radius * Math.Pow(MassKg / s.mass, 1.0 / 3) / Lunit;
        }

        public void Throw(int kind)
        {
            CheckExperiment();
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
            int want = b.spec.cloud ? 2500 : 4000;
            if (MaxParts - nParts < want) CompactParticles();
            if (MaxParts - nParts < want) RetireOldest(want - (MaxParts - nParts));   // audit #45: the oldest debris makes room
            int n = Math.Min(want, MaxParts - nParts), first = nParts;
            var c = b.g;
            c.FourVelocity(a, out _, out double ur, out double uph);
            // the debris starts as a disc of the body's size (capped so it stays on screen); the energy spread is the
            // real one, ΔE = R / r_dis² (M = 1), scaled onto the drawn size
            double Rvis = b.spec.cloud ? b.Rm : Math.Min(b.Rm, 1.0);   // clouds at their stated size (audit #49)
            double dE = b.spec.cloud ? 0 : b.Rm / (rDis * rDis);
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
                    // a piece whose E and L leave no radial motion here (R < 0) has no orbit through this point: it is left
                    // out, not given w = 0. Round-off at a turning point (|R| ≤ 1e-7 r⁴) counts as w = 0 (BH audit #34).
                    double R = p.R(r, a);
                    if (!(R >= -1e-7 * r * r * r * r) || double.IsInfinity(R)) { rejectedParts++; continue; }
                    p.w = (c.w < 0 ? -1 : 1) * Math.Sqrt(Math.Max(R, 0));
                }
                p.t = c.t; p.alive = true; p.inside = false;
                int k = nParts++;
                parts[k] = p;
                pTemp[k] = b.spec.star || b.spec.cloud ? (float)Math.Min(b.spec.temp, 9000) : 1800f;
                pHeat[k] = 0;
                pPhase[k] = 0;
            }
            // the body's mass and glow are shared among the pieces that were kept
            int kept = nParts - first;
            for (int k = first; k < nParts; k++) { pBase[k] = b.spec.glow * 0.08f * 3000f / kept; pMass[k] = b.spec.mass / kept; }
            liveParts += kept;
            if (kept < n) Notify((n - kept) + " pieces left out", (n - kept) + " of " + n + " debris pieces had no valid orbit and were left out (the rest carry the body's mass): the frozen-in model breaks down for a body this big next to a hole this small.");
            // a cloud's spread comes from the velocity it shares across its width: measure it
            if (b.spec.cloud && kept > 0)
            {
                double lo = double.MaxValue, hi = double.MinValue;
                for (int k = first; k < nParts; k++) { lo = Math.Min(lo, parts[k].E); hi = Math.Max(hi, parts[k].E); }
                dE = 0.5 * (hi - lo);
            }
            b.dE = dE; lastDE = dE;
            // The bound debris first comes back after t_min = 2π (GM/2ΔE)^{3/2}: 85 days for the Sun at Sgr A*, hours of real
            // time at the default warp. Raise the warp (after a short hold, see TickSandbox) so the return and the flare
            // arrive about 25 s after the break-up. Not when the whole orbit is inside r_mb: then nothing comes back.
            if (dE > 0 && b.rp > Kerr.MarginallyBound(A))
            {
                double tmin = 2 * Math.PI * Math.Pow(1 / (2 * dE), 1.5);
                float target = (float)Math.Min(6, Math.Log10(tmin / 20));
                if (target > logWarp) { autoWarpFrom = logWarp; warpTarget = target; warpHold = 2f; debrisOut = 1; }
            }
        }

        // Median radius, centre (world, rs units) and the still-outgoing fraction of the bound debris that has not come
        // back yet (phases 0 and 1); every 4th particle.
        double debrisMedR = -1, debrisOut = 1;
        Vector3 debrisCentre;
        readonly List<double> trackR = new List<double>();
        void TrackDebris()
        {
            trackR.Clear();
            double sx = 0, sz = 0; int bound = 0, back = 0;
            for (int i = 0; i < nParts; i += 4)
            {
                ref var p = ref parts[i];
                if (!p.alive || p.E >= 1) continue;
                bound++;
                if (pPhase[i] == 2) { back++; continue; }
                trackR.Add(p.r);
                sx += p.r * Math.Cos(p.phi); sz += p.r * Math.Sin(p.phi);
            }
            if (trackR.Count == 0) { debrisMedR = -1; debrisOut = bound > 0 ? 0 : 1; return; }
            trackR.Sort();
            debrisMedR = trackR[trackR.Count / 2];
            debrisOut = (double)trackR.Count / Math.Max(bound, 1);
            debrisCentre = new Vector3((float)(sx / trackR.Count), 0, (float)(sz / trackR.Count)) * 0.5f;
        }

        // "Camera follows the debris": the orbit camera aims between the hole and the debris and backs off to keep both
        // in view; drag still turns it (audit #46, #63).
        void FollowCamera(float dt)
        {
            if (!follow || riding != null || warpTarget <= 0 || debrisMedR <= 0) { followOn = false; return; }
            followOn = true;
            Orbit.maxDistance = 1200;
            float k = 1 - Mathf.Exp(-dt * 2.5f);
            Orbit.target = Vector3.Lerp(Orbit.target, debrisCentre * 0.5f, k);
            float want = Mathf.Clamp(debrisCentre.magnitude * 1.5f + 10, 24, 1100);
            Orbit.distance = Mathf.Lerp(Orbit.distance, want, k);
        }

        // Kill the oldest n live particles (they were added first, and compaction keeps the order).
        void RetireOldest(int n)
        {
            for (int i = 0; i < nParts && n > 0; i++) if (parts[i].alive) { parts[i].alive = false; n--; }   // compaction recounts liveParts
            CompactParticles();
        }

        void CompactParticles()
        {
            int j = 0;
            for (int i = 0; i < nParts; i++)
            {
                if (!parts[i].alive) continue;
                parts[j] = parts[i]; pTemp[j] = pTemp[i]; pHeat[j] = pHeat[i]; pBase[j] = pBase[i]; pPhase[j] = pPhase[i]; pMass[j] = pMass[i];
                pKick[j] = pKick[i]; pKickDt[j] = pKickDt[i];
                j++;
            }
            nParts = liveParts = j;
        }

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

        // ------------------------------------------------------------------------------------------ simulation

        void TickSandbox(float dt)
        {
            if (noticeLeft > 0 && (noticeLeft -= dt) <= 0) notice = noticeShort = null;
            CheckExperiment();
            // paused: nothing in the sandbox moves (particles, probes, light curve, warp ramp, disk swirl) until Step
            if (paused) { if (!stepPending) return; stepPending = false; dt = 1f / 30; }
            diskClock += dt;
            double a = A;
            // After a break-up the warp follows the bound debris (audit #46): one local orbital time r^{3/2} (at the
            // debris's median radius) passes in about two seconds of play. The stream is watched passing pericentre,
            // stretching on its way out, turning round and coming back; the warp tops out at warpTarget far away and
            // drops again as the debris returns. It lets go once most of the bound debris is back. Nothing changes
            // during the hold after the break-up, and the warp is only set while it is automatic (BH audit #33).
            if (warpTarget > 0)
            {
                if (warpHold > 0) warpHold -= dt;
                else if (debrisOut < 0.1) { if (autoWarp) logWarp = Mathf.Max(logWarp, 1.5f); warpTarget = -1; }
                else if (debrisMedR > 0 && autoWarp)
                {
                    float want = (float)Math.Log10(Math.Pow(Math.Max(debrisMedR, 8), 1.5) / 2);
                    logWarp = Mathf.Clamp(want, 0.8f, warpTarget);
                }
            }
            double h = dt * Math.Pow(10, logWarp);
            simT += h;
            double rh = Kerr.Horizon(a);

            foreach (var b in bodies)
            {
                if (b.state != 0) continue;
                b.g.AdvanceT(a, simT - b.g.t, 0.08, 20000, b.rt);   // stop at r_t even when a frame spans the passage (audit #44)
                double eps = Math.Min(Math.Pow(b.rt / b.g.r, 3), 1);
                b.stretch = 1 + 2 * eps;
                if (b.g.r <= b.rt) Shred(b, b.rt);
                else if (b.g.inside) b.state = 2;
                else if (b.g.w > 0 && b.g.r > 150) b.state = 3;
            }

            // Gas: each particle integrates towards the shared clock with a step budget; one that runs out of steps keeps
            // its own time and catches up on later frames (audit #51). Gas on its way back is stepped singly so its
            // return to pericentre is caught where it happens; returned gas gets the gas model at its pKick times.
            double heatMax = 0, isco = Kerr.Isco(a), tDone = simT;
            int live = 0;
            for (int i = 0; i < nParts; i++)
            {
                ref var p = ref parts[i];
                if (!p.alive) continue;
                double t0 = p.t, dE = 0;
                int budget = p.r < 50 ? 200 : 40;   // more steps near the hole (audit #51)
                if (pPhase[i] == 0) p.AdvanceT(a, simT - p.t, 0.12, budget);
                else if (pPhase[i] == 1)
                    for (; budget > 0 && p.t < simT && !p.inside && p.peris < 2; budget--) if (p.AdvanceT(a, simT - p.t, 0.12, 1) == 0) break;
                if (pPhase[i] == 0 && p.apoR > 0 && p.E < 1) pPhase[i] = 1;   // apocentre: on its way back
                if (pPhase[i] == 1 && p.peris >= 2)                            // back at pericentre
                {
                    pPhase[i] = 2; fallbackAcc += pMass[i];
                    pKickDt[i] = GasChunk * 2 * Math.PI * Math.Pow(Math.Max(p.periR, 1), 1.5);
                    pKick[i] = p.periT + pKickDt[i];
                }
                if (pPhase[i] == 2)
                    while (budget > 0 && !p.inside && p.alive)
                    {
                        double to = Math.Min(simT, pKick[i]);
                        if (to > p.t)
                        {
                            int n = p.AdvanceT(a, to - p.t, 0.12, budget);
                            budget -= n;
                            if (n == 0) break;
                        }
                        if (p.inside || p.t < pKick[i] - 1e-9 * Math.Max(p.t, 1)) break;   // not there yet: next frame
                        // inside the ISCO gas plunges in a free-fall time and radiates almost nothing: no braking there (audit #43)
                        double P = 2 * Math.PI * Math.Pow(p.r, 1.5);
                        if (p.r > isco)
                        {
                            double E0 = p.E;
                            p.w *= Math.Exp(-pKickDt[i] / P);            // circularise: damp the radial motion over about an orbit
                            p.SolveE(a);
                            p.L *= Math.Exp(-pKickDt[i] / (12 * P));    // spread inwards (sped up: a real viscous time is far longer)
                            p.SolveE(a);
                            double de = Math.Max(E0 - p.E, 0);
                            dE += de;
                            Deposit(pKick[i], de * pMass[i]);
                        }
                        pKickDt[i] = GasChunk * P;
                        pKick[i] += pKickDt[i];
                    }
                double dtp = p.t - t0;
                if (p.inside) { p.alive = false; accreted += pMass[i]; continue; }
                if (p.E >= 1 && p.r > TexRMax * 2) { p.alive = false; continue; }
                live++;
                if (pPhase[i] == 2) tDone = Math.Min(tDone, p.t);
                // drawn heat follows the heating rate over a quarter of a local orbit of the particle's own time
                if (dtp > 0) pHeat[i] += (float)((dE / dtp - pHeat[i]) * (1 - Math.Exp(-dtp / (HeatTau * 2 * Math.PI * Math.Pow(p.r, 1.5)))));
                if (pHeat[i] > heatMax) heatMax = pHeat[i];
            }
            liveParts = live;
            heatScale = Math.Max(heatScale * Math.Exp(-h / 3000), heatMax);
            TrackDebris();
            // drop retired slots once they are half the list, at any size (BH audit #37); compaction keeps the order
            if (nParts > 0 && nParts - liveParts >= Math.Max(nParts / 2, 1)) CompactParticles();

            foreach (var p in probes) StepProbe(p, h, dt);
            if (riding != null && (!riding.g.alive && riding.g.inside == false)) StopRide();

            // luminosity, bin by finished bin: the heat released by the gas model (E is energy per unit rest mass) over
            // the bin's time; Eddington for hydrogen. Smoothed over sim time: at least the gas model's own time scale,
            // and 5 % of the time since the latest break-up, so a flare that evolves over 10^5 M is not shot noise from
            // the few particles heating up at any moment (QA-054: at spin 0.998 the gas drains fast near the small ISCO)
            double Ledd = 1.26e31 * MassKg / Phys.Msun, tDisrupt = double.NaN;
            foreach (var bb in bodies) if (bb.tDisrupt >= 0 && !(bb.tDisrupt >= tDisrupt)) tDisrupt = bb.tDisrupt;   // the earliest, as before
            double spacing = Math.Max(LumBin, Math.Pow(10, logWarp) / 30);   // a point per 1/30 s of play at this warp
            for (long done = (long)Math.Floor(tDone / LumBin); lumNext < done; lumNext++)
            {
                lumBins.TryGetValue(lumNext, out var e);
                lumBins.Remove(lumNext);
                double tb = (lumNext + 1) * LumBin;
                double tau = Math.Max(LumTau, double.IsNaN(tDisrupt) ? 0 : 0.05 * (tb - tDisrupt));
                double Lw = e / LumBin * Phys.c * Phys.c / Tunit;
                lum += (Lw / Ledd - lum) * (1 - Math.Exp(-LumBin / tau));
                curveT += LumBin;
                if (tb - curveLastT >= spacing)
                {
                    fallbackRate += (fallbackAcc / Math.Max(curveT, 1e-9) - fallbackRate) * (1 - Math.Exp(-curveT / tau));
                    fallbackAcc = 0; curveT = 0; curveLastT = tb;
                    if (liveParts > 0 || lum > 1e-9) lightCurve.Add(new Vector2((float)tb, (float)lum));
                    if (lightCurve.Count > 600) lightCurve.RemoveAt(0);
                }
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
            if (p.photon && p.turnsEnd < 0 && (g.inside || (g.w > 0 && g.r > 20))) { p.turnsEnd = g.phi / (2 * Math.PI); if (!g.inside) p.status = "escaped"; }
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
            if (liveParts == 0) return;
            texDirty = true;
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
                // per unit length, not area: a stream that stretches as it climbs stays visible (display choice)
                float w = (float)((pBase[i] + 1.5 * fh * pBase[i] * 4) * 500 / Math.Max(p.r, 1));
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
            // no live debris: the debris plane is off and the texture is not uploaded; nor is an unchanged one (paused)
            bool any = liveParts > 0;
            if (any && texDirty) { debrisTex.SetPixelData(texData, 0); debrisTex.Apply(false); texDirty = false; }
            m.SetFloat("_SimTime", (float)diskClock);   // the disk's swirl runs on the sandbox clock, so pause stops it
            m.SetTexture("_DebrisTex", debrisTex);
            m.SetVector("_DebrisRange", new Vector4((float)Math.Log(Kerr.Horizon(a) * 0.95), (float)Math.Log(TexRMax), any ? 1 : 0, 1.0f));
            double rt = 0;
            for (int i = bodies.Count - 1; i >= 0; i--) if (!bodies[i].spec.cloud && bodies[i].rt < 300) { rt = bodies[i].rt; break; }
            m.SetVector("_Guides", new Vector4((float)Kerr.Isco(a), (float)Kerr.PhotonOrbit(a), (float)rt, (float)Kerr.MarginallyBound(a)));
            m.SetFloat("_GuideOn", guides ? 1 : 0);

            int n = 0;
            var pos = new Vector4[4]; var axis = new Vector4[4]; var col = new Vector4[4]; var vel = new Vector4[4]; var rad = new Vector4[4];
            foreach (var b in bodies)
            {
                if (b.state != 0 || n >= 4 || b.Rm > 40) continue;
                var g = b.g;
                g.FourVelocity(a, out double ut, out double ur, out double uph);
                pos[n] = (Vector4)Kerr.FromBL(g.r, 0, g.phi, a); pos[n].w = (float)Math.Max(b.Rm, 0.35);
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
            for (int i = 0; i < n; i++) rad[i] = new Vector4(Mathf.Sqrt(Mathf.Max(((Vector3)pos[i]).sqrMagnitude - (float)(a * a), 0)), pos[i].w * Mathf.Max(axis[i].w, 1) * 1.2f + 0.05f, 0, 0);
            m.SetInt("_NB", n);
            m.SetVectorArray("_BRad", rad);
            m.SetVectorArray("_BPos", pos); m.SetVectorArray("_BAxis", axis); m.SetVectorArray("_BCol", col); m.SetVectorArray("_BVel", vel);
        }

        // The disk the flare feeds: thin, out to the circularisation radius 2 r_p of the latest shredded star, brightness and
        // temperature following the flare luminosity L/L_Edd (a display model: real TDE disks peak in the UV and X-rays).
        bool FlareDisk(out float outerRs, out float tempK, out float gain)
        {
            outerRs = tempK = gain = 0;
            if (lum < 1e-3 || liveParts == 0) return false;
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

        // One line on what the newest throw is doing now, in real seconds of play where that helps (audit #63).
        string Narration(out Tone tone)
        {
            tone = Tone.Normal;
            if (noticeShort != null) { tone = Tone.Hot; return noticeShort; }
            Body b = bodies.Count > 0 ? bodies[bodies.Count - 1] : null;
            double perSec = Math.Pow(10, logWarp);
            if (b == null) return probes.Count > 0 ? "probe on its orbit" : "throw something in";
            if (b.state == 0) return b.g.r > b.rt * 1.5 ? "falling in" : "stretched by the tide";
            if (b.state == 2) { tone = Tone.Cold; return "swallowed whole"; }
            if (b.state == 3) return "flew past intact";
            if (!disk && FlareDisk(out _, out _, out _)) { tone = Tone.Hot; return "flare: " + Fmt.Num(lum, 2) + " × Eddington"; }
            if (b.rp <= Kerr.MarginallyBound(A)) { tone = Tone.Cold; return "all debris swallowed"; }
            if (b.dE > 0)
            {
                double back = b.tDisrupt + 2 * Math.PI * Math.Pow(1 / (2 * b.dE), 1.5) - simT;
                if (back > 0)
                {
                    // real seconds left, allowing for the warp ramp still to come
                    double rate = warpTarget > 0 && autoWarp ? Math.Pow(10, warpTarget) : perSec;
                    return (warpHold > 0 ? "torn apart; " : "debris out; ") + "back in ~" + Fmt.Num(Math.Max(back / rate, 1), 0) + " s";
                }
            }
            return lum > 1e-3 ? "debris returning" : "debris on its way";
        }

        // Perihelion shift per orbit (degrees) of the newest probe, NaN until two pericentres were seen (QA).
        public double LastProbeShift()
        {
            if (probes.Count == 0 || !probes[probes.Count - 1].Shift(out double d)) return double.NaN;
            return d * 180 / Math.PI;
        }

        // Median factor between consecutive light-curve samples over the last 60 (1 = smooth; QA-054 saw ~3).
        double CurveJitter()
        {
            var r = new List<double>();
            for (int i = Math.Max(1, lightCurve.Count - 60); i < lightCurve.Count; i++)
                if (lightCurve[i].y > 1e-6 && lightCurve[i - 1].y > 1e-6) r.Add(Math.Exp(Math.Abs(Math.Log(lightCurve[i].y / lightCurve[i - 1].y))));
            if (r.Count == 0) return 1;
            r.Sort();
            return r[r.Count / 2];
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
            return $"cam d={Orbit.distance:0.0} tgt={Orbit.target} follow={followOn} warp={logWarp:0.00} t={simT:0} parts {nParts} alive {alive} nan {nan} circ {ph2} r {rmin:0.0}..{rmax:0.0} heat {heatScale:0.0e0} lum {lum:0.00e0}{b} curve {lightCurve.Count} pts, step-to-step ×{CurveJitter():0.000} (median)";
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
                // the fate depends on the closest approach too: r_p inside r_mb plunges, r_p inside r_t tears (audit #52a)
                double rpM = periRs * 2;
                string fate = rpM < rmb ? (rt > rpM && rt > rh ? "torn, all swallowed" : "swallowed whole")
                            : rt > rpM ? "shredded" : "flies past intact";
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
            list.Add(new Readout("What is happening", Narration(out var tone), tone));
            list.Add(new Readout("Sim clock", Fmt.Duration(simT * Tunit) + (paused ? " (paused)" : "")));
            list.Add(new Readout("1 s of play is", Fmt.Duration(Math.Pow(10, logWarp) * Tunit)));
            ProbeSummary(list, probes.Count > 0 ? probes[probes.Count - 1] : null);
        }

        void ProbeSummary(List<Readout> list, Probe p)
        {
            const string none = "—";
            list.Add(new Readout("Probe", p != null ? p.name : none));
            // light still circling until its escape or fall is recorded; then that event shows at once (BH audit #36)
            string status = p == null ? none : p.photon && p.turnsEnd < 0 && p.g.alive && !p.g.inside ? "circling" : p.status;
            list.Add(new Readout("Probe status", status));
            if (p != null && p.photon) list.Add(new Readout("Light: turns", (p.turnsEnd >= 0 ? p.turnsEnd : p.g.phi / (2 * Math.PI)).ToString("0.0")));
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
            follow = ui.Toggle("Camera follows the debris", follow);
            bool r = ui.Toggle("Ride along with the probe", ride);
            if (r != ride)
            {
                ride = r;
                Probe last = null;
                for (int i = probes.Count - 1; i >= 0; i--) if (!probes[i].photon && probes[i].g.alive) { last = probes[i]; break; }
                if (ride && last != null) StartRide(last); else if (!ride) StopRide();
            }
            // the slider rounds to its step every frame: compare with the rounded value, so only a real move takes the
            // clock over from the automatic warp (BH audit #33)
            autoWarp = ui.Toggle("Automatic time warp after a break-up", autoWarp);
            float shownWarp = Mathf.Round(logWarp / 0.05f) * 0.05f;
            float lw = ui.Slider(autoWarp ? "Time warp" : "Time warp (manual)", shownWarp, 0, 6, v => "1 s = " + Fmt.Duration(Math.Pow(10, v) * Tunit), 0.05f);
            if (Mathf.Abs(lw - shownWarp) > 1e-4f) { logWarp = lw; autoWarp = false; }
            int tc = ui.Buttons(paused ? "Resume" : "Pause", "Step 1/30 s");
            if (tc == 0) paused = !paused;
            else if (tc == 1) { paused = true; stepPending = true; }
            guides = ui.Toggle("Guide rings (ISCO, photon orbit, tidal radius)", guides);
            showMap = ui.Toggle("Map and light curve", showMap);
            GUILayout.Label("Model, not real: gas spreads in 12 orbits (real viscous time is far longer); debris drawn ≤ 1 M wide, energy spread uses the real star; bodies drawn at least 0.06 M, probes 0.45 M; gas cloud 3 rs wide (real G2-like clouds are thousands of rs); everything in the equatorial plane; the probe's clock inside the horizon runs at display speed.", ui.note);
            if (notice != null) GUILayout.Label(notice, ui.note);   // last, so nothing above it moves
        }
    }
}
