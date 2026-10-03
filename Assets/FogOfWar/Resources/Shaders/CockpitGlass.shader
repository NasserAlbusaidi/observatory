// The cover glass of the hull-camera display. The world camera's frame shows through it; this adds what a real
// panel adds: a raised black level, backlight falloff at the edges, and a faint reflection of the overhead lamp.
// Premultiplied: rgb is added, alpha darkens what is behind.
Shader "FogOfWar/CockpitGlass"
{
    Properties { }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _CkLampPos[8];
            float4 _CkLampCol[8];
            float _CkExposure;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; float3 n : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.n = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 e = min(i.uv, 1.0 - i.uv) * float2(1.9, 1.3);   // metres-ish from the edge
                float falloff = 1.0 - smoothstep(0.0, 0.12, min(e.x, e.y));
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float3 R = reflect(-V, normalize(i.n));
                // the overhead lamp (slot 0) mirrored in the glass: a soft streak
                float3 d = _CkLampPos[0].xyz - i.wp;
                float g = pow(saturate(dot(R, normalize(d))), 400.0);
                float3 add = _CkLampCol[0].rgb * g * 0.02 / max(dot(d, d), 0.1) + 0.0025;
                return float4(add * _CkExposure, falloff * 0.35);
            }
            ENDCG
        }
    }
}
