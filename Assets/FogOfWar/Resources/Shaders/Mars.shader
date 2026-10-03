// Mars surface temperature / terrain map. Elevation (km) arrives in uv2.x; geometry is displaced on the CPU.
// T = global mean − lapse rate × elevation + latitude term (MarsScene.cs). Liquid water needs local pressure above the
// triple point and T below the local boiling point.
Shader "FogOfWar/Mars"
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
            float _Tmean, _Lapse, _Mode, _Sea, _Tau, _HasTex, _LatDT, _P0;
            sampler2D _MarsTex;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv2 : TEXCOORD1; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float h : TEXCOORD1; float lat : TEXCOORD2; float3 op : TEXCOORD3; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.h = v.uv2.x;
                o.op = normalize(v.vertex.xyz);
                o.lat = asin(clamp(o.op.y, -1.0, 1.0));
                return o;
            }
            // thermometer ramp in °C
            float3 ramp(float T)
            {
                float3 c = float3(0.16, 0.10, 0.43);
                c = lerp(c, float3(0.18, 0.37, 0.82), smoothstep(-120.0, -70.0, T));
                c = lerp(c, float3(0.37, 0.78, 0.90), smoothstep(-70.0, -30.0, T));
                c = lerp(c, float3(0.91, 0.96, 0.94), smoothstep(-30.0, 0.0, T));
                c = lerp(c, float3(0.72, 0.88, 0.48), smoothstep(0.0, 15.0, T));
                c = lerp(c, float3(0.94, 0.75, 0.25), smoothstep(15.0, 35.0, T));
                c = lerp(c, float3(0.91, 0.38, 0.17), smoothstep(35.0, 60.0, T));
                c = lerp(c, float3(0.54, 0.06, 0.13), smoothstep(60.0, 140.0, T));
                return c;
            }
            float4 frag(v2f i) : SV_Target
            {
                float latTerm = _LatDT * (cos(i.lat) * cos(i.lat) - 2.0 / 3.0);
                float T = _Tmean - _Lapse * i.h + latTerm;
                float light = 0.25 + 0.85 * max(dot(normalize(i.n), normalize(_FOWSunDir)), 0.0);
                float3 col;
                if (_Mode < 0.5)
                {
                    col = ramp(T) * (0.35 + 0.65 * light);
                }
                else
                {
                    float3 rock = _HasTex > 0.5 ? texToDisplay(tex2DSphere(_MarsTex, i.op).rgb) * 1.1
                                                : lerp(float3(0.42, 0.18, 0.08), float3(0.78, 0.45, 0.25), smoothstep(-4.0, 10.0, i.h));
                    float p = _P0 * exp(-i.h / 11.1);
                    float boil = 1.0 / (1.0 / 373.15 - 8.314 / 43300.0 * log(max(p, 1e-3) / 1013.25)) - 273.15;
                    bool liquid = p > 6.117 && T > 0.0 && T < boil && i.h < _Sea;
                    bool ice = T <= 0.0 && i.h < _Sea + 2.0 && _Tau > 0.3;
                    if (liquid) rock = lerp(float3(0.05, 0.2, 0.4), float3(0.1, 0.35, 0.5), smoothstep(_Sea - 6.0, _Sea, i.h));
                    else if (ice || (abs(i.lat) > 1.35 && _HasTex < 0.5)) rock = float3(0.92, 0.94, 0.98);
                    else if (T > 0.0 && T < 40.0 && _Tau > 0.5) rock = lerp(rock, float3(0.25, 0.4, 0.15), 0.6);
                    col = rock * light;
                }
                return float4(displayToOutput(col), 1.0);
            }
            ENDCG
        }
    }
}
