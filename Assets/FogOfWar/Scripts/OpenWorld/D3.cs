using System;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Double-precision vector for heliocentric physics. Frame: ecliptic J2000, right-handed, km and km/s.
    public struct D3
    {
        public double x, y, z;
        public D3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static readonly D3 Zero = new D3(0, 0, 0);
        public static D3 operator +(D3 a, D3 b) => new D3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static D3 operator -(D3 a, D3 b) => new D3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static D3 operator -(D3 a) => new D3(-a.x, -a.y, -a.z);
        public static D3 operator *(D3 a, double k) => new D3(a.x * k, a.y * k, a.z * k);
        public static D3 operator *(double k, D3 a) => new D3(a.x * k, a.y * k, a.z * k);
        public static D3 operator /(D3 a, double k) => new D3(a.x / k, a.y / k, a.z / k);
        public double Mag => Math.Sqrt(x * x + y * y + z * z);
        public double Sqr => x * x + y * y + z * z;
        public D3 Norm { get { double m = Mag; return m > 0 ? this / m : new D3(1, 0, 0); } }
        public static double Dot(D3 a, D3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static D3 Cross(D3 a, D3 b) => new D3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public override string ToString() => $"({x:G4}, {y:G4}, {z:G4})";
    }

    public static class Frame
    {
        public const double AU = 1.495978707e8;   // km
        public const double Obliquity = 23.4392911 * Math.PI / 180;
        public static readonly D3 EclNorth = new D3(0, 0, 1);

        // Right-handed ecliptic to Unity's left-handed render frame: ecliptic north becomes +Y. Swapping y and z is the
        // handedness change, so prograde orbits still turn anticlockwise seen from above.
        public static Vector3 U(D3 v) => new Vector3((float)v.x, (float)v.z, (float)v.y);
        public static D3 FromU(Vector3 v) => new D3(v.x, v.z, v.y);

        // Equatorial J2000 (RA/Dec frame) to ecliptic J2000.
        public static D3 EqToEcl(D3 v)
        {
            double c = Math.Cos(Obliquity), s = Math.Sin(Obliquity);
            return new D3(v.x, v.y * c + v.z * s, -v.y * s + v.z * c);
        }

        public static D3 RaDec(double raDeg, double decDeg)
        {
            double ra = raDeg * Math.PI / 180, de = decDeg * Math.PI / 180;
            return EqToEcl(new D3(Math.Cos(de) * Math.Cos(ra), Math.Cos(de) * Math.Sin(ra), Math.Sin(de)));
        }

        // Rotation that takes the star catalogue (built in an equatorial Unity frame: +Y celestial pole, RA 0h at +X,
        // RA 6h at +Z) into the ecliptic render frame.
        public static Quaternion StarsToRender =>
            Quaternion.LookRotation(U(EqToEcl(new D3(0, 1, 0))), U(EqToEcl(new D3(0, 0, 1))));

        public static double JulianDate(DateTime utc) => utc.ToOADate() + 2415018.5;
        public static DateTime FromJulian(double jd) => DateTime.FromOADate(jd - 2415018.5);
    }
}
