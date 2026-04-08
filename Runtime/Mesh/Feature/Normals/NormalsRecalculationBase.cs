using JetBrains.Annotations;
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

        public bool HaveDeformationMaps => deformationMaps.Length > 0;
        public bool HaveDeformations => deformaionsComponent.Length > 0;
        public IDeformationMaps[] deformationMaps { get; private set; }
        public IDeformaion[] deformaionsComponent { get; private set; }
        public NativeArray<float4> additiveDeformation { get; protected set; }
        public NativeArray<float4> addedDeformation { get; protected set; }
        public NativeArray<float3> previousNormals { get; protected set; }

        public override void OnInit(ProxyMeshAbstract proxyMesh)
        {
            base.OnInit(proxyMesh);

            deformationMaps = proxy.Get<IDeformationMaps>();
            deformaionsComponent = proxy.Get<IDeformaion>();
            updateIndices = new NativeParallelHashSet<int>(vertexCount, Allocator.Persistent);
            additiveDeformation = new NativeArray<float4>(vertexCount, Allocator.Persistent);
            addedDeformation = new NativeArray<float4>(vertexCount, Allocator.Persistent);
            previousNormals = new NativeArray<float3>(vertexCount, Allocator.Persistent);
        }

        public override void OnShutdown(ProxyMeshAbstract proxyMesh)
        {
            if (updateIndices.IsCreated) updateIndices.Dispose();
            if (additiveDeformation.IsCreated) additiveDeformation.Dispose();
            if (addedDeformation.IsCreated) addedDeformation.Dispose();
            if (previousNormals.IsCreated) previousNormals.Dispose();
        }

        public override JobHandle StartJob(JobHandle dependsOn)
        {
            if (IsInit == false)
                return dependsOn;

            if (HaveDeformationMaps)
            {
                for (int i = 0; i < deformationMaps.Length; i++)
                {
                    for (int x = 0; x < deformationMaps[i].deformationMaps.Length; x++)
                    {
                        if (deformationMaps[i].deformationMaps[x].IsInit)
                        {
                            dependsOn = new UpdateDeformationMapJob()
                            {
                                deformation = additiveDeformation,
                                imagedeformation = deformationMaps[i].deformationMaps[x].deformation,
                                textureSize = deformationMaps[i].deformationMaps[x].textureSize,
                                triangles = deformationMaps[i].deformationMaps[x].triangles,
                                uvs = proxy.nativeUV,
                                updateIndices = updateIndices.AsParallelWriter()
                            }.Schedule(proxy.triangles.Length, 32,dependsOn);
                        }
                    }
                }
            }

            if(HaveDeformations)
            {
                for(int i = 0; i < deformaionsComponent.Length;i++)
                {
                    dependsOn = new UpdateDeformationJob()
                    {
                        input = deformaionsComponent[i].addedDeformation,
                        output = addedDeformation,
                    }.Schedule(vertexCount, 32, dependsOn);
                }
            }

            dependsOn = NormalsJob(dependsOn);

            if (IsDrawNormals)
            {
                dependsOn = new DrawNormals()
                {
                    localToWorldMatrix = proxy.transform.localToWorldMatrix,
                    normals = proxy.animatedNormals,
                    vertices = proxy.animatedVertices,
                    additionalDeformation = additiveDeformation,
                    updateIndices = updateIndices.AsReadOnly()
                }.Schedule(dependsOn);
            }

            dependsOn = new Clear()
            {
                updateIndices = updateIndices,
            }.Schedule(dependsOn);

            return base.StartJob(dependsOn);
        }

        public virtual JobHandle NormalsJob(JobHandle dependsOn)
        {
            return dependsOn;
        }

        [BurstCompile]
        public struct UpdateDeformationJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4> input;
            [NativeDisableParallelForRestriction] public NativeArray<float4> output;

            public void Execute(int index)
            {
                output[index] += new float4(input[index].xyz, math.max(input[index].w, output[index].w));
            }
        }

        [BurstCompile]
        public struct UpdateDeformationMapJob : IJobParallelFor
        {
            [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float4> deformation;
            [ReadOnly] public NativeArray<int3> triangles;
            [ReadOnly] public NativeArray<float4> imagedeformation;
            [ReadOnly] public NativeArray<float2> uvs;
            [ReadOnly] public int textureSize;
            [WriteOnly] public NativeParallelHashSet<int>.ParallelWriter updateIndices;
            public void Execute(int i)
            {
                Update(triangles[i].x);
                Update(triangles[i].y);
                Update(triangles[i].z);
            }

            void Update(int index)
            {
                float2 uv = uvs[index];
                int px = (int)(uv.x * textureSize);
                int py = (int)(uv.y * textureSize);
                px = math.clamp(px, 0, textureSize - 1);
                py = math.clamp(py, 0, textureSize - 1);
                int idx = py * textureSize + px;

                deformation[index] = imagedeformation[idx];
                if (math.length(imagedeformation[idx]) > 0)
                    updateIndices.Add(index);
            }
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
            [ReadOnly] public NativeArray<float4> additionalDeformation; 
            public void Execute()
            {
                foreach (var i in updateIndices)
                {
                    D.raw(new Shape.Line(math.transform(localToWorldMatrix, vertices[i]) + additionalDeformation[i].xyz, math.transform(localToWorldMatrix, vertices[i]) + (0.01f * math.rotate(localToWorldMatrix, math.normalize(normals[i]))) + additionalDeformation[i].xyz), color: Color.red);
                }
            }
        }
    }
}