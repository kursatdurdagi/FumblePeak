using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Factions
{
    /// <summary>
    /// Represents a player in the local multiplayer system.
    /// Each player can own units across multiple factions.
    /// </summary>
    [Serializable]
    public class PlayerInfo
    {
        [Tooltip("Unique identifier for this player. Use PlayerRegistry.GeneratePlayerId() for guaranteed uniqueness.")]
        public string playerId;
        
        [Tooltip("Human-readable display name for this player.")]
        public string displayName;
        
        [Tooltip("Color used to identify this player's units on the minimap (when player color override is enabled).")]
        public Color playerColor = Color.white;
        
        [Tooltip("Optional icon/sprite for this player on the minimap.")]
        public Sprite minimapIcon;
        
        [Tooltip("Is this the local human player? (vs AI-controlled players)")]
        public bool isHumanPlayer = false;
        
        [Tooltip("Is this player currently active in the game?")]
        public bool isActive = true;
        
        [Tooltip("Team index for team-based games. Players on the same team are allies.")]
        public int teamIndex = -1;
        
        /// <summary>
        /// Creates a new player with a generated unique ID.
        /// </summary>
        public PlayerInfo()
        {
            playerId = PlayerRegistry.GeneratePlayerId();
            displayName = "Player";
            playerColor = Color.white;
        }
        
        /// <summary>
        /// Creates a new player with the specified ID and name.
        /// </summary>
        public PlayerInfo(string id, string name)
        {
            playerId = id;
            displayName = name;
            playerColor = Color.white;
        }
        
        /// <summary>
        /// Creates a new player with the specified ID, name, and color.
        /// </summary>
        public PlayerInfo(string id, string name, Color color)
        {
            playerId = id;
            displayName = name;
            playerColor = color;
        }
    }
    
    /// <summary>
    /// Registry for managing players in local multiplayer scenarios.
    /// Tracks player IDs, colors, teams, and provides query methods for player-owned units.
    /// </summary>
    public static class PlayerRegistry
    {
        #region Private Fields
        
        private static readonly Dictionary<string, PlayerInfo> _players = new();
        private static readonly List<string> _playerOrder = new(); // Maintains registration order
        private static string _localPlayerId = null;
        
        // Pre-defined colors for auto-assignment
        private static readonly Color[] DefaultPlayerColors = new Color[]
        {
            new Color(0.2f, 0.6f, 1.0f, 1f),   // Blue
            new Color(1.0f, 0.3f, 0.3f, 1f),   // Red
            new Color(0.3f, 1.0f, 0.3f, 1f),   // Green
            new Color(1.0f, 1.0f, 0.3f, 1f),   // Yellow
            new Color(1.0f, 0.5f, 0.0f, 1f),   // Orange
            new Color(0.8f, 0.3f, 1.0f, 1f),   // Purple
            new Color(0.0f, 1.0f, 1.0f, 1f),   // Cyan
            new Color(1.0f, 0.5f, 0.8f, 1f),   // Pink
        };
        
        private static int _nextColorIndex = 0;
        
        #endregion
        
        #region Events
        
        /// <summary>
        /// Fired when a player is registered.
        /// </summary>
        public static event Action<PlayerInfo> OnPlayerRegistered;
        
        /// <summary>
        /// Fired when a player is unregistered.
        /// </summary>
        public static event Action<string> OnPlayerUnregistered;
        
        /// <summary>
        /// Fired when a player's info is updated.
        /// </summary>
        public static event Action<PlayerInfo> OnPlayerUpdated;
        
        #endregion
        
        #region Player ID Generation
        
        /// <summary>
        /// Generates a unique player ID using GUID.
        /// Format: "player_xxxxxxxx" where x is alphanumeric.
        /// </summary>
        public static string GeneratePlayerId()
        {
            return "player_" + Guid.NewGuid().ToString("N").Substring(0, 16);
        }
        
        /// <summary>
        /// Generates a player ID with a custom prefix.
        /// Format: "{prefix}_xxxxxxxx"
        /// </summary>
        public static string GeneratePlayerId(string prefix)
        {
            return prefix + "_" + Guid.NewGuid().ToString("N").Substring(0, 16);
        }
        
        /// <summary>
        /// Generates a simple numeric player ID.
        /// Format: "player_N" where N is the next available number.
        /// </summary>
        public static string GenerateSimplePlayerId()
        {
            int index = _players.Count + 1;
            string id = $"player_{index}";
            
            // Ensure uniqueness
            while (_players.ContainsKey(id))
            {
                index++;
                id = $"player_{index}";
            }
            
            return id;
        }
        
        #endregion
        
        #region Player Registration
        
        /// <summary>
        /// Registers a new player with an auto-generated ID and name.
        /// </summary>
        /// <returns>The registered PlayerInfo</returns>
        public static PlayerInfo RegisterPlayer()
        {
            var player = new PlayerInfo
            {
                playerId = GenerateSimplePlayerId(),
                displayName = $"Player {_players.Count + 1}",
                playerColor = GetNextDefaultColor()
            };
            
            RegisterPlayer(player);
            return player;
        }
        
        /// <summary>
        /// Registers a new player with the specified name.
        /// </summary>
        public static PlayerInfo RegisterPlayer(string displayName)
        {
            var player = new PlayerInfo
            {
                playerId = GeneratePlayerId(),
                displayName = displayName,
                playerColor = GetNextDefaultColor()
            };
            
            RegisterPlayer(player);
            return player;
        }
        
        /// <summary>
        /// Registers a new player with the specified name and color.
        /// </summary>
        public static PlayerInfo RegisterPlayer(string displayName, Color color)
        {
            var player = new PlayerInfo
            {
                playerId = GeneratePlayerId(),
                displayName = displayName,
                playerColor = color
            };
            
            RegisterPlayer(player);
            return player;
        }
        
        /// <summary>
        /// Registers a player with full control over the PlayerInfo.
        /// </summary>
        public static void RegisterPlayer(PlayerInfo player)
        {
            if (player == null)
            {
                Debug.LogError("[PlayerRegistry] Cannot register null player.");
                return;
            }
            
            if (string.IsNullOrEmpty(player.playerId))
            {
                player.playerId = GeneratePlayerId();
            }
            
            if (_players.ContainsKey(player.playerId))
            {
                Debug.LogWarning($"[PlayerRegistry] Player '{player.playerId}' already registered. Updating info.");
                _players[player.playerId] = player;
                OnPlayerUpdated?.Invoke(player);
                return;
            }
            
            _players[player.playerId] = player;
            _playerOrder.Add(player.playerId);
            
            Debug.Log($"[PlayerRegistry] Registered player: {player.displayName} (ID: {player.playerId})");
            OnPlayerRegistered?.Invoke(player);
        }
        
        /// <summary>
        /// Unregisters a player by ID.
        /// </summary>
        public static bool UnregisterPlayer(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return false;
            
            if (_players.Remove(playerId))
            {
                _playerOrder.Remove(playerId);
                
                if (_localPlayerId == playerId)
                    _localPlayerId = null;
                
                Debug.Log($"[PlayerRegistry] Unregistered player: {playerId}");
                OnPlayerUnregistered?.Invoke(playerId);
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Clears all registered players.
        /// </summary>
        public static void ClearAllPlayers()
        {
            var playerIds = new List<string>(_playerOrder);
            foreach (var id in playerIds)
            {
                UnregisterPlayer(id);
            }
            
            _players.Clear();
            _playerOrder.Clear();
            _localPlayerId = null;
            _nextColorIndex = 0;
            
            Debug.Log("[PlayerRegistry] Cleared all players.");
        }
        
        #endregion
        
        #region Player Queries
        
        /// <summary>
        /// Gets a player by ID.
        /// </summary>
        public static PlayerInfo GetPlayer(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return null;
            _players.TryGetValue(playerId, out var player);
            return player;
        }
        
        /// <summary>
        /// Checks if a player ID exists.
        /// </summary>
        public static bool HasPlayer(string playerId)
        {
            return !string.IsNullOrEmpty(playerId) && _players.ContainsKey(playerId);
        }
        
        /// <summary>
        /// Gets all registered players.
        /// </summary>
        public static IEnumerable<PlayerInfo> GetAllPlayers()
        {
            foreach (var id in _playerOrder)
            {
                if (_players.TryGetValue(id, out var player))
                    yield return player;
            }
        }
        
        /// <summary>
        /// Gets all active players.
        /// </summary>
        public static IEnumerable<PlayerInfo> GetActivePlayers()
        {
            foreach (var id in _playerOrder)
            {
                if (_players.TryGetValue(id, out var player) && player.isActive)
                    yield return player;
            }
        }
        
        /// <summary>
        /// Gets all human players.
        /// </summary>
        public static IEnumerable<PlayerInfo> GetHumanPlayers()
        {
            foreach (var id in _playerOrder)
            {
                if (_players.TryGetValue(id, out var player) && player.isHumanPlayer)
                    yield return player;
            }
        }
        
        /// <summary>
        /// Gets all AI-controlled players.
        /// </summary>
        public static IEnumerable<PlayerInfo> GetAIPlayers()
        {
            foreach (var id in _playerOrder)
            {
                if (_players.TryGetValue(id, out var player) && !player.isHumanPlayer)
                    yield return player;
            }
        }
        
        /// <summary>
        /// Gets all players on a specific team.
        /// </summary>
        public static IEnumerable<PlayerInfo> GetPlayersOnTeam(int teamIndex)
        {
            foreach (var id in _playerOrder)
            {
                if (_players.TryGetValue(id, out var player) && player.teamIndex == teamIndex)
                    yield return player;
            }
        }
        
        /// <summary>
        /// Gets the number of registered players.
        /// </summary>
        public static int PlayerCount => _players.Count;
        
        /// <summary>
        /// Gets the player at the specified index (in registration order).
        /// </summary>
        public static PlayerInfo GetPlayerByIndex(int index)
        {
            if (index < 0 || index >= _playerOrder.Count) return null;
            return GetPlayer(_playerOrder[index]);
        }
        
        /// <summary>
        /// Gets the index of a player (in registration order).
        /// </summary>
        public static int GetPlayerIndex(string playerId)
        {
            return _playerOrder.IndexOf(playerId);
        }
        
        #endregion
        
        #region Local Player
        
        /// <summary>
        /// Sets the local human player ID.
        /// </summary>
        public static void SetLocalPlayer(string playerId)
        {
            if (!HasPlayer(playerId))
            {
                Debug.LogWarning($"[PlayerRegistry] Cannot set local player to unregistered ID: {playerId}");
                return;
            }
            
            _localPlayerId = playerId;
            
            // Mark as human player
            if (_players.TryGetValue(playerId, out var player))
            {
                player.isHumanPlayer = true;
            }
        }
        
        /// <summary>
        /// Gets the local human player.
        /// </summary>
        public static PlayerInfo LocalPlayer => GetPlayer(_localPlayerId);
        
        /// <summary>
        /// Gets the local player ID.
        /// </summary>
        public static string LocalPlayerId => _localPlayerId;
        
        /// <summary>
        /// Checks if the given player ID is the local player.
        /// </summary>
        public static bool IsLocalPlayer(string playerId)
        {
            return !string.IsNullOrEmpty(_localPlayerId) && _localPlayerId == playerId;
        }
        
        #endregion
        
        #region Team/Alliance Queries
        
        /// <summary>
        /// Checks if two players are on the same team (allies).
        /// Players with teamIndex -1 are not on any team.
        /// </summary>
        public static bool AreAllies(string playerId1, string playerId2)
        {
            if (playerId1 == playerId2) return true;
            
            var p1 = GetPlayer(playerId1);
            var p2 = GetPlayer(playerId2);
            
            if (p1 == null || p2 == null) return false;
            if (p1.teamIndex < 0 || p2.teamIndex < 0) return false;
            
            return p1.teamIndex == p2.teamIndex;
        }
        
        /// <summary>
        /// Checks if two players are enemies (not allies and both active).
        /// </summary>
        public static bool AreEnemies(string playerId1, string playerId2)
        {
            if (playerId1 == playerId2) return false;
            
            var p1 = GetPlayer(playerId1);
            var p2 = GetPlayer(playerId2);
            
            if (p1 == null || p2 == null) return false;
            if (!p1.isActive || !p2.isActive) return false;
            
            return !AreAllies(playerId1, playerId2);
        }
        
        #endregion
        
        #region Color Management
        
        /// <summary>
        /// Gets the color for a player ID.
        /// Returns white if player not found.
        /// </summary>
        public static Color GetPlayerColor(string playerId)
        {
            var player = GetPlayer(playerId);
            return player?.playerColor ?? Color.white;
        }
        
        /// <summary>
        /// Sets the color for a player.
        /// </summary>
        public static void SetPlayerColor(string playerId, Color color)
        {
            var player = GetPlayer(playerId);
            if (player != null)
            {
                player.playerColor = color;
                OnPlayerUpdated?.Invoke(player);
            }
        }
        
        /// <summary>
        /// Gets the next default color from the predefined palette.
        /// </summary>
        public static Color GetNextDefaultColor()
        {
            var color = DefaultPlayerColors[_nextColorIndex % DefaultPlayerColors.Length];
            _nextColorIndex++;
            return color;
        }
        
        /// <summary>
        /// Resets the color assignment counter.
        /// </summary>
        public static void ResetColorAssignment()
        {
            _nextColorIndex = 0;
        }
        
        #endregion
        
        #region Unit Queries (requires CrowdController)
        
        /// <summary>
        /// Gets the integer owner ID for a player string ID.
        /// This is the value stored in CrowdAgent.ownerPlayerId.
        /// Returns -1 if player not found.
        /// </summary>
        public static int GetOwnerIdForPlayer(string playerId)
        {
            return GetPlayerIndex(playerId);
        }
        
        /// <summary>
        /// Gets the string player ID for an integer owner ID.
        /// Returns null if not found.
        /// </summary>
        public static string GetPlayerIdForOwnerId(int ownerId)
        {
            var player = GetPlayerByIndex(ownerId);
            return player?.playerId;
        }
        
        /// <summary>
        /// Gets all agents owned by a specific player (by string ID) across all controllers.
        /// </summary>
        public static List<(EnemyMassesCrowdController controller, int index)> GetAgentsByPlayer(string playerId)
        {
            var result = new List<(EnemyMassesCrowdController, int)>();
            
            int ownerId = GetOwnerIdForPlayer(playerId);
            if (ownerId < 0) return result;
            
            return GetAgentsByOwnerId(ownerId);
        }
        
        /// <summary>
        /// Gets all agents owned by a specific owner ID (integer index) across all controllers.
        /// </summary>
        public static List<(EnemyMassesCrowdController controller, int index)> GetAgentsByOwnerId(int ownerId)
        {
            var result = new List<(EnemyMassesCrowdController, int)>();
            
            if (ownerId < 0) return result;
            
            // Find all crowd controllers
            var controllers = UnityEngine.Object.FindObjectsByType<EnemyMassesCrowdController>(FindObjectsSortMode.None);
            
            foreach (var controller in controllers)
            {
                if (controller == null) continue;
                
                int agentCount = controller.GetAgentCount();
                for (int i = 0; i < agentCount; i++)
                {
                    var agent = controller.GetAgent(i);
                    if (agent != null && agent.ownerPlayerId == ownerId)
                    {
                        result.Add((controller, i));
                    }
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Gets the count of agents owned by a specific player (by string ID).
        /// </summary>
        public static int GetAgentCountByPlayer(string playerId)
        {
            int ownerId = GetOwnerIdForPlayer(playerId);
            if (ownerId < 0) return 0;
            
            return GetAgentCountByOwnerId(ownerId);
        }
        
        /// <summary>
        /// Gets the count of agents owned by a specific owner ID.
        /// </summary>
        public static int GetAgentCountByOwnerId(int ownerId)
        {
            if (ownerId < 0) return 0;
            
            int count = 0;
            var controllers = UnityEngine.Object.FindObjectsByType<EnemyMassesCrowdController>(FindObjectsSortMode.None);
            
            foreach (var controller in controllers)
            {
                if (controller == null) continue;
                
                int agentCount = controller.GetAgentCount();
                for (int i = 0; i < agentCount; i++)
                {
                    var agent = controller.GetAgent(i);
                    if (agent != null && agent.ownerPlayerId == ownerId)
                    {
                        count++;
                    }
                }
            }
            
            return count;
        }
        
        /// <summary>
        /// Gets all alive agents owned by a specific player (by string ID).
        /// </summary>
        public static List<(EnemyMassesCrowdController controller, int index)> GetAliveAgentsByPlayer(string playerId)
        {
            int ownerId = GetOwnerIdForPlayer(playerId);
            if (ownerId < 0) return new List<(EnemyMassesCrowdController, int)>();
            
            return GetAliveAgentsByOwnerId(ownerId);
        }
        
        /// <summary>
        /// Gets all alive agents owned by a specific owner ID.
        /// </summary>
        public static List<(EnemyMassesCrowdController controller, int index)> GetAliveAgentsByOwnerId(int ownerId)
        {
            var result = new List<(EnemyMassesCrowdController, int)>();
            
            if (ownerId < 0) return result;
            
            var controllers = UnityEngine.Object.FindObjectsByType<EnemyMassesCrowdController>(FindObjectsSortMode.None);
            
            foreach (var controller in controllers)
            {
                if (controller == null) continue;
                
                int agentCount = controller.GetAgentCount();
                for (int i = 0; i < agentCount; i++)
                {
                    var agent = controller.GetAgent(i);
                    if (agent != null && agent.ownerPlayerId == ownerId && !agent.isDead)
                    {
                        result.Add((controller, i));
                    }
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Gets all agents owned by a specific player (by string ID) in a specific faction.
        /// </summary>
        public static List<(EnemyMassesCrowdController controller, int index)> GetAgentsByPlayerAndFaction(
            string playerId, string factionName)
        {
            int ownerId = GetOwnerIdForPlayer(playerId);
            if (ownerId < 0) return new List<(EnemyMassesCrowdController, int)>();
            
            return GetAgentsByOwnerIdAndFaction(ownerId, factionName);
        }
        
        /// <summary>
        /// Gets all agents owned by a specific owner ID in a specific faction.
        /// </summary>
        public static List<(EnemyMassesCrowdController controller, int index)> GetAgentsByOwnerIdAndFaction(
            int ownerId, string factionName)
        {
            var result = new List<(EnemyMassesCrowdController, int)>();
            
            if (ownerId < 0) return result;
            
            var controllers = UnityEngine.Object.FindObjectsByType<EnemyMassesCrowdController>(FindObjectsSortMode.None);
            
            foreach (var controller in controllers)
            {
                if (controller == null) continue;
                
                int agentCount = controller.GetAgentCount();
                for (int i = 0; i < agentCount; i++)
                {
                    var agent = controller.GetAgent(i);
                    if (agent == null || agent.ownerPlayerId != ownerId) continue;
                    
                    // Check faction name if specified
                    if (!string.IsNullOrEmpty(factionName))
                    {
                        var crowd = agent.enemyCrowd;
                        if (crowd == null || crowd.factionName != factionName) continue;
                    }
                    
                    result.Add((controller, i));
                }
            }
            
            return result;
        }
        
        #endregion
    }
}
