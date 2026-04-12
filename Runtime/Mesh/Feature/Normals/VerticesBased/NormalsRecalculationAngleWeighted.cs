using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Proxy.Mesh.Normals
{
    [System.Serializable]
    public class NormalsRecalculationAngleWeighted : InternalFeature
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
                    updateIndices = proxy.normalsRecalculation.updateIndicesList,
                }.Schedule(proxy.normalsRecalculation.updateIndicesList, 64, dependsOn);
            }
            JobHandle RecalculateNormals()
            {
                return new RecalculateNormalsJob()
                {
                    vertexCount = proxy.vertexCount,
                    vertices = proxy.animatedVertices,
                    normals = proxy.animatedNormals,
                    triangles = proxy.triangles,
                    worldToLocalMatrix = proxy.worldToLocalMatrix,
                    additionalDeformation = proxy.normalsRecalculation.additiveDeformation,
                    updateIndices = proxy.normalsRecalculation.updateIndices.AsReadOnly()
                }.Schedule(proxy.triangles.Length, 32, dependsOn);
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

                    // Векторы рёбер
                    float3 e1 = p1 - p0;
                    float3 e2 = p2 - p0;
                    float3 e3 = p2 - p1;

                    // Нормаль треугольника (не нормированная – пропорциональна площади)
                    float3 triangleNormal = math.normalize(math.cross(e1, e2));

                    // Длины сторон
                    float lenAB = math.length(e1);
                    float lenAC = math.length(e2);
                    float lenBC = math.length(e3);

                    // Углы при каждой вершине (в радианах)
                    float angleA = 0f, angleB = 0f, angleC = 0f;

                    float cosA = math.dot(e1, e2) / (lenAB * lenAC);
                    cosA = math.clamp(cosA, -1f, 1f);
                    angleA = math.acos(cosA);

                    float3 ba = -e1;
                    float3 bc = e3;
                    float cosB = math.dot(ba, bc) / (lenAB * lenBC);
                    cosB = math.clamp(cosB, -1f, 1f);
                    angleB = math.acos(cosB);

                    float3 ca = -e2;
                    float3 cb = -e3;
                    float cosC = math.dot(ca, cb) / (lenAC * lenBC);
                    cosC = math.clamp(cosC, -1f, 1f);
                    angleC = math.acos(cosC);

                    if (updateIndices.Contains(tri.x)) normals[tri.x] += triangleNormal * angleA;
                    if (updateIndices.Contains(tri.y)) normals[tri.y] += triangleNormal * angleB;
                    if (updateIndices.Contains(tri.z)) normals[tri.z] += triangleNormal * angleC;
                }
            }
        }

        [BurstCompile]
        public struct NormalizeJob : IJob
        {
            public NativeArray<float3> normals;
            [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;
            public void Execute()
            {
                foreach (int i in updateIndices) 
                    normals[i] = math.normalize(normals[i]);
            }
        }
    }
}