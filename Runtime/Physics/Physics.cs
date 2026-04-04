using System.Collections;
using UnityEngine;

namespace Proxy
{
    
    public static partial class Physics
    {
        public static bool IsPointInSphere(Vector3 point, Vector3 center, float radius)
        {
            float dx = point.x - center.x;
            float dy = point.y - center.y;
            float dz = point.z - center.z;

            float distanceSquared = dx * dx + dy * dy + dz * dz;
            float radiusSquared = radius * radius;

            return distanceSquared < radiusSquared; // строго внутри
        }

        public static bool IsPointInCapsule(Vector3 point, Vector3 start, Vector3 end, float radius)
        {
            // Вектор, задающий ось капсулы
            Vector3 capsuleDirection = end - start;
            float capsuleLength = capsuleDirection.magnitude;

            // Нормализованный вектор направления капсулы
            Vector3 normalizedDirection = capsuleDirection.normalized;

            // Вектор от начала капсулы до точки
            Vector3 pointToStart = point - start;

            // Проекция вектора pointToStart на ось капсулы
            float projection = Vector3.Dot(pointToStart, normalizedDirection);

            // Ограничение проекции в пределах отрезка капсулы
            float clampedProjection = Mathf.Clamp(projection, 0f, capsuleLength);

            // Находим ближайшую точку на оси капсулы к заданной точке
            Vector3 closestPointOnAxis = start + normalizedDirection * clampedProjection;

            // Вычисляем расстояние от точки до этой ближайшей точки на оси
            float distanceToAxis = (point - closestPointOnAxis).magnitude;

            // Если расстояние меньше или равно радиусу — точка внутри капсулы
            return distanceToAxis <= radius;
        }
    }
    public enum ColliderType
    {
        Sphere,Capsule
    }
}
