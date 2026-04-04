using System.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Proxy.Mesh
{
    public interface IDeform
    {
        public float GetForceAtPoint(float3 point);
        public DeformInfo GetDeformInfo();
    }
}