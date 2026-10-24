using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FumblePeak
{
    /// <summary>
    /// Bağlanan her oyuncu için ağ nesnesi. Kendi uzuv(lar)ını yönlendirir.
    /// Kontroller (GDD 2): Fare = uzvun konumu, Sol Tık = tutun / bırak (aç-kapa).
    /// Birden çok uzvun varsa: Q/E/A/D veya 1-4 doğrudan seçer, Tab / tekerlek sırayla gezer,
    /// Sağ Tık = tutun/bırak + sıradaki uzva geç.
    /// Gamepad: Sol analog = uzuv, RT / A = tutun, LB-RB = uzuv değiştir.
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
            if (isLocalPlayer) UpdateCursorLock(false);
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
            ClimberBody.OnRespawned += ResetCursors; // kampa dönünce imleçler eline gelsin
            RebuildOwned();
        }

        void ResetCursors(string _ = null)
        {
            for (int i = 0; i < 4; i++) virtInit[i] = false;
        }

        public override void OnStopLocalPlayer()
        {
            ClimberBody.OnRespawned -= ResetCursors;
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
            if (isLocalPlayer && owned.Count > 0 && !owned.Contains(localActive)) SelectLimb(owned[0]);
        }

        // ---------------- Girdi ----------------
        // Sanal imleç: her uzvun kendi imleci var, fare HAREKETİ (delta) aktif uzvun imlecini kaydırır.
        // İmleç uzvun erişim alanına kilitli → asla kaybolmaz, uzuv değiştirince zıplama olmaz.
        readonly Vector2[] virt = new Vector2[4];
        readonly bool[] virtInit = new bool[4];
        ClimberBody lastBody;
        public static float Sensitivity = 1f;
        const float WorldPerPixel = 0.0045f;

        public Vector2 CursorOf(int limb) => virtInit[limb] ? virt[limb] : (ClimberBody.Instance != null ? ClimberBody.Instance.Target(limb) : Vector2.zero);
        public Vector2 LocalCursor => CursorOf(localActive);

        void Update()
        {
            if (!isLocalPlayer) return;
            var body = ClimberBody.Instance;
            bool blocked = body == null || owned.Count == 0 || InputBlocked;
            UpdateCursorLock(!blocked);
            if (blocked) return;

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;

            if (body != lastBody) { lastBody = body; for (int i = 0; i < 4; i++) virtInit[i] = false; }
            for (int i = 0; i < 4; i++)
                if (!virtInit[i]) { virt[i] = body.Target(i); virtInit[i] = true; }

            // ----- Uzuv seçimi -----
            if (owned.Count > 1)
            {
                int step = 0;
                if (kb != null && kb.tabKey.wasPressedThisFrame) step = 1;
                if (mouse != null)
                {
                    float sc = mouse.scroll.ReadValue().y;
                    if (sc > 0.1f) step = -1; else if (sc < -0.1f) step = 1;
                }
                if (pad != null && pad.rightShoulder.wasPressedThisFrame) step = 1;
                if (pad != null && pad.leftShoulder.wasPressedThisFrame) step = -1;
                if (step != 0) CycleLimb(step);
                if (kb != null)
                {
                    // Q = Sol Kol, E = Sağ Kol, A = Sol Bacak, D = Sağ Bacak (ya da 1-4)
                    if (kb.qKey.wasPressedThisFrame || kb.digit1Key.wasPressedThisFrame) TrySelect(0);
                    if (kb.eKey.wasPressedThisFrame || kb.digit2Key.wasPressedThisFrame) TrySelect(1);
                    if (kb.aKey.wasPressedThisFrame || kb.digit3Key.wasPressedThisFrame) TrySelect(2);
                    if (kb.dKey.wasPressedThisFrame || kb.digit4Key.wasPressedThisFrame) TrySelect(3);
                }
            }

            int L = localActive;
            bool grabbed = body.IsGrabbing(L);
            Vector2 center = grabbed ? body.RenderEffectorPos(L) : body.RenderAnchor(L);
            float radius = body.reach[L] * (grabbed ? 1.0f : 1.3f);

            // ----- İmleç hareketi -----
            Vector2 c = virt[L];
            bool padUsed = false;
            if (pad != null)
            {
                var stick = pad.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.04f) { c = center + stick * radius; padUsed = true; }
            }
            if (!padUsed)
            {
                Vector2 d = mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
                d += Sim.delta; Sim.delta = Vector2.zero; // otomatik test girdisi
                c += d * WorldPerPixel * Sensitivity;
            }
            // erişim alanına kilitle
            Vector2 off = c - center;
            if (off.sqrMagnitude > radius * radius) c = center + off.normalized * radius;
            virt[L] = c;

            // ----- Tutunan eli hareket ettirmek onu kayadan kaldırır -----
            // (en az bir uzuv daha tutunuyorsa; tek dayanakken yanlışlıkla düşme olmasın)
            if (grabbed && (c - body.RenderEffectorPos(L)).sqrMagnitude > 0.2f * 0.2f && OtherSupports(body, L) > 0)
            {
                CmdRelease((byte)L);
                grabbed = false;
            }

            // ----- Tutun / bırak -----
            bool click = (mouse != null && mouse.leftButton.wasPressedThisFrame)
                         || (kb != null && kb.spaceKey.wasPressedThisFrame)
                         || (pad != null && (pad.rightTrigger.wasPressedThisFrame || pad.buttonSouth.wasPressedThisFrame))
                         || Sim.click;
            bool release = (mouse != null && mouse.rightButton.wasPressedThisFrame)
                           || (pad != null && pad.buttonEast.wasPressedThisFrame)
                           || Sim.rightClick;
            Sim.click = Sim.rightClick = false;

            sendTimer += Time.deltaTime;
            if (sendTimer >= 1f / 40f && (c - lastSent).sqrMagnitude > 0.000001f)
            {
                sendTimer = 0f;
                lastSent = c;
                CmdSetTarget((byte)L, c);
            }

            if (release && (grabbed || body.IsFist(L)))
            {
                CmdRelease((byte)L);
            }
            else if (click)
            {
                // İmleç elin üstündeyse tıklama = bırak; başka yerdeyse = oraya tutun
                bool onHand = (c - body.RenderEffectorPos(L)).sqrMagnitude < 0.2f * 0.2f;
                if ((grabbed || body.IsFist(L)) && onHand)
                {
                    CmdRelease((byte)L);
                }
                else
                {
                    CmdGrabAt((byte)L, c);
                    LastGrabLimb = L;
                    // Kolay mod: tutunma isteğinden sonra kontrol diğer ele geçer (el-el tırmanış)
                    if (AutoSwitch) NextInGroup();
                }
            }
        }

        /// <summary>Tutunduktan sonra aynı gruptaki diğer uzva otomatik geç.</summary>
        public static bool AutoSwitch = true;
        public static int LastGrabLimb = -1;

        /// <summary>Otomatik test / bot girdisi (gerçek oyunda kullanılmaz).</summary>
        public static class Sim
        {
            public static Vector2 delta;
            public static bool click, rightClick;
        }

        static int OtherSupports(ClimberBody body, int except)
        {
            int n = 0;
            for (int i = 0; i < 4; i++) if (i != except && body.IsGrabbing(i)) n++;
            // yere basan ayaklar (gövde zemine yakın) da sayılır
            n += body.GroundedFeet;
            return n;
        }

        bool cursorLocked;
        void UpdateCursorLock(bool want)
        {
            if (want == cursorLocked) return;
            cursorLocked = want;
            UnityEngine.Cursor.lockState = want ? CursorLockMode.Locked : CursorLockMode.None;
            UnityEngine.Cursor.visible = !want;
        }


        /// <summary>Kol ↔ kol, bacak ↔ bacak (el-el tırmanış ritmi).</summary>
        void NextInGroup()
        {
            bool arm = LimbUtil.IsArm(localActive);
            for (int k = 1; k <= 4; k++)
            {
                int cand = (localActive + k) % 4;
                if (owned.Contains(cand) && LimbUtil.IsArm(cand) == arm && cand != localActive) { SelectLimb(cand); return; }
            }
            CycleLimb(1);
        }

        void TrySelect(int limb)
        {
            if (owned.Contains(limb)) SelectLimb(limb);
        }

        void CycleLimb(int step)
        {
            int idx = owned.IndexOf(localActive);
            if (idx < 0) idx = 0;
            SelectLimb(owned[(idx + step + owned.Count) % owned.Count]);
        }

        void SelectLimb(int limb)
        {
            var body = ClimberBody.Instance;
            if (body != null && isLocalPlayer)
            {
                // imleci seçilen elin/ayağın üstüne getir (zıplama yok, tutunan uzuv kopmaz)
                virt[limb] = body.RenderEffectorPos(limb);
                virtInit[limb] = true;
            }
            localActive = limb;
            lastSent = new Vector2(float.MaxValue, 0);
            CmdSetActive((byte)limb);
            RosterChanged?.Invoke();
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
        void CmdSetActive(byte limb)
        {
            if (LimbUtil.Has(limbMask, limb)) activeLimb = limb;
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
