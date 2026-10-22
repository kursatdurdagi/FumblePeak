Shader "Arawn/Fog/FogOfWarLiteURP"
{
    Properties
    {
        _MainTex("Fog Mask", 2D) = "white" {}
        _Color("Fog Color", Color) = (0,0,0,1)
        [Toggle(_BLUR_ON)] _EnableBlur("Enable Blur", Float) = 0
        _BlurRadius("Blur Radius", Range(0,2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma shader_feature_local _BLUR_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_ST;
                float _BlurRadius;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize; // provided by Unity, not per-material

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                half alpha;
                #if defined(_BLUR_ON)
                    float2 texel = _MainTex_TexelSize.xy * _BlurRadius;
                    // 3x3 Gaussian weights
                    const half k0 = 1.0h/16.0h; // corners
                    const half k1 = 2.0h/16.0h; // edges
                    const half k2 = 4.0h/16.0h; // center
                    half a00 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + texel * float2(-1,-1)).r;
                    half a10 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + texel * float2( 0,-1)).r;
                    half a20 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + texel * float2( 1,-1)).r;
                    half a01 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + texel * float2(-1, 0)).r;
                    half a11 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).r;
                    half a21 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + texel * float2( 1, 0)).r;
                    half a02 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + texel * float2(-1, 1)).r;
                    half a12 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + texel * float2( 0, 1)).r;
                    half a22 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + texel * float2( 1, 1)).r;
                    alpha = k0*(a00+a20+a02+a22) + k1*(a10+a01+a21+a12) + k2*a11;
                    alpha = saturate(alpha);
                #else
                    alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).r;
                #endif
                half4 col;
                col.rgb = _Color.rgb;
                col.a = alpha * _Color.a;
                return col;
            }
            ENDHLSL
        }
    }
}
