// Generic world for the open system. Object space: +Y north pole, +X prime meridian (SkyUV / tex2DSphere layout).
// _HasTex = 1 samples an equirectangular colour map; otherwise _Kind picks a procedural look from _ColA/_ColB:
// 0 rocky, 1 cloud deck (Venus), 2 banded gas giant, 3 ice giant. _Atmo adds a lit limb glow (alpha = strength).
Shader "FogOfWar/Planet"
{
    Properties
    {
        _ColA ("Colour A", Color) = (0.5,0.5,0.5,1)
        _ColB ("Colour B", Color) = (0.7,0.7,0.7,1)
        _Atmo ("Atmosphere", Color) = (0,0,0,0)
        _Kind ("Kind", Float) = 0
        _Seed ("Seed", Float) = 0
        _HasTex ("Has texture", Float) = 0
        _Gain ("Map gain", Float) = 1
        _Haze ("Haze cover", Float) = 0
        _HazeCol ("Haze colour", Color) = (0.8,0.6,0.3,1)
        _MainTex ("Colour map", 2D) = "white" {}
    }
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
            float4 _ColA, _ColB, _Atmo;
            float _Kind, _Seed, _HasTex, _Gain, _Haze;
            float4 _HazeCol;
            float3 _SunDir;
            sampler2D _MainTex;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 op : TEXCOORD1; float3 wp : TEXCOORD2; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.op = v.vertex.xyz;
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float3 procedural(float3 p)
            {
                float3 s = float3(_Seed * 1.7, _Seed * 3.1, _Seed * 0.7);
                float lat = p.y;
                if (_Kind < 0.5)
                {
                    float n = fbm(p * 3.0 + s) * 0.65 + fbm(p * 11.0 + s) * 0.35;
                    float crater = smoothstep(0.62, 0.7, fbm(p * 22.0 - s));
                    return lerp(_ColA.rgb, _ColB.rgb, saturate(n * 1.4 - 0.2)) * (1.0 - crater * 0.25);
                }
                if (_Kind < 1.5)
                {
                    float swirl = fbm(float3(p.x * 2.0, lat * 6.0, p.z * 2.0) + s);
                    return lerp(_ColA.rgb, _ColB.rgb, saturate(0.5 + 0.5 * sin(lat * 9.0 + swirl * 5.0)));
                }
                if (_Kind < 2.5)
                {
                    float turb = fbm(float3(p.x * 3.0, lat * 2.0, p.z * 3.0) + s);
                    float bands = sin(lat * 26.0 + turb * 3.5) * 0.5 + 0.5;
                    float fine = sin(lat * 70.0 + turb * 8.0) * 0.5 + 0.5;
                    float3 c = lerp(_ColA.rgb, _ColB.rgb, saturate(bands * 0.8 + fine * 0.2));
                    return lerp(c, _ColA.rgb * 0.8, smoothstep(0.8, 0.98, abs(lat)));
                }
                float b = sin(lat * 12.0 + fbm(p * 2.0 + s) * 1.5) * 0.5 + 0.5;
                return lerp(_ColA.rgb, _ColB.rgb, b * 0.35);
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 p = normalize(i.op);
                float3 n = normalize(i.n);
                float3 l = normalize(_SunDir);
                float3 vdir = normalize(_WorldSpaceCameraPos - i.wp);
                float3 albedo = procedural(p);
                if (_HasTex > 0.5)
                {
                    float4 t = tex2DSphere(_MainTex, p);
                    // a cloud deck map (Venus) is single-channel opacity: tint it between the two deck colours
                    albedo = (_Kind > 0.5 && _Kind < 1.5) ? lerp(_ColA.rgb, _ColB.rgb, t.r) : texToDisplay(t.rgb) * _Gain;
                }
                albedo = lerp(albedo, _HazeCol.rgb, _Haze);
                float ndl = dot(n, l);
                float diff = max(ndl, 0.0);
                // gas and cloud decks scatter light past the terminator
                float soft = _Kind > 0.5 ? smoothstep(-0.15, 0.25, ndl) * 0.9 + diff * 0.1 : regolith(n, l, vdir);
                float rim = pow(1.0 - saturate(dot(n, vdir)), 3.0);
                float3 col = albedo * (0.012 + soft) + _Atmo.rgb * rim * _Atmo.a * saturate(ndl + 0.35);
                return float4(displayToOutput(col), 1.0);
            }
            ENDCG
        }
    }
}
