using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Jobs;
using UnityEngine;

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
        private ProxyMesh m_proxy;
        public ProxyMesh proxy
        {
            get
            {
#if UNITY_EDITOR
                if (Application.isPlaying == false && m_proxy == null)
                {
                    if(TryGetComponent<ProxyMesh>(out ProxyMesh proxyMesh))
                    {
                        return proxyMesh;
                    }
                    return GetComponentInParent<ProxyMesh>();
                }
#endif
                return m_proxy;
            }
            protected set
            {
                m_proxy = value;
            }
        }

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
