using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FumblePeak
{
    /// <summary>
    /// Ana menü (UI Toolkit + NextLocalization):
    ///  Giriş animasyonu → Ana sayfa (Tek oyuncu / Çok oyunculu / Ayarlar / Çıkış)
    ///  Çok oyunculu: Steam lobisi kur (gizlilik), kod ile katıl, IP ile kur/katıl,
    ///                Steam arkadaşları + açık lobiler listesi.
    ///  Lobi: 4 oyuncu yeri (avatar, isim, uzuvlar, hazır), davet, hazır ol, başlat, at, ayrıl.
    ///  Ayarlar: dil, isim, hassasiyet, tam ekran, çözünürlük, kalite, v-sync, ses.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuController : MonoBehaviour
    {
        static bool introPlayed;

        enum State { Intro, Menu, Busy, Lobby }
        State state;

        VisualElement root, intro, menu, lobby, modal, spinner, toastStack;
        VisualElement pageMain, pageMulti, pageSettings, current;
        VisualElement panelHowto, panelBrowser;
        Label pressAny, modalTitle, modalText, steamStatus, lanHint, myIp, nameSteamHint;
        Label lobbyMode, lobbyCode, lobbyCodeLabel, lobbyStatus, browserEmpty;
        VisualElement steamDot, lobbySlots;
        ScrollView browserList;
        Button modalPrimary, modalSecondary, btnHost, btnJoinCode, btnCopy, tabFriends, tabPublic;
        Button btnLobbyInvite, btnLobbyReady, btnLobbyStart;
        TextField fieldLobby, fieldIp, fieldName;
        DropdownField ddPrivacy, ddLobbyPrivacy, ddLanguage, ddResolution, ddQuality;
        MountainBackground bg;
        UILocalizer loc;

        Action modalPrimaryAction, modalSecondaryAction;
        bool waitingConnection;
        float busySince, spin, nextBrowserRefresh, nextLobbyRefresh;
        bool browserShowsPublic;
        readonly HashSet<uint> knownSlots = new HashSet<uint>();
        Resolution[] resolutions;

        FumbleNetworkManager NM => FumbleNetworkManager.Instance;
        SteamLobby Steam => SteamLobby.Instance;
        bool SteamOk => Steam != null && Steam.IsReady;

        // =====================================================================
        void OnEnable()
        {
            L.EnsureInit();
            root = GetComponent<UIDocument>().rootVisualElement;
            var inner = root.Q("root") ?? root;
            bg = new MountainBackground();
            inner.Insert(0, bg);

            Bind();
            loc = new UILocalizer(root);
            loc.Changed += OnLanguageChanged;

            RegisterEvents();
            SetupSettings();
            FillDynamicTexts();

            if (introPlayed) EnterMenu(true); else PlayIntro();
        }

        void OnDisable()
        {
            loc?.Dispose();
            FumblePlayer.RosterChanged -= OnRoster;
            FumblePlayer.PlayerJoined -= OnPlayerJoined;
            FumblePlayer.PlayerLeft -= OnPlayerLeft;
            if (NM != null) NM.StatusChanged -= OnStatus;
            if (Steam != null)
            {
                Steam.Status -= OnStatus;
                Steam.Error -= OnSteamError;
                Steam.AvatarLoaded -= OnAvatar;
            }
        }

        void Start()
        {
            UnityEngine.Cursor.visible = true;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            if (NM != null) NM.StatusChanged += OnStatus;
            if (Steam != null)
            {
                Steam.Status += OnStatus;
                Steam.Error += OnSteamError;
                Steam.AvatarLoaded += OnAvatar;
            }
            RefreshSteamStatus();

            // Oyundan / bağlantıdan dönüş mesajı
            if (!string.IsNullOrEmpty(FumbleNetworkManager.PendingMenuMessage) && !NetworkClient.active)
            {
                ShowMessage(L.T("msg.connection"), FumbleNetworkManager.PendingMenuMessage);
                FumbleNetworkManager.PendingMenuMessage = null;
            }
        }

        void Bind()
        {
            intro = root.Q("intro");
            menu = root.Q("menu");
            lobby = root.Q("lobby");
            modal = root.Q("modal");
            spinner = root.Q("spinner");
            toastStack = root.Q("toast-stack");
            toastStack.pickingMode = PickingMode.Ignore;
            pressAny = root.Q<Label>("press-any");
            modalTitle = root.Q<Label>("modal-title");
            modalText = root.Q<Label>("modal-text");
            modalPrimary = root.Q<Button>("modal-btn-primary");
            modalSecondary = root.Q<Button>("modal-btn-secondary");
            steamStatus = root.Q<Label>("steam-status");
            steamDot = root.Q("steam-dot");
            root.Q<Label>("version").text = "v" + Application.version;

            pageMain = root.Q("page-main");
            pageMulti = root.Q("page-multi");
            pageSettings = root.Q("page-settings");
            current = pageMain;
            panelHowto = root.Q("panel-howto");
            panelBrowser = root.Q("panel-browser");
            browserList = root.Q<ScrollView>("browser-list");
            browserEmpty = root.Q<Label>("browser-empty");
            tabFriends = root.Q<Button>("tab-friends");
            tabPublic = root.Q<Button>("tab-public");

            btnHost = root.Q<Button>("btn-host");
            btnJoinCode = root.Q<Button>("btn-join-code");
            fieldLobby = root.Q<TextField>("field-lobby");
            fieldIp = root.Q<TextField>("field-ip");
            fieldIp.value = PlayerProfile.LastIp;
            lanHint = root.Q<Label>("lan-hint");
            myIp = root.Q<Label>("lan-myip");
            ddPrivacy = root.Q<DropdownField>("dd-privacy");

            fieldName = root.Q<TextField>("field-name");
            nameSteamHint = root.Q<Label>("name-steam-hint");
            ddLanguage = root.Q<DropdownField>("dd-language");
            ddResolution = root.Q<DropdownField>("dd-resolution");
            ddQuality = root.Q<DropdownField>("dd-quality");

            lobbyMode = root.Q<Label>("lobby-mode");
            lobbyCode = root.Q<Label>("lobby-code");
            lobbyCodeLabel = root.Q<Label>("lobby-code-label");
            lobbyStatus = root.Q<Label>("lobby-status");
            lobbySlots = root.Q("lobby-slots");
            ddLobbyPrivacy = root.Q<DropdownField>("dd-lobby-privacy");
            btnCopy = root.Q<Button>("btn-copy");
            btnLobbyInvite = root.Q<Button>("btn-lobby-invite");
            btnLobbyReady = root.Q<Button>("btn-lobby-ready");
            btnLobbyStart = root.Q<Button>("btn-lobby-start");
        }

        void RegisterEvents()
        {
            root.Q<Button>("btn-single").clicked += OnSingle;
            root.Q<Button>("btn-multi").clicked += () => ShowPage(pageMulti);
            root.Q<Button>("btn-settings").clicked += () => ShowPage(pageSettings);
            root.Q<Button>("btn-quit").clicked += AskQuit;
            root.Q<Button>("btn-back-multi").clicked += () => ShowPage(pageMain);
            root.Q<Button>("btn-back-settings").clicked += () => { SaveSettings(); ShowPage(pageMain); };

            btnHost.clicked += OnHost;
            btnJoinCode.clicked += OnJoinCode;
            root.Q<Button>("btn-lan-host").clicked += OnHostLan;
            root.Q<Button>("btn-lan-join").clicked += OnJoinIp;
            tabFriends.clicked += () => { browserShowsPublic = false; RefreshBrowser(true); };
            tabPublic.clicked += () => { browserShowsPublic = true; RefreshBrowser(true); };
            root.Q<Button>("btn-refresh").clicked += () => RefreshBrowser(true);

            btnCopy.clicked += () => { GUIUtility.systemCopyBuffer = lobbyCode.text; Toast(L.T("lobby.copied")); };
            root.Q<Button>("btn-lobby-leave").clicked += AskLeaveLobby;
            btnLobbyInvite.clicked += () => Steam?.InviteFriends();
            btnLobbyReady.clicked += () => { var me = FumblePlayer.Local; if (me != null) me.SetReady(!me.ready); };
            btnLobbyStart.clicked += OnStartGame;
            ddLobbyPrivacy.RegisterValueChangedCallback(e =>
            {
                if (e.target != ddLobbyPrivacy) return; // etiket metni değişimi (lokalizasyon) yukarı kabarcıklanır
                if (ddLobbyPrivacy.index >= 0) Steam?.SetPrivacy((LobbyPrivacy)ddLobbyPrivacy.index);
            });

            modalPrimary.clicked += () => { var a = modalPrimaryAction; HideModal(); a?.Invoke(); };
            modalSecondary.clicked += () => { var a = modalSecondaryAction; HideModal(); a?.Invoke(); };

            root.RegisterCallback<PointerMoveEvent>(e =>
            {
                float w = root.resolvedStyle.width;
                if (w > 1) bg.Parallax = e.position.x / w - 0.5f;
            });

            FumblePlayer.RosterChanged += OnRoster;
            FumblePlayer.PlayerJoined += OnPlayerJoined;
            FumblePlayer.PlayerLeft += OnPlayerLeft;
        }

        // =====================================================================
        // Dinamik (koddan) metinler
        void FillDynamicTexts()
        {
            var port = NM != null ? NM.LocalPort : (ushort)7777;
            lanHint.text = L.T("mp.lan_hint", port);
            myIp.text = L.T("mp.my_ip", LocalIp());
            btnHost.text = SteamOk ? L.T("mp.host_steam") : L.T("mp.host");

            var privacy = new List<string> { L.T("mp.privacy_friends"), L.T("mp.privacy_public"), L.T("mp.privacy_invite") };
            int pi = Mathf.Max(0, ddPrivacy.index);
            ddPrivacy.choices = privacy;
            ddPrivacy.index = pi;
            int lpi = Steam != null ? (int)Steam.Privacy : 0;
            ddLobbyPrivacy.choices = privacy;
            ddLobbyPrivacy.SetValueWithoutNotify(privacy[lpi]);

            RefreshSteamStatus();
            if (state == State.Lobby) RebuildLobby();
            if (!panelBrowser.ClassListContains("panel--hidden")) RefreshBrowser(false);
        }

        void OnLanguageChanged()
        {
            FillDynamicTexts();
            RefreshLanguageDropdown();
        }

        void RefreshSteamStatus()
        {
            bool ok = SteamOk;
            steamDot.EnableInClassList("dot--ok", ok);
            steamDot.EnableInClassList("dot--off", !ok);
            steamStatus.text = ok ? L.T("menu.steam_ok", Steam.PersonaName) : L.T("menu.steam_off");
            btnJoinCode.SetEnabled(ok);
            fieldLobby.SetEnabled(ok);
            ddPrivacy.SetEnabled(ok);
            fieldName.SetEnabled(!ok);
            nameSteamHint.style.display = ok ? DisplayStyle.Flex : DisplayStyle.None;
            fieldName.SetValueWithoutNotify(PlayerProfile.Name);
        }

        // =====================================================================
        // Giriş
        void PlayIntro()
        {
            state = State.Intro;
            menu.AddToClassList("menu--gone");
            var studio = root.Q("intro-studio");
            var logo = root.Q("intro-logo");
            var tag = root.Q("intro-tagline");
            root.schedule.Execute(() => studio.AddToClassList("is-in")).StartingIn(250);
            root.schedule.Execute(() => logo.AddToClassList("is-in")).StartingIn(900);
            root.schedule.Execute(() => tag.AddToClassList("is-in")).StartingIn(1900);
            root.schedule.Execute(() => pressAny.AddToClassList("is-in")).StartingIn(2500);
            root.schedule.Execute(() => { if (state == State.Intro) pressAny.ToggleInClassList("press-any--dim"); }).StartingIn(2600).Every(600);
            root.schedule.Execute(() => { if (state == State.Intro) EnterMenu(false); }).StartingIn(7000);
        }

        void EnterMenu(bool instant)
        {
            introPlayed = true;
            state = State.Menu;
            menu.RemoveFromClassList("menu--gone");
            if (instant)
            {
                intro.style.display = DisplayStyle.None;
                menu.RemoveFromClassList("menu--hidden");
                return;
            }
            intro.AddToClassList("intro--out");
            root.schedule.Execute(() => menu.RemoveFromClassList("menu--hidden")).StartingIn(350);
            root.schedule.Execute(() => intro.style.display = DisplayStyle.None).StartingIn(900);
        }

        // =====================================================================
        void Update()
        {
            var kb = Keyboard.current;
            if (state == State.Intro)
            {
                bool any = (kb != null && kb.anyKey.wasPressedThisFrame)
                           || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                           || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
                if (any && Time.timeSinceLevelLoad > 0.8f) EnterMenu(false);
                if (NetworkClient.active) EnterMenu(true); // davetle açıldıysa girişi atla
                return;
            }

            if (kb != null && kb.escapeKey.wasPressedThisFrame) OnEscape();

            // Spinner
            if (!modal.ClassListContains("modal--hidden") && !spinner.ClassListContains("spinner--hidden"))
            {
                spin = (spin + Time.unscaledDeltaTime * 360f) % 360f;
                spinner.style.rotate = new Rotate(spin);
            }

            bool connected = NetworkClient.isConnected && FumblePlayer.Local != null;
            bool inGame = NM != null && NM.IsInGameScene;

            // Bağlandık → lobi ekranı
            if (connected && !inGame && state != State.Lobby && !FumbleNetworkManager.IsSinglePlayer)
                ShowLobby();

            // Lobideyken bağlantı koptu
            if (state == State.Lobby && !NetworkClient.active)
            {
                HideLobby();
                if (!string.IsNullOrEmpty(FumbleNetworkManager.PendingMenuMessage))
                {
                    ShowMessage(L.T("msg.connection"), FumbleNetworkManager.PendingMenuMessage);
                    FumbleNetworkManager.PendingMenuMessage = null;
                }
            }

            // IP bağlantısı başarısız
            if (state == State.Busy && waitingConnection && Time.unscaledTime - busySince > 1f && !NetworkClient.active && !NetworkServer.active)
            {
                waitingConnection = false;
                SceneFader.Get().FadeIn(0.3f);
                ShowMessage(L.T("msg.failed"), FumbleNetworkManager.PendingMenuMessage ?? L.T("msg.unreachable"));
                FumbleNetworkManager.PendingMenuMessage = null;
            }

            if (state == State.Lobby && Time.unscaledTime > nextLobbyRefresh) RebuildLobby();

            if (state == State.Menu && current == pageMulti && Time.unscaledTime > nextBrowserRefresh)
                RefreshBrowser(false);
        }

        void OnEscape()
        {
            if (!modal.ClassListContains("modal--hidden"))
            {
                if (state != State.Busy) HideModal();
                return;
            }
            if (state == State.Lobby) { AskLeaveLobby(); return; }
            if (current == pageSettings) SaveSettings();
            if (current != pageMain) ShowPage(pageMain);
        }

        void ShowPage(VisualElement page)
        {
            if (page == current) return;
            current.AddToClassList("page--hidden");
            page.RemoveFromClassList("page--hidden");
            page.AddToClassList("page--enter");
            page.schedule.Execute(() => page.RemoveFromClassList("page--enter")).StartingIn(20);
            current = page;

            bool multi = page == pageMulti;
            panelBrowser.EnableInClassList("panel--hidden", !multi);
            panelHowto.EnableInClassList("panel--hidden", multi);
            if (multi) RefreshBrowser(true);
        }

        // =====================================================================
        // Eylemler
        void OnSingle()
        {
            ShowBusy(L.T("menu.single"), L.T("menu.single_desc"), false);
            SceneFader.Get().FadeOut(0.5f, () => NM.StartSinglePlayer(), L.T("msg.octopus"));
        }

        void OnHost()
        {
            if (SteamOk)
            {
                ShowBusy(L.T("msg.creating"), L.T("msg.creating_steam"), true);
                Steam.HostLobby((LobbyPrivacy)Mathf.Max(0, ddPrivacy.index));
            }
            else OnHostLan();
        }

        void OnHostLan()
        {
            NM.HostLocal();
        }

        void OnJoinCode()
        {
            var code = fieldLobby.value?.Trim();
            if (!ulong.TryParse(code, out var id))
            {
                ShowMessage(L.T("msg.failed"), L.T("msg.invalid_code"));
                return;
            }
            JoinSteamLobby(id);
        }

        void JoinSteamLobby(ulong id)
        {
            ShowBusy(L.T("msg.connecting"), L.T("msg.joining"), true);
            Steam.JoinLobby(id);
        }

        void OnJoinIp()
        {
            var ip = string.IsNullOrWhiteSpace(fieldIp.value) ? "localhost" : fieldIp.value.Trim();
            PlayerProfile.LastIp = ip;
            ShowBusy(L.T("msg.connecting"), L.T("msg.connecting_to", ip), true);
            waitingConnection = true;
            busySince = Time.unscaledTime;
            NM.JoinLocal(ip);
        }

        void CancelBusy()
        {
            waitingConnection = false;
            NM.Leave();
            SceneFader.Get().FadeIn(0.3f);
            state = State.Menu;
        }

        void OnStatus(string s)
        {
            if (state == State.Busy) modalText.text = s;
        }

        void OnSteamError(string err)
        {
            waitingConnection = false;
            SceneFader.Get().FadeIn(0.3f);
            ShowMessage(L.T("msg.connection"), err);
        }

        void AskQuit()
        {
            ShowConfirm(L.T("msg.quit_title"), L.T("msg.quit_confirm"), L.T("msg.yes"), L.T("msg.no"), () =>
            {
                SceneFader.Get().FadeOut(0.4f, () =>
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                });
            });
        }

        // =====================================================================
        // Lobi
        void ShowLobby()
        {
            HideModal();
            waitingConnection = false;
            state = State.Lobby;
            knownSlots.Clear();
            menu.AddToClassList("menu--gone");
            lobby.RemoveFromClassList("lobby--hidden");
            lobby.AddToClassList("lobby--enter");
            lobby.schedule.Execute(() => lobby.RemoveFromClassList("lobby--enter")).StartingIn(20);
            SceneFader.Get().FadeIn(0.3f);
            RebuildLobby();
        }

        void HideLobby()
        {
            state = State.Menu;
            lobby.AddToClassList("lobby--hidden");
            menu.RemoveFromClassList("menu--gone");
            menu.RemoveFromClassList("menu--hidden");
            ShowPage(pageMulti);
        }

        void AskLeaveLobby()
        {
            bool host = NetworkServer.active;
            ShowConfirm(L.T("msg.leave_title"), host ? L.T("msg.leave_host") : L.T("msg.leave_client"), L.T("msg.yes"), L.T("msg.no"), () =>
            {
                NM.Leave();
                HideLobby();
            });
        }

        void OnStartGame()
        {
            if (!NetworkServer.active || !NM.AllReady()) return;
            btnLobbyStart.SetEnabled(false);
            SceneFader.Get().FadeOut(0.5f, () => NM.StartGame(), L.T("msg.get_ready"));
        }

        void OnRoster()
        {
            if (state == State.Lobby) RebuildLobby();
        }

        void OnAvatar(ulong id)
        {
            if (state == State.Lobby) RebuildLobby();
            else if (current == pageMulti) RefreshBrowser(false);
        }

        void OnPlayerJoined(string name) { if (state == State.Lobby) Toast(L.T("lobby.joined", name)); }
        void OnPlayerLeft(string name) { if (state == State.Lobby) Toast(L.T("lobby.left", name)); }

        void RebuildLobby()
        {
            nextLobbyRefresh = Time.unscaledTime + 1f;
            bool host = NetworkServer.active;
            var players = FumblePlayer.All.Where(p => p != null).OrderBy(p => p.slot).ToList();
            var me = FumblePlayer.Local;

            // Başlık
            int n = Mathf.Clamp(players.Count, 1, 4);
            lobbyMode.text = L.T("lobby.mode", L.T("lobby.mode" + n));

            bool steamLobby = Steam != null && Steam.InLobby;
            if (steamLobby)
            {
                lobbyCodeLabel.text = L.T("lobby.code");
                lobbyCode.text = Steam.LobbyCode;
            }
            else
            {
                lobbyCodeLabel.text = L.T("lobby.address");
                lobbyCode.text = host ? $"{LocalIp()}:{NM.LocalPort}" : NM.networkAddress;
            }
            btnLobbyInvite.style.display = steamLobby ? DisplayStyle.Flex : DisplayStyle.None;
            ddLobbyPrivacy.style.display = steamLobby && host ? DisplayStyle.Flex : DisplayStyle.None;

            // Butonlar
            btnLobbyStart.style.display = host ? DisplayStyle.Flex : DisplayStyle.None;
            btnLobbyReady.style.display = host ? DisplayStyle.None : DisplayStyle.Flex;
            bool allReady = NM.AllReady();
            btnLobbyStart.SetEnabled(allReady);
            if (me != null) btnLobbyReady.text = me.ready ? L.T("lobby.unready") : L.T("lobby.ready");
            lobbyStatus.text = host ? (allReady ? L.T("lobby.all_ready") : L.T("lobby.wait_players"))
                                    : (me != null && me.ready ? L.T("lobby.wait_host") : L.T("lobby.wait_players"));

            // Oyuncu yerleri
            lobbySlots.Clear();
            for (int s = 0; s < 4; s++)
            {
                var p = players.FirstOrDefault(x => x.slot == s);
                lobbySlots.Add(p != null ? BuildSlot(p, host) : BuildEmptySlot(steamLobby));
            }
        }

        VisualElement BuildSlot(FumblePlayer p, bool viewerIsHost)
        {
            var slot = new VisualElement();
            slot.AddToClassList("slot");
            bool ready = p.IsHostPlayer || p.ready;
            slot.EnableInClassList("slot--ready", ready);
            if (!knownSlots.Contains(p.netId))
            {
                knownSlots.Add(p.netId);
                slot.AddToClassList("slot--new");
                slot.schedule.Execute(() => slot.RemoveFromClassList("slot--new")).StartingIn(30);
            }

            var stripe = new VisualElement();
            stripe.AddToClassList("slot__stripe");
            stripe.style.backgroundColor = p.Color;
            slot.Add(stripe);

            var avatar = new VisualElement();
            avatar.AddToClassList("slot__avatar");
            avatar.style.borderTopColor = avatar.style.borderBottomColor = avatar.style.borderLeftColor = avatar.style.borderRightColor = p.Color;
            var tex = Steam != null ? Steam.GetAvatar(p.steamId) : null;
            if (tex != null) avatar.style.backgroundImage = new StyleBackground(tex);
            else
            {
                avatar.style.backgroundColor = p.Color;
                var initial = new Label(string.IsNullOrEmpty(p.playerName) ? "?" : p.playerName.Substring(0, 1).ToUpperInvariant());
                initial.AddToClassList("slot__initial");
                avatar.Add(initial);
            }
            slot.Add(avatar);

            var name = new Label(p.playerName);
            name.AddToClassList("slot__name");
            slot.Add(name);

            var badges = new VisualElement();
            badges.AddToClassList("slot__badges");
            if (p.IsHostPlayer) badges.Add(Badge(L.T("lobby.host"), false));
            if (p.isLocalPlayer) badges.Add(Badge(L.T("lobby.you"), true));
            slot.Add(badges);

            var limbs = new Label(LimbUtil.Describe(p.limbMask));
            limbs.AddToClassList("slot__limbs");
            slot.Add(limbs);

            var st = new Label(ready ? L.T("lobby.state_ready") : L.T("lobby.state_waiting"));
            st.AddToClassList("slot__state");
            st.EnableInClassList("slot__state--ready", ready);
            slot.Add(st);

            if (viewerIsHost && !p.isLocalPlayer)
            {
                var kick = new Button(() => NM.Kick(p)) { text = L.T("lobby.kick") };
                kick.AddToClassList("small-btn");
                kick.AddToClassList("slot__kick");
                slot.Add(kick);
            }
            return slot;
        }

        VisualElement BuildEmptySlot(bool canInvite)
        {
            var slot = new VisualElement();
            slot.AddToClassList("slot");
            slot.AddToClassList("slot--empty");
            var plus = new Label("+");
            plus.AddToClassList("slot__plus");
            slot.Add(plus);
            var t = new Label(L.T("lobby.empty"));
            t.AddToClassList("slot__empty-title");
            slot.Add(t);
            if (canInvite)
            {
                var h = new Label(L.T("lobby.empty_hint"));
                h.AddToClassList("slot__empty-hint");
                slot.Add(h);
                slot.RegisterCallback<ClickEvent>(_ => Steam?.InviteFriends());
            }
            return slot;
        }

        static Label Badge(string text, bool you)
        {
            var b = new Label(text);
            b.AddToClassList("badge");
            if (you) b.AddToClassList("badge--you");
            return b;
        }

        // =====================================================================
        // Steam arkadaşları / açık lobiler
        void RefreshBrowser(bool force)
        {
            nextBrowserRefresh = Time.unscaledTime + 6f;
            tabFriends.EnableInClassList("tab--active", !browserShowsPublic);
            tabPublic.EnableInClassList("tab--active", browserShowsPublic);

            if (!SteamOk)
            {
                browserList.Clear();
                browserEmpty.text = L.T("mp.nosteam");
                browserEmpty.style.display = DisplayStyle.Flex;
                return;
            }

            if (!browserShowsPublic)
            {
                var friends = Steam.GetPlayingFriends();
                browserList.Clear();
                foreach (var f in friends)
                {
                    ulong lobbyId = f.lobby;
                    browserList.Add(Entry(Steam.GetAvatar(f.id), f.name,
                        lobbyId != 0 ? L.T("mp.friend_in_lobby") : L.T("mp.friend_playing"), lobbyId != 0,
                        lobbyId != 0 ? () => JoinSteamLobby(lobbyId) : (Action)null));
                }
                browserEmpty.text = L.T("mp.friends_empty");
                browserEmpty.style.display = friends.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            else
            {
                if (force)
                {
                    browserList.Clear();
                    browserEmpty.text = L.T("mp.loading");
                    browserEmpty.style.display = DisplayStyle.Flex;
                }
                Steam.RequestPublicLobbies(list =>
                {
                    if (!browserShowsPublic) return;
                    browserList.Clear();
                    foreach (var lob in list)
                    {
                        ulong id = lob.id;
                        browserList.Add(Entry(null, string.IsNullOrEmpty(lob.name) ? "Fumble Peak" : lob.name,
                            L.T("mp.players", lob.members, lob.max), true, () => JoinSteamLobby(id)));
                    }
                    browserEmpty.text = L.T("mp.public_empty");
                    browserEmpty.style.display = list.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                });
            }
        }

        VisualElement Entry(Texture2D avatar, string title, string sub, bool live, Action join)
        {
            var e = new VisualElement();
            e.AddToClassList("entry");
            var av = new VisualElement();
            av.AddToClassList("entry__avatar");
            if (avatar != null) av.style.backgroundImage = new StyleBackground(avatar);
            e.Add(av);
            var txt = new VisualElement();
            txt.AddToClassList("entry__text");
            var t = new Label(title); t.AddToClassList("entry__name");
            var s = new Label(sub); s.AddToClassList("entry__sub"); s.EnableInClassList("entry__sub--live", live);
            txt.Add(t); txt.Add(s);
            e.Add(txt);
            if (join != null)
            {
                var b = new Button(join) { text = L.T("mp.join") };
                b.AddToClassList("small-btn");
                b.AddToClassList("small-btn--accent");
                e.Add(b);
            }
            return e;
        }

        // =====================================================================
        // Ayarlar
        void SetupSettings()
        {
            RefreshLanguageDropdown();
            ddLanguage.RegisterValueChangedCallback(e =>
            {
                if (e.target != ddLanguage) return; // etiket metni değişimi (lokalizasyon) yukarı kabarcıklanır
                var langs = Loc.AvailableLanguages;
                int i = ddLanguage.index;
                if (i >= 0 && i < langs.Length && langs[i] != Loc.CurrentLanguage) Loc.SetLanguage(langs[i]);
            });

            fieldName.SetValueWithoutNotify(PlayerProfile.Name);
            fieldName.RegisterCallback<FocusOutEvent>(_ => { if (!SteamOk) PlayerProfile.Name = fieldName.value; });

            var sens = root.Q<Slider>("slider-sens");
            sens.SetValueWithoutNotify(PlayerProfile.Sensitivity);
            sens.RegisterValueChangedCallback(e => PlayerProfile.Sensitivity = e.newValue);

            var vol = root.Q<Slider>("slider-volume");
            vol.SetValueWithoutNotify(PlayerProfile.Volume);
            vol.RegisterValueChangedCallback(e => PlayerProfile.Volume = e.newValue);

            var fs = root.Q<Toggle>("toggle-fullscreen");
            fs.SetValueWithoutNotify(Screen.fullScreen);
            fs.RegisterValueChangedCallback(e => Screen.fullScreen = e.newValue);

            var vs = root.Q<Toggle>("toggle-vsync");
            vs.SetValueWithoutNotify(PlayerProfile.VSync);
            vs.RegisterValueChangedCallback(e => PlayerProfile.VSync = e.newValue);

            ddQuality.choices = QualitySettings.names.ToList();
            ddQuality.index = QualitySettings.GetQualityLevel();
            ddQuality.RegisterValueChangedCallback(e => { if (e.target == ddQuality && ddQuality.index >= 0) PlayerProfile.Quality = ddQuality.index; });

            resolutions = Screen.resolutions
                .GroupBy(r => (r.width, r.height)).Select(g => g.Last())
                .OrderByDescending(r => r.width).ThenByDescending(r => r.height).ToArray();
            ddResolution.choices = resolutions.Select(r => $"{r.width} × {r.height}").ToList();
            int cur = Array.FindIndex(resolutions, r => r.width == Screen.width && r.height == Screen.height);
            if (cur >= 0) ddResolution.index = cur;
            ddResolution.RegisterValueChangedCallback(e =>
            {
                if (e.target != ddResolution) return;
                int i = ddResolution.index;
                if (i >= 0 && i < resolutions.Length) Screen.SetResolution(resolutions[i].width, resolutions[i].height, Screen.fullScreenMode);
            });
        }

        void RefreshLanguageDropdown()
        {
            var langs = Loc.AvailableLanguages;
            ddLanguage.choices = langs.Select(L.Native).ToList();
            ddLanguage.SetValueWithoutNotify(L.Native(Loc.CurrentLanguage));
        }

        void SaveSettings()
        {
            if (!SteamOk) PlayerProfile.Name = fieldName.value;
            PlayerPrefs.Save();
        }

        // =====================================================================
        // Modal & bildirim
        void ShowBusy(string title, string text, bool cancellable)
        {
            state = State.Busy;
            busySince = Time.unscaledTime;
            modalTitle.text = title;
            modalText.text = text;
            spinner.RemoveFromClassList("spinner--hidden");
            SetModalButtons(cancellable ? L.T("msg.cancel") : null, CancelBusy, null, null);
            modal.RemoveFromClassList("modal--hidden");
        }

        void ShowMessage(string title, string text)
        {
            if (state == State.Busy) state = State.Menu;
            modalTitle.text = title;
            modalText.text = text;
            spinner.AddToClassList("spinner--hidden");
            SetModalButtons(L.T("msg.ok"), null, null, null);
            modal.RemoveFromClassList("modal--hidden");
        }

        void ShowConfirm(string title, string text, string yes, string no, Action onYes)
        {
            modalTitle.text = title;
            modalText.text = text;
            spinner.AddToClassList("spinner--hidden");
            SetModalButtons(yes, onYes, no, null);
            modal.RemoveFromClassList("modal--hidden");
        }

        void SetModalButtons(string primary, Action primaryAction, string secondary, Action secondaryAction)
        {
            modalPrimaryAction = primaryAction;
            modalSecondaryAction = secondaryAction;
            modalPrimary.style.display = primary != null ? DisplayStyle.Flex : DisplayStyle.None;
            modalSecondary.style.display = secondary != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (primary != null) modalPrimary.text = primary;
            if (secondary != null) modalSecondary.text = secondary;
        }

        void HideModal()
        {
            if (state == State.Busy) state = State.Menu;
            modal.AddToClassList("modal--hidden");
        }

        void Toast(string text)
        {
            var t = new Label(text);
            t.AddToClassList("toast-item");
            t.AddToClassList("toast-item--hidden");
            t.pickingMode = PickingMode.Ignore;
            toastStack.Add(t);
            t.schedule.Execute(() => t.RemoveFromClassList("toast-item--hidden")).StartingIn(20);
            t.schedule.Execute(() => t.AddToClassList("toast-item--hidden")).StartingIn(3000);
            t.schedule.Execute(() => t.RemoveFromHierarchy()).StartingIn(3400);
        }

        static string cachedIp;
        static string LocalIp()
        {
            if (cachedIp != null) return cachedIp;
            cachedIp = ResolveIp();
            return cachedIp;
        }

        static string ResolveIp()
        {
            try
            {
                var ip = Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
                return ip != null ? ip.ToString() : "127.0.0.1";
            }
            catch { return "127.0.0.1"; }
        }
    }
}
