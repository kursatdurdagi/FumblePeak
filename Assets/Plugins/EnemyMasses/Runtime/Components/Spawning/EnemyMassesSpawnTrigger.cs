using UnityEngine;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Triggers spawning of enemy crowds when player or specific objects enter the trigger zone.
    /// Spawns agents at designated spawn locations with visual gizmo feedback.
    /// Automatically creates and syncs collider based on Trigger Shape setting.
    /// </summary>
    public class EnemyMassesSpawnTrigger : MonoBehaviour
    {
        [Header("Trigger Settings")]
        [Tooltip("The crowd controller that will spawn agents")]
        public EnemyMassesCrowdController crowdController;

        [Tooltip("Which enemy crowd faction to spawn (index in Enemy Crowds list)")]
        public int enemyCrowdIndex = 0;

        [Tooltip("Number of agents to spawn when triggered")]
        [Min(1)]
        public int spawnCount = 50;

        [Tooltip("Formation type for spawned agents")]
        public FormationType spawnFormation = FormationType.Circle;

        [Header("Spawn Location")]
        [Tooltip("Where agents will spawn (position + rotation)")]
        public Transform spawnLocation;

        [Tooltip("If null, spawns at this trigger's position")]
        public bool useThisTransformIfNoLocation = true;

        [Header("Trigger Filters")]
        [Tooltip("Trigger activation method")]
        public TriggerActivationType activationType = TriggerActivationType.PlayerTarget;

        [Tooltip("Required tag for triggering objects (when using Tag filter)")]
        public string requiredTag = "Player";

        [Tooltip("Required layer for triggering objects (when using Layer filter)")]
        public LayerMask requiredLayer = -1;

        [Header("Trigger Behavior")]
        [Tooltip("Type of collider shape for the trigger zone")]
        public TriggerShape triggerShape = TriggerShape.Box;

        [Tooltip("Trigger only once. Can be reset via ResetTrigger() method")]
        public bool triggerOnce = true;

        [Tooltip("Cooldown before trigger can activate again (0 = no cooldown, ignored if Trigger Once is enabled)")]
        [Min(0f)]
        public float triggerCooldown = 0f;

        [Tooltip("Automatically set destination to spawn location after spawning")]
        public bool autoSetDestination = true;

        [Tooltip("Spawned enemies automatically attack the object that triggered them")]
        public bool autoAttackTriggeringObject = false;

        [Header("Visual Feedback")]
        [Tooltip("Show trigger and spawn location gizmos in editor")]
        public bool showGizmos = true;

        [Tooltip("Custom gizmo color (leave default for auto-color based on faction)")]
        public Color customGizmoColor = Color.clear;

        // Runtime state
        private bool _hasTriggered;
        private float _lastTriggerTime;
        private Collider _triggerCollider;

        // Color palette for factions (8 distinct colors)
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

        public enum TriggerActivationType
        {
            PlayerTarget,      // Use referenced player from crowd controller
            Tag,              // Match by tag
            Layer,            // Match by layer
            TagAndLayer,      // Must match both tag and layer
            TagOrLayer,       // Match either tag or layer
            Any               // Any object triggers it
        }

        public enum TriggerShape
        {
            Box,
            Sphere,
            Capsule
        }

        private void Awake()
        {
            InitializeTriggerCollider();
        }

        private void InitializeTriggerCollider()
        {
            _triggerCollider = GetComponent<Collider>();
            
            // Check if collider type matches trigger shape setting
            bool needsSync = false;
            if (_triggerCollider != null)
            {
                needsSync = (triggerShape == TriggerShape.Box && !(_triggerCollider is BoxCollider)) ||
                           (triggerShape == TriggerShape.Sphere && !(_triggerCollider is SphereCollider)) ||
                           (triggerShape == TriggerShape.Capsule && !(_triggerCollider is CapsuleCollider));
            }
            
            // Remove mismatched collider or create new one if missing
            if (_triggerCollider == null || needsSync)
            {
                if (_triggerCollider != null)
                {
                    DestroyImmediate(_triggerCollider);
                }
                
                // Add collider based on trigger shape
                switch (triggerShape)
                {
                    case TriggerShape.Box:
                        _triggerCollider = gameObject.AddComponent<BoxCollider>();
                        break;
                    case TriggerShape.Sphere:
                        _triggerCollider = gameObject.AddComponent<SphereCollider>();
                        break;
                    case TriggerShape.Capsule:
                        _triggerCollider = gameObject.AddComponent<CapsuleCollider>();
                        break;
                }
            }

            if (_triggerCollider != null)
            {
                _triggerCollider.isTrigger = true;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!CanTrigger())
                return;

            if (!ShouldTriggerForObject(other.gameObject))
                return;

            TriggerSpawn(other.gameObject);
        }

        private bool CanTrigger()
        {
            // Already triggered and set to trigger once
            if (_hasTriggered && triggerOnce)
                return false;

            // Check cooldown
            if (triggerCooldown > 0f && Time.time - _lastTriggerTime < triggerCooldown)
                return false;

            // No crowd controller assigned
            if (crowdController == null)
            {
                Debug.LogWarning($"[SpawnTrigger] {gameObject.name}: No crowd controller assigned!", this);
                return false;
            }

            return true;
        }

        private bool ShouldTriggerForObject(GameObject obj)
        {
            switch (activationType)
            {
                case TriggerActivationType.PlayerTarget:
                    return crowdController != null && crowdController.IsPlayerTargetTransform(obj.transform);

                case TriggerActivationType.Tag:
                    return !string.IsNullOrEmpty(requiredTag) && obj.CompareTag(requiredTag);

                case TriggerActivationType.Layer:
                    return ((1 << obj.layer) & requiredLayer) != 0;

                case TriggerActivationType.TagAndLayer:
                    bool matchesTag = !string.IsNullOrEmpty(requiredTag) && obj.CompareTag(requiredTag);
                    bool matchesLayer = ((1 << obj.layer) & requiredLayer) != 0;
                    return matchesTag && matchesLayer;

                case TriggerActivationType.TagOrLayer:
                    matchesTag = !string.IsNullOrEmpty(requiredTag) && obj.CompareTag(requiredTag);
                    matchesLayer = ((1 << obj.layer) & requiredLayer) != 0;
                    return matchesTag || matchesLayer;

                case TriggerActivationType.Any:
                    return true;

                default:
                    return false;
            }
        }

        private void TriggerSpawn(GameObject triggeringObject)
        {
            Vector3 spawnPos = GetSpawnPosition();
            Quaternion spawnRot = GetSpawnRotation();

            // Spawn agents
            List<int> spawnedIndices = crowdController.SpawnEnemyCrowd(
                enemyCrowdIndex,
                spawnPos,
                spawnRot,
                spawnCount,
                spawnFormation
            );

            if (spawnedIndices != null && spawnedIndices.Count > 0)
            {
                // Auto-attack the triggering object if enabled
                if (autoAttackTriggeringObject && triggeringObject != null)
                {
                    Transform targetTransform = triggeringObject.transform;
                    crowdController.SetAttackTargetForFaction(enemyCrowdIndex, targetTransform);
                }
                // Auto-set destination if enabled and not attacking
                else if (autoSetDestination)
                {
                    crowdController.SetDestination(spawnPos);
                }

                // Update state
                _hasTriggered = true;
                _lastTriggerTime = Time.time;

                // Note: We no longer disable the component when triggerOnce is true
                // The component stays enabled to continue tracking (for attack targets, etc.)
                // The CanTrigger() method prevents re-triggering by checking _hasTriggered
            }
            else
            {
                Debug.LogWarning($"[SpawnTrigger] {gameObject.name}: Failed to spawn agents!", this);
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

        public Color GetGizmoColor()
        {
            if (customGizmoColor != Color.clear)
                return customGizmoColor;

            // Auto-color based on faction index
            return FactionColors[enemyCrowdIndex % FactionColors.Length];
        }

        /// <summary>
        /// Manually reset the trigger so it can be activated again.
        /// Clears the triggered state and cooldown timer.
        /// </summary>
        public void ResetTrigger()
        {
            _hasTriggered = false;
            _lastTriggerTime = 0f;
        }

        /// <summary>
        /// Manually trigger spawning (bypasses filters and cooldowns).
        /// </summary>
        public void ManualTrigger()
        {
            if (crowdController == null)
            {
                Debug.LogWarning($"[SpawnTrigger] {gameObject.name}: No crowd controller assigned!", this);
                return;
            }

            // For manual trigger, use the player target if auto-attack is enabled
            GameObject target = null;
            if (autoAttackTriggeringObject)
            {
                Transform primaryTarget = crowdController.GetPrimaryPlayerTarget();
                if (primaryTarget != null)
                    target = primaryTarget.gameObject;
            }
            
            TriggerSpawn(target);
        }

        /// <summary>
        /// Syncs the collider component to match the triggerShape setting.
        /// Called from editor when triggerShape is changed.
        /// </summary>
        public void SyncColliderToShape()
        {
            Collider currentCollider = GetComponent<Collider>();
            
            // Check if collider type matches trigger shape
            bool needsReplacement = false;
            if (currentCollider != null)
            {
                needsReplacement = (triggerShape == TriggerShape.Box && !(currentCollider is BoxCollider)) ||
                                  (triggerShape == TriggerShape.Sphere && !(currentCollider is SphereCollider)) ||
                                  (triggerShape == TriggerShape.Capsule && !(currentCollider is CapsuleCollider));
            }
            
            if (currentCollider == null || needsReplacement)
            {
                // Remove old collider if present
                if (currentCollider != null)
                {
#if UNITY_EDITOR
                    DestroyImmediate(currentCollider);
#else
                    Destroy(currentCollider);
#endif
                }
                
                // Add new collider based on trigger shape
                Collider newCollider = null;
                switch (triggerShape)
                {
                    case TriggerShape.Box:
                        newCollider = gameObject.AddComponent<BoxCollider>();
                        break;
                    case TriggerShape.Sphere:
                        newCollider = gameObject.AddComponent<SphereCollider>();
                        break;
                    case TriggerShape.Capsule:
                        newCollider = gameObject.AddComponent<CapsuleCollider>();
                        break;
                }
                
                if (newCollider != null)
                {
                    newCollider.isTrigger = true;
                }
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmos)
                return;

            Color gizmoColor = GetGizmoColor();
            Gizmos.color = gizmoColor;

            // Draw trigger zone
            DrawTriggerGizmo();

            // Draw spawn location
            DrawSpawnLocationGizmo();

            // Draw connection line
            if (spawnLocation != null)
            {
                Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.3f);
                Gizmos.DrawLine(transform.position, spawnLocation.position);
            }
        }

        private void DrawTriggerGizmo()
        {
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            switch (triggerShape)
            {
                case TriggerShape.Box:
                    BoxCollider box = GetComponent<BoxCollider>();
                    Vector3 size = box != null ? box.size : Vector3.one * 5f;
                    Vector3 center = box != null ? box.center : Vector3.zero;
                    Gizmos.DrawWireCube(center, size);
                    break;

                case TriggerShape.Sphere:
                    SphereCollider sphere = GetComponent<SphereCollider>();
                    float radius = sphere != null ? sphere.radius : 2.5f;
                    Vector3 sphereCenter = sphere != null ? sphere.center : Vector3.zero;
                    Gizmos.DrawWireSphere(sphereCenter, radius);
                    break;

                case TriggerShape.Capsule:
                    CapsuleCollider capsule = GetComponent<CapsuleCollider>();
                    float capsuleRadius = capsule != null ? capsule.radius : 1f;
                    float capsuleHeight = capsule != null ? capsule.height : 3f;
                    Vector3 capsuleCenter = capsule != null ? capsule.center : Vector3.zero;
                    DrawWireCapsule(capsuleCenter, capsuleRadius, capsuleHeight);
                    break;
            }

            Gizmos.matrix = oldMatrix;
        }

        private void DrawSpawnLocationGizmo()
        {
            Vector3 spawnPos = GetSpawnPosition();
            Quaternion spawnRot = GetSpawnRotation();
            Color gizmoColor = GetGizmoColor();

            // Draw spawn position marker (larger sphere)
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(spawnPos, 0.5f);

            // Draw ground circle to show spawn area
            DrawWireCircle(spawnPos, spawnRot, 2f, 24);

            // Draw directional arrow showing formation facing direction
            DrawDirectionalArrow(spawnPos, spawnRot, 3f, gizmoColor);

            // Draw axis indicator at spawn location
            float axisLength = 1.5f;
            Vector3 forward = spawnRot * Vector3.forward;
            Vector3 right = spawnRot * Vector3.right;
            Vector3 up = spawnRot * Vector3.up;

            // Forward axis (Blue/Cyan - main direction indicator)
            Gizmos.color = new Color(0f, 0.8f, 1f, 1f);
            Gizmos.DrawLine(spawnPos, spawnPos + forward * axisLength);

            // Right axis (Red - shows formation width direction)
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
            Gizmos.DrawLine(spawnPos, spawnPos + right * axisLength * 0.7f);
            Gizmos.DrawLine(spawnPos, spawnPos - right * axisLength * 0.7f);

            // Label "FRONT" indicator
            Gizmos.color = gizmoColor;
        }

        private void DrawDirectionalArrow(Vector3 position, Quaternion rotation, float length, Color color)
        {
            Vector3 forward = rotation * Vector3.forward;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;

            Vector3 arrowTip = position + forward * length;
            Vector3 arrowBase = position + forward * (length * 0.3f);

            // Main arrow shaft (thick line using multiple parallel lines)
            Gizmos.color = color;
            float shaftWidth = 0.15f;
            Gizmos.DrawLine(position + right * shaftWidth, arrowBase + right * shaftWidth);
            Gizmos.DrawLine(position - right * shaftWidth, arrowBase - right * shaftWidth);
            Gizmos.DrawLine(position + up * shaftWidth, arrowBase + up * shaftWidth);
            Gizmos.DrawLine(position - up * shaftWidth, arrowBase - up * shaftWidth);

            // Arrow head (triangle)
            float headWidth = 0.6f;
            float headHeight = 0.3f;
            Vector3 headLeft = arrowBase + right * headWidth;
            Vector3 headRight = arrowBase - right * headWidth;
            Vector3 headTop = arrowBase + up * headHeight;
            Vector3 headBottom = arrowBase - up * headHeight;

            // Horizontal arrow head
            Gizmos.color = new Color(color.r, color.g, color.b, 1f);
            Gizmos.DrawLine(headLeft, arrowTip);
            Gizmos.DrawLine(headRight, arrowTip);
            Gizmos.DrawLine(headLeft, headRight);

            // Vertical arrow head
            Gizmos.DrawLine(headTop, arrowTip);
            Gizmos.DrawLine(headBottom, arrowTip);
            Gizmos.DrawLine(headTop, headBottom);

            // Cross at base of arrow head
            Gizmos.DrawLine(headLeft, headRight);
            Gizmos.DrawLine(headTop, headBottom);

            // Fill arrow head with additional lines for visibility
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

        private void DrawWireCapsule(Vector3 center, float radius, float height)
        {
            float cylinderHeight = Mathf.Max(0, height - radius * 2f);
            Vector3 top = center + Vector3.up * (cylinderHeight * 0.5f);
            Vector3 bottom = center - Vector3.up * (cylinderHeight * 0.5f);

            // Draw spheres at top and bottom
            Gizmos.DrawWireSphere(top, radius);
            Gizmos.DrawWireSphere(bottom, radius);

            // Draw connecting lines
            Gizmos.DrawLine(top + Vector3.forward * radius, bottom + Vector3.forward * radius);
            Gizmos.DrawLine(top - Vector3.forward * radius, bottom - Vector3.forward * radius);
            Gizmos.DrawLine(top + Vector3.right * radius, bottom + Vector3.right * radius);
            Gizmos.DrawLine(top - Vector3.right * radius, bottom - Vector3.right * radius);
        }

        private void OnDrawGizmosSelected()
        {
            if (!showGizmos)
                return;

            // Draw enhanced gizmos when selected
            Color gizmoColor = GetGizmoColor();
            gizmoColor.a = 0.2f;
            Gizmos.color = gizmoColor;

            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            // Draw filled trigger zone
            switch (triggerShape)
            {
                case TriggerShape.Box:
                    BoxCollider box = GetComponent<BoxCollider>();
                    Vector3 size = box != null ? box.size : Vector3.one * 5f;
                    Vector3 center = box != null ? box.center : Vector3.zero;
                    Gizmos.DrawCube(center, size);
                    break;

                case TriggerShape.Sphere:
                    SphereCollider sphere = GetComponent<SphereCollider>();
                    float radius = sphere != null ? sphere.radius : 2.5f;
                    Vector3 sphereCenter = sphere != null ? sphere.center : Vector3.zero;
                    Gizmos.DrawSphere(sphereCenter, radius);
                    break;
            }

            Gizmos.matrix = oldMatrix;
        }
#endif
    }
}
