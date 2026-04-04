using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using System;

namespace Proxy.Mesh
{
    public class ProxySpringBones : MonoBehaviour, IProxyBoneJob, IProxyChild, IDisposable
    {
        private ProxyMesh proxy;
        private ProxySkeleton skeleton => proxy.skeleton;
        [SerializeField] private Transform[] bones;
        [SerializeField] private float mass;
        [SerializeField] private float damping;
        [SerializeField] private float maxAngle;
        public bool IsInit => proxy != null;
        private NativeArray<int> indices;
        public void OnInit(ProxyMesh proxyMesh)
        {
            proxy = proxyMesh;
            indices = new NativeArray<int>(bones.Length, Allocator.Persistent);

            for(int i = 0; i < bones.Length;i++)
            {
                indices[i] = skeleton.GetBoneIndex(bones[i]);
            }
        }

        public void OnJobComplete()
        {

        }
        public void Dispose() => OnShutdown(proxy);
        public void OnShutdown(ProxyMesh proxyMesh)
        {
            indices.Dispose();
        }

        public JobHandle StartBoneJob(JobHandle dependsOn)
        {
            return dependsOn;
        }
    }
}