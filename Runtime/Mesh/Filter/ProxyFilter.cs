using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
namespace Proxy.Mesh
{
    /// <summary>
    /// Возвращает массив точек из ProxyMesh только с фильтром на определенные кости
    /// </summary>
    [AddComponentMenu("Proxy/ProxyFilter")]
    public class ProxyFilter : ProxyFilterAbstract
    {
        #region Data
        public Transform[] bones;
        [Range(0, 1)] public float minWeight;
        public bool m_visualizeMesh = false;
        public Color m_visualizeColor = Color.red;

        private NativeHashSet<int> indices;
        private ProxyMesh ProxyMesh;
        #endregion

        #region Unity Methods

        private void OnValidate()
        {
            OnInit();
        }

        #endregion

        #region Interface

        [ContextMenu("Init")]
        public void OnInit()
        {
            if (ProxyMesh == null)
                return;
            OnInit(ProxyMesh);
        }
        public override bool IsInit { get; protected set; }
        public override void OnInit(ProxyMesh proxyMesh)
        {
            IsInit = true;
            ProxyMesh = proxyMesh;
            if (indices.IsCreated)
                indices.Dispose();

            indices = new NativeHashSet<int>(0, AllocatorManager.Persistent);
            for (int i = 0; i < bones.Length; i++)
            {
                var vertices = proxyMesh.GetVerticesForBone(bones[i], minWeight);
                for (int j = 0; j < vertices.Count; j++)
                    this.indices.Add(vertices[j]);
            }
        }
        public override void OnShutdown(ProxyMesh proxyMesh)
        {
            if (indices.IsCreated)
                indices.Dispose();
            IsInit = false;
        }
        public override NativeHashSet<int> GetIndices(Vector3 vector)
        {
            return indices;
        }
        #endregion

        #region Visual
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || !m_visualizeMesh)
                return;
            Gizmos.color = m_visualizeColor;
            //global::ProxyMesh.DrawMeshWireframe(ProxyMesh,indices);
        }
#endif
        #endregion
    }
}