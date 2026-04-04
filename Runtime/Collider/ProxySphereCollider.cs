using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace Proxy.Mesh
{
    [AddComponentMenu("Proxy/Collider/Sphere Collider")]
    public class ProxySphereCollider : ProxyCollider
    {
        #region Data
        [SerializeField] private ProxyFilterAbstract filter;
        [Min(0)][field: SerializeField] public float radius { get; private set; }
        [Min(0)][field: SerializeField] public float radiusAttract { get; private set; }
        [SerializeField] private ColliderType type;
        /// <summary>
        /// Trigger - проверяет на столкновения
        /// Not Trigger - проверяет на столкновения, после чего предлагает вектор перемещения
        /// </summary>
        public bool IsTrigger
        {
            get
            {
                switch (type)
                {
                    case ColliderType.Trigger:
                        return true;
                    default:
                    case ColliderType.Collider:
                        return false;
                    case ColliderType.AttractCollider:
                        return false;
                }
            }
        }
        [Header("Move")]
        /// <summary>
        /// В случае если коллайдер isTrigger == true, но только с целью вычисления MoveDirection
        /// Например для Inverse Kinematic
        /// </summary>
        [field: SerializeField] public bool IsMovable { get; private set; }
        [field: SerializeField] public float moveSpeed { get; private set; } = 0.01f;
        [Header("Visualize")]
        public bool m_visualizeMesh = false;
        public Color m_visualizeColor = Color.red;

        private NativeHashSet<int> indices;
        private NativeArray<Vector3> moveDirection;
        #endregion

        #region Unity Methods
        private void OnValidate()
        {
            if (radiusAttract < radius)
                radiusAttract = radius;
            if (mesh != null)
                OnInit(mesh);
        }
        #endregion

        #region Interface
        public Vector3 Move;
        private Vector3 _moveDirection;
        public override Vector3 MoveDirection
        {
            get { return _moveDirection; }
            protected set { _moveDirection = value; }
        }
        public override bool IsInit { get; protected set; }

        public override JobHandle StartJob(JobHandle dependsOn)
        {
            switch (type)
            {
                default:
                case ColliderType.Trigger:
                    return new ProxySphereJobTriggerCollider
                    {
                        radius = radius,
                        position = transform.position,
                        vertices = mesh.GetVertices(),
                        indices = filter.GetIndices(transform.position),
                        result = indices
                    }.Schedule(dependsOn);
                case ColliderType.Collider:
                    return new ProxySphereJobCollider
                    {
                        radius = radius,
                        position = transform.position,
                        vertices = mesh.GetVertices(),
                        indices = filter.GetIndices(transform.position),
                        result = indices,
                        moveDirection = moveDirection
                    }.Schedule(dependsOn);
                case ColliderType.ColliderWithNormals:
                    return new ProxySphereJobColliderWithNormals
                    {
                        radius = radius,
                        position = transform.position,
                        vertices = mesh.GetVertices(),
                        indices = filter.GetIndices(transform.position),
                        normals = mesh.GetNormals(),
                        result = indices,
                        moveDirection = moveDirection
                    }.Schedule(dependsOn);
                case ColliderType.AttractCollider:
                    return new ProxySphereJobAttractCollider
                    {
                        radius = radius,
                        radiusAttract = radiusAttract,
                        position = transform.position,
                        vertices = mesh.GetVertices(),
                        indices = filter.GetIndices(transform.position),
                        result = indices,
                        moveDirection = moveDirection
                    }.Schedule(dependsOn);
            }
        }

        public override void OnJobComplete()
        {
            if (!IsTrigger)
            {
                MoveDirection = moveDirection[0] * moveSpeed;
                if (IsMovable)
                    transform.position += MoveDirection;
            }
        }

        private ProxyMesh mesh;

        public override void OnInit(ProxyMesh proxyMesh)
        {
            IsInit = true;
            mesh = proxyMesh;
            if (indices.IsCreated)
                indices.Dispose();
            if (moveDirection.IsCreated)
                moveDirection.Dispose();
            indices = new NativeHashSet<int>(0, Allocator.Persistent);
            if (IsTrigger == false)
            {
                moveDirection = new NativeArray<Vector3>(1, Allocator.Persistent);
            }
        }

        public override void OnShutdown(ProxyMesh proxyMesh)
        {
            IsInit = false;
            indices.Dispose();
            moveDirection.Dispose();
        }
        #endregion

        #region Visual
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!m_visualizeMesh)
                return;
            if (type == ColliderType.AttractCollider)
            {
                Gizmos.color = new Color(Mathf.Abs(1 - m_visualizeColor.r), Mathf.Abs(1 - m_visualizeColor.g), Mathf.Abs(1 - m_visualizeColor.b), 1);
                Gizmos.DrawWireSphere(transform.position, radiusAttract);
            }
            Gizmos.color = m_visualizeColor;
            Gizmos.DrawWireSphere(transform.position, radius);
            if (!Application.isPlaying || mesh == null)
                return;

            //global::ProxyMesh.DrawMeshWireframe(mesh, indices);
            if (MoveDirection != Vector3.zero)
            {
                Gizmos.color = Color.black;
                Gizmos.DrawWireSphere(transform.position + MoveDirection, radius);
            }
        }
#endif
        #endregion
    }
    [System.Serializable]
    public enum ColliderType
    {
        Trigger, Collider, AttractCollider, ColliderWithNormals
    }

    [BurstCompile]
    public struct ProxySphereJobTriggerCollider : IJobCollider
    {
        public float radius;
        public Vector3 position;
        [ReadOnly] public NativeArray<float3> vertices;
        [ReadOnly] public NativeHashSet<int> indices;
        public NativeHashSet<int> result;
        public void Execute()
        {
            result.Clear();
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!indices.Contains(i))
                    continue;
                if (Intersect(vertices[i]))
                {
                    result.Add(i);
                }
            }
        }

        public bool Intersect(Vector3 vector)
        {
            return (Vector3.Distance(position, vector) <= radius);
        }
        public Vector3 Counteraction(Vector3 vector)
        {
            return (position - vector).normalized * (1 / Vector3.Distance(position, vector));
        }
    }

    [BurstCompile]
    public struct ProxySphereJobColliderWithNormals : IJobCollider
    {
        public float radius;
        public float radiusSquared;
        public Vector3 position;
        [ReadOnly] public NativeArray<float3> vertices;
        [ReadOnly] public NativeArray<float3> normals;
        [ReadOnly] public NativeHashSet<int> indices;
        public NativeHashSet<int> result;
        public NativeArray<Vector3> moveDirection;

        public void Execute()
        {
            result.Clear();
            Vector3 sumCounteraction = Vector3.zero;
            int count = 0;

            radiusSquared = radius * radius;

            for (int i = 0; i < vertices.Length; i++)
            {
                if (!indices.Contains(i))
                    continue;

                if (Intersect(vertices[i]))
                {
                    sumCounteraction += Counteraction(vertices[i], normals[i]);
                    count++;
                    result.Add(i);
                }
            }

            moveDirection[0] = count > 0
                ? sumCounteraction / count
                : Vector3.zero;
        }

        public bool Intersect(Vector3 point)
        {
            return (position - point).sqrMagnitude <= radiusSquared;
        }

        public Vector3 Counteraction(Vector3 point, Vector3 normal)
        {
            return normal * (radius - Vector3.Distance(position, point));
        }

        public Vector3 Counteraction(Vector3 vector)
        {
            return default;
        }
    }

    [BurstCompile]
    public struct ProxySphereJobCollider : IJobCollider
    {
        public float radius;
        public float radiusSquared; // Добавлено для оптимизации
        public Vector3 position;
        [ReadOnly] public NativeArray<float3> vertices;
        [ReadOnly] public NativeHashSet<int> indices;
        public NativeHashSet<int> result;
        public NativeArray<Vector3> moveDirection;

        public void Execute()
        {
            result.Clear();
            Vector3 sumCounteraction = Vector3.zero;
            int count = 0;

            radiusSquared = radius * radius;

            for (int i = 0; i < vertices.Length; i++)
            {
                if (!indices.Contains(i))
                    continue;

                if (Intersect(vertices[i]))
                {
                    sumCounteraction += Counteraction(vertices[i]);
                    count++;
                    result.Add(i);
                }
            }

            moveDirection[0] = count > 0
                ? sumCounteraction / count
                : Vector3.zero;

        }

        public bool Intersect(Vector3 point)
        {
            return (position - point).sqrMagnitude <= radiusSquared;
        }

        public Vector3 Counteraction(Vector3 point)
        {
            // Расчёт вектора выталкивания с учётом глубины проникновения
            float3 offset = position - point;
            float sqrDistance = math.dot(offset, offset);

            // Обработка случая центрального пересечения
            if (sqrDistance < 1e-7f)
            {
                return new float3(0, 1, 0) * radius;
            }

            float distance = math.sqrt(sqrDistance);
            float penetration = radius - distance;
            float3 direction = offset / distance;

            return direction * penetration;
        }
    }
    [BurstCompile]
    public struct ProxySphereJobAttractCollider : IJobCollider
    {
        public float radius;
        public float radiusSquared;
        public float radiusAttract;
        public float radiusAttractSquared;

        public Vector3 position;
        [ReadOnly] public NativeArray<float3> vertices;
        [ReadOnly] public NativeHashSet<int> indices;
        public NativeHashSet<int> result;
        public NativeArray<Vector3> moveDirection;

        public void Execute()
        {
            result.Clear();
            radiusSquared = radius * radius;
            radiusAttractSquared = radiusAttract * radiusAttract;

            int countAttract = 0;
            float3 sumPoints = float3.zero;
            float3 closestPoint = float3.zero;
            float minDistanceSq = float.MaxValue;

            // Сбор всех точек в радиусе притяжения
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!indices.Contains(i)) continue;

                float3 vertex = vertices[i];
                float distSq = math.distancesq(position, vertex);

                if (distSq <= radiusAttractSquared)
                {
                    countAttract++;
                    result.Add(i);
                    sumPoints += vertex;

                    // Поиск ближайшей точки
                    if (distSq < minDistanceSq)
                    {
                        minDistanceSq = distSq;
                        closestPoint = vertex;
                    }
                }
            }

            // Рассчет перемещения
            if (countAttract >= 3)
            {
                float3 centroid = sumPoints / countAttract;
                float3 toCentroid = centroid - (float3)position;
                float centroidDist = math.length(toCentroid);

                // Плавное движение к плоскости
                float3 targetDirection = math.normalizesafe(toCentroid) *
                    math.min(centroidDist, radiusAttract);

                moveDirection[0] = Vector3.Lerp(
                    moveDirection[0],
                    targetDirection,
                    0.2f // Коэффициент сглаживания
                );
            }
            else if (countAttract > 0)
            {
                // Плавное движение к ближайшей точке
                float3 toPoint = closestPoint - (float3)position;
                float pointDist = math.length(toPoint);
                float3 targetDirection = math.normalizesafe(toPoint) *
                    math.min(pointDist, radiusAttract);

                moveDirection[0] = Vector3.Lerp(
                    moveDirection[0],
                    targetDirection,
                    0.1f // Более агрессивное сглаживание
                );
            }
            else
            {
                // Плавный возврат к нулю
                moveDirection[0] = Vector3.Lerp(moveDirection[0], Vector3.zero, 0.1f);
            }
        }

        public bool Intersect(Vector3 point) =>
            math.distancesq(position, point) <= radiusSquared;

        public bool IntersectAttract(Vector3 point) =>
            math.distancesq(position, point) <= radiusAttractSquared;
        public Vector3 Counteraction(Vector3 point)
        {
            // Расчёт вектора выталкивания с учётом глубины проникновения
            float3 offset = position - point;
            float sqrDistance = math.dot(offset, offset);

            // Обработка случая центрального пересечения
            if (sqrDistance < 1e-7f)
            {
                return new float3(0, 1, 0) * radius;
            }

            float distance = math.sqrt(sqrDistance);
            float penetration = radius - distance;
            float3 direction = offset / distance;

            return direction * penetration;
        }
    }
}