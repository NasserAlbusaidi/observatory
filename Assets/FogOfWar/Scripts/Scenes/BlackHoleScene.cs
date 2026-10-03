using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // Ray-traced Kerr black hole (spin 0 = Schwarzschild) with a Page–Thorne thin disc (BlackHole.shader), plus a sandbox:
    // throw in stars, planets and gas clouds (BlackHoleScene.Sandbox.cs), drop probes on real orbits, and watch it on a
    // map and a light curve (BlackHoleScene.Hud.cs). Scene units are rs; the physics runs in M = GM/c² = rs/2.
    public partial class BlackHoleScene : SimScene
    {
        public override string Title => "Black Hole";
        public override string Eyebrow => spin > 0.001f ? "Kerr, 1963" : "Schwarzschild, 1916";
        public override string Short => "Lensing, spin, tidal disruption";
        public override string Blurb => spin > 0.001f
            ? "Every pixel is a light ray traced back along a real Kerr geodesic. The hole spins, so space near it is dragged round: the shadow is flattened on the side where light goes with the spin, and the disk reaches closer in, so it is hotter. The <color=#FFB547>photon ring</color> is light that circled the hole before it reached you.\n\n" +
              "Throw something in, or drop a probe, from the Sandbox section below."
            : "Every pixel is a light ray traced back along a real Schwarzschild geodesic, so the far side of the disk is lifted over the top. The <color=#FFB547>photon ring</color> is light that circled the hole at 1.5 rs before it reached you, and the black shadow has a radius of 2.6 rs: its edge is light that only just escaped.\n\n" +
              "One side of the disk is much brighter because that gas comes at you at half the speed of light. Throw something in, or drop a probe, from the Sandbox section below.";
        public override string Note => "Thin disk, Novikov–Thorne; no thickness, limb darkening or returning light. Disk swirl and gas spreading are sped up; exposure is lowered as spin raises the disk's peak brightness. The camera hovers with zero angular momentum unless it rides a probe.";

        static readonly string[] MassNames = { "Stellar (10 solar masses)", "Sagittarius A* (4.3 million)", "M87* (6.5 billion)", "TON 618 (≈66 billion)" };
        static readonly double[] Masses = { 10, 4.3e6, 6.5e9, 6.6e10 };
        int massIdx = 1, shownMassIdx = 1;
        float logMass = Mathf.Log10(4.3e6f);
        bool disk = true, doppler = true, lens = true, clumpy = true;
        float outer = 13, temp = 4800, spin;
        float peakSpin = -1; (double r, double f) peak;
        static readonly double FluxPeak0 = Kerr.FluxPeak(0).f;
        (double r, double f) FluxPeak() { if (peakSpin != spin) { peakSpin = spin; peak = Kerr.FluxPeak(spin); } return peak; }
        Material mat;

        // mass of the hole in kg, and the time and length units GM/c³ (s) and GM/c² (m)
        double MassKg => System.Math.Pow(10, logMass) * Phys.Msun;
        double Tunit => Phys.G * MassKg / (Phys.c * Phys.c * Phys.c);
        double Lunit => Phys.G * MassKg / (Phys.c * Phys.c);

        protected override void Build()
        {
            mat = OwnMat(Gfx.Mat("BlackHole"));
            Gfx.Fullscreen("BlackHole", Root, Cam, mat);
            Orbit.Set(new Vector3(0, 2.2f, -24f), Vector3.zero, 2.2f, 60f);
            Art.SkyFacing(Art.GalacticCentreAhead);   // the real Sagittarius star clouds behind the hole, Milky Way level with the disk
            BuildSandbox();
        }

        protected override void Teardown()
        {
            Art.SkyFacing(Quaternion.identity);
            TeardownSandbox();
        }

        public override void Tick(float dt)
        {
            SyncMass();
            TickSandbox(dt);
            FollowCamera(dt);
        }

        void SyncMass()
        {
            // QA and the shots driver set massIdx directly; the slider sets logMass
            if (massIdx != shownMassIdx)
            {
                shownMassIdx = massIdx;
                if (massIdx >= 0 && massIdx < Masses.Length) logMass = (float)System.Math.Log10(Masses[massIdx]);
            }
        }

        public override void LateTick(float dt)
        {
            SyncMass();
            double a = spin;
            RideCamera();
            Gfx.SetRayCamera(mat, Cam);
            Kerr.SetCamera(mat, Cam.transform.position, a);
            mat.SetVector("_CamVel", camVel);
            // riding a fast probe, everything ahead is blueshifted (brightness ∝ g⁴): the exposure adapts like an eye,
            // to the boost straight ahead, γ(1 + v)
            double vv = System.Math.Sqrt(System.Math.Max(1 - 1 / (camVel.w * camVel.w), 0));
            mat.SetFloat("_Exposure", (float)System.Math.Pow(camVel.w * (1 + vv), -3));
            mat.SetFloat("_Spin", spin);
            mat.SetFloat("_RH", (float)Kerr.Horizon(a));
            mat.SetFloat("_Isco", (float)Kerr.Isco(a));
            // normalise by the spin-0 peak: the temperature slider fixes the accretion rate, so spin only adds the hotter inner disk
            mat.SetFloat("_FluxMax", (float)FluxPeak0);
            mat.SetFloat("_Clumpy", clumpy ? 1 : 0);
            // camera exposure follows the spin: at a fixed accretion rate a fast spinner's inner disk is up to ~200×
            // brighter, which would clip to white; part of that is taken out so the structure stays visible
            mat.SetFloat("_Gain", (float)System.Math.Pow(FluxPeak0 / FluxPeak().f, 0.6));
            mat.SetFloat("_SwirlRate", 15);   // disc pattern speed, sped up: ISCO period ≈ 6 s instead of 92 GM/c³
            mat.SetFloat("_StepK", App.Quality == 0 ? 0.3f : App.Quality == 1 ? 0.2f : 0.15f);   // Sharp 0.12 cost 4K over 16 ms (audit #61)
            mat.SetFloat("_Disk", disk ? 1 : 0);
            mat.SetFloat("_Doppler", doppler ? 1 : 0);
            mat.SetFloat("_Lens", lens ? 1 : 0);
            mat.SetFloat("_Outer", outer);
            mat.SetFloat("_Temp", temp);
            mat.SetInt("_Steps", App.Quality == 2 ? 1200 : 1000);   // 1500 put 4K Sharp over 16 ms (audit #61)
            // a tidal disruption flare with the steady disk off: the returning debris feeds a new thin disk out to the
            // circularisation radius 2 r_p; its accretion rate follows the flare luminosity (in Eddington units)
            if (!disk && FlareDisk(out float fOuter, out float fTemp, out float fGain))
            {
                mat.SetFloat("_Disk", 1);
                mat.SetFloat("_Outer", fOuter);
                mat.SetFloat("_Temp", fTemp);
                mat.SetFloat("_Gain", fGain * (float)System.Math.Pow(FluxPeak0 / FluxPeak().f, 0.6));
            }
            UploadSandbox(mat);
        }

        public override void Readouts(List<Readout> list)
        {
            double M = MassKg;
            double rs = 2 * Phys.G * M / (Phys.c * Phys.c);
            double r = Cam.transform.position.magnitude;
            double rm = r * rs;
            double a = spin;
            string Rs(double rM) => (rM / 2).ToString("0.00") + " rs";
            list.Add(new Readout("Mass", Fmt.Num(System.Math.Pow(10, logMass), 3) + " suns"));
            list.Add(new Readout("Schwarzschild radius (rs)", Fmt.Num(rs / 1000, 3) + " km"));
            list.Add(new Readout("Event horizon", Rs(Kerr.Horizon(a))));
            list.Add(new Readout(a > 0.001 ? "Photon orbit (with / against spin)" : "Photon sphere",
                a > 0.001 ? Rs(Kerr.PhotonOrbit(a)) + " / " + Rs(Kerr.PhotonOrbit(a, false)) : Rs(3)));
            list.Add(new Readout(a > 0.001 ? "Innermost stable orbit (with / against)" : "Innermost stable orbit",
                a > 0.001 ? Rs(Kerr.Isco(a)) + " / " + Rs(Kerr.Isco(a, false)) : Rs(6)));
            list.Add(new Readout("Hottest ring of the disk", Rs(FluxPeak().r)));
            list.Add(new Readout("Light-crossing time GM/c³", Fmt.Duration(Tunit)));
            if (riding != null) { ProbeReadouts(list); return; }
            double dil = CamLapse(r, a);
            double tidal = 2 * Phys.G * M * 2 / (rm * rm * rm) / Phys.g;
            list.Add(new Readout("Camera", "hovers (ZAMO)"));
            list.Add(new Readout("Your distance", r.ToString("0.00") + " rs"));
            list.Add(new Readout("Your clock vs far away", dil.ToString("0.0000") + "×", dil < 0.8 ? Tone.Hot : Tone.Normal));
            list.Add(new Readout("1 hour for you is", Fmt.Num(1 / System.Math.Max(dil, 1e-9), 3) + " h outside"));
            double hover = HoverAccel(2 * r, a) * Phys.c * Phys.c / Lunit / Phys.g;
            list.Add(new Readout("Thrust to hover", Fmt.Num(hover) + " g", hover > 10 ? Tone.Bad : hover > 3 ? Tone.Hot : Tone.Normal));
            list.Add(new Readout("Tidal stretch on a 2 m body", Fmt.Num(tidal) + " g", tidal > 10 ? Tone.Bad : Tone.Normal));
            list.Add(new Readout("Verdict", tidal > 1e3 ? "spaghetti" : tidal > 10 ? "very bad day" : "survivable (for now)", tidal > 10 ? Tone.Bad : Tone.Normal));
            SandboxReadouts(list);
        }

        // Clock rate of the camera (a zero-angular-momentum observer) against a distant clock; r in rs units.
        static double CamLapse(double rRs, double a)
        {
            double r = 2 * rRs, D = r * r - 2 * r + a * a;
            if (D <= 0) return 0;
            double S2 = (r * r + a * a) * (r * r + a * a) - a * a * D;   // equatorial value: close enough for a readout
            return System.Math.Sqrt(r * r * D / S2);
        }

        // Proper acceleration of the hovering (ZAMO) camera in the equatorial plane, geometric units (M = 1): |∇ ln α|.
        static double HoverAccel(double r, double a)
        {
            double Lapse(double x) { double D = x * x - 2 * x + a * a; double S2 = (x * x + a * a) * (x * x + a * a) - a * a * D; return System.Math.Sqrt(System.Math.Max(x * x * D / S2, 1e-30)); }
            double D0 = r * r - 2 * r + a * a;
            if (D0 <= 0) return double.PositiveInfinity;
            double h = 1e-5 * r;
            double dln = (System.Math.Log(Lapse(r + h)) - System.Math.Log(Lapse(r - h))) / (2 * h);
            return System.Math.Sqrt(D0) / r * dln;   // 1/√g_rr = √Δ/r at the equator
        }

        public override void Controls(PanelUI ui)
        {
            int mi = ui.Choice("Black hole", massIdx, MassNames);
            if (mi != massIdx) { massIdx = mi; SyncMass(); }
            // the slider rounds to its 0.01 step every frame; compare with the rounded value so a preset's exact mass is
            // only replaced when the player actually moves the slider (audit #54)
            float shown = Mathf.Round(logMass / 0.01f) * 0.01f;
            float lm = ui.Slider("Mass (any)", shown, 0.5f, 10.9f, v => Fmt.Num(System.Math.Pow(10, v), 2) + " suns", 0.01f);
            if (Mathf.Abs(lm - shown) > 1e-4f) { logMass = lm; massIdx = shownMassIdx = -1; }
            spin = ui.Slider("Spin (a/M)", spin, 0, 0.998f, v => v.ToString("0.000"), 0.001f);
            disk = ui.Toggle("Accretion disk", disk);
            doppler = ui.Toggle("Doppler beaming + redshift", doppler);
            lens = ui.Toggle("Gravitational lensing", lens);
            clumpy = ui.Toggle("Clumpy gas texture", clumpy);
            outer = ui.Slider("Disk outer radius", outer, 6, 20, v => v.ToString("0.0") + " rs", 0.5f);
            temp = ui.Slider(spin > 0.001f ? "Accretion rate (as spin-0 peak temp.)" : "Peak disk temperature", temp, 2500, 20000, v => Fmt.Num(v, 0) + " K", 100);
            SandboxControls(ui);
        }
    }
}
