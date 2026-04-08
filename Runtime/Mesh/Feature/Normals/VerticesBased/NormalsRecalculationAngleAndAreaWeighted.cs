using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Proxy.Mesh.Normals
{
    [System.Serializable]
    public class NormalsRecalculationAngleAndAreaWeighted : InternalFeature
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
                    vertices = proxy.animatedVertices,
                    normals = proxy.animatedNormals,
                    triangles = proxy.triangles,
                    worldToLocalMatrix = proxy.transform.worldToLocalMatrix,
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

                    float3 e1 = p1 - p0;
                    float3 e2 = p2 - p0;
                    float3 e3 = p2 - p1;

                    float3 triangleNormal = math.cross(e1, e2); // (1) Ненормализованная нормаль (уже включает площадь)

                    float lenAB = math.length(e1);
                    float lenAC = math.length(e2);
                    float lenBC = math.length(e3);

                    // (2) Расчёт углов
                    float angleA = 0f, angleB = 0f, angleC = 0f;

                    float cosA = math.clamp(math.dot(e1, e2) / (lenAB * lenAC), -1f, 1f);
                    angleA = math.acos(cosA);

                    float3 ba = -e1;
                    float3 bc = e3;
                    float cosB = math.clamp(math.dot(ba, bc) / (lenAB * lenBC), -1f, 1f);
                    angleB = math.acos(cosB);

                    float3 ca = -e2;
                    float3 cb = -e3;
                    float cosC = math.clamp(math.dot(ca, cb) / (lenAC * lenBC), -1f, 1f);
                    angleC = math.acos(cosC);


                    // (3) КЛЮЧЕВОЕ ИСПРАВЛЕНИЕ: Нормализуем сумму весов для численной стабильности
                    float totalWeight = angleA + angleB + angleC;

                    float invTotalWeight = 1f / totalWeight;
                    angleA *= invTotalWeight;
                    angleB *= invTotalWeight;
                    angleC *= invTotalWeight;


                    // (4) Добавляем взвешенный вклад (площадь × угол)
                    if (updateIndices.Contains(tri.x)) normals[tri.x] += triangleNormal * angleA;
                    if (updateIndices.Contains(tri.y)) normals[tri.y] += triangleNormal * angleB;
                    if (updateIndices.Contains(tri.z)) normals[tri.z] += triangleNormal * angleC;
                }
            }
        }

    }
}