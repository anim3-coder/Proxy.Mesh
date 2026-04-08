using System;
using System.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Proxy.Mesh
{
    using TangensJobs.Mikktspace;
    using TangensJobs.Simple;

    [System.Serializable]
    public class TangentsRecalculation : IProxyAbstractChild, IProxyJob, IDisposable
    {
        [TriInspector.EnumToggleButtons] public Mode mode;
        public float4 value;
        public bool IsInit => proxyMesh != null && proxyMesh.TangentRecalculationEnabled;

        private ProxyMeshAbstract proxyMesh;

        private NativeArray<float3> tangentsAccumulate;
        private NativeArray<float3> bitangentsAccumulate;

        public void Dispose()
        {
            OnShutdown(proxyMesh);
        }

        public void OnInit(ProxyMeshAbstract proxyMesh)
        {
            this.proxyMesh = proxyMesh;

            if (IsInit && mode == Mode.Mikktspace)
            {
                tangentsAccumulate = new NativeArray<float3>(proxyMesh.vertexCount, Allocator.Persistent);
                bitangentsAccumulate = new NativeArray<float3>(proxyMesh.vertexCount, Allocator.Persistent);
            }
        }

        public void OnJobComplete()
        {
        }

        public void OnShutdown(ProxyMeshAbstract proxyMesh)
        {
            if(tangentsAccumulate.IsCreated) tangentsAccumulate.Dispose();
            if(bitangentsAccumulate.IsCreated) bitangentsAccumulate.Dispose();
        }

        public JobHandle StartJob(JobHandle dependsOn)
        {
            if (IsInit == false)
                return dependsOn;
            if ((!tangentsAccumulate.IsCreated || !bitangentsAccumulate.IsCreated) && mode == Mode.Mikktspace)
            {
                OnShutdown(proxyMesh);
                tangentsAccumulate = new NativeArray<float3>(proxyMesh.vertexCount, Allocator.Persistent);
                bitangentsAccumulate = new NativeArray<float3>(proxyMesh.vertexCount, Allocator.Persistent);
            }

            switch (mode)
            {
                default:
                case Mode.Mikktspace:
                    dependsOn = new TangensJobs.Mikktspace.ClearTangentsJob()
                    {
                        tangentsAcc = tangentsAccumulate,
                        bitangentsAcc = bitangentsAccumulate,
                        updateIndices = proxyMesh.normalsRecalculation.updateIndices.AsReadOnly(),
                    }.Schedule(dependsOn);

                    dependsOn = new AccumulateTangentsJob()
                    {
                        vertices = proxyMesh.animatedVertices,
                        uvs = proxyMesh.nativeUV,
                        triangles = proxyMesh.triangles,
                        updateIndices = proxyMesh.normalsRecalculation.updateIndices.AsReadOnly(),
                        tangentsAccumulate = tangentsAccumulate,
                        bitangentsAccumulate = bitangentsAccumulate,
                    }.Schedule(dependsOn);

                    dependsOn = new FinalizeTangentsJob()
                    {
                        normals = proxyMesh.animatedNormals,
                        tangentsAcc = tangentsAccumulate,
                        bitangentsAcc = bitangentsAccumulate,
                        updateIndices = proxyMesh.normalsRecalculation.updateIndices.AsReadOnly(),
                        animatedTangents = proxyMesh.animatedTangents,
                        value = value
                    }.Schedule(dependsOn);
                    break;
                case Mode.Simple:
                    dependsOn = new TangensJobs.Simple.ClearTangentsJob()
                    {
                        tangents = proxyMesh.animatedTangents,
                        updateIndices = proxyMesh.normalsRecalculation.updateIndices.AsReadOnly(),
                    }.Schedule(dependsOn);
                    
                    dependsOn = new RecalculateTangentsJob()
                    {
                        normals = proxyMesh.animatedNormals,
                        vertices = proxyMesh.animatedVertices,
                        uv = proxyMesh.nativeUV,
                        triangles = proxyMesh.triangles,
                        tangents = proxyMesh.animatedTangents,
                        updateIndices = proxyMesh.normalsRecalculation.updateIndices.AsReadOnly(),
                    }.Schedule(proxyMesh.triangles.Length, 32, dependsOn);
                    
                    dependsOn = new NormalizeTangentsJob()
                    {
                        tangents = proxyMesh.animatedTangents,
                        updateIndices = proxyMesh.normalsRecalculation.updateIndices.AsReadOnly(),
                    }.Schedule(dependsOn);
                    break;
            }
            return dependsOn;
        }

        public enum Mode
        {
            Simple, Mikktspace,
        }
    }

    namespace TangensJobs
    {
        namespace Simple
        {
            [BurstCompile]
            public struct ClearTangentsJob : IJob
            {
                [WriteOnly] public NativeArray<float4> tangents;
                [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;
                public void Execute()
                {
                    foreach (var i in updateIndices)
                        tangents[i] = float4.zero;
                }
            }

            [BurstCompile]
            public struct RecalculateTangentsJob : IJobParallelFor
            {
                [ReadOnly] public NativeArray<float3> vertices;
                [ReadOnly] public NativeArray<float3> normals;
                [ReadOnly] public NativeArray<float2> uv;
                [NativeDisableParallelForRestriction] public NativeArray<float4> tangents;
                [ReadOnly] public NativeArray<int3> triangles;
                [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;

                public void Execute(int triangleIndex)
                {
                    int i0 = triangles[triangleIndex].x;
                    int i1 = triangles[triangleIndex].y;
                    int i2 = triangles[triangleIndex].z;

                    // Проверяем, что все три вершины требуют обновления
                    if (!(updateIndices.Contains(i0) && updateIndices.Contains(i1) && updateIndices.Contains(i2)))
                        return;
                    // Получаем позиции и UV
                    float3 p0 = vertices[i0];
                    float3 p1 = vertices[i1];
                    float3 p2 = vertices[i2];

                    float2 uv0 = uv[i0];
                    float2 uv1 = uv[i1];
                    float2 uv2 = uv[i2];

                    // Вычисляем рёбра треугольника и дельты UV
                    float3 edge1 = p1 - p0;
                    float3 edge2 = p2 - p0;

                    float2 duv1 = uv1 - uv0;
                    float2 duv2 = uv2 - uv0;

                    // Вычисляем знаменатель для тангенса
                    float r = 1.0f / (duv1.x * duv2.y - duv1.y * duv2.x);
                    float3 tangent = (edge1 * duv2.y - edge2 * duv1.y) * r;

                    // Нормаль для этой вершины (можно взять среднюю из вершин, но для простоты используем переданную нормаль)
                    // В реальности нужно для каждой вершины суммировать тангенсы всех смежных треугольников.
                    // Здесь мы добавляем вклад в массив tangents для каждой вершины.

                    float3 n0 = normals[i0];
                    float3 n1 = normals[i1];
                    float3 n2 = normals[i2];

                    // Ортогонализация тангенса относительно нормали (Грам-Шмидт)
                    tangent = math.normalize(tangent - n0 * math.dot(n0, tangent));
                    float handedness = (math.dot(math.cross(n0, tangent), edge1) < 0.0f) ? -1.0f : 1.0f;

                    // Суммируем тангенсы для каждой вершины
                    tangents[i0] += new float4(tangent, handedness);
                    tangents[i1] += new float4(tangent, handedness);
                    tangents[i2] += new float4(tangent, handedness);
                }
            }

            [BurstCompile]
            public struct NormalizeTangentsJob : IJob
            {
                [NativeDisableParallelForRestriction] public NativeArray<float4> tangents;
                [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;
                public void Execute()
                {
                    foreach (int i in updateIndices)
                    {
                        float4 t = tangents[i];
                        if (math.lengthsq(t.xyz) > 1e-8f)
                        {
                            t.xyz = math.normalize(t.xyz);
                            // Сохраняем знак w (можно взять знак суммы, но для простоты оставляем как есть)
                            tangents[i] = new float4(t.xyz, math.sign(t.w));
                        }
                        else
                        {
                            tangents[i] = new float4(1, 0, 0, 1);
                        }
                    }
                }
            }
        }

        namespace Mikktspace
        {
            [BurstCompile]
            public struct ClearTangentsJob : IJob
            {
                [WriteOnly] public NativeArray<float3> tangentsAcc;
                [WriteOnly] public NativeArray<float3> bitangentsAcc;
                [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;

                public void Execute()
                {
                    foreach (var i in updateIndices)
                    {
                        tangentsAcc[i] = float3.zero;
                        bitangentsAcc[i] = float3.zero;
                    }
                }
            }

            [BurstCompile]
            public struct AccumulateTangentsJob : IJob
            {
                [ReadOnly] public NativeArray<float3> vertices;
                [ReadOnly] public NativeArray<float2> uvs;
                [ReadOnly] public NativeArray<int3> triangles;
                [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;

                [NativeDisableParallelForRestriction]
                public NativeArray<float3> tangentsAccumulate;
                [NativeDisableParallelForRestriction]
                public NativeArray<float3> bitangentsAccumulate;

                public void Execute()
                {
                    for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex++)
                    {
                        int v0 = triangles[triangleIndex].x;
                        int v1 = triangles[triangleIndex].y;
                        int v2 = triangles[triangleIndex].z;

                        if (!updateIndices.Contains(v0) || !updateIndices.Contains(v1) || !updateIndices.Contains(v2))
                            return;

                        float3 p0 = vertices[v0];
                        float3 p1 = vertices[v1];
                        float3 p2 = vertices[v2];

                        // UV-координаты
                        float2 uv0 = uvs[v0];
                        float2 uv1 = uvs[v1];
                        float2 uv2 = uvs[v2];

                        // Рёбра треугольника в пространстве и в UV-пространстве
                        float3 edge1 = p1 - p0;
                        float3 edge2 = p2 - p0;
                        float2 deltaUV1 = uv1 - uv0;
                        float2 deltaUV2 = uv2 - uv0;

                        float r = 1.0f / (deltaUV1.x * deltaUV2.y - deltaUV1.y * deltaUV2.x);
                        float3 tangent = (edge1 * deltaUV2.y - edge2 * deltaUV1.y) * r;
                        float3 bitangent = (edge2 * deltaUV1.x - edge1 * deltaUV2.x) * r;

                        tangentsAccumulate[v0] += tangent;
                        tangentsAccumulate[v1] += tangent;
                        tangentsAccumulate[v2] += tangent;
                        bitangentsAccumulate[v0] += bitangent;
                        bitangentsAccumulate[v1] += bitangent;
                        bitangentsAccumulate[v2] += bitangent;
                    }
                }
            }

            [BurstCompile]
            public struct FinalizeTangentsJob : IJob
            {
                [ReadOnly] public NativeArray<float3> normals;
                [ReadOnly] public NativeArray<float3> tangentsAcc;
                [ReadOnly] public NativeArray<float3> bitangentsAcc;
                [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;
                [ReadOnly] public float4 value;
                [WriteOnly] public NativeArray<float4> animatedTangents;

                public void Execute()
                {
                    foreach (int vertexIndex in updateIndices)
                    {
                        float3 n = math.normalize(normals[vertexIndex]);
                        float3 t = tangentsAcc[vertexIndex];
                        float3 b = bitangentsAcc[vertexIndex];

                        float3 tangent = math.normalize(t - n * math.dot(n, t));
                        float handedness = math.dot(math.cross(n, tangent), b) < 0 ? -1f : 1f;

                        animatedTangents[vertexIndex] = new float4(tangent, handedness);
                        animatedTangents[vertexIndex] = value;
                    }
                }
            }
        }
    }
}