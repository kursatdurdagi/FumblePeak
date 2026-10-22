using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using GPUInstancerPro.CrowdAnimations;

namespace Arawn.EnemyMasses.Runtime.Civil
{
    /// <summary>
    /// Manages social interactions between civil agents.
    /// Handles detection, pairing, positioning, and animation synchronization.
    /// </summary>
    public class CivilSocialInteractionManager
    {
        private readonly CivilNavigationController _civilController;
        private readonly List<CrowdAgent> _agents;
        private readonly List<EnemyCrowd> _crowds;
        
        // Active interactions
        private readonly List<ActiveSocialInteraction> _activeInteractions = new List<ActiveSocialInteraction>();
        private readonly HashSet<int> _agentsInInteraction = new HashSet<int>();
        
        // Per-agent social state
        private readonly Dictionary<int, AgentSocialState> _socialStates = new Dictionary<int, AgentSocialState>();

        
        // Timing
        private float _lastInteractionCheck;
        private int _currentCheckIndex;
        
        // Spatial lookup for efficient partner finding
        private readonly Dictionary<int, List<int>> _spatialBuckets = new Dictionary<int, List<int>>();
        private const float BUCKET_SIZE = 5f;
        
        public CivilSocialInteractionManager(
            CivilNavigationController civilController,
            List<CrowdAgent> agents,
            List<EnemyCrowd> crowds)
        {
            _civilController = civilController;
            _agents = agents;
            _crowds = crowds;
        }
        
        /// <summary>
        /// Updates the social interaction system.
        /// </summary>
        public void Update(float deltaTime)
        {
            // Update active interactions
            UpdateActiveInteractions(deltaTime);
            
            // Periodic check for new interactions
            CheckForNewInteractions(deltaTime);
        }
        
        /// <summary>
        /// Gets or creates social state for an agent.
        /// </summary>
        public AgentSocialState GetOrCreateSocialState(int agentIndex)
        {
            if (!_socialStates.TryGetValue(agentIndex, out var state))
            {
                state = new AgentSocialState();
                _socialStates[agentIndex] = state;
            }
            return state;
        }
        
        /// <summary>
        /// Checks if an agent is currently in a social interaction.
        /// </summary>
        public bool IsAgentInInteraction(int agentIndex)
        {
            return _agentsInInteraction.Contains(agentIndex);
        }
        
        /// <summary>
        /// Gets the active interaction for an agent (if any).
        /// </summary>
        public ActiveSocialInteraction GetAgentInteraction(int agentIndex)
        {
            foreach (var interaction in _activeInteractions)
            {
                if (interaction.initiatorIndex == agentIndex || interaction.responderIndex == agentIndex)
                    return interaction;
            }
            return null;
        }
        
        /// <summary>
        /// Cleans up social state when an agent is removed.
        /// </summary>
        public void OnAgentRemoved(int agentIndex)
        {
            // Cancel any active interaction
            CancelAgentInteraction(agentIndex);
            
            // Remove social state
            _socialStates.Remove(agentIndex);
        }
        
        #region Interaction Checking
        
        private void CheckForNewInteractions(float deltaTime)
        {
            var settings = GetSocialSettings();
            if (settings == null || !settings.enableSocialInteractions)
                return;
            
            float currentTime = Time.time;
            if (currentTime - _lastInteractionCheck < settings.interactionCheckInterval)
                return;
            
            _lastInteractionCheck = currentTime;
            
            // Check if we can have more interactions
            if (_activeInteractions.Count >= settings.maxConcurrentInteractions)
                return;
            
            // Update spatial buckets for efficient lookup
            UpdateSpatialBuckets();
            
            // Check a subset of agents each frame
            int checksThisFrame = 0;
            int agentCount = _agents.Count;
            
            while (checksThisFrame < settings.maxChecksPerFrame && _currentCheckIndex < agentCount)
            {
                var agent = _agents[_currentCheckIndex];
                _currentCheckIndex++;
                
                if (!IsValidForInteraction(agent, settings))
                    continue;
                
                TryInitiateInteraction(agent, settings, currentTime);
                checksThisFrame++;
            }
            
            // Reset index when we've checked all agents
            if (_currentCheckIndex >= agentCount)
                _currentCheckIndex = 0;
        }
        
        private bool IsValidForInteraction(CrowdAgent agent, SocialInteractionSettings settings)
        {
            if (agent == null || agent.isDead)
                return false;
            
            // Must be a civil agent
            if (!_civilController.IsAgentCivil(agent.index))
                return false;
            
            // Not already in an interaction
            if (_agentsInInteraction.Contains(agent.index))
                return false;
            
            // Check agent state
            var civilState = _civilController.GetCivilState(agent.index);
            if (civilState == null)
                return false;
            
            // Only walking agents can initiate interactions
            if (civilState.navigationState != CivilNavigationState.Walking &&
                civilState.navigationState != CivilNavigationState.Exploring)
                return false;
            
            // Check speed
            if (agent.navAgent != null && agent.navAgent.velocity.magnitude > settings.maxSpeedForInteraction)
                return false;
            
            // Check global cooldown
            var socialState = GetOrCreateSocialState(agent.index);
            if (Time.time - socialState.lastInteractionTime < settings.globalCooldown)
                return false;
            
            return true;
        }
        
        private void TryInitiateInteraction(CrowdAgent initiator, SocialInteractionSettings settings, float currentTime)
        {
            // Find potential partners nearby
            var partners = FindNearbyAgents(initiator, settings.maxDetectionDistance);
            if (partners.Count == 0)
                return;
            
            // Try each interaction type
            foreach (var interactionDef in settings.interactions)
            {
                // Check cooldown for this interaction type
                var initiatorSocial = GetOrCreateSocialState(initiator.index);
                if (initiatorSocial.IsOnCooldown(interactionDef.interactionId, currentTime))
                    continue;
                
                // Roll for trigger chance
                float chance = interactionDef.triggerChance * settings.globalInteractionChance;
                if (Random.value > chance)
                    continue;
                
                // Find a suitable partner
                CrowdAgent partner = FindSuitablePartner(initiator, partners, interactionDef, currentTime);
                if (partner == null && interactionDef.participantCount > 1)
                    continue;
                
                // Start the interaction
                StartInteraction(initiator, partner, interactionDef);
                return; // Only one interaction at a time
            }
        }
        
        private CrowdAgent FindSuitablePartner(CrowdAgent initiator, List<CrowdAgent> candidates, 
            SocialInteractionDefinition def, float currentTime)
        {
            foreach (var candidate in candidates)
            {
                if (candidate.index == initiator.index)
                    continue;
                
                // Check distance
                float distance = Vector3.Distance(initiator.transform.position, candidate.transform.position);
                if (distance > def.detectionRange)
                    continue;
                
                // Not already in interaction
                if (_agentsInInteraction.Contains(candidate.index))
                    continue;
                
                // Check candidate state
                var civilState = _civilController.GetCivilState(candidate.index);
                if (civilState == null || 
                    (civilState.navigationState != CivilNavigationState.Walking &&
                     civilState.navigationState != CivilNavigationState.Exploring))
                    continue;
                
                // Check cooldown
                var candidateSocial = GetOrCreateSocialState(candidate.index);
                if (candidateSocial.IsOnCooldown(def.interactionId, currentTime))
                    continue;
                
                return candidate;
            }
            
            return null;
        }
        
        #endregion
        
        #region Spatial Lookup
        
        private void UpdateSpatialBuckets()
        {
            _spatialBuckets.Clear();
            
            foreach (var agent in _agents)
            {
                if (agent == null || agent.isDead || !_civilController.IsAgentCivil(agent.index))
                    continue;
                
                int bucketKey = GetBucketKey(agent.transform.position);
                
                if (!_spatialBuckets.TryGetValue(bucketKey, out var bucket))
                {
                    bucket = new List<int>();
                    _spatialBuckets[bucketKey] = bucket;
                }
                
                bucket.Add(agent.index);
            }
        }
        
        private int GetBucketKey(Vector3 position)
        {
            int x = Mathf.FloorToInt(position.x / BUCKET_SIZE);
            int z = Mathf.FloorToInt(position.z / BUCKET_SIZE);
            return x * 10000 + z;
        }
        
        private List<CrowdAgent> FindNearbyAgents(CrowdAgent agent, float radius)
        {
            var result = new List<CrowdAgent>();
            Vector3 pos = agent.transform.position;
            
            int bucketRange = Mathf.CeilToInt(radius / BUCKET_SIZE);
            int centerX = Mathf.FloorToInt(pos.x / BUCKET_SIZE);
            int centerZ = Mathf.FloorToInt(pos.z / BUCKET_SIZE);
            
            for (int dx = -bucketRange; dx <= bucketRange; dx++)
            {
                for (int dz = -bucketRange; dz <= bucketRange; dz++)
                {
                    int key = (centerX + dx) * 10000 + (centerZ + dz);
                    
                    if (_spatialBuckets.TryGetValue(key, out var bucket))
                    {
                        foreach (int agentIndex in bucket)
                        {
                            if (agentIndex >= 0 && agentIndex < _agents.Count)
                            {
                                var nearbyAgent = _agents[agentIndex];
                                if (nearbyAgent != null && nearbyAgent.index != agent.index)
                                {
                                    float dist = Vector3.Distance(pos, nearbyAgent.transform.position);
                                    if (dist <= radius)
                                    {
                                        result.Add(nearbyAgent);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            return result;
        }
        
        #endregion
        
        #region Interaction Lifecycle
        
        private void StartInteraction(CrowdAgent initiator, CrowdAgent responder, SocialInteractionDefinition def)
        {
            var interaction = new ActiveSocialInteraction
            {
                definition = def,
                initiatorIndex = initiator.index,
                responderIndex = responder?.index ?? -1,
                startTime = Time.time,
                duration = def.GetRandomDuration() + def.setupTime,
                phase = InteractionPhase.Positioning
            };
            
            // Calculate positioning
            CalculateInteractionPositions(interaction, initiator, responder);
            
            // Store original workflows
            interaction.initiatorOriginalWorkflow = initiator.currentWorkflow;
            if (responder != null)
                interaction.responderOriginalWorkflow = responder.currentWorkflow;
            
            // Mark agents as in interaction
            _agentsInInteraction.Add(initiator.index);
            if (responder != null)
                _agentsInInteraction.Add(responder.index);
            
            // Update social states
            var initiatorSocial = GetOrCreateSocialState(initiator.index);
            initiatorSocial.isInInteraction = true;
            initiatorSocial.currentInteraction = interaction;
            initiatorSocial.currentRole = InteractionRole.Initiator;
            
            if (responder != null)
            {
                var responderSocial = GetOrCreateSocialState(responder.index);
                responderSocial.isInInteraction = true;
                responderSocial.currentInteraction = interaction;
                responderSocial.currentRole = InteractionRole.Responder;
            }
            
            // Update civil navigation state
            _civilController.SetAgentSocialInteractionState(initiator.index, interaction);
            if (responder != null)
                _civilController.SetAgentSocialInteractionState(responder.index, interaction);
            
            _activeInteractions.Add(interaction);
            
            // Fire event
            def.FireStarted(initiator.index, responder?.index ?? -1);
            
            Debug.Log($"[Civil Social] Started {def.displayName} between agent {initiator.index} and {responder?.index ?? -1}");
        }
        
        private void CalculateInteractionPositions(ActiveSocialInteraction interaction, 
            CrowdAgent initiator, CrowdAgent responder)
        {
            var posConfig = interaction.definition.positioningConfig;
            Vector3 initiatorPos = initiator.transform.position;
            Vector3 responderPos = responder?.transform.position ?? initiatorPos;
            
            Vector3 midpoint = (initiatorPos + responderPos) * 0.5f;
            Vector3 direction = (responderPos - initiatorPos).normalized;
            
            if (direction.sqrMagnitude < 0.01f)
                direction = initiator.transform.forward;
            
            switch (posConfig.positioning)
            {
                case InteractionPositioning.None:
                    interaction.initiatorTargetPosition = initiatorPos;
                    interaction.responderTargetPosition = responderPos;
                    interaction.initiatorTargetForward = direction;
                    interaction.responderTargetForward = -direction;
                    break;
                    
                case InteractionPositioning.FaceToFace:
                    float halfDist = posConfig.interactionDistance * 0.5f;
                    interaction.initiatorTargetPosition = midpoint - direction * halfDist;
                    interaction.responderTargetPosition = midpoint + direction * halfDist;
                    interaction.initiatorTargetForward = direction;
                    interaction.responderTargetForward = -direction;
                    break;
                    
                case InteractionPositioning.SideBySide:
                    Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;
                    interaction.initiatorTargetPosition = midpoint - perpendicular * posConfig.interactionDistance * 0.5f;
                    interaction.responderTargetPosition = midpoint + perpendicular * posConfig.interactionDistance * 0.5f;
                    interaction.initiatorTargetForward = direction;
                    interaction.responderTargetForward = direction;
                    break;
                    
                case InteractionPositioning.PreciseWithWarping:
                    // For precise positioning, start with face-to-face and let motion warping handle the rest
                    halfDist = posConfig.interactionDistance * 0.5f;
                    interaction.initiatorTargetPosition = midpoint - direction * halfDist;
                    interaction.responderTargetPosition = midpoint + direction * halfDist;
                    interaction.initiatorTargetForward = direction;
                    interaction.responderTargetForward = -direction;
                    break;
            }
        }
        
        private void UpdateActiveInteractions(float deltaTime)
        {
            for (int i = _activeInteractions.Count - 1; i >= 0; i--)
            {
                var interaction = _activeInteractions[i];
                
                switch (interaction.phase)
                {
                    case InteractionPhase.Positioning:
                        UpdatePositioningPhase(interaction);
                        break;
                        
                    case InteractionPhase.Setup:
                        UpdateSetupPhase(interaction);
                        break;
                        
                    case InteractionPhase.Active:
                        UpdateActivePhase(interaction);
                        break;
                        
                    case InteractionPhase.Outro:
                        UpdateOutroPhase(interaction);
                        break;
                        
                    case InteractionPhase.Complete:
                        CompleteInteraction(interaction);
                        _activeInteractions.RemoveAt(i);
                        break;
                }
            }
        }
        
        private void UpdatePositioningPhase(ActiveSocialInteraction interaction)
        {
            var def = interaction.definition;
            var posConfig = def.positioningConfig;
            
            bool initiatorInPosition = MoveAgentToPosition(
                interaction.initiatorIndex,
                interaction.initiatorTargetPosition,
                interaction.initiatorTargetForward,
                posConfig);
            
            bool responderInPosition = true;
            if (interaction.responderIndex >= 0)
            {
                responderInPosition = MoveAgentToPosition(
                    interaction.responderIndex,
                    interaction.responderTargetPosition,
                    interaction.responderTargetForward,
                    posConfig);
            }
            
            if (initiatorInPosition && responderInPosition)
            {
                interaction.positioningComplete = true;
                interaction.phase = InteractionPhase.Setup;
                
                // Switch to required workflow
                var requiredWorkflow = def.GetRequiredWorkflow();
                SwitchAgentWorkflowForInteraction(interaction.initiatorIndex, requiredWorkflow);
                if (interaction.responderIndex >= 0)
                    SwitchAgentWorkflowForInteraction(interaction.responderIndex, requiredWorkflow);
            }
        }
        
        private bool MoveAgentToPosition(int agentIndex, Vector3 targetPos, Vector3 targetForward, 
            InteractionPositioningConfig config)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return true;
            
            var agent = _agents[agentIndex];
            if (agent == null || agent.navAgent == null)
                return true;
            
            Vector3 currentPos = agent.transform.position;
            float distance = Vector3.Distance(currentPos, targetPos);
            
            // Check if in position
            if (distance <= config.distanceTolerance)
            {
                // Stop agent
                if (agent.navAgent.isOnNavMesh)
                {
                    agent.navAgent.isStopped = true;
                    agent.navAgent.ResetPath();
                }
                
                // Rotate to face target direction
                Quaternion targetRot = Quaternion.LookRotation(targetForward, Vector3.up);
                agent.transform.rotation = Quaternion.RotateTowards(
                    agent.transform.rotation,
                    targetRot,
                    config.rotationSpeed * Time.deltaTime);
                
                float angleDiff = Quaternion.Angle(agent.transform.rotation, targetRot);
                return angleDiff < 5f;
            }
            
            // Move toward position
            if (agent.navAgent.isOnNavMesh)
            {
                agent.navAgent.isStopped = false;
                agent.navAgent.speed = config.positioningSpeed;
                agent.navAgent.SetDestination(targetPos);
            }
            
            return false;
        }
        
        private void UpdateSetupPhase(ActiveSocialInteraction interaction)
        {
            float elapsed = Time.time - interaction.startTime;
            
            // Wait for setup time
            if (elapsed >= interaction.definition.setupTime)
            {
                interaction.phase = InteractionPhase.Active;
                
                // Start animations
                StartInteractionAnimations(interaction);
            }
        }
        
        private void StartInteractionAnimations(ActiveSocialInteraction interaction)
        {
            var def = interaction.definition;

            _civilController.PlaySocialEffect(def.startEffect, interaction.initiatorIndex);
            if (interaction.responderIndex >= 0)
            {
                _civilController.PlaySocialEffect(def.startEffect, interaction.responderIndex);
            }
            
            // Play initiator animation
            if (def.initiatorAnimation?.clip != null)
            {
                PlayInteractionAnimation(interaction.initiatorIndex, def.initiatorAnimation);
            }
            
            // Play responder animation
            if (interaction.responderIndex >= 0 && def.responderAnimation?.clip != null)
            {
                PlayInteractionAnimation(interaction.responderIndex, def.responderAnimation);
            }
        }
        
        private void PlayInteractionAnimation(int agentIndex, SocialAnimationClip animConfig)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return;
            
            var agent = _agents[agentIndex];
            if (agent == null)
                return;
            
            // Get the animation component
            var animator = agent.crowdInstance?.GetComponent<Animator>();
            if (animator != null)
            {
                // Use Animator for MecanimReader/BoneTracker workflows
                if (animConfig.animationLayer >= 0)
                {
                    animator.CrossFade(animConfig.clip.name, animConfig.crossFadeDuration, animConfig.animationLayer);
                }
                else
                {
                    animator.CrossFade(animConfig.clip.name, animConfig.crossFadeDuration);
                }
                animator.speed = animConfig.speedMultiplier;
            }
            
            // For ComputeAnimator workflow, use GPUICrowdInstance.StartAnimation
            if (agent.currentWorkflow == HybridWorkflowType.ComputeAnimator && agent.crowdInstance != null)
            {
                agent.crowdInstance.StartAnimation(animConfig.clip, -1f, animConfig.speedMultiplier, 
                    animConfig.crossFadeDuration, animConfig.loop, true);
            }
        }
        
        private void UpdateActivePhase(ActiveSocialInteraction interaction)
        {
            if (interaction.IsComplete)
            {
                // Check for outro animation
                if (interaction.definition.outroAnimation?.clip != null)
                {
                    interaction.phase = InteractionPhase.Outro;
                    // TODO: Play outro animations
                }
                else
                {
                    interaction.phase = InteractionPhase.Complete;
                }
            }
        }
        
        private void UpdateOutroPhase(ActiveSocialInteraction interaction)
        {
            // Simple implementation - just transition to complete
            // Could be extended to wait for outro animation to finish
            interaction.phase = InteractionPhase.Complete;
        }
        
        private void CompleteInteraction(ActiveSocialInteraction interaction)
        {
            var def = interaction.definition;
            float currentTime = Time.time;
            
            // Update cooldowns and states
            var initiatorSocial = GetOrCreateSocialState(interaction.initiatorIndex);
            initiatorSocial.lastInteractionTime = currentTime;
            initiatorSocial.SetCooldown(def.interactionId, def.cooldownTime, currentTime);
            initiatorSocial.isInInteraction = false;
            initiatorSocial.currentInteraction = null;
            
            if (interaction.responderIndex >= 0)
            {
                var responderSocial = GetOrCreateSocialState(interaction.responderIndex);
                responderSocial.lastInteractionTime = currentTime;
                responderSocial.SetCooldown(def.interactionId, def.cooldownTime, currentTime);
                responderSocial.isInInteraction = false;
                responderSocial.currentInteraction = null;
            }
            
            // Remove from tracking
            _agentsInInteraction.Remove(interaction.initiatorIndex);
            if (interaction.responderIndex >= 0)
                _agentsInInteraction.Remove(interaction.responderIndex);
            
            // Restore workflows
            RestoreAgentWorkflow(interaction.initiatorIndex, interaction.initiatorOriginalWorkflow);
            if (interaction.responderIndex >= 0)
                RestoreAgentWorkflow(interaction.responderIndex, interaction.responderOriginalWorkflow);
            
            // Notify civil controller to resume navigation
            _civilController.EndAgentSocialInteraction(interaction.initiatorIndex);
            if (interaction.responderIndex >= 0)
                _civilController.EndAgentSocialInteraction(interaction.responderIndex);

            // End effects (after restoring control)
            _civilController.PlaySocialEffect(def.endEffect, interaction.initiatorIndex);
            if (interaction.responderIndex >= 0)
            {
                _civilController.PlaySocialEffect(def.endEffect, interaction.responderIndex);
            }
            
            // Fire event
            def.FireEnded(interaction.initiatorIndex, interaction.responderIndex);
            
            Debug.Log($"[Civil Social] Completed {def.displayName} between agent {interaction.initiatorIndex} and {interaction.responderIndex}");
        }
        
        private void CancelAgentInteraction(int agentIndex)
        {
            for (int i = _activeInteractions.Count - 1; i >= 0; i--)
            {
                var interaction = _activeInteractions[i];
                
                if (interaction.initiatorIndex == agentIndex || interaction.responderIndex == agentIndex)
                {
                    // Mark for completion
                    interaction.phase = InteractionPhase.Complete;
                    CompleteInteraction(interaction);
                    _activeInteractions.RemoveAt(i);
                }
            }
        }
        
        #endregion

        
        #region Workflow Management
        
        private void SwitchAgentWorkflowForInteraction(int agentIndex, HybridWorkflowType targetWorkflow)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count)
                return;
            
            var agent = _agents[agentIndex];
            if (agent?.crowdInstance == null)
                return;
            
            if (agent.currentWorkflow == targetWorkflow)
                return;
            
            int workflowId = targetWorkflow switch
            {
                HybridWorkflowType.BoneTracker => GPUIAWBoneTracker.WORKFLOW_ID,
                HybridWorkflowType.MecanimReader => GPUIAWMecanimReader.WORKFLOW_ID,
                _ => GPUIAWComputeAnimator.WORKFLOW_ID
            };
            
            agent.crowdInstance.AnimatorWorkflowID = workflowId;
            agent.currentWorkflow = targetWorkflow;
            
            // For BoneTracker/MecanimReader, may need to enable the Animator
            if (targetWorkflow == HybridWorkflowType.BoneTracker || targetWorkflow == HybridWorkflowType.MecanimReader)
            {
                var animator = agent.crowdInstance.GetComponent<Animator>();
                if (animator != null)
                    animator.enabled = true;
            }
        }
        
        private void RestoreAgentWorkflow(int agentIndex, HybridWorkflowType originalWorkflow)
        {
            SwitchAgentWorkflowForInteraction(agentIndex, originalWorkflow);
        }
        
        #endregion
        
        #region Helpers
        
        private SocialInteractionSettings GetSocialSettings()
        {
            // Get from first civil-enabled crowd
            foreach (var crowd in _crowds)
            {
                if (crowd != null && crowd.civilSettings != null && crowd.civilSettings.enableCivilMode)
                {
                    return crowd.civilSettings.socialSettings;
                }
            }
            return null;
        }
        
        /// <summary>
        /// Gets statistics about social interactions.
        /// </summary>
        public SocialInteractionStats GetStats()
        {
            return new SocialInteractionStats
            {
                activeInteractionCount = _activeInteractions.Count,
                agentsInInteraction = _agentsInInteraction.Count,
                totalSocialStates = _socialStates.Count
            };
        }
        
        #endregion
    }
    
    /// <summary>
    /// Statistics for social interaction system.
    /// </summary>
    public struct SocialInteractionStats
    {
        public int activeInteractionCount;
        public int agentsInInteraction;
        public int totalSocialStates;
    }
}
