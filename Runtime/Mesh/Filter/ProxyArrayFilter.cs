using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;
namespace Proxy.Mesh
{
    /// <summary>
    /// Возвращает массив точек из ProxyMesh только с фильтром на определенные кости
    /// Возвращает массив ТОЛЬКО для ближайщей кости
    /// </summary>
    [AddComponentMenu("Proxy/ProxyArrayFilter")]
    public class ProxyArrayFilter : ProxyFilterAbstract
    {
        #region Data
        public Transform[] bones;
        [Range(0, 1)] public float minWeight;
        public bool m_visualizeMesh = false;
        public Color m_visualizeColor = Color.red;

        private NativeHashSet<int>[] indices;
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
            Dispose();
            indices = new NativeHashSet<int>[bones.Length];

            for (int i = 0; i < bones.Length; i++)
            {
                indices[i] = new NativeHashSet<int>(0, AllocatorManager.Persistent);
                var vertices = proxyMesh.GetVerticesForBone(bones[i], minWeight);
                for (int j = 0; j < vertices.Count; j++)
                    this.indices[i].Add(vertices[j]);
            }
        }
        public void Dispose()
        {
            if (indices != null)
            {
                for (int i = 0; i < indices.Length; i++)
                    if (indices[i].IsCreated)
                        indices[i].Dispose();
            }
        }
        public override void OnShutdown(ProxyMesh proxyMesh)
        {
            if (indices != null)
            {
                for (int i = 0; i < indices.Length; i++)
                    if (indices[i].IsCreated)
                        indices[i].Dispose();
            }
            IsInit = false;
        }
        public override NativeHashSet<int> GetIndices(Vector3 vector)
        {
            int id = 0;
            for (int i = 1; i < bones.Length; i++)
            {
                if (Vector3.Distance(vector, bones[i].transform.position) < Vector3.Distance(vector, bones[id].transform.position))
                {
                    id = 1;
                }
            }
            return indices[id];
        }
        #endregion

        #region Visual
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || !m_visualizeMesh)
                return;
            Gizmos.color = m_visualizeColor;
            //for(int i = 0; i < indices.Length; i++)
            //    global::ProxyMesh.DrawMeshWireframe(ProxyMesh, indices[i]);
        }
#endif
        #endregion
    }
}