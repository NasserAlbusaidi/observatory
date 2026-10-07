using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using FogOfWar.OpenWorld;
using UnityEditor;
using UnityEngine;

namespace FogOfWar.EditorTools
{
    // Assertions use independent closed forms/data; refinements measure discretisation error, not physical truth.
    [Serializable] public sealed class BenchmarkReport
    {
        [Serializable] public sealed class Result
        {
            public string id, reference, units, detail;
            public double measured, tolerance;
            public bool pass;
        }
        [Serializable] public sealed class Source { public string path, sha256; }
        public string utc = DateTime.UtcNow.ToString("O"), commit, unity = Application.unityVersion;
        public string precision, cpu = SystemInfo.processorType, gpu = SystemInfo.graphicsDeviceName;
        public string graphics = SystemInfo.graphicsDeviceVersion, os = SystemInfo.operatingSystem;
        public List<Source> sources = new List<Source>();
        public List<Result> checks = new List<Result>();
        public int failed;
        public BenchmarkReport(string precision)
        {
            this.precision = precision;
            var info = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
            using (var p = System.Diagnostics.Process.Start(info)) { commit = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); }
            using var sha = SHA256.Create();
            foreach (string dir in new[] { "Assets/FogOfWar/Scripts", "Assets/FogOfWar/Editor", "Assets/FogOfWar/Resources/Shaders", "Tools/benchmarks/fixtures" })
            foreach (string path in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                if (!path.EndsWith(".meta")) sources.Add(new Source { path = path.Replace('\\', '/'), sha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant() });
        }
        public void Check(string id, double value, double limit, string units, string reference, string detail = "")
        {
            bool ok = !double.IsNaN(value) && !double.IsInfinity(value) && value <= limit;
            if (!ok) failed++;
            checks.Add(new Result { id = id, measured = value, tolerance = limit, units = units, reference = reference, detail = detail, pass = ok });
            Debug.Log($"BENCH {(ok ? "PASS" : "FAIL")} {id}: {value:E6} <= {limit:E6} {units} {detail}");
        }
        public void Save(string name)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/" + name + ".json", JsonUtility.ToJson(this, true) + "\n");
            Debug.Log($"BENCH {name}: {checks.Count} checks, {failed} failed");
        }
    }

    public static class FogOfWarPhysicsCheck
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static BenchmarkReport report;
        [MenuItem("Fog of War/Independent physics benchmarks")]
        public static void Run()
        {
            report = new BenchmarkReport("IEEE float64 physics; float32 attitude; km, s, TDB JD unless stated");
            try
            {
                report.Check("contact-clock-provenance-regressions", FogOfWarRegressionCheck.RunChecks(), 0, "failed checks",
                    "Rodrigues rotation / analytic circular orbit; details in issue-regressions.txt");
                Burns(); Conics(); Gravity(); Ephemerides(); Relativity(); Journey();
            }
            catch (Exception e) { report.Check("exception", 1, 0, "exceptions", "suite must complete", e.ToString()); }
            report.Save("physics-benchmarks");
            if (Application.isBatchMode) EditorApplication.Exit(report.failed == 0 ? 0 : 1);
        }

        static void Burns()
        {
            var sun = new Body { Name = "force-free origin", Radius = 1, GM = 0 };
            var sys = new SolarSystem { Sun = sun }; sys.Bodies.Add(sun);
            foreach (double cargo in new[] { 0.0, 120.0 })
            foreach (bool rcs in new[] { false, true })
            foreach (double step in new[] { 0.05, 1.0, 30.0 })
            foreach (bool deplete in new[] { false, true })
            {
                const double duration = 120, burnUntil = 2.3;
                double spendAccel = Ship.G0 + (rcs ? Ship.RcsAccel : 0);
                var ship = new Ship { Pos = new D3(10000, 0, 0), Vel = new D3(2, 3, 4), Cargo = cargo,
                    UnlimitedFuel = false, ThrottleCmd = 1, RcsInput = rcs ? Vector3.right : Vector3.zero };
                ship.Fuel = deplete ? (ship.DryMass + cargo) * (Math.Exp(spendAccel * burnUntil / Ship.Ve) - 1) : 200;
                double initialMass = ship.Mass, dryMass = ship.DryMass + cargo;
                D3 p0 = ship.Pos, v0 = ship.Vel, acceleration = ship.Heading * Ship.G0 + (rcs ? Frame.FromU(Vector3.right) * Ship.RcsAccel : D3.Zero);
                double t = 0;
                while (t < duration) { double h = Math.Min(step, duration - t); ship.Step(sys, CareerQA.Epoch + t / 86400, h, sun); t += h; }
                double tb = deplete ? Ship.Ve * Math.Log(initialMass / dryMass) / spendAccel : duration;
                D3 expected = p0 + v0 * duration + acceleration * (tb * (duration - tb / 2));
                string id = $"burn/cargo={cargo}/rcs={rcs}/h={step}/deplete={deplete}";
                report.Check(id + "/position", (ship.Pos - expected).Mag, 1e-6, "km", "constant acceleration then inertial coast; x=x0+v0*T+a*tb*(T-tb/2)");
                report.Check(id + "/velocity", (ship.Vel - v0 - acceleration * tb).Mag, 1e-8, "km/s", "integrated thrust impulse a*tb");
                double fuel = deplete ? 0 : initialMass * Math.Exp(-spendAccel * duration / Ship.Ve) - dryMass;
                report.Check(id + "/fuel", Math.Abs(ship.Fuel - fuel), 1e-8, "tonnes", "Tsiolkovsky rocket equation; cargo included in inert mass; main/RCS exhaust costs add");
            }
        }

        static void Conics()
        {
            const double mu = 398600.435507, axis = 20000;
            foreach (double e in new[] { 0.0, 0.7, 0.99, 1.0001, 1.8, 8.0 })
            foreach (double anomaly in new[] { -1.0, 0.2, 2.0 })
            {
                bool hyper = e > 1;
                double rp = axis * (hyper ? e - 1 : 1 - e), n = Math.Sqrt(mu / (axis * axis * axis));
                double vperi = Math.Sqrt(mu * (1 + e) / rp), dt;
                D3 expectedP, expectedV;
                if (hyper)
                {
                    dt = (e * Math.Sinh(anomaly) - anomaly) / n;
                    double den = e * Math.Cosh(anomaly) - 1;
                    expectedP = new D3(axis * (e - Math.Cosh(anomaly)), axis * Math.Sqrt(e * e - 1) * Math.Sinh(anomaly), 0);
                    expectedV = new D3(-axis * n * Math.Sinh(anomaly) / den, axis * n * Math.Sqrt(e * e - 1) * Math.Cosh(anomaly) / den, 0);
                }
                else
                {
                    dt = (anomaly - e * Math.Sin(anomaly)) / n;
                    double den = 1 - e * Math.Cos(anomaly);
                    expectedP = new D3(axis * (Math.Cos(anomaly) - e), axis * Math.Sqrt(1 - e * e) * Math.Sin(anomaly), 0);
                    expectedV = new D3(-axis * n * Math.Sin(anomaly) / den, axis * n * Math.Sqrt(1 - e * e) * Math.Cos(anomaly) / den, 0);
                }
                bool ok = Rails.Propagate(new D3(rp, 0, 0), new D3(0, vperi, 0), mu, dt, out var p, out var v);
                string id = $"conic/e={e}/anomaly={anomaly}";
                report.Check(id + "/solve", ok ? 0 : 1, 0, "failures", "elliptic eccentric anomaly / hyperbolic anomaly closed forms");
                report.Check(id + "/position", (p - expectedP).Mag, 0.001, "km", "Kepler ellipse/hyperbola parameterisation, no universal variables");
                report.Check(id + "/velocity", (v - expectedV).Mag, 1e-6, "km/s", "time derivative of anomaly parameterisation");
            }
            // Inbound half of e=.5 ellipse: crossing occurs before periapsis, then arc ends outside again.
            double eccentricity = 0.5, radius = 15000, E = 2 * Math.PI - Math.Acos((1 - radius / axis) / eccentricity);
            double rate = Math.Sqrt(mu / Math.Pow(axis, 3)), period = 2 * Math.PI / rate;
            double crossing = (E - eccentricity * Math.Sin(E) - Math.PI) / rate;
            double hit = Rails.FirstCrossing(new D3(-30000, 0, 0), new D3(0, -Math.Sqrt(mu * (1 - eccentricity) / 30000), 0), mu, radius, period);
            report.Check("surface/first-crossing", Math.Abs(hit - crossing), 1e-4, "s", "Kepler anomaly solve for r=15000 on inbound e=.5 ellipse; end outside surface");
        }

        static Ship Integrated(SolarSystem sys, D3 p, D3 v, double duration, double step)
        {
            var ship = new Ship { Pos = p, Vel = v };
            double jd = CareerQA.Epoch, end = jd + duration / 86400;
            while (jd < end)
            {
                double next = Math.Min(end, jd + step / 86400), h = (next - jd) * 86400;
                ship.Step(sys, jd, h, Ship.Dominant(sys, ship.Pos, jd)); jd = next;
            }
            return ship;
        }

        static void Gravity()
        {
            var sys = SolarSystem.Build();
            var earth = sys.Earth; var moon = sys["Moon"];
            // Retain the Sun/Earth/Moon ephemerides, heliocentric indirect terms and Earth J2. Other bodies omitted in
            // this explicitly bounded three-body fixture to make the reference reproducible and affordable.
            sys.Bodies.RemoveAll(b => b != sys.Sun && b != earth && b != moon);
            foreach (string fixture in new[] { "inclined-Earth-J2", "Moon-encounter", "Moon-SOI-exit", "isolated-sphere" })
            {
                SolarSystem model = sys; Body centre = earth; double duration = 3600;
                D3 offset = new D3(7000, 0, 0), relativeV = new D3(0, 5.2, 5.2);
                if (fixture == "Moon-encounter") { centre = moon; offset = new D3(6000, 0, 0); relativeV = new D3(-0.7, 1.4, 0.2); }
                if (fixture == "Moon-SOI-exit") { centre = moon; offset = new D3(60000, 0, 0); relativeV = new D3(2, 0.2, 0.1); duration = 18000; }
                if (fixture == "isolated-sphere")
                {
                    centre = new Body { Name = "Sphere", GM = earth.GM, Radius = earth.Radius };
                    model = new SolarSystem { Sun = centre }; model.Bodies.Add(centre);
                    relativeV = new D3(0, Math.Sqrt(centre.GM / offset.Mag), 0);
                }
                D3 p0 = model.Pos(centre, CareerQA.Epoch) + offset, v0 = model.Vel(centre, CareerQA.Epoch) + relativeV;
                var fine = Integrated(model, p0, v0, duration, 0.25);
                double previous = double.PositiveInfinity;
                foreach (double h in new[] { 2.0, 1.0, 0.5 })
                {
                    var ship = Integrated(model, p0, v0, duration, h);
                    double dp = (ship.Pos - fine.Pos).Mag, dv = (ship.Vel - fine.Vel).Mag;
                    report.Check($"gravity/{fixture}/h={h}/position", dp, 0.25, "km", "zero-thrust production Ship.Step; same forces; reference h=.25s at same TDB endpoint", $"duration={duration}s");
                    report.Check($"gravity/{fixture}/h={h}/velocity", dv, 0.0003, "km/s", "same-force step refinement");
                    report.Check($"gravity/{fixture}/h={h}/convergence", dp <= previous + 1e-5 ? 0 : 1, 0, "nonconvergent refinements", "position error must fall on step halving (10 mm roundoff floor)");
                    previous = dp;
                }
                var scene = new OpenSystemScene(); var coastShip = new Ship { Pos = p0, Vel = v0 };
                typeof(OpenSystemScene).GetField("ship", Private).SetValue(scene, coastShip);
                typeof(OpenSystemScene).GetField("sys", Private).SetValue(scene, model);
                var coast = typeof(OpenSystemScene).GetMethod("Coast", Private);
                double jd = CareerQA.Epoch, end = jd + duration / 86400; int transitions = 0;
                Body dom = Ship.Dominant(model, p0, jd);
                while (jd < end)
                {
                    typeof(OpenSystemScene).GetField("jd", Private).SetValue(scene, jd);
                    double used = (double)coast.Invoke(scene, new object[] { Math.Min(30, (end - jd) * 86400) });
                    if (!(used > 0)) throw new Exception("coast failed to advance");
                    jd += used / 86400;
                    Body next = Ship.Dominant(model, coastShip.Pos, jd);
                    if (next != dom) transitions++; dom = next;
                }
                double difference = (coastShip.Pos - fine.Pos).Mag;
                D3 focusP=model.Pos(centre,end), focusV=model.Vel(centre,end);
                double Eccentricity(Ship ship)
                {
                    var r=ship.Pos-focusP; var v=ship.Vel-focusV;
                    return (D3.Cross(v,D3.Cross(r,v))/centre.GM-r.Norm).Mag;
                }
                double Energy(Ship ship) => (ship.Vel-focusV).Sqr/2-centre.GM/(ship.Pos-focusP).Mag;
                D3 normalFine=D3.Cross(fine.Pos-focusP,fine.Vel-focusV).Norm;
                D3 normalCoast=D3.Cross(coastShip.Pos-focusP,coastShip.Vel-focusV).Norm;
                double planeAngle=Math.Acos(Math.Max(-1,Math.Min(1,D3.Dot(normalFine,normalCoast))));
                report.Check($"gravity/{fixture}/high-warp-model-difference", difference, fixture == "isolated-sphere" ? 0.01 : 250, "km",
                    "dominant-body conic versus three-body/J2 integrator; model difference, NOT integrator accuracy",
                    $"duration={duration}s; dominant transitions={transitions}; velocity difference={(coastShip.Vel-fine.Vel).Mag:R} km/s; osculating eccentricity difference={Math.Abs(Eccentricity(coastShip)-Eccentricity(fine)):R}; specific-energy difference={Math.Abs(Energy(coastShip)-Energy(fine)):R} km^2/s^2; orbital-plane angle={planeAngle:R} rad");
                D3 beforeP = coastShip.Pos, beforeV = coastShip.Vel;
                typeof(OpenSystemScene).GetField("warpIdx", Private).SetValue(scene, 0);
                coastShip.Step(model, jd, 0, dom);
                report.Check($"gravity/{fixture}/mode-switch-jump", (coastShip.Pos-beforeP).Mag+(coastShip.Vel-beforeV).Mag, 0, "km + km/s",
                    "switching mode itself must preserve world state (zero elapsed time)");
                if (fixture == "Moon-SOI-exit") report.Check("gravity/SOI-fixture-crosses-boundary", transitions > 0 ? 0 : 1, 0, "missing transitions", "fixture must actually cross a dominant-body boundary");
            }
        }

        [Serializable] sealed class EphemerisFixture { public VectorFixture[] vectors; }
        [Serializable] sealed class VectorFixture { public string body; public double jd, x, y, z, vx, vy, vz; public bool outside; }
        static void Ephemerides()
        {
            var fixtures = JsonUtility.FromJson<EphemerisFixture>(File.ReadAllText("Tools/benchmarks/fixtures/ephemeris.json"));
            var sys = SolarSystem.Build();
            var fitInputs = JsonUtility.FromJson<EphemerisFixture>(File.ReadAllText("Tools/benchmarks/fixtures/fit-inputs.json"));
            foreach (var f in fitInputs.vectors)
            {
                bool ok = sys[f.body].Cheb.Eval(f.jd, out var p);
                report.Check($"ephemeris/{f.body}/{f.jd:R}/original-source-position", ok ? (p-new D3(f.x,f.y,f.z)).Mag : double.PositiveInfinity,
                    2,"km","original JPL Horizons fitting inputs; raw header, source SHA256, fit SHA256 retained in fit-inputs.json");
            }
            var currentComparison = new List<BenchmarkReport.Result>();
            foreach (var f in fixtures.vectors)
            {
                var body = sys[f.body]; bool ok = body.Cheb.Eval(f.jd, out var p, out var v);
                string id = $"ephemeris/{f.body}/{f.jd:R}";
                report.Check(id + "/coverage", ok != f.outside ? 0 : 1, 0, "failures", "retained source vectors and usable segment bounds; no extrapolation past fit");
                if (!ok) continue;
                // Fresh service vectors are NOT silently substituted for the input data version. Preserve every
                // discrepancy, including Ryugu's ~4.5 km late-century mismatch, separately from polynomial-fit error.
                double difference = (p-new D3(f.x,f.y,f.z)).Mag;
                currentComparison.Add(new BenchmarkReport.Result { id=id, measured=difference, tolerance=2, units="km",
                    reference="fresh Horizons query versus shipped fit; may include source integration/model differences", pass=difference<=2,
                    detail="Diagnostic comparison, separate from original-input fit assertion. See source provenance in both fixture files." });
                report.Check(id + "/fit-velocity", (v - new D3(f.vx, f.vy, f.vz)).Mag, 0.0001, "km/s", "retained Horizons velocity (analytic derivative of fitted polynomial compared separately)");
            }
            File.WriteAllText("Logs/current-horizons-comparison.json", JsonUtility.ToJson(new Comparisons { checks=currentComparison },true));
            // A boundary blend is deliberately a model transition, not a Horizons-accurate fit. Gate continuity only.
            foreach (var body in sys.Bodies)
            {
                if (body.Cheb == null) continue;
                foreach (double boundary in new[] { body.Cheb.JdStart, body.Cheb.JdStart+180, body.Cheb.JdEnd-180, body.Cheb.JdEnd })
                {
                    double delta = 0.001 / 86400;
                    D3 v = sys.Vel(body, boundary), p0 = sys.PosAt(body, boundary-delta), p1 = sys.PosAt(body, boundary+delta);
                    double elapsed = ((boundary+delta)-(boundary-delta))*86400;
                    report.Check($"blend/{body.Name}/{boundary:R}", (p1-p0-v*elapsed).Mag, 0.001, "km",
                        "C1 continuity across mean-element/fit boundary, not source-vector accuracy in the blend");
                }
            }
        }
        [Serializable] sealed class Comparisons { public List<BenchmarkReport.Result> checks; }

        static double Simpson(Func<double, double> f, double lo, double hi, int n)
        {
            double h = (hi-lo)/n, sum = f(lo)+f(hi);
            for (int i=1; i<n; i++) sum += (i%2 == 0 ? 2 : 4)*f(lo+i*h);
            return sum*h/3;
        }
        static void Relativity()
        {
            foreach (double spin in new[] { 0.0, 0.5, 0.9, 0.998 })
            foreach (double r in new[] { 12.0, 30.0 })
            {
                var orbit = Geo.Circular(r, spin); double invariant = 0;
                while (orbit.t < 2000)
                {
                    orbit.Step(spin, orbit.StepSize(spin, 0.04));
                    invariant = Math.Max(invariant, Math.Abs(orbit.w*orbit.w-orbit.R(orbit.r,spin))/Math.Pow(orbit.r,4));
                }
                report.Check($"Kerr/a={spin}/r={r}/frequency", Math.Abs(orbit.phi/orbit.t-1/(Math.Pow(r,1.5)+spin)), 1e-10, "1/M", "exact equatorial circular geodesic Omega=1/(r^(3/2)+a)");
                report.Check($"Kerr/a={spin}/r={r}/radius", Math.Abs(orbit.r-r), 1e-7, "M", "constant-radius analytic solution");
                report.Check($"Kerr/a={spin}/r={r}/radial-invariant", invariant, 1e-9, "dimensionless", "normalised Carter radial first integral");
            }
            foreach (var ends in new[] { new[] { 20.0,80.0 }, new[] { 8.0,20.0 } })
            {
                double rp = ends[0], ra = ends[1], p = 2*rp*ra/(rp+ra), e = (ra-rp)/(ra+rp);
                Func<double,double> f = chi => Math.Sqrt(p/(p-6-2*e*Math.Cos(chi)));
                double reference = Simpson(f,0,2*Math.PI,16384)-2*Math.PI;
                report.Check($"Schwarzschild/{rp}/{ra}/quadrature", Math.Abs(reference-(Simpson(f,0,2*Math.PI,8192)-2*Math.PI)), 1e-10, "rad", "Darwin anomaly integral; independent quadrature refinement");
                foreach (double k in new[] { 0.08, 0.04, 0.02 })
                {
                    var orbit = Geo.Bound(rp, ra, 0); double last = 0, advance = 0, invariant = 0; int count = 0;
                    for (int i=0; i<1000000 && count<4; i++)
                    {
                        orbit.Step(0,orbit.StepSize(0,k));
                        invariant = Math.Max(invariant,Math.Abs(orbit.w*orbit.w-orbit.R(orbit.r,0))/Math.Pow(orbit.r,4));
                        if (orbit.peris > count) { if(count>0) advance = orbit.periPhi-last-2*Math.PI; last=orbit.periPhi; count=orbit.peris; }
                    }
                    report.Check($"Schwarzschild/{rp}/{ra}/k={k}/precession", count<4 ? 1e9 : Math.Abs(advance-reference), 0.0005, "rad/orbit", "integral_0^2pi sqrt(p/(p-6-2e cos chi)) dchi - 2pi; PhysRevD.92.044048");
                    report.Check($"Schwarzschild/{rp}/{ra}/k={k}/invariant", invariant, 2e-5, "dimensionless", "radial first integral over four eccentric orbits");
                }
            }
        }

        static void Journey()
        {
            var scene = new JourneyScene(); var state = typeof(JourneyScene).GetMethod("State",Private);
            object At(double tau) { typeof(JourneyScene).GetField("tau",Private).SetValue(scene,tau); return state.Invoke(scene,null); }
            double Field(object s,string n) => (double)s.GetType().GetField(n).GetValue(s);
            foreach (int destination in new[] {0,1,2,3})
            {
                typeof(JourneyScene).GetField("dest",Private).SetValue(scene,destination);
                double distance = new[] {4.24,26670,220000,2537000.0}[destination], a = 1.0323;
                object start=At(0); double T=Field(start,"T"), midTime = Math.Sqrt(distance/a+distance*distance/4);
                report.Check($"Journey/dest={destination}/reference-time",Math.Abs(Field(start,"tTotal")-2*midTime),1e-7,"yr","hyperbolic worldline: half-trip t=sqrt(D/a+D^2/4)");
                var end=At(T); report.Check($"Journey/dest={destination}/arrival",Math.Abs(Field(end,"x")-distance)+Math.Abs(Field(end,"beta")),1e-8,"ly + c","end at destination and rest");
                var mid=At(T/2); report.Check($"Journey/dest={destination}/midpoint",Math.Abs(Field(mid,"x")-distance/2),1e-7,"ly","symmetric constant proper acceleration worldline");
                foreach (double tau in new[] {1e-8,1e-5})
                {
                    var low=At(tau); double x=Field(low,"x"), expected=0.5*a*tau*tau;
                    report.Check($"Journey/dest={destination}/low-tau={tau}",Math.Abs(x-expected)/expected,1e-8,"relative position","Newtonian low-rapidity Taylor limit x=a*tau^2/2");
                }
            }
        }
    }
}
