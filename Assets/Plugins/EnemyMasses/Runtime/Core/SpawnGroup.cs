using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Represents a batch of agents spawned together in a single SpawnEnemyCrowd() call.
    /// Allows for fine-grained control over specific spawn batches independent of faction type.
    /// </summary>
    public class SpawnGroup
    {
        /// <summary>
        /// Unique identifier for this spawn group (auto-incremented).
        /// </summary>
        public int groupId;
        
        /// <summary>
        /// Index of the EnemyCrowd faction this group belongs to.
        /// </summary>
        public int factionIndex;
        
        /// <summary>
        /// Name of the faction for display purposes.
        /// </summary>
        public string factionName;
        
        /// <summary>
        /// List of agent indices belonging to this group.
        /// </summary>
        public List<int> agentIndices;
        
        /// <summary>
        /// Position where this group was spawned.
        /// </summary>
        public Vector3 spawnPosition;
        
        /// <summary>
        /// Formation type used when spawning this group.
        /// </summary>
        public FormationType formation;
        
        /// <summary>
        /// Formation spacing used when spawning this group.
        /// Stored so patrol and other movement commands can maintain the same spacing.
        /// </summary>
        public float formationSpacing;
        
        /// <summary>
        /// Time when this group was spawned.
        /// </summary>
        public float spawnTime;

        public SpawnGroup(int groupId, int factionIndex, string factionName, Vector3 spawnPosition, FormationType formation, float formationSpacing = 2f)
        {
            this.groupId = groupId;
            this.factionIndex = factionIndex;
            this.factionName = factionName;
            this.spawnPosition = spawnPosition;
            this.formation = formation;
            this.formationSpacing = formationSpacing;
            this.spawnTime = Time.time;
            this.agentIndices = new List<int>();
        }

        /// <summary>
        /// Gets the number of alive agents in this group.
        /// </summary>
        public int GetAliveCount(List<CrowdAgent> allAgents)
        {
            int count = 0;
            foreach (int index in agentIndices)
            {
                if (index >= 0 && index < allAgents.Count)
                {
                    var agent = allAgents[index];
                    if (agent != null && agent.crowdInstance != null && agent.health > 0)
                        count++;
                }
            }
            return count;
        }
    }
}
