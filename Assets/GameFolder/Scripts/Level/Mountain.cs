using UnityEngine;

namespace FumblePeak
{
    /// <summary>
    /// Dağın ölçüleri ve bölgeleri (seviye kurucu, canlılar, HUD ve atmosfer aynı değerleri kullanır).
    /// Her 12 m'de bir kamp; 5 bölge, her bölge bir kampla başlar.
    /// </summary>
    public static class Mountain
    {
        public const float CampSpacing = 12f;
        public const int CampCount = 16;                 // 12, 24, ... 192 m
        public const float SummitY = 204f;
        public const float PeakY = 212f;

        public static float CampY(int index) => (index + 1) * CampSpacing;

        // Bölgeler: 0 Vadi (0-36), 1 Yosunlu Orman Sırtı (36-72), 2 Kızıl Kayalıklar (72-120), 3 Rüzgârlı Sırtlar (120-156), 4 Buzul Zirve (156+)
        public static readonly float[] BiomeStart = { 0f, 36f, 72f, 120f, 156f };
        public const int BiomeCount = 5;

        public static int Biome(float y)
        {
            for (int i = BiomeStart.Length - 1; i > 0; i--) if (y >= BiomeStart[i]) return i;
            return 0;
        }

        /// <summary>
        /// Zorluk eğrisi taban değeri (0 kolay … 1 zor): ilk metreler çok kolay, zirveye doğru yükselir;
        /// her kampın hemen sonrası biraz rahat, bir sonraki kampa yaklaştıkça sıkılaşır.
        /// (Seviye kurucu buna rastgele "kilit noktaları" ekler.)
        /// </summary>
        public static float BaseDifficulty(float y)
        {
            if (y < 8f) return 0f;
            float p = Mathf.Clamp01(y / SummitY);
            float baseD = 0.08f + 0.8f * Mathf.Pow(p, 0.85f);
            float u = Mathf.Repeat(y, CampSpacing) / CampSpacing;
            return Mathf.Clamp01(baseD * Mathf.Lerp(0.7f, 1.12f, u));
        }

        /// <summary>0..1: zirveye ne kadar yaklaşıldı.</summary>
        public static float Progress(float y) => Mathf.Clamp01(y / SummitY);
    }
}
