using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    public enum Look { Rocky, Cloudy, Gas, Ice, Earth, Sun }

    // One natural body. Orbits are osculating Keplerian elements with secular rates, evaluated at any Julian date.
    public class Body
    {
        public string Name;
        public Body Parent;                 // null for the Sun
        public double GM, Radius;           // km^3/s^2, km (mean)
        public Vector3 Shape = Vector3.one; // tri-axial radii relative to Radius, for irregular moons
        public Look Look;
        public Color ColA = Color.gray, ColB = Color.gray, Atmo = Color.clear;
        public string Texture;              // Resources path, optional

        // Orientation: IAU pole (RA/Dec J2000) and prime meridian W = W0 + Wd * days.
        public double PoleRa = 0, PoleDec = 90, W0, Wd;

        // Elements (deg, km): a, e, i, Ω, ϖ (longitude of periapsis), L (mean longitude), each with a rate per day.
        // Planets: ecliptic J2000. Moons with EquatorialFrame: relative to the parent's equator.
        public double A, E, I, Node, Peri, L, dNode, dPeri, dL;
        public bool EquatorialFrame;
        public bool PhaseApprox;            // phase isn't from an ephemeris (flagged in the UI)

        public D3 Pole => Frame.RaDec(PoleRa, PoleDec);

        // Position relative to the parent, km.
        public D3 Local(double jd)
        {
            if (Parent == null) return D3.Zero;
            double d = jd - 2451545.0;
            double node = Deg(Node + dNode * d), peri = Deg(Peri + dPeri * d), M = Deg(L + dL * d) - peri;
            double w = peri - node, inc = Deg(I);
            double Ea = SolveKepler(M, E);
            double xp = A * (Math.Cos(Ea) - E), yp = A * Math.Sqrt(1 - E * E) * Math.Sin(Ea);
            var p = Rotate(xp, yp, w, inc, node);
            if (!EquatorialFrame) return p;
            // Elements were measured in the parent's equatorial plane: x toward its equator's ascending node on the ecliptic.
            var z = Parent.Pole;
            var x = D3.Cross(Frame.EclNorth, z).Norm;
            var y = D3.Cross(z, x);
            return x * p.x + y * p.y + z * p.z;
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
            pole = Pole;
            // IAU: W is measured from the ascending node of the body's equator on the ICRF (Earth) equator.
            var eqNorth = Frame.EqToEcl(new D3(0, 0, 1));
            var q = D3.Cross(eqNorth, pole);
            if (q.Sqr < 1e-12) q = Frame.EqToEcl(new D3(1, 0, 0));
            q = q.Norm;
            var qe = D3.Cross(pole, q);
            double W = Deg(W0 + Wd * (jd - 2451545.0));
            prime = q * Math.Cos(W) + qe * Math.Sin(W);
            east = D3.Cross(pole, prime);
        }
    }

    public class SolarSystem
    {
        public readonly List<Body> Bodies = new List<Body>();
        public Body Sun, Earth, Moon, Emb;
        readonly Dictionary<Body, D3> cache = new Dictionary<Body, D3>();
        double cacheJd = double.NaN;

        public Body this[string name] => Bodies.Find(b => b.Name == name);

        // Heliocentric position, km.
        public D3 Pos(Body b, double jd)
        {
            if (jd != cacheJd) { cache.Clear(); cacheJd = jd; }
            if (cache.TryGetValue(b, out var p)) return p;
            if (b.Parent == null) p = D3.Zero;
            else if (b == Earth) p = Pos(Emb, jd) - Moon.Local(jd) * (Moon.GM / (Earth.GM + Moon.GM));
            else if (b == Moon) p = Pos(Emb, jd) + b.Local(jd) * (Earth.GM / (Earth.GM + Moon.GM));
            else p = Pos(b.Parent, jd) + b.Local(jd);
            cache[b] = p;
            return p;
        }

        public D3 Vel(Body b, double jd)
        {
            const double h = 30.0 / 86400; // 30 s
            var a = PosNoCache(b, jd - h); var c = PosNoCache(b, jd + h);
            return (c - a) / 60.0;
        }

        D3 PosNoCache(Body b, double jd)
        {
            if (b.Parent == null) return D3.Zero;
            if (b == Earth) return PosNoCache(Emb, jd) - Moon.Local(jd) * (Moon.GM / (Earth.GM + Moon.GM));
            if (b == Moon) return PosNoCache(Emb, jd) + b.Local(jd) * (Earth.GM / (Earth.GM + Moon.GM));
            return PosNoCache(b.Parent, jd) + b.Local(jd);
        }

        // Gravitational acceleration at p, km/s^2, from every body with mass that matters.
        public D3 Gravity(D3 p, double jd, Body exclude = null)
        {
            var g = D3.Zero;
            foreach (var b in Bodies)
            {
                if (b == exclude || b.GM < 1 || b == Emb) continue;
                var r = Pos(b, jd) - p;
                double r2 = r.Sqr;
                double rm = Math.Max(Math.Sqrt(r2), b.Radius);
                g += r * (b.GM / (rm * rm * rm));
            }
            return g;
        }

        // Planet elements: JPL "Keplerian Elements for Approximate Positions of the Major Planets", table 1 (1800–2050),
        // a (AU), e, I, L, ϖ, Ω with rates per Julian century.
        static Body Planet(string name, double a, double da, double e, double de, double i, double di, double L, double dL,
            double peri, double dperi, double node, double dnode)
        {
            const double C = 36525.0;
            return new Body
            {
                Name = name, A = a * Frame.AU, E = e, I = i, L = L, dL = dL / C, Peri = peri, dPeri = dperi / C, Node = node, dNode = dnode / C,
            };
        }

        public static SolarSystem Build()
        {
            var s = new SolarSystem();
            var sun = s.Sun = new Body { Name = "Sun", GM = 1.32712440018e11, Radius = 695700, Look = Look.Sun, ColA = new Color(1f, 0.93f, 0.8f), PoleRa = 286.13, PoleDec = 63.87, W0 = 84.176, Wd = 14.1844 };
            s.Bodies.Add(sun);

            Body P(Body b, double gm, double r, Look look, Color a, Color bcol, double ra, double dec, double w0, double wd, string tex = null, Color atmo = default)
            {
                b.Parent = sun; b.GM = gm; b.Radius = r; b.Look = look; b.ColA = a; b.ColB = bcol; b.PoleRa = ra; b.PoleDec = dec; b.W0 = w0; b.Wd = wd; b.Texture = tex; b.Atmo = atmo;
                s.Bodies.Add(b);
                return b;
            }

            P(Planet("Mercury", 0.38709927, 0.00000037, 0.20563593, 0.00001906, 7.00497902, -0.00594749, 252.25032350, 149472.67411175, 77.45779628, 0.16047689, 48.33076593, -0.12534081),
                22031.8, 2439.7, Look.Rocky, new Color(0.45f, 0.43f, 0.41f), new Color(0.62f, 0.6f, 0.57f), 281.0103, 61.4155, 329.5988, 6.1385108, "Planets/Mercury/Color");
            P(Planet("Venus", 0.72333566, 0.00000390, 0.00677672, -0.00004107, 3.39467605, -0.00078890, 181.97909950, 58517.81538729, 131.60246718, 0.00268329, 76.67984255, -0.27769418),
                324858.6, 6051.8, Look.Cloudy, new Color(0.87f, 0.78f, 0.58f), new Color(0.95f, 0.9f, 0.75f), 272.76, 67.16, 160.20, -1.4813688, "Planets/Venus/Color", new Color(1f, 0.85f, 0.55f, 0.6f));
            s.Emb = Planet("Earth–Moon barycentre", 1.00000261, 0.00000562, 0.01671123, -0.00004392, -0.00001531, -0.01294668, 100.46457166, 35999.37244981, 102.93768193, 0.32327364, 0, 0);
            s.Emb.Parent = sun; s.Bodies.Add(s.Emb);
            s.Earth = new Body { Name = "Earth", Parent = sun, GM = 398600.4, Radius = 6371.0, Look = Look.Earth, ColA = new Color(0.2f, 0.4f, 0.75f), Atmo = new Color(0.35f, 0.6f, 1f, 0.6f), PoleRa = 0, PoleDec = 90, W0 = 190.147, Wd = 360.9856235 };
            s.Bodies.Add(s.Earth);
            // Moon: mean elements referred to the ecliptic, with the 18.6-year nodal and 8.85-year apsidal precession.
            s.Moon = new Body
            {
                Name = "Moon", Parent = s.Earth, GM = 4902.8, Radius = 1737.4, Look = Look.Rocky, ColA = new Color(0.5f, 0.5f, 0.5f), ColB = new Color(0.7f, 0.69f, 0.67f), Texture = "Textures/Moon_Color",
                A = 384400, E = 0.0549, I = 5.145, Node = 125.0445, dNode = -0.0529538083, Peri = 83.3532, dPeri = 0.1114040803, L = 218.3165, dL = 13.17639648,
                PoleRa = 269.9949, PoleDec = 66.5392, W0 = 38.3213, Wd = 13.17635815,
            };
            s.Bodies.Add(s.Moon);
            var mars = P(Planet("Mars", 1.52371034, 0.00001847, 0.09339410, 0.00007882, 1.84969142, -0.00813131, -4.55343205, 19140.30268499, -23.94362959, 0.44441088, 49.55953891, -0.29257343),
                42828.37, 3389.5, Look.Rocky, new Color(0.6f, 0.3f, 0.16f), new Color(0.82f, 0.55f, 0.36f), 317.681, 52.887, 176.630, 350.89198226, "Textures/Mars_Color", new Color(0.9f, 0.6f, 0.45f, 0.25f));
            P(Planet("Jupiter", 5.20288700, -0.00011607, 0.04838624, -0.00013253, 1.30439695, -0.00183714, 34.39644051, 3034.74612775, 14.72847983, 0.21252668, 100.47390909, 0.20469106),
                126686534, 69911, Look.Gas, new Color(0.78f, 0.66f, 0.52f), new Color(0.95f, 0.9f, 0.82f), 268.057, 64.495, 284.95, 870.536, "Planets/Jupiter/Color");
            P(Planet("Saturn", 9.53667594, -0.00125060, 0.05386179, -0.00050991, 2.48599187, 0.00193609, 49.95424423, 1222.49362201, 92.59887831, -0.41897216, 113.66242448, -0.28867794),
                37931187, 58232, Look.Gas, new Color(0.85f, 0.76f, 0.56f), new Color(0.93f, 0.88f, 0.72f), 40.589, 83.537, 38.90, 810.7939024, "Planets/Saturn/Color");
            P(Planet("Uranus", 19.18916464, -0.00196176, 0.04725744, -0.00004397, 0.77263783, -0.00242939, 313.23810451, 428.48202785, 170.95427630, 0.40805281, 74.01692503, 0.04240589),
                5793939, 25362, Look.Ice, new Color(0.62f, 0.83f, 0.86f), new Color(0.7f, 0.88f, 0.9f), 257.311, -15.175, 203.81, -501.1600928, "Planets/Uranus/Color");
            P(Planet("Neptune", 30.06992276, 0.00026291, 0.00859048, 0.00005105, 1.77004347, 0.00035372, -55.12002969, 218.45945325, 44.96476227, -0.32241464, 131.78422574, -0.00508664),
                6836529, 24622, Look.Ice, new Color(0.25f, 0.4f, 0.85f), new Color(0.4f, 0.55f, 0.95f), 299.36, 43.46, 249.978, 541.1397757, "Planets/Neptune/Color");

            // Martian moons: mean elements relative to Mars' equator (JPL). Phase at epoch is approximate.
            s.Bodies.Add(new Body
            {
                Name = "Phobos", Parent = mars, GM = 7.087e-4, Radius = 11.08, Shape = new Vector3(1.17f, 0.82f, 1.03f), Look = Look.Rocky, Texture = null,
                ColA = new Color(0.28f, 0.25f, 0.23f), ColB = new Color(0.4f, 0.36f, 0.32f), A = 9376, E = 0.0151, I = 1.075, Node = 164.9, Peri = 150.1, L = 91.0, dL = 360 / 0.31891023,
                PoleRa = 317.68, PoleDec = 52.90, W0 = 35.06, Wd = 1128.8445850, EquatorialFrame = true, PhaseApprox = true,
            });
            s.Bodies.Add(new Body
            {
                Name = "Deimos", Parent = mars, GM = 9.6e-5, Radius = 6.2, Shape = new Vector3(1.21f, 0.98f, 0.84f), Look = Look.Rocky,
                ColA = new Color(0.33f, 0.3f, 0.27f), ColB = new Color(0.46f, 0.42f, 0.37f), A = 23463.2, E = 0.00033, I = 1.788, Node = 339.6, Peri = 290.5, L = 296.2, dL = 360 / 1.263,
                PoleRa = 316.65, PoleDec = 53.52, W0 = 79.41, Wd = 285.1618970, EquatorialFrame = true, PhaseApprox = true,
            });
            return s;
        }
    }
}
