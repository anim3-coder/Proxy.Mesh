using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Vertx.Debugging;

namespace Proxy.Mesh
{
    [System.Serializable]
    public class SkeletonGroups : IProxyAbstractChild, IProxyJob
    {
        public bool IsInit
        {
            get
            {
                if (proxy == null)
                    return false;
                return proxy.GroupsEnabled && indices.Length > 0;
            }
        }
        private ProxyMeshAbstract proxy;
        public NativeArray<int>[] indices;
        public NativeHashSet<int>[] indicesSet;
        public NativeArray<float>[] boundsMin;
        public NativeArray<float>[] boundsMax;
        public NativeArray<Bounds> bounds;
        public Root[] roots;
        public Transforms[] trees;
        public bool IsDrawBounds = false;
        [HideInInspector]public bool IsDrawMesh = false;
        
        public int groupsCount => roots.Length;
        public void OnInit(ProxyMeshAbstract proxyMesh)
        {
            proxy = proxyMesh;
            Init();
        }
        public void Init()
        {
            indices = new NativeArray<int>[roots.Length];
            indicesSet = new NativeHashSet<int>[roots.Length];
            boundsMin = new NativeArray<float>[roots.Length];
            boundsMax = new NativeArray<float>[roots.Length];
            int[] major = proxy.GetMajorVertices();

            bounds = new NativeArray<Bounds>(roots.Length, Allocator.Persistent);
            for (int r = 0; r < roots.Length; r++)
            {
                List<int> ints = new List<int>();
                for (int i = 0; i < trees[r].transforms.Length; i++)
                {
                    ints.AddRange(proxy.GetMajorVerticesForBone(trees[r].transforms[i], major));
                }
                boundsMin[r] = new NativeArray<float>(3, Allocator.Persistent);
                boundsMax[r] = new NativeArray<float>(3, Allocator.Persistent);
                indices[r] = new NativeArray<int>(ints.ToArray(), Allocator.Persistent);
                indicesSet[r] = new NativeHashSet<int>(ints.Count, Allocator.Persistent);
                foreach (int i in ints)
                    indicesSet[r].Add(i);
            }
        }

        [TriInspector.Button("RecalculateTrees")]
        public void RecalculateTrees()
        {
            if (roots == null || roots.Length == 0)
            {
                trees = new Transforms[0];
                return;
            }

            // Create a fast lookup for all root transforms
            HashSet<Transform> rootTransforms = new HashSet<Transform>();
            foreach (var root in roots)
            {
                if (root != null && root.root != null)
                    rootTransforms.Add(root.root);
            }

            // Prepare the trees array
            trees = new Transforms[roots.Length];

            for (int i = 0; i < roots.Length; i++)
            {
                Root currentRoot = roots[i];
                if (currentRoot == null || currentRoot.root == null)
                {
                    trees[i] = new Transforms { transforms = new Transform[] { currentRoot.root } };
                    continue;
                }

                List<Transform> childrenList = new List<Transform>();
                childrenList.Add(currentRoot.root);
                CollectChildren(currentRoot.root, childrenList, rootTransforms);
                trees[i] = new Transforms { transforms = childrenList.ToArray() };
            }
        }

        private void CollectChildren(Transform parent, List<Transform> result, HashSet<Transform> rootSet)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                // If this child is itself a root, skip it and its entire subtree
                if (rootSet.Contains(child))
                    continue;

                result.Add(child);
                CollectChildren(child, result, rootSet);
            }
        }

        public void InitBounds()
        {
            for (int i = 0; i < indices.Length; i++)
            {
                boundsMin[i][0] = float.MaxValue;
                boundsMin[i][1] = float.MaxValue;
                boundsMin[i][2] = float.MaxValue;

                boundsMax[i][0] = float.MinValue;
                boundsMax[i][1] = float.MinValue;
                boundsMax[i][2] = float.MinValue;
            }
        }

        public void OnJobComplete()
        {
        }

        public void OnShutdown(ProxyMeshAbstract proxyMesh)
        {
            bounds.Dispose();
            for (int r = 0; r < indices.Length; r++)
            {
                indices[r].Dispose();
                indicesSet[r].Dispose();
                boundsMin[r].Dispose();
                boundsMax[r].Dispose();
            }
        }

        public JobHandle StartJob(JobHandle dependsOn)
        {
            if(proxy.GroupsEnabled == false)
            {
                return dependsOn;
            }

            dependsOn = MultiPass(dependsOn, indices.Length, CalculateBounds);

            dependsOn = MultiPass(dependsOn, indices.Length, WriteTo);

            if (IsDrawMesh)
            {
                dependsOn = MultiPass(dependsOn, indices.Length, DrawMesh);
            }

            if (IsDrawBounds)
            {
                dependsOn = new DrawBoundsJob()
                {
                    color = Color.aliceBlue,
                    localToWorldMatrix = proxy.localToWorldMatrix,
                    bounds = bounds,
                }.Schedule(dependsOn);
            }

            return dependsOn;
        }

        protected JobHandle WriteTo(int i, JobHandle dependsOn)
        {
            return new RWToJob()
            {
                bounds = bounds,
                boundsMax = boundsMax[i],
                boundsMin = boundsMin[i],
                index = i,
            }.Schedule(dependsOn);
        }

        protected JobHandle CalculateBounds(int i, JobHandle dependsOn)
        {
            return proxy
                 .boundsRecalculation
                 .CalculateBounds(proxy.animatedVertices, indices[i], boundsMin[i], boundsMax[i], dependsOn);
        }

        protected JobHandle DrawMesh(int i, JobHandle dependsOn)
        {
            return new DrawJob()
            {
                color = roots[i].color,
                localToWorldMatrix = proxy.localToWorldMatrix,
                position = proxy.transform.position,
                vertices = proxy.animatedVertices,
                indices = indicesSet[i],
                triangles = proxy.triangles,
            }.Schedule(dependsOn);
        }

        public JobHandle MultiPass(JobHandle dependsOn, int length,Func<int, JobHandle,JobHandle> Action)
        {
            NativeArray<JobHandle> jobs = new NativeArray<JobHandle> (length, Allocator.TempJob);
            for (int i = 0; i < length; i++)
            {
                jobs[i] = Action.Invoke(i, dependsOn);
            }
            return jobs.Dispose(JobHandle.CombineDependencies(jobs));
        }

        [Serializable]
        public class Root
        {
            public Color color;
            public Transform root;
            public float maxDeformation = 100;

            public static implicit operator Transform(Root root)
            {
                return root.root;
            }
            public static implicit operator Color(Root root)
            {
                return root.color;
            }
        }
        [Serializable]
        public class Transforms
        {
            public Transform[] transforms;
        }

        [BurstCompile]
        public struct RWToJob : IJob
        {
            [ReadOnly] public NativeArray<float> boundsMin;
            [ReadOnly] public NativeArray<float> boundsMax;
            [ReadOnly] public int index;
            [WriteOnly, NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction] 
            public NativeArray<Bounds> bounds;

            public void Execute()
            {
                Vector3 min = new Vector3(boundsMin[0], boundsMin[1], boundsMin[2]);
                Vector3 max = new Vector3(boundsMax[0], boundsMax[1], boundsMax[2]);
                Bounds b = new Bounds();
                b.SetMinMax(min, max);
                bounds[index] = b;
            }
        }

        [BurstCompile]
        public struct DrawJob : IJob
        {
            [ReadOnly] public Matrix4x4 localToWorldMatrix;
            [ReadOnly] public float3 position;
            [ReadOnly] public Color color;
            [ReadOnly] public NativeArray<int3> triangles;
            [ReadOnly] public NativeHashSet<int> indices;
            [ReadOnly] public NativeArray<float3> vertices;

            public void Execute()
            {
                for (int i = 0; i < triangles.Length; i+=3)
                {
                    int v0 = triangles[i].x;
                    int v1 = triangles[i].y;
                    int v2 = triangles[i].z;

                    if(indices.Contains(v0) && indices.Contains(v1) && indices.Contains(v2))
                    {
                        float3 p0 = localToWorldMatrix.MultiplyPoint(vertices[v0]);
                        float3 p1 = localToWorldMatrix.MultiplyPoint(vertices[v1]);
                        float3 p2 = localToWorldMatrix.MultiplyPoint(vertices[v2]);

                        p0 += position;
                        p1 += position;
                        p2 += position;

                        Draw(p0, p1, p2);
                    }
                }
            }
            public void Draw(float3 v0, float3 v1, float3 v2)
            {
                D.raw(new Shape.Line(v0, v1), color);
                D.raw(new Shape.Line(v1, v2), color);
                D.raw(new Shape.Line(v2, v1), color);
            }
        }

        [BurstCompile]
        public struct DrawBoundsJob : IJob
        {
            [ReadOnly] public Matrix4x4 localToWorldMatrix;
            [ReadOnly] public Color color;
            [ReadOnly] public NativeArray<Bounds> bounds;
            public void Execute()
            {
                for (int i = 0; i < bounds.Length; i++)
                {
                    Vector3 min = localToWorldMatrix.MultiplyPoint3x4(bounds[i].min);
                    Vector3 max = localToWorldMatrix.MultiplyPoint3x4(bounds[i].max);

                    Bounds b = new Bounds();
                    b.SetMinMax(min, max);
                    D.raw(new Shape.Box(b), color);
                }
            }
        }
    }
}