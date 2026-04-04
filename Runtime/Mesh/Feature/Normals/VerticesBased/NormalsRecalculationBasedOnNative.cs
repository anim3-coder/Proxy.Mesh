using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Proxy.Mesh.Normals
{
    [System.Serializable]
    public class NormalsRecalculationBasedOnNative : InternalFeature
    {
        [TriInspector.Slider(0,1)]public float influence;
        private NativeArray<float3> lastNormals;
        public override void OnInit(ProxyMeshAbstract proxyMesh)
        {
            base.OnInit(proxyMesh);
            lastNormals = new NativeArray<float3>(proxy.vertexCount,Allocator.Persistent);
        }
        public override void OnShutdown(ProxyMeshAbstract proxyMesh)
        {
            base.OnShutdown(proxyMesh);
            if (lastNormals.IsCreated) lastNormals.Dispose();
        }
        public override JobHandle StartJob(JobHandle dependsOn)
        {
            int t = proxy.triangles.Length / 3;
            
            dependsOn = Clear();
            dependsOn = RecalculateNormals();

            dependsOn = new AfterUpdateNormals()
            {
                updateIndices = proxy.normalsRecalculation.updateIndices.AsReadOnly(),
                normals = proxy.animatedNormals,
                lastNormals = lastNormals,
                influence = influence
            }.Schedule(dependsOn);

            return dependsOn;

            JobHandle Clear()
            {
                return new ClearNormalsJob()
                {
                    normals = proxy.animatedNormals,
                    updateIndices = proxy.normalsRecalculation.updateIndices.AsReadOnly(),
                    lastNormal = lastNormals,
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

        [BurstCompile]
        public struct ClearNormalsJob : IJob
        {
            public NativeArray<float3> normals;
            [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;
            [WriteOnly] public NativeArray<float3> lastNormal;
            public void Execute()
            {
                foreach (var i in updateIndices)
                {
                    lastNormal[i] = normals[i];
                    normals[i] = float3.zero;
                }
            }
        }
        [BurstCompile]
        public struct AfterUpdateNormals : IJob
        {
            public NativeArray<float3> normals;
            [ReadOnly] public NativeArray<float3> lastNormals;
            [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;
            [ReadOnly] public float influence;
            public void Execute()
            {
                foreach (var i in updateIndices)
                {
                    normals[i] = NormalSlerp(math.normalizesafe(lastNormals[i]), math.normalizesafe(normals[i]), influence);
                }
            }

            public static float3 NormalSlerp(float3 a, float3 b, float t)
            {
                quaternion q = Quaternion.FromToRotation(a, b);
                quaternion result = math.slerp(quaternion.identity, q, t);
                return math.mul(result, a);
            }
        }
        [BurstCompile]
        public struct AfterUpdateNormalsWithDeformVector : IJob
        {
            public NativeArray<float3> normals;
            [ReadOnly] public NativeArray<float3> lastNormals;
            [ReadOnly] public NativeArray<float3> deformVector;
            [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;
            [ReadOnly] public float influence;
            public void Execute()
            {
                foreach (var i in updateIndices)
                {
                    normals[i] = NormalSlerp(math.normalizesafe(lastNormals[i]), math.normalizesafe(normals[i]), influence);
                }
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