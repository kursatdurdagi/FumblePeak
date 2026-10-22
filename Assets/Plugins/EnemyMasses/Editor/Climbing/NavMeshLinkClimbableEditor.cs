#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using Arawn.EnemyMasses.Runtime.Navigation.Climbing;

namespace Arawn.EnemyMasses.Editor
{
    [CustomEditor(typeof(NavMeshLinkClimbable))]
    public class NavMeshLinkClimbableEditor : UnityEditor.Editor
    {
        private NavMeshLinkClimbable _climbable;

        // Climb Surface Dimensions
        private SerializedProperty _width;
        private SerializedProperty _height;
        private SerializedProperty _baseYOffset;
        private SerializedProperty _wallSurfaceZOffset;

        // NavMesh Links
        private SerializedProperty _doubleSided;
        private SerializedProperty _autoLinkCount;
        private SerializedProperty _linkSpacing;
        private SerializedProperty _linkCount;
        private SerializedProperty _linkWidth;
        private SerializedProperty _agentTypeID;
        private SerializedProperty _navMeshAreaType;
        private SerializedProperty _bidirectional;
        private SerializedProperty _costModifier;

        // Climb Points
        private SerializedProperty _bottomOffset;
        private SerializedProperty _topOffset;
        private SerializedProperty _navMeshSnapRadius;
        private SerializedProperty _preserveHeightOffset;

        // Traversal Settings
        private SerializedProperty _useHeightBasedDuration;
        private SerializedProperty _climbSpeed;
        private SerializedProperty _traversalTime;

        // Animation Speed
        private SerializedProperty _syncAnimationToClimb;
        private SerializedProperty _animationSpeedOverride;
        private SerializedProperty _climbCurve;

        // Climb Style
        private SerializedProperty _climbStyle;
        private SerializedProperty _climbSteps;
        private SerializedProperty _stepPauseStrength;
        private SerializedProperty _randomizeRhythm;
        private SerializedProperty _rhythmVariation;

        // Multi-Agent Climbing
        private SerializedProperty _linksPerPosition;
        private SerializedProperty _climbPositionSpread;
        private SerializedProperty _climbDepthSpread;
        private SerializedProperty _climbSpeedVariation;

        // Debug
        private SerializedProperty _showDebug;
        private SerializedProperty _debugColor;
        private SerializedProperty _aStarDebugColor;

        // A* Pathfinding
        private SerializedProperty _createAStarLinks;
        private SerializedProperty _aStarGraphMask;
        private SerializedProperty _aStarPathfindingTag;
        private SerializedProperty _aStarCostFactor;
        private SerializedProperty _useAStarReflection;

        // Flow Field Links
        private SerializedProperty _createFlowFieldVirtualLinks;
        private SerializedProperty _flowFieldCostMultiplier;

        // Calculated values
        private SerializedProperty _calculatedLinkCount;
        private SerializedProperty _calculatedClimbDuration;
        private SerializedProperty _runtimeNavMeshLinkCount;
        private SerializedProperty _runtimeAStarLinkCount;

        // Foldout states
        private bool _showDimensions = true;
        private bool _showNavMeshLinks = true;
        private bool _showClimbPoints = false;
        private bool _showTraversal = true;
        private bool _showAnimation = true;
        private bool _showClimbStyle = false;
        private bool _showHordeMode = false;
        private bool _showDebugSection = false;
        private bool _showAStarLinks = false;

        private void OnEnable()
        {
            _climbable = (NavMeshLinkClimbable)target;

            // Climb Surface Dimensions
            _width = serializedObject.FindProperty("width");
            _height = serializedObject.FindProperty("height");
            _baseYOffset = serializedObject.FindProperty("baseYOffset");
            _wallSurfaceZOffset = serializedObject.FindProperty("wallSurfaceZOffset");

            // NavMesh Links
            _doubleSided = serializedObject.FindProperty("doubleSided");
            _autoLinkCount = serializedObject.FindProperty("autoLinkCount");
            _linkSpacing = serializedObject.FindProperty("linkSpacing");
            _linkCount = serializedObject.FindProperty("linkCount");
            _linkWidth = serializedObject.FindProperty("linkWidth");
            _agentTypeID = serializedObject.FindProperty("agentTypeID");
            _navMeshAreaType = serializedObject.FindProperty("navMeshAreaType");
            _bidirectional = serializedObject.FindProperty("bidirectional");
            _costModifier = serializedObject.FindProperty("costModifier");

            // Climb Points
            _bottomOffset = serializedObject.FindProperty("bottomOffset");
            _topOffset = serializedObject.FindProperty("topOffset");
            _navMeshSnapRadius = serializedObject.FindProperty("navMeshSnapRadius");
            _preserveHeightOffset = serializedObject.FindProperty("preserveHeightOffset");

            // Traversal Settings
            _useHeightBasedDuration = serializedObject.FindProperty("useHeightBasedDuration");
            _climbSpeed = serializedObject.FindProperty("climbSpeed");
            _traversalTime = serializedObject.FindProperty("traversalTime");

            // Animation Speed
            _syncAnimationToClimb = serializedObject.FindProperty("syncAnimationToClimb");
            _animationSpeedOverride = serializedObject.FindProperty("animationSpeedOverride");
            _climbCurve = serializedObject.FindProperty("climbCurve");

            // Climb Style
            _climbStyle = serializedObject.FindProperty("climbStyle");
            _climbSteps = serializedObject.FindProperty("climbSteps");
            _stepPauseStrength = serializedObject.FindProperty("stepPauseStrength");
            _randomizeRhythm = serializedObject.FindProperty("randomizeRhythm");
            _rhythmVariation = serializedObject.FindProperty("rhythmVariation");

            // Multi-Agent Climbing
            _linksPerPosition = serializedObject.FindProperty("linksPerPosition");
            _climbPositionSpread = serializedObject.FindProperty("climbPositionSpread");
            _climbDepthSpread = serializedObject.FindProperty("climbDepthSpread");
            _climbSpeedVariation = serializedObject.FindProperty("climbSpeedVariation");

            // Debug
            _showDebug = serializedObject.FindProperty("showDebug");
            _debugColor = serializedObject.FindProperty("debugColor");
            _aStarDebugColor = serializedObject.FindProperty("aStarDebugColor");

            // A* Pathfinding
            _createAStarLinks = serializedObject.FindProperty("createAStarLinks");
            _aStarGraphMask = serializedObject.FindProperty("aStarGraphMask");
            _aStarPathfindingTag = serializedObject.FindProperty("aStarPathfindingTag");
            _aStarCostFactor = serializedObject.FindProperty("aStarCostFactor");
            _useAStarReflection = serializedObject.FindProperty("useAStarReflection");
            _createFlowFieldVirtualLinks = serializedObject.FindProperty("createFlowFieldVirtualLinks");
            _flowFieldCostMultiplier = serializedObject.FindProperty("flowFieldCostMultiplier");

            // Calculated values
            _calculatedLinkCount = serializedObject.FindProperty("calculatedLinkCount");
            _calculatedClimbDuration = serializedObject.FindProperty("calculatedClimbDuration");
            _runtimeNavMeshLinkCount = serializedObject.FindProperty("runtimeNavMeshLinkCount");
            _runtimeAStarLinkCount = serializedObject.FindProperty("runtimeAStarLinkCount");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(5);
            DrawHeader();
            EditorGUILayout.Space(10);

            DrawDimensionsSection();
            EditorGUILayout.Space(3);

            DrawNavMeshLinksSection();
            EditorGUILayout.Space(3);

            DrawAStarLinksSection();
            EditorGUILayout.Space(3);

            DrawClimbPointsSection();
            EditorGUILayout.Space(3);

            DrawTraversalSection();
            EditorGUILayout.Space(3);

            DrawAnimationSection();
            EditorGUILayout.Space(3);

            DrawClimbStyleSection();
            EditorGUILayout.Space(3);

            DrawHordeModeSection();
            EditorGUILayout.Space(3);

            DrawDebugSection();
            EditorGUILayout.Space(10);

            DrawQuickActions();
            EditorGUILayout.Space(5);

            DrawRuntimeInfo();

            serializedObject.ApplyModifiedProperties();

            DrawAStarInfoBox();
        }

        private new void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };

            // Colored accent bar
            Color accentColor = _debugColor.colorValue;
            GUI.color = accentColor;
            GUILayout.Box("", GUILayout.Height(3), GUILayout.ExpandWidth(true));
            GUI.color = Color.white;

            EditorGUILayout.LabelField("NavMesh Link Climbable", titleStyle);

            // Summary info
            GUIStyle summaryStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            
            float climbDuration = _useHeightBasedDuration.boolValue 
                ? _height.floatValue / _climbSpeed.floatValue 
                : _traversalTime.floatValue;
            int linkCountDisplay = _autoLinkCount.boolValue 
                ? Mathf.Max(1, Mathf.CeilToInt(_width.floatValue / _linkSpacing.floatValue))
                : _linkCount.intValue;
            int totalLinks = linkCountDisplay * (_doubleSided.boolValue ? 2 : 1) * _linksPerPosition.intValue;
            
            EditorGUILayout.LabelField($"{_width.floatValue:F1}m × {_height.floatValue:F1}m  |  {totalLinks} links  |  {climbDuration:F1}s climb time", summaryStyle);

            EditorGUILayout.EndVertical();
        }

        private void DrawDimensionsSection()
        {
            _showDimensions = EditorGUILayout.BeginFoldoutHeaderGroup(_showDimensions, "Climb Surface Dimensions");
            if (_showDimensions)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                
                EditorGUILayout.PropertyField(_width, new GUIContent("Width", "Width of the climbable surface (X axis)"));
                EditorGUILayout.PropertyField(_height, new GUIContent("Height", "Height of the climbable surface (Y axis)"));
                
                EditorGUILayout.Space(5);
                
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField("Pivot Offsets", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(_baseYOffset, new GUIContent("Base Y Offset", "Vertical offset from pivot to wall base"));
                    EditorGUILayout.PropertyField(_wallSurfaceZOffset, new GUIContent("Surface Z Offset", "Forward offset from pivot to wall surface"));
                }

                EditorGUILayout.Space(5);

                if (GUILayout.Button("Auto-Detect from Collider/Renderer", GUILayout.Height(24)))
                {
                    Undo.RecordObject(_climbable, "Auto-Detect Dimensions");
                    _climbable.AutoDetectDimensions();
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawNavMeshLinksSection()
        {
            _showNavMeshLinks = EditorGUILayout.BeginFoldoutHeaderGroup(_showNavMeshLinks, "NavMesh Links");
            if (_showNavMeshLinks)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.PropertyField(_doubleSided, new GUIContent("Double Sided", "Create links on both sides of the wall"));
                EditorGUILayout.PropertyField(_bidirectional, new GUIContent("Bidirectional", "Agents can climb up AND down"));
                
                EditorGUILayout.Space(5);
                
                EditorGUILayout.PropertyField(_autoLinkCount, new GUIContent("Auto Link Count", "Calculate link count based on width and spacing"));
                
                if (_autoLinkCount.boolValue)
                {
                    EditorGUILayout.PropertyField(_linkSpacing, new GUIContent("Link Spacing", "Maximum spacing between links"));
                    int autoCount = Mathf.Max(1, Mathf.CeilToInt(_width.floatValue / _linkSpacing.floatValue));
                    EditorGUILayout.LabelField($"Calculated: {autoCount} links per side", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.PropertyField(_linkCount, new GUIContent("Link Count", "Number of links per side"));
                }

                EditorGUILayout.PropertyField(_linkWidth, new GUIContent("Link Width", "Width of each individual link"));
                
                EditorGUILayout.Space(5);
                
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField("NavMesh Settings", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(_agentTypeID, new GUIContent("Agent Type ID", "Must match your NavMeshAgent's agent type (0 = Humanoid)"));
                    EditorGUILayout.PropertyField(_navMeshAreaType, new GUIContent("Area Type", "NavMesh area for cost calculation"));
                    EditorGUILayout.PropertyField(_costModifier, new GUIContent("Cost Modifier", "Path cost multiplier (higher = less preferred)"));
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawAStarLinksSection()
        {
            _showAStarLinks = EditorGUILayout.BeginFoldoutHeaderGroup(_showAStarLinks, "A* + Flow Field Links");
            if (_showAStarLinks)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.LabelField("A* Pathfinding", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_createAStarLinks, new GUIContent("Create A* Links", "Create NodeLink2 links when A* is installed"));
                EditorGUILayout.PropertyField(_useAStarReflection, new GUIContent("Use Reflection", "Avoid hard dependency on A* types"));
                EditorGUILayout.PropertyField(_aStarGraphMask, new GUIContent("Graph Mask", "Use -1 for Everything, or bitmask"));
                EditorGUILayout.PropertyField(_aStarPathfindingTag, new GUIContent("Pathfinding Tag", "A* tag for traversal"));
                EditorGUILayout.PropertyField(_aStarCostFactor, new GUIContent("Cost Factor", "Traversal cost multiplier"));

                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Flow Field", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(_createFlowFieldVirtualLinks, new GUIContent("Create FlowField Virtual Links", "Emit climb links for Flow Field integration even without NavMesh/A* link components"));
                EditorGUILayout.PropertyField(_flowFieldCostMultiplier, new GUIContent("FlowField Cost Multiplier", "Transition cost multiplier used by Flow Field climb links"));

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawClimbPointsSection()
        {
            _showClimbPoints = EditorGUILayout.BeginFoldoutHeaderGroup(_showClimbPoints, "Climb Points (Advanced)");
            if (_showClimbPoints)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.HelpBox("These offsets define where agents enter/exit the climb. They snap to the nearest NavMesh surface.", MessageType.Info);

                EditorGUILayout.PropertyField(_bottomOffset, new GUIContent("Bottom Offset", "Offset from wall base for entry points"));
                EditorGUILayout.PropertyField(_topOffset, new GUIContent("Top Offset", "Offset from wall top for exit points"));
                EditorGUILayout.PropertyField(_navMeshSnapRadius, new GUIContent("Snap Radius", "Search radius for NavMesh snapping"));
                EditorGUILayout.PropertyField(_preserveHeightOffset, new GUIContent("Preserve Height", "Keep original Y after snapping"));

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawTraversalSection()
        {
            _showTraversal = EditorGUILayout.BeginFoldoutHeaderGroup(_showTraversal, "Traversal Settings");
            if (_showTraversal)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.PropertyField(_useHeightBasedDuration, new GUIContent("Height-Based Duration", "Calculate climb time from height and speed"));

                if (_useHeightBasedDuration.boolValue)
                {
                    EditorGUILayout.PropertyField(_climbSpeed, new GUIContent("Climb Speed", "Units per second"));
                    float duration = _height.floatValue / Mathf.Max(0.1f, _climbSpeed.floatValue);
                    EditorGUILayout.LabelField($"Calculated Duration: {duration:F2}s", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.PropertyField(_traversalTime, new GUIContent("Traversal Time", "Fixed climb duration in seconds"));
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(_climbCurve, new GUIContent("Climb Curve", "Easing curve for vertical movement"));

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawAnimationSection()
        {
            _showAnimation = EditorGUILayout.BeginFoldoutHeaderGroup(_showAnimation, "Animation");
            if (_showAnimation)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.PropertyField(_syncAnimationToClimb, new GUIContent("Sync to Climb", "Auto-calculate animation speed to match climb duration"));

                if (!_syncAnimationToClimb.boolValue)
                {
                    EditorGUILayout.PropertyField(_animationSpeedOverride, new GUIContent("Speed Override", "Fixed animation playback speed"));
                }
                else
                {
                    EditorGUILayout.HelpBox("Animation speed will be calculated automatically so the climb animation loops match the climb duration.", MessageType.Info);
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawClimbStyleSection()
        {
            _showClimbStyle = EditorGUILayout.BeginFoldoutHeaderGroup(_showClimbStyle, "Climb Style");
            if (_showClimbStyle)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.PropertyField(_climbStyle, new GUIContent("Style", "Movement style during climb"));

                ClimbStyle style = (ClimbStyle)_climbStyle.enumValueIndex;
                
                if (style == ClimbStyle.Human)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Human/Rhythmic Settings", EditorStyles.boldLabel);
                    
                    EditorGUILayout.PropertyField(_climbSteps, new GUIContent("Climb Steps", "Number of 'pull' motions during climb"));
                    EditorGUILayout.PropertyField(_stepPauseStrength, new GUIContent("Pause Strength", "How long to pause between steps"));
                    EditorGUILayout.PropertyField(_randomizeRhythm, new GUIContent("Randomize Rhythm", "Add variation per agent"));
                    
                    if (_randomizeRhythm.boolValue)
                    {
                        EditorGUILayout.PropertyField(_rhythmVariation, new GUIContent("Variation Amount", "How much rhythm varies"));
                    }
                }
                else if (style == ClimbStyle.Machine)
                {
                    EditorGUILayout.HelpBox("Machine style: Smooth, constant velocity like an elevator.", MessageType.None);
                }
                else
                {
                    EditorGUILayout.HelpBox("Custom style: Uses only the Climb Curve with no additional processing.", MessageType.None);
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawHordeModeSection()
        {
            _showHordeMode = EditorGUILayout.BeginFoldoutHeaderGroup(_showHordeMode, "Horde Mode (Multi-Agent)");
            if (_showHordeMode)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.HelpBox("These settings allow multiple agents to climb simultaneously at the same position, creating a 'zombie horde' effect.", MessageType.Info);

                EditorGUILayout.PropertyField(_linksPerPosition, new GUIContent("Links Per Position", "Overlapping links for simultaneous climbing (1 = single-file)"));
                
                if (_linksPerPosition.intValue > 1)
                {
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Visual Spread", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(_climbPositionSpread, new GUIContent("Horizontal Spread", "Left/right variation"));
                    EditorGUILayout.PropertyField(_climbDepthSpread, new GUIContent("Depth Spread", "Forward/back variation"));
                    
                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("Speed Variation", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(_climbSpeedVariation, new GUIContent("Speed Variation", "Random speed differences (0 = uniform, 1 = chaotic)"));
                }

                int totalLinks = (_autoLinkCount.boolValue 
                    ? Mathf.Max(1, Mathf.CeilToInt(_width.floatValue / _linkSpacing.floatValue))
                    : _linkCount.intValue) * (_doubleSided.boolValue ? 2 : 1) * _linksPerPosition.intValue;
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField($"Total Links: {totalLinks}", EditorStyles.boldLabel);

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawDebugSection()
        {
            _showDebugSection = EditorGUILayout.BeginFoldoutHeaderGroup(_showDebugSection, "Debug Visualization");
            if (_showDebugSection)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.PropertyField(_showDebug, new GUIContent("Show Gizmos", "Display debug visualization in Scene view"));
                
                if (_showDebug.boolValue)
                {
                    EditorGUILayout.PropertyField(_debugColor, new GUIContent("Gizmo Color", "Color for debug visualization"));
                    EditorGUILayout.PropertyField(_aStarDebugColor, new GUIContent("A* Gizmo Color", "Color for A* link gizmos"));
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawQuickActions()
        {
            EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Auto-Detect", GUILayout.Height(28)))
            {
                Undo.RecordObject(_climbable, "Auto-Detect Dimensions");
                _climbable.AutoDetectDimensions();
            }
            
            GUI.enabled = Application.isPlaying;
            if (GUILayout.Button("Validate Links", GUILayout.Height(28)))
            {
                _climbable.ValidateLinkConnections();
            }
            
            if (GUILayout.Button("Test Path", GUILayout.Height(28)))
            {
                _climbable.TestPathAcrossWall();
            }
            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to validate links and test pathfinding.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRuntimeInfo()
        {
            if (!Application.isPlaying) return;

            EditorGUILayout.LabelField("Runtime Info", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField($"Calculated Link Count: {_calculatedLinkCount.intValue}");
            EditorGUILayout.LabelField($"Calculated Climb Duration: {_calculatedClimbDuration.floatValue:F2}s");
            EditorGUILayout.LabelField($"Runtime NavMesh Links: {_runtimeNavMeshLinkCount.intValue}");
            EditorGUILayout.LabelField($"Runtime A* Links: {_runtimeAStarLinkCount.intValue}");
            
            // Could add more runtime stats here like:
            // - Number of agents currently climbing
            // - Link utilization
            
            EditorGUILayout.EndVertical();
        }

        private void DrawAStarInfoBox()
        {
            if (!IsAStarPresent()) return;

            EditorGUILayout.HelpBox(
                "A* agents rely on proximity climbing. Enable 'useProximityDetection' on the NavMeshLinkTraversalHandler (not on this component).",
                MessageType.Info);

            var handler = FindTraversalHandler();
            using (new EditorGUI.DisabledScope(handler == null))
            {
                if (GUILayout.Button("Select NavMeshLinkTraversalHandler"))
                {
                    Selection.activeObject = handler;
                }
            }
        }

        private static bool IsAStarPresent()
        {
            return Type.GetType("Pathfinding.NodeLink2, com.arongranberg.astar") != null
                   || Type.GetType("Pathfinding.NodeLink2, AstarPathfindingProject") != null
                   || Type.GetType("Pathfinding.NodeLink2, Assembly-CSharp") != null;
        }

        private static NavMeshLinkTraversalHandler FindTraversalHandler()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<NavMeshLinkTraversalHandler>();
#else
            return UnityEngine.Object.FindObjectOfType<NavMeshLinkTraversalHandler>();
#endif
        }
    }
}
#endif
