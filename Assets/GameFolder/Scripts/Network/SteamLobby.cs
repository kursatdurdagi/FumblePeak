using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
#if FUMBLE_STEAM && FUMBLE_FIZZY
using Steamworks;
#endif

namespace FumblePeak
{
    public enum LobbyPrivacy { Friends = 0, Public = 1, InviteOnly = 2 }

    public struct FriendEntry
    {
        public ulong id;
        public string name;
        public ulong lobby;      // 0 = lobide değil
        public bool inOurGame;
    }

    public struct LobbyEntry
    {
        public ulong id;
        public string name;
        public int members;
        public int max;
    }

    /// <summary>
    /// Steam entegrasyonu: başlatma, lobi kur / katıl, gizlilik, arkadaş daveti (overlay),
    /// Steam arkadaş listesinden "Oyuna Katıl", rich presence, açık lobi listesi, avatarlar.
    /// Steam açık değilse her şey sessizce devre dışı kalır, oyun KCP (IP) ile çalışır.
    /// Test için proje kökünde steam_appid.txt (480 = Spacewar) bulunur.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class SteamLobby : MonoBehaviour
    {
        public static SteamLobby Instance { get; private set; }

        public const string HostAddressKey = "HostAddress";
        public const string GameKey = "fp";          // AppID 480 başka oyunlarla paylaşıldığı için işaret
        public const string VersionKey = "version";
        public const string NameKey = "name";

        public bool IsReady { get; private set; }
        public string PersonaName { get; private set; }
        public ulong MySteamId { get; private set; }
        public bool InLobby => lobbyId != 0;
        public string LobbyCode => lobbyId != 0 ? lobbyId.ToString() : "";
        public LobbyPrivacy Privacy { get; private set; } = LobbyPrivacy.Friends;

        /// <summary>Durum metni (bekleme ekranı için).</summary>
        public event Action<string> Status;
        /// <summary>Kullanıcıya gösterilecek hata (lokalize).</summary>
        public event Action<string> Error;
        public event Action<ulong> AvatarLoaded;

        ulong lobbyId;
        readonly Dictionary<ulong, Texture2D> avatars = new Dictionary<ulong, Texture2D>();

#if FUMBLE_STEAM && FUMBLE_FIZZY
        Callback<LobbyCreated_t> cbLobbyCreated;
        Callback<GameLobbyJoinRequested_t> cbJoinRequested;
        Callback<GameRichPresenceJoinRequested_t> cbRichJoin;
        Callback<LobbyEnter_t> cbLobbyEnter;
        Callback<AvatarImageLoaded_t> cbAvatar;
        CallResult<LobbyMatchList_t> crLobbyList;
        Action<List<LobbyEntry>> pendingListCallback;

        void Awake()
        {
            if (Instance != null && Instance != this) { enabled = false; return; }
            Instance = this;

            try { IsReady = SteamAPI.Init(); }
            catch (Exception e)
            {
                Debug.LogWarning("[Steam] Başlatılamadı: " + e.Message);
                IsReady = false;
            }

            if (!IsReady)
            {
                Debug.Log("[Steam] Steam çalışmıyor → yerel ağ (KCP) modu.");
                return;
            }

            PersonaName = SteamFriends.GetPersonaName();
            MySteamId = SteamUser.GetSteamID().m_SteamID;
            cbLobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            cbJoinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);
            cbRichJoin = Callback<GameRichPresenceJoinRequested_t>.Create(OnRichPresenceJoin);
            cbLobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEnter);
            cbAvatar = Callback<AvatarImageLoaded_t>.Create(OnAvatarLoaded);
            crLobbyList = CallResult<LobbyMatchList_t>.Create(OnLobbyList);

            var nm = GetComponent<FumbleNetworkManager>();
            var fizzy = GetComponent<Mirror.FizzySteam.FizzySteamworks>();
            if (fizzy == null) fizzy = gameObject.AddComponent<Mirror.FizzySteam.FizzySteamworks>();
            if (nm != null) nm.steamTransport = fizzy;

            Debug.Log("[Steam] Hazır: " + PersonaName);
        }

        void Start()
        {
            if (!IsReady) return;
            // Oyun kapalıyken davet kabul edildiyse: +connect_lobby <id>
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out var id))
                    JoinLobby(id);
        }

        void Update()
        {
            if (IsReady && Instance == this) SteamAPI.RunCallbacks();
        }

        void OnApplicationQuit()
        {
            if (IsReady && Instance == this)
            {
                LeaveLobby();
                SteamAPI.Shutdown();
                IsReady = false;
            }
        }

        static ELobbyType ToSteam(LobbyPrivacy p) => p switch
        {
            LobbyPrivacy.Public => ELobbyType.k_ELobbyTypePublic,
            LobbyPrivacy.InviteOnly => ELobbyType.k_ELobbyTypeInvisible,
            _ => ELobbyType.k_ELobbyTypeFriendsOnly,
        };

        // ---------------- Lobi ----------------
        public void HostLobby(LobbyPrivacy privacy)
        {
            if (!IsReady) return;
            Privacy = privacy;
            Status?.Invoke(L.T("msg.creating_steam"));
            SteamMatchmaking.CreateLobby(ToSteam(privacy), 4);
        }

        public void SetPrivacy(LobbyPrivacy privacy)
        {
            Privacy = privacy;
            if (IsReady && lobbyId != 0 && NetworkServer.active)
                SteamMatchmaking.SetLobbyType(new CSteamID(lobbyId), ToSteam(privacy));
        }

        public void JoinLobby(ulong id)
        {
            if (!IsReady) return;
            Status?.Invoke(L.T("msg.joining"));
            SteamMatchmaking.JoinLobby(new CSteamID(id));
        }

        public void InviteFriends()
        {
            if (IsReady && lobbyId != 0) SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(lobbyId));
        }

        public void LeaveLobby()
        {
            if (IsReady && lobbyId != 0)
            {
                SteamMatchmaking.LeaveLobby(new CSteamID(lobbyId));
                SteamFriends.ClearRichPresence();
            }
            lobbyId = 0;
        }

        /// <summary>Oyun başlayınca/lobiye dönünce lobiye girilebilirliği ayarla (oyun içine de katılınabilir).</summary>
        public void SetJoinable(bool joinable)
        {
            if (IsReady && lobbyId != 0 && NetworkServer.active)
                SteamMatchmaking.SetLobbyJoinable(new CSteamID(lobbyId), joinable);
        }

        void OnLobbyCreated(LobbyCreated_t cb)
        {
            if (cb.m_eResult != EResult.k_EResultOK)
            {
                Error?.Invoke(L.T("msg.create_failed", cb.m_eResult));
                return;
            }
            lobbyId = cb.m_ulSteamIDLobby;
            var lobby = new CSteamID(lobbyId);
            SteamMatchmaking.SetLobbyData(lobby, HostAddressKey, MySteamId.ToString());
            SteamMatchmaking.SetLobbyData(lobby, NameKey, PersonaName);
            SteamMatchmaking.SetLobbyData(lobby, GameKey, "1");
            SteamMatchmaking.SetLobbyData(lobby, VersionKey, Application.version);
            SteamMatchmaking.SetLobbyJoinable(lobby, true);
            SetRichPresence();
            FumbleNetworkManager.Instance.HostSteam();
        }

        void SetRichPresence()
        {
            if (lobbyId == 0) return;
            SteamFriends.SetRichPresence("connect", "+connect_lobby " + lobbyId);
            SteamFriends.SetRichPresence("status", "Fumble Peak");
        }

        void OnJoinRequested(GameLobbyJoinRequested_t cb) => RequestJoinFromOutside(cb.m_steamIDLobby.m_SteamID);

        void OnRichPresenceJoin(GameRichPresenceJoinRequested_t cb)
        {
            var parts = (cb.m_rgchConnect ?? "").Split(' ');
            for (int i = 0; i < parts.Length - 1; i++)
                if (parts[i] == "+connect_lobby" && ulong.TryParse(parts[i + 1], out var id))
                    RequestJoinFromOutside(id);
        }

        /// <summary>Steam arayüzünden (davet / arkadaş listesi) gelen katılma isteği.</summary>
        void RequestJoinFromOutside(ulong id)
        {
            if (NetworkClient.active || NetworkServer.active)
            {
                if (lobbyId == id) return;
                // başka bir oturumdayken davet: önce çık, sonra katıl
                FumbleNetworkManager.Instance.Leave();
            }
            JoinLobby(id);
        }

        void OnLobbyEnter(LobbyEnter_t cb)
        {
            if (NetworkServer.active) { lobbyId = cb.m_ulSteamIDLobby; return; } // host kendi lobisine girdi

            var lobby = new CSteamID(cb.m_ulSteamIDLobby);
            if (cb.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Error?.Invoke(cb.m_EChatRoomEnterResponse == (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseFull
                    ? L.T("msg.full") : L.T("msg.not_found"));
                return;
            }

            lobbyId = cb.m_ulSteamIDLobby;
            var host = SteamMatchmaking.GetLobbyData(lobby, HostAddressKey);
            var ver = SteamMatchmaking.GetLobbyData(lobby, VersionKey);
            if (string.IsNullOrEmpty(host))
            {
                LeaveLobby();
                Error?.Invoke(L.T("msg.not_found"));
                return;
            }
            if (!string.IsNullOrEmpty(ver) && ver != Application.version)
            {
                LeaveLobby();
                Error?.Invoke(L.T("msg.version", ver, Application.version));
                return;
            }
            SetRichPresence();
            FumbleNetworkManager.Instance.JoinSteam(host);
        }

        // ---------------- Arkadaşlar ----------------
        public List<FriendEntry> GetPlayingFriends()
        {
            var list = new List<FriendEntry>();
            if (!IsReady) return list;
            var myApp = SteamUtils.GetAppID();
            int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < count; i++)
            {
                var id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                if (!SteamFriends.GetFriendGamePlayed(id, out FriendGameInfo_t gi)) continue;
                if (gi.m_gameID.AppID() != myApp) continue;
                list.Add(new FriendEntry
                {
                    id = id.m_SteamID,
                    name = SteamFriends.GetFriendPersonaName(id),
                    lobby = gi.m_steamIDLobby.IsValid() ? gi.m_steamIDLobby.m_SteamID : 0,
                    inOurGame = true,
                });
            }
            list.Sort((a, b) => (b.lobby != 0).CompareTo(a.lobby != 0));
            return list;
        }

        // ---------------- Açık lobiler ----------------
        public void RequestPublicLobbies(Action<List<LobbyEntry>> callback)
        {
            if (!IsReady) { callback?.Invoke(new List<LobbyEntry>()); return; }
            pendingListCallback = callback;
            SteamMatchmaking.AddRequestLobbyListStringFilter(GameKey, "1", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter(VersionKey, Application.version, ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(30);
            var call = SteamMatchmaking.RequestLobbyList();
            crLobbyList.Set(call);
        }

        void OnLobbyList(LobbyMatchList_t r, bool ioFailure)
        {
            var list = new List<LobbyEntry>();
            if (!ioFailure)
            {
                for (int i = 0; i < r.m_nLobbiesMatching; i++)
                {
                    var id = SteamMatchmaking.GetLobbyByIndex(i);
                    list.Add(new LobbyEntry
                    {
                        id = id.m_SteamID,
                        name = SteamMatchmaking.GetLobbyData(id, NameKey),
                        members = SteamMatchmaking.GetNumLobbyMembers(id),
                        max = Mathf.Max(1, SteamMatchmaking.GetLobbyMemberLimit(id)),
                    });
                }
            }
            var cb = pendingListCallback;
            pendingListCallback = null;
            cb?.Invoke(list);
        }

        // ---------------- Avatarlar ----------------
        public Texture2D GetAvatar(ulong steamId)
        {
            if (!IsReady || steamId == 0) return null;
            if (avatars.TryGetValue(steamId, out var tex)) return tex;
            int handle = SteamFriends.GetMediumFriendAvatar(new CSteamID(steamId));
            if (handle <= 0) return null; // -1: yükleniyor (AvatarImageLoaded gelecek)
            tex = BuildTexture(handle);
            if (tex != null) avatars[steamId] = tex;
            return tex;
        }

        void OnAvatarLoaded(AvatarImageLoaded_t cb)
        {
            var tex = BuildTexture(cb.m_iImage);
            if (tex == null) return;
            avatars[cb.m_steamID.m_SteamID] = tex;
            AvatarLoaded?.Invoke(cb.m_steamID.m_SteamID);
        }

        static Texture2D BuildTexture(int handle)
        {
            if (!SteamUtils.GetImageSize(handle, out uint w, out uint h) || w == 0 || h == 0) return null;
            var buf = new byte[w * h * 4];
            if (!SteamUtils.GetImageRGBA(handle, buf, buf.Length)) return null;
            // Steam görüntüsü yukarıdan aşağı; Unity aşağıdan yukarı → satırları çevir
            var flipped = new byte[buf.Length];
            int row = (int)w * 4;
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(buf, y * row, flipped, ((int)h - 1 - y) * row, row);
            var tex = new Texture2D((int)w, (int)h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            tex.LoadRawTextureData(flipped);
            tex.Apply();
            return tex;
        }
#else
        void Awake()
        {
            if (Instance != null && Instance != this) { enabled = false; return; }
            Instance = this;
            IsReady = false;
            Debug.Log("[Steam] Steamworks.NET / FizzySteamworks yüklü değil → yerel ağ modu.");
        }
        public void HostLobby(LobbyPrivacy p) { }
        public void SetPrivacy(LobbyPrivacy p) { Privacy = p; }
        public void JoinLobby(ulong id) { }
        public void InviteFriends() { }
        public void LeaveLobby() { lobbyId = 0; }
        public void SetJoinable(bool j) { }
        public List<FriendEntry> GetPlayingFriends() => new List<FriendEntry>();
        public void RequestPublicLobbies(Action<List<LobbyEntry>> cb) => cb?.Invoke(new List<LobbyEntry>());
        public Texture2D GetAvatar(ulong id) => null;
#endif

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
