using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Proxy.Mesh.Normals
{
    [System.Serializable]
    public class NormalsRecalculationSimple : InternalFeature
    {
        public override JobHandle StartJob(JobHandle dependsOn)
        {
            int t = proxy.triangles.Length/3;
            dependsOn = Clear();
            dependsOn = RecalculateNormals();
            return dependsOn;

            JobHandle Clear()
            {
                return new ClearNormalsJob()
                {
                    normals = proxy.animatedNormals,
                    updateIndices = proxy.normalsRecalculation.updateIndices.AsReadOnly(),
                }.Schedule(dependsOn);
            }
            JobHandle RecalculateNormals()
            {
                return new RecalculateNormalsJob()
                {
                    vertexCount = proxy.vertexCount,
                    vertices = proxy.animatedVertices,
                    normals = proxy.animatedNormals,
                    triangles = proxy.triangles,
                    updateIndices = proxy.normalsRecalculation.updateIndices.AsReadOnly()
                }.Schedule(t, Mathf.Max(1, t / 100), dependsOn);
            }
        }
    }

    [BurstCompile]
    public struct ClearNormalsJob : IJob
    {
        [WriteOnly] public NativeArray<float3> normals;
        [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;
        public void Execute()
        {
            foreach (var i in updateIndices)
                normals[i] = float3.zero;
        }
    }

    [BurstCompile]
    public struct RecalculateNormalsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> vertices;
        [NativeDisableParallelForRestriction] public NativeArray<float3> normals;
        [ReadOnly] public NativeArray<int> triangles;
        [ReadOnly] public int vertexCount;
        [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;
        public void Execute(int i)
        {
            int v0 = triangles[i * 3];
            int v1 = triangles[i * 3 + 1];
            int v2 = triangles[i * 3 + 2];

            if (updateIndices.Contains(v0) && updateIndices.Contains(v1) && updateIndices.Contains(v2))
            {
                if (v0 >= vertexCount || v1 >= vertexCount || v2 >= vertexCount)
                {
                    return;
                }

                float3 p0 = vertices[v0];
                float3 p1 = vertices[v1];
                float3 p2 = vertices[v2];

                float3 edge1 = p1 - p0;
                float3 edge2 = p2 - p0;

                float3 triangleNormal = math.cross(edge1, edge2);

                normals[v0] += triangleNormal;
                normals[v1] += triangleNormal;
                normals[v2] += triangleNormal;
            }
        }
    }
}