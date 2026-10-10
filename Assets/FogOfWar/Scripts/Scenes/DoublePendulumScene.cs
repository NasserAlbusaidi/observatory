using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    public sealed class DoublePendulumScene : SimScene
    {
        public override string Title => "Double Pendulum";
        public override string Eyebrow => "Same laws. Different futures.";
        public override string Short => "Watch a tiny difference grow";
        public override string Blurb => "Amber and cyan are two independent pendulums laid over each other. They start at rest, with only a tiny difference in the upper angle.\n\nAt low energy they sway together. With a higher launch angle, watch the tips part ways and paint different trails. Pause, step, or replay the exact same experiment; there is no random force.";
        public override string Note => "Two 1 kg point masses on rigid, massless 1 m rods in a vertical plane. No friction or collisions; the two pendulums do not interact. Tip separation is not a Lyapunov exponent. Trails show 16 simulated seconds. Numerical errors also grow in chaotic motion; long-term paths are not exact predictions.";

        static readonly string[] Presets = { "Chaotic tumble", "Gentle swing", "Near upright" };
        static readonly Color[] Colors = { new Color(1, 0.71f, 0.28f), new Color(0.41f, 0.82f, 1) };
        readonly DoublePendulumModel[] models = new DoublePendulumModel[2];
        readonly Transform[,] bobs = new Transform[2, 2];
        readonly LineRenderer[] rods = new LineRenderer[2];
        readonly LabTrail[] trails = { new LabTrail(), new LabTrail() };
        readonly List<Vector3> points = new List<Vector3>(4000);
        readonly List<Color> colors = new List<Color>(4000);
        Mesh trailMesh;
        int preset, sampleSteps;
        float upper = 120, lower = -10, differencePower = -2, gravity = 9.81f, speed = 1;
        bool paused, showTrails = true;
        double accumulator;

        protected override void Build()
        {
            Shader.SetGlobalVector("_FOWSunDir", new Vector3(-0.3f, 0.6f, -0.8f).normalized);
            var sphere = Own(Gfx.UVSphere(32, 16));
            var pivot = Gfx.Obj("Fixed pivot", Root, sphere, OwnMat(Gfx.Lit(new Color(0.65f, 0.7f, 0.8f))));
            pivot.transform.localScale = Vector3.one * 0.06f;
            var lineMat = OwnMat(Gfx.Unlit(Color.white, true));
            for (int i = 0; i < 2; i++)
            {
                var mat = OwnMat(Gfx.Lit(Colors[i], Colors[i] * 0.3f));
                for (int j = 0; j < 2; j++)
                {
                    bobs[i, j] = Gfx.Obj((i == 0 ? "Amber" : "Cyan") + " bob " + (j + 1), Root, sphere, mat).transform;
                    bobs[i, j].localScale = Vector3.one * (i == 0 ? 0.085f : 0.065f);
                }
                var rod = new GameObject(i == 0 ? "Amber rods" : "Cyan rods");
                rod.transform.SetParent(Root, false);
                rods[i] = rod.AddComponent<LineRenderer>();
                rods[i].useWorldSpace = false; rods[i].positionCount = 3;
                rods[i].sharedMaterial = lineMat; rods[i].widthMultiplier = i == 0 ? 0.025f : 0.014f;
                rods[i].startColor = rods[i].endColor = Colors[i];
                rods[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                rods[i].receiveShadows = false;
            }
            trailMesh = Own(Gfx.LineMesh("Pendulum tip trails"));
            Gfx.Obj("Pendulum tip trails", Root, trailMesh, lineMat);
            var grid = Own(Gfx.LineMesh("Metre grid"));
            for (int i = -4; i <= 4; i++)
            {
                var c = new Color(0.35f, 0.46f, 0.65f, i == 0 ? 0.35f : 0.12f);
                points.Add(new Vector3(i * 0.5f, -2, 0.1f)); points.Add(new Vector3(i * 0.5f, 2, 0.1f)); colors.Add(c); colors.Add(c);
                points.Add(new Vector3(-2, i * 0.5f, 0.1f)); points.Add(new Vector3(2, i * 0.5f, 0.1f)); colors.Add(c); colors.Add(c);
            }
            Gfx.SetLines(grid, points, colors); points.Clear(); colors.Clear();
            Gfx.Obj("Metre grid", Root, grid, OwnMat(Gfx.Unlit(Color.white, true)));
            Gfx.Starfield(Root, 2000);
            FrontView(); Reset();
        }

        void FrontView() => Orbit.Set(new Vector3(0, -0.2f, -7), new Vector3(0, -0.2f, 0), 3, 20);
        Vector3 Tip(int i) => new Vector3((float)models[i].TipX, (float)models[i].TipY, -i * 0.025f);

        void Reset()
        {
            models[0] = new DoublePendulumModel(upper, lower, gravity);
            models[1] = new DoublePendulumModel(upper + Math.Pow(10, differencePower), lower, gravity);
            accumulator = 0; sampleSteps = 0; paused = false;
            for (int i = 0; i < 2; i++) { trails[i].Clear(); trails[i].Add(Tip(i)); }
            Draw();
        }

        void ApplyPreset()
        {
            upper = preset == 0 ? 120 : preset == 1 ? 15 : 179;
            lower = preset == 0 ? -10 : preset == 1 ? 20 : 179;
            Reset();
        }

        void Advance()
        {
            models[0].Step(); models[1].Step();
            if (++sampleSteps == 8) { sampleSteps = 0; for (int i = 0; i < 2; i++) trails[i].Add(Tip(i)); }
        }

        public override void Tick(float dt)
        {
            if (!paused)
            {
                accumulator += Math.Min(dt, 0.05f) * speed;
                while (accumulator >= DoublePendulumModel.StepSize) { Advance(); accumulator -= DoublePendulumModel.StepSize; }
            }
            Draw();
        }

        void Draw()
        {
            points.Clear(); colors.Clear();
            for (int i = 0; i < 2; i++)
            {
                var m = models[i];
                var top = new Vector3((float)m.UpperX, (float)m.UpperY, -i * 0.025f);
                bobs[i, 0].localPosition = top; bobs[i, 1].localPosition = Tip(i);
                rods[i].SetPosition(0, new Vector3(0, 0, -i * 0.025f)); rods[i].SetPosition(1, top); rods[i].SetPosition(2, Tip(i));
                if (showTrails) trails[i].Append(points, colors, Colors[i]);
            }
            Gfx.SetLines(trailMesh, points, colors);
        }

        public override void Readouts(List<Readout> list)
        {
            double drift = Math.Max(models[0].EnergyDrift, models[1].EnergyDrift);
            list.Add(new Readout("Simulation", paused ? "PAUSED" : "RUNNING", paused ? Tone.Cold : Tone.Hot));
            list.Add(new Readout("Elapsed", models[0].Time.ToString("0.00") + " s"));
            list.Add(new Readout("Initial angle difference", Math.Pow(10, differencePower).ToString("0.0000") + "°", Tone.Cold));
            list.Add(new Readout("Current tip separation", models[0].TipDistance(models[1]).ToString("0.0000") + " m", Tone.Hot));
            list.Add(new Readout("Amber energy", models[0].Energy.ToString("0.000") + " J"));
            list.Add(new Readout("Max drift / 3mgL", (100 * drift).ToString("0.00000") + " %", drift > 0.001 ? Tone.Bad : Tone.Cold));
            list.Add(new Readout("Rods / masses", "1 m / 1 kg each"));
        }

        public override void Controls(PanelUI ui)
        {
            int action = ui.Buttons(paused ? "Resume" : "Pause", "Replay", "Front view");
            if (action == 0) paused = !paused;
            if (action == 1) Reset();
            if (action == 2) FrontView();
            if (paused && ui.Buttons("Step 1/60 s") == 0) { for (int i = 0; i < 8; i++) Advance(); Draw(); }
            int next = ui.Choice("Launch preset", preset, Presets);
            if (next != preset) { preset = next; ApplyPreset(); }
            ui.Section("Starting conditions · changes replay");
            float a = ui.Slider("Upper angle", upper, -179, 179, v => v.ToString("0") + "°", 1);
            float b = ui.Slider("Lower angle", lower, -179, 179, v => v.ToString("0") + "°", 1);
            float d = ui.Slider("Twin's angle difference", differencePower, -4, 0, v => Mathf.Pow(10, v).ToString("0.0000") + "°", 1);
            float g = ui.Slider("Gravity", gravity, 1, 20, v => v.ToString("0.00") + " m/s²");
            if (a != upper || b != lower || d != differencePower || g != gravity)
            { upper = a; lower = b; differencePower = d; gravity = g; Reset(); }
            speed = ui.Slider("Simulation speed", speed, 0.1f, 4, v => v.ToString("0.0") + "×", 0.1f);
            showTrails = ui.Toggle("Show tip trails", showTrails);
        }

        protected override void Teardown()
        { foreach (var trail in trails) trail.Clear(); points.Clear(); colors.Clear(); models[0] = models[1] = null; }
    }
}
