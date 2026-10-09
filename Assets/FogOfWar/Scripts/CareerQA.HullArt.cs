using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // 16o: the corvette's own model (docs/design-combat.md Phase 3, PR 3f; OpenSystemScene.Hull.cs, ART-CONTRACT.md). The
    // files and every marker, a model name with no file; then in the scene the swap to the corvette (its length, its textured
    // hull), the radiators folding while stowed and opening again, the swap back to the frigate with no model left behind,
    // and chase-camera shots with the radiators out and folded. Nothing here is drawn by a shell, so both shells run all of
    // it. The scene's build, dock, ship, camera and model are put back at the end, after a failed or thrown check too.
    public partial class CareerQA
    {
        static readonly string[] HullArtTextures = { "Albedo", "Normal", "Mask", "Emission" };

        static List<string> HullArtMarkers()
        {
            var list = new List<string> { "Nose", "Engine", "Cockpit" };
            foreach (var end in new[] { "Fore", "Aft" })
                foreach (var dir in new[] { "Up", "Down", "Left", "Right" }) list.Add($"RCS_{end}_{dir}");
            for (int i = 0; i < 6; i++) list.Add("PDC_" + i);
            for (int i = 0; i < 4; i++) list.Add("Radiator_" + i);
            return list;
        }

        static int CountNamed(Transform t, string name)
        {
            int n = t.name == name ? 1 : 0;
            foreach (Transform c in t) n += CountNamed(c, name);
            return n;
        }

        // a model spawned here, with the flat materials Models.Spawn made for it
        static void DestroyModel(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) Gfx.Destroy(m);
            Gfx.Destroy(go);
        }

        static bool IsRadiator(Transform t)
        {
            for (; t != null; t = t.parent) if (t.name.StartsWith("Radiator_", StringComparison.Ordinal)) return true;
            return false;
        }

        // the box of these renderers' meshes in `frame`'s own axes (the ship holder: nose +Z), in its units
        static Bounds LocalBox(Transform frame, IEnumerable<Renderer> rs)
        {
            var w2l = frame.worldToLocalMatrix;
            bool any = false;
            var b = new Bounds();
            foreach (var r in rs)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = w2l * r.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(c, Vector3.zero); any = true; }
                    else b.Encapsulate(c);
                }
            }
            return b;
        }

        static string M(Vector3 v) => $"{v.x:0.00} x {v.y:0.00} x {v.z:0.00} m";

        // 16o-1: the files; each marker exactly once in a corvette spawned on its own (not the scene's); a hull model with no file
        void HullArtFileChecks()
        {
            var fbx = Resources.Load<GameObject>("Models/StealthShip");
            var missing = new List<string>();
            foreach (var t in HullArtTextures) if (Resources.Load<Texture2D>($"Models/StealthShip/StealthShip_{t}") == null) missing.Add(t);
            Check(fbx != null && missing.Count == 0,
                $"hull art: Resources has Models/StealthShip {fbx != null} (True) and its four textures, missing {(missing.Count == 0 ? "none" : string.Join(", ", missing))} (none)");

            if (fbx != null)
            {
                var holder = new GameObject("QA hull art markers");
                try
                {
                    var go = Models.Spawn("StealthShip", holder.transform, Color.gray);
                    var markers = HullArtMarkers();
                    var off = new List<string>();
                    foreach (var m in markers) { int c = CountNamed(go.transform, m); if (c != 1) off.Add($"{m} ×{c}"); }
                    Check(markers.Count == 21 && off.Count == 0,
                        $"hull art: the corvette model has each of its {markers.Count} markers (21: Nose, Engine, Cockpit, 8 RCS, PDC_0..5, Radiator_0..3) exactly once; off: {(off.Count == 0 ? "none" : string.Join(", ", off))}");
                }
                finally { DestroyModel(holder); }
            }

            var spawnM = typeof(OpenSystemScene).GetMethod("SpawnHullModel", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (spawnM == null) { Check(false, "hull art: the scene has no SpawnHullModel"); return; }
            var h = new GameObject("QA hull art fallback");
            try
            {
                var bogus = (GameObject)spawnM.Invoke(null, new object[] { "NoSuchHullModel", h.transform });
                var named = new List<string>();
                bool allFiles = true;
                foreach (var hull in ShipBuild.Hulls)
                {
                    bool has = !string.IsNullOrEmpty(hull.Model) && Resources.Load<GameObject>("Models/" + hull.Model) != null;
                    allFiles &= has;
                    named.Add($"{hull.Name}: {hull.Model ?? "-"} ({(has ? "file" : "no file")})");
                }
                Check(bogus.name == "PlayerShip" && allFiles,
                    $"hull art: a hull model named \"NoSuchHullModel\" spawns \"{bogus.name}\" (PlayerShip); every hull's model has its file: {string.Join("; ", named)}");
            }
            finally { DestroyModel(h); }
        }

        // 16o-2, the scene. The build is set by hand (as 16n does) and the model rebuilt through RefreshHullModel; the ship sits
        // 1,000,000 km out from Vesta at rest with it (16h's place), so no port docks it while the radiators are stowed.
        IEnumerator HullArtSceneChecks()
        {
            var corv = HullNamed(CorvetteName);
            if (corv == null) { Check(false, $"hull art scene: no hull named \"{CorvetteName}\""); yield break; }
            string shell = UI.UiShell.Disabled ? "legacy" : "toolkit";
            var setDocked = typeof(Career).GetProperty("Docked").GetSetMethod(true);
            var hullKmM = s.GetType().GetMethod("HullKm", F);
            ShipBuild build0 = null;
            Port dock0 = null;
            double paid0 = 0, fuel0 = 0, maxG0 = 0, sink0 = 0, loud0 = 0, camDist0 = 0;
            D3 pos0 = default, vel0 = default;
            Quaternion att0 = Quaternion.identity;
            Body landed0 = null;
            object camMode0 = null, pivot0 = null;
            int warp0 = 0;
            bool saved = false;

            Transform Model() => Get<Transform>("shipModel");
            Transform Holder() => Get<Transform>("shipT");
            float Km() => (float)hullKmM.Invoke(s, null);
            List<(Transform t, Vector3 scale)> Radiators() => Get<List<(Transform, Vector3)>>("radiators");
            // the model's box in metres with the Engine glow at its import scale (as RefreshHullModel measured it): the chase
            // camera is inside the 1:1 zone, so the holder's scale is km per model unit
            Vector3 Metres(Func<Renderer, bool> which)
            {
                var engine = Get<Transform>("engine");
                Vector3 e0 = engine != null ? engine.localScale : Vector3.one;
                if (engine != null) engine.localScale = Get<Vector3>("engineScale");
                try
                {
                    var rs = new List<Renderer>();
                    foreach (var r in Model().GetComponentsInChildren<Renderer>()) if (which(r)) rs.Add(r);
                    return LocalBox(Holder(), rs).size * Holder().localScale.x * 1000;
                }
                finally { if (engine != null) engine.localScale = e0; }
            }
            // the holder's children: the model and the plume, nothing else
            string Children()
            {
                var names = new List<string>();
                foreach (Transform c in Holder()) names.Add(c.name);
                return string.Join(", ", names);
            }
            string Ratios(out bool allAt, float want)
            {
                var parts = new List<string>();
                allAt = Radiators().Count == 4;
                foreach (var (t, sc) in Radiators())
                {
                    float k = t.localScale.x / sc.x;
                    allAt &= Mathf.Abs(k - want) <= 1e-4f;
                    parts.Add($"{t.name} {k:0.0000}");
                }
                return parts.Count == 0 ? "no radiators" : string.Join(", ", parts);
            }
            void Stow(bool on)
            {
                ship.ThrottleCmd = 0; ship.Throttle = 0;
                ship.LastLoudJd = on ? jd : jd - 3.0 / 24; // inside the corvette's 2,400 s window, or 3 h past it (16h)
                s.TestWatchNow();
            }
            IEnumerator Real(float seconds)
            {
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < seconds) yield return null;
            }

            bool go = hullKmM != null && Guard("hull art setup", () =>
            {
                build0 = career.Build; dock0 = career.Docked; paid0 = career.ShipPaid; sink0 = career.SinkK;
                fuel0 = ship.Fuel; maxG0 = ship.MaxG; loud0 = ship.LastLoudJd;
                pos0 = ship.Pos; vel0 = ship.Vel; att0 = ship.Att; landed0 = ship.Landed;
                camMode0 = Get<object>("camMode"); pivot0 = Get<object>("pivot"); camDist0 = Get<double>("camDist"); warp0 = Get<int>("warpIdx");
                saved = true;

                career.DropContact();
                Put("warpIdx", 0);
                var vesta = sys["Vesta"];
                D3 pV = sys.Pos(vesta, jd);
                ship.Plan = Plan.None; ship.ThrottleCmd = 0; ship.Throttle = 0; ship.Landed = null; ship.Hold = Hold.Off;
                ship.Pos = pV + pV.Norm * 1e6; ship.Vel = sys.Vel(vesta, jd);
                setDocked.Invoke(career, new object[] { null });
                career.Build = ShipBuild.Stock(corv);
                career.Apply(ship);
                ship.Fuel = career.TankCapacity;
                career.SinkK = 0;
                Stow(false);
                Put("camMode", Enum.ToObject(s.GetType().GetNestedType("CamMode", BindingFlags.NonPublic), 0)); // chase
                Call("SetPivot", Enum.ToObject(s.GetType().GetNestedType("Pivot", BindingFlags.NonPublic), 0), 0.16);
                Call("RefreshHullModel");
                Check(Model().name == "StealthShip" && Get<string>("hullModel") == "StealthShip",
                    $"hull art scene: with the corvette fitted RefreshHullModel spawns \"{Model().name}\" (StealthShip), asked for \"{Get<string>("hullModel")}\" (StealthShip)");
            });
            if (hullKmM == null) Check(false, "hull art scene: the scene has no HullKm");
            for (int i = 0; go && i < 3; i++) yield return null; // LateInner scales it and the old model's Destroy lands

            if (go) go = Guard("hull art corvette", () =>
            {
                var model = Model();
                float km = Km();
                var all = Metres(r => true);
                float mpu = Holder().localScale.x * 1000;
                float nose = Holder().InverseTransformPoint(Models.FindDeep(model, "Nose").position).z * mpu;
                float throat = Holder().InverseTransformPoint(Models.FindDeep(model, "Engine").position).z * mpu;
                // the long extent is 60 m by construction (LateInner scales shipLen to realLen); the frame is not. Nose (+0.50)
                // to the Engine's origin at the bell throat (-0.48) is 0.98 of the json's 1.1 model units, so 53.45 m only
                // when nothing on the model reaches past the nozzle exit or the nose
                const float span = 60f * 0.98f / 1.1f;
                Check(model.name == "StealthShip" && Near(km, 0.060, 1e-6) && Near(all.z, 60, 0.05) && all.z >= all.x && all.z >= all.y && nose > 0
                      && Near(nose - throat, span, 0.05),
                    $"hull art scene: the corvette's model \"{model.name}\" (StealthShip), realLen {km:0.000000} km (0.060 ± 1e-6), measured {M(all)} (long extent along the nose, z: 60 ± 0.05), nose at z {nose:0.00} m (> 0), nose to bell throat {nose - throat:0.000} m ({span:0.000} ± 0.05: the 1.1-unit frame), shipLen {Get<float>("shipLen"):0.0000} model units (logged)");

                int pbr = 0, other = 0, engines = 0;
                string albedo = "-";
                Material hullMat = null;
                bool oneMat = true;
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    if (r.name == "Engine") { engines++; continue; }
                    var m = r.sharedMaterial;
                    if (m != null && m.shader.name == "FogOfWar/ShipPBR") { pbr++; albedo = m.GetTexture("_AlbedoTex")?.name ?? "none"; if (hullMat == null) hullMat = m; oneMat &= m == hullMat; }
                    else other++;
                }
                int rads = Radiators().Count;
                Check(pbr >= 5 && other == 0 && oneMat && albedo == "StealthShip_Albedo" && engines == 1 && rads == 4,
                    $"hull art scene: the corvette's hull is the textured ShipPBR on {pbr} renderers (Body and the 4 radiators at least: ≥ 5), {other} other (0), one material {oneMat} (True), albedo \"{albedo}\" (StealthShip_Albedo); {engines} Engine (1) on its glow; {rads} radiators found (4)");

                string kids = Children();
                Check(Holder().childCount == 2 && kids.Contains("StealthShip") && kids.Contains("Plume") && !kids.Contains("PlayerShip"),
                    $"hull art scene: the ship holder holds \"{kids}\" (StealthShip and Plume: the frigate's model is gone)");

                string out0 = Ratios(out bool atOne, 1);
                var radOut = Metres(r => IsRadiator(r.transform));
                Check(!career.Stowed(ship, jd) && atOne,
                    $"hull art scene: radiators out (stowed {career.Stowed(ship, jd)}, False): local X over import {out0} (1 ± 1e-4); their box {M(radOut)}");
            });
            if (go) yield return Shot($"c16o-corvette-out-{shell}");

            // stowed: they fold over 1.5 s of real time
            if (go) go = Guard("hull art stow", () => Stow(true)); // the folded check asks Stowed once the ship has stepped
            if (go) yield return Real(2f);
            if (go) go = Guard("hull art folded", () =>
            {
                string r = Ratios(out bool atFold, 0.05f);
                var hullBox = LocalBox(Holder(), Array.FindAll(Model().GetComponentsInChildren<Renderer>(), x => x.name != "Engine" && !IsRadiator(x.transform)));
                var radBox = LocalBox(Holder(), Array.FindAll(Model().GetComponentsInChildren<Renderer>(), x => IsRadiator(x.transform)));
                float m = Holder().localScale.x * 1000;
                // past the hull's box, in metres, on any side (negative: inside). stealth.py puts the folded stubs about 4 m
                // inside the aft base's flanks (|y| 0.05 against 0.12 model units), so no tolerance
                var over = Vector3.Max(radBox.max - hullBox.max, hullBox.min - radBox.min) * m;
                float worst = Mathf.Max(over.x, over.y, over.z);
                Check(career.Stowed(ship, jd) && atFold && Near(Get<float>("fold"), 1, 1e-6) && worst <= 0f,
                    $"hull art scene: after 2 s of real time stowed ({career.Stowed(ship, jd)}, True; sink {career.SinkK:0.0} K, {ship.Fuel:N0} t aboard, docked {career.Docked?.Name ?? "-"}): local X over import {r} (0.05 ± 1e-4), fold {Get<float>("fold"):0.0000} (1); the folded radiators' box {M(radBox.size * m)} pokes {worst:0.00} m past the hull's {M(hullBox.size * m)} (≤ 0; per axis {M(over)})");
            });
            if (go) yield return Shot($"c16o-corvette-folded-{shell}");

            // out again
            if (go) go = Guard("hull art unstow", () => Stow(false));
            if (go) yield return Real(2f);
            if (go) go = Guard("hull art deployed", () =>
            {
                string r = Ratios(out bool atOne, 1);
                Check(!career.Stowed(ship, jd) && atOne && Near(Get<float>("fold"), 0, 1e-6),
                    $"hull art scene: 2 s after the radiators came out (stowed {career.Stowed(ship, jd)}, False): local X over import {r} (1 ± 1e-4), fold {Get<float>("fold"):0.0000} (0)");
            });

            // back to the frigate
            if (go) go = Guard("hull art frigate", () =>
            {
                career.Build = ShipBuild.Stock();
                career.Apply(ship);
                Call("RefreshHullModel");
            });
            for (int i = 0; go && i < 3; i++) yield return null;
            if (go) go = Guard("hull art frigate again", () =>
            {
                var model = Model();
                float km = Km();
                var all = Metres(r => true);
                string kids = Children();
                Check(model.name == "PlayerShip" && Near(km, 0.052, 1e-6) && Near(all.z, 52, 0.05) && Radiators().Count == 0
                      && Holder().childCount == 2 && kids.Contains("PlayerShip") && kids.Contains("Plume") && !kids.Contains("StealthShip"),
                    $"hull art scene: back on the frigate the model is \"{model.name}\" (PlayerShip), realLen {km:0.000000} km (0.052 ± 1e-6), measured {M(all)} (z: 52 ± 0.05), {Radiators().Count} radiators (0); the holder holds \"{kids}\" (PlayerShip and Plume: no corvette left)");
            });

            Guard("hull art cleanup", () =>
            {
                if (!saved) return;
                career.Build = build0;
                career.Apply(ship);
                ship.Fuel = fuel0; ship.MaxG = maxG0; career.SinkK = sink0; career.ShipPaid = paid0;
                ship.ThrottleCmd = 0; ship.Throttle = 0;
                ship.Pos = pos0; ship.Vel = vel0; ship.Att = att0; ship.Landed = landed0; ship.LastLoudJd = loud0;
                setDocked.Invoke(career, new object[] { dock0 });
                Put("camMode", camMode0); Put("pivot", pivot0); Put("camDist", camDist0); Put("warpIdx", warp0);
                Call("RefreshHullModel");
                s.TestWatchNow();
            });
        }
    }
}
