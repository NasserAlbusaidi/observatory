using UnityEngine;
using UnityEngine.Rendering;

namespace FogOfWar
{
    // Photo-mode grading and lens effects on the main camera (built-in pipeline OnRenderImage). Depth of field uses its
    // own depth pass (Hidden/FogOfWar/PhotoDepth) because the scene shaders have no ShadowCaster pass, so Unity's
    // camera depth texture would be empty.
    [RequireComponent(typeof(Camera))]
    public class PhotoFX : MonoBehaviour
    {
        public float Exposure, Contrast = 1, Saturation = 1, Temperature, Vignette, Grain, Chroma, Letterbox;
        public int Filter;
        public bool Dof, AutoFocus = true;
        public float FocusT = 0.5f;   // manual focus, 0 near … 1 far (log scale around the rig's scale)
        public float Aperture = 0.6f; // 0 … 1
        public float FocusScale = 1;  // the rig's scale: typical distance to what's in view
        public float FocusDistance { get; private set; } = 1;

        public static readonly string[] FilterNames = { "None", "Noir", "Sepia", "Steel", "Expanse", "Bleach", "Thermal", "Film" };

        Camera cam, depthCam;
        Material mat;
        Shader depthShader;
        RenderTexture depthRT;
        float focusInv = 1, focusTarget = 1;
        bool readPending;

        void Awake()
        {
            cam = GetComponent<Camera>();
            var s = Resources.Load<Shader>("Shaders/PhotoFX");
            if (s != null) mat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            depthShader = Resources.Load<Shader>("Shaders/PhotoDepth");
        }

        void OnDestroy()
        {
            if (mat != null) Destroy(mat);
            if (depthRT != null) { depthRT.Release(); Destroy(depthRT); }
            if (depthCam != null) Destroy(depthCam.gameObject);
        }

        public void ResetLook()
        {
            Exposure = 0; Contrast = 1; Saturation = 1; Temperature = 0; Vignette = 0.25f; Grain = 0; Chroma = 0; Filter = 0;
        }

        // Called after the camera is placed for the frame.
        public void RenderDepth()
        {
            if (!enabled || !Dof || depthShader == null) return;
            int w = Mathf.Max(cam.pixelWidth / 2, 16), h = Mathf.Max(cam.pixelHeight / 2, 16);
            if (depthRT == null || depthRT.width != w || depthRT.height != h)
            {
                if (depthRT != null) { depthRT.Release(); Destroy(depthRT); }
                depthRT = new RenderTexture(w, h, 24, RenderTextureFormat.RFloat) { name = "PhotoFX depth", filterMode = FilterMode.Point };
            }
            if (depthCam == null)
            {
                var go = new GameObject("PhotoFX depth camera") { hideFlags = HideFlags.HideAndDontSave };
                depthCam = go.AddComponent<Camera>();
                depthCam.enabled = false;
            }
            depthCam.CopyFrom(cam);
            depthCam.targetTexture = depthRT;
            depthCam.clearFlags = CameraClearFlags.SolidColor;
            depthCam.backgroundColor = Color.clear; // 1/depth = 0: infinitely far
            depthCam.allowHDR = false; depthCam.allowMSAA = false;
            depthCam.RenderWithShader(depthShader, "Queue");

            if (AutoFocus)
            {
                if (!readPending && SystemInfo.supportsAsyncGPUReadback)
                {
                    readPending = true;
                    AsyncGPUReadback.Request(depthRT, 0, w / 2 - 2, 5, h / 2 - 2, 5, 0, 1, r =>
                    {
                        readPending = false;
                        if (r.hasError) return;
                        var d = r.GetData<float>();
                        float best = 0;
                        for (int i = 0; i < d.Length; i++) best = Mathf.Max(best, d[i]); // nearest thing at the centre
                        if (best > 0) focusTarget = best;
                    });
                }
            }
            else focusTarget = 1f / (FocusScale * Mathf.Pow(10, Mathf.Lerp(-1.5f, 2.5f, FocusT)));
            focusInv = Mathf.Lerp(focusInv, focusTarget, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 6));
            FocusDistance = 1f / Mathf.Max(focusInv, 1e-12f);
        }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (mat == null) { Graphics.Blit(src, dst); return; }
            mat.SetFloat("_Exposure", Exposure);
            mat.SetFloat("_Contrast", Contrast);
            mat.SetFloat("_Saturation", Saturation);
            mat.SetFloat("_Temperature", Temperature);
            mat.SetFloat("_Vignette", Vignette);
            mat.SetFloat("_Grain", Grain);
            mat.SetFloat("_Chroma", Chroma);
            mat.SetFloat("_Filter", Filter);
            mat.SetFloat("_Letterbox", Letterbox);
            mat.SetFloat("_Seed", Time.frameCount % 997);
            bool dof = Dof && depthRT != null;
            mat.SetFloat("_DofOn", dof ? 1 : 0);
            RenderTexture blur = null;
            if (dof)
            {
                mat.SetTexture("_DepthTex", depthRT);
                mat.SetFloat("_FocusInv", focusInv);
                mat.SetFloat("_Aperture", Mathf.Lerp(0.15f, 4f, Aperture * Aperture));
                mat.SetFloat("_MaxBlur", 6 + 22 * Aperture * (src.height / 1080f));
                blur = RenderTexture.GetTemporary(src.width / 2, src.height / 2, 0, src.format);
                Graphics.Blit(src, blur, mat, 1);
                mat.SetTexture("_BlurTex", blur);
            }
            Graphics.Blit(src, dst, mat, 0);
            if (blur != null) RenderTexture.ReleaseTemporary(blur);
        }
    }
}
