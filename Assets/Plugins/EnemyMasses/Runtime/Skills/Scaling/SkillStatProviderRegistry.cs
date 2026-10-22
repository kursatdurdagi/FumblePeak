using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Skills.Scaling
{
    /// <summary>
    /// Static registry for skill stat providers.
    /// Register your custom ISkillStatProvider implementation here to enable stat-based scaling.
    /// 
    /// Usage:
    /// <code>
    /// // In your game initialization:
    /// SkillStatProviderRegistry.RegisterProvider(new MyCustomStatProvider());
    /// 
    /// // Or for Game Creator 2:
    /// SkillStatProviderRegistry.RegisterProvider(new GC2StatProviderAdapter());
    /// </code>
    /// </summary>
    public static class SkillStatProviderRegistry
    {
        private static ISkillStatProvider _provider;
        private static ISkillStatProvider _fallbackProvider;
        
        /// <summary>
        /// Whether a custom stat provider has been registered.
        /// </summary>
        public static bool HasProvider => _provider != null;
        
        /// <summary>
        /// The currently registered stat provider (or fallback if none registered).
        /// </summary>
        public static ISkillStatProvider Provider => _provider ?? (_fallbackProvider ??= new DefaultSkillStatProvider());
        
        /// <summary>
        /// Registers a custom stat provider for skill scaling.
        /// Call this early in your game initialization (e.g., in a RuntimeInitializeOnLoadMethod).
        /// </summary>
        /// <param name="provider">The stat provider implementation</param>
        public static void RegisterProvider(ISkillStatProvider provider)
        {
            _provider = provider;
            Debug.Log($"[SkillScaling] Registered stat provider: {provider?.GetType().Name ?? "null"}");
        }
        
        /// <summary>
        /// Unregisters the current stat provider (returns to default behavior).
        /// </summary>
        public static void UnregisterProvider()
        {
            _provider = null;
        }
        
        /// <summary>
        /// Gets a stat value using the registered provider.
        /// </summary>
        public static float GetStat(string statName, object context)
        {
            return Provider.GetStat(statName, context);
        }
        
        /// <summary>
        /// Gets the level using the registered provider.
        /// </summary>
        public static int GetLevel(object context)
        {
            return Provider.GetLevel(context);
        }
    }
    
    /// <summary>
    /// Default stat provider that uses CrowdAgent's built-in properties.
    /// Used when no custom provider is registered.
    /// </summary>
    public class DefaultSkillStatProvider : ISkillStatProvider
    {
        public float GetStat(string statName, object context)
        {
            if (context is CrowdAgent agent)
            {
                // Map common stat names to CrowdAgent properties
                switch (statName.ToLowerInvariant())
                {
                    case "level":
                        return agent.level;
                    case "health":
                    case "hp":
                        return agent.health;
                    case "maxhealth":
                    case "max_health":
                    case "maxhp":
                        return agent.maxHealth;
                    case "damage":
                    case "attack":
                        return agent.outgoingDamageMultiplier;
                    case "defense":
                    case "armor":
                        return 1f / Mathf.Max(0.01f, agent.incomingDamageMultiplier);
                    case "speed":
                        return agent.navigationAgent?.Speed ?? (agent.hasBaseSpeedSet ? agent.baseSpeed : 1f);
                    default:
                        // Check custom data dictionary if available
                        if (agent.customData != null && agent.customData.TryGetValue(statName, out object value))
                        {
                            if (value is float f) return f;
                            if (value is int i) return i;
                            if (value is double d) return (float)d;
                        }
                        return 0f;
                }
            }
            return 0f;
        }
        
        public int GetLevel(object context)
        {
            if (context is CrowdAgent agent)
            {
                return agent.level;
            }
            return 1;
        }
        
        public bool HasStat(string statName)
        {
            // Default provider supports these built-in stats
            switch (statName.ToLowerInvariant())
            {
                case "level":
                case "health":
                case "hp":
                case "maxhealth":
                case "max_health":
                case "maxhp":
                case "damage":
                case "attack":
                case "defense":
                case "armor":
                case "speed":
                    return true;
                default:
                    return false;
            }
        }
    }
}
