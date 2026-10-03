// Replacement shader for the photo-mode depth pass: writes 1 / eye depth for solid geometry (Queue "Geometry").
// Background ray-marched quads and transparent glows have other queues and are skipped, so they read as sky.
Shader "Hidden/FogOfWar/PhotoDepth"
{
    SubShader
    {
        Tags { "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float z : TEXCOORD0; };
            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.z = -UnityObjectToViewPos(v.vertex.xyz).z;
                return o;
            }
            float4 frag(v2f i) : SV_Target { return float4(1.0 / max(i.z, 1e-6), 0, 0, 1); }
            ENDCG
        }
    }
}
