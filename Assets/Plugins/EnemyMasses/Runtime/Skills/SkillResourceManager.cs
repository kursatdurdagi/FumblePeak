using System;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Central manager for skill resource system (Mana, Energy, etc.).
    /// 
    /// Holds the active ISkillResourceProvider and provides a static API
    /// for querying/consuming resources during skill execution.
    /// 
    /// By default, uses BuiltInSkillResourceProvider. Swap to a third-party
    /// system (e.g. Game Creator 2 Stats) by calling SetProvider().
    /// 
    /// Usage:
    ///   // At game start (e.g. in a bootstrap MonoBehaviour):
    ///   SkillResourceManager.Instance.SetProvider(new GC2StatsSkillResourceProvider());
    /// </summary>
    public class SkillResourceManager
    {
        // ════════════════════════════════════════════════════════════════════
        // SINGLETON
        // ════════════════════════════════════════════════════════════════════
        
        private static SkillResourceManager _instance;
        
        /// <summary>
        /// Gets the singleton instance, creating it lazily if needed.
        /// </summary>
        public static SkillResourceManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new SkillResourceManager();
                }
                return _instance;
            }
        }
        
        // ════════════════════════════════════════════════════════════════════
        // PROVIDER
        // ════════════════════════════════════════════════════════════════════
        
        private ISkillResourceProvider _provider;
        
        /// <summary>
        /// The active resource provider. Never null — defaults to BuiltInSkillResourceProvider.
        /// </summary>
        public ISkillResourceProvider Provider
        {
            get
            {
                if (_provider == null)
                {
                    _provider = new BuiltInSkillResourceProvider();
                }
                return _provider;
            }
        }
        
        /// <summary>
        /// Replace the active resource provider.
        /// Call this at game start to integrate a third-party stat system.
        /// </summary>
        /// <param name="provider">The new provider. Pass null to revert to built-in.</param>
        public void SetProvider(ISkillResourceProvider provider)
        {
            _provider = provider ?? new BuiltInSkillResourceProvider();
            OnProviderChanged?.Invoke(_provider);
        }
        
        /// <summary>
        /// Fired when the resource provider is swapped via SetProvider().
        /// </summary>
        public event Action<ISkillResourceProvider> OnProviderChanged;
        
        // ════════════════════════════════════════════════════════════════════
        // CONVENIENCE API (delegates to active provider)
        // ════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Check if an agent has enough resource to cast a skill.
        /// Returns true if the skill has no resource cost.
        /// </summary>
        public bool HasResource(CrowdAgent agent, AgentSkill skill)
        {
            if (skill == null || !skill.HasResourceCost) return true;
            return Provider.HasResource(agent, skill);
        }
        
        /// <summary>
        /// Consume the resource cost for a skill cast.
        /// No-op if the skill has no resource cost.
        /// </summary>
        public void ConsumeResource(CrowdAgent agent, AgentSkill skill)
        {
            if (skill == null || !skill.HasResourceCost) return;
            Provider.ConsumeResource(agent, skill);
        }
        
        /// <summary>
        /// Get current resource for an agent.
        /// </summary>
        public float GetCurrentResource(CrowdAgent agent, string resourceName)
        {
            return Provider.GetCurrentResource(agent, resourceName);
        }
        
        /// <summary>
        /// Get max resource for an agent.
        /// </summary>
        public float GetMaxResource(CrowdAgent agent, string resourceName)
        {
            return Provider.GetMaxResource(agent, resourceName);
        }
        
        /// <summary>
        /// Initialize an agent's resource pool.
        /// </summary>
        public void InitializeAgent(CrowdAgent agent, string resourceName, float maxValue)
        {
            Provider.InitializeAgent(agent, resourceName, maxValue);
        }
        
        /// <summary>
        /// Clean up an agent's resource state.
        /// </summary>
        public void CleanupAgent(CrowdAgent agent)
        {
            Provider.CleanupAgent(agent);
        }
        
        // ════════════════════════════════════════════════════════════════════
        // RESET (for scene transitions / testing)
        // ════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Reset the singleton. Useful for scene transitions.
        /// The provider will be recreated on next access.
        /// </summary>
        public static void Reset()
        {
            _instance = null;
        }
    }
}
