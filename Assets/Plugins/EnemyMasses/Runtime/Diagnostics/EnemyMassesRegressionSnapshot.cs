using System;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Snapshot payload used by the runtime regression probe.
    /// Captures a compact view of crowd behavior so runs can be compared over time.
    /// </summary>
    [Serializable]
    public struct EnemyMassesRegressionSnapshot
    {
        public int frame;
        public float time;

        public int trackedAgents;
        public int aliveAgents;
        public int deadAgents;
        public int pendingCleanupAgents;

        public int movingAgents;
        public int chasingAgents;
        public int attackingAgents;
        public int blockingAgents;
        public int manualCommandAgents;
        public int arrivedAgents;

        public int destinationRetryingAgents;
        public int pathPendingAgents;

        public int spawnDeltaSinceLastSnapshot;
        public int spawnEstimateSinceStart;
        public int deathEventsSinceStart;

        public int combatStateTransitionsSinceStart;
        public int navigationStateTransitionsSinceStart;
        public int manualCommandTransitionsSinceStart;

        public override string ToString()
        {
            return $"[RegressionSnapshot] frame={frame} time={time:F2} tracked={trackedAgents} " +
                   $"alive={aliveAgents} dead={deadAgents} pendingCleanup={pendingCleanupAgents} " +
                   $"moving={movingAgents} chasing={chasingAgents} attacking={attackingAgents} " +
                   $"blocking={blockingAgents} manual={manualCommandAgents} arrived={arrivedAgents} " +
                   $"retries={destinationRetryingAgents} pathPending={pathPendingAgents} " +
                   $"spawnDelta={spawnDeltaSinceLastSnapshot} spawnEstimateTotal={spawnEstimateSinceStart} " +
                   $"deathEvents={deathEventsSinceStart} combatTransitions={combatStateTransitionsSinceStart} " +
                   $"navTransitions={navigationStateTransitionsSinceStart} manualTransitions={manualCommandTransitionsSinceStart}";
        }
    }
}
