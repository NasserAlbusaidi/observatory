using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // Ray-traced Kerr black hole (spin 0 = Schwarzschild) with a Page–Thorne thin disc. See BlackHole.shader.
    public class BlackHoleScene : SimScene
    {
        public override string Title => "Black Hole";
        public override string Eyebrow => spin > 0.001f ? "Kerr, 1963" : "Schwarzschild, 1916";
        public override string Short => "Lensing, photon ring, beamed disk";
        public override string Blurb =>
            "The same family of solution that gives the Einstein–Rosen bridge. Light follows real Schwarzschild geodesics, so the far side of the disk is lifted over the top and the <color=#FFB547>photon ring</color> traces where light orbits at 1.5 rs.\n\n" +
            "One side of the disk is much brighter because that gas is coming at you at a big fraction of c. Turn off Doppler to see the symmetric disk.";
        public override string Note => "Spaghettification check: the tidal readout is the stretch across a 2 m body. Small black holes shred you outside the horizon; supermassive ones let you cross it without noticing.";

        static readonly string[] MassNames = { "Stellar (10 solar masses)", "Sagittarius A* (4.3 million)", "M87* (6.5 billion)", "TON 618 (≈66 billion)" };
        static readonly double[] Masses = { 10, 4.3e6, 6.5e9, 6.6e10 };
        int massIdx = 1;
        bool disk = true, doppler = true, lens = true, clumpy = true;
        float outer = 13, temp = 4800, spin;
        float peakSpin = -1; (double r, double f) peak;
        static readonly double FluxPeak0 = Kerr.FluxPeak(0).f;
        (double r, double f) FluxPeak() { if (peakSpin != spin) { peakSpin = spin; peak = Kerr.FluxPeak(spin); } return peak; }
        Material mat;

        protected override void Build()
        {
            mat = OwnMat(Gfx.Mat("BlackHole"));
            Gfx.Fullscreen("BlackHole", Root, Cam, mat);
            Orbit.Set(new Vector3(0, 2.2f, -24f), Vector3.zero, 2.2f, 60f);
            Art.SkyFacing(Art.GalacticCentreAhead);   // the real Sagittarius star clouds behind the hole, Milky Way level with the disk
        }

        protected override void Teardown() => Art.SkyFacing(Quaternion.identity);

        public override void LateTick(float dt)
        {
            Gfx.SetRayCamera(mat, Cam);
            double a = spin;
            Kerr.SetCamera(mat, Cam.transform.position, a);
            mat.SetFloat("_Spin", spin);
            mat.SetFloat("_RH", (float)Kerr.Horizon(a));
            mat.SetFloat("_Isco", (float)Kerr.Isco(a));
            // normalise by the spin-0 peak: the temperature slider fixes the accretion rate, so spin only adds the hotter inner disk
            mat.SetFloat("_FluxMax", (float)FluxPeak0);
            mat.SetFloat("_Clumpy", clumpy ? 1 : 0);
            mat.SetFloat("_Gain", 1);
            mat.SetFloat("_SwirlRate", 15);   // disc pattern speed, sped up: ISCO period ≈ 6 s instead of 92 GM/c³
            mat.SetFloat("_StepK", App.Quality == 0 ? 0.3f : App.Quality == 1 ? 0.2f : 0.12f);
            mat.SetFloat("_Disk", disk ? 1 : 0);
            mat.SetFloat("_Doppler", doppler ? 1 : 0);
            mat.SetFloat("_Lens", lens ? 1 : 0);
            mat.SetFloat("_Outer", outer);
            mat.SetFloat("_Temp", temp);
            mat.SetInt("_Steps", 700);
        }

        public override void Readouts(List<Readout> list)
        {
            double M = Masses[massIdx] * Phys.Msun;
            double rs = 2 * Phys.G * M / (Phys.c * Phys.c);
            double r = Cam.transform.position.magnitude;
            double rm = r * rs;
            double a = spin;
            double dil = CamLapse(r, a);
            double tidal = 2 * Phys.G * M * 2 / (rm * rm * rm) / Phys.g;
            string Rs(double rM) => (rM / 2).ToString("0.00") + " rs";
            list.Add(new Readout("Schwarzschild radius (rs)", Fmt.Num(rs / 1000, 2) + " km"));
            list.Add(new Readout("Event horizon", Rs(Kerr.Horizon(a))));
            list.Add(new Readout(a > 0.001 ? "Photon orbit (with / against spin)" : "Photon sphere",
                a > 0.001 ? Rs(Kerr.PhotonOrbit(a)) + " / " + Rs(Kerr.PhotonOrbit(a, false)) : Rs(3)));
            list.Add(new Readout(a > 0.001 ? "Innermost stable orbit (with / against)" : "Innermost stable orbit",
                a > 0.001 ? Rs(Kerr.Isco(a)) + " / " + Rs(Kerr.Isco(a, false)) : Rs(6)));
            list.Add(new Readout("Hottest ring of the disk", Rs(FluxPeak().r)));
            list.Add(new Readout("Your distance", r.ToString("0.00") + " rs = " + Fmt.Num(rm / 1000, 2) + " km"));
            list.Add(new Readout("Your clock vs far away", dil.ToString("0.0000") + "×", dil < 0.8 ? Tone.Hot : Tone.Normal));
            list.Add(new Readout("1 hour for you is", Fmt.Num(1 / System.Math.Max(dil, 1e-9), 3) + " h outside"));
            list.Add(new Readout("Tidal stretch on a body", Fmt.Num(tidal) + " g", tidal > 10 ? Tone.Bad : Tone.Normal));
            list.Add(new Readout("Verdict", tidal > 1e3 ? "spaghetti" : tidal > 10 ? "very bad day" : "survivable (for now)", tidal > 10 ? Tone.Bad : Tone.Normal));
        }

        // Clock rate of the camera (a zero-angular-momentum observer) against a distant clock; r in rs units.
        static double CamLapse(double rRs, double a)
        {
            double r = 2 * rRs, D = r * r - 2 * r + a * a;
            if (D <= 0) return 0;
            double S2 = (r * r + a * a) * (r * r + a * a) - a * a * D;   // equatorial value: close enough for a readout
            return System.Math.Sqrt(r * r * D / S2);
        }

        public override void Controls(PanelUI ui)
        {
            massIdx = ui.Choice("Black hole", massIdx, MassNames);
            disk = ui.Toggle("Accretion disk", disk);
            doppler = ui.Toggle("Doppler beaming + redshift", doppler);
            lens = ui.Toggle("Gravitational lensing", lens);
            clumpy = ui.Toggle("Clumpy gas texture", clumpy);
            spin = ui.Slider("Spin (a/M)", spin, 0, 0.998f, v => v.ToString("0.000"), 0.001f);
            outer = ui.Slider("Disk outer radius", outer, 6, 20, v => v.ToString("0.0") + " rs", 0.5f);
            temp = ui.Slider(spin > 0.001f ? "Disk temperature (peak at spin 0)" : "Peak disk temperature", temp, 2500, 20000, v => Fmt.Num(v, 0) + " K", 100);
        }
    }
}
