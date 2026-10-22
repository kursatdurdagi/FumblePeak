using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Arawn.EnemyMasses.Runtime.Navigation.Climbing
{
    /// <summary>
    /// How the agent moves while climbing
    /// </summary>
    public enum ClimbStyle
    {
        /// <summary>Smooth, constant velocity like an elevator - good for mechanical units</summary>
        Machine,
        /// <summary>Rhythmic stepping motion that mimics human/creature climbing with arm pulls</summary>
        Human,
        /// <summary>Custom - use only the AnimationCurve with no additional processing</summary>
        Custom
    }
    
    /// <summary>
    /// Creates NavMeshLinks for climbing surfaces, enabling native NavMesh pathfinding
    /// to automatically route agents through climbable areas.
    /// 
    /// Benefits over manual climbing:
    /// - Native pathfinding integration - agents automatically find climbing routes
    /// - Simpler state management - no need to disable/enable NavMeshAgent
    /// - Direction is implicit in link traversal
    /// - Works with Unity's built-in navigation
    /// 
    /// For large swarms (1000+ agents), create multiple links across the wall width.
    /// 
    /// SETUP FOR CENTER-PIVOTED OBJECTS (Unity default cubes):
    /// Right-click this component and select "Auto-Detect Dimensions from Collider/Renderer"
    /// This will automatically set width, height, and offset values based on the object's bounds.
    /// </summary>
    [AddComponentMenu("Enemy Masses/Climbing/NavMesh Link Climbable")]
    public class NavMeshLinkClimbable : MonoBehaviour
    {
        [Header("Climb Surface Dimensions")]
        [Tooltip("Width of the climbable surface (X axis in local space). Use 'Auto-Detect Dimensions' context menu to auto-fill.")]
        [SerializeField] private float width = 10f;
        
        [Tooltip("Height of the climbable surface (Y axis in local space). Use 'Auto-Detect Dimensions' context menu to auto-fill.")]
        [SerializeField] private float height = 10f;
        
        [Tooltip("Vertical offset from the GameObject's pivot to the wall BASE. For center-pivoted objects (Unity cubes), set to -height/2. Use 'Auto-Detect' to calculate automatically.")]
        [SerializeField] private float baseYOffset = 0f;
        
        [Tooltip("Forward offset from the GameObject's pivot to the wall's FRONT SURFACE (Z axis). For center-pivoted objects, set to -depth/2. Use 'Auto-Detect' to calculate automatically.")]
        [SerializeField] private float wallSurfaceZOffset = 0f;
        
        [Header("NavMesh Links")]
        [Tooltip("Create links on both sides of the wall (front and back). Enable this for walls that can be climbed from either side.")]
        [SerializeField] private bool doubleSided = true;
        
        [Tooltip("If true, automatically calculate link count based on wall width and linkSpacing. If false, use fixed linkCount.")]
        [SerializeField] private bool autoLinkCount = true;
        
        [Tooltip("Maximum spacing between links (in world units). Used when autoLinkCount is true. Smaller = more links.")]
        [SerializeField] [Range(1f, 50f)] private float linkSpacing = 10f;
        
        [Tooltip("Number of NavMeshLinks to create across the width PER SIDE. Used when autoLinkCount is false. More links = more agents can climb simultaneously")]
        [SerializeField] [Range(1, 100)] private int linkCount = 5;
        
        [Tooltip("Width of each NavMeshLink (affects how many agents can use each link)")]
        [SerializeField] [Range(0.5f, 10f)] private float linkWidth = 2f;
        
        [Tooltip("Agent Type ID - must match your NavMeshAgent's agent type. 0 = Humanoid (default)")]
        [SerializeField] private int agentTypeID = 0;
        
        [Tooltip("Area type for the links (use a custom area with higher cost to make climbing less preferred than walking)")]
        [SerializeField] private int navMeshAreaType = 0; // Default Walkable
        
        [Tooltip("Whether links are bidirectional (agents can climb up AND down)")]
        [SerializeField] private bool bidirectional = true;
        
        [Tooltip("Cost modifier for using this link (higher = less preferred path)")]
        [SerializeField] [Range(1f, 100f)] private float costModifier = 5f;

        [Header("A* Pathfinding Links")]
        [Tooltip("If true, also create A* Pathfinding NodeLink2 links so A* agents can path across this climbable.")]
        [SerializeField] private bool createAStarLinks = true;

        [Tooltip("Graph mask for A* links. Use -1 for 'Everything', otherwise set a bitmask value.")]
        [SerializeField] private int aStarGraphMask = -1;

        [Tooltip("Pathfinding tag for A* links (used to filter/weight traversal).")]
        [SerializeField] private int aStarPathfindingTag = 0;

        [Tooltip("Cost factor for A* links. 1 = normal cost, higher = less preferred.")]
        [SerializeField] [Range(0.1f, 100f)] private float aStarCostFactor = 1f;

        [Tooltip("If true, uses reflection to connect NodeLink2 and apply settings. Safe when A* is not installed.")]
        [SerializeField] private bool useAStarReflection = true;

        [Header("Flow Field Links")]
        [Tooltip("If true, emits virtual climb links for Flow Field integration even when NavMesh/A* links are unavailable.")]
        [SerializeField] private bool createFlowFieldVirtualLinks = true;

        [Tooltip("Cost multiplier used for Flow Field climb transitions (higher = less preferred).")]
        [SerializeField] [Range(0.1f, 10f)] private float flowFieldCostMultiplier = 1f;
        
        [Header("Climb Points")]
        [Tooltip("Offset from wall base for bottom link points. This is an initial search position - the actual endpoint will snap to the nearest NavMesh surface.")]
        [SerializeField] private Vector3 bottomOffset = new Vector3(0, 0, -0.5f);
        
        [Tooltip("Offset from wall top for top link points. This is an initial search position - the actual endpoint will snap to the nearest NavMesh surface.")]
        [SerializeField] private Vector3 topOffset = new Vector3(0, 0, 0.5f);
        
        [Tooltip("Search radius for finding NavMesh surface near the offset points. Increase if links aren't connecting due to NavMesh edge padding.")]
        [SerializeField] [Range(0.5f, 10f)] private float navMeshSnapRadius = 2f;
        
        [Tooltip("If true, preserves the Y offset even after snapping to NavMesh (keeps climb height consistent). If false, uses exact NavMesh Y position.")]
        [SerializeField] private bool preserveHeightOffset = false;
        
        [Header("Traversal Settings")]
        [Tooltip("If true, calculate climb time based on wall height and climbSpeed. If false, use fixed traversalTime.")]
        [SerializeField] private bool useHeightBasedDuration = true;
        
        [Tooltip("Climb speed in units per second (used when useHeightBasedDuration is true). E.g., 3 = 3 meters per second")]
        [SerializeField] [Range(0.5f, 10f)] private float climbSpeed = 2f;
        
        [Tooltip("Fixed time to traverse the link in seconds (used when useHeightBasedDuration is false)")]
        [SerializeField] [Range(0.5f, 20f)] private float traversalTime = 3f;
        
        [Header("Animation Speed")]
        [Tooltip("If true, animation speed is automatically calculated to match climb duration (animation loops exactly during climb). If false, use fixed animationSpeedOverride.")]
        [SerializeField] private bool syncAnimationToClimb = true;
        
        [Tooltip("Fixed animation playback speed (used when syncAnimationToClimb is false). 1 = normal speed, 0.5 = half speed, 2 = double speed")]
        [SerializeField] [Range(0.1f, 3f)] private float animationSpeedOverride = 1f;
        
        [Tooltip("Animation curve for vertical movement during climb (0,0 to 1,1). Use ease-in-out for smooth starts/stops.")]
        [SerializeField] private AnimationCurve climbCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        
        [Header("Climb Style")]
        [Tooltip("How the agent moves while climbing")]
        [SerializeField] private ClimbStyle climbStyle = ClimbStyle.Machine;
        
        [Tooltip("Number of 'steps' or pulls during the climb (used for Human/Rhythmic style)")]
        [SerializeField] [Range(2, 20)] private int climbSteps = 6;
        
        [Tooltip("How much the agent pauses between each step (0 = no pause, 1 = long pauses). Higher values make climbing look more labored.")]
        [SerializeField] [Range(0f, 0.8f)] private float stepPauseStrength = 0.3f;
        
        [Tooltip("Randomize the rhythm slightly per step for more natural movement")]
        [SerializeField] private bool randomizeRhythm = true;
        
        [Tooltip("Amount of rhythm randomization (0-1)")]
        [SerializeField] [Range(0f, 0.3f)] private float rhythmVariation = 0.1f;
        
        [Header("Multi-Agent Climbing (Horde Mode)")]
        [Tooltip("Number of overlapping links at each position. More links = more agents can climb simultaneously at the same spot. Set to 1 for single-file climbing.")]
        [SerializeField] [Range(1, 10)] private int linksPerPosition = 1;
        
        [Tooltip("Horizontal position variation (left/right) for simultaneous climbers. Spreads agents visually so they don't overlap.")]
        [SerializeField] [Range(0f, 2f)] private float climbPositionSpread = 0.5f;
        
        [Tooltip("Forward/backward position variation for simultaneous climbers. Adds depth to the climbing cluster.")]
        [SerializeField] [Range(0f, 1f)] private float climbDepthSpread = 0.2f;
        
        [Tooltip("Random climb speed variation (0 = all same speed, 1 = speeds vary from 50% to 150%). Makes climbing look more chaotic.")]
        [SerializeField] [Range(0f, 1f)] private float climbSpeedVariation = 0.3f;
        
        [Header("Debug")]
        [Tooltip("Show debug visualization")]
        [SerializeField] private bool showDebug = true;
        
        [Tooltip("Color for link visualization")]
        [SerializeField] private Color debugColor = new Color(0.2f, 0.8f, 0.2f, 0.8f);
        
        [Tooltip("Color for A* link visualization")]
        [SerializeField] private Color aStarDebugColor = new Color(0.2f, 0.6f, 1f, 0.8f);
        
        [Header("Info (Read Only)")]
        [Tooltip("Number of links that will be created (calculated at runtime)")]
        [SerializeField] private int calculatedLinkCount;
        
        [Tooltip("Calculated climb duration based on wall height and speed")]
        [SerializeField] private float calculatedClimbDuration;
        
        [Tooltip("Runtime count of Unity NavMesh links created")]
        [SerializeField] private int runtimeNavMeshLinkCount;
        
        [Tooltip("Runtime count of A* NodeLink2 links created")]
        [SerializeField] private int runtimeAStarLinkCount;
        
        /// <summary>
        /// Base offset to handle pivots that aren't at the bottom-front of the wall.
        /// Returns offset in LOCAL UNSCALED space (divides by localScale since we use localToWorldMatrix).
        /// </summary>
        private Vector3 BaseOffset => new Vector3(
            0,
            baseYOffset / transform.localScale.y,
            wallSurfaceZOffset / transform.localScale.z
        );
        
        // Runtime
        private List<NavMeshLink> _navMeshLinks = new List<NavMeshLink>();
        private List<Component> _aStarLinks = new List<Component>();
        private List<ClimbLinkInfo> _climbLinks = new List<ClimbLinkInfo>();
        private Dictionary<int, ClimbTraversalData> _traversingAgents = new Dictionary<int, ClimbTraversalData>();

        /// <summary>Unified link info for traversal, supports both NavMesh and A* links.</summary>
        public class ClimbLinkInfo
        {
            public Vector3 start;
            public Vector3 end;
            public NavMeshLink navMeshLink;
            public Component aStarLink;
            public Transform aStarEndTransform;
            public bool flowFieldEnabled;
            public float flowFieldCostMultiplier;
        }
        
        /// <summary>Data for an agent currently traversing a climb link</summary>
        public class ClimbTraversalData
        {
            public Vector3 startPos;
            public Vector3 endPos;
            public float startTime;
            public float duration;
            public bool ascending;
            public int linkIndex;
            public NavMeshLink link;
            /// <summary>Random seed for this agent's rhythm variation (0-1)</summary>
            public float rhythmSeed;
            
            // Multi-agent climbing variation data
            /// <summary>Horizontal offset for this agent's climb path (left/right spread)</summary>
            public Vector3 positionOffset;
            /// <summary>Speed multiplier for this agent (for chaotic varied speeds)</summary>
            public float speedMultiplier;
            /// <summary>Actual duration after speed variation applied</summary>
            public float ActualDuration => duration / speedMultiplier;
        }
        
        /// <summary>Width of the climbable surface</summary>
        public float Width => width;
        
        /// <summary>Height of the climbable surface</summary>
        public float Height => height;
        
        /// <summary>Number of NavMeshLinks</summary>
        public int LinkCount => linkCount;
        
        /// <summary>
        /// Time to traverse (climb duration).
        /// If useHeightBasedDuration is true, calculates based on height/climbSpeed.
        /// Otherwise returns the fixed traversalTime.
        /// </summary>
        public float TraversalTime
        {
            get
            {
                if (useHeightBasedDuration)
                {
                    // Calculate duration based on wall height and climb speed
                    // Add a minimum to prevent instant traversal
                    return Mathf.Max(0.5f, height / climbSpeed);
                }
                return traversalTime;
            }
        }
        
        /// <summary>Climb animation curve</summary>
        public AnimationCurve ClimbCurve => climbCurve;
        
        /// <summary>Current climb style</summary>
        public ClimbStyle Style => climbStyle;
        
        /// <summary>Whether animation speed should sync to climb duration</summary>
        public bool SyncAnimationToClimb => syncAnimationToClimb;
        
        /// <summary>Fixed animation speed override (used when SyncAnimationToClimb is false)</summary>
        public float AnimationSpeedOverride => animationSpeedOverride;
        
        /// <summary>Number of overlapping links per position (for horde climbing)</summary>
        public int LinksPerPosition => linksPerPosition;
        
        /// <summary>Position spread for simultaneous climbers</summary>
        public float ClimbPositionSpread => climbPositionSpread;
        
        /// <summary>Speed variation for chaotic climbing (0-1)</summary>
        public float ClimbSpeedVariation => climbSpeedVariation;

        /// <summary>Whether this climbable contributes virtual links to Flow Field integration.</summary>
        public bool CreateFlowFieldVirtualLinks => createFlowFieldVirtualLinks;

        /// <summary>Flow Field transition cost multiplier for links emitted by this climbable.</summary>
        public float FlowFieldCostMultiplier => flowFieldCostMultiplier;
        
        /// <summary>
        /// Evaluate climb progress with the configured style applied.
        /// Use this instead of ClimbCurve.Evaluate() for styled movement.
        /// </summary>
        /// <param name="progress">Linear progress from 0 to 1</param>
        /// <param name="agentIndex">Agent index for per-agent rhythm variation</param>
        /// <returns>Styled progress from 0 to 1</returns>
        public float EvaluateClimbProgress(float progress, int agentIndex)
        {
            // First apply the base curve
            float curvedProgress = climbCurve.Evaluate(progress);
            
            switch (climbStyle)
            {
                case ClimbStyle.Machine:
                    // Just use the curve as-is (smooth, constant)
                    return curvedProgress;
                    
                case ClimbStyle.Human:
                    // Apply rhythmic stepping - creates "pull, pause, pull, pause" motion
                    return ApplyRhythmicClimb(curvedProgress, agentIndex);
                    
                case ClimbStyle.Custom:
                default:
                    // Pure curve, no additional processing
                    return curvedProgress;
            }
        }
        
        /// <summary>
        /// Applies rhythmic/stepping motion to simulate human climbing.
        /// Creates a "step-step-step" pattern with brief pauses between steps.
        /// </summary>
        private float ApplyRhythmicClimb(float baseProgress, int agentIndex)
        {
            if (climbSteps <= 1) return baseProgress;
            
            // Get rhythm variation for this agent
            float variation = 0f;
            if (randomizeRhythm && _traversingAgents.TryGetValue(agentIndex, out var data))
            {
                variation = (data.rhythmSeed - 0.5f) * 2f * rhythmVariation;
            }
            
            // Calculate which step we're in
            float stepSize = 1f / climbSteps;
            int currentStep = Mathf.FloorToInt(baseProgress / stepSize);
            currentStep = Mathf.Clamp(currentStep, 0, climbSteps - 1);
            
            // Progress within the current step (0 to 1)
            float stepProgress = (baseProgress - (currentStep * stepSize)) / stepSize;
            
            // Apply variation to step timing
            stepProgress = Mathf.Clamp01(stepProgress + variation);
            
            // Create step motion: fast pull, then brief pause
            // Using a curve: quick acceleration, then slow down/pause
            // This creates the "reach-pull-rest" pattern of climbing
            float stepCurve;
            
            // Guard against stepPauseStrength being 0 or very small (would cause divide by zero)
            float safePauseStrength = Mathf.Max(stepPauseStrength, 0.001f);
            float pausePoint = 1f - safePauseStrength; // Where the pause starts (e.g., 0.7 = last 30% is pause)
            
            if (stepProgress < pausePoint)
            {
                // Active climbing portion - ease out (fast start, slow end)
                float activeProgress = stepProgress / pausePoint;
                stepCurve = 1f - (1f - activeProgress) * (1f - activeProgress); // Ease out quad
            }
            else
            {
                // Pause portion - minimal movement (simulates grip adjustment)
                float pauseProgress = (stepProgress - pausePoint) / safePauseStrength;
                // Small oscillation during pause to simulate grip adjustment
                stepCurve = 1f + Mathf.Sin(pauseProgress * Mathf.PI) * 0.02f;
            }
            
            // Map step-local progress to overall progress
            float stepStart = currentStep * stepSize;
            float stepEnd = (currentStep + 1) * stepSize;
            
            // Blend: step start + (step range * step curve)
            return Mathf.Lerp(stepStart, stepEnd, Mathf.Clamp01(stepCurve));
        }
        
        /// <summary>Get all agents currently traversing this climbable</summary>
        public IReadOnlyDictionary<int, ClimbTraversalData> TraversingAgents => _traversingAgents;
        
        private void OnEnable()
        {
            CreateNavMeshLinks();
            
            // Update calculated values
            UpdateCalculatedLinkCount();
            
            // Verify links are registered after a frame
            if (showDebug && Application.isPlaying)
            {
                StartCoroutine(ValidateLinksNextFrame());
            }
        }
        
        private System.Collections.IEnumerator ValidateLinksNextFrame()
        {
            yield return null; // Wait one frame
        }
        
        private void OnDisable()
        {
            DestroyNavMeshLinks();
            // Clear tracking data
            _traversingAgents.Clear();
        }
        
        private void OnValidate()
        {
            // Update calculated link count for display in inspector
            UpdateCalculatedLinkCount();
            
            // Recreate links if parameters change in editor during play mode
            if (Application.isPlaying && (_navMeshLinks.Count > 0 || _aStarLinks.Count > 0))
            {
                DestroyNavMeshLinks();
                CreateNavMeshLinks();
            }
        }
        
        /// <summary>
        /// Updates the calculated link count and climb duration based on current settings.
        /// </summary>
        private void UpdateCalculatedLinkCount()
        {
            int positions;
            if (autoLinkCount)
            {
                positions = Mathf.Max(1, Mathf.CeilToInt(width / linkSpacing));
            }
            else
            {
                positions = linkCount;
            }
            
            // Total links = positions × linksPerPosition × sides
            calculatedLinkCount = positions * linksPerPosition * (doubleSided ? 2 : 1);
            
            // Update calculated climb duration for inspector display
            calculatedClimbDuration = TraversalTime;
            
            // Warn about very high link counts
            if (calculatedLinkCount > 200)
            {
                Debug.LogWarning($"[NavMeshLinkClimbable] High link count ({calculatedLinkCount})! Consider using smaller wall segments or increasing linkSpacing for better performance.");
            }
        }
        
        /// <summary>
        /// Auto-detect dimensions from the attached Collider or Renderer.
        /// Call this from the context menu or at setup time.
        /// </summary>
        [ContextMenu("Auto-Detect Dimensions from Collider/Renderer")]
        public void AutoDetectDimensions()
        {
            Bounds bounds = default;
            bool foundBounds = false;
            
            // Try BoxCollider first (most common for walls)
            var boxCollider = GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                // BoxCollider bounds are in local space, we need to account for scale
                Vector3 scaledSize = Vector3.Scale(boxCollider.size, transform.localScale);
                Vector3 scaledCenter = Vector3.Scale(boxCollider.center, transform.localScale);
                bounds = new Bounds(scaledCenter, scaledSize);
                foundBounds = true;
            }
            
            // Try MeshRenderer if no collider
            if (!foundBounds)
            {
                var meshRenderer = GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    // Convert world bounds to local
                    bounds = meshRenderer.localBounds;
                    bounds.size = Vector3.Scale(bounds.size, transform.localScale);
                    bounds.center = Vector3.Scale(bounds.center, transform.localScale);
                    foundBounds = true;
                }
            }
            
            if (!foundBounds)
            {
                Debug.LogWarning($"[NavMeshLinkClimbable] No BoxCollider or MeshRenderer found on {name}. Cannot auto-detect dimensions.");
                return;
            }
            
            // Width is along local X axis
            width = bounds.size.x;
            
            // Height is along local Y axis
            height = bounds.size.y;
            
            // Base Y offset: if the bounds center Y is not at 0, we need to offset
            // For a center-pivoted cube, bounds.center.y = 0, but bounds go from -height/2 to +height/2
            // We want our Y=0 to be at the BOTTOM of the bounds
            baseYOffset = bounds.center.y - bounds.size.y * 0.5f;
            
            // Wall surface Z offset: use the front face of the bounds (negative Z in local space)
            wallSurfaceZOffset = bounds.center.z - bounds.size.z * 0.5f;
            
            Debug.Log($"[NavMeshLinkClimbable] Auto-detected dimensions for {name}:\n" +
                $"  Width: {width}\n" +
                $"  Height: {height}\n" +
                $"  Base Y Offset: {baseYOffset} (pivot to bottom)\n" +
                $"  Wall Surface Z Offset: {wallSurfaceZOffset} (pivot to front face)\n" +
                $"  Bounds: center={bounds.center}, size={bounds.size}");
            
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
        
        /// <summary>
        /// Validate that all links are properly connected to the NavMesh.
        /// Call this at runtime after NavMesh is baked.
        /// </summary>
        [ContextMenu("Validate NavMesh Link Connections")]
        public void ValidateLinkConnections()
        {
            if (_navMeshLinks.Count == 0)
            {
                Debug.LogWarning($"[NavMeshLinkClimbable] No links created yet on {name}. Enter Play mode first.");
                return;
            }
            
            int validLinks = 0;
            int invalidLinks = 0;
            
            foreach (var link in _navMeshLinks)
            {
                if (link == null) continue;
                
                Vector3 worldStart = link.transform.TransformPoint(link.startPoint);
                Vector3 worldEnd = link.transform.TransformPoint(link.endPoint);
                
                bool startOnNavMesh = NavMesh.SamplePosition(worldStart, out NavMeshHit startHit, 2f, NavMesh.AllAreas);
                bool endOnNavMesh = NavMesh.SamplePosition(worldEnd, out NavMeshHit endHit, 2f, NavMesh.AllAreas);
                
                if (startOnNavMesh && endOnNavMesh)
                {
                    validLinks++;
                    Debug.Log($"[NavMeshLinkClimbable] {link.gameObject.name}: VALID " +
                        $"(start dist={Vector3.Distance(worldStart, startHit.position):F2}, " +
                        $"end dist={Vector3.Distance(worldEnd, endHit.position):F2})");
                }
                else
                {
                    invalidLinks++;
                    Debug.LogWarning($"[NavMeshLinkClimbable] {link.gameObject.name}: INVALID " +
                        $"(start={worldStart} {(startOnNavMesh ? "OK" : "NO NAVMESH")}, " +
                        $"end={worldEnd} {(endOnNavMesh ? "OK" : "NO NAVMESH")})");
                }
            }
            
            Debug.Log($"[NavMeshLinkClimbable] Validation complete: {validLinks} valid, {invalidLinks} invalid links on {name}");
        }
        
        /// <summary>
        /// Test if pathfinding can find a route across the wall using the NavMeshLinks.
        /// This helps diagnose if the links are being used by the pathfinder.
        /// </summary>
        [ContextMenu("Test Path Across Wall")]
        public void TestPathAcrossWall()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[NavMeshLinkClimbable] This test only works in Play mode.");
                return;
            }
            
            // Get the actual link positions
            if (_navMeshLinks.Count == 0)
            {
                Debug.LogError("[NavMeshLinkClimbable] No links created!");
                return;
            }
            
            // Find front and back links (near center of wall)
            NavMeshLink frontLink = null;
            NavMeshLink backLink = null;
            
            foreach (var link in _navMeshLinks)
            {
                if (link == null) continue;
                if (link.gameObject.name.Contains("Front") && frontLink == null)
                    frontLink = link;
                if (link.gameObject.name.Contains("Back") && backLink == null)
                    backLink = link;
            }
            
            Debug.Log($"[NavMeshLinkClimbable] === PATH TEST ACROSS WALL ===");
            
            if (frontLink != null)
            {
                Debug.Log($"  Front link: start={frontLink.startPoint} end={frontLink.endPoint}");
            }
            if (backLink != null)
            {
                Debug.Log($"  Back link: start={backLink.startPoint} end={backLink.endPoint}");
            }
            
            // Test 1: Can we find NavMesh at the TOP of the front link?
            Vector3 frontTop = frontLink != null ? frontLink.endPoint : transform.TransformPoint(new Vector3(0, height, wallSurfaceZOffset + 1f));
            Vector3 backTop = backLink != null ? backLink.endPoint : transform.TransformPoint(new Vector3(0, height, -wallSurfaceZOffset + 10f));
            
            bool frontTopOnMesh = NavMesh.SamplePosition(frontTop, out NavMeshHit frontTopHit, 2f, NavMesh.AllAreas);
            bool backTopOnMesh = NavMesh.SamplePosition(backTop, out NavMeshHit backTopHit, 2f, NavMesh.AllAreas);
            
            Debug.Log($"  Front TOP ({frontTop}): NavMesh={frontTopOnMesh}, nearest={frontTopHit.position}, dist={Vector3.Distance(frontTop, frontTopHit.position):F2}");
            Debug.Log($"  Back TOP ({backTop}): NavMesh={backTopOnMesh}, nearest={backTopHit.position}, dist={Vector3.Distance(backTop, backTopHit.position):F2}");
            
            // Test 2: Can we path between the TOP endpoints (across the wall surface)?
            if (frontTopOnMesh && backTopOnMesh)
            {
                NavMeshPath topPath = new NavMeshPath();
                NavMesh.CalculatePath(frontTopHit.position, backTopHit.position, NavMesh.AllAreas, topPath);
                Debug.Log($"  Path across wall TOP: {topPath.status} ({topPath.corners.Length} corners)");
                
                if (topPath.status != NavMeshPathStatus.PathComplete)
                {
                    Debug.LogError("  PROBLEM: Cannot path across the top of the wall!");
                    Debug.LogError("  The NavMesh on top of the wall may not be continuous, or the link TOP endpoints aren't reaching it.");
                }
            }
            
            // Test 3: Full path from front ground to back ground
            Vector3 frontGround = frontLink != null ? frontLink.startPoint : transform.TransformPoint(new Vector3(0, 0, wallSurfaceZOffset - 5f));
            Vector3 backGround = backLink != null ? backLink.startPoint : transform.TransformPoint(new Vector3(0, 0, -wallSurfaceZOffset + 15f));
            
            // Offset to be clearly on either side
            frontGround += Vector3.back * 5f; // Move further in front
            backGround += Vector3.forward * 5f; // Move further behind
            
            bool frontGroundOnMesh = NavMesh.SamplePosition(frontGround, out NavMeshHit frontGroundHit, 10f, NavMesh.AllAreas);
            bool backGroundOnMesh = NavMesh.SamplePosition(backGround, out NavMeshHit backGroundHit, 10f, NavMesh.AllAreas);
            
            Debug.Log($"  Front GROUND ({frontGround}): NavMesh={frontGroundOnMesh}, nearest={frontGroundHit.position}");
            Debug.Log($"  Back GROUND ({backGround}): NavMesh={backGroundOnMesh}, nearest={backGroundHit.position}");
            
            if (frontGroundOnMesh && backGroundOnMesh)
            {
                NavMeshPath fullPath = new NavMeshPath();
                NavMesh.CalculatePath(frontGroundHit.position, backGroundHit.position, NavMesh.AllAreas, fullPath);
                
                Debug.Log($"  FULL PATH (front ground -> back ground): {fullPath.status}");
                Debug.Log($"  Path corners: {fullPath.corners.Length}");
                
                for (int i = 0; i < Mathf.Min(fullPath.corners.Length, 10); i++)
                {
                    Debug.Log($"    Corner {i}: {fullPath.corners[i]}");
                }
                
                if (fullPath.status == NavMeshPathStatus.PathComplete)
                {
                    Debug.Log("[NavMeshLinkClimbable] SUCCESS! Full path found across wall!");
                }
                else if (fullPath.status == NavMeshPathStatus.PathPartial)
                {
                    Debug.LogWarning("[NavMeshLinkClimbable] PARTIAL PATH - The NavMeshLinks are NOT being used!");
                    Debug.LogWarning("  Possible causes:");
                    Debug.LogWarning("  1. Link endpoints don't actually reach the NavMesh on top of the wall");
                    Debug.LogWarning("  2. The NavMesh on top is not connected between front and back edges");
                    Debug.LogWarning("  3. There's no NavMesh on the back side of the wall at ground level");
                }
                else
                {
                    Debug.LogError("[NavMeshLinkClimbable] NO PATH FOUND!");
                }
            }
            else
            {
                Debug.LogError("[NavMeshLinkClimbable] Cannot test - no NavMesh found on one or both sides!");
            }
        }

        /// <summary>
        /// Creates NavMeshLinks distributed across the width of the climbable surface.
        /// If doubleSided is true, creates links on both front and back faces.
        /// If linksPerPosition > 1, creates multiple overlapping links for horde climbing.
        /// </summary>
        private void CreateNavMeshLinks()
        {
            DestroyNavMeshLinks();
            runtimeNavMeshLinkCount = 0;
            runtimeAStarLinkCount = 0;
            
            // Calculate effective link count (positions across the wall)
            int effectiveLinkCount;
            if (autoLinkCount)
            {
                // Calculate how many link POSITIONS needed based on wall width and desired spacing
                // At minimum 1 position, and ensure good coverage
                effectiveLinkCount = Mathf.Max(1, Mathf.CeilToInt(width / linkSpacing));
            }
            else
            {
                effectiveLinkCount = linkCount;
            }
            
            // Spacing in world units
            float spacing = width / effectiveLinkCount;
            float startX = -width * 0.5f + spacing * 0.5f;
            
            // Create links for front side (negative Z)
            CreateLinksForSide(spacing, startX, "Front", 1f, effectiveLinkCount);
            
            // Create links for back side (positive Z) if double-sided
            if (doubleSided)
            {
                CreateLinksForSide(spacing, startX, "Back", -1f, effectiveLinkCount);
            }
        }
        
        /// <summary>
        /// Creates links for one side of the wall.
        /// Creates multiple overlapping links at each position if linksPerPosition > 1.
        /// </summary>
        private void CreateLinksForSide(float spacing, float startX, string sideName, float zOffsetMultiplier, int count)
        {
            // All calculations in WORLD space
            for (int i = 0; i < count; i++)
            {
                float localX = startX + i * spacing;
                
                // Calculate world positions directly
                // Bottom point: at wall base, offset forward from wall surface
                Vector3 localBottom = new Vector3(
                    localX / transform.localScale.x,                           // X position along width
                    baseYOffset / transform.localScale.y,                       // Y at wall base  
                    (wallSurfaceZOffset * zOffsetMultiplier + bottomOffset.z * zOffsetMultiplier) / transform.localScale.z  // Z in front of wall
                );
                
                // Top point: at wall top, offset behind wall surface
                Vector3 localTop = new Vector3(
                    localX / transform.localScale.x,                           // X position along width
                    (baseYOffset + height) / transform.localScale.y,            // Y at wall top
                    (wallSurfaceZOffset * zOffsetMultiplier + topOffset.z * zOffsetMultiplier) / transform.localScale.z     // Z behind wall
                );
                
                // Convert to world positions
                Vector3 worldBottom = transform.TransformPoint(localBottom);
                Vector3 worldTop = transform.TransformPoint(localTop);
                
                // CRITICAL: Snap endpoints to EXACT NavMesh positions (Unity NavMesh)
                NavMeshHit bottomHit, topHit;
                bool bottomFound = NavMesh.SamplePosition(worldBottom, out bottomHit, navMeshSnapRadius, NavMesh.AllAreas);
                bool topFound = NavMesh.SamplePosition(worldTop, out topHit, navMeshSnapRadius, NavMesh.AllAreas);

                Vector3 navBottom = bottomFound ? bottomHit.position : worldBottom;
                Vector3 navTop = topFound ? topHit.position : worldTop;

                if (preserveHeightOffset)
                {
                    navBottom.y = worldBottom.y;
                    navTop.y = worldTop.y;
                }

                bool canCreateAStar = createAStarLinks && useAStarReflection && FindAStarNodeLink2Type() != null;
                Vector3 aStarBottom = worldBottom;
                Vector3 aStarTop = worldTop;
                if (preserveHeightOffset)
                {
                    aStarBottom.y = worldBottom.y;
                    aStarTop.y = worldTop.y;
                }

                bool canCreateFlowField = createFlowFieldVirtualLinks;

                // If no backend can represent this climb link, skip this position.
                if ((!bottomFound || !topFound) && !canCreateAStar && !canCreateFlowField)
                {
                    if (showDebug)
                    {
                        Debug.LogWarning($"[NavMeshLinkClimbable] Skipping {sideName} position {i}: " +
                            $"NavMesh bottom={bottomFound} top={topFound}, A* available={canCreateAStar}, FlowField virtual={canCreateFlowField}. " +
                            $"Try increasing navMeshSnapRadius or ensure A* Pathfinding is installed.");
                    }
                    continue;
                }

                // Create multiple overlapping links at this position for horde climbing
                for (int j = 0; j < linksPerPosition; j++)
                {
                    NavMeshLink navLink = null;
                    Component aStarLink = null;
                    Transform aStarEndTransform = null;

                    // Create Unity NavMeshLink if possible
                    if (bottomFound && topFound)
                    {
                        // Create NavMeshLink GameObject - DO NOT parent to scaled object!
                        string linkName = linksPerPosition > 1 
                            ? $"{name}_ClimbLink_{sideName}_{i}_{j}" 
                            : $"{name}_ClimbLink_{sideName}_{i}";
                        var linkObj = new GameObject(linkName);
                        linkObj.transform.position = Vector3.zero;
                        linkObj.transform.rotation = Quaternion.identity;

                        navLink = linkObj.AddComponent<NavMeshLink>();

                        // Configure link properties
                        navLink.agentTypeID = agentTypeID;
                        navLink.area = navMeshAreaType;
                        navLink.bidirectional = bidirectional;
#if UNITY_6000_0_OR_NEWER
                        navLink.costModifier = costModifier;
#else
                        navLink.costModifier = (int)costModifier;
#endif
                        navLink.autoUpdate = true;
                        navLink.width = linkWidth;

                        navLink.startPoint = navBottom;
                        navLink.endPoint = navTop;

#if UNITY_6000_0_OR_NEWER
                        navLink.activated = true;
#else
                        navLink.enabled = true;
#endif
                        navLink.UpdateLink();

                        _navMeshLinks.Add(navLink);
                        runtimeNavMeshLinkCount = _navMeshLinks.Count;
                    }

                    // Create A* NodeLink2 if possible
                    if (canCreateAStar)
                    {
                        string linkName = linksPerPosition > 1
                            ? $"{name}_AStarClimbLink_{sideName}_{i}_{j}"
                            : $"{name}_AStarClimbLink_{sideName}_{i}";
                        var linkObj = new GameObject(linkName);
                        linkObj.transform.position = aStarBottom;
                        linkObj.transform.rotation = Quaternion.identity;

                        var endObj = new GameObject($"{linkName}_End");
                        endObj.transform.position = aStarTop;
                        endObj.transform.rotation = Quaternion.identity;

                        aStarLink = AddAStarNodeLink2(linkObj, endObj.transform);
                        if (aStarLink != null)
                        {
                            ApplyAStarNodeLink2Settings(aStarLink, endObj.transform);
                            InvokeAStarApply(aStarLink);
                        }

                        _aStarLinks.Add(aStarLink);
                        aStarEndTransform = endObj.transform;
                        runtimeAStarLinkCount = _aStarLinks.Count;
                    }

                    // Register a unified climb link for traversal and optional Flow Field integration.
                    Vector3 climbStart = bottomFound && topFound
                        ? navBottom
                        : (canCreateAStar ? aStarBottom : worldBottom);
                    Vector3 climbEnd = bottomFound && topFound
                        ? navTop
                        : (canCreateAStar ? aStarTop : worldTop);

                    _climbLinks.Add(new ClimbLinkInfo
                    {
                        start = climbStart,
                        end = climbEnd,
                        navMeshLink = navLink,
                        aStarLink = aStarLink,
                        aStarEndTransform = aStarEndTransform,
                        flowFieldEnabled = canCreateFlowField,
                        flowFieldCostMultiplier = Mathf.Max(0.1f, flowFieldCostMultiplier)
                    });
                }
                
            }
        }
        
        /// <summary>
        /// Destroys all created NavMeshLinks.
        /// </summary>
        private void DestroyNavMeshLinks()
        {
            foreach (var link in _navMeshLinks)
            {
                if (link != null)
                {
                    if (Application.isPlaying)
                        Destroy(link.gameObject);
                    else
                        DestroyImmediate(link.gameObject);
                }
            }
            _navMeshLinks.Clear();
            runtimeNavMeshLinkCount = 0;

            foreach (var link in _aStarLinks)
            {
                if (link != null)
                {
                    Transform endTransform = null;
                    if (link is Component linkComponent)
                    {
                        endTransform = GetAStarNodeLink2End(linkComponent);
                    }
                    if (Application.isPlaying)
                        Destroy(link.gameObject);
                    else
                        DestroyImmediate(link.gameObject);

                    if (endTransform != null)
                    {
                        if (Application.isPlaying)
                            Destroy(endTransform.gameObject);
                        else
                            DestroyImmediate(endTransform.gameObject);
                    }
                }
            }
            _aStarLinks.Clear();
            _climbLinks.Clear();
            runtimeAStarLinkCount = 0;
        }
        
        /// <summary>
        /// Register an agent starting to traverse a link.
        /// Called by NavMeshLinkTraversalHandler when agent begins off-mesh link traversal.
        /// </summary>
        public void RegisterTraversingAgent(int agentIndex, NavMeshLink link, Vector3 startPos, Vector3 endPos, bool ascending)
        {
            int linkIndex = link != null ? _navMeshLinks.IndexOf(link) : -1;
            
            // Calculate visual variation for horde climbing (position spread + speed variation)
            Vector3 positionOffset = Vector3.zero;
            float speedMultiplier = 1f;
            
            // Apply visual spread if configured (makes overlapping link climbers not stack on each other)
            if (climbPositionSpread > 0 || climbDepthSpread > 0 || climbSpeedVariation > 0)
            {
                // Calculate position offset to spread agents out visually
                float spreadAngle = (agentIndex * 137.5f) % 360f; // Golden angle for good distribution
                float spreadRadius = climbPositionSpread * Mathf.Sqrt(Random.value);
                
                // Calculate right and forward vectors relative to climb direction
                Vector3 climbDir = (endPos - startPos).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, climbDir).normalized;
                if (right == Vector3.zero) right = Vector3.right; // Fallback for vertical climbs
                Vector3 forward = Vector3.Cross(climbDir, right).normalized;
                
                // Apply horizontal spread (perpendicular to climb direction)
                positionOffset = right * Mathf.Cos(spreadAngle * Mathf.Deg2Rad) * spreadRadius;
                // Apply depth spread (forward/back from wall)
                positionOffset += forward * (Random.value - 0.5f) * 2f * climbDepthSpread;
                
                // Calculate speed variation for chaotic look
                if (climbSpeedVariation > 0)
                {
                    speedMultiplier = 1f + (Random.value - 0.5f) * climbSpeedVariation;
                    speedMultiplier = Mathf.Clamp(speedMultiplier, 0.5f, 1.5f);
                }
            }
            
            _traversingAgents[agentIndex] = new ClimbTraversalData
            {
                startPos = startPos + positionOffset,
                endPos = endPos + positionOffset,
                startTime = Time.time,
                duration = TraversalTime, // Use property which respects useHeightBasedDuration
                ascending = ascending,
                linkIndex = linkIndex,
                link = link,
                // Random seed for rhythm variation (different for each agent)
                rhythmSeed = Random.value,
                // Visual variation
                positionOffset = positionOffset,
                speedMultiplier = speedMultiplier
            };
        }
        
        /// <summary>
        /// Unregister an agent that finished traversing.
        /// </summary>
        public void UnregisterTraversingAgent(int agentIndex)
        {
            if (_traversingAgents.TryGetValue(agentIndex, out var data))
            {
                _traversingAgents.Remove(agentIndex);
            }
        }
        
        /// <summary>
        /// Get the climb progress (0-1) for an agent.
        /// </summary>
        public float GetAgentProgress(int agentIndex)
        {
            if (_traversingAgents.TryGetValue(agentIndex, out var data))
            {
                float elapsed = Time.time - data.startTime;
                // Use ActualDuration which accounts for speed variation
                return Mathf.Clamp01(elapsed / data.ActualDuration);
            }
            return 0f;
        }
        
        /// <summary>
        /// Get the current position for a traversing agent based on climb curve.
        /// </summary>
        public Vector3 GetAgentClimbPosition(int agentIndex)
        {
            if (_traversingAgents.TryGetValue(agentIndex, out var data))
            {
                float progress = GetAgentProgress(agentIndex);
                float curvedProgress = climbCurve.Evaluate(progress);
                return Vector3.Lerp(data.startPos, data.endPos, curvedProgress);
            }
            return Vector3.zero;
        }
        
        /// <summary>
        /// Check if an agent is currently traversing this climbable.
        /// </summary>
        public bool IsAgentTraversing(int agentIndex)
        {
            return _traversingAgents.ContainsKey(agentIndex);
        }
        
        /// <summary>
        /// Check if an agent has completed their traversal.
        /// </summary>
        public bool IsTraversalComplete(int agentIndex)
        {
            return GetAgentProgress(agentIndex) >= 1f;
        }
        
        /// <summary>
        /// Get the traversal data for an agent, including their position offset and speed multiplier.
        /// Useful for external systems that need to know the agent's varied climb parameters.
        /// </summary>
        public ClimbTraversalData GetTraversalData(int agentIndex)
        {
            _traversingAgents.TryGetValue(agentIndex, out var data);
            return data;
        }
        
        /// <summary>
        /// Find which link (if any) an agent is on based on their OffMeshLinkData.
        /// </summary>
        public NavMeshLink FindLinkForAgent(OffMeshLinkData linkData)
        {
            // Tolerance for position matching (agents might not be exactly at link position)
            const float MATCH_TOLERANCE = 2.5f;
            
            // Check each link to see if it matches the agent's current off-mesh link
            foreach (var link in _navMeshLinks)
            {
                if (link == null) continue;
                
                // IMPORTANT: NavMeshLink.startPoint/endPoint are LOCAL to the LINK's transform, not the parent!
                // We must use link.transform.TransformPoint, not this.transform.TransformPoint
                Vector3 linkStart = link.transform.TransformPoint(link.startPoint);
                Vector3 linkEnd = link.transform.TransformPoint(link.endPoint);
                
                // Check both orientations (agent could be going either direction)
                float dist1Start = Vector3.Distance(linkData.startPos, linkStart);
                float dist1End = Vector3.Distance(linkData.endPos, linkEnd);
                float dist2Start = Vector3.Distance(linkData.startPos, linkEnd);
                float dist2End = Vector3.Distance(linkData.endPos, linkStart);
                
                bool match1 = (dist1Start < MATCH_TOLERANCE && dist1End < MATCH_TOLERANCE);
                bool match2 = (dist2Start < MATCH_TOLERANCE && dist2End < MATCH_TOLERANCE);
                
                if (match1 || match2)
                {
                    return link;
                }
            }
            return null;
        }

        /// <summary>
        /// Get all climb links created by this climbable (Unity NavMesh + A*).
        /// </summary>
        public IReadOnlyList<ClimbLinkInfo> GetClimbLinks()
        {
            return _climbLinks;
        }

        // ──────────────────────────────────────────────────────────────────────
        // A* Pathfinding (optional, reflection-based)
        // ──────────────────────────────────────────────────────────────────────

        private static System.Type FindAStarNodeLink2Type()
        {
            return System.Type.GetType("Pathfinding.NodeLink2, com.arongranberg.astar")
                ?? System.Type.GetType("Pathfinding.NodeLink2, AstarPathfindingProject")
                ?? System.Type.GetType("Pathfinding.NodeLink2, Assembly-CSharp");
        }

        private Component AddAStarNodeLink2(GameObject host, Transform endTransform)
        {
            var type = FindAStarNodeLink2Type();
            if (type == null || host == null) return null;
            var component = host.AddComponent(type);
            if (component == null) return null;
            SetAStarNodeLink2End(component, endTransform);
            return component;
        }

        private void ApplyAStarNodeLink2Settings(Component nodeLink2, Transform endTransform)
        {
            if (nodeLink2 == null) return;
            SetAStarNodeLink2End(nodeLink2, endTransform);
            SetAStarProperty(nodeLink2, "oneWay", !bidirectional);
            SetAStarProperty(nodeLink2, "costFactor", aStarCostFactor);
            SetAStarProperty(nodeLink2, "pathfindingTag", CreateAStarPathfindingTagValue());
            SetAStarProperty(nodeLink2, "graphMask", CreateAStarGraphMaskValue());
        }

        private object CreateAStarGraphMaskValue()
        {
            var graphMaskType = System.Type.GetType("Pathfinding.GraphMask, com.arongranberg.astar")
                ?? System.Type.GetType("Pathfinding.GraphMask, AstarPathfindingProject")
                ?? System.Type.GetType("Pathfinding.GraphMask, Assembly-CSharp");
            if (graphMaskType == null)
                return null;

            if (aStarGraphMask < 0)
            {
                var everythingProp = graphMaskType.GetProperty("everything", BindingFlags.Public | BindingFlags.Static);
                if (everythingProp != null)
                    return everythingProp.GetValue(null, null);
            }

            // GraphMask has a uint constructor
            try
            {
                return System.Activator.CreateInstance(graphMaskType, new object[] { (uint)aStarGraphMask });
            }
            catch
            {
                return null;
            }
        }

        private void InvokeAStarApply(Component nodeLink2)
        {
            if (nodeLink2 == null) return;
            var method = nodeLink2.GetType().GetMethod("Apply", BindingFlags.Public | BindingFlags.Instance);
            if (method != null)
            {
                method.Invoke(nodeLink2, new object[] { });
            }
        }

        private static void SetAStarNodeLink2End(Component nodeLink2, Transform endTransform)
        {
            if (nodeLink2 == null) return;
            var field = nodeLink2.GetType().GetField("end", BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(nodeLink2, endTransform);
            }
        }

        private static Transform GetAStarNodeLink2End(Component nodeLink2)
        {
            if (nodeLink2 == null) return null;
            var field = nodeLink2.GetType().GetField("end", BindingFlags.Public | BindingFlags.Instance);
            return field != null ? field.GetValue(nodeLink2) as Transform : null;
        }

        private static void SetAStarProperty(Component nodeLink2, string name, object value)
        {
            if (nodeLink2 == null || value == null) return;
            var type = nodeLink2.GetType();
            var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(nodeLink2, value, null);
                return;
            }
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
            {
                field.SetValue(nodeLink2, value);
            }
        }

        private object CreateAStarPathfindingTagValue()
        {
            var tagType = System.Type.GetType("Pathfinding.PathfindingTag, com.arongranberg.astar")
                ?? System.Type.GetType("Pathfinding.PathfindingTag, AstarPathfindingProject")
                ?? System.Type.GetType("Pathfinding.PathfindingTag, Assembly-CSharp");
            if (tagType == null)
                return null;

            try
            {
                return System.Activator.CreateInstance(tagType, new object[] { (uint)Mathf.Max(0, aStarPathfindingTag) });
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Get all NavMeshLinks created by this climbable.
        /// </summary>
        public IReadOnlyList<NavMeshLink> GetNavMeshLinks()
        {
            return _navMeshLinks;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showDebug) return;
            
            DrawClimbableGizmos();
        }
        
        private void OnDrawGizmosSelected()
        {
            DrawClimbableGizmos();
            DrawLinkGizmos();
            DrawAStarLinkGizmos();
        }
        
        private void DrawClimbableGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            
            // Convert to unscaled local space
            float unscaledWidth = width / transform.localScale.x;
            float unscaledHeight = height / transform.localScale.y;
            float unscaledBottomZ = bottomOffset.z / transform.localScale.z;
            float unscaledTopZ = topOffset.z / transform.localScale.z;
            float unscaledZoneHeight = 1f / transform.localScale.y;
            float unscaledZoneDepth = 1f / transform.localScale.z;
            
            // Draw front side (negative Z)
            DrawSideGizmos(unscaledWidth, unscaledHeight, unscaledBottomZ, unscaledTopZ, 
                unscaledZoneHeight, unscaledZoneDepth, 1f, debugColor);
            
            // Draw back side (positive Z) if double-sided
            if (doubleSided)
            {
                Color backColor = new Color(debugColor.r * 0.7f, debugColor.g * 0.7f, debugColor.b, debugColor.a * 0.7f);
                DrawSideGizmos(unscaledWidth, unscaledHeight, -unscaledBottomZ, -unscaledTopZ, 
                    unscaledZoneHeight, unscaledZoneDepth, -1f, backColor);
            }
        }
        
        private void DrawSideGizmos(float unscaledWidth, float unscaledHeight, float unscaledBottomZ, float unscaledTopZ,
            float unscaledZoneHeight, float unscaledZoneDepth, float zMultiplier, Color sideColor)
        {
            Vector3 sideBaseOffset = new Vector3(
                0,
                baseYOffset / transform.localScale.y,
                (wallSurfaceZOffset * zMultiplier) / transform.localScale.z
            );
            
            // Draw wall outline
            Gizmos.color = sideColor;
            Vector3 center = sideBaseOffset + new Vector3(0, unscaledHeight * 0.5f, 0);
            Vector3 size = new Vector3(unscaledWidth, unscaledHeight, 0.1f / transform.localScale.z);
            Gizmos.DrawWireCube(center, size);
            
            // Draw bottom entry zone
            Gizmos.color = new Color(0, 1, 0, 0.3f);
            Vector3 bottomCenter = sideBaseOffset + new Vector3(0, unscaledZoneHeight * 0.5f, unscaledBottomZ);
            Vector3 bottomSize = new Vector3(unscaledWidth, unscaledZoneHeight, unscaledZoneDepth);
            Gizmos.DrawCube(bottomCenter, bottomSize);
            
            // Draw top entry zone
            Gizmos.color = new Color(1, 0.5f, 0, 0.3f);
            Vector3 topCenter = sideBaseOffset + new Vector3(0, unscaledHeight + unscaledZoneHeight * 0.5f, unscaledTopZ);
            Vector3 topSize = new Vector3(unscaledWidth, unscaledZoneHeight, unscaledZoneDepth);
            Gizmos.DrawCube(topCenter, topSize);
        }
        
        private void DrawLinkGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            
            // Convert to unscaled local space
            float unscaledWidth = width / transform.localScale.x;
            float unscaledHeight = height / transform.localScale.y;
            float unscaledLinkWidth = linkWidth / transform.localScale.x;
            Vector3 unscaledBottomOffset = new Vector3(
                bottomOffset.x / transform.localScale.x,
                bottomOffset.y / transform.localScale.y,
                bottomOffset.z / transform.localScale.z
            );
            Vector3 unscaledTopOffset = new Vector3(
                topOffset.x / transform.localScale.x,
                topOffset.y / transform.localScale.y,
                topOffset.z / transform.localScale.z
            );
            
            float spacing = unscaledWidth / linkCount;
            float startX = -unscaledWidth * 0.5f + spacing * 0.5f;
            
            // Draw front side links
            DrawSideLinkGizmos(spacing, startX, unscaledHeight, unscaledLinkWidth, 
                unscaledBottomOffset, unscaledTopOffset, 1f, Color.yellow);
            
            // Draw back side links if double-sided
            if (doubleSided)
            {
                Vector3 backBottomOffset = new Vector3(unscaledBottomOffset.x, unscaledBottomOffset.y, -unscaledBottomOffset.z);
                Vector3 backTopOffset = new Vector3(unscaledTopOffset.x, unscaledTopOffset.y, -unscaledTopOffset.z);
                DrawSideLinkGizmos(spacing, startX, unscaledHeight, unscaledLinkWidth, 
                    backBottomOffset, backTopOffset, -1f, new Color(1f, 0.6f, 0f)); // Orange for back
            }
        }
        
        private void DrawSideLinkGizmos(float spacing, float startX, float unscaledHeight, float unscaledLinkWidth,
            Vector3 unscaledBottomOffset, Vector3 unscaledTopOffset, float zMultiplier, Color linkColor)
        {
            Vector3 sideBaseOffset = new Vector3(
                0,
                baseYOffset / transform.localScale.y,
                (wallSurfaceZOffset * zMultiplier) / transform.localScale.z
            );
            
            float sphereRadius = 0.2f / Mathf.Min(transform.localScale.x, transform.localScale.y, transform.localScale.z);
            
            for (int i = 0; i < linkCount; i++)
            {
                float xPos = startX + i * spacing;
                
                Vector3 bottomPoint = sideBaseOffset + new Vector3(xPos, 0, 0) + unscaledBottomOffset;
                Vector3 topPoint = sideBaseOffset + new Vector3(xPos, unscaledHeight, 0) + unscaledTopOffset;
                
                // Draw link line
                Gizmos.color = linkColor;
                Gizmos.DrawLine(bottomPoint, topPoint);
                
                // Draw link endpoints
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(bottomPoint, sphereRadius);
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(topPoint, sphereRadius);
                
                // Draw link width
                Gizmos.color = new Color(linkColor.r, linkColor.g, linkColor.b, 0.3f);
                Vector3 bottomLeft = bottomPoint + Vector3.left * unscaledLinkWidth * 0.5f;
                Vector3 bottomRight = bottomPoint + Vector3.right * unscaledLinkWidth * 0.5f;
                Vector3 topLeft = topPoint + Vector3.left * unscaledLinkWidth * 0.5f;
                Vector3 topRight = topPoint + Vector3.right * unscaledLinkWidth * 0.5f;
                
                Gizmos.DrawLine(bottomLeft, topLeft);
                Gizmos.DrawLine(bottomRight, topRight);
            }
        }
        
        private void DrawAStarLinkGizmos()
        {
            if (_climbLinks == null || _climbLinks.Count == 0) return;
            
            float sphereRadius = 0.2f;
            Gizmos.color = aStarDebugColor;
            
            foreach (var link in _climbLinks)
            {
                if (link == null || link.aStarLink == null || link.aStarEndTransform == null) continue;
                
                // Use stored climb link endpoints (matches actual climb span)
                var start = link.start;
                var end = link.end;
                
                Gizmos.DrawLine(start, end);
                
                Gizmos.color = new Color(aStarDebugColor.r, aStarDebugColor.g, aStarDebugColor.b, 1f);
                Gizmos.DrawWireSphere(start, sphereRadius);
                Gizmos.DrawWireSphere(end, sphereRadius);
                Gizmos.color = aStarDebugColor;
            }
        }
#endif
    }
}
