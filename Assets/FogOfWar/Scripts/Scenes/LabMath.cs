using System;

namespace FogOfWar
{
    public struct LabVector
    {
        public double x, y, z;
        public LabVector(double x, double y, double z = 0) { this.x = x; this.y = y; this.z = z; }
        public double Squared => x*x + y*y + z*z;
        public double Length => Math.Sqrt(Squared);
        public LabVector Unit => Length > 1e-12 ? this / Length : new LabVector();
        public static LabVector operator +(LabVector a, LabVector b) => new LabVector(a.x+b.x, a.y+b.y, a.z+b.z);
        public static LabVector operator -(LabVector a, LabVector b) => new LabVector(a.x-b.x, a.y-b.y, a.z-b.z);
        public static LabVector operator *(LabVector a, double b) => new LabVector(a.x*b, a.y*b, a.z*b);
        public static LabVector operator /(LabVector a, double b) => a * (1/b);
    }

    public static class LabMath
    {
        public static double Range(double x, double min, double max, string name)
        {
            if (double.IsNaN(x) || x < min || x > max) throw new ArgumentOutOfRangeException(name);
            return x;
        }
        public static double Wrap(double x, double half) => x - 2*half*Math.Floor((x+half)/(2*half));
        public static LabVector Rk4(LabVector p, double h, Func<LabVector, LabVector> f)
        {
            var a=f(p); var b=f(p+a*(h/2)); var c=f(p+b*(h/2)); var d=f(p+c*h);
            return p+(a+b*2+c*2+d)*(h/6);
        }
    }
}
