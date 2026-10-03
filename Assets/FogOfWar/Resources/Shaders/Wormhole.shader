// Ellis / Morris–Thorne wormhole, ray-traced per pixel.
// Metric ds² = -dt² + dl² + (b² + l²) dΩ²; each ray is integrated in its own orbital plane with conserved angular momentum.
Shader "FogOfWar/Wormhole"
{
    Properties { _B ("Throat radius", Float) = 1.2 }
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

            float _B, _Lc, _Pinch;
            float3 _Er;   // unit vector along which proper distance l increases at the camera
            int _Steps;

            float4 frag(v2fFS i) : SV_Target
            {
                float px;
                float3 rd = primaryRay(i.ndc, px);
                float3 er = _Er;
                float nl = dot(rd, er);
                float3 tv = rd - nl * er;
                float nt = length(tv);
                float3 et = nt > 1e-5 ? tv / nt : normalize(cross(er, float3(0, 1, 0.001)));
                float b = _B * (1.0 - _Pinch);
                float l = _Lc;
                float r0 = sqrt(b * b + l * l);
                float L = r0 * nt;
                float pl = nl;
                float phi = 0.0;
                bool crushed = false;
                float minAbsL = abs(l);
                [loop] for (int k = 0; k < _Steps; k++)
                {
                    float r2 = b * b + l * l;
                    float h = 0.02 + 0.04 * sqrt(r2);
                    float a1 = L * L * l / (r2 * r2);
                    float lm = l + 0.5 * h * pl;
                    float plm = pl + 0.5 * h * a1;
                    float rm2 = b * b + lm * lm;
                    float a2 = L * L * lm / (rm2 * rm2);
                    l += h * plm;
                    pl += h * a2;
                    phi += h * L / rm2;
                    minAbsL = min(minAbsL, abs(l));
                    if (_Pinch > 0.0 && minAbsL < 0.05 + 0.6 * _Pinch && sign(l) != sign(_Lc)) { crushed = true; break; }
                    if (abs(l) > 40.0 && l * pl > 0.0) break;
                }
                float3 col = 0;
                if (!crushed)
                {
                    float rr = sqrt(b * b + l * l);
                    float3 erp = cos(phi) * er + sin(phi) * et;
                    float3 ephi = -sin(phi) * er + cos(phi) * et;
                    float3 dir = normalize(pl * erp + (L / rr) * ephi);
                    col = l > 0.0 ? skyA(dir, px) : skyB(dir, px);
                }
                return float4(tonemap(col), 1.0);
            }
            ENDCG
        }
    }
}
