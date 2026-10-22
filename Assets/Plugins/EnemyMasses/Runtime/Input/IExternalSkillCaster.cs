using System.Collections.Generic;
using UnityEngine;
using Arawn.EnemyMasses.Runtime.Damageables;
using Arawn.EnemyMasses.Runtime.Skills;

namespace Arawn.EnemyMasses.Runtime
{
    /// <summary>
    /// Extends <see cref="IRTSExternalSelectable"/> to provide skill-bar and portrait-bar
    /// integration for standalone GameObjects that are not part of an EnemyCrowd.
    /// <para>
    /// Implement this interface (or use the built-in <see cref="StandaloneSkillCaster"/>)
    /// to allow a player-controlled unit to:
    /// <list type="bullet">
    ///   <item>Show its skills in the <c>AgentSkillBarUI</c> when selected</item>
    ///   <item>Appear in the <c>EnemyCrowdPortraitBarUI</c> with a faction name, portrait and health bar</item>
    ///   <item>Execute <see cref="AgentSkill"/> effects at runtime via the normal skill pipeline</item>
    /// </list>
    /// </para>
    /// </summary>
    public interface IExternalSkillCaster : IRTSExternalSelectable
    {
        #region Portrait Bar Data

        /// <summary>Displayed faction name (used by portrait bar tooltip).</summary>
        string FactionName { get; }

        /// <summary>Displayed unit/type name (used by portrait bar label).</summary>
        string EnemyTypeName { get; }

        /// <summary>Portrait texture shown in the portrait bar. Null = uses <see cref="FactionColor"/> as fallback.</summary>
        Texture2D Portrait { get; }

        /// <summary>Fallback color when no portrait texture is assigned.</summary>
        Color FactionColor { get; }

        /// <summary>Whether the portrait bar should show this caster. Return false to hide even when registered.</summary>
        bool ShowInPortraitBar { get; }

        #endregion

        #region Skill Data

        /// <summary>The skill system config that drives the skill bar. Must have <c>enableSkillSystem = true</c>.</summary>
        SkillSystemConfig SkillConfig { get; }

        /// <summary>Shortcut to <c>SkillConfig.skillSlots</c>.</summary>
        IReadOnlyList<AgentSkillSlot> SkillSlots { get; }

        #endregion

        #region Cooldown

        /// <summary>Remaining cooldown seconds for the given slot index.</summary>
        float GetCooldownRemaining(int slotIndex);

        /// <summary>Total cooldown duration for the given slot index.</summary>
        float GetCooldownTotal(int slotIndex);

        /// <summary>True if every slot the UI might query is on cooldown (used by the "error buzz" guard).</summary>
        bool IsSkillOnCooldown(int slotIndex);

        #endregion

        #region Execution

        /// <summary>
        /// Attempt to execute the skill at <paramref name="slotIndex"/> immediately.
        /// Called by the skill bar when the player clicks or presses a keybind.
        /// </summary>
        /// <param name="slotIndex">Index into <see cref="SkillSlots"/>.</param>
        /// <param name="targetAgentIndex">Crowd agent index of the explicit target, or -1 for auto/ground.</param>
        /// <param name="groundTarget">World position for ground-targeted skills, or null.</param>
        /// <param name="controller">The nearby <see cref="EnemyMassesCrowdController"/> (for target resolution and damage).</param>
        /// <param name="damageableTarget">Optional non-agent damageable target, or null.</param>
        /// <returns>True if the skill was successfully fired.</returns>
        bool TryUseSkill(int slotIndex, int targetAgentIndex, Vector3? groundTarget,
            EnemyMassesCrowdController controller, IEnemyMassesDamageable damageableTarget = null);

        #endregion

        #region Live State (portrait bar)

        /// <summary>Normalized health (0–1) shown on the portrait bar health fill.</summary>
        float HealthPercent { get; }

        /// <summary>True if the unit took damage recently (drives "under attack" highlight).</summary>
        bool IsUnderAttack { get; }

        /// <summary>Window in seconds used by <see cref="IsUnderAttack"/>.</summary>
        float UnderAttackWindow { get; }

        #endregion
    }
}
