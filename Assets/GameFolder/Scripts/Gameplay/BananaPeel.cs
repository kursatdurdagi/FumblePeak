using UnityEngine;

namespace FumblePeak
{
    /// <summary>
    /// Maymunun taşa fırlattığı muz kabuğu. Tutamağın köküne eklenir: önce maymunun elinden taşa
    /// kavis çizerek uçar, sonra taşın üstünde durur. O taşa basan / tutunan uzuv kayar
    /// (ClimberBody), kabuk da uçup gider. Bir süre sonra kendiliğinden düşer.
    /// Tüm istemcilerde RPC ile eklenir; sunucu "Landed" bilgisini kullanır.
    /// </summary>
    public class BananaPeel : MonoBehaviour
    {
        public const float Flight = 0.6f;
        public const float Lifetime = 35f;

        Transform vis;
        Vector3 from, rest;
        float t0, slipT = -1f;
        Vector3 slipVel, spin;

        /// <summary>Taşa kondu mu (kaydırır mı)?</summary>
        public bool Landed => slipT < 0f && Time.time >= t0 + Flight;

        public static BananaPeel Throw(Grip hold, Vector3 fromPos, Material mat)
        {
            var peel = hold.GetComponent<BananaPeel>();
            if (peel == null) peel = hold.gameObject.AddComponent<BananaPeel>();
            peel.Launch(fromPos, mat);
            return peel;
        }

        void Launch(Vector3 fromPos, Material mat)
        {
            if (vis == null) vis = PestArt.Peel(null, 1.1f, mat);
            from = fromPos;
            t0 = Time.time;
            slipT = -1f;
            // taşın üstü, kameraya bakan ön yüzüne yakın
            var col = GetComponent<Collider>();
            float r = col is SphereCollider sc ? sc.radius * transform.lossyScale.x : 0.14f;
            rest = transform.position + new Vector3(Random.Range(-0.03f, 0.03f), r * 0.75f, -0.13f);
            vis.position = from;
        }

        /// <summary>Uzuv kaydı: kabuk fırlayıp düşer.</summary>
        public void Slip()
        {
            if (slipT >= 0f || vis == null) return;
            slipT = Time.time;
            slipVel = new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(1.5f, 2.5f), -1f);
            spin = Random.insideUnitSphere * 720f;
        }

        void Update()
        {
            if (vis == null) return;
            float now = Time.time;
            if (slipT >= 0f)
            {
                slipVel += Physics.gravity * Time.deltaTime;
                vis.position += slipVel * Time.deltaTime;
                vis.Rotate(spin * Time.deltaTime, Space.World);
                if (now - slipT > 1.5f) Remove();
                return;
            }
            float k = (now - t0) / Flight;
            if (k < 1f)
            {
                // kavisli uçuş + takla
                var p = Vector3.Lerp(from, rest, k) + Vector3.up * (4f * k * (1f - k) * 0.5f);
                vis.position = p;
                vis.rotation = Quaternion.Euler(k * 720f, k * 360f, 0f);
            }
            else
            {
                vis.position = rest;
                vis.rotation = Quaternion.Euler(-15f, 30f, 0f);
                if (now - t0 > Lifetime) Slip();
            }
        }

        void Remove()
        {
            if (vis != null) Destroy(vis.gameObject);
            Destroy(this);
        }

        void OnDestroy()
        {
            if (vis != null) Destroy(vis.gameObject);
        }

        public static BananaPeel Nearest(Vector3 p, float maxDist = 0.4f)
        {
            BananaPeel best = null; float bd = maxDist * maxDist;
            foreach (var b in FindObjectsByType<BananaPeel>(FindObjectsSortMode.None))
            {
                float d = ((Vector2)b.transform.position - (Vector2)p).sqrMagnitude;
                if (d < bd) { bd = d; best = b; }
            }
            return best;
        }
    }
}
