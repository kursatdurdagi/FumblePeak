using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Manages footstep and ambient audio playback for crowd agents.
    /// Supports two modes:
    /// 1. Per-Agent: Interval-based playback for each agent (detailed, higher CPU)
    /// 2. Group-Based: Faction-wide footstep sounds based on moving agent count (simple, efficient)
    /// Uses CrowdAudioManager for concurrency-limited spatial audio.
    /// </summary>
    public class FootstepAudioManager
    {
        private readonly List<CrowdAgent> _agents;
        private readonly List<EnemyCrowd> _enemyCrowds;
        private readonly CrowdAudioManager _audioManager;
        
        // Batching for ambient sound updates (footsteps check all agents every frame)
        private int _ambientUpdateCursor = 0;
        private const int MAX_AMBIENT_UPDATES_PER_FRAME = 30;
        
        // Global footstep limiting (for per-agent mode)
        private int _footstepsPlayedThisFrame = 0;
        private int _maxFootstepsPerFrame = 8;
        private float _lastFootstepFrameTime = -1f;
        
        // Global footstep audio source tracking (for per-agent mode)
        private int _activeFootstepSources = 0;
        private int _maxGlobalFootstepSources = 16;
        private Queue<float> _footstepEndTimes = new Queue<float>();
        
        // Group footstep tracking (per faction)
        private Dictionary<EnemyCrowd, GroupFootstepState> _groupFootstepStates = new Dictionary<EnemyCrowd, GroupFootstepState>();
        
        private class GroupFootstepState
        {
            public float nextFootstepTime;
            public int lastMovingCount;
            public Vector3 lastCenterPosition;
        }
        
        // Ambient sound settings
        private const float DEFAULT_AMBIENT_MIN_DISTANCE = 2f;
        private const float DEFAULT_AMBIENT_MAX_DISTANCE = 40f;
        private const float DEFAULT_AMBIENT_VOLUME = 0.6f;
        private const float DEFAULT_AMBIENT_PITCH_VARIATION = 0.1f;
        private const int DEFAULT_AMBIENT_MAX_CONCURRENT = 5;
        
        /// <summary>
        /// Maximum footstep sounds that can be triggered per frame.
        /// Prevents audio spam when many agents move simultaneously.
        /// </summary>
        public int MaxFootstepsPerFrame
        {
            get => _maxFootstepsPerFrame;
            set => _maxFootstepsPerFrame = Mathf.Clamp(value, 1, 50);
        }
        
        /// <summary>
        /// Maximum total concurrent footstep audio sources globally.
        /// Limits overall footstep audio regardless of clip variety.
        /// </summary>
        public int MaxGlobalFootstepSources
        {
            get => _maxGlobalFootstepSources;
            set => _maxGlobalFootstepSources = Mathf.Clamp(value, 1, 100);
        }
        
        /// <summary>
        /// Current number of active footstep audio sources (approximate).
        /// </summary>
        public int ActiveFootstepSources => _activeFootstepSources;
        
        public FootstepAudioManager(List<CrowdAgent> agents, List<EnemyCrowd> enemyCrowds)
        {
            _agents = agents;
            _enemyCrowds = enemyCrowds;
            _audioManager = CrowdAudioManager.Instance;
        }
        
        /// <summary>
        /// Configures global footstep limits.
        /// </summary>
        /// <param name="maxPerFrame">Maximum footsteps that can be triggered per frame (1-50)</param>
        /// <param name="maxGlobalSources">Maximum total concurrent footstep audio sources (1-100)</param>
        public void SetGlobalLimits(int maxPerFrame, int maxGlobalSources)
        {
            MaxFootstepsPerFrame = maxPerFrame;
            MaxGlobalFootstepSources = maxGlobalSources;
        }
        
        /// <summary>
        /// Enable debug logging to console.
        /// </summary>
        public bool DebugLogging { get; set; } = true;  // TEMP: Enable by default for debugging
        
        /// <summary>
        /// Updates interval-based footstep sounds for all agents.
        /// Call this from the main controller update loop.
        /// </summary>
        public void UpdateFootsteps(float deltaTime)
        {
            if (_audioManager == null) return;
            
            int agentCount = _agents.Count;
            if (agentCount == 0) return;
            
            float currentTime = Time.time;
            
            // Reset per-frame counter if this is a new frame
            if (currentTime != _lastFootstepFrameTime)
            {
                _footstepsPlayedThisFrame = 0;
                _lastFootstepFrameTime = currentTime;
                
                // Clean up expired footstep end times and update active count
                while (_footstepEndTimes.Count > 0 && _footstepEndTimes.Peek() <= currentTime)
                {
                    _footstepEndTimes.Dequeue();
                    _activeFootstepSources = Mathf.Max(0, _activeFootstepSources - 1);
                }
            }
            
            // Process each faction - use group mode OR per-agent mode
            foreach (var faction in _enemyCrowds)
            {
                if (faction == null) continue;
                
                // Check if using group footstep mode (alternative system)
                if (faction.useGroupFootstepAudio && faction.HasGroupFootstepAudio)
                {
                    UpdateGroupFootsteps(faction, currentTime);
                }
                else if (faction.enableFootstepAudio)
                {
                    UpdatePerAgentFootsteps(faction, currentTime);
                }
            }
        }
        
        /// <summary>
        /// Updates group-based footstep audio for a faction.
        /// Plays one footstep sound per faction based on how many agents are moving.
        /// </summary>
        private void UpdateGroupFootsteps(EnemyCrowd faction, float currentTime)
        {
            // Only process interval mode here - animation events are handled separately
            if (faction.groupFootstepPlaybackMode != FootstepPlaybackMode.Interval)
                return;
            
            // Get or create state for this faction
            if (!_groupFootstepStates.TryGetValue(faction, out var state))
            {
                state = new GroupFootstepState();
                _groupFootstepStates[faction] = state;
            }
            
            // Count moving agents and find center position
            int movingCount = 0;
            Vector3 centerPos = Vector3.zero;
            float minSpeedThreshold = faction.groupFootstepMinSpeed;
            
            foreach (var agent in _agents)
            {
                if (agent == null || agent.isDead) continue;
                if (agent.enemyCrowd != faction) continue;
                
                // Use CurrentVelocity for more responsive stop detection
                float speed = agent.CurrentVelocity.magnitude;
                if (speed >= minSpeedThreshold)
                {
                    movingCount++;
                    centerPos += GetAgentPosition(agent);
                }
            }
            
            // No one moving - reset state and skip
            if (movingCount == 0)
            {
                state.lastMovingCount = 0;
                state.nextFootstepTime = 0f; // Reset timer so it doesn't immediately play when movement resumes
                return;
            }
            
            // Calculate center of moving agents
            centerPos /= movingCount;
            state.lastCenterPosition = centerPos;
            state.lastMovingCount = movingCount;
            
            // Check interval cooldown
            if (currentTime < state.nextFootstepTime) return;
            
            // Detect surface type at center position if enabled
            FootstepSurfaceType surfaceType = FootstepSurfaceType.Grass;
            if (faction.enableGroupSurfaceDetection && faction.groupSurfaceMapping != null)
            {
                surfaceType = SurfaceDetector.GetSurfaceTypeAtPosition(centerPos, faction.groupSurfaceMapping);
            }
            
            // Get appropriate clip based on moving count and surface type
            AudioClip clip = faction.GetRandomGroupFootstepSound(movingCount, surfaceType);
            if (clip == null) return;
            
            // Play the group footstep sound at center of moving agents
            _audioManager.PlaySound(
                clip,
                centerPos,
                faction.groupFootstepVolume,
                faction.groupFootstepPitchVariation,
                faction.groupFootstepSpatialBlend,
                faction.groupFootstepMaxConcurrent,
                faction.groupFootstepMinDistance,
                faction.groupFootstepMaxDistance
            );
            
            // Calculate next interval
            float baseInterval = Random.Range(faction.groupFootstepMinInterval, faction.groupFootstepMaxInterval);
            
            // Scale interval with agent count if enabled (more agents = faster/busier marching)
            if (faction.scaleIntervalWithAgentCount)
            {
                float scaleFactor = movingCount >= faction.groupFootstepThreshold ? 0.7f : 1.0f;
                baseInterval *= scaleFactor;
            }
            
            state.nextFootstepTime = currentTime + baseInterval;
            
            if (DebugLogging)
            {
                string surfaceInfo = faction.enableGroupSurfaceDetection ? $", surface={surfaceType}" : "";
                Debug.Log($"[GroupFootstep] {faction.name}: {movingCount} moving, pos={centerPos}{surfaceInfo}, clip={clip.name}, next in {baseInterval:F2}s");
            }
        }
        
        /// <summary>
        /// Plays a group footstep sound triggered by animation event.
        /// Call this from your animation event handler for group footstep audio.
        /// </summary>
        /// <param name="faction">The faction to play footstep for</param>
        /// <param name="position">Position to play the sound (typically center of moving agents)</param>
        public void PlayGroupFootstepFromEvent(EnemyCrowd faction, Vector3 position)
        {
            if (faction == null || !faction.useGroupFootstepAudio) return;
            if (faction.groupFootstepPlaybackMode != FootstepPlaybackMode.AnimationEvent) return;
            
            // Get moving count for this faction
            int movingCount = 0;
            foreach (var agent in _agents)
            {
                if (agent == null || agent.isDead) continue;
                if (agent.enemyCrowd != faction) continue;
                
                float speed = agent.CurrentVelocity.magnitude;
                if (speed >= faction.groupFootstepMinSpeed)
                    movingCount++;
            }
            
            if (movingCount == 0) return;
            
            // Detect surface type if enabled
            FootstepSurfaceType surfaceType = FootstepSurfaceType.Grass;
            if (faction.enableGroupSurfaceDetection && faction.groupSurfaceMapping != null)
            {
                surfaceType = SurfaceDetector.GetSurfaceTypeAtPosition(position, faction.groupSurfaceMapping);
            }
            
            // Get clip
            AudioClip clip = faction.GetRandomGroupFootstepSound(movingCount, surfaceType);
            if (clip == null) return;
            
            // Play
            _audioManager.PlaySound(
                clip,
                position,
                faction.groupFootstepVolume,
                faction.groupFootstepPitchVariation,
                faction.groupFootstepSpatialBlend,
                faction.groupFootstepMaxConcurrent,
                faction.groupFootstepMinDistance,
                faction.groupFootstepMaxDistance
            );
            
            if (DebugLogging)
            {
                Debug.Log($"[GroupFootstep Event] {faction.name}: {movingCount} moving, clip={clip.name}");
            }
        }
        
        /// <summary>
        /// Updates per-agent footstep audio for a faction.
        /// Original detailed per-agent approach.
        /// </summary>
        private void UpdatePerAgentFootsteps(EnemyCrowd faction, float currentTime)
        {
            var config = faction.footstepAudioConfig;
            if (config == null || !config.IsEnabled)
            {
                return;
            }
            if (config.playbackMode != FootstepPlaybackMode.Interval)
            {
                if (DebugLogging) Debug.Log($"[Footstep] {faction.name}: Not in Interval mode (mode={config.playbackMode})");
                return;
            }
            
            int checkedAgents = 0;
            int movingAgents = 0;
            int readyAgents = 0;
            
            foreach (var agent in _agents)
            {
                if (agent == null || agent.isDead) continue;
                if (agent.enemyCrowd != faction) continue;
                
                checkedAgents++;
                
                // Check if agent is moving fast enough
                // Use CurrentVelocity for immediate response when agents stop
                float speed = agent.CurrentVelocity.magnitude;
                if (speed < config.minSpeedThreshold) continue;
                
                movingAgents++;
                
                // Check interval cooldown
                if (currentTime < agent.nextFootstepTime) continue;
                
                readyAgents++;
                
                // Check global limits
                if (_footstepsPlayedThisFrame >= _maxFootstepsPerFrame)
                {
                    if (DebugLogging) Debug.Log($"[Footstep] Hit per-frame limit ({_footstepsPlayedThisFrame}/{_maxFootstepsPerFrame})");
                    break;
                }
                if (_activeFootstepSources >= _maxGlobalFootstepSources)
                {
                    if (DebugLogging) Debug.Log($"[Footstep] Hit global source limit ({_activeFootstepSources}/{_maxGlobalFootstepSources})");
                    break;
                }
                
                // Agent is moving and interval has elapsed - play footstep
                Vector3 position = GetAgentPosition(agent);
                
                // Determine surface type if detection is enabled
                AudioClip clip;
                if (config.enableSurfaceDetection && config.surfaceMapping != null)
                {
                    FootstepSurfaceType surface = SurfaceDetector.GetSurfaceTypeAtPosition(
                        position, config.surfaceMapping);
                    clip = config.GetRandomClipForSurface(surface);
                }
                else
                {
                    clip = config.GetRandomClip();
                }
                
                if (clip != null)
                {
                    _audioManager.PlaySound(
                        clip,
                        position,
                        config.volume,
                        config.pitchVariation,
                        config.spatialBlend,
                        config.maxConcurrentSounds,
                        config.minDistance,
                        config.maxDistance
                    );
                    
                    // Track global footstep limits
                    _footstepsPlayedThisFrame++;
                    _activeFootstepSources++;
                    _footstepEndTimes.Enqueue(currentTime + clip.length);
                    
                    if (DebugLogging)
                    {
                        Debug.Log($"[Footstep] PLAYED: speed={speed:F2}, interval={config.minInterval:F2}-{config.maxInterval:F2}s, clip={clip.name}");
                    }
                }
                
                // Schedule next footstep
                float nextInterval = Random.Range(config.minInterval, config.maxInterval);
                agent.nextFootstepTime = currentTime + nextInterval;
            }
            
            // Periodic status log (every 2 seconds)
            if (DebugLogging && Mathf.FloorToInt(currentTime) % 2 == 0 && Mathf.FloorToInt(currentTime) != _lastStatusLogTime)
            {
                _lastStatusLogTime = Mathf.FloorToInt(currentTime);
                Debug.Log($"[Footstep Status] {faction.name}: checked={checkedAgents}, moving={movingAgents} (speed>={config.minSpeedThreshold}), ready={readyAgents} (cooldown elapsed), played={_footstepsPlayedThisFrame}");
            }
        }
        
        private int _lastStatusLogTime = -1;
        
        /// <summary>
        /// Updates ambient sounds for all agents.
        /// Ambient sounds play periodically while agents are alive (chants, grunts, groans).
        /// Call this from the main controller update loop.
        /// </summary>
        public void UpdateAmbientSounds(float deltaTime)
        {
            if (_audioManager == null) return;
            
            int agentCount = _agents.Count;
            if (agentCount == 0) return;
            
            float currentTime = Time.time;
            int processed = 0;
            
            // Process a batch of agents per frame
            for (int i = 0; i < agentCount && processed < MAX_AMBIENT_UPDATES_PER_FRAME; i++)
            {
                int index = (_ambientUpdateCursor + i) % agentCount;
                CrowdAgent agent = _agents[index];
                
                if (agent == null || agent.isDead) continue;
                
                // Get faction config
                EnemyCrowd faction = agent.enemyCrowd;
                if (faction == null) continue;
                if (faction.ambientSounds == null || faction.ambientSounds.Length == 0) continue;
                
                // Check cooldown
                if (currentTime < agent.nextAmbientSoundTime) continue;
                
                // Roll for chance (ambientSoundChance is per-second, we check per batch)
                // Adjust for batch frequency: if we check 30 agents/frame at 60fps, 
                // each agent is checked roughly every (agentCount/30)/60 seconds
                float checkInterval = Mathf.Max(0.1f, (float)agentCount / MAX_AMBIENT_UPDATES_PER_FRAME / 60f);
                float adjustedChance = faction.ambientSoundChance * checkInterval;
                
                if (Random.value > adjustedChance) continue;
                
                // Play ambient sound
                Vector3 position = GetAgentPosition(agent);
                AudioClip clip = faction.GetRandomAmbientSound();
                
                if (clip != null)
                {
                    _audioManager.PlaySound(
                        clip,
                        position,
                        DEFAULT_AMBIENT_VOLUME,
                        DEFAULT_AMBIENT_PITCH_VARIATION,
                        1f, // Spatial blend = 3D
                        DEFAULT_AMBIENT_MAX_CONCURRENT,
                        DEFAULT_AMBIENT_MIN_DISTANCE,
                        DEFAULT_AMBIENT_MAX_DISTANCE
                    );
                }
                
                // Schedule next ambient sound check after cooldown
                agent.nextAmbientSoundTime = currentTime + faction.ambientSoundCooldown;
                processed++;
            }
            
            // Advance cursor for next frame
            _ambientUpdateCursor = (_ambientUpdateCursor + MAX_AMBIENT_UPDATES_PER_FRAME) % Mathf.Max(1, agentCount);
        }
        
        /// <summary>
        /// Plays a footstep sound for a specific agent.
        /// Can be called from animation events via GPUICrowdEventDefinition.
        /// Respects global footstep limits.
        /// </summary>
        /// <param name="agent">The agent to play footstep for</param>
        /// <param name="surfaceType">Optional surface type for varied sounds. Pass null to auto-detect.</param>
        /// <returns>True if footstep was played, false if skipped due to limits.</returns>
        public bool PlayFootstepForAgent(CrowdAgent agent, FootstepSurfaceType? surfaceType = null)
        {
            if (agent == null || agent.isDead) return false;
            if (_audioManager == null) return false;
            
            // Check global limits
            float currentTime = Time.time;
            UpdateGlobalLimitTracking(currentTime);
            
            if (_footstepsPlayedThisFrame >= _maxFootstepsPerFrame) return false;
            if (_activeFootstepSources >= _maxGlobalFootstepSources) return false;
            
            EnemyCrowd faction = agent.enemyCrowd;
            if (faction == null || !faction.enableFootstepAudio) return false;
            
            var config = faction.footstepAudioConfig;
            if (config == null || !config.IsEnabled) return false;
            
            Vector3 position = GetAgentPosition(agent);
            
            // Auto-detect surface if not specified and detection is enabled
            FootstepSurfaceType detectedSurface;
            if (surfaceType.HasValue)
            {
                detectedSurface = surfaceType.Value;
            }
            else if (config.enableSurfaceDetection && config.surfaceMapping != null)
            {
                detectedSurface = SurfaceDetector.GetSurfaceTypeAtPosition(position, config.surfaceMapping);
            }
            else
            {
                detectedSurface = FootstepSurfaceType.Default;
            }
            
            AudioClip clip = config.GetRandomClipForSurface(detectedSurface);
            
            if (clip != null)
            {
                _audioManager.PlaySound(
                    clip,
                    position,
                    config.volume,
                    config.pitchVariation,
                    config.spatialBlend,
                    config.maxConcurrentSounds,
                    config.minDistance,
                    config.maxDistance
                );
                
                // Track global limits
                _footstepsPlayedThisFrame++;
                _activeFootstepSources++;
                _footstepEndTimes.Enqueue(currentTime + clip.length);
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Plays a footstep sound for an agent by index.
        /// Can be called from animation events via GPUICrowdEventDefinition.
        /// </summary>
        public bool PlayFootstepForAgent(int agentIndex, FootstepSurfaceType? surfaceType = null)
        {
            if (agentIndex < 0 || agentIndex >= _agents.Count) return false;
            return PlayFootstepForAgent(_agents[agentIndex], surfaceType);
        }
        
        /// <summary>
        /// Plays a footstep sound at a specific position for a faction.
        /// Useful for external systems or custom event handlers.
        /// Respects global footstep limits.
        /// </summary>
        public bool PlayFootstepAtPosition(Vector3 position, EnemyCrowd faction, FootstepSurfaceType? surfaceType = null)
        {
            if (faction == null || !faction.enableFootstepAudio) return false;
            if (_audioManager == null) return false;
            
            // Check global limits
            float currentTime = Time.time;
            UpdateGlobalLimitTracking(currentTime);
            
            if (_footstepsPlayedThisFrame >= _maxFootstepsPerFrame) return false;
            if (_activeFootstepSources >= _maxGlobalFootstepSources) return false;
            
            var config = faction.footstepAudioConfig;
            if (config == null || !config.IsEnabled) return false;
            
            // Auto-detect surface if not specified and detection is enabled
            FootstepSurfaceType detectedSurface;
            if (surfaceType.HasValue)
            {
                detectedSurface = surfaceType.Value;
            }
            else if (config.enableSurfaceDetection && config.surfaceMapping != null)
            {
                detectedSurface = SurfaceDetector.GetSurfaceTypeAtPosition(position, config.surfaceMapping);
            }
            else
            {
                detectedSurface = FootstepSurfaceType.Default;
            }
            
            AudioClip clip = config.GetRandomClipForSurface(detectedSurface);
            
            if (clip != null)
            {
                _audioManager.PlaySound(
                    clip,
                    position,
                    config.volume,
                    config.pitchVariation,
                    config.spatialBlend,
                    config.maxConcurrentSounds,
                    config.minDistance,
                    config.maxDistance
                );
                
                // Track global limits
                _footstepsPlayedThisFrame++;
                _activeFootstepSources++;
                _footstepEndTimes.Enqueue(currentTime + clip.length);
                return true;
            }
            return false;
        }
        
        /// <summary>
        /// Updates global limit tracking (clears expired sounds, resets per-frame counter).
        /// Called automatically but can be called manually if needed.
        /// </summary>
        private void UpdateGlobalLimitTracking(float currentTime)
        {
            // Reset per-frame counter if this is a new frame
            if (currentTime != _lastFootstepFrameTime)
            {
                _footstepsPlayedThisFrame = 0;
                _lastFootstepFrameTime = currentTime;
            }
            
            // Clean up expired footstep end times and update active count
            while (_footstepEndTimes.Count > 0 && _footstepEndTimes.Peek() <= currentTime)
            {
                _footstepEndTimes.Dequeue();
                _activeFootstepSources = Mathf.Max(0, _activeFootstepSources - 1);
            }
        }
        
        private Vector3 GetAgentPosition(CrowdAgent agent)
        {
            return agent.WorldPosition;
        }
    }
}
