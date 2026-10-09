using System;
using System.Collections;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // Career QA sections 9-10: a rescue (match a drifting derelict, hold while the crew crosses) and an intercept (catch a
    // runner under thrust between two ports), with the runner's rail, refusal and dry-tank checks.
    public partial class CareerQA
    {
        IEnumerator RescueAndInterceptChecks()
        {
            // 9. a rescue: fly to a drifting ship on its own orbit, match it, hold while the crew crosses
            State("before rescue");
            Check(career.Docked?.Name == "Luna", "docked at Luna again");
            if (career.Docked != null)
            {
                career.BuyFuel(ship, 1000);
                var res = career.Board.Find(c => c.Kind == JobKind.Rescue) ?? career.Rescue(new System.Random(7), sys, jd);
                career.Accept(res, jd);
                var wreck = sys[res.To];
                Check(wreck != null && Career.IsDerelict(wreck), $"derelict is in the system: {res.To} around {res.Parent}");
                double c3 = career.Credits;
                // closest approach, and which side it came in on: a frame at warp can hide a pass through the wreck
                double wreckMin = double.MaxValue; D3 wreckSide = D3.Zero;
                yield return Trip(wreck, 0, "rescue " + res.To, () =>
                {
                    var rel = ship.Pos - sys.Pos(wreck, jd);
                    wreckMin = Math.Min(wreckMin, rel.Mag);
                    if (wreckSide.Sqr == 0 && rel.Mag < 50) wreckSide = rel.Norm;
                    return res.Dwell > 0 || !career.Active.Contains(res);
                }, 4, 120);
                var wreckRel = ship.Pos - sys.Pos(wreck, jd);
                Check(wreckMin > 0.3 && wreckRel.Mag < 3 && D3.Dot(wreckSide, wreckRel.Norm) > 0,
                    $"the ship stops beside the wreck, not through it: closest {wreckMin:F2} km, {wreckRel.Mag:F2} km as the crew crosses, same side {D3.Dot(wreckSide, wreckRel.Norm):F2}");
                // a close look at the wreck while the crew crosses over
                Call("SetPivot", Enum.ToObject(s.GetType().GetNestedType("Pivot", F), 1), 0.12);
                yield return Frames(1.5f);
                yield return Shot("c08-derelict");
                Call("SetPivot", Enum.ToObject(s.GetType().GetNestedType("Pivot", F), 0), 0.16);
                yield return Fly(() => !career.Active.Contains(res), 3, 60);
                State("after rescue");
                Check(!career.Active.Contains(res) && career.Credits > c3 && sys[res.To] == null, $"crew rescued and paid ({career.Credits - c3:N0} cr), wreck removed");
                yield return Shot("c09-rescued");
            }

            // 10. an intercept: back to Luna, then catch a ship under thrust between two inner ports
            ship.MaxG = 1; // the arrival tests left it at 0.3 g
            yield return Trip(sys.Moon, 0, "back to Luna", () => career.Docked?.Name == "Luna" && ship.Phase == "in orbit", 3, 60);
            Check(career.Docked?.Name == "Luna", "docked at Luna for the intercept");
            if (career.Docked != null)
            {
                career.BuyFuel(ship, 1000);
                var inner = new[] { "Earth orbit", "Luna", "Mars orbit" };
                Contract ic = null;
                for (int seed = 1; seed < 500 && ic == null; seed++)
                {
                    var cand = career.Intercept(new System.Random(seed), sys, jd);
                    if (cand != null && Array.IndexOf(inner, cand.RunFrom) >= 0 && Array.IndexOf(inner, cand.RunTo) >= 0 && cand.RunTo != "Luna") ic = cand;
                }
                Check(ic != null, "an inner-system intercept can be posted");

                // QA-064: a runner nearly at its port can't be caught in time, so it is neither posted nor accepted
                var postedRunners = new System.Collections.Generic.List<Contract>();
                if (ic != null)
                {
                    for (int seed = 1; seed < 400; seed++) { var p = career.Intercept(new System.Random(seed), sys, jd); if (p != null) postedRunners.Add(p); }
                    int posted = postedRunners.Count;
                    var lateJob = JsonUtility.FromJson<Contract>(JsonUtility.ToJson(ic));
                    lateJob.Id += 200000; lateJob.To += "L";
                    double shift = (ic.T1 - jd) * 0.98;
                    lateJob.T0 -= shift; lateJob.T1 -= shift; lateJob.Days = lateJob.T1 - jd;
                    var lateRunner = career.TargetOf(lateJob);
                    bool lateOk = career.CanAccept(lateJob, out string lateWhy);
                    bool lateCatch = lateRunner != null && career.Catchable(lateJob, lateRunner, sys, jd);
                    Check(career.CanAccept(ic, out string icWhy), $"QA-064: the posted intercept can be accepted ({icWhy ?? "ok"}), {posted} posted from 399 seeds");
                    Check(lateRunner != null && !lateCatch && !lateOk && lateWhy != null && lateWhy.Contains("catch"),
                        $"QA-064: a runner {(lateJob.T1 - jd) * 24:F1} h from {lateJob.RunTo} is refused: {lateWhy}");
                }

                // W2 rails (Rail.cs, docs/design-world.md): runner courses between every pair of ports, every 60 days for two
                // years. Some pass too near the Sun or through another body's parking orbit; the boards post none of those.
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    int n = 0, nearSun = 0, throughBody = 0;
                    double sunMin = double.MaxValue, ratioMin = double.MaxValue;
                    string sunWorst = "", bodyWorst = "";
                    var byBody = new System.Collections.Generic.SortedDictionary<string, int>();
                    for (int k = 0; k < 13; k++)
                    {
                        double t0 = jd + 60 * k;
                        foreach (var a in Career.Ports)
                            foreach (var z in Career.Ports)
                            {
                                if (a == z) continue;
                                Body ba = sys[a.Body], bz = sys[z.Body];
                                double T = Math.Sqrt(6 * (sys.PosAt(bz, t0) - sys.PosAt(ba, t0)).Mag / (Career.RunnerG * Ship.G0)) / 86400;
                                var cl = new Rail(sys, ba, bz, t0, t0 + T).Check();
                                n++;
                                string at = $"{a.Name} → {z.Name} on {Frame.UtcFromTdb(t0):yyyy-MM-dd}";
                                if (cl.SunAu < Rail.MinSunAu) { nearSun++; if (cl.SunAu < sunMin) { sunMin = cl.SunAu; sunWorst = at; } }
                                if (cl.Ratio < 0.99)
                                {
                                    throughBody++;
                                    byBody.TryGetValue(cl.Nearest.Name, out int h); byBody[cl.Nearest.Name] = h + 1;
                                    if (cl.Ratio < ratioMin) { ratioMin = cl.Ratio; bodyWorst = $"{at}, {cl.Nearest.Name} at {cl.Ratio:P0} of its parking orbit"; }
                                }
                            }
                    }
                    var hit = new System.Collections.Generic.List<string>();
                    foreach (var kv in byBody) hit.Add($"{kv.Key} {kv.Value}");
                    log.WriteLine($"  rails: {n} runner courses ({Career.Ports.Length} ports, 13 dates 60 days apart), {watch.Elapsed.TotalMilliseconds / n:F1} ms to build and check each");
                    log.WriteLine($"    {nearSun} pass within {Rail.MinSunAu} AU of the Sun" + (nearSun > 0 ? $"; closest {sunMin:F3} AU, {sunWorst}" : ""));
                    log.WriteLine($"    {throughBody} cross another body's parking orbit" + (throughBody > 0 ? $" ({string.Join(", ", hit)}); worst {bodyWorst}" : ""));
                    Check(nearSun + throughBody > 0, $"rails: the clearance test finds courses to refuse ({nearSun} near the Sun, {throughBody} through a body)");

                    int clear = 0, onOrbit = 0;
                    foreach (var p in postedRunners)
                    {
                        var r = Career.RunnerRail(p, sys);
                        if (r.Check().Clear) clear++;
                        double s0 = (r.Pos(p.T0) - sys.PosAt(r.From, p.T0)).Mag / Ship.Standoff(r.From), s1 = (r.Pos(p.T1) - sys.PosAt(r.To, p.T1)).Mag / Ship.Standoff(r.To);
                        if (Math.Abs(s0 - 1) < 1e-3 && Math.Abs(s1 - 1) < 1e-3) onOrbit++;
                    }
                    Check(postedRunners.Count > 0 && clear == postedRunners.Count && onOrbit == postedRunners.Count,
                        $"rails: of {postedRunners.Count} posted runners, {clear} keep clear of the Sun and the bodies on the way, {onOrbit} start and end on a parking orbit");
                }

                // QA-041 (1): a job the tank can't fly is refused on accept
                if (ic != null)
                {
                    double keep = ship.Fuel;
                    ship.Fuel = 3;
                    yield return null;
                    bool ok = career.CanAccept(ic, out string why);
                    Check(!ok && why != null && why.Contains("needs"), $"infeasible intercept refused: {why}");
                    ship.Fuel = keep;
                    yield return null;
                }

                // QA-041 (2): a 204 km/s flyby within 500 km never pays, even at 100k×
                if (ic != null)
                {
                    var fly = ic;
                    var copy = JsonUtility.FromJson<Contract>(JsonUtility.ToJson(fly)); copy.Id += 100000; copy.To += "x";
                    career.Accept(copy, jd);
                    var rb = sys[copy.To];
                    double cf = career.Credits;
                    var keepPos = ship.Pos; var keepVel = ship.Vel;
                    ship.Plan = Plan.None; ship.Landed = null;
                    Put("warpIdx", 5);
                    for (int f = 0; f < 30 && rb != null; f++)
                    {
                        var rv = Career.MakeRunner(copy, sys);
                        var tv = (rv.Path(jd + 30.0 / 86400) - rv.Path(jd - 30.0 / 86400)) / 60.0;
                        var along = tv.Mag > 0 ? tv.Norm : new D3(1, 0, 0);
                        ship.Pos = rb.Path(jd) + along * 100; ship.Vel = tv + along * 204; // passing at 204 km/s, 100 km off
                        yield return null;
                    }
                    Put("warpIdx", 0);
                    Check(career.Credits == cf && career.Active.Contains(copy) && copy.Dwell == 0, $"204 km/s flyby does not pay (dwell {copy.Dwell:F0} s)");
                    career.Abandon(copy);
                    ship.Pos = keepPos; ship.Vel = keepVel;
                    yield return null;
                }

                // QA-041 (3): a dry tank fails a rendezvous job, drops time compression and stops the autopilot
                if (ic != null)
                {
                    var dry = ic;
                    var copy = JsonUtility.FromJson<Contract>(JsonUtility.ToJson(dry)); copy.Id += 200000; copy.To += "y";
                    career.Accept(copy, jd);
                    Call("SetTarget", sys[copy.To]);
                    Put("selectedPlan", Plan.Brachistochrone);
                    Call("TogglePlan");
                    Put("warpIdx", 5);
                    double keep = ship.Fuel;
                    yield return null;
                    ship.Fuel = 0;
                    yield return null; yield return null;
                    Check(!career.Active.Contains(copy) && Get<int>("warpIdx") == 0 && !ship.PlanActive && sys[copy.To] == null,
                        $"dry tank: job failed, warp {Get<int>("warpIdx")}, plan {(ship.PlanActive ? "on" : "off")}");
                    Check(career.Notes.Exists(n => n.Text.Contains(career.Docked == null ? "emergency tender" : "on credit")),
                        $"dry tank offers the emergency tender, or the port's credit when docked ({career.Docked?.Name ?? "not docked"})");
                    ship.Fuel = keep;
                    ship.Plan = Plan.None;
                    Call("SetTarget", sys.Moon);
                    yield return Trip(sys.Moon, 0, "back to Luna again", () => career.Docked?.Name == "Luna" && ship.Phase == "in orbit", 3, 60);
                }
                if (ic != null)
                {
                    var probe = Career.MakeRunner(ic, sys);
                    Body rf = sys[Career.PortAt(ic.RunFrom).Body], rt = sys[Career.PortAt(ic.RunTo).Body];
                    double e0 = (probe.Path(ic.T0 + 1e-7) - sys.PosAt(rf, ic.T0)).Mag - Ship.Standoff(rf);
                    double e1 = (probe.Path(ic.T1 - 1e-7) - sys.PosAt(rt, ic.T1)).Mag - Ship.Standoff(rt);
                    double peak = Career.RunnerPeakG(probe, ic);
                    Check(Math.Abs(e0) < 50 && Math.Abs(e1) < 50 && peak > 0.1 && peak < 0.4,
                        $"runner course: leaves {ic.RunFrom} from its parking orbit ({Ship.Standoff(rf):N0} km out, {e0:+0.0;-0.0} km), reaches {ic.RunTo}'s ({Ship.Standoff(rt):N0} km, {e1:+0.0;-0.0} km), peak {peak:F3} g");
                    career.Accept(ic, jd);
                    var runner = sys[ic.To];
                    // QA-065: the line drawn for it is one smooth course from port to port, not chords across an orbit
                    Call("SetTarget", runner);
                    yield return null; yield return null;
                    string course = s.TestTargetLineReport(out double longest);
                    Check(longest > 0 && longest < 0.05, $"QA-065: runner course line: {course}");
                    double c4 = career.Credits;
                    double runnerMin = double.MaxValue; D3 runnerSide = D3.Zero;
                    yield return Trip(runner, 0, "intercept " + ic.To, () =>
                    {
                        var rel = ship.Pos - sys.Pos(runner, jd);
                        runnerMin = Math.Min(runnerMin, rel.Mag);
                        if (runnerSide.Sqr == 0 && rel.Mag < 50) runnerSide = rel.Norm;
                        return ic.Dwell > 0 || !career.Active.Contains(ic);
                    }, 5, 150);
                    var runnerRel = ship.Pos - sys.Pos(runner, jd);
                    Check(runnerMin > 0.3 && runnerRel.Mag < 3 && D3.Dot(runnerSide, runnerRel.Norm) > 0,
                        $"the ship stops beside the runner, not through it: closest {runnerMin:F2} km, {runnerRel.Mag:F2} km as the cargo crosses, same side {D3.Dot(runnerSide, runnerRel.Norm):F2}");
                    Call("SetPivot", Enum.ToObject(s.GetType().GetNestedType("Pivot", F), 1), 0.2);
                    Put("warpIdx", 0);
                    yield return Frames(1.5f);
                    yield return Shot("c10-intercept");
                    Call("SetPivot", Enum.ToObject(s.GetType().GetNestedType("Pivot", F), 0), 0.16);
                    yield return Fly(() => !career.Active.Contains(ic), 2, 60);
                    State("after intercept");
                    Check(!career.Active.Contains(ic) && career.Credits > c4 && sys[ic.To] == null, $"cargo handed over in flight and paid ({career.Credits - c4:N0} cr)");
                    Check(ship.Target == null && !ship.PlanActive, "after the handover the autopilot lets the runner go");
                }
            }
        }
    }
}
