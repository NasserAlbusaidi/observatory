using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // Ray-traced Schwarzschild black hole with a Doppler-beamed thin disk.
    public class BlackHoleScene : SimScene
    {
        public override string Title => "Black Hole";
        public override string Eyebrow => "Schwarzschild, 1916";
        public override string Short => "Lensing, photon ring, beamed disk";
        public override string Blurb =>
            "The same family of solution that gives the Einstein–Rosen bridge. Light follows real Schwarzschild geodesics, so the far side of the disk is lifted over the top and the <color=#FFB547>photon ring</color> traces where light orbits at 1.5 rs.\n\n" +
            "One side of the disk is much brighter because that gas is coming at you at a big fraction of c. Turn off Doppler to see the symmetric disk.";
        public override string Note => "Spaghettification check: the tidal readout is the stretch across a 2 m body. Small black holes shred you outside the horizon; supermassive ones let you cross it without noticing.";

        static readonly string[] MassNames = { "Stellar (10 solar masses)", "Sagittarius A* (4.3 million)", "M87* (6.5 billion)", "TON 618 (≈66 billion)" };
        static readonly double[] Masses = { 10, 4.3e6, 6.5e9, 6.6e10 };
        int massIdx = 1;
        bool disk = true, doppler = true, lens = true;
        float outer = 13, temp = 4800;
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
            mat.SetFloat("_Disk", disk ? 1 : 0);
            mat.SetFloat("_Doppler", doppler ? 1 : 0);
            mat.SetFloat("_Lens", lens ? 1 : 0);
            mat.SetFloat("_Outer", outer);
            mat.SetFloat("_Temp", temp);
            mat.SetInt("_Steps", Mathf.RoundToInt(420 * App.StepsScale));
        }

        public override void Readouts(List<Readout> list)
        {
            double M = Masses[massIdx] * Phys.Msun;
            double rs = 2 * Phys.G * M / (Phys.c * Phys.c);
            double r = Cam.transform.position.magnitude;
            double rm = r * rs;
            double dil = r > 1 ? System.Math.Sqrt(1 - 1 / r) : 0;
            double tidal = 2 * Phys.G * M * 2 / (rm * rm * rm) / Phys.g;
            list.Add(new Readout("Schwarzschild radius", Fmt.Num(rs / 1000, 2) + " km"));
            list.Add(new Readout("Photon sphere", Fmt.Num(1.5 * rs / 1000, 2) + " km"));
            list.Add(new Readout("Innermost stable orbit", Fmt.Num(3 * rs / 1000, 2) + " km"));
            list.Add(new Readout("Your distance", r.ToString("0.00") + " rs = " + Fmt.Num(rm / 1000, 2) + " km"));
            list.Add(new Readout("Your clock vs far away", dil.ToString("0.0000") + "×", dil < 0.8 ? Tone.Hot : Tone.Normal));
            list.Add(new Readout("1 hour for you is", Fmt.Num(1 / System.Math.Max(dil, 1e-9), 3) + " h outside"));
            list.Add(new Readout("Tidal stretch on a body", Fmt.Num(tidal) + " g", tidal > 10 ? Tone.Bad : Tone.Normal));
            list.Add(new Readout("Verdict", tidal > 1e3 ? "spaghetti" : tidal > 10 ? "very bad day" : "survivable (for now)", tidal > 10 ? Tone.Bad : Tone.Normal));
        }

        public override void Controls(PanelUI ui)
        {
            massIdx = ui.Choice("Black hole", massIdx, MassNames);
            disk = ui.Toggle("Accretion disk", disk);
            doppler = ui.Toggle("Doppler beaming + redshift", doppler);
            lens = ui.Toggle("Gravitational lensing", lens);
            outer = ui.Slider("Disk outer radius", outer, 6, 20, v => v.ToString("0.0") + " rs", 0.5f);
            temp = ui.Slider("Peak disk temperature", temp, 2500, 20000, v => Fmt.Num(v, 0) + " K", 100);
        }
    }
}
