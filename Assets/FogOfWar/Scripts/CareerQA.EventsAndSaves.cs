using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // Career QA sections 5-6: route events, manual delivery, save migration, ownership and standing.
    public partial class CareerQA
    {
        // A finite trip can legitimately roll no call. Exercise the first eligible production roll, not chance.
        sealed class RouteCallRoll : System.Random
        {
            public int Draws;
            public override double NextDouble() { Draws++; return 0; }
        }

        IEnumerable<object> EventAndSaveChecks()
        {
            DryTankChecks(); // 5a: #151, #152 the dry tank at a port
            // 5. a dry tank cuts the drive; the tender refills it
            ship.Plan = Plan.None; ship.Fuel = 0; ship.ThrottleCmd = 1;
            var v0 = ship.Vel;
            yield return Frames(0.5f);
            ship.ThrottleCmd = 0;
            Check(ship.Throttle == 0, "dry tank: drive does not fire");
            yield return Shot("c06-dry");
            double cr = career.Credits;
            career.EmergencyDrop(ship);
            Check(Math.Abs(ship.Fuel - Career.EmergencyTonnes) < 1e-6 && career.Credits < cr, "emergency tender adds reaction mass for a fee");

            // 5b. the express window: bonus inside it, plain pay after it, half after the deadline; a job from an older
            // save (no window) pays plain
            {
                var x = new Contract { Pay = 10000, IssuedJd = 100, DeadlineJd = 104, ExpressDays = 2, ExpressJd = 102 };
                double inside = Career.PayAt(x, 101.9, out bool l1, out bool e1), after = Career.PayAt(x, 103, out bool l2, out bool e2);
                double late = Career.PayAt(x, 105, out bool l3, out bool e3);
                x.ExpressDays = 0; x.ExpressJd = 0;
                double old = Career.PayAt(x, 100.5, out _, out bool e4);
                Check(inside == 12500 && e1 && !l1 && after == 10000 && !e2 && !l2 && late == 5000 && l3 && !e3 && old == 10000 && !e4,
                    $"express pay: {inside:N0} inside the window, {after:N0} after it, {late:N0} late, {old:N0} with no window");
                int n = 0, bad = 0;
                foreach (var b in career.Board) if (b.Kind != JobKind.Intercept) { n++; if (!(b.ExpressDays > 0 && b.ExpressDays <= b.Days)) bad++; }
                Check(bad == 0, $"every board job but an intercept has an express window inside its deadline ({n - bad} of {n})");

                // what the old passenger floor promised: a deadline gives a 1 g ship (the most passengers allow) its full-burn
                // time with a 15 % margin. RefPay's least slack gives 29 %, which also covers the planets' drift since posting.
                int pn = 0, pShort = 0;
                foreach (var b in career.Board)
                {
                    if (b.Kind != JobKind.Passengers) continue;
                    Port pFrom = Career.PortAt(b.From), pTo = Career.PortAt(b.To);
                    if (pFrom == null || pTo == null || sys[pFrom.Body] == null || sys[pTo.Body] == null) continue;
                    pn++;
                    double pd = (sys.Pos(sys[pTo.Body], jd) - sys.Pos(sys[pFrom.Body], jd)).Mag;
                    double pNeed = 1.15 * 2 * Math.Sqrt(pd / (Career.PassengerG * Ship.G0)) + 43200;
                    if (b.Days * 86400 < pNeed) pShort++;
                }
                Check(pn > 0 && pShort == 0, $"every board passenger job gives a 1 g ship its full-burn time with a 15 % margin ({pn - pShort} of {pn})");
            }

            // 5c. route events: calls in flight about a job (a test cargo run to Mars, removed afterwards)
            {
                Check(!career.RouteEvents, "route events are off in the unattended suite");
                double fuelWas = ship.Fuel;
                ship.Fuel = career.TankCapacity;
                var mars = sys["Mars"];
                var run = new Contract { Id = 900001, Kind = JobKind.Cargo, Title = "QA coolant", From = "Luna", To = "Mars orbit", Tonnes = 10, Pay = 10000, Days = 100, Key = "qa/route" };
                run.IssuedJd = jd; run.DeadlineJd = jd + run.Days;
                career.Active.Add(run);
                Call("SetTarget", mars);
                yield return null;

                // a distress call: a rescue near Mars, with the rest of this trip in its time; yes takes it on
                Check(career.MakeCall(Career.RouteKind.Distress, run, sys, ship, jd) && career.Call?.Offer != null, "a distress call comes in");
                var offer = career.Call?.Offer;
                var (ru, rw) = ship.RelToTarget(sys, jd);
                Career.Trip(Math.Max((sys.Pos(mars, jd) - ship.Pos).Mag - Ship.Standoff(mars), 0), ship.AMax, 1, ship.Mass, out double burn, out _, out _, ru, rw);
                career.Answer(true, jd);
                yield return null;
                Check(offer != null && career.Active.Contains(offer) && sys[offer.To] != null && sys[offer.To].Parent == mars && offer.Days > burn / 86400 && offer.ExpressDays == 0 && career.Call == null,
                    $"answering takes on the rescue: its wreck orbits Mars, {offer?.Days:F1} days allowed for a {burn / 86400:F1}-day trip");
                if (offer != null) career.Abandon(offer);
                career.MakeCall(Career.RouteKind.Distress, run, sys, ship, jd);
                var ignored = career.Call?.Offer;
                career.Answer(false, jd);
                Check(ignored != null && !career.Active.Contains(ignored) && sys[ignored.To] == null && career.Call == null, "ignoring a distress call takes nothing on");

                // cargo trouble: venting costs the fuel shown; pushing on halves the pay after the spoil time
                career.MakeCall(Career.RouteKind.CargoTrouble, run, sys, ship, jd);
                double vent = career.Call?.VentTonnes ?? -1, f0 = ship.Fuel;
                career.Answer(true, jd);
                Check(vent > 0 && Math.Abs(f0 - ship.Fuel - vent) < 1e-6 && run.SpoilJd == 0, $"venting costs the {vent:N1} t shown and leaves the cargo whole");
                career.MakeCall(Career.RouteKind.CargoTrouble, run, sys, ship, jd);
                double spoilDays = career.Call?.SpoilDays ?? -1;
                career.Answer(false, jd);
                Check(spoilDays > burn / 86400 && Math.Abs(run.SpoilJd - (jd + spoilDays)) < 1e-9 && Career.PayAt(run, run.SpoilJd - 0.01, out _, out _) == 10000 && Career.PayAt(run, run.SpoilJd + 0.01, out _, out _) == 5000,
                    $"pushing on: full pay within {spoilDays * 24:F0} h, half after (a full burn takes {burn / 3600:F0} h)");
                run.SpoilJd = 0;

                // a real roll: flying the job with events on, a call comes in and drops the warp
                yield return null; // the calls above left DropWarp set: let the scene take it before the warp goes up
                // The preceding survey can end in raider space. This ordinary-call fixture is an inner-system leg.
                var earth = sys.Pos(sys.Earth, jd);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0; ship.Landed = null;
                ship.Pos = earth + earth.Norm * 1e6; ship.Vel = sys.Vel(sys.Earth, jd);
                career.RouteEvents = true;
                Put("cruiseIdx", 1);
                Put("selectedPlan", Plan.Brachistochrone);
                Call("TogglePlan");
                if (!ship.PlanActive && ship.Phase.StartsWith("fuel short")) Call("TogglePlan");
                Put("warpIdx", 6);
                float t0 = Time.realtimeSinceStartup;
                double jd0 = jd, rateMax = 0, presenceMax = 0;
                int callWarp = -1;
                var rngField = typeof(Career).GetField("routeRng", F);
                var rng = rngField.GetValue(career); var roll = new RouteCallRoll();
                rngField.SetValue(career, roll);
                try
                {
                    // Capture at arrival, before Fly restores warp. A missing production DropWarp must fail this check.
                    yield return Fly(() =>
                    {
                        rateMax = Math.Max(rateMax, career.PirateRate(sys, ship, run, jd));
                        presenceMax = Math.Max(presenceMax, Threat.Presence(sys, ship.Pos, jd));
                        if (career.Call == null) return false;
                        callWarp = Get<int>("warpIdx");
                        return true;
                    }, 6, 30);
                    yield return null;
                    Check(career.Call != null && callWarp == 0 && roll.Draws >= 2, $"in flight a call comes in and drops the warp ({career.Call?.Title ?? "none"} after {Time.realtimeSinceStartup - t0:F1} s; plan {ship.PlanActive} {ship.Phase}, docked {career.Docked?.Name ?? "-"}, landed {ship.Landed?.Name ?? "-"}, target {ship.Target?.Name ?? "-"}, arrival warp {callWarp}, jd +{(jd - jd0) * 24:F1} h, active {career.Active.Count}, roll draws {roll.Draws})");
                    // Q3, before the answer and the job's removal, either of which would end a chase: the leg gives raiders nothing
                    Check(career.Call?.Kind != Career.RouteKind.Pirate && career.Encounter == null && rateMax == 0 && presenceMax == 0,
                        $"no raider on the real roll: the call is {career.Call?.Kind.ToString() ?? "none"}, no chase; over the first {(jd - jd0) * 24:F1} h of the trip to Mars, sampled each frame up to the call, raider presence {presenceMax:0.###} and rate {rateMax:0.###} an hour at most");
                    yield return Shot("c05c-route-call");
                }
                finally { rngField.SetValue(career, rng); }
                career.RouteEvents = false;
                career.Answer(false, jd);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0;
                run.SpoilJd = 0;
                career.Active.Remove(run);
                yield return null;
                Check(career.Call == null, "with the job gone, no call is left");
                ship.Fuel = fuelWas;
            }

            // 5d. flown in by hand: the helm taken outside the dock range, then a bound orbit with the drive off, pays the
            // bonus; the helm taken inside the range does not. The ship is put in place, not flown (test cargo runs to Mars)
            {
                var mars = sys["Mars"];
                double range = Career.DockRange(mars);
                var rdir = (sys.Pos(mars, jd) - sys.Pos(sys.Sun, jd)).Norm;
                var vdir = D3.Cross(D3.Cross(rdir, sys.Vel(mars, jd).Norm), rdir).Norm;
                double rOrbit = Ship.Standoff(mars);
                foreach (var (from, paid) in new[] { (1.5, true), (0.8, false) })
                {
                    var run = new Contract { Id = 900002, Kind = JobKind.Cargo, Title = "QA ore", From = "Luna", To = "Mars orbit", Tonnes = 10, Pay = 10000, Days = 100, Key = "qa/hand" };
                    run.IssuedJd = jd; run.DeadlineJd = jd + run.Days;
                    // the autopilot flies a frame, then the pilot takes the helm at rest relative to Mars, `from` × the dock range out
                    // (a fall with no orbit: not docked, so the job, added after, is not paid there)
                    ship.Landed = null; ship.ThrottleCmd = 0; ship.Throttle = 0;
                    Call("SetTarget", mars);
                    ship.Pos = sys.Pos(mars, jd) + rdir * (range * from); ship.Vel = sys.Vel(mars, jd);
                    ship.Plan = Plan.Brachistochrone;
                    yield return null;
                    ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0;
                    ship.Pos = sys.Pos(mars, jd) + rdir * (range * from); ship.Vel = sys.Vel(mars, jd);
                    career.Active.Add(run);
                    yield return null;
                    bool shown = career.HandArrival(run) == paid;
                    // then a circular orbit at the autopilot's parking height, drive off: the port docks it and pays
                    double handC0 = career.Credits;
                    ship.Pos = sys.Pos(mars, jd) + rdir * rOrbit; ship.Vel = sys.Vel(mars, jd) + vdir * Math.Sqrt(mars.GM / rOrbit);
                    yield return null;
                    yield return null;
                    double got = career.Credits - handC0, want = paid ? Math.Round(run.Pay * (1 + Career.HandBonus)) : run.Pay;
                    Check(!career.Active.Contains(run) && career.Docked?.Name == "Mars orbit" && shown && Math.Abs(got - want) < 1e-6
                          && paid == career.Notes.Exists(n => n.Text.Contains("by hand")),
                        $"helm taken {from} × the dock range out ({range * from:N0} km), docked in orbit: {got:N0} cr, {(paid ? "flown-in-by-hand bonus" : "no bonus")}{(shown ? "" : ", job card wrong")}");
                    career.Active.Remove(run);
                    career.Notes.Clear();
                }
            }

            // the job pay the buyout price is set against (a load clears the board, so before section 6)
            {
                double boardPay = 0; foreach (var b in career.Board) boardPay += b.Pay;
                log.WriteLine($"  buyout price {Career.ShipPrice:N0} cr; the board at {career.Docked?.Name ?? "no port"} posts {career.Board.Count} jobs, mean pay {boardPay / Math.Max(1, career.Board.Count):N0} cr");
            }

            // 6. the career save round-trips (through JSON only: the player's own save slot is left alone)
            double credits = career.Credits, fuel = ship.Fuel; int done = career.Done;
            var json = JsonUtility.ToJson(career.ToSave(ship));
            career.Credits = -1; ship.Fuel = 0;
            career.FromSave(JsonUtility.FromJson<CareerSave>(json), ship);
            Check(Math.Abs(career.Credits - credits) < 1e-6 && Math.Abs(ship.Fuel - fuel) < 1e-6 && career.Done == done, "save and load keep credits, reaction mass and record");

            // 6b. the long goal: the ship bought out from the yard in steps at a port; a save keeps it, an old save has none paid
            {
                double bank = career.Credits, paid0 = career.ShipPaid;
                career.ShipPaid = 0; career.Notes.Clear();
                career.Credits = Career.ShipStep - 1;
                bool poor = !career.PayTowardShip();
                career.Credits = Career.ShipPrice + 1000;
                int steps = 0; while (steps < 100 && career.PayTowardShip()) steps++;
                int ownedNotes = career.Notes.FindAll(n => n.What == Career.Happening.ShipOwned).Count;
                bool feat = career.Codex.CheckFeats(career, ship).Contains("Owner-operator");
                Check(career.Docked != null && poor && steps == (int)(Career.ShipPrice / Career.ShipStep) && career.OwnsShip && Math.Abs(career.Credits - 1000) < 1e-6
                      && ownedNotes == 1 && feat && !career.PayTowardShip(),
                    $"the ship bought out in {steps} steps of {Career.ShipStep:N0} cr at {career.Docked?.Name}: owned {career.OwnsShip}, {career.Credits:N0} cr left, {ownedNotes} owned note, feat {feat}{(poor ? "" : ", paid without the credits")}");
                var sj = JsonUtility.ToJson(career.ToSave(ship));
                career.ShipPaid = 0;
                career.FromSave(JsonUtility.FromJson<CareerSave>(sj), ship);
                bool kept = career.OwnsShip;
                career.FromSave(JsonUtility.FromJson<CareerSave>(System.Text.RegularExpressions.Regex.Replace(sj, "\"shipPaid\":[^,]*,", "")), ship);
                Check(kept && career.ShipPaid == 0 && sj.Contains("\"shipPaid\""), $"a save keeps the buyout; a save from before it loads with {career.ShipPaid:N0} cr paid");
                career.Credits = bank; career.ShipPaid = paid0; career.Codex.Achieved.Remove("owner"); career.Notes.Clear();
            }

            // 6c. standing (Career.Factions.cs): every port has an owner; the run's own jobs moved it; on time it goes up, late
            // it stays, an abandon or an expiry takes more off; a new tier says so and reprices the day's board; a save keeps
            // it and an old save loads neutral. Test jobs to the port the ship is docked at; the career is put back after
            {
                var keep = JsonUtility.ToJson(career.ToSave(ship));
                var here = career.Docked;
                Check(Career.PortAt("Earth orbit").Owner == Faction.Guard && Career.PortAt("Luna").Owner == Faction.Guard && Career.PortAt("Mars orbit").Owner == Faction.Tharsis
                      && Career.PortAt("Vesta").Owner == Faction.Tharsis && Array.TrueForAll(new[] { "Ceres", "Ganymede", "Callisto", "Titan", "Enceladus" }, p => Career.PortAt(p).Owner == Faction.Compact),
                    "every port has its owner: the Guard at Earth and Luna, Tharsis at Mars and Vesta, the Compact at the ice ports");
                // jobs posted at Earth and Luna were paid (Guard); the distress rescue near Mars was taken on and dropped (Tharsis)
                Check(career.Standing(Faction.Guard) > 0 && career.Standing(Faction.Tharsis) < 0,
                    $"the run's jobs moved standing: Guard {career.Standing(Faction.Guard):+0;-0;0}, Tharsis {career.Standing(Faction.Tharsis):+0;-0;0}, Compact {career.Standing(Faction.Compact):+0;-0;0}");

                int n = 0;
                Contract Job(string from, double issuedAgo, double days) => new Contract
                {
                    Id = 900100 + n, Kind = JobKind.Cargo, Title = "QA standing", From = from, To = here?.Name, Tonnes = 1, Pay = 1000,
                    IssuedJd = jd - issuedAgo, DeadlineJd = jd - issuedAgo + days, Days = days, Key = "qa/standing/" + n++,
                };
                int Cx() => career.Standing(Faction.Compact);
                int s0 = Cx();
                career.Active.Add(Job("Ceres", 0, 10));
                yield return null; yield return null;
                int s1 = Cx();
                career.Active.Add(Job("Titan", 3, 2));            // 1 day late, not yet failed: half pay
                yield return null; yield return null;
                int s2 = Cx();
                var drop = Job("Ganymede", 0, 10);
                career.Active.Add(drop); career.Abandon(drop);     // before a tick can deliver it
                int s3 = Cx();
                career.Active.Add(Job("Callisto", 10, 2));        // far past twice its time: fails before it can pay
                yield return null; yield return null;
                int s4 = Cx();
                Check(here != null && s1 - s0 == Career.StandingGain && s2 == s1 && s3 - s2 == -Career.StandingLoss && s4 - s3 == -Career.StandingLoss && !career.Active.Exists(x => x.Title == "QA standing"),
                    $"Compact standing: {s0:+0;-0;0}, on time {s1:+0;-0;0}, late {s2:+0;-0;0}, abandoned {s3:+0;-0;0}, expired {s4:+0;-0;0} (docked at {here?.Name ?? "-"})");

                // one job short of Known with this port's owner: the next delivery here makes the tier and reprices the board
                if (here != null)
                {
                    var owner = here.Owner;
                    var cs = career.ToSave(ship);
                    cs.standing[(int)owner] = 4;
                    career.FromSave(cs, ship);
                    yield return null;
                    var before = new System.Collections.Generic.Dictionary<string, double>();
                    foreach (var b in career.Board) if (b.Kind == JobKind.Cargo || b.Kind == JobKind.Passengers) before[b.Key] = b.Pay;
                    career.Notes.Clear();
                    career.Active.Add(Job(here.Name, 0, 10));
                    yield return null; yield return null;
                    double was = 0, now = 0; int same = 0;
                    foreach (var b in career.Board) if (before.TryGetValue(b.Key, out double p)) { was += p; now += b.Pay; same++; }
                    bool note = career.Notes.Exists(x => x.What == Career.Happening.Standing && x.Good && x.Text.Contains("Known"));
                    Check(career.TierName(owner) == "Known" && Math.Abs(career.PayFactor(owner) - 1.05) < 1e-9 && note && same > 0 && same == before.Count && now / was > 1.035 && now / was < 1.065,
                        $"{Career.Factions[(int)owner].Name} at {career.Standing(owner):+0;-0;0}: {career.TierName(owner)}, note {note}; {same} of {before.Count} board jobs repriced ×{now / Math.Max(1, was):F3}");
                    career.Notes.Clear();
                    var lose = Job(here.Name, 0, 10);
                    career.Active.Add(lose); career.Abandon(lose);
                    note = career.Notes.Exists(x => x.What == Career.Happening.Standing && !x.Good && x.Text.Contains("Neutral"));
                    Check(career.TierName(owner) == "Neutral" && career.PayFactor(owner) == 1 && note, $"an abandon drops back to {career.TierName(owner)} ({career.Standing(owner):+0;-0;0}), note {note}");
                }

                // the tier table at its edges, the clamp, a save round trip and a save from before factions
                {
                    var cs = career.ToSave(ship);
                    var tiers = new System.Text.StringBuilder();
                    bool edges = true;
                    foreach (var (v, name, pay) in new[] { (-5, "Distrusted", 0.90), (-4, "Neutral", 1.0), (4, "Neutral", 1.0), (5, "Known", 1.05), (14, "Known", 1.05),
                                                          (15, "Trusted", 1.10), (29, "Trusted", 1.10), (30, "Valued", 1.15), (99, "Valued", 1.15) })
                    {
                        cs.standing[(int)Faction.Guard] = v;
                        career.FromSave(cs, ship);
                        edges &= career.TierName(Faction.Guard) == name && Math.Abs(career.PayFactor(Faction.Guard) - pay) < 1e-9;
                        tiers.Append($"{v}:{career.TierName(Faction.Guard)} ×{career.PayFactor(Faction.Guard):F2}  ");
                    }
                    Check(edges && career.Standing(Faction.Guard) == Career.StandingMax, $"tiers {tiers}(99 kept as {career.Standing(Faction.Guard)})");
                    cs.standing[(int)Faction.Guard] = 7; cs.standing[(int)Faction.Tharsis] = -3; cs.standing[(int)Faction.Compact] = 12;
                    career.FromSave(cs, ship);
                    var sj = JsonUtility.ToJson(career.ToSave(ship));
                    career.FromSave(JsonUtility.FromJson<CareerSave>(sj), ship);
                    bool kept = career.Standing(Faction.Guard) == 7 && career.Standing(Faction.Tharsis) == -3 && career.Standing(Faction.Compact) == 12;
                    var old = System.Text.RegularExpressions.Regex.Replace(sj, "\"standing\":\\[[^\\]]*\\],", "");
                    career.FromSave(JsonUtility.FromJson<CareerSave>(old), ship);
                    bool neutral = career.Standing(Faction.Guard) == 0 && career.Standing(Faction.Tharsis) == 0 && career.Standing(Faction.Compact) == 0 && career.PayFactor(Faction.Tharsis) == 1;
                    Check(kept && neutral && sj.Contains("\"standing\"") && !old.Contains("\"standing\""), $"a save keeps standing ({kept}); a save from before factions loads neutral ({neutral})");
                }
                career.FromSave(JsonUtility.FromJson<CareerSave>(keep), ship);
                career.Notes.Clear();
                yield return null;
            }

        }
    }
}
