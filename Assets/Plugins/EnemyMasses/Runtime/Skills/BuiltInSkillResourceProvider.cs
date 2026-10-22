using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Built-in resource provider for Enemy Masses skills.
    /// Tracks per-agent resources (Mana, Energy, etc.) using simple float pools.
    /// 
    /// Supports multiple resource types per agent (e.g. one skill costs Mana,
    /// another costs Rage) via the resourceName key.
    /// 
    /// For regeneration, call SetCurrentResource periodically or use
    /// SkillResourceManager's built-in regen settings.
    /// </summary>
    public class BuiltInSkillResourceProvider : ISkillResourceProvider
    {
        // Per-agent, per-resource-type storage
        // Key: agent.index, Value: dictionary of resourceName -> (current, max)
        private readonly Dictionary<int, Dictionary<string, ResourceData>> _agentResources 
            = new Dictionary<int, Dictionary<string, ResourceData>>();
        
        private struct ResourceData
        {
            public float Current;
            public float Max;
        }
        
        // ════════════════════════════════════════════════════════════════════
        // ISkillResourceProvider
        // ════════════════════════════════════════════════════════════════════
        
        public bool HasResource(CrowdAgent agent, AgentSkill skill)
        {
            if (agent == null || skill == null || !skill.HasResourceCost) return true;
            
            float current = GetCurrentResource(agent, skill.ResourceName);
            float cost = skill.GetScaledResourceCost(agent);
            return current >= cost;
        }
        
        public void ConsumeResource(CrowdAgent agent, AgentSkill skill)
        {
            if (agent == null || skill == null || !skill.HasResourceCost) return;
            
            float current = GetCurrentResource(agent, skill.ResourceName);
            float cost = skill.GetScaledResourceCost(agent);
            SetCurrentResource(agent, skill.ResourceName, current - cost);
        }
        
        public float GetCurrentResource(CrowdAgent agent, string resourceName)
        {
            if (agent == null || string.IsNullOrEmpty(resourceName)) return 0f;
            
            if (_agentResources.TryGetValue(agent.index, out var resources) &&
                resources.TryGetValue(resourceName, out var data))
            {
                return data.Current;
            }
            return 0f;
        }
        
        public float GetMaxResource(CrowdAgent agent, string resourceName)
        {
            if (agent == null || string.IsNullOrEmpty(resourceName)) return 0f;
            
            if (_agentResources.TryGetValue(agent.index, out var resources) &&
                resources.TryGetValue(resourceName, out var data))
            {
                return data.Max;
            }
            return 0f;
        }
        
        public void SetCurrentResource(CrowdAgent agent, string resourceName, float value)
        {
            if (agent == null || string.IsNullOrEmpty(resourceName)) return;
            
            EnsureAgentExists(agent);
            var resources = _agentResources[agent.index];
            
            if (resources.TryGetValue(resourceName, out var data))
            {
                data.Current = Mathf.Clamp(value, 0f, data.Max);
                resources[resourceName] = data;
            }
            else
            {
                resources[resourceName] = new ResourceData { Current = Mathf.Max(0f, value), Max = value };
            }
        }
        
        public void SetMaxResource(CrowdAgent agent, string resourceName, float maxValue)
        {
            if (agent == null || string.IsNullOrEmpty(resourceName)) return;
            
            EnsureAgentExists(agent);
            var resources = _agentResources[agent.index];
            
            float max = Mathf.Max(0f, maxValue);
            if (resources.TryGetValue(resourceName, out var data))
            {
                data.Max = max;
                data.Current = Mathf.Clamp(data.Current, 0f, max);
                resources[resourceName] = data;
            }
            else
            {
                resources[resourceName] = new ResourceData { Current = max, Max = max };
            }
        }
        
        public void InitializeAgent(CrowdAgent agent, string resourceName, float maxValue)
        {
            if (agent == null || string.IsNullOrEmpty(resourceName)) return;
            
            EnsureAgentExists(agent);
            var resources = _agentResources[agent.index];
            
            float max = Mathf.Max(0f, maxValue);
            resources[resourceName] = new ResourceData { Current = max, Max = max };
        }
        
        public void CleanupAgent(CrowdAgent agent)
        {
            if (agent == null) return;
            _agentResources.Remove(agent.index);
        }
        
        // ════════════════════════════════════════════════════════════════════
        // UTILITY
        // ════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Clear all tracked agent data. Useful for scene transitions.
        /// </summary>
        public void ClearAll()
        {
            _agentResources.Clear();
        }
        
        private void EnsureAgentExists(CrowdAgent agent)
        {
            if (!_agentResources.ContainsKey(agent.index))
            {
                _agentResources[agent.index] = new Dictionary<string, ResourceData>();
            }
        }
    }
}
