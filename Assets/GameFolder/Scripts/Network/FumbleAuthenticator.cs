using System.Collections;
using Mirror;
using UnityEngine;

namespace FumblePeak
{
    /// <summary>
    /// Bağlanırken el sıkışma: oyun sürümü aynı mı, lobide yer var mı?
    /// Uyuşmazlıkta istemciye anlaşılır bir sebep gönderilir ve menüde gösterilir
    /// (ör. arkadaşın projenin eski sürümünü çektiyse "sürümler farklı" yazar).
    /// </summary>
    public class FumbleAuthenticator : NetworkAuthenticator
    {
        /// <summary>Ağ protokolü değişince artır (oyun kodu uyumsuzsa bağlanmayı engeller).</summary>
        public const int Protocol = 3;
        public static string Version => Application.version + "/p" + Protocol;

        public struct AuthRequest : NetworkMessage { public string version; }
        public struct AuthResponse : NetworkMessage { public byte code; public string hostVersion; }

        const byte Ok = 0, BadVersion = 1, Full = 2;

        /// <summary>Host tarafı: bir bağlantı denemesi geldi (adres).</summary>
        public static event System.Action<string> ServerAttempt;
        /// <summary>Host tarafı: bağlantı reddedildi (adres, sebep metni).</summary>
        public static event System.Action<string, string> ServerRejected;
        const float AuthTimeout = 6f;

        // ---------------- Sunucu ----------------
        public override void OnStartServer() => NetworkServer.RegisterHandler<AuthRequest>(OnAuthRequest, false);
        public override void OnStopServer() => NetworkServer.UnregisterHandler<AuthRequest>();

        public override void OnServerAuthenticate(NetworkConnectionToClient conn)
        {
            // istemcinin sürüm mesajını bekle; gelmezse (eski sürüm) reddet
            if (conn.connectionId != NetworkConnection.LocalConnectionId) ServerAttempt?.Invoke(conn.address);
            StartCoroutine(AuthTimeoutRoutine(conn));
        }

        IEnumerator AuthTimeoutRoutine(NetworkConnectionToClient conn)
        {
            yield return new WaitForSecondsRealtime(AuthTimeout);
            if (conn != null && !conn.isAuthenticated && NetworkServer.connections.ContainsKey(conn.connectionId))
            {
                ServerRejected?.Invoke(conn.address, L.T("msg.reject_old"));
                conn.Send(new AuthResponse { code = BadVersion, hostVersion = Version });
                StartCoroutine(RejectLater(conn));
            }
        }

        void OnAuthRequest(NetworkConnectionToClient conn, AuthRequest msg)
        {
            byte code = Ok;
            if (msg.version != Version) code = BadVersion;
            else if (NetworkServer.connections.Count > 4) code = Full;

            conn.Send(new AuthResponse { code = code, hostVersion = Version });
            if (code != Ok) ServerRejected?.Invoke(conn.address, code == BadVersion ? L.T("msg.reject_version", msg.version) : L.T("msg.lobby_full"));
            if (code == Ok) ServerAccept(conn);
            else StartCoroutine(RejectLater(conn));
        }

        IEnumerator RejectLater(NetworkConnectionToClient conn)
        {
            yield return new WaitForSecondsRealtime(0.3f); // cevap mesajı ulaşsın
            ServerReject(conn);
        }

        // ---------------- İstemci ----------------
        public override void OnStartClient() => NetworkClient.RegisterHandler<AuthResponse>(OnAuthResponse, false);
        public override void OnStopClient() => NetworkClient.UnregisterHandler<AuthResponse>();

        public override void OnClientAuthenticate() => NetworkClient.Send(new AuthRequest { version = Version });

        void OnAuthResponse(AuthResponse msg)
        {
            if (msg.code == Ok) { ClientAccept(); return; }
            FumbleNetworkManager.PendingMenuMessage = msg.code == BadVersion
                ? L.T("msg.version_mismatch", msg.hostVersion, Version)
                : L.T("msg.lobby_full");
            FumbleNetworkManager.Instance?.MarkRejected();
            ClientReject();
        }
    }
}
