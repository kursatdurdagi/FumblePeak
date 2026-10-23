using Mirror;
using UnityEngine;

namespace FumblePeak
{
    /// <summary>Kamp ateşi. Gövde girince yeni dönüş noktası olur. (GDD 4.2)</summary>
    public class Checkpoint : MonoBehaviour
    {
        public Transform respawnPoint;

        void OnTriggerEnter(Collider other)
        {
            if (!NetworkServer.active) return;
            var body = ClimberBody.Instance;
            if (body == null || other.attachedRigidbody != body.torso) return;
            body.ServerSetCheckpoint(respawnPoint != null ? respawnPoint.position : transform.position + Vector3.up);
        }
    }
}
