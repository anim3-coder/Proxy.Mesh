Shader "Proxy/TessellationShader"
{
    Properties
    {
        _TessFactor ("Tessellation Factor", Range(1, 64)) = 20
        _MaxTessDistance ("Max Tess Distance", Range(1, 100)) = 20
        _Noise ("Displacement Noise", 2D) = "gray" {}
        _Weight ("Displacement Amount", Range(0, 0.5)) = 0.1
        _Color ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalRenderPipeline" }
        LOD 100
        
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            
            HLSLPROGRAM
            #pragma target 4.6

            #pragma vertex TessellationVertexProgram
            #pragma fragment Frag
            #pragma hull Hull
            #pragma domain Domain

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // --- Свойства материала ---
            float _TessFactor;
            float _MaxTessDistance;
            sampler2D _Noise;
            float _Weight;
            float4 _Color;

            // --- Структуры данных ---
            struct Attributes
            {
                float3 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct ControlPoint
            {
                float3 vertex : INTERNALTESSPOS;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct TessellationFactors
            {
                float edge[3] : SV_TessFactor;
                float inside : SV_InsideTessFactor;
            };

            // --- Вспомогательная функция получения позиции камеры ---
            float3 GetCameraPositionWS_Compat()
            {
                #if defined(USING_STEREO_MATRICES)
                    return unity_StereoWorldSpaceCameraPos[0];
                #else
                    return _WorldSpaceCameraPos;
                #endif
            }

            // --- Patch Constant Function (вычисляет факторы тесселяции) ---
            TessellationFactors PatchConstantFunction(InputPatch<ControlPoint, 3> patch)
            {
                TessellationFactors f;
                float3 centerWS = (patch[0].vertex + patch[1].vertex + patch[2].vertex) / 3.0;
                float dist = distance(centerWS, GetCameraPositionWS_Compat());
                float tess = max(1.0, _TessFactor * (1.0 - saturate(dist / _MaxTessDistance)));
                f.edge[0] = tess;
                f.edge[1] = tess;
                f.edge[2] = tess;
                f.inside = tess;
                return f;
            }

            // --- Hull Shader ---
            [domain("tri")]
            [partitioning("fractional_odd")]
            [outputtopology("triangle_cw")]
            [patchconstantfunc("PatchConstantFunction")]
            [outputcontrolpoints(3)]
            ControlPoint Hull(InputPatch<ControlPoint, 3> patch, uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            // --- Вершинный шейдер для тесселяции (готовит контрольные точки) ---
            ControlPoint TessellationVertexProgram(Attributes v)
            {
                ControlPoint p;
                p.vertex = v.vertex;
                p.normal = v.normal;
                p.uv = v.uv;
                p.color = v.color;
                return p;
            }

            // --- Функция, обрабатывающая каждую сгенерированную вершину (дисплейсмент) ---
            Varyings ProcessGeneratedVertex(Attributes input)
            {
                Varyings output;
                float4 noise = tex2Dlod(_Noise, float4(input.uv + (_Time.x * 0.1), 0, 0));
                input.vertex.xyz += normalize(input.normal) * noise.r * _Weight;
                output.vertex = TransformObjectToHClip(input.vertex.xyz);
                output.uv = input.uv;
                output.normal = input.normal;
                output.color = input.color;
                return output;
            }

            // --- Domain Shader (интерполирует данные и вызывает ProcessGeneratedVertex) ---
            [domain("tri")]
            Varyings Domain(TessellationFactors factors, OutputPatch<ControlPoint, 3> patch, float3 bary : SV_DomainLocation)
            {
                Attributes v;
                v.vertex = patch[0].vertex * bary.x + patch[1].vertex * bary.y + patch[2].vertex * bary.z;
                v.normal = patch[0].normal * bary.x + patch[1].normal * bary.y + patch[2].normal * bary.z;
                v.uv     = patch[0].uv     * bary.x + patch[1].uv     * bary.y + patch[2].uv     * bary.z;
                v.color  = patch[0].color  * bary.x + patch[1].color  * bary.y + patch[2].color  * bary.z;
                return ProcessGeneratedVertex(v);
            }

            // --- Фрагментный шейдер ---
            float4 Frag(Varyings input) : SV_Target
            {
                return _Color;
            }
            
            ENDHLSL
        }
    }
}