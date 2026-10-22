using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Workflow candidate used for hybrid workflow priority sorting
    /// </summary>
    public struct WorkflowCandidate
    {
        public int agentIndex;
        public float sqrDistance;
        public CrowdAgent agent;
        public Vector3 screenPosition;
        public bool isInCenterScreen;
        public float priorityScore; // Lower score = higher priority
    }
}
