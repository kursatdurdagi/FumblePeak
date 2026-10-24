using UnityEngine;

namespace FumblePeak
{
    /// <summary>Bulutları yavaşça yatay kaydırır, sınırdan çıkınca diğer taraftan döner. (Sadece görsel)</summary>
    public class CloudDrift : MonoBehaviour
    {
        public float speed = 0.4f;
        public float minX = -70f, maxX = 70f;
        float bob;

        void Start() => bob = Random.value * 10f;

        void Update()
        {
            var p = transform.position;
            p.x += speed * Time.deltaTime;
            if (p.x > maxX) p.x = minX;
            p.y += Mathf.Sin((Time.time + bob) * 0.3f) * 0.002f;
            transform.position = p;
        }
    }
}
