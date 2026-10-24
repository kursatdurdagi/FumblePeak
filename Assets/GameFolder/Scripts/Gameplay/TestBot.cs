using System.IO;
using System.Text;
using Mirror;
using UnityEngine;

namespace FumblePeak
{
    /// <summary>
    /// Co-op test botu (sadece BUILD'de ve oyun klasörünün yanında "fp_bot.txt" varsa çalışır).
    ///  • Ana menüde localhost'a bağlanır, lobide "Hazır" olur.
    ///  • Oyunda kendi uzuvlarıyla en yüksek erişilebilir tutamağa uzanıp tutunur (gerçek girdi yolu: FumblePlayer.Sim).
    ///  • Durumunu "fp_bot_log.txt" dosyasına yazar.
    /// Editörde ve normal oyuncu kurulumlarında hiçbir şey yapmaz.
    /// </summary>
    public class TestBot : MonoBehaviour
    {
        static TestBot instance;
        string dir;
        float nextAction, nextLog;
        bool joinRequested;
        readonly StringBuilder log = new StringBuilder();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Application.isEditor || instance != null) return;
            var d = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            if (!File.Exists(Path.Combine(d, "fp_bot.txt"))) return;
            var go = new GameObject("[TestBot]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<TestBot>();
            instance.dir = d;
            Application.runInBackground = true;
            Screen.SetResolution(960, 540, FullScreenMode.Windowed);
        }

        void Update()
        {
            var nm = FumbleNetworkManager.Instance;
            if (nm == null) return;

            if (!NetworkClient.active && !joinRequested && Time.realtimeSinceStartup > 4f)
            {
                joinRequested = true;
                Log("join localhost");
                nm.JoinLocal("127.0.0.1");
            }
            if (!NetworkClient.active && joinRequested && Time.realtimeSinceStartup > 20f && Time.frameCount % 600 == 0)
                joinRequested = false; // tekrar dene

            var me = FumblePlayer.Local;
            if (me != null && !me.ready && !nm.IsInGameScene) { me.SetReady(true); Log("ready"); }

            var body = ClimberBody.Instance;
            if (me != null && body != null && Time.time > nextAction)
            {
                nextAction = Time.time + 1.2f;
                ClimbStep(me, body);
            }

            if (Time.realtimeSinceStartup > nextLog)
            {
                nextLog = Time.realtimeSinceStartup + 2f;
                var sb = new StringBuilder();
                sb.Append($"t={Time.realtimeSinceStartup:F0} connected={NetworkClient.isConnected} scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
                if (me != null) sb.Append($" slot={me.slot} mask={me.limbMask} ready={me.ready} active={me.ActiveLimb} players={FumblePlayer.All.Count}");
                if (body != null)
                {
                    sb.Append($" torso={body.TorsoPos.y:F2} grabs=");
                    for (int i = 0; i < 4; i++) sb.Append(body.IsGrabbing(i) ? "1" : "0");
                }
                Log(sb.ToString());
                File.WriteAllText(Path.Combine(dir, "fp_bot_log.txt"), log.ToString());
            }
        }

        void ClimbStep(FumblePlayer me, ClimberBody body)
        {
            int L = me.ActiveLimb;
            if (!LimbUtil.Has(me.limbMask, L)) return;
            Vector2 anchor = body.RenderAnchor(L);
            Vector2 best = Vector2.zero; float by = float.NegativeInfinity;
            foreach (var g in FindObjectsByType<Grip>(FindObjectsSortMode.None))
            {
                Vector2 gp = g.GetComponent<Collider>().bounds.center;
                bool usedByOther = false;
                for (int i = 0; i < 4; i++) if (i != L && (body.RenderEffectorPos(i) - gp).sqrMagnitude < 0.22f * 0.22f) usedByOther = true;
                if (usedByOther) continue;
                float lim = body.reach[L] * 0.9f;
                if ((gp - anchor).magnitude < lim && gp.y > by && (LimbUtil.IsArm(L) || gp.y < anchor.y - 0.3f)) { by = gp.y; best = gp; }
            }
            if (float.IsNegativeInfinity(by)) return;
            Vector2 need = best - me.CursorOf(L);
            FumblePlayer.Sim.delta += need / (0.0045f * FumblePlayer.Sensitivity);
            Invoke(nameof(Click), 0.25f);
        }

        void Click() => FumblePlayer.Sim.click = true;

        void Log(string s)
        {
            log.AppendLine(s);
            if (log.Length > 20000) log.Remove(0, 10000);
        }
    }
}
