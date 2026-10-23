using Mirror;
using UnityEngine;

namespace FumblePeak
{
    /// <summary>Tırmanıcının başlangıç noktası.</summary>
    public class ClimberSpawn : MonoBehaviour
    {
        void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, 0.3f);
        }
    }
}
