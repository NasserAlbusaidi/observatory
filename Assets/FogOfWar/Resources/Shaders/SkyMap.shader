// The real Milky Way behind the mesh scenes: an equirect sky map (Sky/MilkyWay, NASA SVS Deep Star Maps 2020) on a
// sphere at infinity, in the same frame as the Bright Star Catalogue points drawn over it (+Y celestial north, RA 0h
// at +X, RA 6h at +Z; the object's rotation turns it into the render frame like the star mesh).
Shader "FogOfWar/SkyMap"
{
    Properties
    {
        _MainTex ("Sky map", 2D) = "black" {}
        _Gain ("Gain", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "FOWCommon.cginc"
            sampler2D _MainTex;
            float _Gain;
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 d : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o;
                o.d = v.vertex.xyz;
                float3 w = normalize(mul((float3x3)unity_ObjectToWorld, v.vertex.xyz));
                o.pos = mul(UNITY_MATRIX_VP, float4(_WorldSpaceCameraPos + w * (_ProjectionParams.z * 0.85), 1.0));
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float3 c = texToDisplay(tex2DSphere(_MainTex, i.d).rgb) * _Gain;
                // sunlit bodies in view dim the band like the stars (StarGlare.cs)
                return float4(displayToOutput(c) * pow(10.0, -0.4 * FOW_STAR_SLOPE * _FOWStarGlare), 1.0);
            }
            ENDCG
        }
    }
}
