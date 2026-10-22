namespace Arawn.EnemyMasses.Runtime.Factions
{
    /// <summary>
    /// Optional capability for faction providers that support runtime relationship edits.
    /// This keeps relationship-changing APIs decoupled from any concrete provider type.
    /// </summary>
    public interface IFactionRelationshipEditor
    {
        bool SetFactionRelationship(string sourceFactionName, string targetFactionName, RelationshipType relationship, bool bidirectional = false);
        bool SetFactionDefaultRelationship(string factionName, RelationshipType relationship);
        bool SetFactionPlayerRelationship(string factionName, RelationshipType relationship);
        bool SetSameFactionDifferentOwnerRelationship(string factionName, RelationshipType relationship);
        bool RemoveFactionRelationship(string sourceFactionName, string targetFactionName, bool bidirectional = false);
    }

    /// <summary>
    /// Optional settings receiver for Love/Hate providers.
    /// Implement this when a provider needs full settings payload configuration.
    /// </summary>
    public interface ILoveHateFactionSettingsReceiver
    {
        void ApplySettings(LoveHateIntegrationSettings settings);
    }

    /// <summary>
    /// Optional settings receiver for GC2 Factions providers.
    /// Implement this when a provider needs full settings payload configuration.
    /// </summary>
    public interface IGC2FactionSettingsReceiver
    {
        void ApplySettings(GC2FactionIntegrationSettings settings);
    }
}
