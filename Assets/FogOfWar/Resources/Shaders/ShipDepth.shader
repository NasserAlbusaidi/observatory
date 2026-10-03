// Ship self-shadow map (ShipLighting.cs): each texel keeps the smallest depth along the sunlight, 0 = nearest the Sun.
// Min blending replaces a depth buffer, so the map is one RFloat target with no depth-convention differences.
Shader "Hidden/FogOfWar/ShipDepth"
{
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            BlendOp Min
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4x4 _ShipShadowMatrix;
            struct v2f { float4 pos : SV_POSITION; float d : TEXCOORD0; };
            v2f vert(float4 vertex : POSITION)
            {
                v2f o;
                float3 sp = mul(_ShipShadowMatrix, mul(unity_ObjectToWorld, vertex)).xyz;
                float2 c = sp.xy * 2.0 - 1.0;
#if UNITY_UV_STARTS_AT_TOP
                c.y = -c.y;
#endif
                o.pos = float4(c, 0.5, 1.0);
                o.d = sp.z;
                return o;
            }
            float4 frag(v2f i) : SV_Target { return i.d; }
            ENDCG
        }
    }
}
