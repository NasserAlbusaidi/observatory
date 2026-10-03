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
float3 skyTex(sampler2D s, float3 d)
{
    float3 c = tex2Dlod(s, float4(SkyUV(mul((float3x3)_SkyRot, d)), 0, 0)).rgb;
#ifdef UNITY_COLORSPACE_GAMMA
    c = pow(max(c, 0), 2.2);   // tonemap() re-applies the 1/2.2 in gamma projects
#endif
    c = min(c, 0.985);
    return -log(1.0 - c) / 1.15;
}

float3 skyA(float3 d, float px) { return _FOWHasSky > 0.5 ? skyTex(_SkyA, d) : skyAProc(d, px); }
float3 skyB(float3 d, float px) { return _FOWHasSky > 0.5 ? skyTex(_SkyB, d) : skyBProc(d, px); }

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
