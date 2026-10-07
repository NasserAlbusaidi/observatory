// Native GPU probe of the production march. Inputs are independently generated constants of motion.
Shader "Hidden/FogOfWar/BlackHoleBenchmark"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 4.5
            #include "BlackHoleCore.cginc"
            sampler2D _Fixture;
            int _Output, _TableProbe;
            float4 frag(v2f_img i) : SV_Target
            {
                float4 f = tex2Dlod(_Fixture, float4(i.uv, 0, 0)); // b, q, polar sign, radial sign
                float4 s = float4(_CamBL.x, f.w * sqrt(max(0, Rpot(_CamBL.x, f.x, f.y, _Spin, 1))),
                                  _CamTheta, f.z * sqrt(max(0, Theta(_CamTheta, f.x, f.y, _Spin))));
                Ray ray = march(f.x, f.y, 1, s, _Spin, 1, _TableProbe != 0);
                if (_Output == 0) return float4(ray.reason, ray.fallbackSky ? 1 : 0, ray.steps, ray.count);
                if (_Output == 1) return float4(skyDir(ray.s, ray.ph, f.x, f.y, _Spin, 1), ray.s.x);
                if (_Output == 2) return ray.x0;
                if (_Output == 3) return ray.x1;
                if (_Output == 4) return ray.x2;
                return float4(ray.col, ray.trans);
            }
            ENDCG
        }
    }
}
