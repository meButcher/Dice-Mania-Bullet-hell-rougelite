// Simple per-instance coloured, fake-lit shader for GPU instancing.
// Works with the Built-in pipeline and with URP (unlit pass with no LightMode tag), on WebGL2 and WebGPU.
// _Color.rgb = colour, _Color.a = emission (0 = lit only, 1 = fully glowing).
Shader "DiceMania/Instanced"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,0)
        _Flash ("Flash", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options nolightprobe nolightmap
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float4 color : TEXCOORD1;
                float3 wpos : TEXCOORD2;
            };

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.color = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 L = normalize(float3(0.35, 1.0, 0.25));
                float lit = saturate(dot(n, L)) * 0.7 + 0.3;
                float rim = pow(1.0 - saturate(n.y), 2.0) * 0.15;
                float3 baseCol = i.color.rgb * (lit + rim);
                float3 glow = i.color.rgb * 1.35;
                float3 c = lerp(baseCol, glow, saturate(i.color.a));
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}
