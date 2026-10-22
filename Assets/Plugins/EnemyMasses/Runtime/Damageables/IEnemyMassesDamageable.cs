using UnityEngine;
using Arawn.EnemyMasses.Runtime;

namespace Arawn.EnemyMasses.Runtime.Damageables
{
    /// <summary>
    /// Interface for non-agent, damageable world objects (doors, props, objectives).
    /// Implement to allow Enemy Masses agents to attack and damage the object.
    /// </summary>
    public interface IEnemyMassesDamageable
    {
        /// <summary>Transform used as the attack target.</summary>
        Transform DamageTransform { get; }
        /// <summary>True while the object is alive/active and should receive damage.</summary>
        bool IsAlive { get; }
        /// <summary>Apply damage from an Enemy Masses attacker.</summary>
        /// <param name="amount">Damage amount after all multipliers.</param>
        /// <param name="attacker">The attacking agent (may be null).</param>
        /// <param name="hitPoint">World-space hit position (approximate).</param>
        void ApplyDamage(float amount, CrowdAgent attacker, Vector3 hitPoint);
    }
}
