using System.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Proxy.Mesh
{
    [RequireComponent(typeof(Collider))]
    public class ProxyRegistrationCollider : ProxyCollider
    {
        public override ColliderType type
        {
            get
            {
                if(collider as SphereCollider)
                    return ColliderType.Sphere;
                if(collider as CapsuleCollider)
                    return ColliderType.Capsule;
                return ColliderType.Sphere;
            }
        }
        #region Collider
        private Collider m_collider;
        private new Collider collider
        {
            get
            {
                if (m_collider == null)
                    m_collider = GetComponent<Collider>();
                return m_collider;
            }
        }
        #endregion
        public override float3 Point1
        {
            get
            {
                switch(type)
                {
                    default:
                    case ColliderType.Sphere:
                        return (collider as SphereCollider).center;
                    case ColliderType.Capsule:
                        GetCapsulePoints(collider as CapsuleCollider, out float3 p1, out float3 p2);
                        return p1;
                }
            }
        }
        public override float3 Point2
        {
            get
            {
                switch (type)
                {
                    default:
                    case ColliderType.Sphere:
                        return 0;
                    case ColliderType.Capsule:
                        GetCapsulePoints(collider as CapsuleCollider, out float3 p1, out float3 p2);
                        return p2;
                }
            }
        }
        public override float radius 
        { 
            get 
            {
                switch (type)
                {
                    default:
                    case ColliderType.Sphere:
                        return (collider as SphereCollider).radius;
                    case ColliderType.Capsule:
                        return (collider as CapsuleCollider).radius;
                }
            } 
        }
        public void GetCapsulePoints(CapsuleCollider capsule, out float3 start, out float3 end)
        {
            Vector3 localDirection;
            Vector3 localCenter = capsule.center;

            switch (capsule.direction)
            {
                case 0: // X-axis
                    localDirection = Vector3.right;
                    break;
                case 1: // Y-axis (по умолчанию)
                    localDirection = Vector3.up;
                    break;
                case 2: // Z-axis
                    localDirection = Vector3.forward;
                    break;
                default:
                    localDirection = Vector3.up;
                    break;
            }

            float halfHeight = Mathf.Max(0f, (capsule.height - capsule.radius * 2f) * 0.5f);

            Vector3 localStart = localCenter - localDirection * halfHeight;
            Vector3 localEnd = localCenter + localDirection * halfHeight;

            start = localStart;
            end = localEnd;
        }
        public override DeformInfo GetDeformInfo()
        {
            return new DeformInfo()
            {
                type = type,
                point1 = transform.TransformPoint(Point1),
                point2 = transform.TransformPoint(Point2),
                radius = radius * transform.lossyScale.x,
            };
        }
    }
}