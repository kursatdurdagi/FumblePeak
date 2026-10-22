Shader "Arawn/UI/DestinationIndicatorURP"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _Color("Color", Color) = (0, 1, 0, 1)
        [Toggle(_USE_TEXTURE)] _UseTexture("Use Texture", Float) = 0
        
        [Header(Alpha Clipping)]
        [Toggle(_ALPHA_CLIP)] _AlphaClip("Alpha Clipping", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        
        [Header(Emission)]
        [Toggle(_USE_EMISSION)] _UseEmission("Enable Emission", Float) = 1
        [HDR] _EmissionColor("Emission Color", Color) = (0, 2, 0, 1)
        _EmissionMap("Emission Map", 2D) = "white" {}
        [Toggle(_USE_EMISSION_MAP)] _UseEmissionMap("Use Emission Map", Float) = 0
        _EmissionIntensity("Emission Intensity", Range(0, 10)) = 1
        
        [Header(Animation)]
        _PulseSpeed("Pulse Speed", Range(0.1, 10)) = 2
        _PulseMin("Pulse Min", Range(0, 1)) = 0.3
        
        [Header(Emission Pulse)]
        [Toggle(_PULSE_EMISSION)] _PulseEmission("Pulse Emission", Float) = 0
        
        [Header(Size Pulse)]
        [Toggle(_PULSE_SIZE)] _PulseSize("Pulse Size", Float) = 0
        _SizeAmplitude("Size Amplitude", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Overlay" 
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always  // Always render on top, ignoring depth (renders above fog)
        Cull Back

        Pass
        {
            Name "DestinationIndicator"
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ _USE_TEXTURE
            #pragma multi_compile_local _ _ALPHA_CLIP
            #pragma multi_compile_local _ _USE_EMISSION
            #pragma multi_compile_local _ _USE_EMISSION_MAP
            #pragma multi_compile_local _ _PULSE_EMISSION
            #pragma multi_compile_local _ _PULSE_SIZE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 emissionUV : TEXCOORD1;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_ST;
                float4 _EmissionColor;
                float4 _EmissionMap_ST;
                float _EmissionIntensity;
                float _PulseSpeed;
                float _PulseMin;
                float _Cutoff;
                float _SizeAmplitude;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                // Transform vertex to clip space first
                float4 posCS = TransformObjectToHClip(input.positionOS.xyz);
                
                // Size pulse - scale around visual center using UV coordinates
                #if defined(_PULSE_SIZE)
                    // Calculate pulse factor
                    float sizePulse = lerp(1.0 - _SizeAmplitude * 0.5, 1.0 + _SizeAmplitude * 0.5, 
                                          (sin(_Time.y * _PulseSpeed) * 0.5 + 0.5));
                    
                    // UV tells us where this vertex is relative to the quad center
                    // UV (0.5, 0.5) = center, corners are (0,0), (1,0), (0,1), (1,1)
                    // Convert to -1 to +1 range centered at 0
                    float2 direction = (input.uv - 0.5) * 2.0;
                    
                    // Calculate how much to push this vertex outward/inward
                    // We use the direction and scale it by the pulse amount
                    float scaleChange = sizePulse - 1.0;
                    
                    // Apply in clip space - multiply by w to work in homogeneous coords
                    // The 0.1 factor controls how much UV units translate to screen movement
                    posCS.xy += direction * scaleChange * posCS.w * _SizeAmplitude;
                #endif
                
                output.positionCS = posCS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.emissionUV = TRANSFORM_TEX(input.uv, _EmissionMap);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Sample texture (always needed for alpha clipping)
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                
                // Base color
                half4 col = _Color * input.color;
                
                #if defined(_USE_TEXTURE)
                    col *= tex;
                #endif
                
                // Alpha clipping (uses texture alpha regardless of _USE_TEXTURE)
                #if defined(_ALPHA_CLIP)
                    clip(tex.a - _Cutoff);
                #endif
                
                // Emission
                #if defined(_USE_EMISSION)
                    half3 emission = _EmissionColor.rgb;
                    
                    #if defined(_USE_EMISSION_MAP)
                        half4 emissionTex = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.emissionUV);
                        emission *= emissionTex.rgb;
                    #endif
                    
                    // Pulse animation
                    #if defined(_PULSE_EMISSION)
                        float pulse = lerp(_PulseMin, 1.0, (sin(_Time.y * _PulseSpeed) * 0.5 + 0.5));
                        emission *= pulse;
                    #endif
                    
                    emission *= _EmissionIntensity;
                    col.rgb += emission;
                #endif
                
                return col;
            }
            ENDHLSL
        }
    }
    
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
