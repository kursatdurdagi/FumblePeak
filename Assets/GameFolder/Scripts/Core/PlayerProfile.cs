using UnityEngine;

namespace FumblePeak
{
    /// <summary>Yerel oyuncu tercihleri (isim, ses, ekran).</summary>
    public static class PlayerProfile
    {
        const string NameKey = "fp_name";
        const string VolumeKey = "fp_volume";
        const string IpKey = "fp_last_ip";

        public static string Name
        {
            get
            {
                var steam = SteamLobby.Instance != null && SteamLobby.Instance.IsReady ? SteamLobby.Instance.PersonaName : null;
                if (!string.IsNullOrEmpty(steam)) return steam;
                var n = PlayerPrefs.GetString(NameKey, "");
                if (string.IsNullOrWhiteSpace(n))
                {
                    n = L.T("profile.default") + Random.Range(100, 999);
                    PlayerPrefs.SetString(NameKey, n);
                }
                return n;
            }
            set { PlayerPrefs.SetString(NameKey, Sanitize(value)); PlayerPrefs.Save(); }
        }

        public static float Volume
        {
            get => PlayerPrefs.GetFloat(VolumeKey, 0.8f);
            set { PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value)); AudioListener.volume = Mathf.Clamp01(value); }
        }

        public static float Sensitivity
        {
            get => PlayerPrefs.GetFloat("fp_sens", 1f);
            set { PlayerPrefs.SetFloat("fp_sens", Mathf.Clamp(value, 0.3f, 3f)); FumblePlayer.Sensitivity = Mathf.Clamp(value, 0.3f, 3f); }
        }

        public static int Quality
        {
            get => PlayerPrefs.GetInt("fp_quality", QualitySettings.GetQualityLevel());
            set { PlayerPrefs.SetInt("fp_quality", value); QualitySettings.SetQualityLevel(value, true); }
        }

        public static bool VSync
        {
            get => PlayerPrefs.GetInt("fp_vsync", 1) == 1;
            set { PlayerPrefs.SetInt("fp_vsync", value ? 1 : 0); QualitySettings.vSyncCount = value ? 1 : 0; Application.targetFrameRate = value ? -1 : 144; }
        }

        /// <summary>Açılışta kayıtlı ayarları uygula.</summary>
        public static void ApplySaved()
        {
            AudioListener.volume = Volume;
            FumblePlayer.Sensitivity = Sensitivity;
            if (PlayerPrefs.HasKey("fp_quality")) QualitySettings.SetQualityLevel(Quality, true);
            VSync = VSync;
        }

        public static string LastIp
        {
            get => PlayerPrefs.GetString(IpKey, "localhost");
            set => PlayerPrefs.SetString(IpKey, value);
        }

        public static string Sanitize(string n)
        {
            if (string.IsNullOrWhiteSpace(n)) return L.T("profile.default");
            n = n.Trim();
            return n.Length > 20 ? n.Substring(0, 20) : n;
        }
    }
}
