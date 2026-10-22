using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Preset levels for performance optimization.
    /// Higher levels = more aggressive optimization = better performance but potentially lower visual quality.
    /// </summary>
    public enum PerformancePreset
    {
        /// <summary>No optimizations - full quality, highest CPU usage</summary>
        None = 0,
        /// <summary>Moderate optimizations with visible-only animation rotation</summary>
        Prioritized = 5,
        /// <summary>Light optimizations - good quality with minor CPU savings</summary>
        Moderate = 1,
        /// <summary>Balanced optimizations - reduced quality for better performance</summary>
        Aggressive = 2,
        /// <summary>Maximum optimizations - lowest quality, best performance for 1000+ agents</summary>
        VeryAggressive = 3,
        /// <summary>Manual configuration - all settings exposed for fine-tuning</summary>
        Custom = 4
    }

    /// <summary>
    /// Configuration settings for performance optimization in Enemy Masses.
    /// </summary>
    [System.Serializable]
    public class PerformanceSettings
    {
        [Header("Performance Preset")]
        [Tooltip("Quick preset for performance settings:\n" +
                 "• None: Full quality, no optimizations\n" +
                 "• Prioritized: Moderate optimizations with visible-only animation rotation\n" +
                 "• Moderate: Light throttling, good for 100-300 agents\n" +
                 "• Aggressive: Stronger throttling, good for 300-1000 agents\n" +
                 "• Very Aggressive: Maximum optimization for 1000+ agents\n" +
                 "• Custom: Manual control of all settings")]
        public PerformancePreset preset = PerformancePreset.Moderate;

        [Header("Visibility & Priority")]
        [Tooltip("Prioritize visible agents for updates")]
        public bool prioritizeVisibleAgents = true;

        [Tooltip("Rotate animation updates within visible agents when throttling")]
        public bool rotateVisibleAnimationUpdates = false;
        
        [Tooltip("Maximum distance for priority updates")]
        [Min(0f)]
        public float priorityMaxDistance = 50f;
        
        [Header("Priority-Based System")]
        [Tooltip("Enable advanced priority-based visibility updates")]
        public bool enablePriorityBasedVisibility = false;
        
        [Tooltip("How often to update frustum and priority categorization")]
        [Range(0.01f, 0.2f)]
        public float frustumUpdateInterval = 0.033f;
        
        [Tooltip("Debug priority updates in console")]
        public bool debugPriorityUpdates = false;
        
        [Tooltip("Ensure all agents get processed regularly")]
        public bool enablePriorityFailsafe = true;
        
        [Tooltip("Minimum processing rate for any agent")]
        [Range(0.05f, 0.5f)]
        public float failsafeUpdateInterval = 0.2f;
        
        [Header("High Priority (Close & Visible)")]
        [Tooltip("Update interval for high priority agents")]
        [Min(0.02f)]
        public float highPriorityUpdateInterval = 0.05f;
        
        [Tooltip("Fraction to process per frame")]
        [Range(0.3f, 1.0f)]
        public float highPriorityUpdateFraction = 0.8f;
        
        [Tooltip("Distance threshold for high priority")]
        [Min(5f)]
        public float highPriorityDistance = 30f;
        
        [Header("Medium Priority (Visible but Distant)")]
        [Tooltip("Update interval for medium priority agents")]
        [Min(0.03f)]
        public float mediumPriorityUpdateInterval = 0.1f;
        
        [Tooltip("Fraction to process per frame")]
        [Range(0.2f, 0.9f)]
        public float mediumPriorityUpdateFraction = 0.6f;
        
        [Tooltip("Distance threshold for medium priority")]
        [Min(10f)]
        public float mediumPriorityDistance = 80f;
        
        [Header("Low Priority (Distant/Hidden)")]
        [Tooltip("Update interval for low priority agents")]
        [Min(0.04f)]
        public float lowPriorityUpdateInterval = 0.15f;
        
        [Tooltip("Fraction to process per frame")]
        [Range(0.1f, 0.9f)]
        public float lowPriorityUpdateFraction = 0.4f;
        
        [Header("Legacy Visibility (when Priority disabled)")]
        [Tooltip("How often to update visibility lists")]
        [Min(0.02f)]
        public float visibilityUpdateInterval = 0.1f;
        
        [Tooltip("Fraction of agents to update each frame")]
        [Range(0.1f, 1.0f)]
        public float visibilityUpdateFraction = 0.3f;
        
        [Header("Spatial Partitioning")]
        [Tooltip("Enable spatial grid for proximity calculations")]
        public bool enableSpatialGrid = true;
        
        [Tooltip("Size of each grid cell in world units")]
        [Min(1f)]
        public float gridCellSize = 10f;
        
        [Tooltip("Process destination updates in spatial batches")]
        public bool enableSpatialBatching = true;
        
        [Tooltip("Max agents per cell per frame")]
        [Min(1)]
        public int maxAgentsPerCellPerFrame = 10;
        
        [Tooltip("Automatically scale agents per cell based on crowd size")]
        public bool enableAdaptiveScaling = true;
        
        [Tooltip("Increase limit when destination queue is large")]
        public bool enableBurstMode = true;
        
        [Header("Anti-Gridlock")]
        [Tooltip("Enable collision management to prevent gridlock")]
        public bool enableAntiGridlock = true;
        
        [Tooltip("Distance for gridlock detection")]
        [Range(0.5f, 3f)]
        public float gridlockDetectionRadius = 1.5f;
        
        [Tooltip("Minimum nearby agents before reducing collision")]
        [Range(3, 10)]
        public int gridlockAgentThreshold = 5;
        
        [Tooltip("Collision multiplier in crowded areas")]
        [Range(0f, 1f)]
        public float crowdedAreaCollisionMultiplier = 0.3f;
        
        [Tooltip("How often to check and update collision states")]
        [Range(0.1f, 1f)]
        public float collisionUpdateInterval = 0.5f;
        
        [Header("Large Crowd Optimizations")]
        [Tooltip("Enable optimizations for large crowds (5000+)")]
        public bool enableLargeCrowdOptimizations = false;
        
        [Tooltip("Agent count threshold for large crowd optimizations")]
        [Min(1000)]
        public int largeCrowdThreshold = 5000;
        
        [Header("Hybrid Workflows")]
        [Tooltip("Enable dynamic workflow switching based on distance")]
        public bool enableHybridWorkflows = false;
        
        [Tooltip("Distance for high-quality workflows")]
        [Min(0f)]
        public float hybridCloseDistance = 25f;
        
        [Tooltip("Distance for medium-quality workflows")]
        [Min(0f)]
        public float hybridMediumDistance = 50f;
        
        [Tooltip("Workflow for very close agents")]
        public HybridWorkflowType closeRangeWorkflow = HybridWorkflowType.BoneTracker;
        
        [Tooltip("Workflow for medium distance agents")]
        public HybridWorkflowType mediumRangeWorkflow = HybridWorkflowType.MecanimReader;
        
        [Tooltip("Workflow for distant agents")]
        public HybridWorkflowType distantRangeWorkflow = HybridWorkflowType.ComputeAnimator;
        
        [Tooltip("How often to check and update workflows")]
        [Min(0.1f)]
        public float hybridUpdateInterval = 1f;
        
        [Tooltip("Max agents that can use close range workflow")]
        [Min(0)]
        public int maxCloseRangeAgents = 20;
        
        [Tooltip("Max agents that can use medium range workflow")]
        [Min(0)]
        public int maxMediumRangeAgents = 50;
        
        [Tooltip("Prioritize center screen agents")]
        public bool enableCenterScreenPriority = true;
        
        [Tooltip("Center screen width percentage")]
        [Range(0.1f, 1.0f)]
        public float centerScreenWidth = 0.6f;
        
        [Tooltip("Center screen height percentage")]
        [Range(0.1f, 1.0f)]
        public float centerScreenHeight = 0.6f;
        
        [Tooltip("Priority boost for center screen agents")]
        [Min(1.0f)]
        public float centerScreenPriorityBoost = 2.0f;

        /// <summary>
        /// Applies the selected preset to all performance settings.
        /// Call this after changing the preset value.
        /// </summary>
        public void ApplyPreset()
        {
            ApplyPreset(preset);
        }

        /// <summary>
        /// Applies a specific preset to all performance settings.
        /// </summary>
        public void ApplyPreset(PerformancePreset targetPreset)
        {
            if (targetPreset == PerformancePreset.Custom)
                return; // Don't modify settings in custom mode

            switch (targetPreset)
            {
                case PerformancePreset.None:
                    ApplyNonePreset();
                    break;
                case PerformancePreset.Prioritized:
                    ApplyPrioritizedPreset();
                    break;
                case PerformancePreset.Moderate:
                    ApplyModeratePreset();
                    break;
                case PerformancePreset.Aggressive:
                    ApplyAggressivePreset();
                    break;
                case PerformancePreset.VeryAggressive:
                    ApplyVeryAggressivePreset();
                    break;
            }
        }

        private void ApplyNonePreset()
        {
            // Full quality - no optimizations
            prioritizeVisibleAgents = false;
            rotateVisibleAnimationUpdates = false;
            priorityMaxDistance = 100f;
            enablePriorityBasedVisibility = false;
            enableSpatialGrid = false;
            enableSpatialBatching = false;
            enableAdaptiveScaling = false;
            enableBurstMode = false;
            enableAntiGridlock = false;
            enableLargeCrowdOptimizations = false;
            enableHybridWorkflows = false;
            
            // Fast update intervals (process more per frame)
            frustumUpdateInterval = 0.016f;
            visibilityUpdateInterval = 0.016f;
            visibilityUpdateFraction = 1.0f;
            
            highPriorityUpdateInterval = 0.016f;
            highPriorityUpdateFraction = 1.0f;
            mediumPriorityUpdateInterval = 0.016f;
            mediumPriorityUpdateFraction = 1.0f;
            lowPriorityUpdateInterval = 0.016f;
            lowPriorityUpdateFraction = 1.0f;
        }

        private void ApplyPrioritizedPreset()
        {
            // Moderate optimizations with visible-only animation rotation
            prioritizeVisibleAgents = true;
            rotateVisibleAnimationUpdates = true;
            priorityMaxDistance = 50f;
            enablePriorityBasedVisibility = false;
            enableSpatialGrid = true;
            gridCellSize = 10f;
            enableSpatialBatching = false;
            enableAdaptiveScaling = false;
            enableBurstMode = false;
            enableAntiGridlock = true;
            gridlockDetectionRadius = 1.5f;
            gridlockAgentThreshold = 5;
            crowdedAreaCollisionMultiplier = 0.5f;
            collisionUpdateInterval = 0.5f;
            enableLargeCrowdOptimizations = false;
            enableHybridWorkflows = false;
            
            // Light update intervals
            frustumUpdateInterval = 0.033f;
            visibilityUpdateInterval = 0.1f;
            visibilityUpdateFraction = 0.5f;
            
            highPriorityUpdateInterval = 0.033f;
            highPriorityUpdateFraction = 0.8f;
            highPriorityDistance = 30f;
            mediumPriorityUpdateInterval = 0.066f;
            mediumPriorityUpdateFraction = 0.6f;
            mediumPriorityDistance = 60f;
            lowPriorityUpdateInterval = 0.1f;
            lowPriorityUpdateFraction = 0.4f;
        }

        private void ApplyModeratePreset()
        {
            // Light optimizations - good for 100-300 agents
            prioritizeVisibleAgents = true;
            rotateVisibleAnimationUpdates = false;
            priorityMaxDistance = 50f;
            enablePriorityBasedVisibility = false;
            enableSpatialGrid = true;
            gridCellSize = 10f;
            enableSpatialBatching = false;
            enableAdaptiveScaling = false;
            enableBurstMode = false;
            enableAntiGridlock = true;
            gridlockDetectionRadius = 1.5f;
            gridlockAgentThreshold = 5;
            crowdedAreaCollisionMultiplier = 0.5f;
            collisionUpdateInterval = 0.5f;
            enableLargeCrowdOptimizations = false;
            enableHybridWorkflows = false;
            
            // Moderate update intervals
            frustumUpdateInterval = 0.033f;
            visibilityUpdateInterval = 0.1f;
            visibilityUpdateFraction = 0.5f;
            
            highPriorityUpdateInterval = 0.033f;
            highPriorityUpdateFraction = 0.8f;
            highPriorityDistance = 30f;
            mediumPriorityUpdateInterval = 0.066f;
            mediumPriorityUpdateFraction = 0.6f;
            mediumPriorityDistance = 60f;
            lowPriorityUpdateInterval = 0.1f;
            lowPriorityUpdateFraction = 0.4f;
        }

        private void ApplyAggressivePreset()
        {
            // Stronger optimizations - good for 300-1000 agents
            prioritizeVisibleAgents = true;
            rotateVisibleAnimationUpdates = false;
            priorityMaxDistance = 40f;
            enablePriorityBasedVisibility = true;
            enablePriorityFailsafe = true;
            failsafeUpdateInterval = 0.2f;
            enableSpatialGrid = true;
            gridCellSize = 10f;
            enableSpatialBatching = true;
            maxAgentsPerCellPerFrame = 10;
            enableAdaptiveScaling = true;
            enableBurstMode = true;
            enableAntiGridlock = true;
            gridlockDetectionRadius = 1.5f;
            gridlockAgentThreshold = 4;
            crowdedAreaCollisionMultiplier = 0.3f;
            collisionUpdateInterval = 0.3f;
            enableLargeCrowdOptimizations = false;
            enableHybridWorkflows = false;
            
            // Aggressive update intervals
            frustumUpdateInterval = 0.05f;
            visibilityUpdateInterval = 0.15f;
            visibilityUpdateFraction = 0.3f;
            
            highPriorityUpdateInterval = 0.05f;
            highPriorityUpdateFraction = 0.7f;
            highPriorityDistance = 25f;
            mediumPriorityUpdateInterval = 0.1f;
            mediumPriorityUpdateFraction = 0.5f;
            mediumPriorityDistance = 50f;
            lowPriorityUpdateInterval = 0.15f;
            lowPriorityUpdateFraction = 0.3f;
        }

        private void ApplyVeryAggressivePreset()
        {
            // Maximum optimizations - for 1000+ agents
            prioritizeVisibleAgents = true;
            rotateVisibleAnimationUpdates = false;
            priorityMaxDistance = 30f;
            enablePriorityBasedVisibility = true;
            enablePriorityFailsafe = true;
            failsafeUpdateInterval = 0.3f;
            enableSpatialGrid = true;
            gridCellSize = 15f;
            enableSpatialBatching = true;
            maxAgentsPerCellPerFrame = 5;
            enableAdaptiveScaling = true;
            enableBurstMode = true;
            enableAntiGridlock = true;
            gridlockDetectionRadius = 2f;
            gridlockAgentThreshold = 3;
            crowdedAreaCollisionMultiplier = 0.2f;
            collisionUpdateInterval = 0.2f;
            enableLargeCrowdOptimizations = true;
            largeCrowdThreshold = 1000;
            enableHybridWorkflows = true;
            hybridCloseDistance = 20f;
            hybridMediumDistance = 40f;
            hybridUpdateInterval = 0.5f;
            maxCloseRangeAgents = 10;
            maxMediumRangeAgents = 30;
            
            // Very aggressive update intervals
            frustumUpdateInterval = 0.1f;
            visibilityUpdateInterval = 0.2f;
            visibilityUpdateFraction = 0.2f;
            
            highPriorityUpdateInterval = 0.066f;
            highPriorityUpdateFraction = 0.6f;
            highPriorityDistance = 20f;
            mediumPriorityUpdateInterval = 0.133f;
            mediumPriorityUpdateFraction = 0.4f;
            mediumPriorityDistance = 40f;
            lowPriorityUpdateInterval = 0.2f;
            lowPriorityUpdateFraction = 0.2f;
        }
    }
}
