// Vertex-coloured unlit geometry for lines, trails, rings and labels. _Additive switches the blend.
Shader "FogOfWar/Unlit"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 10
        [Toggle] _ZWrite ("ZWrite", Float) = 0
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite [_ZWrite] Blend [_SrcBlend] [_DstBlend]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "FOWCommon.cginc"
            float4 _Color;
            sampler2D _MainTex;
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color * _Color; o.uv = v.uv; return o; }
            float4 frag(v2f i) : SV_Target
            {
                float4 c = i.color * tex2D(_MainTex, i.uv);
                return float4(displayToOutput(c.rgb), c.a);
            }
            ENDCG
        }
    }
}
