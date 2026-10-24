using System;
using UnityEngine;

namespace FumblePeak
{
    /// <summary>
    /// Ana menüdeki sarhoş dağcı. Modelin animasyonu yok → tamamen prosedürel:
    /// kemikler her karede bağlama pozuna döndürülür, sonra gövde eğilir ve kollar/bacaklar
    /// iki kemikli IK ile hedeflere uzanır.
    ///  Boşta: kayaya yaslanır, sağ elindeki şişeden ara ara içer, "Hık!" der.
    ///  Sinematik: sendeleyerek uçurumun ucuna yürür → aşağı işer → dengesini kaybedip düşer.
    /// </summary>
    public class MenuCharacter : MonoBehaviour
    {
        public Transform model;
        public Transform bottle;
        [Tooltip("Sol elin kayadaki dayanma noktası")]
        public Transform rockHand;
        public ParticleSystem pee;
        public float leanAngle = 9f;

        /// <summary>Konuşma balonu: (lokalizasyon anahtarı, süre sn).</summary>
        public static event Action<string, float> Say;

        public Vector3 HipPosition => hips != null ? hips.position : transform.position + Vector3.up;
        public Vector3 BubbleAnchor => head != null ? head.position + Vector3.up * 0.32f : transform.position + Vector3.up * 2f;

        // ---- kemikler ----
        Transform hips, spine, spine1, spine2, neck, head;
        readonly Transform[] up = new Transform[4], lo = new Transform[4], en = new Transform[4]; // 0 SolKol 1 SağKol 2 SolBacak 3 SağBacak
        Transform[] bones;
        Quaternion[] bindRot;
        Vector3 hipsBindPos;
        Quaternion modelBindRot;

        // ---- durum ----
        Vector3 homePos; float homeYaw;
        Vector3 pos; float yaw, roll, pitch;
        float drinkWait = 1.6f, drinkT = -1f, hicT = -1f;
        int drinkCount;

        bool cine; float ct;
        Vector3 walkStart, standPos, edgeDir;
        float startYaw, edgeYaw, rollAtStart;
        bool saidNature, saidAhh, saidHic2, saidScream;

        bool windmill, flailLegs;
        bool falling; Vector3 fallPos, fallVel, hipOffset, spinAxis; Quaternion fallRot;
        bool bottleFree; Vector3 bottleVel, bottleSpin;

        void Awake()
        {
            foreach (var a in model.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            hips = Bone("Hips"); spine = Bone("Spine"); spine1 = Bone("Spine1"); spine2 = Bone("Spine2");
            neck = Bone("Neck"); head = Bone("Head");
            string[] u = { "LeftArm", "RightArm", "LeftUpLeg", "RightUpLeg" };
            string[] l = { "LeftForeArm", "RightForeArm", "LeftLeg", "RightLeg" };
            string[] e = { "LeftHand", "RightHand", "LeftFoot", "RightFoot" };
            for (int i = 0; i < 4; i++) { up[i] = Bone(u[i]); lo[i] = Bone(l[i]); en[i] = Bone(e[i]); }

            bones = model.GetComponentsInChildren<Transform>(true);
            bindRot = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++) bindRot[i] = bones[i].localRotation;
            hipsBindPos = hips.localPosition;
            modelBindRot = model.localRotation;

            homePos = transform.position;
            homeYaw = transform.eulerAngles.y;
            pos = homePos; yaw = homeYaw; roll = leanAngle;
            if (pee != null) { var em = pee.emission; em.enabled = false; }
        }

        Transform Bone(string shortName)
        {
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name;
                int c = n.LastIndexOf(':');
                if (c >= 0) n = n.Substring(c + 1);
                if (n == shortName) return t;
            }
            Debug.LogWarning("[MenuCharacter] kemik yok: " + shortName);
            return model;
        }

        /// <summary>Sinematiği başlat: uçurumun kenarı (ayak hizası) ve dışarı bakan yön.</summary>
        public void BeginCinematic(Vector3 edge, Vector3 outward)
        {
            if (cine) return;
            cine = true; ct = 0f;
            edgeDir = new Vector3(outward.x, 0, outward.z).normalized;
            standPos = new Vector3(edge.x, homePos.y, edge.z) - edgeDir * 0.16f;
            walkStart = pos;
            startYaw = yaw;
            edgeYaw = Mathf.Atan2(edgeDir.x, edgeDir.z) * Mathf.Rad2Deg;
            rollAtStart = roll;
            drinkT = -1f;
        }

        // =====================================================================
        void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float t = Time.time;

            // 1) bağlama pozu
            for (int i = 0; i < bones.Length; i++) bones[i].localRotation = bindRot[i];
            hips.localPosition = hipsBindPos;

            Vector3[] hand = new Vector3[2];
            Vector3[] foot = new Vector3[2];
            float headTilt = 0f, headTurn = 0f, spineBend = 0f, drinkBlend = 0f;
            bool feetIK = true, peeing = false;
            windmill = false; flailLegs = false;
            Quaternion F;

            if (!cine)
            {
                // ---------------- BOŞTA: yaslan, iç, hıçkır ----------------
                float sway = Mathf.Sin(t * 0.9f) * 1.8f + Mathf.Sin(t * 1.7f + 1f) * 0.8f;
                roll = leanAngle + sway * 0.6f;
                pitch = Mathf.Sin(t * 0.6f) * 1.5f;
                yaw = homeYaw + Mathf.Sin(t * 0.4f) * 3f;
                pos = homePos + Vector3.down * 0.03f;

                if (drinkT < 0f)
                {
                    drinkWait -= dt;
                    if (drinkWait <= 0f) { drinkT = 0f; }
                }
                else
                {
                    drinkT += dt;
                    if (drinkT > 1.8f)
                    {
                        drinkT = -1f;
                        drinkWait = UnityEngine.Random.Range(3.5f, 5.5f);
                        drinkCount++;
                        hicT = 0f;
                        Say?.Invoke(drinkCount % 3 == 0 ? "menu.burp" : "menu.hic", 1.3f);
                    }
                }
                if (drinkT >= 0f)
                    drinkBlend = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0f, 0.45f, drinkT)) * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.3f, 1.75f, drinkT)));
                headTilt = -24f * drinkBlend + Mathf.Sin(t * 1.1f) * 3f;
                headTurn = Mathf.Sin(t * 0.5f) * 8f * (1f - drinkBlend);
                spineBend = -roll * 0.35f;

                F = Frame();
                foot[0] = pos + Yawq() * new Vector3(-0.05f, 0.12f, 0.0f);
                foot[1] = pos + Yawq() * new Vector3(0.21f, 0.12f, 0.07f);
                hand[0] = rockHand != null ? rockHand.position + new Vector3(0, Mathf.Sin(t * 0.9f) * 0.01f, 0) : pos + F * new Vector3(-0.3f, 0.95f, 0f);
                hand[1] = pos + F * new Vector3(0.17f, 1.0f, 0.22f); // şişe karında (içme hedefi aşağıda)
            }
            else
            {
                ct += dt;
                F = CinematicPose(t, dt, hand, foot, ref headTilt, ref headTurn, ref spineBend, ref feetIK, ref peeing);
            }

            // 2) model kökü
            if (falling)
            {
                F = fallRot;
                model.rotation = fallRot * modelBindRot;
                model.position = fallPos - fallRot * hipOffset;
                model.position += fallPos - hips.position; // kalça tam yörüngede kalsın
            }
            else
            {
                model.rotation = F * modelBindRot;
                model.position = pos;
            }
            if (hicT >= 0f)
            {
                hicT += dt;
                float j = Mathf.Sin(Mathf.Clamp01(hicT / 0.25f) * Mathf.PI);
                model.position += Vector3.up * j * 0.035f;
                headTilt += j * 8f;
                if (hicT > 0.25f) hicT = -1f;
            }

            Vector3 fwd = F * Vector3.forward, right = F * Vector3.right, upv = F * Vector3.up;
            if (windmill) Windmill(hand, F, t);
            if (flailLegs)
                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? -1f : 1f;
                    float ph = t * 11f + s * Mathf.PI;
                    foot[s] = hips.position + F * new Vector3(side * 0.18f, -0.75f + Mathf.Sin(ph) * 0.12f, Mathf.Cos(ph) * 0.3f);
                }

            // 3) gövde ve kafa
            Bend(spine1, fwd, spineBend * 0.5f);
            Bend(spine2, fwd, spineBend * 0.5f);
            Bend(neck, right, headTilt * 0.4f);
            Bend(head, right, headTilt * 0.6f);
            Bend(head, upv, headTurn);

            // 4) bacaklar
            if (feetIK)
                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? -1f : 1f;
                    TwoBoneIK(up[2 + s], lo[2 + s], en[2 + s], foot[s], F * new Vector3(side * 0.1f, 0f, 1f));
                }
            else
                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? -1f : 1f;
                    TwoBoneIK(up[2 + s], lo[2 + s], en[2 + s], foot[s], F * new Vector3(side * 0.2f, 0.3f, 1f));
                }

            // 5) içerken sağ el ağza
            Vector3 mouth = head.position + fwd * 0.12f - upv * 0.03f;
            if (drinkBlend > 0f)
            {
                var drinkHand = mouth + fwd * 0.12f + Vector3.up * 0.07f;
                hand[1] = Vector3.Lerp(hand[1], drinkHand, drinkBlend);
            }

            // 6) kollar
            TwoBoneIK(up[0], lo[0], en[0], hand[0], F * new Vector3(-0.4f, -1f, -0.45f));
            TwoBoneIK(up[1], lo[1], en[1], hand[1], F * new Vector3(0.45f, -1f, -0.35f));

            // 7) şişe
            UpdateBottle(dt, fwd, mouth, drinkBlend);

            // 8) çiş
            if (pee != null)
            {
                var em = pee.emission;
                em.enabled = peeing;
                if (peeing)
                {
                    pee.transform.position = hips.position + fwd * 0.16f - upv * 0.12f;
                    pee.transform.rotation = Quaternion.LookRotation(fwd * 2.4f + Vector3.up * 0.9f + right * Mathf.Sin(t * 5f) * 0.3f);
                }
            }
        }

        Quaternion Yawq() => Quaternion.Euler(0, yaw, 0);
        Quaternion Frame() => Quaternion.Euler(0, yaw, 0) * Quaternion.AngleAxis(pitch, Vector3.right) * Quaternion.AngleAxis(roll, Vector3.forward);

        // =====================================================================
        Quaternion CinematicPose(float t, float dt, Vector3[] hand, Vector3[] foot,
            ref float headTilt, ref float headTurn, ref float spineBend, ref bool feetIK, ref bool peeing)
        {
            const float tUnlean = 0.4f, tWalkEnd = 2.3f, tPeeEnd = 4.45f, tTip = 4.85f;
            Quaternion F;

            if (ct < tWalkEnd)
            {
                // ---- doğrul + sendeleyerek yürü ----
                float u = Mathf.InverseLerp(tUnlean, tWalkEnd, ct);
                float su = Mathf.SmoothStep(0, 1, u);
                Vector3 path = standPos - walkStart; path.y = 0;
                Vector3 lat = Vector3.Cross(Vector3.up, path.normalized);
                pos = Vector3.Lerp(walkStart, standPos, su) + lat * Mathf.Sin(u * Mathf.PI * 2.5f) * Mathf.Sin(u * Mathf.PI) * 0.22f;

                float travelYaw = Mathf.Atan2(path.x, path.z) * Mathf.Rad2Deg;
                yaw = u < 0.75f ? Mathf.LerpAngle(startYaw, travelYaw, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0f, 0.25f, u)))
                                : Mathf.LerpAngle(travelYaw, edgeYaw, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.75f, 1f, u)));
                if (ct < tUnlean) yaw = startYaw;

                float walking = ct < tUnlean ? 0f : 1f;
                float phase = (ct - tUnlean) * 2.4f * Mathf.PI;
                roll = Mathf.Lerp(rollAtStart, 0f, Mathf.Clamp01(ct / tUnlean)) + Mathf.Sin(phase * 0.5f) * 5f * walking;
                pitch = 4f * walking;
                pos.y = homePos.y - 0.03f - Mathf.Abs(Mathf.Sin(phase)) * 0.025f * walking;
                F = Frame();
                var Y = Yawq();
                for (int s = 0; s < 2; s++)
                {
                    float side = s == 0 ? -1f : 1f;
                    float ph = phase + (s == 0 ? 0f : Mathf.PI);
                    float stride = walking * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.9f, 1f, u)));
                    foot[s] = pos + Y * new Vector3(side * 0.11f, 0.12f + Mathf.Max(0f, Mathf.Cos(ph)) * 0.09f * stride, Mathf.Sin(ph) * 0.17f * stride);
                    foot[s].y = Mathf.Max(foot[s].y, homePos.y + 0.12f);
                }
                foot[1] += Y * new Vector3(0.1f, 0, 0) * (1f - walking); // doğrulurken yaslanma duruşundan
                hand[0] = pos + F * new Vector3(-0.27f, 0.93f, Mathf.Sin(phase + Mathf.PI) * 0.12f * walking);
                if (ct < tUnlean && rockHand != null) hand[0] = Vector3.Lerp(rockHand.position, hand[0], ct / tUnlean);
                hand[1] = pos + F * new Vector3(0.28f, 1.18f + Mathf.Sin(phase * 2f) * 0.04f, 0.18f); // şişe havada
                headTilt = Mathf.Sin(t * 2.1f) * 6f;
                spineBend = Mathf.Sin(phase * 0.5f) * -3f;
                if (!saidNature && ct > 0.15f) { saidNature = true; Say?.Invoke("menu.nature", 1.6f); }
            }
            else if (ct < tPeeEnd)
            {
                // ---- uçurumun kenarında çiş ----
                float k = Mathf.InverseLerp(tWalkEnd, tPeeEnd, ct);
                pos = standPos + Vector3.down * 0.03f;
                yaw = edgeYaw + Mathf.Sin(t * 0.8f) * 3f;
                roll = Mathf.Sin(t * 1.4f) * (2f + k * 4f);
                pitch = Mathf.Sin(t * 1.1f) * 2f + k * 3f;
                F = Frame();
                var Y = Yawq();
                foot[0] = standPos + Y * new Vector3(-0.13f, 0.12f, 0f);
                foot[1] = standPos + Y * new Vector3(0.13f, 0.12f, 0.02f);
                hand[0] = pos + F * new Vector3(-0.02f, 0.9f, 0.2f);       // önde
                float cheers = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(2.9f, 3.3f, ct));
                hand[1] = pos + F * Vector3.Lerp(new Vector3(0.3f, 1.0f, 0.12f), new Vector3(0.32f, 1.62f, 0.15f), cheers); // şişeyi kaldır
                headTilt = Mathf.Lerp(14f, -16f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(2.5f, 3.0f, ct))); // önce aşağı bak, sonra keyif
                peeing = ct > 2.45f && ct < tPeeEnd - 0.1f;
                if (!saidAhh && ct > 2.6f) { saidAhh = true; Say?.Invoke("menu.ahh", 1.5f); }
                if (!saidHic2 && ct > 3.95f) { saidHic2 = true; hicT = 0f; Say?.Invoke("menu.hic", 0.6f); }
            }
            else if (ct < tTip)
            {
                // ---- öne devril ----
                float k = Mathf.InverseLerp(tPeeEnd, tTip, ct);
                pos = standPos + Vector3.down * 0.03f;
                yaw = edgeYaw;
                roll = 0f;
                pitch = 5f + 38f * k * k;
                F = Frame();
                var Y = Yawq();
                foot[0] = standPos + Y * new Vector3(-0.13f, 0.12f, 0f);
                foot[1] = standPos + Y * new Vector3(0.13f, 0.12f, 0.02f);
                windmill = true;
                headTilt = -10f;
                if (!saidScream) { saidScream = true; Say?.Invoke("menu.scream", 2.2f); }
            }
            else
            {
                // ---- serbest düşüş ----
                pitch = 43f; F = Frame();
                if (!falling)
                {
                    falling = true;
                    hipOffset = Quaternion.Inverse(F) * (hips.position - model.position);
                    fallPos = hips.position;
                    fallRot = F;
                    fallVel = Yawq() * new Vector3(0f, 1.6f, 1.9f);
                    spinAxis = (F * Vector3.right + F * Vector3.forward * 0.25f).normalized;
                    bottleFree = true;
                    bottleVel = Yawq() * new Vector3(0.8f, 3.2f, 1.2f);
                    bottleSpin = new Vector3(420f, 90f, 260f);
                }
                fallVel += Physics.gravity * dt;
                fallPos += fallVel * dt;
                fallRot = Quaternion.AngleAxis(260f * dt, spinAxis) * fallRot;
                F = fallRot;
                windmill = true; flailLegs = true;
                feetIK = false;
                headTilt = -20f;
            }
            return F;
        }

        void Windmill(Vector3[] hand, Quaternion F, float t)
        {
            for (int s = 0; s < 2; s++)
            {
                float side = s == 0 ? -1f : 1f;
                float ph = t * 15f + s * 1.9f;
                var shoulder = up[s].position;
                hand[s] = shoulder + F * new Vector3(side * 0.28f, 0.18f + Mathf.Sin(ph) * 0.3f, Mathf.Cos(ph) * 0.3f);
            }
        }

        void UpdateBottle(float dt, Vector3 fwd, Vector3 mouth, float drink)
        {
            if (bottle == null) return;
            if (bottleFree)
            {
                bottleVel += Physics.gravity * dt;
                bottle.position += bottleVel * dt;
                bottle.rotation = Quaternion.Euler(bottleSpin * dt) * bottle.rotation;
                return;
            }
            var h = en[1];
            var grip = h.position + (h.position - lo[1].position).normalized * 0.06f;
            Vector3 axis = Vector3.Slerp(Vector3.Lerp(Vector3.up, fwd, 0.12f).normalized, (mouth - grip).normalized, drink);
            bottle.position = grip;
            bottle.rotation = Quaternion.FromToRotation(Vector3.up, axis);
        }

        static void Bend(Transform b, Vector3 axis, float deg)
        {
            if (Mathf.Abs(deg) > 0.001f) b.rotation = Quaternion.AngleAxis(deg, axis) * b.rotation;
        }

        /// <summary>Analitik iki kemikli IK (D. Holden yöntemi). İpucu = dirseğin/dizin yöneleceği taraf.</summary>
        static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 t, Vector3 hint)
        {
            Vector3 ap = a.position, bp = b.position, cp = c.position;
            float lab = (bp - ap).magnitude, lcb = (bp - cp).magnitude;
            float lat = Mathf.Clamp((t - ap).magnitude, 0.01f, lab + lcb - 0.01f);

            float ac_ab_0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((cp - ap).normalized, (bp - ap).normalized), -1f, 1f));
            float ba_bc_0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((ap - bp).normalized, (cp - bp).normalized), -1f, 1f));
            float ac_at_0 = Mathf.Acos(Mathf.Clamp(Vector3.Dot((cp - ap).normalized, (t - ap).normalized), -1f, 1f));

            float ac_ab_1 = Mathf.Acos(Mathf.Clamp((lcb * lcb - lab * lab - lat * lat) / (-2f * lab * lat), -1f, 1f));
            float ba_bc_1 = Mathf.Acos(Mathf.Clamp((lat * lat - lab * lab - lcb * lcb) / (-2f * lab * lcb), -1f, 1f));

            Vector3 axis0 = Vector3.Cross(cp - ap, hint).normalized;
            if (axis0.sqrMagnitude < 1e-6f) axis0 = Vector3.forward;
            Vector3 axis1 = Vector3.Cross(cp - ap, t - ap);
            axis1 = axis1.sqrMagnitude < 1e-8f ? axis0 : axis1.normalized;

            Quaternion r0 = Quaternion.AngleAxis((ac_ab_1 - ac_ab_0) * Mathf.Rad2Deg, axis0);
            Quaternion r1 = Quaternion.AngleAxis((ba_bc_1 - ba_bc_0) * Mathf.Rad2Deg, axis0);
            Quaternion r2 = Quaternion.AngleAxis(ac_at_0 * Mathf.Rad2Deg, axis1);

            b.rotation = r1 * b.rotation;
            a.rotation = r2 * r0 * a.rotation;
        }
    }
}
