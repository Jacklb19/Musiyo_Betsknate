// Water of the welcome lagoon: shallow clear water under a roof. Still opaque and without depth or colour texture
// reads (the headset pipeline has neither): the depth of the ring between rim and island is computed from the distance
// to the lagoon centre, the bed is a stone and mud texture seen through the surface with a parallax shift, and the
// water absorbs it with depth. The surface reflects the room (warm walls, a bright opening above) instead of the open
// sky, plus small travelling ripples and a narrow glint. Stereo-ready.
Shader "Musiyo/World/Clear Water"
{
    Properties
    {
        [NoScaleOffset] _BedTex ("Bed", 2D) = "grey" {}
        _BedScale ("Bed tiles per metre", Float) = 0.6
        _BedTint ("Bed tint", Color) = (1, 0.95, 0.85, 1)
        _Center ("Lagoon centre (x, z)", Vector) = (0, -20, 0, 0)
        _OuterRadius ("Rim radius (m)", Float) = 3
        _InnerRadius ("Island radius (m)", Float) = 1.3
        _MaxDepth ("Deepest point (m)", Float) = 0.35
        _Absorption ("Absorption per metre (r, g, b)", Vector) = (1.4, 0.75, 0.65, 0)
        _Scatter ("Water body colour", Color) = (0.1, 0.16, 0.13, 1)
        _RoomColor ("Room seen in the water", Color) = (0.42, 0.32, 0.22, 1)
        _OpeningColor ("Opening above", Color) = (0.85, 0.9, 0.95, 1)
        _OpeningHeight ("Opening starts (sine of elevation)", Range(0, 1)) = 0.86
        _Reflection ("Reflection strength", Range(0, 1)) = 0.55
        _WaveLength ("Wave length (m)", Range(0.05, 4)) = 0.35
        _WaveSlope ("Wave slope", Range(0, 0.5)) = 0.05
        _WaveSpeed ("Wave speed (m/s)", Range(0, 1)) = 0.06
        _Glint ("Glint", Range(0, 4)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BedTex);
        SAMPLER(sampler_BedTex);

        CBUFFER_START(UnityPerMaterial)
            float _BedScale;
            half4 _BedTint;
            float4 _Center;
            float _OuterRadius;
            float _InnerRadius;
            float _MaxDepth;
            float4 _Absorption;
            half4 _Scatter;
            half4 _RoomColor;
            half4 _OpeningColor;
            half _OpeningHeight;
            half _Reflection;
            float _WaveLength;
            half _WaveSlope;
            float _WaveSpeed;
            half _Glint;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        static const float2 WaveDirections[4] = { float2(0.83, 0.56), float2(-0.42, 0.91), float2(-0.95, -0.31), float2(0.24, -0.97) };
        static const float WaveScales[4] = { 1.0, 0.71, 0.53, 0.37 };

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
                half up = saturate(input.normalWS.y * 4 - 3);
                half3 normal = normalize(lerp(input.normalWS, normalize(half3(-slope.x, 1, -slope.y)), up));
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // Depth of the ring: zero at the rim and at the island, deepest half way.
                float radius = distance(input.positionWS.xz, _Center.xy);
                float edge = min(_OuterRadius - radius, radius - _InnerRadius);
                float depth = _MaxDepth * smoothstep(0, 0.8, edge) * up;

                // The bed as seen through the surface: shifted along the view, bent a little by the ripples.
                float2 shift = -view.xz / max(view.y, 0.4) * depth * 0.6 + slope * depth * 0.4;
                float2 bedPosition = input.positionWS.xz + shift;
                half3 bed = SAMPLE_TEXTURE2D(_BedTex, sampler_BedTex, bedPosition * _BedScale).rgb * _BedTint.rgb;
                half3 ambient = SampleSH(half3(0, 1, 0));
                Light sun = GetMainLight();
                bed *= ambient + 0.35 + sun.color * 0.15;
                float path = depth * (1 + 1 / max(view.y, 0.2));
                half3 transmitted = exp(-_Absorption.xyz * path);
                half3 under = bed * transmitted + _Scatter.rgb * ambient * (1 - transmitted);

                // What the surface mirrors: the room's walls, and the light of the opening when looking up steeply.
                float3 reflected = reflect(-view, normal);
                half3 room = lerp(_RoomColor.rgb * ambient, _OpeningColor.rgb, smoothstep(_OpeningHeight, 1, reflected.y));
                half facing = saturate(dot(normal, view));
                half fresnel = 0.02 + 0.98 * pow(1 - facing, 5);
                half3 color = lerp(under, room, saturate(fresnel * _Reflection));
                half3 halfway = normalize(half3(0, 1, 0) + view);
                color += _OpeningColor.rgb * pow(saturate(dot(normal, halfway)), 300) * _Glint * up;
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
    }
    Fallback Off
}
