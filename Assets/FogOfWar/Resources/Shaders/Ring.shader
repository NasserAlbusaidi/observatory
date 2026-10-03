// Planetary rings from the art pack's radial profile (Rings/<Planet>_Rings.png): u = (r − inner) / (outer − inner),
// rows 0–1 the lit face, rows 2–3 the unlit face (forward scatter), rgb in display values, alpha = (log10 τ + 9) / 10
// with τ the normal optical depth. Opacity along the view ray is 1 − exp(−τ/|μ|). The planet shadows the rings.
// Mesh: an annulus in object XZ with outer radius 1 and inner radius _Inner.
Shader "FogOfWar/Ring"
{
    Properties
    {
        _MainTex ("Profile", 2D) = "black" {}
        _Inner ("Inner radius / outer", Float) = 0.5
        _TauGain ("Optical depth gain", Float) = 1
        _SunIrr ("Sunlight, 1 AU = 1", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "FOWCommon.cginc"
            sampler2D _MainTex;
            float _Inner, _TauGain, _PlanetR;
            float3 _SunDir, _PlanetPos;
            float _SunIrr;
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float2 op : TEXCOORD0; float3 wp : TEXCOORD1; float3 n : TEXCOORD2; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.op = v.vertex.xz; // radius per fragment, not interpolated per vertex
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.n = normalize(mul((float3x3)unity_ObjectToWorld, float3(0, 1, 0)));
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float u = (length(i.op) - _Inner) / (1.0 - _Inner);
                if (u < 0.0 || u > 1.0) discard;
                float3 n = normalize(i.n);
                float3 vdir = normalize(_WorldSpaceCameraPos - i.wp);
                float3 l = normalize(_SunDir);
                bool litSide = dot(n, l) * dot(n, vdir) > 0.0;
                float4 t = tex2Dlod(_MainTex, float4(u, litSide ? 0.25 : 0.75, 0, 0));
                if (t.a <= 0.0) discard;
                float tau = pow(10.0, t.a * 10.0 - 9.0) * _TauGain;
                float mu = max(abs(dot(n, vdir)), 0.02);
                float opacity = 1.0 - exp(-tau / mu);
                // sunlight reaching this patch: planet shadow, and a low sun grazing the ring plane
                float3 oc = i.wp - _PlanetPos;
                float b = dot(oc, l), c = dot(oc, oc) - _PlanetR * _PlanetR;
                float shadow = (b < 0.0 && b * b - c > 0.0) ? 0.04 : 1.0;
                float mu0 = abs(dot(n, l));
                float light = shadow * saturate(0.15 + mu0 * 3.0);
                float3 col = t.rgb * light;
                return float4(displayToOutput(col) * fowSunlit(_SunIrr) * opacity, opacity);
            }
            ENDCG
        }
    }
}
