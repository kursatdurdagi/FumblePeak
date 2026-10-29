using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace FumblePeak
{
    /// <summary>
    /// Tırmanırken ara ara gelip rahatsız eden canlılar (sunucu yönetir, herkes görür):
    ///  • Arılar: bir uzvun etrafında vızıldar → o uzuv daha hızlı yorulur, kovulmazsa sokar (uzuv bırakır).
    ///  • Sinek: kafanın/uzvun etrafında döner, yorar, bir süre sonra kendi gider.
    ///  • Kuş: kafaya konar, gagalar → bir el kayadan kayar.
    ///  • Örümcek: ipten iner, ele tırmanır → el korkup bırakır.
    ///  • Dağ keçisi: yukarıdan taştan taşa zıplayarak iner, kafanın üstüne dikilip inatlaşır; iki kez
    ///    kovulmazsa (uzuvla vur / salla) 5.5 sn sonra tosla → tırmanıcı düşer.
    ///  • Maymun: yandan gelir, yakındaki taşlara muz kabuğu atar; o taşa tutunan / basan uzuv kayar.
    /// Kovmak için: bir uzvu tutup hızlıca canlının üstünden geçir (el sallama).
    /// Yuvalar (PestNest): yuvayı tutan uzuv onu bozar → kızgın kuş / arı sürüsü / yavru örümcekler
    /// fırlar ve tırmanıcıyı düşürür.
    /// </summary>
    [RequireComponent(typeof(ClimberBody))]
    public class PestController : NetworkBehaviour
    {
        public enum Kind : byte { None = 0, Bees = 1, Fly = 2, Bird = 3, Spider = 4, Goat = 5, Monkey = 6 }
        public enum PestEvent : byte { Arrive = 0, Shoo = 1, Strike = 2, Nest = 3, NestHit = 4, Stubborn = 5 }

        [Header("Görsel")]
        public Material bodyMaterial;   // köşe renkli lit malzeme (dünya)
        public Material wingMaterial;   // şeffaf (kanat, ağ ipi)

        [Header("Zamanlama")]
        public float firstDelay = 35f;
        public Vector2 interval = new Vector2(35f, 55f);

        /// <summary>HUD için: (tür, olay, hedef uzuv; -1 = kafa)</summary>
        public static event System.Action<Kind, PestEvent, int> Event;

        [SyncVar] Kind kind;
        [SyncVar] byte phase;           // 0 yaklaş, 1 rahatsız et, 2 kaç, 3 yuva saldırısı
        [SyncVar] Vector3 sPos;
        [SyncVar] sbyte target;         // uzuv, -1 = kafa
        [SyncVar] Vector3 sThreadTop;   // örümcek ipinin tepesi
        [SyncVar] bool fromNest;        // yuvadan çıkan sürü (kaçarken de sürü görünür)
        [SyncVar] Vector3 sNest;        // bozulan yuvanın konumu
        // Keçi zıplaması (istemciler kavisi kendileri hesaplar)
        [SyncVar] Vector3 sHopFrom, sHopTo;
        [SyncVar] double sHopT0;
        [SyncVar] float sHopDur;
        [SyncVar] sbyte monkeySide;

        ClimberBody body;
        float nextSpawn, phaseTime, annoyTotal;
        Vector3 vel;
        bool knocked;
        int goatHits, peelsLeft, peelsThrown;
        readonly Collider[] buf = new Collider[24];

        void Awake() => body = GetComponent<ClimberBody>();

        public override void OnStartServer() => nextSpawn = Time.time + firstDelay;

        // =====================================================================
        // SUNUCU
        void FixedUpdate()
        {
            if (!isServer) return;
            float dt = Time.fixedDeltaTime;
            if (kind == Kind.None)
            {
                if (Time.time >= nextSpawn) Spawn();
                return;
            }
            phaseTime += dt;
            if (kind == Kind.Goat) { GoatUpdate(); return; }
            if (kind == Kind.Monkey) { MonkeyUpdate(dt); return; }
            Vector3 tp = TargetPos();

            if (phase == 0) // yaklaş
            {
                Vector3 goal = tp + HoverOffset(Time.time);
                float speed = kind == Kind.Spider ? 0.7f : kind == Kind.Bird ? 3.2f : 2.4f;
                sPos = Vector3.MoveTowards(sPos, goal, speed * dt);
                if ((sPos - goal).sqrMagnitude < 0.03f || phaseTime > 6f) { phase = 1; phaseTime = 0f; RpcEvent(kind, PestEvent.Arrive, target); }
            }
            else if (phase == 1) // rahatsız et
            {
                sPos = Vector3.Lerp(sPos, tp + HoverOffset(Time.time), 1f - Mathf.Exp(-8f * dt));
                annoyTotal += dt;
                switch (kind)
                {
                    case Kind.Bees:
                        if (target >= 0) body.ServerAnnoy(target, dt * 1.3f);
                        if (phaseTime > 6f) { Strike(target); }
                        break;
                    case Kind.Fly:
                        for (int i = 0; i < 4; i++) body.ServerAnnoy(i, dt * 0.25f);
                        if (phaseTime > 8f) Flee();
                        break;
                    case Kind.Bird:
                        if (phaseTime > 5f)
                        {
                            int arm = body.ServerIsHeld(0) && body.ServerIsHeld(1) ? Random.Range(0, 2) : body.ServerIsHeld(0) ? 0 : 1;
                            Strike(arm);
                        }
                        break;
                    case Kind.Spider:
                        if (phaseTime > 4.5f) Strike(target);
                        break;
                }
                CheckShoo();
            }
            else if (phase == 3) // yuva saldırısı: fırla → tırmanıcıya üşüş → düşür → kovala
            {
                float wait = kind == Kind.Spider ? 0.15f : 0.25f;
                if (phaseTime < wait) sPos = sNest;
                else
                {
                    Vector3 goal = kind == Kind.Bird ? (Vector3)body.ServerHead() + new Vector3(0f, 0.12f, -0.05f) : tp;
                    float speed = kind == Kind.Bird ? 7f : kind == Kind.Spider ? 3.5f : 5f;
                    sPos = Vector3.MoveTowards(sPos, goal, speed * dt);
                }
                float hitAt = kind == Kind.Bird ? 0.85f : 1.05f;
                if (!knocked && phaseTime > hitAt)
                {
                    knocked = true;
                    float away = Mathf.Sign(body.TorsoPos.x - sNest.x + 0.001f);
                    body.ServerKnockOff(new Vector3(away * 2.2f, 1.3f, 0f));
                    RpcEvent(kind, PestEvent.NestHit, target);
                    target = -1; // artık gövdeyi kovalar
                }
                if (phaseTime > 2.8f) Flee();
            }
            else // kaç
            {
                Vector3 away = kind == Kind.Spider && !fromNest ? Vector3.up : (sPos - (Vector3)body.TorsoPos).normalized + Vector3.up * 0.8f;
                away.z = Mathf.Min(away.z, -0.2f);
                vel = Vector3.Lerp(vel, away.normalized * (kind == Kind.Spider ? 2.5f : 5f), 1f - Mathf.Exp(-5f * dt));
                sPos += vel * dt;
                if (phaseTime > 1.6f) Despawn();
            }
        }

        void Spawn()
        {
            kind = NextForAltitude(body.TorsoPos.y);
            fromNest = false;
            knocked = false;
            phase = 0; phaseTime = 0f;
            if (kind == Kind.Goat)
            {
                // yukarıdan, bir yandan: taştan taşa sekerek iner
                target = -1; goatHits = 0;
                Vector3 start = LandingNear((Vector3)body.TorsoPos + new Vector3(Random.value < 0.5f ? -1.3f : 1.3f, 3.2f, 0f));
                sHopFrom = sHopTo = start; sHopT0 = NetworkTime.time; sHopDur = 0.05f; sPos = start;
                return;
            }
            if (kind == Kind.Monkey)
            {
                target = -1;
                monkeySide = (sbyte)(Random.value < 0.5f ? -1 : 1);
                sPos = (Vector3)body.TorsoPos + new Vector3(monkeySide * 4.2f, 0.6f, -0.15f);
                peelsLeft = body.TorsoPos.y > 100f ? 2 : 1; peelsThrown = 0;
                return;
            }
            var held = new List<int>();
            for (int i = 0; i < 4; i++) if (body.ServerIsHeld(i)) held.Add(i);
            if (kind == Kind.Spider)
            {
                var arms = held.FindAll(i => i < 2);
                if (arms.Count == 0) { kind = Kind.Fly; }
                else target = (sbyte)arms[Random.Range(0, arms.Count)];
            }
            if (kind == Kind.Bird) target = -1;
            if (kind == Kind.Bees || kind == Kind.Fly)
                target = held.Count > 0 && Random.value < 0.75f ? (sbyte)held[Random.Range(0, held.Count)] : (sbyte)-1;

            Vector3 tp = TargetPos();
            if (kind == Kind.Spider)
            {
                sThreadTop = tp + new Vector3(Random.Range(-0.15f, 0.15f), 2.6f, 0.15f);
                sPos = sThreadTop;
            }
            else
            {
                float side = Random.value < 0.5f ? -1f : 1f;
                sPos = tp + new Vector3(side * Random.Range(3f, 4.5f), Random.Range(0.5f, 2f), -Random.Range(0.3f, 1f));
            }
            phase = 0; phaseTime = 0f; annoyTotal = 0f; vel = Vector3.zero;
        }

        /// <summary>
        /// Bir uzuv yuvaya tutundu (ClimberBody.AttachJoint): yuvanın sakinleri fırlar, kısa süre sonra
        /// tırmanıcıyı düşürür. O an gezen bir canlı varsa yerini sürü alır.
        /// </summary>
        [Server]
        public void ServerNestBurst(PestNest nest, int limb)
        {
            if (nest == null || !nest.ServerTryTrigger()) return;
            kind = nest.kind;
            fromNest = true;
            sNest = nest.transform.position + nest.transform.rotation * nest.residentOffset;
            sPos = sNest;
            target = kind == Kind.Bird ? (sbyte)-1 : (sbyte)limb;
            phase = 3; phaseTime = 0f; vel = Vector3.zero; knocked = false;
            RpcNestBurst(nest.transform.position, kind, (sbyte)limb);
            // yuvadan sonra bir süre başka canlı gelmesin
            nextSpawn = Mathf.Max(nextSpawn, Time.time + interval.x);
        }

        // ---- Rakıma göre sıra: her bölgenin kendi canlıları sırayla gelir, bazı bölgelerde hepsi ----
        static readonly Kind[] KFly = { Kind.Fly };
        static readonly Kind[] KFlyBees = { Kind.Fly, Kind.Bees };
        static readonly Kind[] KBeesFly = { Kind.Bees, Kind.Fly };
        static readonly Kind[] KBeesBird = { Kind.Bees, Kind.Bird };
        static readonly Kind[] KBirdFly = { Kind.Bird, Kind.Fly };
        static readonly Kind[] KBirdSpider = { Kind.Bird, Kind.Spider };
        static readonly Kind[] KSpiderBird = { Kind.Spider, Kind.Bird };
        static readonly Kind[] KSpider = { Kind.Spider };
        static readonly Kind[] KAll = { Kind.Bees, Kind.Bird, Kind.Goat, Kind.Fly, Kind.Monkey, Kind.Spider };
        static readonly Kind[] KBeesMonkeyBird = { Kind.Bees, Kind.Monkey, Kind.Bird };
        static readonly Kind[] KBirdMonkeyGoat = { Kind.Bird, Kind.Monkey, Kind.Goat };
        static readonly Kind[] KGoatSpiderMonkey = { Kind.Goat, Kind.Spider, Kind.Monkey };
        static readonly Kind[] KGoatSpiderBird = { Kind.Goat, Kind.Spider, Kind.Bird };
        static readonly Kind[] KSpiderGoat = { Kind.Spider, Kind.Goat };
        static readonly Kind[] KGoatBirdSpider = { Kind.Goat, Kind.Bird, Kind.Spider };
        // [üst sınır (m), canlılar] — kamplar her 12 m'de; her bölgenin son kamp aralığında hepsi gelebilir.
        // Maymun ormanda ve kızıl kayalarda, keçi kızıl kayalardan itibaren.
        static readonly (float top, Kind[] kinds)[] Zones =
        {
            (6f, KFly),             // ısınma
            (12f, KFlyBees),        // vadi
            (36f, KBeesFly),        // vadi üstü çayırlar
            (60f, KBeesMonkeyBird), // yosunlu orman sırtı: maymunlar
            (72f, KAll),            // orman sırtının tepesi: hepsi
            (96f, KBirdMonkeyGoat), // kızıl kayalıklar: keçiler başlar
            (108f, KGoatSpiderMonkey),
            (120f, KAll),           // kızıl kayaların tepesi: hepsi
            (144f, KGoatSpiderBird),// rüzgârlı sırtlar
            (156f, KAll),
            (180f, KSpiderGoat),    // buzul: çatlaklarda örümcek, kayalıkta keçi
            (192f, KGoatBirdSpider),
            (999f, KAll),           // zirve öncesi son kapışma: hepsi
        };
        int seqIndex;

        Kind NextForAltitude(float y)
        {
            Kind[] zone = KAll;
            foreach (var z in Zones) if (y < z.top) { zone = z.kinds; break; }
            var k = zone[seqIndex % zone.Length];
            seqIndex++;
            return k;
        }

        void Strike(int limb)
        {
            if (limb >= 0) body.ServerStartle(limb);
            RpcEvent(kind, PestEvent.Strike, (sbyte)limb);
            Flee();
        }

        void Flee() { phase = 2; phaseTime = 0f; vel = Vector3.zero; }

        void Despawn()
        {
            kind = Kind.None;
            fromNest = false;
            nextSpawn = Mathf.Max(nextSpawn, Time.time + Random.Range(interval.x, interval.y) * (FumbleNetworkManager.IsSinglePlayer ? 1.1f : 0.85f));
        }

        /// <summary>Bir uzuv hızla canlının üstünden geçerse (el sallama) kaçar.</summary>
        void CheckShoo()
        {
            int i = ShooLimb(0.5f);
            if (i < 0) return;
            RpcEvent(kind, PestEvent.Shoo, (sbyte)i);
            Flee();
        }

        /// <summary>Canlıya sürüklenerek hızla değen (vuran / sallanan) uzuv; yoksa -1.</summary>
        int ShooLimb(float radius)
        {
            for (int i = 0; i < 4; i++)
            {
                if (!body.ServerIsDragged(i)) continue;
                Vector3 e = body.ServerEffector(i);
                float d = Vector2.Distance(e, sPos);
                float v = ((Vector2)body.ServerEffectorVel(i)).magnitude;
                if (d < radius && v > 0.7f) return i;
            }
            return -1;
        }

        // =====================================================================
        // DAĞ KEÇİSİ (sunucu)
        Vector3 HopPos(double now)
        {
            float k = Mathf.Clamp01((float)((now - sHopT0) / Mathf.Max(0.01f, sHopDur)));
            float h = 0.16f + 0.22f * Vector3.Distance(sHopFrom, sHopTo);
            return Vector3.Lerp(sHopFrom, sHopTo, k) + Vector3.up * (4f * k * (1f - k) * h);
        }

        bool HopDone => NetworkTime.time >= sHopT0 + sHopDur;

        void HopTo(Vector3 to, float dur)
        {
            sHopFrom = HopPos(NetworkTime.time);
            sHopTo = to; sHopT0 = NetworkTime.time; sHopDur = dur;
        }

        /// <summary>İstenen noktaya en yakın taşın üstü (tırmanıcının tuttuğu taşlar hariç); yoksa noktanın kendisi.</summary>
        Vector3 LandingNear(Vector3 desired)
        {
            desired.z = 0f;
            int n = Physics.OverlapSphereNonAlloc(desired, 0.5f, buf, body.gripMask, QueryTriggerInteraction.Collide);
            Collider best = null; float bd = float.MaxValue;
            for (int k = 0; k < n; k++)
            {
                var g = buf[k].GetComponentInParent<Grip>();
                if (g == null || g.GetComponent<PestNest>() != null) continue;
                Vector3 c = buf[k].bounds.center;
                bool held = false;
                for (int i = 0; i < 4; i++) if (((Vector2)body.ServerEffector(i) - (Vector2)c).sqrMagnitude < 0.25f * 0.25f) held = true;
                if (held) continue;
                float d = ((Vector2)c - (Vector2)desired).sqrMagnitude;
                if (d < bd) { bd = d; best = buf[k]; }
            }
            if (best == null) return desired + new Vector3(0f, 0f, -0.12f);
            var bc = best.bounds;
            return new Vector3(bc.center.x, bc.center.y + bc.extents.y * 0.8f, -0.12f);
        }

        Vector3 GoatGoal() => body.ServerHead() + new Vector3(0f, 0.38f, -0.12f);

        void HopAway()
        {
            float sx = sPos.x >= body.TorsoPos.x ? 1f : -1f;
            HopTo(sPos + new Vector3(sx * 0.9f, 0.85f, 0f), 0.36f);
        }

        void GoatUpdate()
        {
            sPos = HopPos(NetworkTime.time);
            bool done = HopDone;
            switch (phase)
            {
                case 0: // inmek: taştan taşa sekerek kafanın üstüne
                {
                    if (!done) break;
                    Vector3 goal = GoatGoal(), d = goal - sPos;
                    if (d.magnitude < 0.35f || phaseTime > 9f)
                    {
                        phase = 1; phaseTime = 0f;
                        RpcEvent(Kind.Goat, PestEvent.Arrive, -1);
                    }
                    else HopTo(d.magnitude < 0.8f ? goal : LandingNear(sPos + Vector3.ClampMagnitude(d, 0.75f)), 0.42f);
                    break;
                }
                case 1: // inatlaşmak: kafanın üstünde dikilir, toslamaya hazırlanır
                {
                    if (done && (GoatGoal() - sPos).magnitude > 0.5f) HopTo(GoatGoal(), 0.3f);
                    int limb = ShooLimb(0.55f);
                    if (limb >= 0 && phaseTime > 0.3f)
                    {
                        goatHits++;
                        if (goatHits >= 2) { RpcEvent(Kind.Goat, PestEvent.Shoo, (sbyte)limb); Flee(); HopAway(); }
                        else
                        {
                            // inatçı: biraz geri seker, sonra geri gelir (sabır süresi yenilenir)
                            RpcEvent(Kind.Goat, PestEvent.Stubborn, (sbyte)limb);
                            phaseTime = -1.2f;
                            float sx = sPos.x >= body.TorsoPos.x ? 1f : -1f;
                            HopTo(LandingNear(sPos + new Vector3(sx * 0.5f, 0.6f, 0f)), 0.4f);
                        }
                        break;
                    }
                    if (phaseTime > 5.5f)
                    {
                        phase = 4; phaseTime = 0f;
                        HopTo((Vector3)body.TorsoPos + new Vector3(0f, 0.2f, -0.15f), 0.25f);
                    }
                    break;
                }
                case 4: // tos!
                    if (done && !knocked)
                    {
                        knocked = true;
                        float dir = Mathf.Sign(body.TorsoPos.x - sHopFrom.x + 0.001f);
                        body.ServerKnockOff(new Vector3(dir * 2.4f, 1.5f, 0f));
                        RpcEvent(Kind.Goat, PestEvent.Strike, -1);
                    }
                    if (phaseTime > 0.9f) { Flee(); HopAway(); }
                    break;
                default: // kaç: yukarı doğru seker
                    if (done) HopAway();
                    if (phaseTime > 2.2f) Despawn();
                    break;
            }
        }

        // =====================================================================
        // MAYMUN (sunucu)
        void MonkeyUpdate(float dt)
        {
            Vector3 perch = (Vector3)body.TorsoPos + new Vector3(monkeySide * 1.25f, 0.55f, -0.15f);
            switch (phase)
            {
                case 0: // yandan koşarak gelir
                    sPos = Vector3.MoveTowards(sPos, perch, 2.8f * dt);
                    if ((sPos - perch).sqrMagnitude < 0.01f || phaseTime > 5f) { phase = 1; phaseTime = 0f; RpcEvent(Kind.Monkey, PestEvent.Arrive, -1); }
                    break;
                case 1: // tüner, kıkırdar, muz kabuğu atar
                {
                    sPos = Vector3.Lerp(sPos, perch, 1f - Mathf.Exp(-4f * dt));
                    int limb = ShooLimb(0.55f);
                    if (limb >= 0) { RpcEvent(Kind.Monkey, PestEvent.Shoo, (sbyte)limb); Flee(); break; }
                    if (peelsThrown < peelsLeft && phaseTime > 1.1f + peelsThrown * 1.1f)
                    {
                        var g = PickPeelTarget();
                        if (g != null)
                        {
                            RpcPeel(g.transform.position, sPos + new Vector3(0f, 0.35f, 0f));
                            if (peelsThrown == 0) RpcEvent(Kind.Monkey, PestEvent.Strike, -1);
                        }
                        peelsThrown++;
                    }
                    if (phaseTime > 1.1f + peelsLeft * 1.1f + 1.4f) Flee();
                    break;
                }
                default: // kaç: yana koşar
                    sPos += new Vector3(monkeySide * 3.5f * dt, 0.4f * dt, 0f);
                    if (phaseTime > 1.6f) Despawn();
                    break;
            }
        }

        /// <summary>Tırmanıcının yakınında, tutulmayan, sağlam bir taş (üstü tercih edilir).</summary>
        Grip PickPeelTarget()
        {
            Vector3 c = (Vector3)body.TorsoPos + new Vector3(0f, 0.4f, 0f); c.z = 0f;
            int n = Physics.OverlapSphereNonAlloc(c, 0.9f, buf, body.gripMask, QueryTriggerInteraction.Collide);
            var list = new List<Grip>();
            for (int k = 0; k < n; k++)
            {
                var g = buf[k].GetComponentInParent<Grip>();
                if (g == null || list.Contains(g)) continue;
                if (g.GetComponent<PestNest>() != null || g.GetComponent<CrumblingHold>() != null) continue;
                var bp = g.GetComponent<BananaPeel>(); if (bp != null && bp.Landed) continue;
                Vector3 gp = g.transform.position;
                bool held = false;
                for (int i = 0; i < 4; i++) if (((Vector2)body.ServerEffector(i) - (Vector2)gp).sqrMagnitude < 0.22f * 0.22f) held = true;
                if (held) continue;
                list.Add(g);
                if (gp.y > body.TorsoPos.y + 0.2f) list.Add(g); // yukarıdakilere iki kat şans
            }
            return list.Count > 0 ? list[Random.Range(0, list.Count)] : null;
        }

        [ClientRpc]
        void RpcPeel(Vector3 holdPos, Vector3 from)
        {
            monkeyThrowT = Time.time;
            var hits = Physics.OverlapSphere(holdPos, 0.06f, ~0, QueryTriggerInteraction.Collide);
            foreach (var h in hits)
            {
                var g = h.GetComponentInParent<Grip>();
                if (g == null) continue;
                BananaPeel.Throw(g, from, bodyMaterial);
                return;
            }
        }

        Vector3 TargetPos()
        {
            if (target >= 0) return body.ServerEffector(target) + new Vector3(0f, 0f, -0.08f);
            if (phase == 3 && kind != Kind.Bird) return (Vector3)body.TorsoPos + new Vector3(0f, 0.1f, -0.15f);
            return body.ServerHead();
        }

        Vector3 HoverOffset(float t)
        {
            switch (kind)
            {
                case Kind.Bird: return new Vector3(0f, 0.16f + Mathf.Abs(Mathf.Sin(t * 3f)) * 0.03f, 0f);  // kafanın üstünde zıplar
                case Kind.Spider: return new Vector3(0f, 0.04f, -0.03f);
                case Kind.Fly: return new Vector3(Mathf.Sin(t * 5.3f) * 0.22f, Mathf.Sin(t * 3.7f) * 0.15f + 0.08f, -0.15f + Mathf.Cos(t * 4.1f) * 0.08f);
                default: return new Vector3(Mathf.Sin(t * 3f) * 0.18f, 0.1f + Mathf.Sin(t * 2.3f) * 0.08f, -0.12f);
            }
        }

        [ClientRpc]
        void RpcEvent(Kind k, PestEvent e, sbyte limb)
        {
            if (k == Kind.Goat && (e == PestEvent.Stubborn || e == PestEvent.Arrive)) lastBleatT = Time.time;
            Event?.Invoke(k, e, limb);
        }

        [ClientRpc]
        void RpcNestBurst(Vector3 nestPos, Kind k, sbyte limb)
        {
            PestNest.Nearest(nestPos, 0.5f)?.PlayBurst();
            burstTime = Time.time;
            Event?.Invoke(k, PestEvent.Nest, limb);
        }

        // =====================================================================
        // GÖRSEL (tüm istemciler)
        Transform vroot;
        readonly Dictionary<string, Transform> models = new Dictionary<string, Transform>();
        readonly List<Transform> bees = new List<Transform>();
        readonly List<Transform> swarm = new List<Transform>();
        readonly List<Vector3> swarmPos = new List<Vector3>();
        LineRenderer thread;
        Vector3 smooth, lastSmooth;
        string shownKey;
        Transform shown;
        float burstTime = -10f;
        float monkeyThrowT = -10f, lastBleatT = -10f;
        float goatFace = 1f, monkeyFace = 1f;

        string ModelKey()
        {
            if (kind == Kind.None) return null;
            if (fromNest) return kind == Kind.Bird ? "AngryBird" : "Swarm" + kind;
            return kind.ToString();
        }

        void LateUpdate()
        {
            var key = ModelKey();
            if (key != shownKey) ShowModel(key);
            if (key == null || vroot == null) return;

            smooth = Vector3.Lerp(smooth, sPos, 1f - Mathf.Exp(-14f * Time.deltaTime));
            Vector3 move = smooth - lastSmooth; lastSmooth = smooth;
            float t = Time.time;

            if (fromNest && kind != Kind.Bird) { AnimateSwarm(t); UpdateThread(); return; }
            if (kind == Kind.Goat) { AnimateGoat(t); UpdateThread(); return; }
            if (kind == Kind.Monkey) { AnimateMonkey(t, move); UpdateThread(); return; }

            vroot.position = smooth;
            if (kind == Kind.Bird || kind == Kind.Fly)
            {
                var face = move.sqrMagnitude > 1e-6f ? new Vector3(move.x, 0f, move.z - 0.0001f) : Vector3.back;
                if (face.sqrMagnitude > 1e-6f) vroot.rotation = Quaternion.Slerp(vroot.rotation, Quaternion.LookRotation(face.normalized + Vector3.back * 0.6f), 10f * Time.deltaTime);
                if (fromNest) vroot.position += new Vector3(0f, Mathf.Sin(t * 22f) * 0.03f, 0f); // öfkeyle çırpınır
            }
            else if (kind == Kind.Spider) vroot.rotation = Quaternion.Euler(-80f, 0f, Mathf.Sin(t * 2f) * 15f);

            if (shown != null)
            {
                if (kind == Kind.Bird) PestArt.Flap(shown, t, fromNest ? 30f : (phase == 1 ? 0f : 16f), 55f, !fromNest && phase == 1 ? -60f : 0f);
                else PestArt.Flap(shown, t, 70f, 40f);
            }
            for (int i = 0; i < bees.Count; i++)
            {
                float a = t * (3.2f + i * 0.7f) + i * 2.1f;
                bees[i].localPosition = new Vector3(Mathf.Cos(a) * 0.16f, Mathf.Sin(a * 1.7f) * 0.08f, Mathf.Sin(a) * 0.1f);
                bees[i].localRotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) + Vector3.back * 0.2f);
            }
            UpdateThread();
        }

        Transform Part(string path) => shown != null ? shown.Find(path) : null;

        /// <summary>Keçi: zıplama kavisi, havada bacak toplama, inatlaşırken kafa eğip ayak vurma, melerken kafa kaldırma.</summary>
        void AnimateGoat(float t)
        {
            double now = NetworkTime.time;
            var p = HopPos(now);
            float k = Mathf.Clamp01((float)((now - sHopT0) / Mathf.Max(0.01f, sHopDur)));
            bool air = k < 1f;
            float dx = sHopTo.x - sHopFrom.x;
            if (phase == 1 || phase == 4) goatFace = Mathf.Sign(((Vector2)p - (Vector2)(ClimberBody.Instance != null ? ClimberBody.Instance.RenderTorsoPos : (Vector2)p)).x) * -1f;
            else if (Mathf.Abs(dx) > 0.03f) goatFace = Mathf.Sign(dx);
            if (goatFace == 0f) goatFace = 1f;
            vroot.position = p;
            // yana bakar ama hafif kameraya döner (profil + yüz)
            var want = Quaternion.Euler(0f, goatFace > 0f ? 115f : -115f, 0f);
            vroot.rotation = Quaternion.Slerp(vroot.rotation, want, 1f - Mathf.Exp(-12f * Time.deltaTime));
            // inişte ezilme
            float squash = !air && (now - (sHopT0 + sHopDur)) < 0.15f ? 0.85f : 1f;
            if (shown != null) shown.localScale = new Vector3(1f, squash, 1f);

            var head = Part("Goat/Head"); var tail = Part("Goat/Tail");
            float bleat = Mathf.Clamp01(1f - (Time.time - lastBleatT) / 0.8f);
            if (head != null)
            {
                Quaternion hq;
                if (phase == 4) hq = Quaternion.Euler(45f, 0f, 0f);                                      // tos için kafa aşağı
                else if (bleat > 0f) hq = Quaternion.Euler(-30f * bleat, Mathf.Sin(t * 30f) * 12f * bleat, 0f); // "Meee!"
                else if (phase == 1) hq = Quaternion.Euler(28f + Mathf.Sin(t * 4f) * 4f, Mathf.Sin(t * 2.3f) * 12f, 0f); // inatçı bakış
                else hq = Quaternion.Euler(air ? -10f : 5f, 0f, 0f);
                head.localRotation = Quaternion.Slerp(head.localRotation, hq, 1f - Mathf.Exp(-14f * Time.deltaTime));
            }
            if (tail != null) tail.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 14f) * 25f);
            foreach (var (name, front) in new[] { ("Goat/LegFL", true), ("Goat/LegFR", true), ("Goat/LegBL", false), ("Goat/LegBR", false) })
            {
                var leg = Part(name); if (leg == null) continue;
                float a = air ? (front ? -55f : 50f) : 0f;
                if (!air && phase == 1 && front && name.EndsWith("R")) a = Mathf.Max(0f, Mathf.Sin(t * 7f)) * -35f; // ayak vurur
                leg.localRotation = Quaternion.Slerp(leg.localRotation, Quaternion.Euler(a, 0f, 0f), 1f - Mathf.Exp(-18f * Time.deltaTime));
            }
        }

        /// <summary>Maymun: koşarken zıplayarak, tünerken kıkırdayarak, atarken kolunu savurarak.</summary>
        void AnimateMonkey(float t, Vector3 move)
        {
            bool moving = move.sqrMagnitude > 1e-6f;
            if (Mathf.Abs(move.x) > 0.0005f) monkeyFace = Mathf.Sign(move.x);
            float bob = moving ? Mathf.Abs(Mathf.Sin(t * 14f)) * 0.08f : 0f;
            vroot.position = smooth + Vector3.up * bob;
            var want = phase == 1 ? Quaternion.Euler(0f, 180f + monkeySide * -20f, 0f) : Quaternion.Euler(0f, monkeyFace > 0f ? 110f : -110f, 0f);
            vroot.rotation = Quaternion.Slerp(vroot.rotation, want, 1f - Mathf.Exp(-10f * Time.deltaTime));

            var head = Part("Monkey/Head"); var tail = Part("Monkey/Tail");
            var armR = Part("Monkey/ArmR"); var armL = Part("Monkey/ArmL"); var banana = Part("Monkey/ArmR/Banana");
            float sinceThrow = Time.time - monkeyThrowT;
            bool laughing = phase == 1 && sinceThrow > 0.4f && sinceThrow < 2.5f;
            if (head != null)
                head.localRotation = laughing ? Quaternion.Euler(-15f + Mathf.Sin(t * 22f) * 8f, 0f, Mathf.Sin(t * 11f) * 10f)
                                              : Quaternion.Euler(Mathf.Sin(t * 3f) * 5f, Mathf.Sin(t * 1.7f) * 20f, 0f);
            if (tail != null) tail.localRotation = Quaternion.Euler(Mathf.Sin(t * 3f) * 15f, 0f, Mathf.Sin(t * 2.2f) * 20f);
            if (armR != null)
            {
                float throwA = sinceThrow < 0.5f ? Mathf.Lerp(-170f, 30f, sinceThrow / 0.5f) : (phase == 1 ? -40f + Mathf.Sin(t * 4f) * 10f : (moving ? Mathf.Sin(t * 14f) * 40f : 0f));
                armR.localRotation = Quaternion.Euler(throwA, 0f, 0f);
            }
            if (armL != null) armL.localRotation = Quaternion.Euler(laughing ? -20f + Mathf.Sin(t * 22f) * 15f : (moving ? -Mathf.Sin(t * 14f) * 40f : 0f), 0f, laughing ? 20f : 0f);
            if (banana != null) banana.gameObject.SetActive(!(sinceThrow > 0.25f && sinceThrow < 0.9f) && !(phase != 1 && sinceThrow < 6f));
        }

        void UpdateThread()
        {
            if (thread == null) return;
            thread.enabled = kind == Kind.Spider && !fromNest;
            if (!thread.enabled) return;
            thread.SetPosition(0, sThreadTop);
            thread.SetPosition(1, smooth + Vector3.up * 0.03f);
        }

        /// <summary>
        /// Yuvadan fırlayan sürü: her üye yuvadan sırayla çıkar (kısa gecikmelerle "dökülür"),
        /// sürünün merkezine (sPos) uçar / koşar, etrafında kaynaşır; kaçarken dağılır.
        /// </summary>
        void AnimateSwarm(float t)
        {
            vroot.position = Vector3.zero; vroot.rotation = Quaternion.identity;
            float age = t - burstTime;
            bool spiders = kind == Kind.Spider;
            float spread = phase == 2 ? 1.4f : spiders ? 0.28f : 0.32f; // kaçarken dağılır
            for (int i = 0; i < swarm.Count; i++)
            {
                var m = swarm[i];
                float delay = i * (spiders ? 0.035f : 0.045f);
                float a = t * (spiders ? 1.5f : 4f + (i % 5) * 0.6f) + i * 2.399f;
                Vector3 orbit = spiders
                    ? new Vector3(Mathf.Cos(i * 2.399f) * spread * (0.4f + (i % 3) * 0.3f) + Mathf.Sin(a) * 0.04f,
                                  Mathf.Sin(i * 2.399f) * spread * (0.6f + (i % 4) * 0.25f) + Mathf.Cos(a * 1.3f) * 0.04f, -0.14f)
                    : new Vector3(Mathf.Cos(a) * spread * (0.6f + (i % 3) * 0.2f), Mathf.Sin(a * 1.37f) * spread * 0.7f, Mathf.Sin(a) * spread * 0.5f - 0.15f);
                Vector3 goal = smooth + orbit;
                Vector3 p;
                if (age < delay) p = sNest;
                else
                {
                    float k = Mathf.Clamp01((age - delay) / (spiders ? 0.5f : 0.3f));
                    p = Vector3.Lerp(swarmPos[i], goal, phase == 3 && k < 1f ? k * k : 1f - Mathf.Exp(-(spiders ? 10f : 16f) * Time.deltaTime));
                }
                Vector3 d = p - swarmPos[i];
                swarmPos[i] = p;
                m.position = p;
                m.gameObject.SetActive(age >= delay);
                if (spiders)
                {
                    // yüzey üstünde koşar: karın duvara, baş hareket yönüne
                    var dir = d.sqrMagnitude > 1e-7f ? new Vector3(d.x, d.y, 0f).normalized : Vector3.up;
                    m.rotation = Quaternion.LookRotation(dir, Vector3.back);
                }
                else
                {
                    var dir = d.sqrMagnitude > 1e-7f ? d.normalized + Vector3.back * 0.3f : Vector3.back;
                    m.rotation = Quaternion.LookRotation(dir);
                    PestArt.Flap(m, t + i, 75f, 40f);
                }
            }
        }

        void ShowModel(string key)
        {
            shownKey = key;
            if (vroot == null)
            {
                vroot = new GameObject("Pest").transform;
                vroot.SetParent(transform, false);
                thread = new GameObject("SpiderThread").AddComponent<LineRenderer>();
                thread.transform.SetParent(vroot.parent, false);
                thread.positionCount = 2; thread.startWidth = thread.endWidth = 0.008f;
                thread.sharedMaterial = wingMaterial;
                thread.startColor = thread.endColor = new Color(1f, 1f, 1f, 0.6f);
                thread.enabled = false;
            }
            foreach (var m in models.Values) m.gameObject.SetActive(false);
            bees.Clear(); swarm.Clear(); swarmPos.Clear(); shown = null;
            if (key == null) { if (thread != null) thread.enabled = false; return; }
            if (!models.TryGetValue(key, out var model))
            {
                model = Build(key);
                model.SetParent(vroot, false);
                models[key] = model;
            }
            model.gameObject.SetActive(true);
            shown = model;
            if (key == "Bees") foreach (Transform c in model) bees.Add(c);
            if (key.StartsWith("Swarm"))
                foreach (Transform c in model) { swarm.Add(c); swarmPos.Add(sNest); c.position = sNest; }
            smooth = lastSmooth = sPos;
            vroot.position = sPos;
        }

        // ---------------- Prosedürel low-poly modeller (PestArt) ----------------
        Transform Build(string key)
        {
            var root = new GameObject(key).transform;
            switch (key)
            {
                case "Bees": for (int i = 0; i < 3; i++) PestArt.Bee(root, 1f + i * 0.08f, bodyMaterial, wingMaterial); break;
                case "Fly": PestArt.Fly(root, 1f, bodyMaterial, wingMaterial); break;
                case "Bird": PestArt.Bird(root, 1f, false, bodyMaterial); break;
                case "AngryBird": PestArt.Bird(root, 1.35f, true, bodyMaterial); break;
                case "Spider": PestArt.Spider(root, 1f, false, bodyMaterial); break;
                case "Goat": PestArt.Goat(root, 1.3f, bodyMaterial); break;
                case "Monkey": PestArt.Monkey(root, 1.15f, bodyMaterial); break;
                case "SwarmBees": for (int i = 0; i < 16; i++) PestArt.Bee(root, 0.85f + (i % 4) * 0.06f, bodyMaterial, wingMaterial); break;
                case "SwarmSpider": for (int i = 0; i < 14; i++) PestArt.Spider(root, 0.42f + (i % 3) * 0.06f, true, bodyMaterial); break;
                case "SwarmFly": for (int i = 0; i < 12; i++) PestArt.Fly(root, 0.9f, bodyMaterial, wingMaterial); break;
            }
            return root;
        }
    }
}
