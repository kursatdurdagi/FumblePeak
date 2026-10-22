#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.AI;
using UnityEngine.UI;
using Arawn.EnemyMasses.Runtime;
using Arawn.EnemyMasses.Runtime.Civil;
using Arawn.EnemyMasses.Runtime.Networking;
using Arawn.EnemyMasses.Runtime.Navigation;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;
using Arawn.EnemyMasses.Runtime.Navigation.Climbing;
using Arawn.EnemyMasses.Runtime.Navigation.FlowField;
using Arawn.Rendering.Fog;
using GPUInstancerPro;
using GPUInstancerPro.PrefabModule;
using GPUInstancerPro.CrowdAnimations;

namespace Arawn.EnemyMasses.Runtime.Editor
{
    /// <summary>
    /// First-time setup wizard for Enemy Masses.
    /// Guides new users through creating their first EnemyCrowd configuration.
    /// </summary>
    public class EnemyMassesSetupWizard : EditorWindow
    {
        enum Page
        {
            Welcome,
            ConceptOverview,
            CrowdType,
            AgentArchetype,
            AnimatorWorkflow,
            AnimationSetup,
            ClimbingSetup,
            CreateFaction,
            NavigationTypeSetup,
            CombatStyle,
            RTSSetup,
            CivilSettings,
            AssignPrefab,
            NetworkSetup,
            SceneSetup,
            Complete
        }

        // Current wizard state
        Page currentPage = Page.Welcome;
        Vector2 scrollPosition = Vector2.zero;

        // User configuration
        string factionName = "Enemy Faction";
        string enemyTypeName = "Soldier";
        Color factionColor = new Color(0.8f, 0.2f, 0.2f, 1f);
        
        // Crowd Type
        public enum CrowdType
        {
            Combat,
            Civil,
            BattleSimulation
        }
        
        CrowdType selectedCrowdType = CrowdType.Combat;
        
        // Animator Workflow
        AnimatorWorkflowOverride animatorWorkflow = AnimatorWorkflowOverride.ComputeAnimator;
        
        // Animations
        AnimationClip idleClip;
        AnimationClip walkClip;
        AnimationClip runClip;
        AnimationClip[] attackClips;
        AnimationClip[] hitClips;
        AnimationClip[] deathClips;
        
        // Combat
        CombatBehaviorTemplate combatTemplate = CombatBehaviorTemplate.MeleeQueued;
        List<Transform> playerTargets = new List<Transform>();

        // Navigation Backend
        NavigationType selectedNavigationType = NavigationType.UnityNavMesh;
        NavigationType[] availableNavigationTypes = Array.Empty<NavigationType>();
        
        // RTS
        bool isPlayerControlledFaction = false;
        int rtsPresetType = 0; // 0 = Classic RTS, 1 = Modern RTS, 2 = Third Person, 3 = Total War, 4 = Supreme Commander
        bool createMinimap = false;
        int minimapMode = 0; // 0 = Camera-Free (UI), 1 = Camera-Based (Legacy)
        bool createAlertSystem = true;
        bool createFogOfWar = false;
        Vector2 worldSize = new Vector2(1000f, 1000f);
        
        const string MINIMAP_TEXTURE_PATH = "Assets/Plugins/EnemyMasses/Runtime/Texture/MiniMapRenderTexture.renderTexture";
        const string MINIMAP_MATERIAL_PATH = "Assets/Plugins/EnemyMasses/Runtime/Material/MinimapMaterial.mat";
        const string FOG_MATERIAL_PATH = "Assets/Plugins/EnemyMasses/Runtime/Material/Fog.mat";
        const string FOG_COMPUTE_SHADER_PATH = "Assets/Plugins/EnemyMasses/Runtime/Rendering/Fog/FogOfWarCompute.compute";
        const string FLOW_FIELD_CONFIG_DIRECTORY = "Assets/EnemyMasses/Navigation";
        const string FLOW_FIELD_CONFIG_ASSET_PATH = FLOW_FIELD_CONFIG_DIRECTORY + "/FlowFieldRuntimeConfig.asset";
        
        // Civil
        CivilNavigationMode civilNavMode = CivilNavigationMode.FollowConnections;
        CivilMovementStyle civilMoveStyle = CivilMovementStyle.Walk;
        
        // Climbing Configuration
        bool enableClimbing = false;
        AnimationClip climbUpClip;
        AnimationClip climbDownClip;
        AnimationClip climbMountClip;
        AnimationClip climbDismountClip;
        AnimationClip climbFallClip;
        string climbingBoolParameter = "IsClimbing";
        string climbSpeedParameter = "ClimbSpeed";
        string climbMountTrigger = "Mount";
        string climbDismountTrigger = "Dismount";
        string climbFallTrigger = "Fall";
        float climbMountDuration = 0.5f;
        float climbDismountDuration = 0.5f;
        bool enableEdgeFallDetection = true;
        bool enableFallDamage = true;
        float fallDamagePerUnit = 5f;
        
        // Network Configuration
        bool enableNetworking = false;
        MultiplayerMode multiplayerMode = MultiplayerMode.OneVsOne;
        bool addAntiCheat = true;
        int recommendedMaxUnitsPerPlayer = 500;
        
        enum MultiplayerMode
        {
            OneVsOne,           // 2 players - 500 units each
            TwoVsTwo,           // 4 players - 250 units each
            OneVsOneVsOne,      // 3 players - 333 units each
            FreeForAll4,        // 4 players - 250 units each
            FreeForAll6,        // 6 players - 166 units each
            FreeForAll8,        // 8 players - 125 units each
            Custom              // User-defined
        }
        
        int customPlayerCount = 2;
        
        // Agent Archetype (Flying / Non-Humanoid)
        AgentArchetype selectedAgentArchetype = AgentArchetype.Humanoid;
        NavigationType3D flight3DNavType = NavigationType3D.Boids;
        HitboxPreset selectedHitboxPreset = HitboxPreset.Humanoid;
        float vehicleBoundingRadius = 2f;
        float vehicleBoundingHeight = 2f;
        float cruisingAltitude = 10f;
        float altitudeVariance = 3f;
        float verticalSpeed = 5f;
        float bankAngle = 25f;
        float bankSpeed = 90f;
        float headingSmoothRate = 5f;
        float maxPitchAngle = 30f;
        int formationLayers = 1;
        float formationLayerSpacing = 5f;
        
        GameObject selectedPrefab;
        
        // Created assets
        EnemyCrowd createdEnemyCrowd;
        
        // UI State
        bool showResources = true;
        bool dontShowAgain = false;
        Texture2D coverImage;
        GUIStyle headerStyle;
        GUIStyle descriptionStyle;
        GUIStyle tipBoxStyle;
        GUIStyle sectionHeaderStyle;
        bool stylesInitialized = false;
        
        // GPUI Patcher state
        bool showGPUIPatcher = false;
        bool gpuiPatcherInitialized = false;
        bool gpuiInstalled = false;
        bool prefabManagerPatched = false;
        bool crowdSourcePatched = false;
        bool helpUrlPatched = false;
        bool shaderPatched = false;
        bool crowdUtilityPatched = false;
        
        // GPUI Patcher constants
        const string GPUI_PREFAB_MANAGER_PATH = "Packages/com.gurbu.gpui-pro.prefab/Runtime/Scripts/GPUIPrefabManager.cs";
        const string GPUI_CROWD_RENDER_SOURCE_GROUP_PATH = "Packages/com.gurbu.gpui-pro.crowd-animations/Runtime/Scripts/Data/GPUICrowdRenderSourceGroup.cs";
        const string GPUI_CAMERA_PATH = "Packages/com.gurbu.gpui-pro/Runtime/Scripts/GPUICamera.cs";
        const string GPUI_TREE_PROXY_SHADER_PATH = "Packages/com.gurbu.gpui-pro/Runtime/Shaders/TreeProxy_GPUIPro.shader";
        const string PATCH_MARKER_1 = "// [EnemyMasses Patch 1] Validate buffer index bounds";
        const string PATCH_MARKER_2 = "// [EnemyMasses Patch 2] Skip invalid indices";
        const string PATCH_MARKER_3 = "// [EnemyMasses Patch 3] Validate buffer indices before NativeArray copy";
        const string PATCH_MARKER_4 = "// [EnemyMasses Patch 4] Use instanceCount instead of GetMatrixLength()";
        const string PATCH_MARKER_5 = "// [EnemyMasses Patch 5] Clear stale animation data for new instances";
        const string PATCH_MARKER_5_ENHANCED = "ResetBonesToBindPose"; // Enhanced version with bone reset
        const string HELPURL_PATCH_MARKER = "// [EnemyMasses Patch] HelpURL commented out for Unity 6 compatibility";
        const string SHADER_PATCH_MARKER = "// [EnemyMasses Patch] WebGPU Fix";
        
        // Banner images for pages
        const string coverImagePath = "Assets/Plugins/EnemyMasses/Editor/SetupWizard/Images/EnemyMassesWizardCover.png";
        const string slideshowFolderPath = "Assets/Plugins/EnemyMasses/Editor/SetupWizard/Images";
        Dictionary<Page, Texture2D> pageBanner = new Dictionary<Page, Texture2D>();
        readonly Page[] slideshowPages = new[]
        {
            Page.ConceptOverview,
            Page.CrowdType,
            Page.AgentArchetype,
            Page.AnimatorWorkflow,
            Page.AnimationSetup,
            Page.ClimbingSetup,
            Page.CreateFaction,
            Page.NavigationTypeSetup,
            Page.CombatStyle,
            Page.RTSSetup,
            Page.CivilSettings,
            Page.AssignPrefab,
            Page.NetworkSetup,
            Page.SceneSetup
        };

        // Prefs key prefix to track if wizard has been shown (project-scoped)
        const string WIZARD_SHOWN_KEY_PREFIX = "EnemyMasses_SetupWizardShown_";
        // Tracks the last patch revision the user has seen in the wizard.
        // Bump this number whenever a new GPUI patch is added so existing
        // users get the wizard re-opened automatically on plugin update.
        const int CURRENT_PATCH_REVISION = 6;
        const string PATCH_REVISION_KEY_PREFIX = "EnemyMasses_PatchRevision_";
        const string SETTINGS_PATH = "Assets/Plugins/EnemyMasses/Resources";

        static string GetWizardPrefsKey()
        {
            return WIZARD_SHOWN_KEY_PREFIX + Application.dataPath.Replace('/', '_').Replace('\\', '_');
        }
        
        static string GetPatchRevisionPrefsKey()
        {
            return PATCH_REVISION_KEY_PREFIX + Application.dataPath.Replace('/', '_').Replace('\\', '_');
        }

        static bool HasWizardBeenShown() => EditorPrefs.GetBool(GetWizardPrefsKey(), false);
        static void MarkWizardShown() => EditorPrefs.SetBool(GetWizardPrefsKey(), true);
        
        static int GetLastSeenPatchRevision() => EditorPrefs.GetInt(GetPatchRevisionPrefsKey(), 0);
        static void MarkPatchRevisionSeen() => EditorPrefs.SetInt(GetPatchRevisionPrefsKey(), CURRENT_PATCH_REVISION);
        static bool HasUnseenPatches() => GetLastSeenPatchRevision() < CURRENT_PATCH_REVISION;

        [MenuItem("Tools/Enemy Masses/Setup Wizard", priority = 0)]
        public static void OpenWizard()
        {
            var window = GetWindow<EnemyMassesSetupWizard>(true, "Enemy Masses Setup Wizard");
            var fixedSize = new Vector2(750f, 720f);
            window.minSize = fixedSize;
            window.maxSize = new Vector2(fixedSize.x, 950f);
            window.position = GetCenteredRectOnMainEditor(fixedSize);
        }

        /// <summary>
        /// Called automatically when Unity loads - shows wizard on first import
        /// or when new GPUI patches are available that the user hasn't seen yet.
        /// </summary>
        [InitializeOnLoadMethod]
        static void CheckFirstTimeSetup()
        {
            // Delay to ensure Unity is fully loaded
            EditorApplication.delayCall += () =>
            {
                if (!HasWizardBeenShown())
                {
                    // First time - show wizard regardless of existing assets
                    OpenWizard();
                    MarkWizardShown();
                    MarkPatchRevisionSeen();
                }
                else if (HasUnseenPatches())
                {
                    // Returning user but new patches are available — re-open wizard
                    // so they see the patcher section and can apply them.
                    OpenWizard();
                    MarkPatchRevisionSeen();
                }
            };
        }

        static Rect GetCenteredRectOnMainEditor(Vector2 size)
        {
            Rect main = GetMainEditorWindowPosition();
            float x = main.x + (main.width - size.x) / 2f;
            float y = main.y + (main.height - size.y) / 2f;
            return new Rect(x, y, size.x, size.y);
        }

        static Rect GetMainEditorWindowPosition()
        {
            var containerWinType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.ContainerWindow");
            if (containerWinType == null)
                return new Rect(0, 0, Screen.currentResolution.width, Screen.currentResolution.height);
            var showModeField = containerWinType.GetField("m_ShowMode", BindingFlags.NonPublic | BindingFlags.Instance);
            var positionProperty = containerWinType.GetProperty("position", BindingFlags.Public | BindingFlags.Instance);
            if (showModeField == null || positionProperty == null)
                return new Rect(0, 0, Screen.currentResolution.width, Screen.currentResolution.height);
            var windows = Resources.FindObjectsOfTypeAll(containerWinType);
            foreach (var win in windows)
            {
                var showModeObj = showModeField.GetValue(win);
                if (showModeObj == null) continue;
                // 4 == main editor window
                if ((int)showModeObj == 4)
                {
                    var pos = (Rect)positionProperty.GetValue(win, null);
                    return pos;
                }
            }
            return new Rect(0, 0, Screen.currentResolution.width, Screen.currentResolution.height);
        }
        
        void OnEnable()
        {
            // Load cover image
            coverImage = AssetDatabase.LoadAssetAtPath<Texture2D>(coverImagePath);
            
            // Load slideshow images for pages
            LoadSlideshowImages();

            // Cache available navigation backends for this project setup
            RefreshNavigationTypeOptions();
        }
        
        void LoadSlideshowImages()
        {
            pageBanner.Clear();
            if (!AssetDatabase.IsValidFolder(slideshowFolderPath)) return;
            
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { slideshowFolderPath });
            var textures = new List<Texture2D>();
            
            foreach (var guid in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
                    !p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) continue;
                
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (tex != null) textures.Add(tex);
            }
            
            // Shuffle for variety
            var rng = new System.Random(Environment.TickCount);
            for (int i = textures.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (textures[i], textures[j]) = (textures[j], textures[i]);
            }
            
            // Assign unique textures to pages
            int assignCount = Mathf.Min(textures.Count, slideshowPages.Length);
            for (int i = 0; i < assignCount; i++)
            {
                pageBanner[slideshowPages[i]] = textures[i];
            }
        }

        void InitializeStyles()
        {
            if (stylesInitialized) return;
            
            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                margin = new RectOffset(10, 10, 10, 10)
            };
            
            sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                margin = new RectOffset(5, 5, 8, 5)
            };
            
            descriptionStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                wordWrap = true,
                richText = true,
                margin = new RectOffset(10, 10, 5, 5)
            };

            tipBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 11,
                padding = new RectOffset(10, 10, 8, 8),
                richText = true
            };
            
            stylesInitialized = true;
        }
        
        void DrawBannerForCurrentPage()
        {
            // Draw banner image if available for current page
            if (pageBanner.TryGetValue(currentPage, out var banner) && banner != null)
            {
                // Calculate aspect-ratio-preserving rect
                float maxHeight = 120f;
                float availableWidth = EditorGUIUtility.currentViewWidth - 40f;
                float aspectRatio = (float)banner.width / banner.height;
                float displayHeight = Mathf.Min(maxHeight, availableWidth / aspectRatio);
                float displayWidth = displayHeight * aspectRatio;
                
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                var bannerRect = GUILayoutUtility.GetRect(displayWidth, displayHeight);
                GUI.DrawTexture(bannerRect, banner, ScaleMode.ScaleToFit);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                
                GUILayout.Space(10);
            }
        }

        void OnGUI()
        {
            InitializeStyles();
            
            // Draw progress bar
            DrawProgressBar();
            
            EditorGUILayout.Space(10);
            
            // Draw banner for current page (except Welcome and Complete which have custom layouts)
            if (currentPage != Page.Welcome && currentPage != Page.Complete)
            {
                DrawBannerForCurrentPage();
            }
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            
            switch (currentPage)
            {
                case Page.Welcome:
                    DrawWelcomePage();
                    break;
                case Page.ConceptOverview:
                    DrawConceptOverviewPage();
                    break;
                case Page.CrowdType:
                    DrawCrowdTypePage();
                    break;
                case Page.AgentArchetype:
                    DrawAgentArchetypePage();
                    break;
                case Page.AnimatorWorkflow:
                    DrawAnimatorWorkflowPage();
                    break;
                case Page.AnimationSetup:
                    DrawAnimationSetupPage();
                    break;
                case Page.ClimbingSetup:
                    DrawClimbingSetupPage();
                    break;
                case Page.CreateFaction:
                    DrawCreateFactionPage();
                    break;
                case Page.NavigationTypeSetup:
                    DrawNavigationTypeSetupPage();
                    break;
                case Page.CombatStyle:
                    DrawCombatStylePage();
                    break;
                case Page.RTSSetup:
                    DrawRTSSetupPage();
                    break;
                case Page.CivilSettings:
                    DrawCivilSettingsPage();
                    break;
                case Page.AssignPrefab:
                    DrawAssignPrefabPage();
                    break;
                case Page.NetworkSetup:
                    DrawNetworkSetupPage();
                    break;
                case Page.SceneSetup:
                    DrawSceneSetupPage();
                    break;
                case Page.Complete:
                    DrawCompletePage();
                    break;
            }
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndScrollView();
            
            DrawNavigation();
        }

        void DrawProgressBar()
        {
            // Calculate total steps based on path
            int totalSteps = 11; // Welcome -> Concept -> Type -> Archetype -> Workflow -> Anim -> Faction -> Nav -> (Combat/Civil) -> Prefab -> Scene -> Complete
            
            // Calculate current step index
            int currentStep = 0;
            if (currentPage == Page.Complete) currentStep = totalSteps;
            else if (currentPage == Page.Welcome) currentStep = 0;
            else
            {
                // Map pages to step numbers roughly
                if (currentPage >= Page.ConceptOverview) currentStep++;
                if (currentPage >= Page.CrowdType) currentStep++;
                if (currentPage >= Page.AgentArchetype) currentStep++;
                if (currentPage >= Page.AnimatorWorkflow) currentStep++;
                if (currentPage >= Page.AnimationSetup) currentStep++;
                if (currentPage >= Page.CreateFaction) currentStep++;
                if (currentPage >= Page.NavigationTypeSetup) currentStep++;
                if (currentPage >= Page.CombatStyle || currentPage >= Page.CivilSettings) currentStep++;
                if (currentPage >= Page.RTSSetup && selectedCrowdType == CrowdType.BattleSimulation) currentStep++;
                if (currentPage >= Page.AssignPrefab) currentStep++;
                if (currentPage >= Page.SceneSetup) currentStep++;
            }
            
            EditorGUILayout.Space(5);
            
            var rect = GUILayoutUtility.GetRect(10, 8);
            rect.x += 10;
            rect.width -= 20;
            
            // Background
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f, 1f));
            
            // Progress
            float progress = (float)currentStep / totalSteps;
            var progressRect = new Rect(rect.x, rect.y, rect.width * progress, rect.height);
            EditorGUI.DrawRect(progressRect, new Color(0.3f, 0.7f, 0.4f, 1f));
            
            // Step indicator
            string stepText = currentPage == Page.Complete ? "Setup Complete!" : $"Step {currentStep} of {totalSteps}";
            if (currentPage == Page.Welcome) stepText = "Welcome";
            
            EditorGUILayout.LabelField(stepText, EditorStyles.centeredGreyMiniLabel);
        }

        #region Page Implementations

        void DrawWelcomePage()
        {
            GUILayout.Space(10);
            
            // Cover image if available
            if (coverImage != null)
            {
                float maxHeight = 150f;
                float availableWidth = EditorGUIUtility.currentViewWidth - 60f;
                float aspectRatio = (float)coverImage.width / coverImage.height;
                float displayHeight = Mathf.Min(maxHeight, availableWidth / aspectRatio);
                float displayWidth = displayHeight * aspectRatio;
                
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                var coverRect = GUILayoutUtility.GetRect(displayWidth, displayHeight);
                GUI.DrawTexture(coverRect, coverImage, ScaleMode.ScaleToFit);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                
                GUILayout.Space(15);
            }
            
            // Title
            EditorGUILayout.LabelField("Welcome to Enemy Masses!", headerStyle);
            
            GUILayout.Space(15);
            
            // Welcome message in a nice box
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.Space(10);
            
            EditorGUILayout.LabelField(
                "Enemy Masses is a high-performance crowd combat system that lets you create " +
                "massive battles with hundreds or thousands of AI agents.\n\n" +
                "This wizard will guide you through creating your first enemy faction in just a few minutes.",
                descriptionStyle);
            
            EditorGUILayout.Space(10);
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(20);
            
            // Feature highlights
            EditorGUILayout.LabelField("What You'll Create", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            DrawFeatureItem("Enemy Crowd Asset", "A reusable faction configuration");
            DrawFeatureItem("Behavior Settings", "Combat or Civil behavior configuration");
            DrawFeatureItem("Character Prefab", "Assign your 3D character model");
            DrawFeatureItem("Scene Controller", "Set up the runtime system");
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(20);
            
            // GPUI Patcher section
            DrawGPUIPatcherSection();
            
            GUILayout.Space(20);
            
            // Resources section
            showResources = EditorGUILayout.Foldout(showResources, "Resources & Support", true);
            if (showResources)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Documentation, tutorials, and community support:", EditorStyles.miniLabel);
                
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Documentation", EditorStyles.miniButton))
                    Application.OpenURL("https://arawn-software-publishing.gitbook.io/enemy-masses/");
                if (GUILayout.Button("Discord", EditorStyles.miniButton))
                    Application.OpenURL("https://discord.gg/MPhMKtSMUZ");
                if (GUILayout.Button("Support", EditorStyles.miniButton))
                    Application.OpenURL("mailto:mail@arawn.digital");
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            
            GUILayout.Space(30);
            
            // Big start button
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.4f, 1f);
            if (GUILayout.Button("Let's Get Started! ->", GUILayout.Height(40), GUILayout.Width(200)))
            {
                currentPage = Page.ConceptOverview;
            }
            GUI.backgroundColor = Color.white;
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        void DrawFeatureItem(string title, string description)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("-", GUILayout.Width(15));
            EditorGUILayout.LabelField($"<b>{title}</b> - {description}", descriptionStyle);
            EditorGUILayout.EndHorizontal();
        }

        #region GPUI Patcher Section
        
        /// <summary>
        /// Returns true if the GPUIPrefabManager patches are needed for this version of GPUI Pro.
        /// Uses Version Defines in the asmdef to determine compatibility.
        /// </summary>
        static bool NeedsPrefabManagerPatches
        {
            get
            {
#if EM_GPUIP_NEEDS_PATCH
                return true;
#else
                return false;
#endif
            }
        }
        
        void InitializeGPUIPatcherStatus()
        {
            if (gpuiPatcherInitialized) return;
            
            RefreshGPUIPatchStatus();
            
            // Auto-expand if patches are needed
            bool allPatched = GetAllPatchesApplied();
            showGPUIPatcher = gpuiInstalled && !allPatched;
            
            gpuiPatcherInitialized = true;
        }
        
        bool GetAllPatchesApplied()
        {
            // Only check prefab/crowd patches if this GPUI version needs them
            bool corePatched = !NeedsPrefabManagerPatches || (prefabManagerPatched && crowdSourcePatched);
            // Only check HelpURL/shader patches if this GPUI version needs them
            bool otherPatched = !NeedsPrefabManagerPatches || (helpUrlPatched && shaderPatched);
            // Crowd utility patch applies to ALL GPUI versions
            bool universalPatched = crowdUtilityPatched;
            return corePatched && otherPatched && universalPatched;
        }
        
        void RefreshGPUIPatchStatus()
        {
            string prefabManagerPath = Path.GetFullPath(GPUI_PREFAB_MANAGER_PATH);
            string crowdSourcePath = Path.GetFullPath(GPUI_CROWD_RENDER_SOURCE_GROUP_PATH);
            string cameraPath = Path.GetFullPath(GPUI_CAMERA_PATH);
            string shaderPath = Path.GetFullPath(GPUI_TREE_PROXY_SHADER_PATH);
            
            gpuiInstalled = File.Exists(prefabManagerPath);
            
            if (gpuiInstalled)
            {
                string prefabContent = File.ReadAllText(prefabManagerPath);
                prefabManagerPatched = prefabContent.Contains(PATCH_MARKER_1) && 
                                      prefabContent.Contains(PATCH_MARKER_2) && 
                                      prefabContent.Contains(PATCH_MARKER_4);
            }
            
            if (File.Exists(crowdSourcePath))
            {
                string crowdContent = File.ReadAllText(crowdSourcePath);
                // Require Patch 3 + enhanced Patch 5 (with bone reset for animation fix)
                crowdSourcePatched = crowdContent.Contains(PATCH_MARKER_3) && 
                                     crowdContent.Contains(PATCH_MARKER_5) &&
                                     crowdContent.Contains(PATCH_MARKER_5_ENHANCED);
            }
            
            // Crowd utility patch (all versions) — suppress per-frame LogError spam
#if EM_GPUIP_INSTALLED
            crowdUtilityPatched = GPUICrowdUtilityPatcher.IsPatched();
#else
            crowdUtilityPatched = true; // Not applicable without GPUI
#endif
            
            // Only check HelpURL/shader patches if this GPUI version needs them
            if (!NeedsPrefabManagerPatches)
            {
                // These patches are not needed for newer GPUI versions
                helpUrlPatched = true;
                shaderPatched = true;
            }
            else
            {
                if (File.Exists(cameraPath))
                {
                    string cameraContent = File.ReadAllText(cameraPath);
                    helpUrlPatched = cameraContent.Contains(HELPURL_PATCH_MARKER) || !cameraContent.Contains("[HelpURL(");
                }
                else
                {
                    helpUrlPatched = true;
                }

                if (File.Exists(shaderPath))
                {
                    string shaderContent = File.ReadAllText(shaderPath);
                    shaderPatched = shaderContent.Contains(SHADER_PATCH_MARKER);
                }
                else
                {
                    // If shader doesn't exist but GPUI is installed, it might be a different version or missing file.
                    // Assume patched if file missing to avoid blocking, or false?
                    // If GPUI is installed, the shader should be there.
                    shaderPatched = !gpuiInstalled; 
                }
            }
        }
        
        /// <summary>
        /// Checks if there's a NavMeshSurface component in the scene.
        /// Uses component type name to support both Unity 2022 (Unity.AI.Navigation) 
        /// and Unity 6+ (UnityEngine.AI) NavMesh packages.
        /// </summary>
        bool HasNavMeshSurfaceInScene()
        {
            // Search all MonoBehaviours and check for NavMeshSurface by type name
            var allBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var behaviour in allBehaviours)
            {
                if (behaviour != null)
                {
                    string typeName = behaviour.GetType().Name;
                    if (typeName == "NavMeshSurface")
                        return true;
                }
            }
            return false;
        }
        
        void DrawGPUIPatcherSection()
        {
            InitializeGPUIPatcherStatus();
            
            if (!gpuiInstalled)
            {
                // Don't show patcher section if GPUI is not installed
                return;
            }
            
            bool allPatched = GetAllPatchesApplied();
            
            // Determine foldout label based on status
            string foldoutLabel = allPatched 
                ? "GPU Instancer Pro Patches (All Applied)" 
                : "GPU Instancer Pro Patches (Action Required)";
            
            // Use a colored box if patches are needed
            if (!allPatched)
            {
                GUI.backgroundColor = new Color(1f, 0.9f, 0.7f, 1f);
            }
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = Color.white;
            
            showGPUIPatcher = EditorGUILayout.Foldout(showGPUIPatcher, foldoutLabel, true);
            
            if (showGPUIPatcher)
            {
                GUILayout.Space(5);
                
                // Explanation - only mention patches if this version needs them
                string patchExplanation = NeedsPrefabManagerPatches
                    ? "Enemy Masses requires patches to GPU Instancer Pro to fix known issues:\n" +
                      "- Buffer Index Errors during spawn/destroy cycles\n" +
                      "- Invisible Agents after immediate spawn\n" +
                      "- Stale Animation Data (wrong animations when agents become visible)\n" +
                      "- Unity 6 UI Errors from HelpURL attributes\n" +
                      "- WebGPU Build Errors (TreeProxy shader)\n" +
                      "- Missing Crowd Data error spam (all versions)"
                    : "Enemy Masses requires a patch to suppress per-frame error spam\n" +
                      "when crowd data is temporarily unavailable (all GPUI versions).";
                
                EditorGUILayout.LabelField(patchExplanation, descriptionStyle);
                
                GUILayout.Space(10);
                
                // Patch status
                EditorGUILayout.LabelField("Patch Status:", EditorStyles.boldLabel);
                
                // Only show GPUIPrefabManager patches if this version needs them
                if (NeedsPrefabManagerPatches)
                {
                    DrawPatchStatusItem("GPUIPrefabManager Patches", prefabManagerPatched);
                    DrawPatchStatusItem("GPUICrowdRenderSourceGroup Patches", crowdSourcePatched);
                }
                else
                {
                    // Show that these patches are not needed for this version
                    DrawPatchStatusItem("GPUIPrefabManager Patches (not needed)", true);
                    DrawPatchStatusItem("GPUICrowdRenderSourceGroup Patches (not needed)", true);
                }
                
                if (NeedsPrefabManagerPatches)
                {
                    DrawPatchStatusItem("HelpURL Patches (Unity 6)", helpUrlPatched);
                    DrawPatchStatusItem("WebGPU Shader Patches", shaderPatched);
                }
                else
                {
                    // Show that these patches are not needed for this version
                    DrawPatchStatusItem("HelpURL Patches (not needed)", true);
                    DrawPatchStatusItem("WebGPU Shader Patches (not needed)", true);
                }
                
                // Universal patches (all GPUI versions)
                DrawPatchStatusItem("Crowd Data Error Suppression", crowdUtilityPatched);
                
                GUILayout.Space(10);
                
                // Apply button
                EditorGUI.BeginDisabledGroup(allPatched);
                
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                
                GUI.backgroundColor = allPatched ? new Color(0.3f, 0.7f, 0.4f, 1f) : new Color(0.9f, 0.6f, 0.2f, 1f);
                
                string buttonText = allPatched ? "All Patches Applied" : "Apply All Patches";
                if (GUILayout.Button(buttonText, GUILayout.Height(30), GUILayout.Width(180)))
                {
                    ApplyGPUIPatches();
                }
                
                GUI.backgroundColor = Color.white;
                
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                
                EditorGUI.EndDisabledGroup();
                
                if (allPatched)
                {
                    EditorGUILayout.LabelField("All patches have been applied successfully!", EditorStyles.centeredGreyMiniLabel);
                }
            }
            else if (!allPatched)
            {
                // Show a brief warning when collapsed and patches are needed
                EditorGUILayout.LabelField("Click to expand and apply required patches.", EditorStyles.miniLabel);
            }
            
            EditorGUILayout.EndVertical();
        }
        
        void DrawPatchStatusItem(string name, bool isPatched)
        {
            EditorGUILayout.BeginHorizontal();
            string icon = isPatched ? "  [OK]" : "  [X]";
            Color color = isPatched ? new Color(0.3f, 0.8f, 0.4f) : new Color(0.9f, 0.4f, 0.3f);
            
            var style = new GUIStyle(EditorStyles.label) { richText = true };
            EditorGUILayout.LabelField($"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{icon}</color> {name}", style);
            EditorGUILayout.EndHorizontal();
        }
        
        void ApplyGPUIPatches()
        {
            bool success = true;
            string results = "";
            
            // Apply GPUIPrefabManager patches (only if this version needs them)
#if EM_GPUIP_NEEDS_PATCH
            if (!prefabManagerPatched || !crowdSourcePatched)
            {
                try
                {
                    var method = typeof(Arawn.EnemyMasses.Editor.GPUIPrefabManagerPatcher).GetMethod("ApplyPatchesInternal", 
                        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                    
                    if (method != null)
                    {
                        // Pass false for showResultDialog since we show our own results
                        bool patchResult = (bool)method.Invoke(null, new object[] { false });
                        if (patchResult)
                        {
                            results += "[OK] GPUIPrefabManager patches applied\n";
                            results += "[OK] GPUICrowdRenderSourceGroup patches applied\n";
                        }
                        else
                        {
                            results += "[X] Failed to apply some GPUIPrefabManager patches\n";
                            success = false;
                        }
                    }
                    else
                    {
                        Arawn.EnemyMasses.Editor.GPUIPrefabManagerPatcher.ApplyPatchesInternal(false);
                        results += "[OK] GPUIPrefabManager patches applied\n";
                        results += "[OK] GPUICrowdRenderSourceGroup patches applied\n";
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[EnemyMasses Patcher] Error applying patches: {e.Message}");
                    results += "[X] Failed to apply GPUIPrefabManager patches\n";
                    success = false;
                }
            }
            else
            {
                results += "[OK] GPUIPrefabManager patches already applied\n";
                results += "[OK] GPUICrowdRenderSourceGroup patches already applied\n";
            }
#else
            results += "[--] GPUIPrefabManager patches not needed for this GPUI version\n";
            results += "[--] GPUICrowdRenderSourceGroup patches not needed for this GPUI version\n";
#endif
            
            // Apply HelpURL patches
#if EM_GPUIP_NEEDS_PATCH
            if (!helpUrlPatched)
            {
                try
                {
                    var method = typeof(Arawn.EnemyMasses.Editor.GPUICameraPatcher).GetMethod("ApplyPatchesInternal", 
                        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                    
                    if (method != null)
                    {
                        bool helpUrlResult = (bool)method.Invoke(null, new object[] { false });
                        if (helpUrlResult)
                        {
                            results += "[OK] HelpURL patches applied\n";
                        }
                        else
                        {
                            results += "[X] Failed to apply HelpURL patches\n";
                            success = false;
                        }
                    }
                    else
                    {
                        Arawn.EnemyMasses.Editor.GPUICameraPatcher.ApplyPatchesInternal();
                        results += "[OK] HelpURL patches applied\n";
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[EnemyMasses Patcher] Error applying HelpURL patches: {e.Message}");
                    results += "[X] Failed to apply HelpURL patches\n";
                    success = false;
                }
            }
            else
            {
                results += "[OK] HelpURL patches already applied\n";
            }
#else
            results += "[--] HelpURL patches not needed for this GPUI version\n";
#endif

            // Apply Shader patches
#if EM_GPUIP_NEEDS_PATCH
            if (!shaderPatched)
            {
                try
                {
                    var method = typeof(Arawn.EnemyMasses.Editor.GPUIShaderPatcher).GetMethod("ApplyPatchesInternal", 
                        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
                    
                    if (method != null)
                    {
                        bool shaderResult = (bool)method.Invoke(null, new object[] { false });
                        if (shaderResult)
                        {
                            results += "[OK] WebGPU Shader patches applied\n";
                        }
                        else
                        {
                            results += "[X] Failed to apply WebGPU Shader patches\n";
                            success = false;
                        }
                    }
                    else
                    {
                        Arawn.EnemyMasses.Editor.GPUIShaderPatcher.ApplyPatchesInternal(false);
                        results += "[OK] WebGPU Shader patches applied\n";
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[EnemyMasses Patcher] Error applying Shader patches: {e.Message}");
                    results += "[X] Failed to apply WebGPU Shader patches\n";
                    success = false;
                }
            }
            else
            {
                results += "[OK] WebGPU Shader patches already applied\n";
            }
#else
            results += "[--] WebGPU Shader patches not needed for this GPUI version\n";
#endif
            
            // Crowd utility patch (all GPUI versions)
#if EM_GPUIP_INSTALLED
            if (!crowdUtilityPatched)
            {
                try
                {
                    bool crowdUtilResult = GPUICrowdUtilityPatcher.ApplyPatch(false);
                    if (crowdUtilResult)
                    {
                        results += "[OK] Crowd Data Error Suppression patch applied\n";
                    }
                    else
                    {
                        results += "[X] Failed to apply Crowd Data Error Suppression patch\n";
                        success = false;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[EnemyMasses Patcher] Error applying Crowd Utility patch: {e.Message}");
                    results += "[X] Failed to apply Crowd Data Error Suppression patch\n";
                    success = false;
                }
            }
            else
            {
                results += "[OK] Crowd Data Error Suppression patch already applied\n";
            }
#else
            results += "[--] Crowd Data Error Suppression patch not applicable (GPUI not installed)\n";
#endif
            
            AssetDatabase.Refresh();
            RefreshGPUIPatchStatus();
            
            // Mark patch revision as seen so the wizard doesn't re-open for these patches
            if (success)
                MarkPatchRevisionSeen();
            
            // Show results
            EditorUtility.DisplayDialog(
                "Enemy Masses - Patch Results",
                results + (success ? "\nAll patches applied successfully!" : "\nSome patches failed. Check console for details."),
                "OK");
            
            Repaint();
        }
        
        #endregion

        void DrawConceptOverviewPage()
        {
            EditorGUILayout.LabelField("Understanding Enemy Masses", headerStyle);
            
            GUILayout.Space(10);
            
            // Core concept explanation
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "<b>Core Concept:</b> Enemy Masses uses <b>GPU Instancing</b> to render thousands of animated " +
                "characters efficiently. Each group of enemies is defined by an <b>EnemyCrowd</b> asset.",
                descriptionStyle);
            
            GUILayout.Space(5);
            
            EditorGUILayout.LabelField(
                "<b>Requirement:</b> This asset relies on <b>GPU Instancer Pro</b> by GURBU Technologies. " +
                "It is a paid third-party asset that must be purchased and installed for Enemy Masses to function.",
                descriptionStyle);
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            
            // Key components
            EditorGUILayout.LabelField("Key Components", EditorStyles.boldLabel);
            
            DrawConceptCard("EnemyCrowd (ScriptableObject)",
                "Defines a faction/type of enemy:\n" +
                "- Faction name and visual identity\n" +
                "- Behavior (Combat or Civil)\n" +
                "- Stats (health, damage, speed multipliers)\n" +
                "- Assigned character prefab",
                MessageType.Info);
            
            DrawConceptCard("EnemyMassesCrowdController (Scene Component)",
                "The runtime manager that:\n" +
                "- Spawns and manages all agents\n" +
                "- Handles combat AI and pathfinding\n" +
                "- Coordinates animations and state\n" +
                "- One per scene, manages all factions",
                MessageType.Info);
            
            DrawConceptCard("Character Prefab (3D Model)",
                "Your animated character model needs:\n" +
                "- Skinned Mesh Renderer with animations\n" +
                "- Navigation component for selected backend (auto-added if missing)\n" +
                "- GPU Instancer components (auto-added)",
                MessageType.Info);
            
            GUILayout.Space(15);
            
            // Tip
            EditorGUILayout.HelpBox(
                "TIP: You can have multiple EnemyCrowd assets for different enemy types " +
                "(soldiers, archers, bosses) all managed by a single controller!",
                MessageType.None);
        }

        void DrawConceptCard(string title, string description, MessageType type)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(description, descriptionStyle);
            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }

        void DrawCrowdTypePage()
        {
            EditorGUILayout.LabelField("Choose Crowd Type", headerStyle);
            
            GUILayout.Space(10);
            
            EditorGUILayout.HelpBox(
                "Select the type of crowd you want to create. This determines the behavior and available settings.",
                MessageType.Info);
            
            GUILayout.Space(15);
            
            // Combat Crowd Option
            DrawCrowdTypeOption(CrowdType.Combat, "Combat Crowd", 
                "Standard enemy units that fight, chase, and use formations against a player.",
                "Features: Combat AI, Formations, Aggro System, Player Targeting.");
                
            GUILayout.Space(10);
            
            // Battle Simulation Option
            DrawCrowdTypeOption(CrowdType.BattleSimulation, "Battle Simulation (RTS)", 
                "Armies that fight other factions automatically.",
                "Features: Faction vs Faction Battles, RTS Style Combat, Auto-Targeting.");
                
            GUILayout.Space(10);
            
            // Civil Crowd Option
            DrawCrowdTypeOption(CrowdType.Civil, "Civil Crowd", 
                "Neutral NPCs that wander, follow paths, or perform ambient behaviors.",
                "Features: Waypoint Navigation, Free Roam, Ambient Animations. Combat is disabled.");
        }
        
        void DrawCrowdTypeOption(CrowdType type, string title, string description, string features)
        {
            bool isSelected = selectedCrowdType == type;
            
            var originalColor = GUI.backgroundColor;
            if (isSelected)
            {
                GUI.backgroundColor = new Color(0.3f, 0.6f, 0.3f, 1f);
            }
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Toggle(isSelected, "", GUILayout.Width(20)))
            {
                selectedCrowdType = type;
            }
            
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(description, descriptionStyle);
            GUILayout.Space(5);
            EditorGUILayout.LabelField(features, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            
            GUI.backgroundColor = originalColor;
        }

        void DrawAgentArchetypePage()
        {
            EditorGUILayout.LabelField("Agent Archetype", headerStyle);

            GUILayout.Space(10);

            EditorGUILayout.HelpBox(
                "Select the physical form of your crowd agents. This determines which animation clips are required, " +
                "navigation mode, hitbox preset, and formation behavior.",
                MessageType.Info);

            GUILayout.Space(15);

            // --- Archetype Selection ---
            DrawArchetypeOption(AgentArchetype.Humanoid, "Humanoid",
                "Standard bipedal characters (soldiers, zombies, civilians).",
                "Requires: Idle, Walk, Run, Attack, Hit, Death clips. Uses ground navigation.");

            GUILayout.Space(8);

            DrawArchetypeOption(AgentArchetype.FlyingHumanoid, "Flying Humanoid",
                "Humanoid characters that can fly (dragons with riders, jetpack soldiers, angels).",
                "Requires: All humanoid clips. Uses 3D flight navigation + altitude control.");

            GUILayout.Space(8);

            DrawArchetypeOption(AgentArchetype.FlyingVehicle, "Flying Vehicle / Spaceship",
                "Non-humanoid flying entities: spaceships, drones, aircraft, magic carpets.",
                "No locomotion/combat animations required. Uses 3D flight navigation.");

            GUILayout.Space(8);

            DrawArchetypeOption(AgentArchetype.GroundVehicle, "Ground Vehicle",
                "Non-humanoid ground entities: tanks, cars, siege engines.",
                "No locomotion/combat animations required. Uses ground navigation.");

            GUILayout.Space(15);

            // --- Archetype-specific settings ---
            bool isFlying = selectedAgentArchetype == AgentArchetype.FlyingHumanoid ||
                            selectedAgentArchetype == AgentArchetype.FlyingVehicle;
            bool isNonHumanoid = selectedAgentArchetype == AgentArchetype.FlyingVehicle ||
                                 selectedAgentArchetype == AgentArchetype.GroundVehicle;

            if (isFlying)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Flight Settings", EditorStyles.boldLabel);

                flight3DNavType = (NavigationType3D)EditorGUILayout.EnumPopup(
                    new GUIContent("3D Steering Mode",
                        "Boids: Emergent flocking. Direct Steering: Simple velocity. Waypoint3D: Follow path nodes."),
                    flight3DNavType);

                cruisingAltitude = EditorGUILayout.FloatField(
                    new GUIContent("Cruising Altitude", "Default height above terrain (world Y)."),
                    cruisingAltitude);
                cruisingAltitude = Mathf.Max(1f, cruisingAltitude);

                altitudeVariance = EditorGUILayout.FloatField(
                    new GUIContent("Altitude Variance", "Random per-agent offset to avoid flat sheets."),
                    altitudeVariance);
                altitudeVariance = Mathf.Max(0f, altitudeVariance);

                verticalSpeed = EditorGUILayout.FloatField(
                    new GUIContent("Vertical Speed", "How fast agents climb/descend to target altitude."),
                    verticalSpeed);
                verticalSpeed = Mathf.Max(0.5f, verticalSpeed);

                if (selectedAgentArchetype == AgentArchetype.FlyingVehicle)
                {
                    bankAngle = EditorGUILayout.Slider(
                        new GUIContent("Bank Angle", "Max roll angle when turning (visual effect)."),
                        bankAngle, 0f, 60f);

                    bankSpeed = EditorGUILayout.Slider(
                        new GUIContent("Bank Speed", "How quickly the model rolls into/out of banks."),
                        bankSpeed, 10f, 360f);

                    headingSmoothRate = EditorGUILayout.Slider(
                        new GUIContent("Heading Smooth Rate", "How quickly heading follows velocity. Lower = smoother arcs."),
                        headingSmoothRate, 1f, 10f);

                    maxPitchAngle = EditorGUILayout.Slider(
                        new GUIContent("Max Pitch Angle", "Max nose-up/down pitch when climbing or descending."),
                        maxPitchAngle, 0f, 60f);
                }

                GUILayout.Space(5);

                EditorGUILayout.LabelField("3D Formation Layers", EditorStyles.boldLabel);
                formationLayers = EditorGUILayout.IntSlider(
                    new GUIContent("Layers", "Number of vertical layers for 3D formations."),
                    formationLayers, 1, 10);

                formationLayerSpacing = EditorGUILayout.FloatField(
                    new GUIContent("Layer Spacing", "Vertical distance between formation layers."),
                    formationLayerSpacing);
                formationLayerSpacing = Mathf.Max(1f, formationLayerSpacing);

                EditorGUILayout.EndVertical();
            }

            if (isNonHumanoid)
            {
                GUILayout.Space(10);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Vehicle / Non-Humanoid Settings", EditorStyles.boldLabel);

                EditorGUILayout.HelpBox(
                    "Non-humanoid agents use a simplified hitbox (bounding volume) instead of " +
                    "body-part zones. Locomotion and combat animation clips are not required.",
                    MessageType.None);

                selectedHitboxPreset = (HitboxPreset)EditorGUILayout.EnumPopup(
                    new GUIContent("Hitbox Preset", "Humanoid: zone-based (head, torso, limbs). Vehicle: single hull zone. Custom: manual setup later."),
                    selectedHitboxPreset);

                vehicleBoundingRadius = EditorGUILayout.FloatField(
                    new GUIContent("Bounding Radius", "Approximate collision radius of the vehicle mesh."),
                    vehicleBoundingRadius);
                vehicleBoundingRadius = Mathf.Max(0.1f, vehicleBoundingRadius);

                vehicleBoundingHeight = EditorGUILayout.FloatField(
                    new GUIContent("Bounding Height", "Approximate height of the vehicle mesh."),
                    vehicleBoundingHeight);
                vehicleBoundingHeight = Mathf.Max(0.1f, vehicleBoundingHeight);

                EditorGUILayout.EndVertical();
            }
        }

        void DrawArchetypeOption(AgentArchetype archetype, string title, string description, string features)
        {
            bool isSelected = selectedAgentArchetype == archetype;

            var originalColor = GUI.backgroundColor;
            if (isSelected)
                GUI.backgroundColor = new Color(0.3f, 0.6f, 0.3f, 1f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Toggle(isSelected, "", GUILayout.Width(20)))
            {
                selectedAgentArchetype = archetype;
                // Auto-set hitbox preset based on archetype
                bool nonHumanoid = archetype == AgentArchetype.FlyingVehicle || archetype == AgentArchetype.GroundVehicle;
                selectedHitboxPreset = nonHumanoid ? HitboxPreset.Vehicle : HitboxPreset.Humanoid;
            }

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(description, descriptionStyle);
            GUILayout.Space(3);
            EditorGUILayout.LabelField(features, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            GUI.backgroundColor = originalColor;
        }

        void DrawAnimatorWorkflowPage()
        {
            EditorGUILayout.LabelField("Animator Workflow", headerStyle);
            
            GUILayout.Space(10);
            
            EditorGUILayout.HelpBox(
                "Choose how animations are processed. This is a critical performance setting for GPU Instancing.",
                MessageType.Info);
            
            GUILayout.Space(15);
            
            // Workflow Selection
            animatorWorkflow = (AnimatorWorkflowOverride)EditorGUILayout.EnumPopup("Workflow", animatorWorkflow);
            
            GUILayout.Space(15);
            
            // Explanations
            EditorGUILayout.LabelField("Workflow Details:", EditorStyles.boldLabel);
            
            switch (animatorWorkflow)
            {
                case AnimatorWorkflowOverride.ComputeAnimator:
                    EditorGUILayout.HelpBox(
                        "Compute Animator (Recommended for Masses)\n\n" +
                        "Runs animations entirely on the GPU. Extremely fast and supports thousands of units.\n" +
                        "Pros: Best performance, massive scale.\n" +
                        "Cons: Requires baking animations (handled by GPUI Pro). No root motion support.",
                        MessageType.Info);
                    break;
                    
                case AnimatorWorkflowOverride.MecanimReader:
                    EditorGUILayout.HelpBox(
                        "Mecanim Reader\n\n" +
                        "Uses Unity's standard Animator component and reads the state to the GPU.\n" +
                        "Pros: Full compatibility with Animator Controllers, Blend Trees, and Root Motion.\n" +
                        "Cons: Expensive on CPU. Not recommended for > 500 units.",
                        MessageType.Warning);
                    break;
                    
                case AnimatorWorkflowOverride.BoneTracker:
                    EditorGUILayout.HelpBox(
                        "Bone Tracker\n\n" +
                        "Hybrid approach that tracks bone positions on CPU.\n" +
                        "Pros: Allows attaching items (weapons) to hands dynamically.\n" +
                        "Cons: More expensive than Compute Animator.",
                        MessageType.Info);
                    break;
                    
                case AnimatorWorkflowOverride.KeepPrefabSetting:
                    EditorGUILayout.HelpBox(
                        "Keep Prefab Setting\n\n" +
                        "Uses whatever is configured on the GPUI Prefab component.",
                        MessageType.None);
                    break;
            }

            GUILayout.Space(15);

            EditorGUILayout.HelpBox(
                "Pro Tip: Weapon Attachments\n\n" +
                "Bone Tracker allows attaching weapons to bones dynamically but is more expensive.\n" +
                "If you use Compute Animator (faster) and need weapons, use a model with the weapon already attached in the FBX.\n" +
                "To 'sheathe' the weapon with Compute Animator, use Shader/Material tricks (like alpha clipping) to hide it.",
                MessageType.Info);
        }

        void DrawAnimationSetupPage()
        {
            bool isNonHumanoid = selectedAgentArchetype == AgentArchetype.FlyingVehicle ||
                                  selectedAgentArchetype == AgentArchetype.GroundVehicle;

            EditorGUILayout.LabelField("Animation Setup", headerStyle);
            
            GUILayout.Space(10);

            if (isNonHumanoid)
            {
                EditorGUILayout.HelpBox(
                    "Non-humanoid agents (vehicles, spaceships) typically have no locomotion or combat animations.\n\n" +
                    "You may optionally assign clips for visual effects (e.g., turret rotation, engine glow) but " +
                    "none are required. You can configure them later on the EnemyCrowd asset.",
                    MessageType.Info);

                GUILayout.Space(15);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Locomotion (Optional)", EditorStyles.boldLabel);

                idleClip = (AnimationClip)EditorGUILayout.ObjectField("Idle Clip (Optional)", idleClip, typeof(AnimationClip), false);
                walkClip = (AnimationClip)EditorGUILayout.ObjectField("Walk Clip (Optional)", walkClip, typeof(AnimationClip), false);
                runClip = (AnimationClip)EditorGUILayout.ObjectField("Run Clip (Optional)", runClip, typeof(AnimationClip), false);

                EditorGUILayout.EndVertical();

                GUILayout.Space(10);

                if (selectedCrowdType != CrowdType.Civil)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField("Combat (Optional)", EditorStyles.boldLabel);

                    DrawAnimationArray("Attack Clips", ref attackClips);
                    DrawAnimationArray("Hit Clips", ref hitClips);
                    DrawAnimationArray("Death Clips", ref deathClips);

                    EditorGUILayout.EndVertical();
                }
            }
            else
            {
                // Humanoid / FlyingHumanoid — standard required clips
                EditorGUILayout.HelpBox(
                    "Assign the essential animation clips for your crowd. These will override the global defaults.",
                    MessageType.Info);

                GUILayout.Space(15);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Locomotion (Required)", EditorStyles.boldLabel);

                idleClip = (AnimationClip)EditorGUILayout.ObjectField("Idle Clip", idleClip, typeof(AnimationClip), false);
                walkClip = (AnimationClip)EditorGUILayout.ObjectField("Walk Clip", walkClip, typeof(AnimationClip), false);
                runClip = (AnimationClip)EditorGUILayout.ObjectField("Run Clip", runClip, typeof(AnimationClip), false);

                EditorGUILayout.EndVertical();

                GUILayout.Space(10);

                if (selectedCrowdType != CrowdType.Civil)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField("Combat (Required for Combat)", EditorStyles.boldLabel);

                    DrawAnimationArray("Attack Clips", ref attackClips);
                    DrawAnimationArray("Hit Clips", ref hitClips);
                    DrawAnimationArray("Death Clips", ref deathClips);

                    EditorGUILayout.EndVertical();
                }
                else
                {
                    EditorGUILayout.HelpBox("Civil crowds typically only need locomotion animations. You can add specific action animations later.", MessageType.None);
                }
            }
        }
        
        void DrawAnimationArray(string label, ref AnimationClip[] clips)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(100));
            
            if (clips == null) clips = new AnimationClip[0];
            
            if (GUILayout.Button("+", GUILayout.Width(20)))
            {
                Array.Resize(ref clips, clips.Length + 1);
            }
            
            if (clips.Length > 0 && GUILayout.Button("-", GUILayout.Width(20)))
            {
                Array.Resize(ref clips, clips.Length - 1);
            }
            
            EditorGUILayout.EndHorizontal();
            
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i] = (AnimationClip)EditorGUILayout.ObjectField(clips[i], typeof(AnimationClip), false);
            }
            
            GUILayout.Space(5);
        }

        void DrawClimbingSetupPage()
        {
            EditorGUILayout.LabelField("Climbing Setup", headerStyle);
            
            GUILayout.Space(10);
            
            EditorGUILayout.HelpBox(
                "Enable climbing to allow agents to traverse NavMeshLinks for climbing walls, ladders, and vertical surfaces.\n" +
                "Uses Unity's NavMeshLink system for efficient pathfinding integration.\n" +
                "Perfect for siege scenarios (castle walls), zombie hordes (World War Z style), and multi-level navigation.",
                MessageType.Info);
            
            GUILayout.Space(15);
            
            // Enable climbing toggle
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Enable NavMesh Link Climbing", EditorStyles.boldLabel);
            
            enableClimbing = EditorGUILayout.Toggle(
                new GUIContent("Enable Climbing System", "Allow agents to climb via NavMeshLinks"),
                enableClimbing);
            
            if (enableClimbing)
            {
                EditorGUILayout.HelpBox(
                    "The NavMeshLinkTraversalHandler and NavMeshLinkCrowdAnimationHandler components will be added to your scene.\n" +
                    "Add NavMeshLinkClimbable components to NavMeshLink GameObjects to define climbable surfaces.",
                    MessageType.None);
            }
            
            EditorGUILayout.EndVertical();
            
            if (!enableClimbing)
            {
                GUILayout.Space(10);
                EditorGUILayout.HelpBox(
                    "You can enable climbing later by adding the NavMeshLinkTraversalHandler component to your scene " +
                    "and configuring climbing animations in your EnemyCrowd asset.",
                    MessageType.None);
                return;
            }
            
            GUILayout.Space(15);
            
            // Animation configuration based on workflow
            bool isComputeAnimator = animatorWorkflow == AnimatorWorkflowOverride.ComputeAnimator;
            
            if (isComputeAnimator)
            {
                // Compute Animator Clips
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Climbing Animation Clips (Compute Animator)", EditorStyles.boldLabel);
                
                climbUpClip = (AnimationClip)EditorGUILayout.ObjectField(
                    new GUIContent("Climb Up", "Looping animation for climbing upward"),
                    climbUpClip, typeof(AnimationClip), false);
                    
                climbDownClip = (AnimationClip)EditorGUILayout.ObjectField(
                    new GUIContent("Climb Down", "Looping animation for climbing downward"),
                    climbDownClip, typeof(AnimationClip), false);
                    
                climbMountClip = (AnimationClip)EditorGUILayout.ObjectField(
                    new GUIContent("Mount (Optional)", "Transition from ground to climbing"),
                    climbMountClip, typeof(AnimationClip), false);
                    
                climbDismountClip = (AnimationClip)EditorGUILayout.ObjectField(
                    new GUIContent("Dismount (Optional)", "Transition from climbing to ground"),
                    climbDismountClip, typeof(AnimationClip), false);
                    
                climbFallClip = (AnimationClip)EditorGUILayout.ObjectField(
                    new GUIContent("Fall (Optional)", "Animation when knocked off or falling from edge"),
                    climbFallClip, typeof(AnimationClip), false);
                
                EditorGUILayout.EndVertical();
            }
            else
            {
                // Mecanim Parameters
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Climbing Mecanim Parameters", EditorStyles.boldLabel);
                
                EditorGUILayout.HelpBox(
                    "Configure the Animator Controller parameters that control climbing states.\n" +
                    "These should match your Animator Controller setup.",
                    MessageType.None);
                
                GUILayout.Space(5);
                
                climbingBoolParameter = EditorGUILayout.TextField(
                    new GUIContent("Climbing Bool", "Bool parameter to enable climbing state"),
                    climbingBoolParameter);
                    
                climbSpeedParameter = EditorGUILayout.TextField(
                    new GUIContent("Climb Speed Float", "Float parameter for climb direction (-1 down, 1 up)"),
                    climbSpeedParameter);
                    
                climbMountTrigger = EditorGUILayout.TextField(
                    new GUIContent("Mount Trigger", "Trigger to start mounting animation"),
                    climbMountTrigger);
                    
                climbDismountTrigger = EditorGUILayout.TextField(
                    new GUIContent("Dismount Trigger", "Trigger to start dismounting animation"),
                    climbDismountTrigger);
                    
                climbFallTrigger = EditorGUILayout.TextField(
                    new GUIContent("Fall Trigger", "Trigger for fall animation"),
                    climbFallTrigger);
                
                EditorGUILayout.EndVertical();
            }
            
            GUILayout.Space(10);
            
            // Timing settings (both workflows)
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Timing", EditorStyles.boldLabel);
            
            climbMountDuration = EditorGUILayout.FloatField(
                new GUIContent("Mount Duration", "Time to transition from ground to climbing"),
                climbMountDuration);
            climbMountDuration = Mathf.Max(0.1f, climbMountDuration);
            
            climbDismountDuration = EditorGUILayout.FloatField(
                new GUIContent("Dismount Duration", "Time to transition from climbing to ground"),
                climbDismountDuration);
            climbDismountDuration = Mathf.Max(0.1f, climbDismountDuration);
            
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(10);
            
            // Edge Fall Detection settings
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Edge Fall Detection", EditorStyles.boldLabel);
            
            EditorGUILayout.HelpBox(
                "When agents reach the top of a wall and get pushed by the Fake Physics system, " +
                "they can fall off and take damage. Great for siege mechanics!",
                MessageType.None);
            
            enableEdgeFallDetection = EditorGUILayout.Toggle(
                new GUIContent("Enable Edge Fall Detection", "Track agents after climbing for push-off detection"),
                enableEdgeFallDetection);
            
            if (enableEdgeFallDetection)
            {
                enableFallDamage = EditorGUILayout.Toggle(
                    new GUIContent("Enable Fall Damage", "Apply damage based on fall height"),
                    enableFallDamage);
                
                if (enableFallDamage)
                {
                    fallDamagePerUnit = EditorGUILayout.FloatField(
                        new GUIContent("Damage Per Meter", "Damage applied per meter of fall height"),
                        fallDamagePerUnit);
                    fallDamagePerUnit = Mathf.Max(0f, fallDamagePerUnit);
                }
            }
            
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(10);
            
            // NavMeshLink Traversal Settings
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("NavMeshLink Traversal", EditorStyles.boldLabel);
            
            EditorGUILayout.HelpBox(
                "The NavMeshLink-based climbing system integrates with Unity's pathfinding:\n\n" +
                "• Agents automatically traverse NavMeshLinks during navigation\n" +
                "• NavMeshLinkClimbable components define climb animations and behavior\n" +
                "• Proximity detection allows awareness of nearby traversable links\n" +
                "• Full GPU Instancer Pro animation support via NavMeshLinkCrowdAnimationHandler",
                MessageType.None);
            
            GUILayout.Space(5);
            
            EditorGUILayout.HelpBox(
                "SETUP OVERVIEW:\n" +
                "1. Create NavMeshLinks in your scene (AI > Navigation > NavMesh Link)\n" +
                "2. Add NavMeshLinkClimbable to NavMeshLink objects for climbing behavior\n" +
                "3. Configure climb animations in your CrowdPrototype asset\n" +
                "4. The traversal handler manages link detection and agent coordination",
                MessageType.Info);
            
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            
            // Use cases
            EditorGUILayout.HelpBox(
                "USE CASES:\n\n" +
                "• Siege Warfare - Enemies scale castle walls with ladders\n" +
                "• Zombie Hordes - World War Z style swarm climbing\n" +
                "• Multi-Level Maps - Navigate between elevation levels\n" +
                "• Boarding Actions - Climb onto ships or vehicles",
                MessageType.None);
        }

        void DrawCreateFactionPage()
        {
            EditorGUILayout.LabelField("Create Your Faction", headerStyle);
            
            GUILayout.Space(10);
            
            EditorGUILayout.HelpBox(
                "Let's define your enemy faction. This information helps organize your enemies " +
                "and can be used for faction-based combat (enemies vs allies).",
                MessageType.Info);
            
            GUILayout.Space(15);
            
            // Faction settings
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField("Faction Identity", EditorStyles.boldLabel);
            GUILayout.Space(5);
            
            factionName = EditorGUILayout.TextField(
                new GUIContent("Faction Name", "The group this enemy belongs to (e.g., 'Undead Legion', 'Forest Tribe')"),
                factionName);
            
            enemyTypeName = EditorGUILayout.TextField(
                new GUIContent("Enemy Type", "Specific role within the faction (e.g., 'Soldier', 'Archer', 'Elite')"),
                enemyTypeName);
            
            factionColor = EditorGUILayout.ColorField(
                new GUIContent("Faction Color", "Visual identifier for this faction (used in UI, minimaps, etc.)"),
                factionColor);
            
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            
            // Preview
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            
            EditorGUILayout.BeginHorizontal();
            
            // Color preview
            var colorRect = GUILayoutUtility.GetRect(30, 30, GUILayout.Width(30), GUILayout.Height(30));
            EditorGUI.DrawRect(colorRect, factionColor);
            
            GUILayout.Space(10);
            
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField($"<b>{factionName}</b>", descriptionStyle);
            EditorGUILayout.LabelField($"Type: {enemyTypeName}", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            
            // Examples
            EditorGUILayout.HelpBox(
                "EXAMPLES:\n" +
                "- Faction: 'Goblin Horde' -> Types: 'Grunt', 'Shaman', 'Warchief'\n" +
                "- Faction: 'Royal Guard' -> Types: 'Knight', 'Archer', 'Mage'\n" +
                "- Faction: 'Zombie Apocalypse' -> Types: 'Walker', 'Runner', 'Tank'",
                MessageType.None);
        }

        void DrawNavigationTypeSetupPage()
        {
            EditorGUILayout.LabelField("Navigation Type", headerStyle);

            GUILayout.Space(10);

            EditorGUILayout.HelpBox(
                "Choose the navigation backend for this EnemyCrowd. " +
                "This will be written to EnemyCrowd.navigationType and used at runtime for spawned agents.",
                MessageType.Info);

            GUILayout.Space(15);

            RefreshNavigationTypeOptions();

            if (availableNavigationTypes == null || availableNavigationTypes.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No navigation backends are available. Falling back to Unity NavMesh.",
                    MessageType.Warning);
                selectedNavigationType = NavigationType.UnityNavMesh;
                return;
            }

            string[] typeNames = new string[availableNavigationTypes.Length];
            for (int i = 0; i < availableNavigationTypes.Length; i++)
                typeNames[i] = availableNavigationTypes[i].GetDisplayName();

            int selectedIndex = Array.IndexOf(availableNavigationTypes, selectedNavigationType);
            if (selectedIndex < 0)
                selectedIndex = 0;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Backend Selection", EditorStyles.boldLabel);
            selectedIndex = EditorGUILayout.Popup("Navigation Backend", selectedIndex, typeNames);
            selectedNavigationType = availableNavigationTypes[selectedIndex];
            EditorGUILayout.EndVertical();

            GUILayout.Space(10);

            EditorGUILayout.HelpBox(selectedNavigationType.GetTooltip(), MessageType.None);

            if (selectedCrowdType == CrowdType.Civil && selectedNavigationType != NavigationType.UnityNavMesh)
            {
                EditorGUILayout.HelpBox(
                    "Civil systems are primarily tuned for Unity NavMesh. " +
                    "Flow Field/A* may require additional custom setup for full parity.",
                    MessageType.Warning);
            }

            if (selectedNavigationType == NavigationType.FlowField)
            {
                EditorGUILayout.HelpBox(
                    "Flow Field works best for large crowd movement and shared destinations. " +
                    "Ensure a FlowFieldRuntimeHost is present in your scene.",
                    MessageType.Info);
            }
        }

        void DrawCivilSettingsPage()
        {
            EditorGUILayout.LabelField("Civil Settings", headerStyle);
            
            GUILayout.Space(10);
            
            EditorGUILayout.HelpBox(
                "Configure how your civil crowd behaves. These settings control navigation and movement style.",
                MessageType.Info);
            
            GUILayout.Space(15);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Navigation Mode
            EditorGUILayout.LabelField("Navigation Mode", EditorStyles.boldLabel);
            civilNavMode = (CivilNavigationMode)EditorGUILayout.EnumPopup("Mode", civilNavMode);
            
            if (civilNavMode == CivilNavigationMode.FollowConnections)
            {
                EditorGUILayout.HelpBox(
                    "Follow Connections: Agents move between connected waypoints in a graph.\n" +
                    "Best for: City streets, defined paths, traffic flow.", 
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Free Roam: Agents wander randomly to any reachable point on the NavMesh.\n" +
                    "Best for: Parks, open squares, grazing animals.", 
                    MessageType.None);
            }
            
            GUILayout.Space(10);
            
            // Movement Style
            EditorGUILayout.LabelField("Movement Style", EditorStyles.boldLabel);
            civilMoveStyle = (CivilMovementStyle)EditorGUILayout.EnumPopup("Style", civilMoveStyle);
            
            string styleDesc = "";
            switch (civilMoveStyle)
            {
                case CivilMovementStyle.Walk: styleDesc = "Standard walking speed."; break;
                case CivilMovementStyle.Stroll: styleDesc = "Slow, relaxed pace."; break;
                case CivilMovementStyle.Hurried: styleDesc = "Fast walk, busy pace."; break;
                case CivilMovementStyle.Varied: styleDesc = "Randomly varies between stroll/walk/hurried for natural crowds."; break;
            }
            EditorGUILayout.HelpBox(styleDesc, MessageType.None);
            
            EditorGUILayout.EndVertical();
        }

        void DrawCombatStylePage()
        {
            EditorGUILayout.LabelField("Choose Combat Style", headerStyle);
            
            GUILayout.Space(10);
            
            EditorGUILayout.HelpBox(
                "Select a combat behavior template. Each template configures AI behavior, " +
                "positioning, and attack patterns automatically. You can always customize later!",
                MessageType.Info);
            
            GUILayout.Space(15);
            
            // Combat template selection with visual cards
            DrawCombatTemplateOption(CombatBehaviorTemplate.MeleeSwarm, 
                "Melee Swarm",
                "All enemies rush in simultaneously.\nPerfect for: Vampire Survivors, zombie hordes",
                "Fast, aggressive, overwhelming numbers");
            
            DrawCombatTemplateOption(CombatBehaviorTemplate.MeleeQueued,
                "Melee Queued",
                "Enemies circle and take turns attacking.\nPerfect for: Batman, Spider-Man combat",
                "Tactical, one-at-a-time, dramatic");
            
            DrawCombatTemplateOption(CombatBehaviorTemplate.MeleeBattleLine,
                "Battle Line",
                "Military formation with organized ranks.\nPerfect for: Total War, medieval battles",
                "Organized, disciplined, strategic");
            
            DrawCombatTemplateOption(CombatBehaviorTemplate.RangedStationary,
                "Ranged Stationary",
                "Archers/gunners that maintain distance.\nPerfect for: Tower defense, cover shooters",
                "Keep distance, fire at will");
            
            DrawCombatTemplateOption(CombatBehaviorTemplate.HybridAdaptive,
                "Hybrid Adaptive",
                "Switches between ranged and melee.\nPerfect for: Versatile soldiers, mages",
                "Intelligent, range-based switching");
            
            DrawCombatTemplateOption(CombatBehaviorTemplate.MeleeWaves,
                "Wave Attack",
                "Enemies attack in organized waves.\nPerfect for: Diablo 4, wave survival",
                "Group coordination, rest periods");
            
            GUILayout.Space(10);
            
            EditorGUILayout.HelpBox(
                "TIP: 'Melee Queued' is great for beginners - it creates cinematic combat " +
                "where enemies don't all attack at once, giving the player breathing room.",
                MessageType.None);
        }

        void DrawCombatTemplateOption(CombatBehaviorTemplate template, string title, string description, string tagline)
        {
            bool isSelected = combatTemplate == template;
            
            // Card style based on selection
            var originalColor = GUI.backgroundColor;
            if (isSelected)
            {
                GUI.backgroundColor = new Color(0.3f, 0.6f, 0.3f, 1f);
            }
            
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            
            // Selection radio
            if (GUILayout.Toggle(isSelected, "", GUILayout.Width(20)))
            {
                combatTemplate = template;
            }
            
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(description, descriptionStyle);
            EditorGUILayout.LabelField($"<i>{tagline}</i>", descriptionStyle);
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndHorizontal();
            
            GUI.backgroundColor = originalColor;
            GUILayout.Space(3);
        }

        void DrawRTSSetupPage()
        {
            EditorGUILayout.LabelField("RTS Configuration", headerStyle);
            
            GUILayout.Space(10);
            
            EditorGUILayout.HelpBox(
                "Configure the Real-Time Strategy (RTS) aspects of your simulation. " +
                "This enables player control and camera interaction.",
                MessageType.Info);
            
            GUILayout.Space(15);
            
            // RTS Controller Explanation
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("EnemyMassesRTSController", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "This component handles player input, unit selection, and issuing commands (Move, Attack). " +
                "It will be automatically added to your scene controller.",
                descriptionStyle);
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            
            // Player Control
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Faction Control", EditorStyles.boldLabel);
            
            isPlayerControlledFaction = EditorGUILayout.ToggleLeft(
                new GUIContent("Is Player Controlled?", "Check this if the player should be able to select and command this faction."),
                isPlayerControlledFaction);
                
            if (isPlayerControlledFaction)
            {
                EditorGUILayout.HelpBox(
                    $"The faction '{factionName}' will be added to the 'Player Factions' list in the RTS Controller.\n" +
                    "Selection will be restricted to player-controlled factions.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "This faction will be AI-controlled (Enemy). The player cannot select or command these units.",
                    MessageType.None);
            }
            
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            
            // World Size
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("World Settings", EditorStyles.boldLabel);
            
            worldSize = EditorGUILayout.Vector2Field(
                new GUIContent("World Size", "The dimensions of your playable area (Width x Length)."),
                worldSize);
                
            EditorGUILayout.HelpBox(
                "IMPORTANT: This defines the boundaries for the Camera and Minimap.\n" +
                "Set this to match your terrain or level size (e.g., 1000x1000) to ensure the minimap covers the correct area " +
                "and the camera doesn't go out of bounds.",
                MessageType.Info);
            
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            
            // Camera Control Scheme
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("RTS Camera Setup", EditorStyles.boldLabel);
            
            string[] presetOptions = new string[] {
                "Classic RTS",
                "Modern RTS", 
                "Third Person",
                "Total War",
                "Supreme Commander"
            };
            
            rtsPresetType = EditorGUILayout.Popup(
                new GUIContent("Default Preset", "The camera preset style to create. You can add more presets later."),
                rtsPresetType, presetOptions);
                
            string schemeDesc = rtsPresetType switch
            {
                0 => "Classic RTS: Edge pan + Middle mouse drag + Scroll zoom (StarCraft style)",
                1 => "Modern RTS: WASD movement + Right mouse drag rotate + Scroll zoom (Company of Heroes style)",
                2 => "Third Person: Follow target + Right mouse drag rotate + Scroll zoom",
                3 => "Total War: Edge pan + Right drag pan + Middle drag rotate",
                4 => "Supreme Commander: Edge pan + Middle drag pan + Right drag rotate",
                _ => "Select a camera preset style"
            };
            EditorGUILayout.HelpBox(schemeDesc, MessageType.None);
            EditorGUILayout.HelpBox(
                "The wizard will create a RTSCameraPreset ScriptableObject asset. You can add more presets to the RTSCameraController after setup.",
                MessageType.Info);
            
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            
            // Minimap Setup
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Minimap Setup", EditorStyles.boldLabel);
            
            createMinimap = EditorGUILayout.ToggleLeft(
                new GUIContent("Create Minimap System", "Automatically set up a minimap and UI."),
                createMinimap);
                
            if (createMinimap)
            {
                EditorGUI.indentLevel++;
                minimapMode = EditorGUILayout.Popup(
                    new GUIContent("Implementation", "Choose the minimap rendering approach."),
                    minimapMode,
                    new[] { "Camera-Free (Recommended)", "Camera-Based (Legacy)" });

                if (minimapMode == 0)
                {
                    EditorGUILayout.HelpBox(
                        "Camera-Free minimap renders agent markers as lightweight UI elements.\n" +
                        "Works with Built-in, URP, and HDRP — no second camera or RenderTexture needed.",
                        MessageType.Info);

                    createAlertSystem = EditorGUILayout.ToggleLeft(
                        new GUIContent("Add Alert System", "Adds an AoE2-style under-attack alert system that flashes minimap markers when agents are attacked."),
                        createAlertSystem);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "NOTE: The camera-based minimap requires the Universal Render Pipeline (URP).\n" +
                        "It will create a new Camera, Canvas, and UI elements in your scene.",
                        MessageType.Warning);
                }
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            
            // Fog of War Setup
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Fog of War Setup", EditorStyles.boldLabel);
            
            createFogOfWar = EditorGUILayout.ToggleLeft(
                new GUIContent("Create Fog of War", "Add a Fog of War system to hide unexplored areas."),
                createFogOfWar);
                
            if (createFogOfWar)
            {
                EditorGUILayout.HelpBox(
                    "Creates an optimized fog of war for thousands of agents.\n" +
                    "It will create a 'FogOfWar' GameObject at (0,0,0) with default settings.\n\n" +
                    "NOTE: Fog of War requires the Universal Render Pipeline (URP). The shader is NOT compatible with HDRP.",
                    MessageType.Warning);
            }
            
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            
            // Additional Setup Info
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Additional Setup Required", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "The wizard will set up the core RTS systems, but you may want to customize the following later on the EnemyMassesRTSController component:",
                descriptionStyle);
            
            GUILayout.Space(5);
            DrawFeatureItem("Visual Settings", "Selection circles, drag rectangles, colors");
            DrawFeatureItem("Input Settings", "Keybindings for selection and commands");
            DrawFeatureItem("Destination Indicators", "Visual markers for move commands");
            
            EditorGUILayout.EndVertical();
        }

        void DrawAssignPrefabPage()
        {
            bool isNonHumanoidPrefab = selectedAgentArchetype == AgentArchetype.FlyingVehicle ||
                                        selectedAgentArchetype == AgentArchetype.GroundVehicle;

            string pageTitle = isNonHumanoidPrefab ? "Assign Vehicle Prefab" : "Assign Character Prefab";
            EditorGUILayout.LabelField(pageTitle, headerStyle);
            
            GUILayout.Space(10);

            string helpText = isNonHumanoidPrefab
                ? "Drag your vehicle/object prefab here. A MeshRenderer or SkinnedMeshRenderer is supported. " +
                  "No Animator is required for non-humanoid agents. Enemy Masses will automatically " +
                  $"set up the required components ({GetPrimaryNavigationRequirementName()}, GPU Instancer)."
                : "Drag your animated character prefab here. Enemy Masses will automatically " +
                  $"set up the required components ({GetPrimaryNavigationRequirementName()}, GPU Instancer).";
            EditorGUILayout.HelpBox(helpText, MessageType.Info);
            
            GUILayout.Space(10);
            
            // Performance Pro Tip
            EditorGUILayout.BeginVertical(tipBoxStyle);
            EditorGUILayout.LabelField("<b>Pro Tip: Performance Considerations</b>", descriptionStyle);
            EditorGUILayout.LabelField(
                "GPU Instancing helps with rendering, but it doesn't eliminate optimization work. " +
                "Always think in context: everything is multiplied by thousands.\n\n" +
                "- Higher polycount = fewer agents you can spawn before hitting performance limits\n" +
                "- More materials/textures = higher GPU memory usage per agent\n" +
                "- Enemy Masses has no auto-LOD (LOD switching is CPU-intensive with thousands of agents)\n" +
                "- Profile your game early to find the right balance between visual quality and agent count",
                descriptionStyle);
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            
            // Prefab field with large drop area
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField(isNonHumanoidPrefab ? "Vehicle Prefab" : "Character Prefab", EditorStyles.boldLabel);
            GUILayout.Space(5);
            
            selectedPrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("Prefab", isNonHumanoidPrefab ? "Your vehicle/object model" : "Your animated character model"),
                selectedPrefab,
                typeof(GameObject),
                false,
                GUILayout.Height(50));
            
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            
            // Validation
            if (selectedPrefab != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Prefab Validation", EditorStyles.boldLabel);
                
                // Check for required components
                bool hasSkinnedMesh = selectedPrefab.GetComponentInChildren<SkinnedMeshRenderer>() != null;
                bool hasMeshRenderer = selectedPrefab.GetComponentInChildren<MeshRenderer>() != null;
                bool hasAnyRenderer = hasSkinnedMesh || hasMeshRenderer;
                bool hasAnimatorOnRoot = selectedPrefab.GetComponent<Animator>() != null;
                bool hasAnimatorAnywhere = selectedPrefab.GetComponentInChildren<Animator>() != null;
                bool hasGPUIPrefab = selectedPrefab.GetComponent<GPUIPrefab>() != null;
                bool hasGPUICrowd = selectedPrefab.GetComponent<GPUICrowdInstance>() != null;
                GetNavigationValidationForPrefab(
                    selectedPrefab,
                    out string movementPrimaryLabel,
                    out bool hasMovementPrimary,
                    out string movementPrimaryNote,
                    out bool hasMovementSecondaryRequirement,
                    out string movementSecondaryLabel,
                    out bool hasMovementSecondary,
                    out string movementSecondaryNote);

                if (isNonHumanoidPrefab)
                {
                    // Non-humanoid: MeshRenderer OR SkinnedMeshRenderer is fine, no Animator needed
                    DrawValidationItem("Mesh Renderer", hasAnyRenderer, "MeshRenderer or SkinnedMeshRenderer");
                    DrawValidationItem(movementPrimaryLabel, hasMovementPrimary, movementPrimaryNote);
                    if (hasMovementSecondaryRequirement)
                        DrawValidationItem(movementSecondaryLabel, hasMovementSecondary, movementSecondaryNote);
                    DrawValidationItem("GPUIPrefab", hasGPUIPrefab, "Required for GPU Instancer");

                    // GPUICrowdInstance is only needed if the vehicle has an Animator / SkinnedMesh (optional)
                    if (hasSkinnedMesh || hasAnimatorAnywhere)
                        DrawValidationItem("GPUICrowdInstance", hasGPUICrowd, "Required for animated meshes");

                    if (!hasAnyRenderer)
                    {
                        EditorGUILayout.HelpBox(
                            "No MeshRenderer or SkinnedMeshRenderer found. The prefab needs at least one renderer.",
                            MessageType.Warning);
                    }
                }
                else
                {
                    // Humanoid / FlyingHumanoid: SkinnedMeshRenderer + Animator required
                    DrawValidationItem("Skinned Mesh Renderer", hasSkinnedMesh, "Required for GPU instancing");
                    DrawValidationItem("Animator (Root)", hasAnimatorOnRoot, "Required on ROOT for animations");
                    DrawValidationItem(movementPrimaryLabel, hasMovementPrimary, movementPrimaryNote);
                    if (hasMovementSecondaryRequirement)
                        DrawValidationItem(movementSecondaryLabel, hasMovementSecondary, movementSecondaryNote);
                    DrawValidationItem("GPUIPrefab", hasGPUIPrefab, "Required for GPU Instancer");
                    DrawValidationItem("GPUICrowdInstance", hasGPUICrowd, "Required for Crowd Animations");

                    if (!hasSkinnedMesh)
                    {
                        EditorGUILayout.HelpBox(
                            "No SkinnedMeshRenderer found. Make sure your prefab has an animated mesh.",
                            MessageType.Warning);
                    }

                    if (hasAnimatorAnywhere && !hasAnimatorOnRoot)
                    {
                        EditorGUILayout.HelpBox(
                            "CRITICAL: Animator component is on a child object! It MUST be on the root of the prefab to avoid animation issues.",
                            MessageType.Error);
                    }
                    else if (!hasAnimatorAnywhere)
                    {
                        EditorGUILayout.HelpBox(
                            "No Animator component found.",
                            MessageType.Warning);
                    }
                }

                bool missingMovement = !hasMovementPrimary || (hasMovementSecondaryRequirement && !hasMovementSecondary);
                bool missingComponents;
                if (isNonHumanoidPrefab)
                    missingComponents = missingMovement || !hasGPUIPrefab;
                else
                    missingComponents = missingMovement || !hasGPUIPrefab || !hasGPUICrowd;
                if (missingComponents)
                {
                    GUILayout.Space(10);
                    EditorGUILayout.HelpBox("Some required components are missing.", MessageType.Warning);
                    if (GUILayout.Button("Add Missing Components", GUILayout.Height(30)))
                    {
                        string prefabPath = AssetDatabase.GetAssetPath(selectedPrefab);
                        using (var editScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
                        {
                            GameObject prefabRoot = editScope.prefabContentsRoot;
                            AddMissingNavigationComponentsForSelectedType(prefabRoot);
                                
                            if (prefabRoot.GetComponent<GPUIPrefab>() == null)
                                prefabRoot.AddComponent<GPUIPrefab>();

                            // Only add GPUICrowdInstance for humanoid agents or if the prefab has animations
                            if (!isNonHumanoidPrefab)
                            {
                                if (prefabRoot.GetComponent<GPUICrowdInstance>() == null)
                                    prefabRoot.AddComponent<GPUICrowdInstance>();
                            }
                        }
                        // Refresh reference
                        selectedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    }
                }
                
                EditorGUILayout.EndVertical();
            }
            else
            {
                if (isNonHumanoidPrefab)
                {
                    EditorGUILayout.HelpBox(
                        "REQUIREMENTS:\n" +
                        "- Prefab with MeshRenderer or SkinnedMeshRenderer\n" +
                        "- No Animator required\n" +
                        "- Any mesh shape (spaceship, tank, drone, etc.)\n\n" +
                        "GPU Instancer Pro supports both MeshRenderer and SkinnedMeshRenderer for batched instancing.",
                        MessageType.None);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "REQUIREMENTS:\n" +
                        "- Prefab with SkinnedMeshRenderer (animated mesh)\n" +
                        "- Animator component with animation clips\n" +
                        "- Humanoid or Generic rig\n\n" +
                        "Don't have a character? Try Unity's 'Starter Assets' or Mixamo!",
                        MessageType.None);
                }
            }
            
            GUILayout.Space(10);
            
            // Skip option
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Skip for now (assign later)", EditorStyles.miniButton))
            {
                selectedPrefab = null;
                currentPage = Page.SceneSetup;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        string GetPrimaryNavigationRequirementName()
        {
            if (selectedNavigationType == NavigationType.Flight3D)
                return "Flight3DNavigationAgent";

            if (selectedNavigationType == NavigationType.UnityNavMesh)
                return "NavMeshAgent";

            if (selectedNavigationType == NavigationType.FlowField)
                return "FlowFieldAgentAdapter";

            if (!selectedNavigationType.RequiresAStar())
                return "Navigation Agent";

            int navTypeValue = (int)selectedNavigationType;
            switch (navTypeValue)
            {
                case 10: return "AIPath";
                case 11: return "RichAI";
                case 12: return "AILerp";
                case 13: return "AIPath/RichAI + RVOController";
                default: return "A* Movement Component";
            }
        }

        void GetNavigationValidationForPrefab(
            GameObject prefab,
            out string primaryLabel,
            out bool primaryValid,
            out string primaryNote,
            out bool hasSecondaryRequirement,
            out string secondaryLabel,
            out bool secondaryValid,
            out string secondaryNote)
        {
            hasSecondaryRequirement = false;
            secondaryLabel = string.Empty;
            secondaryValid = true;
            secondaryNote = string.Empty;

            if (selectedNavigationType == NavigationType.UnityNavMesh)
            {
                primaryLabel = "NavMeshAgent";
                primaryValid = prefab.GetComponent<NavMeshAgent>() != null;
                primaryNote = "Required for Unity NavMesh";
                return;
            }

            if (selectedNavigationType == NavigationType.Flight3D)
            {
                primaryLabel = "Flight3DNavigationAgent";
                primaryValid = prefab.GetComponent<Flight3DNavigationAgent>() != null;
                primaryNote = "Auto-added at runtime for Flight 3D";
                // Flight3D agents don't need the component on the prefab — it's added by NavigationAgentFactory
                primaryValid = true;
                return;
            }

            if (selectedNavigationType == NavigationType.FlowField)
            {
                primaryLabel = "FlowFieldAgentAdapter";
                primaryValid = prefab.GetComponent<Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters.FlowFieldAgentAdapter>() != null;
                primaryNote = "Required for Flow Field";
                return;
            }

            if (!selectedNavigationType.RequiresAStar())
            {
                primaryLabel = "Movement Agent";
                primaryValid = true;
                primaryNote = "No backend-specific requirement";
                return;
            }

            int navTypeValue = (int)selectedNavigationType;
            Type aiPathType = ResolveAstarRuntimeType("Pathfinding.AIPath");
            Type richAIType = ResolveAstarRuntimeType("Pathfinding.RichAI");
            Type aiLerpType = ResolveAstarRuntimeType("Pathfinding.AILerp");
            Type rvoType = ResolveAstarRuntimeType("Pathfinding.RVO.RVOController");

            switch (navTypeValue)
            {
                case 10:
                    primaryLabel = "A* AIPath";
                    primaryValid = aiPathType != null && prefab.GetComponent(aiPathType) != null;
                    primaryNote = aiPathType == null
                        ? "AIPath type not found (install A* package)"
                        : "Required for A* AIPath";
                    break;

                case 11:
                    primaryLabel = "A* RichAI";
                    primaryValid = richAIType != null && prefab.GetComponent(richAIType) != null;
                    primaryNote = richAIType == null
                        ? "RichAI type not found (install A* package)"
                        : "Required for A* RichAI";
                    break;

                case 12:
                    primaryLabel = "A* AILerp";
                    primaryValid = aiLerpType != null && prefab.GetComponent(aiLerpType) != null;
                    primaryNote = aiLerpType == null
                        ? "AILerp type not found (install A* package)"
                        : "Required for A* AILerp";
                    break;

                case 13:
                    primaryLabel = "A* AIPath or RichAI";
                    primaryValid = (aiPathType != null && prefab.GetComponent(aiPathType) != null) ||
                                   (richAIType != null && prefab.GetComponent(richAIType) != null);
                    primaryNote = (aiPathType == null && richAIType == null)
                        ? "A* movement types not found (install A* package)"
                        : "Required for A* RVO movement";

                    hasSecondaryRequirement = true;
                    secondaryLabel = "A* RVOController";
                    secondaryValid = rvoType != null && prefab.GetComponent(rvoType) != null;
                    secondaryNote = rvoType == null
                        ? "RVOController type not found (install A* package)"
                        : "Required for A* local avoidance";
                    break;

                default:
                    primaryLabel = "A* Movement Component";
                    primaryValid = true;
                    primaryNote = "Unknown A* subtype";
                    break;
            }
        }

        void AddMissingNavigationComponentsForSelectedType(GameObject prefabRoot)
        {
            if (prefabRoot == null)
                return;

            if (selectedNavigationType == NavigationType.UnityNavMesh)
            {
                if (prefabRoot.GetComponent<NavMeshAgent>() == null)
                    prefabRoot.AddComponent<NavMeshAgent>();
                return;
            }

            if (selectedNavigationType == NavigationType.Flight3D)
            {
                // Flight3DNavigationAgent is added at runtime by NavigationAgentFactory — nothing to add to prefab
                return;
            }

            if (selectedNavigationType == NavigationType.FlowField)
            {
                if (prefabRoot.GetComponent<Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters.FlowFieldAgentAdapter>() == null)
                    prefabRoot.AddComponent<Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters.FlowFieldAgentAdapter>();
                return;
            }

            if (!selectedNavigationType.RequiresAStar())
                return;

            int navTypeValue = (int)selectedNavigationType;
            Type aiPathType = ResolveAstarRuntimeType("Pathfinding.AIPath");
            Type richAIType = ResolveAstarRuntimeType("Pathfinding.RichAI");
            Type aiLerpType = ResolveAstarRuntimeType("Pathfinding.AILerp");
            Type rvoType = ResolveAstarRuntimeType("Pathfinding.RVO.RVOController");

            switch (navTypeValue)
            {
                case 10:
                    EnsureComponentByType(prefabRoot, aiPathType, "AIPath");
                    break;

                case 11:
                    EnsureComponentByType(prefabRoot, richAIType, "RichAI");
                    break;

                case 12:
                    EnsureComponentByType(prefabRoot, aiLerpType, "AILerp");
                    break;

                case 13:
                    bool hasMovementComponent = (aiPathType != null && prefabRoot.GetComponent(aiPathType) != null) ||
                                                (richAIType != null && prefabRoot.GetComponent(richAIType) != null);
                    if (!hasMovementComponent)
                    {
                        if (aiPathType != null)
                            EnsureComponentByType(prefabRoot, aiPathType, "AIPath");
                        else if (richAIType != null)
                            EnsureComponentByType(prefabRoot, richAIType, "RichAI");
                        else
                            Debug.LogWarning("[Enemy Masses] Could not add A* movement component. AIPath/RichAI type was not found.");
                    }

                    EnsureComponentByType(prefabRoot, rvoType, "RVOController");
                    break;
            }
        }

        static void EnsureComponentByType(GameObject target, Type componentType, string componentDisplayName)
        {
            if (target == null)
                return;

            if (componentType == null)
            {
                Debug.LogWarning($"[Enemy Masses] Could not add {componentDisplayName}: type not found.");
                return;
            }

            if (target.GetComponent(componentType) != null)
                return;

            target.AddComponent(componentType);
        }

        void DrawValidationItem(string label, bool isValid, string note)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(isValid ? "OK" : "-", GUILayout.Width(25));
            EditorGUILayout.LabelField($"{label}", GUILayout.Width(180));
            EditorGUILayout.LabelField(isValid ? "Found" : note, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        void DrawNetworkSetupPage()
        {
            EditorGUILayout.LabelField("Multiplayer / Network Setup", headerStyle);
            
            GUILayout.Space(10);
            
            // Networking architecture info
            EditorGUILayout.HelpBox(
                "ℹ️ NETWORKING-AGNOSTIC ARCHITECTURE\n\n" +
                "Enemy Masses provides a multiplayer contract (interfaces, validation hooks, " +
                "and tooling) that works with any networking solution:\n" +
                "• Unity NGO, Mirror, Photon, FishNet, or custom\n\n" +
                "Enemy Masses handles the scalable RTS simulation and authority hooks.\n" +
                "You implement the transport/RPC layer and game-specific systems.\n\n" +
                "See documentation for integration examples:\n" +
                "https://arawn-software-publishing.gitbook.io/enemy-masses",
                MessageType.Info);
            
            GUILayout.Space(15);
            
            // Enable Networking Toggle
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Network Support", EditorStyles.boldLabel);
            GUILayout.Space(5);
            
            enableNetworking = EditorGUILayout.Toggle(
                new GUIContent("Enable Networking", "Add networking components to support multiplayer gameplay"),
                enableNetworking);
            
            if (!enableNetworking)
            {
                EditorGUILayout.HelpBox(
                    "Networking is disabled. The system will work in single-player mode only.",
                    MessageType.Info);
            }
            
            EditorGUILayout.EndVertical();
            
            if (enableNetworking)
            {
                GUILayout.Space(15);
                
                // Multiplayer Mode Selection
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Multiplayer Mode", EditorStyles.boldLabel);
                GUILayout.Space(5);
                
                EditorGUILayout.HelpBox(
                    "IMPORTANT: Enemy Masses has a soft cap of ~1000 networked units total for optimal performance. " +
                    "Units are divided equally among players based on the mode you select.\n\n" +
                    "This is a guideline - you can exceed it, but network traffic and server load will increase significantly.",
                    MessageType.Warning);
                
                GUILayout.Space(10);
                
                multiplayerMode = (MultiplayerMode)EditorGUILayout.EnumPopup(
                    new GUIContent("Game Mode", "Select your multiplayer configuration"),
                    multiplayerMode);
                
                // Calculate recommended units per player
                int playerCount = GetPlayerCountFromMode(multiplayerMode);
                if (multiplayerMode == MultiplayerMode.Custom)
                {
                    customPlayerCount = EditorGUILayout.IntSlider(
                        new GUIContent("Player Count", "Number of players in your game"),
                        customPlayerCount, 2, 16);
                    playerCount = customPlayerCount;
                }
                
                recommendedMaxUnitsPerPlayer = Mathf.FloorToInt(1000f / playerCount);
                
                GUILayout.Space(10);
                
                // Display unit allocation
                EditorGUILayout.BeginVertical(tipBoxStyle);
                EditorGUILayout.LabelField("<b>Unit Allocation (1000 soft cap):</b>", descriptionStyle);
                GUILayout.Space(5);
                
                EditorGUILayout.LabelField($"• Players: <b>{playerCount}</b>", descriptionStyle);
                EditorGUILayout.LabelField($"• Units per Player: <b>{recommendedMaxUnitsPerPlayer}</b>", descriptionStyle);
                EditorGUILayout.LabelField($"• Total Units: <b>{recommendedMaxUnitsPerPlayer * playerCount}</b>", descriptionStyle);
                
                GUILayout.Space(5);
                
                string modeDescription = multiplayerMode switch
                {
                    MultiplayerMode.OneVsOne => "Classic 1v1 duel - each player controls up to 500 units",
                    MultiplayerMode.TwoVsTwo => "2v2 team battle - each player controls up to 250 units",
                    MultiplayerMode.OneVsOneVsOne => "3-way free-for-all - each player controls up to 333 units",
                    MultiplayerMode.FreeForAll4 => "4-player free-for-all - each player controls up to 250 units",
                    MultiplayerMode.FreeForAll6 => "6-player free-for-all - each player controls up to 166 units",
                    MultiplayerMode.FreeForAll8 => "8-player free-for-all - each player controls up to 125 units",
                    MultiplayerMode.Custom => $"Custom {playerCount}-player game - each player controls up to {recommendedMaxUnitsPerPlayer} units",
                    _ => "Custom multiplayer mode"
                };
                EditorGUILayout.LabelField($"<i>{modeDescription}</i>", descriptionStyle);
                
                EditorGUILayout.EndVertical();
                
                EditorGUILayout.EndVertical();
                
                GUILayout.Space(15);
                
                // Anti-Cheat Option
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Security", EditorStyles.boldLabel);
                GUILayout.Space(5);
                
                addAntiCheat = EditorGUILayout.Toggle(
                    new GUIContent("Add Anti-Cheat System", "Adds server-side validation for damage, commands, and speed hacks"),
                    addAntiCheat);
                
                if (addAntiCheat)
                {
                    EditorGUILayout.HelpBox(
                        "The Anti-Cheat system includes:\n" +
                        "• Damage validation (prevents impossible damage values)\n" +
                        "• Command rate limiting (prevents macro abuse)\n" +
                        "• Speed hack detection (validates movement velocity)\n" +
                        "• Ownership validation (prevents controlling other players' units)\n\n" +
                        "You can monitor cheat attempts via: Tools > Enemy Masses > Network > Anti-Cheat Monitor",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Anti-Cheat disabled. Only recommended for trusted environments (LAN, co-op).",
                        MessageType.Warning);
                }
                
                EditorGUILayout.EndVertical();
                
                GUILayout.Space(15);
                
                // Network Solution Info - Make it clear this requires additional work
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("🔧 YOUR REMAINING WORK (~30%)", EditorStyles.boldLabel);
                GUILayout.Space(5);
                
                EditorGUILayout.LabelField(
                    "<b>Enemy Masses provides:</b>\n" +
                    "• Network event hooks (spawn, damage, commands, state)\n" +
                    "• Authority interfaces (INetworkAuthority, INetworkSpawnAuthority, etc.)\n" +
                    "• Anti-cheat validation system\n" +
                    "• LocalNetworkAuthority testing stub",
                    descriptionStyle);
                
                GUILayout.Space(10);
                
                EditorGUILayout.LabelField(
                    "<b>You must implement:</b>\n" +
                    "• NetworkManager setup (lobby, connections)\n" +
                    "• Replace LocalNetworkAuthority with real INetworkAuthority\n" +
                    "• RPCs for spawn/damage/command synchronization\n" +
                    "• NetworkVariables for state sync (if using NGO)",
                    descriptionStyle);
                
                GUILayout.Space(10);
                
                EditorGUILayout.LabelField("<b>Supported networking solutions:</b>", descriptionStyle);
                EditorGUILayout.LabelField("• <b>Netcode for GameObjects (NGO)</b> - Full example in docs!", descriptionStyle);
                EditorGUILayout.LabelField("• <b>Mirror</b> - Popular open-source", descriptionStyle);
                EditorGUILayout.LabelField("• <b>Photon Fusion / PUN</b> - Cloud-based", descriptionStyle);
                EditorGUILayout.LabelField("• <b>FishNet</b> - High-performance", descriptionStyle);
                
                GUILayout.Space(10);
                
                if (GUILayout.Button("🌐 Open Online Documentation", GUILayout.Height(28)))
                {
                    Application.OpenURL("https://arawn-software-publishing.gitbook.io/enemy-masses/networking/netcode-for-gameobjects");
                }
                
                EditorGUILayout.EndVertical();
                
                GUILayout.Space(15);
                
                // What components will be added
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Components Added by This Wizard", EditorStyles.boldLabel);
                GUILayout.Space(5);
                
                EditorGUILayout.LabelField("• <b>LocalNetworkAuthority</b> - Testing stub (replace for real multiplayer!)", descriptionStyle);
                if (addAntiCheat)
                {
                    EditorGUILayout.LabelField("• <b>AntiCheatComponent</b> - Server-side validation (ready to use)", descriptionStyle);
                }
                
                EditorGUILayout.HelpBox(
                    "⚠️ LocalNetworkAuthority is for TESTING/SINGLE-PLAYER only!\n\n" +
                    "For real multiplayer, you MUST create a class that implements INetworkAuthority " +
                    "using your chosen networking solution. See the documentation for a complete " +
                    "NGO example with all RPCs and NetworkVariables.",
                    MessageType.Warning);
                
                EditorGUILayout.EndVertical();
            }
            
            GUILayout.Space(10);
            
            // Skip option
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Skip networking (single-player only)", EditorStyles.miniButton))
            {
                enableNetworking = false;
                currentPage = Page.SceneSetup;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
        
        int GetPlayerCountFromMode(MultiplayerMode mode)
        {
            return mode switch
            {
                MultiplayerMode.OneVsOne => 2,
                MultiplayerMode.TwoVsTwo => 4,
                MultiplayerMode.OneVsOneVsOne => 3,
                MultiplayerMode.FreeForAll4 => 4,
                MultiplayerMode.FreeForAll6 => 6,
                MultiplayerMode.FreeForAll8 => 8,
                MultiplayerMode.Custom => customPlayerCount,
                _ => 2
            };
        }

        void DrawSceneSetupPage()
        {
            EditorGUILayout.LabelField("Scene Setup", headerStyle);
            
            GUILayout.Space(10);
            
            EditorGUILayout.HelpBox(
                "Almost done! We'll now create the EnemyCrowd asset and optionally add " +
                "the controller to your current scene.",
                MessageType.Info);
            
            GUILayout.Space(15);
            
            // Summary
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Configuration Summary", EditorStyles.boldLabel);
            GUILayout.Space(5);
            
            EditorGUILayout.LabelField($"Faction: <b>{factionName}</b> - {enemyTypeName}", descriptionStyle);
            EditorGUILayout.LabelField($"Type: <b>{selectedCrowdType}</b>", descriptionStyle);
            EditorGUILayout.LabelField($"Navigation Type: <b>{selectedNavigationType.GetDisplayName()}</b>", descriptionStyle);
            if (selectedCrowdType != CrowdType.Civil)
            {
                EditorGUILayout.LabelField($"Combat Style: <b>{GetTemplateName(combatTemplate)}</b>", descriptionStyle);
                if (selectedCrowdType == CrowdType.BattleSimulation)
                {
                    EditorGUILayout.LabelField($"RTS Control: <b>{(isPlayerControlledFaction ? "Player" : "AI")}</b>", descriptionStyle);
                }
            }
            else
            {
                EditorGUILayout.LabelField($"Civil Navigation Mode: <b>{civilNavMode}</b>", descriptionStyle);
            }
            EditorGUILayout.LabelField($"Prefab: <b>{(selectedPrefab != null ? selectedPrefab.name : "Not assigned")}</b>", descriptionStyle);
            
            // Climbing summary
            if (enableClimbing)
            {
                string climbInfo = enableEdgeFallDetection 
                    ? (enableFallDamage ? $"Enabled (Fall Damage: {fallDamagePerUnit}/m)" : "Enabled (No Fall Damage)")
                    : "Enabled";
                EditorGUILayout.LabelField($"Climbing: <b>{climbInfo}</b>", descriptionStyle);
            }
            else
            {
                EditorGUILayout.LabelField($"Climbing: <b>Disabled</b>", descriptionStyle);
            }
            
            // Network summary
            if (enableNetworking)
            {
                int playerCount = GetPlayerCountFromMode(multiplayerMode);
                string modeStr = multiplayerMode == MultiplayerMode.Custom 
                    ? $"Custom ({playerCount}P)" 
                    : multiplayerMode.ToString().Replace("VsOne", " vs 1").Replace("VsTwo", " vs 2").Replace("FreeForAll", "FFA ");
                EditorGUILayout.LabelField($"Networking: <b>{modeStr}</b> - {recommendedMaxUnitsPerPlayer} units/player", descriptionStyle);
                EditorGUILayout.LabelField($"Anti-Cheat: <b>{(addAntiCheat ? "Enabled" : "Disabled")}</b>", descriptionStyle);
                EditorGUILayout.LabelField($"<color=#88CC88>✓ Networking-agnostic architecture ready</color>", descriptionStyle);
            }
            else
            {
                EditorGUILayout.LabelField($"Networking: <b>Disabled (Single-player)</b>", descriptionStyle);
            }
            
            EditorGUILayout.EndVertical();
            
            // Show networking info for network setups
            if (enableNetworking)
            {
                GUILayout.Space(5);
                EditorGUILayout.HelpBox(
                    "ℹ️ LocalNetworkAuthority will be added for testing.\n" +
                    "Replace with your INetworkAuthority implementation for networked multiplayer.",
                    MessageType.Info);
            }
            
            GUILayout.Space(15);
            
            // Player Targets for Combat
            if (selectedCrowdType == CrowdType.Combat)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Player Targets (Required for Combat)", EditorStyles.boldLabel);

                if (playerTargets.Count == 0)
                    playerTargets.Add(null);

                int removeIndex = -1;
                for (int i = 0; i < playerTargets.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    playerTargets[i] = (Transform)EditorGUILayout.ObjectField(
                        new GUIContent($"Player Target {i + 1}", "A player object that enemies will target."),
                        playerTargets[i],
                        typeof(Transform),
                        true);
                    if (GUILayout.Button("-", GUILayout.Width(24)))
                        removeIndex = i;
                    EditorGUILayout.EndHorizontal();
                }

                if (removeIndex >= 0 && playerTargets.Count > 1)
                    playerTargets.RemoveAt(removeIndex);

                if (GUILayout.Button("Add Player Target"))
                    playerTargets.Add(null);

                bool hasAnyTarget = playerTargets.Exists(target => target != null);
                if (!hasAnyTarget)
                {
                    EditorGUILayout.HelpBox("Please assign at least one Player Target so enemies know who to attack!", MessageType.Warning);
                }
                
                EditorGUILayout.EndVertical();
                GUILayout.Space(15);
            }
            
            // What will be created
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Assets to Create", EditorStyles.boldLabel);
            GUILayout.Space(5);
            
            string assetPath = $"Assets/EnemyCrowds/{factionName}_{enemyTypeName}.asset";
            EditorGUILayout.LabelField($"- EnemyCrowd asset at: {assetPath}", descriptionStyle);
            
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            
            // Scene setup options
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Scene Controller", EditorStyles.boldLabel);
            
            bool hasController = FindFirstObjectByType<EnemyMassesCrowdController>() != null;
            
            if (hasController)
            {
                EditorGUILayout.LabelField("[OK] EnemyMassesCrowdController found in scene", descriptionStyle);
                EditorGUILayout.HelpBox(
                    "The new EnemyCrowd will be added to the existing controller's list.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField("No controller in current scene yet.", descriptionStyle);
                EditorGUILayout.HelpBox(
                    "The controller will be created in the final step of this wizard after the EnemyCrowd asset is created.",
                    MessageType.Info);
            }
            
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(20);
            
            // Create button
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.4f, 1f);
            if (GUILayout.Button("Create EnemyCrowd Asset", GUILayout.Height(35), GUILayout.Width(220)))
            {
                CreateEnemyCrowdAsset();
            }
            GUI.backgroundColor = Color.white;
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        void DrawCompletePage()
        {
            EditorGUILayout.LabelField("Setup Complete!", headerStyle);
            
            GUILayout.Space(20);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "Congratulations! Your first EnemyCrowd has been created.\n\n" +
                "You can now spawn enemies in your game!",
                descriptionStyle);
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            
            // Created asset info
            if (createdEnemyCrowd != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Created Asset", EditorStyles.boldLabel);
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(createdEnemyCrowd, typeof(EnemyCrowd), false);
                if (GUILayout.Button("Select", GUILayout.Width(60)))
                {
                    Selection.activeObject = createdEnemyCrowd;
                    EditorGUIUtility.PingObject(createdEnemyCrowd);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
            
            GUILayout.Space(15);
            
            // Next steps
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Next Steps", EditorStyles.boldLabel);
            GUILayout.Space(5);
            
            EditorGUILayout.LabelField("1. REQUIRED: Add Controllers to your scene (Click button below)", descriptionStyle);
            EditorGUILayout.LabelField("2. REQUIRED: Ensure your scene has a baked NavMesh (see below)", descriptionStyle);
            EditorGUILayout.LabelField("3. Add spawn triggers or use the Spawner component", descriptionStyle);
            EditorGUILayout.LabelField("4. Hit Play and watch the magic!", descriptionStyle);
            
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            
            // Network architecture info - show if networking was enabled
            if (enableNetworking)
            {
                // Check for network components
                bool hasNetworkAuthority = FindFirstObjectByType<LocalNetworkAuthority>() != null;
                
                if (hasNetworkAuthority)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField("[OK] LocalNetworkAuthority found in scene", descriptionStyle);
                    EditorGUILayout.LabelField("Ready for single-player or testing. Replace with your networked implementation for multiplayer.", EditorStyles.miniLabel);
                    EditorGUILayout.EndVertical();
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "No INetworkAuthority component found in scene!\n" +
                        "Add LocalNetworkAuthority (for testing) or your own implementation.",
                        MessageType.Warning);
                }
                
                GUILayout.Space(10);
                
                EditorGUILayout.BeginVertical(tipBoxStyle);
                EditorGUILayout.LabelField("<b>ℹ️ Networking Integration</b>", descriptionStyle);
                GUILayout.Space(5);
                EditorGUILayout.LabelField(
                    "Enemy Masses provides a <b>networking-agnostic multiplayer contract</b>.\n\n" +
                    "<b>To connect your networking solution:</b>\n" +
                    "• Implement <b>INetworkAuthority</b> using your framework (NGO, Mirror, Photon, FishNet)\n" +
                    "• Wire up RPCs/NetworkVariables for spawn/damage synchronization\n" +
                    "• Handle lobby, matchmaking, and session management\n\n" +
                    "See our documentation for integration examples.",
                    descriptionStyle);
                EditorGUILayout.EndVertical();
                
                GUILayout.Space(10);
                
                if (GUILayout.Button("🌐 Open Online Documentation", GUILayout.Height(25)))
                {
                    Application.OpenURL("https://arawn-software-publishing.gitbook.io/enemy-masses/networking/setup");
                }
                
                GUILayout.Space(15);
            }
            
            // NavMesh warning - this is critical and easy to forget
            // Use reflection-based search to support both Unity 2022 and Unity 6 NavMesh packages
            bool hasNavMeshSurface = HasNavMeshSurfaceInScene();
            
            if (!hasNavMeshSurface)
            {
                EditorGUILayout.BeginVertical(tipBoxStyle);
                EditorGUILayout.LabelField("<b>Important: NavMesh Required!</b>", descriptionStyle);
                EditorGUILayout.LabelField(
                    "Enemy Masses agents use Unity's NavMesh for pathfinding. Without a baked NavMesh, " +
                    "agents will fail to spawn correctly and you'll see errors in the console.\n\n" +
                    "<b>To set up NavMesh:</b>\n" +
                    "1. Add a <b>NavMesh Surface</b> component to your ground/terrain object\n" +
                    "2. Click <b>'Bake'</b> in the NavMesh Surface inspector\n" +
                    "3. Or use: Window > AI > Navigation (legacy)",
                    descriptionStyle);
                EditorGUILayout.EndVertical();
                
                GUILayout.Space(10);
                
                EditorGUILayout.HelpBox(
                    "No NavMesh Surface detected in scene!\n" +
                    "Add one to your ground/terrain and bake it before running.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("[OK] NavMesh Surface found in scene", descriptionStyle);
                EditorGUILayout.LabelField("Make sure to bake the NavMesh if you haven't already.", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }
            
            GUILayout.Space(15);
            
            // NavMesh Link Climbing check - if climbing was enabled
            if (enableClimbing)
            {
                bool hasTraversalHandler = FindFirstObjectByType<NavMeshLinkTraversalHandler>() != null;
                bool hasAnimationHandler = FindFirstObjectByType<NavMeshLinkCrowdAnimationHandler>() != null;
                
                if (hasTraversalHandler)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.LabelField("[OK] NavMeshLinkTraversalHandler found in scene", descriptionStyle);
                    if (hasAnimationHandler)
                    {
                        EditorGUILayout.LabelField("[OK] NavMeshLinkCrowdAnimationHandler found - GPU animation support enabled", EditorStyles.miniLabel);
                    }
                    EditorGUILayout.LabelField("Climbing system is ready. Add NavMeshLinkClimbable components to your NavMeshLinks.", EditorStyles.miniLabel);
                    EditorGUILayout.EndVertical();
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "No NavMeshLinkTraversalHandler found in scene!\n" +
                        "Add NavMeshLinkTraversalHandler component to enable climbing behavior.\n" +
                        "Click 'Add Required Scene Components' below to add one automatically.",
                        MessageType.Warning);
                }
                
                GUILayout.Space(15);
            }

            // Check if controllers exist
            bool hasController = FindFirstObjectByType<EnemyMassesCrowdController>() != null;
            bool hasPrefabManager = FindFirstObjectByType<GPUIPrefabManager>() != null;

            if (!hasController || !hasPrefabManager)
            {
                EditorGUILayout.HelpBox(
                    "CRITICAL: Your scene is missing required components!\n" +
                    "You MUST click 'Add Required Scene Components' below to run the simulation.",
                    MessageType.Error);
                GUILayout.Space(10);
            }
            
            // Quick actions
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Open Documentation", GUILayout.Height(30)))
            {
                Application.OpenURL("https://arawn-software-publishing.gitbook.io/enemy-masses/");
            }
            
            // Highlight the button if components are missing
            var originalColor = GUI.backgroundColor;
            if (!hasController || !hasPrefabManager)
            {
                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f); // Red tint
            }

            if (GUILayout.Button("Add Required Scene Components", GUILayout.Height(30)))
            {
                AddControllerToScene();
            }
            
            GUI.backgroundColor = originalColor;
            
            EditorGUILayout.EndHorizontal();
            
            GUILayout.Space(20);
            
            // Close button
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            // Only enable Close button if requirements are met
            bool canClose = hasController && hasPrefabManager;
            EditorGUI.BeginDisabledGroup(!canClose);
            
            if (GUILayout.Button("Close Wizard", GUILayout.Height(30), GUILayout.Width(150)))
            {
                MarkWizardShown();
                MarkPatchRevisionSeen();
                Close();
            }
            
            EditorGUI.EndDisabledGroup();
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            if (!canClose)
            {
                EditorGUILayout.HelpBox("You must add the required scene components before closing.", MessageType.Warning);
            }
        }

        #endregion

        #region Navigation

        void DrawNavigation()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            
            float buttonHeight = 30f;
            float buttonWidth = 100f;
            
            // Back button
            if (currentPage > Page.Welcome && currentPage != Page.Complete)
            {
                if (GUILayout.Button("<- Back", GUILayout.Height(buttonHeight), GUILayout.Width(buttonWidth)))
                {
                    MoveToPreviousPage();
                }
            }
            else
            {
                GUILayout.Space(buttonWidth + 4);
            }
            
            GUILayout.FlexibleSpace();
            
            // Skip wizard link
            if (currentPage != Page.Complete)
            {
                if (GUILayout.Button("Skip Wizard", EditorStyles.linkLabel))
                {
                    if (EditorUtility.DisplayDialog("Skip Wizard?",
                        "You can always access this wizard later via:\nTools -> Enemy Masses -> Setup Wizard",
                        "Skip", "Cancel"))
                    {
                        MarkWizardShown();
                        MarkPatchRevisionSeen();
                        Close();
                    }
                }
            }
            
            GUILayout.FlexibleSpace();
            
            // Next button
            if (currentPage > Page.Welcome && currentPage < Page.SceneSetup)
            {
                bool canProceed = CanProceedFromCurrentPage();
                
                EditorGUI.BeginDisabledGroup(!canProceed);
                if (GUILayout.Button("Next ->", GUILayout.Height(buttonHeight), GUILayout.Width(buttonWidth)))
                {
                    MoveToNextPage();
                }
                EditorGUI.EndDisabledGroup();
            }
            else if (currentPage == Page.Welcome || currentPage == Page.Complete)
            {
                GUILayout.Space(buttonWidth + 4);
            }
            
            EditorGUILayout.EndHorizontal();
            
            // Don't show again checkbox
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            dontShowAgain = EditorGUILayout.Toggle(dontShowAgain, GUILayout.Width(20));
            EditorGUILayout.LabelField("Don't show this wizard automatically (you can still open via Tools > Enemy Masses > Setup Wizard)", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            
            // Apply the don't show again preference when changed
            if (dontShowAgain)
            {
                MarkWizardShown();
                MarkPatchRevisionSeen();
            }
            
            EditorGUILayout.Space(10);
        }

        void MoveToNextPage()
        {
            bool isVehicleOrGround = selectedAgentArchetype == AgentArchetype.FlyingVehicle ||
                                      selectedAgentArchetype == AgentArchetype.GroundVehicle;
            bool isFlying = selectedAgentArchetype == AgentArchetype.FlyingHumanoid ||
                            selectedAgentArchetype == AgentArchetype.FlyingVehicle;

            if (currentPage == Page.AgentArchetype)
            {
                // Vehicles/GroundVehicles skip AnimatorWorkflow+AnimationSetup+Climbing
                // since they have no required animations
                if (isVehicleOrGround)
                    currentPage = Page.CreateFaction;
                else
                    currentPage = Page.AnimatorWorkflow;
            }
            else if (currentPage == Page.CreateFaction)
            {
                // Flying archetypes auto-select Flight3D navigation, so we can skip
                // the navigation type page and go straight there, or show it for confirmation
                if (isFlying)
                {
                    selectedNavigationType = NavigationType.Flight3D;
                    currentPage = Page.NavigationTypeSetup;
                }
                else
                {
                    currentPage = Page.NavigationTypeSetup;
                }
            }
            else if (currentPage == Page.NavigationTypeSetup)
            {
                // Branch based on crowd type
                currentPage = (selectedCrowdType == CrowdType.Civil) ? Page.CivilSettings : Page.CombatStyle;
            }
            else if (currentPage == Page.CombatStyle)
            {
                // If RTS, go to RTS Setup, else go to AssignPrefab
                currentPage = (selectedCrowdType == CrowdType.BattleSimulation) ? Page.RTSSetup : Page.AssignPrefab;
            }
            else if (currentPage == Page.RTSSetup || currentPage == Page.CivilSettings)
            {
                // Merge back to AssignPrefab
                currentPage = Page.AssignPrefab;
            }
            else if (currentPage == Page.AssignPrefab)
            {
                // Go to NetworkSetup (only for Battle Simulation which is typically multiplayer)
                currentPage = Page.NetworkSetup;
            }
            else if (currentPage == Page.NetworkSetup)
            {
                // Go to SceneSetup
                currentPage = Page.SceneSetup;
            }
            else
            {
                currentPage++;
            }
        }

        void MoveToPreviousPage()
        {
            bool isVehicleOrGround = selectedAgentArchetype == AgentArchetype.FlyingVehicle ||
                                      selectedAgentArchetype == AgentArchetype.GroundVehicle;

            if (currentPage == Page.SceneSetup)
            {
                // Go back to NetworkSetup
                currentPage = Page.NetworkSetup;
            }
            else if (currentPage == Page.NetworkSetup)
            {
                // Go back to AssignPrefab
                currentPage = Page.AssignPrefab;
            }
            else if (currentPage == Page.AssignPrefab)
            {
                // Branch back based on crowd type
                if (selectedCrowdType == CrowdType.Civil) currentPage = Page.CivilSettings;
                else if (selectedCrowdType == CrowdType.BattleSimulation) currentPage = Page.RTSSetup;
                else currentPage = Page.CombatStyle;
            }
            else if (currentPage == Page.RTSSetup)
            {
                currentPage = Page.CombatStyle;
            }
            else if (currentPage == Page.NavigationTypeSetup)
            {
                currentPage = Page.CreateFaction;
            }
            else if (currentPage == Page.CombatStyle || currentPage == Page.CivilSettings)
            {
                // Both branches merge back to navigation type selection
                currentPage = Page.NavigationTypeSetup;
            }
            else if (currentPage == Page.CreateFaction)
            {
                // Vehicles skipped Animator/Animation/Climbing, go back to AgentArchetype
                if (isVehicleOrGround)
                    currentPage = Page.AgentArchetype;
                else
                    currentPage = Page.ClimbingSetup;
            }
            else
            {
                currentPage--;
            }
        }

        bool CanProceedFromCurrentPage()
        {
            switch (currentPage)
            {
                case Page.CreateFaction:
                    return !string.IsNullOrWhiteSpace(factionName);
                case Page.AnimationSetup:
                    // Non-humanoid archetypes (Vehicle, GroundVehicle) have no required clips
                    bool isNonHumanoidCheck = selectedAgentArchetype == AgentArchetype.FlyingVehicle ||
                                              selectedAgentArchetype == AgentArchetype.GroundVehicle;
                    if (isNonHumanoidCheck) return true;
                    
                    // Humanoid / FlyingHumanoid require locomotion
                    if (idleClip == null || walkClip == null || runClip == null) return false;
                    // Require combat anims if combat crowd
                    if (selectedCrowdType != CrowdType.Civil)
                    {
                        if (attackClips == null || attackClips.Length == 0) return false;
                        if (hitClips == null || hitClips.Length == 0) return false;
                        if (deathClips == null || deathClips.Length == 0) return false;
                    }
                    return true;
                case Page.AssignPrefab:
                    return true; // Prefab is optional
                default:
                    return true;
            }
        }

        #endregion

        #region Asset Creation

        void RefreshNavigationTypeOptions()
        {
            availableNavigationTypes = NavigationAgentFactory.GetAvailableTypes();

            if (availableNavigationTypes == null || availableNavigationTypes.Length == 0)
            {
                availableNavigationTypes = new[] { NavigationType.UnityNavMesh };
                selectedNavigationType = NavigationType.UnityNavMesh;
                return;
            }

            if (Array.IndexOf(availableNavigationTypes, selectedNavigationType) < 0)
                selectedNavigationType = availableNavigationTypes[0];
        }

        void CreateEnemyCrowdAsset()
        {
            // Ensure directory exists
            string directory = "Assets/EnemyCrowds";
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            // Create sanitized filename
            string safeFactionName = SanitizeFileName(factionName);
            string safeTypeName = SanitizeFileName(enemyTypeName);
            string assetPath = $"{directory}/{safeFactionName}_{safeTypeName}.asset";
            
            // Check for existing asset
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            
            // Create the EnemyCrowd asset
            createdEnemyCrowd = ScriptableObject.CreateInstance<EnemyCrowd>();
            createdEnemyCrowd.factionName = factionName;
            createdEnemyCrowd.enemyTypeName = enemyTypeName;
            createdEnemyCrowd.factionColor = factionColor;
            createdEnemyCrowd.prefab = selectedPrefab;

            NavigationType resolvedNavigationType = selectedNavigationType;
            if (!NavigationAgentFactory.IsTypeAvailable(resolvedNavigationType))
            {
                Debug.LogWarning(
                    $"[Enemy Masses] Selected navigation type {resolvedNavigationType} is not available. " +
                    "Falling back to Unity NavMesh.");
                resolvedNavigationType = NavigationType.UnityNavMesh;
            }
            createdEnemyCrowd.navigationType = resolvedNavigationType;
            
            // Apply Animator Workflow
            createdEnemyCrowd.animatorWorkflow = animatorWorkflow;
            
            // Apply Animations
            createdEnemyCrowd.idleClipOverride = idleClip;
            createdEnemyCrowd.walkClipOverride = walkClip;
            createdEnemyCrowd.runClipOverride = runClip;
            
            if (selectedCrowdType != CrowdType.Civil)
            {
                createdEnemyCrowd.attackClipsOverride = attackClips;
                createdEnemyCrowd.hitClipsOverride = hitClips;
                createdEnemyCrowd.deathClipsOverride = deathClips;
                
                // Apply Combat Settings
                createdEnemyCrowd.enableCombatSystem = true;
                createdEnemyCrowd.combatBehaviorTemplate = combatTemplate;
                createdEnemyCrowd.ApplyCombatTemplate();
                
                // Ensure Civil is disabled
                createdEnemyCrowd.civilSettings.enableCivilMode = false;
            }
            else
            {
                // Apply Civil Settings
                createdEnemyCrowd.civilSettings.enableCivilMode = true;
                createdEnemyCrowd.civilSettings.disableCombat = true;
                createdEnemyCrowd.civilSettings.navigationMode = civilNavMode;
                createdEnemyCrowd.civilSettings.movementStyle = civilMoveStyle;
                
                // Disable Combat
                createdEnemyCrowd.enableCombatSystem = false;
                createdEnemyCrowd.combatBehaviorTemplate = CombatBehaviorTemplate.Custom;
            }
            
            // Apply Climbing Animation Config
            if (enableClimbing)
            {
                bool isComputeAnimator = animatorWorkflow == AnimatorWorkflowOverride.ComputeAnimator;
                
                if (isComputeAnimator)
                {
                    // Compute Animator clips
                    createdEnemyCrowd.climbingAnimationConfig.climbUpClip = climbUpClip;
                    createdEnemyCrowd.climbingAnimationConfig.climbDownClip = climbDownClip;
                    createdEnemyCrowd.climbingAnimationConfig.mountClip = climbMountClip;
                    createdEnemyCrowd.climbingAnimationConfig.dismountTopClip = climbDismountClip;
                    createdEnemyCrowd.climbingAnimationConfig.fallClip = climbFallClip;
                }
                else
                {
                    // Mecanim parameters
                    createdEnemyCrowd.climbingAnimationConfig.climbingBoolParameter = climbingBoolParameter;
                    createdEnemyCrowd.climbingAnimationConfig.climbSpeedParameter = climbSpeedParameter;
                    createdEnemyCrowd.climbingAnimationConfig.mountTriggerParameter = climbMountTrigger;
                    createdEnemyCrowd.climbingAnimationConfig.dismountTriggerParameter = climbDismountTrigger;
                    createdEnemyCrowd.climbingAnimationConfig.fallTriggerParameter = climbFallTrigger;
                }
                
                // Timing (applies to both)
                createdEnemyCrowd.climbingAnimationConfig.mountDuration = climbMountDuration;
                createdEnemyCrowd.climbingAnimationConfig.dismountDuration = climbDismountDuration;
            }
            
            // ═══ AGENT ARCHETYPE & FLIGHT / VEHICLE SETTINGS ═══
            createdEnemyCrowd.agentArchetype = selectedAgentArchetype;
            
            bool isFlying = selectedAgentArchetype == AgentArchetype.FlyingHumanoid ||
                            selectedAgentArchetype == AgentArchetype.FlyingVehicle;
            bool isNonHumanoid = selectedAgentArchetype == AgentArchetype.FlyingVehicle ||
                                  selectedAgentArchetype == AgentArchetype.GroundVehicle;
            
            if (isFlying)
            {
                createdEnemyCrowd.persistentFlight = true;
                createdEnemyCrowd.flight3DNavigationType = flight3DNavType;
                createdEnemyCrowd.cruisingAltitude = cruisingAltitude;
                createdEnemyCrowd.altitudeVariance = altitudeVariance;
                createdEnemyCrowd.verticalSpeed = verticalSpeed;
                createdEnemyCrowd.formationLayers = formationLayers;
                createdEnemyCrowd.formationLayerSpacing = formationLayerSpacing;
                
                // Override navigation type to Flight3D for flying archetypes
                createdEnemyCrowd.navigationType = NavigationType.Flight3D;
                
                if (selectedAgentArchetype == AgentArchetype.FlyingVehicle)
                {
                    createdEnemyCrowd.bankAngle = bankAngle;
                    createdEnemyCrowd.bankSpeed = bankSpeed;
                    createdEnemyCrowd.headingSmoothRate = headingSmoothRate;
                    createdEnemyCrowd.maxPitchAngle = maxPitchAngle;
                }
            }
            
            if (isNonHumanoid)
            {
                createdEnemyCrowd.hitboxPreset = selectedHitboxPreset;
                createdEnemyCrowd.vehicleBoundingRadius = vehicleBoundingRadius;
                createdEnemyCrowd.vehicleBoundingHeight = vehicleBoundingHeight;
            }
            
            // Save asset
            AssetDatabase.CreateAsset(createdEnemyCrowd, assetPath);
            AssetDatabase.SaveAssets();
            
            // Mark wizard as completed
            MarkWizardShown();
            MarkPatchRevisionSeen();
            
            // Move to complete page
            currentPage = Page.Complete;
            
            Debug.Log($"[Enemy Masses] Created EnemyCrowd asset at: {assetPath}");
        }

        string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Replace(" ", "_");
        }

        void AddControllerToScene()
        {
            // Check if controller already exists
            var existingController = FindFirstObjectByType<EnemyMassesCrowdController>();
            EnemyMassesCrowdController controller;
            
            if (existingController != null)
            {
                controller = existingController;
                Selection.activeGameObject = existingController.gameObject;
                EditorGUIUtility.PingObject(existingController.gameObject);
                EditorUtility.DisplayDialog("Controller Exists",
                    "An EnemyMassesCrowdController already exists in the scene.",
                    "OK");
            }
            else
            {
                // Create new controller
                var controllerGO = new GameObject("EnemyMassesCrowdController");
                controller = controllerGO.AddComponent<EnemyMassesCrowdController>();
                
                Selection.activeGameObject = controllerGO;
                EditorGUIUtility.PingObject(controllerGO);
                
                Undo.RegisterCreatedObjectUndo(controllerGO, "Create Enemy Masses Controller");
                
                Debug.Log("[Enemy Masses] Created EnemyMassesCrowdController in scene");
            }
            
            // Configure Controller based on Wizard settings
            if (controller != null)
            {
                // Ensure RTS World Marker is present for minimap/fog rules
                var worldMarker = controller.gameObject.GetComponent<EnemyMassesRTSWorldMarker>();
                if (worldMarker == null)
                {
                    worldMarker = controller.gameObject.AddComponent<EnemyMassesRTSWorldMarker>();
                    Undo.RegisterCreatedObjectUndo(worldMarker, "Add RTS World Marker");
                    Debug.Log("[Enemy Masses] Added EnemyMassesRTSWorldMarker to Controller");
                }

                // Add the created crowd if available
                if (createdEnemyCrowd != null && !controller.EnemyCrowds.Contains(createdEnemyCrowd))
                {
                    controller.EnemyCrowds.Add(createdEnemyCrowd);
                    EditorUtility.SetDirty(controller);
                }
                
                // Enable Register Immediately for all crowd types (Combat, Civil, BattleSimulation)
                SerializedObject controllerSo = new SerializedObject(controller);
                controllerSo.Update();
                SerializedProperty registerImmProp = controllerSo.FindProperty("registerImmediately");
                if (registerImmProp != null)
                {
                    registerImmProp.boolValue = true;
                }
                controllerSo.ApplyModifiedProperties();
                EditorUtility.SetDirty(controller);
                Debug.Log("[Enemy Masses] Enabled Register Immediately on Controller");
                
                // Apply Battle Simulation settings
                if (selectedCrowdType == CrowdType.BattleSimulation)
                {
                    SerializedObject so = new SerializedObject(controller);
                    so.Update();
                    SerializedProperty factionBattleProp = so.FindProperty("factionBattleMode");
                    if (factionBattleProp != null)
                    {
                        factionBattleProp.boolValue = true;
                    }
                    so.ApplyModifiedProperties();
                    Debug.Log("[Enemy Masses] Enabled Faction Battle Mode on Controller");
                    
                    // Add RTS Controller
                    var rtsController = controller.gameObject.GetComponent<EnemyMassesRTSController>();
                    if (rtsController == null)
                    {
                        rtsController = controller.gameObject.AddComponent<EnemyMassesRTSController>();
                        Undo.RegisterCreatedObjectUndo(rtsController, "Add RTS Controller");
                    }
                    
                    // Configure RTS Controller
                    if (rtsController != null)
                    {
                        rtsController.crowdController = controller;
                        if (rtsController.targetCamera == null)
                        {
                            rtsController.targetCamera = Camera.main;
                        }
                        
                        rtsController.restrictSelectionToPlayerFactions = true;
                        
                        // Set reference to RTS Controller on the Crowd Controller
                        SerializedObject crowdSo = new SerializedObject(controller);
                        crowdSo.Update();
                        SerializedProperty rtsProp = crowdSo.FindProperty("rtsController");
                        if (rtsProp != null)
                        {
                            rtsProp.objectReferenceValue = rtsController;
                        }
                        crowdSo.ApplyModifiedProperties();
                        
                        if (isPlayerControlledFaction)
                        {
                            // Add faction to player controlled list
                            // We need to use SerializedObject because FactionMask is a custom class
                            SerializedObject rtsSo = new SerializedObject(rtsController);
                            rtsSo.Update();
                            SerializedProperty playerFactionsProp = rtsSo.FindProperty("playerControlledFactions");
                            SerializedProperty selectedFactionsList = playerFactionsProp.FindPropertyRelative("selectedFactions");
                            
                            bool alreadyExists = false;
                            for (int i = 0; i < selectedFactionsList.arraySize; i++)
                            {
                                if (selectedFactionsList.GetArrayElementAtIndex(i).stringValue == factionName)
                                {
                                    alreadyExists = true;
                                    break;
                                }
                            }
                            
                            if (!alreadyExists)
                            {
                                selectedFactionsList.arraySize++;
                                selectedFactionsList.GetArrayElementAtIndex(selectedFactionsList.arraySize - 1).stringValue = factionName;
                            }
                            rtsSo.ApplyModifiedProperties();
                        }
                        
                        Debug.Log("[Enemy Masses] Configured EnemyMassesRTSController");
                    }
                    
                    // Add Projectile System (Required for RTS/Combat)
                    var projectileSystem = controller.gameObject.GetComponent<EnemyMassesProjectileSystem>();
                    if (projectileSystem == null)
                    {
                        projectileSystem = controller.gameObject.AddComponent<EnemyMassesProjectileSystem>();
                        Undo.RegisterCreatedObjectUndo(projectileSystem, "Add Projectile System");
                        Debug.Log("[Enemy Masses] Added EnemyMassesProjectileSystem");
                    }
                    
                    // Setup Main Camera for RTS
                    if (Camera.main != null)
                    {
                        var mainCamGO = Camera.main.gameObject;
                        
                        // Add GPUICamera
                        if (mainCamGO.GetComponent<GPUICamera>() == null)
                        {
                            mainCamGO.AddComponent<GPUICamera>();
                            Debug.Log("[Enemy Masses] Added GPUICamera to Main Camera");
                        }
                        
                        // Add RTS Camera Controller
                        var camController = mainCamGO.GetComponent<RTSCameraController>();
                        if (camController == null)
                        {
                            camController = mainCamGO.AddComponent<RTSCameraController>();
                            Undo.RegisterCreatedObjectUndo(camController, "Add RTS Camera Controller");
                            Debug.Log("[Enemy Masses] Added RTSCameraController to Main Camera");
                        }
                        
                        // Configure camera preset from existing preset assets
                        if (camController != null)
                        {
                            // Get the existing preset asset from the plugin folder
                            string presetAssetName = rtsPresetType switch
                            {
                                0 => "ClassicRTSPreset",
                                1 => "ModernRTSPreset",
                                2 => "ThirdPersonPreset",
                                3 => "TotalWarPreset",
                                4 => "SupremeCommanderPreset",
                                _ => "ClassicRTSPreset"
                            };
                            
                            string presetPath = $"Assets/Plugins/EnemyMasses/CameraPresets/{presetAssetName}.asset";
                            RTSCameraPreset preset = AssetDatabase.LoadAssetAtPath<RTSCameraPreset>(presetPath);
                            
                            if (preset == null)
                            {
                                Debug.LogWarning($"[Enemy Masses] Could not find camera preset at: {presetPath}. Creating a new one.");
                                
                                // Fallback: create preset if not found
                                preset = rtsPresetType switch
                                {
                                    0 => RTSCameraPreset.CreateClassicRTS(),
                                    1 => RTSCameraPreset.CreateModernRTS(),
                                    2 => RTSCameraPreset.CreateThirdPerson(),
                                    3 => RTSCameraPreset.CreateTotalWar(),
                                    4 => RTSCameraPreset.CreateSupremeCommander(),
                                    _ => RTSCameraPreset.CreateClassicRTS()
                                };
                                
                                string fallbackPath = $"Assets/EnemyMasses/CameraPresets/{presetAssetName}.asset";
                                string presetDir = System.IO.Path.GetDirectoryName(fallbackPath);
                                if (!System.IO.Directory.Exists(presetDir))
                                {
                                    System.IO.Directory.CreateDirectory(presetDir);
                                    AssetDatabase.Refresh();
                                }
                                AssetDatabase.CreateAsset(preset, fallbackPath);
                                AssetDatabase.SaveAssets();
                            }
                            else
                            {
                                Debug.Log($"[Enemy Masses] Using existing camera preset: {presetPath}");
                            }
                            
                            SerializedObject camSo = new SerializedObject(camController);
                            camSo.Update();
                            
                            // Add preset to camera controller
                            SerializedProperty presetsProp = camSo.FindProperty("cameraPresets");
                            if (presetsProp != null)
                            {
                                // Check if preset is already in list
                                bool presetExists = false;
                                for (int i = 0; i < presetsProp.arraySize; i++)
                                {
                                    if (presetsProp.GetArrayElementAtIndex(i).objectReferenceValue == preset)
                                    {
                                        presetExists = true;
                                        break;
                                    }
                                }
                                
                                if (!presetExists)
                                {
                                    presetsProp.InsertArrayElementAtIndex(presetsProp.arraySize);
                                    presetsProp.GetArrayElementAtIndex(presetsProp.arraySize - 1).objectReferenceValue = preset;
                                }
                            }
                            
                            // Bounds
                            SerializedProperty enableBoundsProp = camSo.FindProperty("enableBounds");
                            if (enableBoundsProp != null) enableBoundsProp.boolValue = true;
                            
                            float halfWidth = worldSize.x / 2f;
                            float halfLength = worldSize.y / 2f;
                            
                            SerializedProperty minXProp = camSo.FindProperty("minX");
                            if (minXProp != null) minXProp.floatValue = -halfWidth;
                            
                            SerializedProperty maxXProp = camSo.FindProperty("maxX");
                            if (maxXProp != null) maxXProp.floatValue = halfWidth;
                            
                            SerializedProperty minZProp = camSo.FindProperty("minZ");
                            if (minZProp != null) minZProp.floatValue = -halfLength;
                            
                            SerializedProperty maxZProp = camSo.FindProperty("maxZ");
                            if (maxZProp != null) maxZProp.floatValue = halfLength;
                            
                            camSo.ApplyModifiedProperties();
                        }
                        
                        string presetDisplayName = rtsPresetType switch
                        {
                            0 => "Classic RTS",
                            1 => "Modern RTS",
                            2 => "Third Person",
                            3 => "Total War",
                            4 => "Supreme Commander",
                            _ => "Default"
                        };
                        
                        EditorUtility.DisplayDialog("RTS Camera Setup",
                            "The Main Camera has been configured with the RTS Camera Controller.\n\n" +
                            $"Camera Preset: {presetDisplayName}\n\n" +
                            "A RTSCameraPreset asset was created in Assets/EnemyMasses/CameraPresets/.\n" +
                            "You can add more presets and customize settings by selecting the Main Camera.",
                            "OK");
                    }
                    else
                    {
                        Debug.LogWarning("[Enemy Masses] Could not find Main Camera to attach RTS components!");
                    }
                    
                    // Create Minimap if requested
                    if (createMinimap)
                    {
                        if (minimapMode == 0)
                            CreateMinimapSystemCameraFree();
                        else
                            CreateMinimapSystem();
                    }
                    
                    // Create Fog of War if requested
                    if (createFogOfWar)
                    {
                        CreateFogOfWarSystem();
                    }
                }
                
                // Apply Player Targets for Combat
                if (selectedCrowdType == CrowdType.Combat)
                {
                    List<Transform> resolvedTargets = playerTargets.FindAll(target => target != null);
                    if (resolvedTargets.Count > 0)
                    {
                        SerializedObject so = new SerializedObject(controller);
                        so.Update();
                        SerializedProperty playerTargetsProp = so.FindProperty("playerTargets");
                        if (playerTargetsProp != null)
                        {
                            playerTargetsProp.ClearArray();
                            for (int i = 0; i < resolvedTargets.Count; i++)
                            {
                                playerTargetsProp.InsertArrayElementAtIndex(i);
                                playerTargetsProp.GetArrayElementAtIndex(i).objectReferenceValue = resolvedTargets[i];
                            }
                        }

                        SerializedProperty playerTargetProp = so.FindProperty("playerTarget");
                        if (playerTargetProp != null)
                        {
                            playerTargetProp.objectReferenceValue = resolvedTargets[0];
                        }

                        so.ApplyModifiedProperties();
                        Debug.Log($"[Enemy Masses] Assigned {resolvedTargets.Count} Player Target(s) to Controller");
                    }
                }
            }

            // Check for GPUIPrefabManager
            var prefabManager = FindFirstObjectByType<GPUIPrefabManager>();
            if (prefabManager == null)
            {
                var managerGO = new GameObject("GPUIPrefabManager");
                prefabManager = managerGO.AddComponent<GPUIPrefabManager>();
                Undo.RegisterCreatedObjectUndo(managerGO, "Create GPUIPrefabManager");
                Debug.Log("[Enemy Masses] Created GPUIPrefabManager in scene");
            }

            // Configure GPUIPrefabManager
            if (prefabManager != null)
            {
                prefabManager.isFindInstancesAtInitialization = true;
                
                // Set Register Immediately to true
                SerializedObject pmSo = new SerializedObject(prefabManager);
                pmSo.Update();
                SerializedProperty registerProp = pmSo.FindProperty("registerImmediately");
                if (registerProp != null)
                {
                    registerProp.boolValue = true;
                }
                pmSo.ApplyModifiedProperties();
                
                EditorUtility.SetDirty(prefabManager);
                
                // Assign Prefab Manager to Crowd Controller
                if (controller != null)
                {
                    SerializedObject ccSo = new SerializedObject(controller);
                    ccSo.Update();
                    SerializedProperty pmProp = ccSo.FindProperty("prefabManager");
                    if (pmProp != null)
                    {
                        pmProp.objectReferenceValue = prefabManager;
                    }
                    ccSo.ApplyModifiedProperties();
                    Debug.Log("[Enemy Masses] Assigned GPUIPrefabManager to Crowd Controller");
                }
                
                // Warn user about Default Profile and Prototypes
                EditorUtility.DisplayDialog("GPU Instancer Pro Setup",
                    "A GPUIPrefabManager has been added/verified in the scene.\n\n" +
                    "IMPORTANT STEPS REQUIRED:\n" +
                    "1. Select the GPUIPrefabManager in the scene.\n" +
                    "2. Create and assign a 'Default Profile' in its Inspector.\n" +
                    "3. (Recommended) Add your character prefabs to the 'Prototype Settings' list manually.\n\n" +
                    "The 'Find Instances At Start' and 'Register Immediately' settings have been automatically enabled for you.",
                    "Got it");
                
                // Select the manager so the user sees it
                Selection.activeGameObject = prefabManager.gameObject;
                EditorGUIUtility.PingObject(prefabManager.gameObject);
            }
            
            // Add Climbing Controller if enabled
            if (enableClimbing && controller != null)
            {
                AddClimbingController(controller.gameObject, controller);
            }
            
            // Add Network Components if enabled
            if (enableNetworking && controller != null)
            {
                AddNetworkComponents(controller.gameObject);
            }

            // Add navigation backend runtime scene requirements if needed
            EnsureNavigationBackendSceneRequirements();
        }

        void EnsureNavigationBackendSceneRequirements()
        {
            NavigationType navType = createdEnemyCrowd != null
                ? createdEnemyCrowd.navigationType
                : selectedNavigationType;

            if (!NavigationAgentFactory.IsTypeAvailable(navType))
                navType = NavigationType.UnityNavMesh;

            if (navType == NavigationType.FlowField)
            {
                EnsureFlowFieldRuntimeHostInScene();
                return;
            }

            if (navType.RequiresAStar())
            {
                EnsureAstarPathInScene();
            }
        }

        void EnsureAstarPathInScene()
        {
            Type astarPathType = ResolveAstarPathType();
            if (astarPathType == null)
            {
                Debug.LogWarning("[Enemy Masses] A* navigation selected but Pathfinding.AstarPath type was not found. " +
                                 "Please ensure A* Pathfinding Project is installed.");
                return;
            }

            Component existingPath = FindSceneComponentByType(astarPathType);
            if (existingPath != null)
            {
                Debug.Log("[Enemy Masses] AstarPath already exists in scene.");
                return;
            }

            GameObject astarRoot = FindSceneGameObjectByName("A*");
            if (astarRoot == null)
            {
                astarRoot = new GameObject("A*");
                Undo.RegisterCreatedObjectUndo(astarRoot, "Create A* Root");
                Debug.Log("[Enemy Masses] Created GameObject 'A*'.");
            }

            if (astarRoot.GetComponent(astarPathType) == null)
            {
                Component astarPath = astarRoot.AddComponent(astarPathType);
                Undo.RegisterCreatedObjectUndo(astarPath, "Add AstarPath");
                Debug.Log("[Enemy Masses] Added AstarPath component to 'A*' GameObject.");
            }
        }

        void EnsureFlowFieldRuntimeHostInScene()
        {
            FlowFieldRuntimeHost host = FindFirstObjectByType<FlowFieldRuntimeHost>();
            if (host == null)
            {
                GameObject hostObject = FindSceneGameObjectByName("Flow Field Runtime Host");
                if (hostObject == null)
                {
                    hostObject = new GameObject("Flow Field Runtime Host");
                    Undo.RegisterCreatedObjectUndo(hostObject, "Create Flow Field Runtime Host");
                    Debug.Log("[Enemy Masses] Created GameObject 'Flow Field Runtime Host'.");
                }

                host = hostObject.GetComponent<FlowFieldRuntimeHost>();
                if (host == null)
                {
                    host = hostObject.AddComponent<FlowFieldRuntimeHost>();
                    Undo.RegisterCreatedObjectUndo(host, "Add FlowFieldRuntimeHost");
                    Debug.Log("[Enemy Masses] Added FlowFieldRuntimeHost component.");
                }
            }

            FlowFieldRuntimeConfig config = host.Config;
            if (config == null)
            {
                config = GetOrCreateFlowFieldRuntimeConfigAsset();
                if (config != null)
                {
                    SerializedObject hostSo = new SerializedObject(host);
                    hostSo.Update();
                    SerializedProperty configProp = hostSo.FindProperty("config");
                    if (configProp != null)
                    {
                        configProp.objectReferenceValue = config;
                        hostSo.ApplyModifiedProperties();
                        EditorUtility.SetDirty(host);
                    }

                    Debug.Log("[Enemy Masses] Assigned FlowFieldRuntimeConfig to FlowFieldRuntimeHost.");
                }
            }
            else
            {
                Debug.Log("[Enemy Masses] FlowFieldRuntimeHost already has a FlowFieldRuntimeConfig assigned.");
            }
        }

        FlowFieldRuntimeConfig GetOrCreateFlowFieldRuntimeConfigAsset()
        {
            FlowFieldRuntimeConfig config = AssetDatabase.LoadAssetAtPath<FlowFieldRuntimeConfig>(FLOW_FIELD_CONFIG_ASSET_PATH);
            if (config != null)
                return config;

            string[] existing = AssetDatabase.FindAssets("t:FlowFieldRuntimeConfig");
            if (existing != null && existing.Length > 0)
            {
                string foundPath = AssetDatabase.GUIDToAssetPath(existing[0]);
                config = AssetDatabase.LoadAssetAtPath<FlowFieldRuntimeConfig>(foundPath);
                if (config != null)
                    return config;
            }

            if (!AssetDatabase.IsValidFolder(FLOW_FIELD_CONFIG_DIRECTORY))
            {
                string parentFolder = "Assets/EnemyMasses";
                if (!AssetDatabase.IsValidFolder(parentFolder))
                    AssetDatabase.CreateFolder("Assets", "EnemyMasses");
                AssetDatabase.CreateFolder(parentFolder, "Navigation");
            }

            config = ScriptableObject.CreateInstance<FlowFieldRuntimeConfig>();
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(FLOW_FIELD_CONFIG_ASSET_PATH);
            AssetDatabase.CreateAsset(config, assetPath);
            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(config);
            Debug.Log($"[Enemy Masses] Created FlowFieldRuntimeConfig asset at: {assetPath}");
            return config;
        }

        static Component FindSceneComponentByType(Type componentType)
        {
            if (componentType == null)
                return null;

            UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(componentType);
            for (int i = 0; i < objects.Length; i++)
            {
                Component component = objects[i] as Component;
                if (component == null || component.gameObject == null)
                    continue;

                if (!IsSceneObject(component.gameObject))
                    continue;

                return component;
            }

            return null;
        }

        static GameObject FindSceneGameObjectByName(string objectName)
        {
            GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < allObjects.Length; i++)
            {
                GameObject candidate = allObjects[i];
                if (candidate == null || candidate.name != objectName)
                    continue;

                if (!IsSceneObject(candidate))
                    continue;

                return candidate;
            }

            return null;
        }

        static bool IsSceneObject(GameObject obj)
        {
            if (obj == null)
                return false;

            if (EditorUtility.IsPersistent(obj))
                return false;

            return (obj.hideFlags & HideFlags.HideAndDontSave) == 0;
        }

        static Type ResolveAstarRuntimeType(string fullTypeName)
        {
            if (string.IsNullOrWhiteSpace(fullTypeName))
                return null;

            Type type = Type.GetType($"{fullTypeName}, AstarPathfindingProject")
                        ?? Type.GetType($"{fullTypeName}, com.arongranberg.astar")
                        ?? Type.GetType($"{fullTypeName}, Assembly-CSharp");

            if (type != null)
                return type;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    type = assemblies[i].GetType(fullTypeName);
                    if (type != null)
                        return type;
                }
                catch
                {
                    // Ignore reflection load issues and continue scanning.
                }
            }

            return null;
        }

        static Type ResolveAstarPathType()
        {
            return ResolveAstarRuntimeType("Pathfinding.AstarPath");
        }
        
        void AddClimbingController(GameObject controllerGO, EnemyMassesCrowdController crowdController)
        {
            // Add NavMeshLinkTraversalHandler - the main controller for NavMeshLink-based climbing
            var traversalHandler = controllerGO.GetComponent<NavMeshLinkTraversalHandler>();
            if (traversalHandler == null)
            {
                traversalHandler = controllerGO.AddComponent<NavMeshLinkTraversalHandler>();
                Undo.RegisterCreatedObjectUndo(traversalHandler, "Add NavMeshLinkTraversalHandler");
                Debug.Log("[Enemy Masses] Added NavMeshLinkTraversalHandler to Controller");
            }
            
            // Add NavMeshLinkCrowdAnimationHandler for GPU Instancer Pro animation integration
            var animationHandler = controllerGO.GetComponent<NavMeshLinkCrowdAnimationHandler>();
            if (animationHandler == null)
            {
                animationHandler = controllerGO.AddComponent<NavMeshLinkCrowdAnimationHandler>();
                Undo.RegisterCreatedObjectUndo(animationHandler, "Add NavMeshLinkCrowdAnimationHandler");
                Debug.Log("[Enemy Masses] Added NavMeshLinkCrowdAnimationHandler for GPU animation");
            }
            
            // Configure NavMeshLinkTraversalHandler
            if (traversalHandler != null)
            {
                SerializedObject traversalSo = new SerializedObject(traversalHandler);
                traversalSo.Update();
                
                // Assign crowd controller reference
                SerializedProperty crowdProp = traversalSo.FindProperty("crowdController");
                if (crowdProp != null)
                {
                    crowdProp.objectReferenceValue = crowdController;
                }
                
                // Enable proximity detection by default for NavMeshLink awareness
                SerializedProperty proximityProp = traversalSo.FindProperty("enableProximityDetection");
                if (proximityProp != null)
                {
                    proximityProp.boolValue = true;
                }
                
                // Configure edge fall detection settings
                SerializedProperty enableEdgeFallProp = traversalSo.FindProperty("enableEdgeFallDetection");
                if (enableEdgeFallProp != null)
                {
                    enableEdgeFallProp.boolValue = enableEdgeFallDetection;
                }
                
                SerializedProperty enableFallDamageProp = traversalSo.FindProperty("enableFallDamage");
                if (enableFallDamageProp != null)
                {
                    enableFallDamageProp.boolValue = enableFallDamage;
                }
                
                SerializedProperty fallDamageProp = traversalSo.FindProperty("fallDamagePerUnit");
                if (fallDamageProp != null)
                {
                    fallDamageProp.floatValue = fallDamagePerUnit;
                }
                
                traversalSo.ApplyModifiedProperties();
                EditorUtility.SetDirty(traversalHandler);
                
                Debug.Log("[Enemy Masses] Configured NavMeshLinkTraversalHandler with fall detection settings");
            }
            
            // Configure NavMeshLinkCrowdAnimationHandler
            if (animationHandler != null)
            {
                SerializedObject animSo = new SerializedObject(animationHandler);
                animSo.Update();
                
                // Link to traversal handler
                SerializedProperty traversalProp = animSo.FindProperty("traversalHandler");
                if (traversalProp != null)
                {
                    traversalProp.objectReferenceValue = traversalHandler;
                }
                
                // Link to crowd controller
                SerializedProperty animCrowdProp = animSo.FindProperty("crowdController");
                if (animCrowdProp != null)
                {
                    animCrowdProp.objectReferenceValue = crowdController;
                }
                
                animSo.ApplyModifiedProperties();
                EditorUtility.SetDirty(animationHandler);
            }
            
            // Build dialog message for NavMeshLink-based climbing
            string climbMethodInfo = 
                "NAVMESHLINK-BASED CLIMBING:\n" +
                "• Agents automatically traverse NavMeshLinks during pathfinding\n" +
                "• Use NavMeshLinkClimbable to define climbable surfaces with animation support\n" +
                "• Proximity detection allows awareness of nearby climbable areas\n" +
                "• Full integration with GPU Instancer Pro crowd animations";
            
            EditorUtility.DisplayDialog("Climbing System Setup",
                "NavMeshLinkTraversalHandler and NavMeshLinkCrowdAnimationHandler have been added.\n\n" +
                $"{climbMethodInfo}\n\n" +
                "NEXT STEPS:\n" +
                "1. Create NavMeshLinks in your scene (AI > Navigation > NavMesh Link)\n" +
                "2. Add NavMeshLinkClimbable components to configure climbing behavior\n" +
                "3. Configure climb animations in your CrowdPrototype\n" +
                "4. (Optional) Adjust speed multipliers and traversal settings\n\n" +
                "TIP: NavMeshLinks integrate with Unity's pathfinding for seamless agent navigation!",
                "Got it");
        }
        
        void AddNetworkComponents(GameObject controllerGO)
        {
            // Add LocalNetworkAuthority (testing stub - user will replace with real implementation)
            var networkAuthority = controllerGO.GetComponent<LocalNetworkAuthority>();
            if (networkAuthority == null)
            {
                networkAuthority = controllerGO.AddComponent<LocalNetworkAuthority>();
                Undo.RegisterCreatedObjectUndo(networkAuthority, "Add LocalNetworkAuthority");
                Debug.Log("[Enemy Masses] Added LocalNetworkAuthority to Controller");
            }
            
            // Add AntiCheatComponent if requested
            if (addAntiCheat)
            {
                var antiCheat = controllerGO.GetComponent<AntiCheatComponent>();
                if (antiCheat == null)
                {
                    antiCheat = controllerGO.AddComponent<AntiCheatComponent>();
                    Undo.RegisterCreatedObjectUndo(antiCheat, "Add AntiCheatComponent");
                    Debug.Log("[Enemy Masses] Added AntiCheatComponent to Controller");
                }
                
                // Configure anti-cheat based on multiplayer mode
                ConfigureAntiCheatForMode(antiCheat);
            }
            
            // Show network setup dialog with clear architecture messaging
            int playerCount = GetPlayerCountFromMode(multiplayerMode);
            string modeStr = multiplayerMode == MultiplayerMode.Custom 
                ? $"Custom ({playerCount} players)" 
                : multiplayerMode.ToString();
            
            string networkMessage = 
                "✅ Network Components Added\n" +
                "═══════════════════════════════════════════════════════\n\n" +
                $"Mode: {modeStr}\n" +
                $"Recommended units per player: {recommendedMaxUnitsPerPlayer}\n" +
                $"Total unit soft cap: ~1000\n\n" +
                "COMPONENTS ADDED:\n" +
                "• LocalNetworkAuthority (for single-player/testing)\n" +
                (addAntiCheat ? "• AntiCheatComponent\n" : "") +
                "• Unit allocation configured\n\n" +
                "TO CONNECT YOUR NETWORKING SOLUTION:\n" +
                "• Implement INetworkAuthority using your framework\n" +
                "• Wire up RPCs/NetworkVariables for state sync\n" +
                "• Add lobby/matchmaking as needed\n\n" +
                "📖 See integration examples in documentation";
            
            if (addAntiCheat)
            {
                networkMessage += "\n\n" +
                    "🛡️ Anti-Cheat ready (enable on server only!):\n" +
                    "• Monitor: Tools > Enemy Masses > Network > Anti-Cheat Monitor";
            }
            
            EditorUtility.DisplayDialog("Network Components Added", networkMessage, "OK");
        }
        
        void ConfigureAntiCheatForMode(AntiCheatComponent antiCheat)
        {
            // Configure anti-cheat constraints based on multiplayer mode
            // Larger games need more lenient settings to avoid false positives
            SerializedObject so = new SerializedObject(antiCheat);
            so.Update();
            
            int playerCount = GetPlayerCountFromMode(multiplayerMode);
            
            // Adjust maxAgentsPerCommand based on player count
            // Smaller per-player unit counts = smaller commands
            SerializedProperty maxAgentsProp = so.FindProperty("_maxAgentsPerCommand");
            if (maxAgentsProp != null)
            {
                // Scale down max agents per command based on how many units each player has
                int scaledMax = Mathf.Min(recommendedMaxUnitsPerPlayer, 200);
                maxAgentsProp.intValue = scaledMax;
            }
            
            // For larger games (6+ players), use more lenient settings
            if (playerCount >= 6)
            {
                // Use Lenient preset values
                SerializedProperty suspicionProp = so.FindProperty("_suspicionThreshold");
                if (suspicionProp != null) suspicionProp.floatValue = 10f;
                
                SerializedProperty speedTolProp = so.FindProperty("_speedTolerance");
                if (speedTolProp != null) speedTolProp.floatValue = 2f;
                
                SerializedProperty cmdRateProp = so.FindProperty("_maxCommandsPerSecond");
                if (cmdRateProp != null) cmdRateProp.floatValue = 50f;
            }
            
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(antiCheat);
            
            Debug.Log($"[Enemy Masses] Configured AntiCheat for {playerCount}-player mode (max {recommendedMaxUnitsPerPlayer} units/player)");
        }

        void CreateMinimapSystemCameraFree()
        {
            // 1. Create Canvas
            var canvasGO = new GameObject("MinimapCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            canvasGO.AddComponent<GraphicRaycaster>();
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create Camera-Free Minimap");

            // 2. Minimap container (anchored top-right)
            var displayGO = new GameObject("MinimapDisplay");
            displayGO.transform.SetParent(canvasGO.transform, false);
            var displayRect = displayGO.AddComponent<RectTransform>();
            displayRect.anchorMin = new Vector2(1f, 1f);
            displayRect.anchorMax = new Vector2(1f, 1f);
            displayRect.pivot = new Vector2(1f, 1f);
            displayRect.anchoredPosition = new Vector2(-10f, -10f);
            displayRect.sizeDelta = new Vector2(200f, 200f);

            // 3. Background
            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(displayGO.transform, false);
            var bgRect = bgGO.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            var bgImage = bgGO.AddComponent<RawImage>();
            bgImage.color = new Color(0.08f, 0.12f, 0.08f, 1f);

            // 4. Border
            var borderGO = new GameObject("Border");
            borderGO.transform.SetParent(displayGO.transform, false);
            var borderRect = borderGO.AddComponent<RectTransform>();
            borderRect.anchorMin = Vector2.zero;
            borderRect.anchorMax = Vector2.one;
            borderRect.offsetMin = new Vector2(-2f, -2f);
            borderRect.offsetMax = new Vector2(2f, 2f);
            var borderImg = borderGO.AddComponent<Image>();
            borderImg.color = new Color(0.15f, 0.15f, 0.2f, 0.9f);
            borderImg.raycastTarget = false;
            borderGO.transform.SetAsFirstSibling();

            // 5. View indicator
            var viGO = new GameObject("ViewIndicator");
            viGO.transform.SetParent(displayGO.transform, false);
            var viRect = viGO.AddComponent<RectTransform>();
            viRect.anchorMin = Vector2.zero;
            viRect.anchorMax = Vector2.zero;
            viRect.pivot = new Vector2(0.5f, 0.5f);
            viRect.sizeDelta = new Vector2(24f, 24f);
            var viImage = viGO.AddComponent<Image>();
            viImage.color = new Color(1f, 1f, 1f, 0.5f);
            viImage.raycastTarget = false;

            // 6. RTSMinimapUI component
            var minimapUI = displayGO.AddComponent<UI.RTSMinimapUI>();
            var uiSO = new SerializedObject(minimapUI);
            uiSO.Update();
            uiSO.FindProperty("backgroundImage").objectReferenceValue = bgImage;
            uiSO.FindProperty("viewIndicatorRect").objectReferenceValue = viRect;
            uiSO.FindProperty("worldSize").vector2Value = worldSize;
            uiSO.FindProperty("lockViewIndicatorSize").boolValue = true;
            uiSO.FindProperty("lockedViewIndicatorSize").vector2Value = new Vector2(24f, 24f);
            uiSO.FindProperty("maxMarkers").intValue = 2000;
            uiSO.FindProperty("refreshInterval").floatValue = 0f;
            uiSO.FindProperty("captureBackgroundOnStart").boolValue = true;
            uiSO.FindProperty("showFogOverlay").boolValue = true;

            // Wire camera controller
            if (Camera.main != null)
            {
                var mainRTS = Camera.main.GetComponent<RTSCameraController>();
                if (mainRTS != null)
                    uiSO.FindProperty("cameraController").objectReferenceValue = mainRTS;
            }

            // Wire RTS controller
            var crowdCtrl = FindFirstObjectByType<EnemyMassesCrowdController>();
            if (crowdCtrl != null)
            {
                var rtsCtrl = crowdCtrl.GetComponent<EnemyMassesRTSController>();
                if (rtsCtrl != null)
                    uiSO.FindProperty("rtsController").objectReferenceValue = rtsCtrl;
            }

            uiSO.ApplyModifiedProperties();

            // 7. Data provider
            var providerGO = new GameObject("MinimapDataProvider");
            providerGO.transform.SetParent(canvasGO.transform, false);
            var provider = providerGO.AddComponent<EnemyMassesMinimapDataProvider>();

            // 8. Alert system (AoE2-style under-attack alerts)
            if (createAlertSystem)
            {
                var alertSys = providerGO.AddComponent<MinimapAlertSystem>();
                var alertSO = new SerializedObject(alertSys);
                alertSO.Update();
                if (crowdCtrl != null)
                    alertSO.FindProperty("crowdController").objectReferenceValue = crowdCtrl;
                var rtsCtrlForAlert = crowdCtrl != null ? crowdCtrl.GetComponent<EnemyMassesRTSController>() : null;
                if (rtsCtrlForAlert != null)
                    alertSO.FindProperty("rtsController").objectReferenceValue = rtsCtrlForAlert;
                alertSO.ApplyModifiedProperties();

                // Wire alert system reference into RTSMinimapUI
                var alertUISO = new SerializedObject(minimapUI);
                alertUISO.Update();
                alertUISO.FindProperty("enableAlertFlash").boolValue = true;
                alertUISO.FindProperty("alertSystem").objectReferenceValue = alertSys;
                alertUISO.ApplyModifiedProperties();
            }

            var provSO = new SerializedObject(provider);
            provSO.Update();
            if (crowdCtrl != null)
                provSO.FindProperty("crowdController").objectReferenceValue = crowdCtrl;
            var rtsCtrl2 = crowdCtrl != null ? crowdCtrl.GetComponent<EnemyMassesRTSController>() : null;
            if (rtsCtrl2 != null)
                provSO.FindProperty("rtsController").objectReferenceValue = rtsCtrl2;
            provSO.ApplyModifiedProperties();

            // Wire provider to minimap UI
            var uiSO2 = new SerializedObject(minimapUI);
            uiSO2.Update();
            uiSO2.FindProperty("dataProviderSource").objectReferenceValue = provider;
            uiSO2.ApplyModifiedProperties();

            Debug.Log("[Enemy Masses] Created Camera-Free Minimap System (RTSMinimapUI). " +
                      "Assign a top-down terrain screenshot to the Background RawImage for best results.");
        }

#pragma warning disable CS0618 // RTSMinimapController is obsolete but still supported via the legacy wizard path
        void CreateMinimapSystem()
        {
            // 1. Create Canvas
            var canvasGO = new GameObject("MinimapCanvas");
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            
            canvasGO.AddComponent<GraphicRaycaster>();
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create Minimap Canvas");
            
            // 2. Create Minimap Display (RawImage)
            var displayGO = new GameObject("MinimapDisplay");
            displayGO.transform.SetParent(canvasGO.transform, false);
            var rawImage = displayGO.AddComponent<RawImage>();
            
            // Load Texture
            var renderTexture = AssetDatabase.LoadAssetAtPath<RenderTexture>(MINIMAP_TEXTURE_PATH);
            if (renderTexture != null)
            {
                rawImage.texture = renderTexture;
            }
            else
            {
                Debug.LogError($"[Enemy Masses] Could not find Minimap Render Texture at {MINIMAP_TEXTURE_PATH}");
            }
            
            // Position it
            var rectTransform = rawImage.rectTransform;
            rectTransform.anchoredPosition3D = new Vector3(710, -281, 0);
            rectTransform.localScale = new Vector3(4, 4, 4);
            rectTransform.sizeDelta = new Vector2(100, 100);
            
            // 3. Create View Indicator (Image)
            var indicatorGO = new GameObject("ViewIndicator");
            indicatorGO.transform.SetParent(displayGO.transform, false);
            var indicatorImage = indicatorGO.AddComponent<Image>();
            indicatorImage.raycastTarget = false;
            
            var indicatorRect = indicatorImage.rectTransform;
            indicatorRect.anchoredPosition3D = new Vector3(190, -60, 0);
            indicatorRect.sizeDelta = new Vector2(26, 26);
            
            // Load Material
            var indicatorMat = AssetDatabase.LoadAssetAtPath<Material>(MINIMAP_MATERIAL_PATH);
            if (indicatorMat != null)
            {
                indicatorImage.material = indicatorMat;
            }
            else
            {
                Debug.LogError($"[Enemy Masses] Could not find Minimap Material at {MINIMAP_MATERIAL_PATH}");
            }
            
            // 4. Create Minimap Camera
            var camGO = new GameObject("MinimapCamera");
            var cam = camGO.AddComponent<Camera>();
            cam.depth = 0; // Priority 0
            
            // Culling Mask: Cull Default, TransparentFX, Ignore Raycast, UI
            // We want to SHOW everything ELSE. So we invert the mask of those layers.
            int cullMask = ~(1 << LayerMask.NameToLayer("Default") | 
                             1 << LayerMask.NameToLayer("TransparentFX") | 
                             1 << LayerMask.NameToLayer("Ignore Raycast") | 
                             1 << LayerMask.NameToLayer("UI"));
            cam.cullingMask = cullMask;
            
            // Disable Shadows & Enable Depth Texture (URP Specific)
            // We try to find the UniversalAdditionalCameraData component via reflection to avoid dependency issues if URP is missing
            var urpCamDataType = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
            if (urpCamDataType != null)
            {
                var camData = camGO.AddComponent(urpCamDataType);
                if (camData != null)
                {
                    // renderShadows = false
                    var renderShadowsProp = urpCamDataType.GetProperty("renderShadows");
                    if (renderShadowsProp != null) renderShadowsProp.SetValue(camData, false);
                    
                    // requiresDepthTexture = true (Option: On)
                    var depthTextureProp = urpCamDataType.GetProperty("requiresDepthOption"); // It's an enum usually
                    if (depthTextureProp != null)
                    {
                        // CameraOverrideOption.On = 1
                        depthTextureProp.SetValue(camData, 1); 
                    }
                }
            }
            
            Undo.RegisterCreatedObjectUndo(camGO, "Create Minimap Camera");
            
            // 5. Add RTSMinimapController
            var minimapController = camGO.AddComponent<RTSMinimapController>();
            
            // Configure Controller
            SerializedObject so = new SerializedObject(minimapController);
            so.Update();
            
            // Main Camera Controller
            if (Camera.main != null)
            {
                var mainRTS = Camera.main.GetComponent<RTSCameraController>();
                if (mainRTS != null)
                {
                    so.FindProperty("mainCameraController").objectReferenceValue = mainRTS;
                }
            }
            
            // Minimap Texture
            if (renderTexture != null)
            {
                so.FindProperty("minimapTexture").objectReferenceValue = renderTexture;
            }
            
            // World Size
            so.FindProperty("worldSize").vector2Value = worldSize;
            
            // Minimap UI Rect
            so.FindProperty("minimapUIRect").objectReferenceValue = rawImage.rectTransform;
            
            // Show View Indicator
            so.FindProperty("showViewIndicator").boolValue = true;

            // Load View Indicator Material
            string matPath = "Assets/Plugins/EnemyMasses/Runtime/Material/ViewIndicatorMaterial.mat";
            Material viewMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (viewMat != null)
            {
                so.FindProperty("viewIndicatorMaterial").objectReferenceValue = viewMat;
            }
            else
            {
                Debug.LogWarning($"[Enemy Masses] Could not find ViewIndicatorMaterial at {matPath}");
            }
            
            // View Indicator UI Rect
            so.FindProperty("viewIndicatorUIRect").objectReferenceValue = indicatorImage.rectTransform;
            
            // Locked View Indicator Size
            so.FindProperty("lockViewIndicatorSize").boolValue = true;
            so.FindProperty("lockedViewIndicatorSize").vector2Value = new Vector2(32, 18);
            
            // Agent Icons Configuration
            so.FindProperty("showAgentIcons").boolValue = true;
            so.FindProperty("iconShape").enumValueIndex = (int)RTSMinimapController.IconShape.Square;
            so.FindProperty("iconSize").floatValue = 4f;
            so.FindProperty("useFactionColors").boolValue = true;
            so.FindProperty("showDeadAgents").boolValue = true;
            so.FindProperty("iconUpdateInterval").floatValue = 0.1f;
            so.FindProperty("maxIcons").intValue = 2000;
            so.FindProperty("autoFitToAgentsOnStart").boolValue = true;
            so.FindProperty("autoFitPadding").floatValue = 20f;
            
            // Debug Settings
            so.FindProperty("debugIconRendering").boolValue = false;
            so.FindProperty("debugViewIndicator").boolValue = false;
            
            // References
            if (Camera.main != null)
            {
                var crowdController = FindFirstObjectByType<EnemyMassesCrowdController>();
                if (crowdController != null)
                {
                    so.FindProperty("crowdController").objectReferenceValue = crowdController;
                    
                    var rtsController = crowdController.GetComponent<EnemyMassesRTSController>();
                    if (rtsController != null)
                    {
                        so.FindProperty("rtsController").objectReferenceValue = rtsController;
                    }
                }
            }
            
            so.ApplyModifiedProperties();
            
            Debug.Log("[Enemy Masses] Created Minimap System (Camera-Based Legacy)");
        }
#pragma warning restore CS0618

        void CreateFogOfWarSystem()
        {
            // Create GameObject
            var fogGO = new GameObject("FogOfWar");
            fogGO.transform.position = Vector3.zero;
            
            // Add Component
            var fog = fogGO.AddComponent<FogOfWarLite>();
            
            // Configure Settings
            SerializedObject so = new SerializedObject(fog);
            so.Update();
            
            // Grid
            so.FindProperty("gridX").intValue = 128;
            so.FindProperty("gridY").intValue = 128;
            so.FindProperty("unitScale").floatValue = 8f;
            so.FindProperty("gridCenter").objectReferenceValue = fogGO.transform;
            so.FindProperty("planeHeight").floatValue = 1f;
            
            // Reveal
            so.FindProperty("maxRevealRadius").floatValue = 20f;
            so.FindProperty("revealedAlpha").floatValue = 1f;
            so.FindProperty("refreshInterval").floatValue = 0.5f;
            so.FindProperty("maxAgentsPerTick").intValue = 1000;
            so.FindProperty("lerpBuffer").boolValue = false;
            so.FindProperty("keepRevealed").boolValue = true;
            so.FindProperty("enableRefog").boolValue = true;
            so.FindProperty("refogOpacity").floatValue = 0.5f;
            so.FindProperty("refogSpeed").floatValue = 1f;
            
            // Sampling
            so.FindProperty("roundRobinSampling").boolValue = true;
            
            // References
            var crowdController = FindFirstObjectByType<EnemyMassesCrowdController>();
            if (crowdController != null)
            {
                so.FindProperty("crowdController").objectReferenceValue = crowdController;
                
                var rtsController = crowdController.GetComponent<EnemyMassesRTSController>();
                if (rtsController != null)
                {
                    so.FindProperty("rtsController").objectReferenceValue = rtsController;
                }
            }
            
            // Compute Shader
            var computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>(FOG_COMPUTE_SHADER_PATH);
            if (computeShader != null)
            {
                so.FindProperty("computeShader").objectReferenceValue = computeShader;
            }
            else
            {
                Debug.LogError($"[Enemy Masses] Could not find Fog Compute Shader at {FOG_COMPUTE_SHADER_PATH}. " +
                    "Drag FogOfWarCompute.compute into the FogOfWarLite inspector manually.");
            }
            
            // Material
            var fogMat = AssetDatabase.LoadAssetAtPath<Material>(FOG_MATERIAL_PATH);
            if (fogMat != null)
            {
                so.FindProperty("fogMaterial").objectReferenceValue = fogMat;
            }
            else
            {
                Debug.LogError($"[Enemy Masses] Could not find Fog Material at {FOG_MATERIAL_PATH}");
            }
            
            // Debug
            so.FindProperty("logStats").boolValue = false;
            so.FindProperty("drawBounds").boolValue = true;
            
            so.ApplyModifiedProperties();
            
            Undo.RegisterCreatedObjectUndo(fogGO, "Create Fog of War");
            Debug.Log("[Enemy Masses] Created Fog of War System");
            
            // Link to Minimap if it exists
            if (createMinimap)
            {
                if (minimapMode == 0)
                {
                    // Camera-free: link fog to data provider
                    var dataProvider = FindFirstObjectByType<EnemyMassesMinimapDataProvider>();
                    if (dataProvider != null)
                    {
                        SerializedObject dpSO = new SerializedObject(dataProvider);
                        dpSO.Update();
                        dpSO.FindProperty("fogOfWarProvider").objectReferenceValue = fog;
                        dpSO.ApplyModifiedProperties();
                        Debug.Log("[Enemy Masses] Linked Fog of War to Minimap Data Provider");
                    }

                    // Also link fog to RTSMinimapUI for overlay rendering
                    var minimapUI = FindFirstObjectByType<UI.RTSMinimapUI>();
                    if (minimapUI != null)
                    {
                        SerializedObject muiSO = new SerializedObject(minimapUI);
                        muiSO.Update();
                        muiSO.FindProperty("fogOfWarSource").objectReferenceValue = fog;
                        muiSO.ApplyModifiedProperties();
                        Debug.Log("[Enemy Masses] Linked Fog of War to Minimap UI overlay");
                    }
                }
                else
                {
                    // Camera-based: link fog to minimap controller
#pragma warning disable CS0618
                    var minimapController = FindFirstObjectByType<RTSMinimapController>();
#pragma warning restore CS0618
                    if (minimapController != null)
                    {
                        SerializedObject minimapSO = new SerializedObject(minimapController);
                        minimapSO.Update();
                        minimapSO.FindProperty("fogOfWarProvider").objectReferenceValue = fog;
                        minimapSO.ApplyModifiedProperties();
                        Debug.Log("[Enemy Masses] Linked Fog of War to Minimap Controller");
                    }
                }
            }
        }

        string GetTemplateName(CombatBehaviorTemplate template)
        {
            switch (template)
            {
                case CombatBehaviorTemplate.MeleeSwarm: return "Melee Swarm";
                case CombatBehaviorTemplate.MeleeQueued: return "Melee Queued";
                case CombatBehaviorTemplate.MeleeBattleLine: return "Battle Line";
                case CombatBehaviorTemplate.RangedStationary: return "Ranged Stationary";
                case CombatBehaviorTemplate.RangedBattleLine: return "Ranged Battle Line";
                case CombatBehaviorTemplate.HybridAdaptive: return "Hybrid Adaptive";
                case CombatBehaviorTemplate.MeleeWaves: return "Wave Attack";
                default: return template.ToString();
            }
        }

        #endregion
    }
}
#endif
