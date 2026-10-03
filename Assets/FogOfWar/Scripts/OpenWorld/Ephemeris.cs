using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    public enum Look { Rocky, Cloudy, Gas, Ice, Earth, Sun }

    // Chebyshev fit to JPL Horizons vectors (Tools/horizons/fit.mjs): ecliptic J2000 km, TDB, relative to the parent.
    public class Cheb
    {
        double jd0, seg; int nSeg, n; double[] c;

        public static Cheb Load(string name)
        {
            var ta = Resources.Load<TextAsset>("Ephem/" + name);
            if (ta == null) return null;
            using var r = new BinaryReader(new MemoryStream(ta.bytes));
            if (new string(r.ReadChars(4)) != "FOWC" || r.ReadUInt32() != 1) { Debug.LogError("Fog of War: bad ephemeris " + name); return null; }
            var ch = new Cheb { jd0 = r.ReadDouble(), seg = r.ReadDouble(), nSeg = (int)r.ReadUInt32(), n = (int)r.ReadUInt32() };
            ch.c = new double[ch.nSeg * 3 * ch.n];
            for (int i = 0; i < ch.c.Length; i++) ch.c[i] = r.ReadDouble();
            return ch;
        }

        public double JdEnd => jd0 + seg * nSeg;
        public double JdStart => jd0;

        public bool Eval(double jd, out D3 p)
        {
            double u = (jd - jd0) / seg;
            int k = (int)Math.Floor(u);
            if (k < 0 || k >= nSeg) { p = D3.Zero; return false; }
            double t = 2 * (u - k) - 1;
            p = new D3(Clenshaw(k * 3 * n, t), Clenshaw((k * 3 + 1) * n, t), Clenshaw((k * 3 + 2) * n, t));
            return true;
        }

        double Clenshaw(int o, double t)
        {
            double b0 = 0, b1 = 0, b2 = 0;
            for (int i = n - 1; i >= 1; i--) { b2 = b1; b1 = b0; b0 = 2 * t * b1 - b2 + c[o + i]; }
            return t * b0 - b1 + c[o];
        }
    }

    // One natural body. Position comes from its Chebyshev ephemeris where one exists and covers the date; otherwise from
    // Keplerian mean elements with secular rates.
    public class Body
    {
        public string Name;
        public Body Parent;                 // null for the Sun
        public double GM, Radius;           // km^3/s^2, km (equatorial for planets, mean for small bodies)
        public double Flattening;           // (Req − Rpolar) / Req
        public double J2;                   // zonal harmonic, referred to Radius
        public Vector3 Shape = Vector3.one; // extra tri-axial factors (irregular bodies without a mesh)
        public Look Look;
        public Color ColA = Color.gray, ColB = Color.gray, Atmo = Color.clear;
        public string Texture;              // Resources path, optional
        public Cheb Cheb;

        // Orientation: IAU pole (RA/Dec J2000) and prime meridian W = W0 + Wd * days (TDB from J2000).
        public double PoleRa = 0, PoleDec = 90, W0, Wd;

        // Mean elements (deg, km): a, e, i, Ω, ϖ = Ω + ω, L = ϖ + M, each with a rate per day, at EpochJd (TDB).
        // Planets: ecliptic J2000. Moons with a Laplace pole: Ω measured in the Laplace plane from its node on the ICRF equator.
        public double A, E, I, Node, Peri, L, dA, dE, dI, dNode, dPeri, dL;
        public double EpochJd = 2451545.0;
        public double LaplaceRa = double.NaN, LaplaceDec;
        public bool Synchronous;            // spins once per orbit, prime meridian facing the parent
        public bool SizeEstimated;          // no measured radius; shown at a nominal size (labelled)
        public bool Barycentric;
        public bool Asteroid;               // a named small body orbiting the Sun            // ephemeris gives the system barycentre; subtract the big moons' pull
        public List<Body> MassiveMoons = new List<Body>();

        // Pole of the reference plane the elements are measured in (Laplace or spin pole; mean orbit pole otherwise).
        public D3 OrbitPole => !double.IsNaN(LaplaceRa) ? Frame.RaDec(LaplaceRa, LaplaceDec)
            : new D3(Math.Sin(Deg(I)) * Math.Sin(Deg(Node)), -Math.Sin(Deg(I)) * Math.Cos(Deg(Node)), Math.Cos(Deg(I)));

        public D3 Pole => Frame.RaDec(PoleRa, PoleDec);

        // Keplerian position relative to the parent, km.
        public D3 Kepler(double jd)
        {
            if (Parent == null) return D3.Zero;
            double d = jd - EpochJd;
            double a = A + dA * d, e = Math.Min(Math.Max(E + dE * d, 0), 0.99);
            double node = Deg(Node + dNode * d), peri = Deg(Peri + dPeri * d), M = Deg(L + dL * d) - peri;
            double w = peri - node, inc = Deg(I + dI * d);
            double Ea = SolveKepler(M, e);
            double xp = a * (Math.Cos(Ea) - e), yp = a * Math.Sqrt(1 - e * e) * Math.Sin(Ea);
            var p = Rotate(xp, yp, w, inc, node);
            if (double.IsNaN(LaplaceRa)) return p;
            var z = Frame.RaDec(LaplaceRa, LaplaceDec);
            var x = D3.Cross(Frame.EqToEcl(new D3(0, 0, 1)), z).Norm;
            var y = D3.Cross(z, x);
            return x * p.x + y * p.y + z * p.z;
        }

        // JPL satellite tables give ω, M and precession periods; convert to this class's ϖ, L and rates.
        public Body JplMoon(double a, double e, double w, double M, double i, double node, double nDegPerDay, double pwYears, double pnodeYears,
            double laplaceRa, double laplaceDec, double epochJd = 2451545.0)
        {
            A = a; E = e; I = i; Node = node; Peri = node + w; L = Peri + M; dL = nDegPerDay;
            dNode = pnodeYears > 0 ? -360 / (pnodeYears * 365.25) : 0;
            dPeri = dNode + (pwYears > 0 ? 360 / (pwYears * 365.25) : 0);
            LaplaceRa = laplaceRa; LaplaceDec = laplaceDec; EpochJd = epochJd;
            return this;
        }

        static double Deg(double d) => d * Math.PI / 180;

        static D3 Rotate(double xp, double yp, double w, double inc, double node)
        {
            double cw = Math.Cos(w), sw = Math.Sin(w), cO = Math.Cos(node), sO = Math.Sin(node), ci = Math.Cos(inc), si = Math.Sin(inc);
            return new D3(
                (cw * cO - sw * sO * ci) * xp + (-sw * cO - cw * sO * ci) * yp,
                (cw * sO + sw * cO * ci) * xp + (-sw * sO + cw * cO * ci) * yp,
                (sw * si) * xp + (cw * si) * yp);
        }

        public static double SolveKepler(double M, double e)
        {
            M = Math.IEEERemainder(M, 2 * Math.PI);
            double Ea = e < 0.8 ? M : Math.PI;
            for (int k = 0; k < 30; k++)
            {
                double f = Ea - e * Math.Sin(Ea) - M;
                double dE = f / (1 - e * Math.Cos(Ea));
                Ea -= dE;
                if (Math.Abs(dE) < 1e-12) break;
            }
            return Ea;
        }

        // Body-fixed axes in the ecliptic frame: X prime meridian, Z north pole.
        public void Axes(double jd, out D3 prime, out D3 east, out D3 pole)
        {
            if (Synchronous && Parent != null)
            {
                pole = OrbitPole;
                var toParent = -Kepler(jd).Norm;
                prime = (toParent - pole * D3.Dot(toParent, pole)).Norm;
                east = D3.Cross(pole, prime);
                return;
            }
            double d = jd - 2451545.0;
            double ra = PoleRa, dec = PoleDec, W = W0 + Wd * d;
            if (Name == "Moon")
            {
                // largest IAU periodic terms (Archinal et al. 2018): the near side is otherwise ~2° off
                double E1 = Deg(125.045 - 0.0529921 * d), E2 = Deg(250.089 - 0.1059842 * d);
                ra += -3.8787 * Math.Sin(E1) - 0.1204 * Math.Sin(E2);
                dec += 1.5419 * Math.Cos(E1) + 0.0239 * Math.Cos(E2);
                W += 3.5610 * Math.Sin(E1) + 0.1208 * Math.Sin(E2);
            }
            if (Name == "Neptune")
            {
                // IAU precession of Neptune's pole (Archinal et al. 2018)
                double N = Deg(357.85 + 52.316 * d / 36525);
                ra += 0.70 * Math.Sin(N); dec -= 0.51 * Math.Cos(N); W -= 0.48 * Math.Sin(N);
            }
            pole = Frame.RaDec(ra, dec);
            // IAU: W is measured from the ascending node of the body's equator on the ICRF (Earth) equator.
            var q = D3.Cross(Frame.EqToEcl(new D3(0, 0, 1)), pole);
            if (q.Sqr < 1e-12) q = Frame.EqToEcl(new D3(1, 0, 0));
            q = q.Norm;
            var qe = D3.Cross(pole, q);
            prime = q * Math.Cos(Deg(W)) + qe * Math.Sin(Deg(W));
            east = D3.Cross(pole, prime);
        }
    }

    [Serializable] class SatMoon
    {
        public string name, parent, code, frame;
        public double epochJd, a, e, i, node, peri, L, n, dNode, dPeri, laplaceRa, laplaceDec, gm, radiusKm;
        public bool calibrated;
    }
    [Serializable] class SatFile { public SatMoon[] moons; }
    [Serializable] class ArtBody { public string name; public float meanRadiusKm; }
    [Serializable] class ArtBodies { public ArtBody[] items; }
    [Serializable] class PlanetJson { public float meanRadiusKm; }
    [Serializable] class OscJson { public double epochJd, e, qAu, tpJd, node, w, i; }

    public class SolarSystem
    {
        public readonly List<Body> Bodies = new List<Body>();
        public Body Sun, Earth, Moon;
        readonly Dictionary<Body, D3> cache = new Dictionary<Body, D3>();
        double cacheJd = double.NaN;
        public double EphemStart = double.MinValue, EphemEnd = double.MaxValue;

        public Body this[string name] => Bodies.Find(b => b.Name == name);

        // True outside the span of the JPL fits, where positions fall back to approximate mean elements.
        public bool Approximate(double jd) => jd < EphemStart || jd >= EphemEnd;

        // Heliocentric position, km.
        public D3 Pos(Body b, double jd)
        {
            if (jd != cacheJd) { cache.Clear(); cacheJd = jd; }
            if (cache.TryGetValue(b, out var p)) return p;
            p = PosNoCache(b, jd);
            cache[b] = p;
            return p;
        }

        // Uncached, for sampling other dates (orbit lines) without evicting the current date.
        public D3 PosAt(Body b, double jd) => PosNoCache(b, jd);

        D3 PosNoCache(Body b, double jd)
        {
            if (b.Parent == null) return D3.Zero;
            var parent = b.Parent == Sun ? D3.Zero : jd == cacheJd ? Pos(b.Parent, jd) : PosNoCache(b.Parent, jd);
            var p = parent + (b.Cheb != null && b.Cheb.Eval(jd, out var local) ? local : b.Kepler(jd));
            if (b.Barycentric && b.MassiveMoons.Count > 0)
            {
                // planet centre = barycentre − Σ GMᵢ rᵢ / (GM + Σ GMᵢ), rᵢ = moon relative to the planet
                var s = D3.Zero; double m = b.GM;
                foreach (var moon in b.MassiveMoons) { s += moon.Kepler(jd) * moon.GM; m += moon.GM; }
                p -= s / m;
            }
            return p;
        }

        public D3 Vel(Body b, double jd)
        {
            const double h = 30.0 / 86400; // 30 s
            return (PosNoCache(b, jd + h) - PosNoCache(b, jd - h)) / 60.0;
        }

        // Acceleration of a test particle at p, km/s^2, in the heliocentric frame: direct pull of every massive body,
        // J2 of the oblate ones, and the indirect term (the Sun itself is accelerated by the planets, and our origin
        // rides with it).
        public D3 Gravity(D3 p, double jd, Body exclude = null)
        {
            var g = D3.Zero;
            foreach (var b in Bodies)
            {
                if (b.GM < 1) continue;
                bool moon = b.Parent != null && b.Parent != Sun;
                // a moon matters only near its planet (its pull on the Sun is already in the planet system's ephemeris)
                if (moon && b != exclude && (p - Pos(b.Parent, jd)).Sqr > (b.A * 50) * (b.A * 50)) continue;
                var bp = Pos(b, jd);
                if (b != Sun && !moon) g -= bp * (b.GM / Math.Pow(bp.Sqr, 1.5)); // indirect
                if (b == exclude) continue;
                var rel = p - bp;
                double r2 = rel.Sqr;
                double rm = Math.Max(Math.Sqrt(r2), b.Radius);
                g -= rel * (b.GM / (rm * rm * rm));
                if (b.J2 > 0 && rm < 40 * b.Radius)
                {
                    var n = b.Pole;
                    double z = D3.Dot(rel, n), r = rm, k = 1.5 * b.J2 * b.GM * b.Radius * b.Radius / Math.Pow(r, 5);
                    g -= (rel * (1 - 5 * z * z / (r * r)) + n * (2 * z)) * k;
                }
            }
            return g;
        }

        // Every known moon of Mars and the giants: JPL SSD mean elements referred to their Laplace planes, with short-period
        // moons recalibrated to Horizons (Tools/satellites/build.mjs). Radii: JPL physical table, then the art pack's
        // shape models and maps; otherwise unknown and shown at 2 km.
        static void LoadSatellites(SolarSystem s)
        {
            var ta = Resources.Load<TextAsset>("Ephem/Satellites");
            if (ta == null) { Debug.LogError("Fog of War: Ephem/Satellites.json missing"); return; }
            var art = new Dictionary<string, float>();
            var ab = Resources.Load<TextAsset>("Models/Bodies/Bodies");
            if (ab != null) foreach (var x in JsonUtility.FromJson<ArtBodies>("{\"items\":" + ab.text + "}").items) art[x.name] = x.meanRadiusKm;
            foreach (var m in JsonUtility.FromJson<SatFile>(ta.text).moons)
            {
                var parent = s[m.parent];
                if (parent == null) continue;
                double r = m.radiusKm;
                if (r <= 0 && art.TryGetValue(m.name, out var ar)) r = ar;
                if (r <= 0)
                {
                    var pj = Resources.Load<TextAsset>("Planets/" + m.name + "/Body");
                    if (pj != null) r = JsonUtility.FromJson<PlanetJson>(pj.text).meanRadiusKm;
                }
                bool icy = m.parent != "Mars" && m.parent != "Jupiter";
                var b = new Body
                {
                    Name = m.name.Replace('_', ' '), Parent = parent, GM = m.gm, Radius = r > 0 ? r : 2, SizeEstimated = r <= 0,
                    Look = Look.Rocky, Texture = "Planets/" + m.name + "/Color", Synchronous = true,
                    ColA = icy ? new Color(0.62f, 0.62f, 0.64f) : new Color(0.42f, 0.39f, 0.36f),
                    ColB = icy ? new Color(0.86f, 0.86f, 0.88f) : new Color(0.62f, 0.58f, 0.53f),
                    A = m.a, E = m.e, I = m.i, Node = m.node, Peri = m.peri, L = m.L, dL = m.n, dNode = m.dNode, dPeri = m.dPeri,
                    EpochJd = m.epochJd, LaplaceRa = m.frame == "ecliptic" ? double.NaN : m.laplaceRa, LaplaceDec = m.laplaceDec,
                };
                if (b.Name == "Phobos") { b.Synchronous = false; b.PoleRa = 317.68; b.PoleDec = 52.90; b.W0 = 35.06; b.Wd = 1128.8445850; }
                if (b.Name == "Deimos") { b.Synchronous = false; b.PoleRa = 316.65; b.PoleDec = 53.52; b.W0 = 79.41; b.Wd = 285.1618970; }
                s.Bodies.Add(b);
                if (parent.Barycentric && b.GM > 1) parent.MassiveMoons.Add(b);
            }
        }

        // Named asteroids: JPL Horizons fits (Tools/horizons/fit.mjs), with osculating elements at the end of the fitted
        // span as the fallback. GM: Ceres, Vesta, Pallas, Hygiea from Dawn and astrometry; radii: IAU / NASA, replaced by
        // the shape model's volume radius where the art pack has one.
        static void LoadAsteroids(SolarSystem s)
        {
            // name, GM km³/s², mean radius km, rotation period h (negative = retrograde), pole RA, Dec, W0
            var list = new (string name, double gm, double r, double rotH, double ra, double dec, double w0)[]
            {
                ("Ceres", 62.6284, 469.7, 9.074170, 291.418, 66.764, 170.65), ("Vesta", 17.2882, 262.7, 5.342128, 309.031, 42.235, 285.39),
                ("Pallas", 13.63, 256.5, 7.8132, 30, -16, 0), ("Hygiea", 5.78, 216.5, 13.82, 0, 90, 0), ("Psyche", 1.53, 111, 4.196, 0, 90, 0),
                ("Ida", 0.0028, 15.7, 4.634, 168.76, -87.12, 274.05), ("Mathilde", 0.0069, 26.4, 417.7, 0, 90, 0), ("Gaspra", 0, 6.1, 7.042, 9.47, 26.7, 83.67),
                ("Eros", 4.46e-4, 8.42, 5.270, 11.35, 17.22, 326.07), ("Itokawa", 0, 0.165, 12.132, 90.53, -66.3, 0),
                ("Ryugu", 0, 0.448, -7.6326, 96.4, -66.4, 0), ("Bennu", 4.89e-9, 0.245, -4.296, 85.65, -60.17, 0),
                // large main-belt bodies and the Trojan Hektor: GM from published mass estimates (±10–30 %), radii
                // replaced by the shape models' volume radii; poles not modelled
                ("Juno", 1.82, 127, 7.21, 0, 90, 0), ("Eunomia", 2.1, 135, 6.083, 0, 90, 0), ("Lutetia", 0.114, 49, 8.168, 52, 12, 0),
                ("Euphrosyne", 1.1, 134, 5.53, 0, 90, 0), ("Cybele", 0.91, 119, 6.07, 0, 90, 0), ("Sylvia", 0.99, 136, 5.18, 0, 90, 0),
                ("Davida", 2.2, 149, 5.13, 0, 90, 0), ("Hektor", 0.53, 113, 6.92, 0, 90, 0), ("Interamnia", 2.5, 166, 8.73, 0, 90, 0),
            };
            foreach (var (name, gm, r, rotH, ra, dec, w0) in list)
            {
                var el = Resources.Load<TextAsset>("Ephem/" + name + ".elements");
                var b = new Body
                {
                    Name = name, Parent = s.Sun, GM = gm, Radius = r, Look = Look.Rocky, Asteroid = true, Texture = "Planets/" + name + "/Color",
                    ColA = new Color(0.33f, 0.31f, 0.29f), ColB = new Color(0.5f, 0.48f, 0.45f),
                    PoleRa = ra, PoleDec = dec, W0 = w0, Wd = 360 / (Math.Abs(rotH) / 24) * Math.Sign(rotH),
                };
                if (el != null)
                {
                    var o = JsonUtility.FromJson<OscJson>(el.text);
                    double a = o.qAu / (1 - o.e) * Frame.AU, n = Math.Sqrt(s.Sun.GM / (a * a * a)) * 86400 * 180 / Math.PI; // °/day
                    b.A = a; b.E = o.e; b.I = o.i; b.Node = o.node; b.Peri = o.node + o.w; b.L = b.Peri + n * (o.epochJd - o.tpJd);
                    b.dL = n; b.EpochJd = o.epochJd;
                }
                s.Bodies.Add(b);
            }
        }

        // Fallback elements: JPL "Keplerian Elements for Approximate Positions of the Major Planets", table 1
        // (1800–2050), a (AU), e, I, L, ϖ, Ω with rates per Julian century. Only used outside the Chebyshev span.
        static Body Planet(string name, double a, double da, double e, double de, double i, double di, double L, double dL,
            double peri, double dperi, double node, double dnode)
        {
            const double C = 36525.0;
            return new Body
            {
                Name = name, A = a * Frame.AU, dA = da * Frame.AU / C, E = e, dE = de / C, I = i, dI = di / C, L = L, dL = dL / C,
                Peri = peri, dPeri = dperi / C, Node = node, dNode = dnode / C,
            };
        }

        public static SolarSystem Build()
        {
            var s = new SolarSystem();
            var sun = s.Sun = new Body { Name = "Sun", GM = 1.32712440018e11, Radius = 695700, Look = Look.Sun, ColA = new Color(1f, 0.93f, 0.8f), PoleRa = 286.13, PoleDec = 63.87, W0 = 84.176, Wd = 14.1844 };
            s.Bodies.Add(sun);

            // Equatorial radius and flattening: NASA planetary fact sheets (1-bar level for the giants). J2: JPL SSD.
            Body P(Body b, double gm, double req, double f, double j2, Look look, Color a, Color bcol, double ra, double dec, double w0, double wd, string tex = null, Color atmo = default)
            {
                b.Parent = sun; b.GM = gm; b.Radius = req; b.Flattening = f; b.J2 = j2; b.Look = look; b.ColA = a; b.ColB = bcol;
                b.PoleRa = ra; b.PoleDec = dec; b.W0 = w0; b.Wd = wd; b.Texture = tex; b.Atmo = atmo;
                s.Bodies.Add(b);
                return b;
            }

            P(Planet("Mercury", 0.38709927, 0.00000037, 0.20563593, 0.00001906, 7.00497902, -0.00594749, 252.25032350, 149472.67411175, 77.45779628, 0.16047689, 48.33076593, -0.12534081),
                22031.8, 2440.5, 0.0009, 0, Look.Rocky, new Color(0.45f, 0.43f, 0.41f), new Color(0.62f, 0.6f, 0.57f), 281.0103, 61.4155, 329.5988, 6.1385108, "Planets/Mercury/Color");
            P(Planet("Venus", 0.72333566, 0.00000390, 0.00677672, -0.00004107, 3.39467605, -0.00078890, 181.97909950, 58517.81538729, 131.60246718, 0.00268329, 76.67984255, -0.27769418),
                324858.6, 6051.8, 0, 0, Look.Cloudy, new Color(0.87f, 0.78f, 0.58f), new Color(0.95f, 0.9f, 0.75f), 272.76, 67.16, 160.20, -1.4813688, "Planets/Venus/Clouds", new Color(1f, 0.85f, 0.55f, 0.6f));
            // Earth's fallback is the Earth–Moon barycentre orbit (close enough when the fit is unavailable).
            s.Earth = P(Planet("Earth", 1.00000261, 0.00000562, 0.01671123, -0.00004392, -0.00001531, -0.01294668, 100.46457166, 35999.37244981, 102.93768193, 0.32327364, 0, 0),
                398600.4, 6378.137, 0.003353, 1.0826e-3, Look.Earth, new Color(0.2f, 0.4f, 0.75f), new Color(0.2f, 0.4f, 0.75f), 0, 90, 190.147, 360.9856235, null, new Color(0.35f, 0.6f, 1f, 0.6f));
            // Moon fallback: mean elements referred to the ecliptic with nodal and apsidal precession (≈ 6,000 km error).
            s.Moon = new Body
            {
                Name = "Moon", Parent = s.Earth, GM = 4902.8, Radius = 1738.1, Flattening = 0.0012, Look = Look.Rocky, ColA = new Color(0.5f, 0.5f, 0.5f), ColB = new Color(0.7f, 0.69f, 0.67f), Texture = "Textures/Moon_Color",
                A = 384400, E = 0.0549, I = 5.145, Node = 125.0445, dNode = -0.0529538083, Peri = 83.3532, dPeri = 0.1114040803, L = 218.3165, dL = 13.17639648,
                PoleRa = 269.9949, PoleDec = 66.5392, W0 = 38.3213, Wd = 13.17635815,
            };
            s.Bodies.Add(s.Moon);
            P(Planet("Mars", 1.52371034, 0.00001847, 0.09339410, 0.00007882, 1.84969142, -0.00813131, -4.55343205, 19140.30268499, -23.94362959, 0.44441088, 49.55953891, -0.29257343),
                42828.37, 3396.2, 0.00589, 1.956e-3, Look.Rocky, new Color(0.6f, 0.3f, 0.16f), new Color(0.82f, 0.55f, 0.36f), 317.681, 52.887, 176.630, 350.89198226, "Textures/Mars_Color", new Color(0.9f, 0.6f, 0.45f, 0.25f));
            // Giants: positions are system barycentres. Saturn rotation is the IAU System III (radio) period.
            P(Planet("Jupiter", 5.20288700, -0.00011607, 0.04838624, -0.00013253, 1.30439695, -0.00183714, 34.39644051, 3034.74612775, 14.72847983, 0.21252668, 100.47390909, 0.20469106),
                126686534, 71492, 0.06487, 1.4697e-2, Look.Gas, new Color(0.78f, 0.66f, 0.52f), new Color(0.95f, 0.9f, 0.82f), 268.057, 64.495, 284.95, 870.536, "Planets/Jupiter/Color");
            P(Planet("Saturn", 9.53667594, -0.00125060, 0.05386179, -0.00050991, 2.48599187, 0.00193609, 49.95424423, 1222.49362201, 92.59887831, -0.41897216, 113.66242448, -0.28867794),
                37931187, 60268, 0.09796, 1.6291e-2, Look.Gas, new Color(0.85f, 0.76f, 0.56f), new Color(0.93f, 0.88f, 0.72f), 40.589, 83.537, 38.90, 810.7939024, "Planets/Saturn/Color");
            // Uranus and Neptune: calibrated true colour (Irwin et al. 2024), not the contrast-stretched Voyager blue.
            P(Planet("Uranus", 19.18916464, -0.00196176, 0.04725744, -0.00004397, 0.77263783, -0.00242939, 313.23810451, 428.48202785, 170.95427630, 0.40805281, 74.01692503, 0.04240589),
                5793939, 25559, 0.02293, 3.510e-3, Look.Ice, new Color(0.66f, 0.83f, 0.86f), new Color(0.72f, 0.88f, 0.9f), 257.311, -15.175, 203.81, -501.1600928, "Planets/Uranus/Color");
            P(Planet("Neptune", 30.06992276, 0.00026291, 0.00859048, 0.00005105, 1.77004347, 0.00035372, -55.12002969, 218.45945325, 44.96476227, -0.32241464, 131.78422574, -0.00508664),
                6836529, 24764, 0.01708, 3.408e-3, Look.Ice, new Color(0.58f, 0.77f, 0.89f), new Color(0.64f, 0.81f, 0.91f), 299.36, 43.46, 249.978, 541.1397757, "Planets/Neptune/Color");

            foreach (var g in new[] { "Jupiter", "Saturn", "Uranus", "Neptune" }) s[g].Barycentric = true;
            LoadSatellites(s);
            LoadAsteroids(s);

            foreach (var b in s.Bodies)
            {
                if (b.Parent != null && b.Parent != sun && b != s.Moon) continue; // moons use mean elements
                b.Cheb = Cheb.Load(b.Name);
                if (b.Cheb != null) { s.EphemStart = Math.Max(s.EphemStart, b.Cheb.JdStart); s.EphemEnd = Math.Min(s.EphemEnd, b.Cheb.JdEnd); }
            }
            return s;
        }
    }
}
