using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Jobs;
using Unity.Mathematics;
using System.Linq;
using Unity.Burst;
using Unity.Jobs;
using System;

namespace Proxy.Mesh
{
    [System.Serializable]
    public class ProxySkeleton : IDisposable
    {
        public Transform rootBone;
        public Transform[] bones;
        /// <summary>
        /// Игнорированные кости
        /// </summary>
        public Transform[] ignoreBones;
        public Rigidbody rigidbody;
        public Vector3 linearVelocity => rigidbody.linearVelocity;
        public Vector3 angularVelocity => rigidbody.angularVelocity;
        public NativeArray<int> ignoreIndicies;

        public TransformAccessArray bonesTransformAccessArray;
        public NativeArray<float4x4> boneMatrices;
        /// <summary>
        /// Нормальная позиция в локальном пространстве
        /// </summary>
        public NativeArray<float3> localPositions;
        /// <summary>
        /// Нормальная ротация в локальном пространстве
        /// </summary>
        public NativeArray<quaternion> localRotations;
        public NativeArray<float3> localVelocities;
        public NativeArray<float3> previousLocalPositions;
        public NativeArray<float3> localAccelerations;
        [TriInspector.ShowInInspector, TriInspector.ReadOnly] private IProxyBoneJob[] proxyBoneJobs;
        public void InitSkeleton(UnityEngine.Mesh originalMesh)
        {
            if (rootBone == null)
                return;
            if (bones.Length > 0)
            {
                bonesTransformAccessArray = new TransformAccessArray(bones);
                boneMatrices = new NativeArray<float4x4>(bones.Length, Allocator.Persistent);
            }
            else
            {
                bonesTransformAccessArray = new TransformAccessArray(0);
                boneMatrices = new NativeArray<float4x4>(1, Allocator.Persistent);
            }

            localVelocities = new NativeArray<float3>(bones.Length, Allocator.Persistent);
            previousLocalPositions = new NativeArray<float3>(bones.Length, Allocator.Persistent);
            localAccelerations = new NativeArray<float3>(bones.Length, Allocator.Persistent);

            localPositions = new NativeArray<float3>(bones.Length, Allocator.Persistent);
            localRotations = new NativeArray<quaternion>(bones.Length, Allocator.Persistent);

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                {
                    previousLocalPositions[i] = bones[i].localPosition;
                    localPositions[i] = bones[i].localPosition;
                    localRotations[i] = bones[i].localRotation;
                }
            }

            ignoreIndicies = new NativeArray<int>(ignoreIndicies.Length, Allocator.Persistent);

            for(int i = 0; i < ignoreIndicies.Length;i++)
            {
                ignoreIndicies[i] = GetBoneIndex(ignoreBones[i]);
            }

            var list = rootBone.GetComponents<IProxyBoneJob>().ToList();
            if (rootBone.parent == null)
                list.AddRange(rootBone.GetComponentsInChildren<IProxyBoneJob>(true));
            else
                list.AddRange(rootBone.parent.GetComponentsInChildren<IProxyBoneJob>(true));
            proxyBoneJobs = list.ToArray();
        }
        public void Dispose() => ShutdownSkeleton();
        public void ShutdownSkeleton()
        {
            if (bonesTransformAccessArray.isCreated) bonesTransformAccessArray.Dispose();
            if (boneMatrices.IsCreated) boneMatrices.Dispose();
            if (localVelocities.IsCreated) localVelocities.Dispose();
            if (previousLocalPositions.IsCreated) previousLocalPositions.Dispose();
            if (localAccelerations.IsCreated) localAccelerations.Dispose();
            if (localPositions.IsCreated) localPositions.Dispose();
            if (localRotations.IsCreated) localRotations.Dispose();
            if (ignoreIndicies.IsCreated) ignoreIndicies.Dispose();
        }
        public virtual void Update() { }
        private JobHandle jobHandle;
        public virtual void FixedUpdate()
        {
            if (rootBone == null)
                return;
            jobHandle = new UpdateBoneDynamicsJob()
            {
                localVelocities = localVelocities,
                localAccelerations = localAccelerations,
                previousLocalPositions = previousLocalPositions,
                ignoreIndicies = ignoreIndicies,
                fixedDeltaTime = Time.fixedDeltaTime
            }.Schedule(bonesTransformAccessArray, jobHandle);

            for(int i = 0; i <  proxyBoneJobs.Length; i++)
            {
                jobHandle = proxyBoneJobs[i].StartBoneJob(jobHandle);
            }

            jobHandle.Complete();
        }
        public int GetBoneIndex(Transform transform)
        {
            for(int i = 0; i < bones.Length; i++) 
                if(bones[i] == transform) return i;
            return -1;
        }
        ~ProxySkeleton()
        {
            ShutdownSkeleton();
        }
        public ProxySkeleton(Transform root, Transform[] bones)
        {
            this.rootBone = root;
            this.bones = bones;
        }

        [BurstCompile]
        public struct UpdateBoneDynamicsJob : IJobParallelForTransform
        {
            public NativeArray<float3> localVelocities;
            public NativeArray<float3> localAccelerations;
            public NativeArray<float3> previousLocalPositions;
            [ReadOnly] public NativeArray<int> ignoreIndicies;
            public float fixedDeltaTime;
            public void Execute(int i, TransformAccess transform)
            {
                foreach (var w in ignoreIndicies)
                    if (w == i)
                        return;


                float3 currentPosition = transform.localPosition;
                float3 newVelocity = (currentPosition - previousLocalPositions[i]) / fixedDeltaTime;

                // Рассчитываем ускорение для более выраженного эффекта
                float3 newAcceleration = (newVelocity - localVelocities[i]) / fixedDeltaTime;

                // Усиливаем ускорение для более заметной тряски
                localAccelerations[i] = newAcceleration;
                //localVelocities[i] = newVelocity;
                previousLocalPositions[i] = currentPosition;
            }
        }
    }
}