using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FogOfWar.OpenWorld;
using UnityEngine;
using HS = FogOfWar.OpenWorld.Career.HitSystem;

namespace FogOfWar
{
    // 19. The dev menu (OpenSystemScene.Dev.cs, OpenSystemScene.DevMenu.cs): the gate lets the -fowcareer run in and F7's
    // toggle opens it in the shell this run has (19a); test money (19b); a refused chase and the three test chases (19c); the
    // salvo out of turn and the refill (19d); each hit, the real 4a/4b effect and note (19e); the free repair, clicked as the
    // shell's button is (19f); the tank and the sink (19g); reveal and the seeded draws (19h); the time skip by warp (19i);
    // the sandbox's refusals (19j); closing (19k); the notes against the classic strip (19l). Ship 1e6 km off Vesta, at rest
    // with it, out of every patrol's reach, as 17k; everything it changes is put back.
    public partial class CareerQA
    {
        IEnumerator DevMenuChecks()
        {
            double credits0 = career.Credits, paid0 = career.ShipPaid, sink0 = career.SinkK, fuel0 = ship.Fuel, maxG0 = ship.MaxG;
            var build0 = career.Build.Clone();
            var damage0 = career.Damage.Clone();
            bool reveal0 = Signature.RevealAll, sandbox0 = career.Sandbox;
            var rngField = typeof(Career).GetField("combatRng", F);
            object rng0 = rngField.GetValue(career);
            var active0 = new List<Contract>(career.Active);
            var dock0 = career.Docked;
            var target0 = ship.Target;
            var setDocked = typeof(Career).GetProperty("Docked").GetSetMethod(true);
            var said = new List<string>();
            string Last() { string t = career.Notes.Count > 0 ? career.Notes[career.Notes.Count - 1].Text : ""; said.Add(t); return t; }
            string Why(string prop) => (string)typeof(OpenSystemScene).GetProperty(prop, F).GetValue(s);
            bool toolkit = app.ShellActive && s.HasHud;
            int openFrame = 0;
            Encounter e = null;

            // 19a
            bool go = Guard("dev setup", () =>
            {
                career.DropContact();
                Put("warpIdx", 0);
                var vesta = sys["Vesta"];
                D3 pV = sys.Pos(vesta, jd), vV = sys.Vel(vesta, jd);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0; ship.Landed = null; ship.Hold = Hold.Off;
                ship.Pos = pV + pV.Norm * 1e6; ship.Vel = vV;
                career.Active.Clear();
                career.Damage = new ShipDamage();
                career.Apply(ship);
                bool keys = (bool)SceneStatic("DevKeys");
                Check(keys && !s.DevMenuOpen, $"dev 19a: the dev keys are on under -fowcareer ({keys}); the menu starts closed ({!s.DevMenuOpen})");
                s.ToggleDevMenu();
                openFrame = Time.frameCount;
                Check(s.DevMenuOpen, "dev 19a: F7's toggle opens the menu");
            });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) Guard("dev shell", () =>
            {
                if (toolkit)
                {
                    string report = s.TestDevMenuReport();
                    Check(report.StartsWith("Flex", StringComparison.Ordinal) && s.TestDevMenuFits && s.TestDevClassicFrame < 0,
                        $"dev 19a: the UI Toolkit HUD shows the menu on screen ({s.TestDevMenuFits}), the classic panel does not draw it: {report}");
                }
                else
                {
                    // above the readouts, in a panel scrolled to the top: on screen without scrolling
                    var scroll = (Vector2)typeof(FogOfWarApp).GetField("panelScroll", F).GetValue(app);
                    Check(s.TestDevClassicFrame >= openFrame && s.TestDevClassicY >= 0 && s.TestDevClassicY < 150 && scroll == Vector2.zero,
                        $"dev 19a: the classic panel paints the menu at its top (frame {s.TestDevClassicFrame}, opened at {openFrame}; {s.TestDevClassicY:0} px down, scroll {scroll})");
                }
            });

            // 19b
            if (go) go = Guard("dev money", () =>
            {
                Check(s.DevSetMoney(true) && Last() == "Dev: test money on, ship bought out", $"dev 19b: test money on: \"{said[said.Count - 1]}\"");
            });
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("dev money off", () =>
            {
                Check(career.Credits >= 1e8 && career.ShipPaid == Career.ShipPrice, $"dev 19b: a tick tops the credits up ({career.Credits:N0} cr) and buys the ship out ({career.ShipPaid:N0})");
                Check(s.DevSetMoney(false) && Last() == "Dev: test money off", "dev 19b: test money off");
                career.Credits = credits0; career.ShipPaid = paid0;
            });
            if (go) yield return null;
            if (go) Check(career.Credits == credits0 && career.ShipPaid == paid0, $"dev 19b: off, a tick leaves the credits alone ({career.Credits:N0} cr)");

            // 19c: docked, refused; then 300 km (no job aboard: the test cargo run), 2,000 and 10,000 km on the same job
            if (go) go = Guard("dev chase refused", () =>
            {
                setDocked.Invoke(career, new object[] { Array.Find(Career.Ports, p => p.Name == "Ceres") });
                var r = s.DevChase(300);
                Check(r == null && career.Encounter == null && Last() == "Dev: no chase: undock first", $"dev 19c: docked, no chase: \"{said[said.Count - 1]}\"");
                setDocked.Invoke(career, new object[] { null });
                Check(Why("ChaseWhy") == null, $"dev 19c: 1e6 km off Vesta a chase can start ({Why("ChaseWhy") ?? "-"})");
            });
            foreach (double km in new[] { 300.0, 2000, 10000 })
            {
                if (go) go = Guard("dev chase " + km, () =>
                {
                    e = s.DevChase(km);
                    string note = Last();
                    var job = e?.Job;
                    bool test = job != null && job.Key == Career.DevJobKey && job.Kind == JobKind.Cargo && career.Active.Count == 1 && career.Active[0] == job;
                    Check(e != null && career.Encounter == e && Math.Abs(e.Range - km) < 1e-6 * km && (e.Pirate.Vel - ship.Vel).Mag < 1e-9 && test
                          && note == $"Dev: {e.Hull} {km:N0} km off, at rest with the ship",
                        $"dev 19c: a chase at {km:N0} km: range {e?.Range:N3} km, {(e != null ? (e.Pirate.Vel - ship.Vel).Mag : -1):G3} km/s against the ship, on the test cargo run ({test}, {career.Active.Count} job): \"{note}\"");
                });
                for (int i = 0; go && i < 3; i++) yield return null;
                if (go) Guard("dev chase on " + km, () =>
                {
                    // 300 km is inside FireKm: the raider's own rule fires at the first update. 10,000 km is outside it
                    bool fire = km < Career.FireKm ? e.Missiles.Count == 2 && e.Magazine == 4 : km > Career.FireKm ? e.Missiles.Count == 0 && e.Magazine == 6 : true;
                    Check(career.Encounter == e && fire, $"dev 19c: the {km:N0} km chase is on after 3 updates at {e.Range:N1} km: {e.Missiles.Count} inbound, {e.Magazine} aboard");
                });
            }

            // 19d: on the 10,000 km chase, outside FireKm with none in flight
            if (go) go = Guard("dev salvo", () =>
            {
                double range = (e.Pirate.Pos - ship.Pos).Mag;
                int n = s.DevSalvo();
                string note = Last();
                Check(n == 2 && e.Missiles.Count == 2 && e.Magazine == 4 && Math.Abs(e.Missiles[0].P.Mag - range) < 1e-6 && note == Career.FiredNote(2, range / Career.MissileKms),
                    $"dev 19d: a salvo at {range:N0} km: {n} fired, {e.Missiles.Count} inbound, {e.Magazine} aboard: \"{note}\"");
                Check(s.DevRefill() && e.Magazine == Career.MagazineSize && Last() == "Dev: the raider's magazine refilled (6)", $"dev 19d: refilled to {e.Magazine}");
                n = s.DevSalvo();
                Last();
                Check(n == 2 && e.Missiles.Count == 4 && e.Magazine == 4, $"dev 19d: a second salvo with two in flight: {e.Missiles.Count} inbound, {e.Magazine} aboard");
                s.DevRefill(); Last();
                Check(!s.DevRefill() && Last() == "Dev: no refill: full", "dev 19d: a full magazine is refused");
            });

            // 19e: stock radiators and no PDCs are refused; fitted, each hit is Career.Hit's
            double thrust = 0;
            if (go) go = Guard("dev hits", () =>
            {
                career.Build.pick[(int)Slot.Radiators] = 0; career.Build.pick[(int)Slot.PointDefense] = 0;
                career.Apply(ship);
                bool rad = s.DevHit(HS.Radiators); string n1 = Last();
                bool pdc = s.DevHit(HS.Pdc); string n2 = Last();
                Check(!rad && !pdc && !career.Damage.Any && n1 == "Dev: no hit: fixed radiators" && n2 == "Dev: no hit: no PDCs",
                    $"dev 19e: fixed radiators and no PDCs: no hit (\"{n1}\", \"{n2}\")");
                career.Build.pick[(int)Slot.Radiators] = 1; career.Build.pick[(int)Slot.PointDefense] = 1;
                ship.Fuel = career.TankCapacity / 2;
                career.Apply(ship);
                thrust = career.Build.Thrust;
                Check(s.DevHit(HS.Drive) && career.Damage.drive == 1 && Math.Abs(ship.Thrust - thrust / 2) < 1e-9 && Last() == "Hit: drive, thrust 50 %",
                    $"dev 19e: drive hit: thrust {ship.Thrust:0.000} of {thrust:0.000} MN");
                Check(s.DevHit(HS.Sensors) && career.Damage.sensors == 1 && career.Codex.SensorFactor == 0.5 && Last() == "Hit: sensors, range 50 %",
                    $"dev 19e: sensors hit: range ×{career.Codex.SensorFactor}");
                double fuel = ship.Fuel;
                Check(s.DevHit(HS.Tank) && Math.Abs(ship.Fuel - fuel * (1 - Career.VentShare)) < 1e-9 && Last() == $"Hit: tank, {fuel * Career.VentShare:N1} t of water vented",
                    $"dev 19e: tank hit: {fuel:N1} → {ship.Fuel:N1} t");
                Check(s.DevHit(HS.Radiators) && career.Damage.radiators && !career.DarkReady(ship) && Last() == "Hit: radiators jammed out, not dark-ready",
                    "dev 19e: radiators jammed out, not dark-ready");
                int fitted = career.Build.MountCount;
                Check(s.DevHit(HS.Pdc) && PointDefense.Count(career.Damage.mounts) == 1 && Last() == Career.MountNote(fitted - 1, fitted),
                    $"dev 19e: a PDC mount killed: {PointDefense.Count(career.WorkingMounts)} of {fitted} working");
                Check(career.DamageText() == $"drive 50 %, sensors 50 %, radiators jammed out, 1 of {fitted} PDC mounts out", $"dev 19e: the damage: {career.DamageText()}");
            });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go && toolkit) Guard("dev hud labels", () =>
            {
                string report = s.TestDevMenuReport();
                Check(report.Contains("Free repair /") && report.Contains("Fire salvo /") && report.Contains("Fill tank /"),
                    $"dev 19e: with a chase and damage the HUD's Repair, Fire salvo and Fill tank are open: {report}");
            });
            yield return Shot("c19-dev-menu");

            // 19f: the repair through the shell's own button
            bool clicked = false;
            if (go) go = Guard("dev repair click", () => clicked = s.TestClickDev("Free repair"));
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) go = Guard("dev repair", () =>
            {
                Check(clicked && !career.Damage.Any && Math.Abs(ship.Thrust - thrust) < 1e-9 && career.Codex.SensorFactor == 1 && Last() == "Dev: repaired, no charge",
                    $"dev 19f: Free repair clicked in the {(toolkit ? "UI Toolkit HUD" : "classic panel")}: damage {career.DamageText() ?? "none"}, thrust {ship.Thrust:0.000} MN");
                Check(!s.DevRepair() && Last() == "Dev: no repair: no damage", "dev 19f: no damage, no repair");
            });

            // 19g: half a tank at 50 K, filled, mixes to 25 K; the sink full and empty
            if (go) go = Guard("dev water", () =>
            {
                ship.Fuel = career.TankCapacity / 2; career.SinkK = 50;
                Check(s.DevWater() && ship.Fuel == career.TankCapacity && Math.Abs(career.SinkK - 25) < 1e-9 && Last() == $"Dev: tank filled, {career.TankCapacity:N0} t",
                    $"dev 19g: fill tank: {ship.Fuel:N1} t, sink {career.SinkK:0.##} K (25)");
                Check(!s.DevWater() && Last() == "Dev: no water: full", "dev 19g: a full tank is refused");
                Check(s.DevSink(true) && career.SinkK == Career.SinkMaxK && Last() == "Dev: heat sink full", $"dev 19g: sink full ({career.SinkK} K)");
                Check(s.DevSink(false) && career.SinkK == 0 && Last() == "Dev: heat sink empty", $"dev 19g: sink empty ({career.SinkK} K)");
            });

            // 19h: reveal; two seeded chases draw the same
            if (go) go = Guard("dev reveal and seed", () =>
            {
                s.DevReveal(true); string on = Last();
                bool shown = Signature.RevealAll;
                s.DevReveal(false); string off = Last();
                Check(shown && !Signature.RevealAll && on == "Dev: all traffic shown" && off == "Dev: traffic as the sensors see it", "dev 19h: reveal all traffic on and off");
                int seed = (int)SceneStatic("DevSeed");
                s.DevSeedCombat(true); Last();
                s.DevChase(10000); Last();
                var r = (System.Random)rngField.GetValue(career);
                double a1 = r.NextDouble(), a2 = r.NextDouble();
                s.DevChase(10000); Last();
                r = (System.Random)rngField.GetValue(career);
                double b1 = r.NextDouble(), b2 = r.NextDouble();
                var want = new System.Random(seed);
                Check(a1 == b1 && a2 == b2 && a1 == want.NextDouble() && a2 == want.NextDouble(), $"dev 19h: seeded, two test chases draw {a1:F6}, {a2:F6} and {b1:F6}, {b2:F6} (seed {seed})");
                s.DevSeedCombat(false); Last();
                Check(!(bool)SceneStatic("devSeeded"), "dev 19h: seed off");
                career.DropContact();
                career.Active.RemoveAll(c => c.Key == Career.DevJobKey);
            });

            // 19i: an hour at 10k× and a day at 100k×, each to its stop; one stopped by a drop to 1×; one refused
            foreach (double hours in new[] { 1.0, 24 })
            {
                double j0 = jd;
                if (go) go = Guard("dev skip " + hours, () =>
                {
                    bool ok = s.DevSkip(hours);
                    string note = Last();
                    Check(ok && Get<int>("warpIdx") == (hours < 24 ? 4 : 5) && s.QaStopJd == j0 + hours / 24 && note.StartsWith($"Dev: skipping {(hours < 24 ? "1 h at 10k×" : "1 d at 100k×")}", StringComparison.Ordinal),
                        $"dev 19i: +{hours} h: warp {Get<int>("warpIdx")}, stop {(s.QaStopJd - j0) * 24:F3} h on: \"{note}\"");
                });
                int frames = 0;
                for (; go && s.DevSkipping && frames < 900; frames++) yield return null;
                if (go) Guard("dev skip done " + hours, () =>
                {
                    double ran = (jd - j0) * 86400;
                    Check(!s.DevSkipping && Math.Abs(ran - hours * 3600) < 1 && Get<int>("warpIdx") == 0 && double.IsPositiveInfinity(s.QaStopJd) && ship.Crashed == null
                          && Last().StartsWith("Dev: skip done: ", StringComparison.Ordinal),
                        $"dev 19i: +{hours} h ran {ran:F2} s of sim time in {frames} frames, back at 1×: \"{said[said.Count - 1]}\"");
                });
            }
            if (go) go = Guard("dev skip stop", () => Check(s.DevSkip(24), "dev 19i: a third skip starts"));
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) Put("warpIdx", 0);
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("dev skip stopped", () =>
            {
                Check(!s.DevSkipping && double.IsPositiveInfinity(s.QaStopJd) && Last().StartsWith("Dev: skip stopped at 1×, ", StringComparison.Ordinal),
                    $"dev 19i: dropped to 1×, the skip stops: \"{said[said.Count - 1]}\"");
                s.QaStopJd = jd + 1;
                Check(!s.DevSkip(1) && Last() == "Dev: no skip: QA stop set", "dev 19i: a QA stop date refuses a skip");
                s.QaStopJd = double.PositiveInfinity;
            });

            // 19j: what makes no sense in the sandbox says so; reveal, seed and the skip stay
            if (go) go = Guard("dev sandbox", () =>
            {
                career.Sandbox = true;
                var whys = new List<string> { Why("SandboxWhy"), Why("ChaseWhy"), Why("SalvoWhy"), Why("RefillWhy"), Why("RepairWhy"), Why("WaterWhy"), Why("SinkWhy") };
                foreach (HS h in Enum.GetValues(typeof(HS))) whys.Add(R<string>("HitWhy", h));
                bool money = s.DevSetMoney(true);
                string skip = Why("SkipWhy");
                career.Sandbox = false;
                Check(whys.TrueForAll(w => w == "sandbox") && !money && skip == null, $"dev 19j: sandbox: {string.Join(", ", whys)}; test money refused ({!money}); the skip open ({skip ?? "-"})");
                Last();
            });

            // 19k
            if (go) go = Guard("dev close", () => { s.ToggleDevMenu(); Check(!s.DevMenuOpen, "dev 19k: F7's toggle closes the menu"); });
            yield return null;
            int painted = s.TestDevClassicFrame;
            yield return null; yield return null;
            if (go) Guard("dev closed", () =>
            {
                if (toolkit) Check(s.TestDevMenuReport().StartsWith("None", StringComparison.Ordinal), $"dev 19k: the HUD hides the menu: {s.TestDevMenuReport()}");
                else Check(s.TestDevClassicFrame == painted, $"dev 19k: the classic panel stops painting the menu (frame {s.TestDevClassicFrame})");
            });

            // 19l
            var texts = said.FindAll(t => t.Length > 0);
            float[] widths = null;
            if (go) go = Guard("dev notes setup", () => { Put("noteWidths", null); Put("noteProbe", texts.ToArray()); });
            for (int i = 0; go && i < 10 && widths == null; i++) { yield return null; widths = Get<float[]>("noteWidths"); }
            if (go) Guard("dev notes width", () =>
            {
                if (widths == null || widths.Length != texts.Count) { Check(false, $"dev 19l: the scene measured {widths?.Length.ToString() ?? "nothing"} of {texts.Count} notes in 10 frames"); return; }
                float strip = (float)typeof(OpenSystemScene).GetField("NoteW", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                int widest = 0;
                for (int i = 1; i < widths.Length; i++) if (widths[i] > widths[widest]) widest = i;
                Check(widths[widest] <= strip, $"dev 19l: the {texts.Count} notes fit the classic strip; the widest {widths[widest]:F0} of {strip:F0} px: \"{texts[widest]}\"");
            });

            Guard("dev cleanup", () =>
            {
                career.DropContact();
                career.Active.Clear(); career.Active.AddRange(active0);
                career.Build = build0; career.Damage = damage0;
                career.Sandbox = sandbox0;
                ship.Fuel = fuel0;
                career.Apply(ship);
                ship.MaxG = maxG0;
                career.SinkK = sink0;
                career.Credits = credits0; career.ShipPaid = paid0;
                Signature.RevealAll = reveal0;
                rngField.SetValue(career, rng0);
                foreach (string f in new[] { "devMoney", "devSeeded" }) typeof(OpenSystemScene).GetField(f, BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
                Put("devMenu", false);
                Put("warpIdx", 0);
                s.QaStopJd = double.PositiveInfinity;
                setDocked.Invoke(career, new object[] { dock0 });
                ship.Target = target0;
            });
            yield return null;
        }
    }
}
