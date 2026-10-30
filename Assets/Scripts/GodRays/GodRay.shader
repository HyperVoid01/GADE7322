Shader "Custom/URP/GodRay"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.55, 0.85, 1, 0.35)

        [Header(Shape)]
        _EdgeSoftness ("Edge Softness", Range(0.01, 0.5)) = 0.35
        _TopFade ("Top Fade", Range(0.01, 1)) = 0.35
        _BottomFade ("Bottom Fade", Range(0.01, 1)) = 0.15
        _BottomSpread ("Bottom Spread", Range(1, 3)) = 1.6

        [Header(Streaks)]
        _StreakDensity ("Streak Density", Float) = 6
        _StreakSpeed ("Streak Speed", Float) = 0.15
        _StreakStrength ("Streak Strength", Range(0, 1)) = 0.7
        _PulseSpeed ("Pulse Speed", Float) = 0.8

        [Header(Vertex Motion)]
        _Slant ("Slant Toward Light", Range(0, 1)) = 1
        _SwayAmount ("Sway Amount", Float) = 0.6
        _SwaySpeed ("Sway Speed", Float) = 0.6

        [Header(Fading)]
        _CameraFadeDistance ("Camera Fade Distance", Float) = 4
        _SoftIntersection ("Soft Intersection Distance", Float) = 3
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend One One   // Additive: light only brightens
        ZWrite Off
        Cull Off

        Pass
        {
            Name "GodRay"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float  seed       : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _EdgeSoftness;
                float _TopFade;
                float _BottomFade;
                float _BottomSpread;
                float _StreakDensity;
                float _StreakSpeed;
                float _StreakStrength;
                float _PulseSpeed;
                float _Slant;
                float _SwayAmount;
                float _SwaySpeed;
                float _CameraFadeDistance;
                float _SoftIntersection;
            CBUFFER_END

            // Unique names so they don't clash with URP's built-in Hash functions
            float GodRayHash(float n)
            {
                return frac(sin(n) * 43758.5453);
            }

            // Smooth 1D value noise, used for the light streaks
            float GodRayNoise1D(float x)
            {
                float i = floor(x);
                float f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(GodRayHash(i), GodRayHash(i + 1.0), f);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // Object pivot and scale in world space
                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float scaleX = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
                float scaleY = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));

                // Unique per-shaft random value so shafts don't move in sync
                float seed = frac(sin(dot(centerWS.xz, float2(12.9898, 78.233))) * 43758.5453);

                // Shaft axis points UP toward the main directional light
                // (GodRaySpawner uses the exact same maths to raycast to the ground)
                float3 lightDir = normalize(_MainLightPosition.xyz);
                float3 axis = normalize(float3(lightDir.x * _Slant, max(lightDir.y, 0.3), lightDir.z * _Slant));

                // Billboard around the axis so the quad always faces the camera
                float3 toCam = normalize(GetCameraPositionWS() - centerWS);
                float3 right = cross(axis, toCam);
                right = length(right) > 0.001 ? normalize(right) : float3(1, 0, 0);

                // 0 at the bottom of the quad, 1 at the top
                float t = IN.positionOS.y + 0.5;

                // Wider at the bottom, like light spreading through water
                float width = IN.positionOS.x * scaleX * lerp(_BottomSpread, 1.0, t);

                // Bottom sways, top stays anchored at the surface
                float sway = sin(_Time.y * _SwaySpeed + seed * 6.2831) * _SwayAmount * (1.0 - t);

                float3 positionWS = centerWS
                                  + right * (width + sway)
                                  + axis * (IN.positionOS.y * scaleY);

                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.screenPos = ComputeScreenPos(OUT.positionCS);
                OUT.uv = IN.uv;
                OUT.seed = seed;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;

                // Soft side edges
                float edge = smoothstep(0.0, _EdgeSoftness, uv.x) * smoothstep(0.0, _EdgeSoftness, 1.0 - uv.x);

                // Long fade at the top (hides where the shaft starts),
                // short fade at the bottom (the terrain intersection does the rest)
                float vertical = smoothstep(0.0, _BottomFade, uv.y) * smoothstep(0.0, _TopFade, 1.0 - uv.y);

                // Moving vertical streaks inside the shaft
                float time = _Time.y * _StreakSpeed;
                float streaks = GodRayNoise1D(uv.x * _StreakDensity + IN.seed * 50.0 + time) * 0.6
                              + GodRayNoise1D(uv.x * _StreakDensity * 2.3 - time * 1.7 + uv.y * 0.5) * 0.4;
                streaks = lerp(1.0, streaks, _StreakStrength);

                // Gentle brightness pulse
                float pulse = 0.75 + 0.25 * sin(_Time.y * _PulseSpeed + IN.seed * 6.2831);

                // Soft intersection with terrain so the shaft melts into the ground
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float fragDepth = IN.screenPos.w;
                float soft = saturate((sceneDepth - fragDepth) / _SoftIntersection);

                // Fade when the camera is inside or right next to the shaft
                float cameraFade = saturate((fragDepth - _ProjectionParams.y) / _CameraFadeDistance);

                // Fog: additive light should fade OUT in fog, not be tinted toward the fog colour
                float fogVisibility = 1.0;
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                    fogVisibility = ComputeFogIntensity(IN.fogFactor);
                #endif

                float intensity = edge * vertical * streaks * pulse * soft * cameraFade * fogVisibility * _Color.a;
                return half4(_Color.rgb * intensity, 0);
            }
            ENDHLSL
        }
    }
}