namespace Arawn.EnemyMasses.Runtime.Navigation
{
    /// <summary>
    /// Supported navigation/pathfinding systems.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The available options depend on which packages are installed:
    /// - Unity NavMesh is always available (built-in)
    /// - Flow Field is always available (Enemy Masses runtime)
    /// - A* Pathfinding options appear when com.arongranberg.astar is installed
    /// </para>
    /// <para>
    /// <b>Choosing a Navigation Type:</b>
    /// <list type="bullet">
    /// <item><b>UnityNavMesh:</b> Good default, works with Unity's NavMesh system</item>
    /// <item><b>FlowField:</b> Grid-based crowd steering for large group movement</item>
    /// <item><b>AStarAIPath:</b> Best for grid graphs, point graphs, or when you need custom movement</item>
    /// <item><b>AStarRichAI:</b> Best for navmesh/recast graphs, handles corners smoothly</item>
    /// <item><b>AStarAILerp:</b> Exact path following without physics (good for cutscenes/scripted movement)</item>
    /// <item><b>AStarRVO:</b> A* movement with local avoidance using RVOController (AIPath/RichAI)</item>
    /// </list>
    /// </para>
    /// </remarks>
    public enum NavigationType
    {
        /// <summary>
        /// Unity's built-in NavMeshAgent.
        /// Good default choice, works with Unity's NavMesh baking system.
        /// </summary>
        UnityNavMesh = 0,

        /// <summary>
        /// Flow-field driven movement using grid cost/integration/vector fields.
        /// Designed for large crowd steering and shared destination guidance.
        /// </summary>
        FlowField = 20,
        
#if ASTAR_EXISTS
        /// <summary>
        /// A* Pathfinding Project's AIPath component.
        /// Works with all graph types. Good for grid graphs and point graphs.
        /// Uses CharacterController or Rigidbody for movement.
        /// </summary>
        AStarAIPath = 10,
        
        /// <summary>
        /// A* Pathfinding Project's RichAI component.
        /// Specifically designed for navmesh/recast graphs.
        /// Handles corners and off-mesh links more smoothly than AIPath.
        /// </summary>
        AStarRichAI = 11,
        
        /// <summary>
        /// A* Pathfinding Project's AILerp component.
        /// Follows paths exactly without physics simulation.
        /// Good for scripted movement or when exact path following is needed.
        /// Note: Does not support local avoidance or gravity.
        /// </summary>
        AStarAILerp = 12,
        
        /// <summary>
        /// A* Pathfinding Project movement with local avoidance using RVOController.
        /// Works with AIPath or RichAI and adds RVO-based crowd avoidance.
        /// Requires an RVOSimulator in the scene.
        /// </summary>
        AStarRVO = 13,
#endif

        /// <summary>
        /// 3D volumetric navigation for flying vehicles, spaceships, and persistent flyers.
        /// Uses boids, waypoint, or direct steering — no ground surface required.
        /// </summary>
        Flight3D = 30
    }
    
    /// <summary>
    /// Extension methods for NavigationType.
    /// </summary>
    public static class NavigationTypeExtensions
    {
        /// <summary>
        /// Gets a human-readable display name for the navigation type.
        /// </summary>
        public static string GetDisplayName(this NavigationType type)
        {
            // Handle known types
            switch ((int)type)
            {
                case 0: return "Unity NavMesh";
                case 20: return "Flow Field";
#if ASTAR_EXISTS
                case 10: return "A* AIPath (Grid, Point)";
                case 11: return "A* RichAI (Navmesh)";
                case 12: return "A* AILerp (Exact)";
                case 13: return "A* RVO (Local Avoidance)";
#else
                // When A* is not available, show that these types are unavailable
                case 10: return "A* AIPath (Unavailable)";
                case 11: return "A* RichAI (Unavailable)";
                case 12: return "A* AILerp (Unavailable)";
                case 13: return "A* RVO (Unavailable)";
#endif
                case 30: return "Flight 3D (Volumetric)";
                default: return $"Unknown ({(int)type})";
            }
        }
        
        /// <summary>
        /// Gets a tooltip description for the navigation type.
        /// </summary>
        public static string GetTooltip(this NavigationType type)
        {
            // Handle known types by int value to avoid issues when A* types aren't defined
            switch ((int)type)
            {
                case 0: return "Unity's built-in NavMeshAgent. Good default choice.";
                case 20: return "Flow-field navigation for large crowd steering. Uses shared grid direction sampling.";
#if ASTAR_EXISTS
                case 10: return "A* Pathfinding AIPath. Best for grid/point graphs. Uses CharacterController or Rigidbody.";
                case 11: return "A* Pathfinding RichAI. Best for navmesh graphs. Smooth corner handling.";
                case 12: return "A* Pathfinding AILerp. Exact path following. No physics/avoidance.";
                case 13: return "A* Pathfinding with RVO local avoidance. Uses AIPath/RichAI + RVOController. Requires RVOSimulator.";
#else
                case 10:
                case 11:
                case 12:
                case 13: return "A* Pathfinding is not installed. Please install A* Pathfinding Project or select Unity NavMesh.";
#endif
                case 30: return "3D volumetric navigation for flying vehicles and persistent flyers. Uses boids/direct steering.";
                default: return "";
            }
        }
        
        /// <summary>
        /// Whether this navigation type requires A* Pathfinding Project.
        /// </summary>
        public static bool RequiresAStar(this NavigationType type)
        {
            // Check by int value to handle cases where A* enum values aren't defined
            int value = (int)type;
            return value == 10 || value == 11 || value == 12 || value == 13;
        }
        
        /// <summary>
        /// Checks if the current navigation type is available (A* types require A* to be installed).
        /// </summary>
        public static bool IsAvailable(this NavigationType type)
        {
            int value = (int)type;
            if (value == 0) return true; // Unity NavMesh always available
            if (value == 20) return true; // Flow Field always available
            if (value == 30) return true; // Flight 3D always available
            
#if ASTAR_EXISTS
            // A* types are available when A* is installed
            return value == 10 || value == 11 || value == 12 || value == 13;
#else
            // A* types are not available when A* is not installed
            return false;
#endif
        }
    }
}
