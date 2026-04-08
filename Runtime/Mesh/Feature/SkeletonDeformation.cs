using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Proxy.Mesh.SkeletonDeformation;

namespace Proxy.Mesh
{
    [System.Serializable]
    public class SkeletonDeformation : IProxyAbstractChild, IProxyJob,IDisposable
    {
        public SkeletonDeformationMethod Method;
        public bool IsInit => proxy != null;
        private ProxyMeshAbstract proxy;
        public NativeArray<int4> boneIndices;
        public NativeArray<float4> boneWeights;
        public void OnInit(ProxyMeshAbstract proxyMesh)
        {
            proxy = proxyMesh;
            if(Method != SkeletonDeformationMethod.Simple)
            {
                ConvertToFixedBoneCount(proxy.vertexCount, proxy.bonesPerVertex, proxy.startIndices, proxy.allBoneWeights, out boneIndices, out boneWeights);
            }
        }

        public static void ConvertToFixedBoneCount(
        int vertexCount,
        NativeArray<int> bonesPerVertex,
        NativeArray<int> startIndices,
        NativeArray<BoneWeight1> allBoneWeights,
        out NativeArray<int4> boneIndices,
        out NativeArray<float4> boneWeights,
        Allocator allocator = Allocator.Persistent)
        {
            boneIndices = new NativeArray<int4>(vertexCount, allocator);
            boneWeights = new NativeArray<float4>(vertexCount, allocator);

            for (int i = 0; i < vertexCount; i++)
            {
                int start = startIndices[i];
                int count = bonesPerVertex[i];
                int4 indices = default;
                float4 weights = default;

                int maxBones = math.min(count, 4);
                for (int j = 0; j < maxBones; j++)
                {
                    BoneWeight1 bw = allBoneWeights[start + j];
                    indices[j] = bw.boneIndex;
                    weights[j] = bw.weight;
                }

                boneIndices[i] = indices;
                boneWeights[i] = weights;
            }
        }

        public static void ConvertToFixedBoneCountNormalized(
            int vertexCount,
            NativeArray<int> bonesPerVertex,
            NativeArray<int> startIndices,
            NativeArray<BoneWeight1> allBoneWeights,
            out NativeArray<int4> boneIndices,
            out NativeArray<float4> boneWeights,
            Allocator allocator = Allocator.Persistent)
        {
            boneIndices = new NativeArray<int4>(vertexCount, allocator);
            boneWeights = new NativeArray<float4>(vertexCount, allocator);

            for (int i = 0; i < vertexCount; i++)
            {
                int start = startIndices[i];
                int count = bonesPerVertex[i];
                int4 indices = default;
                float4 weights = default;

                int maxBones = math.min(count, 4);
                for (int j = 0; j < maxBones; j++)
                {
                    BoneWeight1 bw = allBoneWeights[start + j];
                    indices[j] = bw.boneIndex;
                    weights[j] = bw.weight;
                }

                // Normalize so that sum = 1
                float sum = weights.x + weights.y + weights.z + weights.w;
                if (sum > 0f)
                    weights /= sum;

                boneIndices[i] = indices;
                boneWeights[i] = weights;
            }
        }

        public void OnJobComplete()
        {

        }
        public void Dispose() => OnShutdown(proxy);
        public void OnShutdown(ProxyMeshAbstract proxyMesh)
        {
            if(boneIndices.IsCreated) boneIndices.Dispose();
            if(boneWeights.IsCreated) boneWeights.Dispose();
        }

        public JobHandle StartJob(JobHandle dependsOn)
        {
            if (IsInit)
            {
                if(proxy.haveTangents)
                {
                    dependsOn = SkeletonDeformationWithTangents.instance.StartJob(proxy, this, dependsOn);
                }
                else
                {
                    dependsOn = SkeletonDeformationWithoutTangents.instance.StartJob(proxy, this, dependsOn);
                }
            }
            return dependsOn;
        }

        [BurstCompile]
        protected struct SkeletonDeformationJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> baseVertices;
            [ReadOnly] public NativeArray<float3> baseNormals;
            [ReadOnly] public NativeArray<float4> baseTangents;
            [ReadOnly] public NativeArray<BoneWeight1> allBoneWeights;
            [ReadOnly] public NativeArray<int> bonesPerVertex;
            [ReadOnly] public NativeArray<int> startIndices;
            [ReadOnly] public NativeArray<float4x4> boneMatrices;

            [WriteOnly] public NativeArray<float3> animatedVertices;
            [WriteOnly] public NativeArray<float3> animatedNormals;
            [WriteOnly] public NativeArray<float4> animetedTangents;

            public void Execute(int vertexIndex)
            {
                if (vertexIndex < 0 || vertexIndex >= baseVertices.Length)
                    return;

                float3 vertex = baseVertices[vertexIndex];
                float3 normal = baseNormals[vertexIndex];
                float3 tangent = baseTangents[vertexIndex].xyz;
                float3 animatedVertex = float3.zero;
                float3 animatedNormal = float3.zero;
                float3 animatedTangent = float3.zero;
                float totalWeight = 0f;

                int startIndex = startIndices[vertexIndex];
                int boneCount = bonesPerVertex[vertexIndex];

                if (boneCount > 0 && startIndex >= 0 && startIndex < allBoneWeights.Length)
                {
                    for (int i = 0; i < boneCount; i++)
                    {
                        int weightIndex = startIndex + i;

                        if (weightIndex < 0 || weightIndex >= allBoneWeights.Length)
                            continue;

                        BoneWeight1 bw = allBoneWeights[weightIndex];

                        if (bw.boneIndex < 0 || bw.boneIndex >= boneMatrices.Length)
                            continue;

                        if (bw.weight > 0)
                        {
                            animatedVertex += math.transform(boneMatrices[bw.boneIndex], vertex) * bw.weight;
                            animatedNormal += math.rotate(boneMatrices[bw.boneIndex], normal) * bw.weight;
                            animatedTangent += math.rotate(boneMatrices[bw.boneIndex], tangent) * bw.weight;
                            totalWeight += bw.weight;
                        }
                    }
                }


                animatedVertex /= totalWeight;
                animatedNormal /= totalWeight;
                animatedTangent /= totalWeight;
                
                animatedVertices[vertexIndex] = animatedVertex;
                animatedNormals[vertexIndex] = math.normalize(animatedNormal);
                animetedTangents[vertexIndex] = new float4(math.normalize(animatedTangent), baseTangents[vertexIndex].w);
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast)]
        protected struct SkeletonDeformationSIMD : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> baseVertices;
            [ReadOnly] public NativeArray<float3> baseNormals;
            [ReadOnly] public NativeArray<float4> baseTangents;
            [ReadOnly] public NativeArray<int4> boneIndices;
            [ReadOnly] public NativeArray<float4> boneWeights;
            [ReadOnly] public NativeArray<float4x4> boneMatrices;

            [WriteOnly] public NativeArray<float3> animatedVertices;
            [WriteOnly] public NativeArray<float3> animatedNormals;
            [WriteOnly] public NativeArray<float4> animetedTangents;
            public void Execute(int vertexIndex)
            {
                float3 vertex = baseVertices[vertexIndex];
                float3 normal = baseNormals[vertexIndex];
                float3 tangent = baseTangents[vertexIndex].xyz;
                float3 animatedVertex = float3.zero;
                float3 animatedNormal = float3.zero;
                float3 animatedTangent = float3.zero;
                float totalWeight = 0f;

                int4 indices = boneIndices[vertexIndex];
                float4 weights = boneWeights[vertexIndex];

                float w = weights[0];
                int boneIdx = indices[0];
                animatedVertex += math.transform(boneMatrices[boneIdx], vertex) * w;
                animatedNormal += math.rotate(boneMatrices[boneIdx], normal) * w;
                animatedTangent += math.rotate(boneMatrices[boneIdx], tangent) * w;
                totalWeight += w;

                w = weights[1];
                boneIdx = indices[1];
                animatedVertex += math.transform(boneMatrices[boneIdx], vertex) * w;
                animatedNormal += math.rotate(boneMatrices[boneIdx], normal) * w;
                animatedTangent += math.rotate(boneMatrices[boneIdx], tangent) * w;
                totalWeight += w;

                w = weights[2];
                boneIdx = indices[2];
                animatedVertex += math.transform(boneMatrices[boneIdx], vertex) * w;
                animatedNormal += math.rotate(boneMatrices[boneIdx], normal) * w;
                animatedTangent += math.rotate(boneMatrices[boneIdx], tangent) * w;
                totalWeight += w;

                w = weights[3];
                boneIdx = indices[3];
                animatedVertex += math.transform(boneMatrices[boneIdx], vertex) * w;
                animatedNormal += math.rotate(boneMatrices[boneIdx], normal) * w;
                animatedTangent += math.rotate(boneMatrices[boneIdx], tangent) * w;
                totalWeight += w;

                animatedVertices[vertexIndex] = animatedVertex / totalWeight;
                animatedNormals[vertexIndex] = math.normalize(animatedNormal / totalWeight);
                animetedTangents[vertexIndex] = new float4(math.normalize(animatedTangent / totalWeight), baseTangents[vertexIndex].w);
            }
        }

        [BurstCompile]
        protected struct ReadWriteJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> baseVertices;
            [ReadOnly] public NativeArray<float3> baseNormals;
            [ReadOnly] public NativeArray<float4> baseTangents;
            [WriteOnly] public NativeArray<float3> animatedVertices;
            [WriteOnly] public NativeArray<float3> animatedNormals;
            [WriteOnly] public NativeArray<float4> animatedTangents;
            public void Execute(int index)
            {
                animatedVertices[index] = baseVertices[index];
                animatedNormals[index] = baseNormals[index];
                animatedTangents[index] = baseTangents[index];
            }
        }

        public enum SkeletonDeformationMethod
        {
            Simple, SIMD
        }
    }

    internal class SkeletonDeformationWithTangents
    {
        private static SkeletonDeformationWithTangents m_instance;
        public static SkeletonDeformationWithTangents instance
        {
            get
            {
                if(m_instance == null)
                    m_instance = new SkeletonDeformationWithTangents();
                return m_instance;
            }
        }

        public JobHandle StartJob(ProxyMeshAbstract proxy, SkeletonDeformation skeletonDeformation, JobHandle dependsOn)
        {
            if (skeletonDeformation.IsInit)
            {
                if (proxy.skeleton.rootBone != null)
                    return CalculateSkeletonDeformation(proxy, skeletonDeformation, dependsOn);
                else
                {
                    return new ReadWriteJob()
                    {
                        animatedNormals = proxy.animatedNormals,
                        animatedVertices = proxy.animatedVertices,
                        animatedTangents = proxy.animatedTangents,
                        baseNormals = proxy.nativeBaseNormals,
                        baseVertices = proxy.nativeBaseVertices,
                        baseTangents = proxy.nativeBaseTangents,
                    }.Schedule(proxy.vertexCount, Mathf.Max(1, proxy.vertexCount / 100), dependsOn);
                }
            }
            return dependsOn;
        }

        public JobHandle CalculateSkeletonDeformation(ProxyMeshAbstract proxy, SkeletonDeformation skeletonDeformation, JobHandle dependsOn)
        {
            switch (skeletonDeformation.Method)
            {
                default:
                case SkeletonDeformationMethod.Simple:
                    return new SkeletonDeformationJob
                    {
                        baseVertices = proxy.nativeBaseVertices,
                        baseNormals = proxy.nativeBaseNormals,
                        baseTangents = proxy.nativeBaseTangents,
                        allBoneWeights = proxy.allBoneWeights,
                        bonesPerVertex = proxy.bonesPerVertex,
                        startIndices = proxy.startIndices,
                        boneMatrices = proxy.boneMatrices,
                        animatedVertices = proxy.animatedVertices,
                        animatedNormals = proxy.animatedNormals,
                        animetedTangents = proxy.animatedTangents
                    }.Schedule(proxy.vertexCount, Mathf.Max(1, proxy.vertexCount / 100), dependsOn);
                case SkeletonDeformationMethod.SIMD:
                    return new SkeletonDeformationSIMD
                    {
                        baseVertices = proxy.nativeBaseVertices,
                        baseNormals = proxy.nativeBaseNormals,
                        baseTangents = proxy.nativeBaseTangents,
                        boneIndices = skeletonDeformation.boneIndices,
                        boneWeights = skeletonDeformation.boneWeights,
                        boneMatrices = proxy.boneMatrices,
                        animatedVertices = proxy.animatedVertices,
                        animatedNormals = proxy.animatedNormals,
                        animetedTangents = proxy.animatedTangents
                    }.Schedule(proxy.vertexCount, Mathf.Max(1, proxy.vertexCount / 100), dependsOn);
            }
        }

        [BurstCompile]
        protected struct SkeletonDeformationJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> baseVertices;
            [ReadOnly] public NativeArray<float3> baseNormals;
            [ReadOnly] public NativeArray<float4> baseTangents;
            [ReadOnly] public NativeArray<BoneWeight1> allBoneWeights;
            [ReadOnly] public NativeArray<int> bonesPerVertex;
            [ReadOnly] public NativeArray<int> startIndices;
            [ReadOnly] public NativeArray<float4x4> boneMatrices;

            [WriteOnly] public NativeArray<float3> animatedVertices;
            [WriteOnly] public NativeArray<float3> animatedNormals;
            [WriteOnly] public NativeArray<float4> animetedTangents;

            public void Execute(int vertexIndex)
            {
                if (vertexIndex < 0 || vertexIndex >= baseVertices.Length)
                    return;

                float3 vertex = baseVertices[vertexIndex];
                float3 normal = baseNormals[vertexIndex];
                float3 tangent = baseTangents[vertexIndex].xyz;
                float3 animatedVertex = float3.zero;
                float3 animatedNormal = float3.zero;
                float3 animatedTangent = float3.zero;
                float totalWeight = 0f;

                int startIndex = startIndices[vertexIndex];
                int boneCount = bonesPerVertex[vertexIndex];

                if (boneCount > 0 && startIndex >= 0 && startIndex < allBoneWeights.Length)
                {
                    for (int i = 0; i < boneCount; i++)
                    {
                        int weightIndex = startIndex + i;

                        if (weightIndex < 0 || weightIndex >= allBoneWeights.Length)
                            continue;

                        BoneWeight1 bw = allBoneWeights[weightIndex];

                        if (bw.boneIndex < 0 || bw.boneIndex >= boneMatrices.Length)
                            continue;

                        if (bw.weight > 0)
                        {
                            animatedVertex += math.transform(boneMatrices[bw.boneIndex], vertex) * bw.weight;
                            animatedNormal += math.rotate(boneMatrices[bw.boneIndex], normal) * bw.weight;
                            animatedTangent += math.rotate(boneMatrices[bw.boneIndex], tangent) * bw.weight;
                            totalWeight += bw.weight;
                        }
                    }
                }


                animatedVertex /= totalWeight;
                animatedNormal /= totalWeight;
                animatedTangent /= totalWeight;

                animatedVertices[vertexIndex] = animatedVertex;
                animatedNormals[vertexIndex] = math.normalize(animatedNormal);
                animetedTangents[vertexIndex] = new float4(math.normalize(animatedTangent), baseTangents[vertexIndex].w);
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast)]
        protected struct SkeletonDeformationSIMD : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> baseVertices;
            [ReadOnly] public NativeArray<float3> baseNormals;
            [ReadOnly] public NativeArray<float4> baseTangents;
            [ReadOnly] public NativeArray<int4> boneIndices;
            [ReadOnly] public NativeArray<float4> boneWeights;
            [ReadOnly] public NativeArray<float4x4> boneMatrices;

            [WriteOnly] public NativeArray<float3> animatedVertices;
            [WriteOnly] public NativeArray<float3> animatedNormals;
            [WriteOnly] public NativeArray<float4> animetedTangents;
            public void Execute(int vertexIndex)
            {
                float3 vertex = baseVertices[vertexIndex];
                float3 normal = baseNormals[vertexIndex];
                float3 tangent = baseTangents[vertexIndex].xyz;
                float3 animatedVertex = float3.zero;
                float3 animatedNormal = float3.zero;
                float3 animatedTangent = float3.zero;
                float totalWeight = 0f;

                int4 indices = boneIndices[vertexIndex];
                float4 weights = boneWeights[vertexIndex];

                float w = weights[0];
                int boneIdx = indices[0];
                animatedVertex += math.transform(boneMatrices[boneIdx], vertex) * w;
                animatedNormal += math.rotate(boneMatrices[boneIdx], normal) * w;
                animatedTangent += math.rotate(boneMatrices[boneIdx], tangent) * w;
                totalWeight += w;

                w = weights[1];
                boneIdx = indices[1];
                animatedVertex += math.transform(boneMatrices[boneIdx], vertex) * w;
                animatedNormal += math.rotate(boneMatrices[boneIdx], normal) * w;
                animatedTangent += math.rotate(boneMatrices[boneIdx], tangent) * w;
                totalWeight += w;

                w = weights[2];
                boneIdx = indices[2];
                animatedVertex += math.transform(boneMatrices[boneIdx], vertex) * w;
                animatedNormal += math.rotate(boneMatrices[boneIdx], normal) * w;
                animatedTangent += math.rotate(boneMatrices[boneIdx], tangent) * w;
                totalWeight += w;

                w = weights[3];
                boneIdx = indices[3];
                animatedVertex += math.transform(boneMatrices[boneIdx], vertex) * w;
                animatedNormal += math.rotate(boneMatrices[boneIdx], normal) * w;
                animatedTangent += math.rotate(boneMatrices[boneIdx], tangent) * w;
                totalWeight += w;

                animatedVertices[vertexIndex] = animatedVertex / totalWeight;
                animatedNormals[vertexIndex] = math.normalize(animatedNormal / totalWeight);
                animetedTangents[vertexIndex] = new float4(math.normalize(animatedTangent / totalWeight), baseTangents[vertexIndex].w);
            }
        }

        [BurstCompile]
        protected struct ReadWriteJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> baseVertices;
            [ReadOnly] public NativeArray<float3> baseNormals;
            [ReadOnly] public NativeArray<float4> baseTangents;
            [WriteOnly] public NativeArray<float3> animatedVertices;
            [WriteOnly] public NativeArray<float3> animatedNormals;
            [WriteOnly] public NativeArray<float4> animatedTangents;
            public void Execute(int index)
            {
                animatedVertices[index] = baseVertices[index];
                animatedNormals[index] = baseNormals[index];
                animatedTangents[index] = baseTangents[index];
            }
        }
    }
    internal class SkeletonDeformationWithoutTangents 
    {
        private static SkeletonDeformationWithoutTangents m_instance;
        public static SkeletonDeformationWithoutTangents instance
        {
            get
            {
                if (m_instance == null)
                    m_instance = new SkeletonDeformationWithoutTangents();
                return m_instance;
            }
        }

        public JobHandle StartJob(ProxyMeshAbstract proxy, SkeletonDeformation skeletonDeformation, JobHandle dependsOn)
        {
            if (skeletonDeformation.IsInit)
            {
                if (proxy.skeleton.rootBone != null)
                    return CalculateSkeletonDeformation(proxy, skeletonDeformation, dependsOn);
                else
                {
                    return new ReadWriteJob()
                    {
                        animatedNormals = proxy.animatedNormals,
                        animatedVertices = proxy.animatedVertices,
                        baseNormals = proxy.nativeBaseNormals,
                        baseVertices = proxy.nativeBaseVertices,
                    }.Schedule(proxy.vertexCount, Mathf.Max(1, proxy.vertexCount / 100), dependsOn);
                }
            }
            return dependsOn;
        }

        public JobHandle CalculateSkeletonDeformation(ProxyMeshAbstract proxy, SkeletonDeformation skeletonDeformation, JobHandle dependsOn)
        {
            switch (skeletonDeformation.Method)
            {
                default:
                case SkeletonDeformationMethod.Simple:
                    return new SkeletonDeformationJob
                    {
                        baseVertices = proxy.nativeBaseVertices,
                        baseNormals = proxy.nativeBaseNormals,
                        allBoneWeights = proxy.allBoneWeights,
                        bonesPerVertex = proxy.bonesPerVertex,
                        startIndices = proxy.startIndices,
                        boneMatrices = proxy.boneMatrices,
                        animatedVertices = proxy.animatedVertices,
                        animatedNormals = proxy.animatedNormals,
                    }.Schedule(proxy.vertexCount, Mathf.Max(1, proxy.vertexCount / 100), dependsOn);
                case SkeletonDeformationMethod.SIMD:
                    return new SkeletonDeformationSIMD
                    {
                        baseVertices = proxy.nativeBaseVertices,
                        baseNormals = proxy.nativeBaseNormals,
                        boneIndices = skeletonDeformation.boneIndices,
                        boneWeights = skeletonDeformation.boneWeights,
                        boneMatrices = proxy.boneMatrices,
                        animatedVertices = proxy.animatedVertices,
                        animatedNormals = proxy.animatedNormals,
                    }.Schedule(proxy.vertexCount, Mathf.Max(1, proxy.vertexCount / 100), dependsOn);
            }
        }

        [BurstCompile]
        protected struct SkeletonDeformationJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> baseVertices;
            [ReadOnly] public NativeArray<float3> baseNormals;
            [ReadOnly] public NativeArray<BoneWeight1> allBoneWeights;
            [ReadOnly] public NativeArray<int> bonesPerVertex;
            [ReadOnly] public NativeArray<int> startIndices;
            [ReadOnly] public NativeArray<float4x4> boneMatrices;

            [WriteOnly] public NativeArray<float3> animatedVertices;
            [WriteOnly] public NativeArray<float3> animatedNormals;

            public void Execute(int vertexIndex)
            {
                if (vertexIndex < 0 || vertexIndex >= baseVertices.Length)
                    return;

                float3 vertex = baseVertices[vertexIndex];
                float3 normal = baseNormals[vertexIndex];
                float3 animatedVertex = float3.zero;
                float3 animatedNormal = float3.zero;
                float totalWeight = 0f;

                int startIndex = startIndices[vertexIndex];
                int boneCount = bonesPerVertex[vertexIndex];

                if (boneCount > 0 && startIndex >= 0 && startIndex < allBoneWeights.Length)
                {
                    for (int i = 0; i < boneCount; i++)
                    {
                        int weightIndex = startIndex + i;

                        if (weightIndex < 0 || weightIndex >= allBoneWeights.Length)
                            continue;

                        BoneWeight1 bw = allBoneWeights[weightIndex];

                        if (bw.boneIndex < 0 || bw.boneIndex >= boneMatrices.Length)
                            continue;

                        if (bw.weight > 0)
                        {
                            animatedVertex += math.transform(boneMatrices[bw.boneIndex], vertex) * bw.weight;
                            animatedNormal += math.rotate(boneMatrices[bw.boneIndex], normal) * bw.weight;
                            totalWeight += bw.weight;
                        }
                    }
                }


                animatedVertex /= totalWeight;
                animatedNormal /= totalWeight;

                animatedVertices[vertexIndex] = animatedVertex;
                animatedNormals[vertexIndex] = math.normalize(animatedNormal);
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast)]
        protected struct SkeletonDeformationSIMD : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> baseVertices;
            [ReadOnly] public NativeArray<float3> baseNormals;
            [ReadOnly] public NativeArray<int4> boneIndices;
            [ReadOnly] public NativeArray<float4> boneWeights;
            [ReadOnly] public NativeArray<float4x4> boneMatrices;

            [WriteOnly] public NativeArray<float3> animatedVertices;
            [WriteOnly] public NativeArray<float3> animatedNormals;
            public void Execute(int vertexIndex)
            {
                float3 vertex = baseVertices[vertexIndex];
                float3 normal = baseNormals[vertexIndex];
                float3 animatedVertex = float3.zero;
                float3 animatedNormal = float3.zero;
                float totalWeight = 0f;

                int4 indices = boneIndices[vertexIndex];
                float4 weights = boneWeights[vertexIndex];

                float w = weights[0];
                int boneIdx = indices[0];
                animatedVertex += math.transform(boneMatrices[boneIdx], vertex) * w;
                animatedNormal += math.rotate(boneMatrices[boneIdx], normal) * w;
                totalWeight += w;

                w = weights[1];
                boneIdx = indices[1];
                animatedVertex += math.transform(boneMatrices[boneIdx], vertex) * w;
                animatedNormal += math.rotate(boneMatrices[boneIdx], normal) * w;
                totalWeight += w;

                w = weights[2];
                boneIdx = indices[2];
                animatedVertex += math.transform(boneMatrices[boneIdx], vertex) * w;
                animatedNormal += math.rotate(boneMatrices[boneIdx], normal) * w;
                totalWeight += w;

                w = weights[3];
                boneIdx = indices[3];
                animatedVertex += math.transform(boneMatrices[boneIdx], vertex) * w;
                animatedNormal += math.rotate(boneMatrices[boneIdx], normal) * w;
                totalWeight += w;

                animatedVertices[vertexIndex] = animatedVertex / totalWeight;
                animatedNormals[vertexIndex] = math.normalize(animatedNormal / totalWeight);
            }
        }

        [BurstCompile]
        protected struct ReadWriteJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> baseVertices;
            [ReadOnly] public NativeArray<float3> baseNormals;
            [WriteOnly] public NativeArray<float3> animatedVertices;
            [WriteOnly] public NativeArray<float3> animatedNormals;
            public void Execute(int index)
            {
                animatedVertices[index] = baseVertices[index];
                animatedNormals[index] = baseNormals[index];
            }
        }
    }
}