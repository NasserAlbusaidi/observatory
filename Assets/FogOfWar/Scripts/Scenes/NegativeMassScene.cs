using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // Newtonian gravity with signed mass. a_i = Σ G m_j (x_j − x_i)/|r|³, independent of m_i (passive = inertial mass),
    // so positive masses attract everything and negative masses repel everything.
    public class NegativeMassScene : SimScene
    {
        public override string Title => "Negative Mass";
        public override string Eyebrow => "Bondi, 1957";
        public override string Short => "Free acceleration forever";
        public override string Blurb =>
            "Amber balls have positive mass, cyan ones negative. Push a negative mass and it accelerates <color=#FFB547>toward</color> you. Put one next to a positive mass and the positive one flees while the negative one chases it.\n\n" +
            "The pair speeds up forever with no fuel, while total momentum and total energy both stay at zero. That free lunch is why most physicists think persistent negative mass can't exist.";
        public override string Note => "Not antimatter. Antihydrogen falls down; CERN's ALPHA-g measured it in 2023. Negative energy density is real in tiny Casimir-sized doses, and it's the same ingredient the wormhole and warp scenes are begging for.";

        static readonly string[] Presets = { "Bondi runaway pair (+m, −m)", "Mixed swarm", "Only negative masses", "Normal orbit (both positive)" };
        int preset;
        bool follow = true;
        float speed = 1;
        const int TRAIL = 600;

        class Body
        {
            public float m; public Vector3 x, v, a; public Transform t;
            public readonly Vector3[] trail = new Vector3[TRAIL]; public int head;
        }
        readonly List<Body> bodies = new List<Body>();
        Material posMat, negMat, posHalo, negHalo, gridMat;
        Mesh sphere, trailMesh, gridMesh;
        Transform gridT;
        float simT;
        Vector3 com;
        readonly List<Vector3> pts = new List<Vector3>();
        readonly List<Color> cols = new List<Color>();

        protected override void Build()
        {
            Shader.SetGlobalVector("_FOWSunDir", new Vector3(0.4f, 0.8f, -0.45f).normalized);
            sphere = Own(Gfx.UVSphere(48, 24));
            posMat = OwnMat(Gfx.Lit(new Color(1f, 0.71f, 0.28f), new Color(0.35f, 0.18f, 0f)));
            negMat = OwnMat(Gfx.Lit(new Color(0.41f, 0.82f, 1f), new Color(0.03f, 0.18f, 0.3f), 0.9f));
            posHalo = OwnMat(Gfx.Unlit(new Color(1f, 0.71f, 0.28f, 0.1f), true));
            negHalo = OwnMat(Gfx.Unlit(new Color(0.41f, 0.82f, 1f, 0.1f), true));
            trailMesh = Own(Gfx.LineMesh("Trails"));
            Gfx.Obj("Trails", Root, trailMesh, OwnMat(Gfx.Unlit(Color.white, true)));

            gridMesh = Own(Gfx.LineMesh("Grid"));
            var gp = new List<Vector3>(); var gc = new List<Color>();
            for (int i = -100; i <= 100; i++)
            {
                var c = new Color(0.27f, 0.31f, 0.54f, i % 10 == 0 ? 0.7f : 0.35f);
                gp.Add(new Vector3(i * 2, 0, -200)); gp.Add(new Vector3(i * 2, 0, 200)); gc.Add(c); gc.Add(c);
                gp.Add(new Vector3(-200, 0, i * 2)); gp.Add(new Vector3(200, 0, i * 2)); gc.Add(c); gc.Add(c);
            }
            Gfx.SetLines(gridMesh, gp, gc);
            gridMat = OwnMat(Gfx.Unlit(Color.white));
            gridT = Gfx.Obj("Grid", Root, gridMesh, gridMat).transform;
            Gfx.Starfield(Root, 4000);
            Orbit.Set(new Vector3(6, 9, -16), Vector3.zero, 3, 300);
            Reset();
        }

        void Add(float m, Vector3 x, Vector3 v)
        {
            var b = new Body { m = m, x = x, v = v };
            float r = 0.25f + Mathf.Pow(Mathf.Abs(m), 1f / 3f) * 0.35f;
            var go = Gfx.Obj(m > 0 ? "Positive" : "Negative", Root, sphere, m > 0 ? posMat : negMat);
            go.transform.localScale = Vector3.one * r;
            var halo = Gfx.Obj("Halo", go.transform, sphere, m > 0 ? posHalo : negHalo);
            halo.transform.localScale = Vector3.one * 1.7f;
            b.t = go.transform;
            for (int i = 0; i < TRAIL; i++) b.trail[i] = x;
            bodies.Add(b);
        }

        void Reset()
        {
            foreach (var b in bodies) Gfx.Destroy(b.t.gameObject);
            bodies.Clear(); simT = 0;
            if (preset == 0) { Add(1, new Vector3(-1.5f, 0, 0), Vector3.zero); Add(-1, new Vector3(-4, 0, 0), Vector3.zero); }
            else if (preset == 3) { Add(1, new Vector3(-2, 0, 0), new Vector3(0, 0, -0.35f)); Add(1, new Vector3(2, 0, 0), new Vector3(0, 0, 0.35f)); }
            else
            {
                for (int i = 0; i < 14; i++)
                {
                    float sgn = preset == 2 ? -1 : (i % 2 == 1 ? 1 : -1);
                    Add(sgn * (0.6f + Random.value * 0.8f), new Vector3((Random.value - 0.5f) * 10, (Random.value - 0.5f) * 3, (Random.value - 0.5f) * 10), Vector3.zero);
                }
            }
            Accel();
            Orbit.target = Vector3.zero;
        }

        void Accel()
        {
            foreach (var b in bodies) b.a = Vector3.zero;
            for (int i = 0; i < bodies.Count; i++)
                for (int j = 0; j < bodies.Count; j++)
                {
                    if (i == j) continue;
                    var d = bodies[j].x - bodies[i].x;
                    float r2 = d.sqrMagnitude + 0.25f; // softening
                    bodies[i].a += d * (bodies[j].m / (r2 * Mathf.Sqrt(r2)));
                }
        }

        public override void Tick(float dt)
        {
            int steps = 8; float h = dt * speed / steps * 2;
            for (int s = 0; s < steps; s++)
            {
                foreach (var b in bodies) { b.v += b.a * (h / 2); b.x += b.v * h; }
                Accel();
                foreach (var b in bodies) b.v += b.a * (h / 2);
                simT += h;
            }
            com = Vector3.zero;
            pts.Clear(); cols.Clear();
            foreach (var b in bodies)
            {
                b.t.localPosition = b.x;
                b.head = (b.head + 1) % TRAIL;
                b.trail[b.head] = b.x;
                com += b.x;
                var c = b.m > 0 ? new Color(1f, 0.71f, 0.28f) : new Color(0.41f, 0.82f, 1f);
                for (int i = 0; i < TRAIL - 1; i++)
                {
                    int a0 = (b.head - i + TRAIL) % TRAIL, a1 = (b.head - i - 1 + TRAIL) % TRAIL;
                    float f = 1 - (float)i / TRAIL;
                    pts.Add(b.trail[a0]); pts.Add(b.trail[a1]);
                    var cc = new Color(c.r, c.g, c.b, 0.55f * f);
                    cols.Add(cc); cols.Add(cc);
                }
            }
            Gfx.SetLines(trailMesh, pts, cols);
            if (bodies.Count > 0) com /= bodies.Count;
            if (follow && Orbit.enabled) Orbit.target = Vector3.Lerp(Orbit.target, com, 0.15f);
            gridT.localPosition = new Vector3(Mathf.Round(com.x / 20) * 20, -2, Mathf.Round(com.z / 20) * 20);
        }

        public override void Readouts(List<Readout> list)
        {
            Vector3 P = Vector3.zero; float KE = 0, vmax = 0; int np = 0, nn = 0;
            foreach (var b in bodies) { P += b.v * b.m; KE += 0.5f * b.m * b.v.sqrMagnitude; vmax = Mathf.Max(vmax, b.v.magnitude); if (b.m > 0) np++; else nn++; }
            list.Add(new Readout("Bodies", np + " positive, " + nn + " negative"));
            list.Add(new Readout("Elapsed", simT.ToString("0.0") + " s (sim)"));
            list.Add(new Readout("Total momentum |Σ m·v|", Fmt.Num(P.magnitude, 4), Tone.Cold));
            list.Add(new Readout("Total kinetic energy", Fmt.Num(KE, 4), Tone.Cold));
            list.Add(new Readout("Fastest body", vmax.ToString("0.000") + " u/s", vmax > 1 ? Tone.Hot : Tone.Normal));
            list.Add(new Readout("Centre has drifted", com.magnitude.ToString("0.0") + " u"));
            list.Add(new Readout("Fuel burned", "0", Tone.Bad));
        }

        public override void Controls(PanelUI ui)
        {
            if (ui.Buttons("Reset") == 0) Reset();
            int np = ui.Choice("Setup", preset, Presets);
            if (np != preset) { preset = np; Reset(); }
            follow = ui.Toggle("Camera follows the action", follow);
            speed = ui.Slider("Simulation speed", speed, 0.2f, 4f, v => v.ToString("0.0") + "×", 0.1f);
        }
    }
}
