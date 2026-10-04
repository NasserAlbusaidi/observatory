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
        public override string Note => "Thin disk, Novikov–Thorne, with electron-scattering limb darkening; no thickness or returning light. Colours are Planck spectra seen by the human eye (CIE 1931). Disk swirl and gas spreading are sped up; exposure is lowered as spin raises the disk's peak brightness. The camera hovers with zero angular momentum unless it rides a probe. The hovering camera's tide is the Newtonian estimate, and with spin its clock and hover thrust use equatorial values; sandbox probes are followed along their own geodesics.";

        static readonly string[] MassNames = { "Stellar (10 solar masses)", "Sagittarius A* (4.3 million)", "M87* (6.5 billion)", "TON 618 (≈66 billion)" };
        static readonly double[] Masses = { 10, 4.3e6, 6.5e9, 6.6e10 };
        int massIdx = 1, shownMassIdx = 1;
        float logMass = Mathf.Log10(4.3e6f);
        bool disk = true, doppler = true, lens = true, clumpy = true;
        bool thinDisk;                 // A/B: the zero-thickness disc of step 1
        bool hotSpot; float spotRs = 4f; // orbiting hot spot (GRAVITY 2018), radius in rs (8 M)
        readonly List<float> spotCurve = new List<float>();   // the spot's share of the frame's light, one sample per frame
        int spotSeen;
        float outer = 13, temp = 4800, spin;
        public void SetSpin(float a) => spin = Mathf.Clamp(a, 0, 0.998f); // tour
        float peakSpin = -1; (double r, double f) peak;
        static readonly double FluxPeak0 = Kerr.FluxPeak(0).f;
        (double r, double f) FluxPeak() { if (peakSpin != spin) { peakSpin = spin; peak = Kerr.FluxPeak(spin); } return peak; }
        Material mat;
        float debugFate;   // review harness only: 1 ray fates, 2 first disc hit
        BHPost post;
        float rawHdr;      // review harness only: linear radiance straight out of the ray tracer, no post pass
        float exposureEV;  // the player's exposure, EV relative to BHPost.DefaultExposure
        bool prevHdr;
        // -fowbhnopost: the old single-pass look (tonemap in the ray tracer, no glare or accumulation), for A/B runs
        public static readonly bool NoPost = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fowbhnopost") >= 0;
        public BHPost Post => post;

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
            Shader.SetGlobalTexture("_PlanckLUT", PlanckLut);
            // lensing squeezes the sky in one direction: let the sky lookup filter anisotropically (BlackHole.shader)
            if (Shader.GetGlobalTexture("_SkyA") is Texture sky && sky.anisoLevel < 8) sky.anisoLevel = 8;
            prevHdr = Cam.allowHDR;
            Cam.allowHDR = true;   // the ray tracer writes linear radiance for BHPost
            post = Cam.GetComponent<BHPost>() ?? Cam.gameObject.AddComponent<BHPost>();
            post.Reset();
            BuildSandbox();
        }

        protected override void Teardown()
        {
            Art.SkyFacing(Quaternion.identity);
            TeardownSandbox();
            TeardownLens();
            if (post != null) { Gfx.Destroy(post); post = null; }
            if (Cam != null) Cam.allowHDR = prevHdr;   // at app quit the camera can already be gone
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
            mat.SetFloat("_StepK", StepK);
            mat.SetFloat("_Disk", disk ? 1 : 0);
            mat.SetFloat("_Doppler", doppler ? 1 : 0);
            mat.SetFloat("_Lens", lens ? 1 : 0);
            mat.SetFloat("_Outer", outer);
            mat.SetFloat("_Temp", temp);
            // thick disc: H_rad = (3/2)(ṁ/η) M, with the Eddington ratio ṁ from the temperature slider
            // (ṁ = 0.1 at 4800 K, ∝ T⁴ as the disc's flux) and η = 1 − E_isco
            mat.SetFloat("_Thick", thinDisk ? 0 : 1);
            mat.SetFloat("_Hrad", (float)(1.5 * EddRatio / Efficiency));
            mat.SetFloat("_SpotOn", hotSpot ? 1 : 0);
            mat.SetFloat("_SpotR", Mathf.Max(spotRs * 2, (float)Kerr.Isco(spin) * 1.02f));
            mat.SetFloat("_SpotSigma", 0.6f);
            mat.SetInt("_Steps", Steps);
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
            mat.SetFloat("_DebugFate", debugFate);
            UpdateLens(a);
            UpdatePost();
        }

        // Post pass on/off (debug views and -fowbhnopost bypass it), and this frame's jitter for temporal accumulation.
        void UpdatePost()
        {
            bool on = post != null && !NoPost && debugFate == 0 && rawHdr == 0;
            if (post != null) post.enabled = on;
            mat.SetFloat("_HDROut", on || rawHdr > 0 ? 1 : 0);
            mat.SetFloat("_SkyGain", 1f / BHPost.DefaultExposure);
            if (post != null) post.Exposure = BHPost.DefaultExposure * Mathf.Pow(2, exposureEV);
            if (!on) { mat.SetVector("_Jitter", Vector4.zero); return; }
            var t = Cam.transform;
            int pose = Hash(t.position.x, t.position.y, t.position.z, t.rotation.x, t.rotation.y, t.rotation.z, t.rotation.w, Cam.fieldOfView);
            int param = Hash(spin, App.Quality, disk ? 1 : 0, doppler ? 1 : 0, lens ? 1 : 0, clumpy ? 1 : 0, outer, temp, logMass, guides ? 1 : 0,
                Cam.pixelWidth, Cam.pixelHeight, riding != null ? 1 : 0, nParts, bodies.Count, probes.Count, thinDisk ? 1 : 0, hotSpot ? 1 : 0, spotRs, lensTable ? 1 : 0);
            bool running = Time.timeScale > 0 && !paused;
            bool dynamic = running && (nParts > 0 || bodies.Exists(b => b.state == 0) || probes.Exists(p => p.g.alive));
            bool swirl = running && ((clumpy && (disk || nParts > 0)) || hotSpot);
            post.MeasureSpot = hotSpot;
            if (!hotSpot) spotCurve.Clear();
            else if (post.SpotSamples != spotSeen)
            {
                spotSeen = post.SpotSamples;
                spotCurve.Add(post.SpotFlux);
                if (spotCurve.Count > 400) spotCurve.RemoveAt(0);
            }
            var j = post.Step(param, pose, dynamic, swirl);
            mat.SetVector("_Jitter", new Vector4(j.x * 2f / Mathf.Max(Cam.pixelWidth, 1), j.y * 2f / Mathf.Max(Cam.pixelHeight, 1), 0, 0));
        }

        // ray step size and step budget per quality (Fast, Balanced, Sharp)
        float StepK => App.Quality == 0 ? 0.3f : App.Quality == 1 ? 0.2f : 0.15f;   // Sharp 0.12 cost 4K over 16 ms (audit #61)
        int Steps => App.Quality == 2 ? 1200 : 1000;                                // 1500 put 4K Sharp over 16 ms (audit #61)

        static int Hash(params float[] v)
        {
            unchecked
            {
                int h = 17;
                foreach (float x in v) h = h * 31 + Mathf.RoundToInt(x * 1e4f);
                return h;
            }
        }

        // ---- Planck colour LUT (FOWCommon planckRGB): 256 texels over log10 T = 3 … 6, unit luminance, linear sRGB
        static Texture2D planckLut;
        static Texture2D PlanckLut
        {
            get
            {
                if (planckLut != null) return planckLut;
                planckLut = new Texture2D(256, 1, TextureFormat.RGBAHalf, false, true)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "PlanckLUT", hideFlags = HideFlags.DontSave };
                var px = new Color[256];
                for (int i = 0; i < 256; i++) px[i] = PlanckColor(System.Math.Pow(10, 3 + 3.0 * i / 255));
                planckLut.SetPixels(px);
                planckLut.Apply(false, true);
                return planckLut;
            }
        }

        // Planck's law × CIE 1931 2° colour-matching functions (the multi-lobe Gaussian fit of Wyman, Sloan and Shirley
        // 2013), 360–830 nm, → XYZ → linear sRGB (D65), scaled to luminance 1; out-of-gamut colours are mixed with white
        // of the same luminance until no channel is negative.
        public static Color PlanckColor(double T)
        {
            double X = 0, Y = 0, Z = 0;
            for (double l = 360; l <= 830; l += 1)
            {
                double lm = l * 1e-9;
                double B = 1 / (lm * lm * lm * lm * lm * (System.Math.Exp(1.438776877e-2 / (lm * T)) - 1));
                X += B * (1.056 * G(l, 599.8, 37.9, 31.0) + 0.362 * G(l, 442.0, 16.0, 26.7) - 0.065 * G(l, 501.1, 20.4, 26.2));
                Y += B * (0.821 * G(l, 568.8, 46.9, 40.5) + 0.286 * G(l, 530.9, 16.3, 31.1));
                Z += B * (1.217 * G(l, 437.0, 11.8, 36.0) + 0.681 * G(l, 459.0, 26.0, 13.8));
            }
            X /= Y; Z /= Y; Y = 1;
            double r = 3.2406 * X - 1.5372 * Y - 0.4986 * Z;
            double g = -0.9689 * X + 1.8758 * Y + 0.0415 * Z;
            double b = 0.0557 * X - 0.2040 * Y + 1.0570 * Z;
            double m = System.Math.Min(r, System.Math.Min(g, b));
            if (m < 0) { double t = -m / (1 - m); r += (1 - r) * t; g += (1 - g) * t; b += (1 - b) * t; }
            return new Color((float)r, (float)g, (float)b, 1);
        }

        static double G(double x, double mu, double s1, double s2)
        {
            double t = (x - mu) / (x < mu ? s1 : s2);
            return System.Math.Exp(-0.5 * t * t);
        }

        // Eddington ratio of the steady disc from the temperature slider (flux ∝ T⁴), and the efficiency 1 − E_isco.
        double EddRatio => 0.1 * System.Math.Pow(temp / 4800.0, 4);
        double Efficiency { get { var g = Geo.Circular(Kerr.Isco(spin) * 1.0001, spin); return 1 - g.E; } }
        double DiscH(double r) => 1.5 * EddRatio / Efficiency * System.Math.Max(1 - System.Math.Sqrt(Kerr.Isco(spin) / r), 0) + 0.01 * r;

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
            if (!thinDisk) list.Add(new Readout("Disk height at 10 M", (DiscH(10) / 10 * 100).ToString("0.0") + " % of r (ṁ " + EddRatio.ToString("0.00") + " Edd)"));
            if (hotSpot)
            {
                double rsM = System.Math.Max(spotRs * 2, Kerr.Isco(spin) * 1.02);
                double P = 2 * System.Math.PI * (System.Math.Pow(rsM, 1.5) + spin);
                list.Add(new Readout("Hot spot orbit", Fmt.Duration(P * Tunit) + " (on screen " + Fmt.Num(P / 15, 1) + " s)"));
            }
            list.Add(new Readout("Light-crossing time GM/c³", Fmt.Duration(Tunit)));
            if (riding != null) { ProbeReadouts(list); return; }
            double dil = CamLapse(r, a);
            double tidal = 2 * Phys.G * M * 2 / (rm * rm * rm) / Phys.g;
            list.Add(new Readout("Camera", "hovers (ZAMO)"));
            list.Add(new Readout("Your distance", r.ToString("0.00") + " rs"));
            // with spin, the lapse and hover thrust are the equatorial values at the camera's distance (#14)
            string eq = a > 0.001 ? " (equator approx.)" : "";
            list.Add(new Readout("Your clock vs far away" + eq, dil.ToString("0.0000") + "×", dil < 0.8 ? Tone.Hot : Tone.Normal));
            list.Add(new Readout("1 hour for you is", Fmt.Num(1 / System.Math.Max(dil, 1e-9), 3) + " h outside"));
            double hover = HoverAccel(2 * r, a) * Phys.c * Phys.c / Lunit / Phys.g;
            list.Add(new Readout("Thrust to hover" + eq, Fmt.Num(hover) + " g", hover > 10 ? Tone.Bad : hover > 3 ? Tone.Hot : Tone.Normal));
            // Newtonian 2GM·L/r³ (#14); the ridden probe's tide (Sandbox) uses the geodesic formula instead
            list.Add(new Readout("Tidal stretch, 2 m body (Newtonian)", Fmt.Num(tidal) + " g", tidal > 10 ? Tone.Bad : Tone.Normal));
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
            // what the straight-ray comparison keeps (#32)
            if (!lens) GUILayout.Label("Lensing off: light runs in straight lines, seen from flat space. Still on: a black sphere the size of the horizon, and the disk's own gravitational redshift.", ui.note);
            clumpy = ui.Toggle("Clumpy gas texture", clumpy);
            thinDisk = ui.Toggle("Thin disk (no thickness)", thinDisk);
            hotSpot = ui.Toggle("Orbiting hot spot", hotSpot);
            if (hotSpot) spotRs = ui.Slider("Hot spot orbit", spotRs, (float)Kerr.Isco(spin) * 0.51f, 10, v => v.ToString("0.0") + " rs", 0.1f);
            outer = ui.Slider("Disk outer radius", outer, 6, 20, v => v.ToString("0.0") + " rs", 0.5f);
            temp = ui.Slider(spin > 0.001f ? "Accretion rate (as spin-0 peak temp.)" : "Peak disk temperature", temp, 2500, 20000, v => Fmt.Num(v, 0) + " K", 100);
            // camera exposure, not physics: from "see the dim side" (+) to "see the beaming" (−); not saved
            exposureEV = ui.Slider("Exposure", exposureEV, -4, 4, v => (v > 0.04f ? "+" : "") + v.ToString("0.0") + " EV", 0.1f);
            SandboxControls(ui);
        }
    }
}
