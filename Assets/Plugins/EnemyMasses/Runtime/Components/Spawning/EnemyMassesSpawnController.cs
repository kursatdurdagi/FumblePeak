using System;
using System.Collections.Generic;
using UnityEngine;
using GPUInstancerPro;
using GPUInstancerPro.CrowdAnimations;
using GPUInstancerPro.PrefabModule;
using Arawn.EnemyMasses.Runtime.Navigation;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Handles spawning of crowd agents and manages the agent container.
    /// Integrates with EnemyCrowd ScriptableObjects for faction-based spawning.
    /// </summary>
    public class EnemyMassesSpawnController
    {
        private readonly SpawnSettings _settings;
        private readonly AnimationSettings _animationSettings;
        private readonly NavigationSettings _navigationSettings;
        private readonly PerformanceSettings _performanceSettings;
        private readonly EnemyMassesFormationSystem _formationSystem;
        private readonly List<CrowdAgent> _agents;
        private readonly List<EnemyCrowd> _enemyCrowds;
        private readonly Dictionary<GPUICrowdInstance, int> _instanceLookup;
        private readonly HashSet<int> _autoUpdatePrefabIds;
        private readonly Dictionary<int, SpawnGroup> _spawnGroups;
        private readonly Dictionary<int, int> _nameCounters = new(); // per-enemyCrowdIndex sequential naming

        public HitDetectionMode HitDetectionMode { get; set; } = HitDetectionMode.Distance;
        
        private GameObject _agentContainer;
        private GPUIPrefabManager _cachedPrefabManager;
        private int _destinationGeneration;
        private int _nextSpawnGroupId = 1;
        private float _lastSpawnTime;
        private int _lastSpawnCount;

        public event Action<CrowdAgent> OnAgentSpawned;
        
        /// <summary>
        /// Callback to initialize ragdoll physics on spawn (deactivates rigidbodies/colliders).
        /// Set this to the ragdoll system's deactivation method.
        /// </summary>
        public Action<CrowdAgent, bool> InitializeRagdollCallback { get; set; }
        
        /// <summary>
        /// Callback to force Bone Tracker workflow for ragdoll-enabled agents.
        /// This must be called BEFORE ragdoll initialization to ensure bones are tracked.
        /// </summary>
        public Action<CrowdAgent> ForceBoneTrackerForRagdollCallback { get; set; }

        /// <summary>
        /// Gets the time when the last spawn operation completed.
        /// </summary>
        public float LastSpawnTime => _lastSpawnTime;

        /// <summary>
        /// Gets the number of agents spawned in the last spawn operation.
        /// </summary>
        public int LastSpawnCount => _lastSpawnCount;

        /// <summary>
        /// Creates a new spawn controller.
        /// </summary>
        public EnemyMassesSpawnController(
            SpawnSettings settings,
            AnimationSettings animationSettings,
            NavigationSettings navigationSettings,
            PerformanceSettings performanceSettings,
            EnemyMassesFormationSystem formationSystem,
            List<CrowdAgent> agents,
            List<EnemyCrowd> enemyCrowds,
            Dictionary<GPUICrowdInstance, int> instanceLookup)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _animationSettings = animationSettings ?? throw new ArgumentNullException(nameof(animationSettings));
            _navigationSettings = navigationSettings ?? throw new ArgumentNullException(nameof(navigationSettings));
            _performanceSettings = performanceSettings ?? throw new ArgumentNullException(nameof(performanceSettings));
            _formationSystem = formationSystem ?? throw new ArgumentNullException(nameof(formationSystem));
            _agents = agents ?? throw new ArgumentNullException(nameof(agents));
            _enemyCrowds = enemyCrowds ?? throw new ArgumentNullException(nameof(enemyCrowds));
            _instanceLookup = instanceLookup ?? throw new ArgumentNullException(nameof(instanceLookup));
            _autoUpdatePrefabIds = new HashSet<int>();
            _spawnGroups = new Dictionary<int, SpawnGroup>();
        }

        /// <summary>
        /// Initializes the spawn controller (called once at start).
        /// </summary>
        public void Initialize(Transform parentTransform)
        {
            if (_agentContainer == null)
            {
                _agentContainer = new GameObject("Enemy Masses Agents");
                if (parentTransform != null)
                    _agentContainer.transform.SetParent(parentTransform, false);
            }
        }

        /// <summary>
        /// Sets the destination generation for newly spawned agents.
        /// </summary>
        public void SetDestinationGeneration(int generation)
        {
            _destinationGeneration = generation;
        }

        /// <summary>
        /// Gets all active spawn groups.
        /// </summary>
        public IReadOnlyDictionary<int, SpawnGroup> GetSpawnGroups()
        {
            return _spawnGroups;
        }

        #region Public Spawn Methods

        /// <summary>
        /// Spawns agents from an enemy crowd by index.
        /// </summary>
        /// <param name="enemyCrowdIndex">Index in the enemyCrowds list</param>
        /// <param name="spawnPosition">World position to spawn at</param>
        /// <param name="count">Number of agents to spawn</param>
        /// <param name="formation">Formation type</param>
        /// <returns>List of spawned agent indices</returns>
        public List<int> SpawnEnemyCrowd(int enemyCrowdIndex, Vector3 spawnPosition, int count, FormationType formation = FormationType.Random)
        {
            return SpawnEnemyCrowd(enemyCrowdIndex, spawnPosition, Quaternion.identity, count, formation);
        }

        /// <summary>
        /// Spawns agents from an enemy crowd by index with rotation.
        /// </summary>
        public List<int> SpawnEnemyCrowd(int enemyCrowdIndex, Vector3 spawnPosition, Quaternion spawnRotation, int count, FormationType formation = FormationType.Random)
        {
            if (enemyCrowdIndex < 0 || enemyCrowdIndex >= _enemyCrowds.Count)
            {
                Debug.LogError($"Invalid enemyCrowdIndex: {enemyCrowdIndex}. Valid range: 0-{_enemyCrowds.Count - 1}");
                return new List<int>();
            }

            EnemyCrowd enemyCrowd = _enemyCrowds[enemyCrowdIndex];
            if (enemyCrowd == null || enemyCrowd.prefab == null)
            {
                Debug.LogError($"Enemy crowd at index {enemyCrowdIndex} is null or has no prefab assigned!");
                return new List<int>();
            }

            return SpawnEnemyCrowdInternal(enemyCrowd, enemyCrowdIndex, spawnPosition, spawnRotation, count, formation);
        }

        /// <summary>
        /// Spawns agents from an enemy crowd by faction name.
        /// </summary>
        public List<int> SpawnEnemyCrowdByName(string factionName, Vector3 spawnPosition, int count, FormationType formation = FormationType.Random)
        {
            int index = _enemyCrowds.FindIndex(ec => ec != null && ec.factionName == factionName);
            if (index < 0)
            {
                Debug.LogError($"No enemy crowd found with faction name: '{factionName}'");
                return new List<int>();
            }

            return SpawnEnemyCrowd(index, spawnPosition, count, formation);
        }

        #endregion

        #region Internal Spawn Implementation

        private List<int> SpawnEnemyCrowdInternal(
            EnemyCrowd enemyCrowd,
            int enemyCrowdIndex,
            Vector3 spawnPosition,
            Quaternion spawnRotation,
            int count,
            FormationType formation)
        {
            if (count <= 0)
                return new List<int>();

            if (_agentContainer == null)
                _agentContainer = new GameObject("Enemy Masses Agents");

            // Check if this EnemyCrowd has a formation override
            FormationType actualFormation = formation;
            if (enemyCrowd.overrideFormation)
            {
                actualFormation = enemyCrowd.formationType;
            }

            // Create a new spawn group for this batch
            // Store the current formation spacing so patrol/movement can use the same spacing
            int spawnGroupId = _nextSpawnGroupId++;
            SpawnGroup spawnGroup = new SpawnGroup(
                spawnGroupId,
                enemyCrowdIndex,
                enemyCrowd.factionName,
                spawnPosition,
                actualFormation,
                _settings.formationSpacing
            );
            _spawnGroups[spawnGroupId] = spawnGroup;

            // Prepare naming sequence per crowd (sequential numbering per faction index)
            string baseName = enemyCrowd.prefab != null ? enemyCrowd.prefab.name.Replace("(Clone)", string.Empty).Trim() : "Agent";
            if (!_nameCounters.TryGetValue(enemyCrowdIndex, out int nameCounter))
            {
                nameCounter = 0;
            }

            List<int> spawnedIndices = new List<int>();
            int startingIndex = _agents.Count;
            List<Vector3> usedSpawnPositions = new List<Vector3>(count);
            float minSpawnSeparation = Mathf.Max(0.25f, _settings.formationSpacing * 0.5f);

            for (int i = 0; i < count; i++)
            {
                int agentIndex = startingIndex + i;
                
                // Use 'i' (0-based index within this spawn batch) and 'count' for formation calculation
                // This ensures the formation is calculated correctly for just this batch of agents
                Vector3 spawnPos = _formationSystem.CalculateFormationPosition(
                    i,           // Formation index starts at 0 for this batch
                    count,       // Total agents in this spawn batch only
                    spawnPosition,
                    _navigationSettings.destinationSpread,
                    spawnRotation,
                    actualFormation
                );

                // Instantiate the crowd prefab
                GameObject clone = UnityEngine.Object.Instantiate(
                    enemyCrowd.prefab,
                    spawnPos,
                    spawnRotation,
                    _agentContainer.transform
                );

                // Assign human-friendly unique name per crowd
                nameCounter++;
                clone.name = $"{baseName} {nameCounter}";

                ApplyHitDetectionColliders(clone);

                // Ensure GPUICrowdInstance component exists
                if (!clone.TryGetComponent(out GPUICrowdInstance crowdInstance))
                    crowdInstance = clone.AddComponent<GPUICrowdInstance>();

                // Handle GPUI Prefab registration
                GPUIPrefab prefabComponent = null;
                if (clone.TryGetComponent(out GPUIPrefab gpuiPrefab))
                {
                    prefabComponent = gpuiPrefab;
                    RegisterPrefabWithGPUI(gpuiPrefab, enemyCrowd.prefab);
                    
                    // Add cleanup component to ensure proper GPUI cleanup on destroy
                    // This prevents buffer index corruption when agents are destroyed
                    if (!clone.TryGetComponent(out EnemyMassesPrefabCleanup _))
                    {
                        clone.AddComponent<EnemyMassesPrefabCleanup>();
                    }
                    
                    // Force transform sync after GPUI registration to prevent stale buffer data
                    // This is critical when buffer indices are reused from dead agents
                    clone.transform.hasChanged = true;
                    if (gpuiPrefab.registeredManager != null)
                    {
                        gpuiPrefab.registeredManager.UpdateTransformData(gpuiPrefab);
                    }
                }
                else
                {
                    Debug.LogWarning("Spawned crowd instance is missing a GPUIPrefab component.", clone);
                }

                // Apply workflow override and get the actual workflow type
                HybridWorkflowType initialWorkflow = ApplyWorkflowOverride(crowdInstance);

                // Setup navigation agent using the abstraction layer
                // Use the faction's configured navigation type, or auto-detect if not specified
                NavigationType navType = enemyCrowd != null ? enemyCrowd.navigationType : NavigationType.UnityNavMesh;
                INavigationAgent navigationAgent = NavigationAgentFactory.CreateAgent(clone, navType);
                
                if (navigationAgent != null)
                {
                    bool requiresSurfaceValidation = navigationAgent.Capabilities.RequiresSurfaceValidationForControl;
                    bool supportsNavmeshOffsetImpulse = navigationAgent.Capabilities.SupportsNavmeshOffsetImpulse;
                    Vector3 desiredSpawnPos = spawnPos;
                    
                    // For non-Unity navigation, avoid collapsing multiple agents onto the same sampled point.
                    if (!requiresSurfaceValidation)
                    {
                        desiredSpawnPos = FindNonOverlappingSpawnPosition(desiredSpawnPos, usedSpawnPositions, minSpawnSeparation);
                    }

                    // CRITICAL: Sample position FIRST to ensure valid placement
                    Vector3 validNavMeshPos = desiredSpawnPos;
                    if (requiresSurfaceValidation)
                    {
                        float sampleRadius = 10f;
                        if (navigationAgent.SamplePosition(desiredSpawnPos, out Vector3 sampledPos, sampleRadius))
                        {
                            validNavMeshPos = sampledPos;
                        }
                        else
                        {
                            Debug.LogWarning($"[Spawn] Agent {i} spawn pos {desiredSpawnPos} is not near navigation surface! Trying wider search...");
                            if (navigationAgent.SamplePosition(desiredSpawnPos, out sampledPos, 50f))
                            {
                                validNavMeshPos = sampledPos;
                            }
                        }
                    }
                    else
                    {
                        // For A* and other non-Unity nav, keep the exact formation position
                        // to avoid collapsing into a single nearest node.
                        float maxSnapOffset = Mathf.Max(0.05f, minSpawnSeparation * 0.2f);
                        if (navigationAgent.SamplePosition(desiredSpawnPos, out Vector3 sampledPos, maxSnapOffset))
                        {
                            validNavMeshPos = sampledPos;
                        }
                    }
                    
                    // Method for reliable navigation placement:
                    // 1. Disable agent
                    // 2. Move transform to valid position  
                    // 3. Re-enable agent - this forces recalculation
                    navigationAgent.Enabled = false;
                    clone.transform.position = validNavMeshPos;
                    navigationAgent.Enabled = true;
                    
                    // Now do the Warp to ensure proper binding
                    // Temporarily disable position/rotation updates during warp
                    navigationAgent.UpdatePosition = false;
                    navigationAgent.UpdateRotation = false;
                    navigationAgent.Warp(validNavMeshPos);
                    navigationAgent.IsStopped = false;
                    navigationAgent.Position = validNavMeshPos;
                    navigationAgent.ResetPath();
                    
                    // Re-enable position/rotation updates after warp based on backend.
                    // FlowField uses controller-driven sync so move-based knockback visual offsets work reliably.
                    // Flight3D agents drive their own position in Update(), so they must keep UpdatePosition = true.
                    bool isFlight3D = navigationAgent.NavigationType == NavigationType.Flight3D;
                    bool controllerDrivesTransformSync = !isFlight3D &&
                                                         (supportsNavmeshOffsetImpulse ||
                                                         navigationAgent.NavigationType == NavigationType.FlowField);
                    navigationAgent.UpdatePosition = !controllerDrivesTransformSync;
                    navigationAgent.UpdateRotation = !controllerDrivesTransformSync;
                    
                    // Configure Flight3D agent from EnemyCrowd settings and apply altitude offset
                    if (isFlight3D && enemyCrowd != null)
                    {
                        var flight3DAgent = clone.GetComponent<Navigation.Flight3DNavigationAgent>();
                        if (flight3DAgent != null)
                        {
                            flight3DAgent.ConfigureFromCrowd(enemyCrowd);
                            
                            // Spawn at cruising altitude instead of ground level
                            Vector3 altitudePos = clone.transform.position;
                            altitudePos.y += flight3DAgent.PersonalAltitude;
                            clone.transform.position = altitudePos;
                            navigationAgent.Position = altitudePos;
                        }
                    }
                    
                    // Apply speed settings from EnemyCrowd configuration
                    if (enemyCrowd != null)
                    {
                        float modifiedSpeed = enemyCrowd.GetModifiedSpeed(enemyCrowd.agentBaseSpeed);
                        navigationAgent.Speed = modifiedSpeed;
                    }
                    
                    // Apply optional backend-specific defaults (kept outside core flow logic).
                    EnemyMassesNavigationBackendHooks.ApplySpawnAvoidanceDefaults(navigationAgent);
                    
                    // Final verification
                    if (!navigationAgent.IsOnNavMesh)
                    {
                        Debug.LogError($"[Spawn] CRITICAL: Agent {i} still NOT on navigation surface after placement at {validNavMeshPos}! " +
                                      $"Navigation surface may be missing or agent settings incompatible. " +
                                      $"NavigationType={navigationAgent.NavigationType}, Radius={navigationAgent.Radius}, Height={navigationAgent.Height}");
                    }

                    usedSpawnPositions.Add(validNavMeshPos);
                }

                // Get animator - should be on root for GPUI Pro compatibility
                // Non-humanoid archetypes (FlyingVehicle, GroundVehicle) don't require Animators
                bool isNonHumanoid = enemyCrowd != null && enemyCrowd.IsNonHumanoid;
                Animator animator = crowdInstance.Animator;
                if (animator == null && !isNonHumanoid)
                {
                    Debug.LogWarning($"[Spawn] NO Animator found on spawned agent: {clone.name}. " +
                                   $"GPUI Pro requires an Animator component on the root GameObject.", clone);
                }
                
                if (animator != null)
                {
                    // Enable the animator if it's disabled
                    if (!animator.enabled)
                        animator.enabled = true;
                    
                    animator.applyRootMotion = false;
                }
                
                crowdInstance.ApplyRootMotion = false;
                
                // Warn if animator is not on root - GPUI Pro requires root animators for reliable GPU animation
                if (crowdInstance.Animator != null && crowdInstance.Animator.gameObject != clone)
                {
                    Debug.LogWarning($"[Spawn] Animator is on child object '{crowdInstance.Animator.gameObject.name}' instead of root! " +
                                   $"GPUI Pro requires Animator on root GameObject for reliable GPU animations. " +
                                   $"Please move Animator component to root of prefab '{enemyCrowd.factionName}'.", clone);
                }
                else if (crowdInstance.Animator == null && !isNonHumanoid)
                {
                    Debug.LogWarning($"[Spawn] CrowdInstance.Animator is NULL! This will cause animation issues.", clone);
                }

                // Create crowd agent data with correct initial workflow
                var crowdAgent = CreateCrowdAgent(
                    crowdInstance,
                    navigationAgent,
                    animator,
                    prefabComponent,
                    spawnPos,
                    enemyCrowdIndex,
                    actualFormation, // Pass the resolved formation type
                    initialWorkflow  // Pass the actual workflow from GPU instance
                );

                // Add to agents list
                _agents.Add(crowdAgent);
                crowdAgent.index = _agents.Count - 1;
                _instanceLookup[crowdAgent.crowdInstance] = crowdAgent.index;
                
                // Flight3D agents are inherently airborne — mark them as flying at spawn
                // so the flying immunity system correctly excludes them from melee skills,
                // ground-only damage, and non-anti-air attacks.
                if (navigationAgent.NavigationType == NavigationType.Flight3D)
                {
                    crowdAgent.isFlying = true;
                    // Flight3D uses cruisingAltitude (actual 3D altitude), not flightHeight
                    // (which is the FlyingHumanoid visual offset and defaults to 0).
                    float altitude = enemyCrowd != null ? enemyCrowd.cruisingAltitude : 10f;
                    crowdAgent.flightTargetHeight = altitude;
                    crowdAgent.flightCurrentHeight = altitude;
                }
                
                // Assign to spawn group
                crowdAgent.spawnGroupId = spawnGroupId;
                spawnGroup.agentIndices.Add(crowdAgent.index);

                spawnedIndices.Add(crowdAgent.index);

                // CRITICAL: For ragdoll-enabled factions, force Bone Tracker workflow FIRST
                // This ensures bones are tracked from spawn, not just when death happens
                // Without this, colliders won't be properly available when ragdoll activates
                // MUST happen BEFORE InitializeAgentAnimation to prevent GPU animation errors
                if (enemyCrowd != null && enemyCrowd.enableRagdollOnDeath)
                {
                    ForceBoneTrackerForRagdollCallback?.Invoke(crowdAgent);
                }

                // Initialize idle animation for newly spawned agent
                // For ragdoll factions, this will now use Mecanim instead of GPU animations
                InitializeAgentAnimation(crowdAgent, enemyCrowd);
                
                // CRITICAL: Deactivate ragdoll physics on spawn to prevent physics glitches
                // Ragdoll rigidbodies/colliders must be kinematic/disabled until death
                InitializeRagdollCallback?.Invoke(crowdAgent, true);

                // Notify listeners
                OnAgentSpawned?.Invoke(crowdAgent);
            }

            // Persist counter for this crowd index
            _nameCounters[enemyCrowdIndex] = nameCounter;

            if (count > 0)
            {
                _lastSpawnTime = Time.time;
                _lastSpawnCount = count;
            }

            return spawnedIndices;
        }

        private static Vector3 FindNonOverlappingSpawnPosition(Vector3 desiredPos, List<Vector3> usedPositions, float minDistance)
        {
            if (usedPositions == null || usedPositions.Count == 0)
                return desiredPos;

            if (!IsPositionTooClose(desiredPos, usedPositions, minDistance))
                return desiredPos;

            const int maxAttempts = 12;
            float radius = Mathf.Max(minDistance, 0.5f);
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                Vector2 offset2D = UnityEngine.Random.insideUnitCircle * radius;
                Vector3 candidate = new Vector3(desiredPos.x + offset2D.x, desiredPos.y, desiredPos.z + offset2D.y);
                if (!IsPositionTooClose(candidate, usedPositions, minDistance))
                    return candidate;

                radius *= 1.15f;
            }

            return desiredPos;
        }

        private static bool IsPositionTooClose(Vector3 candidate, List<Vector3> usedPositions, float minDistance)
        {
            float minSqr = minDistance * minDistance;
            for (int i = 0; i < usedPositions.Count; i++)
            {
                if ((usedPositions[i] - candidate).sqrMagnitude < minSqr)
                    return true;
            }
            return false;
        }
        
        /// <summary>
        /// Initializes the animation for a newly spawned agent, setting it to idle state.
        /// </summary>
        private void InitializeAgentAnimation(CrowdAgent agent, EnemyCrowd enemyCrowd)
        {
            if (agent == null || agent.crowdInstance == null)
                return;

            // Get the idle animation clip (check faction override first)
            AnimationClip idleClip = _animationSettings.idleClip;
            if (enemyCrowd != null && enemyCrowd.idleClipOverride != null)
                idleClip = enemyCrowd.idleClipOverride;

            // Check if workflow supports GPU animations
            // Only ComputeAnimator supports GPU animation calls - BoneTracker and MecanimReader use Mecanim
            // CRITICAL: If ragdoll is enabled, we MUST use BoneTracker (set by ForceBoneTrackerForRagdollCallback)
            // so don't try to use GPU animations even if _animationSettings says to
            bool workflowSupportsGpu = agent.currentWorkflow == HybridWorkflowType.ComputeAnimator;
            bool useGpu = _animationSettings.controlAnimationsWithGPUI && workflowSupportsGpu;

            if (useGpu && idleClip != null)
            {
                // Verify the GPU instance is actually using ComputeAnimator workflow
                int? actualWorkflowId = agent.crowdInstance?.AnimatorWorkflow?.GetID();
                if (actualWorkflowId != GPUIAWComputeAnimator.WORKFLOW_ID)
                {
                    // Workflow mismatch - don't try to force it, just fall back to Mecanim
                    Debug.Log($"[Spawn] Agent {agent.index}: GPU workflow mismatch (currentWorkflow={agent.currentWorkflow}, actualWorkflowId={actualWorkflowId}), using Mecanim");
                    useGpu = false;
                }
                else
                {
                    // Validate buffer bounds before calling GPU animation API
                    var prefab = agent.crowdInstance?.PrefabComponent;
                    bool isValid = prefab != null && prefab.IsInstanced && prefab.renderKey != 0 && 
                        prefab.bufferIndex >= 0 && GPUIRenderingSystem.IsActive;
                    
                    if (isValid && GPUIRenderingSystem.TryGetRenderSource(prefab.renderKey, out var rs) &&
                        prefab.bufferIndex < rs.bufferSize)
                    {
                        // Start GPU-based idle animation with transition time 0 to immediately snap to idle
                        // This prevents stale animation data from previous buffer occupants from showing
                        // normalizedClipTime=0 starts from beginning, transition=0 means no blend from previous
                        agent.crowdInstance.StartAnimation(idleClip, 0f, 1f, 0f, true, false);
                    }
                    else
                    {
                        // GPU not ready yet - StartAnimation will internally queue via _OnProcessed
                        // Use normalizedClipTime=0 and transitionTime=0 to snap to idle immediately when ready
                        agent.crowdInstance.StartAnimation(idleClip, 0f, 1f, 0f, true, false);
                        
                        // Also set Mecanim to idle as fallback during processing
                        if (agent.mecanimAnimator != null)
                        {
                            agent.mecanimAnimator.SetFloat("Speed", 0f);
                        }
                    }
                }
            }
            
            // Use Mecanim for BoneTracker/MecanimReader workflows or when GPU wasn't used
            if (!useGpu && agent.mecanimAnimator != null)
            {
                // For Mecanim-based workflows, set Speed parameter to 0 for idle
                // Blend tree will handle idle pose at Speed=0
                agent.mecanimAnimator.SetFloat("Speed", 0f);
                
                // Also ensure animator is enabled for BoneTracker workflow
                if (!agent.mecanimAnimator.enabled)
                {
                    agent.mecanimAnimator.enabled = true;
                }
            }
            else if (!useGpu && idleClip == null)
            {
                Debug.LogWarning($"[Spawn] No idle animation clip found for agent {agent.index}! Check AnimationSettings.idleClip or EnemyCrowd.idleClipOverride. Agent may T-pose.");
            }
            else if (!useGpu && agent.mecanimAnimator == null)
            {
                Debug.LogWarning($"[Spawn] Agent {agent.index} has no valid animation method (no GPU support and no Mecanim animator)");
            }

            agent.currentState = CrowdAnimationState.Idle;
        }

        #endregion

        #region Hit Detection Helpers

        private void ApplyHitDetectionColliders(GameObject clone)
        {
            if (clone == null)
                return;

            if (HitDetectionMode == HitDetectionMode.Collider)
            {
                // If any collider already exists (manual setup), keep it. Otherwise add a capsule on root and mark it.
                Collider existing = clone.GetComponentInChildren<Collider>(true);
                if (existing == null)
                {
                    var capsule = clone.GetComponent<CapsuleCollider>();
                    if (capsule == null)
                        capsule = clone.AddComponent<CapsuleCollider>();
                    capsule.isTrigger = false;
                    if (!capsule.TryGetComponent<AutoAddedColliderMarker>(out _))
                        capsule.gameObject.AddComponent<AutoAddedColliderMarker>();
                }
            }
            else
            {
                // Distance mode: remove only auto-added colliders to leave author-provided colliders intact.
                var markers = clone.GetComponentsInChildren<AutoAddedColliderMarker>(true);
                for (int i = 0; i < markers.Length; i++)
                {
                    var marker = markers[i];
                    if (marker == null) continue;
                    var col = marker.GetComponent<Collider>();
                    if (col != null)
                        UnityEngine.Object.Destroy(col);
                    UnityEngine.Object.Destroy(marker);
                }
            }
        }

        #endregion

        #region Helper Methods

        private CrowdAgent CreateCrowdAgent(
            GPUICrowdInstance crowdInstance,
            INavigationAgent navigationAgent,
            Animator animator,
            GPUIPrefab prefabComponent,
            Vector3 spawnPos,
            int enemyCrowdIndex,
            FormationType formationType,
            HybridWorkflowType initialWorkflow)
        {
            EnemyCrowd assignedCrowd = enemyCrowdIndex >= 0 && enemyCrowdIndex < _enemyCrowds.Count ? _enemyCrowds[enemyCrowdIndex] : null;
            
            // Calculate the base speed from EnemyCrowd settings
            float agentBaseSpeed = assignedCrowd != null 
                ? assignedCrowd.GetModifiedSpeed(assignedCrowd.agentBaseSpeed) 
                : (navigationAgent?.Speed ?? 3.5f);
            
            return new CrowdAgent
            {
                crowdInstance = crowdInstance,
                navigationAgent = navigationAgent,
                mecanimAnimator = animator,
                currentState = CrowdAnimationState.Idle,
                cachedBlendWeight = -1f,
                prefabComponent = prefabComponent,
                lastNavSyncTime = Time.time,
                smoothedSpeed = 0f,
                lastDestination = spawnPos,
                lastProgressTime = Time.time,
                lastKnownDistance = float.PositiveInfinity,
                remainingRetries = _navigationSettings.stuckRetryAttempts,
                nextRetryTime = Time.time,
                index = _agents.Count,
                hasArrived = false,
                destinationGeneration = _destinationGeneration - 1,
                currentWorkflow = initialWorkflow,
                lastAliveWorkflow = initialWorkflow,
                lastAttackerUp = Vector3.up,
                enemyCrowdIndex = enemyCrowdIndex,
                enemyCrowd = assignedCrowd, // Phase 2: Set direct reference
                spawnGroupId = -1, // Will be set after creation
                formationType = formationType, // Store formation type for this agent
                animatorWasEnabled = animator != null && animator.enabled,
                animatorPreviousSpeed = animator != null && !Mathf.Approximately(animator.speed, 0f) ? animator.speed : 1f,
                deathVFXRenderer = ResolveDeathRenderer(prefabComponent != null ? prefabComponent.gameObject : crowdInstance?.gameObject, assignedCrowd),
                baseSpeed = agentBaseSpeed, // Initialize base speed from EnemyCrowd settings
                hasBaseSpeedSet = true
            };
        }

        private Renderer ResolveDeathRenderer(GameObject prefabRoot, EnemyCrowd crowd)
        {
            if (prefabRoot == null || crowd == null || crowd.deathEffectsConfig == null)
                return null;

            var config = crowd.deathEffectsConfig;
            if (!config.bindShapeToRenderer)
                return null;

            // Helper to pick first renderer by type preference
            static Renderer PickFirst(GameObject root, bool preferSkinned)
            {
                if (preferSkinned)
                {
                    var skinned = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    if (skinned != null) return skinned;
                }
                var mesh = root.GetComponentInChildren<MeshRenderer>(true);
                if (mesh != null) return mesh;
                if (!preferSkinned)
                {
                    var skinned = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    if (skinned != null) return skinned;
                }
                return null;
            }

            // Name-filtered search (exact GameObject name)
            if (!string.IsNullOrEmpty(config.rendererName))
            {
                Renderer named = null;
                if (config.preferSkinnedRenderer)
                    named = FindRendererByName<SkinnedMeshRenderer>(prefabRoot, config.rendererName);
                if (named == null)
                    named = FindRendererByName<MeshRenderer>(prefabRoot, config.rendererName);
                if (named != null)
                    return named;
            }

            if (config.autoFindFirstRenderer)
            {
                return PickFirst(prefabRoot, config.preferSkinnedRenderer);
            }

            return null;
        }

        private static TRenderer FindRendererByName<TRenderer>(GameObject root, string name) where TRenderer : Renderer
        {
            var renderers = root.GetComponentsInChildren<TRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r != null && r.gameObject.name == name)
                    return r;
            }
            return null;
        }

        /// <summary>
        /// Applies workflow override to the GPU crowd instance and returns the resulting HybridWorkflowType.
        /// </summary>
        private HybridWorkflowType ApplyWorkflowOverride(GPUICrowdInstance instance)
        {
            switch (_animationSettings.animatorWorkflow)
            {
                case AnimatorWorkflowOverride.MecanimReader:
                    instance.AnimatorWorkflowID = GPUIAWMecanimReader.WORKFLOW_ID;
                    return HybridWorkflowType.MecanimReader;
                case AnimatorWorkflowOverride.ComputeAnimator:
                    instance.AnimatorWorkflowID = GPUIAWComputeAnimator.WORKFLOW_ID;
                    return HybridWorkflowType.ComputeAnimator;
                case AnimatorWorkflowOverride.BoneTracker:
                    instance.AnimatorWorkflowID = GPUIAWBoneTracker.WORKFLOW_ID;
                    return HybridWorkflowType.BoneTracker;
                case AnimatorWorkflowOverride.KeepPrefabSetting:
                default:
                    // Detect from instance's current workflow
                    return GetWorkflowTypeFromInstance(instance);
            }
        }
        
        /// <summary>
        /// Gets the HybridWorkflowType from a GPUICrowdInstance's current AnimatorWorkflowID.
        /// </summary>
        private HybridWorkflowType GetWorkflowTypeFromInstance(GPUICrowdInstance instance)
        {
            if (instance == null)
                return _performanceSettings.distantRangeWorkflow;
                
            int workflowId = instance.AnimatorWorkflowID;
            if (workflowId == GPUIAWBoneTracker.WORKFLOW_ID)
                return HybridWorkflowType.BoneTracker;
            if (workflowId == GPUIAWMecanimReader.WORKFLOW_ID)
                return HybridWorkflowType.MecanimReader;
            // Default to ComputeAnimator (ID = 1 typically)
            return HybridWorkflowType.ComputeAnimator;
        }

        private void RegisterPrefabWithGPUI(GPUIPrefab gpuiPrefab, GameObject originalPrefab = null)
        {
            if (gpuiPrefab == null)
                return;
                
            // If already instanced, just ensure transform is synced
            if (gpuiPrefab.IsInstanced)
            {
                gpuiPrefab.transform.hasChanged = true;
                if (gpuiPrefab.registeredManager != null)
                {
                    gpuiPrefab.registeredManager.UpdateTransformData(gpuiPrefab);
                }
                return;
            }
            
            GPUIPrefabManager targetManager = GetActivePrefabManager();
            if (targetManager != null)
            {
                int prototypeIndex = targetManager.GetPrototypeIndex(gpuiPrefab);
                
                // If prototype not found in this manager, try to find any manager that has it
                if (prototypeIndex < 0)
                {
                    // Search all active managers for this prototype
                    if (GPUIRenderingSystem.IsActive && GPUIRenderingSystem.Instance.ActiveGPUIManagers != null)
                    {
                        foreach (var manager in GPUIRenderingSystem.Instance.ActiveGPUIManagers)
                        {
                            if (manager is GPUIPrefabManager prefabMgr && prefabMgr != targetManager)
                            {
                                int altIndex = prefabMgr.GetPrototypeIndex(gpuiPrefab);
                                if (altIndex >= 0)
                                {
                                    targetManager = prefabMgr;
                                    prototypeIndex = altIndex;
                                    break;
                                }
                            }
                        }
                    }
                }
                
                if (prototypeIndex >= 0)
                {
                    // ALWAYS use immediate registration to prevent invisible agents on rapid spawn
                    // Deferred registration can cause timing issues with sequential spawns
                    GPUIPrefabAPI.AddPrefabInstanceImmediate(targetManager, gpuiPrefab, prototypeIndex);
                    
                    EnsureTransformUpdatesConfigured(targetManager, prototypeIndex);
                    
                    // Force immediate transform sync - critical for preventing invisible agents
                    gpuiPrefab.transform.hasChanged = true;
                    targetManager.UpdateTransformData(gpuiPrefab);
                }
                else
                {
                    // No manager has this prototype — try to auto-register the prefab at runtime
                    if (originalPrefab != null)
                    {
                        int addedIndex = GPUIPrefabAPI.AddPrototype(targetManager, originalPrefab);
                        if (addedIndex >= 0)
                        {
                            GPUIPrefabAPI.AddPrefabInstanceImmediate(targetManager, gpuiPrefab, addedIndex);
                            EnsureTransformUpdatesConfigured(targetManager, addedIndex);
                            gpuiPrefab.transform.hasChanged = true;
                            targetManager.UpdateTransformData(gpuiPrefab);
                        }
                        else
                        {
                            // Auto-registration failed — agent will render via standard Unity rendering.
                            // This is normal for non-humanoid prefabs (vehicles/drones) that may not
                            // be compatible with the GPUIPrefabManager's prototype system.
                            // Do NOT call AddPrefabInstance here — it will error with "Unknown prefab ID".
                        }
                    }
                    // else: no original prefab reference — silently skip GPUI, agent renders normally
                }
            }
            else
            {
                // No active manager found — agent will render via standard Unity rendering
            }
            
            // Silent check — no warning spam for agents that render via standard Unity
            // (non-humanoid vehicles/drones may not use GPUI at all)
            if (!gpuiPrefab.IsInstanced)
            {
                // Only warn once per prefab type to avoid console spam
                // Non-humanoid vehicles may intentionally not use GPUI and render via standard Unity
            }
        }

        private GPUIPrefabManager GetActivePrefabManager()
        {
            if (_cachedPrefabManager == null)
            {
                if (_settings.prefabManager != null)
                {
                    _cachedPrefabManager = _settings.prefabManager;
                }
                else if (GPUIRenderingSystem.IsActive && GPUIRenderingSystem.Instance.ActiveGPUIManagers != null)
                {
                    for (int i = 0; i < GPUIRenderingSystem.Instance.ActiveGPUIManagers.Count; i++)
                    {
                        if (GPUIRenderingSystem.Instance.ActiveGPUIManagers[i] is GPUIPrefabManager prefabMgr)
                        {
                            _cachedPrefabManager = prefabMgr;
                            break;
                        }
                    }
                }
            }
            return _cachedPrefabManager;
        }

        private void EnsureTransformUpdatesConfigured(GPUIPrefabManager manager, int prototypeIndex)
        {
            if (manager == null)
                return;

            GPUIPrefabPrototypeData prototypeData = manager.GetPrototypeData(prototypeIndex);
            if (prototypeData == null)
                return;

            int prefabId = prototypeData.prefabID;
            if (_autoUpdatePrefabIds.Contains(prefabId))
                return;

            if (!prototypeData.isAutoUpdateTransformData)
                prototypeData.isAutoUpdateTransformData = true;

            _autoUpdatePrefabIds.Add(prefabId);
            manager.RequireTransformUpdate();
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Clears the cached prefab manager (call when manager changes).
        /// </summary>
        public void ClearCachedManager()
        {
            _cachedPrefabManager = null;
        }

        #endregion
        
        #region Phase 2: Settings Helpers (EnemyCrowd as single source of truth)
        
        private GPUIPrefabManager GetPrefabManager(EnemyCrowd crowd)
        {
            // Phase 3 Refined: PrefabManager is now a global setting
            return _settings.prefabManager;
        }
        
        private bool GetRegisterImmediately(EnemyCrowd crowd)
        {
            // Phase 3 Refined: RegisterImmediately is now a global setting
            return _settings.registerImmediately;
        }
        
        private GameObject GetIndicatorPrefab(EnemyCrowd crowd)
        {
            return crowd?.indicatorPrefab != null ? crowd.indicatorPrefab : _settings.indicatorPrefab;
        }
        
        private FormationType GetDefaultFormationType(EnemyCrowd crowd)
        {
            return crowd != null ? crowd.defaultFormationType : _settings.formationType;
        }
        
        private float GetDefaultFormationSize(EnemyCrowd crowd)
        {
            return crowd != null ? crowd.defaultFormationSize : _settings.formationSize;
        }
        
        private float GetDefaultFormationSpacing(EnemyCrowd crowd)
        {
            return crowd != null ? crowd.defaultFormationSpacing : _settings.formationSpacing;
        }
        
        private float GetDefaultFormationThickness(EnemyCrowd crowd)
        {
            return crowd != null ? crowd.defaultFormationThickness : _settings.formationThickness;
        }
        
        #endregion
    }
}
