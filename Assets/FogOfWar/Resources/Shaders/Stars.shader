// Billboarded point stars at infinity. With _Beta > 0 each star is relativistically aberrated and Doppler-shifted
// for an observer moving along _Fwd: the sky crowds forward, turns blue ahead and fades behind.
// Mesh layout: 4 vertices per star; normal = direction, uv = quad corner (-1..1), uv2 = (temperature K, brightness).
Shader "FogOfWar/Stars"
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
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "FOWCommon.cginc"

            float _Beta, _Gamma, _SizePx, _Gain;
            float3 _Fwd;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float2 corner : TEXCOORD0; float3 col : TEXCOORD1; float a : TEXCOORD2; };

            float visB(float T) { return 1.0 / (exp(min(26400.0 / max(T, 1.0), 80.0)) - 1.0); }

            v2f vert(appdata v)
            {
                v2f o;
                float3 d = normalize(v.normal);
                float ct = dot(d, _Fwd);
                float ctp = (ct + _Beta) / (1.0 + _Beta * ct);
                float3 perp = d - ct * _Fwd;
                float pl = length(perp);
                perp = pl > 1e-6 ? perp / pl : float3(1, 0, 0);
                float3 d2 = ctp * _Fwd + sqrt(max(0.0, 1.0 - ctp * ctp)) * perp;
                float D = _Gamma * (1.0 + _Beta * ct);
                float T = v.uv2.x * D;
                // point sources: aberration shrinks each star's solid angle by D², so flux carries a 1/D² (McKinley & Doherty 1979)
                float b = v.uv2.y * visB(T) / visB(v.uv2.x) / (D * D) * _Gain;
                o.col = blackbody(T);
                o.a = saturate(b);
                float size = clamp(1.2 + 1.6 * log(1.0 + b * 4.0), 0.0, 14.0) * _SizePx;
                float3 wp = _WorldSpaceCameraPos + d2 * (_ProjectionParams.z * 0.8);
                float4 clip = mul(UNITY_MATRIX_VP, float4(wp, 1.0));
                clip.xy += v.uv * size / _ScreenParams.xy * clip.w;
                o.pos = clip;
                o.corner = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float r2 = dot(i.corner, i.corner);
                if (r2 > 1.0) discard;
                float a = exp(-r2 * 4.0) * i.a;
                return float4(displayToOutput(i.col) * a, 1.0);
            }
            ENDCG
        }
    }
}
