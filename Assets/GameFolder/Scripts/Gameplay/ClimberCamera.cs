using UnityEngine;

namespace FumblePeak
{
    /// <summary>Gövdeyi yumuşak takip eden yan kamera + düşüşte sarsıntı.</summary>
    public class ClimberCamera : MonoBehaviour
    {
        public Vector3 offset = new Vector3(0f, 0.35f, -4.6f);
        public float followSpeed = 4f;
        public float lookAhead = 0.25f;

        Vector3 vel;
        float shake;

        void OnEnable()
        {
            ClimberBody.OnRespawned += OnRespawn;
            ClimberBody.OnLimbGaveUp += OnGaveUp;
        }

        void OnDisable()
        {
            ClimberBody.OnRespawned -= OnRespawn;
            ClimberBody.OnLimbGaveUp -= OnGaveUp;
        }

        void OnRespawn(string _) => shake = 0.4f;
        void OnGaveUp(int _) => shake = Mathf.Max(shake, 0.12f);

        void LateUpdate()
        {
            var body = ClimberBody.Instance;
            if (body == null) return;
            Vector3 focus = body.RenderTorsoPos;
            Vector3 desired = focus + offset + Vector3.up * lookAhead;
            if ((desired - transform.position).sqrMagnitude > 400f) transform.position = desired;
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref vel, 1f / followSpeed);
            if (shake > 0f)
            {
                transform.position += (Vector3)Random.insideUnitCircle * shake * 0.25f;
                shake = Mathf.Max(0f, shake - Time.deltaTime);
            }
            transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        }
    }
}
