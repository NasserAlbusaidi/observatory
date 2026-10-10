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
    // aspect: no hold or cabin hit). With no PDCs: 18a/18b the hold at 1,000× asked (the raider 12,000 km astern) and at 100×
    // asked (alongside): every frame before it at the warp the time-based cap allows (Phase 5 point 1: t / EventS), none
    // stepping past its start, 1× from 20 s out to the impact, the impact the closed form's, the warp given back after as far
    // as the cap allows, the plume on only while the missile burns; 18c a flare
    // per missile at ship.Pos + P in the scene frame, nose on the ship, and the dot and tag while it is drawn larger than it
    // is; 18d the hit flash on the side it came in on, fired by MissileHit. With 6 mounts: 18g tracers only inside 5 km from
    // a working mount that bears, none after the impact, none with every mount dead; 18h each from its PDC_n marker to the
    // missile; 18i a kill flash at the missile on MissileKilled. 18e nothing left once the chase ends; 18f the damage the hits
    // did and the build are undone. Shots of the salvo burning at the hold's start, coasting at 30 km and 1 km, the tracers,
    // the kill and the hit, in both shells. 18j (fx-1): a beam hit seen close, shot at 0, 0.05, 0.15 and 0.4 s. 18k (fx-2): that
    // hit's sparks shot at 0.05, 0.3, 1 and 3 s, thrown out on the side it came in on and fading; the kill's debris drawn. The
    // scene is put back at the end, after a failed or thrown check too.
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

            // a chase d km astern with no salvo of its own, and a salvo of 2 put in flight `km` out, fired from rest just now
            // or past its burn (coasting at 10 km/s); the jd it was put there
            double Salvo(double raiderKm, double km, bool coast = false)
            {
                e = career.StartContact(job, sys, ship, jd, ship.Pos - ship.Heading * raiderKm, ship.Vel);
                e.Magazine = 0;
                var m = new Missile { P = approach * km, Age = coast ? Career.BurnS : 0 };
                e.Missiles.Add(m);
                e.Missiles.Add(m);
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
                // per frame: the seconds to the impact and the cap at its start and end, the rate it ran at, the warp after it
                var rows = new List<(double t0, double fw, double t1, int warp, double c0, double c1)>();
                double tPrev = career.ImpactInS, capPrev = cap, jdHit = double.NaN;
                bool atBurn = !shots, at30 = !shots, atClose = !shots;
                // the plume a frame shows is the last LateUpdate's, drawn from the missile as the frame before left it
                bool? burnLast = e.Missiles.Count > 0 ? e.Missiles[0].Burning : (bool?)null;
                int plumeOn = 0, plumeOff = 0, plumeWrong = 0;
                for (int f = 0; f < 4000 && career.Encounter == e && e.Missiles.Count > 0; f++)
                {
                    yield return null;
                    var mt = Get<List<Transform>>("missileT");
                    if (burnLast.HasValue && mt.Count > 0 && mt[0] != null)
                    {
                        bool on = mt[0].GetChild(1).gameObject.activeSelf;
                        if (on != burnLast.Value) plumeWrong++;
                        if (on) plumeOn++; else plumeOff++;
                    }
                    burnLast = e.Missiles.Count > 0 ? e.Missiles[0].Burning : (bool?)null;
                    double t1 = career.ImpactInS, km1 = e.Missiles.Count > 0 ? e.Missiles[0].P.Mag : 0;
                    double c1 = career.WarpCap;
                    rows.Add((tPrev, Get<double>("frameWarp"), t1, Get<int>("warpIdx"), capPrev, c1));
                    tPrev = t1; capPrev = c1;
                    if (double.IsInfinity(t1)) { jdHit = jd; break; }
                    if (!atBurn && t1 <= Career.ImpactHoldS && burnLast == true) { atBurn = true; yield return Placed("burn", true); }
                    else if (!at30 && km1 <= 30) { at30 = true; yield return Placed("30 km", true); }
                    else if (!atClose && km1 <= 1) { atClose = true; yield return Placed("1 km", false); }
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
                    var hold = first >= 0 ? rows.GetRange(first, rows.Count - first) : new List<(double t0, double fw, double t1, int warp, double c0, double c1)>();
                    double preMax = 0, preMin = double.PositiveInfinity, holdMax = 0, holdS = 0;
                    bool overstep = false;
                    int offRung = 0, offCap = 0;
                    // Phase 5 point 1: a frame's cap is at most t / EventS at its start (the impact is one event; 18b's raider
                    // alongside is another). The frame runs at the cap at its start or the warp asked, the lower (FrameRate), but
                    // the one cut to end at the hold; the warp after it is the highest setting under both the cap at its end and
                    // the warp asked
                    foreach (var r in pre)
                    {
                        preMax = Math.Max(preMax, r.fw); preMin = Math.Min(preMin, r.t1);
                        overstep |= r.fw > Math.Max(1, Math.Min(Career.ChaseWarp, r.t0 / Career.EventS)) * (1 + 1e-9);
                        if (r.t1 <= Career.ImpactHoldS + Career.ImpactSlackS) continue;
                        if (Math.Abs(r.fw / Math.Min(WarpOf(want), r.c0) - 1) > 1e-6) offCap++;
                        if (r.warp != Math.Min(want, RungOf(r.c1))) offRung++;
                    }
                    foreach (var r in hold) { holdMax = Math.Max(holdMax, r.fw); holdS += r.fw / 60; }
                    bool hit = !double.IsNaN(jdHit);
                    double flight = (jdHit - jd0) * 86400, want0 = FlightS(0, km);
                    double fwAfter = Get<double>("frameWarp");
                    int wAfter = Get<int>("warpIdx");
                    Check(hit && pre.Count > 0 && !overstep && offCap == 0 && offRung == 0 && Math.Abs(cap / Math.Max(1, Math.Min(Career.ChaseWarp, FlightS(0, km) / Career.EventS)) - 1) < 1e-6 && preMin >= Career.ImpactHoldS - 1e-3,
                        $"18{(want == 3 ? "a" : "b")} impact hold {label}: {WarpOf(want):N0}× asked, the chase's cap at the start {cap:N1}× (the flight's {FlightS(0, km):N1} s / {Career.EventS:0} s; raider {raiderKm:N0} km off); {pre.Count} frames before the hold ran at up to {preMax:N0}× " +
                        $"(none over t / {Career.EventS:0} s at its start: {!overstep}; {offCap} frames (0) not run at the cap or the warp asked, the lower; {offRung} frames (0) where the warp was not the highest setting under the cap and the warp asked), " +
                        $"none ending inside it: the nearest end {preMin:0.0000} s before the impact (≥ {Career.ImpactHoldS:0} − 0.001)");
                    Check(hit && first >= 0 && hold[0].t0 >= Career.ImpactHoldS - 1e-3 && holdMax <= 1 + 1e-6 && Math.Abs(holdS - hold[0].t0) <= 1.0 / 60 + 1e-3,
                        $"18{(want == 3 ? "a" : "b")} impact hold {label}: the 1× hold began {(first >= 0 ? hold[0].t0 : double.NaN):0.0000} s before the impact (20 to 20.01), {hold.Count} frames to it ran at ≤ {holdMax:0.000000}× (≤ 1), " +
                        $"{holdS:0.000} s of sim time (the hold's start ± one 1/60 s update)");
                    // jd's grid (~40 µs) rounds each step the scene takes, and the missiles fly the frame's time, so jd drifts by
                    // up to half a grid a frame from their clock (about 11 µs at 1×, an old split this does not fix)
                    double grid = (BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(jd) + 1) - jd) * 86400, drift = rows.Count * grid / 2;
                    Check(hit && Math.Abs(flight - want0) <= 1.0 / 60 + 1e-3 + drift,
                        $"18{(want == 3 ? "a" : "b")} impact hold {label}: the salvo hit {flight:0.0000} s after it was put {km:N0} km out from rest (closed form {want0:0.0000} s ± one 1/60 s update and {drift:0.0000} s: half of jd's {grid * 1e6:0} µs grid on each of {rows.Count} frames), the warp and the hold notwithstanding");
                    Check(hit && plumeWrong == 0 && plumeOn > 0 && plumeOff > 0,
                        $"18{(want == 3 ? "a" : "b")} plume {label}: drawn on {plumeOn} frames and off {plumeOff} (both > 0: the burn is the first {Career.BurnS:0.0} s), {plumeWrong} frames (0) where it did not match the missile's burn");
                    double capAfter = career.WarpCap;
                    int wBack = Math.Min(want, RungOf(capAfter));
                    // 18a's raider is 12,000 km off, so its next event lets the setting back up too; 18b's, 300 km off, does not
                    double rateBack = Math.Min(WarpOf(want), capAfter);
                    Check(hit && career.Encounter == e && (want != 3 || wBack > 0) && wAfter == wBack && Math.Abs(fwAfter / rateBack - 1) < 0.02,
                        $"18{(want == 3 ? "a" : "b")} impact hold {label}: after the impact the cap is {capAfter:N1}× (the raider's next event), the warp {WarpOf(wAfter):N0}× is given back ({WarpOf(wBack):N0}×: the highest under the cap and the {WarpOf(want):N0}× asked once, before the salvo), and the next frame ran at {fwAfter:N1}× ({rateBack:N1}×, the cap or the warp asked, ± 2 %)");
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
                // the impact effects (fx-1): on the hull mesh, a light on it brighter than sunlight 10 m out, hot
                var light = Shader.GetGlobalVector("_FOWFlashCol");
                float lum = new Color(light.x, light.y, light.z).grayscale, off = (float)(HitF.Off.Mag * 1000 / career.Build.Hull.LengthM);
                Check(t != null && t.gameObject.activeInHierarchy && flashes == 2 && notes == 2 && ang <= 1 && e.Missiles.Count == 0 && Holders("Missile") == 0
                      && HitF.OnHull && off < 0.6f && light.w == 1 && lum > 1,
                    $"18d hit flash: {notes} hit notes (2: the salvo), {flashes} flashes fired (2); the flash is {(t != null && t.gameObject.activeInHierarchy ? "on" : "off")}, {ang:0.000}° off the side the salvo came in on (≤ 1); " +
                    $"{e.Missiles.Count} missiles and {Holders("Missile")} flare holders left (0); on the hull mesh {HitF.OnHull} (True), {off:0.000} hull lengths from the middle (< 0.6); " +
                    $"the hull light {(light.w == 1 ? "on" : "off")}, {lum:0.0} × sunlight at 10 m (> 1) at {HitF.Kms:0.0} km/s");
            }

            // 18g-18i: the PDCs' tracers and the kill flash. A salvo 8 km out at 1×, past its burn at 10 km/s as from a raider at
            // rest: with 6 working mounts (`dead` false) the
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
                    Salvo(300, 8, true);
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
                    if (!placed && near <= 1.2)
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
                        Check(e.Killed > 0 && kills == e.Killed && killNotes == e.Killed && hits == 2 - e.Killed && t != null && t.gameObject.activeInHierarchy && ang <= 1 && off <= OneFrameKm,
                            $"18i kill flash: combatRng seeded {TracerSeed}, {bearing / 2} mounts bearing: {e.Killed} of 2 killed (≥ 1), {killNotes} MissileKilled notes, {kills} kill flashes and {hits} hit flashes ({e.Killed} and {2 - e.Killed}); " +
                            $"the kill flash is {(t != null && t.gameObject.activeInHierarchy ? "on" : "off")} {off * 1000:0.0} m from the ship (≤ {OneFrameKm * 1000:0}: one 1× frame's flight at 10 km/s), {ang:0.000}° off the salvo's side (≤ 1)");
                    });
                    yield return Shot($"c18-kill-{shell}");
                    yield return KillDebris();
                }
                Guard($"missile fx 18g {label}", () =>
                {
                    if (dead)
                        Check(inside > 0 && bearing == 0 && wrong == 0 && outside == 0 && after == 0,
                            $"18g tracers, {label}: {inside} frames inside {PointDefense.EnvelopeKm:0} km with {bearing} mounts bearing (0) drew none ({wrong} frames with tracers, 0), and none at the impact ({after}, 0)");
                    else
                        Check(inside >= InsideFrames && bearing > 0 && wrong == 0 && outside == 0 && after == 0,
                            $"18g tracers, {label}: {outside} frames past {PointDefense.EnvelopeKm:0} km drew tracers (0); {inside} frames inside it (≥ {InsideFrames}: 90 % of 0.5 s at 1×) drew one a missile a bearing mount ({bearing} at most) in all but {wrong} (0); " +
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

            // 18j: the impact effects (fx-1, docs/design-combat.md "Impact effects"). A coasting salvo 3 km out on the beam, a
            // little above it, no PDCs; the camera 90 m off on the side it comes in on. Shots one frame past the impact, then with
            // the flash set to 0.05, 0.15 and 0.4 s old: it lights the hull, then cools to orange and fades
            IEnumerator ImpactShots()
            {
                var keep = approach;
                bool ok = Guard("missile fx 18j start", () =>
                {
                    career.Build.pick[(int)Slot.PointDefense] = 0;
                    career.Damage = new ShipDamage();
                    career.Apply(ship);
                    approach = (D3.Cross(ship.Heading, Frame.EclNorth).Norm + Frame.EclNorth * 0.3).Norm;
                    Salvo(300, 3, true);
                    Put("warpIdx", 0);
                    var dir = Frame.U((approach + ship.Heading * 0.6 + Frame.EclNorth * 0.2).Norm).normalized;
                    Put("camMode", Nested("CamMode", 1));
                    Call("SetPivot", Nested("Pivot", 0), 0.09);
                    Put("yaw", Mathf.Atan2(dir.x, -dir.z) * Mathf.Rad2Deg);
                    Put("pitch", Mathf.Asin(Mathf.Clamp(dir.y, -1, 1)) * Mathf.Rad2Deg);
                });
                approach = keep;
                if (!ok) yield break;
                int hit0 = HitF.Count;
                for (int f = 0; f < 600 && HitF.Count == hit0; f++) yield return null;
                var ages = new List<string>();
                var lums = new List<string>();
                foreach (float at in new[] { 0f, 0.05f, 0.15f, 0.4f })
                {
                    // a 5120 px capture takes longer than 0.05 s: set the flash's age, so each shot is one frame past it
                    if (at > 0) HitF.At = Time.unscaledTime - at;
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    var light = Shader.GetGlobalVector("_FOWFlashCol");
                    ages.Add($"{Time.unscaledTime - HitF.At:0.000}");
                    lums.Add($"{new Color(light.x, light.y, light.z).grayscale:0.0}");
                    yield return Shot($"c18-impact-{at * 1000:000}ms-{shell}");
                }
                Check(HitF.Count > hit0 && HitF.OnHull,
                    $"18j impact shots: {HitF.Count - hit0} hits (2) on the hull mesh {HitF.OnHull} (True) at {HitF.Kms:0.0} km/s; shot at {string.Join(", ", ages)} s after the hit, " +
                    $"the hull light then {string.Join(", ", lums)} × sunlight at 10 m (logged)");
            }

            // 18k: the kill's debris (fx-2), its burst set 0.01 s old: the pieces drawn, the newest burst a kill's
            IEnumerator KillDebris()
            {
                var sp = Get<Sparks>("sparks");
                bool pinned = sp != null && sp.PinAge(1, 0.01f);
                yield return null;
                yield return new WaitForEndOfFrame();
                Guard("missile fx 18k kill debris", () =>
                {
                    int drawn = sp?.Drawn() ?? 0;
                    Check(sp != null && sp.Ok && pinned && drawn > 0,
                        $"18k kill debris: sparks {(sp == null ? "never made" : sp.Ok ? "on" : "off (no compute shaders)")}, a kill burst {(pinned ? "found" : "missing")}, {drawn} pieces drawn 0.01 s after it (> 0)");
                });
                yield return Shot($"c18-kill-debris-{shell}");
            }

            // 18k: the hit's sparks (fx-2) after 18j's shots, the newest hit set to each age: thrown out on the side it came in
            // on, fewer pieces as they cool
            IEnumerator SparkShots()
            {
                var sp = Get<Sparks>("sparks");
                var drawn = new List<int>();
                float ang = 180;
                bool pinned = true;
                foreach (float at in new[] { 0.05f, 0.3f, 1f, 3f })
                {
                    pinned &= sp != null && sp.PinAge(0, at);
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Guard($"missile fx 18k sparks {at:0.00} s", () =>
                    {
                        drawn.Add(sp?.Drawn() ?? 0);
                        if (at != 0.3f || sp == null) return;
                        // where the pieces are, from the hit, against the side the salvo came in on
                        var root = Get<Transform>("shipT").parent;
                        var hitAt = Get<Transform>("shipT").localPosition + Frame.U(HitF.Off);
                        var mean = Vector3.zero;
                        var heads = sp.Heads(4096);
                        foreach (var h in heads) mean += root.InverseTransformPoint(h) - hitAt;
                        if (heads.Length > 0) ang = Vector3.Angle(mean, Frame.U(HitF.Off.Norm)); // 18j's approach is put back by then
                    });
                    yield return Shot($"c18-sparks-{at * 1000:0000}ms-{shell}");
                }
                Guard("missile fx 18k sparks", () =>
                    Check(sp != null && sp.Ok && pinned && drawn.Count == 4 && drawn[1] > 1000 && drawn[3] < drawn[1] && ang <= 60,
                        $"18k sparks: {(sp == null ? "never made" : sp.Ok ? "on" : "off (no compute shaders)")}, the hit's burst {(pinned ? "pinned" : "missing")}; " +
                        $"pieces drawn at 0.05, 0.3, 1 and 3 s: {string.Join(", ", drawn)} (> 1,000 at 0.3 s, fewer at 3 s); at 0.3 s their mean is {ang:0.0}° off the side the salvo came in on (≤ 60)"));
            }

            // from rest: 2,000 km is the fire range's 217.0 s; 250 km is 42.0 s, the burn ending 80 km out, in the hold
            if (go) yield return RunHold("1000x", 3, 12000, Career.FireKm, false);
            if (go) go = Guard("missile fx camera", () => { career.DropContact(); Look(true); });
            // 18a's hit flash fades first: 18b's burn shot comes 0.2 s of real time after its start
            for (float t0 = Time.unscaledTime; go && Time.unscaledTime - t0 < 0.8f;) yield return null;
            if (go) yield return RunHold("100x", 2, 300, 250, true);
            if (go) yield return TracerRun(false);
            if (go) yield return TracerRun(true);
            if (go) yield return ImpactShots();
            if (go) yield return SparkShots();

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
                    Check(Shader.GetGlobalVector("_FOWFlashCol").w == 0 && career.Encounter == null && Get<List<Transform>>("missileT").Count == 0 && Holders("Missile") == 0 && Holders("Missile hit") == 0 && Holders("Missile kill") == 0 && Holders("PDC tracers") == 0 && HitF.T == null && KillF.T == null
                          && Get<List<UnityEngine.Object>>("missileMade").Count == 0 && Get<Encounter>("missilesFor") == null && alive == 0 && made.Count >= 9 && Get<string>("missileShown") == "none",
                        $"18e missile fx: after the flash ({HitFlashLeft()}; its hull light {(Shader.GetGlobalVector("_FOWFlashCol").w == 0 ? "off" : "on")}) and DropContact no flare holder ({Holders("Missile")}), tracer line ({Holders("PDC tracers")}) or flash ({Holders("Missile hit")} hit, {Holders("Missile kill")} kill) under the root, {Get<List<UnityEngine.Object>>("missileMade").Count} tracked (0), " +
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
        // any seed that kills one of the two at 10 km/s with 4 to 6 mounts bearing (0.35 to 0.48 each): its first draw is 0.29
        const int TracerSeed = 3;
        // the tracer salvo at 10 km/s: one 1× frame's flight with a fifth to spare, and 90 % of the frames inside EnvelopeKm
        static double OneFrameKm => Career.MissileDvKms / 60 * 1.2;
        static int InsideFrames => (int)(0.9 * 60 * PointDefense.EnvelopeKm / Career.MissileDvKms);
    }
}
