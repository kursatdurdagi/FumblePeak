using UnityEngine;
using GPUInstancerPro.CrowdAnimations;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Easy-to-use handler for footstep animation events via GPUI Crowd Event Definition.
    /// Add this component to your scene and reference it in your GPUI Crowd Event Definition events.
    /// 
    /// Quick Setup:
    /// 1. Add this component to any GameObject in your scene (or it will auto-create)
    /// 2. In your GPUI Crowd Event Definition, create events at foot-plant times in walk/run clips
    /// 3. Set the event callback to this component's OnFootstep method
    /// 4. The stringParam can optionally specify surface type: "grass", "stone", "water", etc.
    /// </summary>
    [AddComponentMenu("Enemy Masses/Audio/Footstep Event Handler")]
    [DefaultExecutionOrder(1100)] // Run after GPUI systems
    public class FootstepEventHandler : MonoBehaviour
    {
        #region Singleton
        
        private static FootstepEventHandler _instance;
        
        /// <summary>
        /// Gets the singleton instance, auto-creating if needed.
        /// </summary>
        public static FootstepEventHandler Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<FootstepEventHandler>();
                    if (_instance == null)
                    {
                        var go = new GameObject("FootstepEventHandler");
                        _instance = go.AddComponent<FootstepEventHandler>();
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }
        
        #endregion
        
        #region Settings
        
        [Header("Debug")]
        [Tooltip("Enable debug logging for footstep events.")]
        [SerializeField] private bool debugLogging = false;
        
        #endregion
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }
        
        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }
        
        #endregion
        
        #region Animation Event Callbacks
        
        /// <summary>
        /// Called by GPUI Crowd Event Definition when a footstep event fires.
        /// Set this as the callback in your event definition.
        /// 
        /// Parameters:
        /// - crowdInstance: The GPUICrowdInstance that triggered the event
        /// - parameters.stringParam: Optional surface type ("grass", "stone", "water", "wood", "metal", "sand", "snow")
        /// - parameters.intParam: Optional left/right foot indicator (0=left, 1=right) - for future use
        /// - parameters.floatParam: Optional volume multiplier (1.0 = normal)
        /// </summary>
        public void OnFootstep(GPUICrowdInstance crowdInstance, GPUICrowdEventParameters parameters)
        {
            if (crowdInstance == null) return;
            
            // Get the controller
            var controller = EnemyMassesCrowdController.Instance;
            if (controller == null)
            {
                if (debugLogging)
                    Debug.LogWarning("[FootstepEventHandler] No EnemyMassesCrowdController found!");
                return;
            }
            
            // Find the agent by crowd instance
            CrowdAgent agent = controller.GetAgentByCrowdInstance(crowdInstance);
            if (agent == null)
            {
                if (debugLogging)
                    Debug.LogWarning($"[FootstepEventHandler] Could not find agent for {crowdInstance.name}");
                return;
            }
            
            // Parse surface type from string parameter
            FootstepSurfaceType surface = ParseSurfaceType(parameters.stringParam);
            
            // Play the footstep
            controller.PlayFootstepForAgent(agent, surface);
            
            if (debugLogging)
            {
                Debug.Log($"[FootstepEventHandler] Played footstep for agent {agent.index}, surface={surface}");
            }
        }
        
        /// <summary>
        /// Alternative callback that plays footstep at a specific position.
        /// Useful when you don't have direct agent reference.
        /// </summary>
        public void OnFootstepAtPosition(GPUICrowdInstance crowdInstance, GPUICrowdEventParameters parameters)
        {
            if (crowdInstance == null) return;
            
            var controller = EnemyMassesCrowdController.Instance;
            if (controller == null) return;
            
            CrowdAgent agent = controller.GetAgentByCrowdInstance(crowdInstance);
            if (agent == null || agent.enemyCrowd == null) return;
            
            Vector3 position = crowdInstance.transform.position;
            FootstepSurfaceType surface = ParseSurfaceType(parameters.stringParam);
            
            controller.PlayFootstepAtPosition(position, agent.enemyCrowd, surface);
        }
        
        #endregion
        
        #region Helpers
        
        private FootstepSurfaceType ParseSurfaceType(string surfaceString)
        {
            if (string.IsNullOrEmpty(surfaceString))
                return FootstepSurfaceType.Default;
            
            return surfaceString.ToLower() switch
            {
                "grass" => FootstepSurfaceType.Grass,
                "stone" => FootstepSurfaceType.Stone,
                "rock" => FootstepSurfaceType.Stone,
                "water" => FootstepSurfaceType.Water,
                "wood" => FootstepSurfaceType.Wood,
                "metal" => FootstepSurfaceType.Metal,
                "sand" => FootstepSurfaceType.Sand,
                "snow" => FootstepSurfaceType.Snow,
                _ => FootstepSurfaceType.Default
            };
        }
        
        #endregion
    }
}
