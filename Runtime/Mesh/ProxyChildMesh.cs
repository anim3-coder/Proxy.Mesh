using Proxy;
using System.Collections.Generic;
using System.Text;
using TriInspector;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;

namespace Proxy.Mesh
{

    [DisallowMultipleComponent, AddComponentMenu("Proxy/ProxyChildMesh")]
    public class ProxyChildMesh : ProxyMeshAbstract, IProxyChild, IProxyJob
    {
        ProxyMesh proxy;
        [field: SerializeField]public JobType type { get; set; }
        [SerializeField, Required, Group("Main")] private ProxyMesh Proxy;
        [ShowInInspector, ReadOnly, Group("Main")] public override ProxySkeleton skeleton
        {
            get
            {
                if (proxy == null)
                    return base.skeleton;
                return Proxy.skeleton;
            }
            protected set
            {
            }
        }

        bool IProxyChild.IsInit => proxy != null;

        void IProxyChild.OnInit(ProxyMesh proxyMesh)
        {
            proxy = proxyMesh;
            Initialize();
        }

        void IProxyJob.OnJobComplete() => UpdateMesh();

        void IProxyChild.OnShutdown(ProxyMesh proxyMesh)
        {
            proxy = null;
            Cleanup();
        }

        protected override JobHandle StartNewJob(JobHandle dependsOn)
        {
            if (gameObject.activeSelf == false)
                return dependsOn;

            if(isDirty)
            {
                UpdateBlendShapes();
            }

            InitBounds();
            dependsOn = UpdateSkeletonDeformation(dependsOn);
            dependsOn = RecalculateBounds(dependsOn);

            return dependsOn;
        }

        JobHandle IProxyJob.StartJob(JobHandle dependsOn) => StartNewJob(dependsOn);
    }
}