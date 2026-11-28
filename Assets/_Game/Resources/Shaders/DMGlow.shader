// Soft additive glow for flashes, halos, bullet trails, sparks, mist and floor cracks (a cheap stand-in for bloom on WebGL2).
// Output = rgb * a * falloff.
//   _Streak = 0: radial blob on a disc of radius 0.5 (hot centre, soft edge).
//   _Streak = 1: line on a unit quad, local x = along (+x = head, brightest), local z = across.
Shader "DiceMania/Glow"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Streak ("Streak", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "IgnoreProjector"="True" }
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

            float _Streak;

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
                float f;
                if (_Streak > 0.5)
                {
                    float across = saturate(1.0 - abs(i.local.y) * 2.0);
                    float along = saturate(i.local.x + 0.5);
                    f = across * across * (0.15 + 0.85 * along);
                    f += pow(across, 8.0) * along * 0.9;   // white-hot core line
                }
                else
                {
                    float g = 1.0 - saturate(length(i.local) * 2.0);
                    f = g * g + pow(g, 6.0) * 0.7;         // soft falloff + hot centre
                }
                return fixed4(i.color.rgb * i.color.a * f, 1);
            }
            ENDCG
        }
    }
}
