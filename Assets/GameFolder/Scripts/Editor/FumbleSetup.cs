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
            BuildMainMenu(panel, player, climber, mats);
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
                float reach = ((b.position - a.position).magnitude + (c.position - b.position).magnitude) * (i < 2 ? 1.4f : 1.0f); // kollara oynanış için ekstra erişim (kolay tırmanış)
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
            vis.lineMaterial = FumbleLevelBuilder.LineMaterial;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/Climber.prefab");
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------
        static void ClearScene(Scene s)
        {
            foreach (var go in s.GetRootGameObjects()) Object.DestroyImmediate(go);
        }

        static void BuildMainMenu(PanelSettings panel, GameObject player, GameObject climber, Mats mats)
        {
            var scene = EditorSceneManager.OpenScene(MainMenuPath, OpenSceneMode.Single);
            ClearScene(scene);

            BuildMenuStage(mats);

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

        /// <summary>Ana menü 3D sahnesi: zirve, sarhoş dağcı, şişe, kamera ve sinematik.</summary>
        static void BuildMenuStage(Mats mats)
        {
            var stageRoot = new GameObject("MenuStage");
            var info = FumbleLevelBuilder.BuildMenuStage(stageRoot.transform);

            // --- Kamera ---
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 42f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 600f;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var idle = new GameObject("CamIdle").transform;
            idle.SetParent(stageRoot.transform, false);
            idle.position = new Vector3(-0.9f, 1.55f, -5.4f);
            idle.rotation = Quaternion.LookRotation(new Vector3(-0.2f, 1.05f, 0.6f) - idle.position);
            camGo.transform.SetPositionAndRotation(idle.position, idle.rotation);

            var perp = new Vector3(info.edgeDir.z, 0f, -info.edgeDir.x);
            var cine = new GameObject("CamCinematic").transform;
            cine.SetParent(stageRoot.transform, false);
            cine.position = info.edgePos + info.edgeDir * 0.9f + perp * 3.3f + Vector3.up * 0.7f;
            cine.rotation = Quaternion.LookRotation(info.edgePos + Vector3.up - cine.position);

            var edge = new GameObject("EdgePoint").transform;
            edge.SetParent(stageRoot.transform, false);
            edge.position = info.edgePos;
            edge.rotation = Quaternion.LookRotation(info.edgeDir);

            // --- Karakter ---
            var charRoot = new GameObject("DrunkClimber");
            charRoot.transform.SetParent(stageRoot.transform, false);
            charRoot.transform.SetPositionAndRotation(info.charPos, Quaternion.Euler(0, info.charYaw, 0));
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Visual";
            model.transform.SetParent(charRoot.transform, false);
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
            foreach (var a in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);

            var bottle = new GameObject("Bottle");
            bottle.transform.SetParent(stageRoot.transform, false);
            bottle.AddComponent<MeshFilter>().sharedMesh = info.bottleMesh;
            bottle.AddComponent<MeshRenderer>().sharedMaterial = info.worldMat;

            var rockHand = new GameObject("RockHandPoint").transform;
            rockHand.SetParent(stageRoot.transform, false);
            rockHand.position = info.rockHand;

            // Çiş: sarı parçacık akışı
            var peeGo = new GameObject("PeeStream");
            peeGo.transform.SetParent(stageRoot.transform, false);
            var ps = peeGo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 1.6f; main.startSpeed = 2.7f; main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.07f);
            main.startColor = new Color(1f, 0.92f, 0.15f, 1f);
            main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            var em = ps.emission; em.rateOverTime = 220f; em.enabled = false;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 2.5f; shape.radius = 0.005f;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.5f));
            peeGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = mats.steam;

            var mc = charRoot.AddComponent<MenuCharacter>();
            mc.model = model.transform;
            mc.bottle = bottle.transform;
            mc.rockHand = rockHand;
            mc.pee = ps;

            // Hafif kar
            var snowGo = new GameObject("Snowfall");
            snowGo.transform.SetParent(stageRoot.transform, false);
            snowGo.transform.position = new Vector3(0.5f, 6f, 0f);
            var snow = snowGo.AddComponent<ParticleSystem>();
            var sm = snow.main;
            sm.startLifetime = 9f; sm.startSpeed = 0.2f; sm.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            sm.startColor = new Color(1f, 1f, 1f, 0.85f); sm.gravityModifier = 0.03f; sm.maxParticles = 500;
            sm.simulationSpace = ParticleSystemSimulationSpace.World; sm.prewarm = true;
            var se = snow.emission; se.rateOverTime = 40f;
            var ss = snow.shape; ss.shapeType = ParticleSystemShapeType.Box; ss.scale = new Vector3(16f, 12f, 1f);
            snowGo.transform.rotation = Quaternion.Euler(90f, 0, 0);
            var sn = snow.noise; sn.enabled = true; sn.strength = 0.35f; sn.frequency = 0.25f;
            snowGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = mats.steam;

            var stage = stageRoot.AddComponent<MenuStage>();
            stage.character = mc;
            stage.cam = cam;
            stage.edgePoint = edge;
            stage.idleCamPose = idle;
            stage.cinematicCamPose = cine;
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

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(0, 1.6f, -8f);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 52f;
            cam.farClipPlane = 400f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<ClimberCamera>();
            var camData = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            AddAtmosphere(camGo.transform);

            new GameObject("DevBootstrap").AddComponent<DevBootstrap>();

            // ---------- Seviye: prosedürel dağ ----------
            var level = new GameObject("Level_Mountain").transform;
            FumbleLevelBuilder.Build(level, mats.marker);

            // ---------- HUD ----------
            var hudGo = new GameObject("GameHUD");
            var doc = hudGo.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UIDir + "/GameHUD.uxml");
            hudGo.AddComponent<GameHUD>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void AddAtmosphere(Transform cam)
        {
            var atmo = cam.gameObject.AddComponent<AltitudeAtmosphere>();
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat");
            ParticleSystem Make(string name, Color c, float size, float speed, float grav)
            {
                var go = new GameObject(name);
                go.transform.SetParent(cam, false);
                go.transform.localPosition = new Vector3(0, 6f, 7f);
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.startLifetime = 6f; main.startSpeed = speed; main.startSize = size;
                main.startColor = c; main.gravityModifier = grav; main.maxParticles = 400;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(18f, 1f, 8f);
                var noise = ps.noise; noise.enabled = true; noise.strength = 0.4f; noise.frequency = 0.3f;
                var em = ps.emission; em.rateOverTime = 0f;
                go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
                return ps;
            }
            atmo.snow = Make("Snow", new Color(1f, 1f, 1f, 0.9f), 0.07f, 0.3f, 0.08f);
            atmo.pollen = Make("Pollen", new Color(1f, 0.95f, 0.7f, 0.6f), 0.04f, 0.1f, -0.01f);
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
