using UnityEngine;
using GPUInstancerPro;
using GPUInstancerPro.PrefabModule;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Ensures proper cleanup of GPU Instancer Pro prefab instances when agents are destroyed or disabled.
    /// This component is automatically added to spawned agents and removes them from GPUI
    /// before the GameObject is destroyed/disabled, preventing buffer index corruption.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class EnemyMassesPrefabCleanup : MonoBehaviour
    {
        private GPUIPrefab _gpuiPrefab;
        private bool _hasBeenCleanedUp;
        
        private void Awake()
        {
            _gpuiPrefab = GetComponent<GPUIPrefab>();
            _hasBeenCleanedUp = false;
        }
        
        private void OnEnable()
        {
            // Reset cleanup flag when re-enabled (e.g., pooled objects)
            _hasBeenCleanedUp = false;
        }
        
        private void OnDisable()
        {
            // Skip if already cleaned up or during editor shutdown
            if (_hasBeenCleanedUp)
                return;
                
            // Skip cleanup during play mode exit - GPUI is already shutting down
            if (!Application.isPlaying)
                return;
                
            CleanupGPUIInstance();
        }
        
        private void OnDestroy()
        {
            // Ensure cleanup happens even if OnDisable wasn't called
            if (_hasBeenCleanedUp)
                return;
                
            CleanupGPUIInstance();
        }
        
        private void CleanupGPUIInstance()
        {
            _hasBeenCleanedUp = true;
            
            // Use the safe helper which handles all validation and exceptions
            EnemyMassesGPUIHelper.SafeRemovePrefabInstance(_gpuiPrefab);
        }
    }
}
