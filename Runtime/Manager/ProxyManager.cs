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

        static volatile bool isPlaying = false;

        public static event System.Action OnCompleteJob;

        public static List<ProxyMesh> proxies = new List<ProxyMesh>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ReloadDomain()
        {
#if UNITY_EDITOR
            // スクリプトコンパイル開始コールバック
            CompilationPipeline.compilationStarted += OnStarted;
#endif

            InitCustomGameLoop();

            isPlaying = true;
        }

        public static void InitCustomGameLoop()
        {
            PlayerLoopSystem playerLoop = PlayerLoop.GetCurrentPlayerLoop();

            if (CheckRegist(ref playerLoop))
            {
                return;
            }

            SetCustomGameLoop(ref playerLoop);

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
        }

        static JobHandle jobHandle = default;
        static bool jobCompleted;

        public static void JobComplete()
        {
            jobCompleted = true;
            jobHandle.Complete();
        }

        static void FixedUpdate()
        {
            foreach(ProxyMesh proxy in proxies)
                proxy.OnFixedUpdate();
        }

        static void Update()
        {
            if (jobHandle.IsCompleted)
            {
                jobCompleted = true;
                jobHandle.Complete();
            }
            foreach (ProxyMesh proxy in proxies)
                proxy.OnUpdate(jobCompleted);
        }

        static void LateUpdate()
        {
            foreach (ProxyMesh proxy in proxies)
                proxy.OnLateUpdate(jobCompleted);
            if (jobCompleted && jobHandle.IsCompleted)
            {
                foreach (ProxyMesh proxy in proxies)
                {
                    jobHandle = proxy.StartNewJob(jobHandle);
                }
                jobCompleted = false;
            }
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

        static void OnStarted(object obj)
        {
            //Debug.Log($"スクリプトコンパイル開始");
            isPlaying = false;
            //Dispose();

        }
    }
}