Shader "Custom/TerrainTriplanarBlend"
{
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Brightness ("Brightness", Range(0.5, 3)) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.1

        [Header(Ground Layers)]
        [NoScaleOffset] _Tex0 ("Layer 0 (base ground)", 2D) = "white" {}
        _Tiling0 ("Layer 0 tiling (repeats per unit)", Float) = 0.15
        [NoScaleOffset] _Tex1 ("Layer 1 (patches)", 2D) = "white" {}
        _Tiling1 ("Layer 1 tiling", Float) = 0.15
        [NoScaleOffset] _Tex2 ("Layer 2 (slopes and cave walls)", 2D) = "white" {}
        _Tiling2 ("Layer 2 tiling", Float) = 0.15

        [Header(Path)]
        [NoScaleOffset] _PathTex ("Path", 2D) = "white" {}
        _PathTiling ("Path tiling", Float) = 0.15

        [Header(Blending)]
        _TriplanarSharpness ("Triplanar sharpness", Range(1, 16)) = 4

        [Header(Caustics)]
        _CausticsColor ("Caustics color", Color) = (0.7, 1, 1, 1)
        _CausticsIntensity ("Caustics intensity", Range(0, 5)) = 1.5
        _CausticsScale ("Caustics scale (smaller = bigger pattern)", Range(0.005, 0.2)) = 0.04
        _CausticsSpeed ("Caustics speed", Range(0, 2)) = 0.4
        _CausticsSharpness ("Caustics sharpness", Range(1, 16)) = 8
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Tex0);    SAMPLER(sampler_Tex0);
            TEXTURE2D(_Tex1);    SAMPLER(sampler_Tex1);
            TEXTURE2D(_Tex2);    SAMPLER(sampler_Tex2);
            TEXTURE2D(_PathTex); SAMPLER(sampler_PathTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Brightness;
                half _Smoothness;
                float _Tiling0;
                float _Tiling1;
                float _Tiling2;
                float _PathTiling;
                float _TriplanarSharpness;
                half4 _CausticsColor;
                half _CausticsIntensity;
                float _CausticsScale;
                float _CausticsSpeed;
                float _CausticsSharpness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half4  color      : TEXCOORD2;
                half   fogFactor  : TEXCOORD3;
            };

            // Projects the texture along all three axes and blends by how much the surface faces each one
            half3 Triplanar(TEXTURE2D_PARAM(tex, samp), float3 p, float3 blend, float tiling)
            {
                half3 x = SAMPLE_TEXTURE2D(tex, samp, p.zy * tiling).rgb;
                half3 y = SAMPLE_TEXTURE2D(tex, samp, p.xz * tiling).rgb;
                half3 z = SAMPLE_TEXTURE2D(tex, samp, p.xy * tiling).rgb;
                return x * blend.x + y * blend.y + z * blend.z;
            }

            // Tileable procedural caustics: a few layers of interfering sine waves.
            // Returns 0..1, bright where the "light net" lines are.
            float Caustics(float2 uv, float time, float sharpness)
            {
                const float TAU = 6.28318530718;
                const int ITERATIONS = 5;
                const float INTENSITY = 0.005;

                // GLSL-style mod so negative values wrap correctly
                float2 wrapped = uv * TAU;
                wrapped = wrapped - TAU * floor(wrapped / TAU);

                float2 p = wrapped - 250.0;
                float2 i = p;
                float c = 1.0;

                for (int n = 0; n < ITERATIONS; n++)
                {
                    float t = time * (1.0 - (3.5 / (float)(n + 1)));
                    i = p + float2(cos(t - i.x) + sin(t + i.y), sin(t - i.y) + cos(t + i.x));
                    c += 1.0 / length(float2(p.x / (sin(i.x + t) / INTENSITY), p.y / (cos(i.y + t) / INTENSITY)));
                }

                c /= (float)ITERATIONS;
                c = 1.17 - pow(abs(c), 1.4);
                return pow(abs(saturate(c)), sharpness);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);

                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color = IN.color;
                OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS = normalize(IN.normalWS);

                float3 blend = pow(abs(normalWS), _TriplanarSharpness);
                blend /= (blend.x + blend.y + blend.z);

                // Vertex color = layer weights. Normalized in case byte rounding left them slightly off.
                half4 w = IN.color;
                w /= max(w.r + w.g + w.b + w.a, 0.0001h);

                half3 albedo =
                    Triplanar(TEXTURE2D_ARGS(_Tex0, sampler_Tex0), IN.positionWS, blend, _Tiling0) * w.r +
                    Triplanar(TEXTURE2D_ARGS(_Tex1, sampler_Tex1), IN.positionWS, blend, _Tiling1) * w.g +
                    Triplanar(TEXTURE2D_ARGS(_Tex2, sampler_Tex2), IN.positionWS, blend, _Tiling2) * w.b +
                    Triplanar(TEXTURE2D_ARGS(_PathTex, sampler_PathTex), IN.positionWS, blend, _PathTiling) * w.a;

                albedo *= _Tint.rgb * _Brightness;

                // Hand the result to URP's own lighting so main light, additional lights,
                // shadows and ambient all behave exactly like the Lit shader
                InputData inputData = (InputData)0;
                inputData.positionWS = IN.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord = IN.fogFactor;
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = 0;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.smoothness = _Smoothness;
                surfaceData.occlusion = 1;
                surfaceData.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                // Caustics: projected straight down in world space, only where the main light
                // actually reaches the surface (facing it, and not in shadow)
                Light mainLight = GetMainLight(inputData.shadowCoord);
                half facing = saturate(dot(normalWS, mainLight.direction));
                half lit = facing * mainLight.shadowAttenuation;

                float2 causticUV = IN.positionWS.xz * _CausticsScale;
                float caustic = Caustics(causticUV, _Time.y * _CausticsSpeed, _CausticsSharpness);

                // Second, offset layer breaks up the obvious repetition
                float caustic2 = Caustics(causticUV * 0.73 + 17.3, _Time.y * _CausticsSpeed * 0.8 + 5.0, _CausticsSharpness);
                caustic = max(caustic, caustic2 * 0.6);

                color.rgb += albedo * _CausticsColor.rgb * caustic * _CausticsIntensity * lit;

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}