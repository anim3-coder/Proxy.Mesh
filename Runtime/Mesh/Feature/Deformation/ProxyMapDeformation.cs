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
    public class ProxyMapDeformation : ExternalFeature, IDeformationMap
    {
        [SerializeField, TriInspector.Group("Main")] private int textureSize = 64;
        [SerializeField, TriInspector.Group("Main")] private float maxDeformation = 0.5f;
        [SerializeField, TriInspector.Group("Main"), TriInspector.Required(FixAction = nameof(FixTarget), FixActionName = "Fix")] private Collider[] colliders;
        [SerializeField, TriInspector.Group("Main")] private string texturePropertyName = "_VectorDisplacementMap";
        [TriInspector.EnumToggleButtons, TriInspector.Group("Main")] public DeformationType deformationType;
        [TriInspector.Group("Main"), TriInspector.Slider(0.0001f, 1)] public float damping = 1;
        [TriInspector.EnumToggleButtons, TriInspector.Group("Extrusion")] public ExtrusionType extrusionType;
        [TriInspector.EnableIf(nameof(IsExtrusion)), TriInspector.Group("Extrusion")] public float extrusionRadius = 5;
        [TriInspector.EnableIf(nameof(IsExtrusion)), TriInspector.Group("Extrusion")] public float extrusionSmooth = 100;
        [TriInspector.ShowInInspector, TriInspector.ReadOnly] public int activeCount { get; protected set; }
        [SerializeField] private Texture2D[] deformTextures;
        public bool IsExtrusion => extrusionType != ExtrusionType.None;
        public int totalPixels => textureSize * textureSize;

        private NativeArray<float3>[] deformation;
        private NativeArray<int>[] count;
        private NativeArray<Color>[] colorData;

        public NativeArray<Color>[] maps => colorData;

        private NativeArray<DeformInfo> deforms;
        public override void OnInit(ProxyMesh proxyMesh)
        {
            base.OnInit(proxyMesh);

            deformation = new NativeArray<float3>[proxy.subMeshCount];
            count = new NativeArray<int>[proxy.subMeshCount];
            colorData = new NativeArray<Color>[proxy.subMeshCount];
            deformTextures = new Texture2D[proxy.subMeshCount];

            for (int i = 0; i < proxy.subMeshCount; i++)
            {
                deformation[i] = new NativeArray<float3>(totalPixels, Allocator.Persistent);
                count[i] = new NativeArray<int>(totalPixels, Allocator.Persistent);
                colorData[i] = new NativeArray<Color>(totalPixels, Allocator.Persistent);
                deformTextures[i] = CreateEmptyTexture(deformTextures[i]);
            }

            deforms = new NativeArray<DeformInfo>(colliders.Length, Allocator.Persistent);
        }

        private void FixTarget()
        {
            for(int i = 0; i < colliders.Length; i++)
                colliders[i] = colliders[i].gameObject.GetComponent<Collider>();
        }

        private Texture2D CreateEmptyTexture(Texture2D texture)
        {
            if (texture != null) Destroy(texture);
            texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBAFloat, false, true);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            // Заполняем нейтральным цветом (0.5 → смещение 0)
            var empty = new NativeArray<Color>(totalPixels, Allocator.Temp);
            for (int i = 0; i < empty.Length; i++)
                empty[i] = new Color(0.5f, 0.5f, 0.5f, 1f);
            texture.SetPixelData(empty, 0);
            texture.Apply();
            empty.Dispose();
            return texture;
        }

        public override void OnJobComplete()
        {
            int a = 0;
            for (int i = 0; i < deforms.Length; i++)
            {
                if (deforms[i].IsUpdate)
                    colliders[i].outPenetration = deforms[i].outPenetration;
                if (deforms[i].IsValid)
                    a++;
                var info = colliders[i].GetDeformInfo();
                info.IsValid = deforms[i].IsValid;
                deforms[i] = info;
            }
            activeCount = a;

            for(int i = 0; i < proxy.subMeshCount; i++)
            {
                UpdateTexture(proxy.materials[i], deformTextures[i], colorData[i]);
            }

            void UpdateTexture(Material mat, Texture2D texture, NativeArray<Color> colorData)
            {
                if (texture == null || texture.width != textureSize || texture.height != textureSize)
                {
                    OnShutdown(proxy);
                    OnInit(proxy);
                }
                else
                {
                    texture.SetPixelData(colorData, 0);
                    texture.Apply();

                    if (mat != null && mat.HasProperty(texturePropertyName))
                        mat.SetTexture(texturePropertyName, texture);
                }
            }
        }

        public override void OnShutdown(ProxyMesh proxyMesh)
        {
            base.OnShutdown(proxyMesh);

            for (int i = 0; i < proxy.subMeshCount; i++)
            {
                deformation[i].Dispose();
                count[i].Dispose();
                colorData[i].Dispose();
            }
            if (deforms.IsCreated) deforms.Dispose();
        }

        public override JobHandle StartJob(JobHandle dependsOn)
        {
            if (colliders.Length > 0 && this.enabled)
            {
                for (int i = 0; i < proxy.subMeshCount; i++)
                {
                    dependsOn = StartJob(dependsOn, colorData[i], count[i], deformation[i], proxy.subMeshTriangles[i]);
                }
            }
            return dependsOn;
        }

        protected JobHandle StartJob(JobHandle dependsOn,
                                  NativeArray<Color> colorData,
                                  NativeArray<int> count,
                                  NativeArray<float3> deformation,
                                  NativeArray<int> triangles)
        {
            dependsOn = new ClearArraysJob
            {
                colorData = colorData,
                count = count,
            }.Schedule(totalPixels, 64, dependsOn);

            int t = triangles.Length / 3;
            dependsOn = new RasterizeTrianglesJob
            {
                triangles = triangles,
                uvs = proxy.nativeUV,
                vertices = proxy.animatedVertices,
                normals = proxy.animatedNormals,
                textureSize = textureSize,
                deformation = deformation,
                count = count,
                localToWorldMatrix = proxy.transform.localToWorldMatrix,
                worldToLocalMatrix = proxy.transform.worldToLocalMatrix,
                deformInfos = deforms,
                extrusionRadius = extrusionRadius,
                extrusionSmooth = extrusionSmooth,
                deformationType = deformationType,
                extrusionType = extrusionType,
                damping = damping,
                maxDeformation = float.MaxValue,

            }.Schedule(t, 32, dependsOn);

            


            dependsOn = new FinalizeTextureJob
            {
                deformation = deformation,
                count = count,
                outputColors = colorData,
                maxDeformation = maxDeformation
            }.Schedule(totalPixels, 64, dependsOn);

            return dependsOn;
        }

        [BurstCompile]
        protected struct ClearArraysJob : IJobParallelFor
        {
            public NativeArray<int> count;
            public NativeArray<Color> colorData;
            public void Execute(int i)
            {
                count[i] = 0;
                colorData[i] = Color.clear;
            }
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
            [ReadOnly] public NativeArray<int> triangles;
            [ReadOnly] public NativeArray<float2> uvs;
            [ReadOnly] public NativeArray<float3> vertices;
            [ReadOnly] public NativeArray<float3> normals;
            [ReadOnly] public NativeArray<DeformInfo> deformInfos;
            [ReadOnly] public Matrix4x4 localToWorldMatrix;
            [ReadOnly] public Matrix4x4 worldToLocalMatrix;
            [ReadOnly] public int textureSize;
            [NativeDisableParallelForRestriction] public NativeArray<float3> deformation;
            [NativeDisableParallelForRestriction] public NativeArray<int> count;

            public void Execute(int t)
            {
                float texSizeInv = 1.0f / textureSize;

                int i0 = triangles[t * 3];
                int i1 = triangles[t * 3 + 1];
                int i2 = triangles[t * 3 + 2];

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
                            count[idx]++;
                        }
                    }
                }
            }

            public float3 GetDeformation(float3 point, float3 normal, float3 deformation)
            {
                float3 totalDisplacement = float3.zero;
                float3 dir = float3.zero;

                for (int i = 0; i < deformInfos.Length; i++)
                {
                    DeformInfo info = deformInfos[i];
                    totalDisplacement += GetForceAtPoint(deformInfos[i], point, normal);
                }

                if (math.length(totalDisplacement) > math.length(deformation))
                {
                    return math.min(math.length(totalDisplacement), maxDeformation) * math.normalize(totalDisplacement);
                }
                else if (math.length(deformation) > 0)
                {
                    return math.lerp(deformation, 0, damping);
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
            [ReadOnly] public NativeArray<float3> deformation;
            [ReadOnly] public NativeArray<int> count;
            [WriteOnly] public NativeArray<Color> outputColors;
            [ReadOnly] public float maxDeformation;

            public void Execute(int i)
            {
                float3 avg = count[i] > 0 ? deformation[i] / count[i] : float3.zero;
                avg /= maxDeformation;
                float3 packed = avg * 0.5f + 0.5f;
                outputColors[i] = new Color(packed.x, packed.y, packed.z, 1f);
            }
        }
    }

    public interface IDeformationMap
    {
        public NativeArray<Color>[] maps { get; }
    }
}