using System;

namespace FogOfWar
{
    // Planar gravity, G=1. Plummer softening keeps close encounters finite; energy uses the same potential.
    public sealed class ThreeBodyModel
    {
        public const int StepsPerSecond = 38400;
        public const double Softening = 0.02, StepSize = 1.0 / StepsPerSecond;
        public struct Body
        {
            public double mass, x, y, vx, vy;
            public Body(double m, double x, double y, double vx = 0, double vy = 0)
            { mass = m; this.x = x; this.y = y; this.vx = vx; this.vy = vy; }
        }

        public readonly Body[] Bodies = new Body[3];
        readonly double[] ax = new double[3], ay = new double[3];
        public double Time { get; private set; }
        public double InitialEnergy { get; private set; }

        public ThreeBodyModel(int preset = 0, double kick = 0)
        {
            if (preset < 0 || preset > 2) throw new ArgumentOutOfRangeException(nameof(preset));
            if (double.IsNaN(kick) || Math.Abs(kick) > 0.2) throw new ArgumentOutOfRangeException(nameof(kick));
            if (preset == 0)
            {
                // Chenciner-Montgomery / Simo initial data; softening makes this an approximate choreography.
                Bodies[0] = new Body(1, 0.97000436, -0.24308753, 0.466203685, 0.43236573);
                Bodies[1] = new Body(1, -0.97000436, 0.24308753, 0.466203685, 0.43236573);
                Bodies[2] = new Body(1, 0, 0, -0.93240737, -0.86473146);
            }
            else if (preset == 1)
            {
                double omega = Math.Sqrt(3 / Math.Pow(3 + Softening * Softening, 1.5));
                for (int i = 0; i < 3; i++)
                {
                    double a = i * 2 * Math.PI / 3, x = Math.Cos(a), y = Math.Sin(a);
                    Bodies[i] = new Body(1, x, y, -omega * y, omega * x);
                }
            }
            else
            {
                Bodies[0] = new Body(3, 1, 3);
                Bodies[1] = new Body(4, -2, -1);
                Bodies[2] = new Body(5, 1, -1);
            }
            Bodies[2].vx += kick;
            double m = 0, x0 = 0, y0 = 0, vx0 = 0, vy0 = 0;
            foreach (var b in Bodies) { m += b.mass; x0 += b.mass * b.x; y0 += b.mass * b.y; vx0 += b.mass * b.vx; vy0 += b.mass * b.vy; }
            for (int i = 0; i < 3; i++)
            { Bodies[i].x -= x0 / m; Bodies[i].y -= y0 / m; Bodies[i].vx -= vx0 / m; Bodies[i].vy -= vy0 / m; }
            Acceleration();
            InitialEnergy = Energy;
        }

        void Acceleration()
        {
            Array.Clear(ax, 0, 3); Array.Clear(ay, 0, 3);
            for (int i = 0; i < 3; i++)
            for (int j = i + 1; j < 3; j++)
            {
                double dx = Bodies[j].x - Bodies[i].x, dy = Bodies[j].y - Bodies[i].y;
                double r2 = dx * dx + dy * dy + Softening * Softening, f = 1 / (r2 * Math.Sqrt(r2));
                ax[i] += Bodies[j].mass * dx * f; ay[i] += Bodies[j].mass * dy * f;
                ax[j] -= Bodies[i].mass * dx * f; ay[j] -= Bodies[i].mass * dy * f;
            }
        }

        public void Step(double h = StepSize)
        {
            if (double.IsNaN(h) || h <= 0 || h > StepSize) throw new ArgumentOutOfRangeException(nameof(h));
            for (int i = 0; i < 3; i++)
            {
                Bodies[i].vx += ax[i] * h / 2; Bodies[i].vy += ay[i] * h / 2;
                Bodies[i].x += Bodies[i].vx * h; Bodies[i].y += Bodies[i].vy * h;
            }
            Acceleration();
            for (int i = 0; i < 3; i++) { Bodies[i].vx += ax[i] * h / 2; Bodies[i].vy += ay[i] * h / 2; }
            Time += h;
        }

        public double Energy
        {
            get
            {
                double e = 0;
                for (int i = 0; i < 3; i++)
                {
                    var a = Bodies[i];
                    e += a.mass * (a.vx * a.vx + a.vy * a.vy) / 2;
                    for (int j = i + 1; j < 3; j++)
                    {
                        double dx = Bodies[j].x - a.x, dy = Bodies[j].y - a.y;
                        e -= a.mass * Bodies[j].mass / Math.Sqrt(dx * dx + dy * dy + Softening * Softening);
                    }
                }
                return e;
            }
        }

        public double EnergyDrift => Math.Abs(Energy - InitialEnergy) / Math.Max(Math.Abs(InitialEnergy), 1e-12);
        public double Momentum
        {
            get { double x = 0, y = 0; foreach (var b in Bodies) { x += b.mass * b.vx; y += b.mass * b.vy; } return Math.Sqrt(x * x + y * y); }
        }
        public double AngularMomentum
        {
            get { double l = 0; foreach (var b in Bodies) l += b.mass * (b.x * b.vy - b.y * b.vx); return l; }
        }
        public double ClosestPair
        {
            get
            {
                double d = double.MaxValue;
                for (int i = 0; i < 3; i++) for (int j = i + 1; j < 3; j++)
                { double x = Bodies[j].x - Bodies[i].x, y = Bodies[j].y - Bodies[i].y; d = Math.Min(d, Math.Sqrt(x * x + y * y)); }
                return d;
            }
        }
    }
}
