using System;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Axis around which orbital objects rotate.
    /// </summary>
    public enum OrbitalAxis
    {
        [Tooltip("Orbits horizontally around agent (Y-axis)")]
        Horizontal,
        [Tooltip("Orbits vertically in front of agent (X-axis)")]
        VerticalFront,
        [Tooltip("Orbits vertically to side of agent (Z-axis)")]
        VerticalSide,
        [Tooltip("Tilted orbit - combination of horizontal and vertical")]
        Tilted
    }
    
    /// <summary>
    /// Direction of orbital rotation.
    /// </summary>
    public enum OrbitalDirection
    {
        Clockwise,
        CounterClockwise,
        [Tooltip("Each orbital object alternates direction")]
        Alternating
    }
    
    /// <summary>
    /// Configuration for orbital VFX effects that circle around agents.
    /// Uses GPU Instancer Pro for efficient rendering of thousands of orbital objects.
    /// </summary>
    [Serializable]
    public class OrbitalVFXConfig
    {
        [Tooltip("Enable orbital VFX for this skill.")]
        public bool enabled = false;
        
        [Tooltip("The prefab to orbit around agents.\n" +
                 "Can be any mesh - orbs, shields, weapons, particles.\n" +
                 "Will be automatically registered with GPU Instancer Pro.")]
        public GameObject orbitalPrefab;
        
        [Header("Orbit Settings")]
        [Tooltip("Number of objects orbiting each agent.\n" +
                 "Example: 3 orbs orbiting like electrons.")]
        [Range(1, 12)]
        public int orbitCount = 3;
        
        [Tooltip("Distance from agent center to orbital objects.")]
        [Range(0.5f, 10f)]
        public float orbitRadius = 1.5f;
        
        [Tooltip("Speed of orbit rotation in degrees per second.\n" +
                 "180 = half rotation per second.")]
        [Range(10f, 720f)]
        public float orbitSpeed = 180f;
        
        [Tooltip("Axis around which objects orbit.")]
        public OrbitalAxis orbitAxis = OrbitalAxis.Horizontal;
        
        [Tooltip("Direction of rotation.")]
        public OrbitalDirection direction = OrbitalDirection.Clockwise;
        
        [Tooltip("Tilt angle for Tilted axis mode (degrees from horizontal).")]
        [Range(0f, 60f)]
        public float tiltAngle = 30f;
        
        [Header("Vertical Motion")]
        [Tooltip("Enable vertical bobbing motion.")]
        public bool enableBobbing = false;
        
        [Tooltip("Height of vertical bob motion.")]
        [Range(0f, 2f)]
        public float bobHeight = 0.3f;
        
        [Tooltip("Speed of vertical bob motion.")]
        [Range(0.5f, 5f)]
        public float bobSpeed = 2f;
        
        [Header("Scale & Offset")]
        [Tooltip("Height offset above agent's pivot point.")]
        [Range(-2f, 3f)]
        public float heightOffset = 1f;
        
        [Tooltip("Scale of orbital objects.\n" +
                 "1 = prefab's original scale.")]
        [Range(0.1f, 3f)]
        public float orbitalScale = 1f;
        
        [Tooltip("If true, orbital objects face outward from center.\n" +
                 "If false, they maintain their original rotation.")]
        public bool faceOutward = false;
        
        [Tooltip("If true, orbital objects face the direction of travel.\n" +
                 "Overrides faceOutward.")]
        public bool faceMovementDirection = true;
        
        [Header("Performance")]
        [Tooltip("Maximum distance from camera to render orbital VFX.\n" +
                 "Beyond this distance, orbitals are hidden.")]
        [Range(10f, 200f)]
        public float lodDistance = 50f;
        
        [Tooltip("Maximum agents with orbital VFX active simultaneously.\n" +
                 "0 = unlimited (use with caution for large crowds).")]
        public int maxActiveAgents = 0;
        
        [Header("Audio")]
        [Tooltip("Looping sound for the orbital effect.\n" +
                 "Examples: Humming energy, crackling magic, whooshing sounds.")]
        public AudioClip loopingSound;
        
        [Tooltip("Volume for the orbital sound.")]
        [Range(0f, 1f)]
        public float soundVolume = 0.7f;
        
        [Tooltip("Base pitch for the orbital sound.")]
        [Range(0.5f, 2f)]
        public float soundPitch = 1f;
        
        [Tooltip("Pitch variation (±) for variety between agents.")]
        [Range(0f, 0.3f)]
        public float pitchVariation = 0.05f;
        
        [Tooltip("Spatial blend (0 = 2D, 1 = full 3D).")]
        [Range(0f, 1f)]
        public float spatialBlend = 0.9f;
        
        [Tooltip("Maximum distance to hear orbital sound.")]
        public float maxSoundDistance = 30f;
        
        /// <summary>
        /// Check if the configuration is valid for use.
        /// </summary>
        public bool IsValid => enabled && orbitalPrefab != null && orbitCount > 0;
        
        /// <summary>
        /// Calculate the starting angle offset for each orbital object.
        /// Distributes objects evenly around the orbit.
        /// </summary>
        public float GetAngleOffset(int orbitalIndex)
        {
            return (360f / orbitCount) * orbitalIndex;
        }
        
        /// <summary>
        /// Get the rotation multiplier for direction.
        /// </summary>
        public float GetDirectionMultiplier(int orbitalIndex)
        {
            return direction switch
            {
                OrbitalDirection.Clockwise => 1f,
                OrbitalDirection.CounterClockwise => -1f,
                OrbitalDirection.Alternating => (orbitalIndex % 2 == 0) ? 1f : -1f,
                _ => 1f
            };
        }
        
        /// <summary>
        /// Get the axis vector for the orbital rotation.
        /// </summary>
        public Vector3 GetOrbitAxisVector()
        {
            return orbitAxis switch
            {
                OrbitalAxis.Horizontal => Vector3.up,
                OrbitalAxis.VerticalFront => Vector3.right,
                OrbitalAxis.VerticalSide => Vector3.forward,
                OrbitalAxis.Tilted => Quaternion.Euler(tiltAngle, 0, 0) * Vector3.up,
                _ => Vector3.up
            };
        }
    }
}
