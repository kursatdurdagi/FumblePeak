using UnityEngine;

namespace FumblePeak
{
    /// <summary>Low-poly alevin canlı görünmesi: boy/en titreşimi, hafif dönüş ve ışık titremesi.</summary>
    public class CampfireFlicker : MonoBehaviour
    {
        public Light fireLight;
        float seed, baseIntensity = -1f;
        Vector3 baseScale;

        void Start()
        {
            seed = Random.value * 100f;
            baseScale = transform.localScale;
            if (fireLight != null) baseIntensity = fireLight.intensity;
        }

        void Update()
        {
            float t = Time.time * 7f + seed;
            float n = Mathf.PerlinNoise(t, seed) - 0.5f;
            float n2 = Mathf.PerlinNoise(seed, t * 1.3f) - 0.5f;
            transform.localScale = Vector3.Scale(baseScale, new Vector3(1f + n2 * 0.18f, 1f + n * 0.35f, 1f - n2 * 0.18f));
            transform.localRotation = Quaternion.Euler(n2 * 8f, Time.time * 40f + seed, n * 8f);
            if (fireLight != null) fireLight.intensity = baseIntensity * (1f + n * 0.45f);
        }
    }
}
