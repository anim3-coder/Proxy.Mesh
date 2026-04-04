using System.Collections;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Proxy.Mesh
{
    public abstract class ProxyCollider : MonoBehaviour, IProxyChild, IProxyJob
    {
        public abstract Vector3 MoveDirection { get; protected set; }
        public abstract bool IsInit { get; protected set; }
        public abstract void OnInit(ProxyMesh proxyMesh);
        public abstract void OnJobComplete();
        public abstract void OnShutdown(ProxyMesh proxyMesh);
        public abstract JobHandle StartJob(JobHandle dependsOn);
    }
    public interface IJobCollider : IJob
    {
        bool Intersect(Vector3 vector);
        /// <summary>
        /// Вычисляет вектор на который нужно переместиться чтобы не пересекаться с точкой
        /// </summary>
        /// <param name="vector"></param>
        /// <returns></returns>
        Vector3 Counteraction(Vector3 vector);
    }
    public static class ProxyColliderExecute
    {
        public static void Execute(this IJobCollider job,
                                   NativeArray<Vector3> vertices,
                                   NativeHashSet<int> IDs,
                                   NativeHashSet<int> result)
        {
            result.Clear();
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!IDs.Contains(i))
                    continue;
                if (job.Intersect(vertices[i]))
                {
                    result.Add(i);
                }
            }
        }
        public static void Execute(this IJobCollider job,
                                   NativeArray<Vector3> vertices,
                                   NativeHashSet<int> IDs,
                                   NativeArray<int> result,
                                   ref int length)
        {
            length = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!IDs.Contains(i))
                    continue;
                if (job.Intersect(vertices[i]))
                {
                    result[length] = i;
                    length++;
                }
            }
        }
        public static void Execute(this IJobCollider job,
                                   NativeArray<Vector3> vertices,
                                   NativeHashSet<int> IDs,
                                   NativeHashSet<int> result,
                                   NativeArray<Vector3> moveDirection)
        {
            result.Clear();
            Vector3 vector = Vector3.zero;
            int count = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!IDs.Contains(i))
                    continue;
                if (job.Intersect(vertices[i]))
                {
                    vector += job.Counteraction(vertices[i]);
                    count++;
                    result.Add(i);
                }
            }
            if (count > 0)
                vector = vector / count;
            moveDirection[0] = vector;
        }
    }
}