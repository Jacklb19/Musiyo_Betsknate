// Sky of the visit: one photographed cloud panorama, a haze band that meets the distance fog at the horizon, and a
// plain fill below it. One texture read per sky pixel; no lighting, no depth reads. Stereo-ready (instancing/multiview).
Shader "Musiyo/Sky Panorama"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Panorama (equirectangular)", 2D) = "grey" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Exposure ("Exposure", Range(0, 4)) = 1
        _Rotation ("Rotation (degrees)", Range(0, 360)) = 0
        _HorizonColor ("Horizon haze", Color) = (0.725, 0.765, 0.784, 1)
        _HorizonHeight ("Haze height (sine of elevation)", Range(0.001, 0.5)) = 0.12
        _GroundColor ("Below the horizon", Color) = (0.55, 0.58, 0.58, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Exposure;
                float _Rotation;
                half4 _HorizonColor;
                half _HorizonHeight;
                half4 _GroundColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float angle = radians(_Rotation);
                float s = sin(angle), c = cos(angle);
                float3 d = input.positionOS.xyz;
                output.direction = float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 d = normalize(input.direction);
                float2 uv = float2(atan2(d.x, d.z) * (0.5 * INV_PI) + 0.5, asin(clamp(d.y, -1, 1)) * INV_PI + 0.5);
                half3 sky = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0).rgb * _Tint.rgb * _Exposure;
                half haze = 1 - smoothstep(0, _HorizonHeight, d.y);
                half3 color = lerp(sky, _HorizonColor.rgb, haze);
                color = d.y < 0 ? lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-d.y * 4)) : color;
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
