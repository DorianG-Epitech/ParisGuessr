// Flat "maquette" shading: vertex colour x simple directional term, manual distance fog, optional depth pull (colour alpha < 1)
Shader "PG/Faces"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _PGFogColor; float4 _PGFog;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 col : COLOR; float fog : TEXCOORD0; };
            v2f vert (appdata v)
            {
                v2f o;
                float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 vp = mul(UNITY_MATRIX_V, float4(w, 1)).xyz;
                float dist = length(vp);
                float pull = (1.0 - v.color.a) * (0.25 + dist * 0.006);
                vp *= max(0.0, 1.0 - pull / max(dist, 0.001));
                o.pos = mul(UNITY_MATRIX_P, float4(vp, 1));
                float3 n = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                float3 L = normalize(float3(0.45, 0.80, 0.30));
                float sh = 0.80 + 0.20 * saturate(dot(n, L)) - 0.04 * saturate(-dot(n, L));
                o.col = fixed4(v.color.rgb * sh, 1);
                o.fog = saturate((dist - _PGFog.x) / max(1.0, _PGFog.y - _PGFog.x));
                return o;
            }
            fixed4 frag (v2f i) : SV_Target { return fixed4(lerp(i.col.rgb, _PGFogColor.rgb, i.fog), 1); }
            ENDCG
        }
    }
}
