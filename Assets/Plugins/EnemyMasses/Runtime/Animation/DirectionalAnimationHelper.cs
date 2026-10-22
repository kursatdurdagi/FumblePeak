using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Helper class for selecting and managing directional combat animations.
    /// Handles direction detection and animation clip selection based on attacker position.
    /// </summary>
    public static class DirectionalAnimationHelper
    {
        /// <summary>
        /// Gets the appropriate animation clip based on attacker direction relative to victim.
        /// Automatically falls back to non-directional clips if directional system is disabled or no clips available.
        /// </summary>
        /// <param name="animationType">Type of animation (Hit, Death, Block, Attack)</param>
        /// <param name="settings">Global animation settings</param>
        /// <param name="enemyCrowd">Faction-specific settings (can be null)</param>
        /// <param name="victim">The agent receiving the action</param>
        /// <param name="attackerPosition">Position of the attacker (for direction calculation)</param>
        /// <param name="fallbackClips">Non-directional clips to use as fallback</param>
        /// <param name="lastClipIndex">Index of last played clip (to avoid repeats)</param>
        /// <returns>Selected animation clip and its index</returns>
        public static (AnimationClip clip, int index) GetDirectionalClip(
            DirectionalAnimationType animationType,
            AnimationSettings settings,
            EnemyCrowd enemyCrowd,
            CrowdAgent victim,
            Vector3 attackerPosition,
            AnimationClip[] fallbackClips = null,
            int lastClipIndex = -1)
        {
            // Check if directional animations are enabled
            DirectionalAnimationSet directionalSet = GetDirectionalSet(animationType, settings, enemyCrowd);
            
            if (directionalSet != null && directionalSet.IsEnabled && victim != null)
            {
                // Get victim's forward direction
                Vector3 victimForward = GetVictimForward(victim);
                Vector3 victimPosition = GetVictimPosition(victim);

                // Get directional clip
                var result = directionalSet.GetClipForDirection(
                    victimForward,
                    attackerPosition,
                    victimPosition,
                    lastClipIndex
                );

                if (result.clip != null)
                    return result;
            }

            // Fallback to non-directional clips
            if (fallbackClips != null && fallbackClips.Length > 0)
            {
                var validClips = new List<AnimationClip>();
                foreach (var clip in fallbackClips)
                {
                    if (clip != null)
                        validClips.Add(clip);
                }

                if (validClips.Count > 0)
                {
                    int clipIndex = Random.Range(0, validClips.Count);
                    if (clipIndex == lastClipIndex && validClips.Count > 1)
                    {
                        clipIndex = (clipIndex + 1) % validClips.Count;
                    }
                    return (validClips[clipIndex], clipIndex);
                }
            }

            return (null, -1);
        }

        /// <summary>
        /// Gets the directional animation set for the given type, considering faction overrides.
        /// </summary>
        private static DirectionalAnimationSet GetDirectionalSet(
            DirectionalAnimationType animationType,
            AnimationSettings settings,
            EnemyCrowd enemyCrowd)
        {
            // Check for faction-specific override
            if (enemyCrowd != null)
            {
                switch (animationType)
                {
                    case DirectionalAnimationType.Hit:
                        if (enemyCrowd.overrideDirectionalHitAnimations && enemyCrowd.hitAnimationsOverride.IsEnabled)
                            return enemyCrowd.hitAnimationsOverride;
                        break;
                    case DirectionalAnimationType.Death:
                        if (enemyCrowd.overrideDirectionalDeathAnimations && enemyCrowd.deathAnimationsOverride.IsEnabled)
                            return enemyCrowd.deathAnimationsOverride;
                        break;
                    case DirectionalAnimationType.Block:
                        if (enemyCrowd.overrideDirectionalBlockAnimations && enemyCrowd.blockAnimationsOverride.IsEnabled)
                            return enemyCrowd.blockAnimationsOverride;
                        break;
                    case DirectionalAnimationType.Attack:
                        if (enemyCrowd.overrideDirectionalAttackAnimations && enemyCrowd.attackAnimationsOverride.IsEnabled)
                            return enemyCrowd.attackAnimationsOverride;
                        break;
                }
            }

            // Use global settings
            if (settings.HasDirectionalAnimations(animationType))
            {
                return settings.GetDirectionalAnimationSet(animationType);
            }

            return null;
        }

        /// <summary>
        /// Gets the victim agent's forward direction.
        /// </summary>
        private static Vector3 GetVictimForward(CrowdAgent victim)
        {
            return victim.transform?.forward ?? Vector3.forward;
        }

        /// <summary>
        /// Gets the victim agent's position.
        /// </summary>
        private static Vector3 GetVictimPosition(CrowdAgent victim)
        {
            return victim.WorldPosition;
        }

        /// <summary>
        /// Calculates the angle from victim's forward to attacker.
        /// Useful for UI display or debugging.
        /// </summary>
        public static float GetAttackAngle(CrowdAgent victim, Vector3 attackerPosition)
        {
            Vector3 victimForward = GetVictimForward(victim);
            Vector3 victimPosition = GetVictimPosition(victim);
            Vector3 directionToAttacker = (attackerPosition - victimPosition).normalized;
            
            return Vector3.Angle(victimForward, directionToAttacker);
        }

        /// <summary>
        /// Gets a human-readable direction name for debugging.
        /// </summary>
        public static string GetDirectionName(CrowdAgent victim, Vector3 attackerPosition)
        {
            Vector3 victimForward = GetVictimForward(victim);
            Vector3 victimPosition = GetVictimPosition(victim);
            Vector3 directionToAttacker = (attackerPosition - victimPosition).normalized;
            
            float angle = Vector3.Angle(victimForward, directionToAttacker);
            Vector3 cross = Vector3.Cross(victimForward, directionToAttacker);
            bool isFromLeft = cross.y > 0;

            if (angle >= 135f)
                return "Back";
            else if (angle <= 45f)
                return "Front";
            else if (isFromLeft)
                return "Left";
            else
                return "Right";
        }
    }
}
