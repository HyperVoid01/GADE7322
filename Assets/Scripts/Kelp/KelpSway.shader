Shader "Scuba/KelpSway"
{
    Properties
    {
        [Header(Surface)]
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (0.25, 0.6, 0.35, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5

        [Header(Sway  Main Current)]
        _CurrentDirection ("Current Direction (XZ)", Vector) = (1, 0, 0.3, 0)
        _SwayStrength ("Sway Strength", Range(0, 2)) = 0.4
        _SwaySpeed ("Sway Speed", Range(0, 5)) = 1.2
        _SwayFrequency ("Sway Frequency (along height)", Range(0, 5)) = 0.8

        [Header(Flutter  Small Side Wobble)]
        _FlutterStrength ("Flutter Strength", Range(0, 0.5)) = 0.08
        _FlutterSpeed ("Flutter Speed", Range(0, 10)) = 3.5
        _FlutterFrequency ("Flutter Frequency", Range(0, 10)) = 3.0

        [Header(Mask)]
        _PlantHeight ("Plant Height (object space)", Float) = 2.0
        _MaskPower ("Mask Power", Range(1, 4)) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off // kelp leaves are usually single-sided quads

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // All material properties in one CBUFFER so the SRP Batcher works
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff;
                float4 _CurrentDirection;
                float _SwayStrength;
                float _SwaySpeed;
                float _SwayFrequency;
                float _FlutterStrength;
                float _FlutterSpeed;
                float _FlutterFrequency;
                float _PlantHeight;
                float _MaskPower;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
            };

            float3 ApplySway(float3 positionOS)
            {
                // Object origin in world space, used so each plant sways out of sync
                float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
                float phase = dot(originWS.xz, float2(0.37, 0.73));

                // 0 at the base, 1 at the tip. The power curve keeps the base pinned
                float height01 = saturate(positionOS.y / max(_PlantHeight, 0.0001));
                float mask = pow(height01, _MaskPower);

                // Main current: a slow wave travelling up the stalk
                float2 currentDir = normalize(_CurrentDirection.xz + 0.0001);
                float swayTime = _Time.y * _SwaySpeed + phase;
                float sway = sin(swayTime - positionOS.y * _SwayFrequency);
                // Bias forward so the plant leans with the current instead of swinging evenly
                sway = sway * 0.5 + 0.5 * sin(swayTime * 0.5 + phase);

                // Flutter: faster, smaller wobble perpendicular to the current
                float2 sideDir = float2(-currentDir.y, currentDir.x);
                float flutter = sin(_Time.y * _FlutterSpeed + positionOS.y * _FlutterFrequency + phase * 2.0);

                float3 positionWS = TransformObjectToWorld(positionOS);
                float2 offset = currentDir * sway * _SwayStrength + sideDir * flutter * _FlutterStrength;
                positionWS.xz += offset * mask;

                // Pull the tip down slightly as it bends so the stalk doesn't look stretched
                positionWS.y -= length(offset) * mask * 0.3;

                return positionWS;
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                float3 positionWS = ApplySway(IN.positionOS.xyz);

                OUT.positionWS = positionWS;
                OUT.positionCS = TransformWorldToHClip(positionWS);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);

                return OUT;
            }

            half4 Frag(Varyings IN, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);

                // Flip the normal on back faces since Cull is Off
                float3 normalWS = normalize(IN.normalWS) * (isFrontFace ? 1.0 : -1.0);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // Half-Lambert: softer, wraps light around thin foliage
                half ndl = dot(normalWS, mainLight.direction) * 0.5 + 0.5;
                half3 diffuse = mainLight.color * ndl * mainLight.shadowAttenuation;
                half3 ambient = SampleSH(normalWS);

                half3 color = albedo.rgb * (diffuse + ambient);
                color = MixFog(color, IN.fogFactor); // underwater fog applies to the kelp too

                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}