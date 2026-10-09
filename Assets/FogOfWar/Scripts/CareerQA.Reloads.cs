using System;
using System.Collections;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // Career QA scenarios that rebuild the scene: the Vesta trip estimate, docking and Continue with a rescue, touchdown
    // and crash saves, a new game with a rescue active, and the tutorial across Continue.
    public partial class CareerQA
    {
        IEnumerator ReloadChecks()
        {
            // QA-061: Vesta 0.9 AU off while coasting away from it at 446 km/s, 300 km/s sideways. The card read the trip
            // from rest (2·√(a·d)) and the tank ran dry short; the plan has to stop first. The estimate must cover what
            // the autopilot flies, and a tank that can't finish it must not engage on the first press.
            {
                app.Restart(app.GameIndex);
                yield return Frames(1f);
                s = (OpenSystemScene)app.Current;
                Put("warpIdx", 0);
                var vesta = sys["Vesta"];
                var dir = sys.Pos(vesta, jd).Norm;
                var side = D3.Cross(dir, new D3(0, 0, 1)).Norm;
                var pos0 = sys.Pos(vesta, jd) - dir * (0.9 * Frame.AU);
                var vel0 = sys.Vel(vesta, jd) - dir * 446 + side * 300;
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Landed = null;
                ship.Pos = pos0; ship.Vel = vel0; ship.Fuel = career.TankCapacity;
                Call("SetTarget", vesta);
                Put("cruiseIdx", 0);
                Put("selectedPlan", Plan.Brachistochrone);
                double d = Math.Max((sys.Pos(vesta, jd) - ship.Pos).Mag - Ship.Standoff(vesta), 0);
                var (u, w) = ship.RelToTarget(sys, jd);
                Career.Trip(d, ship.AMax, 1, ship.Mass, out double estT, out double estDv, out _, u, w);
                double fromRest = 2 * Math.Sqrt(ship.AMax * d);
                log.WriteLine($"  QA-061 setup: {d / Frame.AU:F3} AU, closing {u:F0} km/s, sideways {w:F0} km/s; from rest {fromRest:F0} km/s, from state {estDv:F0} km/s, Δv left {Career.DvLeft(ship):F0}");

                // tank holding 90 % of the estimate: the first press warns, the second goes anyway
                ship.Fuel = (ship.DryMass + ship.Cargo) * (Math.Exp(0.9 * estDv / Ship.Ve) - 1);
                Call("TogglePlan");
                Check(!ship.PlanActive && ship.Phase.StartsWith("fuel short"), $"short tank ({Career.DvLeft(ship):F0} km/s left): first engage warns ({ship.Phase})");
                Call("TogglePlan");
                Check(ship.PlanActive, "second engage goes anyway");
                ship.Plan = Plan.None;

                // full tank: fly it and compare
                ship.Pos = pos0; ship.Vel = vel0; ship.Fuel = career.TankCapacity;
                double dv0 = ship.DvUsed, jd0 = jd;
                Call("TogglePlan");
                Check(ship.PlanActive, $"full tank ({Career.DvLeft(ship):F0} km/s left) engages at once");
                yield return Fly(() => ship.Phase == "in orbit" || ship.Dry, 5, 120);
                Put("warpIdx", 0);
                double flown = ship.DvUsed - dv0;
                log.WriteLine($"  QA-061 flew {(jd - jd0) * 24:F1} h (est {estT / 3600:F1} h), Δv {flown:F0} km/s = {flown / estDv:F2} × estimate, phase {ship.Phase}");
                Check(ship.Phase == "in orbit" && flown <= estDv * 1.1, $"arrives at Vesta within the 10 % margin ({flown:F0} of {estDv:F0} km/s)");
                ship.Plan = Plan.None;
            }

            // #23: a radial fall toward Luna is not docked, a parking orbit is; #18: Continue on a save made mid-plan at
            // 100k× with a rescue targeted loads at 1×, says the plan was not resumed and targets the rebuilt derelict
            {
                app.Restart(app.GameIndex);
                yield return Frames(1f);
                s = (OpenSystemScene)app.Current;
                Put("warpIdx", 0);
                var moon = sys.Moon;
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Landed = null;
                ship.Pos = sys.Pos(moon, jd) + new D3(2000, 0, 0); ship.Vel = sys.Vel(moon, jd) + new D3(-1, 0, 0);
                yield return null; yield return null;
                Check(career.Docked == null, $"radial fall 263 km above Luna is not docked ({career.Docked?.Name ?? "none"})");
                ship.Pos = sys.Pos(moon, jd) + new D3(5000, 0, 0); ship.Vel = sys.Vel(moon, jd) + new D3(0, Math.Sqrt(moon.GM / 5000), 0);
                yield return null; yield return null;
                Check(career.Docked?.Name == "Luna", $"circular orbit at Luna docks ({career.Docked?.Name ?? "none"})");
                // docked, dry and in debt was a dead end: the tender comes only out there, and fuel needed credits
                if (career.Docked != null)
                {
                    double fuelWas = ship.Fuel, crWas = career.Credits;
                    ship.Fuel = 0; career.Credits = -5000;
                    career.BuyFuel(ship, career.TankCapacity);
                    double price = career.Docked.FuelPrice;
                    Check(Math.Abs(ship.Fuel - Career.EmergencyTonnes) < 0.01 && Math.Abs(career.Credits - (-5000 - Career.EmergencyTonnes * price)) < 1,
                        $"docked, dry and broke: the port sells a tender's load on credit ({ship.Fuel:N1} t, {career.Credits:N0} cr)");
                    career.BuyFuel(ship, 20);
                    Check(Math.Abs(ship.Fuel - Career.EmergencyTonnes) < 0.01, $"no more credit past a tender's load ({ship.Fuel:N1} t)");
                    ship.Fuel = fuelWas; career.Credits = crWas;
                }
                var rj = career.Docked != null ? career.Rescue(new System.Random(5), sys, jd) : null;
                if (rj != null) career.Accept(rj, jd);
                if (rj != null && career.Active.Contains(rj))
                {
                    Call("SetTarget", sys[rj.To]);
                    Put("selectedPlan", Plan.Brachistochrone);
                    Call("TogglePlan");
                    Put("warpIdx", 5);
                    yield return null;
                    s.SaveGame();
                    app.Restart(app.GameIndex);
                    yield return Frames(1f);
                    s = (OpenSystemScene)app.Current;
                    s.LoadGame();
                    yield return null;
                    var wreck = sys[rj.To];
                    Check(wreck != null && ship.Target == wreck, $"Continue targets the rebuilt derelict ({ship.Target?.Name ?? "none"})");
                    Check(Get<int>("warpIdx") == 0 && !ship.PlanActive && career.Notes.Exists(n => n.Text.Contains("Not resumed")),
                        $"Continue starts at 1× with the plan dropped and says so (warp {Get<int>("warpIdx")})");
                }
                else log.WriteLine("  (rescue not accepted: Continue check skipped)");
            }

            // #25: a slow touchdown lands; a fast one destroys the ship, and the last save comes back. Landing at Luna (a
            // port) autosaves, so that save is the landing, not the parking orbit saved by hand before it.
            {
                app.Restart(app.GameIndex);
                yield return Frames(1f);
                s = (OpenSystemScene)app.Current;
                Put("warpIdx", 0);
                string saveKey = SaveKey;
                string kept = PlayerPrefs.GetString(saveKey);
                var first = career.Board.Count > 0 ? career.Board[0] : null;
                if (first != null) career.Accept(first, jd);
                yield return null; yield return null;
                Check(first != null && career.Active.Contains(first) && PlayerPrefs.GetString(saveKey) == kept,
                    "a new career still docked at Earth does not autosave over the old save (a job accepted)");
                var moon = sys.Moon;
                var up = new D3(1, 0, 0);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Landed = null;
                ship.Pos = sys.Pos(moon, jd) + up * 5000; ship.Vel = sys.Vel(moon, jd) + new D3(0, Math.Sqrt(moon.GM / 5000), 0);
                s.SaveGame();
                ship.Pos = sys.Pos(moon, jd) + up * (Ship.SurfaceRadius(moon, up) + 0.02); ship.Vel = sys.Vel(moon, jd) - up * 0.005;
                yield return Fly(() => ship.Landed != null, 0, 20);
                Check(ship.Landed == moon && ship.Phase.StartsWith("landed") && ship.ImpactSpeed < Ship.SafeTouchdown,
                    $"a {ship.ImpactSpeed * 1000:F0} m/s touchdown lands ({ship.Phase})");
                yield return null; yield return null;
                Check(PlayerPrefs.GetString(saveKey).Contains("\"landed\":\"Moon\""), "landing at Luna, a port, autosaves");
                ship.Landed = null;
                ship.Pos = sys.Pos(moon, jd) + up * (Ship.SurfaceRadius(moon, up) + 50); ship.Vel = sys.Vel(moon, jd) - up * 2;
                yield return Fly(() => career.Notes.Exists(n => n.Text.StartsWith("Destroyed")), 1, 30);
                Put("warpIdx", 0);
                double alt = (ship.Pos - sys.Pos(moon, jd)).Mag;
                Check(career.Notes.Exists(n => n.Text.StartsWith("Destroyed: hit Moon")) && ship.Landed == moon && Math.Abs(alt - Ship.SurfaceRadius(moon, up)) < 1,
                    $"a 2 km/s impact destroys the ship and reloads the autosaved landing ({alt:F0} km from Luna's centre, {ship.Phase})");
            }

            // New game while a rescue is active: the rebuilt scene must not touch the old wreck's materials
            {
                int before = exceptions;
                for (int round = 0; round < 2; round++)
                {
                    app.Restart(app.GameIndex); // a new career starts docked at Earth orbit
                    yield return Frames(1f);
                    s = (OpenSystemScene)app.Current;
                    Put("warpIdx", 0);
                    var wreckJob = career.Rescue(new System.Random(11 + round), sys, jd);
                    career.Accept(wreckJob, jd);
                    yield return Frames(1f); // the wreck is built and drawn
                }
                Check(career.Active.Exists(c => c.Kind == JobKind.Rescue) && exceptions == before, $"new game with a rescue active: {exceptions - before} exceptions");
            }

            // QA-051: Continue keeps the tutorial where the save left it, and a skipped tutorial stays skipped
            {
                app.Restart(app.GameIndex);
                yield return Frames(1f);
                s = (OpenSystemScene)app.Current;
                yield return null;
                string running = JsonUtility.ToJson(career.ToSave(ship));        // step 1 of 3 in progress
                Call("SkipTutorial");
                yield return null;
                string skipped = JsonUtility.ToJson(career.ToSave(ship));
                Check(s.TutorialText == null && career.TutorialStep == 0, "skip ends the tutorial");
                foreach (var (saved, wantOn, what) in new[] { (skipped, false, "skipped"), (running, true, "in progress") })
                {
                    app.Restart(app.GameIndex);
                    yield return Frames(1f);
                    s = (OpenSystemScene)app.Current;
                    career.FromSave(JsonUtility.FromJson<CareerSave>(saved), ship);
                    Call("ResumeTutorial");
                    yield return null; yield return null;
                    Check((s.TutorialText != null) == wantOn, $"Continue on a save with the tutorial {what}: card {(s.TutorialText != null ? "shown" : "hidden")}");
                }
                // a save from before the tutorial was saved (no field) counts as done
                app.Restart(app.GameIndex);
                yield return Frames(1f);
                s = (OpenSystemScene)app.Current;
                var old = JsonUtility.FromJson<CareerSave>(skipped.Replace("\"tutorial\":0,", ""));
                career.FromSave(old, ship);
                Call("ResumeTutorial");
                yield return null;
                Check(s.TutorialText == null, "an older save without tutorial state stays quiet");
            }
        }
    }
}
