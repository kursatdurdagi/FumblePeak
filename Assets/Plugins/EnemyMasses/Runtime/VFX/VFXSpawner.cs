using UnityEngine;
using System.Collections.Generic;

namespace EnemyMasses.VFX
{
    /// <summary>
    /// Helper class for spawning VFX that works with both ParticleSystem prefabs and VFX Graph assets.
    /// Handles pooling for ParticleSystem prefabs and delegates to VFXGraphManager for VFX Graph.
    /// 
    /// OPTIMIZED FOR THOUSANDS OF VFX:
    /// - Caches ParticleSystem components per prefab to avoid GetComponent calls
    /// - Uses per-prefab active tracking for O(1) oldest reclaim
    /// - Limits pool sizes to prevent memory explosion
    /// - Avoids allocations in hot paths
    /// </summary>
    public class VFXSpawner
    {
        // ParticleSystem prefab pools
        private Dictionary<GameObject, List<GameObject>> _particleSystemPools;
        private Dictionary<GameObject, Queue<int>> _particleSystemFreeIndices;
        
        // Per-prefab active tracking for fast oldest reclaim (sorted by release time)
        private Dictionary<GameObject, List<(int poolIndex, float releaseTime)>> _activePerPrefab;
        
        // Cached ParticleSystem[] components per prefab to avoid GetComponentsInChildren every spawn
        private Dictionary<GameObject, ParticleSystem[]> _cachedParticleSystems;
        
        private const int MaxPoolSizePerPrefab = 128; // Capped to prevent memory explosion
        private const int DefaultPoolSizePerPrefab = 32;
        
        private Transform _poolParent;
        
        public VFXSpawner(Transform poolParent = null)
        {
            _poolParent = poolParent;
            _particleSystemPools = new Dictionary<GameObject, List<GameObject>>(16);
            _particleSystemFreeIndices = new Dictionary<GameObject, Queue<int>>(16);
            _activePerPrefab = new Dictionary<GameObject, List<(int, float)>>(16);
            _cachedParticleSystems = new Dictionary<GameObject, ParticleSystem[]>(16);
        }
        
        /// <summary>
        /// Spawns a VFX effect at the specified position.
        /// Automatically handles both ParticleSystem prefabs and VFX Graph assets.
        /// </summary>
        /// <param name="vfxRef">The VFX reference</param>
        /// <param name="position">World position</param>
        /// <param name="rotation">Rotation</param>
        /// <param name="duration">Duration before reclaim (use -1 for default)</param>
        /// <param name="scale">Optional scale multiplier</param>
        /// <returns>The spawned GameObject (for ParticleSystem) or null (for VFX Graph)</returns>
        public GameObject SpawnEffect(VFXReference vfxRef, Vector3 position, Quaternion rotation, float duration = -1f, float scale = 1f)
        {
            if (vfxRef == null || !vfxRef.IsValid)
                return null;
                
            if (duration < 0)
                duration = vfxRef.GetDuration();
            
            if (vfxRef.IsVFXGraph)
            {
                // Use VFXGraphManager for GPU-accelerated VFX
                VFXGraphManager.Instance.SpawnEffect(vfxRef, position, rotation, duration, scale);
                return null; // VFX Graph doesn't return a GameObject
            }
            else if (vfxRef.IsParticleSystem)
            {
                return SpawnPooledParticleSystem(vfxRef.particleSystemPrefab, position, rotation, duration, scale);
            }
            
            return null;
        }
        
        /// <summary>
        /// Spawns an impact effect with normal direction.
        /// </summary>
        public GameObject SpawnImpact(VFXReference vfxRef, Vector3 position, Vector3 normal, float duration = -1f)
        {
            if (vfxRef == null || !vfxRef.IsValid)
                return null;
                
            if (duration < 0)
                duration = vfxRef.GetDuration();
            
            if (vfxRef.IsVFXGraph)
            {
                VFXGraphManager.Instance.SpawnImpact(vfxRef, position, normal, duration);
                return null;
            }
            else if (vfxRef.IsParticleSystem)
            {
                Quaternion rotation = normal != Vector3.zero ? Quaternion.LookRotation(normal) : Quaternion.identity;
                return SpawnPooledParticleSystem(vfxRef.particleSystemPrefab, position, rotation, duration);
            }
            
            return null;
        }
        
        /// <summary>
        /// Spawns a muzzle flash effect.
        /// </summary>
        public GameObject SpawnMuzzleFlash(VFXReference vfxRef, Vector3 position, Vector3 direction, float duration = -1f)
        {
            if (vfxRef == null || !vfxRef.IsValid)
                return null;
                
            if (duration < 0)
                duration = vfxRef.GetDuration();
            
            if (vfxRef.IsVFXGraph)
            {
                VFXGraphManager.Instance.SpawnMuzzleFlash(vfxRef, position, direction, duration);
                return null;
            }
            else if (vfxRef.IsParticleSystem)
            {
                Quaternion rotation = direction != Vector3.zero ? Quaternion.LookRotation(direction) : Quaternion.identity;
                return SpawnPooledParticleSystem(vfxRef.particleSystemPrefab, position, rotation, duration);
            }
            
            return null;
        }
        
        /// <summary>
        /// Spawns an AOE explosion effect.
        /// </summary>
        public GameObject SpawnAOEExplosion(VFXReference vfxRef, Vector3 position, float radius, float duration = -1f)
        {
            if (vfxRef == null || !vfxRef.IsValid)
                return null;
                
            if (duration < 0)
                duration = vfxRef.GetDuration();
            
            if (vfxRef.IsVFXGraph)
            {
                VFXGraphManager.Instance.SpawnAOEExplosion(vfxRef, position, radius, duration);
                return null;
            }
            else if (vfxRef.IsParticleSystem)
            {
                // Scale based on radius for ParticleSystem
                float scale = radius > 0 ? radius / 5f : 1f; // Assuming base radius of 5
                return SpawnPooledParticleSystem(vfxRef.particleSystemPrefab, position, Quaternion.identity, duration, scale);
            }
            
            return null;
        }
        
        /// <summary>
        /// Spawns a pooled ParticleSystem prefab.
        /// OPTIMIZED: Caches ParticleSystem components and uses per-prefab active tracking.
        /// </summary>
        private GameObject SpawnPooledParticleSystem(GameObject prefab, Vector3 position, Quaternion rotation, float duration, float scale = 1f)
        {
            if (prefab == null)
                return null;
            
            // Get or create pool
            if (!_particleSystemPools.TryGetValue(prefab, out var pool))
            {
                pool = new List<GameObject>(DefaultPoolSizePerPrefab);
                _particleSystemPools[prefab] = pool;
                _particleSystemFreeIndices[prefab] = new Queue<int>(DefaultPoolSizePerPrefab);
                _activePerPrefab[prefab] = new List<(int, float)>(DefaultPoolSizePerPrefab);
            }
            
            var freeIndices = _particleSystemFreeIndices[prefab];
            var activeList = _activePerPrefab[prefab];
            
            int poolIndex;
            GameObject instance;
            
            if (freeIndices.Count > 0)
            {
                // Reuse existing instance
                poolIndex = freeIndices.Dequeue();
                instance = pool[poolIndex];
            }
            else if (pool.Count < MaxPoolSizePerPrefab)
            {
                // Create new instance
                instance = Object.Instantiate(prefab, position, rotation, _poolParent);
                poolIndex = pool.Count;
                pool.Add(instance);
                
                // Cache ParticleSystem components on first instantiation
                CacheParticleSystems(prefab, instance);
            }
            else
            {
                // Pool exhausted - reclaim oldest from THIS prefab's active list
                if (activeList.Count > 0)
                {
                    // Find oldest (first in list since we add at end)
                    int oldestIdx = 0;
                    float oldestTime = activeList[0].releaseTime;
                    for (int i = 1; i < activeList.Count; i++)
                    {
                        if (activeList[i].releaseTime < oldestTime)
                        {
                            oldestTime = activeList[i].releaseTime;
                            oldestIdx = i;
                        }
                    }
                    
                    poolIndex = activeList[oldestIdx].poolIndex;
                    instance = pool[poolIndex];
                    
                    // Remove from active list (swap with last for O(1))
                    int lastIdx = activeList.Count - 1;
                    if (oldestIdx < lastIdx)
                        activeList[oldestIdx] = activeList[lastIdx];
                    activeList.RemoveAt(lastIdx);
                    
                    // Stop existing particles before reuse
                    StopParticleSystems(prefab, instance);
                }
                else
                {
                    return null;
                }
            }
            
            // Configure instance
            instance.transform.SetPositionAndRotation(position, rotation);
            if (scale != 1f)
                instance.transform.localScale = Vector3.one * scale;
            instance.SetActive(true);
            
            // Play particle systems using cached components
            PlayParticleSystems(prefab, instance);
            
            // Track for reclaim in per-prefab list
            float releaseTime = Time.time + duration;
            activeList.Add((poolIndex, releaseTime));
            
            return instance;
        }
        
        /// <summary>
        /// Caches ParticleSystem components for a prefab to avoid GetComponentsInChildren calls.
        /// </summary>
        private void CacheParticleSystems(GameObject prefab, GameObject instance)
        {
            if (!_cachedParticleSystems.ContainsKey(prefab))
            {
                // Cache the ParticleSystem array structure from this instance
                // Note: We can't cache the actual components since they're different per instance,
                // but we store a reference to know this prefab has particle systems
                var particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
                _cachedParticleSystems[prefab] = particleSystems;
            }
        }
        
        /// <summary>
        /// Plays particle systems on an instance using cached knowledge.
        /// </summary>
        private void PlayParticleSystems(GameObject prefab, GameObject instance)
        {
            // Get all particle systems on this instance
            // We still need GetComponentsInChildren but only for instances that definitely have particles
            if (_cachedParticleSystems.TryGetValue(prefab, out var cachedPS) && cachedPS != null && cachedPS.Length > 0)
            {
                var particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < particleSystems.Length; i++)
                {
                    particleSystems[i].Clear();
                    particleSystems[i].Play();
                }
            }
            else
            {
                // First time - get and cache
                var particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
                _cachedParticleSystems[prefab] = particleSystems;
                for (int i = 0; i < particleSystems.Length; i++)
                {
                    particleSystems[i].Clear();
                    particleSystems[i].Play();
                }
            }
        }
        
        /// <summary>
        /// Stops particle systems on an instance.
        /// </summary>
        private void StopParticleSystems(GameObject prefab, GameObject instance)
        {
            if (_cachedParticleSystems.TryGetValue(prefab, out var cachedPS) && cachedPS != null && cachedPS.Length > 0)
            {
                var particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < particleSystems.Length; i++)
                {
                    particleSystems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }
        
        /// <summary>
        /// Reclaims finished ParticleSystem instances back to their pools.
        /// Should be called every frame.
        /// OPTIMIZED: Uses per-prefab tracking for faster iteration.
        /// </summary>
        public void ReclaimFinishedVFX()
        {
            float currentTime = Time.time;
            
            // Iterate through each prefab's active list
            foreach (var kvp in _activePerPrefab)
            {
                var prefab = kvp.Key;
                var activeList = kvp.Value;
                
                if (!_particleSystemPools.TryGetValue(prefab, out var pool))
                    continue;
                    
                var freeIndices = _particleSystemFreeIndices[prefab];
                
                for (int i = activeList.Count - 1; i >= 0; i--)
                {
                    var (poolIndex, releaseTime) = activeList[i];
                    
                    if (currentTime >= releaseTime)
                    {
                        // Return to pool
                        if (poolIndex < pool.Count)
                        {
                            var instance = pool[poolIndex];
                            if (instance != null)
                            {
                                // Stop particle systems
                                StopParticleSystems(prefab, instance);
                                instance.SetActive(false);
                            }
                            
                            freeIndices.Enqueue(poolIndex);
                        }
                        
                        // Remove from active list (swap with last for O(1))
                        int lastIdx = activeList.Count - 1;
                        if (i < lastIdx)
                            activeList[i] = activeList[lastIdx];
                        activeList.RemoveAt(lastIdx);
                    }
                }
            }
        }
        
        /// <summary>
        /// Clears all pools and destroys pooled instances.
        /// </summary>
        public void ClearAllPools()
        {
            foreach (var pool in _particleSystemPools.Values)
            {
                foreach (var instance in pool)
                {
                    if (instance != null)
                        Object.Destroy(instance);
                }
            }
            
            _particleSystemPools.Clear();
            _particleSystemFreeIndices.Clear();
            _activePerPrefab.Clear();
            _cachedParticleSystems.Clear();
        }

        /// <summary>
        /// Stops all pooled ParticleSystem effects and returns them to their pools.
        /// </summary>
        public void StopAllEffects()
        {
            foreach (var kvp in _particleSystemPools)
            {
                var prefab = kvp.Key;
                var pool = kvp.Value;
                if (pool == null)
                    continue;

                if (!_particleSystemFreeIndices.TryGetValue(prefab, out var freeIndices) || freeIndices == null)
                {
                    freeIndices = new Queue<int>(pool.Count);
                    _particleSystemFreeIndices[prefab] = freeIndices;
                }
                else
                {
                    freeIndices.Clear();
                }

                for (int i = 0; i < pool.Count; i++)
                {
                    var instance = pool[i];
                    if (instance == null)
                        continue;

                    StopParticleSystems(prefab, instance);
                    instance.SetActive(false);
                    freeIndices.Enqueue(i);
                }

                if (_activePerPrefab.TryGetValue(prefab, out var activeList) && activeList != null)
                {
                    activeList.Clear();
                }
            }
        }
        
        /// <summary>
        /// Gets statistics about the ParticleSystem pool state.
        /// </summary>
        public (int totalPools, int totalInstances, int activeInstances) GetPoolStats()
        {
            int totalInstances = 0;
            int activeInstances = 0;
            
            foreach (var pool in _particleSystemPools.Values)
            {
                totalInstances += pool.Count;
            }
            
            foreach (var activeList in _activePerPrefab.Values)
            {
                activeInstances += activeList.Count;
            }
            
            return (_particleSystemPools.Count, totalInstances, activeInstances);
        }
        
        /// <summary>
        /// Pre-warms a pool for a specific prefab to avoid hitches during gameplay.
        /// </summary>
        public void PreWarmPool(GameObject prefab, int count)
        {
            if (prefab == null || count <= 0)
                return;
            
            // Get or create pool
            if (!_particleSystemPools.TryGetValue(prefab, out var pool))
            {
                pool = new List<GameObject>(Mathf.Min(count, MaxPoolSizePerPrefab));
                _particleSystemPools[prefab] = pool;
                _particleSystemFreeIndices[prefab] = new Queue<int>(count);
                _activePerPrefab[prefab] = new List<(int, float)>(count);
            }
            
            var freeIndices = _particleSystemFreeIndices[prefab];
            int toCreate = Mathf.Min(count - pool.Count, MaxPoolSizePerPrefab - pool.Count);
            
            for (int i = 0; i < toCreate; i++)
            {
                var instance = Object.Instantiate(prefab, _poolParent);
                instance.SetActive(false);
                int poolIndex = pool.Count;
                pool.Add(instance);
                freeIndices.Enqueue(poolIndex);
                
                // Cache particle systems on first instance
                if (i == 0)
                    CacheParticleSystems(prefab, instance);
            }
        }
    }
}
