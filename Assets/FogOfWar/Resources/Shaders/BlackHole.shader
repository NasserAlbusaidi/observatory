// Kerr (spinning) black hole with a thin Novikov–Thorne accretion disc, ray-traced per pixel. Spin 0 is Schwarzschild.
// Units inside: G = c = M = 1 (rs = 2). Every pixel is a null geodesic traced backwards from the camera in Boyer–Lindquist
// coordinates with Mino time λ (dλ = dτ/ρ²), where the radial and polar motions separate:
//   r'' = R'(r)/2,  θ'' = Θ'(θ)/2,  φ' = aP/Δ − a + b/sin²θ
// with b = L/E and q = Q/E² from the pixel direction. The camera is a zero-angular-momentum observer (static when a = 0),
// optionally boosted to ride along with a falling probe.
// The disc is opaque and lies in the equatorial plane from the ISCO outwards; its flux is Page–Thorne and its colour is
// shifted by g = E_camera / E_emitter, the exact redshift (Doppler, gravitational and frame dragging together).
// The sandbox adds debris gas (a polar texture in the equatorial plane, splatted by BlackHoleScene each frame), up to
// four solid bodies (tidally stretched ellipsoids) and guide rings; all of them are seen through the same lensed rays.
// Visuals step 2: a per-camera lens table (pass 1) stores what each direction's ray does, so the image (pass 0) shades
// several table samples per pixel instead of tracing one ray; pixels the table can't serve take the full trace.
Shader "FogOfWar/BlackHole"
{
    Properties { _Temp ("Peak disk temperature", Float) = 4800 }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
            #include "BlackHoleCore.cginc"

            // Pass 0, the image from the lens table: LENS_SPP table samples per pixel on a 4 × 2 grid (the frame's jitter
            // moves each one within its own cell). A pixel where any sample can't use the table is discarded and left to
            // pass 1; the stencil marks the pixels this pass served. Debug views 1 and 2 leave every pixel to pass 1.
            float4 fragLens(v2fFS i) : SV_Target
            {
                if (_LensOn < 0.5 || (_DebugFate > 0.5 && _DebugFate < 2.5)) discard;
                float px = 2.0 * _TanFov / _ScreenH;
                float2 jit = _DebugFate > 0.5 ? 0.0 : _Jitter;
                float2 pixNdc = 2.0 / _ScreenParams.xy;
                float3 col = 0;
                float spotLum = 0;
                bool fallback = false;
                [loop] for (int k = 0; k < LENS_SPP; k++)
                {
                    float2 o = (float2(k & 3, k >> 2) + 0.5) / float2(4.0, 2.0) - 0.5 + jit / pixNdc / float2(4.0, 2.0);
                    float3 n; float boost;
                    zamoPhoton(primaryRay(i.ndc + o * pixNdc, px), n, boost);
                    float b, q, EF; float4 s0;
                    photon(n, boost, 1.0, b, q, EF, s0);
                    float3 c = 0, dr = float3(0, 0, 1); float tr = 0, sl = 0, ok = 0;
                    if (!fallback) fallback = !lensSample(-n, b, q, EF, c, tr, sl, dr, ok);
                    // the sky outside the branch: its differentials need every pixel of the quad (same sample, one pixel over)
                    col += c + skyTerm(dr, tr * ok, EF, px);
                    spotLum += sl;
                }
                if (fallback) discard;
                col /= LENS_SPP;
                spotLum /= LENS_SPP;
                // debug view 3: green where the table served the pixel (pass 1 draws red where it did not)
                if (_DebugFate > 2.5) return float4(0, 1, 0, 1);
                // alpha carries the hot spot's share of this pixel's luminance, for its light curve (BHPost reads it)
                if (_HDROut > 0.5) return float4(col * _Exposure, spotLum * _Exposure);
                return float4(tonemap(col * _Exposure), 1.0);
            }

            // Pass 1, the full trace: one ray through the pixel's jittered centre, for every pixel pass 0 left.
            float4 fragTrace(v2fFS i) : SV_Target
            {
                if (_DebugFate > 2.5) return _LensOn > 0.5 ? float4(1, 0, 0, 1) : float4(0, 0, 1, 1);
                float px;
                float a = _Spin;
                float M = _Lens > 0.5 ? 1.0 : 0.0;
                float3 n; float boost;
                zamoPhoton(primaryRay(i.ndc + (_DebugFate > 0.5 ? 0.0 : _Jitter), px), n, boost);
                float b, q, EF; float4 s0;
                photon(n, boost, M, b, q, EF, s0);
                Ray R = march(b, q, EF, s0, a, M, false);
                float3 col = R.col;
                col += skyTerm(skyDir(R.s, R.ph, b, q, a, M), R.escaped && R.trans > 0.01 ? R.trans : 0.0, EF, px);
                // debug views for the review harness: 1 = fate (red captured, green escaped, blue out of steps,
                // white = opaque disc), 2 = first disc hit radius / ISCO and its brightness
                if (_DebugFate > 1.5) return float4(R.firstHit / (_Isco * 4.0), saturate(R.firstG), 0, 1);
                if (_DebugFate > 0.5)
                    return R.trans < 0.01 ? float4(1, 1, 1, 1) : float4(R.s.x < _RH + 1e-3 ? 1 : 0, R.escaped ? 1 : 0, R.steps >= _Steps - 1 ? 1 : 0, 1);
                if (_HDROut > 0.5) return float4(col * _Exposure, R.spotLum * _Exposure);
                return float4(tonemap(col * _Exposure), 1.0);
            }
        ENDCG

        // the stencil bit 128 marks the pixels the table served (the camera clears it every frame)
        Pass
        {
            Stencil { Ref 128 WriteMask 128 Comp Always Pass Replace }
            CGPROGRAM
            #pragma vertex vertFullscreen
            #pragma fragment fragLens
            #pragma target 4.5
            ENDCG
        }
        Pass
        {
            Stencil { Ref 128 ReadMask 128 Comp NotEqual }
            CGPROGRAM
            #pragma vertex vertFullscreen
            #pragma fragment fragTrace
            #pragma target 4.5
            ENDCG
        }
    }
}
