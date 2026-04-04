using System.Collections;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

namespace Proxy
{
    public static partial class Math
    {
        [BurstCompile]
        public static Vector3 FindNearestPointOnPlane(
            Vector3 planePointA,
            Vector3 planePointB,
            Vector3 planePointC,
            Vector3 targetPoint)
        {
            Vector3 vectorAB = planePointB - planePointA;
            Vector3 vectorAC = planePointC - planePointA;
            Vector3 normal = Vector3.Cross(vectorAB, vectorAC);

            if (normal.sqrMagnitude < Mathf.Epsilon)
                return planePointA;

            Vector3 pointToTarget = targetPoint - planePointA;
            float distance = Vector3.Dot(normal, pointToTarget) / normal.sqrMagnitude;
            return targetPoint - normal * distance;
        }
        [BurstCompile]
        public static Vector3 FindNearestPointOnPlane(
            Vector3 planePointA,
            Vector3 planePointB,
            Vector3 planePointC,
            Vector3 targetPoint,
            float addNormalDistance)
        {
            // Вычисляем два вектора в плоскости
            Vector3 vectorAB = planePointB - planePointA;
            Vector3 vectorAC = planePointC - planePointA;

            // Вычисляем нормаль плоскости через векторное произведение
            Vector3 normal = Vector3.Cross(vectorAB, vectorAC);

            // Проверка на вырожденную плоскость
            float normalSqrMagnitude = normal.sqrMagnitude;
            if (normalSqrMagnitude < Mathf.Epsilon)
            {
                // Возвращаем первую точку плоскости как fallback
                return planePointA;
            }

            // Вычисляем вектор от точки на плоскости к целевой точке
            Vector3 pointToTarget = targetPoint - planePointA;

            // Вычисляем расстояние по нормали (скалярное произведение)
            float distance = addNormalDistance + (Vector3.Dot(normal, pointToTarget) / normalSqrMagnitude);

            var point = targetPoint - normal * distance;
            if (IsNaN(point))
                return targetPoint;
            // Корректируем целевую точку по нормали
            return point;
        }
        [BurstCompile]
        public static bool IsNaN(Vector3 v)
        {
            return float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z);
        }

        [BurstCompile]
        public static float3 FindNearestPointOnLine(float3 a, float3 b, float3 point)
        {
            float3 ab = b - a;
            float3 ap = point - a;

            float dot = math.dot(ap, ab);
            float sqLen = math.dot(ab, ab);

            if (sqLen == 0f)
                return a;

            float t = dot / sqLen;
            t = math.clamp(t, 0f, 1f);
            return a + t * ab;               
        }

        [BurstCompile]
        public static float3 GetNormal(float3 a, float3 b)
        {
            return math.normalize(b - a);
        }
        [BurstCompile]
        public static float2 FindNearestPointOnLine(float2 a, float2 b, float2 point)
        {
            float2 ab = b - a;
            float2 ap = point - a;

            float dot = math.dot(ap, ab);
            float sqLen = math.dot(ab, ab);

            if (sqLen == 0f)
                return a;

            float t = dot / sqLen;
            t = math.clamp(t, 0f, 1f);
            return a + t * ab;
        }
        [BurstCompile]
        public static float DistanceToEdge(float3 center, float radius, float3 point, float3 direction)
        {
            float3 oc = point - center;
            float b = math.dot(direction, oc);
            float c = math.dot(oc, oc) - radius * radius;
            float discriminant = b * b - c;
            if (discriminant < 0)
                return float.NaN;
            float sqrtD = math.sqrt(discriminant);
            float t = -b + sqrtD;

            return t;
        }
    }
}