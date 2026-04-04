using System.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using System;

namespace Proxy.Mesh
{
    public class ProxyVertexShake : MonoBehaviour, IProxyChild, IProxyJob, IDisposable
    {
        [Header("Bone Filtering")]
        [SerializeField] private Transform[] shakeBones; // Кости, для которых работает тряска
        [SerializeField, Range(0f, 1f)] private float boneEndFalloff = 0.3f; // Затухание тряски у концов костей
        [Header("Shake Settings")]
        [SerializeField, Range(0.1f, 25f)] private float shakeIntensity = 0.8f;
        [SerializeField, Range(0.5f, 20f)] private float shakeDamping = 1.5f;
        [SerializeField, Range(1f, 20f)] private float shakeFrequency = 8f;
        [SerializeField] private float maxLimbLength = 2f;
        [SerializeField, Range(5f, 200f)] private float maxShakeVelocity = 25f;
        [SerializeField, Range(0.01f, 5f)] private float maxDisplacementFactor = 0.15f;
        private NativeArray<Vector3> shakeVelocities;
        private NativeArray<Vector3> shakeDisplacements;
        private NativeArray<BoneSegment> boneSegments;
        private NativeArray<float> vertexDistancesToBones;
        private NativeArray<bool> activeShakeBones;

        public bool IsInit => proxy != null && enabled;
        private ProxyMesh proxy;
        private int vertexCount => proxy.vertexCount;
        private Transform[] bones => proxy.skeleton.bones;
        private void InitializeBoneSegments()
        {
            boneSegments = new NativeArray<BoneSegment>(bones.Length, Allocator.Persistent);

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null)
                {
                    boneSegments[i] = new BoneSegment { isValid = false };
                    continue;
                }

                Transform bone = bones[i];
                Transform parent = bone.parent;

                BoneSegment segment = new BoneSegment
                {
                    boneIndex = i,
                    isValid = false
                };

                if (parent != null)
                {
                    for (int j = 0; j < bones.Length; j++)
                    {
                        if (bones[j] == parent)
                        {
                            segment.parentBoneIndex = j;
                            segment.isValid = true;
                            break;
                        }
                    }
                }

                if (segment.isValid)
                {
                    Vector3 bonePos = GetBoneBindPosition(i);
                    Vector3 parentPos = GetBoneBindPosition(segment.parentBoneIndex);

                    segment.localDirection = (bonePos - parentPos).normalized;
                    segment.length = Vector3.Distance(bonePos, parentPos);
                }

                boneSegments[i] = segment;
            }
        }

        private Vector3 GetBoneBindPosition(int boneIndex)
        {
            if (boneIndex < 0 || boneIndex >= proxy.bindPoses.Length)
                return Vector3.zero;

            Matrix4x4 bindPose = proxy.bindPoses[boneIndex];
            if (bindPose.determinant != 0)
            {
                Matrix4x4 inverseBindPose = bindPose.inverse;
                return inverseBindPose.GetColumn(3);
            }

            return Vector3.zero;
        }

        private void InitializeShakeBones()
        {
            // Если массив shakeBones не задан, используем все кости
            if (shakeBones == null || shakeBones.Length == 0)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    activeShakeBones[i] = true;
                }
            }
            else
            {
                // Инициализируем все кости как неактивные
                for (int i = 0; i < bones.Length; i++)
                {
                    activeShakeBones[i] = false;
                }

                // Помечаем выбранные кости как активные
                for (int i = 0; i < shakeBones.Length; i++)
                {
                    if (shakeBones[i] != null)
                    {
                        int boneIndex = proxy.GetBoneIndex(shakeBones[i]);
                        if (boneIndex >= 0 && boneIndex < activeShakeBones.Length)
                        {
                            activeShakeBones[boneIndex] = true;
                        }
                    }
                }
            }
        }

        private void CalculateVertexDistances()
        {
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                float maxDistance = 0f;

                int startIndex = proxy.startIndices[vertexIndex];
                int boneCount = proxy.bonesPerVertex[vertexIndex];

                if (boneCount > 0 && startIndex >= 0 && startIndex < proxy.allBoneWeights.Length)
                {
                    for (int i = 0; i < boneCount; i++)
                    {
                        int weightIndex = startIndex + i;

                        if (weightIndex < 0 || weightIndex >= proxy.allBoneWeights.Length)
                            continue;

                        BoneWeight1 bw = proxy.allBoneWeights[weightIndex];

                        if (bw.boneIndex < 0 || bw.boneIndex >= bones.Length)
                            continue;

                        if (bw.weight > 0.01f)
                        {
                            BoneSegment segment = boneSegments[bw.boneIndex];
                            if (segment.isValid)
                            {
                                Vector3 vertexPos = proxy.nativeBaseVertices[vertexIndex];
                                float distance = CalculateDistanceToBoneSegment(vertexPos, bw.boneIndex);
                                maxDistance = Mathf.Max(maxDistance, distance);
                            }
                        }
                    }
                }

                vertexDistancesToBones[vertexIndex] = maxDistance;
            }
        }

        private float CalculateDistanceToBoneSegment(Vector3 vertexPos, int boneIndex)
        {
            BoneSegment segment = boneSegments[boneIndex];
            if (!segment.isValid)
                return 0f;

            Vector3 bonePos = GetBoneBindPosition(boneIndex);
            Vector3 parentPos = GetBoneBindPosition(segment.parentBoneIndex);

            Vector3 lineDir = (bonePos - parentPos).normalized;
            float segmentLength = Vector3.Distance(bonePos, parentPos);

            Vector3 toVertex = vertexPos - parentPos;
            float projection = Vector3.Dot(toVertex, lineDir);
            projection = Mathf.Clamp(projection, 0f, segmentLength);

            Vector3 closestPoint = parentPos + lineDir * projection;

            float perpendicularDistance = Vector3.Distance(vertexPos, closestPoint);

            return perpendicularDistance;
        }

        public void OnInit(ProxyMesh proxyMesh)
        {
            proxy = proxyMesh;
            shakeVelocities = new NativeArray<Vector3>(vertexCount, Allocator.Persistent);
            shakeDisplacements = new NativeArray<Vector3>(vertexCount, Allocator.Persistent);
            vertexDistancesToBones = new NativeArray<float>(vertexCount, Allocator.Persistent);
            activeShakeBones = new NativeArray<bool>(bones.Length, Allocator.Persistent);
            InitializeBoneSegments();
            CalculateVertexDistances();
            InitializeShakeBones();
        }
        public void Dispose() => OnShutdown(proxy);
        public void OnShutdown(ProxyMesh proxyMesh)
        {
            if(shakeVelocities.IsCreated) shakeVelocities.Dispose();
            if(shakeDisplacements.IsCreated) shakeDisplacements.Dispose();
            if(activeShakeBones.IsCreated) activeShakeBones.Dispose();
            if(vertexDistancesToBones.IsCreated) vertexDistancesToBones.Dispose();
            if(boneSegments.IsCreated) boneSegments.Dispose();
        }

        public JobHandle StartJob(JobHandle dependsOn)
        {
            return new VertexShakeJob
            {
                baseVertices = proxy.nativeBaseVertices,
                allBoneWeights = proxy.allBoneWeights,
                bonesPerVertex = proxy.bonesPerVertex,
                startIndices = proxy.startIndices,
                boneMatrices = proxy.boneMatrices,
                boneVelocities = proxy.boneVelocities,
                boneAccelerations = proxy.boneAccelerations,
                boneSegments = boneSegments,
                vertexDistancesToBones = vertexDistancesToBones,
                activeShakeBones = activeShakeBones,
                animatedVertices = proxy.animatedVertices,
                shakeVelocities = shakeVelocities,
                shakeDisplacements = shakeDisplacements,
                shakeIntensity = shakeIntensity,
                shakeDamping = shakeDamping,
                shakeFrequency = shakeFrequency,
                maxLimbLength = maxLimbLength,
                maxShakeVelocity = maxShakeVelocity,
                maxDisplacementFactor = maxDisplacementFactor,
                boneEndFalloff = boneEndFalloff,
                deltaTime = 0.016f,
                worldToLocalMatrix = transform.worldToLocalMatrix
            }.Schedule(vertexCount, Mathf.Max(1, vertexCount / 100), dependsOn);
        }

        public void OnJobComplete()
        {
        }

        public struct BoneSegment
        {
            public int boneIndex;
            public int parentBoneIndex;
            public Vector3 localDirection;
            public float length;
            public bool isValid;
        }
        private struct ShakeForce
        {
            public Vector3 force;
            public float distanceFactor;
        }
        [BurstCompile]
        protected struct VertexShakeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> baseVertices;
            [ReadOnly] public NativeArray<BoneWeight1> allBoneWeights;
            [ReadOnly] public NativeArray<int> bonesPerVertex;
            [ReadOnly] public NativeArray<int> startIndices;
            [ReadOnly] public NativeArray<float4x4> boneMatrices;
            [ReadOnly] public NativeArray<float3> boneVelocities;
            [ReadOnly] public NativeArray<float3> boneAccelerations;
            [ReadOnly] public NativeArray<BoneSegment> boneSegments;
            [ReadOnly] public NativeArray<float> vertexDistancesToBones;
            [ReadOnly] public NativeArray<bool> activeShakeBones;
            [ReadOnly] public Matrix4x4 worldToLocalMatrix;

            [ReadOnly] public float shakeIntensity;
            [ReadOnly] public float shakeDamping;
            [ReadOnly] public float shakeFrequency;
            [ReadOnly] public float maxLimbLength;
            [ReadOnly] public float maxShakeVelocity;
            [ReadOnly] public float maxDisplacementFactor;
            [ReadOnly] public float boneEndFalloff;
            [ReadOnly] public float deltaTime;

            public NativeArray<float3> animatedVertices;
            public NativeArray<Vector3> shakeVelocities;
            public NativeArray<Vector3> shakeDisplacements;

            public void Execute(int vertexIndex)
            {
                if (vertexIndex < 0 || vertexIndex >= baseVertices.Length)
                    return;

                Vector3 baseVertex = baseVertices[vertexIndex];
                Vector3 shakeVelocity = shakeVelocities[vertexIndex];
                Vector3 shakeDisplacement = shakeDisplacements[vertexIndex];

                // Усиленный расчет силы тряски с учетом ускорения
                ShakeForce force = CalculateEnhancedShakeForce(vertexIndex, baseVertex);
                // Применяем физику с усиленными параметрами
                ApplyEnhancedSpringPhysics(ref shakeVelocity, ref shakeDisplacement, force, deltaTime);

                // Обновляем массивы
                shakeVelocities[vertexIndex] = shakeVelocity;
                shakeDisplacements[vertexIndex] = shakeDisplacement;

                // Применяем смещение с увеличенным фактором
                ApplyShakeDisplacement(vertexIndex, shakeDisplacement);
            }

            private ShakeForce CalculateEnhancedShakeForce(int vertexIndex, Vector3 baseVertex)
            {
                float3 velocityForce = float3.zero;
                float3 accelerationForce = float3.zero;
                float totalWeight = 0f;
                float maxDistance = 0f;

                int startIndex = startIndices[vertexIndex];
                int boneCount = bonesPerVertex[vertexIndex];

                if (boneCount > 0 && startIndex >= 0 && startIndex < allBoneWeights.Length)
                {
                    for (int i = 0; i < boneCount; i++)
                    {
                        int weightIndex = startIndex + i;

                        if (weightIndex < 0 || weightIndex >= allBoneWeights.Length)
                            continue;

                        BoneWeight1 bw = allBoneWeights[weightIndex];

                        if (bw.boneIndex < 0 || bw.boneIndex >= boneVelocities.Length)
                            continue;

                        // Проверяем, активна ли кость для тряски
                        if (!activeShakeBones[bw.boneIndex])
                            continue;

                        if (bw.weight > 0.01f)
                        {
                            BoneSegment segment = boneSegments[bw.boneIndex];
                            if (segment.isValid)
                            {
                                float distance = vertexDistancesToBones[vertexIndex];
                                maxDistance = math.max(maxDistance, distance);

                                // Вычисляем фактор затухания у концов кости
                                float endFalloffFactor = CalculateBoneEndFalloff(baseVertex, bw.boneIndex, segment);
                                // Усиленная сила от скорости с учетом затухания
                                velocityForce += boneVelocities[bw.boneIndex] * bw.weight * 1.5f * endFalloffFactor;

                                // Дополнительная сила от ускорения (более резкая тряска)
                                accelerationForce += boneAccelerations[bw.boneIndex] * bw.weight * 0.8f * endFalloffFactor;

                                totalWeight += bw.weight * endFalloffFactor;
                            }
                            else
                            {
                                // Для невалидных сегментов используем полную силу
                                velocityForce += boneVelocities[bw.boneIndex] * bw.weight * 1.5f;
                                accelerationForce += boneAccelerations[bw.boneIndex] * bw.weight * 0.8f;
                                totalWeight += bw.weight;
                            }
                        }
                    }

                    if (totalWeight > 0)
                    {
                        velocityForce /= totalWeight;
                        accelerationForce /= totalWeight;
                    }
                }

                float distanceFactor = math.clamp(maxDistance / maxLimbLength, 0f, 1f);
                float anatomicalFactor = CalculateEnhancedAnatomicalFactor(vertexIndex, distanceFactor);

                // Комбинируем силы скорости и ускорения
                Vector3 totalForce = (velocityForce + accelerationForce) * shakeIntensity * anatomicalFactor;

                return new ShakeForce
                {
                    force = totalForce,
                    distanceFactor = distanceFactor
                };
            }

            /// <summary>
            /// Вычисляет затухание тряски у концов костей
            /// </summary>
            private float CalculateBoneEndFalloff(Vector3 vertexPos, int boneIndex, BoneSegment segment)
            {
                if (!segment.isValid)
                    return 1f;

                // Получаем позиции кости и родителя в bind pose
                Vector3 bonePos = ((Matrix4x4)boneMatrices[boneIndex]).GetColumn(3);
                Vector3 parentPos = ((Matrix4x4)boneMatrices[segment.parentBoneIndex]).GetColumn(3);

                // Вычисляем проекцию вершины на сегмент кости
                Vector3 boneDirection = (bonePos - parentPos).normalized;
                Vector3 toVertex = vertexPos - parentPos;
                float projection = math.dot(toVertex, boneDirection);

                // Нормализуем проекцию относительно длины кости
                float normalizedProjection = math.clamp(projection / segment.length, 0f, 1f);

                // Вычисляем затухание: 1 в середине, меньше у концов
                // Используем функцию, которая дает 1 в середине и уменьшается к концам
                float falloff = 1f - math.pow(math.abs(normalizedProjection - 0.5f) * 2f, 2f) * boneEndFalloff;

                return math.clamp(falloff, 0.1f, 1f);
            }

            private float CalculateEnhancedAnatomicalFactor(int vertexIndex, float distanceFactor)
            {
                float anatomicalFactor = distanceFactor;

                int startIndex = startIndices[vertexIndex];
                int boneCount = bonesPerVertex[vertexIndex];

                if (boneCount > 0 && startIndex >= 0)
                {
                    float segmentLengthFactor = 0f;
                    float velocityMagnitude = 0f;
                    int validSegments = 0;

                    for (int i = 0; i < boneCount; i++)
                    {
                        int weightIndex = startIndex + i;

                        if (weightIndex >= allBoneWeights.Length)
                            continue;

                        BoneWeight1 bw = allBoneWeights[weightIndex];

                        if (bw.boneIndex < 0 || bw.boneIndex >= boneSegments.Length)
                            continue;

                        // Пропускаем неактивные кости
                        if (!activeShakeBones[bw.boneIndex])
                            continue;

                        BoneSegment segment = boneSegments[bw.boneIndex];
                        if (segment.isValid)
                        {
                            segmentLengthFactor += segment.length * bw.weight;
                            velocityMagnitude += math.length(boneVelocities[bw.boneIndex]) * bw.weight;
                            validSegments++;
                        }
                    }

                    if (validSegments > 0)
                    {
                        segmentLengthFactor /= validSegments;
                        velocityMagnitude /= validSegments;

                        float lengthFactor = math.clamp(segmentLengthFactor / maxLimbLength, 0f, 1f);
                        float velocityFactor = math.clamp(velocityMagnitude / 5f, 0f, 2f); // Нормализуем скорость

                        // Усиленный анатомический фактор
                        anatomicalFactor *= (0.5f + 0.3f * lengthFactor + 0.2f * velocityFactor);
                    }
                }

                return math.clamp(anatomicalFactor, 0.1f, 3f); // Минимум 10% эффекта, максимум 300%
            }

            private void ApplyEnhancedSpringPhysics(ref Vector3 velocity, ref Vector3 displacement, ShakeForce force, float dt)
            {
                // Усиленная физика пружины с более выраженным эффектом
                Vector3 acceleration = force.force - displacement * (shakeFrequency * shakeFrequency) - velocity * shakeDamping;

                // Интеграция с улучшенной стабильностью
                velocity += acceleration * dt;

                // Ограничение скорости
                float speed = math.length(velocity);
                if (speed > maxShakeVelocity)
                {
                    velocity = velocity * (maxShakeVelocity / speed);
                }

                // Обновление смещения
                displacement += velocity * dt;

                // Усиленное демпфирование
                velocity *= math.exp(-shakeDamping * dt * 0.3f);
            }

            private void ApplyShakeDisplacement(int vertexIndex, Vector3 displacement)
            {
                Vector3 animatedVertex = animatedVertices[vertexIndex];
                Vector3 worldVertex = worldToLocalMatrix.inverse.MultiplyPoint3x4(animatedVertex);

                // Увеличенное максимальное смещение
                float maxDisplacement = vertexDistancesToBones[vertexIndex] * maxDisplacementFactor;
                float currentDisplacementLength = math.length(displacement);

                if (currentDisplacementLength > maxDisplacement)
                {
                    displacement = displacement * (maxDisplacement / currentDisplacementLength);
                }

                worldVertex += displacement;
                animatedVertices[vertexIndex] = worldToLocalMatrix.MultiplyPoint3x4(worldVertex);
            }

            private struct ShakeForce
            {
                public Vector3 force;
                public float distanceFactor;
            }

            private Vector3 clamp(Vector3 value, float min, float max)
            {
                return new Vector3(
                    math.clamp(value.x, min, max),
                    math.clamp(value.y, min, max),
                    math.clamp(value.z, min, max)
                );
            }
        }
    }
}