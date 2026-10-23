using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace FumblePeak.EditorTools
{
    /// <summary>
    /// Menü: FumblePeak → Kurulum → Hepsini Kur
    /// Katmanları, materyalleri, PanelSettings'i, Player/Climber prefablarını,
    /// MainMenu ve GameScene sahnelerini otomatik oluşturur. Tekrar çalıştırmak güvenlidir.
    /// </summary>
    public static class FumbleSetup
    {
        const string Root = "Assets/GameFolder";
        const string ModelPath = Root + "/GameAsset/Player/TestPlayer.fbx";
        const string MainMenuPath = Root + "/Scenes/MainMenu.unity";
        const string GamePath = Root + "/Scenes/GameScene.unity";
        const string PrefabDir = Root + "/Prefabs";
        const string MatDir = Root + "/Materials";
        const string UIDir = Root + "/UI";
        const string ResUIDir = Root + "/Resources/UI";

        public const int GripLayer = 8;
        public const int ClimberLayer = 9;

        class Mats
        {
            public Material rock, slippery, ground, cliff, ledge, wood, fire, flag, mushroom, mushroomCap, marker, steam;
            public Material skin, shirt, pants, shoes, hair;
        }

        [MenuItem("FumblePeak/Kurulum/Hepsini Kur", priority = 0)]
        public static void SetupAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolders();
            EnsureLayers();
            FumbleLocalizationSeeder.Seed();
            var mats = MakeMaterials();
            var panel = MakePanelSettings();
            var player = MakePlayerPrefab();
            var climber = MakeClimberPrefab(mats);
            BuildGameScene(panel, mats);
            BuildMainMenu(panel, player, climber);
            SetBuildScenes();
            WriteSteamAppId();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("<b>[FumblePeak]</b> Kurulum tamam. MainMenu sahnesinden Play'e bas.");
        }

        // ------------------------------------------------------------
        static void EnsureFolders()
        {
            foreach (var p in new[] { PrefabDir, MatDir, UIDir, Root + "/Resources", ResUIDir })
                if (!AssetDatabase.IsValidFolder(p))
                {
                    var parent = Path.GetDirectoryName(p).Replace('\\', '/');
                    AssetDatabase.CreateFolder(parent, Path.GetFileName(p));
                }
        }

        static void EnsureLayers()
        {
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tm.FindProperty("layers");
            layers.GetArrayElementAtIndex(GripLayer).stringValue = "Grip";
            layers.GetArrayElementAtIndex(ClimberLayer).stringValue = "Climber";
            tm.ApplyModifiedProperties();
            Physics.IgnoreLayerCollision(ClimberLayer, ClimberLayer, true);
            Physics.IgnoreLayerCollision(ClimberLayer, GripLayer, true); // tutamaklar trigger, yine de güvence
        }

        // ------------------------------------------------------------
        static Material Lit(string name, Color c, float smooth = 0.15f)
        {
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Unlit(string name, Color c, bool transparent)
        {
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            if (transparent)
            {
                m.SetFloat("_Surface", 1);
                m.SetFloat("_Blend", 0);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetOverrideTag("RenderType", "Transparent");
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mats MakeMaterials()
        {
            var m = new Mats
            {
                rock = Lit("Rock", new Color(0.52f, 0.47f, 0.42f)),
                slippery = Lit("SlipperyMoss", new Color(0.45f, 0.78f, 0.62f), 0.85f),
                ground = Lit("Ground", new Color(0.36f, 0.58f, 0.27f)),
                cliff = Lit("Cliff", new Color(0.33f, 0.31f, 0.33f)),
                ledge = Lit("Ledge", new Color(0.42f, 0.36f, 0.30f)),
                wood = Lit("Wood", new Color(0.45f, 0.28f, 0.15f)),
                fire = Unlit("Fire", new Color(1f, 0.55f, 0.15f), false),
                flag = Lit("Flag", new Color(1f, 0.45f, 0.2f)),
                mushroom = Lit("MushroomStem", new Color(0.95f, 0.9f, 0.8f)),
                mushroomCap = Lit("MushroomCap", new Color(0.9f, 0.2f, 0.25f), 0.4f),
                marker = Unlit("Marker", Color.white, true),
                skin = Lit("Char_Skin", new Color(0.93f, 0.74f, 0.6f), 0.3f),
                shirt = Lit("Char_Shirt", new Color(0.95f, 0.5f, 0.2f)),
                pants = Lit("Char_Pants", new Color(0.22f, 0.26f, 0.38f)),
                shoes = Lit("Char_Shoes", new Color(0.95f, 0.95f, 0.95f)),
                hair = Lit("Char_Hair", new Color(0.12f, 0.08f, 0.06f)),
            };
            m.steam = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat");
            return m;
        }

        static PanelSettings MakePanelSettings()
        {
            var tssPath = UIDir + "/FumbleTheme.tss";
            if (!File.Exists(tssPath))
            {
                File.WriteAllText(tssPath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(tssPath);
            }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(tssPath);
            var path = ResUIDir + "/FumblePanelSettings.asset";
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(path);
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(ps, path);
            }
            ps.themeStyleSheet = theme;
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            EditorUtility.SetDirty(ps);
            return ps;
        }

        // ------------------------------------------------------------
        static GameObject MakePlayerPrefab()
        {
            var go = new GameObject("FumblePlayer");
            go.AddComponent<NetworkIdentity>();
            go.AddComponent<FumblePlayer>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabDir + "/FumblePlayer.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        static Transform FindBone(Transform root, string shortName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name.Contains(":") ? t.name.Substring(t.name.LastIndexOf(':') + 1) : t.name;
                if (n == shortName) return t;
            }
            return null;
        }

        static GameObject MakeClimberPrefab(Mats mats)
        {
            var root = new GameObject("Climber");
            root.AddComponent<NetworkIdentity>();
            var body = root.AddComponent<ClimberBody>();
            var vis = root.AddComponent<ClimberVisual>();
            body.syncInterval = 0.033f;

            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Visual";
            model.transform.SetParent(root.transform, false);
            for (int i = model.transform.childCount - 1; i >= 0; i--)
            {
                var c = model.transform.GetChild(i);
                if (c.GetComponent<Camera>() != null || c.GetComponent<Light>() != null) Object.DestroyImmediate(c.gameObject);
            }
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.updateWhenOffscreen = true;
                var n = r.name;
                Material mat = n.Contains("Shirt") ? mats.shirt : n.Contains("Pants") ? mats.pants : n.Contains("Sneakers") ? mats.shoes
                             : n.Contains("Eyelashes") ? mats.hair : mats.skin;
                r.sharedMaterials = new[] { mat };
            }

            var hips = FindBone(model.transform, "Hips");
            string[] up = { "LeftArm", "RightArm", "LeftUpLeg", "RightUpLeg" };
            string[] lo = { "LeftForeArm", "RightForeArm", "LeftLeg", "RightLeg" };
            string[] en = { "LeftHand", "RightHand", "LeftFoot", "RightFoot" };
            string[] names = { "Hand_L", "Hand_R", "Foot_L", "Foot_R" };

            var torsoGo = new GameObject("Torso") { layer = ClimberLayer };
            torsoGo.transform.SetParent(root.transform, false);
            var hp = hips.position; hp.z = 0;
            torsoGo.transform.position = hp;
            var torso = torsoGo.AddComponent<Rigidbody>();
            torso.mass = 8f;
            torso.linearDamping = 0.3f;
            torso.angularDamping = 3f;
            torso.interpolation = RigidbodyInterpolation.Interpolate;
            torso.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            torso.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
            var cap = torsoGo.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0, 0.22f, 0); cap.radius = 0.17f; cap.height = 0.75f; cap.direction = 1;
            var head = torsoGo.AddComponent<SphereCollider>();
            head.center = new Vector3(0, 0.68f, 0); head.radius = 0.12f;

            body.torso = torso;
            body.effectors = new Rigidbody[4];
            body.anchorLocal = new Vector3[4];
            body.reach = new float[4];
            body.gripMask = 1 << GripLayer;

            for (int i = 0; i < 4; i++)
            {
                var a = FindBone(model.transform, up[i]);
                var b = FindBone(model.transform, lo[i]);
                var c = FindBone(model.transform, en[i]);
                var anchor = a.position; anchor.z = 0;
                float reach = ((b.position - a.position).magnitude + (c.position - b.position).magnitude) * (i < 2 ? 1.12f : 1.0f); // kollara oynanış için biraz ekstra erişim
                body.anchorLocal[i] = anchor - hp;
                body.reach[i] = reach;

                var eGo = new GameObject(names[i]) { layer = ClimberLayer };
                eGo.transform.SetParent(root.transform, false);
                bool arm = i < 2;
                var dir = arm ? new Vector3(i == 0 ? -0.35f : 0.35f, 1f, 0).normalized : Vector3.down;
                eGo.transform.position = anchor + dir * reach * 0.85f;
                var rb = eGo.AddComponent<Rigidbody>();
                rb.mass = arm ? 1f : 1.4f;
                rb.linearDamping = 0.5f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
                var sc = eGo.AddComponent<SphereCollider>();
                sc.radius = arm ? 0.07f : 0.09f;

                var j = eGo.AddComponent<ConfigurableJoint>();
                j.connectedBody = torso;
                j.autoConfigureConnectedAnchor = false;
                j.anchor = Vector3.zero;
                j.connectedAnchor = body.anchorLocal[i];
                j.xMotion = ConfigurableJointMotion.Limited;
                j.yMotion = ConfigurableJointMotion.Limited;
                j.zMotion = ConfigurableJointMotion.Locked;
                j.angularXMotion = ConfigurableJointMotion.Free;
                j.angularYMotion = ConfigurableJointMotion.Free;
                j.angularZMotion = ConfigurableJointMotion.Free;
                j.linearLimit = new SoftJointLimit { limit = reach };
                j.enableCollision = false;
                body.effectors[i] = rb;
            }

            vis.body = body;
            vis.visualRoot = model.transform;
            vis.markerMaterial = mats.marker;
            vis.steamMaterial = mats.steam;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/Climber.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------
        static void ClearScene(Scene s)
        {
            foreach (var go in s.GetRootGameObjects()) Object.DestroyImmediate(go);
        }

        static void BuildMainMenu(PanelSettings panel, GameObject player, GameObject climber)
        {
            var scene = EditorSceneManager.OpenScene(MainMenuPath, OpenSceneMode.Single);
            ClearScene(scene);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.09f);
            camGo.AddComponent<AudioListener>();

            var nmGo = new GameObject("NetworkManager");
            var kcp = nmGo.AddComponent<kcp2k.KcpTransport>();
            var nm = nmGo.AddComponent<FumbleNetworkManager>();
            nmGo.AddComponent<SteamLobby>();
            nm.transport = kcp;
            nm.localTransport = kcp;
            nm.playerPrefab = player;
            nm.climberPrefab = climber;
            nm.spawnPrefabs = new List<GameObject> { climber };
            nm.onlineScene = "";           // lobi Ana Menü'de kurulur
            nm.gameScene = GamePath;       // host "Başlat" deyince geçilir
            nm.offlineScene = MainMenuPath;
            nm.maxConnections = 4;
            nm.autoCreatePlayer = true;
            nm.dontDestroyOnLoad = true;
            nm.runInBackground = true;
            nm.sendRate = 60;

            var uiGo = new GameObject("MainMenuUI");
            var doc = uiGo.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UIDir + "/MainMenu.uxml");
            uiGo.AddComponent<MainMenuController>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // ------------------------------------------------------------
        static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale, Material m, bool keepCollider = true)
        {
            var g = GameObject.CreatePrimitive(t);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (!keepCollider) Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }

        static void Hold(Transform parent, Vector3 pos, Mats mats, System.Random rnd, bool slippery)
        {
            float s = 0.26f + (float)rnd.NextDouble() * 0.12f;
            var g = Prim(PrimitiveType.Sphere, slippery ? "Hold_Slippery" : "Hold", parent,
                new Vector3(pos.x, pos.y, 0.08f), new Vector3(s * 1.25f, s, s), slippery ? mats.slippery : mats.rock);
            g.transform.rotation = Quaternion.Euler(0, 0, (float)rnd.NextDouble() * 360f);
            g.layer = GripLayer;
            g.GetComponent<Collider>().isTrigger = true;
            g.isStatic = true;
            var grip = g.AddComponent<Grip>();
            grip.type = slippery ? GripType.Slippery : GripType.Rock;
        }

        static void Ledge(Transform parent, string name, Vector3 center, float width, Mats mats)
        {
            var l = Prim(PrimitiveType.Cube, name, parent, center, new Vector3(width, 0.4f, 1.6f), mats.ledge);
            l.isStatic = true;
        }

        static void Campfire(Transform parent, Vector3 pos, Mats mats)
        {
            var fire = new GameObject("Campfire");
            fire.transform.SetParent(parent, false);
            fire.transform.position = pos;
            for (int i = 0; i < 3; i++)
            {
                var log = Prim(PrimitiveType.Cylinder, "Log", fire.transform, pos + new Vector3(0, 0.06f, 0.3f), new Vector3(0.08f, 0.25f, 0.08f), mats.wood, false);
                log.transform.rotation = Quaternion.Euler(90, i * 60f, 0);
            }
            Prim(PrimitiveType.Sphere, "Flame", fire.transform, pos + new Vector3(0, 0.2f, 0.3f), new Vector3(0.22f, 0.32f, 0.22f), mats.fire, false);
            var lg = new GameObject("FireLight");
            lg.transform.SetParent(fire.transform, false);
            lg.transform.position = pos + new Vector3(0, 0.6f, -0.2f);
            var light = lg.AddComponent<Light>();
            light.type = LightType.Point; light.color = new Color(1f, 0.6f, 0.25f); light.range = 5f; light.intensity = 3f;
        }

        static void BuildGameScene(PanelSettings panel, Mats mats)
        {
            var scene = EditorSceneManager.OpenScene(GamePath, OpenSceneMode.Single);
            ClearScene(scene);

            var sky = new Color(0.55f, 0.74f, 0.88f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = sky;
            RenderSettings.fogStartDistance = 14f;
            RenderSettings.fogEndDistance = 55f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.75f, 0.82f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.55f, 0.6f);
            RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.25f);

            var sun = new GameObject("Sun");
            sun.transform.rotation = Quaternion.Euler(40f, 25f, 0f);
            var sl = sun.AddComponent<Light>();
            sl.type = LightType.Directional; sl.intensity = 1.2f; sl.color = new Color(1f, 0.95f, 0.86f);
            sl.shadows = LightShadows.Soft;

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(0, 1.6f, -7.5f);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = sky;
            cam.fieldOfView = 50f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<ClimberCamera>();

            new GameObject("DevBootstrap").AddComponent<DevBootstrap>();

            // ---------- Seviye: Başlangıç Vadisi ----------
            var level = new GameObject("Level_BaslangicVadisi").transform;
            Prim(PrimitiveType.Cube, "Ground", level, new Vector3(0, -0.5f, 0), new Vector3(40, 1, 8), mats.ground).isStatic = true;
            Prim(PrimitiveType.Cube, "CliffWall", level, new Vector3(0, 30f, 1.2f), new Vector3(16, 64, 1.5f), mats.cliff, false).isStatic = true;
            for (int i = 0; i < 6; i++) // arka dağlar
                Prim(PrimitiveType.Cube, "BackRock", level, new Vector3(-18 + i * 7.5f, 8 + (i % 3) * 5, 14), new Vector3(7, 22 + (i % 2) * 10, 3), mats.cliff, false)
                    .transform.rotation = Quaternion.Euler(0, 0, (i % 2 == 0 ? 8 : -6));

            var spawn = new GameObject("ClimberSpawn");
            spawn.transform.SetParent(level, false);
            spawn.transform.position = new Vector3(0, 1.0f, 0);
            spawn.AddComponent<ClimberSpawn>();

            // Mantar (esnek zıplama)
            var mush = new GameObject("Mushroom");
            mush.transform.SetParent(level, false);
            Prim(PrimitiveType.Cylinder, "Stem", mush.transform, new Vector3(-4.2f, 0.45f, 0), new Vector3(0.45f, 0.45f, 0.45f), mats.mushroom).isStatic = true;
            var capGo = Prim(PrimitiveType.Sphere, "Cap", mush.transform, new Vector3(-4.2f, 1.0f, 0), new Vector3(1.8f, 0.55f, 1.4f), mats.mushroomCap);
            capGo.AddComponent<BouncePad>().upSpeed = 9.5f;

            var holds = new GameObject("Holds").transform;
            holds.SetParent(level, false);
            var rnd = new System.Random(1337);
            float cx = 0f;

            // Bölüm 1: y 1.9 → 14  (sadece kaya)
            for (float y = 1.65f; y < 14.2f; y += 0.42f + (float)rnd.NextDouble() * 0.12f)
            {
                cx = Mathf.Clamp(cx + ((float)rnd.NextDouble() - 0.5f) * 0.5f, -2.5f, 2.5f);
                Hold(holds, new Vector3(cx - 0.32f - (float)rnd.NextDouble() * 0.12f, y, 0), mats, rnd, false);
                Hold(holds, new Vector3(cx + 0.32f + (float)rnd.NextDouble() * 0.12f, y + 0.12f, 0), mats, rnd, false);
                if (rnd.NextDouble() < 0.35) Hold(holds, new Vector3(cx + ((float)rnd.NextDouble() - 0.5f) * 0.4f, y + 0.3f, 0), mats, rnd, false);
            }

            // Kamp 1
            var ledgeY = 15f;
            Ledge(level, "Ledge_Camp1", new Vector3(cx, ledgeY, 0), 4.5f, mats);
            Campfire(level, new Vector3(cx + 1.2f, ledgeY + 0.2f, 0), mats);
            var cp = new GameObject("Checkpoint_Camp1");
            cp.transform.SetParent(level, false);
            cp.transform.position = new Vector3(cx, ledgeY + 1.2f, 0);
            var cpBox = cp.AddComponent<BoxCollider>(); cpBox.isTrigger = true; cpBox.size = new Vector3(4.5f, 2f, 2f);
            var cpComp = cp.AddComponent<Checkpoint>();
            var rp = new GameObject("RespawnPoint"); rp.transform.SetParent(cp.transform, false);
            rp.transform.position = new Vector3(cx - 0.8f, ledgeY + 1.25f, 0);
            cpComp.respawnPoint = rp.transform;
            // Kamp çıkıntısının kenarlarına tutamak (çıkıntıya tırmanabilmek için)
            Hold(holds, new Vector3(cx - 2.5f, ledgeY - 0.1f, 0), mats, rnd, false);
            Hold(holds, new Vector3(cx + 2.5f, ledgeY - 0.1f, 0), mats, rnd, false);
            Hold(holds, new Vector3(cx - 2.6f, ledgeY - 0.65f, 0), mats, rnd, false);
            Hold(holds, new Vector3(cx + 2.6f, ledgeY - 0.65f, 0), mats, rnd, false);

            // Bölüm 2: y 16 → 26 (kaygan yosun karışık)
            for (float y = ledgeY + 1.6f; y < 26.5f; y += 0.48f + (float)rnd.NextDouble() * 0.12f)
            {
                cx = Mathf.Clamp(cx + ((float)rnd.NextDouble() - 0.5f) * 1.1f, -3f, 3f);
                Hold(holds, new Vector3(cx - 0.5f - (float)rnd.NextDouble() * 0.2f, y, 0), mats, rnd, rnd.NextDouble() < 0.35);
                Hold(holds, new Vector3(cx + 0.5f + (float)rnd.NextDouble() * 0.2f, y + 0.15f, 0), mats, rnd, rnd.NextDouble() < 0.35);
                if (rnd.NextDouble() < 0.25) Hold(holds, new Vector3(cx, y + 0.32f, 0), mats, rnd, false);
            }

            // Zirve
            float topY = 27.5f;
            Ledge(level, "Ledge_Summit", new Vector3(cx, topY, 0), 5f, mats);
            Hold(holds, new Vector3(cx - 2.7f, topY - 0.1f, 0), mats, rnd, false);
            Hold(holds, new Vector3(cx + 2.7f, topY - 0.1f, 0), mats, rnd, false);
            Hold(holds, new Vector3(cx - 2.8f, topY - 0.6f, 0), mats, rnd, false);
            Hold(holds, new Vector3(cx + 2.8f, topY - 0.6f, 0), mats, rnd, false);
            Prim(PrimitiveType.Cylinder, "FlagPole", level, new Vector3(cx + 1.5f, topY + 1.2f, 0.3f), new Vector3(0.06f, 1.0f, 0.06f), mats.wood, false);
            Prim(PrimitiveType.Cube, "Flag", level, new Vector3(cx + 1.95f, topY + 1.9f, 0.3f), new Vector3(0.8f, 0.5f, 0.04f), mats.flag, false);
            var summit = new GameObject("Summit");
            summit.transform.SetParent(level, false);
            summit.transform.position = new Vector3(cx, topY + 1.2f, 0);
            var sBox = summit.AddComponent<BoxCollider>(); sBox.isTrigger = true; sBox.size = new Vector3(5f, 2f, 2f);
            summit.AddComponent<SummitTrigger>();

            // ---------- HUD ----------
            var hudGo = new GameObject("GameHUD");
            var doc = hudGo.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UIDir + "/GameHUD.uxml");
            hudGo.AddComponent<GameHUD>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void SetBuildScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MainMenuPath, true),
                new EditorBuildSettingsScene(GamePath, true),
            };
        }

        static void WriteSteamAppId()
        {
            var path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "steam_appid.txt");
            if (!File.Exists(path)) File.WriteAllText(path, "480");
        }
    }
}
