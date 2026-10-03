// Kerr (spinning) black hole with a thin Novikov–Thorne accretion disc, ray-traced per pixel. Spin 0 is Schwarzschild.
// Units inside: G = c = M = 1 (rs = 2). Every pixel is a null geodesic traced backwards from the camera in Boyer–Lindquist
// coordinates with Mino time λ (dλ = dτ/ρ²), where the radial and polar motions separate:
//   r'' = R'(r)/2,  θ'' = Θ'(θ)/2,  φ' = aP/Δ − a + b/sin²θ
// with b = L/E and q = Q/E² from the pixel direction. The camera is a zero-angular-momentum observer (static when a = 0),
// optionally boosted to ride along with a falling probe.
// The disc is opaque and lies in the equatorial plane from the ISCO outwards; its flux is Page–Thorne and its colour is
// shifted by g = E_camera / E_emitter, the exact redshift (Doppler, gravitational and frame dragging together).
// The sandbox adds debris gas (a polar texture in the equatorial plane, splatted by BlackHoleScene each frame), up to
// four solid bodies (tidally stretched ellipsoids) and guide rings; all of them are seen through the same lensed rays.
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
            float _Spin, _RH, _Isco, _FluxMax, _StepK, _RMax, _Exposure;
            int _Steps;
            // camera, computed on the CPU in double precision: Boyer–Lindquist position, orthonormal triad (world space),
            // and the ZAMO metric functions there
            float4 _CamBL;                 // r, μ, φ, sin θ
            float3 _CamEr, _CamEth, _CamEph;
            float4 _CamMet;                // α (lapse), ω (frame dragging), ϖ (cylindrical radius), ρ
            float _CamDelta, _CamTheta;
            float4 _CamVel;                // camera velocity relative to the ZAMO in (r̂, θ̂, φ̂), and γ; zero when hovering

            // debris gas: polar texture over ln r ∈ [x, y] (M units). R = brightness weight, G = weight × temperature,
            // B = weight × u^r, A = weight × u^φ
            sampler2D _DebrisTex;
            float4 _DebrisRange;           // ln r min, ln r max, on (1/0), brightness
            float4 _Guides;                // ring radii in M (0 = off): ISCO, photon orbit, tidal radius, marginally bound
            float _GuideOn;

            // bodies: position (world, M units) and radius; stretch axis and factor; temperature, emissivity, kind; 4-velocity
            int _NB;
            float4 _BPos[4], _BAxis[4], _BCol[4], _BVel[4];
            float4 _BRad[4];               // body's Boyer-Lindquist r and its reach (drawn radius x stretch), for a cheap test

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

            // Energy of the photon measured by matter moving with 4-velocity (ut, ur, uph) in the equatorial plane
            // (E = 1 at infinity): −p·u = uᵗ − b u^φ − p_r u^r.
            float emitterE(float ut, float ur, float uph, float b, float pr) { return ut - b * uph - pr * ur; }

            // Disc emission where the ray crosses the equator at radius r (M units), azimuth ph, for a photon with b = L/E.
            // EF is the photon energy the camera measures (E = 1 at infinity).
            float3 diskColor(float r, float ph, float b, float EF, float order, out float alpha)
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
                // higher-order images are squeezed into thin arcs where the noise would alias into streaks: fade it there
                float dens = lerp(1.0, 0.3 + 1.0 * smoothstep(0.15, 0.85, turb * 0.85 + streaks * 0.4), _Clumpy / (1.0 + 3.0 * order));
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
                // the outer edge is a soft fade (opacity too); the inner edge is the ISCO, where the flux itself goes to 0
                float outerFade = smoothstep(_Outer, _Outer * 0.7, rs);
                alpha = outerFade;
                return blackbody(T * g) * F * pow(g, 4.0) * dens * 0.75 * _Gain * outerFade;
            }

            // Debris gas from the sandbox at (r, ph); pr is the photon's p_r there.
            float3 debrisColor(float r, float ph, float b, float pr, float EF, out float alpha)
            {
                alpha = 0;
                float lr = log(r);
                if (lr < _DebrisRange.x || lr > _DebrisRange.y) return 0;
                float2 uv = float2((lr - _DebrisRange.x) / (_DebrisRange.y - _DebrisRange.x), frac(ph / UNITY_TWO_PI));
                float4 t = tex2Dlod(_DebrisTex, float4(uv, 0, 0));
                if (t.x < 1e-5) return 0;
                float T = t.y / t.x, ur = t.z / t.x, uph = t.w / t.x;
                float a = _Spin;
                // u^t from the normalisation u·u = −1 (equatorial Kerr metric)
                float gtt = -(1.0 - 2.0 / r), gtp = -2.0 * a / r, gpp = r * r + a * a + 2.0 * a * a / r;
                float grr = r * r / max(r * r - 2.0 * r + a * a, 1e-4);
                float A = gtt, B = 2.0 * gtp * uph, C = gpp * uph * uph + grr * ur * ur + 1.0;
                float disc = sqrt(max(B * B - 4.0 * A * C, 0.0));
                float ut = (-B - disc) / (2.0 * A);
                if (ut < 0.0) ut = (-B + disc) / (2.0 * A);
                float g = EF / max(emitterE(ut, ur, uph, b, pr), 1e-4);
                if (_Doppler < 0.5)
                {
                    float Dz = max(r * r - 2.0 * r + a * a, 1e-4);
                    float S2z = (r * r + a * a) * (r * r + a * a) - a * a * Dz;
                    g = EF * sqrt(r * r * Dz / S2z) / max(1.0 - 2.0 * a * r / S2z * b, 1e-4);
                }
                alpha = 1.0 - exp(-t.x * 3.0);
                return blackbody(T * g) * pow(g, 4.0) * t.x * _DebrisRange.w;
            }

            // Thin guide rings in the equatorial plane (lensed like everything else).
            float3 guideColor(float r)
            {
                float w = 0.02 + 0.006 * r;
                float3 c = 0;
                c += float3(1.0, 0.71, 0.28) * step(0.01, _Guides.x) * smoothstep(w, 0.0, abs(r - _Guides.x));   // ISCO, amber
                c += float3(0.41, 0.82, 1.0) * step(0.01, _Guides.y) * smoothstep(w, 0.0, abs(r - _Guides.y));   // photon orbit, cyan
                c += float3(1.0, 0.42, 0.37) * step(0.01, _Guides.z) * smoothstep(w, 0.0, abs(r - _Guides.z));   // tidal radius, red
                c += float3(0.7, 0.7, 0.8) * step(0.01, _Guides.w) * smoothstep(w, 0.0, abs(r - _Guides.w));    // marginally bound, grey
                return c * 0.6 * _GuideOn;
            }

            // ---- bodies ----------------------------------------------------------------------------------------------

            float3 toCart(float r, float th, float ph, float a)
            {
                float st, ct, sp, cp; sincos(th, st, ct); sincos(ph, sp, cp);
                float s = sqrt(r * r + a * a);
                return float3(s * st * cp, r * ct, s * st * sp);
            }

            // First hit of segment p0→p1 with body i, as a fraction t ∈ [0, 1] (or 2 for none); normal in body space.
            float hitBody(int i, float3 p0, float3 p1, out float3 nrm)
            {
                nrm = 0;
                float3 c = _BPos[i].xyz; float R = _BPos[i].w;
                float3 e = _BAxis[i].xyz; float k = _BAxis[i].w;
                // map the ellipsoid (semi-axis R k along e, R/√k across) to the unit sphere
                float3 o = p0 - c, d = p1 - p0;
                float oe = dot(o, e), de = dot(d, e);
                float sk = sqrt(k);
                float3 o2 = (o - oe * e) * sk / R + e * (oe / (R * k));
                float3 d2 = (d - de * e) * sk / R + e * (de / (R * k));
                float A = dot(d2, d2), B = dot(o2, d2), C = dot(o2, o2) - 1.0;
                float disc = B * B - A * C;
                if (disc < 0.0 || A < 1e-12) return 2.0;
                float t = (-B - sqrt(disc)) / A;
                if (t < 0.0 || t > 1.0) return 2.0;
                nrm = normalize(o2 + d2 * t);
                return t;
            }

            float3 bodyColor(int i, float3 nrm, float3 segDir, float b, float pr, float EF)
            {
                float4 bc = _BCol[i];
                float4 u = _BVel[i];
                float g = EF / max(emitterE(u.x, u.y, u.z, b, pr), 1e-4);
                if (_Doppler < 0.5) g = EF / max(u.x, 1e-4);
                float mu = saturate(-dot(nrm, segDir));
                // stars: limb darkening (Eddington, 0.4 + 0.6 μ); planets: a dull glow lit by the disc
                float shade = bc.z > 0.5 ? 0.25 + 0.75 * mu : 0.4 + 0.6 * mu;
                return blackbody(bc.x * g) * pow(g, 4.0) * bc.y * shade;
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

            float3 skyDir(float4 s, float ph, float b, float q, float a, float M)
            {
                float st, ct; sincos(s.z, st, ct);
                float sp, cp; sincos(ph, sp, cp);
                float3 er = float3(st * cp, ct, st * sp);
                float3 eth = float3(ct * cp, -st, ct * sp);
                float3 eph = float3(-sp, 0, cp);
                float p1;
                deriv(s, b, q, a, M, p1);
                return normalize(er * s.y + eth * (s.x * s.w) + eph * (s.x * st * p1));
            }

            float4 frag(v2fFS i) : SV_Target
            {
                float px;
                float3 rd = primaryRay(i.ndc, px);
                float a = _Spin;
                float M = _Lens > 0.5 ? 1.0 : 0.0;

                // pixel direction in the camera's own frame
                float3 dc = normalize(float3(dot(rd, _CamEr), dot(rd, _CamEth), dot(rd, _CamEph)));
                float3 n = -dc;                                      // the light travels towards the camera
                // a moving camera: boost the photon into the local ZAMO frame (aberration); keep the energy ratio
                float boost = 1.0;
                if (_CamVel.w > 1.0001)
                {
                    float3 v = _CamVel.xyz; float gam = _CamVel.w;
                    float vv = max(dot(v, v), 1e-12);
                    float k0 = gam * (1.0 + dot(v, n));
                    float3 kv = n + ((gam - 1.0) * dot(v, n) / vv + gam) * v;
                    n = kv / k0;
                    boost = 1.0 / k0;                                // E_camera / E_ZAMO
                }
                // → the photon's conserved b and q (Bardeen 1973; James et al. 2015)
                float rc = _CamBL.x, muc = _CamBL.y, stc = _CamBL.w;
                float lapse = _CamMet.x, omg = _CamMet.y, varpi = _CamMet.z, rho = _CamMet.w;
                float EZ = 1.0 / (lapse + omg * varpi * n.z);
                float EF = EZ * boost;                               // energy the camera measures, E = 1 at infinity
                float pr0 = EZ * rho * n.x / sqrt(_CamDelta);
                float pth = EZ * rho * n.y;
                float b = EZ * varpi * n.z;
                // b = 0 exactly would run through the pole itself; a nudge of 1e-4 M moves the ray far less than a pixel
                b = (b < 0.0 ? -1.0 : 1.0) * max(abs(b), 1e-4);
                float q = pth * pth + muc * muc * (b * b / max(stc * stc, 1e-6) - a * a);

                float4 s = float4(rc, -_CamDelta * pr0, _CamTheta, -pth);   // backward: (r, dr/dλ, θ, dθ/dλ)
                float ph = _CamBL.z;
                float rCap = _RH + 1e-3;
                float projR = 1e-3, projT = 1e-3 * (q + b * b + 1.0);
                bool plane = _Disk > 0.5 || _DebrisRange.z > 0.5 || _GuideOn > 0.5;
                float3 pPrev = 0;

                float3 col = 0;
                float trans = 1.0, crossings = 0.0;
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

                    // solid bodies along this segment (straight chord: segments are short where bending is strong)
                    float tBody = 2.0; float3 nrm = 0; int hitI = -1; float3 pNext = 0;
                    bool nearBody = false;
                    if (_NB > 0)
                    {
                        // only steps whose radial span reaches a body pay for the Cartesian chord (audit #62)
                        float rlo = min(s.x, sn.x), rhi = max(s.x, sn.x);
                        [loop] for (int bj = 0; bj < _NB; bj++) nearBody = nearBody || (_BRad[bj].x + _BRad[bj].y >= rlo && _BRad[bj].x - _BRad[bj].y <= rhi);
                    }
                    if (nearBody)
                    {
                        pPrev = toCart(s.x, s.z, ph, a);
                        pNext = toCart(sn.x, sn.z, phn, a);
                        [loop] for (int bi = 0; bi < _NB; bi++)
                        {
                            float3 nb;
                            float tb = hitBody(bi, pPrev, pNext, nb);
                            if (tb < tBody) { tBody = tb; nrm = nb; hitI = bi; }
                        }
                    }

                    float e0 = s.z - UNITY_HALF_PI, e1 = sn.z - UNITY_HALF_PI;
                    if (plane && e0 * e1 < 0.0)
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
                        if (t < tBody)   // the plane is in front of any body hit on this segment
                        {
                            float t2 = t * t, t3 = t2 * t;
                            float rHit = (2 * t3 - 3 * t2 + 1) * s.x + (t3 - 2 * t2 + t) * s.y * h + (-2 * t3 + 3 * t2) * sn.x + (t3 - t2) * sn.y * h;
                            float phHit = lerp(ph, phn, t);
                            float Dh = max(rHit * rHit - 2.0 * rHit + a * a, 1e-4);
                            float prHit = -lerp(s.y, sn.y, t) / Dh;   // the physical photon's p_r
                            col += trans * guideColor(rHit);
                            if (_DebrisRange.z > 0.5)
                            {
                                float al;
                                float3 c = debrisColor(rHit, phHit, b, prHit, EF, al);
                                col += trans * c;
                                trans *= 1.0 - al;
                            }
                            if (_Disk > 0.5 && rHit > _Isco && rHit * 0.5 < _Outer)
                            {
                                float al;
                                float3 c = diskColor(rHit, phHit, b, EF, crossings, al);
                                col += trans * c;
                                trans *= 1.0 - al;
                            }
                            if (trans < 0.01) break;
                        }
                        crossings += 1.0;
                    }
                    if (hitI >= 0)
                    {
                        float rB = lerp(s.x, sn.x, tBody);
                        float Db = max(rB * rB - 2.0 * rB + a * a, 1e-4);
                        float prB = -lerp(s.y, sn.y, tBody) / Db;
                        col += trans * bodyColor(hitI, nrm, normalize(pNext - pPrev), b, prB, EF);
                        trans = 0;
                        break;
                    }
                    s = sn; ph = phn;
                    if (s.x < rCap) break;
                    if (s.x > _RMax && s.y > 0.0) { escaped = true; break; }
                }
                // a ray still going after all its steps is circling near the photon orbit; it would almost always leave
                // again, so take the sky in its current direction rather than black
                if (!escaped && s.x >= rCap * 1.05 && trans > 0.01 && s.y > 0.0) escaped = true;
                if (escaped && trans > 0.01)
                {
                    float3 dir = skyDir(s, ph, b, q, a, M);
                    // the sky is blueshifted by g = EF for this observer: bolometric intensity × g⁴, and an approximate
                    // colour shift (the ratio of blackbody tints for a 6500 K source seen at g × 6500 K)
                    float gs = clamp(EF, 0.05, 20.0);
                    float3 tint = blackbody(6500.0 * clamp(gs, 0.25, 4.0)) / blackbody(6500.0);
                    col += trans * skyA(dir, px) * tint * gs * gs * gs * gs;
                }
                return float4(tonemap(col * _Exposure), 1.0);
            }
            ENDCG
        }
    }
}
