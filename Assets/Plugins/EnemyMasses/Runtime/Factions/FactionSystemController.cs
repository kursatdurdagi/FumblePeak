using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Factions
{
    /// <summary>
    /// MonoBehaviour component that initializes and manages the Faction System.
    /// Attach this to your EnemyMassesCrowdController or a persistent manager object.
    /// 
    /// This component:
    /// - Initializes the FactionSystemManager on Awake
    /// - Configures the built-in provider with player targets
    /// - Manages player-controlled factions
    /// - Provides inspector controls for faction relationships
    /// </summary>
    [AddComponentMenu("Enemy Masses/Faction System Controller")]
    public class FactionSystemController : MonoBehaviour
    {
        #region Serialized Fields
        
        [Header("Provider Settings")]
        [Tooltip("Which faction provider to use.\n" +
                 "• BuiltIn: Simple, high-performance faction system\n" +
                 "• LoveHate: Use PixelCrushers Love/Hate (requires adapter)\n" +
                 "• GC2Factions: Use NinjutsuGames Factions (requires adapter)\n" +
                 "• Auto: Automatically select best available provider")]
        [SerializeField] private FactionProviderType providerType = FactionProviderType.Auto;
        
        [Header("Player Configuration")]
        [Tooltip("Transform representing the player. Used to determine relationships to player.")]
        [SerializeField] private Transform playerTarget;
        
        [Tooltip("List of all player targets in multiplayer scenarios.")]
        [SerializeField] private List<Transform> playerTargets = new List<Transform>();
        
        [Tooltip("Faction indices that are controlled by players (RTS-style).\n" +
                 "Player-controlled factions are friendly to the player.")]
        [SerializeField] private List<int> playerControlledFactions = new List<int>();
        
        [Header("Integration Settings")]
        [Tooltip("Settings for Love/Hate integration (if using Love/Hate provider)")]
        [SerializeField] private LoveHateIntegrationSettings loveHateSettings = new LoveHateIntegrationSettings();
        
        [Tooltip("Settings for GC2 Factions integration (if using GC2 provider)")]
        [SerializeField] private GC2FactionIntegrationSettings gc2Settings = new GC2FactionIntegrationSettings();
        
        [Header("Runtime Info (Read Only)")]
        [SerializeField, ReadOnly] private string activeProviderName = "Not Initialized";
        [SerializeField, ReadOnly] private int registeredProviderCount = 0;
        
        #endregion
        
        #region Properties
        
        /// <summary>
        /// Gets the FactionSystemManager instance.
        /// </summary>
        public FactionSystemManager Manager => FactionSystemManager.Instance;
        
        /// <summary>
        /// Gets the currently active faction provider.
        /// </summary>
        public IFactionProvider ActiveProvider => Manager?.ActiveProvider;
        
        /// <summary>
        /// Gets the Love/Hate integration settings.
        /// </summary>
        public LoveHateIntegrationSettings LoveHateSettings => loveHateSettings;
        
        /// <summary>
        /// Gets the GC2 Factions integration settings.
        /// </summary>
        public GC2FactionIntegrationSettings GC2Settings => gc2Settings;
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            InitializeFactionSystem();
        }
        
        private void OnDestroy()
        {
            // Don't destroy the singleton - it may be used by other components
            // FactionSystemManager.DestroyInstance();
        }
        
        #endregion
        
        #region Initialization
        
        private void InitializeFactionSystem()
        {
            var manager = FactionSystemManager.Instance;
            
            // Configure built-in provider
            var builtIn = manager.GetBuiltInProvider();
            if (playerTarget != null)
            {
                builtIn.SetPlayerTarget(playerTarget);
            }
            
            foreach (var factionIndex in playerControlledFactions)
            {
                builtIn.SetPlayerControlledFaction(factionIndex, true);
            }

            ConfigureIntegrationProviders(manager);
            
            // Select provider based on settings
            SelectProvider();
            
            // Update inspector info
            UpdateRuntimeInfo();
            
            Debug.Log($"[FactionSystem] Initialized with provider: {manager.ActiveProvider?.DisplayName ?? "None"}");
        }
        
        private void SelectProvider()
        {
            var manager = FactionSystemManager.Instance;
            
            switch (providerType)
            {
                case FactionProviderType.Auto:
                    manager.AutoSelectProvider();
                    break;
                    
                case FactionProviderType.BuiltIn:
                    manager.SetActiveProvider("enemymasses.builtin");
                    break;
                    
                case FactionProviderType.LoveHate:
                    if (!TrySetProviderIfAvailable(manager, "lovehate"))
                    {
                        Debug.LogWarning("[FactionSystem] Love/Hate provider not available. Falling back to built-in.");
                        manager.SetActiveProvider("enemymasses.builtin");
                    }
                    break;
                    
                case FactionProviderType.GC2Factions:
                    if (!TrySetProviderIfAvailable(manager, "gc2factions"))
                    {
                        Debug.LogWarning("[FactionSystem] GC2 Factions provider not available. Falling back to built-in.");
                        manager.SetActiveProvider("enemymasses.builtin");
                    }
                    break;
            }
        }

        private bool TrySetProviderIfAvailable(FactionSystemManager manager, string providerId)
        {
            var provider = manager.GetProvider(providerId);
            if (provider == null)
                return false;

            if (!provider.IsAvailable)
                return false;

            manager.SetActiveProvider(provider);
            return true;
        }

        private void ConfigureIntegrationProviders(FactionSystemManager manager)
        {
            if (manager.GetProvider("lovehate") is ILoveHateFactionSettingsReceiver loveSettingsReceiver)
            {
                loveSettingsReceiver.ApplySettings(loveHateSettings);
            }
            else if (manager.GetProvider("lovehate") is ILoveHateFactionAdapter loveAdapter)
            {
                loveAdapter.AllyThreshold = loveHateSettings.allyThreshold;
                loveAdapter.EnemyThreshold = loveHateSettings.enemyThreshold;

                if (loveHateSettings.factionMappings != null)
                {
                    for (int i = 0; i < loveHateSettings.factionMappings.Count; i++)
                    {
                        var mapping = loveHateSettings.factionMappings[i];
                        if (mapping?.enemyCrowd == null || mapping.loveHateFactionId < 0)
                            continue;

                        loveAdapter.MapFactionToLoveHate(mapping.enemyCrowd, mapping.loveHateFactionId);
                    }
                }
            }

            if (manager.GetProvider("gc2factions") is IGC2FactionSettingsReceiver gc2SettingsReceiver)
            {
                gc2SettingsReceiver.ApplySettings(gc2Settings);
            }
            else if (manager.GetProvider("gc2factions") is IGC2FactionAdapter gc2Adapter)
            {
                if (gc2Settings.factionMappings == null)
                    return;

                for (int i = 0; i < gc2Settings.factionMappings.Count; i++)
                {
                    var mapping = gc2Settings.factionMappings[i];
                    if (mapping?.enemyCrowd == null)
                        continue;

                    gc2Adapter.MapFactionToGC2(mapping.enemyCrowd, mapping.gc2FactionAsset);
                }
            }
        }
        
        private void UpdateRuntimeInfo()
        {
            var manager = FactionSystemManager.Instance;
            activeProviderName = manager.ActiveProvider?.DisplayName ?? "None";
            registeredProviderCount = manager.RegisteredProviders.Count;
        }
        
        #endregion
        
        #region Public Methods
        
        /// <summary>
        /// Sets a faction as player-controlled.
        /// </summary>
        /// <param name="factionIndex">Index of the faction in EnemyCrowds list</param>
        /// <param name="isPlayerControlled">True if player controls this faction</param>
        public void SetPlayerControlledFaction(int factionIndex, bool isPlayerControlled)
        {
            var builtIn = Manager.GetBuiltInProvider();
            builtIn.SetPlayerControlledFaction(factionIndex, isPlayerControlled);
            
            if (isPlayerControlled && !playerControlledFactions.Contains(factionIndex))
            {
                playerControlledFactions.Add(factionIndex);
            }
            else if (!isPlayerControlled)
            {
                playerControlledFactions.Remove(factionIndex);
            }
        }
        
        /// <summary>
        /// Updates the player target.
        /// </summary>
        /// <param name="target">New player target transform</param>
        public void SetPlayerTarget(Transform target)
        {
            playerTarget = target;
            Manager.GetBuiltInProvider().SetPlayerTarget(target);
        }
        
        /// <summary>
        /// Forces a re-evaluation of all faction relationships.
        /// Call this after modifying relationship configurations at runtime.
        /// </summary>
        public void RefreshRelationships()
        {
            Manager.NotifyRelationshipsChanged();
        }
        
        /// <summary>
        /// Gets the relationship between two agents.
        /// Convenience method that delegates to the active provider.
        /// </summary>
        public RelationshipType GetRelationship(CrowdAgent source, CrowdAgent target)
        {
            return Manager.GetRelationship(source, target);
        }
        
        /// <summary>
        /// Checks if an agent should attack another.
        /// </summary>
        public bool ShouldAttack(CrowdAgent source, CrowdAgent target)
        {
            return Manager.ShouldAttack(source, target);
        }
        
        /// <summary>
        /// Checks if an agent should support/heal another.
        /// </summary>
        public bool ShouldSupport(CrowdAgent source, CrowdAgent target)
        {
            return Manager.ShouldSupport(source, target);
        }
        
        #endregion
    }
    
    /// <summary>
    /// Provider type selection for the faction system.
    /// </summary>
    public enum FactionProviderType
    {
        /// <summary>Automatically select the best available provider</summary>
        Auto,
        
        /// <summary>Use the built-in EnemyMasses faction system</summary>
        BuiltIn,
        
        /// <summary>Use PixelCrushers Love/Hate system</summary>
        LoveHate,
        
        /// <summary>Use NinjutsuGames Factions for Game Creator 2</summary>
        GC2Factions
    }
    
    /// <summary>
    /// ReadOnly attribute for inspector display.
    /// </summary>
    public class ReadOnlyAttribute : PropertyAttribute { }
}
