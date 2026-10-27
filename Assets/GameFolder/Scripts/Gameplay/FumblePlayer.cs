using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FumblePeak
{
    /// <summary>
    /// Bağlanan her oyuncu için ağ nesnesi. Kendi uzuv(lar)ını fareyle sürükler:
    /// imleç uzvun üstüne gelince gösterge çıkar, sol tuşa basılı tutup sürükle,
    /// bırakınca altında kaya varsa tutunur, yoksa uzuv serbest kalır.
    /// Co-op'ta herkes sadece kendi uzuvlarını tutabilir.
    /// </summary>
    public class FumblePlayer : NetworkBehaviour
    {
        public static FumblePlayer Local { get; private set; }
        public static readonly List<FumblePlayer> All = new List<FumblePlayer>();
        public static event System.Action RosterChanged;
        /// <summary>Başka bir oyuncu katıldı / ayrıldı (isim). Lobi ve HUD bildirimleri için.</summary>
        public static event System.Action<string> PlayerJoined;
        public static event System.Action<string> PlayerLeft;

        [SyncVar(hook = nameof(OnNameChanged))] public string playerName = "Dağcı";
        [SyncVar(hook = nameof(OnSlotChanged))] public int slot;
        [SyncVar(hook = nameof(OnMaskChanged))] public byte limbMask;
        [SyncVar(hook = nameof(OnActiveChanged))] public byte activeLimb;
        [SyncVar(hook = nameof(OnReadyChanged))] public bool ready;
        [SyncVar(hook = nameof(OnSteamChanged))] public ulong steamId;
        [SyncVar] public bool isHost;

        /// <summary>Bu oyuncu host mu? (lobide "HOST" rozeti, hazır olmasına gerek yok)</summary>
        public bool IsHostPlayer => isHost;

        public Color Color => LimbUtil.PlayerColors[Mathf.Clamp(slot, 0, 3)];

        /// <summary>İstemcide gecikmesiz aktif uzuv.</summary>
        public int ActiveLimb => isLocalPlayer ? localActive : activeLimb;

        public static bool InputBlocked;

        int localActive;
        float sendTimer;
        Vector2 lastSent = new Vector2(float.MaxValue, 0);
        readonly List<int> owned = new List<int>();

        void OnEnable() { All.Add(this); RosterChanged?.Invoke(); }
        void OnDisable() { All.Remove(this); RosterChanged?.Invoke(); }

        public override void OnStartServer()
        {
            isHost = connectionToClient != null && connectionToClient.connectionId == NetworkConnection.LocalConnectionId;
            FumbleNetworkManager.Instance?.ServerRegister(this);
        }

        public override void OnStartClient()
        {
            // Lobi (Ana Menü) → oyun sahnesi geçişinde oyuncu nesnesi korunur
            DontDestroyOnLoad(gameObject);
            if (!isLocalPlayer) profileAnnounced = true;
        }

        public override void OnStopClient()
        {
            if (isLocalPlayer) { UnityEngine.Cursor.lockState = CursorLockMode.None; UnityEngine.Cursor.visible = true; }
            else if (NetworkClient.isConnected && profileAnnounced) PlayerLeft?.Invoke(playerName);
        }

        public override void OnStopServer()
        {
            FumbleNetworkManager.Instance?.ServerUnregister(this);
        }

        public override void OnStartLocalPlayer()
        {
            Local = this;
            ulong sid = SteamLobby.Instance != null && SteamLobby.Instance.IsReady ? SteamLobby.Instance.MySteamId : 0;
            CmdSetProfile(PlayerProfile.Name, sid);
            RebuildOwned();
        }

        public override void OnStopLocalPlayer()
        {
            if (Local == this) Local = null;
        }

        [Server]
        public void ServerSetMask(byte mask)
        {
            limbMask = mask;
            if (!LimbUtil.Has(mask, activeLimb))
                for (int i = 0; i < 4; i++) if (LimbUtil.Has(mask, i)) { activeLimb = (byte)i; break; }
        }

        void OnNameChanged(string _, string __) => RosterChanged?.Invoke();
        void OnSlotChanged(int _, int __) => RosterChanged?.Invoke();
        void OnActiveChanged(byte _, byte __) => RosterChanged?.Invoke();
        void OnReadyChanged(bool _, bool __) => RosterChanged?.Invoke();
        void OnSteamChanged(ulong _, ulong __) => RosterChanged?.Invoke();

        [Server]
        public void ServerSetReady(bool r) => ready = r;

        void OnMaskChanged(byte _, byte __)
        {
            RebuildOwned();
            RosterChanged?.Invoke();
        }

        void RebuildOwned()
        {
            owned.Clear();
            for (int i = 0; i < 4; i++) if (LimbUtil.Has(limbMask, i)) owned.Add(i);
            if (isLocalPlayer && owned.Count > 0 && !owned.Contains(localActive)) localActive = owned[0];
        }

        // ---------------- Girdi: sürükle-bırak ----------------
        // Fare imleci serbest. İmleç kendi uzvunun (el/ayak) üstüne gelince gösterge çıkar.
        // Sol tuşa bas → uzvu tut ve sürükle (en fazla kol/bacak boyu kadar uzanır).
        // Bırak → altında kaya varsa tutunur, yoksa uzuv serbest kalır (fizik).
        public static float Sensitivity = 1f; // (eski ayar; artık kullanılmıyor)
        public const float HoverRadius = 0.26f;

        /// <summary>İmlecin üstünde durduğu kendi uzvun (-1 = yok).</summary>
        public int HoverLimb { get; private set; } = -1;
        /// <summary>Şu an sürüklenen uzuv (-1 = yok).</summary>
        public int DragLimb { get; private set; } = -1;
        /// <summary>Farenin oyun düzlemindeki (z=0) konumu.</summary>
        public Vector2 Pointer { get; private set; }
        public bool PointerValid { get; private set; }

        public Vector2 CursorOf(int limb) => isLocalPlayer && DragLimb == limb ? Pointer
            : (ClimberBody.Instance != null ? ClimberBody.Instance.Target(limb) : Vector2.zero);

        void Update()
        {
            if (!isLocalPlayer) return;
            var body = ClimberBody.Instance;
            bool blocked = body == null || owned.Count == 0 || InputBlocked;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            if (blocked)
            {
                if (DragLimb >= 0 && body != null) EndDrag(body);
                HoverLimb = -1;
                return;
            }

            var mouse = Mouse.current;
            var cam = Camera.main;
            PointerValid = false;
            if (Sim.active) { Pointer = Sim.pointer; PointerValid = true; }
            else if (mouse != null && cam != null)
            {
                var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
                if (Mathf.Abs(ray.direction.z) > 1e-4f)
                {
                    float t = -ray.origin.z / ray.direction.z;
                    if (t > 0f) { Pointer = ray.origin + ray.direction * t; PointerValid = true; }
                }
            }
            if (!PointerValid) return;

            bool down = Sim.active ? Sim.down : mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool up = Sim.active ? Sim.up : mouse != null && mouse.leftButton.wasReleasedThisFrame;
            Sim.down = Sim.up = false;

            if (DragLimb < 0)
            {
                // Hangi uzvumun üstündeyim?
                HoverLimb = -1;
                float best = HoverRadius * HoverRadius;
                foreach (int i in owned)
                {
                    float d = (body.RenderEffectorPos(i) - Pointer).sqrMagnitude;
                    if (d < best) { best = d; HoverLimb = i; }
                }
                if (down && HoverLimb >= 0)
                {
                    DragLimb = HoverLimb;
                    localActive = DragLimb;
                    lastSent = new Vector2(float.MaxValue, 0);
                    CmdBeginDrag((byte)DragLimb);
                    RosterChanged?.Invoke();
                }
            }
            else
            {
                HoverLimb = DragLimb;
                sendTimer += Time.deltaTime;
                if (sendTimer >= 1f / 40f && (Pointer - lastSent).sqrMagnitude > 0.000001f)
                {
                    sendTimer = 0f;
                    lastSent = Pointer;
                    CmdSetTarget((byte)DragLimb, Pointer);
                }
                bool held = Sim.active ? !up : mouse == null || mouse.leftButton.isPressed;
                if (up || !held) EndDrag(body);
            }
        }

        void EndDrag(ClimberBody body)
        {
            CmdEndDrag((byte)DragLimb, Pointer);
            DragLimb = -1;
            RosterChanged?.Invoke();
        }

        /// <summary>Otomatik test / bot girdisi (gerçek oyunda kullanılmaz).</summary>
        public static class Sim
        {
            public static bool active;
            public static Vector2 pointer;
            public static bool down, up;
        }

        // ---------------- Komutlar ----------------
        bool profileAnnounced;

        [Command]
        void CmdSetProfile(string n, ulong sid)
        {
            playerName = PlayerProfile.Sanitize(n);
            steamId = sid;
            RpcAnnounceJoin();
        }

        [ClientRpc]
        void RpcAnnounceJoin()
        {
            profileAnnounced = true;
            if (!isLocalPlayer) PlayerJoined?.Invoke(playerName);
        }

        public void SetReady(bool r) { if (isLocalPlayer) CmdSetReady(r); }

        [Command]
        void CmdSetReady(bool r) => ready = r;

        [TargetRpc]
        public void TargetKicked(NetworkConnectionToClient target)
        {
            FumbleNetworkManager.Instance?.MarkKicked();
        }

        [Command]
        void CmdBeginDrag(byte limb)
        {
            if (!LimbUtil.Has(limbMask, limb) || ClimberBody.Instance == null) return;
            activeLimb = limb;
            ClimberBody.Instance.ServerBeginDrag(limb);
        }

        [Command]
        void CmdEndDrag(byte limb, Vector2 p)
        {
            if (!LimbUtil.Has(limbMask, limb) || ClimberBody.Instance == null) return;
            ClimberBody.Instance.ServerEndDrag(limb, p);
        }

        [Command(channel = Channels.Unreliable)]
        void CmdSetTarget(byte limb, Vector2 p)
        {
            if (!LimbUtil.Has(limbMask, limb)) return;
            ClimberBody.Instance?.ServerSetTarget(limb, p);
        }

        [Command]
        void CmdGrabAt(byte limb, Vector2 p)
        {
            if (!LimbUtil.Has(limbMask, limb) || ClimberBody.Instance == null) return;
            ClimberBody.Instance.ServerSetTarget(limb, p);
            ClimberBody.Instance.ServerGrab(limb);
        }

        [Command]
        void CmdRelease(byte limb)
        {
            if (!LimbUtil.Has(limbMask, limb) || ClimberBody.Instance == null) return;
            ClimberBody.Instance.ServerRelease(limb);
        }

        public void RequestRespawn() { if (isLocalPlayer) CmdRespawn(); }

        float lastRespawnRequest;

        [Command]
        void CmdRespawn()
        {
            if (Time.time - lastRespawnRequest < 3f) return;
            lastRespawnRequest = Time.time;
            ClimberBody.Instance?.ServerRespawn("manual");
        }

        /// <summary>Bir uzvu hangi oyuncu kontrol ediyor?</summary>
        public static FumblePlayer OwnerOf(int limb)
        {
            foreach (var p in All) if (p != null && LimbUtil.Has(p.limbMask, limb)) return p;
            return null;
        }
    }
}
