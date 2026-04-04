using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using UnityEngine;
using Vertx.Debugging;
using Proxy.Mesh.Normals;

namespace Proxy.Mesh
{
    [System.Serializable]
    public class NormalsRecalculation : NormalsRecalculationBase
    {
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
                switch (value)
                {
                    case NormalsRecalculationMethod.Simple:
                        recalculation = new NormalsRecalculationSimple();
                        break;
                    case NormalsRecalculationMethod.BasedOnNative:
                        recalculation = new NormalsRecalculationBasedOnNative();
                        break;
                }
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
                switch (value)
                {
                    case NormalsSmoothingMethod.None:
                        smoothing = new NoneInternalFeature();
                        break;
                    case NormalsSmoothingMethod.NearestNeighbor:
                        smoothing = new NormalsRecalculationNearestNeighborSmooth();
                        break;
                    case NormalsSmoothingMethod.Laplacian:
                        smoothing = new NormalsRecalculationLaplacianSmooth();
                        break;
                }
                _smoothingMethod = value;
                if (Application.isPlaying)
                {
                    smoothing.OnInit(proxy);
                }
            }
        }

        [SerializeReference, TriInspector.HideReferencePicker] public InternalFeature recalculation = new NormalsRecalculationSimple();
        [SerializeReference, TriInspector.HideReferencePicker] public InternalFeature smoothing = new NoneInternalFeature();

        public override void OnInit(ProxyMeshAbstract proxyMesh)
        {
            base.OnInit(proxyMesh);
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
            if (updateIndices.IsCreated) updateIndices.Dispose();

            recalculation.OnShutdown(proxyMesh);
            smoothing.OnShutdown(proxyMesh);
        }

        public override JobHandle StartJob(JobHandle dependsOn)
        {
            if (IsInit == false)
                return dependsOn;
            
            dependsOn = recalculation.StartJob(dependsOn);

            dependsOn = smoothing.StartJob(dependsOn);

            return base.StartJob(dependsOn);
        }

        [System.Serializable]
        public enum NormalsRecalculationMethod
        {
            Simple = 0, BasedOnNative = 2
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
}
