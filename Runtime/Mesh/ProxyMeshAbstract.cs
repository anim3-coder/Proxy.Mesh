using System.Collections.Generic;
using UnityEngine.Rendering;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Jobs;
using TriInspector;
using System.Text;
using Unity.Jobs;
using UnityEngine;
using System.Linq;
using System.Data;
using System;
using Proxy.Mesh.Normals;
using static Proxy.Mesh.NormalsRecalculation;

namespace Proxy.Mesh
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [DeclareFoldoutGroup("Main")]
    [DeclareToggleGroup("Groups", Title = "Groups")]
    [DeclareToggleGroup("Normal", Title = "Normals Recalculation")]
    [DeclareToggleGroup("Tangent", Title = "Tangents Recalculation")]
    public abstract partial class ProxyMeshAbstract : MonoBehaviour, IDisposable
    {
        #region Data
        [SerializeField, RequiredGet(), Group("Main")] protected MeshFilter meshFilter;
        [SerializeField, RequiredGet(), Group("Main")] protected MeshRenderer meshRenderer;
        protected UnityEngine.Mesh originalMesh;
        protected UnityEngine.Mesh animatedMesh;

        public virtual ProxySkeleton skeleton { get; protected set; }

        public NativeArray<float4x4> bindPoses;
        public NativeBitArray changePoses;
        public TransformAccessArray bonesTransformAccessArray => skeleton.bonesTransformAccessArray;
        public NativeArray<float4x4> boneMatrices => skeleton.boneMatrices;
        public NativeArray<float3> boneVelocities => skeleton.localVelocities;
        public NativeArray<float3> boneAccelerations => skeleton.localAccelerations;

        #region BlendShapeCount
        private int _blendShapeCount = -1;
        public int blendShapeCount
        {
            get
            {
                if (_blendShapeCount == -1)
                    _blendShapeCount = GetComponent<MeshFilter>().sharedMesh.blendShapeCount;
                return _blendShapeCount;
            }
        }
        #endregion
        [field: SerializeField, TriInspector.ReadOnly, Group("Main")] public int vertexCount { get; protected set; }

        [SerializeField, OnValueChanged(nameof(MarkDirty)), Group("Main")] protected BlendShapeData[] blendShapeWeights;
        protected NativeArray<float> nativeBlendShapeWeights;
        public NativeArray<BoneWeight1> allBoneWeights;
        public NativeArray<byte> bonesPerVertexRaw;
        public NativeArray<int> bonesPerVertex;
        public NativeArray<int> startIndices;

        // Quality settings
        [SerializeField, Group("Main"), EnumToggleButtons] protected Quality bonesPerVertexQuality = Quality.FourBones;
        [SerializeField, Group("Main")] protected bool IsDrawChangesBone;
        // Job data
        public NativeArray<float3> animatedVertices;
        public NativeArray<float3> animatedNormals;
        public NativeArray<float4> animatedTangents;
        public NativeArray<float3> nativeBaseVertices;
        public NativeArray<float3> nativeBaseNormals;
        public NativeArray<float4> nativeBaseTangents;
        public NativeArray<float2> nativeUV;
        public NativeHashSet<int> triangleHash;
        public NativeArray<int> triangles;

        // Job handle
        protected JobHandle jobHandle;
        public virtual IProxyChild[] proxyChildren { get; protected set; }

        // Bounds data
        #endregion

        #region Feature

        #region Transform
        private Transform _transform;
        public new Transform transform
        {
            get
            {
                if (_transform == null)
                    _transform = base.transform;
                return _transform;
            }
        }
        #endregion

        [field: SerializeField, Group("Main")] public SkeletonDeformation skeletonDeformation { get; protected set; } = new SkeletonDeformation();
        public VertexNeighbours vertexNeighbours { get; private set; } = new VertexNeighbours();
        #endregion

        #region Bounds
        [field: SerializeField, Group("Main")] public BoundsRecalculation boundsRecalculation { get; protected set; }
        public NativeArray<float> boundsMin;
        public NativeArray<float> boundsMax;
        #endregion

        #region Groups
        [field: SerializeField, Group("Groups")] public bool GroupsEnabled { get; internal set; }
        [field: SerializeField, Group("Groups"), HideLabel, InlineProperty] public SkeletonGroups skeletonGroups { get; internal set; } = new SkeletonGroups();
        #endregion

        #region Normals
        [field: SerializeField, Group("Normal")] public bool NormalRecalculationEnabled { get; internal set; }
        [SerializeField, HideInInspector] private NormalsRecalculationBased _normalsRecalculationBasedOn;
        [TriInspector.ShowInInspector] public NormalsRecalculationBased normalsRecalculationBasedOn
        {
            get
            {
                return _normalsRecalculationBasedOn;
            }
            private set
            {
                if (value == _normalsRecalculationBasedOn)
                {
                    return;
                }
                if (Application.isPlaying)
                {
                    normalsRecalculation.OnShutdown(this);
                }
                switch (value)
                {
                    case NormalsRecalculationBased.Vertices:
                        normalsRecalculation = new NormalsRecalculation();
                        break;
                    case NormalsRecalculationBased.Mapping:
                        normalsRecalculation = new NormalsMapRecalculation();
                        break;
                }
                _normalsRecalculationBasedOn = value;
                if (Application.isPlaying)
                {
                    normalsRecalculation.OnInit(this);
                }
            }
        }
        [field: SerializeReference, Group("Normal"), HideLabel, InlineProperty, TriInspector.HideReferencePicker] public NormalsRecalculationBase normalsRecalculation { get; internal set; } = new NormalsRecalculation();

        /// <summary>
        /// Способ изменения нормалей. 
        /// Vertices - напрямую.
        /// Mapping - создает карту изменения нормалей
        /// </summary>
        [System.Serializable]
        public enum NormalsRecalculationBased
        {
            Vertices, Mapping
        }
        #endregion

        #region Tangents
        [field: SerializeField, Group("Tangent")] public bool TangentRecalculationEnabled { get; internal set; }
        [field: SerializeField, Group("Tangent"), HideLabel, InlineProperty] public TangentsRecalculation tangentsRecalculation { get; internal set; }
        #endregion

        #region Abstract Child
        private IProxyAbstractChild[] _proxyAbstractChilds;
        private IProxyAbstractChild[] proxyAbstractChilds
        {
            get
            {
                if(_proxyAbstractChilds == null)
                {
                    _proxyAbstractChilds = new IProxyAbstractChild[]
                    {
                        skeletonDeformation,
                        vertexNeighbours,
                        normalsRecalculation,
                        tangentsRecalculation,
                        boundsRecalculation,
                        skeletonGroups,
                    };
                }
                return _proxyAbstractChilds;
            }
        }

        private void InitAbstractChild()
        {
            for(int i = 0; i < proxyAbstractChilds.Length;i++)
            {
                proxyAbstractChilds[i].OnInit(this);
            }
        }
        private void ShutdownAbstractChild()
        {
            for (int i = 0; i < proxyAbstractChilds.Length; i++)
            {
                proxyAbstractChilds[i].OnShutdown(this);
            }
        }

        #endregion

        #region Bone Weight Optimization Methods
        /// <summary>
        /// Оптимизирует костные веса, оставляя только самые влиятельные кости
        /// </summary>
        protected void OptimizeBoneWeights(ref BoneWeight1[] boneWeights, ref byte[] bonesPerVertex, int maxBonesPerVertex)
        {
            if (maxBonesPerVertex >= 255) return; // Unlimited

            List<BoneWeight1> optimizedWeights = new List<BoneWeight1>();
            byte[] optimizedBonesPerVertex = new byte[bonesPerVertex.Length];

            int currentWeightIndex = 0;

            for (int vertexIndex = 0; vertexIndex < bonesPerVertex.Length; vertexIndex++)
            {
                int originalBoneCount = bonesPerVertex[vertexIndex];

                if (originalBoneCount <= maxBonesPerVertex)
                {
                    // Если костей уже меньше или равно лимиту, просто копируем
                    optimizedBonesPerVertex[vertexIndex] = (byte)originalBoneCount;
                    for (int i = 0; i < originalBoneCount; i++)
                    {
                        optimizedWeights.Add(boneWeights[currentWeightIndex + i]);
                    }
                    currentWeightIndex += originalBoneCount;
                }
                else
                {
                    // Собираем все веса для этой вершины
                    List<BoneWeight1> vertexWeights = new List<BoneWeight1>();
                    for (int i = 0; i < originalBoneCount; i++)
                    {
                        vertexWeights.Add(boneWeights[currentWeightIndex + i]);
                    }

                    // Сортируем по весу в убывающем порядке
                    vertexWeights.Sort((a, b) => b.weight.CompareTo(a.weight));

                    // Оставляем только maxBonesPerVertex самых влиятельных костей
                    float totalWeight = 0f;
                    for (int i = 0; i < maxBonesPerVertex; i++)
                    {
                        totalWeight += vertexWeights[i].weight;
                    }

                    // Нормализуем веса
                    for (int i = 0; i < maxBonesPerVertex; i++)
                    {
                        var weight = vertexWeights[i];
                        weight.weight /= totalWeight;
                        optimizedWeights.Add(weight);
                    }

                    optimizedBonesPerVertex[vertexIndex] = (byte)maxBonesPerVertex;
                    currentWeightIndex += originalBoneCount;
                }
            }

            // Заменяем исходные массивы оптимизированными
            boneWeights = optimizedWeights.ToArray();
            bonesPerVertex = optimizedBonesPerVertex;
        }
        #endregion

        #region Unity methods
        public void ReInit()
        {
            if (meshFilter == null)
                Initialize();
            else
            {
                Cleanup();
                Initialize();
            }
        }

        protected virtual void Initialize()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();

            if (meshFilter == null || meshRenderer == null)
            {
                Debug.LogError("MeshFilter or MeshRenderer is missing");
                enabled = false;
                return;
            }

            originalMesh = meshFilter.sharedMesh;
            vertexCount = originalMesh.vertexCount;
            // Проверка наличия меша
            if (originalMesh == null)
            {
                Debug.LogError("Mesh is not assigned to MeshFilter");
                enabled = false;
                return;
            }

            // Создаем анимированную копию меша
            animatedMesh = Instantiate(originalMesh);
            
            var vertexAttributes = new[]
            {
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream: 1),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, stream: 2),
                new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, stream: 3)
                // при необходимости добавьте другие атрибуты (UV, Tangent и т.д.) с указанием stream 2, 3
            };
            animatedMesh.SetVertexBufferParams(vertexCount, vertexAttributes);
            
            int[] originalIndices = originalMesh.triangles;
            animatedMesh.SetIndexBufferParams(originalIndices.Length, IndexFormat.UInt32);
            animatedMesh.SetIndexBufferData(originalIndices, 0, 0, originalIndices.Length, MeshUpdateFlags.DontValidateIndices);

            // --- Настройка subMesh'ей ---
            int subMeshCount = originalMesh.subMeshCount;
            animatedMesh.subMeshCount = subMeshCount;

            // Для каждого subMesh получаем его описание из оригинального меша
            for (int i = 0; i < subMeshCount; i++)
            {
                // Получаем подмножество индексов для данного subMesh
                var subMeshIndices = originalMesh.GetIndices(i);
                // Создаём дескриптор: начальный индекс в общем буфере и длина
                var descriptor = new SubMeshDescriptor(
                    indexStart: (int)originalMesh.GetIndexStart((int)i),  // можно также вычислить вручную, но удобнее через GetIndexStart (Unity 2019.3+)
                    indexCount: subMeshIndices.Length,
                    topology: originalMesh.GetTopology(i)
                );

                // Дополнительно можно скопировать базовую вершину и ограничивающую область, если нужно
                descriptor.baseVertex = 0; // обычно 0, если вершины не смещены
                                           // descriptor.bounds = ... ; // можно скопировать из originalMesh.GetSubMesh(i).bounds

                animatedMesh.SetSubMesh(i, descriptor, MeshUpdateFlags.DontRecalculateBounds);
            }
            animatedMesh.MarkDynamic();
            meshFilter.sharedMesh = animatedMesh;

            var originalUV = originalMesh.uv;
            using (var tempArray = new NativeArray<Vector2>(originalUV, Allocator.Temp))
            {
                animatedMesh.SetVertexBufferData(tempArray, 0, 0, vertexCount, 2, MeshUpdateFlags.DontRecalculateBounds);
            }

            if (blendShapeCount != blendShapeWeights.Length)
            {
                InitializeBlendShapes();
            }

            // Проверка целостности данных
            if (vertexCount == 0)
            {
                Debug.LogError("Mesh has no vertices");
                enabled = false;
                return;
            }

            // Применение blend shapes к базовым вершинам
            ApplyBlendShapes(originalMesh.vertices, originalMesh.normals, originalMesh.tangents, out Vector3[] baseVertices, out Vector3[] baseNormals, out Vector4[] baseTangents);

            var boneWeights1 = originalMesh.GetAllBoneWeights().ToArray();
            var bonesPerVertexRawNative = originalMesh.GetBonesPerVertex().ToArray();

            // Оптимизация костных весов в соответствии с настройкой качества
            if (bonesPerVertexQuality != Quality.Unlimited)
            {
                OptimizeBoneWeights(ref boneWeights1, ref bonesPerVertexRawNative, (int)bonesPerVertexQuality);
            }

            // Создание Native массивов
            nativeBaseVertices = new NativeArray<float3>(Convert(baseVertices), Allocator.Persistent);
            nativeBaseNormals = new NativeArray<float3>(Convert(baseNormals), Allocator.Persistent);
            nativeBaseTangents = new NativeArray<float4>(Convert(baseTangents), Allocator.Persistent);
            nativeUV = new NativeArray<float2>(Convert(originalUV), Allocator.Persistent);
            animatedVertices = new NativeArray<float3>(vertexCount, Allocator.Persistent);
            animatedNormals = new NativeArray<float3>(vertexCount, Allocator.Persistent);
            animatedTangents = new NativeArray<float4> (vertexCount, Allocator.Persistent);
            triangles = new NativeArray<int>(originalMesh.triangles, Allocator.Persistent);
            triangleHash = new NativeHashSet<int>(originalMesh.triangles.Length, Allocator.Persistent);
            foreach (var tri in triangles)
                triangleHash.Add(tri);

            // Инициализация для расчета границ
            boundsMin = new NativeArray<float>(3, Allocator.Persistent);
            boundsMax = new NativeArray<float>(3, Allocator.Persistent);

            // Инициализация костей
            InitializeBones();

            // Копирование данных о весах костей
            allBoneWeights = new NativeArray<BoneWeight1>(boneWeights1, Allocator.Persistent);
            bonesPerVertexRaw = new NativeArray<byte>(bonesPerVertexRawNative, Allocator.Persistent);
            nativeBlendShapeWeights = new NativeArray<float>(blendShapeWeights.Convert(), Allocator.Persistent);

            // Преобразование byte в int для bonesPerVertex
            bonesPerVertex = new NativeArray<int>(vertexCount, Allocator.Persistent);
            startIndices = new NativeArray<int>(vertexCount, Allocator.Persistent);

            if (skeleton.rootBone != null)
            {
                int currentStartIndex = 0;
                for (int i = 0; i < vertexCount; i++)
                {
                    bonesPerVertex[i] = (int)bonesPerVertexRaw[i];
                    startIndices[i] = currentStartIndex;
                    currentStartIndex += bonesPerVertex[i];
                }
            }
            InitAbstractChild();
        }

        [ContextMenu("InitializeBlendShapes")]
        private void InitializeBlendShapes()
        {
            blendShapeWeights = new BlendShapeData[blendShapeCount];
            for (int i = 0; i < blendShapeWeights.Length; i++)
            {
                blendShapeWeights[i] = new BlendShapeData() { name = meshFilter.mesh.GetBlendShapeName(i), value = 0 };
            }
        }

        private void InitializeBones()
        {
            if (skeleton.rootBone == null)
                return;
            skeleton.InitSkeleton(originalMesh);
            if (originalMesh.bindposes.Length != Bones.Length)
                Debug.LogWarning($"Incorrect length bindposes:{originalMesh.bindposes.Length} boners: {Bones.Length}");
            if (originalMesh != null && originalMesh.bindposes.Length > 0 && originalMesh.bindposes.Length <= Bones.Length)
            {
                changePoses = new NativeBitArray(originalMesh.bindposeCount, Allocator.Persistent);
                bindPoses = new NativeArray<float4x4>(Convert(originalMesh.bindposes), Allocator.Persistent);
            }
            else
            {
                Debug.LogError("Incorrect initialize bones: Check bones or re-create");
                changePoses = new NativeBitArray(originalMesh.bindposeCount, Allocator.Persistent);
                bindPoses = new NativeArray<float4x4>(Bones.Length, Allocator.Persistent);
                for (int i = 0; i < Bones.Length; i++)
                {
                    bindPoses[i] = Matrix4x4.identity;
                }
            }
        }

        public void Dispose() => Cleanup();
        protected virtual void Cleanup()
        {
            jobHandle.Complete();

            if (nativeBaseVertices.IsCreated) nativeBaseVertices.Dispose();
            if (animatedVertices.IsCreated) animatedVertices.Dispose();
            if (allBoneWeights.IsCreated) allBoneWeights.Dispose();
            if (bonesPerVertexRaw.IsCreated) bonesPerVertexRaw.Dispose();
            if (bonesPerVertex.IsCreated) bonesPerVertex.Dispose();
            if (startIndices.IsCreated) startIndices.Dispose();
            if (animatedNormals.IsCreated) animatedNormals.Dispose();
            if (nativeBaseTangents.IsCreated) nativeBaseTangents.Dispose();
            if (animatedTangents.IsCreated) animatedTangents.Dispose();
            if (nativeBaseNormals.IsCreated) nativeBaseNormals.Dispose();
            if (nativeBlendShapeWeights.IsCreated) nativeBlendShapeWeights.Dispose();
            if (boundsMin.IsCreated) boundsMin.Dispose();
            if (boundsMax.IsCreated) boundsMax.Dispose();
            if (bindPoses.IsCreated) bindPoses.Dispose();
            if (changePoses.IsCreated) changePoses.Dispose();
            if (triangles.IsCreated) triangles.Dispose();
            if (triangleHash.IsCreated) triangleHash.Dispose();

            if(skeleton != null) skeleton.ShutdownSkeleton();

            // Восстановление оригинального меша
            if (meshFilter && originalMesh)
            {
                meshFilter.sharedMesh = originalMesh;
            }

            // Уничтожаем созданный анимированный меш
            if (animatedMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(animatedMesh);
                else
                    DestroyImmediate(animatedMesh);
            }

            ShutdownAbstractChild();
        }

        protected void UpdateMesh()
        {
            if (animatedMesh != null && animatedVertices.IsCreated)
            {
                if (gameObject.activeSelf == false)
                    return;
                animatedMesh.SetVertexBufferData(animatedVertices, 0, 0, animatedVertices.Length,stream: 0,flags: MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontValidateLodRanges);
                animatedMesh.SetVertexBufferData(animatedNormals, 0, 0,animatedNormals.Length,stream: 1, flags: MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontValidateLodRanges);
                animatedMesh.SetVertexBufferData(animatedTangents, 0, 0, animatedTangents.Length, stream: 3, flags: MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontValidateLodRanges);

                if (boundsMin.IsCreated && boundsMax.IsCreated)
                {
                    Vector3 min = new Vector3(boundsMin[0], boundsMin[1], boundsMin[2]);
                    Vector3 max = new Vector3(boundsMax[0], boundsMax[1], boundsMax[2]);
                    Bounds bounds = new Bounds();
                    bounds.SetMinMax(min, max);
                    animatedMesh.bounds = bounds;
                }
            }
        }
        #endregion

        #region Methods
        protected void ApplyBlendShapes(Vector3[] baseVerts, Vector3[] baseNormals, Vector4[] baseTangents, out Vector3[] vertices, out Vector3[] normals, out Vector4[] tangents)
        {
            vertices = (Vector3[])baseVerts.Clone();
            normals = (Vector3[])baseNormals.Clone();
            tangents = (Vector4[])baseTangents.Clone();

            if (blendShapeCount == 0)
                return;

            Vector3[] deltaVertices = new Vector3[vertexCount];
            Vector3[] deltaNormals = new Vector3[vertexCount];
            Vector3[] deltaTangents = new Vector3[vertexCount];

            for (int shapeIndex = 0; shapeIndex < blendShapeCount; shapeIndex++)
            {
                float weight = blendShapeWeights[shapeIndex];
                if (Mathf.Abs(weight) < 0.001f) continue;

                int frameCount = originalMesh.GetBlendShapeFrameCount(shapeIndex);
                if (frameCount == 0) continue;

                int frameIndex = frameCount - 1;
                originalMesh.GetBlendShapeFrameVertices(shapeIndex, frameIndex, deltaVertices, deltaNormals, deltaTangents);

                for (int i = 0; i < vertexCount; i++)
                {
                    vertices[i] += deltaVertices[i] * weight;
                    normals[i] += deltaNormals[i] * weight;
                    tangents[i] += new Vector4(deltaTangents[i].x, deltaTangents[i].y, deltaTangents[i].z, 0f) * weight;
                }
            }
        }

        public static float2[] Convert(Vector2[] data)
        {
            return Convert<float2, Vector2>(data, (Vector2 m) => m);
        }

        public static float4[] Convert(Vector4[] data)
        {
            return Convert<float4, Vector4>(data, (Vector4 m) => m);
        }

        public static float4x4[] Convert(Matrix4x4[] matrices)
        {
            return Convert<float4x4, Matrix4x4>(matrices, (Matrix4x4 m) => m);
        }

        public static float3[] Convert(Vector3[] array)
        {
            return Convert<float3, Vector3>(array, (Vector3 v) => v);
        }

        public static T[] Convert<T, Y>(Y[] array, System.Func<Y,T> Method)
        {
            T[] result = new T[array.Length];

            for(int i = 0; i < result.Length;i++)
            {
                result[i] = Method.Invoke(array[i]);
            }

            return result;
        }

        protected virtual JobHandle StartNewJob(JobHandle dependsOn)
        {
            if (gameObject.activeSelf == false)
                return dependsOn;
            
            if(isDirty)
            {
                UpdateBlendShapes();
                isDirty = false;
            }

            dependsOn = UpdateBoneMatrices(dependsOn);

            // Инициализация границ
            InitBounds();

            // Запуск job с безопасным размером пакета
            dependsOn = UpdateSkeletonDeformation(dependsOn);

            if(NormalRecalculationEnabled)
                dependsOn = RecalculationNormals(dependsOn);

            if(TangentRecalculationEnabled)
                dependsOn = RecalculationTangents(dependsOn);

            // Запуск job для расчета границ после анимации вершин
            dependsOn = RecalculateBounds(dependsOn);

            return dependsOn;
        }

        public JobHandle UpdateBoneMatrices(JobHandle dependsOn)
        {
            if (bonesTransformAccessArray.isCreated && bonesTransformAccessArray.length > 0)
            {
                dependsOn = new UpdateBoneMatricesJob()
                {
                    bindPoses = bindPoses,
                    worldToLocalMatrix = transform.worldToLocalMatrix,
                    boneMatrices = boneMatrices,
                    changePoses = changePoses,
                }.Schedule(bonesTransformAccessArray, dependsOn);
            }
            return dependsOn;
        }

        public void InitBounds()
        {
            if (boundsRecalculation.Method == BoundsRecalculation.BoundsRecalculationMethod.None)
                return;
            if (vertexCount > 0 && animatedVertices.IsCreated)
            {
                boundsMin[0] = float.MaxValue;
                boundsMin[1] = float.MaxValue;
                boundsMin[2] = float.MaxValue;

                boundsMax[0] = float.MinValue;
                boundsMax[1] = float.MinValue;
                boundsMax[2] = float.MinValue;

                skeletonGroups.InitBounds();
            }
        }
        
        public JobHandle RecalculateBounds(JobHandle dependsOn)
        {
            return boundsRecalculation.StartJob(dependsOn);
        }

        public JobHandle RecalculationNormals(JobHandle dependsOn)
        {
            if(normalsRecalculation.IsInit == false)
                return dependsOn;
            return normalsRecalculation.StartJob(dependsOn);
        }

        public JobHandle RecalculationTangents(JobHandle dependsOn)
        {
            if (tangentsRecalculation.IsInit == false)
                return dependsOn;
            return tangentsRecalculation.StartJob(dependsOn);
        }


        public JobHandle UpdateSkeletonDeformation(JobHandle dependsOn)
        {
            return skeletonDeformation.StartJob(dependsOn);
        }
        
        public JobHandle UpdateSkeletonBounds(JobHandle dependsOn)
        {
            if(skeletonGroups.IsInit == false)
                return dependsOn;
            return skeletonGroups.StartJob(dependsOn);
        }

        public bool isDirty { get; protected set; }

        public JobHandle MultiPass(JobHandle dependsOn, int length, System.Func<int, JobHandle, JobHandle> Action)
        {
            NativeArray<JobHandle> jobs = new NativeArray<JobHandle>(length, Allocator.TempJob);
            for (int i = 0; i < length; i++)
            {
                jobs[i] = Action.Invoke(i, dependsOn);
            }
            return jobs.Dispose(JobHandle.CombineDependencies(jobs));
        }

        protected void MarkDirty() => isDirty = true;

        protected virtual void UpdateBlendShapes()
        {
            ApplyBlendShapes(originalMesh.vertices, originalMesh.normals, originalMesh.tangents, out Vector3[] vertices, out Vector3[] normals, out Vector4[] tangents);

            nativeBaseVertices.CopyFrom(Convert(vertices));
            nativeBaseNormals.CopyFrom(Convert(normals));
            nativeBaseTangents.CopyFrom(Convert(tangents));
            nativeBlendShapeWeights.CopyFrom(blendShapeWeights.Convert());
        }
        public void SetBlendShapeWeight(int index, float weight)
        {
            if (index < 0 || index >= blendShapeCount)
            {
                Debug.LogError($"Blend shape index {index} is out of range");
                return;
            }

            blendShapeWeights[index].value = weight;
            isDirty = true;
        }

        public void SetBlendShapeWeights(float[] weights)
        {
            blendShapeWeights.Write(weights);
            isDirty = true;
        }

        public float GetBlendShapeWeight(int index)
        {
            if (index < 0 || index >= blendShapeCount)
            {
                Debug.LogError($"Blend shape index {index} is out of range");
                return 0f;
            }

            return blendShapeWeights[index];
        }

        public float[] GetBlendShapeWeights()
        {
            return blendShapeWeights.Convert();
        }

        public Material[] Materials
        {
            get => meshRenderer.sharedMaterials;
            set => meshRenderer.sharedMaterials = value;
        }

        public Material Material
        {
            get => meshRenderer.sharedMaterial;
            set => meshRenderer.sharedMaterial = value;
        }

        #region Raycast 
        /// <summary>
        /// 
        /// </summary>
        /// <param name="origin"></param>
        /// <param name="direction"></param>
        /// <param name="resultPosition">1 element</param>
        /// <param name="resultNormal">1 element</param>
        /// <param name="resultVertices">3 element</param>
        public JobHandle StartRaycastJob(Vector3 origin, Vector3 direction, NativeList<Vector3> resultPosition, NativeList<Vector3> resultNormal,JobHandle dependsOn, float closestHitDistance = float.MaxValue)
        {
            return new RaycastJob()
            {
                origin = origin,
                direction = direction,
                closestHitDistance = closestHitDistance,
                resultNormal = resultNormal.AsParallelWriter(),
                resultPosition = resultPosition.AsParallelWriter(),
                localToWorldMatrix = transform.localToWorldMatrix,
                vertices = animatedVertices,
                triangles = triangles
            }.Schedule(triangles.Length / 3, math.min(1, triangles.Length / 100),dependsOn);
        }
        #endregion

        #region Bones Map
        public Dictionary<int, int[]> GetVertexBoneMap()
        {
            Dictionary<int, int[]> vertexBoneMap = new Dictionary<int, int[]>();

            if (!bonesPerVertex.IsCreated || !startIndices.IsCreated || !allBoneWeights.IsCreated)
            {
                Debug.LogError("Bone weight data not initialized");
                return vertexBoneMap;
            }

            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                int boneCount = bonesPerVertex[vertexIndex];
                int startIndex = startIndices[vertexIndex];

                if (boneCount <= 0)
                {
                    vertexBoneMap[vertexIndex] = new int[0];
                    continue;
                }

                int[] boneIndices = new int[boneCount];
                int validCount = 0;

                for (int i = 0; i < boneCount; i++)
                {
                    int weightIndex = startIndex + i;

                    if (weightIndex >= allBoneWeights.Length)
                    {
                        Debug.LogWarning($"Weight index out of range: {weightIndex} >= {allBoneWeights.Length}");
                        continue;
                    }

                    BoneWeight1 bw = allBoneWeights[weightIndex];
                    if (bw.weight > 0.0001f)
                    {
                        boneIndices[validCount] = bw.boneIndex;
                        validCount++;
                    }
                }

                if (validCount < boneCount)
                {
                    int[] trimmedBones = new int[validCount];
                    System.Array.Copy(boneIndices, trimmedBones, validCount);
                    vertexBoneMap[vertexIndex] = trimmedBones;
                }
                else
                {
                    vertexBoneMap[vertexIndex] = boneIndices;
                }
            }

            return vertexBoneMap;
        }

        public int[] GetBonesForVertex(int vertexIndex)
        {
            if (vertexIndex < 0 || vertexIndex >= vertexCount)
            {
                Debug.LogError($"Invalid vertex index: {vertexIndex}");
                return new int[0];
            }

            int boneCount = bonesPerVertex[vertexIndex];
            int startIndex = startIndices[vertexIndex];
            int[] bones = new int[boneCount];

            for (int i = 0; i < boneCount; i++)
            {
                bones[i] = allBoneWeights[startIndex + i].boneIndex;
            }

            return bones;
        }

        public List<int> GetVerticesForBone(int boneIndex, float minWeight = 0)
        {
            if(boneIndex == -1)
                return new List<int>() { };
            List<int> vertices = new List<int>();

            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                int boneCount = bonesPerVertex[vertexIndex];
                int startIndex = startIndices[vertexIndex];

                for (int i = 0; i < boneCount; i++)
                {
                    if (allBoneWeights[startIndex + i].boneIndex == boneIndex &&
                        allBoneWeights[startIndex + i].weight >= minWeight)
                    {
                        vertices.Add(vertexIndex);
                        break;
                    }
                }
            }

            return vertices;
        }

        public List<int> GetVerticesForBone(Transform transform, float minWeight = 0)
        {
            return GetVerticesForBone(GetBoneIndex(transform), minWeight);
        }

        public List<int> GetMajorVerticesForBone(Transform transform, int[] major)
        {
            var boneIndex = GetBoneIndex(transform);
            if (boneIndex == -1)
                return new List<int>() { };
            List<int> vertices = new List<int>();

            for (int vertexIndex = 0; vertexIndex < major.Length; vertexIndex++)
            {
                if (major[vertexIndex] == boneIndex)
                    vertices.Add(vertexIndex);
            }
            return vertices;
        }

        public int[] GetMajorVertices()
        {
            int[] vertices = new int[vertexCount];

            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                int boneCount = bonesPerVertex[vertexIndex];
                int startIndex = startIndices[vertexIndex];

                int majorBoneIndex = -1;
                float majorBoneWeight = -1;

                for (int i = 0; i < boneCount; i++)
                {
                    if (allBoneWeights[startIndex + i].weight > majorBoneWeight)
                    {
                        majorBoneWeight = allBoneWeights[startIndex + i].weight;
                        majorBoneIndex = allBoneWeights[startIndex + i].boneIndex;
                    }
                }
                vertices[vertexIndex] = majorBoneIndex;
            }

            return vertices;
        }

        public float GetBoneWeightForVertex(int boneIndex, int vertexIndex)
        {
            if (vertexIndex < 0 || vertexIndex >= vertexCount)
                return 0;

            int boneCount = bonesPerVertex[vertexIndex];
            int startIndex = startIndices[vertexIndex];

            for (int i = 0; i < boneCount; i++)
            {
                BoneWeight1 bw = allBoneWeights[startIndex + i];
                if (bw.boneIndex == boneIndex)
                {
                    return bw.weight;
                }
            }

            return 0;
        }
        #endregion

        #region TransformBone Map
        public int GetBoneIndex(Transform boneTransform)
        {
            if (boneTransform == null)
            {
                Debug.LogWarning("Bone transform is null!");
                return -1;
            }

            if (Bones == null)
            {
                Debug.LogError("Bones array not initialized!");
                return -1;
            }

            for (int i = 0; i < Bones.Length; i++)
            {
                if (Bones[i] == boneTransform)
                    return i;
            }

            return -1;
        }

        public Transform GetBoneTransform(int index)
        {
            if (Bones == null || index < 0 || index >= Bones.Length)
                return null;

            return Bones[index];
        }

        public virtual Transform[] Bones
        {
            get => skeleton.bones;
            set
            {
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

        public virtual Transform RootBone
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
        #endregion

        #region Helper Methods
        protected Vector3 TransformPoint(Matrix4x4 matrix, Vector3 point)
        {
            Vector3 res;
            res.x = matrix.m00 * point.x + matrix.m01 * point.y + matrix.m02 * point.z + matrix.m03;
            res.y = matrix.m10 * point.x + matrix.m11 * point.y + matrix.m12 * point.z + matrix.m13;
            res.z = matrix.m20 * point.x + matrix.m21 * point.y + matrix.m22 * point.z + matrix.m23;
            return res;
        }

        protected Vector3 TransformVector(Matrix4x4 matrix, Vector3 vector)
        {
            Vector3 res;
            res.x = matrix.m00 * vector.x + matrix.m01 * vector.y + matrix.m02 * vector.z;
            res.y = matrix.m10 * vector.x + matrix.m11 * vector.y + matrix.m12 * vector.z;
            res.z = matrix.m20 * vector.x + matrix.m21 * vector.y + matrix.m22 * vector.z;
            return res;
        }
        #endregion

        public struct BoneWeightStatistics
        {
            public int totalVertices;
            public int maxBonesPerVertex;
            public Dictionary<int, int> vertexCountByBoneCount;

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Total Vertices: {totalVertices}");
                sb.AppendLine($"Max Bones Per Vertex: {maxBonesPerVertex}");
                sb.AppendLine("Vertex Distribution by Bone Count:");
                foreach (var kvp in vertexCountByBoneCount.OrderBy(x => x.Key))
                {
                    sb.AppendLine($"  {kvp.Key} bones: {kvp.Value} vertices ({((float)kvp.Value / totalVertices * 100f):F1}%)");
                }
                return sb.ToString();
            }
        }

        public enum Quality
        {
            OneBone = 1,
            TwoBones = 2,
            FourBones = 4,
            Unlimited = 255
        }
        #endregion
    }
}