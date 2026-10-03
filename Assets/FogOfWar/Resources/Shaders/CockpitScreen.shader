// The cabin's MFDs: the shared UI texture (three screens side by side), shown as an emissive LCD.
Shader "FogOfWar/CockpitScreen"
{
    Properties { _MainTex ("Screens", 2D) = "black" {} }
    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _CkScreenGain;   // panel brightness, linear

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }

            float4 frag(v2f i) : SV_Target
            {
                // the texture is sampled linear (UI Toolkit wrote it to an sRGB target)
                float3 c = tex2D(_MainTex, i.uv).rgb;
                // an LCD never shows true black, and its backlight falls off a little toward the edges
                float2 s = float2(frac(i.uv.x * 3.0), i.uv.y);
                float2 e = min(s, 1.0 - s);
                float edge = smoothstep(0.0, 0.06, min(e.x, e.y));
                c = (c + 0.004) * lerp(0.75, 1.0, edge);
                return float4(c * _CkScreenGain, 1);
            }
            ENDCG
        }
    }
}
