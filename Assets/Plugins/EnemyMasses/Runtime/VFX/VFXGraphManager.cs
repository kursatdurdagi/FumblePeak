using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace EnemyMasses.VFX
{
    /// <summary>
    /// Manages VFX Graph instances for efficient GPU-accelerated particle effects.
    /// Uses persistent VisualEffect components that receive spawn events rather than
    /// instantiating/destroying prefabs.
    /// 
    /// For ParticleSystem prefabs, delegates to the existing pooling system.
    /// </summary>
    public class VFXGraphManager : MonoBehaviour
    {
        private static VFXGraphManager _instance;
        public static VFXGraphManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    // Try to find existing instance
                    _instance = FindAnyObjectByType<VFXGraphManager>();
                    
                    if (_instance == null)
                    {
                        // Create new instance
                        var go = new GameObject("VFXGraphManager");
                        _instance = go.AddComponent<VFXGraphManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        [Header("Pool Settings")]
        [Tooltip("Maximum number of VFX Graph instances per asset type")]
        [SerializeField] private int maxInstancesPerAsset = 16;

        [Tooltip("Pre-warm VFX Graph instances on first use")]
        [SerializeField] private bool preWarmOnFirstUse = true;

        // Pool of VisualEffect components per VFX Graph asset
        private Dictionary<VisualEffectAsset, List<VisualEffect>> _vfxGraphPools;
        
        // Track which instances are available (by index into the pool list)
        private Dictionary<VisualEffectAsset, Queue<int>> _availableIndices;
        
        // Active VFX instances with their release times
        private List<(VisualEffectAsset asset, int poolIndex, float releaseTime)> _activeVFX;

        // Cached property IDs for faster SetVector3/etc calls
        private static readonly int SpawnPositionId = Shader.PropertyToID(VFXPropertyNames.SpawnPosition);
        private static readonly int SpawnDirectionId = Shader.PropertyToID(VFXPropertyNames.SpawnDirection);
        private static readonly int SpawnNormalId = Shader.PropertyToID(VFXPropertyNames.SpawnNormal);
        private static readonly int SpawnScaleId = Shader.PropertyToID(VFXPropertyNames.SpawnScale);
        private static readonly int EffectRadiusId = Shader.PropertyToID(VFXPropertyNames.EffectRadius);
        private static readonly int SpawnColorId = Shader.PropertyToID(VFXPropertyNames.SpawnColor);
        private static readonly int AOERadiusId = Shader.PropertyToID(VFXPropertyNames.AOERadius);

        // Cached event IDs
        private static readonly int OnSpawnEventId = Shader.PropertyToID(VFXEventNames.OnSpawn);
        private static readonly int OnImpactEventId = Shader.PropertyToID(VFXEventNames.OnImpact);
        private static readonly int OnMuzzleFlashEventId = Shader.PropertyToID(VFXEventNames.OnMuzzleFlash);
        private static readonly int OnAOEExplosionEventId = Shader.PropertyToID(VFXEventNames.OnAOEExplosion);

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            _instance = this;
            DontDestroyOnLoad(gameObject);
            
            Initialize();
        }

        private void Initialize()
        {
            _vfxGraphPools = new Dictionary<VisualEffectAsset, List<VisualEffect>>();
            _availableIndices = new Dictionary<VisualEffectAsset, Queue<int>>();
            _activeVFX = new List<(VisualEffectAsset, int, float)>(256);
        }

        private void Update()
        {
            ReclaimFinishedVFX();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// Gets or creates a pool for the specified VFX Graph asset.
        /// </summary>
        private List<VisualEffect> GetOrCreatePool(VisualEffectAsset asset)
        {
            if (_vfxGraphPools.TryGetValue(asset, out var pool))
            {
                return pool;
            }

            // Create new pool
            pool = new List<VisualEffect>(maxInstancesPerAsset);
            _vfxGraphPools[asset] = pool;
            _availableIndices[asset] = new Queue<int>(maxInstancesPerAsset);

            // Pre-create some instances
            int initialCount = preWarmOnFirstUse ? Mathf.Min(4, maxInstancesPerAsset) : 1;
            for (int i = 0; i < initialCount; i++)
            {
                CreateVFXInstance(asset, pool);
            }

            return pool;
        }

        /// <summary>
        /// Creates a new VFX Graph instance and adds it to the pool.
        /// </summary>
        private VisualEffect CreateVFXInstance(VisualEffectAsset asset, List<VisualEffect> pool)
        {
            var go = new GameObject($"VFX_{asset.name}_{pool.Count}");
            go.transform.SetParent(transform);
            
            var vfx = go.AddComponent<VisualEffect>();
            vfx.visualEffectAsset = asset;
            vfx.Stop(); // Don't auto-play
            go.SetActive(false);
            
            int index = pool.Count;
            pool.Add(vfx);
            _availableIndices[asset].Enqueue(index);
            
            return vfx;
        }

        /// <summary>
        /// Spawns a VFX effect at the specified position.
        /// Works with both VFX Graph (GPU) and ParticleSystem prefabs (CPU).
        /// </summary>
        /// <param name="vfxRef">The VFX reference containing either a ParticleSystem prefab or VFX Graph asset</param>
        /// <param name="position">World position to spawn the effect</param>
        /// <param name="rotation">Rotation of the effect</param>
        /// <param name="duration">How long the effect should last (for pooling/auto-cleanup)</param>
        /// <param name="scale">Optional scale multiplier</param>
        /// <returns>True if the effect was spawned successfully</returns>
        public bool SpawnEffect(VFXReference vfxRef, Vector3 position, Quaternion rotation, float duration = -1f, float scale = 1f)
        {
            if (vfxRef == null || !vfxRef.IsValid)
                return false;

            if (duration < 0)
                duration = vfxRef.GetDuration();

            if (vfxRef.IsVFXGraph)
            {
                return SpawnVFXGraph(vfxRef.vfxGraphAsset, position, rotation, duration, scale);
            }
            else if (vfxRef.IsParticleSystem)
            {
                // Delegate to external ParticleSystem pool (handled by combat/projectile systems)
                // Return false to indicate caller should use their own pooling
                return false;
            }

            return false;
        }

        /// <summary>
        /// Spawns a VFX Graph effect at the specified position.
        /// </summary>
        public bool SpawnVFXGraph(VisualEffectAsset asset, Vector3 position, Quaternion rotation, float duration, float scale = 1f)
        {
            if (asset == null)
                return false;

            var pool = GetOrCreatePool(asset);
            var availableQueue = _availableIndices[asset];

            // Get or create an available instance
            int poolIndex;
            if (availableQueue.Count > 0)
            {
                poolIndex = availableQueue.Dequeue();
            }
            else if (pool.Count < maxInstancesPerAsset)
            {
                // Pool not full, create new instance
                CreateVFXInstance(asset, pool);
                poolIndex = availableQueue.Dequeue();
            }
            else
            {
                // Pool exhausted - find oldest active and reclaim it
                float oldestTime = float.MaxValue;
                int oldestIndex = -1;
                
                for (int i = 0; i < _activeVFX.Count; i++)
                {
                    if (_activeVFX[i].asset == asset && _activeVFX[i].releaseTime < oldestTime)
                    {
                        oldestTime = _activeVFX[i].releaseTime;
                        oldestIndex = i;
                    }
                }

                if (oldestIndex >= 0)
                {
                    poolIndex = _activeVFX[oldestIndex].poolIndex;
                    // Remove from active list
                    int lastIdx = _activeVFX.Count - 1;
                    if (oldestIndex < lastIdx)
                        _activeVFX[oldestIndex] = _activeVFX[lastIdx];
                    _activeVFX.RemoveAt(lastIdx);
                }
                else
                {
                    // Shouldn't happen, but fallback
                    return false;
                }
            }

            // Get the VFX instance
            var vfx = pool[poolIndex];
            
            // Position and activate
            vfx.transform.SetPositionAndRotation(position, rotation);
            if (scale != 1f)
                vfx.transform.localScale = Vector3.one * scale;
            
            vfx.gameObject.SetActive(true);

            // Set spawn parameters via exposed properties
            if (vfx.HasVector3(SpawnPositionId))
                vfx.SetVector3(SpawnPositionId, position);
            
            if (vfx.HasFloat(SpawnScaleId))
                vfx.SetFloat(SpawnScaleId, scale);

            // Send spawn event
            vfx.SendEvent(OnSpawnEventId);
            vfx.Play();

            // Track for reclaim
            float releaseTime = Time.time + duration;
            _activeVFX.Add((asset, poolIndex, releaseTime));

            return true;
        }

        /// <summary>
        /// Spawns an impact effect with direction/normal information.
        /// </summary>
        public bool SpawnImpact(VFXReference vfxRef, Vector3 position, Vector3 normal, float duration = -1f)
        {
            if (vfxRef == null || !vfxRef.IsValid)
                return false;

            if (duration < 0)
                duration = vfxRef.GetDuration();

            if (vfxRef.IsVFXGraph)
            {
                return SpawnImpactVFXGraph(vfxRef.vfxGraphAsset, position, normal, duration);
            }
            
            return false; // ParticleSystem handled externally
        }

        /// <summary>
        /// Spawns an impact VFX Graph effect with direction/normal.
        /// </summary>
        public bool SpawnImpactVFXGraph(VisualEffectAsset asset, Vector3 position, Vector3 normal, float duration)
        {
            if (asset == null)
                return false;

            var pool = GetOrCreatePool(asset);
            var availableQueue = _availableIndices[asset];

            int poolIndex = GetAvailablePoolIndex(asset, pool, availableQueue);
            if (poolIndex < 0)
                return false;

            var vfx = pool[poolIndex];
            
            // Position with rotation facing the normal
            Quaternion rotation = normal != Vector3.zero ? Quaternion.LookRotation(normal) : Quaternion.identity;
            vfx.transform.SetPositionAndRotation(position, rotation);
            vfx.gameObject.SetActive(true);

            // Set impact-specific parameters
            if (vfx.HasVector3(SpawnPositionId))
                vfx.SetVector3(SpawnPositionId, position);
            
            if (vfx.HasVector3(SpawnNormalId))
                vfx.SetVector3(SpawnNormalId, normal);
            
            if (vfx.HasVector3(SpawnDirectionId))
                vfx.SetVector3(SpawnDirectionId, -normal); // Impact direction is opposite of surface normal

            // Send impact event
            vfx.SendEvent(OnImpactEventId);
            vfx.Play();

            // Track for reclaim
            _activeVFX.Add((asset, poolIndex, Time.time + duration));

            return true;
        }

        /// <summary>
        /// Spawns an AOE explosion effect.
        /// </summary>
        public bool SpawnAOEExplosion(VFXReference vfxRef, Vector3 position, float radius, float duration = -1f)
        {
            if (vfxRef == null || !vfxRef.IsValid)
                return false;

            if (duration < 0)
                duration = vfxRef.GetDuration();

            if (vfxRef.IsVFXGraph)
            {
                return SpawnAOEExplosionVFXGraph(vfxRef.vfxGraphAsset, position, radius, duration);
            }
            
            return false; // ParticleSystem handled externally
        }

        /// <summary>
        /// Spawns an AOE explosion VFX Graph effect.
        /// </summary>
        public bool SpawnAOEExplosionVFXGraph(VisualEffectAsset asset, Vector3 position, float radius, float duration)
        {
            if (asset == null)
                return false;

            var pool = GetOrCreatePool(asset);
            var availableQueue = _availableIndices[asset];

            int poolIndex = GetAvailablePoolIndex(asset, pool, availableQueue);
            if (poolIndex < 0)
                return false;

            var vfx = pool[poolIndex];
            
            vfx.transform.position = position;
            vfx.gameObject.SetActive(true);

            // Set AOE-specific parameters
            if (vfx.HasVector3(SpawnPositionId))
                vfx.SetVector3(SpawnPositionId, position);
            
            if (vfx.HasFloat(AOERadiusId))
                vfx.SetFloat(AOERadiusId, radius);
            
            if (vfx.HasFloat(EffectRadiusId))
                vfx.SetFloat(EffectRadiusId, radius);

            // Send AOE event
            vfx.SendEvent(OnAOEExplosionEventId);
            vfx.Play();

            // Track for reclaim
            _activeVFX.Add((asset, poolIndex, Time.time + duration));

            return true;
        }

        /// <summary>
        /// Spawns a muzzle flash effect.
        /// </summary>
        public bool SpawnMuzzleFlash(VFXReference vfxRef, Vector3 position, Vector3 direction, float duration = -1f)
        {
            if (vfxRef == null || !vfxRef.IsValid)
                return false;

            if (duration < 0)
                duration = vfxRef.GetDuration();

            if (vfxRef.IsVFXGraph)
            {
                return SpawnMuzzleFlashVFXGraph(vfxRef.vfxGraphAsset, position, direction, duration);
            }
            
            return false; // ParticleSystem handled externally
        }

        /// <summary>
        /// Spawns a muzzle flash VFX Graph effect.
        /// </summary>
        public bool SpawnMuzzleFlashVFXGraph(VisualEffectAsset asset, Vector3 position, Vector3 direction, float duration)
        {
            if (asset == null)
                return false;

            var pool = GetOrCreatePool(asset);
            var availableQueue = _availableIndices[asset];

            int poolIndex = GetAvailablePoolIndex(asset, pool, availableQueue);
            if (poolIndex < 0)
                return false;

            var vfx = pool[poolIndex];
            
            Quaternion rotation = direction != Vector3.zero ? Quaternion.LookRotation(direction) : Quaternion.identity;
            vfx.transform.SetPositionAndRotation(position, rotation);
            vfx.gameObject.SetActive(true);

            // Set muzzle flash parameters
            if (vfx.HasVector3(SpawnPositionId))
                vfx.SetVector3(SpawnPositionId, position);
            
            if (vfx.HasVector3(SpawnDirectionId))
                vfx.SetVector3(SpawnDirectionId, direction);

            // Send muzzle flash event
            vfx.SendEvent(OnMuzzleFlashEventId);
            vfx.Play();

            // Track for reclaim
            _activeVFX.Add((asset, poolIndex, Time.time + duration));

            return true;
        }

        /// <summary>
        /// Gets an available pool index, creating new instances or reclaiming old ones as needed.
        /// </summary>
        private int GetAvailablePoolIndex(VisualEffectAsset asset, List<VisualEffect> pool, Queue<int> availableQueue)
        {
            if (availableQueue.Count > 0)
            {
                return availableQueue.Dequeue();
            }
            
            if (pool.Count < maxInstancesPerAsset)
            {
                CreateVFXInstance(asset, pool);
                return availableQueue.Dequeue();
            }
            
            // Pool exhausted - find oldest active and reclaim it
            float oldestTime = float.MaxValue;
            int oldestActiveIndex = -1;
            
            for (int i = 0; i < _activeVFX.Count; i++)
            {
                if (_activeVFX[i].asset == asset && _activeVFX[i].releaseTime < oldestTime)
                {
                    oldestTime = _activeVFX[i].releaseTime;
                    oldestActiveIndex = i;
                }
            }

            if (oldestActiveIndex >= 0)
            {
                int poolIndex = _activeVFX[oldestActiveIndex].poolIndex;
                
                // Remove from active list (swap with last)
                int lastIdx = _activeVFX.Count - 1;
                if (oldestActiveIndex < lastIdx)
                    _activeVFX[oldestActiveIndex] = _activeVFX[lastIdx];
                _activeVFX.RemoveAt(lastIdx);
                
                // Stop the VFX before reusing
                pool[poolIndex].Stop();
                
                return poolIndex;
            }

            return -1;
        }

        /// <summary>
        /// Reclaims finished VFX back to their pools.
        /// </summary>
        private void ReclaimFinishedVFX()
        {
            float currentTime = Time.time;
            
            for (int i = _activeVFX.Count - 1; i >= 0; i--)
            {
                var (asset, poolIndex, releaseTime) = _activeVFX[i];
                
                if (currentTime >= releaseTime)
                {
                    // Return to pool
                    if (_vfxGraphPools.TryGetValue(asset, out var pool) && poolIndex < pool.Count)
                    {
                        pool[poolIndex].Stop();
                        pool[poolIndex].gameObject.SetActive(false);
                        _availableIndices[asset].Enqueue(poolIndex);
                    }
                    
                    // Remove from active list (swap with last)
                    int lastIdx = _activeVFX.Count - 1;
                    if (i < lastIdx)
                        _activeVFX[i] = _activeVFX[lastIdx];
                    _activeVFX.RemoveAt(lastIdx);
                }
            }
        }

        /// <summary>
        /// Pre-warms pools for the specified VFX assets.
        /// Call this during loading to avoid hitches during gameplay.
        /// </summary>
        public void PreWarmPools(params VisualEffectAsset[] assets)
        {
            foreach (var asset in assets)
            {
                if (asset != null)
                {
                    GetOrCreatePool(asset);
                }
            }
        }

        /// <summary>
        /// Pre-warms pools for VFXReference assets.
        /// </summary>
        public void PreWarmPools(params VFXReference[] vfxRefs)
        {
            foreach (var vfxRef in vfxRefs)
            {
                if (vfxRef != null && vfxRef.IsVFXGraph)
                {
                    GetOrCreatePool(vfxRef.vfxGraphAsset);
                }
            }
        }

        /// <summary>
        /// Clears all pools and destroys pooled instances.
        /// </summary>
        public void ClearAllPools()
        {
            foreach (var pool in _vfxGraphPools.Values)
            {
                foreach (var vfx in pool)
                {
                    if (vfx != null)
                        Destroy(vfx.gameObject);
                }
            }
            
            _vfxGraphPools.Clear();
            _availableIndices.Clear();
            _activeVFX.Clear();
        }

        /// <summary>
        /// Gets statistics about the VFX pool state.
        /// </summary>
        public (int totalPools, int totalInstances, int activeInstances) GetPoolStats()
        {
            int totalInstances = 0;
            foreach (var pool in _vfxGraphPools.Values)
            {
                totalInstances += pool.Count;
            }
            
            return (_vfxGraphPools.Count, totalInstances, _activeVFX.Count);
        }
    }
}
