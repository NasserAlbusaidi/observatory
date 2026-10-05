using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // Streaming terrain helpers: a QA low-pass camera over a named site (-fowqa), and terrain queries.
    public partial class OpenSystemScene
    {
        readonly Plane[] frustum = new Plane[6];
        Body qaBody;
        double qaLat, qaLon, qaAlt, qaPitch;

        Vis VisOf(Body b) => vis.Find(v => v.b == b);

        // Body-frame unit direction (x prime meridian, y north, z east) of a planetocentric latitude / east longitude.
        static D3 SiteDir(double latDeg, double lonDeg)
        {
            double la = latDeg * Math.PI / 180, lo = lonDeg * Math.PI / 180;
            return new D3(Math.Cos(la) * Math.Cos(lo), Math.Sin(la), Math.Cos(la) * Math.Sin(lo));
        }

        static D3 ToWorld(D3 l, D3 prime, D3 pole, D3 east) => prime * l.x + pole * l.y + east * l.z;

        // Sun elevation (deg) at a site on b at jd.
        double SunElevation(Body b, D3 site, double t)
        {
            b.Axes(t, out var prime, out var east, out var pole);
            var up = ToWorld(site, prime, pole, east);
            var toSun = (sys.Pos(sys.Sun, t) - sys.Pos(b, t)).Norm;
            return Math.Asin(Math.Max(-1, Math.Min(1, D3.Dot(up, toSun)))) * 180 / Math.PI;
        }

        // QA: hold the camera altKm above the ground at a site, looking north and pitched down, with the clock moved to
        // the next local morning (sun 15–35° up) so the relief shows. Returns a line for the QA log.
        public string TestLowPass(string body, double latDeg, double lonDeg, double altKm, double pitchDeg, bool moveClock)
        {
            var b = sys[body];
            var v = b != null ? VisOf(b) : null;
            if (v?.terrain == null) { qaBody = null; return $"{body}: no terrain body"; }
            var site = SiteDir(latDeg, lonDeg);
            if (moveClock)
            {
                // step an hour at a time (a lunar or Hermean day is long) up to 200 days for a rising sun at 15–35°
                double t = jd;
                for (int i = 0; i < 24 * 200; i++, t += 1.0 / 24)
                {
                    double e = SunElevation(b, site, t);
                    if (e > 15 && e < 35 && SunElevation(b, site, t + 1.0 / 24) > e) break;
                }
                jd = t;
                warpIdx = 0;
            }
            qaBody = b; qaLat = latDeg; qaLon = lonDeg; qaAlt = altKm; qaPitch = pitchDeg;
            double sr = v.terrain.SurfaceRadiusKm(site.x, site.y, site.z, 0);
            return $"{body} {latDeg:0.0}°, {lonDeg:0.0}°E at {altKm:0.###} km: ground radius {sr:0.000} km ({(sr - (v.terrain.Data?.RefRadiusKm ?? b.Radius)) * 1000:0} m), sun {SunElevation(b, site, jd):0} deg";
        }

        public void TestLowPassOff() => qaBody = null;

        // QA: each shape model with LODs, the LOD on screen now, and which LODs have their baked normal map.
        public string TestLods()
        {
            var parts = new List<string>();
            foreach (var v in vis)
            {
                if (v.lods == null) continue;
                var maps = new List<string>();
                for (int n = 0; n < v.lods.Length; n++)
                    foreach (var r in v.lods[n].GetComponentsInChildren<Renderer>())
                        if (r.sharedMaterial != null && r.sharedMaterial.GetFloat("_HasLodNormal") > 0.5f) { maps.Add("LOD" + n); break; }
                parts.Add($"{v.b.Name} LOD{v.lod} (maps {(maps.Count > 0 ? string.Join("/", maps) : "none")})");
            }
            return $"{parts.Count} shape models with LODs: " + string.Join(", ", parts);
        }

        public string TestTerrainReport(string body)
        {
            var b = sys[body];
            var v = b != null ? VisOf(b) : null;
            if (v?.terrain == null) return $"{body}: no terrain body";
            var t = v.terrain;
            return $"{body}: {(t.Data != null ? t.Data.Product : "smooth")}, quadtree {(t.Active ? "on" : "off")}, {t.ChunksDrawn} chunks drawn " +
                   $"(deepest L{t.MaxLevelDrawn}), {t.ChunksLive} meshes live, {TerrainBody.Pending} queued, {t.Data?.TilesLoaded ?? 0} tiles loaded, quadtree CPU {t.DrawMs:0.00} ms, " +
                   $"camera r {(camW - sys.Pos(b, jd)).Mag:0.000} km, chunk nearest the nadir L{t.NadirLevel} {(t.NadirGapKm < 1e9 ? t.NadirGapKm : -1):0.000} km off, top r {t.NadirTopKm:0.000} km";
        }

        // Camera for the QA low pass; false when it isn't on.
        bool QaCamera()
        {
            if (qaBody == null) return false;
            var site = SiteDir(qaLat, qaLon);
            var v = VisOf(qaBody);
            double sr = v.terrain.SurfaceRadiusKm(site.x, site.y, site.z, 0);
            qaBody.Axes(jd, out var prime, out var east, out var pole);
            var up = ToWorld(site, prime, pole, east);
            camW = sys.Pos(qaBody, jd) + up * (sr + qaAlt);
            // local north, tilted down
            var north = (pole - up * D3.Dot(pole, up)).Norm;
            if (north.Sqr < 0.5) north = prime;
            double p = qaPitch * Math.PI / 180;
            var look = north * Math.Cos(p) - up * Math.Sin(p);
            Cam.transform.rotation = Quaternion.LookRotation(Frame.U(look), Frame.U(up));
            return true;
        }
    }
}
