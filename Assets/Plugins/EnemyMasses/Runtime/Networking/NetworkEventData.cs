using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Event data for agent spawn events.
    /// Fired by EnemyMassesCrowdController when agents are spawned.
    /// </summary>
    [System.Serializable]
    public struct SpawnEventData
    {
        /// <summary>Local indices of the spawned agents</summary>
        public int[] agentIndices;
        
        /// <summary>Network IDs of the spawned agents (0 if not networked)</summary>
        public uint[] networkIds;
        
        /// <summary>Faction/EnemyCrowd index</summary>
        public int factionIndex;
        
        /// <summary>Spawn positions for each agent</summary>
        public Vector3[] positions;
        
        /// <summary>Spawn rotations for each agent</summary>
        public Quaternion[] rotations;
        
        /// <summary>Player ID that owns these agents (-1 for AI/server)</summary>
        public int ownerPlayerId;
        
        /// <summary>Spawn group ID (-1 if not part of a group)</summary>
        public int spawnGroupId;
        
        /// <summary>Formation type used for spawning</summary>
        public FormationType formation;
        
        /// <summary>Timestamp of spawn</summary>
        public float timestamp;
    }

    /// <summary>
    /// Event data for damage events.
    /// Fired by EnemyMassesCrowdController when damage is dealt.
    /// </summary>
    [System.Serializable]
    public struct DamageEventData
    {
        /// <summary>Local index of the damaged agent</summary>
        public int targetIndex;
        
        /// <summary>Network ID of the damaged agent</summary>
        public uint targetNetworkId;
        
        /// <summary>Local index of the attacker (-1 if none)</summary>
        public int attackerIndex;
        
        /// <summary>Network ID of the attacker (0 if none)</summary>
        public uint attackerNetworkId;
        
        /// <summary>Damage dealt</summary>
        public float damage;
        
        /// <summary>Health before damage</summary>
        public float previousHealth;
        
        /// <summary>Health after damage</summary>
        public float newHealth;
        
        /// <summary>World position of the hit</summary>
        public Vector3 hitPoint;
        
        /// <summary>Direction of the attack</summary>
        public Vector3 attackDirection;
        
        /// <summary>Whether the damage was blocked</summary>
        public bool wasBlocked;
        
        /// <summary>Whether the target died from this damage</summary>
        public bool isDead;
        
        /// <summary>Type of damage</summary>
        public DamageType damageType;
        
        /// <summary>Timestamp of damage event</summary>
        public float timestamp;
    }

    /// <summary>
    /// Event data for death events.
    /// Fired by EnemyMassesCrowdController when an agent dies.
    /// </summary>
    [System.Serializable]
    public struct DeathEventData
    {
        /// <summary>Local index of the dead agent</summary>
        public int agentIndex;
        
        /// <summary>Network ID of the dead agent</summary>
        public uint networkId;
        
        /// <summary>Local index of the killer (-1 if none)</summary>
        public int killerIndex;
        
        /// <summary>Network ID of the killer (0 if none)</summary>
        public uint killerNetworkId;
        
        /// <summary>Player ID of the killer (-1 if AI/environment)</summary>
        public int killerPlayerId;
        
        /// <summary>Position where death occurred</summary>
        public Vector3 deathPosition;
        
        /// <summary>Direction of the killing blow</summary>
        public Vector3 deathDirection;
        
        /// <summary>Faction index of the dead agent</summary>
        public int factionIndex;
        
        /// <summary>Owner player ID of the dead agent</summary>
        public int ownerPlayerId;
        
        /// <summary>Whether ragdoll should be activated</summary>
        public bool useRagdoll;
        
        /// <summary>Timestamp of death</summary>
        public float timestamp;
    }

    /// <summary>
    /// Event data for command events.
    /// Fired by EnemyMassesRTSController when commands are issued.
    /// </summary>
    [System.Serializable]
    public struct CommandEventData
    {
        /// <summary>Type of command</summary>
        public CommandType commandType;
        
        /// <summary>Local indices of commanded agents</summary>
        public int[] agentIndices;
        
        /// <summary>Network IDs of commanded agents</summary>
        public uint[] networkIds;
        
        /// <summary>Target position (for move commands)</summary>
        public Vector3 targetPosition;
        
        /// <summary>Target agent index (for attack commands)</summary>
        public int targetAgentIndex;
        
        /// <summary>Target network ID (for attack commands)</summary>
        public uint targetNetworkId;
        
        /// <summary>Formation type (for move commands)</summary>
        public FormationType formation;
        
        /// <summary>Formation spacing</summary>
        public float formationSpacing;
        
        /// <summary>Facing direction (zero = auto)</summary>
        public Vector3 facingDirection;
        
        /// <summary>Player ID issuing the command</summary>
        public int playerId;
        
        /// <summary>Command sequence number</summary>
        public int sequence;
        
        /// <summary>Timestamp of command</summary>
        public float timestamp;
    }

    /// <summary>
    /// Event data for health change events.
    /// Fired when agent health changes (damage or healing).
    /// </summary>
    [System.Serializable]
    public struct HealthChangeEventData
    {
        /// <summary>Local index of the agent</summary>
        public int agentIndex;
        
        /// <summary>Network ID of the agent</summary>
        public uint networkId;
        
        /// <summary>Previous health value</summary>
        public float previousHealth;
        
        /// <summary>New health value</summary>
        public float newHealth;
        
        /// <summary>Maximum health</summary>
        public float maxHealth;
        
        /// <summary>Health change amount (positive = heal, negative = damage)</summary>
        public float changeAmount;
        
        /// <summary>Source of the health change</summary>
        public HealthChangeSource source;
        
        /// <summary>Timestamp of change</summary>
        public float timestamp;
    }

    /// <summary>
    /// Source of a health change.
    /// </summary>
    public enum HealthChangeSource
    {
        /// <summary>Damage from an attacker</summary>
        Damage,
        /// <summary>Healing effect</summary>
        Healing,
        /// <summary>Regeneration over time</summary>
        Regeneration,
        /// <summary>Status effect (DOT, HOT)</summary>
        StatusEffect,
        /// <summary>Direct set by game logic</summary>
        GameLogic,
        /// <summary>Network sync correction</summary>
        NetworkSync,
        /// <summary>Other/unknown</summary>
        Other
    }

    /// <summary>
    /// Event data for state change events.
    /// Fired when agent combat or navigation state changes.
    /// </summary>
    [System.Serializable]
    public struct StateChangeEventData
    {
        /// <summary>Local index of the agent</summary>
        public int agentIndex;
        
        /// <summary>Network ID of the agent</summary>
        public uint networkId;
        
        /// <summary>Previous combat state</summary>
        public CrowdCombatState previousCombatState;
        
        /// <summary>New combat state</summary>
        public CrowdCombatState newCombatState;
        
        /// <summary>Previous navigation state</summary>
        public AgentNavigationState previousNavState;
        
        /// <summary>New navigation state</summary>
        public AgentNavigationState newNavState;
        
        /// <summary>Timestamp of change</summary>
        public float timestamp;
    }

    /// <summary>
    /// Event data for skill cast events broadcast over the network.
    /// Sent from server to all clients so they can play VFX/animations
    /// for skills cast by agents they can see.
    /// 
    /// This is the serializable network version. For the local runtime version
    /// with CrowdAgent references, see <see cref="Skills.SkillCastEventData"/>.
    /// </summary>
    [System.Serializable]
    public struct NetworkSkillCastEventData
    {
        /// <summary>Network ID of the caster agent</summary>
        public uint casterNetworkId;

        /// <summary>Skill identifier (AgentSkill.SkillId)</summary>
        public string skillId;

        /// <summary>
        /// Integer key from <see cref="NetworkStringRegistry"/> for fast comparison.
        /// Set via <see cref="SetSkillId"/>.
        /// </summary>
        public int skillKey;

        /// <summary>Slot index in the caster's loadout</summary>
        public int slotIndex;

        /// <summary>World position of the target (for VFX placement)</summary>
        public Vector3 targetPosition;

        /// <summary>Network ID of the primary target (0 if ground-targeted or self)</summary>
        public uint targetNetworkId;

        /// <summary>Whether this was a critical hit (affects VFX intensity)</summary>
        public bool isCritical;

        /// <summary>Server timestamp of the cast</summary>
        public float serverTime;

        /// <summary>Player ID of the caster's owner (-1 for AI)</summary>
        public int casterPlayerId;

        /// <summary>
        /// Sets both <see cref="skillId"/> and <see cref="skillKey"/> in one call.
        /// </summary>
        public void SetSkillId(string id)
        {
            skillId = id;
            skillKey = NetworkStringRegistry.GetOrRegister(id);
        }
    }
}
