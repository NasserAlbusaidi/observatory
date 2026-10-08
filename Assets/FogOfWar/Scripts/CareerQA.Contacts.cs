using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // 11b. Contacts (Signature.cs, Traffic.Sense, docs/design-combat.md Phase 2, PR 2b): a traffic ship shows only while our
    // sensors see its burn. Headless on a system of its own: the mass table and Fleet.json, each hull's peak range, the fade
    // mid-trip, the long-range doubling, the seen counts at three dates, and the cost of Sense. Then in the scene: the count
    // against an independent one, the dots drawn, RevealAll, a hidden ship kept as a target, the Contacts row, the own-burn
    // ring and the yard rows. Every check logs what it measured; the design counts (5 to 9 stock, 7 to 13 long-range of
    // about 110) are logged, not gated.
    public partial class CareerQA
    {
        [Serializable] class ContactFleetShip { public float lengthM; }
        [Serializable] class ContactFleetShips { public ContactFleetShip Courier, Packet, Liner, Freighter, Tanker, Hauler; }
        [Serializable] class ContactFleetFile { public ContactFleetShips ships; }

        // wet tonnes and peak range at the stock sensors, docs/design-combat.md Phase 2b table
        static readonly (string hull, double massT, double peakAu)[] ContactTable =
        {
            ("packet", 121, 0.058), ("courier", 338, 0.096), ("hauler", 6653, 0.427),
            ("liner", 10238, 0.530), ("tanker", 15389, 0.649), ("freighter", 18914, 0.720),
        };

        IEnumerator ContactsChecks()
        {
            log.WriteLine("  -- 11b. contacts");
            SolarSystem qs = null;
            if (Guard("contacts system", () => qs = SolarSystem.Build()))
            {
                Guard("contacts mass", ContactMassChecks);
                Guard("contacts peak range", () => ContactPeakChecks(qs));
                Guard("contacts fade", () => ContactFadeChecks(qs));
                Guard("contacts sensors", () => ContactSensorChecks(qs));
                Guard("contacts counts", () => ContactCountChecks(qs));
                Guard("contacts cost", () => ContactCostChecks(qs));
            }
            yield return ContactSceneChecks();
        }

        // sorts the list: the caller reads the ends after the median
        static double ContactMedian(List<double> v)
        {
            v.Sort();
            int n = v.Count;
            return n == 0 ? double.NaN : n % 2 == 1 ? v[n / 2] : (v[n / 2 - 1] + v[n / 2]) / 2;
        }

        // 1: Signature.HullMassT against the table, Traffic.HullLengthM against Fleet.json (matched by name)
        void ContactMassChecks()
        {
            var rows = new List<string>();
            bool ok = Traffic.HullLengthM.Length == Traffic.Hulls.Length;
            foreach (var (hull, want, _) in ContactTable)
            {
                double m = Signature.HullMassT(Traffic.LengthM(hull)), err = m / want - 1;
                ok &= Math.Abs(err) <= 0.01;
                rows.Add($"{hull} {Traffic.LengthM(hull):0.00} m {m:N0} t ({want:N0}; {err * 100:+0.00;-0.00} %)");
            }
            Check(ok, $"contacts: wet mass 500 t x (L/52 m)^3 matches the table within 1 %: {string.Join(", ", rows)}");

            var asset = Resources.Load<TextAsset>("Models/Fleet");
            var fleet = asset != null ? JsonUtility.FromJson<ContactFleetFile>(asset.text) : null;
            if (fleet?.ships == null) { Check(false, $"contacts: Models/Fleet.json loads ({(asset == null ? "missing" : "no ships")})"); return; }
            var json = new Dictionary<string, double>
            {
                ["courier"] = fleet.ships.Courier?.lengthM ?? 0, ["packet"] = fleet.ships.Packet?.lengthM ?? 0, ["liner"] = fleet.ships.Liner?.lengthM ?? 0,
                ["freighter"] = fleet.ships.Freighter?.lengthM ?? 0, ["tanker"] = fleet.ships.Tanker?.lengthM ?? 0, ["hauler"] = fleet.ships.Hauler?.lengthM ?? 0,
            };
            bool same = json.Count == Traffic.Hulls.Length;
            var lens = new List<string>();
            foreach (var hull in Traffic.Hulls)
            {
                json.TryGetValue(hull, out double j);
                double c = Traffic.LengthM(hull);
                same &= j > 0 && Math.Abs(c / j - 1) <= 0.01;
                lens.Add($"{hull} code {c:0.00} json {j:0.00}");
            }
            Check(same, $"contacts: Traffic.HullLengthM matches Fleet.json lengthM by name within 1 %: {string.Join(", ", lens)}");
        }

        // 2: the first 200 ready trips of the schedule from the fixture epoch
        void ContactPeakChecks(SolarSystem qs)
        {
            var tr = new Traffic(qs, 1);
            var byHull = new Dictionary<string, List<double>>();
            foreach (var h in Traffic.Hulls) byHull[h] = new List<double>();
            int n = 0;
            long day0 = (long)Math.Floor(Epoch);
            for (long d = day0; n < 200 && d < day0 + 400; d++)
                foreach (var t in tr.Day(d))
                {
                    if (n >= 200) break;
                    if (!tr.Ready(t)) continue;
                    n++;
                    byHull[t.Hull].Add(Signature.RangeKm(t.JetW(t.Dep + 1e-6)) / Frame.AU);
                    byHull[t.Hull].Add(Signature.RangeKm(t.JetW(t.Arr - 1e-6)) / Frame.AU);
                }
            bool ok = n == 200;
            var rows = new List<string>();
            foreach (var (hull, _, want) in ContactTable)
            {
                var v = byHull[hull];
                double med = ContactMedian(v);
                double lo = v.Count > 0 ? v[0] : double.NaN, hi = v.Count > 0 ? v[v.Count - 1] : double.NaN;
                ok &= v.Count > 0 && Math.Abs(med / want - 1) <= 0.10;
                rows.Add($"{hull} {med:0.000} AU (table {want:0.000}; {(med / want - 1) * 100:+0.0;-0.0} %; {v.Count} ends, {lo:0.000} to {hi:0.000})");
            }
            Check(ok, $"contacts: over the first {n} ready trips from the epoch the median end range of each hull is within 10 % of the table: {string.Join("; ", rows)}");
        }

        // 3: a Mars > Vesta trip fades at mid-trip and is not seen from 1 AU
        void ContactFadeChecks(SolarSystem qs)
        {
            var sched = new Traffic(qs, 1);
            Traffic.Trip trip = null;
            long day0 = (long)Math.Floor(Epoch);
            for (long d = day0; d < day0 + 5 * 366 && trip == null; d++)
                foreach (var t in sched.Day(d))
                    if (t.From.Body == "Mars" && t.To.Body == "Vesta" && sched.Ready(t)) { trip = t; break; }
            if (trip == null) { Check(false, "contacts: a Mars > Vesta trip exists in five years of the schedule"); return; }
            double mid = (trip.Dep + trip.Arr) / 2;
            double p0 = trip.JetW(trip.Dep + 1e-6), p1 = trip.JetW(trip.Arr - 1e-6), pm = trip.JetW(mid), peak = Math.Max(p0, p1);

            var tr = new Traffic(qs, 1);
            tr.Update(mid);
            Traffic.Trip live = null;
            foreach (var t in tr.Flying) if (t.Name == trip.Name) live = t;
            if (live == null) { Check(false, $"contacts: {trip.Name} is in flight at its mid-trip date"); return; }
            var pos = qs.Pos(live.Body, mid);
            double rMid = Signature.RangeKm(live.JetW(mid), Signature.SensorKm(1)) / Frame.AU;
            var axes = new[] { new D3(1, 0, 0), new D3(-1, 0, 0), new D3(0, 1, 0), new D3(0, -1, 0), new D3(0, 0, 1), new D3(0, 0, -1) };
            int unseen = 0;
            foreach (var u in axes)
            {
                tr.Sense(pos + u * Frame.AU, mid, Signature.SensorKm(1));
                if (!live.Seen) unseen++;
            }
            tr.Sense(pos, mid, Signature.SensorKm(0));
            bool atZero = live.Seen;
            Check(pm < 0.05 * peak && unseen == axes.Length && atZero,
                $"contacts: {trip.Name} ({trip.Hull}) Mars > Vesta, {trip.Arr - trip.Dep:0.0} days: jet {p0 / 1e12:0.000} TW leaving, {p1 / 1e12:0.000} TW arriving, {pm / 1e12:0.0000} TW at mid-trip " +
                $"({pm / peak * 100:0.00} % of the {peak / 1e12:0.000} TW peak; under 5 %); unseen from 1 AU on {unseen} of {axes.Length} axes with long-range sensors (reach {rMid:0.0000} AU); seen from 0 km: {atZero}");
        }

        // 4: long-range sensors see everything stock sensors do, and twice as far
        void ContactSensorChecks(SolarSystem qs)
        {
            double j = Epoch + 50;
            var tr = new Traffic(qs, 1);
            tr.Update(j);
            var from = qs.Pos(qs.Earth, j);
            double sk0 = Signature.SensorKm(0), sk1 = Signature.SensorKm(1);
            int stock = tr.Sense(from, j, sk0);
            var was = new bool[tr.Flying.Count];
            for (int i = 0; i < was.Length; i++) was[i] = tr.Flying[i].Seen;
            int lng = tr.Sense(from, j, sk1), lost = 0;
            for (int i = 0; i < was.Length; i++) if (was[i] && !tr.Flying[i].Seen) lost++;
            Check(tr.Flying.Count > 0 && lost == 0 && stock <= lng && tr.SeenCount == lng && sk1 == 2 * sk0,
                $"contacts: from Earth at epoch+50, stock sensors see {stock} of {tr.Flying.Count}, long-range {lng}; {lost} ships seen with stock are lost with long-range (0); SeenCount {tr.SeenCount}; sensor km {sk0:N0} / {sk1:N0}");

            Traffic.Trip best = null;
            double bestW = -1;
            foreach (var t in tr.Flying) { double w = t.JetW(j); if (w > bestW) { bestW = w; best = t; } }
            if (best == null) { Check(false, "contacts: a ship is in flight to test the doubling"); return; }
            double r = Signature.RangeKm(bestW, sk0);
            var at = qs.Pos(best.Body, j);
            var dir = new D3(0.6, 0.64, 0.48).Norm;
            bool Seen(double k, double sensorKm) { tr.Sense(at + dir * (k * r), j, sensorKm); return best.Seen; }
            bool s99 = Seen(0.99, sk0), s101 = Seen(1.01, sk0), l101 = Seen(1.01, sk1), l199 = Seen(1.99, sk1), l201 = Seen(2.01, sk1);
            Check(s99 && !s101 && l101 && l199 && !l201,
                $"contacts: {best.Name} ({best.Hull}, {bestW / 1e12:0.0} TW, stock reach {r / Frame.AU:0.0000} AU): stock sees it at 0.99 R {s99}, at 1.01 R {s101}; long-range at 1.01 R {l101}, 1.99 R {l199}, 2.01 R {l201} (true, false, true, true, false)");
        }

        // 5: the sky from Earth at three fixture dates
        void ContactCountChecks(SolarSystem qs)
        {
            var tr = new Traffic(qs, 1);
            var rows = new List<string>();
            bool ok = true;
            foreach (double off in new[] { 0.0, 100, 200 })
            {
                double j = Epoch + off;
                tr.Update(j);
                var from = qs.Pos(qs.Earth, j);
                int n = tr.Flying.Count, s0 = tr.Sense(from, j, Signature.SensorKm(0)), l = tr.Sense(from, j, Signature.SensorKm(1));
                ok &= n > 0 && s0 <= l && l <= n && tr.SeenCount == l;
                rows.Add($"epoch+{off:0}: seen {s0} of {n} (stock), {l} of {n} (long-range)");
            }
            Check(ok, $"contacts: from Earth, {string.Join("; ", rows)} (design estimate 5 to 9 stock, 7 to 13 long-range of about 110; checked: stock <= long-range <= in flight)");
        }

        // 6: Sense on the live set, 1000 calls. A fresh date each call clears the position cache (the first Sense of a frame);
        // the same date reuses it (DrawTraffic's case).
        void ContactCostChecks(SolarSystem qs)
        {
            var tr = new Traffic(qs, 1);
            tr.Update(Epoch);
            var from = qs.Pos(qs.Earth, Epoch);
            double sk = Signature.SensorKm(0);
            for (int i = 0; i < 20; i++) tr.Sense(from, Epoch, sk);
            const int Calls = 1000;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < Calls; i++) tr.Sense(from, Epoch, sk);
            double same = sw.Elapsed.TotalMilliseconds / Calls;
            sw.Restart();
            for (int i = 0; i < Calls; i++) tr.Sense(from, Epoch + i * 1e-5, sk);
            double fresh = sw.Elapsed.TotalMilliseconds / Calls;
            Perf(fresh < 0.1, $"contacts: Sense on {tr.Flying.Count} ships takes {fresh * 1000:0.0} us a call at a fresh date (under 100 us) and {same * 1000:0.0} us at a cached date, over {Calls} calls");
        }

        // 7: the scene, left by TrafficChecks with live traffic. Time stands still (QaStopJd), so the count, the dots and the rows
        // all read one moment. Everything it touches is put back at the end.
        IEnumerator ContactSceneChecks()
        {
            bool revealWas = Signature.RevealAll, labelsWas = GameSettings.ShowLabels;
            int warpWas = Get<int>("warpIdx");
            var targetWas = ship.Target;
            var mapProp = typeof(OpenSystemScene).GetProperty("MapView", F);
            bool mapWas = mapProp != null && (bool)mapProp.GetValue(s);
            Traffic tr = null;
            bool gui = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            bool go = Guard("contacts scene setup", () =>
            {
                Signature.RevealAll = false;
                GameSettings.ShowLabels = true;
                Put("warpIdx", 0);
                s.QaStopJd = jd;
                Call("TickTraffic");
                tr = s.TrafficNow;
            });
            if (!gui) log.WriteLine("  SKIP contacts: no graphics device (-nographics): the drawn-dot counts");
            for (int i = 0; go && i < 3; i++) yield return null;

            // the scene's count against one worked out here from the rule, at both sensor levels
            int sceneSeen = 0, hidden = 0;
            if (go) go = Guard("contacts scene count", () =>
            {
                if (tr == null || tr.Flying.Count == 0) { Check(false, $"contacts: the scene has traffic in flight ({tr?.Flying.Count ?? -1} ships)"); return; }
                int level = career.Codex.SensorLevel;
                int Mine(double sensorKm, out int wrong)
                {
                    int n = 0; wrong = 0;
                    foreach (var t in tr.Flying)
                    {
                        double w = Signature.JetW(Signature.HullMassT(Traffic.LengthM(t.Hull)), t.Rail.Accel(jd).Mag);
                        double r = Signature.RangeKm(w, sensorKm), d2 = (sys.Pos(t.Body, jd) - ship.Pos).Sqr;
                        bool seen = d2 <= r * r;
                        if (seen) n++;
                        if (seen != t.Seen) wrong++;
                    }
                    return n;
                }
                Call("TickTraffic");
                int mine = Mine(Signature.SensorKm(level), out int wrong), scene = tr.SeenCount;
                Check(scene == mine && wrong == 0 && scene <= tr.Flying.Count,
                    $"contacts: the scene (sensor level {level}) senses {scene} of {tr.Flying.Count} ships; an independent count of the rule from the ship's position gives {mine}; {wrong} ships differ in their Seen flag");
                sceneSeen = scene; hidden = tr.Flying.Count - scene;

                career.Codex.SensorLevel = 1;
                Call("TickTraffic");
                int mineL = Mine(Signature.SensorKm(1), out int wrongL), sceneL = tr.SeenCount;
                career.Codex.SensorLevel = level;
                Call("TickTraffic");
                Check(sceneL == mineL && wrongL == 0 && sceneL >= scene && tr.SeenCount == scene,
                    $"contacts: with long-range sensors fitted the scene senses {sceneL} (independent {mineL}, {wrongL} differ), stock {scene}; the level is put back to {career.Codex.SensorLevel}, sensed {tr.SeenCount}");
            });

            // the dots drawn: a Repaint passes after a few frames
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go && gui) go = Guard("contacts dots", () =>
            {
                int seen = tr.SeenCount;
                Check(s.TrafficConsidered == seen && s.TrafficDrawnUnseen == 0 && s.TrafficDrawn <= seen,
                    $"contacts: one Repaint considers {s.TrafficConsidered} ships (sensed {seen}), draws {s.TrafficDrawn} dots (at most {seen}), {s.TrafficDrawnUnseen} of them for an unseen ship (0)");
            });
            if (go && gui) Signature.RevealAll = true;
            for (int i = 0; go && gui && i < 3; i++) yield return null;
            if (go && gui) go = Guard("contacts reveal", () =>
            {
                Check(s.TrafficConsidered == tr.Flying.Count && s.TrafficDrawn <= tr.Flying.Count && s.TrafficDrawnUnseen <= tr.Flying.Count - tr.SeenCount,
                    $"contacts: with RevealAll one Repaint considers {s.TrafficConsidered} of {tr.Flying.Count} ships, draws {s.TrafficDrawn} dots, {s.TrafficDrawnUnseen} of them unseen (at most {tr.Flying.Count - tr.SeenCount})");
            });
            Signature.RevealAll = revealWas;

            // a hidden ship stays a body, and a target on it is kept
            Traffic.Trip dark = null;
            if (go) go = Guard("contacts hidden target", () =>
            {
                foreach (var t in tr.Flying) if (!t.Seen) { dark = t; break; }
                Check(dark != null && sys.Bodies.Contains(dark.Body),
                    $"contacts: {hidden} of {tr.Flying.Count} ships are hidden at sensed {sceneSeen}; the first, {dark?.Name ?? "-"}, is still in the system's bodies: {dark != null && sys.Bodies.Contains(dark.Body)}");
                if (dark != null) ship.Target = dark.Body;
            });
            for (int i = 0; go && dark != null && i < 2; i++) yield return null;
            if (go) go = Guard("contacts hidden kept", () =>
            {
                if (dark == null) return;
                Check(ship.Target == dark.Body && !dark.Seen && sys.Bodies.Contains(dark.Body),
                    $"contacts: after 2 frames the target is still {dark.Name} ({(ship.Target == dark.Body ? "kept" : ship.Target == null ? "released" : "changed to " + ship.Target.Name)}), unseen {!dark.Seen}");
                ship.Target = targetWas;
            });

            // the Contacts row
            if (go) go = Guard("contacts row", () =>
            {
                var rows = SigRows();
                string want = $"sensed: {tr.SeenCount} of {tr.Flying.Count} in flight";
                Check(SigVal(rows, "Contacts") == want, $"contacts: the Readouts row Contacts reads \"{SigVal(rows, "Contacts")}\" (want \"{want}\")");
            });

            // the own-burn ring: the call itself, then the map
            if (go) go = Guard("contacts ring call", () =>
            {
                var pts = Get<List<Vector3>>("pts");
                var cols = Get<List<Color>>("cols");
                double thr = ship.Throttle;
                ship.Throttle = 1;
                Call("AddSeenRing", 1f);
                int burning = s.SeenRings;
                Call("AddSeenRing", 0f);
                int faded = s.SeenRings;
                ship.Throttle = 0;
                Call("AddSeenRing", 1f);
                int cold = s.SeenRings;
                ship.Throttle = thr;
                pts.Clear(); cols.Clear();
                Check(burning == 1 && cold == 0 && faded == 0,
                    $"contacts: AddSeenRing makes {burning} ring while burning (1), {cold} while cold (0), {faded} with the view too close to show it (0)");
            });
            if (go) go = Guard("contacts map open", () => { if (!mapWas) Call("ToggleMap"); });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) go = Guard("contacts map cold", () =>
            {
                Check(s.SeenRings == 0, $"contacts: on the map with the drive off the own-burn ring count is {s.SeenRings} (0); jet {Signature.JetW(ship) / 1e12:0.00} TW");
                ship.Throttle = 1; // time stands still, so the tick does not ramp it back
            });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) go = Guard("contacts map burn", () =>
            {
                double p = Signature.JetW(ship);
                string km = (string)typeof(OpenSystemScene).GetMethod("Km", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { Signature.RangeKm(p) });
                if (p <= 0) log.WriteLine("  SKIP contacts: the tick reset the throttle, so the map ring was checked by the call alone");
                else Check(s.SeenRings == 1, $"contacts: on the map with the drive at full throttle ({p / 1e12:0.00} TW) the own-burn ring count is {s.SeenRings} (1), a ring at {km}");
                ship.Throttle = 0;
            });
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) go = Guard("contacts map off", () =>
            {
                Check(s.SeenRings == 0, $"contacts: the drive off again, the own-burn ring count is {s.SeenRings} (0)");
            });

            // the yard rows
            if (go) go = Guard("contacts yard", () =>
            {
                var stock = ShipBuild.Stock();
                var longRange = ShipBuild.Stock();
                longRange.pick[(int)Slot.Sensors] = 1;
                var yard = typeof(OpenSystemScene).GetMethod("YardReadouts", BindingFlags.Static | BindingFlags.NonPublic);
                var rows = (List<(string key, string now, string after)>)yard.Invoke(null, new object[] { stock, longRange });
                var burn = rows.Find(r => r.key == "Sees a stock burn at");
                var cold = rows.Find(r => r.key == "Sees a cold hull at");
                Check(burn.key != null && burn.now == "0.234 AU" && burn.after == "0.468 AU",
                    $"contacts: the yard row \"{burn.key ?? "-"}\" reads stock \"{burn.now}\" (0.234 AU), long-range \"{burn.after}\" (0.468 AU); sensor levels {stock.SensorLevel} / {longRange.SensorLevel}");
                Check(cold.key != null && cold.now == "10,000 km" && cold.after == "20,000 km",
                    $"contacts: the yard row \"{cold.key ?? "-"}\" reads stock \"{cold.now}\" (10,000 km), long-range \"{cold.after}\" (20,000 km)");
                Check(Math.Abs(Signature.StockBurnW / 1e12 - 24.52) <= 0.01, $"contacts: Signature.StockBurnW is {Signature.StockBurnW / 1e12:0.000} TW (24.52 +- 0.01)");
            });

            Guard("contacts cleanup", () =>
            {
                Signature.RevealAll = revealWas;
                GameSettings.ShowLabels = labelsWas;
                ship.Throttle = 0; ship.ThrottleCmd = 0;
                ship.Target = targetWas;
                if (!mapWas && mapProp != null && (bool)mapProp.GetValue(s)) Call("ToggleMap");
                s.QaStopJd = double.PositiveInfinity;
                Put("warpIdx", warpWas);
                Call("TickTraffic");
            });
        }
    }
}
