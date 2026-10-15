// Still water for the welcome lagoon. Opaque, so depth, sorting and occlusion stay as for any surface, and no depth or
// colour texture is read (the headset pipeline has neither). A few small travelling waves bend an analytic normal; the
// surface mixes a deep body colour with the museum sky by Fresnel and catches a narrow sun glint. Stereo-ready.
Shader "Musiyo/Lagoon Water"
{
    Properties
    {
        _DeepColor ("Deep colour", Color) = (0.02, 0.07, 0.08, 1)
        _ShallowColor ("Facing colour", Color) = (0.06, 0.16, 0.16, 1)
        [NoScaleOffset] _SkyTex ("Sky panorama", 2D) = "grey" {}
        _SkyTint ("Sky reflection tint", Color) = (0.75, 0.8, 0.82, 1)
        _Reflection ("Reflection strength", Range(0, 1)) = 0.55
        _WaveLength ("Wave length (m)", Range(0.05, 4)) = 0.6
        _WaveSlope ("Wave slope", Range(0, 0.5)) = 0.12
        _WaveSpeed ("Wave speed (m/s)", Range(0, 1)) = 0.12
        _Glint ("Sun glint", Range(0, 4)) = 1.2
        _GlintSharpness ("Glint sharpness", Range(16, 1024)) = 400
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_SkyTex);
        SAMPLER(sampler_SkyTex);

        CBUFFER_START(UnityPerMaterial)
            half4 _DeepColor;
            half4 _ShallowColor;
            half4 _SkyTint;
            half _Reflection;
            float _WaveLength;
            half _WaveSlope;
            float _WaveSpeed;
            half _Glint;
            half _GlintSharpness;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        static const float2 WaveDirections[4] = { float2(0.83, 0.56), float2(-0.42, 0.91), float2(-0.95, -0.31), float2(0.24, -0.97) };
        static const float WaveScales[4] = { 1.0, 0.71, 0.53, 0.37 };

        // Sum of four small waves in different directions; returns the slope of the surface in x and z.
        float2 WaveSlope(float2 p, float time)
        {
            float2 slope = 0;
            [unroll] for (int i = 0; i < 4; i++)
            {
                float k = TWO_PI / (_WaveLength * WaveScales[i]);
                float phase = dot(WaveDirections[i], p) * k + time * _WaveSpeed * k + i * 1.7;
                slope += WaveDirections[i] * cos(phase) * (_WaveSlope * WaveScales[i]);
            }
            return slope;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 slope = WaveSlope(input.positionWS.xz, _Time.y);
                // Waves only on the upward face; the rim and underside keep the delivered normal.
                half up = saturate(input.normalWS.y * 4 - 3);
                half3 normal = normalize(lerp(input.normalWS, normalize(half3(-slope.x, 1, -slope.y)), up));
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half facing = saturate(dot(normal, view));
                half fresnel = 0.04 + 0.96 * pow(1 - facing, 5);

                float3 reflected = reflect(-view, normal);
                reflected.y = abs(reflected.y);
                float2 uv = float2(atan2(reflected.x, reflected.z) * (0.5 * INV_PI) + 0.5, asin(saturate(reflected.y)) * INV_PI + 0.5);
                half3 sky = SAMPLE_TEXTURE2D_LOD(_SkyTex, sampler_SkyTex, uv, 0).rgb * _SkyTint.rgb;

                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 ambient = SampleSH(normal);
                half3 body = lerp(_DeepColor.rgb, _ShallowColor.rgb, facing) * (ambient + sun.color * sun.shadowAttenuation * saturate(dot(normal, sun.direction)) * 0.25);
                half3 color = lerp(body, sky * (ambient * 1.5 + 0.25), fresnel * _Reflection);
                half3 halfway = normalize(sun.direction + view);
                color += sun.color * sun.shadowAttenuation * pow(saturate(dot(normal, halfway)), _GlintSharpness) * _Glint;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half Frag(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octahedral = PackNormalOctQuadEncode(normal);
                    return half4(PackFloat2To888(saturate(octahedral * 0.5 + 0.5)), 0);
                #else
                    return half4(normal, 0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
