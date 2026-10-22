using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Marker component to identify colliders that were auto-added by the system
    /// when collider-based hit detection is enabled. Used for safe removal when
    /// switching back to distance-based hit detection.
    /// </summary>
    [DisallowMultipleComponent]
    public class AutoAddedColliderMarker : MonoBehaviour
    {
    }
}
