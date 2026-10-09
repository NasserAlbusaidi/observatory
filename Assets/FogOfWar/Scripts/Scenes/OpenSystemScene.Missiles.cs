using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // A raider's missiles on screen (combat 4c, docs/design-combat.md "In the scene"): each one in flight as a flare and its
    // 20 g plume, nose on the ship, a dot and tag in the Raiders' colour while it is too small to see, and a flash at the
    // hull when one hits. Drawn only: the flight and the hit are Career.Volley's, at any warp. Tracers and kill flashes are
    // 4b's PDCs.
    public partial class OpenSystemScene
    {
        // [design] the look, in km: no model, the drive's light. A 20 g torch's plume many bodies long; under MissilePx the
        // whole of it is drawn larger, so it reads at the 40 km where the 1× hold starts (100 m there is 2 px)
        const float MissileFlareKm = 0.004f, MissilePlumeKm = 0.1f, MissilePlumeW = 0.006f;
        const float MissilePx = 14, MissileFlarePx = 4; // the plume's least length, and the flare's least width, on screen
        const float HitFlashS = 0.6f, HitFlashKm = 0.015f; // real seconds, and the flash's widest radius

        readonly List<Transform> missileT = new List<Transform>();
        readonly List<float> missilePx = new List<float>();  // each plume's true length on screen, in screen px
        readonly List<UnityEngine.Object> missileMade = new List<UnityEngine.Object>(); // the chase's: freed with it
        Encounter missilesFor;        // the chase the flares belong to
        Mesh missileMesh;
        Material missileFlareMat, missilePlumeMat;
        D3 missileLast;               // the nearest missile's P at the last draw: the side a hit comes in on

        // review checks: how many dots and tags DrawMissiles drew last, its summary, and the hits flashed
        int missileDots;
        string missileShown = "none";
        int missileFlashes;

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
            if (n == 0) { LateHitFlash(); return; }
            float pxPerRad = UnityEngine.Screen.height / 2f / Mathf.Tan(Cam.fieldOfView * Mathf.Deg2Rad / 2);
            double near = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                var P = e.Missiles[i].P;
                if (P.Mag < near) { near = P.Mag; missileLast = P; }
                var t = missileT[i];
                t.localPosition = Map(ship.Pos + P, out double k);
                if (P.Sqr > 1e-18) t.localRotation = Quaternion.LookRotation(Frame.U(-P.Norm));
                float dist = Mathf.Max(t.localPosition.magnitude, 1e-6f);
                float px = missilePx[i] = (float)(MissilePlumeKm * k) / dist * pxPerRad;
                float scale = (float)k * Mathf.Max(1, MissilePx / Mathf.Max(px, 1e-6f));
                t.localScale = Vector3.one * scale;
                // the flare never under MissileFlarePx across, so the head reads at the far end of a short streak
                t.GetChild(0).localScale = Vector3.one * Mathf.Max(MissileFlareKm, MissileFlarePx / 2 * dist / pxPerRad / scale);
            }
            LateHitFlash();
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
                var P = e.Missiles[i].P;
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
                string tag = $"Missile · {Km(P.Mag)} · {Math.Ceiling(P.Mag / Career.MissileKms):0} s";
                GUI.Label(new Rect(g.x + 5, g.y - 8, raiderStyle.CalcSize(new GUIContent(tag)).x, 16), tag, raiderStyle);
                shown += " " + tag + ";";
            }
            missileShown = shown.Trim();
        }

        // ---------------------------------------------------------------- the hit flash

        Transform hitFlashT;
        Material hitFlashMat;
        Mesh hitFlashMesh;
        float hitFlashAt = -1;  // Time.unscaledTime of the last hit; the flash is its own object, so it outlives a chase
        D3 hitFlashFrom;        // the side it came in on, a unit vector in the sim frame

        // Career.OnEvent: a missile's hit (a cabin hit says JobFailed, with the same "Hit:" lead)
        void MissileEvent(Career.Happening what, string text)
        {
            if (what != Career.Happening.MissileHit && !(what == Career.Happening.JobFailed && text != null && text.StartsWith("Hit:"))) return;
            hitFlashFrom = missileLast.Sqr > 1e-18 ? missileLast.Norm : -ship.Heading;
            hitFlashAt = Time.unscaledTime;
            missileFlashes++;
        }

        // At the hull on the side the missile came in on: it swells and fades over HitFlashS, never under a few pixels
        void LateHitFlash()
        {
            float age = Time.unscaledTime - hitFlashAt;
            if (hitFlashAt < 0 || age > HitFlashS) { DropHitFlash(); return; }
            if (hitFlashT == null)
            {
                DropHitFlash(); // a scene rebuild destroyed the holder: free what it left
                hitFlashMesh = Gfx.UVSphere(16, 8);
                hitFlashMat = Gfx.Unlit(Color.white, true);
                hitFlashT = Gfx.Obj("Missile hit", Root, hitFlashMesh, hitFlashMat).transform;
            }
            float u = age / HitFlashS;
            hitFlashMat.SetColor("_Color", new Color(2f, 1.6f, 1f, (1 - u) * (1 - u)));
            hitFlashT.localPosition = Map(ship.Pos + hitFlashFrom * (HullKm() * 0.5), out double k);
            float pxPerRad = UnityEngine.Screen.height / 2f / Mathf.Tan(Cam.fieldOfView * Mathf.Deg2Rad / 2);
            float minKm = 3f * Mathf.Max(hitFlashT.localPosition.magnitude, 1e-6f) / pxPerRad;
            hitFlashT.localScale = Vector3.one * Mathf.Max((float)(HitFlashKm * k) * (0.3f + 0.7f * Mathf.Sqrt(u)), minKm);
        }

        void DropHitFlash()
        {
            if (hitFlashT != null) DestroyHolder(hitFlashT);
            Gfx.Destroy(hitFlashMat);
            Gfx.Destroy(hitFlashMesh);
            hitFlashT = null; hitFlashMat = null; hitFlashMesh = null;
        }

        // Teardown: the flares and the flash are the chase's, not the scene's
        void DropMissileFx()
        {
            DropMissiles();
            DropHitFlash();
            hitFlashAt = -1;
        }
    }
}
