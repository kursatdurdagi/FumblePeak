using UnityEngine;
using UnityEngine.AI;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Allows external units to participate in Enemy Masses RTS selection and move commands
    /// without requiring them to be part of an EnemyCrowd.
    /// Use IRTSExternalNavigationSelectable to expose INavigationAgent when available.
    /// </summary>
    public interface IRTSExternalSelectable
    {
        /// <summary>Legacy NavMeshAgent movement reference used as a fallback path.</summary>
        NavMeshAgent NavAgent { get; }

        /// <summary>Transform used for selection raycasts and indicator placement.</summary>
        Transform Transform { get; }

        /// <summary>Optional per-unit offset applied on top of the controller's indicatorOffset.</summary>
        Vector3 SelectionIndicatorOffset { get; }

        /// <summary>Whether the unit is currently eligible for selection.</summary>
        bool IsSelectable { get; }

        /// <summary>Whether the unit is alive/valid (prevents selection of dead/disabled units).</summary>
        bool IsAlive { get; }

        /// <summary>
        /// When true, bypasses player faction filtering so factionless units (e.g., GC2 players) can be controlled.
        /// </summary>
        bool SkipFactionFilter { get; }

        /// <summary>Optional override for the selection indicator prefab.</summary>
        GameObject SelectionIndicatorOverride { get; }

        /// <summary>
        /// Optional audio clip played when this unit is selected via the RTS controller.
        /// Return null to fall back to normal faction-based selection sounds.
        /// </summary>
        AudioClip SelectionAudioClip => null;

        /// <summary>
        /// Called when the RTS controller issues a move command; implement movement using your own system if needed.
        /// </summary>
        /// <param name="destination">World-space destination already validated against NavMesh.</param>
        void SetDestination(Vector3 destination);
    }
}
