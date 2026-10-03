using System;
using System.Collections.Generic;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Draws the Belt with GPU instancing: a camera-facing dot for every rock too small to resolve, and an instanced rock
    // mesh (the art pack's 32-rock set) for the few close enough to show a shape.
    public class BeltView
    {
        public delegate Vector3 MapFn(D3 world, out double k);

        readonly Belt belt;
        readonly Mesh quad;
        readonly Material dotMat, rockMat;
        readonly Mesh[] rockMesh = new Mesh[32];
        readonly Matrix4x4[] rockLocal = new Matrix4x4[32];
        readonly Matrix4x4[] mats = new Matrix4x4[1023];
        readonly Vector4[] cols = new Vector4[1023];
        readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        readonly List<int>[] near = new List<int>[32];
        public double NearestKm { get; private set; }
        public int NearestIndex { get; private set; }
        public bool Visible = true;
        Vector3[] rp; float[] rk; double[] dShip;

        public BeltView(Belt belt, Func<UnityEngine.Object, UnityEngine.Object> own)
        {
            this.belt = belt;
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
            for (int i = 0; i < 32; i++)
            {
                near[i] = new List<int>();
                var go = Resources.Load<GameObject>($"Models/Asteroids/Asteroid{i:00}");
                var mf = go != null ? go.GetComponentInChildren<MeshFilter>() : null;
                if (mf == null) continue;
                rockMesh[i] = mf.sharedMesh;
                // the mesh's transform inside the prefab (FBX import scale/axes), relative to the prefab root
                rockLocal[i] = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            }
        }

        public void Draw(double jd, D3 camW, D3 shipW, D3 sunW, Camera cam, MapFn map)
        {
            belt.Update(jd);
            NearestKm = double.MaxValue; NearestIndex = -1;
            if (!Visible) return;
            float pxPerRad = Screen.height / 2f / Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2);
            Shader.SetGlobalVector("_RockSunDir", Frame.U((sunW - camW).Norm));
            foreach (var l in near) l.Clear();
            int n = 0;
            double d0 = jd - 2451545.0;
            // render-space mapping is pure per rock, so it runs in parallel; batching below stays on this thread
            if (rp == null) { rp = new Vector3[belt.Count]; rk = new float[belt.Count]; dShip = new double[belt.Count]; }
            System.Threading.Tasks.Parallel.For(0, (belt.Count + 1023) / 1024, chunk =>
            {
                int end = Math.Min(belt.Count, (chunk + 1) * 1024);
                for (int j = chunk * 1024; j < end; j++)
                {
                    rp[j] = map(belt.Pos[j], out double kj);
                    rk[j] = (float)kj;
                    dShip[j] = (belt.Pos[j] - shipW).Sqr;
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
                // never smaller than ~1.2 px; brightness by albedo and sunlight falling off as 1/r²
                float size = Mathf.Max(rr, 1.2f * dist / pxPerRad);
                double rSunAu = w.Mag / Frame.AU;
                float b = Mathf.Clamp(belt.Albedo[k] / 0.15f * (float)(7.3 / (rSunAu * rSunAu)), 0.05f, 2f);
                mats[n] = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * size);
                var c = belt.Colour[k];
                cols[n] = new Vector4(c.r, c.g, c.b, 0.55f * b);
                if (++n == mats.Length) { Flush(quad, dotMat, n); n = 0; }
            }
            if (n > 0) Flush(quad, dotMat, n);
            NearestKm = Math.Sqrt(NearestKm);

            for (int m = 0; m < 32; m++)
            {
                n = 0;
                foreach (int k in near[m])
                {
                    var p = map(belt.Pos[k], out double kk);
                    var rot = Quaternion.AngleAxis((float)(belt.SpinRate[k] * d0 * Mathf.Rad2Deg % 360), Vector3.up) * belt.Spin0[k];
                    mats[n] = Matrix4x4.TRS(p, rot, Vector3.one * (float)(belt.RadiusKm[k] * kk)) * rockLocal[m];
                    var c = belt.Colour[k];
                    cols[n] = new Vector4(c.r, c.g, c.b, 1);
                    if (++n == mats.Length) { Flush(rockMesh[m], rockMat, n); n = 0; }
                }
                if (n > 0) Flush(rockMesh[m], rockMat, n);
            }
        }

        void Flush(Mesh mesh, Material mat, int n)
        {
            mpb.Clear();
            mpb.SetVectorArray("_Col", cols);
            Graphics.DrawMeshInstanced(mesh, 0, mat, mats, n, mpb, UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }
    }
}
