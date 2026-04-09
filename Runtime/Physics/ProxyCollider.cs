using System.Collections;
using TriInspector;
using Unity.Mathematics;
using UnityEngine;

namespace Proxy.Mesh
{ 
    public abstract class ProxyCollider : MonoBehaviour, IDeform
    {
        #region Transform
        private Transform _transform;
        public new Transform transform
        {
            get
            {
                if (_transform == null)
                    _transform = base.transform;
                return _transform;
            }
        }
        #endregion

        [ShowInInspector] public Vector3 outPenetration { get; set; }

        public abstract ColliderType type { get; }

        public abstract float3 Point1 { get; }

        public abstract float3 Point2 { get; }

        public abstract float radius { get; }

        protected virtual void OnEnable()
        {
            ProxyManager.ColliderManager.Registration(this);
        }

        protected virtual void OnDisable()
        {
            ProxyManager.ColliderManager.Remove(this);
        }

        public RawDeformInfo GetRawDeformInfo()
        {
            return new RawDeformInfo()
            {
                point1 = Point1,
                point2 = Point2,
                radius = radius,
                type = type,
            };
        }
        
        public abstract DeformInfo GetDeformInfo();
    }

    public struct RawDeformInfo
    {
        public ColliderType type;
        public float3 point1;
        public float3 point2;
        public float radius;
    }
}