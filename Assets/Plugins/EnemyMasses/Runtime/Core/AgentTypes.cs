using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Reference to an agent that includes the controller it belongs to.
    /// Use this when working with multiple EnemyCrowd controllers.
    /// </summary>
    public struct AgentReference
    {
        public EnemyMassesCrowdController controller;
        public int index;
        public Vector3 position;
        
        public bool IsValid => controller != null && index >= 0;
        
        public AgentReference(EnemyMassesCrowdController controller, int index, Vector3 position)
        {
            this.controller = controller;
            this.index = index;
            this.position = position;
        }
    }
    
    /// <summary>
    /// Cached data for an agent's position and state.
    /// Used by CrowdAgentTracker and PlayerSkillManager for efficient range queries.
    /// </summary>
    public struct CachedAgentData
    {
        public EnemyMassesCrowdController controller;
        public int index;
        public Vector3 position;
        public bool isAlive;
    }
}

// Backwards compatibility - expose in Skills namespace
namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Alias for backwards compatibility. Use Arawn.EnemyMasses.Runtime.AgentReference instead.
    /// </summary>
    public struct AgentReference
    {
        public EnemyMassesCrowdController controller;
        public int index;
        public Vector3 position;
        
        public bool IsValid => controller != null && index >= 0;
        
        public AgentReference(EnemyMassesCrowdController controller, int index, Vector3 position)
        {
            this.controller = controller;
            this.index = index;
            this.position = position;
        }
        
        // Implicit conversion from/to base namespace version
        public static implicit operator Runtime.AgentReference(AgentReference a) => 
            new Runtime.AgentReference(a.controller, a.index, a.position);
        public static implicit operator AgentReference(Runtime.AgentReference a) => 
            new AgentReference(a.controller, a.index, a.position);
    }
    
    /// <summary>
    /// Alias for backwards compatibility. Use Arawn.EnemyMasses.Runtime.CachedAgentData instead.
    /// </summary>
    public struct CachedAgentData
    {
        public EnemyMassesCrowdController controller;
        public int index;
        public Vector3 position;
        public bool isAlive;
        
        // Implicit conversion from/to base namespace version
        public static implicit operator Runtime.CachedAgentData(CachedAgentData a) => 
            new Runtime.CachedAgentData { controller = a.controller, index = a.index, position = a.position, isAlive = a.isAlive };
        public static implicit operator CachedAgentData(Runtime.CachedAgentData a) => 
            new CachedAgentData { controller = a.controller, index = a.index, position = a.position, isAlive = a.isAlive };
    }
}
