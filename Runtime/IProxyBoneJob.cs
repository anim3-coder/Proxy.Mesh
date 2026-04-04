using System.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Proxy.Mesh
{
    /// <summary>
    /// В отличии от IProxyJob выполняется до деформации вершин
    /// </summary>
    public interface IProxyBoneJob
    {
        public JobHandle StartBoneJob(JobHandle dependsOn);
        public void OnJobComplete();
    }
}