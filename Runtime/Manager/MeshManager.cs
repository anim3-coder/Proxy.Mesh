using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using UnityEngine;

namespace Proxy.Mesh
{
    public class MeshManager : IManager
    {
        private HashSet<ProxyMesh> proxies = new HashSet<ProxyMesh>();

        public void Registration(ProxyMesh mesh)
        {
            proxies.Add(mesh);
        }
        public void Remove(ProxyMesh mesh)
        {
            proxies.Remove(mesh);
        }

        public void FixedUpdate()
        {
            foreach (ProxyMesh proxy in proxies)
                proxy.OnFixedUpdate();
        }

        public void LateUpdate()
        {
            foreach (ProxyMesh proxy in proxies)
                proxy.OnLateUpdate(ProxyManager.jobCompleted);
        }

        public JobHandle StartJob(JobHandle dependsOn)
        {
            foreach (ProxyMesh proxy in proxies)
            {
                dependsOn = proxy.StartNewJob(dependsOn);
            }
            return dependsOn;
        }

        public void Update()
        {
            foreach (ProxyMesh proxy in proxies)
                proxy.OnUpdate(ProxyManager.jobCompleted);
        }

        public void OnInit()
        {
        }

        public void OnShutdown()
        {
        }
    }
}