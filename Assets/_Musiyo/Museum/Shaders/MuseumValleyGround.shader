// The ground around the museum and the hills that close the valley. Two ground textures (grass and forest
// floor) blend by vertex colour red; green darkens or lightens patches; blue fades toward a flat forest colour where a
// texture would only show as repetition (the far slopes). Lambert sun plus the scene's ambient. The far slopes take
// the distance haze only up to a limit, so the ridges stay readable against the sky. Stereo-ready.
Shader "Musiyo/World/Valley Ground"
{
    Properties
    {
        _GrassTex ("Grass", 2D) = "white" {}
        _ForestTex ("Forest floor", 2D) = "white" {}
        _GrassTint ("Grass tint", Color) = (1, 1, 1, 1)
        _ForestTint ("Forest floor tint", Color) = (1, 1, 1, 1)
        _FarColor ("Far forest colour", Color) = (0.12, 0.2, 0.1, 1)
        _TilesPerMetre ("Tiles per metre", Float) = 0.25
        _HazeLimit ("Most haze on the far slopes", Range(0, 1)) = 0.7
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex);
        TEXTURE2D(_ForestTex); SAMPLER(sampler_ForestTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _GrassTex_ST;
            float4 _ForestTex_ST;
            half4 _GrassTint;
            half4 _ForestTint;
            half4 _FarColor;
            float _TilesPerMetre;
            half _HazeLimit;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
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
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.positionWS.xz * _TilesPerMetre;
                half3 grass = SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, uv).rgb * _GrassTint.rgb;
                half3 forest = SAMPLE_TEXTURE2D(_ForestTex, sampler_ForestTex, uv * 0.8).rgb * _ForestTint.rgb;
                half3 albedo = lerp(grass, forest, input.color.r) * lerp(0.75, 1.2, input.color.g);
                albedo = lerp(albedo, _FarColor.rgb * lerp(0.8, 1.15, input.color.g), input.color.b);
                half3 normal = normalize(input.normalWS);
                Light sun = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 color = albedo * (SampleSH(normal) + sun.color * sun.shadowAttenuation * saturate(dot(normal, sun.direction)));
                // Linear distance haze from the scene fog, capped so the far ridges keep a silhouette.
                float distanceToEye = distance(input.positionWS, GetCameraPositionWS());
                half haze = 1 - saturate(distanceToEye * unity_FogParams.z + unity_FogParams.w);
                color = lerp(color, unity_FogColor.rgb, haze * lerp(1, _HazeLimit, input.color.b));
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
    }
    Fallback Off
}
