using System.Collections;
using UnityEngine;
using TriInspector;

namespace Proxy.Mesh
{
    [System.Serializable]
    [DeclareHorizontalGroup("vars")]
    public struct BlendShapeData
    {
        [Group("vars"), Slider(0,1), HideLabel] public float value;
        [Group("vars"), ReadOnly, HideLabel] public string name;
        public static implicit operator float(BlendShapeData d)
        {
            return d.value;
        }
    }

    public static class E_BlendShapeData
    {
        public static float[] Convert(this BlendShapeData[] b)
        {
            float[] v = new float[b.Length];
            for(int i = 0; i < b.Length; i++) 
                v[i] = b[i].value;
            return v;
        }
        public static void Write(this BlendShapeData[] b, float[] a)
        {
            for (int i = 0; i < b.Length; i++)
                b[i].value = a[i];
        }
    }
}