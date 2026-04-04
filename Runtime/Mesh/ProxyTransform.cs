using System.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Proxy.Mesh
{
    public class ProxyTransform : MonoBehaviour, IProxyJob, IProxyChild
    {
        bool IProxyChild.IsInit => proxy != null;
        private ProxyMesh proxy;

        [SerializeField] private Transform[] parent;
        [SerializeField] private Transform[] proxyParent;

        void IProxyChild.OnInit(ProxyMesh proxyMesh)
        {
            proxy = proxyMesh;
        }

        void IProxyJob.OnJobComplete()
        {
            for (int i = 0; i < Mathf.Min(parent.Length, proxyParent.Length); i++)
            {
                proxyParent[i].position = parent[i].position;
                proxyParent[i].rotation = parent[i].rotation;
            }
        }

        void IProxyChild.OnShutdown(ProxyMesh proxyMesh)
        {
            if (proxy == proxyMesh)
                proxy = null;
        }

        JobHandle IProxyJob.StartJob(JobHandle dependsOn)
        {
            if (gameObject.activeSelf == false)
                return dependsOn;
            return dependsOn;
        }
    }
}