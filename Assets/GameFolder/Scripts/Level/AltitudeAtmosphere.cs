using UnityEngine;

namespace FumblePeak
{
    /// <summary>
    /// Kameraya bağlı atmosfer: aşağıda uçuşan polen, yükseldikçe kar. Sis rengi yükseldikçe
    /// soğuk tona kayar, görüş mesafesi açılır (yukarıdan uzak dağlar görünür). (Sadece görsel, ağ yok)
    /// </summary>
    public class AltitudeAtmosphere : MonoBehaviour
    {
        public ParticleSystem pollen;
        public ParticleSystem snow;
        public float snowStartY = 120f;  // Rüzgârlı Sırtlar
        public float snowFullY = 160f;   // Buzul
        public Color lowFog = new Color(0.74f, 0.85f, 0.93f);
        public Color highFog = new Color(0.82f, 0.88f, 0.96f);
        public float lowFogEnd = 160f;
        public float highFogEnd = 280f;

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
                em.rateOverTime = Mathf.Lerp(8f, 0f, Mathf.InverseLerp(4f, 30f, y));
            }
            RenderSettings.fogColor = Color.Lerp(lowFog, highFog, k);
            RenderSettings.fogEndDistance = Mathf.Lerp(lowFogEnd, highFogEnd, Mathf.InverseLerp(10f, Mountain.SummitY, y));
        }
    }
}
