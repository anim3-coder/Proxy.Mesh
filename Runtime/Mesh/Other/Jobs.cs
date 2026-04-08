using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Jobs;
using Vertx.Debugging;

namespace Proxy.Mesh
{
    public abstract partial class ProxyMeshAbstract
    {
        [BurstCompile]
        protected struct UpdateBoneMatricesJob : IJobParallelForTransform
        {
            [ReadOnly] public NativeArray<float4x4> bindPoses;
            [ReadOnly] public float4x4 worldToLocalMatrix;
            public NativeArray<float4x4> boneMatrices;
            [WriteOnly, NativeDisableParallelForRestriction] public NativeBitArray changePoses;
            public void Execute(int index, TransformAccess transform)
            {
                if (index >= bindPoses.Length)
                {
                    boneMatrices[index] = Matrix4x4.identity;
                    return;
                }

                float4x4 localMatrix = transform.localToWorldMatrix;
                float4x4 n = math.mul(worldToLocalMatrix, math.mul(localMatrix, bindPoses[index]));
                changePoses.Set(index, !CompareApproximately(boneMatrices[index], n));
                boneMatrices[index] = n;
            }
            public bool CompareApproximately(float4x4 a, float4x4 b)
            {
                bool4 col0 = math.abs(a.c0 - b.c0) < 0.001f;
                bool4 col1 = math.abs(a.c1 - b.c1) < 0.001f;
                bool4 col2 = math.abs(a.c2 - b.c2) < 0.001f;
                bool4 col3 = math.abs(a.c3 - b.c3) < 0.001f;
                return math.all(col0) && math.all(col1) && math.all(col2) && math.all(col3);
            }
        }
        [BurstCompile]
        protected struct DrawChangeBones : IJobParallelForTransform
        {
            [ReadOnly] public NativeBitArray.ReadOnly changePoses;

            public void Execute(int index, TransformAccess transform)
            {
                if(changePoses.IsSet(index)) D.raw(new Shape.Sphere(transform.position, 0.01f), color: Color.red);
            }
        }
        [BurstCompile]
        protected struct RaycastJob : IJobParallelFor
        {
            [ReadOnly] public Matrix4x4 localToWorldMatrix;
            [ReadOnly] public NativeArray<float3> vertices;
            [ReadOnly] public Vector3 origin;
            [ReadOnly] public Vector3 direction;
            [ReadOnly] public NativeArray<int3> triangles;
            [ReadOnly] public float closestHitDistance;
            [WriteOnly] public NativeList<Vector3>.ParallelWriter resultPosition;
            [WriteOnly] public NativeList<Vector3>.ParallelWriter resultNormal;
            public void Execute(int i)
            {
                Vector3 v0 = localToWorldMatrix.MultiplyPoint3x4(vertices[triangles[i].x]);
                Vector3 v1 = localToWorldMatrix.MultiplyPoint3x4(vertices[triangles[i].y]);
                Vector3 v2 = localToWorldMatrix.MultiplyPoint3x4(vertices[triangles[i].z]);

                if (RayIntersectsTriangle(
                    origin,
                    direction.normalized,
                    v0, v1, v2,
                    out float3 hitPointLocal,
                    out float hitDistance))
                {
                    float3 edge1 = v1 - v0;
                    float3 edge2 = v2 - v0;
                    Vector3 closestHitNormal = math.normalize(math.cross(edge1, edge2));
                    if (hitDistance < closestHitDistance && math.dot(direction.normalized, closestHitNormal) < 0)
                    {
                        resultPosition.AddNoResize(hitPointLocal);
                        resultNormal.AddNoResize(closestHitNormal.normalized);
                    }
                }
            }

            private bool RayIntersectsTriangle(
                float3 rayOrigin,
                float3 rayDirection,
                float3 v0, float3 v1, float3 v2,
                out float3 hitPoint,
                out float hitDistance)
            {
                hitPoint = float3.zero;
                hitDistance = 0f;

                const float EPSILON = 0.000001f;

                float3 edge1 = v1 - v0;
                float3 edge2 = v2 - v0;
                float3 h = math.cross(rayDirection, edge2);

                float a = math.dot(edge1, h);

                if (a > -EPSILON && a < EPSILON)
                    return false;

                float f = 1.0f / a;
                float3 s = rayOrigin - v0;
                float u = f * math.dot(s, h);

                if (u < 0.0f || u > 1.0f)
                    return false;

                float3 q = math.cross(s, edge1);
                float v = f * math.dot(rayDirection, q);

                if (v < 0.0f || u + v > 1.0f)
                    return false;

                float t = f * math.dot(edge2, q);

                if (t > EPSILON)
                {
                    hitDistance = t;
                    hitPoint = rayOrigin + rayDirection * t;
                    return true;
                }

                return false;
            }
        }
    }
}