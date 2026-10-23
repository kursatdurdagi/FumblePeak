using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FumblePeak
{
    /// <summary>GameScene doğrudan Play'e basılırsa ağ yöneticisi olmadığı için Ana Menü'ye yönlendirir.</summary>
    public class DevBootstrap : MonoBehaviour
    {
        void Awake()
        {
            if (NetworkManager.singleton == null)
            {
                Debug.Log("[FumblePeak] NetworkManager yok → Ana Menü yükleniyor (oyunu menüden başlat).");
                SceneManager.LoadScene(0);
            }
        }
    }
}
