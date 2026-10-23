using Mirror;
using UnityEngine;

namespace FumblePeak
{
    /// <summary>Başlangıç Vadisi'nin dev esnek mantarı.</summary>
    public class BouncePad : MonoBehaviour
    {
        public float upSpeed = 9f;
        float lastBounce;

        void OnCollisionEnter(Collision c) => TryBounce(c.rigidbody);
        void OnTriggerEnter(Collider c) => TryBounce(c.attachedRigidbody);

        void TryBounce(Rigidbody rb)
        {
            if (!NetworkServer.active || rb == null) return;
            var body = ClimberBody.Instance;
            if (body == null || Time.time - lastBounce < 0.6f) return;
            if (rb != body.torso && System.Array.IndexOf(body.effectors, rb) < 0) return;
            lastBounce = Time.time;
            body.ServerBounce(transform.up * upSpeed);
        }
    }
}
