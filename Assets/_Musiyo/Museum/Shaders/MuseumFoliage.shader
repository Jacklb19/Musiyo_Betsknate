// Leaves and stalks of the set dressing. Opaque geometry (no alpha test), drawn from both sides with the back face lit as
// its own side. Shape reads better through a darker base, a small per-plant shift of tone, light passing through leaves
// seen against the sun, and a slow sway that grows with height. Every pass sways the same way, so shadows and depth match.
// Plants are combined per area before they are drawn: vertex colour red is the height within the plant (0 at the foot,
// 1 at the top) and green a value proper to each plant; see MuseumFoliageBatch.
Shader "Musiyo/Foliage"
{
    Properties
    {
        _BaseMap ("Colour", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [Toggle(_NORMALMAP)] _UseNormalMap ("Use normal map", Float) = 0
        [NoScaleOffset][Normal] _BumpMap ("Normal map", 2D) = "bump" {}
        _BaseShade ("Shade at the base", Range(0, 1)) = 0.55
        _Variation ("Per-plant tone variation", Range(0, 0.5)) = 0.12
        _Translucency ("Light through leaves", Range(0, 2)) = 0.6
        _Smoothness ("Smoothness", Range(0, 1)) = 0.25
        _WindStrength ("Sway at the top (m)", Range(0, 0.2)) = 0.03
        _WindSpeed ("Sway speed", Range(0, 4)) = 1.1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Cull [_Cull]

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BaseShade;
            half _Variation;
            half _Translucency;
            half _Smoothness;
            float _WindStrength;
            float _WindSpeed;
            float _Cull;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float3 Sway(float3 positionOS, half4 plant)
        {
            float3 positionWS = TransformObjectToWorld(positionOS);
            float rise = plant.r;
            float phase = plant.g * TWO_PI + dot(positionWS.xz, float2(0.35, 0.21));
            float t = _Time.y * _WindSpeed;
            float2 push = float2(sin(t + phase) + 0.35 * sin(2.3 * t + phase * 1.7), 0.6 * sin(0.8 * t + phase * 0.6));
            positionWS.xz += push * _WindStrength * rise * rise;
            return positionWS;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local _NORMALMAP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half2 shadeAndTone : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = Sway(input.positionOS.xyz, input.color);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.normalWS = normals.normalWS;
                output.tangentWS = half4(normals.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.shadeAndTone = half2(lerp(_BaseShade, 1, input.color.r), (input.color.g - 0.5) * 2 * _Variation);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 color = albedo.rgb * input.shadeAndTone.x * (1 + input.shadeAndTone.y);
                half3 normal = normalize(input.normalWS);
                #if defined(_NORMALMAP)
                    half3 tangentNormal = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv));
                    half3 bitangent = input.tangentWS.w * cross(normal, input.tangentWS.xyz);
                    normal = normalize(TransformTangentToWorld(tangentNormal, half3x3(input.tangentWS.xyz, bitangent, normal)));
                #endif
                normal = front ? normal : -normal;
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 light = sun.color * sun.shadowAttenuation;
                half diffuse = saturate(dot(normal, sun.direction));
                // Light passing through a thin leaf toward the viewer.
                half through = pow(saturate(dot(view, -sun.direction)), 4) * saturate(dot(-normal, sun.direction) + 0.3) * _Translucency;
                half3 halfway = normalize(sun.direction + view);
                half specular = pow(saturate(dot(normal, halfway)), exp2(10 * _Smoothness + 1)) * _Smoothness * 0.25;
                half3 lit = color * (SampleSH(normal) + light * (diffuse + through)) + light * specular;
                return half4(MixFog(lit, input.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = Sway(input.positionOS.xyz, input.color);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirection = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.positionCS = positionCS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target { return 0; }
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
                output.positionCS = TransformWorldToHClip(Sway(input.positionOS.xyz, input.color));
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
                output.positionCS = TransformWorldToHClip(Sway(input.positionOS.xyz, input.color));
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                normal = front ? normal : -normal;
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
