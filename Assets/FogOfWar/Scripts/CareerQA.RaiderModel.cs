using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // 17x-17z: the raider's model (docs/design-combat.md Phase 4, PR 4c, D12; OpenSystemScene.RaiderHud.cs LateRaider). A
    // chase started by hand (StartContact) 2.05 km off the ship, 1,000,000 km out from Vesta at rest with it: 17x the model
    // is the courier's hull, textured, at its Fleet.json length; 17y it sits at the pirate Ship's position and attitude in
    // the scene frame, at 1× and at the warp the cap gives when 1M× is asked for; 17z a new chase spawns its own model and
    // the far one keeps the dot, and once the chase is dropped no model or material is left. Shots at 2 km from the chase
    // camera and close up. Nothing here is drawn by a shell, so both shells run all of it. The scene is put back at the
    // end, after a failed or thrown check too.
    public partial class CareerQA
    {
        const double RaiderOffKm = 2.05; // where Plan.Pursue holds (Ship.Pursue.cs: the stand-in's radius + 1 km + 1 km)

        IEnumerator RaiderModelChecks()
        {
            string shell = UI.UiShell.Disabled ? "legacy" : "toolkit";
            bool labelsWas = GameSettings.ShowLabels, saved = false;
            D3 pos0 = default, vel0 = default;
            Quaternion att0 = Quaternion.identity;
            Body target0 = null;
            object camMode0 = null, pivot0 = null;
            double camDist0 = 0;
            float yaw0 = 0, pitch0 = 0;
            int warp0 = 0;
            Contract job = null;
            Encounter e = null;
            Transform holder = null, root = null;
            var mats = new List<UnityEngine.Object>();

            Transform Model() => Get<Transform>("raiderT");
            string Children(Transform t)
            {
                var names = new List<string>();
                foreach (Transform c in t) names.Add(c.name);
                return string.Join(", ", names);
            }
            int Raiders()
            {
                int n = 0;
                foreach (Transform c in root) if (c.name == "Raider") n++;
                return n;
            }
            // the model's length on screen in GUI px, as DrawRaider takes it
            float ModelPx()
            {
                var t = Model();
                if (t == null) return 0;
                float pxPerRad = Screen.height / 2f / Mathf.Tan(app.Cam.fieldOfView * Mathf.Deg2Rad / 2);
                return Get<float>("raiderLen") * t.localScale.x / t.localPosition.magnitude * pxPerRad / app.UiScale;
            }
            object Nested(string type, int v) => Enum.ToObject(s.GetType().GetNestedType(type, BindingFlags.NonPublic), v);
            // the chase from now on, d km off the ship along dir, at rest with it
            Encounter Start(D3 dir, double d) => career.StartContact(job, sys, ship, jd, ship.Pos + dir.Norm * d, ship.Vel);

            bool go = Guard("raider model setup", () =>
            {
                pos0 = ship.Pos; vel0 = ship.Vel; att0 = ship.Att; target0 = ship.Target;
                camMode0 = Get<object>("camMode"); pivot0 = Get<object>("pivot"); camDist0 = Get<double>("camDist");
                yaw0 = Get<float>("yaw"); pitch0 = Get<float>("pitch"); warp0 = Get<int>("warpIdx");
                saved = true;

                if (!labelsWas) GameSettings.ShowLabels = true; // the dot and tag are drawn only with labels on
                career.DropContact();
                Put("warpIdx", 0);
                var vesta = sys["Vesta"];
                D3 pV = sys.Pos(vesta, jd);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0; ship.Landed = null; ship.Hold = Hold.Off;
                ship.Pos = pV + pV.Norm * 1e6; ship.Vel = sys.Vel(vesta, jd);
                Call("SetTarget", vesta);
                job = new Contract { Id = 900401, Kind = JobKind.Cargo, Title = "QA ice", From = "Ceres", To = "Vesta", Tonnes = 20, Pay = 20000, Days = 100, IssuedJd = jd, DeadlineJd = jd + 100, Key = "qa/raider-model" };
                career.Active.Add(job);
                // ahead of the ship, a little off its nose, so the chase camera looks at it past the hull
                var ahead = Frame.FromU(ship.Att * new Vector3(0.12f, 0.06f, 1f));
                e = Start(ahead, RaiderOffKm);
                Put("camMode", Nested("CamMode", 0)); // chase
                Call("SetPivot", Nested("Pivot", 0), 0.16);
            });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) yield return new WaitForEndOfFrame();

            // 17x: the model, its length and its hull
            if (go) go = Guard("raider model 17x", () =>
            {
                holder = Model();
                if (holder == null) { Check(false, $"17x raider model: a chase is on ({career.Encounter == e}) and the scene has no raider model"); throw new InvalidOperationException("no model"); }
                root = holder.parent;
                var model = holder.childCount > 0 ? holder.GetChild(0) : null;
                var f = typeof(OpenSystemScene).GetMethod("FleetModel", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "CourierShip" });
                float jsonM = f != null ? (float)f.GetType().GetField("lengthM").GetValue(f) : float.NaN;
                float km = Get<float>("raiderKm"), mpu = holder.localScale.x * 1000;
                var hull = new List<Renderer>();
                var all = new List<Renderer>();
                foreach (var r in model.GetComponentsInChildren<Renderer>(true)) { all.Add(r); if (r.name != "Engine") hull.Add(r); }
                var box = LocalBox(holder, hull).size * mpu;
                var boxEngine = LocalBox(holder, all).size * mpu;
                Check(holder.gameObject.activeInHierarchy && model != null && model.name == "CourierShip" && Get<Encounter>("raiderFor") == e && Raiders() == 1
                      && Near(jsonM, 45.63, 1e-3) && Near(km * 1000, jsonM, 1e-3) && Near(box.z, 45.63, 0.4563) && box.z >= box.x && box.z >= box.y,
                    $"17x raider model: \"{model?.name ?? "none"}\" (CourierShip) in {Raiders()} holder (1) for this chase {Get<Encounter>("raiderFor") == e} (True); Fleet.json {jsonM:0.00} m (45.63), raiderKm {km * 1000:0.00} m; " +
                    $"measured hull {M(box)} (long extent z: 45.63 ± 1 %), with the Engine glow {M(boxEngine)} (logged); raiderLen {Get<float>("raiderLen"):0.0000} model units; holder \"{Children(holder)}\"");

                int pbr = 0, other = 0, engines = 0;
                string albedo = "-";
                foreach (var r in all)
                {
                    foreach (var m in r.sharedMaterials) if (m != null && !mats.Contains(m)) mats.Add(m);
                    if (r.name == "Engine") { engines++; continue; }
                    if (r.name.EndsWith("plume")) continue;
                    var sm = r.sharedMaterial;
                    if (sm != null && sm.shader.name == "FogOfWar/ShipPBR") { pbr++; albedo = sm.GetTexture("_AlbedoTex")?.name ?? "none"; }
                    else other++;
                }
                foreach (var m in Get<List<UnityEngine.Object>>("raiderMade")) if (!mats.Contains(m)) mats.Add(m);
                Check(pbr >= 1 && other == 0 && albedo == "CourierShip_Albedo" && engines == 1,
                    $"17x raider model: the hull is the textured ShipPBR on {pbr} renderers (≥ 1), {other} other (0), albedo \"{albedo}\" (CourierShip_Albedo); {engines} Engine (1); {mats.Count} materials and meshes tracked");
            });

            // 17y: where it sits, at 1×
            void Placement(string when)
            {
                var shipT = Get<Transform>("shipT");
                var want = Frame.U(e.Pirate.Pos - ship.Pos);
                var got = holder.localPosition - shipT.localPosition;
                double offM = (got - want).magnitude * 1000;
                float ang = Quaternion.Angle(holder.localRotation, e.Pirate.Att);
                // both markers are on the thrust axis (Fleet.json); the holder's centre is the box's, which need not be
                var nose = Models.FindDeep(holder, "Nose");
                var throat = Models.FindDeep(holder, "Engine");
                float noseAng = nose != null && throat != null ? Vector3.Angle(root.InverseTransformPoint(nose.position) - root.InverseTransformPoint(throat.position), Frame.U(e.Pirate.Heading)) : 180;
                bool thrust = e.Pirate.Throttle > 0.005, plume = Get<Transform>("raiderPlume").gameObject.activeSelf;
                Check(offM <= 1 && ang <= 0.01f && noseAng <= 1 && plume == (e.Pirate.Throttle * e.Pirate.AccelG > 0.005),
                    $"17y raider model {when}: {offM:0.000} m from the pirate's position in the scene frame (≤ 1; range {(e.Pirate.Pos - ship.Pos).Mag:0.000} km, scene offset {got.magnitude:0.000} km), " +
                    $"rotation {ang:0.0000}° off Pirate.Att (≤ 0.01), nose {noseAng:0.000}° off its heading (≤ 1); pirate {e.Pirate.Phase}, throttle {e.Pirate.Throttle:0.000}, plume {plume} ({thrust})");
            }
            if (go) go = Guard("raider model 17y 1x", () => Placement("at 1×"));
            if (go) yield return Shot($"c17x-raider-2km-{shell}");
            if (go) go = Guard("raider model dot near", () =>
            {
                float px = ModelPx();
                bool dot = Get<bool>("raiderDot");
                string shown = Get<string>("raiderShown");
                Check(dot == (px <= 40) && shown == $"on: {e.Proxy.Name} · 2 km",
                    $"17y raider model: at 2 km the model is {px:0.0} px long on screen ({Screen.width}×{Screen.height}, UI scale {app.UiScale:0.00}), so the dot is {(dot ? "drawn" : "hidden")} ({(px <= 40 ? "drawn" : "hidden")}: hidden past 40 px); the tag: {shown}");
            });

            // at the warp the cap gives when 1M× is asked for, measured after the frame's draw
            if (go) go = Guard("raider model warp", () => Put("warpIdx", QaWarps.Length - 1));
            for (int i = 0; go && i < 30; i++) yield return null;
            if (go) yield return new WaitForEndOfFrame();
            if (go) go = Guard("raider model 17y warp", () =>
            {
                double fw = Get<double>("frameWarp");
                Check(career.Encounter == e && fw <= Career.CloseWarp && fw > 1, $"17y raider model: 1M× asked, the frame ran at {fw:N0}× (cap {Career.CloseWarp:N0}×, > 1×), chase on {career.Encounter == e}");
                Placement($"at {fw:N0}×");
                Put("warpIdx", 0);
            });

            // close up, from its side
            if (go) go = Guard("raider model close", () =>
            {
                Call("SetTarget", e.Proxy);
                Call("SetPivot", Nested("Pivot", 1), Get<float>("raiderKm") * 2.5);
                var side = Frame.U(D3.Cross(e.Pirate.Heading, Frame.EclNorth).Norm * 0.8 + Frame.EclNorth * 0.35 + e.Pirate.Heading * 0.3).normalized;
                Put("yaw", Mathf.Atan2(side.x, -side.z) * Mathf.Rad2Deg);
                Put("pitch", Mathf.Asin(Mathf.Clamp(side.y, -1, 1)) * Mathf.Rad2Deg);
            });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) yield return Shot($"c17x-raider-close-{shell}");
            if (go) go = Guard("raider model dot close", () =>
            {
                float px = ModelPx();
                bool dot = Get<bool>("raiderDot");
                string shown = Get<string>("raiderShown");
                // the hull's own box (its meshes, in the holder's axes) on screen: the tag, where one is drawn, starts past it
                var box = LocalBox(holder, Array.FindAll(holder.GetChild(0).GetComponentsInChildren<Renderer>(), r => r.name != "Engine"));
                float right = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    var c = app.Cam.WorldToScreenPoint(holder.TransformPoint(box.center + Vector3.Scale(box.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                    right = Mathf.Max(right, c.x / app.UiScale);
                }
                var tag = Get<Rect>("raiderTag");
                bool tagged = shown.StartsWith("on: ");
                Check(!dot && px > 40 && (!tagged || tag.xMin >= right),
                    $"17y raider model: framed close up ({Get<double>("camDist") * 1000:0} m off) the model is {px:0} px long on screen (> 40), so the dot is {(dot ? "drawn" : "hidden")} (hidden); " +
                    (tagged ? $"the tag starts at x {tag.xMin:0} px, past the hull's right edge at {right:0} px" : "no tag (the target in the UI Toolkit HUD)") + $": {shown}");
            });

            // 17z: a new chase spawns its own model; far off, the dot is back; dropped, nothing is left
            Transform first = holder;
            if (go) go = Guard("raider model restart", () =>
            {
                Call("SetTarget", sys["Vesta"]);
                Put("camMode", Nested("CamMode", 0));
                Call("SetPivot", Nested("Pivot", 0), 0.16);
                Put("yaw", yaw0); Put("pitch", pitch0);
                e = Start(Frame.FromU(ship.Att * new Vector3(0.12f, 0.06f, 1f)), 300);
            });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) yield return new WaitForEndOfFrame();
            if (go) go = Guard("raider model 17z far", () =>
            {
                holder = Model();
                float px = ModelPx();
                bool dot = Get<bool>("raiderDot");
                string shown = Get<string>("raiderShown");
                Check(first == null && holder != null && Get<Encounter>("raiderFor") == e && Raiders() == 1 && dot && px <= 40 && shown.StartsWith("on: " + e.Proxy.Name),
                    $"17z raider model: a new chase 300 km off: the first model is gone {first == null} (True), {Raiders()} raider holder (1) for the new chase {Get<Encounter>("raiderFor") == e} (True); " +
                    $"{px:0.00} px long on screen (≤ 40), so the dot is {(dot ? "drawn" : "hidden")} (drawn); the tag: {shown}");
                foreach (var m in Get<List<UnityEngine.Object>>("raiderMade")) if (!mats.Contains(m)) mats.Add(m);
                // closing from 300 km it burns: the plume is on
                Placement("closing from 300 km");
                Check(e.Pirate.Throttle > 0.005 && Get<Transform>("raiderPlume").gameObject.activeSelf,
                    $"17z raider model: closing from 300 km the pirate burns (throttle {e.Pirate.Throttle:0.000}, > 0.005; {e.Pirate.Phase}) and its plume is on ({Get<Transform>("raiderPlume").gameObject.activeSelf})");
                career.DropContact();
            });
            for (int i = 0; go && i < 2; i++) yield return null;
            if (go) yield return new WaitForEndOfFrame();
            if (go) go = Guard("raider model 17z dropped", () =>
            {
                int alive = 0;
                foreach (var m in mats) if (m != null) alive++;
                Check(career.Encounter == null && Model() == null && holder == null && Raiders() == 0 && Get<List<UnityEngine.Object>>("raiderMade").Count == 0 && alive == 0
                      && Get<string>("raiderShown") == "none",
                    $"17z raider model: after DropContact no chase ({career.Encounter == null}), no model ({Model() == null}, the last one gone {holder == null}), {Raiders()} raider holders under the root (0, of {root.childCount} children), " +
                    $"{Get<List<UnityEngine.Object>>("raiderMade").Count} tracked (0), {alive} of the {mats.Count} materials and meshes made for both still alive (0); the tag: {Get<string>("raiderShown")} (none)");
            });

            Guard("raider model cleanup", () =>
            {
                career.DropContact();
                career.Active.RemoveAll(c => c.Key == "qa/raider-model");
                GameSettings.ShowLabels = labelsWas;
                if (!saved) return;
                ship.ThrottleCmd = 0; ship.Throttle = 0;
                ship.Pos = pos0; ship.Vel = vel0; ship.Att = att0;
                if (target0 != null) Call("SetTarget", target0);
                Put("camMode", camMode0); Put("pivot", pivot0); Put("camDist", camDist0); Put("yaw", yaw0); Put("pitch", pitch0); Put("warpIdx", warp0);
            });
        }
    }
}
