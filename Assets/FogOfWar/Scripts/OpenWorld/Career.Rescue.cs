using System;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Rescue jobs: a ship with a dead drive coasts on its own two-body orbit. Find it, match its velocity (within
    // RescueKm and RescueKms), and hold there while the crew crosses over. The derelict is a massless Body on a
    // Kepler orbit, so the nav computer can target it, match velocity and hold station like any moon.
    public partial class Career
    {
        public SolarSystem Sys;                    // set by the scene: derelicts are added to and removed from it
        public const double RescueKm = 500, RescueKms = 0.05, TransferSeconds = 1800;
        const string DerelictPrefix = "Derelict ";

        public static bool IsDerelict(Body b) => b != null && b.Name.StartsWith(DerelictPrefix);
        // a ship rather than a natural body: a derelict or a ship flying its own course
        public static bool IsCraft(Body b) => IsDerelict(b) || b?.Path != null;

        // The body a job sends you to: the survey target, the derelict (made up for a job still on the board), or the port.
        public Body TargetOf(Contract c) =>
            c.Kind == JobKind.Survey ? Sys[c.To] : c.Kind == JobKind.Rescue || c.Kind == JobKind.Intercept ? Sys[c.To] ?? Preview(c) : Sys[PortAt(c.To)?.Body ?? ""];

        // a job still on the board has no body in the system yet: build one once for the panel to measure
        readonly System.Collections.Generic.Dictionary<int, Body> previews = new System.Collections.Generic.Dictionary<int, Body>();
        Body Preview(Contract c)
        {
            if (!previews.TryGetValue(c.Id, out var b)) previews[c.Id] = b = c.Kind == JobKind.Rescue ? MakeBody(c, Sys) : MakeRunner(c, Sys);
            return b;
        }

        static readonly string[] Hulls =
        {
            "ice hauler Amundsen", "prospector Second Wind", "survey cutter Kepler", "tender Okonkwo", "freighter Mendel",
            "ore barge Hypatia", "courier Swift Return", "water tug Tiamat", "mining skiff Pemba", "science sloop Leavitt",
        };

        internal Contract Rescue(System.Random rng, SolarSystem sys, double jd)
        {
            var here = sys[Docked.Body];
            // around the port's planet; belt ports put the wreck on a heliocentric orbit near the port
            var parent = here.Parent == sys.Sun ? (here.Asteroid ? sys.Sun : here) : here.Parent;
            var c = new Contract { Id = nextId++, Kind = JobKind.Rescue, From = Docked.Name, Parent = parent.Name, Epoch = jd };
            string hull = Hulls[rng.Next(Hulls.Length)];
            c.To = DerelictPrefix + hull.Substring(hull.IndexOf(' ') + 1) + " " + c.Id;
            c.Title = $"Rescue the {hull}: drive dead, {2 + rng.Next(9)} crew aboard";
            double mu;
            if (parent == sys.Sun)
            {
                var hp = sys.Pos(here, jd);
                double r = hp.Mag;
                c.A = r * (1 + (rng.NextDouble() - 0.5) * 0.06);
                c.E = 0.03 + rng.NextDouble() * 0.1;
                c.I = rng.NextDouble() * 12;
                c.Node = rng.NextDouble() * 360;
                c.Peri = rng.NextDouble() * 360;
                c.L = Math.Atan2(hp.y, hp.x) * 180 / Math.PI + (rng.NextDouble() - 0.5) * 16; // a few degrees from the port
                mu = sys.Sun.GM;
            }
            else
            {
                c.A = parent.Radius * (4 + rng.NextDouble() * 40);
                c.E = rng.NextDouble() * 0.25;
                c.I = rng.NextDouble() * 30;
                c.Node = rng.NextDouble() * 360;
                c.Peri = rng.NextDouble() * 360;
                c.L = rng.NextDouble() * 360;
                mu = parent.GM;
            }
            c.DL = 360 / (2 * Math.PI * Math.Sqrt(c.A * c.A * c.A / mu) / 86400);
            var b = MakeBody(c, sys);
            double d = (sys.Pos(b, jd) - sys.Pos(here, jd)).Mag;
            double slack = 1.1 + rng.NextDouble() * 0.6; // their air is running out
            c.Pay = Math.Round(RefPay(d, 0, out c.Days, slack) * 2 / 100) * 100 + 4000;
            c.Days += TransferSeconds / 86400;
            return c;
        }

        public static Body MakeBody(Contract c, SolarSystem sys) => new Body
        {
            Name = c.To, Parent = sys[c.Parent], GM = 0, Radius = 0.05, Look = Look.Rocky,
            ColA = new Color(0.7f, 0.7f, 0.72f), ColB = new Color(0.5f, 0.5f, 0.52f),
            A = c.A, E = c.E, I = c.I, Node = c.Node, Peri = c.Peri, L = c.L, dL = c.DL, EpochJd = c.Epoch,
        };

        void SpawnDerelict(Contract c)
        {
            if (Sys == null || Sys[c.To] != null) return;
            if (c.Kind == JobKind.Rescue && Sys[c.Parent] != null) Sys.Bodies.Add(MakeBody(c, Sys));
            if (c.Kind == JobKind.Intercept) Sys.Bodies.Add(MakeRunner(c, Sys));
        }

        void RemoveDerelict(Contract c)
        {
            if ((c.Kind != JobKind.Rescue && c.Kind != JobKind.Intercept) || Sys == null) return;
            Sys.Bodies.RemoveAll(b => b.Name == c.To);
        }

        void Rescues(SolarSystem sys, Ship ship, double jd, double simDt)
        {
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                var c = Active[i];
                if (c.Kind != JobKind.Rescue) continue;
                var b = sys[c.To];
                if (b == null) continue;
                bool alongside = (ship.Pos - sys.Pos(b, jd)).Mag < RescueKm && (ship.Vel - sys.Vel(b, jd)).Mag < RescueKms && ship.Throttle == 0;
                if (!alongside) { c.Dwell = 0; continue; }
                TransferInProgress = true;
                c.Dwell += Math.Min(simDt, 30);
                if (c.Dwell >= TransferSeconds) Complete(c, jd, "Crew of the " + c.Title.Substring(11, c.Title.IndexOf(':') - 11) + " rescued");
            }
        }
    }
}
