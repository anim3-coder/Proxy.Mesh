using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using UnityEngine;
using Vertx.Debugging;
using Proxy.Mesh.Normals;
using Unity.VisualScripting.YamlDotNet.Core.Tokens;

namespace Proxy.Mesh
{
    [System.Serializable]
    public class NormalsRecalculation : NormalsRecalculationBase
    {
        [SerializeField] private bool UseDeformationVector;
        [SerializeField, HideInInspector] private NormalsRecalculationMethod _recalculationMethod;
        [TriInspector.ShowInInspector] public NormalsRecalculationMethod recalculationMethod 
        { 
            get
            {
                return _recalculationMethod;
            }
            private set
            {
                if (value == _recalculationMethod)
                {
                    return;
                }
                if (Application.isPlaying)
                {
                    recalculation.OnShutdown(proxy);
                }
                recalculation = GetRecalculation(value);
                _recalculationMethod = value;
                if (Application.isPlaying)
                {
                    recalculation.OnInit(proxy);
                }
            }
        }
        [SerializeField, HideInInspector] private NormalsSmoothingMethod _smoothingMethod;
        [TriInspector.ShowInInspector]
        public NormalsSmoothingMethod smoothingMethod
        {
            get
            {
                return _smoothingMethod;
            }
            private set
            {
                if (value == _smoothingMethod)
                {
                    return;
                }
                if (Application.isPlaying)
                {
                    smoothing.OnShutdown(proxy);
                }
                smoothing = GetSmoothing(value);
                _smoothingMethod = value;
                if (Application.isPlaying)
                {
                    smoothing.OnInit(proxy);
                }
            }
        }

        [SerializeReference, TriInspector.HideReferencePicker] public InternalFeature recalculation = new NormalsRecalculationAreaWeight();
        [SerializeReference, TriInspector.HideReferencePicker] public InternalFeature smoothing = new NoneInternalFeature();
        private InternalFeature GetRecalculation(NormalsRecalculationMethod method)
        {
            switch (method)
            {
                default:
                case NormalsRecalculationMethod.AreaWeight:
                    return new NormalsRecalculationAreaWeight();
                case NormalsRecalculationMethod.AngleWeighted:
                    return new NormalsRecalculationAngleWeighted();
                case NormalsRecalculationMethod.AreaAndAngleWeight:
                    return new NormalsRecalculationAngleAndAreaWeighted();
            }
        }
        private InternalFeature GetSmoothing(NormalsSmoothingMethod smoothingMethod)
        {
            switch (smoothingMethod)
            {
                default:
                case NormalsSmoothingMethod.None:
                    return new NoneInternalFeature();
                case NormalsSmoothingMethod.NearestNeighbor:
                    return new NormalsRecalculationNearestNeighborSmooth();
                case NormalsSmoothingMethod.Laplacian:
                    return new NormalsRecalculationLaplacianSmooth();
            }
        }
        public override void OnInit(ProxyMeshAbstract proxyMesh)
        {
            base.OnInit(proxyMesh);

            if(recalculation == null)
                recalculation = GetRecalculation(recalculationMethod);
            if(smoothing == null)
                smoothing = GetSmoothing(smoothingMethod);

            recalculation.OnInit(proxy);
            smoothing.OnInit(proxy);
        }
        public override void OnJobComplete()
        {
            base.OnJobComplete();
            recalculation.OnJobComplete();
            smoothing.OnJobComplete();
        }
        public override void OnShutdown(ProxyMeshAbstract proxyMesh)
        {
            base.OnShutdown(proxy);

            recalculation.OnShutdown(proxyMesh);
            smoothing.OnShutdown(proxyMesh);
        }

        public override JobHandle NormalsJob(JobHandle dependsOn)
        {
            dependsOn = recalculation.StartJob(dependsOn);

            dependsOn = smoothing.StartJob(dependsOn);

            if (UseDeformationVector)
            {
                dependsOn = new AfterUpdateNormalsWithDeformVector()
                {
                    addedDeform = addedDeformation,
                    additionalDeform = additiveDeformation,
                    lastNormals = previousNormals,
                    normals = proxy.animatedNormals,
                    updateIndices = updateIndices.AsReadOnly(),
                }.Schedule(dependsOn);
            }

            return dependsOn;
        }

        [System.Serializable]
        public enum NormalsRecalculationMethod
        {
            AreaWeight = 0, AngleWeighted = 2, AreaAndAngleWeight = 3
        }

        [System.Serializable]
        public enum NormalsSmoothingMethod
        {
            None = 0, NearestNeighbor = 1,Laplacian = 2
        }
    }

    [BurstCompile]
    public struct ReadWrite : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> input;
        [WriteOnly] public NativeArray<float3> output;
        public void Execute(int index)
        {
            output[index] = input[index];
        }
    }

    [BurstCompile]
    public struct AfterUpdateNormalsWithDeformVector : IJob
    {
        public NativeArray<float3> normals;
        [ReadOnly] public NativeArray<float3> lastNormals;
        [ReadOnly] public NativeArray<float4> addedDeform;
        [ReadOnly] public NativeArray<float4> additionalDeform;
        [ReadOnly] public NativeParallelHashSet<int>.ReadOnly updateIndices;
        public void Execute()
        {
            foreach (var i in updateIndices)
            {
                normals[i] = NormalSlerp(math.normalizesafe(lastNormals[i]), 
                                         math.normalizesafe(normals[i]), 
                                         GetWeight(i));
            }
        }

        public float GetWeight(int i)
        {
            return math.pow(2, (math.length(addedDeform[i].xyz + additionalDeform[i].xyz) / math.max(additionalDeform[i].w, addedDeform[i].w)) - 1);
        }

        public static float3 NormalSlerp(float3 a, float3 b, float t)
        {
            quaternion q = Quaternion.FromToRotation(a, b);
            quaternion result = math.slerp(quaternion.identity, q, t);
            return math.mul(result, a);
        }
    }
}
