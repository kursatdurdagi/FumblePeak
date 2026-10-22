using UnityEngine;
using Arawn.EnemyMasses.Runtime.Navigation;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Helper for applying move-based knockback without using the NavMesh offset system.
    /// Uses INavigationAgent.Move in the navigation controller update loop.
    /// </summary>
    public static class EnemyMassesAstarKnockbackUtility
    {
        /// <summary>
        /// Queue a move-based knockback impulse if the target backend supports it.
        /// Returns true if handled; false if caller should fall back to navmesh offset impulse.
        /// </summary>
        public static bool TryQueueAstarKnockback(
            CrowdAgent target,
            Vector3 velocity,
            float duration,
            float drag,
            float gravity,
            float pushEffective,
            float liftEffective,
            EnemyMassesCrowdController controller = null)
        {
            if (target == null)
                return false;

            // Displacement-immune agents ignore all knockback/push/pull forces
            if (target.isDisplacementImmune)
                return true; // Return true so callers don't fall back to navmesh offset

            var navAgent = target.navigationAgent;
            if (navAgent == null || !navAgent.Capabilities.SupportsMoveBasedKnockback)
                return false;

            if (controller == null)
                controller = EnemyMassesCrowdController.Instance;

            if (controller != null &&
                controller.DisableAstarFakePhysics &&
                navAgent.NavigationType.RequiresAStar())
            {
                target.hasAstarKnockbackImpulse = false;
                target.astarKnockbackVelocity = Vector3.zero;
                target.astarKnockbackVerticalVelocity = 0f;
                target.astarVisualOffsetY = 0f;
                target.astarKnockbackEndTime = 0f;
                if (target.moveBasedKnockbackForcedStop && navAgent.Enabled)
                    navAgent.IsStopped = target.moveBasedKnockbackWasStoppedBeforeImpulse;
                target.moveBasedKnockbackForcedStop = false;
                target.moveBasedKnockbackWasStoppedBeforeImpulse = false;
                return true;
            }

            // Move-based knockback keeps horizontal velocity in navigation space while vertical lift
            // is handled as a visual offset by the navigation sync layer.
            Vector3 horizontal = Vector3.ProjectOnPlane(velocity, Vector3.up);
            if (horizontal.sqrMagnitude < 0.0001f)
                horizontal = Vector3.zero;
            bool hadMoveBasedImpulse = target.hasAstarKnockbackImpulse;

            target.hasAstarKnockbackImpulse = true;
            target.astarKnockbackVelocity = horizontal;
            target.astarKnockbackEndTime = Time.time + Mathf.Max(0.01f, duration);
            target.astarKnockbackDrag = Mathf.Max(0f, drag);
            target.astarKnockbackVerticalVelocity = velocity.y;
            target.astarKnockbackGravity = Mathf.Max(0f, gravity);
            target.astarVisualOffsetY = Mathf.Max(0f, target.astarVisualOffsetY);

            // Pause normal navigation while impulse is active so steering doesn't cancel knockback.
            if (!hadMoveBasedImpulse)
            {
                target.moveBasedKnockbackWasStoppedBeforeImpulse = navAgent.IsStopped;
                target.moveBasedKnockbackForcedStop = !navAgent.IsStopped;
            }

            if (!navAgent.IsStopped)
                navAgent.IsStopped = true;

            // Ensure we are not stuck in the navmesh offset path.
            target.hasNavmeshOffsetImpulse = false;
            target.navmeshOffset = Vector3.zero;
            target.navmeshOffsetVelocity = Vector3.zero;
            target.navmeshOffsetDuration = 0f;
            target.navmeshOffsetEndTime = 0f;

            // Store impulse values for animation classification (play immediately for A*).
            target.lastNavmeshImpulsePush = pushEffective;
            target.lastNavmeshImpulseLift = liftEffective;
            target.navmeshImpulseAnimationPlayed = true;
            target.liftImpulseActive = false;

            EnemyMassesImpulseAnimationHelper.PlayImpulseAnimation(target, pushEffective, liftEffective);

            controller?.MarkActiveAgentsListDirty();
            return true;
        }
    }
}
