using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Discovery: a codex of real facts that unlocks as you visit and scan bodies, the sale of first-scan data, and
    // achievements. Plain data and rules; Career owns it and saves it.
    public class Codex
    {
        [Serializable] class FactJson { public string name; public string[] facts; }
        [Serializable] class FactFile { public FactJson[] items; }

        static Dictionary<string, string[]> facts;
        public static string[] Facts(string body)
        {
            if (facts == null)
            {
                facts = new Dictionary<string, string[]>();
                var ta = Resources.Load<TextAsset>("Codex/Facts");
                if (ta != null) foreach (var f in JsonUtility.FromJson<FactFile>(ta.text).items) facts[f.name] = f.facts;
            }
            return facts.TryGetValue(body, out var r) ? r : Array.Empty<string>();
        }

        public readonly HashSet<string> Visited = new HashSet<string>();
        public readonly HashSet<string> Scanned = new HashSet<string>();
        public readonly HashSet<string> Achieved = new HashSet<string>();
        public readonly HashSet<string> DockedAt = new HashSet<string>();
        public int SensorLevel;                      // 0 stock, 1 upgraded: scan range ×2, dwell ÷2
        public const double SensorCost = 30000;
        public double ScanProgress;                  // seconds on the current scan target
        public string ScanTarget;

        public double SensorRange => SensorLevel > 0 ? 2 : 1;
        public double ScanSeconds => Career.DwellSeconds / (SensorLevel > 0 ? 2 : 1);
        public double ScanRange(Body b) => Career.ScanRange(b) * SensorRange;

        // Passing close: within a few parking distances, or ten radii for the small ones.
        public static double VisitRange(Body b) => Math.Max(Ship.Standoff(b) * 4, b.Radius * 10);

        // Data from far out sells for more: 400 cr plus 600 cr per AU from the Sun; small bodies are less studied.
        public static double DataValue(SolarSystem sys, Body b, double jd)
        {
            double au = sys.Pos(b, jd).Mag / Frame.AU;
            return Math.Round((400 + 600 * au) * (b.Radius < 100 ? 1.5 : 1) / 10) * 10;
        }

        // ------------------------------------------------------------ the entry text

        // Numbers from the game's own body data, so they always match what you fly around.
        public static List<string> Data(SolarSystem sys, Body b, double jd)
        {
            var l = new List<string>();
            l.Add($"Mean radius {Fmt.Num(b.Radius, b.Radius < 10 ? 2 : 0)} km" + (b.SizeEstimated ? " (estimated)" : ""));
            if (b.GM > 1e-6)
            {
                double g = b.GM / (b.Radius * b.Radius) * 1000; // m/s²
                l.Add($"Surface gravity {Fmt.Num(g, g < 1 ? 3 : 2)} m/s² ({Fmt.Num(g / 9.80665, 3)} g); escape velocity {Fmt.Num(Math.Sqrt(2 * b.GM / b.Radius), 2)} km/s");
            }
            if (b.Parent != null && b.A > 0)
            {
                double T = 2 * Math.PI * Math.Sqrt(b.A * b.A * b.A / (b.Parent.GM + b.GM)) / 86400;
                string per = T > 700 ? Fmt.Num(T / 365.25, 2) + " years" : T < 2 ? Fmt.Num(T * 24, 1) + " h" : Fmt.Num(T, 2) + " days";
                string a = b.Parent.Parent == null ? Fmt.Num(b.A / Frame.AU, 3) + " AU" : Fmt.Num(b.A, 0) + " km";
                l.Add($"Orbits {b.Parent.Name} every {per}, {a} out" + (b.E > 0.1 ? $", eccentricity {Fmt.Num(b.E, 2)}" : ""));
            }
            if (b.Parent != null) l.Add($"Now {Fmt.Num(sys.Pos(b, jd).Mag / Frame.AU, 3)} AU from the Sun");
            return l;
        }

        // ------------------------------------------------------------ achievements

        public struct Feat { public string Id, Title, How; }
        public static readonly Feat[] Feats =
        {
            new Feat { Id = "first_job", Title = "Paid in full", How = "Finish a contract." },
            new Feat { Id = "coast", Title = "Burn, coast, burn", How = "Cruise on a coast plan." },
            new Feat { Id = "belter", Title = "Belter", How = "Dock at Ceres or Vesta." },
            new Feat { Id = "jovian", Title = "Jovian", How = "Pass close to Jupiter." },
            new Feat { Id = "ringed", Title = "Ring runner", How = "Pass close to Saturn." },
            new Feat { Id = "ice_giants", Title = "Ice giants", How = "Pass close to Uranus and Neptune." },
            new Feat { Id = "planets", Title = "Grand tour", How = "Pass close to all eight planets." },
            new Feat { Id = "ports", Title = "Every port", How = "Dock at all nine ports." },
            new Feat { Id = "scan10", Title = "Surveyor", How = "Scan 10 bodies." },
            new Feat { Id = "scan50", Title = "Cartographer", How = "Scan 50 bodies." },
            new Feat { Id = "rich", Title = "Six figures", How = "Hold 100,000 cr." },
            new Feat { Id = "drive3", Title = "Hard burn", How = "Fit the 3 g drive." },
            new Feat { Id = "landed", Title = "Touchdown", How = "Touch down on any body." },
            new Feat { Id = "fumes", Title = "Running on fumes", How = "Dock with less than 5 t of reaction mass." },
            new Feat { Id = "icarus", Title = "Icarus", How = "Go within 0.3 AU of the Sun." },
            new Feat { Id = "far", Title = "Edge of the map", How = "Go 30 AU from the Sun." },
        };

        static readonly string[] Planets = { "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune" };

        public bool Has(string id) => Achieved.Contains(id);

        // Returns newly earned titles.
        public List<string> CheckFeats(Career c, Ship ship)
        {
            var got = new List<string>();
            void Earn(string id, bool cond) { if (cond && Achieved.Add(id)) got.Add(Array.Find(Feats, f => f.Id == id).Title); }
            double au = ship.Pos.Mag / Frame.AU;
            Earn("first_job", c.Done >= 1);
            Earn("coast", ship.Phase == "cruising");
            Earn("belter", DockedAt.Contains("Ceres") || DockedAt.Contains("Vesta"));
            Earn("jovian", Visited.Contains("Jupiter"));
            Earn("ringed", Visited.Contains("Saturn"));
            Earn("ice_giants", Visited.Contains("Uranus") && Visited.Contains("Neptune"));
            Earn("planets", Array.TrueForAll(Planets, Visited.Contains));
            Earn("ports", DockedAt.Count >= Career.Ports.Length);
            Earn("scan10", Scanned.Count >= 10);
            Earn("scan50", Scanned.Count >= 50);
            Earn("rich", c.Credits >= 100000);
            Earn("drive3", c.DriveLevel >= 1);
            Earn("landed", ship.Landed != null && ship.Crashed == null); // QA-055: a crash isn't a touchdown
            Earn("fumes", c.Docked != null && ship.Fuel < 5);
            Earn("icarus", au < 0.3);
            Earn("far", au > 30);
            return got;
        }
    }
}
