using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using Arawn.EnemyMasses.Runtime.Factions;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// RTS Minimap controller that syncs with the main camera and allows click-to-move.
    /// IMPORTANT: This must be attached to a SEPARATE minimap camera, NOT the main camera!
    /// The minimap camera renders a top-down orthographic view to a RenderTexture.
    /// 
    /// AGENT VISIBILITY ON MINIMAP:
    /// The recommended way to show crowd agents on the minimap is to add a "GPUI Camera" 
    /// component (from GPU Instancer Pro) to this minimap camera. This enables GPU Instancer's
    /// multi-camera support and renders the actual agent models in the minimap automatically.
    /// 
    /// The legacy "Agent Icons" system is disabled by default and can be enabled if you prefer
    /// simplified icon markers instead of real agent models.
    /// </summary>
    [System.Obsolete("RTSMinimapController requires a dedicated Camera and is URP-only. " +
        "Use RTSMinimapUI + EnemyMassesMinimapDataProvider instead — camera-free, works with Built-in/URP/HDRP. " +
        "Create via GameObject > Enemy Masses > UI > Minimap.")]
    [RequireComponent(typeof(Camera))]
    [AddComponentMenu("Enemy Masses/Input/RTS Minimap Controller (Legacy)")]
    public class RTSMinimapController : MonoBehaviour
    {
        #region Enums
        
        public enum IconShape
        {
            /// <summary>Square/diamond shaped icons</summary>
            Square,
            
            /// <summary>Circular icons</summary>
            Circle
        }
        
        #endregion
        
        #region Inspector Fields
        
        [Header("Main Camera Reference")]
        [Tooltip("Reference to the RTSCameraController component (NOT this camera). This is used for click-to-move functionality.")]
        [SerializeField] private RTSCameraController mainCameraController;
        
        [Header("Minimap Settings")]
        [Tooltip("Minimap render texture (assign to RawImage in UI)")]
        [SerializeField] private RenderTexture minimapTexture;
        
        [Tooltip("Size of the world area the minimap covers")]
        [SerializeField] private Vector2 worldSize = new Vector2(200f, 200f);
        
        [Tooltip("Center of the minimap world area")]
        [SerializeField] private Vector2 worldCenter = Vector2.zero;
        
        [Tooltip("Height of the minimap camera above ground")]
        [SerializeField] private float cameraHeight = 100f;
        
        [Header("Click-to-Move")]
        [Tooltip("Enable clicking on minimap to move main camera")]
        [SerializeField] private bool enableClickToMove = true;
        
        [Tooltip("RectTransform of the UI element displaying the minimap")]
        [SerializeField] private RectTransform minimapUIRect;
        
        [Header("View Indicator")]
        [Tooltip("Show the main camera's view frustum on the minimap")]
        [SerializeField] private bool showViewIndicator = true;
        
        [Tooltip("Material for the view indicator (use unlit/transparent)")]
        [SerializeField] private Material viewIndicatorMaterial;
        
        [Tooltip("Color of the view indicator")]
        [SerializeField] private Color viewIndicatorColor = new Color(1f, 1f, 1f, 0.5f);

        [Tooltip("Optional UI rectangle overlay to show the main camera view on the minimap texture (uses minimapUIRect space)")]
        [SerializeField] private RectTransform viewIndicatorUIRect;

        [Tooltip("Keep the UI indicator size fixed in minimap pixels (position still reflects camera center). If false, size reflects the camera frustum footprint.")]
        [SerializeField] private bool lockViewIndicatorSize = false;

        [Tooltip("Pixel size for the locked UI indicator when lockViewIndicatorSize is enabled.")]
        [SerializeField] private Vector2 lockedViewIndicatorSize = new Vector2(24f, 24f);

        [Tooltip("Position the UI indicator from the camera pivot (ground focus point) instead of the frustum extents. Helpful if you want the indicator center to reach the minimap edges when the camera footprint is large.")]
        [SerializeField] private bool usePivotForIndicatorUI = false;
        
        [Header("Player Target Indicator")]
        [Tooltip("Show an indicator for the player target (e.g. the main character or base)")]
        [SerializeField] private bool showPlayerTarget = false;
        
        [Tooltip("The player target transform. If null, will try to get it from the CrowdController.")]
        [SerializeField] private Transform playerTarget;
        
        [Tooltip("Icon/Sprite for the player target indicator")]
        [SerializeField] private Sprite playerTargetIcon;
        
        [Tooltip("Color of the player target indicator")]
        [SerializeField] private Color playerTargetColor = Color.green;
        
        [Tooltip("Size of the player target indicator (pixels)")]
        [SerializeField] private Vector2 playerTargetSize = new Vector2(16f, 16f);
        
        [Tooltip("Optional UI RectTransform for the player target indicator. If null, one will be created if showPlayerTarget is true.")]
        [SerializeField] private RectTransform playerTargetUIRect;

        [Header("Agent Icons (Legacy)")]
        [Tooltip("Enable simplified icon markers on the minimap instead of real agent models.\\n\\nALTERNATIVE: Add a 'GPUI Camera' component to this minimap camera to render actual crowd agent models via GPU Instancer Pro.\\n\\nThe icon system uses simple colored shapes (circles/squares) on a dedicated layer.")]
        [SerializeField] private bool showAgentIcons = false;
        
        [Tooltip("Reference to the EnemyMassesCrowdController to get agent positions")]
        [SerializeField] private EnemyMassesCrowdController crowdController;

        [Tooltip("RTS controller to determine player-controlled factions (optional)")]
        [SerializeField] private EnemyMassesRTSController rtsController;

        [Tooltip("Fog of war provider for visibility checks (optional; if null, non-player icons stay hidden)")]
        [SerializeField] private MonoBehaviour fogOfWarProvider;
        
        [Tooltip("Shape of the agent icons")]
        [SerializeField] private IconShape iconShape = IconShape.Square;
        
        [Tooltip("Size of agent icons in world units")]
        [SerializeField] private float iconSize = 2f;
        
        [Tooltip("Use faction colors from EnemyCrowd assets")]
        [SerializeField] private bool useFactionColors = true;
        
        [Tooltip("Default icon color if faction color is not available")]
        [SerializeField] private Color defaultIconColor = Color.red;
        
        [Tooltip("Icon color for dead agents (set alpha to 0 to hide dead agents)")]
        [SerializeField] private Color deadAgentColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        
        [Tooltip("Show dead agents on minimap")]
        [SerializeField] private bool showDeadAgents = false;
        
        [Header("Performance")]
        [Tooltip("How often to update agent icons (in seconds). Lower = smoother but more CPU.")]
        [SerializeField] private float iconUpdateInterval = 0.1f;
        
        [Tooltip("Maximum number of icons to render (0 = unlimited)")]
        [SerializeField] private int maxIcons = 1000;
        
        [Tooltip("Automatically fit minimap bounds to agents on start")]
        [SerializeField] private bool autoFitToAgentsOnStart = false;
        
        [Tooltip("Padding around agents when auto-fitting bounds")]
        [SerializeField] private float autoFitPadding = 20f;
        
        [Header("Debug")]
        [Tooltip("Enable debug logging for icon rendering")]
        [SerializeField] private bool debugIconRendering = false;

        [Tooltip("Hide icons for factions not controlled by the player (requires RTS controller)")]
        [SerializeField] private bool hideNonPlayerFactions = true;

        [Tooltip("Reveal non-player icons only when the area is visible in fog")]
        [SerializeField] private bool requireFogRevealForNonPlayers = true;

        [Header("Bounds Sync")]
        [Tooltip("If a FogOfWarLite is assigned/found, copy its grid size and center into worldSize/worldCenter automatically.")]
        [SerializeField] private bool autoSyncFogBounds = true;

        [Tooltip("Prevent any automatic bounds overrides (Fog of War, auto-fit, etc.) so world size/center stay manual.")]
        [SerializeField] private bool lockWorldBounds = false;

        [Header("Debug - View Indicator")]
        [Tooltip("Log view indicator rect/viewport diagnostics each interval to track drift issues")]
        [SerializeField] private bool debugViewIndicator = false;

        [Tooltip("Seconds between view indicator debug logs")]
        #pragma warning disable CS0414 // retained for optional debug logging
        [SerializeField, Range(0.1f, 5f)] private float viewIndicatorDebugInterval = 1f;
        #pragma warning restore CS0414

        private IFogVisibility _fog;
        
        [Header("Icon Layer")]
        [Tooltip("Layer for minimap icons. Icons will be placed on this layer and the minimap camera will be configured to see only this layer (plus terrain/environment). Use a dedicated layer like 'MinimapIcons' or layer 31.")]
        [SerializeField] private int iconLayer = 31;
        
        [Header("Exclude Layers")]
        [Tooltip("Layers to exclude from minimap rendering (e.g., VFX, particles, effects). These layers will be removed from the minimap camera's culling mask. You can include the Default layer here if VFX is on layer 0.")]
        [SerializeField] private LayerMask excludeLayers;
        
        [Tooltip("Auto-exclude common VFX layer names (VFX, Effects, Particles, TransparentFX, etc.). This searches for layers by name and excludes any that are found.")]
        [SerializeField] private bool autoExcludeVFXLayers = true;
        
        [Header("Static Background (Performance)")]
        [Tooltip("Use a static background image instead of rendering the terrain every frame. Much more efficient for large worlds. The background is captured once at startup (or manually via RefreshBackground()).")]
        [SerializeField] private bool useStaticBackground = false;
        
        [Tooltip("Pre-captured background texture. If not assigned and useStaticBackground is true, a capture will be taken automatically at startup.")]
        [SerializeField] private Texture2D staticBackgroundTexture;
        
        [Tooltip("Resolution of the auto-captured background texture. Higher = better quality but more memory.")]
        [SerializeField] private int backgroundCaptureResolution = 1024;
        
        [Tooltip("Delay before auto-capturing background (allows scene to fully load)")]
        [SerializeField] private float backgroundCaptureDelay = 0.5f;
        
        [Tooltip("Automatically refresh the background at this interval (in seconds). Set to 0 to disable auto-refresh. Useful for maps that change dynamically.")]
        [SerializeField] private float backgroundRefreshInterval = 0f;
        
        [Header("Legacy")]
        [Tooltip("Layer mask for objects to show on minimap (legacy system)")]
        [SerializeField] private LayerMask minimapIconLayer;
        
        #endregion
        
        #region Private Fields
        
        private Camera _minimapCamera;
        private Camera _mainCamera;
        private GameObject _viewIndicator;
        private MeshFilter _viewIndicatorMesh;
        private readonly Vector3[] _viewCorners = new Vector3[4];
        
        // GPU Instanced Agent Icons
        private Mesh _iconMesh;
        private Material _instancedMaterial;
        private float _lastIconUpdateTime;
        
        // CommandBuffer for camera-specific rendering
        private CommandBuffer _iconCommandBuffer;
        
        // Batching data per faction (for different colors)
        private class FactionBatch
        {
            public Material material;
            public List<Matrix4x4> matrices = new List<Matrix4x4>();
            public Matrix4x4[] matrixArray; // Reusable array for DrawMeshInstanced
            public Color color;
        }
        
        private Dictionary<int, FactionBatch> _factionBatches = new Dictionary<int, FactionBatch>();
        private Dictionary<Color32, FactionBatch> _externalBatches = new Dictionary<Color32, FactionBatch>();
        private FactionBatch _deadBatch;
        private FactionBatch _defaultBatch;
        private Shader _iconShader;
        
        // Pre-allocated arrays to avoid GC
        private List<Vector3> _positionCache = new List<Vector3>();
        private List<int> _factionCache = new List<int>();
        private List<bool> _deadCache = new List<bool>();
        
        // Constants for GPU instancing
        private const int MAX_INSTANCES_PER_BATCH = 1023; // Unity limit for DrawMeshInstanced
        private static readonly string[] IconShaderNames = new string[]
        {
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Lit",
            "Unlit/Color",
            "Sprites/Default",
            "UI/Default",
            "Hidden/InternalColored"
        };
        
        // Simple GameObject-based icon pool (fallback for URP compatibility)
        private List<GameObject> _iconPool = new List<GameObject>();
        private List<MeshRenderer> _iconRenderers = new List<MeshRenderer>();
        private GameObject _iconContainer;
        private int _activeIconCount;
        private bool _useGameObjectFallback = true; // Use GameObjects instead of DrawMeshNow
        
        // Static background
        private Texture2D _capturedBackground;
        private GameObject _backgroundQuad;
        private bool _backgroundCaptured;
        private int _originalCullingMask;
        private float _lastBackgroundRefreshTime;

        // Player faction cache for icon filtering
        private readonly HashSet<string> _playerFactions = new HashSet<string>();

        // Debug
        private float _lastViewIndicatorDebugTime;
        private Vector3 _lastMainCamPos;
        private Vector3 _lastMainCamEuler;
        private bool _fogBoundsSynced;
        
        // Debug
        private float _lastDebugLogTime;
        private const float DEBUG_LOG_INTERVAL = 2f;
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            _minimapCamera = GetComponent<Camera>();

            CachePlayerFactions();
            
            // Safety check - warn if this camera has an RTSCameraController
            if (GetComponent<RTSCameraController>() != null)
            {
                Debug.LogError("[RTSMinimapController] This component must be on a SEPARATE minimap camera, NOT the main camera! " +
                    "The minimap camera should render to a RenderTexture displayed in UI. " +
                    "Remove RTSMinimapController from your main camera and create a separate camera for the minimap.");
                enabled = false;
                return;
            }
            
            // Setup orthographic camera for top-down view
            _minimapCamera.orthographic = true;
            _minimapCamera.orthographicSize = worldSize.y / 2f;
            
            // Ensure camera can see from high up down to ground level
            _minimapCamera.nearClipPlane = 0.1f;
            _minimapCamera.farClipPlane = cameraHeight + 10f;
            
            // Ensure the minimap camera can see the icon layer
            _minimapCamera.cullingMask |= (1 << iconLayer);
            
            // Exclude VFX/effects layers from minimap
            ExcludeVFXLayers();
            
            // Position camera
            transform.position = new Vector3(worldCenter.x, cameraHeight, worldCenter.y);
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            
            // Setup render texture if assigned
            if (minimapTexture != null)
            {
                _minimapCamera.targetTexture = minimapTexture;
            }
            else
            {
                Debug.LogWarning("[RTSMinimapController] No RenderTexture assigned. The minimap camera should render to a RenderTexture " +
                    "that is displayed on a RawImage in your UI.");
            }
            
            // Find main camera controller if not assigned
            if (mainCameraController == null)
            {
                mainCameraController = FindFirstObjectByType<RTSCameraController>();
            }
            
            if (mainCameraController != null)
            {
                _mainCamera = mainCameraController.GetComponentInChildren<Camera>();
                
                // Safety check - make sure we're not referencing ourselves
                if (_mainCamera == _minimapCamera)
                {
                    Debug.LogError("[RTSMinimapController] Main Camera Controller is referencing this minimap camera! " +
                        "Assign the RTSCameraController on your MAIN camera, not this minimap camera.");
                    _mainCamera = null;
                }
                else if (_mainCamera != null)
                {
                    // Exclude icon layer from main camera so icons only show on minimap
                    _mainCamera.cullingMask &= ~(1 << iconLayer);
                }
            }
            
            // Also exclude icon layer from all other cameras in the scene
            ExcludeIconLayerFromOtherCameras();
            
            // Find crowd controller if not assigned
            if (crowdController == null)
            {
                crowdController = FindFirstObjectByType<EnemyMassesCrowdController>();
            }

            CacheFogProvider();

            // Create view indicator
            if (showViewIndicator)
            {
                CreateViewIndicator();
            }
            
            // Create player target indicator
            if (showPlayerTarget)
            {
                CreatePlayerTargetIndicator();
            }
            
            // Create agent icon system
            if (showAgentIcons)
            {
                CreateAgentIconSystem();
            }

            if (!lockWorldBounds)
            {
                TrySyncFogBounds();
            }
        }

        private void CacheFogProvider()
        {
            _fog = null;
            if (fogOfWarProvider != null)
            {
                _fog = fogOfWarProvider as IFogVisibility;
            }
            if (_fog == null)
            {
                // Try to find any IFogVisibility in scene
                var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] is IFogVisibility fv)
                    {
                        _fog = fv;
                        fogOfWarProvider = behaviours[i];
                        break;
                    }
                }
            }

            // Bounds sync is attempted in TrySyncFogBounds() once _fog is available
        }

        private void TrySyncFogBounds()
        {
            if (_fogBoundsSynced || !autoSyncFogBounds) return;
            if (_fog == null) CacheFogProvider();

            // Avoid hard dependency on FogOfWarLite assembly; use reflection by type name
            var fogObj = _fog as object;
            if (fogObj == null) return;

            Type fogType = fogObj.GetType();

            // Allow a hook on fog to override bounds: bool GetMinimapBounds(out Vector2 size, out Vector2 center)
            if (TryReadFogSyncOverride(fogObj, fogType, out float hookSizeX, out float hookSizeY, out Vector2 hookCenter))
            {
                ApplyFogSync(hookSizeX, hookSizeY, hookCenter);
                return;
            }

            if (fogType.FullName != "Arawn.Rendering.Fog.FogOfWarLite") return;

            float gridX = GetNumericFieldOrProperty(fogObj, fogType, "gridX", 0f);
            float gridY = GetNumericFieldOrProperty(fogObj, fogType, "gridY", 0f);
            float unitScale = GetNumericFieldOrProperty(fogObj, fogType, "unitScale", 1f);
            Transform gridCenter = GetFieldOrPropertyTransform(fogObj, fogType, "gridCenter", null);

            float newSizeX = gridX * unitScale;
            float newSizeY = gridY * unitScale;
            Vector3 center = gridCenter != null ? gridCenter.position : (fogObj as MonoBehaviour)?.transform.position ?? Vector3.zero;

            ApplyFogSync(newSizeX, newSizeY, new Vector2(center.x, center.z));
        }

        private bool TryReadFogSyncOverride(object fogObj, Type fogType, out float sizeX, out float sizeY, out Vector2 center)
        {
            sizeX = sizeY = 0f;
            center = Vector2.zero;
            try
            {
                var method = fogType.GetMethod("GetMinimapBounds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (method != null)
                {
                    object[] args = new object[] { Vector2.zero, Vector2.zero };
                    bool ok = (bool)method.Invoke(fogObj, args);
                    if (ok)
                    {
                        Vector2 s = (Vector2)args[0];
                        Vector2 c = (Vector2)args[1];
                        sizeX = s.x;
                        sizeY = s.y;
                        center = c;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private void ApplyFogSync(float sizeX, float sizeY, Vector2 center)
        {
            if (lockWorldBounds)
            {
                if (debugViewIndicator)
                {
                    Debug.Log("[RTSMinimapController] Bounds locked; skipping fog sync override.");
                }
                return;
            }

            if (sizeX <= 0f || sizeY <= 0f)
            {
                if (debugViewIndicator)
                {
                    Debug.LogWarning("[RTSMinimapController] Fog sync aborted: size is zero or negative.");
                }
                return;
            }

            worldSize = new Vector2(sizeX, sizeY);
            worldCenter = center;

            if (_minimapCamera != null)
            {
                _minimapCamera.orthographicSize = worldSize.y * 0.5f;
                transform.position = new Vector3(worldCenter.x, cameraHeight, worldCenter.y);
            }

            _fogBoundsSynced = true;
            if (debugViewIndicator)
            {
                Debug.Log($"[RTSMinimapController] Synced bounds -> worldSize=({worldSize.x:F2},{worldSize.y:F2}), worldCenter=({worldCenter.x:F2},{worldCenter.y:F2})");
            }
        }

        private float GetNumericFieldOrProperty(object obj, Type type, string name, float fallback)
        {
            try
            {
                var field = type.GetField(name);
                if (field != null)
                {
                    object v = field.GetValue(obj);
                    if (v is float f) return f;
                    if (v is int i) return i;
                    if (v is double d) return (float)d;
                }
                var prop = type.GetProperty(name);
                if (prop != null)
                {
                    object v = prop.GetValue(obj);
                    if (v is float f) return f;
                    if (v is int i) return i;
                    if (v is double d) return (float)d;
                }
            }
            catch { }
            return fallback;
        }

        private Transform GetFieldOrPropertyTransform(object obj, Type type, string name, Transform fallback)
        {
            try
            {
                var field = type.GetField(name);
                if (field != null && typeof(Transform).IsAssignableFrom(field.FieldType)) return (Transform)field.GetValue(obj);
                var prop = type.GetProperty(name);
                if (prop != null && typeof(Transform).IsAssignableFrom(prop.PropertyType)) return (Transform)prop.GetValue(obj);
            }
            catch { }
            return fallback;
        }

        private void CachePlayerFactions()
        {
            _playerFactions.Clear();
            if (rtsController == null)
            {
                rtsController = FindFirstObjectByType<EnemyMassesRTSController>();
            }

            if (rtsController != null && rtsController.playerControlledFactions != null)
            {
                foreach (var f in rtsController.playerControlledFactions.SelectedFactions)
                {
                    if (!string.IsNullOrEmpty(f)) _playerFactions.Add(f);
                }
            }
        }

        private IReadOnlyList<IRTSExternalSelectable> GetExternalSelectables()
        {
            if (rtsController == null)
            {
                rtsController = FindFirstObjectByType<EnemyMassesRTSController>();
            }

            return rtsController != null ? rtsController.ExternalSelectables : null;
        }

        private IReadOnlyCollection<IRTSMinimapIcon> GetRegisteredMinimapIcons()
        {
            return RTSWorldUnitRegistry.RegisteredMinimapIcons;
        }

        private static bool ContainsExternal(IReadOnlyList<IRTSExternalSelectable> externals, IRTSExternalSelectable candidate)
        {
            if (externals == null || candidate == null) return false;
            for (int i = 0; i < externals.Count; i++)
            {
                if (ReferenceEquals(externals[i], candidate)) return true;
            }
            return false;
        }
        
        private void Start()
        {
            // Auto-fit to agents after a short delay (let agents spawn first)
            if (autoFitToAgentsOnStart && !lockWorldBounds)
            {
                Invoke(nameof(DelayedAutoFit), 0.5f);
            }
            
            // Setup static background if enabled
            if (useStaticBackground)
            {
                SetupStaticBackground();
            }
        }
        
        private void DelayedAutoFit()
        {
            FitBoundsToAgents(autoFitPadding);
        }
        
        private void Update()
        {
            if (enableClickToMove)
            {
                ProcessMinimapClick();
            }
            
            if (showViewIndicator && _mainCamera != null)
            {
                UpdateViewIndicator();
            }
            
            if (showPlayerTarget)
            {
                UpdatePlayerTargetIndicator();
            }
            
            if (showAgentIcons)
            {
                if (hideNonPlayerFactions && _playerFactions.Count == 0) CachePlayerFactions();
                if (requireFogRevealForNonPlayers && _fog == null) CacheFogProvider();

                // Throttle icon position updates for performance
                if (Time.time - _lastIconUpdateTime >= iconUpdateInterval)
                {
                    if (_useGameObjectFallback)
                    {
                        UpdateGameObjectIcons();
                    }
                    else
                    {
                        UpdateAgentIconData();
                    }
                    _lastIconUpdateTime = Time.time;
                }
            }
            // Auto-refresh static background if enabled
            if (useStaticBackground && _backgroundCaptured && backgroundRefreshInterval > 0f)
            {
                if (Time.time - _lastBackgroundRefreshTime >= backgroundRefreshInterval)
                {
                    RefreshBackground();
                }
            }
        }
        
        private void LateUpdate()
        {
            // Update command buffer for icons (CommandBuffer approach - may not work in URP)
            if (showAgentIcons && _iconMesh != null && _iconCommandBuffer != null)
            {
                RenderAgentIcons();
            }
        }
        
        /// <summary>
        /// Called after camera renders - PRIMARY rendering method for URP compatibility
        /// </summary>
        private void OnRenderObject()
        {
            // Only render for minimap camera
            if (Camera.current != _minimapCamera) return;
            if (!showAgentIcons || _iconMesh == null) return;
            
            if (debugIconRendering && Time.frameCount % 60 == 0)
            {
                Debug.Log($"[RTSMinimapController] OnRenderObject CALLED for camera: {Camera.current.name}");
            }
            
            // Use direct GL rendering which works in all render pipelines
            RenderIconsGL();
        }
        
        /// <summary>
        /// Render icons using GL/Graphics calls - works in URP
        /// </summary>
        private void RenderIconsGL()
        {
            int totalRendered = 0;
            totalRendered += RenderBatchGL(_deadBatch);
            totalRendered += RenderBatchGL(_defaultBatch);
            foreach (var batch in _factionBatches.Values)
            {
                totalRendered += RenderBatchGL(batch);
            }
            foreach (var batch in _externalBatches.Values)
            {
                totalRendered += RenderBatchGL(batch);
            }
            
            if (debugIconRendering && totalRendered > 0 && Time.frameCount % 120 == 0)
            {
                Debug.Log($"[RTSMinimapController] OnRenderObject rendered {totalRendered} icons for camera {Camera.current.name}");
            }
        }
        
        private int RenderBatchGL(FactionBatch batch)
        {
            if (batch == null || batch.matrices.Count == 0 || batch.material == null) return 0;
            
            if (debugIconRendering && Time.frameCount % 120 == 0)
            {
                Debug.Log($"[RTSMinimapController] RenderBatchGL: {batch.matrices.Count} icons, color={batch.color}, material={batch.material.name}");
            }
            
            // Set up material for rendering
            if (!batch.material.SetPass(0))
            {
                if (debugIconRendering) Debug.LogWarning($"[RTSMinimapController] SetPass(0) failed for material");
                return 0;
            }
            
            // Draw each icon using GL
            int count = 0;
            foreach (var matrix in batch.matrices)
            {
                Graphics.DrawMeshNow(_iconMesh, matrix);
                count++;
            }
            return count;
        }
        
        private void ExcludeIconLayerFromOtherCameras()
        {
            // Find all cameras and exclude the icon layer (except minimap camera)
            Camera[] allCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var cam in allCameras)
            {
                if (cam != _minimapCamera)
                {
                    cam.cullingMask &= ~(1 << iconLayer);
                }
            }
        }
        
        private void ExcludeVFXLayers()
        {
            // Exclude manually specified layers
            if (excludeLayers != 0)
            {
                _minimapCamera.cullingMask &= ~excludeLayers;
            }
            
            // Auto-exclude common VFX layer names
            if (autoExcludeVFXLayers)
            {
                _minimapCamera.cullingMask = ExcludeVFXFromMask(_minimapCamera.cullingMask);
            }
        }
        
        /// <summary>
        /// Returns a culling mask with common VFX layers excluded (by layer name)
        /// </summary>
        private int ExcludeVFXFromMask(int mask)
        {
            string[] vfxLayerNames = new string[]
            {
                "VFX", "vfx", "Effects", "effects", "Particles", "particles",
                "FX", "fx", "TransparentFX", "Visual Effects", "VisualEffects",
                "Projectiles", "projectiles", "Impacts", "impacts",
                "PostProcessing", "Post Processing", "Decals", "decals"
            };
            
            foreach (var layerName in vfxLayerNames)
            {
                int layer = LayerMask.NameToLayer(layerName);
                if (layer >= 0)
                {
                    mask &= ~(1 << layer);
                }
            }
            
            return mask;
        }
        
        private void OnDestroy()
        {
            if (_viewIndicator != null)
            {
                Destroy(_viewIndicator);
            }
            
            // Clean up GPU instancing resources
            if (_iconCommandBuffer != null)
            {
                if (_minimapCamera != null && IsBuiltInPipeline())
                {
                    _minimapCamera.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, _iconCommandBuffer);
                }
                _iconCommandBuffer.Dispose();
                _iconCommandBuffer = null;
            }
            
            if (_iconMesh != null)
            {
                Destroy(_iconMesh);
            }
            
            if (_instancedMaterial != null)
            {
                Destroy(_instancedMaterial);
            }
            
            // Clean up faction batch materials
            foreach (var batch in _factionBatches.Values)
            {
                if (batch.material != null) Destroy(batch.material);
            }
            _factionBatches.Clear();

            foreach (var batch in _externalBatches.Values)
            {
                if (batch.material != null) Destroy(batch.material);
            }
            _externalBatches.Clear();
            
            if (_deadBatch?.material != null)
            {
                Destroy(_deadBatch.material);
            }
            
            if (_defaultBatch?.material != null)
            {
                Destroy(_defaultBatch.material);
            }
            
            // Clean up icon pool
            if (_iconContainer != null)
            {
                Destroy(_iconContainer);
            }
            _iconPool.Clear();
            _iconRenderers.Clear();
            
            // Clean up static background
            if (_capturedBackground != null)
            {
                Destroy(_capturedBackground);
            }
            if (_backgroundQuad != null)
            {
                Destroy(_backgroundQuad);
            }
        }
        
        #endregion
        
        #region Click-to-Move
        
        private void ProcessMinimapClick()
        {
            if (Mouse.current == null || minimapUIRect == null) return;
            
            // Get mouse position
            Vector2 mousePos = Mouse.current.position.ReadValue();
            
            // Check if click is on minimap UI element
            if (!RectTransformUtility.RectangleContainsScreenPoint(minimapUIRect, mousePos))
            {
                return;
            }
            
            // Check for left click (camera move) or right click (agent move command)
            bool leftClick = Mouse.current.leftButton.wasPressedThisFrame;
            bool rightClick = Mouse.current.rightButton.wasPressedThisFrame;
            
            if (!leftClick && !rightClick) return;
            
            // Convert screen position to world position
            Vector3 worldPos = ScreenToMinimapWorldPosition(mousePos);
            
            if (leftClick)
            {
                // Left click: Move camera to this position
                if (mainCameraController != null)
                {
                    mainCameraController.FocusOnPosition(worldPos);
                }
            }
            else if (rightClick)
            {
                // Right click: Move selected agents to this position (command)
                if (rtsController != null && rtsController.SelectedCount > 0)
                {
                    rtsController.MoveSelectedAgentsTo(worldPos);
                }
            }
        }
        
        /// <summary>
        /// Converts a screen position to a world position on the minimap.
        /// Returns the world XZ position at Y=0.
        /// </summary>
        public Vector3 ScreenToMinimapWorldPosition(Vector2 screenPos)
        {
            if (minimapUIRect == null) return Vector3.zero;
            
            // Convert screen position to local position in minimap rect
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                minimapUIRect, screenPos, null, out Vector2 localPoint);
            
            // Convert to normalized position (0-1)
            Rect rect = minimapUIRect.rect;
            Vector2 normalizedPos = new Vector2(
                (localPoint.x - rect.x) / rect.width,
                (localPoint.y - rect.y) / rect.height
            );
            
            // Convert to world position
            return new Vector3(
                worldCenter.x + (normalizedPos.x - 0.5f) * worldSize.x,
                0f,
                worldCenter.y + (normalizedPos.y - 0.5f) * worldSize.y
            );
        }
        
        /// <summary>
        /// Checks if a screen position is over the minimap UI.
        /// </summary>
        public bool IsScreenPositionOverMinimap(Vector2 screenPos)
        {
            if (minimapUIRect == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(minimapUIRect, screenPos);
        }
        
        /// <summary>
        /// Gets the RectTransform of the minimap UI element.
        /// </summary>
        public RectTransform MinimapUIRect => minimapUIRect;
        
        #endregion
        
        #region View Indicator
        
        private void CreateViewIndicator()
        {
            _viewIndicator = new GameObject("MinimapViewIndicator");
            _viewIndicator.transform.SetParent(transform);
            
            // Add mesh components
            _viewIndicatorMesh = _viewIndicator.AddComponent<MeshFilter>();
            var renderer = _viewIndicator.AddComponent<MeshRenderer>();
            
            // Setup material
            if (viewIndicatorMaterial != null)
            {
                renderer.material = viewIndicatorMaterial;
                renderer.material.color = viewIndicatorColor;
            }
            else
            {
                // Create a simple unlit material
                var mat = new Material(Shader.Find("Unlit/Color"));
                mat.color = viewIndicatorColor;
                renderer.material = mat;
            }
            
            // Set to icon layer so it's only visible by minimap camera
            // and won't conflict with other layers
            _viewIndicator.layer = iconLayer;
            
            // Create initial mesh
            _viewIndicatorMesh.mesh = CreateQuadMesh();
        }
        
        private Mesh CreateQuadMesh()
        {
            var mesh = new Mesh();
            
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(-0.5f, 0f, 0.5f)
            };
            
            mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            mesh.normals = new Vector3[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            
            return mesh;
        }
        
        private void UpdateViewIndicator()
        {
            if (_mainCamera == null) return;

            PopulateGroundCorners(_viewCorners);
            UpdateViewIndicatorMesh(_viewCorners);
            UpdateViewIndicatorUI(_viewCorners);

            if (debugViewIndicator && Time.time - _lastViewIndicatorDebugTime >= viewIndicatorDebugInterval)
            {
                _lastViewIndicatorDebugTime = Time.time;
                LogViewIndicatorDebug(_viewCorners);
            }
        }

        private void PopulateGroundCorners(Vector3[] corners)
        {
            // Bottom-left
            Ray ray = _mainCamera.ViewportPointToRay(new Vector3(0, 0, 0));
            corners[0] = GetGroundIntersection(ray);

            // Bottom-right
            ray = _mainCamera.ViewportPointToRay(new Vector3(1, 0, 0));
            corners[1] = GetGroundIntersection(ray);

            // Top-right
            ray = _mainCamera.ViewportPointToRay(new Vector3(1, 1, 0));
            corners[2] = GetGroundIntersection(ray);

            // Top-left
            ray = _mainCamera.ViewportPointToRay(new Vector3(0, 1, 0));
            corners[3] = GetGroundIntersection(ray);
        }

        private void UpdateViewIndicatorMesh(Vector3[] corners)
        {
            if (_viewIndicatorMesh == null) return;

            var mesh = _viewIndicatorMesh.mesh;

            // Convert to local space (relative to minimap camera)
            Vector3[] localCorners = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                // Convert world position to minimap local position
                localCorners[i] = new Vector3(
                    corners[i].x - worldCenter.x,
                    cameraHeight - 1f, // Just below the minimap camera
                    corners[i].z - worldCenter.y
                );
            }

            mesh.vertices = localCorners;
            mesh.RecalculateBounds();

            // Position the indicator
            if (_viewIndicator != null)
            {
                _viewIndicator.transform.localPosition = Vector3.zero;
                _viewIndicator.transform.localRotation = Quaternion.identity;
            }
        }

        private void UpdateViewIndicatorUI(Vector3[] corners)
        {
            if (viewIndicatorUIRect == null || minimapUIRect == null || _minimapCamera == null) return;
            if (worldSize.x <= 0f || worldSize.y <= 0f) return;

            // Ensure indicator is a child of the minimap rect so anchoring works correctly
            if (viewIndicatorUIRect.parent != minimapUIRect)
            {
                viewIndicatorUIRect.SetParent(minimapUIRect, false);
            }

            if (viewIndicatorUIRect.TryGetComponent<Image>(out var img))
            {
                img.color = viewIndicatorColor;
            }

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;

            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 vp = _minimapCamera.WorldToViewportPoint(corners[i]);
                float nx = vp.x;
                float ny = vp.y;

                if (nx < minX) minX = nx;
                if (ny < minY) minY = ny;
                if (nx > maxX) maxX = nx;
                if (ny > maxY) maxY = ny;
            }

            // Clamp after collecting extrema for sizing
            float cMinX = Mathf.Clamp01(minX);
            float cMinY = Mathf.Clamp01(minY);
            float cMaxX = Mathf.Clamp01(maxX);
            float cMaxY = Mathf.Clamp01(maxY);

            Rect rect = minimapUIRect.rect;
            float width = Mathf.Max(0f, (cMaxX - cMinX) * rect.width);
            float height = Mathf.Max(0f, (cMaxY - cMinY) * rect.height);

            if (lockViewIndicatorSize)
            {
                width = lockedViewIndicatorSize.x;
                height = lockedViewIndicatorSize.y;
            }

            // Use the frustum center unless pivot mode is enabled
            float centerX = Mathf.Clamp01((minX + maxX) * 0.5f);
            float centerY = Mathf.Clamp01((minY + maxY) * 0.5f);

            Vector3 pivotGround = Vector3.zero;
            Vector3 pivotViewport = Vector3.zero;
            if (usePivotForIndicatorUI && mainCameraController != null)
            {
                pivotGround = mainCameraController.GetPosition();
                pivotViewport = _minimapCamera.WorldToViewportPoint(pivotGround);

                centerX = Mathf.Clamp01(pivotViewport.x);
                centerY = Mathf.Clamp01(pivotViewport.y);

                // When using pivot mode, force indicator size to the locked size for stability
                width = lockedViewIndicatorSize.x;
                height = lockedViewIndicatorSize.y;
            }

            // Anchor remains centered in parent; offset anchoredPosition maps normalized center to rect corners
            viewIndicatorUIRect.anchorMin = viewIndicatorUIRect.anchorMax = new Vector2(0.5f, 0.5f);
            viewIndicatorUIRect.pivot = new Vector2(0.5f, 0.5f);
            viewIndicatorUIRect.anchoredPosition = new Vector2(
                (centerX - 0.5f) * rect.width,
                (centerY - 0.5f) * rect.height
            );
            viewIndicatorUIRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            viewIndicatorUIRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

            // Debug logging removed to reduce spam
        }

        private void LogViewIndicatorDebug(Vector3[] corners)
        {
            if (viewIndicatorUIRect == null || minimapUIRect == null || _minimapCamera == null) return;

            // Capture RTSCamera bounds/pivot to spot mismatch between movement limits and minimap world
            Vector3 pivotPos = _mainCamera != null ? _mainCamera.transform.position : Vector3.zero;
            Vector3 pivotGround = mainCameraController != null ? mainCameraController.GetPosition() : Vector3.zero;
            Vector3 pivotViewport = _minimapCamera != null ? _minimapCamera.WorldToViewportPoint(pivotGround) : Vector3.zero;
            float camMinX = 0f, camMaxX = 0f, camMinZ = 0f, camMaxZ = 0f;
            if (mainCameraController != null)
            {
                mainCameraController.GetBounds(out camMinX, out camMaxX, out camMinZ, out camMaxZ);
            }

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;

            // Collect viewport extents for the camera corners
            string cornerInfo = string.Empty;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 vp = _minimapCamera.WorldToViewportPoint(corners[i]);
                float nx = vp.x;
                float ny = vp.y;

                if (nx < minX) minX = nx;
                if (ny < minY) minY = ny;
                if (nx > maxX) maxX = nx;
                if (ny > maxY) maxY = ny;

                cornerInfo += $"[{i}] world=({corners[i].x:F1},{corners[i].z:F1}) vp=({nx:F3},{ny:F3}) ";
            }

            float cMinX = Mathf.Clamp01(minX);
            float cMinY = Mathf.Clamp01(minY);
            float cMaxX = Mathf.Clamp01(maxX);
            float cMaxY = Mathf.Clamp01(maxY);

            Rect rect = minimapUIRect.rect;
            float width = Mathf.Max(0f, (cMaxX - cMinX) * rect.width);
            float height = Mathf.Max(0f, (cMaxY - cMinY) * rect.height);

            if (lockViewIndicatorSize)
            {
                width = lockedViewIndicatorSize.x;
                height = lockedViewIndicatorSize.y;
            }

            float centerX = Mathf.Clamp01((minX + maxX) * 0.5f);
            float centerY = Mathf.Clamp01((minY + maxY) * 0.5f);

            if (usePivotForIndicatorUI && mainCameraController != null)
            {
                Vector3 pivotGroundDbg = mainCameraController.GetPosition();
                Vector3 pivotViewportDbg = _minimapCamera.WorldToViewportPoint(pivotGroundDbg);
                centerX = Mathf.Clamp01(pivotViewportDbg.x);
                centerY = Mathf.Clamp01(pivotViewportDbg.y);
                width = lockedViewIndicatorSize.x;
                height = lockedViewIndicatorSize.y;
            }

            Vector2 anchorCenter = new Vector2(centerX, centerY);
            Vector2 anchoredPos = new Vector2(
                (centerX - 0.5f) * rect.width,
                (centerY - 0.5f) * rect.height
            );

            // Normalized pivot position relative to minimap world size for quick sanity checks
            float halfSizeX = worldSize.x * 0.5f;
            float halfSizeZ = worldSize.y * 0.5f;
            float normPivotX = halfSizeX > 0f ? (pivotGround.x - worldCenter.x + halfSizeX) / worldSize.x : 0.5f;
            float normPivotZ = halfSizeZ > 0f ? (pivotGround.z - worldCenter.y + halfSizeZ) / worldSize.y : 0.5f;

            // View indicator debug logging removed
        }
        
        private Vector3 GetGroundIntersection(Ray ray)
        {
            // Intersect with ground plane (y = 0)
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
            
            if (groundPlane.Raycast(ray, out float distance))
            {
                return ray.GetPoint(distance);
            }
            
            // Fallback - project forward
            return ray.origin + ray.direction * 100f;
        }
        
        #endregion

        #region Player Target Indicator

        private void CreatePlayerTargetIndicator()
        {
            if (playerTargetUIRect != null) return;
            if (minimapUIRect == null) return;

            GameObject go = new GameObject("PlayerTargetIndicator");
            go.transform.SetParent(minimapUIRect, false);
            
            playerTargetUIRect = go.AddComponent<RectTransform>();
            playerTargetUIRect.anchorMin = new Vector2(0.5f, 0.5f);
            playerTargetUIRect.anchorMax = new Vector2(0.5f, 0.5f);
            playerTargetUIRect.pivot = new Vector2(0.5f, 0.5f);
            playerTargetUIRect.sizeDelta = playerTargetSize;
            
            Image img = go.AddComponent<Image>();
            img.color = playerTargetColor;
            if (playerTargetIcon != null)
            {
                img.sprite = playerTargetIcon;
            }
            
            // Ensure it's visible
            go.SetActive(true);
        }

        private void UpdatePlayerTargetIndicator()
        {
            // Try to find target if missing
            if (playerTarget == null)
            {
                if (crowdController != null && crowdController.PlayerTarget != null)
                {
                    playerTarget = crowdController.PlayerTarget;
                }
                
                if (playerTarget == null)
                {
                    if (playerTargetUIRect != null && playerTargetUIRect.gameObject.activeSelf)
                        playerTargetUIRect.gameObject.SetActive(false);
                    return;
                }
            }

            // Create UI if missing
            if (playerTargetUIRect == null)
            {
                if (minimapUIRect != null)
                    CreatePlayerTargetIndicator();
                else
                    return;
            }

            if (_minimapCamera == null) return;

            Vector3 targetPos = playerTarget.position;
            Vector3 vp = _minimapCamera.WorldToViewportPoint(targetPos);

            // Check visibility (0-1 range)
            bool isVisible = vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
            
            if (playerTargetUIRect.gameObject.activeSelf != isVisible)
                playerTargetUIRect.gameObject.SetActive(isVisible);

            if (isVisible)
            {
                Rect rect = minimapUIRect.rect;
                playerTargetUIRect.anchoredPosition = new Vector2(
                    (vp.x - 0.5f) * rect.width,
                    (vp.y - 0.5f) * rect.height
                );
                
                // Update size/color in case they changed in inspector
                if (playerTargetUIRect.sizeDelta != playerTargetSize)
                    playerTargetUIRect.sizeDelta = playerTargetSize;
                
                Image img = playerTargetUIRect.GetComponent<Image>();
                if (img != null)
                {
                    if (img.color != playerTargetColor) img.color = playerTargetColor;
                    if (img.sprite != playerTargetIcon) img.sprite = playerTargetIcon;
                }
            }
        }

        #endregion
        
        #region Agent Icons (GPU Instanced)
        
        private void CreateAgentIconSystem()
        {
            // Create the icon mesh based on selected shape
            _iconMesh = iconShape == IconShape.Circle ? CreateIconCircleMesh(16) : CreateIconSquareMesh();
            
            Shader instancedShader = GetIconShader();
            if (instancedShader == null)
            {
                Debug.LogError("[RTSMinimapController] Could not find ANY suitable shader for minimap icons! Icons will not render.");
                return;
            }
            
            _instancedMaterial = new Material(instancedShader);
            _instancedMaterial.enableInstancing = true;
            
            // Create default batch for unknown factions
            _defaultBatch = new FactionBatch
            {
                material = CreateIconMaterial(instancedShader, defaultIconColor),
                color = defaultIconColor
            };
            
            // Create dead agent batch
            _deadBatch = new FactionBatch
            {
                material = CreateIconMaterial(instancedShader, deadAgentColor),
                color = deadAgentColor
            };
            
            // Create command buffer for minimap-only rendering (built-in RP only)
            if (IsBuiltInPipeline())
            {
                _iconCommandBuffer = new CommandBuffer
                {
                    name = "Minimap Agent Icons"
                };
                _minimapCamera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, _iconCommandBuffer);
            }
        }

        private Shader GetIconShader()
        {
            if (_iconShader != null) return _iconShader;

            foreach (var shaderName in IconShaderNames)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader != null)
                {
                    _iconShader = shader;
                    break;
                }
            }

            return _iconShader;
        }

        private static bool IsBuiltInPipeline()
        {
            // Built-in pipeline when no SRP is assigned in quality settings, defaults, or currently running
            return RenderPipelineManager.currentPipeline == null &&
                   GraphicsSettings.defaultRenderPipeline == null &&
                   QualitySettings.renderPipeline == null;
        }
        
        private Material CreateIconMaterial(Shader shader, Color color)
        {
            var mat = new Material(shader);
            mat.enableInstancing = true;
            mat.color = color;
            
            // Set color property based on shader type
            if (mat.HasProperty("_BaseColor"))
            {
                // URP shaders use _BaseColor
                mat.SetColor("_BaseColor", color);
            }
            if (mat.HasProperty("_Color"))
            {
                // Legacy/Sprites shaders use _Color
                mat.SetColor("_Color", color);
            }
            
            // Ensure proper rendering settings
            mat.renderQueue = 3000; // Render on top
            
            return mat;
        }

        private FactionBatch GetOrCreateExternalBatch(Color color)
        {
            Color32 key = (Color32)color;
            if (_externalBatches.TryGetValue(key, out FactionBatch cachedBatch))
            {
                return cachedBatch;
            }

            Shader shader = GetIconShader();
            if (shader == null)
            {
                return null;
            }

            var batch = new FactionBatch
            {
                material = CreateIconMaterial(shader, color),
                color = color
            };

            _externalBatches[key] = batch;
            return batch;
        }
        
        private Mesh CreateIconSquareMesh()
        {
            var mesh = new Mesh();
            mesh.name = "IconSquare_Instanced";
            
            float half = 0.5f;
            mesh.vertices = new Vector3[]
            {
                new Vector3(-half, 0f, -half),
                new Vector3(half, 0f, -half),
                new Vector3(half, 0f, half),
                new Vector3(-half, 0f, half)
            };
            
            mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            mesh.normals = new Vector3[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.RecalculateBounds();
            
            return mesh;
        }
        
        private Mesh CreateIconCircleMesh(int segments)
        {
            var mesh = new Mesh();
            mesh.name = "IconCircle_Instanced";
            
            // Create vertices - center + edge vertices
            var vertices = new Vector3[segments + 1];
            vertices[0] = Vector3.zero; // Center
            
            float angleStep = 360f / segments;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * angleStep * Mathf.Deg2Rad;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * 0.5f, 0f, Mathf.Sin(angle) * 0.5f);
            }
            
            // Create triangles (fan from center)
            var triangles = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                triangles[i * 3] = 0; // Center
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i < segments - 1 ? i + 2 : 1; // Last one wraps to vertex 1
            }
            
            // Create normals
            var normals = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                normals[i] = Vector3.up;
            }
            
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            
            return mesh;
        }
        
        /// <summary>
        /// Gets or creates an icon GameObject from the pool
        /// </summary>
        private GameObject GetPooledIcon(int index)
        {
            // Ensure container exists
            if (_iconContainer == null)
            {
                _iconContainer = new GameObject("MinimapIcons");
                _iconContainer.transform.SetParent(transform);
            }
            
            // Create new icon if needed
            while (index >= _iconPool.Count)
            {
                var iconObj = new GameObject($"Icon_{_iconPool.Count}");
                iconObj.transform.SetParent(_iconContainer.transform);
                iconObj.layer = iconLayer;
                
                var mf = iconObj.AddComponent<MeshFilter>();
                mf.sharedMesh = _iconMesh;
                
                var mr = iconObj.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _instancedMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                
                _iconPool.Add(iconObj);
                _iconRenderers.Add(mr);
                
                iconObj.SetActive(false);
            }
            
            return _iconPool[index];
        }
        
        /// <summary>
        /// Gets or creates a faction-colored material for icon GameObjects
        /// </summary>
        private Material GetFactionMaterial(int factionIndex, EnemyCrowd crowd, bool isDead)
        {
            if (isDead)
            {
                return _deadBatch?.material ?? _instancedMaterial;
            }
            
            var batch = GetOrCreateFactionBatch(factionIndex, crowd);
            return batch?.material ?? _instancedMaterial;
        }
        
        /// <summary>
        /// Determines the minimap color for an agent based on settings priority:
        /// 1. Owner player color (if useOwnerPlayerColor enabled and player registered)
        /// 2. Minimap color override (if useMinimapColorOverride enabled)
        /// 3. Faction color (default)
        /// </summary>
        private Color GetAgentMinimapColor(CrowdAgent agent, EnemyCrowd crowd)
        {
            if (crowd == null) return defaultIconColor;
            
            // Priority 1: Owner player color
            if (crowd.useOwnerPlayerColor && agent != null)
            {
                int ownerId = agent.ownerPlayerId;
                if (ownerId >= 0)
                {
                    var player = PlayerRegistry.GetPlayerByIndex(ownerId);
                    if (player != null)
                    {
                        return player.playerColor;
                    }
                }
            }
            
            // Priority 2: Minimap color override
            if (crowd.useMinimapColorOverride)
            {
                return crowd.minimapColorOverride;
            }
            
            // Priority 3: Faction color
            return useFactionColors ? crowd.factionColor : defaultIconColor;
        }
        
        /// <summary>
        /// Gets the appropriate batch for an agent, considering color override modes.
        /// For owner-based colors, creates per-owner batches dynamically.
        /// </summary>
        private FactionBatch GetAgentBatch(CrowdAgent agent, EnemyCrowd crowd, int factionIndex)
        {
            // Check if we need per-owner colors
            if (crowd != null && crowd.useOwnerPlayerColor && agent != null)
            {
                int ownerId = agent.ownerPlayerId;
                if (ownerId >= 0)
                {
                    var player = PlayerRegistry.GetPlayerByIndex(ownerId);
                    if (player != null)
                    {
                        return GetOrCreatePlayerBatch(ownerId, player.playerColor);
                    }
                }
            }
            
            // Check if we need minimap color override batch
            if (crowd != null && crowd.useMinimapColorOverride)
            {
                return GetOrCreateMinimapOverrideBatch(factionIndex, crowd.minimapColorOverride);
            }
            
            // Default: use faction batch
            return GetOrCreateFactionBatch(factionIndex, crowd);
        }
        
        /// <summary>
        /// Gets or creates a batch for a specific player owner.
        /// </summary>
        private FactionBatch GetOrCreatePlayerBatch(int ownerId, Color playerColor)
        {
            // Use a negative key offset to avoid collision with faction indices
            int key = -(ownerId + 1000);
            
            if (_factionBatches.TryGetValue(key, out FactionBatch cachedBatch))
            {
                // Update color if changed
                if (cachedBatch.color != playerColor)
                {
                    cachedBatch.color = playerColor;
                    if (cachedBatch.material != null)
                    {
                        cachedBatch.material.color = playerColor;
                        if (cachedBatch.material.HasProperty("_BaseColor"))
                            cachedBatch.material.SetColor("_BaseColor", playerColor);
                        if (cachedBatch.material.HasProperty("_Color"))
                            cachedBatch.material.SetColor("_Color", playerColor);
                    }
                }
                return cachedBatch;
            }
            
            Shader shader = GetIconShader();
            if (shader == null) shader = Shader.Find("Sprites/Default");
            
            var batch = new FactionBatch
            {
                material = CreateIconMaterial(shader, playerColor),
                color = playerColor
            };
            
            _factionBatches[key] = batch;
            return batch;
        }
        
        /// <summary>
        /// Gets or creates a batch for a faction's minimap color override.
        /// </summary>
        private FactionBatch GetOrCreateMinimapOverrideBatch(int factionIndex, Color overrideColor)
        {
            // Use a different key offset to avoid collision with player batches
            int key = -(factionIndex + 2000);
            
            if (_factionBatches.TryGetValue(key, out FactionBatch cachedBatch))
            {
                return cachedBatch;
            }
            
            Shader shader = GetIconShader();
            if (shader == null) shader = Shader.Find("Sprites/Default");
            
            var batch = new FactionBatch
            {
                material = CreateIconMaterial(shader, overrideColor),
                color = overrideColor
            };
            
            _factionBatches[key] = batch;
            return batch;
        }
        
        private FactionBatch GetOrCreateFactionBatch(int factionIndex, EnemyCrowd enemyCrowd)
        {
            // Handle invalid faction index
            if (factionIndex < 0)
            {
                return _defaultBatch;
            }
            
            // Check cache first
            if (_factionBatches.TryGetValue(factionIndex, out FactionBatch cachedBatch))
            {
                return cachedBatch;
            }
            
            // Create new batch for this faction
            Color factionColor = defaultIconColor;
            
            if (useFactionColors && enemyCrowd != null)
            {
                try
                {
                    factionColor = enemyCrowd.factionColor;
                }
                catch (System.Exception)
                {
                    factionColor = defaultIconColor;
                }
            }
            
            // Use same shader search as CreateAgentIconSystem
            Shader shader = GetIconShader();
            if (shader == null) shader = Shader.Find("Sprites/Default");
            
            var batch = new FactionBatch
            {
                material = CreateIconMaterial(shader, factionColor),
                color = factionColor
            };
            
            _factionBatches[factionIndex] = batch;
            return batch;
        }
        
        /// <summary>
        /// Gathers agent position data into batches by faction. Called at throttled interval.
        /// </summary>
        private void UpdateAgentIconData()
        {
            if (crowdController == null)
            {
                if (debugIconRendering) Debug.LogWarning("[RTSMinimapController] crowdController is null!");
            }
            
            // Clear all batch matrices
            foreach (var batch in _factionBatches.Values)
            {
                batch.matrices.Clear();
            }
            if (_deadBatch != null) _deadBatch.matrices.Clear();
            if (_defaultBatch != null) _defaultBatch.matrices.Clear();
            foreach (var batch in _externalBatches.Values)
            {
                batch.matrices.Clear();
            }
            
            // Get agents safely
            List<CrowdAgent> agents = null;
            if (crowdController != null)
            {
                try
                {
                    agents = crowdController.GetAgents();
                }
                catch (System.Exception e)
                {
                    if (debugIconRendering) Debug.LogWarning($"[RTSMinimapController] Exception getting agents: {e.Message}");
                }
            }

            IReadOnlyList<IRTSExternalSelectable> externals = GetExternalSelectables();
            int externalCount = externals != null ? externals.Count : 0;
            IReadOnlyCollection<IRTSMinimapIcon> registeredIcons = GetRegisteredMinimapIcons();
            int registeredCount = registeredIcons != null ? registeredIcons.Count : 0;
            
            int agentCount = agents != null ? agents.Count : 0;
            if (agentCount == 0 && externalCount == 0 && registeredCount == 0)
            {
                if (debugIconRendering && Time.frameCount % 60 == 0) 
                    Debug.Log("[RTSMinimapController] No agents, externals, or registered icons found");
                return;
            }
            
            int totalCount = agentCount + externalCount + registeredCount;
            int limit = maxIcons > 0 ? Mathf.Min(totalCount, maxIcons) : totalCount;
            int processedCount = 0;
            int outOfBoundsCount = 0;
            
            // Pre-calculate bounds
            float halfX = worldSize.x / 2f;
            float halfZ = worldSize.y / 2f;
            float minX = worldCenter.x - halfX;
            float maxX = worldCenter.x + halfX;
            float minZ = worldCenter.y - halfZ;
            float maxZ = worldCenter.y + halfZ;
            
            // Pre-calculate the transformation matrix components
            Vector3 scale = Vector3.one * iconSize;
            Quaternion rotation = Quaternion.identity;
            
            // Debug: Track first agent position
            Vector3 firstAgentPos = Vector3.zero;
            bool gotFirstAgent = false;
            
            for (int i = 0; i < agentCount && processedCount < limit; i++)
            {
                // Get agent safely
                CrowdAgent agent;
                try
                {
                    agent = agents[i];
                    if (agent == null) continue;
                }
                catch (System.Exception)
                {
                    continue;
                }
                
                // Check dead state
                bool isDead;
                try
                {
                    isDead = agent.isDead;
                }
                catch (System.Exception)
                {
                    continue;
                }
                
                if (isDead && !showDeadAgents) continue;
                
                // Get position safely
                Vector3 agentPos;
                try
                {
                    if (agent.navigationAgent != null || agent.crowdInstance != null)
                    {
                        agentPos = agent.WorldPosition;
                    }
                    else
                    {
                        continue;
                    }
                }
                catch (System.Exception)
                {
                    continue;
                }
                
                // Track first agent for debug
                if (!gotFirstAgent)
                {
                    firstAgentPos = agentPos;
                    gotFirstAgent = true;
                }
                
                // Fast bounds check
                if (agentPos.x < minX || agentPos.x > maxX || agentPos.z < minZ || agentPos.z > maxZ)
                {
                    outOfBoundsCount++;
                    continue;
                }

                // Faction filter (hide non-player factions, and optionally require fog reveal)
                bool isPlayerFaction = false;
                string factionName = null;
                if (hideNonPlayerFactions)
                {
                    try { factionName = agent.enemyCrowd?.factionName; } catch (System.Exception) { }
                    isPlayerFaction = _playerFactions.Count == 0 || (!string.IsNullOrEmpty(factionName) && _playerFactions.Contains(factionName));
                    if (!isPlayerFaction)
                    {
                        // If not player-controlled, optionally hide unless actively visible (not refogged)
                        if (!requireFogRevealForNonPlayers)
                        {
                            continue;
                        }
                        if (_fog == null || !_fog.IsActivelyVisible(agentPos))
                        {
                            continue;
                        }
                    }
                }
                
                // Create transformation matrix for this icon
                // Position icons just below the minimap camera so they're always visible
                // regardless of terrain height
                Vector3 iconPos = new Vector3(agentPos.x, cameraHeight - 2f, agentPos.z);
                Matrix4x4 matrix = Matrix4x4.TRS(iconPos, rotation, scale);
                
                // Add to appropriate batch
                if (isDead)
                {
                    if (_deadBatch != null) _deadBatch.matrices.Add(matrix);
                }
                else
                {
                    // Get faction batch (supports owner player colors and minimap overrides)
                    int factionIndex = -1;
                    EnemyCrowd crowd = null;
                    try
                    {
                        factionIndex = agent.enemyCrowdIndex;
                        crowd = agent.enemyCrowd;
                    }
                    catch (System.Exception)
                    {
                        // Use default
                    }
                    
                    var batch = GetAgentBatch(agent, crowd, factionIndex);
                    batch.matrices.Add(matrix);
                }
                
                processedCount++;
            }

            for (int i = 0; i < externalCount && processedCount < limit; i++)
            {
                IRTSExternalSelectable external = externals[i];
                if (external == null) continue;

                bool isAlive;
                try
                {
                    isAlive = external.IsAlive;
                }
                catch (System.Exception)
                {
                    continue;
                }

                if (!isAlive) continue;
                var externalIcon = external as IRTSMinimapIcon;
                if (externalIcon == null) continue;
                if (!externalIcon.ShowOnMinimap) continue;

                Vector3 externalPos;
                try
                {
                    externalPos = externalIcon.MinimapPosition;
                }
                catch (System.Exception)
                {
                    continue;
                }

                if (!gotFirstAgent)
                {
                    firstAgentPos = externalPos;
                    gotFirstAgent = true;
                }

                if (externalPos.x < minX || externalPos.x > maxX || externalPos.z < minZ || externalPos.z > maxZ)
                {
                    outOfBoundsCount++;
                    continue;
                }

                Vector3 iconPos = new Vector3(externalPos.x, cameraHeight - 2f, externalPos.z);
                Matrix4x4 matrix = Matrix4x4.TRS(iconPos, rotation, scale);

                var batch = GetOrCreateExternalBatch(externalIcon.MinimapColor);
                if (batch != null)
                {
                    batch.matrices.Add(matrix);
                    processedCount++;
                }
            }

            if (registeredCount > 0)
            {
                foreach (var icon in registeredIcons)
                {
                    if (processedCount >= limit) break;
                    if (icon == null) continue;
                    if (!icon.ShowOnMinimap) continue;

                    var externalSelectable = icon as IRTSExternalSelectable;
                    if (externalSelectable != null && ContainsExternal(externals, externalSelectable)) continue;

                    Vector3 iconPos;
                    try
                    {
                        iconPos = icon.MinimapPosition;
                    }
                    catch (System.Exception)
                    {
                        continue;
                    }

                    if (!gotFirstAgent)
                    {
                        firstAgentPos = iconPos;
                        gotFirstAgent = true;
                    }

                    if (iconPos.x < minX || iconPos.x > maxX || iconPos.z < minZ || iconPos.z > maxZ)
                    {
                        outOfBoundsCount++;
                        continue;
                    }

                    Vector3 worldPos = new Vector3(iconPos.x, cameraHeight - 2f, iconPos.z);
                    Matrix4x4 matrix = Matrix4x4.TRS(worldPos, rotation, scale);

                    var batch = GetOrCreateExternalBatch(icon.MinimapColor);
                    if (batch != null)
                    {
                        batch.matrices.Add(matrix);
                        processedCount++;
                    }
                }
            }
            
            // Debug logging (throttled) - ALWAYS log first time, then throttle
            bool shouldLog = debugIconRendering && Time.time - _lastDebugLogTime > DEBUG_LOG_INTERVAL;
            if (shouldLog || (debugIconRendering && _lastDebugLogTime == 0))
            {
                _lastDebugLogTime = Time.time;
                int totalIcons = 0;
                foreach (var b in _factionBatches.Values) totalIcons += b.matrices.Count;
                totalIcons += _deadBatch?.matrices.Count ?? 0;
                totalIcons += _defaultBatch?.matrices.Count ?? 0;
                foreach (var b in _externalBatches.Values) totalIcons += b.matrices.Count;
                
                Debug.Log($"[RTSMinimapController] Icon Update: {agentCount} total agents, {externalCount} externals, {registeredCount} registered, {processedCount} in bounds, {outOfBoundsCount} out of bounds, {totalIcons} icons queued\n" +
                    $"  Bounds: X[{minX:F1} to {maxX:F1}] Z[{minZ:F1} to {maxZ:F1}]\n" +
                    $"  First agent pos: {firstAgentPos}\n" +
                    $"  Faction batches: {_factionBatches.Count}, external batches: {_externalBatches.Count}");
            }
        }
        
        /// <summary>
        /// Updates icon GameObjects (URP-compatible fallback). Uses a pool of simple GameObjects
        /// placed on the icon layer so they render reliably in URP.
        /// </summary>
        private void UpdateGameObjectIcons()
        {
            if (_iconMesh == null) return;
            
            // Get agents safely
            List<CrowdAgent> agents = null;
            if (hideNonPlayerFactions) CachePlayerFactions();
            if (requireFogRevealForNonPlayers && _fog == null) CacheFogProvider();

            if (crowdController != null)
            {
                try
                {
                    agents = crowdController.GetAgents();
                }
                catch (System.Exception e)
                {
                    if (debugIconRendering) Debug.LogWarning($"[RTSMinimapController] Exception getting agents: {e.Message}");
                }
            }

            IReadOnlyList<IRTSExternalSelectable> externals = GetExternalSelectables();
            int externalCount = externals != null ? externals.Count : 0;
            IReadOnlyCollection<IRTSMinimapIcon> registeredIcons = GetRegisteredMinimapIcons();
            int registeredCount = registeredIcons != null ? registeredIcons.Count : 0;
            
            int agentCount = agents != null ? agents.Count : 0;
            if (agentCount == 0 && externalCount == 0 && registeredCount == 0)
            {
                // Hide all icons
                for (int i = 0; i < _activeIconCount; i++)
                {
                    if (i < _iconPool.Count && _iconPool[i] != null)
                        _iconPool[i].SetActive(false);
                }
                _activeIconCount = 0;
                return;
            }
            
            int totalCount = agentCount + externalCount + registeredCount;
            int limit = maxIcons > 0 ? Mathf.Min(totalCount, maxIcons) : totalCount;
            int iconIndex = 0;
            
            // Pre-calculate bounds
            float halfX = worldSize.x / 2f;
            float halfZ = worldSize.y / 2f;
            float minX = worldCenter.x - halfX;
            float maxX = worldCenter.x + halfX;
            float minZ = worldCenter.y - halfZ;
            float maxZ = worldCenter.y + halfZ;
            
            // Icon Y position - just below the camera
            float iconY = cameraHeight - 2f;
            
            for (int i = 0; i < agentCount && iconIndex < limit; i++)
            {
                // Get agent safely
                CrowdAgent agent;
                try
                {
                    agent = agents[i];
                    if (agent == null) continue;
                }
                catch (System.Exception)
                {
                    continue;
                }
                
                // Check dead state
                bool isDead;
                try
                {
                    isDead = agent.isDead;
                }
                catch (System.Exception)
                {
                    continue;
                }
                
                if (isDead && !showDeadAgents) continue;
                
                // Get position safely
                Vector3 agentPos;
                try
                {
                    if (agent.navigationAgent != null || agent.crowdInstance != null)
                    {
                        agentPos = agent.WorldPosition;
                    }
                    else
                    {
                        continue;
                    }
                }
                catch (System.Exception)
                {
                    continue;
                }
                
                // Bounds check
                if (agentPos.x < minX || agentPos.x > maxX || agentPos.z < minZ || agentPos.z > maxZ)
                {
                    continue;
                }

                // Faction filter (hide non-player factions, and optionally require fog reveal)
                bool isPlayerFaction = false;
                string factionName = null;
                if (hideNonPlayerFactions)
                {
                    try { factionName = agent.enemyCrowd?.factionName; } catch (System.Exception) { }
                    isPlayerFaction = _playerFactions.Count == 0 || (!string.IsNullOrEmpty(factionName) && _playerFactions.Contains(factionName));
                    if (!isPlayerFaction)
                    {
                        if (!requireFogRevealForNonPlayers)
                        {
                            continue;
                        }
                        if (_fog == null || !_fog.IsActivelyVisible(agentPos))
                        {
                            continue;
                        }
                    }
                }
                
                // Get or create pooled icon
                var iconObj = GetPooledIcon(iconIndex);
                
                // Position the icon
                iconObj.transform.position = new Vector3(agentPos.x, iconY, agentPos.z);
                iconObj.transform.localScale = Vector3.one * iconSize;
                
                // Set material based on faction/dead state
                int factionIndex = -1;
                EnemyCrowd crowd = null;
                try
                {
                    factionIndex = agent.enemyCrowdIndex;
                    crowd = agent.enemyCrowd;
                }
                catch (System.Exception) { }
                
                var material = GetFactionMaterial(factionIndex, crowd, isDead);
                if (iconIndex < _iconRenderers.Count && _iconRenderers[iconIndex] != null)
                {
                    _iconRenderers[iconIndex].sharedMaterial = material;
                }
                
                iconObj.SetActive(true);
                iconIndex++;
            }

            for (int i = 0; i < externalCount && iconIndex < limit; i++)
            {
                IRTSExternalSelectable external = externals[i];
                if (external == null) continue;

                bool isAlive;
                try
                {
                    isAlive = external.IsAlive;
                }
                catch (System.Exception)
                {
                    continue;
                }

                if (!isAlive) continue;
                var externalIcon = external as IRTSMinimapIcon;
                if (externalIcon == null) continue;
                if (!externalIcon.ShowOnMinimap) continue;

                Vector3 externalPos;
                try
                {
                    externalPos = externalIcon.MinimapPosition;
                }
                catch (System.Exception)
                {
                    continue;
                }

                if (externalPos.x < minX || externalPos.x > maxX || externalPos.z < minZ || externalPos.z > maxZ)
                {
                    continue;
                }

                var iconObj = GetPooledIcon(iconIndex);
                iconObj.transform.position = new Vector3(externalPos.x, iconY, externalPos.z);
                iconObj.transform.localScale = Vector3.one * iconSize;

                var batch = GetOrCreateExternalBatch(externalIcon.MinimapColor);
                var material = batch?.material ?? _instancedMaterial;
                if (iconIndex < _iconRenderers.Count && _iconRenderers[iconIndex] != null)
                {
                    _iconRenderers[iconIndex].sharedMaterial = material;
                }

                iconObj.SetActive(true);
                iconIndex++;
            }

            if (registeredCount > 0)
            {
                foreach (var icon in registeredIcons)
                {
                    if (iconIndex >= limit) break;
                    if (icon == null) continue;
                    if (!icon.ShowOnMinimap) continue;

                    var externalSelectable = icon as IRTSExternalSelectable;
                    if (externalSelectable != null && ContainsExternal(externals, externalSelectable)) continue;

                    Vector3 iconPos;
                    try
                    {
                        iconPos = icon.MinimapPosition;
                    }
                    catch (System.Exception)
                    {
                        continue;
                    }

                    if (iconPos.x < minX || iconPos.x > maxX || iconPos.z < minZ || iconPos.z > maxZ)
                    {
                        continue;
                    }

                    var iconObj = GetPooledIcon(iconIndex);
                    iconObj.transform.position = new Vector3(iconPos.x, iconY, iconPos.z);
                    iconObj.transform.localScale = Vector3.one * iconSize;

                    var batch = GetOrCreateExternalBatch(icon.MinimapColor);
                    var material = batch?.material ?? _instancedMaterial;
                    if (iconIndex < _iconRenderers.Count && _iconRenderers[iconIndex] != null)
                    {
                        _iconRenderers[iconIndex].sharedMaterial = material;
                    }

                    iconObj.SetActive(true);
                    iconIndex++;
                }
            }
            
            // Hide unused icons
            for (int i = iconIndex; i < _activeIconCount; i++)
            {
                if (i < _iconPool.Count && _iconPool[i] != null)
                    _iconPool[i].SetActive(false);
            }
            
            _activeIconCount = iconIndex;
            
            // Debug logging
            // Debug logging removed to reduce log noise
        }
        
        /// <summary>
        /// Renders all agent icons using GPU instancing via CommandBuffer. Called every frame.
        /// </summary>
        private void RenderAgentIcons()
        {
            if (_iconMesh == null || _iconCommandBuffer == null) return;
            
            // Clear previous frame's commands
            _iconCommandBuffer.Clear();
            
            // Render dead agents first (so living agents render on top)
            RenderBatchToCommandBuffer(_deadBatch);
            
            // Render default batch
            RenderBatchToCommandBuffer(_defaultBatch);
            
            // Render each faction batch
            foreach (var batch in _factionBatches.Values)
            {
                RenderBatchToCommandBuffer(batch);
            }

            foreach (var batch in _externalBatches.Values)
            {
                RenderBatchToCommandBuffer(batch);
            }
        }
        
        private void RenderBatchToCommandBuffer(FactionBatch batch)
        {
            if (batch == null || batch.matrices.Count == 0 || batch.material == null || _iconCommandBuffer == null) return;
            
            int count = batch.matrices.Count;
            
            // Ensure we have a properly sized array
            if (batch.matrixArray == null || batch.matrixArray.Length < count)
            {
                // Round up to nearest power of 2 for efficiency, but cap at MAX_INSTANCES_PER_BATCH
                int newSize = Mathf.Min(Mathf.NextPowerOfTwo(count), MAX_INSTANCES_PER_BATCH);
                batch.matrixArray = new Matrix4x4[newSize];
            }
            
            // Process in chunks of MAX_INSTANCES_PER_BATCH
            int offset = 0;
            while (offset < count)
            {
                int batchSize = Mathf.Min(count - offset, MAX_INSTANCES_PER_BATCH);
                
                // Copy this chunk
                for (int i = 0; i < batchSize; i++)
                {
                    batch.matrixArray[i] = batch.matrices[offset + i];
                }
                
                // Add draw command to the command buffer (renders ONLY to minimap camera)
                _iconCommandBuffer.DrawMeshInstanced(
                    _iconMesh,
                    0, // submesh index
                    batch.material,
                    0, // shader pass
                    batch.matrixArray,
                    batchSize
                );
                
                offset += batchSize;
            }
        }

        #endregion

        #region Public API
        
        /// <summary>
        /// Set the world area covered by the minimap
        /// </summary>
        public void SetWorldBounds(Vector2 center, Vector2 size)
        {
            worldCenter = center;
            worldSize = size;
            
            // Update camera
            _minimapCamera.orthographicSize = worldSize.y / 2f;
            transform.position = new Vector3(worldCenter.x, cameraHeight, worldCenter.y);
        }
        
        /// <summary>
        /// Set world bounds from terrain
        /// </summary>
        public void SetBoundsFromTerrain()
        {
            Terrain terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                Vector3 terrainPos = terrain.transform.position;
                Vector3 terrainSize = terrain.terrainData.size;
                
                worldCenter = new Vector2(
                    terrainPos.x + terrainSize.x / 2f,
                    terrainPos.z + terrainSize.z / 2f
                );
                worldSize = new Vector2(terrainSize.x, terrainSize.z);
                
                SetWorldBounds(worldCenter, worldSize);
            }
        }
        
        /// <summary>
        /// Auto-fit the minimap bounds to encompass all current agents with padding
        /// </summary>
        public void FitBoundsToAgents(float padding = 20f)
        {
            if (crowdController == null) return;
            
            var agents = crowdController.GetAgents();
            if (agents == null || agents.Count == 0) return;
            
            // Find min/max bounds
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            int validAgents = 0;
            
            foreach (var agent in agents)
            {
                if (agent == null) continue;
                
                Vector3 pos = Vector3.zero;
                if (agent.navigationAgent != null || agent.crowdInstance != null)
                    pos = agent.WorldPosition;
                else
                    continue;
                
                minX = Mathf.Min(minX, pos.x);
                maxX = Mathf.Max(maxX, pos.x);
                minZ = Mathf.Min(minZ, pos.z);
                maxZ = Mathf.Max(maxZ, pos.z);
                validAgents++;
            }
            
            if (validAgents == 0) return;
            
            // Calculate center and size with padding
            float centerX = (minX + maxX) / 2f;
            float centerZ = (minZ + maxZ) / 2f;
            float sizeX = Mathf.Max(maxX - minX + padding * 2f, padding * 2f);
            float sizeZ = Mathf.Max(maxZ - minZ + padding * 2f, padding * 2f);
            
            // Make it square (use larger dimension)
            float maxSize = Mathf.Max(sizeX, sizeZ);
            
            SetWorldBounds(new Vector2(centerX, centerZ), new Vector2(maxSize, maxSize));
            
            Debug.Log($"[RTSMinimapController] Fit bounds to {validAgents} agents. Center: ({centerX}, {centerZ}), Size: {maxSize}");
        }
        
        /// <summary>
        /// Convert a world position to minimap UV coordinates (0-1)
        /// </summary>
        public Vector2 WorldToMinimapUV(Vector3 worldPos)
        {
            return new Vector2(
                (worldPos.x - worldCenter.x) / worldSize.x + 0.5f,
                (worldPos.z - worldCenter.y) / worldSize.y + 0.5f
            );
        }
        
        /// <summary>
        /// Convert minimap UV coordinates to world position
        /// </summary>
        public Vector3 MinimapUVToWorld(Vector2 uv)
        {
            return new Vector3(
                worldCenter.x + (uv.x - 0.5f) * worldSize.x,
                0f,
                worldCenter.y + (uv.y - 0.5f) * worldSize.y
            );
        }
        
        /// <summary>
        /// Clear cached faction materials (call if faction colors change at runtime)
        /// </summary>
        public void RefreshFactionColors()
        {
            // Clean up existing faction batches
            foreach (var batch in _factionBatches.Values)
            {
                if (batch.material != null) Destroy(batch.material);
            }
            _factionBatches.Clear();

            foreach (var batch in _externalBatches.Values)
            {
                if (batch.material != null) Destroy(batch.material);
            }
            _externalBatches.Clear();
        }
        
        /// <summary>
        /// Set the icon shape at runtime
        /// </summary>
        public void SetIconShape(IconShape shape)
        {
            if (iconShape == shape) return;
            
            iconShape = shape;
            
            // Recreate the mesh for GPU instancing
            if (_iconMesh != null)
            {
                Destroy(_iconMesh);
            }
            _iconMesh = shape == IconShape.Circle ? CreateIconCircleMesh(16) : CreateIconSquareMesh();
        }
        
        /// <summary>
        /// Set the icon size at runtime
        /// </summary>
        public void SetIconSize(float size)
        {
            iconSize = Mathf.Max(0.1f, size);
        }
        
        /// <summary>
        /// Enable or disable agent icons
        /// </summary>
        public void SetAgentIconsEnabled(bool enabled)
        {
            showAgentIcons = enabled;
            
            if (!enabled)
            {
                // Clear all batches to stop rendering
                foreach (var batch in _factionBatches.Values)
                {
                    batch.matrices.Clear();
                }
                _deadBatch?.matrices.Clear();
                _defaultBatch?.matrices.Clear();
                foreach (var batch in _externalBatches.Values)
                {
                    batch.matrices.Clear();
                }
            }
        }
        
        /// <summary>
        /// Gets the number of currently visible agent icons
        /// </summary>
        public int VisibleIconCount
        {
            get
            {
                int count = 0;
                foreach (var batch in _factionBatches.Values)
                {
                    count += batch.matrices.Count;
                }
                if (_deadBatch != null) count += _deadBatch.matrices.Count;
                if (_defaultBatch != null) count += _defaultBatch.matrices.Count;
                foreach (var batch in _externalBatches.Values)
                {
                    count += batch.matrices.Count;
                }
                return count;
            }
        }
        
        /// <summary>
        /// Gets performance statistics for the icon rendering system
        /// </summary>
        public (int totalIcons, int factionBatches, int drawCalls) GetPerformanceStats()
        {
            int totalIcons = VisibleIconCount;
            int factionCount = _factionBatches.Count + 2 + _externalBatches.Count; // +2 for dead and default batches
            
            // Calculate draw calls (each batch may need multiple if >1023 icons)
            int drawCalls = 0;
            foreach (var batch in _factionBatches.Values)
            {
                drawCalls += Mathf.CeilToInt(batch.matrices.Count / (float)MAX_INSTANCES_PER_BATCH);
            }
            if (_deadBatch != null && _deadBatch.matrices.Count > 0)
                drawCalls += Mathf.CeilToInt(_deadBatch.matrices.Count / (float)MAX_INSTANCES_PER_BATCH);
            if (_defaultBatch != null && _defaultBatch.matrices.Count > 0)
                drawCalls += Mathf.CeilToInt(_defaultBatch.matrices.Count / (float)MAX_INSTANCES_PER_BATCH);
            foreach (var batch in _externalBatches.Values)
            {
                drawCalls += Mathf.CeilToInt(batch.matrices.Count / (float)MAX_INSTANCES_PER_BATCH);
            }
            
            return (totalIcons, factionCount, drawCalls);
        }
        
        #endregion
        
        #region Static Background
        
        /// <summary>
        /// Sets up the static background system
        /// </summary>
        private void SetupStaticBackground()
        {
            // Store original culling mask
            _originalCullingMask = _minimapCamera.cullingMask;
            
            // If we have a pre-assigned texture, use it immediately
            if (staticBackgroundTexture != null)
            {
                ApplyStaticBackground(staticBackgroundTexture);
            }
            else
            {
                // Capture the background after a delay using coroutine
                StartCoroutine(CaptureBackgroundCoroutine(backgroundCaptureDelay));
            }
        }
        
        private void CaptureBackgroundDelayed()
        {
            StartCoroutine(CaptureBackgroundCoroutine(0f));
        }
        
        private System.Collections.IEnumerator CaptureBackgroundCoroutine(float delay)
        {
            if (delay > 0f)
                yield return new WaitForSeconds(delay);
            
            // Store current state
            RenderTexture previousTarget = _minimapCamera.targetTexture;
            int previousCullingMask = _minimapCamera.cullingMask;
            
            // Build capture mask: start with original, exclude icons
            int captureMask = _originalCullingMask & ~(1 << iconLayer);
            
            // Also exclude VFX layers during capture
            captureMask = ExcludeVFXFromMask(captureMask);
            
            _minimapCamera.cullingMask = captureMask;
            
            // Create a temporary render texture for capture
            int resolution = backgroundCaptureResolution;
            RenderTexture captureRT = RenderTexture.GetTemporary(resolution, resolution, 24, RenderTextureFormat.ARGB32);
            _minimapCamera.targetTexture = captureRT;
            
            // Wait for the camera to render naturally at end of frame
            // This avoids conflicts with URP's RenderGraph job system
            yield return new WaitForEndOfFrame();
            
            Debug.Log("[RTSMinimapController] Capturing static background (fog temporarily disabled)...");
            
            // Now read the pixels from what was rendered
            RenderTexture.active = captureRT;
            
            if (_capturedBackground != null)
            {
                Destroy(_capturedBackground);
            }
            _capturedBackground = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
            _capturedBackground.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
            _capturedBackground.Apply();
            _capturedBackground.name = "MinimapBackground_Captured";
            
            // Restore camera state
            RenderTexture.active = null;
            _minimapCamera.targetTexture = previousTarget;
            _minimapCamera.cullingMask = previousCullingMask;
            RenderTexture.ReleaseTemporary(captureRT);
            
            // Apply the captured background
            ApplyStaticBackground(_capturedBackground);
            
            // Update refresh timestamp
            _lastBackgroundRefreshTime = Time.time;
            
            Debug.Log($"[RTSMinimapController] Background captured at {resolution}x{resolution}");
        }
        
        /// <summary>
        /// Captures the current terrain/world as a static background texture.
        /// Call this manually if you want to refresh the background at runtime.
        /// </summary>
        public void CaptureBackground()
        {
            StartCoroutine(CaptureBackgroundCoroutine(0f));
        }
        
        /// <summary>
        /// Applies a texture as the static background
        /// </summary>
        private void ApplyStaticBackground(Texture2D texture)
        {
            if (texture == null) return;
            
            // Create background quad if it doesn't exist
            if (_backgroundQuad == null)
            {
                _backgroundQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _backgroundQuad.name = "MinimapBackground";
                _backgroundQuad.transform.SetParent(transform);
                
                // Remove collider
                var collider = _backgroundQuad.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                
                // Position the quad in front of the camera (in local space, Z is forward for a downward-facing camera)
                // Place it near the far clip plane so it's behind everything else
                _backgroundQuad.transform.localPosition = new Vector3(0f, 0f, cameraHeight - 1f);
                _backgroundQuad.transform.localRotation = Quaternion.identity; // Face the camera (no rotation needed)
                
                // Set layer so only minimap camera sees it
                _backgroundQuad.layer = iconLayer;
            }
            
            // Scale to match world size
            _backgroundQuad.transform.localScale = new Vector3(worldSize.x, worldSize.y, 1f);
            
            // Create material with the texture
            var renderer = _backgroundQuad.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                // Use unlit shader for performance
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Texture");
                if (shader == null) shader = Shader.Find("UI/Default");
                
                var mat = new Material(shader);
                mat.mainTexture = texture;
                
                // Set properties for URP
                if (mat.HasProperty("_BaseMap"))
                {
                    mat.SetTexture("_BaseMap", texture);
                }
                
                renderer.sharedMaterial = mat;
            }
            
            // Disable terrain/world rendering - only render icons on top of background
            // Keep ONLY the icon layer visible - this prevents VFX and other objects from rendering
            _minimapCamera.cullingMask = (1 << iconLayer);
            // View indicator is now on iconLayer, so no need for additional mask
            
            _backgroundCaptured = true;
            Debug.Log("[RTSMinimapController] Static background applied. Camera now only renders icons over background.");
        }
        
        /// <summary>
        /// Disables static background and returns to full terrain rendering
        /// </summary>
        public void DisableStaticBackground()
        {
            if (_backgroundQuad != null)
            {
                Destroy(_backgroundQuad);
                _backgroundQuad = null;
            }
            
            // Restore original culling mask
            _minimapCamera.cullingMask = _originalCullingMask;
            _backgroundCaptured = false;
            useStaticBackground = false;
            
            Debug.Log("[RTSMinimapController] Static background disabled. Camera now renders full scene.");
        }
        
        /// <summary>
        /// Enables static background mode and captures current terrain
        /// </summary>
        public void EnableStaticBackground()
        {
            useStaticBackground = true;
            _originalCullingMask = _minimapCamera.cullingMask;
            CaptureBackground();
        }
        
        /// <summary>
        /// Refreshes the static background by taking a new screenshot of the terrain.
        /// Call this when the map/terrain has changed and the minimap needs to be updated.
        /// This is the PRIMARY method to call when you need to update the minimap background.
        /// </summary>
        public void RefreshBackground()
        {
            if (!useStaticBackground)
            {
                Debug.LogWarning("[RTSMinimapController] RefreshBackground called but useStaticBackground is false. Enable static background first or call EnableStaticBackground().");
                return;
            }
            
            // Temporarily restore original culling mask to capture the full scene
            int currentMask = _minimapCamera.cullingMask;
            _minimapCamera.cullingMask = _originalCullingMask & ~(1 << iconLayer);
            
            // Re-capture
            CaptureBackground();
            
            // Update timestamp
            _lastBackgroundRefreshTime = Time.time;
        }
        
        /// <summary>
        /// Sets the auto-refresh interval for the static background.
        /// Set to 0 to disable auto-refresh.
        /// </summary>
        /// <param name="intervalSeconds">Interval in seconds between automatic refreshes. 0 = disabled.</param>
        public void SetBackgroundRefreshInterval(float intervalSeconds)
        {
            backgroundRefreshInterval = Mathf.Max(0f, intervalSeconds);
            _lastBackgroundRefreshTime = Time.time; // Reset timer
        }
        
        /// <summary>
        /// Gets the current auto-refresh interval
        /// </summary>
        public float BackgroundRefreshInterval => backgroundRefreshInterval;
        
        /// <summary>
        /// Whether the static background is currently active
        /// </summary>
        public bool IsStaticBackgroundActive => _backgroundCaptured;
        
        /// <summary>
        /// Gets the captured background texture (can be saved to disk in editor)
        /// </summary>
        public Texture2D CapturedBackgroundTexture => _capturedBackground;
        
        #endregion
        
        #region Editor Helpers
        
        #if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Draw world bounds
            Gizmos.color = Color.cyan;
            Vector3 center = new Vector3(worldCenter.x, 0f, worldCenter.y);
            Vector3 size = new Vector3(worldSize.x, 1f, worldSize.y);
            Gizmos.DrawWireCube(center, size);
        }
        
        private void OnValidate()
        {
            // Update camera settings when values change in editor
            if (_minimapCamera != null)
            {
                _minimapCamera.orthographicSize = worldSize.y / 2f;
                transform.position = new Vector3(worldCenter.x, cameraHeight, worldCenter.y);
            }
        }
        
        /// <summary>
        /// [Editor Only] Captures and saves the minimap background to a PNG file.
        /// Call from a context menu or custom inspector button.
        /// </summary>
        [ContextMenu("Capture and Save Background")]
        public void CaptureAndSaveBackground()
        {
            // Temporarily enable if needed
            bool wasDisabled = !enabled;
            if (wasDisabled)
            {
                enabled = true;
                _minimapCamera = GetComponent<Camera>();
            }
            
            // Store original culling mask if not yet stored
            if (_originalCullingMask == 0)
            {
                _originalCullingMask = _minimapCamera.cullingMask;
            }
            
            // Capture
            CaptureBackground();
            
            if (_capturedBackground != null)
            {
                // Save to PNG
                byte[] bytes = _capturedBackground.EncodeToPNG();
                string path = UnityEditor.EditorUtility.SaveFilePanel(
                    "Save Minimap Background",
                    "Assets",
                    "MinimapBackground",
                    "png"
                );
                
                if (!string.IsNullOrEmpty(path))
                {
                    System.IO.File.WriteAllBytes(path, bytes);
                    Debug.Log($"[RTSMinimapController] Background saved to: {path}");
                    
                    // Refresh asset database if saved in Assets folder
                    if (path.StartsWith(Application.dataPath))
                    {
                        UnityEditor.AssetDatabase.Refresh();
                    }
                }
            }
            
            if (wasDisabled)
            {
                enabled = false;
            }
        }
        #endif
        
        #endregion
    }
}
