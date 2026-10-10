using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // A raider's missiles on screen (combat 4c, docs/design-combat.md "In the scene"): each one in flight as a flare and, while
    // it burns, its 30 g plume, nose on the ship, a dot and tag in the Raiders' colour while it is too small to see; tracers from the PDC
    // mounts that bear on it in its last 5 km; a flash at the hull when one hits, and at the missile when the PDCs kill it, each
    // throwing its debris (fx-2, Sparks). Drawn only: the flight, the kill and the hit are Career.Volley's, at any warp.
    public partial class OpenSystemScene
    {
        // [design] the look, in km: no model, the drive's light. A 30 g torch's plume many bodies long; under MissilePx the
        // whole of it is drawn larger, so it reads at the 200 km where the 1× hold starts at 10 km/s (100 m there is 0.5 px)
        const float MissileFlareKm = 0.004f, MissilePlumeKm = 0.1f, MissilePlumeW = 0.006f;
        const float MissilePx = 14, MissileFlarePx = 4; // the plume's least length, and the flare's least width, on screen

        readonly List<Transform> missileT = new List<Transform>();
        readonly List<float> missilePx = new List<float>();  // each plume's true length on screen, in screen px
        readonly List<UnityEngine.Object> missileMade = new List<UnityEngine.Object>(); // the chase's: freed with it
        Encounter missilesFor;        // the chase the flares belong to
        Mesh missileMesh;
        Material missileFlareMat, missilePlumeMat;
        D3 missileLast;               // the nearest missile's P at the last draw: the side a hit comes in on
        double missileLastKms;        // and its closing speed then: the hit's energy

        Sparks sparks;                // fx-2: the hits' and kills' debris, made on the first one
        Sparks Spark() => sparks ??= new Sparks();

        // review checks: how many dots DrawMissiles drew last, and its summary
        int missileDots;
        string missileShown = "none";

        // From LateCareer: one holder per missile in flight, at ship.Pos + P in the scene frame. A hit or the chase's end
        // takes holders away the frame it happens; a new chase gets new materials.
        void LateMissiles()
        {
            var e = career?.Encounter;
            if ((missilesFor != null && e != missilesFor) || missileT.Exists(x => x == null)) DropMissiles(); // or a scene rebuild took them
            int n = e?.Missiles.Count ?? 0;
            if (n > 0 && missileMesh == null)
            {
                missileMesh = Gfx.UVSphere(16, 8);
                missileFlareMat = Gfx.Unlit(new Color(1.6f, 1.35f, 1f, 1f), true);
                missilePlumeMat = Gfx.Mat("Plume");
                missilePlumeMat.SetFloat("_Intensity", 1.6f);
                missileMade.Add(missileMesh); missileMade.Add(missileFlareMat); missileMade.Add(missilePlumeMat);
                missilesFor = e;
            }
            while (missileT.Count > n) { int i = missileT.Count - 1; DestroyHolder(missileT[i]); missileT.RemoveAt(i); missilePx.RemoveAt(i); }
            while (missileT.Count < n)
            {
                var t = new GameObject("Missile").transform;
                t.SetParent(Root, false);
                Gfx.Obj("Missile flare", t, missileMesh, missileFlareMat).transform.localScale = Vector3.one * MissileFlareKm;
                // the Plume shader's nozzle is the sphere's +Z end: the plume runs back from the flare
                var p = Gfx.Obj("Missile plume", t, missileMesh, missilePlumeMat).transform;
                p.localScale = new Vector3(MissilePlumeW, MissilePlumeW, MissilePlumeKm / 2);
                p.localPosition = new Vector3(0, 0, -MissilePlumeKm / 2);
                missileT.Add(t); missilePx.Add(0);
            }
            if (n == 0) { LateTracers(null); LateFlash(hitFlash); LateFlash(killFlash); LateSparks(); return; }
            HullColliders(); // cooked while the salvo flies, not on the frame it hits
            float pxPerRad = UnityEngine.Screen.height / 2f / Mathf.Tan(Cam.fieldOfView * Mathf.Deg2Rad / 2);
            double near = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                var P = e.Missiles[i].P;
                if (P.Mag < near) { near = P.Mag; missileLast = P; missileLastKms = e.Missiles[i].Kms; }
                var t = missileT[i];
                t.GetChild(1).gameObject.SetActive(e.Missiles[i].Burning);
                t.localPosition = Map(ship.Pos + P, out double k);
                if (P.Sqr > 1e-18) t.localRotation = Quaternion.LookRotation(Frame.U(-P.Norm));
                float dist = Mathf.Max(t.localPosition.magnitude, 1e-6f);
                float px = missilePx[i] = (float)(MissilePlumeKm * k) / dist * pxPerRad;
                float scale = (float)k * Mathf.Max(1, MissilePx / Mathf.Max(px, 1e-6f));
                t.localScale = Vector3.one * scale;
                // the flare never under MissileFlarePx across, so the head reads at the far end of a short streak
                t.GetChild(0).localScale = Vector3.one * Mathf.Max(MissileFlareKm, MissileFlarePx / 2 * dist / pxPerRad / scale);
            }
            LateTracers(e);
            LateFlash(hitFlash); LateFlash(killFlash); LateSparks();
        }

        void DestroyHolder(Transform t)
        {
            if (t == null) return;
            t.gameObject.SetActive(false);
            Gfx.Destroy(t.gameObject);
        }

        void DropMissiles()
        {
            foreach (var t in missileT) DestroyHolder(t);
            missileT.Clear(); missilePx.Clear();
            foreach (var o in missileMade) Gfx.Destroy(o);
            missileMade.Clear();
            if (tracerObj != null) DestroyHolder(tracerObj.transform);
            tracerObj = null; tracerMesh = null;
            tracers.Clear();
            missileMesh = null; missileFlareMat = missilePlumeMat = null;
            missilesFor = null;
        }

        // After DrawRaider, as it does: a dot on each missile drawn larger than it is, and a tag with its range and the
        // seconds to impact, one tag where a salvo's flares lie on one spot. Not buttons.
        void DrawMissiles()
        {
            var e = career?.Encounter;
            if (Event.current.type != EventType.Repaint) return;
            missileDots = 0;
            if (e == null || e.Missiles.Count == 0 || missileT.Count != e.Missiles.Count) { missileShown = "none"; return; }
            float ui = App.UiScale;
            var col = FactionColours[(int)Faction.Raiders];
            if (raiderStyle == null) raiderStyle = new GUIStyle(labelStyle) { normal = { textColor = col } };
            var tags = new List<Vector2>();
            string shown = "";
            for (int i = 0; i < e.Missiles.Count; i++)
            {
                var M = e.Missiles[i];
                var t = missileT[i];
                var p = t.localPosition;
                float dist = p.magnitude;
                var sp = Cam.WorldToScreenPoint(p);
                if (dist < 1e-6f || sp.z <= 0 || !CockpitShows(sp)) { shown += " out of view;"; continue; }
                var g = new Vector2(sp.x / ui, (UnityEngine.Screen.height - sp.y) / ui);
                if (App.UnderPanel(g)) { shown += " under a panel;"; continue; }
                bool hidden = false;
                foreach (var (c, cr, cd) in occluders) if (cd < dist * 0.999f && (c - g).magnitude < cr) { hidden = true; break; }
                if (hidden) { shown += " behind a body;"; continue; }
                if (missilePx[i] >= MissilePx) { shown += " seen;"; continue; }
                GUI.color = col;
                GUI.DrawTexture(new Rect(g.x - 1.5f, g.y - 1.5f, 3, 3), Texture2D.whiteTexture);
                GUI.color = Color.white;
                missileDots++;
                if (tags.Exists(x => (x - g).magnitude < 12)) { shown += " dot;"; continue; }
                tags.Add(g);
                string tag = $"Missile · {Km(M.P.Mag)} · {Math.Ceiling(M.ImpactInS):0} s";
                GUI.Label(new Rect(g.x + 5, g.y - 8, raiderStyle.CalcSize(new GUIContent(tag)).x, 16), tag, raiderStyle);
                shown += " " + tag + ";";
            }
            missileShown = shown.Trim();
        }

        // ---------------------------------------------------------------- 4b's PDCs: tracers

        // [design] the look: the rounds as dashes that stream from the mount to the missile at RoundKms, never so close on
        // screen that they read as a solid line
        const float TracerSpacingKm = 0.02f, TracerDashes = 30;
        static readonly Color TracerColour = new Color(1.3f, 1.1f, 0.7f, 1f);
        readonly Transform[] mountT = new Transform[6];   // PDC_n on the hull model now shown
        Transform mountsOf;                               // the hull model mountT was read off
        GameObject tracerObj;
        Mesh tracerMesh;
        readonly List<Vector3> tracerPts = new List<Vector3>();
        readonly List<Color> tracerCols = new List<Color>();
        // review checks: the tracers drawn last, as (mount, from, to) in the root's frame
        readonly List<(int mount, Vector3 from, Vector3 to)> tracers = new List<(int, Vector3, Vector3)>();

        // Each working mount that bears on a missile in its last EnvelopeKm (0.5 s at 10 km/s) fires at it from its
        // PDC_n marker. Only a look: the kill is one draw at impact (Career.Strike), on the bearing then.
        void LateTracers(Encounter e)
        {
            tracers.Clear();
            int working = career.WorkingMounts;
            var hull = career.Build.Hull;
            if (working != 0 && e != null)
                for (int i = 0; i < e.Missiles.Count && i < missileT.Count; i++)
                {
                    var P = e.Missiles[i].P;
                    if (P.Mag > PointDefense.EnvelopeKm) continue;
                    var body = Career.ToBody(ship, P);
                    for (int m = 0; m < hull.Mounts.Length; m++)
                        if ((working & (1 << m)) != 0 && PointDefense.Bears(hull.Mounts[m], body))
                            tracers.Add((m, MountAt(m), missileT[i].localPosition));
                }
            if (tracers.Count == 0) { if (tracerObj != null) tracerObj.SetActive(false); return; }
            if (tracerObj == null)
            {
                tracerMesh = Gfx.LineMesh("PDC tracers");
                var mat = Gfx.Unlit(Color.white, true);
                missileMade.Add(tracerMesh); missileMade.Add(mat);
                tracerObj = Gfx.Obj("PDC tracers", Root, tracerMesh, mat);
            }
            tracerObj.SetActive(true);
            tracerPts.Clear(); tracerCols.Clear();
            // game time, so a pause stops the rounds
            double flown = jd % 1 * 86400 * PointDefense.RoundKms;
            foreach (var (_, from, to) in tracers)
            {
                float len = (to - from).magnitude;
                if (len < 1e-6f) continue;
                var u = (to - from) / len;
                float gap = Mathf.Max(TracerSpacingKm, len / TracerDashes), phase = (float)(flown % gap);
                for (float x = phase; x < len; x += gap)
                {
                    tracerPts.Add(from + u * x); tracerPts.Add(from + u * Mathf.Min(len, x + gap * 0.45f));
                    tracerCols.Add(TracerColour); tracerCols.Add(TracerColour);
                }
            }
            Gfx.SetLines(tracerMesh, tracerPts, tracerCols);
        }

        // PDC_n's place in the root's frame: its marker on the hull model shown, or the hull's centre without one
        Vector3 MountAt(int n)
        {
            if (mountsOf != shipModel)
            {
                mountsOf = shipModel;
                for (int i = 0; i < mountT.Length; i++) mountT[i] = shipModel != null ? Models.FindDeep(shipModel, "PDC_" + i) : null;
            }
            var t = n < mountT.Length ? mountT[n] : null;
            return t != null ? Root.InverseTransformPoint(t.position) : shipT.localPosition;
        }

        // ---------------------------------------------------------------- the hit and kill flashes

        // A flash is its own object, so it outlives the chase (a cabin hit ends it in the same update)
        internal sealed class Flash
        {
            public string Name; public float Seconds, Km; public Color Colour;
            public Transform T; public Material Mat; public Mesh Mesh;
            public float At = -1;   // Time.unscaledTime it fired, or -1
            public D3 Off;          // where, from the ship, in the sim frame
            public int Count;       // review checks: how many fired
            public bool Hot;        // a hit: a cooling blackbody that lights the hull, not a fixed colour
            public double Kms;      // a hit's closing speed
            public bool OnHull;     // review checks: the hit's ray met the hull mesh
        }
        // [design] a hit glows at the hull for 0.6 s (Hot: its colour and light from HitGlow); a kill is a short white pop at the missile
        readonly Flash hitFlash = new Flash { Name = "Missile hit", Seconds = 0.6f, Km = 0.006f, Colour = Color.white, Hot = true };
        readonly Flash killFlash = new Flash { Name = "Missile kill", Seconds = 0.35f, Km = 0.01f, Colour = new Color(1.8f, 1.9f, 2.2f) };

        // The hit as a blackbody (docs/design-combat.md "Impact effects"): [design] a 100 kg missile, 1 % of its kinetic energy
        // radiated, from 6,000 K, cooling as T = T0·(1 + t/τ)^(-1/3) (radiative, dT/dt ∝ -T⁴), so 1,500 K at 1 s; its
        // power L0·(T/T0)⁴ integrates to that 1 %. [calc] 1.05 GW at 10 km/s, 610 × sunlight 10 m away.
        const double MissileKg = 100, FlashEta = 0.01, FlashT0 = 6000, FlashTauS = 1.0 / 63, SunWm2 = 1361;
        const float CoreGain = 8, CoreMax = 16; // [design] the glowing spot's brightness on screen, before exposure

        // At `age` s: its temperature, and its visible light 10 m away in sunlight-at-1-AU units
        internal static float HitGlow(double kms, float age, out float kelvin)
        {
            double x = 1 + Math.Max(age, 0) / FlashTauS;
            kelvin = (float)(FlashT0 * Math.Pow(x, -1.0 / 3));
            double l0 = FlashEta * 0.5 * MissileKg * kms * kms * 1e6 / (3 * FlashTauS);
            double watts = l0 * Math.Pow(x, -4.0 / 3);
            return (float)(watts / (4 * Math.PI * 100) / SunWm2) * Blackbody.VisibleShare(kelvin);
        }

        Transform collidersOf; // the hull model that has MeshColliders for HullHitKm

        void HullColliders()
        {
            if (shipModel == null || collidersOf == shipModel) return;
            collidersOf = shipModel;
            foreach (var mf in shipModel.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && mf.sharedMesh.isReadable && mf.GetComponent<MeshCollider>() == null) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }

        // The hull's surface on the line from its middle along `dir` (unit, sim frame), in km from the middle, and its normal
        // there (sim frame): a ray from outside against the hull's mesh. Half a hull length (4c's point) facing `dir` when the
        // ray misses or there is no model.
        double HullHitKm(D3 dir, out bool onHull, out D3 normal)
        {
            double half = HullKm() * 0.5;
            onHull = false;
            normal = dir;
            if (shipModel == null) return half;
            HullColliders();
            Physics.SyncTransforms();
            float len = shipLen * shipT.lossyScale.x; // the model's length in world units: HullKm() in km
            if (len <= 0) return half;
            var w = Root.TransformDirection(Frame.U(dir)).normalized;
            var ray = new Ray(shipT.position + w * (2 * len), -w);
            float best = float.PositiveInfinity;
            foreach (var c in shipModel.GetComponentsInChildren<MeshCollider>())
                if (c.enabled && c.gameObject.activeInHierarchy && c.Raycast(ray, out var hit, 2 * len) && hit.distance < best)
                {
                    best = hit.distance;
                    normal = Frame.FromU(Root.InverseTransformDirection(hit.normal)).Norm;
                }
            onHull = !float.IsInfinity(best);
            return onHull ? (2 * len - best) / len * HullKm() : half;
        }

        // Career.OnEvent: a hit (a cabin hit says JobFailed, with the same "Hit:" lead) at the hull on the side it came in on;
        // a kill where the missile was last drawn: the kill is drawn at impact, so at 1× that is one frame's flight (167 m at 10 km/s) out.
        // Each throws its debris (fx-2).
        void MissileEvent(Career.Happening what, string text)
        {
            var dir = missileLast.Sqr > 1e-18 ? missileLast.Norm : -ship.Heading;
            double kms = missileLastKms > 0 ? missileLastKms : Career.MissileDvKms;
            if (what == Career.Happening.MissileKilled)
            {
                var off = missileLast.Sqr > 1e-18 ? missileLast : dir * (HullKm() * 0.5);
                Fire(killFlash, off);
                Spark().Kill(off, dir, kms, MissileKg, ship.Vel);
            }
            else if (what == Career.Happening.MissileHit || (what == Career.Happening.JobFailed && text != null && text.StartsWith("Hit:")))
            {
                hitFlash.Kms = kms;
                var off = dir * HullHitKm(dir, out hitFlash.OnHull, out var normal);
                Fire(hitFlash, off);
                Spark().Hit(off, normal, dir, kms, MissileKg, ship.Vel);
            }
        }

        // The debris in the ship's frame (m, Unity axes, origin at the hull's middle) on the root's scale there
        void LateSparks()
        {
            if (sparks == null) return;
            var shipLocal = Map(ship.Pos, out double k);
            var toWorld = Root.localToWorldMatrix * Matrix4x4.TRS(shipLocal, Quaternion.identity, Vector3.one * (float)(k / 1000));
            float sunIrr = lighting != null ? lighting.SunIrr * (1 - lighting.Eclipse) : 1;
            sparks.Update(Time.unscaledDeltaTime, ship.Vel, toWorld, Matrix4x4.Rotate(Quaternion.Inverse(ship.Att)), HullKm() * 1000,
                          Frame.U((camW - ship.Pos) * 1000), ((Vector3)Shader.GetGlobalVector("_FOWSunDir")).normalized, sunIrr,
                          lighting?.Exposure ?? 1, Cam);
        }

        static void Fire(Flash f, D3 off)
        {
            f.Off = off;
            f.At = Time.unscaledTime;
            f.Count++;
        }

        // It swells and fades over its Seconds, never under a few pixels across. A hot one glows and lights the hull as it cools.
        void LateFlash(Flash f)
        {
            float age = Time.unscaledTime - f.At;
            if (f.At < 0 || age > f.Seconds) { DropFlash(f); if (f.Hot) lighting?.SetFlash(default, Color.black, 0); return; }
            if (f.T == null)
            {
                DropFlash(f); // a scene rebuild destroyed the holder: free what it left
                f.Mesh = Gfx.UVSphere(16, 8);
                f.Mat = Gfx.Unlit(Color.white, true);
                f.T = Gfx.Obj(f.Name, Root, f.Mesh, f.Mat).transform;
            }
            float u = age / f.Seconds;
            var c = f.Colour; c.a = (1 - u) * (1 - u);
            f.T.localPosition = Map(ship.Pos + f.Off, out double k);
            if (f.Hot)
            {
                float irr10 = HitGlow(f.Kms, age, out float kelvin), share = irr10 / HitGlow(f.Kms, 0, out _);
                var col = Blackbody.Colour(kelvin);
                c = col * Mathf.Min(CoreGain * share * (lighting?.Exposure ?? 1), CoreMax); c.a = 1;
                float metre = (float)(k / 1000) * Root.lossyScale.x;
                lighting?.SetFlash(Root.TransformPoint(f.T.localPosition), col * irr10, metre);
            }
            f.Mat.SetColor("_Color", c);
            float pxPerRad = UnityEngine.Screen.height / 2f / Mathf.Tan(Cam.fieldOfView * Mathf.Deg2Rad / 2);
            float minKm = 3f * Mathf.Max(f.T.localPosition.magnitude, 1e-6f) / pxPerRad;
            f.T.localScale = Vector3.one * Mathf.Max((float)(f.Km * k) * (0.3f + 0.7f * Mathf.Sqrt(u)), minKm);
        }

        void DropFlash(Flash f)
        {
            if (f.T != null) DestroyHolder(f.T);
            Gfx.Destroy(f.Mat);
            Gfx.Destroy(f.Mesh);
            f.T = null; f.Mat = null; f.Mesh = null;
        }

        // Teardown: the flares, the tracers, the flashes and the debris are the chase's, not the scene's
        void DropMissileFx()
        {
            DropMissiles();
            DropFlash(hitFlash); DropFlash(killFlash);
            hitFlash.At = killFlash.At = -1;
            sparks?.Dispose();
            sparks = null;
        }
    }
}
