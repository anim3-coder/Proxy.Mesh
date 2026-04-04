using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Jobs;

namespace Proxy.Mesh
{
    [System.Serializable]
    public class NoneInternalFeature : InternalFeature
    {

    }
    [System.Serializable]
    public abstract class InternalFeature : IProxyAbstractChild, IProxyJob, IDisposable
    {
        protected ProxyMeshAbstract proxy;
        public virtual bool IsInit => proxy != null;

        public virtual void Dispose() => OnShutdown(proxy);
        public virtual void OnInit(ProxyMeshAbstract proxyMesh)
        {
            proxy = proxyMesh;
        }

        public virtual void OnJobComplete()
        {
        }

        public virtual void OnShutdown(ProxyMeshAbstract proxyMesh)
        {
            
        }

        public virtual JobHandle StartJob(JobHandle dependsOn)
        {
            return dependsOn;
        }
    }
    [System.Serializable]
    public abstract class ExternalFeature : UnityEngine.MonoBehaviour, IProxyChild, IProxyJob, IDisposable
    {
        protected ProxyMesh proxy;
        public virtual bool IsInit => proxy != null;

        public virtual void Dispose() => OnShutdown(proxy);
        public virtual void OnInit(ProxyMesh proxyMesh)
        {
            proxy = proxyMesh;
        }

        public virtual void OnJobComplete()
        {
        }

        public virtual void OnShutdown(ProxyMesh proxyMesh)
        {

        }

        public virtual JobHandle StartJob(JobHandle dependsOn)
        {
            return dependsOn;
        }
    }
}
