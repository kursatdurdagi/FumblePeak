using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Navigation.Abstraction
{
    /// <summary>
    /// Factory for creating and managing INavigationAgent instances.
    /// Provides auto-detection and type-specific creation methods.
    /// Uses reflection to support A* Pathfinding when available without requiring compile-time dependencies.
    /// </summary>
    public static class NavigationAgentFactory
    {
#if ASTAR_EXISTS
        // Cached type references for A* components (resolved via reflection)
        private static Type _aiPathType;
        private static Type _richAIType;
        private static Type _aiLerpType;
        private static Type _rvoControllerType;
        private static Type _aiPathAdapterType;
        private static Type _richAIAdapterType;
        private static Type _aiLerpAdapterType;
        private static Type _rvoAdapterType;
        private static bool _astarTypesResolved;
#endif
        
        // Track which unknown navigation types we've already warned about to avoid log spam
        private static readonly HashSet<int> _warnedUnknownTypes = new HashSet<int>();
        
        /// <summary>
        /// Gets the list of navigation types available in the current build.
        /// Useful for populating dropdowns in the inspector.
        /// </summary>
        public static NavigationType[] GetAvailableTypes()
        {
            var types = new List<NavigationType>
            {
                NavigationType.UnityNavMesh,
                NavigationType.FlowField,
                NavigationType.Flight3D
            };
            
#if ASTAR_EXISTS
            // Check if A* is available via reflection
            ResolveAStarTypes();
            if (_aiPathType != null)
            {
                types.Add(NavigationType.AStarAIPath);
                types.Add(NavigationType.AStarRichAI);
                types.Add(NavigationType.AStarAILerp);
                
                if (_rvoControllerType != null && _rvoAdapterType != null)
                    types.Add(NavigationType.AStarRVO);
            }
#endif
            
            return types.ToArray();
        }
        
        /// <summary>
        /// Gets display names for all available navigation types.
        /// </summary>
        public static string[] GetAvailableTypeNames()
        {
            var availableTypes = GetAvailableTypes();
            var names = new string[availableTypes.Length];
            for (int i = 0; i < availableTypes.Length; i++)
            {
                names[i] = availableTypes[i].GetDisplayName();
            }
            return names;
        }
        
        /// <summary>
        /// Attempts to get an existing INavigationAgent from the GameObject.
        /// Returns null if no compatible agent is found.
        /// </summary>
        public static INavigationAgent GetExistingAgent(GameObject gameObject)
        {
            if (gameObject == null) return null;
            
            // First check if there's already an adapter
            var existingAdapter = gameObject.GetComponent<INavigationAgent>();
            if (existingAdapter != null)
                return existingAdapter;
            
            return null;
        }
        
        /// <summary>
        /// Auto-detects and creates the appropriate adapter based on existing components.
        /// If no navigation component exists, creates a NavMeshAgent with adapter.
        /// </summary>
        public static INavigationAgent GetOrCreateAgent(GameObject gameObject)
        {
            if (gameObject == null)
                throw new ArgumentNullException(nameof(gameObject));
            
            // First check for existing adapter
            var existingAgent = GetExistingAgent(gameObject);
            if (existingAgent != null)
                return existingAgent;
            
            // Auto-detect based on existing components
            var detectedType = AutoDetectNavigationType(gameObject);
            
            return CreateAgent(gameObject, detectedType);
        }
        
        /// <summary>
        /// Creates a navigation agent of the specified type.
        /// Adds necessary components if they don't exist.
        /// </summary>
        public static INavigationAgent CreateAgent(GameObject gameObject, NavigationType type)
        {
            if (gameObject == null)
                throw new ArgumentNullException(nameof(gameObject));
            
            // Remove any existing adapters first
            RemoveExistingAdapters(gameObject);
            
            switch (type)
            {
                case NavigationType.UnityNavMesh:
                    return CreateNavMeshAgent(gameObject);

                case NavigationType.FlowField:
                    return CreateFlowFieldAgent(gameObject);

                case NavigationType.Flight3D:
                    return CreateFlight3DAgent(gameObject);
                    
#if ASTAR_EXISTS
                case NavigationType.AStarAIPath:
                    ResolveAStarTypes();
                    return CreateAStarAgent(gameObject, _aiPathType, _aiPathAdapterType, "AIPath");
                    
                case NavigationType.AStarRichAI:
                    ResolveAStarTypes();
                    return CreateAStarAgent(gameObject, _richAIType, _richAIAdapterType, "RichAI");
                    
                case NavigationType.AStarAILerp:
                    ResolveAStarTypes();
                    return CreateAStarAgent(gameObject, _aiLerpType, _aiLerpAdapterType, "AILerp");
                    
                case NavigationType.AStarRVO:
                    ResolveAStarTypes();
                    var rvoComponentType = GetPreferredAStarComponentForRvo(gameObject);
                    return CreateAStarAgent(gameObject, rvoComponentType, _rvoAdapterType, "RVO");
#endif
                    
                default:
                    // Only warn once per unknown type to avoid log spam
                    int typeValue = (int)type;
                    if (!_warnedUnknownTypes.Contains(typeValue))
                    {
                        _warnedUnknownTypes.Add(typeValue);
                        Debug.LogWarning($"[NavigationAgentFactory] Navigation type {typeValue} is not available (A* Pathfinding not installed?). " +
                            "Falling back to Unity NavMesh. This warning will only appear once per type.");
                    }
                    return CreateNavMeshAgent(gameObject);
            }
        }
        
        /// <summary>
        /// Auto-detects the navigation type based on existing components.
        /// </summary>
        public static NavigationType AutoDetectNavigationType(GameObject gameObject)
        {
            if (gameObject == null) return NavigationType.UnityNavMesh;

            if (gameObject.GetComponent<Adapters.FlowFieldAgentAdapter>() != null)
                return NavigationType.FlowField;
            
#if ASTAR_EXISTS
            ResolveAStarTypes();
            
            // Prefer RVO if RVOController is present alongside an AIPath/RichAI component
            if (_rvoControllerType != null && gameObject.GetComponent(_rvoControllerType) != null)
            {
                if (_aiPathType != null && gameObject.GetComponent(_aiPathType) != null)
                    return NavigationType.AStarRVO;
                
                if (_richAIType != null && gameObject.GetComponent(_richAIType) != null)
                    return NavigationType.AStarRVO;
            }
            
            // Check for A* components first (more specific)
            if (_aiPathType != null && gameObject.GetComponent(_aiPathType) != null)
                return NavigationType.AStarAIPath;
            
            if (_richAIType != null && gameObject.GetComponent(_richAIType) != null)
                return NavigationType.AStarRichAI;
            
            if (_aiLerpType != null && gameObject.GetComponent(_aiLerpType) != null)
                return NavigationType.AStarAILerp;
#endif
            
            // Default to Unity NavMesh
            return NavigationType.UnityNavMesh;
        }
        
        /// <summary>
        /// Checks if the specified navigation type is available in the current build.
        /// </summary>
        public static bool IsTypeAvailable(NavigationType type)
        {
            switch (type)
            {
                case NavigationType.UnityNavMesh:
                case NavigationType.FlowField:
                    return true;
                    
#if ASTAR_EXISTS
                case NavigationType.AStarAIPath:
                case NavigationType.AStarRichAI:
                case NavigationType.AStarAILerp:
                case NavigationType.AStarRVO:
                    ResolveAStarTypes();
                    if (type == NavigationType.AStarRVO)
                        return _rvoControllerType != null && _rvoAdapterType != null && (_aiPathType != null || _richAIType != null);
                    return _aiPathType != null;
#endif
                    
                default:
                    return false;
            }
        }
        
        #region Private Methods
        
#if ASTAR_EXISTS
        /// <summary>
        /// Resolves A* types via reflection. Only done once.
        /// </summary>
        private static void ResolveAStarTypes()
        {
            if (_astarTypesResolved) return;
            _astarTypesResolved = true;
            
            try
            {
                // Try to find A* Pathfinding types
                _aiPathType = Type.GetType("Pathfinding.AIPath, AstarPathfindingProject");
                _richAIType = Type.GetType("Pathfinding.RichAI, AstarPathfindingProject");
                _aiLerpType = Type.GetType("Pathfinding.AILerp, AstarPathfindingProject");
                _rvoControllerType = Type.GetType("Pathfinding.RVO.RVOController, AstarPathfindingProject");
                
                // Try to find adapter types
                _aiPathAdapterType = Type.GetType("Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters.AStarAIPathAdapter, Arawn.EnemyMasses.Runtime.AStarPathfinding");
                _richAIAdapterType = Type.GetType("Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters.AStarRichAIAdapter, Arawn.EnemyMasses.Runtime.AStarPathfinding");
                _aiLerpAdapterType = Type.GetType("Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters.AStarAILerpAdapter, Arawn.EnemyMasses.Runtime.AStarPathfinding");
                _rvoAdapterType = Type.GetType("Arawn.EnemyMasses.Runtime.Navigation.Abstraction.Adapters.AStarRVOAdapter, Arawn.EnemyMasses.Runtime.AStarPathfinding");
                
                if (_aiPathType == null)
                {
                    Debug.Log("[NavigationAgentFactory] A* Pathfinding Project not detected. Only Unity NavMesh will be available.");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NavigationAgentFactory] Error resolving A* types: {e.Message}");
            }
        }
#endif
        
        private static INavigationAgent CreateNavMeshAgent(GameObject gameObject)
        {
            // Ensure NavMeshAgent exists
            var navMeshAgent = gameObject.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (navMeshAgent == null)
                navMeshAgent = gameObject.AddComponent<UnityEngine.AI.NavMeshAgent>();
            else if (!navMeshAgent.enabled)
                navMeshAgent.enabled = true;
            
            // Add adapter
            var adapter = gameObject.GetComponent<Adapters.NavMeshAgentAdapter>();
            if (adapter == null)
                adapter = gameObject.AddComponent<Adapters.NavMeshAgentAdapter>();
            
            return adapter;
        }

        private static INavigationAgent CreateFlowFieldAgent(GameObject gameObject)
        {
            DisableConflictingNavigationComponentsForFlowField(gameObject);

            var adapter = gameObject.GetComponent<Adapters.FlowFieldAgentAdapter>();
            if (adapter == null)
                adapter = gameObject.AddComponent<Adapters.FlowFieldAgentAdapter>();

            return adapter;
        }

        private static INavigationAgent CreateFlight3DAgent(GameObject gameObject)
        {
            // Disable ground-based navigation components that would conflict
            DisableConflictingNavigationComponentsForFlowField(gameObject);

            var adapter = gameObject.GetComponent<Navigation.Flight3DNavigationAgent>();
            if (adapter == null)
                adapter = gameObject.AddComponent<Navigation.Flight3DNavigationAgent>();

            return adapter;
        }

        private static void DisableConflictingNavigationComponentsForFlowField(GameObject gameObject)
        {
            var navMeshAgent = gameObject.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (navMeshAgent != null && navMeshAgent.enabled)
                navMeshAgent.enabled = false;

#if ASTAR_EXISTS
            ResolveAStarTypes();

            DisableBehaviourComponent(gameObject, _aiPathType);
            DisableBehaviourComponent(gameObject, _richAIType);
            DisableBehaviourComponent(gameObject, _aiLerpType);
            DisableBehaviourComponent(gameObject, _rvoControllerType);
#endif
        }

#if ASTAR_EXISTS
        private static void DisableBehaviourComponent(GameObject gameObject, Type componentType)
        {
            if (gameObject == null || componentType == null)
                return;

            var component = gameObject.GetComponent(componentType);
            if (component is Behaviour behaviour && behaviour.enabled)
                behaviour.enabled = false;
        }
#endif
        
#if ASTAR_EXISTS
        private static INavigationAgent CreateAStarAgent(GameObject gameObject, Type componentType, Type adapterType, string componentName)
        {
            if (componentType == null || adapterType == null)
            {
                Debug.LogError($"[NavigationAgentFactory] A* Pathfinding types not available. Cannot create {componentName} agent. Falling back to NavMesh.");
                return CreateNavMeshAgent(gameObject);
            }
            
            // Ensure the A* component exists
            var existingComponent = gameObject.GetComponent(componentType);
            if (existingComponent == null)
                gameObject.AddComponent(componentType);
            else if (existingComponent is Behaviour behaviour && !behaviour.enabled)
                behaviour.enabled = true;
            
            // Add adapter
            var existingAdapter = gameObject.GetComponent(adapterType) as INavigationAgent;
            if (existingAdapter == null)
                existingAdapter = gameObject.AddComponent(adapterType) as INavigationAgent;
            
            return existingAdapter;
        }
#endif
        
        private static void RemoveExistingAdapters(GameObject gameObject)
        {
            // Remove NavMesh adapter
            var navMeshAdapter = gameObject.GetComponent<Adapters.NavMeshAgentAdapter>();
            if (navMeshAdapter != null)
                UnityEngine.Object.DestroyImmediate(navMeshAdapter);

            var flowFieldAdapter = gameObject.GetComponent<Adapters.FlowFieldAgentAdapter>();
            if (flowFieldAdapter != null)
                UnityEngine.Object.DestroyImmediate(flowFieldAdapter);
            
#if ASTAR_EXISTS
            // Remove A* adapters via reflection
            ResolveAStarTypes();
            
            if (_aiPathAdapterType != null)
            {
                var adapter = gameObject.GetComponent(_aiPathAdapterType);
                if (adapter != null)
                    UnityEngine.Object.DestroyImmediate(adapter);
            }
            
            if (_richAIAdapterType != null)
            {
                var adapter = gameObject.GetComponent(_richAIAdapterType);
                if (adapter != null)
                    UnityEngine.Object.DestroyImmediate(adapter);
            }
            
            if (_aiLerpAdapterType != null)
            {
                var adapter = gameObject.GetComponent(_aiLerpAdapterType);
                if (adapter != null)
                    UnityEngine.Object.DestroyImmediate(adapter);
            }
            
            if (_rvoAdapterType != null)
            {
                var adapter = gameObject.GetComponent(_rvoAdapterType);
                if (adapter != null)
                    UnityEngine.Object.DestroyImmediate(adapter);
            }
#endif
        }
        
        #if ASTAR_EXISTS
        private static Type GetPreferredAStarComponentForRvo(GameObject gameObject)
        {
            if (gameObject == null) return _aiPathType;
            
            // Prefer RichAI if it's already present (navmesh/recast graphs)
            if (_richAIType != null && gameObject.GetComponent(_richAIType) != null)
                return _richAIType;
            
            // Otherwise use AIPath if present (grid/point graphs)
            if (_aiPathType != null && gameObject.GetComponent(_aiPathType) != null)
                return _aiPathType;
            
            // Default to AIPath if available
            if (_aiPathType != null)
                return _aiPathType;
            
            // Fallback to RichAI if AIPath is unavailable
            return _richAIType;
        }
        #endif
        
        #endregion
    }
}
