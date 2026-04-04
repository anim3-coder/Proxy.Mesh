using System.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using System;

namespace Proxy.Mesh
{
    public class ProxyWaveProgation : MonoBehaviour, IProxyChild, IProxyJob, IDisposable
    {
        [Header("Wave Settings")]
        [SerializeField, Range(0.01f, 100f)] private float waveSpeed = 1f;
        [SerializeField, Range(0.01f, 50f)] private float waveDamping = 15f;
        [SerializeField, Range(0, 100)] private int maxWaveImpacts = 10;
        [SerializeField] private Rigidbody[] waveBodies;


        private List<WaveImpact> activeWaveImpacts = new List<WaveImpact>();
        private NativeArray<WaveImpact> nativeWaveImpacts;
        private ProxyMesh proxy;
        public bool IsInit => proxy != null && enabled;

        /// <summary>
        /// Добавляет волновое воздействие в указанной позиции
        /// </summary>
        /// <param name="worldPosition">Позиция воздействия в мировых координатах</param>
        /// <param name="intensity">Интенсивность волны</param>
        /// <param name="radius">Радиус воздействия</param>
        /// <param name="duration">Длительность волны</param>
        /// <param name="frequency">Частота волны</param>
        public void AddForce(Vector3 worldPosition, float intensity = 0.05f, float radius = 0.25f, float duration = 1f, float frequency = 20f)
        {
            WaveImpact newImpact = new WaveImpact(
                worldPosition,
                intensity,
                radius,
                duration,
                frequency
            );

            activeWaveImpacts.Add(newImpact);

            for (int i = 0; i < waveBodies.Length; i++)
                waveBodies[i].AddExplosionForce(intensity * 100, worldPosition, radius);

            if (activeWaveImpacts.Count > maxWaveImpacts)
            {
                activeWaveImpacts.RemoveAt(0);
            }
        }

        public void OnInit(ProxyMesh proxyMesh)
        {
            proxy = proxyMesh;
            nativeWaveImpacts = new NativeArray<WaveImpact>(maxWaveImpacts, Allocator.Persistent);
        }

        public void OnJobComplete()
        {
            UpdateWaveImpacts();
        }

        private void UpdateWaveImpacts()
        {
            float currentTime = Time.time;
            for (int i = activeWaveImpacts.Count - 1; i >= 0; i--)
            {
                if (currentTime - activeWaveImpacts[i].startTime > activeWaveImpacts[i].duration)
                {
                    activeWaveImpacts.RemoveAt(i);
                }
            }

            UpdateNativeWaveImpacts();
        }

        private void UpdateNativeWaveImpacts()
        {
            for (int i = 0; i < maxWaveImpacts; i++)
            {
                nativeWaveImpacts[i] = new WaveImpact { isValid = false };
            }

            // Copy active impacts to native array
            int count = math.min(activeWaveImpacts.Count, maxWaveImpacts);
            for (int i = 0; i < count; i++)
            {
                nativeWaveImpacts[i] = activeWaveImpacts[i];
            }
        }
        public void Dispose() => OnShutdown(proxy);
        public void OnShutdown(ProxyMesh proxyMesh)
        {
            if(nativeWaveImpacts.IsCreated) nativeWaveImpacts.Dispose();
        }

        public JobHandle StartJob(JobHandle dependsOn)
        {
            if (IsInit == false || activeWaveImpacts.Count == 0) return dependsOn;
            return new WavePropagationJob() { 
                animatedVertices = proxy.animatedVertices,
                animatedNormals = proxy.animatedVertices,
                waveImpacts = nativeWaveImpacts,
                currentTime = Time.time,
                waveSpeed = waveSpeed,
                waveDamping = waveDamping,
                worldToLocalMatrix = transform.worldToLocalMatrix,
                updateIndices = proxy.normalsRecalculation.updateIndices.AsParallelWriter()
            }.Schedule(proxy.vertexCount, math.max(1, proxy.vertexCount / 100), dependsOn);
        }
        public struct WaveImpact
        {
            public Vector3 position;
            public float startTime;
            public float intensity;
            public float radius;
            public float duration;
            public float frequency;
            public bool isValid;

            public WaveImpact(Vector3 pos, float intens, float rad, float dur, float freq)
            {
                position = pos;
                startTime = Time.time;
                intensity = intens;
                radius = rad;
                duration = dur;
                frequency = freq;
                isValid = true;
            }
        }
        [BurstCompile]
        protected struct WavePropagationJob : IJobParallelFor
        {
            [NativeDisableParallelForRestriction, NativeDisableContainerSafetyRestriction] public NativeArray<float3> animatedVertices;
            [WriteOnly] public NativeParallelHashSet<int>.ParallelWriter updateIndices;
            [ReadOnly] public NativeArray<float3> animatedNormals;
            [ReadOnly] public NativeArray<WaveImpact> waveImpacts;
            [ReadOnly] public Matrix4x4 worldToLocalMatrix;
            [ReadOnly] public float currentTime;
            [ReadOnly] public float waveSpeed;
            [ReadOnly] public float waveDamping;

            public void Execute(int vertexIndex)
            {
                if (vertexIndex < 0 || vertexIndex >= animatedVertices.Length)
                    return;

                float3 animatedVertex = animatedVertices[vertexIndex];
                float3 animatedNormal = animatedNormals[vertexIndex];

                // Преобразуем в мировые координаты используя уже анимированные вершины
                float3 worldVertex = worldToLocalMatrix.inverse.MultiplyPoint3x4(animatedVertex);
                float3 worldNormal = worldToLocalMatrix.inverse.MultiplyVector(animatedNormal).normalized;

                float3 totalDisplacement = float3.zero;

                // Process each wave impact
                for (int i = 0; i < waveImpacts.Length; i++)
                {
                    WaveImpact impact = waveImpacts[i];
                    if (!impact.isValid) continue;

                    // Calculate wave effect for this vertex
                    float3 displacement = CalculateWaveDisplacement(worldVertex, worldNormal, impact, currentTime);
                    totalDisplacement += displacement;
                }

                // Apply displacement along the normal
                if (math.length(totalDisplacement) > 0.001f)
                {
                    worldVertex += totalDisplacement;
                    updateIndices.Add(vertexIndex);
                    animatedVertices[vertexIndex] = worldToLocalMatrix.MultiplyPoint3x4(worldVertex);
                }
                else
                {
                    animatedVertices[vertexIndex] = animatedVertex;
                }
            }

            private float3 CalculateWaveDisplacement(float3 worldVertex, float3 worldNormal, WaveImpact impact, float currentTime)
            {
                // Calculate distance from impact center using animated vertices
                float distance = math.distance(worldVertex, impact.position);

                // If beyond radius, no effect
                float effectiveRadius = impact.radius;
                if (distance > effectiveRadius)
                    return float3.zero;

                // Calculate wave propagation time
                float waveTravelTime = distance / waveSpeed;
                float timeSinceImpact = currentTime - impact.startTime;
                float waveArrivalTime = waveTravelTime;

                // Wave hasn't reached this vertex yet
                if (timeSinceImpact < waveArrivalTime)
                    return float3.zero;

                // Calculate wave parameters
                float timeInWave = timeSinceImpact - waveArrivalTime;
                float impactDuration = impact.duration;

                // Normalized time (0 to 1) over wave duration
                float normalizedTime = timeInWave / impactDuration;

                // Wave is finished
                if (normalizedTime > 1f)
                    return float3.zero;

                // Calculate distance falloff (1 at center, 0 at edge)
                float distanceFalloff = 1f - (distance / effectiveRadius);

                // Calculate time falloff (fade out over duration)
                float timeFalloff = 1f - normalizedTime;

                // Calculate damping effect
                float damping = math.exp(-waveDamping * normalizedTime);

                // Calculate wave intensity
                float effectiveIntensity = impact.intensity;
                float effectiveFrequency = impact.frequency;

                // Sine wave with frequency and phase based on distance
                float phase = (effectiveFrequency * timeInWave) - (distance * 0.5f);
                float sineWave = math.sin(phase * 2f * math.PI);

                // Combine all factors
                float waveAmplitude = effectiveIntensity * distanceFalloff * timeFalloff * damping * sineWave;

                // Return displacement along the normal (not radial direction)
                return worldNormal * waveAmplitude;
            }
        }
    }
}