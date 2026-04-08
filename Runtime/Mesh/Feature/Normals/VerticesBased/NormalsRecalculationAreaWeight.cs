using Newtonsoft.Json.Linq;
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Proxy.Mesh.Normals
{
    [System.Serializable]
    public class NormalsRecalculationAreaWeight : InternalFeature
    {
        public override JobHandle StartJob(JobHandle dependsOn)
        {
            dependsOn = Clear();
            dependsOn = RecalculateNormals();

            return dependsOn;

            JobHandle Clear()
            {
                return new ClearNormalsJob()
                {
                    normals = proxy.animatedNormals,
                    previousNormals = proxy.normalsRecalculation.previousNormals,
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
                    worldToLocalMatrix = proxy.transform.worldToLocalMatrix,
                    additionalDeformation = proxy.normalsRecalculation.additiveDeformation,
                    updateIndices = proxy.normalsRecalculation.updateIndices.AsReadOnly()
                }.Schedule(proxy.triangles.Length, 32, dependsOn);
            }
        }
    }

    [BurstCompile]
    public struct ClearNormalsJob : IJob
    {
        public NativeArray<float3> normals;
        [WriteOnly] public NativeArray<float3> previousNormals;
        [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;
        public void Execute()
        {
            foreach (var i in updateIndices)
            {
                previousNormals[i] = normals[i];
                normals[i] = float3.zero;
            }
        }
    }

    [BurstCompile]
    public struct RecalculateNormalsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> vertices;
        [NativeDisableParallelForRestriction] public NativeArray<float3> normals;
        [ReadOnly] public NativeArray<int3> triangles;
        [ReadOnly] public int vertexCount;
        [ReadOnly] public NativeArray<float4> additionalDeformation;
        [ReadOnly] public float4x4 worldToLocalMatrix;
        [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;
        public void Execute(int i)
        {
            int3 tri = triangles[i];

            if (updateIndices.Contains(tri.x) || updateIndices.Contains(tri.y) || updateIndices.Contains(tri.z))
            {
                float3 p0 = vertices[tri.x] + math.transform(worldToLocalMatrix, additionalDeformation[tri.x].xyz);
                float3 p1 = vertices[tri.y] + math.transform(worldToLocalMatrix, additionalDeformation[tri.y].xyz);
                float3 p2 = vertices[tri.z] + math.transform(worldToLocalMatrix, additionalDeformation[tri.z].xyz);

                float3 edge1 = p1 - p0;
                float3 edge2 = p2 - p0;

                float3 triangleNormal = math.cross(edge1, edge2);

                if (updateIndices.Contains(tri.x)) normals[tri.x] += triangleNormal;
                if (updateIndices.Contains(tri.y)) normals[tri.y] += triangleNormal;
                if (updateIndices.Contains(tri.z)) normals[tri.z] += triangleNormal;
            }
        }
    }
}