using System;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// A no-op action that displays a custom comment in the skill bar tooltip.
    /// Place it in any phase (On Cast, On Hit, etc.) and its text will appear
    /// under that phase header when the player hovers over the skill icon.
    /// </summary>
    [Serializable]
    public class TooltipCommentAction : SkillAction
    {
        [Tooltip("The text to display in the skill tooltip under the phase this action belongs to.")]
        [TextArea(1, 4)]
        [SerializeField] private string comment = "";

        // Hide base-class fields that are irrelevant for a cosmetic-only action
#pragma warning disable CS0414 // Field is assigned but never used
        [HideInInspector] [SerializeField] new private float delay = 0f;
        [HideInInspector] [SerializeField] new private bool waitForCompletionBeforeNextAction = false;
#pragma warning restore CS0414

        public string Comment => comment;

        public override void Execute(SkillExecutionContext context)
        {
            // Purely cosmetic — nothing to execute at runtime.
        }

        public override string GetDescription()
        {
            return string.IsNullOrWhiteSpace(comment) ? "(empty comment)" : comment;
        }

        public override bool RequiresServerAuthority => false;
    }
}
