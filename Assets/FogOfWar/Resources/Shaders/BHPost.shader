// Black hole post pass (BHPost.cs), on the linear radiance BlackHole.shader writes when _HDROut = 1:
//   0  temporal accumulation: blend this jittered frame into the history (optionally clamped to its 3×3 neighbourhood
//      in YCoCg, for a moving camera or a changing scene)
//   1  first glare downsample (13 taps), 2 further downsamples, 3 tent upsample added onto the next level
//   4  combine and tonemap: energy-conserving glare (a few percent of the light spread into a tight core and a wide
//      halo, no threshold), then one tonemap on luminance (ACES fit) with highlights desaturated towards white
Shader "Hidden/FogOfWar/BHPost"
{
    Properties { _MainTex ("", 2D) = "black" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex, _HistTex, _CoreTex, _WideTex;
    float4 _MainTex_TexelSize;
    float _Alpha, _Clamp, _Levels;
    float4 _Glare;     // x core weight, y wide weight
    float _PostExposure;
    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

    float3 toYCoCg(float3 c) { return float3(dot(c, float3(0.25, 0.5, 0.25)), dot(c, float3(0.5, 0, -0.5)), dot(c, float3(-0.25, 0.5, -0.25))); }
    float3 fromYCoCg(float3 y) { return float3(y.x + y.y - y.z, y.x + y.z, y.x - y.y - y.z); }

    float3 down13(float2 uv)
    {
        float2 t = _MainTex_TexelSize.xy;
        float3 a = tex2D(_MainTex, uv + t * float2(-2, -2)).rgb, b = tex2D(_MainTex, uv + t * float2(0, -2)).rgb, c = tex2D(_MainTex, uv + t * float2(2, -2)).rgb;
        float3 d = tex2D(_MainTex, uv + t * float2(-2, 0)).rgb, e = tex2D(_MainTex, uv).rgb, f = tex2D(_MainTex, uv + t * float2(2, 0)).rgb;
        float3 g = tex2D(_MainTex, uv + t * float2(-2, 2)).rgb, h = tex2D(_MainTex, uv + t * float2(0, 2)).rgb, i = tex2D(_MainTex, uv + t * float2(2, 2)).rgb;
        float3 j = tex2D(_MainTex, uv + t * float2(-1, -1)).rgb, k = tex2D(_MainTex, uv + t * float2(1, -1)).rgb;
        float3 l = tex2D(_MainTex, uv + t * float2(-1, 1)).rgb, m = tex2D(_MainTex, uv + t * float2(1, 1)).rgb;
        return e * 0.125 + (a + c + g + i) * 0.03125 + (b + d + f + h) * 0.0625 + (j + k + l + m) * 0.125;
    }
    float3 tent(float2 uv)
    {
        float2 t = _MainTex_TexelSize.xy;
        float3 s = tex2D(_MainTex, uv).rgb * 4.0;
        s += (tex2D(_MainTex, uv + float2(t.x, 0)).rgb + tex2D(_MainTex, uv - float2(t.x, 0)).rgb + tex2D(_MainTex, uv + float2(0, t.y)).rgb + tex2D(_MainTex, uv - float2(0, t.y)).rgb) * 2.0;
        s += tex2D(_MainTex, uv + t).rgb + tex2D(_MainTex, uv - t).rgb + tex2D(_MainTex, uv + float2(t.x, -t.y)).rgb + tex2D(_MainTex, uv + float2(-t.x, t.y)).rgb;
        return s / 16.0;
    }
    // ACES filmic fit (Narkowicz 2015) on luminance
    float aces(float x) { return saturate(x * (2.51 * x + 0.03) / (x * (2.43 * x + 0.59) + 0.14)); }
    ENDCG
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        // 0: temporal accumulation
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float3 cur = tex2D(_MainTex, i.uv).rgb;
                float3 hist = tex2D(_HistTex, i.uv).rgb;
                if (_Clamp > 0.5)
                {
                    // clamp the history to the box of this frame's 3×3 neighbourhood (YCoCg): stale colour can't ghost
                    float3 lo = 1e9, hi = -1e9;
                    float2 t = _MainTex_TexelSize.xy;
                    [unroll] for (int y = -1; y <= 1; y++)
                        [unroll] for (int x = -1; x <= 1; x++)
                        {
                            float3 c = toYCoCg(tex2D(_MainTex, i.uv + t * float2(x, y)).rgb);
                            lo = min(lo, c); hi = max(hi, c);
                        }
                    hist = fromYCoCg(clamp(toYCoCg(hist), lo, hi));
                }
                // keep NaN or inf out of the history for good
                float3 o = lerp(hist, cur, _Alpha);
                if (any(!(o == o)) || any(o > 6e4)) o = cur;
                return float4(o, 1);
            }
            ENDCG
        }
        // 1: first downsample (from the full-resolution frame)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // the glare source is capped at a luminance of 64 (before exposure): the approaching side of the disc
            // reaches hundreds near the ISCO, and that much spread wide fogs the whole view
            float4 frag(v2f i) : SV_Target
            {
                float3 c = down13(i.uv);
                float L = dot(c, float3(0.2126, 0.7152, 0.0722));
                return float4(c * min(1.0, 64.0 / max(L, 1e-6)), 1);
            }
            ENDCG
        }
        // 2: downsample
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target { return float4(down13(i.uv), 1); }
            ENDCG
        }
        // 3: upsample, added onto the next larger level
        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target { return float4(tent(i.uv), 1); }
            ENDCG
        }
        // 4: combine and tonemap
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float3 c = tex2D(_MainTex, i.uv).rgb;
                // energy-conserving glare: the glare images hold the same energy as the frame (the wide one is the sum of
                // _Levels blurs, so it is divided by _Levels); a fraction of the light moves into them
                float3 core = tex2D(_CoreTex, i.uv).rgb;
                float3 wide = tex2D(_WideTex, i.uv).rgb / max(_Levels, 1.0);
                // (with the source capped, this is energy-conserving up to that cap)
                c = c * (1.0 - _Glare.x - _Glare.y) + core * _Glare.x + wide * _Glare.y;
                c *= _PostExposure;
                // one tonemap, on luminance: hue and saturation survive, and colours that would leave the gamut are
                // pulled towards white of the same luminance instead of clipping per channel
                float L = max(dot(c, float3(0.2126, 0.7152, 0.0722)), 1e-6);
                float Lt = aces(L);
                float3 o = c * (Lt / L);
                float m = max(o.r, max(o.g, o.b));
                if (m > 1.0) o = lerp(o, Lt.xxx, saturate((m - 1.0) / max(m - Lt, 1e-5)));
                return float4(saturate(o), 1);
            }
            ENDCG
        }
    }
}
