// Cosmic microwave background seen by a moving observer: T' = 2.725 K × Doppler factor.
// Past γ ≈ 1000 the forward CMB is blue-shifted into visible light. Fullscreen background pass.
Shader "FogOfWar/CMB"
{
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vertFullscreen
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "FOWCommon.cginc"

            float _OneMinusBeta, _Beta, _Gamma, _Show;
            float3 _Fwd;
            float visB(float T) { return 1.0 / (exp(min(26400.0 / max(T, 1.0), 80.0)) - 1.0); }

            float4 frag(v2fFS i) : SV_Target
            {
                float px;
                float3 d = primaryRay(i.ndc, px);
                float ctp = dot(d, _Fwd);
                float k = _OneMinusBeta + _Beta * (1.0 - ctp);   // 1 - β cosθ', kept precise near β → 1
                float D = 1.0 / (_Gamma * k);
                float T = 2.725 * D;
                float vis = visB(T) / visB(5800.0);
                float3 c = blackbody(T) * min(vis * 0.6, 40.0) * _Show;
                return float4(tonemap(c), 1.0);
            }
            ENDCG
        }
    }
}
