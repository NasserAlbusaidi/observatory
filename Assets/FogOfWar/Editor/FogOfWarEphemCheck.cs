using System;
using FogOfWar.OpenWorld;
using UnityEditor;
using UnityEngine;

namespace FogOfWar.EditorTools
{
    // Prints parent-relative positions at a fixed TDB epoch (compare with Horizons) and each planet's obliquity
    // (angle between its IAU pole and its orbit normal), which must match the fact-sheet values to ±0.1°.
    public static class FogOfWarEphemCheck
    {
        [MenuItem("Fog of War/4. Ephemeris check")]
        public static void Run()
        {
            var sys = SolarSystem.Build();
            const double jd = 2461316.5; // 2026-10-03 00:00 TDB
            foreach (var b in sys.Bodies)
            {
                if (b.Parent == null) continue;
                var rel = sys.Pos(b, jd) - sys.Pos(b.Parent, jd);
                var v = sys.Vel(b, jd) - sys.Vel(b.Parent, jd);
                var h = D3.Cross(rel, v).Norm;
                b.Axes(jd, out _, out _, out var pole);
                double obl = Math.Acos(Math.Max(-1, Math.Min(1, D3.Dot(h, pole)))) * 180 / Math.PI;
                Debug.Log($"EPHEM {b.Name} {rel.x:F3} {rel.y:F3} {rel.z:F3} src={(b.Cheb != null ? "cheb" : "kepler")} obliquity={obl:F2}");
            }
        }
    }
}
