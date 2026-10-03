// Distant asteroids as GPU-instanced camera-facing dots. Per-instance matrix: translation = render position,
// uniform scale = dot radius in render units. Per-instance colour (rgb display, a = intensity). Additive.
Shader "FogOfWar/BeltDot"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off Blend One One
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "FOWCommon.cginc"
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Col)
            UNITY_INSTANCING_BUFFER_END(Props)
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : TEXCOORD1; };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 c = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                float s = length(unity_ObjectToWorld._m00_m10_m20);
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                o.pos = mul(UNITY_MATRIX_VP, float4(c + (right * v.vertex.x + up * v.vertex.y) * s, 1.0));
                o.uv = v.vertex.xy;
                o.col = UNITY_ACCESS_INSTANCED_PROP(Props, _Col);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float r2 = dot(i.uv, i.uv);
                if (r2 > 1.0) discard;
                float a = exp(-r2 * 3.0) * i.col.a;
                return float4(displayToOutput(i.col.rgb) * a, 1.0);
            }
            ENDCG
        }
    }
}
