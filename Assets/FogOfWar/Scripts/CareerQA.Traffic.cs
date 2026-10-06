using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // 11. NPC traffic (Traffic.cs, docs/design-world.md W2b): the schedule repeats, a traffic set stepped at 1M× matches a
    // fresh one, the courses clear the Sun and the bodies on the way and end on parking orbits, and the cost per frame.
    public partial class CareerQA
    {
        static string Names(Traffic tr)
        {
            var n = new List<string>();
            foreach (var t in tr.Flying) n.Add(t.Name);
            n.Sort(StringComparer.Ordinal);
            return string.Join(",", n);
        }

        // the ships in one set and not the other, with their departure and arrival in hours from jd
        static string Diff(Traffic a, Traffic b, double jd)
        {
            var d = new List<string>();
            void One(Traffic x, Traffic y, string sign)
            {
                var names = new HashSet<string>();
                foreach (var t in y.Flying) names.Add(t.Name);
                foreach (var t in x.Flying)
                    if (!names.Contains(t.Name)) d.Add($"{sign}{t.Name} {t.From.Name}>{t.To.Name} dep {(t.Dep - jd) * 24:+0.00;-0.00} h arr {(t.Arr - jd) * 24:+0.00;-0.00} h");
            }
            One(a, b, "+"); One(b, a, "-");
            return string.Join("; ", d);
        }

        IEnumerator TrafficChecks()
        {
            var qs = SolarSystem.Build(); // a system of its own, so the checks add no ships to the scene's
            double j0 = jd;
            long day0 = (long)Math.Floor(j0);

            string Trips(Traffic tr)
            {
                var sb = new System.Text.StringBuilder();
                for (long d = day0; d < day0 + 3; d++)
                    foreach (var t in tr.Day(d)) sb.Append($"{t.Name} {t.From.Name}>{t.To.Name} {t.Dep:R}-{t.Arr:R};");
                return sb.ToString();
            }
            string a1 = Trips(new Traffic(qs, 1)), a2 = Trips(new Traffic(qs, 1)), b1 = Trips(new Traffic(qs, 2));
            Check(a1.Length > 0 && a1 == a2 && a1 != b1, "traffic: the same seed and day give the same trips, another seed gives others");

            // A reset looks back LookbackDays for the ships still in flight, so no trip may take longer. One day a week over
            // a Jupiter–Saturn synodic period (about 20 years) meets their widest spacing.
            var scan = new Traffic(qs, 1);
            Traffic.Trip longest = null;
            var swScan = Stopwatch.StartNew();
            for (long d = day0; d < day0 + 20 * 366; d += 7)
                foreach (var t in scan.Day(d))
                    if (longest == null || t.Arr - t.Dep > longest.Arr - longest.Dep) longest = t;
            Check(longest != null && longest.Arr - longest.Dep < Traffic.LookbackDays,
                $"traffic: the longest trip in 20 years ({longest?.From.Name} > {longest?.To.Name}, {longest?.Name}) takes {(longest != null ? longest.Arr - longest.Dep : 0):F1} days, inside the {Traffic.LookbackDays:0}-day lookback ({swScan.Elapsed.TotalMilliseconds:F0} ms)");

            // the cost of a trip: drawing it, its Rail and the cheap clearance test
            var cost = new Traffic(qs, 3);
            var sw = Stopwatch.StartNew();
            for (long d = day0; d < day0 + 5; d++)
                foreach (var t in cost.Day(d)) cost.Ready(t);
            double perTrip = sw.Elapsed.TotalMilliseconds / Math.Max(cost.Checked, 1);

            // stepped one 1M× frame (60 fps) at a time for 30 days, against a fresh set at the end
            const double Frame1M = 1e6 / 60 / 86400; // days
            var stepped = new Traffic(qs, 1);
            sw.Restart(); stepped.Update(j0); double resetMs = sw.Elapsed.TotalMilliseconds;
            int frames = 0; double total = 0, worst = 0, j = j0;
            while (j < j0 + 30)
            {
                j += Frame1M;
                sw.Restart(); stepped.Update(j);
                double ms = sw.Elapsed.TotalMilliseconds;
                total += ms; worst = Math.Max(worst, ms); frames++;
            }
            var fresh = new Traffic(qs, 1);
            fresh.Update(j);
            int flying = stepped.Flying.Count;
            Check(flying > 0 && Names(stepped) == Names(fresh), $"traffic: stepped a 1M× frame at a time for 30 days, the ships in flight match a fresh schedule ({flying} ships)");
            log.WriteLine($"  traffic: {stepped.Checked} courses built, {stepped.Refused} refused by the cheap clearance test; {perTrip:F2} ms a trip");
            Check(flying >= 50 && flying <= 200, $"traffic: {flying} ships in flight (about 100 by design)");
            Check(total / frames < 1 && resetMs < 200,
                $"traffic: {total / frames:F2} ms a 1M× frame on average, {worst:F1} ms at worst; a reset (scene start, load) {resetMs:F0} ms");

            // every course starts and ends on a parking orbit, and the full clearance test passes what the cheap one passed
            int n = 0, onOrbit = 0, sunFail = 0;
            var missed = new SortedDictionary<string, int>();
            var why = new List<string>();
            for (long d = day0; n < 400; d++)
                foreach (var t in stepped.Day(d))
                {
                    if (n >= 400) break;
                    if (!stepped.Ready(t)) continue;
                    n++;
                    var r = t.Rail;
                    double s0 = (r.Pos(t.Dep) - qs.PosAt(r.From, t.Dep)).Mag / Ship.Standoff(r.From);
                    double s1 = (r.Pos(t.Arr) - qs.PosAt(r.To, t.Arr)).Mag / Ship.Standoff(r.To);
                    if (Math.Abs(s0 - 1) < 1e-3 && Math.Abs(s1 - 1) < 1e-3) onOrbit++;
                    var full = r.Check();
                    if (full.Clear) continue;
                    if (full.SunAu < Rail.MinSunAu) sunFail++;
                    else { missed.TryGetValue(full.Nearest.Name, out int k); missed[full.Nearest.Name] = k + 1; }
                    var cheap = r.CheckEnds();
                    if (why.Count < 3)
                        why.Add($"{t.Name} {t.From.Name}>{t.To.Name}: full {full.Nearest?.Name} {full.Ratio:F4} / {full.SunAu:F3} AU, cheap {cheap.Nearest?.Name} {cheap.Ratio:F4} / {cheap.SunAu:F3} AU");
                }
            var miss = new List<string>();
            foreach (var kv in missed) miss.Add($"{kv.Key} {kv.Value}");
            foreach (var w in why) log.WriteLine("    " + w);
            Check(onOrbit == n, $"traffic: {onOrbit} of {n} courses start and end on a parking orbit");
            Check(sunFail == 0 && missed.Count == 0,
                $"traffic: of {n} courses the cheap test passed, the full clearance test refuses {sunFail} near the Sun and {Count(missed)} through a body" + (missed.Count > 0 ? $" ({string.Join(", ", miss)})" : ""));
            stepped.Clear(); fresh.Clear();

            // the scene's own traffic, after 5 s at 1M×, matches the schedule and is in the system. 5 s holds at least one
            // whole -fowprof window (2 s) between the two marks.
            Put("warpIdx", 6);
            UnityEngine.Debug.Log("CareerQA traffic: 1M× scene start");
            float t0 = Time.realtimeSinceStartup; int sceneFrames = 0;
            while (Time.realtimeSinceStartup - t0 < 5) { sceneFrames++; yield return null; }
            UnityEngine.Debug.Log("CareerQA traffic: 1M× scene end");
            Put("warpIdx", 0);
            var live = s.TrafficNow;
            var check = new Traffic(qs, 1);
            check.Update(jd);
            bool inSys = live != null;
            if (live != null) foreach (var t in live.Flying) inSys &= t.Body != null && sys.Bodies.Contains(t.Body);
            Check(live != null && live.Flying.Count > 0 && inSys && Names(live) == Names(check),
                $"traffic: the scene's ships in flight after 5 s at 1M× match the schedule ({live?.Flying.Count ?? 0} ships, {(inSys ? "all" : "not all")} in the system; {sceneFrames} frames)"
                + (live != null && Names(live) != Names(check) ? $" (scene against schedule: {Diff(live, check, jd)})" : ""));
            check.Clear();
        }

        static int Count(SortedDictionary<string, int> d) { int c = 0; foreach (var v in d.Values) c += v; return c; }
    }
}
