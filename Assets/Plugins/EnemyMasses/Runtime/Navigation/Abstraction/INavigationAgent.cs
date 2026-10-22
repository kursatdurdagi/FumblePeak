using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Navigation.Abstraction
{
    /// <summary>
    /// Abstraction layer for navigation agents, allowing Enemy Masses to work with
    /// different pathfinding systems (Unity NavMesh, A* Pathfinding Project, etc.)
    /// </summary>
    /// <remarks>
    /// <para>
    /// This interface provides a unified API for navigation that supports:
    /// - Unity's built-in NavMeshAgent
    /// - A* Pathfinding Project (AIPath, RichAI, AILerp, RVO)
    /// - Future navigation systems
    /// </para>
    /// <para>
    /// Key design consideration: The <see cref="Position"/> property and <see cref="UpdatePosition"/>
    /// flag are critical for the fake physics/NavMesh offset system. During knockback impulses,
    /// Enemy Masses takes control of positioning by setting UpdatePosition=false and manually
    /// updating Position for ground-level movement while applying visual offsets to the Transform.
    /// </para>
    /// </remarks>
    public interface INavigationAgent
    {
        // ════════════════════════════════════════════════════════════════════════════════════════
        // IDENTITY
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// The type of navigation system this agent uses.
        /// </summary>
        NavigationType NavigationType { get; }
        
        /// <summary>
        /// Backend capability metadata used by higher-level systems to avoid
        /// hard-coded NavigationType branching.
        /// </summary>
        NavigationCapabilities Capabilities { get; }
        
        /// <summary>
        /// The Transform of the GameObject that has the navigation component.
        /// Used for local coordinate transformations and visual positioning.
        /// </summary>
        Transform Transform { get; }
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // POSITION & MOVEMENT - Critical for Fake Physics System
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// The ground-level position used by the navigation system.
        /// </summary>
        /// <remarks>
        /// <para>
        /// For Unity NavMesh, this maps to NavMeshAgent.nextPosition.
        /// For A* Pathfinding, this maps to IAstarAI.position.
        /// </para>
        /// <para>
        /// <b>Critical for Fake Physics:</b> During knockback impulses, the visual Transform.position
        /// includes a vertical offset (the "lift"), but this Position property stays at ground level
        /// so pathfinding continues to work correctly.
        /// </para>
        /// </remarks>
        Vector3 Position { get; set; }
        
        /// <summary>
        /// Whether the navigation system should automatically update Transform.position.
        /// </summary>
        /// <remarks>
        /// <b>Critical for Fake Physics:</b> Set to false during knockback impulses so
        /// Enemy Masses can control the Transform position directly while keeping the
        /// navigation position at ground level.
        /// </remarks>
        bool UpdatePosition { get; set; }
        
        /// <summary>
        /// Whether the navigation system should automatically update Transform.rotation.
        /// </summary>
        bool UpdateRotation { get; set; }
        
        /// <summary>Current velocity the agent is moving with.</summary>
        Vector3 Velocity { get; set; }
        
        /// <summary>
        /// Velocity the agent wants to move with (may differ from actual due to obstacles).
        /// </summary>
        Vector3 DesiredVelocity { get; }
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // SPEED & MOVEMENT PARAMETERS
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>Maximum movement speed.</summary>
        float Speed { get; set; }
        
        /// <summary>Angular/rotation speed in degrees per second.</summary>
        float AngularSpeed { get; set; }
        
        /// <summary>Acceleration rate.</summary>
        float Acceleration { get; set; }
        
        /// <summary>Distance from destination at which the agent will stop.</summary>
        float StoppingDistance { get; set; }
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // AGENT DIMENSIONS
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>Agent height for navigation.</summary>
        float Height { get; set; }
        
        /// <summary>Agent radius for navigation and avoidance.</summary>
        float Radius { get; set; }
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // PATH STATUS
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>Approximate remaining distance along the current path.</summary>
        float RemainingDistance { get; }
        
        /// <summary>Whether the agent currently has a valid path.</summary>
        bool HasPath { get; }
        
        /// <summary>Whether a path calculation is currently in progress.</summary>
        bool PathPending { get; }
        
        /// <summary>
        /// Whether the agent is on a navigable surface.
        /// For NavMesh: isOnNavMesh. For A*: typically checks if nearest node is walkable.
        /// </summary>
        bool IsOnNavMesh { get; }
        
        /// <summary>The current destination the agent is moving towards.</summary>
        Vector3 Destination { get; set; }
        
        /// <summary>
        /// The next point on the path the agent is steering towards.
        /// </summary>
        Vector3 SteeringTarget { get; }
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // CONTROL FLAGS
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Whether the agent should stop moving.
        /// </summary>
        bool IsStopped { get; set; }
        
        /// <summary>
        /// Whether the agent is enabled.
        /// </summary>
        bool Enabled { get; set; }
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // OFF-MESH LINKS
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>Whether the agent is currently traversing an off-mesh link.</summary>
        bool IsOnOffMeshLink { get; }
        
        /// <summary>Complete the current off-mesh link traversal.</summary>
        void CompleteOffMeshLink();
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // COMMANDS
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Set a new destination for the agent to navigate to.
        /// </summary>
        /// <param name="target">World position to navigate to.</param>
        /// <returns>True if destination was set successfully.</returns>
        bool SetDestination(Vector3 target);
        
        /// <summary>
        /// Immediately teleport the agent to a new position.
        /// </summary>
        /// <param name="position">World position to warp to.</param>
        /// <returns>True if warp was successful.</returns>
        bool Warp(Vector3 position);
        
        /// <summary>
        /// Stop the agent and clear its current path.
        /// </summary>
        void Stop();
        
        /// <summary>
        /// Resume movement after being stopped.
        /// </summary>
        void Resume();
        
        /// <summary>
        /// Clear the current path without stopping the agent.
        /// </summary>
        void ResetPath();
        
        /// <summary>
        /// Apply a movement offset (for local avoidance or steering).
        /// </summary>
        void Move(Vector3 offset);
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // QUERIES
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Sample a valid position on the navigation surface near the given position.
        /// </summary>
        /// <param name="position">Position to sample near.</param>
        /// <param name="result">The sampled valid position.</param>
        /// <param name="maxDistance">Maximum search distance.</param>
        /// <returns>True if a valid position was found.</returns>
        bool SamplePosition(Vector3 position, out Vector3 result, float maxDistance);
        
        /// <summary>
        /// Cast a ray on the navigation surface to check for obstacles.
        /// </summary>
        /// <param name="targetPosition">Target position to raycast to.</param>
        /// <param name="hitPosition">Position where ray hit an obstacle (if any).</param>
        /// <returns>True if ray hit an obstacle before reaching target.</returns>
        bool Raycast(Vector3 targetPosition, out Vector3 hitPosition);
        
        // ════════════════════════════════════════════════════════════════════════════════════════
        // UNDERLYING AGENT ACCESS
        // ════════════════════════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Get the underlying navigation agent as a specific type.
        /// </summary>
        /// <typeparam name="T">Type to cast to (NavMeshAgent, AIPath, etc.)</typeparam>
        /// <returns>The underlying agent cast to T, or null if incompatible.</returns>
        T GetUnderlyingAgent<T>() where T : class;
    }
}
