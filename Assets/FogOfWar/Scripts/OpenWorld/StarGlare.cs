using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Glare from sunlit bodies in view. When planets, moons or the Sun fill much of the screen, the eye cannot see
    // faint stars or the Milky Way. Every point source then reads Mag magnitudes fainter: the star shader adds Mag
    // to each star's magnitude and the sky-map shader scales by the same flux factor (both through _FOWStarGlare).
    // Bright stars stay. Open space gives 0, and the glare is never negative, so the sky is never brighter than the
    // plain catalogue look. Every other scene sees 0 (ShipLighting.ResetGlobals calls Reset).
    //
    // Per frame: Begin(camera), AddBody for each body, End(dt, exposure). The camera must sit at the render origin.
    // Coverage is the screen fraction a body's disc fills, times the lit fraction of that disc as the camera sees it
    // ((1 + cos phase) / 2), times how bright it reads on screen (its sunlight times the camera exposure, at most 1).
    public class StarGlare
    {
        // The most the glare adds to a point source's magnitude. With the star model's 0.7 slope (FOWCommon.cginc)
        // that is a flux factor of 1/4, and the naked-eye limit drops from 6.5 to about 4.35.
        public const float MaxMag = 2.15f;
        const float FullCoverage = 0.2f;    // a fifth of the screen, fully lit and fully exposed, gives the full glare
        const float EaseSeconds = 0.5f;     // time constant of the eye
        const float MinSin = 0.005f;        // bodies under 0.3 degrees radius cover too little to count (the Sun always counts)
        const float Far = 1e4f;             // stands in for infinity on the tangent plane
        const float HalfPi = Mathf.PI * 0.5f;
        const float SunIrr = 1e30f;         // the Sun reads at full brightness whatever the exposure

        static readonly int idGlare = Shader.PropertyToID("_FOWStarGlare");

        // The current glare in magnitudes, for CPU users such as the belt dots. It is the eased state itself.
        public static float Mag { get; private set; }
        static bool live;                   // false until the first End after a Reset: the glare then snaps

        struct Item { public float cover, irr; }
        readonly List<Item> items = new List<Item>(64);
        Vector3 camPos;
        Quaternion invRot;
        float tanV, tanH;

        public static void Reset()
        {
            Mag = 0;
            live = false;
            Shader.SetGlobalFloat(idGlare, 0);
        }

        public void Begin(Camera cam)
        {
            items.Clear();
            camPos = cam.transform.position;
            invRot = Quaternion.Inverse(cam.transform.rotation);
            tanV = Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
            tanH = tanV * cam.aspect;
        }

        // renderPos: the body's render-space position (its direction is the true direction; the distance is compressed).
        // bodyW, camW, sunW: heliocentric km. The disc uses the real distance, so its angular size is exact.
        public void AddBody(Vector3 renderPos, double radiusKm, D3 bodyW, D3 camW, D3 sunW, bool isSun)
        {
            var toCam = camW - bodyW;
            double dist = toCam.Mag;
            if (dist <= 0) return;
            float sinR = (float)Math.Min(radiusKm / dist, 1.0);
            if (!isSun && sinR < MinSin) return;

            // camera frame: x right, y up, z ahead
            Vector3 p = invRot * (renderPos - camPos);
            float len = p.magnitude;
            if (!(len > 0f)) return;
            float cx = p.x / len, cy = p.y / len, cz = p.z / len;

            // Bounding box of the disc on the tangent plane (u = x / z, v = y / z), exact for a sphere: the sphere's cone
            // touches the vertical plane at azimuth b when |sin(azimuth - b)| = sin(radius) / cos(elevation), and the
            // same for the horizontal planes. A body wholly behind the camera fails one of the two and counts 0; one
            // with its centre behind but its limb in front (low orbit, looking at the horizon) still counts.
            if (!Extent(Mathf.Atan2(cx, cz), sinR / Mathf.Sqrt(cx * cx + cz * cz), out float uMin, out float uMax)) return;
            if (!Extent(Mathf.Atan2(cy, cz), sinR / Mathf.Sqrt(cy * cy + cz * cz), out float vMin, out float vMax)) return;

            // overlap of that box with the screen rectangle
            float ow = Mathf.Min(uMax, tanH) - Mathf.Max(uMin, -tanH);
            float oh = Mathf.Min(vMax, tanV) - Mathf.Max(vMin, -tanV);
            if (ow <= 0f || oh <= 0f) return;

            if (isSun)
            {
                // the part of the disc on screen (or, for a disc bigger than the screen, the part of the screen it
                // fills): the Sun is so bright that any of it in view blinds the eye
                float box = Mathf.Min((uMax - uMin) * (vMax - vMin), 4f * tanH * tanV);
                items.Add(new Item { cover = Mathf.Min(ow * oh / box, 1f), irr = SunIrr });
                return;
            }

            // a disc fills pi/4 of its bounding box
            float cover = 0.25f * Mathf.PI * ow * oh / (4f * tanH * tanV);
            var toSun = sunW - bodyW;
            double ds = toSun.Mag;
            double cosPhase = ds > 0 ? D3.Dot(toSun, toCam) / (ds * dist) : 1;
            float lit = (float)(0.5 * (1 + Math.Max(-1, Math.Min(1, cosPhase))));
            items.Add(new Item { cover = cover * lit, irr = ShipLighting.Irradiance(ds) });
        }

        // After the camera exposure for this frame is known (ShipLighting.Update).
        public void End(float dt, float exposure)
        {
            if (!(exposure > 0f)) exposure = 1f;    // NaN before the first lighting update
            float coverage = 0f;
            for (int i = 0; i < items.Count; i++)
                coverage += items[i].cover * Mathf.Min(items[i].irr * exposure, 1f);
            float t = Mathf.Clamp01(coverage / FullCoverage);
            float target = MaxMag * t * t * (3f - 2f * t);
            Mag = live ? Mag + (target - Mag) * (1f - Mathf.Exp(-Mathf.Max(dt, 0f) / EaseSeconds)) : target;
            live = true;
            Mag = Mathf.Clamp(Mag, 0f, MaxMag);
            Shader.SetGlobalFloat(idGlare, Mag);
        }

        // Range of tan(angle) over the part of [centre - half, centre + half] in front of the camera (|angle| < pi/2),
        // where sin(half) = sinHalf. A range that reaches the plane at 90 degrees runs off to infinity. False when none
        // of it is in front. half never exceeds 90 degrees, so a range cannot wrap round to the front a second time.
        static bool Extent(float centre, float sinHalf, out float lo, out float hi)
        {
            lo = -Far; hi = Far;
            if (!(sinHalf < 1f)) return true;
            float half = Mathf.Max(Mathf.Asin(sinHalf), 1e-5f);
            float a = centre - half, b = centre + half;
            if (a >= HalfPi || b <= -HalfPi) return false;
            if (a > -HalfPi) lo = Mathf.Tan(a);
            if (b < HalfPi) hi = Mathf.Tan(b);
            return true;
        }
    }
}
