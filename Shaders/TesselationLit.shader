Shader "Custom/TessellationLit"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor("Base Color", Color) = (1, 1, 1, 1)

        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.5

        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1

        _OcclusionMap("Occlusion Map", 2D) = "white" {}
        _OcclusionStrength("Occlusion Strength", Range(0, 1)) = 1

        _TessFactor("Tessellation Factor", Range(1, 64)) = 15
        _MaxTessDistance("Max Tess Distance", Range(1, 100)) = 25
        _VectorDisplacementMap("Vector Displacement Map (RGB = XYZ)", 2D) = "gray" {}
        _DisplacementStrength("Displacement Strength", Range(0, 0.5)) = 0.05
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        LOD 300

        // ========== ForwardLit Pass ==========
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.6
            #pragma vertex TessellationVertexProgram
            #pragma fragment Frag
            #pragma hull Hull
            #pragma domain Domain
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _MIXED_LIGHTING_SUBTRACTIVE
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED LIGHTMAP_ON
            #pragma multi_compile _ SHADOWS_SHADOWMASK LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // --- Material properties ---
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
            TEXTURE2D(_VectorDisplacementMap); SAMPLER(sampler_VectorDisplacementMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _Metallic;
                float _Smoothness;
                float _BumpScale;
                float _OcclusionStrength;
                float _TessFactor;
                float _MaxTessDistance;
                float _DisplacementStrength;
            CBUFFER_END

            // --- Tessellation structures ---
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 texcoord   : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ControlPoint
            {
                float4 positionOS : INTERNALTESSPOS;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 texcoord   : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                float2 lightmapUV : TEXCOORD4;
                float fogFactor : TEXCOORD5;
                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                    half3 vertexLighting : TEXCOORD6;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            struct TessellationFactors
            {
                float edge[3] : SV_TessFactor;
                float inside  : SV_InsideTessFactor;
            };

            // --- Helper functions ---
            float3 GetCameraPositionWS_Compat()
            {
                #if defined(USING_STEREO_MATRICES)
                    return unity_StereoWorldSpaceCameraPos[0];
                #else
                    return _WorldSpaceCameraPos;
                #endif
            }

            TessellationFactors PatchConstantFunction(InputPatch<ControlPoint, 3> patch)
            {
                TessellationFactors f;
                float3 centerWS = (patch[0].positionOS.xyz + patch[1].positionOS.xyz + patch[2].positionOS.xyz) / 3.0;
                float dist = distance(centerWS, GetCameraPositionWS_Compat());
                float tess = max(1.0, _TessFactor * (1.0 - saturate(dist / _MaxTessDistance)));
                f.edge[0] = tess; f.edge[1] = tess; f.edge[2] = tess;
                f.inside = tess;
                return f;
            }

            [domain("tri")]
            [partitioning("fractional_odd")]
            [outputtopology("triangle_cw")]
            [patchconstantfunc("PatchConstantFunction")]
            [outputcontrolpoints(3)]
            ControlPoint Hull(InputPatch<ControlPoint, 3> patch, uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            ControlPoint TessellationVertexProgram(Attributes input)
            {
                ControlPoint output;
                output.positionOS = input.positionOS;
                output.normalOS = input.normalOS;
                output.tangentOS = input.tangentOS;
                output.texcoord = input.texcoord;
                output.lightmapUV = input.lightmapUV;
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                return output;
            }

            Varyings ProcessGeneratedVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float2 uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
                float3 displacementVector = SAMPLE_TEXTURE2D_LOD(_VectorDisplacementMap, sampler_VectorDisplacementMap, uv, 0).rgb;
                displacementVector = displacementVector * 2.0 - 1.0;
                float3 displacedPosOS = input.positionOS.xyz + (displacementVector * _DisplacementStrength);

                VertexPositionInputs vertexInput = GetVertexPositionInputs(displacedPosOS);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = normalInput.normalWS;
                output.tangentWS = float4(normalInput.tangentWS, input.tangentOS.w);
                output.uv = uv;
                output.lightmapUV = input.lightmapUV * unity_LightmapST.xy + unity_LightmapST.zw;
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);

                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                    output.vertexLighting = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
                #endif
                return output;
            }

            [domain("tri")]
            Varyings Domain(TessellationFactors factors, OutputPatch<ControlPoint, 3> patch, float3 bary : SV_DomainLocation)
            {
                Attributes v;
                v.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
                v.normalOS   = patch[0].normalOS   * bary.x + patch[1].normalOS   * bary.y + patch[2].normalOS   * bary.z;
                v.tangentOS  = patch[0].tangentOS  * bary.x + patch[1].tangentOS  * bary.y + patch[2].tangentOS  * bary.z;
                v.texcoord   = patch[0].texcoord   * bary.x + patch[1].texcoord   * bary.y + patch[2].texcoord   * bary.z;
                v.lightmapUV = patch[0].lightmapUV * bary.x + patch[1].lightmapUV * bary.y + patch[2].lightmapUV * bary.z;
                UNITY_TRANSFER_INSTANCE_ID(patch[0], v);
                return ProcessGeneratedVertex(v);
            }

            void InitializeSurfaceData(Varyings input, out SurfaceData surfaceData)
            {
                surfaceData = (SurfaceData)0;
                float4 albedoAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                surfaceData.albedo = albedoAlpha.rgb * _BaseColor.rgb;
                surfaceData.alpha = albedoAlpha.a * _BaseColor.a;
                surfaceData.metallic = _Metallic;
                surfaceData.smoothness = _Smoothness;

                float4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv);
                surfaceData.normalTS = UnpackNormalScale(normalSample, _BumpScale);

                float occlusion = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, input.uv).r;
                surfaceData.occlusion = lerp(1.0, occlusion, _OcclusionStrength);
                surfaceData.emission = float3(0,0,0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SurfaceData surfaceData;
                InitializeSurfaceData(input, surfaceData);

                BRDFData brdfData;
                InitializeBRDFData(surfaceData.albedo, surfaceData.metallic, surfaceData.specular, surfaceData.smoothness, surfaceData.alpha, brdfData);
                BRDFData brdfDataClearCoat = (BRDFData)0;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                    inputData.vertexLighting = input.vertexLighting;
                #endif
                inputData.bakedGI = SampleLightmap(input.lightmapUV, inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                #if defined(SHADOWS_SHADOWMASK)
                    inputData.shadowMask = SampleShadowMask(input.lightmapUV);
                #endif

                float3 bitangentWS = cross(input.normalWS, input.tangentWS.xyz) * input.tangentWS.w;
                float3x3 tbn = float3x3(input.tangentWS.xyz, bitangentWS, input.normalWS);
                float3 normalWS = TransformTangentToWorld(surfaceData.normalTS, tbn);
                inputData.normalWS = NormalizeNormalPerPixel(normalWS);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);

                #if defined(_ADDITIONAL_LIGHTS)
                    uint pixelLightCount = GetAdditionalLightsCount();
                    AmbientOcclusionFactor aoFactor;
                    aoFactor.indirectAmbientOcclusion = 1;
                    aoFactor.directAmbientOcclusion = 1;
                    #if defined(_CLUSTER_LIGHT_LOOP)
                        for (uint lightIndex = 0; lightIndex < pixelLightCount; lightIndex++)
                        {
                            Light light = GetAdditionalLight(lightIndex, inputData, inputData.shadowMask, aoFactor);
                            half atten = light.distanceAttenuation * light.shadowAttenuation;
                            half3 additionalColor = LightingPhysicallyBased(brdfData, brdfDataClearCoat,
                                light.color, light.direction, atten,
                                inputData.normalWS, inputData.viewDirectionWS, half(0), false);
                            color.rgb += additionalColor;
                        }
                    #else
                        LIGHT_LOOP_BEGIN(pixelLightCount)
                            Light light = GetAdditionalLight(lightIndex, inputData, inputData.shadowMask, aoFactor);
                            half atten = light.distanceAttenuation * light.shadowAttenuation;
                            half3 additionalColor = LightingPhysicallyBased(brdfData, brdfDataClearCoat,
                                light.color, light.direction, atten,
                                inputData.normalWS, inputData.viewDirectionWS, half(0), false);
                            color.rgb += additionalColor;
                        LIGHT_LOOP_END
                    #endif
                #endif

                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        // ========== ShadowCaster Pass ==========
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.6
            #pragma vertex TessellationVertexProgramShadow
            #pragma fragment ShadowPassFragment
            #pragma hull HullShadow
            #pragma domain DomainShadow
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_VectorDisplacementMap); SAMPLER(sampler_VectorDisplacementMap);
            float _DisplacementStrength;
            float _TessFactor;
            float _MaxTessDistance;
            float4 _BaseMap_ST;

            struct AttributesShadow
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 texcoord   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ControlPointShadow
            {
                float4 positionOS : INTERNALTESSPOS;
                float3 normalOS   : NORMAL;
                float2 texcoord   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsShadow
            {
                float4 positionCS : SV_POSITION;
            };

            struct TessellationFactors
            {
                float edge[3] : SV_TessFactor;
                float inside  : SV_InsideTessFactor;
            };

            TessellationFactors PatchConstantFunctionShadow(InputPatch<ControlPointShadow, 3> patch)
            {
                TessellationFactors f;
                float3 centerWS = (patch[0].positionOS.xyz + patch[1].positionOS.xyz + patch[2].positionOS.xyz) / 3.0;
                float dist = distance(centerWS, _WorldSpaceCameraPos);
                float tess = max(1.0, _TessFactor * (1.0 - saturate(dist / _MaxTessDistance)));
                f.edge[0] = tess; f.edge[1] = tess; f.edge[2] = tess;
                f.inside = tess;
                return f;
            }

            [domain("tri")]
            [partitioning("fractional_odd")]
            [outputtopology("triangle_cw")]
            [patchconstantfunc("PatchConstantFunctionShadow")]
            [outputcontrolpoints(3)]
            ControlPointShadow HullShadow(InputPatch<ControlPointShadow, 3> patch, uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            ControlPointShadow TessellationVertexProgramShadow(AttributesShadow input)
            {
                ControlPointShadow output;
                output.positionOS = input.positionOS;
                output.normalOS = input.normalOS;
                output.texcoord = input.texcoord;
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                return output;
            }

            VaryingsShadow ProcessGeneratedVertexShadow(AttributesShadow input)
            {
                VaryingsShadow output;
                // Исправлено: явное вычисление UV без макроса TRANSFORM_TEX
                float2 uv = input.texcoord * _BaseMap_ST.xy + _BaseMap_ST.zw;
                float3 displacementVector = SAMPLE_TEXTURE2D_LOD(_VectorDisplacementMap, sampler_VectorDisplacementMap, uv, 0).rgb;
                displacementVector = displacementVector * 2.0 - 1.0;
                float3 displacedPosOS = input.positionOS.xyz + (displacementVector * _DisplacementStrength);
                output.positionCS = TransformObjectToHClip(displacedPosOS);
                return output;
            }

            [domain("tri")]
            VaryingsShadow DomainShadow(TessellationFactors factors, OutputPatch<ControlPointShadow, 3> patch, float3 bary : SV_DomainLocation)
            {
                AttributesShadow v;
                v.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
                v.normalOS   = patch[0].normalOS   * bary.x + patch[1].normalOS   * bary.y + patch[2].normalOS   * bary.z;
                v.texcoord   = patch[0].texcoord   * bary.x + patch[1].texcoord   * bary.y + patch[2].texcoord   * bary.z;
                UNITY_TRANSFER_INSTANCE_ID(patch[0], v);
                return ProcessGeneratedVertexShadow(v);
            }

            half4 ShadowPassFragment(VaryingsShadow input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }

        // ========== DepthOnly Pass ==========
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.6
            #pragma vertex TessellationVertexProgramDepth
            #pragma fragment DepthOnlyFragment
            #pragma hull HullDepth
            #pragma domain DomainDepth
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_VectorDisplacementMap); SAMPLER(sampler_VectorDisplacementMap);
            float _DisplacementStrength;
            float _TessFactor;
            float _MaxTessDistance;
            float4 _BaseMap_ST;

            struct AttributesDepth
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 texcoord   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ControlPointDepth
            {
                float4 positionOS : INTERNALTESSPOS;
                float3 normalOS   : NORMAL;
                float2 texcoord   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VaryingsDepth
            {
                float4 positionCS : SV_POSITION;
            };

            struct TessellationFactors
            {
                float edge[3] : SV_TessFactor;
                float inside  : SV_InsideTessFactor;
            };

            TessellationFactors PatchConstantFunctionDepth(InputPatch<ControlPointDepth, 3> patch)
            {
                TessellationFactors f;
                float3 centerWS = (patch[0].positionOS.xyz + patch[1].positionOS.xyz + patch[2].positionOS.xyz) / 3.0;
                float dist = distance(centerWS, _WorldSpaceCameraPos);
                float tess = max(1.0, _TessFactor * (1.0 - saturate(dist / _MaxTessDistance)));
                f.edge[0] = tess; f.edge[1] = tess; f.edge[2] = tess;
                f.inside = tess;
                return f;
            }

            [domain("tri")]
            [partitioning("fractional_odd")]
            [outputtopology("triangle_cw")]
            [patchconstantfunc("PatchConstantFunctionDepth")]
            [outputcontrolpoints(3)]
            ControlPointDepth HullDepth(InputPatch<ControlPointDepth, 3> patch, uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            ControlPointDepth TessellationVertexProgramDepth(AttributesDepth input)
            {
                ControlPointDepth output;
                output.positionOS = input.positionOS;
                output.normalOS = input.normalOS;
                output.texcoord = input.texcoord;
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                return output;
            }

            VaryingsDepth ProcessGeneratedVertexDepth(AttributesDepth input)
            {
                VaryingsDepth output;
                // Исправлено: явное вычисление UV без макроса TRANSFORM_TEX
                float2 uv = input.texcoord * _BaseMap_ST.xy + _BaseMap_ST.zw;
                float3 displacementVector = SAMPLE_TEXTURE2D_LOD(_VectorDisplacementMap, sampler_VectorDisplacementMap, uv, 0).rgb;
                displacementVector = displacementVector * 2.0 - 1.0;
                float3 displacedPosOS = input.positionOS.xyz + (displacementVector * _DisplacementStrength);
                output.positionCS = TransformObjectToHClip(displacedPosOS);
                return output;
            }

            [domain("tri")]
            VaryingsDepth DomainDepth(TessellationFactors factors, OutputPatch<ControlPointDepth, 3> patch, float3 bary : SV_DomainLocation)
            {
                AttributesDepth v;
                v.positionOS = patch[0].positionOS * bary.x + patch[1].positionOS * bary.y + patch[2].positionOS * bary.z;
                v.normalOS   = patch[0].normalOS   * bary.x + patch[1].normalOS   * bary.y + patch[2].normalOS   * bary.z;
                v.texcoord   = patch[0].texcoord   * bary.x + patch[1].texcoord   * bary.y + patch[2].texcoord   * bary.z;
                UNITY_TRANSFER_INSTANCE_ID(patch[0], v);
                return ProcessGeneratedVertexDepth(v);
            }

            half4 DepthOnlyFragment(VaryingsDepth input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}