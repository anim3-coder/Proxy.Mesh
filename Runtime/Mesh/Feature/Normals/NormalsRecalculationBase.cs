using Proxy.Mesh;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Vertx.Debugging;

namespace Proxy.Mesh
{
    public abstract class NormalsRecalculationBase : InternalFeature
    {
        [field: SerializeField] public bool IsDrawNormals { get; private set; }
        public NativeParallelHashSet<int> updateIndices { get; private set; }
        protected int vertexCount => proxy.vertexCount;

        public override bool IsInit
        {
            get
            {
                if (proxy == null)
                    return false;
                return proxy.NormalRecalculationEnabled;
            }
        }

        public override void OnInit(ProxyMeshAbstract proxyMesh)
        {
            base.OnInit(proxyMesh);
            if (IsInit)
            {
                int count = vertexCount > 10_000 ? vertexCount / 5 : vertexCount;
                updateIndices = new NativeParallelHashSet<int>(count, Allocator.Persistent);
            }
        }

        public override void OnShutdown(ProxyMeshAbstract proxyMesh)
        {
            if (updateIndices.IsCreated) updateIndices.Dispose();
        }

        public override JobHandle StartJob(JobHandle dependsOn)
        {
            if (IsDrawNormals)
            {
                dependsOn = new DrawNormals()
                {
                    localToWorldMatrix = proxy.transform.localToWorldMatrix,
                    normals = proxy.animatedNormals,
                    vertices = proxy.animatedVertices,
                    updateIndices = updateIndices.AsReadOnly()
                }.Schedule(dependsOn);
            }

            dependsOn = new Clear()
            {
                updateIndices = updateIndices,
            }.Schedule(dependsOn);

            return base.StartJob(dependsOn);
        }

        [BurstCompile]
        public struct Clear : IJob
        {
            public NativeParallelHashSet<int> updateIndices;
            public void Execute()
            {
                updateIndices.Clear();
            }
        }

        [BurstCompile]
        public struct DrawNormals : IJob
        {
            [ReadOnly] public NativeArray<float3> vertices;
            [ReadOnly] public NativeArray<float3> normals;
            [ReadOnly] public float4x4 localToWorldMatrix;
            [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;
            public void Execute()
            {
                foreach (var i in updateIndices)
                {
                    D.raw(new Shape.Line(math.transform(localToWorldMatrix, vertices[i]), math.transform(localToWorldMatrix, vertices[i]) + (0.01f * math.rotate(localToWorldMatrix, math.normalize(normals[i])))), color: Color.red);
                }
            }
        }
    }
}