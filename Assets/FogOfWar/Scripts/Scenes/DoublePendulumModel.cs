using System;

namespace FogOfWar
{
    // Two 1 kg point masses on massless 1 m rods. Angles are measured from the downward vertical.
    public sealed class DoublePendulumModel
    {
        public const double StepSize = 1.0 / 480;
        public struct State
        {
            public double a, b, wa, wb;
            public State(double a, double b, double wa = 0, double wb = 0) { this.a = a; this.b = b; this.wa = wa; this.wb = wb; }
            public State Plus(State d, double h) => new State(a + d.a * h, b + d.b * h, wa + d.wa * h, wb + d.wb * h);
        }
        public State Current { get; private set; }
        public double Gravity { get; private set; }
        public double Time { get; private set; }
        public double InitialEnergy { get; private set; }

        public DoublePendulumModel(double upperDegrees, double lowerDegrees, double gravity = 9.81)
        {
            if (double.IsNaN(upperDegrees) || Math.Abs(upperDegrees) > 360) throw new ArgumentOutOfRangeException(nameof(upperDegrees));
            if (double.IsNaN(lowerDegrees) || Math.Abs(lowerDegrees) > 360) throw new ArgumentOutOfRangeException(nameof(lowerDegrees));
            if (double.IsNaN(gravity) || gravity <= 0 || gravity > 20) throw new ArgumentOutOfRangeException(nameof(gravity));
            Current = new State(upperDegrees * Math.PI / 180, lowerDegrees * Math.PI / 180);
            Gravity = gravity; InitialEnergy = Energy;
        }

        State Derivative(State s)
        {
            double d = s.a - s.b, sin = Math.Sin(d), cos = Math.Cos(d), den = 3 - Math.Cos(2 * d);
            return new State(s.wa, s.wb,
                (-3 * Gravity * Math.Sin(s.a) - Gravity * Math.Sin(s.a - 2 * s.b) - 2 * sin * (s.wb * s.wb + s.wa * s.wa * cos)) / den,
                2 * sin * (2 * s.wa * s.wa + 2 * Gravity * Math.Cos(s.a) + s.wb * s.wb * cos) / den);
        }

        public void Step(double h = StepSize)
        {
            if (double.IsNaN(h) || h <= 0 || h > StepSize) throw new ArgumentOutOfRangeException(nameof(h));
            var s = Current;
            var k1 = Derivative(s); var k2 = Derivative(s.Plus(k1, h / 2));
            var k3 = Derivative(s.Plus(k2, h / 2)); var k4 = Derivative(s.Plus(k3, h));
            Current = s.Plus(k1, h / 6).Plus(k2, h / 3).Plus(k3, h / 3).Plus(k4, h / 6);
            Time += h;
        }

        public double UpperX => Math.Sin(Current.a);
        public double UpperY => -Math.Cos(Current.a);
        public double TipX => UpperX + Math.Sin(Current.b);
        public double TipY => UpperY - Math.Cos(Current.b);
        public double Energy => Current.wa * Current.wa + Current.wb * Current.wb / 2
            + Current.wa * Current.wb * Math.Cos(Current.a - Current.b) - Gravity * (2 * Math.Cos(Current.a) + Math.Cos(Current.b));
        // Relative to the gravitational energy scale, so an initial energy near zero is well-defined.
        public double EnergyDrift => Math.Abs(Energy - InitialEnergy) / (3 * Gravity);
        public double TipDistance(DoublePendulumModel other)
        { double x = TipX - other.TipX, y = TipY - other.TipY; return Math.Sqrt(x * x + y * y); }
    }
}
