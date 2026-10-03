// Kerr (spinning) black hole with a thin Novikov–Thorne accretion disc, ray-traced per pixel. Spin 0 is Schwarzschild.
// Units inside: G = c = M = 1 (rs = 2). Every pixel is a null geodesic traced backwards from the camera in Boyer–Lindquist
// coordinates with Mino time λ (dλ = dτ/ρ²), where the radial and polar motions separate:
//   r'' = R'(r)/2,  θ'' = Θ'(θ)/2,  φ' = aP/Δ − a + b/sin²θ
// with b = L/E and q = Q/E² from the pixel direction. The camera is a zero-angular-momentum observer (static when a = 0).
// The disc is opaque and lies in the equatorial plane from the ISCO outwards; its flux is Page–Thorne and its colour is
// shifted by g = E_camera / E_emitter, the exact redshift (Doppler, gravitational and frame dragging together).
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
            #pragma target 4.5
            #include "UnityCG.cginc"
            #include "FOWCommon.cginc"

            float _Disk, _Doppler, _Outer, _Temp, _Lens, _SimTime, _Clumpy, _Gain, _SwirlRate;
            float _Spin, _RH, _Isco, _FluxMax, _StepK, _RMax;
            int _Steps;
            // camera, computed on the CPU in double precision: Boyer–Lindquist position, orthonormal triad (world space),
            // and the ZAMO metric functions there
            float4 _CamBL;                 // r, μ, φ, sin θ
            float3 _CamEr, _CamEth, _CamEph;
            float4 _CamMet;                // α (lapse), ω (frame dragging), ϖ (cylindrical radius), ρ
            float _CamDelta, _CamTheta;

            // ---- disc ------------------------------------------------------------------------------------------------

            // Page & Thorne (1974) flux for a prograde disc, M = 1, without the 3Ṁ/8π factor. Zero inside the ISCO.
            float pageThorne(float r, float a)
            {
                float x = sqrt(r), x0 = sqrt(_Isco);
                if (x <= x0) return 0.0;
                a = max(a, 1e-3);   // the x2 root sits at 0 when a = 0; the limit is smooth
                float ac = acos(a);
                float x1 = 2.0 * cos((ac - UNITY_PI) / 3.0);
                float x2 = 2.0 * cos((ac + UNITY_PI) / 3.0);
                float x3 = -2.0 * cos(ac / 3.0);
                float br = x - x0 - 1.5 * a * log(x / x0)
                    - 3.0 * (x1 - a) * (x1 - a) / (x1 * (x1 - x2) * (x1 - x3)) * log((x - x1) / (x0 - x1))
                    - 3.0 * (x2 - a) * (x2 - a) / (x2 * (x2 - x1) * (x2 - x3)) * log((x - x2) / (x0 - x2))
                    - 3.0 * (x3 - a) * (x3 - a) / (x3 * (x3 - x1) * (x3 - x2)) * log((x - x3) / (x0 - x3));
                return max(br / (x * x * x * x * (x * x * x - 3.0 * x + 2.0 * a)), 0.0);
            }

            // Disc emission where the ray crosses the equator at radius r (M units), azimuth ph, for a photon with b = L/E.
            // EF is the photon energy the camera measures (E = 1 at infinity).
            float3 diskColor(float r, float ph, float b, float EF, out float alpha)
            {
                float a = _Spin;
                float rs = r * 0.5;                                  // rs units, for the texture and the edges
                float F = pageThorne(r, a) / _FluxMax;
                float sr = sqrt(r);
                float om = 1.0 / (r * sr + a);                       // Keplerian Ω = dφ/dt
                // the gas pattern co-rotates with Ω (sped up by _SwirlRate, in M per second of sim time)
                float ang = ph - om * _SimTime * _SwirlRate;
                float2 q = float2(cos(ang), sin(ang)) * rs;
                float turb = fbm(float3(q * 1.3, rs * 0.6));
                float streaks = fbm(float3(q * 0.35 * rs, 1.0));
                float dens = lerp(1.0, 0.3 + 1.0 * smoothstep(0.15, 0.85, turb * 0.85 + streaks * 0.4), _Clumpy);
                // redshift g = E_obs / E_emit. Circular orbit: E_emit = u^t (1 − Ω b). Without Doppler: a ZAMO emitter.
                float ut = (r * sr + a) / (sqrt(sr * r) * sqrt(max(r * sr - 3.0 * sr + 2.0 * a, 1e-5)));
                float Eorb = ut * (1.0 - om * b);
                float D = r * r - 2.0 * r + a * a;
                float S2 = (r * r + a * a) * (r * r + a * a) - a * a * D;
                float lapse = sqrt(r * r * D / S2);
                float Ezamo = (1.0 - 2.0 * a * r / S2 * b) / lapse;
                float Eem = _Doppler > 0.5 ? Eorb : Ezamo;
                float g = EF / max(Eem, 1e-4);
                float T = _Temp * pow(F, 0.25);
                float edge = smoothstep(_Outer, _Outer * 0.7, rs) * smoothstep(_Isco * 0.5, _Isco * 0.5 + 0.12, rs);
                alpha = edge;
                return blackbody(T * g) * F * pow(g, 4.0) * dens * 0.75 * _Gain * edge;
            }

            // ---- geodesic ----------------------------------------------------------------------------------------------

            // d/dλ of (r, r', θ, θ') and φ' for the backward ray. M is 1, or 0 with lensing off (straight lines).
            // θ itself (not cos θ) is integrated: near a pole sin θ stays resolvable in float, so rays that pass over the
            // pole, where φ swings through π, come out smooth.
            float4 deriv(float4 s, float b, float q, float a, float M, out float php)
            {
                float r = s.x;
                float sn, cs; sincos(s.z, sn, cs);
                float st = max(abs(sn), 1e-7);
                float P = r * r + a * a - a * b;
                float K = (b - a) * (b - a) + q;
                float D = r * r - 2.0 * M * r + a * a;
                php = -(a * P / D - a + b / (st * st));
                float rpp = 2.0 * r * P - (r - M) * K;
                float thpp = b * b * cs / (st * st * st) - a * a * sn * cs;
                return float4(s.y, rpp, s.w, thpp);
            }

            float Rpot(float r, float b, float q, float a, float M)
            {
                float P = r * r + a * a - a * b;
                return P * P - (r * r - 2.0 * M * r + a * a) * ((b - a) * (b - a) + q);
            }

            float Theta(float th, float b, float q, float a)
            {
                float sn, cs; sincos(th, sn, cs);
                float st2 = max(sn * sn, 1e-14);
                return q + cs * cs * (a * a - b * b / st2);
            }

            float4 frag(v2fFS i) : SV_Target
            {
                float px;
                float3 rd = primaryRay(i.ndc, px);
                float a = _Spin;
                float M = _Lens > 0.5 ? 1.0 : 0.0;

                // pixel direction in the camera's local frame → the photon's conserved b and q (Bardeen 1973; James et al. 2015)
                float3 d = normalize(float3(dot(rd, _CamEr), dot(rd, _CamEth), dot(rd, _CamEph)));
                float3 n = -d;                                       // the light travels towards the camera
                float rc = _CamBL.x, muc = _CamBL.y, stc = _CamBL.w;
                float lapse = _CamMet.x, omg = _CamMet.y, varpi = _CamMet.z, rho = _CamMet.w;
                float EF = 1.0 / (lapse + omg * varpi * n.z);
                float pr = EF * rho * n.x / sqrt(_CamDelta);
                float pth = EF * rho * n.y;
                float b = EF * varpi * n.z;
                // b = 0 exactly would run through the pole itself; a nudge of 1e-4 M moves the ray far less than a pixel
                b = (b < 0.0 ? -1.0 : 1.0) * max(abs(b), 1e-4);
                float q = pth * pth + muc * muc * (b * b / max(stc * stc, 1e-6) - a * a);

                float4 s = float4(rc, -_CamDelta * pr, _CamTheta, -pth);   // backward: (r, dr/dλ, θ, dθ/dλ)
                float ph = _CamBL.z;
                float rCap = _RH + 1e-3;
                float projR = 1e-3, projT = 1e-3 * (q + b * b + 1.0);

                float3 col = 0;
                float trans = 1.0;
                bool escaped = false;
                [loop] for (int k = 0; k < _Steps; k++)
                {
                    float p1, p2, p3, p4;
                    float4 k1 = deriv(s, b, q, a, M, p1);
                    float h = _StepK / (abs(s.y) / s.x + sqrt(abs(k1.y) / s.x) + abs(p1) + abs(s.w) / max(abs(sin(s.z)), 1e-7) + sqrt(abs(k1.w)));
                    float4 k2 = deriv(s + 0.5 * h * k1, b, q, a, M, p2);
                    float4 k3 = deriv(s + 0.5 * h * k2, b, q, a, M, p3);
                    float4 k4 = deriv(s + h * k3, b, q, a, M, p4);
                    float4 sn = s + h / 6.0 * (k1 + 2.0 * k2 + 2.0 * k3 + k4);
                    float phn = ph + h / 6.0 * (p1 + 2.0 * p2 + 2.0 * p3 + p4);
                    // hold the first integrals r'² = R(r) and θ'² = Θ(θ) (except near turning points) so errors don't drift
                    float Rn = Rpot(sn.x, b, q, a, M);
                    if (Rn > projR * sn.x * sn.x * sn.x * sn.x) sn.y = (sn.y < 0.0 ? -1.0 : 1.0) * sqrt(Rn);
                    float Tn = Theta(sn.z, b, q, a);
                    if (Tn > projT) sn.w = (sn.w < 0.0 ? -1.0 : 1.0) * sqrt(Tn);

                    float e0 = s.z - UNITY_HALF_PI, e1 = sn.z - UNITY_HALF_PI;
                    if (_Disk > 0.5 && e0 * e1 < 0.0)
                    {
                        // equator crossing: cubic Hermite for θ(t), two Newton steps from the linear guess
                        float t = e0 / (e0 - e1);
                        float d0 = s.w * h, d1 = sn.w * h;
                        [unroll] for (int it = 0; it < 2; it++)
                        {
                            float tt = t * t, ttt = tt * t;
                            float f = (2 * ttt - 3 * tt + 1) * e0 + (ttt - 2 * tt + t) * d0 + (-2 * ttt + 3 * tt) * e1 + (ttt - tt) * d1;
                            float fp = (6 * tt - 6 * t) * e0 + (3 * tt - 4 * t + 1) * d0 + (-6 * tt + 6 * t) * e1 + (3 * tt - 2 * t) * d1;
                            t = saturate(t - f / (abs(fp) > 1e-6 ? fp : 1e-6));
                        }
                        float t2 = t * t, t3 = t2 * t;
                        float rHit = (2 * t3 - 3 * t2 + 1) * s.x + (t3 - 2 * t2 + t) * s.y * h + (-2 * t3 + 3 * t2) * sn.x + (t3 - t2) * sn.y * h;
                        float phHit = lerp(ph, phn, t);
                        if (rHit > _Isco && rHit * 0.5 < _Outer)
                        {
                            float al;
                            float3 dc = diskColor(rHit, phHit, b, EF, al);
                            col += trans * dc;
                            trans *= 1.0 - al;
                            if (trans < 0.01) break;
                        }
                    }
                    s = sn; ph = phn;
                    if (s.x < rCap) break;
                    if (s.x > _RMax && s.y > 0.0) { escaped = true; break; }
                }
                if (escaped && trans > 0.01)
                {
                    // direction of travel far away, from (dr, r dθ, r sin θ dφ)
                    float st, ct; sincos(s.z, st, ct);
                    float sp, cp; sincos(ph, sp, cp);
                    float3 er = float3(st * cp, ct, st * sp);
                    float3 eth = float3(ct * cp, -st, ct * sp);
                    float3 eph = float3(-sp, 0, cp);
                    float p1;
                    deriv(s, b, q, a, M, p1);
                    float3 dir = normalize(er * s.y + eth * (s.x * s.w) + eph * (s.x * st * p1));
                    // the sky is blueshifted by g = EF for this observer; bolometric intensity goes as g⁴
                    float gs = min(EF, 20.0);
                    col += trans * skyA(dir, px) * gs * gs * gs * gs;
                }
                return float4(tonemap(col), 1.0);
            }
            ENDCG
        }
    }
}
