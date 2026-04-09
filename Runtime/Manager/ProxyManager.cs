using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Jobs;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Scripting;

[assembly: AlwaysLinkAssembly]
namespace Proxy.Mesh
{
    public static class ProxyManager
    {
        public delegate void UpdateMethod();

        private static IManager[] managers = new IManager[]
        {
            new ColliderManager(),
            new MeshManager(),
        };
        public static MeshManager MeshManager => managers[1] as MeshManager;
        public static ColliderManager ColliderManager => managers[0] as ColliderManager;

        public static event Action OnCompleteJob;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ReloadDomain()
        {
            InitCustomGameLoop();
        }

        public static void InitCustomGameLoop()
        {
            PlayerLoopSystem playerLoop = PlayerLoop.GetCurrentPlayerLoop();

            if (CheckRegist(ref playerLoop))
            {
                return;
            }

            SetCustomGameLoop(ref playerLoop);

            foreach(IManager manager in managers) 
                manager.OnInit();

            PlayerLoop.SetPlayerLoop(playerLoop);
        }

        static void SetCustomGameLoop(ref PlayerLoopSystem playerLoop)
        {
            PlayerLoopSystem FixedUpdate = new PlayerLoopSystem()
            {
                type = typeof(ProxyManager),
                updateDelegate = ProxyManager.FixedUpdate
            };
            AddPlayerLoop(FixedUpdate, ref playerLoop, "FixedUpdate", "ScriptRunBehaviourFixedUpdate");

            PlayerLoopSystem Update = new PlayerLoopSystem()
            {
                type = typeof(ProxyManager),
                updateDelegate = ProxyManager.Update
            };
            AddPlayerLoop(Update, ref playerLoop, "Update", "ScriptRunDelayedTasks");

            PlayerLoopSystem LateUpdate = new PlayerLoopSystem()
            {
                type = typeof(ProxyManager),
                updateDelegate = ProxyManager.LateUpdate
            };
            AddPlayerLoop(LateUpdate, ref playerLoop, "PostLateUpdate", "ScriptRunBehaviourLateUpdate");

            Application.quitting += OnDestoy;
        }

        static JobHandle jobHandle = default;
        public static bool jobCompleted { get; private set; }

        public static void JobComplete()
        {
            jobCompleted = true;
            jobHandle.Complete();
        }

        static void FixedUpdate()
        {
            foreach (IManager manager in managers)
                manager.FixedUpdate();
        }

        static void Update()
        {
            if (jobHandle.IsCompleted)
            {
                jobCompleted = true;
                jobHandle.Complete();
            }
            foreach(IManager manager in managers)
                manager.Update();
        }

        static void LateUpdate()
        {
            if (jobCompleted && jobHandle.IsCompleted)
            {
                foreach (IManager manager in managers)
                    jobHandle = manager.StartJob(jobHandle);
                jobCompleted = false;
            }
            foreach (IManager manager in managers)
                manager.LateUpdate();
        }

        static void OnDestoy()
        {
            JobComplete();

            Application.quitting -= OnDestoy;

            foreach (IManager manager in managers)
                manager.OnShutdown();
        }

        static void AddPlayerLoop(PlayerLoopSystem method, ref PlayerLoopSystem playerLoop, string categoryName, string systemName, bool last = false, bool before = false)
        {
            int sysIndex = Array.FindIndex(playerLoop.subSystemList, (s) => s.type.Name == categoryName);
            PlayerLoopSystem category = playerLoop.subSystemList[sysIndex];
            var systemList = new List<PlayerLoopSystem>(category.subSystemList);

            if (last)
            {
                systemList.Add(method);
            }
            else
            {
                int index = systemList.FindIndex(h => h.type.Name.Contains(systemName));
                if (before)
                    systemList.Insert(index, method);
                else
                    systemList.Insert(index + 1, method);
            }

            category.subSystemList = systemList.ToArray();
            playerLoop.subSystemList[sysIndex] = category;
        }

        static bool CheckRegist(ref PlayerLoopSystem playerLoop)
        {
            var t = typeof(ProxyManager);
            foreach (var subloop in playerLoop.subSystemList)
            {
                if (subloop.subSystemList != null && subloop.subSystemList.Any(x => x.type == t))
                {
                    return true;
                }
            }
            return false;
        }
    }

    public interface IManager
    {
        void OnInit();
        void OnShutdown();
        void FixedUpdate();
        void Update();
        void LateUpdate();
        JobHandle StartJob(JobHandle dependsOn);
    }
}