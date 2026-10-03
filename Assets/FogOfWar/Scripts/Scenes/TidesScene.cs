using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    // Earth–Moon at true scale (1 unit = 1 Earth radius). Tidal stretching goes as 1/d³; the month from Kepler's third law.
    public class TidesScene : SimScene
    {
        public override string Title => "Moon 1,000 km Closer";
        public override string Eyebrow => "Tidal physics";
        public override string Short => "Tides, month length, Roche limit";
        public override string Blurb =>
            "Earth and Moon at <color=#FFB547>true scale</color>: the Moon really is that far and that small. Tidal stretching scales with 1/d³, so a small nudge does very little and a big one does a lot.\n\n" +
            "The blue shell is the ocean's tidal bulge, exaggerated about a million times so you can see it. Zoom out to find the Moon. Drag it inside the Roche limit and Earth's tides tear it into a ring of rubble.";
        public override string Note => "1,000 km closer is roughly where the Moon was about 25 million years ago, at today's 3.8 cm/yr recession rate. Tides 0.8% stronger and a month 2.6 hours shorter. Nobody would notice without instruments. The view is drawn in the Moon's orbital plane, which is tilted 5.1° to Earth's orbit.";

        const float RE = 6371, D0 = 384400, ROCHE = 18470; // km
        const double ME = 5.972e24, MM = 7.342e22;
        float d = D0 - 1000, exag = 1.5e6f, warp = 40000;
        bool bigMoon;
        Transform earth, moon, bulge, orbitT, rocheT;
        Mesh ringMesh;
        Material ringMat;
        const int NR = 5000;
        readonly float[] ra = new float[NR], rr = new float[NR], ry = new float[NR], drift = new float[NR];
        readonly Vector3[] rv = new Vector3[NR * 4];
        float ang, spin, broken;
        // Solid chunks from the art pack's Rubble.fbx riding along with a subset of the dust particles.
        const int NROCK = 360;
        readonly Transform[] rocks = new Transform[NROCK];
        readonly Vector3[] rockAxis = new Vector3[NROCK];
        readonly float[] rockSize = new float[NROCK], rockRate = new float[NROCK];

        double Period(double km) => 2 * System.Math.PI * System.Math.Sqrt(System.Math.Pow(km * 1e3, 3) / (Phys.G * (ME + MM)));

        protected override void Build()
        {
            var sun = new Vector3(1, 0.15f, -0.3f).normalized;
            Shader.SetGlobalVector("_FOWSunDir", sun);
            var sphere = Own(Gfx.UVSphere(128, 64));
            var earthMat = OwnMat(Gfx.Mat("Earth"));
            Art.Bind(earthMat, ("_DayTex", "Textures/Earth_Day"), ("_NightTex", "Textures/Earth_Night"), ("_CloudTex", "Textures/Earth_Clouds"),
                ("_OceanTex", "Textures/Earth_Ocean"), ("_NormalTex", "Textures/Earth_Normal"));
            earth = Gfx.Obj("Earth", Root, sphere, earthMat).transform;
            earth.localRotation = Quaternion.Euler(0, 0, 23.4f);
            bulge = Gfx.Obj("Tidal bulge", Root, sphere, OwnMat(Gfx.Unlit(new Color(0.23f, 0.63f, 1f, 0.3f)))).transform;
            var moonMat = OwnMat(Gfx.Mat("Moon"));
            Art.Bind(moonMat, ("_MoonTex", "Textures/Moon_Color"));
            moon = Gfx.Obj("Moon", Root, sphere, moonMat).transform;

            var circle = Own(Gfx.LineMesh("Orbit"));
            var cp = new List<Vector3>(); var cc = new List<Color>();
            for (int i = 0; i < 256; i++)
            {
                float a0 = i / 256f * Mathf.PI * 2, a1 = (i + 1) / 256f * Mathf.PI * 2;
                cp.Add(new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0))); cp.Add(new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)));
                cc.Add(Color.white); cc.Add(Color.white);
            }
            Gfx.SetLines(circle, cp, cc);
            orbitT = Gfx.Obj("Orbit", Root, circle, OwnMat(Gfx.Unlit(new Color(0.55f, 0.58f, 0.7f, 0.35f)))).transform;
            var dashed = Own(Gfx.LineMesh("Roche"));
            var dp = new List<Vector3>(); var dc = new List<Color>();
            for (int i = 0; i < cp.Count; i += 4) { dp.Add(cp[i]); dp.Add(cp[i + 1]); dc.Add(Color.white); dc.Add(Color.white); }
            Gfx.SetLines(dashed, dp, dc);
            rocheT = Gfx.Obj("Roche limit", Root, dashed, OwnMat(Gfx.Unlit(new Color(1f, 0.42f, 0.37f, 0.6f)))).transform;
            rocheT.localScale = Vector3.one * (ROCHE / RE);

            for (int i = 0; i < NR; i++) { ra[i] = Random.value * Mathf.PI * 2; rr[i] = 0.85f + Random.value * 0.3f; ry[i] = (Random.value - 0.5f) * 0.02f; }
            ringMesh = Own(new Mesh { name = "Debris" });
            ringMesh.MarkDynamic();
            var uv = new Vector2[NR * 4]; var tri = new int[NR * 6]; var col = new Color[NR * 4];
            for (int i = 0; i < NR; i++)
            {
                uv[i * 4] = new Vector2(0, 0); uv[i * 4 + 1] = new Vector2(1, 0); uv[i * 4 + 2] = new Vector2(1, 1); uv[i * 4 + 3] = new Vector2(0, 1);
                int b = i * 4;
                tri[i * 6] = b; tri[i * 6 + 1] = b + 1; tri[i * 6 + 2] = b + 2; tri[i * 6 + 3] = b; tri[i * 6 + 4] = b + 2; tri[i * 6 + 5] = b + 3;
                float g = 0.65f + Random.value * 0.3f;
                for (int k = 0; k < 4; k++) col[b + k] = new Color(g, g * 0.97f, g * 0.9f, 1);
            }
            ringMesh.vertices = rv; ringMesh.uv = uv; ringMesh.colors = col; ringMesh.triangles = tri;
            ringMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e5f);
            ringMat = OwnMat(Gfx.Unlit(Color.white));
            ringMat.mainTexture = Own(Gfx.SoftDot());
            Gfx.Obj("Debris ring", Root, ringMesh, ringMat);
            BuildRocks();
            Gfx.Starfield(Root, 5000);

            Cam.fieldOfView = 45;
            Orbit.Set(new Vector3(4.5f, 2.2f, -6.5f), Vector3.zero, 2.5f, 600f);
        }

        void BuildRocks()
        {
            var rubble = Models.Spawn("Rubble", Root, new Color(0.62f, 0.6f, 0.57f));
            var parts = new List<(Mesh mesh, Material mat, float scale)>();
            float rootScale = Mathf.Max(rubble.transform.lossyScale.x, 1e-6f);
            foreach (var mf in rubble.GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<Renderer>();
                if (mf.sharedMesh == null || r == null) continue;
                parts.Add((mf.sharedMesh, r.sharedMaterial, mf.transform.lossyScale.x / rootScale));
            }
            rubble.SetActive(false);
            for (int j = 0; j < NROCK; j++)
            {
                rocks[j] = null;
                if (parts.Count == 0) continue;
                var part = parts[j % parts.Count];
                rocks[j] = Gfx.Obj("Rock", Root, part.mesh, part.mat).transform;
                rocks[j].localRotation = Random.rotationUniform;
                rockAxis[j] = Random.onUnitSphere;
                rockRate[j] = (Random.value - 0.5f) * 120f;
                // a few 300 km slabs, mostly tens of km; 1 unit = 6,371 km
                rockSize[j] = part.scale * Mathf.Lerp(0.004f, 0.05f, Mathf.Pow(Random.value, 3));
                rocks[j].gameObject.SetActive(false);
            }
        }

        public override void Tick(float dt)
        {
            float dU = d / RE;
            ang += (float)(dt * warp / Period(d) * Mathf.PI * 2);
            spin += dt * warp / 86164f * 360f;
            earth.localRotation = Quaternion.Euler(0, 0, 23.4f) * Quaternion.Euler(0, -spin, 0);
            orbitT.localScale = Vector3.one * dU;
            bool inside = d < ROCHE;
            broken += ((inside ? 1 : 0) - broken) * Mathf.Min(1, dt * 1.5f);
            moon.gameObject.SetActive(broken < 0.5f);
            moon.localScale = Vector3.one * (1737f / RE) * (bigMoon ? 3 : 1);
            var mp = new Vector3(Mathf.Cos(ang) * dU, 0, Mathf.Sin(ang) * dU);
            moon.localPosition = mp;
            // tidally locked: the near side (longitude 0, object +X) always faces Earth
            moon.localRotation = Quaternion.Euler(0, 180f - ang * Mathf.Rad2Deg, 0);

            // Equilibrium tide range scales as (D0/d)³; the bulge leads the Moon by ~3° because Earth spins faster than the Moon orbits.
            float tide = 0.54f * Mathf.Pow(D0 / d, 3) * (inside ? 1 - broken : 1);
            float stretch = Mathf.Min(tide / (RE * 1000) * exag, 0.6f);
            bulge.localRotation = Quaternion.Euler(0, -ang * Mathf.Rad2Deg - 3f, 0);
            bulge.localScale = new Vector3(1.004f + stretch, 1.004f - stretch / 2, 1.004f - stretch / 2);
            bulge.gameObject.SetActive(tide > 0.01f);

            bool showRing = broken > 0.02f;
            if (showRing)
            {
                var right = Cam.transform.right * 0.06f; var up = Cam.transform.up * 0.06f;
                double moonRate = 1 / Period(d);
                for (int i = 0; i < NR; i++)
                {
                    float spread = (ra[i] - Mathf.PI) * broken;
                    float k = 1 + (rr[i] - 1) * broken;
                    // Kepler shear on the same clock as the Moon: each particle drifts from the Moon's phase at the
                    // difference of their mean motions
                    drift[i] = (drift[i] + (float)(dt * warp * 2 * System.Math.PI * (1 / Period(d * k) - moonRate))) % (2 * Mathf.PI);
                    float a = ang + spread + drift[i];
                    float r = dU * k;
                    var p = new Vector3(Mathf.Cos(a) * r, ry[i] * dU * 0.3f, Mathf.Sin(a) * r);
                    rv[i * 4] = p - right - up; rv[i * 4 + 1] = p + right - up; rv[i * 4 + 2] = p + right + up; rv[i * 4 + 3] = p - right + up;
                }
            }
            else for (int i = 0; i < rv.Length; i++) rv[i] = Vector3.zero;
            ringMesh.vertices = rv;

            float rockScale = bigMoon ? 3 : 1;
            for (int j = 0; j < NROCK; j++)
            {
                var rock = rocks[j];
                if (rock == null) continue;
                rock.gameObject.SetActive(showRing);
                if (!showRing) continue;
                int i = j * (NR / NROCK);
                rock.localPosition = (rv[i * 4] + rv[i * 4 + 2]) * 0.5f;
                rock.localScale = Vector3.one * rockSize[j] * rockScale * Mathf.Clamp01(broken * 1.5f);
                rock.Rotate(rockAxis[j], rockRate[j] * dt, Space.World);
            }
        }

        public override void Readouts(List<Readout> list)
        {
            double ratio = System.Math.Pow(D0 / d, 3);
            double pd = Period(d) / 86400, p0 = Period(D0) / 86400;
            double ageMyr = (D0 - d) * 1e5 / 3.8 / 1e6;
            bool inside = d < ROCHE;
            double angSize = 2 * System.Math.Atan(1737 / d) * 180 / System.Math.PI;
            list.Add(new Readout("Distance", Fmt.Num(d, 0) + " km (" + (d / RE).ToString("0.0") + " Earth radii)"));
            list.Add(new Readout("Tidal force vs today", (ratio >= 10 ? Fmt.Num(ratio, 0) : ratio.ToString("0.0000")) + "×", ratio > 1.5 ? Tone.Hot : Tone.Normal));
            list.Add(new Readout("Ocean tide (open sea)", Fmt.Num(0.54 * ratio, 2) + " m"));
            list.Add(new Readout("Month length", pd.ToString("0.00") + " days"));
            list.Add(new Readout("Change vs today", ((pd - p0) * 24).ToString("0.0") + " h"));
            list.Add(new Readout("Moon's width in the sky", angSize.ToString("0.00") + "° (now 0.52°)"));
            list.Add(new Readout("When was it this close?", d >= D0 ? "in the future" : ageMyr < 300 ? "~" + Fmt.Num(ageMyr, 1) + " Myr ago" : "billions of yr ago"));
            list.Add(new Readout("Moon status", inside ? "shredded into a ring" : "intact", inside ? Tone.Bad : Tone.Cold));
        }

        public override void Controls(PanelUI ui)
        {
            int b = ui.Buttons("Today", "1,000 km closer", "Half the distance", "Inside Roche limit");
            if (b == 0) d = D0; else if (b == 1) d = D0 - 1000; else if (b == 2) d = D0 / 2; else if (b == 3) d = 16000;
            d = ui.Slider("Moon distance", d, 12000, 500000, v => Fmt.Num(v, 0) + " km", 100);
            exag = ui.Slider("Bulge exaggeration", exag, 1e5f, 2e6f, v => (v / 1e6f).ToString("0.00") + " million×", 5e4f);
            warp = ui.Slider("Time warp", warp, 0, 200000, v => Fmt.Num(v, 0) + "×", 1000);
            bigMoon = ui.Toggle("Show the Moon 3× bigger", bigMoon);
        }
    }
}
