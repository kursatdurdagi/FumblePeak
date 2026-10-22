using UnityEngine;
using UnityEngine.AI;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Contains backend-specific optional hooks so core controllers can stay
    /// focused on INavigationAgent abstractions.
    /// </summary>
    internal static class EnemyMassesNavigationBackendHooks
    {
        /// <summary>
        /// Applies optional spawn-time defaults for backends that support
        /// per-agent avoidance tuning.
        /// </summary>
        public static void ApplySpawnAvoidanceDefaults(INavigationAgent navigationAgent)
        {
            if (navigationAgent == null || !navigationAgent.Capabilities.SupportsPerAgentAvoidanceTuning)
                return;

            var navMeshAgent = navigationAgent.GetUnderlyingAgent<NavMeshAgent>();
            if (navMeshAgent == null)
                return;

            navMeshAgent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            navMeshAgent.avoidancePriority = 50;
        }

        /// <summary>
        /// Gets per-agent avoidance tuning values from a backend that supports them.
        /// </summary>
        public static bool TryGetPerAgentAvoidanceTuning(
            INavigationAgent navigationAgent,
            out float avoidancePriority,
            out float radius)
        {
            avoidancePriority = 0f;
            radius = 0f;

            if (navigationAgent == null || !navigationAgent.Capabilities.SupportsPerAgentAvoidanceTuning)
                return false;

            var navMeshAgent = navigationAgent.GetUnderlyingAgent<NavMeshAgent>();
            if (navMeshAgent == null)
                return false;

            avoidancePriority = navMeshAgent.avoidancePriority;
            radius = navMeshAgent.radius;
            return true;
        }

        /// <summary>
        /// Applies per-agent avoidance tuning values to a backend that supports them.
        /// </summary>
        public static bool ApplyPerAgentAvoidanceTuning(
            INavigationAgent navigationAgent,
            float avoidancePriority,
            float radius)
        {
            if (navigationAgent == null || !navigationAgent.Capabilities.SupportsPerAgentAvoidanceTuning)
                return false;

            var navMeshAgent = navigationAgent.GetUnderlyingAgent<NavMeshAgent>();
            if (navMeshAgent == null)
                return false;

            navMeshAgent.avoidancePriority = Mathf.Clamp(Mathf.RoundToInt(avoidancePriority), 0, 99);
            navMeshAgent.radius = Mathf.Max(0.01f, radius);
            return true;
        }

        /// <summary>
        /// Applies backend-specific death navigation state changes.
        /// Returns true when the backend was recognized and handled.
        /// </summary>
        public static bool ApplyDeathNavigationState(INavigationAgent navigationAgent, bool disableNavigation)
        {
            if (navigationAgent == null)
                return false;

            var navMeshAgent = navigationAgent.GetUnderlyingAgent<NavMeshAgent>();
            if (navMeshAgent == null)
                return false;

            if (navMeshAgent.isOnNavMesh)
                navMeshAgent.isStopped = true;

            if (disableNavigation)
            {
                if (navMeshAgent.enabled)
                    navMeshAgent.enabled = false;
            }
            else
            {
                if (!navMeshAgent.enabled)
                    navMeshAgent.enabled = true;

                if (navMeshAgent.isOnNavMesh)
                    navMeshAgent.isStopped = true;
            }

            return true;
        }

        /// <summary>
        /// Samples a nearby NavMesh position for optional NavMesh-specific fallbacks.
        /// </summary>
        public static bool TrySampleNavMeshPosition(Vector3 position, float maxDistance, out Vector3 sampledPosition)
        {
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, maxDistance, NavMesh.AllAreas))
            {
                sampledPosition = hit.position;
                return true;
            }

            sampledPosition = position;
            return false;
        }
    }
}
