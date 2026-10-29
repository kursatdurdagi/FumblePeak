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
    /// Kovmak için: bir uzvu tutup hızlıca canlının üstünden geçir (el sallama).
    /// Yuvalar (PestNest): yuvayı tutan uzuv onu bozar → kızgın kuş / arı sürüsü / yavru örümcekler
    /// fırlar ve tırmanıcıyı düşürür.
    /// </summary>
    [RequireComponent(typeof(ClimberBody))]
    public class PestController : NetworkBehaviour
    {
        public enum Kind : byte { None = 0, Bees = 1, Fly = 2, Bird = 3, Spider = 4 }
        public enum PestEvent : byte { Arrive = 0, Shoo = 1, Strike = 2, Nest = 3, NestHit = 4 }

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

        ClimberBody body;
        float nextSpawn, phaseTime, annoyTotal;
        Vector3 vel;
        bool knocked;

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
        static readonly Kind[] KAll = { Kind.Bees, Kind.Bird, Kind.Fly, Kind.Spider };
        // [üst sınır (m), canlılar] — kamplar her 12 m'de; her bölgenin son kamp aralığında hepsi gelebilir
        static readonly (float top, Kind[] kinds)[] Zones =
        {
            (6f, KFly),             // ısınma
            (12f, KFlyBees),        // vadi
            (36f, KBeesFly),        // vadi üstü çayırlar
            (60f, KBeesBird),       // yosunlu orman sırtı
            (72f, KAll),            // orman sırtının tepesi: hepsi
            (96f, KBirdFly),        // kızıl kayalıklar
            (108f, KBirdSpider),
            (120f, KAll),           // kızıl kayaların tepesi: hepsi
            (144f, KSpiderBird),    // rüzgârlı sırtlar
            (156f, KAll),
            (180f, KSpider),        // buzul: çatlaklarda örümcekler
            (192f, KBirdSpider),
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
            for (int i = 0; i < 4; i++)
            {
                if (!body.ServerIsDragged(i)) continue;
                Vector3 e = body.ServerEffector(i);
                float d = Vector2.Distance(e, sPos);
                float v = ((Vector2)body.ServerEffectorVel(i)).magnitude;
                if (d < 0.5f && v > 0.7f)
                {
                    RpcEvent(kind, PestEvent.Shoo, (sbyte)i);
                    Flee();
                    return;
                }
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

        [ClientRpc] void RpcEvent(Kind k, PestEvent e, sbyte limb) => Event?.Invoke(k, e, limb);

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
                case "SwarmBees": for (int i = 0; i < 16; i++) PestArt.Bee(root, 0.85f + (i % 4) * 0.06f, bodyMaterial, wingMaterial); break;
                case "SwarmSpider": for (int i = 0; i < 14; i++) PestArt.Spider(root, 0.42f + (i % 3) * 0.06f, true, bodyMaterial); break;
                case "SwarmFly": for (int i = 0; i < 12; i++) PestArt.Fly(root, 0.9f, bodyMaterial, wingMaterial); break;
            }
            return root;
        }
    }
}
