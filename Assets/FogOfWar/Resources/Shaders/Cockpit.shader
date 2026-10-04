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

            // the two coordinates across a face (metres), picked by its dominant normal axis
            float2 faceUV(float3 p, float3 n)
            {
                float3 an = abs(n);
                return an.x > an.y && an.x > an.z ? p.yz : an.y > an.z ? p.xz : p.xy;
            }

            // antialiased lines every `pitch` metres on both face axes, `width` metres wide
            float grid(float2 q, float pitch, float width)
            {
                float2 g = abs(frac(q / pitch + 0.5) - 0.5) * pitch;   // metres to the nearest line
                float2 w = fwidth(q) + width;
                float2 ln = 1.0 - smoothstep(w * 0.5, w * 1.5, g);
                return max(ln.x, ln.y);
            }

            // Materials (vertex colour alpha × 3): 0 painted panel, 1 bare metal, 2 padding or rubber, 3 indicator glass.
            float4 frag(v2f i) : SV_Target
            {
                int mat = (int)round(i.col.a * 3.0);
                if (mat == 3) return float4(displayToOutput(i.col.rgb * _CkLedCol.rgb * _CkExposure), 1);
                float3 n = normalize(i.n);
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                // paint: satin; metal: tight bright highlights, darker diffuse; padding: no highlight, wide wrap
                float shine = mat == 1 ? 90.0 : 32.0;
                float ks = mat == 1 ? 0.55 : mat == 2 ? 0.0 : 0.12;
                float wrap = mat == 2 ? 0.5 : 0.2;
                float3 diff = 0, spec = 0;
                [unroll] for (int k = 0; k < LAMPS; k++)
                {
                    float3 d = _CkLampPos[k].xyz - i.wp;
                    float r2 = max(dot(d, d), 0.06);   // lamps are ~25 cm across: no hot spot on nearby panels
                    float3 l = d * rsqrt(r2);
                    // lamps are strips and panels, not points: a little wrap softens the terminator
                    float w = saturate((dot(n, l) + wrap) / (1.0 + wrap));
                    diff += _CkLampCol[k].rgb * w / r2;
                    spec += _CkLampCol[k].rgb * pow(saturate(dot(n, normalize(l + V))), shine) * step(0.0, dot(n, l)) / r2;
                }
                float3 albedo = i.col.rgb;
                float2 q = faceUV(i.op, n);
                float3 c;
                if (mat == 1)
                {
                    // brushed: fine streaks along one face axis; the highlight takes the metal's own tint
                    albedo *= 0.9 + 0.2 * vnoise(float3(q.x * 4.0, q.y * 260.0, 0.0));
                    c = albedo * 0.55 * diff + spec * ks * (0.4 + 1.6 * albedo);
                }
                else if (mat == 2)
                {
                    // quilted padding: diagonal stitch lines every 7 cm, a fine weave, and a soft sheen at grazing angles
                    float2 dq = float2(q.x + q.y, q.x - q.y) * 0.7071;
                    albedo *= lerp(1.0, 0.55, grid(dq, 0.07, 0.0012));
                    albedo *= 0.9 + 0.12 * vnoise(i.op * 380.0);
                    float sheen = pow(1.0 - saturate(dot(n, V)), 3.0) * 0.35;
                    c = albedo * diff * (1.0 + sheen);
                }
                else
                {
                    // painted panels: seams every 40 cm, and a little low-frequency wear so large panels aren't flat
                    albedo *= lerp(1.0, 0.65, grid(q, 0.4, 0.0015));
                    albedo *= 0.85 + 0.3 * vnoise(i.op * 7.0);
                    c = albedo * diff + spec * ks;
                }
                return float4(displayToOutput(c * _CkExposure), 1);
            }
            ENDCG
        }
    }
}
