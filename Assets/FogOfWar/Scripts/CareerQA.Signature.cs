using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // 15. Signatures (Signature.cs, Career.Signature.cs, docs/design-combat.md Phase 2): the constants and ranges, the loud
    // seconds Ship.Step counts, the seen seconds and the chance, the roll and its gate (a hand-flown burn is raided, a survey
    // far from its body is not, a chase does not change), the planner's hours, then in the scene the Signature and Raiders
    // rows and the SEEN pill (the rows alone under -fowlegacyui), and the save. Headless parts run on a system of their own
    // at the pursuit check's date, the planner at the scene's date as 12 does. Every check logs what it measured: the [calc]
    // numbers of 15e were never run.
    public partial class CareerQA
    {
        IEnumerator SignatureChecks()
        {
            SolarSystem qs = null;
            if (Guard("signature system", () => qs = SolarSystem.Build()))
            {
                Guard("signature constants", SigConstantChecks);
                Guard("signature step", () => SigStepChecks(qs));
                Guard("signature seen", () => SigSeenChecks(qs));
                Guard("signature roll", () => SigRollChecks(qs));
                Guard("signature planner", () => SigPlannerChecks(qs));
            }
            yield return SigSceneChecks();
            yield return StealthChecks(); // 16: stowed radiators and the heat sink
        }

        // 15a
        void SigConstantChecks()
        {
            double g = Ship.G0, p500 = Signature.JetW(500, g);
            Check(Math.Abs(Signature.WindowKm - 1143848) <= 1 && Math.Abs(Signature.LoudW - 26.17e9) <= 0.05e9,
                $"signature: a raider's start window ends {Signature.WindowKm:N0} km out (1,143,848 ± 1), and a burn is loud from {Signature.LoudW / 1e9:0.000} GW (26.17 ± 0.05), {Signature.LoudW / p500:0.00000} g at 500 t");

            double Au(double jetW) => Signature.RangeKm(jetW) / Frame.AU;
            double cold = Signature.RangeKm(0), stock = Au(p500), torch = Au(Signature.JetW(500, 5 * g)), eco = Au(Signature.JetW(500, 0.5 * g));
            double fast = Au(Signature.JetW(500, 3 * g)), rcs = Au(Signature.JetW(500, Ship.RcsAccel));
            Check(Math.Abs(cold - 10000) < 1e-6 && Math.Abs(stock - 0.2340) <= 0.001 && Math.Abs(torch - 0.5233) <= 0.001 && Math.Abs(eco - 0.1655) <= 0.001
                  && Math.Abs(fast - 0.4054) <= 0.001 && Math.Abs(rcs - 0.0740) <= 0.001,
                $"signature: a cold hull is seen at {cold:N0} km; at 500 t a stock burn at {stock:0.0000} AU ({Signature.RangeKm(p500):E3} km; 0.2340), Torch {torch:0.0000} (0.5233), Economy {eco:0.0000} (0.1655), Fast {fast:0.0000} (0.4054), full RCS {rcs:0.0000} (0.0740), each ± 0.001");

            double trim = 17.29 / (1576.2 * 1576.2); // km/s²: Vesta's GM / r² at its scan range, the autopilot's hold trim
            bool rcsLoud = Signature.Loud(500, Ship.RcsAccel), trimLoud = Signature.Loud(500, trim), lo = Signature.Loud(500, 0.0010 * g), hi = Signature.Loud(500, 0.0011 * g);
            Check(rcsLoud && !trimLoud && !lo && hi,
                $"signature: loud at 500 t: full RCS {rcsLoud} ({Signature.JetW(500, Ship.RcsAccel) / 1e12:0.00} TW), the Vesta hold trim {trimLoud} ({Signature.JetW(500, trim) / 1e9:0.0} GW), " +
                $"0.0010 g {lo} ({Signature.JetW(500, 0.0010 * g) / 1e9:0.0} GW), 0.0011 g {hi} ({Signature.JetW(500, 0.0011 * g) / 1e9:0.0} GW)");

            var stockBuild = ShipBuild.Stock();
            var torchBuild = ShipBuild.Stock();
            torchBuild.pick[(int)Slot.Drive] = 3;
            var yard = typeof(OpenSystemScene).GetMethod("YardReadouts", BindingFlags.Static | BindingFlags.NonPublic);
            var rows = (List<(string key, string now, string after)>)yard.Invoke(null, new object[] { stockBuild, torchBuild });
            var row = rows.Find(r => r.key == "Seen at, full burn · cold");
            Check(row.key != null && row.now == "0.234 AU · 10,000 km" && row.after == "0.523 AU · 10,000 km",
                $"signature: the yard row \"{row.key ?? "-"}\" reads stock \"{row.now}\" (0.234 AU · 10,000 km), Torch draft \"{row.after}\" (0.523 AU · 10,000 km)");
        }

        // 15b: a ship at the belt point, 300 t dry and 200 t of fuel, Step by Step of 60 s
        void SigStepChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            double j = PursuitCheck.Jd0;
            Ship Fresh() => new Ship { Pos = at, Vel = vel, MaxG = 1, Thrust = 0, Fuel = 200, UnlimitedFuel = false, Plan = Plan.None };

            var coast = Fresh();
            coast.Step(qs, j, 60, qs.Sun);
            Check(coast.LoudSeconds == 0 && double.IsNegativeInfinity(coast.LastLoudJd), $"signature: a coast step adds {coast.LoudSeconds} s of loud time and leaves the last loud moment at {coast.LastLoudJd}");

            var burn = Fresh();
            burn.ThrottleCmd = 1;
            burn.Step(qs, j, 60, qs.Sun);
            double lag = (burn.LastLoudJd - j) * 86400;
            Check(Math.Abs(burn.LoudSeconds - 60) <= 1e-9 && Math.Abs(lag - 60) <= 1e-3,
                $"signature: a 60 s step at full throttle adds {burn.LoudSeconds:0.000000000} s (60 ± 1e-9) and the last loud moment is {lag:0.0000} s on (60 ± 1e-3); jet {Signature.JetW(500, Ship.G0) / 1e12:0.00} TW");

            var rcsFull = Fresh();
            rcsFull.RcsInput = new Vector3(0, 0, 1);
            rcsFull.Step(qs, j, 60, qs.Sun);
            var rcsTrim = Fresh();
            rcsTrim.RcsInput = new Vector3(0, 0, 0.005f);
            rcsTrim.Step(qs, j, 60, qs.Sun);
            Check(Math.Abs(rcsFull.LoudSeconds - 60) <= 1e-9 && rcsTrim.LoudSeconds == 0,
                $"signature: full RCS for 60 s adds {rcsFull.LoudSeconds:0.000000000} s ({Signature.JetW(500, Ship.RcsAccel) / 1e12:0.00} TW); RCS at 0.5 % adds {rcsTrim.LoudSeconds} s ({Signature.JetW(500, 0.005 * Ship.RcsAccel) / 1e9:0.0} GW, under the {Signature.LoudW / 1e9:0.00} GW line)");
        }

        // a bench whose span is live and whose ship has never burned (the first tick of a bench starts it tracked, as a load does)
        static Bench SigBench(SolarSystem qs, D3 at, D3 vel, JobKind kind = JobKind.Cargo, string to = "Vesta", string from = "Ceres")
        {
            var b = MakeBench(qs, at, vel, kind, from, to);
            if (kind == JobKind.Survey) b.Job.DwellNeeded = 1e12; // never done: the job stays aboard
            b.Tick(3600);
            b.Ship.LastLoudJd = double.NegativeInfinity;
            return b;
        }

        // one update of 3,600 s: `loudS` loud seconds in it, and the last loud moment `lastH` hours from its start (NaN: never).
        // A bench ship is not flown and its body moves on, so `near` puts the ship `km` out from that body, at rest with it, as of the update's end.
        static void SigUpdate(Bench b, double loudS, double lastH, Body near = null, double km = 0)
        {
            if (near != null)
            {
                double end = b.Jd + 3600 / 86400.0;
                var p = b.Sys.Pos(near, end);
                b.Ship.Pos = p + p.Norm * km; b.Ship.Vel = b.Sys.Vel(near, end);
            }
            b.Ship.LoudSeconds += loudS;
            b.Ship.LastLoudJd = double.IsNaN(lastH) ? double.NegativeInfinity : b.Jd + lastH / 24;
            b.Tick(3600);
        }

        static RouteCallRoll SigRoll(Bench b)
        {
            var roll = new RouteCallRoll();
            typeof(Career).GetField("routeRng", F).SetValue(b.C, roll);
            b.C.RouteEvents = true;
            return roll;
        }

        static string SigPart(string text, string start)
        {
            int i = text == null ? -1 : text.IndexOf(start, StringComparison.Ordinal);
            return i < 0 ? "-" : text.Substring(i, Math.Min(text.Length - i, 70));
        }

        // 15c: updates of 3,600 s on a warmed belt bench
        void SigSeenChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            double Seen(double loudS, double lastH)
            {
                var b = SigBench(qs, at, vel);
                SigUpdate(b, loudS, lastH);
                return b.C.SeenSeconds(b.Ship, b.Jd, 3600);
            }
            double cold = Seen(0, double.NaN), whole = Seen(3600, 1), first = Seen(1800, 0.5), t15 = Seen(0, -1.5), t20 = Seen(0, -2);
            Check(Math.Abs(cold) <= 0.01, $"signature: a cold ship that never burned is seen {cold:0.000} s of an hour (0)");
            Check(Math.Abs(whole - 3600) <= 0.01 && Math.Abs(first - 3600) <= 0.01,
                $"signature: loud the whole update is seen {whole:0.000} s of 3,600; loud for its first 1,800 s, then tracked, {first:0.000} s of 3,600");
            Check(Math.Abs(t15 - 1800) <= 0.01 && Math.Abs(t20) <= 0.01,
                $"signature: the last burn 1.5 h before the update is seen {t15:0.000} s of it (1,800), 2 h before {t20:0.000} s (0)");

            var b0 = SigBench(qs, at, vel);
            double c600 = b0.C.RaidChance(qs, b0.Ship, b0.Job, b0.Jd, 600), c300 = b0.C.RaidChance(qs, b0.Ship, b0.Job, b0.Jd, 300), c0 = b0.C.RaidChance(qs, b0.Ship, b0.Job, b0.Jd, 0);
            Check(Math.Abs(c600 - 0.0328) <= 1e-4 && Math.Abs(c300 - 0.0165) <= 1e-4 && c0 == 0,
                $"signature: at the belt point, at rest, a raider's chance in 600 s seen is {c600:0.00000} (0.0328), in 300 s {c300:0.00000} (0.0165), in none {c0}; the rate is {b0.C.PirateRate(qs, b0.Ship, b0.Job, b0.Jd):0.0000} an hour");

            var fresh = MakeBench(qs, at, vel);
            Check(fresh.Ship.LastLoudJd == fresh.Jd, $"signature: a bench that never ticked a span starts tracked, as a load does (last loud moment {(fresh.Ship.LastLoudJd - fresh.Jd) * 86400:0.000} s from its date)");
        }

        // 15d: the roll and its gate, with the stub that always rolls 0 and counts its draws
        void SigRollChecks(SolarSystem qs)
        {
            var (vesta, at, vel) = Belt(qs);
            string Kind(Bench b) => b.C.Call == null ? "no call" : b.C.Call.Kind.ToString();

            // a cold ship draws and is not called; the next update, loud, is
            var b1 = SigBench(qs, at, vel);
            var r1 = SigRoll(b1);
            SigUpdate(b1, 0, double.NaN, vesta, 1e6);
            Check(b1.C.Call == null && b1.C.Encounter == null && r1.Draws == 1, $"signature: a cold ship with a cargo job, no plan and no target draws once and is not called ({Kind(b1)}, {r1.Draws} draws)");
            SigUpdate(b1, 3600, 1, vesta, 1e6);
            string seen = b1.C.Call?.Text;
            Check(b1.C.Call?.Kind == Career.RouteKind.Pirate && b1.C.Encounter != null && r1.Draws == 2 && seen != null && seen.Contains("saw your burn from"),
                $"signature: the same ship, loud for an update, with no plan: {Kind(b1)}, chase {b1.C.Encounter != null}, {r1.Draws} draws (2): \"{SigPart(seen, "It saw")}\"");
            b1.C.DropContact();

            var b2 = SigBench(qs, at, vel);
            var r2 = SigRoll(b2);
            SigUpdate(b2, 0, -0.5, vesta, 1e6);
            string tracked = b2.C.Call?.Text;
            Check(b2.C.Call?.Kind == Career.RouteKind.Pirate && b2.C.Encounter != null && tracked != null && tracked.Contains("tracked you since"),
                $"signature: cold for 30 min, no loud time in the update: {Kind(b2)}, chase {b2.C.Encounter != null}, {r2.Draws} draws: \"{SigPart(tracked, "It has")}\"");
            b2.C.DropContact();

            // a survey is raided where its dwell accrues, or when its body is the target
            var s1 = SigBench(qs, at, vel, JobKind.Survey);
            var q1 = SigRoll(s1);
            SigUpdate(s1, 3600, 1, vesta, 1e6);
            Check(s1.C.Call == null && s1.C.Encounter == null && q1.Draws == 0,
                $"signature: a survey of Vesta 1,000,000 km out (scan range {s1.C.Codex.ScanRange(vesta):N0} km), no target, loud: {Kind(s1)}, {q1.Draws} draws (0)");
            var s2 = SigBench(qs, at, vel, JobKind.Survey);
            s2.Ship.Target = vesta;
            var q2 = SigRoll(s2);
            SigUpdate(s2, 3600, 1, vesta, 1e6);
            Check(s2.C.Call?.Kind == Career.RouteKind.Pirate && s2.C.Encounter != null, $"signature: the same survey with Vesta the target, loud: {Kind(s2)}, chase {s2.C.Encounter != null}, {q2.Draws} draws");
            s2.C.DropContact();
            var s3 = SigBench(qs, at, vel, JobKind.Survey);
            var q3 = SigRoll(s3);
            SigUpdate(s3, 3600, 1, vesta, 1000);
            Check(s3.C.Call?.Kind == Career.RouteKind.Pirate && s3.C.Encounter != null,
                $"signature: a survey of Vesta 1,000 km out, at rest with it, no target, loud: {Kind(s3)}, chase {s3.C.Encounter != null}, {q3.Draws} draws, docked {s3.C.Docked?.Name ?? "-"}, dwell {s3.Job.Dwell:N0} s");
            s3.C.DropContact();

            // far from Mars (no raiders there, no patrol in reach): the raider draw is made and misses; only a plan brings the other calls
            var mars = qs["Mars"];
            D3 pM = qs.Pos(mars, PursuitCheck.Jd0);
            Bench MarsBench() => SigBench(qs, pM + pM.Norm * 1e6, qs.Vel(mars, PursuitCheck.Jd0), JobKind.Cargo, "Mars orbit", "Luna");
            var m1 = MarsBench();
            var n1 = SigRoll(m1);
            SigUpdate(m1, 3600, 1, mars, 1e6);
            double presence = Threat.Presence(qs, m1.Ship.Pos, m1.Jd), away = (m1.Ship.Pos - qs.Pos(mars, m1.Jd)).Mag;
            Check(presence == 0 && m1.C.Call == null && n1.Draws == 1, $"signature: a cargo job to Mars orbit, 1,000,000 km out (raider presence {presence:0.00}, {away:N0} km from Mars, patrol reach {Threat.Reach(3):N0}), no plan, loud: {Kind(m1)}, {n1.Draws} draws (1)");
            var m2 = MarsBench();
            var n2 = SigRoll(m2);
            m2.Ship.Target = mars; m2.Ship.Plan = Plan.Brachistochrone;
            SigUpdate(m2, 3600, 1, mars, 1e6);
            Check(m2.C.Call?.Kind == Career.RouteKind.CargoTrouble && n2.Draws >= 2, $"signature: the same with the Fast plan to Mars: {Kind(m2)}, {n2.Draws} draws (at least 2)");

            // the chase does not change: a cold ship, a raider 100,000 km off
            var c = SigBench(qs, at, vel);
            var cr = SigRoll(c);
            var e = c.C.StartContact(c.Job, qs, c.Ship, c.Jd, at - at.Norm * 1e5, vel);
            c.Tick(600);
            Check(c.C.Encounter == e && c.C.Call == null && cr.Draws == 0 && qs.Bodies.Contains(e.Proxy),
                $"signature: a chase with the ship cold stays the same one after 600 s ({(c.C.Encounter == e ? "same" : c.C.Encounter == null ? "gone" : "other")}, range {e.Range:N0} km, {cr.Draws} draws)");
            c.C.DropContact();
        }

        // 15e: Threat.Of on a system of its own at the scene's date, as 12 has it
        void SigPlannerChecks(SolarSystem qs)
        {
            double j0 = jd;
            Threat.Risk Trip(string from, string to, double g)
            {
                Body a = qs[from], b = qs[to];
                double d = (qs.PosAt(b, j0) - qs.PosAt(a, j0)).Mag - Ship.Standoff(b);
                return Threat.Of(qs, a, D3.Zero, b, j0, d, g * Ship.G0);
            }
            string R(Threat.Risk r) => $"{r.Hours:0.00} h, {r.Tier} ({r.Leaving:0.00} leaving, {r.Arriving:0.00} arriving)";
            var vesta = Trip("Mars", "Vesta", 1);
            var vesta3 = Trip("Mars", "Vesta", 3);
            var ceres = Trip("Mars", "Ceres", 1);
            var luna = Trip("Earth", "Moon", 1);
            Check(Math.Abs(vesta.Hours - 2.00) <= 0.05 && vesta.Tier == "moderate" && vesta.Arriving >= 1.95, $"signature: Mars orbit → Vesta at 1 g: {R(vesta)} (2.00 ± 0.05, moderate, arriving ≥ 1.95; no hold at the Vesta port)");
            Check(Math.Abs(vesta3.Hours - 0.67) <= 0.05, $"signature: Mars orbit → Vesta at 3 g: {R(vesta3)} (0.67 ± 0.05)");
            Check(Math.Abs(ceres.Hours - 0.75) <= 0.05, $"signature: Mars orbit → Ceres at 1 g: {R(ceres)} (0.75 ± 0.05)");
            Check(luna.Hours == 0, $"signature: Earth → Luna: {R(luna)} (0)");

            // the hold at the start is gone: all the hours are on the way out
            var back = Trip("Vesta", "Mars", 1);
            Check(Math.Abs(back.Hours - 2.00) <= 0.05 && back.Arriving <= 0.05, $"signature: Vesta → Mars orbit at 1 g: {R(back)} (2.00 ± 0.05, all leaving; W3 gave 3.50)");
            Check(Math.Abs(vesta.Hours - back.Hours) <= 0.1, $"signature: a port at either end adds no hold: Mars orbit → Vesta {vesta.Hours:0.00} h, Vesta → Mars orbit {back.Hours:0.00} h (equal ± 0.1)");

            // a belt hop of 1.5e7 km at 1 g: a slow cruise is the quiet choice now
            var vFrom = qs["Vesta"];
            var vTo = qs["Pallas"];
            double a1 = Ship.G0, d1 = 1.5e7, cap = Math.Sqrt(a1 * d1);
            var full = Threat.Of(qs, vFrom, D3.Zero, vTo, j0, d1, a1);
            var half = Threat.Of(qs, vFrom, D3.Zero, vTo, j0, d1, a1, cap / 2);
            var quarter = Threat.Of(qs, vFrom, D3.Zero, vTo, j0, d1, a1, cap / 4);
            bool hop = Math.Abs(full.Hours - 5.48) <= 0.1 && Math.Abs(half.Hours - 5.37) <= 0.1 && Math.Abs(quarter.Hours - 4.99) <= 0.1 && quarter.Hours <= full.Hours;
            Check(hop,
                $"signature: Vesta → Pallas, 1.5e7 km at 1 g: full burn {full.Hours:0.00} h (5.48), cruise cap {cap / 2:0.0} km/s {half.Hours:0.00} h (5.37), cap {cap / 4:0.0} km/s {quarter.Hours:0.00} h (4.99), each ± 0.1, and the quarter no worse than the full burn; Pallas has no port, so its hold still counts; W3 gave 6.98, 7.81 and 16.45");
            if (!hop)
            {
                log.WriteLine($"    along the line Vesta → Pallas at {j0:F2} (presence, patrol; the slow parts are the ends):");
                for (int i = 0; i <= 10; i++)
                {
                    var x = qs.PosAt(vFrom, j0) * (1 - i / 10.0) + qs.PosAt(vTo, j0) * (i / 10.0);
                    log.WriteLine($"      {i * 10,3} %  presence {Threat.Presence(qs, x, j0):0.000}  patrol {Threat.PatrolAt(qs, x, j0)?.Name ?? "-"}");
                }
            }

            string line = Threat.Line(vesta);
            Check(line.Contains("seen") && line.Contains("dock first"), $"signature: the planner line for Mars orbit → Vesta reads \"{line}\"");
        }

        static int SigAt(List<Readout> rows, string label) => rows.FindIndex(r => r.label == label);
        static string SigVal(List<Readout> rows, string label) { int i = SigAt(rows, label); return i < 0 ? "-" : rows[i].value; }
        static Tone SigTone(List<Readout> rows, string label) { int i = SigAt(rows, label); return i < 0 ? Tone.Normal : rows[i].tone; }

        // the scene's rows, its watch read afresh
        List<Readout> SigRows()
        {
            var rows = new List<Readout>();
            s.TestWatchNow();
            s.Readouts(rows);
            return rows;
        }

        // 15f: the ship 1,000,000 km out from Vesta at rest with it, a cargo job aboard, route events off
        IEnumerator SigSceneChecks()
        {
            bool pills = !UI.UiShell.Disabled;
            if (pills && !app.ShellActive) { Check(false, "signature: no UI Toolkit shell, and -fowlegacyui is not on"); yield break; }
            if (!pills) log.WriteLine("  SKIP signature: no UI Toolkit shell (-fowlegacyui): the SEEN pill; the Signature and Raiders rows run alone");
            bool classicWas = GameSettings.ClassicPanel;
            double fuel0 = 0;
            Contract job = null;
            bool go = Guard("signature scene setup", () =>
            {
                if (pills && classicWas) GameSettings.ClassicPanel = false; // the pill is on the UI Toolkit HUD
                career.DropContact();
                Put("warpIdx", 0);
                var vesta = sys["Vesta"];
                D3 pV = sys.Pos(vesta, jd);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0; ship.Landed = null; ship.Hold = Hold.Off;
                ship.Pos = pV + pV.Norm * 1e6; ship.Vel = sys.Vel(vesta, jd);
                fuel0 = ship.Fuel; ship.Fuel = career.TankCapacity; // 500 t at least: a full burn is 24.5 TW whatever the load
                job = new Contract { Id = 900400, Kind = JobKind.Cargo, Title = "QA signature", From = "Ceres", To = "Vesta", Tonnes = 10, Pay = 20000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, Key = "qa/signature" };
                career.Active.Add(job);
                ship.LastLoudJd = double.NegativeInfinity;
                s.TestWatchNow();
            });
            for (int i = 0; go && i < 3; i++) yield return null;

            // 24: the drive off, never burned
            if (go) go = Guard("signature cold", () =>
            {
                var rows = SigRows();
                string pill = pills ? s.TestSeenPill() : "-";
                Check(SigVal(rows, "Signature").Contains("cold") && SigVal(rows, "Signature").Contains("10,000 km") && SigVal(rows, "Raiders").StartsWith("no track", StringComparison.Ordinal)
                      && SigTone(rows, "Raiders") == Tone.Cold && pill == "-",
                    $"signature: drive off, never burned: Signature \"{SigVal(rows, "Signature")}\", Raiders \"{SigVal(rows, "Raiders")}\" ({SigTone(rows, "Raiders")}), pill \"{pill}\"");
                ship.ThrottleCmd = 1;
            });

            // 25: full throttle for 0.3 s
            if (go) yield return Frames(0.3f);
            if (go) go = Guard("signature loud", () =>
            {
                var rows = SigRows();
                string pill = pills ? s.TestSeenPill() : "LOUD · SEEN AT";
                double p = Signature.JetW(ship), r = Signature.RangeKm(p);
                string km = (string)typeof(OpenSystemScene).GetMethod("Km", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { r });
                string want = $"{Fmt.Num(p / 1e12, 1)} TW exhaust (ideal, not heat), seen at {km}";
                Check(SigVal(rows, "Signature") == want && r / Frame.AU >= 0.20 && r / Frame.AU <= 0.24 && SigVal(rows, "Raiders") == "can see your burn"
                      && SigTone(rows, "Raiders") == Tone.Bad && pill.StartsWith("LOUD · SEEN AT", StringComparison.Ordinal),
                    $"signature: burning at {ship.Throttle * ship.AccelG:0.00} g, {ship.Mass:N0} t: Signature \"{SigVal(rows, "Signature")}\" (want \"{want}\", {r / Frame.AU:0.0000} AU, in 0.20 to 0.24), Raiders \"{SigVal(rows, "Raiders")}\" ({SigTone(rows, "Raiders")}), pill \"{pill}\"");
                ship.ThrottleCmd = 0;
            });

            // 26: the drive off again, the burn still tracked
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("signature tracked", () =>
            {
                var rows = SigRows();
                string pill = pills ? s.TestSeenPill() : "TRACKED 1:59";
                Check((pill == "TRACKED 1:59" || pill == "TRACKED 2:00") && SigVal(rows, "Raiders").StartsWith("tracking you", StringComparison.Ordinal) && SigTone(rows, "Raiders") == Tone.Bad,
                    $"signature: drive off after the burn: pill \"{pill}\" (TRACKED 1:59 or 2:00), Raiders \"{SigVal(rows, "Raiders")}\" ({SigTone(rows, "Raiders")}), {(jd - ship.LastLoudJd) * 86400:0.00} s since the last loud moment");
                // 27: burning 100,000 km from Earth, in the Guard's reach and out of raider space
                D3 earth = sys.Pos(sys.Earth, jd);
                ship.Pos = earth + earth.Norm * 1e5; ship.Vel = sys.Vel(sys.Earth, jd);
                ship.ThrottleCmd = 1;
            });
            if (go) yield return Frames(0.3f);
            if (go) go = Guard("signature quiet", () =>
            {
                var rows = SigRows();
                string pill = pills ? s.TestSeenPill() : "-";
                var patrol = Threat.PatrolAt(sys, ship.Pos, jd);
                Check(ship.Throttle > 0 && SigAt(rows, "Raiders") < 0 && SigAt(rows, "Signature") >= 0 && pill == "-",
                    $"signature: burning 100,000 km from Earth (raider presence {Threat.Presence(sys, ship.Pos, jd):0.00}, the {patrol?.Name ?? "no"} patrol in reach): Raiders row \"{SigVal(rows, "Raiders")}\", pill \"{pill}\"");

                // 29: neither the career's save nor the ship's holds the loud time (the ship has some); an old save loads
                string cj = JsonUtility.ToJson(career.ToSave(ship)), sj = JsonUtility.ToJson(ship.ToSave(jd, 0));
                var old = JsonUtility.FromJson<CareerSave>("{\"credits\":1000,\"fuel\":250,\"drive\":2,\"tank\":1,\"sensors\":1}");
                var c = new Career { Sys = sys };
                var probe = new Ship();
                c.FromSave(old, probe);
                Check(ship.LoudSeconds > 0 && !cj.Contains("oud") && !sj.Contains("oud") && c.Build[Slot.Drive].Name == "Torch drive" && probe.Fuel == 250 && probe.LoudSeconds == 0,
                    $"signature: the ship has {ship.LoudSeconds:0.0} s of loud time and neither save holds it (career {cj.Length} chars, ship {sj.Length}); an old save loads as {c.Build[Slot.Drive].Name}, {probe.Fuel:0} t of fuel");
            });

            Guard("signature cleanup", () =>
            {
                ship.ThrottleCmd = 0; ship.Throttle = 0;
                var vesta = sys["Vesta"];
                D3 pV = sys.Pos(vesta, jd);
                ship.Pos = pV + pV.Norm * 1e6; ship.Vel = sys.Vel(vesta, jd);
                ship.Fuel = fuel0;
                ship.LastLoudJd = double.NegativeInfinity;
                if (job != null) career.Active.Remove(job);
                if (GameSettings.ClassicPanel != classicWas) GameSettings.ClassicPanel = classicWas;
                s.TestWatchNow();
            });
        }
    }
}
