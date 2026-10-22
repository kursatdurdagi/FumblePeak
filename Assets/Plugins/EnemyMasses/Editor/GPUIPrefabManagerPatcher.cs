// Only compile patcher when GPUI Pro needs patching (version < 0.12.9)
#if EM_GPUIP_NEEDS_PATCH
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Patches GPU Instancer Pro's GPUIPrefabManager.cs and GPUICrowdRenderSourceGroup.cs 
    /// to fix ArgumentOutOfRangeException and IndexOutOfRangeException errors
    /// that occur during repeated spawn/destroy cycles, and to fix invisible agent issues.
    /// 
    /// The bugs:
    /// 1. GPUI doesn't validate buffer indices before operations in RemovePrefabInstance
    /// 2. AddPrefabInstanceImmediate uses GetMatrixLength() instead of instanceCount,
    ///    causing invisible agents when buffer is larger than instance count
    /// 3. Removal loop doesn't handle invalid/out-of-bounds indices gracefully
    /// 4. OnBufferIndexModified doesn't validate array bounds before NativeArray.Copy
    /// 5. New instances inherit stale animation data from previous buffer occupants
    ///    (causes wrong animations when agents become visible)
    /// 
    /// This patcher adds bounds checking and fixes the immediate add logic.
    /// Updated for GPU Instancer Pro v1.3.0+
    /// </summary>
    public static class GPUIPrefabManagerPatcher
    {
        private const string GPUI_PREFAB_MANAGER_PATH = "Packages/com.gurbu.gpui-pro.prefab/Runtime/Scripts/GPUIPrefabManager.cs";
        private const string GPUI_CROWD_RENDER_SOURCE_GROUP_PATH = "Packages/com.gurbu.gpui-pro.crowd-animations/Runtime/Scripts/Data/GPUICrowdRenderSourceGroup.cs";
        
        // Markers to detect if already patched
        private const string PATCH_MARKER_1 = "// [EnemyMasses Patch 1] Validate buffer index bounds";
        private const string PATCH_MARKER_2 = "// [EnemyMasses Patch 2] Skip invalid indices";
        private const string PATCH_MARKER_3 = "// [EnemyMasses Patch 3] Validate buffer indices before NativeArray copy";
        private const string PATCH_MARKER_4 = "// [EnemyMasses Patch 4] Use instanceCount instead of GetMatrixLength()";
        private const string PATCH_MARKER_5 = "// [EnemyMasses Patch 5] Clear stale animation data for new instances";
        private const string PATCH_MARKER_5_ENHANCED = "ResetBonesToBindPose"; // Check for enhanced version with bone reset

        /// <summary>
        /// Applies patches to GPU Instancer Pro. Called from EnemyMassesSetupWizard.
        /// </summary>
        public static void PatchGPUIPrefabManager()
        {
            string fullPath = Path.GetFullPath(GPUI_PREFAB_MANAGER_PATH);
            
            if (!File.Exists(fullPath))
            {
                EditorUtility.DisplayDialog(
                    "Enemy Masses - Patcher",
                    "Could not find GPUIPrefabManager.cs at:\n\n" + fullPath + "\n\n" +
                    "Please ensure GPU Instancer Pro is installed via Package Manager.",
                    "OK");
                return;
            }

            // Ask for user confirmation before applying patches
            if (!EditorUtility.DisplayDialog(
                "Enemy Masses - Apply GPUI Patches",
                "This will patch GPU Instancer Pro source files to fix:\n\n" +
                "• Buffer index validation errors\n" +
                "• IndexOutOfRangeException in removal loop\n" +
                "• Invisible agents (AddPrefabInstanceImmediate bug)\n" +
                "• NativeArray bounds validation\n\n" +
                "Backup files will be created before patching.\n\n" +
                "Do you want to continue?",
                "Apply Patches",
                "Cancel"))
            {
                return;
            }

            ApplyPatchesInternal(true);
        }
        
        /// <summary>
        /// Internal method to apply patches. Can be called without user confirmation.
        /// </summary>
        /// <param name="showResultDialog">Whether to show a result dialog after patching</param>
        /// <returns>True if patches were applied successfully</returns>
        public static bool ApplyPatchesInternal(bool showResultDialog = false)
        {
            string fullPath = Path.GetFullPath(GPUI_PREFAB_MANAGER_PATH);
            
            if (!File.Exists(fullPath))
            {
                Debug.LogWarning("[EnemyMasses Patcher] Could not find GPUIPrefabManager.cs");
                return false;
            }

            string content = File.ReadAllText(fullPath);

            // Create backup
            string backupPath = fullPath + ".backup";
            if (!File.Exists(backupPath))
            {
                File.Copy(fullPath, backupPath);
                Debug.Log($"[EnemyMasses Patcher] Created backup at: {backupPath}");
            }

            bool patch1Applied = false;
            bool patch2Applied = false;
            bool patch4Applied = false;
            string patchedContent = content;

            // ============================================================
            // PATCH 1: Fix RemovePrefabInstance to validate buffer index
            // ============================================================
            // Look for the pattern where bufferIndex is added to indexesToRemove without validation
            // Target pattern (GPUI v1.3.0+):
            //     prototypeData = _prototypeDataArray[prototypeIndex];
            //     Debug.Assert(prototypeData.IsInitialized, "Runtime data is not initialized!");
            //     Debug.Assert(gpuiPrefab.bufferIndex >= 0, "Buffer index is negative");
            //     prototypeData.indexesToRemove.Add(gpuiPrefab.bufferIndex);
            //     return true;
            
            if (patchedContent.Contains(PATCH_MARKER_1))
            {
                Debug.Log("[EnemyMasses Patcher] Patch 1 already applied.");
                patch1Applied = true;
            }
            else
            {
                string patch1Pattern = @"(prototypeData = _prototypeDataArray\[prototypeIndex\];[\s\r\n]+Debug\.Assert\(prototypeData\.IsInitialized, ""Runtime data is not initialized!""\);[\s\r\n]+)(Debug\.Assert\(gpuiPrefab\.bufferIndex >= 0, ""Buffer index is negative""\);[\s\r\n]+prototypeData\.indexesToRemove\.Add\(gpuiPrefab\.bufferIndex\);[\s\r\n]+return true;)";
                
                string patch1Replacement = @"$1" + PATCH_MARKER_1 + @"
            int bufferIndex = gpuiPrefab.bufferIndex;
            if (bufferIndex < 0 || bufferIndex >= prototypeData.instanceCount)
            {
                // Invalid buffer index - already removed or corrupted state
                // Clear the prefab's instancing data to prevent further issues
                ClearInstancingData(gpuiPrefab, false);
                return true;
            }
            
            prototypeData.indexesToRemove.Add(bufferIndex);
            return true;";

                if (Regex.IsMatch(patchedContent, patch1Pattern))
                {
                    patchedContent = Regex.Replace(patchedContent, patch1Pattern, patch1Replacement);
                    patch1Applied = true;
                    Debug.Log("[EnemyMasses Patcher] Applied Patch 1: Buffer index validation in RemovePrefabInstance");
                }
                else
                {
                    Debug.LogWarning("[EnemyMasses Patcher] Could not find Patch 1 target. The code may have changed.");
                }
            }

            // ============================================================
            // PATCH 2: Fix AddRemoveInstances to skip invalid indices
            // ============================================================
            // Target the foreach loop in the removal section
            // Original:
            //     foreach (int toRemoveIndex in prototypeData.indexesToRemove)
            //     {
            //         Transform toRemoveTransform = prototypeData.instanceTransforms[toRemoveIndex];
            
            if (patchedContent.Contains(PATCH_MARKER_2))
            {
                Debug.Log("[EnemyMasses Patcher] Patch 2 already applied.");
                patch2Applied = true;
            }
            else
            {
                string patch2Pattern = @"(foreach \(int toRemoveIndex in prototypeData\.indexesToRemove\)[\s\r\n]+\{[\s\r\n]+)(Transform toRemoveTransform = prototypeData\.instanceTransforms\[toRemoveIndex\];)";
                
                string patch2Replacement = @"$1" + PATCH_MARKER_2 + @"
                            // Skip invalid indices to prevent IndexOutOfRangeException
                            int instanceTransformsLength = prototypeData.instanceTransforms != null ? prototypeData.instanceTransforms.Length : 0;
                            if (toRemoveIndex < 0 || toRemoveIndex > lastIndex || toRemoveIndex >= instanceTransformsLength)
                                continue;
                                
                            $2";

                if (Regex.IsMatch(patchedContent, patch2Pattern))
                {
                    patchedContent = Regex.Replace(patchedContent, patch2Pattern, patch2Replacement);
                    patch2Applied = true;
                    Debug.Log("[EnemyMasses Patcher] Applied Patch 2: Bounds checking in AddRemoveInstances loop");
                }
                else
                {
                    Debug.LogWarning("[EnemyMasses Patcher] Could not find Patch 2 target. The code may have changed.");
                }
            }

            // ============================================================
            // PATCH 4: Fix AddPrefabInstanceImmediate to use instanceCount instead of GetMatrixLength()
            // ============================================================
            // BUG: The original code uses GetMatrixLength() which returns the buffer size (with padding).
            // This causes immediate adds to place instances at wrong indices when buffer > instanceCount.
            // Original:
            //     GPUIPrefabPrototypeData prefabPrototypeData = _prototypeDataArray[prototypeIndex];
            //     int bufferIndex = prefabPrototypeData.GetMatrixLength();
            // Fixed:
            //     int bufferIndex = prefabPrototypeData.instanceCount;
            
            if (patchedContent.Contains(PATCH_MARKER_4))
            {
                Debug.Log("[EnemyMasses Patcher] Patch 4 already applied.");
                patch4Applied = true;
            }
            else
            {
                string patch4Pattern = @"(GPUIPrefabPrototypeData prefabPrototypeData = _prototypeDataArray\[prototypeIndex\];[\s\r\n]+)(int bufferIndex = prefabPrototypeData\.GetMatrixLength\(\);)";
                
                string patch4Replacement = @"$1" + PATCH_MARKER_4 + @"
            // Bug fix: GetMatrixLength() returns buffer size with padding, causing wrong indices
            int bufferIndex = prefabPrototypeData.instanceCount;";

                if (Regex.IsMatch(patchedContent, patch4Pattern))
                {
                    patchedContent = Regex.Replace(patchedContent, patch4Pattern, patch4Replacement);
                    patch4Applied = true;
                    Debug.Log("[EnemyMasses Patcher] Applied Patch 4: Fixed AddPrefabInstanceImmediate buffer index");
                }
                else
                {
                    Debug.LogWarning("[EnemyMasses Patcher] Could not find Patch 4 target. The code may have changed.");
                }
            }

            // Write patched content for GPUIPrefabManager
            if (patch1Applied || patch2Applied || patch4Applied)
            {
                File.WriteAllText(fullPath, patchedContent);
                Debug.Log("[EnemyMasses Patcher] GPUIPrefabManager.cs patching complete!");
            }

            // ============================================================
            // PATCH 3 & 5: Fix GPUICrowdRenderSourceGroup.OnBufferIndexModified
            // ============================================================
            (bool patch3Applied, bool patch5Applied) = PatchCrowdRenderSourceGroup();

            AssetDatabase.Refresh();

            // Show results if requested
            if (showResultDialog)
            {
                string message = "GPU Instancer Pro patching complete!\n\n";
                message += "GPUIPrefabManager.cs:\n";
                message += patch1Applied ? "  ✓ Patch 1: Buffer index validation in RemovePrefabInstance\n" : "  ✗ Patch 1: Could not apply (code may have changed)\n";
                message += patch2Applied ? "  ✓ Patch 2: Bounds checking in removal loop\n" : "  ✗ Patch 2: Could not apply (code may have changed)\n";
                message += patch4Applied ? "  ✓ Patch 4: Fixed AddPrefabInstanceImmediate buffer index\n" : "  ✗ Patch 4: Could not apply (code may have changed)\n";
                
                message += "\nGPUICrowdRenderSourceGroup.cs:\n";
                message += patch3Applied ? "  ✓ Patch 3: NativeArray bounds validation\n" : "  ✗ Patch 3: Could not apply or already patched\n";
                message += patch5Applied ? "  ✓ Patch 5: Clear stale animation data for new instances\n" : "  ✗ Patch 5: Could not apply or already patched\n";
                
                message += "\nBackups were created with .backup extension.";

                EditorUtility.DisplayDialog("Enemy Masses - Patcher", message, "OK");
            }

            bool success = patch1Applied && patch2Applied && patch3Applied && patch4Applied && patch5Applied;
            if (!patch1Applied && !patch2Applied && !patch3Applied && !patch4Applied && !patch5Applied)
            {
                Debug.LogWarning("[EnemyMasses Patcher] No patches could be applied. Files may already be patched or code structure has changed.");
            }
            
            return success;
        }

        private static (bool patch3Applied, bool patch5Applied) PatchCrowdRenderSourceGroup()
        {
            string fullPath = Path.GetFullPath(GPUI_CROWD_RENDER_SOURCE_GROUP_PATH);
            
            if (!File.Exists(fullPath))
            {
                Debug.LogWarning($"[EnemyMasses Patcher] Could not find GPUICrowdRenderSourceGroup.cs at: {fullPath}");
                return (false, false);
            }

            string content = File.ReadAllText(fullPath);
            bool patch3AlreadyApplied = content.Contains(PATCH_MARKER_3);
            bool patch5AlreadyApplied = content.Contains(PATCH_MARKER_5);

            // Check if all patches already applied
            if (patch3AlreadyApplied && patch5AlreadyApplied)
            {
                Debug.Log("[EnemyMasses Patcher] GPUICrowdRenderSourceGroup.cs is already fully patched.");
                return (true, true);
            }

            // Create backup
            string backupPath = fullPath + ".backup";
            if (!File.Exists(backupPath))
            {
                File.Copy(fullPath, backupPath);
                Debug.Log($"[EnemyMasses Patcher] Created backup at: {backupPath}");
            }

            string patchedContent = content;
            bool patch3Success = patch3AlreadyApplied;
            bool patch5Success = patch5AlreadyApplied;

            // ============================================================
            // PATCH 3: Validate buffer indices in RSG-level OnBufferIndexModified
            // ============================================================
            if (!patch3AlreadyApplied)
            {
                // The original OnBufferIndexModified method (GPUI v1.3.0+):
                // public unsafe void OnBufferIndexModified(int previousBufferIndex, int newBufferIndex)
                // {
                //     if (_animatorClipDataArray.IsCreated)
                //     {
                //         NativeArray<GPUICrowdAnimatorClipData>.Copy(_animatorClipDataArray, previousBufferIndex * GPUICrowdConstants.ANIMATOR_MAX_CLIPS, ...
                
                string patch3Pattern = @"(public unsafe void OnBufferIndexModified\(int previousBufferIndex, int newBufferIndex\)[\s\r\n]+\{[\s\r\n]+)(if \(_animatorClipDataArray\.IsCreated\))";
                
                string patch3Replacement = @"$1" + PATCH_MARKER_3 + @"
            // Validate indices before NativeArray operations to prevent ArgumentOutOfRangeException
            int maxClipIndex = _animatorClipDataArray.IsCreated ? _animatorClipDataArray.Length / GPUICrowdConstants.ANIMATOR_MAX_CLIPS : 0;
            
            if (previousBufferIndex < 0 || previousBufferIndex >= maxClipIndex || 
                newBufferIndex < 0 || newBufferIndex >= maxClipIndex)
            {
                // Invalid buffer indices - skip the copy operation to prevent crash
                return;
            }
            
            $2";

                if (Regex.IsMatch(patchedContent, patch3Pattern))
                {
                    patchedContent = Regex.Replace(patchedContent, patch3Pattern, patch3Replacement);
                    patch3Success = true;
                    Debug.Log("[EnemyMasses Patcher] Applied Patch 3: NativeArray bounds validation in OnBufferIndexModified");
                }
                else
                {
                    Debug.LogWarning("[EnemyMasses Patcher] Could not find Patch 3 target in GPUICrowdRenderSourceGroup.cs. The code may have changed.");
                }
            }

            // ============================================================
            // PATCH 5: Clear stale animation data for NEW instances
            // When previousBufferIndex is -1 (new instance), clear the animation data
            // at the new buffer slot to prevent showing stale animations from previous occupants
            // ============================================================
            if (!patch5AlreadyApplied)
            {
                // Target: GPUICrowdRenderSource.OnBufferIndexModified (the instance-level one)
                // Original code:
                //     public void OnBufferIndexModified(GPUICrowdInstance crowdInstance, int previousBufferIndex, int newBufferIndex)
                //     {
                //         if (previousBufferIndex >= 0 && newBufferIndex >= 0)
                //             crowdRSG.OnBufferIndexModified(...)
                //         if (previousBufferIndex >= 0)
                //             _boneRWData.ClearReferences(previousBufferIndex);
                // 
                // We need to add: when previousBufferIndex < 0 AND newBufferIndex >= 0, clear the clip data

                string patch5Pattern = @"(public void OnBufferIndexModified\(GPUICrowdInstance crowdInstance, int previousBufferIndex, int newBufferIndex\)[\s\r\n]+\{[\s\r\n]+)(if \(previousBufferIndex >= 0 && newBufferIndex >= 0\))";

                string patch5Replacement = @"$1" + PATCH_MARKER_5 + @"
            // When a NEW instance gets a buffer slot (previousBufferIndex = -1),
            // clear the animation clip data to prevent stale animations from showing
            if (previousBufferIndex < 0 && newBufferIndex >= 0)
            {
                crowdRSG.ClearAnimationClipData(renderSource.bufferStartIndex + newBufferIndex);
            }
            
            $2";

                if (Regex.IsMatch(patchedContent, patch5Pattern))
                {
                    patchedContent = Regex.Replace(patchedContent, patch5Pattern, patch5Replacement);
                    
                    // Also need to add the ClearAnimationClipData method to GPUICrowdRenderSourceGroup
                    // Find a good place to add it - after OnBufferIndexModified
                    string methodPattern = @"(public unsafe void OnBufferIndexModified\(int previousBufferIndex, int newBufferIndex\)[\s\S]*?\n        \})(\s*\n\s*#region Transition Methods)";
                    
                    string clearMethodAddition = @"$1

        /// <summary>
        /// Clears animation clip data at the specified buffer index to prevent stale animation data.
        /// Called when a new instance is assigned a buffer slot that may contain old animation data.
        /// Also resets the bone transforms to bind pose to prevent showing old animation poses.
        /// </summary>
        public unsafe void ClearAnimationClipData(int rsgBufferIndex)
        {
            if (!_animatorClipDataArray.IsCreated || rsgBufferIndex < 0)
                return;
                
            int maxIndex = _animatorClipDataArray.Length / GPUICrowdConstants.ANIMATOR_MAX_CLIPS;
            if (rsgBufferIndex >= maxIndex)
                return;
                
            // Clear all clip slots for this instance (default struct has IsValid = false)
            int startIdx = rsgBufferIndex * GPUICrowdConstants.ANIMATOR_MAX_CLIPS;
            for (int i = 0; i < GPUICrowdConstants.ANIMATOR_MAX_CLIPS; i++)
            {
                UnsafeUtility.WriteArrayElementWithStride(p_animatorClipDataArray, startIdx + i, GPUICrowdAnimatorClipData.STRIDE, default(GPUICrowdAnimatorClipData));
            }
            _requireSetClipDataBuffer = true;
            
            // Also clear frames and weights if they exist
            if (_clipFramesAndWeightsArray.IsCreated && rsgBufferIndex * 2 + 1 < _clipFramesAndWeightsArray.Length)
            {
                _clipFramesAndWeightsArray[rsgBufferIndex * 2] = Vector4.zero;
                _clipFramesAndWeightsArray[rsgBufferIndex * 2 + 1] = Vector4.zero;
                _requireSetFramesAndWeightsBuffer = true;
            }
            
            // Reset the bone transforms to bind pose to prevent showing old animation poses
            // This is critical because the animation shader skips bone updates when clip weights are zero,
            // so without this, the old bone poses from previous agents would remain visible
            ResetBonesToBindPose(rsgBufferIndex);
        }
        
        /// <summary>
        /// Resets the bone transforms for a single instance to bind pose using a compute shader.
        /// </summary>
        private void ResetBonesToBindPose(int rsgBufferIndex)
        {
            if (boneDataBuffer == null || rig == null || rig.bindPoseDataList == null || rig.bindPoseDataList.Count == 0)
                return;
                
            int boneCount = rig.bones.Count;
            if (boneCount == 0)
                return;
                
            // Get bind pose transforms
            GPUITransformData[] bindPoseTransforms = rig.bindPoseDataList[0].GetInverseTransformDataArray();
            if (bindPoseTransforms == null || bindPoseTransforms.Length != boneCount)
                return;
                
            // Create temporary buffer for bind pose data
            GraphicsBuffer sourceBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, boneCount, GPUITransformData.STRIDE);
            sourceBuffer.SetData(bindPoseTransforms);
            
            // Use compute shader to copy bind pose to this instance's bones
            ComputeShader cs = GPUICrowdConstants.CS_BoneBufferUtility;
            int kernelIndex = 2; // CopyBoneData kernel
            int startIndex = rsgBufferIndex * boneCount;
            
            cs.SetBuffer(kernelIndex, GPUICrowdConstants.PROP_boneDataBuffer, boneDataBuffer);
            cs.SetBuffer(kernelIndex, GPUIConstants.PROP_sourceBuffer, sourceBuffer);
            cs.SetInt(GPUIConstants.PROP_startIndex, startIndex);
            cs.SetInt(GPUIConstants.PROP_count, boneCount);
            cs.SetInt(GPUICrowdConstants.PROP_boneCount, boneCount);
            cs.DispatchX(kernelIndex, boneCount);
            
            sourceBuffer.Dispose();
        }
$2";

                    if (Regex.IsMatch(patchedContent, methodPattern))
                    {
                        patchedContent = Regex.Replace(patchedContent, methodPattern, clearMethodAddition);
                        patch5Success = true;
                        Debug.Log("[EnemyMasses Patcher] Applied Patch 5: Clear stale animation data for new instances");
                    }
                    else
                    {
                        Debug.LogWarning("[EnemyMasses Patcher] Could not add ClearAnimationClipData method. Patch 5 partially applied.");
                    }
                }
                else
                {
                    Debug.LogWarning("[EnemyMasses Patcher] Could not find Patch 5 target in GPUICrowdRenderSourceGroup.cs. The code may have changed.");
                }
            }

            if (patch3Success || patch5Success)
            {
                File.WriteAllText(fullPath, patchedContent);
                Debug.Log("[EnemyMasses Patcher] GPUICrowdRenderSourceGroup.cs patching complete!");
            }

            return (patch3Success, patch5Success);
        }

        /// <summary>
        /// Validates if GPUIPrefabManager patching is available.
        /// </summary>
        public static bool ValidatePatchGPUIPrefabManager()
        {
            // Only enable if the file exists
            return File.Exists(Path.GetFullPath(GPUI_PREFAB_MANAGER_PATH));
        }

        /// <summary>
        /// Restores original GPUI files from backup.
        /// </summary>
        public static void RestoreOriginal()
        {
            string prefabManagerPath = Path.GetFullPath(GPUI_PREFAB_MANAGER_PATH);
            string prefabManagerBackup = prefabManagerPath + ".backup";
            string crowdSourcePath = Path.GetFullPath(GPUI_CROWD_RENDER_SOURCE_GROUP_PATH);
            string crowdSourceBackup = crowdSourcePath + ".backup";

            bool hasPrefabBackup = File.Exists(prefabManagerBackup);
            bool hasCrowdBackup = File.Exists(crowdSourceBackup);

            if (!hasPrefabBackup && !hasCrowdBackup)
            {
                EditorUtility.DisplayDialog(
                    "Enemy Masses - Patcher",
                    "No backup files found.\n\n" +
                    "The original files cannot be restored.",
                    "OK");
                return;
            }

            string message = "This will restore the original GPUI files from backups.\n\n";
            if (hasPrefabBackup) message += "- GPUIPrefabManager.cs\n";
            if (hasCrowdBackup) message += "- GPUICrowdRenderSourceGroup.cs\n";
            message += "\nThe Enemy Masses patches will be removed.\n\nContinue?";

            if (EditorUtility.DisplayDialog(
                "Enemy Masses - Patcher",
                message,
                "Restore Original",
                "Cancel"))
            {
                int restored = 0;
                if (hasPrefabBackup)
                {
                    File.Copy(prefabManagerBackup, prefabManagerPath, true);
                    restored++;
                }
                if (hasCrowdBackup)
                {
                    File.Copy(crowdSourceBackup, crowdSourcePath, true);
                    restored++;
                }
                
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog(
                    "Enemy Masses - Patcher",
                    $"Restored {restored} original file(s).\n\n" +
                    "You may need to re-apply the patches if you experience buffer index errors.",
                    "OK");
                Debug.Log($"[EnemyMasses Patcher] Restored {restored} original GPUI file(s) from backup");
            }
        }

        /// <summary>
        /// Validates if restore is available.
        /// </summary>
        public static bool ValidateRestoreOriginal()
        {
            string prefabManagerBackup = Path.GetFullPath(GPUI_PREFAB_MANAGER_PATH) + ".backup";
            string crowdSourceBackup = Path.GetFullPath(GPUI_CROWD_RENDER_SOURCE_GROUP_PATH) + ".backup";
            return File.Exists(prefabManagerBackup) || File.Exists(crowdSourceBackup);
        }

        /// <summary>
        /// Checks patch status. Called from EnemyMassesSetupWizard.
        /// </summary>
        public static void CheckPatchStatus()
        {
            string prefabManagerPath = Path.GetFullPath(GPUI_PREFAB_MANAGER_PATH);
            string crowdSourcePath = Path.GetFullPath(GPUI_CROWD_RENDER_SOURCE_GROUP_PATH);

            bool prefabManagerExists = File.Exists(prefabManagerPath);
            bool crowdSourceExists = File.Exists(crowdSourcePath);

            if (!prefabManagerExists && !crowdSourceExists)
            {
                EditorUtility.DisplayDialog(
                    "Enemy Masses - Patch Status",
                    "GPUI files not found.\n\n" +
                    "GPU Instancer Pro may not be installed.",
                    "OK");
                return;
            }

            string prefabContent = prefabManagerExists ? File.ReadAllText(prefabManagerPath) : "";
            string crowdContent = crowdSourceExists ? File.ReadAllText(crowdSourcePath) : "";
            
            bool hasPatch1 = prefabContent.Contains(PATCH_MARKER_1);
            bool hasPatch2 = prefabContent.Contains(PATCH_MARKER_2);
            bool hasPatch3 = crowdContent.Contains(PATCH_MARKER_3);
            bool hasPatch4 = prefabContent.Contains(PATCH_MARKER_4);
            bool hasPatch5 = crowdContent.Contains(PATCH_MARKER_5);
            bool hasPatch5Enhanced = crowdContent.Contains(PATCH_MARKER_5_ENHANCED);

            string status = "GPU Instancer Pro Patch Status:\n\n";
            
            status += "GPUIPrefabManager.cs:\n";
            if (prefabManagerExists)
            {
                status += hasPatch1 ? "  ✓ Patch 1: Buffer index validation\n" : "  ✗ Patch 1: Not applied\n";
                status += hasPatch2 ? "  ✓ Patch 2: Bounds checking in removal loop\n" : "  ✗ Patch 2: Not applied\n";
                status += hasPatch4 ? "  ✓ Patch 4: Fixed AddPrefabInstanceImmediate (invisible agents)\n" : "  ✗ Patch 4: Not applied\n";
            }
            else
            {
                status += "  ⚠ File not found\n";
            }
            
            status += "\nGPUICrowdRenderSourceGroup.cs:\n";
            if (crowdSourceExists)
            {
                status += hasPatch3 ? "  ✓ Patch 3: NativeArray bounds validation\n" : "  ✗ Patch 3: Not applied\n";
                if (hasPatch5Enhanced)
                    status += "  ✓ Patch 5: Clear stale animation data + bone reset (enhanced)\n";
                else if (hasPatch5)
                    status += "  ⚠ Patch 5: Animation data clear (needs update for bone reset)\n";
                else
                    status += "  ✗ Patch 5: Not applied\n";
            }
            else
            {
                status += "  ⚠ File not found\n";
            }

            bool fullyPatched = hasPatch1 && hasPatch2 && hasPatch3 && hasPatch4 && hasPatch5;
            bool partiallyPatched = hasPatch1 || hasPatch2 || hasPatch3 || hasPatch4 || hasPatch5;
            
            if (fullyPatched)
            {
                status += "\n✓ FULLY PATCHED - Buffer index errors and invisible agents should be fixed.";
            }
            else if (partiallyPatched)
            {
                status += "\n⚠ PARTIALLY PATCHED - Run the patcher again.";
            }
            else
            {
                status += "\n✗ NOT PATCHED - Run 'Tools > Enemy Masses > Fix GPUIPrefabManager'";
            }

            // Check for backups
            string prefabBackup = prefabManagerPath + ".backup";
            string crowdBackup = crowdSourcePath + ".backup";
            if (File.Exists(prefabBackup) || File.Exists(crowdBackup))
            {
                status += "\n\n📁 Backup file(s) exist.";
            }

            EditorUtility.DisplayDialog("Enemy Masses - Patch Status", status, "OK");
        }
    }
}
#endif
