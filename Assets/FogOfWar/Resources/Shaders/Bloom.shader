// Subtle bloom for the open world (Bloom.cs): a soft-knee bright pass, a 13-tap downsample chain and a tent-filtered
// upsample chain, added back at a low intensity. Only light well above the sunlit hull's brightness glows.
Shader "Hidden/FogOfWar/Bloom"
{
    Properties { _MainTex ("", 2D) = "black" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex, _BloomTex;
    float4 _MainTex_TexelSize;
    float4 _Threshold;   // x threshold, y knee, z 1 / (4 knee), w unused
    float _Intensity;
    struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
    v2f vert(appdata_img v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord; return o; }

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
    ENDCG
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        // 0: bright pass + first downsample
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float3 c = min(down13(i.uv), 64.0);
                float br = max(c.r, max(c.g, c.b));
                float rq = clamp(br - _Threshold.x + _Threshold.y, 0.0, 2.0 * _Threshold.y);
                rq = rq * rq * _Threshold.z;
                float w = max(rq, br - _Threshold.x) / max(br, 1e-4);
                return float4(c * w, 1);
            }
            ENDCG
        }
        // 1: downsample
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target { return float4(down13(i.uv), 1); }
            ENDCG
        }
        // 2: upsample, added onto the next larger level
        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target { return float4(tent(i.uv), 1); }
            ENDCG
        }
        // 3: combine with the frame
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float4 src = tex2D(_MainTex, i.uv);
                return float4(src.rgb + tex2D(_BloomTex, i.uv).rgb * _Intensity, src.a);
            }
            ENDCG
        }
    }
}
