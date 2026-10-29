using System.Collections.Generic;
using UnityEngine;

namespace FumblePeak
{
    /// <summary>
    /// Rotaya nadiren konan canlı yuvası (arı kovanı, kuş yuvası, örümcek ağı). Tutunulabilir görünür
    /// ama tutan uzuv yuvayı bozar: içinden kızgın kuş / arı sürüsü / yavru örümcekler fırlayıp
    /// tırmanıcıyı düşürür (PestController.ServerNestBurst). Oyuncu böylece yuvalara dokunmamayı öğrenir.
    /// Bu bileşen sadece görseldir (yuvadaki sakinler, sallanma); saldırıyı sunucu yönetir.
    /// </summary>
    public class PestNest : MonoBehaviour
    {
        public PestController.Kind kind = PestController.Kind.Bees;
        public Material bodyMaterial;
        public Material wingMaterial;
        [Tooltip("Sakinlerin durduğu yer (yerel)")]
        public Vector3 residentOffset = new Vector3(0f, 0.05f, -0.12f);

        public static readonly List<PestNest> All = new List<PestNest>();

        Transform visual, residents;
        readonly List<Transform> members = new List<Transform>();
        Quaternion visualRot;
        float shakeUntil, hiddenUntil, serverReadyAt, seed;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            seed = Random.value * 10f;
            visual = transform.Find("Visual");
            if (visual != null) visualRot = visual.localRotation;
            if (bodyMaterial == null) return;
            residents = new GameObject("Residents").transform;
            residents.SetParent(transform, false);
            residents.localPosition = residentOffset;
            switch (kind)
            {
                case PestController.Kind.Bees:
                    for (int i = 0; i < 3; i++) members.Add(PestArt.Bee(residents, 0.9f, bodyMaterial, wingMaterial));
                    break;
                case PestController.Kind.Bird:
                    var b = PestArt.Bird(residents, 0.9f, false, bodyMaterial);
                    b.localRotation = Quaternion.Euler(0f, 180f, 0f); // kameraya bakar
                    members.Add(b);
                    break;
                case PestController.Kind.Spider:
                    var s = PestArt.Spider(residents, 1.1f, false, bodyMaterial);
                    s.localRotation = Quaternion.Euler(-80f, 0f, 0f); // ağın üstünde, baş aşağı
                    members.Add(s);
                    break;
                default:
                    for (int i = 0; i < 4; i++) members.Add(PestArt.Fly(residents, 0.8f, bodyMaterial, wingMaterial));
                    break;
            }
        }

        void Update()
        {
            float t = Time.time + seed;
            // Saldırı anı: yuva sarsılır
            if (visual != null)
            {
                if (Time.time < shakeUntil)
                {
                    float k = (shakeUntil - Time.time) / 0.7f;
                    visual.localRotation = visualRot * Quaternion.Euler(Mathf.Sin(t * 55f) * 9f * k, 0f, Mathf.Sin(t * 47f) * 12f * k);
                }
                else visual.localRotation = visualRot;
            }
            if (residents == null) return;
            bool hidden = Time.time < hiddenUntil;
            if (residents.gameObject.activeSelf == hidden) residents.gameObject.SetActive(!hidden);
            if (hidden) return;

            switch (kind)
            {
                case PestController.Kind.Bees:
                case PestController.Kind.Fly:
                    for (int i = 0; i < members.Count; i++)
                    {
                        float a = t * (2.2f + i * 0.5f) + i * 2.1f;
                        float r = kind == PestController.Kind.Bees ? 0.2f : 0.16f;
                        members[i].localPosition = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a * 1.6f) * 0.07f, Mathf.Sin(a) * r * 0.6f - 0.05f);
                        members[i].localRotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a) * 0.6f) + Vector3.back * 0.15f);
                        PestArt.Flap(members[i], t, 70f, 40f);
                    }
                    break;
                case PestController.Kind.Bird:
                    if (members.Count > 0)
                    {
                        var b = members[0];
                        // kafa sallar, ara sıra yerinde zıplar (yuvayı korur)
                        float hop = Mathf.Max(0f, Mathf.Sin(t * 1.3f) - 0.85f) * 0.25f;
                        b.localPosition = new Vector3(0f, hop, 0f);
                        b.localRotation = Quaternion.Euler(Mathf.Sin(t * 2.7f) * 6f, 180f + Mathf.Sin(t * 0.8f) * 35f, 0f);
                        PestArt.Flap(b, t, 18f, hop > 0f ? 35f : 0f, hop > 0f ? 0f : -55f);
                    }
                    break;
                case PestController.Kind.Spider:
                    if (members.Count > 0)
                        members[0].localPosition = new Vector3(Mathf.Sin(t * 0.7f) * 0.03f, Mathf.Sin(t * 0.5f) * 0.03f, 0f);
                    break;
            }
        }

        /// <summary>Tüm istemcilerde: yuva bozuldu (sakinler fırladı, yuva sarsılır).</summary>
        public void PlayBurst()
        {
            shakeUntil = Time.time + 0.7f;
            hiddenUntil = Time.time + 9f;
        }

        /// <summary>Sunucu: arka arkaya tetiklenmesin.</summary>
        public bool ServerTryTrigger()
        {
            if (Time.time < serverReadyAt) return false;
            serverReadyAt = Time.time + 5f;
            return true;
        }

        public static PestNest Nearest(Vector3 p, float maxDist = 1f)
        {
            PestNest best = null; float bd = maxDist * maxDist;
            foreach (var n in All)
            {
                float d = (n.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = n; }
            }
            return best;
        }
    }
}
