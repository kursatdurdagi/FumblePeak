using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Represents a selection of factions, similar to LayerMask but for faction names.
    /// Stores selected faction names as a list of strings.
    /// </summary>
    [Serializable]
    public class FactionMask
    {
        [SerializeField]
        private List<string> selectedFactions = new List<string>();
        
        /// <summary>
        /// Gets the list of selected faction names
        /// </summary>
        public List<string> SelectedFactions => selectedFactions;
        
        /// <summary>
        /// Returns true if the specified faction is selected in this mask
        /// </summary>
        public bool ContainsFaction(string factionName)
        {
            if (string.IsNullOrEmpty(factionName)) return false;
            return selectedFactions.Contains(factionName);
        }
        
        /// <summary>
        /// Returns true if any factions are selected
        /// </summary>
        public bool HasAnyFaction => selectedFactions.Count > 0;
        
        /// <summary>
        /// Add a faction to the selection
        /// </summary>
        public void AddFaction(string factionName)
        {
            if (!string.IsNullOrEmpty(factionName) && !selectedFactions.Contains(factionName))
            {
                selectedFactions.Add(factionName);
            }
        }
        
        /// <summary>
        /// Remove a faction from the selection
        /// </summary>
        public void RemoveFaction(string factionName)
        {
            selectedFactions.Remove(factionName);
        }
        
        /// <summary>
        /// Clear all selected factions
        /// </summary>
        public void Clear()
        {
            selectedFactions.Clear();
        }
        
        /// <summary>
        /// Set the selected factions from a list
        /// </summary>
        public void SetFactions(IEnumerable<string> factions)
        {
            selectedFactions.Clear();
            if (factions != null)
            {
                foreach (var faction in factions)
                {
                    if (!string.IsNullOrEmpty(faction))
                    {
                        selectedFactions.Add(faction);
                    }
                }
            }
        }
        
        /// <summary>
        /// Gets the number of selected factions
        /// </summary>
        public int Count => selectedFactions.Count;
        
        public override string ToString()
        {
            if (selectedFactions.Count == 0) return "None";
            if (selectedFactions.Count == 1) return selectedFactions[0];
            return $"{selectedFactions.Count} factions";
        }
    }
}
