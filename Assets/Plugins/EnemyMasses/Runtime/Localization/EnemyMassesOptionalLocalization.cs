using System;
using System.Reflection;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Localization
{
    /// <summary>
    /// Optional localized string reference that resolves via Unity Localization when installed.
    /// Falls back to the source text when localization is unavailable or unresolved.
    /// </summary>
    [Serializable]
    public struct OptionalLocalizedString
    {
        [SerializeField]
        [Tooltip("Enable optional Unity Localization lookup for this value.")]
        private bool enabled;

        [SerializeField]
        [Tooltip("String Table Collection name.")]
        private string tableCollection;

        [SerializeField]
        [Tooltip("Entry key inside the String Table Collection.")]
        private string entryKey;

        [SerializeField]
        [Tooltip("If lookup fails, keep the source text instead of returning an empty string.")]
        private bool fallbackToSourceText;

        public bool Enabled => enabled;
        public string TableCollection => tableCollection;
        public string EntryKey => entryKey;

        public bool HasReference =>
            enabled &&
            !string.IsNullOrWhiteSpace(tableCollection) &&
            !string.IsNullOrWhiteSpace(entryKey);

        public string Resolve(string sourceText)
        {
            if (!HasReference)
                return sourceText;

            if (EnemyMassesLocalizationBridge.TryGetLocalizedString(tableCollection, entryKey, out string localized) &&
                !string.IsNullOrEmpty(localized))
            {
                return localized;
            }

            return fallbackToSourceText ? sourceText : string.Empty;
        }
    }

    /// <summary>
    /// Reflection-only bridge to Unity Localization to avoid hard compile-time dependency.
    /// Supports optional package integration when com.unity.localization is installed.
    /// </summary>
    public static class EnemyMassesLocalizationBridge
    {
        private const string LocalizationSettingsTypeName =
            "Unity.Localization.Settings.LocalizationSettings, Unity.Localization";

        private static bool _initialized;
        private static bool _isAvailable;
        private static object _stringDatabase;
        private static MethodInfo _getLocalizedStringMethod;

        public static bool IsAvailable
        {
            get
            {
                EnsureInitialized();
                return _isAvailable;
            }
        }

        public static bool TryGetLocalizedString(string tableCollection, string entryKey, out string localized)
        {
            localized = null;

            if (string.IsNullOrWhiteSpace(tableCollection) || string.IsNullOrWhiteSpace(entryKey))
                return false;

            EnsureInitialized();
            if (!_isAvailable || _stringDatabase == null || _getLocalizedStringMethod == null)
                return false;

            try
            {
                object[] args = BuildInvokeArguments(_getLocalizedStringMethod, tableCollection, entryKey);
                if (args == null)
                    return false;

                object value = _getLocalizedStringMethod.Invoke(_stringDatabase, args);
                if (value is string localizedString)
                {
                    localized = localizedString;
                    return true;
                }
            }
            catch
            {
                // Ignore and gracefully fallback.
            }

            return false;
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;
            _isAvailable = false;
            _stringDatabase = null;
            _getLocalizedStringMethod = null;

            Type localizationSettingsType = Type.GetType(LocalizationSettingsTypeName);
            if (localizationSettingsType == null)
                return;

            PropertyInfo stringDatabaseProperty = localizationSettingsType.GetProperty(
                "StringDatabase",
                BindingFlags.Public | BindingFlags.Static);
            if (stringDatabaseProperty == null)
                return;

            object database = stringDatabaseProperty.GetValue(null);
            if (database == null)
                return;

            MethodInfo method = FindGetLocalizedStringMethod(database.GetType());
            if (method == null)
                return;

            _stringDatabase = database;
            _getLocalizedStringMethod = method;
            _isAvailable = true;
        }

        private static MethodInfo FindGetLocalizedStringMethod(Type databaseType)
        {
            MethodInfo best = null;
            int bestScore = int.MaxValue;

            MethodInfo[] methods = databaseType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, "GetLocalizedString", StringComparison.Ordinal) ||
                    method.ReturnType != typeof(string))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 2)
                    continue;

                if (!CanCreateReferenceArgument(parameters[0].ParameterType) ||
                    !CanCreateReferenceArgument(parameters[1].ParameterType))
                {
                    continue;
                }

                int score = parameters.Length;
                if (score < bestScore)
                {
                    best = method;
                    bestScore = score;
                }
            }

            return best;
        }

        private static bool CanCreateReferenceArgument(Type parameterType)
        {
            if (parameterType == typeof(string))
                return true;

            if (parameterType.GetConstructor(new[] { typeof(string) }) != null)
                return true;

            MethodInfo implicitFromString = parameterType.GetMethod(
                "op_Implicit",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);
            return implicitFromString != null;
        }

        private static bool TryCreateReferenceArgument(Type parameterType, string value, out object argument)
        {
            if (parameterType == typeof(string))
            {
                argument = value;
                return true;
            }

            ConstructorInfo ctor = parameterType.GetConstructor(new[] { typeof(string) });
            if (ctor != null)
            {
                argument = ctor.Invoke(new object[] { value });
                return true;
            }

            MethodInfo implicitFromString = parameterType.GetMethod(
                "op_Implicit",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);
            if (implicitFromString != null)
            {
                argument = implicitFromString.Invoke(null, new object[] { value });
                return true;
            }

            argument = null;
            return false;
        }

        private static object[] BuildInvokeArguments(MethodInfo method, string tableCollection, string entryKey)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length < 2)
                return null;

            var args = new object[parameters.Length];

            if (!TryCreateReferenceArgument(parameters[0].ParameterType, tableCollection, out args[0]))
                return null;

            if (!TryCreateReferenceArgument(parameters[1].ParameterType, entryKey, out args[1]))
                return null;

            for (int i = 2; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                if (parameter.HasDefaultValue)
                {
                    object defaultValue = parameter.DefaultValue;
                    if (defaultValue == DBNull.Value || defaultValue == Type.Missing)
                    {
                        defaultValue = parameter.ParameterType.IsValueType
                            ? Activator.CreateInstance(parameter.ParameterType)
                            : null;
                    }

                    args[i] = defaultValue;
                    continue;
                }

                args[i] = parameter.ParameterType.IsValueType
                    ? Activator.CreateInstance(parameter.ParameterType)
                    : null;
            }

            return args;
        }
    }
}
