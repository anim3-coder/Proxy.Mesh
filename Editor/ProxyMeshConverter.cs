using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;
using Proxy.Mesh;

namespace Proxy.Mesh.Editor
{
    public static class ProxyMeshConverter
    {
        [MenuItem("GameObject/Convert to ProxyMesh", false, 0)]
        public static void ConvertSelectedToProxyMesh()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("No GameObject selected");
                return;
            }

            ConvertSkinnedMeshRendererToProxyMesh(selected);
        }
        [MenuItem("GameObject/Convert to ProxyChildMesh", false, 0)]
        public static void ConvertSelectedToProxyChildMesh()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("No GameObject selected");
                return;
            }

            ConvertSkinnedMeshRendererToProxyChildMesh(selected);
        }

        [MenuItem("GameObject/Convert to ProxyMesh", true)]
        public static bool ValidateConvertSelectedToProxyMesh()
        {
            GameObject selected = Selection.activeGameObject;
            return selected != null && selected.GetComponent<SkinnedMeshRenderer>() != null;
        }

        [MenuItem("CONTEXT/SkinnedMeshRenderer/Convert to ProxyMesh")]
        public static void ConvertSkinnedMeshRendererContext(MenuCommand command)
        {
            SkinnedMeshRenderer skinnedRenderer = command.context as SkinnedMeshRenderer;
            if (skinnedRenderer != null)
            {
                ConvertSkinnedMeshRendererToProxyMesh(skinnedRenderer.gameObject);
            }
        }
        [MenuItem("CONTEXT/SkinnedMeshRenderer/Convert to ProxyChildMesh")]
        public static void ConvertSkinnedMeshRendererChildContext(MenuCommand command)
        {
            SkinnedMeshRenderer skinnedRenderer = command.context as SkinnedMeshRenderer;
            if (skinnedRenderer != null)
            {
                ConvertSkinnedMeshRendererToProxyChildMesh(skinnedRenderer.gameObject);
            }
        }
        [MenuItem("CONTEXT/ProxyMesh/Upgrade To Proxy Child")]
        public static void ConvertProxyMeshToChildMesh(MenuCommand command)
        {
            ProxyMesh m = command.context as ProxyMesh;
            if (m != null)
            {
                ConvertProxyMeshToChildMesh(m.gameObject);
            }
        }
        public static void ConvertSkinnedMeshRendererToProxyMesh(GameObject target)
        {
            if (target == null)
            {
                Debug.LogError("Target GameObject is null");
                return;
            }

            SkinnedMeshRenderer skinnedRenderer = target.GetComponent<SkinnedMeshRenderer>();
            if (skinnedRenderer == null)
            {
                Debug.LogError($"No SkinnedMeshRenderer found on {target.name}");
                return;
            }

            // Check if already has ProxyMesh
            if (target.GetComponent<ProxyMesh>() != null)
            {
                Debug.LogWarning($"GameObject {target.name} already has ProxyMesh component");
                return;
            }

            Undo.RecordObject(target, "Convert to ProxyMesh");

            try
            {
                // Store original data
                UnityEngine.Mesh originalMesh = skinnedRenderer.sharedMesh;
                Material[] materials = skinnedRenderer.sharedMaterials;
                Transform rootBone = skinnedRenderer.rootBone;
                Transform[] bones = skinnedRenderer.bones;
                Bounds bounds = skinnedRenderer.localBounds;
                bool updateWhenOffscreen = skinnedRenderer.updateWhenOffscreen;
                Transform probeAnchor = skinnedRenderer.probeAnchor;
                ReflectionProbeUsage reflectionProbeUsage = skinnedRenderer.reflectionProbeUsage;
                ShadowCastingMode shadowCastingMode = skinnedRenderer.shadowCastingMode;
                bool receiveShadows = skinnedRenderer.receiveShadows;
                LightProbeUsage lightProbeUsage = skinnedRenderer.lightProbeUsage;
                uint renderingLayerMask = skinnedRenderer.renderingLayerMask;
                var rendererPriority = skinnedRenderer.rendererPriority;
                string sortingLayerName = skinnedRenderer.sortingLayerName;
                int sortingLayerID = skinnedRenderer.sortingLayerID;
                int sortingOrder = skinnedRenderer.sortingOrder;

                // Store blend shape weights
                float[] blendShapeWeights = null;
                if (originalMesh != null && originalMesh.blendShapeCount > 0)
                {
                    blendShapeWeights = new float[originalMesh.blendShapeCount];
                    for (int i = 0; i < originalMesh.blendShapeCount; i++)
                    {
                        blendShapeWeights[i] = skinnedRenderer.GetBlendShapeWeight(i);
                    }
                }

                // Remove SkinnedMeshRenderer
                Undo.DestroyObjectImmediate(skinnedRenderer);

                // Add required components
                MeshFilter meshFilter = Undo.AddComponent<MeshFilter>(target);
                MeshRenderer meshRenderer = Undo.AddComponent<MeshRenderer>(target);
                ProxyMesh proxyMesh = Undo.AddComponent<ProxyMesh>(target);

                // Configure components
                meshFilter.sharedMesh = originalMesh;
                meshRenderer.sharedMaterials = materials;

                // Copy renderer settings
                meshRenderer.localBounds = bounds;
                meshRenderer.probeAnchor = probeAnchor;
                meshRenderer.reflectionProbeUsage = reflectionProbeUsage;
                meshRenderer.shadowCastingMode = shadowCastingMode;
                meshRenderer.receiveShadows = receiveShadows;
                meshRenderer.lightProbeUsage = lightProbeUsage;
                meshRenderer.renderingLayerMask = renderingLayerMask;
                meshRenderer.rendererPriority = rendererPriority;

                // Copy sorting settings
                meshRenderer.sortingLayerName = sortingLayerName;
                meshRenderer.sortingLayerID = sortingLayerID;
                meshRenderer.sortingOrder = sortingOrder;

                proxyMesh.RootBone = rootBone;
                proxyMesh.Bones = bones;


                if (blendShapeWeights != null && originalMesh != null)
                {
                    for (int i = 0; i < blendShapeWeights.Length; i++)
                    {
                        proxyMesh.SetBlendShapeWeight(i, blendShapeWeights[i]);
                    }
                }

                Debug.Log($"Successfully converted {target.name} from SkinnedMeshRenderer to ProxyMesh");

                // Select the converted object
                Selection.activeGameObject = target;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to convert {target.name} to ProxyMesh: {e.Message}");
                // Try to restore original state
                if (target.GetComponent<SkinnedMeshRenderer>() == null)
                {
                    Undo.AddComponent<SkinnedMeshRenderer>(target);
                }
            }
        }
        public static void ConvertSkinnedMeshRendererToProxyChildMesh(GameObject target)
        {
            if (target == null)
            {
                Debug.LogError("Target GameObject is null");
                return;
            }

            SkinnedMeshRenderer skinnedRenderer = target.GetComponent<SkinnedMeshRenderer>();
            if (skinnedRenderer == null)
            {
                Debug.LogError($"No SkinnedMeshRenderer found on {target.name}");
                return;
            }

            // Check if already has ProxyMesh
            if (target.GetComponent<ProxyMeshAbstract>() != null)
            {
                Debug.LogWarning($"GameObject {target.name} already has ProxyMesh component");
                return;
            }

            Undo.RecordObject(target, "Convert to ProxyMesh");

            //try
            //{
            // Store original data
            UnityEngine.Mesh originalMesh = skinnedRenderer.sharedMesh;
            Material[] materials = skinnedRenderer.sharedMaterials;
            Transform rootBone = skinnedRenderer.rootBone;
            Transform[] bones = skinnedRenderer.bones;
            Bounds bounds = skinnedRenderer.localBounds;
            bool updateWhenOffscreen = skinnedRenderer.updateWhenOffscreen;
            Transform probeAnchor = skinnedRenderer.probeAnchor;
            ReflectionProbeUsage reflectionProbeUsage = skinnedRenderer.reflectionProbeUsage;
            ShadowCastingMode shadowCastingMode = skinnedRenderer.shadowCastingMode;
            bool receiveShadows = skinnedRenderer.receiveShadows;
            LightProbeUsage lightProbeUsage = skinnedRenderer.lightProbeUsage;
            uint renderingLayerMask = skinnedRenderer.renderingLayerMask;
            var rendererPriority = skinnedRenderer.rendererPriority;
            string sortingLayerName = skinnedRenderer.sortingLayerName;
            int sortingLayerID = skinnedRenderer.sortingLayerID;
            int sortingOrder = skinnedRenderer.sortingOrder;

            // Store blend shape weights
            float[] blendShapeWeights = null;
            if (originalMesh != null && originalMesh.blendShapeCount > 0)
            {
                blendShapeWeights = new float[originalMesh.blendShapeCount];
                for (int i = 0; i < originalMesh.blendShapeCount; i++)
                {
                    blendShapeWeights[i] = skinnedRenderer.GetBlendShapeWeight(i);
                }
            }

            // Remove SkinnedMeshRenderer
            Undo.DestroyObjectImmediate(skinnedRenderer);

            // Add required components
            MeshFilter meshFilter = Undo.AddComponent<MeshFilter>(target);
            MeshRenderer meshRenderer = Undo.AddComponent<MeshRenderer>(target);
            ProxyChildMesh proxyMesh = Undo.AddComponent<ProxyChildMesh>(target);

            // Configure components
            meshFilter.sharedMesh = originalMesh;
            meshRenderer.sharedMaterials = materials;

            // Copy renderer settings
            meshRenderer.localBounds = bounds;
            meshRenderer.probeAnchor = probeAnchor;
            meshRenderer.reflectionProbeUsage = reflectionProbeUsage;
            meshRenderer.shadowCastingMode = shadowCastingMode;
            meshRenderer.receiveShadows = receiveShadows;
            meshRenderer.lightProbeUsage = lightProbeUsage;
            meshRenderer.renderingLayerMask = renderingLayerMask;
            meshRenderer.rendererPriority = rendererPriority;

            // Copy sorting settings
            meshRenderer.sortingLayerName = sortingLayerName;
            meshRenderer.sortingLayerID = sortingLayerID;
            meshRenderer.sortingOrder = sortingOrder;

            // Initialize ProxyMesh
            if (Application.isPlaying)
            {
                proxyMesh.ReInit();
            }
            else
            {
                // Force initialization in editor mode
                var awakeMethod = typeof(ProxyChildMesh).GetMethod("Awake",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (awakeMethod != null)
                {
                    awakeMethod.Invoke(proxyMesh, null);
                }
            }

            // Restore blend shape weights
            proxyMesh.SetBlendShapeWeights(blendShapeWeights);

            Debug.Log($"Successfully converted {target.name} from SkinnedMeshRenderer to ProxyMesh");

            // Select the converted object
            Selection.activeGameObject = target;
            //}
            /*catch (System.Exception e)
            {
                Debug.LogError($"Failed to convert {target.name} to ProxyMesh: {e.Message}");
                // Try to restore original state
                if (target.GetComponent<SkinnedMeshRenderer>() == null)
                {
                    Undo.AddComponent<SkinnedMeshRenderer>(target);
                }
            }*/
        }
        public static void ConvertProxyMeshToChildMesh(GameObject target)
        {
            Undo.RecordObject(target, "Convert to ProxyMesh");

            var proxy = target.GetComponent<ProxyMesh>();
            ProxyChildMesh proxyMesh = Undo.AddComponent<ProxyChildMesh>(target);
            Undo.DestroyObjectImmediate(proxy);

            Selection.activeGameObject = target;
        }

        [MenuItem("GameObject/Convert All Children to ProxyChildMesh", false, 0)]
        public static void ConvertAllChildrenToProxyChildMesh()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("No GameObject selected");
                return;
            }

            ConvertAllSkinnedMeshRenderersInChildren(selected);
        }

        [MenuItem("GameObject/Convert All Children to ProxyMesh", true)]
        public static bool ValidateConvertAllChildrenToProxyMesh()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null) return false;

            SkinnedMeshRenderer[] skinnedRenderers = selected.GetComponentsInChildren<SkinnedMeshRenderer>();
            return skinnedRenderers.Length > 0;
        }

        public static void ConvertAllSkinnedMeshRenderersInChildren(GameObject parent)
        {
            if (parent == null) return;

            SkinnedMeshRenderer[] skinnedRenderers = parent.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (skinnedRenderers.Length == 0)
            {
                Debug.LogWarning($"No SkinnedMeshRenderer components found in children of {parent.name}");
                return;
            }

            List<GameObject> convertedObjects = new List<GameObject>();

            foreach (SkinnedMeshRenderer skinnedRenderer in skinnedRenderers)
            {
                if (skinnedRenderer.gameObject.GetComponent<ProxyMeshAbstract>() == null)
                {
                    ConvertSkinnedMeshRendererToProxyChildMesh(skinnedRenderer.gameObject);
                    convertedObjects.Add(skinnedRenderer.gameObject);
                }
            }

            Debug.Log($"Converted {convertedObjects.Count} SkinnedMeshRenderer components to ProxyMesh");

            if (convertedObjects.Count > 0)
            {
                Selection.objects = convertedObjects.ToArray();
            }
        }
    }
}