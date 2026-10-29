using System.Collections.Generic;
using UnityEngine;

namespace FumblePeak
{
    /// <summary>
    /// Çürük (çatlak) taş: tutunulabilir ama tutulunca titrer, kısa süre sonra parçalanıp düşer;
    /// tutan uzuv boşlukta kalır. Bir süre sonra yerine yenisi "büyür" (co-op'ta herkes için tutarlı).
    /// Durum sunucuda karar verilir (ClimberBody), görsel tüm istemcilerde RPC ile oynatılır.
    /// İpucu: çatlaklı, kurşuni-kahve renkli taşlar — dikkatli bakan ayırt edebilir.
    /// </summary>
    public class CrumblingHold : MonoBehaviour
    {
        public float breakDelay = 0.85f;
        public float regrowTime = 20f;

        public static readonly List<CrumblingHold> All = new List<CrumblingHold>();

        Transform stone;
        Vector3 stoneLocal, stoneScale;
        Quaternion stoneRot;
        Collider col;
        float shakeStart = -1f, regrowAt = -1f, serverBreakAt = -1f, popStart = -1f;
        bool broken;

        public bool IsBroken => broken;

        void Awake()
        {
            stone = transform.Find("Stone");
            if (stone != null) { stoneLocal = stone.localPosition; stoneScale = stone.localScale; stoneRot = stone.localRotation; }
            col = GetComponent<Collider>();
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        // ---------- Sunucu ----------
        /// <summary>İlk tutuluşta geri sayım başlar (bırakılsa da kırılır). true: yeni başladı.</summary>
        public bool ServerTouch()
        {
            if (broken || serverBreakAt > 0f) return false;
            serverBreakAt = Time.time + breakDelay;
            return true;
        }

        public bool ServerShouldBreak => !broken && serverBreakAt > 0f && Time.time >= serverBreakAt;

        // ---------- Tüm istemciler ----------
        public void PlayShake()
        {
            if (!broken && shakeStart < 0f) shakeStart = Time.time;
        }

        public void Break()
        {
            if (broken) return;
            broken = true;
            serverBreakAt = -1f; shakeStart = -1f;
            if (col != null) col.enabled = false;
            if (stone != null)
            {
                SpawnDebris();
                stone.gameObject.SetActive(false);
            }
            regrowAt = Time.time + regrowTime;
        }

        void Restore()
        {
            broken = false;
            if (col != null) col.enabled = true;
            if (stone != null)
            {
                stone.gameObject.SetActive(true);
                stone.localPosition = stoneLocal;
                stone.localRotation = stoneRot;
                popStart = Time.time;
            }
        }

        void Update()
        {
            if (stone == null) return;
            if (broken)
            {
                if (Time.time >= regrowAt) Restore();
                return;
            }
            if (shakeStart >= 0f)
            {
                // giderek şiddetlenen titreme + hafif çökme
                float k = Mathf.Clamp01((Time.time - shakeStart) / breakDelay);
                stone.localPosition = stoneLocal + (Vector3)(Random.insideUnitCircle * (0.006f + 0.02f * k)) + Vector3.down * 0.02f * k * k;
                stone.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-6f, 6f) * k) * stoneRot;
            }
            if (popStart >= 0f)
            {
                // yeniden oluşurken büyüyerek belirir
                float k = Mathf.Clamp01((Time.time - popStart) / 0.5f);
                stone.localScale = Vector3.Scale(stoneScale, new Vector3(k, k, 1f));
                if (k >= 1f) popStart = -1f;
            }
        }

        void SpawnDebris()
        {
            var mf = stone.GetComponent<MeshFilter>();
            var mr = stone.GetComponent<MeshRenderer>();
            if (mf == null || mr == null) return;
            float baseS = stone.lossyScale.x;
            Vector3 c = new Vector3(stone.position.x, stone.position.y, -0.08f);
            for (int i = 0; i < 8; i++)
            {
                var go = new GameObject("Debris");
                go.transform.position = c + (Vector3)(Random.insideUnitCircle * baseS * 0.3f);
                go.transform.rotation = Random.rotation;
                go.transform.localScale = Vector3.one * baseS * Random.Range(0.22f, 0.42f);
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mr.sharedMaterial;
                var rb = go.AddComponent<Rigidbody>();
                rb.useGravity = true;
                rb.linearVelocity = new Vector3(Random.Range(-1.4f, 1.4f), Random.Range(0.3f, 2f), Random.Range(-1.2f, -0.3f));
                rb.angularVelocity = Random.insideUnitSphere * 12f;
                Destroy(go, 2.5f);
            }
        }

        public static CrumblingHold Nearest(Vector3 p, float maxDist = 0.4f)
        {
            CrumblingHold best = null; float bd = maxDist * maxDist;
            foreach (var h in All)
            {
                float d = ((Vector2)h.transform.position - (Vector2)p).sqrMagnitude;
                if (d < bd) { bd = d; best = h; }
            }
            return best;
        }
    }
}
