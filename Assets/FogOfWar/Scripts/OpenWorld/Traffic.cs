using System;
using System.Collections.Generic;

namespace FogOfWar.OpenWorld
{
    // NPC traffic (docs/design-world.md, "NPC traffic"): ships on Rails between the ports. A schedule from (seed, day)
    // gives each day's trips, so the same date always gives the same ships and nothing is saved. Only ships in flight
    // are bodies: each joins the system at departure and leaves it on arrival.
    public sealed class Traffic
    {
        public sealed class Trip
        {
            public string Name, Hull;
            public Port From, To;
            public Faction Owner;   // the origin port's
            public double Dep, Arr; // JD
            public Rail Rail;       // built by Ready, the first time the trip is needed
            public bool Refused;    // its course failed Rail.CheckEnds
            public Body Body;       // while in flight
            internal Body A, Z;
        }

        public const string Prefix = "Traffic ";
        public static bool Is(Body b) => b != null && b.Name.StartsWith(Prefix, StringComparison.Ordinal);

        // working values [design]: departures a day from each port (Career.Ports order), and how strongly a ship picks a
        // near port (weight = distance^-DistPower). A trip takes about 10 days, so about 110 ships are in flight. The drive
        // is the runners'.
        static readonly int[] PerDay = { 2, 1, 2, 1, 1, 1, 1, 1, 1 };
        const double DistPower = 0.25, DriveG = Career.RunnerG;
        // longer than the longest trip, so a reset finds every ship in flight: Jupiter to Saturn across the Sun, both at
        // aphelion, is 15.6 AU and 27.6 days [calc]
        public const double LookbackDays = 30;
        static readonly string[] Hulls = { "courier", "packet", "liner", "freighter", "tanker", "hauler" }; // the Fleet.json classes

        readonly SolarSystem sys;
        readonly int seed;
        readonly Body[] ports;
        readonly Dictionary<long, List<Trip>> days = new Dictionary<long, List<Trip>>();
        readonly List<Trip> flying = new List<Trip>();
        double last = double.NaN;

        public IReadOnlyList<Trip> Flying => flying;
        public int Checked { get; private set; }    // courses built and tested so far
        public int Refused { get; private set; }    // of those, the ones that failed Rail.CheckEnds

        public Traffic(SolarSystem sys, int seed = 1)
        {
            this.sys = sys; this.seed = seed;
            ports = Array.ConvertAll(Career.Ports, p => sys[p.Body]);
        }

        // Bring the ships in flight up to the clock: arrivals leave, and departures since the last call join, a day at a
        // time, so a frame at 1M× misses none. A step back in time, or one longer than the lookback, rebuilds from the
        // schedule. A trip that departs and arrives within one step never shows.
        public void Update(double jd)
        {
            if (double.IsNaN(last) || jd < last || jd - last > LookbackDays) { Reset(jd); return; }
            for (int i = flying.Count - 1; i >= 0; i--)
                if (flying[i].Arr <= jd) { sys.Bodies.Remove(flying[i].Body); flying[i].Body = null; flying.RemoveAt(i); }
            for (long d = (long)Math.Floor(last); d <= (long)Math.Floor(jd); d++)
                foreach (var t in Day(d))
                    if (t.Dep > last && t.Dep <= jd && t.Arr > jd && Ready(t)) Launch(t);
            if (Math.Floor(jd) > Math.Floor(last)) Forget(jd);
            last = jd;
        }

        public void Reset(double jd)
        {
            Clear();
            for (long d = (long)Math.Floor(jd - LookbackDays); d <= (long)Math.Floor(jd); d++)
                foreach (var t in Day(d))
                    if (t.Dep <= jd && t.Arr > jd && Ready(t)) Launch(t);
            Forget(jd);
            last = jd;
        }

        public void Clear()
        {
            foreach (var t in flying) { sys.Bodies.Remove(t.Body); t.Body = null; }
            flying.Clear();
            last = double.NaN;
        }

        // A course is built and tested the first time its trip is needed (at departure, or by QA), not when its day is
        // drawn: a reset then pays only for the ships in flight, and a day's cost spreads over its departures.
        public bool Ready(Trip t)
        {
            if (t.Rail == null)
            {
                t.Rail = new Rail(sys, t.A, t.Z, t.Dep, t.Arr);
                t.Refused = !t.Rail.CheckEnds().Clear;
                Checked++;
                if (t.Refused) Refused++;
            }
            return !t.Refused;
        }

        void Launch(Trip t)
        {
            t.Body = t.Rail.Craft(t.Name);
            sys.Bodies.Add(t.Body);
            flying.Add(t);
        }

        // days that can no longer launch a ship
        void Forget(double jd)
        {
            long first = (long)Math.Floor(jd - LookbackDays);
            var old = new List<long>();
            foreach (var d in days.Keys) if (d < first) old.Add(d);
            foreach (var d in old) days.Remove(d);
        }

        // The trips that leave on day d (whole JD days), refused ones included (Ready tests them). Each trip draws its
        // numbers whether or not its course passes, so one refused trip does not reshuffle the rest of the day.
        public List<Trip> Day(long d)
        {
            if (days.TryGetValue(d, out var list)) return list;
            list = new List<Trip>();
            var rng = new Random(unchecked(seed * 1000003 + (int)(d % 1000000)));
            var w = new double[ports.Length];
            int n = 0;
            for (int i = 0; i < ports.Length; i++)
                for (int k = 0; k < PerDay[i]; k++, n++)
                {
                    double dep = d + rng.NextDouble(), pick = rng.NextDouble();
                    string hull = Hulls[rng.Next(Hulls.Length)];
                    var a = ports[i];
                    if (a == null) continue;
                    var pa = sys.PosAt(a, dep);
                    double sum = 0;
                    for (int j = 0; j < ports.Length; j++)
                    {
                        w[j] = j == i || ports[j] == null ? 0 : Math.Pow((sys.PosAt(ports[j], dep) - pa).Mag, -DistPower);
                        sum += w[j];
                    }
                    int to = -1;
                    pick *= sum;
                    for (int j = 0; j < ports.Length; j++)
                        if (w[j] > 0) { to = j; if ((pick -= w[j]) <= 0) break; }
                    if (to < 0) continue;
                    var z = ports[to];
                    double T = Math.Sqrt(6 * (sys.PosAt(z, dep) - pa).Mag / (DriveG * Ship.G0)) / 86400; // as a runner's
                    list.Add(new Trip
                    {
                        Name = $"{Prefix}{hull} {d % 10000:0000}-{n:00}", Hull = hull,
                        From = Career.Ports[i], To = Career.Ports[to], Owner = Career.Ports[i].Owner,
                        Dep = dep, Arr = dep + T, A = a, Z = z,
                    });
                }
            days[d] = list;
            return list;
        }
    }
}
