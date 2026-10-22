using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Type of projectile for network synchronization.
    /// Maps to EnemyMassesProjectileSystem.ProjectileType with additional network-specific types.
    /// </summary>
    public enum NetworkProjectileType : byte
    {
        /// <summary>Standard linear projectile (arrows, bullets)</summary>
        Linear = 0,
        /// <summary>Parabolic projectile (grenades, catapult shots)</summary>
        Parabolic = 1,
        /// <summary>Homing projectile that tracks target</summary>
        Homing = 2,
        /// <summary>Instant hitscan (laser, railgun)</summary>
        Hitscan = 3,
        /// <summary>Area of effect spell/ability</summary>
        AreaOfEffect = 4,
        /// <summary>Beam weapon (continuous damage)</summary>
        Beam = 5
    }

    /// <summary>
    /// Interface for server-authoritative projectile spawning and hit detection.
    /// 
    /// Projectile visuals are simulated locally on all clients (deterministic math),
    /// but hit detection and damage are server-authoritative.
    /// 
    /// This allows responsive visuals while preventing damage cheating.
    /// </summary>
    public interface INetworkProjectileAuthority
    {
        /// <summary>
        /// Called when a projectile should be spawned.
        /// Server validates and broadcasts to all clients.
        /// </summary>
        /// <param name="data">Projectile spawn data</param>
        /// <returns>True if projectile was accepted (for local visual spawn)</returns>
        bool OnProjectileSpawn(ProjectileSpawnData data);

        /// <summary>
        /// Called on clients when server broadcasts a projectile spawn.
        /// Client should spawn visual representation.
        /// </summary>
        /// <param name="data">Projectile spawn data from server</param>
        void OnNetworkProjectileSpawn(ProjectileSpawnData data);

        /// <summary>
        /// Called when a projectile hits a target (server-side).
        /// Server validates hit and broadcasts result.
        /// </summary>
        /// <param name="projectileId">ID of the projectile that hit</param>
        /// <param name="hitData">Hit information</param>
        void OnProjectileHit(int projectileId, ProjectileHitData hitData);

        /// <summary>
        /// Called on clients when server confirms a projectile hit.
        /// Client should spawn impact VFX and update target.
        /// </summary>
        /// <param name="projectileId">ID of the projectile</param>
        /// <param name="hitData">Confirmed hit information</param>
        void OnNetworkProjectileHitConfirmed(int projectileId, ProjectileHitData hitData);

        /// <summary>
        /// Called when a projectile misses or expires.
        /// </summary>
        /// <param name="projectileId">ID of the projectile</param>
        /// <param name="finalPosition">Where the projectile ended up</param>
        void OnProjectileMiss(int projectileId, Vector3 finalPosition);

        /// <summary>
        /// Generates a unique projectile ID. Called by server.
        /// </summary>
        int GenerateProjectileId();

        /// <summary>
        /// Returns true if this instance is the network authority (server/host).
        /// </summary>
        bool IsServer { get; }
    }

    /// <summary>
    /// Data for spawning a networked projectile.
    /// </summary>
    [System.Serializable]
    public struct ProjectileSpawnData
    {
        /// <summary>Unique projectile ID</summary>
        public int projectileId;
        
        /// <summary>Network ID of the source agent</summary>
        public uint sourceNetworkId;
        
        /// <summary>Network ID of the target agent (0 for position-targeted)</summary>
        public uint targetNetworkId;
        
        /// <summary>Target position (used if targetNetworkId is 0)</summary>
        public Vector3 targetPosition;
        
        /// <summary>Spawn position</summary>
        public Vector3 spawnPosition;
        
        /// <summary>Initial velocity/direction</summary>
        public Vector3 velocity;
        
        /// <summary>Type of projectile</summary>
        public NetworkProjectileType projectileType;
        
        /// <summary>Base damage (before multipliers)</summary>
        public float damage;
        
        /// <summary>AOE radius (0 for single-target)</summary>
        public float aoeRadius;
        
        /// <summary>Projectile speed</summary>
        public float speed;
        
        /// <summary>Maximum lifetime in seconds</summary>
        public float lifetime;
        
        /// <summary>Faction index of the source</summary>
        public int sourceFactionIndex;
        
        /// <summary>Visual prefab ID (for client-side spawning)</summary>
        public int visualPrefabId;
        
        /// <summary>Server timestamp of spawn</summary>
        public float serverTime;
        
        /// <summary>Whether this is a secondary attack (e.g., grenade)</summary>
        public bool isSecondaryAttack;
    }

    /// <summary>
    /// Data for a projectile hit event.
    /// </summary>
    [System.Serializable]
    public struct ProjectileHitData
    {
        /// <summary>Network ID of the hit agent (0 if terrain/nothing)</summary>
        public uint hitNetworkId;
        
        /// <summary>World position of impact</summary>
        public Vector3 hitPosition;
        
        /// <summary>Surface normal at hit point</summary>
        public Vector3 hitNormal;
        
        /// <summary>Damage dealt to the target</summary>
        public float damageDealt;
        
        /// <summary>Whether the target died from this hit</summary>
        public bool targetDied;
        
        /// <summary>Network IDs of agents hit by AOE (empty if not AOE)</summary>
        public uint[] aoeHitNetworkIds;
        
        /// <summary>Damage dealt to each AOE target (same order as aoeHitNetworkIds)</summary>
        public float[] aoeDamageDealt;
        
        /// <summary>
        /// Number of valid entries in <see cref="aoeHitNetworkIds"/> and
        /// <see cref="aoeDamageDealt"/>. May be less than the array length
        /// when pooled buffers are used via <see cref="AoeBufferPool"/>.
        /// </summary>
        public int aoeHitCount;
        
        /// <summary>Server timestamp of hit</summary>
        public float serverTime;
        
        /// <summary>Whether impact VFX should be shown</summary>
        public bool showImpactVFX;
    }

    /// <summary>
    /// Simple pool for AOE hit arrays to avoid per-hit allocations.
    /// Callers must return buffers after the network layer has serialized the data.
    /// <para>
    /// Example usage:
    /// <code>
    /// var ids  = AoeBufferPool.RentIds(hitCount);
    /// var dmg  = AoeBufferPool.RentDamage(hitCount);
    /// // … populate ids/dmg, send over network …
    /// AoeBufferPool.Return(ids, dmg);
    /// </code>
    /// </para>
    /// </summary>
    public static class AoeBufferPool
    {
        /// <summary>Hard cap on AOE targets to prevent degenerate allocations.</summary>
        public const int MAX_AOE_TARGETS = 64;

        private static readonly Stack<uint[]> _idPool = new(8);
        private static readonly Stack<float[]> _dmgPool = new(8);

        /// <summary>Rent a uint[] buffer with at least <paramref name="count"/> slots.</summary>
        public static uint[] RentIds(int count)
        {
            if (count <= 0) return System.Array.Empty<uint>();
            if (count > MAX_AOE_TARGETS) count = MAX_AOE_TARGETS;

            if (_idPool.Count > 0)
            {
                var buf = _idPool.Pop();
                if (buf.Length >= count) return buf;
            }
            return new uint[Mathf.Max(count, 8)];
        }

        /// <summary>Rent a float[] buffer with at least <paramref name="count"/> slots.</summary>
        public static float[] RentDamage(int count)
        {
            if (count <= 0) return System.Array.Empty<float>();
            if (count > MAX_AOE_TARGETS) count = MAX_AOE_TARGETS;

            if (_dmgPool.Count > 0)
            {
                var buf = _dmgPool.Pop();
                if (buf.Length >= count) return buf;
            }
            return new float[Mathf.Max(count, 8)];
        }

        /// <summary>
        /// Return rented buffers to the pool. Clears contents to avoid stale data.
        /// Safe to pass null.
        /// </summary>
        public static void Return(uint[] ids, float[] dmg)
        {
            if (ids != null && ids.Length > 0)
            {
                System.Array.Clear(ids, 0, ids.Length);
                _idPool.Push(ids);
            }
            if (dmg != null && dmg.Length > 0)
            {
                System.Array.Clear(dmg, 0, dmg.Length);
                _dmgPool.Push(dmg);
            }
        }

        /// <summary>Clears all pooled buffers. Call on scene unload or session end.</summary>
        public static void Clear()
        {
            _idPool.Clear();
            _dmgPool.Clear();
        }
    }
}
