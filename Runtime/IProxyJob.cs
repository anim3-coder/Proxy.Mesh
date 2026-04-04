using System.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Proxy.Mesh
{
    public interface IProxyJob
    {
        public JobType type => JobType.Default;
        public JobHandle StartJob(JobHandle dependsOn);
        public void OnJobComplete();
    }
    public enum JobType
    {
        /// <summary>
        /// Базовая работа, начинается после всего последовательно с другими задачами
        /// </summary>
        Default,
        /// <summary>
        /// Паралельная работа, начинается после всего парарельно с другими задачами
        /// </summary>
        Parallel,
        /// <summary>
        /// Парарельная работа начинающаяся после скининга
        /// </summary>
        ParallelAfterSkinning
    }
}