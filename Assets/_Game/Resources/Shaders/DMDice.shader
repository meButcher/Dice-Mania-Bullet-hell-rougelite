// The HUD dice cubes: atlas-textured faces with simple lighting so the cube reads as 3D, plus a white flash on landing.
Shader "DiceMania/Dice"
{
    Properties
    {
        _MainTex ("Face atlas", 2D) = "white" {}
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
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Flash;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 n : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.n = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv);
                float3 n = normalize(i.n);
                float lit = saturate(dot(n, normalize(float3(-0.35, 0.55, -1.0)))) * 0.55 + 0.5;
                float3 col = lerp(c.rgb * lit, float3(1, 1, 1), saturate(_Flash));
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
