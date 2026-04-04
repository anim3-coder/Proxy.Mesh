using System.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Proxy.Mesh
{
    [RequireComponent(typeof(UnityEngine.Collider))]
    public class ProxyRegistrationCollider : Collider
    {
        public Proxy.ColliderType type;
        private new UnityEngine.Collider collider;
        public float SmoothingRadius = 1f;
        private float3 point1;
        private float3 point2;
        private float radius;
        private SphereCollider sphereCollider;
        private CapsuleCollider capsuleCollider;
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
        private void Reset()
        {
            if(TryGetComponent<CapsuleCollider>(out CapsuleCollider _))
                type = Proxy.ColliderType.Capsule;
            if (TryGetComponent<SphereCollider>(out SphereCollider _))
                type = Proxy.ColliderType.Sphere;
        }
        protected virtual void OnEnable()
        {
            collider = GetComponent<UnityEngine.Collider>();
            if (collider is SphereCollider)
            {
                sphereCollider = collider as SphereCollider;
                type = Proxy.ColliderType.Sphere;
            }
            if (collider is CapsuleCollider)
            {
                capsuleCollider = collider as CapsuleCollider;
                type = Proxy.ColliderType.Capsule;
            }
        }
        protected virtual void Update()
        {
            switch (type)
            {
                case Proxy.ColliderType.Sphere:
                    point1 = transform.TransformPoint(sphereCollider.center);
                    radius = sphereCollider.radius * transform.lossyScale.x;
                    break;
                case Proxy.ColliderType.Capsule:
                    GetCapsulePoints(capsuleCollider, out point1, out point2);
                    radius = capsuleCollider.radius * transform.lossyScale.x;
                    break;
            }
        }
        public void GetCapsulePoints(CapsuleCollider capsule, out float3 start, out float3 end)
        {
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;

            // Локальные координаты направления капсулы
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

            start = transform.TransformPoint(localStart);
            end = transform.TransformPoint(localEnd);
        }
        public override float GetForceAtPoint(float3 point)
        {
            switch (type)
            {
                case Proxy.ColliderType.Sphere:
                    return Physics.IsPointInSphere(point, point1, radius) ? 1 : 0;
                case Proxy.ColliderType.Capsule:
                    return Physics.IsPointInCapsule(point, point1, point2, radius) ? 1 : 0;
                default:
                    return 0;
            }
        }
        public override DeformInfo GetDeformInfo()
        {
            return new DeformInfo()
            {
                type = type,
                point1 = point1,
                point2 = point2,
                radius = radius,
            };
        }
    }
}