using System;
using System.Collections.Generic;
using FogOfWar;

namespace Observatory.Checks
{
    // Runs the production model classes, without Unity or a second implementation of the equations.
    public static class PhysicsChecks
    {
        static readonly List<string> results = new List<string>();
        static int failures;
        static void Check(string name, double error, double tolerance)
        {
            bool pass = !double.IsNaN(error) && !double.IsInfinity(error) && error <= tolerance;
            if (!pass) failures++;
            results.Add($"{(pass ? "PASS" : "FAIL")} {name}: {error:E4} <= {tolerance:E4}");
        }
        static void Reject(string name, Action action)
        {
            try { action(); Check(name, 1, 0); }
            catch (ArgumentOutOfRangeException) { Check(name, 0, 0); }
        }
        static void Evolve(ThreeBodyModel m, double until, double step = ThreeBodyModel.StepSize)
        { while (until - m.Time > 1e-12) m.Step(Math.Min(step, until - m.Time)); }
        static void Evolve(DoublePendulumModel m, double until, double step = DoublePendulumModel.StepSize)
        { while (until - m.Time > 1e-12) m.Step(Math.Min(step, until - m.Time)); }
        static double Difference(DoublePendulumModel a, DoublePendulumModel b)
        {
            var x = a.Current; var y = b.Current;
            return Math.Abs(x.a - y.a) + Math.Abs(x.b - y.b) + Math.Abs(x.wa - y.wa) + Math.Abs(x.wb - y.wb);
        }

        public static string[] Run()
        {
            results.Clear(); failures = 0;
            ThreeBodies(); Pendulums();
            Reject("gravity rejects NaN kick", () => new ThreeBodyModel(0, double.NaN));
            Reject("gravity rejects infinite kick", () => new ThreeBodyModel(0, double.PositiveInfinity));
            Reject("gravity rejects unknown preset", () => new ThreeBodyModel(3));
            Reject("gravity rejects oversized step", () => new ThreeBodyModel().Step(1));
            Reject("pendulum rejects zero gravity", () => new DoublePendulumModel(0, 0, 0));
            Reject("pendulum rejects NaN angle", () => new DoublePendulumModel(double.NaN, 0));
            Reject("pendulum rejects infinite gravity", () => new DoublePendulumModel(0, 0, double.PositiveInfinity));
            Reject("pendulum rejects negative step", () => new DoublePendulumModel(0, 0).Step(-1));
            if (failures > 0) throw new Exception(string.Join(Environment.NewLine, results) + $"\n{failures} checks failed.");
            results.Add($"All {results.Count} physics checks passed. Unity rendering and input are not exercised.");
            return results.ToArray();
        }

        static void ThreeBodies()
        {
            for (int preset = 0; preset < 3; preset++)
            foreach (double kick in new[] { -0.2, 0.0, 0.2 })
            {
                var m = new ThreeBodyModel(preset, kick);
                double drift = 0, momentum = 0, angular = 0, initialL = m.AngularMomentum;
                for (int sample = 1; sample <= 1800; sample++)
                {
                    Evolve(m, sample / 30.0);
                    drift = Math.Max(drift, m.EnergyDrift); momentum = Math.Max(momentum, m.Momentum);
                    angular = Math.Max(angular, Math.Abs(m.AngularMomentum - initialL));
                }
                string name = $"gravity preset={preset} kick={kick} over 60 time units";
                Check(name + " relative energy drift", drift, 0.005);
                Check(name + " momentum", momentum, 1e-9);
                Check(name + " angular momentum", angular, 1e-8);
                double x = 0, y = 0;
                foreach (var b in m.Bodies) { x += b.mass * b.x; y += b.mass * b.y; }
                Check(name + " centre-of-mass numerator", Math.Sqrt(x * x + y * y), 1e-7);
            }

            var triangle = new ThreeBodyModel(1);
            Evolve(triangle, 5);
            double omega = Math.Sqrt(3 / Math.Pow(3 + ThreeBodyModel.Softening * ThreeBodyModel.Softening, 1.5));
            Check("equilateral solution x(t), softened analytic reference", Math.Abs(triangle.Bodies[0].x - Math.Cos(5 * omega)), 2e-6);
            Check("equilateral solution y(t), softened analytic reference", Math.Abs(triangle.Bodies[0].y - Math.Sin(5 * omega)), 2e-6);

            var eight = new ThreeBodyModel(); var initial = new ThreeBodyModel();
            Evolve(eight, 6.32591398);
            double closure = 0;
            for (int i = 0; i < 3; i++)
            { double x = eight.Bodies[i].x - initial.Bodies[i].x, y = eight.Bodies[i].y - initial.Bodies[i].y; closure = Math.Max(closure, Math.Sqrt(x * x + y * y)); }
            Check("figure-eight stays near unsoftened orbit after one reference period (model error included)", closure, 0.02);

            var replay = new ThreeBodyModel(2, 0.1); var same = new ThreeBodyModel(2, 0.1);
            Evolve(replay, 10); Evolve(same, 10);
            Check("gravity replay is deterministic", Math.Abs(replay.Bodies[2].x - same.Bodies[2].x) + Math.Abs(replay.Energy - same.Energy), 0);

            var coarse = new ThreeBodyModel(); var fine = new ThreeBodyModel(); var reference = new ThreeBodyModel();
            Evolve(coarse, 2); Evolve(fine, 2, ThreeBodyModel.StepSize / 2); Evolve(reference, 2, ThreeBodyModel.StepSize / 4);
            double ec = Math.Abs(coarse.Bodies[0].x - reference.Bodies[0].x), ef = Math.Abs(fine.Bodies[0].x - reference.Bodies[0].x);
            Check("Verlet refinement reduces short-time position error at least 3x", ef / ec, 1.0 / 3);
        }

        static void Pendulums()
        {
            var rest = new DoublePendulumModel(0, 0); Evolve(rest, 10);
            Check("pendulum downward equilibrium", Math.Abs(rest.Current.a) + Math.Abs(rest.Current.b) + Math.Abs(rest.Current.wa) + Math.Abs(rest.Current.wb), 0);
            Check("pendulum rest energy = -3mgL", Math.Abs(rest.Energy + 3 * rest.Gravity), 1e-12);

            foreach (double g in new[] { 1.0, 9.81, 20.0 })
            foreach (var launch in new[] { new[] { 15.0, 20.0 }, new[] { 120.0, -10.0 }, new[] { 179.0, 179.0 }, new[] { -179.0, 179.0 }, new[] { 180.0, 179.0 } })
            {
                var m = new DoublePendulumModel(launch[0], launch[1], g);
                double drift = 0, lengths = 0;
                for (int sample = 1; sample <= 1800; sample++)
                {
                    Evolve(m, sample / 30.0); drift = Math.Max(drift, m.EnergyDrift);
                    double x = m.TipX - m.UpperX, y = m.TipY - m.UpperY;
                    lengths = Math.Max(lengths, Math.Abs(x * x + y * y - 1) + Math.Abs(m.UpperX * m.UpperX + m.UpperY * m.UpperY - 1));
                }
                Check($"pendulum g={g} angles={launch[0]}/{launch[1]} energy drift / 3mgL over 60s", drift, 2e-5);
                Check($"pendulum g={g} angles={launch[0]}/{launch[1]} rod constraints", lengths, 2e-15);
            }

            // Linear normal mode: b=sqrt(2)*a, omega^2=g*(2-sqrt(2)), valid in the small-angle limit.
            var small = new DoublePendulumModel(0.01, Math.Sqrt(2) * 0.01);
            double period = 2 * Math.PI / Math.Sqrt(small.Gravity * (2 - Math.Sqrt(2)));
            Evolve(small, period / 2);
            Check("small-angle normal mode reaches opposite extreme after half period", Math.Abs(small.Current.a + 0.01 * Math.PI / 180), 1e-10);

            var baseModel = new DoublePendulumModel(120, -10); var twin = new DoublePendulumModel(120.01, -10);
            var identical = new DoublePendulumModel(120, -10);
            double maxSeparation = 0;
            for (int sample = 1; sample <= 900; sample++)
            {
                Evolve(baseModel, sample / 30.0); Evolve(twin, sample / 30.0); Evolve(identical, sample / 30.0);
                maxSeparation = Math.Max(maxSeparation, baseModel.TipDistance(twin));
            }
            Check("identical pendulum starts remain identical", Difference(baseModel, identical), 0);
            Check("0.01 degree perturbation reaches at least 0.5m tip separation within 30s", 0.5 / maxSeparation, 1);

            var coarse = new DoublePendulumModel(120, -10, 20); var fine = new DoublePendulumModel(120, -10, 20); var reference = new DoublePendulumModel(120, -10, 20);
            Evolve(coarse, 3); Evolve(fine, 3, DoublePendulumModel.StepSize / 2); Evolve(reference, 3, DoublePendulumModel.StepSize / 4);
            Check("RK4 refinement reduces short-time state error at least 10x", Difference(fine, reference) / Difference(coarse, reference), 0.1);
        }
    }
}
