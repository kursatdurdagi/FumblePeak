using UnityEngine;

namespace FumblePeak
{
    /// <summary>
    /// Kameraya bağlı atmosfer: aşağıda uçuşan polen, yükseldikçe kar ve rüzgâr sesi yerine
    /// daha yoğun kar. Sis rengi yükseldikçe soğuk tona kayar. (Sadece görsel, ağ yok)
    /// </summary>
    public class AltitudeAtmosphere : MonoBehaviour
    {
        public ParticleSystem pollen;
        public ParticleSystem snow;
        public float snowStartY = 22f;
        public float snowFullY = 34f;
        public Color lowFog = new Color(0.74f, 0.85f, 0.93f);
        public Color highFog = new Color(0.82f, 0.88f, 0.96f);

        void LateUpdate()
        {
            float y = transform.position.y;
            float k = Mathf.InverseLerp(snowStartY, snowFullY, y);
            if (snow != null)
            {
                var em = snow.emission;
                em.rateOverTime = Mathf.Lerp(0f, 60f, k);
            }
            if (pollen != null)
            {
                var em = pollen.emission;
                em.rateOverTime = Mathf.Lerp(8f, 0f, Mathf.InverseLerp(4f, 16f, y));
            }
            RenderSettings.fogColor = Color.Lerp(lowFog, highFog, k);
        }
    }
}
