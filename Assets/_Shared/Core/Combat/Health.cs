using UnityEngine;
using UnityEngine.Events;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Simple hit-point pool for anything that can be shot (players, dummies).
    /// Weapons call TakeDamage on raycast hits.
    /// </summary>
    public class Health : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private bool deactivateOnDeath = true;

        public UnityEvent<float> onDamaged;
        public UnityEvent onDeath;

        public float Current { get; private set; }
        public float Max => maxHealth;
        public bool IsDead => Current <= 0f;

        private void Awake() => Current = maxHealth;

        public void TakeDamage(float amount, Weapon source = null)
        {
            if (IsDead)
                return;

            Current = Mathf.Max(0f, Current - amount);
            onDamaged?.Invoke(amount);
            Debug.Log($"{name} took {amount} damage from {(source != null ? source.WeaponName : "unknown")} -> {Current}/{maxHealth}");

            if (IsDead)
                Die();
        }

        public void ResetHealth()
        {
            Current = maxHealth;
            if (deactivateOnDeath)
                gameObject.SetActive(true);
        }

        private void Die()
        {
            onDeath?.Invoke();
            if (deactivateOnDeath)
                gameObject.SetActive(false);
        }
    }
}
