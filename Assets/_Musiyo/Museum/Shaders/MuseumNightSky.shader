// Night sky of the visit: the starry panorama of the title and the credits, with the moon drawn from its direction
// instead of painted into the panorama, so its edge stays clean at any resolution (a headset eye sees about twenty pixels
// per degree). A small face texture for its seas, a gentle limb and a wide, faint halo. No lighting, no depth reads;
// one panorama read per sky pixel and one face read only on the disc. Stereo-ready (instancing/multiview).
Shader "Musiyo/Night Sky"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Panorama (equirectangular)", 2D) = "black" {}
        _Exposure ("Exposure", Range(0, 4)) = 1
        _HorizonColor ("Horizon haze", Color) = (0.022, 0.027, 0.045, 1)
        _HorizonHeight ("Haze height (sine of elevation)", Range(0.001, 0.5)) = 0.22
        _GroundColor ("Below the horizon", Color) = (0.008, 0.009, 0.011, 1)
        [NoScaleOffset] _MoonTex ("Moon face (red channel)", 2D) = "white" {}
        _MoonDirection ("Moon direction", Vector) = (-0.9, 0.42, 0, 0)
        _MoonRight ("Moon face right", Vector) = (0, 0, 1, 0)
        _MoonUp ("Moon face up", Vector) = (0.42, 0.9, 0, 0)
        _MoonRadius ("Moon angular radius (radians)", Float) = 0.026
        _MoonColor ("Moon colour", Color) = (0.91, 0.93, 0.97, 1)
        _HaloColor ("Halo colour", Color) = (0.17, 0.22, 0.34, 1)
        _HaloSize ("Halo reach (moon radii)", Float) = 9
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
            TEXTURE2D(_MoonTex);
            SAMPLER(sampler_MoonTex);

            CBUFFER_START(UnityPerMaterial)
                half _Exposure;
                half4 _HorizonColor;
                half _HorizonHeight;
                half4 _GroundColor;
                float4 _MoonDirection;
                float4 _MoonRight;
                float4 _MoonUp;
                float _MoonRadius;
                half4 _MoonColor;
                half4 _HaloColor;
                half _HaloSize;
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
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 d = normalize(input.direction);
                float2 uv = float2(atan2(d.x, d.z) * (0.5 * INV_PI) + 0.5, asin(clamp(d.y, -1, 1)) * INV_PI + 0.5);
                half3 sky = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, uv, 0).rgb;

                // The moon: where the view meets its disc, in moon radii from its centre; only the half of the sky it faces.
                float2 local = float2(dot(d, _MoonRight.xyz), dot(d, _MoonUp.xyz)) / _MoonRadius;
                float r = dot(d, _MoonDirection.xyz) > 0 ? length(local) : 1e4;
                float2 faceUV = saturate(local * 0.5 + 0.5);
                float2 faceDx = ddx(faceUV), faceDy = ddy(faceUV);
                float edge = clamp(fwidth(r), 1e-4, 0.5);
                half disc = 1 - smoothstep(1 - edge, 1 + edge, r);
                // A tight corona and a wide, faint glow, both fading out well before the halo's reach.
                half outside = max(r - 1, 0);
                half halo = 0.5 * exp(-outside * 2.2) + 0.5 * exp(-outside * 3 / max(_HaloSize, 1));
                sky += _HaloColor.rgb * halo * (1 - disc);
                [branch] if (disc > 0)
                {
                    half face = SAMPLE_TEXTURE2D_GRAD(_MoonTex, sampler_MoonTex, faceUV, faceDx, faceDy).r;
                    // A gentle limb: the edge a little dimmer than the middle, never a dark ring.
                    half limb = lerp(0.72, 1, sqrt(saturate(1 - r * r)));
                    sky = lerp(sky, _MoonColor.rgb * face * limb, disc);
                }
                sky *= _Exposure;

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
