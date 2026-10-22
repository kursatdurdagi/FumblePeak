using UnityEngine;
using UnityEngine.Events;
using Arawn.EnemyMasses.Runtime;

namespace Arawn.EnemyMasses.Runtime.Damageables
{
    /// <summary>
    /// Simple health component for props/objects (doors, switches, objectives) so Enemy Masses agents can damage them.
    /// </summary>
    [AddComponentMenu("Enemy Masses/Damageable Object")]
    public class EnemyMassesDamageableObject : MonoBehaviour, IEnemyMassesDamageable
    {
        [Header("Health")]
        [SerializeField] private float maxHealth = 200f;
        [SerializeField] private bool destroyOnDeath = false;
        [SerializeField] private bool disableCollidersOnDeath = true;
        [SerializeField] private bool disableRenderersOnDeath = false;
        [SerializeField] private GameObject[] deactivateOnDeath;

        [Header("Events")]
        [SerializeField] private UnityEvent<float, float> onDamaged; // current, max
        [SerializeField] private UnityEvent onDeath;

        public float CurrentHealth { get; private set; }
        public bool IsDead { get; private set; }

        public Transform DamageTransform => this.transform;
        public bool IsAlive => !IsDead;

        private Collider[] _colliders;
        private Renderer[] _renderers;

        private void Awake()
        {
            CurrentHealth = Mathf.Max(1f, maxHealth);
            _colliders = GetComponentsInChildren<Collider>(includeInactive: true);
            _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        }

        public void ApplyDamage(float amount, CrowdAgent attacker, Vector3 hitPoint)
        {
            if (IsDead) return;
            if (amount <= 0f) return;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - amount);
            onDamaged?.Invoke(CurrentHealth, maxHealth);

            if (CurrentHealth <= 0f)
            {
                HandleDeath();
            }
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            if (amount <= 0f) return;
            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            onDamaged?.Invoke(CurrentHealth, maxHealth);
        }

        private void HandleDeath()
        {
            if (IsDead) return;
            IsDead = true;

            onDeath?.Invoke();

            if (deactivateOnDeath != null)
            {
                for (int i = 0; i < deactivateOnDeath.Length; i++)
                {
                    if (deactivateOnDeath[i] != null)
                    {
                        deactivateOnDeath[i].SetActive(false);
                    }
                }
            }

            if (disableCollidersOnDeath && _colliders != null)
            {
                foreach (var col in _colliders)
                {
                    if (col != null) col.enabled = false;
                }
            }

            if (disableRenderersOnDeath && _renderers != null)
            {
                foreach (var rend in _renderers)
                {
                    if (rend != null) rend.enabled = false;
                }
            }

            if (destroyOnDeath)
            {
                Destroy(gameObject);
            }
        }
    }
}
