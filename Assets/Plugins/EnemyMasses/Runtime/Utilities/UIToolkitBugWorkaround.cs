using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Workaround for Unity 2023+ bug where UIElements/UI Toolkit text rendering
    /// causes "GetName can only be called from the main thread" errors during scene loading.
    /// 
    /// This is a known Unity engine bug (not caused by user code) that occurs when
    /// Unity's UITKTextJobSystem tries to access font assets from background threads.
    /// 
    /// Bug reference: Unity Issue Tracker - various reports for Unity 2023.x and 6000.x
    /// 
    /// This script attempts to pre-warm font assets on the main thread before scene loading
    /// can trigger the background thread access.
    /// </summary>
    public static class UIToolkitBugWorkaround
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            // Force font asset initialization on main thread before any background jobs
            // This helps prevent the race condition that causes the threading errors
            try
            {
                // Pre-warm TextCore by accessing it early on main thread
                var defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (defaultFont != null)
                {
                    // Just accessing the font helps initialize TextCore on main thread
                    var _ = defaultFont.name;
                }
            }
            catch
            {
                // Silently ignore - this is just a best-effort workaround
            }
        }
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void PreSceneLoad()
        {
            // Additional pre-warming before scene load
            // This runs after SubsystemRegistration but before any scene objects are loaded
        }
    }
}
