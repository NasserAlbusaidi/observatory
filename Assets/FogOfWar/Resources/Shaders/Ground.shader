// Night-side Earth for the meteor scene: real city lights over a moonlit Blue Marble when the art pack is bound,
// procedural dark land otherwise, plus a faint 2° lat/long grid. Object space uses the art pack's planet frame.
Shader "FogOfWar/Ground"
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
            float _HasTex;
            sampler2D _DayTex, _NightTex;
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 op : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.op = v.vertex.xyz; return o; }
            float4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.op);
                float lat = asin(clamp(d.y, -1.0, 1.0)) * 57.2958 / 2.0;
                float lon = atan2(d.z, d.x) * 57.2958 / 2.0;
                float2 gp = float2(lat, lon);
                float2 gd = abs(frac(gp) - 0.5);
                float2 fw = fwidth(gp);
                float gl = 1.0 - min(smoothstep(0.0, fw.x * 1.5, gd.x), smoothstep(0.0, fw.y * 1.5, gd.y));
                float3 c;
                if (_HasTex > 0.5)
                {
                    float3 day = texToDisplay(tex2DSphere(_DayTex, d).rgb);
                    float3 lights = texToDisplay(tex2DSphere(_NightTex, d).rgb);
                    c = day * float3(0.07, 0.08, 0.12) + lights * float3(1.0, 0.78, 0.5) * 0.85;
                }
                else
                {
                    float land = fbm(d * 40.0);
                    c = lerp(float3(0.015, 0.02, 0.045), float3(0.05, 0.04, 0.035), smoothstep(0.45, 0.6, land));
                }
                c += float3(0.08, 0.1, 0.18) * gl * 0.35;
                return float4(displayToOutput(c), 1.0);
            }
            ENDCG
        }
    }
}
