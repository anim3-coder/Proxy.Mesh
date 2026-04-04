namespace Proxy.Mesh.Editor
{
    using UnityEngine;
    using UnityEditor;

    public class TransferBones : EditorWindow
    {
        private ProxyMeshAbstract From;
        private ProxyMeshAbstract To;

        [MenuItem("Tools/Proxy/TransferBones")]
        public static void ShowWindow()
        {
            TransferBones window = GetWindow<TransferBones>();
            window.titleContent = new GUIContent("Transfer Bones");
            window.Show();
        }

        private void OnGUI()
        {
            GUILayout.Label("Select two proxy", EditorStyles.boldLabel);

            // Поля для выбора объектов
            From = (ProxyMeshAbstract)EditorGUILayout.ObjectField("From", From, typeof(ProxyMeshAbstract), true);
            To = (ProxyMeshAbstract)EditorGUILayout.ObjectField("To", To, typeof(ProxyMeshAbstract), true);

            if (GUILayout.Button("Transfer"))
            {
                To.skeleton.rootBone = To.transform.parent.FindChildRecursive(From.skeleton.rootBone.name);
                To.skeleton.bones = new Transform[From.skeleton.bones.Length];
                for(int i = 0;  i < From.skeleton.bones.Length;i++)
                {
                    To.skeleton.bones[i] = To.transform.parent.FindChildRecursive(From.skeleton.bones[i].name);
                }
            }
            GUI.enabled = true;
        }
    }

    public static partial class E_Transform
    {
        public static Transform FindChildRecursive(this Transform parent, string childName)
        {
            for(int i = 0; i < parent.childCount;i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == childName)
                    return child;

                Transform result = child.FindChildRecursive(childName);
                if (result != null)
                    return result;
            }
            return null;
        }
    }
}