using UnityEngine;
using GPUInstancerPro.PrefabModule;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Configuration settings for spawning crowds in Enemy Masses.
    /// </summary>
    [System.Serializable]
    public class SpawnSettings
    {
        [Header("GPUI Prefab Manager")]
        [Tooltip("Reference to GPUI Prefab Manager")]
        public GPUIPrefabManager prefabManager;
        
        [Tooltip("Register instances immediately when manager is provided. Recommended to prevent buffer index issues.")]
        public bool registerImmediately = true;
        
        [Header("Indicator")]
        [Tooltip("Indicator prefab for showing destination")]
        public GameObject indicatorPrefab;
        
        [Header("Formation System")]
        [Tooltip("Formation shape that agents will use around the target destination")]
        public FormationType formationType = FormationType.Random;
        
        [Tooltip("Size/radius of the formation around the destination")]
        [Min(1f)]
        public float formationSize = 10f;
        
        [Tooltip("Spacing between agents in the formation")]
        [Min(0.5f)]
        public float formationSpacing = 2f;
        
        [Tooltip("For hollow formations, the thickness of the formation walls")]
        [Min(0.5f)]
        public float formationThickness = 3f;

        [Header("Height Settings")]
        [Tooltip("If true, spawned agents will use the original spawn point's Y coordinate instead of the NavMesh height. " +
                 "Enable this if your NavMesh is at a different height than your visual terrain.")]
        public bool preserveSpawnHeight = false;
    }
}
