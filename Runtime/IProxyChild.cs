using System.Collections;
using UnityEngine;

namespace Proxy.Mesh
{
    /// <summary>
    /// Интерфейс для элементов которые вызываются позже ProxyMesh
    /// </summary>
    public interface IProxyChild
    {
        bool IsInit { get; }
        void OnInit(ProxyMesh proxyMesh);
        void OnShutdown(ProxyMesh proxyMesh);
    }

    internal interface IProxyAbstractChild
    {
        bool IsInit { get; }
        void OnInit(ProxyMeshAbstract proxyMesh);
        void OnShutdown(ProxyMeshAbstract proxyMesh);
    }
}