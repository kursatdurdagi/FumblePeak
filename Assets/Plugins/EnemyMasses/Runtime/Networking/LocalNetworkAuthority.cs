using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Local (single-player) implementation of network authority interfaces.
    /// 
    /// This class provides a working implementation for testing and single-player mode.
    /// All operations are immediately approved since there's no network to validate against.
    /// 
    /// Use this as:
    /// 1. A reference implementation for creating real network solutions
    /// 2. A testing stub for development
    /// 3. The default authority in single-player mode
    /// 
    /// For actual multiplayer, implement INetworkAuthority (or individual interfaces)
    /// with your networking solution (Netcode, Mirror, Photon, etc.)
    /// </summary>
    public class LocalNetworkAuthority : MonoBehaviour, INetworkAuthorityExtended
    {
        #region Configuration
        
        [Header("Authority Settings")]
        [Tooltip("Simulate network conditions by adding artificial delay")]
        [SerializeField] private bool simulateNetworkDelay = false;
        
        [Tooltip("Artificial delay in milliseconds (for testing)")]
        [SerializeField] [Range(0, 500)] private int simulatedDelayMs = 50;
        
        [Tooltip("Enable debug logging for network operations")]
        [SerializeField] private bool enableDebugLogging = false;
        
        [Tooltip("Local player ID for single-player mode")]
        [SerializeField] private int localPlayerId = 0;
        
        #endregion
        
        #region Internal State
        
        private uint _nextNetworkId = 1;
        private int _nextProjectileId = 1;
        private int _nextCommandSequence = 1;
        private int _nextDamageSequence = 1;
        private float _startTime;
        
        // Track spawned agents for despawn validation
        private readonly HashSet<uint> _spawnedNetworkIds = new();
        
        // Sub-authorities (can be overridden)
        private INetworkTransformAuthority _transformAuthority;
        private INetworkStateAuthority _stateAuthority;
        private INetworkFogAuthority _fogAuthority;
        private INetworkProjectileAuthority _projectileAuthority;
        private INetworkSkillAuthority _skillAuthority;
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            _startTime = Time.realtimeSinceStartup;
            
            // Create default sub-authorities
            _transformAuthority = new LocalTransformAuthority();
            _stateAuthority = new LocalStateAuthority();
            _fogAuthority = new LocalFogAuthority(this);
            _projectileAuthority = new LocalProjectileAuthority(this);
            _skillAuthority = new LocalSkillAuthority(this);
        }
        
        private void Start()
        {
            // Register with the provider
            NetworkAuthorityProvider.SetAuthority(this);
            
            if (enableDebugLogging)
            {
                Debug.Log("[LocalNetworkAuthority] Registered as network authority (single-player mode)");
            }
        }
        
        private void OnDestroy()
        {
            // Unregister from provider
            NetworkAuthorityProvider.ClearAuthorities();
        }
        
        #endregion
        
        #region INetworkAuthority Implementation
        
        public bool IsNetworkActive => true; // Always "active" in single-player
        public bool IsHost => true;
        public bool IsDedicatedServer => false;
        public float NetworkTime => Time.realtimeSinceStartup - _startTime;
        public float RoundTripTime => simulateNetworkDelay ? simulatedDelayMs / 1000f : 0f;
        
        public void OnNetworkConnected()
        {
            if (enableDebugLogging)
                Debug.Log("[LocalNetworkAuthority] OnNetworkConnected (no-op in single-player)");
        }
        
        public void OnNetworkDisconnected()
        {
            if (enableDebugLogging)
                Debug.Log("[LocalNetworkAuthority] OnNetworkDisconnected (no-op in single-player)");
        }
        
        #endregion
        
        #region INetworkSpawnAuthority Implementation
        
        public bool IsServer => true;
        public int LocalPlayerId => localPlayerId;
        
        public bool CanSpawnLocally(int factionIndex, Vector3 position, Quaternion rotation, 
            int count, int requestingPlayerId)
        {
            // In single-player, always allow local spawning
            if (enableDebugLogging)
                Debug.Log($"[LocalNetworkAuthority] CanSpawnLocally: faction={factionIndex}, count={count}, player={requestingPlayerId} -> true");
            return true;
        }
        
        public uint[] OnAgentsSpawned(int[] agentIndices, int factionIndex, Vector3[] positions, 
            Quaternion[] rotations, int ownerPlayerId)
        {
            if (enableDebugLogging)
                Debug.Log($"[LocalNetworkAuthority] OnAgentsSpawned: faction={factionIndex}, count={agentIndices.Length}, owner={ownerPlayerId}");
            
            // Generate network IDs for spawned agents
            uint[] networkIds = new uint[agentIndices.Length];
            for (int i = 0; i < agentIndices.Length; i++)
            {
                networkIds[i] = _nextNetworkId++;
                _spawnedNetworkIds.Add(networkIds[i]);
            }
            return networkIds;
        }
        
        public void OnNetworkSpawnRequest(uint[] networkIds, int factionIndex, Vector3[] positions, 
            Quaternion[] rotations, int ownerPlayerId)
        {
            // No-op in single-player - no remote spawn requests
            if (enableDebugLogging)
                Debug.Log($"[LocalNetworkAuthority] OnNetworkSpawnRequest (no-op): {networkIds.Length} agents");
        }
        
        public void OnAgentDespawning(int agentIndex, uint networkId, DespawnReason reason)
        {
            if (enableDebugLogging)
                Debug.Log($"[LocalNetworkAuthority] OnAgentDespawning: agent={agentIndex}, networkId={networkId}, reason={reason}");
            
            _spawnedNetworkIds.Remove(networkId);
        }
        
        public void OnNetworkDespawnRequest(uint networkId, DespawnReason reason)
        {
            // No-op in single-player - no remote despawn requests
            if (enableDebugLogging)
                Debug.Log($"[LocalNetworkAuthority] OnNetworkDespawnRequest (no-op): {networkId}");
        }
        
        public uint GenerateNetworkId()
        {
            return _nextNetworkId++;
        }
        
        #endregion
        
        #region INetworkDamageAuthority Implementation
        
        public bool EnableClientPrediction => false; // No prediction needed in single-player
        
        public bool OnDamageAttempt(uint targetNetworkId, float damage, uint attackerNetworkId,
            Vector3 hitPoint, DamageType damageType, out int sequence)
        {
            // In single-player, always allow damage and process immediately
            sequence = _nextDamageSequence++;
            
            if (enableDebugLogging)
                Debug.Log($"[LocalNetworkAuthority] OnDamageAttempt: target={targetNetworkId}, damage={damage}, seq={sequence} -> approved");
            
            return true;
        }
        
        public void OnDamageConfirmed(uint targetNetworkId, float actualDamage, float newHealth,
            bool isDead, int sequence)
        {
            // No-op in single-player - damage is immediately confirmed
            if (enableDebugLogging)
                Debug.Log($"[LocalNetworkAuthority] OnDamageConfirmed: target={targetNetworkId}, damage={actualDamage}, health={newHealth}, dead={isDead}");
        }
        
        public void OnDamageRollback(uint targetNetworkId, float correctHealth, int sequence,
            DamageRollbackReason reason)
        {
            // No-op in single-player - rollbacks don't happen
            if (enableDebugLogging)
                Debug.Log($"[LocalNetworkAuthority] OnDamageRollback (no-op): target={targetNetworkId}, reason={reason}");
        }
        
        public void OnAgentDeath(uint networkId, uint killerNetworkId, int killerPlayerId,
            Vector3 deathPosition)
        {
            if (enableDebugLogging)
                Debug.Log($"[LocalNetworkAuthority] OnAgentDeath: agent={networkId}, killer={killerNetworkId}, player={killerPlayerId}");
            
            _spawnedNetworkIds.Remove(networkId);
        }
        
        #endregion
        
        #region INetworkCommandAuthority Implementation
        
        public bool OnMoveCommand(MoveCommandData command)
        {
            // In single-player, always accept commands
            if (enableDebugLogging)
                Debug.Log($"[LocalNetworkAuthority] OnMoveCommand: dest={command.destination}, agents={command.agentNetworkIds?.Length ?? 0}");
            return true;
        }
        
        public bool OnAttackCommand(AttackCommandData command)
        {
            if (enableDebugLogging)
                Debug.Log($"[LocalNetworkAuthority] OnAttackCommand: target={command.targetNetworkId}, attackers={command.attackerNetworkIds?.Length ?? 0}");
            return true;
        }
        
        public bool OnStopCommand(StopCommandData command)
        {
            if (enableDebugLogging)
                Debug.Log($"[LocalNetworkAuthority] OnStopCommand: agents={command.agentNetworkIds?.Length ?? 0}");
            return true;
        }
        
        public void OnCommandConfirmed(int commandSequence, bool success, CommandResult result)
        {
            // No-op in single-player - commands are immediately confirmed
            if (enableDebugLogging)
                Debug.Log($"[LocalNetworkAuthority] OnCommandConfirmed: seq={commandSequence}, success={success}");
        }
        
        public void OnCommandRollback(int commandSequence, CommandRejectReason reason)
        {
            // No-op in single-player - commands are never rolled back
            if (enableDebugLogging)
                Debug.Log($"[LocalNetworkAuthority] OnCommandRollback (no-op): seq={commandSequence}, reason={reason}");
        }
        
        public void OnRemoteCommandReceived(RemoteCommandData command)
        {
            // No-op in single-player - no remote commands
        }
        
        public int GetNextCommandSequence()
        {
            return _nextCommandSequence++;
        }
        
        #endregion
        
        #region INetworkAuthorityExtended Implementation
        
        public INetworkTransformAuthority TransformAuthority => _transformAuthority;
        public INetworkStateAuthority StateAuthority => _stateAuthority;
        public INetworkFogAuthority FogAuthority => _fogAuthority;
        public INetworkProjectileAuthority ProjectileAuthority => _projectileAuthority;
        public INetworkSkillAuthority SkillAuthority => _skillAuthority;
        
        #endregion
        
        #region Helper Methods
        
        /// <summary>
        /// Generates a unique projectile ID.
        /// </summary>
        public int GenerateProjectileId()
        {
            return _nextProjectileId++;
        }
        
        /// <summary>
        /// Check if a network ID is currently spawned.
        /// </summary>
        public bool IsNetworkIdSpawned(uint networkId)
        {
            return _spawnedNetworkIds.Contains(networkId);
        }
        
        #endregion
    }
    
    #region Local Sub-Authority Implementations
    
    /// <summary>
    /// Local implementation of transform authority.
    /// </summary>
    internal class LocalTransformAuthority : INetworkTransformAuthority
    {
        public int TransformSyncTickRate => 20;
        public float InterpolationDelay => 0f;
        public bool EnableExtrapolation => false;
        public float MaxExtrapolationTime => 0.2f;
        
        public void OnTransformBatchUpdate(TransformSnapshot[] snapshots)
        {
            // No-op in single-player
        }
        
        public void OnNetworkTransformReceived(TransformSnapshot[] snapshots, float serverTime)
        {
            // No-op in single-player
        }
        
        public bool TryGetInterpolatedTransform(uint networkId, float renderTime, 
            out Vector3 position, out Quaternion rotation)
        {
            // No interpolation needed in single-player
            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }
    }
    
    /// <summary>
    /// Local implementation of state authority.
    /// </summary>
    internal class LocalStateAuthority : INetworkStateAuthority
    {
        public bool IsServer => true;
        public int StateSyncTickRate => 10;
        public bool UseDeltaCompression => false;
        
        public void OnStateBatchUpdate(NetworkAgentState[] dirtyStates, float serverTime)
        {
            // No-op in single-player
        }
        
        public void OnNetworkStateReceived(NetworkAgentState[] states, float serverTime)
        {
            // No-op in single-player
        }
        
        public void RequestFullStateSync(uint[] networkIds)
        {
            // No-op in single-player
        }
        
        public void OnFullStateSyncReceived(NetworkAgentState[] states)
        {
            // No-op in single-player
        }
    }
    
    /// <summary>
    /// Local implementation of fog authority.
    /// </summary>
    internal class LocalFogAuthority : INetworkFogAuthority
    {
        private readonly LocalNetworkAuthority _parent;
        private readonly Dictionary<uint, FogRevealerData> _revealers = new();
        
        public LocalFogAuthority(LocalNetworkAuthority parent)
        {
            _parent = parent;
        }
        
        public bool IsServer => true;
        public int LocalPlayerId => _parent.LocalPlayerId;
        public bool FogOfWarEnabled => false; // Disabled by default in single-player
        
        public bool IsAgentVisibleToLocalPlayer(uint networkId)
        {
            // In single-player, all agents are visible
            return true;
        }
        
        public bool IsPositionVisibleToLocalPlayer(Vector3 worldPosition)
        {
            // In single-player, all positions are visible
            return true;
        }
        
        public void OnAgentVisibilityChanged(uint networkId, bool isNowVisible)
        {
            // No-op in single-player
        }
        
        public void OnFogVisibilityUpdate(int playerId, uint[] revealedAgents, uint[] hiddenAgents)
        {
            // No-op in single-player
        }
        
        public uint[] GetVisibleAgentIds()
        {
            // Return empty - all agents are visible in single-player
            return System.Array.Empty<uint>();
        }

        public void GetVisibleAgentIds(List<uint> result)
        {
            result.Clear();
            // All agents are visible in single-player — nothing to add.
        }
        
        public uint[] GetAgentsOwnedByPlayer(int playerId)
        {
            // Would need to track this if needed
            return System.Array.Empty<uint>();
        }

        public void GetAgentsOwnedByPlayer(int playerId, List<uint> result)
        {
            result.Clear();
        }
        
        public void RegisterFogRevealer(Vector3 position, float radius, int ownerPlayerId, uint revealerId)
        {
            _revealers[revealerId] = new FogRevealerData
            {
                revealerId = revealerId,
                position = position,
                radius = radius,
                ownerPlayerId = ownerPlayerId,
                isActive = true,
                agentNetworkId = 0
            };
        }
        
        public void UnregisterFogRevealer(uint revealerId)
        {
            _revealers.Remove(revealerId);
        }
        
        public void UpdateFogRevealerPosition(uint revealerId, Vector3 newPosition)
        {
            if (_revealers.TryGetValue(revealerId, out var revealer))
            {
                revealer.position = newPosition;
                _revealers[revealerId] = revealer;
            }
        }
    }
    
    /// <summary>
    /// Local implementation of projectile authority.
    /// </summary>
    internal class LocalProjectileAuthority : INetworkProjectileAuthority
    {
        private readonly LocalNetworkAuthority _parent;
        
        public LocalProjectileAuthority(LocalNetworkAuthority parent)
        {
            _parent = parent;
        }
        
        public bool IsServer => true;
        
        public bool OnProjectileSpawn(ProjectileSpawnData data)
        {
            // Always approve in single-player
            return true;
        }
        
        public void OnNetworkProjectileSpawn(ProjectileSpawnData data)
        {
            // No-op - local spawns don't need this
        }
        
        public void OnProjectileHit(int projectileId, ProjectileHitData hitData)
        {
            // Hit is processed locally
        }
        
        public void OnNetworkProjectileHitConfirmed(int projectileId, ProjectileHitData hitData)
        {
            // No-op
        }
        
        public void OnProjectileMiss(int projectileId, Vector3 finalPosition)
        {
            // No-op
        }
        
        public int GenerateProjectileId()
        {
            return _parent.GenerateProjectileId();
        }
    }
    
    /// <summary>
    /// Local implementation of skill authority.
    /// Everything is immediately approved in single-player.
    /// </summary>
    internal class LocalSkillAuthority : INetworkSkillAuthority
    {
        private readonly LocalNetworkAuthority _parent;
        private int _nextSequence = 1;
        
        public LocalSkillAuthority(LocalNetworkAuthority parent)
        {
            _parent = parent;
        }
        
        public bool IsServer => true;
        public bool EnableSkillPrediction => false; // No prediction needed in single-player
        public int SkillStateSyncTickRate => 5;
        
        public bool OnSkillExecutionRequest(SkillExecutionRequestData request)
        {
            // In single-player, always approve immediately
            return true;
        }
        
        public void OnSkillExecutionConfirmed(SkillExecutionConfirmationData confirmation)
        {
            // No-op in single-player — execution is immediate
        }
        
        public void OnSkillExecutionRejected(SkillExecutionRejectionData rejection)
        {
            // No-op in single-player — requests are never rejected
        }
        
        public void OnResourceStateSync(ResourceStateSnapshot[] states, float serverTime)
        {
            // No-op in single-player — resources are always local
        }
        
        public void OnCooldownStateSync(CooldownStateSnapshot[] states, float serverTime)
        {
            // No-op in single-player — cooldowns are always local
        }
        
        public void RequestSkillStateSync(uint[] agentNetworkIds)
        {
            // No-op in single-player — state is always up to date
        }
        
        public void BroadcastSkillCast(NetworkSkillCastEventData castData)
        {
            // No-op in single-player — no remote clients to notify
        }
        
        public void PushCooldownState(CooldownStateSnapshot snapshot)
        {
            // No-op in single-player — cooldowns are always local
        }
        
        public void OnRemoteSkillCastReceived(NetworkSkillCastEventData castData)
        {
            // No-op in single-player — all casts are local
        }
        
        public void OnCooldownStatePush(CooldownStateSnapshot snapshot)
        {
            // No-op in single-player — cooldowns are always authoritative locally
        }
        
        /// <summary>
        /// Generates a sequence number for skill request tracking.
        /// </summary>
        public int GenerateSequence()
        {
            return _nextSequence++;
        }
    }
    
    #endregion
}
