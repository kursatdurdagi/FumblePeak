using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// AI difficulty level that affects tactical decision making.
    /// </summary>
    public enum RTSAIDifficulty
    {
        Beginner = 0,
        Easy = 1,
        Normal = 2,
        Hard = 3,
        Expert = 4,
        Nightmare = 5
    }

    /// <summary>
    /// Current state of the RTS AI.
    /// </summary>
    public enum RTSAIState
    {
        Idle,
        Exploring,
        Engaging,
        Retreating,
        Regrouping,
        SettingTrap,
        Baiting,
        Flanking
    }

    /// <summary>
    /// An efficient RTS AI controller that manages non-player controlled agents.
    /// Supports exploration, combat tactics, baiting, trapping, and dynamic difficulty.
    /// </summary>
    [AddComponentMenu("Enemy Masses/RTS AI Controller")]
    public class EnemyMassesRTSAI : MonoBehaviour
    {
        #region References

        [Header("References")]
        [Tooltip("The crowd controller to command. If null, will search for one in the scene.")]
        public EnemyMassesCrowdController crowdController;

        [Tooltip("Reference to the player's RTS controller to identify enemy factions.")]
        public EnemyMassesRTSController playerRTSController;

        #endregion

        #region Faction Control

        [Header("Faction Control")]
        [Tooltip("Factions that this AI is allowed to control.")]
        public FactionMask controlledFactions;

        [Tooltip("Enemy factions to attack (player-controlled factions).")]
        public FactionMask enemyFactions;

        [Tooltip("Auto-detect enemy factions from the player's RTS controller.")]
        public bool autoDetectEnemyFactions = true;

        #endregion

        #region Difficulty Settings

        [Header("Difficulty")]
        [Tooltip("AI difficulty level affects tactical decisions and reaction time.")]
        [Range(0, 5)]
        public int difficulty = 2;

        [Tooltip("How often the AI makes decisions (seconds). Lower = more responsive but more CPU.")]
        [Range(0.1f, 2f)]
        public float decisionInterval = 0.5f;

        [Tooltip("Random variance in decision timing to make AI less predictable.")]
        [Range(0f, 0.5f)]
        public float decisionVariance = 0.1f;

        #endregion

        #region Exploration Settings

        [Header("Exploration")]
        [Tooltip("Enable map exploration when not in combat.")]
        public bool enableExploration = true;

        [Tooltip("Radius around current position to explore.")]
        [Range(10f, 200f)]
        public float explorationRadius = 50f;

        [Tooltip("How long to explore an area before moving on (seconds).")]
        [Range(5f, 60f)]
        public float explorationDuration = 15f;

        [Tooltip("Minimum distance to travel for a new exploration target.")]
        [Range(5f, 50f)]
        public float minExplorationDistance = 10f;

        #endregion

        #region Combat Tactics

        [Header("Combat Tactics")]
        [Tooltip("Enable advanced tactical behaviors (baiting, trapping, flanking).")]
        public bool enableAdvancedTactics = true;

        [Tooltip("Minimum agents required to attempt baiting tactics.")]
        [Range(5, 100)]
        public int minAgentsForBaiting = 20;

        [Tooltip("Percentage of forces to use as bait (0.05 = 5%).")]
        [Range(0.01f, 0.2f)]
        public float baitPercentage = 0.05f;

        [Tooltip("Distance bait units should retreat to lure enemies.")]
        [Range(10f, 100f)]
        public float baitRetreatDistance = 30f;

        [Tooltip("Engage enemies when they're within this range.")]
        [Range(10f, 100f)]
        public float engagementRange = 40f;

        [Tooltip("Retreat when outnumbered by this ratio (e.g., 2.0 = retreat if enemies are 2x our numbers).")]
        [Range(1.5f, 5f)]
        public float retreatRatio = 2.5f;

        [Tooltip("Regroup scattered forces when spread beyond this distance.")]
        [Range(20f, 100f)]
        public float regroupDistance = 40f;

        #endregion

        #region Group Splitting (Multi-Directional Attacks)

        [Header("Group Splitting")]
        [Tooltip("Enable splitting large forces into multiple tactical groups for flanking attacks.")]
        public bool enableGroupSplitting = true;

        [Tooltip("Maximum agents in a single group before splitting occurs.")]
        [Range(20, 200)]
        public int maxGroupSize = 50;

        [Tooltip("Minimum agents required to form a new group when splitting.")]
        [Range(5, 50)]
        public int minGroupSize = 10;

        [Tooltip("Maximum number of tactical groups the AI can manage.")]
        [Range(2, 6)]
        public int maxGroups = 4;

        [Tooltip("Distance offset for flanking maneuvers from the main attack direction.")]
        [Range(10f, 50f)]
        public float flankingDistance = 25f;

        [Tooltip("Angle in degrees for flanking groups (90 = perpendicular, 135 = rear flank).")]
        [Range(45f, 150f)]
        public float flankingAngle = 90f;

        [Tooltip("Delay between main force engagement and flank attacks (seconds).")]
        [Range(0f, 5f)]
        public float flankingDelay = 2f;

        [Tooltip("Preferred attack distribution: Front / Flanks / Rear (should sum to 1.0).")]
        public Vector3 attackDistribution = new Vector3(0.4f, 0.4f, 0.2f);

        #endregion

        #region Formation Spacing (AoE Avoidance)

        [Header("Formation Spacing (AoE Avoidance)")]
        [Tooltip("Enable dynamic formation spacing to avoid AoE damage.")]
        public bool enableAoEAvoidance = true;

        [Tooltip("Normal formation spacing when no AoE threat detected.")]
        [Range(0.5f, 3f)]
        public float normalSpacing = 1.5f;

        [Tooltip("Spread out spacing when AoE threat detected.")]
        [Range(2f, 6f)]
        public float spreadSpacing = 4f;

        [Tooltip("Maximum spacing to use against heavy AoE threats.")]
        [Range(4f, 10f)]
        public float maxSpreadSpacing = 6f;

        [Tooltip("How quickly spacing adjusts to threats (seconds to full adjustment).")]
        [Range(0.5f, 5f)]
        public float spacingAdjustmentSpeed = 2f;

        [Tooltip("Auto-detect AoE threats from enemy EnemyCrowd settings.")]
        public bool autoDetectAoEThreats = true;

        [Tooltip("Manual AoE threat level override (0 = none, 1 = max). Used when autoDetect is disabled.")]
        [Range(0f, 1f)]
        public float manualAoEThreatLevel = 0f;

        [Tooltip("Minimum damage radius to consider as AoE threat.")]
        [Range(1f, 10f)]
        public float aoEThresholdRadius = 2f;

        #endregion

        #region Spawning

        [Header("Spawning (Optional)")]
        [Tooltip("Enable AI-controlled spawning of reinforcements.")]
        public bool enableSpawning = false;

        [Tooltip("Maximum total agents the AI can have at once.")]
        [Range(10, 1000)]
        public int maxAgentCount = 200;

        [Tooltip("Agents to spawn per wave.")]
        [Range(1, 100)]
        public int spawnWaveSize = 10;

        [Tooltip("Minimum seconds between spawn waves.")]
        [Range(5f, 300f)]
        public float spawnInterval = 30f;

        [Tooltip("Formation type for spawned agents.")]
        public FormationType spawnFormation = FormationType.Circle;

        [Tooltip("Spawn location for reinforcements. If null, spawns near existing forces.")]
        public Transform spawnLocation;

        #endregion

        #region Visual Feedback

        [Header("Visual Feedback")]
        [Tooltip("Show AI state gizmos in editor.")]
        public bool showGizmos = true;

        [Tooltip("Color for AI controlled agents visualization.")]
        public Color aiColor = new Color(1f, 0.4f, 0.1f, 0.6f);

        #endregion

        #region Debug

        [Header("Debug")]
        [Tooltip("Enable debug logging for AI decisions.")]
        public bool debugLogs = false;

        #endregion

        // Runtime state
        private RTSAIState _currentState = RTSAIState.Idle;
        private float _nextDecisionTime;
        private float _nextSpawnTime;
        private float _stateStartTime;
        private Vector3 _currentObjective;
        private Vector3 _explorationCenter;
        private float _explorationStartTime;

        // Agent tracking
        private List<int> _controlledAgentIndices = new List<int>();
        private List<int> _baitAgentIndices = new List<int>();
        private List<int> _mainForceIndices = new List<int>();
        private List<int> _enemyAgentIndices = new List<int>();

        // Tactical group tracking
        private List<TacticalGroup> _tacticalGroups = new List<TacticalGroup>();
        private float _lastGroupUpdateTime;
        private float _flankingStartTime;
        private bool _flanksEngaged;

        // Tactical state
        private Vector3 _trapPosition;
        private Vector3 _baitTargetPosition;
        private int _consecutiveIdleFrames;

        // AoE avoidance state
        private float _currentAoEThreatLevel;
        private float _targetAoEThreatLevel;
        private float _currentFormationSpacing;
        private float _lastSpacingUpdateTime;
        private float _aoEThreatCacheTime;

        // Cached references
        private List<CrowdAgent> _agentsCache;
        private HashSet<string> _controlledFactionNames = new HashSet<string>();
        private HashSet<string> _enemyFactionNames = new HashSet<string>();

        // Performance optimization
        private const int MaxAgentsPerDecision = 100;
        private int _decisionBatchIndex;

        #region Properties

        public RTSAIState CurrentState => _currentState;
        public int ControlledAgentCount => _controlledAgentIndices.Count;
        public int EnemyAgentCount => _enemyAgentIndices.Count;
        public RTSAIDifficulty DifficultyLevel => (RTSAIDifficulty)Mathf.Clamp(difficulty, 0, 5);
        public float CurrentAoEThreatLevel => _currentAoEThreatLevel;
        public float CurrentFormationSpacing => _currentFormationSpacing;
        public int TacticalGroupCount => _tacticalGroups.Count;
        public IReadOnlyList<TacticalGroup> TacticalGroups => _tacticalGroups;

        #endregion

        #region Tactical Group Class

        /// <summary>
        /// Represents a tactical group of agents that can operate semi-independently.
        /// </summary>
        [System.Serializable]
        public class TacticalGroup
        {
            public int groupId;
            public TacticalRole role;
            public List<int> agentIndices = new List<int>();
            public Vector3 targetPosition;
            public Vector3 lastKnownCenter;
            public CombatStance currentStance = CombatStance.Offensive;
            public bool isEngaged;
            public float engagementTime;

            public int Count => agentIndices.Count;

            public TacticalGroup(int id, TacticalRole role)
            {
                this.groupId = id;
                this.role = role;
            }

            public void Clear()
            {
                agentIndices.Clear();
                isEngaged = false;
            }
        }

        /// <summary>
        /// Role assigned to a tactical group.
        /// </summary>
        public enum TacticalRole
        {
            MainForce,      // Primary assault force
            LeftFlank,      // Attacks from the left side
            RightFlank,     // Attacks from the right side
            RearGuard,      // Attacks from behind
            Reserve,        // Held back for reinforcement
            Bait            // Used for baiting tactics
        }

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            Initialize();
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            if (crowdController == null)
                return;

            if (Time.time >= _nextDecisionTime)
            {
                MakeDecision();
                _nextDecisionTime = Time.time + decisionInterval + UnityEngine.Random.Range(-decisionVariance, decisionVariance);
            }

            if (enableSpawning && Time.time >= _nextSpawnTime)
            {
                TrySpawnReinforcements();
                _nextSpawnTime = Time.time + spawnInterval;
            }

            // Update formation spacing for AoE avoidance
            if (enableAoEAvoidance)
            {
                UpdateFormationSpacing();
            }
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                _nextDecisionTime = Time.time + UnityEngine.Random.Range(0f, decisionInterval);
                _nextSpawnTime = Time.time + spawnInterval;
            }
        }

        #endregion

        #region Initialization

        private void Initialize()
        {
            if (crowdController == null)
            {
                crowdController = FindFirstObjectByType<EnemyMassesCrowdController>();
            }

            if (playerRTSController == null)
            {
                playerRTSController = FindFirstObjectByType<EnemyMassesRTSController>();
            }

            UpdateFactionSets();
            
            // Initialize formation spacing
            _currentFormationSpacing = normalSpacing;
            _currentAoEThreatLevel = 0f;
            _targetAoEThreatLevel = 0f;

            if (debugLogs)
            {
                Debug.Log($"[RTS AI] Initialized. Controlling factions: {string.Join(", ", _controlledFactionNames)}");
            }
        }

        private void UpdateFactionSets()
        {
            _controlledFactionNames.Clear();
            _enemyFactionNames.Clear();

            if (controlledFactions != null && controlledFactions.HasAnyFaction)
            {
                foreach (var faction in controlledFactions.SelectedFactions)
                {
                    _controlledFactionNames.Add(faction);
                }
            }

            if (autoDetectEnemyFactions && playerRTSController != null && 
                playerRTSController.playerControlledFactions != null &&
                playerRTSController.playerControlledFactions.HasAnyFaction)
            {
                foreach (var faction in playerRTSController.playerControlledFactions.SelectedFactions)
                {
                    _enemyFactionNames.Add(faction);
                }
            }
            else if (enemyFactions != null && enemyFactions.HasAnyFaction)
            {
                foreach (var faction in enemyFactions.SelectedFactions)
                {
                    _enemyFactionNames.Add(faction);
                }
            }
        }

        #endregion

        #region Agent Discovery

        private void UpdateAgentLists()
        {
            _controlledAgentIndices.Clear();
            _enemyAgentIndices.Clear();

            _agentsCache = crowdController.GetAgents();
            if (_agentsCache == null)
                return;

            for (int i = 0; i < _agentsCache.Count; i++)
            {
                var agent = _agentsCache[i];
                if (agent == null || agent.isDead)
                    continue;

                string factionName = GetAgentFactionName(agent);
                if (string.IsNullOrEmpty(factionName))
                    continue;

                if (_controlledFactionNames.Contains(factionName))
                {
                    _controlledAgentIndices.Add(i);
                }
                else if (_enemyFactionNames.Contains(factionName))
                {
                    _enemyAgentIndices.Add(i);
                }
            }
        }

        private string GetAgentFactionName(CrowdAgent agent)
        {
            if (crowdController.EnemyCrowds == null || 
                agent.enemyCrowdIndex < 0 || 
                agent.enemyCrowdIndex >= crowdController.EnemyCrowds.Count)
                return null;

            var crowd = crowdController.EnemyCrowds[agent.enemyCrowdIndex];
            return crowd?.factionName;
        }

        #endregion

        #region Decision Making

        private void MakeDecision()
        {
            UpdateAgentLists();

            // No agents to control - go idle
            if (_controlledAgentIndices.Count == 0)
            {
                TransitionToState(RTSAIState.Idle);
                _consecutiveIdleFrames++;
                return;
            }

            _consecutiveIdleFrames = 0;

            // Update tactical groups if needed
            if (enableGroupSplitting)
            {
                UpdateTacticalGroups();
            }

            // Find nearest enemy concentration
            Vector3 armyCenter = GetArmyCenter(_controlledAgentIndices);
            Vector3 nearestEnemyPos;
            float enemyDistance;
            int enemyCount;
            FindNearestEnemyConcentration(armyCenter, out nearestEnemyPos, out enemyDistance, out enemyCount);

            // Evaluate tactical situation
            float forceRatio = enemyCount > 0 ? (float)_controlledAgentIndices.Count / enemyCount : float.MaxValue;

            // State machine decisions
            switch (_currentState)
            {
                case RTSAIState.Idle:
                    DecideFromIdle(armyCenter, nearestEnemyPos, enemyDistance, enemyCount, forceRatio);
                    break;

                case RTSAIState.Exploring:
                    DecideWhileExploring(armyCenter, nearestEnemyPos, enemyDistance, enemyCount, forceRatio);
                    break;

                case RTSAIState.Engaging:
                    DecideWhileEngaging(armyCenter, nearestEnemyPos, enemyDistance, enemyCount, forceRatio);
                    break;

                case RTSAIState.Retreating:
                    DecideWhileRetreating(armyCenter, nearestEnemyPos, enemyDistance, enemyCount, forceRatio);
                    break;

                case RTSAIState.Regrouping:
                    DecideWhileRegrouping(armyCenter, nearestEnemyPos, enemyDistance, enemyCount, forceRatio);
                    break;

                case RTSAIState.SettingTrap:
                    DecideWhileSettingTrap(armyCenter, nearestEnemyPos, enemyDistance, enemyCount, forceRatio);
                    break;

                case RTSAIState.Baiting:
                    DecideWhileBaiting(armyCenter, nearestEnemyPos, enemyDistance, enemyCount, forceRatio);
                    break;

                case RTSAIState.Flanking:
                    DecideWhileFlanking(armyCenter, nearestEnemyPos, enemyDistance, enemyCount, forceRatio);
                    break;
            }
        }

        private void DecideFromIdle(Vector3 armyCenter, Vector3 enemyPos, float enemyDistance, int enemyCount, float forceRatio)
        {
            if (enemyCount > 0 && enemyDistance < engagementRange)
            {
                // Enemy nearby - engage or use tactics
                if (ShouldUseTactics(forceRatio))
                {
                    StartBaitingTactic(armyCenter, enemyPos);
                }
                else if (ShouldUseFlankingAttack(forceRatio))
                {
                    StartFlankingAttack(armyCenter, enemyPos);
                }
                else
                {
                    TransitionToState(RTSAIState.Engaging);
                    AttackEnemies(armyCenter);
                }
            }
            else if (enableExploration)
            {
                StartExploration(armyCenter);
            }
        }

        private void DecideWhileExploring(Vector3 armyCenter, Vector3 enemyPos, float enemyDistance, int enemyCount, float forceRatio)
        {
            // Check if exploration time expired
            if (Time.time - _explorationStartTime > explorationDuration)
            {
                StartExploration(armyCenter); // New exploration target
                return;
            }

            // Enemy detected during exploration
            if (enemyCount > 0 && enemyDistance < engagementRange)
            {
                if (debugLogs)
                    Debug.Log($"[RTS AI] Enemy detected during exploration at distance {enemyDistance:F1}");

                if (forceRatio < 1f / retreatRatio)
                {
                    TransitionToState(RTSAIState.Retreating);
                    RetreatFrom(enemyPos);
                }
                else if (ShouldUseTactics(forceRatio))
                {
                    StartBaitingTactic(armyCenter, enemyPos);
                }
                else if (ShouldUseFlankingAttack(forceRatio))
                {
                    StartFlankingAttack(armyCenter, enemyPos);
                }
                else
                {
                    TransitionToState(RTSAIState.Engaging);
                    AttackEnemies(armyCenter);
                }
            }

            // Check if forces are scattered
            if (AreForcesTooScattered(armyCenter))
            {
                TransitionToState(RTSAIState.Regrouping);
                RegroupForces(armyCenter);
            }
        }

        private void DecideWhileEngaging(Vector3 armyCenter, Vector3 enemyPos, float enemyDistance, int enemyCount, float forceRatio)
        {
            if (enemyCount == 0)
            {
                // No more enemies - return to exploration
                if (enableExploration)
                    StartExploration(armyCenter);
                else
                    TransitionToState(RTSAIState.Idle);
                return;
            }

            // Check if we should retreat
            if (forceRatio < 1f / retreatRatio)
            {
                TransitionToState(RTSAIState.Retreating);
                RetreatFrom(enemyPos);
                return;
            }

            // Continue engagement - update attack orders periodically
            if (Time.time - _stateStartTime > 3f)
            {
                AttackEnemies(armyCenter);
                _stateStartTime = Time.time;
            }
        }

        private void DecideWhileRetreating(Vector3 armyCenter, Vector3 enemyPos, float enemyDistance, int enemyCount, float forceRatio)
        {
            // Safe distance reached or enemy gone
            if (enemyDistance > engagementRange * 1.5f || enemyCount == 0)
            {
                if (AreForcesTooScattered(armyCenter))
                {
                    TransitionToState(RTSAIState.Regrouping);
                    RegroupForces(armyCenter);
                }
                else if (enableExploration)
                {
                    StartExploration(armyCenter);
                }
                else
                {
                    TransitionToState(RTSAIState.Idle);
                }
                return;
            }

            // Continue retreating if enemies still close
            if (Time.time - _stateStartTime > 2f)
            {
                RetreatFrom(enemyPos);
                _stateStartTime = Time.time;
            }
        }

        private void DecideWhileRegrouping(Vector3 armyCenter, Vector3 enemyPos, float enemyDistance, int enemyCount, float forceRatio)
        {
            // Enemy approaching during regroup - respond
            if (enemyCount > 0 && enemyDistance < engagementRange * 0.5f)
            {
                TransitionToState(RTSAIState.Engaging);
                AttackEnemies(armyCenter);
                return;
            }

            // Check if regrouped
            if (!AreForcesTooScattered(armyCenter) || Time.time - _stateStartTime > 10f)
            {
                if (enableExploration)
                    StartExploration(armyCenter);
                else
                    TransitionToState(RTSAIState.Idle);
            }
        }

        private void DecideWhileSettingTrap(Vector3 armyCenter, Vector3 enemyPos, float enemyDistance, int enemyCount, float forceRatio)
        {
            // Main force in position - start baiting
            float distanceToTrapPos = Vector3.Distance(armyCenter, _trapPosition);
            if (distanceToTrapPos < 10f || Time.time - _stateStartTime > 8f)
            {
                SetMainForceStance(CombatStance.HoldPosition);
                TransitionToState(RTSAIState.Baiting);
                SendBaitUnits(enemyPos);
            }
        }

        private void DecideWhileBaiting(Vector3 armyCenter, Vector3 enemyPos, float enemyDistance, int enemyCount, float forceRatio)
        {
            // Check if bait worked - enemies approaching trap
            float enemyDistanceToTrap = Vector3.Distance(enemyPos, _trapPosition);
            
            if (enemyDistanceToTrap < baitRetreatDistance * 0.5f)
            {
                // Sprung the trap!
                if (debugLogs)
                    Debug.Log("[RTS AI] Trap sprung! Engaging enemies.");

                TransitionToState(RTSAIState.Engaging);
                SetAllAgentsStance(CombatStance.Offensive);
                AttackEnemies(armyCenter);
                return;
            }

            // Timeout - bait failed
            if (Time.time - _stateStartTime > 15f)
            {
                if (debugLogs)
                    Debug.Log("[RTS AI] Bait tactic timed out.");

                SetAllAgentsStance(CombatStance.Offensive);
                TransitionToState(RTSAIState.Engaging);
                AttackEnemies(armyCenter);
            }
        }

        private void DecideWhileFlanking(Vector3 armyCenter, Vector3 enemyPos, float enemyDistance, int enemyCount, float forceRatio)
        {
            // No enemies left
            if (enemyCount == 0)
            {
                if (enableExploration)
                    StartExploration(armyCenter);
                else
                    TransitionToState(RTSAIState.Idle);
                return;
            }

            // Check if we should retreat
            if (forceRatio < 1f / retreatRatio)
            {
                TransitionToState(RTSAIState.Retreating);
                RetreatFrom(enemyPos);
                return;
            }

            // Check if it's time to engage flanking groups
            float timeSinceFlankingStart = Time.time - _flankingStartTime;
            
            if (!_flanksEngaged && timeSinceFlankingStart >= flankingDelay)
            {
                EngageFlankingGroups(enemyPos);
                _flanksEngaged = true;
            }

            // Update group targets periodically
            if (Time.time - _stateStartTime > 2f)
            {
                UpdateFlankingTargets(enemyPos);
                _stateStartTime = Time.time;
            }

            // Check if flanking complete (all groups engaged and enemies significantly reduced)
            if (_flanksEngaged && timeSinceFlankingStart > 10f)
            {
                // Switch to normal engagement
                TransitionToState(RTSAIState.Engaging);
                ConsolidateGroups();
                AttackEnemies(armyCenter);
            }
        }

        #endregion

        #region Tactical Actions

        private bool ShouldUseTactics(float forceRatio)
        {
            if (!enableAdvancedTactics)
                return false;

            if (_controlledAgentIndices.Count < minAgentsForBaiting)
                return false;

            // Higher difficulty = more likely to use tactics
            float tacticsChance = 0.1f + (difficulty * 0.15f);
            
            // More likely when we have numerical advantage
            if (forceRatio > 1.5f)
                tacticsChance += 0.2f;

            return UnityEngine.Random.value < tacticsChance;
        }

        private void StartBaitingTactic(Vector3 armyCenter, Vector3 enemyPos)
        {
            if (debugLogs)
                Debug.Log("[RTS AI] Initiating baiting tactic.");

            // Calculate trap position - behind our current position relative to enemy
            Vector3 dirFromEnemy = (armyCenter - enemyPos).normalized;
            _trapPosition = armyCenter + dirFromEnemy * baitRetreatDistance;

            // Ensure trap position is on NavMesh
            if (TrySampleControlledNavigationPosition(_trapPosition, 20f, out Vector3 hit))
            {
                _trapPosition = hit;
            }

            // Select bait units
            SelectBaitUnits();

            // Move main force to trap position
            TransitionToState(RTSAIState.SettingTrap);
            MoveMainForceToPosition(_trapPosition);
        }

        private void SelectBaitUnits()
        {
            _baitAgentIndices.Clear();
            _mainForceIndices.Clear();

            int baitCount = Mathf.Max(1, Mathf.CeilToInt(_controlledAgentIndices.Count * baitPercentage));

            // Select bait units (prefer faster/healthier units)
            for (int i = 0; i < _controlledAgentIndices.Count; i++)
            {
                if (i < baitCount)
                    _baitAgentIndices.Add(_controlledAgentIndices[i]);
                else
                    _mainForceIndices.Add(_controlledAgentIndices[i]);
            }
        }

        private void SendBaitUnits(Vector3 enemyPos)
        {
            if (_baitAgentIndices.Count == 0)
                return;

            // Set bait units to offensive and send toward enemy
            foreach (int idx in _baitAgentIndices)
            {
                if (idx >= 0 && idx < _agentsCache.Count)
                {
                    _agentsCache[idx].combatStance = CombatStance.Offensive;
                }
            }

            // Move bait toward enemy (they'll retreat when attacked due to low numbers)
            SetDestinationForAgents(_baitAgentIndices, enemyPos);
        }

        private void MoveMainForceToPosition(Vector3 position)
        {
            if (_mainForceIndices.Count == 0)
            {
                SetDestinationForAgents(_controlledAgentIndices, position);
            }
            else
            {
                SetDestinationForAgents(_mainForceIndices, position);
            }
        }

        private void SetMainForceStance(CombatStance stance)
        {
            var indices = _mainForceIndices.Count > 0 ? _mainForceIndices : _controlledAgentIndices;
            foreach (int idx in indices)
            {
                if (idx >= 0 && idx < _agentsCache.Count)
                {
                    _agentsCache[idx].combatStance = stance;
                    if (stance == CombatStance.HoldPosition && _agentsCache[idx].transform != null)
                    {
                        _agentsCache[idx].holdPositionLocation = _agentsCache[idx].transform.position;
                        _agentsCache[idx].hasHoldPositionSet = true;
                    }
                }
            }
        }

        private void SetAllAgentsStance(CombatStance stance)
        {
            foreach (int idx in _controlledAgentIndices)
            {
                if (idx >= 0 && idx < _agentsCache.Count)
                {
                    _agentsCache[idx].combatStance = stance;
                }
            }
        }

        private void StartExploration(Vector3 fromPosition)
        {
            TransitionToState(RTSAIState.Exploring);
            _explorationCenter = fromPosition;
            _explorationStartTime = Time.time;

            // Pick a random exploration target
            Vector3 explorationTarget = GetRandomExplorationTarget(fromPosition);
            SetDestinationForAgents(_controlledAgentIndices, explorationTarget);
            SetAllAgentsStance(CombatStance.Offensive);

            if (debugLogs)
                Debug.Log($"[RTS AI] Exploring toward {explorationTarget}");
        }

        private void AttackEnemies(Vector3 armyCenter)
        {
            if (_enemyAgentIndices.Count == 0)
                return;

            // Find closest enemy cluster
            Vector3 targetPos = GetArmyCenter(_enemyAgentIndices);
            
            SetAllAgentsStance(CombatStance.Offensive);
            SetDestinationForAgents(_controlledAgentIndices, targetPos);

            if (debugLogs)
                Debug.Log($"[RTS AI] Attacking enemies at {targetPos}");
        }

        private void RetreatFrom(Vector3 threatPosition)
        {
            Vector3 armyCenter = GetArmyCenter(_controlledAgentIndices);
            Vector3 retreatDir = (armyCenter - threatPosition).normalized;
            Vector3 retreatTarget = armyCenter + retreatDir * baitRetreatDistance;

            if (TrySampleControlledNavigationPosition(retreatTarget, 30f, out Vector3 hit))
            {
                retreatTarget = hit;
            }

            SetAllAgentsStance(CombatStance.Defensive);
            SetDestinationForAgents(_controlledAgentIndices, retreatTarget);

            if (debugLogs)
                Debug.Log($"[RTS AI] Retreating to {retreatTarget}");
        }

        private void RegroupForces(Vector3 center)
        {
            SetAllAgentsStance(CombatStance.Defensive);
            SetDestinationForAgents(_controlledAgentIndices, center);

            if (debugLogs)
                Debug.Log($"[RTS AI] Regrouping at {center}");
        }

        #endregion

        #region Group Splitting & Flanking

        /// <summary>
        /// Updates tactical groups based on current force size and composition.
        /// Splits large groups and maintains group coherence.
        /// </summary>
        private void UpdateTacticalGroups()
        {
            // Only update periodically for performance
            if (Time.time - _lastGroupUpdateTime < 1f)
                return;
            _lastGroupUpdateTime = Time.time;

            int totalAgents = _controlledAgentIndices.Count;

            // Not enough agents to split
            if (totalAgents < minGroupSize * 2)
            {
                // Consolidate into single group
                if (_tacticalGroups.Count != 1)
                {
                    ConsolidateGroups();
                }
                return;
            }

            // Check if we need to split (force is too large)
            if (_tacticalGroups.Count <= 1 && totalAgents > maxGroupSize)
            {
                SplitIntoTacticalGroups();
            }
            // Check if we need to merge (groups too small after losses)
            else if (_tacticalGroups.Count > 1)
            {
                ValidateAndRebalanceGroups();
            }
        }

        /// <summary>
        /// Splits the controlled agents into multiple tactical groups.
        /// </summary>
        private void SplitIntoTacticalGroups()
        {
            _tacticalGroups.Clear();

            int totalAgents = _controlledAgentIndices.Count;
            
            // Calculate number of groups based on total agents
            int numGroups = Mathf.Min(maxGroups, Mathf.CeilToInt((float)totalAgents / maxGroupSize));
            numGroups = Mathf.Max(2, numGroups); // At least 2 groups for flanking

            // Normalize attack distribution
            Vector3 dist = attackDistribution;
            float distSum = dist.x + dist.y + dist.z;
            if (distSum > 0)
            {
                dist /= distSum;
            }
            else
            {
                dist = new Vector3(0.4f, 0.4f, 0.2f);
            }

            // Calculate agents per role based on distribution
            int mainForceCount = Mathf.RoundToInt(totalAgents * dist.x);
            int flankCount = Mathf.RoundToInt(totalAgents * dist.y);
            int rearCount = totalAgents - mainForceCount - flankCount;

            // Create groups with roles
            int agentIndex = 0;

            // Main force group
            var mainGroup = new TacticalGroup(0, TacticalRole.MainForce);
            for (int i = 0; i < mainForceCount && agentIndex < _controlledAgentIndices.Count; i++, agentIndex++)
            {
                mainGroup.agentIndices.Add(_controlledAgentIndices[agentIndex]);
            }
            if (mainGroup.Count > 0)
                _tacticalGroups.Add(mainGroup);

            // Split flank count between left and right
            int leftFlankCount = flankCount / 2;
            int rightFlankCount = flankCount - leftFlankCount;

            // Left flank group
            if (leftFlankCount >= minGroupSize)
            {
                var leftGroup = new TacticalGroup(1, TacticalRole.LeftFlank);
                for (int i = 0; i < leftFlankCount && agentIndex < _controlledAgentIndices.Count; i++, agentIndex++)
                {
                    leftGroup.agentIndices.Add(_controlledAgentIndices[agentIndex]);
                }
                if (leftGroup.Count > 0)
                    _tacticalGroups.Add(leftGroup);
            }

            // Right flank group
            if (rightFlankCount >= minGroupSize)
            {
                var rightGroup = new TacticalGroup(2, TacticalRole.RightFlank);
                for (int i = 0; i < rightFlankCount && agentIndex < _controlledAgentIndices.Count; i++, agentIndex++)
                {
                    rightGroup.agentIndices.Add(_controlledAgentIndices[agentIndex]);
                }
                if (rightGroup.Count > 0)
                    _tacticalGroups.Add(rightGroup);
            }

            // Rear guard group
            if (rearCount >= minGroupSize)
            {
                var rearGroup = new TacticalGroup(3, TacticalRole.RearGuard);
                for (int i = 0; i < rearCount && agentIndex < _controlledAgentIndices.Count; i++, agentIndex++)
                {
                    rearGroup.agentIndices.Add(_controlledAgentIndices[agentIndex]);
                }
                if (rearGroup.Count > 0)
                    _tacticalGroups.Add(rearGroup);
            }

            // Add remaining agents to main force
            while (agentIndex < _controlledAgentIndices.Count)
            {
                if (_tacticalGroups.Count > 0)
                    _tacticalGroups[0].agentIndices.Add(_controlledAgentIndices[agentIndex]);
                agentIndex++;
            }

            if (debugLogs)
            {
                Debug.Log($"[RTS AI] Split forces into {_tacticalGroups.Count} tactical groups:");
                foreach (var group in _tacticalGroups)
                {
                    Debug.Log($"  - {group.role}: {group.Count} agents");
                }
            }
        }

        /// <summary>
        /// Validates group sizes and rebalances if needed.
        /// </summary>
        private void ValidateAndRebalanceGroups()
        {
            // Remove empty groups
            _tacticalGroups.RemoveAll(g => g.Count == 0);

            // Check if any group is too small
            bool needsRebalance = false;
            foreach (var group in _tacticalGroups)
            {
                // Update agent list (remove dead agents)
                group.agentIndices.RemoveAll(idx => 
                    idx < 0 || idx >= _agentsCache.Count || 
                    _agentsCache[idx] == null || _agentsCache[idx].isDead);

                if (group.Count < minGroupSize && group.Count > 0)
                {
                    needsRebalance = true;
                }
            }

            if (needsRebalance)
            {
                // Merge small groups into larger ones
                MergeSmallGroups();
            }
        }

        /// <summary>
        /// Merges groups that are too small into larger groups.
        /// </summary>
        private void MergeSmallGroups()
        {
            var smallGroups = _tacticalGroups.FindAll(g => g.Count < minGroupSize && g.Count > 0);
            var largeGroups = _tacticalGroups.FindAll(g => g.Count >= minGroupSize);

            if (largeGroups.Count == 0)
            {
                // All groups are small - consolidate everything
                ConsolidateGroups();
                return;
            }

            // Distribute small group agents to larger groups
            foreach (var smallGroup in smallGroups)
            {
                // Find the smallest large group to balance
                largeGroups.Sort((a, b) => a.Count.CompareTo(b.Count));
                
                foreach (int agentIdx in smallGroup.agentIndices)
                {
                    largeGroups[0].agentIndices.Add(agentIdx);
                }

                smallGroup.Clear();
            }

            // Remove emptied groups
            _tacticalGroups.RemoveAll(g => g.Count == 0);

            if (debugLogs)
            {
                Debug.Log($"[RTS AI] Merged small groups. Now have {_tacticalGroups.Count} groups.");
            }
        }

        /// <summary>
        /// Consolidates all tactical groups into a single main force.
        /// </summary>
        private void ConsolidateGroups()
        {
            _tacticalGroups.Clear();
            
            var mainGroup = new TacticalGroup(0, TacticalRole.MainForce);
            mainGroup.agentIndices.AddRange(_controlledAgentIndices);
            _tacticalGroups.Add(mainGroup);

            if (debugLogs)
            {
                Debug.Log($"[RTS AI] Consolidated into single group of {mainGroup.Count} agents.");
            }
        }

        /// <summary>
        /// Determines if flanking attack should be used.
        /// </summary>
        private bool ShouldUseFlankingAttack(float forceRatio)
        {
            if (!enableGroupSplitting)
                return false;

            // Need enough agents to split into meaningful groups
            if (_controlledAgentIndices.Count < minGroupSize * 2)
                return false;

            // Higher difficulty = more likely to use flanking
            float flankingChance = 0.2f + (difficulty * 0.12f);
            
            // More likely when we have numerical advantage
            if (forceRatio > 1.3f)
                flankingChance += 0.2f;

            // Less likely when force is small
            if (_controlledAgentIndices.Count < maxGroupSize)
                flankingChance *= 0.5f;

            return UnityEngine.Random.value < flankingChance;
        }

        /// <summary>
        /// Initiates a flanking attack with multiple groups.
        /// </summary>
        private void StartFlankingAttack(Vector3 armyCenter, Vector3 enemyPos)
        {
            if (debugLogs)
                Debug.Log("[RTS AI] Initiating flanking attack maneuver.");

            // Ensure we have tactical groups
            if (_tacticalGroups.Count < 2)
            {
                SplitIntoTacticalGroups();
            }

            if (_tacticalGroups.Count < 2)
            {
                // Couldn't split, fall back to normal engagement
                TransitionToState(RTSAIState.Engaging);
                AttackEnemies(armyCenter);
                return;
            }

            TransitionToState(RTSAIState.Flanking);
            _flankingStartTime = Time.time;
            _flanksEngaged = false;

            // Calculate attack directions
            Vector3 toEnemy = (enemyPos - armyCenter).normalized;
            Vector3 perpendicular = Vector3.Cross(toEnemy, Vector3.up).normalized;

            // Assign positions based on group roles
            foreach (var group in _tacticalGroups)
            {
                Vector3 targetPos = enemyPos;
                float angleRad = flankingAngle * Mathf.Deg2Rad;

                switch (group.role)
                {
                    case TacticalRole.MainForce:
                        // Main force approaches directly
                        targetPos = enemyPos;
                        group.currentStance = CombatStance.Offensive;
                        group.isEngaged = true; // Main force engages immediately
                        break;

                    case TacticalRole.LeftFlank:
                        // Left flank approaches from the side
                        targetPos = enemyPos - perpendicular * flankingDistance;
                        if (TrySampleControlledNavigationPosition(targetPos, 30f, out Vector3 hitL))
                            targetPos = hitL;
                        group.currentStance = CombatStance.Defensive; // Hold until engagement signal
                        break;

                    case TacticalRole.RightFlank:
                        // Right flank approaches from the other side
                        targetPos = enemyPos + perpendicular * flankingDistance;
                        if (TrySampleControlledNavigationPosition(targetPos, 30f, out Vector3 hitR))
                            targetPos = hitR;
                        group.currentStance = CombatStance.Defensive;
                        break;

                    case TacticalRole.RearGuard:
                        // Rear guard circles around to attack from behind
                        Vector3 behindEnemy = enemyPos + toEnemy * flankingDistance;
                        if (TrySampleControlledNavigationPosition(behindEnemy, 40f, out Vector3 hitB))
                            targetPos = hitB;
                        else
                            targetPos = enemyPos; // Fall back to direct attack
                        group.currentStance = CombatStance.Defensive;
                        break;

                    default:
                        targetPos = enemyPos;
                        group.currentStance = CombatStance.Offensive;
                        break;
                }

                group.targetPosition = targetPos;
                SetGroupStance(group, group.currentStance);
                SetDestinationForAgents(group.agentIndices, targetPos);
            }

            if (debugLogs)
            {
                Debug.Log($"[RTS AI] Flanking attack deployed with {_tacticalGroups.Count} groups.");
            }
        }

        /// <summary>
        /// Engages all flanking groups (called after delay).
        /// </summary>
        private void EngageFlankingGroups(Vector3 enemyPos)
        {
            if (debugLogs)
                Debug.Log("[RTS AI] Engaging flanking groups!");

            foreach (var group in _tacticalGroups)
            {
                if (!group.isEngaged)
                {
                    group.currentStance = CombatStance.Offensive;
                    group.isEngaged = true;
                    group.engagementTime = Time.time;
                    group.targetPosition = enemyPos;
                    
                    SetGroupStance(group, CombatStance.Offensive);
                    SetDestinationForAgents(group.agentIndices, enemyPos);
                }
            }
        }

        /// <summary>
        /// Updates targets for flanking groups based on enemy position.
        /// </summary>
        private void UpdateFlankingTargets(Vector3 enemyPos)
        {
            foreach (var group in _tacticalGroups)
            {
                if (group.isEngaged)
                {
                    // Update to current enemy position
                    group.targetPosition = enemyPos;
                    SetDestinationForAgents(group.agentIndices, enemyPos);
                }
            }
        }

        /// <summary>
        /// Sets combat stance for all agents in a group.
        /// </summary>
        private void SetGroupStance(TacticalGroup group, CombatStance stance)
        {
            foreach (int idx in group.agentIndices)
            {
                if (idx >= 0 && idx < _agentsCache.Count)
                {
                    _agentsCache[idx].combatStance = stance;
                    
                    if (stance == CombatStance.HoldPosition && _agentsCache[idx].transform != null)
                    {
                        _agentsCache[idx].holdPositionLocation = _agentsCache[idx].transform.position;
                        _agentsCache[idx].hasHoldPositionSet = true;
                    }
                }
            }
            group.currentStance = stance;
        }

        /// <summary>
        /// Gets the center position of a tactical group.
        /// </summary>
        private Vector3 GetGroupCenter(TacticalGroup group)
        {
            if (group.agentIndices.Count == 0 || _agentsCache == null)
                return transform.position;

            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (int idx in group.agentIndices)
            {
                if (idx >= 0 && idx < _agentsCache.Count && 
                    _agentsCache[idx] != null && !_agentsCache[idx].isDead && 
                    _agentsCache[idx].transform != null)
                {
                    sum += _agentsCache[idx].transform.position;
                    count++;
                }
            }

            Vector3 center = count > 0 ? sum / count : transform.position;
            group.lastKnownCenter = center;
            return center;
        }

        #endregion

        #region Spawning

        private void TrySpawnReinforcements()
        {
            if (!enableSpawning)
                return;

            if (crowdController == null)
                return;

            if (_controlledAgentIndices.Count >= maxAgentCount)
                return;

            // Find a faction we control to spawn
            int factionIndex = GetSpawnableFactionIndex();
            if (factionIndex < 0)
                return;

            Vector3 spawnPos = GetSpawnPosition();
            
            int toSpawn = Mathf.Min(spawnWaveSize, maxAgentCount - _controlledAgentIndices.Count);
            
            crowdController.SpawnEnemyCrowd(
                factionIndex,
                spawnPos,
                Quaternion.identity,
                toSpawn,
                spawnFormation
            );

            if (debugLogs)
                Debug.Log($"[RTS AI] Spawned {toSpawn} reinforcements at {spawnPos}");
        }

        private int GetSpawnableFactionIndex()
        {
            if (crowdController.EnemyCrowds == null)
                return -1;

            for (int i = 0; i < crowdController.EnemyCrowds.Count; i++)
            {
                var crowd = crowdController.EnemyCrowds[i];
                if (crowd != null && _controlledFactionNames.Contains(crowd.factionName))
                {
                    return i;
                }
            }

            return -1;
        }

        private Vector3 GetSpawnPosition()
        {
            if (spawnLocation != null)
                return spawnLocation.position;

            // Spawn near existing forces
            if (_controlledAgentIndices.Count > 0)
            {
                Vector3 center = GetArmyCenter(_controlledAgentIndices);
                Vector3 offset = UnityEngine.Random.insideUnitSphere * 20f;
                offset.y = 0;
                Vector3 target = center + offset;

                if (TrySampleControlledNavigationPosition(target, 30f, out Vector3 hit))
                {
                    return hit;
                }
                return center;
            }

            return transform.position;
        }

        #endregion

        #region Utility Methods

        private void TransitionToState(RTSAIState newState)
        {
            if (_currentState != newState)
            {
                if (debugLogs)
                    Debug.Log($"[RTS AI] State: {_currentState} -> {newState}");

                _currentState = newState;
                _stateStartTime = Time.time;
            }
        }

        private Vector3 GetArmyCenter(List<int> agentIndices)
        {
            if (agentIndices.Count == 0 || _agentsCache == null)
                return transform.position;

            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (int idx in agentIndices)
            {
                if (idx >= 0 && idx < _agentsCache.Count && _agentsCache[idx] != null && !_agentsCache[idx].isDead && _agentsCache[idx].transform != null)
                {
                    sum += _agentsCache[idx].transform.position;
                    count++;
                }
            }

            return count > 0 ? sum / count : transform.position;
        }

        private void FindNearestEnemyConcentration(Vector3 fromPosition, out Vector3 position, out float distance, out int count)
        {
            position = Vector3.zero;
            distance = float.MaxValue;
            count = _enemyAgentIndices.Count;

            if (count == 0 || _agentsCache == null)
                return;

            position = GetArmyCenter(_enemyAgentIndices);
            distance = Vector3.Distance(fromPosition, position);
        }

        private bool AreForcesTooScattered(Vector3 center)
        {
            if (_controlledAgentIndices.Count < 2)
                return false;

            int scattered = 0;
            foreach (int idx in _controlledAgentIndices)
            {
                if (idx >= 0 && idx < _agentsCache.Count && _agentsCache[idx] != null && _agentsCache[idx].transform != null)
                {
                    float dist = Vector3.Distance(_agentsCache[idx].transform.position, center);
                    if (dist > regroupDistance)
                        scattered++;
                }
            }

            // Consider scattered if more than 30% are far from center
            return (float)scattered / _controlledAgentIndices.Count > 0.3f;
        }

        private Vector3 GetRandomExplorationTarget(Vector3 fromPosition)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * explorationRadius;
                Vector3 target = fromPosition + new Vector3(randomCircle.x, 0, randomCircle.y);

                // Ensure minimum distance
                if (Vector3.Distance(target, fromPosition) < minExplorationDistance)
                    continue;

                if (TrySampleControlledNavigationPosition(target, 20f, out Vector3 hit))
                {
                    return hit;
                }
            }

            return fromPosition;
        }

        private void SetDestinationForAgents(List<int> agentIndices, Vector3 destination)
        {
            if (agentIndices.Count == 0 || _agentsCache == null)
                return;

            // Set destination for each agent individually
            foreach (int idx in agentIndices)
            {
                if (idx >= 0 && idx < _agentsCache.Count)
                {
                    // If the agent is under patrol control (ManualCommand from SpawnPatrol),
                    // end the patrol by forcing the navigation state to Formation.
                    // ForceSetNavigationState clears hasManualCommand automatically,
                    // so SpawnPatrol will detect the agents are no longer under its control.
                    var agent = _agentsCache[idx];
                    if (agent != null && agent.hasManualCommand &&
                        agent.navigationState == AgentNavigationState.ManualCommand)
                    {
                        agent.ForceSetNavigationState(AgentNavigationState.Formation);
                    }

                    crowdController.SetDestinationForAgent(idx, destination);
                }
            }
        }

        private bool TrySampleControlledNavigationPosition(Vector3 position, float maxDistance, out Vector3 sampledPosition)
        {
            sampledPosition = position;

            if (_agentsCache != null)
            {
                foreach (int idx in _controlledAgentIndices)
                {
                    if (idx < 0 || idx >= _agentsCache.Count)
                        continue;

                    var navAgent = _agentsCache[idx]?.navigationAgent;
                    if (navAgent == null)
                        continue;

                    if (navAgent.SamplePosition(position, out sampledPosition, maxDistance))
                        return true;
                }
            }

            return EnemyMassesNavigationBackendHooks.TrySampleNavMeshPosition(position, maxDistance, out sampledPosition);
        }

        #endregion

        #region AoE Avoidance

        /// <summary>
        /// Updates formation spacing based on AoE threat level.
        /// Called every frame to smoothly adjust spacing.
        /// </summary>
        private void UpdateFormationSpacing()
        {
            // Update AoE threat detection periodically (not every frame for performance)
            if (Time.time - _aoEThreatCacheTime > 1f)
            {
                _targetAoEThreatLevel = DetectAoEThreatLevel();
                _aoEThreatCacheTime = Time.time;
            }

            // Smoothly interpolate current threat level toward target
            float adjustSpeed = 1f / Mathf.Max(0.1f, spacingAdjustmentSpeed);
            _currentAoEThreatLevel = Mathf.MoveTowards(_currentAoEThreatLevel, _targetAoEThreatLevel, adjustSpeed * Time.deltaTime);

            // Calculate target spacing based on threat level
            float targetSpacing;
            if (_currentAoEThreatLevel <= 0f)
            {
                targetSpacing = normalSpacing;
            }
            else if (_currentAoEThreatLevel >= 1f)
            {
                targetSpacing = maxSpreadSpacing;
            }
            else
            {
                // Lerp between spread and max spread based on threat intensity
                float lowThreshold = 0.3f;
                if (_currentAoEThreatLevel < lowThreshold)
                {
                    targetSpacing = Mathf.Lerp(normalSpacing, spreadSpacing, _currentAoEThreatLevel / lowThreshold);
                }
                else
                {
                    targetSpacing = Mathf.Lerp(spreadSpacing, maxSpreadSpacing, (_currentAoEThreatLevel - lowThreshold) / (1f - lowThreshold));
                }
            }

            // Smoothly adjust current spacing
            _currentFormationSpacing = Mathf.MoveTowards(_currentFormationSpacing, targetSpacing, adjustSpeed * Time.deltaTime * 2f);

            // Apply spacing change if significant
            if (Mathf.Abs(_currentFormationSpacing - _lastSpacingUpdateTime) > 0.1f)
            {
                ApplyFormationSpacing(_currentFormationSpacing);
                _lastSpacingUpdateTime = _currentFormationSpacing;
            }
        }

        /// <summary>
        /// Detects AoE threat level from enemy forces.
        /// Returns 0-1 where 0 = no AoE threat, 1 = maximum AoE threat.
        /// </summary>
        private float DetectAoEThreatLevel()
        {
            if (!autoDetectAoEThreats)
            {
                return manualAoEThreatLevel;
            }

            if (crowdController == null || crowdController.EnemyCrowds == null)
                return manualAoEThreatLevel;

            float maxThreat = 0f;
            int aoEEnemyCount = 0;

            // Check each enemy faction for AoE capabilities
            foreach (var crowd in crowdController.EnemyCrowds)
            {
                if (crowd == null)
                    continue;

                // Skip if not an enemy faction
                if (!_enemyFactionNames.Contains(crowd.factionName))
                    continue;

                // Check for AoE indicators in the EnemyCrowd
                float factionThreat = EvaluateFactionAoEThreat(crowd);
                
                if (factionThreat > 0f)
                {
                    aoEEnemyCount++;
                    maxThreat = Mathf.Max(maxThreat, factionThreat);
                }
            }

            // Increase threat level if multiple AoE factions present
            if (aoEEnemyCount > 1)
            {
                maxThreat = Mathf.Min(1f, maxThreat + 0.2f * (aoEEnemyCount - 1));
            }

            // Higher difficulty AI responds more strongly to AoE threats
            float difficultyModifier = 0.5f + (difficulty * 0.1f);
            maxThreat *= difficultyModifier;

            return Mathf.Clamp01(maxThreat);
        }

        /// <summary>
        /// Evaluates AoE threat from a specific EnemyCrowd faction.
        /// </summary>
        private float EvaluateFactionAoEThreat(EnemyCrowd crowd)
        {
            float threat = 0f;

            // Check primary projectile config for AoE
            if (crowd.projectileConfig != null && crowd.projectileConfig.enableAOE)
            {
                if (crowd.projectileConfig.aoeRadius > aoEThresholdRadius)
                {
                    float radiusThreat = Mathf.Clamp01((crowd.projectileConfig.aoeRadius - aoEThresholdRadius) / 5f);
                    threat = Mathf.Max(threat, radiusThreat);
                    
                    // Projectile Arc mode with AoE is especially dangerous (grenades, catapults)
                    if (crowd.rangedAttackMode == RangedAttackMode.ProjectileArc)
                    {
                        threat = Mathf.Min(1f, threat + 0.2f);
                    }
                }
            }

            // Check secondary projectile config for AoE
            if (crowd.secondaryProjectileConfig != null && crowd.secondaryProjectileConfig.enableAOE)
            {
                if (crowd.secondaryProjectileConfig.aoeRadius > aoEThresholdRadius)
                {
                    float radiusThreat = Mathf.Clamp01((crowd.secondaryProjectileConfig.aoeRadius - aoEThresholdRadius) / 5f);
                    threat = Mathf.Max(threat, radiusThreat);
                    
                    if (crowd.secondaryAttackMode == RangedAttackMode.ProjectileArc)
                    {
                        threat = Mathf.Min(1f, threat + 0.2f);
                    }
                }
            }

            // Check if ranged with large engagement radius (indicates area denial capability)
            if ((crowd.combatRangeType == CombatRangeType.Ranged || crowd.combatRangeType == CombatRangeType.Hybrid) &&
                crowd.engagementRadius > 20f)
            {
                // Ranged units with AoE are more dangerous, add small bonus
                if (threat > 0f)
                {
                    threat = Mathf.Min(1f, threat + 0.1f);
                }
            }

            return threat;
        }

        /// <summary>
        /// Applies the calculated formation spacing to controlled agents.
        /// </summary>
        private void ApplyFormationSpacing(float spacing)
        {
            if (_controlledAgentIndices.Count == 0 || _agentsCache == null)
                return;

            // Only apply if we have significant controlled force
            if (_controlledAgentIndices.Count < 5)
                return;

            // Log spacing changes for debugging
            if (debugLogs)
            {
                Debug.Log($"[RTS AI] Adjusting formation spacing to {spacing:F2} (AoE threat: {_currentAoEThreatLevel:P0})");
            }

            // The spacing is used when giving move commands
            // Store it for use in SetDestinationForAgents and combat commands
        }

        /// <summary>
        /// Gets the current recommended formation spacing for the AI's forces.
        /// </summary>
        public float GetRecommendedSpacing()
        {
            return enableAoEAvoidance ? _currentFormationSpacing : normalSpacing;
        }

        /// <summary>
        /// Manually set the AoE threat level (0-1).
        /// Useful for scripted events where you know AoE is coming.
        /// </summary>
        public void SetAoEThreatLevel(float threatLevel)
        {
            manualAoEThreatLevel = Mathf.Clamp01(threatLevel);
            _targetAoEThreatLevel = manualAoEThreatLevel;
            
            if (debugLogs)
            {
                Debug.Log($"[RTS AI] Manual AoE threat level set to {threatLevel:P0}");
            }
        }

        /// <summary>
        /// Force immediate spacing adjustment (skips smooth transition).
        /// </summary>
        public void ForceSpacingUpdate()
        {
            _targetAoEThreatLevel = DetectAoEThreatLevel();
            _currentAoEThreatLevel = _targetAoEThreatLevel;
            
            float targetSpacing = _currentAoEThreatLevel <= 0f ? normalSpacing : 
                                  Mathf.Lerp(spreadSpacing, maxSpreadSpacing, _currentAoEThreatLevel);
            _currentFormationSpacing = targetSpacing;
            ApplyFormationSpacing(_currentFormationSpacing);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Manually trigger an attack on enemy forces.
        /// </summary>
        public void ForceAttack()
        {
            if (!Application.isPlaying)
                return;

            UpdateAgentLists();
            if (_controlledAgentIndices.Count > 0)
            {
                Vector3 armyCenter = GetArmyCenter(_controlledAgentIndices);
                TransitionToState(RTSAIState.Engaging);
                AttackEnemies(armyCenter);
            }
        }

        /// <summary>
        /// Manually trigger a retreat.
        /// </summary>
        public void ForceRetreat()
        {
            if (!Application.isPlaying)
                return;

            UpdateAgentLists();
            if (_controlledAgentIndices.Count > 0 && _enemyAgentIndices.Count > 0)
            {
                Vector3 enemyPos = GetArmyCenter(_enemyAgentIndices);
                TransitionToState(RTSAIState.Retreating);
                RetreatFrom(enemyPos);
            }
        }

        /// <summary>
        /// Set all controlled agents to a specific stance.
        /// </summary>
        public void SetStance(CombatStance stance)
        {
            if (!Application.isPlaying)
                return;

            UpdateAgentLists();
            SetAllAgentsStance(stance);
        }

        /// <summary>
        /// Force the AI to regroup its forces.
        /// </summary>
        public void ForceRegroup()
        {
            if (!Application.isPlaying)
                return;

            UpdateAgentLists();
            if (_controlledAgentIndices.Count > 0)
            {
                Vector3 center = GetArmyCenter(_controlledAgentIndices);
                TransitionToState(RTSAIState.Regrouping);
                RegroupForces(center);
            }
        }

        /// <summary>
        /// Force the AI to split its forces into tactical groups.
        /// </summary>
        public void ForceSplitGroups()
        {
            if (!Application.isPlaying)
                return;

            UpdateAgentLists();
            if (_controlledAgentIndices.Count >= minGroupSize * 2)
            {
                SplitIntoTacticalGroups();
            }
        }

        /// <summary>
        /// Force the AI to consolidate all groups into one.
        /// </summary>
        public void ForceConsolidateGroups()
        {
            if (!Application.isPlaying)
                return;

            UpdateAgentLists();
            ConsolidateGroups();
        }

        /// <summary>
        /// Initiates a flanking attack if conditions are met.
        /// </summary>
        public void ForceFlankingAttack()
        {
            if (!Application.isPlaying)
                return;

            UpdateAgentLists();
            if (_controlledAgentIndices.Count > 0 && _enemyAgentIndices.Count > 0)
            {
                Vector3 armyCenter = GetArmyCenter(_controlledAgentIndices);
                Vector3 enemyPos = GetArmyCenter(_enemyAgentIndices);
                StartFlankingAttack(armyCenter, enemyPos);
            }
        }

        /// <summary>
        /// Gets information about a specific tactical group.
        /// </summary>
        public TacticalGroup GetTacticalGroup(int index)
        {
            if (index >= 0 && index < _tacticalGroups.Count)
                return _tacticalGroups[index];
            return null;
        }

        /// <summary>
        /// Gets agents belonging to a specific tactical group.
        /// </summary>
        public List<int> GetGroupAgentIndices(TacticalRole role)
        {
            var group = _tacticalGroups.Find(g => g.role == role);
            return group?.agentIndices ?? new List<int>();
        }

        #endregion

        #region Gizmos

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmos)
                return;

            // Draw AI state indicator
            Vector3 pos = transform.position + Vector3.up * 2f;
            Gizmos.color = GetStateColor();
            Gizmos.DrawWireSphere(pos, 1f);

            if (Application.isPlaying && _controlledAgentIndices.Count > 0 && _agentsCache != null)
            {
                Vector3 center = GetArmyCenter(_controlledAgentIndices);
                
                // Draw army center
                Gizmos.color = aiColor;
                Gizmos.DrawWireSphere(center, 3f);

                // Draw exploration radius
                if (_currentState == RTSAIState.Exploring)
                {
                    Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);
                    DrawWireCircle(_explorationCenter, explorationRadius, 32);
                }

                // Draw trap position when setting trap
                if (_currentState == RTSAIState.SettingTrap || _currentState == RTSAIState.Baiting)
                {
                    Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
                    Gizmos.DrawWireCube(_trapPosition, Vector3.one * 5f);
                    Gizmos.DrawLine(center, _trapPosition);
                }

                // Draw tactical groups when flanking or when split
                if (_tacticalGroups.Count > 1)
                {
                    foreach (var group in _tacticalGroups)
                    {
                        if (group.agentIndices.Count == 0) continue;

                        Vector3 groupCenter = GetGroupCenter(group);
                        Gizmos.color = GetGroupColor(group.role);
                        
                        // Draw group center sphere
                        Gizmos.DrawWireSphere(groupCenter, 2f);
                        
                        // Draw line to target position
                        if (_currentState == RTSAIState.Flanking && group.targetPosition != Vector3.zero)
                        {
                            Gizmos.DrawLine(groupCenter, group.targetPosition);
                            Gizmos.DrawWireSphere(group.targetPosition, 1f);
                        }

                        // Draw label using handles in editor
#if UNITY_EDITOR
                        UnityEditor.Handles.Label(groupCenter + Vector3.up * 3f, 
                            $"{group.role} ({group.Count})");
#endif
                    }
                }
            }
        }

        private Color GetStateColor()
        {
            return _currentState switch
            {
                RTSAIState.Idle => Color.gray,
                RTSAIState.Exploring => Color.green,
                RTSAIState.Engaging => Color.red,
                RTSAIState.Retreating => Color.yellow,
                RTSAIState.Regrouping => Color.cyan,
                RTSAIState.SettingTrap => new Color(1f, 0.5f, 0f),
                RTSAIState.Baiting => new Color(1f, 0f, 0.5f),
                RTSAIState.Flanking => new Color(0.5f, 0f, 1f),
                _ => Color.white
            };
        }

        private Color GetGroupColor(TacticalRole role)
        {
            return role switch
            {
                TacticalRole.MainForce => new Color(1f, 0.3f, 0.1f, 0.8f),   // Orange
                TacticalRole.LeftFlank => new Color(0.2f, 0.6f, 1f, 0.8f),  // Blue
                TacticalRole.RightFlank => new Color(0.2f, 1f, 0.6f, 0.8f), // Teal
                TacticalRole.RearGuard => new Color(0.8f, 0.2f, 0.8f, 0.8f), // Purple
                TacticalRole.Reserve => new Color(0.5f, 0.5f, 0.5f, 0.8f),  // Gray
                TacticalRole.Bait => new Color(1f, 1f, 0.2f, 0.8f),         // Yellow
                _ => Color.white
            };
        }

        private void DrawWireCircle(Vector3 center, float radius, int segments)
        {
            float angleStep = 360f / segments;
            Vector3 prevPoint = center + Vector3.right * radius;

            for (int i = 1; i <= segments; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 point = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(prevPoint, point);
                prevPoint = point;
            }
        }
#endif

        #endregion
    }
}
