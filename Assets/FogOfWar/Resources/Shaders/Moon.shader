// Moon: LRO colour map when the art pack is bound (_HasTex = 1), procedural maria otherwise. Lit by the sun only.
Shader "FogOfWar/Moon"
{
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "FOWCommon.cginc"
            float3 _FOWSunDir;
            float _HasTex;
            sampler2D _MoonTex;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 op : TEXCOORD1; float3 wp : TEXCOORD2; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.n = UnityObjectToWorldNormal(v.normal); o.op = normalize(v.vertex.xyz); o.wp = mul(unity_ObjectToWorld, v.vertex).xyz; return o; }
            float4 frag(v2f i) : SV_Target
            {
                float3 c;
                if (_HasTex > 0.5) c = texToDisplay(tex2DSphere(_MoonTex, i.op).rgb) * 1.15;
                else
                {
                    float m = fbm(i.op * 2.2);
                    float cr = fbm(i.op * 9.0);
                    c = lerp(0.62, 0.32, smoothstep(0.45, 0.65, m)) * (0.85 + 0.3 * cr);
                }
                float diff = regolith(normalize(i.n), normalize(_FOWSunDir), normalize(_WorldSpaceCameraPos - i.wp));
                return float4(displayToOutput(c * (0.02 + diff)), 1.0);
            }
            ENDCG
        }
    }
}
