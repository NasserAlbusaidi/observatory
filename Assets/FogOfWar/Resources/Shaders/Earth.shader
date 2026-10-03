// Earth: NASA Blue Marble day map, Earth-at-night city lights, cloud layer, ocean glint and relief normals when the
// art pack's textures are bound (_HasTex = 1); procedural fbm continents otherwise.
Shader "FogOfWar/Earth"
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
            float _SimTime, _HasTex;
            sampler2D _DayTex, _NightTex, _CloudTex, _OceanTex, _NormalTex;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 op : TEXCOORD1; float3 wp : TEXCOORD2; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.op = normalize(v.vertex.xyz);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            float4 fragTex(v2f i)
            {
                float3 p = normalize(i.op);
                float3 l = normalize(_FOWSunDir);
                float3 vdir = normalize(_WorldSpaceCameraPos - i.wp);
                float3 nObj = sphereNormal(_NormalTex, p);
                float3 n = normalize(mul((float3x3)unity_ObjectToWorld, nObj));
                float3 ng = normalize(i.n);
                float3 day = texToDisplay(tex2DSphere(_DayTex, p).rgb);
                float3 lights = texToDisplay(tex2DSphere(_NightTex, p).rgb);
                float ocean = tex2DSphere(_OceanTex, p).r;
                // clouds drift slowly eastward
                float3 pc = float3(p.x * cos(_SimTime * 0.004) - p.z * sin(_SimTime * 0.004), p.y, p.x * sin(_SimTime * 0.004) + p.z * cos(_SimTime * 0.004));
                float cloud = tex2DSphere(_CloudTex, pc).r;
                float ndl = dot(ng, l);
                float diff = max(dot(n, l), 0.0) * smoothstep(-0.05, 0.1, ndl);
                float spec = pow(max(dot(ng, normalize(l + vdir)), 0.0), 60.0) * ocean * (1.0 - cloud) * 0.6 * step(0.0, ndl);
                float3 c = lerp(day, 1.0, cloud * 0.85);
                float night = smoothstep(0.08, -0.12, ndl);
                float rim = pow(1.0 - max(dot(ng, vdir), 0.0), 3.0);
                float3 col = c * (0.015 + diff) + spec * float3(1.0, 0.9, 0.75)
                           + lights * float3(1.0, 0.8, 0.55) * night * (1.0 - cloud * 0.7) * 0.9
                           + float3(0.3, 0.55, 1.0) * rim * 0.6 * (0.2 + max(ndl, 0.0));
                return float4(displayToOutput(col), 1.0);
            }

            float4 frag(v2f i) : SV_Target
            {
                if (_HasTex > 0.5) return fragTex(i);
                float3 n = normalize(i.n);
                float3 p = i.op;
                float3 l = normalize(_FOWSunDir);
                float land = fbm(p * 1.6 + 4.0);
                float ice = smoothstep(0.82, 0.9, abs(p.y));
                float3 ocean = float3(0.02, 0.09, 0.22);
                float3 ground = lerp(float3(0.12, 0.22, 0.08), float3(0.42, 0.33, 0.2), smoothstep(0.55, 0.7, fbm(p * 5.0)));
                float3 c = land > 0.53 ? ground : ocean;
                c = lerp(c, 0.9, ice);
                float clouds = smoothstep(0.55, 0.75, fbm(p * 3.0 + float3(_SimTime * 0.02, 0, 0)));
                c = lerp(c, 0.95, clouds * 0.7);
                float ndl = dot(n, l);
                float diff = max(ndl, 0.0);
                float3 night = float3(1.0, 0.7, 0.35) * step(0.53, land) * step(0.5, fbm(p * 18.0)) * 0.12 * smoothstep(0.1, -0.15, ndl);
                float3 vdir = normalize(_WorldSpaceCameraPos - i.wp);
                float rim = pow(1.0 - max(dot(n, vdir), 0.0), 3.0);
                float3 col = c * (0.03 + diff) + night + float3(0.3, 0.55, 1.0) * rim * 0.6 * (0.2 + diff);
                return float4(displayToOutput(col), 1.0);
            }
            ENDCG
        }
    }
}
