using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace FogOfWar.EditorTools
{
    // Assertions use independent closed forms/data; refinements measure discretisation error, not physical truth.
    [Serializable] public sealed class BenchmarkReport
    {
        [Serializable] public sealed class Result
        {
            public string id, reference, units, detail, category;
            public double measured, tolerance;
            public bool pass;
        }
        [Serializable] public sealed class Sample
        {
            public string id, units, reference, detail;
            public double measured;
        }
        [Serializable] public sealed class Source { public string path, sha256; }
        public string utc = DateTime.UtcNow.ToString("O"), commit, unity = Application.unityVersion;
        public string precision, cpu = SystemInfo.processorType, gpu = SystemInfo.graphicsDeviceName;
        public string graphics = SystemInfo.graphicsDeviceVersion, os = SystemInfo.operatingSystem;
        public List<Source> sources = new List<Source>();
        public List<Result> checks = new List<Result>();
        public List<Sample> diagnostics = new List<Sample>();
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
        public void Check(string id, double value, double limit, string units, string reference, string detail = "", string category = "invariant-or-coverage")
        {
            bool ok = !double.IsNaN(value) && !double.IsInfinity(value) && value <= limit;
            if (!ok) failed++;
            checks.Add(new Result { id = id, measured = value, tolerance = limit, units = units, reference = reference, detail = detail, pass = ok, category = category });
            Debug.Log($"BENCH {(ok ? "PASS" : "FAIL")} {id}: {value:E6} <= {limit:E6} {units} {detail}");
        }
        public void Diagnostic(string id, double value, string units, string reference, string detail = "")
        {
            diagnostics.Add(new Sample { id = id, measured = value, units = units, reference = reference, detail = detail });
            Debug.Log($"BENCH DIAGNOSTIC {id}: {value:E6} {units} {detail}");
        }
        public void Save(string name)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/" + name + ".json", JsonUtility.ToJson(this, true) + "\n");
            string groups = string.Join(", ", checks.GroupBy(c => c.category).Select(g => $"{g.Key}={g.Count()}"));
            Debug.Log($"BENCH {name}: {checks.Count} checks, {failed} failed ({groups}); {diagnostics.Count} diagnostics, not gates");
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
                Relativity(); Journey();
            }
            catch (Exception e) { report.Check("exception", 1, 0, "exceptions", "suite must complete", e.ToString(), category: "invariant-or-coverage"); }
            report.Save("physics-benchmarks");
            if (Application.isBatchMode) EditorApplication.Exit(report.failed == 0 ? 0 : 1);
        }

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
                report.Check($"Kerr/a={spin}/r={r}/frequency", Math.Abs(orbit.phi/orbit.t-1/(Math.Pow(r,1.5)+spin)), 1e-10, "1/M", "exact equatorial circular geodesic Omega=1/(r^(3/2)+a)", category: "independent-reference");
                report.Check($"Kerr/a={spin}/r={r}/radius", Math.Abs(orbit.r-r), 1e-7, "M", "constant-radius analytic solution", category: "independent-reference");
                report.Check($"Kerr/a={spin}/r={r}/radial-invariant", invariant, 1e-9, "dimensionless", "normalised Carter radial first integral", category: "invariant-or-coverage");
            }
            foreach (var ends in new[] { new[] { 20.0,80.0 }, new[] { 8.0,20.0 } })
            {
                double rp = ends[0], ra = ends[1], p = 2*rp*ra/(rp+ra), e = (ra-rp)/(ra+rp);
                Func<double,double> f = chi => Math.Sqrt(p/(p-6-2*e*Math.Cos(chi)));
                double reference = Simpson(f,0,2*Math.PI,16384)-2*Math.PI;
                report.Check($"Schwarzschild/{rp}/{ra}/quadrature", Math.Abs(reference-(Simpson(f,0,2*Math.PI,8192)-2*Math.PI)), 1e-10, "rad", "Darwin anomaly integral; independent quadrature refinement", category: "self-convergence");
                foreach (double k in new[] { 0.08, 0.04, 0.02 })
                {
                    var orbit = Geo.Bound(rp, ra, 0); double last = 0, advance = 0, invariant = 0; int count = 0;
                    for (int i=0; i<1000000 && count<4; i++)
                    {
                        orbit.Step(0,orbit.StepSize(0,k));
                        invariant = Math.Max(invariant,Math.Abs(orbit.w*orbit.w-orbit.R(orbit.r,0))/Math.Pow(orbit.r,4));
                        if (orbit.peris > count) { if(count>0) advance = orbit.periPhi-last-2*Math.PI; last=orbit.periPhi; count=orbit.peris; }
                    }
                    report.Check($"Schwarzschild/{rp}/{ra}/k={k}/precession", count<4 ? 1e9 : Math.Abs(advance-reference), 0.0005, "rad/orbit", "integral_0^2pi sqrt(p/(p-6-2e cos chi)) dchi - 2pi; PhysRevD.92.044048", category: "independent-reference");
                    report.Check($"Schwarzschild/{rp}/{ra}/k={k}/invariant", invariant, 2e-5, "dimensionless", "radial first integral over four eccentric orbits", category: "invariant-or-coverage");
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
                report.Check($"Journey/dest={destination}/reference-time",Math.Abs(Field(start,"tTotal")-2*midTime),1e-7,"yr","hyperbolic worldline: half-trip t=sqrt(D/a+D^2/4)", category: "independent-reference");
                var end=At(T); report.Check($"Journey/dest={destination}/arrival",Math.Abs(Field(end,"x")-distance)+Math.Abs(Field(end,"beta")),1e-8,"ly + c","end at destination and rest", category: "independent-reference");
                var mid=At(T/2); report.Check($"Journey/dest={destination}/midpoint",Math.Abs(Field(mid,"x")-distance/2),1e-7,"ly","symmetric constant proper acceleration worldline", category: "independent-reference");
                foreach (double tau in new[] {1e-8,1e-5})
                {
                    var low=At(tau); double x=Field(low,"x"), expected=0.5*a*tau*tau;
                    report.Check($"Journey/dest={destination}/low-tau={tau}",Math.Abs(x-expected)/expected,1e-8,"relative position","Newtonian low-rapidity Taylor limit x=a*tau^2/2", category: "independent-reference");
                }
            }
        }
    }
}
