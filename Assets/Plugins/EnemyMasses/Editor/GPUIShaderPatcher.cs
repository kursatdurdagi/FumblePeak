// Only compile patcher when GPUI Pro needs patching (version < 0.12.9)
#if EM_GPUIP_NEEDS_PATCH
using UnityEngine;
using UnityEditor;
using System.IO;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Patches GPU Instancer Pro's TreeProxy_GPUIPro.shader to fix WebGPU build errors.
    /// 
    /// The bug:
    /// Shader error in 'Hidden/GPUInstancerPro/Nature/TreeProxy': error: a vertex shader must include the 'position' builtin in its return type (on webgpu)
    /// 
    /// This patcher modifies the vertex shader to return a position with SV_POSITION semantic.
    /// </summary>
    public static class GPUIShaderPatcher
    {
        private const string GPUI_TREE_PROXY_SHADER_PATH = "Packages/com.gurbu.gpui-pro/Runtime/Shaders/TreeProxy_GPUIPro.shader";
        
        // Marker to detect if already patched
        private const string PATCH_MARKER = "// [EnemyMasses Patch] WebGPU Fix";

        /// <summary>
        /// Applies patches to GPU Instancer Pro shaders. Called from EnemyMassesSetupWizard.
        /// </summary>
        public static void PatchGPUIShaders()
        {
            string fullPath = Path.GetFullPath(GPUI_TREE_PROXY_SHADER_PATH);
            
            if (!File.Exists(fullPath))
            {
                EditorUtility.DisplayDialog(
                    "Enemy Masses - Patcher",
                    "Could not find TreeProxy_GPUIPro.shader at:\n\n" + fullPath + "\n\n" +
                    "Please ensure GPU Instancer Pro is installed via Package Manager.",
                    "OK");
                return;
            }

            // Ask for user confirmation before applying patches
            if (!EditorUtility.DisplayDialog(
                "Enemy Masses - Apply GPUI Shader Patches",
                "This will patch GPU Instancer Pro shader files to fix:\n\n" +
                "• WebGPU build error in TreeProxy shader\n\n" +
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
            string fullPath = Path.GetFullPath(GPUI_TREE_PROXY_SHADER_PATH);
            
            if (!File.Exists(fullPath))
            {
                Debug.LogWarning("[EnemyMasses Patcher] Could not find TreeProxy_GPUIPro.shader");
                return false;
            }

            string content = File.ReadAllText(fullPath);

            if (content.Contains(PATCH_MARKER))
            {
                if (showResultDialog)
                {
                    EditorUtility.DisplayDialog(
                        "Enemy Masses - Patcher",
                        "TreeProxy_GPUIPro.shader is already patched.",
                        "OK");
                }
                return true;
            }

            // Create backup
            string backupPath = fullPath + ".backup";
            if (!File.Exists(backupPath))
            {
                File.Copy(fullPath, backupPath);
                Debug.Log($"[EnemyMasses Patcher] Created backup at: {backupPath}");
            }

            bool patched = false;

            // Patch: Replace void vert () { } with valid WebGPU vertex shader
            string originalCode = "void vert () { }";
            string patchedCode = @"
            // [EnemyMasses Patch] WebGPU Fix: Vertex shader must return position
            struct v2f {
                float4 pos : SV_POSITION;
            };

            v2f vert () { 
                v2f o;
                o.pos = float4(0,0,0,1);
                return o;
            }";

            if (content.Contains(originalCode))
            {
                content = content.Replace(originalCode, patchedCode);
                patched = true;
            }
            else
            {
                // Try to find with different formatting if exact match fails
                // Regex might be safer but simple replace is often enough for generated/consistent code
                if (content.Contains("void vert() {}"))
                {
                    content = content.Replace("void vert() {}", patchedCode);
                    patched = true;
                }
                else if (content.Contains("void vert (){ }"))
                {
                    content = content.Replace("void vert (){ }", patchedCode);
                    patched = true;
                }
            }

            if (patched)
            {
                try
                {
                    File.WriteAllText(fullPath, content);
                    Debug.Log("[EnemyMasses Patcher] Successfully patched TreeProxy_GPUIPro.shader");
                    
                    if (showResultDialog)
                    {
                        EditorUtility.DisplayDialog(
                            "Enemy Masses - Patcher",
                            "Successfully patched TreeProxy_GPUIPro.shader for WebGPU compatibility.",
                            "OK");
                    }
                    return true;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[EnemyMasses Patcher] Failed to write to file: {e.Message}");
                    return false;
                }
            }
            else
            {
                Debug.LogWarning("[EnemyMasses Patcher] Could not find code pattern to patch in TreeProxy_GPUIPro.shader");
                if (showResultDialog)
                {
                    EditorUtility.DisplayDialog(
                        "Enemy Masses - Patcher",
                        "Could not find the code pattern to patch. The file might have been modified or is a different version.",
                        "OK");
                }
                return false;
            }
        }
    }
}
#endif
