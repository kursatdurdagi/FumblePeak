using UnityEngine;
using System;
using System.Collections.Generic;
using EnemyMasses.VFX;

namespace Arawn.EnemyMasses.Runtime.Civil
{
    /// <summary>
    /// Type of social interaction between civil agents.
    /// </summary>
    public enum SocialInteractionType
    {
        /// <summary>Agents wave at each other from a distance.</summary>
        Wave,
        
        /// <summary>Agents nod/acknowledge in passing.</summary>
        Nod,
        
        /// <summary>Agents stop and have a brief conversation.</summary>
        Talk,
        
        /// <summary>Agents greet each other with a handshake.</summary>
        Handshake,
        
        /// <summary>Agents hug (friendly greeting).</summary>
        Hug,
        
        /// <summary>Agents stand and chat for extended time.</summary>
        ExtendedConversation,
        
        /// <summary>One agent gives directions to another.</summary>
        GiveDirections,
        
        /// <summary>Agents exchange items.</summary>
        Exchange
    }
    
    /// <summary>
    /// Complexity level of social interaction (affects workflow requirements).
    /// </summary>
    public enum InteractionComplexity
    {
        /// <summary>Simple animation, no precise positioning needed. Uses ComputeAnimator.</summary>
        Simple,
        
        /// <summary>Moderate complexity, soft positioning. Can use MecanimReader.</summary>
        Moderate,
        
        /// <summary>Complex interaction requiring precise positioning and IK. Requires BoneTracker.</summary>
        Complex
    }
    
    /// <summary>
    /// Role of an agent in a social interaction.
    /// </summary>
    public enum InteractionRole
    {
        /// <summary>Agent initiates the interaction.</summary>
        Initiator,
        
        /// <summary>Agent responds/receives the interaction.</summary>
        Responder
    }
    
    /// <summary>
    /// Positioning requirement for social interactions.
    /// </summary>
    public enum InteractionPositioning
    {
        /// <summary>No special positioning, agents stay where they are.</summary>
        None,
        
        /// <summary>Agents face each other.</summary>
        FaceToFace,
        
        /// <summary>Agents stand side by side.</summary>
        SideBySide,
        
        /// <summary>Precise positioning with motion warping.</summary>
        PreciseWithWarping
    }
    
    /// <summary>
    /// Animation clip configuration for a social interaction participant.
    /// </summary>
    [Serializable]
    public class SocialAnimationClip
    {
        [Tooltip("Animation clip to play.")]
        public AnimationClip clip;
        
        [Tooltip("Cross-fade duration into this animation.")]
        [Range(0f, 1f)]
        public float crossFadeDuration = 0.25f;
        
        [Tooltip("Animation speed multiplier.")]
        [Range(0.1f, 3f)]
        public float speedMultiplier = 1f;
        
        [Tooltip("If true, animation loops until interaction ends.")]
        public bool loop = false;
        
        [Tooltip("Normalized time (0-1) when sync point occurs for paired animations.")]
        [Range(0f, 1f)]
        public float syncPoint = 0f;
        
        [Tooltip("Layer to play animation on (-1 = base layer).")]
        public int animationLayer = -1;
    }
    
    /// <summary>
    /// Configuration for positioning during a social interaction.
    /// </summary>
    [Serializable]
    public class InteractionPositioningConfig
    {
        [Tooltip("How agents should be positioned relative to each other.")]
        public InteractionPositioning positioning = InteractionPositioning.FaceToFace;
        
        [Tooltip("Ideal distance between agents for this interaction.")]
        [Range(0.3f, 5f)]
        public float interactionDistance = 1.0f;
        
        [Tooltip("Tolerance for distance matching (agent won't re-position if within tolerance).")]
        [Range(0.1f, 1f)]
        public float distanceTolerance = 0.3f;
        
        [Tooltip("How fast agents move into position.")]
        [Range(0.5f, 5f)]
        public float positioningSpeed = 2f;
        
        [Tooltip("How fast agents rotate to face each other.")]
        [Range(90f, 720f)]
        public float rotationSpeed = 360f;
        
        [Tooltip("If true, use motion warping for precise hand/body contact.")]
        public bool useMotionWarping = false;
        
        [Tooltip("Bone to align for warping (e.g., 'RightHand' for handshake).")]
        public string warpTargetBone;
        
        [Tooltip("Offset from warp target bone.")]
        public Vector3 warpOffset;
    }

    /// <summary>
    /// Optional audio/VFX payloads for social interactions (start/end cues).
    /// Mirrors the melee effects workflow for consistent authoring in the Civil tab.
    /// </summary>
    [Serializable]
    public class SocialInteractionEffect
    {
        [Tooltip("Enable audio/VFX when this trigger fires.")]
        public bool enabled = false;

        [Header("Visuals")]
        [Tooltip("VFX to spawn at the agent when the trigger fires (supports ParticleSystem or VFX Graph).")]
        public VFXReference vfx = new VFXReference { type = VFXType.None, defaultDuration = 1f };

        [Tooltip("Override VFX lifetime (seconds). 0 = use asset duration.")]
        [Range(0f, 5f)]
        public float vfxDuration = 0f;

        [Tooltip("Local-space offset from the agent root for the VFX spawn point.")]
        public Vector3 vfxOffset = Vector3.zero;

        [Header("Audio")]
        [Tooltip("Sound to play when the trigger fires.")]
        public AudioClip sound;

        [Tooltip("Volume of the sound.")]
        [Range(0f, 1f)]
        public float soundVolume = 0.8f;

        [Tooltip("Random pitch variation (±). 0.1 = pitch ranges 0.9–1.1.")]
        [Range(0f, 0.5f)]
        public float soundPitchVariation = 0.1f;

        [Tooltip("Spatial blend (0 = 2D, 1 = 3D).")]
        [Range(0f, 1f)]
        public float soundSpatialBlend = 1f;
    }
    
    /// <summary>
    /// Defines a complete social interaction between agents.
    /// </summary>
    [Serializable]
    public class SocialInteractionDefinition
    {
        [Header("Identity")]
        [Tooltip("Unique identifier for this interaction.")]
        public string interactionId;
        
        [Tooltip("Display name for debugging/UI.")]
        public string displayName;
        
        [Tooltip("Type of social interaction.")]
        public SocialInteractionType interactionType;
        
        [Header("Requirements")]
        [Tooltip("Complexity level (affects workflow requirements).")]
        public InteractionComplexity complexity = InteractionComplexity.Simple;
        
        [Tooltip("Number of participants required (1 = solo gesture, 2 = paired interaction).")]
        [Range(1, 2)]
        public int participantCount = 2;
        
        [Tooltip("Maximum distance to detect potential interaction partners.")]
        [Range(1f, 20f)]
        public float detectionRange = 5f;
        
        [Tooltip("Minimum time since last interaction before this can trigger.")]
        [Range(0f, 300f)]
        public float cooldownTime = 30f;
        
        [Header("Animations")]
        [Tooltip("Animation for the initiator role.")]
        public SocialAnimationClip initiatorAnimation;
        
        [Tooltip("Animation for the responder role.")]
        public SocialAnimationClip responderAnimation;
        
        [Tooltip("Optional intro animation before main interaction.")]
        public SocialAnimationClip introAnimation;
        
        [Tooltip("Optional outro animation after main interaction.")]
        public SocialAnimationClip outroAnimation;
        
        [Header("Positioning")]
        [Tooltip("Positioning configuration for this interaction.")]
        public InteractionPositioningConfig positioningConfig;

        [Header("Effects")]
        [Tooltip("Effects played when the interaction begins (after positioning/setup).")]
        public SocialInteractionEffect startEffect = new SocialInteractionEffect();

        [Tooltip("Effects played when the interaction completes (after outro).")]
        public SocialInteractionEffect endEffect = new SocialInteractionEffect();
        
        [Header("Timing")]
        [Tooltip("Minimum duration of the interaction.")]
        [Min(0.5f)]
        public float minDuration = 2f;
        
        [Tooltip("Maximum duration of the interaction.")]
        [Min(0.5f)]
        public float maxDuration = 5f;
        
        [Tooltip("Time to wait at start before animations begin (for positioning).")]
        [Range(0f, 2f)]
        public float setupTime = 0.5f;
        
        [Header("Selection")]
        [Tooltip("Selection weight when multiple interactions are possible.")]
        [Range(0.1f, 10f)]
        public float selectionWeight = 1f;
        
        [Tooltip("Chance this interaction triggers when conditions are met (0-1).")]
        [Range(0f, 1f)]
        public float triggerChance = 0.3f;
        
        // Events
        /// <summary>Event fired when interaction starts.</summary>
        public event Action<int, int> OnInteractionStarted;
        
        /// <summary>Event fired when interaction ends.</summary>
        public event Action<int, int> OnInteractionEnded;
        
        /// <summary>
        /// Gets the required workflow type based on complexity.
        /// </summary>
        public HybridWorkflowType GetRequiredWorkflow()
        {
            return complexity switch
            {
                InteractionComplexity.Simple => HybridWorkflowType.ComputeAnimator,
                InteractionComplexity.Moderate => HybridWorkflowType.MecanimReader,
                InteractionComplexity.Complex => HybridWorkflowType.BoneTracker,
                _ => HybridWorkflowType.ComputeAnimator
            };
        }
        
        /// <summary>
        /// Gets a random duration within the min/max range.
        /// </summary>
        public float GetRandomDuration()
        {
            return UnityEngine.Random.Range(minDuration, maxDuration);
        }
        
        internal void FireStarted(int initiatorIndex, int responderIndex) 
            => OnInteractionStarted?.Invoke(initiatorIndex, responderIndex);
        
        internal void FireEnded(int initiatorIndex, int responderIndex) 
            => OnInteractionEnded?.Invoke(initiatorIndex, responderIndex);
    }
    
    /// <summary>
    /// Runtime state for an active social interaction.
    /// </summary>
    public class ActiveSocialInteraction
    {
        /// <summary>The interaction definition being executed.</summary>
        public SocialInteractionDefinition definition;
        
        /// <summary>Index of the initiator agent.</summary>
        public int initiatorIndex;
        
        /// <summary>Index of the responder agent (-1 if solo).</summary>
        public int responderIndex;
        
        /// <summary>Time when interaction started.</summary>
        public float startTime;
        
        /// <summary>Total duration of this instance.</summary>
        public float duration;
        
        /// <summary>Current phase of the interaction.</summary>
        public InteractionPhase phase;
        
        /// <summary>Position where initiator should stand.</summary>
        public Vector3 initiatorTargetPosition;
        
        /// <summary>Position where responder should stand.</summary>
        public Vector3 responderTargetPosition;
        
        /// <summary>Direction initiator should face.</summary>
        public Vector3 initiatorTargetForward;
        
        /// <summary>Direction responder should face.</summary>
        public Vector3 responderTargetForward;
        
        /// <summary>Are both agents in position?</summary>
        public bool positioningComplete;
        
        /// <summary>Original workflow of initiator (to restore after).</summary>
        public HybridWorkflowType initiatorOriginalWorkflow;
        
        /// <summary>Original workflow of responder (to restore after).</summary>
        public HybridWorkflowType responderOriginalWorkflow;
        
        /// <summary>
        /// Gets normalized progress of the interaction (0-1).
        /// </summary>
        public float NormalizedProgress => duration > 0 ? Mathf.Clamp01((Time.time - startTime) / duration) : 0f;
        
        /// <summary>
        /// Checks if the interaction has completed.
        /// </summary>
        public bool IsComplete => Time.time >= startTime + duration;
    }
    
    /// <summary>
    /// Phase of a social interaction.
    /// </summary>
    public enum InteractionPhase
    {
        /// <summary>Agents are moving into position.</summary>
        Positioning,
        
        /// <summary>Setup/intro phase.</summary>
        Setup,
        
        /// <summary>Main interaction is playing.</summary>
        Active,
        
        /// <summary>Outro/cleanup phase.</summary>
        Outro,
        
        /// <summary>Interaction is complete.</summary>
        Complete
    }
    
    /// <summary>
    /// Settings for social interaction system.
    /// </summary>
    [Serializable]
    public class SocialInteractionSettings
    {
        [Header("General")]
        [Tooltip("Enable social interactions between civil agents.")]
        public bool enableSocialInteractions = true;
        
        [Tooltip("Global chance modifier for all interactions (0-1).")]
        [Range(0f, 1f)]
        public float globalInteractionChance = 0.5f;
        
        [Tooltip("Minimum time between any interactions for an agent.")]
        [Range(1f, 120f)]
        public float globalCooldown = 15f;
        
        [Tooltip("Check interval for potential interactions (seconds).")]
        [Range(0.1f, 5f)]
        public float interactionCheckInterval = 1f;
        
        [Header("Distance & Detection")]
        [Tooltip("Maximum distance to consider agents for interaction.")]
        [Range(1f, 30f)]
        public float maxDetectionDistance = 10f;
        
        [Tooltip("Agents moving faster than this won't initiate interactions.")]
        [Range(0f, 5f)]
        public float maxSpeedForInteraction = 2f;
        
        [Header("Performance")]
        [Tooltip("Maximum concurrent social interactions.")]
        [Range(1, 100)]
        public int maxConcurrentInteractions = 20;
        
        [Tooltip("Maximum interaction checks per frame.")]
        [Range(1, 50)]
        public int maxChecksPerFrame = 10;
        
        [Header("Interaction Definitions")]
        [Tooltip("Available social interactions.")]
        public List<SocialInteractionDefinition> interactions = new List<SocialInteractionDefinition>();
        
        /// <summary>
        /// Creates default interaction definitions.
        /// </summary>
        public void CreateDefaultInteractions()
        {
            interactions.Clear();
            
            // Wave - Simple, no positioning needed
            interactions.Add(new SocialInteractionDefinition
            {
                interactionId = "wave",
                displayName = "Wave",
                interactionType = SocialInteractionType.Wave,
                complexity = InteractionComplexity.Simple,
                participantCount = 2,
                detectionRange = 8f,
                cooldownTime = 20f,
                minDuration = 1.5f,
                maxDuration = 2.5f,
                triggerChance = 0.4f,
                positioningConfig = new InteractionPositioningConfig
                {
                    positioning = InteractionPositioning.FaceToFace,
                    interactionDistance = 4f,
                    distanceTolerance = 2f
                }
            });
            
            // Nod - Very simple acknowledgment
            interactions.Add(new SocialInteractionDefinition
            {
                interactionId = "nod",
                displayName = "Nod",
                interactionType = SocialInteractionType.Nod,
                complexity = InteractionComplexity.Simple,
                participantCount = 2,
                detectionRange = 3f,
                cooldownTime = 10f,
                minDuration = 0.8f,
                maxDuration = 1.2f,
                triggerChance = 0.6f,
                positioningConfig = new InteractionPositioningConfig
                {
                    positioning = InteractionPositioning.None,
                    interactionDistance = 2f,
                    distanceTolerance = 1f
                }
            });
            
            // Talk - Moderate complexity, face to face
            interactions.Add(new SocialInteractionDefinition
            {
                interactionId = "talk",
                displayName = "Brief Conversation",
                interactionType = SocialInteractionType.Talk,
                complexity = InteractionComplexity.Moderate,
                participantCount = 2,
                detectionRange = 4f,
                cooldownTime = 45f,
                minDuration = 5f,
                maxDuration = 15f,
                setupTime = 1f,
                triggerChance = 0.25f,
                positioningConfig = new InteractionPositioningConfig
                {
                    positioning = InteractionPositioning.FaceToFace,
                    interactionDistance = 1.2f,
                    distanceTolerance = 0.3f,
                    positioningSpeed = 1.5f
                }
            });
            
            // Handshake - Complex, requires precise positioning
            interactions.Add(new SocialInteractionDefinition
            {
                interactionId = "handshake",
                displayName = "Handshake",
                interactionType = SocialInteractionType.Handshake,
                complexity = InteractionComplexity.Complex,
                participantCount = 2,
                detectionRange = 3f,
                cooldownTime = 60f,
                minDuration = 2f,
                maxDuration = 3f,
                setupTime = 0.8f,
                triggerChance = 0.15f,
                positioningConfig = new InteractionPositioningConfig
                {
                    positioning = InteractionPositioning.PreciseWithWarping,
                    interactionDistance = 0.8f,
                    distanceTolerance = 0.1f,
                    positioningSpeed = 1f,
                    useMotionWarping = true,
                    warpTargetBone = "RightHand"
                }
            });
            
            // Hug - Complex interaction
            interactions.Add(new SocialInteractionDefinition
            {
                interactionId = "hug",
                displayName = "Hug",
                interactionType = SocialInteractionType.Hug,
                complexity = InteractionComplexity.Complex,
                participantCount = 2,
                detectionRange = 2f,
                cooldownTime = 120f,
                minDuration = 2.5f,
                maxDuration = 4f,
                setupTime = 1f,
                triggerChance = 0.05f,
                positioningConfig = new InteractionPositioningConfig
                {
                    positioning = InteractionPositioning.PreciseWithWarping,
                    interactionDistance = 0.5f,
                    distanceTolerance = 0.1f,
                    positioningSpeed = 1f,
                    useMotionWarping = true
                }
            });
        }
    }
    
    /// <summary>
    /// Per-agent social interaction state.
    /// </summary>
    public class AgentSocialState
    {
        /// <summary>Time of last interaction.</summary>
        public float lastInteractionTime;
        
        /// <summary>Cooldowns per interaction type.</summary>
        public Dictionary<string, float> interactionCooldowns = new Dictionary<string, float>();
        
        /// <summary>Is agent currently in an interaction?</summary>
        public bool isInInteraction;
        
        /// <summary>Current active interaction (if any).</summary>
        public ActiveSocialInteraction currentInteraction;
        
        /// <summary>Role in current interaction.</summary>
        public InteractionRole currentRole;
        
        /// <summary>
        /// Checks if a specific interaction is on cooldown.
        /// </summary>
        public bool IsOnCooldown(string interactionId, float currentTime)
        {
            if (interactionCooldowns.TryGetValue(interactionId, out float cooldownEnd))
            {
                return currentTime < cooldownEnd;
            }
            return false;
        }
        
        /// <summary>
        /// Sets cooldown for an interaction.
        /// </summary>
        public void SetCooldown(string interactionId, float duration, float currentTime)
        {
            interactionCooldowns[interactionId] = currentTime + duration;
        }
    }
}
