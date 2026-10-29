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
        public float shoulderAboveHand = 0.55f;
        [Header("Sürükle-bırak tırmanış")]
        [Tooltip("Gövdenin duruş hedefine gidiş hızı (m/sn)")]
        public float bodyMoveSpeed = 1.3f;
        [Tooltip("Tutunan kol: omuz elin bu oran x erişim kadar altında asılı durur")]
        public float armHangRatio = 0.3f;
        [Tooltip("Basan bacak: kalça ayağın bu oran x erişim kadar üstünde durur")]
        public float legStandRatio = 0.85f;
        [Tooltip("Ayaklar basarken kolların gövdeyi yukarı çekme payı (0-1)")]
        [Range(0, 1)] public float armPullShare = 0.6f;
        [Tooltip("Sürüklenen uzuv erişimini aşınca gövdeyi en fazla bu kadar çekebilir (m)")]
        public float maxDragPull = 0.12f;
        [Tooltip("Tutunan uzuv erişiminin bu katından fazla gerilirse...")]
        public float strainLimit = 1.08f;
        [Tooltip("...bu kadar saniye sonra kopar")]
        public float strainTime = 0.3f;
        [Tooltip("Tek oyuncuda tutunma süresi tüketim çarpanı (>1 = tek kişi daha zor)")]
        public float singlePlayerDrain = 1.15f;
        public float uprightStrength = 140f;
        public float uprightDamping = 14f;
        public float gripRadius = 0.3f;
        [Tooltip("El/ayak taşın yüzeyine en fazla bu kadar uzaktaysa tutunur (boşluğu tutmasın)")]
        public float grabTolerance = 0.09f;
        public LayerMask gripMask = ~0;
        [Tooltip("Ayakların basabildiği zemin (Grip/Climber hariç)")]
        public LayerMask groundMask = ~((1 << 8) | (1 << 9));
        public float standHeight = 0.85f;

        [Header("Dayanıklılık (GDD 2.2)")]
        [Tooltip("Bir kayada en az / en çok tutunma süresi (sn); her tutunuşta rastgele seçilir")]
        public float minHoldTime = 10f;
        public float maxHoldTime = 15f;
        [Tooltip("Serbest uzvun yorgunluk toparlama hızı (sn/sn)")]
        public float recoverPerSecond = 2f;
        [Tooltip("Hiçbir el tutunmuyorsa ayaklar bu kadar sn sonra kayar (düşüş)")]
        public float feetOnlyTime = 0.4f;
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
        [SyncVar] byte sDrag;
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
        readonly float[] holdTime = new float[4];   // bu kayada geçen (yükle ağırlıklı) süre
        readonly float[] holdLimit = new float[4];  // bu tutunuşun rastgele süre sınırı
        float feetOnlyTimer;
        float stuckTimer;
        readonly System.Collections.Generic.List<CrumblingHold> crumbling = new System.Collections.Generic.List<CrumblingHold>();   // tutunmadan, hareketsiz ve rotadan/kamptan uzakta geçen süre
        readonly float[] cooldown = new float[4];
        readonly float[] slipAt = new float[4];      // muz kabuğu: bu anda uzuv kayar
        readonly Collider[] overlap = new Collider[8];
        Vector3 checkpoint;
        int lastGroundedFeet;
        Vector2 bodyTarget;
        readonly Vector2[] lastTarget = new Vector2[4];
        readonly Grip[] lastGrip = new Grip[4];
        readonly float[] fistSince = new float[4];
        readonly bool[] dragging = new bool[4];
        readonly float[] strain = new float[4];
        readonly bool[] footOnGround = new bool[4];
        Vector3 placeAt;
        bool hasPlace;

        // ---------- Okuma (tüm istemciler) ----------
        public Vector2 TorsoPos => isServer ? (Vector2)torso.position : sTorso;
        public float TorsoRot => isServer ? torso.rotation.eulerAngles.z : sTorsoRot;
        public Vector2 EffectorPos(int i) => isServer ? (Vector2)effectors[i].position : i switch { 0 => sE0, 1 => sE1, 2 => sE2, _ => sE3 };
        public Vector2 Target(int i) => isServer ? targets[i] : i switch { 0 => sT0, 1 => sT1, 2 => sT2, _ => sT3 };
        public bool IsGrabbing(int i) => LimbUtil.Has(sGrab, i);
        public bool IsFist(int i) => LimbUtil.Has(sFist, i);
        /// <summary>Uzuv şu an fareyle sürükleniyor mu?</summary>
        public bool IsDragged(int i) => isServer ? dragging[i] : LimbUtil.Has(sDrag, i);
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
            IgnoreSelfCollisions();
            TeleportAll(start);
            AttachStartHolds();
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
                dragging[i] = false;
                strain[i] = 0f;
                holdTime[i] = 0f;
                lastGrip[i] = null;
            }
            bodyTarget = torsoPos;
            feetOnlyTimer = 0f;
            stuckTimer = 0f;
        }

        /// <summary>
        /// Başlangıç / kampta yeniden doğuş: eller ve ayaklar en yakın uygun kayalara tutunmuş gelir
        /// (eller omzun üstünde, ayaklar kalçanın altında). Yakında kaya yoksa o uzuv serbest kalır.
        /// </summary>
        void AttachStartHolds()
        {
            var used = new System.Collections.Generic.HashSet<Collider>();
            for (int i = 0; i < 4; i++)
            {
                bool arm = LimbUtil.IsArm(i);
                float side = (i % 2 == 0) ? -1f : 1f;
                Vector2 a = (Vector2)torso.position + (Vector2)anchorLocal[i];
                Vector2 ideal = a + (arm ? new Vector2(side * 0.15f, 0.42f) : new Vector2(side * 0.12f, -0.55f));
                int n = Physics.OverlapSphereNonAlloc(new Vector3(a.x, a.y, 0f), reach[i] * 0.92f, overlap, gripMask, QueryTriggerInteraction.Collide);
                Collider best = null; float bd = float.MaxValue;
                for (int k = 0; k < n; k++)
                {
                    var c = overlap[k];
                    var g = c.GetComponentInParent<Grip>();
                    if (g == null || g.type == GripType.Slippery || used.Contains(c) || g.GetComponent<PestNest>() != null || g.GetComponent<CrumblingHold>() != null) continue;
                    Vector2 gp = c.bounds.center;
                    if (arm ? gp.y < a.y - 0.05f : gp.y > a.y - 0.05f) continue;
                    float d = (gp - ideal).sqrMagnitude;
                    if (d < bd) { bd = d; best = c; }
                }
                if (best == null) continue;
                used.Add(best);
                Vector3 p = best.ClosestPoint(new Vector3(ideal.x, ideal.y, 0f)); p.z = 0;
                effectors[i].position = p;
                targets[i] = p;
                AttachJoint(i, best, p);
            }
        }

        // ---------- Oyuncu girdileri (FumblePlayer komutlarından) ----------
        [Server]
        public void ServerSetTarget(int limb, Vector2 p)
        {
            if (limb < 0 || limb > 3) return;
            if (!float.IsFinite(p.x) || !float.IsFinite(p.y)) return;
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
            }
        }

        [Server]
        public void ServerRelease(int limb)
        {
            if (limb < 0 || limb > 3) return;
            wantGrab[limb] = false;
            dragging[limb] = false;
            ReleaseInternal(limb);
        }

        /// <summary>Fareyle uzvu tuttu: kayadaysa bırakır, fareyi takip eder.</summary>
        [Server]
        public void ServerBeginDrag(int limb)
        {
            if (limb < 0 || limb > 3 || cooldown[limb] > 0f) return;
            ReleaseInternal(limb);
            wantGrab[limb] = false;
            dragging[limb] = true;
            strain[limb] = 0f;
            targets[limb] = effectors[limb].position;
        }

        /// <summary>Fare bırakıldı: uzvun altında kaya varsa tutunur, yoksa uzuv serbest (fizik) kalır.</summary>
        [Server]
        public void ServerEndDrag(int limb, Vector2 p)
        {
            if (limb < 0 || limb > 3) return;
            if (float.IsFinite(p.x) && float.IsFinite(p.y)) targets[limb] = p;
            dragging[limb] = false;
            wantGrab[limb] = false;
            TryGrab(limb);
            // el hâlâ fareye yetişmeye çalışıyorsa (çok küçük gecikme), fare taşın üstündeyse yine tut
            if (joints[limb] == null && cooldown[limb] <= 0f)
            {
                Vector3 t = new Vector3(targets[limb].x, targets[limb].y, 0f);
                bool inReach = ((Vector2)t - AnchorWorld(limb)).magnitude <= reach[limb];
                if (inReach && (effectors[limb].position - t).sqrMagnitude < 0.18f * 0.18f && FindGrip(t, out var c, out var cp))
                {
                    effectors[limb].position = cp;
                    AttachJoint(limb, c, cp);
                }
            }
        }

        /// <summary>Tutunma isteği: zaten tutuyorsa dokunma (toggle değil).</summary>
        [Server]
        public void ServerGrab(int limb)
        {
            if (limb < 0 || limb > 3 || cooldown[limb] > 0f) return;
            if (joints[limb] != null) ReleaseInternal(limb); // başka yere tutunmak için önce bırak
            wantGrab[limb] = true;
            fistSince[limb] = Time.time;
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
            AttachStartHolds();
            RpcRespawn(reason);
        }

        // ---------- Böcek / kuş etkileşimi (PestController) ----------
        [Server] public bool ServerIsHeld(int i) => joints[i] != null;
        [Server] public bool ServerIsDragged(int i) => dragging[i];
        [Server] public Vector3 ServerEffector(int i) => effectors[i].position;
        [Server] public Vector3 ServerEffectorVel(int i) => effectors[i].linearVelocity;
        [Server] public Vector3 ServerHead() => torso.position + torso.rotation * new Vector3(0f, 0.72f, -0.05f);
        /// <summary>Uzvu rahatsız et: tutunma süresi daha hızlı dolar.</summary>
        [Server] public void ServerAnnoy(int i, float seconds) { if (joints[i] != null) holdTime[i] = Mathf.Min(holdLimit[i], holdTime[i] + seconds); }
        /// <summary>Korkup bırak (örümcek ele çıktı, kuş gagaladı).</summary>
        [Server] public void ServerStartle(int i)
        {
            if (joints[i] == null) return;
            ReleaseInternal(i);
            cooldown[i] = 0.4f;
            RpcLimbSlipped((byte)i);
        }

        /// <summary>Kamp alanında ayakta duruluyorsa yorgunluk hızla geçer (tutunan uzuvlar dahil).</summary>
        [Server]
        public void ServerRest(float dt)
        {
            if (lastGroundedFeet == 0) return;
            for (int i = 0; i < 4; i++) holdTime[i] = Mathf.Max(0f, holdTime[i] - 4f * dt);
        }

        [Server]
        public void ServerSummit()
        {
            RpcSummit();
        }

        /// <summary>Yuva saldırısı: tüm uzuvlar bırakır, gövde savrulur; kısa süre hiçbir şey tutulamaz → düşüş.</summary>
        [Server]
        public void ServerKnockOff(Vector3 impulse)
        {
            for (int i = 0; i < 4; i++)
            {
                wantGrab[i] = false; dragging[i] = false; ReleaseInternal(i);
                cooldown[i] = 1.6f;
            }
            torso.AddForce(impulse, ForceMode.VelocityChange);
            torso.AddTorque(Vector3.forward * -Mathf.Sign(impulse.x) * 6f, ForceMode.VelocityChange);
            foreach (var e in effectors) e.AddForce(impulse * 1.2f + (Vector3)Random.insideUnitCircle * 1.5f, ForceMode.VelocityChange);
            RpcKnockedOff();
        }

        [Server]
        public void ServerBounce(Vector3 impulse)
        {
            for (int i = 0; i < 4; i++) { wantGrab[i] = false; dragging[i] = false; ReleaseInternal(i); }
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
            // Az dayanakla daha hızlı yorulur: 4 = 0.8x, 3 = 1x, 2 = 1.3x, 1 = 2x
            float load = supports >= 4 ? 0.8f : supports == 3 ? 1f : supports == 2 ? 1.3f : 2f;
            if (FumbleNetworkManager.IsSinglePlayer) load *= singlePlayerDrain;

            for (int i = 0; i < 4; i++)
            {
                var eff = effectors[i];
                Vector3 anchor = AnchorWorld(i);
                cooldown[i] -= dt;

                if (joints[i] != null)
                {
                    // Muz kabuğu: tutunur tutunmaz kayar, kabuk uçar
                    if (slipAt[i] > 0f && Time.time >= slipAt[i])
                    {
                        var pg = grips[i];
                        ReleaseInternal(i);
                        wantGrab[i] = false; dragging[i] = false;
                        cooldown[i] = 0.6f;
                        eff.AddForce(new Vector3(Random.Range(-1.2f, 1.2f), -1.8f, 0f), ForceMode.VelocityChange);
                        RpcBananaSlip((byte)i, pg != null ? pg.transform.position : eff.position);
                        continue;
                    }
                    // Kaygan yüzey: yavaşça aşağı kayar
                    var g = grips[i];
                    if (g != null && g.type == GripType.Slippery)
                    {
                        grabPoint[i] += Vector3.down * g.slideSpeed * dt;
                        joints[i].connectedAnchor = grabPoint[i];
                        if (!StillOnGrip(grabPoint[i], g)) { ReleaseInternal(i); continue; }
                    }

                    // Tutunma süresi: 10-15 sn (rastgele) dolunca el/ayak kızarır ve kendini bırakır
                    holdTime[i] += dt * load;
                    if (holdTime[i] >= holdLimit[i])
                    {
                        holdTime[i] = holdLimit[i];
                        wantGrab[i] = false;
                        dragging[i] = false;
                        ReleaseInternal(i);
                        cooldown[i] = forcedCooldown;
                        RpcLimbGaveUp((byte)i);
                    }
                }
                else
                {
                    Vector3 rel = eff.linearVelocity - torso.linearVelocity;
                    footOnGround[i] = false;
                    if (dragging[i] || wantGrab[i])
                    {
                        // Sürüklenen uzuv fareye uzanır — en fazla kol/bacak boyu kadar
                        Vector3 tgt = targets[i];
                        Vector3 desired = anchor + Vector3.ClampMagnitude(tgt - anchor, reach[i]);
                        desired.z = 0;
                        Vector3 f = (desired - eff.position) * limbSpring - rel * limbDamping;
                        eff.AddForce(f, ForceMode.Acceleration);
                    }
                    else
                    {
                        // Serbest uzuv: fizik + hafif sönüm (sallanır, sarkar)
                        eff.AddForce(-rel * 1.5f, ForceMode.Acceleration);
                    }
                    lastTarget[i] = targets[i];

                    if (!LimbUtil.IsArm(i) && !dragging[i] && Physics.CheckSphere(eff.position, 0.2f, groundMask, QueryTriggerInteraction.Ignore))
                    {
                        groundedFeet++;
                        footSum += eff.position;
                        footOnGround[i] = true;
                    }

                    if (wantGrab[i] && cooldown[i] <= 0f)
                    {
                        bool arrived = ((Vector2)eff.position - targets[i]).sqrMagnitude < 0.25f * 0.25f;
                        bool settled = rel.sqrMagnitude < 0.5f * 0.5f;
                        if (arrived || settled) TryGrab(i);
                        if (joints[i] == null && Time.time - fistSince[i] > 2.5f && !arrived) wantGrab[i] = false; // boşluğa yumruk
                    }
                    holdTime[i] = Mathf.Max(0f, holdTime[i] - recoverPerSecond * dt);
                }
            }

            lastGroundedFeet = groundedFeet;

            // Çürük taşlar: süresi dolan parçalanır, üstündeki uzuvlar boşluğa düşer
            for (int c = crumbling.Count - 1; c >= 0; c--)
            {
                var ch = crumbling[c];
                if (ch == null) { crumbling.RemoveAt(c); continue; }
                if (!ch.ServerShouldBreak) continue;
                var g = ch.GetComponent<Grip>();
                bool someone = false;
                for (int i = 0; i < 4; i++)
                    if (joints[i] != null && grips[i] == g)
                    {
                        ReleaseInternal(i);
                        wantGrab[i] = false;
                        cooldown[i] = 0.5f;
                        someone = true;
                    }
                RpcCrumble(ch.transform.position, true);
                ch.Break(); // sunucuda hemen (host'ta RPC ikinci kez çağırırsa etkisiz)
                crumbling.RemoveAt(c);
                if (someone) RpcHoldCrumbled();
            }

            // Hiçbir el tutunmuyor ve yere basılmıyorsa ayaklar tek başına taşıyamaz → kayar, düşersin
            bool armHeld = joints[0] != null || joints[1] != null;
            bool legHeld = joints[2] != null || joints[3] != null;
            if (!armHeld && legHeld && groundedFeet == 0)
            {
                feetOnlyTimer += dt;
                if (feetOnlyTimer > feetOnlyTime)
                {
                    for (int i = 2; i < 4; i++)
                        if (joints[i] != null) { ReleaseInternal(i); RpcLimbSlipped((byte)i); }
                    feetOnlyTimer = 0f;
                }
            }
            else feetOnlyTimer = 0f;

            // Tutunurken gövdeyi hedefe taşı (tüm tutunan uzuvlar aynı hedefi paylaşır → birbirine karşı çekmez)
            int nowGrabbed = 0;
            for (int i = 0; i < 4; i++) if (joints[i] != null) { nowGrabbed++; lastTarget[i] = targets[i]; }
            if (nowGrabbed > 0)
            {
                Posture(dt);
                ClampBodyTarget();
                Vector2 err = bodyTarget - (Vector2)torso.position;
                Vector2 v = torso.linearVelocity;
                Vector2 a = err * pullStrength - v * pullDampingRatio + Vector2.up * 9.81f * gravityCompensation;
                a = Vector2.ClampMagnitude(a, maxPullAccel);
                torso.AddForce(a, ForceMode.Acceleration);
            }

            // Ayakta durma desteği: hiçbir yere tutunmuyorken zemine basan ayakların üstünde dur
            if (groundedFeet > 0 && nowGrabbed == 0)
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

            // Düşüş → checkpoint
            if (torso.position.y < checkpoint.y - fallRespawnDistance)
                ServerRespawn("fall");
            else
            {
                // Takıldı mı? Hiçbir yere tutunmuyor, duruyor (yerde / bir çıkıntıda) ve son kamptan
                // aşağıda ya da yana uzakta → oradan bir yere gidilemez, son kampa (ya da başlangıca) dön.
                Vector3 tp = torso.position;
                bool idle = nowGrabbed == 0 && torso.linearVelocity.sqrMagnitude < 0.6f * 0.6f;
                bool away = checkpoint.y - tp.y > 1.2f || Mathf.Abs(tp.x - checkpoint.x) > 2f;
                if (idle && away && tp.y < Mountain.SummitY - 1f)
                {
                    stuckTimer += dt;
                    if (stuckTimer > 1.2f) ServerRespawn("fall");
                }
                else stuckTimer = 0f;
            }

            WriteSync();
        }

        /// <summary>
        /// Gövde duruşu: tutunan kollar gövdeyi elin altında asılı tutar, basan bacaklar kalçayı
        /// ayağın üstüne iter → ayak yukarı konunca gövde yükselir (kol + bacak birlikte tırmanış).
        /// Sürüklenen uzuv erişimini aşarsa gövdeyi o yöne çeker; bu yüzden diğer tutunan uzuvlar
        /// fazla gerilirse kopar (dikkatli olmak lazım).
        /// </summary>
        void Posture(float dt)
        {
            // X: tüm dayanakların ortası. Y: bacaklar iter (kalça ayağın üstünde), kollar çeker.
            float sumX = 0f, wx = 0f;
            float legY = 0f; int legN = 0;
            float armPullY = float.NegativeInfinity, armHangY = float.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                bool held = joints[i] != null;
                bool standing = !held && footOnGround[i];
                if (!held && !standing) continue;
                Vector2 p = held ? (Vector2)grabPoint[i] : (Vector2)effectors[i].position;
                sumX += p.x - anchorLocal[i].x; wx += 1f;
                if (LimbUtil.IsArm(i))
                {
                    armPullY = Mathf.Max(armPullY, p.y - reach[i] * armHangRatio - anchorLocal[i].y);
                    armHangY = Mathf.Max(armHangY, p.y - reach[i] * 0.72f - anchorLocal[i].y);
                }
                else { legY += p.y + reach[i] * legStandRatio - anchorLocal[i].y; legN++; }
            }
            if (wx <= 0f) return;
            // bacaklar gövdeyi YUKARI itmez: ayak yukarı alınınca gövde yerinde kalır
            float y;
            bool armHold = !float.IsNegativeInfinity(armPullY);
            if (armHold && legN > 0) y = armPullY;     // kollar çeker; bacaklar sadece sınırlar (ClampBodyTarget)
            else if (armHold) y = armHangY;            // ayak desteği yok: kollardan sarkar, yukarı çıkamaz
            else y = legN > 0 ? legY / legN : torso.position.y; // (sadece ayak: kısa süre sonra kayar)
            Vector2 goal = new Vector2(sumX / wx, y);

            // Sürüklenen uzuv fazla uzağa çekiliyorsa gövdeyi de sürükle
            for (int i = 0; i < 4; i++)
            {
                if (!dragging[i]) continue;
                Vector2 a = goal + (Vector2)anchorLocal[i];
                Vector2 off = targets[i] - a;
                float excess = off.magnitude - reach[i];
                if (excess > 0f) goal += off.normalized * Mathf.Min(excess, maxDragPull);
            }

            bodyTarget = Vector2.MoveTowards(bodyTarget, goal, bodyMoveSpeed * dt);

            // Gerilme: tutunan uzuv erişiminin çok dışında kalırsa kopar
            int held2 = 0;
            for (int i = 0; i < 4; i++) if (joints[i] != null) held2++;
            for (int i = 0; i < 4; i++)
            {
                if (joints[i] == null) { strain[i] = 0f; continue; }
                float d = ((bodyTarget + (Vector2)anchorLocal[i]) - (Vector2)grabPoint[i]).magnitude;
                if (d > reach[i] * strainLimit && held2 > 1)
                {
                    strain[i] += dt;
                    if (strain[i] > strainTime)
                    {
                        strain[i] = 0f;
                        ReleaseInternal(i);
                        held2--;
                        RpcLimbSlipped((byte)i);
                    }
                }
                else strain[i] = Mathf.Max(0f, strain[i] - dt);
            }
        }

        /// <summary>Gövde hedefini her tutunan uzvun erişimi içinde tut.</summary>
        void ClampBodyTarget()
        {
            for (int it = 0; it < 3; it++)
            {
                for (int i = 0; i < 4; i++)
                {
                    if (joints[i] == null) continue;
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

        /// <summary>
        /// Bir noktada tutunulabilir taş var mı? Taşın yüzeyine grabTolerance'tan yakın olmalı.
        /// (Sunucu tutunurken, istemci de vurgularken aynı kuralı kullanır.)
        /// </summary>
        public bool FindGrip(Vector3 p, out Collider best, out Vector3 point)
        {
            p.z = 0f;
            best = null; point = Vector3.zero;
            int n = Physics.OverlapSphereNonAlloc(p, grabTolerance + 0.02f, overlap, gripMask, QueryTriggerInteraction.Collide);
            float bd = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                var c = overlap[k];
                if (c.GetComponentInParent<Grip>() == null) continue;
                var cp = c.ClosestPoint(p);
                float d = (cp - p).sqrMagnitude;
                if (d > grabTolerance * grabTolerance || d >= bd) continue;
                bd = d; best = c; point = cp;
            }
            return best != null;
        }

        void TryGrab(int i)
        {
            var eff = effectors[i];
            if (cooldown[i] > 0f) return;
            if (!FindGrip(eff.position, out var best, out var bestP)) return; // boşluk: uzuv serbest kalır, fizikle düşer
            bestP.z = 0;
            eff.position = bestP;
            AttachJoint(i, best, bestP);
        }

        void AttachJoint(int i, Collider best, Vector3 bestP)
        {
            var eff = effectors[i];
            eff.linearVelocity = Vector3.zero;
            var j = eff.gameObject.AddComponent<FixedJoint>();
            j.autoConfigureConnectedAnchor = false;
            j.anchor = Vector3.zero;
            j.connectedBody = null;
            j.connectedAnchor = bestP;
            joints[i] = j;
            var newGrip = best.GetComponentInParent<Grip>();
            // Yeni kaya = taze tutunuş (yeni rastgele süre). Aynı kayayı bırakıp tutmak süreyi sıfırlamaz.
            if (newGrip != lastGrip[i]) { holdTime[i] *= 0.3f; holdLimit[i] = Random.Range(minHoldTime, maxHoldTime); }
            else if (holdLimit[i] <= 0f) holdLimit[i] = Random.Range(minHoldTime, maxHoldTime);
            lastGrip[i] = newGrip;
            grips[i] = newGrip;
            // Canlı yuvası: tutan uzuv yuvayı bozar → sakinleri fırlar, tırmanıcıyı düşürür
            var nest = newGrip != null ? newGrip.GetComponent<PestNest>() : null;
            if (nest != null) GetComponent<PestController>()?.ServerNestBurst(nest, i);
            // Muz kabuğu olan taş: el / ayak hemen kayar
            var peel = newGrip != null ? newGrip.GetComponent<BananaPeel>() : null;
            slipAt[i] = peel != null && peel.Landed ? Time.time + 0.2f : 0f;
            // Çürük taş: tutulunca titrer, kısa süre sonra parçalanır
            var crumb = newGrip != null ? newGrip.GetComponent<CrumblingHold>() : null;
            if (crumb != null && crumb.ServerTouch())
            {
                crumbling.Add(crumb);
                RpcCrumble(crumb.transform.position, false);
            }
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
            slipAt[i] = 0f;
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
            byte dr = 0;
            for (int i = 0; i < 4; i++) if (dragging[i]) dr |= (byte)(1 << i);
            sDrag = dr;
            sGround = (byte)lastGroundedFeet;
            sF0 = Fat(0); sF1 = Fat(1); sF2 = Fat(2); sF3 = Fat(3);
            if (torso.position.y > bestHeight) bestHeight = torso.position.y;
        }

        byte Fat(int i) => (byte)Mathf.RoundToInt(Mathf.Clamp01(holdLimit[i] > 0f ? holdTime[i] / holdLimit[i] : 0f) * 255f);

        // ---------- Görsel olaylar ----------
        public static event System.Action<string> OnRespawned;
        public static event System.Action OnCheckpointReached;
        public static event System.Action OnSummitReached;
        public static event System.Action<int> OnLimbGaveUp;
        public static event System.Action<int> OnLimbSlipped;
        public static event System.Action OnKnockedOff;
        public static event System.Action OnHoldCrumbled;
        public static event System.Action<int> OnBananaSlip;

        [ClientRpc] void RpcRespawn(string reason) => OnRespawned?.Invoke(reason);
        [ClientRpc] void RpcCheckpoint() => OnCheckpointReached?.Invoke();
        [ClientRpc] void RpcSummit() => OnSummitReached?.Invoke();
        [ClientRpc] void RpcLimbGaveUp(byte limb) => OnLimbGaveUp?.Invoke(limb);
        [ClientRpc] void RpcLimbSlipped(byte limb) => OnLimbSlipped?.Invoke(limb);
        [ClientRpc] void RpcKnockedOff() => OnKnockedOff?.Invoke();
        [ClientRpc] void RpcHoldCrumbled() => OnHoldCrumbled?.Invoke();

        [ClientRpc]
        void RpcBananaSlip(byte limb, Vector3 holdPos)
        {
            BananaPeel.Nearest(holdPos)?.Slip();
            OnBananaSlip?.Invoke(limb);
        }

        [ClientRpc]
        void RpcCrumble(Vector3 pos, bool breakNow)
        {
            var ch = CrumblingHold.Nearest(pos);
            if (ch == null) return;
            if (breakNow) ch.Break(); else ch.PlayShake();
        }
    }
}
