using UnityEngine;
using System.Collections;
using System.Collections.Generic;
#if MEMORYPACK && ARAWN_REMEMBERME
using System;
using System.Reflection;
#endif

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Simple spawner that automatically spawns a crowd when the scene starts.
    /// Provides gizmo preview of the formation so spawn layout is visible in-editor.
    /// </summary>
    public class EnemyMassesStartSpawner : MonoBehaviour
    {
        [Header("Spawn Settings")]
        [Tooltip("The crowd controller that will spawn agents")]
        public EnemyMassesCrowdController crowdController;

        [Tooltip("Which enemy crowd faction to spawn (index in Enemy Crowds list)")]
        public int enemyCrowdIndex = 0;

        [Tooltip("Number of agents to spawn on start")]
        [Min(1)]
        public int spawnCount = 50;

        [Tooltip("Formation type for spawned agents")]
        public FormationType spawnFormation = FormationType.Circle;

        [Tooltip("Spacing override for this spawn. 0 = use faction default; non-zero overrides (abs value used)")]
        public float spawnFormationSpacing = 0f;

        [Tooltip("Spawn automatically in Start() when playing")]
        public bool spawnOnStart = true;

        [Tooltip("Delay in seconds before spawning begins (0 = immediate)")]
        [Min(0f)]
        public float spawnDelay = 0f;

        [Tooltip("If true, only spawns once even if component is re-enabled")]
        public bool spawnOnce = true;

        [Header("Spawn Location")]
        [Tooltip("Where agents will spawn (position + rotation)")]
        public Transform spawnLocation;

        [Tooltip("If no location is set, use this transform's position")]
        public bool useThisTransformIfNoLocation = true;

        [Header("Post Spawn Behavior")]
        [Tooltip("Automatically set a destination after spawning")]
        public bool autoSetDestination = true;

        [Tooltip("Optional destination override. If null, uses spawn position")]
        public Transform destinationOverride;

        [Tooltip("Spawned enemies automatically attack this target (falls back to PlayerTarget if null)")]
        public bool autoAttackTarget = false;

        [Tooltip("Explicit attack target to use when Auto Attack Target is enabled")]
        public Transform attackTarget;

        [Header("Visual Feedback")]
        [Tooltip("Show spawn location and formation preview gizmos in editor")]
        public bool showGizmos = true;

        [Tooltip("Show formation layout preview for the spawn count")]
        public bool showFormationPreview = true;

        [Tooltip("Custom gizmo color (leave default for auto-color based on faction)")]
        public Color customGizmoColor = Color.clear;

    #if MEMORYPACK && ARAWN_REMEMBERME
        [Header("Crystal Save Integration")]
        [Tooltip("If ON and Crystal Save is present, skip auto-spawn when a load is in progress to avoid double-spawning agents that will be restored by SaveablePrefabs.")]
        public bool skipSpawnDuringCrystalSaveLoad = true;
    #endif

        private bool _hasSpawned;
        private Coroutine _delayedSpawnCoroutine;

        // Color palette for factions (matches Spawn Trigger for consistency)
        private static readonly Color[] FactionColors = new Color[]
        {
            new Color(1f, 0.3f, 0.3f, 0.5f),    // Red
            new Color(0.3f, 0.6f, 1f, 0.5f),    // Blue
            new Color(0.3f, 1f, 0.3f, 0.5f),    // Green
            new Color(1f, 0.8f, 0.2f, 0.5f),    // Yellow
            new Color(1f, 0.4f, 0.8f, 0.5f),    // Pink
            new Color(0.6f, 0.3f, 1f, 0.5f),    // Purple
            new Color(1f, 0.6f, 0.2f, 0.5f),    // Orange
            new Color(0.2f, 0.9f, 0.9f, 0.5f),  // Cyan
        };

        private void OnValidate()
        {
            spawnCount = Mathf.Max(1, spawnCount);
            if (float.IsNaN(spawnFormationSpacing) || float.IsInfinity(spawnFormationSpacing))
                spawnFormationSpacing = 0f;
        }

        private void Start()
        {
            if (!Application.isPlaying)
                return;

            if (_hasSpawned && spawnOnce)
                return;

            if (spawnOnStart)
            {
#if MEMORYPACK && ARAWN_REMEMBERME
                if (skipSpawnDuringCrystalSaveLoad)
                {
                    if (IsCrystalSaveLoading())
                        return;
                }
#endif
                if (spawnDelay > 0f)
                    _delayedSpawnCoroutine = StartCoroutine(DelayedSpawnCoroutine(spawnDelay));
                else
                    TrySpawn();
            }
        }

#if MEMORYPACK && ARAWN_REMEMBERME
        private bool IsCrystalSaveLoading()
        {
            // Reflection-only to avoid assembly reference since Crystal Save is in Assembly-CSharp.
            const string typeName = "Arawn.CrystalSave.Runtime.SaveManager";
            var t = Type.GetType(typeName);
            if (t == null)
                return false;

            var instanceProp = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var isLoadingProp = t.GetProperty("IsLoading", BindingFlags.Public | BindingFlags.Instance);
            if (instanceProp == null || isLoadingProp == null)
                return false;

            var instance = instanceProp.GetValue(null);
            if (instance == null)
                return false;

            var value = isLoadingProp.GetValue(instance);
            return value is bool b && b;
        }
#endif

        private IEnumerator DelayedSpawnCoroutine(float delay)
        {
            yield return new WaitForSeconds(delay);
            _delayedSpawnCoroutine = null;
            TrySpawn();
        }

        /// <summary>
        /// Cancels a pending delayed spawn if one is in progress.
        /// </summary>
        public void CancelDelayedSpawn()
        {
            if (_delayedSpawnCoroutine != null)
            {
                StopCoroutine(_delayedSpawnCoroutine);
                _delayedSpawnCoroutine = null;
            }
        }

        /// <summary>
        /// Manually trigger spawning (usable from other scripts or editor buttons).
        /// Ignores any configured delay.
        /// </summary>
        public void SpawnNow()
        {
            CancelDelayedSpawn();
            TrySpawn();
        }

        private void OnDisable()
        {
            CancelDelayedSpawn();
        }

        private bool TrySpawn()
        {
            if (crowdController == null)
            {
                Debug.LogWarning($"[StartSpawner] {gameObject.name}: No crowd controller assigned!", this);
                return false;
            }

            if (_hasSpawned && spawnOnce)
                return false;

            Vector3 spawnPos = GetSpawnPosition();
            Quaternion spawnRot = GetSpawnRotation();

            List<int> spawnedIndices = crowdController.SpawnEnemyCrowd(
                enemyCrowdIndex,
                spawnPos,
                spawnRot,
                spawnCount,
                spawnFormation,
                spawnFormationSpacing
            );

            if (spawnedIndices != null && spawnedIndices.Count > 0)
            {
                HandlePostSpawnBehavior(spawnPos);
                _hasSpawned = true;
                return true;
            }

            Debug.LogWarning($"[StartSpawner] {gameObject.name}: Failed to spawn agents!", this);
            return false;
        }

        private void HandlePostSpawnBehavior(Vector3 spawnPos)
        {
            if (autoAttackTarget)
            {
                Transform target = attackTarget != null ? attackTarget : crowdController.GetNearestPlayerTarget(spawnPos);
                if (target != null)
                {
                    crowdController.SetAttackTargetForFaction(enemyCrowdIndex, target);
                    return;
                }
            }

            if (autoSetDestination)
            {
                Vector3 destination = destinationOverride != null ? destinationOverride.position : spawnPos;
                crowdController.SetDestination(destination);
            }
        }

        private Vector3 GetSpawnPosition()
        {
            if (spawnLocation != null)
                return spawnLocation.position;

            if (useThisTransformIfNoLocation)
                return transform.position;

            return transform.position;
        }

        private Quaternion GetSpawnRotation()
        {
            if (spawnLocation != null)
                return spawnLocation.rotation;

            if (useThisTransformIfNoLocation)
                return transform.rotation;

            return Quaternion.identity;
        }

        private FormationType GetEffectiveFormationType()
        {
            EnemyCrowd crowd = GetCrowdConfig();
            if (crowd != null && crowd.overrideFormation)
                return crowd.formationType;

            return spawnFormation;
        }

        private EnemyCrowd GetCrowdConfig()
        {
            if (crowdController == null || crowdController.EnemyCrowds == null)
                return null;

            if (enemyCrowdIndex < 0 || enemyCrowdIndex >= crowdController.EnemyCrowds.Count)
                return null;

            return crowdController.EnemyCrowds[enemyCrowdIndex];
        }

        public Color GetGizmoColor()
        {
            if (customGizmoColor != Color.clear)
                return customGizmoColor;

            return FactionColors[Mathf.Abs(enemyCrowdIndex) % FactionColors.Length];
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmos)
                return;

            Color gizmoColor = GetGizmoColor();
            Vector3 spawnPos = GetSpawnPosition();
            Quaternion spawnRot = GetSpawnRotation();

            // Spawn marker
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(spawnPos, 0.5f);
            DrawWireCircle(spawnPos, spawnRot, 2f, 24);
            DrawDirectionalArrow(spawnPos, spawnRot, 3f, gizmoColor);

            // Destination line
            if (destinationOverride != null)
            {
                Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.35f);
                Gizmos.DrawLine(spawnPos, destinationOverride.position);
                Gizmos.DrawWireSphere(destinationOverride.position, 0.3f);
            }

            if (showFormationPreview)
            {
                DrawFormationPreview(spawnPos, spawnRot, gizmoColor);
            }
        }

        private void DrawFormationPreview(Vector3 spawnPos, Quaternion spawnRot, Color color)
        {
            FormationType formation = GetEffectiveFormationType();
            EnemyCrowd crowd = GetCrowdConfig();

            SpawnSettings previewSettings = BuildPreviewSettings(crowd, formation);
            float destinationSpread = GetDestinationSpread(crowd);

            var formationSystem = new EnemyMassesFormationSystem(previewSettings);

            int previewCount = Mathf.Clamp(spawnCount, 1, 256); // Avoid editor slowdown
            for (int i = 0; i < previewCount; i++)
            {
                Vector3 pos = formationSystem.CalculateFormationPosition(
                    i,
                    previewCount,
                    spawnPos,
                    destinationSpread,
                    spawnRot,
                    formation
                );

                Gizmos.color = new Color(color.r, color.g, color.b, 0.55f);
                Gizmos.DrawWireSphere(pos, 0.15f);
            }
        }

        private SpawnSettings BuildPreviewSettings(EnemyCrowd crowd, FormationType formation)
        {
            return new SpawnSettings
            {
                formationType = formation,
                formationSize = crowd != null ? crowd.defaultFormationSize : 10f,
                formationSpacing = !Mathf.Approximately(spawnFormationSpacing, 0f)
                    ? Mathf.Abs(spawnFormationSpacing)
                    : (crowd != null ? crowd.defaultFormationSpacing : 2f),
                formationThickness = crowd != null ? crowd.defaultFormationThickness : 3f
            };
        }

        private float GetDestinationSpread(EnemyCrowd crowd)
        {
            return crowd != null ? crowd.destinationSpread : 25f;
        }

        private void DrawDirectionalArrow(Vector3 position, Quaternion rotation, float length, Color color)
        {
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;

            Vector3 arrowTip = position + forward * length;
            Vector3 arrowBase = position + forward * (length * 0.3f);

            // Shaft (multi-line for thicker appearance)
            Gizmos.color = color;
            float shaftWidth = 0.15f;
            Gizmos.DrawLine(position + right * shaftWidth, arrowBase + right * shaftWidth);
            Gizmos.DrawLine(position - right * shaftWidth, arrowBase - right * shaftWidth);
            Gizmos.DrawLine(position + up * shaftWidth, arrowBase + up * shaftWidth);
            Gizmos.DrawLine(position - up * shaftWidth, arrowBase - up * shaftWidth);

            // Arrow head
            float headWidth = 0.6f;
            float headHeight = 0.3f;
            Vector3 headLeft = arrowBase + right * headWidth;
            Vector3 headRight = arrowBase - right * headWidth;
            Vector3 headTop = arrowBase + up * headHeight;
            Vector3 headBottom = arrowBase - up * headHeight;

            Gizmos.color = new Color(color.r, color.g, color.b, 1f);
            Gizmos.DrawLine(headLeft, arrowTip);
            Gizmos.DrawLine(headRight, arrowTip);
            Gizmos.DrawLine(headLeft, headRight);
            Gizmos.DrawLine(headTop, arrowTip);
            Gizmos.DrawLine(headBottom, arrowTip);
            Gizmos.DrawLine(headTop, headBottom);

            // Fill head for visibility
            int fillLines = 4;
            for (int i = 1; i < fillLines; i++)
            {
                float t = (float)i / fillLines;
                Vector3 leftPoint = Vector3.Lerp(headLeft, arrowTip, t);
                Vector3 rightPoint = Vector3.Lerp(headRight, arrowTip, t);
                Gizmos.DrawLine(leftPoint, rightPoint);
            }
        }

        private void DrawWireCircle(Vector3 center, Quaternion rotation, float radius, int segments)
        {
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;

            float angleStep = 360f / segments;
            Vector3 prevPoint = center + right * radius;

            for (int i = 1; i <= segments; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                Vector3 point = center + (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * radius;
                Gizmos.DrawLine(prevPoint, point);
                prevPoint = point;
            }
        }
#endif
    }
}
