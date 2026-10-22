using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Helper extensions for routing navigation authority between formation, chase, and combat systems.
    /// </summary>
    public static class AgentNavigationStateExtensions
    {
        /// <summary>
        /// Updates the navigation state on an agent and stamps the change time.
        /// ManualCommand state is protected and cannot be overwritten by other systems.
        /// Use ForceSetNavigationState to override ManualCommand.
        /// </summary>
        public static void SetNavigationState(this CrowdAgent agent, AgentNavigationState newState)
        {
            if (agent == null)
                return;

            if (agent.navigationState == newState)
                return;

            // Protect ManualCommand state - only ForceSetNavigationState can override it
            // This prevents formation, chase, and combat systems from stealing control
            if (agent.navigationState == AgentNavigationState.ManualCommand && 
                newState != AgentNavigationState.ManualCommand)
            {
                // Debug: Show attempted override (throttled)
                if (agent.index < 3 && Time.frameCount % 300 == 0)
                    Debug.Log($"[HOLD DEBUG] Agent {agent.index}: SetNavigationState BLOCKED {agent.navigationState}->{newState} (ManualCommand protected)");
                return;
            }

            // Debug: Log state transitions for HoldPosition agents (throttled)
            if (agent.combatStance == CombatStance.HoldPosition && agent.index < 3 && Time.frameCount % 300 == 0)
            {
                Debug.Log($"[HOLD DEBUG] Agent {agent.index}: SetNavigationState {agent.navigationState}->{newState}");
            }

            agent.navigationState = newState;
            agent.navigationStateChangedTime = Time.time;
        }

        /// <summary>
        /// Forces navigation state change, even if agent is in ManualCommand state.
        /// Use this when the player explicitly issues a new command or when clearing manual control.
        /// </summary>
        public static void ForceSetNavigationState(this CrowdAgent agent, AgentNavigationState newState)
        {
            if (agent == null)
                return;

            if (agent.navigationState == newState)
                return;

            // Clear manual command flag when leaving ManualCommand state
            if (agent.navigationState == AgentNavigationState.ManualCommand && 
                newState != AgentNavigationState.ManualCommand)
            {
                agent.hasManualCommand = false;
            }

            agent.navigationState = newState;
            agent.navigationStateChangedTime = Time.time;
        }
    }
}
