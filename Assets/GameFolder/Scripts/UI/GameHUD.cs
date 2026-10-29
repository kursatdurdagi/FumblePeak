using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FumblePeak
{
    /// <summary>
    /// Oyun içi arayüz (UI Toolkit + NextLocalization). GDD gereği can/stamina barı yok:
    /// sadece kim hangi uzvu yönetiyor, bildirimler ve duraklat menüsü.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class GameHUD : MonoBehaviour
    {
        VisualElement root, roster, toast, summit, pause, lobbyRow;
        Label toastText, controls, lobbyCode, lobbyLabel, pauseHint;
        Button btnInvite, btnLobby;
        IVisualElementScheduledItem toastHide;
        UILocalizer loc;
        bool paused, single;

        void OnEnable()
        {
            L.EnsureInit();
            root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.Q("hud").pickingMode = PickingMode.Ignore;
            roster = root.Q("roster");
            toast = root.Q("toast");
            toastText = root.Q<Label>("toast-text");
            summit = root.Q("summit");
            pause = root.Q("pause");
            controls = root.Q<Label>("controls");
            lobbyCode = root.Q<Label>("lobby-code");
            lobbyLabel = root.Q<Label>("lobby-label");
            lobbyRow = root.Q("lobby-row");
            pauseHint = root.Q<Label>("pause-hint");
            btnInvite = root.Q<Button>("btn-invite");
            btnLobby = root.Q<Button>("btn-lobby");
            // Eski uzuv paneli ve eğitim sekmesi artık yok (sürükle-bırak kontroller)
            root.Q("limb-panel")?.RemoveFromHierarchy();
            root.Q("tutorial")?.RemoveFromHierarchy();
            single = FumbleNetworkManager.IsSinglePlayer;

            loc = new UILocalizer(root);
            loc.Changed += RefreshTexts;

            root.Q<Button>("btn-resume").clicked += () => SetPaused(false);
            btnInvite.clicked += () => SteamLobby.Instance?.InviteFriends();
            root.Q<Button>("btn-copy").clicked += () => { GUIUtility.systemCopyBuffer = lobbyCode.text; ShowToast(L.T("lobby.copied")); };
            root.Q<Button>("btn-respawn").clicked += () => { FumblePlayer.Local?.RequestRespawn(); SetPaused(false); };
            btnLobby.clicked += ReturnToLobby;
            root.Q<Button>("btn-leave").clicked += Leave;

            foreach (var l in new[] { roster, toast, summit, controls }) l.pickingMode = PickingMode.Ignore;
            toast.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
            summit.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);

            RefreshTexts();

            FumblePlayer.RosterChanged += RebuildRoster;
            FumblePlayer.PlayerJoined += OnJoined;
            FumblePlayer.PlayerLeft += OnLeft;
            ClimberBody.OnRespawned += OnRespawned;
            ClimberBody.OnCheckpointReached += OnCheckpoint;
            ClimberBody.OnSummitReached += OnSummit;
            ClimberBody.OnLimbGaveUp += OnGaveUp;
            ClimberBody.OnLimbSlipped += OnSlipped;
            ClimberBody.OnHoldCrumbled += OnCrumbled;
            PestController.Event += OnPest;

            root.schedule.Execute(() => controls.AddToClassList("controls--faded")).StartingIn(14000);
            root.schedule.Execute(RebuildRoster).StartingIn(300);
            ShowToast(single ? L.T("hud.biome1") + " · " + L.T("msg.octopus") : L.T("hud.biome1"), 3f);
        }

        void OnDisable()
        {
            loc?.Dispose();
            FumblePlayer.RosterChanged -= RebuildRoster;
            FumblePlayer.PlayerJoined -= OnJoined;
            FumblePlayer.PlayerLeft -= OnLeft;
            ClimberBody.OnRespawned -= OnRespawned;
            ClimberBody.OnCheckpointReached -= OnCheckpoint;
            ClimberBody.OnSummitReached -= OnSummit;
            ClimberBody.OnLimbGaveUp -= OnGaveUp;
            ClimberBody.OnLimbSlipped -= OnSlipped;
            ClimberBody.OnHoldCrumbled -= OnCrumbled;
            PestController.Event -= OnPest;
            FumblePlayer.InputBlocked = false;
            Time.timeScale = 1f;
        }

        void RefreshTexts()
        {
            controls.text = L.T("hud.controls", single ? L.T("hud.esc_pause") : L.T("hud.esc_menu"));
            pauseHint.text = single ? L.T("hud.paused_single") : L.T("hud.paused_coop");
            RebuildRoster();
        }

        void Update()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.startButton.wasPressedThisFrame))
                SetPaused(!paused);
            if (kb != null && kb.hKey.wasPressedThisFrame) controls.ToggleInClassList("controls--faded");
        }

        // ---------------- Duraklat ----------------
        void SetPaused(bool p)
        {
            paused = p;
            FumblePlayer.InputBlocked = p;
            pause.EnableInClassList("pause--hidden", !p);
            if (single)
            {
                Time.timeScale = p ? 0f : 1f; // tek oyuncuda gerçek duraklatma
                lobbyRow.style.display = DisplayStyle.None;
                btnInvite.style.display = DisplayStyle.None;
                btnLobby.style.display = DisplayStyle.None;
                return;
            }
            if (!p) return;

            var steam = SteamLobby.Instance;
            bool inLobby = steam != null && steam.InLobby;
            btnInvite.style.display = inLobby ? DisplayStyle.Flex : DisplayStyle.None;
            btnLobby.style.display = NetworkServer.active ? DisplayStyle.Flex : DisplayStyle.None;
            lobbyRow.style.display = DisplayStyle.Flex;
            var nm = FumbleNetworkManager.Instance;
            if (inLobby) { lobbyLabel.text = L.T("lobby.code"); lobbyCode.text = steam.LobbyCode; }
            else if (NetworkServer.active) { lobbyLabel.text = L.T("hud.address", nm.LocalPort); lobbyCode.text = LocalIp(); }
            else { lobbyLabel.text = L.T("hud.connected"); lobbyCode.text = nm.networkAddress; }
        }

        void Leave()
        {
            FumblePlayer.InputBlocked = true;
            Time.timeScale = 1f;
            SceneFader.Get().FadeOut(0.4f, () => FumbleNetworkManager.Instance.Leave(), "");
        }

        void ReturnToLobby()
        {
            if (!NetworkServer.active) return;
            FumblePlayer.InputBlocked = true;
            SceneFader.Get().FadeOut(0.4f, () => FumbleNetworkManager.Instance.ReturnToLobby(), "");
        }

        // ---------------- Oyuncu listesi ----------------
        void RebuildRoster()
        {
            if (roster == null) return;
            roster.Clear();
            if (single) return;
            foreach (var p in FumblePlayer.All.Where(x => x != null).OrderBy(x => x.slot))
            {
                var item = new VisualElement { pickingMode = PickingMode.Ignore };
                item.AddToClassList("roster-item");
                if (p.isLocalPlayer) item.AddToClassList("roster-item--local");
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("roster-item__dot");
                dot.style.backgroundColor = p.Color;
                item.Add(dot);
                var name = new Label(p.playerName + (p.isLocalPlayer ? " " + L.T("lobby.you") : "")) { pickingMode = PickingMode.Ignore };
                name.AddToClassList("roster-item__name");
                item.Add(name);
                for (int i = 0; i < 4; i++)
                {
                    if (!LimbUtil.Has(p.limbMask, i)) continue;
                    var chip = new Label(LimbUtil.Name(i)) { pickingMode = PickingMode.Ignore };
                    chip.AddToClassList("limb-chip");
                    if (p.ActiveLimb == i && ClimberBody.Instance != null && ClimberBody.Instance.IsDragged(i)) chip.AddToClassList("limb-chip--active");
                    item.Add(chip);
                }
                roster.Add(item);
            }
        }

        static int CountBits(byte b) { int c = 0; while (b != 0) { c += b & 1; b >>= 1; } return c; }

        // ---------------- Bildirimler ----------------
        void ShowToast(string text, float seconds = 2f)
        {
            toastText.text = text;
            toast.AddToClassList("toast--show");
            toastHide?.Pause();
            toastHide = root.schedule.Execute(() => toast.RemoveFromClassList("toast--show")).StartingIn((long)(seconds * 1000));
        }

        void OnJoined(string name) => ShowToast(L.T("lobby.joined", name), 2.5f);
        void OnLeft(string name) => ShowToast(L.T("lobby.left", name), 2.5f);

        void OnRespawned(string reason)
        {
            SceneFader.Get().Flash(reason == "manual" ? "" : "AAAAAaaaaa...", 0.45f);
            ShowToast(L.T("hud.respawned"), 2.5f);
        }

        int lastBiome;

        void OnCheckpoint()
        {
            var b = ClimberBody.Instance;
            float y = b != null ? b.TorsoPos.y : 0f;
            int camp = Mathf.Clamp(Mathf.RoundToInt(y / Mountain.CampSpacing), 1, Mountain.CampCount);
            string text = L.T("hud.camp_n", camp, Mountain.CampCount, Mathf.RoundToInt(Mountain.CampY(camp - 1)));
            int biome = Mountain.Biome(Mountain.CampY(camp - 1) + 0.5f);
            if (biome != lastBiome && y < Mountain.SummitY - 1f)
            {
                lastBiome = biome;
                text = L.T("hud.biome" + (biome + 1)) + "\n" + text;
            }
            ShowToast(text, 3.2f);
        }

        void OnGaveUp(int limb)
        {
            var owner = FumblePlayer.OwnerOf(limb);
            var text = L.T("hud.gave_up", LimbUtil.Name(limb));
            if (owner != null && !single) text += $" ({owner.playerName})";
            ShowToast(text, 1.8f);
        }

        void OnPest(PestController.Kind kind, PestController.PestEvent e, int limb)
        {
            if (e == PestController.PestEvent.Nest)
            {
                ShowToast(L.T(kind switch { PestController.Kind.Bees => "hud.nest_bees", PestController.Kind.Bird => "hud.nest_bird", PestController.Kind.Spider => "hud.nest_spider", _ => "hud.nest_fly" }), 2.6f);
                return;
            }
            if (e == PestController.PestEvent.NestHit) { ShowToast(L.T("hud.nest_hit"), 3f); return; }
            string key = e == PestController.PestEvent.Shoo ? "hud.pest_shoo"
                : e == PestController.PestEvent.Arrive
                    ? kind switch { PestController.Kind.Bees => "hud.pest_bees", PestController.Kind.Fly => "hud.pest_fly", PestController.Kind.Bird => "hud.pest_bird", _ => "hud.pest_spider" }
                    : kind switch { PestController.Kind.Bees => "hud.pest_sting", PestController.Kind.Bird => "hud.pest_peck", _ => "hud.pest_spook" };
            ShowToast(L.T(key), 2.2f);
        }

        void OnCrumbled() => ShowToast(L.T("hud.crumble"), 1.8f);

        void OnSlipped(int limb)
        {
            var owner = FumblePlayer.OwnerOf(limb);
            var text = L.T("hud.slipped", LimbUtil.Name(limb));
            if (owner != null && !single) text += $" ({owner.playerName})";
            ShowToast(text, 1.6f);
        }

        void OnSummit()
        {
            summit.RemoveFromClassList("summit--hidden");
            root.schedule.Execute(() => summit.AddToClassList("summit--hidden")).StartingIn(7000);
        }

        static string cachedIp;
        static string LocalIp()
        {
            if (cachedIp != null) return cachedIp;
            try
            {
                var ip = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList
                    .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a));
                cachedIp = ip != null ? ip.ToString() : "127.0.0.1";
            }
            catch { cachedIp = "127.0.0.1"; }
            return cachedIp;
        }
    }
}
