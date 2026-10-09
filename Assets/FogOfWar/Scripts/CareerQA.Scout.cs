using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;
using UIE = UnityEngine.UIElements;

namespace FogOfWar
{
    // The 16j checks, moved out of CareerQA.Stealth.cs to keep it under 800 lines; StealthChecks there guards and runs them.
    public partial class CareerQA
    {
        // 16j: the quiet watch (Career.Scout.cs, docs/design-combat.md Phase 3c): a survey with Quiet counts only the seconds
        // of an update that no raider could see. Updates of 3,600 s on a Pallas survey bench, the ship in scan range and at rest
        // with it; every plain control is an identical bench with Quiet off. A quiet or seen time is worked from three dates,
        // and a date is a double near 2.46e6 with a grid of 2^-31 d = 40 µs, so those carry tol = 1e-3 s, not the brief's 1e-6 s.
        // The plain controls and the full-credit dwell are exact (the whole update in range, no burn, seen 0) and keep 1e-6 s.
        void StealthScoutChecks(SolarSystem qs)
        {
            const double tol = 1e-3, exact = 1e-6;
            var (_, at, vel) = Belt(qs);
            var pallas = qs[Career.ReconBody];
            Check(pallas != null, $"stealth scout: the recon body {Career.ReconBody} is in the system");
            if (pallas == null) return;
            var live = new List<Bench>();
            double km = 0;
            Bench Make(bool stowable, bool quiet)
            {
                var b = SigBench(qs, at, vel, JobKind.Survey, Career.ReconBody);
                if (stowable) { b.C.Build.pick[(int)Slot.Radiators] = 1; b.C.Apply(b.Ship); }
                b.Job.Quiet = quiet;
                live.Add(b);
                return b;
            }
            // a warm-up update puts the ship in range, so the measured one starts there; the dwell and the seen seconds are its own
            double Run(Bench b, double loudS, double lastH, out double seen, Action before = null)
            {
                SigUpdate(b, 0, double.NaN, pallas, km);
                b.Job.Dwell = 0;
                before?.Invoke();
                SigUpdate(b, loudS, lastH, pallas, km);
                seen = b.C.SeenSeconds(b.Ship, b.Jd, 3600);
                return b.Job.Dwell;
            }
            try
            {
                var probe = Make(false, true);
                double radius = pallas.Radius, range = probe.C.Codex.ScanRange(pallas);
                int level = probe.C.Codex.SensorLevel, mult = level > 0 ? 2 : 1;
                km = radius + (range - radius) / 2;
                Check(km > radius && km < range && level == 0,
                    $"stealth scout: Pallas has radius {radius:N1} km and scan range {range:N1} km; the ship sits {km:N1} km out at rest with it (inside the range, above the radius); stock sensors level {level} (0), dwell counts x{mult} (1)");

                // the 2 h hold ends 1,800 s into the update: half of it is unseen
                var q1 = Make(false, true);
                double d1 = Run(q1, 0, -1.5, out double s1);
                var p1 = Make(false, false);
                double e1 = Run(p1, 0, -1.5, out double t1);
                Check(Near(d1, 1800 * mult, tol * mult) && Near(e1, 3600 * mult, exact * mult) && Near(s1, 1800, tol),
                    $"stealth scout: stock hull, the last loud moment 1.5 h before the update: quiet dwell {d1:0.000000} s (1,800 ± {tol * mult}), seen {s1:0.000000} s (1,800 ± {tol}), plain control {e1:0.000000} s (3,600 ± {exact * mult}, seen {t1:0.000000})");

                // a burn just ended at the update's start: all of it is seen
                var q2 = Make(false, true);
                double d2 = Run(q2, 0, 0, out double s2);
                var p2 = Make(false, false);
                double e2 = Run(p2, 0, 0, out _);
                Check(Near(d2, 0, tol * mult) && Near(e2, 3600 * mult, exact * mult) && Near(s2, 3600, tol),
                    $"stealth scout: stock hull, a burn just ended: quiet dwell {d2:0.000000} s (0 ± {tol * mult}), seen {s2:0.000000} s (3,600 ± {tol}), plain control {e2:0.000000} s (3,600 ± {exact * mult})");

                // 1,800 loud seconds, then the hold's 1,800 s: the two cover the update
                var q3 = Make(false, true);
                double d3 = Run(q3, 1800, -1.5, out double s3);
                var p3 = Make(false, false);
                double e3 = Run(p3, 1800, -1.5, out _);
                Check(Near(d3, 0, tol * mult) && Near(e3, 3600 * mult, exact * mult) && Near(s3, 3600, tol),
                    $"stealth scout: stock hull, 1,800 loud seconds in the update and the hold's 1,800 s: quiet dwell {d3:0.000000} s (0 ± {tol * mult}), seen {s3:0.000000} s (3,600 ± {tol}), plain control {e3:0.000000} s (3,600 ± {exact * mult}: the scan takes off burn seconds only, and this update has none)");

                // 40 min after a burn: the stowed hold (2,160 s) has run out, the stock one (7,200 s) has not
                var q4 = Make(true, true);
                double d4 = Run(q4, 0, -40.0 / 60, out double s4);
                bool stowed = q4.C.Stowed(q4.Ship, q4.Jd);
                double hold = q4.C.HoldS(q4.Ship);
                var p4 = Make(true, false);
                double e4 = Run(p4, 0, -40.0 / 60, out _);
                Check(Near(d4, 3600 * mult, exact * mult) && Near(e4, 3600 * mult, exact * mult) && Near(s4, 0, tol) && stowed && Near(hold, 2160, 1e-9),
                    $"stealth scout: stowable hull, 40 min after a burn: quiet dwell {d4:0.000000} s (3,600 ± {exact * mult}), seen {s4:0.000000} s (0 ± {tol}), plain control {e4:0.000000} s (3,600 ± {exact * mult}), stowed {stowed} (True), hold {hold:0.000000000} s (2,160 ± 1e-9)");
                var k4 = Make(false, true);
                double d4k = Run(k4, 0, -40.0 / 60, out double s4k);
                double hold4k = k4.C.HoldS(k4.Ship);
                Check(Near(d4k, 0, tol * mult) && Near(s4k, 3600, tol) && Near(hold4k, Signature.TrackHoldS, 1e-9),
                    $"stealth scout: stock hull, 40 min after a burn: quiet dwell {d4k:0.000000} s (0 ± {tol * mult}), seen {s4k:0.000000} s (3,600 ± {tol}), hold {hold4k:0.0} s (7,200)");

                // a chase on: a raider watches the ship whatever its hold says. Scan runs before Contacts in Tick, so the chase is
                // still on when the dwell is credited, even if the lose rule ends it later in the update (st5 logs which)
                Encounter ch = null;
                var q5 = Make(true, true);
                double d5 = Run(q5, 0, double.NaN, out _, () => ch = q5.C.StartContact(q5.Job, qs, q5.Ship, q5.Jd, q5.Ship.Pos - q5.Ship.Pos.Norm * 1e5, q5.Ship.Vel));
                string st5 = q5.C.Encounter == null ? "gone" : q5.C.Encounter == ch ? "on" : "other";
                q5.C.DropContact();
                var p5 = Make(true, false);
                double e5 = Run(p5, 0, double.NaN, out _, () => p5.C.StartContact(p5.Job, qs, p5.Ship, p5.Jd, p5.Ship.Pos - p5.Ship.Pos.Norm * 1e5, p5.Ship.Vel));
                p5.C.DropContact();
                var n5 = Make(true, true);
                double dn5 = Run(n5, 0, double.NaN, out _);
                Check(Near(d5, 0, tol * mult) && e5 > 0 && Near(dn5, 3600 * mult, exact * mult),
                    $"stealth scout: stowable hull, never burned, a chase on for the update (afterwards {st5}): quiet dwell {d5:0.000000} s (0 ± {tol * mult}), plain control {e5:0.000000} s (above 0), the same hull with no chase {dn5:0.000000} s (3,600 ± {exact * mult})");

                // the ship leaves the range about 3/4 into the update (the Hermite curve is a smoothstep, S(0.75) = 0.84375 of the way
                // out), and the first 1,800 s are seen: the in-range seconds lie inside the seen ones up to 1,800, so only q - seen count.
                // The whole-update rule would credit min(q, 3,600 - seen) = 1,800 here
                double kmOut = km + (range - km) / 0.84375;
                double Leave(Bench b, out double seen)
                {
                    SigUpdate(b, 0, double.NaN, pallas, km);
                    b.Job.Dwell = 0;
                    SigUpdate(b, 0, -1.5, pallas, kmOut);
                    seen = b.C.SeenSeconds(b.Ship, b.Jd, 3600);
                    return b.Job.Dwell;
                }
                var q6 = Make(false, true);
                double d6 = Leave(q6, out double s6);
                var p6 = Make(false, false);
                double e6 = Leave(p6, out _);
                double inRange = e6 / mult, want6 = Math.Max(0, inRange - s6) * mult, old6 = Math.Min(inRange, 3600 - s6) * mult;
                Check(inRange > 1900 && inRange < 3500 && Near(s6, 1800, tol) && Near(d6, want6, tol * mult) && d6 < old6 - 100,
                    $"stealth scout: stock hull leaving the range ({kmOut:N1} km out at the update's end), the last loud moment 1.5 h before it: in range {inRange:0.000} s (1,900 to 3,500), seen {s6:0.000000} s (1,800 ± {tol}), quiet dwell {d6:0.000000} s (in range - seen = {want6:0.000000} ± {tol * mult}, under the whole-update rule's {old6:0.000000} by over 100)");

                // landed on the target: no raider reaches a landed ship (RouteTick, Contacts), so the hold after the landing burn does not count
                var q7 = Make(false, true);
                double d7 = Run(q7, 0, 0, out double s7, () => q7.Ship.Landed = pallas);
                Check(Near(d7, 3600 * mult, exact * mult) && Near(s7, 3600, tol),
                    $"stealth scout: stock hull landed on {Career.ReconBody}, a burn just ended: quiet dwell {d7:0.000000} s (3,600 ± {exact * mult}; with the ship aloft check 2 has 0), seen {s7:0.000000} s (3,600 ± {tol})");
            }
            finally { foreach (var b in live) b.C.DropContact(); }

            StealthScoutBoardChecks(qs, at, vel);
            StealthScoutSaveChecks(qs, at, vel);
            string line = Career.QuietLine;
            Check(!line.Contains("oud") && !line.Contains("\n") && line.Length > 0,
                $"stealth scout: the quiet line is {line.Length} chars, one line, with no \"oud\" (the save scan of 15 reads for it): \"{line}\"");
        }

        // 16j, the board: slot 8 of Ceres and Vesta over 40 days, and no such slot elsewhere
        void StealthScoutBoardChecks(SolarSystem qs, D3 at, D3 vel)
        {
            const int days = 40;
            var live = new List<Bench>();
            try
            {
                foreach (string name in new[] { "Ceres", "Vesta" })
                {
                    var b = BoardBench(qs, at, vel, name, live);
                    int posts = 0, bad = 0;
                    double lo = double.MaxValue, hi = 0;
                    string first = "-", others = "-";
                    for (int i = 0; i < days; i++)
                    {
                        b.Jd = PursuitCheck.Jd0 + i;
                        b.C.Tick(qs, b.Ship, b.Jd, 0);
                        if (name == "Ceres" && i == 0)
                        {
                            var rest = new List<string>();
                            foreach (var j in b.C.Board) if (!j.Key.EndsWith("/8")) rest.Add($"{j.Key} {j.Title}");
                            others = string.Join("; ", rest);
                        }
                        foreach (var j in b.C.Board)
                        {
                            if (!j.Key.EndsWith("/8")) continue;
                            posts++;
                            lo = Math.Min(lo, j.Pay); hi = Math.Max(hi, j.Pay);
                            double wait = (Career.DwellSeconds + Signature.TrackHoldS) / 86400;
                            bool ok = j.Kind == JobKind.Survey && j.Quiet && j.To == Career.ReconBody && j.From == name && j.DwellNeeded == Career.DwellSeconds
                                      && j.Title == "Quiet watch on " + Career.ReconBody && !j.Title.Contains("oud") && j.Key == $"{name}/{(long)Math.Floor(b.Jd)}/8"
                                      && j.Pay % 100 == 0 && j.Pay >= 1500 && j.Days > wait && j.ExpressDays > 0 && j.ExpressDays <= j.Days;
                            if (!ok) bad++;
                            if (first == "-") first = $"{j.Key} \"{j.Title}\" {j.Pay:N0} cr, {j.Days:0.000} d, express {j.ExpressDays:0.000} d";
                        }
                    }
                    string pays = posts > 0 ? $"{lo:N0} to {hi:N0}" : "-";
                    Check(posts >= 10 && posts <= 30 && bad == 0,
                        $"stealth scout: {name} posts the quiet watch on {posts} of {days} days (10 to 30), {bad} of them wrong (0: a Survey, Quiet, to {Career.ReconBody}, dwell {Career.DwellSeconds:N0} s, key ending /8, pay a multiple of 100 and at least 1,500, days over the wait); pay {pays} cr; first: {first}");
                    if (name == "Ceres") log.WriteLine($"    stealth scout: Ceres on its first day, the other slots: {others}");
                }
                foreach (string name in new[] { "Mars orbit", "Earth orbit", "Ganymede" })
                {
                    var b = BoardBench(qs, at, vel, name, live);
                    int eights = 0, quiets = 0, jobs = 0;
                    for (int i = 0; i < days; i++)
                    {
                        b.Jd = PursuitCheck.Jd0 + i;
                        b.C.Tick(qs, b.Ship, b.Jd, 0);
                        foreach (var j in b.C.Board) { jobs++; if (j.Key.EndsWith("/8")) eights++; if (j.Quiet) quiets++; }
                    }
                    Check(eights == 0 && quiets == 0 && jobs > 0,
                        $"stealth scout: {name} over {days} days posts {jobs} jobs, {eights} in slot 8 (0), {quiets} quiet (0)");
                }
            }
            finally { foreach (var b in live) b.C.DropContact(); }
        }

        // a career docked at a port by the real path (FindDock lands a ship on a port body), with no job aboard
        static Bench BoardBench(SolarSystem qs, D3 at, D3 vel, string port, List<Bench> live)
        {
            var b = MakeBench(qs, at, vel);
            b.C.Active.Clear();
            b.Ship.Landed = qs[Array.Find(Career.Ports, p => p.Name == port).Body];
            live.Add(b);
            return b;
        }

        // 16j, the save: an old contract has no Quiet, a quiet one keeps it
        void StealthScoutSaveChecks(SolarSystem qs, D3 at, D3 vel)
        {
            var old = JsonUtility.FromJson<Contract>("{\"Id\":1,\"Kind\":2,\"Title\":\"x\"}");
            var quiet = new Contract { Id = 2, Kind = JobKind.Survey, Title = "Quiet watch on Pallas", To = Career.ReconBody, DwellNeeded = Career.DwellSeconds, Quiet = true, Key = "qa/scout-save" };
            string cj = JsonUtility.ToJson(quiet);
            var back = JsonUtility.FromJson<Contract>(cj);
            bool inJson = cj.Contains("\"Quiet\":true");
            Check(old != null && !old.Quiet && back != null && back.Quiet && inJson,
                $"stealth scout: an old contract loads with Quiet {old?.Quiet} (False); a quiet one goes through JsonUtility ({cj.Length} chars) and comes back with Quiet {back?.Quiet} (True), the JSON holds \"Quiet\":true {inJson} (True)");

            var b = SigBench(qs, at, vel, JobKind.Survey, Career.ReconBody);
            b.C.Active.Add(quiet);
            string json = JsonUtility.ToJson(b.C.ToSave(b.Ship));
            var c = new Career { Sys = qs };
            c.FromSave(JsonUtility.FromJson<CareerSave>(json), new Ship());
            var loaded = c.Active.Find(x => x.Id == quiet.Id);
            var plain = c.Active.Find(x => x.Id == b.Job.Id);
            string qText = loaded == null ? "missing" : loaded.Quiet.ToString(), pText = plain == null ? "missing" : plain.Quiet.ToString();
            bool noOud = !json.Contains("oud");
            Check(loaded != null && loaded.Quiet && plain != null && !plain.Quiet && noOud,
                $"stealth scout: a career save with an active quiet watch ({json.Length} chars) loads it with Quiet {qText} (True) beside a plain survey with Quiet {pText} (False); the save holds no \"oud\" ({noOud})");
        }

        static int CountText(UIE.VisualElement e, string text)
        {
            int n = e is UIE.Label l && l.text == text ? 1 : 0;
            foreach (var c in e.Children()) n += CountText(c, text);
            return n;
        }

        // 16j, the scene: the Jobs page's active card and board card print the quiet line (UI Toolkit). The page is built into
        // a box of its own, as 14b does; the board card needs a dock, which is set by hand and put back, with the yard's draft.
        void StealthScoutShellChecks()
        {
            if (UI.UiShell.Disabled)
            {
                log.WriteLine("  SKIP stealth scout: no UI Toolkit shell (-fowlegacyui): the quiet line on the Jobs page's cards; the classic panel draws it with GUILayout.Label and no QA reads IMGUI text");
                return;
            }
            if (!app.ShellActive) { Check(false, "stealth scout: no UI Toolkit shell, and -fowlegacyui is not on"); return; }
            log.WriteLine("  SKIP stealth scout: the classic panel's two lines (ActiveJobs, ContractBoard) are GUILayout.Label calls that no QA reads; the UI Toolkit cards are checked");
            var setDocked = typeof(Career).GetProperty("Docked").GetSetMethod(true);
            var dock0 = career.Docked;
            var draft0 = Get<ShipBuild>("yardDraft");
            var yardPort0 = Get<Port>("yardPort");
            var head0 = Get<UIE.Label>("yardHead");
            var act = new Contract { Id = 900600, Kind = JobKind.Survey, Title = "Quiet watch on Pallas", From = "Ceres", To = Career.ReconBody, Pay = 9000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, DwellNeeded = Career.DwellSeconds, Quiet = false, Key = "qa/scout-active" };
            var post = new Contract { Id = 900601, Kind = JobKind.Survey, Title = "Quiet watch on Pallas", From = "Ceres", To = Career.ReconBody, Pay = 9000, Days = 100, DwellNeeded = Career.DwellSeconds, Quiet = false, Key = "qa/scout-board" };
            int Lines()
            {
                var box = new UIE.VisualElement();
                Call("BuildJobs", box);
                return CountText(box, Career.QuietLine);
            }
            try
            {
                career.Active.Add(act);
                int base0 = Lines();
                act.Quiet = true;
                int active = Lines();

                setDocked.Invoke(career, new object[] { Array.Find(Career.Ports, p => p.Name == "Ceres") });
                act.Quiet = false;
                int baseD = Lines();
                act.Quiet = true;
                int activeD = Lines();
                career.Board.Add(post);
                post.Quiet = true;
                int both = Lines();
                act.Quiet = false; post.Quiet = false;
                int none = Lines();
                Check(active == base0 + 1 && activeD == baseD + 1 && both == baseD + 2 && none == baseD,
                    $"stealth scout: the Jobs page prints the quiet line under a quiet job's text: undocked, the active card {base0} -> {active} copies (+1); docked at Ceres {baseD} -> {activeD} with the active card (+1), {both} with the board card too (+2), {none} with both set plain (back to {baseD})");
            }
            finally
            {
                career.Board.Remove(post);
                career.Active.Remove(act);
                setDocked.Invoke(career, new object[] { dock0 });
                Put("yardDraft", draft0); Put("yardPort", yardPort0); Put("yardHead", head0);
                Put("jobsSig", null); // the page rebuilds from the real jobs
            }
        }
    }
}
