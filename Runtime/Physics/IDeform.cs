using System.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Proxy.Mesh
{
    public interface IDeform
    {
        public DeformInfo GetDeformInfo();
    }
}