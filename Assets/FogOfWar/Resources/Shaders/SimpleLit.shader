// Pipeline-independent lit surface (works in Built-in and URP): Lambert + Blinn spec + rim, one directional sun.
Shader "FogOfWar/SimpleLit"
{
    Properties
    {
        _Color ("Color", Color) = (0.8,0.8,0.85,1)
        _Emission ("Emission", Color) = (0,0,0,1)
        _Spec ("Specular", Float) = 0.4
        _Rim ("Rim", Color) = (0.3,0.5,1,1)
        _Alpha ("Alpha", Float) = 1
        _SunIrr ("Sunlight, 1 AU = 1", Float) = 1
        _HasLodNormal ("Has a baked normal map", Float) = 0
        _LodNormal ("Baked normal map (tangent space, BC5 RG)", 2D) = "bump" {}
    }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "FOWCommon.cginc"
            float4 _Color, _Emission, _Rim;
            float _Spec, _Alpha, _SunIrr, _HasLodNormal;
            sampler2D _LodNormal;
            float3 _FOWSunDir;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 tangent : TANGENT; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; float2 uv : TEXCOORD2; float4 t : TEXCOORD3; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = v.uv;
                o.t = float4(UnityObjectToWorldDir(v.tangent.xyz), v.tangent.w * unity_WorldTransformParams.w);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float3 n = lodNormal(_LodNormal, _HasLodNormal, normalize(i.n), i.t, i.uv);
                float3 l = normalize(_FOWSunDir);
                float3 vdir = normalize(_WorldSpaceCameraPos - i.wp);
                float diff = max(dot(n, l), 0.0);
                float spec = pow(max(dot(n, normalize(l + vdir)), 0.0), 48.0) * _Spec;
                float rim = pow(1.0 - max(dot(n, vdir), 0.0), 3.0);
                float3 c = _Color.rgb * (0.08 + diff) + spec + _Rim.rgb * rim * 0.35;
                return float4(displayToOutput(c * fowDisplayGain(fowSunlit(_SunIrr)) + _Emission.rgb * fowDisplayGain(_FOWExposure)), _Alpha);
            }
            ENDCG
        }
    }
}
