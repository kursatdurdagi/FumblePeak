using System;
using System.Collections.Generic;
using UnityEngine;
using GPUInstancerPro.CrowdAnimations;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Captures periodic runtime snapshots for regression validation.
    /// Add this to the same GameObject as EnemyMassesCrowdController (or assign one manually).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(500)]
    public class EnemyMassesRegressionProbe : MonoBehaviour
    {
        [SerializeField] private EnemyMassesCrowdController crowdController;
        [SerializeField] private bool autoFindController = true;
        [SerializeField] private bool logSnapshots = true;
        [SerializeField] private float snapshotInterval = 1f;

        private readonly Dictionary<int, CrowdCombatState> _lastCombatStates = new();
        private readonly Dictionary<int, AgentNavigationState> _lastNavigationStates = new();

        private float _nextSnapshotTime;
        private int _lastKnownAgentCount;
        private int _spawnEstimateSinceStart;
        private int _deathEventsSinceStart;
        private int _combatStateTransitionsSinceStart;
        private int _navigationStateTransitionsSinceStart;
        private int _manualCommandTransitionsSinceStart;
        private EnemyMassesRegressionSnapshot _latestSnapshot;

        /// <summary>
        /// Fired whenever a new snapshot is captured.
        /// </summary>
        public event Action<EnemyMassesRegressionSnapshot> SnapshotCaptured;

        /// <summary>
        /// Most recent captured snapshot.
        /// </summary>
        public EnemyMassesRegressionSnapshot LatestSnapshot => _latestSnapshot;

        private void OnEnable()
        {
            EnsureControllerReference();
            SubscribeEvents();
            PrimeStateTracking();
            _nextSnapshotTime = Time.time + Mathf.Max(0.05f, snapshotInterval);
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
            _lastCombatStates.Clear();
            _lastNavigationStates.Clear();
        }

        private void LateUpdate()
        {
            EnsureControllerReference();
            if (crowdController == null)
                return;

            float interval = Mathf.Max(0.05f, snapshotInterval);
            if (Time.time < _nextSnapshotTime)
                return;

            CaptureNow();
            _nextSnapshotTime = Time.time + interval;
        }

        /// <summary>
        /// Captures a snapshot immediately.
        /// </summary>
        public EnemyMassesRegressionSnapshot CaptureNow()
        {
            EnsureControllerReference();
            var snapshot = BuildSnapshot();
            _latestSnapshot = snapshot;
            SnapshotCaptured?.Invoke(snapshot);

            if (logSnapshots)
                Debug.Log(snapshot.ToString(), this);

            return snapshot;
        }

        private EnemyMassesRegressionSnapshot BuildSnapshot()
        {
            var snapshot = new EnemyMassesRegressionSnapshot
            {
                frame = Time.frameCount,
                time = Time.time
            };

            if (crowdController == null)
                return snapshot;

            List<CrowdAgent> agents = crowdController.GetAgents();
            if (agents == null)
                return snapshot;

            int tracked = 0;
            int alive = 0;
            int dead = 0;
            int pendingCleanup = 0;
            int moving = 0;
            int chasing = 0;
            int attacking = 0;
            int blocking = 0;
            int manual = 0;
            int arrived = 0;
            int retries = 0;
            int pathPending = 0;

            int currentAgentCount = agents.Count;
            int spawnDeltaSinceLastSnapshot = Mathf.Max(0, currentAgentCount - _lastKnownAgentCount);
            if (spawnDeltaSinceLastSnapshot > 0)
                _spawnEstimateSinceStart += spawnDeltaSinceLastSnapshot;
            _lastKnownAgentCount = currentAgentCount;

            for (int i = 0; i < agents.Count; i++)
            {
                CrowdAgent agent = agents[i];
                if (agent == null)
                    continue;

                tracked++;

                if (agent.isDead)
                    dead++;
                else
                    alive++;

                if (agent.pendingCleanup)
                    pendingCleanup++;

                if (!agent.isDead && agent.CurrentVelocity.sqrMagnitude > 0.01f)
                    moving++;

                if (agent.navigationState == AgentNavigationState.ChasingTarget)
                    chasing++;

                if (agent.combatState == CrowdCombatState.Attacking)
                    attacking++;

                if (agent.combatState == CrowdCombatState.Blocking)
                    blocking++;

                if (agent.hasManualCommand)
                    manual++;

                if (agent.hasArrived)
                    arrived++;

                if (agent.destinationRetries > 0)
                    retries++;

                if (agent.pathPendingStartTime > 0f)
                    pathPending++;

                TrackTransitions(agent, i);
            }

            snapshot.trackedAgents = tracked;
            snapshot.aliveAgents = alive;
            snapshot.deadAgents = dead;
            snapshot.pendingCleanupAgents = pendingCleanup;
            snapshot.movingAgents = moving;
            snapshot.chasingAgents = chasing;
            snapshot.attackingAgents = attacking;
            snapshot.blockingAgents = blocking;
            snapshot.manualCommandAgents = manual;
            snapshot.arrivedAgents = arrived;
            snapshot.destinationRetryingAgents = retries;
            snapshot.pathPendingAgents = pathPending;
            snapshot.spawnDeltaSinceLastSnapshot = spawnDeltaSinceLastSnapshot;
            snapshot.spawnEstimateSinceStart = _spawnEstimateSinceStart;
            snapshot.deathEventsSinceStart = _deathEventsSinceStart;
            snapshot.combatStateTransitionsSinceStart = _combatStateTransitionsSinceStart;
            snapshot.navigationStateTransitionsSinceStart = _navigationStateTransitionsSinceStart;
            snapshot.manualCommandTransitionsSinceStart = _manualCommandTransitionsSinceStart;

            return snapshot;
        }

        private void TrackTransitions(CrowdAgent agent, int fallbackKey)
        {
            int key = agent.index >= 0 ? agent.index : fallbackKey;

            if (_lastCombatStates.TryGetValue(key, out CrowdCombatState previousCombatState))
            {
                if (previousCombatState != agent.combatState)
                    _combatStateTransitionsSinceStart++;
            }
            _lastCombatStates[key] = agent.combatState;

            if (_lastNavigationStates.TryGetValue(key, out AgentNavigationState previousNavigationState))
            {
                if (previousNavigationState != agent.navigationState)
                {
                    _navigationStateTransitionsSinceStart++;
                    if (agent.navigationState == AgentNavigationState.ManualCommand)
                        _manualCommandTransitionsSinceStart++;
                }
            }
            _lastNavigationStates[key] = agent.navigationState;
        }

        private void EnsureControllerReference()
        {
            if (crowdController != null)
                return;

            if (!autoFindController)
                return;

            crowdController = GetComponent<EnemyMassesCrowdController>();
            if (crowdController == null)
                crowdController = EnemyMassesCrowdController.Instance;
        }

        private void PrimeStateTracking()
        {
            _lastCombatStates.Clear();
            _lastNavigationStates.Clear();

            if (crowdController == null)
                return;

            List<CrowdAgent> agents = crowdController.GetAgents();
            _lastKnownAgentCount = agents != null ? agents.Count : 0;
        }

        private void SubscribeEvents()
        {
            if (crowdController == null)
                return;

            crowdController.EventAgentKilled += OnAgentKilled;
        }

        private void UnsubscribeEvents()
        {
            if (crowdController == null)
                return;

            crowdController.EventAgentKilled -= OnAgentKilled;
        }

        private void OnAgentKilled(GPUICrowdInstance _)
        {
            _deathEventsSinceStart++;
        }
    }
}
