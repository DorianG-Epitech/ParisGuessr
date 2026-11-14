// Screen-space-width outlines. Each segment is a quad: POSITION = this end, NORMAL = other end, TEXCOORD0.x = side (+-1).
// The segment is clipped to the near plane in view space and pulled slightly toward the camera so it wins over its faces.
Shader "PG/Lines"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _PGFogColor; float4 _PGFog; float4 _PGLineColor; float _PGLineWidth;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float fog : TEXCOORD0; float faint : TEXCOORD1; };
            v2f vert (appdata v)
            {
                v2f o;
                float3 a = mul(UNITY_MATRIX_V, float4(mul(unity_ObjectToWorld, v.vertex).xyz, 1)).xyz;
                float3 b = mul(UNITY_MATRIX_V, float4(mul(unity_ObjectToWorld, float4(v.normal, 1)).xyz, 1)).xyz;
                float nz = -(_ProjectionParams.y + 0.02);
                float valid = 1;
                if (a.z > nz)
                {
                    if (b.z > nz) valid = 0;
                    else a = lerp(a, b, (nz - a.z) / (b.z - a.z));
                }
                else if (b.z > nz) b = lerp(b, a, (nz - b.z) / (a.z - b.z));
                float ground = v.uv.y < -0.5 ? 1.0 : 0.0;   // curbs: must beat the (pulled) road surface
                float da = length(a);
                a *= max(0.0, 1.0 - (0.04 + da * 0.004 + ground * (0.32 + da * 0.008)) / max(da, 0.001));
                float db = length(b);
                b *= max(0.0, 1.0 - (0.04 + db * 0.004 + ground * (0.32 + db * 0.008)) / max(db, 0.001));
                float4 ca = mul(UNITY_MATRIX_P, float4(a, 1));
                float4 cb = mul(UNITY_MATRIX_P, float4(b, 1));
                float2 half_ = _ScreenParams.xy * 0.5;
                float2 sa = ca.xy / ca.w * half_;
                float2 sb = cb.xy / cb.w * half_;
                float2 d = sb - sa; float L = length(d);
                d = L > 1e-4 ? d / L : float2(1, 0);
                float2 n = float2(-d.y, d.x);
                // fade the width a little with distance so far blocks don't turn into black noise
                float w = _PGLineWidth * lerp(1.0, 0.55, saturate(da / 250.0)) * (v.uv.y > 0.5 ? 0.75 : 1.0);
                ca.xy += n * v.uv.x * (w * 0.5) / half_ * ca.w;
                o.pos = valid > 0 ? ca : float4(0, 0, -10, 1);
                o.fog = saturate((da - _PGFog.x) / max(1.0, _PGFog.y - _PGFog.x));
                o.faint = max(0.0, v.uv.y);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target { float3 c = lerp(_PGLineColor.rgb, float3(0.70, 0.69, 0.67), i.faint); return fixed4(lerp(c, _PGFogColor.rgb, saturate(i.fog + i.faint * 0.25)), 1); }
            ENDCG
        }
    }
}
