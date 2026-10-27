using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Mirror.Discovery;
using UnityEngine;

namespace FumblePeak
{
    /// <summary>
    /// Aynı Wi-Fi'daki lobileri bulur. Mirror'ın varsayılanı sadece 255.255.255.255'e yayın yapar;
    /// macOS'ta bu bazen yanlış arayüze (AWDL/VPN) gider. Burada her aktif ağ arayüzünün
    /// alt ağ yayın adresine (ör. 192.168.1.255) ayrı ayrı istek gönderilir.
    /// </summary>
    public class FumbleDiscovery : NetworkDiscovery
    {
        float nextBroadcast;
        bool searching;

        public void Search()
        {
            enableActiveDiscovery = false; // kendi döngümüzü kullanıyoruz
            StartDiscovery();
            searching = true;
            nextBroadcast = 0f;
        }

        public void StopSearch()
        {
            searching = false;
            StopDiscovery();
        }

        void Update()
        {
            if (!searching || Time.unscaledTime < nextBroadcast) return;
            nextBroadcast = Time.unscaledTime + 1.5f;
            foreach (var addr in BroadcastTargets())
            {
                BroadcastAddress = addr;
                try { BroadcastDiscoveryRequest(); } catch { }
            }
            BroadcastAddress = "";
        }

        public static List<string> BroadcastTargets()
        {
            var list = new List<string> { "255.255.255.255" };
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var ip = ua.Address.GetAddressBytes();
                        if (ip[0] == 169 && ip[1] == 254) continue; // link-local
                        byte[] mask;
                        try { mask = ua.IPv4Mask != null ? ua.IPv4Mask.GetAddressBytes() : null; } catch { mask = null; }
                        if (mask == null || mask.Length != 4) mask = new byte[] { 255, 255, 255, 0 }; // varsayılan /24
                        if (mask.Length != 4 || (mask[0] == 0 && mask[1] == 0)) continue;
                        var b = new byte[4];
                        for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                        var s = new IPAddress(b).ToString();
                        if (!list.Contains(s)) list.Add(s);
                    }
                }
            }
            catch { }
            return list;
        }
    }
}
