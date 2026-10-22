using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Factions
{
    /// <summary>
    /// Singleton manager that coordinates faction providers.
    /// Allows EnemyMasses to query relationships through a unified interface,
    /// regardless of which faction system is being used (built-in, Love/Hate, NinjutsuGames, etc.)
    /// </summary>
    public class FactionSystemManager
    {
        #region Singleton
        
        private static FactionSystemManager _instance;
        
        /// <summary>
        /// Gets the singleton instance of the FactionSystemManager.
        /// Creates the instance with the built-in provider if it doesn't exist.
        /// </summary>
        public static FactionSystemManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new FactionSystemManager();
                }
                return _instance;
            }
        }
        
        /// <summary>
        /// Destroys the singleton instance. Call during scene cleanup or application quit.
        /// </summary>
        public static void DestroyInstance()
        {
            if (_instance != null)
            {
                _instance.Shutdown();
                _instance = null;
            }
        }
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Fired when the active faction provider changes.
        /// </summary>
        public event Action<IFactionProvider> OnProviderChanged;
        
        /// <summary>
        /// Fired when any faction relationship changes at runtime.
        /// Listeners should refresh their targeting/combat state.
        /// </summary>
        public event Action OnRelationshipsChanged;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// The currently active faction provider.
        /// </summary>
        public IFactionProvider ActiveProvider { get; private set; }
        
        /// <summary>
        /// All registered faction providers, sorted by priority (highest first).
        /// </summary>
        public IReadOnlyList<IFactionProvider> RegisteredProviders => _providers;
        
        /// <summary>
        /// Returns true if a third-party provider is active (not the built-in).
        /// </summary>
        public bool UsingThirdPartyProvider => ActiveProvider != null && ActiveProvider.ProviderId != _builtInProvider.ProviderId;
        
        #endregion
        
        #region Private Fields
        
        private readonly List<IFactionProvider> _providers = new();
        private readonly EnemyMassesFactionProvider _builtInProvider = new();
        private bool _initialized;
        
        #endregion
        
        #region Initialization
        
        private FactionSystemManager()
        {
            // Register built-in provider as fallback
            RegisterProvider(_builtInProvider);
            SetActiveProvider(_builtInProvider);
            _initialized = true;
        }
        
        /// <summary>
        /// Registers a faction provider. Call this from integration adapters.
        /// </summary>
        /// <param name="provider">The provider to register</param>
        public void RegisterProvider(IFactionProvider provider)
        {
            if (provider == null)
            {
                Debug.LogWarning("[FactionSystem] Attempted to register null provider");
                return;
            }
            
            // Check for duplicate
            if (_providers.Exists(p => p.ProviderId == provider.ProviderId))
            {
                Debug.LogWarning($"[FactionSystem] Provider '{provider.ProviderId}' is already registered");
                return;
            }
            
            _providers.Add(provider);
            
            // Sort by priority (highest first)
            _providers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            
            Debug.Log($"[FactionSystem] Registered provider: {provider.DisplayName} (Priority: {provider.Priority})");
            
            // Auto-switch to higher priority provider if it's available
            if (_initialized && provider.IsAvailable && provider.Priority > ActiveProvider.Priority)
            {
                SetActiveProvider(provider);
            }
        }
        
        /// <summary>
        /// Unregisters a faction provider.
        /// </summary>
        /// <param name="providerId">The provider ID to unregister</param>
        public void UnregisterProvider(string providerId)
        {
            var provider = _providers.Find(p => p.ProviderId == providerId);
            if (provider != null)
            {
                provider.Shutdown();
                _providers.Remove(provider);
                
                // If we removed the active provider, fall back to built-in
                if (ActiveProvider?.ProviderId == providerId)
                {
                    SetActiveProvider(_builtInProvider);
                }
                
                Debug.Log($"[FactionSystem] Unregistered provider: {provider.DisplayName}");
            }
        }
        
        /// <summary>
        /// Sets the active faction provider by ID.
        /// </summary>
        /// <param name="providerId">The provider ID to activate</param>
        /// <returns>True if the provider was found and activated</returns>
        public bool SetActiveProvider(string providerId)
        {
            var provider = _providers.Find(p => p.ProviderId == providerId);
            if (provider != null)
            {
                SetActiveProvider(provider);
                return true;
            }
            
            Debug.LogWarning($"[FactionSystem] Provider '{providerId}' not found");
            return false;
        }
        
        /// <summary>
        /// Sets the active faction provider.
        /// </summary>
        /// <param name="provider">The provider to activate</param>
        public void SetActiveProvider(IFactionProvider provider)
        {
            if (provider == null)
            {
                Debug.LogWarning("[FactionSystem] Cannot set null as active provider");
                return;
            }
            
            // Shutdown old provider
            if (ActiveProvider != null && ActiveProvider != provider)
            {
                ActiveProvider.Shutdown();
            }
            
            // Initialize new provider
            provider.Initialize();
            ActiveProvider = provider;
            
            Debug.Log($"[FactionSystem] Active provider: {provider.DisplayName}");
            OnProviderChanged?.Invoke(provider);
        }
        
        /// <summary>
        /// Selects the best available provider (highest priority that's available).
        /// </summary>
        public void AutoSelectProvider()
        {
            foreach (var provider in _providers)
            {
                if (provider.IsAvailable)
                {
                    SetActiveProvider(provider);
                    return;
                }
            }
            
            // Fallback to built-in
            SetActiveProvider(_builtInProvider);
        }
        
        private void Shutdown()
        {
            foreach (var provider in _providers)
            {
                provider.Shutdown();
            }
            _providers.Clear();
            ActiveProvider = null;
        }
        
        #endregion
        
        #region Relationship Queries (Delegated to Active Provider)
        
        /// <summary>
        /// Gets the relationship between two agents.
        /// </summary>
        public RelationshipType GetRelationship(CrowdAgent sourceAgent, CrowdAgent targetAgent)
        {
            return ActiveProvider?.GetRelationship(sourceAgent, targetAgent) ?? RelationshipType.Neutral;
        }
        
        /// <summary>
        /// Gets the relationship between two agents by index.
        /// </summary>
        public RelationshipType GetRelationship(int sourceAgentIndex, int targetAgentIndex)
        {
            return ActiveProvider?.GetRelationship(sourceAgentIndex, targetAgentIndex) ?? RelationshipType.Neutral;
        }
        
        /// <summary>
        /// Gets the relationship between an agent and an external object.
        /// </summary>
        public RelationshipType GetRelationshipToExternal(CrowdAgent sourceAgent, GameObject targetObject)
        {
            return ActiveProvider?.GetRelationshipToExternal(sourceAgent, targetObject) ?? RelationshipType.Neutral;
        }
        
        /// <summary>
        /// Gets the relationship between two factions.
        /// </summary>
        public RelationshipType GetFactionRelationship(EnemyCrowd sourceFaction, EnemyCrowd targetFaction)
        {
            return ActiveProvider?.GetFactionRelationship(sourceFaction, targetFaction) ?? RelationshipType.Neutral;
        }
        
        /// <summary>
        /// Gets the relationship between two factions by index.
        /// </summary>
        public RelationshipType GetFactionRelationship(int sourceFactionIndex, int targetFactionIndex)
        {
            return ActiveProvider?.GetFactionRelationship(sourceFactionIndex, targetFactionIndex) ?? RelationshipType.Neutral;
        }
        
        /// <summary>
        /// Gets an agent's relationship toward the player(s).
        /// </summary>
        public RelationshipType GetPlayerRelationship(CrowdAgent sourceAgent)
        {
            return ActiveProvider?.GetPlayerRelationship(sourceAgent) ?? RelationshipType.Enemy;
        }
        
        /// <summary>
        /// Gets a faction's relationship toward the player(s).
        /// </summary>
        public RelationshipType GetPlayerRelationship(EnemyCrowd sourceFaction)
        {
            return ActiveProvider?.GetPlayerRelationship(sourceFaction) ?? RelationshipType.Enemy;
        }
        
        /// <summary>
        /// Checks if source should attack target.
        /// </summary>
        public bool ShouldAttack(CrowdAgent sourceAgent, CrowdAgent targetAgent)
        {
            return ActiveProvider?.ShouldAttack(sourceAgent, targetAgent) ?? false;
        }
        
        /// <summary>
        /// Checks if source should support target.
        /// </summary>
        public bool ShouldSupport(CrowdAgent sourceAgent, CrowdAgent targetAgent)
        {
            return ActiveProvider?.ShouldSupport(sourceAgent, targetAgent) ?? false;
        }
        
        #endregion
        
        #region Owner Management (Delegated to Active Provider)
        
        /// <summary>
        /// Gets the owner player ID for an agent.
        /// </summary>
        public int GetOwnerPlayerId(CrowdAgent agent)
        {
            return ActiveProvider?.GetOwnerPlayerId(agent) ?? -1;
        }
        
        /// <summary>
        /// Sets the owner player ID for an agent.
        /// </summary>
        public void SetOwnerPlayerId(CrowdAgent agent, int playerId)
        {
            ActiveProvider?.SetOwnerPlayerId(agent, playerId);
        }
        
        /// <summary>
        /// Checks if two agents have the same owner.
        /// </summary>
        public bool HaveSameOwner(CrowdAgent agentA, CrowdAgent agentB)
        {
            return ActiveProvider?.HaveSameOwner(agentA, agentB) ?? false;
        }
        
        /// <summary>
        /// Gets all agents belonging to a specific owner.
        /// </summary>
        public IEnumerable<CrowdAgent> GetAgentsByOwner(int playerId)
        {
            return ActiveProvider?.GetAgentsByOwner(playerId) ?? System.Array.Empty<CrowdAgent>();
        }
        
        /// <summary>
        /// Gets all agents matching a relationship to the source.
        /// </summary>
        public IEnumerable<CrowdAgent> GetAgentsByRelationship(CrowdAgent sourceAgent, RelationshipFlags relationshipFlags)
        {
            return ActiveProvider?.GetAgentsByRelationship(sourceAgent, relationshipFlags) ?? System.Array.Empty<CrowdAgent>();
        }
        
        #endregion
        
        #region Utility Methods
        
        /// <summary>
        /// Notifies all listeners that relationships have changed.
        /// Call this after modifying faction relationships at runtime.
        /// </summary>
        public void NotifyRelationshipsChanged()
        {
            OnRelationshipsChanged?.Invoke();
            
            // Invalidate built-in provider cache if it's active
            if (ActiveProvider is EnemyMassesFactionProvider builtIn)
            {
                builtIn.InvalidateCache();
            }
        }
        
        /// <summary>
        /// Gets the built-in provider for direct configuration.
        /// Use this to configure player targets, player-controlled factions, etc.
        /// </summary>
        public EnemyMassesFactionProvider GetBuiltInProvider()
        {
            return _builtInProvider;
        }
        
        /// <summary>
        /// Checks if a provider with the given ID is registered.
        /// </summary>
        public bool IsProviderRegistered(string providerId)
        {
            return _providers.Exists(p => p.ProviderId == providerId);
        }
        
        /// <summary>
        /// Gets a registered provider by ID.
        /// </summary>
        public IFactionProvider GetProvider(string providerId)
        {
            return _providers.Find(p => p.ProviderId == providerId);
        }
        
        #endregion
    }
}
