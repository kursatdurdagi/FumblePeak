#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Arawn.EnemyMasses.Runtime.UI;

#if TMP_PRESENT
using UITextComponent = TMPro.TextMeshProUGUI;
#else
using UITextComponent = UnityEngine.UI.Text;
#endif

namespace Arawn.EnemyMasses.Runtime.Editor
{
    /// <summary>
    /// Provides convenient menu items for creating Enemy Masses components.
    /// </summary>
    public static class EnemyMassesMenuItems
    {
        [MenuItem("GameObject/Enemy Masses/Crowd Controller", false, 10)]
        public static void CreateCrowdController(MenuCommand menuCommand)
        {
            // Check if controller already exists
            var existingController = Object.FindFirstObjectByType<EnemyMassesCrowdController>();
            if (existingController != null)
            {
                if (!EditorUtility.DisplayDialog("Controller Exists",
                    "An EnemyMassesCrowdController already exists in the scene.\n\n" +
                    "Do you want to select the existing one?",
                    "Select Existing", "Create New Anyway"))
                {
                    // Create new anyway
                    CreateNewController();
                    return;
                }
                
                Selection.activeGameObject = existingController.gameObject;
                EditorGUIUtility.PingObject(existingController.gameObject);
                return;
            }
            
            CreateNewController();
        }
        
        static void CreateNewController()
        {
            var controllerGO = new GameObject("EnemyMassesCrowdController");
            controllerGO.AddComponent<EnemyMassesCrowdController>();
            
            Selection.activeGameObject = controllerGO;
            EditorGUIUtility.PingObject(controllerGO);
            
            Undo.RegisterCreatedObjectUndo(controllerGO, "Create Enemy Masses Controller");
            
            Debug.Log("[Enemy Masses] Created EnemyMassesCrowdController");
        }

        [MenuItem("GameObject/Enemy Masses/Spawner/Spawn Interval", false, 20)]
        public static void CreateSpawnInterval(MenuCommand menuCommand)
        {
            CreateComponentGameObject<EnemyMassesSpawnInterval>("EnemyMassesSpawnInterval", menuCommand);
        }

        [MenuItem("GameObject/Enemy Masses/Spawner/Spawn Trigger", false, 21)]
        public static void CreateSpawnTrigger(MenuCommand menuCommand)
        {
            CreateComponentGameObject<EnemyMassesSpawnTrigger>("EnemyMassesSpawnTrigger", menuCommand);
        }

        [MenuItem("GameObject/Enemy Masses/Spawner/Start Spawner", false, 22)]
        public static void CreateStartSpawner(MenuCommand menuCommand)
        {
            CreateComponentGameObject<EnemyMassesStartSpawner>("EnemyMassesStartSpawner", menuCommand);
        }

        [MenuItem("GameObject/Enemy Masses/Spawner/Spawn Patrol", false, 23)]
        public static void CreateSpawnPatrol(MenuCommand menuCommand)
        {
            CreateComponentGameObject<EnemyMassesSpawnPatrol>("EnemyMassesSpawnPatrol", menuCommand);
        }

        [MenuItem("GameObject/Enemy Masses/Spawner/Spawn Around Target", false, 24)]
        public static void CreateSpawnAroundTarget(MenuCommand menuCommand)
        {
            CreateComponentGameObject<EnemyMassesSpawnAroundTarget>("EnemyMassesSpawnAroundTarget", menuCommand);
        }

        [MenuItem("GameObject/Enemy Masses/Combat/Combat Effect Zone", false, 30)]
        public static void CreateCombatEffectZone(MenuCommand menuCommand)
        {
            CreateComponentGameObject<EnemyMassesCombatEffectZone>("EnemyMassesCombatEffectZone", menuCommand);
        }

        [MenuItem("GameObject/Enemy Masses/Placement/Static Wall Placement", false, 35)]
        public static void CreateWallPlacementZone(MenuCommand menuCommand)
        {
            CreateComponentGameObject<EnemyMassesWallPlacementZone>("WallPlacementZone", menuCommand);
        }

        [MenuItem("GameObject/Enemy Masses/Placement/Dynamic Wall Placement", false, 36)]
        public static void CreateDynamicWallPlacement(MenuCommand menuCommand)
        {
            // --- Root GameObject with the runtime component ---
            var rootGO = new GameObject("DynamicWallPlacement");
            var runtime = rootGO.AddComponent<EnemyMassesDynamicWallPlacementRuntime>();

            GameObjectUtility.SetParentAndAlign(rootGO, menuCommand.context as GameObject);

            // --- Child zone (Manual arrangement, managed by the runtime) ---
            var zoneGO = new GameObject("DynamicWallPlacementZone");
            zoneGO.transform.SetParent(rootGO.transform, false);
            var zone = zoneGO.AddComponent<EnemyMassesWallPlacementZone>();

            // Configure zone as Manual for runtime dynamic spots
            zone.ConfigureAsRuntimeManualZone(false);

            // Auto-find the Crowd Controller for the zone
            var crowdController = Object.FindFirstObjectByType<EnemyMassesCrowdController>();
            if (crowdController != null)
            {
                var zoneSO = new SerializedObject(zone);
                zoneSO.FindProperty("crowdController").objectReferenceValue = crowdController;
                zoneSO.ApplyModifiedPropertiesWithoutUndo();
            }

            // --- Wire references via SerializedObject so private fields are set ---
            var runtimeSO = new SerializedObject(runtime);
            runtimeSO.FindProperty("runtimeZone").objectReferenceValue = zone;

            // Auto-find the RTS controller in the scene
            var rtsController = Object.FindFirstObjectByType<EnemyMassesRTSController>();
            if (rtsController != null)
            {
                runtimeSO.FindProperty("rtsController").objectReferenceValue = rtsController;
            }

            runtimeSO.ApplyModifiedPropertiesWithoutUndo();

            // --- Register undo ---
            Undo.RegisterCreatedObjectUndo(rootGO, "Create Dynamic Wall Placement");

            // Mark dirty
            EditorUtility.SetDirty(runtime);
            EditorUtility.SetDirty(zone);

            Selection.activeGameObject = rootGO;
            EditorGUIUtility.PingObject(rootGO);

            string rtsStatus = rtsController != null
                ? $"RTS Controller '{rtsController.name}' auto-linked."
                : "No RTS Controller found in scene — assign one manually.";

            string crowdStatus = crowdController != null
                ? $"Crowd Controller '{crowdController.name}' auto-linked."
                : "No Crowd Controller found in scene — assign one manually on the zone.";

            Debug.Log($"[Enemy Masses] Created Dynamic Wall Placement setup. {rtsStatus} {crowdStatus}\n" +
                      "Add wall BoxColliders to the 'Wall Box Colliders' list to complete setup.");
        }

        [MenuItem("GameObject/Enemy Masses/RTS/World Anchor", false, 40)]
        public static void CreateWorldAnchor(MenuCommand menuCommand)
        {
            CreateComponentGameObject<EnemyMassesRTSWorldAnchor>("EnemyMassesRTSWorldAnchor", menuCommand);
        }

        [MenuItem("GameObject/Enemy Masses/RTS/AI Controller", false, 41)]
        public static void CreateRTSAI(MenuCommand menuCommand)
        {
            CreateComponentGameObject<EnemyMassesRTSAI>("EnemyMassesRTSAI", menuCommand);
        }

        [MenuItem("GameObject/Enemy Masses/UI/Agent Skill Bar", false, 50)]
        public static void CreateAgentSkillBar(MenuCommand menuCommand)
        {
            // Canvas root
            var canvasGO = new GameObject("AgentSkillBar");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // CanvasGroup for fade
            canvasGO.AddComponent<CanvasGroup>();

            // Bar root (anchored to bottom center)
            var barRootGO = new GameObject("BarRoot", typeof(RectTransform));
            barRootGO.transform.SetParent(canvasGO.transform, false);
            var barRootRect = barRootGO.GetComponent<RectTransform>();
            barRootRect.anchorMin = new Vector2(0.5f, 0f);
            barRootRect.anchorMax = new Vector2(0.5f, 0f);
            barRootRect.pivot = new Vector2(0.5f, 0f);
            barRootRect.anchoredPosition = new Vector2(0, 20);
            barRootRect.sizeDelta = new Vector2(700, 80);

            // Bar background
            var barBgImg = barRootGO.AddComponent<Image>();
            barBgImg.color = new Color(0.05f, 0.05f, 0.08f, 0.85f);

            // Slot container with horizontal layout
            var slotContainerGO = new GameObject("SlotContainer", typeof(RectTransform));
            slotContainerGO.transform.SetParent(barRootGO.transform, false);
            var slotContainerRect = slotContainerGO.GetComponent<RectTransform>();
            slotContainerRect.anchorMin = Vector2.zero;
            slotContainerRect.anchorMax = Vector2.one;
            slotContainerRect.offsetMin = Vector2.zero;
            slotContainerRect.offsetMax = Vector2.zero;
            var hlg = slotContainerGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.padding = new RectOffset(8, 8, 8, 8);
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            slotContainerGO.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Tooltip panel (hidden by default)
            var tooltipGO = new GameObject("TooltipPanel", typeof(RectTransform));
            tooltipGO.transform.SetParent(canvasGO.transform, false);
            var tooltipRect = tooltipGO.GetComponent<RectTransform>();
            tooltipRect.anchorMin = new Vector2(0.5f, 0f);
            tooltipRect.anchorMax = new Vector2(0.5f, 0f);
            tooltipRect.pivot = new Vector2(0.5f, 0f);
            tooltipRect.anchoredPosition = new Vector2(0, 110);
            tooltipRect.sizeDelta = new Vector2(320, 200);
            var tooltipBg = tooltipGO.AddComponent<Image>();
            tooltipBg.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);

            // VerticalLayoutGroup required so ContentSizeFitter can compute preferred size from children
            var tooltipLayout = tooltipGO.AddComponent<VerticalLayoutGroup>();
            tooltipLayout.padding = new RectOffset(10, 10, 10, 10);
            tooltipLayout.childForceExpandWidth = true;
            tooltipLayout.childForceExpandHeight = false;
            tooltipLayout.childControlWidth = true;
            tooltipLayout.childControlHeight = true;
            tooltipLayout.childAlignment = TextAnchor.UpperLeft;

            var tooltipFitter = tooltipGO.AddComponent<ContentSizeFitter>();
            tooltipFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Tooltip text — offsets handled by VerticalLayoutGroup padding
            var tooltipTextGO = new GameObject("TooltipText", typeof(RectTransform));
            tooltipTextGO.transform.SetParent(tooltipGO.transform, false);
            var tooltipTextRect = tooltipTextGO.GetComponent<RectTransform>();
            tooltipTextRect.anchorMin = Vector2.zero;
            tooltipTextRect.anchorMax = Vector2.one;
            tooltipTextRect.offsetMin = Vector2.zero;
            tooltipTextRect.offsetMax = Vector2.zero;
            var tooltipTMP = tooltipTextGO.AddComponent<UITextComponent>();
            tooltipTMP.fontSize = 12;
            tooltipTMP.color = new Color(0.9f, 0.9f, 0.85f, 1f);
#if TMP_PRESENT
            tooltipTMP.enableWordWrapping = true;
            tooltipTMP.richText = true;
#else
            tooltipTMP.supportRichText = true;
            tooltipTMP.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltipTMP.verticalOverflow = VerticalWrapMode.Overflow;
            tooltipTMP.alignment = TextAnchor.UpperLeft;
#endif
            tooltipGO.SetActive(false);

            // Add the AgentSkillBarUI component
            var skillBar = canvasGO.AddComponent<AgentSkillBarUI>();
            skillBar.skillBarRoot = barRootRect;
            skillBar.slotContainer = slotContainerRect;
            skillBar.barBackground = barBgImg;
            skillBar.tooltipPanel = tooltipRect;
            skillBar.tooltipText = tooltipTMP;

            // Assign targeting cursor (Runtime path so it's available in builds)
            var targetCursor = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Plugins/EnemyMasses/Runtime/UI/Icons/target.png");
            if (targetCursor != null)
            {
                skillBar.targetingCursorTexture = targetCursor;
                skillBar.targetingCursorHotspot = new Vector2(16, 16);
            }

            // Populate default keybindings
            skillBar.PopulateDefaultBindings(10);

            // Register
            GameObjectUtility.SetParentAndAlign(canvasGO, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create Agent Skill Bar");
            Selection.activeGameObject = canvasGO;

            Debug.Log("[Enemy Masses] Created Agent Skill Bar UI. Assign an RTS Controller and Crowd Controller, or they will be auto-discovered at runtime.");
        }

        [MenuItem("GameObject/Enemy Masses/UI/RTS Command Panel", false, 51)]
        public static void CreateRTSCommandPanel(MenuCommand menuCommand)
        {
            // Canvas root
            var canvasGO = new GameObject("RTSCommandPanel");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 11;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            // Panel root (anchored to bottom-left)
            var panelRootGO = new GameObject("PanelRoot", typeof(RectTransform));
            panelRootGO.transform.SetParent(canvasGO.transform, false);
            var panelRootRect = panelRootGO.GetComponent<RectTransform>();
            panelRootRect.anchorMin = new Vector2(0f, 0f);
            panelRootRect.anchorMax = new Vector2(0f, 0f);
            panelRootRect.pivot = new Vector2(0f, 0f);
            panelRootRect.anchoredPosition = new Vector2(20, 20);
            panelRootRect.sizeDelta = new Vector2(386, 50);

            // Panel background
            var panelBg = panelRootGO.AddComponent<Image>();
            panelBg.color = new Color(0.05f, 0.05f, 0.08f, 0.85f);

            // Button container with horizontal layout
            var buttonContainerGO = new GameObject("ButtonContainer", typeof(RectTransform));
            buttonContainerGO.transform.SetParent(panelRootGO.transform, false);
            var btnContRect = buttonContainerGO.GetComponent<RectTransform>();
            btnContRect.anchorMin = Vector2.zero;
            btnContRect.anchorMax = Vector2.one;
            btnContRect.offsetMin = Vector2.zero;
            btnContRect.offsetMax = Vector2.zero;
            var hlg = buttonContainerGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.padding = new RectOffset(6, 6, 6, 6);
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            // Strict mode toggle
            var strictGO = CreateCommandToggle("StrictToggle", buttonContainerGO.transform, "S", new Color(0.8f, 0.5f, 0.2f, 1f));
            var strictToggle = strictGO.GetComponent<Toggle>();

            // Stance buttons
            var offGO = CreateCommandButton("OffensiveButton", buttonContainerGO.transform, "O", new Color(0.9f, 0.3f, 0.3f, 1f));
            var defGO = CreateCommandButton("DefensiveButton", buttonContainerGO.transform, "D", new Color(0.3f, 0.6f, 0.9f, 1f));
            var holdGO = CreateCommandButton("HoldButton", buttonContainerGO.transform, "H", new Color(0.9f, 0.8f, 0.3f, 1f));
            var passGO = CreateCommandButton("PassiveButton", buttonContainerGO.transform, "P", new Color(0.5f, 0.8f, 0.4f, 1f));
            var stopGO = CreateCommandButton("StopButton", buttonContainerGO.transform, "X", new Color(0.7f, 0.2f, 0.2f, 1f));
            var followGO = CreateCommandButton("FollowButton", buttonContainerGO.transform, "F", new Color(0.3f, 0.9f, 0.7f, 1f));

            // ── Tooltip Panel ──
            // Floating panel positioned above buttons, hidden by default
            var tooltipPanelGO = new GameObject("TooltipPanel", typeof(RectTransform), typeof(Image));
            tooltipPanelGO.transform.SetParent(panelRootGO.transform, false);
            var tooltipRect = tooltipPanelGO.GetComponent<RectTransform>();
            // Anchor to bottom-left of panel root, positioned above the buttons
            tooltipRect.anchorMin = new Vector2(0.5f, 1f);
            tooltipRect.anchorMax = new Vector2(0.5f, 1f);
            tooltipRect.pivot = new Vector2(0.5f, 0f);
            tooltipRect.anchoredPosition = new Vector2(0, 8);
            tooltipRect.sizeDelta = new Vector2(120, 28);

            var tooltipBg = tooltipPanelGO.GetComponent<Image>();
            tooltipBg.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);

            // Tooltip text
            var tooltipTextGO = new GameObject("TooltipText", typeof(RectTransform), typeof(UITextComponent));
            tooltipTextGO.transform.SetParent(tooltipPanelGO.transform, false);
            var tooltipTextRect = tooltipTextGO.GetComponent<RectTransform>();
            tooltipTextRect.anchorMin = Vector2.zero;
            tooltipTextRect.anchorMax = Vector2.one;
            tooltipTextRect.offsetMin = new Vector2(6, 2);
            tooltipTextRect.offsetMax = new Vector2(-6, -2);
            var tooltipTMP = tooltipTextGO.GetComponent<UITextComponent>();
            tooltipTMP.fontSize = 12;
            tooltipTMP.color = new Color(0.9f, 0.9f, 0.85f, 1f);
#if TMP_PRESENT
            tooltipTMP.alignment = TMPro.TextAlignmentOptions.Center;
            tooltipTMP.enableWordWrapping = false;
            tooltipTMP.overflowMode = TMPro.TextOverflowModes.Overflow;
#else
            tooltipTMP.alignment = TextAnchor.MiddleCenter;
            tooltipTMP.horizontalOverflow = HorizontalWrapMode.Overflow;
            tooltipTMP.verticalOverflow = VerticalWrapMode.Overflow;
#endif

            // Content size fitter so tooltip auto-resizes to text width
            var tooltipHFitter = tooltipPanelGO.AddComponent<ContentSizeFitter>();
            tooltipHFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            tooltipHFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var tooltipHLG = tooltipPanelGO.AddComponent<HorizontalLayoutGroup>();
            tooltipHLG.padding = new RectOffset(10, 10, 4, 4);
            tooltipHLG.childAlignment = TextAnchor.MiddleCenter;
            tooltipHLG.childForceExpandWidth = false;
            tooltipHLG.childForceExpandHeight = false;
            tooltipHLG.childControlWidth = true;
            tooltipHLG.childControlHeight = true;

            tooltipPanelGO.SetActive(false);

            // Add the RTSSelectionCommandPanel component
            var panel = canvasGO.AddComponent<RTSSelectionCommandPanel>();
            panel.panelRoot = panelRootGO;
            panel.strictModeToggle = strictToggle;
            panel.offensiveStrictButton = offGO.GetComponent<Button>();
            panel.defensiveStrictButton = defGO.GetComponent<Button>();
            panel.holdStrictButton = holdGO.GetComponent<Button>();
            panel.passiveButton = passGO.GetComponent<Button>();
            panel.stopButton = stopGO.GetComponent<Button>();
            panel.followButton = followGO.GetComponent<Button>();
            panel.tooltipPanel = tooltipRect;
            panel.tooltipText = tooltipTMP;

            // Register
            GameObjectUtility.SetParentAndAlign(canvasGO, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create RTS Command Panel");
            Selection.activeGameObject = canvasGO;

            Debug.Log("[Enemy Masses] Created RTS Command Panel. Assign an RTS Controller or it will be auto-discovered at runtime.");
        }

        [MenuItem("GameObject/Enemy Masses/UI/Status Effect Info UI", false, 52)]
        public static void CreateStatusEffectInfoUI(MenuCommand menuCommand)
        {
            // Canvas root
            var canvasGO = new GameObject("StatusEffectInfoUI");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 12;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
            canvasGO.AddComponent<CanvasGroup>();

            // Bar root (anchored to top-center, below potential skill bar area)
            var barRootGO = new GameObject("BarRoot", typeof(RectTransform));
            barRootGO.transform.SetParent(canvasGO.transform, false);
            var barRootRect = barRootGO.GetComponent<RectTransform>();
            barRootRect.anchorMin = new Vector2(0.5f, 1f);
            barRootRect.anchorMax = new Vector2(0.5f, 1f);
            barRootRect.pivot = new Vector2(0.5f, 1f);
            barRootRect.anchoredPosition = new Vector2(0, -10);
            barRootRect.sizeDelta = new Vector2(500, 50);

            // Bar background
            var barBgImg = barRootGO.AddComponent<Image>();
            barBgImg.color = new Color(0.05f, 0.05f, 0.08f, 0.75f);

            // Slot container with horizontal layout
            var slotContainerGO = new GameObject("SlotContainer", typeof(RectTransform));
            slotContainerGO.transform.SetParent(barRootGO.transform, false);
            var slotContainerRect = slotContainerGO.GetComponent<RectTransform>();
            slotContainerRect.anchorMin = Vector2.zero;
            slotContainerRect.anchorMax = Vector2.one;
            slotContainerRect.offsetMin = Vector2.zero;
            slotContainerRect.offsetMax = Vector2.zero;
            var hlg = slotContainerGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.padding = new RectOffset(6, 6, 6, 6);
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            slotContainerGO.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Tooltip panel (hidden by default)
            var tooltipGO = new GameObject("TooltipPanel", typeof(RectTransform));
            tooltipGO.transform.SetParent(canvasGO.transform, false);
            var tooltipRect = tooltipGO.GetComponent<RectTransform>();
            tooltipRect.anchorMin = new Vector2(0.5f, 1f);
            tooltipRect.anchorMax = new Vector2(0.5f, 1f);
            tooltipRect.pivot = new Vector2(0.5f, 1f);
            tooltipRect.anchoredPosition = new Vector2(0, -70);
            tooltipRect.sizeDelta = new Vector2(280, 80);
            var tooltipBg = tooltipGO.AddComponent<Image>();
            tooltipBg.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);

            var tooltipLayout = tooltipGO.AddComponent<VerticalLayoutGroup>();
            tooltipLayout.padding = new RectOffset(8, 8, 6, 6);
            tooltipLayout.childForceExpandWidth = true;
            tooltipLayout.childForceExpandHeight = false;
            tooltipLayout.childControlWidth = true;
            tooltipLayout.childControlHeight = true;
            tooltipLayout.childAlignment = TextAnchor.UpperLeft;

            var tooltipFitter = tooltipGO.AddComponent<ContentSizeFitter>();
            tooltipFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Tooltip text
            var tooltipTextGO = new GameObject("TooltipText", typeof(RectTransform));
            tooltipTextGO.transform.SetParent(tooltipGO.transform, false);
            var tooltipTextRect = tooltipTextGO.GetComponent<RectTransform>();
            tooltipTextRect.anchorMin = Vector2.zero;
            tooltipTextRect.anchorMax = Vector2.one;
            tooltipTextRect.offsetMin = Vector2.zero;
            tooltipTextRect.offsetMax = Vector2.zero;
            var tooltipTMP = tooltipTextGO.AddComponent<UITextComponent>();
            tooltipTMP.fontSize = 11;
            tooltipTMP.color = new Color(0.9f, 0.9f, 0.85f, 1f);
#if TMP_PRESENT
            tooltipTMP.enableWordWrapping = true;
            tooltipTMP.richText = true;
#else
            tooltipTMP.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (tooltipTMP.font == null) tooltipTMP.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            tooltipTMP.supportRichText = true;
            tooltipTMP.horizontalOverflow = HorizontalWrapMode.Wrap;
            tooltipTMP.verticalOverflow = VerticalWrapMode.Overflow;
            tooltipTMP.alignment = TextAnchor.UpperLeft;
#endif
            tooltipGO.SetActive(false);

            // Add the StatusEffectInfoUI component and wire references
            var statusUI = canvasGO.AddComponent<StatusEffectInfoUI>();
            statusUI.barRoot = barRootRect;
            statusUI.slotContainer = slotContainerRect;
            statusUI.barBackground = barBgImg;
            statusUI.tooltipPanel = tooltipRect;
            statusUI.tooltipText = tooltipTMP;

            // Register
            GameObjectUtility.SetParentAndAlign(canvasGO, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create Status Effect Info UI");
            Selection.activeGameObject = canvasGO;

            Debug.Log("[Enemy Masses] Created Status Effect Info UI. Assign an RTS Controller, Crowd Controller, and optionally a Status Effect UI Registry.");
        }

        private static GameObject CreateCommandButton(string name, Transform parent, string labelText, Color textColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(44, 36);

            var img = go.GetComponent<Image>();
            img.color = new Color(0.11f, 0.14f, 0.20f, 1f);

            // Button label
            var textGO = new GameObject("Label", typeof(RectTransform), typeof(UITextComponent));
            textGO.transform.SetParent(go.transform, false);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textGO.GetComponent<UITextComponent>();
            text.text = labelText;
            text.fontSize = 14;
            text.color = textColor;
#if TMP_PRESENT
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.fontStyle = TMPro.FontStyles.Bold;
#else
            text.alignment = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Bold;
#endif

            return go;
        }

        private static GameObject CreateCommandToggle(string name, Transform parent, string labelText, Color textColor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(44, 36);

            var img = go.GetComponent<Image>();
            img.color = new Color(0.11f, 0.14f, 0.20f, 1f);

            var toggle = go.GetComponent<Toggle>();
            toggle.targetGraphic = img;
            toggle.isOn = false;

            // Checkmark (colored overlay when ON)
            var checkGO = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkGO.transform.SetParent(go.transform, false);
            var checkRect = checkGO.GetComponent<RectTransform>();
            checkRect.anchorMin = Vector2.zero;
            checkRect.anchorMax = Vector2.one;
            checkRect.offsetMin = new Vector2(2, 2);
            checkRect.offsetMax = new Vector2(-2, -2);
            var checkImg = checkGO.GetComponent<Image>();
            checkImg.color = new Color(0.44f, 0.95f, 1f, 0.3f);
            toggle.graphic = checkImg;

            // Toggle label
            var textGO = new GameObject("Label", typeof(RectTransform), typeof(UITextComponent));
            textGO.transform.SetParent(go.transform, false);
            var textRect = textGO.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textGO.GetComponent<UITextComponent>();
            text.text = labelText;
            text.fontSize = 14;
            text.color = textColor;
#if TMP_PRESENT
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.fontStyle = TMPro.FontStyles.Bold;
#else
            text.alignment = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Bold;
#endif

            return go;
        }

        [MenuItem("GameObject/Enemy Masses/UI/Minimap", false, 53)]
        public static void CreateMinimapUI(MenuCommand menuCommand)
        {
            // Canvas root
            var canvasGO = new GameObject("MinimapCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGO.AddComponent<GraphicRaycaster>();

            // Minimap container (anchored top-right)
            var minimapGO = new GameObject("MinimapDisplay", typeof(RectTransform));
            minimapGO.transform.SetParent(canvasGO.transform, false);
            var minimapRect = minimapGO.GetComponent<RectTransform>();
            minimapRect.anchorMin = new Vector2(1f, 1f);
            minimapRect.anchorMax = new Vector2(1f, 1f);
            minimapRect.pivot = new Vector2(1f, 1f);
            minimapRect.anchoredPosition = new Vector2(-10f, -10f);
            minimapRect.sizeDelta = new Vector2(200f, 200f);

            // Background image
            var bgGO = new GameObject("Background", typeof(RectTransform));
            bgGO.transform.SetParent(minimapGO.transform, false);
            var bgRect = bgGO.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            var bgImage = bgGO.AddComponent<RawImage>();
            bgImage.color = new Color(0.08f, 0.12f, 0.08f, 1f);

            // Border (outline around minimap)
            var borderGO = new GameObject("Border", typeof(RectTransform));
            borderGO.transform.SetParent(minimapGO.transform, false);
            var borderRect = borderGO.GetComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.offsetMin = new Vector2(-2f, -2f);
            borderRect.offsetMax = new Vector2(2f, 2f);
            var borderImg = borderGO.AddComponent<Image>();
            borderImg.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);
            borderImg.raycastTarget = false;
            borderGO.transform.SetAsFirstSibling();

            // View indicator
            var viewIndicatorGO = new GameObject("ViewIndicator", typeof(RectTransform));
            viewIndicatorGO.transform.SetParent(minimapGO.transform, false);
            var viRect = viewIndicatorGO.GetComponent<RectTransform>();
            viRect.anchorMin = Vector2.zero;
            viRect.anchorMax = Vector2.zero;
            viRect.pivot = new Vector2(0.5f, 0.5f);
            viRect.sizeDelta = new Vector2(24f, 24f);
            var viImage = viewIndicatorGO.AddComponent<Image>();
            viImage.color = new Color(1f, 1f, 1f, 0.5f);
            viImage.raycastTarget = false;

            // Add RTSMinimapUI component and wire references
            var minimapUI = minimapGO.AddComponent<UI.RTSMinimapUI>();
            // Use SerializedObject to assign private serialized fields
            var so = new SerializedObject(minimapUI);
            so.FindProperty("backgroundImage").objectReferenceValue = bgImage;
            so.FindProperty("viewIndicatorRect").objectReferenceValue = viRect;
            so.FindProperty("captureBackgroundOnStart").boolValue = true;
            so.FindProperty("showFogOverlay").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Create data provider on a sibling object
            var providerGO = new GameObject("MinimapDataProvider");
            providerGO.transform.SetParent(canvasGO.transform, false);
            var provider = providerGO.AddComponent<EnemyMassesMinimapDataProvider>();

            // Create alert system on same object as provider
            var alertSys = providerGO.AddComponent<MinimapAlertSystem>();

            // Wire provider + alert system to minimap UI
            var so2 = new SerializedObject(minimapUI);
            so2.FindProperty("dataProviderSource").objectReferenceValue = provider;
            so2.FindProperty("enableAlertFlash").boolValue = true;
            so2.FindProperty("alertSystem").objectReferenceValue = alertSys;
            so2.ApplyModifiedPropertiesWithoutUndo();

            // Register undo
            GameObjectUtility.SetParentAndAlign(canvasGO, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create Minimap UI");
            Selection.activeGameObject = minimapGO;

            Debug.Log("[Enemy Masses] Created Minimap UI. Assign a background texture (top-down terrain screenshot) to the Background RawImage for best results.\n" +
                      "The data provider will auto-discover CrowdController and RTSController at runtime.");
        }

        [MenuItem("GameObject/Enemy Masses/UI/Portrait Bar", false, 54)]
        public static void CreatePortraitBar(MenuCommand menuCommand)
        {
            // Canvas root
            var canvasGO = new GameObject("CrowdPortraitBar");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 12;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGO.AddComponent<GraphicRaycaster>();
            canvasGO.AddComponent<CanvasGroup>();

            // Bar root (anchored to top-left)
            var barRootGO = new GameObject("BarRoot", typeof(RectTransform));
            barRootGO.transform.SetParent(canvasGO.transform, false);
            var barRootRect = barRootGO.GetComponent<RectTransform>();
            barRootRect.anchorMin = new Vector2(0f, 1f);
            barRootRect.anchorMax = new Vector2(0f, 1f);
            barRootRect.pivot = new Vector2(0f, 1f);
            barRootRect.anchoredPosition = new Vector2(10, -10);
            barRootRect.sizeDelta = new Vector2(600, 90);

            // Bar background
            var barBg = barRootGO.AddComponent<Image>();
            barBg.color = new Color(0.05f, 0.05f, 0.08f, 0.7f);

            // Slot container with horizontal layout
            var slotContainerGO = new GameObject("SlotContainer", typeof(RectTransform));
            slotContainerGO.transform.SetParent(barRootGO.transform, false);
            var slotContainerRect = slotContainerGO.GetComponent<RectTransform>();
            slotContainerRect.anchorMin = Vector2.zero;
            slotContainerRect.anchorMax = Vector2.one;
            slotContainerRect.offsetMin = Vector2.zero;
            slotContainerRect.offsetMax = Vector2.zero;
            var hlg = slotContainerGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.padding = new RectOffset(8, 8, 8, 8);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            var csf = slotContainerGO.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Tooltip panel (hidden by default)
            var tooltipGO = new GameObject("TooltipPanel", typeof(RectTransform));
            tooltipGO.transform.SetParent(canvasGO.transform, false);
            var tooltipRect = tooltipGO.GetComponent<RectTransform>();
            tooltipRect.anchorMin = new Vector2(0f, 1f);
            tooltipRect.anchorMax = new Vector2(0f, 1f);
            tooltipRect.pivot = new Vector2(0.5f, 0f);
            tooltipRect.sizeDelta = new Vector2(160, 40);
            var tooltipBg = tooltipGO.AddComponent<Image>();
            tooltipBg.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);

            var tooltipLayout = tooltipGO.AddComponent<VerticalLayoutGroup>();
            tooltipLayout.padding = new RectOffset(8, 8, 4, 4);
            tooltipLayout.childForceExpandWidth = true;
            tooltipLayout.childForceExpandHeight = false;
            tooltipLayout.childControlWidth = true;
            tooltipLayout.childControlHeight = true;
            tooltipLayout.childAlignment = TextAnchor.MiddleCenter;

            var tooltipFitter = tooltipGO.AddComponent<ContentSizeFitter>();
            tooltipFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            tooltipFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var tooltipTextGO = new GameObject("TooltipText", typeof(RectTransform));
            tooltipTextGO.transform.SetParent(tooltipGO.transform, false);
            var tooltipTextRect = tooltipTextGO.GetComponent<RectTransform>();
            tooltipTextRect.anchorMin = Vector2.zero;
            tooltipTextRect.anchorMax = Vector2.one;
            tooltipTextRect.offsetMin = Vector2.zero;
            tooltipTextRect.offsetMax = Vector2.zero;
            var tooltipTMP = tooltipTextGO.AddComponent<UITextComponent>();
            tooltipTMP.fontSize = 11;
            tooltipTMP.color = new Color(0.9f, 0.9f, 0.85f, 1f);
#if TMP_PRESENT
            tooltipTMP.alignment = TMPro.TextAlignmentOptions.Center;
            tooltipTMP.enableWordWrapping = false;
#else
            tooltipTMP.alignment = TextAnchor.MiddleCenter;
            tooltipTMP.horizontalOverflow = HorizontalWrapMode.Overflow;
#endif
            tooltipGO.SetActive(false);

            // Add the portrait bar component
            var portraitBar = canvasGO.AddComponent<EnemyCrowdPortraitBarUI>();
            portraitBar.barRoot = barRootRect;
            portraitBar.slotContainer = slotContainerRect;
            portraitBar.tooltipPanel = tooltipRect;
            portraitBar.tooltipText = tooltipTMP;

            // Register
            GameObjectUtility.SetParentAndAlign(canvasGO, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create Crowd Portrait Bar");
            Selection.activeGameObject = canvasGO;

            Debug.Log("[Enemy Masses] Created Crowd Portrait Bar UI. References will be auto-resolved at runtime.\n" +
                      "Portraits are sourced from each EnemyCrowd's portrait texture. Use 'Generate Portrait from Prefab' on each EnemyCrowd if needed.");
        }

        private static void CreateComponentGameObject<T>(string name, MenuCommand menuCommand) where T : Component
        {
            var go = new GameObject(name);
            go.AddComponent<T>();

            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            Selection.activeGameObject = go;
        }
        
        [MenuItem("Assets/Create/Enemy Masses/Enemy Crowd (With Defaults)", false, 201)]
        public static void CreateEnemyCrowdAsset()
        {
            // Create a new EnemyCrowd ScriptableObject
            var enemyCrowd = ScriptableObject.CreateInstance<EnemyCrowd>();
            enemyCrowd.factionName = "New Faction";
            enemyCrowd.enemyTypeName = "Enemy";
            enemyCrowd.factionColor = new Color(0.8f, 0.2f, 0.2f, 1f);
            enemyCrowd.combatBehaviorTemplate = CombatBehaviorTemplate.MeleeQueued;
            enemyCrowd.ApplyCombatTemplate();
            
            // Determine path
            string path = "Assets";
            foreach (var obj in Selection.GetFiltered<Object>(SelectionMode.Assets))
            {
                path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    path = System.IO.Path.GetDirectoryName(path);
                }
                break;
            }
            
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{path}/NewEnemyCrowd.asset");
            
            AssetDatabase.CreateAsset(enemyCrowd, assetPath);
            AssetDatabase.SaveAssets();
            
            Selection.activeObject = enemyCrowd;
            EditorGUIUtility.PingObject(enemyCrowd);
        }
        
[MenuItem("Tools/Enemy Masses/Open Documentation", false, 50)]
        public static void OpenDocumentation()
        {
            Application.OpenURL("https://arawn-software-publishing.gitbook.io/enemy-masses/");
        }
        
        [MenuItem("Tools/Enemy Masses/Discord Support", false, 51)]
        public static void OpenDiscord()
        {
            Application.OpenURL("https://discord.gg/MPhMKtSMUZ");
        }
    }
}
#endif
