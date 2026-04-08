using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Proxy.Mesh
{

    [System.Serializable]
    public class MapExport : InternalFeature
    {
        [SerializeField, TriInspector.Group("Main")] private string texturePropertyName = "_VectorDisplacementMap";
        [field: SerializeField, TriInspector.Group("Main")] public int textureSize { get; private set; } = 64;
        [field: SerializeField, TriInspector.Group("Main")] public float maxDeformation { get; private set; } = 0.5f;
        [field: SerializeField, TriInspector.Group("Main")] public Material material {  get; private set; }
        public override bool IsInit => base.IsInit && subMeshIndex != -1;
        [TriInspector.ShowInInspector] private int subMeshIndex;
        public NativeArray<Color> rawTexture { get; private set; }
        private Texture2D texture;
        public int totalPixels => textureSize * textureSize;
        public NativeArray<int3> triangles
        {
            get
            {
                if (subMeshIndex >= 0)
                    return proxy.subMeshTriangles[subMeshIndex];
                return default;
            }
        }
        public override void OnInit(ProxyMeshAbstract proxyMesh)
        {
            base.OnInit(proxyMesh);

            subMeshIndex = -1;
            for (int i = 0; i < proxy.sharedMaterials.Length; i++) 
                if(proxy.sharedMaterials[i] == material)
                    subMeshIndex = i;

            rawTexture = new NativeArray<Color>(totalPixels, Allocator.Persistent);
            texture = CreateEmptyTexture(texture);
        }

        public override void OnShutdown(ProxyMeshAbstract proxyMesh)
        {
            base.OnShutdown(proxyMesh);

            if(rawTexture.IsCreated) rawTexture.Dispose();
            MonoBehaviour.Destroy(texture);
        }

        public override void OnJobComplete()
        {
            base.OnJobComplete();

            if (texture == null || texture.width != textureSize || texture.height != textureSize)
            {
                OnShutdown(proxy);
                OnInit(proxy);
            }
            else
            {
                texture.SetPixelData(rawTexture, 0);
                texture.Apply();

                if (material != null && material.HasProperty(texturePropertyName))
                    material.SetTexture(texturePropertyName, texture);
            }
        }

        private Texture2D CreateEmptyTexture(Texture2D texture)
        {
            if (texture != null) MonoBehaviour.Destroy(texture);
            texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBAFloat, false, true);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            var empty = new NativeArray<Color>(totalPixels, Allocator.Temp);
            for (int i = 0; i < empty.Length; i++)
                empty[i] = new Color(0.5f, 0.5f, 0.5f, 1f);
            texture.SetPixelData(empty, 0);
            texture.Apply();
            empty.Dispose();
            return texture;
        }
    }
}
