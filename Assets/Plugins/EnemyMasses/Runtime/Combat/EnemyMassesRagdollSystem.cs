using System.Collections.Generic;
using UnityEngine;
using GPUInstancerPro.CrowdAnimations;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Manages ragdoll physics activation and deactivation for dead agents.
    /// Handles component caching, impulse application, and workflow transitions.
    /// </summary>
    public class EnemyMassesRagdollSystem
    {
        private readonly CombatSettings _settings;
        private readonly List<EnemyCrowd> _enemyCrowds;
        
        private bool _ragdollComponentsWarningLogged;
        private HashSet<int> _loggedMissingAnimatorStates = new HashSet<int>();

        public event System.Action<CrowdAgent> OnRagdollActivated;
        public event System.Action<CrowdAgent> OnRagdollDeactivated;

        /// <summary>
        /// Creates a new ragdoll system.
        /// </summary>
        public EnemyMassesRagdollSystem(CombatSettings settings, List<EnemyCrowd> enemyCrowds)
        {
            _settings = settings ?? throw new System.ArgumentNullException(nameof(settings));
            _enemyCrowds = enemyCrowds ?? throw new System.ArgumentNullException(nameof(enemyCrowds));
        }

        /// <summary>
        /// Checks if ragdoll should be used for a specific agent (per-faction setting).
        /// </summary>
        public bool ShouldUseRagdollForAgent(CrowdAgent agent)
        {
            if (agent == null)
                return false;

            // Check if agent belongs to a faction by index
            if (agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count)
            {
                EnemyCrowd crowd = _enemyCrowds[agent.enemyCrowdIndex];
                if (crowd != null)
                    return crowd.enableRagdollOnDeath;
            }

            // Fallback: use direct reference if index was not set correctly
            if (agent.enemyCrowd != null)
                return agent.enemyCrowd.enableRagdollOnDeath;

            return false;
        }

        /// <summary>
        /// Prepares ragdoll impulse for an agent based on attacker orientation.
        /// </summary>
        public void PrepareRagdollImpulse(CrowdAgent agent, Vector3? attackerForward, Vector3? attackerUp)
        {
            if (agent == null)
                return;

            Vector3 baseImpulse = SampleBaseRagdollImpulse();
            Vector3 computedImpulse = Vector3.zero;

            if (baseImpulse != Vector3.zero)
            {
                if (attackerForward.HasValue && attackerForward.Value.sqrMagnitude > 0.0001f)
                {
                    Vector3 forward = attackerForward.Value;
                    Vector3 up = attackerUp.HasValue && attackerUp.Value.sqrMagnitude > 0.0001f
                        ? attackerUp.Value
                        : Vector3.up;
                    computedImpulse = RotateImpulseIntoWorld(baseImpulse, forward, up);
                }
                else if (agent.hasAttackerOrientation && agent.lastAttackerForward.sqrMagnitude > 0.0001f)
                {
                    Vector3 forward = agent.lastAttackerForward;
                    Vector3 up = agent.lastAttackerUp.sqrMagnitude > 0.0001f ? agent.lastAttackerUp : Vector3.up;
                    computedImpulse = RotateImpulseIntoWorld(baseImpulse, forward, up);
                }
                else
                {
                    computedImpulse = baseImpulse;
                }
            }

            agent.pendingRagdollImpulse = computedImpulse;
            agent.hasPendingRagdollImpulse = computedImpulse != Vector3.zero;
        }

        /// <summary>
        /// Attempts to activate ragdoll physics for an agent.
        /// Requires Bone Tracker workflow to be active.
        /// </summary>
        public void TryActivateRagdoll(CrowdAgent agent, System.Action<CrowdAgent> forceBoneTrackerCallback)
        {
            if (agent == null)
            {
                Debug.LogWarning("[Ragdoll] TryActivateRagdoll called with null agent");
                return;
            }
            
            if (!ShouldUseRagdollForAgent(agent))
            {
                Debug.Log($"[Ragdoll] Agent {agent.index} not eligible for ragdoll (faction setting)");
                return;
            }
            
            if (agent.ragdollActivated)
            {
                Debug.Log($"[Ragdoll] Agent {agent.index} ragdoll already activated");
                return;
            }

            agent.ragdollPending = false;

            GPUICrowdInstance crowdInstance = agent.crowdInstance;
            if (crowdInstance == null)
            {
                Debug.LogWarning($"[Ragdoll] Agent {agent.index} has no crowdInstance");
                return;
            }
            
            Debug.Log($"[Ragdoll] Activating ragdoll for agent {agent.index}, workflow: {crowdInstance.AnimatorWorkflowID}, hasImpulse: {agent.hasPendingRagdollImpulse}, impulse: {agent.pendingRagdollImpulse}");

            // Verify Bone Tracker workflow is active
            if (crowdInstance.AnimatorWorkflowID != GPUIAWBoneTracker.WORKFLOW_ID)
            {
                // Try to force it one more time
                forceBoneTrackerCallback?.Invoke(agent);

                // Check again after forcing
                if (crowdInstance.AnimatorWorkflowID != GPUIAWBoneTracker.WORKFLOW_ID)
                {
                    if (!_ragdollComponentsWarningLogged)
                    {
                        Debug.LogWarning($"Ragdoll activation failed. Agent workflow is '{agent.currentWorkflow}' (ID: {crowdInstance.AnimatorWorkflowID}) but needs Bone Tracker (ID: {GPUIAWBoneTracker.WORKFLOW_ID}). " +
                                       "Make sure the Bone Tracker workflow is available and not overridden.");
                        _ragdollComponentsWarningLogged = true;
                    }
                    return;
                }
            }

            EnsureRagdollComponentCache(agent);

            var animator = crowdInstance.Animator;

            if (animator != null)
            {
                agent.animatorWasEnabled = animator.enabled;
                agent.animatorPreviousSpeed = !Mathf.Approximately(animator.speed, 0f) ? animator.speed : 1f;
                if (animator.enabled)
                {
                    animator.enabled = false;
                }
                else
                {
                    animator.speed = 0f;
                }
            }

            // CRITICAL: Disable the main character collider/controller if present
            // This prevents the main collider from blocking ragdoll bones
            DisableMainCharacterCollider(agent);

            bool foundRigidbody = false;
            int rigidbodyCount = 0;
            Rigidbody[] rigidbodies = agent.ragdollRigidbodies;
            if (rigidbodies != null)
            {
                for (int i = 0; i < rigidbodies.Length; i++)
                {
                    Rigidbody rb = rigidbodies[i];
                    if (rb == null)
                        continue;

                    // Enable physics simulation
                    rb.isKinematic = false;
                    rb.detectCollisions = true;
                    
                    // Ensure rigidbody isn't using interpolation that might cause issues
                    rb.interpolation = RigidbodyInterpolation.None;
                    
                    // Wake up the rigidbody to ensure physics processes it
                    rb.WakeUp();
                    
                    foundRigidbody = true;
                    rigidbodyCount++;
                }
            }
            
            Debug.Log($"[Ragdoll] Agent {agent.index}: Found {rigidbodyCount} rigidbodies (cached array: {(rigidbodies != null ? rigidbodies.Length.ToString() : "null")})");

            // Enable all ragdoll colliders
            // We need to enable colliders for ground collision - enable ALL non-trigger colliders
            // except the main character collider (which we disabled above)
            int colliderCount = 0;
            int triggerSkipped = 0;
            int mainColliderSkipped = 0;
            Collider[] colliders = agent.ragdollColliders;
            
            Debug.Log($"[Ragdoll] Agent {agent.index}: Collider cache has {(colliders != null ? colliders.Length.ToString() : "null")} colliders");
            
            if (colliders != null)
            {
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider col = colliders[i];
                    if (col == null)
                        continue;

                    // Skip trigger colliders - they don't provide physical collision
                    if (col.isTrigger)
                    {
                        triggerSkipped++;
                        continue;
                    }
                    
                    // Skip the main character collider (already disabled separately)
                    if (col == agent.mainCharacterCollider)
                    {
                        mainColliderSkipped++;
                        continue;
                    }

                    // Enable ALL non-trigger colliders (ragdoll bones need them for ground collision)
                    // The collider might not have attachedRigidbody if it's on a different GO than the rigidbody
                    col.enabled = true;
                    colliderCount++;
                    
                    // Log first few colliders for debugging
                    if (colliderCount <= 3)
                    {
                        Debug.Log($"[Ragdoll] Agent {agent.index}: Enabled collider '{col.name}' (type: {col.GetType().Name}, attachedRB: {(col.attachedRigidbody != null ? col.attachedRigidbody.name : "none")})");
                    }
                }
            }
            
            Debug.Log($"[Ragdoll] Agent {agent.index}: Enabled {colliderCount} colliders (skipped {triggerSkipped} triggers, {mainColliderSkipped} main)");
            
            // If no colliders were enabled but we have rigidbodies, there's a setup issue
            if (colliderCount == 0 && rigidbodyCount > 0)
            {
                Debug.LogWarning($"[Ragdoll] Agent {agent.index}: WARNING - {rigidbodyCount} rigidbodies but 0 colliders enabled! " +
                    "The ragdoll won't collide with ground. Check that ragdoll bones have Collider components.");
            }

            Vector3 impulseToApply;
            if (agent.hasPendingRagdollImpulse)
            {
                impulseToApply = agent.pendingRagdollImpulse;
                Debug.Log($"[Ragdoll] Agent {agent.index}: Using pending impulse: {impulseToApply}");
            }
            else
            {
                Vector3 baseImpulse = SampleBaseRagdollImpulse();
                Debug.Log($"[Ragdoll] Agent {agent.index}: Sampled base impulse: {baseImpulse}, hasAttackerOrientation: {agent.hasAttackerOrientation}, attackerForward: {agent.lastAttackerForward}");
                if (baseImpulse != Vector3.zero && agent.hasAttackerOrientation && agent.lastAttackerForward.sqrMagnitude > 0.0001f)
                {
                    Vector3 up = agent.lastAttackerUp.sqrMagnitude > 0.0001f ? agent.lastAttackerUp : Vector3.up;
                    impulseToApply = RotateImpulseIntoWorld(baseImpulse, agent.lastAttackerForward, up);
                    Debug.Log($"[Ragdoll] Agent {agent.index}: Rotated impulse to world: {impulseToApply}");
                }
                else
                {
                    impulseToApply = baseImpulse;
                }
            }

            if (foundRigidbody && impulseToApply != Vector3.zero)
            {
                Rigidbody targetRb = null;
                for (int i = 0; i < (rigidbodies != null ? rigidbodies.Length : 0); i++)
                {
                    if (rigidbodies[i] != null)
                    {
                        targetRb = rigidbodies[i];
                        break;
                    }
                }

                if (targetRb != null)
                {
                    targetRb.AddForce(impulseToApply, ForceMode.Impulse);
                    Debug.Log($"[Ragdoll] Agent {agent.index}: Applied impulse {impulseToApply} to {targetRb.name}");
                }
                else
                {
                    Debug.LogWarning($"[Ragdoll] Agent {agent.index}: Could not find valid rigidbody to apply impulse!");
                }
            }
            else if (!foundRigidbody)
            {
                Debug.LogWarning($"[Ragdoll] Agent {agent.index}: No rigidbodies found, cannot apply impulse");
            }
            else if (impulseToApply == Vector3.zero)
            {
                Debug.Log($"[Ragdoll] Agent {agent.index}: Impulse is zero, ragdoll will just fall naturally");
            }

            agent.hasPendingRagdollImpulse = false;
            agent.pendingRagdollImpulse = Vector3.zero;

            if (!foundRigidbody && !_ragdollComponentsWarningLogged)
            {
                string factionName = GetAgentFactionName(agent);
                Debug.LogWarning($"Ragdoll is enabled for faction '{factionName}' but no Rigidbody components were found on the crowd instance '{crowdInstance.name}' hierarchy. " +
                               "Make sure your ragdoll has Rigidbody components on the bones.");
                _ragdollComponentsWarningLogged = true;
            }

            // Disable navigation only when ragdolling a dead agent with ragdoll-on-death enabled.
            var navigationAgent = agent.navigationAgent;
            if (agent.isDead && ShouldUseRagdollForAgent(agent) && navigationAgent != null)
            {
                Debug.Log($"[NAVAGENT_DISABLE_TRACE] ragdoll activation disabling nav agent index={agent.index} hasImpulse={agent.hasNavmeshOffsetImpulse} enabled={navigationAgent.Enabled} navType={navigationAgent.NavigationType}");
                navigationAgent.Enabled = false;
            }

            agent.ragdollActivated = foundRigidbody;
            
            Debug.Log($"[Ragdoll] Agent {agent.index}: Ragdoll activation complete, activated={foundRigidbody}");
            
            if (foundRigidbody)
                OnRagdollActivated?.Invoke(agent);
        }

        /// <summary>
        /// Deactivates ragdoll physics for an agent.
        /// </summary>
        public void DeactivateRagdollPhysics(CrowdAgent agent, bool force = false)
        {
            if ((!ShouldUseRagdollForAgent(agent) && !force) || agent == null)
                return;

            GPUICrowdInstance crowdInstance = agent.crowdInstance;
            if (crowdInstance == null)
                return;

            // Use forced cache when force=true to handle prefabs with ragdoll components
            // even if the faction doesn't have ragdoll enabled
            if (force)
                EnsureRagdollComponentCacheForced(agent);
            else
                EnsureRagdollComponentCache(agent);

            var rigidbodies = agent.ragdollRigidbodies;
            if (rigidbodies != null)
            {
                for (int i = 0; i < rigidbodies.Length; i++)
                {
                    Rigidbody rb = rigidbodies[i];
                    if (rb == null)
                        continue;

                    rb.isKinematic = true;
                    rb.detectCollisions = false;
                }
            }

            var colliders = agent.ragdollColliders;
            if (colliders != null)
            {
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider col = colliders[i];
                    if (col == null)
                        continue;

                    // Skip triggers
                    if (col.isTrigger)
                        continue;
                    
                    // Skip main character collider (handled separately)
                    if (col == agent.mainCharacterCollider)
                        continue;

                    // Disable ALL ragdoll colliders (matching what we enable in TryActivateRagdoll)
                    col.enabled = false;
                }
            }
            
            // Re-enable main character collider for respawn
            if (agent.mainCharacterCollider != null)
                agent.mainCharacterCollider.enabled = true;
            
            // Re-enable CharacterController for respawn
            if (agent.characterController != null)
                agent.characterController.enabled = true;

            if (agent.navigationAgent != null && !agent.navigationAgent.Enabled)
                agent.navigationAgent.Enabled = true;

            agent.ragdollActivated = false;
            agent.ragdollPending = false;
            agent.hasPendingRagdollImpulse = false;
            agent.pendingRagdollImpulse = Vector3.zero;
            agent.hasAttackerOrientation = false;
            agent.lastAttackerForward = Vector3.zero;
            agent.lastAttackerUp = Vector3.up;
            
            OnRagdollDeactivated?.Invoke(agent);
        }

        #region Component Caching

        /// <summary>
        /// Ensures ragdoll components (Rigidbodies and Colliders) are cached for the agent.
        /// </summary>
        public void EnsureRagdollComponentCache(CrowdAgent agent)
        {
            if (!ShouldUseRagdollForAgent(agent) || agent == null)
                return;

            EnsureRagdollComponentCacheForced(agent);
        }
        
        /// <summary>
        /// Ensures ragdoll components are cached regardless of faction settings.
        /// Used during spawn to detect and disable any ragdoll components in the prefab.
        /// </summary>
        private void EnsureRagdollComponentCacheForced(CrowdAgent agent)
        {
            if (agent == null)
                return;
                
            GPUICrowdInstance crowdInstance = agent.crowdInstance;
            if (crowdInstance == null)
                return;

            // Cache ragdoll rigidbodies (on bone children) - includeInactive=true to find disabled ones
            if (agent.ragdollRigidbodies == null || agent.ragdollRigidbodies.Length == 0)
            {
                agent.ragdollRigidbodies = crowdInstance.GetComponentsInChildren<Rigidbody>(true);
                // Debug log removed for production build
            }

            // Cache ragdoll colliders (on bone children) - includeInactive=true to find disabled ones
            if (agent.ragdollColliders == null || agent.ragdollColliders.Length == 0)
            {
                agent.ragdollColliders = crowdInstance.GetComponentsInChildren<Collider>(true);
                // Debug logs removed for production build
                
                // Warn if no colliders found (only if ragdoll is actually enabled for this faction)
                if (agent.ragdollColliders.Length == 0 && ShouldUseRagdollForAgent(agent))
                {
                    Debug.LogWarning($"[Ragdoll] Agent {agent.index}: NO COLLIDERS found in hierarchy of '{crowdInstance.name}'! " +
                        "Ragdoll needs Collider components on bones for ground collision.");
                }
            }
            
            // Cache main character collider (on root - typically CapsuleCollider for NavMesh agents)
            // This collider needs to be disabled during ragdoll to prevent interference
            if (agent.mainCharacterCollider == null)
            {
                // Look for collider on the root that's NOT part of the ragdoll (no attached rigidbody)
                var rootCollider = crowdInstance.GetComponent<Collider>();
                if (rootCollider != null && rootCollider.attachedRigidbody == null)
                {
                    agent.mainCharacterCollider = rootCollider;
                }
                else
                {
                    // Also check for CapsuleCollider specifically (common for character controllers)
                    var capsule = crowdInstance.GetComponent<CapsuleCollider>();
                    if (capsule != null && capsule.attachedRigidbody == null)
                    {
                        agent.mainCharacterCollider = capsule;
                    }
                }
            }
            
            // Cache CharacterController if present
            if (agent.characterController == null)
            {
                agent.characterController = crowdInstance.GetComponent<CharacterController>();
                if (agent.characterController != null)
                    Debug.Log($"[Ragdoll] Agent {agent.index}: Found CharacterController");
            }
        }
        
        /// <summary>
        /// Disables the main character collider/controller to prevent interference with ragdoll.
        /// </summary>
        private void DisableMainCharacterCollider(CrowdAgent agent)
        {
            if (agent == null)
                return;
            
            // Disable main character collider (prevents ragdoll from being pushed by the capsule)
            if (agent.mainCharacterCollider != null && agent.mainCharacterCollider.enabled)
            {
                agent.mainCharacterCollider.enabled = false;
                Debug.Log($"[Ragdoll] Agent {agent.index}: Disabled main character collider ({agent.mainCharacterCollider.GetType().Name})");
            }
            
            // Disable CharacterController (it also has collision)
            if (agent.characterController != null && agent.characterController.enabled)
            {
                agent.characterController.enabled = false;
                Debug.Log($"[Ragdoll] Agent {agent.index}: Disabled CharacterController");
            }
        }

        #endregion

        #region Impulse Calculation

        private Vector3 SampleBaseRagdollImpulse()
        {
            // If both range vectors are zero, fall back to the legacy single impulse value
            bool hasRange = _settings.ragdollImpulseMin != Vector3.zero || _settings.ragdollImpulseMax != Vector3.zero;
            if (!hasRange)
                return _settings.ragdollImpulse;

            float minX = Mathf.Min(_settings.ragdollImpulseMin.x, _settings.ragdollImpulseMax.x);
            float maxX = Mathf.Max(_settings.ragdollImpulseMin.x, _settings.ragdollImpulseMax.x);
            float minY = Mathf.Min(_settings.ragdollImpulseMin.y, _settings.ragdollImpulseMax.y);
            float maxY = Mathf.Max(_settings.ragdollImpulseMin.y, _settings.ragdollImpulseMax.y);
            float minZ = Mathf.Min(_settings.ragdollImpulseMin.z, _settings.ragdollImpulseMax.z);
            float maxZ = Mathf.Max(_settings.ragdollImpulseMin.z, _settings.ragdollImpulseMax.z);

            float RandomBetween(float a, float b)
            {
                if (Mathf.Approximately(a, b))
                    return a;
                return Random.Range(a, b);
            }

            float x = RandomBetween(minX, maxX);
            float y = RandomBetween(minY, maxY);
            float z = RandomBetween(minZ, maxZ);

            return new Vector3(x, y, z);
        }

        private Vector3 RotateImpulseIntoWorld(Vector3 baseImpulse, Vector3 forward, Vector3 up)
        {
            if (baseImpulse == Vector3.zero)
                return Vector3.zero;

            Vector3 forwardNorm = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.zero;
            if (forwardNorm == Vector3.zero)
                return baseImpulse;

            Vector3 upNorm = up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
            if (Mathf.Abs(Vector3.Dot(forwardNorm, upNorm)) > 0.999f)
            {
                upNorm = Mathf.Abs(Vector3.Dot(forwardNorm, Vector3.up)) < 0.999f ? Vector3.up : Vector3.right;
            }

            Quaternion rotation = Quaternion.LookRotation(forwardNorm, upNorm);
            return rotation * baseImpulse;
        }

        #endregion

        #region Helper Methods

        private string GetAgentFactionName(CrowdAgent agent)
        {
            if (agent == null)
                return "Unknown";

            if (agent.enemyCrowdIndex >= 0 && agent.enemyCrowdIndex < _enemyCrowds.Count)
            {
                EnemyCrowd crowd = _enemyCrowds[agent.enemyCrowdIndex];
                if (crowd != null)
                    return crowd.factionName;
            }

            return "Unknown";
        }

        #endregion
    }
}
