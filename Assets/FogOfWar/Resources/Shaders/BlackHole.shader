// Schwarzschild black hole with a thin accretion disk, ray-traced per pixel.
// Units: Schwarzschild radius rs = 1. Photons obey d²x/dλ² = -1.5 h² x / r⁵.
Shader "FogOfWar/BlackHole"
{
    Properties { _Temp ("Peak disk temperature", Float) = 4800 }
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

            float _Disk, _Doppler, _Outer, _Temp, _Lens, _SimTime;
            int _Steps;

            float3 diskColor(float3 hit, float3 vel, out float alpha)
            {
                float rr = length(hit.xz);
                // Novikov–Thorne-like flux, inner edge at the ISCO (3 rs); peak value 0.0567 at r ≈ 4.1 rs
                float x = 3.0 / rr;
                float prof = x * x * x * max(1.0 - sqrt(x), 0.0);
                float tempRel = pow(prof / 0.0567, 0.25);
                float om = 0.9 * pow(rr, -1.5);
                float ang = _SimTime * om * 6.0;
                float2 q = float2(cos(ang) * hit.x - sin(ang) * hit.z, sin(ang) * hit.x + cos(ang) * hit.z);
                float turb = fbm(float3(q * 1.3, rr * 0.6));
                float streaks = fbm(float3(q * 0.35 * rr, 1.0));
                float dens = smoothstep(0.15, 0.85, turb * 0.85 + streaks * 0.4);
                // Doppler factor and gravitational redshift
                float3 vdir = normalize(float3(-hit.z, 0.0, hit.x));
                float beta = min(sqrt(0.5 / (rr - 1.0)), 0.99);
                float3 ph = -normalize(vel);
                float D = sqrt(1.0 - beta * beta) / (1.0 - beta * dot(vdir, ph));
                float grav = sqrt(1.0 - 1.0 / rr);
                float g = lerp(grav, D * grav, _Doppler);
                float T = _Temp * tempRel * g;
                float3 c = blackbody(T) * prof / 0.0567 * pow(g, 4.0) * (0.3 + 1.0 * dens) * 0.75;
                float edge = smoothstep(_Outer, _Outer * 0.7, rr) * smoothstep(2.95, 3.25, rr);
                alpha = saturate((0.55 + 0.45 * dens) * edge);
                return c * edge;
            }

            float4 frag(v2fFS i) : SV_Target
            {
                float px;
                float3 rd = primaryRay(i.ndc, px);
                float3 pos = _CamPos;
                float3 vel = rd;
                float3 hv = cross(pos, vel);
                float h2 = dot(hv, hv);
                float3 col = 0;
                float trans = 1.0;
                bool captured = false;
                [loop] for (int k = 0; k < _Steps; k++)
                {
                    float r = length(pos);
                    if (r < 1.0) { captured = true; break; }
                    if (r > 60.0 && dot(pos, vel) > 0.0) break;
                    float dt = clamp(0.045 * r * (r - 0.8), 0.012, 2.5);
                    float3 acc = -1.5 * h2 * pos / pow(r, 5.0) * _Lens;
                    float3 nv = vel + acc * dt;
                    float3 np = pos + nv * dt;
                    if (_Disk > 0.5 && pos.y * np.y < 0.0)
                    {
                        float t = pos.y / (pos.y - np.y);
                        float3 hit = lerp(pos, np, t);
                        float rr = length(hit.xz);
                        if (rr > 2.9 && rr < _Outer)
                        {
                            float a;
                            float3 dc = diskColor(hit, nv, a);
                            col += trans * dc * a;
                            trans *= 1.0 - a;
                            if (trans < 0.02) break;
                        }
                    }
                    pos = np;
                    vel = nv;
                }
                if (!captured && trans > 0.02) col += trans * skyA(normalize(vel), px);
                return float4(tonemap(col), 1.0);
            }
            ENDCG
        }
    }
}
