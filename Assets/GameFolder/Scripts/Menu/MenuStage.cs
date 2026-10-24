using System;
using System.Collections;
using UnityEngine;

namespace FumblePeak
{
    /// <summary>
    /// Ana menünün 3D sahnesi: zirvede şişesiyle sallanan dağcı ve "oyuna başla" sinematiği
    /// (uçurumun ucuna yürür → aşağı işer → dengesini kaybedip düşer → seviyeye geçilir).
    /// Kamera da burada yönetilir.
    /// </summary>
    public class MenuStage : MonoBehaviour
    {
        public static MenuStage Instance { get; private set; }

        public MenuCharacter character;
        public Camera cam;
        [Tooltip("Uçurumun ucu (dağcının işeyeceği nokta, ayak hizası)")]
        public Transform edgePoint;
        public Transform idleCamPose;
        public Transform cinematicCamPose;

        /// <summary>Sinematik süresi (sn) — host bu kadar bekleyip sahneyi değiştirir.</summary>
        public const float CinematicDuration = 6.2f;

        public bool Playing { get; private set; }

        Vector3 camVel;
        Vector3 lookTarget, lookVel;
        float camBlend;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (cam != null && idleCamPose != null)
            {
                cam.transform.SetPositionAndRotation(idleCamPose.position, idleCamPose.rotation);
                lookTarget = idleCamPose.position + idleCamPose.forward * 6f;
            }
        }

        public void PlayCinematic(Action onDone)
        {
            if (Playing) return;
            Playing = true;
            StartCoroutine(Run(onDone));
        }

        IEnumerator Run(Action onDone)
        {
            character.BeginCinematic(edgePoint.position, edgePoint.forward);
            float t = 0f;
            while (t < CinematicDuration - 0.45f)
            {
                t += Time.deltaTime;
                camBlend = Mathf.Clamp01(t / 1.2f);
                yield return null;
            }
            onDone?.Invoke();
        }

        void LateUpdate()
        {
            if (cam == null) return;
            float time = Time.time;

            if (!Playing)
            {
                // boşta: hafif süzülen kamera
                var p = idleCamPose.position + new Vector3(Mathf.Sin(time * 0.17f) * 0.25f, Mathf.Sin(time * 0.23f) * 0.12f, 0f);
                cam.transform.position = Vector3.SmoothDamp(cam.transform.position, p, ref camVel, 0.6f);
                var look = idleCamPose.position + idleCamPose.forward * 6f + new Vector3(Mathf.Sin(time * 0.11f) * 0.2f, 0f, 0f);
                lookTarget = Vector3.SmoothDamp(lookTarget, look, ref lookVel, 0.6f);
            }
            else
            {
                // sinematik: uçuruma yakın yan açı, düşerken karakteri takip et
                var basePos = Vector3.Lerp(idleCamPose.position, cinematicCamPose.position, Mathf.SmoothStep(0, 1, camBlend));
                cam.transform.position = Vector3.SmoothDamp(cam.transform.position, basePos, ref camVel, 0.35f);
                var hip = character.HipPosition;
                var desired = new Vector3(hip.x, Mathf.Max(hip.y, edgePoint.position.y - 6f), hip.z);
                lookTarget = Vector3.SmoothDamp(lookTarget, desired, ref lookVel, 0.18f);
            }
            cam.transform.rotation = Quaternion.LookRotation(lookTarget - cam.transform.position, Vector3.up);
        }
    }
}
