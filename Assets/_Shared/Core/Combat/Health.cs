using UnityEngine;
using UnityEngine.Events;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Simple hit-point pool for anything that can be shot (players, dummies).
    /// Weapons call TakeDamage on raycast hits.
    /// <para>
    /// OYUNCU CANI SUNUCU-OTORİTERDİR. Ağa bağlı oyuncuların canı
    /// <see cref="ApplyServerHealth"/> ile <c>health_update</c>'ten set edilir;
    /// <see cref="TakeDamage"/> yalnız yerel hedefler (dummy) içindir.
    /// </para>
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

        /// <summary>
        /// Yerel hasar uygulaması. YALNIZ ağa bağlı olmayan hedefler (pratik dummy'leri,
        /// kırılabilir objeler) içindir — oyuncu canı <c>health_update</c>'ten gelir
        /// (<see cref="ApplyServerHealth"/>), ağ oyuncularına asla yerel hasar uygulanmaz.
        /// </summary>
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

        /// <summary>
        /// Sunucudan gelen canı uygular (health_update). Fark pozitifse
        /// <c>onDamaged</c>, can bu güncellemeyle sıfırlandıysa <c>onDeath</c> tetiklenir.
        /// <para>
        /// <c>deactivateOnDeath</c> davranışı UYGULANMAZ: ağ oyuncusunun objesi
        /// kapatılmaz (avatar ölüyken de görünür kalır, yalnız soluklaşır) ve canlanma
        /// bir sonraki health_update ile durum değişimi olarak gelir (§10.4).
        /// </para>
        /// </summary>
        public void ApplyServerHealth(float hp)
        {
            float clamped = Mathf.Clamp(hp, 0f, maxHealth);
            float delta = Current - clamped;
            bool wasDead = IsDead;

            Current = clamped;

            if (delta > 0f)
                onDamaged?.Invoke(delta);

            if (!wasDead && IsDead)
                onDeath?.Invoke();
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
