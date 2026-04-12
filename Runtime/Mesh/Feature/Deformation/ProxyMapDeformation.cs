using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Proxy.Mesh.ProxyMeshDeformation;

namespace Proxy.Mesh
{
    [TriInspector.DeclareFoldoutGroup("Main")]
    [TriInspector.DeclareFoldoutGroup("Extrusion")]
    public class ProxyMapDeformation : ExternalFeature, IDeformationMaps
    {
        [SerializeField] private ProxyMapDeformationInternal[] deformations;

        public IDeformationMap[] deformationMaps => deformations;

        private void Reset()
        {
            deformations = new ProxyMapDeformationInternal[proxy.materials.Length];
        }

        public override void OnInit(ProxyMesh proxyMesh)
        {
            base.OnInit(proxyMesh);

            for (int i = 0; i < deformations.Length; i++)
                deformations[i].OnInit(proxy);
        }

        public override void OnJobComplete()
        {
            base.OnJobComplete();

            for (int i = 0; i < deformations.Length; i++)
                deformations[i].OnJobComplete();
        }

        public override void OnShutdown(ProxyMesh proxyMesh)
        {
            base.OnShutdown(proxy);

            for (int i = 0; i < deformations.Length; i++)
                deformations[i].OnShutdown(proxy);
        }

        public override JobHandle StartJob(JobHandle dependsOn)
        {
            for (int i = 0; i < deformations.Length; i++)
                dependsOn = deformations[i].StartJob(dependsOn);
            return dependsOn;
        }

        [System.Serializable]
        [TriInspector.DeclareFoldoutGroup("Main")]
        [TriInspector.DeclareFoldoutGroup("Extrusion")]
        public class ProxyMapDeformationInternal : MapExport, IDeformationMap
        {
            [SerializeField, TriInspector.Group("Main"), TriInspector.Required(FixAction = nameof(FixTarget), FixActionName = "Fix")] private ProxyCollider[] colliders;
            [TriInspector.EnumToggleButtons, TriInspector.Group("Main")] public DeformationType deformationType;
            [TriInspector.Group("Main"), TriInspector.Slider(0.0001f, 1)] public float damping = 1;
            [TriInspector.EnumToggleButtons, TriInspector.Group("Extrusion")] public ExtrusionType extrusionType;
            [TriInspector.EnableIf(nameof(IsExtrusion)), TriInspector.Group("Extrusion")] public float extrusionRadius = 5;
            [TriInspector.EnableIf(nameof(IsExtrusion)), TriInspector.Group("Extrusion")] public float extrusionSmooth = 100;
            [TriInspector.ShowInInspector, TriInspector.ReadOnly] public int activeCount { get; protected set; }
            public NativeArray<float4> deformation { get; protected set; }

            private void FixTarget()
            {
                for (int i = 0; i < colliders.Length; i++)
                    colliders[i] = colliders[i].gameObject.GetComponent<ProxyCollider>();
            }
            public bool IsExtrusion => extrusionType != ExtrusionType.None;

            public override void OnInit(ProxyMeshAbstract proxyMesh)
            {
                base.OnInit(proxyMesh);

                deformation = new NativeArray<float4>(totalPixels, Allocator.Persistent);
            }

            public override void OnShutdown(ProxyMeshAbstract proxyMesh)
            {
                base.OnShutdown(proxyMesh);

                if (deformation.IsCreated) deformation.Dispose();
            }

            public override JobHandle StartJob(JobHandle dependsOn)
            {
                if (IsInit == false)
                    return dependsOn;
                dependsOn = new RasterizeTrianglesJob
                {
                    triangles = triangles,
                    uvs = proxy.nativeUV,
                    vertices = proxy.animatedVertices,
                    normals = proxy.animatedNormals,
                    textureSize = textureSize,
                    deformation = deformation,
                    localToWorldMatrix = proxy.localToWorldMatrix,
                    worldToLocalMatrix = proxy.worldToLocalMatrix,
                    deformIndices = ProxyManager.ColliderManager.GetIndicesCached(colliders),
                    deformInfos = ProxyManager.ColliderManager.colliderData.AsArray(),
                    extrusionRadius = extrusionRadius,
                    extrusionSmooth = extrusionSmooth,
                    deformationType = deformationType,
                    extrusionType = extrusionType,
                    damping = damping,
                    maxDeformation = maxDeformation,

                }.Schedule(triangles.Length, 32, dependsOn);

                dependsOn = new FinalizeTextureJob
                {
                    deformation = deformation,
                    outputColors = rawTexture,
                    maxDeformation = maxDeformation
                }.Schedule(totalPixels, 64, dependsOn);
                return dependsOn;
            }

            [BurstCompile]
            protected struct RasterizeTrianglesJob : IJobParallelFor
            {
                [ReadOnly] public ExtrusionType extrusionType;
                [ReadOnly] public DeformationType deformationType;
                [ReadOnly] public float extrusionRadius;
                [ReadOnly] public float extrusionSmooth;
                [ReadOnly] public float damping;
                [ReadOnly] public float maxDeformation;
                [ReadOnly] public NativeArray<int3> triangles;
                [ReadOnly] public NativeArray<float2> uvs;
                [ReadOnly] public NativeArray<float3> vertices;
                [ReadOnly] public NativeArray<float3> normals;
                [ReadOnly] public NativeArray<int> deformIndices;
                [ReadOnly] public NativeArray<DeformInfo> deformInfos;
                [ReadOnly] public Matrix4x4 localToWorldMatrix;
                [ReadOnly] public Matrix4x4 worldToLocalMatrix;
                [ReadOnly] public int textureSize;
                [NativeDisableParallelForRestriction] public NativeArray<float4> deformation;

                public void Execute(int t)
                {
                    float texSizeInv = 1.0f / textureSize;

                    int i0 = triangles[t].x;
                    int i1 = triangles[t].y;
                    int i2 = triangles[t].z;

                    float2 uv0 = uvs[i0];
                    float2 uv1 = uvs[i1];
                    float2 uv2 = uvs[i2];

                    float3 d0 = localToWorldMatrix.MultiplyPoint(vertices[i0]);
                    float3 d1 = localToWorldMatrix.MultiplyPoint(vertices[i1]);
                    float3 d2 = localToWorldMatrix.MultiplyPoint(vertices[i2]);

                    float3 n0 = localToWorldMatrix.MultiplyVector(normals[i0]);
                    float3 n1 = localToWorldMatrix.MultiplyVector(normals[i1]);
                    float3 n2 = localToWorldMatrix.MultiplyVector(normals[i2]);

                    // Bounding box в UV (0..1)
                    float minU = math.min(uv0.x, math.min(uv1.x, uv2.x));
                    float maxU = math.max(uv0.x, math.max(uv1.x, uv2.x));
                    float minV = math.min(uv0.y, math.min(uv1.y, uv2.y));
                    float maxV = math.max(uv0.y, math.max(uv1.y, uv2.y));

                    // Преобразуем в координаты пикселей (с запасом +1)
                    int px0 = math.clamp((int)(minU * textureSize), 0, textureSize - 1);
                    int px1 = math.clamp((int)(maxU * textureSize) + 1, 0, textureSize - 1);
                    int py0 = math.clamp((int)(minV * textureSize), 0, textureSize - 1);
                    int py1 = math.clamp((int)(maxV * textureSize) + 1, 0, textureSize - 1);

                    // Предвычисляем барицентрические константы
                    float2 v0 = uv1 - uv0;
                    float2 v1 = uv2 - uv0;
                    float dot00 = math.dot(v0, v0);
                    float dot01 = math.dot(v0, v1);
                    float dot11 = math.dot(v1, v1);
                    float invDenom = 1.0f / (dot00 * dot11 - dot01 * dot01);

                    for (int py = py0; py <= py1; py++)
                    {
                        float v = (py + 0.5f) * texSizeInv; // центр пикселя в UV
                        for (int px = px0; px <= px1; px++)
                        {
                            float u = (px + 0.5f) * texSizeInv;
                            float2 p = new float2(u, v);

                            // Барицентрические координаты
                            float2 v2 = p - uv0;
                            float dot02 = math.dot(v0, v2);
                            float dot12 = math.dot(v1, v2);
                            float baryU = (dot11 * dot02 - dot01 * dot12) * invDenom;
                            float baryV = (dot00 * dot12 - dot01 * dot02) * invDenom;
                            float baryW = 1.0f - baryU - baryV;

                            if (baryU >= -1e-5f && baryV >= -1e-5f && baryW >= -1e-5f)
                            {
                                float3 point = baryU * d1 + baryV * d2 + baryW * d0;
                                float3 normal = baryU * n1 + baryV * n2 + baryW * n0;
                                int idx = py * textureSize + px;
                                deformation[idx] = GetDeformation(point, math.normalizesafe(normal), deformation[idx]);
                            }
                        }
                    }
                }

                public float4 GetDeformation(float3 point, float3 normal, float4 deformation)
                {
                    float3 totalDisplacement = float3.zero;
                    float3 dir = float3.zero;

                    for (int i = 0; i < deformIndices.Length; i++)
                    {
                        DeformInfo info = deformInfos[deformIndices[i]];
                        totalDisplacement += GetForceAtPoint(info, point, normal);
                    }

                    if (math.length(totalDisplacement) > math.length(deformation.xyz))
                    {
                        return new float4(math.min(math.length(totalDisplacement), maxDeformation) * math.normalize(totalDisplacement), maxDeformation);
                    }
                    else if (math.length(deformation.xyz) > 0)
                    {
                        return new float4(math.lerp(deformation.xyz, 0, damping), maxDeformation);
                    }
                    return 0;
                }
                private float3 GetForceAtPoint(DeformInfo info, float3 point, float3 normal)
                {
                    switch (info.type)
                    {
                        default:
                        case Proxy.ColliderType.Sphere:
                            return GetForceAtSphere(info.point1, info.radius, point, normal);

                        case Proxy.ColliderType.Capsule:
                            float3 capsuleVector = info.point2 - info.point1;
                            float capsuleLengthSqr = math.lengthsq(capsuleVector);

                            float t = math.dot(point - info.point1, capsuleVector) / capsuleLengthSqr;
                            t = math.clamp(t, 0, 1);

                            float3 closestPoint = info.point1 + t * capsuleVector;
                            return GetForceAtSphere(closestPoint, info.radius, point, normal);
                    }
                }
                private float3 GetForceAtSphere(float3 center, float radius, float3 point, float3 normal)
                {
                    float distance = math.distance(point, center);
                    if (distance <= radius)
                    {
                        float3 direction = math.normalize(point - center);
                        float penetration;
                        switch (deformationType)
                        {
                            case DeformationType.Realistic:
                                penetration = radius - distance;
                                float dot = math.dot(direction, normal);
                                return (dot < 0 ? direction * penetration : float3.zero);
                            case DeformationType.ByNormal:
                                penetration = Math.DistanceToEdge(center, radius, point, -normal);
                                return -normal * penetration;
                            case DeformationType.Hybrid:
                                dot = math.dot(direction, normal);
                                if (dot < 0)
                                {
                                    penetration = radius - distance;
                                    return direction * penetration;
                                }
                                else
                                {
                                    penetration = Math.DistanceToEdge(center, radius, point, -normal);
                                    return -normal * penetration;
                                }
                        }
                    }
                    else if (extrusionType != ExtrusionType.None)
                    {
                        float disRad = radius * extrusionRadius;
                        if (distance > radius && distance < disRad)
                        {
                            float penetration = (disRad - distance) / extrusionSmooth;
                            return normal * penetration;
                        }
                    }
                    return float3.zero;
                }

            }

            [BurstCompile]
            protected struct FinalizeTextureJob : IJobParallelFor
            {
                [ReadOnly] public NativeArray<float4> deformation;
                [WriteOnly] public NativeArray<Color> outputColors;
                [ReadOnly] public float maxDeformation;

                public void Execute(int i)
                {
                    float3 packed = deformation[i].xyz * 0.5f + 0.5f;
                    outputColors[i] = new Color(packed.x, packed.y, packed.z, 1f);
                }
            }
        }

    }

    public interface IDeformationMaps
    {
        public Transform transform { get; }
        public ProxyMesh proxy { get; }
        public IDeformationMap[] deformationMaps { get; }
    }
    public interface IDeformationMap
    {
        public bool IsInit { get; }
        /// <summary>
        /// xyz - DeformVector | w - max deformation
        /// </summary>
        public NativeArray<float4> deformation { get; }
        public NativeArray<int3> triangles { get; }
        /// <summary>
        /// Максимальная деформация возможная на карте.
        /// </summary>
        public float maxDeformation { get; }
        public int textureSize { get; }
        public Material material { get; }
        public NativeArray<Color> rawTexture { get; }
    }
}