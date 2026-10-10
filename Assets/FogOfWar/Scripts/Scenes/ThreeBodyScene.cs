using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar
{
    public sealed class ThreeBodyScene : SimScene
    {
        public override string Title => "Three-Body Lab";
        public override string Eyebrow => "Order on the edge of chaos";
        public override string Short => "Three suns. No fixed centre.";
        public override string Blurb => "Each body pulls on the other two. Start with a figure-eight dance, an unstable rotating triangle, or three unequal masses falling together.\n\nChange the cyan body's launch velocity by a tiny amount and replay. The same laws can produce a very different encounter. Amber, coral and cyan trails record the last 16 simulation time units.";
        public override string Note => "Planar Newtonian gravity, G = 1, in arbitrary units. Force is softened below 0.02 units; the figure-eight is approximate. Spheres are enlarged markers, not stellar surfaces. No collisions, mergers or relativity. Energy drift measures numerical error, not new physics.";

        static readonly string[] Presets = { "Figure-eight dance", "Rotating triangle", "3 : 4 : 5 close encounter" };
        static readonly Color[] Colors = { new Color(1, 0.71f, 0.28f), new Color(1, 0.38f, 0.38f), new Color(0.41f, 0.82f, 1) };
        const float Scale = 3;
        readonly Transform[] markers = new Transform[3];
        readonly LabTrail[] trails = { new LabTrail(), new LabTrail(), new LabTrail() };
        readonly List<Vector3> points = new List<Vector3>(6000);
        readonly List<Color> colors = new List<Color>(6000);
        ThreeBodyModel model;
        Mesh trailMesh;
        int preset, sampleSteps;
        float speed = 1, kick;
        bool paused, autoFrame = true, showTrails = true;
        double accumulator;

        protected override void Build()
        {
            Shader.SetGlobalVector("_FOWSunDir", new Vector3(-0.4f, 0.8f, -0.5f).normalized);
            var sphere = Own(Gfx.UVSphere(40, 20));
            for (int i = 0; i < 3; i++)
            {
                markers[i] = Gfx.Obj("Body " + (i + 1), Root, sphere, OwnMat(Gfx.Lit(Colors[i], Colors[i] * 0.35f))).transform;
                var halo = Gfx.Obj("Halo", markers[i], sphere, OwnMat(Gfx.Unlit(new Color(Colors[i].r, Colors[i].g, Colors[i].b, 0.07f), true)));
                halo.transform.localScale = Vector3.one * 1.8f;
            }
            trailMesh = Own(Gfx.LineMesh("Gravity trails"));
            Gfx.Obj("Gravity trails", Root, trailMesh, OwnMat(Gfx.Unlit(Color.white, true)));
            var grid = Own(Gfx.LineMesh("Orbital plane"));
            for (int i = -12; i <= 12; i++)
            {
                var c = new Color(0.35f, 0.46f, 0.65f, i == 0 ? 0.4f : 0.12f);
                points.Add(new Vector3(i, -0.15f, -12)); points.Add(new Vector3(i, -0.15f, 12)); colors.Add(c); colors.Add(c);
                points.Add(new Vector3(-12, -0.15f, i)); points.Add(new Vector3(12, -0.15f, i)); colors.Add(c); colors.Add(c);
            }
            Gfx.SetLines(grid, points, colors); points.Clear(); colors.Clear();
            Gfx.Obj("Orbital plane", Root, grid, OwnMat(Gfx.Unlit(Color.white, true)));
            Gfx.Starfield(Root, 3000);
            Orbit.Set(new Vector3(0, 10, -11), Vector3.zero, 5, 1600);
            Reset();
        }

        Vector3 Position(int i) => new Vector3((float)model.Bodies[i].x * Scale, 0, (float)model.Bodies[i].y * Scale);

        void Reset()
        {
            // Slider endpoints are floats; 0.2f widens to slightly more than the model's double-precision limit.
            model = new ThreeBodyModel(preset, Math.Max(-0.2, Math.Min(0.2, kick)));
            accumulator = 0; sampleSteps = 0; paused = false;
            for (int i = 0; i < 3; i++)
            {
                trails[i].Clear(); trails[i].Add(Position(i));
                markers[i].localScale = Vector3.one * (0.2f * Mathf.Pow((float)model.Bodies[i].mass, 1f / 3));
            }
            Draw();
        }

        void Advance()
        {
            model.Step();
            if (++sampleSteps == ThreeBodyModel.StepsPerSecond / 60)
            { sampleSteps = 0; for (int i = 0; i < 3; i++) trails[i].Add(Position(i)); }
        }

        public override void Tick(float dt)
        {
            if (!paused)
            {
                accumulator += Math.Min(dt, 0.05f) * speed;
                while (accumulator >= ThreeBodyModel.StepSize) { Advance(); accumulator -= ThreeBodyModel.StepSize; }
            }
            Draw();
        }

        void Draw()
        {
            points.Clear(); colors.Clear();
            float radius = 0;
            for (int i = 0; i < 3; i++)
            {
                markers[i].localPosition = Position(i);
                radius = Mathf.Max(radius, markers[i].localPosition.magnitude);
                if (showTrails) trails[i].Append(points, colors, Colors[i]);
            }
            Gfx.SetLines(trailMesh, points, colors);
            if (autoFrame && Orbit.enabled && App.Rig == null) Orbit.distance = Mathf.Clamp(radius * 3.5f, 12, 1600);
        }

        public override bool HandleZoom(float wheel) { autoFrame = false; return false; }

        public override void Readouts(List<Readout> list)
        {
            list.Add(new Readout("Simulation", paused ? "PAUSED" : "RUNNING", paused ? Tone.Cold : Tone.Hot));
            list.Add(new Readout("Time (model units)", model.Time.ToString("0.00")));
            list.Add(new Readout("Masses · amber / coral / cyan", preset == 2 ? "3 / 4 / 5" : "1 / 1 / 1"));
            list.Add(new Readout("Closest pair", model.ClosestPair.ToString("0.000") + " u"));
            list.Add(new Readout("Total energy", model.Energy.ToString("0.00000")));
            list.Add(new Readout("Energy drift", (100 * model.EnergyDrift).ToString("0.0000") + " %", model.EnergyDrift > 0.005 ? Tone.Bad : Tone.Cold));
            list.Add(new Readout("Net momentum", model.Momentum.ToString("0.00E+0"), Tone.Cold));
        }

        public override void Controls(PanelUI ui)
        {
            int action = ui.Buttons(paused ? "Resume" : "Pause", "Replay", "Top view");
            if (action == 0) paused = !paused;
            if (action == 1) Reset();
            if (action == 2) Orbit.Set(new Vector3(0, 16, -1.5f), Vector3.zero, 5, 1600);
            if (paused && ui.Buttons("Step 1/60 unit") == 0) { for (int i = 0; i < ThreeBodyModel.StepsPerSecond / 60; i++) Advance(); Draw(); }
            int next = ui.Choice("Initial arrangement", preset, Presets);
            float nextKick = ui.Slider("Cyan launch nudge (restarts)", kick, -0.2f, 0.2f, v => v.ToString("+0.000;-0.000;0.000") + " u/t", 0.005f);
            if (next != preset || nextKick != kick) { preset = next; kick = nextKick; Reset(); }
            speed = ui.Slider("Simulation speed", speed, 0.1f, 4, v => v.ToString("0.0") + "×", 0.1f);
            autoFrame = ui.Toggle("Keep all three bodies in view", autoFrame);
            showTrails = ui.Toggle("Show orbital trails", showTrails);
        }

        protected override void Teardown()
        { foreach (var trail in trails) trail.Clear(); points.Clear(); colors.Clear(); model = null; }
    }
}
