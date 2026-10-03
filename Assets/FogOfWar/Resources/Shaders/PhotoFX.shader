// Photo mode grading and lens effects, applied in OnRenderImage (built-in pipeline).
// Pass 0: composite (depth of field mix, chromatic aberration, exposure, white balance, contrast, saturation, filter,
// vignette, grain, letterbox). Pass 1: disc blur of the source for depth of field, scaled by each pixel's blur size.
Shader "Hidden/FogOfWar/PhotoFX"
{
    Properties { _MainTex ("Source", 2D) = "black" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex, _BlurTex, _DepthTex;
    float4 _MainTex_TexelSize;
    float _Exposure, _Contrast, _Saturation, _Temperature, _Vignette, _Grain, _Chroma, _Filter, _Letterbox, _Seed;
    float _DofOn, _FocusInv, _Aperture, _MaxBlur;

    float luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
    float hash12(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }

    // blur size (0..1) from inverse depth: thin-lens circle of confusion ∝ |1/focus − 1/depth|
    float coc(float2 uv)
    {
        float inv = tex2D(_DepthTex, uv).r; // 1/eye depth; 0 = sky
        return saturate(abs(inv - _FocusInv) / max(_FocusInv, 1e-9) * _Aperture);
    }

    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

    float4 fragBlur(v2f i) : SV_Target
    {
        float c = coc(i.uv);
        float r = c * _MaxBlur;
        float3 acc = tex2D(_MainTex, i.uv).rgb; float w = 1;
        // 2 rings of a golden-angle disc
        [unroll] for (int k = 0; k < 24; k++)
        {
            float a = k * 2.39996;
            float rr = sqrt((k + 0.5) / 24.0) * r;
            float2 o = float2(cos(a), sin(a)) * rr * _MainTex_TexelSize.xy;
            float2 uv = i.uv + o;
            // don't pull sharp in-focus foreground into a blurred background
            float ws = saturate(coc(uv) * 2 + 0.15);
            acc += tex2D(_MainTex, uv).rgb * ws; w += ws;
        }
        return float4(acc / w, 1);
    }

    float3 filterColour(float3 g, float f)
    {
        float y = luma(g);
        if (f < 0.5) return g;
        if (f < 1.5) { float n = saturate((y - 0.5) * 1.35 + 0.5); return n.xxx; }                       // noir
        if (f < 2.5) return lerp(y.xxx, y * float3(1.07, 0.92, 0.72) + float3(0.04, 0.02, 0), 0.9);     // sepia print
        if (f < 3.5) return lerp(g, y * float3(0.72, 0.9, 1.08), 0.55);                                   // cold steel
        if (f < 4.5)                                                                                       // teal and orange
        {
            float3 sh = float3(0.0, 0.10, 0.14), hi = float3(0.14, 0.05, -0.06);
            return saturate(g + lerp(sh, hi, smoothstep(0.15, 0.75, y)) * 0.8);
        }
        if (f < 5.5) { float3 b = lerp(y.xxx, g, 0.35); return saturate((b - 0.5) * 1.3 + 0.5); }          // bleach bypass
        if (f < 6.5)                                                                                       // thermal
        {
            float3 c0 = float3(0.02, 0.0, 0.12), c1 = float3(0.55, 0.0, 0.55), c2 = float3(1.0, 0.45, 0.0), c3 = float3(1, 1, 0.75);
            return y < 0.33 ? lerp(c0, c1, y / 0.33) : y < 0.66 ? lerp(c1, c2, (y - 0.33) / 0.33) : lerp(c2, c3, (y - 0.66) / 0.34);
        }
        return g * float3(0.95, 1.0, 0.9) * 0.9 + float3(0.06, 0.05, 0.07);                              // faded film
    }

    float4 fragComp(v2f i) : SV_Target
    {
        float2 uv = i.uv;
        float2 d = uv - 0.5;
        // chromatic aberration grows toward the edges
        float2 ca = d * dot(d, d) * _Chroma * 0.06;
        float3 c;
        c.r = tex2D(_MainTex, uv - ca).r;
        c.g = tex2D(_MainTex, uv).g;
        c.b = tex2D(_MainTex, uv + ca).b;
        if (_DofOn > 0.5)
        {
            float k = smoothstep(0.02, 0.35, coc(uv));
            c = lerp(c, tex2D(_BlurTex, uv).rgb, k);
        }
        c *= exp2(_Exposure);
        // white balance: positive is warmer
        c *= float3(1 + 0.1 * _Temperature, 1, 1 - 0.1 * _Temperature);
        // grade in display space
        float3 g = LinearToGammaSpace(max(c, 0));
        g = (g - 0.5) * _Contrast + 0.5;
        g = lerp(luma(g).xxx, g, _Saturation);
        g = filterColour(saturate(g), _Filter);
        float v = 1 - _Vignette * smoothstep(0.25, 0.95, length(d * float2(1.1, 1)) * 1.25);
        g *= v;
        g += (hash12(uv * _MainTex_TexelSize.zw + _Seed) - 0.5) * _Grain * 0.18;
        c = GammaToLinearSpace(saturate(g));
        // letterbox to a target aspect ratio (0 = off)
        if (_Letterbox > 0)
        {
            float aspect = _MainTex_TexelSize.z / _MainTex_TexelSize.w;
            if (_Letterbox < aspect) { float w = _Letterbox / aspect; if (abs(d.x) > w * 0.5) c = 0; }
            else { float h = aspect / _Letterbox; if (abs(d.y) > h * 0.5) c = 0; }
        }
        return float4(c, 1);
    }
    ENDCG
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragComp
            ENDCG }
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragBlur
            ENDCG }
    }
}
