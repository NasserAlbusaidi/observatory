// Close-up asteroids: GPU-instanced rock meshes with regolith (Lommel–Seeliger) shading. Per-instance colour; the sun
// direction is shared (_RockSunDir), since every rock near the camera sees the Sun from the same direction.
Shader "FogOfWar/Rock"
{
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "FOWCommon.cginc"
            float3 _RockSunDir;
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Col)
            UNITY_INSTANCING_BUFFER_END(Props)
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; float4 col : TEXCOORD2; };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.col = UNITY_ACCESS_INSTANCED_PROP(Props, _Col);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.n), l = normalize(_RockSunDir), vdir = normalize(_WorldSpaceCameraPos - i.wp);
                return float4(displayToOutput(i.col.rgb * (0.01 + regolith(n, l, vdir))), 1.0);
            }
            ENDCG
        }
    }
}
