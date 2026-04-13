using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Jobs;

namespace Proxy.Mesh
{
    public class ProxyVertexShake : ExternalFeature
    {
        [SerializeField] private Transform[] shakeBones;
        [SerializeField] private float MaxVelocity;
        [SerializeField] private float IntegralLimit;
        [SerializeField] private float P;
        [SerializeField] private float I;
        [SerializeField] private float D;
        private NativeArray<BoneSegment> boneSegments;
        private NativeArray<float> vertexDistancesToBones;
        private NativeArray<int> indices;
        private NativeArray<float3> vertices;
        private NativeArray<float3> velocity;
        private NativeArray<float3> error;
        private NativeArray<float3> integralError;

        private Transform[] bones => proxy.skeleton.bones;

        public override void OnInit(ProxyMesh proxyMesh)
        {
            base.OnInit(proxyMesh);
            boneSegments = new NativeArray<BoneSegment>(bones.Length, Allocator.Persistent);
            List<int> list = new List<int>();
            foreach (var bone in shakeBones)
                list.AddRange(proxy.GetVerticesForBone(bone));
            indices = new NativeArray<int>(list.ToArray(), Allocator.Persistent);
            vertices = new NativeArray<float3>(list.Count, Allocator.Persistent);
            
            for(int i = 0; i < list.Count; i++)
            {
                vertices[i] = proxy.nativeBaseVertices[list[i]];
            }
            
            vertexDistancesToBones = new NativeArray<float>(list.Count, Allocator.Persistent);
            velocity = new NativeArray<float3>(list.Count, Allocator.Persistent);
            error = new NativeArray<float3>(list.Count, Allocator.Persistent);
            integralError = new NativeArray<float3>(list.Count, Allocator.Persistent);
            CalculateVertexDistances();
        }

        public override JobHandle StartJob(JobHandle dependsOn)
        {
            return new ShakeJob
            { 
                indices = indices,
                lastVertices = vertices,
                vertices = proxy.animatedVertices,
                vertexToDistance = vertexDistancesToBones,
                P = P,
                I = I,
                D = D,
                deltaTime = Time.deltaTime,
                MaxVelocity = MaxVelocity,
                IntegralLimit = IntegralLimit,
                velocity = velocity,
                error = error,
                integralError = integralError,
            }.Schedule(vertices.Length, 128, dependsOn);
        }

        public struct BoneSegment
        {
            public int boneIndex;
            public int parentBoneIndex;
            public Vector3 localDirection;
            public float length;
        }

        private Vector3 GetBoneBindPosition(int boneIndex)
        {
            if (boneIndex < 0 || boneIndex >= proxy.bindPoses.Length)
                return Vector3.zero;

            Matrix4x4 bindPose = proxy.bindPoses[boneIndex];
            if (bindPose.determinant != 0)
            {
                Matrix4x4 inverseBindPose = bindPose.inverse;
                return inverseBindPose.GetColumn(3);
            }

            return Vector3.zero;
        }

        private void CalculateVertexDistances()
        {
            for (int x = 0; x < vertices.Length; x++)
            {
                int vertexIndex = indices[x];
                float maxDistance = 0f;

                int startIndex = proxy.startIndices[vertexIndex];
                int boneCount = proxy.bonesPerVertex[vertexIndex];

                if (boneCount > 0 && startIndex >= 0 && startIndex < proxy.allBoneWeights.Length)
                {
                    for (int i = 0; i < boneCount; i++)
                    {
                        int weightIndex = startIndex + i;

                        if (weightIndex < 0 || weightIndex >= proxy.allBoneWeights.Length)
                            continue;

                        BoneWeight1 bw = proxy.allBoneWeights[weightIndex];

                        if (bw.boneIndex < 0 || bw.boneIndex >= bones.Length)
                            continue;

                        if (bw.weight > 0.01f)
                        {
                            Vector3 vertexPos = proxy.nativeBaseVertices[vertexIndex];
                            float distance = CalculateDistanceToBoneSegment(vertexPos, bw.boneIndex);
                            maxDistance = Mathf.Max(maxDistance, distance);
                        }
                    }
                }

                vertexDistancesToBones[x] = maxDistance;
            }
        }

        private float CalculateDistanceToBoneSegment(Vector3 vertexPos, int boneIndex)
        {
            return Vector3.Distance(vertexPos, GetBoneBindPosition(boneIndex));  
        }

        public override void OnShutdown(ProxyMesh proxyMesh)
        {
            base.OnShutdown(proxyMesh);
            boneSegments.Dispose();
            indices.Dispose();
            vertices.Dispose();
            velocity.Dispose();
            error.Dispose();
            integralError.Dispose();
        }
        [BurstCompile]
        public struct ShakeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> indices;
            [ReadOnly] public NativeArray<float> vertexToDistance;
            [ReadOnly] public float deltaTime;
            [ReadOnly] public float P;
            [ReadOnly] public float I;
            [ReadOnly] public float D;
            [ReadOnly] public float MaxVelocity;
            [ReadOnly] public float IntegralLimit;
            [NativeDisableParallelForRestriction] public NativeArray<float3> lastVertices;
            [NativeDisableParallelForRestriction] public NativeArray<float3> vertices;
            [NativeDisableParallelForRestriction] public NativeArray<float3> velocity;
            [NativeDisableParallelForRestriction] public NativeArray<float3> error;
            [NativeDisableParallelForRestriction] public NativeArray<float3> integralError;
            public void Execute(int index)
            {
                velocity[index] = Update(velocity[index] , (vertices[indices[index]] - lastVertices[index]), vertexToDistance[index], index);
                lastVertices[index] += velocity[index];
                vertices[indices[index]] = lastVertices[index];
            }


            public float3 Update(float3 currentVelocity, float3 targetVelocity, float deltaTime, int index)
            {
                if (deltaTime <= 0f)
                    return float3.zero;

                float3 error = targetVelocity - currentVelocity;

                // Пропорциональная часть (покомпонентное умножение)
                float3 p = P * error;

                // Интегральная часть с ограничением
                integralError[index] += error * deltaTime;
                if (IntegralLimit > 0f)
                {
                    integralError[index] = math.clamp(integralError[index], -IntegralLimit, IntegralLimit);
                }
                float3 i = I * integralError[index];

                // Дифференциальная часть
                float3 derivative = (error - this.error[index]) / deltaTime;
                float3 d = D * derivative;

                this.error[index] = error;

                float3 force = p + i + d;

                // Ограничение силы по магнитуде
                float mag = math.length(force);
                if (mag > MaxVelocity && MaxVelocity > 0f)
                    force = force / mag * MaxVelocity;

                return force;
            }
        }
    }
}