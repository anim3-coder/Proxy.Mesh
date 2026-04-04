using System.Collections;
using UnityEngine;

namespace Proxy.Mesh
{
    public class ProxyRegistrationRigidbody : MonoBehaviour
    {
        [field: SerializeField] public new Rigidbody rigidbody { get; private set; }
        [field: SerializeField] public new ProxyRegistrationCollider collider { get; private set; }

        private void Reset()
        {
            collider = GetComponent<ProxyRegistrationCollider>();
            rigidbody = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            rigidbody.MovePosition(rigidbody.position + collider.outPenetration);
        }
    }
}