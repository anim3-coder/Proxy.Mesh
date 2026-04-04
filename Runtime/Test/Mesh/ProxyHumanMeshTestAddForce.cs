using System.Collections;
using TriInspector;
using UnityEngine;

namespace Proxy.Mesh
{
    public class ProxyHumanMeshTestAddForce : MonoBehaviour
    {
        [SerializeField] private ProxyWaveProgation human;
        [SerializeField] private float force = 0.05f;
        [SerializeField] private float radius = 0.2f;
        [SerializeField] private float duration = 1;
        [SerializeField] private float frequency = 20;
        private void Reset()
        {
            human = FindAnyObjectByType<ProxyWaveProgation>();
        }
        [Button("Test")]
        private void Test()
        {
            human.AddForce(transform.position, force, radius, duration, frequency);
        }
    }
}