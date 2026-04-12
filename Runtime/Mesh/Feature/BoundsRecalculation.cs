using System;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Vertx.Debugging;

namespace Proxy.Mesh
{
    [System.Serializable]
    public class BoundsRecalculation : IProxyAbstractChild, IProxyJob, IDisposable
    {
        public BoundsRecalculationMethod Method = BoundsRecalculationMethod.Simple;
        public bool IsDrawBounds;
        public bool IsInit => proxy != null;
        private ProxyMeshAbstract proxy;
        public void OnInit(ProxyMeshAbstract proxyMesh)
        {
            proxy = proxyMesh;
        }
        public bool IsRunning
        {
            get
            {
                return Application.isPlaying;
            }
        }
        private int vertexCount => proxy.vertexCount;
        public void OnJobComplete()
        {
        }
        public void Dispose() => OnShutdown(proxy);
        public void OnShutdown(ProxyMeshAbstract proxyMesh)
        {
        }

        public JobHandle StartJob(JobHandle dependsOn)
        {
            if(IsInit == false || Method == BoundsRecalculationMethod.None)
                return dependsOn;

            if (proxy.skeletonGroups.IsInit)
            {
                dependsOn = new CalculateBoundsBySkeletonBounds()
                {
                    bounds = proxy.skeletonGroups.bounds,
                    boundsMax = proxy.boundsMax,
                    boundsMin = proxy.boundsMin,
                }.Schedule(dependsOn);
            }
            else
            {
                dependsOn = CalculateBounds(proxy.animatedVertices, proxy.boundsMin, proxy.boundsMax, dependsOn);
            }

            if (IsDrawBounds)
            {
                dependsOn = new DrawBounds()
                {
                    boundsMin = proxy.boundsMin,
                    boundsMax = proxy.boundsMax,
                    color = Color.aliceBlue,
                    localToWorldMatrix = proxy.localToWorldMatrix,
                }.Schedule(dependsOn);
            }   
            return dependsOn;
        }

        public JobHandle CalculateBounds(NativeArray<float3> vertices, NativeArray<float> boundsMin, NativeArray<float> boundsMax, JobHandle dependsOn)
        {
            switch (Method)
            {
                default:
                case BoundsRecalculationMethod.Simple:
                    return new CalculateBoundsJob()
                    {
                        vertices = vertices,
                        boundsMin = boundsMin,
                        boundsMax = boundsMax,
                    }.Schedule(dependsOn);
                case BoundsRecalculationMethod.Batch:
                    return new CalculateBatchBoundsJob()
                    {
                        vertices = vertices,
                        boundsMin = boundsMin,
                        boundsMax = boundsMax,
                    }.Schedule(vertexCount, Mathf.Max(1, vertexCount / 100), dependsOn);
                case BoundsRecalculationMethod.Multithreading:
                    return new CalculateBoundsJobMultithreading()
                    {
                        vertices = vertices,
                        boundsMin = boundsMin,
                        boundsMax = boundsMax,
                    }.Schedule(vertexCount, Mathf.Max(1, vertexCount / 100), dependsOn);
            }
        }

        public JobHandle CalculateBounds(NativeArray<float3> vertices, NativeArray<int> indices,NativeArray<float> boundsMin, NativeArray<float> boundsMax, JobHandle dependsOn)
        {
            switch (Method)
            {
                default:
                case BoundsRecalculationMethod.Simple:
                    return new WCalculateBoundsJob()
                    {
                        vertices = vertices,
                        boundsMin = boundsMin,
                        boundsMax = boundsMax,
                        indices = indices,
                    }.Schedule(dependsOn);
                case BoundsRecalculationMethod.Batch:
                    return new WCalculateBatchBoundsJob()
                    {
                        vertices = vertices,
                        boundsMin = boundsMin,
                        boundsMax = boundsMax,
                        indices = indices,
                    }.Schedule(indices.Length, Mathf.Max(1, indices.Length / 100), dependsOn);
                case BoundsRecalculationMethod.Multithreading:
                    return new WCalculateBoundsJobMultithreading()
                    {
                        vertices = vertices,
                        boundsMin = boundsMin,
                        boundsMax = boundsMax,
                        indices = indices,
                    }.Schedule(indices.Length, Mathf.Max(1, indices.Length / 100), dependsOn);
            }
        }

        [BurstCompile]
        protected struct CalculateBoundsBySkeletonBounds : IJob
        {
            [ReadOnly] public NativeArray<Bounds> bounds;
            public NativeArray<float> boundsMin;
            public NativeArray<float> boundsMax;

            private void AtomicMin(int component, float value)
            {
                unsafe
                {
                    float* address = (float*)boundsMin.GetUnsafePtr() + component;
                    float current;
                    float newValue;
                    do
                    {
                        current = *address;
                        newValue = math.min(current, value);
                        if (newValue >= current) // значение не меньше текущего – выходим
                            break;
                    } while (Interlocked.CompareExchange(ref *address, newValue, current) != current);
                }
            }

            private void AtomicMax(int component, float value)
            {
                unsafe
                {
                    float* address = (float*)boundsMax.GetUnsafePtr() + component;
                    float current;
                    float newValue;
                    do
                    {
                        current = *address;
                        newValue = math.max(current, value);
                        if (newValue <= current) // значение не больше текущего – выходим
                            break;
                    } while (Interlocked.CompareExchange(ref *address, newValue, current) != current);
                }
            }

            public void Execute()
            {
                Vector3 min = bounds[0].min;
                Vector3 max = bounds[0].max;

                for(int i = 1; i < bounds.Length;i++)
                {
                    min = Vector3.Min(bounds[i].min, min);
                    max = Vector3.Max(bounds[i].max, max);
                }

                boundsMin[0] = min.x;
                boundsMin[1] = min.y;
                boundsMin[2] = min.z;

                boundsMax[0] = max.x;
                boundsMax[1] = max.y;
                boundsMax[2] = max.z;
            }
        }

        [BurstCompile]
        protected struct ClearJob : IJob
        {
            [WriteOnly] public NativeArray<float> boundsMin;
            [WriteOnly] public NativeArray<float> boundsMax;
            public void Execute()
            {
                boundsMin[0] = float.MaxValue;
                boundsMin[1] = float.MaxValue;
                boundsMin[2] = float.MaxValue;

                boundsMax[0] = float.MinValue;
                boundsMax[1] = float.MinValue;
                boundsMax[2] = float.MinValue;
            }
        }

        [BurstCompile]
        public struct DrawBounds : IJob
        {
            [ReadOnly] public Matrix4x4 localToWorldMatrix;
            [ReadOnly] public NativeArray<float> boundsMin;
            [ReadOnly] public NativeArray<float> boundsMax;
            [ReadOnly] public Color color;
            public void Execute()
            {
                Vector3 min = localToWorldMatrix.MultiplyPoint3x4(new Vector3(boundsMin[0], boundsMin[1], boundsMin[2]));
                Vector3 max = localToWorldMatrix.MultiplyPoint3x4(new Vector3(boundsMax[0], boundsMax[1], boundsMax[2]));

                Bounds bounds = new Bounds();
                bounds.SetMinMax(min, max);
                D.raw(new Shape.Box(bounds), color);
            }
        }

        #region Without Indices
        [BurstCompile]
        protected struct CalculateBoundsJob : IJob
        {
            [ReadOnly] public NativeArray<float3> vertices;

            public NativeArray<float> boundsMin;
            public NativeArray<float> boundsMax;

            public void Execute()
            {
                if (vertices.Length == 0)
                    return;

                float3 min = vertices[0];
                float3 max = vertices[0];

                for (int i = 1; i < vertices.Length; i++)
                {
                    min = math.min(min, vertices[i]);
                    max = math.max(max, vertices[i]);
                }

                boundsMin[0] = min.x;
                boundsMin[1] = min.y;
                boundsMin[2] = min.z;
                boundsMax[0] = max.x;
                boundsMax[1] = max.y;
                boundsMax[2] = max.z;
            }

        }

        [BurstCompile]
        protected struct CalculateBatchBoundsJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<float3> vertices;

            [NativeDisableParallelForRestriction]
            public NativeArray<float> boundsMin;

            [NativeDisableParallelForRestriction]
            public NativeArray<float> boundsMax;

            public void Execute(int startIndex, int count)
            {
                if (count == 0) return;

                float3 localMin = vertices[startIndex];
                float3 localMax = vertices[startIndex];

                int endIndex = startIndex + count;
                for (int i = startIndex + 1; i < endIndex; i++)
                {
                    float3 v = vertices[i];
                    localMin = math.min(localMin, v);
                    localMax = math.min(localMax, v);
                }

                AtomicMin(0, localMin.x);
                AtomicMin(1, localMin.y);
                AtomicMin(2, localMin.z);

                AtomicMax(0, localMax.x);
                AtomicMax(1, localMax.y);
                AtomicMax(2, localMax.z);
            }

            private void AtomicMin(int component, float value)
            {
                unsafe
                {
                    float* address = (float*)boundsMin.GetUnsafePtr() + component;
                    float current;
                    float newValue;
                    do
                    {
                        current = *address;
                        newValue = math.min(current, value);
                        if (newValue >= current) // значение не меньше текущего – выходим
                            break;
                    } while (Interlocked.CompareExchange(ref *address, newValue, current) != current);
                }
            }

            private void AtomicMax(int component, float value)
            {
                unsafe
                {
                    float* address = (float*)boundsMax.GetUnsafePtr() + component;
                    float current;
                    float newValue;
                    do
                    {
                        current = *address;
                        newValue = math.max(current, value);
                        if (newValue <= current) // значение не больше текущего – выходим
                            break;
                    } while (Interlocked.CompareExchange(ref *address, newValue, current) != current);
                }
            }
        }

        [BurstCompile]
        protected struct CalculateBoundsJobMultithreading : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> vertices;

            // Массивы должны быть инициализированы:
            // boundsMin = float.MaxValue, boundsMax = float.MinValue
            [NativeDisableParallelForRestriction]
            public NativeArray<float> boundsMin;

            [NativeDisableParallelForRestriction]
            public NativeArray<float> boundsMax;

            public void Execute(int index)
            {
                float3 vertex = vertices[index];

                // Атомарное обновление минимумов по каждой компоненте
                AtomicMin(0, vertex.x);
                AtomicMin(1, vertex.y);
                AtomicMin(2, vertex.z);

                // Атомарное обновление максимумов
                AtomicMax(0, vertex.x);
                AtomicMax(1, vertex.y);
                AtomicMax(2, vertex.z);
            }

            private void AtomicMin(int component, float value)
            {
                unsafe
                {
                    float* address = (float*)boundsMin.GetUnsafePtr() + component;
                    float current;
                    float newValue;
                    do
                    {
                        current = *address;
                        newValue = math.min(current, value);
                        if (newValue >= current) // значение не меньше текущего – выходим
                            break;
                    } while (Interlocked.CompareExchange(ref *address, newValue, current) != current);
                }
            }

            private void AtomicMax(int component, float value)
            {
                unsafe
                {
                    float* address = (float*)boundsMax.GetUnsafePtr() + component;
                    float current;
                    float newValue;
                    do
                    {
                        current = *address;
                        newValue = math.max(current, value);
                        if (newValue <= current) // значение не больше текущего – выходим
                            break;
                    } while (Interlocked.CompareExchange(ref *address, newValue, current) != current);
                }
            }
        }
        #endregion

        #region With Indices
        [BurstCompile]
        protected struct WCalculateBoundsJob : IJob
        {
            [ReadOnly] public NativeArray<float3> vertices;
            [ReadOnly] public NativeArray<int> indices;

            public NativeArray<float> boundsMin;
            public NativeArray<float> boundsMax;

            public void Execute()
            {
                float3 min = vertices[indices[0]];
                float3 max = vertices[indices[0]];

                for (int i = 1; i < indices.Length; i++)
                {
                    min = math.min(min, vertices[indices[i]]);
                    max = math.max(max, vertices[indices[i]]);
                }

                boundsMin[0] = min.x;
                boundsMin[1] = min.y;
                boundsMin[2] = min.z;
                boundsMax[0] = max.x;
                boundsMax[1] = max.y;
                boundsMax[2] = max.z;
            }

        }

        [BurstCompile]
        protected struct WCalculateBatchBoundsJob : IJobParallelForBatch
        {
            [ReadOnly] public NativeArray<float3> vertices;
            [ReadOnly] public NativeArray<int> indices;

            [NativeDisableParallelForRestriction]
            public NativeArray<float> boundsMin;

            [NativeDisableParallelForRestriction]
            public NativeArray<float> boundsMax;

            public void Execute(int startIndex, int count)
            {
                if (count == 0) return;

                float3 localMin = vertices[indices[startIndex]];
                float3 localMax = vertices[indices[startIndex]];

                int endIndex = startIndex + count;
                for (int i = startIndex + 1; i < endIndex; i++)
                {
                    float3 v = vertices[indices[i]];
                    localMin = math.min(localMin, v);
                    localMax = math.max(localMax, v);
                }

                AtomicMin(0, localMin.x);
                AtomicMin(1, localMin.y);
                AtomicMin(2, localMin.z);

                AtomicMax(0, localMax.x);
                AtomicMax(1, localMax.y);
                AtomicMax(2, localMax.z);
            }

            private void AtomicMin(int component, float value)
            {
                unsafe
                {
                    float* address = (float*)boundsMin.GetUnsafePtr() + component;
                    float current;
                    float newValue;
                    do
                    {
                        current = *address;
                        newValue = math.min(current, value);
                        if (newValue >= current) // значение не меньше текущего – выходим
                            break;
                    } while (Interlocked.CompareExchange(ref *address, newValue, current) != current);
                }
            }

            private void AtomicMax(int component, float value)
            {
                unsafe
                {
                    float* address = (float*)boundsMax.GetUnsafePtr() + component;
                    float current;
                    float newValue;
                    do
                    {
                        current = *address;
                        newValue = math.max(current, value);
                        if (newValue <= current) // значение не больше текущего – выходим
                            break;
                    } while (Interlocked.CompareExchange(ref *address, newValue, current) != current);
                }
            }
        }

        [BurstCompile]
        protected struct WCalculateBoundsJobMultithreading : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> vertices;
            [ReadOnly] public NativeArray<int> indices;
            [NativeDisableParallelForRestriction]
            public NativeArray<float> boundsMin;

            [NativeDisableParallelForRestriction]
            public NativeArray<float> boundsMax;

            public void Execute(int index)
            {
                index = indices[index];
                float3 vertex = vertices[index];

                AtomicMin(0, vertices[index].x);
                AtomicMin(1, vertices[index].y);
                AtomicMin(2, vertices[index].z);

                AtomicMax(0, vertices[index].x);
                AtomicMax(1, vertices[index].y);
                AtomicMax(2, vertices[index].z);
            }

            private void AtomicMin(int component, float value)
            {
                unsafe
                {
                    float* address = (float*)boundsMin.GetUnsafePtr() + component;
                    float current;
                    float newValue;
                    do
                    {
                        current = *address;
                        newValue = math.min(current, value);
                        if (newValue >= current) // значение не меньше текущего – выходим
                            break;
                    } while (Interlocked.CompareExchange(ref *address, newValue, current) != current);
                }
            }

            private void AtomicMax(int component, float value)
            {
                unsafe
                {
                    float* address = (float*)boundsMax.GetUnsafePtr() + component;
                    float current;
                    float newValue;
                    do
                    {
                        current = *address;
                        newValue = math.max(current, value);
                        if (newValue <= current) // значение не больше текущего – выходим
                            break;
                    } while (Interlocked.CompareExchange(ref *address, newValue, current) != current);
                }
            }
        }
        #endregion

        public enum BoundsRecalculationMethod
        {
            None, Simple, Batch, Multithreading
        }
    }
}