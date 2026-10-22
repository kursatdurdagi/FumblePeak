using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Where to call the method from.
    /// </summary>
    public enum MethodCallTarget
    {
        Caster,
        PrimaryTarget,
        Controller,
        TargetByName,
        TargetByTag,
        SingletonType
    }

    /// <summary>
    /// Optional payload passed to the method.
    /// </summary>
    public enum MethodCallParameter
    {
        None,
        SkillContext,
        CasterAgent,
        PrimaryTargetAgent,
        AllTargets,
        Controller,
        TargetPosition,
        IsCritical,
        EffectMultiplier,
        ExecutionId,
        NetworkProjectileId
    }

    /// <summary>
    /// Calls a public method on scene components or on a singleton instance.
    /// Useful when a ScriptableObject skill needs to trigger scene-driven logic.
    /// </summary>
    [Serializable]
    public class InvokeMethodAction : SkillAction
    {
        public override bool RequiresServerAuthority => false;

        [Header("Target")]
        [SerializeField] private MethodCallTarget target = MethodCallTarget.Caster;

        [Tooltip("Used when Target = TargetByName")]
        [SerializeField] private string targetObjectName = string.Empty;

        [Tooltip("Used when Target = TargetByTag")]
        [SerializeField] private string targetTag = "Untagged";

        [Tooltip("Used when Target = SingletonType. Full type name preferred, e.g. Arawn.EnemyMasses.Runtime.CrowdAudioManager")]
        [SerializeField] private string singletonTypeName = string.Empty;

        [Tooltip("Optional. If set, only this component type on the target object is searched.\n" +
                 "If empty, all MonoBehaviours on the target object are searched.")]
        [SerializeField] private string componentTypeName = string.Empty;

        [Header("Method")]
        [SerializeField] private string methodName = string.Empty;

        [SerializeField] private MethodCallParameter parameter = MethodCallParameter.None;

        [Tooltip("If true, logs warnings when target/method resolution fails.")]
        [SerializeField] private bool logWarnings = true;

        public override void Execute(SkillExecutionContext context)
        {
            if (context == null || string.IsNullOrWhiteSpace(methodName))
                return;

            object payload = ResolvePayload(context, parameter);
            bool wantsArg = parameter != MethodCallParameter.None;

            switch (target)
            {
                case MethodCallTarget.SingletonType:
                    InvokeSingleton(singletonTypeName, methodName, payload, wantsArg, logWarnings);
                    break;

                default:
                    GameObject go = ResolveSceneTarget(context);
                    if (go == null)
                    {
                        if (logWarnings)
                        {
                            Debug.LogWarning($"[InvokeMethodAction] Target GameObject not found for target={target}.");
                        }
                        return;
                    }

                    if (!InvokeOnGameObject(go, componentTypeName, methodName, payload, wantsArg, logWarnings))
                    {
                        if (logWarnings)
                        {
                            Debug.LogWarning($"[InvokeMethodAction] Method '{methodName}' not found on target '{go.name}'.");
                        }
                    }
                    break;
            }
        }

        public override string GetDescription()
        {
            return $"Invoke '{methodName}' on {target}";
        }

        private GameObject ResolveSceneTarget(SkillExecutionContext context)
        {
            switch (target)
            {
                case MethodCallTarget.Caster:
                    return context.Caster?.transform?.gameObject;

                case MethodCallTarget.PrimaryTarget:
                    return context.PrimaryTarget?.transform?.gameObject;

                case MethodCallTarget.Controller:
                    return context.Controller != null ? context.Controller.gameObject : null;

                case MethodCallTarget.TargetByName:
                    return string.IsNullOrWhiteSpace(targetObjectName)
                        ? null
                        : GameObject.Find(targetObjectName);

                case MethodCallTarget.TargetByTag:
                    if (string.IsNullOrWhiteSpace(targetTag))
                        return null;

                    try
                    {
                        return GameObject.FindGameObjectWithTag(targetTag);
                    }
                    catch
                    {
                        return null;
                    }

                default:
                    return null;
            }
        }

        private static object ResolvePayload(SkillExecutionContext context, MethodCallParameter payload)
        {
            switch (payload)
            {
                case MethodCallParameter.SkillContext:
                    return context;
                case MethodCallParameter.CasterAgent:
                    return context.Caster;
                case MethodCallParameter.PrimaryTargetAgent:
                    return context.PrimaryTarget;
                case MethodCallParameter.AllTargets:
                    return context.AllTargets;
                case MethodCallParameter.Controller:
                    return context.Controller;
                case MethodCallParameter.TargetPosition:
                    return context.TargetPosition;
                case MethodCallParameter.IsCritical:
                    return context.IsCritical;
                case MethodCallParameter.EffectMultiplier:
                    return context.EffectMultiplier;
                case MethodCallParameter.ExecutionId:
                    return context.ExecutionId;
                case MethodCallParameter.NetworkProjectileId:
                    return context.NetworkProjectileId;
                case MethodCallParameter.None:
                default:
                    return null;
            }
        }

        private static bool InvokeOnGameObject(
            GameObject targetObject,
            string componentTypeName,
            string method,
            object arg,
            bool wantsArg,
            bool logWarn)
        {
            if (targetObject == null)
                return false;

            if (!string.IsNullOrWhiteSpace(componentTypeName))
            {
                Type componentType = ResolveType(componentTypeName);
                if (componentType == null)
                {
                    if (logWarn)
                    {
                        Debug.LogWarning($"[InvokeMethodAction] Component type not found: '{componentTypeName}'");
                    }
                    return false;
                }

                if (!typeof(Component).IsAssignableFrom(componentType))
                {
                    if (logWarn)
                    {
                        Debug.LogWarning($"[InvokeMethodAction] Type '{componentType.FullName}' is not a Component.");
                    }
                    return false;
                }

                Component component = targetObject.GetComponent(componentType);
                if (component == null)
                {
                    if (logWarn)
                    {
                        Debug.LogWarning($"[InvokeMethodAction] Component '{componentType.Name}' not found on '{targetObject.name}'.");
                    }
                    return false;
                }

                return TryInvoke(component, method, arg, wantsArg, BindingFlags.Instance | BindingFlags.Public);
            }

            var components = targetObject.GetComponents<MonoBehaviour>();
            for (int i = 0; i < components.Length; i++)
            {
                MonoBehaviour mb = components[i];
                if (mb == null) continue;

                if (TryInvoke(mb, method, arg, wantsArg, BindingFlags.Instance | BindingFlags.Public))
                    return true;
            }

            return false;
        }

        private static void InvokeSingleton(
            string typeName,
            string method,
            object arg,
            bool wantsArg,
            bool logWarn)
        {
            Type singletonType = ResolveType(typeName);
            if (singletonType == null)
            {
                if (logWarn)
                {
                    Debug.LogWarning($"[InvokeMethodAction] Singleton type not found: '{typeName}'");
                }
                return;
            }

            object instance = ResolveSingletonInstance(singletonType);
            if (instance != null)
            {
                if (!TryInvoke(instance, method, arg, wantsArg, BindingFlags.Instance | BindingFlags.Public) && logWarn)
                {
                    Debug.LogWarning($"[InvokeMethodAction] Public instance method '{method}' not found on singleton '{singletonType.FullName}'.");
                }
                return;
            }

            bool staticInvoked = TryInvoke(
                singletonType,
                method,
                arg,
                wantsArg,
                BindingFlags.Static | BindingFlags.Public
            );

            if (!staticInvoked && logWarn)
            {
                Debug.LogWarning($"[InvokeMethodAction] Singleton instance for '{singletonType.FullName}' was null and static method '{method}' was not found.");
            }
        }

        private static object ResolveSingletonInstance(Type singletonType)
        {
            const BindingFlags staticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            PropertyInfo instanceProperty = singletonType.GetProperty("Instance", staticFlags)
                ?? singletonType.GetProperty("instance", staticFlags);

            if (instanceProperty != null)
            {
                return instanceProperty.GetValue(null);
            }

            FieldInfo instanceField = singletonType.GetField("Instance", staticFlags)
                ?? singletonType.GetField("instance", staticFlags);

            if (instanceField != null)
            {
                return instanceField.GetValue(null);
            }

            if (typeof(Component).IsAssignableFrom(singletonType))
            {
                return UnityEngine.Object.FindFirstObjectByType(singletonType);
            }

            return null;
        }

        private static Type ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            // Fast path for assembly-qualified names and fully-qualified names that Type.GetType can resolve.
            Type direct = Type.GetType(typeName, false);
            if (direct != null)
                return direct;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly asm = assemblies[i];

                Type fullMatch = asm.GetType(typeName, false);
                if (fullMatch != null)
                    return fullMatch;

                Type nameMatch = null;
                try
                {
                    nameMatch = asm.GetTypes().FirstOrDefault(t => t.Name == typeName);
                }
                catch
                {
                    // Ignore dynamic/reflection-restricted assemblies.
                }

                if (nameMatch != null)
                    return nameMatch;
            }

            return null;
        }

        private static bool TryInvoke(object targetOrType, string method, object arg, bool wantsArg, BindingFlags flags)
        {
            if (targetOrType == null || string.IsNullOrWhiteSpace(method))
                return false;

            Type targetType = targetOrType as Type ?? targetOrType.GetType();

            if (!wantsArg)
            {
                MethodInfo noArg = targetType.GetMethod(method, flags, null, Type.EmptyTypes, null);
                if (noArg == null)
                    return false;

                noArg.Invoke(targetOrType is Type ? null : targetOrType, null);
                return true;
            }

            Type argType = arg?.GetType();

            if (argType != null)
            {
                MethodInfo exact = targetType.GetMethod(method, flags, null, new[] { argType }, null);
                if (exact != null)
                {
                    exact.Invoke(targetOrType is Type ? null : targetOrType, new[] { arg });
                    return true;
                }
            }

            MethodInfo compatible = null;
            MethodInfo[] methods = targetType.GetMethods(flags);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (candidate.Name != method)
                    continue;

                var parameters = candidate.GetParameters();
                if (parameters.Length != 1)
                    continue;

                Type parameterType = parameters[0].ParameterType;
                if (arg == null)
                {
                    if (!parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) != null)
                    {
                        compatible = candidate;
                        break;
                    }
                }
                else if (parameterType.IsInstanceOfType(arg) || parameterType == typeof(object))
                {
                    compatible = candidate;
                    break;
                }
            }

            if (compatible == null)
                return false;

            compatible.Invoke(targetOrType is Type ? null : targetOrType, new[] { arg });
            return true;
        }
    }
}
