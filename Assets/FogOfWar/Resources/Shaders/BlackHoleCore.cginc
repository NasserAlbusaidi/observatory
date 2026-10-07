// Shared by BlackHole.shader (the image) and BlackHoleLens.shader (the lens table): the uniforms, the disc and sandbox
// emitters, the geodesic, the full trace and the table lookup. See BlackHole.shader for the physics.
#ifndef BLACKHOLE_CORE_INCLUDED
#define BLACKHOLE_CORE_INCLUDED
#include "UnityCG.cginc"
#include "FOWCommon.cginc"

float _Disk, _Doppler, _Outer, _Temp, _Lens, _SimTime, _Clumpy, _Gain, _SwirlRate;
float _Spin, _RH, _Isco, _FluxMax, _StepK, _RMax, _Exposure, _DebugFate;
float _HDROut;                 // 1: write linear radiance for BHPost (glare, accumulation, tonemap); 0: tonemap here
float2 _Jitter;                // sub-pixel offset in NDC for temporal accumulation (0 in debug views)
float _SkyGain;                // 1 / BHPost's default exposure: the sky keeps its pixel values at the default
// thick disc (step 2): radiation-pressure scale height H_rad (M); 0 = the thin disc
float _Thick, _Hrad;
// orbiting hot spot: on, radius (M), Gaussian radius (M); its azimuth follows Ω at the disc's swirl clock
float _SpotOn, _SpotR, _SpotSigma;
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
    float ac = acos(a);
    float x1 = 2.0 * cos((ac - UNITY_PI) / 3.0);
    float x2 = 2.0 * cos((ac + UNITY_PI) / 3.0);
    float x3 = -2.0 * cos(ac / 3.0);
    // (xᵢ − a)²/xᵢ written as xᵢ(xᵢ² − 1)²/4 (the xᵢ are roots of x³ − 3x + 2a): no division by x2, which
    // is 0 at a = 0, so spin 0 is the exact Schwarzschild flux (as Kerr.PageThorne)
    float c1 = x1 * (x1 * x1 - 1.0) * (x1 * x1 - 1.0) * 0.25;
    float c2 = x2 * (x2 * x2 - 1.0) * (x2 * x2 - 1.0) * 0.25;
    float c3 = x3 * (x3 * x3 - 1.0) * (x3 * x3 - 1.0) * 0.25;
    float br = x - x0 - 1.5 * a * log(x / x0)
        - 3.0 * c1 / ((x1 - x2) * (x1 - x3)) * log((x - x1) / (x0 - x1))
        - 3.0 * c2 / ((x2 - x1) * (x2 - x3)) * log((x - x2) / (x0 - x2))
        - 3.0 * c3 / ((x3 - x1) * (x3 - x2)) * log((x - x3) / (x0 - x3));
    return max(br / (x * x * x * x * (x * x * x - 3.0 * x + 2.0 * a)), 0.0);
}

// Energy of the photon measured by matter moving with 4-velocity (ut, ur, uph) in the equatorial plane
// (E = 1 at infinity): −p·u = uᵗ − b u^φ − p_r u^r.
float emitterE(float ut, float ur, float uph, float b, float pr) { return ut - b * uph - pr * ur; }

// Disc emission where the ray crosses the equator at radius r (M units), azimuth ph, for a photon with b = L/E.
// EF is the photon energy the camera measures (E = 1 at infinity).
// Gas density pattern at (r, ph): two flow-map phases. Each phase follows the gas at exactly Ω(r) for one orbit
// since its own restart, with fresh noise after every restart; the two are half an orbit apart and
// cross-faded with triangle weights, so the shear never winds the pattern into rings ("vinyl").
float gasPattern(float r, float ph, float om, float a)
{
    float rs = r * 0.5;
    float P = UNITY_TWO_PI * (r * sqrt(r) + a);         // orbital period (M)
    float cyc = _SimTime * _SwirlRate / P;
    float d = 0.0;
    [unroll] for (int k = 0; k < 2; k++)
    {
        float c = cyc + 0.5 * k;
        float f = frac(c), seed = floor(c) * 7.31 + k * 3.17;
        float ang = ph - om * f * P;                     // where this gas was when the phase restarted
        float2 q = float2(cos(ang), sin(ang)) * rs;
        float turb = fbm(float3(q * 1.3, rs * 0.6 + seed));
        float streaks = fbm(float3(q * 0.35 * rs, 1.0 + seed));
        float clumps = fbm(float3(q.x * 2.1, rs * 1.7, q.y * 2.1) + seed * 1.9);
        float v = smoothstep(0.15, 0.85, turb * 0.7 + streaks * 0.35 + clumps * 0.3);
        d += v * (1.0 - abs(2.0 * f - 1.0));
    }
    return d;
}

// The gas density at (r, ph) for a crossing of the given order: the swirl pattern, faded on higher-order images,
// which are squeezed into thin arcs where the noise would alias into streaks. 1 without the clumpy texture.
float gasDens(float r, float ph, float order)
{
    if (_Clumpy < 0.5) return 1.0;
    float a = _Spin;
    float om = 1.0 / (r * sqrt(r) + a);
    return lerp(1.0, 0.3 + 1.0 * gasPattern(r, ph, om, a), 1.0 / (1.0 + 3.0 * order));
}

// dens: the gas density there (gasDens; shadeCrossing works it out once for the slab and the midplane).
float3 diskColor(float r, float ph, float b, float q, float EF, float order, float dens, out float alpha, out float dbgLum)
{
    float a = _Spin;
    float rs = r * 0.5;                                  // rs units, for the edges
    float F = pageThorne(r, a) / _FluxMax;
    float sr = sqrt(r);
    float om = 1.0 / (r * sr + a);                       // Keplerian Ω = dφ/dt
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
    // electron-scattering limb darkening (Chandrasekhar): I ∝ 1 + 2.06 μ, μ the cosine from the disc normal
    // in the emitter's frame, |p^θ̂| / E_emit = √q / (r E_emit) at the equator; normalised at μ = 0.5
    float mu = saturate(sqrt(max(q, 0.0)) / (r * max(Eem, 1e-4)));
    float limb = (1.0 + 2.06 * mu) / (1.0 + 2.06 * 0.5);
    // the outer edge is a soft fade (opacity too); the inner edge is the ISCO, where the flux itself goes to 0
    float outerFade = smoothstep(_Outer, _Outer * 0.7, rs);
    alpha = outerFade;
    dbgLum = 0;
    if (_DebugFate > 1.5)
    {
        // debug view 2 keeps the brightness the old look had (old texture, old tint), so it stays comparable
        float ang = ph - om * _SimTime * _SwirlRate;
        float2 q2 = float2(cos(ang), sin(ang)) * rs;
        float densOld = lerp(1.0, 0.3 + 1.0 * smoothstep(0.15, 0.85, fbm(float3(q2 * 1.3, rs * 0.6)) * 0.85 + fbm(float3(q2 * 0.35 * rs, 1.0)) * 0.4), _Clumpy / (1.0 + 3.0 * order));
        dbgLum = dot(blackbody(T * g) * F * pow(g, 4.0) * densOld * 0.75 * _Gain * outerFade, 1.0);
    }
    return planckRGB(T * g) * F * pow(g, 4.0) * limb * dens * 0.75 * _Gain * outerFade;
}

// Disc gas at radius r seen by a photon with b, q, without the swirl texture: emission and the local temperature
// T (for the hot spot). Same physics as diskColor (Page–Thorne flux, exact g, Planck colour, limb darkening).
float3 gasColor(float r, float b, float q, float EF, float tScale, float fScale, out float T)
{
    float a = _Spin;
    float F = pageThorne(r, a) / _FluxMax;
    float sr = sqrt(r);
    float om = 1.0 / (r * sr + a);
    float ut = (r * sr + a) / (sqrt(sr * r) * sqrt(max(r * sr - 3.0 * sr + 2.0 * a, 1e-5)));
    float Eorb = ut * (1.0 - om * b);
    float D = r * r - 2.0 * r + a * a;
    float S2 = (r * r + a * a) * (r * r + a * a) - a * a * D;
    float Ezamo = (1.0 - 2.0 * a * r / S2 * b) / sqrt(max(r * r * D / S2, 1e-8));
    float Eem = _Doppler > 0.5 ? Eorb : Ezamo;
    float g = EF / max(Eem, 1e-4);
    T = _Temp * pow(F, 0.25);
    float mu = saturate(sqrt(max(q, 0.0)) / (r * max(Eem, 1e-4)));
    float limb = (1.0 + 2.06 * mu) / (1.0 + 2.06 * 0.5);
    float outerFade = smoothstep(_Outer, _Outer * 0.7, r * 0.5);
    return planckRGB(T * tScale * g) * F * fScale * pow(g, 4.0) * limb * 0.75 * _Gain * outerFade;
}

// Half-thickness of the disc at r (M): Shakura–Sunyaev radiation-pressure height, zero at the ISCO (the
// stress-free edge), plus a 1 % gas-pressure floor.
float discH(float r) { return _Hrad * max(1.0 - sqrt(_Isco / r), 0.0) + 0.01 * r; }

// The hot spot's weight at (r, ph): a Gaussian of radius _SpotSigma round _SpotR, moving at the Keplerian Ω on
// the disc's swirl clock.
float spotWeight(float r, float ph)
{
    float phs = (_SimTime * _SwirlRate) / (_SpotR * sqrt(_SpotR) + _Spin);
    float d2 = r * r + _SpotR * _SpotR - 2.0 * r * _SpotR * cos(ph - phs);
    return exp(-0.5 * d2 / (_SpotSigma * _SpotSigma));
}

// The near half of the slab around an equator crossing, sampled at 4 points along the chord from where the ray
// enters the disc to the midplane: Gaussian density in z/H, optical depth 4 from surface to midplane (vertically),
// source = the local Page–Thorne blackbody times the crossing's gas density (the swirl texture, dens). Inside the
// hot spot the gas is up to 3× hotter, so the spot shows at the slab's photosphere; spotLum gets its share. The
// opacity fades with the disc's outer edge like the emission. Returns the light added; trans is reduced.
// v: the backward ray's direction there in the local frame (radial, vertical, azimuthal).
float3 slabColor(float rHit, float phHit, float b, float q, float EF, float3 v, float dens, inout float trans, inout float spotLum)
{
    float H = discH(rHit);
    float sinI = max(abs(v.y), 1e-3);
    float L = min(2.0 * H / sinI, 0.2 * rHit);           // full chord, clamped for grazing rays
    float sig = 0.5 * H, kap = 4.0 / (sig * 1.2533);     // ∫ kap exp(−z²/2σ²) dz over z ≥ 0 = 4
    float3 c = 0;
    [unroll] for (int k = 3; k >= 0; k--)               // from the camera side towards the midplane
    {
        float sk = -(k + 0.5) / 4.0 * 0.5 * L;            // behind the crossing on the backward ray
        float z = sk * v.y;
        float rk = rHit + sk * v.x;
        if (rk <= _Isco) continue;
        float fade = smoothstep(_Outer, _Outer * 0.7, rk * 0.5);
        float dtau = kap * exp(-0.5 * z * z / (sig * sig)) * (0.125 * L) * fade;
        float ts = _SpotOn > 0.5 ? 1.0 + 2.0 * spotWeight(rk, phHit + sk * v.z / rHit) : 1.0;
        float ts4 = ts * ts * ts * ts;
        float Tk;
        float3 S = gasColor(rk, b, q, EF, ts, ts4, Tk) * dens;
        float3 e = trans * S * (1.0 - exp(-dtau));
        c += e;
        spotLum += dot(e, float3(0.2126, 0.7152, 0.0722)) * (1.0 - 1.0 / ts4);
        trans *= exp(-dtau);
    }
    return c;
}

// The hot spot (GRAVITY 2018 Sgr A* flares): a Gaussian blob in the plane at _SpotR, moving at the
// Keplerian Ω (same swirl clock as the disc), 3× the local temperature (so 81× the flux). Returns light and
// its weight (the blob's own opacity when the disc is off).
float3 spotColor(float rHit, float phHit, float b, float q, float EF, out float w)
{
    w = spotWeight(rHit, phHit);
    if (w < 1e-3) { w = 0; return 0; }
    float T;
    return gasColor(max(rHit, _Isco * 1.001), b, q, EF, 3.0, 81.0, T) * w;
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
    return planckRGB(T * g) * pow(g, 4.0) * t.x * _DebrisRange.w;
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
    return planckRGB(bc.x * g) * pow(g, 4.0) * bc.y * shade;
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
    // At zero spin the radial azimuth term cancels identically, including at the horizon. Evaluating 0/0 in an
    // intermediate RK stage otherwise invents a numerical failure for a captured Schwarzschild ray.
    php = -(a == 0.0 ? 0.0 : a * P / D - a) - b / (st * st);
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
    float3 direction = er * s.y + eth * (s.x * s.w) + eph * (s.x * st * p1);
    // Captured states can lie at the coordinate horizon, where the unused sky tangent is singular. Keep diagnostic
    // textures finite as well as the visible image; a zero direction carries no escaped-ray claim.
    return any(!isfinite(direction)) || dot(direction, direction) <= 0.0 ? float3(0, 0, 0) : normalize(direction);
}

// ---- shading at an equator crossing ----------------------------------------------------------------------

// Everything in the equatorial plane where a ray crosses it, nearest first: guide rings, debris, the near half
// of the thick slab, the hot spot, the disc. The full trace and the lens-table lookup both shade a crossing
// here. pr: the photon's p_r there; thd: the backward ray's dθ/dλ; order: the crossings before this one.
void shadeCrossing(float rHit, float phHit, float prHit, float thd, float b, float q, float EF, float M, float order, float densIn,
                   inout float3 col, inout float trans, inout float spotLum, inout float firstHit, inout float firstG)
{
    float a = _Spin;
    col += trans * guideColor(rHit);
    if (_DebrisRange.z > 0.5)
    {
        float al;
        float3 c = debrisColor(rHit, phHit, b, prHit, EF, al);
        col += trans * c;
        trans *= 1.0 - al;
    }
    bool disc = _Disk > 0.5 && rHit > _Isco && rHit * 0.5 < _Outer;
    // the gas pattern co-rotates with Ω (sped up by _SwirlRate, in M per second of sim time); densIn: the lens table
    // pass's shared value (lensCentre), else < 0. An if, not ?:, which in HLSL would evaluate both sides.
    float dens = densIn;
    [branch] if (disc && dens < 0.0) dens = gasDens(rHit, phHit, order);
    // thick disc: the near half of the slab first (it can hide the far side's inner rim)
    bool slab = disc && _Thick > 0.5;
    if (slab)
    {
        float Dv = max(rHit * rHit - 2.0 * M * rHit + a * a, 1e-4);
        float rpd = -prHit * Dv;                                        // the backward ray's dr/dλ
        float phd = -(a * (rHit * rHit + a * a - a * b) / Dv - a + b);  // and dφ/dλ, at θ = π/2
        float3 v = normalize(float3(rHit * rpd / sqrt(Dv), rHit * thd, rHit * phd));
        col += slabColor(rHit, phHit, b, q, EF, v, dens, trans, spotLum);
    }
    // the hot spot in the midplane (in a thick disc it is in the slab instead)
    if (_SpotOn > 0.5 && !slab)
    {
        float w;
        float3 c = spotColor(rHit, phHit, b, q, EF, w);
        col += trans * c;
        spotLum += trans * dot(c, float3(0.2126, 0.7152, 0.0722));
        if (_Disk < 0.5) trans *= 1.0 - w;
    }
    if (disc)
    {
        float al;
        float dl;
        float3 c = diskColor(rHit, phHit, b, q, EF, order, dens, al, dl);
        if (firstHit == 0.0) { firstHit = rHit; firstG = dl; }
        col += trans * c;
        trans *= 1.0 - al;
    }
}

// ---- the ray ---------------------------------------------------------------------------------------------

// A pixel direction in the camera's frame → the photon's direction n in the local ZAMO frame (the light travels
// towards the camera), with the aberration of a moving camera; boost = E_camera / E_ZAMO.
void zamoPhoton(float3 rd, out float3 n, out float boost)
{
    float3 dc = normalize(float3(dot(rd, _CamEr), dot(rd, _CamEth), dot(rd, _CamEph)));
    n = -dc;
    boost = 1.0;
    if (_CamVel.w > 1.0001)
    {
        float3 v = _CamVel.xyz; float gam = _CamVel.w;
        float vv = max(dot(v, v), 1e-12);
        float k0 = gam * (1.0 + dot(v, n));
        float3 kv = n + ((gam - 1.0) * dot(v, n) / vv + gam) * v;
        n = kv / k0;
        boost = 1.0 / k0;
    }
}

// → the photon's conserved b and q (Bardeen 1973; James et al. 2015), the energy EF the camera measures
// (E = 1 at infinity) and the backward ray's start state (r, dr/dλ, θ, dθ/dλ).
void photon(float3 n, float boost, float M, out float b, out float q, out float EF, out float4 s)
{
    float a = _Spin;
    float rc = _CamBL.x, muc = _CamBL.y, stc = _CamBL.w;
    float lapse = _CamMet.x, omg = _CamMet.y, varpi = _CamMet.z, rho = _CamMet.w, Dc = _CamDelta;
    // lensing off: the same coordinates with M = 0 are flat space, so the camera frame is flat too (no lapse,
    // no frame dragging, flat Δ) and each pixel's ray is the straight line in its own direction
    if (M < 0.5) { lapse = 1.0; omg = 0.0; varpi = sqrt(rc * rc + a * a) * stc; Dc = rc * rc + a * a; }
    float EZ = 1.0 / (lapse + omg * varpi * n.z);
    EF = EZ * boost;
    float pr0 = EZ * rho * n.x / sqrt(Dc);
    float pth = EZ * rho * n.y;
    b = EZ * varpi * n.z;
    // b = 0 exactly would run through the pole itself; a nudge of 1e-4 M moves the ray far less than a pixel
    b = (b < 0.0 ? -1.0 : 1.0) * max(abs(b), 1e-4);
    q = pth * pth + muc * muc * (b * b / max(stc * stc, 1e-6) - a * a);
    s = float4(rc, -Dc * pr0, _CamTheta, -pth);
}

// One backward ray. Full mode (table = false) shades every crossing and body on the way and stops on an opaque
// one. Table mode, for the lens table's build pass, shades nothing: every plane is on, no body is tested, and
// it records the first three equator crossings (x0..x2: r, φ − φ_camera, p_r, dθ/dλ) and the radial range of
// the path. Both take the same steps, so a table cell holds what the full trace of that direction sees.
struct Ray
{
    float3 col; float trans, spotLum, firstHit, firstG;
    float4 s; float ph; int steps; bool escaped, fallbackSky;
    int reason; // 0 capture, 1 confirmed escape, 2 unresolved (budget exhausted, or a forbidden plunge at the retry floor), 3 opaque hit, 4 numerical failure
    float count; float4 x0, x1, x2; float rmin, rmax;
};

Ray march(float b, float q, float EF, float4 s, float a, float M, bool table)
{
    Ray R;
    R.col = 0; R.trans = 1.0; R.spotLum = 0; R.firstHit = 0; R.firstG = 0;
    R.x0 = 0; R.x1 = 0; R.x2 = 0; R.rmin = s.x; R.rmax = s.x;
    float ph0 = _CamBL.z, ph = ph0;
    float rCap = _RH + 1e-3;
    float projR = 1e-3, projT = 1e-3 * (q + b * b + 1.0);
    // Keep the polar coordinate measured from the NEARER pole. Storing pi-epsilon in float loses the small angle
    // at the south pole, corrupting the sharp b/sin(theta)^2 azimuth swing even as the step size tends to zero.
    // The local polar equation is symmetric between hemispheres; convert back only for geometry/output.
    bool south = s.z > UNITY_HALF_PI;
    if (south) { s.z = UNITY_PI - s.z; s.w = -s.w; }
    bool plane = table || _Disk > 0.5 || _DebrisRange.z > 0.5 || _GuideOn > 0.5 || _SpotOn > 0.5;
    float3 pPrev = 0;
    float crossings = 0.0; int stepsUsed = 0;
    bool escaped = false;
    int reason = 2;
    // A ray that can fall in has R > 0 all the way down to the horizon. If R(rCap) < 0 it has a turning
    // point outside, and a step that lands inside rCap overshot it: retry that step smaller (QA-034).
    bool canFall = Rpot(rCap, b, q, a, M) > 0.0;
    float hScale = 1.0;
    [loop] for (int k = 0; k < _Steps; k++)
    {
        stepsUsed = k + 1;
        float p1, p2, p3, p4;
        float4 k1 = deriv(s, b, q, a, M, p1);
        // An exactly meridional ray can cross the coordinate pole: forcing h proportional to sin(theta) otherwise
        // strands it there forever. Other near-polar rays need a tighter azimuth step to converge their sky direction.
        bool meridian = abs(b) < 1e-6;
        float polar = meridian ? 1.0 : max(abs(sin(s.z)), 1e-7);
        float stepK = abs(sin(s.z)) < 0.1 ? min(_StepK, 0.1) : _StepK;
        float h = hScale * stepK / (abs(s.y) / s.x + sqrt(abs(k1.y) / s.x) + abs(p1) + abs(s.w) / polar + sqrt(abs(k1.w)));
        float4 k2 = deriv(s + 0.5 * h * k1, b, q, a, M, p2);
        float4 k3 = deriv(s + 0.5 * h * k2, b, q, a, M, p3);
        float4 k4 = deriv(s + h * k3, b, q, a, M, p4);
        float4 sn = s + h / 6.0 * (k1 + 2.0 * k2 + 2.0 * k3 + k4);
        float phn = ph + h / 6.0 * (p1 + 2.0 * p2 + 2.0 * p3 + p4);
        if (any(!isfinite(sn)) || !isfinite(phn)) { reason = 4; break; }
        if (meridian && sn.z < 0.0) { sn.z = -sn.z; sn.w = -sn.w; phn += UNITY_PI; }
        if (sn.x < rCap && !canFall && hScale > 1e-3) { hScale *= 0.25; continue; }
        hScale = min(hScale * 2.0, 1.0);
        // hold the first integrals r'² = R(r) and θ'² = Θ(θ) (except near turning points) so errors don't drift
        float Rn = Rpot(sn.x, b, q, a, M);
        if (Rn > projR * sn.x * sn.x * sn.x * sn.x) sn.y = (sn.y < 0.0 ? -1.0 : 1.0) * sqrt(Rn);
        float Tn = Theta(sn.z, b, q, a);
        if (Tn > projT) sn.w = (sn.w < 0.0 ? -1.0 : 1.0) * sqrt(Tn);
        R.rmin = min(R.rmin, sn.x); R.rmax = max(R.rmax, sn.x);

        // solid bodies along this segment (straight chord: segments are short where bending is strong)
        float tBody = 2.0; float3 nrm = 0; int hitI = -1; float3 pNext = 0;
        bool nearBody = false;
        if (!table && _NB > 0)
        {
            // only steps whose radial span reaches a body pay for the Cartesian chord (audit #62)
            float rlo = min(s.x, sn.x), rhi = max(s.x, sn.x);
            [loop] for (int bj = 0; bj < _NB; bj++) nearBody = nearBody || (_BRad[bj].x + _BRad[bj].y >= rlo && _BRad[bj].x - _BRad[bj].y <= rhi);
        }
        if (nearBody)
        {
            pPrev = toCart(s.x, south ? UNITY_PI - s.z : s.z, ph, a);
            pNext = toCart(sn.x, south ? UNITY_PI - sn.z : sn.z, phn, a);
            [loop] for (int bi = 0; bi < _NB; bi++)
            {
                float3 nb;
                float tb = hitBody(bi, pPrev, pNext, nb);
                if (tb < tBody) { tBody = tb; nrm = nb; hitI = bi; }
            }
        }

        float e0 = s.z - UNITY_HALF_PI, e1 = sn.z - UNITY_HALF_PI;
        // a step can land exactly on the equator in float (e = 0): a sign test counts that crossing once
        if (plane && ((e0 > 0.0) != (e1 > 0.0)))
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
                rHit = clamp(rHit, min(s.x, sn.x), max(s.x, sn.x));   // the cubic can overshoot on a long step
                float phHit = lerp(ph, phn, t);
                float Dh = max(rHit * rHit - 2.0 * M * rHit + a * a, 1e-4);
                float prHit = -lerp(s.y, sn.y, t) / Dh;   // the physical photon's p_r
                float thd = lerp(s.w, sn.w, t) * (south ? -1.0 : 1.0);
                if (table)
                {
                    float4 X = float4(rHit, phHit - ph0, prHit, thd);
                    if (crossings < 0.5) R.x0 = X; else if (crossings < 1.5) R.x1 = X; else if (crossings < 2.5) R.x2 = X;
                }
                else
                {
                    shadeCrossing(rHit, phHit, prHit, thd, b, q, EF, M, crossings, -1.0, R.col, R.trans, R.spotLum, R.firstHit, R.firstG);
                    if (R.trans < 0.01) { reason = 3; break; }
                }
            }
            crossings += 1.0;
        }
        if (hitI >= 0)
        {
            float rB = lerp(s.x, sn.x, tBody);
            float Db = max(rB * rB - 2.0 * M * rB + a * a, 1e-4);
            float prB = -lerp(s.y, sn.y, tBody) / Db;
            R.col += R.trans * bodyColor(hitI, nrm, normalize(pNext - pPrev), b, prB, EF);
            R.trans = 0;
            reason = 3;
            break;
        }
        if (sn.z > UNITY_HALF_PI) { sn.z = UNITY_PI - sn.z; sn.w = -sn.w; south = !south; }
        s = sn; ph = phn;
        // A forbidden plunge after the retry floor is unresolved, never a confirmed capture.
        if (s.x < rCap) { reason = canFall ? 0 : 2; break; }
        if (s.x > _RMax && s.y > 0.0) { escaped = true; reason = 1; break; }
    }
    // Display-only fallback: an unfinished ray can still sample the sky, but it is never a confirmed escape.
    // Diagnostics and lens tables retain the unresolved state; its current direction is not asymptotic evidence.
    R.fallbackSky = reason == 2 && R.trans > 0.01 && (!canFall || (s.x >= rCap * 1.05 && s.y > 0.0));
    R.reason = reason;
    if (south) { s.z = UNITY_PI - s.z; s.w = -s.w; }
    R.s = s; R.ph = ph; R.steps = stepsUsed; R.escaped = escaped; R.count = crossings;
    return R;
}

float4 traceReasonColour(float reason, bool fallbackSky)
{
    if (reason < 0.5) return float4(1, 0, 0, 1); // capture
    if (reason < 1.5) return float4(0, 1, 0, 1); // confirmed escape
    if (reason < 2.5) return fallbackSky ? float4(0, 1, 1, 1) : float4(0, 0, 1, 1); // unresolved
    if (reason < 3.5) return float4(1, 1, 1, 1); // opaque
    return float4(1, 0, 1, 1); // numerical failure
}

// The sky seen along an escaping direction, weight w (0 = none). dir's screen-space differentials pick the
// filter footprint, so every pixel of the 2×2 quad must call this (outside any branch). Where a neighbour did
// not reach the sky its direction means nothing: fall back to the plain pixel footprint.
void skyGrad(float3 dir, float w, out float3 ddxDir, out float3 ddyDir)
{
    float ok = w > 0.0 ? 1.0 : 0.0;
    ddxDir = ddx(dir); ddyDir = ddy(dir);
    bool diffOk = abs(ddx(ok)) < 0.5 && abs(ddy(ok)) < 0.5;
    if (!diffOk || !(length(ddxDir) < 0.5) || !(length(ddyDir) < 0.5)) { ddxDir = 0; ddyDir = 0; }
}

// The same with the differentials given (callable inside a branch).
float3 skyTermGrad(float3 dir, float w, float EF, float px, float3 ddxDir, float3 ddyDir)
{
    if (!(w > 0.0)) return 0;
    // the sky is blueshifted by g = EF for this observer: bolometric intensity × g⁴, and the colour shift of a
    // 6500 K source seen at g × 6500 K
    float gs = clamp(EF, 0.05, 20.0);
    float3 tint = planckRGB(6500.0 * clamp(gs, 0.25, 4.0)) / planckRGB(6500.0);
    return w * skyAGrad(dir, ddxDir, ddyDir, px, _HDROut > 0.5) * (_HDROut > 0.5 ? _SkyGain : 1.0) * tint * gs * gs * gs * gs;
}

float3 skyTerm(float3 dir, float w, float EF, float px)
{
    float3 dx, dy;
    skyGrad(dir, w, dx, dy);
    return skyTermGrad(dir, w, EF, px, dx, dy);
}

// ---- lens table (visuals step 2) ---------------------------------------------------------------------------
// Per camera, a 1024² table of what the rays do, rebuilt only when the camera's r or θ, the spin, lensing or
// the quality change (BlackHoleScene.Lens.cs); turning the camera, the disc's swirl, debris and the hot spot
// need no rebuild. Cells are directions d in the camera's ZAMO frame: ψ around the direction to the hole and ρ
// away from it, ρ warped logarithmically about the shadow edge ρc(ψ) (computed on the CPU from the photon-orbit
// family) so cells crowd the critical curve and none straddles capture and escape. One table covers every sign
// of b and of the initial p_r and p_θ.
//   T0: reason × 8 + crossings (max 4; 0 capture, 1 escape, 2 unresolved (budget exhausted, or a forbidden plunge at the retry floor), 3 opaque, 4 numerical), direction at camera φ = 0
//   T1..T3: the first three equator crossings (r, φ − φ_camera, p_r, dθ/dλ)
//   T4: xy radial range of the path (for bodies), z steps used, w display-only fallbackSky flag
Texture2D<float4> _LensT0, _LensT1, _LensT2, _LensT3, _LensT4;
Texture2D<float> _LensEdge;    // ρc at the centres of _LensN bins over ψ
float _LensOn, _LensN;
float _LensSpp;                // base samples per pixel before refinement: 1 (or 2)
float _LensEdgeTest;           // refinement's edge test: 1 cells at the pixel's corners, 2 codes across the 2 x 2 quad, 0 none
float _LensNoTrace;            // review harness: pass 1 draws fallback pixels black instead of tracing them
#define LENS_EPS 2e-4
#define LENS_GRAZE 0.05
float _LensPoleB;              // M: rays this close over a pole take the full trace (0.6; step 2 gate: the stripe reached |b| = 0.56)

float lensEdge(float psi)
{
    int N = (int)_LensN;
    float x = psi / UNITY_TWO_PI * _LensN - 0.5;
    float fl = floor(x);
    int i0 = (int)fl;
    i0 = i0 < 0 ? i0 + N : i0 >= N ? i0 - N : i0;     // ψ ∈ [0, 2π): at most one turn out
    int i1 = i0 + 1 >= N ? 0 : i0 + 1;
    return lerp(_LensEdge.Load(int3(i0, 0, 0)), _LensEdge.Load(int3(i1, 0, 0)), x - fl);
}

// table row coordinate v ∈ [0, 1] ↔ ρ: 0.5 is the shadow edge, 0 the direction to the hole, 1 straight away
float lensV(float rho, float rc)
{
    return rho < rc ? 0.5 * (1.0 - log(1.0 + (rc - rho) / LENS_EPS) / log(1.0 + rc / LENS_EPS))
                    : 0.5 + 0.5 * log(1.0 + (rho - rc) / LENS_EPS) / log(1.0 + (UNITY_PI - rc) / LENS_EPS);
}

float lensRho(float v, float rc)
{
    return v < 0.5 ? rc - LENS_EPS * (exp((1.0 - 2.0 * v) * log(1.0 + rc / LENS_EPS)) - 1.0)
                   : rc + LENS_EPS * (exp((2.0 * v - 1.0) * log(1.0 + (UNITY_PI - rc) / LENS_EPS)) - 1.0);
}

// something is drawn in the plane at radius r
bool planeDrawn(float r)
{
    float lr = log(r);
    return _GuideOn > 0.5 || (_DebrisRange.z > 0.5 && lr >= _DebrisRange.x && lr <= _DebrisRange.y)
        || (_Disk > 0.5 && r > _Isco && r * 0.5 < _Outer) || (_SpotOn > 0.5 && abs(r - _SpotR) < 4.0 * _SpotSigma);
}

// cosine of the crossing from the disc normal in the gas frame (orbiting gas outside the ISCO, a ZAMO inside)
float crossingMu(float r, float b, float q)
{
    float a = _Spin;
    float D = max(r * r - 2.0 * r + a * a, 1e-4);
    float S2 = (r * r + a * a) * (r * r + a * a) - a * a * D;
    float E = (1.0 - 2.0 * a * r / S2 * b) / sqrt(r * r * D / S2);
    if (r > _Isco)
    {
        float sr = sqrt(r);
        float om = 1.0 / (r * sr + a);
        float ut = (r * sr + a) / (sqrt(sr * r) * sqrt(max(r * sr - 3.0 * sr + 2.0 * a, 1e-5)));
        E = ut * (1.0 - om * b);
    }
    return sqrt(max(q, 0.0)) / (r * max(E, 1e-4));
}

// The table cells around the view direction d (ZAMO frame): columns i0, i1 (wrapping in ψ), rows j0 and j0 + 1, and
// the bilinear fractions.
void lensCells(float3 d, out int i0, out int i1, out int j0, out float fx, out float fy)
{
    float psi = atan2(d.z, d.y);
    if (psi < 0.0) psi += UNITY_TWO_PI;
    float rho = atan2(length(d.yz), -d.x);
    float x = psi / UNITY_TWO_PI * _LensN - 0.5, y = lensV(rho, lensEdge(psi)) * _LensN - 0.5;
    float fi = floor(x);
    float fj = clamp(floor(y), 0.0, _LensN - 2.0);
    fx = x - fi; fy = saturate(y - fj);
    int N = (int)_LensN;
    i0 = (int)fi;
    i0 = i0 < 0 ? i0 + N : i0 >= N ? i0 - N : i0;    // ψ ∈ [0, 2π): at most one turn out
    i1 = i0 + 1 >= N ? 0 : i0 + 1;
    j0 = (int)fj;
}

// The escape direction at view direction d from the table (world, camera φ applied); false unless all four cells
// escape. The image pass takes the sky's filter footprint from these at neighbouring pixels.
bool lensSkyDir(float3 d, out float3 dir)
{
    int i0, i1, j0; float fx, fy;
    lensCells(d, i0, i1, j0, fx, fy);
    float4 a00 = _LensT0.Load(int3(i0, j0, 0)), a10 = _LensT0.Load(int3(i1, j0, 0));
    float4 a01 = _LensT0.Load(int3(i0, j0 + 1, 0)), a11 = _LensT0.Load(int3(i1, j0 + 1, 0));
    float4 w = float4((1.0 - fx) * (1.0 - fy), fx * (1.0 - fy), (1.0 - fx) * fy, fx * fy);
    float3 e = w.x * a00.yzw + w.y * a10.yzw + w.z * a01.yzw + w.w * a11.yzw;
    float sp, cp; sincos(_CamBL.z, sp, cp);
    dir = normalize(float3(e.x * cp - e.z * sp, e.y, e.x * sp + e.z * cp));
    return all(floor(float4(a00.x, a10.x, a01.x, a11.x) / 8.0) == 1.0);
}

// The fate × 8 + count code of the cell at view direction d (adaptive sampling's edge test).
float lensCode(float3 d)
{
    int i0, i1, j0; float fx, fy;
    lensCells(d, i0, i1, j0, fx, fy);
    return _LensT0.Load(int3(fx < 0.5 ? i0 : i1, fy < 0.5 ? j0 : j0 + 1, 0)).x;
}

float4 lensReasonColour(float3 d)
{
    int i0, i1, j0; float fx, fy;
    lensCells(d, i0, i1, j0, fx, fy);
    int3 cell = int3(fx < 0.5 ? i0 : i1, fy < 0.5 ? j0 : j0 + 1, 0);
    return traceReasonColour(floor(_LensT0.Load(cell).x / 8.0), _LensT4.Load(cell).w > 0.5);
}

// The four cells' fate × 8 + count codes.
float4 lensCodes(int i0, int i1, int j0)
{
    return float4(_LensT0.Load(int3(i0, j0, 0)).x, _LensT0.Load(int3(i1, j0, 0)).x,
                  _LensT0.Load(int3(i0, j0 + 1, 0)).x, _LensT0.Load(int3(i1, j0 + 1, 0)).x);
}

// Crossing k of those cells, interpolated (r, φ − φ_camera, p_r, dθ/dλ). Azimuths are unwrapped against the first
// cell (two rays either side of a pole differ by ~2π); false if the cells still disagree by more than a radian.
bool lensCrossing(int k, int i0, int i1, int j0, float fx, float fy, out float4 X)
{
    int3 c00 = int3(i0, j0, 0), c10 = int3(i1, j0, 0), c01 = int3(i0, j0 + 1, 0), c11 = int3(i1, j0 + 1, 0);
    float4 w = float4((1.0 - fx) * (1.0 - fy), fx * (1.0 - fy), (1.0 - fx) * fy, fx * fy);
    float4 x00, x10, x01, x11;
    if (k == 0) { x00 = _LensT1.Load(c00); x10 = _LensT1.Load(c10); x01 = _LensT1.Load(c01); x11 = _LensT1.Load(c11); }
    else if (k == 1) { x00 = _LensT2.Load(c00); x10 = _LensT2.Load(c10); x01 = _LensT2.Load(c01); x11 = _LensT2.Load(c11); }
    else { x00 = _LensT3.Load(c00); x10 = _LensT3.Load(c10); x01 = _LensT3.Load(c01); x11 = _LensT3.Load(c11); }
    float3 dp = float3(x10.y, x01.y, x11.y) - x00.y;
    dp -= UNITY_TWO_PI * round(dp / UNITY_TWO_PI);
    X = w.x * x00 + w.y * x10 + w.z * x01 + w.w * x11;
    X.y = x00.y + dot(w.yzw, dp);
    return all(abs(dp) <= 1.0);
}

// The swirl texture is the costly part of shading a crossing and barely changes across one pixel, so the table pass
// evaluates it once per pixel, at the pixel's (jittered) centre, for each of its first three crossings; the pixel's
// samples share it where their crossing lies close to the centre's, while the flux, redshift, edges and sky stay per
// sample. Per crossing: r, φ − φ_camera and the density (< 0: nothing to share).
void lensCentre(float3 d, out float3 Cr, out float3 Cph, out float3 Cd)
{
    Cr = 0; Cph = 0; Cd = -1;
    if (_Disk < 0.5 || _Clumpy < 0.5) return;
    int i0, i1, j0; float fx, fy;
    lensCells(d, i0, i1, j0, fx, fy);
    float4 codes = lensCodes(i0, i1, j0);
    if (any(codes != codes.x)) return;
    float cnt = codes.x - floor(codes.x / 8.0) * 8.0;
    [loop] for (int k = 0; k < 3; k++)
    {
        if (k >= cnt) break;
        float4 X;
        if (!lensCrossing(k, i0, i1, j0, fx, fy, X) || X.x <= _Isco) continue;
        float g = gasDens(X.x, X.y + _CamBL.z, k);
        if (k == 0) { Cr.x = X.x; Cph.x = X.y; Cd.x = g; }
        else if (k == 1) { Cr.y = X.x; Cph.y = X.y; Cd.y = g; }
        else { Cr.z = X.x; Cph.z = X.y; Cd.z = g; }
    }
}

// One pixel sample from the table, for the view direction d (ZAMO frame) of a photon with b, q, EF: the light of its
// crossings into col (spotLum: the hot spot's share), the transmission left, and the sky direction with skyOk.
// Returns false where the table can't stand in for the full trace: the four cells around d disagree on fate or
// crossing count, a body may lie on the path, a drawn crossing is grazing (μ < 0.05), the photon is near-equatorial
// (|q| tiny) or passes close over a pole (|b| small: there its escape direction swings faster than the table
// follows), or more than three crossings are needed before the ray is stopped. Cr, Cph, Cd: lensCentre's.
bool lensSample(float3 d, float b, float q, float EF, float3 Cr, float3 Cph, float3 Cd, inout float3 col, out float trans,
                inout float spotLum, out float3 dir, out float skyOk, out float code)
{
    trans = 1.0; dir = float3(0, 0, 1); skyOk = 0; code = -1;
    if (abs(q) < 1e-3 || abs(b) < _LensPoleB) return false;
    int i0, i1, j0; float fx, fy;
    lensCells(d, i0, i1, j0, fx, fy);
    float4 codes = lensCodes(i0, i1, j0);
    float4 fates = floor(codes / 8.0);
    bool plane = _Disk > 0.5 || _DebrisRange.z > 0.5 || _GuideOn > 0.5 || _SpotOn > 0.5;
    if (plane ? any(codes != codes.x) : any(fates != fates.x)) return false;
    float fate = fates.x, cnt = codes.x - fate * 8.0;
    if (fate >= 2.0) return false; // an unresolved/numerically failed cell is not trusted as a resolved lens sample
    code = codes.x;
    if (_NB > 0)
    {
        float4 r00 = _LensT4.Load(int3(i0, j0, 0)), r10 = _LensT4.Load(int3(i1, j0, 0));
        float4 r01 = _LensT4.Load(int3(i0, j0 + 1, 0)), r11 = _LensT4.Load(int3(i1, j0 + 1, 0));
        float rlo = min(min(r00.x, r10.x), min(r01.x, r11.x)), rhi = max(max(r00.y, r10.y), max(r01.y, r11.y));
        [loop] for (int bj = 0; bj < _NB; bj++)
            if (_BRad[bj].x + _BRad[bj].y >= rlo && _BRad[bj].x - _BRad[bj].y <= rhi) return false;
    }
    if (plane)
    {
        float fh = 0, fg = 0;
        [loop] for (int k = 0; k < 3; k++)
        {
            if (k >= cnt || trans < 0.01) break;
            float4 X;
            if (!lensCrossing(k, i0, i1, j0, fx, fy, X)) return false;
            if (planeDrawn(X.x) && crossingMu(X.x, b, q) < LENS_GRAZE) return false;
            float cr = k == 0 ? Cr.x : k == 1 ? Cr.y : Cr.z;
            float cph = k == 0 ? Cph.x : k == 1 ? Cph.y : Cph.z;
            float cd = k == 0 ? Cd.x : k == 1 ? Cd.y : Cd.z;
            float dph = X.y - cph;
            dph -= UNITY_TWO_PI * round(dph / UNITY_TWO_PI);
            float dens = cd >= 0.0 && abs(X.x - cr) < 0.02 * X.x && abs(dph) < 0.05 ? cd : -1.0;
            shadeCrossing(X.x, X.y + _CamBL.z, X.z, X.w, b, q, EF, 1.0, k, dens, col, trans, spotLum, fh, fg);
        }
        if (cnt > 3.5 && trans >= 0.01) return false;
    }
    if (fate == 1.0 && trans > 0.01)
    {
        float4 w = float4((1.0 - fx) * (1.0 - fy), fx * (1.0 - fy), (1.0 - fx) * fy, fx * fy);
        float3 e = w.x * _LensT0.Load(int3(i0, j0, 0)).yzw + w.y * _LensT0.Load(int3(i1, j0, 0)).yzw
                 + w.z * _LensT0.Load(int3(i0, j0 + 1, 0)).yzw + w.w * _LensT0.Load(int3(i1, j0 + 1, 0)).yzw;
        float sp, cp; sincos(_CamBL.z, sp, cp);
        dir = normalize(float3(e.x * cp - e.z * sp, e.y, e.x * sp + e.z * cp));
        skyOk = 1;
    }
    return true;
}

#endif
