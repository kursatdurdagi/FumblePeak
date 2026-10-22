using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Provides a fog-of-war texture for minimap overlay rendering.
    /// Implemented by FogOfWarLite to allow the minimap to display fog
    /// without a direct assembly reference to the fog module.
    /// </summary>
    public interface IMinimapFogProvider
    {
        /// <summary>
        /// The fog texture (typically Alpha8 format).
        /// 255 = fully hidden, 0 = fully revealed.
        /// </summary>
        Texture2D FogTexture { get; }

        /// <summary>
        /// Whether the fog buffers have been initialized and are safe to read.
        /// </summary>
        bool HasInitializedBuffers { get; }

        /// <summary>
        /// World-space center of the fog grid (X, Z).
        /// </summary>
        Vector2 FogWorldCenter { get; }

        /// <summary>
        /// World-space size of the fog grid (width, depth) in world units.
        /// </summary>
        Vector2 FogWorldSize { get; }
    }
}
