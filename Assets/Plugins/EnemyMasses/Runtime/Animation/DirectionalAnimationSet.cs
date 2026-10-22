using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Container for directional animation clips based on attack direction.
    /// Supports front, back, left, and right directional variants.
    /// Auto-enables directional system when clips are assigned.
    /// </summary>
    [System.Serializable]
    public class DirectionalAnimationSet
    {
        [Header("Forward Animations")]
        [Tooltip("Animation clips for attacks from the front (0-45° from forward)")]
        public List<AnimationClip> frontClips = new List<AnimationClip>();

        [Header("Back Animations")]
        [Tooltip("Animation clips for attacks from behind (135-180° from forward)")]
        public List<AnimationClip> backClips = new List<AnimationClip>();

        [Header("Left Side Animations")]
        [Tooltip("Animation clips for attacks from the left side (45-135° from forward, left side)")]
        public List<AnimationClip> leftClips = new List<AnimationClip>();

        [Header("Right Side Animations")]
        [Tooltip("Animation clips for attacks from the right side (45-135° from forward, right side)")]
        public List<AnimationClip> rightClips = new List<AnimationClip>();

        [Header("Settings")]
        [Tooltip("Angle threshold for front hits (in degrees from forward vector). Default: 45°")]
        [Range(0f, 90f)]
        public float frontAngleThreshold = 45f;

        [Tooltip("Angle threshold for back hits (in degrees from backward vector). Default: 45°")]
        [Range(0f, 90f)]
        public float backAngleThreshold = 45f;

        /// <summary>
        /// Checks if directional animations are enabled (any clips assigned).
        /// </summary>
        public bool IsEnabled
        {
            get
            {
                return HasBackClips || HasSideClips;
            }
        }

        /// <summary>
        /// Checks if back directional animations are available.
        /// </summary>
        public bool HasBackClips
        {
            get { return backClips != null && backClips.Count > 0 && backClips[0] != null; }
        }

        /// <summary>
        /// Checks if side directional animations are available.
        /// </summary>
        public bool HasSideClips
        {
            get
            {
                bool hasLeft = leftClips != null && leftClips.Count > 0 && leftClips[0] != null;
                bool hasRight = rightClips != null && rightClips.Count > 0 && rightClips[0] != null;
                return hasLeft || hasRight;
            }
        }

        /// <summary>
        /// Checks if front directional animations are available (used as fallback).
        /// </summary>
        public bool HasFrontClips
        {
            get { return frontClips != null && frontClips.Count > 0 && frontClips[0] != null; }
        }

        /// <summary>
        /// Gets a random animation clip based on the direction from victim to attacker.
        /// </summary>
        /// <param name="victimForward">Victim's forward direction</param>
        /// <param name="attackerPosition">Attacker's world position</param>
        /// <param name="victimPosition">Victim's world position</param>
        /// <param name="lastClipIndex">Index of last played clip (to avoid repeats)</param>
        /// <returns>Selected animation clip and its index, or null if no appropriate clip found</returns>
        public (AnimationClip clip, int index) GetClipForDirection(
            Vector3 victimForward,
            Vector3 attackerPosition,
            Vector3 victimPosition,
            int lastClipIndex = -1)
        {
            // Calculate direction from victim to attacker
            Vector3 directionToAttacker = (attackerPosition - victimPosition).normalized;

            // Calculate angle from victim's forward to attacker
            float angle = Vector3.Angle(victimForward, directionToAttacker);

            // Determine if attack is from left or right
            Vector3 cross = Vector3.Cross(victimForward, directionToAttacker);
            bool isFromLeft = cross.y > 0;

            // Select appropriate clip list based on direction
            List<AnimationClip> selectedClips = null;

            // Check back hits first (priority if available)
            if (angle >= (180f - backAngleThreshold) && HasBackClips)
            {
                selectedClips = backClips;
            }
            // Check front hits
            else if (angle <= frontAngleThreshold && HasFrontClips)
            {
                selectedClips = frontClips;
            }
            // Check side hits
            else if (HasSideClips)
            {
                if (isFromLeft && leftClips != null && leftClips.Count > 0)
                    selectedClips = leftClips;
                else if (!isFromLeft && rightClips != null && rightClips.Count > 0)
                    selectedClips = rightClips;
            }

            // Fallback to front clips if no other direction matches
            if (selectedClips == null || selectedClips.Count == 0)
            {
                if (HasFrontClips)
                    selectedClips = frontClips;
                else
                    return (null, -1);
            }

            // Remove null clips
            selectedClips = selectedClips.FindAll(clip => clip != null);
            if (selectedClips.Count == 0)
                return (null, -1);

            // Select random clip, avoiding last played if possible
            int clipIndex;
            if (selectedClips.Count == 1)
            {
                clipIndex = 0;
            }
            else
            {
                // Try to avoid repeating the same clip
                clipIndex = Random.Range(0, selectedClips.Count);
                if (clipIndex == lastClipIndex && selectedClips.Count > 1)
                {
                    clipIndex = (clipIndex + 1) % selectedClips.Count;
                }
            }

            return (selectedClips[clipIndex], clipIndex);
        }

        /// <summary>
        /// Gets a random clip from front clips (used when direction is unknown or for non-directional attacks).
        /// </summary>
        public (AnimationClip clip, int index) GetRandomFrontClip(int lastClipIndex = -1)
        {
            if (!HasFrontClips)
                return (null, -1);

            var validClips = frontClips.FindAll(clip => clip != null);
            if (validClips.Count == 0)
                return (null, -1);

            int clipIndex = Random.Range(0, validClips.Count);
            if (clipIndex == lastClipIndex && validClips.Count > 1)
            {
                clipIndex = (clipIndex + 1) % validClips.Count;
            }

            return (validClips[clipIndex], clipIndex);
        }

        /// <summary>
        /// Debug visualization of the direction thresholds.
        /// </summary>
        public void DrawDirectionGizmos(Vector3 position, Vector3 forward, float radius = 2f)
        {
#if UNITY_EDITOR
            // Front cone
            if (HasFrontClips)
            {
                UnityEngine.Gizmos.color = Color.green;
                DrawDirectionCone(position, forward, frontAngleThreshold, radius);
            }

            // Back cone
            if (HasBackClips)
            {
                UnityEngine.Gizmos.color = Color.red;
                DrawDirectionCone(position, -forward, backAngleThreshold, radius);
            }

            // Left side
            if (leftClips != null && leftClips.Count > 0)
            {
                UnityEngine.Gizmos.color = Color.blue;
                Vector3 left = Quaternion.Euler(0, -90, 0) * forward;
                DrawDirectionCone(position, left, 45f, radius);
            }

            // Right side
            if (rightClips != null && rightClips.Count > 0)
            {
                UnityEngine.Gizmos.color = Color.yellow;
                Vector3 right = Quaternion.Euler(0, 90, 0) * forward;
                DrawDirectionCone(position, right, 45f, radius);
            }
#endif
        }

        private void DrawDirectionCone(Vector3 position, Vector3 direction, float angle, float radius)
        {
#if UNITY_EDITOR
            Vector3 rightBound = Quaternion.Euler(0, angle, 0) * direction;
            Vector3 leftBound = Quaternion.Euler(0, -angle, 0) * direction;

            UnityEngine.Gizmos.DrawRay(position, direction * radius);
            UnityEngine.Gizmos.DrawRay(position, rightBound * radius);
            UnityEngine.Gizmos.DrawRay(position, leftBound * radius);
#endif
        }
    }
}
