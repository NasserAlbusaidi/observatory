// Impact sparks (SparksCompute.compute, docs/design-combat.md "The sparks (fx-2)"): one quad a piece, drawn indirect. A piece is a
// point source: its pixels sum to π·E / Ω_pixel times the exposure, where a white surface in full sunlight is 1 (ShipPBR's
// scale). It is a streak from where it was a shutter ago, with the same total light; a Gaussian across it, never under
// 0.7 px, and wider where its peak would pass 1. Additive, behind the hull where the hull is nearer.
Shader "FogOfWar/Sparks"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest LEqual Blend One One
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"
            #define UNITY_INDIRECT_DRAW_ARGS IndirectDrawArgs
            #include "UnityIndirect.cginc"
            #include "FOWCommon.cginc"

            struct Spark { float3 head; float3 tail; float3 col; };
            StructuredBuffer<Spark> _Sparks;

            struct v2f
            {
                float4 pos : SV_POSITION;
                noperspective float4 prof : TEXCOORD0;  // along the streak from its tail (px), across it (px), its length, σ
                nointerpolation float3 col : TEXCOORD1; // RGB where the profile is 1
            };

            static const float2 Corner[6] = { float2(-1, -1), float2(1, -1), float2(1, 1), float2(-1, -1), float2(1, 1), float2(-1, 1) };

            v2f vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                InitIndirectDrawArgs(0);
                Spark sp = _Sparks[GetIndirectInstanceID(iid)];
                float2 c = Corner[GetIndirectVertexID(vid) % 6];
                v2f o;
                o.prof = 0;
                o.col = 0;
                float4 ch = mul(UNITY_MATRIX_VP, float4(sp.head, 1.0));
                float4 ct = mul(UNITY_MATRIX_VP, float4(sp.tail, 1.0));
                if (ch.w <= 1e-6) { o.pos = float4(2, 2, 2, 1); return o; }   // behind the eye: outside the clip volume
                if (ct.w <= 1e-6) ct = ch;
                float2 halfPx = _ScreenParams.xy * 0.5;
                float2 d = ch.xy / ch.w * halfPx - ct.xy / ct.w * halfPx;
                float L = length(d);
                float2 u = L > 1e-3 ? d / L : float2(1, 0), n = float2(-u.y, u.x);
                // one pixel's solid angle: 2·tan(fov/2)/H, squared
                float px = 2.0 / (UNITY_MATRIX_P[1][1] * _ScreenParams.y);
                float scale = UNITY_PI * _FOWExposure / (px * px);
                float S = scale * lumaRGB(sp.col);
                // σ for a peak of 1 at this total and length: 2π·σ² + √(2π)·L·σ = S
                float r2pi = 2.5066283;
                float sigma = (-r2pi * L + sqrt(6.2831853 * L * L + 25.132741 * S)) / 12.566371;
                sigma = clamp(sigma, 0.7, 6.0);
                o.col = sp.col * scale / (sigma * r2pi * (L + sigma * r2pi));
                float ext = 3.0 * sigma;
                float4 p = c.x < 0 ? ct : ch;
                p.xy += (u * (c.x * ext) + n * (c.y * ext)) / halfPx * p.w;
                o.pos = p;
                o.prof = float4(c.x < 0 ? -ext : L + ext, c.y * ext, L, sigma);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float s = i.prof.x, L = i.prof.z, sigma = i.prof.w;
                float a = s < 0 ? s : max(s - L, 0);
                float g = exp(-(a * a + i.prof.y * i.prof.y) / (2.0 * sigma * sigma));
                return float4(linearToOutput(i.col * g), 0);
            }
            ENDCG
        }
    }
}
