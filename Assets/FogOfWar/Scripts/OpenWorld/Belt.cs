using System;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // A procedural asteroid population on Keplerian orbits, following PHYSICS-REVIEW.md §3:
    // main belt 2.1–3.3 AU with the Kirkwood gaps carved in semi-major axis (4:1, 3:1, 5:2, 7:3, 2:1 with Jupiter),
    // five collisional families, Hungarias, Hildas held in the 3:2 resonance (they trace a triangle in Jupiter's
    // rotating frame), and Trojans librating about L4/L5 (L4 ≈ 1.4× L5). S-types dominate inside 2.5 AU, C-types
    // outside 2.8 AU. Sizes follow N(>D) ∝ D^−2.5 from 1 to 60 km. The real belt has ~1–2 million bodies over 1 km;
    // this is a ~1 % sample, so it is sparser than reality, never denser.
    public class Belt
    {
        public enum Group { Main, Family, Hungaria, Hilda, TrojanL4, TrojanL5 }

        public readonly int Count;
        public readonly double[] A, E, M0, N; // km, -, rad at J2000, rad/day
        public readonly D3[] P, Q;           // orbit-plane basis (ecliptic): r = a(cosE − e)P + a√(1−e²) sinE Q
        public readonly float[] RadiusKm, Albedo;
        public readonly Color[] Colour;
        public readonly byte[] Mesh;
        public readonly Group[] Kind;
        public readonly Quaternion[] Spin0;
        public readonly float[] SpinRate;    // rad/day
        public readonly D3[] Pos;            // heliocentric km, filled by Update
        public readonly int[] AHistogram = new int[240]; // 1.6–5.6 AU in 240 bins, for the panel
        public const double HistMin = 1.6, HistMax = 5.6;

        const double J2000 = 2451545.0;

        public Belt(SolarSystem sys, int mainCount = 16000, int seed = 1)
        {
            var rnd = new System.Random(seed);
            double U() => rnd.NextDouble();
            double Gauss() => Math.Sqrt(-2 * Math.Log(1 - U())) * Math.Cos(2 * Math.PI * U());
            // Rayleigh with the given median
            double Rayleigh(double median) => median / Math.Sqrt(2 * Math.Log(2)) * Math.Sqrt(-2 * Math.Log(1 - U()));

            var jup = sys["Jupiter"];
            double aJ = jup.A / Frame.AU;
            double nJ = jup.dL;                          // deg/day
            double LJ0 = jup.L + jup.dL * (J2000 - jup.EpochJd); // Jupiter mean longitude at J2000, deg

            int hung = mainCount / 25, hilda = mainCount / 13, l4 = mainCount / 8, l5 = l4 * 5 / 7;
            Count = mainCount + hung + hilda + l4 + l5;
            A = new double[Count]; E = new double[Count]; M0 = new double[Count]; N = new double[Count];
            P = new D3[Count]; Q = new D3[Count]; Pos = new D3[Count];
            RadiusKm = new float[Count]; Albedo = new float[Count]; Colour = new Color[Count]; Mesh = new byte[Count];
            Kind = new Group[Count]; Spin0 = new Quaternion[Count]; SpinRate = new float[Count];

            // Kirkwood gaps: a = aJ (q/p)^(2/3)
            double Res(int p, int q) => aJ * Math.Pow((double)q / p, 2.0 / 3.0);
            var gaps = new[] { (Res(4, 1), 0.03), (Res(3, 1), 0.025), (Res(5, 2), 0.02), (Res(7, 3), 0.012), (Res(2, 1), 0.04) };
            double MainDensity(double a)
            {
                if (a < 2.1 || a > 3.3) return 0;
                double d = Math.Exp(-Math.Pow((a - 2.75) / 0.42, 2)); // broad hump
                foreach (var (ag, w) in gaps) d *= 1 - 0.97 * Math.Exp(-Math.Pow((a - ag) / w, 2));
                return d;
            }
            var families = new (double a, double e, double i)[] { (2.20, 0.15, 5.5), (2.36, 0.10, 6.5), (2.87, 0.05, 2.0), (3.02, 0.08, 10.0), (3.13, 0.15, 1.5) };

            for (int k = 0; k < Count; k++)
            {
                double a, e, inc, node = U() * 360, w = U() * 360, L = U() * 360;
                Group g;
                if (k < mainCount)
                {
                    if (U() < 0.15)
                    {
                        var f = families[rnd.Next(families.Length)];
                        a = f.a + Gauss() * 0.025; e = Math.Max(0, f.e + Gauss() * 0.012); inc = Math.Max(0, f.i + Gauss() * 0.4);
                        g = Group.Family;
                        if (MainDensity(a) < 0.05) { k--; continue; } // families respect the gaps too
                    }
                    else
                    {
                        do a = 2.1 + U() * 1.2; while (U() > MainDensity(a));
                        e = Math.Min(Rayleigh(0.14), 0.4); inc = Math.Min(Rayleigh(8), 35);
                        g = Group.Main;
                    }
                }
                else if (k < mainCount + hung)
                {
                    a = 1.78 + U() * 0.22; e = U() * 0.1; inc = 16 + U() * 19; g = Group.Hungaria;
                }
                else if (k < mainCount + hung + hilda)
                {
                    // 3:2 resonance: n = 1.5 nJ and the resonant angle σ = 3λJ − 2λ − ϖ librates about 0, so each Hilda
                    // is at perihelion when Jupiter passes its perihelion longitude: aphelia avoid Jupiter.
                    a = Res(3, 2); e = 0.07 + U() * 0.2; inc = Math.Min(Rayleigh(8), 20); g = Group.Hilda;
                    double varpi = U() * 360, sigma = Gauss() * 25;
                    L = (3 * LJ0 - varpi - sigma) / 2 + rnd.Next(2) * 180; // λ is defined mod 180° here: both branches resonate
                    w = varpi - node;
                }
                else
                {
                    bool leading = k < mainCount + hung + hilda + l4;
                    a = aJ * (1 + Gauss() * 0.01); e = U() * 0.15; inc = Math.Min(Rayleigh(10), 40);
                    g = leading ? Group.TrojanL4 : Group.TrojanL5;
                    L = LJ0 + (leading ? 60 : -60) + Gauss() * 15;
                }

                A[k] = a * Frame.AU; E[k] = e; Kind[k] = g;
                double incR = inc * Math.PI / 180, nodeR = node * Math.PI / 180, wR = w * Math.PI / 180;
                double cO = Math.Cos(nodeR), sO = Math.Sin(nodeR), ci = Math.Cos(incR), si = Math.Sin(incR), cw = Math.Cos(wR), sw = Math.Sin(wR);
                P[k] = new D3(cw * cO - sw * sO * ci, cw * sO + sw * cO * ci, sw * si);
                Q[k] = new D3(-sw * cO - cw * sO * ci, -sw * sO + cw * cO * ci, cw * si);
                // Hildas and Trojans share Jupiter's clock; the rest follow Kepler's third law
                double nDeg = g == Group.Hilda ? 1.5 * nJ : g >= Group.TrojanL4 ? nJ : Math.Sqrt(sys.Sun.GM / Math.Pow(A[k], 3)) * 86400 * 180 / Math.PI;
                N[k] = nDeg * Math.PI / 180;
                double varpiDeg = node + w;
                M0[k] = (L - varpiDeg) * Math.PI / 180;
                AHistogram[Math.Clamp((int)((a - HistMin) / (HistMax - HistMin) * AHistogram.Length), 0, AHistogram.Length - 1)]++;

                // composition by zone: S (stony, albedo ≈ 0.25) inside 2.5 AU, C (dark, ≈ 0.06) outside 2.8, mixed between;
                // Trojans and Hildas are dark D/P types
                double pS = g >= Group.Hilda ? 0.02 : a < 2.5 ? 0.8 : a > 2.8 ? 0.15 : 0.8 - (a - 2.5) / 0.3 * 0.65;
                bool stony = U() < pS, metal = !stony && U() < 0.05;
                Albedo[k] = metal ? 0.15f : stony ? 0.25f : 0.06f;
                Colour[k] = metal ? new Color(0.62f, 0.6f, 0.58f) : stony ? new Color(0.72f, 0.62f, 0.5f) : new Color(0.42f, 0.4f, 0.38f);
                // N(>D) ∝ D^−2.5 between 1 and 60 km
                double D = Math.Pow(1 - U() * (1 - Math.Pow(60, -2.5)), -1 / 2.5);
                RadiusKm[k] = (float)(D / 2);
                Mesh[k] = (byte)rnd.Next(32);
                Spin0[k] = Quaternion.Euler((float)(U() * 360), (float)(U() * 360), (float)(U() * 360));
                SpinRate[k] = (float)(2 * Math.PI / (2 + U() * 20) * 24); // 2–22 h periods
            }
        }

        // Heliocentric positions at jd (TDB).
        public void Update(double jd)
        {
            double d = jd - J2000;
            // independent per rock: spread over the cores in chunks
            System.Threading.Tasks.Parallel.For(0, (Count + 1023) / 1024, chunk =>
            {
                int end = Math.Min(Count, (chunk + 1) * 1024);
                for (int k = chunk * 1024; k < end; k++)
                {
                    double e = E[k], M = M0[k] + N[k] * d;
                    double Ea = Body.SolveKepler(M, e);
                    double x = A[k] * (Math.Cos(Ea) - e), y = A[k] * Math.Sqrt(1 - e * e) * Math.Sin(Ea);
                    Pos[k] = P[k] * x + Q[k] * y;
                }
            });
        }

        // a-histogram (the only place the Kirkwood gaps really show) as a small texture for the panel.
        public Texture2D HistogramTexture(int w = 480, int h = 90)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            var px = new Color32[w * h];
            int max = 1; foreach (var c in AHistogram) max = Math.Max(max, c);
            for (int x = 0; x < w; x++)
            {
                int bin = x * AHistogram.Length / w;
                int top = (int)((h - 1) * Math.Sqrt(AHistogram[bin] / (double)max)); // √ scale so the Hildas and Trojans show
                for (int y = 0; y < h; y++)
                    px[y * w + x] = y <= top ? new Color32(0x69, 0xD2, 0xFF, 0xD0) : new Color32(0, 0, 0, 0x30);
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }
    }
}
