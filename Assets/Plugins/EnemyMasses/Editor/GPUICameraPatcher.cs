// Only compile patcher when GPUI Pro needs patching (version < 0.12.9)
#if EM_GPUIP_NEEDS_PATCH
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace Arawn.EnemyMasses.Editor
{
    /// <summary>
    /// Patches GPU Instancer Pro scripts to fix Unity 6 UI Toolkit text rendering errors.
    /// 
    /// The bug:
    /// Unity 6's UI Toolkit text rendering system has threading issues when rendering certain
    /// attributes (like HelpURL) in the Inspector. This causes errors like:
    /// - "GetName can only be called from the main thread"
    /// - "InvalidOperationException: Operations that change non-concurrent collections..."
    /// - "NullReferenceException in MeshGenerator.DrawText"
    /// 
    /// This patcher comments out HelpURL attributes to prevent these errors.
    /// </summary>
    public static class GPUICameraPatcher
    {
        // Files to patch - these are commonly viewed in the Inspector
        private static readonly string[] GPUI_FILES_TO_PATCH = new string[]
        {
            "Packages/com.gurbu.gpui-pro/Runtime/Scripts/GPUICamera.cs",
            "Packages/com.gurbu.gpui-pro/Runtime/Scripts/Helpers/GPUIEventFaceCamera.cs",
            "Packages/com.gurbu.gpui-pro/Runtime/Scripts/Helpers/GPUIEventFloatingOrigin.cs",
            "Packages/com.gurbu.gpui-pro/Runtime/Scripts/Helpers/GPUIAreaCuller.cs",
            "Packages/com.gurbu.gpui-pro/Runtime/Scripts/Helpers/GPUIEventSystem.cs",
            "Packages/com.gurbu.gpui-pro/Runtime/Scripts/Data/GPUIProfile.cs",
            "Packages/com.gurbu.gpui-pro/Runtime/Scripts/Data/GPUILODGroupData.cs",
        };
        
        // Marker to detect if already patched
        private const string PATCH_MARKER = "// [EnemyMasses Patch] HelpURL commented out for Unity 6 compatibility";

        /// <summary>
        /// Applies HelpURL patches to GPU Instancer Pro. Called from EnemyMassesSetupWizard.
        /// </summary>
        public static void PatchGPUIHelpURLs()
        {
            // Ask for user confirmation before applying patches
            if (!EditorUtility.DisplayDialog(
                "Enemy Masses - Apply HelpURL Patches",
                "This will patch GPU Instancer Pro source files to fix Unity 6 UI Toolkit text rendering errors.\n\n" +
                "The patch comments out [HelpURL] attributes that cause:\n" +
                "• \"GetName can only be called from the main thread\"\n" +
                "• \"InvalidOperationException: Operations that change non-concurrent collections...\"\n" +
                "• \"NullReferenceException in MeshGenerator.DrawText\"\n\n" +
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
            List<string> patchedFiles = new List<string>();
            List<string> alreadyPatchedFiles = new List<string>();
            List<string> failedFiles = new List<string>();
            List<string> notFoundFiles = new List<string>();

            foreach (string relativePath in GPUI_FILES_TO_PATCH)
            {
                string fullPath = Path.GetFullPath(relativePath);
                
                if (!File.Exists(fullPath))
                {
                    notFoundFiles.Add(Path.GetFileName(relativePath));
                    continue;
                }

                string content = File.ReadAllText(fullPath);

                // Check if already patched
                if (content.Contains(PATCH_MARKER))
                {
                    alreadyPatchedFiles.Add(Path.GetFileName(relativePath));
                    continue;
                }

                // Create backup
                string backupPath = fullPath + ".helpurl.backup";
                if (!File.Exists(backupPath))
                {
                    File.Copy(fullPath, backupPath);
                }

                // Patch: Comment out any HelpURL attribute
                string patchPattern = @"(\[HelpURL\([^\)]+\)\])";
                
                if (Regex.IsMatch(content, patchPattern))
                {
                    string patchedContent = Regex.Replace(content, patchPattern, PATCH_MARKER + "\n    // $1");
                    File.WriteAllText(fullPath, patchedContent);
                    patchedFiles.Add(Path.GetFileName(relativePath));
                    Debug.Log($"[EnemyMasses GPUI Patcher] Patched: {relativePath}");
                }
                else
                {
                    failedFiles.Add(Path.GetFileName(relativePath));
                }
            }

            AssetDatabase.Refresh();

            // Build result message
            string message = "GPUI HelpURL Patching Results:\n\n";
            
            if (patchedFiles.Count > 0)
            {
                message += $"[OK] Patched ({patchedFiles.Count}):\n";
                foreach (string f in patchedFiles)
                    message += $"  - {f}\n";
                message += "\n";
            }
            
            if (alreadyPatchedFiles.Count > 0)
            {
                message += $"Already patched ({alreadyPatchedFiles.Count}):\n";
                foreach (string f in alreadyPatchedFiles)
                    message += $"  - {f}\n";
                message += "\n";
            }
            
            if (notFoundFiles.Count > 0)
            {
                message += $"Not found ({notFoundFiles.Count}):\n";
                foreach (string f in notFoundFiles)
                    message += $"  - {f}\n";
                message += "\n";
            }

            if (patchedFiles.Count > 0)
            {
                message += "Unity 6 UI Toolkit text rendering errors should now be fixed.\n";
                message += "Backups created with .helpurl.backup extension.";
            }
            else if (alreadyPatchedFiles.Count == GPUI_FILES_TO_PATCH.Length - notFoundFiles.Count)
            {
                message += "All files were already patched.";
            }

            if (showResultDialog)
            {
                EditorUtility.DisplayDialog("Enemy Masses - GPUI HelpURL Patcher", message, "OK");
            }
            
            return patchedFiles.Count > 0 || alreadyPatchedFiles.Count > 0;
        }

        /// <summary>
        /// Validates if HelpURL patching is available.
        /// </summary>
        public static bool ValidatePatchGPUIHelpURLs()
        {
            // Enable if any of the files exist
            foreach (string relativePath in GPUI_FILES_TO_PATCH)
            {
                if (File.Exists(Path.GetFullPath(relativePath)))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Restores original GPUI HelpURL files from backup.
        /// </summary>
        public static void RestoreOriginalGPUIHelpURLFiles()
        {
            List<string> restoredFiles = new List<string>();
            
            foreach (string relativePath in GPUI_FILES_TO_PATCH)
            {
                string fullPath = Path.GetFullPath(relativePath);
                string backupPath = fullPath + ".helpurl.backup";

                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, fullPath, true);
                    restoredFiles.Add(Path.GetFileName(relativePath));
                }
            }

            if (restoredFiles.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Enemy Masses - GPUI Patcher",
                    "No backup files found.\n\nThe original files cannot be restored.",
                    "OK");
                return;
            }

            AssetDatabase.Refresh();

            string message = $"Restored {restoredFiles.Count} file(s):\n\n";
            foreach (string f in restoredFiles)
                message += $"  - {f}\n";
            message += "\nNote: Unity 6 UI errors may return.";

            EditorUtility.DisplayDialog("Enemy Masses - GPUI Patcher", message, "OK");
            Debug.Log($"[EnemyMasses GPUI Patcher] Restored {restoredFiles.Count} original file(s)");
        }

        /// <summary>
        /// Validates if HelpURL restore is available.
        /// </summary>
        public static bool ValidateRestoreOriginalGPUIHelpURLFiles()
        {
            foreach (string relativePath in GPUI_FILES_TO_PATCH)
            {
                if (File.Exists(Path.GetFullPath(relativePath) + ".helpurl.backup"))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks HelpURL patch status.
        /// </summary>
        public static void CheckGPUIHelpURLPatchStatus()
        {
            List<string> patchedFiles = new List<string>();
            List<string> unpatchedFiles = new List<string>();
            List<string> notFoundFiles = new List<string>();

            foreach (string relativePath in GPUI_FILES_TO_PATCH)
            {
                string fullPath = Path.GetFullPath(relativePath);
                
                if (!File.Exists(fullPath))
                {
                    notFoundFiles.Add(Path.GetFileName(relativePath));
                    continue;
                }

                string content = File.ReadAllText(fullPath);
                
                if (content.Contains(PATCH_MARKER))
                    patchedFiles.Add(Path.GetFileName(relativePath));
                else
                    unpatchedFiles.Add(Path.GetFileName(relativePath));
            }

            string status = "GPUI HelpURL Patch Status:\n\n";
            
            if (patchedFiles.Count > 0)
            {
                status += $"[OK] Patched ({patchedFiles.Count}):\n";
                foreach (string f in patchedFiles)
                    status += $"  - {f}\n";
            }
            
            if (unpatchedFiles.Count > 0)
            {
                status += $"\n[X] Not patched ({unpatchedFiles.Count}):\n";
                foreach (string f in unpatchedFiles)
                    status += $"  - {f}\n";
            }

            if (unpatchedFiles.Count > 0)
            {
                status += "\nRun 'Tools > Enemy Masses > Fix GPUI HelpURL' to patch.";
            }
            else if (patchedFiles.Count > 0)
            {
                status += "\nAll files patched. Unity 6 UI errors should be fixed.";
            }

            EditorUtility.DisplayDialog("Enemy Masses - GPUI HelpURL Patch Status", status, "OK");
        }
    }
}
#endif
