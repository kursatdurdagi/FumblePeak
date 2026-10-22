using UnityEngine;

// Unity 6+ uses PhysicsMaterial, Unity 2022.3 and earlier use PhysicMaterial
#if UNITY_6000_0_OR_NEWER
using PhysicsMaterialCompat = UnityEngine.PhysicsMaterial;
#else
using PhysicsMaterialCompat = UnityEngine.PhysicMaterial;
#endif

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Static utility class for detecting surface types at world positions.
    /// Supports terrain texture sampling, physics material detection, and tag/layer-based fallbacks.
    /// Optimized for high-frequency calls from crowd agents.
    /// </summary>
    public static class SurfaceDetector
    {
        // Cached raycast results to reduce allocations
        private static readonly RaycastHit[] _raycastResults = new RaycastHit[4];
        
        // Default layer mask for surface detection (Ground, Default, Terrain layers)
        private static LayerMask _defaultLayerMask = -1; // All layers
        
        /// <summary>
        /// Sets the default layer mask used for surface raycasts.
        /// </summary>
        public static void SetDefaultLayerMask(LayerMask mask)
        {
            _defaultLayerMask = mask;
        }
        
        /// <summary>
        /// Detects the surface type at a world position using terrain sampling and raycast fallback.
        /// </summary>
        /// <param name="position">World position to check</param>
        /// <param name="mapping">Surface mapping configuration</param>
        /// <param name="raycastDown">Distance to raycast down for non-terrain detection</param>
        /// <returns>The detected surface type</returns>
        public static FootstepSurfaceType GetSurfaceTypeAtPosition(
            Vector3 position,
            SurfaceMapping mapping,
            float raycastDown = 2f)
        {
            if (mapping == null)
                return FootstepSurfaceType.Default;
            
            // First, try terrain detection (most common case for outdoor environments)
            var terrain = GetTerrainAtPosition(position);
            if (terrain != null)
            {
                return GetSurfaceTypeFromTerrain(position, terrain, mapping);
            }
            
            // Fall back to raycast-based detection
            return GetSurfaceTypeFromRaycast(position, mapping, raycastDown);
        }
        
        /// <summary>
        /// Gets the surface type from terrain texture at a position.
        /// Uses terrain splat map (alpha map) sampling.
        /// </summary>
        public static FootstepSurfaceType GetSurfaceTypeFromTerrain(
            Vector3 worldPosition,
            Terrain terrain,
            SurfaceMapping mapping)
        {
            if (terrain == null || mapping == null)
                return mapping?.defaultTerrainSurfaceType ?? FootstepSurfaceType.Default;
            
            TerrainData terrainData = terrain.terrainData;
            if (terrainData == null)
                return mapping.defaultTerrainSurfaceType;
            
            // Convert world position to terrain-local coordinates
            Vector3 terrainPos = terrain.transform.position;
            Vector3 relativePos = worldPosition - terrainPos;
            
            // Normalize to terrain size (0-1 range)
            float normalizedX = relativePos.x / terrainData.size.x;
            float normalizedZ = relativePos.z / terrainData.size.z;
            
            // Clamp to valid range
            normalizedX = Mathf.Clamp01(normalizedX);
            normalizedZ = Mathf.Clamp01(normalizedZ);
            
            // Convert to splat map coordinates
            int alphaMapWidth = terrainData.alphamapWidth;
            int alphaMapHeight = terrainData.alphamapHeight;
            int mapX = Mathf.FloorToInt(normalizedX * (alphaMapWidth - 1));
            int mapZ = Mathf.FloorToInt(normalizedZ * (alphaMapHeight - 1));
            
            // Get splat map data at this position (1x1 area)
            float[,,] splatMapData = terrainData.GetAlphamaps(mapX, mapZ, 1, 1);
            
            // Find the dominant texture (highest weight)
            int dominantTextureIndex = 0;
            float maxWeight = 0f;
            int numLayers = splatMapData.GetLength(2);
            
            for (int i = 0; i < numLayers; i++)
            {
                float weight = splatMapData[0, 0, i];
                if (weight > maxWeight)
                {
                    maxWeight = weight;
                    dominantTextureIndex = i;
                }
            }
            
            // Get the terrain layer name
            TerrainLayer[] layers = terrainData.terrainLayers;
            if (layers == null || dominantTextureIndex >= layers.Length)
                return mapping.defaultTerrainSurfaceType;
            
            TerrainLayer dominantLayer = layers[dominantTextureIndex];
            if (dominantLayer == null)
                return mapping.defaultTerrainSurfaceType;
            
            // Use the layer name to determine surface type
            string layerName = dominantLayer.name;
            if (string.IsNullOrEmpty(layerName) && dominantLayer.diffuseTexture != null)
                layerName = dominantLayer.diffuseTexture.name;
            
            return mapping.GetSurfaceTypeFromTerrainLayerName(layerName);
        }
        
        /// <summary>
        /// Gets the surface type using a raycast from position downward.
        /// Checks physics materials, tags, and layers on hit objects.
        /// </summary>
        public static FootstepSurfaceType GetSurfaceTypeFromRaycast(
            Vector3 position,
            SurfaceMapping mapping,
            float raycastDown = 2f,
            LayerMask? layerMask = null)
        {
            if (mapping == null)
                return FootstepSurfaceType.Default;
            
            LayerMask mask = layerMask ?? _defaultLayerMask;
            
            // Raycast downward from slightly above position
            Ray ray = new Ray(position + Vector3.up * 0.1f, Vector3.down);
            int hitCount = Physics.RaycastNonAlloc(ray, _raycastResults, raycastDown + 0.1f, mask);
            
            if (hitCount == 0)
                return mapping.defaultSurfaceType;
            
            // Find closest hit
            RaycastHit closestHit = _raycastResults[0];
            for (int i = 1; i < hitCount; i++)
            {
                if (_raycastResults[i].distance < closestHit.distance)
                    closestHit = _raycastResults[i];
            }
            
            // Try physics material first (most specific)
            Collider hitCollider = closestHit.collider;
            if (hitCollider != null)
            {
                PhysicsMaterialCompat physicMaterial = hitCollider.sharedMaterial;
                if (physicMaterial != null)
                {
                    var surfaceType = mapping.GetSurfaceTypeFromPhysicsMaterial(physicMaterial);
                    if (surfaceType != FootstepSurfaceType.Default)
                        return surfaceType;
                }
                
                // Try layer
                var layerSurfaceType = mapping.GetSurfaceTypeFromLayer(hitCollider.gameObject.layer);
                if (layerSurfaceType != FootstepSurfaceType.Default)
                    return layerSurfaceType;
                
                // Try tag
                string tag = hitCollider.gameObject.tag;
                if (!string.IsNullOrEmpty(tag) && tag != "Untagged")
                {
                    var tagSurfaceType = mapping.GetSurfaceTypeFromTag(tag);
                    if (tagSurfaceType != FootstepSurfaceType.Default)
                        return tagSurfaceType;
                }
            }
            
            return mapping.defaultSurfaceType;
        }
        
        /// <summary>
        /// Gets the terrain at a world position, if any.
        /// </summary>
        public static Terrain GetTerrainAtPosition(Vector3 position)
        {
            // First check active terrain (fast path for single terrain scenes)
            Terrain activeTerrain = Terrain.activeTerrain;
            if (activeTerrain != null && IsPositionOnTerrain(position, activeTerrain))
                return activeTerrain;
            
            // Check all terrains for multi-terrain scenes
            Terrain[] allTerrains = Terrain.activeTerrains;
            foreach (var terrain in allTerrains)
            {
                if (terrain != null && IsPositionOnTerrain(position, terrain))
                    return terrain;
            }
            
            return null;
        }
        
        /// <summary>
        /// Checks if a world position is within a terrain's bounds.
        /// </summary>
        public static bool IsPositionOnTerrain(Vector3 position, Terrain terrain)
        {
            if (terrain == null || terrain.terrainData == null)
                return false;
            
            Vector3 terrainPos = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;
            
            return position.x >= terrainPos.x &&
                   position.x <= terrainPos.x + terrainSize.x &&
                   position.z >= terrainPos.z &&
                   position.z <= terrainPos.z + terrainSize.z;
        }
        
        /// <summary>
        /// Batch processes surface detection for multiple positions.
        /// More efficient than calling GetSurfaceTypeAtPosition repeatedly.
        /// </summary>
        /// <param name="positions">Array of world positions</param>
        /// <param name="results">Output array for surface types (must be same length as positions)</param>
        /// <param name="mapping">Surface mapping configuration</param>
        /// <param name="raycastDown">Distance to raycast down for non-terrain detection</param>
        public static void GetSurfaceTypesAtPositions(
            Vector3[] positions,
            FootstepSurfaceType[] results,
            SurfaceMapping mapping,
            float raycastDown = 2f)
        {
            if (positions == null || results == null || mapping == null)
                return;
            
            int count = Mathf.Min(positions.Length, results.Length);
            
            // Cache active terrain for batch processing
            Terrain activeTerrain = Terrain.activeTerrain;
            Terrain[] allTerrains = null;
            
            for (int i = 0; i < count; i++)
            {
                Vector3 pos = positions[i];
                
                // Try active terrain first
                if (activeTerrain != null && IsPositionOnTerrain(pos, activeTerrain))
                {
                    results[i] = GetSurfaceTypeFromTerrain(pos, activeTerrain, mapping);
                    continue;
                }
                
                // Check other terrains if needed
                if (allTerrains == null)
                    allTerrains = Terrain.activeTerrains;
                
                bool foundTerrain = false;
                foreach (var terrain in allTerrains)
                {
                    if (terrain != activeTerrain && IsPositionOnTerrain(pos, terrain))
                    {
                        results[i] = GetSurfaceTypeFromTerrain(pos, terrain, mapping);
                        foundTerrain = true;
                        break;
                    }
                }
                
                if (!foundTerrain)
                {
                    results[i] = GetSurfaceTypeFromRaycast(pos, mapping, raycastDown);
                }
            }
        }
    }
}
