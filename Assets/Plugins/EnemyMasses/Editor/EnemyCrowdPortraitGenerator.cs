using UnityEngine;
using UnityEditor;
using System.IO;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Editor utility that auto-generates portrait textures from EnemyCrowd prefabs.
    /// Humanoid models: captures head, neck, and upper shoulders.
    /// Non-humanoid models: captures the full object.
    /// Portraits are saved as PNG assets next to the EnemyCrowd ScriptableObject.
    /// </summary>
    public static class EnemyCrowdPortraitGenerator
    {
        /// <summary>
        /// Resolution of the generated portrait texture (square).
        /// </summary>
        public const int PortraitSize = 256;

        /// <summary>
        /// Background color for the portrait (transparent).
        /// </summary>
        private static readonly Color BackgroundColor = new Color(0f, 0f, 0f, 0f);

        /// <summary>
        /// Generates a portrait texture from the given prefab and saves it as a PNG asset.
        /// Returns the saved Texture2D asset, or null on failure.
        /// </summary>
        /// <param name="prefab">The prefab to photograph.</param>
        /// <param name="enemyCrowdAsset">The EnemyCrowd SO — used to derive a save path.</param>
        public static Texture2D GeneratePortrait(GameObject prefab, Object enemyCrowdAsset)
        {
            return GeneratePortraitInternal(prefab, enemyCrowdAsset, null);
        }

        /// <summary>
        /// Generates a portrait from the given prefab and saves it as a PNG asset at a
        /// caller-specified directory.  Used by non-SO workflows (e.g. StandaloneSkillCaster on a prefab).
        /// </summary>
        /// <param name="prefab">The prefab to photograph.</param>
        /// <param name="saveDirectory">Asset-relative directory to save the portrait PNG (e.g. "Assets/Portraits").</param>
        /// <param name="assetName">Base file name without extension (e.g. "MyUnit_Portrait").</param>
        public static Texture2D GeneratePortrait(GameObject prefab, string saveDirectory, string assetName)
        {
            return GeneratePortraitInternal(prefab, null, saveDirectory, assetName);
        }

        private static Texture2D GeneratePortraitInternal(
            GameObject prefab, Object ownerAsset,
            string explicitDirectory = null, string explicitName = null)
        {
            if (prefab == null)
            {
                Debug.LogWarning("[PortraitGenerator] No prefab assigned — cannot generate portrait.");
                return null;
            }

            // Instantiate off-screen for rendering
            GameObject instance = Object.Instantiate(prefab, Vector3.up * 10000f, Quaternion.identity);
            instance.hideFlags = HideFlags.HideAndDontSave;

            // Disable any components that might interfere (agents, AI, etc.)
            DisableRuntimeComponents(instance);

            try
            {
                // Detect humanoid status and find head
                Animator animator = instance.GetComponentInChildren<Animator>();
                bool isHumanoid = animator != null && animator.avatar != null && animator.avatar.isHuman;

                // Find head focal point and compute framing
                HeadFrameInfo frameInfo = FindHeadFrame(instance, animator, isHumanoid);

                // Render with head-priority framing
                Texture2D portrait = RenderPortrait(instance, frameInfo);

                // Save as asset
                if (portrait != null)
                {
                    string savedPath = null;
                    if (!string.IsNullOrEmpty(explicitDirectory) && !string.IsNullOrEmpty(explicitName))
                        savedPath = SavePortraitAssetTo(portrait, explicitDirectory, explicitName);
                    else if (ownerAsset != null)
                        savedPath = SavePortraitAsset(portrait, ownerAsset);

                    if (!string.IsNullOrEmpty(savedPath))
                    {
                        // Load the saved asset so we return a persistent reference
                        var importer = AssetImporter.GetAtPath(savedPath) as TextureImporter;
                        if (importer != null)
                        {
                            importer.textureType = TextureImporterType.Sprite;
                            importer.alphaIsTransparency = true;
                            importer.npotScale = TextureImporterNPOTScale.None;
                            importer.mipmapEnabled = false;
                            importer.SaveAndReimport();
                        }

                        Texture2D savedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(savedPath);
                        if (savedTexture != null)
                        {
                            Object.DestroyImmediate(portrait);
                            return savedTexture;
                        }
                    }
                }

                return portrait;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Holds information about how to frame the head for a portrait shot.
        /// </summary>
        private struct HeadFrameInfo
        {
            /// <summary>Point the camera should look at (center of the face/head).</summary>
            public Vector3 focalPoint;
            /// <summary>Radius of the head region to frame (controls zoom level).</summary>
            public float frameRadius;
            /// <summary>Direction the face is pointing (camera will be placed opposite).</summary>
            public Vector3 faceForward;
            /// <summary>World-up direction for the head (keeps camera level).</summary>
            public Vector3 headUp;
        }

        /// <summary>
        /// Finds the head and determines the best framing for a face-priority portrait.
        /// Works for humanoid rigs, non-humanoid rigs with a "head" bone, and plain meshes.
        /// </summary>
        private static HeadFrameInfo FindHeadFrame(GameObject root, Animator animator, bool isHumanoid)
        {
            Transform headBone = null;
            Transform neckBone = null;

            // --- 1. Try humanoid bones ---
            if (isHumanoid && animator != null)
            {
                headBone = animator.GetBoneTransform(HumanBodyBones.Head);
                neckBone = animator.GetBoneTransform(HumanBodyBones.Neck);
            }

            // --- 2. Fallback: search hierarchy for a transform named "head" (case-insensitive) ---
            if (headBone == null)
            {
                headBone = FindTransformByName(root.transform, "head");
                if (headBone != null && neckBone == null)
                    neckBone = FindTransformByName(root.transform, "neck");
            }

            // --- 3. Compute full mesh bounds (needed for fallback and for head-size estimation) ---
            Bounds fullBounds = ComputeRendererBounds(root);

            if (headBone != null)
            {
                return BuildHeadFrame(headBone, neckBone, root, fullBounds);
            }

            // --- 4. No head bone found: frame the upper portion of the model ---
            return BuildFallbackFrame(root, fullBounds);
        }

        /// <summary>
        /// Builds a HeadFrameInfo centered on a known head bone.
        /// Uses only bone hierarchy (not renderer bounds, which are typically full-body).
        /// </summary>
        private static HeadFrameInfo BuildHeadFrame(Transform head, Transform neck, GameObject root, Bounds fullBounds)
        {
            // Estimate head size from neck-to-head distance, or fraction of total height
            float neckToHead;
            if (neck != null)
            {
                neckToHead = Vector3.Distance(head.position, neck.position);
            }
            else
            {
                neckToHead = fullBounds.size.y * 0.12f; // ~12% of total height as a guess
            }
            neckToHead = Mathf.Max(neckToHead, 0.05f);

            // Scan all child bones under the head to capture extremities (ears, horns, jaw, etc.)
            Bounds headBoneBounds = new Bounds(head.position, Vector3.zero);
            EncapsulateChildBones(head, ref headBoneBounds);

            // --- Find eye bones for accurate face centering ---
            // Try humanoid eye bones first, then search by name
            Transform leftEye = null;
            Transform rightEye = null;
            Animator animator = root.GetComponentInChildren<Animator>();
            if (animator != null && animator.avatar != null && animator.avatar.isHuman)
            {
                leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
                rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            }
            if (leftEye == null)
                leftEye = FindTransformByName(head, "eye.l") ?? FindTransformByName(head, "lefteye") ?? FindTransformByName(head, "eye_l");
            if (rightEye == null)
                rightEye = FindTransformByName(head, "eye.r") ?? FindTransformByName(head, "righteye") ?? FindTransformByName(head, "eye_r");
            // Generic fallback: any child with "eye" in the name
            if (leftEye == null && rightEye == null)
            {
                Transform anyEye = FindTransformByName(head, "eye");
                if (anyEye != null) leftEye = anyEye;
            }

            // --- Determine the face center (focal point) ---
            Vector3 focalPoint;
            if (leftEye != null && rightEye != null)
            {
                // Average of both eyes — this is the true face center
                Vector3 eyeCenter = (leftEye.position + rightEye.position) * 0.5f;
                focalPoint = eyeCenter;
            }
            else if (leftEye != null || rightEye != null)
            {
                // One eye found — use it, slightly toward head center
                Transform eye = leftEye ?? rightEye;
                focalPoint = Vector3.Lerp(eye.position, head.position, 0.2f);
                focalPoint.y = eye.position.y; // keep eye height
            }
            else
            {
                // No eye bones — the head bone is at the skull base, estimate face center
                // at roughly 70% up from head bone to the top of the head bone bounds
                float headTop = headBoneBounds.max.y;
                float eyeHeight = Mathf.Lerp(head.position.y, headTop, 0.55f);
                focalPoint = new Vector3(head.position.x, eyeHeight, head.position.z);
            }

            // Frame radius: use the larger of bone-derived extent and the neckToHead estimate.
            // The full head is roughly 1.5x the neckToHead distance in all directions.
            float boneExtent = Mathf.Max(
                headBoneBounds.extents.x,
                headBoneBounds.extents.y,
                headBoneBounds.extents.z);

            float frameRadius = Mathf.Max(boneExtent, neckToHead * 1.3f);
            frameRadius *= 2.2f; // Zoom out to show full head with neck/chin visible

            // Determine face forward direction:
            // Prefer the root transform's forward (most models are authored facing +Z or -Z).
            Vector3 modelForward = root.transform.forward;

            // Heuristic: if the head bone's local Z axis diverges significantly from the root
            // forward (e.g. head looking down in T-pose), prefer root forward.
            float alignment = Vector3.Dot(head.forward, modelForward);
            Vector3 faceForward = Mathf.Abs(alignment) > 0.3f ? head.forward : modelForward;

            return new HeadFrameInfo
            {
                focalPoint = focalPoint,
                frameRadius = frameRadius,
                faceForward = faceForward.normalized,
                headUp = Vector3.up
            };
        }

        /// <summary>
        /// Recursively encapsulates all child bone positions into the given bounds.
        /// </summary>
        private static void EncapsulateChildBones(Transform bone, ref Bounds bounds)
        {
            foreach (Transform child in bone)
            {
                bounds.Encapsulate(child.position);
                EncapsulateChildBones(child, ref bounds);
            }
        }

        /// <summary>
        /// Fallback frame when no head bone is found — focuses on the upper 30% of the model.
        /// </summary>
        private static HeadFrameInfo BuildFallbackFrame(GameObject root, Bounds fullBounds)
        {
            // Focus on the top ~30% of the model
            float topY = fullBounds.max.y;
            float focusY = fullBounds.min.y + fullBounds.size.y * 0.8f; // 80% up
            Vector3 focalPoint = new Vector3(fullBounds.center.x, focusY, fullBounds.center.z);
            float frameRadius = (topY - focusY) * 1.3f + fullBounds.size.x * 0.35f;
            frameRadius = Mathf.Max(frameRadius, fullBounds.size.y * 0.2f);

            return new HeadFrameInfo
            {
                focalPoint = focalPoint,
                frameRadius = frameRadius,
                faceForward = root.transform.forward,
                headUp = Vector3.up
            };
        }

        /// <summary>
        /// Computes bounds for all renderers on the object.
        /// </summary>
        private static Bounds ComputeRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.one);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        /// <summary>
        /// Recursively searches for a transform whose name contains the given keyword (case-insensitive).
        /// Returns the first match.
        /// </summary>
        private static Transform FindTransformByName(Transform root, string keyword)
        {
            string lower = keyword.ToLowerInvariant();

            // Breadth-first search to find the most prominent "head"
            var queue = new System.Collections.Generic.Queue<Transform>();
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                Transform current = queue.Dequeue();
                if (current.name.ToLowerInvariant().Contains(lower))
                    return current;

                foreach (Transform child in current)
                    queue.Enqueue(child);
            }

            return null;
        }

        /// <summary>
        /// Renders the instance using an off-screen camera framed on the head.
        /// </summary>
        private static Texture2D RenderPortrait(GameObject instance, HeadFrameInfo frameInfo)
        {
            // Create render texture
            RenderTexture rt = new RenderTexture(PortraitSize, PortraitSize, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            rt.Create();

            // Create temporary camera
            GameObject camGO = new GameObject("PortraitCamera");
            camGO.hideFlags = HideFlags.HideAndDontSave;
            Camera cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BackgroundColor;
            cam.targetTexture = rt;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 100f;
            cam.orthographic = false;
            cam.fieldOfView = 30f;

            // Create temporary directional light for consistent lighting
            GameObject lightGO = new GameObject("PortraitLight");
            lightGO.hideFlags = HideFlags.HideAndDontSave;
            Light light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.97f, 0.92f); // Warm white
            light.intensity = 1.2f;

            // Create fill light from below-left
            GameObject fillLightGO = new GameObject("PortraitFillLight");
            fillLightGO.hideFlags = HideFlags.HideAndDontSave;
            Light fillLight = fillLightGO.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.color = new Color(0.6f, 0.7f, 0.9f); // Cool blue fill
            fillLight.intensity = 0.5f;

            try
            {
                // Frame the camera on the head/face using the frameInfo
                Vector3 focalPoint = frameInfo.focalPoint;
                float frameRadius = frameInfo.frameRadius;

                // Distance so the head fills most of the frame
                float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
                float distance = (frameRadius) / Mathf.Tan(halfFov);
                distance = Mathf.Max(distance, 0.3f); // Minimum distance to avoid clipping

                // Place camera in front of the face, looking at it
                Vector3 camPosition = focalPoint + frameInfo.faceForward * distance;
                cam.transform.position = camPosition;
                cam.transform.LookAt(focalPoint, frameInfo.headUp);

                // Position lights relative to camera — key light from above-right, fill from left
                Vector3 camRight = cam.transform.right;
                Vector3 camUp = cam.transform.up;
                lightGO.transform.position = camPosition + camRight * distance * 0.5f + camUp * distance * 0.8f;
                lightGO.transform.LookAt(focalPoint);
                fillLightGO.transform.position = camPosition - camRight * distance * 0.7f - camUp * distance * 0.2f;
                fillLightGO.transform.LookAt(focalPoint);

                // Set all instance renderers to a visible layer (use default)
                SetLayerRecursive(instance, 0);

                // Render
                cam.Render();

                // Read pixels
                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(PortraitSize, PortraitSize, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, PortraitSize, PortraitSize), 0, 0);
                tex.Apply();
                RenderTexture.active = null;

                return tex;
            }
            finally
            {
                Object.DestroyImmediate(camGO);
                Object.DestroyImmediate(lightGO);
                Object.DestroyImmediate(fillLightGO);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        /// <summary>
        /// Saves the portrait texture as a PNG asset next to the EnemyCrowd ScriptableObject.
        /// Returns the asset path, or null on failure.
        /// </summary>
        private static string SavePortraitAsset(Texture2D portrait, Object enemyCrowdAsset)
        {
            string soPath = AssetDatabase.GetAssetPath(enemyCrowdAsset);
            if (string.IsNullOrEmpty(soPath))
            {
                Debug.LogWarning("[PortraitGenerator] Cannot determine save path — EnemyCrowd asset has no path.");
                return null;
            }

            string directory = Path.GetDirectoryName(soPath);
            string soName = Path.GetFileNameWithoutExtension(soPath);

            return SavePortraitAssetTo(portrait, directory, soName);
        }

        /// <summary>
        /// Saves the portrait texture as a PNG asset in the specified directory
        /// with the given base name. Creates a "Portraits" subfolder automatically.
        /// Returns the asset path, or null on failure.
        /// </summary>
        internal static string SavePortraitAssetTo(Texture2D portrait, string directory, string baseName)
        {
            // Create a Portraits subfolder
            string portraitDir = Path.Combine(directory, "Portraits");
            if (!AssetDatabase.IsValidFolder(portraitDir))
            {
                AssetDatabase.CreateFolder(directory, "Portraits");
            }

            string fileName = $"{baseName}_Portrait.png";
            string savePath = Path.Combine(portraitDir, fileName).Replace("\\", "/");

            byte[] pngData = portrait.EncodeToPNG();
            File.WriteAllBytes(savePath, pngData);
            AssetDatabase.ImportAsset(savePath, ImportAssetOptions.ForceUpdate);

            Debug.Log($"[PortraitGenerator] Portrait saved to: {savePath}");
            return savePath;
        }

        /// <summary>
        /// Disables runtime components that might interfere with the static render.
        /// </summary>
        private static void DisableRuntimeComponents(GameObject instance)
        {
            // Disable common runtime components
            foreach (var agent in instance.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>())
                agent.enabled = false;

            foreach (var rb in instance.GetComponentsInChildren<Rigidbody>())
            {
                rb.isKinematic = true;
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector3.zero;
#else
                rb.velocity = Vector3.zero;
#endif
            }

            foreach (var col in instance.GetComponentsInChildren<Collider>())
                col.enabled = false;

            // Disable any MonoBehaviours that aren't renderers/animators
            foreach (var mb in instance.GetComponentsInChildren<MonoBehaviour>())
                mb.enabled = false;

            // Keep Animator enabled (needed for bone positions) but set speed to 0
            foreach (var anim in instance.GetComponentsInChildren<Animator>())
            {
                anim.enabled = true;
                anim.speed = 0f;
                // Force a T-pose update so bones are in a known position
                anim.Update(0f);
            }
        }

        /// <summary>
        /// Recursively sets the layer for all children.
        /// </summary>
        private static void SetLayerRecursive(GameObject obj, int layer)
        {
            obj.layer = layer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }
    }
}
