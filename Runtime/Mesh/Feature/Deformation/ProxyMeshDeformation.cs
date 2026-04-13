using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Proxy.Mesh
{
    [RequireComponent(typeof(ProxyMesh))]
    [TriInspector.DeclareFoldoutGroup("Main")]
    [TriInspector.DeclareFoldoutGroup("Extrusion")]
    public class ProxyMeshDeformation : ExternalFeature, IDeformaion
    {
        [TriInspector.ValidateInput(nameof(Validate))]
        [SerializeField, TriInspector.Group("Main")] private ProxyCollider[] colliders;
        [TriInspector.Group("Main")] public bool applyDeformation = true;
        [TriInspector.Group("Main")] public bool calculatePenetration;
        [TriInspector.EnumToggleButtons, TriInspector.Group("Main")] public DeformationType deformationType;
        [TriInspector.Group("Main"), TriInspector.Slider(0.0001f, 1)] public float damping = 1;
        [TriInspector.EnumToggleButtons, TriInspector.Group("Extrusion")] public ExtrusionType extrusionType;
        [TriInspector.EnableIf(nameof(IsExtrusion)), TriInspector.Group("Extrusion")] public float extrusionRadius = 5;
        [TriInspector.EnableIf(nameof(IsExtrusion)), TriInspector.Group("Extrusion")] public float extrusionSmooth = 100;
        
        #region DeformVector
        private NativeArray<float4> _addedDeformation;
        public NativeArray<float4> addedDeformation
        {
            get
            {
                if(_addedDeformation.IsCreated)
                    return _addedDeformation;
                _addedDeformation = new NativeArray<float4>(proxy.vertexCount, Allocator.Persistent);
                return _addedDeformation;
            }
        }
        #endregion
        #region Intersect
        private NativeBitArray _intersects;
        private bool _itersects_empty = true;
        public NativeBitArray intersects
        {
            get
            {
                if(_itersects_empty)
                {
                    _intersects = new NativeBitArray(proxy.skeletonGroups.groupsCount,Allocator.Persistent);
                    _itersects_empty = false;
                }
                return _intersects;
            }
        }
        #endregion
        public bool IsExtrusion
        {
            get => extrusionType != ExtrusionType.None; 
        }

        [TriInspector.Button("Find colliders")]
        public void FindColliders()
        { 
            colliders = FindObjectsByType<ProxyCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }
        
        public TriInspector.TriValidationResult Validate()
        {
            if(TryGetComponent(out ProxyMesh proxy))
            {
                if (proxy.NormalRecalculationEnabled)
                    return TriInspector.TriValidationResult.Valid;
                return TriInspector.TriValidationResult.Error("without updating the normals, this will not work fully.").WithFix(() => proxy.NormalRecalculationEnabled = true);
            }
            return TriInspector.TriValidationResult.Valid;
        }

        public override void OnInit(ProxyMesh proxyMesh)
        {
            base.OnInit(proxyMesh);
        }

        
        public override void OnShutdown(ProxyMesh proxyMesh)
        {
            base.OnShutdown(proxyMesh);
            if (intersects.IsCreated) intersects.Dispose();
            if (_addedDeformation.IsCreated) _addedDeformation.Dispose();
        }

        public override JobHandle StartJob(JobHandle dependsOn)
        {
            if (colliders.Length > 0 && this.enabled)
            {
                if (proxy.GroupsEnabled)
                {
                    for(int i = 0; i < proxy.skeletonGroups.groupsCount; i++)
                    {
                        if (intersects.IsSet(i) == false || proxy.skeletonGroups.roots[i].maxDeformation <= 0)
                        {
                            dependsOn = new WAfterDeformUpdate
                            {
                                damping = damping,
                                maxDeformation = proxy.skeletonGroups.roots[i].maxDeformation,
                                deformVector = addedDeformation,
                                animatedVertices = proxy.animatedVertices,
                                localToWorldMatrix = proxy.localToWorldMatrix,
                                worldToLocalMatrix = proxy.worldToLocalMatrix,
                                applyDeformaion = applyDeformation,
                                indices = proxy.skeletonGroups.indices[i],
                                updateIndices = proxy.normalsRecalculation.updateIndices.AsParallelWriter(),
                                updateIndicesList = proxy.normalsRecalculation.updateIndicesList.AsParallelWriter(),
                            }.Schedule(proxy.skeletonGroups.indices[i].Length, 64, dependsOn);
                        }
                        else
                        {
                            dependsOn = new WDeformJob
                            {
                                localToWorldMatrix = proxy.localToWorldMatrix,
                                worldToLocalMatrix = proxy.worldToLocalMatrix,
                                deformIndices = ProxyManager.ColliderManager.GetIndicesCached(colliders),
                                deformInfos = ProxyManager.ColliderManager.colliderData.AsArray(),
                                maxDeformation = proxy.skeletonGroups.roots[i].maxDeformation,
                                indices = proxy.skeletonGroups.indices[i],
                                extrusionRadius = extrusionRadius,
                                extrusionSmooth = extrusionSmooth,
                                deformationType = deformationType,
                                extrusionType = extrusionType,
                                deformVector = addedDeformation,
                                damping = damping,
                                applyDeformaion = applyDeformation,
                                animatedVertices = proxy.animatedVertices,
                                animatedNormals = proxy.animatedNormals,
                                updateIndices = proxy.normalsRecalculation.updateIndices.AsParallelWriter(),
                                updateIndicesList = proxy.normalsRecalculation.updateIndicesList.AsParallelWriter(),
                            }.Schedule(proxy.skeletonGroups.indices[i].Length, 64, dependsOn);
                        }
                    }
                    dependsOn = new MultiBoundsIntersect
                    {
                        localToWorldMatrix = proxy.localToWorldMatrix,
                        bounds = proxy.skeletonGroups.bounds,
                        deformIndices = ProxyManager.ColliderManager.GetIndicesCached(colliders),
                        deformInfos = ProxyManager.ColliderManager.colliderData.AsArray(),
                        intersects = _intersects,
                    }.Schedule(dependsOn);
                }
                else
                {
                    dependsOn = new BoundsIntersect
                    {
                        localToWorldMatrix = proxy.localToWorldMatrix,
                        deformIndices = ProxyManager.ColliderManager.GetIndicesCached(colliders),
                        boundsMax = proxy.boundsMax,
                        boundsMin = proxy.boundsMin,
                        deformInfos = ProxyManager.ColliderManager.colliderData.AsArray(),
                    }.Schedule(dependsOn);
                    if (AnyValid())
                    {
                        dependsOn = new DeformJob
                        {
                            localToWorldMatrix = proxy.localToWorldMatrix,
                            worldToLocalMatrix = proxy.worldToLocalMatrix,
                            deformIndices = ProxyManager.ColliderManager.GetIndicesCached(colliders),
                            deformInfos = ProxyManager.ColliderManager.colliderData.AsArray(),
                            extrusionRadius = extrusionRadius,
                            extrusionSmooth = extrusionSmooth,
                            deformationType = deformationType,
                            extrusionType = extrusionType,
                            damping = damping,
                            applyDeformaion = applyDeformation,
                            animatedVertices = proxy.animatedVertices,
                            animatedNormals = proxy.animatedNormals,
                            deformVector = addedDeformation,
                            updateIndices = proxy.normalsRecalculation.updateIndices.AsParallelWriter(),
                            updateIndicesList = proxy.normalsRecalculation.updateIndicesList.AsParallelWriter(),
                            maxDeformation = float.MaxValue,
                        }.Schedule(proxy.vertexCount, 64, dependsOn);
                    }
                }
                if(calculatePenetration)
                {
                    dependsOn = new CalculatePenetration() 
                    {
                        deformIndices = ProxyManager.ColliderManager.GetIndicesCached(colliders),
                        deformInfos = ProxyManager.ColliderManager.colliderData.AsArray(),
                        extrusionRadius = extrusionRadius,
                        indices = proxy.normalsRecalculation.updateIndices.AsReadOnly(),
                        vertices = proxy.animatedVertices,
                        normals = proxy.animatedNormals,
                        localToWorldMatrix = proxy.localToWorldMatrix
                    }.Schedule(colliders.Length, 64, dependsOn);
                }
            }
            return dependsOn;
        }

        private bool AnyValid()
        {
            foreach(int index in ProxyManager.ColliderManager.GetIndicesCached(colliders))
            {
                if (ProxyManager.ColliderManager.colliderData[index].IsValid)
                    return true;
            }
            return false;
        }

        [System.Serializable]
        public enum ExtrusionType : byte
        {
            None, ByNormal
        }

        [System.Serializable]
        public enum DeformationType : byte
        {
            Realistic, ByNormal, Hybrid
        }

        [BurstCompile]
        protected struct WAfterDeformUpdate : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> indices;
            [NativeDisableParallelForRestriction] public NativeArray<float4> deformVector;
            [NativeDisableParallelForRestriction] public NativeArray<float3> animatedVertices;
            [WriteOnly] public NativeParallelHashSet<int>.ParallelWriter updateIndices;
            [WriteOnly] public NativeList<int>.ParallelWriter updateIndicesList;
            [ReadOnly] public Matrix4x4 localToWorldMatrix;
            [ReadOnly] public Matrix4x4 worldToLocalMatrix;
            [ReadOnly] public bool applyDeformaion;
            [ReadOnly] public float maxDeformation;
            [ReadOnly] public float damping;
            public void Execute(int index)
            {
                int vertexIndex = indices[index];
                float3 worldVertex = localToWorldMatrix.MultiplyPoint3x4(animatedVertices[vertexIndex]);
                if (math.length(deformVector[vertexIndex].xyz) > 0)
                {
                    if (updateIndices.Add(vertexIndex))
                        updateIndicesList.AddNoResize(vertexIndex);
                    worldVertex += deformVector[vertexIndex].xyz;
                    if (applyDeformaion)
                        animatedVertices[vertexIndex] = worldToLocalMatrix.MultiplyPoint3x4(worldVertex);
                    deformVector[vertexIndex] = new float4(math.lerp(deformVector[vertexIndex], 0, damping).xyz, maxDeformation);
                }
            }
        }
        [BurstCompile]
        protected struct BoundsIntersect : IJob
        {
            [ReadOnly] public Matrix4x4 localToWorldMatrix;
            [ReadOnly] public NativeArray<int> deformIndices;
            public NativeArray<DeformInfo> deformInfos;
            [ReadOnly] public NativeArray<float> boundsMin;
            [ReadOnly] public NativeArray<float> boundsMax;
            public void Execute()
            {
                float3 lmin = localToWorldMatrix.MultiplyPoint3x4(new Vector3(boundsMin[0], boundsMin[1], boundsMin[2]));
                float3 lmax = localToWorldMatrix.MultiplyPoint3x4(new Vector3(boundsMax[0], boundsMax[1], boundsMax[2]));
                
                float3 min = math.min(lmin, lmax);
                float3 max = math.max(lmin, lmax);

                for (int i = 0; i < deformIndices.Length; i++)
                {
                    int dIndex = deformIndices[i];
                    DeformInfo info = deformInfos[dIndex];
                    bool isValid;
                    switch (info.type)
                    {
                        case ColliderType.Sphere:
                            isValid = valid(info.point1, info.radius);
                            if (isValid)
                            {
                                info.IsValid = true;
                            }
                            break;
                        case ColliderType.Capsule:
                            isValid = valid((info.point1 + info.point2) / 2, info.radius + math.length(info.point1 - info.point2));
                            if (isValid)
                            {
                                info.IsValid = true;
                            }
                            break;
                    }
                    deformInfos[dIndex] = info;
                }
                bool valid(float3 point, float radius)
                {
                    float3 closestPoint = math.clamp(point, min, max);
                    float distance = math.distance(point, closestPoint);
                    return distance <= radius;
                }
            }
        }
        [BurstCompile]
        protected struct MultiBoundsIntersect : IJob
        {
            [ReadOnly] public Matrix4x4 localToWorldMatrix;
            [ReadOnly] public NativeArray<int> deformIndices;
            public NativeArray<DeformInfo> deformInfos;
            [ReadOnly] public NativeArray<Bounds> bounds;
            [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction]
            public NativeBitArray intersects;
            public void Execute()
            {
                for(int i = 0; i < deformIndices.Length;i++)
                {
                    int dIndex = deformIndices[i];
                    DeformInfo info = deformInfos[dIndex];
                    info.IsValid = false;
                    deformInfos[dIndex] = info;
                }

                for (int w = 0; w < bounds.Length; w++)
                {
                    float3 lmin = localToWorldMatrix.MultiplyPoint3x4(bounds[w].min);
                    float3 lmax = localToWorldMatrix.MultiplyPoint3x4(bounds[w].max);

                    float3 min = math.min(lmin, lmax);
                    float3 max = math.max(lmin, lmax);

                    bool intersect = false;
                    for (int i = 0; i < deformIndices.Length; i++)
                    {
                        int dIndex = deformIndices[i];
                        DeformInfo info = deformInfos[dIndex];
                        bool isValid;
                        switch (info.type)
                        {
                            case ColliderType.Sphere:
                                isValid = valid(info.point1, info.radius);
                                if (isValid)
                                {
                                    info.IsValid = true;
                                    intersect = true;
                                }
                                break;
                            case ColliderType.Capsule:
                                isValid = valid((info.point1 + info.point2) / 2, info.radius + math.length(info.point1 - info.point2));
                                if (isValid)
                                {
                                    info.IsValid = true;
                                    intersect = true;
                                }
                                break;
                        }
                        deformInfos[dIndex] = info;
                    }
                    intersects.Set(w, intersect);

                    var b = new Bounds();
                    b.SetMinMax(min, max);

                    bool valid(float3 point, float radius)
                    {
                        float3 closestPoint = math.clamp(point, min, max);
                        float distance = math.distance(point, closestPoint);
                        return distance <= radius;
                    }
                }
            }
        }
        [BurstCompile]
        protected struct DeformJob : IJobParallelFor
        {
            [ReadOnly] public ExtrusionType extrusionType;
            [ReadOnly] public DeformationType deformationType;
            [ReadOnly] public bool applyDeformaion;
            [ReadOnly] public float extrusionRadius;
            [ReadOnly] public float extrusionSmooth;
            [ReadOnly] public float maxDeformation;
            [ReadOnly] public float damping;
            [ReadOnly] public Matrix4x4 localToWorldMatrix;
            [ReadOnly] public Matrix4x4 worldToLocalMatrix;
            [ReadOnly] public NativeArray<int> deformIndices;
            [NativeDisableParallelForRestriction] public NativeArray<DeformInfo> deformInfos;
            [NativeDisableParallelForRestriction] public NativeArray<float3> animatedVertices;
            [NativeDisableParallelForRestriction] public NativeArray<float4> deformVector;
            [ReadOnly] public NativeArray<float3> animatedNormals;
            [WriteOnly] public NativeParallelHashSet<int>.ParallelWriter updateIndices;
            [WriteOnly] public NativeList<int>.ParallelWriter updateIndicesList;
            public void Execute(int vertexIndex)
            {
                if (vertexIndex < 0 || vertexIndex >= animatedVertices.Length)
                    return;

                float3 worldVertex = localToWorldMatrix.MultiplyPoint3x4(animatedVertices[vertexIndex]);
                float3 worldNormal = localToWorldMatrix.MultiplyVector(animatedNormals[vertexIndex]).normalized;

                float3 totalDisplacement = float3.zero;
                float3 dir = float3.zero;

                for (int i = 0; i < deformIndices.Length; i++)
                {
                    int dIndex = deformIndices[i];
                    if (deformInfos[dIndex].IsValid == false)
                        continue;
                    DeformInfo info = deformInfos[dIndex];
                    totalDisplacement += GetForceAtPoint(deformInfos[dIndex], worldVertex, worldNormal);
                    info.IsUpdate = true;
                    deformInfos[dIndex] = info;
                }

                if (math.length(totalDisplacement) > math.length(deformVector[vertexIndex].xyz))
                {
                    if(maxDeformation > 0)
                        if (updateIndices.Add(vertexIndex))
                            updateIndicesList.AddNoResize(vertexIndex);
                    deformVector[vertexIndex] = new float4(math.min(math.length(totalDisplacement), maxDeformation) * math.normalize(totalDisplacement), maxDeformation);
                    worldVertex += deformVector[vertexIndex].xyz;
                    if(applyDeformaion)
                        animatedVertices[vertexIndex] = worldToLocalMatrix.MultiplyPoint3x4(worldVertex);
                }
                else if (math.length(deformVector[vertexIndex].xyz) > 0)
                {
                    if (updateIndices.Add(vertexIndex))
                        updateIndicesList.AddNoResize(vertexIndex);
                    worldVertex += deformVector[vertexIndex].xyz;
                    if (applyDeformaion)
                        animatedVertices[vertexIndex] = worldToLocalMatrix.MultiplyPoint3x4(worldVertex);
                    deformVector[vertexIndex] = new float4(math.lerp(deformVector[vertexIndex], 0, damping).xyz, maxDeformation);
                }
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
        protected struct WDeformJob : IJobParallelFor
        {
            [ReadOnly] public ExtrusionType extrusionType;
            [ReadOnly] public DeformationType deformationType;
            [ReadOnly] public float extrusionRadius;
            [ReadOnly] public float extrusionSmooth;
            [ReadOnly] public float damping;
            [ReadOnly] public bool applyDeformaion;
            [ReadOnly] public float maxDeformation;
            [ReadOnly] public Matrix4x4 localToWorldMatrix;
            [ReadOnly] public Matrix4x4 worldToLocalMatrix;
            [ReadOnly] public NativeArray<int> deformIndices;
            [NativeDisableParallelForRestriction] public NativeArray<DeformInfo> deformInfos;
            [NativeDisableParallelForRestriction] public NativeArray<float3> animatedVertices;
            [NativeDisableParallelForRestriction] public NativeArray<float4> deformVector;
            [ReadOnly] public NativeArray<float3> animatedNormals;
            [ReadOnly] public NativeArray<int> indices;
            [WriteOnly] public NativeParallelHashSet<int>.ParallelWriter updateIndices;
            [WriteOnly] public NativeList<int>.ParallelWriter updateIndicesList;
            public void Execute(int index)
            {
                int vertexIndex = indices[index];
                if (vertexIndex < 0 || vertexIndex >= animatedVertices.Length)
                    return;

                // Перевод в мировые координаты
                float3 worldVertex = localToWorldMatrix.MultiplyPoint3x4(animatedVertices[vertexIndex]);
                float3 worldNormal = localToWorldMatrix.MultiplyVector(animatedNormals[vertexIndex]).normalized;

                float3 totalDisplacement = float3.zero;
                float3 dir = float3.zero;

                for (int i = 0; i < deformIndices.Length; i++)
                {
                    int dIndex = deformIndices[i];
                    if (deformInfos[dIndex].IsValid == false)
                        continue;
                    DeformInfo info = deformInfos[dIndex];
                    totalDisplacement += GetForceAtPoint(deformInfos[dIndex], worldVertex, worldNormal);
                    info.IsUpdate = true;
                    deformInfos[dIndex] = info;
                }

                if (math.length(totalDisplacement) > math.length(deformVector[vertexIndex].xyz))
                {
                    if (maxDeformation > 0)
                        if (updateIndices.Add(vertexIndex))
                            updateIndicesList.AddNoResize(vertexIndex);
                    deformVector[vertexIndex] = new float4(math.min(math.length(totalDisplacement), maxDeformation) * math.normalize(totalDisplacement), maxDeformation);
                    worldVertex += deformVector[vertexIndex].xyz;
                    if (applyDeformaion)
                        animatedVertices[vertexIndex] = worldToLocalMatrix.MultiplyPoint3x4(worldVertex);
                }
                else if (math.length(deformVector[vertexIndex].xyz) > 0)
                {
                    if (updateIndices.Add(vertexIndex))
                        updateIndicesList.AddNoResize(vertexIndex);
                    worldVertex += deformVector[vertexIndex].xyz;
                    if (applyDeformaion)
                        animatedVertices[vertexIndex] = worldToLocalMatrix.MultiplyPoint3x4(worldVertex);
                    deformVector[vertexIndex] = new float4(math.lerp(deformVector[vertexIndex], 0, damping).xyz, maxDeformation);
                }
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
        protected struct CalculatePenetration : IJobParallelFor
        {
            [ReadOnly] public float extrusionRadius;
            [ReadOnly] public NativeArray<int> deformIndices;
            public NativeArray<DeformInfo> deformInfos;
            [ReadOnly] public NativeArray<float3> vertices;
            [ReadOnly] public NativeArray<float3> normals;
            [ReadOnly] public NativeParallelHashSet<int>.ReadOnly indices;
            [ReadOnly] public Matrix4x4 localToWorldMatrix;
            public void Execute(int index)
            {
                index = deformIndices[index];
                DeformInfo info = deformInfos[index];
                int count = 0;
                foreach(int i in indices)
                {
                    float3 penetration = GetPenetration(info, localToWorldMatrix.MultiplyPoint3x4(vertices[i]), localToWorldMatrix.MultiplyVector(normals[i]));
                    if(math.length(penetration) > 0)
                        count++;
                    info.outPenetration += penetration;
                }
                if(count > 0)
                    info.outPenetration = info.outPenetration / count;
                deformInfos[index] = info;
            }

            private float3 GetPenetration(DeformInfo info, float3 point, float3 normal)    
            {
                switch (info.type)
                {
                    default:
                    case Proxy.ColliderType.Sphere:
                        return GetPenetrationAtSphere(info.point1, info.radius, point, normal);

                    case Proxy.ColliderType.Capsule:
                        float3 capsuleVector = info.point2 - info.point1;
                        float capsuleLengthSqr = math.lengthsq(capsuleVector);

                        float t = math.dot(point - info.point1, capsuleVector) / capsuleLengthSqr;
                        t = math.clamp(t, 0, 1);

                        float3 closestPoint = info.point1 + t * capsuleVector;
                        return GetPenetrationAtSphere(closestPoint, info.radius, point, normal);
                }
            }
            private float3 GetPenetrationAtSphere(float3 center, float radius, float3 point, float3 normal)
            {
                float distance = math.distance(point, center);
                float3 direction = math.normalize(point - center);
                if (distance <= radius)
                {    
                    float penetration = radius - distance;
                    return (math.dot(direction, normal) < 0 ? -1 : 1) * direction * penetration;
                }
                else if (math.dot(direction, normal) > 0)
                {
                    radius = radius * 1.5f;
                    float penetration = radius - distance;
                    if (distance < radius)
                    {
                        return direction * penetration;
                    }
                }
                return float3.zero;
            }
        }
}
    [BurstCompile]
    public struct DeformInfo
    {
        public Proxy.ColliderType type;
        public float3 point1;
        public float3 point2;
        public float3 outPenetration;
        public float radius;
        public bool IsValid;
        public bool IsUpdate;
    }

    public interface IDeformaion
    {
        /// <summary>
        /// xyz - DeformVector | w - maxDeformation
        /// </summary>
        public NativeArray<float4> addedDeformation { get; }
    }
}