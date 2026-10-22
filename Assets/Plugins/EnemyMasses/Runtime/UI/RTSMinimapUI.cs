using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Arawn.EnemyMasses.Runtime.UI
{
    /// <summary>
    /// Camera-free minimap that renders agent markers as UI elements.
    /// Works with any render pipeline (Built-in, URP, HDRP).
    ///
    /// Reads data from an <see cref="IMinimapDataProvider"/> and renders markers
    /// using a pooled set of UI <see cref="Image"/> children.
    ///
    /// Replaces the camera-based <c>RTSMinimapController</c> for projects
    /// where a dedicated camera is too expensive or incompatible.
    /// </summary>
    [AddComponentMenu("Enemy Masses/UI/Minimap UI")]
    [RequireComponent(typeof(RectTransform))]
    public class RTSMinimapUI : MonoBehaviour, IPointerClickHandler
    {
        #region Inspector ─ Data

        [Header("Data Provider")]
        [Tooltip("MonoBehaviour implementing IMinimapDataProvider. Auto-discovered if null.")]
        [SerializeField] private MonoBehaviour dataProviderSource;

        #endregion

        #region Inspector ─ World Bounds

        [Header("World Bounds")]
        [Tooltip("Center of the world area displayed on the minimap (X = world X, Y = world Z).")]
        [SerializeField] private Vector2 worldCenter = Vector2.zero;

        [Tooltip("Size of the world area displayed on the minimap (X = world width, Y = world depth).")]
        [SerializeField] private Vector2 worldSize = new Vector2(200f, 200f);

        [Tooltip("Automatically update world bounds from the data provider.")]
        [SerializeField] private bool autoSyncBounds = false;

        [Tooltip("Padding (in world units) added around auto-synced bounds.")]
        [SerializeField] private float autoSyncPadding = 20f;

        #endregion

        #region Inspector ─ Background

        [Header("Background")]
        [Tooltip("Static background image (e.g., a top-down terrain screenshot or runtime capture).")]
        [SerializeField] private RawImage backgroundImage;

        [Tooltip("Background tint color.")]
        [SerializeField] private Color backgroundColor = new Color(0.08f, 0.12f, 0.08f, 1f);

        [Tooltip("Automatically capture a top-down terrain screenshot on Start.")]
        [SerializeField] private bool captureBackgroundOnStart = true;

        [Tooltip("Resolution of the captured background texture.")]
        [SerializeField] private int captureResolution = 1024;

        [Tooltip("Delay (seconds) before capturing to let the scene load.")]
        [SerializeField] private float captureDelay = 0.5f;

        [Tooltip("Camera culling mask for background capture. -1 = Everything except UI.")]
        [SerializeField] private LayerMask captureCullingMask = ~(1 << 5); // Everything except UI

        #endregion

        #region Inspector ─ Fog Overlay

        [Header("Fog of War Overlay")]
        [Tooltip("Show a fog of war overlay on the minimap.")]
        [SerializeField] private bool showFogOverlay = true;

        [Tooltip("RawImage used to display the fog texture. Auto-created if null.")]
        [SerializeField] private RawImage fogOverlayImage;

        [Tooltip("MonoBehaviour implementing IMinimapFogProvider to read the fog texture from. Auto-discovered if null.")]
        [SerializeField] private MonoBehaviour fogOfWarSource;

        [Tooltip("How often to refresh the fog overlay (seconds). 0 = every frame.")]
        [SerializeField] private float fogRefreshInterval = 0.1f;

        #endregion

        #region Inspector ─ Markers

        [Header("Marker Appearance")]
        [Tooltip("Default marker sprite. If null, uses Unity's built-in white square.")]
        [SerializeField] private Sprite markerSprite;

        [Tooltip("Marker size in pixels.")]
        [SerializeField] private Vector2 markerSize = new Vector2(4f, 4f);

        [Tooltip("Selected marker size in pixels.")]
        [SerializeField] private Vector2 selectedMarkerSize = new Vector2(6f, 6f);

        [Tooltip("Maximum markers to display. Prevents excessive UI elements.")]
        [SerializeField] private int maxMarkers = 2048;

        #endregion

        #region Inspector ─ View Indicator

        [Header("View Indicator")]
        [Tooltip("Show a rectangle indicating the main camera's view on the minimap.")]
        [SerializeField] private bool showViewIndicator = true;

        [Tooltip("RectTransform used as the view indicator overlay. Created automatically if null.")]
        [SerializeField] private RectTransform viewIndicatorRect;

        [Tooltip("Color of the view indicator border.")]
        [SerializeField] private Color viewIndicatorColor = new Color(1f, 1f, 1f, 0.5f);

        [Tooltip("Use a fixed size for the indicator instead of computing from the camera frustum.")]
        [SerializeField] private bool lockViewIndicatorSize = false;

        [Tooltip("Fixed size in pixels when lockViewIndicatorSize is enabled.")]
        [SerializeField] private Vector2 lockedViewIndicatorSize = new Vector2(24f, 24f);

        [Tooltip("Maximum view indicator size in pixels. Prevents the indicator from covering the entire minimap when zoomed out.")]
        [SerializeField] private float maxViewIndicatorSize = 180f;

        #endregion

        #region Inspector ─ Interaction

        [Header("Interaction")]
        [Tooltip("Left-click on minimap moves the camera to that world position.")]
        [SerializeField] private bool leftClickMovesCamera = true;

        [Tooltip("Right-click on minimap issues a move command for selected agents.")]
        [SerializeField] private bool rightClickMovesAgents = true;

        [Tooltip("Reference to RTSCameraController for camera focus. Auto-discovered if null.")]
        [SerializeField] private RTSCameraController cameraController;

        [Tooltip("Reference to EnemyMassesRTSController for move commands. Auto-discovered if null.")]
        [SerializeField] private EnemyMassesRTSController rtsController;

        #endregion

        #region Inspector ─ Performance

        [Header("Performance")]
        [Tooltip("How often to refresh marker positions (seconds). 0 = every frame.")]
        [SerializeField] private float refreshInterval = 0f;

        #endregion

        #region Inspector ─ Alerts

        [Header("Under-Attack Alerts")]
        [Tooltip("Enable AoE2-style marker flashing when player units are attacked.")]
        [SerializeField] private bool enableAlertFlash = true;

        [Tooltip("MinimapAlertSystem component. Auto-discovered from the data provider if null.")]
        [SerializeField] private MinimapAlertSystem alertSystem;

        #endregion

        #region Properties

        /// <summary>The data provider driving this minimap.</summary>
        public IMinimapDataProvider DataProvider
        {
            get => _dataProvider;
            set
            {
                if (_dataProvider != null)
                    _dataProvider.OnMarkersChanged -= OnMarkersChanged;
                _dataProvider = value;
                if (_dataProvider != null)
                    _dataProvider.OnMarkersChanged += OnMarkersChanged;
            }
        }

        /// <summary>World center of the displayed area.</summary>
        public Vector2 WorldCenter
        {
            get => worldCenter;
            set => worldCenter = value;
        }

        /// <summary>World size of the displayed area.</summary>
        public Vector2 WorldSize
        {
            get => worldSize;
            set => worldSize = value;
        }

        #endregion

        #region Public API

        /// <summary>Convert a world position to minimap UV coordinates [0,1].</summary>
        public Vector2 WorldToMinimapUV(Vector3 worldPos)
        {
            return new Vector2(
                (worldPos.x - worldCenter.x) / worldSize.x + 0.5f,
                (worldPos.z - worldCenter.y) / worldSize.y + 0.5f
            );
        }

        /// <summary>Convert minimap UV [0,1] to a world position (Y=0).</summary>
        public Vector3 MinimapUVToWorld(Vector2 uv)
        {
            return new Vector3(
                worldCenter.x + (uv.x - 0.5f) * worldSize.x,
                0f,
                worldCenter.y + (uv.y - 0.5f) * worldSize.y
            );
        }

        /// <summary>Convert a screen position over the minimap to a world position.</summary>
        public Vector3 ScreenToMinimapWorldPosition(Vector2 screenPos)
        {
            if (_rectTransform == null) return Vector3.zero;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rectTransform, screenPos, null, out Vector2 localPoint);

            Rect rect = _rectTransform.rect;
            Vector2 normalizedPos = new Vector2(
                (localPoint.x - rect.x) / rect.width,
                (localPoint.y - rect.y) / rect.height
            );

            return MinimapUVToWorld(normalizedPos);
        }

        /// <summary>Set the world bounds the minimap displays.</summary>
        public void SetWorldBounds(Vector2 center, Vector2 size)
        {
            worldCenter = center;
            worldSize = size;
        }

        /// <summary>Force an immediate marker refresh.</summary>
        public void ForceRefresh()
        {
            _dirty = true;
        }

        #endregion

        #region Unity Callbacks

        private IMinimapDataProvider _dataProvider;
        private RectTransform _rectTransform;
        private readonly List<Image> _markerPool = new List<Image>();
        private MinimapMarker[] _markerBuffer;
        private int _activeMarkerCount;
        private float _nextRefresh;
        private float _nextFogRefresh;
        private bool _dirty = true;
        private Camera _mainCamera;
        private Texture2D _capturedBackground;
        private IMinimapFogProvider _fogProvider;
        private Texture2D _fogRGBA; // RGBA32 copy of fog Alpha8 texture
        private Color32[] _fogPixelBuffer;
        private MinimapAlertSystem _alertSystem;
        private Image _viewIndicatorImage;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();

            // Discover data provider
            if (dataProviderSource != null)
                _dataProvider = dataProviderSource as IMinimapDataProvider;

            if (_dataProvider == null)
            {
                var providerMB = FindAnyObjectByType<EnemyMassesMinimapDataProvider>();
                if (providerMB != null)
                {
                    dataProviderSource = providerMB;
                    _dataProvider = providerMB;
                }
            }

            // Discover controllers
            if (cameraController == null)
                cameraController = FindAnyObjectByType<RTSCameraController>();
            if (rtsController == null)
                rtsController = FindAnyObjectByType<EnemyMassesRTSController>();

            _mainCamera = Camera.main;

            // Subscribe
            if (_dataProvider != null)
                _dataProvider.OnMarkersChanged += OnMarkersChanged;

            // Allocate buffer
            _markerBuffer = new MinimapMarker[maxMarkers];

            // Discover alert system
            if (enableAlertFlash)
                DiscoverAlertSystem();

            // Background tint
            if (backgroundImage != null)
                backgroundImage.color = backgroundColor;

            // Create view indicator if needed
            if (showViewIndicator && viewIndicatorRect == null)
                CreateViewIndicator();

            // Fog overlay: attempt initial discovery (will retry lazily in LateUpdate if not found)
            if (showFogOverlay)
                DiscoverFogProvider();

            // Background capture
            if (captureBackgroundOnStart && backgroundImage != null)
                StartCoroutine(CaptureBackgroundCoroutine());
        }

        private void OnDestroy()
        {
            if (_dataProvider != null)
                _dataProvider.OnMarkersChanged -= OnMarkersChanged;

            if (_capturedBackground != null)
                Destroy(_capturedBackground);

            if (_fogRGBA != null)
                Destroy(_fogRGBA);
        }

        private void LateUpdate()
        {
            if (_dataProvider == null) return;

            // Bounds sync
            if (autoSyncBounds)
            {
                var bounds = _dataProvider.WorldBounds;
                if (bounds.size.sqrMagnitude > 0f)
                {
                    worldCenter = new Vector2(bounds.center.x, bounds.center.z);
                    worldSize = new Vector2(
                        bounds.size.x + autoSyncPadding * 2f,
                        bounds.size.z + autoSyncPadding * 2f
                    );
                }
            }

            // Throttled refresh
            bool timeToRefresh = refreshInterval <= 0f || Time.time >= _nextRefresh;
            if (timeToRefresh || _dirty)
            {
                _nextRefresh = Time.time + refreshInterval;
                _dirty = false;
                RefreshMarkers();
            }

            // Fog overlay (lazy discovery — retries until provider is found)
            if (showFogOverlay)
            {
                // Re-attempt discovery if we don't have a provider yet
                if (_fogProvider == null)
                    DiscoverFogProvider();

                // Create the overlay RawImage once we have a provider
                if (_fogProvider != null && fogOverlayImage == null)
                    CreateFogOverlay();

                if (_fogProvider != null && fogOverlayImage != null)
                {
                    bool fogTimeToRefresh = fogRefreshInterval <= 0f || Time.time >= _nextFogRefresh;
                    if (fogTimeToRefresh)
                    {
                        _nextFogRefresh = Time.time + fogRefreshInterval;
                        UpdateFogOverlay();
                    }
                }
            }

            // View indicator
            if (showViewIndicator)
                UpdateViewIndicator();

            // Handle continuous click (drag on minimap)
            HandleContinuousInput();
        }

        #endregion

        #region IPointerClickHandler

        /// <summary>Handles click events routed through Unity's EventSystem.</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && leftClickMovesCamera)
            {
                Vector3 worldPos = ScreenToMinimapWorldPosition(eventData.position);
                if (cameraController != null)
                    cameraController.FocusOnPosition(worldPos);
            }
            else if (eventData.button == PointerEventData.InputButton.Right && rightClickMovesAgents)
            {
                Vector3 worldPos = ScreenToMinimapWorldPosition(eventData.position);
                if (rtsController != null && rtsController.SelectedCount > 0)
                {
                    rtsController.MoveSelectedAgentsTo(worldPos);
                    rtsController.PlayCommandSound(rtsController.SelectedCount);
                }
            }
        }

        #endregion

        #region Continuous Input (Drag)

        private void HandleContinuousInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current == null) return;
            if (!Mouse.current.leftButton.isPressed && !Mouse.current.rightButton.isPressed) return;

            Vector2 mousePos = Mouse.current.position.ReadValue();
            if (!RectTransformUtility.RectangleContainsScreenPoint(_rectTransform, mousePos)) return;

            if (Mouse.current.leftButton.isPressed && leftClickMovesCamera)
            {
                Vector3 worldPos = ScreenToMinimapWorldPosition(mousePos);
                if (cameraController != null)
                    cameraController.FocusOnPosition(worldPos);
            }
#else
            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1)) return;

            Vector2 mousePos = Input.mousePosition;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_rectTransform, mousePos)) return;

            if (Input.GetMouseButton(0) && leftClickMovesCamera)
            {
                Vector3 worldPos = ScreenToMinimapWorldPosition(mousePos);
                if (cameraController != null)
                    cameraController.FocusOnPosition(worldPos);
            }
#endif
        }

        #endregion

        #region Marker Rendering

        private void RefreshMarkers()
        {
            int count = _dataProvider.GetMarkers(_markerBuffer);
            _activeMarkerCount = count;

            // Ensure pool has enough markers
            EnsurePoolSize(count);

            Rect rect = _rectTransform.rect;

            for (int i = 0; i < count; i++)
            {
                ref var marker = ref _markerBuffer[i];
                var img = _markerPool[i];

                // Position
                Vector2 uv = WorldToMinimapUV(marker.Position);

                // Clamp to minimap bounds
                if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f)
                {
                    img.gameObject.SetActive(false);
                    continue;
                }

                img.gameObject.SetActive(true);

                var rt = img.rectTransform;
                rt.anchoredPosition = new Vector2(
                    uv.x * rect.width,
                    uv.y * rect.height
                );

                // Size & color
                Vector2 baseSize = marker.IsSelected ? selectedMarkerSize : markerSize;

                // Alert flash: blend color and scale up when under attack
                if (enableAlertFlash && marker.AlertIntensity > 0f && _alertSystem != null)
                {
                    img.color = Color.Lerp(marker.Color, _alertSystem.AlertColor, marker.AlertIntensity);
                    float scale = Mathf.Lerp(1f, _alertSystem.MarkerSizeMultiplier, marker.AlertIntensity);
                    rt.sizeDelta = baseSize * scale;
                    rt.SetAsLastSibling(); // Render flashing markers on top
                }
                else
                {
                    rt.sizeDelta = baseSize;
                    img.color = marker.Color;
                }
            }

            // Hide unused
            for (int i = count; i < _markerPool.Count; i++)
            {
                if (_markerPool[i].gameObject.activeSelf)
                    _markerPool[i].gameObject.SetActive(false);
            }
        }

        private void EnsurePoolSize(int needed)
        {
            while (_markerPool.Count < needed)
            {
                var go = new GameObject($"Marker_{_markerPool.Count}", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                go.layer = gameObject.layer;

                var img = go.GetComponent<Image>();
                img.sprite = markerSprite;
                img.raycastTarget = false;

                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = markerSize;

                go.SetActive(false);
                _markerPool.Add(img);
            }
        }

        #endregion

        #region View Indicator

        private void CreateViewIndicator()
        {
            var go = new GameObject("ViewIndicator", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);

            viewIndicatorRect = go.GetComponent<RectTransform>();
            viewIndicatorRect.anchorMin = Vector2.zero;
            viewIndicatorRect.anchorMax = Vector2.zero;
            viewIndicatorRect.pivot = new Vector2(0.5f, 0.5f);

            var img = go.GetComponent<Image>();
            img.color = viewIndicatorColor;
            img.raycastTarget = false;
            _viewIndicatorImage = img;

            // Make it a border (hollow rect) by using a very low fill center
            img.type = Image.Type.Sliced;
            img.fillCenter = false;
        }

        private void UpdateViewIndicator()
        {
            if (viewIndicatorRect == null || _mainCamera == null) return;

            if (lockViewIndicatorSize)
            {
                // Fixed size, positioned at camera target
                Vector3 camTarget = cameraController != null
                    ? cameraController.GetPosition()
                    : _mainCamera.transform.position;

                Vector2 uv = WorldToMinimapUV(camTarget);
                Rect rect = _rectTransform.rect;

                viewIndicatorRect.anchoredPosition = new Vector2(
                    uv.x * rect.width,
                    uv.y * rect.height
                );
                viewIndicatorRect.sizeDelta = lockedViewIndicatorSize;
            }
            else
            {
                // Compute from camera frustum → ground plane intersections
                Vector3[] corners = new Vector3[4];
                PopulateGroundCorners(corners);

                // Convert to UV
                float minU = float.MaxValue, maxU = float.MinValue;
                float minV = float.MaxValue, maxV = float.MinValue;

                for (int i = 0; i < 4; i++)
                {
                    Vector2 uv = WorldToMinimapUV(corners[i]);
                    if (uv.x < minU) minU = uv.x;
                    if (uv.x > maxU) maxU = uv.x;
                    if (uv.y < minV) minV = uv.y;
                    if (uv.y > maxV) maxV = uv.y;
                }

                // Clamp
                minU = Mathf.Clamp01(minU);
                maxU = Mathf.Clamp01(maxU);
                minV = Mathf.Clamp01(minV);
                maxV = Mathf.Clamp01(maxV);

                Rect rect = _rectTransform.rect;
                float cx = (minU + maxU) * 0.5f;
                float cy = (minV + maxV) * 0.5f;

                viewIndicatorRect.anchoredPosition = new Vector2(
                    cx * rect.width,
                    cy * rect.height
                );
                float indW = (maxU - minU) * rect.width;
                float indH = (maxV - minV) * rect.height;

                // Clamp to maximum size to prevent the indicator from covering the entire minimap
                if (maxViewIndicatorSize > 0f)
                {
                    indW = Mathf.Min(indW, maxViewIndicatorSize);
                    indH = Mathf.Min(indH, maxViewIndicatorSize);
                }

                viewIndicatorRect.sizeDelta = new Vector2(indW, indH);
            }

            // Flash view indicator when units are under attack
            if (_viewIndicatorImage == null)
                _viewIndicatorImage = viewIndicatorRect.GetComponent<Image>();
            if (_viewIndicatorImage != null)
            {
                if (enableAlertFlash && _alertSystem != null && _alertSystem.FlashViewIndicator && _alertSystem.HasActiveAlerts)
                {
                    float flash = Mathf.Abs(Mathf.Sin(Time.time * _alertSystem.FlashFrequency * Mathf.PI));
                    _viewIndicatorImage.color = Color.Lerp(viewIndicatorColor, _alertSystem.ViewIndicatorAlertColor, flash);
                }
                else
                {
                    _viewIndicatorImage.color = viewIndicatorColor;
                }
            }
        }

        private void PopulateGroundCorners(Vector3[] corners)
        {
            Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

            corners[0] = GetGroundIntersection(groundPlane, _mainCamera.ViewportPointToRay(new Vector3(0, 0, 0)));
            corners[1] = GetGroundIntersection(groundPlane, _mainCamera.ViewportPointToRay(new Vector3(1, 0, 0)));
            corners[2] = GetGroundIntersection(groundPlane, _mainCamera.ViewportPointToRay(new Vector3(1, 1, 0)));
            corners[3] = GetGroundIntersection(groundPlane, _mainCamera.ViewportPointToRay(new Vector3(0, 1, 0)));
        }

        private static Vector3 GetGroundIntersection(Plane plane, Ray ray)
        {
            if (plane.Raycast(ray, out float distance))
                return ray.GetPoint(distance);
            return ray.origin + ray.direction * 100f;
        }

        #endregion

        #region Background Capture

        /// <summary>
        /// Captures a one-shot top-down screenshot of the terrain and assigns it
        /// to the background RawImage. The temporary camera is destroyed after capture.
        /// </summary>
        public void CaptureBackground()
        {
            StartCoroutine(CaptureBackgroundCoroutine());
        }

        private IEnumerator CaptureBackgroundCoroutine()
        {
            if (backgroundImage == null) yield break;
            if (captureDelay > 0f)
                yield return new WaitForSeconds(captureDelay);

            int res = Mathf.Max(64, captureResolution);

            // Temporarily hide the fog-of-war plane so it doesn't get baked into the screenshot
            GameObject fogPlane = null;
            var fogPlaneTransform = FindFogPlane();
            if (fogPlaneTransform != null)
            {
                fogPlane = fogPlaneTransform.gameObject;
                fogPlane.SetActive(false);
            }

            // Create temporary orthographic camera
            var tempGO = new GameObject("[MinimapCapture_Temp]");
            var tempCam = tempGO.AddComponent<Camera>();
            tempCam.orthographic = true;
            tempCam.orthographicSize = worldSize.y * 0.5f;
            tempCam.aspect = worldSize.x / worldSize.y;
            tempCam.transform.position = new Vector3(worldCenter.x, 500f, worldCenter.y);
            tempCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            tempCam.cullingMask = captureCullingMask;
            tempCam.clearFlags = CameraClearFlags.SolidColor;
            tempCam.backgroundColor = backgroundColor;
            tempCam.enabled = false; // we render manually

            // Render to RT
            var rt = RenderTexture.GetTemporary(res, res, 24);
            tempCam.targetTexture = rt;
            tempCam.Render();

            // Read pixels
            RenderTexture.active = rt;
            if (_capturedBackground != null)
                Destroy(_capturedBackground);
            _capturedBackground = new Texture2D(res, res, TextureFormat.RGB24, false);
            _capturedBackground.ReadPixels(new UnityEngine.Rect(0, 0, res, res), 0, 0);
            _capturedBackground.Apply();

            // Cleanup
            RenderTexture.active = null;
            tempCam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(tempGO);

            // Re-enable the fog plane
            if (fogPlane != null)
                fogPlane.SetActive(true);

            // Assign
            backgroundImage.texture = _capturedBackground;
            backgroundImage.color = Color.white;
        }

        /// <summary>
        /// Finds the fog-of-war plane by its well-known name.
        /// This avoids needing a direct assembly reference to FogOfWarLite.
        /// </summary>
        private static Transform FindFogPlane()
        {
            // FogOfWarLite creates a child plane named "[FOW_Lite_Plane]"
            var go = GameObject.Find("[FOW_Lite_Plane]");
            return go != null ? go.transform : null;
        }

        #endregion

        #region Fog Overlay

        private void CreateFogOverlay()
        {
            var go = new GameObject("FogOverlay", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(transform, false);

            // Position directly above the background image, below markers
            if (backgroundImage != null)
                go.transform.SetSiblingIndex(backgroundImage.transform.GetSiblingIndex() + 1);
            else
                go.transform.SetSiblingIndex(0);

            var fogRect = go.GetComponent<RectTransform>();
            fogRect.anchorMin = Vector2.zero;
            fogRect.anchorMax = Vector2.one;
            fogRect.offsetMin = Vector2.zero;
            fogRect.offsetMax = Vector2.zero;

            fogOverlayImage = go.GetComponent<RawImage>();
            fogOverlayImage.color = Color.white;
            fogOverlayImage.raycastTarget = false;
        }

        private void DiscoverFogProvider()
        {
            if (fogOfWarSource != null)
            {
                _fogProvider = fogOfWarSource as IMinimapFogProvider;
                return;
            }

            var candidates = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mb in candidates)
            {
                if (mb is IMinimapFogProvider provider)
                {
                    fogOfWarSource = mb;
                    _fogProvider = provider;
                    break;
                }
            }
        }

        private void DiscoverAlertSystem()
        {
            if (alertSystem != null)
            {
                _alertSystem = alertSystem;
                return;
            }

            // Try to get from data provider first
            if (_dataProvider is EnemyMassesMinimapDataProvider emProvider && emProvider.AlertSystem != null)
            {
                _alertSystem = emProvider.AlertSystem;
                alertSystem = _alertSystem;
                return;
            }

            // Scene search fallback
            _alertSystem = FindAnyObjectByType<MinimapAlertSystem>();
            if (_alertSystem != null)
                alertSystem = _alertSystem;
        }

        private void UpdateFogOverlay()
        {
            if (_fogProvider == null || fogOverlayImage == null) return;
            if (!_fogProvider.HasInitializedBuffers) return;

            var srcTex = _fogProvider.FogTexture;
            if (srcTex == null) return;

            int w = srcTex.width;
            int h = srcTex.height;

            // Create or resize the RGBA32 copy
            if (_fogRGBA == null || _fogRGBA.width != w || _fogRGBA.height != h)
            {
                if (_fogRGBA != null) Destroy(_fogRGBA);
                _fogRGBA = new Texture2D(w, h, TextureFormat.RGBA32, false);
                _fogRGBA.wrapMode = TextureWrapMode.Clamp;
                _fogRGBA.filterMode = FilterMode.Bilinear;
                _fogPixelBuffer = new Color32[w * h];
            }

            // Read the Alpha8 raw data and convert to RGBA32.
            // The fog texture is stored with BOTH axes inverted by WorldToCell():
            //   texX = (gridX/2) - ux   → world +X maps to lower texX
            //   texY = (gridY/2) - uy   → world +Z maps to lower texY
            // On the world-space Plane this is compensated by UV unwrapping,
            // but for a flat UI RawImage we need to flip 180° (reverse the buffer).
            var rawAlpha = srcTex.GetRawTextureData<byte>();
            int len = rawAlpha.Length;
            if (len != _fogPixelBuffer.Length) return;

            for (int i = 0; i < len; i++)
            {
                byte a = rawAlpha[len - 1 - i]; // reverse = 180° rotation
                _fogPixelBuffer[i] = new Color32(0, 0, 0, a);
            }

            _fogRGBA.SetPixels32(_fogPixelBuffer);
            _fogRGBA.Apply(false, false);

            fogOverlayImage.texture = _fogRGBA;

            // Compute UV rect to align fog grid bounds with minimap world bounds.
            // Fog covers fogCenter ± fogSize/2 in world space.
            // Minimap covers worldCenter ± worldSize/2.
            // We need to map: which part of the fog texture corresponds to the minimap area.
            Vector2 fogCenter = _fogProvider.FogWorldCenter;
            Vector2 fogSize = _fogProvider.FogWorldSize;

            if (fogSize.x > 0f && fogSize.y > 0f && worldSize.x > 0f && worldSize.y > 0f)
            {
                // Fog world bounds
                float fogMinX = fogCenter.x - fogSize.x * 0.5f;
                float fogMinZ = fogCenter.y - fogSize.y * 0.5f;

                // Minimap world bounds
                float mapMinX = worldCenter.x - worldSize.x * 0.5f;
                float mapMinZ = worldCenter.y - worldSize.y * 0.5f;

                // UV offset: how far into the fog texture does the minimap's min corner fall?
                float uvX = (mapMinX - fogMinX) / fogSize.x;
                float uvY = (mapMinZ - fogMinZ) / fogSize.y;

                // UV scale: what fraction of the fog texture does the minimap span?
                float uvW = worldSize.x / fogSize.x;
                float uvH = worldSize.y / fogSize.y;

                fogOverlayImage.uvRect = new UnityEngine.Rect(uvX, uvY, uvW, uvH);
            }
        }

        #endregion

        #region Helpers

        private void OnMarkersChanged() => _dirty = true;

        #endregion
    }
}
