// The pilot's cabin (open world, cockpit view). Lit only by the cabin's own lamps: it is windowless, so no sunlight
// reaches it, and it keeps a fixed exposure of its own (_CkExposure) instead of the world's.
// Vertex colour: rgb albedo in display values; alpha 1 marks self-lit indicator glass (LEDs, lamp strips).
Shader "FogOfWar/Cockpit"
{
    Properties { }
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

            #define LAMPS 8
            float4 _CkLampPos[LAMPS];   // world position (xyz)
            float4 _CkLampCol[LAMPS];   // rgb: intensity, display units at 1 m; a: unused
            float4 _CkLedCol;           // rgb: gain on indicator glass (dims and reddens with the cabin mode)
            float _CkExposure;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; float3 op : TEXCOORD2; float4 col : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.op = v.vertex.xyz;
                o.col = v.color;
                return o;
            }

            // panel seams every 40 cm on the two axes across the face, about 3 mm wide, antialiased
            float seams(float3 p, float3 n)
            {
                float3 an = abs(n);
                float2 q = an.x > an.y && an.x > an.z ? p.yz : an.y > an.z ? p.xz : p.xy;
                float2 g = abs(frac(q / 0.4 + 0.5) - 0.5) * 0.4;   // metres to the nearest seam
                float2 w = fwidth(q) + 0.0015;
                float2 ln = 1.0 - smoothstep(w * 0.5, w * 1.5, g);
                return max(ln.x, ln.y);
            }

            float4 frag(v2f i) : SV_Target
            {
                if (i.col.a > 0.5) return float4(displayToOutput(i.col.rgb * _CkLedCol.rgb * _CkExposure), 1);
                float3 n = normalize(i.n);
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float3 diff = 0, spec = 0;
                [unroll] for (int k = 0; k < LAMPS; k++)
                {
                    float3 d = _CkLampPos[k].xyz - i.wp;
                    float r2 = max(dot(d, d), 0.06);   // lamps are ~25 cm across: no hot spot on nearby panels
                    float3 l = d * rsqrt(r2);
                    // lamps are strips and panels, not points: a little wrap softens the terminator
                    float w = saturate((dot(n, l) + 0.2) / 1.2);
                    diff += _CkLampCol[k].rgb * w / r2;
                    spec += _CkLampCol[k].rgb * pow(saturate(dot(n, normalize(l + V))), 32.0) * step(0.0, dot(n, l)) / r2;
                }
                float3 albedo = i.col.rgb;
                // painted metal: seams, and a little low-frequency wear so large panels aren't flat
                albedo *= lerp(1.0, 0.65, seams(i.op, n));
                albedo *= 0.85 + 0.3 * vnoise(i.op * 7.0);
                float3 c = albedo * diff + spec * 0.12;
                return float4(displayToOutput(c * _CkExposure), 1);
            }
            ENDCG
        }
    }
}
