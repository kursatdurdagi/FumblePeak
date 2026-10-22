using UnityEngine;
using System.Collections.Generic;
using GPUInstancerPro.CrowdAnimations;

namespace Arawn.EnemyMasses.Runtime.Navigation.Climbing
{
    /// <summary>
    /// Integrates NavMeshLink climbing with GPU Instancer Pro Crowd Animations.
    /// Handles setting climb animations for agents traversing NavMeshLinks.
    /// 
    /// This is a CENTRAL CONTROLLER - only ONE is needed in the scene.
    /// Attach it to the same GameObject as NavMeshLinkTraversalHandler.
    /// 
    /// Animation clips are sourced from each agent's EnemyCrowd.climbingAnimationConfig,
    /// allowing different factions to have different climb animations.
    /// 
    /// Uses agent.crowdInstance.StartAnimation() for GPU Instancer Pro ComputeAnimator workflow.
    /// </summary>
    [AddComponentMenu("Enemy Masses/Climbing/NavMesh Link Crowd Animation Handler")]
    [RequireComponent(typeof(NavMeshLinkTraversalHandler))]
    public class NavMeshLinkCrowdAnimationHandler : MonoBehaviour
    {
        [Header("Animation Settings")]
        [Tooltip("Animation transition speed for crossfade")]
        [SerializeField] [Range(0.1f, 2f)] private float transitionSpeed = 0.2f;
        
        [Tooltip("Speed multiplier for climb animation playback")]
        [SerializeField] [Range(0.5f, 2f)] private float animationSpeedMultiplier = 1f;
        
        [Header("References")]
        [Tooltip("The crowd controller managing the agents (auto-found if not set)")]
        [SerializeField] private EnemyMassesCrowdController crowdController;
        
        [Header("Debug")]
        [SerializeField] private bool debugMode = false;
        
        private NavMeshLinkTraversalHandler _traversalHandler;
        private HashSet<int> _animatedAgents = new HashSet<int>();
        
        private void Awake()
        {
            _traversalHandler = GetComponent<NavMeshLinkTraversalHandler>();
            
            if (crowdController == null)
            {
                crowdController = FindFirstObjectByType<EnemyMassesCrowdController>();
            }
        }
        
        private void LateUpdate()
        {
            if (crowdController == null) return;
            
            int agentCount = crowdController.GetAgentCount();
            if (agentCount == 0) return;
            
            // Update animations for climbing agents
            for (int i = 0; i < agentCount; i++)
            {
                var agent = crowdController.GetAgent(i);
                if (agent == null || agent.isDead) continue;
                
                bool isClimbing = _traversalHandler.IsAgentClimbing(agent.index);
                bool wasAnimated = _animatedAgents.Contains(agent.index);
                
                if (isClimbing && !wasAnimated)
                {
                    // Start climb animation
                    bool ascending = _traversalHandler.IsAgentAscending(agent.index);
                    SetClimbAnimation(agent, ascending);
                    _animatedAgents.Add(agent.index);
                }
                else if (!isClimbing && wasAnimated)
                {
                    // Stop climb animation
                    ResetAnimation(agent);
                    _animatedAgents.Remove(agent.index);
                }
                else if (isClimbing)
                {
                    // Update animation progress if needed
                    UpdateClimbAnimation(agent);
                }
            }
            
            // Clean up dead agents
            _animatedAgents.RemoveWhere(idx => 
            {
                var agent = crowdController.GetAgent(idx);
                return agent == null || agent.isDead;
            });
        }
        
        /// <summary>
        /// Get the climb animation config from the agent's EnemyCrowd.
        /// </summary>
        private ClimbingAnimationConfig GetClimbConfig(CrowdAgent agent)
        {
            if (agent.enemyCrowd != null)
            {
                return agent.enemyCrowd.climbingAnimationConfig;
            }
            return null;
        }
        
        /// <summary>
        /// Set climbing animation for an agent.
        /// Uses the agent's EnemyCrowd.climbingAnimationConfig for the appropriate clips/parameters.
        /// Animation speed is adjusted to match the climb duration.
        /// Respects EnemyCrowd.animatorWorkflow to choose between GPU Instancer Pro and Mecanim.
        /// </summary>
        private void SetClimbAnimation(CrowdAgent agent, bool ascending)
        {
            var config = GetClimbConfig(agent);
            if (config == null) 
            {
                Debug.LogError($"<color=red>❌ CLIMB ANIM FAIL: Agent {agent.index} - No climbingAnimationConfig in EnemyCrowd!</color>");
                return;
            }
            
            // Check which workflow to use from EnemyCrowd settings
            var workflow = agent.enemyCrowd?.animatorWorkflow ?? AnimatorWorkflowOverride.ComputeAnimator;
            bool useMecanim = (workflow == AnimatorWorkflowOverride.MecanimReader || 
                               workflow == AnimatorWorkflowOverride.BoneTracker);
            
            // Get climb duration to sync animation speed
            float climbDuration = _traversalHandler.GetAgentClimbDuration(agent.index);
            
            // Use GPU Instancer Pro via crowdInstance.StartAnimation() for ComputeAnimator workflow
            if (!useMecanim)
            {
                if (agent.crowdInstance == null)
                {
                    Debug.LogError($"<color=red>❌ CLIMB ANIM FAIL: Agent {agent.index} - crowdInstance is NULL!</color>");
                    return;
                }
                
                AnimationClip clip = ascending ? config.climbUpClip : config.climbDownClip;
                
                if (clip == null)
                {
                    Debug.LogError($"<color=red>❌ CLIMB ANIM FAIL: Agent {agent.index} - {(ascending ? "climbUpClip" : "climbDownClip")} is NULL in EnemyCrowd.climbingAnimationConfig!</color>");
                    return;
                }
                
                // Get the climbable for this agent to check animation speed settings
                var climbable = _traversalHandler.GetAgentClimbable(agent.index);
                
                // Calculate animation speed - either synced to climb duration or fixed override
                float animSpeed;
                if (climbable != null && !climbable.SyncAnimationToClimb)
                {
                    // Use fixed animation speed from climbable settings
                    animSpeed = climbable.AnimationSpeedOverride * animationSpeedMultiplier;
                }
                else
                {
                    // Sync animation to climb duration
                    // If clip is 2 seconds and climb is 6 seconds, speed = 0.33
                    animSpeed = animationSpeedMultiplier;
                    if (climbDuration > 0 && clip.length > 0)
                    {
                        animSpeed = (clip.length / climbDuration) * animationSpeedMultiplier;
                    }
                }
                
                // Use crowdInstance.StartAnimation - the correct API for GPU Instancer Pro
                // Parameters: (clip, normalizedClipTime, speed, transitionTime, isLoopingOverride, isSyncTime)
                agent.crowdInstance.StartAnimation(clip, -1f, animSpeed, transitionSpeed, true, false);
                return;
            }
            
            // Use Mecanim for MecanimReader/BoneTracker workflows
            if (useMecanim)
            {
                var animator = agent.transform.GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    // Set climbing bool parameter
                    if (!string.IsNullOrEmpty(config.climbingBoolParameter))
                    {
                        animator.SetBool(config.climbingBoolParameter, true);
                    }
                    
                    // Set climb speed/direction parameter
                    if (!string.IsNullOrEmpty(config.climbSpeedParameter))
                    {
                        animator.SetFloat(config.climbSpeedParameter, ascending ? 1f : -1f);
                    }
                    
                    if (debugMode)
                    {
                        Debug.Log($"[NavMeshLinkCrowdAnimationHandler] Agent {agent.index} MECANIM: {config.climbingBoolParameter}=true, {config.climbSpeedParameter}={(ascending ? 1f : -1f)}");
                    }
                }
            }
        }
        
        /// <summary>
        /// Reset agent animation after climbing.
        /// Plays optional transition animation if configured, then returns to locomotion.
        /// Respects EnemyCrowd.animatorWorkflow to choose between GPU Instancer Pro and Mecanim.
        /// </summary>
        private void ResetAnimation(CrowdAgent agent)
        {
            var config = GetClimbConfig(agent);
            
            // Check which workflow to use
            var workflow = agent.enemyCrowd?.animatorWorkflow ?? AnimatorWorkflowOverride.ComputeAnimator;
            bool useMecanim = (workflow == AnimatorWorkflowOverride.MecanimReader || 
                               workflow == AnimatorWorkflowOverride.BoneTracker);
            
            // Determine if agent was ascending (for choosing correct transition clip)
            bool wasAscending = _traversalHandler.IsAgentAscending(agent.index);
            
            // GPU Instancer Pro reset via crowdInstance.StartAnimation()
            if (!useMecanim && agent.crowdInstance != null)
            {
                // Check for transition clip first
                AnimationClip transitionClip = wasAscending ? config?.climbUpTransitionClip : config?.climbDownTransitionClip;
                
                if (transitionClip != null)
                {
                    // Play transition clip (non-looping)
                    float transitionDuration = config.transitionClipDuration > 0 ? config.transitionClipDuration : transitionClip.length;
                    float transitionSpeed = transitionClip.length / transitionDuration;
                    
                    agent.crowdInstance.StartAnimation(transitionClip, -1f, transitionSpeed, transitionSpeed, false, false);
                    
                    if (debugMode)
                    {
                        Debug.Log($"[NavMeshLinkCrowdAnimationHandler] Agent {agent.index} playing transition clip: {transitionClip.name} (duration={transitionDuration:F2}s)");
                    }
                    
                    // Note: The transition clip will play once, then the EnemyMassesAnimationController 
                    // will take over and blend to locomotion since isClimbing is now false
                }
                else
                {
                    // No transition clip - go directly to idle
                    AnimationClip idleClip = config?.climbIdleClip;
                    
                    // If no specific climb idle, try the standard idle from EnemyCrowd
                    if (idleClip == null && agent.enemyCrowd != null)
                    {
                        idleClip = agent.enemyCrowd.idleClipOverride;
                    }
                    
                    if (idleClip != null)
                    {
                        // Reset to idle animation at normal speed
                        agent.crowdInstance.StartAnimation(idleClip, -1f, 1f, transitionSpeed, true, false);
                    }
                    
                    if (debugMode)
                    {
                        Debug.Log($"[NavMeshLinkCrowdAnimationHandler] Agent {agent.index} finished climbing, reset to idle (no transition clip)");
                    }
                }
                return;
            }
            
            // Mecanim reset
            if (useMecanim)
            {
                var animator = agent.transform.GetComponentInChildren<Animator>();
                if (animator != null && config != null)
                {
                    if (!string.IsNullOrEmpty(config.climbingBoolParameter))
                    {
                        animator.SetBool(config.climbingBoolParameter, false);
                    }
                }
            }
        }
        
        /// <summary>
        /// Update climb animation progress (for animation sync if needed).
        /// </summary>
        private void UpdateClimbAnimation(CrowdAgent agent)
        {
            // Could sync animation progress with climb progress if needed
            // For looping animations, this might not be needed
            // For single-play animations, you might want to set normalized time
            // This is left as a hook for future enhancement
        }
    }
}
