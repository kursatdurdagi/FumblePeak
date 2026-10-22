using System;
using UnityEngine;
using GPUInstancerPro;
using GPUInstancerPro.CrowdAnimations;
using GPUInstancerPro.PrefabModule;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Provides safe wrappers around GPU Instancer Pro APIs to prevent exceptions
    /// during buffer compaction, shutdown, or when dealing with stale references.
    /// </summary>
    public static class EnemyMassesGPUIHelper
    {
        /// <summary>
        /// Checks if GPU Instancer systems are active and ready for operations.
        /// </summary>
        public static bool IsGPUIActive()
        {
            return GPUIRenderingSystem.IsActive && GPUICrowdSkinningSystem.IsActive;
        }
        
        /// <summary>
        /// Checks if a prefab instance is valid for GPU operations.
        /// Works with both GPUIPrefab and GPUIPrefabBase.
        /// </summary>
        public static bool IsPrefabValid(GPUIPrefabBase prefab)
        {
            if (!IsGPUIActive())
                return false;
            if (prefab == null || !prefab.IsInstanced)
                return false;
            if (prefab.renderKey == 0 || prefab.bufferIndex < 0)
                return false;
            
            // Verify buffer bounds
            if (!GPUIRenderingSystem.TryGetRenderSource(prefab.renderKey, out var renderSource))
                return false;
            if (prefab.bufferIndex >= renderSource.bufferSize)
                return false;
            
            return true;
        }
        
        /// <summary>
        /// Checks if a crowd instance is ready for animation operations.
        /// </summary>
        public static bool IsCrowdInstanceValid(GPUICrowdInstance crowdInstance)
        {
            if (crowdInstance == null || !crowdInstance.IsInstanced)
                return false;
            var prefab = crowdInstance.PrefabComponent;
            if (!IsPrefabValid(prefab))
                return false;
            
            // Verify crowd-specific data exists
            if (!GPUICrowdSkinningSystem.TryGetCrowdRenderSource(prefab.renderKey, out _))
                return false;
            
            return crowdInstance.AnimatorWorkflow != null;
        }
        
        /// <summary>
        /// Safely starts an animation with exception handling.
        /// Returns true if the animation was started successfully.
        /// IMPORTANT: Only use this for ComputeAnimator workflow. BoneTracker and MecanimReader
        /// do not support GPU animation calls - they require Mecanim Animator instead.
        /// </summary>
        public static bool SafeStartAnimation(
            GPUICrowdInstance crowdInstance, 
            AnimationClip clip,
            float normalizedClipTime = -1f,
            float speed = 1f,
            float transitionTime = 0.2f,
            bool? isLoopingOverride = null,
            bool isSyncTime = true)
        {
            if (!IsCrowdInstanceValid(crowdInstance) || clip == null)
                return false;
            
            // CRITICAL: Check workflow type - only ComputeAnimator supports GPU animation calls
            var workflow = crowdInstance.AnimatorWorkflow;
            if (workflow == null)
            {
                Debug.LogWarning($"[EnemyMasses] SafeStartAnimation blocked: AnimatorWorkflow is null");
                return false;
            }
            
            // Block BoneTracker and MecanimReader - they don't have internal animator systems
            if (workflow.GetID() == GPUIAWBoneTracker.WORKFLOW_ID || 
                workflow.GetID() == GPUIAWMecanimReader.WORKFLOW_ID)
            {
                Debug.LogWarning($"[EnemyMasses] SafeStartAnimation blocked: Workflow {workflow.GetID()} does not support GPU animations. Use Mecanim Animator instead.");
                return false;
            }
            
            try
            {
                crowdInstance.StartAnimation(clip, normalizedClipTime, speed, transitionTime, isLoopingOverride, isSyncTime);
                return true;
            }
            catch (Exception e)
            {
                // Log at debug level to avoid spam
                Debug.LogWarning($"[EnemyMasses] SafeStartAnimation failed: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Safely starts a blend animation with exception handling.
        /// Returns true if the blend was started successfully.
        /// IMPORTANT: Only use this for ComputeAnimator workflow. BoneTracker and MecanimReader
        /// do not support GPU animation calls - they require Mecanim Animator instead.
        /// </summary>
        public static bool SafeStartBlend(
            GPUICrowdInstance crowdInstance,
            Vector4 weights,
            AnimationClip clip1,
            AnimationClip clip2,
            AnimationClip clip3,
            AnimationClip clip4,
            Vector4 normalizedClipTimes,
            Vector4? animationSpeeds,
            float transitionTime,
            bool? isLoopingOverride,
            bool isSyncTime)
        {
            if (!IsCrowdInstanceValid(crowdInstance) || (clip1 == null && clip2 == null))
                return false;
            
            // CRITICAL: Check workflow type - only ComputeAnimator supports GPU animation calls
            var workflow = crowdInstance.AnimatorWorkflow;
            if (workflow == null)
            {
                Debug.LogWarning($"[EnemyMasses] SafeStartBlend blocked: AnimatorWorkflow is null");
                return false;
            }
            
            // Block BoneTracker and MecanimReader - they don't have internal animator systems
            if (workflow.GetID() == GPUIAWBoneTracker.WORKFLOW_ID || 
                workflow.GetID() == GPUIAWMecanimReader.WORKFLOW_ID)
            {
                Debug.LogWarning($"[EnemyMasses] SafeStartBlend blocked: Workflow {workflow.GetID()} does not support GPU animations. Use Mecanim Animator instead.");
                return false;
            }
            
            try
            {
                GPUICrowdAPI.StartBlend(crowdInstance, weights, clip1, clip2, clip3, clip4, normalizedClipTimes, animationSpeeds, transitionTime, isLoopingOverride, isSyncTime);
                return true;
            }
            catch (Exception e)
            {
                // Log at debug level to avoid spam
                Debug.LogWarning($"[EnemyMasses] SafeStartBlend failed: {e.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Safely removes a prefab instance from GPUI.
        /// Returns true if the removal was successful or if the instance was already removed.
        /// Works with both GPUIPrefab and GPUIPrefabBase.
        /// </summary>
        public static bool SafeRemovePrefabInstance(GPUIPrefabBase prefab)
        {
            if (prefab == null)
                return true;
            
            // Already removed
            if (!prefab.IsInstanced)
                return true;
            
            // GPUI shutting down - nothing to do
            if (!GPUIRenderingSystem.IsActive)
                return true;
            
            // Invalid buffer index - already corrupt or removed
            if (prefab.bufferIndex < 0)
                return true;
            
            // Cast to GPUIPrefab if possible for the API call
            if (prefab is GPUIPrefab gpuiPrefab)
            {
                try
                {
                    GPUIPrefabAPI.RemovePrefabInstance(gpuiPrefab);
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[EnemyMasses] SafeRemovePrefabInstance failed: {e.Message}");
                    return false;
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// Safely adds a prefab instance to GPUI.
        /// Returns true if the addition was successful.
        /// </summary>
        public static bool SafeAddPrefabInstance(GPUIPrefab prefab)
        {
            if (prefab == null)
                return false;
            
            // Already instanced
            if (prefab.IsInstanced)
                return true;
            
            // GPUI not ready
            if (!GPUIRenderingSystem.IsActive)
                return false;
            
            try
            {
                GPUIPrefabAPI.AddPrefabInstance(prefab);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EnemyMasses] SafeAddPrefabInstance failed: {e.Message}");
                return false;
            }
        }
    }
}
