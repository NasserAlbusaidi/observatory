// Fusion-drive exhaust on a stretched sphere (object +Z toward the nozzle). Additive: a white-hot core near the
// nozzle fading to blue along the length, soft edges (brightest where you look through the most plasma), a slight
// flicker, and shock diamonds that fade downstream. _Intensity sets the overall level.
Shader "FogOfWar/Plume"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off Blend One One
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "FOWCommon.cginc"
            float _Intensity;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float z : TEXCOORD0; float3 n : TEXCOORD1; float3 wp : TEXCOORD2; };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.z = v.vertex.z;
                o.n = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float t = saturate((1.0 - i.z) * 0.5);                // 0 at the nozzle, 1 at the tip
                float3 vdir = normalize(_WorldSpaceCameraPos - i.wp);
                float edge = pow(abs(dot(normalize(i.n), vdir)), 1.5); // soft limb
                float along = pow(1.0 - t, 1.6);
                float diamonds = 1.0 + 0.45 * sin(t * 42.0 - _Time.y * 25.0) * exp(-t * 4.0);
                float flicker = 0.9 + 0.1 * sin(_Time.y * 61.0) * sin(_Time.y * 37.0);
                float3 col = lerp(float3(1.0, 0.97, 0.92), float3(0.35, 0.55, 1.0), saturate(t * 1.6));
                float k = _Intensity * edge * along * diamonds * flicker;
                return float4(displayToOutput(col * k), 1.0);
            }
            ENDCG
        }
    }
}
