using System;
using System.Collections.Generic;
using UnityEngine;
using Arawn.EnemyMasses.Runtime.Navigation;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Skill effect that resurrects dead agents of the caster's EnemyCrowd.
    /// <para>
    /// For <b>NavMesh</b> and <b>FlowField</b> navigation: dead agents are disabled/pooled,
    /// so resurrection re-enables them via <see cref="EnemyMassesCrowdController.RespawnAgent"/>.
    /// </para>
    /// <para>
    /// For <b>A* Pathfinding</b> navigation: dead agents are destroyed, so resurrection
    /// spawns brand-new replacement agents via <see cref="EnemyMassesCrowdController.SpawnEnemyCrowd"/>.
    /// </para>
    /// <para>
    /// The <see cref="EnemyCrowdPortraitBarUI"/> automatically reflects resurrection —
    /// once living agents exist again for a crowd, the portrait reverts from the dead tint
    /// and darkened frame back to normal.
    /// </para>
    /// </summary>
    [Serializable]
    public class ResurrectEffect : SkillEffect
    {
        [Header("Resurrect Settings")]
        [Tooltip("Maximum number of agents to resurrect per cast.\n" +
                 "0 = resurrect ALL dead agents of the caster's EnemyCrowd.")]
        [SerializeField] private int maxResurrectCount = 0;

        [Tooltip("Radius around the caster where resurrected agents appear.\n" +
                 "Agents are placed at random positions within this radius.\n" +
                 "For non-A* agents, set to 0 to respawn at their original death position.")]
        [SerializeField] private float spawnRadius = 5f;

        [Tooltip("Health percentage (0-1) that resurrected agents come back with.\n" +
                 "1.0 = full health, 0.5 = half health.")]
        [SerializeField, Range(0.01f, 1f)] private float healthPercent = 1f;

        [Tooltip("If true, resurrected agents appear at the caster's position " +
                 "(within spawnRadius). If false, non-A* agents respawn at their " +
                 "original death position.")]
        [SerializeField] private bool respawnAtCaster = true;

        public int MaxResurrectCount => maxResurrectCount;
        public float SpawnRadius => spawnRadius;
        public float HealthPercent => healthPercent;
        public bool RespawnAtCaster => respawnAtCaster;

        public override void Execute(SkillExecutionContext context)
        {
            if (context.Controller == null || context.Caster == null)
                return;

            int casterCrowdIndex = context.Caster.enemyCrowdIndex;
            EnemyCrowd crowd = context.Caster.enemyCrowd;
            if (crowd == null) return;

            Vector3 casterPos = context.Caster.WorldPosition;
            bool isAStarNavigation = crowd.navigationType.RequiresAStar();

            // Collect dead agents for the caster's EnemyCrowd
            var agents = context.Controller.GetAgents();
            var deadAgentIndices = new List<int>();
            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent != null && agent.isDead && agent.enemyCrowdIndex == casterCrowdIndex)
                {
                    deadAgentIndices.Add(agent.index);
                }
            }

            if (deadAgentIndices.Count == 0) return;

            // Cap to maxResurrectCount if set
            int toResurrect = deadAgentIndices.Count;
            if (maxResurrectCount > 0 && toResurrect > maxResurrectCount)
                toResurrect = maxResurrectCount;

            int resurrectedCount = 0;

            if (isAStarNavigation)
            {
                // A* agents have their GameObjects destroyed — cannot re-enable.
                // Spawn brand-new replacement agents instead.
                Vector3 spawnCenter = casterPos;
                var spawnedIndices = context.Controller.SpawnEnemyCrowd(
                    casterCrowdIndex,
                    spawnCenter,
                    Quaternion.identity,
                    toResurrect,
                    FormationType.Circle,
                    spawnRadius > 0f ? spawnRadius : (float?)null
                );

                if (spawnedIndices != null)
                {
                    resurrectedCount = spawnedIndices.Count;

                    // Apply partial health if healthPercent < 1
                    if (healthPercent < 1f)
                    {
                        foreach (int idx in spawnedIndices)
                        {
                            float maxHealth = context.Controller.GetAgentMaxHealth(idx);
                            context.Controller.SetAgentHealth(idx, maxHealth * healthPercent);
                        }
                    }
                }
            }
            else
            {
                // NavMesh / FlowField agents are disabled/pooled — re-enable via RespawnAgent.
                for (int i = 0; i < toResurrect; i++)
                {
                    int agentIndex = deadAgentIndices[i];

                    Vector3 spawnPos;
                    Quaternion spawnRot;

                    if (respawnAtCaster)
                    {
                        // Random offset within spawnRadius of the caster
                        Vector2 offset = UnityEngine.Random.insideUnitCircle * spawnRadius;
                        spawnPos = casterPos + new Vector3(offset.x, 0f, offset.y);
                        spawnRot = Quaternion.identity;
                    }
                    else
                    {
                        // Respawn at the agent's last known position (death position)
                        var agent = context.Controller.GetAgent(agentIndex);
                        spawnPos = agent != null ? agent.lastDestination : casterPos;
                        spawnRot = Quaternion.identity;
                    }

                    bool success = context.Controller.RespawnAgent(agentIndex, spawnPos, spawnRot);
                    if (success)
                    {
                        resurrectedCount++;

                        // Apply partial health if healthPercent < 1
                        if (healthPercent < 1f)
                        {
                            float maxHealth = context.Controller.GetAgentMaxHealth(agentIndex);
                            context.Controller.SetAgentHealth(agentIndex, maxHealth * healthPercent);
                        }
                    }
                }
            }

            if (resurrectedCount > 0)
            {
                Debug.Log($"[ResurrectEffect] Resurrected {resurrectedCount}/{toResurrect} agents for " +
                          $"{crowd.factionName} (nav: {crowd.navigationType})");
            }
            else
            {
                Debug.LogWarning($"[ResurrectEffect] Failed to resurrect any agents for {crowd.factionName}");
            }
        }

        public override string GetDescription()
        {
            string countDesc = maxResurrectCount > 0 ? $"up to {maxResurrectCount}" : "all";
            string healthDesc = healthPercent < 1f ? $" at {healthPercent * 100:0}% health" : "";
            return $"Resurrects {countDesc} dead allies{healthDesc}";
        }
    }
}
