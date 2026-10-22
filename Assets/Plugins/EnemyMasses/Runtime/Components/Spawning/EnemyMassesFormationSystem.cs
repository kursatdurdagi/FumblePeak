using UnityEngine;
using Arawn.EnemyMasses.Runtime.Navigation.Abstraction;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Calculates formation positions for crowd spawning.
    /// Supports multiple formation types: Random, Square, Circle, Triangle, Rectangle,
    /// and their hollow variants.
    /// 
    /// Also supports 3D layered formations for flying agents via
    /// <see cref="CalculateFormationPosition3D"/> which stacks multiple 2D formation layers
    /// at configurable vertical spacing. Network-safe: produced positions are deterministic
    /// given the same inputs — both server and client compute identical results.
    /// </summary>
    public class EnemyMassesFormationSystem
    {
        private readonly SpawnSettings _settings;

        /// <summary>
        /// Creates a new formation system with the given settings.
        /// </summary>
        public EnemyMassesFormationSystem(SpawnSettings settings)
        {
            _settings = settings ?? throw new System.ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Calculates a formation position for an agent based on its index.
        /// </summary>
        /// <param name="agentIndex">Index of the agent in the formation</param>
        /// <param name="totalAgents">Total number of agents in the formation</param>
        /// <param name="destination">Center point of the formation</param>
        /// <param name="destinationSpread">Spread radius for random positions</param>
        /// <param name="formationType">Type of formation to use (optional override)</param>
        /// <returns>World position for the agent</returns>
        public Vector3 CalculateFormationPosition(
            int agentIndex, 
            int totalAgents, 
            Vector3 destination, 
            float destinationSpread,
            FormationType? formationType = null)
        {
            return CalculateFormationPosition(agentIndex, totalAgents, destination, destinationSpread, Quaternion.identity, null, formationType);
        }

        /// <summary>
        /// Calculates a formation position for an agent based on its index using an optional navigation adapter
        /// for backend-aware position sampling.
        /// </summary>
        public Vector3 CalculateFormationPosition(
            int agentIndex,
            int totalAgents,
            Vector3 destination,
            float destinationSpread,
            INavigationAgent navigationAgent,
            FormationType? formationType = null)
        {
            return CalculateFormationPosition(agentIndex, totalAgents, destination, destinationSpread, Quaternion.identity, navigationAgent, formationType);
        }

        /// <summary>
        /// Calculates a formation position for an agent based on its index, with rotation support.
        /// </summary>
        /// <param name="agentIndex">Index of the agent in the formation</param>
        /// <param name="totalAgents">Total number of agents in the formation</param>
        /// <param name="destination">Center point of the formation</param>
        /// <param name="destinationSpread">Spread radius for random positions</param>
        /// <param name="rotation">Rotation to apply to the formation (rotates positions around center)</param>
        /// <param name="formationType">Type of formation to use (optional override)</param>
        /// <returns>World position for the agent</returns>
        public Vector3 CalculateFormationPosition(
            int agentIndex, 
            int totalAgents, 
            Vector3 destination, 
            float destinationSpread,
            Quaternion rotation,
            FormationType? formationType = null)
        {
            return CalculateFormationPosition(agentIndex, totalAgents, destination, destinationSpread, rotation, null, formationType);
        }

        /// <summary>
        /// Calculates a formation position for an agent based on its index, with rotation support and optional
        /// backend-aware sampling through an <see cref="INavigationAgent"/>.
        /// </summary>
        public Vector3 CalculateFormationPosition(
            int agentIndex,
            int totalAgents,
            Vector3 destination,
            float destinationSpread,
            Quaternion rotation,
            INavigationAgent navigationAgent,
            FormationType? formationType = null)
        {
            FormationType formation = formationType ?? _settings.formationType;
            
            if (formation == FormationType.Random)
            {
                return GetRandomSampledPosition(destination, destinationSpread, navigationAgent);
            }

            if (totalAgents <= 0)
                return destination;

            agentIndex = Mathf.Clamp(agentIndex, 0, Mathf.Max(0, totalAgents - 1));

            Vector3 formationPos = destination;

            switch (formation)
            {
                case FormationType.Square:
                    formationPos = CalculateSquareFormation(agentIndex, totalAgents, destination, false);
                    break;
                case FormationType.Circle:
                    formationPos = CalculateCircleFormation(agentIndex, totalAgents, destination, false);
                    break;
                case FormationType.Triangle:
                    formationPos = CalculateTriangleFormation(agentIndex, totalAgents, destination, false);
                    break;
                case FormationType.Rectangle:
                    formationPos = CalculateRectangleFormation(agentIndex, totalAgents, destination, false);
                    break;
                case FormationType.HollowSquare:
                    formationPos = CalculateSquareFormation(agentIndex, totalAgents, destination, true);
                    break;
                case FormationType.HollowCircle:
                    formationPos = CalculateCircleFormation(agentIndex, totalAgents, destination, true);
                    break;
                case FormationType.HollowTriangle:
                    formationPos = CalculateTriangleFormation(agentIndex, totalAgents, destination, true);
                    break;
                case FormationType.HollowRectangle:
                    formationPos = CalculateRectangleFormation(agentIndex, totalAgents, destination, true);
                    break;
            }

            // Apply rotation to the formation position (rotate around center point)
            if (rotation != Quaternion.identity)
            {
                Vector3 offset = formationPos - destination;
                formationPos = destination + rotation * offset;
            }

            if (TrySamplePosition(formationPos, _settings.formationSize, navigationAgent, out Vector3 sampledFormationPos))
                return sampledFormationPos;

            if (CanUseUnsampledPosition(navigationAgent))
                return formationPos;

            // Fallback to random sampled position if the primary formation position is invalid.
            return GetRandomSampledPosition(destination, destinationSpread, navigationAgent);
        }

        /// <summary>
        /// Calculates a formation position using the given formation type and spacing, with formation size
        /// auto-computed from the agent count and spacing so that agents are never constrained into a
        /// too-small area. This is the recommended entry point for callers that do not have an explicit
        /// size constraint (e.g., RTS move commands, scripted formation moves).
        /// </summary>
        /// <param name="agentIndex">Index of the agent in the formation</param>
        /// <param name="totalAgents">Total number of agents in the formation</param>
        /// <param name="destination">Center point of the formation</param>
        /// <param name="formationType">Type of formation to use</param>
        /// <param name="spacing">Spacing between agents</param>
        /// <param name="rotation">Rotation to apply to the formation (rotates positions around center)</param>
        /// <returns>World position for the agent</returns>
        public Vector3 CalculateFormationPosition(
            int agentIndex,
            int totalAgents,
            Vector3 destination,
            FormationType formationType,
            float spacing,
            Quaternion rotation = default)
        {
            // Auto-compute formation size large enough that adaptive spacing is never triggered.
            // For hollow formations this becomes the bounding dimension, producing shapes that
            // scale naturally with agent count — consistent with how the spawn path behaves.
            float autoSize = spacing * Mathf.CeilToInt(Mathf.Sqrt(Mathf.Max(1, totalAgents))) * 2f;
            return CalculateFormationPosition(agentIndex, totalAgents, destination, autoSize, formationType, spacing, spacing, rotation, null);
        }

        /// <summary>
        /// Calculates a formation position with custom spacing and thickness parameters.
        /// Used for Formation Reformation to override default spawn settings.
        /// </summary>
        public Vector3 CalculateFormationPosition(
            int agentIndex, 
            int totalAgents, 
            Vector3 destination, 
            float formationSize,
            FormationType formationType,
            float spacing,
            float thickness)
        {
            return CalculateFormationPosition(agentIndex, totalAgents, destination, formationSize, formationType, spacing, thickness, Quaternion.identity, null);
        }

        /// <summary>
        /// Calculates a formation position with custom spacing and thickness parameters and optional backend-aware
        /// sampling through an <see cref="INavigationAgent"/>.
        /// Used for formation reformation to override default spawn settings.
        /// </summary>
        public Vector3 CalculateFormationPosition(
            int agentIndex,
            int totalAgents,
            Vector3 destination,
            float formationSize,
            FormationType formationType,
            float spacing,
            float thickness,
            INavigationAgent navigationAgent)
        {
            return CalculateFormationPosition(agentIndex, totalAgents, destination, formationSize, formationType, spacing, thickness, Quaternion.identity, navigationAgent);
        }

        /// <summary>
        /// Calculates a formation position with custom spacing and thickness parameters, rotation support,
        /// and optional backend-aware sampling through an <see cref="INavigationAgent"/>.
        /// </summary>
        public Vector3 CalculateFormationPosition(
            int agentIndex,
            int totalAgents,
            Vector3 destination,
            float formationSize,
            FormationType formationType,
            float spacing,
            float thickness,
            Quaternion rotation,
            INavigationAgent navigationAgent)
        {
            // Temporarily store original settings
            float originalSize = _settings.formationSize;
            float originalSpacing = _settings.formationSpacing;
            float originalThickness = _settings.formationThickness;
            
            // Override settings with reformation parameters
            _settings.formationSize = formationSize;
            _settings.formationSpacing = spacing;
            _settings.formationThickness = thickness;
            
            // Calculate position using the overridden settings
            Vector3 result = CalculateFormationPosition(agentIndex, totalAgents, destination, formationSize, rotation, navigationAgent, formationType);
            
            // Restore original settings
            _settings.formationSize = originalSize;
            _settings.formationSpacing = originalSpacing;
            _settings.formationThickness = originalThickness;
            
            return result;
        }

        #region Square Formation

        private Vector3 CalculateSquareFormation(int agentIndex, int totalAgents, Vector3 center, bool hollow)
        {
            if (hollow)
            {
                return CalculateHollowSquareFormation(agentIndex, totalAgents, center);
            }

            int sideLength = Mathf.CeilToInt(Mathf.Sqrt(totalAgents));
            int row = agentIndex / sideLength;
            int col = agentIndex % sideLength;

            // Adaptive spacing: automatically adjust spacing based on formation size and agent count
            // This ensures all agents fit within the formation size regardless of agent count
            float maxFormationDimension = (sideLength - 1) * _settings.formationSpacing;
            float adaptiveSpacing = _settings.formationSpacing;
            
            if (maxFormationDimension > _settings.formationSize)
            {
                // Scale down spacing to fit within formation size
                adaptiveSpacing = _settings.formationSize / (sideLength - 1);
                adaptiveSpacing = Mathf.Max(adaptiveSpacing, 0.5f); // Minimum spacing for agent collision
            }
            
            float halfSize = (sideLength - 1) * adaptiveSpacing * 0.5f;

            Vector3 offset = new Vector3(
                col * adaptiveSpacing - halfSize,
                0f,
                row * adaptiveSpacing - halfSize
            );

            return center + offset;
        }

        private Vector3 CalculateHollowSquareFormation(int agentIndex, int totalAgents, Vector3 center)
        {
            // Calculate perimeter positions for hollow square
            int perimeterAgents = Mathf.CeilToInt(Mathf.Sqrt(totalAgents)) * 4 - 4;
            float perimeterRatio = (float)agentIndex / totalAgents * perimeterAgents;
            int perimeterIndex = Mathf.FloorToInt(perimeterRatio);

            float sideLength = _settings.formationSize;
            float halfSize = sideLength * 0.5f;
            int agentsPerSide = Mathf.Max(1, perimeterAgents / 4);

            int side = perimeterIndex / agentsPerSide;
            int positionOnSide = perimeterIndex % agentsPerSide;
            float t = agentsPerSide > 1 ? (float)positionOnSide / (agentsPerSide - 1) : 0f;

            Vector3 offset = Vector3.zero;
            switch (side % 4)
            {
                case 0: // Top side
                    offset = new Vector3(Mathf.Lerp(-halfSize, halfSize, t), 0f, halfSize);
                    break;
                case 1: // Right side
                    offset = new Vector3(halfSize, 0f, Mathf.Lerp(halfSize, -halfSize, t));
                    break;
                case 2: // Bottom side
                    offset = new Vector3(Mathf.Lerp(halfSize, -halfSize, t), 0f, -halfSize);
                    break;
                case 3: // Left side
                    offset = new Vector3(-halfSize, 0f, Mathf.Lerp(-halfSize, halfSize, t));
                    break;
            }

            return center + offset;
        }

        #endregion

        #region Circle Formation

        private Vector3 CalculateCircleFormation(int agentIndex, int totalAgents, Vector3 center, bool hollow)
        {
            if (hollow)
            {
                // Hollow circle - agents only on the perimeter
                float angle = 2f * Mathf.PI * agentIndex / totalAgents;
                float radius = _settings.formationSize * 0.5f;
                Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                return center + offset;
            }
            else
            {
                // Filled circle - use concentric rings
                int ring = 0;
                int agentInRing = agentIndex;
                int agentsInCurrentRing = 1; // Center has 1 agent

                // Find which ring this agent belongs to
                while (agentInRing >= agentsInCurrentRing)
                {
                    agentInRing -= agentsInCurrentRing;
                    ring++;
                    agentsInCurrentRing = ring == 0 ? 1 : Mathf.CeilToInt(2f * Mathf.PI * ring);
                }

                if (ring == 0)
                {
                    return center; // Center position
                }

                float angle = 2f * Mathf.PI * agentInRing / agentsInCurrentRing;
                float radius = ring * _settings.formationSpacing;
                Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                return center + offset;
            }
        }

        #endregion

        #region Triangle Formation

        private Vector3 CalculateTriangleFormation(int agentIndex, int totalAgents, Vector3 center, bool hollow)
        {
            if (hollow)
            {
                return CalculateHollowTriangleFormation(agentIndex, totalAgents, center);
            }

            // Filled triangle formation
            int row = 0;
            int agentInRow = agentIndex;
            int agentsInCurrentRow = 1;

            // Find which row this agent belongs to
            while (agentInRow >= agentsInCurrentRow)
            {
                agentInRow -= agentsInCurrentRow;
                row++;
                agentsInCurrentRow = row + 1;
            }

            float spacing = _settings.formationSpacing;
            float rowOffset = row * spacing;
            float colOffset = (agentInRow - (agentsInCurrentRow - 1) * 0.5f) * spacing;

            Vector3 offset = new Vector3(colOffset, 0f, -rowOffset);
            return center + offset;
        }

        private Vector3 CalculateHollowTriangleFormation(int agentIndex, int totalAgents, Vector3 center)
        {
            // Calculate perimeter positions for hollow triangle
            float height = _settings.formationSize * Mathf.Sqrt(3f) / 2f;
            float sideLength = _settings.formationSize;

            // Three vertices of the triangle
            Vector3[] vertices = {
                new Vector3(0f, 0f, height * 2f / 3f),                    // Top
                new Vector3(-sideLength / 2f, 0f, -height / 3f),          // Bottom left
                new Vector3(sideLength / 2f, 0f, -height / 3f)            // Bottom right
            };

            int agentsPerSide = Mathf.Max(1, totalAgents / 3);
            int side = agentIndex / agentsPerSide;
            int positionOnSide = agentIndex % agentsPerSide;
            float t = agentsPerSide > 1 ? (float)positionOnSide / (agentsPerSide - 1) : 0f;

            Vector3 startVertex = vertices[side % 3];
            Vector3 endVertex = vertices[(side + 1) % 3];
            Vector3 offset = Vector3.Lerp(startVertex, endVertex, t);

            return center + offset;
        }

        #endregion

        #region Rectangle Formation

        private Vector3 CalculateRectangleFormation(int agentIndex, int totalAgents, Vector3 center, bool hollow)
        {
            if (hollow)
            {
                return CalculateHollowRectangleFormation(agentIndex, totalAgents, center);
            }

            // Filled rectangle - use aspect ratio of 2:1 (width:height)
            float aspectRatio = 2f;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(totalAgents * aspectRatio));
            int rows = Mathf.CeilToInt((float)totalAgents / cols);

            int row = agentIndex / cols;
            int col = agentIndex % cols;

            float spacing = _settings.formationSpacing;
            float halfWidth = (cols - 1) * spacing * 0.5f;
            float halfHeight = (rows - 1) * spacing * 0.5f;

            Vector3 offset = new Vector3(
                col * spacing - halfWidth,
                0f,
                row * spacing - halfHeight
            );

            return center + offset;
        }

        private Vector3 CalculateHollowRectangleFormation(int agentIndex, int totalAgents, Vector3 center)
        {
            // Calculate perimeter positions for hollow rectangle
            float width = _settings.formationSize;
            float height = _settings.formationSize * 0.6f; // Rectangular aspect ratio

            int agentsPerSide = Mathf.Max(1, totalAgents / 4);

            int side = agentIndex / agentsPerSide;
            int positionOnSide = agentIndex % agentsPerSide;
            float t = agentsPerSide > 1 ? (float)positionOnSide / (agentsPerSide - 1) : 0f;

            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;

            Vector3 offset = Vector3.zero;
            switch (side % 4)
            {
                case 0: // Top side
                    offset = new Vector3(Mathf.Lerp(-halfWidth, halfWidth, t), 0f, halfHeight);
                    break;
                case 1: // Right side
                    offset = new Vector3(halfWidth, 0f, Mathf.Lerp(halfHeight, -halfHeight, t));
                    break;
                case 2: // Bottom side
                    offset = new Vector3(Mathf.Lerp(halfWidth, -halfWidth, t), 0f, -halfHeight);
                    break;
                case 3: // Left side
                    offset = new Vector3(-halfWidth, 0f, Mathf.Lerp(-halfHeight, halfHeight, t));
                    break;
            }

            return center + offset;
        }

        #endregion

        #region 3D Formations (Layered)

        /// <summary>
        /// Calculates a 3D layered formation position for flying agents.
        /// Distributes agents across multiple vertical layers, each layer being a standard 2D formation.
        /// 
        /// Network-compatible: deterministic given the same inputs.
        /// </summary>
        /// <param name="agentIndex">Index of the agent in the formation (0-based).</param>
        /// <param name="totalAgents">Total number of agents in the formation.</param>
        /// <param name="destination">Center point of the formation (ground-level reference).</param>
        /// <param name="layers">Number of vertical layers. 1 = standard 2D formation.</param>
        /// <param name="layerSpacing">Vertical distance between layers (world units).</param>
        /// <param name="baseAltitude">Base altitude offset above the destination Y coordinate.</param>
        /// <param name="formationType">2D formation type used within each layer.</param>
        /// <param name="spacing">Horizontal spacing between agents within a layer.</param>
        /// <param name="rotation">Optional rotation applied to the entire formation.</param>
        /// <returns>3D world position for the agent.</returns>
        public Vector3 CalculateFormationPosition3D(
            int agentIndex,
            int totalAgents,
            Vector3 destination,
            int layers,
            float layerSpacing,
            float baseAltitude,
            FormationType formationType,
            float spacing,
            Quaternion rotation = default)
        {
            layers = Mathf.Max(1, layers);

            if (layers == 1)
            {
                // Single layer — standard 2D formation at base altitude
                Vector3 pos2D = CalculateFormationPosition(agentIndex, totalAgents, destination, formationType, spacing, rotation);
                pos2D.y = destination.y + baseAltitude;
                return pos2D;
            }

            // Distribute agents evenly across layers
            int agentsPerLayer = Mathf.CeilToInt((float)totalAgents / layers);
            int layer = agentIndex / agentsPerLayer;
            int indexInLayer = agentIndex % agentsPerLayer;

            // Agents in the last layer might be fewer
            int agentsInThisLayer = (layer == layers - 1)
                ? totalAgents - (agentsPerLayer * (layers - 1))
                : agentsPerLayer;

            agentsInThisLayer = Mathf.Max(1, agentsInThisLayer);
            indexInLayer = Mathf.Min(indexInLayer, agentsInThisLayer - 1);

            // Calculate 2D position within this layer
            Vector3 layerCenter = destination;
            Vector3 pos = CalculateFormationPosition(indexInLayer, agentsInThisLayer, layerCenter, formationType, spacing, rotation);

            // Apply vertical offset: center the layers around baseAltitude
            float totalHeight = (layers - 1) * layerSpacing;
            float layerAltitude = baseAltitude - (totalHeight * 0.5f) + (layer * layerSpacing);
            pos.y = destination.y + layerAltitude;

            return pos;
        }

        /// <summary>
        /// Convenience overload that reads layer configuration directly from an EnemyCrowd.
        /// </summary>
        public Vector3 CalculateFormationPosition3D(
            int agentIndex,
            int totalAgents,
            Vector3 destination,
            EnemyCrowd crowd,
            FormationType formationType,
            float spacing,
            Quaternion rotation = default)
        {
            return CalculateFormationPosition3D(
                agentIndex,
                totalAgents,
                destination,
                crowd.formationLayers,
                crowd.formationLayerSpacing,
                crowd.cruisingAltitude,
                formationType,
                spacing,
                rotation);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Gets a random sampled position within range of the origin.
        /// Uses the provided navigation adapter when available.
        /// Backward-compatibility alias for previous NavMesh-focused callers.
        /// </summary>
        public Vector3 GetRandomNavMeshPosition(Vector3 origin, float range)
        {
            return GetRandomSampledPosition(origin, range, null);
        }

        /// <summary>
        /// Gets a random sampled position within range of the origin, using the provided navigation adapter
        /// when available.
        /// </summary>
        public Vector3 GetRandomSampledPosition(Vector3 origin, float range, INavigationAgent navigationAgent)
        {
            Vector3 fallbackRandomPoint = origin;
            for (int i = 0; i < 30; i++)
            {
                Vector3 randomPoint = origin + Random.insideUnitSphere * range;
                randomPoint.y = origin.y;
                fallbackRandomPoint = randomPoint;

                if (TrySamplePosition(randomPoint, range, navigationAgent, out Vector3 sampledPosition))
                    return sampledPosition;
            }

            if (CanUseUnsampledPosition(navigationAgent))
                return fallbackRandomPoint;

            // Fallback: return origin when this backend requires explicit surface validation and no sample was found.
            return origin;
        }

        private static bool TrySamplePosition(
            Vector3 position,
            float maxDistance,
            INavigationAgent navigationAgent,
            out Vector3 sampledPosition)
        {
            if (navigationAgent != null)
            {
                if (navigationAgent.SamplePosition(position, out sampledPosition, maxDistance))
                    return true;

                sampledPosition = position;
                return false;
            }

            return EnemyMassesNavigationBackendHooks.TrySampleNavMeshPosition(position, maxDistance, out sampledPosition);
        }

        private static bool CanUseUnsampledPosition(INavigationAgent navigationAgent)
        {
            if (navigationAgent == null)
                return true;

            return !navigationAgent.Capabilities.RequiresSurfaceValidationForControl;
        }

        #endregion
    }
}
