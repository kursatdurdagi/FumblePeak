using UnityEngine;
using EnemyMasses.VFX;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Configuration settings for the Enemy Masses combat system.
    /// </summary>
    [System.Serializable]
    public class CombatSettings
    {
        [Header("Hit Detection")]
        [Tooltip("Global hit detection mode: Distance uses virtual hitboxes; Collider uses Unity colliders/trigger hits.")]
        public HitDetectionMode hitDetectionMode = HitDetectionMode.Distance;

        [Header("Combat System")]
        [Tooltip("Enable the combat system for agent vs agent/player combat")]
        public bool enableCombatSystem = false;
        
        [Tooltip("Enable developer test mode for combat AI")]
        public bool developerTestMode = false;
        
        [Header("Combat Ranges")]
        [Tooltip("Radius around target to search for combat candidates")]
        [Min(1f)]
        public float engagementRadius = 15f;
        
        [Header("Health & Damage")]
        [Tooltip("Base health applied to each crowd agent")]
        [Min(1f)]
        public float agentBaseHealth = 50f;
        
        [Tooltip("Random additive health per agent (0 = identical health)")]
        [Min(0f)]
        public float agentHealthVariance = 15f;
        
        [Tooltip("Damage dealt per successful attack")]
        [Min(0f)]
        public float agentAttackDamage = 10f;
        
        [Header("Combat Timing")]
        [Tooltip("Base cooldown between consecutive attacks")]
        [Min(0f)]
        public float attackCooldown = 2f;
        
        [Tooltip("Randomized offset added to the base attack cooldown")]
        public Vector2 attackCooldownJitter = new Vector2(0.5f, 1.5f);
        
        [Tooltip("Chance (0-1) that an agent will block instead of taking damage")]
        [Range(0f, 1f)]
        public float blockChance = 0.2f;
        
        [Tooltip("Randomized time range (seconds) that an agent will keep the defend state active")]
        public Vector2 blockHoldTimeRange = new Vector2(0.4f, 0.85f);
        
        [Tooltip("Maximum number of combat state updates processed per frame")]
        [Min(8)]
        public int maxCombatUpdatesPerFrame = 512;
        
        [Tooltip("How often combat target positions are updated")]
        [Min(0.1f)]
        public float navTargetUpdateInterval = 0.2f;
        
        [Header("Combat Animations")]
        [Tooltip("Attack animation clips")]
        public AnimationClip[] attackClips;
        
        [Tooltip("Block/defend animation clip")]
        public AnimationClip blockClip;
        
        [Tooltip("Block animation clips array for randomization")]
        public AnimationClip[] blockClips;
        
        [Tooltip("Hit reaction animation clips")]
        public AnimationClip[] hitClips;
        
        [Tooltip("Death animation clip")]
        public AnimationClip deathClip;
        
        [Tooltip("Death animation clips array for randomization")]
        public AnimationClip[] deathClips;
        
        [Header("Root Motion")]
        [Tooltip("Enable root motion for combat animations")]
        public bool enableRootMotion = true;
        
        [Header("Animator Parameters")]
        [Tooltip("Attack trigger parameter names")]
        public string[] attackTriggerParameters = new[] { "attack1", "attack2" };
        
        [Tooltip("Defend bool parameter name")]
        public string defendBoolParameter = "defend";
        
        [Tooltip("Defend bool parameters array for randomization")]
        public string[] defendBoolParameters;
        
        [Tooltip("Hit trigger parameter names")]
        public string[] hitTriggerParameters = new[] { "hit1", "hit2", "hit3" };
        
        [Tooltip("Death trigger parameter names")]
        public string[] deathTriggerParameters = new[] { "death", "die", "death1" };
        
        [Header("Combat Behavior")]
        [Tooltip("Maintain facing direction between attacks")]
        public bool maintainFacingBetweenAttacks = true;
        
        [Tooltip("Stop movement when close to target")]
        public bool stopMovementWhenClose = true;
        
        [Tooltip("Lock movement during attack animations")]
        public bool lockMovementDuringAttacks = true;

        [Tooltip("If enabled, non-lethal damage triggers hit reaction state (short movement stop + hit animation). Disable to keep agents moving while taking damage.")]
        public bool triggerHitReactionOnDamage = true;

        [Tooltip("Enable legacy melee basic attacks. Disable to prevent basic melee damage (skills can still be used).")]
        public bool enableMeleeAutoAttack = true;
        
        [Header("Formation Reformation")]
        [Tooltip("Enable formation reformation when chasing distant targets")]
        public bool enableFormationReformation = false;
        
        [Tooltip("Distance from target where agents reform into formation while chasing")]
        [Min(5f)]
        public float reformationDistance = 20f;
        
        [Tooltip("Distance from target where formation dissolves and agents engage freely")]
        [Min(3f)]
        public float formationDissolveDistance = 10f;
        
        [Tooltip("Size/radius of the formation shape")]
        [Min(1f)]
        public float reformationSize = 8f;
        
        [Tooltip("Spacing between agents in formation (affects row/column spacing)")]
        [Min(0.5f)]
        public float reformationSpacing = 2f;
        
        [Tooltip("Thickness of formation rings/rows (for hollow shapes)")]
        [Min(0.5f)]
        public float reformationThickness = 2f;
        
        [Header("Post-Attack Retreat")]
        [Tooltip("Enable retreat after attacking")]
        public bool enablePostAttackRetreat = true;
        
        [Tooltip("Distance range to retreat after attack")]
        public Vector2 postAttackRetreatDistance = new Vector2(0.75f, 1.5f);
        
        [Header("Ragdoll")]
        [Tooltip("Delay (seconds) before ragdoll activates, giving death animations time to start playing.")]
        [Min(0f)]
        public float ragdollActivationDelay = 0.1f;
        
        [Tooltip("Impulse force applied to ragdoll on death (legacy - use Min/Max for randomization)")]
        public Vector3 ragdollImpulse = new Vector3(0f, 2f, 5f);
        
        [Tooltip("Minimum random impulse (X=sideways, Y=upward, Z=forward from attacker)")]
        public Vector3 ragdollImpulseMin = new Vector3(-1f, 1f, 3f);
        
        [Tooltip("Maximum random impulse (X=sideways, Y=upward, Z=forward from attacker)")]
        public Vector3 ragdollImpulseMax = new Vector3(1f, 3f, 8f);
        
        [Header("Dead Agent Cleanup")]
        [Tooltip("How to handle dead agents")]
        public DeadAgentCleanupMode deadAgentCleanupMode = DeadAgentCleanupMode.Disable;
        
        [Tooltip("Delay before cleanup starts")]
        [Min(0f)]
        public float deadAgentCleanupDelay = 5f;
        
        [Tooltip("Maximum number of dead agents processed per frame")]
        [Min(1)]
        public int maxDeadAgentCleanupPerFrame = 10;
        
        [Tooltip("Process dead agents in batches")]
        public bool batchDeadAgentCleanup = true;
        
        [Tooltip("Disable components on death for optimization")]
        public bool disableComponentsOnDeath = true;
        
        [Tooltip("Optimize dead agent lists")]
        public bool optimizeDeadAgentLists = true;

        [Header("Death Effects")]
        [Tooltip("Default VFX/audio to use when a faction does not override death effects.")]
        public DeathEffectsConfig deathEffectsConfig = new DeathEffectsConfig();

        [Header("Debug")]
        [Tooltip("When enabled, logs detailed damage breakdowns (base, multipliers, final). Developer use only.")]
        public bool enableDebugDamageLogging = false;
        
        [Header("Death Sinking Effect")]
        [Tooltip("Enable death sinking effect (agents sink into ground before being disabled)")]
        public bool enableDeathSinking = false;
        
        [Tooltip("Offset applied to dead agent position over time (e.g., (0, -2, 0) sinks 2 units down, (0, 5, 0) lifts 5 units up)")]
        public Vector3 deathSinkOffset = new Vector3(0f, -2f, 0f);
        
        [Tooltip("Duration in seconds for the sinking animation (shorter = faster sink)")]
        [Min(0.1f)]
        public float deathSinkDuration = 2f;
        
        [Tooltip("Animation curve for sinking movement (allows easing in/out)")]
        public AnimationCurve deathSinkCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }
}
