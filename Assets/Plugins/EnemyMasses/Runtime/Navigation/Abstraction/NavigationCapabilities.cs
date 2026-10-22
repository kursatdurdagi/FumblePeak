namespace Arawn.EnemyMasses.Runtime.Navigation.Abstraction
{
    /// <summary>
    /// Describes backend-specific navigation capabilities so higher-level systems
    /// can make decisions without hard-coding NavigationType checks.
    /// </summary>
    public readonly struct NavigationCapabilities
    {
        /// <summary>
        /// True when control operations should be gated by IsOnNavMesh/graph.
        /// Unity NavMesh requires this, while other backends may not.
        /// </summary>
        public bool RequiresSurfaceValidationForControl { get; }

        /// <summary>
        /// True when the backend supports path-pending state.
        /// </summary>
        public bool SupportsPathPending { get; }

        /// <summary>
        /// True when the backend supports off-mesh link traversal state.
        /// </summary>
        public bool SupportsOffMeshLinks { get; }

        /// <summary>
        /// True when the backend can use navmesh offset impulse style knockback.
        /// </summary>
        public bool SupportsNavmeshOffsetImpulse { get; }

        /// <summary>
        /// True when the backend supports move(delta)-based knockback handling.
        /// </summary>
        public bool SupportsMoveBasedKnockback { get; }

        /// <summary>
        /// True when callers should throttle frequent destination updates to avoid
        /// canceling in-flight requests.
        /// </summary>
        public bool ShouldThrottleFrequentDestinationUpdates { get; }

        /// <summary>
        /// True when backend-specific per-agent avoidance tuning is supported.
        /// </summary>
        public bool SupportsPerAgentAvoidanceTuning { get; }

        public NavigationCapabilities(
            bool requiresSurfaceValidationForControl,
            bool supportsPathPending,
            bool supportsOffMeshLinks,
            bool supportsNavmeshOffsetImpulse,
            bool supportsMoveBasedKnockback,
            bool shouldThrottleFrequentDestinationUpdates,
            bool supportsPerAgentAvoidanceTuning)
        {
            RequiresSurfaceValidationForControl = requiresSurfaceValidationForControl;
            SupportsPathPending = supportsPathPending;
            SupportsOffMeshLinks = supportsOffMeshLinks;
            SupportsNavmeshOffsetImpulse = supportsNavmeshOffsetImpulse;
            SupportsMoveBasedKnockback = supportsMoveBasedKnockback;
            ShouldThrottleFrequentDestinationUpdates = shouldThrottleFrequentDestinationUpdates;
            SupportsPerAgentAvoidanceTuning = supportsPerAgentAvoidanceTuning;
        }

        /// <summary>
        /// Capability profile for Unity NavMeshAgent.
        /// </summary>
        public static NavigationCapabilities UnityNavMesh => new NavigationCapabilities(
            requiresSurfaceValidationForControl: true,
            supportsPathPending: true,
            supportsOffMeshLinks: true,
            supportsNavmeshOffsetImpulse: true,
            supportsMoveBasedKnockback: false,
            shouldThrottleFrequentDestinationUpdates: false,
            supportsPerAgentAvoidanceTuning: true);

        /// <summary>
        /// Capability profile for A* adapters (AIPath, RichAI, AILerp, RVO).
        /// </summary>
        public static NavigationCapabilities AStar => new NavigationCapabilities(
            requiresSurfaceValidationForControl: false,
            supportsPathPending: true,
            supportsOffMeshLinks: false,
            supportsNavmeshOffsetImpulse: false,
            supportsMoveBasedKnockback: true,
            shouldThrottleFrequentDestinationUpdates: true,
            supportsPerAgentAvoidanceTuning: false);

        /// <summary>
        /// Capability profile for FlowField adapter.
        /// </summary>
        public static NavigationCapabilities FlowField => new NavigationCapabilities(
            requiresSurfaceValidationForControl: false,
            supportsPathPending: false,
            supportsOffMeshLinks: false,
            supportsNavmeshOffsetImpulse: false,
            supportsMoveBasedKnockback: true,
            shouldThrottleFrequentDestinationUpdates: false,
            supportsPerAgentAvoidanceTuning: false);

        /// <summary>
        /// Capability profile for Flight3D volumetric navigation.
        /// </summary>
        public static NavigationCapabilities Flight3D => new NavigationCapabilities(
            requiresSurfaceValidationForControl: false,
            supportsPathPending: false,
            supportsOffMeshLinks: false,
            supportsNavmeshOffsetImpulse: false,
            supportsMoveBasedKnockback: true,
            shouldThrottleFrequentDestinationUpdates: false,
            supportsPerAgentAvoidanceTuning: false);
    }
}
