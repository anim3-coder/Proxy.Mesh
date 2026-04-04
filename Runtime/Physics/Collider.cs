using System.Collections;
using TriInspector;
using Unity.Mathematics;
using UnityEngine;

namespace Proxy.Mesh
{ 
    public abstract class Collider : MonoBehaviour, IDeform
    {
        [ShowInInspector] public Vector3 outPenetration { get; set; }
        public abstract float GetForceAtPoint(float3 point);
        public abstract DeformInfo GetDeformInfo();
    }
}