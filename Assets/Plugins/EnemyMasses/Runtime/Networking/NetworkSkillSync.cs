using System.Collections.Generic;
using UnityEngine;
using Arawn.EnemyMasses.Runtime.Skills;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Static helper methods for applying server-authoritative skill state on clients.
    /// 
    /// Networking implementors should call these from their <see cref="INetworkSkillAuthority"/>
    /// implementation when receiving state from the server:
    /// 
    /// <list type="bullet">
    ///   <item><see cref="ApplyCooldownState"/> — Apply authoritative cooldown timings to local trackers.</item>
    ///   <item><see cref="ApplyCooldownEntry"/> — Apply a single cooldown entry to local trackers.</item>
    ///   <item><see cref="ResolveAgentByNetworkId"/> — Find a local CrowdAgent by its network ID.</item>
    ///   <item><see cref="ResolveSkillBySlotIndex"/> — Find a skill slot by index from an agent's config.</item>
    /// </list>
    /// 
    /// For VFX/animation replication of remote skill casts, networking implementors should:
    /// 1. Receive the <see cref="NetworkSkillCastEventData"/> via <see cref="INetworkSkillAuthority.OnRemoteSkillCastReceived"/>.
    /// 2. Resolve the caster agent via <see cref="ResolveAgentByNetworkId"/>.
    /// 3. Resolve the skill via <see cref="ResolveSkillBySlotIndex"/>.
    /// 4. Call <see cref="SkillExecutor.TriggerSkillAnimation"/> and/or play VFX/SFX as needed.
    /// </summary>
    public static class NetworkSkillSync
    {
        /// <summary>
        /// Applies an authoritative <see cref="CooldownStateSnapshot"/> to local cooldown trackers.
        /// 
        /// Call this from <see cref="INetworkSkillAuthority.OnCooldownStatePush"/> or
        /// <see cref="INetworkSkillAuthority.OnCooldownStateSync"/> to keep client-side
        /// cooldown timers accurate.
        /// 
        /// Handles server/client time offset: converts server-relative cooldown end times
        /// to local Time.time by applying the provided <paramref name="serverTimeOffset"/>.
        /// </summary>
        /// <param name="snapshot">Authoritative cooldown state from server</param>
        /// <param name="agentState">Per-agent cooldown tracker (for PerAgent mode skills)</param>
        /// <param name="factionCooldowns">Per-faction cooldown tracker (can be null)</param>
        /// <param name="enemyCrowdCooldowns">Per-EnemyCrowd cooldown tracker (can be null)</param>
        /// <param name="defaultCooldownMode">Default cooldown mode from config</param>
        /// <param name="serverTimeOffset">Time.time - serverTime (0 if clocks are in sync)</param>
        public static void ApplyCooldownState(
            CooldownStateSnapshot snapshot,
            SkillRuntimeState agentState,
            FactionSkillCooldowns factionCooldowns,
            FactionSkillCooldowns enemyCrowdCooldowns,
            SkillCooldownMode defaultCooldownMode,
            float serverTimeOffset = 0f)
        {
            if (snapshot.cooldowns == null || agentState == null) return;

            foreach (var entry in snapshot.cooldowns)
            {
                if (string.IsNullOrEmpty(entry.skillId)) continue;
                
                ApplyCooldownEntry(
                    entry, agentState, factionCooldowns, enemyCrowdCooldowns,
                    defaultCooldownMode, serverTimeOffset);
            }
        }

        /// <summary>
        /// Applies a single <see cref="CooldownEntry"/> to the appropriate cooldown tracker.
        /// </summary>
        /// <param name="entry">Cooldown entry from the server</param>
        /// <param name="agentState">Per-agent cooldown tracker</param>
        /// <param name="factionCooldowns">Per-faction cooldown tracker (can be null)</param>
        /// <param name="enemyCrowdCooldowns">Per-EnemyCrowd cooldown tracker (can be null)</param>
        /// <param name="defaultCooldownMode">Default cooldown mode from config</param>
        /// <param name="serverTimeOffset">Time.time - serverTime (0 if clocks are in sync)</param>
        public static void ApplyCooldownEntry(
            CooldownEntry entry,
            SkillRuntimeState agentState,
            FactionSkillCooldowns factionCooldowns,
            FactionSkillCooldowns enemyCrowdCooldowns,
            SkillCooldownMode defaultCooldownMode,
            float serverTimeOffset = 0f)
        {
            if (string.IsNullOrEmpty(entry.skillId) || agentState == null) return;
            
            // Convert server-relative end time to local time
            float localEndTime = entry.cooldownEndTime > 0f 
                ? entry.cooldownEndTime + serverTimeOffset 
                : 0f;
            
            // Calculate remaining duration from local perspective
            float remaining = localEndTime > 0f ? Mathf.Max(0f, localEndTime - Time.time) : 0f;
            
            if (remaining <= 0f)
            {
                // Cooldown is expired — clear it
                agentState.ResetCooldown(entry.skillId);
                // Also clear shared trackers in case the mode was shared
                factionCooldowns?.ResetCooldown(entry.skillId);
                enemyCrowdCooldowns?.ResetCooldown(entry.skillId);
                return;
            }
            
            // Apply the cooldown with the server-authoritative remaining duration.
            // We apply to ALL trackers that might be relevant since we may not know
            // which mode the server used. The trackers that aren't checked won't
            // cause issues — they'll just carry harmless stale data.
            agentState.StartCooldown(entry.skillId, remaining);
            
            // Note: shared cooldowns (PerFaction/PerEnemyCrowd) are applied too,
            // keeping them in sync. This is safe because if the mode isn't shared,
            // the shared tracker is never queried by IsSkillOnCooldown.
            if (factionCooldowns != null)
                factionCooldowns.StartCooldown(entry.skillId, remaining);
            if (enemyCrowdCooldowns != null)
                enemyCrowdCooldowns.StartCooldown(entry.skillId, remaining);
        }

        /// <summary>
        /// Resolves a <see cref="CrowdAgent"/> by its network ID using the controller's
        /// O(1) dictionary lookup (<see cref="EnemyMassesCrowdController.GetAgentByNetworkId"/>).
        /// Returns null if not found.
        /// </summary>
        /// <param name="controller">The crowd controller to search</param>
        /// <param name="networkId">Network ID to find</param>
        /// <returns>The agent, or null if not found</returns>
        public static CrowdAgent ResolveAgentByNetworkId(
            EnemyMassesCrowdController controller,
            uint networkId)
        {
            if (controller == null || networkId == 0) return null;
            return controller.GetAgentByNetworkId(networkId);
        }

        /// <summary>
        /// Resolves a skill slot by index from an agent's skill system config.
        /// Returns null if the slot index is invalid or the agent has no skill config.
        /// </summary>
        /// <param name="agent">The caster agent</param>
        /// <param name="slotIndex">Slot index (0-based)</param>
        /// <returns>The skill slot, or null</returns>
        public static AgentSkillSlot ResolveSkillBySlotIndex(CrowdAgent agent, int slotIndex)
        {
            if (agent?.enemyCrowd?.skillSystemConfig?.skillSlots == null) return null;
            
            var slots = agent.enemyCrowd.skillSystemConfig.skillSlots;
            if (slotIndex < 0 || slotIndex >= slots.Count) return null;
            return slots[slotIndex];
        }

        /// <summary>
        /// Calculates the server/client time offset for cooldown sync.
        /// Use this when you have a known server time reference.
        /// 
        /// <example>
        /// float offset = NetworkSkillSync.CalculateTimeOffset(snapshot.serverTime);
        /// NetworkSkillSync.ApplyCooldownState(snapshot, agentState, ..., offset);
        /// </example>
        /// </summary>
        /// <param name="serverTime">Server timestamp from the received data</param>
        /// <returns>Offset to add to server times to convert to local Time.time</returns>
        public static float CalculateTimeOffset(float serverTime)
        {
            return Time.time - serverTime;
        }
    }
}
