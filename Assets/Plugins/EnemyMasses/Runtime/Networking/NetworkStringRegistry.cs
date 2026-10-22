using System.Collections.Generic;

namespace Arawn.EnemyMasses.Runtime.Networking
{
    /// <summary>
    /// Bidirectional registry that maps string identifiers (skill IDs, resource names, stat names)
    /// to compact integer keys for network serialization and fast comparison.
    ///
    /// <para>
    /// <b>Why:</b> String comparison is O(length) and allocates on serialization.
    /// Integer comparison is O(1) and allocation-free. In a "masses of enemies" context
    /// where thousands of agents sync state every tick, this eliminates a major source
    /// of GC pressure and CPU cost.
    /// </para>
    ///
    /// <para>
    /// <b>Usage:</b> Call <see cref="GetOrRegister"/> once per unique string
    /// (typically at initialization or first encounter). Then use the returned key
    /// in all hot-path comparisons and network struct fields.
    /// </para>
    ///
    /// <para>
    /// <b>Determinism:</b> Keys are assigned in registration order. For client/server
    /// agreement, either:
    /// <list type="bullet">
    ///   <item>Register all strings in a deterministic order at startup (asset database, sorted).</item>
    ///   <item>Or send the registry from server → clients at session start.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class NetworkStringRegistry
    {
        /// <summary>Key value indicating "not registered" or "unknown".</summary>
        public const int UNREGISTERED = 0;

        private static readonly Dictionary<string, int> _stringToKey = new(128);
        private static readonly Dictionary<int, string> _keyToString = new(128);
        private static int _nextKey = 1; // 0 is reserved for UNREGISTERED

        // ─── Registration ────────────────────────────────────────────────

        /// <summary>
        /// Returns the integer key for <paramref name="value"/>.
        /// If the string has not been registered yet, a new key is assigned.
        /// Thread-safe is NOT guaranteed — call from the main thread only. 
        /// </summary>
        /// <param name="value">The string to register (skill ID, resource name, stat name).</param>
        /// <returns>A positive integer key (&gt; 0), or <see cref="UNREGISTERED"/> if
        /// <paramref name="value"/> is null or empty.</returns>
        public static int GetOrRegister(string value)
        {
            if (string.IsNullOrEmpty(value))
                return UNREGISTERED;

            if (_stringToKey.TryGetValue(value, out int key))
                return key;

            key = _nextKey++;
            _stringToKey[value] = key;
            _keyToString[key] = value;
            return key;
        }

        /// <summary>
        /// Looks up the key for a string without registering it.
        /// Returns <see cref="UNREGISTERED"/> if not found.
        /// </summary>
        public static int TryGetKey(string value)
        {
            if (string.IsNullOrEmpty(value))
                return UNREGISTERED;

            return _stringToKey.TryGetValue(value, out int key) ? key : UNREGISTERED;
        }

        /// <summary>
        /// Resolves a key back to its original string.
        /// Returns null if the key is unregistered.
        /// </summary>
        public static string Resolve(int key)
        {
            if (key <= UNREGISTERED)
                return null;

            return _keyToString.TryGetValue(key, out string value) ? value : null;
        }

        /// <summary>
        /// Returns the total number of registered entries.
        /// </summary>
        public static int Count => _stringToKey.Count;

        // ─── Lifecycle ───────────────────────────────────────────────────

        /// <summary>
        /// Clears the entire registry. Call between sessions if skill/resource sets change,
        /// or during application shutdown. Both client and server must clear together
        /// (or re-sync the registry on the next session).
        /// </summary>
        public static void Clear()
        {
            _stringToKey.Clear();
            _keyToString.Clear();
            _nextKey = 1;
        }

        // ─── Serialization helpers ───────────────────────────────────────

        /// <summary>
        /// Exports the current registry as ordered (key, value) pairs.
        /// Use this to serialize the server's registry and send to clients at session start.
        /// </summary>
        public static KeyValuePair<int, string>[] Export()
        {
            var entries = new KeyValuePair<int, string>[_keyToString.Count];
            int i = 0;
            foreach (var kvp in _keyToString)
                entries[i++] = kvp;
            return entries;
        }

        /// <summary>
        /// Imports a registry received from the server.
        /// Clears the local registry first.
        /// </summary>
        public static void Import(KeyValuePair<int, string>[] entries)
        {
            Clear();
            if (entries == null) return;

            foreach (var kvp in entries)
            {
                _stringToKey[kvp.Value] = kvp.Key;
                _keyToString[kvp.Key] = kvp.Value;
                if (kvp.Key >= _nextKey)
                    _nextKey = kvp.Key + 1;
            }
        }
    }
}
