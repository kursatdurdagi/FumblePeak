using System;
using UnityEngine;

namespace Arawn.EnemyMasses.Runtime.Skills
{
    /// <summary>
    /// Skill effect that toggles flight mode for agents.
    /// <para>
    /// When <see cref="land"/> is <c>false</c> (default), affected agents enter flight mode —
    /// a take-off animation plays (if configured on the <see cref="EnemyCrowd"/>), followed by
    /// a looping flight clip. Locomotion and combat animations are suppressed while flying.
    /// </para>
    /// <para>
    /// When <see cref="land"/> is <c>true</c>, affected agents exit flight mode —
    /// a landing animation plays (if configured), then normal idle/locomotion resumes.
    /// </para>
    /// <para>
    /// <b>ComputeAnimator</b> workflows use the clip fields on EnemyCrowd:
    /// <c>flightTakeOffClipOverride</c>, <c>flightLoopClipOverride</c>, <c>flightLandClipOverride</c>.
    /// </para>
    /// <para>
    /// <b>MecanimReader / BoneTracker</b> workflows use the parameter fields on EnemyCrowd:
    /// <c>flightBoolParameterOverride</c>, <c>flightTakeOffTriggerParameterOverride</c>,
    /// <c>flightLandTriggerParameterOverride</c>.
    /// </para>
    /// <para>
    /// Target selection:
    /// <list type="bullet">
    /// <item><see cref="TargetMode.Caster"/> — only the caster enters/exits flight mode.</item>
    /// <item><see cref="TargetMode.CasterCrowd"/> — all alive agents of the caster's EnemyCrowd.</item>
    /// <item><see cref="TargetMode.AllTargets"/> — all agents passed in <see cref="SkillExecutionContext.AllTargets"/>.</item>
    /// </list>
    /// </para>
    /// </summary>
    [Serializable]
    public class FlyEffect : SkillEffect
    {
        /// <summary>
        /// Determines which agents are affected by the flight toggle.
        /// </summary>
        public enum TargetMode
        {
            /// <summary>Only the caster agent.</summary>
            Caster,
            /// <summary>All alive agents of the caster's EnemyCrowd.</summary>
            CasterCrowd,
            /// <summary>All agents in <see cref="SkillExecutionContext.AllTargets"/>.</summary>
            AllTargets
        }

        [Header("Flight Settings")]
        [Tooltip("If false, agents take off into flight mode.\n" +
                 "If true, agents land from flight mode.")]
        [SerializeField] private bool land = false;

        [Tooltip("Which agents are affected by the flight toggle.")]
        [SerializeField] private TargetMode targetMode = TargetMode.CasterCrowd;

        [Tooltip("Flight altitude above ground level (world units).\n" +
                 "Set to 0 to use the default height from the EnemyCrowd asset.\n" +
                 "Only used when taking off (land = false).")]
        [Min(0f)]
        [SerializeField] private float flightHeight = 0f;

        /// <summary>Whether this effect lands agents (true) or makes them fly (false).</summary>
        public bool Land => land;

        /// <summary>Which agents are affected.</summary>
        public TargetMode Target => targetMode;

        /// <summary>Flight altitude override. 0 = use EnemyCrowd default.</summary>
        public float FlightHeight => flightHeight;

        public override void Execute(SkillExecutionContext context)
        {
            if (context.Controller == null || context.Caster == null)
                return;

            int affected = 0;

            switch (targetMode)
            {
                case TargetMode.Caster:
                {
                    bool success = land
                        ? context.Controller.SetAgentLanded(context.Caster.index)
                        : context.Controller.SetAgentFlying(context.Caster.index, flightHeight);
                    if (success) affected++;
                    break;
                }

                case TargetMode.CasterCrowd:
                {
                    affected = context.Controller.SetCrowdFlying(
                        context.Caster.enemyCrowdIndex, !land, flightHeight);
                    break;
                }

                case TargetMode.AllTargets:
                {
                    if (context.AllTargets != null)
                    {
                        foreach (var target in context.AllTargets)
                        {
                            if (target == null || target.isDead) continue;

                            bool success = land
                                ? context.Controller.SetAgentLanded(target.index)
                                : context.Controller.SetAgentFlying(target.index, flightHeight);
                            if (success) affected++;
                        }
                    }
                    break;
                }
            }


        }

        public override string GetDescription()
        {
            string action = land ? "Lands" : "Takes off";
            string scope = targetMode switch
            {
                TargetMode.Caster => "the caster",
                TargetMode.CasterCrowd => "all allies in the caster's crowd",
                TargetMode.AllTargets => "all targets",
                _ => "agents"
            };
            string heightInfo = (!land && flightHeight > 0f) ? $" at {flightHeight}m" : "";
            return $"{action} {scope}{heightInfo}";
        }
    }
}
