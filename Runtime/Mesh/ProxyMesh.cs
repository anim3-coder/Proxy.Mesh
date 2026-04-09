using System.Collections.Generic;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Jobs;
using UnityEngine;
using System.Linq;
using System;
using TriInspector;
using Unity.Mathematics;
using UnityEngine.UIElements;

namespace Proxy.Mesh
{
    [DisallowMultipleComponent, AddComponentMenu("Proxy/ProxyMesh")]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ProxyMesh : ProxyMeshAbstract
    {
        #region Data
        protected Transform rootBone
        {
            get
            {
                return skeleton.rootBone;
            }
            set
            {
                skeleton.rootBone = value;
            }
        }
        protected Transform[] bones
        {
            get
            {
                return skeleton.bones;
            }
            set
            {
                skeleton.bones = value;
            }
        }

        protected IProxyJob[] proxyJob;
        [field: SerializeField, Group("Main")] public override ProxySkeleton skeleton { get; protected set; }
        #endregion

        #region Unity Methods

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            ProxyManager.MeshManager.Registration(this);
        }

        private void OnDisable()
        {
            ProxyManager.MeshManager.Remove(this);
        }

        protected override void Initialize()
        {
            base.Initialize();
            InitializeProxyChildren();
        }

        private void InitializeProxyChildren()
        {
            proxyChildren = Get<IProxyChild>();

            var proxyJobsList = new List<IProxyJob>();
            for (int i = 0; i < proxyChildren.Length; i++)
            {
                if (proxyChildren[i] is IProxyJob)
                {
                    proxyJobsList.Add((IProxyJob)proxyChildren[i]);
                }
            }
            proxyJob = proxyJobsList.ToArray();

            for (int i = 0; i < proxyChildren.Length; i++)
            {
                proxyChildren[i].OnInit(this);
            }
        }
        public virtual void OnFixedUpdate()
        {
            this.skeleton.FixedUpdate();
        }
        public virtual void OnUpdate(bool IsJobCompleted)
        {
            this.skeleton.Update();

            if (IsJobCompleted)
            {
                OnUpdateMesh();
            }
        }

        public virtual void OnLateUpdate(bool IsJobCompleted)
        {
        
        }

        public override Transform[] Bones
        {
            get
            {
#if UNITY_EDITOR
                if (Application.isPlaying == false)
                {
                    return bones;
                }
#endif
                return skeleton.bones;
            }
            set
            {
#if UNITY_EDITOR
                if (Application.isPlaying == false)
                {
                    bones = value;
                    return;
                }
#endif
                if (bonesTransformAccessArray.isCreated)
                    bonesTransformAccessArray.Dispose();

                skeleton.bones = value;
                if (skeleton.bones != null && skeleton.bones.Length > 0)
                {
                    skeleton.bonesTransformAccessArray = new TransformAccessArray(skeleton.bones);

                    // Обновляем boneMatrices если размер изменился
                    if (!boneMatrices.IsCreated || boneMatrices.Length != skeleton.bones.Length)
                    {
                        if (boneMatrices.IsCreated)
                            boneMatrices.Dispose();
                        skeleton.boneMatrices = new NativeArray<float4x4>(skeleton.bones.Length, Allocator.Persistent);
                    }
                }
            }
        }

        public override Transform RootBone
        {
            get
            {
#if UNITY_EDITOR
                if (Application.isPlaying == false)
                {
                    return rootBone;
                }
#endif
                return skeleton.rootBone;
            }
            set
            {
#if UNITY_EDITOR
                if (Application.isPlaying == false)
                {
                    rootBone = value;
                    return;
                }
#endif
                skeleton.rootBone = value;
            }
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        protected override void Cleanup()
        {
            base.Cleanup();

            if (proxyChildren != null)
            {
                for (int i = 0; i < proxyChildren.Length; i++)
                {
                    if (proxyChildren[i] == null) continue;
                    proxyChildren[i].OnShutdown(this);
                }
            }

            skeleton = null;
        }

        public NativeArray<float3> GetVertices()
        {
            return animatedVertices;
        }

        public NativeArray<float3> GetNormals()
        {
            return animatedNormals;
        }
        #endregion

        #region Override
        public override JobHandle StartNewJob(JobHandle dependsOn)
        {
            if (gameObject.activeSelf == false)
                return dependsOn;

            if (isDirty)
            {
                UpdateBlendShapes();
                isDirty = false;
            }

            // Обновление матриц костей только если есть кости
            dependsOn = UpdateBoneMatrices(dependsOn);

            if (IsDrawChangesBone)
            {
                dependsOn = new DrawChangeBones()
                {
                    changePoses = changePoses.AsReadOnly(),
                }.Schedule(bonesTransformAccessArray, dependsOn);
            }

            InitBounds();

            dependsOn = UpdateSkeletonDeformation(dependsOn);

            JobHandle afterSkinningHandle = StartProxyJobsAfterSkinning(dependsOn);

            // Запуск job для расчета границ после анимации вершин
            dependsOn = RecalculateBounds(dependsOn);
            dependsOn = UpdateSkeletonBounds(dependsOn);

            JobHandle parallelHandle = StartProxyJobsParallel(dependsOn);
            dependsOn = StartProxyJobs(dependsOn);

            dependsOn = RecalculationNormals(dependsOn);

            dependsOn = RecalculationTangents(dependsOn);

            dependsOn = JobHandle.CombineDependencies(dependsOn, parallelHandle, afterSkinningHandle);

            return dependsOn;
        }

        public JobHandle StartProxyJobsAfterSkinning(JobHandle dependsOn)
        {
            int count = 0;
            for (int i = 0; i < proxyJob.Length; i++)
                if (proxyJob[i].type == JobType.ParallelAfterSkinning)
                    count++;
            if (count == 0)
                return dependsOn;
            NativeArray<JobHandle> jobs = new NativeArray<JobHandle>(count, Allocator.TempJob);
            int index = 0;
            for (int i = 0; i < proxyJob.Length; i++)
            {
                if (proxyJob[i].type == JobType.ParallelAfterSkinning)
                {
                    jobs[index] = proxyJob[i].StartJob(dependsOn);
                    index++;
                }
            }
            return jobs.Dispose(JobHandle.CombineDependencies(jobs));
        }

        public JobHandle StartProxyJobsParallel(JobHandle dependsOn)
        {
            int count = 0;
            for (int i = 0; i < proxyJob.Length; i++)
                if (proxyJob[i].type == JobType.Parallel)
                    count++;
            if (count == 0)
                return dependsOn;
            NativeArray<JobHandle> jobs = new NativeArray<JobHandle>(count, Allocator.TempJob);
            int index = 0;
            for (int i = 0; i < proxyJob.Length; i++)
            {
                if (proxyJob[i].type == JobType.Parallel)
                {
                    jobs[index] = proxyJob[i].StartJob(dependsOn);
                    index++;
                }
            }
            return jobs.Dispose(JobHandle.CombineDependencies(jobs));
        }

        public JobHandle StartProxyJobs(JobHandle dependsOn)
        {
            for (int i = 0; i < proxyJob.Length; i++)
                if (proxyJob[i].type == JobType.Default)
                    dependsOn = proxyJob[i].StartJob(dependsOn);
            return dependsOn;
        }

        protected virtual void OnUpdateMesh()
        {
            UpdateMesh();
            boundsRecalculation.OnJobComplete();
            skeletonGroups.OnJobComplete();
            for (int i = 0; i < proxyJob.Length; i++)
                proxyJob[i].OnJobComplete();
            
            normalsRecalculation.OnJobComplete();
            tangentsRecalculation.OnJobComplete();
        }
        #endregion
    }
}