using Proxy.Mesh;
using System;
using System.Collections;
using Unity.Collections;
using UnityEngine;

namespace Proxy.Mesh
{
    [System.Serializable]
    public class VertexNeighbours : IProxyAbstractChild, IDisposable
    {
        public bool IsInit => proxyMesh != null;
        private ProxyMeshAbstract proxyMesh;

        public NativeArray<int> neighbourCounts
        {
            get
            {
                if (_neighbourCounts.IsCreated == false)
                    MeshUtils.ComputeVertexNeighbours(proxyMesh.triangles, proxyMesh.vertexCount, out _neighbourCounts, out _neighbourIndices, out _neighbourStartOffsets, 8 , Allocator.Persistent);
                return _neighbourCounts;
            }
        }
        public NativeArray<int> neighbourIndices
        {
            get
            {
                if (_neighbourIndices.IsCreated == false)
                    MeshUtils.ComputeVertexNeighbours(proxyMesh.triangles, proxyMesh.vertexCount, out _neighbourCounts, out _neighbourIndices, out _neighbourStartOffsets, 8, Allocator.Persistent);
                return _neighbourIndices;
            }
        }
        public NativeArray<int> neighbourStartOffsets
        {
            get
            {
                if (_neighbourStartOffsets.IsCreated == false)
                    MeshUtils.ComputeVertexNeighbours(proxyMesh.triangles, proxyMesh.vertexCount, out _neighbourCounts, out _neighbourIndices, out _neighbourStartOffsets, 8 ,Allocator.Persistent);
                return _neighbourStartOffsets;
            }
        }
        private NativeArray<int> _neighbourCounts;
        private NativeArray<int> _neighbourIndices;
        private NativeArray<int> _neighbourStartOffsets;


        public void OnInit(ProxyMeshAbstract proxyMesh)
        {
            this.proxyMesh = proxyMesh;
        }
        public void Dispose() => OnShutdown(proxyMesh);
        public void OnShutdown(ProxyMeshAbstract proxyMesh)
        {
            if(_neighbourCounts.IsCreated) _neighbourCounts.Dispose();
            if(_neighbourIndices.IsCreated) _neighbourIndices.Dispose();
            if(_neighbourStartOffsets.IsCreated) _neighbourStartOffsets.Dispose();
        }
    }
}