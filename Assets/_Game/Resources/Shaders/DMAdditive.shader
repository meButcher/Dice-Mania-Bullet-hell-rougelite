// Additive glow shader for zones, rings and auras. Output = rgb * a.
Shader "DiceMania/Additive"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,0.5)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : TEXCOORD0;
                float2 local : TEXCOORD1;
            };

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                o.local = v.vertex.xz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Brighter towards the edge of the disc for a "dome" read.
                float r = saturate(length(i.local) * 2.0);
                float edge = lerp(0.35, 1.0, r * r);
                return fixed4(i.color.rgb * i.color.a * edge, 1);
            }
            ENDCG
        }
    }
}
