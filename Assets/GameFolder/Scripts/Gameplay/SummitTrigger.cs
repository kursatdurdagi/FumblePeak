using Mirror;
using UnityEngine;

namespace FumblePeak
{
    /// <summary>Zirve. (Bölüm sonu)</summary>
    public class SummitTrigger : MonoBehaviour
    {
        bool done;
        void OnTriggerEnter(Collider other)
        {
            if (done || !NetworkServer.active) return;
            var body = ClimberBody.Instance;
            if (body == null || other.attachedRigidbody != body.torso) return;
            done = true;
            body.ServerSummit();
        }
    }
}
