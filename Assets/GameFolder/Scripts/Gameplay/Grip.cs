using Mirror;
using UnityEngine;

namespace FumblePeak
{
    public enum GripType { Rock, Slippery }
    /// <summary>Tutunulabilir yüzey. Collider trigger olabilir (gövde takılmasın).</summary>
    public class Grip : MonoBehaviour
    {
        public GripType type = GripType.Rock;
        [Tooltip("Kaygan yüzeyde aşağı kayma hızı (m/sn)")]
        public float slideSpeed = 0.35f;
    }
}
