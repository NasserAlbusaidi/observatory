using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // 18a-18i: the missiles on screen and the 1× hold before an impact (docs/design-combat.md Phase 4, PR 4c, D11;
    // OpenSystemScene.Missiles.cs, Career.WarpCap). The ship 1,000,000 km out from Vesta at rest with it, drive off; a chase
    // started by hand astern with an empty magazine, and a salvo of 2 put in flight by hand, 14° off the tail (the Aft
    // aspect: no hold or cabin hit). With no PDCs: 18a/18b the hold at 1,000× asked (the raider past CloseKm) and at 100×
    // asked (inside it): every frame before it at the warp asked, none stepping past its start, 1× from 20 s out to the
    // impact, the impact the closed form's, the warp given back after; 18c a flare per missile at ship.Pos + P in the scene
    // frame, nose on the ship, and the dot and tag while it is drawn larger than it is; 18d the hit flash on the side it came
    // in on, fired by MissileHit. With 6 mounts: 18g tracers only inside 5 km from a working mount that bears, none after the
    // impact, none with every mount dead; 18h each from its PDC_n marker to the missile; 18i a kill flash at the missile on
    // MissileKilled. 18e nothing left once the chase ends; 18f the damage the hits did and the build are undone. Shots of
    // the salvo at 30 km and 1 km, the tracers, the kill and the hit, in both shells. The scene is put back at the end, after
    // a failed or thrown check too.
    public partial class CareerQA
    {
        IEnumerator MissileFxChecks()
        {
            string shell = UI.UiShell.Disabled ? "legacy" : "toolkit";
            bool labelsWas = GameSettings.ShowLabels, saved = false;
            D3 pos0 = default, vel0 = default;
            Quaternion att0 = Quaternion.identity;
            Body target0 = null;
            object camMode0 = null, pivot0 = null;
            double camDist0 = 0, fuel0 = 0, maxG0 = 0;
            float yaw0 = 0, pitch0 = 0;
            int warp0 = 0, failed0 = 0;
            ShipDamage dmg0 = null;
            ShipBuild build0 = null;
            Contract[] active0 = null;
            Contract job = null;
            Encounter e = null;
            var made = new List<UnityEngine.Object>();
            D3 approach = default; // unit, from the ship toward the salvo

            object Nested(string type, int v) => Enum.ToObject(s.GetType().GetNestedType(type, BindingFlags.NonPublic), v);
            int Holders(string name)
            {
                int n = 0;
                foreach (Transform c in Get<Transform>("shipT").parent) if (c.name == name) n++;
                return n;
            }
            // notes go after 8 s: count those since a run began
            int HitNotes(float since) => career.Notes.FindAll(n => n.Time >= since && (n.What == Career.Happening.MissileHit || (n.What == Career.Happening.JobFailed && n.Text.StartsWith("Hit:")))).Count;

            bool go = Guard("missile fx setup", () =>
            {
                pos0 = ship.Pos; vel0 = ship.Vel; att0 = ship.Att; target0 = ship.Target;
                camMode0 = Get<object>("camMode"); pivot0 = Get<object>("pivot"); camDist0 = Get<double>("camDist");
                yaw0 = Get<float>("yaw"); pitch0 = Get<float>("pitch"); warp0 = Get<int>("warpIdx");
                dmg0 = career.Damage.Clone(); build0 = career.Build.Clone(); fuel0 = ship.Fuel; maxG0 = ship.MaxG; failed0 = career.Failed;
                active0 = career.Active.ToArray();
                saved = true;

                if (!labelsWas) GameSettings.ShowLabels = true; // the dots and tags are drawn only with labels on
                career.DropContact();
                Put("warpIdx", 0);
                var vesta = sys["Vesta"];
                D3 pV = sys.Pos(vesta, jd);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0; ship.Landed = null; ship.Hold = Hold.Off; ship.AngVel = Vector3.zero;
                ship.Pos = pV + pV.Norm * 1e6; ship.Vel = sys.Vel(vesta, jd);
                Call("SetTarget", vesta);
                // the chase's job alone aboard: a hit with nothing else to take has no other job to fail or rob
                job = new Contract { Id = 900402, Kind = JobKind.Cargo, Title = "QA ice", From = "Ceres", To = "Vesta", Tonnes = 20, Pay = 20000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, Key = "qa/missile-fx" };
                career.Active.Clear();
                career.Active.Add(job);
                // 18a-18d: no PDCs, so every missile hits
                career.Build.pick[(int)Slot.PointDefense] = 0;
                career.Damage = new ShipDamage();
                career.Apply(ship);
                var side = D3.Cross(ship.Heading, Frame.EclNorth).Norm;
                approach = (-ship.Heading + side * 0.25).Norm;
            });

            // a chase d km astern with no salvo of its own, and a salvo of 2 put in flight `km` out; the jd it was put there
            double Salvo(double raiderKm, double km)
            {
                e = career.StartContact(job, sys, ship, jd, ship.Pos - ship.Heading * raiderKm, ship.Vel);
                e.Magazine = 0;
                e.Missiles.Add(new Missile { P = approach * km });
                e.Missiles.Add(new Missile { P = approach * km });
                return jd;
            }

            // one run of the hold: warp `want` asked, every frame logged until the impact and one frame after it
            IEnumerator RunHold(string label, int want, double raiderKm, double km, bool shots)
            {
                double jd0 = 0, cap = 0;
                int flash0 = 0;
                float since = 0;
                bool ok = Guard($"missile fx {label} start", () =>
                {
                    jd0 = Salvo(raiderKm, km);
                    cap = career.WarpCap;
                    flash0 = HitF.Count; since = Time.realtimeSinceStartup;
                    Put("warpIdx", want);
                });
                if (!ok) yield break;
                var rows = new List<(double t0, double fw, double t1, int warp)>();
                double tPrev = career.ImpactInS, jdHit = double.NaN;
                bool at30 = !shots, atClose = !shots;
                for (int f = 0; f < 4000 && career.Encounter == e && e.Missiles.Count > 0; f++)
                {
                    yield return null;
                    double t1 = career.ImpactInS;
                    rows.Add((tPrev, Get<double>("frameWarp"), t1, Get<int>("warpIdx")));
                    tPrev = t1;
                    if (double.IsInfinity(t1)) { jdHit = jd; break; }
                    if (!at30 && t1 <= 15) { at30 = true; yield return Placed("30 km", true); }
                    else if (!atClose && t1 <= 0.5) { atClose = true; yield return Placed("1 km", false); }
                }
                if (shots && double.IsNaN(jdHit) == false)
                {
                    yield return new WaitForEndOfFrame();
                    Guard($"missile fx {label} 18d flash", () => { FlashCheck(flash0, since); Look(true); });
                    yield return Shot($"c18-hit-{shell}");
                }
                yield return null; // the frame after the impact
                Guard($"missile fx {label} 18{(want == 3 ? "a" : "b")}", () =>
                {
                    var pre = rows.FindAll(r => r.t0 > Career.ImpactHoldS + Career.ImpactSlackS);
                    int first = rows.FindIndex(r => r.t0 <= Career.ImpactHoldS + Career.ImpactSlackS);
                    var hold = first >= 0 ? rows.GetRange(first, rows.Count - first) : new List<(double t0, double fw, double t1, int warp)>();
                    double preMax = 0, preMin = double.PositiveInfinity, holdMax = 0, holdS = 0;
                    bool overstep = false;
                    foreach (var r in pre) { preMax = Math.Max(preMax, r.fw); preMin = Math.Min(preMin, r.t1); overstep |= r.fw > cap * (1 + 1e-9); }
                    foreach (var r in hold) { holdMax = Math.Max(holdMax, r.fw); holdS += r.fw / 60; }
                    bool hit = !double.IsNaN(jdHit);
                    double flight = (jdHit - jd0) * 86400, want0 = km / Career.MissileKms;
                    double fwAfter = Get<double>("frameWarp");
                    int wAfter = Get<int>("warpIdx");
                    Check(hit && pre.Count > 0 && !overstep && preMax >= 0.9 * Math.Min(cap, WarpOf(want)) && preMin >= Career.ImpactHoldS - 1e-3,
                        $"18{(want == 3 ? "a" : "b")} impact hold {label}: {WarpOf(want):N0}× asked, the chase's cap {cap:N0}× (raider {raiderKm:N0} km off); {pre.Count} frames before the hold ran at up to {preMax:N0}× (≥ 90 % of {Math.Min(cap, WarpOf(want)):N0}×, none over the cap: {!overstep}), " +
                        $"none ending inside it: the nearest end {preMin:0.0000} s before the impact (≥ {Career.ImpactHoldS:0} − 0.001)");
                    Check(hit && first >= 0 && hold[0].t0 >= Career.ImpactHoldS - 1e-3 && holdMax <= 1 + 1e-6 && Math.Abs(holdS - hold[0].t0) <= 1.0 / 60 + 1e-3,
                        $"18{(want == 3 ? "a" : "b")} impact hold {label}: the 1× hold began {(first >= 0 ? hold[0].t0 : double.NaN):0.0000} s before the impact (20 to 20.01), {hold.Count} frames to it ran at ≤ {holdMax:0.000000}× (≤ 1), " +
                        $"{holdS:0.000} s of sim time (the hold's start ± one 1/60 s update)");
                    Check(hit && Math.Abs(flight - want0) <= 1.0 / 60 + 1e-3,
                        $"18{(want == 3 ? "a" : "b")} impact hold {label}: the salvo hit {flight:0.0000} s after it was put {km:N0} km out (closed form {want0:0.0000} s ± one 1/60 s update), the warp and the hold notwithstanding");
                    double capAfter = career.WarpCap;
                    Check(hit && career.Encounter == e && capAfter == cap && wAfter == want && fwAfter >= 0.9 * Math.Min(cap, WarpOf(want)) && fwAfter <= cap * (1 + 1e-9),
                        $"18{(want == 3 ? "a" : "b")} impact hold {label}: after the impact the cap is {capAfter:N0}× again ({cap:N0}), the warp {WarpOf(wAfter):N0}× is given back ({WarpOf(want):N0}× asked once, before the salvo), and the next frame ran at {fwAfter:N0}×");
                });
                Put("warpIdx", 0);
            }

            // 18c: the flares where the missiles are, at one frame held still; a shot of it
            // the orbit camera on the ship: far, ahead of it a little up and to the side, looking back past the hull along the
            // salvo's line; else 2 km off that line, so a missile 1 km out shows its plume side on
            void Look(bool far)
            {
                var side = D3.Cross(ship.Heading, Frame.EclNorth).Norm;
                var across = D3.Cross(approach, Frame.EclNorth).Norm;
                var dir = Frame.U((far ? ship.Heading - side * 0.35 + Frame.EclNorth * 0.2 : across + approach * 0.25 + Frame.EclNorth * 0.15).Norm).normalized;
                Put("camMode", Nested("CamMode", 1));
                Call("SetPivot", Nested("Pivot", 0), far ? 0.16 : 2.0);
                Put("yaw", Mathf.Atan2(dir.x, -dir.z) * Mathf.Rad2Deg);
                Put("pitch", Mathf.Asin(Mathf.Clamp(dir.y, -1, 1)) * Mathf.Rad2Deg);
            }

            IEnumerator Placed(string at, bool far)
            {
                s.QaFreeze = true;
                Look(far);
                yield return null;
                yield return new WaitForEndOfFrame();
                Guard($"missile fx 18c {at}", () =>
                {
                    var ts = Get<List<Transform>>("missileT");
                    var shipT = Get<Transform>("shipT");
                    double offM = 0; float ang = 0, scale = float.PositiveInfinity;
                    for (int i = 0; i < Math.Min(ts.Count, e.Missiles.Count); i++)
                    {
                        var P = e.Missiles[i].P;
                        offM = Math.Max(offM, ((ts[i].localPosition - shipT.localPosition) - Frame.U(P)).magnitude * 1000);
                        ang = Mathf.Max(ang, Vector3.Angle(ts[i].localRotation * Vector3.forward, Frame.U(-P.Norm)));
                        scale = Mathf.Min(scale, ts[i].localScale.x);
                        foreach (var r in ts[i].GetComponentsInChildren<Renderer>(true)) { if (!made.Contains(r.sharedMaterial)) made.Add(r.sharedMaterial); var mf = r.GetComponent<MeshFilter>(); if (mf != null && !made.Contains(mf.sharedMesh)) made.Add(mf.sharedMesh); }
                    }
                    foreach (var o in Get<List<UnityEngine.Object>>("missileMade")) if (!made.Contains(o)) made.Add(o);
                    var px = Get<List<float>>("missilePx");
                    int dots = Get<int>("missileDots");
                    string shown = Get<string>("missileShown");
                    bool small = px.Count > 0 && px[0] < 14;
                    Check(ts.Count == e.Missiles.Count && ts.Count == 2 && Holders("Missile") == 2 && offM <= 1 && ang <= 0.01f && scale >= 1 - 1e-6f && Get<Encounter>("missilesFor") == e,
                        $"18c missile flares at {e.Missiles[0].P.Mag:0.000} km: {ts.Count} holders for {e.Missiles.Count} missiles ({Holders("Missile")} under the root), worst {offM:0.000} m from ship.Pos + P in the scene frame (≤ 1), " +
                        $"nose {ang:0.0000}° off the closing direction (≤ 0.01); drawn {scale:0.00}× its size (≥ 1; the plume is {(px.Count > 0 ? px[0] : 0):0.0} px true, 14 least)");
                    Check(small ? dots == 2 && shown.Contains("Missile · ") : dots == 0,
                        $"18c missile marker at {e.Missiles[0].P.Mag:0.000} km: the plume is {(px.Count > 0 ? px[0] : 0):0.0} px on screen, so {(small ? "a dot each and one tag" : "no dot")}: {dots} dots; {shown}");
                });
                yield return Shot($"c18-salvo-{at.Replace(" ", "")}-{shell}");
                s.QaFreeze = false;
            }

            // 18d: a flash for each MissileHit, at the hull on the side the salvo came in on
            void FlashCheck(int flash0, float since)
            {
                var t = HitF.T;
                var shipT = Get<Transform>("shipT");
                int flashes = HitF.Count - flash0, notes = HitNotes(since);
                float ang = t != null ? Vector3.Angle(t.localPosition - shipT.localPosition, Frame.U(approach)) : 180;
                if (t != null) { made.Add(HitF.Mat); made.Add(HitF.Mesh); }
                Check(t != null && t.gameObject.activeInHierarchy && flashes == 2 && notes == 2 && ang <= 1 && e.Missiles.Count == 0 && Holders("Missile") == 0,
                    $"18d hit flash: {notes} hit notes (2: the salvo), {flashes} flashes fired (2); the flash is {(t != null && t.gameObject.activeInHierarchy ? "on" : "off")}, {ang:0.000}° off the side the salvo came in on (≤ 1); " +
                    $"{e.Missiles.Count} missiles and {Holders("Missile")} flare holders left (0)");
            }

            // 18g-18i: the PDCs' tracers and the kill flash. A salvo 8 km out at 1×: with 6 working mounts (`dead` false) the
            // tracers run from each one that bears on a missile inside EnvelopeKm and stop at the impact, and a kill (combatRng
            // seeded) flashes at the missile; with every mount dead, none
            IEnumerator TracerRun(bool dead)
            {
                string label = dead ? "every mount dead" : "6 working mounts";
                int kill0 = 0, hit0 = 0, outside = 0, wrong = 0, inside = 0, after = -1, bearing = 0;
                float since = 0;
                bool ok = Guard($"missile fx tracers {label} start", () =>
                {
                    career.Build.pick[(int)Slot.PointDefense] = 3;
                    career.Damage.mounts = dead ? career.Build.FittedMounts : 0;
                    career.Apply(ship);
                    career.SeedCombat(TracerSeed);
                    Salvo(300, 8);
                    kill0 = KillF.Count; hit0 = HitF.Count; since = Time.realtimeSinceStartup;
                    Put("warpIdx", 0);
                });
                if (!ok) yield break;
                bool placed = dead;
                for (int f = 0; f < 600; f++)
                {
                    yield return new WaitForEndOfFrame();
                    var tr = Get<List<(int mount, Vector3 from, Vector3 to)>>("tracers");
                    if (career.Encounter != e || e.Missiles.Count == 0)
                    {
                        after = tr.Count + (Get<GameObject>("tracerObj")?.activeSelf == true ? 1000 : 0);
                        break;
                    }
                    double near = double.PositiveInfinity;
                    int want = 0;
                    foreach (var m in e.Missiles)
                    {
                        near = Math.Min(near, m.P.Mag);
                        if (m.P.Mag <= PointDefense.EnvelopeKm) want += career.MountsBearing(ship, m.P);
                    }
                    if (near > PointDefense.EnvelopeKm) { if (tr.Count != 0) outside++; continue; }
                    inside++;
                    bearing = Math.Max(bearing, want);
                    if (tr.Count != want) wrong++;
                    if (!placed && near <= 3)
                    {
                        placed = true;
                        yield return TracersPlaced();
                    }
                }
                if (!dead)
                {
                    yield return new WaitForEndOfFrame();
                    Guard("missile fx 18i kill flash", () =>
                    {
                        int kills = KillF.Count - kill0, hits = HitF.Count - hit0;
                        int killNotes = career.Notes.FindAll(n => n.Time >= since && n.What == Career.Happening.MissileKilled).Count;
                        var t = KillF.T;
                        var shipT = Get<Transform>("shipT");
                        float ang = t != null ? Vector3.Angle(t.localPosition - shipT.localPosition, Frame.U(approach)) : 180;
                        double off = t != null ? (t.localPosition - shipT.localPosition).magnitude : double.NaN;
                        if (t != null) { made.Add(KillF.Mat); made.Add(KillF.Mesh); }
                        if (HitF.T != null) { made.Add(HitF.Mat); made.Add(HitF.Mesh); }
                        Check(e.Killed > 0 && kills == e.Killed && killNotes == e.Killed && hits == 2 - e.Killed && t != null && t.gameObject.activeInHierarchy && ang <= 1 && off <= 0.04,
                            $"18i kill flash: combatRng seeded {TracerSeed}, {bearing / 2} mounts bearing: {e.Killed} of 2 killed (≥ 1), {killNotes} MissileKilled notes, {kills} kill flashes and {hits} hit flashes ({e.Killed} and {2 - e.Killed}); " +
                            $"the kill flash is {(t != null && t.gameObject.activeInHierarchy ? "on" : "off")} {off * 1000:0.0} m from the ship (≤ 40: one 1× frame's flight), {ang:0.000}° off the salvo's side (≤ 1)");
                    });
                    yield return Shot($"c18-kill-{shell}");
                }
                Guard($"missile fx 18g {label}", () =>
                {
                    if (dead)
                        Check(inside > 0 && bearing == 0 && wrong == 0 && outside == 0 && after == 0,
                            $"18g tracers, {label}: {inside} frames inside {PointDefense.EnvelopeKm:0} km with {bearing} mounts bearing (0) drew none ({wrong} frames with tracers, 0), and none at the impact ({after}, 0)");
                    else
                        Check(inside >= 140 && bearing > 0 && wrong == 0 && outside == 0 && after == 0,
                            $"18g tracers, {label}: {outside} frames past {PointDefense.EnvelopeKm:0} km drew tracers (0); {inside} frames inside it (≥ 140: 2.5 s at 1×) drew one a missile a bearing mount ({bearing} at most) in all but {wrong} (0); " +
                            $"the frame after the impact: {after} (0, the line object off)");
                });
                career.DropContact();
            }

            // 18h: each tracer from its mount's PDC_n marker to the missile, at one frame held still; a shot of it
            IEnumerator TracersPlaced()
            {
                s.QaFreeze = true;
                Look(false);
                yield return null;
                yield return new WaitForEndOfFrame();
                Guard("missile fx 18h tracers", () =>
                {
                    var tr = Get<List<(int mount, Vector3 from, Vector3 to)>>("tracers");
                    var ts = Get<List<Transform>>("missileT");
                    var model = Get<Transform>("shipModel");
                    var root = Get<Transform>("shipT").parent;
                    int working = career.WorkingMounts;
                    var hull = career.Build.Hull;
                    double worstFrom = 0, worstTo = 0;
                    var mounts = new List<int>();
                    foreach (var (m, from, to) in tr)
                    {
                        var marker = Models.FindDeep(model, "PDC_" + m);
                        worstFrom = Math.Max(worstFrom, marker != null ? (root.InverseTransformPoint(marker.position) - from).magnitude * 1000 : double.PositiveInfinity);
                        double dTo = double.PositiveInfinity;
                        foreach (var t in ts) dTo = Math.Min(dTo, (t.localPosition - to).magnitude * 1000);
                        worstTo = Math.Max(worstTo, dTo);
                        if (!mounts.Contains(m)) mounts.Add(m);
                    }
                    var body = Career.ToBody(ship, e.Missiles[0].P);
                    var want = new List<int>();
                    for (int m = 0; m < hull.Mounts.Length; m++) if ((working & (1 << m)) != 0 && PointDefense.Bears(hull.Mounts[m], body)) want.Add(m);
                    mounts.Sort();
                    var mesh = Get<Mesh>("tracerMesh");
                    Check(tr.Count == want.Count * e.Missiles.Count && string.Join(",", mounts) == string.Join(",", want) && worstFrom <= 0.1 && worstTo <= 0.1 && mesh != null && mesh.vertexCount > 0,
                        $"18h tracers at {e.Missiles[0].P.Mag:0.000} km: {tr.Count} tracers ({want.Count} bearing × {e.Missiles.Count} missiles) from PDC_{string.Join(", PDC_", mounts)} (the working mounts that bear: PDC_{string.Join(", PDC_", want)}); " +
                        $"each starts {worstFrom:0.000} m from its marker and ends {worstTo:0.000} m from its missile's flare (≤ 0.1); {mesh?.vertexCount ?? 0} line vertices");
                    if (mesh != null && !made.Contains(mesh)) made.Add(mesh);
                    foreach (var o in Get<List<UnityEngine.Object>>("missileMade")) if (!made.Contains(o)) made.Add(o);
                });
                yield return Shot($"c18-tracers-{shell}");
                s.QaFreeze = false;
            }

            if (go) yield return RunHold("1000x", 3, 12000, 200, false);
            if (go) go = Guard("missile fx camera", () => { career.DropContact(); Look(true); });
            if (go) yield return RunHold("100x", 2, 300, 80, true);
            if (go) yield return TracerRun(false);
            if (go) yield return TracerRun(true);

            // 18d: the flash is gone; 18e: nothing is left once the chase ends
            if (go)
            {
                float t0 = Time.unscaledTime;
                while (Time.unscaledTime - t0 < 0.8f) yield return null;
                go = Guard("missile fx 18e", () => career.DropContact());
                yield return null;
                yield return new WaitForEndOfFrame();
                Guard("missile fx 18e dropped", () =>
                {
                    int alive = 0;
                    foreach (var o in made) if (o != null) alive++;
                    Check(career.Encounter == null && Get<List<Transform>>("missileT").Count == 0 && Holders("Missile") == 0 && Holders("Missile hit") == 0 && Holders("Missile kill") == 0 && Holders("PDC tracers") == 0 && HitF.T == null && KillF.T == null
                          && Get<List<UnityEngine.Object>>("missileMade").Count == 0 && Get<Encounter>("missilesFor") == null && alive == 0 && made.Count >= 9 && Get<string>("missileShown") == "none",
                        $"18e missile fx: after the flash ({HitFlashLeft()}) and DropContact no flare holder ({Holders("Missile")}), tracer line ({Holders("PDC tracers")}) or flash ({Holders("Missile hit")} hit, {Holders("Missile kill")} kill) under the root, {Get<List<UnityEngine.Object>>("missileMade").Count} tracked (0), " +
                        $"{alive} of the {made.Count} materials and meshes made for them still alive (0); the marker: {Get<string>("missileShown")} (none)");
                });
            }

            Guard("missile fx cleanup", () =>
            {
                s.QaFreeze = false;
                career.DropContact();
                if (saved)
                {
                    // 18f: the hits' damage, vented water and lowered drive limit are undone, so later sections start as before
                    career.Damage = dmg0;
                    career.Build = build0;
                    career.Active.Clear();
                    career.Active.AddRange(active0);
                    ship.Fuel = fuel0;
                    career.Apply(ship);
                    ship.MaxG = maxG0;
                    Check(career.Damage.drive == dmg0.drive && career.Damage.sensors == dmg0.sensors && career.Damage.radiators == dmg0.radiators && career.Damage.mounts == dmg0.mounts && career.Build.MountCount == build0.MountCount && career.Failed == failed0 && ship.Fuel == fuel0
                          && career.Active.Count == active0.Length,
                        $"18f missile fx: the career is as before section 18: {career.Build.MountCount} PDC mounts ({build0.MountCount}), damage \"{career.DamageText() ?? "none"}\" (\"{(dmg0.Any ? "as saved" : "none")}\"), {career.Failed} failed ({failed0}), fuel {ship.Fuel:0.000} t ({fuel0:0.000}), {career.Active.Count} jobs ({active0.Length}), max {ship.MaxG:0.00} g");
                }
                GameSettings.ShowLabels = labelsWas;
                if (!saved) return;
                ship.ThrottleCmd = 0; ship.Throttle = 0;
                ship.Pos = pos0; ship.Vel = vel0; ship.Att = att0;
                if (target0 != null) Call("SetTarget", target0);
                Put("camMode", camMode0); Put("pivot", pivot0); Put("camDist", camDist0); Put("yaw", yaw0); Put("pitch", pitch0); Put("warpIdx", warp0);
            });
        }

        string HitFlashLeft() => HitF.T == null && KillF.T == null ? "gone" : "still on";
        OpenSystemScene.Flash HitF => Get<OpenSystemScene.Flash>("hitFlash");
        OpenSystemScene.Flash KillF => Get<OpenSystemScene.Flash>("killFlash");
        const int TracerSeed = 4; // any seed that kills at least one of the two at 5 bearing mounts (0.93 each)
    }
}
