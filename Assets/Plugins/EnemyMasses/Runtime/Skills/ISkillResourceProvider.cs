namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Abstraction for skill resource management (Mana, Energy, Rage, etc.).
    /// 
    /// Implement this interface to integrate a third-party stat/resource system
    /// (e.g. Game Creator 2 Stats, Opsive UCC, custom solution) with Enemy Masses skills.
    /// 
    /// The built-in implementation (BuiltInSkillResourceProvider) provides a simple
    /// per-agent float resource pool. For more advanced systems with multiple resource
    /// types, regeneration curves, or buff modifiers, implement your own provider.
    /// 
    /// Register your provider via:
    ///   SkillResourceManager.Instance.SetProvider(myProvider);
    /// </summary>
    public interface ISkillResourceProvider
    {
        /// <summary>
        /// Can this agent afford the resource cost?
        /// </summary>
        /// <param name="agent">The agent attempting to cast.</param>
        /// <param name="skill">The skill being cast (use skill.ResourceName to identify which resource).</param>
        /// <returns>True if the agent has enough resource to cast.</returns>
        bool HasResource(CrowdAgent agent, AgentSkill skill);
        
        /// <summary>
        /// Consume the resource cost for casting this skill.
        /// Called immediately after the skill passes all other checks and is about to execute.
        /// Only called if HasResource returned true.
        /// </summary>
        /// <param name="agent">The agent casting.</param>
        /// <param name="skill">The skill being cast.</param>
        void ConsumeResource(CrowdAgent agent, AgentSkill skill);
        
        /// <summary>
        /// Get the current resource value for an agent.
        /// The resource type is identified by the skill's ResourceName.
        /// </summary>
        /// <param name="agent">The agent to query.</param>
        /// <param name="resourceName">Name of the resource (e.g. "Mana", "Energy").</param>
        /// <returns>Current resource amount.</returns>
        float GetCurrentResource(CrowdAgent agent, string resourceName);
        
        /// <summary>
        /// Get the maximum resource value for an agent.
        /// </summary>
        /// <param name="agent">The agent to query.</param>
        /// <param name="resourceName">Name of the resource (e.g. "Mana", "Energy").</param>
        /// <returns>Maximum resource amount.</returns>
        float GetMaxResource(CrowdAgent agent, string resourceName);
        
        /// <summary>
        /// Set the current resource value for an agent.
        /// Used for initialization, regen, or external modification.
        /// </summary>
        /// <param name="agent">The agent to modify.</param>
        /// <param name="resourceName">Name of the resource.</param>
        /// <param name="value">New resource value (will be clamped to [0, max]).</param>
        void SetCurrentResource(CrowdAgent agent, string resourceName, float value);
        
        /// <summary>
        /// Set the maximum resource value for an agent.
        /// </summary>
        /// <param name="agent">The agent to modify.</param>
        /// <param name="resourceName">Name of the resource.</param>
        /// <param name="maxValue">New maximum resource value.</param>
        void SetMaxResource(CrowdAgent agent, string resourceName, float maxValue);
        
        /// <summary>
        /// Called when an agent is spawned/initialized. 
        /// Use this to set up initial resource values.
        /// </summary>
        /// <param name="agent">The newly spawned agent.</param>
        /// <param name="resourceName">Name of the resource.</param>
        /// <param name="maxValue">Maximum resource value from configuration.</param>
        void InitializeAgent(CrowdAgent agent, string resourceName, float maxValue);
        
        /// <summary>
        /// Called when an agent is despawned/destroyed.
        /// Use this to clean up any tracked state.
        /// </summary>
        /// <param name="agent">The agent being removed.</param>
        void CleanupAgent(CrowdAgent agent);
    }
}
