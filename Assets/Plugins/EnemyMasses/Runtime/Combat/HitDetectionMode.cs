namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Global hit detection mode selector.
    /// Distance: uses virtual hitboxes/math-based distance checks.
    /// Collider: uses Unity colliders/trigger-based hits.
    /// </summary>
    public enum HitDetectionMode
    {
        Distance = 0,
        Collider = 1
    }
}
