using UnityEngine;
using System.Collections.Generic;

// Unity 6+ uses PhysicsMaterial, Unity 2022.3 and earlier use PhysicMaterial
#if UNITY_6000_0_OR_NEWER
using PhysicsMaterialCompat = UnityEngine.PhysicsMaterial;
#else
using PhysicsMaterialCompat = UnityEngine.PhysicMaterial;
#endif

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// ScriptableObject that defines how terrain textures, physics materials, and tags map to surface types.
    /// Used by the surface detection system to determine which footstep sounds to play.
    /// </summary>
    [CreateAssetMenu(fileName = "SurfaceMapping", menuName = "Enemy Masses/Surface Mapping")]
    public class SurfaceMapping : ScriptableObject
    {
        [Header("Terrain Texture Mapping")]
        [Tooltip("Maps terrain texture names (or partial names) to surface types.\n" +
                 "The system checks if the terrain layer name CONTAINS the specified string (case-insensitive).")]
        public TerrainTextureMapping[] terrainTextureMappings = new TerrainTextureMapping[]
        {
            new() { textureNameContains = "grass", surfaceType = FootstepSurfaceType.Grass },
            new() { textureNameContains = "dirt", surfaceType = FootstepSurfaceType.Grass },
            new() { textureNameContains = "mud", surfaceType = FootstepSurfaceType.Grass },
            new() { textureNameContains = "stone", surfaceType = FootstepSurfaceType.Stone },
            new() { textureNameContains = "rock", surfaceType = FootstepSurfaceType.Stone },
            new() { textureNameContains = "concrete", surfaceType = FootstepSurfaceType.Stone },
            new() { textureNameContains = "gravel", surfaceType = FootstepSurfaceType.Stone },
            new() { textureNameContains = "sand", surfaceType = FootstepSurfaceType.Sand },
            new() { textureNameContains = "desert", surfaceType = FootstepSurfaceType.Sand },
            new() { textureNameContains = "snow", surfaceType = FootstepSurfaceType.Snow },
            new() { textureNameContains = "ice", surfaceType = FootstepSurfaceType.Snow },
            new() { textureNameContains = "water", surfaceType = FootstepSurfaceType.Water },
            new() { textureNameContains = "swamp", surfaceType = FootstepSurfaceType.Water },
        };
        
        [Header("Physics Material Mapping")]
        [Tooltip("Maps physics materials to surface types. Useful for non-terrain surfaces.")]
        public PhysicsMaterialMapping[] physicsMaterialMappings;
        
        [Header("Tag Mapping")]
        [Tooltip("Maps GameObject tags to surface types. Simple fallback for tagged surfaces.")]
        public TagMapping[] tagMappings = new TagMapping[]
        {
            new() { tag = "Wood", surfaceType = FootstepSurfaceType.Wood },
            new() { tag = "Metal", surfaceType = FootstepSurfaceType.Metal },
            new() { tag = "Water", surfaceType = FootstepSurfaceType.Water },
        };
        
        [Header("Layer Mapping")]
        [Tooltip("Maps layers to surface types. Alternative to tags.")]
        public LayerMapping[] layerMappings;
        
        [Header("Defaults")]
        [Tooltip("Default surface type when no mapping matches.")]
        public FootstepSurfaceType defaultSurfaceType = FootstepSurfaceType.Default;
        
        [Tooltip("Surface type for terrain when no texture mapping matches.")]
        public FootstepSurfaceType defaultTerrainSurfaceType = FootstepSurfaceType.Grass;
        
        // Cached dictionaries for fast lookup
        private Dictionary<string, FootstepSurfaceType> _tagLookup;
        private Dictionary<int, FootstepSurfaceType> _layerLookup;
        private Dictionary<PhysicsMaterialCompat, FootstepSurfaceType> _physicsMaterialLookup;
        
        private void OnEnable()
        {
            RebuildLookupTables();
        }
        
        /// <summary>
        /// Rebuilds the cached lookup dictionaries. Call after modifying mappings at runtime.
        /// </summary>
        public void RebuildLookupTables()
        {
            // Tag lookup
            _tagLookup = new Dictionary<string, FootstepSurfaceType>();
            if (tagMappings != null)
            {
                foreach (var mapping in tagMappings)
                {
                    if (!string.IsNullOrEmpty(mapping.tag) && !_tagLookup.ContainsKey(mapping.tag))
                        _tagLookup[mapping.tag] = mapping.surfaceType;
                }
            }
            
            // Layer lookup
            _layerLookup = new Dictionary<int, FootstepSurfaceType>();
            if (layerMappings != null)
            {
                foreach (var mapping in layerMappings)
                {
                    if (!_layerLookup.ContainsKey(mapping.layer))
                        _layerLookup[mapping.layer] = mapping.surfaceType;
                }
            }
            
            // Physics material lookup
            _physicsMaterialLookup = new Dictionary<PhysicsMaterialCompat, FootstepSurfaceType>();
            if (physicsMaterialMappings != null)
            {
                foreach (var mapping in physicsMaterialMappings)
                {
                    if (mapping.physicsMaterial != null && !_physicsMaterialLookup.ContainsKey(mapping.physicsMaterial))
                        _physicsMaterialLookup[mapping.physicsMaterial] = mapping.surfaceType;
                }
            }
        }
        
        /// <summary>
        /// Gets surface type from a terrain layer name.
        /// </summary>
        public FootstepSurfaceType GetSurfaceTypeFromTerrainLayerName(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
                return defaultTerrainSurfaceType;
            
            string lowerName = layerName.ToLowerInvariant();
            
            if (terrainTextureMappings != null)
            {
                foreach (var mapping in terrainTextureMappings)
                {
                    if (!string.IsNullOrEmpty(mapping.textureNameContains) &&
                        lowerName.Contains(mapping.textureNameContains.ToLowerInvariant()))
                    {
                        return mapping.surfaceType;
                    }
                }
            }
            
            return defaultTerrainSurfaceType;
        }
        
        /// <summary>
        /// Gets surface type from a GameObject tag.
        /// </summary>
        public FootstepSurfaceType GetSurfaceTypeFromTag(string tag)
        {
            if (_tagLookup == null)
                RebuildLookupTables();
            
            if (!string.IsNullOrEmpty(tag) && _tagLookup.TryGetValue(tag, out var surfaceType))
                return surfaceType;
            
            return defaultSurfaceType;
        }
        
        /// <summary>
        /// Gets surface type from a layer.
        /// </summary>
        public FootstepSurfaceType GetSurfaceTypeFromLayer(int layer)
        {
            if (_layerLookup == null)
                RebuildLookupTables();
            
            if (_layerLookup.TryGetValue(layer, out var surfaceType))
                return surfaceType;
            
            return defaultSurfaceType;
        }
        
        /// <summary>
        /// Gets surface type from a physics material.
        /// </summary>
        public FootstepSurfaceType GetSurfaceTypeFromPhysicsMaterial(PhysicsMaterialCompat material)
        {
            if (_physicsMaterialLookup == null)
                RebuildLookupTables();
            
            if (material != null && _physicsMaterialLookup.TryGetValue(material, out var surfaceType))
                return surfaceType;
            
            return defaultSurfaceType;
        }
        
        /// <summary>
        /// Creates a default surface mapping with common presets.
        /// </summary>
        public static SurfaceMapping CreateDefault()
        {
            var mapping = CreateInstance<SurfaceMapping>();
            mapping.name = "DefaultSurfaceMapping";
            return mapping;
        }
    }
    
    /// <summary>
    /// Maps terrain texture names (partial match) to surface types.
    /// </summary>
    [System.Serializable]
    public struct TerrainTextureMapping
    {
        [Tooltip("If the terrain layer name contains this string (case-insensitive), use this surface type.")]
        public string textureNameContains;
        
        [Tooltip("The surface type to use.")]
        public FootstepSurfaceType surfaceType;
    }
    
    /// <summary>
    /// Maps physics materials to surface types.
    /// </summary>
    [System.Serializable]
    public struct PhysicsMaterialMapping
    {
        [Tooltip("The physics material to match.")]
        public PhysicsMaterialCompat physicsMaterial;
        
        [Tooltip("The surface type to use.")]
        public FootstepSurfaceType surfaceType;
    }
    
    /// <summary>
    /// Maps tags to surface types.
    /// </summary>
    [System.Serializable]
    public struct TagMapping
    {
        [Tooltip("The GameObject tag to match.")]
        [Tag]
        public string tag;
        
        [Tooltip("The surface type to use.")]
        public FootstepSurfaceType surfaceType;
    }
    
    /// <summary>
    /// Maps layers to surface types.
    /// </summary>
    [System.Serializable]
    public struct LayerMapping
    {
        [Tooltip("The layer to match.")]
        [Layer]
        public int layer;
        
        [Tooltip("The surface type to use.")]
        public FootstepSurfaceType surfaceType;
    }
    
    /// <summary>
    /// Attribute to show tag selector in inspector.
    /// </summary>
    public class TagAttribute : PropertyAttribute { }
    
    /// <summary>
    /// Attribute to show layer selector in inspector.
    /// </summary>
    public class LayerAttribute : PropertyAttribute { }
}
