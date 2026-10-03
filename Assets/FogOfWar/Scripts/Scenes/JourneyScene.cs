using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FogOfWar
{
    // Constant proper acceleration, flip at the midpoint. Units: light-years, years, c = 1.
    // The view out of the forward window is aberrated and Doppler-shifted on the GPU; the CMB blue-shifts into view.
    public class JourneyScene : SimScene
    {
        public override string Title => "1g to Anywhere";
        public override string Eyebrow => "Special relativity";
        public override string Short => "Cross the galaxy in 24 ship-years";
        public override string Blurb =>
            "Accelerate at a constant 1 g, flip at the halfway point, brake at 1 g. The view is the forward window, rendered with real <color=#FFB547>aberration</color> (the sky crowds ahead) and <color=#FFB547>Doppler shift</color> (ahead turns blue, behind goes dark).\n\n" +
            "Past γ ≈ 1,000 the cosmic microwave background is blue-shifted into visible light and the space ahead starts to glow. Drag to look behind you, or switch to the chase camera to watch the ship flip and brake tail-first.\n\nThe stars are the real sky, all 9,000 naked-eye stars from the Yale Bright Star Catalogue, turned so your destination sits dead ahead.";
        public override string Note => "Why mass is a bitch: the speed never reaches 1. Every extra 9 costs more rapidity, and the perfect-rocket fuel ratio grows exponentially with rapidity.";

        const double G_LY = 1.0323; // 1 g in ly/yr²
        static readonly string[] DestNames = { "Proxima Centauri (4.24 ly)", "Galactic centre (26,000 ly)", "The video's 220,000 ly crossing", "Andromeda (2.54 million ly)" };
        static readonly double[] Dests = { 4.24, 26000, 220000, 2537000 };
        // Real directions on the sky (RA, Dec in degrees). The 220,000 ly crossing heads through the galactic centre.
        static readonly Vector2[] DestRaDec = { new Vector2(217.429f, -62.680f), new Vector2(266.405f, -28.936f), new Vector2(266.405f, -28.936f), new Vector2(10.685f, 41.269f) };
        int dest = 2;
        float acc = 1, speed = 1;
        bool cmb = true, paused;
        int view;
        double tau;
        Material starMat, cmbMat;
        Mesh skyMesh;
        GameObject skyObj;
        Transform ship, engine;
        Quaternion shipBase = Quaternion.identity;
        Vector3 engineScale = Vector3.one;
        float flip;
        Vector3 shipCentreOffset;

        struct Trip { public double th, T, tTotal, w, t, x, tau, beta, oneMinusBeta, gamma, wMax; }

        Trip State()
        {
            double D = Dests[dest], a = acc * G_LY;
            var s = new Trip();
            s.th = Acosh(1 + a * D / 2) / a;
            s.tTotal = 2 * Math.Sinh(a * s.th) / a;
            s.T = 2 * s.th;
            s.tau = Math.Min(Math.Max(tau, 0), s.T);
            if (s.tau <= s.th)
            {
                s.w = a * s.tau; s.t = Math.Sinh(s.w) / a; s.x = (Math.Cosh(s.w) - 1) / a;
            }
            else
            {
                double r = s.T - s.tau; s.w = a * r;
                s.t = s.tTotal - Math.Sinh(s.w) / a; s.x = D - (Math.Cosh(s.w) - 1) / a;
            }
            s.beta = Math.Tanh(s.w);
            s.oneMinusBeta = 2 / (Math.Exp(2 * s.w) + 1);
            s.gamma = Math.Cosh(s.w);
            s.wMax = a * s.th;
            return s;
        }

        static double Acosh(double x) => Math.Log(x + Math.Sqrt(x * x - 1));

        protected override void Build()
        {
            cmbMat = OwnMat(Gfx.Mat("CMB"));
            Gfx.Fullscreen("CMB", Root, Cam, cmbMat);

            starMat = OwnMat(Gfx.Mat("Stars"));
            starMat.SetVector("_Fwd", Vector3.forward);
            starMat.SetFloat("_Gain", 1);
            BuildSky();
            BuildShip();

            view = 0;
            ApplyView();
            tau = 0; paused = false;
        }

        // The real sky (Bright Star Catalogue) turned so the destination sits dead ahead along +Z, the direction of
        // travel, with the galactic plane level. Falls back to a random sky with a fake Milky Way band.
        void BuildSky()
        {
            var dirs = new List<Vector3>(); var temps = new List<float>(); var mags = new List<float>();
            var cat = Art.Catalogue();
            if (cat != null)
            {
                var rd = DestRaDec[dest];
                var toWorld = Quaternion.Inverse(Quaternion.LookRotation(Art.FromRaDec(rd.x, rd.y), Art.FromRaDec(192.859f, 27.128f)));
                foreach (var d in cat.dirs) dirs.Add(toWorld * d);
                temps.AddRange(cat.temps); mags.AddRange(cat.mags);
            }
            else
            {
                int N = 12000;
                var tilt = Quaternion.AngleAxis(52f, Vector3.right);
                for (int i = 0; i < N; i++)
                {
                    Vector3 d;
                    if (i % 3 == 0)
                    {
                        float th = Random.value * Mathf.PI * 2;
                        d = tilt * new Vector3(Mathf.Cos(th), (Random.value - 0.5f) * 0.25f, Mathf.Sin(th)).normalized;
                    }
                    else d = Random.onUnitSphere;
                    float r = Random.value;
                    dirs.Add(d);
                    temps.Add(r < 0.7f ? 3000 + Random.value * 2500 : r < 0.93f ? 5500 + Random.value * 3000 : 9000 + Random.value * 16000);
                    mags.Add(0.15f + Mathf.Pow(Random.value, 6) * 2.5f);
                }
                dirs[0] = Vector3.forward; temps[0] = 6000; mags[0] = 3; // the destination, dead ahead
            }
            if (skyObj != null) Gfx.Destroy(skyObj);
            if (skyMesh != null) Gfx.Destroy(skyMesh);
            skyMesh = Own(Gfx.StarMesh(dirs, temps, mags));
            skyObj = Gfx.Obj("Sky", Root, skyMesh, starMat);
        }

        // The art pack's torch ship for the chase view: nose along +Z while accelerating, flipped tail-first to brake.
        void BuildShip()
        {
            Shader.SetGlobalVector("_FOWSunDir", new Vector3(0.5f, 0.6f, -0.45f).normalized);
            ship = Models.Spawn("TorchShip", Root, new Color(0.8f, 0.82f, 0.88f)).transform;
            Models.AlignMarker(ship, "Nose", Vector3.forward);
            var rends = ship.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                float len = Mathf.Max(b.size.x, b.size.y, b.size.z);
                float k = len > 1e-4f ? 2f / len : 1f;
                ship.localScale *= k;
                ship.localPosition -= (b.center - ship.position) * k;
            }
            shipBase = ship.localRotation;
            shipCentreOffset = ship.localPosition;
            engine = Models.FindDeep(ship, "Engine");
            if (engine != null) engineScale = engine.localScale;
            flip = 0;
        }

        public void ShowChase() { view = 1; ApplyView(); }

        void ApplyView()
        {
            ship.gameObject.SetActive(view == 1);
            if (view == 0) { Orbit.SetLook(Vector3.zero, 0, 0); Cam.fieldOfView = 75; }
            else { Orbit.Set(new Vector3(2.6f, 1.1f, -3.4f), Vector3.zero, 1.6f, 12f); Cam.fieldOfView = 60; }
        }

        public override void Tick(float dt)
        {
            var s0 = State();
            if (!paused) tau = Math.Min(s0.T, tau + dt * speed * s0.T / 45.0);
            var s = State();
            starMat.SetFloat("_Beta", (float)s.beta);
            starMat.SetFloat("_Gamma", (float)s.gamma);
            starMat.SetFloat("_SizePx", Mathf.Max(1, Screen.height / 1000f));
            cmbMat.SetFloat("_Beta", (float)s.beta);
            cmbMat.SetFloat("_Gamma", (float)s.gamma);
            cmbMat.SetFloat("_OneMinusBeta", (float)s.oneMinusBeta);
            cmbMat.SetFloat("_Show", cmb ? 1 : 0);
            cmbMat.SetVector("_Fwd", Vector3.forward);

            if (ship != null && ship.gameObject.activeSelf)
            {
                bool braking = s.tau > s.th, done = s.tau >= s.T;
                flip = Mathf.MoveTowards(flip, braking ? 1 : 0, dt * 0.6f);
                float a = Mathf.SmoothStep(0, 180, flip);
                // rotate about the ship's own centre, which sits at the origin
                ship.localRotation = Quaternion.Euler(0, a, 0) * shipBase;
                ship.localPosition = Quaternion.Euler(0, a, 0) * shipCentreOffset;
                if (engine != null)
                {
                    bool firing = !done && !paused && (flip < 0.02f || flip > 0.98f);
                    float pulse = 1 + 0.12f * Mathf.Sin(Time.time * 31f) + 0.06f * Mathf.Sin(Time.time * 53f);
                    engine.localScale = firing ? engineScale * pulse : engineScale * 0.0001f;
                }
            }
        }

        public override void LateTick(float dt) => Gfx.SetRayCamera(cmbMat, Cam);

        public override void Readouts(List<Readout> list)
        {
            var s = State();
            bool done = s.tau >= s.T;
            list.Add(new Readout("Phase", done ? "arrived" : s.tau < s.th ? "accelerating" : "braking (flipped)", done ? Tone.Cold : Tone.Normal));
            list.Add(new Readout("Ship clock", Fmt.Years(s.tau), Tone.Hot));
            list.Add(new Readout("Earth clock", Fmt.Years(s.t), Tone.Cold));
            list.Add(new Readout("Distance covered", Fmt.Num(s.x, s.x < 100 ? 2 : 0) + " / " + Fmt.Num(Dests[dest], 2) + " ly"));
            list.Add(new Readout("Speed", Fmt.Beta(s.beta, s.oneMinusBeta)));
            list.Add(new Readout("Time dilation γ", Fmt.Num(s.gamma, 2)));
            list.Add(new Readout("Whole trip, ship time", Fmt.Years(s.T)));
            list.Add(new Readout("Whole trip, Earth time", Fmt.Years(s.tTotal)));
            list.Add(new Readout("Perfect photon rocket fuel", Fmt.Num(Math.Exp(2 * s.wMax), 2) + "× payload", Tone.Bad));
        }

        public override void Controls(PanelUI ui)
        {
            int b = ui.Buttons("Restart trip", paused ? "Resume" : "Pause");
            if (b == 0) { tau = 0; paused = false; }
            if (b == 1) paused = !paused;
            int nv = ui.Choice("View", view, new[] { "Forward window", "Chase camera" });
            if (nv != view) { view = nv; ApplyView(); }
            int nd = ui.Choice("Destination", dest, DestNames);
            if (nd != dest) { dest = nd; tau = 0; BuildSky(); }
            float na = ui.Slider("Proper acceleration", acc, 0.1f, 3f, v => v.ToString("0.00") + " g", 0.05f);
            if (Mathf.Abs(na - acc) > 1e-4f) { acc = na; tau = 0; }
            speed = ui.Slider("Playback speed", speed, 0.1f, 4f, v => v.ToString("0.0") + "×", 0.1f);
            cmb = ui.Toggle("Show cosmic microwave background", cmb);
        }
    }
}
