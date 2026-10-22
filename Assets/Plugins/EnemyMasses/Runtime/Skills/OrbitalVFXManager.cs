using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using UnityEngine;
using GPUInstancerPro;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Manages all orbital VFX using GPU Instancer Pro for maximum performance.
    /// Handles thousands of orbital objects with minimal CPU overhead.
    /// 
    /// Usage:
    /// 1. Call AddOrbitalEffect() when a skill activates
    /// 2. Call RemoveOrbitalEffect() when a skill ends
    /// 3. Manager handles all GPU instancing automatically
    /// </summary>
    public class OrbitalVFXManager : MonoBehaviour
    {
        #region Singleton
        private static OrbitalVFXManager _instance;
        public static OrbitalVFXManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[EnemyMasses] OrbitalVFXManager");
                    _instance = go.AddComponent<OrbitalVFXManager>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        
        public static bool IsActive => _instance != null;
        #endregion
        
        #region Data Structures
        
        /// <summary>
        /// Tracks an active orbital effect on an agent.
        /// </summary>
        private struct ActiveOrbitalEffect
        {
            public int agentIndex;
            public Transform agentTransform;
            public OrbitalVFXConfig config;
            public int skillId;
            public string effectKey;
            public float startTime;
            public float duration; // 0 = infinite
            public int rendererStartIndex; // First index in the orbital matrices array
        }
        
        /// <summary>
        /// Tracks a registered orbital prefab renderer.
        /// </summary>
        private class OrbitalRendererData
        {
            public GameObject prefab;
            public int rendererKey;
            public int currentInstanceCount;
            public int maxInstanceCount;
            public NativeArray<Matrix4x4> matrices;
            public List<int> activeEffectIds; // Which effects are using this renderer
            
            public void Dispose()
            {
                if (matrices.IsCreated)
                    matrices.Dispose();
                if (rendererKey != 0)
                    GPUICoreAPI.DisposeRenderer(rendererKey);
            }
        }
        
        #endregion
        
        #region Fields
        
        // Configuration
        private const int INITIAL_BUFFER_SIZE = 256;
        private const int BUFFER_SIZE_INCREMENT = 128;
        
        // Registered renderers by prefab instance ID
        private Dictionary<int, OrbitalRendererData> _renderers = new();
        
        // Active orbital effects by unique ID
        private Dictionary<int, ActiveOrbitalEffect> _activeEffects = new();
        private int _nextEffectId = 1;
        
        // Cached camera for LOD
        private Camera _mainCamera;
        private Vector3 _cameraPosition;
        
        // Job system
        private JobHandle _updateJobHandle;
        private bool _jobScheduled;
        
        #endregion
        
        #region MonoBehaviour
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }
        
        private void OnDestroy()
        {
            DisposeAll();
            if (_instance == this)
                _instance = null;
        }
        
        private void LateUpdate()
        {
            if (_activeEffects.Count == 0)
                return;
            
            // Cache camera position
            if (_mainCamera == null)
                _mainCamera = Camera.main;
            if (_mainCamera != null)
                _cameraPosition = _mainCamera.transform.position;
            
            // Complete previous frame's job
            if (_jobScheduled)
            {
                _updateJobHandle.Complete();
                _jobScheduled = false;
            }
            
            // Update all orbital transforms
            UpdateOrbitalTransforms();
            
            // Upload updated matrices to GPU
            UploadMatricesToGPU();
        }
        
        #endregion
        
        #region Public API
        
        /// <summary>
        /// Add an orbital VFX effect to an agent.
        /// </summary>
        /// <param name="agentIndex">The agent's index in the crowd system.</param>
        /// <param name="agentTransform">The agent's transform to orbit around.</param>
        /// <param name="config">The orbital VFX configuration.</param>
        /// <param name="duration">Duration in seconds. 0 = infinite until manually removed.</param>
        /// <param name="skillId">Skill instance ID for grouping related effects.</param>
        /// <param name="effectKey">Optional key to identify a specific orbital effect.</param>
        /// <returns>Effect ID used to remove the effect later.</returns>
        public int AddOrbitalEffect(
            int agentIndex,
            Transform agentTransform,
            OrbitalVFXConfig config,
            float duration,
            int skillId,
            string effectKey)
        {
            if (!config.IsValid || agentTransform == null)
                return -1;
            
            // Get or create renderer for this prefab
            int prefabId = config.orbitalPrefab.GetInstanceID();
            if (!_renderers.TryGetValue(prefabId, out var rendererData))
            {
                rendererData = CreateRenderer(config.orbitalPrefab);
                if (rendererData == null)
                    return -1;
                _renderers[prefabId] = rendererData;
            }
            
            // Check max active agents limit
            if (config.maxActiveAgents > 0 && rendererData.activeEffectIds.Count >= config.maxActiveAgents)
            {
                // Find furthest effect and remove it
                RemoveFurthestEffect(rendererData);
            }
            
            // Ensure buffer capacity
            int requiredInstances = rendererData.currentInstanceCount + config.orbitCount;
            if (requiredInstances > rendererData.maxInstanceCount)
            {
                ExpandBuffer(rendererData, requiredInstances);
            }
            
            // Create the effect
            int effectId = _nextEffectId++;
            var effect = new ActiveOrbitalEffect
            {
                agentIndex = agentIndex,
                agentTransform = agentTransform,
                config = config,
                skillId = skillId,
                effectKey = string.IsNullOrEmpty(effectKey) ? string.Empty : effectKey,
                startTime = Time.time,
                duration = duration,
                rendererStartIndex = rendererData.currentInstanceCount
            };
            
            _activeEffects[effectId] = effect;
            rendererData.activeEffectIds.Add(effectId);
            rendererData.currentInstanceCount += config.orbitCount;
            
            // Update instance count on GPU
            GPUICoreAPI.SetInstanceCount(rendererData.rendererKey, rendererData.currentInstanceCount);
            
            return effectId;
        }
        
        /// <summary>
        /// Remove an orbital effect by its ID.
        /// </summary>
        public void RemoveOrbitalEffect(int effectId)
        {
            if (!_activeEffects.TryGetValue(effectId, out var effect))
                return;
            
            int prefabId = effect.config.orbitalPrefab.GetInstanceID();
            if (_renderers.TryGetValue(prefabId, out var rendererData))
            {
                // Remove from active list
                rendererData.activeEffectIds.Remove(effectId);
                
                // Compact the buffer (move last effect's instances to fill the gap)
                CompactBuffer(rendererData, effect);
            }
            
            _activeEffects.Remove(effectId);
        }

        /// <summary>
        /// Remove orbital effects for a specific agent filtered by skill and key.
        /// </summary>
        public void RemoveOrbitalEffects(int agentIndex, int skillId, string effectKey)
        {
            bool filterSkill = skillId > 0;
            bool filterKey = !string.IsNullOrEmpty(effectKey);
            if (_activeEffects.Count == 0)
                return;
            
            var toRemove = new List<int>();
            foreach (var kvp in _activeEffects)
            {
                var effect = kvp.Value;
                if (effect.agentIndex != agentIndex)
                    continue;
                if (filterSkill && effect.skillId != skillId)
                    continue;
                if (filterKey && !string.Equals(effect.effectKey, effectKey, StringComparison.Ordinal))
                    continue;
                
                toRemove.Add(kvp.Key);
            }
            
            foreach (int effectId in toRemove)
                RemoveOrbitalEffect(effectId);
        }
        
        /// <summary>
        /// Remove all orbital effects from a specific agent.
        /// </summary>
        public void RemoveAllEffectsFromAgent(int agentIndex)
        {
            var toRemove = new List<int>();
            foreach (var kvp in _activeEffects)
            {
                if (kvp.Value.agentIndex == agentIndex)
                    toRemove.Add(kvp.Key);
            }
            
            foreach (int effectId in toRemove)
                RemoveOrbitalEffect(effectId);
        }
        
        /// <summary>
        /// Remove all orbital effects (e.g., on scene change).
        /// </summary>
        public void RemoveAllEffects()
        {
            _activeEffects.Clear();
            foreach (var renderer in _renderers.Values)
            {
                renderer.activeEffectIds.Clear();
                renderer.currentInstanceCount = 0;
                GPUICoreAPI.SetInstanceCount(renderer.rendererKey, 0);
            }
        }
        
        #endregion
        
        #region Internal Methods
        
        private OrbitalRendererData CreateRenderer(GameObject prefab)
        {
            if (!GPUIRenderingSystem.IsActive)
            {
                Debug.LogWarning("[OrbitalVFXManager] GPU Instancer Pro is not active. Cannot create orbital renderer.");
                return null;
            }
            
            if (!GPUICoreAPI.RegisterRenderer(this, prefab, out int rendererKey))
            {
                Debug.LogError($"[OrbitalVFXManager] Failed to register renderer for {prefab.name}");
                return null;
            }
            
            var data = new OrbitalRendererData
            {
                prefab = prefab,
                rendererKey = rendererKey,
                currentInstanceCount = 0,
                maxInstanceCount = INITIAL_BUFFER_SIZE,
                matrices = new NativeArray<Matrix4x4>(INITIAL_BUFFER_SIZE, Allocator.Persistent),
                activeEffectIds = new List<int>()
            };
            
            // Initialize with identity matrices
            for (int i = 0; i < INITIAL_BUFFER_SIZE; i++)
                data.matrices[i] = Matrix4x4.identity;
            
            GPUICoreAPI.SetTransformBufferData(rendererKey, data.matrices);
            GPUICoreAPI.SetInstanceCount(rendererKey, 0);
            
            return data;
        }
        
        private void ExpandBuffer(OrbitalRendererData data, int requiredSize)
        {
            int newSize = ((requiredSize / BUFFER_SIZE_INCREMENT) + 1) * BUFFER_SIZE_INCREMENT;
            
            var newMatrices = new NativeArray<Matrix4x4>(newSize, Allocator.Persistent);
            
            // Copy existing data
            if (data.matrices.IsCreated)
            {
                NativeArray<Matrix4x4>.Copy(data.matrices, newMatrices, data.currentInstanceCount);
                data.matrices.Dispose();
            }
            
            // Initialize new entries
            for (int i = data.maxInstanceCount; i < newSize; i++)
                newMatrices[i] = Matrix4x4.identity;
            
            data.matrices = newMatrices;
            data.maxInstanceCount = newSize;
            
            // Re-upload full buffer
            GPUICoreAPI.SetTransformBufferData(data.rendererKey, data.matrices);
        }
        
        private void CompactBuffer(OrbitalRendererData rendererData, ActiveOrbitalEffect removedEffect)
        {
            int removeStart = removedEffect.rendererStartIndex;
            int removeCount = removedEffect.config.orbitCount;
            int removeEnd = removeStart + removeCount;
            
            // Move all instances after the removed effect down
            int instancesAfter = rendererData.currentInstanceCount - removeEnd;
            if (instancesAfter > 0)
            {
                for (int i = 0; i < instancesAfter; i++)
                {
                    rendererData.matrices[removeStart + i] = rendererData.matrices[removeEnd + i];
                }
                
                // Update indices for all effects that come after
                foreach (int effectId in rendererData.activeEffectIds)
                {
                    if (_activeEffects.TryGetValue(effectId, out var effect))
                    {
                        if (effect.rendererStartIndex >= removeEnd)
                        {
                            effect.rendererStartIndex -= removeCount;
                            _activeEffects[effectId] = effect;
                        }
                    }
                }
            }
            
            rendererData.currentInstanceCount -= removeCount;
            GPUICoreAPI.SetInstanceCount(rendererData.rendererKey, rendererData.currentInstanceCount);
        }
        
        private void RemoveFurthestEffect(OrbitalRendererData rendererData)
        {
            float maxDistSq = float.MinValue;
            int furthestEffectId = -1;
            
            foreach (int effectId in rendererData.activeEffectIds)
            {
                if (_activeEffects.TryGetValue(effectId, out var effect) && effect.agentTransform != null)
                {
                    float distSq = (effect.agentTransform.position - _cameraPosition).sqrMagnitude;
                    if (distSq > maxDistSq)
                    {
                        maxDistSq = distSq;
                        furthestEffectId = effectId;
                    }
                }
            }
            
            if (furthestEffectId >= 0)
                RemoveOrbitalEffect(furthestEffectId);
        }
        
        private void UpdateOrbitalTransforms()
        {
            float time = Time.time;
            var expiredEffects = new List<int>();
            
            foreach (var kvp in _activeEffects)
            {
                var effect = kvp.Value;
                
                // Check expiration
                if (effect.duration > 0 && time - effect.startTime >= effect.duration)
                {
                    expiredEffects.Add(kvp.Key);
                    continue;
                }
                
                // Check if agent still exists
                if (effect.agentTransform == null)
                {
                    expiredEffects.Add(kvp.Key);
                    continue;
                }
                
                // LOD check
                float distSq = (effect.agentTransform.position - _cameraPosition).sqrMagnitude;
                float lodDistSq = effect.config.lodDistance * effect.config.lodDistance;
                bool isVisible = distSq <= lodDistSq;
                
                // Get renderer data
                int prefabId = effect.config.orbitalPrefab.GetInstanceID();
                if (!_renderers.TryGetValue(prefabId, out var rendererData))
                    continue;
                
                // Calculate orbital positions
                Vector3 agentPos = effect.agentTransform.position;
                Vector3 heightOffset = Vector3.up * effect.config.heightOffset;
                Vector3 center = agentPos + heightOffset;
                
                float elapsedTime = time - effect.startTime;
                float baseAngle = elapsedTime * effect.config.orbitSpeed;
                
                // Calculate bobbing
                float bob = 0f;
                if (effect.config.enableBobbing)
                {
                    bob = Mathf.Sin(elapsedTime * effect.config.bobSpeed * Mathf.PI * 2f) * effect.config.bobHeight;
                }
                
                Vector3 orbitAxis = effect.config.GetOrbitAxisVector();
                
                for (int i = 0; i < effect.config.orbitCount; i++)
                {
                    int matrixIndex = effect.rendererStartIndex + i;
                    if (matrixIndex >= rendererData.matrices.Length)
                        continue;
                    
                    if (!isVisible)
                    {
                        // Hide by setting scale to 0
                        rendererData.matrices[matrixIndex] = Matrix4x4.TRS(
                            center,
                            Quaternion.identity,
                            Vector3.zero
                        );
                        continue;
                    }
                    
                    float angleOffset = effect.config.GetAngleOffset(i);
                    float dirMult = effect.config.GetDirectionMultiplier(i);
                    float angle = (baseAngle * dirMult) + angleOffset;
                    
                    // Calculate orbital position
                    Quaternion orbitRotation = Quaternion.AngleAxis(angle, orbitAxis);
                    Vector3 orbitalOffset = orbitRotation * (Vector3.forward * effect.config.orbitRadius);
                    Vector3 position = center + orbitalOffset + Vector3.up * bob;
                    
                    // Calculate rotation
                    Quaternion rotation;
                    if (effect.config.faceMovementDirection)
                    {
                        // Face tangent to orbit
                        Vector3 tangent = Vector3.Cross(orbitAxis, orbitalOffset).normalized;
                        if (tangent.sqrMagnitude > 0.001f)
                            rotation = Quaternion.LookRotation(tangent * dirMult, orbitAxis);
                        else
                            rotation = Quaternion.identity;
                    }
                    else if (effect.config.faceOutward)
                    {
                        // Face away from center
                        rotation = Quaternion.LookRotation(orbitalOffset.normalized, Vector3.up);
                    }
                    else
                    {
                        rotation = Quaternion.identity;
                    }
                    
                    rendererData.matrices[matrixIndex] = Matrix4x4.TRS(
                        position,
                        rotation,
                        Vector3.one * effect.config.orbitalScale
                    );
                }
            }
            
            // Remove expired effects
            foreach (int effectId in expiredEffects)
                RemoveOrbitalEffect(effectId);
        }
        
        private void UploadMatricesToGPU()
        {
            foreach (var renderer in _renderers.Values)
            {
                if (renderer.currentInstanceCount > 0)
                {
                    GPUICoreAPI.SetTransformBufferData(
                        renderer.rendererKey,
                        renderer.matrices,
                        0, 0,
                        renderer.currentInstanceCount,
                        false // Don't overwrite previous frame for motion vectors
                    );
                }
            }
        }
        
        private void DisposeAll()
        {
            if (_jobScheduled)
            {
                _updateJobHandle.Complete();
                _jobScheduled = false;
            }
            
            foreach (var renderer in _renderers.Values)
                renderer.Dispose();
            
            _renderers.Clear();
            _activeEffects.Clear();
        }
        
        #endregion
    }
}
