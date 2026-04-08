using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Proxy.Mesh.Normals
{
    [System.Serializable]
    public class NormalsRecalculationLaplacianSmooth : InternalFeature
    {
        [TriInspector.Slider(0, 180)] public float smoothAngle;

        private NativeArray<int> neighbourCounts => proxy.vertexNeighbours.neighbourCounts;
        private NativeArray<int> neighbourIndices => proxy.vertexNeighbours.neighbourIndices;
        private NativeArray<int> neighbourStartOffsets => proxy.vertexNeighbours.neighbourStartOffsets;

        private NativeArray<float3> _inputNormals;
        public NativeArray<float3> inputNormals
        {
            get
            {
                if (_inputNormals.IsCreated == false)
                    _inputNormals = new NativeArray<float3>(proxy.animatedNormals.Length, Allocator.Persistent);
                return _inputNormals;
            }
        }

        public override void OnShutdown(ProxyMeshAbstract proxyMesh)
        {
            base.OnShutdown(proxyMesh);
            if (_inputNormals.IsCreated) _inputNormals.Dispose();
        }
        public override JobHandle StartJob(JobHandle dependsOn)
        {
            dependsOn = new ReadWrite()
            {
                input = proxy.animatedNormals,
                output = inputNormals,
            }.Schedule(proxy.vertexCount, Mathf.Max(1, proxy.vertexCount / 100), dependsOn);
            dependsOn = new LaplacianSmoothNormalsJob()
            {
                inputNormals = inputNormals,
                outputNormals = proxy.animatedNormals,
                neighbourCounts = neighbourCounts,
                neighbourIndices = neighbourIndices,
                neighbourStartOffsets = neighbourStartOffsets,
                vertexCount = proxy.vertexCount,
                smoothAngle = smoothAngle,
                updateIndices = proxy.normalsRecalculation.updateIndices.AsReadOnly(),
            }.Schedule(dependsOn);

            return dependsOn;
        }
    }

    [BurstCompile]
    public struct LaplacianSmoothNormalsJob : IJob
    {
        [ReadOnly] public NativeArray<float3> inputNormals;
        [NativeDisableParallelForRestriction] public NativeArray<float3> outputNormals;

        [ReadOnly] public NativeArray<int> neighbourCounts;
        [ReadOnly] public NativeArray<int> neighbourIndices;
        [ReadOnly] public NativeArray<int> neighbourStartOffsets;
        [ReadOnly] public int vertexCount;
        [ReadOnly] public float smoothAngle;
        [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;

        public void Execute()
        {
            foreach (int i in updateIndices)
            {
                float3 current = inputNormals[i];
                int neighbourCount = neighbourCounts[i];

                if (neighbourCount == 0)
                {
                    outputNormals[i] = current;
                    continue;
                }

                float3 sum = current;
                int selectedCount = 1;
                int start = neighbourStartOffsets[i];

                for (int j = 0; j < neighbourCount; ++j)
                {
                    int nb = neighbourIndices[start + j];
                    float3 nbNormal = inputNormals[nb];
                    float angle = Angle(math.normalizesafe(current), math.normalizesafe(nbNormal));
                    if (angle > smoothAngle)
                    {
                        sum += nbNormal;
                        selectedCount++;
                    }
                }

                if (selectedCount > 0)
                {
                    float3 avg = sum / selectedCount;
                    float3 newNormal = avg;
                    outputNormals[i] = newNormal;
                }
                else
                {
                    outputNormals[i] = current;
                }
            }
        }

        public float Angle(float3 a, float3 b)
        {
            return Quaternion.Angle(Quaternion.identity, Quaternion.FromToRotation(a, b));
        }
    }
}
