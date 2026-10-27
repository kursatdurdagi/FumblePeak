using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FumblePeak
{
    /// <summary>
    /// Host / client ilişkisi.
    ///  • Host = sunucu + yerel oyuncu. Lobi Ana Menü sahnesinde kurulur (onlineScene boş);
    ///    oyuncular burada toplanır, "Hazır" olur, host oyunu başlatınca herkes GameScene'e geçer.
    ///  • Taşıyıcı: Steam açıksa FizzySteamworks (P2P relay), değilse KCP (IP, UDP 7777).
    ///  • Tek oyuncu: dinlemeyen yerel host, doğrudan oyuna geçer.
    /// </summary>
    public class FumbleNetworkManager : NetworkManager
    {
        public static FumbleNetworkManager Instance => singleton as FumbleNetworkManager;

        [Header("Fumble Peak")]
        public GameObject climberPrefab;
        public Transport localTransport;   // KCP
        [Scene] public string gameScene;
        [HideInInspector] public Transport steamTransport; // SteamLobby tarafından eklenir

        /// <summary>Ana menüde gösterilecek bekleyen mesaj (lokalize).</summary>
        public static string PendingMenuMessage;
        /// <summary>Tek oyunculu (çevrimdışı) oturum mu?</summary>
        public static bool IsSinglePlayer { get; private set; }

        public event Action<string> StatusChanged;

        readonly List<FumblePlayer> serverPlayers = new List<FumblePlayer>();
        bool intentionalLeave;
        bool wasConnected;

        public bool UsingSteam => steamTransport != null && transport == steamTransport;
        public bool IsInGameScene => !string.IsNullOrEmpty(gameScene) && SceneManager.GetActiveScene().path == gameScene;
        public int PlayerCount => FumblePlayer.All.Count;

        public override void Awake()
        {
            if (localTransport == null) localTransport = GetComponent<kcp2k.KcpTransport>();
            if (transport == null) transport = localTransport;
            base.Awake();
            L.EnsureInit();
            PlayerProfile.ApplySaved();
        }

        public void UseTransport(Transport t)
        {
            if (t == null) t = localTransport;
            transport = t;
            Transport.active = t;
        }

        public ushort LocalPort => localTransport is PortTransport pt ? pt.Port : (ushort)7777;

        void Begin(bool single)
        {
            intentionalLeave = false;
            wasConnected = false;
            IsSinglePlayer = single;
            PendingMenuMessage = null;
        }

        // ---------------- Başlatma ----------------
        /// <summary>Tek oyuncu: port açılmaz, kimse katılamaz; doğrudan oyuna geçilir.</summary>
        public void StartSinglePlayer()
        {
            Begin(true);
            UseTransport(localTransport);
            NetworkServer.listen = false;
            StartHost();
            StartCoroutine(StartGameNextFrame());
        }

        IEnumerator StartGameNextFrame()
        {
            // Yerel oyuncu oluşmadan sahne değişirse istemci ikinci kez AddPlayer gönderir
            // ("There is already a player for this connection") → önce oyuncuyu bekle.
            float until = Time.unscaledTime + 3f;
            while (NetworkClient.localPlayer == null && Time.unscaledTime < until) yield return null;
            StartGame();
        }

        /// <summary>IP ile lobi kur (lobi ana menüde açılır).</summary>
        public void HostLocal()
        {
            Begin(false);
            NetworkServer.listen = true;
            UseTransport(localTransport);
            StartHost();
        }

        public void JoinLocal(string address)
        {
            Begin(false);
            UseTransport(localTransport);
            networkAddress = string.IsNullOrWhiteSpace(address) ? "localhost" : address.Trim();
            StatusChanged?.Invoke(L.T("msg.connecting_to", networkAddress));
            StartClient();
        }

        /// <summary>SteamLobby lobi oluşturunca çağırır.</summary>
        public void HostSteam()
        {
            Begin(false);
            NetworkServer.listen = true;
            UseTransport(steamTransport);
            StartHost();
        }

        /// <summary>SteamLobby lobiye girince çağırır (host SteamID ile bağlanır).</summary>
        public void JoinSteam(string hostSteamId)
        {
            Begin(false);
            UseTransport(steamTransport);
            networkAddress = hostSteamId;
            StatusChanged?.Invoke(L.T("msg.joining"));
            StartClient();
        }

        public void Leave()
        {
            intentionalLeave = true;
            Time.timeScale = 1f;
            SteamLobby.Instance?.LeaveLobby();
            if (NetworkServer.active && NetworkClient.isConnected) StopHost();
            else if (NetworkClient.active) StopClient();
            else if (NetworkServer.active) StopServer();
        }

        /// <summary>Host: lobiden oyuna geç (herkes GameScene'e).</summary>
        public void StartGame()
        {
            if (!NetworkServer.active || string.IsNullOrEmpty(gameScene)) return;
            ServerChangeScene(gameScene);
        }

        /// <summary>Host: herkese menü sinematiğini oynat, bitince oyuna geç.</summary>
        public void StartGameWithCinematic(float duration)
        {
            if (!NetworkServer.active || string.IsNullOrEmpty(gameScene)) return;
            NetworkServer.SendToAll(new CinematicMessage());
            StartCoroutine(CinematicThenStart(duration));
        }

        IEnumerator CinematicThenStart(float duration)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, duration - 0.45f));
            if (NetworkServer.active) SceneFader.Get().FadeOut(0.4f, StartGame, L.T("msg.get_ready"));
        }

        /// <summary>Host: herkesi lobiye (Ana Menü) geri götür.</summary>
        public void ReturnToLobby()
        {
            if (!NetworkServer.active || IsSinglePlayer) return;
            foreach (var p in serverPlayers) if (p != null) p.ServerSetReady(false);
            ServerChangeScene(offlineScene);
        }

        /// <summary>Host: oyuncuyu lobiden at.</summary>
        public void Kick(FumblePlayer p)
        {
            if (!NetworkServer.active || p == null || p.isLocalPlayer) return;
            var conn = p.connectionToClient;
            p.TargetKicked(conn);
            StartCoroutine(DisconnectLater(conn));
        }

        IEnumerator DisconnectLater(NetworkConnectionToClient conn)
        {
            yield return new WaitForSecondsRealtime(0.25f);
            conn?.Disconnect();
        }

        /// <summary>Host bağlantıyı reddetti (sürüm farkı / lobi dolu); sebep PendingMenuMessage'da.</summary>
        public void MarkRejected() => intentionalLeave = true;

        public void MarkKicked()
        {
            intentionalLeave = true;
            PendingMenuMessage = L.T("msg.kicked");
        }

        // ---------------- Sunucu ----------------
        public override void OnServerSceneChanged(string sceneName)
        {
            base.OnServerSceneChanged(sceneName);
            var spawn = FindFirstObjectByType<ClimberSpawn>();
            if (spawn != null && climberPrefab != null && ClimberBody.Instance == null)
            {
                var go = Instantiate(climberPrefab, Vector3.zero, Quaternion.identity);
                go.GetComponent<ClimberBody>().ServerPlaceAt(spawn.transform.position);
                NetworkServer.Spawn(go);
            }
        }

        public override void OnServerConnect(NetworkConnectionToClient conn)
        {
            base.OnServerConnect(conn);
            if (serverPlayers.Count >= (IsSinglePlayer ? 1 : 4))
                StartCoroutine(DisconnectLater(conn));
        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            if (serverPlayers.Count >= (IsSinglePlayer ? 1 : 4))
            {
                conn.Disconnect();
                return;
            }
            var go = Instantiate(playerPrefab);
            var p = go.GetComponent<FumblePlayer>();
            p.slot = FreeSlot();
            go.name = $"FumblePlayer [{conn.connectionId}]";
            NetworkServer.AddPlayerForConnection(conn, go);
        }

        int FreeSlot()
        {
            for (int s = 0; s < 4; s++)
                if (!serverPlayers.Any(p => p != null && p.slot == s)) return s;
            return 0;
        }

        public void ServerRegister(FumblePlayer p)
        {
            if (!serverPlayers.Contains(p)) serverPlayers.Add(p);
            ReassignLimbs();
        }

        public void ServerUnregister(FumblePlayer p)
        {
            serverPlayers.Remove(p);
            ReassignLimbs();
        }

        void ReassignLimbs()
        {
            serverPlayers.RemoveAll(p => p == null);
            var ordered = serverPlayers.OrderBy(p => p.slot).ToList();
            var masks = LimbUtil.AssignMasks(ordered.Count);
            for (int i = 0; i < ordered.Count; i++) ordered[i].ServerSetMask(masks[i]);
        }

        public bool AllReady()
        {
            foreach (var p in FumblePlayer.All)
                if (p != null && !p.IsHostPlayer && !p.ready) return false;
            return true;
        }

        public override void OnStopServer()
        {
            serverPlayers.Clear();
            base.OnStopServer();
        }

        // ---------------- İstemci ----------------
        bool playerRequested;

        public override void OnClientConnect()
        {
            // onlineScene boş → taban sınıf burada AddPlayer gönderir
            playerRequested = autoCreatePlayer && !clientLoadedScene;
            base.OnClientConnect();
            wasConnected = true;
        }

        /// <summary>Sahne değişince oyuncuyu yalnızca daha önce istenmediyse iste (çift AddPlayer hatası).</summary>
        public override void OnClientSceneChanged()
        {
            if (NetworkClient.connection != null && NetworkClient.connection.isAuthenticated && !NetworkClient.ready)
                NetworkClient.Ready();
            if (autoCreatePlayer && NetworkClient.localPlayer == null && !playerRequested && NetworkClient.connection != null && NetworkClient.connection.isAuthenticated)
            {
                playerRequested = true;
                NetworkClient.AddPlayer();
            }
        }

        public override void OnClientDisconnect()
        {
            base.OnClientDisconnect();
            if (!intentionalLeave && !NetworkServer.active && string.IsNullOrEmpty(PendingMenuMessage))
                PendingMenuMessage = wasConnected ? L.T("msg.disconnected") : L.T("msg.unreachable");
        }

        public override void OnClientError(TransportError error, string reason)
        {
            base.OnClientError(error, reason);
            if (!intentionalLeave && string.IsNullOrEmpty(PendingMenuMessage))
                PendingMenuMessage = (wasConnected ? L.T("msg.disconnected") : L.T("msg.unreachable")) + "\n(" + reason + ")";
        }

        public override void OnClientChangeScene(string newSceneName, SceneOperation sceneOperation, bool customHandling)
        {
            base.OnClientChangeScene(newSceneName, sceneOperation, customHandling);
            SceneFader.Get().FadeOut(0.2f, null, newSceneName == gameScene ? L.T("msg.get_ready") : "");
        }

        public override void OnStopClient()
        {
            playerRequested = false;
            base.OnStopClient();
            SteamLobby.Instance?.LeaveLobby();
        }
    }

    /// <summary>Host → herkes: ana menüdeki "zirvede işerken düşme" sinematiğini oynat.</summary>
    public struct CinematicMessage : NetworkMessage { }
}
