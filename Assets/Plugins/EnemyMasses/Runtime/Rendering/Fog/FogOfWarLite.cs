using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Arawn.EnemyMasses.Runtime;

namespace Arawn.Rendering.Fog
{
    /// <summary>
    /// GPU compute-shader driven fog of war.
    /// All per-pixel work (clear, stamp, blend) runs on the GPU via three compute
    /// kernels. The fog plane renders directly from a RenderTexture — zero CPU→GPU
    /// upload per tick. A lightweight AsyncGPUReadback supplies CPU-side data for
    /// <see cref="IsRevealed"/>/<see cref="IsActivelyVisible"/> queries and the
    /// <see cref="IMinimapFogProvider.FogTexture"/> interface (1-frame latency).
    /// </summary>
    [DefaultExecutionOrder(210)]
    public class FogOfWarLite : MonoBehaviour, IFogVisibility, IMinimapFogProvider
    {
        // ─── Serialised fields ──────────────────────────────────────────────────
        [Header("Grid")]
        [Tooltip("Number of cells in X (width)")] public int gridX = 512;
        [Tooltip("Number of cells in Y (height)")] public int gridY = 512;
        [Tooltip("World units per cell")] public float unitScale = 1f;
        [Tooltip("Center of the fog grid")] public Transform gridCenter;
        [Tooltip("Height offset for fog plane")][Range(0f, 50f)] public float planeHeight = 1f;

        [Header("Reveal")]
        [Tooltip("Max reveal radius in world units")]
        public float maxRevealRadius = 25f;
        [Tooltip("Alpha used for hidden fog (revealed is transparent)")][Range(0f, 1f)] public float revealedAlpha = 1f;
        [Tooltip("Seconds between fog updates")][Range(0.02f, 0.5f)] public float refreshInterval = 0.1f;
        [Tooltip("Cap how many agents to process per tick (sampling)")] public int maxAgentsPerTick = 5000;
        [Tooltip("Smooth blend of mask")] public bool lerpBuffer = false;
        [Tooltip("Keep previously revealed cells visible (no re-fog)")] public bool keepRevealed = true;
        [Tooltip("Allow fog to fade back up to a partial opacity when not revealed")] public bool enableRefog = false;
        [Tooltip("Target opacity when refogging (0 = fully clear, 1 = fully hidden)")][Range(0f, 1f)] public float refogOpacity = 0.5f;
        [Tooltip("Refog speed (per second)")][Range(0.01f, 5f)] public float refogSpeed = 1.0f;

        [Header("Sampling")]
        [Tooltip("Process agents round-robin across frames to avoid re-scanning the same agents every tick.")]
        public bool roundRobinSampling = true;

        [Header("Compute")]
        [Tooltip("Compute shader asset (FogOfWarCompute). Must be assigned.")]
        public ComputeShader computeShader;

        [Header("References")]
        public EnemyMassesCrowdController crowdController;
        public EnemyMassesRTSController rtsController;
        [Tooltip("Fog material (expects _MainTex R channel = opacity)")] public Material fogMaterial;

        [Header("Debug")]
        public bool logStats = false;
        public bool drawBounds = false;

        // ─── GPU resources ──────────────────────────────────────────────────────
        private RenderTexture _maskRT;   // per-tick scratch (cleared, stamped)
        private RenderTexture _alphaRT;  // persistent accumulated fog
        private ComputeBuffer _revealerBuffer;
        private int _kernelClear;
        private int _kernelStamp;
        private int _kernelBlend;
        private Vector4[] _revealerData;
        private int _maxRevealerCapacity = 256;

        // ─── CPU readback ───────────────────────────────────────────────────────
        private NativeArray<byte> _cpuAlpha;
        private Texture2D _tex;          // R8 copy for IMinimapFogProvider
        private bool _readbackPending;
        private bool _readbackReady;
        private bool _destroyed;

        // ─── World plane ────────────────────────────────────────────────────────
        private GameObject _plane;

        // ─── Agent tracking ─────────────────────────────────────────────────────
        private HashSet<string> _playerFactions = new HashSet<string>();
        private float _lastUpdate;
        private int _agentCursor;

        // ─── Interface properties ───────────────────────────────────────────────
        /// <summary>CPU-side fog texture (R8). Updated asynchronously via GPU readback.</summary>
        public Texture2D FogTexture => _tex;
        /// <summary>Raw CPU alpha buffer. 255 = hidden, 0 = revealed.</summary>
        public NativeArray<byte> AlphaBuffer => _cpuAlpha;
        public bool HasInitializedBuffers => _readbackReady && _cpuAlpha.IsCreated && _tex != null;

        public Vector2 FogWorldCenter => gridCenter != null
            ? new Vector2(gridCenter.position.x, gridCenter.position.z)
            : Vector2.zero;

        public Vector2 FogWorldSize => new Vector2(gridX * unitScale, gridY * unitScale);

        // ═════════════════════════════════════════════════════════════════════════
        // Lifecycle
        // ═════════════════════════════════════════════════════════════════════════

        void Awake()
        {
            if (gridCenter == null) gridCenter = transform;
            if (crowdController == null) crowdController = FindFirstObjectByType<EnemyMassesCrowdController>();
            if (rtsController == null) rtsController = FindFirstObjectByType<EnemyMassesRTSController>();

            if (!SystemInfo.supportsComputeShaders)
            {
                Debug.LogError("[FogOfWarLite] Compute shaders are not supported on this platform. Fog disabled.");
                enabled = false;
                return;
            }

            if (computeShader == null)
            {
                Debug.LogError("[FogOfWarLite] Compute shader asset not assigned. Drag FogOfWarCompute.compute into the inspector.");
                enabled = false;
                return;
            }

            InitializeGPU();
            BuildPlane();
            CachePlayerFactions();
        }

        void OnDestroy()
        {
            _destroyed = true;
            ReleaseGPU();
            if (_cpuAlpha.IsCreated) _cpuAlpha.Dispose();
        }

        void Update()
        {
            float now = Time.time;
            float delta = now - _lastUpdate;
            if (delta < refreshInterval) return;
            _lastUpdate = now;
            DispatchFog(delta);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // GPU initialisation / teardown
        // ═════════════════════════════════════════════════════════════════════════

        void InitializeGPU()
        {
            _kernelClear = computeShader.FindKernel("ClearMask");
            _kernelStamp = computeShader.FindKernel("StampRevealers");
            _kernelBlend = computeShader.FindKernel("BlendMask");

            _maskRT  = CreateFogRT("FOW_Mask");
            _alphaRT = CreateFogRT("FOW_Alpha");

            // Initialise _alphaRT to 1.0 (fully hidden)
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _alphaRT;
            GL.Clear(false, true, Color.white);
            RenderTexture.active = prev;

            // CPU readback buffer (starts fully hidden)
            _cpuAlpha = new NativeArray<byte>(gridX * gridY, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < _cpuAlpha.Length; i++) _cpuAlpha[i] = 255;

            // Texture2D for minimap interface (R8 — one byte per pixel)
            _tex = new Texture2D(gridX, gridY, TextureFormat.R8, false);
            _tex.wrapMode = TextureWrapMode.Clamp;
            _tex.filterMode = FilterMode.Bilinear;

            // Revealer upload buffer
            _revealerData = new Vector4[_maxRevealerCapacity];
            _revealerBuffer = new ComputeBuffer(_maxRevealerCapacity, sizeof(float) * 4);
        }

        RenderTexture CreateFogRT(string rtName)
        {
            var rt = new RenderTexture(gridX, gridY, 0, RenderTextureFormat.RFloat);
            rt.name = rtName;
            rt.enableRandomWrite = true;
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.Create();
            return rt;
        }

        void ReleaseGPU()
        {
            if (_maskRT  != null) { _maskRT.Release();  Destroy(_maskRT);  _maskRT  = null; }
            if (_alphaRT != null) { _alphaRT.Release(); Destroy(_alphaRT); _alphaRT = null; }
            if (_revealerBuffer != null) { _revealerBuffer.Release(); _revealerBuffer = null; }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // World-space fog plane
        // ═════════════════════════════════════════════════════════════════════════

        void BuildPlane()
        {
            _plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _plane.name = "[FOW_Lite_Plane]";
            _plane.transform.SetParent(transform, false);
            _plane.transform.position = new Vector3(
                gridCenter.position.x,
                gridCenter.position.y + planeHeight,
                gridCenter.position.z);
            _plane.transform.localScale = new Vector3(
                (gridX * unitScale) / 10f, 1f, (gridY * unitScale) / 10f);
            DestroyImmediate(_plane.GetComponent<MeshCollider>());

            if (fogMaterial == null)
            {
                // Prefer compute-aware shader; fall back to URP variant
                Shader sh = Shader.Find("EnemyMasses/FogOfWarCompute");
                if (sh == null) sh = Shader.Find("Arawn/Fog/FogOfWarLiteURP");
                if (sh == null) sh = Shader.Find("Unlit/Transparent");
                fogMaterial = new Material(sh);
                fogMaterial.color = new Color(0f, 0f, 0f, 1f);
            }
            fogMaterial = new Material(fogMaterial); // instance
            // Bind the RenderTexture directly — no CPU upload needed for the plane
            fogMaterial.SetTexture("_MainTex", _alphaRT);

            var mr = _plane.GetComponent<MeshRenderer>();
            mr.sharedMaterial = fogMaterial;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Per-tick compute dispatch
        // ═════════════════════════════════════════════════════════════════════════

        void DispatchFog(float deltaTime)
        {
            EnsureRTSController();

            int revealerCount = CollectRevealers();
            if (revealerCount == 0 && !enableRefog) return;

            int groupsX = Mathf.CeilToInt(gridX / 8f);
            int groupsY = Mathf.CeilToInt(gridY / 8f);

            // --- Kernel 0: ClearMask ---
            computeShader.SetInt("_GridX", gridX);
            computeShader.SetInt("_GridY", gridY);
            computeShader.SetTexture(_kernelClear, "_MaskTex", _maskRT);
            computeShader.Dispatch(_kernelClear, groupsX, groupsY, 1);

            // --- Kernel 1: StampRevealers ---
            if (revealerCount > 0)
            {
                EnsureRevealerBuffer(revealerCount);
                _revealerBuffer.SetData(_revealerData, 0, 0, revealerCount);
                computeShader.SetTexture(_kernelStamp, "_MaskTex", _maskRT);
                computeShader.SetBuffer(_kernelStamp, "_Revealers", _revealerBuffer);
                computeShader.SetInt("_RevealerCount", revealerCount);
                computeShader.Dispatch(_kernelStamp, Mathf.CeilToInt(revealerCount / 64f), 1, 1);
            }

            // --- Kernel 2: BlendMask ---
            computeShader.SetTexture(_kernelBlend, "_MaskTex", _maskRT);
            computeShader.SetTexture(_kernelBlend, "_AlphaTex", _alphaRT);
            computeShader.SetFloat("_RevealedAlpha", revealedAlpha);
            computeShader.SetFloat("_RefogSpeed", refogSpeed);
            computeShader.SetFloat("_RefogOpacity", refogOpacity);
            computeShader.SetFloat("_DeltaTime", Mathf.Max(0.0001f, deltaTime));
            computeShader.SetInt("_KeepRevealed", keepRevealed ? 1 : 0);
            computeShader.SetInt("_EnableRefog", enableRefog ? 1 : 0);
            computeShader.SetInt("_LerpBuffer", lerpBuffer ? 1 : 0);
            computeShader.Dispatch(_kernelBlend, groupsX, groupsY, 1);

            // --- Async GPU readback (CPU queries + minimap texture) ---
            if (!_readbackPending)
            {
                _readbackPending = true;
                AsyncGPUReadback.Request(_alphaRT, 0, callback: OnReadbackComplete);
            }

            if (logStats && revealerCount > 0)
                Debug.Log($"[FogOfWarLite] Dispatched {revealerCount} revealers to GPU");
        }

        void OnReadbackComplete(AsyncGPUReadbackRequest request)
        {
            _readbackPending = false;
            if (_destroyed || !_cpuAlpha.IsCreated) return;
            if (request.hasError) return;

            // RFloat data → byte conversion
            var floats = request.GetData<float>();
            int len = Mathf.Min(floats.Length, _cpuAlpha.Length);
            for (int i = 0; i < len; i++)
                _cpuAlpha[i] = (byte)(Mathf.Clamp01(floats[i]) * 255f);

            // Update the Texture2D used by IMinimapFogProvider / minimap
            _tex.SetPixelData(_cpuAlpha, 0);
            _tex.Apply(false, false);
            _readbackReady = true;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Revealer collection (CPU — only gathers positions, no per-pixel work)
        // ═════════════════════════════════════════════════════════════════════════

        int CollectRevealers()
        {
            var agents = crowdController != null ? crowdController.GetAgents() : null;
            var externals = rtsController != null ? rtsController.ExternalSelectables : null;
            var registeredRevealers = RTSWorldUnitRegistry.RegisteredFogRevealers;

            int count = 0;
            int agentCount = agents != null ? agents.Count : 0;

            // ── Crowd agents ────────────────────────────────────────────────────
            if (roundRobinSampling && agentCount > 0)
            {
                int maxProcess = Mathf.Min(maxAgentsPerTick, agentCount);
                int processed = 0;
                for (int i = 0; i < agentCount && processed < maxProcess; i++)
                {
                    int idx = (_agentCursor + i) % agentCount;
                    var agent = agents[idx];
                    if (agent == null || agent.isDead || agent.navigationAgent == null) continue;

                    string faction = agent.enemyCrowd?.factionName;
                    bool isPlayer = _playerFactions.Count == 0 ||
                                    (!string.IsNullOrEmpty(faction) && _playerFactions.Contains(faction));
                    if (!isPlayer) continue;

                    Vector3 wp = agent.WorldPosition;
                    Vector2Int cell = WorldToCell(wp);
                    if (!InBounds(cell)) continue;

                    int radius = Mathf.Max(1, Mathf.RoundToInt(GetSightRange(agent) / unitScale));
                    AddRevealer(ref count, cell.x, cell.y, radius);
                    processed++;
                }
                _agentCursor = (_agentCursor + processed) % Mathf.Max(1, agentCount);
            }
            else if (agentCount > 0)
            {
                int processed = 0;
                foreach (var agent in agents)
                {
                    if (processed >= maxAgentsPerTick) break;
                    if (agent == null || agent.isDead || agent.navigationAgent == null) continue;

                    string faction = agent.enemyCrowd?.factionName;
                    bool isPlayer = _playerFactions.Count == 0 ||
                                    (!string.IsNullOrEmpty(faction) && _playerFactions.Contains(faction));
                    if (!isPlayer) continue;

                    Vector3 wp = agent.WorldPosition;
                    Vector2Int cell = WorldToCell(wp);
                    if (!InBounds(cell)) continue;

                    int radius = Mathf.Max(1, Mathf.RoundToInt(GetSightRange(agent) / unitScale));
                    AddRevealer(ref count, cell.x, cell.y, radius);
                    processed++;
                }
            }

            // ── External selectables ────────────────────────────────────────────
            if (externals != null)
            {
                for (int i = 0; i < externals.Count; i++)
                {
                    var external = externals[i];
                    if (external == null || !external.IsAlive) continue;
                    var revealer = external as IRTSFogRevealer;
                    if (revealer == null || !revealer.RevealFogOfWar) continue;

                    float radiusWorld = revealer.FogRevealRadius;
                    if (radiusWorld <= 0f) continue;

                    Vector3 wp = revealer.FogRevealPosition;
                    Vector2Int cell = WorldToCell(wp);
                    if (!InBounds(cell)) continue;

                    int radius = Mathf.Max(1, Mathf.RoundToInt(radiusWorld / unitScale));
                    AddRevealer(ref count, cell.x, cell.y, radius);
                }
            }

            // ── Registered fog revealers ────────────────────────────────────────
            if (registeredRevealers != null)
            {
                foreach (var revealer in registeredRevealers)
                {
                    if (revealer == null || !revealer.RevealFogOfWar) continue;

                    float radiusWorld = revealer.FogRevealRadius;
                    if (radiusWorld <= 0f) continue;

                    Vector3 wp = revealer.FogRevealPosition;
                    Vector2Int cell = WorldToCell(wp);
                    if (!InBounds(cell)) continue;

                    int radius = Mathf.Max(1, Mathf.RoundToInt(radiusWorld / unitScale));
                    AddRevealer(ref count, cell.x, cell.y, radius);
                }
            }

            return count;
        }

        void AddRevealer(ref int count, int cellX, int cellY, int radius)
        {
            if (count >= _revealerData.Length)
            {
                _maxRevealerCapacity = Mathf.Max(_maxRevealerCapacity * 2, count + 64);
                Array.Resize(ref _revealerData, _maxRevealerCapacity);
            }
            _revealerData[count] = new Vector4(cellX, cellY, radius, 0f);
            count++;
        }

        void EnsureRevealerBuffer(int needed)
        {
            if (_revealerBuffer != null && _revealerBuffer.count >= needed) return;
            if (_revealerBuffer != null) _revealerBuffer.Release();
            int capacity = Mathf.Max(needed, _maxRevealerCapacity);
            _revealerBuffer = new ComputeBuffer(capacity, sizeof(float) * 4);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Utility (unchanged API surface for backward compat)
        // ═════════════════════════════════════════════════════════════════════════

        void CachePlayerFactions()
        {
            _playerFactions.Clear();
            if (rtsController != null && rtsController.playerControlledFactions != null)
            {
                foreach (var f in rtsController.playerControlledFactions.SelectedFactions)
                {
                    if (!string.IsNullOrEmpty(f)) _playerFactions.Add(f);
                }
            }
        }

        void EnsureRTSController()
        {
            if (rtsController != null) return;
            rtsController = FindFirstObjectByType<EnemyMassesRTSController>();
            if (rtsController != null && rtsController.playerControlledFactions != null &&
                rtsController.playerControlledFactions.HasAnyFaction)
            {
                CachePlayerFactions();
            }
        }

        Vector2Int WorldToCell(Vector3 wp)
        {
            int ux = Mathf.RoundToInt((wp.x - gridCenter.position.x) / unitScale);
            int uy = Mathf.RoundToInt((wp.z - gridCenter.position.z) / unitScale);
            int texY = (gridY / 2) - uy;
            int texX = (gridX / 2) - ux;
            return new Vector2Int(texX, texY);
        }

        bool InBounds(Vector2Int c) => c.x >= 0 && c.x < gridX && c.y >= 0 && c.y < gridY;

        float GetSightRange(CrowdAgent agent)
        {
            if (agent.enemyCrowd != null)
                return agent.enemyCrowd.sightRange > 0 ? agent.enemyCrowd.sightRange : agent.enemyCrowd.aggroRange;
            return 10f;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // IFogVisibility
        // ═════════════════════════════════════════════════════════════════════════

        public bool IsRevealed(Vector3 worldPos, byte maxAlpha = 250)
        {
            if (!_cpuAlpha.IsCreated) return false;
            Vector2Int cell = WorldToCell(worldPos);
            if (!InBounds(cell)) return false;
            return _cpuAlpha[cell.y * gridX + cell.x] < maxAlpha;
        }

        /// <summary>
        /// Returns true only if the position is actively visible (not refogged).
        /// Use for minimap icons that should hide when refog covers them.
        /// </summary>
        public bool IsActivelyVisible(Vector3 worldPos, byte maxAlpha = 32)
        {
            if (!_cpuAlpha.IsCreated) return false;
            Vector2Int cell = WorldToCell(worldPos);
            if (!InBounds(cell)) return false;
            return _cpuAlpha[cell.y * gridX + cell.x] < maxAlpha;
        }

        // ═════════════════════════════════════════════════════════════════════════
        // Gizmos
        // ═════════════════════════════════════════════════════════════════════════

        void OnDrawGizmosSelected()
        {
            if (!drawBounds) return;
            if (gridCenter == null) gridCenter = transform;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(gridCenter.position, new Vector3(gridX * unitScale, 0.1f, gridY * unitScale));
        }
    }
}
