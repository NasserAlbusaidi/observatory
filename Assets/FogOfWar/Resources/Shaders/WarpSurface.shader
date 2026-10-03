// Alcubierre York time θ = v·(x/r)·df/dr drawn as a height field: contraction ahead (blue), expansion behind (orange).
// The mesh is a flat grid in the XZ plane; the ship flies along +X.
Shader "FogOfWar/WarpSurface"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "FOWCommon.cginc"
            float _V, _R, _Sigma, _Amp, _Scroll, _AmpCol;
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float theta : TEXCOORD0; float2 xz : TEXCOORD1; };
            float sech2(float x) { float c = cosh(clamp(x, -20.0, 20.0)); return 1.0 / (c * c); }
            float yorkTime(float2 xz)
            {
                float r = max(length(xz), 1e-4);
                float dfdr = _Sigma / (2.0 * tanh(_Sigma * _R)) * (sech2(_Sigma * (r + _R)) - sech2(_Sigma * (r - _R)));
                return _V * (xz.x / r) * dfdr;
            }
            v2f vert(appdata v)
            {
                v2f o;
                float3 p = v.vertex.xyz;
                float th = yorkTime(p.xz);
                o.theta = th;
                o.xz = p.xz + float2(_Scroll, 0.0);
                p.y = th * _Amp;
                o.pos = UnityObjectToClipPos(float4(p, 1.0));
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float2 g = abs(frac(i.xz) - 0.5);
                float2 fw = fwidth(i.xz);
                float line_ = 1.0 - min(smoothstep(0.0, fw.x * 1.5, g.x), smoothstep(0.0, fw.y * 1.5, g.y));
                float t = clamp(i.theta * _AmpCol, -1.0, 1.0);
                float3 expand = float3(1.0, 0.42, 0.18), contract = float3(0.25, 0.75, 1.0), flat_ = float3(0.33, 0.36, 0.5);
                float3 c = t > 0.0 ? lerp(flat_, expand, t) : lerp(flat_, contract, -t);
                float glow = abs(t);
                float3 col = c * (0.12 + 0.9 * line_) + c * glow * 0.35;
                return float4(displayToOutput(col), 0.35 + 0.65 * max(line_, glow));
            }
            ENDCG
        }
    }
}
