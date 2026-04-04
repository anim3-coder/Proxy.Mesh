using System.Collections;
using Unity.Collections;
using UnityEngine;

namespace Proxy.Mesh
{
    public abstract class ProxyFilterAbstract : MonoBehaviour, IProxyChild
    {
        public abstract bool IsInit { get; protected set; }

        public abstract void OnInit(ProxyMesh proxyMesh);
        public abstract void OnShutdown(ProxyMesh proxyMesh);
        public abstract NativeHashSet<int> GetIndices(Vector3 vector);
    }
}