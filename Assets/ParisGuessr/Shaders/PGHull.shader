// Inverted hull outline (trees): back faces pushed out along the normal, drawn in the line colour.
Shader "PG/Hull"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+5" }
        Cull Front
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _PGFogColor; float4 _PGFog; float4 _PGLineColor; float _PGLineWidth;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float fog : TEXCOORD0; };
            v2f vert (appdata v)
            {
                v2f o;
                float3 w = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 n = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                float dist = length(_WorldSpaceCameraPos - w);
                // ~constant pixel thickness: world offset proportional to distance
                float k = _PGLineWidth * dist * 2.0 / (_ScreenParams.y * UNITY_MATRIX_P[1][1]) * 0.9;
                w += n * k;
                o.pos = mul(UNITY_MATRIX_VP, float4(w, 1));
                o.fog = saturate((dist - _PGFog.x) / max(1.0, _PGFog.y - _PGFog.x));
                return o;
            }
            fixed4 frag (v2f i) : SV_Target { return fixed4(lerp(_PGLineColor.rgb, _PGFogColor.rgb, i.fog), 1); }
            ENDCG
        }
    }
}
