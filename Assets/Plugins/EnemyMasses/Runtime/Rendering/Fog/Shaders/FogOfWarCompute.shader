// Built-in pipeline fog rendering shader for compute-driven FogOfWarLite.
// Reads fog opacity from R channel of an RFloat RenderTexture.
// For URP projects, use the companion FogOfWarLiteURP.shader instead.
Shader "EnemyMasses/FogOfWarCompute"
{
    Properties
    {
        _MainTex ("Fog Texture", 2D) = "white" {}
        _Color   ("Fog Color",  Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4    _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv     : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv     = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Fog opacity lives in R channel (RFloat RenderTexture)
                float fog = tex2D(_MainTex, i.uv).r;
                return fixed4(_Color.rgb, fog * _Color.a);
            }
            ENDCG
        }
    }
}
