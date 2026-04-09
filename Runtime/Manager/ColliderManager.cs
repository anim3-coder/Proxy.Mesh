using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Burst;
using Unity.Mathematics;

namespace Proxy.Mesh
{
    using Unity.Burst;
    using Unity.Collections;
    using Unity.Jobs;
    using Unity.Mathematics;
    using UnityEngine;
    using UnityEngine.Jobs; // для TransformAccessArray

    public class ColliderManager : IManager
    {
        private Dictionary<int, ProxyCollider> colliders = new();
        private HashSet<int> dirty = new();

        private NativeParallelHashMap<int, int> idToIndex;
        private NativeList<int> ids;
        private NativeList<RawDeformInfo> rawData;
        private TransformAccessArray transformAccessArray;
        public NativeList<DeformInfo> colliderData;

        private int stateVersion;
        private Dictionary<ProxyCollider[], CachedIndicesEntry> indicesCache = new();

        private struct CachedIndicesEntry
        {
            public NativeArray<int> indices;
            public int versionAtCreation;
        }

        public void OnInit()
        {
            idToIndex = new NativeParallelHashMap<int, int>(64, Allocator.Persistent);
            ids = new NativeList<int>(64, Allocator.Persistent);
            rawData = new NativeList<RawDeformInfo>(64, Allocator.Persistent);
            transformAccessArray = new TransformAccessArray(64);
            colliderData = new NativeList<DeformInfo>(64, Allocator.Persistent);
            stateVersion = 0;
            indicesCache.Clear();
        }


        public void OnShutdown()
        {
            idToIndex.Dispose();
            ids.Dispose();
            rawData.Dispose();
            transformAccessArray.Dispose();
            colliderData.Dispose();

            foreach (var entry in indicesCache.Values)
                if (entry.indices.IsCreated)
                    entry.indices.Dispose();
            indicesCache.Clear();

            colliders.Clear();
        }

        public void Registration(ProxyCollider collider)
        {
            int id = collider.GetInstanceID();
            if (colliders.TryAdd(id, collider))
            {
                int index = rawData.Length;
                idToIndex.Add(id, index);
                ids.Add(id);
                rawData.Add(collider.GetRawDeformInfo());
                transformAccessArray.Add(collider.transform); // ← добавляем Transform
                colliderData.Add(default);
            }
            else
            {
                Debug.LogWarning($"{collider.name} уже зарегистрирован");
            }

            stateVersion++;
        }

        public void Remove(ProxyCollider collider)
        {
            int id = collider.GetInstanceID();
            if (!colliders.ContainsKey(id)) return;

            int index = idToIndex[id];
            int lastIndex = rawData.Length - 1;

            if (index != lastIndex)
            {
                int lastId = ids[lastIndex];

                rawData[index] = rawData[lastIndex];
                colliderData[index] = colliderData[lastIndex];
                ids[index] = lastId;

                idToIndex[lastId] = index;

                Transform lastTransform = transformAccessArray[lastIndex];
                transformAccessArray[index] = lastTransform;
            }

            rawData.RemoveAt(lastIndex);
            colliderData.RemoveAt(lastIndex);
            ids.RemoveAt(lastIndex);
            transformAccessArray.RemoveAtSwapBack(index);

            idToIndex.Remove(id);
            colliders.Remove(id);
            dirty.Remove(id);

            stateVersion++;
        }

        public JobHandle StartJob(JobHandle dependsOn)
        {
            return new UpdateDataJob
            {
                rawData = rawData.AsArray(),
                colliderData = colliderData.AsArray().Reinterpret<DeformInfo>()
            }.Schedule(transformAccessArray, dependsOn);
        }

        public void MarkedDirty(ProxyCollider collider)
        {
            int id = collider.GetInstanceID();
            if (colliders.ContainsKey(id))
                dirty.Add(id);
        }

        public void Update()
        {
            if (ProxyManager.jobCompleted)
            {
                foreach (int id in dirty)
                {
                    if (idToIndex.TryGetValue(id, out int index))
                    {
                        rawData[index] = colliders[id].GetRawDeformInfo();
                    }
                }
                dirty.Clear();
            }
        }

        public NativeArray<int> GetIndicesCached(ProxyCollider[] colliders)
        {
            // Проверяем кэш
            if (indicesCache.TryGetValue(colliders, out var entry))
            {
                if (entry.versionAtCreation == stateVersion)
                    return entry.indices; // Валидный кэш
                else
                    entry.indices.Dispose(); // Освобождаем устаревший массив
            }

            // Создаём новый массив
            var newIndices = new NativeArray<int>(colliders.Length, Allocator.Persistent);
            for (int i = 0; i < colliders.Length; i++)
            {
                int id = colliders[i].GetInstanceID();
                if (idToIndex.TryGetValue(id, out int index))
                    newIndices[i] = index;
                else
                    newIndices[i] = -1;
            }

            // Сохраняем в кэш
            indicesCache[colliders] = new CachedIndicesEntry
            {
                indices = newIndices,
                versionAtCreation = stateVersion
            };

            return newIndices;
        }

        public void FixedUpdate()
        {
        }

        public void LateUpdate()
        {
        }

        [BurstCompile]
        protected struct UpdateDataJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<RawDeformInfo> rawData;
            [NativeDisableParallelForRestriction] public NativeArray<DeformInfo> colliderData;

            public void Execute(int index, TransformAccess transform)
            {
                var raw = rawData[index];
                // Доступ к матрице и масштабу через TransformAccess (быстро и Burst-совместимо)
                float3 scale = transform.localScale;
                float4x4 localToWorld = transform.localToWorldMatrix;

                colliderData[index] = new DeformInfo
                {
                    radius = raw.radius * scale.x, // предполагаем равномерный масштаб по осям
                    point1 = math.transform(localToWorld, raw.point1),
                    point2 = math.transform(localToWorld, raw.point2),
                    IsUpdate = colliderData[index].IsUpdate,
                    IsValid = colliderData[index].IsValid,
                };
            }
        }
    }
}