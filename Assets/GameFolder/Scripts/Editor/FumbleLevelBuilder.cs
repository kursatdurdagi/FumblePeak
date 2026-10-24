using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FumblePeak.EditorTools
{
    /// <summary>
    /// "Başlangıç Vadisi" dağını prosedürel, low-poly olarak kurar:
    ///  • Tırmanış yamacı (yüksekliğe göre çimen → kaya → kar), yanlarda geriye kıvrılan sırt
    ///  • Rota boyunca belirgin (açık renk, tebeşirli) tutamak kayaları; kaygan yosun ve buz bölümleri
    ///  • 3 kamp çıkıntısı (kamp ateşi + çadır), karlı zirve platosu ve bayrak
    ///  • Vadi zemini, çam ağaçları, kayalar, mantarlar, uzak karlı dağ sıraları, bulutlar
    ///  • Prosedürel gökyüzü, sis, güneş, post-processing
    /// Tüm meshler Assets/GameFolder/Level/Meshes altına kaydedilir.
    /// </summary>
    public static class FumbleLevelBuilder
    {
        const string LevelDir = "Assets/GameFolder/Level";
        const string MeshDir = LevelDir + "/Meshes";
        const int GripLayer = 8;

        // ---- Rota ölçüleri ----
        public const float Camp1Y = 12.5f, Camp2Y = 24.5f, Camp3Y = 36f, SummitY = 44f;
        const float PeakY = 52f;

        static System.Random rnd;
        static float R() => (float)rnd.NextDouble();
        static float R(float a, float b) => a + (b - a) * R();

        // ---------------------------------------------------------------
        // Renk paleti (sıcak, sevimli)
        static readonly Color Grass = new Color(0.47f, 0.74f, 0.36f);
        static readonly Color GrassDark = new Color(0.33f, 0.6f, 0.3f);
        static readonly Color Moss = new Color(0.42f, 0.58f, 0.36f);
        static readonly Color RockWarm = new Color(0.62f, 0.52f, 0.46f);
        static readonly Color RockCool = new Color(0.56f, 0.55f, 0.6f);
        static readonly Color RockDark = new Color(0.44f, 0.4f, 0.42f);
        static readonly Color Snow = new Color(0.95f, 0.97f, 1f);
        static readonly Color SnowShade = new Color(0.82f, 0.88f, 0.97f);
        static readonly Color HoldSand = new Color(0.96f, 0.76f, 0.5f);
        static readonly Color HoldChalk = new Color(1f, 0.96f, 0.88f);
        static readonly Color HoldMoss = new Color(0.45f, 0.82f, 0.55f);
        static readonly Color HoldIce = new Color(0.7f, 0.9f, 1f);

        class Mats
        {
            public Material vc;      // vertex color lit (dünya)
            public Material hold;    // tutamaklar (vurgu için ayrı)
            public Material fire, flag, tent, line;
        }

        // ---------------------------------------------------------------
        public static void Build(Transform root, Material markerFallback)
        {
            rnd = new System.Random(20261024);
            EnsureDir(LevelDir); EnsureDir(MeshDir);
            var m = MakeMaterials();

            SetupEnvironment();

            var terrain = Group(root, "Terrain");
            BuildCliff(terrain, m);
            BuildValley(terrain, m);
            BuildDistantRanges(Group(root, "DistantMountains"), m);

            var route = Group(root, "Route");
            var holds = Group(route, "Holds");
            BuildRoute(route, holds, m);

            var deco = Group(root, "Decoration");
            BuildTrees(deco, m);
            BuildScatterRocks(deco, m);
            BuildMushrooms(deco, m);
            BuildClouds(Group(root, "Clouds"), m);
        }

        // ===============================================================
        // ANA MENÜ SAHNESİ: karlı zirve platosu, yaslanılacak kaya, kamp, şişe
        public struct MenuStageInfo
        {
            public Vector3 charPos, edgePos, edgeDir, rockHand;
            public float charYaw;
            public Mesh bottleMesh;
            public Material worldMat;
        }

        public static MenuStageInfo BuildMenuStage(Transform root)
        {
            rnd = new System.Random(7051);
            EnsureDir(LevelDir); EnsureDir(MeshDir);
            var m = MakeMaterials();
            SetupEnvironment();
            RenderSettings.fogStartDistance = 35f;
            RenderSettings.fogEndDistance = 220f;
            RenderSettings.fogColor = new Color(0.8f, 0.88f, 0.96f);
            // zirvedeyiz: ufkun altı bulut denizi gibi açık mavi-beyaz olsun (yeşil değil)
            var menuSkyPath = $"{LevelDir}/Menu_Sky.mat";
            var menuSky = AssetDatabase.LoadAssetAtPath<Material>(menuSkyPath);
            if (menuSky == null) { menuSky = new Material(RenderSettings.skybox); AssetDatabase.CreateAsset(menuSky, menuSkyPath); }
            menuSky.CopyPropertiesFromMaterial(RenderSettings.skybox);
            menuSky.SetColor("_GroundColor", new Color(0.86f, 0.91f, 0.97f));
            EditorUtility.SetDirty(menuSky);
            RenderSettings.skybox = menuSky;
            MakeRockSets();

            var info = new MenuStageInfo { worldMat = m.vc };
            var C = new Vector3(0.6f, 0f, 0.8f);
            const float R0 = 3.6f;

            // --- Plato: düzensiz karlı tepe + aşağı inen kayalık yamaçlar ---
            var mb = new MB();
            int seg = 22;
            var ringTop = new Vector3[seg]; var ringMid = new Vector3[seg]; var ringLow = new Vector3[seg]; var ringBase = new Vector3[seg]; var ringDeep = new Vector3[seg]; var ringFloor = new Vector3[seg];
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float r = R0 * (0.9f + R() * 0.18f);
                bool atEdge = i == 20 || i == 21 || i == 0; // uçurumun ucu: keskin, dimdik kenar
                ringTop[i] = C + d * r + Vector3.up * R(-0.04f, 0.04f);
                if (atEdge) r = R0 * 0.92f;
                ringMid[i] = C + d * (r + (atEdge ? 0.08f : R(0.15f, 0.5f))) + Vector3.down * R(1.2f, 2f);
                ringLow[i] = C + d * (r + R(1.2f, 2.4f)) + Vector3.down * R(4.5f, 6f);
                ringBase[i] = C + d * (r * 3.2f + R(0f, 3f)) + Vector3.down * R(13f, 16f);
                ringDeep[i] = C + d * (r * 7f + R(0f, 6f)) + Vector3.down * R(32f, 38f);
                ringFloor[i] = C + d * (r * 16f + R(0f, 10f)) + Vector3.down * R(65f, 75f);
            }
            var center = C + Vector3.up * 0.02f;
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                // tepe: hafif kubbe, kar
                mb.Tri(center, ringTop[j], ringTop[i], (p, n) => Jitter(n.y > 0.97f ? Snow : SnowShade, 0.02f));
                System.Func<Vector3, Vector3, Color> cliff = (p, n) =>
                {
                    var col = n.y > 0.55f ? SnowShade : Color.Lerp(RockCool, RockWarm, R());
                    if (p.y < -9f && n.y > 0.35f) col = SnowShade;
                    return Jitter(col, 0.04f);
                };
                mb.Tri(ringTop[i], ringTop[j], ringMid[i], cliff); mb.Tri(ringTop[j], ringMid[j], ringMid[i], cliff);
                mb.Tri(ringMid[i], ringMid[j], ringLow[i], cliff); mb.Tri(ringMid[j], ringLow[j], ringLow[i], cliff);
                mb.Tri(ringLow[i], ringLow[j], ringBase[i], cliff); mb.Tri(ringLow[j], ringBase[j], ringBase[i], cliff);
                mb.Tri(ringBase[i], ringBase[j], ringDeep[i], cliff); mb.Tri(ringBase[j], ringDeep[j], ringDeep[i], cliff);
                mb.Tri(ringDeep[i], ringDeep[j], ringFloor[i], cliff); mb.Tri(ringDeep[j], ringFloor[j], ringFloor[i], cliff);
            }
            var plateau = MeshObj("SummitPlateau", root, mb.Save("Menu_Plateau"), m.vc, Vector3.zero);
            plateau.AddComponent<MeshCollider>();


            // Uzak zirveler (aşağıda, sis içinde) + bulut denizi
            var far = Group(root, "FarPeaks");
            int idx = 0;
            for (int ring = 0; ring < 3; ring++)
            {
                float dist = 70f + ring * 45f;
                int count = 9 + ring * 3;
                for (int k = 0; k < count; k++)
                {
                    float a = (k + R(0.1f, 0.9f)) / count * Mathf.PI * 2f;
                    float h = R(45f, 90f) + ring * 10f;
                    var mesh = PeakMesh($"Menu_Far_{idx}", h * R(0.5f, 0.75f), h, R(0.6f, 0.72f), new Color(0.5f, 0.58f, 0.66f), 2000 + idx);
                    var go = MeshObj("Peak", far, mesh, m.vc, C + new Vector3(Mathf.Cos(a) * dist, -h - R(-10f, 8f) - 20f - ring * 6f, Mathf.Sin(a) * dist), false);
                    go.transform.rotation = Quaternion.Euler(0, R(0, 360), 0);
                    idx++;
                }
            }
            var clouds = Group(root, "CloudSea");
            var cloudMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{MeshDir}/Cloud.asset");
            if (cloudMesh != null)
                for (int i = 0; i < 26; i++)
                {
                    float a = R() * Mathf.PI * 2f, d = R(9f, 70f);
                    var go = MeshObj("Cloud", clouds, cloudMesh, m.vc, C + new Vector3(Mathf.Cos(a) * d, R(-16f, -7f), Mathf.Sin(a) * d), false);
                    go.isStatic = false;
                    float s = R(1.6f, 4f);
                    go.transform.localScale = new Vector3(s * 1.5f, s * 0.6f, s);
                    go.AddComponent<CloudDrift>().speed = R(0.15f, 0.4f);
                }

            // --- Karakter ve yaslanılacak kaya ---
            info.charPos = new Vector3(1.6f, 0f, 0.4f);
            info.charYaw = 196f;
            var yawQ = Quaternion.Euler(0, info.charYaw, 0);
            Vector3 left = yawQ * Vector3.left, back = yawQ * Vector3.back;
            var boulderMesh = RockMesh("Menu_Boulder", RockCool, Snow, 7101, 0.8f);
            var boulder = MeshObj("LeanRock", root, boulderMesh, m.vc, Vector3.zero);
            var bb = boulderMesh.bounds;
            var want = new Vector3(0.95f, 1.0f, 0.9f);
            boulder.transform.localScale = new Vector3(want.x / bb.size.x, want.y / bb.size.y, want.z / bb.size.z);
            boulder.transform.rotation = yawQ;
            boulder.transform.position = info.charPos + left * 0.98f + back * 0.05f + Vector3.up * (want.y * 0.5f - 0.04f)
                                         - yawQ * Vector3.Scale(bb.center, boulder.transform.localScale);
            info.rockHand = info.charPos + left * 0.57f + Vector3.up * 0.97f + back * 0.02f;

            // Uçurumun ucu: kameraya dönük sağ kenar
            info.edgeDir = new Vector3(1f, 0f, -0.35f).normalized;
            info.edgePos = C + info.edgeDir * (R0 * 0.92f - 0.12f);

            // --- Kamp ---
            BuildCampfire(root, C + new Vector3(-1.25f, 0.02f, 0.2f), m);
            var tent = MeshObj("Tent", root, TentMesh(), m.tent, C + new Vector3(-1.9f, 0.02f, 1.9f));
            tent.transform.rotation = Quaternion.Euler(0, 160f, 0);
            for (int i = 0; i < 3; i++)
            {
                var pine = MeshObj("Pine", root, PineMesh(), m.vc, C + new Vector3(-2.5f + i * 0.7f, 0f, 2.6f + (i % 2) * 0.4f));
                pine.transform.localScale = Vector3.one * R(0.45f, 0.65f);
            }
            for (int i = 0; i < 6; i++)
            {
                float a = R() * Mathf.PI * 2f, d = R(1.6f, 3.0f);
                var p = C + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                if ((p - info.charPos).sqrMagnitude < 1.4f || (p - info.edgePos).sqrMagnitude < 2.2f) continue;
                var r = MeshObj("Rock", root, decoRocks[rnd.Next(decoRocks.Length)], m.vc, p);
                r.transform.localScale = Vector3.one * R(0.15f, 0.35f);
                r.transform.rotation = Quaternion.Euler(0, R(0, 360), 0);
            }
            // Zirve bayrağı
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(pole.GetComponent<Collider>());
            pole.name = "FlagPole"; pole.transform.SetParent(root, false);
            pole.transform.position = C + new Vector3(0.4f, 1.1f, 2.4f);
            pole.transform.localScale = new Vector3(0.05f, 1.1f, 0.05f);
            pole.GetComponent<Renderer>().sharedMaterial = m.tent;
            var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(flag.GetComponent<Collider>());
            flag.name = "Flag"; flag.transform.SetParent(root, false);
            flag.transform.position = C + new Vector3(0.82f, 1.95f, 2.4f);
            flag.transform.localScale = new Vector3(0.8f, 0.48f, 0.03f);
            flag.GetComponent<Renderer>().sharedMaterial = m.flag;

            info.bottleMesh = BottleMesh();
            return info;
        }

        /// <summary>Yeşil cam şişe + krem etiket. Orijin ortada, boyun +Y.</summary>
        static Mesh BottleMesh()
        {
            var mb = new MB();
            var glass = new Color(0.25f, 0.62f, 0.32f);
            var label = new Color(1f, 0.93f, 0.75f);
            var cap = new Color(0.95f, 0.78f, 0.25f);
            // (yükseklik, yarıçap) profili
            var prof = new (float y, float r, Color c)[]
            {
                (-0.12f, 0.0f, glass), (-0.12f, 0.042f, glass), (-0.06f, 0.044f, label), (0.02f, 0.044f, glass),
                (0.05f, 0.04f, glass), (0.085f, 0.018f, glass), (0.14f, 0.016f, glass), (0.15f, 0.019f, cap), (0.165f, 0.018f, cap), (0.165f, 0f, cap)
            };
            int seg = 10;
            for (int k = 0; k < prof.Length - 1; k++)
                for (int s = 0; s < seg; s++)
                {
                    float a0 = s / (float)seg * Mathf.PI * 2f, a1 = (s + 1) / (float)seg * Mathf.PI * 2f;
                    Vector3 P(float y, float r, float a) => new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                    var p0 = P(prof[k].y, prof[k].r, a0); var p1 = P(prof[k].y, prof[k].r, a1);
                    var q0 = P(prof[k + 1].y, prof[k + 1].r, a0); var q1 = P(prof[k + 1].y, prof[k + 1].r, a1);
                    var col = prof[k].c;
                    if (prof[k].r > 0.0001f) mb.Tri(p0, q0, p1, col);
                    if (prof[k + 1].r > 0.0001f) mb.Tri(p1, q0, q1, col);
                }
            return mb.Save("Menu_Bottle");
        }

        static Transform Group(Transform parent, string name)
        {
            var g = new GameObject(name).transform;
            g.SetParent(parent, false);
            return g;
        }

        static void EnsureDir(string p)
        {
            if (AssetDatabase.IsValidFolder(p)) return;
            var parent = Path.GetDirectoryName(p).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(p));
        }

        // ---------------------------------------------------------------
        static Mats MakeMaterials()
        {
            var shader = Shader.Find("FumblePeak/VertexColorLit");
            Material Make(string name, Shader sh, Color c)
            {
                var path = $"{LevelDir}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, path); }
                mat.shader = sh;
                mat.SetColor("_BaseColor", c);
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
                return mat;
            }
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Mats
            {
                vc = Make("World_VertexColor", shader, Color.white),
                hold = Make("Hold_VertexColor", shader, Color.white),
                fire = Make("Fire", unlit, new Color(1f, 0.6f, 0.2f)),
                flag = Make("FlagCloth", lit, new Color(1f, 0.42f, 0.36f)),
                tent = Make("TentCloth", lit, new Color(0.98f, 0.66f, 0.25f)),
            };
            m.hold.SetColor("_RimColor", new Color(1f, 0.95f, 0.8f, 0.35f));
            var line = LineMaterial;
            m.line = line;
            return m;
        }

        /// <summary>Rehber çizgisi: köşe rengi destekli, şeffaf parçacık shader'ı.</summary>
        public static Material LineMaterial
        {
            get
            {
                var linePath = "Assets/GameFolder/Materials/Line.mat";
                var line = AssetDatabase.LoadAssetAtPath<Material>(linePath);
                if (line == null)
                {
                    line = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                    AssetDatabase.CreateAsset(line, linePath);
                }
                line.SetFloat("_Surface", 1);
                line.SetFloat("_Blend", 0);
                line.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                line.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                line.SetFloat("_ZWrite", 0);
                line.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                line.renderQueue = (int)RenderQueue.Transparent;
                line.SetTexture("_BaseMap", null);
                EditorUtility.SetDirty(line);
                return line;
            }
        }

        // ---------------------------------------------------------------
        // Ortam: gökyüzü, sis, güneş, post-processing
        static void SetupEnvironment()
        {
            var skyPath = $"{LevelDir}/Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
            if (sky == null) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, skyPath); }
            sky.SetFloat("_SunSize", 0.05f);
            sky.SetFloat("_SunSizeConvergence", 4f);
            sky.SetFloat("_AtmosphereThickness", 0.75f);
            sky.SetColor("_SkyTint", new Color(0.42f, 0.62f, 0.95f));
            sky.SetColor("_GroundColor", new Color(0.62f, 0.75f, 0.6f));
            sky.SetFloat("_Exposure", 1.1f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.74f, 0.85f, 0.93f);
            RenderSettings.fogStartDistance = 28f;
            RenderSettings.fogEndDistance = 150f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.8f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.68f, 0.7f, 0.72f);
            RenderSettings.ambientGroundColor = new Color(0.42f, 0.4f, 0.36f);

            var sun = new GameObject("Sun");
            sun.transform.rotation = Quaternion.Euler(38f, 22f, 0f);
            var l = sun.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.2f;
            l.color = new Color(1f, 0.93f, 0.82f);
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.75f;
            RenderSettings.sun = l;

            // Post-processing
            var profPath = $"{LevelDir}/MountainVolume.asset";
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profPath);
            if (prof == null) { prof = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(prof, profPath); }
            T Get<T>() where T : VolumeComponent
            {
                if (!prof.TryGet<T>(out var c)) { c = prof.Add<T>(true); AssetDatabase.AddObjectToAsset(c, prof); }
                c.active = true;
                return c;
            }
            var bloom = Get<Bloom>(); bloom.intensity.Override(0.35f); bloom.threshold.Override(1.05f); bloom.scatter.Override(0.6f);
            var tone = Get<Tonemapping>(); tone.mode.Override(TonemappingMode.Neutral);
            var ca = Get<ColorAdjustments>(); ca.saturation.Override(10f); ca.postExposure.Override(-0.1f); ca.contrast.Override(10f);
            var vig = Get<Vignette>(); vig.intensity.Override(0.2f); vig.smoothness.Override(0.5f);
            EditorUtility.SetDirty(prof);
            var vol = new GameObject("PostProcess").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = prof;
        }

        // ---------------------------------------------------------------
        // Mesh yardımcıları (düz gölgeli low-poly: her üçgen kendi köşeleri)
        class MB
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Color> c = new List<Color>();
            public readonly List<int> t = new List<int>();

            public void Tri(Vector3 a, Vector3 b, Vector3 d, Color col)
            {
                col = col.linear; // proje lineer renk uzayında: sRGB paleti dönüştür
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(d);
                c.Add(col); c.Add(col); c.Add(col);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 d, System.Func<Vector3, Vector3, Color> shade)
            {
                var n = Vector3.Cross(b - a, d - a).normalized;
                Tri(a, b, d, shade((a + b + d) / 3f, n));
            }

            public Mesh Save(string name)
            {
                var path = $"{MeshDir}/{name}.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                bool isNew = mesh == null;
                if (isNew) mesh = new Mesh();
                mesh.Clear();
                mesh.name = name;
                mesh.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(v);
                mesh.SetColors(c);
                mesh.SetTriangles(t, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                if (isNew) AssetDatabase.CreateAsset(mesh, path);
                else EditorUtility.SetDirty(mesh);
                return mesh;
            }
        }

        static GameObject MeshObj(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            go.isStatic = true;
            return go;
        }

        static float Noise(float x, float y) => Mathf.PerlinNoise(x + 37.1f, y + 11.7f) * 2f - 1f;

        /// <summary>Vadi zemini yüksekliği (ağaç/kaya yerleşimi de kullanır).</summary>
        static float ValleyH(float x, float z)
        {
            float h = 0f;
            float dist = Mathf.Max(0f, Mathf.Abs(x) - 6f);
            h += Mathf.Max(0f, Noise(x * 0.06f, z * 0.06f)) * dist * 0.18f;
            if (z < -2f) h -= 0.25f;
            return h;
        }

        static Color Jitter(Color c, float amt)
        {
            float j = (R() - 0.5f) * amt;
            return new Color(Mathf.Clamp01(c.r + j), Mathf.Clamp01(c.g + j), Mathf.Clamp01(c.b + j));
        }

        // ---------------------------------------------------------------
        // Tırmanış yamacı
        public static float RouteCenterX(float y) => Mathf.Sin(y * 0.16f) * 1.6f + Mathf.Sin(y * 0.05f + 1.3f) * 0.9f;

        static float HalfWidth(float y) => Mathf.Lerp(19f, 2.4f, Mathf.Pow(Mathf.Clamp01(y / PeakY), 0.7f));

        /// <summary>Yamaç yüzeyinin derinliği (z). Rota şeridinde karaktere yakın, yanlarda geriye kıvrılır.</summary>
        public static float FaceZ(float x, float y)
        {
            float cx = RouteCenterX(y);
            float hw = HalfWidth(y);
            float z = 0.42f + (Noise(x * 0.45f, y * 0.45f) * 0.5f + 0.5f) * 0.3f + (Noise(x * 1.3f, y * 1.3f) * 0.5f + 0.5f) * 0.12f;
            float side = Mathf.Abs(x - cx * 0.4f) - hw;
            if (side > 0f) z += side * side * 0.18f + side * 0.7f;
            // tepenin üstü geriye yatar → zirve silueti
            if (y > PeakY - 6f) z += (y - (PeakY - 6f)) * 0.9f;
            return z;
        }

        static Color FaceColor(Vector3 c, Vector3 n)
        {
            float y = c.y;
            float up = n.y;
            float wn = Noise(c.x * 0.3f, c.y * 0.3f) * 3f;
            Color col;
            if (y + wn < 5f) col = Color.Lerp(Moss, RockWarm, Mathf.InverseLerp(1f, 5f, y + wn));
            else if (y + wn < 22f) col = Color.Lerp(RockWarm, RockCool, Mathf.InverseLerp(5f, 22f, y + wn));
            else if (y + wn < 32f) col = Color.Lerp(RockCool, RockDark, Mathf.InverseLerp(22f, 32f, y + wn) * 0.5f);
            else col = Color.Lerp(RockCool, SnowShade, Mathf.InverseLerp(32f, 40f, y + wn));
            // yukarı bakan yüzeyler: aşağıda çimen, yukarıda kar
            if (up > 0.45f) col = y < 14f ? Color.Lerp(col, Grass, 0.75f) : Color.Lerp(col, Snow, 0.8f);
            if (y > 40f + wn) col = Color.Lerp(col, Snow, 0.85f);
            return Jitter(col, 0.05f);
        }

        static void BuildCliff(Transform parent, Mats m)
        {
            const float cell = 0.75f;
            float xMin = -30f, xMax = 30f, yMin = -1.5f, yMax = PeakY + 4f;
            int nx = Mathf.CeilToInt((xMax - xMin) / cell), ny = Mathf.CeilToInt((yMax - yMin) / cell);
            var pts = new Vector3[nx + 1, ny + 1];
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= ny; j++)
                {
                    float x = xMin + i * cell + (R() - 0.5f) * cell * 0.45f;
                    float y = yMin + j * cell + (R() - 0.5f) * cell * 0.45f;
                    pts[i, j] = new Vector3(x, y, FaceZ(x, y));
                }

            // Yamaç iki parçada (index limiti ve gölge mesafesi için)
            int half = ny / 2;
            for (int part = 0; part < 2; part++)
            {
                var mb = new MB();
                int j0 = part == 0 ? 0 : half, j1 = part == 0 ? half : ny;
                for (int i = 0; i < nx; i++)
                    for (int j = j0; j < j1; j++)
                    {
                        var a = pts[i, j]; var b = pts[i + 1, j]; var c = pts[i, j + 1]; var d = pts[i + 1, j + 1];
                        // kameraya (−Z) bakan üçgenler: saat yönü
                        mb.Tri(a, c, b, FaceColor);
                        mb.Tri(b, c, d, FaceColor);
                    }
                MeshObj("MountainFace_" + part, parent, mb.Save("MountainFace_" + part), m.vc, Vector3.zero);
            }

            // Görünmez arka duvar çarpışması gerekmez (Z kilitli). Zemin çarpışması:
            var ground = new GameObject("GroundCollider");
            ground.transform.SetParent(parent, false);
            ground.transform.position = new Vector3(0, -0.5f, 0);
            var bc = ground.AddComponent<BoxCollider>();
            bc.size = new Vector3(80f, 1f, 6f);
        }

        // ---------------------------------------------------------------
        // Vadi zemini (kameranın önünde ve yanlarda)
        static void BuildValley(Transform parent, Mats m)
        {
            const float cell = 1.6f;
            float xMin = -70f, xMax = 70f, zMin = -16f, zMax = 70f;
            int nx = Mathf.CeilToInt((xMax - xMin) / cell), nz = Mathf.CeilToInt((zMax - zMin) / cell);
            var pts = new Vector3[nx + 1, nz + 1];
            for (int i = 0; i <= nx; i++)
                for (int k = 0; k <= nz; k++)
                {
                    float x = xMin + i * cell + (R() - 0.5f) * cell * 0.4f;
                    float z = zMin + k * cell + (R() - 0.5f) * cell * 0.4f;
                    pts[i, k] = new Vector3(x, ValleyH(x, z), z);
                }
            var mb = new MB();
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    var a = pts[i, k]; var b = pts[i + 1, k]; var c = pts[i, k + 1]; var d = pts[i + 1, k + 1];
                    System.Func<Vector3, Vector3, Color> shade = (p, n) =>
                    {
                        float f = Mathf.PerlinNoise(p.x * 0.15f, p.z * 0.15f);
                        var col = Color.Lerp(GrassDark, Grass, f);
                        if (R() < 0.012f) col = Color.Lerp(col, new Color(1f, 0.88f, 0.55f), 0.35f); // çiçek lekesi
                        if (p.y > 6f) col = Color.Lerp(col, RockCool, Mathf.InverseLerp(6f, 14f, p.y));
                        return Jitter(col, 0.04f);
                    };
                    mb.Tri(a, c, b, shade);
                    mb.Tri(b, c, d, shade);
                }
            MeshObj("Valley", parent, mb.Save("Valley"), m.vc, Vector3.zero);
        }

        // ---------------------------------------------------------------
        // Uzak dağ sıraları (sis içinde kaybolur)
        static Mesh PeakMesh(string name, float radius, float height, float snowLine, Color baseCol, int seed)
        {
            var old = rnd; rnd = new System.Random(seed);
            var mb = new MB();
            int seg = 9, rings = 5;
            var ring = new Vector3[rings + 1, seg];
            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings;
                float rad = radius * (1f - t) * (1f - t * 0.1f);
                for (int s = 0; s < seg; s++)
                {
                    float a = (s + (r % 2) * 0.5f) / seg * Mathf.PI * 2f;
                    float jr = rad * (0.75f + R() * 0.4f);
                    ring[r, s] = new Vector3(Mathf.Cos(a) * jr, t * height * (0.9f + R() * 0.15f), Mathf.Sin(a) * jr);
                }
            }
            var top = new Vector3(0, height * 1.02f, 0);
            System.Func<Vector3, Vector3, Color> shade = (p, n) =>
            {
                float h = p.y / height;
                var col = Color.Lerp(baseCol, RockCool, Mathf.Clamp01(h * 1.3f));
                if (h > snowLine + (R() - 0.5f) * 0.08f) col = n.y > 0.2f ? Snow : SnowShade;
                return Jitter(col, 0.04f);
            };
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int s1 = (s + 1) % seg;
                    mb.Tri(ring[r, s], ring[r + 1, s], ring[r, s1], shade);
                    mb.Tri(ring[r, s1], ring[r + 1, s], ring[r + 1, s1], shade);
                }
            for (int s = 0; s < seg; s++) mb.Tri(ring[rings, s], top, ring[rings, (s + 1) % seg], shade);
            rnd = old;
            return mb.Save(name);
        }

        static void BuildDistantRanges(Transform parent, Mats m)
        {
            // [z, adet, yükseklik aralığı, taban rengi]
            var layers = new (float z, int count, float hMin, float hMax, Color col)[]
            {
                (28f, 7, 18f, 34f, new Color(0.45f, 0.6f, 0.45f)),
                (55f, 8, 30f, 55f, new Color(0.5f, 0.58f, 0.62f)),
                (95f, 9, 45f, 80f, new Color(0.6f, 0.66f, 0.76f)),
            };
            int idx = 0;
            foreach (var L in layers)
            {
                for (int k = 0; k < L.count; k++)
                {
                    float h = R(L.hMin, L.hMax);
                    var mesh = PeakMesh($"Peak_{idx}", h * R(0.55f, 0.8f), h, R(0.55f, 0.7f), L.col, 1000 + idx);
                    float x = Mathf.Lerp(-110f, 110f, (k + R(0.1f, 0.9f)) / L.count);
                    if (L.z < 40f && Mathf.Abs(x) < 16f) x += Mathf.Sign(x == 0 ? 1 : x) * 16f; // yamacın arkası boş kalsın
                    var go = MeshObj($"Peak_{idx}", parent, mesh, m.vc, new Vector3(x, -1f, L.z + R(-6f, 6f)), L.z < 60f);
                    go.transform.rotation = Quaternion.Euler(0, R(0, 360), 0);
                    idx++;
                }
            }
        }

        // ---------------------------------------------------------------
        // Kayalar (tutamak ve süs)
        static Mesh RockMesh(string name, Color main, Color top, int seed, float squash = 1f)
        {
            var old = rnd; rnd = new System.Random(seed);
            // ikosahedron + 1 bölme + gürültü
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] f =
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
            };
            var faces = new List<int>(f);
            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int i)) return i;
                verts.Add((verts[a] + verts[b]) * 0.5f);
                mid[key] = verts.Count - 1;
                return verts.Count - 1;
            }
            var nf = new List<int>();
            for (int i = 0; i < faces.Count; i += 3)
            {
                int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            for (int i = 0; i < verts.Count; i++)
            {
                var p = verts[i].normalized * (0.82f + R() * 0.3f);
                p.y *= squash;
                verts[i] = p * 0.5f;
            }
            var mb = new MB();
            for (int i = 0; i < nf.Count; i += 3)
            {
                Vector3 a = verts[nf[i]], b = verts[nf[i + 1]], c = verts[nf[i + 2]];
                // dışa bakan yüz: normal merkezden dışarı
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), a + b + c) < 0f) { var tmp = b; b = c; c = tmp; }
                mb.Tri(a, b, c, (p, n) => Jitter(n.y > 0.35f ? top : main, 0.05f));
            }
            rnd = old;
            return mb.Save(name);
        }

        static Mesh[] holdMeshes, holdMoss, holdIce, decoRocks;

        static void MakeRockSets()
        {
            holdMeshes = new Mesh[4]; holdMoss = new Mesh[3]; holdIce = new Mesh[3]; decoRocks = new Mesh[4];
            for (int i = 0; i < 4; i++) holdMeshes[i] = RockMesh("Hold_" + i, HoldSand, HoldChalk, 300 + i, 0.8f);
            for (int i = 0; i < 3; i++) holdMoss[i] = RockMesh("HoldMoss_" + i, HoldMoss, new Color(0.62f, 0.92f, 0.62f), 400 + i, 0.75f);
            for (int i = 0; i < 3; i++) holdIce[i] = RockMesh("HoldIce_" + i, HoldIce, Snow, 500 + i, 0.75f);
            for (int i = 0; i < 4; i++) decoRocks[i] = RockMesh("Rock_" + i, RockWarm, RockCool, 600 + i, 0.7f);
        }

        enum HoldKind { Rock, Moss, Ice }

        static void Hold(Transform parent, Vector2 p, HoldKind kind, Mats m)
        {
            var set = kind == HoldKind.Rock ? holdMeshes : kind == HoldKind.Moss ? holdMoss : holdIce;
            var mesh = set[rnd.Next(set.Length)];
            float s = R(0.27f, 0.34f);
            float zFace = FaceZ(p.x, p.y);
            var go = MeshObj(kind == HoldKind.Rock ? "Hold" : kind == HoldKind.Moss ? "Hold_Moss" : "Hold_Ice", parent, mesh, m.hold,
                new Vector3(p.x, p.y, zFace * 0.5f), true);
            go.isStatic = false; // vurgu için MaterialPropertyBlock kullanılır
            go.transform.localScale = new Vector3(s * 1.2f, s, Mathf.Max(0.5f, zFace + 0.2f));
            go.transform.rotation = Quaternion.Euler(0, 0, R(-25f, 25f));
            go.layer = GripLayer;
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 0.5f;
            // çarpışma merkezi oyun düzlemine (z=0) yakın olsun
            col.center = new Vector3(0, 0, -go.transform.position.z / go.transform.localScale.z);
            var grip = go.AddComponent<Grip>();
            grip.type = kind == HoldKind.Rock ? GripType.Rock : GripType.Slippery;
            grip.slideSpeed = kind == HoldKind.Ice ? 0.3f : 0.18f;
        }

        // ---------------------------------------------------------------
        // Rota: tutamaklar, kamp çıkıntıları, zirve
        static void BuildRoute(Transform route, Transform holds, Mats m)
        {
            MakeRockSets();

            var spawn = new GameObject("ClimberSpawn");
            spawn.transform.SetParent(route, false);
            spawn.transform.position = new Vector3(RouteCenterX(1f), 1.0f, 0);
            spawn.AddComponent<ClimberSpawn>();

            float[] camps = { Camp1Y, Camp2Y, Camp3Y };
            int campIndex = 0;
            float y = 1.5f;
            float side = -1f;
            while (y < SummitY + 0.2f)
            {
                float cx = RouteCenterX(y);
                // son metrelerde rota zirve platosunun kenarına kıvrılır (platonun altına sıkışmasın)
                if (y > SummitY - 4f) cx = RouteCenterX(SummitY) + Mathf.Lerp(cx - RouteCenterX(SummitY), 3.15f, Mathf.SmoothStep(0f, 1f, (y - (SummitY - 4f)) / 3.2f));
                // kamp yakınında: çıkıntı rotanın yanına kurulur, tutamaklar kenarından devam eder
                if (campIndex < camps.Length && y > camps[campIndex] - 0.2f)
                {
                    float cy = camps[campIndex];
                    BuildCamp(route, holds, m, campIndex, cy, cx);
                    campIndex++;
                    // merdiven kesintisiz devam eder (kamp yanda, dinlenmek isteyen geçer)
                }

                HoldKind kind = HoldKind.Rock;
                if (y > Camp1Y + 1f && y < Camp2Y && R() < 0.3f) kind = HoldKind.Moss;
                if (y > Camp2Y + 1f && R() < 0.3f) kind = HoldKind.Ice;

                // Basamaklı merdiven: tutamaklar dönüşümlü sol / sağ (el-el tırmanış ritmi)
                float colX = cx + side * (0.2f + R(-0.04f, 0.05f));
                Hold(holds, new Vector2(colX, y), kind, m);
                // kaygan tutamağın yanında güvenli bir yedek
                if (kind != HoldKind.Rock) Hold(holds, new Vector2(cx - side * 0.05f + R(-0.05f, 0.05f), y + 0.16f), HoldKind.Rock, m);
                // ara sıra bonus tutamak (alternatif yol)
                if (R() < 0.22f) Hold(holds, new Vector2(cx + side * R(0.75f, 0.95f), y + R(-0.1f, 0.1f)), HoldKind.Rock, m);
                // ayaklar için alçak yan tutamaklar (otomatik ayak bulma)
                if (R() < 0.3f) Hold(holds, new Vector2(cx - side * R(0.55f, 0.7f), y - 0.15f), HoldKind.Rock, m);

                side = -side;
                y += (y < Camp1Y ? 0.27f : 0.3f) + R(0f, 0.05f);
            }
            BuildSummit(route, holds, m);
        }

        static Mesh ledgeMesh, tentMesh, logMesh;

        static Mesh LedgeMesh(Color topCol)
        {
            // yassı, düzensiz bir kaya plakası: üst yüzey çimen/kar
            return RockMesh("Ledge_" + ColorUtility.ToHtmlStringRGB(topCol), RockWarm, topCol, 700 + (int)(topCol.r * 100), 0.35f);
        }

        static void BuildCamp(Transform route, Transform holds, Mats m, int index, float cy, float cx)
        {
            float side = index % 2 == 0 ? 1f : -1f;
            const float width = 3.0f;
            float ledgeX = cx + side * (width * 0.5f + 0.6f); // rota çıkıntının yanından geçer, altına sıkışmaz
            var top = cy < 14f ? Grass : Snow;

            var camp = new GameObject($"Camp_{index + 1}").transform;
            camp.SetParent(route, false);

            // Çıkıntı: görsel kaya plakası + katı kutu çarpışması
            var ledge = MeshObj("Ledge", camp, LedgeMesh(top), m.vc, new Vector3(ledgeX, cy - 0.12f, 0.3f));
            ledge.transform.localScale = new Vector3(width + 0.4f, 0.9f, 2.4f);
            var lc = new GameObject("LedgeCollider");
            lc.transform.SetParent(camp, false);
            lc.transform.position = new Vector3(ledgeX, cy - 0.15f, 0f);
            var box = lc.AddComponent<BoxCollider>();
            box.size = new Vector3(width, 0.4f, 2f);

            // Çıkıntının kenarında tutamaklar (üstüne çıkmak için)
            float edge = ledgeX - side * (width * 0.5f + 0.15f);
            Hold(holds, new Vector2(edge, cy - 0.35f), HoldKind.Rock, m);
            Hold(holds, new Vector2(edge - side * 0.45f, cy - 0.15f), HoldKind.Rock, m);
            Hold(holds, new Vector2(edge, cy + 0.35f), HoldKind.Rock, m);
            Hold(holds, new Vector2(edge - side * 0.5f, cy + 0.5f), HoldKind.Rock, m);
            // çıkıntının üstünde devam (duvardaki tutamaklar)
            Hold(holds, new Vector2(ledgeX - side * 0.6f, cy + 1.05f), HoldKind.Rock, m);
            Hold(holds, new Vector2(ledgeX + side * 0.2f, cy + 1.35f), HoldKind.Rock, m);

            // Kamp ateşi
            var firePos = new Vector3(ledgeX + side * 0.6f, cy + 0.05f, 0.35f);
            BuildCampfire(camp, firePos, m);

            // Çadır
            var tent = MeshObj("Tent", camp, TentMesh(), m.tent, new Vector3(ledgeX + side * 0.9f, cy + 0.05f, 0.8f));
            tent.transform.rotation = Quaternion.Euler(0, side > 0 ? -20f : 20f, 0);
            tent.transform.localScale = Vector3.one * 0.9f;

            // Kontrol noktası: rota + çıkıntı geniş alan
            var cp = new GameObject("Checkpoint");
            cp.transform.SetParent(camp, false);
            cp.transform.position = new Vector3((ledgeX + cx) * 0.5f, cy + 0.9f, 0);
            var t = cp.AddComponent<BoxCollider>();
            t.isTrigger = true;
            t.size = new Vector3(Mathf.Abs(ledgeX - cx) + width + 1.2f, 1.6f, 2f);
            var check = cp.AddComponent<Checkpoint>();
            var rp = new GameObject("RespawnPoint");
            rp.transform.SetParent(cp.transform, false);
            rp.transform.position = new Vector3(ledgeX - side * 0.3f, cy + 1.0f, 0);
            check.respawnPoint = rp.transform;

            // Çıkıntıda küçük çam
            var tree = MeshObj("Pine", camp, PineMesh(), m.vc, new Vector3(ledgeX - side * 1.1f, cy + 0.05f, 0.75f));
            tree.transform.localScale = Vector3.one * 0.55f;
        }

        static void BuildCampfire(Transform parent, Vector3 pos, Mats m)
        {
            var fire = new GameObject("Campfire");
            fire.transform.SetParent(parent, false);
            fire.transform.position = pos;
            if (logMesh == null) logMesh = RockMesh("Log", new Color(0.5f, 0.32f, 0.2f), new Color(0.6f, 0.4f, 0.25f), 801, 0.4f);
            for (int i = 0; i < 4; i++)
            {
                var log = MeshObj("Log", fire.transform, logMesh, m.vc, pos);
                log.transform.localScale = new Vector3(0.45f, 0.12f, 0.12f);
                log.transform.rotation = Quaternion.Euler(0, i * 45f, 0);
            }
            var flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(flame.GetComponent<Collider>());
            flame.name = "Flame";
            flame.transform.SetParent(fire.transform, false);
            flame.transform.position = pos + Vector3.up * 0.18f;
            flame.transform.localScale = new Vector3(0.2f, 0.32f, 0.2f);
            flame.GetComponent<Renderer>().sharedMaterial = m.fire;
            flame.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var ps = new GameObject("Sparks").AddComponent<ParticleSystem>();
            ps.transform.SetParent(fire.transform, false);
            ps.transform.position = pos + Vector3.up * 0.2f;
            ps.transform.rotation = Quaternion.Euler(-90, 0, 0);
            var main = ps.main;
            main.startLifetime = 1.2f; main.startSpeed = 0.6f; main.startSize = 0.06f;
            main.startColor = new Color(1f, 0.65f, 0.25f);
            main.gravityModifier = -0.1f;
            var em = ps.emission; em.rateOverTime = 12f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 15f; shape.radius = 0.08f;
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat");

            var lg = new GameObject("FireLight");
            lg.transform.SetParent(fire.transform, false);
            lg.transform.position = pos + new Vector3(0, 0.5f, -0.5f);
            var light = lg.AddComponent<Light>();
            light.type = LightType.Point; light.color = new Color(1f, 0.62f, 0.3f); light.range = 4f; light.intensity = 2.5f;
            light.shadows = LightShadows.None;
        }

        static Mesh TentMesh()
        {
            if (tentMesh != null) return tentMesh;
            var mb = new MB();
            var col = Color.white;
            Vector3 a = new Vector3(-0.6f, 0, -0.5f), b = new Vector3(0.6f, 0, -0.5f), c = new Vector3(0, 0.8f, -0.5f);
            Vector3 d = new Vector3(-0.6f, 0, 0.5f), e = new Vector3(0.6f, 0, 0.5f), f = new Vector3(0, 0.8f, 0.5f);
            mb.Tri(a, c, b, new Color(0.85f, 0.85f, 0.85f)); // ön (kapı)
            mb.Tri(d, e, f, col);
            mb.Tri(a, d, f, col); mb.Tri(a, f, c, col);
            mb.Tri(b, c, f, col); mb.Tri(b, f, e, col);
            tentMesh = mb.Save("Tent");
            return tentMesh;
        }

        static void BuildSummit(Transform route, Transform holds, Mats m)
        {
            float cx = RouteCenterX(SummitY);
            var summit = new GameObject("Summit").transform;
            summit.SetParent(route, false);

            var plate = MeshObj("SummitRock", summit, LedgeMesh(Snow), m.vc, new Vector3(cx, SummitY - 0.1f, 0.4f));
            plate.transform.localScale = new Vector3(6f, 1.2f, 3f);
            var lc = new GameObject("SummitCollider");
            lc.transform.SetParent(summit, false);
            lc.transform.position = new Vector3(cx, SummitY - 0.15f, 0);
            lc.AddComponent<BoxCollider>().size = new Vector3(5.4f, 0.4f, 2f);

            // kenar tutamakları
            Hold(holds, new Vector2(cx - 2.9f, SummitY - 0.3f), HoldKind.Rock, m);
            Hold(holds, new Vector2(cx + 2.9f, SummitY - 0.3f), HoldKind.Rock, m);
            Hold(holds, new Vector2(cx - 0.4f, SummitY - 0.55f), HoldKind.Rock, m);
            Hold(holds, new Vector2(cx + 0.4f, SummitY - 0.4f), HoldKind.Rock, m);
            Hold(holds, new Vector2(cx, SummitY + 0.35f), HoldKind.Rock, m);

            // Bayrak
            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(pole.GetComponent<Collider>());
            pole.name = "FlagPole";
            pole.transform.SetParent(summit, false);
            pole.transform.position = new Vector3(cx + 1.2f, SummitY + 1.3f, 0.5f);
            pole.transform.localScale = new Vector3(0.06f, 1.4f, 0.06f);
            pole.GetComponent<Renderer>().sharedMaterial = m.tent;
            var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(flag.GetComponent<Collider>());
            flag.name = "Flag";
            flag.transform.SetParent(summit, false);
            flag.transform.position = new Vector3(cx + 1.75f, SummitY + 2.35f, 0.5f);
            flag.transform.localScale = new Vector3(1.0f, 0.6f, 0.04f);
            flag.GetComponent<Renderer>().sharedMaterial = m.flag;

            BuildCampfire(summit, new Vector3(cx - 1.4f, SummitY + 0.05f, 0.4f), m);

            var trig = new GameObject("SummitTrigger");
            trig.transform.SetParent(summit, false);
            trig.transform.position = new Vector3(cx, SummitY + 1.1f, 0);
            var t = trig.AddComponent<BoxCollider>();
            t.isTrigger = true;
            t.size = new Vector3(5.4f, 1.8f, 2f);
            trig.AddComponent<SummitTrigger>();

            var cp = trig.AddComponent<Checkpoint>();
            cp.respawnPoint = trig.transform;
        }

        // ---------------------------------------------------------------
        static Mesh pineMesh;

        static Mesh PineMesh()
        {
            if (pineMesh != null) return pineMesh;
            var mb = new MB();
            var trunk = new Color(0.45f, 0.3f, 0.2f);
            int seg = 6;
            // gövde
            for (int s = 0; s < seg; s++)
            {
                float a0 = s / (float)seg * Mathf.PI * 2f, a1 = (s + 1) / (float)seg * Mathf.PI * 2f;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.12f, 0, Mathf.Sin(a0) * 0.12f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.12f, 0, Mathf.Sin(a1) * 0.12f);
                var q0 = p0 * 0.7f + Vector3.up * 0.7f; var q1 = p1 * 0.7f + Vector3.up * 0.7f;
                mb.Tri(p0, q0, p1, trunk); mb.Tri(p1, q0, q1, trunk);
            }
            // 3 kat koni
            float[] baseY = { 0.5f, 1.25f, 1.95f };
            float[] rad = { 0.85f, 0.65f, 0.45f };
            float[] h = { 1.1f, 0.95f, 0.85f };
            Color[] cols = { new Color(0.22f, 0.5f, 0.32f), new Color(0.26f, 0.56f, 0.35f), new Color(0.3f, 0.62f, 0.38f) };
            for (int k = 0; k < 3; k++)
            {
                var top = new Vector3(0, baseY[k] + h[k], 0);
                for (int s = 0; s < 7; s++)
                {
                    float a0 = s / 7f * Mathf.PI * 2f, a1 = (s + 1) / 7f * Mathf.PI * 2f;
                    var p0 = new Vector3(Mathf.Cos(a0) * rad[k], baseY[k], Mathf.Sin(a0) * rad[k]);
                    var p1 = new Vector3(Mathf.Cos(a1) * rad[k], baseY[k], Mathf.Sin(a1) * rad[k]);
                    mb.Tri(p0, top, p1, Jitter(cols[k], 0.05f));
                    mb.Tri(p0, p1, new Vector3(0, baseY[k], 0), cols[k] * 0.7f);
                }
            }
            pineMesh = mb.Save("Pine");
            return pineMesh;
        }

        static void BuildTrees(Transform parent, Mats m)
        {
            var mesh = PineMesh();
            for (int i = 0; i < 70; i++)
            {
                float x = R(-45f, 45f);
                float z = R(-10f, 26f);
                // tırmanış alanının önünü ve kamerayı kapatma
                if (Mathf.Abs(x) < 7f && z < 3f) continue;
                if (z < -3f && Mathf.Abs(x) < 14f) continue;
                if (z > 0f && z < FaceZ(x, 0.5f)) continue; // yamacın içine girmesin
                var go = MeshObj("Pine", parent, mesh, m.vc, new Vector3(x, ValleyH(x, z) - 0.05f, z));
                float s = R(0.9f, 2.2f);
                go.transform.localScale = new Vector3(s, s * R(0.9f, 1.25f), s);
                go.transform.rotation = Quaternion.Euler(0, R(0, 360), 0);
            }
        }

        static void BuildScatterRocks(Transform parent, Mats m)
        {
            // vadi zemini
            for (int i = 0; i < 40; i++)
            {
                float x = R(-30f, 30f), z = R(-8f, 14f);
                if (Mathf.Abs(x) < 5f && z < 2f) continue;
                var go = MeshObj("Rock", parent, decoRocks[rnd.Next(decoRocks.Length)], m.vc, new Vector3(x, ValleyH(x, z), z));
                float s = R(0.4f, 2.2f);
                go.transform.localScale = new Vector3(s * R(1f, 1.6f), s, s);
                go.transform.rotation = Quaternion.Euler(0, R(0, 360), 0);
            }
            // yamaçta süs kayalar (rota dışı, karakterin arkasında)
            for (int i = 0; i < 90; i++)
            {
                float y = R(2f, PeakY - 4f);
                float x = RouteCenterX(y) + (R() < 0.5f ? -1f : 1f) * R(2.2f, HalfWidth(y) + 3f);
                float zf = FaceZ(x, y);
                float s = R(0.6f, 1.8f);
                var go = MeshObj("Outcrop", parent, decoRocks[rnd.Next(decoRocks.Length)], m.vc, new Vector3(x, y, zf + s * 0.35f));
                go.transform.localScale = new Vector3(s * R(1f, 2f), s * R(0.5f, 1f), s);
                go.transform.rotation = Quaternion.Euler(R(-20, 20), R(0, 360), R(-20, 20));
            }
        }

        static void BuildMushrooms(Transform parent, Mats m)
        {
            var cap = RockMesh("MushroomCap", new Color(0.95f, 0.32f, 0.35f), new Color(1f, 0.45f, 0.45f), 901, 0.45f);
            var stem = RockMesh("MushroomStem", new Color(0.98f, 0.93f, 0.85f), new Color(0.98f, 0.93f, 0.85f), 902, 1.4f);
            void Make(Vector3 pos, float s, bool bouncy)
            {
                var root = new GameObject(bouncy ? "BounceMushroom" : "Mushroom").transform;
                root.SetParent(parent, false);
                root.position = pos;
                var st = MeshObj("Stem", root, stem, m.vc, pos + Vector3.up * 0.45f * s);
                st.transform.localScale = new Vector3(0.45f, 1f, 0.45f) * s;
                var cp = MeshObj("Cap", root, cap, m.vc, pos + Vector3.up * 1.0f * s);
                cp.transform.localScale = new Vector3(1.8f, 1f, 1.5f) * s;
                if (bouncy)
                {
                    var c = cp.AddComponent<SphereCollider>();
                    c.radius = 0.5f;
                    cp.AddComponent<BouncePad>().upSpeed = 9.5f;
                    cp.isStatic = false;
                }
            }
            Make(new Vector3(RouteCenterX(1f) - 4.2f, 0, 0), 1f, true);
            Make(new Vector3(6.5f, 0, -1.5f), 0.6f, false);
            Make(new Vector3(7.2f, 0, -0.8f), 0.4f, false);
            Make(new Vector3(-9f, 0, 1.5f), 0.7f, false);
        }

        // ---------------------------------------------------------------
        static void BuildClouds(Transform parent, Mats m)
        {
            var mb = new MB();
            var puffs = new[] { new Vector3(0, 0, 0), new Vector3(1.1f, 0.25f, 0.1f), new Vector3(-1.1f, 0.1f, 0), new Vector3(0.4f, 0.6f, 0.2f), new Vector3(-0.5f, 0.5f, -0.1f) };
            var rock = RockMesh("CloudPuff", Color.white, Color.white, 999, 0.9f);
            var pv = rock.vertices; var pt = rock.triangles;
            foreach (var p in puffs)
            {
                float s = R(1.1f, 1.6f);
                for (int i = 0; i < pt.Length; i += 3)
                {
                    var a = pv[pt[i]] * s * 2f + p; var b = pv[pt[i + 1]] * s * 2f + p; var c = pv[pt[i + 2]] * s * 2f + p;
                    mb.Tri(a, b, c, (q, n) => n.y > -0.2f ? Color.white : new Color(0.85f, 0.9f, 0.98f));
                }
            }
            var cloud = mb.Save("Cloud");
            for (int i = 0; i < 16; i++)
            {
                var pos = new Vector3(R(-70f, 70f), R(14f, 62f), R(18f, 45f));
                var go = MeshObj("Cloud", parent, cloud, m.vc, pos, false);
                go.isStatic = false;
                float s = R(1.5f, 3.5f);
                go.transform.localScale = new Vector3(s * 1.4f, s * 0.8f, s);
                go.AddComponent<CloudDrift>().speed = R(0.2f, 0.6f);
            }
        }
    }
}
