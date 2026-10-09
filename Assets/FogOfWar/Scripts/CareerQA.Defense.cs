using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using FogOfWar.OpenWorld;
using UnityEngine;
using UIE = UnityEngine.UIElements;

namespace FogOfWar
{
    // 17. Raider missiles, damage, armor and repairs (Career.Damage.cs, docs/design-combat.md Phase 4a): the constants, the
    // hit table and the armor rows (17a); the fire rule, the flight time and the magazine (17b); the aspect from the attitude
    // and the table's weights over seeded draws (17c); the plating (17d); every effect and floor (17e); the readers of the
    // damaged drive and sensors against the yard's as-fitted readouts (17f); the repair (17g); the chase's end dropping the
    // missiles (17h); the route and board draws (17i); the save (17j); then in the scene the Missiles and Damage rows (both
    // shells) and the yard's Repair row (UI Toolkit; UiShots draws the classic one docked), and the notes against the
    // classic note strip (17l). Headless parts run on benches of their own at the pursuit check's date (CareerQA.Pirates.cs).
    // 4b goes on from 17p. Every [calc] number was worked by hand: every check logs what it measured.
    public partial class CareerQA
    {
        sealed class CountingRng : System.Random
        {
            public int Draws;
            public CountingRng(int seed) : base(seed) { }
            public override double NextDouble() { Draws++; return base.NextDouble(); }
        }

        IEnumerator DefenseChecks()
        {
            SolarSystem qs = null;
            if (Guard("defense system", () => qs = SolarSystem.Build()))
            {
                Guard("defense table", DefenseTableChecks);
                Guard("defense fire", () => DefenseFireChecks(qs));
                Guard("defense aspect", () => DefenseAspectChecks(qs));
                Guard("defense plating", () => DefensePlatingChecks(qs));
                Guard("defense effects", () => DefenseEffectChecks(qs));
                Guard("defense readers", () => DefenseReaderChecks(qs));
                Guard("defense repair", () => DefenseRepairChecks(qs));
                Guard("defense ends", () => DefenseEndChecks(qs));
                Guard("defense rng", () => DefenseRngChecks(qs));
                Guard("defense save", () => DefenseSaveChecks(qs));
            }
            yield return DefenseSceneChecks();
            yield return DefenseNoteChecks(qs);
        }

        // a raider at rest with the bench ship, km off along dir (the ship's outward line by default); a bench never flies it
        static Encounter RaiderAt(Bench b, double km, D3? dir = null)
        {
            var n = (dir ?? b.Ship.Pos).Norm;
            return b.C.StartContact(b.Job, b.Sys, b.Ship, b.Jd, b.Ship.Pos + n * km, b.Ship.Vel);
        }

        static void SetCombatRng(Career c, System.Random r) => typeof(Career).GetField("combatRng", F).SetValue(c, r);
        static int SaidCount(Bench b, string start) => b.Said.FindAll(s => s.Text.StartsWith(start, StringComparison.Ordinal)).Count;

        // 17a
        void DefenseTableChecks()
        {
            var sums = new List<int>();
            foreach (var row in Career.HitWeights) { int t = 0; foreach (int w in row) t += w; sums.Add(t); }
            int systems = Enum.GetValues(typeof(Career.HitSystem)).Length;
            bool shape = Career.HitWeights.Length == 3 && Array.TrueForAll(Career.HitWeights, r => r.Length == systems);
            Check(shape && sums.TrueForAll(t => t == 100) && Career.HitWeights[0][(int)Career.HitSystem.Drive] == 0 && Career.HitWeights[2][(int)Career.HitSystem.Drive] == 60,
                $"defense 17a: the hit table has 3 aspects of {systems} systems ({shape}), rows sum to {string.Join(", ", sums)} (100 each); drive fore {Career.HitWeights[0][0]} % (0), aft {Career.HitWeights[2][0]} % (60)");
            Check(Career.FireKm == 2000 && Career.MissileKms == 2 && Career.Salvo == 2 && Career.MagazineSize == 6 && Career.MaxHits == 2 && Career.HitFactor == 0.5
                  && Career.VentShare == 0.2 && Career.HoldShare == 0.2 && Career.RepairShare == 0.25 && Career.FireKm / Career.MissileKms == 1000,
                $"defense 17a: fire {Career.FireKm:N0} km, {Career.MissileKms} km/s ({Career.FireKm / Career.MissileKms:N0} s from the fire range), salvo {Career.Salvo}, magazine {Career.MagazineSize}, {Career.MaxHits} hits at ×{Career.HitFactor}, vent {Career.VentShare}, hold {Career.HoldShare}, repair {Career.RepairShare}");
            var armor = ShipBuild.Catalog[(int)Slot.Armor];
            string rows = string.Join("; ", Array.ConvertAll(armor, m => $"{m.Name} {m.Mass} t {m.Price:N0} cr {m.RefitDays} d stops {m.Size}"));
            Check((int)Slot.Armor == 6 && armor.Length == 3 && armor[0].Name == "No armor" && armor[0].Mass == 0 && armor[0].Price == 0 && armor[0].RefitDays == 0.5 && armor[0].Size == 0
                  && armor[1].Name == "Light plating" && armor[1].Mass == 15 && armor[1].Price == 40000 && armor[1].RefitDays == 2 && armor[1].Size == 0.5
                  && armor[2].Name == "Heavy plating" && armor[2].Mass == 40 && armor[2].Price == 100000 && armor[2].RefitDays == 3 && armor[2].Size == 0.8
                  && Array.TrueForAll(ShipBuild.Hulls, h => h.StockPick.Length == 7 && h.StockPick[(int)Slot.Armor] == 0),
                $"defense 17a: Slot.Armor is index {(int)Slot.Armor} (6); {rows}; every hull's stock pick has 7 slots and no armor");
            var light = ShipBuild.Stock(); light.pick[(int)Slot.Armor] = 1;
            var heavy = ShipBuild.Stock(); heavy.pick[(int)Slot.Armor] = 2;
            Check(Near(light.DryMass, 315, 1e-9) && Near(light.GFull, 0.971, 0.0005) && Near(light.GEmpty, 1.587, 0.0005) && Near(light.DvFull, 4916, 1)
                  && Near(heavy.DryMass, 340, 1e-9) && Near(heavy.GFull, 0.926, 0.0005) && Near(heavy.GEmpty, 1.471, 0.0005) && Near(heavy.DvFull, 4626, 1),
                $"defense 17a: light plating {light.DryMass} t dry, {light.GFull:0.000} g full, {light.GEmpty:0.000} empty, {light.DvFull:N0} km/s (315, 0.971, 1.587, 4,916); heavy {heavy.DryMass} t, {heavy.GFull:0.000}, {heavy.GEmpty:0.000}, {heavy.DvFull:N0} (340, 0.926, 1.471, 4,626)");
        }

        // 17b: a raider just outside FireKm holds fire; inside, a salvo of 2 that hits 1,000 s later (± one 100 s update); a
        // new salvo only when the last has resolved; 6 missiles are 3 salvos; the call card names the missiles
        void DefenseFireChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            try
            {
                var e = RaiderAt(b, 2001);
                b.Tick(100);
                int outside = e.Missiles.Count;
                e.Pirate.Pos = b.Ship.Pos + b.Ship.Pos.Norm * 1999;
                var fires = new List<double>();
                var hits = new List<double>();
                int inFlight = 0, during = 0;
                b.Said.Clear();
                for (int i = 0; i < 40 && b.C.Encounter == e; i++)
                {
                    int fired0 = SaidCount(b, "Missiles:"), hit0 = b.Said.FindAll(s => s.What == Career.Happening.MissileHit).Count;
                    b.Tick(100); // a bench flies neither ship: the raider stays 1,999 km off
                    if (SaidCount(b, "Missiles:") > fired0) fires.Add(b.Jd);
                    if (b.Said.FindAll(s => s.What == Career.Happening.MissileHit).Count > hit0) hits.Add(b.Jd);
                    if (e.Missiles.Count > 0) { inFlight = Math.Max(inFlight, e.Missiles.Count); during++; }
                }
                var flights = new List<string>();
                bool timed = hits.Count == 3 && fires.Count == 3;
                for (int i = 0; i < Math.Min(fires.Count, hits.Count); i++)
                {
                    double s = (hits[i] - fires[i]) * 86400;
                    flights.Add($"{s:F0}");
                    timed &= Math.Abs(s - 999.5) <= 100 + 1e-6;
                }
                string note = b.Said.Find(s => s.Text.StartsWith("Missiles:")).Text;
                int hitNotes = b.Said.FindAll(s => s.What == Career.Happening.MissileHit || (s.What == Career.Happening.JobFailed && s.Text.StartsWith("Hit:"))).Count;
                Check(outside == 0 && timed && inFlight == 2 && e.Magazine == 0 && hitNotes == 6 && note == "Missiles: 2 fired, impact in 17 min" && b.C.Encounter == e,
                    $"defense 17b: at 2,001 km the raider holds fire ({outside} in flight, 0); at 1,999 km it fires {fires.Count} salvos (3) of {inFlight} (2), each hitting {string.Join(", ", flights)} s after it fired (999.5 ± one 100 s update); " +
                    $"magazine left {e.Magazine} (0), hit notes {hitNotes} (6), {during} updates with missiles in flight; first note \"{note}\"");
            }
            finally { b.C.DropContact(); }

            var c = MakeBench(qs, at, vel);
            try
            {
                bool made = c.C.MakeCall(Career.RouteKind.Pirate, c.Job, qs, c.Ship, c.Jd);
                string text = c.C.Call?.Text;
                Check(made && text != null && text.Contains(" It carries 6 missiles to cripple the drive. ") && c.C.Encounter?.Magazine == 6 && c.C.Encounter.Missiles.Count == 0,
                    $"defense 17b: a raider call ({made}) says \"{SigPart(text, "It carries")}\", and its raider has {c.C.Encounter?.Magazine} missiles (6), none in flight");
            }
            finally { c.C.DropContact(); }
        }

        // 17c: the body frame and the aspect cones, then the table over 10,000 seeded draws per aspect, then the real strike
        // path with the nose on the raider and with the tail on it
        void DefenseAspectChecks(SolarSystem qs)
        {
            var ship = new Ship();
            var rng = new System.Random(4401);
            double worstNose = 0, worstUp = 0;
            int notFore = 0;
            for (int i = 0; i < 1000; i++)
            {
                ship.Att = Quaternion.Euler((float)(rng.NextDouble() * 360), (float)(rng.NextDouble() * 360), (float)(rng.NextDouble() * 360));
                var nose = Career.ToBody(ship, ship.Heading);
                var up = Career.ToBody(ship, Frame.FromU(ship.Att * Vector3.up));
                worstNose = Math.Max(worstNose, (nose - Vector3.forward).magnitude);
                worstUp = Math.Max(worstUp, (up - Vector3.up).magnitude);
                if (Career.AspectOf(nose) != Career.Aspect.Fore) notFore++;
            }
            Check(worstNose < 1e-5 && worstUp < 1e-5 && notFore == 0,
                $"defense 17c: over 1,000 seeded attitudes the heading reads {worstNose:E1} off body +Z and the attitude's up {worstUp:E1} off body +Y (< 1e-5); the nose is fore every time ({notFore} not)");

            ship.Att = Quaternion.LookRotation(Frame.U(new D3(0.3, -0.5, 0.8).Norm), Vector3.up);
            var side = Frame.U(D3.Cross(ship.Heading, new D3(0, 0, 1)).Norm);
            Career.Aspect At(float deg) => Career.AspectOf(Career.ToBody(ship, Frame.FromU(Quaternion.AngleAxis(deg, side) * Frame.U(ship.Heading))));
            var cones = new[] { At(0), At(44), At(46), At(90), At(134), At(136), At(180) };
            Check(cones[0] == Career.Aspect.Fore && cones[1] == Career.Aspect.Fore && cones[2] == Career.Aspect.Broadside && cones[3] == Career.Aspect.Broadside
                  && cones[4] == Career.Aspect.Broadside && cones[5] == Career.Aspect.Aft && cones[6] == Career.Aspect.Aft,
                $"defense 17c: from the nose at 0°, 44°, 46°, 90°, 134°, 136°, 180° the aspect reads {string.Join(", ", cones)} (Fore, Fore, Broadside, Broadside, Broadside, Aft, Aft)");

            var draws = new System.Random(1701);
            foreach (Career.Aspect a in Enum.GetValues(typeof(Career.Aspect)))
            {
                const int n = 10000;
                var count = new int[Career.HitWeights[(int)a].Length];
                for (int i = 0; i < n; i++) count[(int)Career.SystemFor(a, draws.NextDouble())]++;
                double worst = 0;
                var got = new List<string>();
                for (int k = 0; k < count.Length; k++)
                {
                    double pct = 100.0 * count[k] / n;
                    worst = Math.Max(worst, Math.Abs(pct - Career.HitWeights[(int)a][k]));
                    got.Add($"{(Career.HitSystem)k} {pct:0.0}/{Career.HitWeights[(int)a][k]}");
                }
                bool zeros = true;
                for (int k = 0; k < count.Length; k++) if (Career.HitWeights[(int)a][k] == 0 && count[k] != 0) zeros = false;
                Check(worst <= 2 && zeros, $"defense 17c: {a}, 10,000 seeded draws: {string.Join(", ", got)} (% drawn/table); worst {worst:0.00} points off (≤ 2), none on a 0 % system ({zeros})");
            }

            // the strike path: nose on the raider, no drive hit; tail on it, no hold, cabin or sensor hit
            var (_, at, vel) = Belt(qs);
            foreach (bool noseOn in new[] { true, false })
            {
                var b = MakeBench(qs, at, vel);
                b.C.SeedCombat(77);
                var from = new D3(0.6, 0.0, 0.8);
                b.Ship.Att = Quaternion.LookRotation(Frame.U(noseOn ? from : -from), Vector3.up);
                for (int i = 0; i < 1000; i++) b.C.Strike(b.Ship, from * 1.5);
                int drive = SaidCount(b, "Hit: drive"), sensors = SaidCount(b, "Hit: sensors"), hold = SaidCount(b, "Hit: hold"), tank = SaidCount(b, "Hit: tank");
                Check(noseOn ? drive == 0 && sensors > 200 && hold > 100 : sensors == 0 && hold == 0 && drive > 500,
                    $"defense 17c: 1,000 strikes with the {(noseOn ? "nose" : "tail")} on the raider: drive {drive}, sensors {sensors}, hold {hold}, tank {tank} hits ({(noseOn ? "no drive; sensors near 300, hold near 200" : "no sensors or hold; drive near 600")})");
            }
        }

        // 17d: 10,000 seeded strikes on light and on heavy plating
        void DefensePlatingChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            foreach (var (pick, want) in new[] { (1, 50.0), (2, 80.0), (0, 0.0) })
            {
                var b = MakeBench(qs, at, vel);
                b.C.Build.pick[(int)Slot.Armor] = pick;
                b.C.Apply(b.Ship);
                b.C.SeedCombat(9000 + pick);
                for (int i = 0; i < 10000; i++) b.C.Strike(b.Ship, new D3(1, 0, 0));
                int stopped = SaidCount(b, Career.PlatingNote);
                double pct = stopped / 100.0;
                Check(Math.Abs(pct - want) <= 2, $"defense 17d: {b.C.Build[Slot.Armor].Name} stops {stopped:N0} of 10,000 seeded hits: {pct:0.00} % ({want} ± 2)");
            }
        }

        // 17e: each effect and its floor, one hit at a time
        void DefenseEffectChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            double built = b.C.Build.Thrust;
            var thrusts = new List<double>();
            for (int i = 0; i < 3; i++) { b.C.Hit(b.Ship, Career.HitSystem.Drive); thrusts.Add(b.Ship.Thrust / built); }
            var driveNotes = b.Said.FindAll(s => s.Text.StartsWith("Hit: drive")).ConvertAll(s => s.Text);
            Check(thrusts[0] == 0.5 && thrusts[1] == 0.25 && thrusts[2] == 0.25 && b.C.Damage.drive == 2 && b.C.Thrust == built * 0.25
                  && driveNotes.Count == 3 && driveNotes[0] == "Hit: drive, thrust 50 %" && driveNotes[2] == "Hit: drive, thrust 25 %",
                $"defense 17e: three drive hits leave the ship's thrust at {string.Join(", ", thrusts)} of the fitted {built:0.000} MN (0.5, 0.25, 0.25), {b.C.Damage.drive} hits counted (2); notes: {string.Join(" / ", driveNotes)}");

            for (int i = 0; i < 3; i++) b.C.Hit(b.Ship, Career.HitSystem.Sensors);
            Check(b.C.SensorFactor == 0.25 && b.C.Damage.sensors == 2 && b.C.Codex.SensorRange == 0.25 && b.C.Codex.ScanSeconds == Career.DwellSeconds
                  && Signature.SensorKm(b.C.Codex.SensorLevel, b.C.Codex.SensorFactor) == 2500 && b.Said.Exists(s => s.Text == "Hit: sensors, range 50 %"),
                $"defense 17e: three sensor hits: factor {b.C.SensorFactor} (0.25), {b.C.Damage.sensors} counted (2), codex range ×{b.C.Codex.SensorRange} (0.25), scan time {b.C.Codex.ScanSeconds:N0} s (7,200: the dwell does not change), contact range {Signature.SensorKm(b.C.Codex.SensorLevel, b.C.Codex.SensorFactor):N0} km (2,500)");

            b.Ship.Fuel = 150; b.C.SinkK = 30;
            b.C.Hit(b.Ship, Career.HitSystem.Tank);
            Check(Near(b.Ship.Fuel, 120, 1e-9) && b.C.SinkK == 30 && b.Said.Exists(s => s.Text == "Hit: tank, 30.0 t of water vented"),
                $"defense 17e: a tank hit vents 20 % of 150 t: {b.Ship.Fuel:0.000} t left (120), the sink still {b.C.SinkK} K (30: intensive); \"{b.Said.FindLast(s => s.Text.StartsWith("Hit: tank")).Text}\"");

            // the hold: the chase's cargo job first, else the largest load aboard
            var small = new Contract { Id = 900701, Kind = JobKind.Cargo, Title = "QA small", From = "Ceres", To = "Vesta", Tonnes = 10, Pay = 5000, Days = 100, IssuedJd = b.Jd, DeadlineJd = b.Jd + 100, Key = "qa/defense-small" };
            b.C.Active.Add(small);
            b.C.Hit(b.Ship, Career.HitSystem.Hold);
            double bigT = b.Job.Tonnes, bigPay = b.Job.Pay, smallT = small.Tonnes;
            var e = b.C.StartContact(small, qs, b.Ship, b.Jd, at + at.Norm * 5e4, vel);
            b.C.Hit(b.Ship, Career.HitSystem.Hold);
            b.C.DropContact();
            Check(Near(bigT, 32, 1e-9) && Near(bigPay, 16000, 1e-9) && Near(smallT, 10, 1e-9) && Near(small.Tonnes, 8, 1e-9) && Near(small.Pay, 4000, 1e-9)
                  && b.Said.Exists(s => s.Text == "Hit: hold, lost 8.0 t and 4,000 cr") && b.C.Active.Contains(b.Job) && b.C.Active.Contains(small),
                $"defense 17e: a hold hit with no chase takes 20 % of the largest load: 40 → {bigT} t, 20,000 → {bigPay:N0} cr (32, 16,000), the 10 t job untouched ({smallT}); chased on the 10 t job it takes that one: {small.Tonnes} t, {small.Pay:N0} cr (8, 4,000); both jobs kept");

            // the cabin: the chase's passenger job fails as an expired one does, and the chase ends on the next update
            var p = MakeBench(qs, at, vel, JobKind.Passengers);
            int failed0 = p.C.Failed, stand0 = p.C.Standing(Faction.Compact);
            var pe = RaiderAt(p, 1e5);
            p.C.Hit(p.Ship, Career.HitSystem.Cabin);
            bool gone = !p.C.Active.Contains(p.Job);
            p.Tick(10);
            string cabin = p.Said.Find(s => s.What == Career.Happening.JobFailed).Text;
            Check(gone && p.C.Failed == failed0 + 1 && p.C.Standing(Faction.Compact) == stand0 - Career.StandingLoss && cabin == "Hit: cabin, passengers hurt: QA ore failed"
                  && p.C.Encounter == null && p.Said.Exists(s => s.What == Career.Happening.RaiderGone && s.Text.StartsWith("The job is over")),
                $"defense 17e: a cabin hit fails the chase's passenger job ({gone}): failed {failed0} → {p.C.Failed}, Compact standing {stand0} → {p.C.Standing(Faction.Compact)} (−{Career.StandingLoss}), \"{cabin}\"; the next update ends the chase ({p.Said.FindLast(s => s.What == Career.Happening.RaiderGone).Text})");

            // empty systems: no cargo, no passengers, fixed radiators, any PDC
            var h = MakeBench(qs, at, vel);
            h.C.Active.Clear();
            h.C.Apply(h.Ship);
            foreach (var s in new[] { Career.HitSystem.Hold, Career.HitSystem.Cabin, Career.HitSystem.Radiators, Career.HitSystem.Pdc }) h.C.Hit(h.Ship, s);
            h.Ship.Fuel = 0;
            h.C.Hit(h.Ship, Career.HitSystem.Tank);
            int hull = SaidCount(h, Career.HullNote);
            Check(hull == 5 && !h.C.Damage.Any && h.C.Failed == 0,
                $"defense 17e: a hold, cabin, fixed-radiator, PDC and dry-tank hit on an empty ship: \"{Career.HullNote}\" {hull} times (5), no damage ({!h.C.Damage.Any}), no job failed");

            // the radiators: a jammed stowable set is not dark-ready, holds the track OutHoldS and does not soak
            foreach (bool jam in new[] { false, true })
            {
                var d = Dark(qs, at, vel);
                if (jam) d.C.Hit(d.Ship, Career.HitSystem.Radiators);
                d.C.SinkK = 0;
                d.Ship.LastLoudJd = d.Jd;
                d.Tick(600);
                bool ready = d.C.DarkReady(d.Ship), stowed = d.C.Stowed(d.Ship, d.Jd);
                double hold = d.C.HoldS(d.Ship), k = d.C.SinkK;
                Check(jam ? !ready && !stowed && hold == d.C.OutHoldS && k == 0 && d.C.Damage.radiators && d.Said.Exists(s => s.Text == "Hit: radiators jammed out, not dark-ready")
                          : ready && stowed && hold == Signature.TrackHoldS * d.C.Build.DarkRho && k > 0,
                    $"defense 17e: stowable radiators {(jam ? "jammed by a hit" : "not hit")}: dark-ready {ready}, stowed {stowed} 10 min after a burn, hold {hold:N0} s, sink {k:0.0000} K after 600 s " +
                    (jam ? $"(False, False, {d.C.OutHoldS:N0}, 0)" : $"(True, True, {Signature.TrackHoldS * d.C.Build.DarkRho:N0}, > 0)"));
            }
        }

        // 17f: the drive and sensors as the ship flies now (damaged) against the yard's as-fitted readouts
        void DefenseReaderChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            b.C.Build.pick[(int)Slot.Drive] = 2; // the Fast drive: 3 g at RatedMass
            b.Ship.MaxG = 10;
            b.C.Apply(b.Ship);
            double gEmpty = b.C.Build.GEmpty, gFull = b.C.Build.GFull, top0 = b.C.TopG, acc0 = b.C.AccelAt(500);
            b.C.Hit(b.Ship, Career.HitSystem.Drive);
            b.C.Codex.Achieved.Remove("drive3");
            var feats = b.C.Codex.CheckFeats(b.C, b.Ship);
            Check(Near(b.C.TopG, gEmpty / 2, 1e-12) && Near(b.C.AccelAt(500), acc0 / 2, 1e-15) && Near(b.C.TopG, top0 / 2, 1e-12) && b.Ship.MaxG <= b.C.TopG + 1e-12
                  && Near(b.Ship.AMax, b.C.Thrust / b.Ship.Mass, 1e-15) && b.C.Build.GFull == gFull && b.C.Build.GEmpty == gEmpty && b.C.Codex.Has("drive3"),
                $"defense 17f: a Fast drive hit once: TopG {top0:0.000} → {b.C.TopG:0.000} g, AccelAt(500 t) {acc0 * 1000:0.000} → {b.C.AccelAt(500) * 1000:0.000} m/s², the pilot's limit clamped to {b.Ship.MaxG:0.000} g, ship.AMax {b.Ship.AMax * 1000:0.000} m/s² " +
                $"(thrust / mass: the planner, CatchNow, Divert); the yard's as-fitted {b.C.Build.GFull:0.000} g full and {b.C.Build.GEmpty:0.000} g empty unchanged; the Hard burn feat reads the drive fitted ({b.C.Codex.Has("drive3")}: {string.Join(", ", feats)})");
            b.C.Hit(b.Ship, Career.HitSystem.Sensors);
            Check(Signature.SensorKm(b.C.Build.SensorLevel) == Signature.StockKm && Signature.SensorKm(b.C.Codex.SensorLevel, b.C.Codex.SensorFactor) == Signature.StockKm / 2
                  && b.C.Codex.ScanRange(qs["Vesta"]) == Career.ScanRange(qs["Vesta"]) / 2,
                $"defense 17f: a sensor hit: the traffic sense range {Signature.SensorKm(b.C.Codex.SensorLevel, b.C.Codex.SensorFactor):N0} km (5,000), the scan range of Vesta {b.C.Codex.ScanRange(qs["Vesta"]):N0} km (half of {Career.ScanRange(qs["Vesta"]):N0}); the yard's as-fitted {Signature.SensorKm(b.C.Build.SensorLevel):N0} km (10,000)");
        }

        // 17g: the repair: cost and days by the formula, the why-nots, a refit that replaces a damaged part
        void DefenseRepairChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var live = new List<Bench>();
            try
            {
                var b = DockedAt(qs, at, vel, "Ceres", live, false);
                b.C.Build.pick[(int)Slot.Radiators] = 1;
                b.C.Damage = new ShipDamage { drive = 1 };
                bool one = b.C.CanRepair(out _, out double cost1, out double days1);
                b.C.Damage = new ShipDamage { drive = 2, sensors = 1, radiators = true };
                b.C.Apply(b.Ship);
                bool ok = b.C.CanRepair(out string why, out double cost, out double days);
                double cr0 = b.C.Credits, thrustBefore = b.Ship.Thrust;
                bool done = b.C.Repair(b.Ship);
                string note = b.Said.FindLast(s => s.What == Career.Happening.Upgrade).Text;
                Check(one && cost1 == 7500 && days1 == 1 && ok && cost == 32500 && days == 3.5 && done && b.C.Credits == cr0 - 32500 && Near(b.C.RefitJd - b.Jd, 3.5, 1e-9)
                      && b.C.InYard && !b.C.Damage.Any && b.Ship.Thrust == b.C.Build.Thrust && thrustBefore == b.C.Build.Thrust / 4 && note == "Repaired: −32,500 cr. In the yard 3.5 days",
                    $"defense 17g: one stock drive hit costs {cost1:N0} cr and {days1} day (7,500, 1); two drive hits, one sensor hit and jammed stowable radiators at Ceres: {cost:N0} cr " +
                    $"(25 % of 2 × 30,000 + 10,000 + 60,000 = 32,500), {days} days (2 + 0.5 + 1 = 3.5), allowed {ok} ({why ?? "-"}); repaired {done}: −{cr0 - b.C.Credits:N0} cr, in the yard {(b.C.RefitJd - b.Jd):0.###} days, " +
                    $"damage left {b.C.Damage.Any}, thrust {thrustBefore:0.000} → {b.Ship.Thrust:0.000} MN; \"{note}\"");

                b.C.Damage = new ShipDamage { drive = 1 };
                b.C.Credits = 100;
                b.C.CanRepair(out string broke, out _, out _);
                b.C.Credits = 5e6;
                b.C.Sandbox = true;
                b.C.CanRepair(out string sand, out _, out _);
                b.C.Sandbox = false;
                b.C.Damage = new ShipDamage();
                b.C.CanRepair(out string none, out _, out _);
                var away = MakeBench(qs, at, vel);
                live.Add(away);
                away.C.Damage = new ShipDamage { sensors = 1 };
                away.C.CanRepair(out string undocked, out _, out _);
                Check(broke == "not enough credits" && sand == "sandbox" && none == "no damage" && undocked == "dock first" && !away.C.Repair(away.Ship) && away.C.Damage.sensors == 1,
                    $"defense 17g: why not: 100 cr \"{broke}\", sandbox \"{sand}\", no damage \"{none}\", in flight \"{undocked}\" (and the repair refused, the hit kept)");

                // a refit that replaces the damaged drive clears its hits; the sensors, not replaced, keep theirs
                b.C.Damage = new ShipDamage { drive = 2, sensors = 1 };
                b.C.RefitJd = 0;
                var to = b.C.Build.Clone(); to.pick[(int)Slot.Drive] = 2;
                bool refit = b.C.RefitTo(b.Ship, to);
                Check(refit && b.C.Damage.drive == 0 && b.C.Damage.sensors == 1 && b.Ship.Thrust == ShipBuild.Rated(3),
                    $"defense 17g: a refit to the Fast drive ({refit}) clears the drive's hits ({b.C.Damage.drive}, 0) and keeps the sensors' ({b.C.Damage.sensors}, 1): thrust {b.Ship.Thrust:0.000} MN ({ShipBuild.Rated(3):0.000})");
            }
            finally { foreach (var x in live) x.C.DropContact(); }
        }

        // 17h: docked, landed or lost ends the chase with missiles in flight; a new chase starts with none and a full magazine
        void DefenseEndChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            var e = RaiderAt(b, 1999);
            b.Tick(100);
            int flying = e.Missiles.Count;
            b.Ship.Landed = qs["Vesta"];
            b.Tick(1);
            b.Ship.Landed = null;
            bool docked = b.C.Encounter == null && b.Said.Exists(s => s.What == Career.Happening.RaiderGone && s.Text.StartsWith("Docked at Vesta"));
            var e2 = RaiderAt(b, 5e4);
            bool fresh = e2.Missiles.Count == 0 && e2.Magazine == Career.MagazineSize;
            b.C.DropContact();
            Check(flying == 2 && docked && fresh,
                $"defense 17h: with {flying} missiles in flight (2), docking at Vesta ends the chase ({docked}) and drops them; the next chase starts with {e2.Missiles.Count} in flight and {e2.Magazine} aboard (0, 6)");

            var d = Dark(qs, at, vel);
            var de = RaiderAt(d, 1999);
            d.Tick(100);
            int dFlying = de.Missiles.Count;
            de.Pirate.Pos = d.Ship.Pos + d.Ship.Pos.Norm * 1e5;
            de.FixJd = d.Jd - 2300 / 86400.0;
            d.Tick(10);
            bool lost = d.C.Encounter == null && d.Said.Exists(s => s.What == Career.Happening.RaiderGone && s.Text.StartsWith("Track lost"));
            Check(dFlying == 2 && lost && d.Said.FindAll(s => s.What == Career.Happening.MissileHit).Count == 0,
                $"defense 17h: a dark-ready hull 100,000 km out with {dFlying} missiles in flight (2), its last fix 2,300 s ago: the raider loses it ({lost}) and the missiles go with the chase, no hit");
        }

        // 17i: a chase with three salvos draws nothing from routeRng; the combat rng draws twice a missile; a Ceres board is the
        // same with and without combat draws and damage
        void DefenseRngChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var b = MakeBench(qs, at, vel);
            var roll = SigRoll(b);
            var combat = new CountingRng(5);
            SetCombatRng(b.C, combat);
            var e = RaiderAt(b, 1999);
            for (int i = 0; i < 32 && b.C.Encounter == e; i++) b.Tick(100);
            b.C.DropContact();
            int quiet = roll.Draws;
            b.Tick(100); // no chase: the roll draws as before (and its zero roll may start one)
            b.C.DropContact();
            Check(quiet == 0 && combat.Draws == 12 && roll.Draws == 1 && e.Magazine == 0,
                $"defense 17i: 3,200 s of a chase with 3 salvos: routeRng drew {quiet} (0), the combat rng {combat.Draws} (12: plating and system for 6 missiles); after the chase one update draws {roll.Draws} from routeRng (1)");

            var live = new List<Bench>();
            try
            {
                string Board(bool fight)
                {
                    var x = BoardBench(qs, at, vel, "Ceres", live);
                    if (fight)
                    {
                        x.C.SeedCombat(3);
                        for (int i = 0; i < 1000; i++) x.C.Strike(x.Ship, new D3(0, 1, 0));
                        x.C.Damage = new ShipDamage { drive = 2, sensors = 2 };
                    }
                    x.C.Tick(qs, x.Ship, x.Jd, 0);
                    return string.Join("; ", x.C.Board.ConvertAll(j => $"{j.Key} {j.Title} {j.Pay}"));
                }
                string plain = Board(false), fought = Board(true);
                Check(plain.Length > 0 && plain == fought, $"defense 17i: the Ceres board is the same after 1,000 combat draws and damage ({plain == fought}): {plain}");
            }
            finally { foreach (var x in live) x.C.DropContact(); }
        }

        // 17j: a save keeps the damage; an old save has none; a bad one is clamped; a 6-pick build loads with no armor
        void DefenseSaveChecks(SolarSystem qs)
        {
            var (_, at, vel) = Belt(qs);
            var b = Dark(qs, at, vel);
            b.C.Damage = new ShipDamage { drive = 2, sensors = 1, radiators = true };
            b.C.Build.pick[(int)Slot.Armor] = 2;
            string json = JsonUtility.ToJson(b.C.ToSave(b.Ship));
            var c = new Career { Sys = qs };
            var ship = new Ship();
            c.FromSave(JsonUtility.FromJson<CareerSave>(json), ship);
            Check(c.Damage.drive == 2 && c.Damage.sensors == 1 && c.Damage.radiators && c.Build.pick[(int)Slot.Armor] == 2 && ship.Thrust == c.Build.Thrust / 4 && !c.DarkReady(ship) && json.Contains("\"damage\""),
                $"defense 17j: a save round trip ({json.Length} chars) keeps drive {c.Damage.drive}, sensors {c.Damage.sensors}, radiators jammed {c.Damage.radiators} (2, 1, True), heavy plating (pick {c.Build.pick[(int)Slot.Armor]}), thrust {ship.Thrust:0.000} MN (a quarter), not dark-ready");

            var old = new Career { Sys = qs };
            old.FromSave(JsonUtility.FromJson<CareerSave>("{\"credits\":1000,\"fuel\":100,\"build\":{\"hull\":\"FWO-052 light frigate\",\"pick\":[1,1,1,2,0,1]}}"), new Ship());
            var bad = new Career { Sys = qs };
            bad.FromSave(JsonUtility.FromJson<CareerSave>("{\"credits\":1000,\"damage\":{\"drive\":9,\"sensors\":-3,\"radiators\":true}}"), new Ship());
            Check(!old.Damage.Any && old.DriveFactor == 1 && old.Build.pick.Length == 7 && old.Build.pick[(int)Slot.Armor] == 0 && old.Build.Stowable
                  && bad.Damage.drive == 2 && bad.Damage.sensors == 0 && !bad.Damage.radiators,
                $"defense 17j: an old save with a 6-pick build and no damage field loads with no damage ({!old.Damage.Any}), picks {Picks(old.Build)} (no armor, stowable kept); a save with drive 9, sensors −3 and jammed fixed radiators loads as {bad.Damage.drive}, {bad.Damage.sensors}, {bad.Damage.radiators} (2, 0, False)");
        }

        // 17k: the scene: a raider 1,999 km off fires, the Missiles row; a drive hit, the Damage row; the Jobs page's Repair
        // row docked at Ceres (UI Toolkit) and the page's signature
        IEnumerator DefenseSceneChecks()
        {
            Contract job = null;
            Encounter e = null;
            var dock0 = career.Docked;
            var draft0 = Get<ShipBuild>("yardDraft");
            var yardPort0 = Get<Port>("yardPort");
            var head0 = Get<UIE.Label>("yardHead");
            var setDocked = typeof(Career).GetProperty("Docked").GetSetMethod(true);
            void Restore()
            {
                Put("yardDraft", draft0); Put("yardPort", yardPort0); Put("yardHead", head0); Put("repairBtn", null);
                career.DropContact();
                career.Active.RemoveAll(c => c.Key == "qa/defense-scene");
                career.Damage = new ShipDamage();
                career.Apply(ship);
                Put("warpIdx", 0);
                Put("jobsSig", null);
            }
            bool go = Guard("defense scene setup", () =>
            {
                career.DropContact();
                Put("warpIdx", 0);
                var vesta = sys["Vesta"];
                D3 pV = sys.Pos(vesta, jd), vV = sys.Vel(vesta, jd);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0; ship.Landed = null; ship.Hold = Hold.Off;
                ship.Pos = pV + pV.Norm * 1e6; ship.Vel = vV;
                job = new Contract { Id = 900700, Kind = JobKind.Cargo, Title = "QA defense", From = "Ceres", To = "Vesta", Tonnes = 10, Pay = 20000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, Key = "qa/defense-scene" };
                career.Active.Add(job);
                e = career.StartContact(job, sys, ship, jd, ship.Pos + pV.Norm * 1999, vV);
            });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) go = Guard("defense scene rows", () =>
            {
                var rows = SigRows();
                string m = SigVal(rows, "Missiles");
                bool shape = Regex.IsMatch(m, @"^2 inbound, impact in 16:[0-5]\d; 4 left aboard the raider$");
                Check(e.Missiles.Count == 2 && shape && SigTone(rows, "Missiles") == Tone.Bad && SigAt(rows, "Damage") < 0,
                    $"defense 17k: in the scene a raider 1,999 km off fired {e.Missiles.Count} (2): Missiles row \"{m}\" (2 inbound, impact in 16:xx; 4 left), {SigTone(rows, "Missiles")} (Bad); no Damage row yet ({SigVal(rows, "Damage")})");
                var sigM = s.GetType().GetMethod("JobsSignature", F);
                string sig0 = (string)sigM.Invoke(s, null);
                career.Hit(ship, Career.HitSystem.Drive);
                string sig1 = (string)sigM.Invoke(s, null);
                rows = SigRows();
                string d = SigVal(rows, "Damage");
                Check(d == "drive 50 %: any port repairs it" && SigTone(rows, "Damage") == Tone.Bad && ship.Thrust == career.Build.Thrust / 2 && sig1 != sig0,
                    $"defense 17k: a drive hit: Damage row \"{d}\" ({SigTone(rows, "Damage")}), the ship's thrust {ship.Thrust:0.000} of {career.Build.Thrust:0.000} MN, the Jobs page's signature changed ({sig1 != sig0})");
                career.DropContact();
                if (UI.UiShell.Disabled)
                {
                    log.WriteLine("  SKIP defense 17k: no UI Toolkit shell (-fowlegacyui): the Jobs page's Repair row; the rows above run in both shells, and UiShots reads the classic panel's Repair button docked at Earth");
                    return;
                }
                setDocked.Invoke(career, new object[] { Array.Find(Career.Ports, p => p.Name == "Ceres") });
                var box = new UIE.VisualElement();
                Call("BuildJobs", box);
                career.CanRepair(out _, out double cost, out _);
                string want = R<string>("RepairLine", false);
                int repair = CountText(box, want), damage = CountText(box, "Damage: drive 50 %");
                Check(repair == 1 && damage == 1 && cost == 7500 && want.StartsWith("Repair all: 7,500 cr, ", StringComparison.Ordinal),
                    $"defense 17k: the Jobs page docked at Ceres draws the Damage line {damage} time (1) and the button \"{want}\" {repair} time (1)");
                career.Damage = new ShipDamage();
                var clean = new UIE.VisualElement();
                Call("BuildJobs", clean);
                Check(CountContains(clean, "Repair all") == 0 && CountContains(clean, "Damage:") == 0, "defense 17k: with no damage the Jobs page has no Repair row");
            });
            Guard("defense scene cleanup", () => { setDocked.Invoke(career, new object[] { dock0 }); Restore(); });
            yield return null;
        }

        // 17l: the 4a notes against the classic note strip, measured as PirateNoteChecks measures W4's
        IEnumerator DefenseNoteChecks(SolarSystem qs)
        {
            var texts = new List<string>();
            bool go = Guard("defense notes setup", () =>
            {
                texts.Add(Career.FiredNote(2, 999.9)); texts.Add(Career.FiredNote(2, 59)); texts.Add(Career.PlatingNote); texts.Add(Career.HullNote);
                var (_, at, vel) = Belt(qs);
                var b = MakeBench(qs, at, vel);
                b.Job.Tonnes = 99.9 / Career.HoldShare; b.Job.Pay = 9999999 / Career.HoldShare;
                b.Ship.Fuel = 400; b.C.Build.pick[(int)Slot.Radiators] = 1;
                foreach (Career.HitSystem h in Enum.GetValues(typeof(Career.HitSystem))) b.C.Hit(b.Ship, h);
                b.C.Hit(b.Ship, Career.HitSystem.Drive); b.C.Hit(b.Ship, Career.HitSystem.Sensors);
                foreach (string rider in (string[])typeof(Career).GetField("Riders", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))
                {
                    var p = MakeBench(qs, at, vel, JobKind.Passengers);
                    p.Job.Title = rider;
                    p.C.Hit(p.Ship, Career.HitSystem.Cabin);
                    texts.AddRange(p.Said.FindAll(x => x.Text.StartsWith("Hit:")).ConvertAll(x => x.Text));
                }
                texts.AddRange(b.Said.FindAll(x => x.Text.StartsWith("Hit:")).ConvertAll(x => x.Text));
                var r = DockedAt(qs, at, vel, "Ceres", new List<Bench>(), false);
                r.C.Credits = 1e8; r.C.RefitJd = r.Jd + 90;
                r.C.Damage = new ShipDamage { drive = 2, sensors = 2, radiators = true };
                r.C.Build.pick[(int)Slot.Drive] = 3; r.C.Build.pick[(int)Slot.Radiators] = 1;
                r.C.Repair(r.Ship);
                texts.AddRange(r.Said.FindAll(x => x.Text.StartsWith("Repaired")).ConvertAll(x => x.Text));
                Put("noteWidths", null);
                Put("noteProbe", texts.ToArray());
            });
            float[] widths = null;
            for (int i = 0; go && i < 10 && widths == null; i++) { yield return null; widths = Get<float[]>("noteWidths"); }
            if (go) Guard("defense notes width", () =>
            {
                if (widths == null || widths.Length != texts.Count) { Check(false, $"defense 17l: the scene measured {widths?.Length.ToString() ?? "nothing"} of {texts.Count} texts in 10 frames"); return; }
                float strip = (float)typeof(OpenSystemScene).GetField("NoteW", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                int widest = 0;
                var over = new List<string>();
                for (int i = 0; i < widths.Length; i++)
                {
                    if (widths[i] > widths[widest]) widest = i;
                    if (widths[i] > strip) over.Add($"{widths[i]:F0} px \"{texts[i]}\"");
                }
                Check(over.Count == 0 && texts.Count >= 16,
                    $"defense 17l: the 4a notes fit the classic note strip: {texts.Count} texts (every hit, each rider's cabin hit, 99.9 t and 9,999,999 cr, the repair), the widest {widths[widest]:F0} of {strip:F0} px: \"{texts[widest]}\"" +
                    (over.Count > 0 ? $"; {over.Count} too wide: " + string.Join("; ", over) : ""));
            });
        }
    }
}
