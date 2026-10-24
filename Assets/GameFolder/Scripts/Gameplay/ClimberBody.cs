using Mirror;
using UnityEngine;

namespace FumblePeak
{
    /// <summary>
    /// Tek, paylaşılan tırmanıcı bedeni. Fizik SADECE sunucuda (host) çalışır,
    /// istemciler senkronize edilen pozları yumuşatarak gösterir (ClimberVisual).
    /// Oyun düzlemi: XY, Z = 0 (2.5D tırmanış duvarı).
    /// </summary>
    public class ClimberBody : NetworkBehaviour
    {
        public static ClimberBody Instance { get; private set; }

        [Header("Parçalar")]
        public Rigidbody torso;
        public Rigidbody[] effectors = new Rigidbody[4];
        [Tooltip("Omuz / kalça noktaları (gövdeye göre yerel)")]
        public Vector3[] anchorLocal = new Vector3[4];
        public float[] reach = { 0.5f, 0.5f, 0.8f, 0.8f };

        [Header("Kuvvetler")]
        public float limbSpring = 220f;
        public float limbDamping = 16f;
        [Tooltip("Gövdeyi hedefe çekme sertliği (ivme / metre)")]
        public float pullStrength = 70f;
        public float pullDampingRatio = 13f;
        [Range(0, 1)] public float gravityCompensation = 0.9f;
        public float maxPullAccel = 45f;
        [Tooltip("Omzun tutunan elin üstüne çıkabileceği en fazla yükseklik")]
        public float shoulderAboveHand = 0.3f;
        [Header("Kolay oynanış")]
        [Tooltip("Tutununca gövde kendiliğinden yukarı çekilir (m/sn)")]
        public float autoPullSpeed = 1.7f;
        [Tooltip("Otomatik çekmede omzun elin ne kadar altında duracağı")]
        public float armHang = -0.2f;
        [Tooltip("Kolları da yöneten oyuncunun bacakları basamakları kendisi bulur")]
        public bool autoLegs = true;
        [Tooltip("Tek oyuncuda dayanıklılık tüketim çarpanı")]
        public float singlePlayerDrain = 0.45f;
        public float uprightStrength = 140f;
        public float uprightDamping = 14f;
        public float gripRadius = 0.3f;
        public LayerMask gripMask = ~0;
        [Tooltip("Ayakların basabildiği zemin (Grip/Climber hariç)")]
        public LayerMask groundMask = ~((1 << 8) | (1 << 9));
        public float standHeight = 0.85f;

        [Header("Dayanıklılık (GDD 2.2)")]
        public float maxStamina = 10f;
        public float regenPerSecond = 3f;
        public float forcedCooldown = 1f;

        [Header("Checkpoint")]
        public float fallRespawnDistance = 8f;

        // ---------- Senkronize durum ----------
        [SyncVar] Vector2 sTorso;
        [SyncVar] float sTorsoRot;
        [SyncVar] Vector2 sE0, sE1, sE2, sE3;
        [SyncVar] Vector2 sT0, sT1, sT2, sT3;
        [SyncVar] byte sGrab;
        [SyncVar] byte sFist;
        [SyncVar] byte sGround;
        [SyncVar] byte sF0, sF1, sF2, sF3;
        [SyncVar] public float bestHeight;

        // ---------- Sunucu durumu ----------
        readonly Vector2[] targets = new Vector2[4];
        readonly bool[] wantGrab = new bool[4];
        readonly FixedJoint[] joints = new FixedJoint[4];
        readonly Grip[] grips = new Grip[4];
        readonly Vector3[] grabPoint = new Vector3[4];
        readonly Vector2[] cursorAtGrab = new Vector2[4];
        readonly Vector3[] anchorOffsetAtGrab = new Vector3[4];
        readonly float[] stamina = new float[4];
        readonly float[] cooldown = new float[4];
        readonly Collider[] overlap = new Collider[8];
        Vector3 checkpoint;
        int lastGroundedFeet;
        Vector2 bodyTarget;
        readonly Vector2[] lastTarget = new Vector2[4];
        readonly Grip[] lastGrip = new Grip[4];
        readonly float[] manualTime = new float[4];
        readonly float[] fistSince = new float[4];
        float autoLegTimer;
        Vector3 placeAt;
        bool hasPlace;

        // ---------- Okuma (tüm istemciler) ----------
        public Vector2 TorsoPos => isServer ? (Vector2)torso.position : sTorso;
        public float TorsoRot => isServer ? torso.rotation.eulerAngles.z : sTorsoRot;
        public Vector2 EffectorPos(int i) => isServer ? (Vector2)effectors[i].position : i switch { 0 => sE0, 1 => sE1, 2 => sE2, _ => sE3 };
        public Vector2 Target(int i) => isServer ? targets[i] : i switch { 0 => sT0, 1 => sT1, 2 => sT2, _ => sT3 };
        public bool IsGrabbing(int i) => LimbUtil.Has(sGrab, i);
        public bool IsFist(int i) => LimbUtil.Has(sFist, i);
        /// <summary>Zemine basan ayak sayısı (tüm istemciler).</summary>
        public int GroundedFeet => isServer ? lastGroundedFeet : sGround;
        public float Fatigue01(int i) => (i switch { 0 => sF0, 1 => sF1, 2 => sF2, _ => sF3 }) / 255f;
        public Vector2 RenderTorsoPos => isServer ? (Vector2)torso.transform.position : sTorso;
        public float RenderTorsoRot => isServer ? torso.transform.eulerAngles.z : sTorsoRot;
        public Vector2 RenderEffectorPos(int i) => isServer ? (Vector2)effectors[i].transform.position : EffectorPos(i);
        public Vector2 RenderAnchor(int i) => RenderTorsoPos + (Vector2)(Quaternion.Euler(0, 0, RenderTorsoRot) * anchorLocal[i]);

        public Vector2 AnchorWorld(int i)
        {
            var rot = Quaternion.Euler(0, 0, TorsoRot);
            return TorsoPos + (Vector2)(rot * anchorLocal[i]);
        }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Spawn öncesi sunucuda konumlandırma.</summary>
        public void ServerPlaceAt(Vector3 pos)
        {
            placeAt = pos;
            hasPlace = true;
        }

        public override void OnStartServer()
        {
            Instance = this;
            var start = hasPlace ? placeAt : torso.position;
            checkpoint = start;
            TeleportAll(start);
            for (int i = 0; i < 4; i++)
            {
                stamina[i] = maxStamina;
                targets[i] = effectors[i].position;
            }
            IgnoreSelfCollisions();
        }

        public override void OnStartClient()
        {
            Instance = this;
            if (isServer) return;
            // İstemcide fizik yok: sadece görsel.
            torso.isKinematic = true;
            foreach (var e in effectors)
            {
                e.isKinematic = true;
                foreach (var j in e.GetComponents<Joint>()) Destroy(j);
            }
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        }

        void IgnoreSelfCollisions()
        {
            var cols = GetComponentsInChildren<Collider>();
            for (int a = 0; a < cols.Length; a++)
                for (int b = a + 1; b < cols.Length; b++)
                    Physics.IgnoreCollision(cols[a], cols[b]);
        }

        void TeleportAll(Vector3 torsoPos)
        {
            torsoPos.z = 0;
            for (int i = 0; i < 4; i++) ReleaseInternal(i);
            torso.position = torsoPos;
            torso.rotation = Quaternion.identity;
            torso.linearVelocity = Vector3.zero;
            torso.angularVelocity = Vector3.zero;
            for (int i = 0; i < 4; i++)
            {
                var a = torsoPos + anchorLocal[i];
                var dir = LimbUtil.IsArm(i) ? new Vector3(i == 0 ? -0.35f : 0.35f, 1f, 0).normalized : Vector3.down;
                var p = a + dir * reach[i] * 0.85f;
                effectors[i].position = p;
                effectors[i].linearVelocity = Vector3.zero;
                targets[i] = p;
                wantGrab[i] = false;
            }
        }

        // ---------- Oyuncu girdileri (FumblePlayer komutlarından) ----------
        [Server]
        public void ServerSetTarget(int limb, Vector2 p)
        {
            if (limb < 0 || limb > 3) return;
            if (!float.IsFinite(p.x) || !float.IsFinite(p.y)) return;
            if ((targets[limb] - p).sqrMagnitude > 0.0001f) manualTime[limb] = Time.time;
            targets[limb] = p;
        }

        [Server]
        public void ServerToggleGrab(int limb)
        {
            if (limb < 0 || limb > 3) return;
            if (wantGrab[limb] || joints[limb] != null)
            {
                wantGrab[limb] = false;
                ReleaseInternal(limb);
            }
            else if (cooldown[limb] <= 0f)
            {
                wantGrab[limb] = true; // yumruk; hedefe varınca / durunca en yakın kayaya kenetlenir
                fistSince[limb] = Time.time;
                manualTime[limb] = Time.time;
            }
        }

        [Server]
        public void ServerRelease(int limb)
        {
            if (limb < 0 || limb > 3) return;
            wantGrab[limb] = false;
            ReleaseInternal(limb);
            manualTime[limb] = Time.time;
        }

        /// <summary>Tutunma isteği: zaten tutuyorsa dokunma (toggle değil).</summary>
        [Server]
        public void ServerGrab(int limb)
        {
            if (limb < 0 || limb > 3 || cooldown[limb] > 0f) return;
            if (joints[limb] != null) ReleaseInternal(limb); // başka yere tutunmak için önce bırak
            wantGrab[limb] = true;
            fistSince[limb] = Time.time;
            manualTime[limb] = Time.time;
        }

        [Server]
        public void ServerSetCheckpoint(Vector3 p)
        {
            if (p.y > checkpoint.y - 0.1f && (p - checkpoint).sqrMagnitude > 0.01f)
            {
                checkpoint = p;
                RpcCheckpoint();
            }
        }

        [Server]
        public void ServerRespawn(string reason)
        {
            TeleportAll(checkpoint);
            for (int i = 0; i < 4; i++) stamina[i] = maxStamina;
            RpcRespawn(reason);
        }

        [Server]
        public void ServerSummit()
        {
            RpcSummit();
        }

        [Server]
        public void ServerBounce(Vector3 impulse)
        {
            for (int i = 0; i < 4; i++) { wantGrab[i] = false; ReleaseInternal(i); }
            torso.AddForce(impulse, ForceMode.VelocityChange);
            foreach (var e in effectors) e.AddForce(impulse, ForceMode.VelocityChange);
        }

        // ---------- Fizik ----------
        void FixedUpdate()
        {
            if (!isServer) return;
            float dt = Time.fixedDeltaTime;

            int grabbedCount = 0;
            for (int i = 0; i < 4; i++) if (joints[i] != null) grabbedCount++;
            if (grabbedCount == 0) bodyTarget = torso.position; // serbestken hedef = mevcut konum
            int groundedFeet = 0;
            Vector3 footSum = Vector3.zero;
            int supports = grabbedCount + lastGroundedFeet; // zemine basan ayaklar da ağırlık taşır
            // 3+ destek = dinlenme (tükenmez), 2 = normal, 1 = 2 kat hızlı (GDD 2.2)
            float load = supports >= 3 ? 0f : supports == 2 ? 1f : 2f;
            if (FumbleNetworkManager.IsSinglePlayer) load *= singlePlayerDrain;

            for (int i = 0; i < 4; i++)
            {
                var eff = effectors[i];
                Vector3 anchor = AnchorWorld(i);
                cooldown[i] -= dt;

                if (joints[i] != null)
                {
                    // Kaygan yüzey: yavaşça aşağı kayar
                    var g = grips[i];
                    if (g != null && g.type == GripType.Slippery)
                    {
                        grabPoint[i] += Vector3.down * g.slideSpeed * dt;
                        joints[i].connectedAnchor = grabPoint[i];
                        if (!StillOnGrip(grabPoint[i], g)) { ReleaseInternal(i); continue; }
                    }

                    // Dayanıklılık
                    if (load > 0f) stamina[i] -= dt * load;
                    else stamina[i] = Mathf.Min(maxStamina, stamina[i] + 0.5f * dt);
                    if (stamina[i] <= 0f)
                    {
                        stamina[i] = 0f;
                        wantGrab[i] = false;
                        ReleaseInternal(i);
                        cooldown[i] = forcedCooldown;
                        RpcLimbGaveUp((byte)i);
                    }
                }
                else
                {
                    // Serbest uzuv hedefe uzanır (erişim sınırı içinde)
                    Vector3 tgt = targets[i];
                    Vector3 desired = anchor + Vector3.ClampMagnitude(tgt - anchor, reach[i]);
                    desired.z = 0;
                    lastTarget[i] = targets[i];
                    Vector3 rel = eff.linearVelocity - torso.linearVelocity;
                    Vector3 f = (desired - eff.position) * limbSpring - rel * limbDamping;
                    eff.AddForce(f, ForceMode.Acceleration);

                    if (!LimbUtil.IsArm(i) && Physics.CheckSphere(eff.position, 0.2f, groundMask, QueryTriggerInteraction.Ignore))
                    {
                        groundedFeet++;
                        footSum += eff.position;
                    }

                    if (wantGrab[i] && cooldown[i] <= 0f)
                    {
                        bool arrived = ((Vector2)eff.position - targets[i]).sqrMagnitude < 0.25f * 0.25f;
                        bool settled = rel.sqrMagnitude < 0.5f * 0.5f;
                        if (arrived || settled) TryGrab(i);
                        if (joints[i] == null && Time.time - fistSince[i] > 2.5f && !arrived) wantGrab[i] = false; // boşluğa yumruk
                    }
                    stamina[i] = Mathf.Min(maxStamina, stamina[i] + regenPerSecond * dt);
                }
            }

            lastGroundedFeet = groundedFeet;

            // Tutunurken gövdeyi hedefe taşı (tüm tutunan uzuvlar aynı hedefi paylaşır → birbirine karşı çekmez)
            int nowGrabbed = 0;
            for (int i = 0; i < 4; i++) if (joints[i] != null) { nowGrabbed++; lastTarget[i] = targets[i]; }
            if (nowGrabbed > 0)
            {
                AutoPull(dt);
                ClampBodyTarget();
                Vector2 err = bodyTarget - (Vector2)torso.position;
                Vector2 v = torso.linearVelocity;
                Vector2 a = err * pullStrength - v * pullDampingRatio + Vector2.up * 9.81f * gravityCompensation;
                a = Vector2.ClampMagnitude(a, maxPullAccel);
                torso.AddForce(a, ForceMode.Acceleration);
            }

            // Ayakta durma desteği: zemine basan ayakların üstünde gövdeyi tut
            if (groundedFeet > 0)
            {
                Vector3 foot = footSum / groundedFeet;
                float dy = (foot.y + standHeight) - torso.position.y;
                float dx = foot.x - torso.position.x;
                Vector3 v = torso.linearVelocity;
                float ay = Mathf.Clamp(dy * 90f - v.y * 12f + (dy > 0 ? 9.81f : 0f), 0f, 45f);
                float ax = Mathf.Clamp(dx * 25f - v.x * 5f, -20f, 20f) * (grabbedCount > 0 ? 0.3f : 1f);
                torso.AddForce(new Vector3(ax, ay, 0f), ForceMode.Acceleration);
            }

            // Hafif dik durma torku (komik sallanmaya izin verir)
            float ang = Mathf.DeltaAngle(0, torso.rotation.eulerAngles.z);
            torso.AddTorque(Vector3.forward * (-ang * Mathf.Deg2Rad * uprightStrength - torso.angularVelocity.z * uprightDamping), ForceMode.Acceleration);

            if (autoLegs) AutoLegs(dt);

            // Düşüş → checkpoint
            if (torso.position.y < checkpoint.y - fallRespawnDistance)
                ServerRespawn("fall");

            WriteSync();
        }

        /// <summary>
        /// Kolay tırmanış: tutunan kollar gövdeyi kendiliğinden yukarı çeker (omuz elin biraz altına),
        /// sadece bacak tutunuyorsa gövde ayağın üstünde dikilir.
        /// </summary>
        void AutoPull(float dt)
        {
            float bestArmY = float.NegativeInfinity, bestLegY = float.NegativeInfinity;
            float sumX = 0f; int n = 0;
            for (int i = 0; i < 4; i++)
            {
                if (joints[i] == null) continue;
                Vector2 g = grabPoint[i];
                if (LimbUtil.IsArm(i)) bestArmY = Mathf.Max(bestArmY, g.y - armHang - anchorLocal[i].y);
                else bestLegY = Mathf.Max(bestLegY, g.y + reach[i] * 0.8f - anchorLocal[i].y);
                sumX += g.x - anchorLocal[i].x * 0.6f;
                n++;
            }
            float ty = bestArmY > float.NegativeInfinity ? Mathf.Max(bestArmY, bestLegY) : bestLegY;
            var goal = new Vector2(sumX / n, ty);
            bodyTarget = Vector2.MoveTowards(bodyTarget, goal, autoPullSpeed * dt);
        }

        /// <summary>
        /// Kolları da yöneten oyuncu (tek oyuncu / çapraz mod) için bacaklar basamakları kendisi bulur.
        /// Oyuncu bacağı elle oynatırsa 3 sn otomatik devre dışı kalır.
        /// </summary>
        void AutoLegs(float dt)
        {
            autoLegTimer -= dt;
            if (autoLegTimer > 0f) return;
            autoLegTimer = 0.25f;

            bool anyArm = joints[0] != null || joints[1] != null;
            for (int i = 2; i < 4; i++)
            {
                Vector2 hip = AnchorWorld(i);
                // Kollar gövdeyi çekerken fazla gerilen ayak (kim yönetirse yönetsin) kayadan kayar
                if (joints[i] != null && anyArm && (hip - (Vector2)grabPoint[i]).magnitude > reach[i] * 0.9f)
                {
                    wantGrab[i] = false;
                    ReleaseInternal(i);
                    continue;
                }

                var owner = FumblePlayer.OwnerOf(i);
                bool ownerHasArm = owner != null && (LimbUtil.Has(owner.limbMask, 0) || LimbUtil.Has(owner.limbMask, 1));
                if (!ownerHasArm || Time.time - manualTime[i] < 3f) continue;

                if (joints[i] != null)
                {
                    // Gövde ayağı geçtiyse ya da bacak fazla gerildiyse ayağı bırak → yeni basamak arar
                    float drop0 = hip.y - grabPoint[i].y;
                    float stretch = (hip - (Vector2)grabPoint[i]).magnitude;
                    if (drop0 < reach[i] * 0.3f || (anyArm && stretch > reach[i] * 0.78f))
                    {
                        wantGrab[i] = false;
                        ReleaseInternal(i);
                    }
                    continue;
                }
                if (!anyArm || wantGrab[i]) continue;

                float side = i == 2 ? -1f : 1f;
                int n = Physics.OverlapSphereNonAlloc(hip, reach[i] * 0.95f, overlap, gripMask, QueryTriggerInteraction.Collide);
                Grip best = null; float bestScore = float.MaxValue; Vector2 bestP = Vector2.zero;
                for (int k = 0; k < n; k++)
                {
                    var g = overlap[k].GetComponentInParent<Grip>();
                    if (g == null || g.type == GripType.Slippery) continue;
                    Vector2 gp = overlap[k].bounds.center;
                    float drop = hip.y - gp.y;
                    if (drop < reach[i] * 0.35f || (gp - hip).magnitude > reach[i] * 0.68f) continue;
                    bool usedByOther = false;
                    for (int o = 0; o < 4; o++) if (o != i && grips[o] == g) usedByOther = true;
                    if (usedByOther) continue;
                    float score = Mathf.Abs(gp.x - (hip.x + side * 0.15f)) + drop * 0.5f;
                    if (score < bestScore) { bestScore = score; best = g; bestP = gp; }
                }
                if (best == null) continue;
                targets[i] = bestP;
                wantGrab[i] = true;
                fistSince[i] = Time.time;
            }
        }

        /// <summary>Gövde hedefini her tutunan uzvun erişimi içinde tut.</summary>
        void ClampBodyTarget()
        {
            for (int it = 0; it < 3; it++)
            {
                bool armHold = joints[0] != null || joints[1] != null;
                for (int i = 0; i < 4; i++)
                {
                    if (joints[i] == null) continue;
                    if (armHold && !LimbUtil.IsArm(i)) continue; // kollar tutarken bacak hedefi kısıtlamaz (gerilirse bırakır)
                    Vector2 a = bodyTarget + (Vector2)anchorLocal[i];
                    Vector2 g = grabPoint[i];
                    Vector2 d = a - g;
                    float max = reach[i] * 0.95f;
                    if (d.sqrMagnitude > max * max)
                        bodyTarget = g + d.normalized * max - (Vector2)anchorLocal[i];
                    // Omuz, tutunan elin en fazla biraz üstüne çıkabilir (duvar tırmanışı pozu)
                    if (LimbUtil.IsArm(i))
                    {
                        float maxY = g.y + shoulderAboveHand - anchorLocal[i].y;
                        if (bodyTarget.y > maxY) bodyTarget.y = maxY;
                    }
                }
            }
        }

        bool StillOnGrip(Vector3 p, Grip g)
        {
            var c = g.GetComponent<Collider>();
            if (c == null) return false;
            var cp = c.ClosestPoint(p);
            return (cp - p).sqrMagnitude < gripRadius * gripRadius;
        }

        void TryGrab(int i)
        {
            var eff = effectors[i];
            int n = Physics.OverlapSphereNonAlloc(eff.position, gripRadius, overlap, gripMask, QueryTriggerInteraction.Collide);
            Collider best = null; float bestD = float.MaxValue; Vector3 bestP = Vector3.zero;
            for (int k = 0; k < n; k++)
            {
                var c = overlap[k];
                if (c.GetComponentInParent<Grip>() == null) continue;
                var p = c.ClosestPoint(eff.position);
                Vector3 tgt = new Vector3(targets[i].x, targets[i].y, 0f);
                float d = (c.ClosestPoint(tgt) - tgt).sqrMagnitude; // imlecin gösterdiği kaya öncelikli
                if (d < bestD) { bestD = d; best = c; bestP = p; }
            }
            if (best == null) return;

            bestP.z = 0;
            eff.position = bestP;
            eff.linearVelocity = Vector3.zero;
            var j = eff.gameObject.AddComponent<FixedJoint>();
            j.autoConfigureConnectedAnchor = false;
            j.anchor = Vector3.zero;
            j.connectedBody = null;
            j.connectedAnchor = bestP;
            joints[i] = j;
            var newGrip = best.GetComponentInParent<Grip>();
            if (newGrip != lastGrip[i]) stamina[i] = Mathf.Max(stamina[i], maxStamina * 0.75f);
            lastGrip[i] = newGrip;
            grips[i] = newGrip;
            grabPoint[i] = bestP;
            cursorAtGrab[i] = targets[i];
            lastTarget[i] = targets[i];
            bool firstGrab = true;
            for (int k = 0; k < 4; k++) if (k != i && joints[k] != null) firstGrab = false;
            if (firstGrab) bodyTarget = torso.position;
            anchorOffsetAtGrab[i] = (Vector3)AnchorWorld(i) - bestP;
            anchorOffsetAtGrab[i].z = 0;
        }

        void ReleaseInternal(int i)
        {
            if (joints[i] != null) Destroy(joints[i]);
            joints[i] = null;
            grips[i] = null;
        }

        void WriteSync()
        {
            sTorso = torso.position;
            sTorsoRot = torso.rotation.eulerAngles.z;
            sE0 = effectors[0].position; sE1 = effectors[1].position; sE2 = effectors[2].position; sE3 = effectors[3].position;
            sT0 = targets[0]; sT1 = targets[1]; sT2 = targets[2]; sT3 = targets[3];
            byte g = 0, f = 0;
            for (int i = 0; i < 4; i++)
            {
                if (joints[i] != null) g |= (byte)(1 << i);
                else if (wantGrab[i]) f |= (byte)(1 << i);
            }
            sGrab = g; sFist = f;
            sGround = (byte)lastGroundedFeet;
            sF0 = Fat(0); sF1 = Fat(1); sF2 = Fat(2); sF3 = Fat(3);
            if (torso.position.y > bestHeight) bestHeight = torso.position.y;
        }

        byte Fat(int i) => (byte)Mathf.RoundToInt((1f - stamina[i] / maxStamina) * 255f);

        // ---------- Görsel olaylar ----------
        public static event System.Action<string> OnRespawned;
        public static event System.Action OnCheckpointReached;
        public static event System.Action OnSummitReached;
        public static event System.Action<int> OnLimbGaveUp;

        [ClientRpc] void RpcRespawn(string reason) => OnRespawned?.Invoke(reason);
        [ClientRpc] void RpcCheckpoint() => OnCheckpointReached?.Invoke();
        [ClientRpc] void RpcSummit() => OnSummitReached?.Invoke();
        [ClientRpc] void RpcLimbGaveUp(byte limb) => OnLimbGaveUp?.Invoke(limb);
    }
}
