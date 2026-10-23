using UnityEngine;

namespace FumblePeak
{
    public enum Limb : byte { LeftArm = 0, RightArm = 1, LeftLeg = 2, RightLeg = 3 }

    public static class LimbUtil
    {
        public const int Count = 4;

        /// <summary>Lokalize uzuv adı.</summary>
        public static string Name(int limb) => L.T("limb." + limb);

        public static readonly Color[] PlayerColors =
        {
            new Color(1.00f, 0.45f, 0.20f), // turuncu
            new Color(0.25f, 0.70f, 1.00f), // mavi
            new Color(0.55f, 0.90f, 0.30f), // yeşil
            new Color(1.00f, 0.82f, 0.20f), // sarı
        };

        public static bool Has(byte mask, int limb) => (mask & (1 << limb)) != 0;

        public static bool IsArm(int limb) => limb <= 1;

        /// <summary>GDD 4.1 - Dinamik oyuncu atamaları.</summary>
        public static byte[] AssignMasks(int playerCount)
        {
            switch (playerCount)
            {
                case 0: return new byte[0];
                case 1: return new byte[] { 0b1111 };                                  // Ahtapot modu
                case 2: return new byte[] { (1 << 0) | (1 << 3), (1 << 1) | (1 << 2) }; // Çapraz bağ
                case 3: return new byte[] { 1 << 0, 1 << 1, (1 << 2) | (1 << 3) };      // Asimetrik
                default:
                    var r = new byte[playerCount];
                    for (int i = 0; i < playerCount; i++) r[i] = i < 4 ? (byte)(1 << i) : (byte)0;
                    return r;                                                            // Tam kaos
            }
        }

        public static string Describe(byte mask)
        {
            var s = "";
            for (int i = 0; i < Count; i++)
                if (Has(mask, i)) s += (s.Length > 0 ? ", " : "") + Name(i);
            return s.Length > 0 ? s : L.T("limb.none");
        }
    }
}
