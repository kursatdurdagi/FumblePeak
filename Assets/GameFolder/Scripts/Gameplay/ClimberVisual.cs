using UnityEngine;

namespace FumblePeak
{
    /// <summary>
    /// TestPlayer modelini fizik gövdesine bağlar: iki kemikli IK (kollar + bacaklar),
    /// diegetik yorgunluk (kızarma, titreme, buhar - GDD 2.2) ve oyuncu imleç işaretleri.
    /// Tüm istemcilerde çalışır.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class ClimberVisual : MonoBehaviour
    {
        public ClimberBody body;
        public Transform visualRoot;
        public Material markerMaterial;
        public Material steamMaterial;
        public Material lineMaterial;
        public float smoothing = 18f;
        public Color fatigueColor = new Color(1f, 0.25f, 0.18f);

        static readonly string[] Upper = { "LeftArm", "RightArm", "LeftUpLeg", "RightUpLeg" };
        static readonly string[] Lower = { "LeftForeArm", "RightForeArm", "LeftLeg", "RightLeg" };
        static readonly string[] End = { "LeftHand", "RightHand", "LeftFoot", "RightFoot" };

        Transform hips;
        readonly Transform[] up = new Transform[4], lo = new Transform[4], en = new Transform[4];
        readonly Quaternion[] upBind = new Quaternion[4], loBind = new Quaternion[4], enBind = new Quaternion[4];
        Quaternion rootBindRot;

        Vector2 sTorso; float sRot; bool init;
        readonly Vector2[] sEff = new Vector2[4];

        Renderer[] skin;
        Color[] skinBase;
        MaterialPropertyBlock mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        readonly Transform[] markers = new Transform[4];
        readonly LineRenderer[] lines = new LineRenderer[4];
        readonly Renderer[] markerR = new Renderer[4];
        readonly ParticleSystem[] steam = new ParticleSystem[4];

        void Awake()
        {
            if (body == null) body = GetComponent<ClimberBody>();
            foreach (var t in visualRoot.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name.Contains(":") ? t.name.Substring(t.name.LastIndexOf(':') + 1) : t.name;
                if (n == "Hips") hips = t;
                for (int i = 0; i < 4; i++)
                {
                    if (n == Upper[i]) up[i] = t;
                    if (n == Lower[i]) lo[i] = t;
                    if (n == End[i]) en[i] = t;
                }
            }
            for (int i = 0; i < 4; i++)
            {
                upBind[i] = up[i].localRotation; loBind[i] = lo[i].localRotation; enBind[i] = en[i].localRotation;
            }
            rootBindRot = visualRoot.rotation;
            fingers[0] = FingerBones("Left"); fingers[1] = FingerBones("Right");
            for (int hh = 0; hh < 2; hh++)
            {
                fingerBind[hh] = new Quaternion[5][];
                for (int k = 0; k < 5; k++)
                {
                    fingerBind[hh][k] = new Quaternion[3];
                    for (int j = 0; j < 3; j++) fingerBind[hh][k][j] = fingers[hh][k][j] != null ? fingers[hh][k][j].localRotation : Quaternion.identity;
                }
            }

            var all = visualRoot.GetComponentsInChildren<Renderer>(true);
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var r in all) if (r.name.Contains("Body")) list.Add(r);
            skin = list.ToArray();
            skinBase = new Color[skin.Length];
            for (int i = 0; i < skin.Length; i++)
                skinBase[i] = skin[i].sharedMaterial != null && skin[i].sharedMaterial.HasProperty(BaseColor)
                    ? skin[i].sharedMaterial.GetColor(BaseColor) : Color.white;
            mpb = new MaterialPropertyBlock();
            hoverMpb = new MaterialPropertyBlock();

            for (int i = 0; i < 4; i++)
            {
                var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(m.GetComponent<Collider>());
                m.name = "Marker_" + (Limb)i;
                m.transform.SetParent(transform, false);
                markerR[i] = m.GetComponent<Renderer>();
                markerR[i].sharedMaterial = markerMaterial;
                markerR[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                markers[i] = m.transform;
                var lr = new GameObject("Guide_" + (Limb)i).AddComponent<LineRenderer>();
                lr.transform.SetParent(transform, false);
                lr.positionCount = 2;
                lr.startWidth = 0.035f; lr.endWidth = 0.015f;
                lr.sharedMaterial = lineMaterial != null ? lineMaterial : steamMaterial;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.numCapVertices = 4;
                lines[i] = lr;
                steam[i] = CreateSteam(en[i]);
            }
        }

        ParticleSystem CreateSteam(Transform parent)
        {
            var go = new GameObject("Steam");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = 0.9f; main.startSpeed = 0.5f; main.startSize = 0.12f;
            main.startColor = new Color(1f, 1f, 1f, 0.45f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.15f;
            var em = ps.emission; em.rateOverTime = 0f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.05f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0.5f, 0), new GradientAlphaKey(0f, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = steamMaterial != null ? steamMaterial : markerMaterial;
            ps.Play();
            return ps;
        }

        void LateUpdate()
        {
            if (body == null) return;
            // Host: fizik zaten interpolasyonlu → ek yumuşatma yok (gecikme/titreme olmasın)
            float k = body.isServer ? 1f : 1f - Mathf.Exp(-smoothing * Time.deltaTime);

            Vector2 tp = body.RenderTorsoPos; float tr = body.RenderTorsoRot;
            if (!init) { sTorso = tp; sRot = tr; for (int i = 0; i < 4; i++) sEff[i] = body.RenderEffectorPos(i); init = true; }
            bool snap = (tp - sTorso).sqrMagnitude > 9f; // ışınlanma (checkpoint)
            sTorso = snap ? tp : Vector2.Lerp(sTorso, tp, k);
            sRot = snap ? tr : Mathf.LerpAngle(sRot, tr, k);

            // Kök + kalça hizalama
            visualRoot.rotation = Quaternion.Euler(0, 0, sRot) * rootBindRot;
            Vector3 delta = hips.position - visualRoot.position;
            visualRoot.position = new Vector3(sTorso.x, sTorso.y, 0) - delta;

            float maxFat = 0f;
            for (int i = 0; i < 4; i++)
            {
                var e = body.RenderEffectorPos(i);
                sEff[i] = snap ? e : Vector2.Lerp(sEff[i], e, k);
                float fat = body.Fatigue01(i);
                maxFat = Mathf.Max(maxFat, fat);

                up[i].localRotation = upBind[i];
                lo[i].localRotation = loBind[i];
                en[i].localRotation = enBind[i];

                Vector3 target = new Vector3(sEff[i].x, sEff[i].y, LimbUtil.IsArm(i) ? -0.05f : -0.03f);
                if (fat > 0.8f) target += Random.insideUnitSphere * 0.025f * Mathf.InverseLerp(0.8f, 1f, fat); // titreme

                float side = (i % 2 == 0) ? -1f : 1f;
                Vector3 hint = LimbUtil.IsArm(i)
                    ? visualRoot.rotation * Quaternion.Inverse(rootBindRot) * new Vector3(side * 0.4f, -1f, -0.3f)
                    : visualRoot.rotation * Quaternion.Inverse(rootBindRot) * new Vector3(side * 0.7f, 0.2f, -0.6f);
                TwoBoneIK(up[i], lo[i], en[i], target, hint);

                // Buhar
                var em = steam[i].emission;
                em.rateOverTime = fat > 0.75f ? Mathf.Lerp(4f, 22f, Mathf.InverseLerp(0.75f, 1f, fat)) : 0f;

                UpdateMarker(i);
            }

            UpdateHover();

            // Kızarma (GDD: 5-8 sn) - nefes nefese kalan bir surat
            float red = Mathf.InverseLerp(0.45f, 0.85f, maxFat);
            for (int s = 0; s < skin.Length; s++)
            {
                skin[s].GetPropertyBlock(mpb);
                mpb.SetColor(BaseColor, Color.Lerp(skinBase[s], fatigueColor, red * 0.85f));
                skin[s].SetPropertyBlock(mpb);
            }
        }

        void UpdateMarker(int i)
        {
            var owner = FumblePlayer.OwnerOf(i);
            if (owner == null) { markers[i].gameObject.SetActive(false); lines[i].enabled = false; return; }
            bool local = owner.isLocalPlayer;
            bool dragged = local ? owner.DragLimb == i : body.IsDragged(i);
            bool hovered = local && owner.HoverLimb == i && !dragged;

            Vector2 eff = body.RenderEffectorPos(i);
            markers[i].gameObject.SetActive(true);
            markers[i].position = new Vector3(eff.x, eff.y, -0.25f);
            float pulse = 1f + Mathf.Sin(Time.time * 8f) * 0.12f;
            float size; float alpha;
            if (dragged) { size = 0.2f; alpha = 0.9f; }
            else if (hovered) { size = 0.32f * pulse; alpha = 0.8f; }       // "bunu tutabilirsin" göstergesi
            else { size = local ? 0.1f : 0.08f; alpha = local ? 0.6f : 0.45f; } // kimin uzvu olduğu belli olsun
            // Yorgunluk: tutunma süresi doldukça nokta büyür, kızarır ve hızla yanıp söner
            float fat = body.IsGrabbing(i) ? body.Fatigue01(i) : 0f;
            if (fat > 0.35f && !dragged && !hovered)
            {
                float k = Mathf.InverseLerp(0.35f, 1f, fat);
                size = Mathf.Lerp(size, 0.22f, k) * (1f + Mathf.Sin(Time.time * Mathf.Lerp(6f, 20f, k)) * 0.15f * k);
                alpha = Mathf.Lerp(alpha, 0.9f, k);
            }
            markers[i].localScale = Vector3.one * size;
            var c = Color.Lerp(owner.Color, new Color(1f, 0.12f, 0.08f), Mathf.InverseLerp(0.35f, 0.95f, fat)); c.a = alpha;
            markerR[i].GetPropertyBlock(mpb);
            mpb.SetColor(BaseColor, c);
            markerR[i].SetPropertyBlock(mpb);

            lines[i].enabled = dragged;
            if (dragged)
            {
                Vector2 t = local ? owner.Pointer : body.Target(i);
                Vector2 anchor = body.RenderAnchor(i);
                bool over = (t - anchor).magnitude > body.reach[i]; // erişimin dışı → gövdeyi zorluyor
                lines[i].SetPosition(0, new Vector3(eff.x, eff.y, -0.2f));
                lines[i].SetPosition(1, new Vector3(t.x, t.y, -0.2f));
                var lc = over ? new Color(1f, 0.3f, 0.25f) : owner.Color;
                lc.a = 0.7f; lines[i].startColor = lc; lc.a = 0.15f; lines[i].endColor = lc;
            }
        }

        // ---------- İmlecin altındaki kayayı vurgula (nereye tutunacağın belli olsun) ----------
        readonly Collider[] hoverBuf = new Collider[12];
        Renderer hovered;
        Color hoveredBase;
        static readonly Color HoverColor = new Color(1f, 0.85f, 0.35f);

        void UpdateHover()
        {
            var me = FumblePlayer.Local;
            Renderer best = null;
            if (me != null)
            {
                int L = me.DragLimb;
                if (L >= 0)
                {
                    // bırakınca tutunacağı taş: el/ayak taşın üstündeyse (sunucuyla aynı kural)
                    Vector2 c = body.RenderEffectorPos(L);
                    if (body.FindGrip(new Vector3(c.x, c.y, 0f), out var col, out _))
                        best = col.GetComponentInChildren<Renderer>();
                }
            }
            if (best != hovered)
            {
                if (hovered != null) hovered.SetPropertyBlock(null);
                hovered = best;
                if (hovered != null)
                {
                    var m = hovered.sharedMaterial;
                    hoveredBase = m != null && m.HasProperty(BaseColor) ? m.GetColor(BaseColor) : Color.white;
                }
            }
            if (hovered != null)
            {
                float k = 0.55f + Mathf.Sin(Time.time * 9f) * 0.2f;
                hoverMpb.SetColor(BaseColor, Color.Lerp(hoveredBase, HoverColor, k));
                hovered.SetPropertyBlock(hoverMpb);
            }
        }

        MaterialPropertyBlock hoverMpb;

        void OnDestroy()
        {
            if (hovered != null) hovered.SetPropertyBlock(null);
        }

        // ---------- El pozu: tırmanış tutuşu ----------
        readonly Transform[][][] fingers = new Transform[2][][];
        readonly float[] curlNow = new float[2];
        readonly Quaternion[][][] fingerBind = new Quaternion[2][][];

        Transform[][] FingerBones(string side)
        {
            string[] names = { "Index", "Middle", "Ring", "Pinky", "Thumb" };
            var all = visualRoot.GetComponentsInChildren<Transform>(true);
            var r = new Transform[5][];
            for (int f = 0; f < 5; f++)
            {
                r[f] = new Transform[3];
                for (int j = 0; j < 3; j++)
                {
                    string want = side + "Hand" + names[f] + (j + 1);
                    foreach (var t in all)
                    {
                        var n = t.name.Contains(":") ? t.name.Substring(t.name.LastIndexOf(':') + 1) : t.name;
                        if (n == want) { r[f][j] = t; break; }
                    }
                }
            }
            return r;
        }

        /// <summary>
        /// El her zaman yatay: parmaklar duvara (ileri, hafif yukarı), avuç aşağı — kayanın üstünü kavrar gibi.
        /// Tutunurken parmaklar kıvrılır, serbestken açık.
        /// </summary>
        void PoseHand(int i)
        {
            var h = en[i];
            Vector3 fingerDir = new Vector3(0f, 0.35f, 1f).normalized;   // duvara doğru
            Vector3 palmDir = new Vector3(0f, -1f, 0.35f).normalized;    // avuç aşağı (kayanın üstüne)
            // bağlama pozunda: up = parmak yönü, forward = avuç normali
            h.rotation = Quaternion.LookRotation(palmDir, fingerDir);
            bool grab = body.IsGrabbing(i);
            float want = grab ? 70f : body.IsDragged(i) ? 25f : 12f;
            curlNow[i] = Mathf.Lerp(curlNow[i], want, 1f - Mathf.Exp(-14f * Time.deltaTime));
            var f = fingers[i];
            if (f == null) return;
            for (int k = 0; k < 5; k++)
                for (int j = 0; j < 3; j++)
                {
                    var b = f[k][j]; if (b == null) continue;
                    float a = k == 4 ? curlNow[i] * (j == 0 ? 0.3f : 0.6f) : curlNow[i] * (j == 0 ? 0.8f : 1f);
                    b.localRotation = fingerBind[i][k][j] * Quaternion.Euler(a, 0f, 0f);
                }
        }

        /// <summary>Analitik iki kemikli IK (D. Holden yöntemi).</summary>
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
