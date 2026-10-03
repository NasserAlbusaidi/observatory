using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // Terraformed-Mars energy balance. Absorbed = S(1−A)πR²; emitted = 4πR²σT⁴/(1 + ¾τ) (grey-atmosphere greenhouse).
    // Local temperature = global mean − effective lapse rate × elevation + latitude term; the mean relaxes with thermal
    // inertia. Heuristics (labelled in the Note): pole–equator contrast ΔT = 70 K / (1 + ¾τ), applied as ΔT(cos²lat − ⅔) so
    // it leaves the mean alone; the lapse rate fades as Γ·¾τ/(1 + ¾τ) because thin air barely couples to the ground.
    // Liquid water needs local pressure above the triple point (6.117 mbar) and boils by Clausius–Clapeyron.
    public class MarsScene : SimScene
    {
        public override string Title => "Terraform Mars";
        public override string Eyebrow => "Energy balance";
        public override string Short => "The average is a liar";
        public override string Blurb =>
            "Your Universe Sandbox Mars. Crank the greenhouse and watch <color=#FFB547>energy in</color> against <color=#69D2FF>energy out</color>. While they differ the planet is still heating or cooling, whatever the thermometer says right now.\n\n" +
            "The relief is real Mars (MOLA laser altimetry): Olympus Mons, Tharsis, Hellas, Valles Marineris. Lapse rate times elevation is why the peaks freeze while the basins cook, and why the global average describes almost nowhere.";
        public override string Note => "Equilibrium dial: watch the in/out gap, not the thermometer. A big greenhouse takes decades to finish warming. (No water-vapour feedback is modelled, so there is no true runaway. Map: simple model, not a GCM. The pole–equator contrast and the lapse rate shrink as the air thins; both are heuristics. Water is liquid only where the local pressure is above 6.1 mbar, and it boils lower on high ground.)";

        const double S = 586, R = 3.3895e6;
        float tau = 2.8f, albedo = 0.22f, lapse = 6.5f, relief = 8, cwarp = 2, pressure = 1000; // pressure: mbar at datum
        const double ScaleH = 11.1, Triple = 6.117; // km, mbar

        double LatDT => 70 / (1 + 0.75 * tau);
        double LapseEff => lapse * 0.75 * tau / (1 + 0.75 * tau);
        static double Pressure(double p0, double hKm) => p0 * System.Math.Exp(-hKm / ScaleH);
        // boiling point (°C) at p mbar: 1/T = 1/373.15 − (R/L) ln(p/1013.25), L = 43.3 kJ/mol, fitted to the triple point and 100 °C at 1 atm
        static double Boil(double p) => 1 / (1 / 373.15 - 8.314 / 43300 * System.Math.Log(p / 1013.25)) - 273.15;
        int mode;
        double Tk = 210, years;
        Mesh mesh; Vector3[] baseV, verts; float[] H;
        float reliefNow = -1;
        Material mat, atmoMat;
        Transform planet;
        readonly List<int> samples = new List<int>();

        protected override void Build()
        {
            Shader.SetGlobalVector("_FOWSunDir", new Vector3(1, 0.35f, 0.25f).normalized);
            mesh = Own(Gfx.IcoSphere(7));
            samples.Clear(); // this scene object is reused on every visit
            baseV = mesh.vertices;
            verts = new Vector3[baseV.Length];
            H = new float[baseV.Length];
            var noise = new ValueNoise(7);
            Vector3 Dir(float lat, float lon) => new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
            var olympus = Dir(0.32f, -2.3f); var tharsis = Dir(0.02f, -1.9f); var hellas = Dir(-0.74f, 1.2f); var valles = Dir(-0.15f, -1.3f);
            float G(Vector3 v, Vector3 c, float s, float a) => a * Mathf.Exp(-(1 - Vector3.Dot(v, c)) / s);
            var uv2 = new Vector2[baseV.Length];
            // Real MOLA-derived relief from the art pack: km = -8.2 + grey × 29.4. Needs Read/Write on the texture,
            // which the FogOfWarArtImport postprocessor sets; without it the procedural planet below is used.
            var heightTex = Art.Tex("Textures/Mars_Height");
            bool realRelief = heightTex != null && heightTex.isReadable;
            if (heightTex != null && !heightTex.isReadable)
                Debug.LogWarning("Fog of War: Mars_Height isn't readable. Run Fog of War → 1 to re-import the art, or tick Read/Write on it.");
            for (int i = 0; i < baseV.Length; i++)
            {
                var v = baseV[i];
                if (realRelief)
                {
                    var uv = Art.SphereUV(v);
                    float hk = -8.2f + heightTex.GetPixelBilinear(uv.x, uv.y).r * 29.4f;
                    H[i] = hk;
                    uv2[i] = new Vector2(hk, 0);
                    if (i % 23 == 0) samples.Add(i);
                    continue;
                }
                float h = (noise.Fbm(new Vector3(v.x * 2 + 5, v.y * 2, v.z * 2)) - 0.5f) * 9;   // continents
                h += (noise.Fbm(v * 9, 4) - 0.5f) * 3;                                          // hills
                h += (noise.Fbm(v * 34, 3) - 0.5f) * 1.4f;                                      // rough ground
                h += 4 * Mathf.Max(0, -v.y) - 3 * Mathf.Max(0, v.y);                             // southern highlands, northern lowlands
                h += G(v, olympus, 0.0025f, 20) + G(v, tharsis, 0.03f, 7) - G(v, hellas, 0.012f, 8) - G(v, valles, 0.0015f, 5);
                H[i] = h;
                uv2[i] = new Vector2(h, 0);
                if (i % 23 == 0) samples.Add(i);
            }
            mesh.uv2 = uv2;
            mat = OwnMat(Gfx.Mat("Mars"));
            Art.Bind(mat, ("_MarsTex", "Textures/Mars_Color"));
            planet = Gfx.Obj("Mars", Root, mesh, mat).transform;
            atmoMat = OwnMat(Gfx.Unlit(new Color(0.5f, 0.7f, 1f, 0f), true));
            var atmo = Gfx.Obj("Atmosphere", Root, Own(Gfx.UVSphere(64, 32)), atmoMat);
            atmo.transform.localScale = Vector3.one * 1.06f;
            Gfx.Starfield(Root, 4000);
            Cam.fieldOfView = 40;
            Orbit.Set(new Vector3(1.2f, 0.9f, -3.6f), Vector3.zero, 1.3f, 10f);
            Tk = 210; years = 0; reliefNow = -1;
        }

        void ApplyRelief(float r)
        {
            reliefNow = r;
            float k = r / 3390f;
            for (int i = 0; i < baseV.Length; i++) verts[i] = baseV[i] * (1 + H[i] * k);
            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        double Target() => System.Math.Pow(S * (1 - albedo) / 4 * (1 + 0.75 * tau) / Phys.sigma, 0.25);

        public override void Tick(float dt)
        {
            if (!Mathf.Approximately(relief, reliefNow)) ApplyRelief(relief);
            planet.Rotate(0, -dt * 3f, 0, Space.World);
            double step = dt * cwarp;
            Tk += (Target() - Tk) * (1 - System.Math.Exp(-step * 0.35));
            years += step * 6;
            mat.SetFloat("_Tmean", (float)(Tk - 273.15));
            mat.SetFloat("_Lapse", (float)LapseEff);
            mat.SetFloat("_LatDT", (float)LatDT);
            mat.SetFloat("_P0", pressure);
            mat.SetFloat("_Mode", mode);
            mat.SetFloat("_Sea", -1);
            mat.SetFloat("_Tau", tau);
            atmoMat.SetColor("_Color", new Color(0.5f, 0.7f, 1f, Mathf.Min(0.25f, tau * 0.03f)));
        }

        public override void Readouts(List<Readout> list)
        {
            double area = System.Math.PI * R * R;
            double ein = S * (1 - albedo) * area;
            double eout = 4 * area * Phys.sigma * System.Math.Pow(Tk, 4) / (1 + 0.75 * tau);
            double mn = 1e9, mx = -1e9, sum = 0; int liquid = 0;
            foreach (int i in samples)
            {
                var v = baseV[i];
                double lat = System.Math.Asin(Mathf.Clamp(v.y, -1, 1));
                double c = System.Math.Cos(lat);
                double T = Tk - 273.15 - LapseEff * H[i] + LatDT * (c * c - 2.0 / 3);
                mn = System.Math.Min(mn, T); mx = System.Math.Max(mx, T); sum += T;
                double p = Pressure(pressure, H[i]);
                if (p > Triple && T > 0 && T < Boil(p)) liquid++;
            }
            double gap = (ein - eout) / ein;
            list.Add(new Readout("Energy in", (ein / 1e15).ToString("0.00") + " PW", Tone.Hot));
            list.Add(new Readout("Energy out", (eout / 1e15).ToString("0.00") + " PW", Tone.Cold));
            list.Add(new Readout("Balance", System.Math.Abs(gap) < 0.01 ? "equilibrium" : gap > 0 ? "heating (" + (gap * 100).ToString("0") + "% trapped)" : "cooling",
                System.Math.Abs(gap) < 0.01 ? Tone.Normal : gap > 0 ? Tone.Bad : Tone.Cold));
            list.Add(new Readout("Global average", (sum / samples.Count).ToString("0.0") + " °C"));
            list.Add(new Readout("Hottest spot", mx.ToString("0") + " °C", Tone.Hot));
            list.Add(new Readout("Coldest spot", mn.ToString("0") + " °C", Tone.Cold));
            list.Add(new Readout("Surface pressure (datum)", Fmt.Num(pressure, pressure < 100 ? 1 : 0) + " mbar" + (pressure < Triple ? " (below water's triple point)" : "")));
            list.Add(new Readout("Water boils at (datum)", pressure < Triple ? "no liquid phase" : Boil(pressure).ToString("0") + " °C"));
            list.Add(new Readout("Pole–equator contrast", LatDT.ToString("0") + " K"));
            list.Add(new Readout("Surface where water is liquid", (100.0 * liquid / samples.Count).ToString("0") + "%"));
            list.Add(new Readout("Equilibrium target", (Target() - 273.15).ToString("0.0") + " °C"));
            list.Add(new Readout("Years since change", Fmt.Num(years, 0)));
        }

        public override void Controls(PanelUI ui)
        {
            int b = ui.Buttons("Mars today", "Habitable-ish", "Hothouse (τ = 12)");
            if (b == 0) { tau = 0.13f; albedo = 0.25f; lapse = 4.5f; pressure = 6.1f; years = 0; }
            if (b == 1) { tau = 2.8f; albedo = 0.22f; lapse = 6.5f; pressure = 1000; years = 0; }
            if (b == 2) { tau = 12f; albedo = 0.2f; lapse = 6.8f; pressure = 3000; years = 0; }
            tau = ui.Slider("Greenhouse optical depth τ", tau, 0, 15, v => v.ToString("0.00"), 0.05f);
            albedo = ui.Slider("Albedo (reflectivity)", albedo, 0.05f, 0.6f, v => v.ToString("0.00"), 0.01f);
            pressure = ui.Slider("Surface pressure", pressure, 1, 5000, v => Fmt.Num(v, v < 100 ? 1 : 0) + " mbar", 0.5f);
            lapse = ui.Slider("Lapse rate (thick air)", lapse, 0, 10, v => v.ToString("0.0") + " °C/km", 0.1f);
            mode = ui.Choice("Map", mode, new[] { "Surface temperature", "Terrain, water and ice" });
            relief = ui.Slider("Relief exaggeration", relief, 0, 20, v => v.ToString("0.0") + "×", 0.5f);
            cwarp = ui.Slider("Climate time warp", cwarp, 0.1f, 10, v => v.ToString("0.0") + "×", 0.1f);
        }
    }
}
