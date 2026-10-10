using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace FogOfWar
{
    // Post pass for the black hole scene (BHPost.shader), on the camera while the scene runs. The ray tracer writes linear
    // radiance; this accumulates jittered frames over time (anti-aliasing, and supersampling while the view is still),
    // spreads a few percent of the light into glare, and applies one luminance tonemap. `-fownobloom` turns the glare off.
    [RequireComponent(typeof(Camera))]
    public class BHPost : MonoBehaviour
    {
        public static readonly bool NoGlare = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fownobloom") >= 0;
        public const int MaxFrames = 64, SwirlFrames = 8;
        // Default exposure: the inner disc on the approaching side sits at the top of the tonemap's shoulder, so its
        // beaming over the receding side (≈ 20× in radiance at 10 M edge-on) still reads on screen. The player's
        // exposure slider multiplies it (BlackHoleScene, EV −4 … +4, not saved).
        public const float DefaultExposure = 0.33f;
        public float CoreGlare = 0.03f, WideGlare = 0.07f, Exposure = DefaultExposure;
        const int GlareLevels = 6;   // the widest levels spread a bright disc into a veil over the whole view

        Material mat;
        RenderTexture histA, histB, coreRT;
        readonly RenderTexture[] chain = new RenderTexture[7];
        int frame, n, lastParam, lastPose;
        bool fresh = true;
        float alpha = 1, clampHist;

        // Number of frames accumulated in the current history, and the format the camera hands over (shots driver).
        public int Accumulated => n;
        public RenderTextureFormat SourceFormat { get; private set; }

        // Hot spot light curve: the ray tracer writes the spot's luminance into alpha; when MeasureSpot is set, the
        // frame's mean alpha comes back asynchronously (a mip chain's 1×1 level) into SpotFlux, SpotSamples counts them.
        public bool MeasureSpot;
        public float SpotFlux { get; private set; }
        public int SpotSamples { get; private set; }
        RenderTexture spotRT;
        bool spotPending;

        // Called once per frame before rendering. paramHash covers anything that changes the image (spin, quality,
        // toggles, sliders): a change restarts the accumulation. poseHash covers the camera: a change keeps the history
        // but blends fast and clamps it. dynamic: the sandbox is moving things. swirl: the disc pattern is animating.
        // Returns this frame's sub-pixel jitter in pixels (−0.5 … 0.5).
        public Vector2 Step(int paramHash, int poseHash, bool dynamic, bool swirl)
        {
            frame++;
            // the decision is applied when a frame is actually rendered (OnRenderImage): in batch mode many ticks can pass
            // between renders, and a restart must not be counted away before the frame that needs it is drawn
            if (fresh || paramHash != lastParam) pendingReset = true;
            moving |= poseHash != lastPose || dynamic;   // held until a rendered frame uses it
            this.swirl = swirl;
            fresh = false;
            lastParam = paramHash; lastPose = poseHash;
            int k = frame % MaxFrames + 1;
            return new Vector2(Halton(k, 2) - 0.5f, Halton(k, 3) - 0.5f);
        }

        bool pendingReset = true, moving, swirl;

        void Decide()
        {
            if (pendingReset) { n = 0; alpha = 1; clampHist = 0; pendingReset = false; }
            else if (moving) { n = 6; alpha = 0.15f; clampHist = 1; moving = false; }
            else
            {
                n = Mathf.Min(n + 1, swirl ? SwirlFrames : MaxFrames);
                alpha = 1f / (n + 1);
                clampHist = swirl ? 1 : 0;
            }
        }

        // Start again from this frame (scene cut).
        public void Reset() { fresh = true; pendingReset = true; }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (mat == null)
            {
                var s = Resources.Load<Shader>("Shaders/BHPost");
                if (s == null) { Graphics.Blit(src, dst); enabled = false; Debug.LogError("Fog of War: missing shader Resources/Shaders/BHPost"); return; }
                mat = new Material(s) { hideFlags = HideFlags.DontSave };
            }
            SourceFormat = src.format;
            int w = src.width, h = src.height;
            if (histA == null || histA.width != w || histA.height != h)
            {
                Release();
                histA = NewRT(w, h); histB = NewRT(w, h);
                pendingReset = true;
            }
            Decide();

            if (MeasureSpot && !spotPending && SystemInfo.supportsAsyncGPUReadback)
            {
                if (spotRT == null)
                {
                    spotRT = new RenderTexture(256, 256, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                    { useMipMap = true, autoGenerateMips = true, hideFlags = HideFlags.DontSave };
                    spotRT.Create();
                }
                Graphics.Blit(src, spotRT);
                spotPending = true;
                AsyncGPUReadback.Request(spotRT, 8, TextureFormat.RGBAFloat, r =>
                {
                    spotPending = false;
                    if (r.hasError) return;
                    SpotFlux = r.GetData<Color>()[0].a;
                    SpotSamples++;
                });
            }

            // accumulate into histB from (src, histA), then swap
            mat.SetTexture("_HistTex", histA);
            mat.SetFloat("_Alpha", alpha);
            mat.SetFloat("_Clamp", clampHist);
            Graphics.Blit(src, histB, mat, 0);
            (histA, histB) = (histB, histA);
            var acc = histA;

            // glare chain
            int levels = 0;
            if (!NoGlare)
            {
                int cw = Mathf.Max(1, w / 2), ch = Mathf.Max(1, h / 2);
                for (; levels < GlareLevels && cw >= 8 && ch >= 8; levels++, cw /= 2, ch /= 2)
                {
                    chain[levels] = RenderTexture.GetTemporary(cw, ch, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                    chain[levels].filterMode = FilterMode.Bilinear;
                    Graphics.Blit(levels == 0 ? acc : chain[levels - 1], chain[levels], mat, levels == 0 ? 1 : 2);
                }
                if (levels > 0)
                {
                    if (coreRT == null || coreRT.width != chain[0].width || coreRT.height != chain[0].height)
                    {
                        if (coreRT != null) { coreRT.Release(); Destroy(coreRT); }
                        coreRT = NewRT(chain[0].width, chain[0].height);
                    }
                    Graphics.Blit(chain[0], coreRT);
                    for (int i = levels - 1; i > 0; i--) Graphics.Blit(chain[i], chain[i - 1], mat, 3);
                }
            }
            mat.SetTexture("_CoreTex", levels > 0 ? (Texture)coreRT : Texture2D.blackTexture);
            mat.SetTexture("_WideTex", levels > 0 ? (Texture)chain[0] : Texture2D.blackTexture);
            mat.SetFloat("_Levels", Mathf.Max(levels, 1));
            mat.SetVector("_Glare", levels > 0 ? new Vector4(CoreGlare, WideGlare, 0, 0) : Vector4.zero);
            mat.SetFloat("_PostExposure", Exposure);
            Graphics.Blit(acc, dst, mat, 4);
            for (int i = 0; i < levels; i++) { RenderTexture.ReleaseTemporary(chain[i]); chain[i] = null; }
        }

        static RenderTexture NewRT(int w, int h)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            rt.Create();
            return rt;
        }

        void Release()
        {
            foreach (var rt in new[] { histA, histB, coreRT, spotRT }) if (rt != null) { rt.Release(); Destroy(rt); }
            histA = histB = coreRT = spotRT = null;
        }

        void OnDisable() { fresh = true; pendingReset = true; }
        void OnDestroy() { Release(); if (mat != null) Destroy(mat); }

        static float Halton(int i, int b)
        {
            float f = 1, r = 0;
            while (i > 0) { f /= b; r += f * (i % b); i /= b; }
            return r;
        }
    }
}
