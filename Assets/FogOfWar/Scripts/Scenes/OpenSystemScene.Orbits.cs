using System;
using System.Collections.Generic;
using FogOfWar.OpenWorld;
using UnityEngine;

namespace FogOfWar
{
    // Open System: the orbit lines, and the course line of a craft on its own path, rebuilt each frame from LateInner.
    public partial class OpenSystemScene
    {
        void DrawOrbits()
        {
            pts.Clear(); cols.Clear(); targetLine.Clear();
            var own = OwnBody();
            ownSegs = 0;
            // the map with a target set: the rest of the orbits step back
            float rest = MapView && ship.Target != null ? 0.5f : 1f;
            float fade = Mathf.Clamp01((float)(Math.Log10(Math.Max(camDist, 1)) - 3) / 4f);
            foreach (var b in sys.Bodies)
            {
                if (b.Parent == null) continue;
                bool isMoon = b.Parent != sys.Sun;
                // moons' orbits only when the camera is near enough to see them as more than a dot
                var parentW = isMoon ? sys.Pos(b.Parent, jd) : D3.Zero;
                bool tgt = ship.Target == b;
                if (isMoon && (parentW - camW).Mag > b.A * 400) continue;
                if (isMoon && !tgt && b.Radius < 50) continue; // hundreds of small moons: their orbits would be a hairball
                bool mine = b == own && !tgt;
                var c = tgt ? new Color(1f, 0.71f, 0.28f, 0.75f) : mine ? OwnOrbitCol : isMoon ? new Color(0.5f, 0.6f, 0.85f, 0.35f) : new Color(0.45f, 0.55f, 0.85f, 0.4f);
                // planet orbits fade out when the camera is in close: from a ship they'd be streaks across the screen
                if (!isMoon) c.a *= Mathf.Lerp(tgt || mine ? 0.3f : 0f, 1f, fade);
                if (b.Asteroid && !tgt && !mine) c.a *= 0.25f; // two dozen named asteroids: keep their orbits in the background
                if (!tgt && !mine) c.a *= rest;
                if (b.Path != null)
                {
                    // a craft on its own course has no orbit: its line is the course itself (QA-065)
                    if (!CoursePoints(b, line)) continue;
                }
                else
                {
                    line.Clear();
                    const int N = 180;
                    double period = 360 / Math.Max(Math.Abs(b.dL), 1e-9); // days
                    for (int i = 0; i <= N; i++)
                    {
                        double t = jd + period * i / N;
                        // the real (perturbed) path relative to the parent, drawn around where the parent is now
                        line.Add(parentW + (b.Cheb == null && isMoon ? b.Kepler(t) : sys.PosAt(b, t) - (isMoon ? sys.PosAt(b.Parent, t) : D3.Zero)));
                    }
                }
                Vector3 prev = default;
                for (int i = 0; i < line.Count; i++)
                {
                    var q = Map(line[i], out _);
                    if (i > 0) { pts.Add(prev); pts.Add(q); cols.Add(c); cols.Add(c); if (mine) ownSegs++; }
                    prev = q;
                }
                if (tgt) { targetLine.Clear(); targetLine.AddRange(line); }
            }
            DrawMapLines(own, Mathf.Lerp(0.3f, 1f, fade));
            AddThreatRings(fade);
            AddSeenRing(fade);
            // photo mode and F2 (HUD hidden) take the orbit and course lines out of the picture too
            if (App.HideGui || !App.ShowUI) { pts.Clear(); cols.Clear(); }
            OrbitSegments = pts.Count / 2;
            Gfx.SetLines(orbitMesh, pts, cols);
            orbitMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e7f);
        }

        readonly List<D3> line = new List<D3>(), targetLine = new List<D3>();
        public int OrbitSegments { get; private set; }   // line segments drawn last frame (checks)

        // An intercept runner's course from its departure to its arrival, in world km. Other path bodies (the
        // practice beacon) get no line. Treated as an orbit, a runner sampled its path ~1e9 days out, where it sits
        // at its destination port at scattered dates: dozens of chords across that port's orbit.
        bool CoursePoints(Body b, List<D3> outW)
        {
            outW.Clear();
            var job = career?.Active.Find(x => x.Kind == JobKind.Intercept && x.To == b.Name);
            if (job == null || !(job.T1 > job.T0)) return false;
            const int N = 120;
            for (int i = 0; i <= N; i++) outW.Add(sys.PosAt(b, job.T0 + (job.T1 - job.T0) * i / N));
            return true;
        }

        // review checks: the line last drawn for the target, in world km: its segments, length and longest segment's share
        public string TestTargetLineReport(out double longestShare)
        {
            longestShare = -1;
            if (targetLine.Count < 2) return "no line";
            double len = 0, longest = 0;
            for (int i = 1; i < targetLine.Count; i++) { double s = (targetLine[i] - targetLine[i - 1]).Mag; len += s; longest = Math.Max(longest, s); }
            longestShare = longest / Math.Max(len, 1e-9);
            return $"{targetLine.Count - 1} segments, {len / Frame.AU:F2} AU long, longest {longestShare * 100:F1} %";
        }
    }
}
