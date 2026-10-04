using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // Alcubierre warp bubble: York-time height field, the ship riding the flat centre, space streaming past.
    public class WarpScene : SimScene
    {
        public override string Title => "Warp Bubble";
        public override string Eyebrow => "Alcubierre, 1994";
        public override string Short => "Shrink space ahead, stretch it behind";
        public override string Blurb =>
            "The ship never moves through space. The bubble wall <color=#69D2FF>contracts space in front</color> and <color=#FFB547>expands it behind</color>, and the flat patch in the middle is carried along. The grid is spacetime; its height is the York time from Alcubierre's paper.\n\n" +
            "Push the speed past 1 c. Nothing in the maths stops you, which is the whole problem with \"mathematically possible\".";
        public override string Note => "The bill: the wall needs negative energy density. The original estimates came out far beyond the mass-energy of the observable universe. Later reshaped bubbles cut that by many orders of magnitude, but it's still negative energy nobody can make. Above 1 c the crew also can't signal the front of their own bubble, so they can't switch it on or off. The energy readout only compares this wall shape with the default bubble; it says nothing about how big the real bill is or whether one could be built.";

        float v = 2, R = 3, sigma = 2.5f, amp = 1.2f;
        Material surf, streakMat;
        Transform ship, glow;
        Mesh streakMesh;
        readonly List<Vector3> streakBase = new List<Vector3>();
        readonly List<Vector3> pts = new List<Vector3>();
        readonly List<Color> cols = new List<Color>();
        Transform shell;
        float scroll, t;
        Vector3 glowScale;
        int view;

        protected override void Build()
        {
            Shader.SetGlobalVector("_FOWSunDir", new Vector3(-0.5f, 0.8f, -0.35f).normalized);
            // spacetime grid
            int n = 300; float size = 36;
            var verts = new Vector3[(n + 1) * (n + 1)];
            for (int z = 0; z <= n; z++)
                for (int x = 0; x <= n; x++)
                    verts[z * (n + 1) + x] = new Vector3(-size / 2 + size * x / n, 0, -size / 2 + size * z / n);
            var idx = new int[n * n * 6];
            int k = 0;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int a = z * (n + 1) + x, b = a + n + 1;
                    idx[k++] = a; idx[k++] = b; idx[k++] = a + 1;
                    idx[k++] = a + 1; idx[k++] = b; idx[k++] = b + 1;
                }
            var grid = Own(new Mesh { name = "WarpGrid", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 });
            grid.vertices = verts; grid.triangles = idx;
            grid.bounds = new Bounds(Vector3.zero, new Vector3(size, 40, size));
            surf = OwnMat(Gfx.Mat("WarpSurface"));
            Gfx.Obj("Spacetime", Root, grid, surf);

            ship = Models.Spawn("WarpShip", Root, new Color(0.85f, 0.86f, 0.9f)).transform;
            ship.localPosition = new Vector3(0, 0.35f, 0);
            Models.AlignMarker(ship, "Nose", Vector3.right);
            ship.localScale *= 1.5f;
            glow = Models.FindDeep(ship, "Engine") ?? ship;
            glowScale = glow.localScale;

            // bubble shell: lat/long wireframe
            var shellMesh = Own(Gfx.LineMesh("Shell"));
            var sp = new List<Vector3>(); var sc = new List<Color>();
            var c = new Color(0.41f, 0.82f, 1f, 0.12f);
            for (int lat = -60; lat <= 60; lat += 20)
                for (int lon = 0; lon < 360; lon += 6)
                {
                    sp.Add(Sph(lat, lon)); sp.Add(Sph(lat, lon + 6)); sc.Add(c); sc.Add(c);
                }
            for (int lon = 0; lon < 360; lon += 20)
                for (int lat = -90; lat < 90; lat += 6)
                {
                    sp.Add(Sph(lat, lon)); sp.Add(Sph(lat + 6, lon)); sc.Add(c); sc.Add(c);
                }
            Gfx.SetLines(shellMesh, sp, sc);
            shell = Gfx.Obj("Bubble", Root, shellMesh, OwnMat(Gfx.Unlit(Color.white, true))).transform;

            // near-field streaks that stream past to show motion relative to distant space
            streakBase.Clear(); // this scene object is reused on every visit
            for (int i = 0; i < 900; i++) streakBase.Add(Random.insideUnitSphere * 60f);
            streakMesh = Own(Gfx.LineMesh("Streaks"));
            streakMat = OwnMat(Gfx.Unlit(Color.white, true));
            Gfx.Obj("Streaks", Root, streakMesh, streakMat);
            Gfx.Starfield(Root, 5000);

            view = 0;
            ApplyView();
        }

        void ApplyView()
        {
            // Close-up frames the ship from the front quarter so the model's detail reads; min distance lets you scroll in.
            if (view == 0) Orbit.Set(new Vector3(-6.5f, 5.5f, -8.5f), Vector3.zero, 1.2f, 40);
            else Orbit.Set(new Vector3(1.4f, 1.6f, -4.6f), new Vector3(0, 0.35f, 0), 1.2f, 40);
            Orbit.maxPitch = 85; Orbit.minPitch = 3;
        }

        public void ShowCloseUp() { view = 1; ApplyView(); }

        static Vector3 Sph(float lat, float lon)
        {
            float la = lat * Mathf.Deg2Rad, lo = lon * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(la) * Mathf.Cos(lo), Mathf.Sin(la), Mathf.Cos(la) * Mathf.Sin(lo));
        }

        public override void Tick(float dt)
        {
            t += dt;
            scroll += dt * v * 1.5f;
            surf.SetFloat("_V", v); surf.SetFloat("_R", R); surf.SetFloat("_Sigma", sigma); surf.SetFloat("_Amp", amp);
            surf.SetFloat("_Scroll", scroll);
            surf.SetFloat("_AmpCol", 1f / Mathf.Max(0.15f, v * sigma * 0.35f));
            shell.localScale = Vector3.one * R;
            ship.localPosition = new Vector3(0, 0.35f + Mathf.Sin(t * 1.3f) * 0.04f, 0);
            if (glow != ship) glow.localScale = glowScale * (1 + Mathf.Sin(t * 9) * 0.15f);

            pts.Clear(); cols.Clear();
            float len = Mathf.Clamp(v * 0.8f, 0.2f, 6f);
            var sc = new Color(0.75f, 0.85f, 1f, 0.5f);
            foreach (var b in streakBase)
            {
                float x = Mathf.Repeat(b.x - scroll * 4 + 60, 120) - 60;
                pts.Add(new Vector3(x, b.y, b.z)); pts.Add(new Vector3(x + len, b.y, b.z));
                cols.Add(sc); cols.Add(new Color(sc.r, sc.g, sc.b, 0));
            }
            Gfx.SetLines(streakMesh, pts, cols);
        }

        public override void Readouts(List<Readout> list)
        {
            // Pfenning & Ford 1997 eq. 26: total energy ∝ −v² ∫ r² f'(r)² dr for the shader's tanh shape, integrated
            // numerically so thick walls are right too (the old R²σ thin-wall scaling was 57% low at R 1.5, σ 0.6).
            if (R != relR || sigma != relSigma) { relR = R; relSigma = sigma; relI = ShapeIntegral(R, sigma); }
            double rel = v * v * relI / (2.0 * 2.0 * DefaultIntegral); // default bubble: v 2 c, R 3, σ 2.5
            list.Add(new Readout("Bubble speed", v.ToString("0.0") + " c", v > 1 ? Tone.Hot : Tone.Normal));
            list.Add(new Readout("Crew's proper acceleration", "0 g (free fall)", Tone.Cold));
            list.Add(new Readout("Time dilation inside", "none (γ = 1)"));
            list.Add(new Readout("Proxima Centauri in", Fmt.Years(4.24 / v)));
            list.Add(new Readout("Bubble radius", (R * 33).ToString("0") + " m"));
            // Pfenning & Ford 1997: Δ = (1 + tanh²σR)² / (2σ tanh σR), ≈ 2/σ for a thin wall
            double th = System.Math.Tanh(sigma * R);
            list.Add(new Readout("Wall thickness ≈", (33 * System.Math.Pow(1 + th * th, 2) / (2 * sigma * th)).ToString("0.0") + " m"));
            list.Add(new Readout("Negative energy vs default bubble", Fmt.Num(rel, 2) + "× (ratio only)", Tone.Cold));
            list.Add(new Readout("Crew can steer the front?", v > 1 ? "no (beyond their horizon)" : "yes", v > 1 ? Tone.Bad : Tone.Normal));
        }

        float relR = -1, relSigma = -1;
        double relI;
        static double defaultIntegral;
        static double DefaultIntegral => defaultIntegral > 0 ? defaultIntegral : (defaultIntegral = ShapeIntegral(3, 2.5));

        // ∫₀^(R+30/σ) r² f'(r)² dr for f(r) = [tanh σ(r+R) − tanh σ(r−R)] / (2 tanh σR), by Simpson's rule.
        public static double ShapeIntegral(double R, double sigma)
        {
            const int n = 4000;
            double b = R + 30 / sigma, h = b / n, k = sigma / (2 * System.Math.Tanh(sigma * R)), sum = 0;
            for (int i = 0; i <= n; i++)
            {
                double r = i * h, c1 = System.Math.Cosh(sigma * (r + R)), c2 = System.Math.Cosh(sigma * (r - R));
                double fp = k * (1 / (c1 * c1) - 1 / (c2 * c2));
                sum += (i == 0 || i == n ? 1 : i % 2 == 1 ? 4 : 2) * r * r * fp * fp;
            }
            return sum * h / 3;
        }

        public override void Controls(PanelUI ui)
        {
            v = ui.Slider("Bubble speed", v, 0.1f, 10f, x => x.ToString("0.0") + " c", 0.1f);
            R = ui.Slider("Bubble radius", R, 1.5f, 5f, x => (x * 33).ToString("0") + " m", 0.1f);
            sigma = ui.Slider("Wall sharpness σ", sigma, 0.6f, 8f, x => x.ToString("0.0"), 0.1f);
            amp = ui.Slider("Height exaggeration", amp, 0.2f, 3f, x => x.ToString("0.0") + "×", 0.1f);
            int nv = ui.Choice("View", view, new[] { "Whole bubble", "Close on the ship" });
            if (nv != view) { view = nv; ApplyView(); }
        }
    }
}
