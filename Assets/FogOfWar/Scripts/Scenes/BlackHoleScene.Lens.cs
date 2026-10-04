using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FogOfWar
{
    // Lens table (visuals step 2): what every direction's ray does from the current camera, so the image pass shades
    // table samples instead of tracing a ray per pixel (BlackHoleCore.cginc, lensSample). The table depends only on the
    // camera's r and θ, the spin and the quality, so turning the camera or animating the disc, debris and hot spot never
    // rebuilds it; a camera that moves in or out rebuilds it each frame. With lensing off, a camera inside 4 M or a
    // debug view of the ray fates, every pixel takes the full trace.
    public partial class BlackHoleScene
    {
        public const int LensN = 1024;
        const int EdgeSamples = 4096;      // photon orbits sampled for the shadow edge (each with both signs of p_θ)
        bool lensTable = true;             // review harness: false = the full trace everywhere (A/B)
        float lensSpp = 8;                 // review harness: 1 for the cost split
        float lensPoleB = 0.6f;            // M: rays with |b| below this take the full trace (review harness: 0 = none)
        bool lensNoTrace;                  // review harness: fallback pixels black instead of traced (cost split)
        double lastR, lastMu;
        int movingFrames;                  // frames in a row the camera's r or θ changed
        Material lensMat;
        Mesh lensQuad;
        CommandBuffer lensCmd;
        readonly RenderTexture[] lensRT = new RenderTexture[5];
        Texture2D lensEdgeTex;
        readonly float[] lensEdge = new float[LensN];
        double keyR, keyMu, keyA = -1;
        int keyQ = -1;
        float keyK;
        bool lensValid;

        public int LensBuilds { get; private set; }
        // VRAM of the table: four RGBA float targets and one RG float target
        public static long LensBytes => (long)LensN * LensN * (4 * 16 + 8);
        public bool LensActive { get; private set; }

        void UpdateLens(double a)
        {
            Kerr.ToBL(Cam.transform.position * 2f, a, out double r, out double mu, out _);
            // a camera that moves in or out frame after frame (falling, riding a probe) would rebuild every frame,
            // which costs more than the full trace: from the second such frame the full trace draws until it settles.
            // Fast quality always takes the full trace (cheaper than the table's samples there).
            bool moved = Math.Abs(r - lastR) > 1e-5 * r || Math.Abs(mu - lastMu) > 1e-5;
            movingFrames = moved ? movingFrames + 1 : 0;
            lastR = r; lastMu = mu;
            bool want = lensTable && lens && debugFate != 1 && debugFate != 2 && r >= 4 && LensSupported
                && App.Quality > 0 && movingFrames < 2;
            if (want && (!lensValid || keyA != a || keyQ != App.Quality || keyK != StepK || Math.Abs(r - keyR) > 1e-5 * r || Math.Abs(mu - keyMu) > 1e-5))
            {
                lensValid = BuildLens(r, mu, a);
                keyR = r; keyMu = mu; keyA = a; keyQ = App.Quality; keyK = StepK;
            }
            LensActive = want && lensValid;
            mat.SetFloat("_LensOn", LensActive ? 1 : 0);
            mat.SetFloat("_LensSpp", lensSpp);
            mat.SetFloat("_LensPoleB", lensPoleB);
            mat.SetFloat("_LensNoTrace", lensNoTrace ? 1 : 0);
        }

        static bool LensSupported => SystemInfo.supportedRenderTargetCount >= 5
            && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat) && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGFloat);

        // Review harness: rebuild now for the current camera (timing).
        public void RebuildLens()
        {
            Kerr.ToBL(Cam.transform.position * 2f, spin, out double r, out double mu, out _);
            lensValid = BuildLens(r, mu, spin);
        }

        bool BuildLens(double r, double mu, double a)
        {
            if (!ShadowEdge(r, mu, a, lensEdge)) return false;
            if (lensMat == null)
            {
                lensMat = Gfx.Mat("BlackHoleLens");
                lensMat.hideFlags = HideFlags.DontSave;
                lensQuad = new Mesh { name = "LensQuad", hideFlags = HideFlags.DontSave };
                lensQuad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
                lensQuad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                lensCmd = new CommandBuffer { name = "BlackHole lens table" };
                lensEdgeTex = new Texture2D(LensN, 1, TextureFormat.RFloat, false, true)
                { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = "BHLensEdge", hideFlags = HideFlags.DontSave };
                for (int i = 0; i < lensRT.Length; i++)
                {
                    lensRT[i] = new RenderTexture(LensN, LensN, 0, i < 4 ? RenderTextureFormat.ARGBFloat : RenderTextureFormat.RGFloat, RenderTextureReadWrite.Linear)
                    { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "BHLens" + i, hideFlags = HideFlags.DontSave };
                    lensRT[i].Create();
                }
            }
            lensEdgeTex.SetPixelData(lensEdge, 0);
            lensEdgeTex.Apply(false);
            // the table pass reads only the camera, the spin and the step settings (march in table mode)
            Kerr.SetCamera(lensMat, Cam.transform.position, a);
            lensMat.SetFloat("_Spin", (float)a);
            lensMat.SetFloat("_RH", (float)Kerr.Horizon(a));
            lensMat.SetFloat("_StepK", StepK);
            lensMat.SetInt("_Steps", Steps);
            lensMat.SetFloat("_LensN", LensN);
            lensMat.SetTexture("_LensEdge", lensEdgeTex);
            lensCmd.Clear();
            var ids = new RenderTargetIdentifier[lensRT.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = lensRT[i];
            lensCmd.SetRenderTarget(ids, ids[0]);
            lensCmd.DrawMesh(lensQuad, Matrix4x4.identity, lensMat, 0, 0);
            Graphics.ExecuteCommandBuffer(lensCmd);
            for (int i = 0; i < lensRT.Length; i++) mat.SetTexture("_LensT" + i, lensRT[i]);
            mat.SetTexture("_LensEdge", lensEdgeTex);
            mat.SetFloat("_LensN", LensN);
            LensBuilds++;
            return true;
        }

        void TeardownLens()
        {
            foreach (var rt in lensRT) if (rt != null) { rt.Release(); Gfx.Destroy(rt); }
            Array.Clear(lensRT, 0, lensRT.Length);
            if (lensMat != null) Gfx.Destroy(lensMat);
            if (lensQuad != null) Gfx.Destroy(lensQuad);
            if (lensEdgeTex != null) Gfx.Destroy(lensEdgeTex);
            lensCmd?.Release();
            lensMat = null; lensQuad = null; lensEdgeTex = null; lensCmd = null;
            lensValid = false;
        }

        // The shadow edge ρc(ψ) seen by the hovering camera at (r, μ): the spherical photon orbits (Teo 2003: b and q of
        // the orbit at radius r between the prograde and retrograde equatorial ones; b² + q = 27 at spin 0) mapped into
        // the camera's ZAMO frame as view directions (ψ around the direction to the hole, ρ away from it), then sampled at
        // the table's ψ bin centres. False if the samples don't close round the hole. The edge only centres the table's
        // warp: an edge a little off costs table resolution, not correctness (cells that disagree fall back).
        static readonly List<(double psi, double rho)> edgePts = new List<(double, double)>(2 * EdgeSamples);
        static bool ShadowEdge(double rc, double muc, double a, float[] edge)
        {
            double stc = Math.Sqrt(Math.Max(1 - muc * muc, 1e-12));
            double rho2 = rc * rc + a * a * muc * muc, D = rc * rc - 2 * rc + a * a;
            double S2 = (rc * rc + a * a) * (rc * rc + a * a) - a * a * D * stc * stc;
            double lapse = Math.Sqrt(rho2 * D / S2), om = 2 * a * rc / S2, varpi = Math.Sqrt(S2 / rho2) * stc, rho = Math.Sqrt(rho2);
            double rp = Kerr.PhotonOrbit(a), rm = Kerr.PhotonOrbit(a, false);
            edgePts.Clear();
            for (int i = 0; i < EdgeSamples; i++)
            {
                double b, q;
                if (a < 1e-3)
                {
                    double chi = 2 * Math.PI * (i + 0.5) / EdgeSamples, sb = Math.Sin(chi), cb = Math.Cos(chi);
                    b = Math.Sqrt(27) * sb; q = 27 * cb * cb;
                }
                else
                {
                    double r = rp + (rm - rp) * (i + 0.5) / EdgeSamples;
                    b = -(r * r * r - 3 * r * r + a * a * r + a * a) / (a * (r - 1));
                    q = r * r * r * (4 * a * a - r * (r - 3) * (r - 3)) / (a * a * (r - 1) * (r - 1));
                }
                double nph = b * lapse / (varpi * (1 - b * om));
                double EZ = 1 / (lapse + om * varpi * nph);
                double pth2 = q - muc * muc * (b * b / (stc * stc) - a * a);
                if (pth2 < 0) continue;
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    double nth = sg * Math.Sqrt(pth2) / (EZ * rho);
                    double nr2 = 1 - nph * nph - nth * nth;
                    if (nr2 < 0) continue;
                    // view direction d = −n: (−√nr2, −nth, −nph) in (r̂, θ̂, φ̂)
                    double psi = Math.Atan2(-nph, -nth);
                    if (psi < 0) psi += 2 * Math.PI;
                    edgePts.Add((psi, Math.Atan2(Math.Sqrt(nth * nth + nph * nph), Math.Sqrt(nr2))));
                }
            }
            if (edgePts.Count < 256) return false;
            edgePts.Sort((x, y) => x.psi.CompareTo(y.psi));
            int n = edgePts.Count;
            double gap = edgePts[0].psi + 2 * Math.PI - edgePts[n - 1].psi;
            for (int i = 1; i < n; i++) gap = Math.Max(gap, edgePts[i].psi - edgePts[i - 1].psi);
            if (gap > 0.3) return false;
            int j = 0;
            for (int k = 0; k < edge.Length; k++)
            {
                double c = (k + 0.5) / edge.Length * 2 * Math.PI;
                while (j < n && edgePts[j].psi < c) j++;
                var lo = j > 0 ? edgePts[j - 1] : (edgePts[n - 1].psi - 2 * Math.PI, edgePts[n - 1].rho);
                var hi = j < n ? edgePts[j] : (edgePts[0].psi + 2 * Math.PI, edgePts[0].rho);
                double f = hi.Item1 > lo.Item1 ? (c - lo.Item1) / (hi.Item1 - lo.Item1) : 0;
                double v = lo.Item2 + (hi.Item2 - lo.Item2) * f;
                if (!(v > 0 && v < Math.PI)) return false;
                edge[k] = (float)v;
            }
            return true;
        }
    }
}
