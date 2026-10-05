// Shared HLSL for Fog of War: noise, blackbody colour, procedural skies, tonemapping.
#ifndef FOW_COMMON_INCLUDED
#define FOW_COMMON_INCLUDED

float hash13(float3 p) { p = frac(p * 0.1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }
float3 hash33(float3 p) { p = frac(p * float3(0.1031, 0.1030, 0.0973)); p += dot(p, p.yxz + 33.33); return frac((p.xxy + p.yxx) * p.zyx); }

float vnoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = lerp(lerp(hash13(i), hash13(i + float3(1, 0, 0)), f.x), lerp(hash13(i + float3(0, 1, 0)), hash13(i + float3(1, 1, 0)), f.x), f.y);
    float b = lerp(lerp(hash13(i + float3(0, 0, 1)), hash13(i + float3(1, 0, 1)), f.x), lerp(hash13(i + float3(0, 1, 1)), hash13(i + float3(1, 1, 1)), f.x), f.y);
    return lerp(a, b, f.z);
}

float fbm(float3 p)
{
    float a = 0.5, s = 0.0;
    [unroll] for (int i = 0; i < 5; i++) { s += a * vnoise(p); p = p * 2.03 + 1.7; a *= 0.5; }
    return s;
}

// Approximate blackbody colour (Tanner Helland fit), t in kelvin.
float3 blackbody(float t)
{
    t = clamp(t, 800.0, 40000.0) / 100.0;
    float r = t <= 66.0 ? 1.0 : saturate(1.29293618606 * pow(abs(t - 60.0), -0.1332047592));
    float g = t <= 66.0 ? saturate(0.39008157876 * log(t) - 0.63184144378) : saturate(1.12989086089 * pow(abs(t - 60.0), -0.0755148492));
    float b = t >= 66.0 ? 1.0 : (t <= 19.0 ? 0.0 : saturate(0.54320678911 * log(t - 10.0) - 1.19625408914));
    return float3(r, g, b);
}

// Point stars on the celestial sphere. px = angular size of one pixel in radians.
float3 starLayer(float3 d, float scale, float density, float px)
{
    float3 p = d * scale;
    float3 c = floor(p);
    float3 s = sign(frac(p) - 0.5);
    float3 col = 0;
    [unroll] for (int k = 0; k < 8; k++)
    {
        float3 o = float3(k & 1, (k >> 1) & 1, (k >> 2) & 1);
        float3 cc = c + o * s;
        float3 h = hash33(cc);
        if (h.z > density) continue;
        float3 sp = normalize(cc + h * 0.8 + 0.1);
        float ang = acos(clamp(dot(sp, d), -1.0, 1.0));
        float w = max(px * 1.1, 0.00035);
        float b = pow(frac(h.x * 17.3), 10.0) * 2.5 + 0.06;
        float3 tint = blackbody(3000.0 + 9000.0 * frac(h.y * 7.1));
        col += tint * b * exp(-ang * ang / (w * w));
    }
    return col;
}

float3 skyStars(float3 d, float px)
{
    return starLayer(d, 70.0, 0.05, px) + starLayer(d, 160.0, 0.025, px) * 0.6;
}

// Our universe (procedural fallback): Milky Way band, dust and a galactic core.
float3 skyAProc(float3 d, float px)
{
    float3 n = normalize(float3(0.25, 1.0, 0.35));
    float lat = dot(d, n);
    float band = exp(-lat * lat * 14.0);
    float dust = fbm(d * 6.0 + 3.0);
    float glow = fbm(d * 3.0) * band;
    float3 col = float3(0.55, 0.5, 0.42) * glow * 0.45 * smoothstep(0.3, 0.8, dust + 0.2)
               + float3(0.12, 0.14, 0.24) * band * 0.12;
    float3 core = normalize(float3(-0.8, -0.1, 0.55));
    float cg = max(dot(d, core), 0.0);
    col += float3(1.0, 0.75, 0.45) * pow(cg, 30.0) * 1.4 * (0.4 + 0.6 * dust);
    col += skyStars(d, px) * (1.0 + band * 2.0);
    return col;
}

// The other universe (procedural fallback): a rose and teal nebula with a spiral galaxy.
float3 skyBProc(float3 d, float px)
{
    float neb = fbm(d * 2.4 + 5.0);
    float neb2 = fbm(d * 5.0 - 2.0);
    float3 col = lerp(float3(0.02, 0.05, 0.08), float3(0.85, 0.25, 0.45), smoothstep(0.42, 0.88, neb)) * 0.9;
    col += float3(0.1, 0.75, 0.7) * smoothstep(0.5, 0.9, neb2) * 0.6;
    float3 gc = normalize(float3(0.3, 0.35, -1.0));
    float3 gx = normalize(cross(gc, float3(0, 1, 0)));
    float3 gy = cross(gx, gc);
    float2 q = float2(dot(d, gx), dot(d, gy)) / max(dot(d, gc), 0.01);
    q.y *= 2.2;
    float r = length(q);
    float a = atan2(q.y, q.x);
    float arms = pow(0.5 + 0.5 * cos(2.0 * a - 9.0 * log(r + 0.02)), 3.0);
    float disk = exp(-r * 9.0);
    col += (float3(0.9, 0.85, 1.0) * arms * disk * 1.6 + float3(1.0, 0.85, 0.6) * exp(-r * 40.0) * 2.0) * step(0.0, dot(d, gc));
    col += skyStars(d, px) * float3(0.9, 0.95, 1.1) * 1.3;
    return col;
}

// ---- Equirectangular maps from the art pack (space-sim-art) ----
// Unity +Y = north (celestial or planetary), longitude/RA 0 at +X, 90° east at +Z. Same lookup for skies and planets.
float2 SkyUV(float3 d) { return float2(0.5 + atan2(d.z, d.x) * 0.15915494, 0.5 + asin(clamp(d.y, -1.0, 1.0)) * 0.31830989); }

// Sample a planet map at object-space direction p without the 1-pixel seam where atan2 wraps: take each derivative
// from whichever of u and u+0.5 is continuous here.
float4 tex2DSphere(sampler2D s, float3 p)
{
    float2 uv = SkyUV(normalize(p));
    float2 uw = float2(frac(uv.x + 0.5), uv.y);
    float2 dx = ddx(uv), dy = ddy(uv), dxw = ddx(uw), dyw = ddy(uw);
    dx.x = abs(dx.x) < abs(dxw.x) ? dx.x : dxw.x;
    dy.x = abs(dy.x) < abs(dyw.x) ? dy.x : dyw.x;
    return tex2Dgrad(s, uv, dx, dy);
}

// Relief normal from a tangent-space normal map (east = +x, north = +y), in object space for unit-sphere point p.
// Only red and green are read and z is rebuilt, so BC5 (two-channel) maps work.
float3 sphereNormal(sampler2D s, float3 p)
{
    float2 xy = tex2DSphere(s, p).rg * 2.0 - 1.0;
    float z = sqrt(saturate(1.0 - dot(xy, xy)));
    float3 east = normalize(float3(-p.z, 0.0, p.x) + float3(1e-5, 0, 0));
    float3 north = cross(east, p);
    return normalize(xy.x * east + xy.y * north + max(z, 0.2) * p);
}

// A shape model's baked normal map (Blender/lods.py: tangent space, MikkTSpace, OpenGL +Y; imported BC5, so only RG
// arrive). n: world normal; t: world tangent with the bitangent sign in w. Off (has < 0.5): n unchanged.
float3 lodNormal(sampler2D s, float has, float3 n, float4 t, float2 uv)
{
    if (has < 0.5) return n;
    float2 xy = tex2D(s, uv).rg * 2.0 - 1.0;
    float z = sqrt(saturate(1.0 - dot(xy, xy)));
    float3 tw = normalize(t.xyz - n * dot(t.xyz, n));
    float3 bw = cross(n, tw) * (t.w < 0.0 ? -1.0 : 1.0);
    return normalize(xy.x * tw + xy.y * bw + z * n);
}

// A colour texture sampled by the GPU is already linear in Linear projects; convert it back to display values so it
// mixes with the rest of the shader maths, which is authored in display space and ends with displayToOutput().
float3 texToDisplay(float3 c)
{
#ifdef UNITY_COLORSPACE_GAMMA
    return c;
#else
    return pow(max(c, 0), 1.0 / 2.2);
#endif
}

// Sky maps arrive as display-ready pixels. The ray tracers add radiance and then tonemap(), so undo the tonemap here
// and an unlensed patch of sky comes out with exactly its original pixel value.
sampler2D _SkyA, _SkyB;
float _FOWHasSky;
float4x4 _SkyRot;   // world direction → sky-map direction, so a scene can choose which part of the real sky sits behind it
float _SkyAW, _SkyBW;   // map widths in texels (Art.BindSky)
// px = angular size of one screen pixel (radians); the mip level makes one texel cover about one pixel, so an 8K sky
// doesn't sparkle in small windows. Explicit lod keeps the RA 12h seam free of derivative artefacts.
float3 skyTex(sampler2D s, float3 d, float px, float w)
{
    float lod = max(0.0, log2(px * w / 6.2831853));
    float3 c = tex2Dlod(s, float4(SkyUV(mul((float3x3)_SkyRot, d)), 0, lod)).rgb;
#ifdef UNITY_COLORSPACE_GAMMA
    c = pow(max(c, 0), 2.2);   // tonemap() re-applies the 1/2.2 in gamma projects
#endif
    c = min(c, 0.985);
    return -log(1.0 - c) / 1.15;
}

float3 skyA(float3 d, float px) { return _FOWHasSky > 0.5 ? skyTex(_SkyA, d, px, _SkyAW) : skyAProc(d, px); }
float3 skyB(float3 d, float px) { return _FOWHasSky > 0.5 ? skyTex(_SkyB, d, px, _SkyBW) : skyBProc(d, px); }

// Sky A through a lensed ray: dx, dy are the screen-space differentials of the arriving direction d. A magnified patch of
// sky stays sharp and a squeezed one is filtered along the squeeze only (tex2Dgrad: anisotropic footprint). The u
// differential takes whichever of u and u + 0.5 is continuous, so the RA 12h seam has no artefact. px is the floor.
// Inverse of the luminance tonemap in BHPost.shader (ACES fit, Narkowicz 2015): the display luminance y → linear x.
float acesInverse(float y)
{
    y = min(y, 0.995);
    float A = 2.43 * y - 2.51, B = 0.59 * y - 0.03, C = 0.14 * y;
    return max((-B - sqrt(max(B * B - 4.0 * A * C, 0.0))) / (2.0 * A), 0.0);
}

// acesInv: the caller tonemaps with BHPost's luminance curve, so undo that one instead of tonemap(): an unlensed patch
// of sky then comes out with its original pixel value either way.
float3 skyAGrad(float3 d, float3 dx, float3 dy, float px, bool acesInv)
{
    if (_FOWHasSky < 0.5) return skyAProc(d, max(max(length(dx), length(dy)), px));
    float3x3 R = (float3x3)_SkyRot;
    float2 uv = SkyUV(mul(R, d));
    float2 ux = SkyUV(mul(R, normalize(d + dx))) - uv, uy = SkyUV(mul(R, normalize(d + dy))) - uv;
    ux.x -= round(ux.x); uy.x -= round(uy.x);
    // the floor: one screen pixel's worth of sky in each direction, as skyTex's isotropic choice
    float m = px / 6.2831853;
    if (length(ux) < m) ux = normalize(ux + 1e-9) * m;
    if (length(uy) < m) uy = normalize(uy + 1e-9) * m;
    float3 c = tex2Dgrad(_SkyA, uv, ux, uy).rgb;
#ifdef UNITY_COLORSPACE_GAMMA
    c = pow(max(c, 0), 2.2);
#endif
    if (acesInv)
    {
        float y = max(dot(c, float3(0.2126, 0.7152, 0.0722)), 1e-6);
        return c * (acesInverse(y) / y);
    }
    c = min(c, 0.985);
    return -log(1.0 - c) / 1.15;
}

// Colour of a blackbody at temperature T (kelvin) in linear sRGB with unit luminance: Planck's law weighted by the
// CIE 1931 2° observer, XYZ → sRGB (D65), out-of-gamut colours desaturated towards white. A 256-texel LUT over
// log10 T from 3 to 6, built on the CPU (BlackHoleScene.PlanckLut); below 1000 K it keeps the 1000 K colour.
sampler2D _PlanckLUT;
float3 planckRGB(float T)
{
    float u = saturate((log10(max(T, 1.0)) - 3.0) / 3.0);
    return tex2Dlod(_PlanckLUT, float4((u * 255.0 + 0.5) / 256.0, 0.5, 0, 0)).rgb;
}

// Luminance of linear sRGB.
float lumaRGB(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

// Filmic-ish exposure curve; output is linear, Unity handles the sRGB write in linear projects.
float3 tonemap(float3 c)
{
    c = 1.0 - exp(-c * 1.15);
#ifdef UNITY_COLORSPACE_GAMMA
    c = pow(c, 1.0 / 2.2);
#endif
    return c;
}

// For shaders whose colours were authored as display (sRGB) values.
float3 displayToOutput(float3 c)
{
#ifdef UNITY_COLORSPACE_GAMMA
    return c;
#else
    return pow(max(c, 0), 2.2);
#endif
}

// Physical light scale for the open world (ShipLighting.cs). _FOWExposure is the camera exposure, 1 = the look the
// shaders were tuned for in full sunlight at 1 AU; every other scene keeps it at 1 (FogOfWarApp). A sunlit surface
// multiplies its output by its own sunlight (irr: 1 at 1 AU, inverse square) times the exposure; self-luminous
// light (city lights, glows) by the exposure alone.
float _FOWExposure;
// Capped at 1: a sunlit surface never shows brighter than fully exposed sunlight, so the extra exposure the camera
// takes in an eclipse lifts the ship and glowing light only, not the sunlit planets in view (min(exposure, 1/irr)).
float fowSunlit(float irr) { return min(irr * _FOWExposure, 1.0); }
// The same scale applied to display-space colour before displayToOutput(), for shaders that add lit and glowing
// terms there: a sum keeps today's look exactly when the scale is 1.
float fowDisplayGain(float k) { return pow(max(k, 0.0), 1.0 / 2.2); }

// Ray-traced fullscreen setup shared by the wormhole and black hole.
float3 _CamPos, _CamRight, _CamUp, _CamFwd;
float _TanFov, _Aspect, _ScreenH;

struct appdataFS { float4 vertex : POSITION; };
struct v2fFS { float4 pos : SV_POSITION; float2 ndc : TEXCOORD0; };

v2fFS vertFullscreen(appdataFS v)
{
    v2fFS o;
    float2 ndc = v.vertex.xy * 2.0;     // Unity's quad spans -0.5..0.5
    o.ndc = ndc;
    o.pos = float4(ndc.x, ndc.y * _ProjectionParams.x, 0.5, 1.0);
    return o;
}

float3 primaryRay(float2 ndc, out float px)
{
    px = 2.0 * _TanFov / _ScreenH;
    return normalize(_CamFwd + _CamRight * ndc.x * _Aspect * _TanFov + _CamUp * ndc.y * _TanFov);
}

#endif

// Airless regolith (Moon, Mercury, rocky moons, asteroids): Lommel–Seeliger, flat to the limb, with a simple
// opposition surge. Scaled ×2 so a sub-solar point seen face-on matches Lambert's 1.
float regolith(float3 n, float3 l, float3 v)
{
    float mu0 = max(dot(n, l), 0.0), mu = max(dot(n, v), 0.0);
    float g = acos(clamp(dot(l, v), -1.0, 1.0));
    return 2.0 * mu0 / (mu0 + mu + 1e-4) * (0.8 + 0.5 * exp(-g / 0.1));
}
