using System;
using UnityEngine;

namespace FogOfWar.OpenWorld
{
    // Subtle bloom on the main camera while the open world runs (built-in pipeline OnRenderImage). The threshold sits
    // above a sunlit white hull, so only the Sun, the drive and specular glints glow. `-fownobloom` turns it off for
    // A/B frame-rate runs.
    [RequireComponent(typeof(Camera))]
    public class Bloom : MonoBehaviour
    {
        public static readonly bool Disabled = Array.IndexOf(Environment.GetCommandLineArgs(), "-fownobloom") >= 0;
        public float Threshold = 0.9f, Knee = 0.4f, Intensity = 0.08f;
        const int Levels = 6;
        Material mat;
        readonly RenderTexture[] chain = new RenderTexture[Levels];

        public static void Enable(Camera cam, bool on)
        {
            if (cam == null) return;
            var b = cam.GetComponent<Bloom>();
            if (b == null)
            {
                if (!on || Disabled) return;
                b = cam.gameObject.AddComponent<Bloom>();
            }
            b.enabled = on && !Disabled;
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (mat == null)
            {
                var s = Resources.Load<Shader>("Shaders/Bloom");
                if (s == null) { Graphics.Blit(src, dst); enabled = false; Debug.LogError("Fog of War: missing shader Resources/Shaders/Bloom"); return; }
                mat = new Material(s);
            }
            mat.SetVector("_Threshold", new Vector4(Threshold, Knee, 0.25f / Mathf.Max(Knee, 1e-4f), 0));
            mat.SetFloat("_Intensity", Intensity);
            var fmt = src.format == RenderTextureFormat.ARGB32 ? RenderTextureFormat.ARGB32 : RenderTextureFormat.ARGBHalf;
            int w = Mathf.Max(1, src.width / 2), h = Mathf.Max(1, src.height / 2);
            int n = 0;
            for (; n < Levels && w >= 8 && h >= 8; n++, w /= 2, h /= 2)
            {
                chain[n] = RenderTexture.GetTemporary(w, h, 0, fmt, RenderTextureReadWrite.Linear);
                chain[n].filterMode = FilterMode.Bilinear;
                Graphics.Blit(n == 0 ? src : chain[n - 1], chain[n], mat, n == 0 ? 0 : 1);
            }
            for (int i = n - 1; i > 0; i--) Graphics.Blit(chain[i], chain[i - 1], mat, 2);
            if (n > 0) mat.SetTexture("_BloomTex", chain[0]);
            else mat.SetTexture("_BloomTex", Texture2D.blackTexture);
            Graphics.Blit(src, dst, mat, 3);
            for (int i = 0; i < n; i++) { RenderTexture.ReleaseTemporary(chain[i]); chain[i] = null; }
        }

        void OnDestroy() { if (mat != null) Destroy(mat); }
    }
}
