using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Central interface for all network authority systems.
    /// Provides a single access point for networking integration.
    /// 
    /// Implement this interface in your networking solution to provide
    /// all network authorities in one place, or implement individual
    /// authority interfaces separately.
    /// </summary>
    public interface INetworkAuthority : 
        INetworkSpawnAuthority, 
        INetworkDamageAuthority, 
        INetworkCommandAuthority
    {
        /// <summary>
        /// Whether networking is currently active and connected.
        /// </summary>
        bool IsNetworkActive { get; }

        /// <summary>
        /// Whether this client is the host (server + client).
        /// </summary>
        bool IsHost { get; }

        /// <summary>
        /// Whether this is a dedicated server (no local player).
        /// </summary>
        bool IsDedicatedServer { get; }

        /// <summary>
        /// Current network time (synchronized across all clients).
        /// </summary>
        float NetworkTime { get; }

        /// <summary>
        /// Round-trip time to server in seconds.
        /// </summary>
        float RoundTripTime { get; }

        /// <summary>
        /// Called when network connection is established.
        /// </summary>
        void OnNetworkConnected();

        /// <summary>
        /// Called when network connection is lost.
        /// </summary>
        void OnNetworkDisconnected();
    }

    /// <summary>
    /// Optional interface for advanced network features.
    /// Implement alongside INetworkAuthority for full multiplayer support.
    /// </summary>
    public interface INetworkAuthorityExtended : INetworkAuthority
    {
        /// <summary>
        /// Gets the transform authority for position sync.
        /// </summary>
        INetworkTransformAuthority TransformAuthority { get; }

        /// <summary>
        /// Gets the state authority for agent state sync.
        /// </summary>
        INetworkStateAuthority StateAuthority { get; }

        /// <summary>
        /// Gets the fog authority for visibility management.
        /// </summary>
        INetworkFogAuthority FogAuthority { get; }

        /// <summary>
        /// Gets the projectile authority for ranged attacks.
        /// </summary>
        INetworkProjectileAuthority ProjectileAuthority { get; }

        /// <summary>
        /// Gets the skill authority for server-authoritative skill execution.
        /// </summary>
        INetworkSkillAuthority SkillAuthority { get; }
    }

    /// <summary>
    /// Static helper for accessing network authorities from EnemyMassesCrowdController.
    /// </summary>
    public static class NetworkAuthorityProvider
    {
        private static INetworkSpawnAuthority _spawnAuthority;
        private static INetworkTransformAuthority _transformAuthority;
        private static INetworkDamageAuthority _damageAuthority;
        private static INetworkCommandAuthority _commandAuthority;
        private static INetworkStateAuthority _stateAuthority;
        private static INetworkFogAuthority _fogAuthority;
        private static INetworkProjectileAuthority _projectileAuthority;
        private static INetworkSkillAuthority _skillAuthority;

        // ── Cached results (refreshed on authority registration changes) ──
        private static bool _cachedIsNetworkMode;
        private static bool _cachedIsServerAuthority = true; // true = single-player default

        /// <summary>
        /// Whether any network authority is set (multiplayer mode).
        /// Cached; updated when authorities are registered/unregistered.
        /// </summary>
        public static bool IsNetworkMode => _cachedIsNetworkMode;

        /// <summary>
        /// True when this runtime instance should execute authoritative simulation.
        /// In single-player (no network authority assigned) this returns true.
        /// In multiplayer this returns true only on server/host authority.
        /// Cached; updated when authorities are registered/unregistered.
        /// Call <see cref="RefreshCache"/> manually after host migration.
        /// </summary>
        public static bool IsServerAuthority => _cachedIsServerAuthority;

        /// <summary>
        /// Recomputes the cached <see cref="IsNetworkMode"/> and <see cref="IsServerAuthority"/>.
        /// Called automatically by <see cref="SetAuthority"/>, <see cref="SetAuthorities"/>,
        /// and <see cref="ClearAuthorities"/>. Call manually after host migration or
        /// any scenario where <c>IsServer</c> changes on an existing authority.
        /// </summary>
        public static void RefreshCache()
        {
            _cachedIsNetworkMode =
                _spawnAuthority != null ||
                _transformAuthority != null ||
                _damageAuthority != null ||
                _commandAuthority != null ||
                _stateAuthority != null ||
                _fogAuthority != null ||
                _projectileAuthority != null ||
                _skillAuthority != null;

            if (!_cachedIsNetworkMode)
            {
                _cachedIsServerAuthority = true;
                return;
            }

            if (_spawnAuthority != null) { _cachedIsServerAuthority = _spawnAuthority.IsServer; return; }
            if (_damageAuthority != null) { _cachedIsServerAuthority = _damageAuthority.IsServer; return; }
            if (_commandAuthority != null) { _cachedIsServerAuthority = _commandAuthority.IsServer; return; }
            if (_stateAuthority != null) { _cachedIsServerAuthority = _stateAuthority.IsServer; return; }
            if (_fogAuthority != null) { _cachedIsServerAuthority = _fogAuthority.IsServer; return; }
            if (_projectileAuthority != null) { _cachedIsServerAuthority = _projectileAuthority.IsServer; return; }
            if (_skillAuthority != null) { _cachedIsServerAuthority = _skillAuthority.IsServer; return; }
            _cachedIsServerAuthority = false;
        }

        /// <summary>
        /// Spawn authority (or null for single-player).
        /// </summary>
        public static INetworkSpawnAuthority SpawnAuthority => _spawnAuthority;

        /// <summary>
        /// Transform authority (or null for single-player).
        /// </summary>
        public static INetworkTransformAuthority TransformAuthority => _transformAuthority;

        /// <summary>
        /// Damage authority (or null for single-player).
        /// </summary>
        public static INetworkDamageAuthority DamageAuthority => _damageAuthority;

        /// <summary>
        /// Command authority (or null for single-player).
        /// </summary>
        public static INetworkCommandAuthority CommandAuthority => _commandAuthority;

        /// <summary>
        /// State authority (or null for single-player).
        /// </summary>
        public static INetworkStateAuthority StateAuthority => _stateAuthority;

        /// <summary>
        /// Fog authority (or null for single-player).
        /// </summary>
        public static INetworkFogAuthority FogAuthority => _fogAuthority;

        /// <summary>
        /// Projectile authority (or null for single-player).
        /// </summary>
        public static INetworkProjectileAuthority ProjectileAuthority => _projectileAuthority;

        /// <summary>
        /// Skill authority for server-authoritative skill execution (or null for single-player).
        /// </summary>
        public static INetworkSkillAuthority SkillAuthority => _skillAuthority;

        /// <summary>
        /// Sets all network authorities from a combined interface.
        /// </summary>
        public static void SetAuthority(INetworkAuthority authority)
        {
            _spawnAuthority = authority;
            _damageAuthority = authority;
            _commandAuthority = authority;

            if (authority is INetworkAuthorityExtended extended)
            {
                _transformAuthority = extended.TransformAuthority;
                _stateAuthority = extended.StateAuthority;
                _fogAuthority = extended.FogAuthority;
                _projectileAuthority = extended.ProjectileAuthority;
                _skillAuthority = extended.SkillAuthority;
            }

            RefreshCache();
        }

        /// <summary>
        /// Sets individual network authorities.
        /// </summary>
        public static void SetAuthorities(
            INetworkSpawnAuthority spawn = null,
            INetworkTransformAuthority transform = null,
            INetworkDamageAuthority damage = null,
            INetworkCommandAuthority command = null,
            INetworkStateAuthority state = null,
            INetworkFogAuthority fog = null,
            INetworkProjectileAuthority projectile = null,
            INetworkSkillAuthority skill = null)
        {
            _spawnAuthority = spawn;
            _transformAuthority = transform;
            _damageAuthority = damage;
            _commandAuthority = command;
            _stateAuthority = state;
            _fogAuthority = fog;
            _projectileAuthority = projectile;
            _skillAuthority = skill;

            RefreshCache();
        }

        /// <summary>
        /// Clears all network authorities (returns to single-player mode).
        /// </summary>
        public static void ClearAuthorities()
        {
            _spawnAuthority = null;
            _transformAuthority = null;
            _damageAuthority = null;
            _commandAuthority = null;
            _stateAuthority = null;
            _fogAuthority = null;
            _projectileAuthority = null;
            _skillAuthority = null;

            RefreshCache();
        }
    }
}
