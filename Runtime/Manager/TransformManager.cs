namespace Proxy.Mesh
{
    using System.Collections.Generic;
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using UnityEngine;
    using UnityEngine.Jobs;

    public class TransformManager : IManager
    {
        private Dictionary<int, ITransformRW> transforms = new();
        private Dictionary<Transform, ITransformRW> transformToRW = new();

        protected TransformAccessArray transformAccessArray;
        protected NativeList<Matrix4x4> localToWorldMatrix;
        protected NativeList<Matrix4x4> worldToLocalMatrix;

        public void OnInit()
        {
            transformAccessArray = new TransformAccessArray(32);
            localToWorldMatrix = new NativeList<Matrix4x4>(32, Allocator.Persistent);
            worldToLocalMatrix = new NativeList<Matrix4x4>(32, Allocator.Persistent);
        }
        public void OnShutdown()
        {
            if (transformAccessArray.isCreated)
                transformAccessArray.Dispose();
            if (localToWorldMatrix.IsCreated)
                localToWorldMatrix.Dispose();
            if (worldToLocalMatrix.IsCreated)
                worldToLocalMatrix.Dispose();

            transforms.Clear();
            transformToRW.Clear();
            transforms = null;
            transformToRW = null;
        }
        public void Registration(ITransformRW transform)
        {
            int id = transform.GetInstanceID();
            if (transforms.TryAdd(id, transform))
            {
                Transform t = transform.transform;

                transformAccessArray.Add(t);

                transform.worldToLocalMatrix = t.worldToLocalMatrix;
                transform.localToWorldMatrix = t.localToWorldMatrix;

                localToWorldMatrix.Add(transform.localToWorldMatrix);
                worldToLocalMatrix.Add(transform.worldToLocalMatrix);

                transformToRW.Add(t, transform);
            }
        }
        public void Remove(ITransformRW transform)
        {
            if (transforms == null)
                return;

            int id = transform.GetInstanceID();
            if (transforms.Remove(id))
            {
                Transform t = transform.transform;
                if (t == null)
                    Debug.LogError($"Transform null");

                int index = -1;
                for (int i = 0; i < transformAccessArray.length; i++)
                {
                    if (transformAccessArray[i] == t)
                    {
                        index = i;
                        break;
                    }
                }

                if (index >= 0)
                {
                    transformAccessArray.RemoveAtSwapBack(index);
                    localToWorldMatrix.RemoveAtSwapBack(index);
                    worldToLocalMatrix.RemoveAtSwapBack(index);
                }

                transformToRW.Remove(t);
            }
        }
        public void Update()
        {
            if (transforms == null || transformAccessArray.length == 0)
                return;

            if (ProxyManager.jobCompleted)
            {
                for (int i = 0; i < transformAccessArray.length; i++)
                {
                    Transform t = transformAccessArray[i];
                    if (transformToRW.TryGetValue(t, out ITransformRW rw))
                    {
                        rw.localToWorldMatrix = localToWorldMatrix[i];
                        rw.worldToLocalMatrix = worldToLocalMatrix[i];
                    }
                }
            }
        }
        public void FixedUpdate() { }
        public void LateUpdate() { }
        public JobHandle StartJob(JobHandle dependsOn)
        {
            if (transforms == null)
                return dependsOn;
            return new ReadJob()
            {
                localToWorldMatrix = localToWorldMatrix,
                worldToLocalMatrix = worldToLocalMatrix,
            }.Schedule(transformAccessArray, dependsOn);
        }

        [BurstCompile]
        protected struct ReadJob : IJobParallelForTransform
        {
            [NativeDisableParallelForRestriction, WriteOnly] public NativeList<Matrix4x4> localToWorldMatrix;
            [NativeDisableParallelForRestriction, WriteOnly] public NativeList<Matrix4x4> worldToLocalMatrix;
            public void Execute(int index, TransformAccess transform)
            {
                localToWorldMatrix[index] = transform.localToWorldMatrix;
                worldToLocalMatrix[index] = transform.worldToLocalMatrix;
            }
        }
    }

    public interface ITransformRW
    {
        Transform transform { get; }
        int GetInstanceID();
        Matrix4x4 localToWorldMatrix { get; set; }
        Matrix4x4 worldToLocalMatrix { get; set; }
    }
}