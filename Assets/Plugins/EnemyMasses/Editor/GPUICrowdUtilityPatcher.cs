// Patches GPUICrowdUtility.cs to suppress per-frame LogError spam when crowd RSG data is missing.
// This patch applies to ALL GPUI Pro versions — the issue is not version-gated.
#if EM_GPUIP_INSTALLED
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;

namespace Arawn.EnemyMasses.Runtime.Editor
{
    /// <summary>
    /// Patches GPU Instancer Pro's GPUICrowdUtility.cs to suppress per-frame
    /// Debug.LogError spam when crowd render-source-group data is temporarily
    /// missing (timing/init-order issue in the package).
    ///
    /// The bug:
    /// GPUICrowdUtility.StartBlend and SetAnimationSpeeds call
    /// GPUICrowdSkinningSystem.Instance.RenderSourceGroupProvider.TryGetData(...)
    /// and emit Debug.LogError every frame when data is absent.  This floods the
    /// console and tanks the editor.  The calling code already handles the
    /// false / void return gracefully, so the LogError serves no purpose at
    /// runtime.
    ///
    /// Patch 6 replaces both Debug.LogError calls with a silent early-return.
    /// </summary>
    public static class GPUICrowdUtilityPatcher
    {
        internal const string GPUI_CROWD_UTILITY_PATH =
            "Packages/com.gurbu.gpui-pro.crowd-animations/Runtime/Scripts/Static/GPUICrowdUtility.cs";

        internal const string PATCH_MARKER_6 =
            "// [EnemyMasses Patch 6] Suppress per-frame error spam for missing crowd data";

        // ----------------------------------------------------------------
        // Public API (called from EnemyMassesSetupWizard)
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns true when GPUICrowdUtility.cs exists on disk.
        /// </summary>
        public static bool IsAvailable()
        {
            return File.Exists(Path.GetFullPath(GPUI_CROWD_UTILITY_PATH));
        }

        /// <summary>
        /// Returns true when Patch 6 is already present in the file.
        /// </summary>
        public static bool IsPatched()
        {
            string fullPath = Path.GetFullPath(GPUI_CROWD_UTILITY_PATH);
            if (!File.Exists(fullPath))
                return false;
            string content = File.ReadAllText(fullPath);
            return content.Contains(PATCH_MARKER_6);
        }

        /// <summary>
        /// Applies Patch 6 to GPUICrowdUtility.cs.
        /// Creates a .backup file before the first modification.
        /// </summary>
        /// <returns>True if the patch was applied (or already present).</returns>
        public static bool ApplyPatch(bool showResultDialog = false)
        {
            string fullPath = Path.GetFullPath(GPUI_CROWD_UTILITY_PATH);

            if (!File.Exists(fullPath))
            {
                Debug.LogWarning("[EnemyMasses Patcher] GPUICrowdUtility.cs not found at: " + fullPath);
                return false;
            }

            string content = File.ReadAllText(fullPath);

            if (content.Contains(PATCH_MARKER_6))
            {
                Debug.Log("[EnemyMasses Patcher] Patch 6 already applied to GPUICrowdUtility.cs.");
                return true;
            }

            // Backup
            string backupPath = fullPath + ".backup";
            if (!File.Exists(backupPath))
            {
                File.Copy(fullPath, backupPath);
                Debug.Log("[EnemyMasses Patcher] Created backup at: " + backupPath);
            }

            string patchedContent = content;
            int patchCount = 0;

            // ── Targeted approach: replace each Debug.LogError line that references
            // ERROR_NO_CROWD_DATA with our patch marker comment.  This is the only
            // LogError in GPUICrowdUtility that mentions this constant, and it
            // appears exactly twice: once in StartBlend (return false) and once in
            // SetAnimationSpeeds (return void).  The surrounding if-block and
            // return statement are left intact.
            //
            // Before:
            //   Debug.LogError(GPUIConstants.LOG_PREFIX + string.Format(GPUICrowdConstants.ERROR_NO_CROWD_DATA, rendererKey));
            // After:
            //   // [EnemyMasses Patch 6] Suppress per-frame error spam for missing crowd data

            string errorPattern =
                @"(\s*)Debug\.LogError\(GPUIConstants\.LOG_PREFIX\s*\+\s*string\.Format\(" +
                @"GPUICrowdConstants\.ERROR_NO_CROWD_DATA,\s*rendererKey\)\);";

            patchCount = Regex.Matches(patchedContent, errorPattern).Count;

            if (patchCount > 0)
            {
                patchedContent = Regex.Replace(patchedContent, errorPattern, "$1" + PATCH_MARKER_6);
                Debug.Log($"[EnemyMasses Patcher] Replaced {patchCount} Debug.LogError line(s) with patch marker");
            }

            if (patchCount > 0)
            {
                File.WriteAllText(fullPath, patchedContent);
                AssetDatabase.Refresh();
                Debug.Log($"[EnemyMasses Patcher] Patch 6 applied to {patchCount} site(s) in GPUICrowdUtility.cs");
            }
            else
            {
                Debug.LogWarning("[EnemyMasses Patcher] Could not find Patch 6 targets in GPUICrowdUtility.cs. The package code may have changed.");
            }

            if (showResultDialog)
            {
                string msg = patchCount > 0
                    ? $"Patch 6 applied to {patchCount} site(s) in GPUICrowdUtility.cs.\n\n" +
                      "The per-frame 'Can not find Crowd data' error spam will no longer appear."
                    : "Could not apply Patch 6 — the target code was not found.\n" +
                      "The GPU Instancer Pro package may have changed.";
                EditorUtility.DisplayDialog("Enemy Masses — Crowd Utility Patch", msg, "OK");
            }

            return patchCount > 0;
        }
    }
}
#endif
