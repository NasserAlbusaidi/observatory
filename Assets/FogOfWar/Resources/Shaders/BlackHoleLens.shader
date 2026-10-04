// The lens table's build pass (BlackHoleScene.Lens.cs): one cell per pixel of five render targets (_LensN²), each the
// table-mode trace of one direction (BlackHoleCore.cginc). The material carries the same uniforms as the image's.
Shader "Hidden/FogOfWar/BlackHoleLens"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vertFullscreen
            #pragma fragment fragTable
            #pragma target 4.5
            #include "BlackHoleCore.cginc"

            struct LensOut { float4 t0 : SV_Target0; float4 t1 : SV_Target1; float4 t2 : SV_Target2; float4 t3 : SV_Target3; float4 t4 : SV_Target4; };

            LensOut fragTable(v2fFS i)
            {
                float2 cell = floor(i.pos.xy);
                float psi = (cell.x + 0.5) / _LensN * UNITY_TWO_PI;
                float rho = lensRho((cell.y + 0.5) / _LensN, lensEdge(psi));
                float sp, cp, sr, cr; sincos(psi, sp, cp); sincos(rho, sr, cr);
                float3 d = float3(-cr, sr * cp, sr * sp);
                float b, q, EF; float4 s0;
                photon(-d, 1.0, 1.0, b, q, EF, s0);
                Ray R = march(b, q, EF, s0, _Spin, 1.0, true);
                float fate = R.escaped ? 1.0 : R.s.x < _RH + 1e-3 ? 0.0 : 2.0;
                LensOut o;
                o.t0 = float4(fate * 8.0 + min(R.count, 4.0), skyDir(R.s, R.ph - _CamBL.z, b, q, _Spin, 1.0));
                o.t1 = R.x0; o.t2 = R.x1; o.t3 = R.x2;
                o.t4 = float4(R.rmin, R.rmax, 0, 0);
                return o;
            }
            ENDCG
        }
    }
}
