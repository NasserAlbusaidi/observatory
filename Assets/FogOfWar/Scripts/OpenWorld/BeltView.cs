using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Draws the Belt with GPU instancing: a camera-facing dot for every rock too small to resolve, and an instanced rock
    // mesh (the art pack's 64-rock set, each with its baked normal map) for the few close enough to show a shape.
    // In the ship view a dot's brightness is the rock's real visual magnitude (size, albedo, Sun and camera distance,
    // phase), drawn through the same star model as the catalogue stars, so most of the belt is invisible, as it is
    // from a real ship. The map view keeps a teaching look where every rock shows.
    public class BeltView
    {
        public delegate Vector3 MapFn(D3 world, out double k);

        const double PhaseG = 0.15;   // IAU H,G slope parameter; the belt average
        const float MaxDotPx = 14f;   // a dot widens like a star past peak 1; this caps it (V -7.7 and brighter)

        readonly Belt belt;
        readonly Mesh quad;
        readonly Material dotMat, rockMat;
        readonly Mesh[] rockMesh = new Mesh[Belt.Shapes];
        readonly Texture2D[] rockNormal = new Texture2D[Belt.Shapes];
        readonly Matrix4x4[] rockLocal = new Matrix4x4[Belt.Shapes];
        readonly Matrix4x4[] mats = new Matrix4x4[1023];
        readonly Vector4[] cols = new Vector4[1023];
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        readonly List<int>[] near = new List<int>[Belt.Shapes];
        public double NearestKm { get; private set; }
        public int NearestIndex { get; private set; }
        public bool Visible = true;
        Vector3[] rp; float[] rk; double[] dShip;
        readonly float[] absMag, lumaLin;  // H magnitude per rock; luminance of its linear colour (map view)
        readonly float[] peaks;            // linear star peak per rock for this frame; 0 = too faint to draw

        public BeltView(Belt belt, Func<UnityEngine.Object, UnityEngine.Object> own)
        {
            this.belt = belt;
            absMag = new float[belt.Count]; lumaLin = new float[belt.Count]; peaks = new float[belt.Count];
            for (int k = 0; k < belt.Count; k++)
            {
                // H = 5 log10(1329 km / (D sqrt(albedo)))
                absMag[k] = (float)(5 * Math.Log10(1329.0 / (2.0 * belt.RadiusKm[k] * Math.Sqrt(belt.Albedo[k]))));
                var c = belt.Colour[k];
                // same gamma and weights as unitLuma() / lumaRGB() in the shader
                lumaLin[k] = 0.2126f * Mathf.Pow(c.r, 2.2f) + 0.7152f * Mathf.Pow(c.g, 2.2f) + 0.0722f * Mathf.Pow(c.b, 2.2f);
            }
            quad = (Mesh)own(new Mesh
            {
                name = "BeltDot",
                vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
            });
            quad.bounds = new Bounds(Vector3.zero, Vector3.one * 2);
            dotMat = (Material)own(Gfx.Mat("BeltDot"));
            dotMat.enableInstancing = true;
            rockMat = (Material)own(Gfx.Mat("Rock"));
            rockMat.enableInstancing = true;
            for (int i = 0; i < Belt.Shapes; i++)
            {
                near[i] = new List<int>();
                var go = Resources.Load<GameObject>($"Models/Asteroids/Asteroid{i:00}");
                var mf = go != null ? go.GetComponentInChildren<MeshFilter>() : null;
                if (mf == null) continue;
                rockMesh[i] = mf.sharedMesh;
                // the mesh's transform inside the prefab (FBX import scale/axes), relative to the prefab root
                rockLocal[i] = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                rockNormal[i] = Resources.Load<Texture2D>($"Models/Asteroids/Normals/Asteroid{i:00}_Normal");
            }
        }

        // diagram = the map view: every rock shows with a teaching brightness instead of its real magnitude.
        public void Draw(double jd, D3 camW, D3 shipW, D3 sunW, Camera cam, MapFn map, bool diagram)
        {
            belt.Update(jd);
            NearestKm = double.MaxValue; NearestIndex = -1;
            if (!Visible) return;
            float pxPerRad = Screen.height / 2f / Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2);
            Shader.SetGlobalVector("_RockSunDir", Frame.U((sunW - camW).Norm));
            foreach (var l in near) l.Clear();
            int n = 0;
            double d0 = jd - 2451545.0;
            // render-space mapping and magnitude are pure per rock, so they run in parallel; batching below stays on this thread
            if (rp == null) { rp = new Vector3[belt.Count]; rk = new float[belt.Count]; dShip = new double[belt.Count]; }
            float glare = StarGlare.Mag;   // sunlit bodies in view dim the dots like the stars
            System.Threading.Tasks.Parallel.For(0, (belt.Count + 1023) / 1024, chunk =>
            {
                int end = Math.Min(belt.Count, (chunk + 1) * 1024);
                for (int j = chunk * 1024; j < end; j++)
                {
                    rp[j] = map(belt.Pos[j], out double kj);
                    rk[j] = (float)kj;
                    dShip[j] = (belt.Pos[j] - shipW).Sqr;
                    if (!diagram) peaks[j] = Art.StarPeak(Magnitude(j, camW, sunW) + glare);
                }
            });
            for (int k = 0; k < belt.Count; k++)
            {
                var w = belt.Pos[k];
                if (dShip[k] < NearestKm) { NearestKm = dShip[k]; NearestIndex = k; }
                var p = rp[k]; double kk = rk[k];
                float dist = p.magnitude;
                if (dist < 1e-6f) continue;
                float rr = (float)(belt.RadiusKm[k] * kk);
                float px = rr / dist * pxPerRad;
                if (px > 2.5f && rockMesh[belt.Mesh[k]] != null) { near[belt.Mesh[k]].Add(k); continue; }
                float size, intensity;
                if (diagram)
                {
                    // never smaller than ~1.2 px; brightness by albedo and sunlight falling off as 1/r².
                    // The shader gives the colour unit luminance, so the colour's own luminance goes back in here.
                    size = Mathf.Max(rr, 1.2f * dist / pxPerRad);
                    double rSunAu = w.Mag / Frame.AU;
                    float b = Mathf.Clamp(belt.Albedo[k] / 0.15f * (float)(7.3 / (rSunAu * rSunAu)), 0.05f, 2f);
                    intensity = 0.55f * b * lumaLin[k];
                }
                else
                {
                    // a star-model point of the rock's real magnitude; the false test also drops a NaN
                    if (!(peaks[k] > 0f)) continue;
                    float shown = Art.StarSpot(peaks[k], out float widthPx);
                    // quad scale = half the width in render units; scaling by dist keeps the width at widthPx on screen
                    size = 0.5f * Mathf.Min(widthPx, MaxDotPx) * dist / pxPerRad;
                    intensity = shown;
                }
                mats[n] = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * size);
                var c = belt.Colour[k];
                cols[n] = new Vector4(c.r, c.g, c.b, intensity);
                if (++n == mats.Length) { Flush(quad, dotMat, n); n = 0; }
            }
            if (n > 0) Flush(quad, dotMat, n);
            NearestKm = Math.Sqrt(NearestKm);

            for (int m = 0; m < Belt.Shapes; m++)
            {
                n = 0;
                foreach (int k in near[m])
                {
                    var p = map(belt.Pos[k], out double kk);
                    var rot = Quaternion.AngleAxis((float)(belt.SpinRate[k] * d0 * Mathf.Rad2Deg % 360), Vector3.up) * belt.Spin0[k];
                    mats[n] = Matrix4x4.TRS(p, rot, Vector3.one * (float)(belt.RadiusKm[k] * kk)) * rockLocal[m];
                    var c = belt.Colour[k];
                    cols[n] = new Vector4(c.r, c.g, c.b, 1);
                    if (++n == mats.Length) { Flush(rockMesh[m], rockMat, n, rockNormal[m]); n = 0; }
                }
                if (n > 0) Flush(rockMesh[m], rockMat, n, rockNormal[m]);
            }
        }

        // Visual magnitude of rock j from the camera: IAU H,G law. It uses the real positions in km, not the render ones
        // (those are log-compressed past LinearLimit), so the distances and the phase angle are true.
        float Magnitude(int j, D3 camW, D3 sunW)
        {
            D3 toSun = sunW - belt.Pos[j], toCam = camW - belt.Pos[j];
            double rKm = toSun.Mag, dKm = toCam.Mag;
            double cosA = rKm > 0 && dKm > 0 ? Math.Clamp(D3.Dot(toSun, toCam) / (rKm * dKm), -1.0, 1.0) : 1.0;
            double t = Math.Sqrt((1 - cosA) / (1 + cosA));   // tan(α/2); infinite at α = 180° (the rock between Sun and camera)
            double phase = (1 - PhaseG) * Math.Exp(-3.33 * Math.Pow(t, 0.63)) + PhaseG * Math.Exp(-1.87 * Math.Pow(t, 1.22));
            double rAu = Math.Max(rKm, 1.0) / Frame.AU, dAu = Math.Max(dKm, 1.0) / Frame.AU;
            return (float)(absMag[j] + 5 * Math.Log10(rAu * dAu) - 2.5 * Math.Log10(Math.Max(phase, 1e-12)));
        }

        void Flush(Mesh mesh, Material mat, int n, Texture2D normal = null)
        {
            mpb.Clear();
            mpb.SetVectorArray("_Col", cols);
            mpb.SetFloat("_HasLodNormal", normal != null ? 1 : 0);
            if (normal != null) mpb.SetTexture("_LodNormal", normal);
            Graphics.DrawMeshInstanced(mesh, 0, mat, mats, n, mpb, UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
    }
}
