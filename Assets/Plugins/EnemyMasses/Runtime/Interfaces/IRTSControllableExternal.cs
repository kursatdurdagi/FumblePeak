using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Interface for external units (non-CrowdAgent) that can be selected and commanded 
    /// via the EnemyMassesRTSController. Implement this on any MonoBehaviour to make it
    /// selectable, commandable, and visible on the minimap/fog of war.
    /// </summary>
    public interface IRTSControllableExternal
    {
        /// <summary>
        /// Unique identifier for this controllable (auto-assigned on registration).
        /// </summary>
        int ExternalId { get; set; }

        /// <summary>
        /// The GameObject this controllable is attached to.
        /// </summary>
        GameObject GameObject { get; }

        /// <summary>
        /// Transform used for position, selection, and minimap rendering.
        /// </summary>
        Transform Transform { get; }

        /// <summary>
        /// Current world position of this controllable.
        /// </summary>
        Vector3 Position { get; }

        /// <summary>
        /// Faction name for grouping and player-control checks.
        /// </summary>
        string FactionName { get; }

        /// <summary>
        /// Color used for minimap icon and selection indicator tinting.
        /// </summary>
        Color FactionColor { get; }

        /// <summary>
        /// Radius in world units for revealing fog of war.
        /// </summary>
        float FogRevealRadius { get; }

        /// <summary>
        /// Whether this unit is alive/active and should be selectable.
        /// </summary>
        bool IsAlive { get; }

        /// <summary>
        /// Whether this unit is currently selected.
        /// </summary>
        bool IsSelected { get; set; }

        /// <summary>
        /// Current combat stance.
        /// </summary>
        CombatStance CombatStance { get; set; }

        /// <summary>
        /// Called when RTS controller issues a move command.
        /// </summary>
        void MoveTo(Vector3 destination);

        /// <summary>
        /// Called when RTS controller issues a stop command.
        /// </summary>
        void Stop();

        /// <summary>
        /// Called when RTS controller issues an attack command on a position.
        /// </summary>
        void AttackPosition(Vector3 position);

        /// <summary>
        /// Called when RTS controller issues an attack command on a target.
        /// </summary>
        void AttackTarget(Transform target);

        /// <summary>
        /// Called when selection state changes.
        /// </summary>
        void OnSelectionChanged(bool selected);

        /// <summary>
        /// Called when combat stance changes.
        /// </summary>
        void OnStanceChanged(CombatStance newStance);
    }
}
