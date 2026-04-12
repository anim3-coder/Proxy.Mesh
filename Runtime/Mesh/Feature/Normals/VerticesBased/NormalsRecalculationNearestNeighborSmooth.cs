using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Proxy.Mesh.Normals
{
    [System.Serializable]
    public class NormalsRecalculationNearestNeighborSmooth : InternalFeature
    {
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

        [TriInspector.Slider(1,5)] public int accuracy = 1;
        public override void OnShutdown(ProxyMeshAbstract proxyMesh)
        {
            base.OnShutdown(proxyMesh);
            if(_inputNormals.IsCreated) _inputNormals.Dispose();
        }

        public override JobHandle StartJob(JobHandle dependsOn)
        {
            for (int i = 0; i < accuracy; i++)
            {
                dependsOn = new ReadWrite()
                {
                    input = proxy.animatedNormals,
                    output = inputNormals,
                }.Schedule(proxy.vertexCount, Mathf.Max(1, proxy.vertexCount / 100), dependsOn);
                dependsOn = new SmoothNormalsJob()
                {
                    inputNormals = inputNormals,
                    outputNormals = proxy.animatedNormals,
                    neighbourCounts = neighbourCounts,
                    neighbourIndices = neighbourIndices,
                    neighbourStartOffsets = neighbourStartOffsets,
                    vertexCount = proxy.vertexCount,
                    accuracy = accuracy,
                    updateIndices = proxy.normalsRecalculation.updateIndicesList,
                }.Schedule(proxy.normalsRecalculation.updateIndicesList, 32, dependsOn);
            }
            return dependsOn;
        }

        [BurstCompile]
        public struct SmoothNormalsJob : IJobParallelForDefer
        {
            [ReadOnly] public NativeArray<float3> inputNormals;
            [NativeDisableParallelForRestriction] public NativeArray<float3> outputNormals;
            [ReadOnly] public int accuracy;
            [ReadOnly] public NativeArray<int> neighbourCounts;
            [ReadOnly] public NativeArray<int> neighbourIndices;
            [ReadOnly] public NativeArray<int> neighbourStartOffsets;
            [ReadOnly] public int vertexCount;
            [ReadOnly] public NativeList<int> updateIndices;

            public void Execute(int i)
            {
                i = updateIndices[i];
                float3 current = inputNormals[i];
                int neighbourCount = neighbourCounts[i];

                if (neighbourCount == 0)
                {
                    outputNormals[i] = current;
                    return;
                }

                float3 sum = current;
                int start = neighbourStartOffsets[i];

                for (int j = 0; j < neighbourCount; ++j)
                {
                    int nb = neighbourIndices[start + j];
                    float3 nbNormal = inputNormals[nb];
                    float cosAngle = math.dot(current, nbNormal);
                    sum = NormalSlerp(math.normalizesafe(nbNormal), math.normalizesafe(sum), 0.5f);
                }

                outputNormals[i] = sum;

            }

            public static float3 NormalSlerp(float3 a, float3 b, float t)
            {
                quaternion q = Quaternion.FromToRotation(a, b);
                quaternion result = math.slerp(quaternion.identity, q, t);
                return math.mul(result, a);
            }
        }
    }
}
