// Player ship hull: metal/roughness PBR (GGX, Smith, Schlick) from the baked atlas in Models/PlayerShip, lit only by
// what is really there (ShipLighting.cs): the Sun as one hard directional light with inverse-square falloff, cut by
// eclipses and by the ship's own shadow map; planetshine from the brightest nearby body; the drive plume while it
// burns. No ambient term, so a side facing away from every source is black. Output is scaled by _FOWExposure.
Shader "FogOfWar/ShipPBR"
{
    Properties
    {
        _AlbedoTex ("Albedo (sRGB)", 2D) = "white" {}
        _NormalTex ("Object-space normal (Blender axes)", 2D) = "bump" {}
        _MaskTex ("Mask: R metallic, G occlusion, A smoothness", 2D) = "white" {}
        _EmissionTex ("Emission (sRGB)", 2D) = "black" {}
        _EmissionGain ("Emission gain", Float) = 3
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
            sampler2D _AlbedoTex, _NormalTex, _MaskTex, _EmissionTex;
            float _EmissionGain;
            float4x4 _BlenderToMesh;            // Blender object axes -> this mesh's object axes (FBX axis conversion)
            float3 _FOWSunDir;
            float _FOWSunIrr, _FOWEclipse;
            float4 _FOWShineDir, _FOWShineCol;  // shine: w = angular radius of the body (rad)
            float4 _FOWPlumePos, _FOWPlumeCol;  // plume: w = falloff radius (render units)
            sampler2D _ShipShadowTex;
            float4x4 _ShipShadowMatrix;         // world -> (u, v, depth) in 0..1, depth 0 at the Sun side
            float4 _ShipShadowParams;           // x texel size, y depth bias, z normal offset (world), w enabled

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; float3 ng : TEXCOORD2; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.ng = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            float D_GGX(float nh, float a) { float a2 = a * a; float d = nh * nh * (a2 - 1.0) + 1.0; return a2 / (UNITY_PI * d * d + 1e-7); }
            float V_Smith(float nl, float nv, float a)
            {
                float k = a * 0.5;
                return 0.25 / ((nl * (1.0 - k) + k) * (nv * (1.0 - k) + k) + 1e-5);
            }
            float3 F_Schlick(float3 f0, float vh) { return f0 + (1.0 - f0) * pow(1.0 - vh, 5.0); }

            // radiance leaving toward the eye per unit irradiance arriving along l (a white Lambert facing l gives 1)
            float3 brdf(float3 n, float3 v, float3 l, float3 albedo, float metal, float rough)
            {
                float nl = saturate(dot(n, l));
                if (nl <= 0.0) return 0;
                float nv = max(dot(n, v), 1e-3);
                float3 h = normalize(l + v);
                float nh = saturate(dot(n, h)), vh = saturate(dot(v, h));
                float a = max(rough * rough, 0.002);
                float3 f0 = lerp(0.04, albedo, metal);
                float3 F = F_Schlick(f0, vh);
                float3 spec = D_GGX(nh, a) * V_Smith(nl, nv, a) * F;
                float3 kd = (1.0 - F) * (1.0 - metal);
                return (kd * albedo + UNITY_PI * spec) * nl;
            }

            float shadow(float3 wp, float3 ng)
            {
                if (_ShipShadowParams.w < 0.5) return 1.0;
                float3 sp = mul(_ShipShadowMatrix, float4(wp + ng * _ShipShadowParams.z, 1.0)).xyz;
                if (any(sp.xy < 0.0) || any(sp.xy > 1.0)) return 1.0;
                float lit = 0.0;
                [unroll] for (int y = -1; y <= 1; y++)
                    [unroll] for (int x = -1; x <= 1; x++)
                    {
                        float d = tex2Dlod(_ShipShadowTex, float4(sp.xy + float2(x, y) * _ShipShadowParams.x, 0, 0)).r;
                        lit += step(sp.z - _ShipShadowParams.y, d);
                    }
                return lit / 9.0;
            }

            // keeps everything up to 0.8 exact (so the hull matches the planets' brightness), rolls off above it
            float3 shoulder(float3 c) { return c < 0.8 ? c : 0.8 + 0.2 * (1.0 - exp(-(c - 0.8) / 0.2)); }

            float4 frag(v2f i) : SV_Target
            {
                float3 albedo = tex2D(_AlbedoTex, i.uv).rgb;
                float4 mask = tex2D(_MaskTex, i.uv);
                float metal = mask.r, ao = mask.g, rough = 1.0 - mask.a;
                float3 nb = tex2D(_NormalTex, i.uv).rgb * 2.0 - 1.0;
                float3 n = normalize(mul((float3x3)unity_ObjectToWorld, mul((float3x3)_BlenderToMesh, nb)));
                float3 ng = normalize(i.ng);
                float3 v = normalize(_WorldSpaceCameraPos - i.wp);
                if (dot(n, v) < 0.0 && dot(ng, v) > 0.0) n = normalize(n - v * dot(n, v) * 1.02); // keep map normals facing the eye

                float3 l = normalize(_FOWSunDir);
                float sun = _FOWSunIrr * (1.0 - _FOWEclipse) * shadow(i.wp, ng) * step(0.0, dot(ng, l) + 0.05);
                float3 c = brdf(n, v, l, albedo, metal, rough) * sun;

                // planetshine: a body many degrees across, so widen the lobe by its angular radius
                float3 ls = normalize(_FOWShineDir.xyz);
                float wrap = saturate(_FOWShineDir.w);
                float3 nd = normalize(n + ls * wrap * 0.5);
                c += brdf(nd, v, ls, albedo, metal, max(rough, wrap)) * _FOWShineCol.rgb * ao;

                // drive plume, only while it burns
                if (_FOWPlumeCol.a > 0.0)
                {
                    float3 dp = _FOWPlumePos.xyz - i.wp;
                    float d2 = dot(dp, dp), r2 = _FOWPlumePos.w * _FOWPlumePos.w;
                    c += brdf(n, v, dp * rsqrt(d2 + 1e-12), albedo, metal, rough) * _FOWPlumeCol.rgb * (r2 / (d2 + r2)) * ao;
                }

                c *= _FOWExposure;
                c += tex2D(_EmissionTex, i.uv).rgb * _EmissionGain * _FOWExposure;
                c = shoulder(c);
#ifdef UNITY_COLORSPACE_GAMMA
                c = pow(c, 1.0 / 2.2);
#endif
                return float4(c, 1.0);
            }
            ENDCG
        }
    }
}
